// Cert Lab - the Windows administration programs on the pretend machines: sc.exe (query, queryex, qc, start, stop,
// config, create, delete, description), net.exe (start, stop, user, localgroup), tasklist, taskkill, schtasks
// (/query /create /run /end /delete /change) and systeminfo. Text in, text out, like the real programs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static string Opt(List<string> a, string name)
        {
            for (int i = 0; i < a.Count - 1; i++) if (a[i].Equals(name, StringComparison.OrdinalIgnoreCase) || a[i].Equals("-" + name.TrimStart('/'), StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
        static bool Flag(List<string> a, string name) { return a.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase) || x.Equals("-" + name.TrimStart('/'), StringComparison.OrdinalIgnoreCase)); }
        static string ScState(WinService s) { return s.Status == "Running" ? "4  RUNNING" : s.Status == "Paused" ? "7  PAUSED" : "1  STOPPED"; }
        static string ScType(WinService s) { return s.ServiceType.Contains("Share") ? (s.ServiceType.Contains("Own") ? "30  WIN32" : "20  WIN32_SHARE_PROCESS") : (s.Name == "Spooler" ? "110  WIN32_OWN_PROCESS  (interactive)" : "10  WIN32_OWN_PROCESS"); }
        static void ScStatus(NativeCall c, WinService s, bool ex)
        {
            c.L("SERVICE_NAME: " + s.Name + " ");
            if (!ex && c.Args.Count > 0 && c.Args[0].Equals("query", StringComparison.OrdinalIgnoreCase) && c.Args.Count == 1) c.L("DISPLAY_NAME: " + s.DisplayName);
            c.L("        TYPE               : " + ScType(s) + "  ");
            c.L("        STATE              : " + ScState(s) + " ");
            c.L("                                (" + (s.CanStop && s.Running ? "STOPPABLE" : "NOT_STOPPABLE") + ", " + (s.CanPause && s.Running ? "PAUSABLE" : "NOT_PAUSABLE") + ", " + (s.CanShutdown && s.Running ? "ACCEPTS_SHUTDOWN" : "IGNORES_SHUTDOWN") + ")");
            c.L("        WIN32_EXIT_CODE    : " + (s.Running ? "0  (0x0)" : (s.ExitCode != 0 ? s.ExitCode + "  (0x" + s.ExitCode.ToString("x") + ")" : s.EverStarted ? "0  (0x0)" : "1077  (0x435)")));
            c.L("        SERVICE_EXIT_CODE  : 0  (0x0)");
            c.L("        CHECKPOINT         : 0x0");
            c.L("        WAIT_HINT          : 0x0");
            if (ex) { c.L("        PID                : " + s.Pid); c.L("        FLAGS              : "); }
            c.L("");
        }
        static int ScFail(NativeCall c, string op, int code, string text) { c.L("[SC] " + op + " FAILED " + code + ":"); c.L(""); c.L(text); c.L(""); return code; }

        static partial void RegisterAdminNatives()
        {
            Add("sc", null, c =>
            {
                var a = c.Args; var m = c.M;
                if (a.Count > 0 && a[0].StartsWith("\\\\", StringComparison.Ordinal)) { var rm = m.Net.Get(a[0].TrimStart('\\')); if (rm == null) return ScFail(c, "OpenSCManager", 1722, "The RPC server is unavailable."); m = rm; a = a.Skip(1).ToList(); }
                if (a.Count == 0) { c.L("DESCRIPTION:"); c.L("        SC is a command line program used for communicating with the"); c.L("        Service Control Manager and services."); c.L("USAGE:"); c.L("        sc <server> [command] [service name] <option1> <option2>..."); c.L(""); c.L("Cert Lab supports: query, queryex, qc, qdescription, start, stop, pause, continue, config, description, create, delete."); return 0; }
                string cmd = a[0].ToLowerInvariant();
                if ((cmd == "query" || cmd == "queryex") && a.Count == 1 || (cmd == "query" || cmd == "queryex") && a.Count > 1 && a[1].StartsWith("state=", StringComparison.OrdinalIgnoreCase))
                {
                    string state = a.Count > 2 ? a[2].ToLowerInvariant() : (a.Count > 1 && a[1].Length > 6 ? a[1].Substring(6).ToLowerInvariant() : "active");
                    foreach (var s in m.LiveServices.Where(x => x.ServiceType != "KernelDriver" && (state == "all" || (state == "inactive" ? !x.Running : x.Running))).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        c.L("SERVICE_NAME: " + s.Name); c.L("DISPLAY_NAME: " + s.DisplayName);
                        var keep = c.Out.Count; ScStatus(c, s, cmd == "queryex"); c.Out.RemoveAt(keep);
                    }
                    return 0;
                }
                if (a.Count < 2) return ScFail(c, cmd == "query" ? "EnumQueryServicesStatus:OpenService" : "OpenService", 1060, "The specified service does not exist as an installed service.");
                var svc = m.Svc(a[1]);
                if (cmd == "create")
                {
                    if (svc != null) return ScFail(c, "CreateService", 1073, "The specified service already exists.");
                    string bin = Opt(a, "binPath=") ?? Opt(a, "binpath=");
                    if (bin == null) { c.L("DESCRIPTION:"); c.L("        Creates a service entry in the registry and Service Database."); c.L("USAGE:"); c.L("        sc <server> create [service name] [binPath= ] <option1> <option2>..."); return 1639; }
                    var s = new WinService { Name = a[1], DisplayName = Opt(a, "DisplayName=") ?? a[1], BinaryPath = bin, Custom = true, StartType = StartFrom(Opt(a, "start=")) ?? "Manual", Account = Opt(a, "obj=") ?? "LocalSystem" };
                    s.Exe = WinPath.GetFileNameWithoutExtension(PsMachine.ExeOf(bin));
                    var dep = Opt(a, "depend="); if (dep != null) s.DependsOn.AddRange(dep.Split('/').Where(x => x.Length > 0));
                    m.Services.Add(s);
                    m.Event("System", "Service Control Manager", 7045, 4, "A service was installed in the system.\r\n\r\nService Name:  " + s.Name + "\r\nService File Name:  " + s.BinaryPath + "\r\nService Type:  user mode service\r\nService Start Type:  " + PsMachine.StartWord(s.StartType, false) + "\r\nService Account:  " + s.Account, m.AdminUserSid);
                    m.AdminChanged("service:" + s.Name);
                    c.L("[SC] CreateService SUCCESS"); return 0;
                }
                if (svc == null) return ScFail(c, cmd == "query" || cmd == "queryex" ? "EnumQueryServicesStatus:OpenService" : "OpenService", 1060, "The specified service does not exist as an installed service.");
                switch (cmd)
                {
                    case "query": ScStatus(c, svc, false); return 0;
                    case "queryex": ScStatus(c, svc, true); return 0;
                    case "qc":
                        c.L("[SC] QueryServiceConfig SUCCESS"); c.L("");
                        c.L("SERVICE_NAME: " + svc.Name);
                        c.L("        TYPE               : " + ScType(svc).Replace("  (interactive)", " (interactive)"));
                        c.L("        START_TYPE         : " + (svc.StartType == "Automatic" ? (svc.Delayed ? "2   AUTO_START  (DELAYED)" : "2   AUTO_START") : svc.StartType == "Manual" ? "3   DEMAND_START" : svc.StartType == "Disabled" ? "4   DISABLED" : svc.StartType == "Boot" ? "0   BOOT_START" : "1   SYSTEM_START"));
                        c.L("        ERROR_CONTROL      : 1   NORMAL");
                        c.L("        BINARY_PATH_NAME   : " + svc.BinaryPath);
                        c.L("        LOAD_ORDER_GROUP   : " + svc.Group);
                        c.L("        TAG                : 0");
                        c.L("        DISPLAY_NAME       : " + svc.DisplayName);
                        if (svc.DependsOn.Count == 0) c.L("        DEPENDENCIES       : ");
                        for (int i = 0; i < svc.DependsOn.Count; i++) c.L((i == 0 ? "        DEPENDENCIES       : " : "                           : ") + svc.DependsOn[i]);
                        c.L("        SERVICE_START_NAME : " + svc.Account);
                        return 0;
                    case "qdescription":
                        c.L("[SC] QueryServiceConfig2 SUCCESS"); c.L(""); c.L("SERVICE_NAME: " + svc.Name); c.L("DESCRIPTION:  " + svc.Description); return 0;
                    case "start":
                        if (svc.Running) return ScFail(c, "StartService", 1056, "An instance of the service is already running.");
                        var why = m.StartService(svc, true);
                        if (why == "1058") return ScFail(c, "StartService", 1058, "The service cannot be started, either because it is disabled or because it has no enabled devices associated with it.");
                        if (why == "2") return ScFail(c, "StartService", 2, "The system cannot find the file specified.");
                        if (why != null) return ScFail(c, "StartService", 1068, "The dependency service or group failed to start.");
                        c.L(""); c.L("SERVICE_NAME: " + svc.Name + " ");
                        c.L("        TYPE               : " + ScType(svc) + "  "); c.L("        STATE              : 2  START_PENDING "); c.L("                                (NOT_STOPPABLE, NOT_PAUSABLE, IGNORES_SHUTDOWN)");
                        c.L("        WIN32_EXIT_CODE    : 0  (0x0)"); c.L("        SERVICE_EXIT_CODE  : 0  (0x0)"); c.L("        CHECKPOINT         : 0x0"); c.L("        WAIT_HINT          : 0x7d0"); c.L("        PID                : " + svc.Pid); c.L("        FLAGS              : ");
                        m.AdminChanged("service:" + svc.Name);
                        return 0;
                    case "stop":
                        if (!svc.Running) return ScFail(c, "ControlService", 1062, "The service has not been started.");
                        var w2 = m.StopService(svc, false, true);
                        if (w2 == "1051") return ScFail(c, "ControlService", 1051, "A stop control has been sent to a service that other running services are dependent on.");
                        if (w2 != null) return ScFail(c, "ControlService", 1052, "The requested control is not valid for this service.");
                        c.L(""); c.L("SERVICE_NAME: " + svc.Name + " ");
                        c.L("        TYPE               : " + ScType(svc) + "  "); c.L("        STATE              : 3  STOP_PENDING "); c.L("                                (STOPPABLE, NOT_PAUSABLE, IGNORES_SHUTDOWN)");
                        c.L("        WIN32_EXIT_CODE    : 0  (0x0)"); c.L("        SERVICE_EXIT_CODE  : 0  (0x0)"); c.L("        CHECKPOINT         : 0x0"); c.L("        WAIT_HINT          : 0x0");
                        m.AdminChanged("service:" + svc.Name);
                        return 0;
                    case "pause":
                        if (!svc.CanPause) return ScFail(c, "ControlService", 1052, "The requested control is not valid for this service.");
                        if (!svc.Running) return ScFail(c, "ControlService", 1062, "The service has not been started.");
                        svc.Status = "Paused"; m.ServiceEvent(svc, "Paused"); ScStatus(c, svc, false); return 0;
                    case "continue":
                        if (svc.Status != "Paused") return ScFail(c, "ControlService", 1052, "The requested control is not valid for this service.");
                        svc.Status = "Running"; m.ServiceEvent(svc, "Running"); ScStatus(c, svc, false); return 0;
                    case "config":
                        var st = Opt(a, "start=");
                        if (st != null) { var nt = StartFrom(st); if (nt == null) { c.L("[SC] ChangeServiceConfig FAILED 87:"); c.L(""); c.L("The parameter is incorrect."); c.L(""); return 87; } m.SetStartType(svc, nt, true); svc.Delayed = st.Equals("delayed-auto", StringComparison.OrdinalIgnoreCase); }
                        var bp = Opt(a, "binPath="); if (bp != null) svc.BinaryPath = bp;
                        var dn = Opt(a, "DisplayName="); if (dn != null) svc.DisplayName = dn;
                        var ob = Opt(a, "obj="); if (ob != null) svc.Account = ob;
                        var dp = Opt(a, "depend="); if (dp != null) { svc.DependsOn.Clear(); svc.DependsOn.AddRange(dp.Split('/').Where(x => x.Length > 0)); }
                        if (st == null && bp == null && dn == null && ob == null && dp == null) { c.L("DESCRIPTION:"); c.L("        Modifies a service entry in the registry and Service Database."); c.L("USAGE:"); c.L("        sc <server> config [service name] <option1> <option2>..."); c.L("OPTIONS:"); c.L(" start= <boot|system|auto|demand|disabled|delayed-auto>"); return 1639; }
                        m.AdminChanged("service:" + svc.Name);
                        c.L("[SC] ChangeServiceConfig SUCCESS"); return 0;
                    case "description":
                        svc.Description = string.Join(" ", a.Skip(2).ToArray()); c.L("[SC] ChangeServiceConfig2 SUCCESS"); return 0;
                    case "delete":
                        if (!svc.Custom && !svc.Feature) { svc.Deleted = true; }
                        svc.Deleted = true; if (svc.Running) { svc.Deleted = false; m.StopService(svc, true, true); svc.Deleted = true; }
                        m.AdminChanged("service:" + svc.Name);
                        c.L("[SC] DeleteService SUCCESS"); return 0;
                }
                c.L("ERROR:  Unrecognized command"); c.L(""); c.L("DESCRIPTION:"); c.L("        SC is a command line program used for communicating with the"); c.L("        Service Control Manager and services."); return 1;
            });

            Add("net", null, NetCmd);
            Add("net1", null, NetCmd);
            Add("tasklist", null, Tasklist);
            Add("taskkill", null, Taskkill);
            Add("schtasks", null, Schtasks);
            Add("systeminfo", null, Systeminfo);
        }
        static string StartFrom(string s)
        {
            if (s == null) return null;
            switch (s.ToLowerInvariant()) { case "auto": case "delayed-auto": return "Automatic"; case "demand": return "Manual"; case "disabled": return "Disabled"; case "boot": return "Boot"; case "system": return "System"; }
            return null;
        }

        static int NetCmd(NativeCall c)
        {
            var a = c.Args; var m = c.M;
            if (a.Count == 0) { c.L("The syntax of this command is:"); c.L(""); c.L("NET"); c.L("    [ ACCOUNTS | COMPUTER | CONFIG | CONTINUE | FILE | GROUP | HELP |"); c.L("      HELPMSG | LOCALGROUP | PAUSE | SESSION | SHARE | START |"); c.L("      STATISTICS | STOP | TIME | USE | USER | VIEW ]"); c.L(""); return 1; }
            string cmd = a[0].ToLowerInvariant();
            Func<string, WinService> find = n => m.Svc(n) ?? m.SvcByDisplay(n);
            if (cmd == "start" && a.Count == 1)
            {
                c.L("These Windows services are started:"); c.L("");
                foreach (var s in m.LiveServices.Where(x => x.Running && x.ServiceType != "KernelDriver").OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)) c.L("   " + s.DisplayName);
                c.L(""); c.L("The command completed successfully."); c.L(""); return 0;
            }
            if (cmd == "start" || cmd == "stop" || cmd == "pause" || cmd == "continue")
            {
                string name = string.Join(" ", a.Skip(1).Where(x => !x.StartsWith("/", StringComparison.Ordinal)).ToArray());
                var s = find(name);
                if (s == null) { c.L("The service name is invalid."); c.L(""); c.L("More help is available by typing NET HELPMSG 2185."); c.L(""); return 2; }
                if (cmd == "start")
                {
                    if (s.Running) { c.L("The requested service has already been started."); c.L(""); c.L("More help is available by typing NET HELPMSG 2182."); c.L(""); return 2; }
                    var why = m.StartService(s, true);
                    if (why == "1058") { c.L("System error 1058 has occurred."); c.L(""); c.L("The service cannot be started, either because it is disabled or because it has no enabled devices associated with it."); c.L(""); return 2; }
                    if (why != null) { c.L("The " + s.DisplayName + " service could not be started."); c.L(""); return 2; }
                    c.L("The " + s.DisplayName + " service is starting."); c.L("The " + s.DisplayName + " service was started successfully."); c.L("");
                    m.AdminChanged("service:" + s.Name); return 0;
                }
                if (cmd == "stop")
                {
                    if (!s.Running) { c.L("The " + s.DisplayName + " service is not started."); c.L(""); c.L("More help is available by typing NET HELPMSG 3521."); c.L(""); return 2; }
                    if (!s.CanStop) { c.L("The requested pause, continue, or stop is not valid for this service."); c.L(""); c.L("More help is available by typing NET HELPMSG 2191."); c.L(""); return 2; }
                    var deps = m.Dependents(s).Where(d => d.Running).ToList();
                    if (deps.Count > 0)
                    {
                        c.L("The following services are dependent on the " + s.DisplayName + " service."); c.L("Stopping the " + s.DisplayName + " service will also stop these services."); c.L("");
                        foreach (var d in deps) c.L("   " + d.DisplayName);
                        c.L("");
                        bool yes = a.Any(x => x.Equals("/y", StringComparison.OrdinalIgnoreCase) || x.Equals("/yes", StringComparison.OrdinalIgnoreCase));
                        if (!yes)
                        {
                            foreach (var l in c.Out) c.R.HostWrite(l + "\n"); c.Out.Clear();
                            var ans = c.R.Host == null ? null : c.R.Host.ReadLine("Do you want to continue this operation? (Y/N) [N]: ", false);
                            if (ans == null || !ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) { c.L("The command was canceled."); c.L(""); return 2; }
                        }
                        foreach (var d in deps) { c.L("The " + d.DisplayName + " service is stopping."); m.StopService(d, true, true); c.L("The " + d.DisplayName + " service was stopped successfully."); c.L(""); }
                    }
                    m.StopService(s, true, true);
                    c.L("The " + s.DisplayName + " service is stopping."); c.L("The " + s.DisplayName + " service was stopped successfully."); c.L("");
                    m.AdminChanged("service:" + s.Name); return 0;
                }
                if (cmd == "pause") { if (!s.CanPause || !s.Running) { c.L("The requested pause, continue, or stop is not valid for this service."); c.L(""); return 2; } s.Status = "Paused"; m.ServiceEvent(s, "Paused"); c.L("The " + s.DisplayName + " service was paused successfully."); c.L(""); return 0; }
                if (s.Status != "Paused") { c.L("The requested pause, continue, or stop is not valid for this service."); c.L(""); return 2; }
                s.Status = "Running"; m.ServiceEvent(s, "Running"); c.L("The " + s.DisplayName + " service was continued successfully."); c.L(""); return 0;
            }
            if (cmd == "user" || cmd == "users") return NetUser(c, m, a.Skip(1).ToList());
            if (cmd == "localgroup") return NetLocalGroup(c, m, a.Skip(1).ToList());
            if (cmd == "helpmsg" && a.Count > 1)
            {
                var msgs = new Dictionary<string, string> { { "2182", "The requested service has already been started." }, { "2185", "The service name is invalid." }, { "3521", "The *** service is not started." }, { "2221", "The user name could not be found." }, { "2191", "The requested pause, continue, or stop is not valid for this service." }, { "1378", "The specified account name is already a member of the group." } };
                string t; c.L(""); c.L(msgs.TryGetValue(a[1], out t) ? t : "The system cannot find message text for message number 0x" + a[1] + " in the message file for NETMSG."); c.L(""); return 0;
            }
            c.L("Cert Lab: net " + a[0] + " is not simulated. Try net start, net stop, net user or net localgroup."); return 1;
        }

        static void Columns(NativeCall c, IEnumerable<string> names)
        {
            var l = names.ToList();
            for (int i = 0; i < l.Count; i += 3) c.L(string.Join("", l.Skip(i).Take(3).Select((n, j) => j < 2 ? n.PadRight(25) : n).ToArray()).TrimEnd());
        }
        static int NetUser(NativeCall c, PsMachine m, List<string> a)
        {
            bool domain = a.Any(x => x.Equals("/domain", StringComparison.OrdinalIgnoreCase));
            var words = a.Where(x => !x.StartsWith("/", StringComparison.Ordinal)).ToList();
            if (words.Count == 0)
            {
                c.L(""); c.L("User accounts for \\\\" + (domain ? "DC01.lab.example.com" : m.Name)); c.L("");
                c.L("-------------------------------------------------------------------------------");
                Columns(c, domain ? PsMachine.DomainPrincipals.Where(d => d.Contains("|User|")).Select(d => d.Split('|')[0]) : m.Users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(u => u.Name));
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            string name = words[0];
            var user = m.FindUser(name);
            if (a.Any(x => x.Equals("/add", StringComparison.OrdinalIgnoreCase)))
            {
                if (user != null) { c.L("The account already exists."); c.L(""); c.L("More help is available by typing NET HELPMSG 2224."); c.L(""); return 2; }
                string pw = words.Count > 1 ? words[1] : "";
                if (pw == "*") pw = c.R.Host == null ? "" : (c.R.Host.ReadLine("Type a password for the user: ", true) ?? "");
                if (m.IsServer && pw.Length > 0 && (pw.Length < 6 || new[] { pw.Any(char.IsUpper), pw.Any(char.IsLower), pw.Any(char.IsDigit), pw.Any(ch => !char.IsLetterOrDigit(ch)) }.Count(b => b) < 3))
                { c.L("The password does not meet the password policy requirements. Check the minimum password length, password complexity and password history requirements."); c.L(""); c.L("More help is available by typing NET HELPMSG 2245."); c.L(""); return 2; }
                var u = new WinUser { Name = name, Rid = m.NextRid++, Password = pw, HasPassword = pw.Length > 0, PasswordLastSet = m.Now };
                u.Sid = m.MachineSid + "-" + u.Rid;
                var fn = a.FirstOrDefault(x => x.StartsWith("/fullname:", StringComparison.OrdinalIgnoreCase)); if (fn != null) u.FullName = fn.Substring(10).Trim('"');
                var cm = a.FirstOrDefault(x => x.StartsWith("/comment:", StringComparison.OrdinalIgnoreCase)); if (cm != null) u.Description = cm.Substring(9).Trim('"');
                m.Users.Add(u);
                var g = m.FindGroup("Users"); if (g != null) g.Members.Add(new WinMember { Name = m.Name + "\\" + u.Name, Sid = u.Sid });
                m.AccountEvent(4720, u.Name, u.Sid, null); m.AccountEvent(4722, u.Name, u.Sid, null);
                m.AdminChanged("user:" + u.Name);
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            if (user == null) { c.L("The user name could not be found."); c.L(""); c.L("More help is available by typing NET HELPMSG 2221."); c.L(""); return 2; }
            if (a.Any(x => x.Equals("/delete", StringComparison.OrdinalIgnoreCase)))
            {
                if (user.Rid < 1000) { c.L("System error 5 has occurred."); c.L(""); c.L("Access is denied."); c.L(""); return 2; }
                m.Users.Remove(user); foreach (var g in m.Groups) g.Members.RemoveAll(x => x.Sid == user.Sid);
                m.AccountEvent(4726, user.Name, user.Sid, null); m.AdminChanged("user:" + user.Name);
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            var act = a.FirstOrDefault(x => x.StartsWith("/active:", StringComparison.OrdinalIgnoreCase));
            if (words.Count > 1 || act != null)
            {
                if (words.Count > 1) { string pw = words[1] == "*" ? (c.R.Host == null ? "" : c.R.Host.ReadLine("Type a password for the user: ", true) ?? "") : words[1]; user.Password = pw; user.HasPassword = pw.Length > 0; user.PasswordLastSet = m.Now; m.AccountEvent(4724, user.Name, user.Sid, null); }
                if (act != null) { bool on = act.Substring(8).StartsWith("y", StringComparison.OrdinalIgnoreCase); if (on != user.Enabled) { user.Enabled = on; m.AccountEvent(on ? 4722 : 4725, user.Name, user.Sid, null); } }
                m.AdminChanged("user:" + user.Name);
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            Func<DateTime?, string> fd = t => t.HasValue ? t.Value.ToString("M/d/yyyy h:mm:ss tt", PsConv.Us) : "Never";
            Action<string, string> row = (k, v) => c.L(k.PadRight(29) + v);
            row("User name", user.Name); row("Full Name", user.FullName); row("Comment", user.Description); row("User's comment", ""); row("Country/region code", "000 (System Default)");
            row("Account active", user.Enabled ? "Yes" : "No"); row("Account expires", fd(user.AccountExpires)); c.L("");
            row("Password last set", fd(user.PasswordLastSet)); row("Password expires", user.PasswordNeverExpires || !user.PasswordLastSet.HasValue ? "Never" : fd(user.PasswordLastSet.Value.AddDays(42)));
            row("Password changeable", fd(user.PasswordLastSet)); row("Password required", user.PasswordRequired ? "Yes" : "No"); row("User may change password", user.UserMayChangePassword ? "Yes" : "No"); c.L("");
            row("Workstations allowed", "All"); row("Logon script", ""); row("User profile", ""); row("Home directory", ""); row("Last logon", fd(user.LastLogon)); c.L("");
            row("Logon hours allowed", "All"); c.L("");
            var groups = m.Groups.Where(g => g.Members.Any(x => x.Sid == user.Sid)).Select(g => "*" + g.Name).ToList();
            c.L("Local Group Memberships".PadRight(29) + string.Join("", groups.Take(2).Select(x => x.PadRight(25)).ToArray()).TrimEnd());
            for (int i = 2; i < groups.Count; i += 2) c.L(new string(' ', 29) + string.Join("", groups.Skip(i).Take(2).Select(x => x.PadRight(25)).ToArray()).TrimEnd());
            c.L("Global Group memberships".PadRight(29) + "*None"); c.L("The command completed successfully."); c.L("");
            return 0;
        }
        static int NetLocalGroup(NativeCall c, PsMachine m, List<string> a)
        {
            var words = a.Where(x => !x.StartsWith("/", StringComparison.Ordinal)).ToList();
            if (words.Count == 0)
            {
                c.L(""); c.L("Aliases for \\\\" + m.Name); c.L(""); c.L("-------------------------------------------------------------------------------");
                foreach (var g in m.Groups.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) c.L("*" + g.Name);
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            var grp = m.FindGroup(words[0]);
            bool add = a.Any(x => x.Equals("/add", StringComparison.OrdinalIgnoreCase)), del = a.Any(x => x.Equals("/delete", StringComparison.OrdinalIgnoreCase));
            if (grp == null && add && words.Count == 1) { var g = new WinGroup { Name = words[0], Sid = m.MachineSid + "-" + m.NextRid++ }; var cm = a.FirstOrDefault(x => x.StartsWith("/comment:", StringComparison.OrdinalIgnoreCase)); if (cm != null) g.Description = cm.Substring(9).Trim('"'); m.Groups.Add(g); m.AccountEvent(4731, g.Name, g.Sid, null); c.L("The command completed successfully."); c.L(""); return 0; }
            if (grp == null) { c.L("The specified local group does not exist."); c.L(""); c.L("More help is available by typing NET HELPMSG 3780."); c.L(""); return 2; }
            if (words.Count == 1 && del) { if (grp.Sid.StartsWith("S-1-5-32-", StringComparison.Ordinal)) { c.L("System error 5 has occurred."); c.L(""); c.L("Access is denied."); c.L(""); return 2; } m.Groups.Remove(grp); m.AccountEvent(4734, grp.Name, grp.Sid, null); c.L("The command completed successfully."); c.L(""); return 0; }
            if (words.Count == 1)
            {
                c.L("Alias name     " + grp.Name); c.L("Comment        " + grp.Description); c.L(""); c.L("Members"); c.L(""); c.L("-------------------------------------------------------------------------------");
                foreach (var mb in grp.Members) c.L(mb.Source == "Local" && mb.Name.StartsWith(m.Name + "\\", StringComparison.OrdinalIgnoreCase) ? mb.Name.Substring(m.Name.Length + 1) : mb.Name);
                c.L("The command completed successfully."); c.L(""); return 0;
            }
            foreach (var who in words.Skip(1))
            {
                var mb = PsCmdlets.ResolveMember(m, who);
                if (mb == null) { c.L("There is no such global user or group: " + who + "."); c.L(""); c.L("More help is available by typing NET HELPMSG 3783."); c.L(""); return 2; }
                var have = grp.Members.FirstOrDefault(x => x.Sid == mb.Sid);
                if (add)
                {
                    if (have != null) { c.L("System error 1378 has occurred."); c.L(""); c.L("The specified account name is already a member of the group."); c.L(""); return 2; }
                    grp.Members.Add(mb); m.AccountEvent(4732, grp.Name, grp.Sid, "Member:\r\n\tSecurity ID:\t\t" + mb.Sid + "\r\n\tAccount Name:\t\t" + mb.Name);
                }
                else if (del)
                {
                    if (have == null) { c.L("System error 1377 has occurred."); c.L(""); c.L("The specified account name is not a member of the group."); c.L(""); return 2; }
                    grp.Members.Remove(have); m.AccountEvent(4733, grp.Name, grp.Sid, "Member:\r\n\tSecurity ID:\t\t" + mb.Sid + "\r\n\tAccount Name:\t\t" + mb.Name);
                }
            }
            m.AdminChanged("group:" + grp.Name);
            c.L("The command completed successfully."); c.L(""); return 0;
        }
    }

    public partial class PsCmdlets
    {
        public static WinMember ResolveMember(PsMachine m, string who) { return ResolvePrincipal(m, who); }
    }
}
