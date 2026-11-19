// Cert Lab - checks of the MD-102 course's content: every file parses; objectives, decks and sheets line up; every
// portal line of every setup and model solution is accepted by the portal; every try-it and lab task fails before the
// work and passes after the model solution; every practice exam goes from 0 to the full score with the model answers on
// two seeds; exams have the item kinds of Microsoft's exams; lesson terminal examples print what the simulator prints.
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
        static CourseInfo epCourse;
        static CourseInfo EpCourse()
        {
            if (epCourse == null && AppRootForTests != null) epCourse = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses")).FirstOrDefault(c => c.Id == "md102");
            return epCourse;
        }

        static partial void EpGroupsMore(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ep-kinds", EpKindTests));
            l.Add(new KeyValuePair<string, Action>("ep-content", EpContentTests));
            l.Add(new KeyValuePair<string, Action>("ep-tries", EpTryTests));
            l.Add(new KeyValuePair<string, Action>("ep-labs", () => { var c = EpCourse(); if (c != null) foreach (var lab in c.Labs.Where(x => Wanted(x.File))) EpRunSet(lab, 5, false, "lab " + lab.Id); }));
            l.Add(new KeyValuePair<string, Action>("ep-exams", () => { var c = EpCourse(); if (c != null) foreach (var ex in c.Exams.Where(x => Wanted(x.File))) { EpRunSet(ex, 11, true, "exam " + ex.Id + " (seed 11)"); EpRunSet(ex, 29, true, "exam " + ex.Id + " (seed 29)"); } }));
        }

        /// <summary>The new question kinds parse and score as documented.</summary>
        static void EpKindTests()
        {
            var t = CourseLoader.ParseTaskSet(string.Join("\n", new[] {
                "# Exam: k | Kinds | 60",
                "@case cs1 | Lakeside", "## Overview", "A shop.", "## Requirements", "- Keep it simple.", "@endcase",
                "@multi m1 | Apps | 2 | case=cs1", "Pick two.", "+ a", "- b", "+ c", "! because",
                "@order o1 | Deployment | 3", "In order?", "1 first", "2 second", "3 third", "x never", "! why",
                "@yesno y1 | Identity | 3", "Statements:", "Y one", "N two", "Y three", "! why",
                "@choice c1 | Apps | 1 | case=cs1", "Q?", "- no", "+ yes", "! why" }), "Exam");
            Check("a case study parses with its sections", t.Cases.Count == 1 && t.Cases[0].Sections.Count == 2 && t.Cases[0].Sections[1].Key == "Requirements");
            var m = t.Items[0]; var o = t.Items[1]; var y = t.Items[2]; var c = t.Items[3];
            Check("kinds", m.Kind == "multi" && o.Kind == "order" && y.Kind == "yesno" && c.Kind == "choice");
            Check("questions refer to their case", m.Case == "cs1" && c.Case == "cs1" && t.CaseOf(m) == t.Cases[0]);
            Check("multi keeps every right answer", m.Answers.SequenceEqual(new[] { 0, 2 }));
            Check("order keeps the sequence and the distractor", o.OrderAnswer.SequenceEqual(new[] { "first", "second", "third" }) && o.Choices.Count == 4);
            Check("yesno keeps the truth of each statement", y.Truth.SequenceEqual(new[] { true, false, true }));
            bool p;
            Check("multi: both right scores", TaskRunner.ScoreMore(m, new List<string> { "2", "0" }, out p) == 2 && p);
            Check("multi: one wrong scores 0", TaskRunner.ScoreMore(m, new List<string> { "0", "1" }, out p) == 0 && !p);
            Check("multi: one of two scores 0", TaskRunner.ScoreMore(m, new List<string> { "0" }, out p) == 0);
            Check("order: right sequence scores", TaskRunner.ScoreMore(o, new List<string> { "first", "second", "third" }, out p) == 3 && p);
            Check("order: wrong sequence scores 0", TaskRunner.ScoreMore(o, new List<string> { "second", "first", "third" }, out p) == 0);
            Check("order: a distractor scores 0", TaskRunner.ScoreMore(o, new List<string> { "first", "second", "third", "never" }, out p) == 0);
            Check("yesno: all right scores all", TaskRunner.ScoreMore(y, new List<string> { "Y", "N", "Y" }, out p) == 3 && p);
            Check("yesno: two of three right scores 2", TaskRunner.ScoreMore(y, new List<string> { "Y", "Y", "Y" }, out p) == 2 && !p);
            Check("yesno: unanswered scores 0", TaskRunner.ScoreMore(y, new List<string>(), out p) == 0);
            var inst = t.Instance(7);
            Check("an attempt shuffles the order item's actions but keeps the answer", inst.Items[1].Choices.Count == 4 && inst.Items[1].OrderAnswer.SequenceEqual(o.OrderAnswer) && inst.Cases.Count == 1);
            var ch = new Dictionary<string, int>(); var more = new Dictionary<string, List<string>>();
            TaskRunner.ModelAnswers(inst, ch, more);
            var sim = EpSim.Create(1);
            var rep = TaskRunner.Grade(sim, inst, false, ch, more);
            Check("the model answers score the maximum", rep.Score == rep.Max && rep.Max == 9, rep.Score + "/" + rep.Max);
            var none = TaskRunner.Grade(sim, inst, false, null, null);
            Check("no answers score 0", none.Score == 0);
            sim.Dispose();
            var all = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses"));
            Check("Home lists five courses: RHCSA, PowerShell, MD-102, AZ-800, Security+", string.Join(",", all.Select(x => x.Id).ToArray()) == "rhcsa,powershell,md102,az800,secplus", string.Join(",", all.Select(x => x.Id).ToArray()));
        }

        /// <summary>A try-it, lab or exam on a fresh tenant: each item fails first and passes after its model solution.</summary>
        static void EpRunSet(TaskSet set, int seed, bool exam, string label)
        {
            var inst = set.Instance(seed);
            var sim = EpSim.Create(seed);
            try
            {
                TaskRunner.Prepare(sim, inst);
                Check(label + ": setup lines are accepted", sim.Errors.Count == 0, string.Join("\n", sim.Errors.ToArray()));
                sim.Errors.Clear();
                var before = TaskRunner.Grade(sim, inst, false, null, null);
                foreach (var ir in before.Items.Where(x => x.Item.Kind == "task"))
                    Check(label + " " + ir.Item.Id + " fails before the work is done", !ir.Pass, string.Join("\n", ir.Checks.Where(x => x.Pass).Select(x => "passes already: " + x.Desc).ToArray()));
                if (exam) Check(label + " scores 0 at the start", before.Score == 0, before.Score.ToString());
                var typed = new StringBuilder();
                foreach (var it in inst.Items.Where(x => x.Kind == "task"))
                    foreach (var s in it.Solution)
                    {
                        sim.Type(s.Machine, s.Line);
                        typed.Append(s.Machine + "> " + s.Line + "\n");
                        if (s.Machine != EpSim.Portal && !s.Line.StartsWith("#")) { var con = sim.Console(s.Machine); if (con != null) typed.Append(con.TakeOutput()); }
                        else if (s.Machine == EpSim.Portal) typed.Append("   " + sim.LastResult + "\n");
                    }
                Check(label + ": every solution line is accepted", sim.Errors.Count == 0, string.Join("\n", sim.Errors.ToArray()));
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

        static void EpTryTests()
        {
            var c = EpCourse(); if (c == null) return;
            foreach (var l in c.AllLessons.Where(x => x.Try != null))
            {
                if (!string.IsNullOrEmpty(OnlyFile) && !l.Id.StartsWith(OnlyFile)) continue;
                l.Try.Id = l.Id;
                EpRunSet(l.Try, 3, false, "try " + l.Id);
            }
        }
    }
}
