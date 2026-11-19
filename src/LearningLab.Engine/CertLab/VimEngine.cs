// Cert Lab - the text editors of the simulated machines (VimEngine: moved from App Hub's Editor.cs; VimView and
// EditorHost, which draw it, stay with the screens): a vi / vim with its modes (normal, insert, command line,
// visual line), counts, motions, operators, undo, search and :s substitution, and a small nano. The vi engine
// works on keys as strings so tests can drive it without a window; VimView draws it in the terminal's place.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class VimEngine
    {
        public List<string> L = new List<string>();
        public int Row, Col;
        public string Mode = "normal";      // normal, insert, replace, cmd, search, visual
        public string CmdLine = "";
        public string Message = "";
        public bool Modified, Done, Saved, ReadOnly, Number;
        public string Path, Result;
        string pending = "";
        string reg = ""; bool regLines;
        readonly List<string[]> undo = new List<string[]>();
        readonly List<string[]> redo = new List<string[]>();
        string lastSearch = ""; bool searchBack;
        string lastChange = "";
        int visualStart;
        public Action<string> OnWrite;   // :w while the editor stays open
        public int Top;

        public VimEngine(string path, string text, bool readOnly)
        {
            Path = path; ReadOnly = readOnly;
            bool isNew = text == null;
            var t = (text ?? "").Replace("\r", "");
            if (t.EndsWith("\n", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
            L = t.Split('\n').ToList();
            if (L.Count == 0) L.Add("");
            Message = "\"" + path + "\"" + (isNew ? " [New]" : " " + L.Count + "L, " + (text ?? "").Length + "B") + (readOnly ? " [readonly]" : "");
        }

        public string Text { get { return string.Join("\n", L.ToArray()) + "\n"; } }
        string Cur { get { return L[Row]; } set { L[Row] = value; } }
        void Snap() { undo.Add(L.ToArray()); if (undo.Count > 200) undo.RemoveAt(0); redo.Clear(); }
        void Clamp()
        {
            if (Row < 0) Row = 0; if (Row >= L.Count) Row = L.Count - 1;
            int max = Mode == "insert" ? Cur.Length : Math.Max(0, Cur.Length - 1);
            if (Col > max) Col = max; if (Col < 0) Col = 0;
        }

        /// <summary>A key: a character, or Esc, Enter, Backspace, Delete, Left, Right, Up, Down, Home, End, PageUp, PageDown, Tab, C-r, C-c.</summary>
        public void Key(string k)
        {
            if (Done) return;
            if (k == "C-c" && Mode != "normal") k = "Esc";
            switch (Mode)
            {
                case "insert": case "replace": InsertKey(k); break;
                case "cmd": case "search": CmdKey(k); break;
                case "visual": VisualKey(k); break;
                default: NormalKey(k); break;
            }
            Clamp();
        }
        public void Keys(string s)
        {
            // <Esc>, <CR>, <BS> notation for tests
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '<') { int e = s.IndexOf('>', i); if (e > 0) { string n = s.Substring(i + 1, e - i - 1); Key(n == "CR" ? "Enter" : n == "BS" ? "Backspace" : n == "Esc" ? "Esc" : n); i = e + 1; continue; } }
                Key(s[i].ToString()); i++;
            }
        }

        /// <summary>Pasted text (the terminal's paste): goes in at the cursor in insert mode, before the cursor in
        /// normal mode (as vim does with a terminal's bracketed paste), and its first line onto the : or / line.
        /// No auto-indent, tabs kept.</summary>
        public void Paste(string text)
        {
            if (Done || string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            if (Mode == "cmd" || Mode == "search") { int nl = text.IndexOf('\n'); CmdLine += nl >= 0 ? text.Substring(0, nl) : text; return; }
            if (Mode == "replace") { foreach (char ch in text) InsertKey(ch == '\n' ? "Enter" : ch.ToString()); Clamp(); return; }
            bool insert = Mode == "insert";
            if (!insert) { Mode = "normal"; pending = ""; ReadOnlyCheck(); Snap(); }   // insert mode took its undo step when it began
            var parts = text.Split('\n');
            int col = Math.Min(Col, Cur.Length);
            string head = Cur.Substring(0, col), tail = Cur.Substring(col);
            if (parts.Length == 1) { Cur = head + parts[0] + tail; Col = col + parts[0].Length; }
            else
            {
                Cur = head + parts[0];
                for (int i = 1; i < parts.Length; i++) L.Insert(Row + i, parts[i] + (i == parts.Length - 1 ? tail : ""));
                Row += parts.Length - 1;
                Col = parts[parts.Length - 1].Length;
            }
            Modified = true;
            if (!insert) Col = Math.Max(0, Col - 1);
            Clamp();
        }

        void InsertKey(string k)
        {
            switch (k)
            {
                case "Esc": Mode = "normal"; Col = Math.Max(0, Col - 1); Message = ""; return;
                case "Enter": { string rest = Cur.Substring(Math.Min(Col, Cur.Length)); string ind = Regex.Match(Cur, "^\\s*").Value; Cur = Cur.Substring(0, Math.Min(Col, Cur.Length)); L.Insert(Row + 1, rest); Row++; Col = 0; Modified = true; return; }
                case "Backspace": if (Col > 0) { Cur = Cur.Remove(Col - 1, 1); Col--; Modified = true; } else if (Row > 0) { int c = L[Row - 1].Length; L[Row - 1] += Cur; L.RemoveAt(Row); Row--; Col = c; Modified = true; } return;
                case "Delete": if (Col < Cur.Length) { Cur = Cur.Remove(Col, 1); Modified = true; } else if (Row < L.Count - 1) { Cur += L[Row + 1]; L.RemoveAt(Row + 1); Modified = true; } return;
                case "Left": if (Col > 0) Col--; return;
                case "Right": if (Col < Cur.Length) Col++; return;
                case "Up": if (Row > 0) Row--; return;
                case "Down": if (Row < L.Count - 1) Row++; return;
                case "Home": Col = 0; return;
                case "End": Col = Cur.Length; return;
                case "Tab": k = "\t"; break;
                case "PageUp": case "PageDown": return;
            }
            if (k.Length != 1) return;
            if (Mode == "replace" && Col < Cur.Length) Cur = Cur.Remove(Col, 1).Insert(Col, k);
            else Cur = Cur.Insert(Math.Min(Col, Cur.Length), k);
            Col++;
            Modified = true;
        }

        void CmdKey(string k)
        {
            if (k == "Esc") { Mode = "normal"; CmdLine = ""; return; }
            if (k == "Backspace") { if (CmdLine.Length == 0) { Mode = "normal"; return; } CmdLine = CmdLine.Substring(0, CmdLine.Length - 1); return; }
            if (k == "Enter")
            {
                string c = CmdLine; CmdLine = "";
                if (Mode == "search") { Mode = "normal"; if (c.Length > 0) lastSearch = c; searchBack = false; Search(false); return; }
                Mode = "normal";
                Ex(c);
                return;
            }
            if (k.Length == 1) CmdLine += k;
        }

        void Search(bool reverse)
        {
            if (lastSearch.Length == 0) { Message = "E35: No previous regular expression"; return; }
            bool back = searchBack != reverse;
            Regex rx;
            try { rx = new Regex(Cmd.PosixToNet(lastSearch, false)); } catch (ArgumentException) { Message = "E486: Pattern not found: " + lastSearch; return; }
            int n = L.Count;
            for (int i = 0; i <= n; i++)
            {
                int r = back ? ((Row - i) % n + n) % n : (Row + i) % n;
                var line = L[r];
                if (!back)
                {
                    int from = i == 0 ? Col + 1 : 0;
                    var m = from <= line.Length ? rx.Match(line, Math.Min(from, line.Length)) : Match.Empty;
                    if (m.Success) { if (r < Row || (r == Row && i > 0)) Message = "search hit BOTTOM, continuing at TOP"; Row = r; Col = m.Index; return; }
                }
                else
                {
                    var ms = rx.Matches(line).Cast<Match>().Where(m => i > 0 || m.Index < Col).ToList();
                    if (ms.Count > 0) { Row = r; Col = ms.Last().Index; return; }
                }
            }
            Message = "E486: Pattern not found: " + lastSearch;
        }

        public void Ex(string c)
        {
            c = c.Trim();
            if (c.Length == 0) return;
            var num = Regex.Match(c, "^(\\d+)$");
            if (num.Success) { Row = Math.Max(0, Math.Min(L.Count - 1, int.Parse(num.Value) - 1)); Col = 0; return; }
            if (c == "$") { Row = L.Count - 1; return; }
            var sub = Regex.Match(c, "^(%|\\d+(,\\d+)?|\\.)?s(.)(.*)$");
            if (sub.Success) { Substitute(sub.Groups[1].Value, sub.Groups[3].Value[0], sub.Groups[4].Value); return; }
            var del = Regex.Match(c, "^(%|\\d+(,\\d+)?)?d$");
            if (del.Success && c != "") { Snap(); int a, b; Range(del.Groups[1].Value, out a, out b); reg = string.Join("\n", L.Skip(a).Take(b - a + 1).ToArray()); regLines = true; L.RemoveRange(a, b - a + 1); if (L.Count == 0) L.Add(""); Row = Math.Min(a, L.Count - 1); Modified = true; return; }
            string cmd = c, arg = "";
            int sp = c.IndexOf(' ');
            if (sp > 0) { cmd = c.Substring(0, sp); arg = c.Substring(sp + 1).Trim(); }
            switch (cmd)
            {
                case "w": case "w!": case "write": Write(arg, cmd.EndsWith("!", StringComparison.Ordinal)); return;
                case "wq": case "wq!": case "x": case "x!": case "xit": case "exit":
                    if ((cmd.StartsWith("x", StringComparison.Ordinal) || cmd == "exit") && !Modified) { Done = true; return; }
                    if (Write(arg, cmd.EndsWith("!", StringComparison.Ordinal))) Done = true; return;
                case "q": case "quit":
                    if (Modified) { Message = "E37: No write since last change (add ! to override)"; return; }
                    Done = true; return;
                case "q!": case "quit!": case "cq": Done = true; return;
                case "qa": case "qa!": case "wqa": case "xa": if (cmd == "wqa" || cmd == "xa") Write("", false); Done = true; return;
                case "e!": Message = "(Cert Lab: :e! is not available; use u to undo)"; return;
                case "set": case "se":
                    if (arg == "nu" || arg == "number") Number = true;
                    else if (arg == "nonu" || arg == "nonumber") Number = false;
                    else if (arg.StartsWith("paste", StringComparison.Ordinal) || arg.StartsWith("nopaste", StringComparison.Ordinal) || arg.StartsWith("hls", StringComparison.Ordinal) || arg.StartsWith("nohls", StringComparison.Ordinal) || arg.StartsWith("ic", StringComparison.Ordinal) || arg.StartsWith("ts", StringComparison.Ordinal) || arg.StartsWith("list", StringComparison.Ordinal) || arg.StartsWith("ai", StringComparison.Ordinal) || arg.StartsWith("expandtab", StringComparison.Ordinal)) { }
                    else Message = "E518: Unknown option: " + arg;
                    return;
                case "noh": case "nohlsearch": return;
                case "syntax": return;
                case "help": case "h": Message = "Cert Lab vi: i insert, Esc normal mode, :w save, :q quit, :wq save and quit, :q! quit without saving, dd delete line, u undo"; return;
            }
            Message = "E492: Not an editor command: " + c;
        }

        void Range(string spec, out int a, out int b)
        {
            if (spec == "%") { a = 0; b = L.Count - 1; return; }
            if (spec.Length == 0 || spec == ".") { a = b = Row; return; }
            var p = spec.Split(',');
            a = Math.Max(0, int.Parse(p[0]) - 1); b = p.Length > 1 ? Math.Min(L.Count - 1, int.Parse(p[1]) - 1) : a;
            if (a > b) { int t = a; a = b; b = t; }
        }

        void Substitute(string range, char d, string rest)
        {
            var parts = new List<string>(); var sb = new StringBuilder();
            for (int i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '\\' && i + 1 < rest.Length && rest[i + 1] == d) { sb.Append(d); i++; continue; }
                if (rest[i] == d) { parts.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(rest[i]);
            }
            parts.Add(sb.ToString());
            if (parts.Count < 2) { Message = "E486: Pattern not found"; return; }
            string pat = parts[0].Length == 0 ? lastSearch : parts[0], rep = parts[1], flags = parts.Count > 2 ? parts[2] : "";
            Regex rx;
            try { rx = new Regex(Cmd.PosixToNet(pat, false), flags.Contains("i") ? RegexOptions.IgnoreCase : RegexOptions.None); } catch (ArgumentException) { Message = "E486: Pattern not found: " + pat; return; }
            int a, b; Range(range, out a, out b);
            Snap();
            int count = 0, lines = 0;
            for (int r = a; r <= b; r++)
            {
                int n = 0;
                string res = rx.Replace(L[r], m => { if (!flags.Contains("g") && n > 0) return m.Value; n++; return rep.Replace("&", m.Value).Replace("\\n", "\n"); });
                if (n > 0) { count += n; lines++; L[r] = res; Row = r; }
            }
            if (count == 0) { undo.RemoveAt(undo.Count - 1); Message = "E486: Pattern not found: " + pat; return; }
            // replacements with \n become several lines
            for (int r = L.Count - 1; r >= 0; r--) if (L[r].Contains("\n")) { var sp = L[r].Split('\n'); L.RemoveAt(r); L.InsertRange(r, sp); }
            Modified = true;
            if (count > 1) Message = count + " substitutions on " + lines + " line" + (lines == 1 ? "" : "s");
        }

        bool Write(string file, bool force)
        {
            if (ReadOnly && !force) { Message = "E45: 'readonly' option is set (add ! to override)"; return false; }
            if (!string.IsNullOrEmpty(file)) Path = file;
            if (string.IsNullOrEmpty(Path) || Path == "[No Name]") { Message = "E32: No file name"; return false; }
            Result = Text; Saved = true;
            if (OnWrite != null)
            {
                try { OnWrite(Result); }
                catch (Exception e) { Message = "\"" + Path + "\" E212: Can't open file for writing" + (e.Message.Length > 0 ? " (" + e.Message + ")" : ""); Saved = false; return false; }
            }
            Modified = false;
            Message = "\"" + Path + "\" " + L.Count + "L, " + Result.Length + "B written";
            return true;
        }

        void VisualKey(string k)
        {
            switch (k)
            {
                case "Esc": Mode = "normal"; return;
                case "j": case "Down": if (Row < L.Count - 1) Row++; return;
                case "k": case "Up": if (Row > 0) Row--; return;
                case "G": Row = L.Count - 1; return;
                case "d": case "x": { int a = Math.Min(visualStart, Row), b = Math.Max(visualStart, Row); Snap(); reg = string.Join("\n", L.Skip(a).Take(b - a + 1).ToArray()); regLines = true; L.RemoveRange(a, b - a + 1); if (L.Count == 0) L.Add(""); Row = Math.Min(a, L.Count - 1); Modified = true; Mode = "normal"; return; }
                case "y": { int a = Math.Min(visualStart, Row), b = Math.Max(visualStart, Row); reg = string.Join("\n", L.Skip(a).Take(b - a + 1).ToArray()); regLines = true; Row = a; Mode = "normal"; Message = (b - a + 1) + " lines yanked"; return; }
                case ">": case "<": { int a = Math.Min(visualStart, Row), b = Math.Max(visualStart, Row); Snap(); for (int r = a; r <= b; r++) L[r] = k == ">" ? "\t" + L[r] : L[r].StartsWith("\t", StringComparison.Ordinal) ? L[r].Substring(1) : Regex.Replace(L[r], "^ {1,8}", ""); Modified = true; Mode = "normal"; return; }
            }
        }

        static bool IsWord(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
        int WordFwd(int r, int c, bool end)
        {
            string s = L[r];
            if (end) { c++; while (c < s.Length && char.IsWhiteSpace(s[c])) c++; if (c >= s.Length) return s.Length - 1; bool w = IsWord(s[c]); while (c + 1 < s.Length && !char.IsWhiteSpace(s[c + 1]) && IsWord(s[c + 1]) == w) c++; return c; }
            if (c < s.Length) { bool w = IsWord(s[c]); bool sp = char.IsWhiteSpace(s[c]); if (!sp) while (c < s.Length && !char.IsWhiteSpace(s[c]) && IsWord(s[c]) == w) c++; }
            while (c < s.Length && char.IsWhiteSpace(s[c])) c++;
            return c;
        }

        void NormalKey(string k)
        {
            Message = Message.StartsWith("E", StringComparison.Ordinal) || Message.StartsWith("search", StringComparison.Ordinal) ? Message : "";
            string p = pending + k;
            var m = Regex.Match(p, "^(\\d*)(.*)$");
            int count = m.Groups[1].Value.Length > 0 && m.Groups[1].Value != "0" ? int.Parse(m.Groups[1].Value) : 1;
            bool hasCount = m.Groups[1].Value.Length > 0 && m.Groups[1].Value != "0";
            string c = m.Groups[2].Value;
            if (m.Groups[1].Value == "0" && c.Length == 0) { Col = 0; pending = ""; return; }
            if (c.Length == 0) { pending = p; return; }
            pending = "";
            switch (c)
            {
                case "Esc": return;
                case "h": case "Left": case "Backspace": Col -= count; return;
                case "l": case "Right": case " ": Col += count; return;
                case "j": case "Down": case "Enter": case "+": Row += count; if (c == "Enter" || c == "+") Col = Regex.Match(L[Math.Min(Row, L.Count - 1)], "^\\s*").Length; return;
                case "k": case "Up": case "-": Row -= count; return;
                case "PageDown": Row += 20; return;
                case "PageUp": Row -= 20; return;
                case "0": case "Home": Col = 0; return;
                case "^": Col = Regex.Match(Cur, "^\\s*").Length; return;
                case "$": case "End": Col = Math.Max(0, Cur.Length - 1); return;
                case "w": for (int i = 0; i < count; i++) { int nc = WordFwd(Row, Col, false); if (nc >= Cur.Length && Row < L.Count - 1) { Row++; Col = Regex.Match(Cur, "^\\s*").Length; } else Col = nc; } return;
                case "e": for (int i = 0; i < count; i++) Col = WordFwd(Row, Col, true); return;
                case "b": for (int i = 0; i < count; i++) { if (Col == 0 && Row > 0) { Row--; Col = Cur.Length; } int cc = Col - 1; while (cc > 0 && char.IsWhiteSpace(Cur[cc])) cc--; if (cc >= 0 && cc < Cur.Length) { bool w = IsWord(Cur[cc]); while (cc > 0 && !char.IsWhiteSpace(Cur[cc - 1]) && IsWord(Cur[cc - 1]) == w) cc--; } Col = Math.Max(0, cc); } return;
                case "gg": Row = hasCount ? count - 1 : 0; Col = 0; return;
                case "G": Row = hasCount ? count - 1 : L.Count - 1; Col = 0; return;
                case "g": pending = p; return;
                case "i": if (!ReadOnlyCheck()) { Snap(); Mode = "insert"; } return;
                case "a": if (!ReadOnlyCheck()) { Snap(); Mode = "insert"; Col = Math.Min(Col + 1, Cur.Length); } return;
                case "I": if (!ReadOnlyCheck()) { Snap(); Mode = "insert"; Col = Regex.Match(Cur, "^\\s*").Length; } return;
                case "A": if (!ReadOnlyCheck()) { Snap(); Mode = "insert"; Col = Cur.Length; } return;
                case "o": if (!ReadOnlyCheck()) { Snap(); L.Insert(Row + 1, ""); Row++; Col = 0; Mode = "insert"; Modified = true; } return;
                case "O": if (!ReadOnlyCheck()) { Snap(); L.Insert(Row, ""); Col = 0; Mode = "insert"; Modified = true; } return;
                case "R": if (!ReadOnlyCheck()) { Snap(); Mode = "replace"; } return;
                case "v": case "V": visualStart = Row; Mode = "visual"; Message = "-- VISUAL LINE --"; return;
                case ":": Mode = "cmd"; CmdLine = ""; return;
                case "/": Mode = "search"; CmdLine = ""; return;
                case "?": Mode = "search"; CmdLine = ""; searchBack = true; return;
                case "n": Search(false); return;
                case "N": Search(true); return;
                case "x": case "Delete": if (Cur.Length > 0) { Snap(); int n = Math.Min(count, Cur.Length - Col); reg = Cur.Substring(Col, n); regLines = false; Cur = Cur.Remove(Col, n); Modified = true; lastChange = p; } return;
                case "X": if (Col > 0) { Snap(); int n = Math.Min(count, Col); Cur = Cur.Remove(Col - n, n); Col -= n; Modified = true; } return;
                case "D": Snap(); reg = Cur.Substring(Math.Min(Col, Cur.Length)); regLines = false; Cur = Cur.Substring(0, Math.Min(Col, Cur.Length)); Modified = true; lastChange = p; return;
                case "C": if (!ReadOnlyCheck()) { Snap(); Cur = Cur.Substring(0, Math.Min(Col, Cur.Length)); Mode = "insert"; Modified = true; } return;
                case "J": if (Row < L.Count - 1) { Snap(); Cur = Cur.TrimEnd() + " " + L[Row + 1].TrimStart(); L.RemoveAt(Row + 1); Modified = true; } return;
                case "u": if (undo.Count > 0) { redo.Add(L.ToArray()); L = undo[undo.Count - 1].ToList(); undo.RemoveAt(undo.Count - 1); Modified = true; Message = "1 change; before #" + (undo.Count + 1); } else Message = "Already at oldest change"; return;
                case "C-r": if (redo.Count > 0) { undo.Add(L.ToArray()); L = redo[redo.Count - 1].ToList(); redo.RemoveAt(redo.Count - 1); Modified = true; } else Message = "Already at newest change"; return;
                case "p": Put(false, count); return;
                case "P": Put(true, count); return;
                case "~": if (Col < Cur.Length) { Snap(); char ch = Cur[Col]; Cur = Cur.Remove(Col, 1).Insert(Col, char.IsUpper(ch) ? char.ToLowerInvariant(ch).ToString() : char.ToUpperInvariant(ch).ToString()); Col++; Modified = true; } return;
                case ".": if (lastChange.Length > 0) { var lc = lastChange; foreach (char ch in lc) NormalKey(ch.ToString()); } return;
                case "ZZ": if (Modified) { if (Write("", false)) Done = true; } else Done = true; return;
                case "ZQ": Done = true; return;
                case "Z": pending = p; return;
                case "d": case "y": case "c": case ">": case "<": case "r": pending = p; return;
            }
            if (c.Length == 2 && c[0] == 'r') { if (Col < Cur.Length && !ReadOnlyCheck()) { Snap(); Cur = Cur.Remove(Col, 1).Insert(Col, c.Substring(1)); Modified = true; } return; }
            if (c.Length == 2 && (c[0] == 'd' || c[0] == 'y' || c[0] == 'c' || c[0] == '>' || c[0] == '<'))
            {
                char op = c[0], mo = c[1];
                if (mo == op)
                {
                    int a = Row, b = Math.Min(L.Count - 1, Row + count - 1);
                    if (op == 'y') { reg = string.Join("\n", L.Skip(a).Take(b - a + 1).ToArray()); regLines = true; Message = count > 1 ? count + " lines yanked" : ""; return; }
                    if (ReadOnlyCheck()) return;
                    Snap();
                    if (op == '>' || op == '<') { for (int r = a; r <= b; r++) L[r] = op == '>' ? "\t" + L[r] : L[r].StartsWith("\t", StringComparison.Ordinal) ? L[r].Substring(1) : Regex.Replace(L[r], "^ {1,8}", ""); Modified = true; return; }
                    reg = string.Join("\n", L.Skip(a).Take(b - a + 1).ToArray()); regLines = true;
                    if (op == 'c') { L.RemoveRange(a, b - a + 1); L.Insert(a, ""); Row = a; Col = 0; Mode = "insert"; Modified = true; return; }
                    L.RemoveRange(a, b - a + 1);
                    if (L.Count == 0) L.Add("");
                    Row = Math.Min(a, L.Count - 1); Col = 0; Modified = true; lastChange = p;
                    if (count > 2) Message = count + " fewer lines";
                    return;
                }
                if (mo == 'w' || mo == 'e' || mo == '$' || mo == 'b' || mo == '0')
                {
                    int from = Col, to;
                    if (mo == '$') to = Cur.Length;
                    else if (mo == '0') { to = Col; from = 0; }
                    else if (mo == 'b') { int cc = Col - 1; while (cc > 0 && char.IsWhiteSpace(Cur[cc])) cc--; while (cc > 0 && !char.IsWhiteSpace(Cur[cc - 1])) cc--; to = Col; from = Math.Max(0, cc); }
                    else { to = Col; for (int i = 0; i < count; i++) to = mo == 'e' || op == 'c' ? WordFwd(Row, to, true) + 1 : WordFwd(Row, to, false); }
                    to = Math.Min(to, Cur.Length);
                    if (to <= from) return;
                    reg = Cur.Substring(from, to - from); regLines = false;
                    if (op == 'y') return;
                    if (ReadOnlyCheck()) return;
                    Snap();
                    Cur = Cur.Remove(from, to - from); Col = from; Modified = true; lastChange = p;
                    if (op == 'c') Mode = "insert";
                    return;
                }
                if (mo == 'G' && op == 'd') { Snap(); reg = string.Join("\n", L.Skip(Row).ToArray()); regLines = true; L.RemoveRange(Row, L.Count - Row); if (L.Count == 0) L.Add(""); Row = L.Count - 1; Modified = true; return; }
                if (mo == 'g' && op == 'd') { pending = p; return; }
            }
            if (c == "dgg") { Snap(); reg = string.Join("\n", L.Take(Row + 1).ToArray()); regLines = true; L.RemoveRange(0, Row + 1); if (L.Count == 0) L.Add(""); Row = 0; Modified = true; return; }
        }
        bool ReadOnlyCheck() { if (ReadOnly) Message = "W10: Warning: Changing a readonly file"; return false; }
        void Put(bool before, int count)
        {
            if (reg.Length == 0 && !regLines) return;
            Snap();
            for (int i = 0; i < count; i++)
            {
                if (regLines) { var ls = reg.Split('\n'); int at = before ? Row : Row + 1; L.InsertRange(at, ls); Row = at; Col = 0; }
                else { int at = before ? Col : Math.Min(Col + 1, Cur.Length); Cur = Cur.Insert(at, reg); Col = at + reg.Length - 1; }
            }
            Modified = true;
        }

        public string StatusLine
        {
            get
            {
                if (Mode == "cmd") return ":" + CmdLine;
                if (Mode == "search") return (searchBack ? "?" : "/") + CmdLine;
                if (Mode == "insert") return "-- INSERT --";
                if (Mode == "replace") return "-- REPLACE --";
                if (Mode == "visual") return "-- VISUAL LINE --";
                return Message;
            }
        }
    }
}
