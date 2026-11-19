// Cert Lab - checks of the Security+ course content: every file parses, lessons and quizzes, decks (300+ acronyms),
// cheat sheets, exams (size, PBQs, domain weights, model answers 0 -> 900 on two seeds, the 750 mark), PBQ practice,
// search, the security tools (openssl, nmap, certlab-mkcert), labs and try-its on the RHEL machines, the PBQ views.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static CourseInfo secCourse;
        static CourseInfo Sec()
        {
            if (secCourse == null) secCourse = CourseLoader.Load(Path.Combine(AppRootForTests, "Courses", "secplus"));
            return secCourse;
        }

        static void SecContentTests()
        {
            var all = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses"));
            Check("Home lists five courses, Security+ last", all.Count == 5 && all[4].Id == "secplus", string.Join(", ", all.Select(x => x.Id).ToArray()));
            var c = Sec();
            Check("course files parse", c.Problems.Count == 0, string.Join("\n", c.Problems.ToArray()));
            Check("100-900, pass 750", c.ScoreMin == 100 && c.ExamPoints == 900 && c.PassMark == 750 && c.ExamMinutes == 90);
            Check("five domains with weights adding up to 100", c.Objectives.Count == 5 && c.Weights.Count == 5 && c.Weights.Sum() == 100);
            Check("the disclaimer says not affiliated with CompTIA and approximate weights", c.Disclaimer.Contains("not affiliated") && c.Disclaimer.Contains("CompTIA") && c.Disclaimer.Contains("approximate"));
            Check("19 units, each with lessons", c.Units.Count == 19 && c.Units.All(u => u.Lessons.Count >= 2), string.Join(", ", c.Units.Where(u => u.Lessons.Count < 2).Select(u => u.Id).ToArray()));
            Check("every unit's objective is a domain", c.Units.All(u => c.Objectives.Contains(u.Objective)));
            Check("every domain has units", c.Objectives.All(o => c.Units.Any(u => u.Objective == o)));
            Check("lesson ids are unique", c.AllLessons.Select(l => l.Id).Distinct().Count() == c.AllLessons.Count());
            foreach (var l in c.AllLessons)
            {
                Check(l.Id + " has a quiz of 3+", l.Quiz.Count >= 3, l.Quiz.Count.ToString());
                foreach (var q in l.Quiz) Check(l.Id + " quiz '" + Short(q.Q) + "': one right answer of 4 and a reason", q.Answer >= 0 && q.Choices.Count == 4 && q.Why.Length > 0 && q.Choices.All(x => x.Length > 0));
                Check(l.Id + " has no unchecked terminal examples", !l.Blocks.Any(b => b.Kind == "terminal") || l.Blocks.Where(b => b.Kind == "terminal").All(b => b.Text.Split('\n').All(x => !Regex.IsMatch(x, "^\\[[^\\]]+\\][#$] ") || CommandsIn(x.Substring(x.IndexOf(' ') + 1)).All(Known))));
            }
            int quiz = c.AllLessons.Sum(l => l.Quiz.Count);
            Check("150+ quiz questions", quiz >= 150, quiz.ToString());
            foreach (var f in Directory.GetFiles(c.Dir, "*.*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(f);
                Check(Path.GetFileName(f) + " is ASCII", text.All(ch => ch < 128 && (ch >= 32 || ch == '\n' || ch == '\r' || ch == '\t')), FirstNonAscii(text));
            }
            int acr = c.Decks.Where(d => d.Id.StartsWith("acronyms")).Sum(d => d.Cards.Count);
            Check("300+ acronym cards", acr >= 300, acr.ToString());
            var fronts = c.Decks.Where(d => d.Id.StartsWith("acronyms")).SelectMany(d => d.Cards).Select(x => x.Front).ToList();
            foreach (var must in new[] { "AES", "CVSS", "DMARC", "MTTR", "RPO", "SASE", "SOAR", "TPM", "XDR", "ALE" })
                Check("the acronym deck has " + must, fronts.Any(x => x == must || x.StartsWith(must + " ")), must);
            Check("no card uses type-the-answer backticks", c.Decks.All(d => d.Cards.All(x => x.Command == null)), string.Join(", ", c.Decks.SelectMany(d => d.Cards).Where(x => x.Command != null).Select(x => x.Id).Take(5).ToArray()));
            Check("every domain has a deck and a cheat sheet", c.Objectives.All(o => c.Decks.Any(d => d.Objective == o) && c.Sheets.Any(s => s.Objective == o)));
            foreach (var d in c.Decks) Check("deck " + d.Id + ": objective known, 20+ cards", c.Objectives.Contains(d.Objective) && d.Cards.Count >= 20, d.Objective + " " + d.Cards.Count);
            foreach (var s in c.Sheets) Check("sheet " + s.Id + ": objective known, 25+ rows", c.Objectives.Contains(s.Objective) && s.Sections.Sum(x => x.Rows.Count) >= 25, s.Objective);
            foreach (var id in new[] { "ports", "attacks", "crypto", "risk", "incident-response", "frameworks" }) Check("cheat sheet " + id, c.Sheets.Any(s => s.Id == id));

            // practice exams
            Check("3+ practice exams", c.Exams.Count >= 3, c.Exams.Count.ToString());
            foreach (var e in c.Exams)
            {
                int pbq = e.Items.Count(i => i.Pbq != null);
                Check(e.Id + ": 80-90 items", e.Items.Count >= 80 && e.Items.Count <= 90, e.Items.Count.ToString());
                Check(e.Id + ": 3-5 PBQs", pbq >= 3 && pbq <= 5, pbq.ToString());
                Check(e.Id + ": 90 minutes", e.Minutes == 90);
                Check(e.Id + ": questions only", e.Items.All(i => i.IsQuestion));
                Check(e.Id + ": ids unique", e.Items.Select(i => i.Id).Distinct().Count() == e.Items.Count);
                Check(e.Id + ": objectives known", e.Items.All(i => c.Objectives.Contains(i.Objective)), string.Join(", ", e.Items.Where(i => !c.Objectives.Contains(i.Objective)).Select(i => i.Id + "=" + i.Objective).ToArray()));
                Check(e.Id + ": every item explained", e.Items.All(i => i.Explain.Length > 0), string.Join(", ", e.Items.Where(i => i.Explain.Length == 0).Select(i => i.Id).ToArray()));
                Check(e.Id + ": one-answer questions have one right of 4", e.Items.Where(i => i.Kind == "choice").All(i => i.Answers.Count == 1 && i.Choices.Count == 4), string.Join(", ", e.Items.Where(i => i.Kind == "choice" && !(i.Answers.Count == 1 && i.Choices.Count == 4)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": multiple-answer questions have 2+ right of 5+", e.Items.Where(i => i.Kind == "multi").All(i => i.Answers.Count >= 2 && i.Choices.Count >= 5), string.Join(", ", e.Items.Where(i => i.Kind == "multi" && !(i.Answers.Count >= 2 && i.Choices.Count >= 5)).Select(i => i.Id).ToArray()));
                Check(e.Id + ": some multiple-answer questions", e.Items.Count(i => i.Kind == "multi") >= 5);
                Check(e.Id + ": the right answer isn't always in the same place", e.Items.Where(i => i.Kind == "choice").Select(i => i.Answer).Distinct().Count() >= 4);
                int tot = e.TotalPoints;
                for (int k = 0; k < c.Objectives.Count; k++)
                {
                    double share = e.Items.Where(i => i.Objective == c.Objectives[k]).Sum(i => i.Points) * 100.0 / tot;
                    Check(e.Id + ": " + c.Objectives[k] + " is close to its weight (" + c.Weights[k] + "%)", Math.Abs(share - c.Weights[k]) <= 4, share.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                }
                foreach (var it in e.Items.Where(i => i.Pbq != null)) SecPbqModel(e.Id, it);
            }
            Check("every PBQ kind appears in the exams", PbqSpec.Kinds.All(k => c.Exams.Any(e => e.Items.Any(i => i.Kind == k))), string.Join(", ", PbqSpec.Kinds.Where(k => !c.Exams.Any(e => e.Items.Any(i => i.Kind == k))).ToArray()));
            // PBQ practice: each kind with several scenarios
            foreach (var k in PbqSpec.Kinds)
            {
                int n = c.Practice.SelectMany(p => p.Items).Count(i => i.Kind == k);
                Check("PBQ practice: 3+ " + k + " scenarios", n >= 3, n.ToString());
            }
            foreach (var p in c.Practice) foreach (var it in p.Instance(0).Items) { Check(Path.GetFileName(p.File) + " " + it.Id + " is a PBQ", it.Pbq != null); if (it.Pbq != null) SecPbqModel(Path.GetFileName(p.File), it); }
            Check("labs: 8+", c.Labs.Count >= 8, c.Labs.Count.ToString());
        }
        static string Short(string s) { return s.Length > 50 ? s.Substring(0, 50) + "..." : s; }
        static string FirstNonAscii(string t) { for (int i = 0; i < t.Length; i++) if (t[i] >= 128) return "at " + i + ": '" + t.Substring(Math.Max(0, i - 20), Math.Min(40, t.Length - Math.Max(0, i - 20))) + "'"; return ""; }

        /// <summary>A PBQ's model answer earns everything, no answer earns nothing, and it is answerable.</summary>
        static void SecPbqModel(string where, TaskItem it)
        {
            bool p;
            int full = TaskRunner.ScoreMore(it, PbqGrade.ModelAnswer(it.Pbq), out p);
            Check(where + " " + it.Id + ": the model answer scores " + it.Points, full == it.Points && p, full + " " + string.Join("; ", PbqGrade.Grade(it.Pbq, it.Points, PbqGrade.ModelAnswer(it.Pbq)).Parts.Where(x => !x.Pass).Select(x => x.Label + ": " + x.Yours).ToArray()));
            if (it.Pbq.Start.Count == 0) Check(where + " " + it.Id + ": no answer scores 0", TaskRunner.ScoreMore(it, new List<string>(), out p) == 0);
            else Check(where + " " + it.Id + ": the broken start rows are not already right", TaskRunner.ScoreMore(it, new List<string>(), out p) < it.Points);
            Check(where + " " + it.Id + ": has an explanation and a prompt", it.Explain.Length > 0 && it.Text.Length > 0);
            Check(where + " " + it.Id + ": 3+ parts", PbqGrade.PartCount(it.Pbq) >= 3, PbqGrade.PartCount(it.Pbq).ToString());
            var s = it.Pbq;
            if (s.Tests.Count > 0)
            {
                Check(where + " " + it.Id + ": firewall tests allow and deny", s.Tests.Any(t => t.Allow) && s.Tests.Any(t => !t.Allow));
                // the order matters: the model rules reversed must lose points (unless one rule)
                var rev = new List<string>(); for (int i = 0; i < s.Model.Count; i++) rev.Add("r:" + i + "=" + PbqGrade.Row(s.Model[s.Model.Count - 1 - i]));
                for (int i = s.Model.Count; i < Math.Max(s.Rows, s.Start.Count); i++) rev.Add("r:" + i + "=");
                if (where.StartsWith("exam")) Check(where + " " + it.Id + ": the rule order matters (reversed rules lose points)", TaskRunner.ScoreMore(it, rev, out p) < it.Points);
                Check(where + " " + it.Id + ": an allow-everything rule doesn't pass", TaskRunner.ScoreMore(it, new List<string> { "r:0=ALLOW|Any|Any|Any|Any" }.Concat(Enumerable.Range(1, Math.Max(s.Rows, s.Start.Count)).Select(i => "r:" + i + "=")).ToList(), out p) < it.Points);
            }
            if (s.Exhibits.Any(x => x.IsLog)) Check(where + " " + it.Id + ": logs have indicators to find", s.IndicatorCount >= 1);
            if (s.Pairs.Count > 0) Check(where + " " + it.Id + ": every match card is offered", s.Pairs.All(x => s.Targets.Contains(x.Right)));
        }
    }
}
