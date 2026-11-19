// Cert Lab - the practice network: one or two RHEL-like machines on 192.168.50.0/24 plus a utility server
// (utility.lab.example.com, 192.168.50.254) that offers what the exam's classroom offers: DNS for lab.example.com,
// package repositories over HTTP (BaseOS and AppStream), NFS exports, an NTP server and a container registry.
// It also keeps the clock: simulated time only moves when the window's timer, sleep or a test moves it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class NfsExport { public string Path; public string FsId; public string Clients = "*"; public string Opts = "rw,sync"; public bool RootSquash = true; }

    public partial class LabNet
    {
        public List<Machine> Machines = new List<Machine>();
        public long Now = 1772442000;       // 2026-03-02 09:00:00 UTC
        public int Seed = 1;
        public string Domain = "lab.example.com";
        public string UtilityIp = "192.168.50.254";
        public Dictionary<string, FileSys> NfsFs = new Dictionary<string, FileSys>();
        public List<NfsExport> Exports = new List<NfsExport>();
        public Dictionary<string, string> Dns = new Dictionary<string, string>();      // name -> ip
        public List<CImage> Registry = new List<CImage>();
        public bool InTick;
        public string Version = "1";

        [NonSerialized] public Action<string> Trace;
        /// <summary>Cyber Range only (RangeNet.cs): its pretend Windows hosts, routes between subnets, web apps and the
        /// noise log. Null for the courses' practice machines, which then behave exactly as before.</summary>
        [NonSerialized] public RangeNet Range;

        public Machine Get(string name) { return Machines.FirstOrDefault(m => m.Name == name); }

        public void AttachAll() { foreach (var m in Machines) m.Attach(this); }

        /// <summary>Moves the clock forward: cron, at, timers and background jobs happen on the way.</summary>
        public void Advance(long seconds)
        {
            if (seconds <= 0) return;
            if (InTick) { Now += seconds; return; }
            InTick = true;
            try
            {
                long end = Now + seconds;
                while (Now < end)
                {
                    long next = Math.Min(end, (Now / 60 + 1) * 60);   // stop at every minute boundary
                    Now = next;
                    foreach (var m in Machines.ToList()) m.Tick();
                }
            }
            finally { InTick = false; }
            if (Range != null) Range.AfterAdvance(seconds);
        }

        // ------------------------------------------------------------ names and addresses
        public string Resolve(Machine from, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (IsIp(name)) return name;
            string n = name.ToLowerInvariant().TrimEnd('.');
            if (n == "localhost" || n == "localhost.localdomain") return "127.0.0.1";
            // /etc/hosts first (nsswitch: files dns)
            var hosts = from.V.Cat("/etc/hosts") ?? "";
            foreach (var raw in hosts.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < p.Length; i++) if (p[i].ToLowerInvariant() == n) return p[0];
            }
            if (n == from.HostnameNow.ToLowerInvariant() || n == from.ShortHost.ToLowerInvariant()) { var a = from.FirstV4(); if (a != null) return a; }
            // DNS: only through a name server the machine can reach
            if (!from.HasDnsServer()) return null;
            var names = new List<string> { n };
            if (!n.Contains(".")) foreach (var s in from.SearchDomains()) names.Add(n + "." + s);
            foreach (var cand in names)
            {
                string ip;
                if (Dns.TryGetValue(cand, out ip)) return ip;
                var m = Machines.FirstOrDefault(x => x.HostnameNow.ToLowerInvariant() == cand && x.State != "off");
                if (m != null && m.FirstV4() != null) return m.FirstV4();
            }
            return null;
        }

        public static bool IsIp(string s)
        {
            var p = s.Split('.');
            if (p.Length != 4) return false;
            foreach (var x in p) { int n; if (!int.TryParse(x, out n) || n < 0 || n > 255) return false; }
            return true;
        }

        /// <summary>Who answers at an address: a machine, the utility server ("utility") or nobody.</summary>
        public object Owner(string ip)
        {
            if (ip == UtilityIp) return "utility";
            foreach (var m in Machines)
            {
                if (m.State == "off" || m.State == "grub") continue;
                foreach (var nif in m.Ifs) if (nif.Up) foreach (var a in nif.V4) if (a.Split('/')[0] == ip) return m;
            }
            if (Range != null) return Range.HostAt(ip);
            return null;
        }

        /// <summary>Can this machine send packets to that address (an address on the same network, or a gateway)?</summary>
        public bool Reachable(Machine from, string ip)
        {
            if (ip == "127.0.0.1" || ip.StartsWith("127.", StringComparison.Ordinal)) return true;
            foreach (var nif in from.Ifs)
            {
                if (!nif.Up || !nif.Carrier) continue;
                foreach (var a in nif.V4)
                {
                    if (a.Split('/')[0] == ip) return true;
                    if (SameNet(a, ip)) return Owner(ip) != null;
                }
            }
            // Cyber Range: a router between subnets lets some traffic through (RangeNet.Routes)
            if (Range != null && Range.RouteAllows(from, ip, -1)) return Owner(ip) != null;
            return false;
        }

        public static bool SameNet(string cidr, string ip)
        {
            var p = cidr.Split('/');
            int bits = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 24;
            uint a = IpNum(p[0]), b = IpNum(ip);
            uint mask = bits == 0 ? 0 : 0xFFFFFFFFu << (32 - bits);
            return (a & mask) == (b & mask);
        }
        public static uint IpNum(string ip)
        {
            var p = ip.Split('.');
            if (p.Length != 4) return 0;
            uint v = 0; foreach (var x in p) { uint n; uint.TryParse(x, out n); v = (v << 8) | (n & 255); }
            return v;
        }

        public bool NfsRootSquash(FileSys fs)
        {
            var e = Exports.FirstOrDefault(x => x.FsId == fs.Id);
            return e == null || e.RootSquash;
        }

        // ------------------------------------------------------------ building a network
        public static LabNet Create(int machines, int seed)
        {
            var n = new LabNet { Seed = seed };
            n.Dns["utility.lab.example.com"] = "192.168.50.254";
            n.Dns["repo.lab.example.com"] = "192.168.50.254";
            n.Dns["registry.lab.example.com"] = "192.168.50.254";
            n.Dns["nfs.lab.example.com"] = "192.168.50.254";
            n.Dns["ntp.lab.example.com"] = "192.168.50.254";
            n.Dns["server1.lab.example.com"] = "192.168.50.11";
            n.Dns["server2.lab.example.com"] = "192.168.50.12";
            n.BuildUtility();
            for (int i = 1; i <= Math.Max(1, Math.Min(2, machines)); i++)
            {
                var m = new Machine { Name = "server" + i, Seed = seed * 100 + i };
                m.Attach(n);
                m.Install("server" + i + ".lab.example.com", "192.168.50." + (10 + i));
                n.Machines.Add(m);
            }
            foreach (var m in n.Machines) m.Boot(Machine.Kernels[0], m.DefaultArgs(), false);
            return n;
        }

        void BuildUtility()
        {
            // NFS exports: a shared folder and home directories for "remote" users
            var shared = NewNfs("shared");
            var root = shared.Root;
            AddNfsFile(shared, root, "README.txt", "Files on the utility server's NFS share /exports/shared.\n", 0x1A4, 0, 0);
            AddNfsFile(shared, root, "notes.txt", "Mounted over NFS from utility.lab.example.com.\n", 0x1A4, 0, 0);
            root.Mode = 0x1FF;
            Exports.Add(new NfsExport { Path = "/exports/shared", FsId = shared.Id, Opts = "rw,sync,root_squash" });
            var pub = NewNfs("public");
            AddNfsFile(pub, pub.Root, "welcome.html", "<h1>Welcome</h1>\n", 0x1A4, 0, 0);
            Exports.Add(new NfsExport { Path = "/exports/public", FsId = pub.Id, Opts = "ro,sync" });
            foreach (var u in new[] { new[] { "remoteuser1", "1501" }, new[] { "remoteuser2", "1502" }, new[] { "remoteuser3", "1503" }, new[] { "production5", "1505" } })
            {
                var h = NewNfs("home-" + u[0]);
                int uid = int.Parse(u[1], CultureInfo.InvariantCulture);
                h.Root.Uid = uid; h.Root.Gid = uid; h.Root.Mode = 0x1C0;
                AddNfsFile(h, h.Root, "welcome.txt", "This is " + u[0] + "'s home directory on utility.lab.example.com (NFS).\n", 0x1A4, uid, uid);
                AddNfsFile(h, h.Root, ".bashrc", "# .bashrc\n", 0x1A4, uid, uid);
                Exports.Add(new NfsExport { Path = "/exports/home/" + u[0], FsId = h.Id, Opts = "rw,sync" });
            }
            Registry.AddRange(CImage.Catalog());
            BuildDvd();
            BuildWww();
        }

        /// <summary>The installation DVD (also what repo.lab.example.com serves): BaseOS and AppStream repositories.</summary>
        void BuildDvd()
        {
            var fs = new FileSys { Id = "dvd", Type = "iso9660", Bytes = 10900000000L, BaseUsed = 10900000000L, Label = "RHEL-9-5-0-BaseOS-x86_64" };
            var r = new Inode { Ino = 1, T = 'd', Mode = 0x16D, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:iso9660_t:s0", Mtime = 1730867419, Ctime = 1730867419, Atime = 1730867419 };
            fs.Nodes[1] = r;
            NfsFs[fs.Id] = fs;
            Func<Inode, string, Inode> dir = (p, n) => { int ino; if (p.Kids.TryGetValue(n, out ino)) return fs.Node(ino); var d = fs.NewNode('d', 0x16D, 0, 0, 1730867419); d.Ctx = r.Ctx; p.Kids[n] = d.Ino; p.Nlink++; return d; };
            Action<Inode, string, string, long> file = (p, n, text, size) => { var f = fs.NewNode('f', 0x124, 0, 0, 1730867419); f.Text = text; if (text == null) f.BinSize = size; f.Ctx = r.Ctx; p.Kids[n] = f.Ino; };
            file(r, "media.repo", "[InstallMedia]\nname=Red Hat Enterprise Linux 9.5.0\nmediaid=None\nmetadata_expire=-1\ngpgcheck=0\ncost=500\n", 0);
            file(r, "RPM-GPG-KEY-redhat-release", Packages.Configs["/etc/pki/rpm-gpg/RPM-GPG-KEY-redhat-release"].Text, 0);
            file(r, "RPM-GPG-KEY-redhat-beta", "-----BEGIN PGP PUBLIC KEY BLOCK-----\n(beta key stand-in)\n-----END PGP PUBLIC KEY BLOCK-----\n", 0);
            file(r, "EULA", "END USER LICENSE AGREEMENT (Cert Lab stand-in: the real DVD carries Red Hat's agreement here.)\n", 0);
            file(r, "GPL", "The GNU General Public License applies to many packages on this disc (stand-in text).\n", 0);
            file(r, "extra_files.json", "{\"data\": [], \"header\": {\"version\": \"1.0\"}}\n", 0);
            dir(r, "EFI"); dir(r, "images"); dir(r, "isolinux");
            foreach (var repo in new[] { "BaseOS", "AppStream" })
            {
                var rd = dir(r, repo);
                var pk = dir(rd, "Packages");
                var meta = dir(rd, "repodata");
                file(meta, "repomd.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<repomd xmlns=\"http://linux.duke.edu/metadata/repo\" xmlns:rpm=\"http://linux.duke.edu/metadata/rpm\">\n  <revision>1730867419</revision>\n  <data type=\"primary\">\n    <location href=\"repodata/primary.xml.gz\"/>\n  </data>\n</repomd>\n", 0);
                file(meta, "primary.xml.gz", null, 2400000);
                file(meta, "comps.xml", "<comps><!-- package groups --></comps>\n", 0);
                foreach (var p in Packages.All.Where(x => x.Repo == repo))
                {
                    var letter = dir(pk, p.Name.Substring(0, 1).ToLowerInvariant());
                    file(letter, p.Nevra + ".rpm", null, p.SizeKb * 1024);
                }
            }
        }

        /// <summary>Files the utility web server offers besides the repositories (for wget / curl practice).</summary>
        void BuildWww()
        {
            var fs = new FileSys { Id = "www", Type = "xfs", Bytes = 1L << 34 };
            var r = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:httpd_sys_content_t:s0", Mtime = Now, Ctime = Now, Atime = Now };
            fs.Nodes[1] = r;
            NfsFs[fs.Id] = fs;
            Action<Inode, string, string> file = (p, n, text) => { var f = fs.NewNode('f', 0x1A4, 0, 0, Now); f.Text = text; f.Ctx = r.Ctx; p.Kids[n] = f.Ino; };
            file(r, "index.html", "<html><body><h1>utility.lab.example.com</h1><p>Repositories: <a href=\"/BaseOS/\">BaseOS</a> <a href=\"/AppStream/\">AppStream</a>. Practice files: <a href=\"/files/\">files</a>.</p></body></html>\n");
            file(r, "RPM-GPG-KEY-redhat-release", Packages.Configs["/etc/pki/rpm-gpg/RPM-GPG-KEY-redhat-release"].Text);
            var files = fs.NewNode('d', 0x1ED, 0, 0, Now); files.Ctx = r.Ctx; r.Kids["files"] = files.Ino;
            file(files, "users.txt", "alice\nbob\ncarol\ndavid\nerin\n");
            file(files, "words.txt", "apple\nbanana\ncherry\nstarfish\nstart\nstartle\nmustard\nlast\n");
            file(files, "index.html", "<html><body><h1>Practice web page</h1><p>Served by utility.lab.example.com.</p></body></html>\n");
            file(files, "report.csv", "host,service,port\nserver1,sshd,22\nserver1,httpd,80\nserver2,sshd,22\nserver2,chronyd,123\n");
            file(files, "Containerfile", "FROM registry.lab.example.com/ubi9/ubi:latest\nRUN echo \"Built in Cert Lab\" > /etc/motd\nCMD [\"/bin/bash\"]\n");
            file(files, "hello.sh", "#!/bin/bash\necho \"Hello from $(hostname)\"\n");
        }

        FileSys NewNfs(string name)
        {
            var fs = new FileSys { Id = "nfs-" + name, Type = "nfs4", Bytes = 50L * 1024 * 1024 * 1024, BaseUsed = 9L * 1024 * 1024 * 1024 };
            var r = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Mtime = Now, Ctime = Now, Atime = Now, Ctx = "system_u:object_r:nfs_t:s0" };
            fs.Nodes[1] = r;
            NfsFs[fs.Id] = fs;
            return fs;
        }
        void AddNfsFile(FileSys fs, Inode dir, string name, string text, int mode, int uid, int gid)
        {
            var n = fs.NewNode('f', mode, uid, gid, Now);
            n.Text = text; n.Ctx = "system_u:object_r:nfs_t:s0";
            dir.Kids[name] = n.Ino;
        }

        /// <summary>A folder above exports (NFSv4 pseudo root, e.g. /exports/home): shows the exports in it as folders.</summary>
        public FileSys VirtualNfsDir(string path)
        {
            string id = "nfs-virt" + path.Replace('/', '-');
            FileSys fs;
            if (!NfsFs.TryGetValue(id, out fs)) fs = NewNfs("virt" + path.Replace('/', '-'));
            foreach (var e in Exports.Where(x => x.Path.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                string rest = e.Path.Substring(path.Length + 1).Split('/')[0];
                if (!fs.Root.Kids.ContainsKey(rest)) { var d = fs.NewNode('d', 0x1ED, 0, 0, Now); d.Ctx = "system_u:object_r:nfs_t:s0"; fs.Root.Kids[rest] = d.Ino; }
            }
            return fs;
        }

        public NfsExport FindExport(string path)
        {
            path = path.TrimEnd('/');
            return Exports.FirstOrDefault(e => e.Path == path);
        }

        // ------------------------------------------------------------ saving
        public string Save() { return Json.Write(Ser.ToJson(this)); }
        public static LabNet Load(string json)
        {
            var n = Ser.FromJson<LabNet>(Json.Parse(json));
            n.AttachAll();
            foreach (var m in n.Machines) { m.Sessions = new List<Session>(); m.AfterLoad(); }
            return n;
        }
    }

    /// <summary>Time zones the simulator knows (fixed offsets with US / EU daylight saving rules).</summary>
    public static class Tz
    {
        // name, standard offset in minutes, DST rule (us, eu, none)
        public static readonly string[][] Zones = new[]
        {
            new[] { "UTC", "0", "none" }, new[] { "Etc/UTC", "0", "none" },
            new[] { "America/New_York", "-300", "us" }, new[] { "America/Chicago", "-360", "us" }, new[] { "America/Denver", "-420", "us" },
            new[] { "America/Phoenix", "-420", "none" }, new[] { "America/Los_Angeles", "-480", "us" }, new[] { "America/Anchorage", "-540", "us" },
            new[] { "Pacific/Honolulu", "-600", "none" }, new[] { "America/Toronto", "-300", "us" }, new[] { "America/Vancouver", "-480", "us" },
            new[] { "America/Mexico_City", "-360", "none" }, new[] { "America/Sao_Paulo", "-180", "none" }, new[] { "America/Bogota", "-300", "none" },
            new[] { "Europe/London", "0", "eu" }, new[] { "Europe/Dublin", "0", "eu" }, new[] { "Europe/Lisbon", "0", "eu" }, new[] { "Europe/Paris", "60", "eu" },
            new[] { "Europe/Berlin", "60", "eu" }, new[] { "Europe/Madrid", "60", "eu" }, new[] { "Europe/Rome", "60", "eu" }, new[] { "Europe/Amsterdam", "60", "eu" },
            new[] { "Europe/Prague", "60", "eu" }, new[] { "Europe/Warsaw", "60", "eu" }, new[] { "Europe/Athens", "120", "eu" }, new[] { "Europe/Helsinki", "120", "eu" },
            new[] { "Europe/Moscow", "180", "none" }, new[] { "Africa/Cairo", "120", "none" }, new[] { "Africa/Johannesburg", "120", "none" }, new[] { "Africa/Lagos", "60", "none" },
            new[] { "Asia/Dubai", "240", "none" }, new[] { "Asia/Kolkata", "330", "none" }, new[] { "Asia/Shanghai", "480", "none" }, new[] { "Asia/Singapore", "480", "none" },
            new[] { "Asia/Tokyo", "540", "none" }, new[] { "Asia/Seoul", "540", "none" }, new[] { "Australia/Sydney", "600", "none" }, new[] { "Australia/Perth", "480", "none" },
            new[] { "Pacific/Auckland", "720", "none" },
        };

        public static string[] Find(string name) { return Zones.FirstOrDefault(z => z[0] == name); }

        public static int OffsetMinutes(string zone, long t)
        {
            var z = Find(zone) ?? Zones[0];
            int off = int.Parse(z[1], CultureInfo.InvariantCulture);
            var utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t);
            if (z[2] == "us")
            {
                var start = NthSunday(utc.Year, 3, 2).AddHours(2).AddMinutes(-off);
                var end = NthSunday(utc.Year, 11, 1).AddHours(2).AddMinutes(-(off + 60));
                if (utc >= start && utc < end) off += 60;
            }
            else if (z[2] == "eu")
            {
                var start = LastSunday(utc.Year, 3).AddHours(1);
                var end = LastSunday(utc.Year, 10).AddHours(1);
                if (utc >= start && utc < end) off += 60;
            }
            return off;
        }
        static DateTime NthSunday(int y, int month, int n)
        {
            var d = new DateTime(y, month, 1);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d.AddDays(7 * (n - 1));
        }
        static DateTime LastSunday(int y, int month)
        {
            var d = new DateTime(y, month, 1).AddMonths(1).AddDays(-1);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }
        public static DateTime ToLocal(string zone, long t)
        {
            return new DateTime(1970, 1, 1).AddSeconds(t).AddMinutes(OffsetMinutes(zone, t));
        }
        public static string Abbrev(string zone, long t)
        {
            var z = Find(zone) ?? Zones[0];
            int off = OffsetMinutes(zone, t);
            bool dst = off != int.Parse(z[1], CultureInfo.InvariantCulture);
            switch (zone)
            {
                case "America/New_York": case "America/Toronto": return dst ? "EDT" : "EST";
                case "America/Chicago": return dst ? "CDT" : "CST";
                case "America/Denver": return dst ? "MDT" : "MST";
                case "America/Phoenix": return "MST";
                case "America/Los_Angeles": case "America/Vancouver": return dst ? "PDT" : "PST";
                case "America/Anchorage": return dst ? "AKDT" : "AKST";
                case "Pacific/Honolulu": return "HST";
                case "Europe/London": return dst ? "BST" : "GMT";
                case "Europe/Dublin": return dst ? "IST" : "GMT";
                case "Europe/Lisbon": return dst ? "WEST" : "WET";
                case "Asia/Kolkata": return "IST";
                case "Asia/Tokyo": return "JST";
                case "Asia/Shanghai": return "CST";
                case "UTC": case "Etc/UTC": return "UTC";
            }
            if (z[2] == "eu" && off >= 60 && off <= 180) return off == 60 ? "CET" : off == 120 ? (dst ? "CEST" : "EET") : "EEST";
            int h = off / 60, m = Math.Abs(off % 60);
            return (off >= 0 ? "+" : "-") + Math.Abs(h).ToString("00") + (m != 0 ? m.ToString("00") : "");
        }
        public static string OffsetText(string zone, long t)
        {
            int off = OffsetMinutes(zone, t);
            return (off >= 0 ? "+" : "-") + (Math.Abs(off) / 60).ToString("00") + (Math.Abs(off) % 60).ToString("00");
        }
    }
}
