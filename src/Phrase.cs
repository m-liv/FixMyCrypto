using System;

namespace FixMyCrypto {
    public class Phrase {
        private short[] ix;

        public int Length { get { return ix.Length; } }
        public short[] Indices { get { return ix; } }
        public Phrase(short[] indices) { ix = indices; }
        public Phrase(string[] phrase) {
            ix = new short[phrase.Length];
            for (int i = 0; i < phrase.Length; i++) ix[i] = PhraseProducer.GetWordIndex(phrase[i]);
        }
        public Phrase(string phrase) : this(phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { }

        public override bool Equals(object obj) {
            Phrase p = (Phrase)obj;
            if (p == null) return false;

            if (p.Length != ix.Length) return false;

            for (int i = 0; i < p.Length; i++) if (p.ix[i] != ix[i]) return false;

            return true;
        }

        public override int GetHashCode() {
            
            //  Slow?
            int result = 0;
            int shift = 0;
            for (int i = 0; i < ix.Length; i++)
            {
                shift = (shift + 11) % 21;
                result ^= (ix[i] + 1024) << shift;
            }
            return result;

            //  Slower?
            // return this.ToString().GetHashCode();
        }

        // public override string ToString() {
        //     return String.Join(' ', ix);
        // }

        public Phrase Clone() {
            short[] i = ix.Copy();
            return new Phrase(i);
        }

        public string[] ToWords() {
            string[] words = new string[ix.Length];
            for (int i = 0; i < ix.Length; i++) words[i] = PhraseProducer.GetWord(ix[i]);
            return words;
        }

        public string ToPhrase() {
            return String.Join(' ', this.ToWords());
        }

        public short this[int i] {
            get { return ix[i]; }
            set { ix[i] = value; }
        }

    }
}