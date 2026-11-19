// Cyber Range - the extra programs of the player's box (each has a manual page in Courses\rhcsa\man): smbclient
// (pretend Windows file shares), winrm-shell (a PowerShell session on a pretend Windows host), ftp, mysql (a small
// pretend database client), hashcrack (tries a word list against the simulator's own hash formats), pwguess (tries
// passwords against a pretend ssh / ftp server, noisily), webscan (tries common page names), vulnlookup (the range's
// made-up vulnerability notes) and ticket-request (asks the pretend domain for service tickets).
// They only ever talk to the simulated range network: nothing reaches this PC or a real network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class RangeTools
    {
        public static readonly string[] All = { "smbclient", "winrm-shell", "ftp", "mysql", "hashcrack", "pwguess", "webscan", "vulnlookup", "ticket-request", "xorcat", "vigenere", "morse", "catcher", "browse", "nc", "siem", "cloudctl", "toyvm" };
        static bool done;
        public static void Register()
        {
            if (done) return;
            done = true;
            Cmd.RegRange();
        }
        public static void InstallTool(Machine m, string name)
        {
            var n = m.V.Put("/usr/local/bin/" + name, null, 0x1ED, 0, 0, null);
            n.Text = null; n.BinSize = 60000 + name.Length * 2711; n.Prog = name;
        }
        /// <summary>The attack box's own made-up distro banner (never "Kali"): shown instead of the RHEL welcome.</summary>
        public const string AttackBannerText = "RangeKit Linux 3.2 \"Toolbox\"   (a made-up practice distribution - Cyber Range)\nYour own box. The tools you need are installed; the targets are all pretend.\nTry: nmap, curl, smbclient, winrm-shell, ftp, mysql, hashcrack, pwguess, webscan, vulnlookup.\n";

        /// <summary>The player's own box: the tools, nmap, the word lists, its banner and a short welcome note.</summary>
        public static void SetUpAttackBox(Machine m)
        {
            foreach (var t in All) InstallTool(m, t);
            var nm = Packages.Get("nmap");
            if (nm != null && !m.PackageInstalled("nmap")) m.InstallPkg(nm, "AppStream");
            m.V.EnsureDir("/usr/share/wordlists", 0x1ED, null);
            m.V.Put("/usr/share/wordlists/range-common.txt", RangeData.WordText, 0x1A4, 0, 0, null);
            m.V.Put("/usr/share/wordlists/range-paths.txt", string.Join("\n", WebPaths) + "\n", 0x1A4, 0, 0, null);
            if (m.Banner.Length == 0) m.Banner = AttackBannerText;
        }
        public static readonly string[] WebPaths = { "admin", "backup", "backups", "config", "debug", "dev", "docs", "files", "hidden", "images", "index.html", "login", "notes", "old", "private", "robots.txt", "secret", "server-status", "staff", "test", "tools", "upload", "uploads", "download", "api", "status", "portal", "preview", ".git", "wp-admin", "phpinfo.php" };
    }

    public static partial class Cmd
    {
        public static void RegRange()
        {
            P("smbclient", SmbClient); P("winrm-shell", WinrmShell); P("ftp", Ftp); P("mysql", Mysql); P("hashcrack", HashCrack);
            P("pwguess", PwGuess); P("webscan", WebScan); P("vulnlookup", VulnLookup); P("ticket-request", TicketRequest);
            P("xorcat", XorCat); P("vigenere", VigenereCmd); P("morse", MorseCmd);
            RegRange3();
        }

        static string Ask(Sh sh, Io io, string prompt, bool secret)
        {
            if (io.In != null && !io.In.IsTty) { var l = io.In.ReadLine(); if (l != null) return l; }
            if (sh.S == null) return null;
            return sh.S.ReadLine(prompt, secret);
        }
        static RangeNet RangeOf(Sh sh, Io io, string prog)
        {
            var r = sh.M.Net == null ? null : sh.M.Net.Range;
            if (r == null) io.E(prog + ": this program only works in Cyber Range (it talks to the range's pretend network).");
            return r;
        }
        static List<string> Words(string line)
        {
            var l = new List<string>(); var sb = new StringBuilder(); char q = '\0'; bool any = false;
            foreach (char c in line ?? "")
            {
                if (q != '\0') { if (c == q) q = '\0'; else sb.Append(c); continue; }
                if (c == '"' || c == '\'') { q = c; any = true; continue; }
                if (c == ' ' || c == '\t') { if (sb.Length > 0 || any) { l.Add(sb.ToString()); sb.Clear(); any = false; } continue; }
                sb.Append(c);
            }
            if (sb.Length > 0 || any) l.Add(sb.ToString());
            return l;
        }
        static string Stamp(DateTime t) { return t.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------ smbclient
        static int SmbClient(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "smbclient"); if (net == null) return 1;
            string list = null, target = null, user = null, cmds = null; bool noPass = false;
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-L" && i + 1 < a.Count) list = a[++i];
                else if (s == "-N" || s == "--no-pass") noPass = true;
                else if ((s == "-U" || s == "--user") && i + 1 < a.Count) user = a[++i];
                else if (s.StartsWith("-U", StringComparison.Ordinal) && s.Length > 2) user = s.Substring(2);
                else if (s == "-c" && i + 1 < a.Count) cmds = a[++i];
                else if (s.StartsWith("-", StringComparison.Ordinal)) { io.E("smbclient: unknown option " + s + " (Cyber Range simulates -L, -N, -U and -c)"); return 1; }
                else target = s;
            }
            string spec = list ?? target;
            if (spec == null) { io.E("Usage: smbclient [-N] [-U user[%password]] [-c commands] //server/share\n       smbclient -L //server [-N] [-U user[%password]]"); return 1; }
            var m0 = Regex.Match(spec.Replace('\\', '/'), "^//([^/]+)(?:/([^/]+))?/?$");
            if (!m0.Success) { io.E(spec + ": Not enough '\\' characters in service"); return 1; }
            string host = m0.Groups[1].Value, share = m0.Groups[2].Success ? m0.Groups[2].Value : null;
            if (list == null && share == null) { io.E(spec + ": Not enough '\\' characters in service"); return 1; }
            RangeHost h; string ip;
            string err = net.ConnectHost(sh.M, host, 445, out h, out ip);
            if (err != null)
            {
                io.E("do_connect: Connection to " + host + " failed (Error " + (err == "resolve" ? "NT_STATUS_UNSUCCESSFUL" : err == "timeout" ? "NT_STATUS_IO_TIMEOUT" : err == "noroute" ? "NT_STATUS_HOST_UNREACHABLE" : "NT_STATUS_CONNECTION_REFUSED") + ")");
                return 1;
            }
            string pass = null;
            if (user != null && user.Contains("%")) { pass = user.Substring(user.IndexOf('%') + 1); user = user.Substring(0, user.IndexOf('%')); }
            bool anon = string.IsNullOrEmpty(user) || user.Equals("guest", StringComparison.OrdinalIgnoreCase);
            if (!anon && pass == null && !noPass) pass = Ask(sh, io, "Password for [" + (user.Contains("\\") ? user : "WORKGROUP\\" + user) + "]:", true) ?? "";
            if (anon && !noPass && list == null) { var pp = Ask(sh, io, "Password for [WORKGROUP\\" + (user ?? "root") + "]:", true); }
            bool admin = false;
            RangeAccount acct = null;
            if (!anon)
            {
                acct = h.Account(user, out admin);
                if (acct == null || acct.Password != pass)
                {
                    net.Noise(sh.M, ip, "login-failed", 2, "smb as " + user);
                    io.E("session setup failed: NT_STATUS_LOGON_FAILURE");
                    return 1;
                }
                net.LoggedIn(sh.M, ip, acct.User, admin, "smb", pass, null);
            }
            else net.Noise(sh.M, ip, "smb-anonymous", 1, list != null ? "list shares" : "share " + share);
            if (list != null)
            {
                io.L("\n\tSharename       Type      Comment\n\t---------       ----      -------");
                io.L("\tADMIN$          Disk      Remote Admin");
                io.L("\tC$              Disk      Default share");
                foreach (var s in h.Shares) io.L("\t" + s.Name.PadRight(16) + "Disk      " + s.Comment);
                io.L("\tIPC$            IPC       Remote IPC");
                io.L("SMB1 disabled -- no workgroup available");
                return 0;
            }
            string root;
            var sd = h.Shares.FirstOrDefault(x => x.Name.Equals(share, StringComparison.OrdinalIgnoreCase));
            if (share.Equals("C$", StringComparison.OrdinalIgnoreCase) || share.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase))
            {
                if (!admin) { io.E("tree connect failed: NT_STATUS_ACCESS_DENIED"); return 1; }
                root = share.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "C:\\" : "C:\\Windows";
            }
            else if (sd == null) { io.E("tree connect failed: NT_STATUS_BAD_NETWORK_NAME"); return 1; }
            else
            {
                bool ok = anon ? sd.Anonymous : (admin || sd.Users.Count == 0 || sd.Users.Any(u => u.Equals(acct.User, StringComparison.OrdinalIgnoreCase)));
                if (!ok) { io.E("tree connect failed: NT_STATUS_ACCESS_DENIED"); return 1; }
                root = sd.Path;
            }
            string cwd = "";
            Func<string, string> full = rel =>
            {
                var parts = new List<string>(cwd.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries));
                string r = (rel ?? "").Replace('/', '\\');
                if (r.StartsWith("\\", StringComparison.Ordinal)) parts.Clear();
                foreach (var p in r.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { if (p == ".") continue; if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; } parts.Add(p); }
                return string.Join("\\", parts.ToArray());
            };
            Func<string, WinFile> node = rel => h.Ps.FindFile(root.TrimEnd('\\') + (rel.Length > 0 ? "\\" + rel : ""));
            Func<string, int> run = line =>
            {
                var w = Words(line);
                if (w.Count == 0) return 0;
                string c = w[0].ToLowerInvariant();
                switch (c)
                {
                    case "ls": case "dir":
                    {
                        var d = node(cwd);
                        if (d == null || !d.IsDir) { io.L("NT_STATUS_OBJECT_PATH_NOT_FOUND listing \\" + cwd); return 1; }
                        string mask = w.Count > 1 ? w[1] : "*";
                        io.L("  " + ".".PadRight(36) + "D        0  " + Stamp(d.Modified));
                        io.L("  " + "..".PadRight(36) + "D        0  " + Stamp(d.Modified));
                        foreach (var k in d.Kids.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            if (!Sh.GlobMatch(mask, k.Name)) continue;
                            string attr = k.IsDir ? "D" : ((k.Attr & 2) != 0 ? "AH" : "A");
                            long size = k.IsDir ? 0 : (k.FakeSize >= 0 ? k.FakeSize : Encoding.UTF8.GetByteCount(k.Text ?? ""));
                            io.L("  " + k.Name.PadRight(36) + attr.PadRight(4) + size.ToString(CultureInfo.InvariantCulture).PadLeft(9) + "  " + Stamp(k.Modified));
                        }
                        io.L("\n\t\t10485247 blocks of size 4096. 2348710 blocks available");
                        return 0;
                    }
                    case "cd":
                    {
                        if (w.Count < 2) { io.L("Current directory is \\\\" + host + "\\" + share + "\\" + cwd); return 0; }
                        string nd = full(w[1]); var d = node(nd);
                        if (d == null || !d.IsDir) { io.L("cd \\" + nd + "\\: NT_STATUS_OBJECT_NAME_NOT_FOUND"); return 1; }
                        cwd = nd; return 0;
                    }
                    case "pwd": io.L("Current directory is \\\\" + host + "\\" + share + "\\" + cwd + (cwd.Length > 0 ? "\\" : "")); return 0;
                    case "get": case "more": case "cat":
                    {
                        if (w.Count < 2) { io.L(c + " <filename>"); return 1; }
                        string f = full(w[1]); var n = node(f);
                        if (n == null || n.IsDir) { io.L("NT_STATUS_OBJECT_NAME_NOT_FOUND opening remote file \\" + f); return 1; }
                        string text = n.Text ?? "";
                        if (c != "get") { io.P(text.Replace("\r\n", "\n") + (text.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n")); return 0; }
                        string local = w.Count > 2 ? w[2] : n.Name;
                        try { sh.M.V.WriteText(sh.P, local, text.Replace("\r\n", "\n"), false); }
                        catch (VfsEx e) { io.L("Error opening local file " + local + ": " + VfsEx.Msg(e.Errno)); return 1; }
                        long size = Encoding.UTF8.GetByteCount(text);
                        io.L("getting file \\" + f + " of size " + size + " as " + local + " (" + (size / 10.0 + 12.5).ToString("0.0", CultureInfo.InvariantCulture) + " KiloBytes/sec) (average " + (size / 10.0 + 12.5).ToString("0.0", CultureInfo.InvariantCulture) + " KiloBytes/sec)");
                        net.Noise(sh.M, ip, "smb-read", 1, share + "\\" + f);
                        return 0;
                    }
                    case "put": case "mkdir": case "rm": case "del": case "rmdir":
                        io.L("NT_STATUS_ACCESS_DENIED (Cyber Range's shares are read-only)"); return 1;
                    case "help": case "?":
                        io.L("Commands: ls [mask]  cd <folder>  pwd  get <file> [local name]  more <file>  exit"); return 0;
                    case "exit": case "quit": case "q": case "bye": return -1;
                }
                io.L(w[0] + ": command not found");
                return 1;
            };
            if (cmds != null)
            {
                int st = 0;
                foreach (var part in cmds.Split(';')) { int r = run(part.Trim()); if (r < 0) break; if (r > 0) st = 1; }
                return st;
            }
            io.L("Try \"help\" to get a list of possible commands.");
            for (;;)
            {
                string line;
                try { line = Ask(sh, io, "smb: \\" + (cwd.Length > 0 ? cwd + "\\" : "") + "> ", false); }
                catch (BreakSignal) { io.P("\n"); return 1; }
                if (line == null) { io.P("\n"); return 0; }
                if (run(line) < 0) return 0;
            }
        }

        // ------------------------------------------------------------ winrm-shell
        class RangePsHost : IPsHost
        {
            public Sh Sh; public Io Io;
            public void Write(string text) { Io.P(text.Replace("\r\n", "\n")); }
            public string ReadLine(string prompt, bool secret) { return Sh.S == null ? null : Sh.S.ReadLine(prompt, secret); }
            public bool Interactive { get { return Sh.S != null; } }
            public int Width { get { return 120; } }
        }

        static int WinrmShell(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "winrm-shell"); if (net == null) return 1;
            var o = Opts.Parse(a, "i:u:p:H:", "ip=|i,user=|u,password=|p,hash=|H", "winrm-shell");
            if (o.Error != null) { io.E(o.Error); return 1; }
            string host = o.Get("i") ?? (o.Args.Count > 0 ? o.Args[0] : null), user = o.Get("u");
            if (host == null || user == null) { io.E("Usage: winrm-shell -i HOST -u USER [-p PASSWORD | -H HASH]"); return 1; }
            RangeHost h; string ip;
            string err = net.ConnectHost(sh.M, host, 5985, out h, out ip);
            if (err != null) { io.E("winrm-shell: could not connect to " + host + " port 5985 (" + (err == "resolve" ? "unknown host" : err == "timeout" ? "connection timed out" : err == "noroute" ? "no route to host" : "connection refused") + ")"); return 1; }
            string pass = o.Get("p"), hash = o.Get("H");
            if (pass == null && hash == null) pass = Ask(sh, io, "Password: ", true) ?? "";
            bool admin;
            var acct = h.Account(user, out admin);
            bool ok = acct != null && (acct.Remote || admin) && (hash != null ? hash.Trim().ToLowerInvariant() == acct.SimHash : acct.Password == pass);
            if (!ok)
            {
                net.Noise(sh.M, ip, "login-failed", 2, "winrm as " + user);
                io.E("winrm-shell: " + (acct != null && !acct.Remote && !admin && (hash != null ? hash.Trim().ToLowerInvariant() == acct.SimHash : acct.Password == pass) ? "the account may not use remote management (it is not in Remote Management Users)." : "the user name or password was not accepted (HTTP 401 Unauthorized)."));
                return 1;
            }
            net.LoggedIn(sh.M, ip, acct.User, admin, "winrm", hash != null ? "hash " + hash.Trim().ToLowerInvariant() : pass, hash != null ? "with a hash" : null);
            var bridge = new RangePsHost { Sh = sh, Io = io };
            var run = new PsRunspace(h.Ps, bridge, acct.User);
            string acting = admin ? null : acct.User;
            io.L("Cyber Range remote shell: connected to " + h.Name + " (" + ip + ") as " + acct.User + (admin ? " (an administrator)" : "") + ".");
            io.L("PowerShell runs on the pretend Windows host. Extra commands: download FILE [LOCAL], upload LOCAL [FILE], exit.\n");
            try
            {
                PsMachine.Acting = acting; PsMachine.ActingOn = h.Ps;
                try { run.ExecuteInteractive("Set-Location 'C:\\Users\\" + acct.User + "\\Documents'"); } catch (Exception) { }
            }
            finally { PsMachine.Acting = null; PsMachine.ActingOn = null; }
            var buf = new StringBuilder();
            for (;;)
            {
                string prompt = buf.Length > 0 ? ">> " : "*" + run.PromptText();
                string line;
                try { line = Ask(sh, io, prompt, false); }
                catch (BreakSignal) { buf.Clear(); io.P("\n"); continue; }
                if (line == null) break;
                string t = line.Trim();
                if (buf.Length == 0)
                {
                    if (t.Equals("exit", StringComparison.OrdinalIgnoreCase) || t.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
                    var w = Words(t);
                    if (w.Count > 0 && (w[0] == "download" || w[0] == "upload"))
                    {
                        PsMachine.Acting = acting; PsMachine.ActingOn = h.Ps;
                        try { Transfer(sh, io, run, w); }
                        catch (ProvError e) { io.L("Error: " + e.Message); }
                        finally { PsMachine.Acting = null; PsMachine.ActingOn = null; }
                        continue;
                    }
                }
                if (buf.Length > 0) buf.Append('\n');
                buf.Append(line);
                bool done = true;
                PsMachine.Acting = acting; PsMachine.ActingOn = h.Ps;
                try { done = run.ExecuteInteractive(buf.ToString()); }
                catch (PsBreakSignal) { done = true; io.P("\n"); }
                catch (BreakSignal) { done = true; io.P("\n"); }
                finally { PsMachine.Acting = null; PsMachine.ActingOn = null; }
                if (!done) continue;
                net.RemoteCommand(sh.M, ip, buf.ToString());
                buf.Clear();
                net.SyncFromPs();
                if (run.ExitRequested) break;
            }
            h.Ps.Sessions.Remove(run);
            io.L("Connection to " + h.Name + " closed.");
            return 0;
        }

        static void Transfer(Sh sh, Io io, PsRunspace run, List<string> w)
        {
            if (w.Count < 2) { io.L("Usage: download FILE [LOCAL]   upload LOCAL [FILE]"); return; }
            if (w[0] == "download")
            {
                string full = run.FullFsPath(w[1], run.CurrentFsDir);
                var f = run.M.FindFile(full);
                if (f == null || f.IsDir) { io.L("Error: " + full + " was not found (or is a folder)."); return; }
                string local = w.Count > 2 ? w[2] : f.Name;
                try { sh.M.V.WriteText(sh.P, local, (f.Text ?? "").Replace("\r\n", "\n"), false); }
                catch (VfsEx e) { io.L("Error: " + local + ": " + VfsEx.Msg(e.Errno)); return; }
                io.L("Downloaded " + full + " to " + local + ".");
                return;
            }
            string text;
            try { text = sh.M.V.ReadText(sh.P, w[1]); } catch (VfsEx e) { io.L("Error: " + w[1] + ": " + VfsEx.Msg(e.Errno)); return; }
            string dest = run.FullFsPath(w.Count > 2 ? w[2] : Vfs.Base(w[1]), run.CurrentFsDir);
            string why = run.WriteFile(dest, text.Replace("\n", "\r\n"), "ascii", false);
            io.L(why ?? "Uploaded " + w[1] + " to " + dest + ".");
        }

        // ------------------------------------------------------------ ftp
        static int Ftp(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "ftp"); if (net == null) return 1;
            var args = a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            if (args.Count == 0) { io.E("usage: ftp HOST (Cyber Range's ftp connects straight away)"); return 1; }
            string host = args[0];
            object tg; string ip;
            string err = sh.M.Connect(host, 21, out tg, out ip);
            var tm = tg as Machine;
            if (err != null || tm == null) { io.E(err == "resolve" ? "ftp: " + host + ": Name or service not known" : "ftp: connect: " + (err == "timeout" ? "Connection timed out" : err == "noroute" ? "No route to host" : "Connection refused")); return 1; }
            io.L("Connected to " + host + " (" + ip + ").\n220 (vsFTPd 3.0.5)");
            string conf = tm.V.Cat("/etc/vsftpd/vsftpd.conf") ?? "";
            bool anonOk = Regex.IsMatch(conf, "(?m)^\\s*anonymous_enable\\s*=\\s*YES");
            var denied = (tm.V.Cat("/etc/vsftpd/ftpusers") ?? "root\n").Split('\n').Select(x => x.Trim()).ToList();
            string me = sh.P.Cr.User ?? "root";
            string user = Ask(sh, io, "Name (" + host + ":" + me + "): ", false);
            if (user == null) return 1;
            if (user.Trim().Length == 0) user = me;
            io.L("331 Please specify the password.");
            string pass = Ask(sh, io, "Password: ", true) ?? "";
            bool anon = user == "anonymous" || user == "ftp";
            PCtx ctx; string root, cwd;
            if (anon)
            {
                if (!anonOk) { io.L("530 Login incorrect.\nLogin failed."); net.Noise(sh.M, ip, "login-failed", 2, "ftp anonymous"); return 1; }
                var fpw = tm.User("ftp");
                ctx = new PCtx { Cr = fpw != null ? tm.CredFor("ftp") : new Cred { Uid = 14, Gid = 50, User = "ftp" }, Cwd = "/", Root = "/" };
                root = "/var/ftp"; cwd = "/";
                net.Noise(sh.M, ip, "ftp-anonymous", 1, "");
            }
            else
            {
                var pw = tm.User(user);
                if (pw == null || denied.Contains(user) || !tm.CheckPassword(user, pass)) { io.L("530 Login incorrect.\nLogin failed."); net.Noise(sh.M, ip, "login-failed", 2, "ftp as " + user); return 1; }
                ctx = new PCtx { Cr = tm.CredFor(user), Cwd = pw.Home, Root = "/" };
                root = ""; cwd = pw.Home;
                net.LoggedIn(sh.M, ip, user, false, "ftp", pass, null);
            }
            io.L("230 Login successful.\nRemote system type is UNIX.\nUsing binary mode to transfer files.");
            Func<string, string> rpath = p =>
            {
                string j = Vfs.Norm(Vfs.Join(cwd, p ?? "."));
                return root.Length > 0 ? root + (j == "/" ? "" : j) : j;
            };
            for (;;)
            {
                string line;
                try { line = Ask(sh, io, "ftp> ", false); }
                catch (BreakSignal) { io.P("\n"); continue; }
                if (line == null) { io.L("221 Goodbye."); return 0; }
                var w = Words(line);
                if (w.Count == 0) continue;
                string c = w[0].ToLowerInvariant();
                if (c == "bye" || c == "quit" || c == "exit") { io.L("221 Goodbye."); return 0; }
                if (c == "binary" || c == "ascii" || c == "passive") { io.L(c == "passive" ? "Passive mode on." : "200 Switching to " + (c == "binary" ? "Binary" : "ASCII") + " mode."); continue; }
                if (c == "help" || c == "?") { io.L("Commands: ls  cd  pwd  get  bye"); continue; }
                if (c == "pwd") { io.L("257 \"" + cwd + "\" is the current directory"); continue; }
                if (c == "cd")
                {
                    string target = w.Count > 1 ? w[1] : "/";
                    try { var l = tm.V.Resolve(ctx, rpath(target), true); if (!l.Exists || l.Node.T != 'd' || !tm.V.Can(ctx.Cr, l.Node, 1)) throw new VfsEx("ENOENT"); cwd = Vfs.Norm(Vfs.Join(cwd, target)); io.L("250 Directory successfully changed."); }
                    catch (VfsEx) { io.L("550 Failed to change directory."); }
                    continue;
                }
                if (c == "ls" || c == "dir")
                {
                    string target = w.Count > 1 && !w[1].StartsWith("-", StringComparison.Ordinal) ? w[1] : ".";
                    bool all = w.Any(x => x == "-a" || x == "-la" || x == "-al");
                    io.L("227 Entering Passive Mode (" + ip.Replace('.', ',') + ",156,64).\n150 Here comes the directory listing.");
                    try
                    {
                        var l = tm.V.Resolve(ctx, rpath(target), true);
                        if (l.Exists && l.Node.T == 'd' && tm.V.Can(ctx.Cr, l.Node, 4))
                            foreach (var k in l.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal))
                            {
                                if (k.StartsWith(".", StringComparison.Ordinal) && !all) continue;
                                var n = l.Fs.Node(l.Node.Kids[k]);
                                long size = n.T == 'd' ? 4096 : (n.Text != null ? Encoding.UTF8.GetByteCount(n.Text) : Math.Max(0, n.BinSize));
                                io.L(Vfs.ModeString(n) + "    " + n.Nlink + " " + n.Uid.ToString(CultureInfo.InvariantCulture).PadRight(9) + n.Gid.ToString(CultureInfo.InvariantCulture).PadRight(9) + size.ToString(CultureInfo.InvariantCulture).PadLeft(12) + " " + tm.LocalTime(n.Mtime).ToString("MMM dd HH:mm", CultureInfo.InvariantCulture) + " " + k);
                            }
                    }
                    catch (VfsEx) { }
                    io.L("226 Directory send OK.");
                    continue;
                }
                if (c == "get" || c == "recv")
                {
                    if (w.Count < 2) { io.L("(remote-file) usage: get remote-file [local-file]"); continue; }
                    string local = w.Count > 2 ? w[2] : Vfs.Base(w[1]);
                    string text;
                    try { text = tm.V.ReadText(ctx, rpath(w[1])); }
                    catch (VfsEx) { io.L("227 Entering Passive Mode (" + ip.Replace('.', ',') + ",156,64).\n550 Failed to open file."); continue; }
                    try { sh.M.V.WriteText(sh.P, local, text, false); }
                    catch (VfsEx e) { io.L("local: " + local + ": " + VfsEx.Msg(e.Errno)); continue; }
                    long size = Encoding.UTF8.GetByteCount(text);
                    io.L("local: " + local + " remote: " + w[1] + "\n227 Entering Passive Mode (" + ip.Replace('.', ',') + ",156,64).\n150 Opening BINARY mode data connection for " + w[1] + " (" + size + " bytes).\n226 Transfer complete.\n" + size + " bytes received in 0.000312 secs (" + (size / 312.0).ToString("0.0", CultureInfo.InvariantCulture) + " kbytes/sec)");
                    net.Noise(sh.M, ip, "ftp-read", 1, rpath(w[1]));
                    continue;
                }
                if (c == "put" || c == "mkdir" || c == "delete") { io.L("550 Permission denied."); continue; }
                io.L("?Invalid command.");
            }
        }

        // ------------------------------------------------------------ mysql (a small pretend database client)
        static int Mysql(Sh sh, List<string> a, Io io)
        {
            var net = sh.M.Net == null ? null : sh.M.Net.Range;   // also the RHCSA machines' mariadb client (no noise log there)
            string host = "localhost", user = sh.P.Cr.User ?? "root", pass = null, db = null, exec = null; bool askPass = false;
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-h" && i + 1 < a.Count) host = a[++i];
                else if (s.StartsWith("-h", StringComparison.Ordinal) && s.Length > 2) host = s.Substring(2);
                else if (s.StartsWith("--host=", StringComparison.Ordinal)) host = s.Substring(7);
                else if (s == "-u" && i + 1 < a.Count) user = a[++i];
                else if (s.StartsWith("-u", StringComparison.Ordinal) && s.Length > 2) user = s.Substring(2);
                else if (s.StartsWith("--user=", StringComparison.Ordinal)) user = s.Substring(7);
                else if (s == "-p") askPass = true;
                else if (s.StartsWith("-p", StringComparison.Ordinal)) pass = s.Substring(2);
                else if (s.StartsWith("--password=", StringComparison.Ordinal)) pass = s.Substring(11);
                else if (s == "-e" && i + 1 < a.Count) exec = a[++i];
                else if (s.StartsWith("--execute=", StringComparison.Ordinal)) exec = s.Substring(10);
                else if (s.StartsWith("-", StringComparison.Ordinal)) { }
                else db = s;
            }
            object tg; string ip;
            string err = sh.M.Connect(host, 3306, out tg, out ip);
            var tm = tg as Machine;
            if (err != null || tm == null) { io.E("ERROR 2002 (HY000): Can't connect to server on '" + host + "' (" + (err == "timeout" ? "110" : err == "noroute" ? "113" : "111") + ")"); return 1; }
            if (askPass) pass = Ask(sh, io, "Enter password: ", true) ?? "";
            string from = sh.M.FirstV4() ?? "localhost";
            var users = (tm.V.Cat("/var/lib/mysql/range-users.txt") ?? "").Split('\n').Select(x => x.Trim().Split(':')).Where(x => x.Length >= 3).ToList();
            var u = users.FirstOrDefault(x => x[0] == user);
            if (u == null || u[1] != (pass ?? ""))
            {
                if (net != null) net.Noise(sh.M, ip, "login-failed", 2, "database as " + user);
                io.E("ERROR 1045 (28000): Access denied for user '" + user + "'@'" + from + "' (using password: " + (string.IsNullOrEmpty(pass) ? "NO" : "YES") + ")");
                return 1;
            }
            if (net != null) net.LoggedIn(sh.M, ip, user, false, "database", pass, null);
            var allowed = u[2].Split(',').Select(x => x.Trim()).ToList();
            Func<string, bool> may = d => allowed.Contains("*") || allowed.Contains(d);
            Func<List<string>> dbs = () => { var l = new List<string>(); try { var dl = tm.V.G("/var/lib/mysql"); if (dl.Exists) foreach (var k in dl.Node.Kids.Keys) if (dl.Fs.Node(dl.Node.Kids[k]).T == 'd' && may(k)) l.Add(k); } catch (VfsEx) { } l.Sort(StringComparer.Ordinal); return l; };
            if (db != null && !may(db)) { io.E("ERROR 1044 (42000): Access denied for user '" + user + "'@'" + from + "' to database '" + db + "'"); return 1; }
            Func<string, int> query = null;
            query = q =>
            {
                string t = q.Trim().TrimEnd(';').Trim();
                if (t.Length == 0) return 0;
                Match mm;
                if (Regex.IsMatch(t, "^show\\s+databases$", RegexOptions.IgnoreCase)) { var rows = new List<string[]> { new[] { "information_schema" } }; rows.AddRange(dbs().Select(x => new[] { x })); Table(io, new[] { "Database" }, rows); return 0; }
                if ((mm = Regex.Match(t, "^use\\s+`?(\\w+)`?$", RegexOptions.IgnoreCase)).Success)
                {
                    if (!dbs().Contains(mm.Groups[1].Value)) { io.L("ERROR 1049 (42000): Unknown database '" + mm.Groups[1].Value + "'"); return 1; }
                    db = mm.Groups[1].Value; io.L("Database changed"); return 0;
                }
                if (Regex.IsMatch(t, "^select\\s+database\\(\\)$", RegexOptions.IgnoreCase)) { Table(io, new[] { "database()" }, new List<string[]> { new[] { db ?? "NULL" } }); return 0; }
                if (Regex.IsMatch(t, "^select\\s+(user|current_user)\\(\\)$", RegexOptions.IgnoreCase)) { Table(io, new[] { "user()" }, new List<string[]> { new[] { user + "@" + from } }); return 0; }
                if (db == null && Regex.IsMatch(t, "^(show\\s+tables|select|describe|desc)\\b", RegexOptions.IgnoreCase)) { io.L("ERROR 1046 (3D000): No database selected"); return 1; }
                Func<string, List<string[]>> load = tbl =>
                {
                    string text = tm.V.Cat("/var/lib/mysql/" + db + "/" + tbl + ".tsv");
                    if (text == null) return null;
                    return text.Replace("\r", "").Split('\n').Where(x => x.Length > 0).Select(x => x.Split('\t')).ToList();
                };
                if (Regex.IsMatch(t, "^show\\s+tables$", RegexOptions.IgnoreCase))
                {
                    var rows = new List<string[]>();
                    try { var dl = tm.V.G("/var/lib/mysql/" + db); foreach (var k in dl.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal)) if (k.EndsWith(".tsv", StringComparison.Ordinal)) rows.Add(new[] { k.Substring(0, k.Length - 4) }); } catch (VfsEx) { }
                    Table(io, new[] { "Tables_in_" + db }, rows); return 0;
                }
                if ((mm = Regex.Match(t, "^(?:describe|desc)\\s+`?(\\w+)`?$", RegexOptions.IgnoreCase)).Success)
                {
                    var tb = load(mm.Groups[1].Value);
                    if (tb == null) { io.L("ERROR 1146 (42S02): Table '" + db + "." + mm.Groups[1].Value + "' doesn't exist"); return 1; }
                    Table(io, new[] { "Field", "Type", "Null", "Key", "Default", "Extra" }, tb[0].Select((c, i) => new[] { c, i == 0 && c == "id" ? "int(11)" : "varchar(255)", i == 0 ? "NO" : "YES", i == 0 && c == "id" ? "PRI" : "", "NULL", "" }).ToList());
                    return 0;
                }
                if ((mm = Regex.Match(t, "^select\\s+(.+?)\\s+from\\s+`?(\\w+)`?(?:\\s+where\\s+(\\w+)\\s*=\\s*'([^']*)')?(?:\\s+limit\\s+(\\d+))?$", RegexOptions.IgnoreCase)).Success)
                {
                    var tb = load(mm.Groups[2].Value);
                    if (tb == null) { io.L("ERROR 1146 (42S02): Table '" + db + "." + mm.Groups[2].Value + "' doesn't exist"); return 1; }
                    var head = tb[0];
                    var cols = mm.Groups[1].Value.Trim() == "*" ? Enumerable.Range(0, head.Length).ToList() : mm.Groups[1].Value.Split(',').Select(x => Array.IndexOf(head, x.Trim().Trim('`'))).ToList();
                    int bad = cols.IndexOf(-1);
                    if (bad >= 0) { io.L("ERROR 1054 (42S22): Unknown column '" + mm.Groups[1].Value.Split(',')[bad].Trim() + "' in 'field list'"); return 1; }
                    var rows = tb.Skip(1).ToList();
                    if (mm.Groups[3].Success)
                    {
                        int wc = Array.IndexOf(head, mm.Groups[3].Value);
                        if (wc < 0) { io.L("ERROR 1054 (42S22): Unknown column '" + mm.Groups[3].Value + "' in 'where clause'"); return 1; }
                        rows = rows.Where(r => wc < r.Length && r[wc] == mm.Groups[4].Value).ToList();
                    }
                    if (mm.Groups[5].Success) rows = rows.Take(int.Parse(mm.Groups[5].Value, CultureInfo.InvariantCulture)).ToList();
                    if (rows.Count == 0) { io.L("Empty set (0.001 sec)"); return 0; }
                    Table(io, cols.Select(c => head[c]).ToArray(), rows.Select(r => cols.Select(c => c < r.Length ? r[c] : "NULL").ToArray()).ToList());
                    if (net != null) net.Noise(sh.M, ip, "database-read", 1, db + "." + mm.Groups[2].Value);
                    return 0;
                }
                io.L("ERROR 1064 (42000): You have an error in your SQL syntax (Cyber Range's pretend database understands SHOW DATABASES, USE, SHOW TABLES, DESCRIBE and SELECT ... FROM ... [WHERE column = 'value'] [LIMIT n])");
                return 1;
            };
            if (exec != null) { int st = 0; foreach (var q in exec.Split(';')) if (query(q) != 0) st = 1; return st; }
            io.L("Welcome to the MariaDB monitor.  Commands end with ; or \\g.\nServer version: 10.5.22-MariaDB MariaDB Server (Cyber Range)\n\nType 'help;' or '\\h' for help.\n");
            var buf = new StringBuilder();
            for (;;)
            {
                string line;
                try { line = Ask(sh, io, buf.Length == 0 ? "MariaDB [" + (db ?? "(none)") + "]> " : "    -> ", false); }
                catch (BreakSignal) { buf.Clear(); io.P("^C\n"); continue; }
                if (line == null) { io.L("Bye"); return 0; }
                string tt = line.Trim();
                if (buf.Length == 0 && (tt == "exit" || tt == "quit" || tt == "\\q" || tt == "exit;" || tt == "quit;")) { io.L("Bye"); return 0; }
                if (buf.Length == 0 && (tt == "help;" || tt == "help" || tt == "\\h")) { io.L("Try: SHOW DATABASES;  USE name;  SHOW TABLES;  DESCRIBE table;  SELECT * FROM table;  exit"); continue; }
                buf.Append(line).Append(' ');
                if (!tt.EndsWith(";", StringComparison.Ordinal) && !tt.EndsWith("\\g", StringComparison.Ordinal)) continue;
                string all = buf.ToString().Replace("\\g", ";"); buf.Clear();
                foreach (var q in all.Split(';')) if (q.Trim().Length > 0) query(q);
            }
        }

        static void Table(Io io, string[] head, List<string[]> rows)
        {
            var w = head.Select(h => h.Length).ToArray();
            foreach (var r in rows) for (int i = 0; i < w.Length && i < r.Length; i++) w[i] = Math.Max(w[i], r[i].Length);
            string line = "+" + string.Join("+", w.Select(x => new string('-', x + 2)).ToArray()) + "+";
            io.L(line);
            io.L("| " + string.Join(" | ", head.Select((h, i) => h.PadRight(w[i])).ToArray()) + " |");
            io.L(line);
            foreach (var r in rows) io.L("| " + string.Join(" | ", w.Select((x, i) => (i < r.Length ? r[i] : "").PadRight(x)).ToArray()) + " |");
            io.L(line);
            io.L(rows.Count + " row" + (rows.Count == 1 ? "" : "s") + " in set (0.001 sec)\n");
        }

        // ------------------------------------------------------------ hashcrack
        class Crack { public string Who = "", Hash = "", Kind = "", Found; }

        static string KindOf(string h, string format)
        {
            if (h.StartsWith("$6$", StringComparison.Ordinal)) return "sha512crypt-sim";
            if (h.StartsWith("$rangetgs$", StringComparison.Ordinal)) return "rangetgs";
            if (!Regex.IsMatch(h, "^[0-9a-fA-F]+$")) return null;
            if (h.Length == 32) return format == "nt" ? "nt-sim" : "md5";
            if (h.Length == 40) return "sha1";
            if (h.Length == 64) return "sha256";
            return null;
        }
        static bool Tries(Crack c, string w)
        {
            switch (c.Kind)
            {
                case "sha512crypt-sim": { var p = c.Hash.Split('$'); return p.Length >= 4 && Machine.HashPassword(p[2], w) == c.Hash; }
                case "rangetgs": { var p = c.Hash.Split('$'); return p.Length >= 4 && RangeHash.Ticket(p[2], w) == c.Hash; }
                case "nt-sim": return RangeHash.Nt(w) == c.Hash.ToLowerInvariant();
                case "md5": return RangeHash.Md5(w) == c.Hash.ToLowerInvariant();
                case "sha1": return RangeHash.Sha1(w) == c.Hash.ToLowerInvariant();
                case "sha256": return RangeHash.Sha256(w) == c.Hash.ToLowerInvariant();
            }
            return false;
        }

        static int HashCrack(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "w:f:", "wordlist=|w,format=|f,show", "hashcrack");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Args.Count == 0) { io.E("Usage: hashcrack [--format=md5|sha1|sha256|nt] [--wordlist=FILE] HASHFILE\n       hashcrack --show HASHFILE"); return 1; }
            string format = (o.Get("f") ?? "").ToLowerInvariant();
            if (format.Length > 0 && !new[] { "md5", "sha1", "sha256", "nt", "sha512crypt", "rangetgs" }.Contains(format)) { io.E("hashcrack: unknown format " + format + " (md5, sha1, sha256, nt, sha512crypt, rangetgs)"); return 1; }
            string text;
            try { text = sh.M.V.ReadText(sh.P, o.Args[0]); } catch (VfsEx e) { io.E("hashcrack: " + o.Args[0] + ": " + VfsEx.Msg(e.Errno)); return 1; }
            var list = new List<Crack>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var l = raw.Trim(); if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                var f = l.Split(':');
                Crack c = null;
                if (f.Length >= 4 && Regex.IsMatch(f[1], "^\\d+$") && f[3].Length == 32) c = new Crack { Who = f[0], Hash = f[3], Kind = "nt-sim" };   // user:rid:lm:nt:::
                else if (f.Length >= 2 && KindOf(f[1], format) != null) c = new Crack { Who = f[0], Hash = f[1], Kind = KindOf(f[1], format) };
                else if (KindOf(l, format) != null) c = new Crack { Who = "?", Hash = l, Kind = KindOf(l, format) };
                if (c != null) { if (c.Kind == "md5" && format == "nt") c.Kind = "nt-sim"; list.Add(c); }
            }
            if (list.Count == 0) { io.L("No password hashes loaded (see the manual page for the formats: man hashcrack)"); return 1; }
            string home = Home(sh);
            string pot = home.TrimEnd('/') + "/.hashcrack.pot";
            var known = new Dictionary<string, string>();
            try { foreach (var l in sh.M.V.ReadText(sh.P, pot).Split('\n')) { int i = l.LastIndexOf(':'); if (i > 0) known[l.Substring(0, i)] = l.Substring(i + 1); } } catch (VfsEx) { }
            if (o.Has("show"))
            {
                int n = 0;
                foreach (var c in list) { string v; if (known.TryGetValue(c.Hash, out v)) { io.L(c.Who + ":" + v); n++; } }
                io.L("\n" + n + " password hash" + (n == 1 ? "" : "es") + " cracked, " + (list.Count - n) + " left");
                return 0;
            }
            string wl = o.Get("w") ?? "/usr/share/wordlists/range-common.txt";
            string words;
            try { words = sh.M.V.ReadText(sh.P, wl); } catch (VfsEx e) { io.E("hashcrack: " + wl + ": " + VfsEx.Msg(e.Errno)); return 1; }
            var wlist = words.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            io.L("Using default input encoding: UTF-8");
            io.L("Loaded " + list.Count + " password hash" + (list.Count == 1 ? "" : "es") + " (" + string.Join(", ", list.Select(c => c.Kind).Distinct().ToArray()) + ")");
            io.L("Trying " + wlist.Count + " words from " + wl);
            int cracked = 0;
            var potAdd = new StringBuilder();
            foreach (var c in list)
            {
                string v;
                if (known.TryGetValue(c.Hash, out v)) { c.Found = v; cracked++; continue; }
                foreach (var w in wlist) if (Tries(c, w)) { c.Found = w; break; }
                if (c.Found != null) { cracked++; io.L(c.Found.PadRight(17) + "(" + c.Who + ")"); potAdd.Append(c.Hash + ":" + c.Found + "\n"); }
            }
            if (potAdd.Length > 0) { try { sh.M.V.WriteText(sh.P, pot, potAdd.ToString(), true); } catch (VfsEx) { } }
            io.L(cracked + " of " + list.Count + " hashes cracked." + (cracked > 0 ? " Use \"hashcrack --show " + o.Args[0] + "\" to see them again." : " Try another word list."));
            return 0;
        }

        // ------------------------------------------------------------ pwguess
        static int PwGuess(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "pwguess"); if (net == null) return 1;
            var o = Opts.Parse(a, "l:L:p:P:", "", "pwguess");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Args.Count == 0 || (!o.Has("l") && !o.Has("L")) || (!o.Has("p") && !o.Has("P"))) { io.E("Usage: pwguess (-l USER | -L USERFILE) (-p PASSWORD | -P WORDFILE) ssh://HOST | ftp://HOST"); return 1; }
            var mt = Regex.Match(o.Args[0], "^(ssh|ftp)://([^/:]+)/?$");
            var mf = Regex.Match(o.Args[0], "^http-form://([^/]+)(/.*)$");
            if (!mt.Success && !mf.Success) { io.E("pwguess: give the service and host as ssh://HOST, ftp://HOST or http-form://HOST/PATH"); return 1; }
            Func<string, List<string>> fileLines = f => sh.M.V.ReadText(sh.P, f).Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            List<string> users, pws;
            try
            {
                users = o.Has("l") ? new List<string> { o.Get("l") } : fileLines(o.Get("L"));
                pws = o.Has("p") ? new List<string> { o.Get("p") } : fileLines(o.Get("P"));
            }
            catch (VfsEx e) { io.E("pwguess: " + VfsEx.Msg(e.Errno)); return 1; }
            if (mf.Success) return PwGuessForm(sh, io, net, mf.Groups[1].Value, mf.Groups[2].Value, users, pws);
            string svc = mt.Groups[1].Value, host = mt.Groups[2].Value;
            int port = svc == "ssh" ? 22 : 21;
            object tg; string ip;
            string err = sh.M.Connect(host, port, out tg, out ip);
            var tm = tg as Machine;
            if (err != null || tm == null) { io.E("[ERROR] could not connect to " + svc + "://" + host + ":" + port + " (" + (err ?? "refused") + ")"); return 1; }
            int tries = Math.Min(users.Count * pws.Count, 5000);
            io.L("pwguess (Cyber Range, simulated) - only for systems you are allowed to test.");
            io.L("[DATA] " + pws.Count + " password" + (pws.Count == 1 ? "" : "s") + " x " + users.Count + " user" + (users.Count == 1 ? "" : "s") + " = " + tries + " tries on " + svc + "://" + host + ":" + port);
            bool pwAuth = svc == "ftp" || tm.SshdValue("PasswordAuthentication", "yes").ToLowerInvariant() == "yes";
            string permitRoot = tm.SshdValue("PermitRootLogin", "prohibit-password").ToLowerInvariant();
            var ftpDenied = (tm.V.Cat("/etc/vsftpd/ftpusers") ?? "root\n").Split('\n').Select(x => x.Trim()).ToList();
            int found = 0, n = 0;
            foreach (var u in users)
            {
                foreach (var p in pws)
                {
                    if (++n > 5000) break;
                    var pw = tm.User(u);
                    bool ok = pwAuth && pw != null && tm.CheckPassword(u, p) && (svc == "ftp" ? !ftpDenied.Contains(u) : (pw.Uid != 0 || permitRoot == "yes"));
                    if (!ok) { if (n <= 300 && svc == "ssh") tm.Log("sshd", 40000 + n, 6, "Failed password for " + (pw == null ? "invalid user " : "") + u + " from " + (sh.M.FirstV4() ?? "") + " port " + (40000 + n) + " ssh2", "sshd.service"); continue; }
                    found++;
                    io.L("[" + port + "][" + svc + "] host: " + host + "   login: " + u + "   password: " + p);
                    net.LoggedIn(sh.M, ip, u, false, svc + "-guess", p, "found by guessing");
                    break;
                }
            }
            net.Noise(sh.M, ip, "password-guessing", 5, n + " tries on " + svc);
            io.L(found + " valid password" + (found == 1 ? "" : "s") + " found");
            return found > 0 ? 0 : 1;
        }

        /// <summary>pwguess against a web sign-in form: posts user=...&amp;pass=... to the page for every pair; a page that
        /// answers 200-399 accepted the password, 401 / 403 refused it. Only a page that never slows down or locks the
        /// account lets this work (the "no rate limit" mistake).</summary>
        static int PwGuessForm(Sh sh, Io io, RangeNet net, string host, string path, List<string> users, List<string> pws)
        {
            string url = "http://" + host + path;
            io.L("pwguess (Cyber Range, simulated) - only for systems you are allowed to test.");
            int tries = Math.Min(users.Count * pws.Count, 5000);
            io.L("[DATA] " + pws.Count + " password" + (pws.Count == 1 ? "" : "s") + " x " + users.Count + " user" + (users.Count == 1 ? "" : "s") + " = " + tries + " tries on " + url);
            int found = 0, n = 0; string ip = null;
            foreach (var u in users)
            {
                foreach (var p in pws)
                {
                    if (++n > 5000) break;
                    HttpReply r;
                    PostData = "user=" + Uri.EscapeDataString(u) + "&pass=" + Uri.EscapeDataString(p);
                    try { r = Fetch(sh, url); } finally { PostData = null; }
                    if (r.Error != null) { io.E("[ERROR] " + r.Error); return 1; }
                    if (r.Code == 429) { io.L("[STOP] the page answered 429 Too Many Requests: it limits guessing"); return 1; }
                    if (r.Code >= 400) continue;
                    found++;
                    io.L("[http-form] host: " + host + "   login: " + u + "   password: " + p);
                    if (ip == null) ip = sh.M.Net.Resolve(sh.M, host) ?? host;
                    net.LoggedIn(sh.M, ip, u, false, "web-guess", p, "found by guessing");
                    break;
                }
            }
            net.Noise(sh.M, sh.M.Net.Resolve(sh.M, host) ?? host, "password-guessing", 5, n + " tries on " + url);
            io.L(found + " valid password" + (found == 1 ? "" : "s") + " found");
            return found > 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ webscan
        static int WebScan(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "webscan"); if (net == null) return 1;
            var o = Opts.Parse(a, "w:", "wordlist=|w", "webscan");
            if (o.Error != null || o.Args.Count == 0) { io.E(o.Error ?? "Usage: webscan [-w WORDLIST] http://HOST[:PORT]/[PATH/]"); return 1; }
            string baseUrl = o.Args[0]; if (!baseUrl.Contains("://")) baseUrl = "http://" + baseUrl; if (!baseUrl.EndsWith("/", StringComparison.Ordinal)) baseUrl += "/";
            string wl = o.Get("w") ?? "/usr/share/wordlists/range-paths.txt";
            List<string> words;
            try { words = sh.M.V.ReadText(sh.P, wl).Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).ToList(); }
            catch (VfsEx e) { io.E("webscan: " + wl + ": " + VfsEx.Msg(e.Errno)); return 1; }
            io.L("webscan (Cyber Range, simulated): trying " + words.Count + " names under " + baseUrl);
            int hits = 0;
            var first = Fetch(sh, baseUrl + words[0]);
            if (first.Error != null) { io.E("webscan: " + first.Error); return 1; }
            foreach (var w in words)
            {
                var r = Fetch(sh, baseUrl + w);
                if (r.Error != null || r.Code == 404) continue;
                hits++;
                io.L("/" + w + "".PadRight(Math.Max(1, 24 - w.Length)) + "(Status: " + r.Code + ") [Size: " + r.Length + "]" + (r.Location != null ? " [--> " + r.Location + "]" : ""));
            }
            var m0 = Regex.Match(baseUrl, "^\\w+://([^/:]+)");
            string ip = sh.M.Net.Resolve(sh.M, m0.Groups[1].Value) ?? m0.Groups[1].Value;
            net.Noise(sh.M, ip, "web-scan", 4, words.Count + " requests");
            io.L(hits + " found");
            return 0;
        }

        // ------------------------------------------------------------ vulnlookup
        static int VulnLookup(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "x:", "show=|x", "vulnlookup");
            if (o.Error != null) { io.E(o.Error); return 1; }
            var all = RangeData.Vulns;
            if (o.Has("x"))
            {
                int id; if (!int.TryParse(o.Get("x"), out id) || id < 1 || id > all.Count) { io.E("vulnlookup: no note number " + o.Get("x")); return 1; }
                var v = all[id - 1];
                io.L("Note " + id + ": " + v[2] + "\nProduct: " + v[0] + "   Versions: " + v[1] + "\n\n" + v[3]);
                return 0;
            }
            if (o.Args.Count == 0) { io.E("Usage: vulnlookup WORD...   (then vulnlookup -x NUMBER shows a note)"); return 1; }
            var terms = o.Args.Select(x => x.ToLowerInvariant()).ToList();
            var hits = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                string hay = (all[i][0] + " " + all[i][1] + " " + all[i][2]).ToLowerInvariant();
                if (terms.All(t => hay.Contains(t))) hits.Add(i);
            }
            io.L("Cyber Range's made-up vulnerability notes (for the pretend products of the range only)");
            io.L(new string('-', 78));
            if (hits.Count == 0) { io.L("No results"); return 1; }
            io.L(" #   Title" + new string(' ', 50) + "Product");
            foreach (var i in hits) io.L((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2) + "   " + all[i][2].PadRight(55) + all[i][0] + " " + all[i][1]);
            io.L(new string('-', 78) + "\nRead one: vulnlookup -x NUMBER");
            return 0;
        }

        // ------------------------------------------------------------ ticket-request
        static int TicketRequest(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "ticket-request"); if (net == null) return 1;
            var o = Opts.Parse(a, "d:u:p:", "domain=|d,user=|u,password=|p", "ticket-request");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Args.Count == 0 || !o.Has("u")) { io.E("Usage: ticket-request -d DOMAIN -u USER [-p PASSWORD] DOMAIN-CONTROLLER"); return 1; }
            RangeHost h; string ip;
            string err = net.ConnectHost(sh.M, o.Args[0], 88, out h, out ip);
            if (err != null) { io.E("ticket-request: could not reach the domain's ticket service on " + o.Args[0] + " port 88 (" + err + ")"); return 1; }
            if (net.DomainName.Length == 0) { io.E("ticket-request: " + o.Args[0] + " is not a domain controller"); return 1; }
            string dom = o.Get("d") ?? net.DomainName;
            if (!dom.Equals(net.DomainName, StringComparison.OrdinalIgnoreCase) && !dom.Equals(net.DomainNetbios, StringComparison.OrdinalIgnoreCase)) { io.E("ticket-request: the domain " + dom + " is not known here (KDC_ERR_WRONG_REALM)"); return 1; }
            string pass = o.Get("p") ?? Ask(sh, io, "Password for " + o.Get("u") + "@" + dom.ToUpperInvariant() + ": ", true) ?? "";
            var acct = net.DomainAccounts.FirstOrDefault(x => x.User.Equals(o.Get("u"), StringComparison.OrdinalIgnoreCase));
            if (acct == null || acct.Password != pass) { net.Noise(sh.M, ip, "login-failed", 2, "ticket request as " + o.Get("u")); io.E("ticket-request: the domain refused the account (KDC_ERR_PREAUTH_FAILED)"); return 1; }
            net.LoggedIn(sh.M, ip, acct.User, false, "domain", pass, null);
            var svc = net.DomainAccounts.Where(x => x.Spn.Length > 0).ToList();
            io.L("ticket-request (Cyber Range, simulated): service accounts in " + net.DomainName.ToUpperInvariant() + "\n");
            io.L("ServiceName".PadRight(34) + "Account");
            io.L(new string('-', 34) + new string('-', 16));
            foreach (var s in svc) io.L(s.Spn.PadRight(34) + s.User);
            io.L("");
            foreach (var s in svc) io.L(RangeHash.Ticket(s.User, s.Password));
            net.Noise(sh.M, ip, "ticket-request", 3, svc.Count + " service tickets");
            return 0;
        }
    }
}
