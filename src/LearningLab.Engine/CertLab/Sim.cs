// Cert Lab - the simulator behind a course, through one interface (ISim), so later courses can bring their own
// (PowerShell, Windows...). RhelSim wraps a LabNet with a console session per machine. TaskRunner prepares labs and
// exams (setup), checks the machines' state (after rebooting them for exams) and can type the model solutions into
// the consoles (the tests prove each exam scores 100% that way).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public interface ISim
    {
        string Kind { get; }
        List<string> MachineNames { get; }
        ITerm Term(string machine);
        void StartConsoles(bool autoLogin, IEnumerable<string> noLogin);
        void Type(string machine, string line);
        void Reboot(string machine);
        void Power(string machine);
        bool CanSave { get; }
        void RunSetup(List<SolutionStep> steps);
        bool Check(string machine, string script, out string output);
        void RebootAll();
        void Advance(long seconds);
        string Save();
        string Clock { get; }
        string Status(string machine);
        void Dispose();
        bool Idle { get; }
    }

    public class RhelSim : ISim
    {
        public LabNet Net;
        readonly Dictionary<string, Session> consoles = new Dictionary<string, Session>();
        public string RootPassword = Machine.RootPw;
        public readonly Dictionary<string, string> Passwords = new Dictionary<string, string>();   // machines whose root password a solution changed
        public string PasswordFor(string machine) { string p; return Passwords.TryGetValue(machine, out p) ? p : RootPassword; }
        public string Kind { get { return "rhel"; } }

        public static RhelSim Create(int machines, int seed)
        {
            var s = new RhelSim { Net = LabNet.Create(machines, seed) };
            s.Net.Seed = seed;
            return s;
        }
        public static RhelSim Load(string json)
        {
            var s = new RhelSim { Net = LabNet.Load(json) };
            return s;
        }

        public List<string> MachineNames { get { return Net.Machines.Select(m => m.Name).ToList(); } }
        public ITerm Term(string machine) { return Console(machine); }
        public bool CanSave { get { return true; } }
        public void Reboot(string machine) { var m = M(machine); if (m.State == "off") m.PowerOn(); else m.Shutdown(true); }
        public void Power(string machine) { var m = M(machine); if (m.State == "off") { m.PowerOn(); var cs = Console(machine); if (cs.Waiting) cs.Execute(""); } else m.Shutdown(false); }

        /// <summary>The simulator a course names (rhel or powershell).</summary>
        public static ISim Create(string kind, int machines, int seed)
        {
            if (kind == "powershell") return PsSim.Create(machines, seed);
            if (kind == "endpoint") return EpSim.Create(seed);
            if (kind == "winserver") return WsSim.Create(seed);
            return Create(machines, seed);
        }
        public Machine M(string name) { return Net.Get(name); }

        public Session Console(string machine)
        {
            Session s;
            if (consoles.TryGetValue(machine, out s)) return s;
            var m = Net.Get(machine);
            if (m == null) return null;
            s = new Session(m) { Tty = "tty1", IsConsole = true };
            consoles[machine] = s;
            return s;
        }
        /// <summary>Starts every console, logged in as root (labs and exams begin at a root shell).</summary>
        public void StartConsoles(bool autoLogin) { StartConsoles(autoLogin, null); }
        /// <summary>noLogin: machines whose console starts at the login prompt (an exam where the root password is unknown).</summary>
        public void StartConsoles(bool autoLogin, IEnumerable<string> noLogin)
        {
            foreach (var m in Net.Machines)
            {
                var s = Console(m.Name);
                if (autoLogin && (noLogin == null || !noLogin.Contains(m.Name))) s.AutoLogin = "root";
                s.Start();
            }
        }

        public void RunSetup(List<SolutionStep> steps)
        {
            // consecutive lines for the same machine form one script (if / for / here-documents work)
            int i = 0;
            while (i < steps.Count)
            {
                string mach = steps[i].Machine;
                var sb = new StringBuilder();
                while (i < steps.Count && steps[i].Machine == mach) { sb.Append(steps[i].Line).Append('\n'); i++; }
                var m = Net.Get(mach);
                if (m == null) continue;
                var r = m.RunCommand("root", PathPrefix + sb.ToString(), null, 400000, null);
                if (SetupLog != null) SetupLog(mach + ": exit " + r.Status + (r.Output.Length > 0 ? "\n" + r.Output : ""));
            }
        }
        public Action<string> SetupLog;
        const string PathPrefix = "PATH=/usr/libexec/certlab:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin\n";

        public bool Check(string machine, string script, out string output)
        {
            var m = Net.Get(machine);
            output = "";
            if (m == null) { output = "no machine " + machine; return false; }
            if (m.State == "off" || m.State == "grub") { output = machine + " is not running"; return false; }
            var r = m.RunCommand("root", PathPrefix + script, null, 200000, null);
            output = r.Output;
            return r.Status == 0;
        }

        /// <summary>The graders' reboot: every machine restarts straight to its default entry (no GRUB menu).</summary>
        public void RebootAll()
        {
            foreach (var m in Net.Machines)
            {
                if (m.State == "off") { m.State = "grub"; }
                m.Shutdown(true, true);
            }
        }

        public void Advance(long seconds) { Net.Advance(seconds); }
        public string Save() { return Net.Save(); }
        public string Clock { get { var m = Net.Machines.FirstOrDefault(); return m == null ? "" : Cmd.DateFormat(m, m.Now, "%a %b %e %H:%M", false); } }
        public string Status(string machine)
        {
            var m = Net.Get(machine);
            if (m == null) return "";
            switch (m.State)
            {
                case "running": return m.HostnameNow + " - running (" + Machine.TargetDescription(m.Target) + ")";
                case "grub": return m.Name + " - boot menu";
                case "emergency": return m.Name + " - emergency mode";
                case "rescue": return m.Name + " - rescue mode";
                case "rdbreak": return m.Name + " - stopped in the initramfs (rd.break)";
                case "initbash": return m.Name + " - init=/bin/bash shell";
                case "off": return m.Name + " - switched off";
            }
            return m.Name + " - " + m.State;
        }
        public bool Idle { get { return consoles.Values.All(s => !s.Busy); } }
        public void Dispose() { foreach (var s in consoles.Values) s.Dispose(); consoles.Clear(); }

        /// <summary>Types one line into a machine's console the way a person would, handling the login prompt,
        /// the GRUB menu and the emergency-mode password on the way ("#key e", "#type TEXT", "#password X", "#wait N" steer it).</summary>
        public void Type(string machine, string line)
        {
            var s = Console(machine);
            if (s == null) return;
            if (!s.Waiting) s.Start();
            string l = line;
            if (l.StartsWith("#password ", StringComparison.Ordinal)) { Passwords[machine] = l.Substring(10).Trim(); return; }
            if (l.Trim() == "#reset") { var rm = Net.Get(machine); if (rm.State == "off") rm.PowerOn(); else rm.Shutdown(true); return; }
            if (l.StartsWith("#wait ", StringComparison.Ordinal)) { long n; if (long.TryParse(l.Substring(6).Trim(), out n)) Net.Advance(n); return; }
            if (l.StartsWith("#key ", StringComparison.Ordinal))
            {
                foreach (var k in l.Substring(5).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) PressKey(s, k);
                return;
            }
            if (l.StartsWith("#type ", StringComparison.Ordinal) && s.Grub != null) { s.GrubType(l.Substring(6)); return; }
            if (l.StartsWith("#", StringComparison.Ordinal) && !l.StartsWith("#!", StringComparison.Ordinal)) { if (l.StartsWith("#type ", StringComparison.Ordinal)) s.Execute(l.Substring(6)); return; }
            for (int guard = 0; guard < 8; guard++)
            {
                string p = s.Prompt;
                if (s.Grub != null && s.Grub.Mode == "menu") { s.GrubKey("Enter"); continue; }
                if (p.EndsWith(" login: ", StringComparison.Ordinal)) { s.Execute("root"); loginTyped.Add(machine); continue; }
                if (p == "Password: " && s.Secret && loginTyped.Remove(machine)) { s.Execute(PasswordFor(machine)); continue; }
                if (p.Contains("Give root password for maintenance")) { s.Execute(PasswordFor(machine)); continue; }
                if (p.StartsWith("Press Enter for maintenance", StringComparison.Ordinal)) { s.Execute(""); continue; }
                if (p.Contains("Press Enter to switch it on") || (s.M.State == "off" && p.Length == 0)) { s.Execute(""); continue; }
                break;
            }
            s.Execute(l);
        }
        /// <summary>"#key NAME": one key as the keyboard sends it. On the GRUB screen a single character is typed
        /// (e, c), anything else is a key name (Up, End, Esc, Ctrl+X, F10...); elsewhere Ctrl+C, Ctrl+D and Enter.</summary>
        static void PressKey(Session s, string k)
        {
            if (s.Grub != null)
            {
                if (k.Length == 1) s.GrubType(k); else s.GrubKey(k);
                return;
            }
            if (k == "Ctrl+C") s.Break();
            else if (k == "Ctrl+D") s.Eof();
            else if (k == "Enter") s.Execute("");
        }

        /// <summary>How a solution line reads for a person ("show me" and the exam review), or null to hide it.</summary>
        public static string SolutionText(string line)
        {
            if (!line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("#!", StringComparison.Ordinal)) return line;
            if (line.Trim() == "#reset") return "(reset the machine: its Restart / Reboot button)";
            if (line.StartsWith("#key ", StringComparison.Ordinal)) return "(press " + line.Substring(5).Trim() + ")";
            if (line.StartsWith("#type ", StringComparison.Ordinal)) return "(type \"" + line.Substring(6) + "\")";
            return null;
        }

        readonly HashSet<string> loginTyped = new HashSet<string>();   // machines where Type just answered "login:" (only then is "Password:" the login's)
    }

    public class CheckResult { public string Desc = "", Machine = "", Output = ""; public bool Pass; }
    public class ItemResult { public TaskItem Item; public bool Pass; public List<CheckResult> Checks = new List<CheckResult>(); public int Points; public string Picked = ""; public PbqResult Pbq; }
    public class GradeReport
    {
        public List<ItemResult> Items = new List<ItemResult>();
        public int Score, Max;
        public Dictionary<string, int[]> PerObjective = new Dictionary<string, int[]>();
        public int Scaled(int outOf) { return Max == 0 ? 0 : (int)Math.Round(Score * (double)outOf / Max); }
        public int Percent { get { return Max == 0 ? 0 : (int)Math.Round(Score * 100.0 / Max); } }
    }

    public static class TaskRunner
    {
        public static void Prepare(ISim sim, TaskSet t) { sim.RunSetup(t.Setup); }

        public static GradeReport Grade(ISim sim, TaskSet t, bool reboot, Dictionary<string, int> choiceAnswers) { return Grade(sim, t, reboot, choiceAnswers, null); }

        /// <summary>The model answers of every question (tests, and the "right answer" in the review).</summary>
        public static void ModelAnswers(TaskSet t, Dictionary<string, int> choice, Dictionary<string, List<string>> more)
        {
            foreach (var it in t.Items)
            {
                if (it.Kind == "choice") choice[it.Id] = it.Answer;
                else if (it.Kind == "multi") more[it.Id] = it.Answers.Select(a => a.ToString(CultureInfo.InvariantCulture)).ToList();
                else if (it.Kind == "order") more[it.Id] = new List<string>(it.OrderAnswer);
                else if (it.Kind == "yesno") more[it.Id] = it.Truth.Select(x => x ? "Y" : "N").ToList();
                else if (it.Pbq != null) more[it.Id] = PbqGrade.ModelAnswer(it.Pbq);
            }
        }

        /// <summary>Points for a multiple-answer, drag-and-order or Yes / No item: all or nothing, except a Yes / No series,
        /// where each statement is worth its share (Microsoft doesn't publish its rules; this is Cert Lab's).</summary>
        public static int ScoreMore(TaskItem it, List<string> ans, out bool pass)
        {
            pass = false;
            if (it.Pbq != null) { var pr = PbqGrade.Grade(it.Pbq, it.Points, ans); pass = pr.Fraction >= 0.999; return pr.Points; }
            if (ans == null) return 0;
            if (it.Kind == "multi") { var want = it.Answers.Select(a => a.ToString(CultureInfo.InvariantCulture)).OrderBy(x => x).ToList(); pass = ans.OrderBy(x => x).SequenceEqual(want); return pass ? it.Points : 0; }
            if (it.Kind == "order") { pass = ans.SequenceEqual(it.OrderAnswer); return pass ? it.Points : 0; }
            if (it.Kind == "yesno")
            {
                int right = 0;
                for (int i = 0; i < it.Truth.Count; i++) if (i < ans.Count && ans[i] == (it.Truth[i] ? "Y" : "N")) right++;
                pass = right == it.Truth.Count;
                return it.Truth.Count == 0 ? 0 : it.Points * right / it.Truth.Count;
            }
            return 0;
        }

        public static GradeReport Grade(ISim sim, TaskSet t, bool reboot, Dictionary<string, int> choiceAnswers, Dictionary<string, List<string>> more)
        {
            if (reboot && sim != null) sim.RebootAll();
            var rep = new GradeReport();
            foreach (var it in t.Items)
            {
                var ir = new ItemResult { Item = it };
                if (it.Kind == "multi" || it.Kind == "order" || it.Kind == "yesno" || it.Pbq != null)
                {
                    List<string> ans = null; if (more != null) more.TryGetValue(it.Id, out ans);
                    bool p; int pts = ScoreMore(it, ans, out p);
                    ir.Pass = p; ir.Points = pts;
                    if (it.Pbq != null) { ir.Pbq = PbqGrade.Grade(it.Pbq, it.Points, ans); foreach (var part in ir.Pbq.Parts) ir.Checks.Add(new CheckResult { Desc = part.Label, Pass = part.Pass, Output = part.Yours }); }
                    else ir.Checks.Add(new CheckResult { Desc = "Answer", Pass = p });
                    rep.Items.Add(ir); rep.Score += pts; rep.Max += it.Points;
                    string ob = it.Objective.Length > 0 ? it.Objective : "Other";
                    int[] pv0; if (!rep.PerObjective.TryGetValue(ob, out pv0)) { pv0 = new int[2]; rep.PerObjective[ob] = pv0; }
                    pv0[0] += pts; pv0[1] += it.Points;
                    continue;
                }
                if (it.Kind == "choice")
                {
                    int ans; ir.Pass = choiceAnswers != null && choiceAnswers.TryGetValue(it.Id, out ans) && ans == it.Answer;
                    ir.Checks.Add(new CheckResult { Desc = "Answer: " + (it.Answer >= 0 && it.Answer < it.Choices.Count ? it.Choices[it.Answer] : ""), Pass = ir.Pass });
                }
                else
                {
                    bool all = it.Checks.Count > 0;
                    foreach (var c in it.Checks)
                    {
                        string outp;
                        bool ok = sim.Check(c.Machine, c.Script, out outp);
                        ir.Checks.Add(new CheckResult { Desc = c.Desc.Length > 0 ? c.Desc : c.Script, Machine = c.Machine, Pass = ok, Output = outp });
                        if (!ok) all = false;
                    }
                    ir.Pass = all;
                }
                ir.Points = ir.Pass ? it.Points : 0;
                rep.Items.Add(ir);
                rep.Score += ir.Points; rep.Max += it.Points;
                string obj = it.Objective.Length > 0 ? it.Objective : "Other";
                int[] pv; if (!rep.PerObjective.TryGetValue(obj, out pv)) { pv = new int[2]; rep.PerObjective[obj] = pv; }
                pv[0] += ir.Points; pv[1] += it.Points;
            }
            return rep;
        }

        /// <summary>How a solution step reads for a person, for any course (null: not shown).</summary>
        public static string StepText(SolutionStep s)
        {
            if (s.Machine == WsSim.Admin || s.Line.StartsWith("#power", StringComparison.Ordinal) || s.Line.StartsWith("#renew", StringComparison.Ordinal)) return WsSim.SolutionText(s.Machine, s.Line);
            if (s.Machine == EpSim.Portal || s.Line.StartsWith("#settings", StringComparison.Ordinal) || s.Line.StartsWith("#oobe", StringComparison.Ordinal) || s.Line.StartsWith("#portal", StringComparison.Ordinal) || s.Line.StartsWith("#laps", StringComparison.Ordinal)) return EpSim.SolutionText(s.Machine, s.Line);
            return RhelSim.SolutionText(s.Line);
        }
        /// <summary>"On server1:", "On RB-LT-001:", "In the practice portal:".</summary>
        /// <summary>How a task's machine is named in the exam ("practice portal", "practice consoles", "DC1").</summary>
        public static string MachineLabel(string machine) { return machine == EpSim.Portal ? "practice portal" : machine == WsSim.Admin ? "practice consoles" : machine; }
        public static string Where(string machine) { return machine == EpSim.Portal ? "In the practice portal:" : machine == WsSim.Admin ? "In the practice consoles:" : "On " + machine + ":"; }
        public static string SolutionBlock(IEnumerable<SolutionStep> steps) { return string.Join("\n", steps.Select(StepText).Where(x => x != null).ToArray()); }

        /// <summary>Types every task's model solution into the consoles (tests and the "show me" review).</summary>
        public static void TypeSolutions(ISim sim, TaskSet t)
        {
            foreach (var it in t.Items)
                foreach (var s in it.Solution)
                    sim.Type(s.Machine, s.Line);
        }
    }
}
