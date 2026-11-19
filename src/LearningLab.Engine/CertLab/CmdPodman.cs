// Cert Lab - podman, skopeo and buildah. Images come from the practice registry on the utility server
// (registry.lab.example.com, which needs podman login like a real private registry) or are built from a
// Containerfile. Rootless containers belong to the user who made them; root's are kept apart, as in podman.
// Programs inside a container run in its own small file system (a chroot of the container's mount).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        public const string RegistryHost = "registry.lab.example.com";
        public const string RegistryUser = "student";
        public const string RegistryPass = "student";

        static void RegPodman()
        {
            P("podman", Podman); P("skopeo", Skopeo);
            P("buildah", (sh, a, io) =>
            {
                if (a.Count > 1 && (a[1] == "bud" || a[1] == "build")) { var b = new List<string> { "podman", "build" }; b.AddRange(a.Skip(2)); return Podman(sh, b, io); }
                if (a.Count > 1 && a[1] == "images") return Podman(sh, new List<string> { "podman", "images" }, io);
                io.E("(Cert Lab: buildah build and buildah images are included; for the rest use podman.)");
                return 1;
            });
        }

        static string Owner(Sh sh) { return sh.P.Cr.Uid == 0 ? "" : (sh.M.UName(sh.P.Cr.Uid)); }
        static string Hex64(string seed)
        {
            using (var h = SHA256.Create()) return string.Join("", h.ComputeHash(Encoding.UTF8.GetBytes(seed)).Select(b => b.ToString("x2")).ToArray());
        }
        static string Ago(Machine m, long t)
        {
            long d = Math.Max(0, m.Now - t);
            if (d < 2) return "Less than a second ago";
            if (d < 60) return d + " seconds ago";
            if (d < 3600) return (d / 60 == 1 ? "About a minute" : (d / 60) + " minutes") + " ago";
            if (d < 86400) return (d / 3600 == 1 ? "About an hour" : (d / 3600) + " hours") + " ago";
            if (d < 86400 * 14) return (d / 86400) + " days ago";
            return (d / 86400 / 7) + " weeks ago";
        }
        static string Up(Machine m, long t)
        {
            long d = Math.Max(0, m.Now - t);
            if (d < 60) return d + " seconds";
            if (d < 120) return "About a minute";
            if (d < 3600) return (d / 60) + " minutes";
            if (d < 7200) return "About an hour";

            if (d < 86400) return (d / 3600) + " hours";
            return (d / 86400) + " days";
        }
        static string Mb(long b) { return b >= 1000000000 ? (b / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + " GB" : b >= 1000000 ? (b / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " MB" : (b / 1e3).ToString("0", CultureInfo.InvariantCulture) + " kB"; }

        static string AuthFile(Sh sh)
        {
            if (sh.P.Cr.Uid == 0) return "/run/containers/0/auth.json";
            string x = sh.GetVar("XDG_RUNTIME_DIR");
            return (string.IsNullOrEmpty(x) ? "/tmp/podman-run-" + sh.P.Cr.Uid : x) + "/containers/auth.json";
        }
        static bool LoggedIn(Sh sh, string host)
        {
            string t = sh.M.V.Cat(AuthFile(sh));
            return t != null && t.Contains("\"" + host + "\"");
        }

        static bool CheckRootless(Sh sh, Io io)
        {
            if (sh.P.Cr.Uid != 0 && string.IsNullOrEmpty(sh.GetVar("XDG_RUNTIME_DIR")))
                io.E("WARN[0000] The cgroupv2 manager is set to systemd but there is no systemd user session available\nWARN[0000] For using systemd, you may need to log in using a user session\nWARN[0000] Alternatively, you can enable lingering with: `loginctl enable-linger " + sh.P.Cr.Uid + "` (possibly as root)\nWARN[0000] Falling back to --cgroup-manager=cgroupfs");
            return true;
        }

        static List<string> SearchRegistries(Machine m)
        {
            var t = (m.V.Cat("/etc/containers/registries.conf") ?? "");
            var extra = new List<string>();
            try { var d = m.V.G("/etc/containers/registries.conf.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) extra.Add(m.V.Cat("/etc/containers/registries.conf.d/" + k) ?? ""); } catch (VfsEx) { }
            var l = new List<string>();
            foreach (var text in new[] { t }.Concat(extra))
            {
                var mm = Regex.Match(text, "(?m)^\\s*unqualified-search-registries\\s*=\\s*\\[([^\\]]*)\\]");
                if (mm.Success) { l = mm.Groups[1].Value.Split(',').Select(x => x.Trim().Trim('"', '\'')).Where(x => x.Length > 0).ToList(); }
            }
            return l;
        }

        /// <summary>Full name for an image reference: "ubi9/ubi" -> registry/ubi9/ubi:latest (via the search list).</summary>
        static string FullRef(Machine m, string r, out List<string> tried)
        {
            tried = new List<string>();
            string name = r, tag = "latest";
            if (name.StartsWith("docker://", StringComparison.Ordinal)) name = name.Substring(9);
            int at = name.IndexOf('@'); if (at > 0) name = name.Substring(0, at);
            int colon = name.LastIndexOf(':');
            if (colon > 0 && !name.Substring(colon).Contains("/")) { tag = name.Substring(colon + 1); name = name.Substring(0, colon); }
            string first = name.Split('/')[0];
            bool qualified = name.Contains("/") && (first.Contains(".") || first.Contains(":") || first == "localhost");
            if (qualified) return name + ":" + tag;
            foreach (var reg in SearchRegistries(m)) tried.Add(reg + "/" + name + ":" + tag);
            return null;
        }

        static CImage LocalImage(Machine m, string owner, string r)
        {
            var mine = m.Images.Where(i => i.Owner == owner).ToList();
            string name = r, tag = "latest";
            int colon = name.LastIndexOf(':');
            if (colon > 0 && !name.Substring(colon).Contains("/")) { tag = name.Substring(colon + 1); name = name.Substring(0, colon); }
            var hit = mine.FirstOrDefault(i => i.Repo == name && i.Tag == tag)
                ?? mine.FirstOrDefault(i => (i.Repo.EndsWith("/" + name, StringComparison.Ordinal)) && i.Tag == tag)
                ?? mine.FirstOrDefault(i => i.Id.StartsWith(r, StringComparison.Ordinal) && r.Length >= 3);
            return hit;
        }

        /// <summary>podman pull: the registry must answer and know the image; the practice registry wants a login.</summary>
        static CImage Pull(Sh sh, Io io, string r, bool quiet)
        {
            var m = sh.M;
            List<string> tried;
            string full = FullRef(m, r, out tried);
            var cands = full != null ? new List<string> { full } : tried;
            if (cands.Count == 0) { io.E("Error: short-name \"" + r + "\" did not resolve to an alias and no unqualified-search registries are defined in \"/etc/containers/registries.conf\""); return null; }
            var errors = new List<string>();
            foreach (var c in cands)
            {
                string host = c.Split('/')[0];
                string repo = c.Substring(0, c.LastIndexOf(':')), tag = c.Substring(c.LastIndexOf(':') + 1);
                if (!quiet) io.E("Trying to pull " + c + "...");
                if (host != RegistryHost)
                {
                    errors.Add("initializing source docker://" + c + ": pinging container registry " + host + ": Get \"https://" + host + "/v2/\": dial tcp: lookup " + host + ": no such host");
                    continue;
                }
                string ip = m.Net.Resolve(m, host);
                if (ip == null || !m.Net.Reachable(m, ip)) { errors.Add("initializing source docker://" + c + ": pinging container registry " + host + ": Get \"https://" + host + "/v2/\": dial tcp " + (ip ?? host) + ":443: connect: no route to host"); continue; }
                var img = m.Net.Registry.FirstOrDefault(i => i.Repo == repo && i.Tag == tag);
                if (!LoggedIn(sh, host)) { errors.Add("initializing source docker://" + c + ": reading manifest " + tag + " in " + repo + ": unauthorized: authentication required"); continue; }
                if (img == null) { errors.Add("initializing source docker://" + c + ": reading manifest " + tag + " in " + repo + ": manifest unknown"); continue; }
                if (!quiet) io.E("Getting image source signatures\nCopying blob " + img.Id.Substring(20, 12) + " done   | \nCopying config " + img.Id.Substring(0, 10) + " done   | \nWriting manifest to image destination");
                var local = img.Copy(); local.Owner = Owner(sh);
                m.Images.RemoveAll(i => i.Owner == local.Owner && i.Repo == local.Repo && i.Tag == local.Tag);
                m.Images.Add(local);
                EnsureStorage(sh);
                return local;
            }
            if (cands.Count == 1) io.E("Error: " + errors[0]);
            else io.E("Error: " + string.Join("\n", errors.Select(e => "  * " + e).ToArray()).Trim());
            return null;
        }

        static void EnsureStorage(Sh sh)
        {
            string root = sh.M.StorageRoot(Owner(sh));
            try
            {
                foreach (var d in new[] { root, root + "/overlay", root + "/volumes" })
                    if (!sh.M.V.Exists(d))
                    {
                        var n = sh.M.V.EnsureDir(d, 0x1C0, null);
                        if (sh.P.Cr.Uid != 0) { n.Uid = sh.P.Cr.Uid; n.Gid = sh.P.Cr.Gid; }
                    }
            }
            catch (VfsEx) { }
        }

        static Container FindC(Sh sh, string idOrName)
        {
            string o = Owner(sh);
            return sh.M.Containers.FirstOrDefault(c => c.Owner == o && (c.Name == idOrName || c.Id == idOrName)) ?? sh.M.Containers.FirstOrDefault(c => c.Owner == o && idOrName.Length >= 3 && c.Id.StartsWith(idOrName, StringComparison.Ordinal));
        }

        static bool LongRunningCmd(CImage img, string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return img.Kind == "httpd" || img.Kind == "nginx" || img.Kind == "mariadb";
            return Regex.IsMatch(cmd, "^(/usr/bin/)?sleep +(infinity|\\d{3,})") || cmd.Contains("tail -f") || cmd.Contains("run-httpd") || cmd.Contains("httpd -D") || cmd.Contains("nginx -g") || cmd.Contains("run-mysqld");
        }

        /// <summary>Runs a command line inside a container (a chroot of its file system) as the container's root.</summary>
        static int RunInside(Sh sh, Container c, string cmd, Io io, bool interactive)
        {
            var m = sh.M;
            bool wasMounted = m.Mounts.Any(x => x.Path == c.Mount);
            if (!wasMounted) m.MountContainer(c);
            try
            {
                var p = new PCtx { Cr = new Cred { Uid = 0, Gid = 0, User = "root" }, Root = c.Mount, Cwd = "/", Domain = "container_t" };
                var cs = new Sh(m, sh.S, p) { Host = c.Short };
                cs.SetVar("PATH", "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"); cs.GetVarObj("PATH").Export = true;
                cs.SetVar("HOME", "/root"); cs.SetVar("HOSTNAME", c.Short); cs.SetVar("container", "oci");
                foreach (var e in c.Env) { var kv = e.Split(new[] { '=' }, 2); if (kv.Length == 2) { cs.SetVar(kv[0], kv[1]); cs.GetVarObj(kv[0]).Export = true; } }
                if (interactive && sh.S != null && string.IsNullOrEmpty(cmd))
                {
                    cs.SetVar("PS1", "[root@" + c.Short + " /]# ");
                    sh.S.ShStack.Add(cs);
                    try { return cs.Repl(); } catch (ExitEx e) { return e.Code; } finally { sh.S.ShStack.Remove(cs); }
                }
                cs.Io = io;
                try { return cs.RunText(string.IsNullOrEmpty(cmd) ? "true" : cmd, io); }
                catch (ExitEx e) { return e.Code; }
                catch (TooLong) { return 0; }
            }
            finally { if (!wasMounted && !c.Running) m.UnmountContainer(c); }
        }

        static string HttpdLogs(Container c, Machine m)
        {
            var d = m.LocalTime(m.Now);
            string ip = c.Owner.Length > 0 ? "10.0.2.100" : "10.88.0.2";
            if (c.Kind == "httpd") return "=> sourcing 10-set-mpm.sh ...\n=> sourcing 20-copy-config.sh ...\n=> sourcing 40-ssl-certs.sh ...\n---> Generating SSL key pair for httpd...\nAH00558: httpd: Could not reliably determine the server's fully qualified domain name, using " + ip + ". Set the 'ServerName' directive globally to suppress this message\n[" + d.ToString("ddd MMM dd HH:mm:ss.ffffff yyyy", CultureInfo.InvariantCulture) + "] [ssl:warn] [pid 1:tid 1] AH01909: " + ip + ":8443:0 server certificate does NOT include an ID which matches the server name\n[" + d.ToString("ddd MMM dd HH:mm:ss.ffffff yyyy", CultureInfo.InvariantCulture) + "] [mpm_event:notice] [pid 1:tid 1] AH00489: Apache/2.4.62 (Red Hat Enterprise Linux) OpenSSL/3.2.2 configured -- resuming normal operations\n";
            if (c.Kind == "nginx") return "/docker-entrypoint.sh: Configuration complete; ready for start up\n";
            if (c.Kind == "mariadb") return "=> sourcing 20-validate-variables.sh ...\n=> sourcing 25-validate-replication-variables.sh ...\n=> sourcing 30-base-config.sh ...\n---> " + d.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "     Processing basic MySQL configuration files ...\n---> " + d.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "     Starting MariaDB server ...\n[Note] /usr/libexec/mariadbd: ready for connections.\n";
            return "";
        }

        /// <summary>Starts a created or stopped container: checks its ports, runs its command.</summary>
        static string StartC(Sh sh, Container c, Io io)
        {
            var m = sh.M;
            foreach (var p in c.Ports)
            {
                if (c.Owner.Length > 0 && p.Host < UnprivStart(m)) return "rootlessport cannot expose privileged port " + p.Host + ", you can add 'net.ipv4.ip_unprivileged_port_start=" + p.Host + "' to /etc/sysctl.conf (currently " + UnprivStart(m) + "), or choose a larger port number (>= " + UnprivStart(m) + "): listen tcp 0.0.0.0:" + p.Host + ": bind: permission denied";
                var svc = m.Services.Values.FirstOrDefault(s => s.Active == "active" && s.Ports.Contains(p.Host));
                var other = m.Containers.FirstOrDefault(x => x != c && x.Running && x.Ports.Any(q => q.Host == p.Host));
                if (svc != null || other != null) return (c.Owner.Length > 0 ? "rootlessport listen tcp 0.0.0.0:" + p.Host + ": bind: address already in use" : "cannot listen on the TCP port: listen tcp4 :" + p.Host + ": bind: address already in use");
            }
            if (c.Kind == "mariadb" && !c.Env.Any(e => e.StartsWith("MYSQL_ROOT_PASSWORD=", StringComparison.Ordinal)) && !(c.Env.Any(e => e.StartsWith("MYSQL_USER=", StringComparison.Ordinal)) && c.Env.Any(e => e.StartsWith("MYSQL_PASSWORD=", StringComparison.Ordinal)) && c.Env.Any(e => e.StartsWith("MYSQL_DATABASE=", StringComparison.Ordinal))))
            {
                c.Logs += "=> sourcing 20-validate-variables.sh ...\nYou must either specify the following environment variables:\n  MYSQL_USER\n  MYSQL_PASSWORD\n  MYSQL_DATABASE\nOr the following environment variable:\n  MYSQL_ROOT_PASSWORD (regex: '^[^\\'\\\\]+$')\nOr both.\n";
                c.Running = false; c.ExitCode = 1; c.Status = "Exited (1)";
                return null;
            }
            var img = m.Images.FirstOrDefault(i => i.Id == c.ImageId) ?? new CImage { Kind = c.Kind };
            string unit = sh.GetVar("CERTLAB_UNIT");
            if (!string.IsNullOrEmpty(unit)) c.Unit = unit;
            if (LongRunningCmd(img, c.Cmd == img.Cmd ? "" : c.Cmd) || (c.Cmd == img.Cmd && img.Kind != "ubi"))
            {
                c.Running = true; c.Status = "Up"; c.StartedAt = m.Now;
                m.MountContainer(c);
                c.Logs += HttpdLogs(c, m);
                return null;
            }
            // a command that ends: run it now, keep its output as the logs
            var buf = new BufSink();
            int st = RunInside(sh, c, c.Cmd == "/bin/bash" ? "true" : c.Cmd, new Io { In = new StrSrc(""), Out = buf, Err = buf }, false);
            c.Logs += buf.Text;
            c.Running = false; c.ExitCode = st; c.Status = "Exited (" + st + ")"; c.StartedAt = m.Now; c.StoppedAt = m.Now;
            return null;
        }
        static int UnprivStart(Machine m)
        {
            string v;
            if (m.Sysctl.TryGetValue("net.ipv4.ip_unprivileged_port_start", out v)) { int n; if (int.TryParse(v, out n)) return n; }
            return 1024;
        }

        static void StopC(Machine m, Container c)
        {
            if (!c.Running) return;
            c.Running = false; c.ExitCode = c.Kind == "ubi" ? 137 : 0; c.Status = "Exited (" + c.ExitCode + ")"; c.StoppedAt = m.Now;
            m.UnmountContainer(c);
        }

        static void RemoveC(Machine m, Container c)
        {
            StopC(m, c);
            m.Containers.Remove(c);
            m.Fss.Remove(c.FsId);
        }

        /// <summary>-v host:container[:opts]; :Z / :z relabel the host folder for containers.</summary>
        static string AddVolume(Sh sh, Container c, string spec)
        {
            var m = sh.M;
            var p = spec.Split(':');
            if (p.Length < 2) return "invalid volume specification: \"" + spec + "\"";
            string host = p[0], cont = p[1], opts = p.Length > 2 ? p[2] : "";
            var v = new CVol { Host = host, Cont = cont, Opts = opts };
            if (!host.StartsWith("/", StringComparison.Ordinal) && !host.StartsWith(".", StringComparison.Ordinal))
            {
                // a named volume
                string dir = m.StorageRoot(c.Owner) + "/volumes/" + host + "/_data";
                if (!m.V.Exists(dir)) { var n = m.V.EnsureDir(dir, 0x1ED, "system_u:object_r:container_file_t:s0"); if (c.Owner.Length > 0) { var pw = m.User(c.Owner); n.Uid = pw.Uid; n.Gid = pw.Gid; } }
                v.Host = dir; v.Named = true;
            }
            else
            {
                string full = Vfs.Norm(Vfs.Join(sh.P.Cwd, host));
                Loc l;
                try { l = m.V.Resolve(sh.P, full, true); } catch (VfsEx e) { return "statfs " + full + ": " + e.Message.ToLowerInvariant(); }
                if (!l.Exists) return "statfs " + full + ": no such file or directory";
                v.Host = l.GPath;
                var ol = opts.Split(',');
                if (ol.Contains("Z") || ol.Contains("z"))
                {
                    if (v.Host == "/" || v.Host == "/home" || v.Host == "/usr" || v.Host == "/etc") return "relabeling content in " + v.Host + " is not allowed";
                    string ctx = ol.Contains("Z") ? "system_u:object_r:container_file_t:s0:c" + (100 + m.Rand(800)) + ",c" + (900 + m.Rand(100)) : "system_u:object_r:container_file_t:s0";
                    Action<Inode, FileSys> lab = null;
                    lab = (n, fs) => { n.Ctx = ctx; if (n.T == 'd') foreach (var k in n.Kids.Values) { var ch = fs.Node(k); if (ch != null) lab(ch, fs); } };
                    // a rootless user may relabel what they own, also files they gave to a container user (podman unshare chown: their subordinate IDs)
                    if (sh.P.Cr.Uid == 0 || l.Node.Uid == sh.P.Cr.Uid || (l.Node.Uid >= 100000 && l.Node.Uid < 165536 * 4)) lab(l.Node, l.Fs);
                }
            }
            c.Volumes.Add(v);
            return null;
        }

        // ------------------------------------------------------------ podman
        static int Podman(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count < 2) { io.E("Error: missing command 'podman COMMAND'\nTry 'podman --help' for more information"); return 125; }
            string verb = a[1];
            var rest = a.Skip(2).ToList();
            string owner = Owner(sh);
            if (verb == "--version" || verb == "-v" || verb == "version") { io.L(verb == "version" ? "Client:       Podman Engine\nVersion:      5.2.2\nAPI Version:  5.2.2\nGo Version:   go1.22.7 (Red Hat 1.22.7-2.el9_5)\nBuilt:        Tue Nov 12 00:00:00 2024\nOS/Arch:      linux/amd64" : "podman version 5.2.2"); return 0; }
            if (owner.Length > 0 && verb != "login" && verb != "logout" && verb != "search") CheckRootless(sh, io);
            switch (verb)
            {
                case "login":
                    {
                        string user = null, pass = null; bool stdin = false; string host = null;
                        for (int i = 0; i < rest.Count; i++)
                        {
                            if ((rest[i] == "-u" || rest[i] == "--username") && i + 1 < rest.Count) user = rest[++i];
                            else if (rest[i].StartsWith("--username=", StringComparison.Ordinal)) user = rest[i].Substring(11);
                            else if ((rest[i] == "-p" || rest[i] == "--password") && i + 1 < rest.Count) pass = rest[++i];
                            else if (rest[i].StartsWith("--password=", StringComparison.Ordinal)) pass = rest[i].Substring(11);
                            else if (rest[i] == "--password-stdin") stdin = true;
                            else if (rest[i] == "--get-login") { io.L(LoggedIn(sh, host ?? RegistryHost) ? RegistryUser : ""); return LoggedIn(sh, RegistryHost) ? 0 : 125; }
                            else if (!rest[i].StartsWith("-", StringComparison.Ordinal)) host = rest[i];
                        }
                        if (host == null) { var sr = SearchRegistries(m); host = sr.FirstOrDefault(); if (host == null) { io.E("Error: please provide a registry to log in to"); return 125; } }
                        host = host.Replace("https://", "").Replace("docker://", "").TrimEnd('/');
                        if (user == null) user = sh.S != null && io.In.IsTty ? sh.S.ReadLine("Username: ", false) : io.In.ReadLine();
                        if (pass == null) pass = stdin ? (io.In.ReadLine() ?? "") : (sh.S != null && io.In.IsTty ? sh.S.ReadLine("Password: ", true) : io.In.ReadLine());
                        if (host != RegistryHost) { io.E("Error: authenticating creds for \"" + host + "\": pinging container registry " + host + ": Get \"https://" + host + "/v2/\": dial tcp: lookup " + host + ": no such host"); return 125; }
                        if (user != RegistryUser || pass != RegistryPass) { io.E("Error: logging into \"" + host + "\": invalid username/password"); return 125; }
                        string f = AuthFile(sh);
                        try
                        {
                            string dir = Vfs.Parent(f);
                            if (!m.V.Exists(dir)) { var n = m.V.EnsureDir(dir, 0x1C0, null); n.Uid = sh.P.Cr.Uid; n.Gid = sh.P.Cr.Gid; }
                            var fn = m.V.Put(f, "{\n\t\"auths\": {\n\t\t\"" + host + "\": {\n\t\t\t\"auth\": \"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass)) + "\"\n\t\t}\n\t}\n}", 0x180, sh.P.Cr.Uid, sh.P.Cr.Gid, null);
                        }
                        catch (VfsEx) { }
                        io.L("Login Succeeded!");
                        return 0;
                    }
                case "logout":
                    m.V.RemoveG(AuthFile(sh));
                    io.L("Removed login credentials for " + (rest.FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) ?? RegistryHost));
                    return 0;
                case "search":
                    {
                        string term = rest.LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) ?? "";
                        bool tags = rest.Contains("--list-tags");
                        string host = term.Contains("/") && term.Split('/')[0].Contains(".") ? term.Split('/')[0] : null;
                        string q = host != null ? term.Substring(host.Length + 1) : term;
                        var regs = host != null ? new List<string> { host } : SearchRegistries(m);
                        if (!regs.Contains(RegistryHost)) { io.E("Error: 1 error occurred:\n\t* couldn't search registry \"" + regs.FirstOrDefault() + "\": pinging container registry " + regs.FirstOrDefault() + ": Get \"https://" + regs.FirstOrDefault() + "/v2/\": dial tcp: lookup " + regs.FirstOrDefault() + ": no such host"); return 125; }
                        if (tags)
                        {
                            io.L("NAME                                TAG");
                            foreach (var i in m.Net.Registry.Where(x => x.Repo == RegistryHost + "/" + q.Split(':')[0])) io.L((i.Repo).PadRight(35) + " " + i.Tag);
                            return 0;
                        }
                        io.L("NAME                                              DESCRIPTION");
                        foreach (var i in m.Net.Registry.Where(x => x.Repo.Contains(q)).GroupBy(x => x.Repo).Select(g => g.First()))
                            io.L(i.Repo.PadRight(49) + " " + (i.Description.Length > 45 ? i.Description.Substring(0, 42) + "..." : i.Description));
                        return 0;
                    }
                case "pull":
                    {
                        var r = rest.FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));
                        if (r == null) { io.E("Error: requires at least 1 arg(s), only received 0"); return 125; }
                        var img = Pull(sh, io, r, rest.Contains("-q") || rest.Contains("--quiet"));
                        if (img == null) return 125;
                        io.L(img.Id);
                        return 0;
                    }
                case "images": case "image":
                    {
                        if (verb == "image" && rest.Count > 0)
                        {
                            var sub = new List<string> { "podman", rest[0] == "ls" || rest[0] == "list" ? "images" : rest[0] == "rm" ? "rmi" : rest[0] };
                            sub.AddRange(rest.Skip(1));
                            return Podman(sh, sub, io);
                        }
                        var mine = m.Images.Where(i => i.Owner == owner).ToList();
                        if (rest.Contains("-q") || rest.Contains("--quiet")) { foreach (var i in mine) io.L(i.Short); return 0; }
                        io.L("REPOSITORY                               TAG         IMAGE ID      CREATED       SIZE");
                        foreach (var i in mine.OrderByDescending(x => x.Created)) io.L(i.Repo.PadRight(40) + " " + i.Tag.PadRight(11) + " " + i.Short + "  " + Ago(m, i.Created).PadRight(13) + " " + Mb(i.Size));
                        return 0;
                    }
                case "rmi":
                    {
                        bool force = rest.Contains("-f") || rest.Contains("--force");
                        int st = 0;
                        foreach (var r in rest.Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
                        {
                            var img = LocalImage(m, owner, r);
                            if (img == null) { io.E("Error: " + r + ": image not known"); st = 1; continue; }
                            var users = m.Containers.Where(c => c.Owner == owner && c.ImageId == img.Id).ToList();
                            if (users.Count > 0 && !force) { io.E("Error: image used by " + users[0].Id + ": image is in use by a container: consider listing external containers and force-removing image"); st = 2; continue; }
                            foreach (var c in users) RemoveC(m, c);
                            m.Images.Remove(img);
                            io.L("Untagged: " + img.Full);
                            if (!m.Images.Any(i => i.Owner == owner && i.Id == img.Id)) io.L("Deleted: " + img.Id);
                        }
                        return st;
                    }
                case "tag":
                    {
                        if (rest.Count < 2) { io.E("Error: at least two arguments are required"); return 125; }
                        var img = LocalImage(m, owner, rest[0]);
                        if (img == null) { io.E("Error: " + rest[0] + ": image not known"); return 125; }
                        foreach (var t in rest.Skip(1))
                        {
                            var n = img.Copy();
                            string name = t, tag = "latest"; int colon = name.LastIndexOf(':');
                            if (colon > 0 && !name.Substring(colon).Contains("/")) { tag = name.Substring(colon + 1); name = name.Substring(0, colon); }
                            if (!name.Contains("/") || !name.Split('/')[0].Contains(".") && name.Split('/')[0] != "localhost") name = "localhost/" + name;
                            n.Repo = name; n.Tag = tag;
                            m.Images.RemoveAll(i => i.Owner == owner && i.Repo == name && i.Tag == tag);
                            m.Images.Add(n);
                        }
                        return 0;
                    }
                case "run": case "create":
                    return PodmanRun(sh, io, rest, verb == "create");
                case "ps": case "container":
                    {
                        if (verb == "container" && rest.Count > 0)
                        {
                            var sub = new List<string> { "podman", rest[0] == "ls" || rest[0] == "list" ? "ps" : rest[0] };
                            sub.AddRange(rest.Skip(1));
                            return Podman(sh, sub, io);
                        }
                        bool all = rest.Contains("-a") || rest.Contains("--all");
                        var list = m.Containers.Where(c => c.Owner == owner && (all || c.Running)).ToList();
                        if (rest.Contains("-q") || rest.Contains("--quiet")) { foreach (var c in list) io.L(c.Short); return 0; }
                        int fi = rest.IndexOf("--format");
                        if (fi >= 0 && fi + 1 < rest.Count)
                        {
                            foreach (var c in list) io.L(Template(m, rest[fi + 1], c, null));
                            return 0;
                        }
                        io.L("CONTAINER ID  IMAGE                                          COMMAND               CREATED         STATUS                     PORTS                   NAMES");
                        foreach (var c in list)
                        {
                            string cmd = c.Cmd.Length > 20 ? c.Cmd.Substring(0, 17) + "..." : c.Cmd;
                            string status = c.Running ? "Up " + Up(m, c.StartedAt) : c.Status == "Created" ? "Created" : c.Status + " " + Ago(m, c.StoppedAt > 0 ? c.StoppedAt : c.StartedAt > 0 ? c.StartedAt : c.Created);
                            string ports = string.Join(", ", c.Ports.Select(p => "0.0.0.0:" + p.Host + "->" + p.Cont + "/" + p.Proto).ToArray());
                            io.L(c.Short + "  " + c.Image.PadRight(46) + " " + cmd.PadRight(21) + " " + Ago(m, c.Created).PadRight(15) + " " + status.PadRight(26) + " " + ports.PadRight(23) + " " + c.Name);
                        }
                        return 0;
                    }
                case "start": case "stop": case "restart": case "kill": case "rm": case "pause": case "unpause":
                    {
                        bool all = rest.Contains("-a") || rest.Contains("--all");
                        bool force = rest.Contains("-f") || rest.Contains("--force");
                        var names = rest.Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
                        int ti = rest.IndexOf("-t"); if (ti >= 0 && ti + 1 < rest.Count) names.Remove(rest[ti + 1]);
                        var targets = all ? m.Containers.Where(c => c.Owner == owner).ToList() : new List<Container>();
                        int st = 0;
                        foreach (var n in names) { var c = FindC(sh, n); if (c == null) { io.E("Error: no container with name or ID \"" + n + "\" found: no such container"); st = 125; continue; } targets.Add(c); }
                        if (targets.Count == 0 && names.Count == 0 && !all) { io.E("Error: you must provide at least one name or id"); return 125; }
                        foreach (var c in targets)
                        {
                            switch (verb)
                            {
                                case "start":
                                    if (!c.Running) { var err = StartC(sh, c, io); if (err != null) { io.E("Error: unable to start container \"" + c.Id + "\": " + err); st = 125; continue; } }
                                    break;
                                case "stop": case "kill": StopC(m, c); break;
                                case "restart": StopC(m, c); { var err = StartC(sh, c, io); if (err != null) { io.E("Error: " + err); st = 125; continue; } } break;
                                case "rm":
                                    if (c.Running && !force) { io.E("Error: cannot remove container " + c.Id + " as it is running - running or paused containers cannot be removed without force: container state improper"); st = 2; continue; }
                                    RemoveC(m, c);
                                    break;
                            }
                            io.L(names.Contains(c.Name) || all ? c.Name : c.Id);
                        }
                        return st;
                    }
                case "logs":
                    {
                        var n = rest.LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));
                        var c = n == null ? null : FindC(sh, n);
                        if (c == null) { io.E("Error: no container with name or ID \"" + n + "\" found: no such container"); return 125; }
                        io.P(c.Logs);
                        if ((rest.Contains("-f") || rest.Contains("--follow")) && c.Running && sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { } }
                        return 0;
                    }
                case "exec":
                    {
                        int i = 0; bool it = false; var env = new List<string>();
                        while (i < rest.Count && rest[i].StartsWith("-", StringComparison.Ordinal)) { if (rest[i].Contains("i") || rest[i].Contains("t")) it = true; if ((rest[i] == "-e" || rest[i] == "--env") && i + 1 < rest.Count) env.Add(rest[++i]); if (rest[i] == "-u" || rest[i] == "--user" || rest[i] == "-w") i++; i++; }
                        if (i >= rest.Count) { io.E("Error: exec requires the name or ID of a container or the --latest flag"); return 125; }
                        var c = FindC(sh, rest[i]);
                        if (c == null) { io.E("Error: no container with name or ID \"" + rest[i] + "\" found: no such container"); return 125; }
                        if (!c.Running) { io.E("Error: can only create exec sessions on running containers: container state improper"); return 125; }
                        var cmd = string.Join(" ", rest.Skip(i + 1).Select(x => x.Contains(" ") ? "'" + x + "'" : x).ToArray());
                        if (cmd.Length == 0) { io.E("Error: must provide a non-empty command to start an exec session: invalid argument"); return 125; }
                        bool shellOnly = cmd == "/bin/bash" || cmd == "bash" || cmd == "sh" || cmd == "/bin/sh";
                        return RunInside(sh, c, shellOnly && it ? "" : cmd, io, it && shellOnly);
                    }
                case "port":
                    {
                        var c = FindC(sh, rest.FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) ?? "");
                        if (c == null) { io.E("Error: no container with name or ID found: no such container"); return 125; }
                        foreach (var p in c.Ports) io.L(p.Cont + "/" + p.Proto + " -> 0.0.0.0:" + p.Host);
                        return 0;
                    }
                case "inspect":
                    {
                        string fmt = null; var names = new List<string>();
                        for (int i = 0; i < rest.Count; i++) { if ((rest[i] == "-f" || rest[i] == "--format") && i + 1 < rest.Count) fmt = rest[++i]; else if (rest[i].StartsWith("--format=", StringComparison.Ordinal)) fmt = rest[i].Substring(9); else if (!rest[i].StartsWith("-", StringComparison.Ordinal)) names.Add(rest[i]); }
                        int st = 0;
                        var objs = new List<string>();
                        foreach (var n in names)
                        {
                            var c = FindC(sh, n);
                            var img = c == null ? LocalImage(m, owner, n) : null;
                            if (c == null && img == null) { io.E("Error: no such object: \"" + n + "\""); st = 125; continue; }
                            if (fmt != null) { io.L(Template(m, fmt, c, img)); continue; }
                            objs.Add(c != null ? ContainerJson(m, c) : ImageJson(img));
                        }
                        if (fmt == null && objs.Count > 0) io.L("[\n" + string.Join(",\n", objs.ToArray()) + "\n]");
                        return st;
                    }
                case "cp":
                    {
                        var ps = rest.Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
                        if (ps.Count != 2) { io.E("Error: accepts 2 arg(s), received " + ps.Count); return 125; }
                        Func<string, Tuple<Container, string>> split = s => { int c0 = s.IndexOf(':'); if (c0 <= 0 || s.StartsWith("/", StringComparison.Ordinal) || s.StartsWith(".", StringComparison.Ordinal)) return Tuple.Create((Container)null, s); return Tuple.Create(FindC(sh, s.Substring(0, c0)), s.Substring(c0 + 1)); };
                        var src = split(ps[0]); var dst = split(ps[1]);
                        if ((ps[0].Contains(":") && src.Item1 == null && !ps[0].StartsWith("/", StringComparison.Ordinal)) || (ps[1].Contains(":") && dst.Item1 == null && !ps[1].StartsWith("/", StringComparison.Ordinal))) { io.E("Error: no such container"); return 125; }
                        var cont = src.Item1 ?? dst.Item1;
                        if (cont == null) { io.E("Error: copying from host to host is not supported; one side must be a container (name:/path)"); return 125; }
                        bool mounted = m.Mounts.Any(x => x.Path == cont.Mount);
                        if (!mounted) m.MountContainer(cont);
                        try
                        {
                            var rootCtx = new PCtx { Cr = new Cred { Uid = 0 }, Root = cont.Mount, Cwd = "/" };
                            if (src.Item1 != null) return CopyTree(sh, io, m, rootCtx, src.Item2, m, sh.P, dst.Item2, true, true, true) ? 0 : 125;
                            return CopyTree(sh, io, m, sh.P, src.Item2, m, rootCtx, dst.Item2, true, true, true) ? 0 : 125;
                        }
                        finally { if (!mounted && !cont.Running) m.UnmountContainer(cont); }
                    }
                case "generate":
                    return PodmanGenerate(sh, io, rest);
                case "build":
                    return PodmanBuild(sh, io, rest);
                case "volume":
                    {
                        string sub = rest.Count > 0 ? rest[0] : "ls";
                        string root = m.StorageRoot(owner) + "/volumes";
                        var vols = new List<string>();
                        try { var d = m.V.G(root); if (d.Exists && d.IsDir) vols = Vfs.Sorted(d.Node.Kids.Keys); } catch (VfsEx) { }
                        if (sub == "create") { string n = rest.Count > 1 ? rest[1] : Hex64("vol" + m.Now + m.Rand(99999)); EnsureStorage(sh); var dn = m.V.EnsureDir(root + "/" + n + "/_data", 0x1ED, "system_u:object_r:container_file_t:s0"); if (owner.Length > 0) { var pw = m.User(owner); dn.Uid = pw.Uid; dn.Gid = pw.Gid; } io.L(n); return 0; }
                        if (sub == "ls" || sub == "list") { io.L("DRIVER      VOLUME NAME"); foreach (var v in vols) io.L("local       " + v); return 0; }
                        if (sub == "inspect") { foreach (var n in rest.Skip(1)) { if (!vols.Contains(n)) { io.E("Error: inspecting object: no such volume " + n); return 125; } io.L("[\n     {\n          \"Name\": \"" + n + "\",\n          \"Driver\": \"local\",\n          \"Mountpoint\": \"" + root + "/" + n + "/_data\",\n          \"Scope\": \"local\"\n     }\n]"); } return 0; }
                        if (sub == "rm") { foreach (var n in rest.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal))) { if (!vols.Contains(n)) { io.E("Error: no volume with name \"" + n + "\" found: no such volume"); return 1; } if (m.Containers.Any(c => c.Owner == owner && c.Volumes.Any(v => v.Named && v.Host.Contains("/volumes/" + n + "/")))) { io.E("Error: volume " + n + " is being used by the following container(s): ...: volume is being used"); return 2; } m.V.RemoveG(root + "/" + n); io.L(n); } return 0; }
                        io.E("Error: unrecognized command `podman volume " + sub + "`");
                        return 125;
                    }
                case "unshare":
                    {
                        if (owner.Length == 0) { io.E("Error: please use unshare with rootless"); return 125; }
                        if (rest.Count >= 2 && rest[0] == "cat" && rest[1] == "/proc/self/uid_map") { io.L("         0       " + sh.P.Cr.Uid.ToString().PadLeft(4) + "          1\n         1     100000      65536"); return 0; }
                        if (rest.Count >= 3 && rest[0] == "chown")
                        {
                            var args = rest.Skip(1).ToList();
                            bool rec = args.Remove("-R");
                            string spec = args[0];
                            var ug = spec.Split(':');
                            Func<string, int, int> map = (s, me) => { int n; if (!int.TryParse(s, out n)) return -1; return n == 0 ? me : 100000 + n - 1; };
                            int uid = map(ug[0], sh.P.Cr.Uid), gid = ug.Length > 1 ? map(ug[1], sh.P.Cr.Gid) : -1;
                            foreach (var p in args.Skip(1))
                            {
                                Loc l; try { l = m.V.Resolve(sh.P, p, true); } catch (VfsEx e) { io.E("chown: " + e.Message); return 1; }
                                if (!l.Exists) { io.E("chown: cannot access '" + p + "': No such file or directory"); return 1; }
                                Action<Inode, FileSys> set = null;
                                set = (n, fs) => { if (uid >= 0) n.Uid = uid; if (gid >= 0) n.Gid = gid; if (rec && n.T == 'd') foreach (var k in n.Kids.Values) { var ch = fs.Node(k); if (ch != null) set(ch, fs); } };
                                set(l.Node, l.Fs);
                            }
                            return 0;
                        }
                        io.E("(Cert Lab: podman unshare runs chown and cat /proc/self/uid_map; other commands run as you)");
                        return sh.RunText(string.Join(" ", rest.ToArray()), io);
                    }
                case "info":
                    io.L("host:\n  arch: amd64\n  hostname: " + m.HostnameNow + "\n  os: linux\n  security:\n    rootless: " + (owner.Length > 0 ? "true" : "false") + "\n    selinuxEnabled: " + (m.SeMode != "disabled" ? "true" : "false") + "\nregistries:\n  search:\n" + string.Join("", SearchRegistries(m).Select(r => "  - " + r + "\n").ToArray()) + "store:\n  graphRoot: " + m.StorageRoot(owner) + "\n  imageStore:\n    number: " + m.Images.Count(i => i.Owner == owner) + "\nversion:\n  Version: 5.2.2");
                    return 0;
                case "system":
                    if (rest.Count > 0 && (rest[0] == "migrate" || rest[0] == "renumber")) return 0;
                    if (rest.Count > 0 && rest[0] == "prune") { foreach (var c in m.Containers.Where(x => x.Owner == owner && !x.Running).ToList()) RemoveC(m, c); io.L("Deleted Containers\nTotal reclaimed space: 0B"); return 0; }
                    io.E("Error: unrecognized command `podman system " + string.Join(" ", rest.ToArray()) + "`");
                    return 125;
                case "help": case "--help": case "-h":
                    io.L("Manage pods, containers and images\n\nUsage:\n  podman [options] [command]\n\nAvailable Commands:\n  build       Build an image using instructions from Containerfiles\n  cp          Copy files/folders between a container and the local filesystem\n  create      Create but do not start a container\n  exec        Run a process in a running container\n  generate    Generate structured data based on containers, pods or volumes\n  images      List images in local storage\n  inspect     Display the configuration of object denoted by ID\n  login       Log in to a container registry\n  logout      Log out of a container registry\n  logs        Fetch the logs of one or more containers\n  port        List port mappings or a specific mapping for the container\n  ps          List containers\n  pull        Pull an image from a registry\n  restart     Restart one or more containers\n  rm          Remove one or more containers\n  rmi         Remove one or more images from local storage\n  run         Run a command in a new container\n  search      Search registry for image\n  start       Start one or more containers\n  stop        Stop one or more containers\n  tag         Add an additional name to a local image\n  unshare     Run a command in a modified user namespace\n  volume      Manage volumes");
                    return 0;
            }
            io.E("Error: unrecognized command `podman " + verb + "`\nTry 'podman --help' for more information");
            return 125;
        }

        static int PodmanRun(Sh sh, Io io, List<string> rest, bool createOnly)
        {
            var m = sh.M;
            string owner = Owner(sh);
            string name = null, restart = "no", user = null, workdir = null;
            bool detach = false, rm = false, it = false, replace = false;
            var ports = new List<string>(); var vols = new List<string>(); var env = new List<string>();
            int i = 0;
            for (; i < rest.Count; i++)
            {
                string s = rest[i];
                if (!s.StartsWith("-", StringComparison.Ordinal)) break;
                Func<string> val = () => { int eq = s.IndexOf('='); if (s.StartsWith("--", StringComparison.Ordinal) && eq > 0) return s.Substring(eq + 1); return i + 1 < rest.Count ? rest[++i] : ""; };
                string key = s.StartsWith("--", StringComparison.Ordinal) && s.Contains("=") ? s.Substring(0, s.IndexOf('=')) : s;
                switch (key)
                {
                    case "-d": case "--detach": detach = true; break;
                    case "--rm": rm = true; break;
                    case "-i": case "-t": case "-it": case "-ti": case "--interactive": case "--tty": it = true; break;
                    case "-dit": case "-itd": case "-dt": case "-td": detach = true; it = true; break;
                    case "--name": name = val(); break;
                    case "-p": case "--publish": ports.Add(val()); break;
                    case "-v": case "--volume": vols.Add(val()); break;
                    case "-e": case "--env": env.Add(val()); break;
                    case "--restart": restart = val(); break;
                    case "-u": case "--user": user = val(); break;
                    case "-w": case "--workdir": workdir = val(); break;
                    case "--replace": replace = true; break;
                    case "--security-opt": case "--network": case "--net": case "--hostname": case "-h": case "--label": case "-l": case "--memory": case "-m": case "--cpus": case "--userns": case "--pull": case "--cidfile": case "--cgroups": case "--sdnotify": case "--log-driver": case "--entrypoint": val(); break;
                    case "-P": case "--privileged": case "--init": case "-q": case "--quiet": break;
                    default:
                        if (s.StartsWith("-p", StringComparison.Ordinal) && s.Length > 2) { ports.Add(s.Substring(2)); break; }
                        if (s.StartsWith("-v", StringComparison.Ordinal) && s.Length > 2) { vols.Add(s.Substring(2)); break; }
                        if (s.StartsWith("-e", StringComparison.Ordinal) && s.Length > 2) { env.Add(s.Substring(2)); break; }
                        io.E("Error: unknown flag: " + s + "\nSee 'podman run --help'");
                        return 125;
                }
            }
            if (i >= rest.Count) { io.E("Error: requires at least 1 arg(s), only received 0"); return 125; }
            string imageRef = rest[i];
            var cmdArgs = rest.Skip(i + 1).ToList();
            EnsureStorage(sh);
            var img = LocalImage(m, owner, imageRef);
            if (img == null)
            {
                List<string> tried; string full = FullRef(m, imageRef, out tried);
                img = full != null ? LocalImage(m, owner, full) : null;
                if (img == null) { img = Pull(sh, io, imageRef, false); if (img == null) return 125; }
            }
            if (name != null)
            {
                var old = m.Containers.FirstOrDefault(x => x.Owner == owner && x.Name == name);
                if (old != null)
                {
                    if (!replace) { io.E("Error: creating container storage: the container name \"" + name + "\" is already in use by " + old.Id + ". You have to remove that container to be able to reuse that name: that name is already in use, or use --replace to instruct Podman to do so."); return 125; }
                    RemoveC(m, old);
                }
            }
            string id = Hex64(m.Name + owner + (name ?? "") + m.Now + m.Rand(1000000));
            if (name == null) { string[] adj = { "happy", "brave", "quirky", "eager", "gallant", "jolly", "sharp", "zen" }, noun = { "turing", "hopper", "lovelace", "curie", "noether", "babbage", "ritchie", "torvalds" }; name = adj[m.Rand(adj.Length)] + "_" + noun[m.Rand(noun.Length)]; }
            var c = new Container { Id = id, Name = name, Image = img.Full, ImageId = img.Id, Owner = owner, Kind = img.Kind, Created = m.Now, AutoRemove = rm, Restart = restart, User = user ?? img.User };
            c.Cmd = cmdArgs.Count > 0 ? string.Join(" ", cmdArgs.Select(x => x.Contains(" ") ? "'" + x + "'" : x).ToArray()) : img.Cmd;
            c.Env.AddRange(img.Env); c.Env.AddRange(env);
            foreach (var p in ports)
            {
                var pp = p.Split('/'); string proto = pp.Length > 1 ? pp[1] : "tcp";
                var parts = pp[0].Split(':');
                int h, ct;
                if (parts.Length == 1) { if (!int.TryParse(parts[0], out ct)) { io.E("Error: invalid port format"); return 125; } h = ct; }
                else if (!int.TryParse(parts[parts.Length - 2], out h) || !int.TryParse(parts[parts.Length - 1], out ct)) { io.E("Error: invalid containerPort: " + parts[parts.Length - 1]); return 125; }
                c.Ports.Add(new CPort { Host = h, Cont = ct, Proto = proto });
            }
            c.FsId = m.ContainerFs(img, id).Id;
            foreach (var v in vols) { var err = AddVolume(sh, c, v); if (err != null) { m.Fss.Remove(c.FsId); io.E("Error: " + err); return 125; } }
            m.Containers.Add(c);
            if (createOnly) { io.L(id); return 0; }
            var e2 = StartC(sh, c, io);
            if (e2 != null)
            {
                c.Status = "Created";
                io.E("Error: " + e2);
                if (rm) RemoveC(m, c);
                return 126;
            }
            if (detach) { io.L(id); return 0; }
            int st;
            if (c.Running)
            {
                if (it && (c.Cmd == "/bin/bash" || c.Cmd == "bash" || c.Cmd == "sh")) { st = RunInside(sh, c, "", io, true); StopC(m, c); }
                else
                {
                    // a server in the foreground: its log until Ctrl+C
                    io.P(c.Logs);
                    if (sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { } }
                    StopC(m, c); st = 0;
                }
            }
            else if (it && (c.Cmd == "/bin/bash" || c.Cmd == "bash" || c.Cmd == "sh" || c.Cmd == "/bin/sh"))
            {
                st = RunInside(sh, c, "", io, true);
                c.Status = "Exited (" + st + ")";
            }
            else { io.P(c.Logs); st = c.ExitCode; }
            if (rm) RemoveC(m, c);
            return st;
        }

        static string Template(Machine m, string fmt, Container c, CImage img)
        {
            return Regex.Replace(fmt, "\\{\\{\\s*([^}]*?)\\s*\\}\\}", mm =>
            {
                string k = mm.Groups[1].Value.Trim();
                if (k.StartsWith("json ", StringComparison.Ordinal)) k = k.Substring(5).Trim();
                if (c != null)
                {
                    var im = m.Images.FirstOrDefault(i => i.Id == c.ImageId);
                    switch (k)
                    {
                        case ".Names": case ".Name": return c.Name;
                        case ".ID": case ".Id": return c.Short;
                        case ".Image": case ".ImageName": return c.Image;
                        case ".Status": return c.Running ? "Up " + Up(m, c.StartedAt) : c.Status;
                        case ".State.Status": case ".State": return c.Running ? "running" : c.Status == "Created" ? "created" : "exited";
                        case ".State.Running": return c.Running ? "true" : "false";
                        case ".State.ExitCode": return c.ExitCode.ToString();
                        case ".Ports": return string.Join(", ", c.Ports.Select(p => "0.0.0.0:" + p.Host + "->" + p.Cont + "/" + p.Proto).ToArray());
                        case ".Config.User": return c.User;
                        case ".Config.Env": return "[" + string.Join(" ", c.Env.ToArray()) + "]";
                        case ".Config.Cmd": case ".Command": return "[" + c.Cmd + "]";
                        case ".HostConfig.RestartPolicy.Name": return c.Restart;
                        case ".Mounts": return "[" + string.Join(" ", c.Volumes.Select(v => "{bind  " + v.Host + " " + v.Cont + "  [" + v.Opts + "] true}").ToArray()) + "]";
                        case ".NetworkSettings.IPAddress": return c.Owner.Length > 0 ? "" : "10.88.0." + (2 + m.Containers.IndexOf(c));
                    }
                    return "<no value>";
                }
                switch (k)
                {
                    case ".Id": case ".ID": return img.Id;
                    case ".Config.User": case ".User": return img.User;
                    case ".Config.Cmd": return "[" + img.Cmd + "]";
                    case ".Config.ExposedPorts": return "map[" + string.Join(" ", img.Exposed.Select(x => x + ":{}").ToArray()) + "]";
                    case ".Config.Env": return "[" + string.Join(" ", img.Env.ToArray()) + "]";
                    case ".Config.WorkingDir": return img.WorkDir;
                    case ".Config.Labels": case ".Labels": return "map[" + string.Join(" ", img.Labels.Select(x => x.Replace("=", ":")).ToArray()) + "]";
                    case ".RepoTags": return "[" + img.Full + "]";
                    case ".Size": return img.Size.ToString();
                    case ".Architecture": return "amd64";
                    case ".Os": return "linux";
                }
                if (k.StartsWith(".Config.Labels.", StringComparison.Ordinal) || k.StartsWith("index .Config.Labels ", StringComparison.Ordinal))
                {
                    string lk = k.StartsWith(".Config.Labels.", StringComparison.Ordinal) ? k.Substring(15) : k.Substring(21).Trim('"');
                    var l = img.Labels.FirstOrDefault(x => x.StartsWith(lk + "=", StringComparison.Ordinal));
                    return l == null ? "<no value>" : l.Substring(lk.Length + 1);
                }
                return "<no value>";
            });
        }

        static string J(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        static string ImageJson(CImage i)
        {
            return "     {\n          \"Id\": " + J(i.Id) + ",\n          \"RepoTags\": [\n               " + J(i.Full) + "\n          ],\n          \"Created\": " + J(DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(i.Created), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) + ",\n          \"Config\": {\n               \"User\": " + J(i.User) + ",\n               \"ExposedPorts\": {" + string.Join(",", i.Exposed.Select(x => "\n                    " + J(x) + ": {}").ToArray()) + (i.Exposed.Count > 0 ? "\n               " : "") + "},\n               \"Env\": [\n" + string.Join(",\n", i.Env.Select(x => "                    " + J(x)).ToArray()) + "\n               ],\n               \"Cmd\": [\n                    " + J(i.Cmd) + "\n               ],\n               \"WorkingDir\": " + J(i.WorkDir) + ",\n               \"Labels\": {\n" + string.Join(",\n", i.Labels.Select(x => "                    " + J(x.Split('=')[0]) + ": " + J(x.Substring(x.IndexOf('=') + 1))).ToArray()) + "\n               }\n          },\n          \"Architecture\": \"amd64\",\n          \"Os\": \"linux\",\n          \"Size\": " + i.Size + "\n     }";
        }
        static string ContainerJson(Machine m, Container c)
        {
            return "     {\n          \"Id\": " + J(c.Id) + ",\n          \"Created\": " + J(DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(c.Created), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) + ",\n          \"State\": {\n               \"Status\": " + J(c.Running ? "running" : c.Status == "Created" ? "created" : "exited") + ",\n               \"Running\": " + (c.Running ? "true" : "false") + ",\n               \"ExitCode\": " + c.ExitCode + "\n          },\n          \"Image\": " + J(c.ImageId) + ",\n          \"ImageName\": " + J(c.Image) + ",\n          \"Name\": " + J(c.Name) + ",\n          \"Mounts\": [" + string.Join(",", c.Volumes.Select(v => "\n               {\n                    \"Type\": \"" + (v.Named ? "volume" : "bind") + "\",\n                    \"Source\": " + J(v.Host) + ",\n                    \"Destination\": " + J(v.Cont) + ",\n                    \"Options\": [" + (v.Opts.Length > 0 ? J(v.Opts) : "") + "]\n               }").ToArray()) + (c.Volumes.Count > 0 ? "\n          " : "") + "],\n          \"HostConfig\": {\n               \"RestartPolicy\": {\n                    \"Name\": " + J(c.Restart) + "\n               },\n               \"PortBindings\": {" + string.Join(",", c.Ports.Select(p => "\n                    \"" + p.Cont + "/" + p.Proto + "\": [ { \"HostIp\": \"\", \"HostPort\": \"" + p.Host + "\" } ]").ToArray()) + (c.Ports.Count > 0 ? "\n               " : "") + "}\n          },\n          \"Config\": {\n               \"User\": " + J(c.User) + ",\n               \"Env\": [\n" + string.Join(",\n", c.Env.Select(x => "                    " + J(x)).ToArray()) + "\n               ],\n               \"Cmd\": [\n                    " + J(c.Cmd) + "\n               ]\n          }\n     }";
        }

        static int PodmanGenerate(Sh sh, Io io, List<string> rest)
        {
            var m = sh.M;
            if (rest.Count == 0 || rest[0] != "systemd") { io.E("Error: unrecognized command `podman generate " + string.Join(" ", rest.ToArray()) + "`"); return 125; }
            bool files = rest.Contains("--files") || rest.Contains("-f"), useName = rest.Contains("--name") || rest.Contains("-n"), isNew = rest.Contains("--new");
            var names = rest.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            int ti = rest.IndexOf("--restart-policy");
            if (names.Count == 0) { io.E("Error: exactly one argument is required"); return 125; }
            var c = FindC(sh, names[0]);
            if (c == null) { io.E("Error: " + names[0] + " does not refer to a container or pod: no pod with name or ID " + names[0] + " found: no such pod: no container with name or ID \"" + names[0] + "\" found: no such container"); return 125; }
            io.E("DEPRECATED command:\nIt is recommended to use Quadlets for running containers and pods under systemd.\n\nPlease refer to podman-systemd.unit(5) for details.");
            string unit = "container-" + (useName ? c.Name : c.Id) + ".service";
            var pw = m.UserById(sh.P.Cr.Uid);
            string run = sh.P.Cr.Uid == 0 ? "/run" : "/run/user/" + sh.P.Cr.Uid;
            var d = m.LocalTime(m.Now);
            var sb = new StringBuilder();
            sb.Append("# " + unit + "\n# autogenerated by Podman 5.2.2\n# " + d.ToString("ddd MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString().PadLeft(2) + " " + d.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, m.Now) + " " + d.Year + "\n\n");
            sb.Append("[Unit]\nDescription=Podman " + unit + "\nDocumentation=man:podman-generate-systemd(1)\nWants=network-online.target\nAfter=network-online.target\nRequiresMountsFor=" + run + "/containers\n\n[Service]\nEnvironment=PODMAN_SYSTEMD_UNIT=%n\nRestart=on-failure\nTimeoutStopSec=70\n");
            if (isNew)
            {
                var args = new StringBuilder("/usr/bin/podman run \\\n\t--cidfile=%t/%n.ctr-id \\\n\t--cgroups=no-conmon \\\n\t--rm \\\n\t--sdnotify=conmon \\\n\t--replace \\\n\t-d \\\n\t--name " + c.Name);
                foreach (var p in c.Ports) args.Append(" \\\n\t-p " + p.Host + ":" + p.Cont);
                foreach (var v in c.Volumes) args.Append(" \\\n\t-v " + (v.Named ? v.Host.Split('/').Reverse().Skip(1).First() : v.Host) + ":" + v.Cont + (v.Opts.Length > 0 ? ":" + v.Opts : ""));
                var im = m.Images.FirstOrDefault(i => i.Id == c.ImageId);
                foreach (var e in c.Env.Where(x => im == null || !im.Env.Contains(x))) args.Append(" \\\n\t-e " + e);
                args.Append(" " + c.Image);
                if (im != null && c.Cmd != im.Cmd) args.Append(" " + c.Cmd);
                sb.Append("ExecStart=" + args + "\nExecStop=/usr/bin/podman stop \\\n\t--ignore -t 10 \\\n\t--cidfile=%t/%n.ctr-id\nExecStopPost=/usr/bin/podman rm \\\n\t-f \\\n\t--ignore -t 10 \\\n\t--cidfile=%t/%n.ctr-id\nType=notify\nNotifyAccess=all\n");
            }
            else
                sb.Append("ExecStart=/usr/bin/podman start " + c.Name + "\nExecStop=/usr/bin/podman stop  \\\n\t-t 10 " + c.Name + "\nExecStopPost=/usr/bin/podman stop  \\\n\t-t 10 " + c.Name + "\nPIDFile=" + run + "/containers/overlay-containers/" + c.Id + "/userdata/conmon.pid\nType=forking\n");
            sb.Append("\n[Install]\nWantedBy=default.target\n");
            if (!files) { io.P(sb.ToString()); return 0; }
            try
            {
                var n = m.V.WriteText(sh.P, unit, sb.ToString(), false);
                io.L(Vfs.Norm(Vfs.Join(sh.P.Cwd, unit)));
            }
            catch (VfsEx e) { io.E("Error: open " + unit + ": " + e.Message.ToLowerInvariant()); return 125; }
            return 0;
        }

        /// <summary>podman build: FROM, RUN, COPY / ADD, ENV, EXPOSE, USER, WORKDIR, CMD, ENTRYPOINT, LABEL.</summary>
        static int PodmanBuild(Sh sh, Io io, List<string> rest)
        {
            var m = sh.M;
            string owner = Owner(sh);
            string tag = null, file = null, ctxDir = null;
            for (int i = 0; i < rest.Count; i++)
            {
                if ((rest[i] == "-t" || rest[i] == "--tag") && i + 1 < rest.Count) tag = rest[++i];
                else if (rest[i].StartsWith("--tag=", StringComparison.Ordinal)) tag = rest[i].Substring(6);
                else if ((rest[i] == "-f" || rest[i] == "--file") && i + 1 < rest.Count) file = rest[++i];
                else if (rest[i] == "--layers" || rest[i] == "--no-cache" || rest[i] == "-q") { }
                else if (!rest[i].StartsWith("-", StringComparison.Ordinal)) ctxDir = rest[i];
            }
            ctxDir = Vfs.Norm(Vfs.Join(sh.P.Cwd, ctxDir ?? "."));
            string cf = file != null ? Vfs.Norm(Vfs.Join(sh.P.Cwd, file)) : null;
            if (cf == null) foreach (var n in new[] { "Containerfile", "Dockerfile" }) if (m.V.Exists(Vfs.Global(sh.P, ctxDir + "/" + n))) { cf = ctxDir + "/" + n; break; }
            if (cf == null) { io.E("Error: no Containerfile or Dockerfile specified or found in context directory, " + ctxDir + ": no such file or directory"); return 125; }
            string text;
            try { text = m.V.ReadText(sh.P, cf); } catch (VfsEx e) { io.E("Error: " + cf + ": " + e.Message); return 125; }
            var lines = text.Replace("\\\n", " ").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).ToList();
            if (lines.Count == 0 || !lines[0].StartsWith("FROM", StringComparison.OrdinalIgnoreCase)) { io.E("Error: no FROM statement found"); return 125; }
            CImage img = null;
            Container tmp = null;
            int step = 0;
            try
            {
                foreach (var line in lines)
                {
                    step++;
                    int sp = line.IndexOf(' ');
                    string ins = (sp < 0 ? line : line.Substring(0, sp)).ToUpperInvariant(), arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                    io.L("STEP " + step + "/" + lines.Count + ": " + ins + " " + arg);
                    if (ins == "FROM")
                    {
                        var baseImg = LocalImage(m, owner, arg);
                        if (baseImg == null) { List<string> tried; var full = FullRef(m, arg, out tried); baseImg = full != null ? LocalImage(m, owner, full) : null; }
                        if (baseImg == null) baseImg = Pull(sh, io, arg, false);
                        if (baseImg == null) return 125;
                        img = baseImg.Copy();
                        string tid = Hex64("build" + m.Now + m.Rand(1000000));
                        tmp = new Container { Id = tid, Name = "build-" + tid.Substring(0, 8), Owner = owner, Kind = img.Kind };
                        tmp.FsId = m.ContainerFs(img, tid).Id;
                        m.MountContainer(tmp);
                        continue;
                    }
                    if (img == null) { io.E("Error: no FROM statement found"); return 125; }
                    Func<string, List<string>> jsonOrWords = s => s.StartsWith("[", StringComparison.Ordinal) ? Regex.Matches(s, "\"((?:[^\"\\\\]|\\\\.)*)\"").Cast<Match>().Select(x => x.Groups[1].Value).ToList() : new List<string> { s };
                    switch (ins)
                    {
                        case "RUN":
                            {
                                string cmd = string.Join(" ", jsonOrWords(arg).ToArray());
                                if (Regex.IsMatch(cmd, "^(dnf|yum|microdnf)\\b"))
                                {
                                    var pk = Regex.Matches(cmd, "install\\s+(?:-y\\s+)?([\\w .+-]+)").Cast<Match>().SelectMany(x => x.Groups[1].Value.Split(' ')).Where(x => x.Length > 0 && x != "-y" && !x.StartsWith("&&", StringComparison.Ordinal)).ToList();
                                    io.L("Updating Subscription Management repositories.\nThis system has release set to 9 and it receives updates only for this release.\n(Cert Lab: the container installs " + (pk.Count > 0 ? string.Join(", ", pk.ToArray()) : "the packages") + " from the UBI repositories.)\nComplete!");
                                    break;
                                }
                                var buf = new BufSink();
                                int st = RunInside(sh, tmp, cmd, new Io { In = new StrSrc(""), Out = buf, Err = buf }, false);
                                io.P(buf.Text);
                                if (st != 0) { io.E("Error: building at STEP \"RUN " + arg + "\": while running runtime: exit status " + st); return st; }
                                break;
                            }
                        case "COPY": case "ADD":
                            {
                                var parts = jsonOrWords(arg); if (parts.Count == 1) parts = Regex.Split(arg, "\\s+").Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
                                if (parts.Count < 2) { io.E("Error: " + ins + " requires at least two arguments"); return 125; }
                                string dest = parts[parts.Count - 1];
                                var rootCtx = new PCtx { Cr = new Cred { Uid = 0 }, Root = tmp.Mount, Cwd = img.WorkDir };
                                foreach (var src in parts.Take(parts.Count - 1))
                                {
                                    string sp2 = ctxDir + "/" + src;
                                    if (!m.V.Exists(Vfs.Global(sh.P, sp2))) { io.E("Error: building at STEP \"" + ins + " " + arg + "\": checking on sources under \"" + ctxDir + "\": copier: stat: \"/" + src + "\": no such file or directory"); return 125; }
                                    try { var dl = m.V.Resolve(rootCtx, dest.EndsWith("/", StringComparison.Ordinal) ? dest : Vfs.Parent(dest), true); if (!dl.Exists) m.V.Mkdir(rootCtx, dest.EndsWith("/", StringComparison.Ordinal) ? dest : Vfs.Parent(dest), 0x1ED, true); } catch (VfsEx) { }
                                    if (!CopyTree(sh, io, m, sh.P, sp2, m, rootCtx, dest, true, true, true)) return 125;
                                }
                                break;
                            }
                        case "ENV":
                            {
                                var mm = Regex.Match(arg, "^(\\w+)[= ](.*)$");
                                if (mm.Success) { img.Env.RemoveAll(x => x.StartsWith(mm.Groups[1].Value + "=", StringComparison.Ordinal)); img.Env.Add(mm.Groups[1].Value + "=" + mm.Groups[2].Value.Trim().Trim('"')); }
                                break;
                            }
                        case "EXPOSE": foreach (var p in arg.Split(' ').Where(x => x.Length > 0)) { var e = p.Contains("/") ? p : p + "/tcp"; if (!img.Exposed.Contains(e)) img.Exposed.Add(e); } break;
                        case "USER": img.User = arg; break;
                        case "WORKDIR": img.WorkDir = arg; try { m.V.Mkdir(new PCtx { Cr = new Cred { Uid = 0 }, Root = tmp.Mount, Cwd = "/" }, arg, 0x1ED, true); } catch (VfsEx) { } break;
                        case "CMD": case "ENTRYPOINT": img.Cmd = string.Join(" ", jsonOrWords(arg).ToArray()); if (img.Kind == "httpd" && !img.Cmd.Contains("httpd")) img.Kind = "ubi"; if (img.Cmd.Contains("httpd -D") || img.Cmd.Contains("run-httpd")) img.Kind = "httpd"; break;
                        case "LABEL": foreach (Match lm in Regex.Matches(arg, "(\\S+?)=(\"[^\"]*\"|\\S+)")) { img.Labels.RemoveAll(x => x.StartsWith(lm.Groups[1].Value + "=", StringComparison.Ordinal)); img.Labels.Add(lm.Groups[1].Value + "=" + lm.Groups[2].Value.Trim('"')); } break;
                        case "MAINTAINER": img.Labels.Add("maintainer=" + arg); break;
                        case "VOLUME": case "ARG": case "SHELL": case "STOPSIGNAL": case "HEALTHCHECK": case "ONBUILD": break;
                        default: io.E("Error: building at STEP \"" + ins + "\": unknown instruction: " + ins); return 125;
                    }
                }
                // what the steps changed becomes part of the image
                var fresh = m.ContainerFs(img, "cmp" + Hex64(tmp.Id));
                var fs = m.V.Fs(tmp.FsId);
                img.ExtraFiles = new List<string>();
                Action<Inode, Inode, string> diff = null;
                diff = (n, f, path) =>
                {
                    if (n.T == 'd') { foreach (var kv in n.Kids) { var ch = fs.Node(kv.Value); int fi; var fch = f != null && f.T == 'd' && f.Kids.TryGetValue(kv.Key, out fi) ? fresh.Node(fi) : null; if (ch != null) diff(ch, fch, path + "/" + kv.Key); } return; }
                    if (n.T == 'f' && n.Text != null && (f == null || f.Text != n.Text)) img.ExtraFiles.Add(path + "\t" + n.Text);
                };
                diff(fs.Root, fresh.Root, "");
                m.Fss.Remove(fresh.Id);
                string name = tag ?? "";
                string repo = name.Length == 0 ? "<none>" : name.Contains(":") && !name.Substring(name.LastIndexOf(':')).Contains("/") ? name.Substring(0, name.LastIndexOf(':')) : name;
                string t2 = name.Length == 0 ? "<none>" : name.Contains(":") && !name.Substring(name.LastIndexOf(':')).Contains("/") ? name.Substring(name.LastIndexOf(':') + 1) : "latest";
                if (repo != "<none>" && (!repo.Contains("/") || (!repo.Split('/')[0].Contains(".") && repo.Split('/')[0] != "localhost"))) repo = "localhost/" + repo;
                img.Repo = repo; img.Tag = t2; img.Owner = owner; img.Created = m.Now;
                img.Id = Hex64("img" + string.Join("|", img.ExtraFiles.ToArray()) + img.Cmd + m.Now);
                img.Size += img.ExtraFiles.Sum(x => x.Length) + 4096;
                m.Images.RemoveAll(i => i.Owner == owner && i.Repo == repo && i.Tag == t2);
                m.Images.Add(img);
                io.L("COMMIT " + (tag ?? "") + "\n--> " + img.Id.Substring(0, 11) + (tag != null ? "\nSuccessfully tagged " + repo + ":" + t2 : "") + "\n" + img.Id);
                return 0;
            }
            finally
            {
                if (tmp != null) { m.UnmountContainer(tmp); m.Fss.Remove(tmp.FsId); }
            }
        }

        // ------------------------------------------------------------ skopeo
        static int Skopeo(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count < 2) { io.E("Various operations with container images and container image registries\n\nUsage:\n  skopeo [flags]\n  skopeo [command]\n\nAvailable Commands:\n  copy, delete, inspect, list-tags, login, logout, sync"); return 1; }
            string verb = a[1];
            var rest = a.Skip(2).ToList();
            if (verb == "login" || verb == "logout") { var b = new List<string> { "podman", verb }; b.AddRange(rest); return Podman(sh, b, io); }
            string target = rest.LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));
            if (verb == "inspect" || verb == "list-tags")
            {
                if (target == null || !target.StartsWith("docker://", StringComparison.Ordinal)) { io.E("FATA[0000] Invalid source name " + target + ": Invalid image name \"" + target + "\", expected colon-separated transport:reference"); return 1; }
                string r = target.Substring(9);
                List<string> tried; string full = FullRef(m, r, out tried) ?? (tried.Count > 0 ? tried[0] : r);
                string host = full.Split('/')[0];
                string repo = full.Substring(0, full.LastIndexOf(':')), tag = full.Substring(full.LastIndexOf(':') + 1);
                if (host != RegistryHost) { io.E("FATA[0000] Error parsing image name \"" + target + "\": pinging container registry " + host + ": Get \"https://" + host + "/v2/\": dial tcp: lookup " + host + ": no such host"); return 1; }
                bool creds = rest.Contains("--creds") && rest[rest.IndexOf("--creds") + 1] == RegistryUser + ":" + RegistryPass;
                if (!LoggedIn(sh, host) && !creds) { io.E("FATA[0000] Error parsing image name \"" + target + "\": unable to retrieve auth token: invalid username/password: unauthorized: authentication required"); return 1; }
                if (verb == "list-tags")
                {
                    var tags = m.Net.Registry.Where(i => i.Repo == (r.Contains(":") ? repo : full.Substring(0, full.LastIndexOf(':')))).Select(i => i.Tag).ToList();
                    io.L("{\n    \"Repository\": \"" + repo + "\",\n    \"Tags\": [\n" + string.Join(",\n", tags.Select(t => "        \"" + t + "\"").ToArray()) + "\n    ]\n}");
                    return 0;
                }
                var img = m.Net.Registry.FirstOrDefault(i => i.Repo == repo && i.Tag == tag);
                if (img == null) { io.E("FATA[0000] Error parsing image name \"" + target + "\": reading manifest " + tag + " in " + repo + ": manifest unknown"); return 1; }
                int fi = rest.IndexOf("--format"); if (fi < 0) fi = rest.IndexOf("-f");
                if (fi >= 0 && fi + 1 < rest.Count) { io.L(Template(m, rest[fi + 1].Replace(".Labels", ".Config.Labels").Replace(".Config.Config.Labels", ".Config.Labels"), null, img)); return 0; }
                io.L("{\n    \"Name\": \"" + repo + "\",\n    \"Digest\": \"sha256:" + Hex64(img.Id) + "\",\n    \"RepoTags\": [\n" + string.Join(",\n", m.Net.Registry.Where(i => i.Repo == repo).Select(i => "        \"" + i.Tag + "\"").ToArray()) + "\n    ],\n    \"Created\": \"" + DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(img.Created), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\",\n    \"DockerVersion\": \"\",\n    \"Labels\": {\n" + string.Join(",\n", img.Labels.Select(x => "        " + J(x.Split('=')[0]) + ": " + J(x.Substring(x.IndexOf('=') + 1))).ToArray()) + "\n    },\n    \"Architecture\": \"amd64\",\n    \"Os\": \"linux\",\n    \"Layers\": [\n        \"sha256:" + Hex64(img.Id + "layer") + "\"\n    ],\n    \"Env\": [\n" + string.Join(",\n", img.Env.Select(x => "        " + J(x)).ToArray()) + "\n    ]\n}");
                return 0;
            }
            if (verb == "copy")
            {
                var ps = rest.Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
                if (ps.Count != 2) { io.E("FATA[0000] Exactly two arguments expected"); return 1; }
                if (ps[0].StartsWith("docker://", StringComparison.Ordinal) && ps[1].StartsWith("containers-storage:", StringComparison.Ordinal))
                {
                    var img = Pull(sh, io, ps[0].Substring(9), false);
                    if (img == null) return 1;
                    string to = ps[1].Substring(19);
                    if (to.Length > 0 && to != img.Full) Podman(sh, new List<string> { "podman", "tag", img.Full, to }, io);
                    return 0;
                }
                if (ps[0].StartsWith("docker://", StringComparison.Ordinal) && ps[1].StartsWith("dir:", StringComparison.Ordinal))
                {
                    List<string> tried; string full = FullRef(m, ps[0].Substring(9), out tried) ?? (tried.Count > 0 ? tried[0] : "");
                    if (!LoggedIn(sh, RegistryHost)) { io.E("FATA[0000] initializing source docker://" + full + ": unauthorized: authentication required"); return 1; }
                    string dir = ps[1].Substring(4);
                    try { m.V.Mkdir(sh.P, dir, 0x1ED, true); m.V.WriteText(sh.P, dir + "/manifest.json", "{\"schemaVersion\":2}\n", false); m.V.WriteText(sh.P, dir + "/version", "Directory Transport Version: 1.1\n", false); }
                    catch (VfsEx e) { io.E("FATA[0000] " + e.Message); return 1; }
                    io.L("Getting image source signatures\nCopying blob done\nCopying config done\nWriting manifest to image destination");
                    return 0;
                }
                io.E("(Cert Lab: skopeo copy works from docker://... to containers-storage: or dir:)");
                return 1;
            }
            io.E("Error: unknown command \"" + verb + "\" for \"skopeo\"");
            return 1;
        }
    }
}
