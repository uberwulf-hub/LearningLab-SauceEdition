// Cert Lab - checks of the PowerShell course's content: every file parses; every command a lesson example, card,
// sheet, check or solution uses exists in the simulator and has a help page; every try-it and lab task fails
// before the work and passes after the model solution is typed into the consoles; every practice exam goes from
// 0 to 100% (hands-on tasks after the graders' restart, questions with the model answers) on two seeds; and the
// lesson examples print what the simulator prints.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static CourseInfo psCourse;

        static partial void PsGroupsContent(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ps-content", PsContentTests));
            l.Add(new KeyValuePair<string, Action>("ps-tries", PsTryTests));
            l.Add(new KeyValuePair<string, Action>("ps-labs", PsLabTests));
            l.Add(new KeyValuePair<string, Action>("ps-exams", PsExamTests));
        }

        static CourseInfo PsCourse()
        {
            if (psCourse == null && AppRootForTests != null) psCourse = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses")).FirstOrDefault(c => c.Id == "powershell");
            return psCourse;
        }
        public static string AppRootForTests;

        /// <summary>The commands a piece of PowerShell runs (names in command position), found with the parser.</summary>
        public static List<string> PsCommandsIn(string text)
        {
            var l = new List<string>();
            ScriptBlockAst ast;
            try { ast = PsParser.Parse(text, ""); } catch (PsParseError) { return l; }
            Walk(ast, a => { var c = a as CommandAst; if (c != null && c.Name != null) l.Add(c.Name); });
            return l;
        }
        static void Walk(object node, Action<PsAst> f)
        {
            if (node == null) return;
            var a = node as PsAst;
            if (a != null) f(a);
            if (node is string || node.GetType().IsPrimitive) return;
            if (node is IEnumerable && !(node is string)) { foreach (var x in (IEnumerable)node) if (x is PsAst || x is IEnumerable || x is CmdElem || x is SwitchClause || x is CatchClause || x is Redir || x is KeyValuePair<PsAst, PsAst>) Walk(x, f); return; }
            if (node is KeyValuePair<PsAst, PsAst>) { var kv = (KeyValuePair<PsAst, PsAst>)node; Walk(kv.Key, f); Walk(kv.Value, f); return; }
            if (node is KeyValuePair<string, PsAst>) { Walk(((KeyValuePair<string, PsAst>)node).Value, f); return; }
            foreach (var fi in node.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (fi.Name == "Src") continue;
                var v = fi.GetValue(node);
                if (v is PsAst || v is IEnumerable && !(v is string) || v is CmdElem || v is SwitchClause || v is CatchClause || v is Redir) Walk(v, f);
            }
        }

        static bool PsKnown(PsRunspace r, string name)
        {
            if (name.Contains("\\") || name.StartsWith(".") || name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) return true;
            return r.Resolve(name, r.Global, false) != null || PsCmdlets.Get(name) != null;
        }

        /// <summary>Lines typed at the prompt in a lesson's example terminal ("PS C:\...> cmd"), with >> continuation lines.</summary>
        static List<KeyValuePair<string, string>> ExampleCommands(string block)
        {
            var res = new List<KeyValuePair<string, string>>();
            var lines = block.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], "^(\\[[^\\]]+\\]: )?PS [^>]*> (.*)$");
                if (!m.Success) continue;
                var cmd = new StringBuilder(m.Groups[2].Value);
                while (i + 1 < lines.Length && lines[i + 1].StartsWith(">> ")) { cmd.Append('\n').Append(lines[++i].Substring(3)); }
                res.Add(new KeyValuePair<string, string>(lines[i], cmd.ToString()));
            }
            return res;
        }

        /// <summary>Every help page: parses, belongs to a simulated command (or is an about_ topic), documents only
        /// parameters the command has, names related pages that exist, and renders through Get-Help (normal, -Detailed,
        /// -Full, -Examples) without an error.</summary>
        static void PsHelpPageTests(PsRunspace r)
        {
            var common = new HashSet<string>(PsBinder.CommonDefs(true).SelectMany(p => new[] { p.Name }.Concat(p.Aliases)), StringComparer.OrdinalIgnoreCase);
            var bad = new List<string>();
            foreach (var name in PsHelp.Names.OrderBy(x => x).ToList())
            {
                var p = PsHelp.Page(name);
                bool about = name.StartsWith("about_", StringComparison.OrdinalIgnoreCase) || name.Equals("default", StringComparison.OrdinalIgnoreCase);
                if (about) { if (!PsHelp.Raw(name).Contains("SHORT DESCRIPTION") && !name.Equals("default", StringComparison.OrdinalIgnoreCase)) bad.Add(name + ": no SHORT DESCRIPTION"); }
                else if (p == null || p.Synopsis.Length == 0) { bad.Add(name + ": no SYNOPSIS"); continue; }
                CmdletDef d;
                if (!about && !PsCmdlets.All.TryGetValue(name, out d)) { bad.Add(name + ": no such simulated cmdlet"); continue; }
                if (!about)
                {
                    d = PsCmdlets.All[name];
                    foreach (var kv in p.Parameters)
                        if (!common.Contains(kv.Key) && !d.Params.Any(x => x.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
                            bad.Add(name + ": PARAMETER " + kv.Key + " is not a parameter of the simulated cmdlet");
                    if (p.Examples.Count == 0) bad.Add(name + ": no EXAMPLE");
                }
                foreach (var rel in p.Related)
                    if (PsHelp.Raw(rel) == null && !PsCmdlets.All.ContainsKey(rel)) bad.Add(name + ": RELATED " + rel + " has no page");
                foreach (var mode in about ? new[] { "normal" } : new[] { "normal", "detailed", "full", "examples" })
                {
                    try
                    {
                        var text = PsHelp.Render(r, name, mode, null);
                        if (string.IsNullOrEmpty(text)) bad.Add(name + ": Get-Help " + mode + " is empty");
                        else if (!about && text.IndexOf("NAME", StringComparison.Ordinal) < 0) bad.Add(name + ": Get-Help " + mode + " has no NAME");
                    }
                    catch (Exception e) { bad.Add(name + ": Get-Help " + mode + " threw " + e.GetType().Name + ": " + e.Message); }
                }
            }
            Check("every help page renders through Get-Help (" + PsHelp.Names.Count() + " pages)", bad.Count == 0, string.Join("\n", bad.ToArray()));
        }

        static void PsContentTests()
        {
            var c = PsCourse();
            Check("the PowerShell course loads", c != null);
            var allCourses = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses"));
            Check("Home lists both courses, RHCSA first", string.Join(",", allCourses.Select(x => x.Id).ToArray()).StartsWith("rhcsa,powershell"), string.Join(",", allCourses.Select(x => x.Id).ToArray()));
            if (c == null) return;
            Check("PowerShell course files parse", c.Problems.Count == 0, string.Join("\n", c.Problems.ToArray()));
            Check("every unit has lessons", c.Units.All(u => u.Lessons.Count > 0), string.Join(", ", c.Units.Where(u => u.Lessons.Count == 0).Select(u => u.Id).ToArray()));
            Check("lesson ids are unique", c.AllLessons.Select(x => x.Id).Distinct().Count() == c.AllLessons.Count());
            var net = PsNet.Create(2, 1);
            var r = new PsRunspace(net.Machines[0], new NullHost(), "Administrator");
            PsCmdlets.Init();
            var noHelp = new SortedSet<string>();
            // functions the course's own examples and solutions define
            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> collect = txt => { try { Walk(PsParser.Parse(txt, ""), a => { var fa = a as FunctionAst; if (fa != null) own.Add(fa.Name); var ca = a as ConfigAst; if (ca != null) own.Add(ca.Name); }); } catch (PsParseError) { } };
            foreach (var l in c.AllLessons)
            {
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal")) foreach (var ex in ExampleCommands(b.Text)) collect(ex.Value);
                if (l.Try != null) foreach (var it in l.Try.Items) { collect(string.Join("\n", it.Solution.Select(s => s.Line).ToArray())); foreach (var su in l.Try.Setup) collect(su.Line); }
            }
            foreach (var t in c.Labs.Concat(c.Exams))
            {
                collect(string.Join("\n", t.Setup.Select(s => s.Line).ToArray()));
                foreach (var it in t.Items) collect(string.Join("\n", it.Solution.Select(s => s.Line).ToArray()));
                // functions written into files by a solution ("function Get-X { }" inside a string), with {vars} filled
                var all = string.Join("\n", t.Items.SelectMany(it => it.Solution).Concat(t.Setup).Select(s => s.Line).ToArray());
                foreach (var kv in t.Vars) if (kv.Value.Count > 0) all = all.Replace("{" + kv.Key + "}", kv.Value[0]);
                foreach (Match m in Regex.Matches(all, "(?i)\\bfunction\\s+([A-Za-z][\\w-]*)")) own.Add(m.Groups[1].Value);
            }
            foreach (var f in Directory.GetFiles(c.Dir, "*.txt", SearchOption.AllDirectories)) foreach (Match m in Regex.Matches(File.ReadAllText(f), "(?im)^\\s*function\\s+([A-Za-z][\\w-]*)")) own.Add(m.Groups[1].Value);
            Action<string, string> need = (name, where) =>
            {
                if (own.Contains(name)) return;
                // only names that look like commands: Verb-Noun, an alias, or a program
                if (name.StartsWith("-") || !Regex.IsMatch(name, "^[A-Za-z][\\w.-]*$") || !name.Contains("-") && r.Resolve(name, r.Global, false) == null && PsNatives.Get(name) == null) return;
                if (!PsKnown(r, name)) { Check(where + ": '" + name + "' exists in the simulator", false, name); return; }
                var ci = r.Resolve(name, r.Global, false);
                if (ci != null && ci.Def != null && PsHelp.Page(ci.Def.Name) == null) noHelp.Add(ci.Def.Name + " (" + where + ")");
            };
            foreach (var l in c.AllLessons)
            {
                foreach (var q in l.Quiz) Check(l.Id + " quiz '" + q.Q + "' has one right answer", q.Answer >= 0 && q.Choices.Count >= 2);
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal"))
                    foreach (var ex in ExampleCommands(b.Text))
                        foreach (var cmd in PsCommandsIn(ex.Value)) need(cmd, l.Id);
                if (l.Try != null) foreach (var it in l.Try.Items) foreach (var s in it.Solution) foreach (var cmd in PsCommandsIn(s.Line)) need(cmd, "try " + l.Id);
            }
            var objectives = new HashSet<string>(c.Objectives);
            Check("every objective has a deck", c.Objectives.All(o => c.Decks.Any(d => d.Objective == o)), string.Join(", ", c.Objectives.Where(o => !c.Decks.Any(d => d.Objective == o)).ToArray()));
            Check("every objective has a cheat sheet", c.Objectives.All(o => c.Sheets.Any(s => s.Objective == o)), string.Join(", ", c.Objectives.Where(o => !c.Sheets.Any(s => s.Objective == o)).ToArray()));
            foreach (var d in c.Decks)
            {
                Check("deck " + d.Id + " objective is known", objectives.Contains(d.Objective), d.Objective);
                Check("deck " + d.Id + " has cards", d.Cards.Count >= 8, d.Cards.Count.ToString());
                foreach (var card in d.Cards.Where(x => x.Command != null)) foreach (var cmd in PsCommandsIn(card.Command)) need(cmd, "card " + card.Id);
            }
            foreach (var s in c.Sheets)
            {
                Check("sheet " + s.Id + " objective is known", objectives.Contains(s.Objective), s.Objective);
                foreach (var sec in s.Sections) foreach (var row in sec.Rows) foreach (var cmd in PsCommandsIn(row.Cmd)) need(cmd, "sheet " + s.Id);
            }
            foreach (var t in c.Labs.Concat(c.Exams))
                foreach (var it in t.Items)
                {
                    Func<string, string> fill = s => { foreach (var kv in t.Vars) if (kv.Value.Count > 0) s = s.Replace("{" + kv.Key + "}", kv.Value[0]); return s; };
                    foreach (var st in it.Solution) foreach (var cmd in PsCommandsIn(fill(st.Line))) need(cmd, Path.GetFileName(t.File) + " " + it.Id);
                    foreach (var ch in it.Checks) foreach (var cmd in PsCommandsIn(fill(ch.Script))) need(cmd, Path.GetFileName(t.File) + " " + it.Id + " check");
                    if (it.Kind == "task") Check(Path.GetFileName(t.File) + " " + it.Id + ": has checks (an @setup inside a task swallows them: put it before @task)", it.Checks.Count > 0);
                    if (it.Kind == "task") Check(Path.GetFileName(t.File) + " " + it.Id + ": machines are WIN-SRV1 / WIN-CL1", (it.Machine == "WIN-SRV1" || it.Machine == "WIN-CL1") && it.Checks.All(x => x.Machine == "WIN-SRV1" || x.Machine == "WIN-CL1"), it.Machine);
                }
            Check("every command the course uses has a help page", noHelp.Count == 0, string.Join(", ", noHelp.ToArray()));
            var missing = PsCmdlets.All.Values.Where(d => PsHelp.Page(d.Name) == null).Select(d => d.Name).OrderBy(x => x).ToList();
            Check("every simulated cmdlet has a help page", missing.Count == 0, missing.Count + " without a page: " + string.Join(" ", missing.ToArray()));
            PsHelpPageTests(r);
            Check("labs: at least one per unit", c.Units.All(u => c.Labs.Any(x => x.Unit == u.Id)), string.Join(", ", c.Units.Where(u => !c.Labs.Any(x => x.Unit == u.Id)).Select(u => u.Id).ToArray()));
            Check("at least 3 practice exams", c.Exams.Count >= 3, c.Exams.Count.ToString());
            // "What does this print?" questions: the code (lines indented by 4 spaces) is typed into a fresh WIN-SRV1
            // console and the right answer (backticks removed, lines separated by " / ") must be what it prints.
            foreach (var e in c.Exams)
                foreach (var it in e.Items.Where(i => i.Kind == "choice" && i.Text.IndexOf("print", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var code = it.Text.Replace("\r", "").Split('\n').Where(x => x.StartsWith("    ")).Select(x => x.Substring(4)).ToList();
                    if (code.Count == 0) continue;
                    var p = new P();
                    var got = new StringBuilder();
                    try { foreach (var line in code) got.Append(p.Run(line).Replace("\r", "")); }
                    finally { p.Dispose(); }
                    string actual = string.Join("\n", got.ToString().Split('\n').Select(x => x.TrimEnd()).Where(x => x.Length > 0).ToArray());
                    string right = it.Answer >= 0 && it.Answer < it.Choices.Count ? it.Choices[it.Answer] : "";
                    string want = string.Join("\n", right.Split(new[] { " / " }, StringSplitOptions.None).Select(x => x.Trim().Trim('`')).ToArray());
                    Check(e.Id + " " + it.Id + ": the right answer is what the simulator prints", actual == want, "prints:\n" + actual + "\nanswer:\n" + want);
                    foreach (var wrong in it.Choices.Where((x, i) => i != it.Answer))
                        Check(e.Id + " " + it.Id + ": a wrong answer is not what it prints (" + wrong + ")", string.Join("\n", wrong.Split(new[] { " / " }, StringSplitOptions.None).Select(x => x.Trim().Trim('`')).ToArray()) != actual);
                }
            foreach (var e in c.Exams)
            {
                Check(e.Id + ": hands-on tasks and questions", e.Items.Any(i => i.Kind == "task") && e.Items.Any(i => i.Kind == "choice"));
                Check(e.Id + ": tasks on both machines", e.Items.Any(i => i.Kind == "task" && i.Machine == "WIN-SRV1") && e.Items.Any(i => i.Kind == "task" && i.Machine == "WIN-CL1"));
                Check(e.Id + ": every objective is examined", c.Objectives.All(o => e.Items.Any(i => i.Objective == o)), string.Join(", ", c.Objectives.Where(o => !e.Items.Any(i => i.Objective == o)).ToArray()));
                Check(e.Id + ": objectives are known", e.Items.All(i => objectives.Contains(i.Objective)), string.Join(", ", e.Items.Where(i => !objectives.Contains(i.Objective)).Select(i => i.Id + "=" + i.Objective).ToArray()));
                Check(e.Id + ": every task has checks, a solution and an explanation", e.Items.Where(i => i.Kind == "task").All(i => i.Checks.Count > 0 && i.Solution.Count > 0 && i.Explain.Length > 0), string.Join(", ", e.Items.Where(i => i.Kind == "task" && !(i.Checks.Count > 0 && i.Solution.Count > 0 && i.Explain.Length > 0)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": every question has one right answer and an explanation", e.Items.Where(i => i.Kind == "choice").All(i => i.Answer >= 0 && i.Choices.Count >= 3 && i.Explain.Length > 0), string.Join(", ", e.Items.Where(i => i.Kind == "choice" && !(i.Answer >= 0 && i.Choices.Count >= 3 && i.Explain.Length > 0)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": points add up to " + c.ExamPoints, e.TotalPoints == c.ExamPoints, e.TotalPoints.ToString());
            }
            net.Machines.ForEach(m => m.Sessions.Clear());
            PsExampleTests(c);
        }

        /// <summary>Lesson examples: each example terminal, typed into a fresh console, must print what the lesson shows.</summary>
        static void PsExampleTests(CourseInfo c)
        {
            foreach (var l in c.AllLessons)
            {
                if (!string.IsNullOrEmpty(OnlyFile) && !l.Id.StartsWith(OnlyFile)) continue;
                int n = 0;
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal"))
                {
                    n++;
                    var cmds = ExampleCommands(b.Text);
                    if (cmds.Count == 0) continue;
                    bool remote = b.Text.Contains("]: PS ");
                    var sim = PsSim.Create(2, 1);
                    try
                    {
                        string machine = Regex.IsMatch(b.Text, "^#machine WIN-CL1", RegexOptions.Multiline) ? "WIN-CL1" : "WIN-SRV1";
                        var setup = b.Text.Replace("\r", "").Split('\n').Where(x => x.StartsWith("#setup ")).Select(x => x.Substring(7)).ToList();
                        sim.RunSetup(setup.Select(su => new SolutionStep { Machine = su.StartsWith("WIN-CL1:") ? "WIN-CL1" : "WIN-SRV1", Line = su.StartsWith("WIN-CL1:") ? su.Substring(8) : su }).ToList());
                        var typed = new List<string>();
                        foreach (var cmd in cmds) typed.AddRange(cmd.Value.Split('\n'));
                        string got = sim.Transcript(machine, typed, 120).Replace("\r", "");
                        string want = string.Join("\n", b.Text.Replace("\r", "").Split('\n').Where(x => !x.StartsWith("#setup ") && !x.StartsWith("#machine ")).ToArray());
                        // the transcript ends with the next prompt; examples may leave out trailing blank lines
                        string g = Regex.Replace(got, "\\n*(\\[[^\\]]+\\]: )?PS [^>\n]*> $", "").TrimEnd('\n', ' ');
                        string w = want.TrimEnd('\n', ' ');
                        g = string.Join("\n", g.Split('\n').Select(x => x.TrimEnd()).ToArray());
                        w = string.Join("\n", w.Split('\n').Select(x => x.TrimEnd()).ToArray());
                        Check(l.Id + " example " + n + " prints what the simulator prints", g == w, Diff(w, g));
                    }
                    finally { sim.Dispose(); }
                }
            }
        }
        /// <summary>Authoring help: types the commands of every terminal example in a lesson file into the simulator and
        /// writes what it prints back into the example (prompt lines and >> lines are kept, the output is replaced).</summary>
        public static string FillExamples(string text)
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
                var cmds = ExampleCommands(body);
                if (cmds.Count > 0)
                {
                    var sim = PsSim.Create(2, 1);
                    try
                    {
                        string machine = block.Any(x => x.StartsWith("#machine WIN-CL1")) ? "WIN-CL1" : "WIN-SRV1";
                        sim.RunSetup(block.Where(x => x.StartsWith("#setup ")).Select(x => x.Substring(7)).Select(su => new SolutionStep { Machine = su.StartsWith("WIN-CL1:") ? "WIN-CL1" : "WIN-SRV1", Line = su.StartsWith("WIN-CL1:") ? su.Substring(8) : su }).ToList());
                        var typed = new List<string>();
                        foreach (var cmd in cmds) typed.AddRange(cmd.Value.Split('\n'));
                        string got = sim.Transcript(machine, typed, 120).Replace("\r", "");
                        got = Regex.Replace(got, "\\n*(\\[[^\\]]+\\]: )?PS [^>\n]*> $", "").TrimEnd('\n', ' ');
                        foreach (var d in block.Where(x => x.StartsWith("#setup ") || x.StartsWith("#machine "))) sb.Append(d).Append('\n');
                        foreach (var gl in got.Split('\n')) sb.Append(gl.TrimEnd()).Append('\n');
                    }
                    finally { sim.Dispose(); }
                }
                else foreach (var b in block) sb.Append(b).Append('\n');
                i = j - 1;
            }
            var res = sb.ToString();
            if (!text.EndsWith("\n") && res.EndsWith("\n")) res = res.Substring(0, res.Length - 1);
            return res;
        }

        static string Diff(string want, string got)
        {
            var a = want.Split('\n'); var b = got.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i] : "(end)", y = i < b.Length ? b[i] : "(end)";
                if (x != y) return "line " + (i + 1) + "\n  lesson:    " + x + "\n  simulator: " + y + "\n--- simulator output ---\n" + Tail(got, 1500);
            }
            return "";
        }

        /// <summary>A try-it, lab or exam on a fresh network: every item fails at the start and passes after its solution.</summary>
        static void PsRunSet(TaskSet set, int seed, bool exam, string label)
        {
            var inst = set.Instance(seed);
            var sim = PsSim.Create(2, seed);
            var setupLog = new StringBuilder();
            sim.SetupLog = s => setupLog.Append(s).Append('\n');
            try
            {
                sim.StartConsoles(true, null);
                foreach (var n in sim.MachineNames) sim.Console(n).TakeOutput();
                TaskRunner.Prepare(sim, inst);
                var before = TaskRunner.Grade(sim, inst, false, null);
                foreach (var ir in before.Items)
                    Check(label + " " + ir.Item.Id + " fails before the work is done", !ir.Pass, string.Join("\n", ir.Checks.Where(x => x.Pass).Select(x => "passes already: " + x.Desc).ToArray()));
                if (exam) Check(label + " scores 0 at the start", before.Score == 0, before.Score.ToString());
                var typed = new StringBuilder();
                var answers = new Dictionary<string, int>();
                foreach (var it in inst.Items)
                {
                    if (it.Kind == "choice") { answers[it.Id] = it.Answer; continue; }
                    foreach (var s in it.Solution)
                    {
                        sim.Type(s.Machine, s.Line);
                        var con = sim.Console(s.Machine);
                        typed.Append(s.Machine + "> " + s.Line + "\n" + con.TakeOutput());
                        if (typed.Length > 60000) typed.Remove(0, typed.Length - 60000);
                    }
                }
                var after = TaskRunner.Grade(sim, inst, exam, answers);
                foreach (var ir in after.Items)
                {
                    var bad = ir.Checks.Where(x => !x.Pass).ToList();
                    Check(label + " " + ir.Item.Id + " passes with its model solution", ir.Pass, string.Join("\n", bad.Select(x => "failed: " + x.Desc + " [" + x.Machine + "]\n" + x.Output.Trim()).ToArray()) + "\n--- consoles (end) ---\n" + Tail(typed.ToString(), 2500) + (setupLog.Length > 0 ? "\n--- setup ---\n" + Tail(setupLog.ToString(), 1200) : ""));
                }
                if (exam) Check(label + " scores 100% with the model solutions", after.Score == after.Max, after.Score + "/" + after.Max);
            }
            finally { sim.Dispose(); }
        }

        static void PsTryTests()
        {
            var c = PsCourse(); if (c == null) return;
            foreach (var l in c.AllLessons.Where(x => x.Try != null))
            {
                if (!string.IsNullOrEmpty(OnlyFile) && !l.Id.StartsWith(OnlyFile)) continue;
                l.Try.Id = l.Id;
                PsRunSet(l.Try, 3, false, "try " + l.Id);
            }
        }
        static void PsLabTests() { var c = PsCourse(); if (c == null) return; foreach (var lab in c.Labs.Where(x => Wanted(x.File))) PsRunSet(lab, 5, false, "lab " + lab.Id); }
        static void PsExamTests()
        {
            var c = PsCourse(); if (c == null) return;
            foreach (var ex in c.Exams.Where(x => Wanted(x.File))) { PsRunSet(ex, 11, true, "exam " + ex.Id + " (seed 11)"); PsRunSet(ex, 29, true, "exam " + ex.Id + " (seed 29)"); }
        }
    }
}
