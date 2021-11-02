using System;
using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Cryptography.ECDSA;

namespace FixMyCrypto {
    class PhraseToAddressSolana : PhraseToAddress {
        private HMACSHA512 HMAC512;
        public enum KeyType {
            Public,
            Private
        }
        public PhraseToAddressSolana(BlockingCollection<Work> phrases, BlockingCollection<Work> addresses, int threadNum, int threadMax) : base(phrases, addresses, threadNum, threadMax) {
            this.HMAC512 = new HMACSHA512(ed25519_seed);
        }
        public override CoinType GetCoinType() { return CoinType.SOL; }
        public override string[] GetDefaultPaths(string[] knownAddresses) {
            string[] p = { 
                "m/44'/501'",
                "m/44'/501'/{account}'",
                "m/44'/501'/{account}'/{index}'",
                // not sure how to implement non-hardened derivation
                // "m/501'/{account}'/0/{index}" 
                };
            return p;
        }
        public override Object DeriveMasterKey(Phrase phrase, string passphrase) {
            //  https://github.com/LedgerHQ/speculos/blob/c0311aef48412e40741a55f113939469da78e8e5/src/bolos/os_bip32.c#L112

            byte[] salt = Encoding.UTF8.GetBytes("mnemonic" + passphrase);
            string password = phrase.ToPhrase();
            var seed = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA512, 2048, 64);

            //  expand_seed_slip10

            HMAC512.Initialize();
            var hash = HMAC512.ComputeHash(seed);

            var iL = hash.Slice(0, 32);
            var iR = hash.Slice(32);

            return new Key(iL, iR);
        }
        protected override Object DeriveChildKey(Object parentKey, uint index) {
            //  https://github.com/LedgerHQ/speculos/blob/c0311aef48412e40741a55f113939469da78e8e5/src/bolos/os_bip32.c#L342
            //  https://github.com/alepop/ed25519-hd-key/blob/d8c0491bc39e197c86816973e80faab54b9cbc26/src/index.ts#L33

            if (!PathNode.IsHardened(index)) {
                throw new NotSupportedException("SOL non-hardened path not supported");
            }

            Key x = (Key)parentKey;

            byte[] tmp = new byte[37];
            tmp[0] = 0;
            System.Buffer.BlockCopy(x.data, 0, tmp, 1, 32);

            tmp[33] = (byte)((index >> 24) & 0xff);
            tmp[34] = (byte)((index >> 16) & 0xff);
            tmp[35] = (byte)((index >> 8) & 0xff);
            tmp[36] = (byte)(index & 0xff);

            HMACSHA512 hmac = new HMACSHA512(x.cc);
            byte[] I = hmac.ComputeHash(tmp);

            return new Key(I.Slice(0, 32), I.Slice(32));
        }

        protected override Address DeriveAddress(PathNode node) {
            Key key = (Key)node.Key;

            byte[] pub = Chaos.NaCl.Ed25519.PublicKeyFromSeed(key.data);

            string address = Base58.Encode(pub);
            return new Address(address, node.GetPath());
        }

        public override void ValidateAddress(string address) {
            var tmp = Base58.Decode(address);
            if (tmp.Length != 32) throw new Exception("invalid SOL address length");
        }
    }


}