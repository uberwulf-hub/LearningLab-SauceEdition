// Cert Lab - courses as data: Courses\<id>\course.json lists the units; lessons, flash card decks, cheat sheets,
// labs and practice exams are text files in the course's folders (format in Courses\FORMAT.md). A course names its
// simulator ("rhel"); exam items have a kind ("task" today: hands-on, checked on the machines; "choice" for
// multiple-choice questions) so later courses can mix them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class CourseInfo
    {
        public string Id = "", Title = "", Short = "", Simulator = "rhel", Description = "", Dir = "", Exam = "", Disclaimer = "", ExamNote = "", ExamHint = "", AboutMachines = "", HelpDir = "";
        public int Order = 100;
        public int Machines = 2, ExamMinutes = 180, PassPercent = 70, ExamPoints = 300, ScoreMin, PassScore;
        public List<int> Weights = new List<int>();          // the published share of each objective (Security+), in percent
        public List<TaskSet> Practice = new List<TaskSet>();  // pbq\*.txt: performance-based question practice sets
        /// <summary>The passing score on the course's scale (Security+ 750 of 900; others a percentage of the points).</summary>
        public int PassMark { get { return PassScore > 0 ? PassScore : ExamPoints * PassPercent / 100; } }
        /// <summary>A raw score on the course's scale: from ScoreMin to ExamPoints when ScoreMin is 100 or more (Security+:
        /// 100 + share x 800); otherwise the share of ExamPoints, at least ScoreMin.</summary>
        public int Scale(int score, int max)
        {
            double f = max <= 0 ? 0 : Math.Max(0, Math.Min(1, score / (double)max));
            if (ScoreMin >= 100) return ScoreMin + (int)Math.Round((ExamPoints - ScoreMin) * f);
            return Math.Max(ScoreMin, (int)Math.Round(ExamPoints * f));
        }
        public List<CUnit> Units = new List<CUnit>();
        public List<Deck> Decks = new List<Deck>();
        public List<Sheet> Sheets = new List<Sheet>();
        public List<TaskSet> Labs = new List<TaskSet>();
        public List<TaskSet> Exams = new List<TaskSet>();
        public List<string> Problems = new List<string>();
        public List<string> Objectives = new List<string>();

        public CUnit Unit(string id) { return Units.FirstOrDefault(u => u.Id == id); }
        public Lesson Lesson(string id) { return Units.SelectMany(u => u.Lessons).FirstOrDefault(l => l.Id == id); }
        public IEnumerable<Lesson> AllLessons { get { return Units.SelectMany(u => u.Lessons); } }
    }

    public class CUnit { public string Id = "", Title = "", Objective = "", Intro = ""; public List<Lesson> Lessons = new List<Lesson>(); }

    public class Block { public string Kind = "p"; public string Text = ""; public List<string> Items = new List<string>(); }

    public class Lesson
    {
        public string Id = "", Title = "", UnitId = "";
        public List<Block> Blocks = new List<Block>();
        public TaskSet Try;
        public List<QuizQ> Quiz = new List<QuizQ>();
        public string PlainText { get { return string.Join("\n", Blocks.Select(b => b.Text + " " + string.Join(" ", b.Items.ToArray())).ToArray()); } }
    }

    public class QuizQ { public string Q = ""; public List<string> Choices = new List<string>(); public int Answer = -1; public string Why = ""; }

    public class Card { public string Front = "", Back = "", Deck = "", Id = ""; public string Command; }
    public class Deck { public string Id = "", Title = "", Objective = ""; public List<Card> Cards = new List<Card>(); }

    public class SheetRow { public string Cmd = "", Meaning = ""; }
    public class SheetSection { public string Title = ""; public List<SheetRow> Rows = new List<SheetRow>(); }
    public class Sheet { public string Id = "", Title = "", Objective = ""; public List<SheetSection> Sections = new List<SheetSection>(); }

    public class CheckDef { public string Machine = "server1", Desc = "", Script = ""; }
    public class SolutionStep { public string Machine = "server1"; public string Line = ""; }

    public class TaskItem
    {
        public string Id = "", Kind = "task", Objective = "", Machine = "server1", Text = "";
        public int Points = 10;
        public List<CheckDef> Checks = new List<CheckDef>();
        public List<SolutionStep> Solution = new List<SolutionStep>();
        public List<string> Hints = new List<string>();
        public string Explain = "";
        public List<string> Choices = new List<string>(); public int Answer = -1;   // kind "choice"
        public List<int> Answers = new List<int>();          // kind "multi": every right choice
        public List<string> OrderAnswer = new List<string>(); // kind "order": the right sequence (Choices: every action, distractors too)
        public List<bool> Truth = new List<bool>();          // kind "yesno": Choices are the statements, Truth their answers
        public string Case = "";                             // the case study this question belongs to
        public List<string> Raw = new List<string>();        // PBQ kinds: the body lines as written (Pbq.cs parses them)
        public PbqSpec Pbq;
        public bool IsPbq { get { return Pbq != null; } }
        public bool IsQuestion { get { return Kind != "task"; } }
    }

    /// <summary>A case study in a practice exam: a scenario in sections (Overview, Existing environment, Requirements...)
    /// that several questions refer to.</summary>
    public class CaseStudy
    {
        public string Id = "", Title = "";
        public List<KeyValuePair<string, string>> Sections = new List<KeyValuePair<string, string>>();
        public string Text { get { return string.Join("\n\n", Sections.Select(s => s.Key + "\n" + s.Value).ToArray()); } }
    }

    /// <summary>A lesson's "try it", a lab or a practice exam: setup lines, then tasks with checks and model solutions.</summary>
    public class TaskSet
    {
        public string Id = "", Title = "", Unit = "", Intro = "";
        public int Minutes = 30, Machines = 2;
        public List<SolutionStep> Setup = new List<SolutionStep>();
        public List<TaskItem> Items = new List<TaskItem>();
        public Dictionary<string, List<string>> Vars = new Dictionary<string, List<string>>();
        public bool RebootFirst;
        public List<string> NoLogin = new List<string>();   // @nologin server2: that console starts at the login prompt
        public string File = "";
        public List<CaseStudy> Cases = new List<CaseStudy>();
        public CaseStudy CaseOf(TaskItem it) { return it == null || it.Case.Length == 0 ? null : Cases.FirstOrDefault(c => c.Id == it.Case); }

        /// <summary>A copy with {vars} chosen from the seed (exams vary from attempt to attempt).</summary>
        public TaskSet Instance(int seed)
        {
            var r = new Random(seed * 7907 + FwHash.Of(Id));
            var pick = new Dictionary<string, string>();
            foreach (var kv in Vars.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var opts = kv.Value;
                string v;
                var range = opts.Count == 1 ? Regex.Match(opts[0], "^(\\d+)-(\\d+)$") : Match.Empty;
                if (range.Success) { int a = int.Parse(range.Groups[1].Value), b = int.Parse(range.Groups[2].Value); v = (a + r.Next(b - a + 1)).ToString(CultureInfo.InvariantCulture); }
                else v = opts[r.Next(opts.Count)];
                // a later var may refer to an earlier one
                foreach (var p in pick) v = v.Replace("{" + p.Key + "}", p.Value);
                pick[kv.Key] = v;
            }
            Func<string, string> sub = s => { if (s == null) return null; foreach (var p in pick) s = s.Replace("{" + p.Key + "}", p.Value); return s; };
            var t = new TaskSet { Id = Id, Title = sub(Title), Unit = Unit, Intro = sub(Intro), Minutes = Minutes, Machines = Machines, RebootFirst = RebootFirst, File = File };
            foreach (var s in Setup) t.Setup.Add(new SolutionStep { Machine = s.Machine, Line = sub(s.Line) });
            foreach (var it in Items)
            {
                var n = new TaskItem { Id = it.Id, Kind = it.Kind, Objective = it.Objective, Machine = it.Machine, Text = sub(it.Text), Points = it.Points, Explain = sub(it.Explain), Answer = it.Answer, Case = it.Case };
                if (it.Pbq != null) { foreach (var rl in it.Raw) n.Raw.Add(sub(rl)); n.Pbq = PbqSpec.Parse(it.Kind, n.Raw); n.Text = n.Pbq.Prompt; n.Explain = n.Pbq.Explain; }
                n.Answers.AddRange(it.Answers); n.Truth.AddRange(it.Truth);
                foreach (var oa in it.OrderAnswer) n.OrderAnswer.Add(sub(oa));
                foreach (var c in it.Checks) n.Checks.Add(new CheckDef { Machine = c.Machine, Desc = sub(c.Desc), Script = sub(c.Script) });
                foreach (var s in it.Solution) n.Solution.Add(new SolutionStep { Machine = s.Machine, Line = sub(s.Line) });
                foreach (var h in it.Hints) n.Hints.Add(sub(h));
                foreach (var ch in it.Choices) n.Choices.Add(sub(ch));
                if (n.Kind == "order")
                {
                    // the actions are offered in a different order each attempt
                    var sr = new Random(seed * 31 + n.Id.Length * 7 + n.Choices.Count);
                    for (int k = n.Choices.Count - 1; k > 0; k--) { int j = sr.Next(k + 1); var tmp = n.Choices[k]; n.Choices[k] = n.Choices[j]; n.Choices[j] = tmp; }
                }
                t.Items.Add(n);
            }
            t.NoLogin = new List<string>(NoLogin);
            foreach (var cs in Cases) { var nc = new CaseStudy { Id = cs.Id, Title = sub(cs.Title) }; foreach (var s in cs.Sections) nc.Sections.Add(new KeyValuePair<string, string>(sub(s.Key), sub(s.Value))); t.Cases.Add(nc); }
            t.Picked = pick;
            return t;
        }
        public Dictionary<string, string> Picked = new Dictionary<string, string>();
        public int TotalPoints { get { return Items.Sum(i => i.Points); } }
    }

    public static class CourseLoader
    {
        public static List<CourseInfo> LoadAll(string coursesDir)
        {
            var l = new List<CourseInfo>();
            if (!Directory.Exists(coursesDir)) return l;
            foreach (var d in Directory.GetDirectories(coursesDir).OrderBy(x => x, StringComparer.Ordinal))
            {
                var f = Path.Combine(d, "course.json");
                if (!File.Exists(f)) continue;
                try { l.Add(Load(d)); } catch (Exception e) { var c = new CourseInfo { Id = Path.GetFileName(d), Title = Path.GetFileName(d), Dir = d }; c.Problems.Add("course.json: " + e.Message); l.Add(c); }
            }
            return l.OrderBy(c => c.Order).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        }

        public static CourseInfo Load(string dir)
        {
            var j = Json.ParseObj(File.ReadAllText(Path.Combine(dir, "course.json")));
            var c = new CourseInfo
            {
                Dir = dir, Id = j.Str("id"), Title = j.Str("title"), Short = j.Str("short"), Simulator = j.Str("simulator", "rhel"), Description = j.Str("description"),
                Machines = j.Int("machines", 2), ExamMinutes = j.Int("examMinutes", 180), PassPercent = j.Int("passPercent", 70), ExamPoints = j.Int("examPoints", 300), ScoreMin = j.Int("scoreMin", 0), PassScore = j.Int("passScore", 0),
                Exam = j.Str("exam"), Disclaimer = j.Str("disclaimer"), ExamNote = j.Str("examNote"), ExamHint = j.Str("examHint"), AboutMachines = j.Str("aboutMachines"), Order = j.Int("order", 100)
            };
            foreach (var o in j.Arr("objectives")) c.Objectives.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
            foreach (var o in j.Arr("weights")) c.Weights.Add(Convert.ToInt32(o, CultureInfo.InvariantCulture));
            foreach (var uo in j.Arr("units"))
            {
                var u = uo as JObj; if (u == null) continue;
                var unit = new CUnit { Id = u.Str("id"), Title = u.Str("title"), Objective = u.Str("objective") };
                string file = u.Str("file");
                if (file.Length > 0)
                {
                    string p = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p)) { try { ParseLessons(File.ReadAllText(p), unit); } catch (Exception e) { c.Problems.Add(file + ": " + e.Message); } }
                    else c.Problems.Add(file + ": missing");
                }
                c.Units.Add(unit);
            }
            foreach (var f in Files(dir, "cards")) { try { c.Decks.Add(ParseDeck(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f))); } catch (Exception e) { c.Problems.Add(Path.GetFileName(f) + ": " + e.Message); } }
            foreach (var f in Files(dir, "cheats")) { try { c.Sheets.Add(ParseSheet(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f))); } catch (Exception e) { c.Problems.Add(Path.GetFileName(f) + ": " + e.Message); } }
            foreach (var f in Files(dir, "labs")) { try { var t = ParseTaskSet(File.ReadAllText(f), "Lab"); t.File = f; c.Labs.Add(t); } catch (Exception e) { c.Problems.Add(Path.GetFileName(f) + ": " + e.Message); } }
            foreach (var f in Files(dir, "exams")) { try { var t = ParseTaskSet(File.ReadAllText(f), "Exam"); t.File = f; t.RebootFirst = true; c.Exams.Add(t); } catch (Exception e) { c.Problems.Add(Path.GetFileName(f) + ": " + e.Message); } }
            foreach (var f in Files(dir, "pbq")) { try { var t = ParseTaskSet(File.ReadAllText(f), "Practice"); t.File = f; c.Practice.Add(t); } catch (Exception e) { c.Problems.Add(Path.GetFileName(f) + ": " + e.Message); } }
            foreach (var t0 in c.Labs.Concat(c.Exams).Concat(c.Practice)) { var t = t0.Vars.Count > 0 ? t0.Instance(0) : t0; t.File = t0.File; foreach (var it in t.Items) if (it.Pbq != null) foreach (var pr in it.Pbq.Problems) c.Problems.Add(Path.GetFileName(t.File) + " " + it.Id + ": " + pr); }
            return c;
        }

        static IEnumerable<string> Files(string dir, string sub)
        {
            var d = Path.Combine(dir, sub);
            if (!Directory.Exists(d)) return new string[0];
            return Directory.GetFiles(d, "*.txt").OrderBy(x => x, StringComparer.Ordinal);
        }

        static string[] Head(string line, string tag)
        {
            // "# Tag: id | title | more"
            var rest = line.Substring(line.IndexOf(':') + 1);
            return rest.Split('|').Select(x => x.Trim()).ToArray();
        }

        public static void ParseLessons(string text, CUnit unit)
        {
            var lines = text.Replace("\r", "").Split('\n');
            Lesson cur = null;
            var para = new StringBuilder();
            Action flush = () =>
            {
                if (para.Length > 0)
                {
                    var t = para.ToString().Trim();
                    if (cur != null) cur.Blocks.Add(new Block { Kind = "p", Text = t });
                    else unit.Intro += (unit.Intro.Length > 0 ? "\n\n" : "") + t;
                    para.Clear();
                }
            };
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.StartsWith("# Unit:", StringComparison.Ordinal)) { var h = Head(l, "Unit"); if (h.Length > 0 && unit.Id.Length == 0) unit.Id = h[0]; if (h.Length > 1 && unit.Title.Length == 0) unit.Title = h[1]; if (h.Length > 2 && unit.Objective.Length == 0) unit.Objective = h[2]; continue; }
                if (l.StartsWith("## Lesson:", StringComparison.Ordinal))
                {
                    flush();
                    var h = Head(l, "Lesson");
                    cur = new Lesson { Id = h[0], Title = h.Length > 1 ? h[1] : h[0], UnitId = unit.Id };
                    unit.Lessons.Add(cur);
                    continue;
                }
                if (cur == null) { if (l.Trim().Length == 0) flush(); else para.Append(l.Trim()).Append(' '); continue; }
                if (l.StartsWith("### ", StringComparison.Ordinal)) { flush(); cur.Blocks.Add(new Block { Kind = "h", Text = l.Substring(4).Trim() }); continue; }
                if (l.StartsWith("```", StringComparison.Ordinal))
                {
                    flush();
                    string kind = l.Substring(3).Trim(); if (kind.Length == 0) kind = "terminal";
                    var sb = new StringBuilder();
                    for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++) sb.Append(lines[i]).Append('\n');
                    cur.Blocks.Add(new Block { Kind = kind == "file" ? "file" : "terminal", Text = sb.ToString().TrimEnd('\n') });
                    continue;
                }
                if (l.StartsWith("- ", StringComparison.Ordinal) || l.StartsWith("* ", StringComparison.Ordinal))
                {
                    flush();
                    var b = cur.Blocks.Count > 0 && cur.Blocks[cur.Blocks.Count - 1].Kind == "ul" ? cur.Blocks[cur.Blocks.Count - 1] : null;
                    if (b == null) { b = new Block { Kind = "ul" }; cur.Blocks.Add(b); }
                    b.Items.Add(l.Substring(2).Trim());
                    continue;
                }
                if (l.StartsWith("  ", StringComparison.Ordinal) && cur.Blocks.Count > 0 && cur.Blocks[cur.Blocks.Count - 1].Kind == "ul" && para.Length == 0 && l.Trim().Length > 0) { var b = cur.Blocks[cur.Blocks.Count - 1]; b.Items[b.Items.Count - 1] += " " + l.Trim(); continue; }
                if (l.StartsWith("> ", StringComparison.Ordinal)) { flush(); cur.Blocks.Add(new Block { Kind = "note", Text = l.Substring(2).Trim() }); continue; }
                if (l.StartsWith("@try", StringComparison.Ordinal))
                {
                    flush();
                    var sb = new StringBuilder();
                    var m = Regex.Match(l, "machines=(\\d)");
                    for (i++; i < lines.Length && lines[i].Trim() != "@end"; i++) sb.Append(lines[i]).Append('\n');
                    var ts = ParseTaskSet("# Try: " + cur.Id + " | " + cur.Title + "\n" + sb, "Try");
                    ts.Machines = m.Success ? int.Parse(m.Groups[1].Value) : 1;
                    cur.Try = ts;
                    continue;
                }
                if (l.StartsWith("@quiz", StringComparison.Ordinal))
                {
                    flush();
                    QuizQ q = null;
                    for (i++; i < lines.Length && lines[i].Trim() != "@end"; i++)
                    {
                        string ql = lines[i];
                        if (ql.StartsWith("? ", StringComparison.Ordinal)) { q = new QuizQ { Q = ql.Substring(2).Trim() }; cur.Quiz.Add(q); }
                        else if (q != null && ql.StartsWith("- ", StringComparison.Ordinal)) q.Choices.Add(ql.Substring(2).Trim());
                        else if (q != null && ql.StartsWith("+ ", StringComparison.Ordinal)) { q.Answer = q.Choices.Count; q.Choices.Add(ql.Substring(2).Trim()); }
                        else if (q != null && ql.StartsWith("! ", StringComparison.Ordinal)) q.Why += (q.Why.Length > 0 ? " " : "") + ql.Substring(2).Trim();
                    }
                    continue;
                }
                if (l.Trim().Length == 0) { flush(); continue; }
                para.Append(l.Trim()).Append(' ');
            }
            flush();
        }

        public static Deck ParseDeck(string text, string id)
        {
            var d = new Deck { Id = id };
            int n = 0;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var l = raw.TrimEnd();
                if (l.StartsWith("# Deck:", StringComparison.Ordinal)) { var h = Head(l, "Deck"); d.Id = h[0]; if (h.Length > 1) d.Title = h[1]; if (h.Length > 2) d.Objective = h[2]; continue; }
                if (l.Trim().Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                int sep = l.IndexOf(" :: ", StringComparison.Ordinal);
                if (sep < 0) continue;
                var c = new Card { Front = l.Substring(0, sep).Trim(), Back = l.Substring(sep + 4).Trim(), Deck = d.Id, Id = d.Id + "-" + (++n) };
                var cm = Regex.Match(c.Back, "`([^`]+)`");
                if (cm.Success) c.Command = cm.Groups[1].Value;
                d.Cards.Add(c);
            }
            if (d.Title.Length == 0) d.Title = id;
            return d;
        }

        public static Sheet ParseSheet(string text, string id)
        {
            var s = new Sheet { Id = id };
            SheetSection sec = null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var l = raw.TrimEnd();
                if (l.StartsWith("# Sheet:", StringComparison.Ordinal)) { var h = Head(l, "Sheet"); s.Id = h[0]; if (h.Length > 1) s.Title = h[1]; if (h.Length > 2) s.Objective = h[2]; continue; }
                if (l.StartsWith("## ", StringComparison.Ordinal)) { sec = new SheetSection { Title = l.Substring(3).Trim() }; s.Sections.Add(sec); continue; }
                if (l.Trim().Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                int sep = l.IndexOf(" :: ", StringComparison.Ordinal);
                if (sep < 0) continue;
                if (sec == null) { sec = new SheetSection { Title = "" }; s.Sections.Add(sec); }
                sec.Rows.Add(new SheetRow { Cmd = l.Substring(0, sep).Trim().Trim('`'), Meaning = l.Substring(sep + 4).Trim() });
            }
            if (s.Title.Length == 0) s.Title = id;
            return s;
        }

        /// <summary>A lab, an exam or a lesson's try-it (format in FORMAT.md).</summary>
        public static TaskSet ParseTaskSet(string text, string tag)
        {
            var t = new TaskSet();
            var lines = text.Replace("\r", "").Split('\n');
            TaskItem item = null;
            string mode = "intro"; string mach = "server1";
            CheckDef check = null;
            var intro = new StringBuilder(); var itemText = new StringBuilder(); var explain = new StringBuilder();
            Action endItem = () =>
            {
                if (item == null) return;
                item.Text = itemText.ToString().Trim(); item.Explain = explain.ToString().Trim();
                if (PbqSpec.IsPbq(item.Kind)) { item.Pbq = PbqSpec.Parse(item.Kind, item.Raw); item.Text = item.Pbq.Prompt; item.Explain = item.Pbq.Explain; }
                itemText.Clear(); explain.Clear();
                item = null;
            };
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (Regex.IsMatch(l, "^# (Lab|Exam|Try|Practice):"))
                {
                    var h = Head(l, tag);
                    t.Id = h[0]; if (h.Length > 1) t.Title = h[1];
                    if (tag == "Lab") { if (h.Length > 2) t.Unit = h[2]; int mn; if (h.Length > 3 && int.TryParse(h[3], out mn)) t.Minutes = mn; }
                    if (tag == "Exam") { int mn; if (h.Length > 2 && int.TryParse(h[2], out mn)) t.Minutes = mn; }
                    continue;
                }
                if (l.StartsWith("@nologin", StringComparison.Ordinal)) { foreach (var x in l.Substring(8).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) t.NoLogin.Add(x); continue; }
                if (l.StartsWith("@vars", StringComparison.Ordinal)) { mode = "vars"; continue; }
                if (l.StartsWith("@endvars", StringComparison.Ordinal)) { mode = "none"; continue; }
                if (l.StartsWith("@setup", StringComparison.Ordinal)) { endItem(); mode = "setup"; mach = Arg(l, "server1"); continue; }
                if (l.StartsWith("@task", StringComparison.Ordinal))
                {
                    endItem();
                    var h = l.Substring(5).Split('|').Select(x => x.Trim()).ToArray();
                    item = new TaskItem { Id = h.Length > 0 && h[0].Length > 0 ? h[0] : "t" + (t.Items.Count + 1), Objective = h.Length > 1 ? h[1] : "", Machine = h.Length > 3 && h[3].Length > 0 ? h[3] : "server1" };
                    int pts; if (h.Length > 2 && int.TryParse(h[2], out pts)) item.Points = pts;
                    t.Items.Add(item);
                    mode = "task";
                    continue;
                }
                var qk = Regex.Match(l, "^@(choice|multi|order|yesno|match|firewall|logs|place|cert|risk)\\b");
                if (qk.Success)
                {
                    endItem();
                    var h = l.Substring(qk.Length).Split('|').Select(x => x.Trim()).ToArray();
                    item = new TaskItem { Kind = qk.Groups[1].Value, Id = h.Length > 0 && h[0].Length > 0 ? h[0] : "c" + (t.Items.Count + 1), Objective = h.Length > 1 ? h[1] : "" };
                    int pts; if (h.Length > 2 && int.TryParse(h[2], out pts)) item.Points = pts;
                    foreach (var extra in h.Skip(3)) if (extra.StartsWith("case=", StringComparison.Ordinal)) item.Case = extra.Substring(5).Trim();
                    t.Items.Add(item);
                    mode = PbqSpec.IsPbq(item.Kind) ? "pbq" : item.Kind;
                    continue;
                }
                if (l.StartsWith("@case", StringComparison.Ordinal))
                {
                    endItem();
                    var h = l.Substring(5).Split('|').Select(x => x.Trim()).ToArray();
                    var cs = new CaseStudy { Id = h[0], Title = h.Length > 1 ? h[1] : h[0] };
                    string head = ""; var body = new StringBuilder();
                    for (i++; i < lines.Length && !lines[i].StartsWith("@endcase", StringComparison.Ordinal); i++)
                    {
                        if (lines[i].StartsWith("## ", StringComparison.Ordinal)) { if (head.Length > 0 || body.Length > 0) cs.Sections.Add(new KeyValuePair<string, string>(head, body.ToString().Trim())); head = lines[i].Substring(3).Trim(); body.Clear(); }
                        else body.Append(lines[i]).Append('\n');
                    }
                    if (head.Length > 0 || body.Length > 0) cs.Sections.Add(new KeyValuePair<string, string>(head, body.ToString().Trim()));
                    t.Cases.Add(cs);
                    mode = "none";
                    continue;
                }
                if (l.StartsWith("@check", StringComparison.Ordinal))
                {
                    var h = l.Substring(6).Split(new[] { '|' }, 2).Select(x => x.Trim()).ToArray();
                    check = new CheckDef { Machine = h.Length > 0 && h[0].Length > 0 ? h[0] : (item != null ? item.Machine : "server1"), Desc = h.Length > 1 ? h[1] : "" };
                    if (item == null) { item = new TaskItem { Id = "t" + (t.Items.Count + 1) }; t.Items.Add(item); }
                    item.Checks.Add(check);
                    mode = "check";
                    continue;
                }
                if (l.StartsWith("@solution", StringComparison.Ordinal)) { mode = "solution"; mach = Arg(l, item != null ? item.Machine : "server1"); continue; }
                if (l.StartsWith("@hint", StringComparison.Ordinal)) { mode = "hint"; if (item != null) item.Hints.Add(l.Substring(5).Trim()); continue; }
                if (l.StartsWith("@explain", StringComparison.Ordinal)) { mode = "explain"; continue; }
                if (l.StartsWith("@endtask", StringComparison.Ordinal)) { endItem(); mode = "none"; continue; }
                switch (mode)
                {
                    case "intro": intro.Append(l).Append('\n'); break;
                    case "vars":
                        {
                            int eq = l.IndexOf('=');
                            if (eq > 0) t.Vars[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                            break;
                        }
                    case "setup": if (l.Trim().Length > 0) t.Setup.Add(new SolutionStep { Machine = mach, Line = l }); break;
                    case "task": itemText.Append(l).Append('\n'); break;
                    case "pbq": item.Raw.Add(l); break;
                    case "check": check.Script += (check.Script.Length > 0 ? "\n" : "") + l; check.Script = check.Script.TrimEnd('\n'); break;
                    case "solution": if (l.Trim().Length > 0 && item != null) item.Solution.Add(new SolutionStep { Machine = mach, Line = l }); break;
                    case "hint": if (item != null && l.Trim().Length > 0) item.Hints[item.Hints.Count - 1] = (item.Hints[item.Hints.Count - 1] + " " + l.Trim()).Trim(); break;
                    case "explain": explain.Append(l).Append('\n'); break;
                    case "choice": case "multi":
                        if (l.StartsWith("- ", StringComparison.Ordinal)) item.Choices.Add(l.Substring(2).Trim());
                        else if (l.StartsWith("+ ", StringComparison.Ordinal)) { item.Answer = item.Choices.Count; item.Answers.Add(item.Choices.Count); item.Choices.Add(l.Substring(2).Trim()); }
                        else if (l.StartsWith("! ", StringComparison.Ordinal)) explain.Append(l.Substring(2).Trim()).Append(' ');
                        else itemText.Append(l).Append('\n');
                        break;
                    case "order":
                        {
                            var om = Regex.Match(l, "^(\\d+|x) (.+)$");
                            if (om.Success) { var txt = om.Groups[2].Value.Trim(); item.Choices.Add(txt); if (om.Groups[1].Value != "x") item.OrderAnswer.Add(txt); }
                            else if (l.StartsWith("! ", StringComparison.Ordinal)) explain.Append(l.Substring(2).Trim()).Append(' ');
                            else itemText.Append(l).Append('\n');
                            break;
                        }
                    case "yesno":
                        if (l.StartsWith("Y ", StringComparison.Ordinal) || l.StartsWith("N ", StringComparison.Ordinal)) { item.Choices.Add(l.Substring(2).Trim()); item.Truth.Add(l[0] == 'Y'); }
                        else if (l.StartsWith("! ", StringComparison.Ordinal)) explain.Append(l.Substring(2).Trim()).Append(' ');
                        else itemText.Append(l).Append('\n');
                        break;
                }
            }
            endItem();
            t.Intro = intro.ToString().Trim();
            foreach (var it in t.Items) foreach (var c in it.Checks) c.Script = c.Script.Trim('\n');
            return t;
        }
        static string Arg(string l, string def) { var p = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); return p.Length > 1 ? p[1] : def; }
    }
}
