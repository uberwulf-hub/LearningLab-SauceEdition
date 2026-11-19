// Cert Lab - more Windows administration programs: tasklist (/svc /fi /fo /nh), taskkill (/im /pid /f /t),
// schtasks (/query /create /run /end /delete /change) and systeminfo, over the pretend machine's data.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static string ImageName(WinProcess p) { return p.Id == 0 ? "System Idle Process" : p.Path == null ? p.Name : p.Name + ".exe"; }
        static string Kb(long bytes) { return (bytes / 1024).ToString("N0", PsConv.Us) + " K"; }
        static bool TaskFilter(PsMachine m, WinProcess p, string f)
        {
            var parts = f.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return true;
            string k = parts[0].ToLowerInvariant(), op = parts[1].ToLowerInvariant(), v = parts[2].Trim();
            string actual; bool num = false;
            switch (k)
            {
                case "imagename": actual = ImageName(p); break;
                case "pid": actual = p.Id.ToString(CultureInfo.InvariantCulture); num = true; break;
                case "session": actual = p.SessionId.ToString(CultureInfo.InvariantCulture); num = true; break;
                case "username": actual = p.User; break;
                case "status": actual = "Running"; break;
                case "memusage": actual = (p.Ws / 1024).ToString(CultureInfo.InvariantCulture); num = true; break;
                case "services": actual = string.Join(",", m.LiveServices.Where(s => s.Pid == p.Id && s.Running).Select(s => s.Name).ToArray()); break;
                case "windowtitle": actual = p.MainWindowTitle; break;
                default: return true;
            }
            if (num)
            {
                long a, b; long.TryParse(actual, out a); long.TryParse(v, out b);
                switch (op) { case "eq": return a == b; case "ne": return a != b; case "gt": return a > b; case "lt": return a < b; case "ge": return a >= b; case "le": return a <= b; }
                return true;
            }
            bool like = PsConv.Like(actual, v, false) || (k == "username" && PsConv.Like(actual.Substring(actual.IndexOf('\\') + 1), v, false));
            return op == "ne" ? !like : like;
        }

        static int Tasklist(NativeCall c)
        {
            var a = c.Args; var m = c.M;
            if (Flag(a, "/?")) { c.L(""); c.L("TASKLIST [/S system [/U username [/P [password]]]]"); c.L("         [/M [module] | /SVC | /V] [/FI filter] [/FO format] [/NH]"); c.L(""); c.L("Description:"); c.L("    This tool displays a list of currently running processes on"); c.L("    either a local or remote machine."); return 0; }
            var target = Opt(a, "/s") == null ? m : m.Net.Get(Opt(a, "/s"));
            if (target == null) { c.L("ERROR: The RPC server is unavailable."); return 1; }
            var procs = target.LiveProcesses().OrderBy(p => p.Id == 0 ? 0 : p.Id == 4 ? 1 : 2).ThenBy(p => p.Start).ThenBy(p => p.Id).ToList();
            for (int i = 0; i < a.Count - 1; i++) if (a[i].Equals("/fi", StringComparison.OrdinalIgnoreCase)) { var f = a[i + 1]; procs = procs.Where(p => TaskFilter(target, p, f)).ToList(); }
            if (procs.Count == 0) { c.L("INFO: No tasks are running which match the specified criteria."); return 0; }
            string fo = (Opt(a, "/fo") ?? "table").ToLowerInvariant();
            bool svc = Flag(a, "/svc"), nh = Flag(a, "/nh"), v = Flag(a, "/v");
            Func<WinProcess, string> services = p => { var l = target.LiveServices.Where(s => s.Pid == p.Id && s.Running && s.ServiceType != "KernelDriver").Select(s => s.Name).ToList(); return l.Count == 0 ? "N/A" : string.Join(", ", l.ToArray()); };
            string[] heads = svc ? new[] { "Image Name", "PID", "Services" } : v ? new[] { "Image Name", "PID", "Session Name", "Session#", "Mem Usage", "Status", "User Name", "CPU Time", "Window Title" } : new[] { "Image Name", "PID", "Session Name", "Session#", "Mem Usage" };
            Func<WinProcess, string[]> row = p =>
            {
                string sn = p.SessionId == 0 ? "Services" : p.SessionId == 1 ? "Console" : "RDP-Tcp#0";
                if (svc) return new[] { ImageName(p), p.Id.ToString(CultureInfo.InvariantCulture), services(p) };
                var basic = new[] { ImageName(p), p.Id.ToString(CultureInfo.InvariantCulture), sn, p.SessionId.ToString(CultureInfo.InvariantCulture), Kb(p.Ws) };
                if (!v) return basic;
                var ts = TimeSpan.FromSeconds(p.Cpu);
                return basic.Concat(new[] { p.Id <= 4 ? "Unknown" : "Running", p.Id <= 4 || p.Protected ? "N/A" : p.User, string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds), p.MainWindowTitle.Length > 0 ? p.MainWindowTitle : "N/A" }).ToArray();
            };
            if (fo == "csv")
            {
                if (!nh) c.L(string.Join(",", heads.Select(h => "\"" + h + "\"").ToArray()));
                foreach (var p in procs) c.L(string.Join(",", row(p).Select(x => "\"" + x + "\"").ToArray()));
                return 0;
            }
            if (fo == "list")
            {
                foreach (var p in procs) { c.L(""); var r = row(p); for (int i = 0; i < heads.Length; i++) c.L((heads[i] + ":").PadRight(14) + r[i]); }
                return 0;
            }
            int[] w = svc ? new[] { 25, 8, 44 } : v ? new[] { 25, 8, 16, 11, 12, 15, 50, 12, 72 } : new[] { 25, 8, 16, 11, 12 };
            bool[] right = svc ? new[] { false, true, false } : v ? new[] { false, true, false, true, true, false, false, true, false } : new[] { false, true, false, true, true };
            Func<string[], string> fmt = cells => string.Join(" ", cells.Select((x, i) => right[i] ? x.PadLeft(w[i]) : (x.Length > w[i] && i < cells.Length - 1 ? x.Substring(0, w[i]) : x.PadRight(w[i]))).ToArray()).TrimEnd();
            c.L("");
            if (!nh) { c.L(fmt(heads)); c.L(string.Join(" ", w.Select(x => new string('=', x)).ToArray())); }
            foreach (var p in procs) c.L(fmt(row(p)));
            return 0;
        }

        static int Taskkill(NativeCall c)
        {
            var a = c.Args; var m = c.M;
            var ims = new List<string>(); var pids = new List<int>();
            for (int i = 0; i < a.Count - 1; i++)
            {
                if (a[i].Equals("/im", StringComparison.OrdinalIgnoreCase)) ims.Add(a[i + 1]);
                if (a[i].Equals("/pid", StringComparison.OrdinalIgnoreCase)) { int p; if (int.TryParse(a[i + 1], out p)) pids.Add(p); }
            }
            if (ims.Count == 0 && pids.Count == 0) { c.L("ERROR: Invalid syntax. Neither /FI nor /PID nor /IM were specified."); c.L("Type \"TASKKILL /?\" for usage."); return 1; }
            bool force = Flag(a, "/f"), tree = Flag(a, "/t");
            int code = 0;
            var live = m.LiveProcesses();
            Action<WinProcess, string> kill = null;
            kill = (p, label) =>
            {
                if (tree) foreach (var ch in m.LiveProcesses().Where(x => x.ParentId == p.Id && !x.Exited).ToList()) kill(ch, ImageName(ch));
                if (p.Protected || p.Id <= 4) { c.Err("ERROR: The process with PID " + p.Id + " could not be terminated.\nReason: Access is denied."); code = 1; return; }
                bool gui = p.MainWindowTitle.Length > 0;
                if (!force && !gui) { c.Err("ERROR: The process with PID " + p.Id + " could not be terminated.\nReason: This process can only be terminated forcefully (with /F option)."); code = 1; return; }
                m.EndProcess(p, force ? 1 : 0);
                if (!force) c.L("SUCCESS: Sent termination signal to the process \"" + label + "\" with PID " + p.Id + ".");
                else if (tree && p.ParentId != 0) c.L("SUCCESS: The process with PID " + p.Id + " (child process of PID " + p.ParentId + ") has been terminated.");
                else c.L("SUCCESS: The process \"" + label + "\" with PID " + p.Id + " has been terminated.");
            };
            foreach (var im in ims)
            {
                var hit = live.Where(p => PsConv.Like(ImageName(p), im, false) || PsConv.Like(p.Name, im, false)).ToList();
                if (hit.Count == 0) { c.Err("ERROR: The process \"" + im + "\" not found."); code = 128; continue; }
                foreach (var p in hit) if (!p.Exited) kill(p, ImageName(p));
            }
            foreach (var id in pids)
            {
                var p = live.FirstOrDefault(x => x.Id == id);
                if (p == null) { c.Err("ERROR: The process \"" + id + "\" not found."); code = 128; continue; }
                if (!p.Exited) kill(p, ImageName(p));
            }
            m.AdminChanged("process");
            return code;
        }

        static int Schtasks(NativeCall c)
        {
            var a = c.Args; var m = c.M;
            string tn = Opt(a, "/tn");
            Func<WinTask> find = () =>
            {
                if (tn == null) return null;
                string path = "\\", name = tn;
                int bs = tn.LastIndexOf('\\');
                if (bs >= 0) { path = PsMachine.NormTaskPath(tn.Substring(0, bs)); name = tn.Substring(bs + 1); }
                return m.FindTask(bs >= 0 ? path : "\\", name) ?? (bs < 0 ? m.FindTask(null, name) : null);
            };
            Func<string, string> when = s => s;
            if (a.Count == 0 || Flag(a, "/query"))
            {
                string fo = (Opt(a, "/fo") ?? "table").ToLowerInvariant();
                var list = tn != null ? new[] { find() }.Where(x => x != null).ToList() : m.Tasks.Where(t => !t.Hidden).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (tn != null && list.Count == 0) { c.Err("ERROR: The system cannot find the file specified."); return 1; }
                Func<WinTask, string> next = t => t.State == "Disabled" ? "N/A" : t.NextRun.HasValue ? t.NextRun.Value.ToString("M/d/yyyy h:mm:ss tt", PsConv.Us) : "N/A";
                Func<WinTask, string> status = t => t.State == "Disabled" ? "Disabled" : t.State == "Running" ? "Running" : "Ready";
                if (fo == "csv")
                {
                    c.L("\"TaskName\",\"Next Run Time\",\"Status\"");
                    foreach (var t in list) c.L("\"" + t.FullName + "\",\"" + next(t) + "\",\"" + status(t) + "\"");
                    return 0;
                }
                if (fo == "list" || Flag(a, "/v"))
                {
                    foreach (var t in list)
                    {
                        c.L("");
                        Action<string, string> r = (k, v) => c.L((k + ":").PadRight(38) + v);
                        r("Folder", t.Path.Length > 1 ? t.Path.TrimEnd('\\') : "\\"); r("HostName", m.Name); r("TaskName", t.FullName); r("Next Run Time", next(t)); r("Status", status(t)); r("Logon Mode", t.LogonType == "Password" || t.LogonType == "ServiceAccount" ? "Interactive/Background" : "Interactive only");
                        if (Flag(a, "/v"))
                        {
                            r("Last Run Time", t.LastRun.HasValue ? t.LastRun.Value.ToString("M/d/yyyy h:mm:ss tt", PsConv.Us) : "11/30/1999 12:00:00 AM"); r("Last Result", ((long)(uint)t.LastResult).ToString(CultureInfo.InvariantCulture)); r("Author", t.Author ?? "N/A");
                            r("Task To Run", string.Join(", ", t.Actions.Select(x => x.Execute + (x.Arguments != null ? " " + x.Arguments : "")).ToArray())); r("Comment", t.Description ?? "N/A"); r("Run As User", t.UserId ?? "N/A");
                        }
                    }
                    return 0;
                }
                foreach (var g in list.GroupBy(t => t.Path))
                {
                    c.L(""); c.L("Folder: " + (g.Key.Length > 1 ? g.Key.TrimEnd('\\') : "\\"));
                    c.L("TaskName                                 Next Run Time          Status");
                    c.L("======================================== ====================== ===============");
                    foreach (var t in g) c.L((t.Name.Length > 40 ? t.Name.Substring(0, 37) + "..." : t.Name).PadRight(41) + next(t).PadRight(23) + status(t));
                }
                return 0;
            }
            if (Flag(a, "/create"))
            {
                string tr = Opt(a, "/tr"), sc = (Opt(a, "/sc") ?? "").ToLowerInvariant();
                if (tn == null || tr == null || sc.Length == 0) { c.Err("ERROR: Invalid syntax. Mandatory option '" + (tn == null ? "/tn" : tr == null ? "/tr" : "/sc") + "' is missing.\nType \"SCHTASKS /CREATE /?\" for usage."); return 1; }
                if (find() != null && !Flag(a, "/f")) { c.Err("ERROR: Cannot create a file when that file already exists."); return 1; }
                string path = "\\", name = tn; int bs = tn.LastIndexOf('\\'); if (bs >= 0) { path = PsMachine.NormTaskPath(tn.Substring(0, bs)); name = tn.Substring(bs + 1); }
                var t = new WinTask { Name = name, Path = path, Date = m.Now, Author = m.DomainShort + "\\Administrator", LastResult = 267011 };
                var words = PsMachine.SplitArgs(tr);
                t.Actions.Add(new WinTaskAction { Execute = words.Count > 0 ? words[0] : tr, Arguments = words.Count > 1 ? string.Join(" ", words.Skip(1).Select(w => w.Contains(" ") ? "\"" + w + "\"" : w).ToArray()) : null });
                var trg = new WinTrigger();
                DateTime st = m.Now.Date.AddHours(m.Now.Hour).AddMinutes(m.Now.Minute);
                string sts = Opt(a, "/st"); TimeSpan tod;
                if (sts != null && TimeSpan.TryParse(sts, CultureInfo.InvariantCulture, out tod)) st = m.Now.Date.Add(tod);
                string sd = Opt(a, "/sd"); DateTime dd; if (sd != null && DateTime.TryParse(sd, PsConv.Us, DateTimeStyles.None, out dd)) st = dd.Date.Add(st.TimeOfDay);
                int mo = 1; int.TryParse(Opt(a, "/mo") ?? "1", out mo); if (mo < 1) mo = 1;
                switch (sc)
                {
                    case "once": trg.Kind = "Once"; break;
                    case "daily": trg.Kind = "Daily"; trg.DaysInterval = mo; break;
                    case "weekly":
                        trg.Kind = "Weekly"; trg.WeeksInterval = mo;
                        foreach (var d in (Opt(a, "/d") ?? st.DayOfWeek.ToString().Substring(0, 3)).Split(','))
                        { var dn = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }; int i = Array.IndexOf(dn, d.Trim().ToUpperInvariant().Substring(0, Math.Min(3, d.Trim().Length))); if (i >= 0) trg.Days.Add((DayOfWeek)i); }
                        break;
                    case "minute": trg.Kind = "Once"; trg.RepInterval = TimeSpan.FromMinutes(mo); break;
                    case "hourly": trg.Kind = "Once"; trg.RepInterval = TimeSpan.FromHours(mo); break;
                    case "onstart": trg.Kind = "AtStartup"; break;
                    case "onlogon": trg.Kind = "AtLogOn"; break;
                    default: c.Err("ERROR: Invalid value for /SC option."); return 1;
                }
                trg.Start = st;
                t.Triggers.Add(trg);
                string ru = Opt(a, "/ru");
                if (ru == null) { t.UserId = "Administrator"; t.LogonType = "Interactive"; }
                else if (ru.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || ru.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase)) { t.UserId = "SYSTEM"; t.LogonType = "ServiceAccount"; }
                else { t.UserId = ru; t.LogonType = Opt(a, "/rp") != null ? "Password" : "Interactive"; t.Password = Opt(a, "/rp"); }
                if ((Opt(a, "/rl") ?? "").Equals("HIGHEST", StringComparison.OrdinalIgnoreCase)) t.RunLevel = "Highest";
                var old = find(); if (old != null) m.Tasks.Remove(old);
                m.Tasks.Add(t);
                t.NextRun = m.NextTaskTime(t, m.Now);
                m.AdminChanged("task:" + t.FullName);
                c.L("SUCCESS: The scheduled task \"" + name + "\" has successfully been created.");
                return 0;
            }
            var task = find();
            if (Flag(a, "/run") || Flag(a, "/end") || Flag(a, "/delete") || Flag(a, "/change"))
            {
                if (tn == null) { c.Err("ERROR: Invalid syntax. Mandatory option '/tn' is missing."); return 1; }
                if (task == null) { c.Err("ERROR: The system cannot find the file specified."); return 1; }
            }
            if (Flag(a, "/run"))
            {
                if (task.State == "Disabled") { c.Err("ERROR: The task is disabled."); return 1; }
                m.RunTask(task, "demand");
                c.L("SUCCESS: Attempted to run the scheduled task \"" + tn + "\"."); return 0;
            }
            if (Flag(a, "/end")) { if (task.State == "Running") task.State = "Ready"; c.L("SUCCESS: The scheduled task \"" + tn + "\" has been terminated successfully."); return 0; }
            if (Flag(a, "/delete"))
            {
                if (!Flag(a, "/f"))
                {
                    var ans = c.R.Host == null ? "y" : c.R.Host.ReadLine("WARNING: Are you sure you want to remove the task \"" + tn + "\" (Y/N)? ", false);
                    if (ans != null && !ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) { c.L("INFO: The task was not deleted."); return 0; }
                }
                m.Tasks.Remove(task); m.AdminChanged("task:" + task.FullName);
                c.L("SUCCESS: The scheduled task \"" + tn + "\" was successfully deleted."); return 0;
            }
            if (Flag(a, "/change"))
            {
                if (Flag(a, "/disable")) { task.State = "Disabled"; task.NextRun = null; }
                if (Flag(a, "/enable") && task.State == "Disabled") task.State = "Ready";
                var tr = Opt(a, "/tr"); if (tr != null) { var w = PsMachine.SplitArgs(tr); task.Actions.Clear(); task.Actions.Add(new WinTaskAction { Execute = w.Count > 0 ? w[0] : tr, Arguments = w.Count > 1 ? string.Join(" ", w.Skip(1).ToArray()) : null }); }
                var ru = Opt(a, "/ru"); if (ru != null) { task.UserId = ru.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ? "SYSTEM" : ru; task.LogonType = task.UserId == "SYSTEM" ? "ServiceAccount" : "Interactive"; }
                task.NextRun = m.NextTaskTime(task, m.Now);
                m.AdminChanged("task:" + task.FullName);
                c.L("SUCCESS: The parameters of scheduled task \"" + tn + "\" have been changed."); return 0;
            }
            c.Err("ERROR: Invalid argument/option - '" + a[0] + "'.\nType \"SCHTASKS /?\" for usage."); return 1;
        }

        static int Systeminfo(NativeCall c)
        {
            var m = c.M;
            var target = Opt(c.Args, "/s") == null ? m : m.Net.Get(Opt(c.Args, "/s"));
            if (target == null) { c.Err("ERROR: The RPC server is unavailable."); return 1; }
            m = target;
            var os = PsCim.Find("root/cimv2", "Win32_OperatingSystem").Rows(m)[0];
            Func<DateTime, string> d = t => t.ToString("M/d/yyyy, h:mm:ss tt", PsConv.Us);
            var lines = new List<KeyValuePair<string, string>>();
            Action<string, string> r = (k, v) => lines.Add(new KeyValuePair<string, string>(k, v));
            r("Host Name", m.Name); r("OS Name", m.OsCaption); r("OS Version", m.OsVersion + " N/A Build " + m.OsBuild); r("OS Manufacturer", "Microsoft Corporation");
            r("OS Configuration", m.IsServer ? "Member Server" : "Member Workstation"); r("OS Build Type", "Multiprocessor Free"); r("Registered Owner", PsConv.ToStr(os.Get("RegisteredUser"))); r("Registered Organization", PsConv.ToStr(os.Get("Organization")));
            r("Product ID", m.ProductId); r("Original Install Date", d(m.InstallDate)); r("System Boot Time", d(m.BootTime)); r("System Manufacturer", "Microsoft Corporation"); r("System Model", "Virtual Machine"); r("System Type", "x64-based PC");
            r("Processor(s)", "1 Processor(s) Installed.\n                           [01]: Intel64 Family 6 Model 106 Stepping 6 GenuineIntel ~1995 Mhz");
            r("BIOS Version", "Microsoft Corporation Hyper-V UEFI Release v4.1, 4/5/2023"); r("Windows Directory", "C:\\Windows"); r("System Directory", "C:\\Windows\\system32"); r("Boot Device", "\\Device\\HarddiskVolume1");
            r("System Locale", "en-us;English (United States)"); r("Input Locale", "en-us;English (United States)"); r("Time Zone", "(UTC-08:00) Pacific Time (US & Canada)");
            long tot = PsConv.ToLong(os.Get("TotalVisibleMemorySize")) / 1024, free = PsConv.ToLong(os.Get("FreePhysicalMemory")) / 1024;
            r("Total Physical Memory", tot.ToString("N0", PsConv.Us) + " MB"); r("Available Physical Memory", free.ToString("N0", PsConv.Us) + " MB");
            r("Virtual Memory: Max Size", (tot + 1216).ToString("N0", PsConv.Us) + " MB"); r("Virtual Memory: Available", (free + 1100).ToString("N0", PsConv.Us) + " MB"); r("Virtual Memory: In Use", (tot - free + 116).ToString("N0", PsConv.Us) + " MB");
            r("Page File Location(s)", "C:\\pagefile.sys"); r("Domain", m.Domain); r("Logon Server", "\\\\DC01");
            var hf = new StringBuilder(m.Hotfixes.Count + " Hotfix(s) Installed.");
            for (int i = 0; i < m.Hotfixes.Count; i++) hf.Append("\n                           [" + (i + 1).ToString("00") + "]: " + m.Hotfixes[i].Id);
            r("Hotfix(s)", hf.ToString());
            r("Network Card(s)", "1 NIC(s) Installed.\n                           [01]: Microsoft Hyper-V Network Adapter\n                                 Connection Name: Ethernet\n                                 DHCP Enabled:    No\n                                 IP address(es)\n                                 [01]: " + m.Ip);
            r("Hyper-V Requirements", "A hypervisor has been detected. Features required for Hyper-V will not be displayed.");
            string fo = (Opt(c.Args, "/fo") ?? "list").ToLowerInvariant();
            if (fo == "csv")
            {
                c.L(string.Join(",", lines.Select(kv => "\"" + kv.Key + "\"").ToArray()));
                c.L(string.Join(",", lines.Select(kv => "\"" + kv.Value.Replace("\n", ",").Replace("                           ", "").Replace("  ", " ").Trim() + "\"").ToArray()));
                return 0;
            }
            c.L("");
            foreach (var kv in lines) c.L((kv.Key + ":").PadRight(27) + kv.Value);
            return 0;
        }
    }
}
