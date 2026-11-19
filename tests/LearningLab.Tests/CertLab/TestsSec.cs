// Cert Lab - checks of the performance-based questions (Security+): each kind's grader with right, partly right and
// wrong answers, the firewall evaluator's rule order and implicit deny, the risk arithmetic, and the 100-900 scale
// with its 750 mark. The course content is checked in TestsSecContent.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static IEnumerable<KeyValuePair<string, Action>> SecGroups()
        {
            yield return new KeyValuePair<string, Action>("sec-pbq", SecPbqTests);
            yield return new KeyValuePair<string, Action>("sec-content", SecContentTests);
            yield return new KeyValuePair<string, Action>("sec-search", SecSearchTests);
            yield return new KeyValuePair<string, Action>("sec-tools", SecToolTests);
            yield return new KeyValuePair<string, Action>("sec-tries", () => SecSetTests("tries"));
            yield return new KeyValuePair<string, Action>("sec-labs", () => SecSetTests("labs"));
            yield return new KeyValuePair<string, Action>("sec-exams", SecExamTests);
        }

        /// <summary>One PBQ item from text (as in an exam file).</summary>
        static TaskItem Pbq(string text)
        {
            var t = CourseLoader.ParseTaskSet("# Exam: t | T | 90\n" + text, "Exam");
            var it = t.Items[0];
            Check("PBQ parses: " + it.Id, it.Pbq != null && it.Pbq.Problems.Count == 0, it.Pbq == null ? "no spec" : string.Join("; ", it.Pbq.Problems.ToArray()));
            return it;
        }
        static int Pts(TaskItem it, params string[] ans) { bool p; return TaskRunner.ScoreMore(it, ans.ToList(), out p); }

        static void SecPbqTests()
        {
            // ---- match
            var m = Pbq("@match m1 | Threats, vulnerabilities and mitigations | 4\nMatch them.\ncolumns Attack | Mitigation\n= Phishing -> Awareness training\n= SQL injection -> Input validation\n= Tailgating -> Access control vestibule\n= Brute force -> Account lockout\nx Full-disk encryption\n! Because.");
            Check("match: four pairs and five cards", m.Pbq.Pairs.Count == 4 && m.Pbq.Targets.Count == 5 && m.Pbq.LeftTitle == "Attack");
            Check("match: all right scores 4", Pts(m, "m:0=Awareness training", "m:1=Input validation", "m:2=Access control vestibule", "m:3=Account lockout") == 4);
            Check("match: two right scores 2", Pts(m, "m:0=Awareness training", "m:1=Full-disk encryption", "m:2=Access control vestibule") == 2);
            Check("match: a distractor everywhere scores 0", Pts(m, "m:0=Full-disk encryption", "m:1=Full-disk encryption", "m:2=Full-disk encryption", "m:3=Full-disk encryption") == 0);
            Check("match: the explanation is kept", m.Explain == "Because." && m.Text == "Match them.", m.Explain + "|" + m.Text);

            // ---- firewall: first match wins, implicit deny, the empty table
            var f = Pbq("@firewall f1 | Security operations | 6\nRules.\nrows 4\naddr Internet = 0.0.0.0/0\naddr LAN = 10.0.1.0/24\naddr Web server = 10.0.2.10\naddr Bad host = 203.0.113.66\nports 22, 80, 443\n" +
                "test allow | 203.0.113.9 -> 10.0.2.10 | 443/TCP | customers reach HTTPS\ntest deny | 203.0.113.66 -> 10.0.2.10 | 443/TCP | the attacker is blocked\ntest allow | 10.0.1.20 -> 10.0.2.10 | 22/TCP | admins use SSH\ntest deny | 203.0.113.9 -> 10.0.2.10 | 22/TCP | no SSH from outside\n" +
                "rule DENY | Bad host | Any | Any | Any\nrule ALLOW | Any | Web server | 443 | TCP\nrule ALLOW | LAN | Web server | 22 | TCP\n! Order matters.");
            var s = f.Pbq;
            var model = PbqGrade.ModelAnswer(s);
            Check("firewall: the model answer scores 6", Pts(f, model.ToArray()) == 6, string.Join(" ; ", model.ToArray()));
            Check("firewall: the empty table scores 0", Pts(f) == 0);
            Check("firewall: 'deny everything' scores 0 (no wanted packet gets through)", Pts(f, "r:0=DENY|Any|Any|Any|Any") == 0);
            Check("firewall: allow-all first gets only the allow tests (3 of 6)", Pts(f, "r:0=ALLOW|Any|Any|Any|Any", "r:1=DENY|Bad host|Any|Any|Any") == 3);
            Check("firewall: the deny after the broad allow is too late (4 of 6 tests: 1 deny lost)", Pts(f, "r:0=ALLOW|Any|Web server|443|TCP", "r:1=DENY|Bad host|Any|Any|Any", "r:2=ALLOW|LAN|Web server|22|TCP") == 4);
            int no;
            var rules = PbqGrade.Rules(s, new List<string> { "r:0=ALLOW|Any|Web server|443|TCP", "r:1=DENY|Bad host|Any|Any|Any" });
            Check("evaluator: the first matching rule decides", PbqGrade.Evaluate(s, rules, s.Tests[1], out no) && no == 1);
            Check("evaluator: no match is the implicit deny (rule 0)", !PbqGrade.Evaluate(s, rules, s.Tests[2], out no) && no == 0);
            Check("evaluator: an incomplete row is ignored", PbqGrade.Rules(s, new List<string> { "r:0=ALLOW|Any||443|TCP" }).Count == 0);
            Check("evaluator: CIDR matching", AddrIn(s, "LAN", "10.0.1.200") && !AddrIn(s, "LAN", "10.0.2.1"));
            var g = PbqGrade.Grade(s, 6, new List<string> { "r:0=ALLOW|Any|Any|Any|Any" });
            Check("firewall review names the deciding rule", g.Parts[1].Yours == "allowed by rule 1" && g.Parts[1].Right == "blocked", g.Parts[1].Yours);
            var fs = Pbq("@firewall f2 | Security operations | 4\nFix it.\nrows 3\naddr LAN = 10.0.1.0/24\nports 53\nstart ALLOW | Any | Any | Any | Any\nstart DENY | LAN | Any | 53 | UDP\ntest deny | 10.0.1.5 -> 198.51.100.1 | 53/UDP | no outside DNS\ntest allow | 10.0.1.5 -> 198.51.100.1 | 443/TCP | web works\nrule DENY | LAN | Any | 53 | UDP\nrule ALLOW | Any | Any | Any | Any\n! Swap them.");
            Check("firewall: the start rows count until changed (allow-all first: 1 of 2)", Pts(fs) == 2);
            Check("firewall: the model answer replaces every start row", Pts(fs, PbqGrade.ModelAnswer(fs.Pbq).ToArray()) == 4);

            // ---- logs: indicators (wrong marks take away), sub-questions
            var l = Pbq("@logs l1 | Threats, vulnerabilities and mitigations | 6\nRead it.\n@log auth.log\n  Mar  2 09:00:01 web1 sshd[1]: Accepted publickey for ana from 10.0.1.5\n* Mar  2 09:01:01 web1 sshd[2]: Failed password for root from 203.0.113.7\n* Mar  2 09:01:02 web1 sshd[3]: Failed password for root from 203.0.113.7\n  Mar  2 09:02:00 web1 CRON[4]: session opened\n@endlog\n" +
                "? attack | What is it?\n- Phishing\n+ Brute force\n?? fix | Which two? (pick two)\n+ Lockout\n+ Keys only\n- Reboot\n! Brute force.");
            Check("logs: 2 indicators, 2 questions, 3 parts", l.Pbq.IndicatorCount == 2 && l.Pbq.Questions.Count == 2 && PbqGrade.PartCount(l.Pbq) == 3);
            Check("logs: all right scores 6", Pts(l, "l:0:1", "l:0:2", "q:attack=1", "q:fix=0,1") == 6);
            Check("logs: one indicator and the attack: 1 + 2 of 6", Pts(l, "l:0:1", "q:attack=1") == 3);
            Check("logs: a wrong mark takes one away (2 - 1 of 2 indicators = half a part)", Pts(l, "l:0:1", "l:0:2", "l:0:0", "q:attack=1", "q:fix=0,1") == 5);
            Check("logs: half the multi-answer is wrong", Pts(l, "q:fix=0") == 0);
            Check("logs: a third choice spoils the multi-answer", Pts(l, "q:fix=0,1,2") == 0);
            Check("logs: lines keep their text without the marker", l.Pbq.Exhibits[0].Lines[1].StartsWith("Mar  2 09:01:01"));

            // ---- place
            var p = Pbq("@place p1 | Security architecture | 4\nPlace them.\nzone inet | Internet\nzone dmz | Screened subnet | Edge firewall\nzone lan | Internal network | Internal firewall\ndevice Web application firewall -> dmz\ndevice Database -> lan\ndevice NIDS sensor -> lan, dmz\ndevice Reverse proxy -> dmz\n! Zones.");
            Check("place: all right scores 4", Pts(p, "d:0=dmz", "d:1=lan", "d:2=dmz", "d:3=dmz") == 4);
            Check("place: either listed zone is right", Pts(p, "d:2=lan") == 1 && Pts(p, "d:2=dmz") == 1);
            Check("place: wrong zones score 0", Pts(p, "d:0=inet", "d:1=inet", "d:2=inet", "d:3=lan") == 0);
            Check("place: a bad zone name is reported", PbqSpec.Parse("place", new List<string> { "zone a | A", "device X -> b" }).Problems.Any(x => x.Contains("no zone b")));

            // ---- cert (an exhibit plus questions)
            var c = Pbq("@cert c1 | General security concepts | 4\nLook.\n@exhibit Certificate\nCertificate:\n    Data:\n        Version: 3 (0x2)\n@endexhibit\n? wrong | What is wrong?\n+ It expired\n- Nothing\n? type | Which type for many subdomains?\n- Self-signed\n+ Wildcard\n! Dates.");
            Check("cert: the exhibit keeps its indentation", !c.Pbq.Exhibits[0].IsLog && c.Pbq.Exhibits[0].Lines[1] == "    Data:");
            Check("cert: both right 4, one right 2, none 0", Pts(c, "q:wrong=0", "q:type=1") == 4 && Pts(c, "q:wrong=0", "q:type=0") == 2 && Pts(c, "q:wrong=1") == 0);

            // ---- risk arithmetic
            var r = Pbq("@risk r1 | Security program management and oversight | 6\nA server is worth 40,000.\ncalc sle | SLE | = 40000 * 25 / 100\ncalc ale | ALE | = 40000 * 0.25 * 0.5\ncalc save | Savings | = (40000 * 0.25 * 0.5) - (40000 * 0.25 * 0.1) - 1500\n? strat | Strategy?\n+ Mitigate\n- Avoid\n! Maths.");
            Check("risk: SLE = AV x EF", Math.Abs(r.Pbq.Calcs[0].Value - 10000) < 1e-9, r.Pbq.Calcs[0].Value.ToString(CultureInfo.InvariantCulture));
            Check("risk: ALE = SLE x ARO", Math.Abs(r.Pbq.Calcs[1].Value - 5000) < 1e-9);
            Check("risk: parentheses and precedence", Math.Abs(r.Pbq.Calcs[2].Value - 2500) < 1e-9, r.Pbq.Calcs[2].Value.ToString(CultureInfo.InvariantCulture));
            Check("risk: '$10,000' and 5000.00 are accepted", Pts(r, "c:sle=$10,000", "c:ale=5000.00", "c:save=2,500", "q:strat=0") == 6);
            Check("risk: 1 dollar off is wrong for 10,000 (0.2% is 20)", Pts(r, "c:sle=10001") == 1);
            Check("risk: 9,000 is wrong", Pts(r, "c:sle=9000") == 0);
            Check("risk: words are not numbers", Pts(r, "c:sle=ten thousand") == 0);
            double v; string err;
            Check("eval: unary minus and division", PbqGrade.TryEval("-(6 - 10) / 8", out v, out err) && Math.Abs(v - 0.5) < 1e-12);
            Check("eval: a missing parenthesis is an error", !PbqGrade.TryEval("(1 + 2", out v, out err) && err != null);
            Check("eval: garbage is an error", !PbqGrade.TryEval("12 apples", out v, out err));
            Check("a calc that doesn't compute is a problem", PbqSpec.Parse("risk", new List<string> { "calc a | A | = 1 +" }).Problems.Count > 0);

            // ---- vars fill PBQs per attempt
            var set = CourseLoader.ParseTaskSet("# Exam: t | T | 90\n@vars\nav = 80000 | 120000\n@endvars\n@risk r1 | Security program management and oversight | 2\nWorth {av}.\ncalc sle | SLE | = {av} * 0.5\n? s | S?\n+ A\n- B\n", "Exam");
            var i1 = set.Instance(1).Items[0];
            Check("vars: the calc follows the picked value", Math.Abs(i1.Pbq.Calcs[0].Value * 2 - double.Parse(set.Instance(1).Picked["av"], CultureInfo.InvariantCulture)) < 1e-9 && i1.Text.StartsWith("Worth "), i1.Text);

            // ---- the 100-900 scale and the 750 mark
            var crs = new CourseInfo { ExamPoints = 900, ScoreMin = 100, PassScore = 750 };
            Check("scale: nothing right is 100", crs.Scale(0, 110) == 100);
            Check("scale: everything right is 900", crs.Scale(110, 110) == 900);
            Check("scale: half is 500", crs.Scale(55, 110) == 500);
            Check("scale: 81.25% is exactly 750, the pass mark", crs.Scale(65, 80) == 750 && crs.PassMark == 750);
            Check("scale: 80% is below the mark", crs.Scale(64, 80) < 750);
            var ms = new CourseInfo { ExamPoints = 1000, ScoreMin = 1, PassPercent = 70 };
            Check("scale: Microsoft courses unchanged (share of 1000, at least 1)", ms.Scale(0, 50) == 1 && ms.Scale(35, 50) == 700 && ms.PassMark == 700);
        }
        static bool AddrIn(PbqSpec s, string name, string ip)
        {
            int no;
            var rules = new List<PbqRule> { new PbqRule { Action = "ALLOW", Src = name, Dst = "Any", Port = "Any", Proto = "ANY" } };
            return PbqGrade.Evaluate(s, rules, new PbqTest { Src = ip, Dst = "1.2.3.4", Port = 1, Proto = "TCP" }, out no);
        }
    }
}
