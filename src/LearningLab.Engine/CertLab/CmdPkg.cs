// Cert Lab - dnf (yum) and rpm. Repositories come from /etc/yum.repos.d/*.repo like on RHEL: a baseurl on the
// mounted DVD (file:///mnt/BaseOS) or on the utility server (http://repo.lab.example.com/AppStream). A repository
// that cannot be read stops dnf with the same kind of message the real one prints, so broken repo files and
// unmounted DVDs are found the same way. Transactions are recorded for dnf history.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class Repo
    {
        public string Id = "", Name = "", File = "", BaseUrl = "", GpgKey = "";
        public bool Enabled = true, GpgCheck = true;
        public string Content;   // BaseOS / AppStream when the repository was found, else null
        public string Error;     // why it could not be read
    }

    public partial class Machine
    {
        public const string DnfHistory = "/var/lib/dnf/certlab-history";
        public const string RpmKeys = "/var/lib/rpm/certlab-keys";

        public void SetPkgReason(string name, string reason)
        {
            var db = InstalledPackages();
            string[] r;
            if (!db.TryGetValue(name, out r)) return;
            var l = r.Take(5).ToList(); while (l.Count < 5) l.Add("");
            l.Add(reason);
            db[name] = l.ToArray();
            WriteRpmDb(db);
        }
        public string PkgReason(string name)
        {
            string[] r;
            return InstalledPackages().TryGetValue(name, out r) && r.Length > 5 ? r[5] : "user";
        }
    }

    public static partial class Cmd
    {
        static readonly string[][] PkgGroups =
        {
            new[] { "Basic Web Server", "web-server", "These tools allow you to run a Web server on the system.", "httpd,mod_ssl,httpd-manual,httpd-tools" },
            new[] { "Container Management", "container-management", "Tools for managing Linux containers", "podman,buildah,skopeo,containers-common,crun,conmon" },
            new[] { "Headless Management", "headless-management", "Tools for managing the system without an attached graphical console.", "cockpit,cockpit-ws,cockpit-system" },
            new[] { "System Tools", "system-tools", "This group is a collection of various tools for the system, such as the client for connecting to SMB shares and tools to monitor network traffic.", "bind-utils,tree,lsof,tmux,nmap-ncat,tcpdump,sysstat,zsh" },
            new[] { "File and Storage Server", "file-server", "CIFS, SMB, NFS, iSCSI, iSER, and iSNS network storage server.", "nfs-utils,autofs" },
            new[] { "Network File System Client", "network-file-system-client", "Enables the system to attach to network storage.", "nfs-utils,autofs" },
            new[] { "Performance Tools", "performance", "Tools for diagnosing system and application-level performance problems.", "sysstat,tuned,stress-ng" },
        };
        static readonly string[] Protected = { "dnf", "systemd", "sudo", "kernel", "bash", "rpm", "coreutils", "redhat-release" };

        static void RegPkg()
        {
            P("dnf", Dnf); P("yum", Dnf); P("dnf-3", Dnf); P("rpm", Rpm);
        }

        static string VerOf(Pkg p) { return p.Ver.Substring(0, p.Ver.IndexOf('-') < 0 ? p.Ver.Length : p.Ver.IndexOf('-')); }
        static string RelOf(Pkg p) { int i = p.Ver.IndexOf('-'); return i < 0 ? "" : p.Ver.Substring(i + 1); }
        static string KSize(long kb) { return kb >= 1024 ? (kb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " M" : kb + " k"; }

        // ------------------------------------------------------------ repositories
        static Dictionary<string, string> Ini(string text, List<string> order)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            string sec = "";
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) { sec = line.Substring(1, line.Length - 2).Trim(); if (order != null && !order.Contains(sec)) order.Add(sec); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) { d[sec + "\u0001!bad"] = line; continue; }
                d[sec + "\u0001" + line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        static bool Truthy(string v, bool def) { if (v == null) return def; v = v.ToLowerInvariant(); return v == "1" || v == "yes" || v == "true" || v == "on"; }

        public static List<Repo> LoadRepos(Machine m, out string fileError)
        {
            fileError = null;
            var list = new List<Repo>();
            var main = Ini(m.V.Cat("/etc/dnf/dnf.conf"), null);
            string mg; bool gpgDefault = !main.TryGetValue("main\u0001gpgcheck", out mg) || Truthy(mg, true);
            try
            {
                var d = m.V.G("/etc/yum.repos.d");
                if (!d.Exists || !d.IsDir) return list;
                foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                {
                    if (!k.EndsWith(".repo", StringComparison.Ordinal)) continue;
                    string path = "/etc/yum.repos.d/" + k;
                    var text = m.V.Cat(path);
                    if (text == null) continue;
                    var order = new List<string>();
                    var ini = Ini(text, order);
                    foreach (var kv in ini) if (kv.Key.EndsWith("\u0001!bad", StringComparison.Ordinal) && fileError == null) fileError = "Parsing file \"" + path + "\" failed: Parsing file '" + path + "' failed: IniParser: Missing '=' at line " + (Array.IndexOf(text.Split('\n'), text.Split('\n').First(l => l.Trim() == kv.Value)) + 1);
                    if (ini.Keys.Any(x => x.StartsWith("\u0001", StringComparison.Ordinal)) && fileError == null) fileError = "Parsing file \"" + path + "\" failed: Parsing file '" + path + "' failed: IniParser: Missing section header at line 1";
                    foreach (var sec in order)
                    {
                        if (sec == "main") continue;
                        Func<string, string> g = key => { string v; return ini.TryGetValue(sec + "\u0001" + key, out v) ? v : null; };
                        var r = new Repo { Id = sec, File = path, Name = g("name") ?? sec, BaseUrl = (g("baseurl") ?? g("mirrorlist") ?? g("metalink") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "", Enabled = Truthy(g("enabled"), true), GpgCheck = Truthy(g("gpgcheck"), gpgDefault), GpgKey = g("gpgkey") ?? "" };
                        if (Regex.IsMatch(sec, "[^A-Za-z0-9_.:-]") && fileError == null) fileError = "Bad id for repo: " + sec + ", byte = " + Regex.Match(sec, "[^A-Za-z0-9_.:-]").Value + " " + Regex.Match(sec, "[^A-Za-z0-9_.:-]").Index;
                        list.Add(r);
                    }
                }
            }
            catch (VfsEx) { }
            return list;
        }

        /// <summary>Finds what a repository's baseurl points at (repodata/repomd.xml must be there).</summary>
        static void Probe(Sh sh, Repo r)
        {
            var m = sh.M;
            string url = r.BaseUrl.TrimEnd('/');
            if (url.Length == 0) { r.Error = "Cannot find a valid baseurl for repo: " + r.Id; return; }
            url = url.Replace("$releasever", "9").Replace("$basearch", "x86_64");
            if (url.StartsWith("file://", StringComparison.Ordinal))
            {
                string path = url.Substring(7);
                try
                {
                    var l = m.V.Resolve(new PCtx(), path + "/repodata/repomd.xml", true);
                    if (!l.Exists) { r.Error = "Curl error (37): Couldn't read a file:// file for " + url + "/repodata/repomd.xml [Couldn't open file " + path + "/repodata/repomd.xml]"; return; }
                    string top = Vfs.Base(path);
                    r.Content = top == "BaseOS" || top == "AppStream" ? top : "";
                    if (l.Fs == null || l.Fs.Id != "dvd") r.Content = top == "BaseOS" || top == "AppStream" ? top : "";
                }
                catch (VfsEx) { r.Error = "Curl error (37): Couldn't read a file:// file for " + url + "/repodata/repomd.xml [Couldn't open file " + path + "/repodata/repomd.xml]"; }
                return;
            }
            if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("ftp://", StringComparison.Ordinal))
            {
                var rep = Fetch(m, new PCtx(), url + "/repodata/repomd.xml");
                string host = Regex.Match(url, "^\\w+://([^/:]+)").Groups[1].Value;
                if (rep.Error != null)
                {
                    string why = rep.CurlCode == 6 ? "Curl error (6): Couldn't resolve host name for " + url + "/repodata/repomd.xml [Could not resolve host: " + host + "]" : "Curl error (" + (rep.CurlCode == 0 ? 7 : rep.CurlCode) + "): Couldn't connect to server for " + url + "/repodata/repomd.xml [" + rep.Error + "]";
                    r.Error = why; return;
                }
                if (rep.Code != 200) { r.Error = "Status code: " + rep.Code + " for " + url + "/repodata/repomd.xml (IP: " + (m.Net.Resolve(m, host) ?? host) + ")"; return; }
                string top = Vfs.Base(Regex.Replace(url, "^\\w+://[^/]+", ""));
                r.Content = top == "BaseOS" || top == "AppStream" ? top : "";
                return;
            }
            r.Error = "Curl error (1): Unsupported protocol for " + url + "/repodata/repomd.xml [Protocol \"" + url.Split(':')[0] + "\" not supported or disabled in libcurl]";
        }

        class DnfCtx
        {
            public List<Repo> Repos = new List<Repo>();
            public bool Yes, Quiet, NoGpg, AssumeNo;
            public List<string> Args = new List<string>();
            public List<string> Enable = new List<string>(), Disable = new List<string>();
            public List<string> RepoOnly = new List<string>();
            public bool Ready;
        }

        static string MetaLine(Repo r) { string n = r.Name.Length > 46 ? r.Name.Substring(0, 46) : r.Name; return n.PadRight(48) + " 32 MB/s | " + (r.Content == "AppStream" ? "7.8 MB" : r.Content == "BaseOS" ? "2.4 MB" : "3.1 kB") + "     00:00    "; }

        /// <summary>Reads the enabled repositories (metadata). False after printing the error dnf prints.</summary>
        static bool OpenRepos(Sh sh, Io io, DnfCtx c, bool needSome)
        {
            var m = sh.M;
            string fe;
            var repos = LoadRepos(m, out fe);
            if (fe != null) { io.E(fe); return false; }
            foreach (var r in repos)
            {
                if (c.Enable.Any(x => Sh.GlobMatch(x, r.Id))) r.Enabled = true;
                if (c.Disable.Any(x => Sh.GlobMatch(x, r.Id))) r.Enabled = false;
                if (c.RepoOnly.Count > 0) r.Enabled = c.RepoOnly.Any(x => Sh.GlobMatch(x, r.Id));
            }
            foreach (var x in c.Enable.Concat(c.RepoOnly)) if (!x.Contains("*") && !repos.Any(r => r.Id == x)) { io.E("Error: Unknown repo: '" + x + "'"); return false; }
            c.Repos = repos;
            var on = repos.Where(r => r.Enabled).ToList();
            if (on.Count == 0)
            {
                if (needSome) { io.E("Error: There are no enabled repositories in \"/etc/yum.repos.d\", \"/etc/yum/repos.d\", \"/etc/distro.repos.d\"."); return false; }
                return true;
            }
            bool printedAny = false;
            foreach (var r in on)
            {
                Probe(sh, r);
                string marker = "/var/cache/dnf/" + r.Id + ".certlab-meta";
                if (r.Error != null)
                {
                    io.E("Errors during downloading metadata for repository '" + r.Id + "':\n  - " + r.Error + "\nError: Failed to download metadata for repo '" + r.Id + "': Cannot download repomd.xml: " + r.Error);
                    return false;
                }
                if (!m.V.Exists(marker))
                {
                    if (sh.P.Cr.Uid == 0) { try { m.V.Put(marker, r.BaseUrl + "\n", 0x1A4, 0, 0, "system_u:object_r:rpm_var_cache_t:s0"); } catch (VfsEx) { } }
                    if (!c.Quiet) io.L(MetaLine(r));
                    printedAny = true;
                }
            }
            if (!c.Quiet && !printedAny)
            {
                long newest = 0;
                foreach (var r in on) { try { var l = m.V.G("/var/cache/dnf/" + r.Id + ".certlab-meta"); if (l.Exists) newest = Math.Max(newest, l.Node.Mtime); } catch (VfsEx) { } }
                long ago = Math.Max(0, m.Now - newest);
                io.L("Last metadata expiration check: " + (ago / 3600) + ":" + (ago / 60 % 60).ToString("00") + ":" + (ago % 60).ToString("00") + " ago on " + m.LocalTime(newest).ToString("ddd dd MMM yyyy hh:mm:ss tt", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, newest) + ".");
            }
            c.Ready = true;
            return true;
        }

        static List<Tuple<Pkg, Repo>> Available(DnfCtx c)
        {
            var l = new List<Tuple<Pkg, Repo>>();
            foreach (var r in c.Repos.Where(x => x.Enabled && x.Content != null))
                foreach (var p in Packages.All.Where(x => x.Repo == r.Content && x.Name != "kernel"))
                    if (!l.Any(t => t.Item1 == p)) l.Add(Tuple.Create(p, r));
            return l;
        }

        static Pkg Match(string arg, IEnumerable<Pkg> pool)
        {
            string a = arg;
            foreach (var p in pool)
                if (p.Name == a || p.Nevra == a || p.Name + "-" + p.Ver == a || p.Name + "." + p.Arch == a || p.Name + "-" + VerOf(p) == a) return p;
            return null;
        }

        static bool Ask(Sh sh, Io io, DnfCtx c, string q)
        {
            if (c.Yes) return true;
            if (c.AssumeNo) { io.L(q + "n\nOperation aborted."); return false; }
            string ans;
            if (io.In.IsTty && sh.S != null) ans = sh.S.ReadLine(q, false);
            else { io.P(q); ans = io.In.ReadLine(); io.P("\n"); }
            bool ok = ans != null && (ans.Trim().ToLowerInvariant() == "y" || ans.Trim().ToLowerInvariant() == "yes");
            if (!ok) io.L("Operation aborted.");
            return ok;
        }

        static void Record(Machine m, string user, string cmdline, string action, List<string> pkgs)
        {
            var t = m.V.Cat(Machine.DnfHistory) ?? "1|" + (m.Now - 86400 * 2) + "|root|anaconda|Install|" + Packages.All.Count(p => p.Base) + "|\n";
            int id = t.Split('\n').Count(x => x.Length > 0) + 1;
            t += id + "|" + m.Now + "|" + user + "|" + cmdline.Replace("|", " ") + "|" + action + "|" + pkgs.Count + "|" + string.Join(",", pkgs.ToArray()) + "\n";
            m.SysWrite(Machine.DnfHistory, t, 0x1A4, "system_u:object_r:rpm_var_lib_t:s0");
        }

        static string TxHeader() { string line = new string('=', 80); return line + "\n" + " Package".PadRight(27) + " Architecture".PadRight(14) + " Version".PadRight(25) + " Repository".PadRight(17) + " Size\n" + line; }

        // ------------------------------------------------------------ dnf
        static int Dnf(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var c = new DnfCtx();
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-y" || s == "--assumeyes") c.Yes = true;
                else if (s == "-q" || s == "--quiet") c.Quiet = true;
                else if (s == "--assumeno") c.AssumeNo = true;
                else if (s == "--nogpgcheck") c.NoGpg = true;
                else if (s.StartsWith("--enablerepo", StringComparison.Ordinal)) c.Enable.AddRange((s.Contains("=") ? s.Substring(s.IndexOf('=') + 1) : (i + 1 < a.Count ? a[++i] : "")).Split(','));
                else if (s.StartsWith("--disablerepo", StringComparison.Ordinal)) c.Disable.AddRange((s.Contains("=") ? s.Substring(s.IndexOf('=') + 1) : (i + 1 < a.Count ? a[++i] : "")).Split(','));
                else if (s.StartsWith("--repo=", StringComparison.Ordinal) || s.StartsWith("--repoid=", StringComparison.Ordinal)) c.RepoOnly.AddRange(s.Substring(s.IndexOf('=') + 1).Split(','));
                else if (s == "--repo" || s == "--repoid") { if (i + 1 < a.Count) c.RepoOnly.AddRange(a[++i].Split(',')); }
                else if (s == "-v" || s == "--verbose" || s == "-C" || s == "--cacheonly" || s == "--refresh" || s == "--best" || s == "--allowerasing" || s == "--skip-broken" || s == "--nobest" || s == "--installed" || s == "--available" || s == "--all" || s == "--hidden" || s == "--enabled" || s == "--disabled" || s.StartsWith("--setopt", StringComparison.Ordinal) || s == "--save" || s.StartsWith("--add-repo", StringComparison.Ordinal) || s.StartsWith("--set-", StringComparison.Ordinal) || s == "--enable" || s == "--disable" || s == "--nodocs" || s == "-x") { c.Args.Add(s); if (s == "-x" && i + 1 < a.Count) c.Args.Add(a[++i]); }
                else if (s == "-h" || s == "--help") { io.L("usage: dnf [options] COMMAND\n\nList of Main Commands:\n\nautoremove                remove all unneeded packages that were originally installed as dependencies\ncheck-update              check for available package upgrades\nclean                     remove cached data\ngroup                     display, or use, the groups information\nhistory                   display, or use, the transaction history\ninfo                      display details about a package or group of packages\ninstall                   install a package or packages on your system\nlist                      list a package or groups of packages\nmakecache                 generate the metadata cache\nprovides                  find what package provides the given value\nreinstall                 reinstall a package\nremove                    remove a package or packages from your system\nrepolist                  display the configured software repositories\nrepoquery                 search for packages matching keyword\nsearch                    search package details for the given string\nupgrade                   upgrade a package or packages on your system\n\nList of Plugin Commands:\n\nconfig-manager            manage dnf configuration options and repositories\ndownload                  Download package to current directory"); return 0; }
                else if (s.StartsWith("-", StringComparison.Ordinal)) { io.E("usage: dnf [options] COMMAND\n\ndnf: error: unrecognized arguments: " + s); return 2; }
                else c.Args.Add(s);
            }
            if (c.Args.Count == 0) { io.E("usage: dnf [options] COMMAND\n\n(Cert Lab) Try: dnf repolist, dnf install PACKAGE, dnf list installed, dnf provides FILE"); return 1; }
            string cmd = c.Args[0]; var rest = c.Args.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            var flags = c.Args.Skip(1).Where(x => x.StartsWith("-", StringComparison.Ordinal)).ToList();
            bool root = sh.P.Cr.Uid == 0;
            Func<int> needRoot = () => { io.E("Error: This command has to be run with superuser privileges (under the root user on most systems)."); return 1; };
            string cmdline = string.Join(" ", a.Skip(1).ToArray());
            if (root && !c.Quiet && cmd != "config-manager" && cmd != "history" && cmd != "clean") io.E("Updating Subscription Management repositories.\nUnable to read consumer identity\n\nThis system is not registered with an entitlement server. You can use \"rhc\" or \"subscription-manager\" to register.\n");
            var inst = m.InstalledPackages();
            switch (cmd)
            {
                case "repolist":
                    {
                        string fe; var repos = LoadRepos(m, out fe);
                        if (fe != null) { io.E(fe); return 1; }
                        bool all = flags.Contains("--all") || rest.Contains("all"), dis = flags.Contains("--disabled") || rest.Contains("disabled");
                        var show = repos.Where(r => all || (dis ? !r.Enabled : r.Enabled)).ToList();
                        if (show.Count == 0) { if (!all && !dis) io.L("No repositories available"); return 0; }
                        int w = Math.Max(8, show.Max(r => r.Id.Length)) + 1;
                        if (flags.Contains("-v"))
                        {
                            foreach (var r in show) { Probe(sh, r); io.L("Repo-id            : " + r.Id + "\nRepo-name          : " + r.Name + "\nRepo-status        : " + (r.Enabled ? "enabled" : "disabled") + "\nRepo-pkgs          : " + (r.Content == null ? "0" : Packages.All.Count(p => p.Repo == r.Content).ToString()) + "\nRepo-baseurl       : " + r.BaseUrl + "\nRepo-filename      : " + r.File + "\n"); }
                            return 0;
                        }
                        io.L("repo id".PadRight(w) + " repo name" + (all ? "".PadRight(Math.Max(1, 40 - 9)) + "status" : ""));
                        foreach (var r in show) io.L(r.Id.PadRight(w) + " " + (all ? r.Name.PadRight(40) + (r.Enabled ? "enabled" : "disabled") : r.Name));
                        return 0;
                    }
                case "repoinfo":
                    return Dnf(sh, new List<string> { "dnf", "repolist", "-v" }, io);
                case "config-manager":
                    {
                        if (!root) return needRoot();
                        var cargs = c.Args.Skip(1).ToList();
                        for (int i = 0; i < cargs.Count; i++)
                        {
                            string s = cargs[i];
                            if (s.StartsWith("--add-repo", StringComparison.Ordinal))
                            {
                                string url = s.Contains("=") ? s.Substring(s.IndexOf('=') + 1) : (i + 1 < cargs.Count ? cargs[++i] : "");
                                if (url.Length == 0) { io.E("Error: argument --add-repo: expected one argument"); return 2; }
                                io.L("Adding repo from: " + url);
                                if (url.EndsWith(".repo", StringComparison.Ordinal))
                                {
                                    var rep = Fetch(sh, url);
                                    if (rep.Error != null || rep.Code != 200) { io.E("Error: Configuration of repo failed"); return 1; }
                                    m.SysWrite("/etc/yum.repos.d/" + Vfs.Base(url), rep.Body, 0x1A4, null);
                                    continue;
                                }
                                string id = Regex.Replace(url, "^\\w+://", "").Trim('/');
                                id = Regex.Replace(id, "[^A-Za-z0-9_.:-]", "_");
                                m.SysWrite("/etc/yum.repos.d/" + id + ".repo", "[" + id + "]\nname=created by dnf config-manager from " + url + "\nbaseurl=" + url + "\nenabled=1\n", 0x1A4, null);
                            }
                            else if (s == "--enable" || s == "--disable" || s == "--set-enabled" || s == "--set-disabled")
                            {
                                bool en = s.Contains("enable") && !s.Contains("disable");
                                string fe; var repos = LoadRepos(m, out fe);
                                var ids = cargs.Skip(i + 1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
                                i = cargs.Count;
                                foreach (var id in ids)
                                {
                                    var hit = repos.Where(r => Sh.GlobMatch(id, r.Id)).ToList();
                                    if (hit.Count == 0) { io.E("Error: No matching repo to modify: " + id + "."); return 1; }
                                    foreach (var r in hit) SetRepoKey(m, r, "enabled", en ? "1" : "0");
                                }
                            }
                            else if (s.StartsWith("--setopt=", StringComparison.Ordinal))
                            {
                                var kv = s.Substring(9).Split(new[] { '=' }, 2);
                                var key = kv[0].Split('.');
                                string fe; var repos = LoadRepos(m, out fe);
                                if (key.Length == 2 && kv.Length == 2) foreach (var r in repos.Where(x => Sh.GlobMatch(key[0], x.Id))) SetRepoKey(m, r, key[1], kv[1]);
                            }
                        }
                        return 0;
                    }
                case "clean":
                    {
                        if (!root) return needRoot();
                        int n = 0;
                        try { var d = m.V.G("/var/cache/dnf"); if (d.Exists) { foreach (var k in d.Node.Kids.Keys.ToList()) { m.V.RemoveG("/var/cache/dnf/" + k); n++; } } } catch (VfsEx) { }
                        io.L("Updating Subscription Management repositories.\n" + (n * 13 + 1) + " files removed");
                        return 0;
                    }
                case "makecache":
                    if (!OpenRepos(sh, io, c, false)) return 1;
                    io.L("Metadata cache created.");
                    return 0;
                case "list":
                    {
                        string which = rest.Count > 0 && (rest[0] == "installed" || rest[0] == "available" || rest[0] == "all" || rest[0] == "updates" || rest[0] == "upgrades") ? rest[0] : "all";
                        var pats = rest.Where(x => x != which).ToList();
                        if (flags.Contains("--installed")) which = "installed";
                        if (flags.Contains("--available")) which = "available";
                        if (which != "installed" && !OpenRepos(sh, io, c, false)) return 1;
                        Func<string, bool> pm = n => pats.Count == 0 || pats.Any(p => Sh.GlobMatch(p, n));
                        bool any = false;
                        if (which == "installed" || which == "all")
                        {
                            var rows = inst.Values.Where(r => pm(r[0])).OrderBy(r => r[0], StringComparer.Ordinal).ToList();
                            if (rows.Count > 0) { io.L("Installed Packages"); any = true; foreach (var r in rows) io.L((r[0] + "." + r[2]).PadRight(40) + " " + r[1].PadRight(28) + " @" + (r[3] == "@System" ? "System" : r[3].TrimStart('@'))); }
                        }
                        if (which == "available" || which == "all")
                        {
                            var rows = Available(c).Where(t => pm(t.Item1.Name) && !inst.ContainsKey(t.Item1.Name)).OrderBy(t => t.Item1.Name, StringComparer.Ordinal).ToList();
                            if (rows.Count > 0) { io.L("Available Packages"); any = true; foreach (var t in rows) io.L((t.Item1.Name + "." + t.Item1.Arch).PadRight(40) + " " + t.Item1.Ver.PadRight(28) + " " + t.Item2.Id); }
                        }
                        if (which == "updates" || which == "upgrades") return 0;
                        if (!any && pats.Count > 0) { io.E("Error: No matching Packages to list"); return 1; }
                        return 0;
                    }
                case "info":
                    {
                        if (!OpenRepos(sh, io, c, false)) return 1;
                        var avail = Available(c);
                        bool any = false;
                        foreach (var pat in rest.Count > 0 ? rest : new List<string> { "*" })
                        {
                            var ip = inst.Keys.Where(n => Sh.GlobMatch(pat, n)).Select(n => Packages.Get(n)).Where(p => p != null).ToList();
                            var ap = avail.Where(t => Sh.GlobMatch(pat, t.Item1.Name) && !inst.ContainsKey(t.Item1.Name)).ToList();
                            if (ip.Count > 0) { io.L("Installed Packages"); foreach (var p in ip) io.L(PkgInfo(p, "@System", inst[p.Name][3].TrimStart('@'))); any = true; }
                            if (ap.Count > 0) { io.L("Available Packages"); foreach (var t in ap) io.L(PkgInfo(t.Item1, t.Item2.Id, null)); any = true; }
                        }
                        if (!any) { io.E("Error: No matching Packages to list"); return 1; }
                        return 0;
                    }
                case "search":
                    {
                        if (rest.Count == 0) { io.E("Error: Need at least one keyword."); return 1; }
                        if (!OpenRepos(sh, io, c, false)) return 1;
                        var pool = Available(c).Select(t => t.Item1).Concat(inst.Keys.Select(n => Packages.Get(n)).Where(p => p != null)).Distinct().ToList();
                        bool any = false;
                        foreach (var k in rest)
                        {
                            var exact = pool.Where(p => p.Name == k).ToList();
                            var name = pool.Where(p => p.Name != k && p.Name.Contains(k)).OrderBy(p => p.Name).ToList();
                            var sum = pool.Where(p => !p.Name.Contains(k) && p.Summary.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p.Name).ToList();
                            Action<string, List<Pkg>> sec = (title, l) => { if (l.Count == 0) return; any = true; string t = " " + title + " "; int pad = Math.Max(0, (80 - t.Length) / 2); io.L(new string('=', pad) + t + new string('=', Math.Max(0, 80 - pad - t.Length))); foreach (var p in l) io.L(p.Name + "." + p.Arch + " : " + p.Summary); };
                            sec("Name Exactly Matched: " + k, exact); sec("Name Matched: " + k, name); sec("Summary Matched: " + k, sum);
                        }
                        if (!any) { io.L("No matches found."); return 1; }
                        return 0;
                    }
                case "provides": case "whatprovides": case "wp":
                    {
                        if (rest.Count == 0) { io.E("Error: Command line error: argument provides: expected at least one argument"); return 2; }
                        if (!OpenRepos(sh, io, c, false)) return 1;
                        var pool = Available(c).Select(t => t.Item1).Concat(inst.Keys.Select(n => Packages.Get(n)).Where(p => p != null)).Distinct().ToList();
                        bool any = false;
                        foreach (var q in rest)
                        {
                            foreach (var p in pool.OrderBy(x => x.Name, StringComparer.Ordinal))
                            {
                                var files = Packages.Files(p);
                                string hit = files.FirstOrDefault(f => f == q || Sh.GlobMatch(q, f) || (!q.Contains("/") && Vfs.Base(f) == q && (f.StartsWith("/usr/bin/", StringComparison.Ordinal) || f.StartsWith("/usr/sbin/", StringComparison.Ordinal))));
                                if (hit == null && p.Name == q) hit = "";
                                if (hit == null) continue;
                                any = true;
                                var repoName = inst.ContainsKey(p.Name) ? "@System" : (Available(c).First(t => t.Item1 == p).Item2.Id);
                                io.L(p.Nevra + " : " + p.Summary + "\nRepo        : " + repoName + "\nMatched from:\n" + (hit.Length > 0 ? "Filename    : " + hit : "Provide    : " + p.Name + " = " + p.Ver) + "\n");
                            }
                        }
                        if (!any) { io.E("Error: No matches found. If searching for a file, try specifying the full path or using a wildcard prefix (\"*/\") at the beginning."); return 1; }
                        return 0;
                    }
                case "install": case "in": case "reinstall": case "localinstall":
                    {
                        if (!root) return needRoot();
                        if (rest.Count == 0) { io.E("Error: Command line error: argument install: expected at least one argument"); return 2; }
                        bool localOnly = rest.All(x => x.EndsWith(".rpm", StringComparison.Ordinal));
                        if (!OpenRepos(sh, io, c, !localOnly)) return 1;
                        var avail = Available(c);
                        var want = new List<Pkg>(); var already = new List<Pkg>(); var missing = new List<string>();
                        var fromRepo = new Dictionary<string, string>();
                        foreach (var arg in rest)
                        {
                            if (arg.StartsWith("@", StringComparison.Ordinal))
                            {
                                var g = PkgGroups.FirstOrDefault(x => x[0].Equals(arg.Substring(1), StringComparison.OrdinalIgnoreCase) || x[1] == arg.Substring(1));
                                if (g == null) { missing.Add(arg); continue; }
                                foreach (var n in g[3].Split(',')) { var p = Packages.Get(n); if (p != null && avail.Any(t => t.Item1 == p)) { if (inst.ContainsKey(n)) already.Add(p); else want.Add(p); } }
                                continue;
                            }
                            if (arg.EndsWith(".rpm", StringComparison.Ordinal))
                            {
                                Loc l;
                                try { l = m.V.Resolve(sh.P, arg, true); } catch (VfsEx) { l = null; }
                                var p = l != null && l.Exists ? Packages.All.FirstOrDefault(x => x.Nevra + ".rpm" == Vfs.Base(arg)) : null;
                                if (l == null || !l.Exists) { io.E("Can not load RPM file: " + arg + ".\nCould not open: " + arg); return 1; }
                                if (p == null) { io.E("Can not load RPM file: " + arg + ".\nCould not open: " + arg); return 1; }
                                fromRepo[p.Name] = "@commandline";
                                if (inst.ContainsKey(p.Name) && cmd != "reinstall") already.Add(p); else want.Add(p);
                                continue;
                            }
                            var cand = avail.Select(t => t.Item1).ToList();
                            var hit = Match(arg, cand);
                            if (hit == null && arg.Contains("/")) hit = cand.FirstOrDefault(x => Packages.Files(x).Contains(arg));
                            if (hit == null && (arg.Contains("*") || arg.Contains("?")))
                            {
                                var many = cand.Where(x => Sh.GlobMatch(arg, x.Name)).ToList();
                                if (many.Count > 0) { foreach (var p in many) if (inst.ContainsKey(p.Name)) already.Add(p); else want.Add(p); continue; }
                            }
                            if (hit == null)
                            {
                                var ip = Match(arg, inst.Keys.Select(n => Packages.Get(n)).Where(x => x != null));
                                if (ip != null && cmd != "reinstall") { already.Add(ip); continue; }
                                missing.Add(arg); continue;
                            }
                            if (inst.ContainsKey(hit.Name) && cmd != "reinstall") already.Add(hit); else want.Add(hit);
                        }
                        foreach (var p in already.Distinct()) io.L("Package " + p.Nevra + " is already installed.");
                        foreach (var x in missing) io.L("No match for argument: " + x);
                        if (missing.Count > 0) { io.E("Error: Unable to find a match: " + string.Join(" ", missing.ToArray())); return 1; }
                        // dependencies
                        var deps = new List<Pkg>();
                        var queue = new Queue<Pkg>(want);
                        var seen = new HashSet<string>(want.Select(p => p.Name));
                        while (queue.Count > 0)
                        {
                            var p = queue.Dequeue();
                            foreach (var r in p.Requires)
                            {
                                if (inst.ContainsKey(r) || seen.Contains(r)) continue;
                                var d = Packages.Get(r);
                                if (d == null || !avail.Any(t => t.Item1 == d)) { io.E("Error: \n Problem: conflicting requests\n  - nothing provides " + r + " needed by " + p.Nevra + " from " + (fromRepo.ContainsKey(p.Name) ? "@commandline" : avail.First(t => t.Item1 == p).Item2.Id) + "\n(try to add '--skip-broken' to skip uninstallable packages)"); return 1; }
                                seen.Add(r); deps.Add(d); queue.Enqueue(d);
                            }
                        }
                        want = want.Distinct().ToList();
                        if (want.Count == 0) { io.L("Dependencies resolved.\nNothing to do.\nComplete!"); return 0; }
                        Func<Pkg, string> repoOf = p => fromRepo.ContainsKey(p.Name) ? "@commandline" : avail.First(t => t.Item1 == p).Item2.Id;
                        // GPG: the release key is imported at installation; a repository with gpgcheck needs it
                        var sb = new StringBuilder("Dependencies resolved.\n");
                        string line = new string('=', 80);
                        sb.Append(TxHeader() + "\n");
                        sb.Append(cmd == "reinstall" ? "Reinstalling:\n" : "Installing:\n");
                        foreach (var p in want) sb.Append(" " + p.Name.PadRight(26) + " " + p.Arch.PadRight(13) + " " + p.Ver.PadRight(24) + " " + repoOf(p).PadRight(16) + " " + KSize(p.SizeKb / 3).PadLeft(6) + "\n");
                        if (deps.Count > 0) { sb.Append("Installing dependencies:\n"); foreach (var p in deps) sb.Append(" " + p.Name.PadRight(26) + " " + p.Arch.PadRight(13) + " " + p.Ver.PadRight(24) + " " + repoOf(p).PadRight(16) + " " + KSize(p.SizeKb / 3).PadLeft(6) + "\n"); }
                        var all = want.Concat(deps).ToList();
                        long total = all.Sum(p => p.SizeKb / 3), isize = all.Sum(p => p.SizeKb);
                        sb.Append("\nTransaction Summary\n" + line + "\n" + (cmd == "reinstall" ? "Reinstall  " : "Install  ") + all.Count + " Package" + (all.Count == 1 ? "" : "s") + "\n\nTotal size: " + KSize(total) + "\nInstalled size: " + KSize(isize));
                        io.L(sb.ToString());
                        if (!Ask(sh, io, c, "Is this ok [y/N]: ")) return 1;
                        var gpgRepos = all.Where(p => !c.NoGpg && repoOf(p) != "@commandline").Select(p => c.Repos.First(r => r.Id == repoOf(p))).Where(r => r.GpgCheck).Distinct().ToList();
                        io.L("Downloading Packages:");
                        int k = 0;
                        foreach (var p in all) io.L("(" + (++k) + "/" + all.Count + "): " + (p.Nevra + ".rpm").PadRight(52) + " 40 MB/s | " + KSize(p.SizeKb / 3).PadLeft(6) + "     00:00    ");
                        io.L(new string('-', 80) + "\nTotal".PadRight(53) + " 38 MB/s | " + KSize(total).PadLeft(6) + "     00:00     ");
                        foreach (var r in gpgRepos)
                        {
                            if (m.V.Exists(Machine.RpmKeys) && (m.V.Cat(Machine.RpmKeys) ?? "").Contains("fd431d51")) continue;
                            string key = r.GpgKey.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (key == null)
                            {
                                var bad = all.First(p => repoOf(p) == r.Id);
                                io.E("warning: /var/cache/dnf/" + r.Id + "-certlab/packages/" + bad.Nevra + ".rpm: Header V3 RSA/SHA256 Signature, key ID fd431d51: NOKEY\nPublic key for " + bad.Nevra + ".rpm is not installed\nThe downloaded packages were saved in cache until the next successful transaction.\nYou can remove cached packages by executing 'dnf clean packages'.\nError: GPG check FAILED");
                                return 1;
                            }
                            string kp = key.StartsWith("file://", StringComparison.Ordinal) ? key.Substring(7) : null;
                            bool ok = kp != null ? m.V.Exists(kp) : Fetch(sh, key).Code == 200;
                            if (!ok) { io.E("Curl error (37): Couldn't read a file:// file for " + key + " [Couldn't open file " + (kp ?? key) + "]\nError: GPG check FAILED"); return 1; }
                            io.L("Importing GPG key 0xFD431D51:\n Userid     : \"Red Hat, Inc. (release key 2) <security@redhat.com>\"\n Fingerprint: 567E 347A D004 4ADE 55BA 8A5F 199E 2F91 FD43 1D51\n From       : " + (kp ?? key));
                            if (!Ask(sh, io, c, "Is this ok [y/N]: ")) return 1;
                            m.SysWrite(Machine.RpmKeys, "gpg-pubkey-fd431d51-4ae0493b\ngpg-pubkey-5a6340b3-6229229e\n", 0x1A4, null);
                            io.L("Key imported successfully");
                        }
                        io.L("Running transaction check\nTransaction check succeeded.\nRunning transaction test\nTransaction test succeeded.\nRunning transaction\n  Preparing        :                                                        1/1 ");
                        k = 0;
                        foreach (var p in deps.Concat(want)) io.L("  " + (cmd == "reinstall" ? "Reinstalling" : "Installing  ") + "     : " + (p.Name + "-" + p.Ver + "." + p.Arch).PadRight(54) + (++k) + "/" + all.Count + " ");
                        foreach (var p in deps) { m.InstallPkg(p, repoOf(p)); m.SetPkgReason(p.Name, "dep"); }
                        foreach (var p in want) { bool wasDep = inst.ContainsKey(p.Name) && m.PkgReason(p.Name) == "dep"; m.InstallPkg(p, repoOf(p)); if (wasDep) m.SetPkgReason(p.Name, "dep"); }
                        k = 0;
                        foreach (var p in deps.Concat(want)) io.L("  Verifying        : " + (p.Name + "-" + p.Ver + "." + p.Arch).PadRight(54) + (++k) + "/" + all.Count + " ");
                        io.L("Installed products updated.\n\n" + (cmd == "reinstall" ? "Reinstalled:" : "Installed:") + "\n" + string.Join("\n", all.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => "  " + p.Nevra).ToArray()) + "\n\nComplete!");
                        Record(m, sh.P.Cr.User, cmdline, cmd == "reinstall" ? "Reinstall" : "Install", all.Select(p => p.Name).ToList());
                        return 0;
                    }
                case "remove": case "erase": case "rm": case "autoremove":
                    {
                        if (!root) return needRoot();
                        if (rest.Count == 0 && cmd != "autoremove") { io.E("Error: Command line error: argument remove: expected at least one argument"); return 2; }
                        var target = new List<Pkg>(); var missing = new List<string>();
                        foreach (var arg in rest)
                        {
                            var hits = inst.Keys.Where(n => n == arg || Sh.GlobMatch(arg, n) || Match(arg, new[] { Packages.Get(n) }.Where(x => x != null)) != null).Select(n => Packages.Get(n)).Where(p => p != null).ToList();
                            if (hits.Count == 0) missing.Add(arg); else target.AddRange(hits);
                        }
                        foreach (var x in missing) io.L("No match for argument: " + x);
                        if (missing.Count > 0 && target.Count == 0) { io.E("No packages marked for removal.\nDependencies resolved.\nNothing to do.\nComplete!"); return 0; }
                        // what needs the removed packages goes too
                        var gone = new HashSet<string>(target.Select(p => p.Name));
                        bool grew = true;
                        while (grew)
                        {
                            grew = false;
                            foreach (var n in inst.Keys) { var p = Packages.Get(n); if (p != null && !gone.Contains(n) && p.Requires.Any(r => gone.Contains(r))) { gone.Add(n); grew = true; } }
                        }
                        var prot = gone.Where(n => Protected.Contains(n)).ToList();
                        if (prot.Count > 0) { io.E("Error: \n Problem: The operation would result in removing the following protected packages: " + string.Join(", ", prot.ToArray())); return 1; }
                        // dependencies nobody needs any more (clean_requirements_on_remove)
                        var unused = new List<string>();
                        grew = true;
                        while (grew)
                        {
                            grew = false;
                            foreach (var n in inst.Keys)
                            {
                                if (gone.Contains(n) || unused.Contains(n) || m.PkgReason(n) != "dep") continue;
                                bool needed = inst.Keys.Any(o2 => !gone.Contains(o2) && !unused.Contains(o2) && Packages.Get(o2) != null && Packages.Get(o2).Requires.Contains(n));
                                if (!needed) { unused.Add(n); grew = true; }
                            }
                        }
                        var rm = gone.Concat(unused).Select(n => Packages.Get(n)).Where(p => p != null).ToList();
                        if (rm.Count == 0) { io.L("Dependencies resolved.\nNothing to do.\nComplete!"); return 0; }
                        string line = new string('=', 80);
                        var sb = new StringBuilder("Dependencies resolved.\n" + TxHeader() + "\nRemoving:\n");
                        foreach (var p in rm.Where(x => target.Contains(x))) sb.Append(" " + p.Name.PadRight(26) + " " + p.Arch.PadRight(13) + " " + p.Ver.PadRight(24) + " " + ("@" + inst[p.Name][3].TrimStart('@')).PadRight(16) + " " + KSize(p.SizeKb).PadLeft(6) + "\n");
                        var dependent = rm.Where(x => !target.Contains(x) && !unused.Contains(x.Name)).ToList();
                        if (dependent.Count > 0) { sb.Append("Removing dependent packages:\n"); foreach (var p in dependent) sb.Append(" " + p.Name.PadRight(26) + " " + p.Arch.PadRight(13) + " " + p.Ver.PadRight(24) + " " + ("@" + inst[p.Name][3].TrimStart('@')).PadRight(16) + " " + KSize(p.SizeKb).PadLeft(6) + "\n"); }
                        var unusedP = rm.Where(x => unused.Contains(x.Name)).ToList();
                        if (unusedP.Count > 0) { sb.Append("Removing unused dependencies:\n"); foreach (var p in unusedP) sb.Append(" " + p.Name.PadRight(26) + " " + p.Arch.PadRight(13) + " " + p.Ver.PadRight(24) + " " + ("@" + inst[p.Name][3].TrimStart('@')).PadRight(16) + " " + KSize(p.SizeKb).PadLeft(6) + "\n"); }
                        sb.Append("\nTransaction Summary\n" + line + "\nRemove  " + rm.Count + " Package" + (rm.Count == 1 ? "" : "s") + "\n\nFreed space: " + KSize(rm.Sum(p => p.SizeKb)));
                        io.L(sb.ToString());
                        if (!Ask(sh, io, c, "Is this ok [y/N]: ")) return 1;
                        io.L("Running transaction check\nTransaction check succeeded.\nRunning transaction test\nTransaction test succeeded.\nRunning transaction\n  Preparing        :                                                        1/1 ");
                        int k = 0;
                        foreach (var p in rm) io.L("  Erasing          : " + (p.Name + "-" + p.Ver + "." + p.Arch).PadRight(54) + (++k) + "/" + rm.Count + " ");
                        foreach (var p in rm) m.RemovePkg(p);
                        io.L("Installed products updated.\n\nRemoved:\n" + string.Join("\n", rm.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => "  " + p.Nevra).ToArray()) + "\n\nComplete!");
                        Record(m, sh.P.Cr.User, cmdline, "Removed", rm.Select(p => p.Name).ToList());
                        return 0;
                    }
                case "upgrade": case "update": case "up": case "distro-sync": case "upgrade-minimal":
                    if (!root) return needRoot();
                    if (!OpenRepos(sh, io, c, true)) return 1;
                    io.L("Dependencies resolved.\nNothing to do.\nComplete!");
                    return 0;
                case "check-update": case "check-upgrade":
                    if (!OpenRepos(sh, io, c, false)) return 1;
                    return 0;
                case "download":
                    {
                        if (!OpenRepos(sh, io, c, true)) return 1;
                        int st = 0;
                        foreach (var arg in rest)
                        {
                            var p = Match(arg, Available(c).Select(t => t.Item1));
                            if (p == null) { io.E("No package " + arg + " available.\nError: No packages available."); st = 1; continue; }
                            try { var n = m.V.WriteText(sh.P, p.Nevra + ".rpm", "", false); n.Text = null; n.BinSize = p.SizeKb * 1024 / 3; io.L((p.Nevra + ".rpm").PadRight(52) + " 40 MB/s | " + KSize(p.SizeKb / 3).PadLeft(6) + "     00:00    "); }
                            catch (VfsEx e) { io.E("[Errno 13] " + e.Message + ": '" + p.Nevra + ".rpm'"); st = 1; }
                        }
                        return st;
                    }
                case "group": case "groups": case "grouplist": case "groupinstall": case "groupinfo": case "groupremove":
                    {
                        string verb = cmd == "grouplist" ? "list" : cmd == "groupinstall" ? "install" : cmd == "groupinfo" ? "info" : cmd == "groupremove" ? "remove" : (rest.Count > 0 ? rest[0] : "summary");
                        var gargs = cmd.StartsWith("group", StringComparison.Ordinal) && cmd.Length > 6 ? rest : rest.Skip(1).ToList();
                        string gname = string.Join(" ", gargs.ToArray());
                        Func<string[], bool> instG = g => g[3].Split(',').All(n => m.PackageInstalled(n));
                        if (verb == "list" || verb == "summary")
                        {
                            if (!OpenRepos(sh, io, c, false)) return 1;
                            io.L("Available Environment Groups:\n   Server with GUI\n   Minimal Install\n   Workstation\n   Custom Operating System\n   Virtualization Host\nInstalled Environment Groups:\n   Server\nInstalled Groups:");
                            foreach (var g in PkgGroups.Where(instG)) io.L("   " + g[0]);
                            io.L("Available Groups:");
                            foreach (var g in PkgGroups.Where(g => !instG(g))) io.L("   " + g[0]);
                            return 0;
                        }
                        var grp = PkgGroups.FirstOrDefault(x => x[0].Equals(gname.Trim('"'), StringComparison.OrdinalIgnoreCase) || x[1] == gname);
                        if (grp == null) { io.E("Warning: Group '" + gname + "' does not exist.\nError: No groups matched: " + gname); return 1; }
                        if (verb == "info")
                        {
                            if (!OpenRepos(sh, io, c, false)) return 1;
                            io.L("\nGroup: " + grp[0] + "\n Description: " + grp[2] + "\n Mandatory Packages:\n" + string.Join("\n", grp[3].Split(',').Take(1).Select(x => "   " + x).ToArray()) + "\n Default Packages:\n" + string.Join("\n", grp[3].Split(',').Skip(1).Select(x => "   " + x).ToArray()));
                            return 0;
                        }
                        var sub = new List<string> { "dnf", verb == "remove" ? "remove" : "install" };
                        if (c.Yes) sub.Add("-y");
                        sub.AddRange(grp[3].Split(',').Where(n => verb == "remove" ? m.PackageInstalled(n) : true));
                        return Dnf(sh, sub, new Io { In = io.In, Out = io.Out, Err = new NullSink() });
                    }
                case "history":
                    {
                        string t = m.V.Cat(Machine.DnfHistory) ?? "1|" + (m.Now - 86400 * 2) + "|root|anaconda|Install|" + Packages.All.Count(p => p.Base) + "|\n";
                        var rows = t.Split('\n').Where(x => x.Length > 0).Select(x => x.Split('|')).ToList();
                        string verb = rest.Count > 0 ? rest[0] : "list";
                        if (verb == "list" || Regex.IsMatch(verb, "^\\d+$") && false)
                        {
                            io.L("ID     | Command line             | Date and time    | Action(s)      | Altered\n" + new string('-', 79));
                            foreach (var r in rows.AsEnumerable().Reverse()) io.L(r[0].PadLeft(6) + " | " + (r[3] == "anaconda" ? "" : r[3]).PadRight(24).Substring(0, 24) + " | " + m.LocalTime(long.Parse(r[1])).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " | " + r[4].PadRight(14) + " | " + r[5].PadLeft(4) + (r[3] == "anaconda" ? " EE" : "   "));
                            return 0;
                        }
                        if (verb == "info" || verb == "undo" || verb == "redo")
                        {
                            string ids = rest.Count > 1 ? rest[1] : "last";
                            var row = ids == "last" ? rows.Last() : rows.FirstOrDefault(r => r[0] == ids || (ids.StartsWith("last-", StringComparison.Ordinal) && r[0] == (rows.Count - int.Parse(ids.Substring(5))).ToString()));
                            if (row == null) { io.E("Error: Can't find transaction with ID: " + ids); return 1; }
                            if (verb == "info")
                            {
                                io.L("Transaction ID : " + row[0] + "\nBegin time     : " + m.LocalTime(long.Parse(row[1])).ToString("ddd dd MMM yyyy hh:mm:ss tt", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, long.Parse(row[1])) + "\nUser           : " + row[2] + " <" + row[2] + ">\nReturn-Code    : Success\nCommand Line   : " + row[3] + "\nPackages Altered:");
                                foreach (var n in row[6].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { var p = Packages.Get(n); if (p != null) io.L("    " + (row[4] == "Removed" ? "Removed" : "Install").PadRight(8) + " " + p.Nevra.PadRight(50) + " @" + (row[4] == "Removed" ? "@System" : p.Repo)); }
                                return 0;
                            }
                            if (!root) return needRoot();
                            if (row[3] == "anaconda") { io.E("Error: An operation cannot be undone (the installation itself)."); return 1; }
                            bool undoInstall = (verb == "undo") == (row[4] != "Removed");
                            var sub = new List<string> { "dnf", undoInstall ? "remove" : "install" };
                            if (c.Yes) sub.Add("-y");
                            sub.AddRange(row[6].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                            return Dnf(sh, sub, new Io { In = io.In, Out = io.Out, Err = new NullSink() });
                        }
                        io.E("Error: Invalid history sub-command, use: " + verb);
                        return 1;
                    }
                case "module":
                    {
                        if (!OpenRepos(sh, io, c, false)) return 1;
                        string verb = rest.Count > 0 ? rest[0] : "list";
                        if (verb == "list")
                        {
                            io.L("Red Hat Enterprise Linux 9 for x86_64 - AppStream (from " + (c.Repos.FirstOrDefault(r => r.Content == "AppStream") ?? new Repo { Id = "AppStream" }).Id + ")\nName         Stream     Profiles                         Summary\nmaven        3.8        common [d]                       Java project management and project comprehension tool\nnginx        1.22       common [d]                       nginx webserver\nnodejs       18         common [d], development, minimal Javascript runtime\nphp          8.1        common [d], devel, minimal       PHP scripting language\npostgresql   15         client, server [d]               PostgreSQL server and client module\nredis        7          common [d]                       Redis persistent key-value database\nruby         3.1        common [d]                       An interpreter of object-oriented scripting language\n\nHint: [d]efault, [e]nabled, [x]disabled, [i]nstalled");
                            return 0;
                        }
                        io.E("(Cert Lab: module streams are listed only. RHEL 9 installs its default versions as ordinary packages: dnf install nginx, dnf install php.)");
                        return 1;
                    }
                case "repoquery":
                    {
                        if (!OpenRepos(sh, io, c, false)) return 1;
                        bool installed = flags.Contains("--installed");
                        var pool = installed ? inst.Keys.Select(n => Packages.Get(n)).Where(p => p != null).ToList() : Available(c).Select(t => t.Item1).ToList();
                        foreach (var p in pool.Where(p => rest.Count == 0 || rest.Any(r => Sh.GlobMatch(r, p.Name))).OrderBy(p => p.Name, StringComparer.Ordinal)) io.L(p.Name + "-0:" + p.Ver + "." + p.Arch);
                        return 0;
                    }
                default:
                    io.E("No such command: " + cmd + ". Please use /usr/bin/dnf --help\nIt could be a DNF plugin command, try: \"dnf install 'dnf-command(" + cmd + ")'\"");
                    return 1;
            }
        }

        static void SetRepoKey(Machine m, Repo r, string key, string value)
        {
            var text = m.V.Cat(r.File) ?? "";
            var lines = text.Split('\n').ToList();
            int start = lines.FindIndex(l => l.Trim() == "[" + r.Id + "]");
            if (start < 0) return;
            int end = lines.FindIndex(start + 1, l => l.Trim().StartsWith("[", StringComparison.Ordinal));
            if (end < 0) end = lines.Count;
            int at = -1;
            for (int i = start + 1; i < end; i++) if (Regex.IsMatch(lines[i], "^\\s*" + Regex.Escape(key) + "\\s*=")) at = i;
            if (at >= 0) lines[at] = key + "=" + value;
            else { int ins = end; while (ins > start + 1 && lines[ins - 1].Trim().Length == 0) ins--; lines.Insert(ins, key + "=" + value); }
            m.SysWrite(r.File, string.Join("\n", lines.ToArray()), 0x1A4, null);
        }

        static string PkgInfo(Pkg p, string repo, string fromRepo)
        {
            return "Name         : " + p.Name + "\nVersion      : " + VerOf(p) + "\nRelease      : " + RelOf(p) + "\nArchitecture : " + p.Arch + "\nSize         : " + KSize(repo == "@System" ? p.SizeKb : p.SizeKb / 3) + "\nSource       : " + p.Name + "-" + p.Ver + ".src.rpm\nRepository   : " + repo + (fromRepo != null ? "\nFrom repo    : " + fromRepo : "") + "\nSummary      : " + p.Summary + "\nURL          : https://example.invalid/" + p.Name + "\nLicense      : GPL-2.0-or-later\nDescription  : " + p.Summary + ".\n";
        }

        // ------------------------------------------------------------ rpm
        static int Rpm(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var inst = m.InstalledPackages();
            var args = a.Skip(1).ToList();
            string flags = string.Join("", args.Where(x => x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("--", StringComparison.Ordinal)).Select(x => x.Substring(1)).ToArray());
            var longs = args.Where(x => x.StartsWith("--", StringComparison.Ordinal)).ToList();
            var names = args.Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            if (longs.Contains("--import"))
            {
                if (sh.P.Cr.Uid != 0) { io.E("error: cannot open Packages database in /var/lib/rpm"); return 1; }
                foreach (var k in names)
                {
                    string text = null;
                    if (k.StartsWith("http", StringComparison.Ordinal)) { var r = Fetch(sh, k); if (r.Code == 200) text = r.Body; }
                    else { try { text = m.V.ReadText(sh.P, k.StartsWith("file://", StringComparison.Ordinal) ? k.Substring(7) : k); } catch (VfsEx) { } }
                    if (text == null) { io.E("error: " + k + ": import read failed(2)."); return 1; }
                    if (!text.Contains("BEGIN PGP PUBLIC KEY")) { io.E("error: " + k + ": key 1 not an armored public key."); return 1; }
                    m.SysWrite(Machine.RpmKeys, "gpg-pubkey-fd431d51-4ae0493b\ngpg-pubkey-5a6340b3-6229229e\n", 0x1A4, null);
                }
                return 0;
            }
            if (flags.Contains("q") || longs.Contains("--query"))
            {
                bool all = flags.Contains("a") || longs.Contains("--all");
                bool fileQ = flags.Contains("f") || longs.Contains("--file");
                bool pkgFile = flags.Contains("p");
                bool info = flags.Contains("i") || longs.Contains("--info"), list = flags.Contains("l") || longs.Contains("--list"), conf = flags.Contains("c") || longs.Contains("--configfiles"), docs = flags.Contains("d") || longs.Contains("--docfiles"), req = flags.Contains("R") || longs.Contains("--requires");
                bool scripts = longs.Contains("--scripts"), changelog = longs.Contains("--changelog");
                bool what = longs.Contains("--whatprovides") || longs.Contains("--whatrequires");
                var targets = new List<Pkg>();
                int st = 0;
                if (names.Contains("gpg-pubkey") || (all && names.Count == 0 && false)) { }
                if (all)
                {
                    targets = inst.Keys.Select(n => Packages.Get(n)).Where(p => p != null && (names.Count == 0 || names.Any(x => Sh.GlobMatch(x, p.Name)))).ToList();
                    if (names.Count == 0 || names.Any(x => Sh.GlobMatch(x, "gpg-pubkey"))) foreach (var k in (m.V.Cat(Machine.RpmKeys) ?? "gpg-pubkey-fd431d51-4ae0493b\ngpg-pubkey-5a6340b3-6229229e\n").Split('\n').Where(x => x.Length > 0)) if (!info && !list) io.L(k);
                }
                else if (fileQ)
                {
                    foreach (var f in names)
                    {
                        string full = Vfs.Norm(Vfs.Join(sh.P.Cwd, f));
                        Loc l = null;
                        try { l = m.V.Resolve(sh.P, full, true); } catch (VfsEx) { }
                        if (l == null || !l.Exists) { io.E("error: file " + full + ": No such file or directory"); st = 1; continue; }
                        string owner = l.Node.Pkg;
                        if (owner == null) foreach (var n in inst.Keys) { var p = Packages.Get(n); if (p != null && Packages.Files(p).Contains(l.GPath)) { owner = n; break; } }
                        if (owner == null && (l.GPath == "/etc/hosts" || l.GPath == "/etc/passwd" || l.GPath == "/etc/group" || l.GPath == "/etc/shadow" || l.GPath == "/etc/fstab" || l.GPath == "/etc/profile" || l.GPath == "/etc/bashrc")) { io.L("setup-2.13.7-10.el9.noarch"); continue; }
                        if (owner == null || !inst.ContainsKey(owner)) { io.L("file " + full + " is not owned by any package"); st = 1; continue; }
                        var op = Packages.Get(owner);
                        if (list || conf || docs || info) targets.Add(op); else io.L(op.Nevra);
                    }
                    if (!(list || conf || docs || info)) return st;
                }
                else if (what)
                {
                    foreach (var w in names)
                    {
                        var hits = inst.Keys.Select(n => Packages.Get(n)).Where(p => p != null && (longs.Contains("--whatrequires") ? p.Requires.Contains(w) : (p.Name == w || Packages.Files(p).Contains(w)))).ToList();
                        if (hits.Count == 0) { io.L(longs.Contains("--whatrequires") ? "no package requires " + w : "no package provides " + w); st = 1; }
                        foreach (var p in hits) io.L(p.Nevra);
                    }
                    return st;
                }
                else if (pkgFile)
                {
                    foreach (var f in names)
                    {
                        var p = Packages.All.FirstOrDefault(x => x.Nevra + ".rpm" == Vfs.Base(f));
                        Loc l = null; try { l = m.V.Resolve(sh.P, f, true); } catch (VfsEx) { }
                        if (p == null || l == null || !l.Exists) { io.E("error: open of " + f + " failed: No such file or directory"); st = 1; continue; }
                        targets.Add(p);
                    }
                }
                else
                {
                    if (names.Count == 0) { io.E("rpm: no arguments given for query"); return 1; }
                    foreach (var n in names)
                    {
                        var p = Match(n, inst.Keys.Select(x => Packages.Get(x)).Where(x => x != null));
                        if (p == null) { io.L("package " + n + " is not installed"); st = 1; continue; }
                        targets.Add(p);
                    }
                }
                foreach (var p in targets.OrderBy(x => all ? x.Name : "", StringComparer.Ordinal))
                {
                    if (info) { io.L(PkgInfo(p, "(none)", null).Replace("Repository   : (none)\n", "Install Date : " + m.LocalTime(inst.ContainsKey(p.Name) ? long.Parse(inst[p.Name][4], CultureInfo.InvariantCulture) : m.Now).ToString("ddd dd MMM yyyy hh:mm:ss tt", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, m.Now) + "\nGroup        : Unspecified\nSignature    : RSA/SHA256, Mon 04 Nov 2024 03:10:19 AM EST, Key ID 199e2f91fd431d51\nPackager     : Red Hat, Inc. <http://bugzilla.redhat.com/bugzilla>\n").TrimEnd('\n')); continue; }
                    if (list || conf || docs)
                    {
                        var files = Packages.Files(p);
                        if (conf) files = files.Where(f => Packages.Configs.ContainsKey(f) && Packages.Configs[f].Config && f.StartsWith("/etc", StringComparison.Ordinal)).ToList();
                        if (docs) files = files.Where(f => f.StartsWith("/usr/share/doc", StringComparison.Ordinal) || f.StartsWith("/usr/share/man", StringComparison.Ordinal)).ToList();
                        if (files.Count == 0) io.L("(contains no files)");
                        foreach (var f in files) io.L(f);
                        continue;
                    }
                    if (req) { foreach (var r in p.Requires) io.L(r); io.L("rpmlib(CompressedFileNames) <= 3.0.4-1\nrpmlib(PayloadFilesHavePrefix) <= 4.0-1"); continue; }
                    if (scripts) { io.L(p.Units.Count > 0 ? "postinstall scriptlet (using /bin/sh):\n\n%systemd_post " + string.Join(" ", p.Units.ToArray()) : ""); continue; }
                    if (changelog) { io.L("* Mon Nov 04 2024 Red Hat, Inc. - " + p.Ver + "\n- Cert Lab: made-up changelog entry for practice."); continue; }
                    io.L(p.Nevra);
                }
                return st;
            }
            if (flags.Contains("i") || flags.Contains("U") || flags.Contains("F") || longs.Contains("--install") || longs.Contains("--upgrade"))
            {
                if (sh.P.Cr.Uid != 0) { io.E("error: can't create transaction lock on /var/lib/rpm/.rpm.lock (Permission denied)"); return 1; }
                bool nodeps = longs.Contains("--nodeps"), test = longs.Contains("--test"), v = flags.Contains("v"), hash = flags.Contains("h");
                var pk = new List<Pkg>();
                foreach (var f in names)
                {
                    Loc l = null;
                    try { l = m.V.Resolve(sh.P, f, true); } catch (VfsEx) { }
                    if (f.StartsWith("http", StringComparison.Ordinal)) { io.E("Retrieving " + f); }
                    if (l == null || !l.Exists) { io.E("error: open of " + f + " failed: No such file or directory"); return 1; }
                    var p = Packages.All.FirstOrDefault(x => x.Nevra + ".rpm" == Vfs.Base(f));
                    if (p == null) { io.E("error: " + f + ": not an rpm package (or package manifest)"); return 1; }
                    pk.Add(p);
                }
                if (pk.Count == 0) { io.E("rpm: no packages given for install"); return 1; }
                foreach (var p in pk) if (inst.ContainsKey(p.Name) && !longs.Contains("--replacepkgs") && !longs.Contains("--force")) { io.E("\tpackage " + p.Nevra + " is already installed"); return 1; }
                var missing = new List<string>();
                if (!nodeps) foreach (var p in pk) foreach (var r in p.Requires) if (!inst.ContainsKey(r) && !pk.Any(x => x.Name == r)) missing.Add("\t" + r + " is needed by " + p.Nevra);
                if (missing.Count > 0) { io.E("error: Failed dependencies:\n" + string.Join("\n", missing.ToArray())); return 1; }
                if (v) io.L("Verifying...                          ################################# [100%]\nPreparing...                          ################################# [100%]");
                if (test) return 0;
                if (v) io.L("Updating / installing...");
                int k = 0;
                foreach (var p in pk) { m.InstallPkg(p, "@commandline"); if (v || hash) io.L("   " + (++k) + ":" + (p.Name + "-" + p.Ver).PadRight(34) + "################################# [100%]"); }
                return 0;
            }
            if (flags.Contains("e") || longs.Contains("--erase"))
            {
                if (sh.P.Cr.Uid != 0) { io.E("error: can't create transaction lock on /var/lib/rpm/.rpm.lock (Permission denied)"); return 1; }
                bool nodeps = longs.Contains("--nodeps");
                var pk = new List<Pkg>();
                foreach (var n in names)
                {
                    var p = Match(n, inst.Keys.Select(x => Packages.Get(x)).Where(x => x != null));
                    if (p == null) { io.E("error: package " + n + " is not installed"); return 1; }
                    pk.Add(p);
                }
                var need = new List<string>();
                if (!nodeps) foreach (var p in pk) foreach (var o in inst.Keys) { var op = Packages.Get(o); if (op != null && !pk.Contains(op) && op.Requires.Contains(p.Name)) need.Add("\t" + p.Name + " is needed by (installed) " + op.Nevra); }
                if (need.Count > 0) { io.E("error: Failed dependencies:\n" + string.Join("\n", need.ToArray())); return 1; }
                foreach (var p in pk) m.RemovePkg(p);
                return 0;
            }
            if (flags.Contains("V") || longs.Contains("--verify"))
            {
                int st = 0;
                foreach (var n in names)
                {
                    var p = Match(n, inst.Keys.Select(x => Packages.Get(x)).Where(x => x != null));
                    if (p == null) { io.L("package " + n + " is not installed"); st = 1; continue; }
                    foreach (var kv in Packages.Configs.Where(x => x.Value.Pkg == p.Name && !x.Key.EndsWith("/", StringComparison.Ordinal)))
                    {
                        var cur = m.V.Cat(kv.Key);
                        if (cur == null) { io.L("missing   c " + kv.Key); st = 1; }
                        else if (cur != kv.Value.Text) { io.L("S.5....T.  c " + kv.Key); st = 1; }
                    }
                }
                return st;
            }
            if (flags.Contains("K") || longs.Contains("--checksig")) { foreach (var f in names) io.L(f + ": digests signatures OK"); return 0; }
            io.E("rpm: no operation specified\n(Cert Lab: rpm -q / -qa / -qi / -ql / -qc / -qd / -qf / -ivh / -Uvh / -e / -V / --import)");
            return 1;
        }
    }
}
