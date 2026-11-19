// Cert Lab - Windows administration cmdlets, part 1: services (Get-Service, Start-, Stop-, Restart-, Suspend-,
// Resume-, Set-, New-Service) and processes (Get-Process, Stop-Process, Start-Process, Wait-Process), with the
// objects and error messages of Windows PowerShell 5.1. They work on the pretend machine's own lists.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        public const string Diag = "Microsoft.PowerShell.Diagnostics", Local = "Microsoft.PowerShell.LocalAccounts", Cim = "CimCmdlets", Sched = "ScheduledTasks", SrvMgr = "ServerManager";
        const string SvcEx = "Microsoft.PowerShell.Commands.ServiceCommandException", ProcEx = "Microsoft.PowerShell.Commands.ProcessCommandException";

        static partial void RegisterAdmin()
        {
            AddType("ServiceStartMode", "System.ServiceProcess.ServiceStartMode");
            AddType("CimClass", "Microsoft.Management.Infrastructure.CimClass");
            AddType("CimInstance", "Microsoft.Management.Infrastructure.CimInstance");
            AddType("EventLogEntryType", "System.Diagnostics.EventLogEntryType");
            AddType("CimTrigger", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskTrigger");
            AddType("CimAction", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskAction");
            AddType("CimPrincipal", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskPrincipal");
            AddType("CimSettings", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskSettings");
            AddType("SecurityIdentifier", "System.Security.Principal.SecurityIdentifier");
            AddType("Feature", "Microsoft.Windows.ServerManager.Commands.Feature");
            PsEnums.Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.LogonTypeEnum", "None=0,Password=1,S4U=2,Interactive=3,Group=4,ServiceAccount=5,InteractiveOrPassword=6");
            PsEnums.Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.RunLevelEnum", "Limited=0,Highest=1");
            PsEnums.Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.MultipleInstancesEnum", "Parallel=0,Queue=1,IgnoreNew=2,StopExisting=3");
            PsEnums.Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.CompatibilityEnum", "At=0,V1=1,Vista=2,Win7=3,Win8=4");
            PsEnums.Define("System.Diagnostics.OverflowAction", "DoNotOverwrite=-1,OverwriteAsNeeded=0,OverwriteOlder=1");
            PsEnums.Define("System.Diagnostics.Eventing.Reader.EventLogMode", "Circular=0,AutoBackup=1,Retain=2");
            PsEnums.Define("System.Diagnostics.Eventing.Reader.EventLogType", "Administrative=0,Operational=1,Analytical=2,Debug=3");
            PsEnums.Define("System.Diagnostics.ProcessPriorityClass", "Normal=32,Idle=64,High=128,RealTime=256,BelowNormal=16384,AboveNormal=32768");
            PsEnums.Define("System.Diagnostics.ProcessWindowStyle", "Normal=0,Hidden=1,Minimized=2,Maximized=3");
            PsEnums.Define("Microsoft.PowerShell.Commands.WaitForServiceTypes", "Wait=0");
            PsEnums.Define("Microsoft.Management.Infrastructure.CimCmdlets.ProtocolType", "Default=0,Dcom=1,Wsman=2");
            PsEnums.Define("Microsoft.PowerShell.Commands.ObjectClass", "User=1,Group=2");
            RegServices();
            RegProcesses();
            RegEvents();
            RegTasks();
            RegAccounts();
            RegCim();
            RegComputer();
            RegFeatures();
            AdminViews();
        }

        // ------------------------------------------------------------ helpers shared by the admin cmdlets
        /// <summary>The machine a -ComputerName names (null or ".": this one).</summary>
        public static PsMachine RemoteMachine(CmdCtx c, string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "127.0.0.1" || name.Equals(c.M.Name, StringComparison.OrdinalIgnoreCase) || name.Equals(c.M.Fqdn, StringComparison.OrdinalIgnoreCase)) return c.M;
            return c.M.Net.Get(name);
        }
        /// <summary>A machine reached over RPC / DCOM (Get-Service -ComputerName, Get-WmiObject, Get-EventLog...): null when it
        /// does not exist or its firewall does not let TCP 135 in from this machine (WIN-CL1's WMI rules are off).</summary>
        public static PsMachine RemoteRpc(CmdCtx c, string name)
        {
            var m = RemoteMachine(c, name);
            if (m == null || m == c.M) return m;
            return m.State == "running" && m.FirewallAllows("TCP", 135, c.M.Ip) ? m : null;
        }
        static bool Matches(string value, string[] patterns) { return patterns == null || patterns.Any(p => PsConv.Like(value, p, false)); }
        static bool Excluded(string value, string[] patterns) { return patterns != null && patterns.Any(p => PsConv.Like(value, p, false)); }
        static PsEnum En(string type, string name) { return PsEnums.V(type, name); }
        static string Q(string s) { return "'" + s + "'"; }

        // ------------------------------------------------------------ service objects
        public static PsObj SvcObj(PsMachine m, WinService s, string machineName) { return SvcObj(m, s, machineName, false); }
        static PsObj SvcObj(PsMachine m, WinService s, string machineName, bool nested)
        {
            var o = new PsObj("System.ServiceProcess.ServiceController", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            o.Base = s;
            Fill(o, m, s, machineName, nested);
            if (nested) o.ToStr = x => s.Name;
            o.Method("Refresh", "void Refresh()", (self, a) => { Fill(self, m, s, machineName, nested); return AutoNull.Value; });
            o.Method("Start", "void Start(), void Start(string[] args)", (self, a) =>
            {
                var why = m.StartService(s, true);
                if (why != null) throw new PsMethodError("Cannot start service " + s.Name + " on computer '" + machineName + "'.", "System.InvalidOperationException");
                return AutoNull.Value;
            });
            o.Method("Stop", "void Stop()", (self, a) =>
            {
                var why = m.StopService(s, false, true);
                if (why == "1051") throw new PsMethodError("Cannot stop " + s.Name + " service on computer '" + machineName + "'.", "System.InvalidOperationException");
                if (why != null) throw new PsMethodError("Cannot stop " + s.Name + " service on computer '" + machineName + "'.", "System.InvalidOperationException");
                return AutoNull.Value;
            });
            o.Method("Pause", "void Pause()", (self, a) => { if (!s.CanPause || !s.Running) throw new PsMethodError("Cannot pause " + s.Name + " service on computer '" + machineName + "'.", "System.InvalidOperationException"); s.Status = "Paused"; m.ServiceEvent(s, "Paused"); return AutoNull.Value; });
            o.Method("Continue", "void Continue()", (self, a) => { if (s.Status != "Paused") throw new PsMethodError("Cannot resume " + s.Name + " service on computer '" + machineName + "'.", "System.InvalidOperationException"); s.Status = "Running"; m.ServiceEvent(s, "Running"); return AutoNull.Value; });
            o.Method("WaitForStatus", "void WaitForStatus(System.ServiceProcess.ServiceControllerStatus desiredStatus), void WaitForStatus(System.ServiceProcess.ServiceControllerStatus desiredStatus, timespan timeout)", (self, a) =>
            {
                string want = a.Length > 0 ? PsConv.ToStr(a[0]) : "Running";
                if (!s.Status.Equals(want, StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Length > 1) throw new PsMethodError("Time out has expired and the operation has not been completed.", "System.InvalidOperationException");
                    throw new PsMethodError("Cert Lab: WaitForStatus would wait forever here: the service is " + s.Status + " and nothing will change it. Give a timeout, for example WaitForStatus('" + want + "', '00:00:05').", "System.InvalidOperationException");
                }
                Fill(self, m, s, machineName, nested);
                return AutoNull.Value;
            });
            o.Method("Close", "void Close()", (self, a) => AutoNull.Value);
            o.Method("Dispose", "void Dispose()", (self, a) => AutoNull.Value);
            o.Method("ExecuteCommand", "void ExecuteCommand(int command)", (self, a) => AutoNull.Value);
            return o;
        }
        static void Fill(PsObj o, PsMachine m, WinService s, string machineName, bool nested)
        {
            o.Alias("Name", "ServiceName");
            o.Alias("RequiredServices", "ServicesDependedOn");
            o.Val("CanPauseAndContinue", "bool", s.CanPause && s.Running);
            o.Val("CanShutdown", "bool", s.CanShutdown && s.Running);
            o.Val("CanStop", "bool", s.CanStop && s.Running);
            o.Val("DisplayName", "string", s.DisplayName);
            if (nested) o.Val("DependentServices", "System.ServiceProcess.ServiceController[]", new object[0]);
            else o.Prop("DependentServices", "System.ServiceProcess.ServiceController[]", () => m.Dependents(s).Select(d => (object)SvcObj(m, d, machineName, true)).ToArray());
            o.Val("MachineName", "string", machineName);
            o.Val("ServiceName", "string", s.Name);
            if (nested) o.Val("ServicesDependedOn", "System.ServiceProcess.ServiceController[]", new object[0]);
            else o.Prop("ServicesDependedOn", "System.ServiceProcess.ServiceController[]", () => s.DependsOn.Select(d => m.Svc(d)).Where(d => d != null).Select(d => (object)SvcObj(m, d, machineName, true)).ToArray());
            o.Val("ServiceHandle", "System.Runtime.InteropServices.SafeHandle", null);
            o.Val("Status", "System.ServiceProcess.ServiceControllerStatus", En("System.ServiceProcess.ServiceControllerStatus", s.Status));
            o.Val("ServiceType", "System.ServiceProcess.ServiceType", SvcTypeEnum(s));
            o.Val("StartType", "System.ServiceProcess.ServiceStartMode", En("System.ServiceProcess.ServiceStartMode", s.StartType));
            o.Val("Site", "System.ComponentModel.ISite", null);
            o.Val("Container", "System.ComponentModel.IContainer", null);
        }
        static PsEnum SvcTypeEnum(WinService s)
        {
            var t = PsEnums.Get("System.ServiceProcess.ServiceType");
            long v = 0;
            foreach (var part in s.ServiceType.Split(',')) { var e = t.Of(part.Trim()); if (e != null) v |= e.Value; }
            if (s.Name == "Spooler") v |= 256;
            return t.Of(v);
        }
        static string SvcLabel(WinService s) { return s.DisplayName + " (" + s.Name + ")"; }

        /// <summary>The services a command names: -Name (service or display names, wildcards), -DisplayName, -InputObject.</summary>
        static List<WinService> PickServices(CmdCtx c, PsMachine m, bool errors)
        {
            var res = new List<WinService>();
            var inc = c.Strs("Include"); var exc = c.Strs("Exclude");
            if (c.Has("InputObject"))
            {
                foreach (var x in c.Objs("InputObject"))
                {
                    var po = x as PsObj; var s = po == null ? null : po.Base as WinService;
                    if (s == null || s.Deleted) { if (errors && po != null) c.Error("Cannot find any service with service name '" + PsConv.ToStr(po.Get("ServiceName")) + "'.", "NoServiceFoundForGivenName", "ObjectNotFound", PsConv.ToStr(po.Get("ServiceName")), SvcEx); continue; }
                    res.Add(s);
                }
                return res;
            }
            if (c.Has("DisplayName"))
            {
                foreach (var dn in c.Strs("DisplayName"))
                {
                    var found = m.LiveServices.Where(s => s.ServiceType != "KernelDriver" && PsConv.Like(s.DisplayName, dn, false)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    if (found.Count == 0 && !PsConv.HasWildcard(dn) && errors) c.Error("Cannot find any service with display name '" + dn + "'.", "NoServiceFoundForGivenDisplayName", "ObjectNotFound", dn, SvcEx);
                    foreach (var s in found) if (!res.Contains(s) && Matches(s.Name, inc) && !Excluded(s.Name, exc)) res.Add(s);
                }
                return res;
            }
            var names = c.Strs("Name") ?? new[] { "*" };
            foreach (var n in names)
            {
                if (PsConv.HasWildcard(n))
                {
                    foreach (var s in m.LiveServices.Where(x => x.ServiceType != "KernelDriver" && PsConv.Like(x.Name, n, false)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                        if (!res.Contains(s) && Matches(s.Name, inc) && !Excluded(s.Name, exc)) res.Add(s);
                    continue;
                }
                var one = m.Svc(n) ?? m.SvcByDisplay(n);
                if (one == null) { if (errors) c.Error("Cannot find any service with service name '" + n + "'.", "NoServiceFoundForGivenName", "ObjectNotFound", n, SvcEx); continue; }
                if (Matches(one.Name, inc) && !Excluded(one.Name, exc) && !res.Contains(one)) res.Add(one);
            }
            return res;
        }
        static string MachineLabel(CmdCtx c, PsMachine m) { return m == c.M ? "." : m.Name; }

        static void RegServices()
        {
            const string sel = "Name:string[]@0 pipe prop alias=ServiceName ps=Default; DisplayName:string[] ps=DisplayName; InputObject:ServiceController[] pipe ps=InputObject; Include:string[]; Exclude:string[]";
            var gs = Add("Get-Service", Mgmt, "Name:string[]@0 pipe prop alias=ServiceName ps=Default; DisplayName:string[] ps=DisplayName; InputObject:ServiceController[] pipe ps=InputObject; ComputerName:string[] prop alias=Cn; DependentServices:switch alias=DS; RequiredServices:switch alias=SDO,ServicesDependedOn; Include:string[]; Exclude:string[]", (c, o) =>
            {
                foreach (var cn in c.Strs("ComputerName") ?? new[] { "." })
                {
                    var m = RemoteRpc(c, cn);
                    if (m == null) { c.Error("Cannot open Service Control Manager on computer '" + cn + "'. This operation might require other privileges.", "System.InvalidOperationException", "NotSpecified", null, "System.InvalidOperationException"); continue; }
                    string ml = m == c.M ? "." : cn;
                    foreach (var s in PickServices(c, m, true))
                    {
                        if (c.Sw("DependentServices")) { foreach (var d in m.Dependents(s).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) c.Out(SvcObj(m, d, ml)); continue; }
                        if (c.Sw("RequiredServices")) { foreach (var d in s.DependsOn.Select(x => m.Svc(x)).Where(x => x != null)) c.Out(SvcObj(m, d, ml)); continue; }
                        c.Out(SvcObj(m, s, ml));
                    }
                }
            });
            gs.DefaultSet = "Default";

            Action<string, string, Action<CmdCtx, WinService>> control = (name, spec, act) =>
            {
                var d = Add(name, Mgmt, sel + "; PassThru:switch" + (spec.Length > 0 ? "; " + spec : ""), (c, o) =>
                {
                    foreach (var s in PickServices(c, c.M, true))
                    {
                        if (!c.ShouldProcess(SvcLabel(s), name)) continue;
                        int before = c.R.Errors.Count;
                        act(c, s);
                        if (c.Sw("PassThru") && c.R.Errors.Count == before) c.Out(SvcObj(c.M, s, "."));
                    }
                });
                d.ShouldProcess = true; d.DefaultSet = "Default";
            };
            control("Start-Service", "", (c, s) =>
            {
                if (s.Running) return;
                var why = c.M.StartService(s, true);
                if (why != null) c.Error("Service '" + SvcLabel(s) + "' cannot be started due to the following error: Cannot start service " + s.Name + " on computer '.'.", "CouldNotStartService", "OpenError", SvcObj(c.M, s, "."), SvcEx);
                else c.M.AdminChanged("service:" + s.Name);
            });
            control("Stop-Service", "Force:switch; NoWait:switch", (c, s) =>
            {
                if (!s.Running) return;
                var why = c.M.StopService(s, c.Sw("Force"), true);
                if (why == "1051") c.Error("Cannot stop service '" + SvcLabel(s) + "' because it has dependent services. It can only be stopped if the Force flag is set.", "ServiceHasDependentServices", "InvalidOperation", SvcObj(c.M, s, "."), SvcEx);
                else if (why != null) c.Error("Service '" + SvcLabel(s) + "' cannot be stopped due to the following error: Cannot stop " + s.Name + " service on computer '.'.", "CouldNotStopService", "CloseError", SvcObj(c.M, s, "."), SvcEx);
                else c.M.AdminChanged("service:" + s.Name);
            });
            control("Restart-Service", "Force:switch", (c, s) =>
            {
                var deps = c.M.Dependents(s).Where(d => d.Running).ToList();
                if (s.Running)
                {
                    var why = c.M.StopService(s, c.Sw("Force"), true);
                    if (why == "1051") { c.Error("Cannot stop service '" + SvcLabel(s) + "' because it has dependent services. It can only be stopped if the Force flag is set.", "ServiceHasDependentServices", "InvalidOperation", SvcObj(c.M, s, "."), SvcEx); return; }
                    if (why != null) { c.Error("Service '" + SvcLabel(s) + "' cannot be stopped due to the following error: Cannot stop " + s.Name + " service on computer '.'.", "CouldNotStopService", "CloseError", SvcObj(c.M, s, "."), SvcEx); return; }
                }
                var w2 = c.M.StartService(s, true);
                if (w2 != null) { c.Error("Service '" + SvcLabel(s) + "' cannot be started due to the following error: Cannot start service " + s.Name + " on computer '.'.", "CouldNotStartService", "OpenError", SvcObj(c.M, s, "."), SvcEx); return; }
                foreach (var d in deps) c.M.StartService(d, true);
                c.M.AdminChanged("service:" + s.Name);
            });
            control("Suspend-Service", "", (c, s) =>
            {
                if (!s.CanPause) { c.Error("Service '" + SvcLabel(s) + "' cannot be suspended because the service does not support being suspended or resumed.", "CouldNotSuspendServiceNotSupported", "CloseError", SvcObj(c.M, s, "."), SvcEx); return; }
                if (!s.Running) { c.Error("Service '" + SvcLabel(s) + "' cannot be suspended because it is not currently running.", "CouldNotSuspendServiceNotRunning", "CloseError", SvcObj(c.M, s, "."), SvcEx); return; }
                if (s.Status == "Paused") return;
                s.Status = "Paused"; c.M.ServiceEvent(s, "Paused");
            });
            control("Resume-Service", "", (c, s) =>
            {
                if (!s.CanPause) { c.Error("Service '" + SvcLabel(s) + "' cannot be resumed because the service does not support being suspended or resumed.", "CouldNotResumeServiceNotSupported", "CloseError", SvcObj(c.M, s, "."), SvcEx); return; }
                if (s.Status != "Paused") return;
                s.Status = "Running"; c.M.ServiceEvent(s, "Running");
            });

            var ss = Add("Set-Service", Mgmt, "Name:string@0 ! pipe prop alias=ServiceName,SN ps=Name; InputObject:ServiceController pipe ps=InputObject; ComputerName:string[] prop alias=cn; DisplayName:string alias=DN; Description:string; StartupType:ServiceStartMode alias=StartMode,SM,ST; Status:string set=Running|Stopped|Paused; PassThru:switch", (c, o) =>
            {
                foreach (var cn in c.Strs("ComputerName") ?? new[] { "." })
                {
                    var m = RemoteRpc(c, cn);
                    WinService s;
                    if (c.Has("InputObject")) { var po = c.Get("InputObject") as PsObj; s = po == null ? null : po.Base as WinService; if (po != null && po.Get("MachineName") != null && PsConv.ToStr(po.Get("MachineName")) != ".") m = RemoteRpc(c, PsConv.ToStr(po.Get("MachineName"))); }
                    else s = m == null ? null : (m.Svc(c.Str("Name")) ?? m.SvcByDisplay(c.Str("Name")));
                    if (m == null || s == null) { c.Error("Service " + (c.Str("Name") ?? "") + " was not found on computer '" + cn + "'.", "InvalidOperationException", "ObjectNotFound", cn, "System.InvalidOperationException"); continue; }
                    if (!c.ShouldProcess(SvcLabel(s), "Set-Service")) continue;
                    if (c.Has("DisplayName")) s.DisplayName = c.Str("DisplayName");
                    if (c.Has("Description")) s.Description = c.Str("Description");
                    if (c.Has("StartupType"))
                    {
                        string st = PsConv.ToStr(c.Get("StartupType"));
                        if (st == "Boot" || st == "System") { c.Error("Service '" + SvcLabel(s) + "' cannot be configured due to the following error: The parameter is incorrect", "CouldNotSetService", "PermissionDenied", s.Name, SvcEx); continue; }
                        m.SetStartType(s, st, true);
                    }
                    if (c.Has("Status"))
                    {
                        string want = c.Str("Status");
                        if (want.Equals("Running", StringComparison.OrdinalIgnoreCase))
                        {
                            if (s.Status == "Paused") { s.Status = "Running"; m.ServiceEvent(s, "Running"); }
                            else if (m.StartService(s, true) != null) { c.Error("Service '" + SvcLabel(s) + "' cannot be started due to the following error: Cannot start service " + s.Name + " on computer '" + MachineLabel(c, m) + "'.", "CouldNotStartService", "OpenError", SvcObj(m, s, "."), SvcEx); continue; }
                        }
                        else if (want.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
                        {
                            var why = m.StopService(s, false, true);
                            if (why == "1051") { c.Error("Cannot stop service '" + SvcLabel(s) + "' because it has dependent services.", "ServiceHasDependentServicesNoForce", "InvalidOperation", SvcObj(m, s, "."), SvcEx); continue; }
                            if (why != null) { c.Error("Service '" + SvcLabel(s) + "' cannot be stopped due to the following error: Cannot stop " + s.Name + " service on computer '.'.", "CouldNotStopService", "CloseError", SvcObj(m, s, "."), SvcEx); continue; }
                        }
                        else
                        {
                            if (!s.CanPause) { c.Error("Service '" + SvcLabel(s) + "' cannot be suspended because the service does not support being suspended or resumed.", "CouldNotSuspendServiceNotSupported", "CloseError", SvcObj(m, s, "."), SvcEx); continue; }
                            if (s.Running && s.Status != "Paused") { s.Status = "Paused"; m.ServiceEvent(s, "Paused"); }
                        }
                    }
                    m.AdminChanged("service:" + s.Name);
                    if (c.Sw("PassThru")) c.Out(SvcObj(m, s, MachineLabel(c, m)));
                }
            });
            ss.ShouldProcess = true; ss.DefaultSet = "Name";

            var ns = Add("New-Service", Mgmt, "Name:string@0 ! alias=ServiceName; BinaryPathName:string@1 ! alias=Path; DisplayName:string; Description:string; StartupType:ServiceStartMode; Credential:pscredential; DependsOn:string[]", (c, o) =>
            {
                string name = c.Str("Name");
                if (c.M.Svc(name) != null || c.M.SvcByDisplay(name) != null) { c.Error("Service '" + name + "' cannot be created due to the following error: The specified service already exists", "CouldNotNewService", "PermissionDenied", name, SvcEx); return; }
                if (!c.ShouldProcess(name, "New-Service")) return;
                var s = new WinService { Name = name, DisplayName = c.Str("DisplayName") ?? name, Description = c.Str("Description") ?? "", BinaryPath = c.Str("BinaryPathName"), StartType = c.Has("StartupType") ? PsConv.ToStr(c.Get("StartupType")) : "Automatic", Custom = true, ServiceType = "Win32OwnProcess" };
                var exe = PsMachine.ExeOf(s.BinaryPath); s.Exe = WinPath.GetFileNameWithoutExtension(exe.Length == 0 ? name : exe);
                var cred = c.Get("Credential") as PsObj;
                if (cred != null) s.Account = PsConv.ToStr(cred.Get("UserName"));
                if (c.Has("DependsOn")) s.DependsOn.AddRange(c.Strs("DependsOn"));
                c.M.Services.Add(s);
                c.M.Event("System", "Service Control Manager", 7045, 4, "A service was installed in the system.\r\n\r\nService Name:  " + s.Name + "\r\nService File Name:  " + s.BinaryPath + "\r\nService Type:  user mode service\r\nService Start Type:  " + PsMachine.StartWord(s.StartType, false) + "\r\nService Account:  " + s.Account, c.M.AdminUserSid);
                c.M.AdminChanged("service:" + s.Name);
                c.Out(SvcObj(c.M, s, "."));
            });
            ns.ShouldProcess = true;
        }

        // ------------------------------------------------------------ process objects
        static readonly HashSet<string> NoUserInfo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "services", "MpDefenderCoreService" };
        public static PsObj ProcObj(PsMachine m, WinProcess p, string machineName, bool withUser)
        {
            var o = new PsObj("System.Diagnostics.Process", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            if (withUser) o.TypeNames.Insert(0, "System.Diagnostics.Process#IncludeUserName");
            o.Base = p;
            o.ToStr = x => "System.Diagnostics.Process (" + p.Name + ")";
            o.DefaultDisplay = null;
            bool hidden = NoUserInfo.Contains(p.Name);
            o.Alias("Name", "ProcessName");
            o.Alias("SI", "SessionId");
            o.Alias("Handles", "Handlecount");
            o.Alias("VM", "VirtualMemorySize64");
            o.Alias("WS", "WorkingSet64");
            o.Alias("PM", "PagedMemorySize64");
            o.Alias("NPM", "NonpagedSystemMemorySize64");
            o.Prop("Path", "string", () => hidden ? null : p.Path);
            o.Prop("Company", "string", () => hidden || p.Path == null ? null : p.Company);
            o.Prop("CPU", "double", () => hidden && p.Id <= 4 ? (object)null : p.Cpu);
            o.Prop("FileVersion", "string", () => hidden || p.Path == null ? null : p.Version + " (WinBuild.160101.0800)");
            o.Prop("ProductVersion", "string", () => hidden || p.Path == null ? null : p.Version);
            o.Prop("Description", "string", () => hidden || p.Path == null ? null : p.Description);
            o.Prop("Product", "string", () => hidden || p.Path == null ? null : (p.Company == "Microsoft Corporation" ? "Microsoft\u00ae Windows\u00ae Operating System" : p.Description));
            o.Val("__NounName", "string", "Process");
            o.Val("BasePriority", "int", p.Priority);
            o.Prop("ExitCode", "int", () => p.Exited ? (object)p.ExitCode : null);
            o.Prop("HasExited", "bool", () => hidden ? (object)null : p.Exited);
            o.Prop("ExitTime", "datetime", () => p.Exited ? (object)p.ExitTime : null);
            o.Val("Handle", "System.IntPtr", hidden ? null : (object)(long)(2000 + p.Id % 1000));
            o.Prop("HandleCount", "int", () => p.Handles);
            o.Val("Id", "int", p.Id);
            o.Val("MachineName", "string", machineName);
            o.Val("MainWindowHandle", "System.IntPtr", (long)(p.MainWindowTitle.Length > 0 ? 65552 + p.Id : 0));
            o.Val("MainWindowTitle", "string", p.MainWindowTitle);
            o.Val("MaxWorkingSet", "System.IntPtr", hidden ? null : (object)1413120L);
            o.Val("MinWorkingSet", "System.IntPtr", hidden ? null : (object)204800L);
            o.Prop("NonpagedSystemMemorySize64", "long", () => p.Npm);
            o.Prop("PagedMemorySize64", "long", () => p.Pm);
            o.Val("PriorityClass", "System.Diagnostics.ProcessPriorityClass", hidden ? null : En("System.Diagnostics.ProcessPriorityClass", "Normal"));
            o.Prop("PrivateMemorySize64", "long", () => p.Pm);
            o.Val("ProcessName", "string", p.Name);
            o.Val("Responding", "bool", true);
            o.Val("SessionId", "int", p.SessionId);
            o.Prop("StartTime", "datetime", () => hidden ? (object)null : p.Start);
            o.Prop("Threads", "System.Diagnostics.ProcessThreadCollection", () => Enumerable.Range(0, Math.Min(p.Threads, 6)).Select(i => (object)(p.Id + 4 + i * 8)).ToArray());
            o.Prop("TotalProcessorTime", "timespan", () => hidden ? (object)null : TimeSpan.FromSeconds(p.Cpu));
            o.Prop("UserProcessorTime", "timespan", () => hidden ? (object)null : TimeSpan.FromSeconds(Math.Round(p.Cpu * 0.6, 3)));
            o.Prop("PrivilegedProcessorTime", "timespan", () => hidden ? (object)null : TimeSpan.FromSeconds(Math.Round(p.Cpu * 0.4, 3)));
            o.Prop("VirtualMemorySize64", "long", () => p.Vm);
            o.Prop("WorkingSet64", "long", () => p.Ws);
            o.Prop("VirtualMemorySize", "int", () => (int)(p.Vm & 0x7fffffff));
            o.Prop("WorkingSet", "int", () => (int)p.Ws);
            o.Prop("PagedMemorySize", "int", () => (int)p.Pm);
            o.Val("PriorityBoostEnabled", "bool", hidden ? null : (object)true);
            o.Prop("MainModule", "System.Diagnostics.ProcessModule", () => hidden || p.Path == null ? null : (object)("System.Diagnostics.ProcessModule (" + p.Name + ".exe)"));
            if (withUser) o.Val("UserName", "string", p.User);
            o.Method("Kill", "void Kill()", (self, a) =>
            {
                if (p.Protected) throw new PsMethodError("Access is denied", "System.InvalidOperationException");
                if (!p.Exited) m.EndProcess(p, -1);
                return AutoNull.Value;
            });
            o.Method("Refresh", "void Refresh()", (self, a) => AutoNull.Value);
            o.Method("CloseMainWindow", "bool CloseMainWindow()", (self, a) => { if (p.MainWindowTitle.Length == 0 || p.Exited) return false; m.EndProcess(p, 0); return true; });
            o.Method("WaitForExit", "void WaitForExit(), bool WaitForExit(int milliseconds)", (self, a) =>
            {
                if (p.Exited) return a.Length > 0 ? (object)true : AutoNull.Value;
                if (a.Length > 0) { m.Net.Advance(PsConv.ToInt(a[0]) / 1000); return p.Exited; }
                throw new PsMethodError("Cert Lab: WaitForExit() would wait until someone closes " + p.Name + ". Give a time in milliseconds, for example WaitForExit(5000).", "System.InvalidOperationException");
            });
            return o;
        }

        static List<WinProcess> Sorted(IEnumerable<WinProcess> l) { return l.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id).ToList(); }
        static string ProcLabel(WinProcess p) { return p.Name + " (" + p.Id + ")"; }

        static List<WinProcess> PickProcesses(CmdCtx c, PsMachine m, bool errors)
        {
            var live = m.LiveProcesses();
            var res = new List<WinProcess>();
            if (c.Has("InputObject"))
            {
                foreach (var x in c.Objs("InputObject")) { var po = x as PsObj; var p = po == null ? null : po.Base as WinProcess; if (p != null) res.Add(p); }
                return res;
            }
            if (c.Has("Id"))
            {
                foreach (var id in c.Objs("Id").Select(x => PsConv.ToInt(x)))
                {
                    var p = live.FirstOrDefault(x => x.Id == id);
                    if (p == null) { if (errors) c.Error("Cannot find a process with the process identifier " + id + ".", "NoProcessFoundForGivenId", "ObjectNotFound", id, ProcEx); continue; }
                    res.Add(p);
                }
                return res;
            }
            var names = c.Strs("Name") ?? new[] { "*" };
            foreach (var n0 in names)
            {
                string n = n0.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !PsConv.HasWildcard(n0) ? n0 : n0;
                var found = Sorted(live.Where(p => PsConv.Like(p.Name, n, false)));
                if (found.Count == 0 && !PsConv.HasWildcard(n))
                {
                    if (errors) c.Error("Cannot find a process with the name \"" + n + "\". Verify the process name and call the cmdlet again.", "NoProcessFoundForGivenName", "ObjectNotFound", n, ProcEx);
                    continue;
                }
                foreach (var p in found) if (!res.Contains(p)) res.Add(p);
            }
            return res;
        }

        static void RegProcesses()
        {
            var gp = Add("Get-Process", Mgmt, "Name:string[]@0 prop alias=ProcessName ps=Name,NameWithUserName; Id:int[] ! prop alias=PID ps=Id,IdWithUserName; InputObject:Process[] ! pipe ps=InputObject,InputObjectWithUserName; ComputerName:string[] prop alias=Cn; IncludeUserName:switch ps=NameWithUserName,IdWithUserName,InputObjectWithUserName; FileVersionInfo:switch alias=FV,FVI; Module:switch", (c, o) =>
            {
                foreach (var cn in c.Strs("ComputerName") ?? new[] { "." })
                {
                    var m = RemoteRpc(c, cn);
                    if (m == null) { c.Error("Couldn't connect to remote machine.", "CouldNotEnumerateProcesses", "NotSpecified", null, "System.InvalidOperationException"); continue; }
                    if (m != c.M && c.Sw("IncludeUserName")) throw c.Fail("Parameter set cannot be resolved using the specified named parameters.", "AmbiguousParameterSet", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                    foreach (var p in PickProcesses(c, m, true))
                    {
                        if (c.Sw("FileVersionInfo") || c.Sw("Module"))
                        {
                            if (p.Path == null || NoUserInfo.Contains(p.Name)) { c.Error("Cannot enumerate the modules of the \"" + p.Name + "\" process.", "CouldNotEnumerateModules", "PermissionDenied", ProcObj(m, p, ".", false), ProcEx); continue; }
                            var f = new PsObj(c.Sw("Module") ? "System.Diagnostics.ProcessModule" : "System.Diagnostics.FileVersionInfo", "System.Object");
                            string path = p.Path.Trim('"'); int sp = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (sp > 0) path = path.Substring(0, sp + 4);
                            if (c.Sw("Module")) { f.Val("ModuleName", "string", path.Substring(path.LastIndexOf('\\') + 1)); f.Val("FileName", "string", path); f.Val("Size(K)", "int", 300 + p.Id % 900); f.Val("ModuleMemorySize", "int", (300 + p.Id % 900) * 1024); }
                            else { f.Val("ProductVersion", "string", p.Version); f.Val("FileVersion", "string", p.Version + " (WinBuild.160101.0800)"); f.Val("FileName", "string", path); f.Val("CompanyName", "string", p.Company); f.Val("FileDescription", "string", p.Description); f.Val("ProductName", "string", "Microsoft\u00ae Windows\u00ae Operating System"); }
                            c.Out(f);
                            continue;
                        }
                        c.Out(ProcObj(m, p, m == c.M ? "." : cn, c.Sw("IncludeUserName")));
                    }
                }
            });
            gp.DefaultSet = "Name";

            var sp2 = Add("Stop-Process", Mgmt, "Id:int[]@0 ! prop ps=Id; Name:string[] ! prop alias=ProcessName ps=Name; InputObject:Process[] ! pipe ps=InputObject; PassThru:switch; Force:switch", (c, o) =>
            {
                foreach (var p in PickProcesses(c, c.M, true))
                {
                    if (p.Exited) continue;
                    if (!c.ShouldProcess(ProcLabel(p), "Stop-Process")) continue;
                    if (p.Protected || p.Id <= 4)
                    {
                        c.Error("Cannot stop process \"" + ProcLabel(p) + "\" because of the following error: Access is denied", "CouldNotStopProcess", "CloseError", ProcObj(c.M, p, ".", false), ProcEx);
                        continue;
                    }
                    string me = c.R.User.Contains("\\") ? c.R.User : c.M.DomainShort + "\\" + c.R.User;
                    if (!c.Sw("Force") && !c.WhatIf && !p.User.Equals(me, StringComparison.OrdinalIgnoreCase) && c.R.Host != null && c.R.Host.Interactive)
                    {
                        string a = c.R.AskChoice("Confirm", "Are you sure you want to perform the Stop-Process operation on the following item: " + p.Name + "(" + p.Id + ")?", "Y");
                        if (a == "N" || a == "L") continue;
                    }
                    c.M.EndProcess(p, -1);
                    c.M.AdminChanged("process:" + p.Name);
                    if (c.Sw("PassThru")) c.Out(ProcObj(c.M, p, ".", false));
                }
            });
            sp2.ShouldProcess = true; sp2.DefaultSet = "Id";

            var st = Add("Start-Process", Mgmt, "FilePath:string@0 ! alias=PSPath,Path; ArgumentList:string[]@1 alias=Args; Credential:pscredential alias=RunAs; WorkingDirectory:string; LoadUserProfile:switch alias=Lup; NoNewWindow:switch alias=nnw; PassThru:switch; RedirectStandardError:string alias=RSE; RedirectStandardInput:string alias=RSI; RedirectStandardOutput:string alias=RSO; Verb:string; WindowStyle:string set=Normal|Hidden|Minimized|Maximized; Wait:switch; UseNewEnvironment:switch", (c, o) =>
            {
                string file = c.Str("FilePath").Trim().Trim('"');
                string args = c.Has("ArgumentList") ? string.Join(" ", c.Strs("ArgumentList")) : "";
                string user = c.R.User;
                var cred = c.Get("Credential") as PsObj;
                if (cred != null) { user = PsConv.ToStr(cred.Get("UserName")); int bs = user.LastIndexOf('\\'); if (bs >= 0) user = user.Substring(bs + 1); }
                string bare = file.Substring(file.LastIndexOf('\\') + 1);
                string lower = bare.ToLowerInvariant();
                if (lower.EndsWith(".exe", StringComparison.Ordinal)) lower = lower.Substring(0, lower.Length - 4);
                Func<string, PsRuntimeException> notFound = f => c.Fail("This command cannot be run due to the error: The system cannot find the file specified.", "InvalidOperationException", "InvalidOperation", null, "System.InvalidOperationException");
                if (file.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || file.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                    throw c.Fail("This command cannot be run due to the error: The practice machines have no web browser.", "InvalidOperationException", "InvalidOperation", null, "System.InvalidOperationException");
                string fullDoc = file.Contains("\\") || file.Contains(".") && !lower.Contains(".") ? c.R.FullFsPath(file, c.R.CurrentFsDir) : null;
                WinProcess proc = null;
                string displayUser = user == "SYSTEM" ? "NT AUTHORITY\\SYSTEM" : c.M.DomainShort + "\\" + user;
                int session = c.M.IsServer ? 2 : 1;
                if (lower == "powershell" || lower == "pwsh" || lower == "cmd")
                {
                    if (lower == "pwsh") throw notFound(file);
                    proc = c.M.NewProcess(lower, lower == "cmd" ? "C:\\Windows\\system32\\cmd.exe" : "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", session, displayUser, c.R.Pid);
                    proc.CommandLine = (lower == "cmd" ? "\"C:\\Windows\\system32\\cmd.exe\"" : "\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"") + (args.Length > 0 ? " " + args : "");
                    proc.Description = lower == "cmd" ? "Windows Command Processor" : "Windows PowerShell"; proc.Ws = 70000000; proc.Pm = 60000000; proc.Handles = 600;
                    if (lower == "powershell" && args.Trim().Length > 0)
                    {
                        string verb = c.Str("Verb");
                        int code = c.M.RunPowerShellCommandLine(args, user, c.Str("WorkingDirectory"), !c.Sw("NoNewWindow"), c.Sw("NoNewWindow") ? c.R.Host : null);
                        bool noExit = args.IndexOf("-NoExit", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!noExit) c.M.EndProcess(proc, code);
                    }
                    else if (lower == "cmd" && args.TrimStart().StartsWith("/c", StringComparison.OrdinalIgnoreCase)) c.M.EndProcess(proc, 0);
                }
                else if (lower == "notepad" || lower == "calc" || lower == "mspaint" || lower == "explorer" || lower == "write" || lower == "taskmgr" || lower == "mmc" || lower == "regedit" || lower == "eventvwr" || lower == "services.msc" || lower == "control" || lower == "msinfo32")
                {
                    string exe = lower == "services.msc" ? "mmc" : lower;
                    string path = exe == "notepad" || exe == "regedit" || exe == "explorer" ? "C:\\Windows\\" + (exe == "notepad" ? "system32\\" : "") + exe + ".exe" : "C:\\Windows\\system32\\" + exe + ".exe";
                    if (exe == "calc") { exe = "CalculatorApp"; path = "C:\\Program Files\\WindowsApps\\Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\\CalculatorApp.exe"; }
                    proc = c.M.NewProcess(exe, path, session, displayUser, c.R.Pid);
                    proc.Description = Describe2(exe); proc.Ws = 15900000 + proc.Id % 7 * 4096; proc.Pm = 3100000 + proc.Id % 5 * 4096; proc.Npm = 12000; proc.Handles = 230 + proc.Id % 40; proc.Cpu = 0.08; proc.Threads = 7;
                    proc.CommandLine = "\"" + path + "\"" + (args.Length > 0 ? " " + args : "");
                    proc.MainWindowTitle = exe == "notepad" ? (args.Length > 0 ? WinPath.GetFileName(args.Trim('"')) + " - Notepad" : "Untitled - Notepad") : exe == "CalculatorApp" ? "Calculator" : Describe2(exe);
                    if (c.Sw("Wait")) c.Warn("Cert Lab: " + bare + " stays open until someone closes its window, so on the practice machine Start-Process -Wait does not wait for it. Close it with Stop-Process.");
                }
                else if (fullDoc != null && (lower.EndsWith(".ps1", StringComparison.Ordinal) || lower.EndsWith(".txt", StringComparison.Ordinal) || lower.EndsWith(".log", StringComparison.Ordinal) || lower.EndsWith(".csv", StringComparison.Ordinal) || lower.EndsWith(".ini", StringComparison.Ordinal)))
                {
                    var node = c.R.FsNode(fullDoc);
                    if (node == null) throw notFound(file);
                    proc = c.M.NewProcess("notepad", "C:\\Windows\\system32\\notepad.exe", session, displayUser, c.R.Pid);
                    proc.Description = "Notepad"; proc.Ws = 15900000; proc.Pm = 3100000; proc.Handles = 232; proc.MainWindowTitle = node.Name + " - Notepad";
                    proc.CommandLine = "\"C:\\Windows\\system32\\NOTEPAD.EXE\" " + fullDoc;
                }
                else
                {
                    var nat = PsNatives.Get(lower);
                    if (nat == null) throw notFound(file);
                    proc = c.M.NewProcess(lower, nat.Path, session, displayUser, c.R.Pid);
                    var call = new NativeCall { R = c.R, Scope = c.Scope, St = c.St };
                    call.Args.AddRange(PsMachine.SplitArgs(args));
                    int code = nat.Run(call);
                    if (c.Sw("NoNewWindow")) foreach (var l in call.Out) c.R.HostWrite(l + "\n");
                    if (c.Has("RedirectStandardOutput")) c.R.WriteFile(c.R.FullFsPath(c.Str("RedirectStandardOutput"), c.R.CurrentFsDir), string.Join("\r\n", call.Out.ToArray()) + "\r\n", "ascii", false);
                    c.M.EndProcess(proc, code);
                }
                if (c.Sw("PassThru") && proc != null) c.Out(ProcObj(c.M, proc, ".", false));
            });
            st.ShouldProcess = false;

            Add("Wait-Process", Mgmt, "Name:string[]@0 ! prop alias=ProcessName ps=Name; Id:int[] ! prop alias=PID,ProcessId ps=Id; InputObject:Process[] ! pipe ps=InputObject; Timeout:int alias=TimeoutSec", (c, o) =>
            {
                var l = PickProcesses(c, c.M, true).Where(p => !p.Exited).ToList();
                if (l.Count == 0) return;
                if (c.Has("Timeout"))
                {
                    c.M.Net.Advance(c.Int("Timeout", 0));
                    foreach (var p in l.Where(x => !x.Exited)) c.Error("This command stopped operation because process \"" + ProcLabel(p) + "\" is not stopped in the specified time-out.", "ProcessNotTerminated", "CloseError", ProcObj(c.M, p, ".", false), "System.TimeoutException");
                    return;
                }
                c.Warn("Cert Lab: Wait-Process would wait until " + string.Join(", ", l.Select(p => ProcLabel(p)).ToArray()) + " ends, and nothing on the practice machine will end it. Use -Timeout, or stop it with Stop-Process.");
            }).DefaultSet = "Name";
        }
        static string Describe2(string n)
        {
            switch (n) { case "notepad": return "Notepad"; case "CalculatorApp": return "Calculator"; case "mspaint": return "Paint"; case "explorer": return "Windows Explorer"; case "taskmgr": return "Task Manager"; case "mmc": return "Microsoft Management Console"; case "regedit": return "Registry Editor"; case "eventvwr": return "Event Viewer Snapin Launcher"; case "msinfo32": return "System Information"; }
            return n;
        }
    }
}
