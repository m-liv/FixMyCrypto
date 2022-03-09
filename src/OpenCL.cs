using NBitcoin;
using OpenCl.DotNetCore;
using OpenCl.DotNetCore.CommandQueues;
using OpenCl.DotNetCore.Contexts;
using OpenCl.DotNetCore.Devices;
using OpenCl.DotNetCore.Kernels;
using OpenCl.DotNetCore.Memory;
using OpenCl.DotNetCore.Platforms;
using OpenCl.DotNetCore.Programs;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ConsoleTables;

namespace FixMyCrypto {

    class OpenCL {

        private Context context;
        private Program program_pbkdf2;

        private Program program_bip32derive;

        private CommandQueue commandQueue;
        private Dictionary<string, Kernel> kernels = new();

        private bool program_pbkdf2_ready = false;

        private bool program_bip32derive_ready = false;
        private int usingDkLen;

        private int saltBufferSize;     //  max char length of a passphrase

        private int inBufferSize = 256; //  max char length of a phrase

        private int pwdBufferSize;

        private int outBufferSize;

        private int wordSize;

        private Device chosenDevice;


        // private CommandQueue commandQueue;

        public OpenCL(int platformId = 0, int deviceId = 0, int maxPassphraseLength = 32) {
            LogOpenCLInfo();
            if (platformId < 0 || deviceId < 0) throw new ArgumentException();

            IEnumerable<Platform> platforms = Platform.GetPlatforms();
            chosenDevice = platforms.ToList()[platformId].GetDevices(DeviceType.All).ToList()[deviceId];
            Log.Info($"Selected device ({platformId}, {deviceId}): {chosenDevice.Name} ({chosenDevice.Vendor})");
            context = Context.CreateContext(chosenDevice);            
            commandQueue = CommandQueue.CreateCommandQueue(context, chosenDevice);
            saltBufferSize = 8 + maxPassphraseLength;   //  "mnemonic" + passphrase
        }

        ~OpenCL() {
            program_pbkdf2?.Dispose();
            program_bip32derive?.Dispose();
            foreach (Kernel k in kernels.Values) k?.Dispose();
            commandQueue?.Dispose();
            context?.Dispose();
            chosenDevice?.Dispose();
        }

        public void Init_Sha512(int dklen = 64) {
            if (program_pbkdf2_ready && dklen == usingDkLen) return;

            program_pbkdf2_ready = false;
            outBufferSize = (dklen > 64) ? 128 : 64;
            wordSize = (dklen > 32) ? 8 : 4;
            pwdBufferSize = inBufferSize;

            // Creates a program and then the kernel from it
            
            string code = OpenCL_Bufferstructs.buffer_structs_template_cl + OpenCL_Sha512.hmac512_cl + OpenCL_Pbkdf2.pbkdf2_cl + OpenCL_Pbkdf2.pbkdf2_variants;
            // string code = Bip39_Solver_Sha.sha_cl + Bip39_Solver.int_to_address_cl;
            
            code = code.Replace("<hashBlockSize_bits>", "1024");
            code = code.Replace("<hashDigestSize_bits>", "512");
            code = code.Replace("<inBufferSize_bytes>", $"{inBufferSize}");
            code = code.Replace("<outBufferSize_bytes>", $"{outBufferSize}");
            code = code.Replace("<saltBufferSize_bytes>", $"{saltBufferSize}");
            code = code.Replace("<pwdBufferSize_bytes>", $"{pwdBufferSize}");
            code = code.Replace("<ctBufferSize_bytes>", $"{saltBufferSize}");
            code = code.Replace("<word_size>", $"{wordSize}");

            Log.Info("Compiling OpenCL PBKDF2 scripts...");
            program_pbkdf2?.Dispose();
            program_pbkdf2 = context.CreateAndBuildProgramFromString(code);
            Log.Info("OpenCL Compiled");
            program_pbkdf2_ready = true;
            usingDkLen = dklen;
        }

        public int GetBatchSize() {
            return 8192;
            /*
            int memoryPerWork = (wordSize + inBufferSize) + (wordSize + saltBufferSize) + outBufferSize;
            int workgroupPerCore = (int)(chosenDevice.LocalMemorySize / memoryPerWork);
            int size = workgroupPerCore * chosenDevice.MaximumComputeUnits;
            Log.Debug($"Workgroup size: {size}");
            return size;
            */
        }

        public Seed[] Pbkdf2_Sha512_MultiPassword(Phrase[] phrases, string[] passphrases, byte[][] passwords, byte[] salt, int iters = 2048, int dklen = 64) {
            Init_Sha512(dklen);

            // Log.Debug($"password batch size={passwords.Length}");

            byte[] data = new byte[passwords.Length * (wordSize + inBufferSize)];
            BinaryWriter w = new(new MemoryStream(data));
            for (int i = 0; i < passwords.Length; i++) {
                byte[] pb = passwords[i];
                if (pb.Length > inBufferSize) {
                        throw new Exception("phrase exceeds max length");
                }
                w.Write((ulong)pb.Length);
                w.Write(pb);
                w.Write(new byte[inBufferSize - pb.Length]);
            }
            w.Close();

            if (salt.Length > saltBufferSize) {
                throw new Exception("passphrase max length set incorrectly");
            }
            byte[] saltData = new byte[wordSize + saltBufferSize];
            BinaryWriter w2 = new(new MemoryStream(saltData));
            w2.Write((ulong)salt.Length);
            w2.Write(salt);
            w2.Write(new byte[saltBufferSize - salt.Length]);
            w2.Close();

            // Log.Debug($"data: {data.ToHexString()}");
            // Log.Debug($"saltData: {saltData.ToHexString()}");

            byte[] result = RunKernel($"pbkdf2_{iters}_{dklen}", data, saltData, passwords.Length);

            // Console.WriteLine($"ocl: {result.ToHexString()}");

            Seed[] retval = new Seed[passwords.Length];
            BinaryReader r = new BinaryReader(new MemoryStream(result));
            for (int i = 0; i < passwords.Length; i++) {
                byte[] seed = r.ReadBytes(outBufferSize);
                // Console.WriteLine($"seed: {seed.ToHexString()}");
                if (outBufferSize > dklen) seed = seed.Slice(0, dklen);
                if (phrases.Length == passwords.Length) {
                    retval[i] = new Seed(seed, phrases[i], passphrases[0]);
                }
                else {
                    retval[i] = new Seed(seed, phrases[0], passphrases[i]);
                }
            }
            return retval;
        }

        private byte[] RunKernel(string kernelName, byte[] data, byte[] saltData, int count) {
            byte[] result = null;

            Kernel kernel;
            if (kernels.ContainsKey(kernelName)) {
                kernel = kernels[kernelName];
            }
            else {
                kernel = program_pbkdf2.CreateKernel(kernelName);
                kernels[kernelName] = kernel;
            }

            try {
                int outSize = outBufferSize * count;
                using MemoryBuffer inBuffer = context.CreateBuffer(MemoryFlag.ReadOnly | MemoryFlag.CopyHostPointer, data);
                using MemoryBuffer saltBuffer = context.CreateBuffer(MemoryFlag.ReadOnly | MemoryFlag.CopyHostPointer, saltData);
                using MemoryBuffer outBuffer = context.CreateBuffer<byte>(MemoryFlag.WriteOnly, outSize);

                kernel.SetKernelArgument(0, inBuffer);
                kernel.SetKernelArgument(1, saltBuffer);
                kernel.SetKernelArgument(2, outBuffer);
                commandQueue.EnqueueNDRangeKernel(kernel, 1, count);
                result = commandQueue.EnqueueReadBuffer<byte>(outBuffer, outSize);
            }
            catch (Exception e) {
                Log.Error(e.ToString());
                throw;
            }

            return result;
        }

        public Seed[] Pbkdf2_Sha512_MultiSalt(Phrase[] phrases, string[] passphrases, byte[] password, byte[][] salts, int iters = 2048, int dklen = 64) {
            Init_Sha512(dklen);
            // Log.Debug($"salt batch size={salts.Length}");

            byte[] data = new byte[wordSize + pwdBufferSize];
            BinaryWriter w = new(new MemoryStream(data));
            if (password.Length > pwdBufferSize) {
                    throw new Exception("phrase exceeds max length");
            }
            w.Write((ulong)password.Length);
            w.Write(password);
            w.Write(new byte[pwdBufferSize - password.Length]);
            w.Close();

            byte[] saltData = new byte[salts.Length * (wordSize + inBufferSize)];
            BinaryWriter w2 = new(new MemoryStream(saltData));
            for (int i = 0; i < salts.Length; i++) {
                if (salts[i].Length > inBufferSize) {
                    throw new Exception("passphrase max length set incorrectly");
                }
                w2.Write((ulong)salts[i].Length);
                w2.Write(salts[i]);
                w2.Write(new byte[inBufferSize - salts[i].Length]);
            }
            w2.Close();

            // Log.Debug($"data: {data.ToHexString()}");
            // Log.Debug($"saltData: {saltData.ToHexString()}");

            byte[] result = RunKernel($"pbkdf2_saltlist_{iters}_{dklen}", data, saltData, salts.Length);

            // Console.WriteLine($"ocl: {result.ToHexString()}");

            Seed[] retval = new Seed[salts.Length];
            BinaryReader r = new BinaryReader(new MemoryStream(result));
            for (int i = 0; i < salts.Length; i++) {
                byte[] seed = r.ReadBytes(outBufferSize);
                if (outBufferSize > dklen) seed = seed.Slice(0, dklen);
                if (phrases.Length == salts.Length) {
                    retval[i] = new Seed(seed, phrases[i], passphrases[0]);
                }
                else {
                    retval[i] = new Seed(seed, phrases[0], passphrases[i]);
                }
            }
            return retval;
        }

        public void Init_Bip32Derive() {
            if (program_bip32derive_ready) return;

            string code = Bip39_Solver.common_cl + Bip39_Solver_Secp256k1.secp256k1_common_cl + Bip39_Solver_Secp256k1.secp256k1_scalar_cl + Bip39_Solver_Secp256k1.secp256k1_field_cl + Bip39_Solver_Secp256k1.secp256k1_group_cl + Bip39_Solver_Secp256k1.secp256k1_preq_cl + Bip39_Solver_Secp256k1.secp256k1_cl + Bip39_Solver_Sha.sha2_cl + Bip39_Solver.address_cl;

            Log.Info("Compiling OpenCL BIP32 Derive scripts...");
            program_bip32derive?.Dispose();
            program_bip32derive = context.CreateAndBuildProgramFromString(code);
            Log.Info("OpenCL Compiled");
            program_bip32derive_ready = true;
        }

        public Cryptography.Key[] Bip32_Derive(Cryptography.Key[] keys, uint path, int keyLen = 32, int ccLen = 32) {
            Init_Bip32Derive();

            int length = keyLen + ccLen;
            int count = keys.Length;
            byte[] data = new byte[count * length];
            BinaryWriter w = new(new MemoryStream(data));
            for (int i = 0; i < count; i++) {
                w.Write(keys[i].data);
                w.Write(keys[i].cc);
            }
            w.Close();

            string kernelName;

            if (PathNode.IsHardened(path))
            {
                kernelName = "bip32_derive_hardened";
            }
            else
            {
                kernelName = "bip32_derive_normal";
            }

            uint[] paths = new uint[] { path };

            byte[] result;

            try {
                Kernel kernel;
                if (kernels.ContainsKey(kernelName)) {
                    kernel = kernels[kernelName];
                }
                else {
                    kernel = program_bip32derive.CreateKernel(kernelName);
                    kernels[kernelName] = kernel;
                }

                int outSize = length * count;
                using MemoryBuffer inBuffer = context.CreateBuffer(MemoryFlag.ReadOnly | MemoryFlag.CopyHostPointer, data);
                using MemoryBuffer pathBuffer = context.CreateBuffer(MemoryFlag.ReadOnly | MemoryFlag.CopyHostPointer, paths);
                using MemoryBuffer outBuffer = context.CreateBuffer<byte>(MemoryFlag.WriteOnly, outSize);

                kernel.SetKernelArgument(0, inBuffer);
                kernel.SetKernelArgument(1, outBuffer);
                kernel.SetKernelArgument(2, pathBuffer);
                commandQueue.EnqueueNDRangeKernel(kernel, 1, count);
                result = commandQueue.EnqueueReadBuffer<byte>(outBuffer, outSize);

                Cryptography.Key[] ret = new Cryptography.Key[count];
                BinaryReader r = new BinaryReader(new MemoryStream(result));
                for (int i = 0; i < count; i++) {
                    ret[i] = new Cryptography.Key(r.ReadBytes(keyLen), r.ReadBytes(ccLen));
                }
                return ret;
            }
            catch (Exception e) {
                Log.Error(e.ToString());
                throw;
            }
        }

        public void Benchmark_Bip32Derive(uint path, int count = 10240)
        {
            Log.Info($"Benchmark_Bip32Derive path={path}");
            Cryptography.Key[] src = new Cryptography.Key[count];
            ExtKey[] ex = new ExtKey[count];
            ExtKey[] child = new ExtKey[count];
            Parallel.For(0, count, i => {
                Phrase p = new Phrase();
                byte[] seed = Cryptography.Pbkdf2_HMAC512(p.ToPhrase(), Cryptography.PassphraseToSalt(""), 2048, 64);
                ex[i] = ExtKey.CreateFromSeed(seed);

                src[i] = new(ex[i].PrivateKey.ToBytes(), ex[i].ChainCode);
            });

            Stopwatch sw = new Stopwatch();
            sw.Start();
            Parallel.For(0, count, i => {
                child[i] = ex[i].Derive(path);
            });
            sw.Stop();
            long cpu = sw.ElapsedMilliseconds;

            Cryptography.Key[] keys;
            
            Init_Bip32Derive();
            sw.Restart();
            keys = Bip32_Derive(src, path);
            sw.Stop();
            long ocl = sw.ElapsedMilliseconds;

            int badkey = 0, badcc = 0;
            for (int i = 0; i < count; i++) {
                if (keys[i].data.ToHexString() != child[i].PrivateKey.ToBytes().ToHexString()) {
                    badkey++;
                    Log.Error($"SK{i} OCL {keys[i].data.ToHexString()} != ExtKey {child[i].PrivateKey.ToBytes().ToHexString()}");
                }
                if (keys[i].cc.ToHexString() != child[i].ChainCode.ToHexString()) {
                    badcc++;
                    Log.Error($"CC{i} OCL {keys[i].cc.ToHexString()} != ExtKey {child[i].ChainCode.ToHexString()}");
                }
            }
            Log.Info($"Benchmark_Bip32Derive() cputime={cpu} ocltime={ocl} badkey={badkey} badcc={badcc}");
            if (badkey > 0 || badcc > 0) throw new Exception("bad results in Benchmark_Bip32Derive()");
        }

        public void Benchmark_Pbkdf2(int tcount = 1024)
        {
            Wordlists.Initialize();

            {
                Log.Info("Benchmark OpenCL phrases...");
                Phrase[] ph = new Phrase[tcount];
                byte[][] passwords = new byte[tcount][];
                for (int i = 0; i < tcount; i++) ph[i] = new Phrase();
                for (int i = 0; i < tcount; i++) passwords[i] = ph[i].ToPhrase().ToUTF8Bytes();
                string pp = "";
                byte[] salt = Cryptography.PassphraseToSalt(pp);
                Stopwatch oclsw = new Stopwatch();
                oclsw.Start();
                Seed[] oclseeds = Pbkdf2_Sha512_MultiPassword(ph, new string[] { pp }, passwords, salt);
                oclsw.Stop();
                long ocltime = oclsw.ElapsedMilliseconds;
                int badcount = 0;
                Seed[] cpuseeds = new Seed[tcount];
                oclsw.Restart();
                Parallel.For(0, tcount, i => 
                {
                    cpuseeds[i] = new Seed(Cryptography.Pbkdf2_HMAC512(ph[i].ToPhrase(), salt, 2048, 64), ph[i], pp);
                });
                oclsw.Stop();
                long cputime = oclsw.ElapsedMilliseconds;

                for (int i = 0; i < tcount; i++)
                {
                    if (cpuseeds[i].seed.ToHexString() != oclseeds[i].seed.ToHexString())
                    {
                        // Console.WriteLine($"phrase {i}: {ph[i].ToPhrase()}\ncpu: {cpuseeds[i].seed.ToHexString()}\nocl: {((byte[])(oclseeds[i].seed)).ToHexString()}");
                        badcount++;
                    }
                }
                Log.Info($"Benchmark_Pbkdf2() phrases ocltime={ocltime} cputime={cputime} badcount={badcount}");
                if (badcount > 0) throw new SystemException("failed Benchmark_Pbkdf2() phrases");
            }

            {
                Log.Info("Benchmark OpenCL passphrases...");
                Phrase ph = new Phrase();
                string phrase = ph.ToPhrase();
                byte[] password = phrase.ToUTF8Bytes();
                string[] pp = new string[tcount];
                for (int i = 0; i < tcount; i++) {
                    pp[i] = $"{i}";
                }
                byte[][] salts = new byte[tcount][];
                for (int i = 0; i < tcount; i++) salts[i] = Cryptography.PassphraseToSalt(pp[i]);
                Stopwatch oclsw = new Stopwatch();
                oclsw.Start();
                Seed[] oclseeds = Pbkdf2_Sha512_MultiSalt(new Phrase[] { ph }, pp, password, salts);
                oclsw.Stop();
                long ocltime = oclsw.ElapsedMilliseconds;
                int badcount = 0;
                Seed[] cpuseeds = new Seed[tcount];
                oclsw.Restart();
                Parallel.For(0, tcount, i => 
                {
                    byte[] salt = Cryptography.PassphraseToSalt(pp[i]);
                    cpuseeds[i] = new Seed(Cryptography.Pbkdf2_HMAC512(phrase, salt, 2048, 64), ph, pp[i]);
                });
                oclsw.Stop();
                long cputime = oclsw.ElapsedMilliseconds;

                for (int i = 0; i < tcount; i++)
                {
                    if (cpuseeds[i].seed.ToHexString() != oclseeds[i].seed.ToHexString())
                    {
                        // Console.WriteLine($"pp {i}: {pp[i]}\ncpu: {cpuseeds[i].seed.ToHexString()}\nocl: {((byte[])(oclseeds[i].seed)).ToHexString()}");
                        badcount++;
                    }
                }
                Log.Info($"Benchmark_Pbkdf2() passphrases ocltime={ocltime} cputime={cputime} badcount={badcount}");
                if (badcount > 0) throw new SystemException("failed Benchmark_Pbkdf2() passphrases");
            }

        }

        public static void LogOpenCLInfo() {
            IEnumerable<Platform> platforms = Platform.GetPlatforms();
            ConsoleTable consoleTable = new ConsoleTable("Platform", "OpenCL Version", "Vendor", "Device", "Driver Version", "Bits", "Global Memory", "Local Memory", "Clock Speed", "CUs", "Available");
            foreach (Platform platform in platforms)
            {
                foreach (Device device in platform.GetDevices(DeviceType.All))
                {
                    consoleTable.AddRow(
                        platform.Name,
                        $"{platform.Version.MajorVersion}.{platform.Version.MinorVersion}",
                        platform.Vendor,
                        device.Name,
                        device.DriverVersion,
                        $"{device.AddressBits} Bit",
                        $"{Math.Round(device.GlobalMemorySize / 1024.0f / 1024.0f / 1024.0f, 2)} GiB",
                        $"{Math.Round(device.LocalMemorySize / 1024.0f, 2)} KiB",
                        $"{device.MaximumClockFrequency} MHz",
                        device.MaximumComputeUnits,
                        device.IsAvailable ? "✔" : "✖");
                }
            }
            Log.Info("OpenCL Supported Platforms & Devices:");
            Log.Info(consoleTable.ToStringAlternative());
        }
    }
}