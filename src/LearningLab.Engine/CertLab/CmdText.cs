// Cert Lab - text programs: grep, sed, head, tail, wc, sort, uniq, cut, tr, nl, paste, tac, rev, column, seq,
// base64, the checksum programs, diff, cmp, comm, xargs, less / more, expr, bc, fold, shuf, yes, strings.
// Regular expressions are POSIX basic (BRE) or extended (ERE) and are translated to .NET's syntax.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegText()
        {
            P("grep", Grep); P("egrep", (sh, a, io) => { var b = new List<string>(a); b.Insert(1, "-E"); b[0] = "grep"; return Grep(sh, b, io); }); P("fgrep", (sh, a, io) => { var b = new List<string>(a); b.Insert(1, "-F"); b[0] = "grep"; return Grep(sh, b, io); });
            P("sed", Sed); P("head", Head); P("tail", Tail); P("wc", Wc); P("sort", Sort); P("uniq", Uniq); P("cut", Cut); P("tr", Tr);
            P("nl", Nl); P("paste", Paste); P("tac", Tac); P("rev", Rev); P("column", Column); P("seq", Seq); P("base64", Base64); P("base32", Base32);
            P("md5sum", (sh, a, io) => Hash(sh, a, io, "md5")); P("sha1sum", (sh, a, io) => Hash(sh, a, io, "sha1")); P("sha256sum", (sh, a, io) => Hash(sh, a, io, "sha256")); P("sha512sum", (sh, a, io) => Hash(sh, a, io, "sha512")); P("cksum", (sh, a, io) => Hash(sh, a, io, "cksum")); P("b2sum", (sh, a, io) => Hash(sh, a, io, "sha512"));
            P("diff", Diff); P("cmp", CmpCmd); P("comm", Comm); P("xargs", Xargs); P("less", Less); P("more", Less); P("expr", Expr); P("bc", Bc);
            P("fold", Fold); P("shuf", Shuf); P("yes", Yes); P("strings", (sh, a, io) => { int st = 0; foreach (var kv in Inputs(sh, a.Skip(1).ToList(), io, "strings", ref st)) foreach (Match m in Regex.Matches(kv.Value, "[\\x20-\\x7e]{4,}")) io.L(m.Value); return st; });
            P("awk", Awk.Run); P("gawk", Awk.Run);
            P("od", (sh, a, io) => { int st = 0; var t = string.Join("", Inputs(sh, a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList(), io, "od", ref st).Select(x => x.Value).ToArray()); var b = Encoding.UTF8.GetBytes(t); for (int i = 0; i < b.Length; i += 16) io.L(Convert.ToString(i, 8).PadLeft(7, '0') + " " + string.Join(" ", b.Skip(i).Take(16).Select(x => (x >= 32 && x < 127 ? ((char)x).ToString() : x == 10 ? "\\n" : x == 9 ? "\\t" : Convert.ToString(x, 8).PadLeft(3, '0')).PadLeft(3)).ToArray())); io.L(Convert.ToString(b.Length, 8).PadLeft(7, '0')); return st; });
        }

        // ------------------------------------------------------------ regular expressions
        /// <summary>A POSIX regular expression (BRE unless ere) as a .NET pattern.</summary>
        public static string PosixToNet(string re, bool ere)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < re.Length; i++)
            {
                char c = re[i];
                if (c == '[')
                {
                    // a bracket expression: copy it with [:classes:] translated
                    int e = i + 1;
                    if (e < re.Length && re[e] == '^') e++;
                    if (e < re.Length && re[e] == ']') e++;
                    while (e < re.Length && re[e] != ']') { if (re[e] == '[' && e + 1 < re.Length && (re[e + 1] == ':' || re[e + 1] == '.' || re[e + 1] == '=')) { int ce = re.IndexOf(re[e + 1] + "]", e + 2, StringComparison.Ordinal); if (ce > 0) e = ce + 1; } e++; }
                    if (e >= re.Length) { sb.Append("\\["); continue; }
                    string body = re.Substring(i + 1, e - i - 1);
                    var cb = new StringBuilder("[");
                    int k = 0;
                    if (body.StartsWith("^", StringComparison.Ordinal)) { cb.Append('^'); k = 1; }
                    for (; k < body.Length; k++)
                    {
                        if (body[k] == '[' && k + 1 < body.Length && body[k + 1] == ':') { int ce = body.IndexOf(":]", k + 2, StringComparison.Ordinal); cb.Append(Sh.PosixClass(body.Substring(k + 2, ce - k - 2))); k = ce + 1; continue; }
                        if (body[k] == '\\' || body[k] == '[' || (body[k] == '^' && k > 0)) cb.Append('\\');
                        cb.Append(body[k]);
                    }
                    cb.Append(']');
                    sb.Append(cb);
                    i = e;
                    continue;
                }
                if (c == '\\' && i + 1 < re.Length)
                {
                    char d = re[++i];
                    if (!ere && (d == '(' || d == ')' || d == '{' || d == '}' || d == '+' || d == '?' || d == '|')) { sb.Append(d); continue; }
                    if (d == '<') { sb.Append("\\b(?=\\w)"); continue; }
                    if (d == '>') { sb.Append("\\b(?<=\\w)"); continue; }
                    if (d == 'w' || d == 'W' || d == 's' || d == 'S' || d == 'b' || d == 'B' || char.IsDigit(d)) { sb.Append('\\').Append(d); continue; }
                    if (d == 'n') { sb.Append("\\n"); continue; }
                    if (d == 't') { sb.Append("\\t"); continue; }
                    sb.Append(Regex.Escape(d.ToString()));
                    continue;
                }
                if (!ere && (c == '(' || c == ')' || c == '{' || c == '}' || c == '+' || c == '?' || c == '|')) { sb.Append('\\').Append(c); continue; }
                if (!ere && c == '*' && (sb.Length == 0 || sb.ToString() == "^")) { sb.Append("\\*"); continue; }
                if (ere && c == '{' && !Regex.IsMatch(re.Substring(i), "^\\{\\d*(,\\d*)?\\}")) { sb.Append("\\{"); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static Regex MakeRx(string re, bool ere, bool fixedStr, bool icase, bool word, bool line)
        {
            string p = fixedStr ? Regex.Escape(re) : PosixToNet(re, ere);
            if (word) p = "(?<![\\w])(?:" + p + ")(?![\\w])";
            if (line) p = "^(?:" + p + ")$";
            var opts = RegexOptions.CultureInvariant | (icase ? RegexOptions.IgnoreCase : RegexOptions.None);
            try { return new Regex(p, opts); }
            catch (ArgumentException) { throw new ShError("Unmatched ( or \\(", 2); }
        }

        // ------------------------------------------------------------ grep
        static int Grep(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ivclLnrRwxEFoqshHA:B:C:e:f:PZzam:", "ignore-case|i,invert-match|v,count|c,files-with-matches|l,files-without-match|L,line-number|n,recursive|r,dereference-recursive|R,word-regexp|w,line-regexp|x,extended-regexp|E,fixed-strings|F,only-matching|o,quiet|q,silent|q,no-messages|s,no-filename|h,with-filename|H,after-context=|A,before-context=|B,context=|C,regexp=|e,file=|f,color=?,colour=?,include=,exclude=,max-count=|m,perl-regexp|P,null|Z,text|a,binary-files=", "grep");
            if (o.Error != null) { io.E(o.Error.Replace("Try 'grep --help'", "Usage: grep [OPTION]... PATTERNS [FILE]...\nTry 'grep --help'")); return 2; }
            var pats = new List<string>(o.All("e"));
            var args = new List<string>(o.Args);
            if (o.Has("f")) { try { pats.AddRange(Lines(Read(sh, o.Get("f")))); } catch (VfsEx e) { io.E("grep: " + o.Get("f") + ": " + e.Message); return 2; } }
            if (pats.Count == 0)
            {
                if (args.Count == 0) { io.E("Usage: grep [OPTION]... PATTERNS [FILE]...\nTry 'grep --help' for more information."); return 2; }
                pats.AddRange(args[0].Split('\n')); args.RemoveAt(0);
            }
            bool ere = o.Has("E") || o.Has("P"), fx = o.Has("F");
            var rxs = new List<Regex>();
            try { foreach (var p in pats) rxs.Add(MakeRx(p, ere, fx, o.Has("i"), o.Has("w"), o.Has("x"))); }
            catch (ShError e) { io.E("grep: " + e.Message); return 2; }
            bool rec = o.Has("r") || o.Has("R");
            if (args.Count == 0 && rec) args.Add(".");
            var files = new List<string>();
            int st = 1, err = 0;
            if (rec)
            {
                foreach (var f in args)
                {
                    try
                    {
                        var l = Find(sh, f, true);
                        if (!l.Exists) { if (!o.Has("s")) io.E("grep: " + f + ": No such file or directory"); err = 2; continue; }
                        GrepCollect(sh, io, l, f, files, o);
                    }
                    catch (VfsEx e) { if (!o.Has("s")) io.E("grep: " + f + ": " + e.Message); err = 2; }
                }
            }
            else files = args;
            bool showName = o.Has("H") || (!o.Has("h") && (files.Count > 1 || rec));
            int after = o.Has("A") ? int.Parse(o.Get("A")) : (o.Has("C") ? int.Parse(o.Get("C")) : 0);
            int before = o.Has("B") ? int.Parse(o.Get("B")) : (o.Has("C") ? int.Parse(o.Get("C")) : 0);
            int maxCount = o.Has("m") ? int.Parse(o.Get("m")) : int.MaxValue;
            var inputs = new List<KeyValuePair<string, string>>();
            if (files.Count == 0) inputs.Add(new KeyValuePair<string, string>("(standard input)", io.In.ReadAll()));
            else
                foreach (var f in files)
                {
                    if (f == "-") { inputs.Add(new KeyValuePair<string, string>("(standard input)", io.In.ReadAll())); continue; }
                    try
                    {
                        var l = Find(sh, f, true);
                        if (!l.Exists) { if (!o.Has("s")) io.E("grep: " + f + ": No such file or directory"); err = 2; continue; }
                        if (l.Node.T == 'd') { if (!o.Has("s")) io.E("grep: " + f + ": Is a directory"); continue; }
                        inputs.Add(new KeyValuePair<string, string>(f, sh.M.V.ReadNode(sh.P, l)));
                    }
                    catch (VfsEx e) { if (!o.Has("s")) io.E("grep: " + f + ": " + e.Message); err = 2; }
                }
            foreach (var kv in inputs)
            {
                if (kv.Value.Length > 0 && kv.Value.Contains("\u0000") || kv.Value.StartsWith("\u0001", StringComparison.Ordinal))
                {
                    if (rxs.Any(r => r.IsMatch(kv.Value)) != o.Has("v")) { st = 0; if (!o.Has("q") && !o.Has("c")) io.L("grep: " + kv.Key + ": binary file matches"); }
                    continue;
                }
                var lines = Lines(kv.Value);
                int count = 0;
                int lastPrinted = -1, afterLeft = 0;
                string prefixName = showName ? kv.Key : null;
                for (int i = 0; i < lines.Count && count < maxCount; i++)
                {
                    bool m = rxs.Any(r => r.IsMatch(lines[i]));
                    if (o.Has("v")) m = !m;
                    if (m)
                    {
                        count++; st = 0;
                        if (o.Has("q")) return 0;
                        if (o.Has("l") || o.Has("L") || o.Has("c")) continue;
                        if (before > 0 || after > 0)
                        {
                            if (lastPrinted >= 0 && i - before > lastPrinted + 1) io.L("--");
                            for (int b = Math.Max(lastPrinted + 1, i - before); b < i; b++) io.L((prefixName != null ? prefixName + "-" : "") + (o.Has("n") ? (b + 1) + "-" : "") + lines[b]);
                        }
                        if (o.Has("o") && !o.Has("v"))
                        {
                            foreach (var r in rxs) foreach (Match mm in r.Matches(lines[i])) if (mm.Length > 0) io.L((prefixName != null ? prefixName + ":" : "") + (o.Has("n") ? (i + 1) + ":" : "") + mm.Value);
                        }
                        else io.L((prefixName != null ? prefixName + ":" : "") + (o.Has("n") ? (i + 1) + ":" : "") + lines[i]);
                        lastPrinted = i; afterLeft = after;
                    }
                    else if (afterLeft > 0 && !o.Has("l") && !o.Has("c") && !o.Has("q"))
                    {
                        io.L((prefixName != null ? prefixName + "-" : "") + (o.Has("n") ? (i + 1) + "-" : "") + lines[i]);
                        lastPrinted = i; afterLeft--;
                    }
                }
                if (o.Has("c")) io.L((prefixName != null ? prefixName + ":" : "") + count);
                if (o.Has("l") && count > 0) io.L(kv.Key);
                if (o.Has("L") && count == 0) { io.L(kv.Key); st = 0; }
            }
            return err != 0 && st != 0 ? 2 : (err != 0 && o.Has("q") ? 2 : (st == 0 ? 0 : (err != 0 ? 2 : 1)));
        }
        static void GrepCollect(Sh sh, Io io, Loc l, string shown, List<string> files, Opts o)
        {
            if (l.Node.T != 'd') { files.Add(shown); return; }
            if (!sh.M.V.Can(sh.P.Cr, l.Node, 5)) { if (!o.Has("s")) io.E("grep: " + shown + ": Permission denied"); return; }
            foreach (var k in Vfs.Sorted(l.Node.Kids.Keys))
            {
                Loc cl;
                try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), o.Has("R")); } catch (VfsEx) { continue; }
                if (!cl.Exists || cl.Node.T == 'l') continue;
                string p = shown.TrimEnd('/') + "/" + k;
                if (shown == ".") p = "./" + k;
                if (cl.Node.T == 'd') GrepCollect(sh, io, cl, p, files, o);
                else if (cl.Node.T == 'f')
                {
                    if (o.Has("include") && !Sh.GlobMatch(o.Get("include"), k)) continue;
                    if (o.Has("exclude") && Sh.GlobMatch(o.Get("exclude"), k)) continue;
                    if (!sh.M.V.Can(sh.P.Cr, cl.Node, 4)) { if (!o.Has("s")) io.E("grep: " + p + ": Permission denied"); continue; }
                    files.Add(p);
                }
            }
        }

        // ------------------------------------------------------------ sed
        class SedCmd { public string A1, A2; public bool Neg; public char C; public Regex Rx; public string Rep; public bool G, Print, Icase; public int Nth = 1; public string Text; public string Y1, Y2; public List<SedCmd> Block; public bool InRange; public string WFile; }

        static int Sed(Sh sh, List<string> a, Io io)
        {
            // GNU sed: -i's suffix must be attached (-i.bak); a lone -i (also inside -ni, -Ei) has none
            a = a.Select((x, k) => k > 0 && Regex.IsMatch(x, "^-[nErsuz]*i$") ? (x.Length > 2 ? x.Substring(0, x.Length - 1) + "\u0001" : "--in-place=") : x).SelectMany(x => x.EndsWith("\u0001", StringComparison.Ordinal) ? new[] { x.TrimEnd('\u0001'), "--in-place=" } : new[] { x }).ToList();
            var o = Opts.Parse(a, "ne:f:i:Ersuz", "quiet|n,silent|n,expression=|e,file=|f,in-place=?|i,regexp-extended|E,separate|s,unbuffered|u,null-data|z,posix,debug", "sed");
            // -i may have no suffix: "sed -i 's/a/b/' f" parses the script as the suffix
            var args = new List<string>(o.Args);
            var scripts = new List<string>(o.All("e"));
            string inplace = o.Has("i") ? o.Get("i") : null;
            if (o.Error != null)
            {
                return Fail(io, o);
            }
            if (inplace != null && inplace.Length > 0 && LooksLikeSedScript(inplace) && scripts.Count == 0) { scripts.Add(inplace); inplace = ""; }
            if (o.Has("f")) { try { scripts.Add(Read(sh, o.Get("f"))); } catch (VfsEx e) { io.E("sed: couldn't open file " + o.Get("f") + ": " + e.Message); return 1; } }
            if (scripts.Count == 0) { if (args.Count == 0) { io.E("Usage: sed [OPTION]... {script-only-if-no-other-script} [input-file]..."); return 1; } scripts.Add(args[0]); args.RemoveAt(0); }
            bool ere = o.Has("E") || o.Has("r");
            List<SedCmd> prog;
            try { prog = SedParse(string.Join("\n", scripts.ToArray()), ere); }
            catch (ShError e) { io.E("sed: -e expression #1, char " + e.Status + ": " + e.Message); return 1; }
            bool quiet = o.Has("n");
            int st = 0;
            if (inplace != null)
            {
                if (args.Count == 0) { io.E("sed: no input files"); return 1; }
                foreach (var f in args)
                {
                    try
                    {
                        var l = Find(sh, f, true);
                        if (!l.Exists) { io.E("sed: can't read " + f + ": No such file or directory"); st = 2; continue; }
                        string text = sh.M.V.ReadNode(sh.P, l);
                        var outp = new StringBuilder();
                        bool quit;
                        SedRun(sh, prog, text, quiet, outp, out quit);
                        if (inplace.Length > 0) { var bl = Find(sh, f + inplace, true); sh.M.V.WriteLoc(sh.P, bl, text, false, 0x1A4); }
                        // sed -i writes a new file in the same folder and renames it over the old one; GNU sed keeps the
                        // owner, mode and SELinux label of the original
                        if (!sh.M.V.Can(sh.P.Cr, l.Parent ?? l.Node, 2)) throw new VfsEx("EACCES");
                        if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
                        l.Node.Text = outp.ToString(); l.Node.BinSize = -1; l.Node.Mtime = sh.M.Now;
                    }
                    catch (VfsEx e) { io.E("sed: couldn't edit " + f + ": " + e.Message); st = 4; }
                }
                return st;
            }
            var all = new StringBuilder();
            foreach (var kv in Inputs(sh, args, io, "sed", ref st)) all.Append(kv.Value);
            var res = new StringBuilder();
            bool q2;
            SedRun(sh, prog, all.ToString(), quiet, res, out q2);
            io.P(res.ToString());
            return st;
        }
        static bool LooksLikeSedScript(string s) { return Regex.IsMatch(s, "^([0-9$,/!]|s[^a-zA-Z0-9]|[dpqyaic=])"); }

        static List<SedCmd> SedParse(string s, bool ere)
        {
            int i = 0;
            return SedParseList(s, ref i, ere, false);
        }
        static List<SedCmd> SedParseList(string s, ref int i, bool ere, bool inBlock)
        {
            var l = new List<SedCmd>();
            while (i < s.Length)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ';')) i++;
                if (i >= s.Length) break;
                if (s[i] == '}') { if (!inBlock) throw new ShError("unexpected `}'", i + 1); i++; return l; }
                if (s[i] == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
                var c = new SedCmd();
                c.A1 = SedAddr(s, ref i, ere);
                if (c.A1 != null && i < s.Length && s[i] == ',') { i++; c.A2 = SedAddr(s, ref i, ere); }
                while (i < s.Length && s[i] == ' ') i++;
                if (i < s.Length && s[i] == '!') { c.Neg = true; i++; while (i < s.Length && s[i] == ' ') i++; }
                if (i >= s.Length) throw new ShError("missing command", i);
                c.C = s[i++];
                switch (c.C)
                {
                    case '{': c.Block = SedParseList(s, ref i, ere, true); break;
                    case 's':
                        {
                            char d = s[i++];
                            string re = SedPart(s, ref i, d), rep = SedPart(s, ref i, d);
                            bool icase = false;
                            while (i < s.Length && "gpiI0123456789we".IndexOf(s[i]) >= 0)
                            {
                                char f = s[i++];
                                if (f == 'g') c.G = true; else if (f == 'p') c.Print = true; else if (f == 'i' || f == 'I') icase = true;
                                else if (char.IsDigit(f)) { int n = f - '0'; while (i < s.Length && char.IsDigit(s[i])) n = n * 10 + (s[i++] - '0'); c.Nth = n; }
                                else if (f == 'w') { int e = s.IndexOf('\n', i); c.WFile = (e < 0 ? s.Substring(i) : s.Substring(i, e - i)).Trim(); i = e < 0 ? s.Length : e; }
                            }
                            c.Rx = new Regex(re.Length == 0 ? "(?!)" : PosixToNet(re, ere), RegexOptions.CultureInvariant | (icase ? RegexOptions.IgnoreCase : RegexOptions.None));
                            c.Rep = rep;
                            break;
                        }
                    case 'y': { char d = s[i++]; c.Y1 = SedPart(s, ref i, d); c.Y2 = SedPart(s, ref i, d); if (c.Y1.Length != c.Y2.Length) throw new ShError("strings for `y' command are different lengths", i); break; }
                    case 'a': case 'i': case 'c':
                        {
                            while (i < s.Length && s[i] == ' ') i++;
                            if (i < s.Length && s[i] == '\\') { i++; if (i < s.Length && s[i] == '\n') i++; }
                            int e = s.IndexOf('\n', i);
                            string t = e < 0 ? s.Substring(i) : s.Substring(i, e - i);
                            i = e < 0 ? s.Length : e;
                            c.Text = t.Replace("\\n", "\n").Replace("\\t", "\t");
                            break;
                        }
                    case 'd': case 'p': case 'q': case '=': case 'n': case 'N': case 'D': case 'P': case 'h': case 'H': case 'g': case 'G': case 'x': case 'l': case 'Q': case 'z': break;
                    case 'w': case 'r': { int e = s.IndexOf('\n', i); c.Text = (e < 0 ? s.Substring(i) : s.Substring(i, e - i)).Trim(); i = e < 0 ? s.Length : e; break; }
                    default: throw new ShError("unknown command: `" + c.C + "'", i);
                }
                l.Add(c);
            }
            if (inBlock) throw new ShError("unmatched `{'", i);
            return l;
        }
        static string SedAddr(string s, ref int i, bool ere)
        {
            if (i >= s.Length) return null;
            if (char.IsDigit(s[i])) { int st = i; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '~')) i++; return s.Substring(st, i - st); }
            if (s[i] == '$') { i++; return "$"; }
            if (s[i] == '/' || s[i] == '\\')
            {
                char d = s[i] == '\\' ? s[++i] : '/';
                i++;
                string re = SedPart(s, ref i, d);
                bool ic = i < s.Length && s[i] == 'I'; if (ic) i++;
                return (ic ? "I" : "/") + PosixToNet(re, ere);
            }
            if (s[i] == '+' && false) return null;
            return null;
        }
        static string SedPart(string s, ref int i, char d)
        {
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != d)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { if (s[i + 1] == d) { sb.Append(d); i += 2; continue; } if (s[i + 1] == 'n') { sb.Append("\\n"); i += 2; continue; } sb.Append(s[i]).Append(s[i + 1]); i += 2; continue; }
                sb.Append(s[i++]);
            }
            if (i >= s.Length) throw new ShError("unterminated `s' command", i);
            i++;
            return sb.ToString();
        }

        static bool SedMatch(string addr, string line, int n, bool last)
        {
            if (addr == "$") return last;
            if (addr.StartsWith("/", StringComparison.Ordinal) || addr.StartsWith("I", StringComparison.Ordinal)) return Regex.IsMatch(line, addr.Substring(1), addr.StartsWith("I", StringComparison.Ordinal) ? RegexOptions.IgnoreCase : RegexOptions.None);
            if (addr.Contains("~")) { var p = addr.Split('~'); int f = int.Parse(p[0]), st = int.Parse(p[1]); return st == 0 ? n == f : n >= f && (n - f) % st == 0; }
            int v; return int.TryParse(addr, out v) && v == n;
        }

        static void SedRun(Sh sh, List<SedCmd> prog, string text, bool quiet, StringBuilder outp, out bool quit)
        {
            var lines = Lines(text);
            bool noFinalNl = text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal);
            string hold = "";
            quit = false;
            for (int idx = 0; idx < lines.Count && !quit; idx++)
            {
                string ps = lines[idx];
                var appendAfter = new StringBuilder();
                bool deleted = false;
                bool lastLine = idx == lines.Count - 1;
                SedExec(sh, prog, ref ps, ref idx, lines, ref hold, quiet, outp, appendAfter, ref deleted, ref quit, idx + 1);
                if (!deleted && !quiet) outp.Append(ps).Append(lastLine && noFinalNl && appendAfter.Length == 0 ? "" : "\n");
                outp.Append(appendAfter);
            }
        }

        static void SedExec(Sh sh, List<SedCmd> prog, ref string ps, ref int idx, List<string> lines, ref string hold, bool quiet, StringBuilder outp, StringBuilder after, ref bool deleted, ref bool quit, int lineNo)
        {
            foreach (var c in prog)
            {
                if (deleted || quit) return;
                int n = idx + 1;
                bool last = idx == lines.Count - 1;
                bool sel;
                if (c.A1 == null) sel = true;
                else if (c.A2 == null) sel = SedMatch(c.A1, ps, n, last);
                else
                {
                    if (!c.InRange)
                    {
                        sel = SedMatch(c.A1, ps, n, last);
                        if (sel)
                        {
                            int a2; bool num = int.TryParse(c.A2, out a2);
                            c.InRange = !(num && a2 <= n) && c.A2 != "$" || c.A2 == "$" && !last;
                            if (c.A2.StartsWith("+", StringComparison.Ordinal)) { }
                        }
                    }
                    else
                    {
                        sel = true;
                        int a2;
                        if (int.TryParse(c.A2, out a2)) { if (n >= a2) c.InRange = false; }
                        else if (SedMatch(c.A2, ps, n, last)) c.InRange = false;
                    }
                }
                if (c.Neg) sel = !sel;
                if (!sel) continue;
                switch (c.C)
                {
                    case '{': SedExec(sh, c.Block, ref ps, ref idx, lines, ref hold, quiet, outp, after, ref deleted, ref quit, lineNo); break;
                    case 's':
                        {
                            int count = 0; bool did = false;
                            string rep = c.Rep;
                            string res = c.Rx.Replace(ps, m =>
                            {
                                count++;
                                if (!c.G && count != c.Nth) return m.Value;
                                if (c.G && count < c.Nth) return m.Value;
                                did = true;
                                return SedRep(rep, m);
                            });
                            if (did) { ps = res; if (c.Print) outp.Append(ps).Append('\n'); if (c.WFile != null) { try { var l = Find(sh, c.WFile, true); sh.M.V.WriteLoc(sh.P, l, ps + "\n", true, 0x1A4); } catch (VfsEx) { } } }
                            break;
                        }
                    case 'y': { var sb = new StringBuilder(); foreach (char ch in ps) { int k = c.Y1.IndexOf(ch); sb.Append(k >= 0 ? c.Y2[k] : ch); } ps = sb.ToString(); break; }
                    case 'd': deleted = true; return;
                    case 'D': { int nl = ps.IndexOf('\n'); if (nl < 0) { deleted = true; return; } ps = ps.Substring(nl + 1); break; }
                    case 'p': outp.Append(ps).Append('\n'); break;
                    case 'P': outp.Append(ps.Split('\n')[0]).Append('\n'); break;
                    case '=': outp.Append(n).Append('\n'); break;
                    case 'a': after.Append(c.Text).Append('\n'); break;
                    case 'i': outp.Append(c.Text).Append('\n'); break;
                    case 'c': if (c.A2 == null || !c.InRange) outp.Append(c.Text).Append('\n'); deleted = true; return;
                    case 'q': quit = true; return;
                    case 'Q': quit = true; deleted = true; return;
                    case 'n': if (!quiet) outp.Append(ps).Append('\n'); if (idx + 1 < lines.Count) { idx++; ps = lines[idx]; } else { quit = true; return; } break;
                    case 'N': if (idx + 1 < lines.Count) { idx++; ps = ps + "\n" + lines[idx]; } else { return; } break;
                    case 'h': hold = ps; break;
                    case 'H': hold = hold + "\n" + ps; break;
                    case 'g': ps = hold; break;
                    case 'G': ps = ps + "\n" + hold; break;
                    case 'x': { var t = ps; ps = hold; hold = t; break; }
                    case 'z': ps = ""; break;
                    case 'l': outp.Append(ps.Replace("\\", "\\\\").Replace("\t", "\\t")).Append("$\n"); break;
                    case 'w': try { var l = Find(sh, c.Text, true); sh.M.V.WriteLoc(sh.P, l, ps + "\n", true, 0x1A4); } catch (VfsEx) { } break;
                    case 'r': try { after.Append(Read(sh, c.Text)); } catch (VfsEx) { } break;
                }
            }
        }
        static string SedRep(string rep, Match m)
        {
            var sb = new StringBuilder();
            bool upper = false, lower = false;
            for (int i = 0; i < rep.Length; i++)
            {
                char c = rep[i];
                string add = null;
                if (c == '&') add = m.Value;
                else if (c == '\\' && i + 1 < rep.Length)
                {
                    char d = rep[++i];
                    if (char.IsDigit(d)) add = m.Groups[d - '0'].Value;
                    else if (d == 'n') add = "\n";
                    else if (d == 't') add = "\t";
                    else if (d == 'U') { upper = true; lower = false; continue; }
                    else if (d == 'L') { lower = true; upper = false; continue; }
                    else if (d == 'E') { upper = lower = false; continue; }
                    else add = d.ToString();
                }
                else add = c.ToString();
                sb.Append(upper ? add.ToUpperInvariant() : lower ? add.ToLowerInvariant() : add);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ head / tail
        static List<string> NumOpt(List<string> a, string flag)
        {
            // head -5  ->  head -n 5
            var b = new List<string>(a);
            for (int i = 1; i < b.Count; i++) if (Regex.IsMatch(b[i], "^-\\d+$")) { b[i] = flag + b[i].Substring(1); }
            return b;
        }
        static int Head(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(NumOpt(a, "-n"), "n:c:qv", "lines=|n,bytes=|c,quiet|q,silent|q,verbose|v", "head");
            if (o.Error != null) return Fail(io, o);
            int st = 0;
            var ins = Inputs(sh, o.Args, io, "head", ref st);
            bool names = (ins.Count > 1 && !o.Has("q")) || o.Has("v");
            bool first = true;
            foreach (var kv in ins)
            {
                if (names) { if (!first) io.L(""); io.L("==> " + (kv.Key == "-" ? "standard input" : kv.Key) + " <=="); }
                first = false;
                if (o.Has("c")) { long c = ParseSize(o.Get("c").TrimStart('-'), 1); io.P(kv.Value.Substring(0, (int)Math.Min(c, kv.Value.Length))); continue; }
                string ns = o.Get("n", "10");
                var lines = Lines(kv.Value);
                int n;
                if (ns.StartsWith("-", StringComparison.Ordinal)) { int.TryParse(ns.Substring(1), out n); foreach (var l in lines.Take(Math.Max(0, lines.Count - n))) io.L(l); }
                else { if (!int.TryParse(ns, out n)) { io.E("head: invalid number of lines: '" + ns + "'"); return 1; } foreach (var l in lines.Take(n)) io.L(l); }
            }
            return st;
        }
        static int Tail(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(NumOpt(a, "-n"), "n:c:fFqv", "lines=|n,bytes=|c,follow=?|f,quiet|q,verbose|v,retry,pid=,sleep-interval=", "tail");
            if (o.Error != null) return Fail(io, o);
            int st = 0;
            var ins = Inputs(sh, o.Args, io, "tail", ref st);
            bool names = (ins.Count > 1 && !o.Has("q")) || o.Has("v");
            bool first = true;
            foreach (var kv in ins)
            {
                if (names) { if (!first) io.L(""); io.L("==> " + (kv.Key == "-" ? "standard input" : kv.Key) + " <=="); }
                first = false;
                if (o.Has("c")) { long c = ParseSize(o.Get("c").TrimStart('+'), 1); io.P(kv.Value.Substring((int)Math.Max(0, kv.Value.Length - c))); continue; }
                string ns = o.Get("n", "10");
                var lines = Lines(kv.Value);
                int n;
                if (ns.StartsWith("+", StringComparison.Ordinal)) { int.TryParse(ns.Substring(1), out n); foreach (var l in lines.Skip(Math.Max(0, n - 1))) io.L(l); }
                else { if (!int.TryParse(ns.TrimStart('-'), out n)) { io.E("tail: invalid number of lines: '" + ns + "'"); return 1; } foreach (var l in lines.Skip(Math.Max(0, lines.Count - n))) io.L(l); }
            }
            if ((o.Has("f") || o.Has("F")) && sh.S != null && io.Out.IsTty)
            {
                try { sh.S.WaitForBreak(); } catch (BreakSignal) { io.P("^C\n"); return 130; }
            }
            return st;
        }

        // ------------------------------------------------------------ wc
        static int Wc(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lwcmL", "lines|l,words|w,bytes|c,chars|m,max-line-length|L", "wc");
            if (o.Error != null) return Fail(io, o);
            bool l = o.Has("l"), w = o.Has("w"), c = o.Has("c"), m = o.Has("m"), L = o.Has("L");
            if (!l && !w && !c && !m && !L) { l = w = c = true; }
            int st = 0;
            var ins = Inputs(sh, o.Args, io, "wc", ref st);
            var rows = new List<long[]>(); var names = new List<string>();
            long[] tot = new long[5];
            foreach (var kv in ins)
            {
                string t = kv.Value;
                long nl = t.Count(ch => ch == '\n'), nw = Regex.Matches(t, "\\S+").Count, nb = Encoding.UTF8.GetByteCount(t), nc = t.Length, ml = t.Split('\n').Select(x => (long)x.Length).DefaultIfEmpty(0).Max();
                var r = new[] { nl, nw, nc, nb, ml };
                for (int i = 0; i < 5; i++) tot[i] = i == 4 ? Math.Max(tot[i], r[i]) : tot[i] + r[i];
                rows.Add(r); names.Add(kv.Key == "-" ? "" : kv.Key);
            }
            if (ins.Count > 1) { rows.Add(tot); names.Add("total"); }
            int width = ins.Count == 1 && o.Args.Count <= 1 && (Convert.ToInt32(l) + Convert.ToInt32(w) + Convert.ToInt32(c) + Convert.ToInt32(m) + Convert.ToInt32(L)) == 1 ? 1 : Math.Max(1, rows.SelectMany(r => r).Select(x => x.ToString().Length).DefaultIfEmpty(1).Max());
            if (o.Args.Count == 0 && (Convert.ToInt32(l) + Convert.ToInt32(w) + Convert.ToInt32(c) + Convert.ToInt32(m)) > 1) width = 7;
            for (int i = 0; i < rows.Count; i++)
            {
                var parts = new List<string>();
                if (l) parts.Add(rows[i][0].ToString().PadLeft(width));
                if (w) parts.Add(rows[i][1].ToString().PadLeft(width));
                if (m) parts.Add(rows[i][2].ToString().PadLeft(width));
                if (c) parts.Add(rows[i][3].ToString().PadLeft(width));
                if (L) parts.Add(rows[i][4].ToString().PadLeft(width));
                io.L(string.Join(" ", parts.ToArray()) + (names[i].Length > 0 ? " " + names[i] : ""));
            }
            return st;
        }

        // ------------------------------------------------------------ sort / uniq / cut / tr
        static int Sort(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "nrufk:t:hMVbdgo:cR", "numeric-sort|n,reverse|r,unique|u,ignore-case|f,key=|k,field-separator=|t,human-numeric-sort|h,month-sort|M,version-sort|V,ignore-leading-blanks|b,general-numeric-sort|g,output=|o,check|c,random-sort|R", "sort");
            if (o.Error != null) return Fail(io, o);
            int st = 0;
            var lines = new List<string>();
            foreach (var kv in Inputs(sh, o.Args, io, "sort", ref st)) lines.AddRange(Lines(kv.Value));
            string sep = o.Get("t");
            var keys = o.All("k");
            Func<string, string> key = line =>
            {
                if (keys.Count == 0) return line;
                var k = keys[0];
                var kp = k.Split(',');
                int f1 = int.Parse(Regex.Match(kp[0], "^\\d+").Value), f2 = kp.Length > 1 ? int.Parse(Regex.Match(kp[1], "^\\d+").Value) : int.MaxValue;
                var fields = sep != null ? line.Split(sep[0]) : Regex.Split(line.Trim(), "\\s+");
                var sel = fields.Skip(f1 - 1).Take(f2 == int.MaxValue ? fields.Length : f2 - f1 + 1);
                return string.Join(sep ?? " ", sel.ToArray());
            };
            bool num = o.Has("n") || o.Has("g") || keys.Any(k => k.Contains("n")), human = o.Has("h") || keys.Any(k => k.Contains("h")), rev = o.Has("r") || keys.Any(k => Regex.IsMatch(k, "\\d+r")), fold = o.Has("f");
            Comparison<string> cmp = (x, y) =>
            {
                string kx = key(x), ky = key(y);
                int r;
                if (num || human)
                {
                    double dx = HumanNum(kx, human), dy = HumanNum(ky, human);
                    r = dx.CompareTo(dy);
                    if (r == 0) r = string.CompareOrdinal(x, y);
                }
                else if (o.Has("V")) r = VersionCmp(kx, ky);
                else
                {
                    r = SortCollate(fold ? kx.ToLowerInvariant() : kx, fold ? ky.ToLowerInvariant() : ky);
                    if (r == 0 && keys.Count > 0) r = SortCollate(x, y);
                }
                return rev ? -r : r;
            };
            var sorted = lines.ToList();
            if (o.Has("R")) { var rnd = new Random(sh.M.Seed); sorted = sorted.OrderBy(x => rnd.Next()).ToList(); }
            else sorted = sorted.OrderBy(x => x, Comparer<string>.Create(cmp)).ToList();
            if (o.Has("u")) { var u = new List<string>(); foreach (var x in sorted) if (u.Count == 0 || cmp(u[u.Count - 1], x) != 0 && (fold ? u[u.Count - 1].ToLowerInvariant() != x.ToLowerInvariant() : true)) u.Add(x); sorted = u; }
            if (o.Has("c")) { for (int i = 1; i < lines.Count; i++) if (cmp(lines[i - 1], lines[i]) > 0) { io.E("sort: -:" + (i + 1) + ": disorder: " + lines[i]); return 1; } return 0; }
            if (o.Has("o")) { try { var l = Find(sh, o.Get("o"), true); sh.M.V.WriteLoc(sh.P, l, JoinLines(sorted), false, 0x1A4); } catch (VfsEx e) { io.E("sort: open failed: " + o.Get("o") + ": " + e.Message); return 2; } return st; }
            io.P(JoinLines(sorted));
            return st;
        }
        static int SortCollate(string a, string b)
        {
            // like en_US.UTF-8: letters and digits first, case folded; punctuation only breaks ties
            int r = string.CompareOrdinal(Alnum(a).ToLowerInvariant(), Alnum(b).ToLowerInvariant());
            if (r != 0) return r;
            r = string.CompareOrdinal(Alnum(b), Alnum(a));
            if (r != 0) return r;
            return string.CompareOrdinal(a, b);
        }
        static string Alnum(string s) { var sb = new StringBuilder(); foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c); return sb.ToString(); }
        static double HumanNum(string s, bool human)
        {
            var m = Regex.Match(s.TrimStart(), "^-?\\d+(\\.\\d+)?");
            if (!m.Success) return double.NegativeInfinity == 0 ? 0 : -1e300 * 0;
            double v = double.Parse(m.Value, CultureInfo.InvariantCulture);
            if (human && s.TrimStart().Length > m.Length)
            {
                char u = char.ToUpperInvariant(s.TrimStart()[m.Length]);
                int p = "KMGTPE".IndexOf(u);
                if (p >= 0) v *= Math.Pow(1024, p + 1);
            }
            return v;
        }
        static int VersionCmp(string a, string b)
        {
            var pa = Regex.Split(a, "(\\d+)"); var pb = Regex.Split(b, "(\\d+)");
            for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            {
                long x, y;
                int r = long.TryParse(pa[i], out x) && long.TryParse(pb[i], out y) ? x.CompareTo(y) : string.CompareOrdinal(pa[i], pb[i]);
                if (r != 0) return r;
            }
            return pa.Length.CompareTo(pb.Length);
        }

        static int Uniq(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "cduif:", "count|c,repeated|d,unique|u,ignore-case|i,skip-fields=|f", "uniq");
            if (o.Error != null) return Fail(io, o);
            int st = 0;
            var ins = Inputs(sh, o.Args.Take(1).ToList(), io, "uniq", ref st);
            var lines = ins.Count > 0 ? Lines(ins[0].Value) : new List<string>();
            var groups = new List<KeyValuePair<string, int>>();
            foreach (var l in lines)
            {
                if (groups.Count > 0 && (o.Has("i") ? string.Equals(groups[groups.Count - 1].Key, l, StringComparison.OrdinalIgnoreCase) : groups[groups.Count - 1].Key == l)) groups[groups.Count - 1] = new KeyValuePair<string, int>(groups[groups.Count - 1].Key, groups[groups.Count - 1].Value + 1);
                else groups.Add(new KeyValuePair<string, int>(l, 1));
            }
            var sb = new StringBuilder();
            foreach (var g in groups)
            {
                if (o.Has("d") && g.Value < 2) continue;
                if (o.Has("u") && g.Value > 1) continue;
                sb.Append(o.Has("c") ? g.Value.ToString().PadLeft(7) + " " + g.Key : g.Key).Append('\n');
            }
            if (o.Args.Count > 1) { try { var l = Find(sh, o.Args[1], true); sh.M.V.WriteLoc(sh.P, l, sb.ToString(), false, 0x1A4); } catch (VfsEx e) { io.E("uniq: " + o.Args[1] + ": " + e.Message); return 1; } }
            else io.P(sb.ToString());
            return st;
        }

        static List<int[]> Ranges(string spec)
        {
            var l = new List<int[]>();
            foreach (var p in spec.Split(','))
            {
                if (p.Length == 0) continue;
                var r = p.Split('-');
                int a = r[0].Length == 0 ? 1 : int.Parse(r[0]);
                int b = r.Length == 1 ? a : (r[1].Length == 0 ? int.MaxValue : int.Parse(r[1]));
                l.Add(new[] { a, b });
            }
            return l;
        }
        static int Cut(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "d:f:c:b:sz", "delimiter=|d,fields=|f,characters=|c,bytes=|b,only-delimited|s,complement,output-delimiter=", "cut");
            if (o.Error != null) return Fail(io, o);
            if (!o.Has("f") && !o.Has("c") && !o.Has("b")) { io.E("cut: you must specify a list of bytes, characters, or fields\nTry 'cut --help' for more information."); return 1; }
            int st = 0;
            string d = o.Get("d", "\t");
            if (d.Length != 1) { io.E("cut: the delimiter must be a single character\nTry 'cut --help' for more information."); return 1; }
            string od = o.Get("output-delimiter") ?? d;
            List<int[]> rs;
            try { rs = Ranges(o.Get("f") ?? o.Get("c") ?? o.Get("b")); } catch (Exception) { io.E("cut: invalid field value"); return 1; }
            Func<int, bool> inR = n => rs.Any(r => n >= r[0] && n <= r[1]);
            var sb = new StringBuilder();
            foreach (var kv in Inputs(sh, o.Args, io, "cut", ref st))
                foreach (var line in Lines(kv.Value))
                {
                    if (o.Has("f"))
                    {
                        if (!line.Contains(d[0])) { if (!o.Has("s")) sb.Append(line).Append('\n'); continue; }
                        var f = line.Split(d[0]);
                        var sel = new List<string>();
                        for (int i = 0; i < f.Length; i++) if (inR(i + 1) != o.Has("complement")) sel.Add(f[i]);
                        sb.Append(string.Join(od, sel.ToArray())).Append('\n');
                    }
                    else
                    {
                        var c = new StringBuilder();
                        for (int i = 0; i < line.Length; i++) if (inR(i + 1) != o.Has("complement")) c.Append(line[i]);
                        sb.Append(c).Append('\n');
                    }
                }
            io.P(sb.ToString());
            return st;
        }

        static string TrSet(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '[' && i + 1 < s.Length && s[i + 1] == ':')
                {
                    int e = s.IndexOf(":]", i + 2, StringComparison.Ordinal);
                    if (e > 0)
                    {
                        string n = s.Substring(i + 2, e - i - 2);
                        string chars = "";
                        for (int c = 0; c < 128; c++)
                        {
                            char ch = (char)c;
                            bool inc = n == "upper" ? char.IsUpper(ch) : n == "lower" ? char.IsLower(ch) : n == "digit" ? char.IsDigit(ch) : n == "alpha" ? char.IsLetter(ch) : n == "alnum" ? char.IsLetterOrDigit(ch) : n == "space" ? char.IsWhiteSpace(ch) : n == "blank" ? (ch == ' ' || ch == '\t') : n == "punct" ? char.IsPunctuation(ch) || char.IsSymbol(ch) : n == "cntrl" ? char.IsControl(ch) : false;
                            if (inc) chars += ch;
                        }
                        sb.Append(chars); i = e + 1; continue;
                    }
                }
                if (s[i] == '\\' && i + 1 < s.Length) { char d = s[++i]; sb.Append(d == 'n' ? '\n' : d == 't' ? '\t' : d == 'r' ? '\r' : d == '\\' ? '\\' : d); continue; }
                if (i + 2 < s.Length && s[i + 1] == '-') { for (char c = s[i]; c <= s[i + 2]; c++) sb.Append(c); i += 2; continue; }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
        static int Tr(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dscC", "delete|d,squeeze-repeats|s,complement|c", "tr");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("tr: missing operand\nTry 'tr --help' for more information."); return 1; }
            string s1 = TrSet(o.Args[0]), s2 = o.Args.Count > 1 ? TrSet(o.Args[1]) : "";
            bool comp = o.Has("c") || o.Has("C");
            string text = io.In.ReadAll();
            var sb = new StringBuilder();
            Func<char, bool> in1 = ch => comp ? s1.IndexOf(ch) < 0 : s1.IndexOf(ch) >= 0;
            char last = '\0'; bool hasLast = false;
            foreach (char ch in text)
            {
                char outc = ch;
                if (o.Has("d")) { if (in1(ch)) continue; }
                else if (s2.Length > 0 && in1(ch))
                {
                    int k = comp ? s2.Length - 1 : s1.IndexOf(ch);
                    outc = s2[Math.Min(k, s2.Length - 1)];
                }
                if (o.Has("s"))
                {
                    string sq = s2.Length > 0 && !o.Has("d") ? s2 : s1;
                    if (hasLast && outc == last && (sq.IndexOf(outc) >= 0 || (comp && s2.Length == 0 && in1(outc)))) continue;
                }
                sb.Append(outc); last = outc; hasLast = true;
            }
            io.P(sb.ToString());
            return 0;
        }

        // ------------------------------------------------------------ small ones
        static int Nl(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "b:w:s:n:v:", "body-numbering=|b,number-width=|w,number-separator=|s", "nl");
            int st = 0; int n = o.Has("v") ? int.Parse(o.Get("v")) : 1; bool all = o.Get("b") == "a"; int w = o.Has("w") ? int.Parse(o.Get("w")) : 6; string sep = o.Get("s", "\t");
            foreach (var kv in Inputs(sh, o.Args, io, "nl", ref st)) foreach (var l in Lines(kv.Value)) io.L(l.Trim().Length == 0 && !all ? "      " + "" : (n++).ToString().PadLeft(w) + sep + l);
            return st;
        }
        static int Paste(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "d:s", "delimiters=|d,serial|s", "paste");
            int st = 0;
            string d = Printf.Escapes(o.Get("d", "\t"), false);
            var ins = Inputs(sh, o.Args, io, "paste", ref st).Select(x => Lines(x.Value)).ToList();
            if (o.Has("s")) { foreach (var l in ins) { var sb = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(d[(i - 1) % d.Length]); sb.Append(l[i]); } io.L(sb.ToString()); } return st; }
            int max = ins.Count == 0 ? 0 : ins.Max(x => x.Count);
            for (int r = 0; r < max; r++) { var sb = new StringBuilder(); for (int c = 0; c < ins.Count; c++) { if (c > 0) sb.Append(d[(c - 1) % d.Length]); if (r < ins[c].Count) sb.Append(ins[c][r]); } io.L(sb.ToString()); }
            return st;
        }
        static int Tac(Sh sh, List<string> a, Io io) { int st = 0; foreach (var kv in Inputs(sh, a.Skip(1).ToList(), io, "tac", ref st)) { var l = Lines(kv.Value); l.Reverse(); io.P(JoinLines(l)); } return st; }
        static int Rev(Sh sh, List<string> a, Io io) { int st = 0; foreach (var kv in Inputs(sh, a.Skip(1).ToList(), io, "rev", ref st)) foreach (var l in Lines(kv.Value)) { var c = l.ToCharArray(); Array.Reverse(c); io.L(new string(c)); } return st; }
        static int Column(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ts:o:nN:", "table|t,separator=|s,output-separator=|o", "column");
            int st = 0;
            var lines = new List<string>();
            foreach (var kv in Inputs(sh, o.Args, io, "column", ref st)) lines.AddRange(Lines(kv.Value));
            if (!o.Has("t")) { foreach (var l in lines) io.L(l); return st; }
            string sep = o.Get("s");
            var rows = lines.Where(l => l.Trim().Length > 0).Select(l => sep != null ? l.Split(sep.ToCharArray()) : Regex.Split(l.Trim(), "\\s+")).ToList();
            int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            var w = new int[cols];
            foreach (var r in rows) for (int i = 0; i < r.Length; i++) w[i] = Math.Max(w[i], r[i].Length);
            string os = o.Get("o", "  ");
            foreach (var r in rows) { var sb = new StringBuilder(); for (int i = 0; i < r.Length; i++) { sb.Append(i == r.Length - 1 ? r[i] : r[i].PadRight(w[i]) + os); } io.L(sb.ToString()); }
            return st;
        }
        static int Seq(Sh sh, List<string> a, Io io)
        {
            var b = new List<string>(a);
            var o = Opts.Parse(b.Select(x => Regex.IsMatch(x, "^-\\d") ? "\u0002" + x : x).ToList(), "s:wf:", "separator=|s,equal-width|w,format=|f", "seq");
            var nums = o.Args.Select(x => x.TrimStart('\u0002')).ToList();
            if (nums.Count == 0) { io.E("seq: missing operand\nTry 'seq --help' for more information."); return 1; }
            double first = 1, step = 1, last;
            if (!double.TryParse(nums[nums.Count - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out last)) { io.E("seq: invalid floating point argument: '" + nums[nums.Count - 1] + "'"); return 1; }
            if (nums.Count >= 2) double.TryParse(nums[0], NumberStyles.Float, CultureInfo.InvariantCulture, out first);
            if (nums.Count == 3) double.TryParse(nums[1], NumberStyles.Float, CultureInfo.InvariantCulture, out step);
            if (step == 0) { io.E("seq: invalid Zero increment value: '0'"); return 1; }
            string sep = o.Get("s", "\n");
            var items = new List<string>();
            int dec = Math.Max(Decimals(nums[0]), nums.Count == 3 ? Decimals(nums[1]) : 0);
            for (double x = first; step > 0 ? x <= last + 1e-9 : x >= last - 1e-9; x += step) { items.Add(x.ToString("F" + dec, CultureInfo.InvariantCulture)); if (items.Count > 100000) break; }
            if (o.Has("w")) { int w = items.Max(x => x.Length); items = items.Select(x => x.PadLeft(w, '0')).ToList(); }
            if (o.Has("f")) { string err; items = items.Select(x => Printf.Format(o.Get("f"), new List<string> { x }, out err)).ToList(); }
            if (items.Count > 0) io.P(string.Join(sep, items.ToArray()) + "\n");
            return 0;
        }
        static int Decimals(string s) { int i = s.IndexOf('.'); return i < 0 ? 0 : s.Length - i - 1; }
        static int Base64(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dw:i", "decode|d,wrap=|w,ignore-garbage|i", "base64");
            int st = 0;
            string t = string.Join("", Inputs(sh, o.Args, io, "base64", ref st).Select(x => x.Value).ToArray());
            if (o.Has("d")) { try { io.P(Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(t, "\\s", "")))); } catch (FormatException) { io.E("base64: invalid input"); return 1; } return st; }
            string enc = Convert.ToBase64String(Encoding.UTF8.GetBytes(t));
            int w = o.Has("w") ? int.Parse(o.Get("w")) : 76;
            if (w == 0) io.L(enc); else for (int i = 0; i < enc.Length; i += w) io.L(enc.Substring(i, Math.Min(w, enc.Length - i)));
            return st;
        }
        static int Hash(Sh sh, List<string> a, Io io, string kind)
        {
            var o = Opts.Parse(a, "cbt", "check|c,binary|b,text|t,quiet,status", a[0]);
            int st = 0;
            if (o.Has("c"))
            {
                foreach (var kv in Inputs(sh, o.Args, io, a[0], ref st))
                    foreach (var line in Lines(kv.Value))
                    {
                        var m = Regex.Match(line, "^([0-9a-f]+)\\s+\\*?(.+)$");
                        if (!m.Success) continue;
                        string f = m.Groups[2].Value;
                        string got;
                        try { got = HashOf(Read(sh, f), kind); } catch (VfsEx) { io.L(f + ": FAILED open or read"); st = 1; continue; }
                        bool ok = got == m.Groups[1].Value;
                        if (!ok) st = 1;
                        if (!o.Has("status")) io.L(f + ": " + (ok ? "OK" : "FAILED"));
                    }
                return st;
            }
            foreach (var kv in Inputs(sh, o.Args, io, a[0], ref st))
            {
                if (kind == "cksum") { var b = Encoding.UTF8.GetBytes(kv.Value); io.L(((uint)FwHash.Of(kv.Value)).ToString() + " " + b.Length + (kv.Key == "-" ? "" : " " + kv.Key)); continue; }
                io.L(HashOf(kv.Value, kind) + "  " + kv.Key);
            }
            return st;
        }
        public static string HashOf(string text, string kind)
        {
            System.Security.Cryptography.HashAlgorithm h = kind == "md5" ? (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.MD5.Create() : kind == "sha1" ? System.Security.Cryptography.SHA1.Create() : kind == "sha256" ? (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create() : System.Security.Cryptography.SHA512.Create();
            using (h) return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")).ToArray());
        }

        static int Diff(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "uqrNiwBy", "unified=?|u,brief|q,recursive|r,ignore-case|i,ignore-all-space|w,side-by-side|y", "diff");
            if (o.Error != null) { io.E(o.Error); return 2; }
            if (o.Args.Count != 2) { io.E("diff: missing operand after '" + (o.Args.Count > 0 ? o.Args[0] : "diff") + "'\ndiff: Try 'diff --help' for more information."); return 2; }
            string ta, tb;
            try { ta = o.Args[0] == "-" ? io.In.ReadAll() : Read(sh, o.Args[0]); } catch (VfsEx e) { io.E("diff: " + o.Args[0] + ": " + e.Message); return 2; }
            try { tb = o.Args[1] == "-" ? io.In.ReadAll() : Read(sh, o.Args[1]); } catch (VfsEx e) { io.E("diff: " + o.Args[1] + ": " + e.Message); return 2; }
            if (ta == tb) return 0;
            if (o.Has("q")) { io.L("Files " + o.Args[0] + " and " + o.Args[1] + " differ"); return 1; }
            var A = Lines(ta); var B = Lines(tb);
            Func<string, string> norm = s => { if (o.Has("i")) s = s.ToLowerInvariant(); if (o.Has("w")) s = Regex.Replace(s, "\\s+", ""); return s; };
            // longest common subsequence
            int n = A.Count, m = B.Count;
            var L = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--) for (int j = m - 1; j >= 0; j--) L[i, j] = norm(A[i]) == norm(B[j]) ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
            var ops = new List<char[]>(); // 'e','d','a'
            var lineA = new List<int>(); var lineB = new List<int>();
            int x = 0, y = 0;
            var hunks = new List<int[]>();   // a1,a2,b1,b2 (0-based, exclusive)
            while (x < n || y < m)
            {
                if (x < n && y < m && norm(A[x]) == norm(B[y])) { x++; y++; continue; }
                int sx = x, sy = y;
                while ((x < n || y < m) && !(x < n && y < m && norm(A[x]) == norm(B[y])))
                {
                    if (y >= m || (x < n && L[x + 1, y] >= L[x, y + 1])) x++; else y++;
                }
                hunks.Add(new[] { sx, x, sy, y });
            }
            if (o.Has("u"))
            {
                io.L("--- " + o.Args[0] + "\t" + FullDate(sh.M, sh.M.Now));
                io.L("+++ " + o.Args[1] + "\t" + FullDate(sh.M, sh.M.Now));
                foreach (var h in hunks)
                {
                    int s1 = Math.Max(0, h[0] - 3), e1 = Math.Min(n, h[1] + 3), s2 = Math.Max(0, h[2] - 3), e2 = Math.Min(m, h[3] + 3);
                    io.L("@@ -" + (s1 + 1) + "," + (e1 - s1) + " +" + (s2 + 1) + "," + (e2 - s2) + " @@");
                    for (int i = s1; i < h[0]; i++) io.L(" " + A[i]);
                    for (int i = h[0]; i < h[1]; i++) io.L("-" + A[i]);
                    for (int i = h[2]; i < h[3]; i++) io.L("+" + B[i]);
                    for (int i = h[1]; i < e1; i++) io.L(" " + A[i]);
                }
                return 1;
            }
            foreach (var h in hunks)
            {
                Func<int, int, string> rng = (s, e) => e - s <= 1 ? (s + 1).ToString() : (s + 1) + "," + e;
                if (h[1] == h[0]) io.L(h[0] + "a" + rng(h[2], h[3]));
                else if (h[3] == h[2]) io.L(rng(h[0], h[1]) + "d" + h[2]);
                else io.L(rng(h[0], h[1]) + "c" + rng(h[2], h[3]));
                for (int i = h[0]; i < h[1]; i++) io.L("< " + A[i]);
                if (h[1] > h[0] && h[3] > h[2]) io.L("---");
                for (int i = h[2]; i < h[3]; i++) io.L("> " + B[i]);
            }
            return 1;
        }
        static int CmpCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "sl", "silent|s,quiet|s", "cmp");
            if (o.Args.Count < 2) { io.E("cmp: missing operand"); return 2; }
            string x, y;
            try { x = Read(sh, o.Args[0]); y = Read(sh, o.Args[1]); } catch (VfsEx e) { io.E("cmp: " + e.Message); return 2; }
            if (x == y) return 0;
            int i = 0; while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
            if (!o.Has("s")) io.L(o.Args[0] + " " + o.Args[1] + " differ: byte " + (i + 1) + ", line " + (x.Substring(0, i).Count(c => c == '\n') + 1));
            return 1;
        }
        static int Comm(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "123", "", "comm");
            if (o.Args.Count < 2) { io.E("comm: missing operand"); return 1; }
            List<string> A, B;
            try { A = Lines(Read(sh, o.Args[0])); B = Lines(Read(sh, o.Args[1])); } catch (VfsEx e) { io.E("comm: " + e.Message); return 1; }
            int i = 0, j = 0;
            while (i < A.Count || j < B.Count)
            {
                int c = i >= A.Count ? 1 : j >= B.Count ? -1 : string.CompareOrdinal(A[i], B[j]);
                if (c < 0) { if (!o.Has("1")) io.L(A[i]); i++; }
                else if (c > 0) { if (!o.Has("2")) io.L((o.Has("1") ? "" : "\t") + B[j]); j++; }
                else { if (!o.Has("3")) io.L((o.Has("1") ? "" : "\t") + (o.Has("2") ? "" : "\t") + A[i]); i++; j++; }
            }
            return 0;
        }
        static int Xargs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "n:I:0rtd:pL:", "max-args=|n,replace=?|I,null|0,no-run-if-empty|r,verbose|t,delimiter=|d,max-lines=|L", "xargs", true);
            if (o.Error != null) return Fail(io, o);
            var cmd = o.Args.Count > 0 ? o.Args : new List<string> { "echo" };
            string input = io.In.ReadAll();
            List<string> items;
            if (o.Has("0")) items = input.Split('\0').Where(x => x.Length > 0).ToList();
            else if (o.Has("d")) items = input.Split(Printf.Escapes(o.Get("d"), false)[0]).Select(x => x.TrimEnd('\n')).Where(x => x.Length > 0).ToList();
            else if (o.Has("I")) items = Lines(input).Where(x => x.Length > 0).ToList();
            else items = Regex.Matches(input, "\"[^\"]*\"|'[^']*'|\\S+").Cast<Match>().Select(m => m.Value.Trim('"', '\'')).ToList();
            if (items.Count == 0 && (o.Has("r") || o.Has("I"))) return 0;
            int st = 0;
            if (o.Has("I"))
            {
                string rep = o.Get("I"); if (rep.Length == 0) rep = "{}";
                foreach (var it in items) { var c = cmd.Select(w => w.Replace(rep, it)).ToList(); if (o.Has("t")) io.E(string.Join(" ", c.ToArray())); if (sh.Run(c, io) != 0) st = 123; }
                return st;
            }
            int n = o.Has("n") ? int.Parse(o.Get("n")) : (o.Has("L") ? int.Parse(o.Get("L")) : int.MaxValue);
            for (int i = 0; i < Math.Max(1, items.Count); i += n)
            {
                var c = new List<string>(cmd); c.AddRange(items.Skip(i).Take(n));
                if (o.Has("t")) io.E(string.Join(" ", c.ToArray()));
                if (sh.Run(c, io) != 0) st = 123;
                if (items.Count == 0) break;
            }
            return st;
        }
        static int Less(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "NSRFXEeisMmn", "", a[0]);
            int st = 0;
            foreach (var kv in Inputs(sh, o.Args, io, a[0], ref st))
            {
                if (o.Has("N")) { int n = 1; foreach (var l in Lines(kv.Value)) io.L((n++).ToString().PadLeft(7) + " " + l); }
                else io.P(kv.Value.EndsWith("\n", StringComparison.Ordinal) || kv.Value.Length == 0 ? kv.Value : kv.Value + "\n");
            }
            if (io.Out.IsTty && o.Args.Count > 0) io.L("(END)  (Cert Lab shows the whole file in the terminal; scroll back with the mouse wheel or Page Up)");
            return st;
        }
        static int Expr(Sh sh, List<string> a, Io io)
        {
            var t = a.Skip(1).ToList();
            if (t.Count == 0) { io.E("expr: missing operand\nTry 'expr --help' for more information."); return 2; }
            try
            {
                string v;
                if (t.Count == 3 && t[1] == ":") { var m = Regex.Match(t[0], "^" + PosixToNet(t[2], false)); v = m.Success ? (m.Groups.Count > 1 ? m.Groups[1].Value : m.Length.ToString()) : (t[2].Contains("\\(") ? "" : "0"); }
                else if (t[0] == "length" && t.Count == 2) v = t[1].Length.ToString();
                else if (t[0] == "substr" && t.Count == 4) { int p = int.Parse(t[2]), l = int.Parse(t[3]); v = p - 1 < t[1].Length ? t[1].Substring(p - 1, Math.Min(l, t[1].Length - p + 1)) : ""; }
                else if (t[0] == "index" && t.Count == 3) { int k = t[1].IndexOfAny(t[2].ToCharArray()); v = (k + 1).ToString(); }
                else if (t.Count == 3)
                {
                    long x, y; bool nx = long.TryParse(t[0], out x), ny = long.TryParse(t[2], out y);
                    switch (t[1])
                    {
                        case "+": v = (x + y).ToString(); break;
                        case "-": v = (x - y).ToString(); break;
                        case "*": v = (x * y).ToString(); break;
                        case "/": if (y == 0) { io.E("expr: division by zero"); return 2; } v = (x / y).ToString(); break;
                        case "%": if (y == 0) { io.E("expr: division by zero"); return 2; } v = (x % y).ToString(); break;
                        case "<": v = (nx && ny ? x < y : string.CompareOrdinal(t[0], t[2]) < 0) ? "1" : "0"; break;
                        case ">": v = (nx && ny ? x > y : string.CompareOrdinal(t[0], t[2]) > 0) ? "1" : "0"; break;
                        case "=": case "==": v = t[0] == t[2] ? "1" : "0"; break;
                        case "!=": v = t[0] != t[2] ? "1" : "0"; break;
                        case "<=": v = (nx && ny ? x <= y : string.CompareOrdinal(t[0], t[2]) <= 0) ? "1" : "0"; break;
                        case ">=": v = (nx && ny ? x >= y : string.CompareOrdinal(t[0], t[2]) >= 0) ? "1" : "0"; break;
                        default: io.E("expr: syntax error: unexpected argument '" + t[1] + "'"); return 2;
                    }
                    if ((t[1] == "+" || t[1] == "-" || t[1] == "*" || t[1] == "/" || t[1] == "%") && (!nx || !ny)) { io.E("expr: non-integer argument"); return 2; }
                }
                else if (t.Count == 1) v = t[0];
                else { io.E("expr: syntax error"); return 2; }
                io.L(v);
                return v == "0" || v == "" ? 1 : 0;
            }
            catch (Exception) { io.E("expr: syntax error"); return 2; }
        }
        static int Bc(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lq", "mathlib|l,quiet|q", "bc");
            int st = 0;
            int scale = o.Has("l") ? 20 : 0;
            foreach (var kv in Inputs(sh, o.Args, io, "bc", ref st))
                foreach (var line0 in Lines(kv.Value))
                    foreach (var line in line0.Split(';'))
                    {
                        var l = line.Trim();
                        if (l.Length == 0 || l == "quit") continue;
                        var sm = Regex.Match(l, "^scale\\s*=\\s*(\\d+)$");
                        if (sm.Success) { scale = int.Parse(sm.Groups[1].Value); continue; }
                        try { double v = new BcEval(l).Run(scale); io.L(scale == 0 ? Math.Truncate(v).ToString(CultureInfo.InvariantCulture) : v.ToString("F" + scale, CultureInfo.InvariantCulture)); }
                        catch (Exception) { io.E("(standard_in) 1: syntax error"); st = 1; }
                    }
            return st;
        }
        class BcEval
        {
            readonly string s; int i; int scale;
            public BcEval(string t) { s = t; }
            public double Run(int sc) { scale = sc; double v = Add(); if (i < s.Length) throw new FormatException(); return v; }
            void Ws() { while (i < s.Length && s[i] == ' ') i++; }
            double Add() { double v = Mul(); for (;;) { Ws(); if (i < s.Length && s[i] == '+') { i++; v += Mul(); } else if (i < s.Length && s[i] == '-') { i++; v -= Mul(); } else return v; } }
            double Mul() { double v = Pow(); for (;;) { Ws(); if (i < s.Length && s[i] == '*') { i++; v *= Pow(); } else if (i < s.Length && s[i] == '/') { i++; double d = Pow(); v = scale == 0 ? Math.Truncate(v / d) : v / d; } else if (i < s.Length && s[i] == '%') { i++; v %= Pow(); } else return v; } }
            double Pow() { double b = Un(); Ws(); if (i < s.Length && s[i] == '^') { i++; return Math.Pow(b, Pow()); } return b; }
            double Un() { Ws(); if (i < s.Length && s[i] == '-') { i++; return -Un(); } if (i < s.Length && s[i] == '(') { i++; double v = Add(); Ws(); if (i >= s.Length || s[i] != ')') throw new FormatException(); i++; return v; } int st = i; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++; if (st == i) { if (s.Substring(i).StartsWith("sqrt(", StringComparison.Ordinal)) { i += 5; double v = Add(); i++; return Math.Sqrt(v); } throw new FormatException(); } return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture); }
        }
        static int Fold(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "w:s", "width=|w,spaces|s", "fold");
            int st = 0; int w = o.Has("w") ? int.Parse(o.Get("w")) : 80;
            foreach (var kv in Inputs(sh, o.Args, io, "fold", ref st)) foreach (var l in Lines(kv.Value)) { for (int i = 0; i < Math.Max(1, l.Length); i += w) io.L(l.Substring(i, Math.Min(w, l.Length - i))); }
            return st;
        }
        static int Shuf(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "n:i:e", "head-count=|n,input-range=|i,echo|e", "shuf");
            int st = 0;
            List<string> items;
            if (o.Has("i")) { var r = o.Get("i").Split('-'); int lo = int.Parse(r[0]), hi = int.Parse(r[1]); items = Enumerable.Range(lo, hi - lo + 1).Select(x => x.ToString()).ToList(); }
            else if (o.Has("e")) items = new List<string>(o.Args);
            else { items = new List<string>(); foreach (var kv in Inputs(sh, o.Args, io, "shuf", ref st)) items.AddRange(Lines(kv.Value)); }
            var rnd = new Random(sh.M.Seed * 7 + (sh.M.Counter++));
            items = items.OrderBy(x => rnd.Next()).ToList();
            if (o.Has("n")) items = items.Take(int.Parse(o.Get("n"))).ToList();
            io.P(JoinLines(items));
            return st;
        }
        static int Yes(Sh sh, List<string> a, Io io)
        {
            string w = a.Count > 1 ? string.Join(" ", a.Skip(1).ToArray()) : "y";
            if (sh.Background) { sh.LongRunning = true; sh.BgCpu = 99.5; sh.BgComm = "yes"; return 0; }
            if (io.Out.IsTty && sh.S != null)
            {
                for (int i = 0; i < 40; i++) io.L(w);
                try { sh.S.WaitForBreak(); } catch (BreakSignal) { io.P("^C\n"); return 130; }
                return 0;
            }
            // into a pipe or file: enough lines for what follows (a real yes runs until the reader stops)
            for (int i = 0; i < 2000; i++) io.L(w);
            return 0;
        }
    }
}
