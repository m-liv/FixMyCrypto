using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FixMyCrypto {
    abstract class PhraseToAddress {
        protected BlockingCollection<Work> phraseQueue, addressQueue;

        public static PhraseToAddress Create(CoinType coin, BlockingCollection<Work> phrases, BlockingCollection<Work> addresses) {
            switch (coin) {
                case CoinType.ADA:

                return new PhraseToAddressCardano(phrases, addresses);

                case CoinType.ADALedger:

                return new PhraseToAddressCardanoLedger(phrases, addresses);

                case CoinType.ADATrezor:

                return new PhraseToAddressCardanoTrezor(phrases, addresses);

                case CoinType.ETH:

                return new PhraseToAddressEth(phrases, addresses);

                case CoinType.BTC:
                case CoinType.DOGE:
                case CoinType.LTC:
                case CoinType.BCH:

                return new PhraseToAddressBitAltcoin(phrases, addresses, coin);

                case CoinType.SOL:

                return new PhraseToAddressSolana(phrases, addresses);

                case CoinType.ALGO:

                return new PhraseToAddressAlgorand(phrases, addresses);

                case CoinType.DOT:

                return new PhraseToAddressPolkadot(phrases, addresses);

                case CoinType.DOTLedger:

                return new PhraseToAddressPolkadotLedger(phrases, addresses);

                case CoinType.XRP:

                return new PhraseToAddressXrp(phrases, addresses);

                default:

                throw new NotSupportedException();
            }
        }
        Stopwatch queueWaitTime = new Stopwatch();
        Stopwatch stopWatch = new Stopwatch();
        long count = 0;
        string lastPassphrase = null;
        Phrase lastPhrase = null;
        Checkpoint checkpoint = null;
        long passphraseTested = 0, passphraseTotal = 0, passphraseDone = 0, passphraseStart = 0;
        object mutex = new();
        protected OpenCL ocl = null;
        protected MultiPassphrase mp = null;
        protected long mpCount = 0;

        private class Batch {
            public Object[] keys;

            public PathTree tree;

            public ProduceAddress produceAddress;
            
            public Batch (Object[] keys, PathTree tree, ProduceAddress produceAddress) {
                this.keys = keys;
                this.tree = tree;
                this.produceAddress = produceAddress;
            }
        }
        private class PhraseBatch : Batch {
            public Phrase[] phrases;
            public string passphrase;

            public PhraseBatch(Phrase[] phrases, string passphrase, Object[] keys, PathTree tree, ProduceAddress produceAddress) : base(keys, tree, produceAddress) {
                this.phrases = phrases;
                this.passphrase = passphrase;
            }
        }
        private class PassphraseBatch : Batch {
            public Phrase phrase;
            public string[] passphrases;
            public PassphraseBatch(Phrase phrase, string[] passphrases, Object[] keys, PathTree tree, ProduceAddress produceAddress) : base(keys, tree, produceAddress) {
                this.phrase = phrase;
                this.passphrases = passphrases;
            }
        }
        private BlockingCollection<Batch> batchQueue = new BlockingCollection<Batch>(Settings.Threads);

        protected PhraseToAddress(BlockingCollection<Work> phrases, BlockingCollection<Work> addresses) {
            this.phraseQueue = phrases;
            this.addressQueue = addresses;
        }
        public void SetOpenCL(OpenCL ocl) {
            this.ocl = ocl;
        }
        public void SetPassphrase(MultiPassphrase mp, long mpCount) {
            this.mp = mp;
            this.mpCount = mpCount;
        }
        public void SetCheckpoint(Checkpoint c) {
            checkpoint = c;
        }
        public (Phrase, string, long) GetLastTested() {
            lock(mutex) {
                return (this.lastPhrase, this.lastPassphrase, this.passphraseDone);
            }
        }

        public abstract Object DeriveMasterKey(Phrase phrase, string passphrase);

        public virtual Object[] DeriveMasterKey_BatchPhrases(Phrase[] phrases, string passphrase)
        {
            Object[] keys = new object[phrases.Length];
            Parallel.For(0, phrases.Length, i => {
                if (Global.Done) return;
                keys[i] = DeriveMasterKey(phrases[i], passphrase);
            });
            return keys;
        }

        public virtual Object[] DeriveMasterKey_BatchPassphrases(Phrase phrase, string[] passphrases)
        {
            Object[] keys = new object[passphrases.Length];
            Parallel.For(0, passphrases.Length, i => {
                if (Global.Done) return;
                keys[i] = DeriveMasterKey(phrase, passphrases[i]);
            });
            return keys;
        }
        protected abstract Object DeriveChildKey(Object parentKey, uint index);
        protected abstract Address DeriveAddress(PathNode node);
        public abstract void ValidateAddress(string address);

        public static void ValidateAddress(CoinType coin, string address) {
            PhraseToAddress p2a = PhraseToAddress.Create(coin, null, null);
            p2a.ValidateAddress(address);
        }

        protected virtual string GetStakePath() { return null; }
        private void DeriveChildKeys(PathNode node) {
            node.Key = DeriveChildKey(node.Parent.Key, node.Value);

            foreach (PathNode child in node.Children) {
                if (Global.Done) break;
                DeriveChildKeys(child);
            }
        }

        private void DeriveAddresses(PathNode node, Phrase phrase, string passphrase, List<Address> addresses) {
            if (node.End) {
                var address = DeriveAddress(node);
                if (address != null) {
                    address.phrase = phrase;
                    address.passphrase = passphrase;
                    addresses.Add(address);
                }
            }

            foreach (PathNode child in node.Children) {
                if (Global.Done) break;
                DeriveAddresses(child, phrase, passphrase, addresses);
            }
        }
        private List<Address> GetAddressesList(Phrase phrase, string[] passphrases, int account, int index, string[] paths) {
            int[] accounts = { account };
            int[] indices = { index };
            return GetAddressesList(phrase, passphrases, paths, accounts, indices);
        }

        public PathTree CreateTree(string[] paths, int[] accounts, int[] indices) {
            PathTree tree = new PathTree();

            if (paths == null || paths.Length == 0 || (paths.Length == 1 && String.IsNullOrEmpty(paths[0]))) {
                paths = GetDefaultPaths(Settings.KnownAddresses);
            }

            if (accounts == null || accounts.Length == 0) accounts = Settings.Accounts;
            if (indices == null || indices.Length == 0) indices = Settings.Indices;

            foreach (string path in paths) {
                string pa = path;
                if (!pa.Contains("{account}") && !pa.Contains("{index}")) {
                    pa = Path.Tokenize(path);
                }

                foreach (int account in accounts) {
                    string pat = pa.Replace("{account}", $"{account}");

                    foreach (int index in indices) {
                        string p = pat.Replace("{index}", $"{index}");
                        tree.AddPath(p);
                    }

                    switch (this.GetCoinType()) {
                        case CoinType.ADA:
                        case CoinType.ADALedger:
                        case CoinType.ADATrezor:

                        //  Derive stake key
                        string stakePath = GetStakePath().Replace("{account}", $"{account}");
                        tree.AddPath(stakePath, false);
                        break;
                    }
                }
            }

            return tree;
        }

        public List<Address> GetAddressesList(Phrase phrase, string[] passphrases, string[] paths, int[] accounts, int[] indices) {

            //  Create path tree
            PathTree tree = CreateTree(paths, accounts, indices);

            //  Derive descendent keys

            List<Address> addresses = new();

            // MasterKey[] masterKeys = DeriveMasterKeys(phrase, passphrases);
            // foreach (MasterKey key in masterKeys) {

            foreach (string passphrase in passphrases) {
                if (Global.Done) break;

                // tree.Root.Key = key.key;
                tree.Root.Key = DeriveMasterKey(phrase, passphrase);

                foreach (PathNode child in tree.Root.Children) {
                    DeriveChildKeys(child);
                }

                List<Address> addrs = new();

                DeriveAddresses(tree.Root, phrase, passphrase, addrs);

                bool found = false;

                foreach (Address address in addrs) {

                    if (Settings.KnownAddresses != null && Settings.KnownAddresses.Length > 0) {
                        foreach (string known in Settings.KnownAddresses) {
                            if (known.Equals(address.address, StringComparison.OrdinalIgnoreCase)) {
                                found = true;
                                break;
                            }
                        }
                    }
                }

                addresses.AddRange(addrs);

                if (found) break;
            }

            return addresses;
        }

        public delegate void ProduceAddress(List<Address> addresses);

        public void ProcessBatch() {
            while (!Global.Done) {
                Batch batch = null;

                batchQueue.TryTake(out batch, 10);

                if (batch == null) continue;

                if (batch is PhraseBatch) {
                    PhraseBatch pb = batch as PhraseBatch;

                    Parallel.For(0, pb.phrases.Length, i => {
                        if (Global.Done) return;

                        PathTree t = new PathTree(pb.tree);

                        t.Root.Key = pb.keys[i];

                        foreach (PathNode child in t.Root.Children) {
                            DeriveChildKeys(child);
                        }

                        List<Address> addrs = new();

                        DeriveAddresses(t.Root, pb.phrases[i], pb.passphrase, addrs);

                        if (Produce != null) Produce(addrs);

                        lock(mutex) { 
                            count += addrs.Count; 
                        }
                    });

                    lock(mutex) {
                        lastPhrase = pb.phrases[pb.phrases.Length - 1];
                        lastPassphrase = pb.passphrase;
                    }
                }
                else if (batch is PassphraseBatch) {
                    PassphraseBatch pb = batch as PassphraseBatch;

                    Parallel.For(0, pb.passphrases.Length, i => {
                        if (Global.Done) return;

                        PathTree t = new PathTree(pb.tree);

                        t.Root.Key = pb.keys[i];

                        foreach (PathNode child in t.Root.Children) {
                            DeriveChildKeys(child);
                        }

                        List<Address> addrs = new();

                        DeriveAddresses(t.Root, pb.phrase, pb.passphrases[i], addrs);

                        if (pb.produceAddress != null) pb.produceAddress(addrs);

                        lock(mutex) {
                            count += addrs.Count;
                            passphraseTested++;
                        }
                    });

                    lock(mutex) {
                        lastPhrase = pb.phrase;
                        lastPassphrase = pb.passphrases[pb.passphrases.Length - 1];
                        passphraseDone = passphraseTested;
                    }
                }
            }
        }

        public void GetAddressesBatchPassphrases(Phrase phrase, string[] passphrases, PathTree tree, ProduceAddress Produce) {

            if (Global.Done) return;

            Object[] keys = DeriveMasterKey_BatchPassphrases(phrase, passphrases);

            //  Enqueue batch

            PassphraseBatch pb = new PassphraseBatch(phrase, passphrases, keys, tree, Produce);
            
            try {
                batchQueue.Add(pb);
            }
            catch (InvalidOperationException) {
                return;
            }
        }

        public void GetAddressesBatchPhrases(Phrase[] phrases, string passphrase, PathTree tree, ProduceAddress Produce = null) {

            if (Global.Done) return;

            Object[] keys = DeriveMasterKey_BatchPhrases(phrases, passphrase);

            PhraseBatch pb = new PhraseBatch(phrases, passphrase, keys, tree, Produce);
            
            try {
                batchQueue.Add(pb);
            }
            catch (InvalidOperationException) {
                return;
            }
        }
        public abstract string[] GetDefaultPaths(string[] knownAddresses);
        public List<Address> GetAddressList(string phrase, string passphrase, int account, int index, string path = null) {
            string[] paths = { path };
            Phrase p = new Phrase(phrase);
            string[] passphrases = { passphrase };
            return GetAddressesList(p, passphrases, account, index, paths);
        }
        public abstract CoinType GetCoinType();
        public void Finish() {
            Global.Done = true;
            phraseQueue.CompleteAdding();
            addressQueue.CompleteAdding();
            if (count > 0) Log.Info("P2A done, count: " + count + " total time: " + stopWatch.ElapsedMilliseconds/1000 + $"s, keys/s: {1000*count/stopWatch.ElapsedMilliseconds}, queue wait: " + queueWaitTime.ElapsedMilliseconds/1000 + "s");
            count = 0;
        }
        private long logTotal = 0;
        public void PassphraseLog() {
            if (stopWatch.ElapsedMilliseconds == 0 || (passphraseTested - passphraseStart) == 0 || passphraseTotal == 0 || passphraseTested <= logTotal) return;
            Log.Info($"Passphrases tested {passphraseTested}/{passphraseTotal} ({100.0*passphraseTested/passphraseTotal:F2}%), passphrases/s: {1000*(passphraseTested - passphraseStart)/stopWatch.ElapsedMilliseconds}");
            logTotal = passphraseTested;
        }
        public void Consume() {
            Log.Debug("P2A start");

            PathTree tree = CreateTree(Settings.Paths, Settings.Accounts, Settings.Indices);

            MultiPassphrase p;
            long passphraseCount;
            string passphrase = null;

            //  Generate passphrase list
            if (this.mp != null && this.mpCount > 0) {
                p = this.mp;
                passphraseCount = this.mpCount;
            }
            else {
                p = new MultiPassphrase(Settings.Passphrases);
                passphraseCount = p.GetCount();
            }

            if (passphraseCount == 1) {
                foreach (string s in p) {
                    passphrase = s;
                    break;
                }
            }

            int batchSize = 1024;
            if (ocl != null) batchSize = ocl.GetBatchSize();
            Queue<Phrase> phraseBatch = new(batchSize);
            Queue<string> passphraseBatch = new(batchSize);

            //  Start batch thread
            Thread t = new Thread(ProcessBatch);
            t.Start();

            stopWatch.Start();

            System.Timers.Timer passphraseLogger = new System.Timers.Timer(15 * 1000);
            passphraseLogger.Elapsed += (StringReader, args) => {
                PassphraseLog();
            };

            while (!Global.Done) {

                //  Dequeue phrase
                Work w = null;

                queueWaitTime.Start();
                phraseQueue.TryTake(out w, 10);
                queueWaitTime.Stop();

                if (w != null) {
 
                    if (Global.Done) break;
                    
                    //  Convert phrase to address

                    try {
                        stopWatch.Start();
                        if (passphraseCount > 1) {
                            passphraseTested = 0;
                            passphraseTotal = passphraseCount;
                            passphraseLogger.Start();
                            IEnumerator<string> e = p.GetEnumerator();
                            while (e.MoveNext() && !Global.Done) {
                                string current = e.Current;

                                //  If checkpoint is set, skip passphrases until we reach the checkpoint
                                (string checkpointPassphrase, long num) = checkpoint.GetCheckpointPassphrase();
                                if (checkpointPassphrase != null) {
                                    passphraseTested++;
                                    if (num == passphraseTested) {
                                        if (checkpointPassphrase == current) {
                                            Log.Info($"Resuming from last checkpoint passphrase: {current}");
                                            passphraseDone = passphraseTested;
                                            passphraseStart = passphraseTested;
                                            checkpoint.ClearPassphrase();
                                            checkpoint.Start();
                                            stopWatch.Restart();
                                            continue;
                                        }
                                        else {
                                            Log.Error($"Passphrase restore error\nexpect:  {checkpointPassphrase}\ncurrent: {current}");
                                            FixMyCrypto.PauseAndExit(1);
                                        }
                                    }
                                    else {
                                        continue;
                                    }
                                }

                                passphraseBatch.Enqueue(current);
                                if (passphraseBatch.Count >= batchSize) {
                                    string[] passphrases = passphraseBatch.ToArray();
                                    passphraseBatch.Clear();
                                    GetAddressesBatchPassphrases(w.phrase, passphrases, tree, Produce);
                                }
                            }

                            //  Finish remaining in batch
                            if (passphraseBatch.Count > 0 && !Global.Done) {
                                string[] passphrases = passphraseBatch.ToArray();
                                passphraseBatch.Clear();
                                GetAddressesBatchPassphrases(w.phrase, passphrases, tree, Produce);
                            }

                            passphraseLogger.Stop();
                        }
                        else {
                            phraseBatch.Enqueue(w.phrase);
                            if (phraseBatch.Count >= batchSize) {
                                Phrase[] phrases = phraseBatch.ToArray();
                                phraseBatch.Clear();
                                GetAddressesBatchPhrases(phrases, passphrase, tree, Produce);
                            }
                        }
                    }
                    catch (Exception ex) {
                        Log.Error("P2A error: " + ex.Message);
                    }
                    finally {
                        stopWatch.Stop();
                    }
                }
                else if (phraseBatch.Count > 0) {
                    //  Run a partial batch

                    Phrase[] phrases = phraseBatch.ToArray();
                    phraseBatch.Clear();
                    GetAddressesBatchPhrases(phrases, passphrase, tree, Produce);
                }
            }

            //  End of phrase generation; finish any remaining phrases in queue

            if (phraseBatch.Count > 0) {
                Phrase[] phrases = phraseBatch.ToArray();
                phraseBatch.Clear();
                GetAddressesBatchPhrases(phrases, passphrase, tree, Produce);
            }

            batchQueue.CompleteAdding();

            passphraseLogger.Stop();
            Finish();
            t.Join();
            stopWatch.Stop();
        }

        public void Produce(List<Address> addresses) {
            if (Settings.KnownAddresses != null && Settings.KnownAddresses.Length > 0) {
                //  See if we generated the known address
                foreach (Address address in addresses) {
                    foreach (string knownAddress in Settings.KnownAddresses) {
                        if (address.address.Equals(knownAddress, StringComparison.OrdinalIgnoreCase)) {
                            //  Found known address
                            Finish();

                            FoundResult.DoFoundResult(this.GetCoinType(), address);
                        }
                    }
                }
            }
            else {
                //  Need to search blockchain for address

                Work w2 = new Work(null, addresses);

                //  Enqueue address

                queueWaitTime.Start();
                try {
                    addressQueue.Add(w2);
                }
                catch (InvalidOperationException) {
                    return;
                }
                finally {
                    queueWaitTime.Stop();
                }
            }                    
        }
    }
}