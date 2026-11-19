// Cert Lab - one simulated RHEL-like machine: its disks and file systems (what survives a reboot) and its running
// state (mounts, services, processes, network, firewall, SELinux mode, journal), which a reboot rebuilds from the
// files in /etc exactly as the real boot does. The other parts of the class are in M*.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class JournalEntry
    {
        public long Time; public int Boot; public string Ident = ""; public int Pid; public int Pri = 6; public string Unit = ""; public string Msg = ""; public string UserUnit = ""; public int Uid;
    }

    public class ProcEntry
    {
        public int Pid, PPid = 1, Uid; public string User = "root"; public string Cmd = ""; public string Comm = "";
        public int Nice; public double Cpu; public double Mem; public long Rss = 4000; public long Vsz = 20000; public string State = "S";
        public long Start; public string Tty = "?"; public string Unit = ""; public string Domain = "system_r:unconfined_service_t"; public long EndsAt = -1;
        public string Group = "root"; public string Session = ""; public int Prio { get { return 20 + Nice; } }
        public string Container = "";
    }

    public class ServiceState
    {
        public string Unit; public string Active = "inactive"; public string Sub = "dead"; public int Pid; public long Since; public string Result = "success";
        public string Owner = "";     // user units: the user
        public int Tasks = 1; public long MemKb = 2048;
        public List<int> Ports = new List<int>();
    }

    public class NetIf
    {
        public string Name; public string Mac; public bool Up = true; public string Conn = "";
        public List<string> V4 = new List<string>();   // 192.168.50.11/24
        public List<string> V6 = new List<string>();
        public string Gw4 = ""; public string Gw6 = ""; public List<string> Dns = new List<string>(); public List<string> Search = new List<string>();
        public bool Carrier = true;
    }

    public class FwZone
    {
        public string Name; public string Target = "default";
        public List<string> Services = new List<string>(); public List<string> Ports = new List<string>(); public List<string> Sources = new List<string>();
        public List<string> Interfaces = new List<string>(); public List<string> Rich = new List<string>(); public bool Masquerade;
        public List<string> Forward = new List<string>();
        public string Short = "";
        public FwZone Copy() { return new FwZone { Name = Name, Target = Target, Services = new List<string>(Services), Ports = new List<string>(Ports), Sources = new List<string>(Sources), Interfaces = new List<string>(Interfaces), Rich = new List<string>(Rich), Masquerade = Masquerade, Forward = new List<string>(Forward), Short = Short }; }
    }

    public class SwapArea { public string Dev; public long Bytes; public int Prio = -2; public long UsedKb; }

    public class PwEnt { public string Name, Pass = "x", Gecos = "", Home = "", Shell = ""; public int Uid, Gid; }
    public class GrEnt { public string Name, Pass = "x"; public int Gid; public List<string> Members = new List<string>(); }
    public class ShEnt { public string Name, Hash = "!!"; public string LastChg = "", Min = "", Max = "", Warn = "", Inactive = "", Expire = "", Reserved = ""; }

    public partial class Machine
    {
        // ---- what survives a reboot
        public string Name = "server1";          // the simulator's name for it (window titles); the hostname lives in /etc/hostname
        public string MachineId = "";
        public Dictionary<string, FileSys> Fss = new Dictionary<string, FileSys>();
        public List<Disk> Disks = new List<Disk>();
        public Dictionary<string, BlockContent> Content = new Dictionary<string, BlockContent>();
        public List<VolGroup> Vgs = new List<VolGroup>();
        public int Seed = 1;
        public int Counter = 1;
        public string RootPassword = "";          // only for the "you forgot it" setups: the hash lives in /etc/shadow
        public string Banner = "";                 // Cyber Range: the login banner (/etc/issue text); empty = the RHEL text
        public List<string> Macs = new List<string>();
        public List<string> NicNames = new List<string>();

        // ---- the running system (rebuilt at boot)
        public List<Mount> Mounts = new List<Mount>();
        public List<SwapArea> Swaps = new List<SwapArea>();
        public List<ProcEntry> Procs = new List<ProcEntry>();
        public Dictionary<string, ServiceState> Services = new Dictionary<string, ServiceState>();
        public Dictionary<string, string> LoadedUnits = new Dictionary<string, string>();   // unit -> text seen at the last daemon-reload
        public List<JournalEntry> Journal = new List<JournalEntry>();
        public List<NetIf> Ifs = new List<NetIf>();
        public Dictionary<string, FwZone> FwRuntime = new Dictionary<string, FwZone>();
        public string FwDefaultZone = "public";
        public bool FwRunning;
        public string SeMode = "enforcing";
        public Dictionary<string, string> SeBools = new Dictionary<string, string>();   // runtime values that differ from the policy's
        public List<SePort> SeLocalPorts = new List<SePort>();
        public string Hostname = "";
        public string TransientHostname = "";
        public int BootId;
        public long BootTime;
        public string State = "running";          // running, off, grub, emergency, rescue, rdbreak, initbash, relabel, poweroff
        public string Target = "multi-user.target";
        public string Cmdline = "";
        public string TunedProfile = "";
        public int NextPid = 1000;
        public int AuditSerial = 100;
        public Dictionary<string, string> Sysctl = new Dictionary<string, string>();
        public List<string> BootLog = new List<string>();
        public List<string> FailedMounts = new List<string>();
        public bool JournalPersistentThisBoot;
        public int LastJournalBoot;
        public long ChronySyncedAt = -1;
        public string ChronySource = "";
        public List<Container> Containers = new List<Container>();
        public List<CImage> Images = new List<CImage>();
        public Dictionary<string, long> CronLastRun = new Dictionary<string, long>();
        public long LastTick;
        public Dictionary<string, long> TimerLast = new Dictionary<string, long>();
        public List<string> Logins = new List<string>();    // "user tty from time"

        [NonSerialized] public LabNet Net;
        [NonSerialized] public Vfs V;
        [NonSerialized] public List<Session> Sessions = new List<Session>();

        public Machine() { }

        public long Now { get { return Net == null ? 1772442000 : Net.Now; } }
        public void Attach(LabNet net) { Net = net; V = new Vfs(this); if (Sessions == null) Sessions = new List<Session>(); }

        public string NewUuid()
        {
            // deterministic: from the machine's seed and a counter
            var r = new Random(Seed * 7919 + (Counter++) * 104729);
            var b = new byte[16]; r.NextBytes(b);
            b[6] = (byte)((b[6] & 0x0F) | 0x40); b[8] = (byte)((b[8] & 0x3F) | 0x80);
            var g = new StringBuilder();
            for (int i = 0; i < 16; i++) { if (i == 4 || i == 6 || i == 8 || i == 10) g.Append('-'); g.Append(b[i].ToString("x2")); }
            return g.ToString();
        }
        public string NewHex(int n)
        {
            var r = new Random(Seed * 31337 + (Counter++) * 7);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append("0123456789abcdef"[r.Next(16)]);
            return sb.ToString();
        }
        public int Rand(int max) { var r = new Random(Seed * 92821 + (Counter++) * 31); return r.Next(max); }

        public string HostnameNow { get { return string.IsNullOrEmpty(Hostname) ? "localhost" : Hostname; } }
        public string ShortHost { get { var h = HostnameNow; int i = h.IndexOf('.'); return i > 0 ? h.Substring(0, i) : h; } }

        // ------------------------------------------------------------ users and groups (from the files in /etc)
        public List<PwEnt> Passwd()
        {
            var l = new List<PwEnt>();
            string t = V.Cat("/etc/passwd") ?? "";
            foreach (var line in t.Split('\n'))
            {
                var p = line.Split(':');
                if (p.Length < 7) continue;
                int uid, gid;
                if (!int.TryParse(p[2], out uid) || !int.TryParse(p[3], out gid)) continue;
                l.Add(new PwEnt { Name = p[0], Pass = p[1], Uid = uid, Gid = gid, Gecos = p[4], Home = p[5], Shell = p[6] });
            }
            return l;
        }
        public List<GrEnt> Groups()
        {
            var l = new List<GrEnt>();
            string t = V.Cat("/etc/group") ?? "";
            foreach (var line in t.Split('\n'))
            {
                var p = line.Split(':');
                if (p.Length < 4) continue;
                int gid;
                if (!int.TryParse(p[2], out gid)) continue;
                var g = new GrEnt { Name = p[0], Pass = p[1], Gid = gid };
                foreach (var m in p[3].Split(',')) if (m.Trim().Length > 0) g.Members.Add(m.Trim());
                l.Add(g);
            }
            return l;
        }
        public List<ShEnt> Shadow()
        {
            var l = new List<ShEnt>();
            string t = V.Cat("/etc/shadow") ?? "";
            foreach (var line in t.Split('\n'))
            {
                var p = line.Split(':');
                if (p.Length < 2) continue;
                var s = new ShEnt { Name = p[0], Hash = p[1] };
                if (p.Length > 2) s.LastChg = p[2]; if (p.Length > 3) s.Min = p[3]; if (p.Length > 4) s.Max = p[4];
                if (p.Length > 5) s.Warn = p[5]; if (p.Length > 6) s.Inactive = p[6]; if (p.Length > 7) s.Expire = p[7]; if (p.Length > 8) s.Reserved = p[8];
                l.Add(s);
            }
            return l;
        }
        public void WritePasswd(List<PwEnt> l) { SysWrite("/etc/passwd", string.Join("", l.Select(p => p.Name + ":" + p.Pass + ":" + p.Uid + ":" + p.Gid + ":" + p.Gecos + ":" + p.Home + ":" + p.Shell + "\n").ToArray())); }
        public void WriteGroups(List<GrEnt> l)
        {
            SysWrite("/etc/group", string.Join("", l.Select(g => g.Name + ":x:" + g.Gid + ":" + string.Join(",", g.Members.ToArray()) + "\n").ToArray()));
            // gshadow follows the group file
            var gs = new StringBuilder();
            var old = (V.Cat("/etc/gshadow") ?? "").Split('\n').Where(x => x.Length > 0).ToDictionary(x => x.Split(':')[0], x => x);
            foreach (var g in l)
            {
                string pw = "!";
                string o; if (old.TryGetValue(g.Name, out o)) { var p = o.Split(':'); if (p.Length > 1) pw = p[1]; }
                gs.Append(g.Name + ":" + pw + "::" + string.Join(",", g.Members.ToArray()) + "\n");
            }
            SysWrite("/etc/gshadow", gs.ToString(), 0, "system_u:object_r:shadow_t:s0");
        }
        public void WriteShadow(List<ShEnt> l)
        {
            SysWrite("/etc/shadow", string.Join("", l.Select(s => s.Name + ":" + s.Hash + ":" + s.LastChg + ":" + s.Min + ":" + s.Max + ":" + s.Warn + ":" + s.Inactive + ":" + s.Expire + ":" + s.Reserved + "\n").ToArray()), 0, null);
        }

        /// <summary>Rewrites a system file in place (keeps its inode, owner, mode and label; makes it if missing).</summary>
        public void SysWrite(string path, string text) { SysWrite(path, text, -1, null); }
        public void SysWrite(string path, string text, int mode, string ctx)
        {
            Loc l = null;
            try { l = V.G(path); } catch (VfsEx) { }
            if (l != null && l.Exists && l.Node.T == 'f')
            {
                if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
                l.Node.Text = text; l.Node.BinSize = -1; l.Node.Mtime = Now; l.Node.Ctime = Now;
                if (mode >= 0) l.Node.Mode = mode;
                return;
            }
            if (l != null && l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
            V.Put(path, text, mode >= 0 ? mode : 0x1A4, 0, 0, ctx);
        }

        public PwEnt User(string name) { return Passwd().FirstOrDefault(p => p.Name == name); }
        public PwEnt UserById(int uid) { return Passwd().FirstOrDefault(p => p.Uid == uid); }
        public GrEnt Group(string name) { return Groups().FirstOrDefault(g => g.Name == name); }
        public GrEnt GroupById(int gid) { return Groups().FirstOrDefault(g => g.Gid == gid); }
        public string UName(int uid) { var p = UserById(uid); return p == null ? uid.ToString(CultureInfo.InvariantCulture) : p.Name; }
        public string GName(int gid) { var g = GroupById(gid); return g == null ? gid.ToString(CultureInfo.InvariantCulture) : g.Name; }

        public Cred CredFor(string user)
        {
            var p = User(user);
            if (p == null) return null;
            var c = new Cred { Uid = p.Uid, Gid = p.Gid, User = p.Name };
            foreach (var g in Groups()) if (g.Members.Contains(p.Name) && g.Gid != p.Gid) c.Groups.Add(g.Gid);
            return c;
        }

        public static string HashPassword(string salt, string pw)
        {
            using (var sha = System.Security.Cryptography.SHA512.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "$" + pw + "$certlab"));
                string b = Convert.ToBase64String(h).Replace('+', '.').Replace("=", "");
                return "$6$" + salt + "$" + b.Substring(0, 86 > b.Length ? b.Length : 86);
            }
        }
        public string NewSalt() { var r = new Random(Seed * 13 + (Counter++) * 101); const string a = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"; var sb = new StringBuilder(); for (int i = 0; i < 16; i++) sb.Append(a[r.Next(a.Length)]); return sb.ToString(); }

        public bool CheckPassword(string user, string pw)
        {
            var s = Shadow().FirstOrDefault(x => x.Name == user);
            if (s == null || s.Hash.Length < 4 || !s.Hash.StartsWith("$6$", StringComparison.Ordinal)) return false;
            var parts = s.Hash.Split('$');
            if (parts.Length < 4) return false;
            return HashPassword(parts[2], pw) == s.Hash;
        }
        public void SetPassword(string user, string pw)
        {
            var l = Shadow();
            var s = l.FirstOrDefault(x => x.Name == user);
            if (s == null) { s = new ShEnt { Name = user }; l.Add(s); }
            s.Hash = HashPassword(NewSalt(), pw);
            s.LastChg = DaysNow.ToString(CultureInfo.InvariantCulture);
            WriteShadow(l);
        }
        public long DaysNow { get { return Now / 86400; } }

        // ------------------------------------------------------------ the journal and log files
        public void Log(string ident, int pid, int pri, string msg, string unit)
        {
            Journal.Add(new JournalEntry { Time = Now, Boot = BootId, Ident = ident, Pid = pid, Pri = pri, Msg = msg, Unit = unit ?? "" });
            if (Journal.Count > 6000) Journal.RemoveRange(0, 1000);
            Syslog(ident, pid, pri, msg, FacilityOf(ident));
        }
        static string FacilityOf(string ident)
        {
            if (ident == "sshd" || ident == "sudo" || ident == "su" || ident == "login" || ident == "passwd" || ident == "useradd" || ident == "usermod" || ident == "userdel" || ident == "groupadd" || ident == "chage") return "authpriv";
            if (ident == "CROND" || ident == "crond" || ident == "crontab" || ident == "atd") return "cron";
            if (ident == "kernel") return "kern";
            if (ident.StartsWith("local", StringComparison.Ordinal)) return ident;
            return "daemon";
        }

        /// <summary>rsyslog: the rules in /etc/rsyslog.conf (and rsyslog.d) decide which files get a message.</summary>
        public void Syslog(string ident, int pid, int pri, string msg, string facility)
        {
            if (!ServiceActive("rsyslog.service")) return;
            string line = SyslogTime(Now) + " " + ShortHost + " " + ident + (pid > 0 ? "[" + pid + "]" : "") + ": " + msg + "\n";
            foreach (var rule in RsyslogRules())
                if (RsyslogMatch(rule[0], facility, pri)) AppendLog(rule[1], line);
        }
        public string SyslogTime(long t) { var d = LocalTime(t); return d.ToString("MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2) + " " + d.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }

        List<string[]> RsyslogRules()
        {
            var l = new List<string[]>();
            var files = new List<string> { "/etc/rsyslog.conf" };
            try { var d = V.G("/etc/rsyslog.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) if (k.EndsWith(".conf", StringComparison.Ordinal)) files.Add("/etc/rsyslog.d/" + k); } catch (VfsEx) { }
            foreach (var f in files)
            {
                var t = V.Cat(f);
                if (t == null) continue;
                foreach (var raw in t.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("$", StringComparison.Ordinal) || line.StartsWith("module(", StringComparison.Ordinal) || line.StartsWith("global(", StringComparison.Ordinal) || line.StartsWith("include(", StringComparison.Ordinal) || line.StartsWith("input(", StringComparison.Ordinal)) continue;
                    var p = System.Text.RegularExpressions.Regex.Split(line, "\\s+");
                    if (p.Length < 2) continue;
                    string dest = p[p.Length - 1].TrimStart('-');
                    if (!dest.StartsWith("/", StringComparison.Ordinal)) continue;
                    l.Add(new[] { p[0], dest });
                }
            }
            return l;
        }
        static readonly string[] PriNames = { "emerg", "alert", "crit", "err", "warning", "notice", "info", "debug" };
        public static int PriOf(string s)
        {
            if (s == "error") return 3; if (s == "warn") return 4; if (s == "panic") return 0;
            int i = Array.IndexOf(PriNames, s); return i;
        }
        static bool RsyslogMatch(string selectors, string facility, int pri)
        {
            bool match = false;
            foreach (var sel in selectors.Split(';'))
            {
                int dot = sel.IndexOf('.');
                if (dot < 0) continue;
                string facs = sel.Substring(0, dot), lvl = sel.Substring(dot + 1);
                bool facMatch = facs == "*" || facs.Split(',').Contains(facility);
                if (!facMatch) continue;
                if (lvl == "none") { match = false; continue; }
                bool eq = lvl.StartsWith("=", StringComparison.Ordinal); if (eq) lvl = lvl.Substring(1);
                if (lvl == "*") { match = true; continue; }
                int lp = PriOf(lvl);
                if (lp < 0) continue;
                match = eq ? pri == lp : pri <= lp;
            }
            return match;
        }

        public void AppendLog(string path, string line)
        {
            try
            {
                var l = V.G(path);
                if (l.Mnt != null && l.Mnt.Ro) return;
                if (l.Exists) { if (l.Node.T == 'f') { l.Node.Text = (l.Node.Text ?? "") + line; l.Node.Mtime = Now; } return; }
                if (l.Parent == null) return;
                var n = l.PFs.NewNode('f', 0x180, 0, 0, Now);
                n.Text = line;
                n.Ctx = Selinux.DefaultContext(this, path, 'f');
                l.Parent.Kids[l.Name] = n.Ino;
            }
            catch (VfsEx) { }
        }

        // ------------------------------------------------------------ time
        public DateTime LocalTime(long t) { return Tz.ToLocal(TimeZoneName, t); }
        public string TimeZoneName
        {
            get
            {
                try
                {
                    var l = V.GNoFollow("/etc/localtime");
                    if (l.Exists && l.Node.T == 'l') { var tg = l.Node.Target; int i = tg.IndexOf("zoneinfo/", StringComparison.Ordinal); if (i >= 0) return tg.Substring(i + 9); }
                }
                catch (VfsEx) { }
                return "UTC";
            }
        }

        // ------------------------------------------------------------ processes
        public ProcEntry Spawn(string user, string cmd, string unit, string domain)
        {
            var p = new ProcEntry { Pid = NextPid, User = user, Cmd = cmd, Comm = CommOf(cmd), Start = Now, Unit = unit ?? "", Domain = domain ?? "system_r:unconfined_service_t" };
            NextPid += 1 + (NextPid % 7);
            var pw = User(user);
            p.Uid = pw == null ? 0 : pw.Uid;
            Procs.Add(p);
            return p;
        }
        public static string CommOf(string cmd)
        {
            var w = cmd.Trim().Split(' ')[0];
            if (w.StartsWith("[", StringComparison.Ordinal) && w.EndsWith("]", StringComparison.Ordinal)) w = w.Substring(1, w.Length - 2);   // [kthreadd]: the kernel thread kthreadd
            int i = w.LastIndexOf('/'); if (i >= 0) w = w.Substring(i + 1);
            w = w.TrimEnd(':');   // "sshd: /usr/sbin/sshd -D [listener]": the program is sshd
            if (w.Length > 15) w = w.Substring(0, 15);
            return w;
        }
        public ProcEntry ProcByPid(int pid) { return Procs.FirstOrDefault(p => p.Pid == pid); }
        public int ServicePid(string unit) { ServiceState s; if (Services.TryGetValue(unit, out s) && s.Active == "active") return s.Pid; return 0; }

        // ------------------------------------------------------------ /proc
        public void RefreshProc(FileSys fs, Inode dir)
        {
            if (dir.Ino != fs.RootIno) return;
            var root = fs.Root;
            Action<string, string> put = (name, text) =>
            {
                int ino; Inode n;
                if (root.Kids.TryGetValue(name, out ino)) n = fs.Node(ino);
                else { n = fs.NewNode('f', 0x124, 0, 0, Now); root.Kids[name] = n.Ino; n.Ctx = "system_u:object_r:proc_t:s0"; }
                n.Text = text;
            };
            put("cmdline", Cmdline + "\n");
            put("version", "Linux version " + KernelRelease + " (mockbuild@cert-lab) (gcc (GCC) 11.5.0 20240719 (Red Hat 11.5.0-2), GNU ld version 2.35.2-54.el9) #1 SMP PREEMPT_DYNAMIC\n");
            put("uptime", string.Format(CultureInfo.InvariantCulture, "{0}.00 {1}.00\n", Math.Max(1, Now - BootTime), Math.Max(1, Now - BootTime) * 2));
            put("loadavg", LoadAvg() + " 1/180 " + NextPid + "\n");
            put("meminfo", MemInfo());
            put("cpuinfo", CpuInfo());
            put("swaps", ProcSwaps());
            put("mounts", ProcMounts());
            put("partitions", ProcPartitions());
            put("filesystems", "nodev\tsysfs\nnodev\ttmpfs\nnodev\tproc\nnodev\tdevtmpfs\nnodev\tautofs\nnodev\tnfs4\n\txfs\n\text4\n\tvfat\n");
            // a few sysctls
            foreach (var kv in SysctlAll())
            {
                var path = kv.Key.Replace('.', '/');
                var parts = ("sys/" + path).Split('/');
                Inode cur = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    int ino;
                    if (!cur.Kids.TryGetValue(parts[i], out ino)) { var d = fs.NewNode('d', 0x16D, 0, 0, Now); d.Ctx = "system_u:object_r:sysctl_t:s0"; cur.Kids[parts[i]] = d.Ino; cur = d; }
                    else cur = fs.Node(ino);
                }
                int fi; Inode f;
                if (cur.Kids.TryGetValue(parts[parts.Length - 1], out fi)) f = fs.Node(fi);
                else { f = fs.NewNode('f', 0x1A4, 0, 0, Now); f.Ctx = "system_u:object_r:sysctl_t:s0"; cur.Kids[parts[parts.Length - 1]] = f.Ino; }
                f.Text = kv.Value + "\n";
            }
            // a directory per process
            foreach (var k in root.Kids.Keys.ToList()) { int dummy; if (int.TryParse(k, out dummy) && ProcByPid(dummy) == null) { fs.Nodes.Remove(root.Kids[k]); root.Kids.Remove(k); } }
            foreach (var p in Procs)
            {
                string k = p.Pid.ToString(CultureInfo.InvariantCulture);
                int ino; Inode d;
                if (!root.Kids.TryGetValue(k, out ino)) { d = fs.NewNode('d', 0x16D, p.Uid, 0, Now); d.Ctx = "system_u:system_r:" + Selinux.TypeOf("x:" + p.Domain) + ":s0"; root.Kids[k] = d.Ino; }
                else d = fs.Node(ino);
                int ci; Inode cn;
                if (!d.Kids.TryGetValue("cmdline", out ci)) { cn = fs.NewNode('f', 0x124, p.Uid, 0, Now); d.Kids["cmdline"] = cn.Ino; } else cn = fs.Node(ci);
                cn.Text = p.Cmd.Replace(' ', '\0');
                if (!d.Kids.TryGetValue("status", out ci)) { cn = fs.NewNode('f', 0x124, p.Uid, 0, Now); d.Kids["status"] = cn.Ino; } else cn = fs.Node(ci);
                cn.Text = "Name:\t" + p.Comm + "\nState:\t" + p.State + " (sleeping)\nPid:\t" + p.Pid + "\nPPid:\t" + p.PPid + "\nUid:\t" + p.Uid + "\t" + p.Uid + "\t" + p.Uid + "\t" + p.Uid + "\nVmRSS:\t" + p.Rss + " kB\n";
            }
        }

        public void WriteProc(Loc l, string text)
        {
            // /proc/sys/...: a runtime sysctl change
            string gp = l.GPath;
            int i = gp.IndexOf("/proc/sys/", StringComparison.Ordinal);
            if (i >= 0)
            {
                string key = gp.Substring(i + 10).Replace('/', '.');
                Sysctl[key] = text.Trim();
                l.Node.Text = text.Trim() + "\n";
                return;
            }
            throw new VfsEx("EACCES");
        }

        public string KernelRelease { get { var k = DefaultKernel(); return k == null ? Kernels[0] : k; } }
        public static readonly string[] Kernels = { "5.14.0-503.14.1.el9_5.x86_64", "5.14.0-427.13.1.el9_4.x86_64" };

        public long MemTotalKb { get { return 3825412; } }
        public long MemUsedKb { get { return 612000 + Procs.Sum(p => p.Rss) / 4 + Containers.Count(c => c.Running) * 48000; } }
        public string MemInfo()
        {
            long total = MemTotalKb, used = MemUsedKb, buff = 2048, cache = 984356;
            long free = total - used - buff - cache;
            long swapTotal = Swaps.Sum(s => s.Bytes / 1024 - 4), swapFree = swapTotal - Swaps.Sum(s => s.UsedKb);
            var sb = new StringBuilder();
            Action<string, long> a = (k, v) => sb.Append((k + ":").PadRight(16) + v.ToString(CultureInfo.InvariantCulture).PadLeft(8) + " kB\n");
            a("MemTotal", total); a("MemFree", free); a("MemAvailable", free + cache - 100000); a("Buffers", buff); a("Cached", cache);
            a("SwapCached", 0); a("Active", 512000); a("Inactive", 600000); a("SwapTotal", swapTotal); a("SwapFree", swapFree);
            a("Dirty", 12); a("Shmem", 18000); a("Slab", 120000); a("SReclaimable", 70000); a("CommitLimit", total / 2 + swapTotal);
            return sb.ToString();
        }
        public string CpuInfo()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 2; i++)
                sb.Append("processor\t: " + i + "\nvendor_id\t: GenuineIntel\ncpu family\t: 6\nmodel\t\t: 85\nmodel name\t: Intel Xeon Processor (Cascadelake)\ncpu MHz\t\t: 2294.608\ncache size\t: 16384 KB\ncpu cores\t: 2\nflags\t\t: fpu vme de pse tsc msr pae mce cx8 apic sep mtrr pge mca cmov pat pse36 clflush mmx fxsr sse sse2 ss syscall nx pdpe1gb rdtscp lm constant_tsc rep_good nopl xtopology cpuid tsc_known_freq pni pclmulqdq vmx ssse3 fma cx16 pcid sse4_1 sse4_2 x2apic movbe popcnt aes xsave avx f16c rdrand hypervisor lahf_lm abm 3dnowprefetch avx2 avx512f\nbogomips\t: 4589.21\n\n");
            return sb.ToString();
        }
        public string LoadAvg()
        {
            double cpu = Procs.Sum(p => p.Cpu) / 100.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00} {2:0.00}", cpu + 0.02, cpu * 0.8 + 0.05, cpu * 0.5 + 0.01);
        }

        public IEnumerable<KeyValuePair<string, string>> SysctlAll()
        {
            var d = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "net.ipv4.ip_forward", "0" }, { "net.ipv4.ip_unprivileged_port_start", "1024" }, { "vm.swappiness", TunedSwappiness() },
                { "kernel.hostname", ShortHost }, { "kernel.pid_max", "4194304" }, { "net.ipv6.conf.all.disable_ipv6", "0" }, { "vm.dirty_ratio", "30" },
                { "kernel.sched_autogroup_enabled", "1" }, { "fs.file-max", "9223372036854775807" },
            };
            foreach (var kv in Sysctl) d[kv.Key] = kv.Value;
            return d;
        }
        string TunedSwappiness()
        {
            switch (TunedProfile) { case "virtual-guest": return "30"; case "throughput-performance": return "10"; case "latency-performance": return "10"; default: return "30"; }
        }

        // ------------------------------------------------------------ small things other parts use
        public bool PackageInstalled(string name) { return InstalledPackages().ContainsKey(name); }

        public IEnumerable<string[]> ContainerHomeRules()
        {
            yield break;
        }

        public static string Size(long bytes, bool si)
        {
            // like lsblk / df -h: 1 decimal under 10, none otherwise; K M G T
            double v = bytes; string[] u = { "B", "K", "M", "G", "T", "P" };
            int i = 0;
            double div = si ? 1000 : 1024;
            while (v >= div && i < u.Length - 1) { v /= div; i++; }
            if (i == 0) return bytes.ToString(CultureInfo.InvariantCulture) + (si ? "B" : "B");
            string s;
            if (v < 10 && Math.Abs(v - Math.Round(v)) > 0.05) s = (Math.Ceiling(v * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
            else s = Math.Ceiling(v - 0.0001).ToString("0", CultureInfo.InvariantCulture);
            if (s == "1024" && i < u.Length - 1) { s = "1.0"; i++; }
            return s + u[i];
        }
    }
}
