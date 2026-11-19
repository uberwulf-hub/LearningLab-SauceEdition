// Cyber Range - one run of a scenario or a puzzle: builds its machines from the seed (RHEL-like machines from
// Cert Lab's simulator, pretend Windows hosts from the PowerShell course's), fills every secret for this run, places
// the flags, starts the player's console and keeps the score. Red Team is solo; Red vs Defender adds a simulated
// defender that watches the noise log (RangeDefender.cs); Blue Team puts the player on defence while a simulated
// attacker acts on the clock, with questions to answer and fixes to make (checked like a lab).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class RangeRun
    {
        public RangeScenario Sc, Template;     // Sc: filled for this run; Template: the data file
        public RangePuzzle Pz, PzTemplate;
        public int Seed;
        public RangeNet Net;
        public RhelSim Sim;
        public Dictionary<string, string> Vars = new Dictionary<string, string>();
        public Dictionary<string, string> Flags = new Dictionary<string, string>();   // id -> FLAG{...}
        public List<string> Found = new List<string>();
        public List<int> HintsUsed = new List<int>();
        public bool LearningMode;              // Beginner: hints are free
        public long Seconds;                   // time spent in this run (real seconds the run page was open)
        public bool Finished, GaveUp;
        public string StartMachine = "";
        public readonly List<string> SetupLog = new List<string>();
        // blue team
        public readonly Dictionary<string, string> Answered = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly HashSet<string> TasksDone = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Missed = new HashSet<string>(StringComparer.Ordinal);   // questions closed after their last try (no points)
        public readonly Dictionary<string, int> WrongTries = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> AttackerLog = new List<string>();
        public bool Breached;                  // the attacker reached its goal before the player stopped it
        int nextStep;

        public string Id { get { return Sc != null ? Sc.Id : Pz.Id; } }
        public string Title { get { return Sc != null ? Sc.Title : Pz.Title; } }
        public string Level { get { return Sc != null ? Sc.Level : Pz.Level; } }
        public string Category { get { return Sc != null ? Sc.Category : Pz.Category; } }
        public bool IsBlue { get { return Sc != null && RangeData.BlueLike(Sc.Category); } }
        public bool IsDefender { get { return Sc != null && Sc.Category == "defender"; } }
        public List<RangeHint> Hints { get { return Sc != null ? Sc.Hints : Pz.Hints; } }
        public string Walkthrough { get { return Sc != null ? Sc.Walkthrough : Pz.Walkthrough; } }
        public int MaxPoints { get { return Sc != null ? Sc.MaxPoints : Pz.Points; } }
        public RangeDefender Defender;

        public int FlagPoints
        {
            get
            {
                if (Pz != null) return Found.Count > 0 ? Pz.Points : 0;
                int p = Sc.Flags.Where(f => Found.Contains(f.Id)).Sum(f => f.Points);
                p += Sc.Questions.Where(q => Answered.ContainsKey(q.Id) && !Missed.Contains(q.Id)).Sum(q => q.Points);
                p += Sc.Tasks.Where(t => TasksDone.Contains(t.Id)).Sum(t => t.Points);
                if (IsBlue && !Breached) p += Sc.GoalPoints;
                if (IsDefender && Defender != null) p += Defender.QuietBonus(Sc.QuietPoints);
                return p;
            }
        }
        public int HintCost { get { return LearningMode ? 0 : HintsUsed.Where(i => i >= 0 && i < Hints.Count).Sum(i => Hints[i].Cost); } }
        public int Score { get { return Math.Max(0, FlagPoints - HintCost); } }
        public bool AllFound
        {
            get
            {
                if (Pz != null) return Found.Count > 0;
                return Sc.Flags.All(f => Found.Contains(f.Id)) && Sc.Questions.All(q => Answered.ContainsKey(q.Id)) && Sc.Tasks.All(t => TasksDone.Contains(t.Id));
            }
        }
        public ITerm Console { get { return Sim.Console(StartMachine); } }

        // ------------------------------------------------------------ building
        public static RangeRun ForScenario(RangeScenario template, int seed)
        {
            RangeScenario s; Dictionary<string, string> vars, flags;
            if (template.Raw != null) s = RangeData.Instantiate(template, seed, out vars, out flags);
            else
            {
                vars = RangeData.MakeVars(template.Vars, template.Variants, seed, template.Id);
                flags = new Dictionary<string, string>();
                foreach (var f in template.Flags) flags[f.Id] = RangeData.FlagValue(seed, template.Id, f.Id);
                s = template;
            }
            var r = new RangeRun { Sc = s, Template = template, Seed = seed, StartMachine = s.Start, Vars = vars, Flags = flags };
            r.Build();
            return r;
        }
        public static RangeRun ForPuzzle(RangePuzzle template, int seed)
        {
            RangePuzzle p; Dictionary<string, string> vars, flags;
            if (template.Raw.Length > 0) p = RangeData.InstantiatePuzzle(template, seed, out vars, out flags);
            else { vars = RangeData.MakeVars(template.Vars, template.Variants, seed, template.Id); flags = new Dictionary<string, string> { { "", RangeData.FlagValue(seed, template.Id, "flag") } }; p = template; }
            var r = new RangeRun { Pz = p, PzTemplate = template, Seed = seed, StartMachine = "puzzlebox", Vars = vars, Flags = flags };
            r.BuildPuzzle();
            return r;
        }

        string F(string text) { return RangeData.Fill(text, Vars, Flags); }

        LabNet NewLab()
        {
            RangeTools.Register();
            var lab = new LabNet { Seed = Seed, UtilityIp = "192.0.2.254", Domain = "range.example" };
            Net = new RangeNet { Lab = lab, Start = StartMachine };
            lab.Range = Net;
            Sim = new RhelSim { Net = lab };
            Sim.SetupLog = x => SetupLog.Add(x);
            return lab;
        }

        static void InstallWithDeps(Machine m, string name, HashSet<string> done)
        {
            if (done.Contains(name) || m.PackageInstalled(name)) return;
            done.Add(name);
            var p = Packages.Get(name);
            if (p == null) throw new InvalidOperationException("unknown package " + name);
            foreach (var r in p.Requires) InstallWithDeps(m, r, done);
            m.InstallPkg(p, "AppStream");
        }

        Machine NewLinux(LabNet lab, RangeMachineDef d, int index)
        {
            var m = new Machine { Name = d.Name, Seed = Seed * 100 + index + 1 };
            m.Attach(lab);
            m.Install(d.Hostname.Length > 0 ? d.Hostname : d.Name + ".range.example", d.FirstIp);
            m.NicNames.Clear(); m.Macs.Clear();
            for (int i = 0; i < d.Nics.Count; i++)
            {
                m.NicNames.Add("enp" + (i + 1) + "s0");
                m.Macs.Add("52:54:00:" + RangeData.Hex(Seed + d.Name + i, 2) + ":" + RangeData.Hex(d.Name + i + Seed, 2) + ":" + (10 + index).ToString("x2"));
            }
            lab.Machines.Add(m);
            return m;
        }

        void Prepare(Machine m, RangeMachineDef d)
        {
            m.V.RemoveG(Machine.NmDir + "/enp1s0.nmconnection");
            for (int i = 0; i < d.Nics.Count; i++)
            {
                string nic = "enp" + (i + 1) + "s0";
                string cidr = d.Nics[i].Contains("/") ? d.Nics[i] : d.Nics[i] + "/24";
                m.V.Put(Machine.NmDir + "/" + nic + ".nmconnection", "[connection]\nid=" + nic + "\nuuid=" + m.NewUuid() + "\ntype=ethernet\ninterface-name=" + nic + "\n\n[ethernet]\n\n[ipv4]\naddress1=" + cidr + "\nmethod=manual\n\n[ipv6]\nmethod=disabled\n", 0x180, 0, 0, null);
            }
            m.Ifs.Clear();
            m.NmStart();
            var done = new HashSet<string>();
            foreach (var p in d.Packages) InstallWithDeps(m, p, done);
            foreach (var t in d.Tools)
            {
                var p = Packages.Get(t);
                if (p != null) InstallWithDeps(m, t, done);
                else RangeTools.InstallTool(m, t);
            }
            if (done.Count > 0) Run(m, "systemctl daemon-reload");
            if (d.Banner.Length > 0) m.Banner = F(d.Banner);
        }

        void AfterBoot(Machine m, RangeMachineDef d)
        {
            var sb = new StringBuilder();
            sb.Append("setenforce 0\nsed -i 's/^SELINUX=enforcing/SELINUX=permissive/' /etc/selinux/config\n");
            if (!d.Firewall) sb.Append("systemctl disable --now firewalld >/dev/null 2>&1\n");
            if (d.Motd.Length > 0) { Run(m, "cat > /etc/motd <<'RANGE_MOTD'\n" + F(d.Motd) + "\nRANGE_MOTD\n"); }
            Run(m, sb.ToString());
            if (d.Setup.Length > 0) Run(m, F(d.Setup));
        }

        void Run(Machine m, string script)
        {
            var r = m.RunCommand("root", "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin\n" + script, null, 800000, null);
            SetupLog.Add(m.Name + ": exit " + r.Status + (r.Output.Length > 0 ? "\n" + r.Output : ""));
        }

        void PlaceLinuxFlag(Machine m, RangeFlagDef f)
        {
            string dir = Vfs.Parent(f.Path);
            Run(m, "mkdir -p '" + dir + "'\nprintf '%s\\n' '" + Flags[f.Id] + "' > '" + f.Path + "'\nchown " + f.Owner + ": '" + f.Path + "'\nchmod " + f.Mode + " '" + f.Path + "'\n");
        }

        void Build()
        {
            var lab = NewLab();
            var s = Sc;
            Net.DomainName = s.DomainName; Net.DomainNetbios = s.DomainNetbios;
            foreach (var a in s.DomainAccounts) Net.DomainAccounts.Add(new RangeAccount { User = a.User, Password = F(a.Password), Admin = a.Admin, Remote = a.Remote, Spn = a.Spn });
            var linux = new List<KeyValuePair<Machine, RangeMachineDef>>();
            int i = 0;
            foreach (var d in s.Machines)
            {
                if (d.Os == "linux") linux.Add(new KeyValuePair<Machine, RangeMachineDef>(NewLinux(lab, d, i), d));
                i++;
                if (d.Versions.Count > 0) Net.MachineVersions[d.Name] = d.Versions.ToDictionary(kv => kv.Key, kv => F(kv.Value));
            }
            foreach (var kv in linux) kv.Key.Boot(Machine.Kernels[0], kv.Key.DefaultArgs(), false);
            foreach (var kv in linux) Prepare(kv.Key, kv.Value);
            var windows = s.Machines.Where(d => d.Os == "windows").ToList();
            if (windows.Count > 0)
            {
                PsEnums.Init();
                Net.Ps = new PsNet { Seed = Seed, Rng = new Random(Seed) };
                Net.Ps.Now = new DateTime(1970, 1, 1).AddSeconds(lab.Now);
                foreach (var d in windows) BuildWindows(d);
            }
            foreach (var r in s.Routes) Net.Routes.Add(new RangeRoute { From = r.From, To = r.To, Ports = new List<int>(r.Ports) });
            foreach (var w in s.Web)
            {
                var c = new RangeWebApp { Machine = w.Machine, Port = w.Port, Path = w.Path, Kind = w.Kind, Param = w.Param, RunAs = w.RunAs, Template = F(w.Template), Base = w.Base, Title = F(w.Title), Body = F(w.Body), Server = F(w.Server), Fail = F(w.Fail), Cookie = F(w.Cookie), Want = F(w.Want), Header = F(w.Header) };
                foreach (var kv in w.Settings) c.Settings[kv.Key] = F(kv.Value);
                foreach (var l in w.Logins) c.Logins.Add(l.Select(F).ToArray());
                foreach (var r in w.Records) c.Records.Add(r.Select(F).ToArray());
                Net.WebApps.Add(c);
            }
            foreach (var kv in linux) AfterBoot(kv.Key, kv.Value);
            // SOC Analyst: the pretend SIEM's events on the analyst's box (the scenario's lines and the day's background noise)
            var siemBox = lab.Get(s.Start);
            if (s.SiemText.Length > 0 && siemBox != null)
            {
                siemBox.V.EnsureDir("/var/lib/siem", 0x1ED, null);
                siemBox.V.Put(Cmd.SiemFile, RangeSoc.BuildLog(s, Seed), 0x1A4, 0, 0, null);
            }
            foreach (var f in s.Flags)
            {
                var m = lab.Get(f.Machine);
                if (m != null) { PlaceLinuxFlag(m, f); continue; }
                var h = Net.HostByName(f.Machine);
                if (h != null) PlaceWindowsFlag(h, f);
            }
            var start = lab.Get(s.Start);
            if (start != null)
            {
                if (s.Hosts.Count > 0) Run(start, "cat >> /etc/hosts <<'EOF'\n" + string.Join("\n", s.Hosts.Select(F).ToArray()) + "\nEOF\n");
                // Blue Team: the box greets its defender, not an attacker (the attack box's banner is for red runs)
                if (s.Category == "blue" && start.Banner.Length == 0) start.Banner = DefenderBanner();
                if (s.Category == "soc" && start.Banner.Length == 0) start.Banner = AnalystBanner();
                RangeTools.SetUpAttackBox(start);
            }
            // the logs and the clock start clean for the player
            Net.Events.Clear(); Net.Seen.Clear(); Net.Worked.Clear();
            if (s.Category == "defender") Defender = new RangeDefender(this);
            if (IsBlue) { nextStep = 0; Breached = false; AttackerSteps.Clear(); }
            StartClock = Net.Lab.Now;
        }

        /// <summary>The Blue Team box's welcome: the company's server message, a line saying the player defends it, and
        /// the log and tool commands that are a good start (the Windows and web ones only when the scenario has them).</summary>
        public string DefenderBanner()
        {
            var s = Sc;
            var d = s.Machines.First(x => x.Name == s.Start);
            string host = d.Hostname.Length > 0 ? d.Hostname : d.Name;
            string co; if (!Vars.TryGetValue("CO", out co) || co.Length == 0) { int dot = host.IndexOf('.'); co = dot > 0 ? host.Substring(dot + 1) : "the company"; }
            bool soc = d.Role.Length == 0 || d.Role.StartsWith("your box", StringComparison.OrdinalIgnoreCase);
            var others = s.Machines.Where(x => x.Name != s.Start).ToList();
            var sb = new StringBuilder();
            sb.Append(co + " - " + host + (soc ? " (the security team's workstation)" : " (" + d.Role + ")") + "\n");
            sb.Append("Authorized staff only. Activity on this network is monitored and logged.\n\n");
            sb.Append("Cyber Range - Blue Team. You are the defender here: someone has been at work on " + (soc ? "the machines you look after" : "this server") + ".\n");
            sb.Append("Read the logs, answer the questions in the Findings tab and make the fixes before the attacker reaches its goal.\n");
            sb.Append("Good first commands: last, lastb, journalctl, grep sshd /var/log/secure, ls -la /etc/cron.d, systemctl list-timers, ss -tlnp, ps aux.\n");
            if (s.Machines.Any(x => x.Packages.Contains("httpd"))) sb.Append("Web servers: tail -n 50 /var/log/httpd/access_log, ls -la /var/www/html.\n");
            if (others.Any(x => x.Os == "linux")) sb.Append("Other Linux machines: ssh to them with the account in the briefing (sudo for root).\n");
            if (others.Any(x => x.Os == "windows")) sb.Append("Windows PCs: winrm-shell -i HOST -u Administrator, then Get-WinEvent -LogName Security, Get-ScheduledTask, Get-Service.\n");
            return sb.ToString();
        }

        /// <summary>The SOC Analyst's box: the company's line, a sentence saying the player is the analyst on shift, and
        /// the siem commands that are a good start.</summary>
        public string AnalystBanner()
        {
            var d = Sc.Machines.First(x => x.Name == Sc.Start);
            string host = d.Hostname.Length > 0 ? d.Hostname : d.Name;
            string co; if (!Vars.TryGetValue("CO", out co) || co.Length == 0) { int dot = host.IndexOf('.'); co = dot > 0 ? host.Substring(dot + 1) : "the company"; }
            var sb = new StringBuilder();
            sb.Append(co + " - " + host + " (the security team's analyst workstation)\n");
            sb.Append("Authorized staff only. Activity on this network is monitored and logged.\n\n");
            sb.Append("Cyber Range - SOC Analyst. You are the analyst on shift. The alerts have fired and the SIEM has collected\n");
            sb.Append("the logs from the company's computers. Decide what is real, follow the trail, and answer in the Findings tab.\n");
            sb.Append("Good first commands: siem alerts, siem fields, siem search host=NAME, siem show ID, man siem.\n");
            return sb.ToString();
        }

        /// <summary>The Linux Ladder's box: which step this is and what to look for.</summary>
        public string LadderBanner()
        {
            return "Linux Ladder - a practice computer (Cyber Range). You are " + Pz.Login + ".\n" +
                   "Find the password for the next step: it is the answer to type in the answer box.\n" +
                   "Stuck? man COMMAND explains any command, and the Hints tab helps one step at a time.\n";
        }

        void BuildWindows(RangeMachineDef d)
        {
            var pm = new PsMachine(Net.Ps, d.Name.ToUpperInvariant(), d.Server, d.FirstIp);
            pm.NoComplexity = true;
            Net.Ps.Machines.Add(pm);
            Net.Ps.Hosts.Add(new LabHost { Name = pm.Name, Ip = d.FirstIp, Machine = pm, Ports = new List<int>(d.Ports) });
            pm.Boot(true);
            var h = new RangeHost { Name = pm.Name, Hostname = d.Hostname.Length > 0 ? d.Hostname : d.Name.ToLowerInvariant() + ".range.example", Role = d.Role, Ps = pm, Net = Net, DomainJoined = d.DomainJoined };
            h.Nics.AddRange(d.Nics.Select(x => x.Contains("/") ? x : x + "/24"));
            h.Ports.AddRange(d.Ports);
            foreach (var kv in d.Versions) h.Versions[kv.Key] = F(kv.Value);
            foreach (var kv in d.PortNames) h.Names[kv.Key] = kv.Value;
            foreach (var a in d.Accounts) h.Accounts.Add(new RangeAccount { User = a.User, Password = F(a.Password), Admin = a.Admin, Remote = a.Remote, Spn = a.Spn });
            foreach (var sh in d.Shares) h.Shares.Add(new RangeShare { Name = sh.Name, Path = sh.Path, Comment = sh.Comment, Anonymous = sh.Anonymous, Users = new List<string>(sh.Users) });
            h.Private.AddRange(d.Private); h.Writable.AddRange(d.Writable); h.DomainAdmins.AddRange(d.DomainAdmins);
            Net.Hosts.Add(h);
            var sb = new StringBuilder();
            foreach (var a in h.Accounts.Where(x => !x.User.Equals("Administrator", StringComparison.OrdinalIgnoreCase)))
            {
                sb.Append("New-LocalUser -Name '" + a.User + "' -Password (ConvertTo-SecureString '" + a.Password.Replace("'", "''") + "' -AsPlainText -Force) | Out-Null\n");
                if (a.Admin) sb.Append("Add-LocalGroupMember -Group Administrators -Member '" + a.User + "'\n");
                else if (a.Remote) sb.Append("Add-LocalGroupMember -Group 'Remote Management Users' -Member '" + a.User + "' -ErrorAction SilentlyContinue\n");
            }
            foreach (var a in h.Accounts.Select(x => x.User).Concat(h.DomainJoined ? Net.DomainAccounts.Select(x => x.User) : new string[0]).Distinct(StringComparer.OrdinalIgnoreCase))
                sb.Append("New-Item -ItemType Directory -Path 'C:\\Users\\" + a + "\\Desktop','C:\\Users\\" + a + "\\Documents' -Force | Out-Null\n");
            foreach (var sh in h.Shares) sb.Append("New-Item -ItemType Directory -Path '" + sh.Path + "' -Force | Out-Null\n");
            RunPs(pm, sb.ToString());
            if (d.Setup.Length > 0) RunPs(pm, F(d.Setup));
            // pretend event-log history for blue team
            foreach (var ev in d.Events)
            {
                // a seeded 1102 ("the audit log was cleared") really clears that log first: only it and later events remain
                if (ev.Id == 1102) { var lg = pm.EvLog(ev.Log); if (lg != null) lg.Events.Clear(); }
                for (int n = 0; n < Math.Max(1, ev.Repeat); n++)
                {
                    var at = Net.Ps.Now.AddSeconds(-(ev.Ago - (long)n * ev.Every));
                    pm.EventAt(at, ev.Log, ev.Provider, ev.Id, ev.Level, F(ev.Message), F(ev.User));
                }
            }
            pm.Access = h.Allowed;
            pm.ServiceStarted = s => RunServiceScript(pm, s);
        }

        /// <summary>A service someone installed started (the range's hook): if its program is Windows PowerShell with a
        /// script, the script runs now as the service's account (LocalSystem), so a script anyone may change runs with
        /// the system's rights - the pretend mechanic behind "a service whose program folder is writable". Other
        /// programs only pretend to run.</summary>
        static void RunServiceScript(PsMachine pm, WinService s)
        {
            string bin = (s.BinaryPath ?? "").Trim();
            string exe = PsMachine.ExeOf(bin);
            string bare = exe.Substring(exe.LastIndexOf('\\') + 1);
            if (!bare.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) && !bare.Equals("powershell", StringComparison.OrdinalIgnoreCase)) return;
            string args = bin.StartsWith("\"", StringComparison.Ordinal) ? bin.Substring(Math.Min(bin.Length, exe.Length + 2)) : bin.Substring(Math.Min(bin.Length, exe.Length));
            var acting = PsMachine.Acting;
            PsMachine.Acting = null;   // the service runs as its own account, not as the person who restarted it
            try { pm.RunPowerShellCommandLine(args.Trim(), "SYSTEM", null, true); }
            catch (Exception) { }
            finally { PsMachine.Acting = acting; }
        }

        void RunPs(PsMachine m, string script)
        {
            var host = new NullHost();
            var r = new PsRunspace(m, host, "Administrator");
            var errs = new List<string>();
            var st = new Streams { Err = e => errs.Add(PsRunspace.ErrorText(e)) };
            try { r.RunScript(script, "", st); }
            catch (Exception e) { errs.Add(e.Message); }
            m.Sessions.Remove(r);
            SetupLog.Add(m.Name + ": " + (errs.Count == 0 ? "ok" : string.Join(" | ", errs.ToArray())));
        }

        void PlaceWindowsFlag(RangeHost h, RangeFlagDef f)
        {
            string dir = f.Path.Substring(0, f.Path.LastIndexOf('\\'));
            RunPs(h.Ps, "New-Item -ItemType Directory -Path '" + dir + "' -Force | Out-Null\nSet-Content -Path '" + f.Path + "' -Value '" + Flags[f.Id] + "'\n");
        }

        void BuildPuzzle()
        {
            var lab = NewLab();
            var d = new RangeMachineDef { Name = "puzzlebox", Hostname = "puzzlebox.range.example", Nics = new List<string> { "10.99.0.10/24" }, Packages = new List<string>(Pz.Packages) };
            // every puzzle box has the hex tools (xxd comes with vim-common): Reading hex's hints use xxd and it wasn't there
            if (!d.Packages.Contains("vim-common")) d.Packages.Add("vim-common");
            var m = NewLinux(lab, d, 0);
            m.Boot(Machine.Kernels[0], m.DefaultArgs(), false);
            Prepare(m, d);
            AfterBoot(m, d);
            // the console's account: "player", or (the Linux Ladder) this step's own account
            Run(m, "useradd -m -c 'Puzzle player' " + Pz.Login + "\necho '" + Pz.Login + ":" + RangeData.Hex("pw" + Seed, 12) + "' | chpasswd\n");
            if (Pz.Category == "ladder") m.Banner = LadderBanner();
            RangeTools.SetUpAttackBox(m);
            // a puzzle may have its own pretend web apps (web topic)
            foreach (var w in Pz.Web) Net.WebApps.Add(w);
            Run(m, F(Pz.Setup));
            if (Pz.IsWindows) BuildPuzzleWindows(lab, m);
            Net.Events.Clear(); Net.Seen.Clear(); Net.Worked.Clear();
        }

        /// <summary>The puzzle's address on the range network for a Windows puzzle, and its account.</summary>
        public const string WinPuzzleIp = "10.99.0.20", WinPuzzleName = "WINBOX";
        public string WinPuzzlePassword { get { return "Pz-" + RangeData.Hex("winbox|" + Seed, 8); } }

        /// <summary>A Windows puzzle: a pretend Windows box (backed by the PowerShell course's simulator) next to the
        /// puzzle box, with the account "player" (a standard user unless the puzzle says winadmin: yes), the puzzle's
        /// PowerShell setup and event-log history. The console opens a remote shell to it (StartConsole).</summary>
        void BuildPuzzleWindows(LabNet lab, Machine linux)
        {
            PsEnums.Init();
            Net.Ps = new PsNet { Seed = Seed, Rng = new Random(Seed) };
            Net.Ps.Now = new DateTime(1970, 1, 1).AddSeconds(lab.Now);
            var d = new RangeMachineDef { Name = WinPuzzleName, Os = "windows", Server = false, Hostname = "winbox.range.example", Role = "the puzzle's Windows box", Nics = new List<string> { WinPuzzleIp + "/24" }, Ports = new List<int> { 135, 445, 5985 } };
            d.Accounts.Add(new RangeAccount { User = "Administrator", Password = "Adm-" + RangeData.Hex("winadm|" + Seed, 12), Admin = true });
            d.Accounts.Add(new RangeAccount { User = "player", Password = WinPuzzlePassword, Admin = Pz.WinAdmin, Remote = true });
            d.Setup = Pz.WinSetup;
            d.Events.AddRange(Pz.WinEvents);
            BuildWindows(d);
            Run(linux, "echo '" + WinPuzzleIp + " winbox winbox.range.example' >> /etc/hosts\nprintf 'The puzzle'\"'\"'s Windows box: winbox (" + WinPuzzleIp + ")\\nuser: player\\npassword: " + WinPuzzlePassword + "\\nConnect again with: winrm-shell -i winbox -u player -p " + WinPuzzlePassword + "\\n' > /home/player/winbox.txt\nchown player: /home/player/winbox.txt\n");
        }

        /// <summary>Starts the player's console: root on the scenario's own box, the user "player" on a puzzle box.</summary>
        public void StartConsole()
        {
            var s = Sim.Console(StartMachine);
            s.Tty = "pts/0";
            s.AutoLogin = Pz != null ? Pz.Login : "root";
            if (!s.Waiting && !s.Finished) s.Start();
            // a Windows puzzle opens straight into a remote PowerShell session on its Windows box
            if (Pz != null && Pz.IsWindows && !winOpened) { winOpened = true; Sim.Type(StartMachine, "winrm-shell -i winbox -u player -p " + WinPuzzlePassword); }
        }
        bool winOpened;

        // ------------------------------------------------------------ playing
        /// <summary>Checks a submitted flag (or a puzzle's answer). Returns a sentence for the person.</summary>
        public string Submit(string text, out bool right)
        {
            right = false;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return "Type or paste a flag first.";
            if (Pz != null)
            {
                bool ok = Pz.Answer == "flag" ? t == Flags[""] : string.Equals(RangeData.NormalAnswer(t), RangeData.NormalAnswer(RangeData.Fill(Pz.Answer, Vars, Flags)), StringComparison.OrdinalIgnoreCase);
                if (!ok) return Pz.Answer == "flag" ? "That is not this puzzle's flag." : "That is not the answer. Look again.";
                right = true;
                if (Found.Count == 0) Found.Add("flag");
                Finished = true;
                return "Solved! " + Pz.Points + " points.";
            }
            foreach (var f in Sc.Flags)
            {
                if (Flags[f.Id] != t) continue;
                right = true;
                if (Found.Contains(f.Id)) return "You already captured that flag (" + f.Name + ").";
                Found.Add(f.Id);
                var host = Sc.Machines.FirstOrDefault(m => m.Name == f.Machine);
                if (host != null)
                {
                    SeenHost sh;
                    if (!Net.Seen.TryGetValue(host.FirstIp, out sh)) { sh = new SeenHost { Ip = host.FirstIp }; Net.Seen[host.FirstIp] = sh; }
                    if (sh.Name.Length == 0) sh.Name = host.Os == "windows" ? host.Name.ToUpperInvariant() : (host.Hostname.Length > 0 ? host.Hostname.Split('.')[0] : host.Name);
                    if (sh.Os.Length == 0) sh.Os = host.Os == "windows" ? "Windows" : "Linux";
                    if (f.Level == "admin") sh.Owned = "admin"; else if (sh.Owned == "") sh.Owned = "user";
                }
                if (AllFound) Finished = true;
                return "Flag captured: " + f.Name + " (+" + f.Points + " points)." + (AllFound ? " That was the last one!" : "");
            }
            if (t.StartsWith("FLAG{", StringComparison.Ordinal) && t.EndsWith("}", StringComparison.Ordinal)) return "That flag is not one of this run's flags. (Flags change every run.)";
            return "That doesn't look like a flag. Flags look like FLAG{...}.";
        }

        /// <summary>Blue team: answer a question. Returns (ok, sentence).</summary>
        public string Answer(RangeQuestion q, string text, out bool right)
        {
            right = false;
            if (Answered.ContainsKey(q.Id)) { right = true; return "You already answered that."; }
            string got = RangeData.NormalAnswer(text);
            if (got.Length == 0) return "Type your answer first.";
            var accept = new List<string> { q.Answer };
            accept.AddRange(q.Accept);
            // a verdict question (real or false alarm) takes the usual ways of saying either
            string want = RangeData.Verdict(RangeData.Fill(q.Answer, Vars, Flags));
            bool ok = accept.Any(a => RangeData.NormalAnswer(RangeData.Fill(a, Vars, Flags)) == got) || (want != null && RangeData.Verdict(got) == want);
            if (ok)
            {
                right = true; Answered[q.Id] = got;
                if (AllFound) Finished = true;
                return "Correct. +" + q.Points + " points." + (AllFound ? " That was the last one!" : "");
            }
            if (want != null && RangeData.Verdict(got) == null) return "Answer with \"real\" or \"false alarm\".";
            if (q.Tries > 0)
            {
                int n; WrongTries.TryGetValue(q.Id, out n); WrongTries[q.Id] = ++n;
                if (n >= q.Tries)
                {
                    // out of tries: the question closes with no points, and the answer is shown (a decision, not a guessing game)
                    Answered[q.Id] = got; Missed.Add(q.Id);
                    if (AllFound) Finished = true;
                    return "Not right, and that was your " + (q.Tries == 1 ? "only try" : "last try") + " for this one (no points). The answer was: " + RangeData.Fill(q.Answer, Vars, Flags) + ".";
                }
                return "Not quite. " + (q.Tries - n) + " " + (q.Tries - n == 1 ? "try" : "tries") + " left for this one.";
            }
            return "Not quite. Look again in the logs.";
        }

        /// <summary>Blue team: run a task's check script as root on its machine. Returns (done, sentence).</summary>
        public string CheckTask(RangeTask t, out bool done)
        {
            done = TasksDone.Contains(t.Id);
            if (done) return "Already done.";
            string machine = F(t.Machine);
            var m = Net.Lab.Get(machine);
            int status;
            if (m != null)
            {
                var r = m.RunCommand("root", "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin\n" + F(t.Check), null, 400000, null);
                status = r.Status;
            }
            else
            {
                var h = Net.HostByName(machine);
                if (h == null) return "That machine isn't in this scenario.";
                status = CheckPs(h.Ps, F(t.Check));
            }
            if (status == 0) { done = true; TasksDone.Add(t.Id); if (AllFound) Finished = true; return t.Name + ": done. +" + t.Points + " points."; }
            return t.Name + ": not yet. " + (t.Explain.Length > 0 ? "" : "Make the fix on " + t.Machine + " first.");
        }

        int CheckPs(PsMachine m, string script)
        {
            var host = new NullHost();
            var r = new PsRunspace(m, host, "Administrator");
            int code = 1;
            var st = new Streams { };
            try { r.RunScript(script + "\nif ($LASTEXITCODE -ne $null) { exit $LASTEXITCODE }", "", st); code = r.LastExit; }
            catch (Exception) { code = 1; }
            m.Sessions.Remove(r);
            return code;
        }

        public string TakeHint(int i)
        {
            if (i < 0 || i >= Hints.Count) return "";
            if (!HintsUsed.Contains(i)) HintsUsed.Add(i);
            return Hints[i].Text;
        }

        public void Tick(long seconds)
        {
            if (Finished || GaveUp) return;
            Seconds += seconds;
            if (Sim != null && Sim.Idle) Net.Lab.Advance(seconds);
            RunAttacker();
        }

        /// <summary>Blue team: the simulated attacker's steps whose time has come, unless the player stopped them.</summary>
        public void RunAttacker()
        {
            if (!IsBlue || Sim == null || !Sim.Idle) return;
            long now = Net.Lab.Now - StartClock;
            while (nextStep < Sc.Attacker.Count && Sc.Attacker[nextStep].At <= now)
            {
                var step = Sc.Attacker[nextStep++];
                var m = Net.Lab.Get(step.Machine);
                bool stopped = false;
                int done = nextStep - 1;
                if (Replay != null && done < Replay.Count) stopped = Replay[done] == "blocked";   // a run brought back: as it went before
                else if (step.Unless.Length > 0 && m != null)
                    stopped = m.RunCommand("root", "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin\n" + F(step.Unless), null, 200000, null).Status == 0;
                else if (step.Unless.Length > 0)
                {
                    // a pretend Windows host: the test is PowerShell ending in exit 0 (already blocked) / exit 1
                    var wh = Net.HostByName(step.Machine);
                    if (wh != null) stopped = CheckPs(wh.Ps, F(step.Unless)) == 0;
                }
                AttackerSteps.Add(stopped ? "blocked" : "ran");
                if (stopped) { AttackerLog.Add("[blocked] " + F(step.Note)); continue; }
                if (m != null && step.Run.Length > 0) m.RunCommand("root", "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin\n" + F(step.Run), null, 400000, null);
                else if (step.Run.Length > 0) { var h = Net.HostByName(step.Machine); if (h != null) CheckPs(h.Ps, F(step.Run)); }
                AttackerLog.Add(F(step.Note));
                if (step.Goal) Breached = true;
            }
        }
        long StartClock;
        /// <summary>How each attacker step went ("ran" / "blocked"), in order (kept for an App Hub update: RangeResume.cs).</summary>
        public List<string> AttackerSteps = new List<string>();
        /// <summary>While a run is brought back: the steps' outcomes from before, replayed instead of checked.</summary>
        public List<string> Replay;
        /// <summary>Seconds on the scenario's clock since the run started.</summary>
        public long ClockElapsed { get { return Net == null || Net.Lab == null ? 0 : Net.Lab.Now - StartClock; } }

        public void Dispose() { if (Sim != null) Sim.Dispose(); }

        public static string TimeText(long s) { return s >= 3600 ? (s / 3600) + ":" + (s % 3600 / 60).ToString("00") + ":" + (s % 60).ToString("00") : (s / 60) + ":" + (s % 60).ToString("00"); }
    }
}
