﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography; 
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FixMyCrypto
{
    static class Global {
        public static bool done = false;

        public static bool found = false;
    }
    public enum CoinType {
        BTC,
        ADA,
        ADALedger,
        ADATrezor,
        ETH,
        DOGE,
        LTC,
        BCH,
        XRP,
        SOL
    }
    public enum BtcApiType {
        blockcypher,
        mempool
    }
    public enum AdaApiType {
        rest,
        graphql
    }
    public enum EthApiType {
        gethrpc,
        blockcypher
    }
    public enum AltcoinApiType {
        dogechain,
        bitcore
    }
    public class WebClient {
        public static int retryCount = 5;
        public static readonly HttpClient client = new HttpClient();
    }

    public static class ArrayExtensions {
        private static char[] digits = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
        public static string ToHexString(this byte[] b) {
            return b.ToHexString(0, b.Length);
        }
        public static string ToHexString(this byte[] b, int start, int len) {
            char[] c = new char[len * 2];
            int p = 0;
            int q = start;
            int end = start + len;
            while (q < end) {
                byte a = b[q++];
                c[p++] = digits[a >> 4];
                c[p++] = digits[a & 0x0f];
            }
            return new string(c);
        }

        //  Array.Copy or for-loop copy faster than BlockCopy for most of what we do
        //  https://stackoverflow.com/questions/1389821/array-copy-vs-buffer-blockcopy
        public static T[] Slice<T>(this T[] b, int start, int len = -1) {
            if (len == -1) len = b.Length - start;
            T[] r = new T[len];

            // int i;
            // for (i = 0; i < len; i++) r[i] = b[i + start];

            // Buffer.BlockCopy(b, start, r, 0, len);
            
            Array.Copy(b, start, r, 0, len);
            return r;           
        }

        public static short[] Copy(this short[] a) {
            short[] b = new short[a.Length];
            a.CopyTo(b, 0);
            return b;
        }

    }
    class Work {
        public Phrase phrase;
        public IList<Address> addresses;
        public Work(Phrase phrase, List<Address> addresses) {
            this.phrase = phrase;
            this.addresses = addresses;
        }

        public override string ToString() {
            string result = "";
            if (this.phrase != null) result += $"phrase: \"{phrase.ToPhrase()}\"\n";
            if (this.addresses != null) {
                foreach (Address address in addresses)
                    result += $"address: {address}\n";
            }
            return result;
        }
    }

    class FoundResult {

        public static void DoFoundResult(CoinType coin, Address addr) {
            Global.found = true;

            Address best = addr;
            Log.All("\n\n\n\n");

            if (!String.IsNullOrEmpty(Settings.GetApiPath(coin))) {
                Log.All("Found result! Please wait while searching related addresses...");

                //  Check blockchain for other related addresses for txs

                LookupAddress la = LookupAddress.Create(coin, null, 0, 0);

                List<Address> addresses = GetUsedAddresses(la, coin, addr, ref best);

                int totalTx = 0;

                foreach (Address address in addresses) {
                    Log.All($"Found used address: {address.path}: {address.address}");

                    try {
                        List<string> txs = Task.Run<List<string>>(async () => await la.GetTransactions(address.address)).Result;

                        if (txs.Count > 0) {
                            Log.All($"Found {txs.Count} transactions:");
                            
                            foreach (string tx in txs) {
                                Log.All($"tx hash: {tx}");
                            }
                        }

                        totalTx += txs.Count;
                    }
                    catch (Exception) {}
                }

                if (totalTx > 0) {
                    Log.All("Verify listed addresses & TX hashes on blockchain explorer!");
                }
                else if (addresses.Count > 0) {
                    Log.All("Verify listed addresses on blockchain explorer!");
                }
            }
            else {
                //  No blockchain available - show possible related addresses

                Log.All($"Found known address {addr.path}: {addr.address}");

                int index = 0;

                int account = 0;

                Path.GetAccountIndex(addr.path, out account, out index);

                int min = Math.Max(index - 2, 0);

                int max = index + (5 - (index - min));

                int count = 0;

                List<int> indices = new List<int>();

                for (int i = min; i <= max; i++) if (i != index) indices.Add(i);

                PhraseToAddress p2a = PhraseToAddress.Create(coin, null, null, -1, 0);

                int[] accounts = { account };
                string[] paths = { addr.path };

                List<Address> addresses = p2a.GetAddresses(addr.phrase, addr.passphrase, paths, accounts, indices.ToArray());

                foreach (Address address in addresses) {
                    Log.All($"Possible related address {address.path}: {address.address}");
                    count++;
                }

                if (count > 0) {
                    Log.All("Verify listed addresses on blockchain explorer!");
                }

            }

            var resultData = new {
                instructions = "Contact help@fixmycrypto.com if you require further assistance.",
                coin = $"{coin}",
                address = best.address,
                path = best.path,
                wrongPhrase = Settings.phrase,
                correctedPhrase = addr.phrase.ToPhrase(),
                passphrase = addr.passphrase
            };
            string result = JsonConvert.SerializeObject(resultData, Formatting.Indented);
            StreamWriter writer = File.CreateText("results.json");
            writer.WriteLine(result);
            writer.Close();
            Log.All($"\n!!! FOUND WALLET !!!\nSample address:\n{best.address} ({best.path})\n\nRecovery Phrase written to: results.json\n");
            Log.All("To support the developers, please donate to one of these addresses:\nBTC: bc1q477afku8x7964gmzlsapgj8705e63ch89p8k4z\nETH: 0x0327DF6652D07eE6cc670626b034edFfceD1B20C\nDOGE: DT8iZF8RbqpRftgrWdiq34EZdJpCGiWBwG\nADA: addr1qxhjru35kv8fq66afxxdnjzf720anfcppktchh6mjuwxma3e876gh3czzkq0guls5qrkghexsuh543h7k2xqlje5lskqfp2elv\n");
        }

        public static List<Address> GetUsedAddresses(LookupAddress la, CoinType coin, Address addr, ref Address best) {
            PhraseToAddress p2a = PhraseToAddress.Create(coin, null, null, -1, 0);
            // int end = Math.Max(Settings.indexMax, Settings.indexMin + 20);
            double maxCoins = -1;
            List<Address> usedAddresses = new List<Address>();
            List<int> indices = new List<int>(Settings.indices);

            //  Ensure we test at least 5 addresses
            int next = indices[indices.Count - 1] + 1;
            while (indices.Count < 5) {
                if (!indices.Contains(next)) indices.Add(next);
                next++;
            }
 
            try {
                string[] paths = { addr.path };
                List<Address> addresses = p2a.GetAddresses(addr.phrase, addr.passphrase, paths, Settings.accounts, indices.ToArray());
                foreach (Address address in addresses) {
                    Log.All($"Lookup related address {address.path}: {address.address}");
                    LookupAddress.LookupResult result = la.GetContents(address.address);

                    if (result.coins > 0 || result.txCount > 0) {
                        Log.Debug($"found active address: {address} : {address.path} {result}");

                        usedAddresses.Add(address);
    
                        // end = index + 20;

                        if (result.coins > maxCoins) {
                            best = address;
                            maxCoins = result.coins;
                        }
                    }
                }
            }
            catch (Exception) {}

            return usedAddresses;
        }
    }
}
