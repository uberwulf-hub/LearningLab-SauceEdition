// Cert Lab - tests of the course content: every file parses, every command a card, sheet or solution uses exists
// in the simulator and has a manual page, every lesson's try-it, lab and practice exam fails before the work is
// done and passes once the model solution is typed into the consoles (exams: after the graders' reboot).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static CourseInfo course;

        static void AddGroups(List<KeyValuePair<string, Action>> g, string appRoot, bool quick)
        {
            course = CourseLoader.LoadAll(Path.Combine(appRoot, "Courses")).FirstOrDefault(c => c.Id == "rhcsa");
            foreach (var e in EngineGroups()) g.Add(e);
            PsHelp.Dir = Path.Combine(appRoot, "Courses", "powershell", "help");
            AppRootForTests = appRoot;
            foreach (var e in PsGroups()) g.Add(e);
            foreach (var e in EpGroups()) g.Add(e);
            foreach (var e in WsGroups()) g.Add(e);   // TestsWs*.cs: the AZ-800 course
            foreach (var e in SecGroups()) g.Add(e);  // TestsSec*.cs: the Security+ course
            g.Add(new KeyValuePair<string, Action>("unpack", UnpackTests));   // TestsLearningLab.cs: the content shipped in the engine
            g.Add(new KeyValuePair<string, Action>("bring-across", BringAcrossTests));   // ...and App Hub's data brought across once
            g.Add(new KeyValuePair<string, Action>("saves", SafeSaveTests));  // TestsSaves.cs: progress and saved machines kept safe
            g.Add(new KeyValuePair<string, Action>("resume", ResumeTests));   // TestsResume.cs: closing for an App Hub update and carrying on
            g.Add(new KeyValuePair<string, Action>("speech", SpeechTests));   // TestsSpeech.cs: reading aloud (a pretend voice)
            g.Add(new KeyValuePair<string, Action>("content", ContentTests));
            g.Add(new KeyValuePair<string, Action>("search", SearchTests));
            g.Add(new KeyValuePair<string, Action>("tries", () => TryTests(quick)));
            g.Add(new KeyValuePair<string, Action>("labs", () => LabTests(quick)));
            g.Add(new KeyValuePair<string, Action>("exams", () => ExamTests(quick)));
            foreach (var e in RangeGroups(quick)) g.Add(e);   // TestsRange*.cs: Cyber Range
        }

        /// <summary>Only the content groups for one file (CertLab.ps1 -Test -Only labs:lab-05 and so on).</summary>
        public static string OnlyFile;

        static readonly HashSet<string> Keywords = new HashSet<string> { "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac", "function", "in", "!", "{", "}", "[[", "]]", "time", "select" };

        /// <summary>The commands a command line runs (first word of each simple command, without assignments, sudo...).</summary>
        static IEnumerable<string> CommandsIn(string line)
        {
            var l = Regex.Replace(line, "'[^']*'|\"[^\"]*\"", "x");
            foreach (var part in Regex.Split(l, "\\|\\||&&|[|;&]|\\$\\(|`|\\(|\\)"))
            {
                var w = part.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (w.Count > 0 && (w[0] == "for" || w[0] == "select" || w[0] == "case")) continue;   // for VAR in WORDS
                // a redirection and its target ("< file", "2> /dev/null") are not commands
                for (int i = 0; i < w.Count; i++) if (Regex.IsMatch(w[i], "^(\\d?[<>]{1,2}|&>|[<>]&)$")) { w.RemoveAt(i); if (i < w.Count) w.RemoveAt(i); i--; }
                while (w.Count > 0 && (Regex.IsMatch(w[0], "^\\w+=") || w[0] == "sudo" || w[0] == "time" || w[0] == "exec" || w[0] == "nohup" || w[0] == "!" || Keywords.Contains(w[0]) || w[0].StartsWith("<") || w[0].StartsWith(">") || Regex.IsMatch(w[0], "^\\d?>")))
                {
                    if (w[0] == "sudo") { w.RemoveAt(0); while (w.Count > 0 && w[0].StartsWith("-")) { bool val = w[0] == "-u" || w[0] == "-g"; w.RemoveAt(0); if (val && w.Count > 0) w.RemoveAt(0); } continue; }
                    w.RemoveAt(0);
                }
                if (w.Count == 0) continue;
                string c = w[0];
                if (c.StartsWith("$") || c.StartsWith("-") || c.Contains("=") || c.StartsWith("/") && !c.StartsWith("/usr/") && !c.StartsWith("/bin/") && !c.StartsWith("/sbin/")) continue;
                if (c.StartsWith("/")) c = Path.GetFileName(c);
                if (Regex.IsMatch(c, "^[a-z][a-z0-9_.+-]*$")) yield return c;
            }
        }

        /// <summary>The solution lines that are commands: not directives, not the body of a here-document, not answers
        /// to prompts that follow ssh / passwd (a line right after a line that asks for input).</summary>
        static IEnumerable<SolutionStep> SolutionCommands(List<SolutionStep> steps)
        {
            string end = null; bool answer = false;
            foreach (var s in steps)
            {
                if (end != null) { if (s.Line.Trim() == end) end = null; continue; }
                if (answer) { answer = false; continue; }
                if (s.Line.StartsWith("#")) continue;
                var m = Regex.Match(s.Line, "<<-?\\s*['\"]?(\\w+)['\"]?");
                if (m.Success) end = m.Groups[1].Value;
                // an interactive ssh login or ssh-copy-id: the next line is the password
                var w = s.Line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (w[0] == "ssh-copy-id" || (w[0] == "ssh" && w.Where((x, i) => i > 0 && !x.StartsWith("-") && !(i > 0 && (w[i - 1] == "-o" || w[i - 1] == "-p" || w[i - 1] == "-i"))).Count() == 1)) answer = true;
                yield return s;
            }
        }

        static bool Known(string cmd)
        {
            Cmd.Init();
            return Sh.Programs.ContainsKey(cmd) || Sh.Builtins.ContainsKey(cmd);
        }

        static void ContentTests()
        {
            Check("the RHCSA course loads", course != null);
            if (course == null) return;
            Check("course files parse", course.Problems.Count == 0, string.Join("\n", course.Problems.ToArray()));
            Check("every unit has lessons", course.Units.All(u => u.Lessons.Count > 0), string.Join(", ", course.Units.Where(u => u.Lessons.Count == 0).Select(u => u.Id).ToArray()));
            Check("lesson ids are unique", course.AllLessons.Select(l => l.Id).Distinct().Count() == course.AllLessons.Count());
            foreach (var l in course.AllLessons)
            {
                foreach (var q in l.Quiz) Check(l.Id + " quiz '" + q.Q + "' has one right answer", q.Answer >= 0 && q.Choices.Count >= 2 && q.Choices.Count(c => c.Length > 0) == q.Choices.Count);
                // commands in the terminal examples
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal"))
                    foreach (var line in b.Text.Split('\n'))
                    {
                        var mm = Regex.Match(line, "^\\[[^\\]]+\\][#$] (.*)$");
                        if (!mm.Success) continue;
                        foreach (var c in CommandsIn(mm.Groups[1].Value)) Check(l.Id + ": the example command '" + c + "' exists in the simulator", Known(c), line);
                    }
            }
            var objectives = new HashSet<string>(course.Objectives);
            Check("every objective has a deck", course.Objectives.All(o => course.Decks.Any(d => d.Objective == o)), string.Join(", ", course.Objectives.Where(o => !course.Decks.Any(d => d.Objective == o)).ToArray()));
            Check("every objective has a cheat sheet", course.Objectives.All(o => course.Sheets.Any(s => s.Objective == o)), string.Join(", ", course.Objectives.Where(o => !course.Sheets.Any(s => s.Objective == o)).ToArray()));
            var noMan = new SortedSet<string>();
            Action<string, string> needMan = (c, where) => { if (Sh.Programs.ContainsKey(c) && Man.Find(c, null) == null) noMan.Add(c + " (" + where + ")"); };
            foreach (var d in course.Decks)
            {
                Check("deck " + d.Id + " objective is known", objectives.Contains(d.Objective), d.Objective);
                Check("deck " + d.Id + " has cards", d.Cards.Count >= 8, d.Cards.Count.ToString());
                foreach (var c in d.Cards.Where(x => x.Command != null))
                    foreach (var w in CommandsIn(c.Command)) { Check("card " + c.Id + ": '" + w + "' exists", Known(w), c.Command); needMan(w, c.Id); }
            }
            foreach (var s in course.Sheets)
            {
                Check("sheet " + s.Id + " objective is known", objectives.Contains(s.Objective), s.Objective);
                foreach (var sec in s.Sections) foreach (var r in sec.Rows)
                    {
                        if (r.Cmd.StartsWith("/") || r.Cmd.Contains(" = ") || !Regex.IsMatch(r.Cmd, "^[a-z]")) continue;   // file paths and settings
                        foreach (var w in CommandsIn(r.Cmd)) { Check("sheet " + s.Id + ": '" + w + "' exists", Known(w), r.Cmd); needMan(w, s.Id); }
                    }
            }
            foreach (var t in course.Labs.Concat(course.Exams))
                foreach (var it in t.Items) foreach (var st in SolutionCommands(it.Solution)) foreach (var w in CommandsIn(st.Line)) { Check(Path.GetFileName(t.File) + " " + it.Id + ": '" + w + "' exists", Known(w), st.Line); needMan(w, Path.GetFileName(t.File)); }
            Check("every command the course uses has a manual page", noMan.Count == 0, string.Join(", ", noMan.ToArray()));
            var noPkg = Sh.Programs.Keys.Where(k => Packages.WhoProvides(k) == null && !k.StartsWith("certlab")).OrderBy(x => x).ToList();
            Check("every program comes from a package", noPkg.Count == 0, string.Join(" ", noPkg.ToArray()));
            var allMissing = Sh.Programs.Keys.Where(k => Man.Find(k, null) == null && !k.StartsWith("certlab")).OrderBy(x => x).ToList();
            Check("every program in the simulator has a manual page", allMissing.Count == 0, allMissing.Count + " without a page: " + string.Join(" ", allMissing.ToArray()));
            Check("labs: at least one per unit", course.Units.All(u => course.Labs.Any(l => l.Unit == u.Id)), string.Join(", ", course.Units.Where(u => !course.Labs.Any(l => l.Unit == u.Id)).Select(u => u.Id).ToArray()));
            Check("at least 4 practice exams", course.Exams.Count >= 4, course.Exams.Count.ToString());
            foreach (var e in course.Exams)
            {
                Check(e.Id + ": 15-22 tasks", e.Items.Count >= 15 && e.Items.Count <= 22, e.Items.Count.ToString());
                Check(e.Id + ": tasks on both machines", e.Items.Any(i => i.Machine == "server1") && e.Items.Any(i => i.Machine == "server2"));
                Check(e.Id + ": every objective is examined", course.Objectives.All(o => e.Items.Any(i => i.Objective == o)), string.Join(", ", course.Objectives.Where(o => !e.Items.Any(i => i.Objective == o)).ToArray()));
                Check(e.Id + ": objectives are known", e.Items.All(i => objectives.Contains(i.Objective)), string.Join(", ", e.Items.Where(i => !objectives.Contains(i.Objective)).Select(i => i.Id + "=" + i.Objective).ToArray()));
                Check(e.Id + ": every task has checks, a solution and an explanation", e.Items.All(i => i.Checks.Count > 0 && i.Solution.Count > 0 && i.Explain.Length > 0), string.Join(", ", e.Items.Where(i => !(i.Checks.Count > 0 && i.Solution.Count > 0 && i.Explain.Length > 0)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": points add up to 300", e.TotalPoints == 300, e.TotalPoints.ToString());
            }
        }

        /// <summary>Runs a task set on a fresh network: every task fails at the start, and passes after its solution.</summary>
        static void RunSet(TaskSet set, int seed, bool exam, string label)
        {
            var inst = set.Instance(seed);
            var sim = RhelSim.Create(Math.Max(1, set.Machines), seed);
            var setupLog = new StringBuilder();
            sim.SetupLog = s => setupLog.Append(s).Append('\n');
            try
            {
                sim.StartConsoles(true, inst.NoLogin);
                foreach (var n in sim.MachineNames) sim.Console(n).TakeOutput();
                TaskRunner.Prepare(sim, inst);
                var before = TaskRunner.Grade(sim, inst, exam, null);
                foreach (var ir in before.Items)
                    Check(label + " " + ir.Item.Id + " fails before the work is done", !ir.Pass, string.Join("\n", ir.Checks.Where(c => c.Pass).Select(c => "passes already: " + c.Desc).ToArray()));
                if (exam) Check(label + " scores 0 at the start", before.Score == 0, before.Score.ToString());
                var typed = new StringBuilder();
                foreach (var it in inst.Items)
                    foreach (var s in it.Solution)
                    {
                        sim.Type(s.Machine, s.Line);
                        var c = sim.Console(s.Machine);
                        string o = c.TakeOutput();
                        typed.Append(s.Machine + "> " + s.Line + "\n" + o);
                        if (typed.Length > 60000) typed.Remove(0, typed.Length - 60000);
                    }
                var after = TaskRunner.Grade(sim, inst, exam, null);
                foreach (var ir in after.Items)
                {
                    var bad = ir.Checks.Where(c => !c.Pass).ToList();
                    Check(label + " " + ir.Item.Id + " passes with its model solution", ir.Pass, string.Join("\n", bad.Select(c => "failed: " + c.Desc + " [" + c.Machine + "]\n" + c.Output.Trim()).ToArray()) + "\n--- console (end) ---\n" + Tail(typed.ToString(), 2500) + (setupLog.Length > 0 ? "\n--- setup ---\n" + Tail(setupLog.ToString(), 1200) : ""));
                }
                if (exam) Check(label + " scores 100% with the model solutions", after.Score == after.Max, after.Score + "/" + after.Max);
            }
            finally { sim.Dispose(); }
        }
        static string Tail(string s, int n) { return s.Length <= n ? s : "..." + s.Substring(s.Length - n); }

        static bool Wanted(string file)
        {
            if (string.IsNullOrEmpty(OnlyFile)) return true;
            return Path.GetFileNameWithoutExtension(file ?? "").StartsWith(OnlyFile) || (file ?? "").Contains(OnlyFile);
        }

        static void TryTests(bool quick)
        {
            if (course == null) return;
            foreach (var l in course.AllLessons.Where(x => x.Try != null))
            {
                if (!string.IsNullOrEmpty(OnlyFile) && !l.Id.StartsWith(OnlyFile)) continue;
                l.Try.Id = l.Id;
                RunSet(l.Try, 3, false, "try " + l.Id);
            }
        }

        static void LabTests(bool quick)
        {
            if (course == null) return;
            foreach (var lab in course.Labs.Where(x => Wanted(x.File))) RunSet(lab, 5, false, "lab " + lab.Id);
        }

        static void ExamTests(bool quick)
        {
            if (course == null) return;
            foreach (var ex in course.Exams.Where(x => Wanted(x.File)))
            {
                RunSet(ex, 11, true, "exam " + ex.Id + " (seed 11)");
                if (!quick) RunSet(ex, 29, true, "exam " + ex.Id + " (seed 29)");
            }
        }
    }
}
