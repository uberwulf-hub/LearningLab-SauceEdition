// Cert Lab - the programs of the simulated machines: registration, option parsing in the GNU style
// (-abc, -n 5, -n5, --long, --long=value, --), and helpers the programs share.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class Opts
    {
        public readonly Dictionary<string, List<string>> V = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public readonly List<string> Args = new List<string>();
        public string Error;
        public bool Has(string k) { return V.ContainsKey(k); }
        public string Get(string k) { List<string> l; return V.TryGetValue(k, out l) && l.Count > 0 ? l[l.Count - 1] : null; }
        public string Get(string k, string def) { return Get(k) ?? def; }
        public List<string> All(string k) { List<string> l; return V.TryGetValue(k, out l) ? l : new List<string>(); }
        public int Count(string k) { List<string> l; return V.TryGetValue(k, out l) ? l.Count : 0; }
        void Add(string k, string v) { List<string> l; if (!V.TryGetValue(k, out l)) { l = new List<string>(); V[k] = l; } l.Add(v); }

        /// <summary>shorts: letters, a ':' after one that takes a value ("n:fv"); longs: "name" or "name=" (takes a
        /// value) with an optional "|x" alias to a short letter ("lines=|n").</summary>
        public static Opts Parse(List<string> a, string shorts, string longs, string prog)
        {
            return Parse(a, shorts, longs, prog, false);
        }
        public static Opts Parse(List<string> a, string shorts, string longs, string prog, bool stopAtFirstArg)
        {
            var o = new Opts();
            var longMap = new Dictionary<string, string[]>(StringComparer.Ordinal);   // name -> [takesValue, alias]
            foreach (var l in (longs ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = l.Split('|');
                string nm = p[0]; bool val = nm.EndsWith("=", StringComparison.Ordinal); bool opt = nm.EndsWith("=?", StringComparison.Ordinal);
                nm = nm.TrimEnd('=', '?');
                longMap[nm] = new[] { val ? "1" : (opt ? "2" : "0"), p.Length > 1 ? p[1] : nm };
            }
            bool done = false;
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (done || s == "-" || !s.StartsWith("-", StringComparison.Ordinal)) { o.Args.Add(s); if (stopAtFirstArg) done = true; continue; }
                if (s == "--") { done = true; continue; }
                if (s.StartsWith("--", StringComparison.Ordinal))
                {
                    string nm = s.Substring(2), val = null;
                    int eq = nm.IndexOf('=');
                    if (eq >= 0) { val = nm.Substring(eq + 1); nm = nm.Substring(0, eq); }
                    string[] spec;
                    if (!longMap.TryGetValue(nm, out spec))
                    {
                        var cands = longMap.Keys.Where(k => k.StartsWith(nm, StringComparison.Ordinal)).ToList();
                        if (cands.Count == 1) { nm = cands[0]; spec = longMap[nm]; }
                        else { o.Error = prog + ": " + (cands.Count > 1 ? "option '--" + nm + "' is ambiguous" : "unrecognized option '--" + nm + "'") + "\nTry '" + prog + " --help' for more information."; return o; }
                    }
                    if (spec[0] == "1" && val == null) { if (i + 1 >= a.Count) { o.Error = prog + ": option '--" + nm + "' requires an argument\nTry '" + prog + " --help' for more information."; return o; } val = a[++i]; }
                    if (spec[0] == "0" && val != null) { o.Error = prog + ": option '--" + nm + "' doesn't allow an argument\nTry '" + prog + " --help' for more information."; return o; }
                    o.Add(spec[1], val ?? "");
                    continue;
                }
                for (int k = 1; k < s.Length; k++)
                {
                    char c = s[k];
                    int pos = shorts.IndexOf(c);
                    if (pos < 0 || c == ':')
                    {
                        // a negative number for programs that take them (e.g. head -5) is handled by callers
                        o.Error = prog + ": invalid option -- '" + c + "'\nTry '" + prog + " --help' for more information.";
                        return o;
                    }
                    bool takes = pos + 1 < shorts.Length && shorts[pos + 1] == ':';
                    if (takes)
                    {
                        string val = s.Substring(k + 1);
                        if (val.Length == 0) { if (i + 1 >= a.Count) { o.Error = prog + ": option requires an argument -- '" + c + "'\nTry '" + prog + " --help' for more information."; return o; } val = a[++i]; }
                        o.Add(c.ToString(), val);
                        break;
                    }
                    o.Add(c.ToString(), "");
                }
            }
            return o;
        }
    }

    public static partial class Cmd
    {
        static bool inited;
        public static void Init()
        {
            if (inited) return;
            inited = true;
            RegFiles(); RegText(); RegArchive(); RegUsers(); RegSystem(); RegStorage(); RegNet(); RegSelinux(); RegPkg(); RegPodman(); RegEdit(); RegMisc();
        }
        static void P(string name, CmdFn f) { Sh.Programs[name] = f; }

        public static int Fail(Io io, Opts o) { io.E(o.Error); return o.Error.Contains("invalid option") || o.Error.Contains("unrecognized") ? 2 : 1; }
        public static int Fail(Io io, Opts o, int code) { io.E(o.Error); return code; }

        // ------------------------------------------------------------ helpers
        public static Loc Find(Sh sh, string path, bool follow) { return sh.M.V.Resolve(sh.P, path, follow); }
        public static bool TryFind(Sh sh, string path, bool follow, out Loc l, out string err)
        {
            err = null; l = null;
            try { l = sh.M.V.Resolve(sh.P, path, follow); if (!l.Exists) { err = "No such file or directory"; return false; } return true; }
            catch (VfsEx e) { err = e.Message; return false; }
        }
        public static string Read(Sh sh, string path) { return sh.M.V.ReadText(sh.P, path); }
        public static string Abs(Sh sh, string p) { return p.StartsWith("/", StringComparison.Ordinal) ? Vfs.Norm(p) : Vfs.Norm(Vfs.Join(sh.P.Cwd, p)); }

        /// <summary>The text of the files named (or stdin for none / "-"), with errors in GNU style.</summary>
        public static List<KeyValuePair<string, string>> Inputs(Sh sh, List<string> files, Io io, string prog, ref int status)
        {
            var l = new List<KeyValuePair<string, string>>();
            if (files.Count == 0) { l.Add(new KeyValuePair<string, string>("-", io.In.ReadAll())); return l; }
            foreach (var f in files)
            {
                if (f == "-") { l.Add(new KeyValuePair<string, string>("-", io.In.ReadAll())); continue; }
                try
                {
                    var loc = sh.M.V.Resolve(sh.P, f, true);
                    if (!loc.Exists) { io.E(prog + ": " + f + ": No such file or directory"); status = prog == "grep" ? 2 : 1; continue; }
                    if (loc.Node.T == 'd') { io.E(prog + ": " + f + ": Is a directory"); status = prog == "grep" ? 2 : 1; continue; }
                    l.Add(new KeyValuePair<string, string>(f, sh.M.V.ReadNode(sh.P, loc)));
                }
                catch (VfsEx e) { io.E(prog + ": " + f + ": " + e.Message); status = prog == "grep" ? 2 : 1; }
            }
            return l;
        }

        public static List<string> Lines(string text)
        {
            var l = text.Split('\n').ToList();
            if (l.Count > 0 && l[l.Count - 1].Length == 0) l.RemoveAt(l.Count - 1);
            return l;
        }
        public static string JoinLines(IEnumerable<string> l) { var sb = new StringBuilder(); foreach (var x in l) sb.Append(x).Append('\n'); return sb.ToString(); }

        /// <summary>Asks a yes/no question on the terminal (cp -i, rm -i...). Non-interactive: no.</summary>
        public static bool Confirm(Sh sh, Io io, string question)
        {
            if (sh.S == null) return false;
            string ans;
            if (io.In.IsTty) ans = sh.S.ReadLine(question, false);
            else { io.Err.Write(question); ans = io.In.ReadLine(); if (ans != null) io.Err.Write("\n"); }
            return ans != null && ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }

        public static string ReadSecret(Sh sh, Io io, string prompt)
        {
            if (io.In.IsTty && sh.S != null) return sh.S.ReadLine(prompt, true);
            return io.In.ReadLine();
        }

        public static long ParseSize(string s, long unitDefault)
        {
            // 100, 100K, 1.5G, 500MiB, 1GB, +10M
            var m = Regex.Match(s.Trim(), "^([+-]?)(\\d+(?:\\.\\d+)?)\\s*([KkMmGgTtPpBbSs]?)(i?[Bb]?)$");
            if (!m.Success) return -1;
            double n = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            string u = m.Groups[3].Value.ToUpperInvariant();
            bool si = m.Groups[4].Value.ToUpperInvariant() == "B" && m.Groups[3].Value.Length > 0;
            double mul = unitDefault;
            switch (u)
            {
                case "K": mul = si ? 1000 : 1024; break;
                case "M": mul = si ? 1e6 : 1024.0 * 1024; break;
                case "G": mul = si ? 1e9 : 1024.0 * 1024 * 1024; break;
                case "T": mul = si ? 1e12 : 1024.0 * 1024 * 1024 * 1024; break;
                case "P": mul = 1024.0 * 1024 * 1024 * 1024 * 1024; break;
                case "B": mul = 1; break;
                case "S": mul = 512; break;
            }
            return (long)Math.Round(n * mul);
        }

        public static string Human(long bytes)
        {
            // ls -h / du -h / df -h style: 1 decimal below 10, rounded up
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture);
            string[] u = { "K", "M", "G", "T", "P" };
            double v = bytes; int i = -1;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            if (v < 10) { double r = Math.Ceiling(v * 10) / 10; if (r >= 10) return "10" + u[i]; return r.ToString("0.0", CultureInfo.InvariantCulture) + u[i]; }
            double c = Math.Ceiling(v);
            if (c >= 1024 && i < u.Length - 1) return "1.0" + u[i + 1];
            return c.ToString("0", CultureInfo.InvariantCulture) + u[i];
        }

        public static string LsDate(Machine m, long t)
        {
            var d = m.LocalTime(t);
            var now = m.LocalTime(m.Now);
            bool recent = Math.Abs((now - d).TotalDays) < 182;
            string mon = d.ToString("MMM", CultureInfo.InvariantCulture);
            return mon + " " + d.Day.ToString().PadLeft(2) + " " + (recent ? d.ToString("HH:mm", CultureInfo.InvariantCulture) : " " + d.Year);
        }
        public static string FullDate(Machine m, long t)
        {
            var d = m.LocalTime(t);
            return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "." + ((t * 7919) % 1000000000).ToString("000000000") + " " + Tz.OffsetText(m.TimeZoneName, t);
        }
        public static string DateText(Machine m, long t)
        {
            var d = m.LocalTime(t);
            return d.ToString("ddd MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString().PadLeft(2) + " " + d.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, t) + " " + d.Year;
        }

        /// <summary>chmod-style symbolic modes: u+x, go-w, a=r, u=rwx,g=rx,o=, +t, g+s, a+X.</summary>
        public static bool ApplySymbolic(string spec, int mode, bool forUmask, int fileType, out int result)
        {
            result = mode;
            bool isDir = fileType == 'd';
            foreach (var clause in spec.Split(','))
            {
                var m = Regex.Match(clause, "^([ugoa]*)([-+=][rwxXst]*)+$");
                if (!m.Success) return false;
                string who = m.Groups[1].Value;
                if (who.Length == 0) who = "a";
                int whoMask = 0;
                foreach (char w in who) { if (w == 'u' || w == 'a') whoMask |= 0x9C0; if (w == 'g' || w == 'a') whoMask |= 0x438; if (w == 'o' || w == 'a') whoMask |= 0x207; }
                foreach (Match op in Regex.Matches(clause.Substring(m.Groups[1].Value.Length), "([-+=])([rwxXst]*)"))
                {
                    char o = op.Groups[1].Value[0];
                    int bits = 0;
                    foreach (char p in op.Groups[2].Value)
                    {
                        switch (p)
                        {
                            case 'r': bits |= 0x124; break;
                            case 'w': bits |= 0x92; break;
                            case 'x': bits |= 0x49; break;
                            case 'X': if (isDir || (result & 0x49) != 0) bits |= 0x49; break;
                            case 's': bits |= 0xC00; break;
                            case 't': bits |= 0x200; break;
                        }
                    }
                    bits &= whoMask;
                    if (o == '+') result |= bits;
                    else if (o == '-') result &= ~bits;
                    else { result &= ~(whoMask & 0xFFF & ~(isDir ? 0 : 0)); result |= bits; }
                }
            }
            return true;
        }

        public static int ParseMode(string spec, int cur, char type, out bool ok)
        {
            ok = true;
            if (Regex.IsMatch(spec, "^[0-7]{1,4}$"))
            {
                int v = Convert.ToInt32(spec, 8);
                // directories keep setuid / setgid bits unless the octal says them (GNU chmod rule for 3 digits)
                if (type == 'd' && spec.Length <= 3) v |= cur & 0xC00;
                return v;
            }
            int r;
            if (!ApplySymbolic(spec, cur, false, type, out r)) { ok = false; return cur; }
            return r;
        }

        public static string Owner(Machine m, int uid) { return m.UName(uid); }
        public static string Grp(Machine m, int gid) { return m.GName(gid); }

        /// <summary>uid from a name or number.</summary>
        public static int Uid(Machine m, string s) { var p = m.User(s); if (p != null) return p.Uid; int n; return int.TryParse(s, out n) ? n : -1; }
        public static int Gid(Machine m, string s) { var g = m.Group(s); if (g != null) return g.Gid; int n; return int.TryParse(s, out n) ? n : -1; }

        public static string Q(string s) { return "'" + s + "'"; }
    }
}
