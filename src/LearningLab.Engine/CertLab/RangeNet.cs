// Cyber Range - the scenario network: the RHEL-like machines of Cert Lab (LabNet, Machine) plus pretend Windows hosts
// (RangeHost, each backed by a PsMachine from the PowerShell course), routes between subnets, the scenario's pretend
// web apps, what the player has discovered (the map's fog of war), credentials that worked (loot) and the noise log.
// Everything is simulated: nothing here opens a socket, runs a real command or reaches this PC.
//
// The noise log is the hook for later modes: every action a defender could notice (scans, logins, failed logins,
// web requests to an app's tricky pages, password guessing) becomes a NoiseEvent with source, target, kind and
// loudness, kept on the scenario clock and handed to the IDefender. Version 1 only has NoDefender.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CertLab
{
    public class RangeAccount
    {
        public string User = "", Password = "";
        public bool Admin, Remote = true;
        public string Spn = "";         // a service name (only for the domain's service accounts)
        public string SimHash { get { return RangeHash.Nt(Password); } }
    }

    public class RangeShare
    {
        public string Name = "", Path = "", Comment = "";
        public bool Anonymous;
        public List<string> Users = new List<string>();   // empty: any account of the host (or the domain)
    }

    public class RangeRoute
    {
        public string From = "", To = "";                 // networks (10.10.1.0/24) or single addresses
        public List<int> Ports = new List<int>();          // empty: every port
    }

    /// <summary>A pretend Windows host: its addresses and ports on the range network, its accounts and shares, and the
    /// PsMachine whose PowerShell the remote shell runs.</summary>
    public class RangeHost
    {
        public string Name = "", Hostname = "", Os = "windows", Role = "";
        public List<string> Nics = new List<string>();
        public List<int> Ports = new List<int>();
        public Dictionary<int, string> Names = new Dictionary<int, string>();
        public Dictionary<int, string> Versions = new Dictionary<int, string>();
        public PsMachine Ps;
        public RangeNet Net;
        public List<RangeAccount> Accounts = new List<RangeAccount>();
        public List<RangeShare> Shares = new List<RangeShare>();
        public List<string> Private = new List<string>();    // folders only administrators may open
        public List<string> Writable = new List<string>();   // folders any account may write to (besides its own profile and the temp folders)
        public bool DomainJoined;
        public List<string> DomainAdmins = new List<string>();   // domain accounts that are administrators here

        public string Ip { get { return Nics.Count == 0 ? "" : Nics[0].Split('/')[0]; } }
        public bool Listens(int port) { return Ports.Contains(port); }
        public string Version(int port)
        {
            string v; if (Versions.TryGetValue(port, out v)) return v;
            switch (port)
            {
                case 135: return "Microsoft Windows RPC";
                case 139: return "Microsoft Windows netbios-ssn";
                case 445: return "Microsoft Windows Server 2022 microsoft-ds";
                case 5985: return "Microsoft HTTPAPI httpd 2.0 (SSDP/UPnP)";
                case 88: return "Microsoft Windows Kerberos";
                case 389: return "Microsoft Windows Active Directory LDAP (Domain: " + (Net == null ? "" : Net.DomainName) + ")";
                case 80: return "Microsoft IIS httpd 10.0";
                case 3389: return "Microsoft Terminal Services";
            }
            return "";
        }

        /// <summary>An account that can sign in here: a local one, or a domain one when the host is in the domain.</summary>
        public RangeAccount Account(string user, out bool admin)
        {
            admin = false;
            if (string.IsNullOrEmpty(user)) return null;
            string u = user; bool dom = false;
            int bs = u.IndexOf('\\'); if (bs >= 0) { dom = !u.Substring(0, bs).Equals(Name, StringComparison.OrdinalIgnoreCase) && u.Substring(0, bs) != "."; u = u.Substring(bs + 1); }
            int at = u.IndexOf('@'); if (at >= 0) { dom = true; u = u.Substring(0, at); }
            if (!dom)
            {
                var a = Accounts.FirstOrDefault(x => x.User.Equals(u, StringComparison.OrdinalIgnoreCase));
                if (a != null) { admin = a.Admin; return a; }
            }
            if (DomainJoined && Net != null)
            {
                var d = Net.DomainAccounts.FirstOrDefault(x => x.User.Equals(u, StringComparison.OrdinalIgnoreCase));
                if (d != null) { admin = d.Admin || DomainAdmins.Any(x => x.Equals(d.User, StringComparison.OrdinalIgnoreCase)); return d; }
            }
            return null;
        }

        /// <summary>The host's file rule for a standard account (administrators may do anything).</summary>
        public bool Allowed(string user, string path, bool write)
        {
            bool admin; var a = Account(user, out admin);
            if (a == null || admin) return admin;
            string p = (path ?? "").Replace('/', '\\').TrimEnd('\\');
            Func<string, bool> under = pre => { var q = pre.Replace('/', '\\').TrimEnd('\\'); return p.Equals(q, StringComparison.OrdinalIgnoreCase) || p.StartsWith(q + "\\", StringComparison.OrdinalIgnoreCase); };
            string me = a.User;
            if (p.StartsWith("C:\\Users\\", StringComparison.OrdinalIgnoreCase))
            {
                string who = p.Substring(9).Split('\\')[0];
                if (!who.Equals(me, StringComparison.OrdinalIgnoreCase) && !who.Equals("Public", StringComparison.OrdinalIgnoreCase) && !who.Equals("Default", StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (Private.Any(under)) return false;
            if (under("C:\\Windows\\System32\\config")) return false;
            if (!write) return true;
            if (under("C:\\Users\\" + me) || under("C:\\Users\\Public") || under("C:\\Temp") || under("C:\\Windows\\Temp")) return true;
            return Writable.Any(under);
        }
    }

    public class SeenHost
    {
        public string Ip = "", Name = "", Os = "";
        public List<int> Ports = new List<int>();
        public string Owned = "";      // "", "user", "admin"
        public List<string> Users = new List<string>();
    }

    public class NoiseEvent
    {
        public long Time; public string Source = "", Target = "", Kind = "", Detail = ""; public int Loud;
        public override string ToString() { return Time + " " + Source + " -> " + Target + " " + Kind + " (" + Loud + ") " + Detail; }
    }

    public class LootItem
    {
        public string Host = "", User = "", Secret = "", Where = "";
        public bool Auto;
    }

    /// <summary>A simulated defender that watches the noise log and reacts (blocks noisy addresses, resets passwords,
    /// ends sessions). Version 1 ships only NoDefender; scenario files say "defender": "none".</summary>
    public interface IDefender
    {
        string Name { get; }
        void Attach(RangeNet net);
        void OnNoise(NoiseEvent e);
        void Tick(long now);
    }
    public class NoDefender : IDefender
    {
        public string Name { get { return "none"; } }
        public void Attach(RangeNet net) { }
        public void OnNoise(NoiseEvent e) { }
        public void Tick(long now) { }
    }

    /// <summary>Simulated password hashes for the pretend Windows hosts and service tickets. They are Cyber Range's own
    /// formats (not Windows' real ones) and only mean something inside the simulator.</summary>
    public static class RangeHash
    {
        static string Hex(string s, int n)
        {
            using (var h = SHA256.Create())
            {
                var b = h.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString().Substring(0, n);
            }
        }
        public static string Nt(string password) { return Hex("range-nt|" + (password ?? ""), 32); }
        public static string Ticket(string user, string password) { return "$rangetgs$" + user + "$" + Hex("range-tgs|" + user.ToLowerInvariant() + "|" + (password ?? ""), 48); }
        public static string Sha256(string s) { return Hex(s ?? "", 64); }
        public static string Md5(string s)
        {
            using (var h = MD5.Create()) { var b = h.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")); var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        }
        public static string Sha1(string s)
        {
            using (var h = SHA1.Create()) { var b = h.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")); var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        }
    }

    public partial class RangeNet
    {
        public LabNet Lab;
        public PsNet Ps;
        public string Start = "";
        public readonly List<RangeHost> Hosts = new List<RangeHost>();
        public readonly List<RangeRoute> Routes = new List<RangeRoute>();
        public readonly List<RangeWebApp> WebApps = new List<RangeWebApp>();
        public readonly Dictionary<string, Dictionary<int, string>> MachineVersions = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<NoiseEvent> Events = new List<NoiseEvent>();
        public readonly Dictionary<string, SeenHost> Seen = new Dictionary<string, SeenHost>();
        public readonly List<LootItem> Worked = new List<LootItem>();     // credentials that signed in somewhere (auto-capture)
        public readonly List<RangeAccount> DomainAccounts = new List<RangeAccount>();
        public string DomainName = "", DomainNetbios = "", DcIp = "";
        public IDefender Defender = new NoDefender();
        /// <summary>Called (on the console's worker thread) when something the map or loot shows changed.</summary>
        public Action Changed;
        bool syncing;

        /// <summary>Programs that check who runs them themselves; the set-user-ID rule of the range leaves them alone.</summary>
        public static readonly HashSet<string> SelfCheckingSuid = new HashSet<string>(StringComparer.Ordinal) { "passwd", "su", "sudo", "sudoedit", "mount", "umount", "chage", "newgrp", "gpasswd", "crontab", "at", "chsh", "chfn", "unix_chkpwd", "ssh-agent" };

        /// <summary>The Red-vs-defender block: true if the simulated defender has cut this source off from that host.</summary>
        public bool DefenderBlocks(Machine from, string ip)
        {
            var d = Defender as RangeDefender;
            return d != null && d.BlocksReach(from, ip);
        }

        public RangeHost HostAt(string ip)
        {
            foreach (var h in Hosts) foreach (var n in h.Nics) if (n.Split('/')[0] == ip) return h;
            return null;
        }
        public RangeHost HostByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (LabNet.IsIp(name)) return HostAt(name);
            return Hosts.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || h.Hostname.Equals(name, StringComparison.OrdinalIgnoreCase) || h.Hostname.Split('.')[0].Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Is the address on a network one of the machine's interfaces is on (or its own)?</summary>
        public bool DirectlyConnected(Machine from, string ip)
        {
            if (ip.StartsWith("127.", StringComparison.Ordinal)) return true;
            foreach (var nif in from.Ifs)
            {
                if (!nif.Up || !nif.Carrier) continue;
                foreach (var a in nif.V4) if (a.Split('/')[0] == ip || LabNet.SameNet(a, ip)) return true;
            }
            return false;
        }

        static bool InNet(string spec, string ip)
        {
            if (spec.Contains("/")) return LabNet.SameNet(spec, ip);
            return spec == ip;
        }

        /// <summary>Does the router let traffic from this machine reach the address (on this port; -1 = any port)?</summary>
        public bool RouteAllows(Machine from, string ip, int port)
        {
            foreach (var r in Routes)
            {
                if (!InNet(r.To, ip)) continue;
                if (port >= 0 && r.Ports.Count > 0 && !r.Ports.Contains(port)) continue;
                foreach (var nif in from.Ifs)
                {
                    if (!nif.Up) continue;
                    foreach (var a in nif.V4) if (InNet(r.From, a.Split('/')[0])) return true;
                }
            }
            return false;
        }

        /// <summary>A Windows host is reachable from this machine on this port (the same rules as Machine.Connect).</summary>
        public string ConnectHost(Machine from, string host, int port, out RangeHost h, out string ip)
        {
            h = null;
            object target;
            string err = from.Connect(host, port, out target, out ip);
            h = target as RangeHost;
            if (err == null && h == null) return "refused";
            return err;
        }

        public string VersionOf(Machine t, int port)
        {
            Dictionary<int, string> d; string v;
            if (MachineVersions.TryGetValue(t.Name, out d) && d.TryGetValue(port, out v)) return v;
            return null;
        }

        // ------------------------------------------------------------ the clock
        public void AfterAdvance(long seconds)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                if (Ps != null)
                {
                    var want = new DateTime(1970, 1, 1).AddSeconds(Lab.Now);
                    if (Ps.Now < want) Ps.Advance((long)(want - Ps.Now).TotalSeconds);
                }
                Defender.Tick(Lab.Now);
            }
            finally { syncing = false; }
        }
        /// <summary>After a Windows shell moved its own clock (Start-Sleep), the Linux machines catch up.</summary>
        public void SyncFromPs()
        {
            if (Ps == null || syncing) return;
            var lab = new DateTime(1970, 1, 1).AddSeconds(Lab.Now);
            if (Ps.Now > lab) Lab.Advance((long)(Ps.Now - lab).TotalSeconds);
        }

        // ------------------------------------------------------------ what the player did (map, loot, noise)
        public string IpOf(Machine m) { return m == null ? "" : (m.FirstV4() ?? m.Name); }

        public void Noise(Machine from, string target, string kind, int loud, string detail)
        {
            var e = new NoiseEvent { Time = Lab.Now, Source = IpOf(from), Target = target ?? "", Kind = kind, Loud = loud, Detail = detail ?? "" };
            Events.Add(e);
            if (Events.Count > 5000) Events.RemoveRange(0, 1000);
            Defender.OnNoise(e);
        }

        SeenHost See(string ip)
        {
            SeenHost s;
            if (!Seen.TryGetValue(ip, out s))
            {
                s = new SeenHost { Ip = ip };
                Seen[ip] = s;
            }
            return s;
        }

        /// <summary>nmap reported this host (open: its open ports, or null for a ping scan).</summary>
        public void Scanned(Machine from, string ip, List<int> open, int portsTried, bool versions)
        {
            if (Lab.Owner(ip) == null) return;
            var s = See(ip);
            if (open != null) foreach (var p in open) if (!s.Ports.Contains(p)) s.Ports.Add(p);
            s.Ports.Sort();
            var rh = HostAt(ip);
            if (versions || open != null && open.Count > 0) s.Os = rh != null ? "Windows" : "Linux";
            int loud = open == null ? 1 : versions ? 3 : portsTried > 100 ? 3 : portsTried > 10 ? 2 : 1;
            Noise(from, ip, open == null ? "ping-sweep" : "port-scan", loud, open == null ? "" : portsTried + " ports");
            var c = Changed; if (c != null) c();
        }

        /// <summary>A sign-in worked (ssh, the Windows remote shell, file shares, ftp, the database).</summary>
        public void LoggedIn(Machine from, string ip, string user, bool admin, string how, string secret, string detail)
        {
            var s = See(ip);
            var owner = Lab.Owner(ip);
            var m = owner as Machine; var rh = owner as RangeHost;
            if (m != null) { s.Name = m.ShortHost; if (s.Os == "") s.Os = "Linux"; }
            if (rh != null) { s.Name = rh.Name; s.Os = "Windows"; }
            bool shell = how == "ssh" || how == "winrm";
            if (shell)
            {
                string lvl = admin ? "admin" : "user";
                if (s.Owned != "admin") s.Owned = lvl;
            }
            if (!s.Users.Contains(user)) s.Users.Add(user);
            if (!string.IsNullOrEmpty(secret) && !Worked.Any(w => w.Host == ip && w.User == user && w.Secret == secret))
                Worked.Add(new LootItem { Host = ip, User = user, Secret = secret, Where = "signed in with " + how, Auto = true });
            Noise(from, ip, "login", 1, how + " as " + user + (detail != null ? " (" + detail + ")" : ""));
            var c = Changed; if (c != null) c();
        }

        /// <summary>Someone used sudo or su on a range machine (not the start box): a "privilege" event for a defender that
        /// watches for it. The source is the player's own box (the only actor in the range).</summary>
        public void Privilege(Machine m, string user, string what)
        {
            if (m == null || m.Name == Start) return;
            var start = Lab.Get(Start);
            var e = new NoiseEvent { Time = Lab.Now, Source = start != null ? IpOf(start) : "", Target = IpOf(m), Kind = "privilege", Loud = 1, Detail = user + ": " + what };
            Events.Add(e);
            Defender.OnNoise(e);
            if (what.StartsWith("sudo", StringComparison.Ordinal) || what == "su root") Elevated(m);
        }

        /// <summary>A command in a remote Windows shell that starts something new (a process, a task, a service):
        /// a "new-process" event for a defender that watches for it.</summary>
        public void RemoteCommand(Machine from, string ip, string line)
        {
            var t = (line ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(t, "(?i)\\b(Start-Process|Register-ScheduledTask|New-ScheduledTask|schtasks|New-Service|sc\\.exe|sc\\s+(create|config|start)|Start-Service|Restart-Service|Start-ScheduledTask|Invoke-Expression|iex|-EncodedCommand|-enc)\\b")) return;
            Noise(from, ip, "new-process", 2, t.Length > 60 ? t.Substring(0, 60) + "..." : t);
        }

        /// <summary>The player became root (or an administrator) on a machine without signing in again (sudo, su, a set-user-ID program).</summary>
        public void Elevated(Machine m)
        {
            if (m == null || m.Name == Start) return;
            var s = See(IpOf(m));
            s.Name = m.ShortHost; if (s.Os == "") s.Os = "Linux";
            s.Owned = "admin";
            var c = Changed; if (c != null) c();
        }
    }
}
