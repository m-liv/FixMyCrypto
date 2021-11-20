using System;
using System.Collections.Generic;

namespace FixMyCrypto {

    class Part : IEnumerable<string> {

        enum OpType {
            Ordered,
            Or,
            And
        }

        OpType opType;
        Part[] parts;
        string stringValue;
        bool optional;

        //  for graphviz only
        string range;

        private static bool IsStartDelimiter(char c) {
            switch (c) {
                case '[':
                case '(':
                return true;

                default:
                return false;
            }
        }
        private static char GetEndDelimiter(char c) {
            switch (c) {
                case '[':
                return ']';

                case '(':
                return ')';

                default:
                return ' ';
            }
        }

        private bool IsOperator(char c) {
            switch (c) {
                case '&':
                case '|':

                return true;

                default:

                return false;
            }
        }

        private char GetOuterOperator(string set) {
            int depth = 0;
            char blockType = ' ';
            for (int i = 0; i < set.Length - 1; i++) {
                if (depth == 0 && IsStartDelimiter(set[i]) && set.Contains(GetEndDelimiter(set[i]))) {
                    depth++;
                    blockType = set[i];
                }
                else if (depth == 1 && set[i] == GetEndDelimiter(blockType)) {
                    depth--;
                    blockType = ' ';
                }
                else if (depth == 0 && IsOperator(set[i]) && set[i+1] == set[i]) {
                    //  found top level operator

                    return set[i];
                }
                else {
                    if (set[i] == blockType) depth++;
                    if (set[i] == GetEndDelimiter(blockType)) depth--;
                }
            }

            return ' ';
        }
        private List<Part> CreateBooleanSet(string set) {
            // Log.Debug($"bool set: {set}");

            List<Part> parts = new List<Part>();

            char op = GetOuterOperator(set);

            if (op == '&') {
                this.opType = OpType.And;
                // Log.Debug($"&& set: {set}");
                string[] andParts = set.Split("&&");

                foreach (string part in andParts) {
                    // Log.Debug($"&& part: {part}");
                    parts.Add(new Part(part));
                }
            }
            else if (op == '|') {
                this.opType = OpType.Or;
                // Log.Debug($"|| set: {set}");
                string[] orParts = set.Split("||");
                foreach (string part in orParts) {
                    // Log.Debug($"|| part: {part}");
                    parts.Add(new Part(part));
                }
            }
            else {
                this.opType = OpType.Ordered;
                parts.Add(new Part(set));
            }

            return parts;
        }

        private List<Part> CreateOptionSet(string set) {
            bool exclude = false;
            this.opType = OpType.Or;
            this.range = set;

            if (set.StartsWith("^")) {
                set = set.Substring(1);
                if (set.Length > 0 && set[0] != '^') {
                    exclude = true;
                }
            }
            
            int start = 0;

            if (set.Length == 0) return new List<Part>();

            List<string> items = new List<string>();
            
            while (start < set.Length)
            {
                int dash = set.IndexOf('-', start + 1);

                if (dash <= 0 || dash >= set.Length - 1)
                    break;

                string p = set.Substring(start, dash - start - 1);

                if (p.Length > 0) items.Add(p);

                char a = set[dash - 1];
                char z = set[dash + 1];

                for (var i = a; i <= z; ++i)
                    items.Add($"{i}");

                start = dash + 2;
            }

            for (int i = start; i < set.Length; i++) items.Add($"{set[i]}");

            if (exclude) {
                List<string> rValues = new List<string>();
                for (char i = (char)0x20; i < 0x7f; i++) {
                    if (!items.Contains($"{i}")) rValues.Add($"{i}");
                }
                items = rValues;
            }

            List<Part> parts = new List<Part>();

            foreach (string s in items) {
                parts.Add(new Part(s));
            }

            return parts;
        }


        private bool IsSet(string set, char start, char end) {
            if (!set.StartsWith(start) || !set.EndsWith(end)) return false;

            int depth = 0;
            for (int i = 1; i < set.Length - 1; i++) {
                if (set[i] == start) depth++;
                if (set[i] == end) depth--;

                if (depth < 0) return false;
            }

            return (depth == 0);
        }

        private bool IsBooleanSet(string set) {
            return IsSet(set, '(', ')');
        }
        private bool IsOptionSet(string set) {
            return IsSet(set, '[', ']');
        }

        public Part(string set) {
            // Log.Debug($"Part: {set}");

            List<Part> values = new List<Part>();
            stringValue = null;

            if ((set.StartsWith("(") && set.EndsWith(")?")) || (set.StartsWith("[") && set.EndsWith("]?"))) {
                optional = true;
                set = set.Substring(0, set.Length - 1);
            }

            if (IsBooleanSet(set)) {
                set = set.Substring(1, set.Length - 2);

                values.AddRange(CreateBooleanSet(set));
            }
            else if (IsOptionSet(set)) {
                set = set.Substring(1, set.Length - 2);

                values.AddRange(CreateOptionSet(set));
            }
            else if (((set.Contains("(") && set.Contains(")")) || (set.Contains("[") && set.Contains("]")))) {
                string current = "";
                int depth = 0;
                char blockType = ' ';
                this.opType = OpType.Ordered;

                for (int i = 0; i < set.Length; i++) {
                    if (depth == 0 && IsStartDelimiter(set[i])) {
                        if (current.Length > 0) {
                            Part p = new Part(current);
                            values.Add(p);
                            current = "";
                        }

                        current += set[i];

                        blockType = set[i];
                        depth++;
                    }
                    else if (depth == 1 && set[i] == GetEndDelimiter(blockType)) {
                        current += set[i];

                        if (i + 1 < set.Length) {
                            switch (set[i+1]) {
                                case '?':

                                current += set[i+1];
                                i += 1;
                                break;
                            }
                        }

                        Part p = new Part(current);
                        values.Add(p);
                        current = "";

                        depth--;
                        blockType = ' ';
                    }
                    else {
                        current += set[i];
                        if (set[i] == blockType) depth++;
                        if (set[i] == GetEndDelimiter(blockType)) depth--;
                    }
                }

                if (depth > 0) {
                    throw new Exception($"Invalid passphrase format (check for unescaped characters): {set}");
                }

                if (current.Length > 0) values.Add(new Part(current));
            }
            else {
                this.stringValue = set;
            }

            this.parts = values.ToArray();
        }

        private IEnumerable<string> Recurse(string prefix, Part[] parts, int start = 0) {
             if (start >= parts.Length) {
                yield return prefix;
                yield break;
            }

            foreach (string p in parts[start]) {
                foreach (string r in Recurse(prefix + p, parts, start + 1)) {
                    yield return r;
                }
            }
        }
        private IEnumerable<string> Permute(Part[] parts, int start = 0) {
            if (start >= parts.Length) {
                foreach (string r in Recurse("", parts)) {
                    // Log.Debug($"Recurse returned: {r}");
                    yield return r;
                }
            }

            for (int i = start; i < parts.Length; i++) {
                Part[] p = (Part[])parts.Clone();

                Part tmp = p[start];
                p[start] = p[i];
                p[i] = tmp;

                foreach (string s in Permute(p, start + 1)) {
                    // Log.Debug($"Permute returned: {s}");
                    yield return s;
                }
            }
        }

        public IEnumerator<string> GetEnumerator() {
            if (this.optional) {
                yield return "";
            }
            
            if (this.stringValue != null) {
                yield return this.stringValue;
                yield break;
            }

            if (this.opType == OpType.Ordered) {
                foreach (string r in Recurse("", this.parts)) yield return r;
            }
            else if (this.opType == OpType.And) {
                foreach (string p in Permute(this.parts)) {
                    yield return p;
                }
            }
            else {
                //  OR
                foreach (Part p in this.parts) {
                    foreach (string s in p) {
                        yield return s;
                    }
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public int GetCount() {
            if (this.stringValue != null) return 1;

            int count;

            if (this.opType == OpType.Or) {
                count = 0;
                foreach (Part p in this.parts) {
                    count += p.GetCount();
                }
            }
            else if (this.opType == OpType.And) {
                count = Utils.Factorial(this.parts.Length);
                foreach (Part p in this.parts) {
                    count *= p.GetCount();
                }
            }
            else {
                count = 1;
                foreach (Part p in this.parts) {
                    count *= p.GetCount();
                }
            }


            if (this.optional) count += 1;

            return count;
        }

        public override string ToString() {
            string label = this.stringValue;

            if (label == null) {
                if (this.opType == OpType.Ordered) {
                    label = "";
                    foreach (Part p in this.parts) {
                        label += p.ToString();
                    }
                }
                else if (this.range != null) {
                    label = $"[{this.range}]";
                    if (optional) label += "?";
                }
                else if (this.opType == OpType.Or) {
                    label = "(" + String.Join("||", (object[])this.parts) + ")";
                    if (optional) label += "?";
                }
                else if (this.opType == OpType.And) {
                    label = "(" + String.Join("&&", (object[])this.parts) + ")";
                    if (optional) label += "?";
                }
            }

            return label;
        }

        private string EscapeString(string s) {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "&lt;").Replace("|", "\\|");
        }

        public string GetTopology(bool root = false, string parentLabel = "") {
            string label = this.ToString();
            int id = (parentLabel + label).GetHashCode();
            string nodes;

            if (opType == OpType.Ordered && parts.Length > 1) {
                string recordLabel = "";
                int record = 0;
                foreach (Part p in parts) {
                    if (recordLabel.Length > 0) recordLabel += "|";
                    recordLabel += $"<f{record}> {EscapeString(p.ToString())}";
                    record++;
                }
                //  add record node
                nodes = $"\t\"{id:X8}\" [label=\"{recordLabel}\"{(root ? " root=\"true\"" : "")} ordering=\"out\" shape=record]\n";

                for (int i = 0; i < parts.Length; i++) {
                    Part p = parts[i];
                    //  skip single string parts, since they are contained in the record label
                    if (p.stringValue != null) continue;

                    nodes += p.GetTopology(parentLabel: label);
                    string child = p.ToString();
                    int childId =  (label + child).GetHashCode();
                    string target = p.opType == OpType.Ordered && p.parts.Length > 1 ? ":f0" : "";

                    //  add parent link to child
                    nodes += $"\t\"{id:X8}\":f{i} -> \"{childId:X8}{target}\":n\n";
                }
            }
            else {
                string readableLabel = label;
                if (parts.Length > 1 && range == null) {
                    readableLabel = "";
                    foreach (Part p in parts) {
                        if (readableLabel.Length > 0) readableLabel += opType == OpType.And ? " AND " : " OR ";
                        readableLabel += "\"" + p.ToString() + "\"";
                    }
                }
                string shape = "oval";
                if (opType == OpType.Or && parts.Length > 1) shape = "invhouse";
                if (opType == OpType.And) shape = "box style=rounded";
                nodes = $"\t\"{id:X8}\" [label=\"{EscapeString(readableLabel)}\"{(root ? " root=\"true\"" : "")}{(opType == OpType.Ordered && parts.Length > 1 ? " ordering=\"out\"" : "")} shape={shape}]\n";

                foreach (Part p in this.parts) {
                    nodes += p.GetTopology(parentLabel: label);
                    string child = p.ToString();
                    int childId =  (label + child).GetHashCode();
                    string target = p.opType == OpType.Ordered && p.parts.Length > 1 ? ":f0" : "";
                    nodes += $"\t\"{id:X8}\"\t->\t\"{childId:X8}\"{target}:n\n";
                }

                if (this.optional) {
                    Part p = new Part("");
                    string child = p.ToString();
                    int childId =  (label + child).GetHashCode();
                    nodes += p.GetTopology(parentLabel: label);
                    nodes += $"\t\"{id:X8}\"\t->\t\"{childId:X8}\":n\n";
                }
            }

            return nodes;
        }
    }

    class Passphrase : IEnumerable<string> {
        Part root;
        string toFuzz;
        int depth = 1;

        public Passphrase(string passphrase, int fuzzDepth = 1) {
            if (passphrase == null) return;

            if (passphrase.StartsWith("{{") && passphrase.EndsWith("}}")) {
                toFuzz = passphrase.Substring(2, passphrase.Length - 4);
                depth = fuzzDepth;
            }
            else {
                root = new Part(passphrase);
            }
        }

        public IEnumerator<string> GetEnumerator() {
            if (root != null) {
                foreach (string r in root) yield return r;
            }
            else if (toFuzz != null) {
                foreach (string r in Fuzz(toFuzz, depth)) yield return r;
            }
            else {
                yield return "";
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public override string ToString() {
            throw new NotSupportedException();
        }

        public int GetCount() {
            if (root != null) return root.GetCount();

            if (depth == 1) return
                    toFuzz.Length                                   //  Deletions
                    + (toFuzz.Length * (0x7f - 0x20 - 1))           //  Substitutions
                    + ((toFuzz.Length + 1) * (0x7f - 0x20))         //  Insertions
                    + (toFuzz.Length * (toFuzz.Length - 1) / 2);    //  Transpositions

            //  TODO: Better way to count permutations when depth > 1
            //  It isn't GetCount(depth: 1) ^ 2 due to insertions/deletions changing the length
            int count = 0;
            foreach (string r in Fuzz(toFuzz, depth)) count++;
            return count;
        }   

        private IEnumerable<string> Fuzz(string src, int depth) {
            if (depth == 0) {
                yield return src;
                yield break;
            }

            //  Deletions
            for (int i = 0; i < src.Length; i++) {
                string test = src.Substring(0, i) + src.Substring(i + 1);
                // Log.Debug($"delete {i}: {test}");
                foreach (string r in Fuzz(test, depth - 1)) yield return r;
            }

            //  Substitutions
            for (int i = 0; i < src.Length; i++) {
                for (byte c = 0x20; c < 0x7f; c++) {
                    if (src[i] == (char)c) continue;

                    string test = src.Substring(0, i) + (char)c + src.Substring(i + 1);
                    // Log.Debug($"sub {i} with {(char)c}: {test}");
                    foreach (string r in Fuzz(test, depth - 1)) yield return r;
                }
            }

            //  Insertions
            for (int i = 0; i <= src.Length; i++) {
                for (byte c = 0x20; c < 0x7f; c++) {
                    string test = src.Substring(0, i) + (char)c + src.Substring(i);
                    // Log.Debug($"insert {i} with {(char)c}: {test}");
                    foreach (string r in Fuzz(test, depth - 1)) yield return r;
                }
            }

            //  Transpositions
            for (int i = 0; i < src.Length - 1; i++) {
                for (int j = i + 1; j < src.Length; j++) {
                    char[] c = src.ToCharArray();

                    char tmp = c[i];
                    c[i] = c[j];
                    c[j] = tmp;

                    string test = new string(c);
                    // Log.Debug($"transpose {i} {j}: {test}");
                    foreach (string r in Fuzz(test, depth - 1)) yield return r;
                }
            }
        }

        public void WriteTopologyFile(string path) {
            string topology = GetTopology();

            System.IO.File.WriteAllText(path, topology);
        }

        private string GetTopology() {

            //  https://graphviz.org/doc/info/lang.html

            string topology = "digraph G {\n\toverlap = false\n\tconcentrate = false\n";

            if (root != null) {
                topology += root.GetTopology(true);
            }
            else {
                //  dummy topology for passphrase fuzz mode

                Part p = new Part("{{" + toFuzz + "}}");

                topology += p.GetTopology(true);
            }

            topology += "}\n";

            return topology;
        }
    }

}