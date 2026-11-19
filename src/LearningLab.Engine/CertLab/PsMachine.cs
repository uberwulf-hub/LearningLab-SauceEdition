// Cert Lab - the pretend Windows machines of the PowerShell course: a small network (WIN-SRV1, a Windows Server 2022
// member server; WIN-CL1, a Windows 11 client; DC01, a domain controller that answers DNS; WEB01, a web server), one
// clock for all of them, and per machine its file system (drive C: with Windows, Program Files, Users, the lab
// files), registry (HKLM, HKCU), environment variables and execution policy. Services, processes, event logs,
// scheduled tasks, local users, networking and features live in the other PsMachine files. Everything is data in
// memory: nothing here touches the real PC.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WinFile
    {
        public string Name = "";
        public bool IsDir;
        public WinFile Parent;
        public readonly Dictionary<string, WinFile> Kids = new Dictionary<string, WinFile>(StringComparer.OrdinalIgnoreCase);
        public string Text = "";
        public byte[] Bytes;                       // a binary file (programs): its size only matters
        public string Encoding = "ascii";          // ascii, utf8, utf8bom, unicode, bigendianunicode, utf32
        public int Attr = 32;                      // FileAttributes: 1 ReadOnly, 2 Hidden, 4 System, 16 Directory, 32 Archive
        public DateTime Created, Modified, Accessed;
        public readonly Dictionary<string, string> Streams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // Zone.Identifier
        public string Owner = "BUILTIN\\Administrators";
        public long FakeSize = -1;
        public string Signer;                      // a signed script: who signed it (Authenticode, simulated)
        public string SignedText;                  // the text that was signed (a change breaks the signature)

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name + "\\";
                var parts = new List<string>();
                for (var f = this; f.Parent != null; f = f.Parent) parts.Add(f.Name);
                var root = this; while (root.Parent != null) root = root.Parent;
                parts.Reverse();
                return root.Name + "\\" + string.Join("\\", parts.ToArray());
            }
        }
        public bool Hidden { get { return (Attr & 2) != 0; } }
        public bool ReadOnly { get { return (Attr & 1) != 0; } }
        public long Length
        {
            get
            {
                if (IsDir) return 0;
                if (FakeSize >= 0) return FakeSize;
                if (Bytes != null) return Bytes.Length;
                return ByteCount(Text, Encoding);
            }
        }
        public static long ByteCount(string text, string enc)
        {
            switch ((enc ?? "ascii").ToLowerInvariant())
            {
                case "unicode": case "bigendianunicode": return 2 + 2L * text.Length;
                case "utf32": return 4 + 4L * text.Length;
                case "utf8bom": return 3 + System.Text.Encoding.UTF8.GetByteCount(text);
                case "utf8": return System.Text.Encoding.UTF8.GetByteCount(text);
                default: return text.Length;
            }
        }
        public WinFile Kid(string n) { WinFile f; return Kids.TryGetValue(n, out f) ? f : null; }
        public WinFile Add(WinFile f) { f.Parent = this; Kids[f.Name] = f; return f; }
        public string Mode
        {
            get
            {
                return (IsDir ? "d" : "-") + ((Attr & 32) != 0 ? "a" : "-") + ((Attr & 1) != 0 ? "r" : "-") + ((Attr & 2) != 0 ? "h" : "-") + ((Attr & 4) != 0 ? "s" : "-") + ((Attr & 1024) != 0 ? "l" : "-");
            }
        }
        public WinFile CloneTree()
        {
            var c = new WinFile { Name = Name, IsDir = IsDir, Text = Text, Bytes = Bytes, Encoding = Encoding, Attr = Attr, Created = Created, Modified = Modified, Accessed = Accessed, Owner = Owner, FakeSize = FakeSize, Signer = Signer, SignedText = SignedText };
            foreach (var s in Streams) c.Streams[s.Key] = s.Value;
            foreach (var k in Kids.Values) c.Add(k.CloneTree());
            return c;
        }
    }

    /// <summary>A certificate in a store: Store is "LocalMachine\My", "CurrentUser\My", "LocalMachine\Root"...</summary>
    public class WinCert
    {
        public string Store = "LocalMachine\\My", Subject = "", Issuer = "", Thumbprint = "", FriendlyName = "";
        public DateTime NotBefore, NotAfter;
        public bool HasPrivateKey, CodeSigning;
        public List<string> EnhancedKeyUsage = new List<string>();
    }

    public class RegValue { public string Name = "", Kind = "String"; public object Data; }
    public class RegKey
    {
        public string Name = "";
        public RegKey Parent;
        public readonly Dictionary<string, RegKey> Kids = new Dictionary<string, RegKey>(StringComparer.OrdinalIgnoreCase);
        public readonly List<RegValue> Values = new List<RegValue>();
        public RegKey Kid(string n) { RegKey k; return Kids.TryGetValue(n, out k) ? k : null; }
        public RegKey Sub(string path)
        {
            var k = this;
            foreach (var part in path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var n = k.Kid(part);
                if (n == null) { n = new RegKey { Name = part, Parent = k }; k.Kids[part] = n; }
                k = n;
            }
            return k;
        }
        public RegKey Find(string path)
        {
            var k = this;
            foreach (var part in path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { k = k.Kid(part); if (k == null) return null; }
            return k;
        }
        public RegValue Value(string n) { return Values.FirstOrDefault(v => v.Name.Equals(n, StringComparison.OrdinalIgnoreCase)); }
        public RegKey Set(string name, object data, string kind)
        {
            var v = Value(name);
            if (v == null) { v = new RegValue { Name = name }; Values.Add(v); }
            v.Data = data; v.Kind = kind ?? (data is int ? "DWord" : data is long ? "QWord" : data is string[] ? "MultiString" : "String");
            return this;
        }
        public RegKey S(string name, string v) { return Set(name, v, "String"); }
        public RegKey D(string name, int v) { return Set(name, v, "DWord"); }
        public string HivePath
        {
            get
            {
                var parts = new List<string>();
                for (var k = this; k != null; k = k.Parent) parts.Add(k.Name);
                parts.Reverse();
                return string.Join("\\", parts.ToArray());
            }
        }
        public int SubKeyCount { get { return Kids.Count; } }
        public RegKey CloneTree(RegKey parent)
        {
            var c = new RegKey { Name = Name, Parent = parent };
            foreach (var v in Values) c.Values.Add(new RegValue { Name = v.Name, Kind = v.Kind, Data = v.Data is string[] ? ((string[])v.Data).Clone() : v.Data });
            foreach (var k in Kids.Values) c.Kids[k.Name] = k.CloneTree(c);
            return c;
        }
    }

    /// <summary>A host on the lab network (DC01, WEB01 or one of the console machines).</summary>
    public class LabHost
    {
        public string Name = "", Ip = "", Description = "";
        public List<int> Ports = new List<int>();
        public bool Pingable = true;
        public PsMachine Machine;
        public string Fqdn { get { return Name.ToLowerInvariant() + ".lab.example.com"; } }
    }

    public partial class PsNet
    {
        public readonly List<PsMachine> Machines = new List<PsMachine>();
        public readonly List<LabHost> Hosts = new List<LabHost>();
        public DateTime Now = new DateTime(2026, 3, 2, 9, 0, 0);
        public int Seed;
        public Random Rng;
        public readonly List<KeyValuePair<string, string>> DnsA = new List<KeyValuePair<string, string>>();       // name -> ip
        public readonly List<KeyValuePair<string, string>> DnsCname = new List<KeyValuePair<string, string>>();   // alias -> name

        public static PsNet Create(int machines, int seed)
        {
            var n = new PsNet { Seed = seed, Rng = new Random(seed) };
            PsEnums.Init();
            n.Hosts.Add(new LabHost { Name = "DC01", Ip = "10.0.10.10", Description = "domain controller and DNS server", Ports = new List<int> { 53, 88, 135, 389, 445, 464, 636, 3268, 5985 } });
            n.Hosts.Add(new LabHost { Name = "WEB01", Ip = "10.0.10.50", Description = "web server", Ports = new List<int> { 80, 443, 3389 } });
            var srv = new PsMachine(n, "WIN-SRV1", true, "10.0.10.11");
            n.Machines.Add(srv);
            n.Hosts.Add(new LabHost { Name = "WIN-SRV1", Ip = "10.0.10.11", Machine = srv, Ports = new List<int> { 135, 445, 3389, 5985 } });
            if (machines > 1)
            {
                var cl = new PsMachine(n, "WIN-CL1", false, "10.0.10.21");
                n.Machines.Add(cl);
                n.Hosts.Add(new LabHost { Name = "WIN-CL1", Ip = "10.0.10.21", Machine = cl, Ports = new List<int> { 135, 445 } });
            }
            foreach (var h in n.Hosts) n.DnsA.Add(new KeyValuePair<string, string>(h.Fqdn, h.Ip));
            n.DnsA.Add(new KeyValuePair<string, string>("lab.example.com", "10.0.10.10"));
            n.DnsCname.Add(new KeyValuePair<string, string>("www.lab.example.com", "web01.lab.example.com"));
            n.DnsCname.Add(new KeyValuePair<string, string>("intranet.lab.example.com", "web01.lab.example.com"));
            foreach (var m in n.Machines) m.Boot(true);
            return n;
        }

        public PsMachine Get(string name)
        {
            var h = FindHost(name);
            return h == null ? null : h.Machine;
        }
        /// <summary>A host by name, FQDN, NetBIOS name or IP address.</summary>
        public LabHost FindHost(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.Trim().TrimEnd('.');
            foreach (var h in Hosts)
                if (h.Name.Equals(n, StringComparison.OrdinalIgnoreCase) || h.Fqdn.Equals(n, StringComparison.OrdinalIgnoreCase) || h.Ip == n) return h;
            foreach (var c in DnsCname) if (c.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) return FindHost(c.Value);
            foreach (var m in Machines) foreach (var ip in m.Addresses()) if (ip == n) return Hosts.FirstOrDefault(h => h.Machine == m);
            return null;
        }

        /// <summary>Moves the clock on: jobs finish, scheduled tasks run, event logs fill.</summary>
        public void Advance(long seconds)
        {
            if (seconds <= 0) return;
            var target = Now.AddSeconds(seconds);
            // step in pieces so timed things (scheduled tasks, jobs) happen in order
            while (Now < target)
            {
                var next = target;
                foreach (var m in Machines) { var t = m.NextTimer(Now); if (t.HasValue && t.Value > Now && t.Value < next) next = t.Value; }
                Now = next;
                foreach (var m in Machines) m.Timers(Now);
            }
        }
    }

    public partial class PsMachine
    {
        public PsNet Net;
        public string Name = "", Ip = "";
        public bool IsServer;
        public string Domain = "lab.example.com", DomainShort = "LAB";
        public DateTime Now { get { return Net.Now; } }
        public DateTime BootTime;
        public double UtcOffsetHours = -8;
        public int Cpus = 2;
        public long MemoryBytes = 4294967296L;
        public string OsCaption, OsVersion, OsBuild, PsVersionText, OsEdition, OsDisplayVersion;
        public DateTime InstallDate;
        public WinFile C;
        public RegKey HKLM, HKCU;
        public readonly Dictionary<string, string> EnvMachine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> EnvUser = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string ExecPolicyMachine, ExecPolicyUser = "Undefined";
        public string ExecPolicyGpo;               // MachinePolicy (Group Policy): normally not set
        public bool WinRmListening;                // WinRM accepts remote PowerShell
        public string State = "running";
        public readonly List<PsRunspace> Sessions = new List<PsRunspace>();
        public Random Rng;
        int guidCount;
        public int NextPid = 4000;
        public readonly List<string> Transcript = new List<string>();

        public PsMachine(PsNet net, string name, bool server, string ip)
        {
            Net = net; Name = name; IsServer = server; Ip = ip;
            Rng = new Random(net.Seed * 31 + name.Length * 7 + (server ? 1 : 2));
            if (server)
            {
                OsCaption = "Microsoft Windows Server 2022 Datacenter"; OsVersion = "10.0.20348"; OsBuild = "20348"; PsVersionText = "5.1.20348.2849"; OsEdition = "ServerDatacenter"; OsDisplayVersion = "21H2";
                ExecPolicyMachine = "RemoteSigned"; WinRmListening = true; Cpus = 4; MemoryBytes = 8589934592L;
                InstallDate = new DateTime(2026, 1, 12, 10, 14, 37);
            }
            else
            {
                OsCaption = "Microsoft Windows 11 Enterprise"; OsVersion = "10.0.22631"; OsBuild = "22631"; PsVersionText = "5.1.22621.4249"; OsEdition = "Enterprise"; OsDisplayVersion = "23H2";
                ExecPolicyMachine = "Undefined"; WinRmListening = false;   // Windows 11: every scope Undefined, Restricted in effect Cpus = 2; MemoryBytes = 4294967296L;
                InstallDate = new DateTime(2026, 1, 19, 14, 2, 11);
            }
            SeedFiles();
            SeedRegistry();
            SeedEnv();
            SeedAdmin();
            SeedNet();
            SeedMore();
        }
        partial void SeedAdmin();
        partial void SeedNet();
        partial void SeedMore();
        partial void OnBoot(bool first);
        partial void OnShutdown();
        partial void OnTimers(DateTime now);
        partial void OnNextTimer(DateTime now, ref DateTime? next);

        public string Fqdn { get { return Name.ToLowerInvariant() + "." + Domain; } }
        public string ShortDomainUser(string user) { return DomainShort + "\\" + user; }
        public IEnumerable<string> Addresses() { yield return Ip; yield return "127.0.0.1"; foreach (var a in ExtraIps) yield return a; }
        public readonly List<string> ExtraIps = new List<string>();

        public void Boot(bool first)
        {
            State = "running";
            BootTime = first ? Now.AddMinutes(-(Name.Length * 3 + 40)) : Now;
            OnBoot(first);
        }

        /// <summary>Restart-Computer, or the graders' restart: sessions end, services start again, logs record it.</summary>
        public void Reboot()
        {
            foreach (var s in Sessions.ToList()) s.Closed = true;
            Sessions.Clear();
            OnShutdown();
            Boot(false);
            Net.Now = Net.Now.AddSeconds(45);
            BootTime = Net.Now.AddSeconds(-30);
            foreach (var cb in RebootListeners.ToList()) cb();
        }
        public readonly List<Action> RebootListeners = new List<Action>();

        public DateTime? NextTimer(DateTime now)
        {
            DateTime? n = null; OnNextTimer(now, ref n);
            foreach (var h in NextTimerHooks) { var t = h(now); if (t.HasValue && (!n.HasValue || t.Value < n.Value)) n = t; }
            return n;
        }
        public void Timers(DateTime now) { OnTimers(now); foreach (var h in TimerHooks.ToList()) h(now); }
        /// <summary>More things that happen on the clock (background jobs): when is the next one, and run what is due.</summary>
        public readonly List<Func<DateTime, DateTime?>> NextTimerHooks = new List<Func<DateTime, DateTime?>>();
        public readonly List<Action<DateTime>> TimerHooks = new List<Action<DateTime>>();

        /// <summary>Certificates in the stores (Cert:\LocalMachine\My, Cert:\CurrentUser\My, Root...).</summary>
        public readonly List<WinCert> Certs = new List<WinCert>();

        public object NewGuid()
        {
            guidCount++;
            var b = new byte[16];
            Rng.NextBytes(b);
            b[7] = (byte)((b[7] & 0x0F) | 0x40); b[8] = (byte)((b[8] & 0x3F) | 0x80);
            return new Guid(b);
        }
        public string RandomName(int n) { const string a = "abcdefghijklmnopqrstuvwxyz0123456789"; var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(a[Rng.Next(a.Length)]); return sb.ToString(); }
        public string RandomHex(int n) { var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append("0123456789abcdef"[Rng.Next(16)]); return sb.ToString(); }

        public PsObj OsVersionObj()
        {
            var o = new PsObj("System.OperatingSystem", "System.Object");
            o.Val("Platform", "System.PlatformID", "Win32NT");
            o.Val("ServicePack", "string", "");
            o.Val("Version", "version", new Version(OsVersion + ".0"));
            o.Val("VersionString", "string", "Microsoft Windows NT " + OsVersion + ".0");
            o.ToStr = x => "Microsoft Windows NT " + OsVersion + ".0";
            return o;
        }

        // ------------------------------------------------------------ the file system
        static readonly DateTime OsDate = new DateTime(2021, 5, 8, 1, 20, 0);
        public WinFile Dir(string path) { return MakeDir(path, OsDate); }
        public WinFile MakeDir(string path, DateTime t)
        {
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var cur = C;
            WinFile extra;
            if (parts.Length > 0 && !parts[0].Equals("C:", StringComparison.OrdinalIgnoreCase) && ExtraDrives.TryGetValue(parts[0].TrimEnd(':'), out extra)) cur = extra;
            for (int i = 1; i < parts.Length; i++)
            {
                var k = cur.Kid(parts[i]);
                if (k == null) { k = cur.Add(new WinFile { Name = parts[i], IsDir = true, Attr = 16, Created = t, Modified = t, Accessed = t }); }
                cur = k;
            }
            return cur;
        }
        public WinFile File(string path, string text, DateTime t)
        {
            int i = path.LastIndexOf('\\');
            var d = MakeDir(path.Substring(0, i), t);
            var f = d.Add(new WinFile { Name = path.Substring(i + 1), Text = text, Created = t, Modified = t, Accessed = t });
            return f;
        }
        WinFile Bin(string path, long size, DateTime t) { var f = File(path, "", t); f.FakeSize = size; f.Bytes = new byte[0]; return f; }

        /// <summary>Cyber Range only: who is acting on this thread (a standard user's remote shell; null = the
        /// administrator or the system) and the host's rule for (user, path, write). Null on the courses' machines.</summary>
        [ThreadStatic] public static string Acting;
        [ThreadStatic] public static PsMachine ActingOn;
        /// <summary>Cyber Range: the pretend company switched the password complexity policy off.</summary>
        public bool NoComplexity;
        public Func<string, string, bool, bool> Access;
        /// <summary>Cyber Range only: called when a service someone installed (New-Service, sc.exe create) starts, so the
        /// range can run the script it names. Null on the courses' machines (their services start without running anything).</summary>
        public Action<WinService> ServiceStarted;
        /// <summary>Cyber Range: may the account acting on this thread read this file (files reached without FindFile)?</summary>
        public static bool ReadDenied(string fullPath) { var m = ActingOn; return m != null && m.Access != null && Acting != null && !m.Access(Acting, fullPath, false); }

        public WinFile FindFile(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            if (Access != null && Acting != null && !Access(Acting, fullPath, false)) throw new ProvError("Access to the path '" + fullPath + "' is denied.", "UnauthorizedAccessError", "PermissionDenied", fullPath, "System.UnauthorizedAccessException");
            var parts = fullPath.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var cur = C;
            if (!parts[0].Equals("C:", StringComparison.OrdinalIgnoreCase)) { if (!ExtraDrives.TryGetValue(parts[0].TrimEnd(':').ToUpperInvariant(), out cur)) return null; }   // D:, E: on the AZ-800 servers
            for (int i = 1; i < parts.Length; i++) { cur = cur.Kid(parts[i]); if (cur == null) return null; }
            return cur;
        }

        void SeedFiles()
        {
            C = new WinFile { Name = "C:", IsDir = true, Attr = 22, Created = OsDate, Modified = InstallDate, Accessed = InstallDate };
            var inst = InstallDate;
            Dir("C:\\PerfLogs");
            var pf = Dir("C:\\Program Files");
            pf.Attr = 17;
            Dir("C:\\Program Files\\Common Files");
            Dir("C:\\Program Files\\Internet Explorer");
            Dir("C:\\Program Files\\Windows Defender");
            Dir("C:\\Program Files\\WindowsPowerShell\\Configuration");
            Dir("C:\\Program Files\\WindowsPowerShell\\Scripts");
            Dir("C:\\Program Files\\WindowsPowerShell\\Modules\\PackageManagement\\1.0.0.1");
            Dir("C:\\Program Files\\WindowsPowerShell\\Modules\\PowerShellGet\\1.0.0.1");
            Dir("C:\\Program Files\\WindowsPowerShell\\Modules\\Pester\\3.4.0");
            Dir("C:\\Program Files\\WindowsPowerShell\\Modules\\PSReadline\\2.0.0");
            Dir("C:\\Program Files (x86)").Attr = 17;
            Dir("C:\\Program Files (x86)\\Common Files");
            Dir("C:\\Program Files (x86)\\Internet Explorer");
            var pd = Dir("C:\\ProgramData"); pd.Attr = 18;
            Dir("C:\\ProgramData\\Microsoft");
            Dir("C:\\ProgramData\\Package Cache");
            var users = Dir("C:\\Users"); users.Attr = 17;
            var pub = MakeDir("C:\\Users\\Public", inst); pub.Attr = 17;
            MakeDir("C:\\Users\\Public\\Documents", inst);
            MakeDir("C:\\Users\\Public\\Desktop", inst).Attr = 19;
            var def = MakeDir("C:\\Users\\Default", OsDate); def.Attr = 18;
            string home = "C:\\Users\\Administrator";
            MakeDir(home, inst);
            foreach (var d in new[] { "Desktop", "Documents", "Downloads", "Favorites", "Links", "Music", "Pictures", "Saved Games", "Videos", "Contacts", "Searches" }) MakeDir(home + "\\" + d, inst).Attr = 17;
            MakeDir(home + "\\3D Objects", inst).Attr = 17;
            var ad = MakeDir(home + "\\AppData", inst); ad.Attr = 18;
            MakeDir(home + "\\AppData\\Local\\Temp", inst);
            MakeDir(home + "\\AppData\\Local\\Microsoft\\Windows\\PowerShell", inst);
            MakeDir(home + "\\AppData\\Roaming\\Microsoft\\Windows\\PowerShell\\PSReadLine", inst);
            File(home + "\\AppData\\Roaming\\Microsoft\\Windows\\PowerShell\\PSReadLine\\ConsoleHost_history.txt", "", inst).Encoding = "utf8";
            File(home + "\\NTUSER.DAT", "", inst).Attr = 34;
            // Windows
            var win = Dir("C:\\Windows");
            Dir("C:\\Windows\\Logs\\CBS");
            Dir("C:\\Windows\\Temp");
            Dir("C:\\Windows\\SysWOW64");
            Dir("C:\\Windows\\WinSxS");
            Dir("C:\\Windows\\Web\\Wallpaper\\Windows");
            Dir("C:\\Windows\\Fonts").Attr = 17;
            File("C:\\Windows\\win.ini", "; for 16-bit app support\r\n[fonts]\r\n[extensions]\r\n[mci extensions]\r\n[files]\r\n[Mail]\r\nMAPI=1\r\n", OsDate);
            File("C:\\Windows\\system.ini", "; for 16-bit app support\r\n[386Enh]\r\nwoafont=dosapp.fon\r\nEGA80WOA.FON=EGA80WOA.FON\r\nEGA40WOA.FON=EGA40WOA.FON\r\nCGA80WOA.FON=CGA80WOA.FON\r\nCGA40WOA.FON=CGA40WOA.FON\r\n\r\n[drivers]\r\nwave=mmdrv.dll\r\ntimer=timer.drv\r\n\r\n[mci]\r\n", OsDate);
            Bin("C:\\Windows\\explorer.exe", 4919904, OsDate);
            Bin("C:\\Windows\\notepad.exe", 201216, OsDate);
            Bin("C:\\Windows\\regedit.exe", 358400, OsDate);
            File("C:\\Windows\\WindowsUpdate.log", "Windows Update logs are now generated using ETW (Event Tracing for Windows).\r\nPlease run the Get-WindowsUpdateLog PowerShell command to convert ETW traces into a readable WindowsUpdate.log.\r\n", inst);
            var s32 = Dir("C:\\Windows\\System32");
            foreach (var p in new[] { "cmd.exe|289792", "notepad.exe|201216", "hostname.exe|16384", "whoami.exe|73728", "ipconfig.exe|36352", "PING.EXE|22016", "nslookup.exe|99840", "sc.exe|70656", "net.exe|57344", "netstat.exe|39936",
                "tasklist.exe|86016", "schtasks.exe|229376", "winrm.cmd|35", "gpupdate.exe|29184", "systeminfo.exe|94720", "shutdown.exe|33792", "robocopy.exe|141312", "certutil.exe|1565184", "mmc.exe|2002432", "taskmgr.exe|1269760",
                "services.msc|92745", "eventvwr.exe|98304", "msinfo32.exe|130560", "tracert.exe|21504", "route.exe|25088", "arp.exe|33792", "where.exe|72192", "xcopy.exe|49152", "findstr.exe|36352", "icacls.exe|36352" })
            {
                var q = p.Split('|');
                Bin("C:\\Windows\\System32\\" + q[0], long.Parse(q[1], CultureInfo.InvariantCulture), OsDate);
            }
            File("C:\\Windows\\System32\\drivers\\etc\\hosts", "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\r\n#\r\n# This file contains the mappings of IP addresses to host names. Each\r\n# entry should be kept on an individual line. The IP address should\r\n# be placed in the first column followed by the corresponding host name.\r\n# The IP address and the host name should be separated by at least one\r\n# space.\r\n#\r\n# localhost name resolution is handled within DNS itself.\r\n#\t127.0.0.1       localhost\r\n#\t::1             localhost\r\n", OsDate);
            File("C:\\Windows\\System32\\drivers\\etc\\services", "# Cert Lab: a short copy of the services file\r\nftp-data           20/tcp\r\nftp                21/tcp\r\nssh                22/tcp\r\ntelnet             23/tcp\r\nsmtp               25/tcp    mail\r\ndomain             53/tcp\r\ndomain             53/udp\r\nhttp               80/tcp    www www-http\r\nkerberos           88/tcp    krb5 kerberos-sec\r\npop3              110/tcp\r\nntp               123/udp\r\nepmap             135/tcp    loc-srv\r\nnetbios-ssn       139/tcp\r\nldap              389/tcp\r\nhttps             443/tcp    MCom\r\nmicrosoft-ds      445/tcp\r\nms-wbt-server    3389/tcp\r\nwsman            5985/tcp\r\nwsmans           5986/tcp\r\n", OsDate);
            var wpsh = Dir("C:\\Windows\\System32\\WindowsPowerShell\\v1.0");
            Bin("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", 452608, OsDate);
            Bin("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell_ise.exe", 225280, OsDate);
            Dir("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\en-US");
            foreach (var m in new[] { "CimCmdlets", "Microsoft.PowerShell.Archive", "Microsoft.PowerShell.Diagnostics", "Microsoft.PowerShell.Host", "Microsoft.PowerShell.LocalAccounts", "Microsoft.PowerShell.Management",
                "Microsoft.PowerShell.Security", "Microsoft.PowerShell.Utility", "Microsoft.WSMan.Management", "NetAdapter", "NetSecurity", "NetTCPIP", "DnsClient", "PSDesiredStateConfiguration", "PSDiagnostics",
                "ScheduledTasks", "Storage", "SmbShare", "PowerShellGet", "PackageManagement" })
                Dir("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\Modules\\" + m);
            if (IsServer) Dir("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\Modules\\ServerManager");
            Dir("C:\\Windows\\System32\\config").Attr = 16;
            Dir("C:\\Windows\\System32\\LogFiles\\Firewall");
            Dir("C:\\Windows\\System32\\Tasks\\Microsoft\\Windows");
            Dir("C:\\Windows\\System32\\winevt\\Logs");
            Dir("C:\\Windows\\System32\\OpenSSH");
            // the lab's own files: data the course uses
            SeedLabFiles(inst.AddDays(1));
        }

        void SeedLabFiles(DateTime t)
        {
            string d = "C:\\LabFiles";
            MakeDir(d, t);
            File(d + "\\employees.csv",
                "Name,Department,Title,Office,Salary,Started\r\n" +
                "Ada Lovelace,Engineering,Software Developer,London,98000,2019-04-01\r\n" +
                "Grace Hopper,Engineering,Engineering Manager,Arlington,124000,2015-09-14\r\n" +
                "Alan Turing,Research,Research Scientist,Manchester,105000,2018-01-08\r\n" +
                "Katherine Johnson,Research,Data Analyst,Hampton,87000,2020-06-22\r\n" +
                "Linus Ewing,IT,Systems Administrator,Seattle,79000,2021-03-15\r\n" +
                "Margaret Hamilton,Engineering,Software Developer,Boston,101000,2017-11-02\r\n" +
                "Tim Berners,IT,Network Engineer,Geneva,83000,2022-02-28\r\n" +
                "Radia Perlman,IT,Network Engineer,Seattle,96000,2016-07-11\r\n" +
                "Dennis Ritchie,Engineering,Software Developer,Murray Hill,99000,2014-05-19\r\n" +
                "Hedy Lamarr,Research,Research Scientist,Vienna,92000,2023-08-07\r\n", t);
            File(d + "\\servers.txt", "WIN-SRV1\r\nWIN-CL1\r\nDC01\r\nWEB01\r\n", t);
            File(d + "\\app.log",
                "2026-03-01 08:00:01 INFO  Service started (version 2.4.1)\r\n" +
                "2026-03-01 08:00:05 INFO  Connected to database SQL01\r\n" +
                "2026-03-01 08:14:22 WARN  Slow response from SQL01 (2350 ms)\r\n" +
                "2026-03-01 08:30:00 INFO  Nightly report written to C:\\Reports\\daily.csv\r\n" +
                "2026-03-01 09:02:41 ERROR Login failed for user 'svc_backup'\r\n" +
                "2026-03-01 09:02:44 ERROR Login failed for user 'svc_backup'\r\n" +
                "2026-03-01 09:15:10 INFO  Cache cleared\r\n" +
                "2026-03-01 10:47:03 WARN  Disk C: is 85 percent full\r\n" +
                "2026-03-01 11:20:58 ERROR Timeout talking to WEB01 after 30 seconds\r\n" +
                "2026-03-01 12:00:00 INFO  Service health check passed\r\n", t);
            File(d + "\\settings.json",
                "{\r\n  \"AppName\": \"Inventory\",\r\n  \"Version\": \"2.4.1\",\r\n  \"Database\": {\r\n    \"Server\": \"SQL01\",\r\n    \"Port\": 1433\r\n  },\r\n  \"Features\": [\"Reports\", \"Export\", \"Audit\"],\r\n  \"MaxUsers\": 250,\r\n  \"Debug\": false\r\n}\r\n", t).Encoding = "utf8";
            File(d + "\\shares.csv", "ShareName;Path;Owner\r\nPublic;C:\\Shares\\Public;IT\r\nFinance;C:\\Shares\\Finance;Finance\r\nProjects;C:\\Shares\\Projects;Engineering\r\n", t);
            File(d + "\\notes.txt", "Remember to check the backup job on WIN-SRV1.\r\nThe printer on floor 2 needs toner.\r\nReview the firewall rules on Friday.\r\n", t);
            MakeDir(d + "\\Reports", t);
            File(d + "\\Reports\\2026-01.csv", "Month,Tickets,Closed\r\nJanuary,412,398\r\n", t.AddDays(-30));
            File(d + "\\Reports\\2026-02.csv", "Month,Tickets,Closed\r\nFebruary,387,371\r\n", t.AddDays(-2));
            File(d + "\\Reports\\summary [old].txt", "Summary of last year (kept for the lesson on -LiteralPath).\r\n", t.AddDays(-60));
            MakeDir(d + "\\Archive", t);
            File(d + "\\Archive\\old-app.log", "2025-12-31 23:59:59 INFO  Year end\r\n", new DateTime(2026, 1, 1, 0, 0, 0));
            File(d + "\\Archive\\readme.txt", "Old files, safe to delete after 90 days.\r\n", new DateTime(2025, 11, 3, 14, 0, 0));
            var dl = File("C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1", "# Downloaded from the intranet\r\nGet-CimInstance -ClassName Win32_OperatingSystem | Select-Object CSName, Caption, Version\r\n", t);
            dl.Streams["Zone.Identifier"] = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=http://intranet.lab.example.com/tools/\r\nHostUrl=http://intranet.lab.example.com/tools/Get-Inventory.ps1\r\n";
        }

        void SeedRegistry()
        {
            HKLM = new RegKey { Name = "HKEY_LOCAL_MACHINE" };
            HKCU = new RegKey { Name = "HKEY_CURRENT_USER" };
            var cv = HKLM.Sub("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
            cv.S("SystemRoot", "C:\\Windows").S("BuildBranch", IsServer ? "fe_release" : "ni_release").S("CurrentBuild", OsBuild).S("CurrentBuildNumber", OsBuild).S("CurrentMajorVersionNumber", "").D("CurrentMajorVersionNumber", 10).D("CurrentMinorVersionNumber", 0)
              .S("CurrentType", "Multiprocessor Free").S("CurrentVersion", "6.3").S("DisplayVersion", OsDisplayVersion).S("EditionID", OsEdition).S("InstallationType", IsServer ? "Server" : "Client")
              .Set("InstallDate", (int)(InstallDate - new DateTime(1970, 1, 1)).TotalSeconds, "DWord").S("ProductName", IsServer ? "Windows Server 2022 Datacenter" : "Windows 10 Enterprise").S("RegisteredOrganization", "Lab Example").S("RegisteredOwner", "Lab Administrator").S("ReleaseId", IsServer ? "2009" : "2009").D("UBR", IsServer ? 2849 : 4249);
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows\\CurrentVersion").S("CommonFilesDir", "C:\\Program Files\\Common Files").S("CommonFilesDir (x86)", "C:\\Program Files (x86)\\Common Files").S("ProgramFilesDir", "C:\\Program Files").S("ProgramFilesDir (x86)", "C:\\Program Files (x86)").S("ProgramFilesPath", "%ProgramFiles%").S("ProgramW6432Dir", "C:\\Program Files");
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run").Set("SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe", "ExpandString");
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System").D("EnableLUA", 1).D("ConsentPromptBehaviorAdmin", 5).D("FilterAdministratorToken", 0);
            HKLM.Sub("SOFTWARE\\Microsoft\\PowerShell\\1\\PowerShellEngine").S("ApplicationBase", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0").S("PowerShellVersion", "5.1." + OsBuild + ".1").S("RuntimeVersion", "v4.0.30319");
            HKLM.Sub("SOFTWARE\\Microsoft\\PowerShell\\1\\ShellIds\\Microsoft.PowerShell").S("Path", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
            HKLM.Sub("SOFTWARE\\Microsoft\\PowerShell\\3\\PowerShellEngine").S("PowerShellVersion", PsVersionText).S("PSCompatibleVersion", "1.0, 2.0, 3.0, 4.0, 5.0, 5.1");
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows Defender");
            HKLM.Sub("SOFTWARE\\Policies\\Microsoft\\Windows");
            HKLM.Sub("SOFTWARE\\Classes");
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName").S("ComputerName", Name);
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment").Set("Path", "%SystemRoot%\\system32;%SystemRoot%;%SystemRoot%\\System32\\Wbem;%SYSTEMROOT%\\System32\\WindowsPowerShell\\v1.0\\;%SYSTEMROOT%\\System32\\OpenSSH\\", "ExpandString").S("OS", "Windows_NT").S("PROCESSOR_ARCHITECTURE", "AMD64").Set("PSModulePath", "%ProgramFiles%\\WindowsPowerShell\\Modules;%SystemRoot%\\system32\\WindowsPowerShell\\v1.0\\Modules", "ExpandString").Set("TEMP", "%SystemRoot%\\TEMP", "ExpandString").Set("TMP", "%SystemRoot%\\TEMP", "ExpandString").S("windir", "%SystemRoot%");
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\TimeZoneInformation").S("TimeZoneKeyName", "Pacific Standard Time");
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Services");
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\Terminal Server").D("fDenyTSConnections", 1);
            HKLM.Sub("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0").S("ProcessorNameString", "Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz").D("~MHz", 1995);
            HKLM.Sub("SAM"); HKLM.Sub("SECURITY");
            HKCU.Sub("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
            HKCU.Sub("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced").D("Hidden", 2).D("HideFileExt", 1).D("ShowSuperHidden", 0);
            HKCU.Sub("Environment").Set("TEMP", "%USERPROFILE%\\AppData\\Local\\Temp", "ExpandString").Set("TMP", "%USERPROFILE%\\AppData\\Local\\Temp", "ExpandString").Set("Path", "%USERPROFILE%\\AppData\\Local\\Microsoft\\WindowsApps;", "ExpandString");
            HKCU.Sub("Control Panel\\Desktop").S("Wallpaper", "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg").S("ScreenSaveActive", "1").S("ScreenSaveTimeOut", "900").S("WallpaperStyle", "10");
            HKCU.Sub("Control Panel\\International").S("LocaleName", "en-US").S("sShortDate", "M/d/yyyy").S("sTimeFormat", "h:mm:ss tt");
            HKCU.Sub("Console").D("QuickEdit", 1).D("ScreenBufferSize", 589889656);
            HKCU.Sub("Software\\Microsoft\\PowerShell");
            HKCU.Sub("Software\\Classes");
        }

        void SeedEnv()
        {
            EnvMachine["ComSpec"] = "C:\\Windows\\system32\\cmd.exe";
            EnvMachine["DriverData"] = "C:\\Windows\\System32\\Drivers\\DriverData";
            EnvMachine["NUMBER_OF_PROCESSORS"] = Cpus.ToString(CultureInfo.InvariantCulture);
            EnvMachine["OS"] = "Windows_NT";
            EnvMachine["Path"] = "C:\\Windows\\system32;C:\\Windows;C:\\Windows\\System32\\Wbem;C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\;C:\\Windows\\System32\\OpenSSH\\";
            EnvMachine["PATHEXT"] = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";
            EnvMachine["PROCESSOR_ARCHITECTURE"] = "AMD64";
            EnvMachine["PROCESSOR_IDENTIFIER"] = "Intel64 Family 6 Model 106 Stepping 6, GenuineIntel";
            EnvMachine["PROCESSOR_LEVEL"] = "6";
            EnvMachine["PROCESSOR_REVISION"] = "6a06";
            EnvMachine["PSModulePath"] = "C:\\Program Files\\WindowsPowerShell\\Modules;C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\Modules";
            EnvMachine["TEMP"] = "C:\\Windows\\TEMP";
            EnvMachine["TMP"] = "C:\\Windows\\TEMP";
            EnvMachine["USERNAME"] = "SYSTEM";
            EnvMachine["windir"] = "C:\\Windows";
            EnvUser["Path"] = "C:\\Users\\Administrator\\AppData\\Local\\Microsoft\\WindowsApps;";
            EnvUser["TEMP"] = "C:\\Users\\Administrator\\AppData\\Local\\Temp";
            EnvUser["TMP"] = "C:\\Users\\Administrator\\AppData\\Local\\Temp";
        }

        /// <summary>The environment a new PowerShell process gets: machine + user + per-process values.</summary>
        public Dictionary<string, string> NewProcessEnv(string user)
        {
            var e = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in EnvMachine) e[kv.Key] = kv.Value;
            foreach (var kv in EnvUser) { if (kv.Key.Equals("Path", StringComparison.OrdinalIgnoreCase)) e["Path"] = e["Path"].TrimEnd(';') + ";" + kv.Value; else e[kv.Key] = kv.Value; }
            string home = "C:\\Users\\" + user;
            e["ALLUSERSPROFILE"] = "C:\\ProgramData";
            e["APPDATA"] = home + "\\AppData\\Roaming";
            e["CommonProgramFiles"] = "C:\\Program Files\\Common Files";
            e["CommonProgramFiles(x86)"] = "C:\\Program Files (x86)\\Common Files";
            e["CommonProgramW6432"] = "C:\\Program Files\\Common Files";
            e["COMPUTERNAME"] = Name;
            e["HOMEDRIVE"] = "C:";
            e["HOMEPATH"] = "\\Users\\" + user;
            e["LOCALAPPDATA"] = home + "\\AppData\\Local";
            e["LOGONSERVER"] = "\\\\DC01";
            e["ProgramData"] = "C:\\ProgramData";
            e["ProgramFiles"] = "C:\\Program Files";
            e["ProgramFiles(x86)"] = "C:\\Program Files (x86)";
            e["ProgramW6432"] = "C:\\Program Files";
            e["PSModulePath"] = home + "\\Documents\\WindowsPowerShell\\Modules;" + EnvMachine["PSModulePath"];
            e["PUBLIC"] = "C:\\Users\\Public";
            e["SystemDrive"] = "C:";
            e["SystemRoot"] = "C:\\Windows";
            e["USERDNSDOMAIN"] = Domain.ToUpperInvariant();
            e["USERDOMAIN"] = DomainShort;
            e["USERDOMAIN_ROAMINGPROFILE"] = DomainShort;
            e["USERNAME"] = user;
            e["USERPROFILE"] = home;
            if (!IsServer) e["OneDrive"] = home + "\\OneDrive";
            return e;
        }

        /// <summary>The policy in effect: MachinePolicy, UserPolicy, Process, CurrentUser, LocalMachine (first that is set).</summary>
        public string EffectivePolicy(string processPolicy)
        {
            if (!string.IsNullOrEmpty(ExecPolicyGpo) && ExecPolicyGpo != "Undefined") return ExecPolicyGpo;
            if (!string.IsNullOrEmpty(processPolicy) && processPolicy != "Undefined") return processPolicy;
            if (!string.IsNullOrEmpty(ExecPolicyUser) && ExecPolicyUser != "Undefined") return ExecPolicyUser;
            if (!string.IsNullOrEmpty(ExecPolicyMachine) && ExecPolicyMachine != "Undefined") return ExecPolicyMachine;
            return IsServer ? "RemoteSigned" : "Restricted";
        }
    }
}
