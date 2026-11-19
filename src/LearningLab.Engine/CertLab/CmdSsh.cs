// Cert Lab - ssh, scp, ssh-keygen, ssh-copy-id, ssh-keyscan, and the web clients curl and wget.
// Keys are made-up but behave like OpenSSH's: a private key file holds its public half, sshd checks
// authorized_keys with the same ownership / mode / SELinux rules, host keys go into known_hosts.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class SshKey
    {
        public string Type = "rsa";      // rsa, ecdsa, ed25519
        public string Blob = "";         // the base64 public part
        public string Comment = "";
        public string Pass = "";         // passphrase hash ("" = none)
        public int Bits = 3072;
        public string AlgName { get { return Type == "rsa" ? "ssh-rsa" : Type == "ecdsa" ? "ecdsa-sha2-nistp256" : "ssh-ed25519"; } }
        public string PubLine { get { return AlgName + " " + Blob + (Comment.Length > 0 ? " " + Comment : ""); } }
        public string Fingerprint { get { return Cmd.Fingerprint(Blob); } }
        public string Upper { get { return Type == "rsa" ? "RSA" : Type == "ecdsa" ? "ECDSA" : "ED25519"; } }
    }

    public class SshLogin
    {
        public Machine M; public PwEnt Pw; public string Ip; public string Host; public bool Utility;
    }

    public static partial class Cmd
    {
        static void RegSsh()
        {
            P("sshd", Sshd); P("ssh", Ssh); P("scp", Scp); P("ssh-keygen", SshKeygen); P("ssh-copy-id", SshCopyId); P("ssh-keyscan", SshKeyscan);
            P("sftp", (sh, a, io) => { io.E("Cert Lab's sftp is not included; use scp to copy files (scp file user@host:/path)."); return 1; });
            P("ssh-agent", (sh, a, io) => { io.L("SSH_AUTH_SOCK=/tmp/ssh-XXXXcertlab/agent." + sh.Pid + "; export SSH_AUTH_SOCK;\nSSH_AGENT_PID=" + (sh.Pid + 1) + "; export SSH_AGENT_PID;\necho Agent pid " + (sh.Pid + 1) + ";"); return 0; });
            P("ssh-add", (sh, a, io) => { io.E("Could not open a connection to your authentication agent. (Cert Lab has no ssh-agent; ssh asks for a key's passphrase each time.)"); return 2; });
        }

        /// <summary>sshd -t (check the configuration) and sshd -T (print the settings in effect); the daemon itself runs as sshd.service.</summary>
        static int Sshd(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("sshd re-exec requires execution with an absolute path"); return 255; }
            string err;
            var cfg = m.SshdConfig(out err);
            if (err != null) { io.E(err + "\n/etc/ssh/sshd_config: terminating, 1 bad configuration options"); return 255; }
            if (a.Contains("-t")) return 0;
            if (a.Contains("-T"))
            {
                var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "port", "22" }, { "addressfamily", "any" }, { "listenaddress", "[::]:22" }, { "permitrootlogin", "prohibit-password" }, { "pubkeyauthentication", "yes" },
                    { "passwordauthentication", "yes" }, { "permitemptypasswords", "no" }, { "kbdinteractiveauthentication", "yes" }, { "usepam", "no" }, { "x11forwarding", "no" },
                    { "printmotd", "yes" }, { "authorizedkeysfile", ".ssh/authorized_keys .ssh/authorized_keys2" }, { "maxauthtries", "6" }, { "maxsessions", "10" },
                    { "logingracetime", "120" }, { "strictmodes", "yes" }, { "syslogfacility", "AUTH" }, { "loglevel", "INFO" }, { "clientaliveinterval", "0" }, { "clientalivecountmax", "3" },
                    { "gssapiauthentication", "no" }, { "subsystem", "sftp /usr/libexec/openssh/sftp-server" },
                };
                foreach (var kv in cfg) defaults[kv.Key] = kv.Value;
                foreach (var kv in defaults.OrderBy(x => x.Key, StringComparer.Ordinal)) io.L(kv.Key.ToLowerInvariant() + " " + kv.Value);
                return 0;
            }
            io.E("(Cert Lab: the SSH server runs as a service: systemctl status sshd. sshd -t checks the configuration, sshd -T shows the settings in effect.)");
            return 1;
        }

        // ------------------------------------------------------------ keys
        public static string Fingerprint(string blob)
        {
            using (var h = SHA256.Create()) return "SHA256:" + Convert.ToBase64String(h.ComputeHash(Encoding.ASCII.GetBytes(blob))).TrimEnd('=');
        }
        static string Prefix(string type)
        {
            return type == "rsa" ? "AAAAB3NzaC1yc2EAAAADAQABAAABgQ" : type == "ecdsa" ? "AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBB" : "AAAAC3NzaC1lZDI1NTE5AAAAI";
        }
        static string MakeBlob(string type, string seed)
        {
            var sb = new StringBuilder(Prefix(type));
            int len = type == "rsa" ? 300 : type == "ecdsa" ? 88 : 43;
            using (var h = SHA256.Create())
            {
                int i = 0;
                while (sb.Length < Prefix(type).Length + len)
                {
                    var b = h.ComputeHash(Encoding.UTF8.GetBytes(seed + "/" + i++));
                    sb.Append(Convert.ToBase64String(b).TrimEnd('=').Replace("+", "x").Replace("/", "Q"));
                }
            }
            return sb.ToString().Substring(0, Prefix(type).Length + len) + (type == "rsa" ? "==" : "");
        }
        static string PassHash(string p) { if (string.IsNullOrEmpty(p)) return ""; using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes("certlab-pass:" + p))); }

        static string PrivateText(SshKey k)
        {
            string body = "openssh-key-v1\0" + (k.Pass.Length > 0 ? "aes256-ctr\0bcrypt\0" + k.Pass : "none\0none\0") + "\0" + k.Type + "\0" + k.Bits + "\0" + k.Blob + "\0" + k.Comment;
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
            var sb = new StringBuilder("-----BEGIN OPENSSH PRIVATE KEY-----\n");
            for (int i = 0; i < b64.Length; i += 70) sb.Append(b64.Substring(i, Math.Min(70, b64.Length - i))).Append('\n');
            sb.Append("-----END OPENSSH PRIVATE KEY-----\n");
            return sb.ToString();
        }
        public static SshKey ParsePrivate(string text)
        {
            if (text == null || !text.Contains("BEGIN OPENSSH PRIVATE KEY")) return null;
            try
            {
                var b64 = string.Join("", text.Split('\n').Where(l => !l.StartsWith("-----", StringComparison.Ordinal)).Select(l => l.Trim()).ToArray());
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('\0');
                if (parts.Length < 8 || parts[0] != "openssh-key-v1") return null;
                var k = new SshKey { Pass = parts[1] == "none" ? "" : parts[3], Type = parts[4], Blob = parts[6], Comment = parts[7] };
                int.TryParse(parts[5], out k.Bits);
                return k;
            }
            catch (FormatException) { return null; }
        }
        public static SshKey ParsePublic(string line)
        {
            var p = (line ?? "").Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            // authorized_keys lines may start with options (from="...", command="...")
            while (i < p.Length && !p[i].StartsWith("ssh-", StringComparison.Ordinal) && !p[i].StartsWith("ecdsa-", StringComparison.Ordinal)) i++;
            if (i + 1 >= p.Length) return null;
            string ty = p[i] == "ssh-rsa" ? "rsa" : p[i].StartsWith("ecdsa", StringComparison.Ordinal) ? "ecdsa" : p[i] == "ssh-ed25519" ? "ed25519" : null;
            if (ty == null) return null;
            return new SshKey { Type = ty, Blob = p[i + 1], Comment = i + 2 < p.Length ? string.Join(" ", p.Skip(i + 2).ToArray()) : "" };
        }
        static SshKey HostKey(Machine m, string type)
        {
            return new SshKey { Type = type, Blob = MakeBlob(type, "host:" + m.Name + ":" + m.Seed + ":" + type), Bits = type == "rsa" ? 3072 : 256 };
        }
        static SshKey UtilityKey() { return new SshKey { Type = "ed25519", Blob = MakeBlob("ed25519", "host:utility"), Bits = 256 }; }

        static string RandomArt(SshKey k)
        {
            string head = "+---[" + k.Upper + " " + k.Bits + "]";
            head = head.PadRight(18, '-') + "+";
            var sb = new StringBuilder(head + "\n");
            const string chars = " .o+=*BOX@%&#/^";
            var h = new SHA256Managed().ComputeHash(Encoding.ASCII.GetBytes(k.Blob));
            var grid = new int[9, 17];
            int x = 8, y = 4;
            foreach (var b in h)
                for (int s = 0; s < 8; s += 2)
                {
                    int d = (b >> s) & 3;
                    x = Math.Max(0, Math.Min(16, x + ((d & 1) != 0 ? 1 : -1)));
                    y = Math.Max(0, Math.Min(8, y + ((d & 2) != 0 ? 1 : -1)));
                    if (grid[y, x] < chars.Length - 1) grid[y, x]++;
                }
            for (int r = 0; r < 9; r++)
            {
                sb.Append('|');
                for (int c = 0; c < 17; c++) sb.Append(r == 4 && c == 8 ? 'S' : (r == y && c == x ? 'E' : chars[grid[r, c]]));
                sb.Append("|\n");
            }
            sb.Append("+----[SHA256]-----+");
            return sb.ToString();
        }

        static PCtx UserCtx(Machine m, PwEnt pw)
        {
            return new PCtx { Cr = m.CredFor(pw.Name), Cwd = pw.Home, Root = "/" };
        }

        static string Home(Sh sh)
        {
            var pw = sh.M.UserById(sh.P.Cr.Uid);
            return pw != null ? pw.Home : (sh.GetVar("HOME") ?? "/");
        }

        // ------------------------------------------------------------ ssh-keygen
        static int SshKeygen(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "t:b:f:N:C:qR:lyEp:P:Hm:", "", "ssh-keygen");
            if (o.Error != null) { io.E(o.Error.Replace("Try 'ssh-keygen --help' for more information.", "usage: ssh-keygen [-q] [-a rounds] [-b bits] [-C comment] [-f output_keyfile]\n                  [-m format] [-N new_passphrase] [-O option]\n                  [-t dsa | ecdsa | ecdsa-sk | ed25519 | ed25519-sk | rsa]")); return 1; }
            var m = sh.M;
            string home = Home(sh);
            if (o.Has("R"))
            {
                string kh = o.Get("f") ?? home + "/.ssh/known_hosts";
                string text;
                try { text = m.V.ReadText(sh.P, kh); } catch (VfsEx e) { io.E("do_known_hosts: " + kh + ": " + VfsEx.Msg(e.Errno)); return 255; }
                string host = o.Get("R");
                var keep = text.Split('\n').Where(l => l.Length > 0 && !l.Split(' ')[0].Split(',').Contains(host)).ToList();
                int removed = text.Split('\n').Count(l => l.Length > 0) - keep.Count;
                if (removed > 0)
                {
                    io.L("# Host " + host + " found: line 1\n" + kh + " updated.\nOriginal contents retained as " + kh + ".old");
                    m.V.WriteText(sh.P, kh + ".old", text, false);
                    m.V.WriteText(sh.P, kh, string.Join("\n", keep.ToArray()) + (keep.Count > 0 ? "\n" : ""), false);
                }
                return 0;
            }
            if (o.Has("l") || o.Has("y"))
            {
                string f = o.Get("f");
                if (f == null && sh.S != null) f = sh.S.ReadLine("Enter file in which the key is (" + home + "/.ssh/id_rsa): ", false);
                if (string.IsNullOrEmpty(f)) f = home + "/.ssh/id_rsa";
                string text;
                try { text = m.V.ReadText(sh.P, f); } catch (VfsEx e) { io.E(f + ": " + VfsEx.Msg(e.Errno)); return 255; }
                var k = ParsePrivate(text) ?? ParsePublic(text.Split('\n')[0]);
                if (k == null) { io.E(f + " is not a key file."); return 255; }
                if (o.Has("y")) { io.L(k.AlgName + " " + k.Blob + " " + k.Comment); return 0; }
                io.L((k.Type == "rsa" ? "3072" : "256") + " " + k.Fingerprint + " " + (k.Comment.Length > 0 ? k.Comment : "no comment") + " (" + k.Upper + ")");
                return 0;
            }
            string type = (o.Get("t") ?? "rsa").ToLowerInvariant();
            if (type == "dsa") { io.E("unknown key type dsa (DSA keys are no longer supported in RHEL 9)"); return 1; }
            if (type != "rsa" && type != "ecdsa" && type != "ed25519") { io.E("unknown key type " + type); return 1; }
            string def = home + "/.ssh/id_" + type;
            bool quiet = o.Has("q");
            if (o.Has("p"))
            {
                string f = o.Get("f") ?? def;
                string text;
                try { text = m.V.ReadText(sh.P, f); } catch (VfsEx e) { io.E(f + ": " + VfsEx.Msg(e.Errno)); return 255; }
                var k = ParsePrivate(text);
                if (k == null) { io.E("Failed to load key " + f + ": invalid format"); return 255; }
                if (k.Pass.Length > 0)
                {
                    string old = o.Get("P") ?? (sh.S != null ? sh.S.ReadLine("Enter old passphrase: ", true) : "");
                    if (PassHash(old) != k.Pass) { io.E("Failed to load key " + f + ": incorrect passphrase supplied to decrypt private key"); return 255; }
                }
                string np = o.Get("N");
                if (np == null && sh.S != null) { np = sh.S.ReadLine("Enter new passphrase (empty for no passphrase): ", true); var again = sh.S.ReadLine("Enter same passphrase again: ", true); if (np != again) { io.E("Pass phrases do not match.  Try again."); return 1; } }
                k.Pass = PassHash(np ?? "");
                m.V.WriteText(sh.P, f, PrivateText(k), false);
                io.L("Your identification has been saved with the new passphrase.");
                return 0;
            }
            if (!quiet) io.L("Generating public/private " + type + " key pair.");
            string file = o.Get("f");
            if (file == null)
            {
                if (sh.S == null) file = def;
                else { file = sh.S.ReadLine("Enter file in which to save the key (" + def + "): ", false); if (string.IsNullOrEmpty(file)) file = def; }
            }
            if (file.StartsWith("~/", StringComparison.Ordinal)) file = home + file.Substring(1);
            // ~/.ssh is made when it is missing (the default place only)
            string dir = Vfs.Parent(Vfs.Norm(Vfs.Join(sh.P.Cwd, file)));
            try
            {
                var dl = m.V.Resolve(sh.P, dir, true);
                if (!dl.Exists)
                {
                    if (dir != home + "/.ssh") { io.E("Saving key \"" + file + "\" failed: No such file or directory"); return 1; }
                    var n = m.V.Mkdir(sh.P, dir, 0x1C0, false); n.Mode = (n.Mode & ~0xFFF) | 0x1C0;
                    if (!quiet) io.L("Created directory '" + dir + "'.");
                }
                var fl = m.V.Resolve(sh.P, file, true);
                if (fl.Exists)
                {
                    io.P(file + " already exists.\n");
                    string yn = sh.S != null ? sh.S.ReadLine("Overwrite (y/n)? ", false) : "n";
                    if (yn == null || !yn.Trim().StartsWith("y", StringComparison.Ordinal)) return 1;
                }
            }
            catch (VfsEx e) { io.E("Saving key \"" + file + "\" failed: " + VfsEx.Msg(e.Errno)); return 1; }
            string pass = o.Get("N");
            if (pass == null)
            {
                if (sh.S == null) pass = "";
                else
                {
                    for (;;)
                    {
                        pass = sh.S.ReadLine("Enter passphrase (empty for no passphrase): ", true) ?? "";
                        string again = sh.S.ReadLine("Enter same passphrase again: ", true) ?? "";
                        if (pass == again) break;
                        io.L("Passphrases do not match.  Try again.");
                    }
                }
            }
            int bits = type == "rsa" ? 3072 : type == "ecdsa" ? 256 : 256;
            if (o.Has("b")) int.TryParse(o.Get("b"), out bits);
            if (type == "rsa" && bits < 1024) { io.E("Invalid RSA key length: minimum is 1024 bits"); return 1; }
            var key = new SshKey { Type = type, Bits = bits, Comment = o.Get("C") ?? (sh.P.Cr.User + "@" + m.HostnameNow), Pass = PassHash(pass) };
            key.Blob = MakeBlob(type, m.Name + ":" + file + ":" + m.Now + ":" + m.Rand(1000000));
            try
            {
                var pn = m.V.WriteText(sh.P, file, PrivateText(key), false); pn.Mode = (pn.Mode & ~0xFFF) | 0x180;
                var qn = m.V.WriteText(sh.P, file + ".pub", key.PubLine + "\n", false); qn.Mode = (qn.Mode & ~0xFFF) | 0x1A4;
            }
            catch (VfsEx e) { io.E("Saving key \"" + file + "\" failed: " + VfsEx.Msg(e.Errno)); return 1; }
            if (!quiet)
            {
                io.L("Your identification has been saved in " + file + "\nYour public key has been saved in " + file + ".pub\nThe key fingerprint is:\n" + key.Fingerprint + " " + key.Comment + "\nThe key's randomart image is:");
                io.L(RandomArt(key));
            }
            return 0;
        }

        // ------------------------------------------------------------ connecting and logging in
        class SshOpts
        {
            public string User, Host; public int Port = 22; public List<string> Ids = new List<string>();
            public string Strict = "ask"; public bool Batch, NoPassword, NoPubkey, Quiet; public string KnownHosts;
        }

        static void SshOption(SshOpts so, string kv)
        {
            var p = kv.Split(new[] { '=', ' ' }, 2);
            if (p.Length < 2) return;
            string k = p[0].Trim().ToLowerInvariant(), v = p[1].Trim();
            switch (k)
            {
                case "stricthostkeychecking": so.Strict = v.ToLowerInvariant(); break;
                case "batchmode": so.Batch = v.ToLowerInvariant() == "yes"; break;
                case "passwordauthentication": so.NoPassword = v.ToLowerInvariant() == "no"; break;
                case "pubkeyauthentication": so.NoPubkey = v.ToLowerInvariant() == "no"; break;
                case "port": int.TryParse(v, out so.Port); break;
                case "user": if (so.User == null) so.User = v; break;
                case "identityfile": so.Ids.Add(v); break;
                case "userknownhostsfile": so.KnownHosts = v; break;
                case "loglevel": if (v.ToLowerInvariant() == "quiet" || v.ToLowerInvariant() == "error") so.Quiet = true; break;
            }
        }

        /// <summary>~/.ssh/config: Host blocks with HostName, User, Port, IdentityFile (first value wins).</summary>
        static string ApplyUserConfig(Sh sh, SshOpts so, string host)
        {
            string text = null;
            try { text = sh.M.V.ReadText(sh.P, Home(sh) + "/.ssh/config"); } catch (VfsEx) { }
            if (text == null) return host;
            bool on = false; string real = host;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = Regex.Split(line, "[\\s=]+", RegexOptions.None);
                if (p.Length < 2) continue;
                if (p[0].Equals("Host", StringComparison.OrdinalIgnoreCase)) { on = p.Skip(1).Any(x => Sh.GlobMatch(x, host)); continue; }
                if (!on) continue;
                if (p[0].Equals("HostName", StringComparison.OrdinalIgnoreCase)) real = p[1];
                else if (p[0].Equals("User", StringComparison.OrdinalIgnoreCase)) { if (so.User == null) so.User = p[1]; }
                else SshOption(so, p[0] + "=" + p[1]);
            }
            return real;
        }

        static readonly string[] DefaultIds = { "id_rsa", "id_ecdsa", "id_ecdsa_sk", "id_ed25519", "id_ed25519_sk" };

        /// <summary>Connects, checks the host key and logs in (keys, then password). Null when it failed (the reason is printed).</summary>
        static SshLogin SshLoginTo(Sh sh, Io io, SshOpts so, string prog)
        {
            var m = sh.M;
            string clientUser = sh.P.Cr.User ?? "root";
            var me = m.UserById(sh.P.Cr.Uid);
            if (me != null) clientUser = me.Name;
            string user = so.User ?? clientUser;
            string host = so.Host;
            object target; string ip;
            string err = m.Connect(host, so.Port, out target, out ip);
            if (err == "resolve") { io.E(prog + ": Could not resolve hostname " + host + ": Name or service not known"); return null; }
            if (err == "refused") { io.E(prog + ": connect to host " + host + " port " + so.Port + ": Connection refused"); return null; }
            if (err == "noroute") { io.E(prog + ": connect to host " + host + " port " + so.Port + ": No route to host"); return null; }
            if (err == "timeout") { io.E(prog + ": connect to host " + host + " port " + so.Port + ": Connection timed out"); return null; }
            if (target is RangeHost) { io.E("kex_exchange_identification: read: Connection reset by peer\nConnection reset by " + ip + " port " + so.Port); return null; }
            var tm = target as Machine;
            bool utility = target is string;
            var hk = tm != null ? HostKey(tm, "ed25519") : UtilityKey();
            // the host key
            string kh = so.KnownHosts ?? Home(sh) + "/.ssh/known_hosts";
            string khText = "";
            try { khText = m.V.ReadText(sh.P, kh); } catch (VfsEx) { }
            string hostKeyName = so.Port == 22 ? host : "[" + host + "]:" + so.Port;
            var known = khText.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)).Select(l => l.Split(' ')).Where(p => p.Length >= 3 && p[0].Split(',').Contains(hostKeyName)).ToList();
            if (known.Count > 0 && !known.Any(p => p[2] == hk.Blob))
            {
                io.E("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @\n@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\nIT IS POSSIBLE THAT SOMEONE IS DOING SOMETHING NASTY!\nThe fingerprint for the ED25519 key sent by the remote host is\n" + hk.Fingerprint + ".\nPlease contact your system administrator.\nAdd correct host key in " + kh + " to get rid of this message.\nHost key for " + host + " has changed and you have requested strict checking.\nHost key verification failed.");
                return null;
            }
            if (known.Count == 0)
            {
                bool add = so.Strict == "no" || so.Strict == "accept-new" || so.Strict == "off";
                if (!add)
                {
                    if (so.Batch || so.Strict == "yes" || sh.S == null) { io.E("Host key verification failed."); return null; }
                    io.P("The authenticity of host '" + host + " (" + ip + ")' can't be established.\nED25519 key fingerprint is " + hk.Fingerprint + ".\nThis key is not known by any other names\n");
                    for (;;)
                    {
                        var ans = sh.S.ReadLine("Are you sure you want to continue connecting (yes/no/[fingerprint])? ", false);
                        ans = (ans ?? "no").Trim();
                        if (ans == "yes" || ans == hk.Fingerprint) break;
                        if (ans == "no") { io.E("Host key verification failed."); return null; }
                        io.P("Please type 'yes', 'no' or the fingerprint: ");
                    }
                }
                try
                {
                    var dl = m.V.Resolve(sh.P, Vfs.Parent(kh), true);
                    if (!dl.Exists && Vfs.Parent(kh) == Home(sh) + "/.ssh") { var n = m.V.Mkdir(sh.P, Vfs.Parent(kh), 0x1C0, false); n.Mode = (n.Mode & ~0xFFF) | 0x1C0; }
                    var kn = m.V.WriteText(sh.P, kh, hostKeyName + " " + hk.AlgName + " " + hk.Blob + "\n", true);
                    if (khText.Length == 0) kn.Mode = (kn.Mode & ~0xFFF) | 0x1A4;
                    if (!so.Quiet) io.E("Warning: Permanently added '" + hostKeyName + "' (ED25519) to the list of known hosts.");
                }
                catch (VfsEx) { io.E("Failed to add the host to the list of known hosts (" + kh + ")."); }
            }
            if (utility)
            {
                io.E(user + "@" + host + ": Permission denied (publickey,gssapi-keyex,gssapi-with-mic).\n(Cert Lab: the utility server offers files, repositories and NFS; nobody logs in to it.)");
                return null;
            }
            // the server's rules
            int sport = 50000 + m.Rand(15000);
            string from = m.FirstV4() ?? "127.0.0.1";
            if (tm == m) from = "127.0.0.1";
            var pw = tm.User(user);
            string permitRoot = tm.SshdValue("PermitRootLogin", "prohibit-password").ToLowerInvariant();
            bool pwAuth = tm.SshdValue("PasswordAuthentication", "yes").ToLowerInvariant() == "yes" && !so.NoPassword;
            bool keyAuth = tm.SshdValue("PubkeyAuthentication", "yes").ToLowerInvariant() == "yes" && !so.NoPubkey;
            string methods = "publickey,gssapi-keyex,gssapi-with-mic" + (tm.SshdValue("PasswordAuthentication", "yes").ToLowerInvariant() == "yes" ? ",password" : "");
            bool allowed = pw != null;
            if (pw != null)
            {
                string au = tm.SshdValue("AllowUsers", null), du = tm.SshdValue("DenyUsers", null), ag = tm.SshdValue("AllowGroups", null), dg = tm.SshdValue("DenyGroups", null);
                if (au != null && !au.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Any(x => Sh.GlobMatch(x.Split('@')[0], user))) { allowed = false; tm.Log("sshd", sport, 6, "User " + user + " from " + from + " not allowed because not listed in AllowUsers", "sshd.service"); }
                if (du != null && du.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Any(x => Sh.GlobMatch(x.Split('@')[0], user))) { allowed = false; tm.Log("sshd", sport, 6, "User " + user + " from " + from + " not allowed because listed in DenyUsers", "sshd.service"); }
                var cr = tm.CredFor(user);
                Func<string, bool> inGroups = list => list.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Any(g => tm.Groups().Any(x => Sh.GlobMatch(g, x.Name) && cr.InGroup(x.Gid)));
                if (ag != null && !inGroups(ag)) { allowed = false; tm.Log("sshd", sport, 6, "User " + user + " from " + from + " not allowed because none of user's groups are listed in AllowGroups", "sshd.service"); }
                if (dg != null && inGroups(dg)) { allowed = false; tm.Log("sshd", sport, 6, "User " + user + " from " + from + " not allowed because a group is listed in DenyGroups", "sshd.service"); }
                if (pw.Uid == 0 && permitRoot == "no") allowed = false;
            }
            else tm.Log("sshd", sport, 6, "Invalid user " + user + " from " + from + " port " + sport, "sshd.service");
            // public keys
            if (keyAuth && allowed && !(pw.Uid == 0 && permitRoot == "forced-commands-only"))
            {
                var authorized = AuthorizedKeys(tm, pw, sport, from);
                var ids = so.Ids.Count > 0 ? so.Ids : DefaultIds.Select(x => Home(sh) + "/.ssh/" + x).ToList();
                foreach (var idf0 in ids)
                {
                    string idf = idf0.StartsWith("~/", StringComparison.Ordinal) ? Home(sh) + idf0.Substring(1) : idf0;
                    Loc l;
                    try { l = m.V.Resolve(sh.P, idf, true); } catch (VfsEx) { continue; }
                    if (!l.Exists) { if (so.Ids.Count > 0) io.E("Warning: Identity file " + idf + " not accessible: No such file or directory."); continue; }
                    if (l.Node.Uid == sh.P.Cr.Uid && (l.Node.Mode & 0x3F) != 0)
                    {
                        io.E("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n@         WARNING: UNPROTECTED PRIVATE KEY FILE!          @\n@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\nPermissions 0" + Convert.ToString(l.Node.Mode & 0x1FF, 8) + " for '" + idf + "' are too open.\nIt is required that your private key files are NOT accessible by others.\nThis private key will be ignored.\nLoad key \"" + idf + "\": bad permissions");
                        continue;
                    }
                    string text;
                    try { text = m.V.ReadNode(sh.P, l); } catch (VfsEx) { continue; }
                    var k = ParsePrivate(text);
                    if (k == null || !authorized.Contains(k.Blob)) continue;
                    if (k.Pass.Length > 0)
                    {
                        if (so.Batch || sh.S == null) continue;
                        var pp = sh.S.ReadLine("Enter passphrase for key '" + idf + "': ", true);
                        if (PassHash(pp ?? "") != k.Pass) continue;
                    }
                    tm.Log("sshd", sport, 6, "Accepted publickey for " + user + " from " + from + " port " + sport + " ssh2: " + k.Upper + " " + k.Fingerprint, "sshd.service");
                    if (m.Net.Range != null) m.Net.Range.LoggedIn(m, ip, user, pw.Uid == 0, "ssh", null, "key " + idf);
                    return Opened(tm, pw, ip, host, sport);
                }
            }
            // password
            if (pwAuth && !so.Batch && sh.S != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    var p = sh.S.ReadLine(user + "@" + host + "'s password: ", true);
                    if (p == null) break;
                    var s = pw == null ? null : tm.Shadow().FirstOrDefault(x => x.Name == user);
                    if (allowed && pw != null && !(pw.Uid == 0 && permitRoot != "yes") && s != null && !s.Hash.StartsWith("!", StringComparison.Ordinal) && tm.CheckPassword(user, p))
                    {
                        tm.Log("sshd", sport, 6, "Accepted password for " + user + " from " + from + " port " + sport + " ssh2", "sshd.service");
                        if (s.LastChg == "0" || PasswordExpired(tm, s))
                        {
                            io.P("WARNING: Your password has expired.\nYou must change your password now and login again!\nChanging password for user " + user + ".\n");
                            sh.S.ReadLine("Current password: ", true);
                            var a1 = sh.S.ReadLine("New password: ", true); var a2 = sh.S.ReadLine("Retype new password: ", true);
                            if (a1 != null && a1 == a2 && a1.Length > 0) { tm.SetPassword(user, a1); io.P("passwd: all authentication tokens updated successfully.\n"); }
                            else io.P("Sorry, passwords do not match.\npasswd: Authentication token manipulation error\n");
                            io.P("Connection to " + host + " closed.\n");
                            return null;
                        }
                        if (m.Net.Range != null) m.Net.Range.LoggedIn(m, ip, user, pw.Uid == 0, "ssh", p, null);
                        return Opened(tm, pw, ip, host, sport);
                    }
                    if (m.Net.Range != null) m.Net.Range.Noise(m, ip, "login-failed", 2, "ssh password for " + user);
                    tm.Log("sshd", sport, 6, "Failed password for " + (pw == null ? "invalid user " : "") + user + " from " + from + " port " + sport + " ssh2", "sshd.service");
                    if (i < 2) io.P("Permission denied, please try again.\n");
                }
            }
            io.E(user + "@" + host + ": Permission denied (" + methods + ").");
            return null;
        }

        static bool PasswordExpired(Machine m, ShEnt s)
        {
            long max, last;
            return long.TryParse(s.Max, out max) && long.TryParse(s.LastChg, out last) && max >= 0 && max < 99999 && m.DaysNow > last + max;
        }

        static SshLogin Opened(Machine tm, PwEnt pw, string ip, string host, int sport)
        {
            tm.Log("sshd", sport, 6, "pam_unix(sshd:session): session opened for user " + pw.Name + "(uid=" + pw.Uid + ") by " + pw.Name + "(uid=0)", "sshd.service");
            return new SshLogin { M = tm, Pw = pw, Ip = ip, Host = host };
        }

        /// <summary>The keys sshd accepts for a user (after its ownership, mode and SELinux checks).</summary>
        static HashSet<string> AuthorizedKeys(Machine tm, PwEnt pw, int sport, string from)
        {
            var set = new HashSet<string>();
            string files = tm.SshdValue("AuthorizedKeysFile", ".ssh/authorized_keys");
            bool strict = tm.SshdValue("StrictModes", "yes").ToLowerInvariant() != "no";
            var ctx = UserCtx(tm, pw);
            ctx.Domain = "unconfined_t";
            foreach (var f0 in files.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string f = f0.Replace("%h", pw.Home).Replace("%u", pw.Name).Replace("%%", "%");
                if (!f.StartsWith("/", StringComparison.Ordinal)) f = pw.Home.TrimEnd('/') + "/" + f;
                Loc l;
                try { l = tm.V.Resolve(ctx, f, true); } catch (VfsEx) { continue; }
                if (!l.Exists) continue;
                if (strict)
                {
                    string bad = null;
                    Func<Inode, bool> badMode = n => (n.Uid != pw.Uid && n.Uid != 0) || (n.Mode & 0x12) != 0;
                    if (badMode(l.Node)) bad = "file " + f;
                    try
                    {
                        string d = Vfs.Parent(f);
                        while (bad == null && d.Length > 1)
                        {
                            var dl = tm.V.G(d);
                            if (dl.Exists && badMode(dl.Node)) bad = "directory " + d;
                            if (d == pw.Home) break;
                            d = Vfs.Parent(d);
                        }
                    }
                    catch (VfsEx) { }
                    if (bad != null) { tm.Log("sshd", sport, 3, "Authentication refused: bad ownership or modes for " + bad, "sshd.service"); continue; }
                }
                string ty = Selinux.TypeOf(l.Node.Ctx);
                if (Selinux.Enforcing(tm) && ty != "ssh_home_t" && ty != "user_home_t" && ty != "admin_home_t" && ty != "user_home_dir_t")
                {
                    Selinux.Avc(tm, "sshd_t", "sshd", "read", Vfs.Base(f), l.Node.Ino, l.Node.Ctx, "file");
                    tm.Log("sshd", sport, 3, "Could not open user '" + pw.Name + "' authorized keys '" + f + "': Permission denied", "sshd.service");
                    continue;
                }
                string text;
                try { text = tm.V.ReadNode(ctx, l); }
                catch (VfsEx) { tm.Log("sshd", sport, 3, "Could not open user '" + pw.Name + "' authorized keys '" + f + "': Permission denied", "sshd.service"); continue; }
                foreach (var line in text.Split('\n')) { var k = ParsePublic(line); if (k != null) set.Add(k.Blob); }
            }
            return set;
        }

        static SshOpts ParseSshArgs(Sh sh, List<string> a, Io io, out List<string> rest, string prog, bool scp)
        {
            var so = new SshOpts();
            rest = new List<string>();
            int i = 1;
            for (; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "--") { i++; break; }
                if (!s.StartsWith("-", StringComparison.Ordinal) || s.Length < 2) { if (scp) { rest.Add(s); continue; } break; }
                for (int k = 1; k < s.Length; k++)
                {
                    char c = s[k];
                    string takes = scp ? "PiolFcJ" : "pilFoEcLRDJbmSWwQBO";
                    if (takes.IndexOf(c) >= 0)
                    {
                        string v = s.Substring(k + 1);
                        if (v.Length == 0) { if (i + 1 >= a.Count) { io.E(prog + ": option requires an argument -- " + c); return null; } v = a[++i]; }
                        if ((scp && c == 'P') || (!scp && c == 'p')) { if (!int.TryParse(v, out so.Port)) { io.E("Bad port '" + v + "'"); return null; } }
                        else if (c == 'i') so.Ids.Add(v);
                        else if (c == 'l' && !scp) so.User = v;
                        else if (c == 'o') SshOption(so, v);
                        break;
                    }
                    if (c == 'q') so.Quiet = true;
                    else if ("tTvnNfxXAaCgk46KMsVyY rpBd3".IndexOf(c) < 0) { io.E(prog + ": unknown option -- " + c + "\nusage: " + (scp ? "scp [-346ABCOpqRrsTv] [-c cipher] [-D sftp_server_path] [-F ssh_config]\n           [-i identity_file] [-J destination] [-l limit] [-o ssh_option]\n           [-P port] [-S program] [-X sftp_option] source ... target" : "ssh [-46AaCfGgKkMNnqsTtVvXxYy] [-B bind_interface] [-b bind_address]\n           [-c cipher_spec] [-D [bind_address:]port] [-E log_file]\n           [-e escape_char] [-F configfile] [-I pkcs11] [-i identity_file]\n           [-J destination] [-L address] [-l login_name] [-m mac_spec]\n           [-O ctl_cmd] [-o option] [-p port] [-Q query_option] [-R address]\n           [-S ctl_path] [-W host:port] [-w local_tun[:remote_tun]]\n           destination [command [argument ...]]")); return null; }
                }
                if (scp && s.Contains("r")) so.Strict = so.Strict;   // -r is read by scp itself
            }
            for (; i < a.Count; i++) rest.Add(a[i]);
            return so;
        }

        static void SplitUserHost(string dest, SshOpts so)
        {
            int at = dest.LastIndexOf('@');
            if (at >= 0) { so.User = dest.Substring(0, at); so.Host = dest.Substring(at + 1); }
            else so.Host = dest;
            if (so.Host.StartsWith("ssh://", StringComparison.Ordinal)) so.Host = so.Host.Substring(6);
        }

        /// <summary>A shell for the user on the other machine, like sshd starts one.</summary>
        static Sh RemoteShell(Sh sh, SshLogin lg)
        {
            var tm = lg.M;
            var p = UserCtx(tm, lg.Pw);
            try { var hl = tm.V.Resolve(p, lg.Pw.Home, true); if (!hl.Exists || hl.Node.T != 'd') p.Cwd = "/"; } catch (VfsEx) { p.Cwd = "/"; }
            var rs = new Sh(tm, sh.S, p) { LoginShell = true };
            rs.InitLogin(lg.Pw);
            rs.SetVar("XDG_RUNTIME_DIR", "/run/user/" + lg.Pw.Uid); rs.GetVarObj("XDG_RUNTIME_DIR").Export = true;
            rs.SetVar("SSH_CONNECTION", (sh.M.FirstV4() ?? "127.0.0.1") + " 51234 " + lg.Ip + " 22"); rs.GetVarObj("SSH_CONNECTION").Export = true;
            if (p.Cwd == "/") rs.SetVar("PWD", "/");
            tm.StartUserManager(lg.Pw.Name);
            return rs;
        }

        static int Ssh(Sh sh, List<string> a, Io io)
        {
            List<string> rest;
            var so = ParseSshArgs(sh, a, io, out rest, "ssh", false);
            if (so == null) return 255;
            if (rest.Count == 0) { io.E("usage: ssh [-46AaCfGgKkMNnqsTtVvXxYy] [-B bind_interface] [-b bind_address]\n           [-c cipher_spec] [-D [bind_address:]port] [-E log_file]\n           [-e escape_char] [-F configfile] [-I pkcs11] [-i identity_file]\n           [-J destination] [-L address] [-l login_name] [-m mac_spec]\n           [-O ctl_cmd] [-o option] [-p port] [-Q query_option] [-R address]\n           [-S ctl_path] [-W host:port] [-w local_tun[:remote_tun]]\n           destination [command [argument ...]]"); return 255; }
            string userGiven = so.User;
            so.User = null;
            SplitUserHost(rest[0], so);
            if (so.User == null) so.User = userGiven;
            so.Host = ApplyUserConfig(sh, so, so.Host);
            var cmd = rest.Skip(1).ToList();
            var lg = SshLoginTo(sh, io, so, "ssh");
            if (lg == null) return 255;
            if (lg.Pw.Shell.EndsWith("nologin", StringComparison.Ordinal) || lg.Pw.Shell.EndsWith("false", StringComparison.Ordinal)) { io.L("This account is currently not available."); io.E("Connection to " + so.Host + " closed."); return 1; }
            var rs = RemoteShell(sh, lg);
            string tty = "pts/" + (lg.M.Logins.Count + 1);
            lg.M.Logins.Add(lg.Pw.Name + " " + tty + " " + lg.M.Now + " " + (sh.M == lg.M ? "127.0.0.1" : sh.M.FirstV4()));
            try
            {
                if (cmd.Count > 0)
                {
                    var rio = new Io { In = io.In, Out = io.Out, Err = io.Err };
                    rs.Io = rio; rs.Interactive = false;
                    try { return rs.RunText(string.Join(" ", cmd.ToArray()), rio); }
                    catch (ExitEx e) { return e.Code; }
                }
                if (sh.S == null) { io.E("Pseudo-terminal will not be allocated because stdin is not a terminal."); return 0; }
                string last = lg.M.LastLoginText(lg.Pw.Name, "pts/0");
                string motd = (lg.M.V.Cat("/etc/motd") ?? "").TrimEnd('\n');
                string banner = motd.Length > 0 ? motd + "\n" : (lg.M.Net != null && lg.M.Net.Range != null ? "" : "Register this system with Red Hat Insights: insights-client --register\nCreate an account or view all your systems at https://red.ht/insights-dashboard\n");
                if (!so.Quiet) io.L(banner + last.Replace(" on pts/0", " from " + (sh.M.FirstV4() ?? "127.0.0.1")));
                sh.S.Remote.Add(lg.M);
                sh.S.ShStack.Add(rs);
                int st;
                try { st = rs.Repl(); }
                finally { sh.S.ShStack.Remove(rs); sh.S.Remote.Remove(lg.M); rs.SaveHistory(); }
                io.L("Connection to " + so.Host + " closed.");
                return st;
            }
            catch (MachineKick k)
            {
                if (k.Target != lg.M || lg.M == sh.M) throw;
                if (sh.S != null) { sh.S.ShStack.Remove(rs); sh.S.Remote.Remove(lg.M); }
                io.P(k.Why ?? ("Connection to " + so.Host + " closed by remote host.\n"));
                return 255;
            }
            finally { lg.M.Logins.RemoveAll(x => x.StartsWith(lg.Pw.Name + " " + tty + " ", StringComparison.Ordinal)); }
        }

        // ------------------------------------------------------------ scp
        static int Scp(Sh sh, List<string> a, Io io)
        {
            List<string> rest;
            bool recursive = a.Skip(1).Any(x => x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("--", StringComparison.Ordinal) && x.Contains("r") && !x.StartsWith("-o", StringComparison.Ordinal));
            bool preserve = a.Skip(1).Any(x => x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("--", StringComparison.Ordinal) && x.Contains("p") && !x.StartsWith("-o", StringComparison.Ordinal));
            var so = ParseSshArgs(sh, a, io, out rest, "scp", true);
            if (so == null) return 1;
            if (rest.Count < 2) { io.E("usage: scp [-346ABCOpqRrsTv] [-c cipher] [-D sftp_server_path] [-F ssh_config]\n           [-i identity_file] [-J destination] [-l limit] [-o ssh_option]\n           [-P port] [-S program] [-X sftp_option] source ... target"); return 1; }
            Func<string, string[]> split = s =>
            {
                int c = s.IndexOf(':');
                if (c <= 0 || s.StartsWith("/", StringComparison.Ordinal) || (s.IndexOf('/') >= 0 && s.IndexOf('/') < c)) return new[] { null, s };
                return new[] { s.Substring(0, c), s.Substring(c + 1) };
            };
            var logins = new Dictionary<string, Tuple<Machine, PCtx, string>>();
            Func<string, Tuple<Machine, PCtx, string>> side = remote =>
            {
                if (remote == null) return Tuple.Create(sh.M, sh.P, sh.P.Cwd);
                Tuple<Machine, PCtx, string> t;
                if (logins.TryGetValue(remote, out t)) return t;
                var o2 = new SshOpts { Port = so.Port, Ids = so.Ids, Strict = so.Strict, Batch = so.Batch, NoPassword = so.NoPassword, NoPubkey = so.NoPubkey, KnownHosts = so.KnownHosts, Quiet = so.Quiet };
                SplitUserHost(remote, o2);
                o2.Host = ApplyUserConfig(sh, o2, o2.Host);
                var lg = SshLoginTo(sh, io, o2, "ssh");
                if (lg == null) return null;
                var ctx = UserCtx(lg.M, lg.Pw);
                t = Tuple.Create(lg.M, ctx, lg.Pw.Home);
                logins[remote] = t;
                return t;
            };
            var dst = split(rest[rest.Count - 1]);
            var dside = side(dst[0]);
            if (dside == null) { io.E("scp: Connection closed"); return 255; }
            string dpath = dst[1].Length == 0 ? dside.Item3 : (dst[1].StartsWith("/", StringComparison.Ordinal) ? dst[1] : (dst[0] == null ? dst[1] : dside.Item3.TrimEnd('/') + "/" + dst[1]));
            int status = 0;
            foreach (var s0 in rest.Take(rest.Count - 1))
            {
                var src = split(s0);
                var sside = side(src[0]);
                if (sside == null) { io.E("scp: Connection closed"); return 255; }
                string spath = src[1].StartsWith("/", StringComparison.Ordinal) || src[0] == null ? src[1] : sside.Item3.TrimEnd('/') + "/" + src[1];
                var paths = new List<string>();
                if (src[0] != null && (spath.Contains("*") || spath.Contains("?")))
                {
                    try
                    {
                        var dl = sside.Item1.V.Resolve(sside.Item2, Vfs.Parent(spath), true);
                        if (dl.Exists && dl.IsDir) foreach (var k in Vfs.Sorted(dl.Node.Kids.Keys)) if (Sh.GlobMatch(Vfs.Base(spath), k) && !k.StartsWith(".", StringComparison.Ordinal)) paths.Add(Vfs.Parent(spath).TrimEnd('/') + "/" + k);
                    }
                    catch (VfsEx) { }
                    if (paths.Count == 0) { io.E("scp: " + spath + ": No such file or directory"); status = 1; continue; }
                }
                else paths.Add(spath);
                foreach (var sp in paths)
                    if (!CopyTree(sh, so.Quiet ? new Io { In = io.In, Out = new NullSink(), Err = io.Err } : io, sside.Item1, sside.Item2, sp, dside.Item1, dside.Item2, dpath, recursive, preserve, true)) status = 1;
            }
            return status;
        }

        static bool CopyTree(Sh sh, Io io, Machine sm, PCtx sc, string spath, Machine dm, PCtx dc, string dpath, bool recursive, bool preserve, bool top)
        {
            Loc sl;
            try { sl = sm.V.Resolve(sc, spath, true); }
            catch (VfsEx e) { io.E("scp: " + spath + ": " + VfsEx.Msg(e.Errno)); return false; }
            if (!sl.Exists) { io.E("scp: " + spath + ": No such file or directory"); return false; }
            // copying into an existing folder keeps the name
            string target = dpath;
            try
            {
                var tl = dm.V.Resolve(dc, dpath, true);
                if (tl.Exists && tl.IsDir) target = dpath.TrimEnd('/') + "/" + Vfs.Base(spath);
                else if (!tl.Exists && dpath.EndsWith("/", StringComparison.Ordinal)) { io.E("scp: " + dpath + ": Is a directory? No such file or directory"); return false; }
            }
            catch (VfsEx e) { io.E("scp: " + dpath + ": " + VfsEx.Msg(e.Errno)); return false; }
            if (sl.IsDir)
            {
                if (!recursive) { io.E("scp: " + spath + ": not a regular file"); return false; }
                try
                {
                    var tl = dm.V.Resolve(dc, target, true);
                    if (!tl.Exists) dm.V.Mkdir(dc, target, sl.Node.Mode & 0xFFF, false);
                }
                catch (VfsEx e) { io.E("scp: " + target + ": " + VfsEx.Msg(e.Errno)); return false; }
                bool ok = true;
                foreach (var k in Vfs.Sorted(sl.Node.Kids.Keys)) if (!CopyTree(sh, io, sm, sc, spath.TrimEnd('/') + "/" + k, dm, dc, target, true, preserve, false)) ok = false;
                return ok;
            }
            string text;
            try { text = sm.V.ReadNode(sc, sl); }
            catch (VfsEx e) { io.E("scp: " + spath + ": " + VfsEx.Msg(e.Errno)); return false; }
            try
            {
                var n = dm.V.WriteText(dc, target, sl.Node.Text == null ? "" : text, false);
                if (sl.Node.Text == null) { n.Text = null; n.BinSize = sl.Node.BinSize; }
                if (preserve) { n.Mode = (n.Mode & ~0xFFF) | (sl.Node.Mode & 0xFFF); n.Mtime = sl.Node.Mtime; }
                else if ((sl.Node.Mode & 0x49) != 0) n.Mode |= (sl.Node.Mode & 0x49) & ~dc.Umask;
            }
            catch (VfsEx e) { io.E("scp: " + target + ": " + VfsEx.Msg(e.Errno)); return false; }
            if (io.Out.IsTty)
            {
                long size = sl.Node.Size;
                io.L(Vfs.Base(spath).PadRight(40) + " 100% " + (size < 1024 ? size + "     " : (size / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "KB").PadLeft(6) + "   1.2MB/s   00:00    ");
            }
            return true;
        }

        // ------------------------------------------------------------ ssh-copy-id, ssh-keyscan
        static int SshCopyId(Sh sh, List<string> a, Io io)
        {
            string idFile = null; bool force = false; int port = 22; var so = new SshOpts();
            string dest = null;
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-i") { idFile = i + 1 < a.Count && !a[i + 1].StartsWith("-", StringComparison.Ordinal) ? a[++i] : null; if (idFile == null) idFile = ""; }
                else if (s.StartsWith("-i", StringComparison.Ordinal) && s.Length > 2) idFile = s.Substring(2);
                else if (s == "-f") force = true;
                else if (s == "-p" && i + 1 < a.Count) int.TryParse(a[++i], out port);
                else if (s == "-o" && i + 1 < a.Count) SshOption(so, a[++i]);
                else if (s == "-n" || s == "-s" || s == "-x") { }
                else if (s.StartsWith("-", StringComparison.Ordinal)) { io.E("Usage: /usr/bin/ssh-copy-id [-h|-?|-f|-n|-s|-x] [-i [identity_file]] [-p port] [-F alternative ssh_config file] [-t target_path] [[-o <ssh -o options>] ...] [user@]hostname"); return 1; }
                else dest = s;
            }
            if (dest == null) { io.E("Usage: /usr/bin/ssh-copy-id [-h|-?|-f|-n|-s|-x] [-i [identity_file]] [-p port] [-F alternative ssh_config file] [-t target_path] [[-o <ssh -o options>] ...] [user@]hostname"); return 1; }
            string home = Home(sh);
            string pubFile;
            if (!string.IsNullOrEmpty(idFile)) pubFile = idFile.EndsWith(".pub", StringComparison.Ordinal) ? idFile : idFile + ".pub";
            else
            {
                pubFile = null;
                foreach (var n in new[] { "id_rsa", "id_ecdsa", "id_ecdsa_sk", "id_ed25519", "id_ed25519_sk" })
                    if (sh.M.V.Exists(Vfs.Global(sh.P, home + "/.ssh/" + n + ".pub"))) { pubFile = home + "/.ssh/" + n + ".pub"; }
                // ssh-copy-id takes the newest key file; with several the last in this list is a fair stand-in
            }
            if (pubFile == null) { io.E("/usr/bin/ssh-copy-id: ERROR: No identities found"); return 1; }
            if (pubFile.StartsWith("~/", StringComparison.Ordinal)) pubFile = home + pubFile.Substring(1);
            string pubText;
            try { pubText = sh.M.V.ReadText(sh.P, pubFile); }
            catch (VfsEx) { io.E("/usr/bin/ssh-copy-id: ERROR: failed to open ID file '" + pubFile + "': No such file or directory"); return 1; }
            var key = ParsePublic(pubText.Split('\n')[0]);
            if (key == null) { io.E("/usr/bin/ssh-copy-id: ERROR: failed to read a key from '" + pubFile + "'"); return 1; }
            io.E("/usr/bin/ssh-copy-id: INFO: Source of key(s) to be installed: \"" + pubFile + "\"");
            so.Port = port;
            SplitUserHost(dest, so);
            so.Host = ApplyUserConfig(sh, so, so.Host);
            if (!force) io.E("/usr/bin/ssh-copy-id: INFO: attempting to log in with the new key(s), to filter out any that are already installed");
            // already there? (a quiet key-only login)
            if (!force)
            {
                var probe = new SshOpts { User = so.User, Host = so.Host, Port = so.Port, Strict = so.Strict, Batch = false, NoPassword = true, Quiet = true, Ids = new List<string> { pubFile.Substring(0, pubFile.Length - 4) } };
                var bufIo = new Io { In = io.In, Out = io.Out, Err = new BufSink() };
                var plg = SshLoginTo(sh, bufIo, probe, "ssh");
                string perr = ((BufSink)bufIo.Err).Text;
                if (plg != null)
                {
                    io.E("\n/usr/bin/ssh-copy-id: WARNING: All keys were skipped because they already exist on the remote system.\n\t\t(if you think this is a mistake, you may want to use -f option)\n");
                    return 0;
                }
                if (perr.Contains("Could not resolve") || perr.Contains("Connection refused") || perr.Contains("No route") || perr.Contains("verification failed")) { io.E(perr.TrimEnd('\n')); return 1; }
                foreach (var l in perr.Split('\n')) if (l.StartsWith("Warning: Permanently added", StringComparison.Ordinal)) io.E(l);
                io.E("/usr/bin/ssh-copy-id: INFO: 1 key(s) remain to be installed -- if you are prompted now it is to install the new keys");
            }
            var lg = SshLoginTo(sh, io, new SshOpts { User = so.User, Host = so.Host, Port = so.Port, Strict = so.Strict, Ids = new List<string>(), NoPubkey = false }, "ssh");
            if (lg == null) return 1;
            var tm = lg.M;
            var ctx = UserCtx(tm, lg.Pw);
            try
            {
                string dir = lg.Pw.Home.TrimEnd('/') + "/.ssh";
                var dl = tm.V.Resolve(ctx, dir, true);
                if (!dl.Exists) { var n = tm.V.Mkdir(ctx, dir, 0x1C0, false); n.Mode = (n.Mode & ~0xFFF) | 0x1C0; }
                string f = dir + "/authorized_keys";
                var fl = tm.V.Resolve(ctx, f, true);
                string old = fl.Exists ? tm.V.ReadNode(ctx, fl) : "";
                var kn = tm.V.WriteText(ctx, f, (old.Length > 0 && !old.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "") + key.PubLine + "\n", true);
                if (!fl.Exists) kn.Mode = (kn.Mode & ~0xFFF) | 0x180;
            }
            catch (VfsEx e) { io.E("sh: .ssh/authorized_keys: " + VfsEx.Msg(e.Errno)); return 1; }
            io.L("\nNumber of key(s) added: 1\n\nNow try logging into the machine, with:   \"ssh " + (so.Port != 22 ? "-p " + so.Port + " " : "") + "'" + dest + "'\"\nand check to make sure that only the key(s) you wanted were added.\n");
            return 0;
        }

        static int SshKeyscan(Sh sh, List<string> a, Io io)
        {
            int st = 0;
            foreach (var h in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
            {
                object target; string ip;
                string err = sh.M.Connect(h, 22, out target, out ip);
                if (err != null) { io.E(err == "resolve" ? "getaddrinfo " + h + ": Name or service not known" : ""); st = 1; continue; }
                var tm = target as Machine;
                foreach (var t in tm != null ? new[] { "rsa", "ecdsa", "ed25519" } : new[] { "ed25519" })
                {
                    var k = tm != null ? HostKey(tm, t) : UtilityKey();
                    io.E("# " + h + ":22 SSH-2.0-OpenSSH_8.7");
                    io.L(h + " " + k.AlgName + " " + k.Blob);
                }
            }
            return st;
        }
    }
}
