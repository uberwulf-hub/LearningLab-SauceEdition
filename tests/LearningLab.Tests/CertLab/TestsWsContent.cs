// Cert Lab - checks of the AZ-800 course's content: the files parse; objectives, decks and sheets line up; every console
// line of every setup and model solution starts with a known action and every PowerShell command exists; every check
// line parses; every try-it and lab task fails before the work and passes after its model solution; every practice
// exam goes from 0 to the full score on two seeds; exams have Microsoft's item kinds; lesson terminal examples print
// what the simulator prints (WsFillExamples fills them while authoring).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static CourseInfo wsCourse;
        static CourseInfo WsCourse()
        {
            if (wsCourse == null && AppRootForTests != null) wsCourse = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses")).FirstOrDefault(c => c.Id == "az800");
            return wsCourse;
        }
        static readonly HashSet<string> WsVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "new", "set", "move", "rename", "delete", "restore", "members", "password", "unlock", "replicate", "fsmo", "enable", "upnsuffix", "trust", "gpo", "wmi", "inheritance", "gpupdate", "centralstore", "dns", "dhcp", "wac", "feature", "service", "share", "ntfs", "vm", "vswitch", "dfsn", "dfsr", "storage", "arc", "update", "azure", "connect", "cloudsync", "filesync" };

        static partial void WsGroupsMore2(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ws-content", WsContentTests));
            l.Add(new KeyValuePair<string, Action>("ws-tries", () => { var c = WsCourse(); if (c == null) return; foreach (var x in c.AllLessons.Where(y => y.Try != null && (string.IsNullOrEmpty(OnlyFile) || y.Id.StartsWith(OnlyFile)))) { x.Try.Id = x.Id; WsRunSet(x.Try, 3, false, "try " + x.Id); } }));
            l.Add(new KeyValuePair<string, Action>("ws-labs", () => { var c = WsCourse(); if (c != null) foreach (var lab in c.Labs.Where(x => Wanted(x.File))) WsRunSet(lab, 5, false, "lab " + lab.Id); }));
            l.Add(new KeyValuePair<string, Action>("ws-exams", () => { var c = WsCourse(); if (c != null) foreach (var ex in c.Exams.Where(x => Wanted(x.File))) { WsRunSet(ex, 11, true, "exam " + ex.Id + " (seed 11)"); WsRunSet(ex, 29, true, "exam " + ex.Id + " (seed 29)"); } }));
        }

        /// <summary>A try-it, lab or exam on a fresh network: each task fails first and passes after its model solution.</summary>
        static void WsRunSet(TaskSet set, int seed, bool exam, string label)
        {
            var inst = set.Instance(seed);
            var sim = WsSim.Create(seed);
            try
            {
                TaskRunner.Prepare(sim, inst);
                Check(label + ": setup lines work", sim.Errors.Count == 0, string.Join("\n", sim.Errors.ToArray()));
                sim.Errors.Clear();
                var before = TaskRunner.Grade(sim, inst, false, null, null);
                foreach (var ir in before.Items.Where(x => x.Item.Kind == "task"))
                    Check(label + " " + ir.Item.Id + " fails before the work is done", !ir.Pass, string.Join("\n", ir.Checks.Where(x => x.Pass).Select(x => "passes already: " + x.Desc).ToArray()));
                if (exam) Check(label + " scores 0 at the start", before.Score == 0, before.Score.ToString());
                var typed = new StringBuilder();
                var psErrors = new List<string>();
                foreach (var it in inst.Items.Where(x => x.Kind == "task"))
                    foreach (var s in it.Solution)
                    {
                        sim.Type(s.Machine, s.Line);
                        typed.Append(s.Machine + "> " + s.Line + "\n");
                        if (s.Machine != WsSim.Admin && !s.Line.StartsWith("#")) { var con = sim.Console(s.Machine); if (con != null) { var o = con.TakeOutput(); typed.Append(o); if (o.Contains("FullyQualifiedErrorId")) psErrors.Add(it.Id + " " + s.Machine + "> " + s.Line + "\n" + o); } }
                        else if (s.Machine == WsSim.Admin) typed.Append("   " + sim.LastResult + "\n");
                    }
                Check(label + ": every solution line works", sim.Errors.Count == 0 && psErrors.Count == 0, string.Join("\n", sim.Errors.Concat(psErrors).ToArray()));
                var ch = new Dictionary<string, int>(); var more = new Dictionary<string, List<string>>();
                TaskRunner.ModelAnswers(inst, ch, more);
                var after = TaskRunner.Grade(sim, inst, exam && inst.RebootFirst, ch, more);
                foreach (var ir in after.Items)
                {
                    var bad = ir.Checks.Where(x => !x.Pass).ToList();
                    Check(label + " " + ir.Item.Id + " passes with its model solution", ir.Pass, string.Join("\n", bad.Select(x => "failed: " + x.Desc + " [" + x.Machine + "]\n" + x.Output.Trim()).ToArray()) + "\n--- typed (end) ---\n" + Tail(typed.ToString(), 2500));
                }
                if (exam) Check(label + " scores the maximum with the model answers", after.Score == after.Max, after.Score + "/" + after.Max);
            }
            finally { sim.Dispose(); }
        }

        static void WsContentTests()
        {
            var c = WsCourse();
            Check("the AZ-800 course loads", c != null);
            if (c == null) return;
            Check("AZ-800 course files parse", c.Problems.Count == 0, string.Join("\n", c.Problems.ToArray()));
            Check("every unit has lessons", c.Units.All(u => u.Lessons.Count >= 2), string.Join(", ", c.Units.Where(u => u.Lessons.Count < 2).Select(u => u.Id).ToArray()));
            Check("lesson ids are unique", c.AllLessons.Select(x => x.Id).Distinct().Count() == c.AllLessons.Count());
            var objectives = new HashSet<string>(c.Objectives);
            Check("units use known objectives", c.Units.All(u => objectives.Contains(u.Objective)));
            foreach (var l in c.AllLessons)
            {
                Check(l.Id + " has a quiz", l.Quiz.Count >= 2, l.Quiz.Count.ToString());
                foreach (var q in l.Quiz) Check(l.Id + " quiz '" + q.Q + "' has one right answer and a reason", q.Answer >= 0 && q.Choices.Count >= 3 && q.Why.Length > 0);
            }
            Check("every objective has a deck", c.Objectives.All(o => c.Decks.Any(d => d.Objective == o)), string.Join(", ", c.Objectives.Where(o => !c.Decks.Any(d => d.Objective == o)).ToArray()));
            Check("every objective has a cheat sheet", c.Objectives.All(o => c.Sheets.Any(s => s.Objective == o)), string.Join(", ", c.Objectives.Where(o => !c.Sheets.Any(s => s.Objective == o)).ToArray()));
            foreach (var d in c.Decks) { Check("deck " + d.Id + " objective is known", objectives.Contains(d.Objective), d.Objective); Check("deck " + d.Id + " has cards", d.Cards.Count >= 10, d.Cards.Count.ToString()); }
            foreach (var s in c.Sheets) Check("sheet " + s.Id + " objective is known", objectives.Contains(s.Objective), s.Objective);
            Check("labs: at least one per unit", c.Units.All(u => c.Labs.Any(x => x.Unit == u.Id)), string.Join(", ", c.Units.Where(u => !c.Labs.Any(x => x.Unit == u.Id)).Select(u => u.Id).ToArray()));
            var sim = WsSim.Create(1);
            var machines = new HashSet<string>(sim.MachineNames, StringComparer.OrdinalIgnoreCase) { WsSim.Admin };
            var r = new PsRunspace(sim.Lab.M("DC1"), new NullHost(), "Administrator");
            PsCmdlets.Init();
            var sets = c.Labs.Concat(c.Exams).Concat(c.AllLessons.Where(l => l.Try != null).Select(l => { l.Try.File = l.Id; return l.Try; })).ToList();
            foreach (var t in sets)
            {
                string where = Path.GetFileName(t.File);
                Func<string, string> fill = s => { foreach (var kv in t.Vars) if (kv.Value.Count > 0) s = s.Replace("{" + kv.Key + "}", kv.Value[0]); return s; };
                foreach (var st in t.Setup) WsLineOk(where + " setup", st, machines, r, fill);
                foreach (var it in t.Items)
                {
                    if (it.Kind != "task") continue;
                    Check(where + " " + it.Id + ": has checks, a solution" + (t.File.Contains("exam") ? " and an explanation" : ""), it.Checks.Count > 0 && it.Solution.Count > 0 && (!t.File.Contains("exam") || it.Explain.Length > 0));
                    Check(where + " " + it.Id + ": objective is known", objectives.Contains(it.Objective), it.Objective);
                    foreach (var st in it.Solution) WsLineOk(where + " " + it.Id, st, machines, r, fill);
                    foreach (var ck in it.Checks)
                    {
                        Check(where + " " + it.Id + ": check machine " + ck.Machine + " exists", machines.Contains(ck.Machine));
                        if (ck.Machine != WsSim.Admin) continue;
                        foreach (var line in fill(ck.Script).Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0 && !x.Trim().StartsWith("#")))
                        {
                            string found; bool okLine = true;
                            try { WsCheck.Line(sim.Lab, line.Trim(), out found); } catch (Exception e) { okLine = false; found = e.Message; }
                            Check(where + " " + it.Id + ": check line parses: " + line.Trim(), okLine, found);
                        }
                    }
                }
            }
            sim.Dispose();
            var ix = SearchIndex.Build(c);
            var none = new HashSet<string>();
            Check("search finds Group Policy loopback in the lessons", ix.Find("loopback", SearchIndex.Allows(none), 50).Any(h => h.Doc.Kind == "lesson"));
            Check("search finds FSMO in the flash cards", ix.Find("PDC emulator", SearchIndex.Allows(none), 200).Any(h => h.Doc.Kind == "card"));
            var cs = c.Exams.SelectMany(e => e.Cases).FirstOrDefault();
            if (cs != null) Check("search finds a case study", ix.Find("\"" + cs.Title + "\"", SearchIndex.Allows(none), 50).Any(h => h.Doc.Context.Contains("case study")));
            EpExamShape(c, objectives);
            WsExampleTests(c);
        }

        static void WsLineOk(string where, SolutionStep st, HashSet<string> machines, PsRunspace r, Func<string, string> fill)
        {
            Check(where + ": machine " + st.Machine + " exists", machines.Contains(st.Machine), st.Machine);
            string line = fill(st.Line).Trim();
            if (line.StartsWith("#wait ")) return;
            if (st.Machine == WsSim.Admin) { var verb = line.Split(' ')[0]; Check(where + ": console line starts with a known action: " + line, WsVerbs.Contains(verb), verb); return; }
            if (line.StartsWith("#")) { Check(where + ": known directive: " + line, Regex.IsMatch(line, "^#(power (on|off)|reboot|renew)$"), line); return; }
            foreach (var cmd in PsCommandsIn(line))
                if (Regex.IsMatch(cmd, "^[A-Za-z][\\w.-]*$") && (cmd.Contains("-") || PsNatives.Get(cmd) != null))
                    Check(where + ": '" + cmd + "' exists in the simulated PowerShell", PsKnown(r, cmd), cmd);
        }
    }
}
