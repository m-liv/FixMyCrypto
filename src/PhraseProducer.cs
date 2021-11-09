using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FixMyCrypto {
    class PhraseProducer {
        public BlockingCollection<Work> queue;
        int internalThreads;
        int valid = 0, invalid = 0, dupes = 0;
        string[] phrase;

        enum SwapMode {
            SameLetter,
            Similar,
            AnyLetter
        }
        private ParallelOptions parallelOptions;
        Stopwatch queueWaitTime = new Stopwatch();

        public PhraseProducer(BlockingCollection<Work> queue, string[] phrase) {
            this.queue = queue;
            this.phrase = phrase;
            this.internalThreads = Math.Max(Settings.Threads / 4, 1);
        }
        
        public void Finish() {
            Global.Done = true;
            queue.CompleteAdding();
        }
        private static ConcurrentDictionary<Phrase, byte> testedPhrases = new();
        private void TestPhrase(short[] phraseArray) {
            //  Check if phrase has valid BIP39 checksum

            (bool isValid, int hash) = Phrase.VerifyChecksum(phraseArray);
            
            if (isValid) {
                //  don't retest valid phrases
                Phrase p = new Phrase(phraseArray, hash);

                if (testedPhrases.ContainsKey(p)) {
                    dupes++;
                    return;
                }

                try {
                    testedPhrases[p] = 0;
                }
                catch (Exception) {
                    testedPhrases.Clear();
                    testedPhrases[p] = 0;
                }

                valid++;
                Work w = new Work(p, null);

                //  Enqueue
                queueWaitTime.Start();
                try {
                    queue.Add(w);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                finally {
                    queueWaitTime.Stop();
                }
            }
            else {
                invalid++;
            }
        }

        private void SwapTwo(short[] phrase) {
            // for (int i = 0; i < phrase.Length - 1; i++) {
            Parallel.For(0, phrase.Length - 1, this.parallelOptions, i => {

                for (int j = i + 1; j < phrase.Length; j++) {
                    short[] p = phrase.Copy();

                    short temp = p[i];
                    p[i] = p[j];
                    p[j] = temp;

                    TestPhrase(p);
                    if (Global.Done) return;
                }
            });
        }
        private void SwapThree(short[] phrase) {
            // for (int i = 0; i < phrase.Length - 1; i++) {
            Parallel.For(0, phrase.Length - 2, this.parallelOptions, i => {
                for (int j = i + 1; j < phrase.Length - 1; j++) {
                    
                    // Range range = GetRange(j + 1);

                    // for (int k = range.Start.Value; k < range.End.Value; k++) {
                    for (int k = j + 1; k < phrase.Length; k++) {
                        short[] p = phrase.Copy();

                        //  Swap ijk - jki
                        short temp = p[i];
                        p[i] = p[j];
                        p[j] = p[k];
                        p[k] = temp;

                        TestPhrase(p);
                        if (Global.Done) return;

                        //  Swap ijk - kij
                        p = phrase.Copy();

                        temp = p[i];
                        p[i] = p[k];
                        p[k] = p[j];
                        p[j] = temp;

                        TestPhrase(p);
                        if (Global.Done) return;                      
                    }
                }
            });
        }
        private void SwapFour(short[] phrase) {
            // for (int i = 0; i < phrase.Length - 1; i++) {
            Parallel.For(0, phrase.Length - 3, this.parallelOptions, i => {
                for (int j = i + 1; j < phrase.Length - 2; j++) {
                    for (int k = j + 1; k < phrase.Length - 1; k++) {

                        // Range range = GetRange(k + 1);

                        // for (int l = range.Start.Value; l < range.End.Value; l++) {
                        for (int l = k + 1; l < phrase.Length; l++) {
                            short[] p = phrase.Copy();

                            //  Swap ijkl - jkli
                            short temp = p[i];
                            p[i] = p[j];
                            p[j] = p[k];
                            p[k] = p[l];
                            p[l] = temp;

                            TestPhrase(p);
                            if (Global.Done) return;

                            //  Swap ijkl - klij
                            p = phrase.Copy();
                            temp = p[i];
                            p[i] = p[k];
                            p[l] = p[j];
                            p[j] = p[l];
                            p[k] = temp;

                            TestPhrase(p);
                            if (Global.Done) return;

                            //  Swap ijkl - lijk
                            p = phrase.Copy();
                            temp = p[i];
                            p[i] = p[l];
                            p[l] = p[k];
                            p[k] = p[j];
                            p[j] = temp;

                            TestPhrase(p);
                            if (Global.Done) return;
                        }             
                    }
                }
            });
        }
        private IList<short> GetReplacementWords(short word, SwapMode swapMode) {
            if (Wordlists.WordArray[word] == "*" || Wordlists.WordArray[word] == "?") {
                return Wordlists.SortedWords[word];
            }

            IList<short> words;

            switch (swapMode) {
                case SwapMode.Similar:
                words = Wordlists.WordsByMaxDistance[word];
                break;

                case SwapMode.AnyLetter:
                words = Wordlists.SortedWords[word];
                break;

                case SwapMode.SameLetter:
                default:
                char letter = Wordlists.WordArray[word][0];
                words = Wordlists.WordListByLetter[letter-'a'];
                break;
            }

            return words;
        }
        private void SwapTwoCOW(short[] phrase, SwapMode swapMode) {
            //  for (int i = 0; i < phrase.Length - 1; i++) {
            Parallel.For(0, phrase.Length - 1, this.parallelOptions, i => {
                if (Global.Done) return;

                if (i > 0) Log.Info($"PP S2C1W ({swapMode}) progress: {(100*i/phrase.Length)}%");
                
                // Range range = GetRange(i + 1);

                // for (int j = range.Start.Value; j < range.End.Value; j++) {
                for (int j = i + 1; j < phrase.Length; j++) {
                    short[] swapped = phrase.Copy();

                    short temp = swapped[i];
                    swapped[i] = swapped[j];
                    swapped[j] = temp;

                    /*  already tested by SwapTwo
                    //  Test swapped only
                    TestPhrase(swapped);
                    if (Global.done) return;
                    */

                    for (int k = 0; k < phrase.Length; k++) {
                        IList<short> words = GetReplacementWords(swapped[k], swapMode);

                        foreach (short word in words) {
                            //  COW on swapped
                            if (swapped[k] != word) {
                                short[] cow_swapped = swapped.Copy();
                                cow_swapped[k] = word;

                                TestPhrase(cow_swapped);
                                if (Global.Done) return;
                            }
                        } 
                    }
                }
            });
        }

        private void ChangeWords(short[] phrase, int depth, SwapMode swapMode, List<int> skip = null) {
            if (depth == 0) return;

            int start = 0;
            int end = phrase.Length;

            Parallel.For(start, end, this.parallelOptions, i => {
                if (Global.Done) return;
            // for (int i = start; i < end; i++) {
                if (skip != null && skip.Contains(i)) return;

                if (i > start && skip == null) Log.Info($"PP C{depth}W ({swapMode}) progress: {(100*(i-start)/(end-start))}%");

                IList<short> words = GetReplacementWords(phrase[i], swapMode);

                foreach (short word in words) {
                    if (word == phrase[i]) continue;

                    short[] cow = phrase.Copy();
                    cow[i] = word;

                    if (depth > 1) {
                        List<int> skip2 = new List<int>();
                        if (skip != null) skip2.AddRange(skip);
                        skip2.Add(i);

                        //  test recursive swaps
                        ChangeWords(cow, depth - 1, swapMode, skip2);
                    }
                    else {
                        //  test after current swap
                        TestPhrase(cow);
                        if (Global.Done) return;
                    }
                }
            });
        }

        private void ChangeWordsSwap(short[] phrase, int depth, SwapMode swapMode, List<int> skip = null) {
            if (depth == 0) return;

            int start = 0;
            int end = phrase.Length;

            Parallel.For(start, end, this.parallelOptions, i => {
            // for (int i = start; i < end; i++) {
                if (Global.Done) return;
                if (skip != null && skip.Contains(i)) return;

                if (i > start && skip == null) Log.Info($"PP C{depth}W+S ({swapMode}) progress: {(100*(i-start)/(end-start))}%");

                IList<short> words = GetReplacementWords(phrase[i], swapMode);

                foreach (short word in words) {
                    if (word == phrase[i]) continue;

                    short[] cow = phrase.Copy();
                    cow[i] = word;

                    if (depth > 1) {
                        List<int> skip2 = new List<int>();
                        if (skip != null) skip2.AddRange(skip);
                        skip2.Add(i);

                        //  test recursive swaps
                        ChangeWordsSwap(cow, depth - 1, swapMode, skip2);
                    }
                    else {
                        //  test after current swap
                        TestPhrase(cow);
                        if (Global.Done) return;

                        //  plus 1 swap

                        for (int j = 0; j < cow.Length - 1; j++) {
                            for (int k = j + 1; k < cow.Length; k++) {
                                short[] cowswap = cow.Copy();

                                short temp = cowswap[j];
                                cowswap[j] = cowswap[k];
                                cowswap[k] = temp;

                                TestPhrase(cowswap);
                                if (Global.Done) return;
                            }
                        }
                    }
                }
            });
        }

        private void SwapColumns(short[] phrase) {
            //  Transpose rows/columns
            for (int rowCount = 2; rowCount <= phrase.Length / 2; rowCount++) {
                int colCount = phrase.Length / rowCount;
                if (rowCount * colCount == phrase.Length) {
                    //  Transpose
                    Log.Debug($"transpose {rowCount}x{colCount}");
                    short[] t = new short[phrase.Length];

                    for (int row = 0; row < rowCount; row++) {
                        for (int col = 0; col < colCount; col++) {
                            t[col * rowCount + row] = phrase[row * colCount + col];
                        }
                    }

                    TestPhrase(t);
                    if (Global.Done) return;
                }
            }

            if (phrase.Length % 2 != 0) return;

            short[] p = new short[phrase.Length];

            //  Swap two columns i.e. 6 7 8 9 10 11 0 1 2 3 4 5

            for (int i = 0; i < phrase.Length / 2; i++) {
                p[i] = phrase[i + phrase.Length / 2];
            }

            for (int i = 0; i < phrase.Length / 2; i++) {
                p[i + phrase.Length / 2] = phrase[i];
            }

            TestPhrase(p);
            if (Global.Done) return;

            // Swap 2 col x N/2 rows, i.e. 0 6 1 7 2 8 3 9 4 10 5 11

            p = new short[phrase.Length];

            for (int i = 0; i < phrase.Length / 2; i++) {
                p[i] = phrase[2 * i];

                p[i + phrase.Length / 2] = phrase[2 * i + 1];
            }

            TestPhrase(p);
            if (Global.Done) return;
        }

        private void FixMissing(short[] phrase, int missing, int wrong, bool runAlgorithms, int difficulty, int threads) {
            if (Global.Done) return;

            if (missing == 0) {
                FixInvalid(phrase, wrong, wrong, runAlgorithms, SwapMode.Similar, difficulty, threads);
                return;
            }

            //  Try inserting every word into every position

            ParallelOptions po = new ParallelOptions();
            po.MaxDegreeOfParallelism = threads;

            Parallel.For(0, Wordlists.OriginalWordlist.Count, po, word => {
                if (Global.Done) return;
                for (int i = 0; i <= phrase.Length; i++) {
                    if (Global.Done) return;

                    short[] copy = new short[phrase.Length + 1];

                    if (i > 0) Array.Copy(phrase, 0, copy, 0, i);
                    copy[i] = (short)word;
                    if (phrase.Length - i > 0) Array.Copy(phrase, i, copy, i + 1, phrase.Length - i);

                    // Log.Debug($"try (missing = {missing}): {Phrase.ToPhrase(copy)}");

                    FixMissing(copy, missing - 1, wrong, runAlgorithms, difficulty, 1);
                }
            });
        }

        private void FixInvalid(short[] phrase, int depth, int maxDepth, bool runAlgorithms, SwapMode mode, int difficulty, int threads) {
            if (Global.Done) return;

            if (depth == 0) {
                TestPhrase(phrase);
                if (Global.Done) return;

                if (runAlgorithms) RunAlgorithms(phrase, difficulty, 1);
                
                return;
            }

            for (int i = 0; i < phrase.Length; i++) {
                if (Global.Done) return;

                if (phrase[i] < Wordlists.OriginalWordlist.Count) continue;

                IList<short> words = GetReplacementWords(phrase[i], mode);

                // int attempt = 0;

                ParallelOptions po = new ParallelOptions();
                po.MaxDegreeOfParallelism = threads;

                // Log.Info($"PP{threadNum}: Replace invalid word #{depth} ({words.Count})");

                Parallel.ForEach(words, po, replacement => {
                // foreach (int replacement in words) {
                    // attempt++;

                    if (Global.Done) return;

                    short[] fix = phrase.Copy();

                    // Log.Info($"PP{threadNum}: Replace invalid word {fix[i]} with {replacement} ({attempt}/{words.Count})");

                    fix[i] = replacement;

                    FixInvalid(fix, depth - 1, maxDepth, runAlgorithms, mode, difficulty, 1);
                });
            }
        }
        private void RunAlgorithms(short[] phrase, int difficulty, int threads) {
            // Log.Debug($"RunAlgorithms on phrase: {String.Join(' ', phrase)}");

            if (Global.Done) return;

            parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = threads;

            Stopwatch sw2 = new Stopwatch();

            //  instant
            Log.Info($"PP: Swap columns");
            sw2.Start();
            SwapColumns(phrase);
            sw2.Stop();
            Log.Info($"PP: Swap columns finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  <1s
            Log.Info($"PP: Swap any 2");
            sw2.Start();
            SwapTwo(phrase);
            sw2.Stop();
            Log.Info($"PP: Swap any 2 finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  3s
            Log.Info($"PP: Change 1 word (same letter)");
            sw2.Restart();
            ChangeWords(phrase, 1, SwapMode.SameLetter);
            sw2.Stop();
            Log.Info($"PP: Change 1 word (same letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  7s
            Log.Info($"PP: Swap any 3");
            sw2.Start();
            SwapThree(phrase);
            sw2.Stop();
            Log.Info($"PP: Swap any 3 finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  40s
            Log.Info($"PP: Swap any 4");
            sw2.Start();
            SwapFour(phrase);
            sw2.Stop();
            Log.Info($"PP: Swap any 4 finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  1 min
            Log.Info($"PP: Change 1 word (any letter)");
            sw2.Restart();
            ChangeWords(phrase, 1, SwapMode.AnyLetter);
            sw2.Stop();
            Log.Info($"PP: Change 1 word (any letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  2 mins for 12
            Log.Info($"PP: Swap 2, Change 1 (distance = {Settings.WordDistance})");
            sw2.Restart();
            SwapTwoCOW(phrase, SwapMode.Similar);
            sw2.Stop();
            Log.Info($"PP: Swap 2, Change 1 (distance = {Settings.WordDistance}) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  2-3mins for 12
            Log.Info($"PP: Change 2 words (distance = {Settings.WordDistance})");
            sw2.Restart();
            ChangeWords(phrase, 2, SwapMode.Similar);
            sw2.Stop();
            Log.Info($"PP: Change 2 words (distance = {Settings.WordDistance}) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            if (difficulty < 1) return;

            //  Advanced modes
            //  ?
            Log.Info($"PP: Swap 2, Change 2 (distance = {Settings.WordDistance})");
            sw2.Restart();
            ChangeWordsSwap(phrase, 2, SwapMode.Similar);
            sw2.Stop();
            Log.Info($"PP: Swap 2, Change 2 (distance = {Settings.WordDistance}) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  10 mins
            Log.Info($"PP: Swap 2, Change 1 (same letter)");
            sw2.Restart();
            SwapTwoCOW(phrase, SwapMode.SameLetter);
            sw2.Stop();
            Log.Info($"PP: Swap 2, Change 1 (same letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  2hrs
            Log.Info($"PP: Change 2 words (same letter)");
            sw2.Restart();
            ChangeWords(phrase, 2, SwapMode.SameLetter);
            sw2.Stop();
            Log.Info($"PP: Change 2 words (same letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  3hrs
            Log.Info($"PP: Swap 2, Change 1 (any letter)");
            sw2.Restart();
            SwapTwoCOW(phrase, SwapMode.AnyLetter);
            sw2.Stop();
            Log.Info($"PP: Swap 2, Change 1 (any letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            //  3-4hrs for 10
            Log.Info($"PP: Change 3 words (distance = {Settings.WordDistance})");
            sw2.Restart();
            ChangeWords(phrase, 3, SwapMode.Similar);
            sw2.Stop();
            Log.Info($"PP: Change 3 words (distance = {Settings.WordDistance}) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;
            
            if (difficulty < 2) return;

            //  SUPER ADVANCED MODE
            //  Long time?
            Log.Info($"PP: Change 4 words (distance = {Settings.WordDistance})");
            sw2.Restart();
            ChangeWords(phrase, 4, SwapMode.Similar);
            sw2.Stop();
            Log.Info($"PP: Change 4 words (distance = {Settings.WordDistance}) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            if (difficulty < 3) return;

            //  60 days?
            Log.Info($"PP: Change 2 words (any letter)");
            sw2.Restart();
            ChangeWords(phrase, 2, SwapMode.AnyLetter);
            sw2.Stop();
            Log.Info($"PP: Change 2 words (any letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;

            if (difficulty < 4) return;

            //  crazy time
            Log.Info($"PP: Change 3 words (any letter)");
            sw2.Restart();
            ChangeWords(phrase, 3, SwapMode.AnyLetter);
            sw2.Stop();
            Log.Info($"PP: Change 3 words (any letter) finished in {sw2.ElapsedMilliseconds/1000}s valid: {valid} invalid: {invalid} dupes: {dupes}");
            if (Global.Done) return;
        }
    
        public void ProduceWork() {
            Log.Debug("PP start");

            int wrongWords = 0, missingWords = 0;
            foreach (string word in this.phrase) {
                if (word == "?") {
                    missingWords++;
                }
                else if (!Wordlists.OriginalWordlist.Contains(word)) {
                    wrongWords++;
                    Log.Debug($"invalid word {wrongWords}: {word}");
                }
            }

            if (wrongWords > 0 || missingWords > 0) Log.Info($"Phrase contains {missingWords} missing and {wrongWords} invalid/unknown words");

            Phrase p = new Phrase(this.phrase);
            short[] phrase = p.Indices;

            Stopwatch sw1 = new Stopwatch();
            sw1.Start();

            if (missingWords > 0) {
                Log.Info($"PP replace {missingWords} missing words (no swaps/changes)");
                FixMissing(phrase.Slice(0, phrase.Length - missingWords), missingWords, wrongWords, false, Settings.Difficulty, this.internalThreads);

                if (!Global.Done) {
                    Log.Info($"PP replace {missingWords} missing words (+ swaps/changes)");
                    FixMissing(phrase.Slice(0, phrase.Length - missingWords), missingWords, wrongWords, true, Settings.Difficulty, this.internalThreads);
                }
            }
            else if (wrongWords == 0) {
                TestPhrase(phrase);

                //  Run all algorithms with a bit extra difficulty

                if (!Global.Done) RunAlgorithms(phrase, Settings.Difficulty + 1, this.internalThreads);
            }
            else {
                //  Try fixing invalid words only without any swaps/changes, starting with similar words
                Log.Info($"PP replace {wrongWords} invalid words with similar words (no swaps/changes)");
                FixInvalid(phrase, wrongWords, wrongWords, false, SwapMode.Similar, Settings.Difficulty, this.internalThreads);

                //  Try fixing invalid words plus swaps/changes
                if (!Global.Done) {
                    Log.Info($"PP replace {wrongWords} invalid words with similar words + swaps/changes");
                    FixInvalid(phrase, wrongWords, wrongWords, true, SwapMode.Similar, Settings.Difficulty, this.internalThreads);
                }

                //  Try any substitute for invalid words (no swaps/changes) if practical
                if (!Global.Done && wrongWords < 3) {
                    Log.Info($"PP replace {wrongWords} invalid words with any words");
                    FixInvalid(phrase, wrongWords, wrongWords, false, SwapMode.AnyLetter, Settings.Difficulty, this.internalThreads);
                }

                //  Last ditch effort
                if (!Global.Done) {
                    Log.Info($"PP replace {wrongWords} invalid words with any words + swaps/changes");
                    FixInvalid(phrase, wrongWords, wrongWords, true, SwapMode.AnyLetter, Settings.Difficulty + 1, this.internalThreads);
                }
            }

            sw1.Stop();
            Log.Info("PP done, valid: " + valid + " invalid: " + invalid + $", dupes: {dupes}, total time: {sw1.ElapsedMilliseconds/1000.0:F2}s, time/req: {((valid + invalid != 0) ? ((double)sw1.ElapsedMilliseconds/(valid+invalid)) : 0):F3}ms/req, queue wait: " + queueWaitTime.ElapsedMilliseconds/1000 + "s");
            Finish();
        }

    }
}