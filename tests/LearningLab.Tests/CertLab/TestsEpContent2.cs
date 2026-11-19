// Cert Lab - checks of the MD-102 course's files: structure, objectives, decks and sheets, task machines, check and
// portal line syntax, the PowerShell used on devices, exam composition, and lesson terminal examples (typed into a
// fresh pretend device: "#machine NAME" picks it, "#setup portal:LINE" / "#setup NAME:POWERSHELL" / "#setup wait:N"
// prepare the tenant first; EpFillExamples writes the simulator's output into a lesson file while authoring).
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
        static readonly string[] PortalVerbs = { "create", "set", "assign", "unassign", "delete", "members", "remote", "import", "tenant", "device", "#wait" };

        static void EpContentTests()
        {
            var c = EpCourse();
            Check("the MD-102 course loads", c != null);
            if (c == null) return;
            Check("MD-102 course files parse", c.Problems.Count == 0, string.Join("\n", c.Problems.ToArray()));
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
            // machines, checks, portal lines and PowerShell on devices
            var sim = EpSim.Create(1);
            var machines = new HashSet<string>(sim.MachineNames, StringComparer.OrdinalIgnoreCase) { EpSim.Portal };
            var psNet = PsNet.Create(2, 1);
            var r = new PsRunspace(psNet.Machines[0], new NullHost(), "Administrator");
            PsCmdlets.Init();
            var sets = c.Labs.Concat(c.Exams).Concat(c.AllLessons.Where(l => l.Try != null).Select(l => { l.Try.File = l.Id; return l.Try; })).ToList();
            foreach (var t in sets)
            {
                string where = Path.GetFileName(t.File);
                Func<string, string> fill = s => { foreach (var kv in t.Vars) if (kv.Value.Count > 0) s = s.Replace("{" + kv.Key + "}", kv.Value[0]); return s; };
                foreach (var st in t.Setup) EpLineOk(where + " setup", st, machines, r, fill);
                foreach (var it in t.Items)
                {
                    if (it.Kind != "task") continue;
                    Check(where + " " + it.Id + ": has checks, a solution" + (t.File.Contains("exam") ? " and an explanation" : ""), it.Checks.Count > 0 && it.Solution.Count > 0 && (!t.File.Contains("exam") || it.Explain.Length > 0));
                    Check(where + " " + it.Id + ": objective is known", objectives.Contains(it.Objective), it.Objective);
                    foreach (var st in it.Solution) EpLineOk(where + " " + it.Id, st, machines, r, fill);
                    foreach (var ch in it.Checks)
                    {
                        Check(where + " " + it.Id + ": check machine " + ch.Machine + " exists", machines.Contains(ch.Machine));
                        if (ch.Machine != EpSim.Portal) continue;
                        foreach (var line in fill(ch.Script).Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0 && !x.Trim().StartsWith("#")))
                        {
                            string found; bool okLine = true;
                            try { EpCheck.Line(sim.T, line.Trim(), out found); } catch (Exception e) { okLine = false; found = e.Message; }
                            Check(where + " " + it.Id + ": check line parses: " + line.Trim(), okLine, found);
                        }
                    }
                }
            }
            sim.Dispose();
            // Search covers the course: lessons, case studies, cards; exam explanations stay hidden until taken
            var ix = SearchIndex.Build(c);
            var none = new HashSet<string>();
            Check("search finds Autopilot in the lessons", ix.Find("Autopilot", SearchIndex.Allows(none), 50).Any(h => h.Doc.Kind == "lesson"));
            Check("search finds BitLocker in the flash cards", ix.Find("BitLocker", SearchIndex.Allows(none), 200).Any(h => h.Doc.Kind == "card"));
            var cs = c.Exams.SelectMany(e => e.Cases).FirstOrDefault();
            if (cs != null) Check("search finds a case study", ix.Find("\"" + cs.Title + "\"", SearchIndex.Allows(none), 50).Any(h => h.Doc.Context.Contains("case study")));
            Check("exam explanations are hidden before the exam is taken", !ix.Find("Report-only", SearchIndex.Allows(none), 300).Any(h => h.Doc.Secret));
            EpExamShape(c, objectives);
            EpExampleTests(c);
        }

        static void EpLineOk(string where, SolutionStep st, HashSet<string> machines, PsRunspace r, Func<string, string> fill)
        {
            Check(where + ": machine " + st.Machine + " exists", machines.Contains(st.Machine), st.Machine);
            string line = fill(st.Line).Trim();
            if (st.Machine == EpSim.Portal) { var verb = line.Split(' ')[0].ToLowerInvariant(); Check(where + ": portal line starts with a known verb: " + line, PortalVerbs.Contains(verb), verb); return; }
            if (line.StartsWith("#")) { Check(where + ": known device directive: " + line, Regex.IsMatch(line, "^#(settings (join|register|enroll|disconnect|sync)|oobe|portal install|laps use|wait \\d+|power (on|off))"), line); return; }
            foreach (var cmd in PsCommandsIn(line))
                if (Regex.IsMatch(cmd, "^[A-Za-z][\\w.-]*$") && (cmd.Contains("-") || PsNatives.Get(cmd) != null))
                    Check(where + ": '" + cmd + "' exists in the simulated PowerShell", PsKnown(r, cmd), cmd);
        }

        static void EpExamShape(CourseInfo c, HashSet<string> objectives)
        {
            Check("at least 3 practice exams", c.Exams.Count >= 3, c.Exams.Count.ToString());
            foreach (var e in c.Exams)
            {
                Func<string, int> n = k => e.Items.Count(i => i.Kind == k);
                Check(e.Id + ": 30 or more items", e.Items.Count >= 30, e.Items.Count.ToString());
                Check(e.Id + ": a case study with 3 or more questions", e.Cases.Count >= 1 && e.Cases.All(cs => e.Items.Count(i => i.Case == cs.Id) >= 3));
                Check(e.Id + ": case study questions come together", e.Cases.All(cs => { var idx = e.Items.Select((it, i) => it.Case == cs.Id ? i : -1).Where(i => i >= 0).ToList(); return idx.Count > 0 && idx.Last() - idx.First() == idx.Count - 1; }));
                Check(e.Id + ": every question's case exists", e.Items.All(i => i.Case.Length == 0 || e.CaseOf(i) != null));
                Check(e.Id + ": 2 or more drag-and-order items", n("order") >= 2, n("order").ToString());
                Check(e.Id + ": 2 or more Yes / No series", n("yesno") >= 2, n("yesno").ToString());
                Check(e.Id + ": 3 or more multiple-answer items", n("multi") >= 3, n("multi").ToString());
                Check(e.Id + ": 3 or more hands-on tasks", n("task") >= 3, n("task").ToString());
                Check(e.Id + ": every objective is examined", c.Objectives.All(o => e.Items.Any(i => i.Objective == o)), string.Join(", ", c.Objectives.Where(o => !e.Items.Any(i => i.Objective == o)).ToArray()));
                Check(e.Id + ": objectives are known", e.Items.All(i => objectives.Contains(i.Objective)), string.Join(", ", e.Items.Where(i => !objectives.Contains(i.Objective)).Select(i => i.Id + "=" + i.Objective).ToArray()));
                Check(e.Id + ": every question has an explanation", e.Items.Where(i => i.IsQuestion).All(i => i.Explain.Length > 0), string.Join(", ", e.Items.Where(i => i.IsQuestion && i.Explain.Length == 0).Select(i => i.Id).ToArray()));
                Check(e.Id + ": single-answer questions have one right answer of 3+", e.Items.Where(i => i.Kind == "choice").All(i => i.Answers.Count == 1 && i.Choices.Count >= 3), string.Join(", ", e.Items.Where(i => i.Kind == "choice" && !(i.Answers.Count == 1 && i.Choices.Count >= 3)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": multiple-answer questions have 2+ right answers of 4+", e.Items.Where(i => i.Kind == "multi").All(i => i.Answers.Count >= 2 && i.Choices.Count >= 4), string.Join(", ", e.Items.Where(i => i.Kind == "multi" && !(i.Answers.Count >= 2 && i.Choices.Count >= 4)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": order items have 3+ steps and a distractor", e.Items.Where(i => i.Kind == "order").All(i => i.OrderAnswer.Count >= 3 && i.Choices.Count > i.OrderAnswer.Count), string.Join(", ", e.Items.Where(i => i.Kind == "order" && !(i.OrderAnswer.Count >= 3 && i.Choices.Count > i.OrderAnswer.Count)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": Yes / No series have 2+ statements, not all the same answer", e.Items.Where(i => i.Kind == "yesno").All(i => i.Truth.Count >= 2 && i.Truth.Distinct().Count() == 2), string.Join(", ", e.Items.Where(i => i.Kind == "yesno" && !(i.Truth.Count >= 2 && i.Truth.Distinct().Count() == 2)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": item ids are unique", e.Items.Select(i => i.Id).Distinct().Count() == e.Items.Count);
            }
        }

        static List<string> ExampleSetup(string block, out string machine)
        {
            var lines = block.Replace("\r", "").Split('\n');
            var m = lines.FirstOrDefault(x => x.StartsWith("#machine "));
            machine = m == null ? "RB-LT-001" : m.Substring(9).Trim();
            return lines.Where(x => x.StartsWith("#setup ")).Select(x => x.Substring(7)).ToList();
        }
        static string EpTypeExample(string block)
        {
            string machine;
            var setup = ExampleSetup(block, out machine);
            var sim = EpSim.Create(1);
            try
            {
                foreach (var su in setup)
                {
                    int colon = su.IndexOf(':');
                    string where = su.Substring(0, colon), line = su.Substring(colon + 1);
                    if (where == "wait") { sim.Advance(long.Parse(line)); continue; }
                    if (where == EpSim.Portal) { sim.Type(EpSim.Portal, line); continue; }
                    sim.RunSetup(new List<SolutionStep> { new SolutionStep { Machine = where, Line = line } });
                }
                var typed = new List<string>();
                foreach (var cmd in ExampleCommands(block)) typed.AddRange(cmd.Value.Split('\n'));
                string got = sim.Transcript(machine, typed, 120).Replace("\r", "");
                return Regex.Replace(got, "\\n*PS [^>\\n]*> $", "").TrimEnd('\n', ' ');
            }
            finally { sim.Dispose(); }
        }

        static void EpExampleTests(CourseInfo c)
        {
            foreach (var l in c.AllLessons)
            {
                int n = 0;
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal"))
                {
                    n++;
                    if (ExampleCommands(b.Text).Count == 0) continue;
                    string g = string.Join("\n", EpTypeExample(b.Text).Split('\n').Select(x => x.TrimEnd()).ToArray());
                    string w = string.Join("\n", b.Text.Replace("\r", "").Split('\n').Where(x => !x.StartsWith("#setup ") && !x.StartsWith("#machine ")).Select(x => x.TrimEnd()).ToArray()).TrimEnd('\n', ' ');
                    Check(l.Id + " example " + n + " prints what the simulator prints", g == w, Diff(w, g));
                }
            }
        }

        /// <summary>Authoring help: fills each terminal example of an MD-102 lesson file with what the simulator prints.</summary>
        public static string EpFillExamples(string text)
        {
            var lines = text.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append('\n');
                if (lines[i].Trim() != "```terminal") continue;
                var block = new List<string>();
                int j = i + 1;
                while (j < lines.Length && !lines[j].StartsWith("```")) block.Add(lines[j++]);
                string body = string.Join("\n", block.ToArray());
                if (ExampleCommands(body).Count > 0)
                {
                    foreach (var d in block.Where(x => x.StartsWith("#setup ") || x.StartsWith("#machine "))) sb.Append(d).Append('\n');
                    foreach (var gl in EpTypeExample(body).Split('\n')) sb.Append(gl.TrimEnd()).Append('\n');
                }
                else foreach (var b in block) sb.Append(b).Append('\n');
                i = j - 1;
            }
            var res = sb.ToString();
            if (!text.EndsWith("\n") && res.EndsWith("\n")) res = res.Substring(0, res.Length - 1);
            return res;
        }
    }
}
