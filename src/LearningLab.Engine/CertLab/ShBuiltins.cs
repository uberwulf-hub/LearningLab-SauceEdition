// Cert Lab - the bash-like shell, part 4: the builtins (cd, echo, printf, read, export, declare, set, test / [ / [[,
// getopts, trap, source, alias, history, jobs...).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public partial class Sh
    {
        public static readonly Dictionary<string, CmdFn> Builtins = new Dictionary<string, CmdFn>(StringComparer.Ordinal);
        public static readonly Dictionary<string, CmdFn> Programs = new Dictionary<string, CmdFn>(StringComparer.Ordinal);
        static Sh() { RegisterBuiltins(); Cmd.Init(); }

        static void B(string name, CmdFn f) { Builtins[name] = f; }

        // Cyber Range only (the courses' machines keep real bash's help): what there is to try on this box. `help` names
        // the everyday commands and the range's own tools that are installed here; `help all` lists every program.
        static readonly string[] HelpBasics = { "ls", "cd", "cat", "less", "head", "tail", "grep", "find", "file", "strings",
            "wc", "sort", "uniq", "cut", "tr", "xxd", "base64", "base32", "echo", "id", "whoami", "sudo", "ssh", "curl", "nc",
            "ping", "ip", "ss", "openssl", "sha256sum", "md5sum", "tar", "zcat" };
        static readonly string[] HelpRangeTools = { "nmap", "smbclient", "winrm-shell", "ftp", "mysql", "hashcrack", "pwguess",
            "webscan", "vulnlookup", "ticket-request", "xorcat", "vigenere", "morse", "catcher", "browse", "siem", "cloudctl",
            "toyvm" };

        static void HelpPrograms(Sh sh, List<string> a, Io io)
        {
            var names = sh.ProgramsOnPath();
            if (names.Count == 0) return;
            var sb = new StringBuilder();
            if (a.Count > 1 && a[1] == "all")
            {
                sb.Append("\nEvery program on this box (" + names.Count + "):\n\n");
                HelpList(sb, names);
            }
            else
            {
                var have = new HashSet<string>(names, StringComparer.Ordinal);
                var basics = HelpBasics.Where(have.Contains).ToList();
                var tools = HelpRangeTools.Where(have.Contains).ToList();
                if (basics.Count > 0) { sb.Append("\nEveryday commands on this box:\n"); HelpList(sb, basics); sb.Append("\n"); }
                if (tools.Count > 0) { sb.Append("\nThe range's tools on this box:\n"); HelpList(sb, tools); sb.Append("\n"); }
                sb.Append("\nType `man <command>' to learn one (e.g. `man " + (have.Contains("base64") ? "base64" : names[0]) + "'), and `help all' for all " + names.Count + " programs.");
            }
            io.L(sb.ToString());
        }

        static void HelpList(StringBuilder sb, List<string> names)
        {
            int col = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string w = names[i] + (i < names.Count - 1 ? ", " : "");
                if (col > 0 && col + w.Length > 78) { sb.Append("\n"); col = 0; }
                if (col == 0) { sb.Append(" "); col = 1; }
                sb.Append(w); col += w.Length;
            }
        }

        static void RegisterBuiltins()
        {
            B("cd", Cd); B("pwd", PwdCmd); B("echo", Echo); B("printf", PrintfCmd);
            B("export", Export); B("unset", UnsetCmd); B("set", SetCmd); B("shift", Shift);
            B("read", Read); B("local", LocalCmd); B("declare", Declare); B("typeset", Declare); B("readonly", Readonly);
            B("return", (sh, a, io) => { int n = sh.Status; if (a.Count > 1 && !int.TryParse(a[1], out n)) { io.E("bash: return: " + a[1] + ": numeric argument required"); n = 2; } if (sh.FuncDepth == 0 && !sh.Sourcing) throw new ShError("return: can only `return' from a function or sourced script"); throw new ReturnEx(n & 255); });
            B("exit", (sh, a, io) => { int n = sh.Status; if (a.Count > 1 && !int.TryParse(a[1], out n)) { io.E("bash: exit: " + a[1] + ": numeric argument required"); n = 2; } if (sh.Interactive && sh.S != null) io.Err.Write(sh.LoginShell ? "logout\n" : "exit\n"); throw new ExitEx(n & 255); });
            B("logout", (sh, a, io) => { if (!sh.LoginShell) { io.E("bash: logout: not login shell: use `exit'"); return 1; } io.Err.Write("logout\n"); throw new ExitEx(0); });
            B("break", (sh, a, io) => { int n = 1; if (a.Count > 1) int.TryParse(a[1], out n); throw new LoopEx(Math.Max(1, n), false); });
            B("continue", (sh, a, io) => { int n = 1; if (a.Count > 1) int.TryParse(a[1], out n); throw new LoopEx(Math.Max(1, n), true); });
            B("source", Source); B(".", Source); B("eval", (sh, a, io) => sh.RunNested(string.Join(" ", a.Skip(1).ToArray()), io));
            B("exec", Exec);
            B("true", (sh, a, io) => 0); B("false", (sh, a, io) => 1); B(":", (sh, a, io) => 0);
            B("test", (sh, a, io) => sh.TestCmd(a.Skip(1).ToList(), io, "test")); B("[", (sh, a, io) => { if (a[a.Count - 1] != "]") { io.E("bash: [: missing `]'"); return 2; } return sh.TestCmd(a.Skip(1).Take(a.Count - 2).ToList(), io, "["); });
            B("type", TypeCmd); B("command", CommandCmd); B("hash", (sh, a, io) => { if (a.Count == 1) io.L("hash: hash table empty"); return 0; });
            B("alias", Alias); B("unalias", (sh, a, io) => { int st = 0; foreach (var n in a.Skip(1)) { if (n == "-a") { sh.Aliases.Clear(); continue; } if (!sh.Aliases.Remove(n)) { io.E("bash: unalias: " + n + ": not found"); st = 1; } } return st; });
            B("history", HistoryCmd); B("jobs", Jobs_); B("fg", Fg); B("bg", (sh, a, io) => { var j = sh.Jobs.LastOrDefault(x => !x.Done); if (j == null) { io.E("bash: bg: current: no such job"); return 1; } io.L("[" + j.Id + "]+ " + j.Text + " &"); return 0; });
            B("wait", WaitCmd); B("umask", Umask); B("getopts", Getopts); B("trap", Trap); B("let", (sh, a, io) => { long v = 0; foreach (var e in a.Skip(1)) v = sh.Arith(e); return v != 0 ? 0 : 1; });
            B("shopt", (sh, a, io) => 0); B("ulimit", (sh, a, io) => { if (a.Contains("-a")) io.L("core file size              (blocks, -c) 0\nfile size                   (blocks, -f) unlimited\nmax user processes                  (-u) 14812\nopen files                          (-n) 1024\nstack size                  (kbytes, -s) 8192"); else io.L("unlimited"); return 0; });
            B("mapfile", Mapfile); B("readarray", Mapfile);
            // time COMMAND: runs it, then the times (the simulator's commands take next to no time)
            B("time", (sh, a, io) => { int st = a.Count > 1 ? sh.Run(a.Skip(1).ToList(), io) : 0; io.E("\nreal\t0m0.00" + (3 + a.Count % 6) + "s\nuser\t0m0.001s\nsys\t0m0.002s"); return st; });
            B("pushd", Pushd); B("popd", Popd); B("dirs", (sh, a, io) => { io.L(string.Join(" ", new[] { sh.TildePath(sh.P.Cwd) }.Concat(sh.DirStack.Select(sh.TildePath)).ToArray())); return 0; });
            B("help", (sh, a, io) => { io.L("GNU bash, version 5.1.8(1)-release (x86_64-redhat-linux-gnu)\nThese shell commands are defined internally. Type `help' to see this list.\nType `man bash' for more about the shell in general; `man <command>' for any command.\n\n cd, echo, printf, read, export, unset, set, shift, local, declare, readonly, return, exit,\n break, continue, source (.), eval, exec, true, false, test ([), [[ ]], (( )), type, command,\n alias, unalias, history, jobs, fg, bg, wait, umask, getopts, trap, let, mapfile, pushd, popd"); if (sh.M.Net != null && sh.M.Net.Range != null) HelpPrograms(sh, a, io); return 0; });
            B("disown", (sh, a, io) => { sh.Jobs.Clear(); return 0; });
            B("times", (sh, a, io) => { io.L("0m0.012s 0m0.008s\n0m0.104s 0m0.061s"); return 0; });
            B("enable", (sh, a, io) => 0); B("compgen", (sh, a, io) => 0); B("complete", (sh, a, io) => 0);
            B("suspend", (sh, a, io) => { io.E("bash: suspend: cannot suspend a login shell"); return 1; });
        }

        public bool Sourcing;
        public List<string> DirStack = new List<string>();

        int RunNested(string text, Io io)
        {
            ShList list;
            try { list = ShParser.Parse(text); }
            catch (ShIncomplete) { io.E("bash: syntax error: unexpected end of file"); return 2; }
            catch (ShSyntax e) { io.E("bash: " + e.Message); return 2; }
            var save = Io; Io = io;
            try { return Exec(list); } finally { Io = save; }
        }

        // ------------------------------------------------------------ cd / pwd
        static int Cd(Sh sh, List<string> a, Io io)
        {
            var args = a.Skip(1).Where(x => x != "-L" && x != "-P").ToList();
            string dir = args.Count == 0 ? sh.GetVar("HOME") : args[0];
            if (dir == null) { io.E("bash: cd: HOME not set"); return 1; }
            bool printIt = false;
            if (dir == "-") { dir = sh.GetVar("OLDPWD"); if (dir == null) { io.E("bash: cd: OLDPWD not set"); return 1; } printIt = true; }
            if (args.Count > 1) { io.E("bash: cd: too many arguments"); return 1; }
            Loc l;
            try { l = sh.M.V.Resolve(sh.P, dir, true); }
            catch (VfsEx e) { io.E("bash: cd: " + dir + ": " + e.Message); return 1; }
            if (!l.Exists) { io.E("bash: cd: " + dir + ": No such file or directory"); return 1; }
            if (l.Node.T != 'd') { io.E("bash: cd: " + dir + ": Not a directory"); return 1; }
            if (!sh.M.V.Can(sh.P.Cr, l.Node, 1)) { io.E("bash: cd: " + dir + ": Permission denied"); return 1; }
            sh.SetVar("OLDPWD", sh.P.Cwd);
            // keep the logical path (through symlinks) like bash does
            string logical = dir.StartsWith("/", StringComparison.Ordinal) ? Vfs.Norm(dir) : Vfs.Norm(Vfs.Join(sh.P.Cwd, dir));
            sh.P.Cwd = logical;
            try { var chk = sh.M.V.Resolve(sh.P, logical, true); if (!chk.Exists) sh.P.Cwd = l.Path; } catch (VfsEx) { sh.P.Cwd = l.Path; }
            sh.SetVar("PWD", sh.P.Cwd);
            if (printIt) io.L(sh.P.Cwd);
            return 0;
        }
        static int PwdCmd(Sh sh, List<string> a, Io io)
        {
            if (a.Contains("-P")) { try { io.L(sh.M.V.Resolve(sh.P, sh.P.Cwd, true).Path); } catch (VfsEx) { io.L(sh.P.Cwd); } return 0; }
            io.L(sh.P.Cwd); return 0;
        }

        // ------------------------------------------------------------ echo / printf
        static int Echo(Sh sh, List<string> a, Io io)
        {
            bool nl = true, esc = false;
            int i = 1;
            for (; i < a.Count; i++)
            {
                if (!Regex.IsMatch(a[i], "^-[neE]+$")) break;
                foreach (char c in a[i].Substring(1)) { if (c == 'n') nl = false; else if (c == 'e') esc = true; else if (c == 'E') esc = false; }
            }
            string s = string.Join(" ", a.Skip(i).ToArray());
            if (esc) { bool stop; s = Printf.Escapes(s, true, out stop); if (stop) nl = false; }
            io.P(s + (nl ? "\n" : ""));
            return 0;
        }
        static int PrintfCmd(Sh sh, List<string> a, Io io)
        {
            var args = a.Skip(1).ToList();
            string var = null;
            if (args.Count >= 2 && args[0] == "-v") { var = args[1]; args = args.Skip(2).ToList(); }
            if (args.Count > 0 && args[0] == "--") args.RemoveAt(0);
            if (args.Count == 0) { io.E("printf: usage: printf [-v var] format [arguments]"); return 2; }
            string err;
            string outp = Printf.Format(args[0], args.Skip(1).ToList(), out err);
            if (var != null) sh.SetVar(var, outp); else io.P(outp);
            if (err != null) { io.E("bash: printf: " + err); return 1; }
            return 0;
        }

        // ------------------------------------------------------------ variables
        static int Export(Sh sh, List<string> a, Io io)
        {
            bool remove = false;
            var names = new List<string>();
            foreach (var x in a.Skip(1)) { if (x == "-n") remove = true; else if (x == "-p" || x == "-f") { } else names.Add(x); }
            if (names.Count == 0)
            {
                foreach (var kv in sh.AllVars().OrderBy(k => k.Key, StringComparer.Ordinal)) if (kv.Value.Export) io.L("declare -x " + kv.Key + (kv.Value.Val != null ? "=\"" + kv.Value.Val.Replace("\"", "\\\"") + "\"" : ""));
                return 0;
            }
            int st = 0;
            foreach (var n in names)
            {
                int eq = n.IndexOf('=');
                string name = eq > 0 ? n.Substring(0, eq) : n;
                if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) { io.E("bash: export: `" + n + "': not a valid identifier"); st = 1; continue; }
                if (eq > 0) sh.SetVar(name, n.Substring(eq + 1));
                var v = sh.GetVarObj(name);
                if (v == null) { v = new ShVar(); sh.Vars[name] = v; }
                v.Export = !remove;
            }
            return st;
        }
        static int UnsetCmd(Sh sh, List<string> a, Io io)
        {
            bool fn = false;
            foreach (var n in a.Skip(1))
            {
                if (n == "-f") { fn = true; continue; }
                if (n == "-v") { fn = false; continue; }
                if (fn) { sh.Funcs.Remove(n); continue; }
                var m = Regex.Match(n, "^([A-Za-z_][A-Za-z0-9_]*)\\[(.*)\\]$");
                if (m.Success)
                {
                    var v = sh.GetVarObj(m.Groups[1].Value);
                    if (v != null && v.Arr != null) { long i = sh.Arith(m.Groups[2].Value); if (i >= 0 && i < v.Arr.Count) v.Arr[(int)i] = null; }
                    else if (v != null && v.Assoc != null) { v.Assoc.Remove(m.Groups[2].Value); v.AssocOrder.Remove(m.Groups[2].Value); }
                    continue;
                }
                sh.Unset(n);
                if (!sh.Vars.ContainsKey(n)) sh.Funcs.Remove(n);
            }
            return 0;
        }
        static int SetCmd(Sh sh, List<string> a, Io io)
        {
            if (a.Count == 1)
            {
                foreach (var kv in sh.AllVars().OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (kv.Value.Arr != null) io.L(kv.Key + "=(" + string.Join(" ", kv.Value.Arr.Select((x, i) => x == null ? null : "[" + i + "]=\"" + x + "\"").Where(x => x != null).ToArray()) + ")");
                    else if (kv.Value.Val != null) io.L(kv.Key + "=" + Quote(kv.Value.Val));
                }
                return 0;
            }
            for (int i = 1; i < a.Count; i++)
            {
                string o = a[i];
                if (o == "--") { sh.Pos = a.Skip(i + 1).ToList(); return 0; }
                if (o == "-o" || o == "+o")
                {
                    bool on = o == "-o";
                    if (i + 1 >= a.Count) { io.L("errexit        \t" + (sh.ErrExit ? "on" : "off") + "\nnounset        \t" + (sh.NoUnset ? "on" : "off") + "\npipefail       \t" + (sh.PipeFail ? "on" : "off") + "\nxtrace         \t" + (sh.XTrace ? "on" : "off") + "\nnoclobber      \t" + (sh.NoClobber ? "on" : "off")); return 0; }
                    string nm = a[++i];
                    switch (nm) { case "errexit": sh.ErrExit = on; break; case "nounset": sh.NoUnset = on; break; case "pipefail": sh.PipeFail = on; break; case "xtrace": sh.XTrace = on; break; case "noclobber": sh.NoClobber = on; break; case "vi": case "emacs": case "posix": case "history": case "monitor": break; default: io.E("bash: set: " + nm + ": invalid option name"); return 1; }
                    continue;
                }
                if ((o.StartsWith("-", StringComparison.Ordinal) || o.StartsWith("+", StringComparison.Ordinal)) && o.Length > 1)
                {
                    bool on = o[0] == '-';
                    foreach (char c in o.Substring(1))
                    {
                        switch (c) { case 'e': sh.ErrExit = on; break; case 'x': sh.XTrace = on; break; case 'u': sh.NoUnset = on; break; case 'C': sh.NoClobber = on; break; case 'v': case 'h': case 'm': case 'B': case 'H': case 'f': case 'n': break; default: io.E("bash: set: -" + c + ": invalid option"); return 2; }
                    }
                    continue;
                }
                sh.Pos = a.Skip(i).ToList();
                return 0;
            }
            return 0;
        }
        static int Shift(Sh sh, List<string> a, Io io)
        {
            int n = 1;
            if (a.Count > 1 && !int.TryParse(a[1], out n)) { io.E("bash: shift: " + a[1] + ": numeric argument required"); return 1; }
            if (n > sh.Pos.Count) return 1;
            sh.Pos.RemoveRange(0, n);
            return 0;
        }
        static int Read(Sh sh, List<string> a, Io io)
        {
            bool raw = false, secret = false; string prompt = null, arr = null; int nchars = -1; string delim = null;
            var names = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string o = a[i];
                if (o == "-r") raw = true;
                else if (o == "-s") secret = true;
                else if (o == "-p" && i + 1 < a.Count) prompt = a[++i];
                else if (o == "-a" && i + 1 < a.Count) arr = a[++i];
                else if (o == "-n" && i + 1 < a.Count) int.TryParse(a[++i], out nchars);
                else if (o == "-t" && i + 1 < a.Count) i++;
                else if (o == "-d" && i + 1 < a.Count) delim = a[++i];
                else if (o.StartsWith("-", StringComparison.Ordinal) && o.Length > 1 && names.Count == 0)
                {
                    foreach (char c in o.Substring(1)) { if (c == 'r') raw = true; else if (c == 's') secret = true; }
                }
                else names.Add(o);
            }
            string line;
            if (io.In.IsTty && sh.S != null) line = sh.S.ReadLine(prompt ?? "", secret);
            else
            {
                if (prompt != null && sh.S != null && io.In.IsTty) io.Err.Write(prompt);
                line = io.In.ReadLine();
            }
            if (line == null) return 1;
            if (!raw)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < line.Length; i++) { if (line[i] == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; } else if (line[i] != '\\') sb.Append(line[i]); }
                line = sb.ToString();
            }
            if (nchars >= 0 && line.Length > nchars) line = line.Substring(0, nchars);
            string ifs = sh.GetVar("IFS") ?? " \t\n";
            if (arr != null)
            {
                var parts = ifs.Length == 0 ? new[] { line } : line.Split(ifs.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                sh.SetArray(arr, parts.ToList(), false);
                return 0;
            }
            if (names.Count == 0) names.Add("REPLY");
            if (names.Count == 1 && names[0] == "REPLY" && !a.Contains("REPLY")) { sh.SetVar("REPLY", line); return 0; }
            string rest = ifs.Trim().Length == ifs.Length && ifs.Length > 0 ? line : line.Trim(ifs.Where(char.IsWhiteSpace).ToArray());
            for (int k = 0; k < names.Count; k++)
            {
                if (k == names.Count - 1) { sh.SetVar(names[k], rest.TrimEnd(ifs.Where(char.IsWhiteSpace).ToArray())); break; }
                int cut = -1;
                for (int j = 0; j < rest.Length; j++) if (ifs.IndexOf(rest[j]) >= 0) { cut = j; break; }
                if (cut < 0) { sh.SetVar(names[k], rest); rest = ""; continue; }
                sh.SetVar(names[k], rest.Substring(0, cut));
                rest = rest.Substring(cut + 1).TrimStart(ifs.Where(char.IsWhiteSpace).ToArray());
            }
            return 0;
        }
        static int LocalCmd(Sh sh, List<string> a, Io io)
        {
            if (sh.FuncDepth == 0) { io.E("bash: local: can only be used in a function"); return 1; }
            foreach (var x in a.Skip(1))
            {
                if (x.StartsWith("-", StringComparison.Ordinal)) continue;
                if (x.IndexOf('=') > 0) sh.DoAssign(x, true);
                else if (!sh.Locals[sh.Locals.Count - 1].ContainsKey(x)) sh.Locals[sh.Locals.Count - 1][x] = new ShVar();
            }
            return 0;
        }
        static int Declare(Sh sh, List<string> a, Io io)
        {
            bool arr = false, assoc = false, integer = false, export = false, ro = false, print = false, funcs = false, fnames = false;
            var names = new List<string>();
            foreach (var x in a.Skip(1))
            {
                if (x.StartsWith("-", StringComparison.Ordinal) && x.Length > 1 && names.Count == 0)
                {
                    foreach (char c in x.Substring(1)) { if (c == 'a') arr = true; else if (c == 'A') assoc = true; else if (c == 'i') integer = true; else if (c == 'x') export = true; else if (c == 'r') ro = true; else if (c == 'p') print = true; else if (c == 'f') funcs = true; else if (c == 'F') fnames = true; }
                    continue;
                }
                names.Add(x);
            }
            if (funcs || fnames)
            {
                foreach (var kv in sh.Funcs.OrderBy(k => k.Key, StringComparer.Ordinal)) if (names.Count == 0 || names.Contains(kv.Key)) io.L(fnames ? "declare -f " + kv.Key : kv.Key + " () \n" + kv.Value.Source.Trim());
                return 0;
            }
            if (names.Count == 0 || print)
            {
                var list = names.Count == 0 ? sh.AllVars().OrderBy(k => k.Key, StringComparer.Ordinal).ToList() : names.Select(n => new KeyValuePair<string, ShVar>(n, sh.GetVarObj(n))).ToList();
                int st = 0;
                foreach (var kv in list)
                {
                    if (kv.Value == null) { io.E("bash: declare: " + kv.Key + ": not found"); st = 1; continue; }
                    string fl = (kv.Value.Arr != null ? "a" : kv.Value.Assoc != null ? "A" : "") + (kv.Value.Integer ? "i" : "") + (kv.Value.ReadOnly ? "r" : "") + (kv.Value.Export ? "x" : "");
                    if (fl.Length == 0) fl = "-";
                    string val;
                    if (kv.Value.Arr != null) val = "=(" + string.Join(" ", kv.Value.Arr.Select((v, i) => v == null ? null : "[" + i + "]=\"" + v + "\"").Where(v => v != null).ToArray()) + ")";
                    else if (kv.Value.Assoc != null) val = "=(" + string.Join(" ", kv.Value.AssocOrder.Select(k => "[" + k + "]=\"" + kv.Value.Assoc[k] + "\" ").ToArray()).TrimEnd() + " )";
                    else val = kv.Value.Val == null ? "" : "=\"" + kv.Value.Val + "\"";
                    io.L("declare -" + fl.TrimStart('-') + (fl == "-" ? "-" : "") + " " + kv.Key + val);
                }
                return st;
            }
            foreach (var n in names)
            {
                int eq = n.IndexOf('=');
                string name = eq > 0 ? n.Substring(0, eq) : n;
                ShVar v;
                bool inFunc = sh.FuncDepth > 0;
                if (inFunc) { var sc = sh.Locals[sh.Locals.Count - 1]; if (!sc.TryGetValue(name, out v)) { v = new ShVar(); sc[name] = v; } }
                else { v = sh.GetVarObj(name); if (v == null) { v = new ShVar(); sh.Vars[name] = v; } }
                if (assoc && v.Assoc == null) { v.Assoc = new Dictionary<string, string>(); v.AssocOrder = new List<string>(); v.Val = null; }
                if (arr && v.Arr == null && v.Assoc == null) { v.Arr = new List<string>(); if (v.Val != null) v.Arr.Add(v.Val); v.Val = null; }
                if (integer) v.Integer = true;
                if (export) v.Export = true;
                if (eq > 0) { v.ReadOnly = false; sh.DoAssign(n, inFunc); }
                if (ro) v.ReadOnly = true;
            }
            return 0;
        }
        static int Readonly(Sh sh, List<string> a, Io io)
        {
            if (a.Count == 1) { foreach (var kv in sh.AllVars()) if (kv.Value.ReadOnly) io.L("declare -r " + kv.Key + "=\"" + kv.Value.Val + "\""); return 0; }
            foreach (var n in a.Skip(1))
            {
                if (n.StartsWith("-", StringComparison.Ordinal)) continue;
                if (n.IndexOf('=') > 0) sh.DoAssign(n, false);
                var v = sh.GetVarObj(n.Split('=')[0]);
                if (v == null) { v = new ShVar(); sh.Vars[n.Split('=')[0]] = v; }
                v.ReadOnly = true;
            }
            return 0;
        }

        // ------------------------------------------------------------ source / exec / type
        static int Source(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.E("bash: " + a[0] + ": filename argument required"); return 2; }
            string text;
            try
            {
                var l = sh.M.V.Resolve(sh.P, a[1], true);
                if (!l.Exists && !a[1].Contains("/")) { string shown; var f = sh.FindProgram(a[1], out shown); if (f != null) l = f; }
                if (!l.Exists) { io.E("bash: " + a[1] + ": No such file or directory"); return 1; }
                text = sh.M.V.ReadNode(sh.P, l);
            }
            catch (VfsEx e) { io.E("bash: " + a[1] + ": " + e.Message); return 1; }
            var savePos = sh.Pos;
            if (a.Count > 2) sh.Pos = a.Skip(2).ToList();
            bool saveSrc = sh.Sourcing; sh.Sourcing = true;
            try { return sh.RunNested(text, io); }
            catch (ReturnEx r) { return r.Code; }
            finally { sh.Sourcing = saveSrc; if (a.Count > 2) sh.Pos = savePos; }
        }
        static int Exec(Sh sh, List<string> a, Io io)
        {
            if (a.Count == 1)
            {
                // exec with only redirections: they stay for the rest of the shell
                sh.Io = io;
                if (sh.Parent != null) { }
                sh.PersistIo = io;
                return 0;
            }
            int st = sh.Run(a.Skip(1).ToList(), io);
            throw new ExitEx(st);
        }
        public Io PersistIo;

        static int TypeCmd(Sh sh, List<string> a, Io io)
        {
            bool t = a.Contains("-t"), all = a.Contains("-a"), path = a.Contains("-p") || a.Contains("-P");
            int st = 0;
            foreach (var n in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
            {
                bool found = false;
                string al;
                if (!path && sh.Aliases.TryGetValue(n, out al)) { io.L(t ? "alias" : n + " is aliased to `" + al + "'"); found = true; if (!all) continue; }
                if (!path && (n == "if" || n == "then" || n == "fi" || n == "for" || n == "while" || n == "do" || n == "done" || n == "case" || n == "esac" || n == "[[" || n == "function")) { io.L(t ? "keyword" : n + " is a shell keyword"); found = true; if (!all) continue; }
                ShFunc fn;
                if (!path && sh.Funcs.TryGetValue(n, out fn)) { io.L(t ? "function" : n + " is a function\n" + n + " () \n" + fn.Source.Trim()); found = true; if (!all) continue; }
                if (!path && Builtins.ContainsKey(n)) { io.L(t ? "builtin" : n + " is a shell builtin"); found = true; if (!all) continue; }
                string shown;
                var l = sh.FindProgram(n, out shown);
                if (l != null) { io.L(t ? "file" : path ? shown : n + " is " + shown); found = true; }
                if (!found) { if (!t) io.E("bash: type: " + n + ": not found"); st = 1; }
            }
            return st;
        }
        static int CommandCmd(Sh sh, List<string> a, Io io)
        {
            if (a.Count > 1 && (a[1] == "-v" || a[1] == "-V"))
            {
                int st = 0;
                foreach (var n in a.Skip(2))
                {
                    if (sh.Funcs.ContainsKey(n) || Builtins.ContainsKey(n)) { io.L(a[1] == "-v" ? n : n + " is a shell builtin"); continue; }
                    string al; if (sh.Aliases.TryGetValue(n, out al)) { io.L("alias " + n + "='" + al + "'"); continue; }
                    string shown; var l = sh.FindProgram(n, out shown);
                    if (l != null) io.L(a[1] == "-v" ? shown : n + " is " + shown); else st = 1;
                }
                return st;
            }
            var rest = a.Skip(1).Where(x => x != "-p").ToList();
            if (rest.Count == 0) return 0;
            CmdFn b;
            if (Builtins.TryGetValue(rest[0], out b)) return b(sh, rest, io);
            return sh.RunExternal(rest, io);
        }
        static int Alias(Sh sh, List<string> a, Io io)
        {
            if (a.Count == 1 || (a.Count == 2 && a[1] == "-p")) { foreach (var kv in sh.Aliases.OrderBy(k => k.Key, StringComparer.Ordinal)) io.L("alias " + kv.Key + "='" + kv.Value + "'"); return 0; }
            int st = 0;
            foreach (var x in a.Skip(1))
            {
                int eq = x.IndexOf('=');
                if (eq > 0) sh.Aliases[x.Substring(0, eq)] = x.Substring(eq + 1);
                else { string v; if (sh.Aliases.TryGetValue(x, out v)) io.L("alias " + x + "='" + v + "'"); else { io.E("bash: alias: " + x + ": not found"); st = 1; } }
            }
            return st;
        }
        static int HistoryCmd(Sh sh, List<string> a, Io io)
        {
            if (a.Contains("-c")) { sh.History.Clear(); return 0; }
            if (a.Contains("-w") || a.Contains("-a")) { sh.SaveHistory(); return 0; }
            int n = sh.History.Count;
            if (a.Count > 1) int.TryParse(a[a.Count - 1], out n);
            int start = Math.Max(0, sh.History.Count - n);
            for (int i = start; i < sh.History.Count; i++) io.L((i + 1).ToString().PadLeft(5) + "  " + sh.History[i]);
            return 0;
        }
        /// <summary>~/.bash_history from earlier logins (up / down and history show it again).</summary>
        public void LoadHistory()
        {
            try
            {
                string home = GetVar("HOME");
                if (home == null) return;
                var l = M.V.Resolve(P, home + "/.bash_history", true);
                if (!l.Exists || l.Node.T != 'f') return;
                foreach (var x in (M.V.ReadNode(P, l) ?? "").Split('\n')) if (x.Length > 0) History.Add(x);
                while (History.Count > 1000) History.RemoveAt(0);
            }
            catch (Exception) { }
        }

        public void SaveHistory()
        {
            try
            {
                string home = GetVar("HOME");
                if (home == null) return;
                var l = M.V.Resolve(P, home + "/.bash_history", true);
                M.V.WriteLoc(P, l, string.Join("\n", History.ToArray()) + (History.Count > 0 ? "\n" : ""), false, 0x180);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ jobs
        static int Jobs_(Sh sh, List<string> a, Io io)
        {
            foreach (var j in sh.Jobs)
            {
                bool running = !j.Done && sh.M.ProcByPid(j.Pid) != null;
                if (a.Contains("-l")) io.L("[" + j.Id + "]+ " + j.Pid + " " + (running ? "Running" : "Done").PadRight(24) + j.Text + (running ? " &" : ""));
                else if (a.Contains("-p")) io.L(j.Pid.ToString());
                else io.L("[" + j.Id + "]+  " + (running ? "Running" : "Done").PadRight(24) + j.Text + (running ? " &" : ""));
            }
            return 0;
        }
        static int Fg(Sh sh, List<string> a, Io io)
        {
            ShJob j = null;
            if (a.Count > 1) { int id; if (int.TryParse(a[1].TrimStart('%'), out id)) j = sh.Jobs.FirstOrDefault(x => x.Id == id); }
            else j = sh.Jobs.LastOrDefault(x => !x.Done);
            if (j == null) { io.E("bash: fg: current: no such job"); return 1; }
            io.L(j.Text);
            var p = sh.M.ProcByPid(j.Pid);
            if (p == null) { j.Done = true; return 0; }
            if (p.EndsAt == long.MaxValue)
            {
                // runs until Ctrl+C
                if (sh.S == null) return 0;
                try { sh.S.WaitForBreak(); }
                catch (BreakSignal) { io.P("^C\n"); sh.M.Procs.Remove(p); j.Done = true; j.Reported = true; return 130; }
            }
            sh.M.Net.Advance(Math.Max(0, p.EndsAt - sh.M.Now));
            j.Done = true; j.Reported = true;
            return 0;
        }
        static int WaitCmd(Sh sh, List<string> a, Io io)
        {
            long until = sh.M.Now;
            foreach (var j in sh.Jobs) { var p = sh.M.ProcByPid(j.Pid); if (p != null && p.EndsAt != long.MaxValue) until = Math.Max(until, p.EndsAt); }
            if (until > sh.M.Now) sh.M.Net.Advance(until - sh.M.Now);
            return 0;
        }

        // ------------------------------------------------------------ umask, getopts, trap, mapfile, pushd
        static int Umask(Sh sh, List<string> a, Io io)
        {
            bool sym = a.Contains("-S");
            var rest = a.Skip(1).Where(x => x != "-S" && x != "-p").ToList();
            if (rest.Count == 0)
            {
                int m = sh.P.Umask;
                if (sym) { int p = ~m & 0x1FF; io.L("u=" + Vfs.Rwx(p >> 6).Replace("-", "") + ",g=" + Vfs.Rwx((p >> 3) & 7).Replace("-", "") + ",o=" + Vfs.Rwx(p & 7).Replace("-", "")); }
                else io.L(Convert.ToString(m, 8).PadLeft(4, '0'));
                return 0;
            }
            string v = rest[0];
            if (Regex.IsMatch(v, "^[0-7]{1,4}$")) { sh.P.Umask = Convert.ToInt32(v, 8) & 0x1FF; return 0; }
            // symbolic: u=rwx,g=rx,o=
            int perm = ~sh.P.Umask & 0x1FF;
            int np;
            if (!Cmd.ApplySymbolic(v, perm, true, 0, out np)) { io.E("bash: umask: `" + v + "': invalid symbolic mode operator"); return 1; }
            sh.P.Umask = ~np & 0x1FF;
            return 0;
        }
        static int Getopts(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 3) { io.E("getopts: usage: getopts optstring name [arg ...]"); return 2; }
            string spec = a[1], name = a[2];
            var args = a.Count > 3 ? a.Skip(3).ToList() : sh.Pos;
            bool silent = spec.StartsWith(":", StringComparison.Ordinal);
            if (silent) spec = spec.Substring(1);
            if (sh.OptInd < 1) sh.OptInd = 1;
            if (sh.OptInd > args.Count) { sh.SetVar(name, "?"); return 1; }
            string cur = args[sh.OptInd - 1];
            if (sh.optSub == 0)
            {
                if (cur == "--") { sh.OptInd++; sh.SetVar(name, "?"); return 1; }
                if (!cur.StartsWith("-", StringComparison.Ordinal) || cur == "-") { sh.SetVar(name, "?"); return 1; }
                sh.optSub = 1;
            }
            char c = cur[sh.optSub];
            sh.optSub++;
            bool endOfWord = sh.optSub >= cur.Length;
            int k = spec.IndexOf(c);
            if (k < 0 || c == ':')
            {
                if (endOfWord) { sh.OptInd++; sh.optSub = 0; }
                if (silent) { sh.SetVar(name, "?"); sh.SetVar("OPTARG", c.ToString()); }
                else { io.E(sh.Arg0 + ": illegal option -- " + c); sh.SetVar(name, "?"); sh.Unset("OPTARG"); }
                return 0;
            }
            if (k + 1 < spec.Length && spec[k + 1] == ':')
            {
                string arg;
                if (!endOfWord) { arg = cur.Substring(sh.optSub); sh.OptInd++; sh.optSub = 0; }
                else
                {
                    sh.OptInd++; sh.optSub = 0;
                    if (sh.OptInd > args.Count)
                    {
                        if (silent) { sh.SetVar(name, ":"); sh.SetVar("OPTARG", c.ToString()); }
                        else { io.E(sh.Arg0 + ": option requires an argument -- " + c); sh.SetVar(name, "?"); sh.Unset("OPTARG"); }
                        return 0;
                    }
                    arg = args[sh.OptInd - 1]; sh.OptInd++;
                }
                sh.SetVar("OPTARG", arg);
            }
            else { if (endOfWord) { sh.OptInd++; sh.optSub = 0; } sh.Unset("OPTARG"); }
            sh.SetVar(name, c.ToString());
            return 0;
        }
        static readonly string[] Signals = { "", "HUP", "INT", "QUIT", "ILL", "TRAP", "ABRT", "BUS", "FPE", "KILL", "USR1", "SEGV", "USR2", "PIPE", "ALRM", "TERM", "STKFLT", "CHLD", "CONT", "STOP", "TSTP", "TTIN", "TTOU", "URG", "XCPU", "XFSZ", "VTALRM", "PROF", "WINCH", "IO", "PWR", "SYS" };
        public static string SigName(int n) { return n > 0 && n < Signals.Length ? Signals[n] : n.ToString(); }
        public static int SigNum(string s)
        {
            s = s.ToUpperInvariant(); if (s.StartsWith("SIG", StringComparison.Ordinal)) s = s.Substring(3);
            int n; if (int.TryParse(s, out n)) return n;
            int i = Array.IndexOf(Signals, s); return i;
        }
        static int Trap(Sh sh, List<string> a, Io io)
        {
            if (a.Count == 1 || a[1] == "-p") { foreach (var kv in sh.Traps) io.L("trap -- '" + kv.Value + "' " + kv.Key); return 0; }
            if (a[1] == "-l") { var sb = new StringBuilder(); for (int i = 1; i < Signals.Length; i++) sb.Append((" " + i + ") SIG" + Signals[i]).PadRight(16) + (i % 5 == 0 ? "\n" : "")); io.L(sb.ToString().TrimEnd()); return 0; }
            string action = a[1];
            var sigs = a.Skip(2).ToList();
            if (sigs.Count == 0) { sigs.Add(action); action = "-"; }
            foreach (var s0 in sigs)
            {
                string s = s0.ToUpperInvariant(); if (s.StartsWith("SIG", StringComparison.Ordinal)) s = s.Substring(3);
                if (s == "0") s = "EXIT";
                int n; if (int.TryParse(s, out n) && n > 0 && n < Signals.Length) s = Signals[n];
                if (s != "EXIT" && s != "ERR" && s != "DEBUG" && s != "RETURN" && Array.IndexOf(Signals, s) < 0) { io.E("bash: trap: " + s0 + ": invalid signal specification"); return 1; }
                if (action == "-") sh.Traps.Remove(s); else sh.Traps[s] = action;
            }
            return 0;
        }
        static int Mapfile(Sh sh, List<string> a, Io io)
        {
            bool trim = a.Contains("-t");
            string name = a.Skip(1).LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) ?? "MAPFILE";
            var lines = new List<string>();
            string l;
            while ((l = io.In.ReadLine()) != null) lines.Add(trim ? l : l + "\n");
            sh.SetArray(name, lines, false);
            return 0;
        }
        static int Pushd(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { if (sh.DirStack.Count == 0) { io.E("bash: pushd: no other directory"); return 1; } string t = sh.DirStack[0]; sh.DirStack[0] = sh.P.Cwd; Cd(sh, new List<string> { "cd", t }, io); }
            else { string old = sh.P.Cwd; if (Cd(sh, new List<string> { "cd", a[1] }, io) != 0) return 1; sh.DirStack.Insert(0, old); }
            io.L(string.Join(" ", new[] { sh.TildePath(sh.P.Cwd) }.Concat(sh.DirStack.Select(sh.TildePath)).ToArray()));
            return 0;
        }
        static int Popd(Sh sh, List<string> a, Io io)
        {
            if (sh.DirStack.Count == 0) { io.E("bash: popd: directory stack empty"); return 1; }
            string t = sh.DirStack[0]; sh.DirStack.RemoveAt(0);
            Cd(sh, new List<string> { "cd", t }, io);
            io.L(string.Join(" ", new[] { sh.TildePath(sh.P.Cwd) }.Concat(sh.DirStack.Select(sh.TildePath)).ToArray()));
            return 0;
        }

        // ------------------------------------------------------------ test, [ and [[
        int TestCmd(List<string> a, Io io, string name)
        {
            try { return new TestEval(this, a, false).Run() ? 0 : 1; }
            catch (ShError e) { io.E("bash: " + name + ": " + e.Message); return 2; }
        }
        bool CondEval(List<string> words)
        {
            // expand each word (no splitting or globbing); the right side of == != is a pattern
            var ex = new List<string>();
            for (int i = 0; i < words.Count; i++)
            {
                string w = words[i];
                if (w.StartsWith("\u0001", StringComparison.Ordinal)) { ex.Add(w); continue; }
                string prev = i > 0 ? words[i - 1] : "";
                if (prev == "==" || prev == "!=" || prev == "=") ex.Add("\u0002" + ExpandPattern(w));
                else if (prev == "=~") ex.Add("\u0003" + RegexWord(w));
                else ex.Add(ExpandOne(w));
            }
            try { return new TestEval(this, ex, true).Run(); }
            catch (ShError e) { Io.E("bash: " + e.Message); return false; }
        }
        string RegexWord(string w)
        {
            // quoted parts of the regex match literally
            var sb = new StringBuilder();
            int i = 0;
            while (i < w.Length)
            {
                char c = w[i];
                if (c == '"' || c == '\'')
                {
                    int e = w.IndexOf(c, i + 1); if (e < 0) e = w.Length;
                    string inner = w.Substring(i + 1, e - i - 1);
                    sb.Append(Regex.Escape(c == '"' ? ExpandOne("\"" + inner + "\"") : inner));
                    i = e + 1; continue;
                }
                if (c == '\\' && i + 1 < w.Length) { sb.Append(Regex.Escape(w[i + 1].ToString())); i += 2; continue; }
                if (c == '$') { int st = i; i++; while (i < w.Length && (char.IsLetterOrDigit(w[i]) || w[i] == '_' || w[i] == '{' || w[i] == '}')) i++; sb.Append(ExpandOne(w.Substring(st, i - st))); continue; }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }
    }

    public class TestEval
    {
        readonly Sh sh; readonly List<string> a; readonly bool cond; int i;
        public TestEval(Sh s, List<string> args, bool dbl) { sh = s; a = args; cond = dbl; }

        static string W(string x) { return x.StartsWith("\u0001", StringComparison.Ordinal) || x.StartsWith("\u0002", StringComparison.Ordinal) || x.StartsWith("\u0003", StringComparison.Ordinal) ? x.Substring(1) : x; }
        bool IsOp(int k, string op) { return k < a.Count && (cond ? (a[k] == "\u0001" + op || a[k] == op) : a[k] == op); }

        public bool Run()
        {
            if (a.Count == 0) return false;
            if (!cond)
            {
                // POSIX rules for 1-3 arguments
                if (a.Count == 1) return a[0].Length > 0;
                if (a.Count == 2 && a[0] == "!") return a[1].Length == 0;
                if (a.Count == 2) return Unary(a[0], a[1]);
                if (a.Count == 3 && IsBinary(a[1])) return Binary(a[0], a[1], a[2]);
            }
            bool r = Or();
            if (i < a.Count) throw new ShError(cond ? "syntax error in conditional expression" : "too many arguments");
            return r;
        }
        bool Or() { bool v = And(); while (IsOp(i, "||") || (!cond && i < a.Count && a[i] == "-o")) { i++; bool r = And(); v = v || r; } return v; }
        bool And() { bool v = Not(); while (IsOp(i, "&&") || (!cond && i < a.Count && a[i] == "-a")) { i++; bool r = Not(); v = v && r; } return v; }
        bool Not()
        {
            if (i < a.Count && W(a[i]) == "!" && !(i + 1 < a.Count && IsBinary(W(a[i + 1])) && i + 2 < a.Count)) { i++; return !Not(); }
            return Prim();
        }
        bool Prim()
        {
            if (i >= a.Count) throw new ShError("argument expected");
            if (IsOp(i, "(")) { i++; bool v = Or(); if (!IsOp(i, ")")) throw new ShError("`)' expected"); i++; return v; }
            string x = W(a[i]);
            if (i + 2 < a.Count + 0 && i + 1 < a.Count && IsBinary(W(a[i + 1])) && i + 2 < a.Count)
            {
                string op = W(a[i + 1]), y = a[i + 2];
                i += 3;
                return Binary(x, op, y);
            }
            if (x.StartsWith("-", StringComparison.Ordinal) && x.Length == 2 && i + 1 < a.Count) { i += 2; return Unary(x, W(a[i - 1])); }
            i++;
            return x.Length > 0;
        }
        static bool IsBinary(string op) { return op == "=" || op == "==" || op == "!=" || op == "=~" || op == "<" || op == ">" || op == "-eq" || op == "-ne" || op == "-lt" || op == "-le" || op == "-gt" || op == "-ge" || op == "-nt" || op == "-ot" || op == "-ef"; }

        long Int(string s)
        {
            if (cond) return sh.Arith(s);
            long n; if (!long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) throw new ShError(s + ": integer expression expected");
            return n;
        }

        bool Binary(string x, string op, string y0)
        {
            string y = W(y0);
            switch (op)
            {
                case "=": case "==": return y0.StartsWith("\u0002", StringComparison.Ordinal) ? Sh.GlobMatch(y, x) : x == y;
                case "!=": return y0.StartsWith("\u0002", StringComparison.Ordinal) ? !Sh.GlobMatch(y, x) : x != y;
                case "=~":
                    {
                        Match m;
                        try { m = Regex.Match(x, y); } catch (ArgumentException) { throw new ShError("invalid regular expression"); }
                        if (m.Success) sh.SetArray("BASH_REMATCH", m.Groups.Cast<Group>().Select(g => g.Value).ToList(), false);
                        return m.Success;
                    }
                case "<": return string.CompareOrdinal(x, y) < 0;
                case ">": return string.CompareOrdinal(x, y) > 0;
                case "-eq": return Int(x) == Int(y);
                case "-ne": return Int(x) != Int(y);
                case "-lt": return Int(x) < Int(y);
                case "-le": return Int(x) <= Int(y);
                case "-gt": return Int(x) > Int(y);
                case "-ge": return Int(x) >= Int(y);
                case "-nt": { var a1 = Node(x); var b1 = Node(y); return a1 != null && (b1 == null || a1.Mtime > b1.Mtime); }
                case "-ot": { var a1 = Node(x); var b1 = Node(y); return b1 != null && (a1 == null || a1.Mtime < b1.Mtime); }
                case "-ef": { var a1 = Node(x); var b1 = Node(y); return a1 != null && a1 == b1; }
            }
            return false;
        }

        Inode Node(string p) { try { var l = sh.M.V.Resolve(sh.P, p, true); return l.Node; } catch (VfsEx) { return null; } }
        Inode LNode(string p) { try { var l = sh.M.V.Resolve(sh.P, p, false); return l.Node; } catch (VfsEx) { return null; } }

        bool Unary(string op, string x)
        {
            switch (op)
            {
                case "-z": return x.Length == 0;
                case "-n": return x.Length > 0;
                case "-e": case "-a": return Node(x) != null;
                case "-f": { var n = Node(x); return n != null && n.T == 'f'; }
                case "-d": { var n = Node(x); return n != null && n.T == 'd'; }
                case "-L": case "-h": { var n = LNode(x); return n != null && n.T == 'l'; }
                case "-b": { var n = Node(x); return n != null && n.T == 'b'; }
                case "-c": { var n = Node(x); return n != null && n.T == 'c'; }
                case "-p": { var n = Node(x); return n != null && n.T == 'p'; }
                case "-S": { var n = Node(x); return n != null && n.T == 's'; }
                case "-s": { var n = Node(x); return n != null && n.Size > 0 && n.T != 'd' || (n != null && n.T == 'd'); }
                case "-r": { var n = Node(x); return n != null && sh.M.V.Can(sh.P.Cr, n, 4); }
                case "-w": { var n = Node(x); return n != null && sh.M.V.Can(sh.P.Cr, n, 2); }
                case "-x": { var n = Node(x); return n != null && sh.M.V.Can(sh.P.Cr, n, 1); }
                case "-u": { var n = Node(x); return n != null && (n.Mode & 0x800) != 0; }
                case "-g": { var n = Node(x); return n != null && (n.Mode & 0x400) != 0; }
                case "-k": { var n = Node(x); return n != null && (n.Mode & 0x200) != 0; }
                case "-O": { var n = Node(x); return n != null && n.Uid == sh.P.Cr.Uid; }
                case "-G": { var n = Node(x); return n != null && n.Gid == sh.P.Cr.Gid; }
                case "-N": { var n = Node(x); return n != null && n.Mtime > n.Atime; }
                case "-t": return x == "0" || x == "1" || x == "2" ? sh.S != null : false;
                case "-v": return sh.GetVarObj(x) != null;
                case "-o": return false;
            }
            throw new ShError(op + ": unary operator expected");
        }
    }

    /// <summary>printf formats and echo -e escapes.</summary>
    public static class Printf
    {
        public static string Escapes(string s, bool echoStyle) { bool stop; return Escapes(s, echoStyle, out stop); }
        public static string Escapes(string s, bool echoStyle, out bool stop)
        {
            stop = false;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char d = s[++i];
                switch (d)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'e': case 'E': sb.Append('\u001b'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\'': sb.Append('\''); break;
                    case '"': sb.Append('"'); break;
                    case 'c': if (echoStyle) { stop = true; return sb.ToString(); } sb.Append("\\c"); break;
                    case '0':
                        {
                            int v = 0, k = 0;
                            while (k < 3 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7') { v = v * 8 + (s[i + 1] - '0'); i++; k++; }
                            sb.Append((char)v); break;
                        }
                    case 'x':
                        {
                            int v = 0, k = 0;
                            while (k < 2 && i + 1 < s.Length && Uri.IsHexDigit(s[i + 1])) { v = v * 16 + Convert.ToInt32(s[i + 1].ToString(), 16); i++; k++; }
                            sb.Append((char)v); break;
                        }
                    default:
                        if (!echoStyle && d >= '1' && d <= '7')
                        {
                            int v = d - '0', k = 1;
                            while (k < 3 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7') { v = v * 8 + (s[i + 1] - '0'); i++; k++; }
                            sb.Append((char)v); break;
                        }
                        sb.Append('\\').Append(d); break;
                }
            }
            return sb.ToString();
        }

        public static string Format(string fmt, List<string> args, out string error)
        {
            error = null;
            var sb = new StringBuilder();
            int ai = 0;
            bool usedArg;
            int rounds = 0;
            do
            {
                usedArg = false;
                for (int i = 0; i < fmt.Length; i++)
                {
                    char c = fmt[i];
                    if (c == '\\') { int e = i + 1; string esc = "\\" + (e < fmt.Length ? fmt[e].ToString() : ""); if (e < fmt.Length && (fmt[e] == '0' || fmt[e] == 'x')) { e++; while (e < fmt.Length && e - i < 5 && Uri.IsHexDigit(fmt[e])) e++; esc = fmt.Substring(i, e - i); i = e - 1; } else i = e; sb.Append(Escapes(esc, false)); continue; }
                    if (c != '%') { sb.Append(c); continue; }
                    if (i + 1 < fmt.Length && fmt[i + 1] == '%') { sb.Append('%'); i++; continue; }
                    var m = Regex.Match(fmt.Substring(i), "^%([-+ #0]*)(\\*|\\d+)?(?:\\.(\\*|\\d+))?([sdiufFeEgGxXocbq])");
                    if (!m.Success) { sb.Append(c); continue; }
                    i += m.Length - 1;
                    string flags = m.Groups[1].Value;
                    string wid = m.Groups[2].Value, prec = m.Groups[3].Value;
                    if (wid == "*") { wid = ai < args.Count ? args[ai++] : "0"; usedArg = true; }
                    if (prec == "*") { prec = ai < args.Count ? args[ai++] : "0"; usedArg = true; }
                    char conv = m.Groups[4].Value[0];
                    string arg = ai < args.Count ? args[ai] : null;
                    if (arg != null) { ai++; usedArg = true; }
                    string s;
                    switch (conv)
                    {
                        case 's': s = arg ?? ""; if (prec.Length > 0) { int p = int.Parse(prec); if (s.Length > p) s = s.Substring(0, p); } break;
                        case 'b': s = Escapes(arg ?? "", true); break;
                        case 'q': s = Sh.Quote(arg ?? ""); break;
                        case 'c': s = string.IsNullOrEmpty(arg) ? "" : arg.Substring(0, 1); break;
                        case 'd': case 'i': case 'u': case 'x': case 'X': case 'o':
                            {
                                long v = 0;
                                string t = (arg ?? "0").Trim();
                                if (t.StartsWith("'", StringComparison.Ordinal) || t.StartsWith("\"", StringComparison.Ordinal)) v = t.Length > 1 ? t[1] : 0;
                                else if (t.Length > 0 && !long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
                                {
                                    try { v = t.StartsWith("0x", StringComparison.Ordinal) ? Convert.ToInt64(t.Substring(2), 16) : Convert.ToInt64(t, 8); } catch (Exception) { error = t + ": invalid number"; v = 0; }
                                }
                                if (conv == 'x') s = v.ToString("x"); else if (conv == 'X') s = v.ToString("X"); else if (conv == 'o') s = Convert.ToString(v, 8);
                                else s = v.ToString(CultureInfo.InvariantCulture);
                                if (prec.Length > 0) s = s.PadLeft(int.Parse(prec), '0');
                                if (flags.Contains("+") && v >= 0 && (conv == 'd' || conv == 'i')) s = "+" + s;
                                break;
                            }
                        default:
                            {
                                double v = 0;
                                if (!string.IsNullOrEmpty(arg) && !double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) error = arg + ": invalid number";
                                int p = prec.Length > 0 ? int.Parse(prec) : 6;
                                if (conv == 'f' || conv == 'F') s = v.ToString("F" + p, CultureInfo.InvariantCulture);
                                else if (conv == 'e' || conv == 'E') s = v.ToString((conv == 'e' ? "e" : "E") + p, CultureInfo.InvariantCulture).Replace("e+0", "e+").Replace("E+0", "E+");
                                else s = v.ToString("G" + p, CultureInfo.InvariantCulture);
                                break;
                            }
                    }
                    if (wid.Length > 0)
                    {
                        int w = int.Parse(wid);
                        if (flags.Contains("-")) s = s.PadRight(w);
                        else if (flags.Contains("0") && conv != 's') { bool neg = s.StartsWith("-", StringComparison.Ordinal); s = (neg ? "-" : "") + s.TrimStart('-').PadLeft(w - (neg ? 1 : 0), '0'); }
                        else s = s.PadLeft(w);
                    }
                    sb.Append(s);
                }
                rounds++;
            } while (usedArg && ai < args.Count && rounds < 1000);
            return sb.ToString();
        }
    }
}
