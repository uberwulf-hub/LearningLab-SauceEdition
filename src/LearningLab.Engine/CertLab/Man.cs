// Cert Lab - man, whatis, apropos (man -k) and info, reading the manual pages Cert Lab ships in
// Courses\rhcsa\man (one text file per page, named NAME.SECTION; written for Cert Lab, not copied).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class Man
    {
        public static string Dir;
        static Dictionary<string, string> pages;     // "ls.1" -> text
        static readonly object gate = new object();

        static Dictionary<string, string> Pages
        {
            get
            {
                lock (gate)
                {
                    if (pages != null) return pages;
                    pages = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (Dir != null && Directory.Exists(Dir))
                        foreach (var f in Directory.GetFiles(Dir))
                        {
                            string n = Path.GetFileName(f);
                            if (!Regex.IsMatch(n, "^.+\\.[1-8][a-z]*$")) continue;
                            pages[n] = File.ReadAllText(f).Replace("\r\n", "\n");
                        }
                    return pages;
                }
            }
        }
        public static void Reload() { lock (gate) { pages = null; } }
        public static int Count { get { return Pages.Count; } }
        public static IEnumerable<string> Names { get { return Pages.Keys; } }

        public static string Find(string name, string section)
        {
            var t = FindRaw(name, section);
            // ".so grep.1": this page is another page (egrep, vim, yum...)
            for (int i = 0; i < 3 && t != null && t.StartsWith(".so ", StringComparison.Ordinal); i++)
            {
                var target = t.Substring(4).Trim().Split('\n')[0].Trim();
                target = target.Contains("/") ? target.Substring(target.LastIndexOf('/') + 1) : target;
                int dot = target.LastIndexOf('.');
                t = dot > 0 ? FindRaw(target.Substring(0, dot), target.Substring(dot + 1)) : FindRaw(target, null);
            }
            return t;
        }

        static string FindRaw(string name, string section)
        {
            var p = Pages;
            if (section != null) { string t; return p.TryGetValue(name + "." + section, out t) ? t : null; }
            foreach (var s in new[] { "1", "8", "5", "7", "3" }) { string t; if (p.TryGetValue(name + "." + s, out t)) return t; }
            var any = p.Keys.FirstOrDefault(k => k.StartsWith(name + ".", StringComparison.Ordinal));
            return any == null ? null : p[any];
        }

        /// <summary>The NAME line: "ls - list directory contents".</summary>
        public static string Summary(string text)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
                if (lines[i].Trim() == "NAME") { var s = lines[i + 1].Trim(); return s; }
            return "";
        }

        public static int Cmd(Sh sh, List<string> a, Io io)
        {
            var args = a.Skip(1).ToList();
            if (args.Count > 0 && (args[0] == "-k" || args[0] == "--apropos"))
            {
                if (args.Count < 2) { io.E("apropos what?"); return 1; }
                var rx = new Regex(Regex.Escape(args[1]), RegexOptions.IgnoreCase);
                var hits = new List<string>();
                foreach (var kv in Pages.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    string sum = Summary(kv.Value);
                    int dot = kv.Key.LastIndexOf('.');
                    string nm = kv.Key.Substring(0, dot), sec = kv.Key.Substring(dot + 1);
                    if (rx.IsMatch(nm) || rx.IsMatch(sum)) hits.Add(nm + " (" + sec + ")".PadRight(Math.Max(1, 16 - nm.Length - sec.Length)) + " - " + Regex.Replace(sum, "^.*? - ", ""));
                }
                if (hits.Count == 0) { io.E(args[1] + ": nothing appropriate."); return 16; }
                foreach (var h in hits) io.L(h);
                return 0;
            }
            if (args.Count > 0 && args[0] == "-f") { var b = new List<string> { "whatis" }; b.AddRange(args.Skip(1)); return Whatis(sh, b, io); }
            string section = null;
            if (args.Count > 1 && Regex.IsMatch(args[0], "^[1-8]$")) { section = args[0]; args.RemoveAt(0); }
            if (args.Count == 0) { io.E("What manual page do you want?\nFor example, try 'man man'."); return 1; }
            int st = 0;
            foreach (var n in args)
            {
                string name = n, sec = section;
                var mm = Regex.Match(n, "^(.+)\\(([1-8])\\)$"); if (mm.Success) { name = mm.Groups[1].Value; sec = mm.Groups[2].Value; }
                string t = Find(name, sec);
                if (t == null) { io.E(sec != null ? "No manual entry for " + name + " in section " + sec : "No manual entry for " + name); st = 16; continue; }
                io.P(t.EndsWith("\n", StringComparison.Ordinal) ? t : t + "\n");
                if (io.Out.IsTty) io.L(" Manual page " + name + "(" + (sec ?? SectionOf(name)) + ") line 1 (END) - Cert Lab shows the whole page; scroll back to read it.");
            }
            return st;
        }
        static string SectionOf(string name) { var k = Pages.Keys.FirstOrDefault(x => x.StartsWith(name + ".", StringComparison.Ordinal)); return k == null ? "1" : k.Substring(name.Length + 1); }

        public static int Whatis(Sh sh, List<string> a, Io io)
        {
            int st = 0;
            foreach (var n in a.Skip(1))
            {
                var keys = Pages.Keys.Where(k => k.StartsWith(n + ".", StringComparison.Ordinal) && k.LastIndexOf('.') == n.Length).OrderBy(k => k).ToList();
                if (keys.Count == 0) { io.E(n + ": nothing appropriate."); st = 16; continue; }
                foreach (var k in keys) { string sec = k.Substring(n.Length + 1); io.L((n + " (" + sec + ")").PadRight(17) + " - " + Regex.Replace(Summary(Pages[k]), "^.*? - ", "")); }
            }
            return st;
        }

        public static int Info(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.L("File: dir,\tNode: Top,\tThis is the top of the INFO tree.\n\n* Menu:\n* Coreutils: (coreutils).       Core GNU (file, text, shell) utilities.\n* Bash: (bash).                 The GNU Bourne-Again SHell.\n\n(Cert Lab: info shows the same text as the manual pages; try  info ls)"); return 0; }
            string t = Find(a[1], null);
            if (t == null) { io.E("info: No menu item '" + a[1] + "' in node '(dir)Top'"); return 1; }
            io.L("File: " + a[1] + ".info,  Node: Top  (Cert Lab shows the manual page here)\n");
            io.P(t);
            return 0;
        }

        /// <summary>prog --help: the synopsis and options from the manual page.</summary>
        public static string Help(string name)
        {
            string t = Find(name, null);
            if (t == null) return null;
            var lines = t.Split('\n');
            var sb = new StringBuilder();
            bool take = false;
            foreach (var l in lines)
            {
                string tr = l.Trim();
                if (tr == "SYNOPSIS") { take = true; sb.Append("Usage:\n"); continue; }
                if (tr == "DESCRIPTION") { take = false; continue; }
                if (tr == "OPTIONS") { take = true; sb.Append("\nOptions:\n"); continue; }
                if (Regex.IsMatch(l, "^[A-Z][A-Z ]+$") && tr != "SYNOPSIS" && tr != "OPTIONS") take = false;
                if (take) sb.Append(l).Append('\n');
            }
            sb.Append("\nFor the full page: man " + name + "\n");
            return sb.ToString();
        }
    }
}
