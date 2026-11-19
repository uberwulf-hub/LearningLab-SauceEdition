// Cert Lab - the bash-like shell, part 2: expansions in bash's order (braces, tilde, parameters / command
// substitution / arithmetic, word splitting, path names, quote removal), glob patterns and $(( )) arithmetic.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class ShError : Exception { public int Status = 1; public ShError(string m) : base(m) { } public ShError(string m, int st) : base(m) { Status = st; } }

    public partial class Sh
    {
        const char Brk = '\ue000';   // a forced field break ("$@")

        class Field
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public readonly List<byte> F = new List<byte>();   // 1 quoted, 2 from an expansion (may be split)
            public bool HadQuotes;
            public void Add(char c, byte f) { Sb.Append(c); F.Add(f); }
            public void Add(string s, byte f) { foreach (char c in s) Add(c, f); }
        }

        // ------------------------------------------------------------ public entry points
        public List<string> ExpandWords(IEnumerable<string> words)
        {
            var res = new List<string>();
            foreach (var w0 in words)
                foreach (var w in BraceExpand(w0))
                {
                    var f = Scan(w, false);
                    foreach (var fld in Split(f))
                    {
                        if (HasGlob(fld))
                        {
                            var g = Glob(GlobPattern(fld));
                            if (g.Count > 0) { res.AddRange(g); continue; }
                        }
                        res.Add(fld.Sb.ToString());
                    }
                }
            return res;
        }

        /// <summary>One word, no splitting and no path names (assignments, redirection targets, case words).</summary>
        public string ExpandOne(string w)
        {
            var f = Scan(w, true);
            return f.Sb.ToString().Replace(Brk.ToString(), " ");
        }

        /// <summary>A pattern for case / [[ == ]] / ${x#pat}: quoted parts match literally.</summary>
        public string ExpandPattern(string w)
        {
            var f = Scan(w, true);
            var sb = new StringBuilder();
            for (int i = 0; i < f.Sb.Length; i++)
            {
                char c = f.Sb[i];
                if (c == Brk) { sb.Append(' '); continue; }
                if ((f.F[i] & 1) != 0 && "*?[]\\".IndexOf(c) >= 0) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ braces {a,b} {1..5}
        public static List<string> BraceExpand(string w)
        {
            var res = new List<string>();
            int open = -1, depth = 0; bool sq = false, dq = false;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'' && !dq) { sq = !sq; continue; }
                if (c == '"' && !sq) { dq = !dq; continue; }
                if (sq || dq) continue;
                if (c == '$' && i + 1 < w.Length && w[i + 1] == '{') { int e = MatchClose(w, i + 1, '{', '}'); if (e > 0) { i = e; continue; } }
                if (c == '{') { if (depth == 0) open = i; depth++; }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    if (depth == 0 && open >= 0)
                    {
                        string inner = w.Substring(open + 1, i - open - 1);
                        string pre = w.Substring(0, open), post = w.Substring(i + 1);
                        var alts = SplitTop(inner);
                        List<string> items = null;
                        if (alts.Count > 1) items = alts;
                        else
                        {
                            var m = Regex.Match(inner, "^(-?\\d+)\\.\\.(-?\\d+)(\\.\\.(-?\\d+))?$");
                            var ma = Regex.Match(inner, "^([a-zA-Z])\\.\\.([a-zA-Z])$");
                            if (m.Success)
                            {
                                long a = long.Parse(m.Groups[1].Value), b = long.Parse(m.Groups[2].Value);
                                long step = m.Groups[4].Success ? Math.Abs(long.Parse(m.Groups[4].Value)) : 1; if (step == 0) step = 1;
                                int width = (m.Groups[1].Value.TrimStart('-').StartsWith("0", StringComparison.Ordinal) && m.Groups[1].Value.TrimStart('-').Length > 1) || (m.Groups[2].Value.TrimStart('-').StartsWith("0", StringComparison.Ordinal) && m.Groups[2].Value.TrimStart('-').Length > 1) ? Math.Max(m.Groups[1].Value.Length, m.Groups[2].Value.Length) : 0;
                                items = new List<string>();
                                if (a <= b) for (long k = a; k <= b && items.Count < 100000; k += step) items.Add(width > 0 ? k.ToString("D" + width) : k.ToString(CultureInfo.InvariantCulture));
                                else for (long k = a; k >= b && items.Count < 100000; k -= step) items.Add(width > 0 ? k.ToString("D" + width) : k.ToString(CultureInfo.InvariantCulture));
                            }
                            else if (ma.Success)
                            {
                                char a = ma.Groups[1].Value[0], b = ma.Groups[2].Value[0];
                                items = new List<string>();
                                if (a <= b) for (char k = a; k <= b; k++) items.Add(k.ToString()); else for (char k = a; k >= b; k--) items.Add(k.ToString());
                            }
                        }
                        if (items != null)
                        {
                            foreach (var it in items) res.AddRange(BraceExpand(pre + it + post));
                            return res;
                        }
                        open = -1;
                    }
                }
            }
            res.Add(w);
            return res;
        }

        static List<string> SplitTop(string s)
        {
            var l = new List<string>(); int depth = 0, st = 0; bool sq = false, dq = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'' && !dq) sq = !sq; else if (c == '"' && !sq) dq = !dq;
                else if (!sq && !dq) { if (c == '{') depth++; else if (c == '}') depth--; else if (c == ',' && depth == 0) { l.Add(s.Substring(st, i - st)); st = i + 1; } }
            }
            l.Add(s.Substring(st));
            return l;
        }

        public static int MatchClose(string s, int i, char open, char close)
        {
            int depth = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'' && open == '(') { int e = s.IndexOf('\'', i + 1); if (e < 0) return -1; i = e; continue; }
                if (c == '"') { i = SkipDq(s, i); if (i < 0) return -1; continue; }
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
        static int SkipDq(string s, int i)
        {
            for (i = i + 1; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '"') return i;
                if (s[i] == '$' && i + 1 < s.Length && s[i + 1] == '(') { int e = MatchClose(s, i + 1, '(', ')'); if (e < 0) return -1; i = e; }
            }
            return -1;
        }

        // ------------------------------------------------------------ the main scan
        Field Scan(string w, bool noSplit)
        {
            var f = new Field();
            int i = 0;
            bool assign = ShParser.IsAssign(w);
            // tilde at the start (and after = in an assignment)
            i = Tilde(w, 0, f);
            while (i < w.Length)
            {
                char c = w[i];
                if (c == '\\')
                {
                    if (i + 1 < w.Length) { f.Add(w[i + 1], 1); i += 2; } else { f.Add('\\', 0); i++; }
                    continue;
                }
                if (c == '\'')
                {
                    int e = w.IndexOf('\'', i + 1);
                    if (e < 0) e = w.Length;
                    f.Add(w.Substring(i + 1, e - i - 1), 1);
                    f.HadQuotes = true;
                    i = e + 1;
                    continue;
                }
                if (c == '$' && i + 1 < w.Length && w[i + 1] == '\'')
                {
                    int e = i + 2; var sb = new StringBuilder();
                    while (e < w.Length && w[e] != '\'') { if (w[e] == '\\' && e + 1 < w.Length) { sb.Append(w[e]); e++; } sb.Append(w[e]); e++; }
                    f.Add(Printf.Escapes(sb.ToString(), false), 1);
                    f.HadQuotes = true;
                    i = e + 1;
                    continue;
                }
                if (c == '"')
                {
                    f.HadQuotes = true;
                    i++;
                    while (i < w.Length && w[i] != '"')
                    {
                        char d = w[i];
                        if (d == '\\' && i + 1 < w.Length && "$`\"\\\n".IndexOf(w[i + 1]) >= 0) { if (w[i + 1] != '\n') f.Add(w[i + 1], 1); i += 2; continue; }
                        if (d == '$' || d == '`') { i = Dollar(w, i, f, true); continue; }
                        f.Add(d, 1); i++;
                    }
                    i++;
                    continue;
                }
                if (c == '$' || c == '`') { i = Dollar(w, i, f, false); continue; }
                if (assign && (c == ':' || c == '=') && i + 1 < w.Length && w[i + 1] == '~') { f.Add(c, 0); i = Tilde(w, i + 1, f); continue; }
                f.Add(c, 0); i++;
            }
            return f;
        }

        /// <summary>A here-document body: $, ` and \ ($ ` \ newline) are special, quotes are plain text.</summary>
        public string ExpandHereText(string w)
        {
            var f = new Field();
            int i = 0;
            while (i < w.Length)
            {
                char d = w[i];
                if (d == '\\' && i + 1 < w.Length && "$`\\\n".IndexOf(w[i + 1]) >= 0) { if (w[i + 1] != '\n') f.Add(w[i + 1], 1); i += 2; continue; }
                if (d == '$' || d == '`') { i = Dollar(w, i, f, true); continue; }
                f.Add(d, 1); i++;
            }
            return f.Sb.ToString().Replace(Brk.ToString(), " ");
        }

        int Tilde(string w, int i, Field f)
        {
            if (i >= w.Length || w[i] != '~') return i;
            int e = i + 1;
            while (e < w.Length && w[e] != '/' && w[e] != ':' && (char.IsLetterOrDigit(w[e]) || w[e] == '_' || w[e] == '-' || w[e] == '.' || w[e] == '+')) e++;
            string user = w.Substring(i + 1, e - i - 1);
            string home = null;
            if (user == "") home = GetVar("HOME");
            else if (user == "+") home = GetVar("PWD");
            else if (user == "-") home = GetVar("OLDPWD");
            else { var p = M.User(user); if (p != null) home = p.Home; }
            if (home == null) return i;   // left as it is
            f.Add(home, 1);
            return e;
        }

        /// <summary>$name, ${...}, $(...), $((...)), `...` at i; appends the value; returns the index after it.</summary>
        int Dollar(string w, int i, Field f, bool inDq)
        {
            byte fl = (byte)(inDq ? 1 : 2);
            if (w[i] == '`')
            {
                int e = i + 1;
                var sb = new StringBuilder();
                while (e < w.Length && w[e] != '`') { if (w[e] == '\\' && e + 1 < w.Length && "$`\\".IndexOf(w[e + 1]) >= 0) e++; sb.Append(w[e]); e++; }
                f.Add(CommandSubst(sb.ToString()), fl);
                return e + 1;
            }
            if (i + 1 >= w.Length) { f.Add('$', 0); return i + 1; }
            char c = w[i + 1];
            if (c == '(' && i + 2 < w.Length && w[i + 2] == '(')
            {
                int e = MatchClose(w, i + 1, '(', ')');
                if (e > 0 && w[e - 1] == ')')
                {
                    string expr = w.Substring(i + 3, e - 1 - (i + 3));
                    f.Add(Arith(expr).ToString(CultureInfo.InvariantCulture), fl);
                    return e + 1;
                }
            }
            if (c == '(')
            {
                int e = MatchClose(w, i + 1, '(', ')');
                if (e < 0) e = w.Length - 1;
                f.Add(CommandSubst(w.Substring(i + 2, e - i - 2)), fl);
                return e + 1;
            }
            if (c == '{')
            {
                int e = MatchClose(w, i + 1, '{', '}');
                if (e < 0) throw new ShError("bad substitution");
                string inner = w.Substring(i + 2, e - i - 2);
                bool multi;
                var vals = Brace(inner, inDq, out multi);
                AddValues(f, vals, multi, inDq, fl);
                return e + 1;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int e = i + 1;
                while (e < w.Length && (char.IsLetterOrDigit(w[e]) || w[e] == '_')) e++;
                string name = w.Substring(i + 1, e - i - 1);
                string v = GetVar(name);
                if (v == null && NoUnset) throw new ShError(name + ": unbound variable");
                f.Add(v ?? "", fl);
                return e;
            }
            if (char.IsDigit(c)) { f.Add(Positional(c - '0') ?? "", fl); return i + 2; }
            if (c == '@' || c == '*')
            {
                AddValues(f, new List<string>(Pos), c == '@', inDq, fl);
                return i + 2;
            }
            if ("?#$!-".IndexOf(c) >= 0) { f.Add(Special(c), fl); return i + 2; }
            f.Add('$', 0);
            return i + 1;
        }

        void AddValues(Field f, List<string> vals, bool multi, bool inDq, byte fl)
        {
            if (multi && inDq)
            {
                for (int k = 0; k < vals.Count; k++) { if (k > 0) f.Add(Brk, 2); f.Add(vals[k], 1); }
                if (vals.Count == 0) f.Add("\ue001", 2);   // "$@" with nothing: no field at all
                return;
            }
            string ifs = GetVar("IFS") ?? " \t\n";
            string sep = inDq ? (ifs.Length > 0 ? ifs.Substring(0, 1) : "") : " ";
            f.Add(string.Join(sep, vals.ToArray()), fl);
        }

        string Special(char c)
        {
            switch (c)
            {
                case '?': return Status.ToString(CultureInfo.InvariantCulture);
                case '#': return Pos.Count.ToString(CultureInfo.InvariantCulture);
                case '$': return Pid.ToString(CultureInfo.InvariantCulture);
                case '!': return LastBg > 0 ? LastBg.ToString(CultureInfo.InvariantCulture) : "";
                case '-': return (Interactive ? "himBH" : "hB") + (ErrExit ? "e" : "") + (XTrace ? "x" : "") + (NoUnset ? "u" : "");
            }
            return "";
        }

        public string Positional(int n)
        {
            if (n == 0) return Arg0;
            return n <= Pos.Count ? Pos[n - 1] : null;
        }

        /// <summary>${...}: returns the value(s); multi when it is an array with [@] (or $@).</summary>
        List<string> Brace(string s, bool inDq, out bool multi)
        {
            multi = false;
            var one = new List<string>();
            if (s.Length > 1 && s[0] == '#' && s != "#")
            {
                string nm = s.Substring(1);
                var m = Regex.Match(nm, "^([A-Za-z_][A-Za-z0-9_]*)\\[([@*])\\]$");
                if (m.Success) { var a = GetArray(m.Groups[1].Value); one.Add((a == null ? (GetVar(m.Groups[1].Value) == null ? 0 : 1) : a.Count).ToString(CultureInfo.InvariantCulture)); return one; }
                if (nm == "@" || nm == "*") { one.Add(Pos.Count.ToString(CultureInfo.InvariantCulture)); return one; }
                one.Add((ParamValue(nm) ?? "").Length.ToString(CultureInfo.InvariantCulture));
                return one;
            }
            if (s.StartsWith("!", StringComparison.Ordinal))
            {
                var m = Regex.Match(s, "^!([A-Za-z_][A-Za-z0-9_]*)\\[([@*])\\]$");
                if (m.Success) { multi = m.Groups[2].Value == "@"; return ArrayKeys(m.Groups[1].Value); }
                string target = GetVar(s.Substring(1));
                one.Add(target == null ? "" : (ParamValue(target) ?? ""));
                return one;
            }
            // the name
            int i = 0;
            string name;
            if (s.Length > 0 && (s[0] == '@' || s[0] == '*' || s[0] == '?' || s[0] == '#' || s[0] == '$' || s[0] == '!' || s[0] == '-')) { name = s.Substring(0, 1); i = 1; }
            else if (s.Length > 0 && char.IsDigit(s[0])) { while (i < s.Length && char.IsDigit(s[i])) i++; name = s.Substring(0, i); }
            else { while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++; name = s.Substring(0, i); }
            if (name.Length == 0) throw new ShError("${" + s + "}: bad substitution");
            string index = null;
            if (i < s.Length && s[i] == '[') { int e = s.IndexOf(']', i); if (e < 0) throw new ShError("${" + s + "}: bad substitution"); index = s.Substring(i + 1, e - i - 1); i = e + 1; }
            string op = s.Substring(i);
            // values
            List<string> vals;
            bool isMulti = false;
            if (name == "@" || name == "*") { vals = new List<string>(Pos); isMulti = name == "@"; }
            else if (index == "@" || index == "*") { var a = GetArray(name); vals = a == null ? (GetVar(name) == null ? new List<string>() : new List<string> { GetVar(name) }) : new List<string>(a); isMulti = index == "@"; }
            else
            {
                string v = index == null ? ParamValue(name) : ArrayGet(name, index);
                vals = v == null ? null : new List<string> { v };
            }
            bool unset = vals == null;
            bool empty = unset || (vals.Count == 0) || (vals.Count == 1 && vals[0].Length == 0);
            if (op.Length == 0)
            {
                if (unset && NoUnset && name != "@" && name != "*") throw new ShError(name + ": unbound variable");
                multi = isMulti; return vals ?? one;
            }
            string[] two = { ":-", ":=", ":?", ":+" };
            foreach (var t in two)
                if (op.StartsWith(t, StringComparison.Ordinal))
                {
                    string word = op.Substring(2);
                    return Default(t[1], empty, name, vals, word, isMulti, out multi);
                }
            if (op[0] == '-' || op[0] == '=' || op[0] == '?' || op[0] == '+')
                return Default(op[0], unset, name, vals, op.Substring(1), isMulti, out multi);
            if (vals == null) vals = new List<string>();
            var outv = new List<string>();
            foreach (var v0 in vals)
            {
                string v = v0;
                if (op.StartsWith("##", StringComparison.Ordinal) || op.StartsWith("#", StringComparison.Ordinal)) { bool lng = op.StartsWith("##", StringComparison.Ordinal); v = TrimPattern(v, ExpandPattern(op.Substring(lng ? 2 : 1)), true, lng); }
                else if (op.StartsWith("%%", StringComparison.Ordinal) || op.StartsWith("%", StringComparison.Ordinal)) { bool lng = op.StartsWith("%%", StringComparison.Ordinal); v = TrimPattern(v, ExpandPattern(op.Substring(lng ? 2 : 1)), false, lng); }
                else if (op.StartsWith("/", StringComparison.Ordinal))
                {
                    bool all = op.StartsWith("//", StringComparison.Ordinal);
                    string rest = op.Substring(all ? 2 : 1);
                    bool anchorStart = !all && rest.StartsWith("#", StringComparison.Ordinal), anchorEnd = !all && rest.StartsWith("%", StringComparison.Ordinal);
                    if (anchorStart || anchorEnd) rest = rest.Substring(1);
                    int sl = FindSlash(rest);
                    string pat = sl < 0 ? rest : rest.Substring(0, sl), rep = sl < 0 ? "" : ExpandOne(rest.Substring(sl + 1));
                    var rx = GlobRegex(ExpandPattern(pat), false);
                    string rs = rx.ToString();
                    rs = rs.Substring(1, rs.Length - 2);
                    var r2 = new Regex((anchorStart ? "^" : "") + rs + (anchorEnd ? "$" : ""));
                    v = all ? r2.Replace(v, rep.Replace("$", "$$")) : r2.Replace(v, rep.Replace("$", "$$"), 1);
                }
                else if (op == "^^") v = v.ToUpperInvariant();
                else if (op == ",,") v = v.ToLowerInvariant();
                else if (op == "^") v = v.Length > 0 ? char.ToUpperInvariant(v[0]) + v.Substring(1) : v;
                else if (op == ",") v = v.Length > 0 ? char.ToLowerInvariant(v[0]) + v.Substring(1) : v;
                else if (op.StartsWith(":", StringComparison.Ordinal))
                {
                    var parts = op.Substring(1).Split(new[] { ':' }, 2);
                    long off = Arith(parts[0]);
                    if (off < 0) off = Math.Max(0, v.Length + off);
                    if (off > v.Length) off = v.Length;
                    long len = parts.Length > 1 ? Arith(parts[1]) : v.Length - off;
                    if (len < 0) len = Math.Max(0, v.Length - off + len);
                    v = v.Substring((int)off, (int)Math.Min(len, v.Length - off));
                }
                else throw new ShError("${" + s + "}: bad substitution");
                outv.Add(v);
            }
            multi = isMulti;
            return outv;
        }

        static int FindSlash(string s) { for (int i = 0; i < s.Length; i++) { if (s[i] == '\\') { i++; continue; } if (s[i] == '/') return i; } return -1; }

        List<string> Default(char kind, bool cond, string name, List<string> vals, string word, bool isMulti, out bool multi)
        {
            multi = false;
            switch (kind)
            {
                case '-': if (cond) return new List<string> { ExpandOne(word) }; multi = isMulti; return vals;
                case '=':
                    if (cond) { string v = ExpandOne(word); SetVar(name, v); return new List<string> { v }; }
                    multi = isMulti; return vals;
                case '?':
                    if (cond) throw new ShError(name + ": " + (word.Length > 0 ? ExpandOne(word) : "parameter null or not set"));
                    multi = isMulti; return vals;
                case '+': if (!cond) return new List<string> { ExpandOne(word) }; return new List<string> { "" };
            }
            return vals ?? new List<string>();
        }

        string TrimPattern(string v, string pat, bool front, bool longest)
        {
            var rx = GlobRegex(pat, false);
            if (front)
            {
                if (longest) { for (int k = v.Length; k >= 0; k--) if (rx.IsMatch(v.Substring(0, k))) return v.Substring(k); }
                else { for (int k = 0; k <= v.Length; k++) if (rx.IsMatch(v.Substring(0, k))) return v.Substring(k); }
            }
            else
            {
                if (longest) { for (int k = 0; k <= v.Length; k++) if (rx.IsMatch(v.Substring(k))) return v.Substring(0, k); }
                else { for (int k = v.Length; k >= 0; k--) if (rx.IsMatch(v.Substring(k))) return v.Substring(0, k); }
            }
            return v;
        }

        string ParamValue(string name)
        {
            if (name.Length > 0 && char.IsDigit(name[0])) { int n; return int.TryParse(name, out n) ? Positional(n) : null; }
            if (name.Length == 1 && "?#$!-".IndexOf(name[0]) >= 0) return Special(name[0]);
            if (name == "@" || name == "*") return string.Join(" ", Pos.ToArray());
            var a = GetArray(name);
            if (a != null) return a.Count > 0 ? a[0] : null;
            return GetVar(name);
        }

        string CommandSubst(string cmd)
        {
            var sub = Subshell();
            var buf = new BufSink();
            var io = new Io { In = Io.In, Out = buf, Err = Io.Err };
            int st = sub.RunText(cmd, io);
            Status = st;
            CmdSubstStatus = st;
            Steps = sub.Steps;
            return buf.Text.TrimEnd('\n');
        }

        // ------------------------------------------------------------ word splitting
        List<Field> Split(Field f)
        {
            string ifs = GetVar("IFS") ?? " \t\n";
            var res = new List<Field>();
            var cur = new Field();
            bool any = false;
            bool sawEmptyAt = false;
            for (int i = 0; i < f.Sb.Length; i++)
            {
                char c = f.Sb[i]; byte fl = f.F[i];
                if (c == '\ue001') { sawEmptyAt = true; continue; }
                if (c == Brk) { res.Add(cur); cur = new Field { HadQuotes = true }; any = false; continue; }
                if ((fl & 2) != 0 && (fl & 1) == 0 && ifs.IndexOf(c) >= 0)
                {
                    if (any || cur.Sb.Length > 0) { res.Add(cur); cur = new Field(); any = false; }
                    continue;
                }
                cur.Add(c, fl);
                any = true;
                if ((fl & 1) != 0) cur.HadQuotes = true;
            }
            if (cur.Sb.Length > 0 || (f.HadQuotes && !sawEmptyAt && res.Count == 0) || cur.HadQuotes) res.Add(cur);
            return res;
        }

        static bool HasGlob(Field f)
        {
            for (int i = 0; i < f.Sb.Length; i++)
                if ((f.F[i] & 1) == 0 && (f.Sb[i] == '*' || f.Sb[i] == '?' || f.Sb[i] == '[')) return true;
            return false;
        }
        static string GlobPattern(Field f)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < f.Sb.Length; i++)
            {
                char c = f.Sb[i];
                if ((f.F[i] & 1) != 0 && "*?[]\\".IndexOf(c) >= 0) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ glob patterns
        static readonly Dictionary<string, Regex> globCache = new Dictionary<string, Regex>();

        public static Regex GlobRegex(string pat, bool dotSpecial)
        {
            string key = (dotSpecial ? "1" : "0") + pat;
            Regex r;
            lock (globCache) { if (globCache.TryGetValue(key, out r)) return r; }
            var sb = new StringBuilder("^");
            for (int i = 0; i < pat.Length; i++)
            {
                char c = pat[i];
                if (c == '\\' && i + 1 < pat.Length) { sb.Append(Regex.Escape(pat[i + 1].ToString())); i++; continue; }
                if (c == '*') { sb.Append(".*"); continue; }
                if (c == '?') { sb.Append('.'); continue; }
                if (c == '[')
                {
                    int e = i + 1;
                    if (e < pat.Length && (pat[e] == '!' || pat[e] == '^')) e++;
                    if (e < pat.Length && pat[e] == ']') e++;
                    while (e < pat.Length && pat[e] != ']') { if (pat[e] == '[' && e + 1 < pat.Length && pat[e + 1] == ':') { int ce = pat.IndexOf(":]", e + 2, StringComparison.Ordinal); if (ce > 0) e = ce + 1; } e++; }
                    if (e >= pat.Length) { sb.Append("\\["); continue; }
                    string cls = pat.Substring(i + 1, e - i - 1);
                    var cb = new StringBuilder("[");
                    int k = 0;
                    if (cls.Length > 0 && (cls[0] == '!' || cls[0] == '^')) { cb.Append('^'); k = 1; }
                    for (; k < cls.Length; k++)
                    {
                        char d = cls[k];
                        if (d == '[' && k + 1 < cls.Length && cls[k + 1] == ':')
                        {
                            int ce = cls.IndexOf(":]", k + 2, StringComparison.Ordinal);
                            string nm = cls.Substring(k + 2, ce - k - 2);
                            cb.Append(PosixClass(nm));
                            k = ce + 1;
                            continue;
                        }
                        if (d == '\\' || d == ']' || d == '[' || d == '^') cb.Append('\\');
                        cb.Append(d);
                    }
                    cb.Append(']');
                    sb.Append(cb);
                    i = e;
                    continue;
                }
                sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            r = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            lock (globCache) { globCache[key] = r; }
            return r;
        }

        public static string PosixClass(string nm)
        {
            switch (nm)
            {
                case "digit": return "0-9";
                case "alpha": return "a-zA-Z";
                case "alnum": return "a-zA-Z0-9";
                case "upper": return "A-Z";
                case "lower": return "a-z";
                case "space": return "\\s";
                case "blank": return " \\t";
                case "punct": return "!-/:-@\\[-`{-~";
                case "xdigit": return "0-9A-Fa-f";
                case "print": return "\\x20-\\x7E";
                case "graph": return "\\x21-\\x7E";
                case "cntrl": return "\\x00-\\x1F";
            }
            return "";
        }

        public static bool GlobMatch(string pat, string s) { return GlobRegex(pat, false).IsMatch(s); }

        /// <summary>Path name expansion: the matching paths, sorted, or none.</summary>
        public List<string> Glob(string pat)
        {
            var res = new List<string>();
            bool abs = pat.StartsWith("/", StringComparison.Ordinal);
            var parts = pat.Split('/').Where(p => p.Length > 0).ToList();
            GlobWalk(abs ? "/" : "", parts, 0, res);
            res.Sort(Vfs.LsCompare);
            if (pat.EndsWith("/", StringComparison.Ordinal)) for (int i = 0; i < res.Count; i++) res[i] += "/";
            return res;
        }

        void GlobWalk(string prefix, List<string> parts, int k, List<string> res)
        {
            if (k >= parts.Count) { if (prefix.Length > 0) res.Add(prefix); return; }
            string part = parts[k];
            bool glob = part.IndexOfAny(new[] { '*', '?', '[' }) >= 0 && !IsAllEscaped(part);
            string dir = prefix.Length == 0 ? "." : prefix;
            if (!glob)
            {
                string lit = Unescape(part);
                string np = prefix.Length == 0 ? lit : (prefix.EndsWith("/", StringComparison.Ordinal) ? prefix + lit : prefix + "/" + lit);
                try { var l = M.V.Resolve(P, np, true); if (!l.Exists) return; } catch (VfsEx) { return; }
                GlobWalk(np, parts, k + 1, res);
                return;
            }
            Loc dl;
            try { dl = M.V.Resolve(P, dir, true); } catch (VfsEx) { return; }
            if (!dl.Exists || dl.Node.T != 'd') return;
            if (!M.V.Can(P.Cr, dl.Node, 4)) return;
            var rx = GlobRegex(part, true);
            bool dotOk = part.StartsWith(".", StringComparison.Ordinal) || part.StartsWith("\\.", StringComparison.Ordinal);
            foreach (var name in dl.Node.Kids.Keys.ToList())
            {
                if (name.StartsWith(".", StringComparison.Ordinal) && !dotOk) continue;
                if (!rx.IsMatch(name)) continue;
                string np = prefix.Length == 0 ? name : (prefix.EndsWith("/", StringComparison.Ordinal) ? prefix + name : prefix + "/" + name);
                if (k + 1 < parts.Count)
                {
                    try { var cl = M.V.Resolve(P, np, true); if (!cl.Exists || cl.Node.T != 'd') continue; } catch (VfsEx) { continue; }
                }
                GlobWalk(np, parts, k + 1, res);
            }
        }
        static bool IsAllEscaped(string p) { for (int i = 0; i < p.Length; i++) { if (p[i] == '\\') { i++; continue; } if (p[i] == '*' || p[i] == '?' || p[i] == '[') return false; } return true; }
        static string Unescape(string p) { var sb = new StringBuilder(); for (int i = 0; i < p.Length; i++) { if (p[i] == '\\' && i + 1 < p.Length) { sb.Append(p[i + 1]); i++; } else sb.Append(p[i]); } return sb.ToString(); }

        // ------------------------------------------------------------ arithmetic
        public long Arith(string expr)
        {
            var a = new ArithEval(this, ExpandArithText(expr));
            return a.Run();
        }
        string ExpandArithText(string expr)
        {
            // $var, $(cmd) and ${} inside (( )) are expanded first
            if (expr.IndexOf('$') < 0 && expr.IndexOf('`') < 0) return expr;
            return ExpandOne(expr.Replace("\"", ""));
        }
    }

    public class ArithEval
    {
        readonly Sh sh; readonly string s; int i;
        public ArithEval(Sh shell, string text) { sh = shell; s = text; }
        public long Run() { Ws(); if (i >= s.Length) return 0; long v = Comma(); Ws(); if (i < s.Length) throw new ShError(s.Trim() + ": syntax error: invalid arithmetic operator (error token is \"" + s.Substring(i) + "\")"); return v; }
        void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        bool Eat(string op) { Ws(); if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) { i += op.Length; return true; } return false; }
        bool Peek(string op) { Ws(); return string.CompareOrdinal(s, i, op, 0, op.Length) == 0; }

        long Comma() { long v = Assign(); while (Eat(",")) v = Assign(); return v; }
        long Assign()
        {
            Ws();
            int save = i;
            var m = Regex.Match(s.Substring(i), "^([A-Za-z_][A-Za-z0-9_]*)(\\[[^\\]]*\\])?\\s*(=|\\+=|-=|\\*=|/=|%=|<<=|>>=|&=|\\^=|\\|=)(?!=)");
            if (m.Success)
            {
                i += m.Length;
                string name = m.Groups[1].Value, idx = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value.Trim('[', ']') : null, op = m.Groups[3].Value;
                long rhs = Assign();
                long cur = Get(name, idx);
                long v;
                switch (op)
                {
                    case "=": v = rhs; break;
                    case "+=": v = cur + rhs; break;
                    case "-=": v = cur - rhs; break;
                    case "*=": v = cur * rhs; break;
                    case "/=": if (rhs == 0) throw new ShError("division by 0 (error token is \"" + rhs + "\")"); v = cur / rhs; break;
                    case "%=": if (rhs == 0) throw new ShError("division by 0"); v = cur % rhs; break;
                    case "<<=": v = cur << (int)rhs; break;
                    case ">>=": v = cur >> (int)rhs; break;
                    case "&=": v = cur & rhs; break;
                    case "^=": v = cur ^ rhs; break;
                    default: v = cur | rhs; break;
                }
                Set(name, idx, v);
                return v;
            }
            i = save;
            return Ternary();
        }
        long Ternary()
        {
            long c = Or();
            if (Eat("?")) { long a = Assign(); if (!Eat(":")) throw new ShError("expected `:'"); long b = Assign(); return c != 0 ? a : b; }
            return c;
        }
        long Or() { long v = And(); while (Peek("||")) { i += 2; long r = And(); v = (v != 0 || r != 0) ? 1 : 0; } return v; }
        long And() { long v = BOr(); while (Peek("&&")) { i += 2; long r = BOr(); v = (v != 0 && r != 0) ? 1 : 0; } return v; }
        long BOr() { long v = BXor(); while (Peek("|") && !Peek("||") && !Peek("|=")) { i++; v |= BXor(); } return v; }
        long BXor() { long v = BAnd(); while (Peek("^") && !Peek("^=")) { i++; v ^= BAnd(); } return v; }
        long BAnd() { long v = Eq(); while (Peek("&") && !Peek("&&") && !Peek("&=")) { i++; v &= Eq(); } return v; }
        long Eq() { long v = Rel(); for (;;) { if (Eat("==")) v = v == Rel() ? 1 : 0; else if (Eat("!=")) v = v != Rel() ? 1 : 0; else return v; } }
        long Rel()
        {
            long v = Shift();
            for (;;)
            {
                if (Eat("<=")) v = v <= Shift() ? 1 : 0;
                else if (Eat(">=")) v = v >= Shift() ? 1 : 0;
                else if (Peek("<") && !Peek("<<")) { i++; v = v < Shift() ? 1 : 0; }
                else if (Peek(">") && !Peek(">>")) { i++; v = v > Shift() ? 1 : 0; }
                else return v;
            }
        }
        long Shift() { long v = Add(); for (;;) { if (Peek("<<") && !Peek("<<=")) { i += 2; v <<= (int)Add(); } else if (Peek(">>") && !Peek(">>=")) { i += 2; v >>= (int)Add(); } else return v; } }
        long Add() { long v = Mul(); for (;;) { if (Peek("+") && !Peek("++") && !Peek("+=")) { i++; v += Mul(); } else if (Peek("-") && !Peek("--") && !Peek("-=")) { i++; v -= Mul(); } else return v; } }
        long Mul()
        {
            long v = Pow();
            for (;;)
            {
                if (Peek("*") && !Peek("**") && !Peek("*=")) { i++; v *= Pow(); }
                else if (Peek("/") && !Peek("/=")) { i++; long d = Pow(); if (d == 0) throw new ShError("division by 0 (error token is \"0\")"); v /= d; }
                else if (Peek("%") && !Peek("%=")) { i++; long d = Pow(); if (d == 0) throw new ShError("division by 0 (error token is \"0\")"); v %= d; }
                else return v;
            }
        }
        long Pow() { long b = Unary(); if (Eat("**")) { long e = Pow(); long r = 1; for (long k = 0; k < e; k++) r *= b; return r; } return b; }
        long Unary()
        {
            Ws();
            if (Eat("++")) { var nm = Name(); long v = Get(nm, null) + 1; Set(nm, null, v); return v; }
            if (Eat("--")) { var nm = Name(); long v = Get(nm, null) - 1; Set(nm, null, v); return v; }
            if (Eat("!")) return Unary() == 0 ? 1 : 0;
            if (Eat("~")) return ~Unary();
            if (Peek("-")) { i++; return -Unary(); }
            if (Peek("+")) { i++; return Unary(); }
            return Postfix();
        }
        long Postfix()
        {
            Ws();
            if (Eat("(")) { long v = Comma(); if (!Eat(")")) throw new ShError("missing `)'"); return v; }
            if (i < s.Length && char.IsDigit(s[i]))
            {
                int st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '#' || s[i] == '_')) i++;
                return Num(s.Substring(st, i - st));
            }
            if (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_'))
            {
                string nm = Name();
                string idx = null;
                if (i < s.Length && s[i] == '[') { int e = s.IndexOf(']', i); idx = s.Substring(i + 1, e - i - 1); i = e + 1; }
                if (Eat("++")) { long v = Get(nm, idx); Set(nm, idx, v + 1); return v; }
                if (Eat("--")) { long v = Get(nm, idx); Set(nm, idx, v - 1); return v; }
                return Get(nm, idx);
            }
            if (i >= s.Length) throw new ShError("syntax error: operand expected (error token is \"" + s.Trim() + "\")");
            throw new ShError("syntax error: operand expected (error token is \"" + s.Substring(i) + "\")");
        }
        string Name() { Ws(); int st = i; while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++; return s.Substring(st, i - st); }
        static long Num(string t)
        {
            try
            {
                int h = t.IndexOf('#');
                if (h > 0) { int b = int.Parse(t.Substring(0, h)); long v = 0; foreach (char c in t.Substring(h + 1)) { int d = char.IsDigit(c) ? c - '0' : char.IsLower(c) ? c - 'a' + 10 : c - 'A' + 36; v = v * b + d; } return v; }
                if (t.StartsWith("0x", StringComparison.Ordinal) || t.StartsWith("0X", StringComparison.Ordinal)) return Convert.ToInt64(t.Substring(2), 16);
                if (t.Length > 1 && t[0] == '0') return Convert.ToInt64(t, 8);
                return long.Parse(t, CultureInfo.InvariantCulture);
            }
            catch (Exception) { throw new ShError(t + ": value too great for base (error token is \"" + t + "\")"); }
        }
        long Get(string name, string idx)
        {
            string v = idx == null ? sh.GetVar(name) : sh.ArrayGet(name, idx);
            if (string.IsNullOrEmpty(v)) return 0;
            long n;
            if (long.TryParse(v.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) return n;
            if (v == name) return 0;
            return new ArithEval(sh, v).Run();
        }
        void Set(string name, string idx, long v)
        {
            if (idx == null) sh.SetVar(name, v.ToString(CultureInfo.InvariantCulture));
            else sh.ArraySet(name, idx, v.ToString(CultureInfo.InvariantCulture));
        }
    }
}
