// Cert Lab - Windows administration cmdlets, part 3: scheduled tasks (the ScheduledTasks module): Get-ScheduledTask,
// Get-ScheduledTaskInfo, New-ScheduledTaskAction / -Trigger / -Principal / -SettingsSet, New-ScheduledTask,
// Register-, Set-, Unregister-, Start-, Stop-, Enable-, Disable-ScheduledTask. Tasks run on the simulated clock
// (PsMachine.OnTimers) and keep LastRunTime, LastTaskResult and NextRunTime. Also the CIM object helper.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public partial class PsCmdlets
    {
        // ------------------------------------------------------------ CIM instances
        /// <summary>A CIM instance: typenames CimInstance#namespace/Class (and its parents), PSComputerName, CimClass.</summary>
        public static PsObj CimObj(string ns, string cls, string[] parents, string computer)
        {
            var o = new PsObj();
            string pre = "Microsoft.Management.Infrastructure.CimInstance#";
            o.TypeNames.Add(pre + ns + "/" + cls);
            foreach (var p in parents) o.TypeNames.Add(pre + ns + "/" + p);
            o.TypeNames.Add(pre + cls);
            foreach (var p in parents) o.TypeNames.Add(pre + p);
            o.TypeNames.Add("Microsoft.Management.Infrastructure.CimInstance");
            o.TypeNames.Add("System.Object");
            if (computer != null) o.Computer = computer;
            o.ToStr = x => cls;
            return o;
        }
        /// <summary>Adds PSComputerName and the CimClass / CimInstanceProperties / CimSystemProperties members at the end.</summary>
        public static PsObj CimEnd(PsObj o, string ns, string cls, string computer)
        {
            o.Val("PSComputerName", "string", computer);
            o.Val("CimClass", "Microsoft.Management.Infrastructure.CimClass", ns + ":" + cls);
            var names = o.Props.Where(p => p.Name != "PSComputerName" && p.Name != "CimClass").Select(p => (object)p.Name).ToArray();
            o.Val("CimInstanceProperties", "Microsoft.Management.Infrastructure.Internal.Data.CimPropertiesCollection", names);
            var sys = new PsObj("Microsoft.Management.Infrastructure.CimSystemProperties", "System.Object");
            sys.Val("Namespace", "string", ns); sys.Val("ServerName", "string", computer ?? ""); sys.Val("ClassName", "string", cls); sys.Val("Path", "string", null);
            sys.ToStr = x => "Microsoft.Management.Infrastructure.CimSystemProperties";
            o.Val("CimSystemProperties", "Microsoft.Management.Infrastructure.CimSystemProperties", sys);
            if (o.DefaultDisplay == null) o.DefaultDisplay = o.Props.Where(p => p.Name != "CimClass" && p.Name != "CimInstanceProperties" && p.Name != "CimSystemProperties").Select(p => p.Name).ToArray();
            return o;
        }
        const string TaskNs = "Root/Microsoft/Windows/TaskScheduler";

        public static string IsoDuration(TimeSpan? t)
        {
            if (!t.HasValue) return null;
            var v = t.Value;
            if (v == TimeSpan.Zero) return "PT0S";
            var sb = new StringBuilder("P");
            if (v.Days > 0) sb.Append(v.Days).Append('D');
            if (v.Hours > 0 || v.Minutes > 0 || v.Seconds > 0)
            {
                sb.Append('T');
                if (v.Hours > 0) sb.Append(v.Hours).Append('H');
                if (v.Minutes > 0) sb.Append(v.Minutes).Append('M');
                if (v.Seconds > 0) sb.Append(v.Seconds).Append('S');
            }
            return sb.ToString();
        }
        public static TimeSpan? ParseIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Regex.Match(s.Trim(), @"^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.IgnoreCase);
            if (!m.Success) { TimeSpan t; if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out t)) return t; return null; }
            Func<int, int> g = i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return new TimeSpan(g(1), g(2), g(3), g(4));
        }
        static string Boundary(DateTime t) { return t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture); }

        static PsObj RepetitionObj(TimeSpan? iv, TimeSpan? dur, string computer)
        {
            var r = CimObj(TaskNs, "MSFT_TaskRepetitionPattern", new string[0], computer);
            r.Val("Duration", "string", iv.HasValue ? (dur.HasValue && dur.Value > TimeSpan.Zero ? IsoDuration(dur) : null) : null);
            r.Val("Interval", "string", IsoDuration(iv));
            r.Val("StopAtDurationEnd", "bool", false);
            return CimEnd(r, TaskNs, "MSFT_TaskRepetitionPattern", computer);
        }
        static readonly string[] dayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        static string TriggerClass(string kind)
        {
            switch (kind) { case "Daily": return "MSFT_TaskDailyTrigger"; case "Weekly": return "MSFT_TaskWeeklyTrigger"; case "AtStartup": return "MSFT_TaskBootTrigger"; case "AtLogOn": return "MSFT_TaskLogonTrigger"; }
            return "MSFT_TaskTimeTrigger";
        }
        public static PsObj TriggerObj(WinTrigger t, string computer)
        {
            string cls = TriggerClass(t.Kind);
            var o = CimObj(TaskNs, cls, new[] { "MSFT_TaskTrigger" }, computer);
            o.Base = t;
            o.Val("Enabled", "bool", t.Enabled);
            o.Val("EndBoundary", "string", null);
            o.Val("ExecutionTimeLimit", "string", null);
            o.Val("Id", "string", t.Id);
            o.Val("Repetition", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskRepetitionPattern", RepetitionObj(t.RepInterval, t.RepDuration, computer));
            o.Val("StartBoundary", "string", t.Kind == "AtStartup" || t.Kind == "AtLogOn" ? null : Boundary(t.Start));
            if (t.Kind == "Daily") { o.Val("DaysInterval", "int16", (short)t.DaysInterval); o.Val("RandomDelay", "string", null); }
            if (t.Kind == "Weekly") { int mask = 0; foreach (var d in t.Days) mask |= 1 << (int)d; o.Val("DaysOfWeek", "uint16", (ushort)mask); o.Val("RandomDelay", "string", null); o.Val("WeeksInterval", "uint16", (ushort)t.WeeksInterval); }
            if (t.Kind == "Once") o.Val("RandomDelay", "string", null);
            if (t.Kind == "AtStartup") o.Val("Delay", "string", t.Delay);
            if (t.Kind == "AtLogOn") { o.Val("Delay", "string", t.Delay); o.Val("UserId", "string", t.User); }
            return CimEnd(o, TaskNs, cls, computer);
        }
        /// <summary>Reads a trigger back from its CIM object (the person may have changed StartBoundary, Repetition...).</summary>
        static WinTrigger TriggerFrom(PsObj o)
        {
            var b = o.Base as WinTrigger;
            var t = new WinTrigger();
            string tn = o.TypeName;
            t.Kind = tn.EndsWith("MSFT_TaskDailyTrigger", StringComparison.Ordinal) ? "Daily" : tn.EndsWith("MSFT_TaskWeeklyTrigger", StringComparison.Ordinal) ? "Weekly" : tn.EndsWith("MSFT_TaskBootTrigger", StringComparison.Ordinal) ? "AtStartup" : tn.EndsWith("MSFT_TaskLogonTrigger", StringComparison.Ordinal) ? "AtLogOn" : "Once";
            DateTime d;
            var sbv = PsConv.ToStr(o.Get("StartBoundary"));
            if (sbv.Length > 0 && DateTime.TryParse(sbv, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) t.Start = d; else if (b != null) t.Start = b.Start;
            if (o.Has("Enabled")) t.Enabled = PsConv.ToBool(o.Get("Enabled"));
            if (o.Has("DaysInterval")) t.DaysInterval = Math.Max(1, PsConv.ToInt(o.Get("DaysInterval")));
            if (o.Has("WeeksInterval")) t.WeeksInterval = Math.Max(1, PsConv.ToInt(o.Get("WeeksInterval")));
            if (o.Has("DaysOfWeek")) { int mask = PsConv.ToInt(o.Get("DaysOfWeek")); for (int i = 0; i < 7; i++) if ((mask & (1 << i)) != 0) t.Days.Add((DayOfWeek)i); }
            if (o.Has("UserId")) t.User = PsConv.ToStr(o.Get("UserId"));
            if (o.Has("Delay")) t.Delay = o.Get("Delay") == null ? null : PsConv.ToStr(o.Get("Delay"));
            var rep = o.Get("Repetition") as PsObj;
            if (rep != null) { t.RepInterval = ParseIso(PsConv.ToStr(rep.Get("Interval"))); t.RepDuration = ParseIso(PsConv.ToStr(rep.Get("Duration"))); }
            return t;
        }
        static PsObj ActionObj(WinTaskAction a, string computer)
        {
            var o = CimObj(TaskNs, "MSFT_TaskExecAction", new[] { "MSFT_TaskAction" }, computer);
            o.Base = a;
            o.Val("Id", "string", a.Id);
            o.Val("Arguments", "string", a.Arguments);
            o.Val("Execute", "string", a.Execute);
            o.Val("WorkingDirectory", "string", a.WorkingDirectory);
            return CimEnd(o, TaskNs, "MSFT_TaskExecAction", computer);
        }
        static WinTaskAction ActionFrom(PsObj o)
        {
            Func<string, string> s = n => { var v = o.Get(n); return v == null ? null : PsConv.ToStr(v); };
            return new WinTaskAction { Execute = s("Execute") ?? "", Arguments = s("Arguments"), WorkingDirectory = s("WorkingDirectory"), Id = s("Id") };
        }
        static PsObj PrincipalObj(string user, string group, string logon, string level, string computer)
        {
            var o = CimObj(TaskNs, "MSFT_TaskPrincipal2", new[] { "MSFT_TaskPrincipal" }, computer);
            o.Val("DisplayName", "string", null);
            o.Val("GroupId", "string", group);
            o.Val("Id", "string", "Author");
            o.Val("LogonType", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.LogonTypeEnum", En("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.LogonTypeEnum", logon));
            o.Val("RunLevel", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.RunLevelEnum", En("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.RunLevelEnum", level));
            o.Val("UserId", "string", user);
            o.Val("ProcessTokenSidType", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.ProcessTokenSidTypeEnum", "Default");
            o.Val("RequiredPrivilege", "string[]", null);
            return CimEnd(o, TaskNs, "MSFT_TaskPrincipal2", computer);
        }
        static PsObj SettingsObj(WinTask t, string computer)
        {
            var o = CimObj(TaskNs, "MSFT_TaskSettings3", new[] { "MSFT_TaskSettings2", "MSFT_TaskSettings" }, computer);
            o.Val("AllowDemandStart", "bool", true);
            o.Val("AllowHardTerminate", "bool", true);
            o.Val("Compatibility", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.CompatibilityEnum", En("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.CompatibilityEnum", "Win8"));
            o.Val("DeleteExpiredTaskAfter", "string", null);
            o.Val("DisallowStartIfOnBatteries", "bool", !t.AllowStartIfOnBatteries);
            o.Val("Enabled", "bool", t.State != "Disabled");
            o.Val("ExecutionTimeLimit", "string", t.ExecutionTimeLimit);
            o.Val("Hidden", "bool", t.Hidden);
            var idle = CimObj(TaskNs, "MSFT_TaskIdleSettings", new string[0], computer);
            idle.Val("IdleDuration", "string", "PT10M"); idle.Val("RestartOnIdle", "bool", false); idle.Val("StopOnIdleEnd", "bool", true); idle.Val("WaitTimeout", "string", "PT1H");
            o.Val("IdleSettings", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskIdleSettings", CimEnd(idle, TaskNs, "MSFT_TaskIdleSettings", computer));
            o.Val("MultipleInstances", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.MultipleInstancesEnum", En("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.MultipleInstancesEnum", t.MultipleInstances));
            o.Val("NetworkSettings", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskNetworkSettings", "MSFT_TaskNetworkSettings");
            o.Val("Priority", "int", t.Priority);
            o.Val("RestartCount", "int", t.RestartCount);
            o.Val("RestartInterval", "string", null);
            o.Val("RunOnlyIfIdle", "bool", false);
            o.Val("RunOnlyIfNetworkAvailable", "bool", false);
            o.Val("StartWhenAvailable", "bool", t.StartWhenAvailable);
            o.Val("StopIfGoingOnBatteries", "bool", !t.AllowStartIfOnBatteries);
            o.Val("WakeToRun", "bool", false);
            o.Val("DisallowStartOnRemoteAppSession", "bool", false);
            o.Val("UseUnifiedSchedulingEngine", "bool", true);
            o.Val("MaintenanceSettings", "Microsoft.Management.Infrastructure.CimInstance#MSFT_TaskMaintenanceSettings", null);
            o.Val("volatile", "bool", false);
            return CimEnd(o, TaskNs, "MSFT_TaskSettings3", computer);
        }
        static void SettingsInto(PsObj s, WinTask t)
        {
            if (s == null) return;
            if (s.Has("Enabled") && !PsConv.ToBool(s.Get("Enabled"))) t.State = "Disabled";
            if (s.Has("Hidden")) t.Hidden = PsConv.ToBool(s.Get("Hidden"));
            if (s.Has("ExecutionTimeLimit") && s.Get("ExecutionTimeLimit") != null) t.ExecutionTimeLimit = PsConv.ToStr(s.Get("ExecutionTimeLimit"));
            if (s.Has("MultipleInstances")) t.MultipleInstances = PsConv.ToStr(s.Get("MultipleInstances"));
            if (s.Has("Priority")) t.Priority = PsConv.ToInt(s.Get("Priority"));
            if (s.Has("RestartCount")) t.RestartCount = PsConv.ToInt(s.Get("RestartCount"));
            if (s.Has("StartWhenAvailable")) t.StartWhenAvailable = PsConv.ToBool(s.Get("StartWhenAvailable"));
            if (s.Has("DisallowStartIfOnBatteries")) t.AllowStartIfOnBatteries = !PsConv.ToBool(s.Get("DisallowStartIfOnBatteries"));
        }

        public static PsObj TaskObj(PsMachine m, WinTask t, string computer)
        {
            var o = CimObj(TaskNs, "MSFT_ScheduledTask", new string[0], computer);
            o.Base = t;
            o.Prop("State", "Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.StateEnum", () => En("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.StateEnum", t.State));
            o.Val("Actions", "CimInstance#InstanceArray", t.Actions.Select(a => (object)ActionObj(a, computer)).ToArray());
            o.Val("Author", "string", t.Author);
            o.Val("Date", "string", t.Date == DateTime.MinValue ? null : Boundary(t.Date));
            o.Val("Description", "string", t.Description);
            o.Val("Documentation", "string", null);
            o.Val("Principal", "CimInstance#Instance", PrincipalObj(t.UserId, t.GroupId, t.LogonType, t.RunLevel, computer));
            o.Val("SecurityDescriptor", "string", null);
            o.Val("Settings", "CimInstance#Instance", SettingsObj(t, computer));
            o.Val("Source", "string", t.Builtin ? "Microsoft Corporation" : null);
            o.Val("TaskName", "string", t.Name);
            o.Val("TaskPath", "string", t.Path);
            o.Val("Triggers", "CimInstance#InstanceArray", t.Triggers.Select(x => (object)TriggerObj(x, computer)).ToArray());
            o.Val("URI", "string", t.Name.Length == 0 ? null : t.FullName);
            o.Val("Version", "string", t.Builtin ? "1.0" : null);
            o.ToStr = x => "MSFT_ScheduledTask (TaskName = \"" + t.Name + "\", TaskPath = \"" + t.Path + "\")";
            return CimEnd(o, TaskNs, "MSFT_ScheduledTask", computer);
        }
        static PsObj TaskInfoObj(PsMachine m, WinTask t, string computer)
        {
            var o = CimObj(TaskNs, "MSFT_TaskDynamicInfo", new string[0], computer);
            o.Base = t;
            o.Val("LastRunTime", "datetime", t.LastRun.HasValue ? (object)t.LastRun.Value : (t.Builtin ? null : (object)new DateTime(1999, 11, 30)));
            o.Val("LastTaskResult", "uint32", (long)(uint)t.LastResult);
            o.Val("NextRunTime", "datetime", t.NextRun);
            o.Val("NumberOfMissedRuns", "uint32", (long)t.MissedRuns);
            o.Val("TaskName", "string", t.Name);
            o.Val("TaskPath", "string", t.Path);
            CimEnd(o, TaskNs, "MSFT_TaskDynamicInfo", computer);
            o.DefaultDisplay = new[] { "LastRunTime", "LastTaskResult", "NextRunTime", "NumberOfMissedRuns", "TaskName", "TaskPath", "PSComputerName" };
            return o;
        }

        /// <summary>The -At value of a trigger: a time with no date means today on the pretend machine's clock.</summary>
        static DateTime AtValue(CmdCtx c, object v)
        {
            v = PsConv.Base(v);
            if (v is DateTime) return (DateTime)v;
            string s = PsConv.ToStr(v).Trim();
            DateTime d;
            if (DateTime.TryParse(s, PsConv.Us, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out d))
                return d.Date == DateTime.MinValue.Date ? c.M.Now.Date.Add(d.TimeOfDay) : d;
            throw c.Fail("Cannot bind parameter 'At'. Cannot convert value \"" + s + "\" to type \"System.DateTime\". Error: \"String was not recognized as a valid DateTime.\"", "CannotConvertArgumentNoMessage,Microsoft.Management.Infrastructure.CimCmdlets.NewScheduledTaskTrigger", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
        }

        static List<WinTask> PickTasks(CmdCtx c, PsMachine m, bool errors, bool wildcards)
        {
            var res = new List<WinTask>();
            if (c.Has("InputObject"))
            {
                foreach (var x in c.Objs("InputObject")) { var po = PsConv.Base(x) as PsObj; var t = po == null ? null : po.Base as WinTask; if (t != null && m.Tasks.Contains(t)) res.Add(t); else if (po != null && po.Get("TaskName") != null) { var t2 = m.FindTask(PsConv.ToStr(po.Get("TaskPath")), PsConv.ToStr(po.Get("TaskName"))); if (t2 != null) res.Add(t2); } }
                return res;
            }
            var names = c.Strs("TaskName") ?? new[] { "*" };
            var paths = c.Strs("TaskPath");
            foreach (var n in names)
            {
                var found = m.Tasks.Where(t => (wildcards ? PsConv.Like(t.Name, n, false) : t.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && (paths == null || paths.Any(p => wildcards ? PsConv.Like(t.Path, p, false) || PsConv.Like(t.Path, PsMachine.NormTaskPath(p), false) : t.Path.Equals(PsMachine.NormTaskPath(p), StringComparison.OrdinalIgnoreCase)))).ToList();
                if (found.Count == 0 && (!PsConv.HasWildcard(n) || !wildcards))
                {
                    if (errors)
                    {
                        string prop = paths != null && c.Has("TaskPath") && !c.Has("TaskName") ? "TaskPath" : "TaskName";
                        var r = c.MakeError("No MSFT_ScheduledTask objects found with property '" + prop + "' equal to '" + (prop == "TaskPath" ? paths[0] : n) + "'.  Verify the value of the property and retry.", "CmdletizationQuery_NotFound_" + prop, "ObjectNotFound", prop == "TaskPath" ? paths[0] : n, "Microsoft.PowerShell.Cmdletization.Cim.CimJobException");
                        c.WriteError(r);
                    }
                    continue;
                }
                foreach (var t in found.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)) if (!res.Contains(t)) res.Add(t);
            }
            return res;
        }
        static string CurrentUserName(CmdCtx c) { return c.R.User == "SYSTEM" ? "NT AUTHORITY\\SYSTEM" : c.R.User.Contains("\\") ? c.R.User : c.M.DomainShort + "\\" + c.R.User; }

        /// <summary>Who the task runs as (-User / -Password / -RunLevel or -Principal). Returns an error text or null.</summary>
        static string ApplyPrincipal(CmdCtx c, WinTask t)
        {
            var p = c.Get("Principal") as PsObj;
            if (p != null)
            {
                t.UserId = p.Get("UserId") == null ? null : PsConv.ToStr(p.Get("UserId"));
                t.GroupId = p.Get("GroupId") == null ? null : PsConv.ToStr(p.Get("GroupId"));
                t.LogonType = PsConv.ToStr(p.Get("LogonType") ?? "Interactive");
                t.RunLevel = PsConv.ToStr(p.Get("RunLevel") ?? "Limited");
                if (t.UserId != null && IsSystemUser(t.UserId)) { t.UserId = "SYSTEM"; t.LogonType = "ServiceAccount"; }
                return null;
            }
            if (c.Has("User"))
            {
                string u = c.Str("User");
                if (IsSystemUser(u)) { t.UserId = "SYSTEM"; t.LogonType = "ServiceAccount"; }
                else if (u.Equals("NT AUTHORITY\\LOCALSERVICE", StringComparison.OrdinalIgnoreCase) || u.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) || u.Equals("NT AUTHORITY\\LOCAL SERVICE", StringComparison.OrdinalIgnoreCase)) { t.UserId = "LOCAL SERVICE"; t.LogonType = "ServiceAccount"; }
                else
                {
                    t.UserId = u;
                    if (c.Has("Password"))
                    {
                        string pw = c.Str("Password");
                        var lu = c.M.FindUser(u);
                        if (lu != null && (!lu.Enabled || lu.Password != pw)) return "The user name or password is incorrect.";
                        if (lu == null && !u.Contains("\\") && !u.Contains("@")) return "No mapping between account names and security IDs was done.";
                        t.Password = pw; t.LogonType = "Password";
                    }
                    else t.LogonType = "Interactive";
                }
            }
            if (c.Has("RunLevel")) t.RunLevel = c.Str("RunLevel");
            return null;
        }
        static bool IsSystemUser(string u) { return u.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || u.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase) || u.Equals("S-1-5-18") || u.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase); }

        static PsErrorRecord CimError(CmdCtx c, string msg, string hresult, string cat)
        {
            var r = c.MakeError(msg, "HRESULT " + hresult, cat, null, "Microsoft.Management.Infrastructure.CimException");
            r.TargetName = "PS_ScheduledTask"; r.TargetType = "Root/Microsoft/...S_ScheduledTask";
            return r;
        }

        static void RegTasks()
        {
            Func<string, string, Action<CmdCtx, object>, CmdletDef> add = (name, spec, f) => { var d = Add(name, Sched, spec, f); d.FqClass = name; return d; };
            add("Get-ScheduledTask", "TaskName:string[]@0; TaskPath:string[]@1; CimSession:CimSession[] alias=Session; ThrottleLimit:int; AsJob:switch", (c, o) =>
            {
                var m = CimTarget(c);
                foreach (var t in PickTasks(c, m, true, true)) c.Out(TaskObj(m, t, m == c.M ? null : m.Name));
            });
            add("Get-ScheduledTaskInfo", "TaskName:string@0 ! ps=TaskName; TaskPath:string@1 ps=TaskName; InputObject:CimInstance ! pipe ps=InputObject; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                var m = CimTarget(c);
                foreach (var t in PickTasks(c, m, true, false)) c.Out(TaskInfoObj(m, t, m == c.M ? null : m.Name));
            }).DefaultSet = "TaskName";

            add("New-ScheduledTaskAction", "Execute:string@0 !; Argument:string@1; WorkingDirectory:string@2; Id:string; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                c.Out(ActionObj(new WinTaskAction { Execute = c.Str("Execute"), Arguments = c.Str("Argument"), WorkingDirectory = c.Str("WorkingDirectory"), Id = c.Str("Id") }, null));
            });
            var trig = add("New-ScheduledTaskTrigger", "Once:switch ! ps=Once; Daily:switch ! ps=Daily; Weekly:switch ! ps=Weekly; AtStartup:switch ! ps=AtStartup; AtLogOn:switch ! ps=AtLogon; At:object ! ps=Once,Daily,Weekly; DaysInterval:uint32 ps=Daily; WeeksInterval:uint32 ps=Weekly; DaysOfWeek:string[] ! ps=Weekly; RepetitionInterval:timespan ps=Once; RepetitionDuration:timespan ps=Once; RandomDelay:timespan; User:string ps=AtLogon; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                var t = new WinTrigger();
                if (c.Sw("Daily")) t.Kind = "Daily"; else if (c.Sw("Weekly")) t.Kind = "Weekly"; else if (c.Sw("AtStartup")) t.Kind = "AtStartup"; else if (c.Sw("AtLogOn")) t.Kind = "AtLogOn";
                else if (c.Sw("Once")) t.Kind = "Once";
                else throw c.Fail("Parameter set cannot be resolved using the specified named parameters.", "AmbiguousParameterSet,New-ScheduledTaskTrigger", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                if (c.Has("At")) t.Start = AtValue(c, c.Get("At"));
                if (c.Has("DaysInterval")) t.DaysInterval = Math.Max(1, c.Int("DaysInterval", 1));
                if (c.Has("WeeksInterval")) t.WeeksInterval = Math.Max(1, c.Int("WeeksInterval", 1));
                if (c.Has("DaysOfWeek"))
                    foreach (var d in c.Strs("DaysOfWeek"))
                    {
                        var dn = dayNames.FirstOrDefault(x => x.Equals(d, StringComparison.OrdinalIgnoreCase));
                        if (dn == null) throw c.Fail("Cannot bind parameter 'DaysOfWeek'. Cannot convert value \"" + d + "\" to type \"System.DayOfWeek\". Error: \"Unable to match the identifier name " + d + " to a valid enumerator name. Specify one of the following enumerator names and try again:\nSunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday\"", "CannotConvertArgumentNoMessage,New-ScheduledTaskTrigger", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                        t.Days.Add((DayOfWeek)Array.IndexOf(dayNames, dn));
                    }
                if (c.Has("RepetitionInterval")) t.RepInterval = (TimeSpan)c.Get("RepetitionInterval");
                if (c.Has("RepetitionDuration"))
                {
                    var dur = (TimeSpan)c.Get("RepetitionDuration");
                    if (dur == TimeSpan.MaxValue || dur.TotalDays > 9999) throw c.Fail("The task XML contains a value which is incorrectly formatted or out of range.", "HRESULT 0x80041318,New-ScheduledTaskTrigger", "InvalidArgument", null, "Microsoft.Management.Infrastructure.CimException");
                    t.RepDuration = dur;
                    if (!t.RepInterval.HasValue) throw c.Fail("The RepetitionInterval parameter value must be specified when the RepetitionDuration parameter is used.", "RepetitionIntervalMissing,New-ScheduledTaskTrigger", "InvalidArgument", null, "System.ArgumentException");
                }
                if (t.RepInterval.HasValue && t.RepDuration.HasValue && t.RepInterval.Value > t.RepDuration.Value) throw c.Fail("The repetition interval must be shorter than the repetition duration.", "HRESULT 0x80041318,New-ScheduledTaskTrigger", "InvalidArgument", null, "Microsoft.Management.Infrastructure.CimException");
                if (c.Has("User")) t.User = c.Str("User");
                if (c.Has("RandomDelay")) t.Delay = IsoDuration((TimeSpan)c.Get("RandomDelay"));
                c.Out(TriggerObj(t, null));
            });
            trig.DefaultSet = "Once";
            var pr = add("New-ScheduledTaskPrincipal", "UserId:string@0 ! ps=User; GroupId:string ! ps=Group; LogonType:string set=None|Password|S4U|Interactive|Group|ServiceAccount|InteractiveOrPassword ps=User; RunLevel:string set=Limited|Highest; Id:string; ProcessTokenSidType:string; RequiredPrivilege:string[]; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                string user = c.Str("UserId");
                string logon = c.Str("LogonType") ?? (c.Has("GroupId") ? "Group" : (user != null && IsSystemUser(user) ? "ServiceAccount" : "Interactive"));
                c.Out(PrincipalObj(user, c.Str("GroupId"), logon, c.Str("RunLevel") ?? "Limited", null));
            });
            pr.DefaultSet = "User";
            add("New-ScheduledTaskSettingsSet", "AllowStartIfOnBatteries:switch; DontStopIfGoingOnBatteries:switch; StartWhenAvailable:switch; Hidden:switch; Disable:switch; ExecutionTimeLimit:timespan; RestartCount:int; RestartInterval:timespan; MultipleInstances:string set=Parallel|Queue|IgnoreNew|StopExisting; Priority:int range=0~10; RunOnlyIfNetworkAvailable:switch; WakeToRun:switch; RunOnlyIfIdle:switch; DontStopOnIdleEnd:switch; Compatibility:string; DeleteExpiredTaskAfter:timespan; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                var t = new WinTask { AllowStartIfOnBatteries = c.Sw("AllowStartIfOnBatteries"), StartWhenAvailable = c.Sw("StartWhenAvailable"), Hidden = c.Sw("Hidden") };
                if (c.Sw("Disable")) t.State = "Disabled";
                if (c.Has("ExecutionTimeLimit")) t.ExecutionTimeLimit = IsoDuration((TimeSpan)c.Get("ExecutionTimeLimit"));
                if (c.Has("RestartCount")) t.RestartCount = c.Int("RestartCount", 0);
                if (c.Has("MultipleInstances")) t.MultipleInstances = c.Str("MultipleInstances");
                if (c.Has("Priority")) t.Priority = c.Int("Priority", 7);
                var s = SettingsObj(t, null);
                if (c.Sw("DontStopIfGoingOnBatteries")) s.Set("StopIfGoingOnBatteries", false);
                if (c.Sw("RunOnlyIfNetworkAvailable")) s.Set("RunOnlyIfNetworkAvailable", true);
                if (c.Sw("WakeToRun")) s.Set("WakeToRun", true);
                if (c.Has("RestartInterval")) s.Set("RestartInterval", IsoDuration((TimeSpan)c.Get("RestartInterval")));
                c.Out(s);
            });
            add("New-ScheduledTask", "Action:CimAction[]@0; Trigger:CimTrigger[]@1; Settings:CimSettings@2; Principal:CimPrincipal@3; Description:string@4; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                var t = new WinTask { Name = "", Path = "\\", Author = null, Date = DateTime.MinValue, Description = c.Str("Description") };
                foreach (var a in c.Objs("Action")) t.Actions.Add(ActionFrom((PsObj)a));
                foreach (var x in c.Objs("Trigger")) t.Triggers.Add(TriggerFrom((PsObj)x));
                SettingsInto(c.Get("Settings") as PsObj, t);
                var p = c.Get("Principal") as PsObj;
                if (p != null) { t.UserId = PsConv.ToStr(p.Get("UserId")); t.LogonType = PsConv.ToStr(p.Get("LogonType")); t.RunLevel = PsConv.ToStr(p.Get("RunLevel")); }
                else { t.UserId = null; t.LogonType = "Interactive"; }
                var obj = TaskObj(c.M, t, null);
                obj.Set("TaskName", null); obj.Set("TaskPath", null);
                c.Out(obj);
            });

            var reg = add("Register-ScheduledTask", "TaskName:string@0 !; TaskPath:string@1; Action:CimAction[]@2 ! ps=User,Principal; Trigger:CimTrigger[]@3 ps=User,Principal; Settings:CimSettings@4 ps=User,Principal; User:string@5 ps=User; Password:string@6 ps=User,Object; RunLevel:string set=Limited|Highest ps=User; Principal:CimPrincipal ! ps=Principal; InputObject:CimInstance ! pipe ps=Object; Xml:string ! ps=Xml; Description:string ps=User,Principal; Force:switch; CimSession:CimSession[] alias=Session; AsJob:switch", (c, o) =>
            {
                var m = CimTarget(c);
                string name = c.Str("TaskName"), path = PsMachine.NormTaskPath(c.Str("TaskPath"));
                if (name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0 && !name.StartsWith("\\", StringComparison.Ordinal)) { c.WriteError(CimError(c, "The filename, directory name, or volume label syntax is incorrect.", "0x8007007b", "InvalidArgument")); return; }
                if (name.StartsWith("\\", StringComparison.Ordinal)) { int bs = name.LastIndexOf('\\'); path = PsMachine.NormTaskPath(name.Substring(0, bs)); name = name.Substring(bs + 1); }
                var old = m.FindTask(path, name);
                if (old != null && !c.Sw("Force")) { c.WriteError(CimError(c, "Cannot create a file when that file already exists.", "0x800700b7", "ResourceExists")); return; }
                if (c.Has("Xml")) { c.WriteError(CimError(c, "Cert Lab: registering a task from XML is not simulated; use -Action and -Trigger.", "0x80041318", "NotImplemented")); return; }
                var t = new WinTask { Name = name, Path = path, Date = m.Now, Author = CurrentUserName(c), UserId = null, LastResult = 267011 };
                t.Uri = t.FullName;
                if (c.Has("InputObject"))
                {
                    var src = c.Get("InputObject") as PsObj; var st = src == null ? null : src.Base as WinTask;
                    if (src != null)
                    {
                        foreach (var a in PsConv.Items(src.Get("Actions"))) t.Actions.Add(ActionFrom((PsObj)PsConv.Base(a)));
                        foreach (var x in PsConv.Items(src.Get("Triggers"))) t.Triggers.Add(TriggerFrom((PsObj)PsConv.Base(x)));
                        SettingsInto(src.Get("Settings") as PsObj, t);
                        t.Description = src.Get("Description") == null ? null : PsConv.ToStr(src.Get("Description"));
                        var p = src.Get("Principal") as PsObj;
                        if (p != null && p.Get("UserId") != null) { t.UserId = PsConv.ToStr(p.Get("UserId")); t.LogonType = PsConv.ToStr(p.Get("LogonType")); t.RunLevel = PsConv.ToStr(p.Get("RunLevel")); if (IsSystemUser(t.UserId)) t.UserId = "SYSTEM"; }
                    }
                }
                else
                {
                    foreach (var a in c.Objs("Action")) t.Actions.Add(ActionFrom((PsObj)PsConv.Base(a)));
                    foreach (var x in c.Objs("Trigger")) t.Triggers.Add(TriggerFrom((PsObj)PsConv.Base(x)));
                    SettingsInto(c.Get("Settings") as PsObj, t);
                    t.Description = c.Str("Description");
                }
                if (t.UserId == null)
                {
                    var why = ApplyPrincipalFor(c, t);
                    if (why != null) { c.WriteError(CimError(c, why, why.StartsWith("No mapping", StringComparison.Ordinal) ? "0x80070534" : "0x8007052e", why.StartsWith("No mapping", StringComparison.Ordinal) ? "NotSpecified" : "AuthenticationError")); return; }
                }
                else if (c.Has("RunLevel")) t.RunLevel = c.Str("RunLevel");
                if (t.Actions.Count == 0) { c.WriteError(CimError(c, "The task XML is missing a required element or attribute.\n(48,4):Actions:", "0x80041319", "NotSpecified")); return; }
                if (!c.ShouldProcess(t.FullName, "Register-ScheduledTask")) return;
                if (old != null) m.Tasks.Remove(old);
                m.Tasks.Add(t);
                t.NextRun = m.NextTaskTime(t, m.Now);
                m.AdminChanged("task:" + t.FullName);
                c.Out(TaskObj(m, t, m == c.M ? null : m.Name));
            });
            reg.DefaultSet = "User";

            var set = add("Set-ScheduledTask", "TaskName:string@0 ! ps=User,Principal; TaskPath:string@1 ps=User,Principal; Action:CimAction[] ps=User,Principal; Trigger:CimTrigger[] ps=User,Principal; Settings:CimSettings ps=User,Principal; User:string ps=User; Password:string ps=User,Object; Principal:CimPrincipal ps=Principal; InputObject:CimInstance ! pipe ps=Object; CimSession:CimSession[] alias=Session", (c, o) =>
            {
                var m = CimTarget(c);
                WinTask t = null; PsObj src = null;
                if (c.Has("InputObject")) { src = c.Get("InputObject") as PsObj; var bt = src == null ? null : src.Base as WinTask; t = bt != null && m.Tasks.Contains(bt) ? bt : (src == null ? null : m.FindTask(PsConv.ToStr(src.Get("TaskPath")), PsConv.ToStr(src.Get("TaskName")))); }
                else t = m.FindTask(c.Has("TaskPath") ? c.Str("TaskPath") : null, c.Str("TaskName"));
                if (t == null) { c.WriteError(CimError(c, "The system cannot find the file specified.", "0x80070002", "ObjectNotFound")); return; }
                if (!c.ShouldProcess(t.FullName, "Set-ScheduledTask")) return;
                if (src != null)
                {
                    // the changed object: take its actions, triggers, settings and description
                    t.Actions.Clear(); foreach (var a in PsConv.Items(src.Get("Actions"))) t.Actions.Add(ActionFrom((PsObj)PsConv.Base(a)));
                    t.Triggers.Clear(); foreach (var x in PsConv.Items(src.Get("Triggers"))) t.Triggers.Add(TriggerFrom((PsObj)PsConv.Base(x)));
                    var sset = src.Get("Settings") as PsObj; if (sset != null) { var en = PsConv.ToBool(sset.Get("Enabled")); SettingsInto(sset, t); if (en && t.State == "Disabled") t.State = "Ready"; }
                    if (src.Get("Description") != null) t.Description = PsConv.ToStr(src.Get("Description"));
                }
                if (c.Has("Action")) { t.Actions.Clear(); foreach (var a in c.Objs("Action")) t.Actions.Add(ActionFrom((PsObj)PsConv.Base(a))); }
                if (c.Has("Trigger")) { t.Triggers.Clear(); foreach (var x in c.Objs("Trigger")) t.Triggers.Add(TriggerFrom((PsObj)PsConv.Base(x))); }
                if (c.Has("Settings")) { var s = c.Get("Settings") as PsObj; bool en = PsConv.ToBool(s.Get("Enabled")); SettingsInto(s, t); if (en && t.State == "Disabled") t.State = "Ready"; }
                if (c.Has("User") || c.Has("Principal")) { var why = ApplyPrincipalFor(c, t); if (why != null) { c.WriteError(CimError(c, why, "0x8007052e", "AuthenticationError")); return; } }
                t.NextRun = m.NextTaskTime(t, m.Now);
                m.AdminChanged("task:" + t.FullName);
                c.Out(TaskObj(m, t, m == c.M ? null : m.Name));
            });
            set.DefaultSet = "User";

            Action<string, Func<CmdCtx, PsMachine, WinTask, bool>> simple = (name, f) =>
            {
                var d = add(name, "TaskName:string@0 ! ps=TaskName; TaskPath:string@1 ps=TaskName; InputObject:CimInstance ! pipe ps=InputObject; CimSession:CimSession[] alias=Session; PassThru:switch", (c, o) =>
                {
                    var m = CimTarget(c);
                    var list = PickTasks(c, m, true, false);
                    foreach (var t in list) if (f(c, m, t)) m.AdminChanged("task:" + t.FullName);
                });
                d.DefaultSet = "TaskName";
                if (name == "Unregister-ScheduledTask") { d.ShouldProcess = true; d.ConfirmImpact = "High"; }
                if (name.StartsWith("Enable", StringComparison.Ordinal) || name.StartsWith("Disable", StringComparison.Ordinal)) d.ShouldProcess = true;
            };
            simple("Start-ScheduledTask", (c, m, t) =>
            {
                if (t.State == "Disabled") { c.WriteError(CimError(c, "The task is disabled.", "0x80041326", "NotSpecified")); return false; }
                m.RunTask(t, "demand");
                return true;
            });
            simple("Stop-ScheduledTask", (c, m, t) => { if (t.State == "Running") t.State = "Ready"; return true; });
            simple("Enable-ScheduledTask", (c, m, t) =>
            {
                if (!c.ShouldProcess(t.FullName, "Enable-ScheduledTask")) return false;
                if (t.State == "Disabled") t.State = "Ready";
                t.NextRun = m.NextTaskTime(t, m.Now);
                c.Out(TaskObj(m, t, m == c.M ? null : m.Name));
                return true;
            });
            simple("Disable-ScheduledTask", (c, m, t) =>
            {
                if (!c.ShouldProcess(t.FullName, "Disable-ScheduledTask")) return false;
                t.State = "Disabled"; t.NextRun = null;
                c.Out(TaskObj(m, t, m == c.M ? null : m.Name));
                return true;
            });
            simple("Unregister-ScheduledTask", (c, m, t) =>
            {
                if (!c.ShouldProcess(t.FullName, "Delete")) return false;
                m.Tasks.Remove(t);
                if (c.Sw("PassThru")) c.Out(TaskObj(m, t, null));
                return true;
            });
        }
        static string ApplyPrincipalFor(CmdCtx c, WinTask t)
        {
            if (!c.Has("User") && !c.Has("Principal"))
            {
                string me = CurrentUserName(c);
                t.UserId = c.R.User == "SYSTEM" ? "SYSTEM" : me.Substring(me.IndexOf('\\') + 1);
                t.LogonType = c.R.User == "SYSTEM" ? "ServiceAccount" : "Interactive";
                if (c.Has("RunLevel")) t.RunLevel = c.Str("RunLevel");
                return null;
            }
            return ApplyPrincipal(c, t);
        }

        /// <summary>The machine a CIM command works on: -CimSession (its computer), else this one.</summary>
        static PsMachine CimTarget(CmdCtx c)
        {
            var s = c.Objs("CimSession").Select(x => PsConv.Base(x) as PsObj).FirstOrDefault(x => x != null);
            if (s == null) return c.M;
            var m = s.Base as PsMachine;
            if (m == null) throw c.Fail("The CIM session is not valid.", "Microsoft.Management.Infrastructure.CimException", "InvalidArgument", null, "Microsoft.Management.Infrastructure.CimException");
            return m;
        }
    }
}
