// Cert Lab - the AZ-800 course's pretend network (Harborline Logistics, corp.example; made up): domain controllers
// DC1 and DC2, the file server FS1, the Hyper-V host HV1, the Server Core box CORE1, the member server SRV1 and the
// Windows 11 client CL1, each a simulated PowerShell machine (PsMachine) on one PsNet, plus the directory (one copy per
// DC), Group Policy, DNS, DHCP, files and storage, Hyper-V, and a pretend Azure subscription (WsAzure). Everything runs
// on the simulated clock: replication, Group Policy refresh, DHCP leases, sync cycles. Nothing reaches a real network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public PsNet Net;
        public string DomainDns = "corp.example", NetBios = "CORP", Company = "Harborline Logistics";
        public string DomainDn { get { return Dn.FromDns(DomainDns); } }
        public string ConfigDn { get { return "CN=Configuration," + DomainDn; } }
        public string SitesDn { get { return "CN=Sites," + ConfigDn; } }
        public readonly Dictionary<string, WsDir> Dirs = new Dictionary<string, WsDir>(StringComparer.OrdinalIgnoreCase);   // DC name -> its copy
        public readonly Dictionary<string, WsServer> Servers = new Dictionary<string, WsServer>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Fsmo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);    // role -> DC
        public static readonly string[] FsmoRoles = { "SchemaMaster", "DomainNamingMaster", "PDCEmulator", "RIDMaster", "InfrastructureMaster" };
        public readonly List<string> Log = new List<string>();      // what happened on the clock (the workspace shows the end)
        public DateTime Now { get { return Net.Now; } }
        public WsAzure Azure;
        public readonly List<Action> AfterSeed = new List<Action>();   // timers the parts start once the seed is done
        int guidCount;

        public static WsLab Create(int seed)
        {
            PsEnums.Init();
            var lab = new WsLab { Net = new PsNet { Seed = seed, Rng = new Random(seed) } };
            lab.Build();
            return lab;
        }

        public static readonly string[][] MachineList = {
            new[] { "DC1", "10.10.0.10", "dc" }, new[] { "DC2", "10.20.0.11", "dc" }, new[] { "FS1", "10.10.0.20", "files" },
            new[] { "HV1", "10.10.0.30", "hyperv" }, new[] { "CORE1", "10.10.0.40", "core" }, new[] { "SRV1", "10.10.0.50", "member" },
            new[] { "CL1", "10.10.0.101", "client" } };

        void Build()
        {
            foreach (var row in MachineList)
            {
                bool server = row[2] != "client";
                var m = new PsMachine(Net, row[0], server, row[1]) { Domain = DomainDns, DomainShort = NetBios };
                m.Ws = this;
                Net.Machines.Add(m);
                Net.Hosts.Add(new LabHost { Name = row[0], Ip = row[1], Machine = m, Ports = new List<int> { 135, 445, 5985 } });
                Servers[row[0]] = new WsServer { Name = row[0], M = m, Lab = this, Kind = row[2] };
                m.Domain = DomainDns; m.DomainShort = NetBios;
            }
            Seed();
            foreach (var m in Net.Machines) m.Boot(true);
            foreach (var a in AfterSeed) a();
            ScheduleDfsr();
            At("gp:secrefresh", Now.AddHours(16), () => ApplyDomainSecurity(Fsmo["PDCEmulator"]), false);
            var first = Net.Machines[0];
            first.NextTimerHooks.Add(NextEvent);
            first.TimerHooks.Add(RunDue);
            foreach (var s in Servers.Values) { var s0 = s; s.AfterBoot(); s.M.RebootListeners.Add(() => s0.AfterBoot()); }
        }

        public WsServer Server(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.Trim().TrimEnd('.');
            if (n.StartsWith("\\\\", StringComparison.Ordinal)) n = n.Substring(2);
            int dot = n.IndexOf('.');
            if (dot > 0 && n.Substring(dot + 1).Equals(DomainDns, StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, dot);
            WsServer s; if (Servers.TryGetValue(n, out s)) return s;
            return Servers.Values.FirstOrDefault(x => x.M.Ip == n);
        }
        public PsMachine M(string name) { var s = Server(name); return s == null ? null : s.M; }

        public string NewGuid()
        {
            guidCount++;
            var b = new byte[16]; Net.Rng.NextBytes(b);
            b[7] = (byte)((b[7] & 0x0F) | 0x40); b[8] = (byte)((b[8] & 0x3F) | 0x80);
            return new Guid(b).ToString();
        }
        public void Note(string s) { Log.Add(Now.ToString("ddd HH:mm", CultureInfo.InvariantCulture) + "  " + s); if (Log.Count > 300) Log.RemoveAt(0); }

        // ------------------------------------------------------------ the clock
        class Timer { public string Key; public DateTime Due; public Action Run; }
        readonly List<Timer> timers = new List<Timer>();

        /// <summary>Runs an action at a time; a timer with the same key is replaced (keepEarlier: unless it is due sooner).</summary>
        public void At(string key, DateTime due, Action run, bool keepEarlier)
        {
            var old = timers.FirstOrDefault(t => t.Key == key);
            if (old != null) { if (keepEarlier && old.Due <= due) return; timers.Remove(old); }
            timers.Add(new Timer { Key = key, Due = due, Run = run });
        }
        public void Cancel(string key) { timers.RemoveAll(t => t.Key == key); }
        public DateTime? Due(string key) { var t = timers.FirstOrDefault(x => x.Key == key); return t == null ? (DateTime?)null : t.Due; }

        DateTime? NextEvent(DateTime now)
        {
            DateTime? n = null;
            foreach (var t in timers) if (t.Due > now && (!n.HasValue || t.Due < n.Value)) n = t.Due;
            return n;
        }
        void RunDue(DateTime now)
        {
            for (int guard = 0; guard < 500; guard++)
            {
                var t = timers.Where(x => x.Due <= now).OrderBy(x => x.Due).FirstOrDefault();
                if (t == null) return;
                timers.Remove(t);
                t.Run();
            }
        }
        /// <summary>Moves the clock on (everything due runs in order).</summary>
        public void Advance(long seconds) { Net.Advance(seconds); RunDue(Now); }
    }

    /// <summary>One pretend machine's roles and services (created from its installed features).</summary>
    public partial class WsServer
    {
        public string Name = "", Kind = "";
        public PsMachine M; public WsLab Lab;
        public bool IsDc { get { return Lab.Dirs.ContainsKey(Name); } }
        public bool IsClient { get { return Kind == "client"; } }
        public bool Running { get { return M.State == "running"; } }
        public bool Has(string feature) { return M.FeatureInstalled(feature); }
        public bool SvcRunning(string svc) { var s = M.Svc(svc); return s != null && s.Running; }
        partial void OnAfterBoot();
        public void AfterBoot() { OnAfterBoot(); }
    }
}
