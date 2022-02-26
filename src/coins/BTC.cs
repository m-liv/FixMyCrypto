using NBitcoin;
using NBitcoin.Altcoins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FixMyCrypto {
    class PhraseToAddressBitAltcoin : PhraseToAddress {
        private Network network;
        private CoinType coinType;
        public PhraseToAddressBitAltcoin(BlockingCollection<Work> phrases, BlockingCollection<Work> addresses, CoinType coin) : base(phrases, addresses) {
            this.coinType = coin;

            switch (this.coinType) {
                case CoinType.BTC:

                this.network = Network.Main;

                break;

                case CoinType.DOGE:

                this.network = Dogecoin.Instance.Mainnet;

                break;

                case CoinType.LTC:

                this.network = Litecoin.Instance.Mainnet;

                break;

                case CoinType.BCH:

                this.network = Network.Main;    //  TODO

                break;

                case CoinType.XRP:

                this.network = Network.Main;    //  TODO

                break;

                default:

                throw new NotSupportedException();
            }
        }
        public override string[] GetDefaultPaths(string[] knownAddresses) {
            List<string> paths = new List<string>();

            switch (this.coinType) {
                case CoinType.BTC:

                if (knownAddresses != null && knownAddresses.Length > 0) {
                    foreach (string address in knownAddresses) {
                        //  Guess path from known address format

                        if (address.StartsWith("bc1q")) {
                            //  BIP84
                            paths.Add("m/84'/0'/{account}'/0/{index}");
                        }
                        else if (address.StartsWith("bc1p") || address.StartsWith("tb1")) {
                            //  BIP86
                            paths.Add("m/86'/0'/{account}'/0/{index}");
                        }
                        else if (address.StartsWith("3")) {
                            //  BIP49
                            paths.Add("m/49'/0'/{account}'/0/{index}");
                        }
                        else {
                            //  BIP32 MultiBit HD
                            paths.Add("m/0'/0/{index}");
                            //  BIP32 Bitcoin Core
                            paths.Add("m/0'/0'/{index}'");
                            //  BIP32 blockchain.info/Coinomi/Ledger
                            paths.Add("m/44'/0'/{account}'/{index}");
                            //  BIP44
                            paths.Add("m/44'/0'/{account}'/0/{index}");
                        }
                    }
                }
                else {
                    //  Unknown path

                    //  BIP32 MultiBit HD
                    paths.Add("m/0'/0/{index}");
                    //  BIP32 Bitcoin Core
                    paths.Add("m/0'/0'/{index}'");
                    //  BIP32 blockchain.info/Coinomi/Ledger
                    paths.Add("m/44'/0'/{account}'/{index}");
                    //  BIP44
                    paths.Add("m/44'/0'/{account}'/0/{index}");
                    //  BIP49
                    paths.Add("m/49'/0'/{account}'/0/{index}");
                    //  BIP84
                    paths.Add("m/84'/0'/{account}'/0/{index}");
                }

                break;

                case CoinType.DOGE:

                paths.Add("m/44'/3'/{account}'/0/{index}");

                break;

                case CoinType.LTC:

                if (knownAddresses != null && knownAddresses.Length > 0) {
                    foreach (string address in knownAddresses) {
                        if (address.StartsWith("M")) {
                            paths.Add("m/49'/2'/{account}'/0/{index}");
                        }
                        else if (address.StartsWith("ltc1")) {
                            paths.Add("m/84'/2'/{account}'/0/{index}");
                        }
                        else {
                            paths.Add("m/44'/2'/{account}'/0/{index}");
                        }
                    }
                }
                else {
                    paths.Add("m/44'/2'/{account}'/0/{index}");
                    paths.Add("m/49'/2'/{account}'/0/{index}");
                    paths.Add("m/84'/2'/{account}'/0/{index}");
                }

                break;

                case CoinType.BCH:

                //  Pre-Fork
                paths.Add("m/44'/0'/{account}'/0/{index}");

                //  Post-Fork
                paths.Add("m/44'/145'/{account}'/0/{index}");

                break;

                case CoinType.XRP:

                paths.Add("m/44'/144'/{account}'/0/{index}");

                break;

                default:

                throw new NotSupportedException();
            }

            return paths.ToArray();
        }
        public override CoinType GetCoinType() { return this.coinType; }

        private ScriptPubKeyType GetKeyType(string path) {
            ScriptPubKeyType keyType = ScriptPubKeyType.Legacy;
            if (path.StartsWith("m/49")) keyType = ScriptPubKeyType.SegwitP2SH;
            if (path.StartsWith("m/84")) keyType = ScriptPubKeyType.Segwit;
            if (path.StartsWith("m/86")) keyType = ScriptPubKeyType.TaprootBIP86;
            return keyType;
        }
        public override Object DeriveRootKey(Phrase phrase, string passphrase) {
            string p = phrase.ToPhrase();
            byte[] salt = Cryptography.PassphraseToSalt(passphrase);
            byte[] seed = Cryptography.Pbkdf2_HMAC512(p, salt, 2048, 64);
            return ExtKey.CreateFromSeed(seed);
        }
        public override Object[] DeriveRootKey_BatchPhrases(Phrase[] phrases, string passphrase) {
            if (ocl == null) {
                return base.DeriveRootKey_BatchPhrases(phrases, passphrase);
            }
            else {
                Seed[] seeds = ocl.Pbkdf2_Sha512_MultiPhrase(phrases, passphrase);
                Object[] keys = new object[phrases.Length];
                Parallel.For(0, phrases.Length, i => {
                    if (Global.Done) return;
                    keys[i] = ExtKey.CreateFromSeed(seeds[i].seed);
                });
                return keys;
            }
        }
        public override Object[] DeriveRootKey_BatchPassphrases(Phrase phrase, string[] passphrases) {
            if (ocl == null) {
                return base.DeriveRootKey_BatchPassphrases(phrase, passphrases);
            }
            else {
                Seed[] seeds = ocl.Pbkdf2_Sha512_MultiPassphrase(phrase, passphrases);
                Object[] keys = new object[passphrases.Length];
                Parallel.For(0, passphrases.Length, i => {
                    if (Global.Done) return;
                    keys[i] = ExtKey.CreateFromSeed(seeds[i].seed);
                });
                return keys;
            }
        }
        protected override Object DeriveChildKey(Object parentKey, uint index) {
            ExtKey key = (ExtKey)parentKey;
            return key.Derive(index);
        }
        protected override Address DeriveAddress(PathNode node) {
            ExtKey pk = (ExtKey)node.Key;
            string path = node.GetPath();
            string address = pk.GetPublicKey().GetAddress(GetKeyType(path), this.network).ToString();
            return new Address(address, path);
        }

        public override void ValidateAddress(string address) {
            switch (this.coinType) {
                case CoinType.BTC:

                if (address.StartsWith("1"))
                {
                    var addr = new BitcoinPubKeyAddress(address, Network.Main);
                } 
                else if(address.StartsWith("3"))
                {
                    var addr = new BitcoinScriptAddress(address, Network.Main);
                }
                else if (address.StartsWith("bc1q"))
                {
                    var addr = new BitcoinWitPubKeyAddress(address, Network.Main);
                }
                else if (address.StartsWith("bc1p") || address.StartsWith("tb1"))
                {
                    var addr = TaprootAddress.Create(address, Network.Main);
                }
                else
                {
                    throw new Exception("Invalid address");
                }

                break;

                case CoinType.BCH:

                //  TODO: Not working

                // var bch = BitcoinAddress.Create(address, NBitcoin.Altcoins.BCash.Instance.Mainnet);

                /*
                if (address.StartsWith("1")) {
                    var bch = new BitcoinPubKeyAddress(address, NBitcoin.Altcoins.BCash.Instance.Mainnet);
                }
                else if (address.StartsWith("q")) {
                    var bch = new BitcoinPubKeyAddress(address, NBitcoin.Altcoins.BCash.Instance.Mainnet);
                }
                else {
                    throw new Exception("Invalid address");
                }
                */

                break;

                case CoinType.DOGE:

                if (!address.StartsWith("D")) throw new Exception("Invalid address");

                var doge = new BitcoinPubKeyAddress(address, NBitcoin.Altcoins.Dogecoin.Instance.Mainnet);

                break;

                case CoinType.LTC:

                if (address.StartsWith("L"))
                {
                    var addr = new BitcoinPubKeyAddress(address, NBitcoin.Altcoins.Litecoin.Instance.Mainnet);
                }
                else if(address.StartsWith("M"))
                {
                    var addr = new BitcoinScriptAddress(address, NBitcoin.Altcoins.Litecoin.Instance.Mainnet);
                }
                else if (address.StartsWith("ltc1q"))
                {
                    var addr = new BitcoinWitPubKeyAddress(address, NBitcoin.Altcoins.Litecoin.Instance.Mainnet);
                }
                else
                {
                    throw new Exception("Invalid address");
                }

                break;

                //  TODO: other coins

                default:

                break;
            }
        }
    }
}