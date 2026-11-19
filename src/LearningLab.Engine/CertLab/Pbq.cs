// Cert Lab - performance-based questions (PBQs, the Security+ course): exam items with several parts that give
// partial credit. Kinds: match (drag-and-drop matching), firewall (fill a rule table; graded by sending test
// packets through it in order), logs (pick the indicators in logs, then the attack and the response), place (put
// appliances into the zones of a drawn network), cert (read a certificate exhibit, answer about it), risk (work out
// SLE / ALE, pick a strategy). One parser for all of them: a PBQ may mix parts (format in Courses\FORMAT.md, "The
// Security+ course"). Answers are "key=value" strings in the exam's answer list (PbqGrade.cs scores them).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class PbqQuestion { public string Id = "", Text = ""; public bool Multi; public List<string> Choices = new List<string>(); public List<int> Right = new List<int>(); }
    public class PbqLog { public string Title = ""; public List<string> Lines = new List<string>(); public List<bool> Marked = new List<bool>(); public bool IsLog = true; }
    public class PbqCalc { public string Id = "", Label = "", Expr = ""; public double Value; public string Error; }
    public class PbqPair { public string Left = "", Right = ""; }
    public class PbqZone { public string Id = "", Title = "", Link = ""; }
    public class PbqDevice { public string Name = ""; public List<string> Zones = new List<string>(); }
    public class PbqTest { public bool Allow; public string Src = "", Dst = "", Proto = "TCP", Why = ""; public int Port; }
    public class PbqRule { public string Action = "", Src = "", Dst = "", Port = "", Proto = ""; public bool Complete { get { return Action.Length > 0 && Src.Length > 0 && Dst.Length > 0 && Port.Length > 0 && Proto.Length > 0; } } }

    public class PbqSpec
    {
        public string Kind = "", Prompt = "", Explain = "";
        public List<PbqLog> Exhibits = new List<PbqLog>();        // logs (lines can be marked) and plain exhibits (a certificate...)
        public List<PbqQuestion> Questions = new List<PbqQuestion>();
        public List<PbqCalc> Calcs = new List<PbqCalc>();
        public List<PbqPair> Pairs = new List<PbqPair>();
        public List<string> Targets = new List<string>();          // match: every right-hand side (pairs' and distractors), in file order
        public string LeftTitle = "Item", RightTitle = "Match";
        public List<PbqZone> Zones = new List<PbqZone>();
        public List<PbqDevice> Devices = new List<PbqDevice>();
        public int Rows = 5;
        public List<KeyValuePair<string, string>> Addrs = new List<KeyValuePair<string, string>>();   // name -> CIDR
        public List<string> Ports = new List<string>();
        public List<PbqRule> Start = new List<PbqRule>(), Model = new List<PbqRule>();
        public List<PbqTest> Tests = new List<PbqTest>();
        public List<string> Problems = new List<string>();

        public static readonly string[] Kinds = { "match", "firewall", "logs", "place", "cert", "risk" };
        public static bool IsPbq(string kind) { return Array.IndexOf(Kinds, kind) >= 0; }
        public int IndicatorCount { get { return Exhibits.Sum(e => e.Marked.Count(x => x)); } }

        public static PbqRule ParseRule(string s)
        {
            var p = (s ?? "").Split('|').Select(x => x.Trim()).ToList();
            while (p.Count < 5) p.Add("");
            return new PbqRule { Action = p[0].ToUpperInvariant(), Src = p[1], Dst = p[2], Port = p[3], Proto = p[4].ToUpperInvariant() };
        }
        public static string RuleText(PbqRule r) { return r.Action + " | " + r.Src + " | " + r.Dst + " | " + r.Port + " | " + (r.Proto == "ANY" ? "Any" : r.Proto); }

        /// <summary>Parses the body lines of a PBQ item (everything after its @kind line, vars already filled in).</summary>
        public static PbqSpec Parse(string kind, List<string> raw)
        {
            var s = new PbqSpec { Kind = kind };
            var prompt = new StringBuilder(); var explain = new StringBuilder();
            bool prose = true;
            PbqQuestion q = null;
            for (int i = 0; i < raw.Count; i++)
            {
                string l = raw[i];
                string t = l.Trim();
                if (t.StartsWith("@log", StringComparison.Ordinal) || t.StartsWith("@exhibit", StringComparison.Ordinal))
                {
                    prose = false; q = null;
                    bool isLog = t.StartsWith("@log", StringComparison.Ordinal);
                    var ex = new PbqLog { IsLog = isLog, Title = t.Substring(isLog ? 4 : 8).Trim() };
                    string end = isLog ? "@endlog" : "@endexhibit";
                    for (i++; i < raw.Count && raw[i].Trim() != end; i++)
                    {
                        string ll = raw[i];
                        if (isLog && ll.StartsWith("* ", StringComparison.Ordinal)) { ex.Lines.Add(ll.Substring(2)); ex.Marked.Add(true); }
                        else { ex.Lines.Add(isLog && ll.StartsWith("  ", StringComparison.Ordinal) ? ll.Substring(2) : ll); ex.Marked.Add(false); }
                    }
                    while (ex.Lines.Count > 0 && ex.Lines[ex.Lines.Count - 1].Trim().Length == 0) { ex.Lines.RemoveAt(ex.Lines.Count - 1); ex.Marked.RemoveAt(ex.Marked.Count - 1); }
                    if (i >= raw.Count) s.Problems.Add("'" + end + "' missing");
                    s.Exhibits.Add(ex);
                    continue;
                }
                if (t.StartsWith("! ", StringComparison.Ordinal)) { explain.Append(t.Substring(2).Trim()).Append(' '); prose = false; continue; }
                if (t.StartsWith("?? ", StringComparison.Ordinal) || t.StartsWith("? ", StringComparison.Ordinal))
                {
                    prose = false;
                    bool multi = t.StartsWith("??", StringComparison.Ordinal);
                    var h = t.Substring(multi ? 3 : 2).Split(new[] { '|' }, 2);
                    q = new PbqQuestion { Multi = multi, Id = h.Length > 1 ? h[0].Trim() : "q" + (s.Questions.Count + 1), Text = (h.Length > 1 ? h[1] : h[0]).Trim() };
                    s.Questions.Add(q);
                    continue;
                }
                if (q != null && (t.StartsWith("+ ", StringComparison.Ordinal) || t.StartsWith("- ", StringComparison.Ordinal)))
                {
                    if (t[0] == '+') q.Right.Add(q.Choices.Count);
                    q.Choices.Add(t.Substring(2).Trim());
                    continue;
                }
                var m = Regex.Match(t, "^(calc|zone|device|addr|ports|rows|start|rule|test|columns)\\s+(.*)$");
                if (t.StartsWith("= ", StringComparison.Ordinal) || t.StartsWith("x ", StringComparison.Ordinal) || m.Success) { prose = false; q = null; }
                if (t.StartsWith("= ", StringComparison.Ordinal))
                {
                    int a = t.IndexOf(" -> ", StringComparison.Ordinal);
                    if (a < 0) { s.Problems.Add("match line without ' -> ': " + t); continue; }
                    var pr = new PbqPair { Left = t.Substring(2, a - 2).Trim(), Right = t.Substring(a + 4).Trim() };
                    s.Pairs.Add(pr);
                    if (!s.Targets.Contains(pr.Right)) s.Targets.Add(pr.Right);
                    continue;
                }
                if (t.StartsWith("x ", StringComparison.Ordinal)) { var x = t.Substring(2).Trim(); if (!s.Targets.Contains(x)) s.Targets.Add(x); continue; }
                if (m.Success) { s.Directive(m.Groups[1].Value, m.Groups[2].Value.Trim()); continue; }
                if (prose) prompt.Append(l).Append('\n');
                else if (t.Length > 0) s.Problems.Add("line not understood: " + t);
            }
            s.Prompt = prompt.ToString().Trim();
            s.Explain = explain.ToString().Trim();
            foreach (var c in s.Calcs)
            {
                string err; double v;
                if (PbqGrade.TryEval(c.Expr.TrimStart('=').Trim(), out v, out err)) c.Value = v; else { c.Error = err; s.Problems.Add("calc " + c.Id + ": " + err); }
            }
            s.Validate();
            return s;
        }

        /// <summary>What the person sees before answering (search): the prompt, exhibits, sub-questions and choices,
        /// items and cards, zones and devices, requirements, the calculations asked for.</summary>
        public string PublicText()
        {
            var l = new List<string> { Prompt };
            foreach (var e in Exhibits) { l.Add(e.Title); l.AddRange(e.Lines); }
            foreach (var p in Pairs) l.Add(p.Left);
            l.AddRange(Targets);
            foreach (var z in Zones) l.Add(z.Title + " " + z.Link);
            foreach (var d in Devices) l.Add(d.Name);
            foreach (var t in Tests) l.Add(t.Why);
            foreach (var c in Calcs) l.Add(c.Label);
            foreach (var q in Questions) { l.Add(q.Text); l.AddRange(q.Choices); }
            return string.Join("\n", l.Where(x => !string.IsNullOrEmpty(x)).ToArray());
        }
        /// <summary>The answers (search shows them only after the exam was taken).</summary>
        public string AnswerText()
        {
            var l = new List<string>();
            foreach (var p in Pairs) l.Add(p.Left + " -> " + p.Right);
            foreach (var r in Model) l.Add(RuleText(r));
            foreach (var d in Devices) l.Add(d.Name + " -> " + string.Join(", ", d.Zones.Select(z => Zones.First(x => x.Id == z).Title).ToArray()));
            foreach (var q in Questions) l.Add(q.Text + " " + string.Join("; ", q.Right.Select(i => q.Choices[i]).ToArray()));
            foreach (var c in Calcs) l.Add(c.Label + " = " + PbqGrade.Money(c.Value));
            l.Add(Explain);
            return string.Join("\n", l.Where(x => !string.IsNullOrEmpty(x)).ToArray());
        }

        void Directive(string d, string v)
        {
            var p = v.Split('|').Select(x => x.Trim()).ToArray();
            switch (d)
            {
                case "calc": Calcs.Add(new PbqCalc { Id = p[0], Label = p.Length > 1 ? p[1] : p[0], Expr = p.Length > 2 ? p[2] : "" }); break;
                case "zone": Zones.Add(new PbqZone { Id = p[0], Title = p.Length > 1 ? p[1] : p[0], Link = p.Length > 2 ? p[2] : "" }); break;
                case "device":
                    {
                        int a = v.IndexOf(" -> ", StringComparison.Ordinal);
                        if (a < 0) { Problems.Add("device without ' -> ': " + v); break; }
                        Devices.Add(new PbqDevice { Name = v.Substring(0, a).Trim(), Zones = v.Substring(a + 4).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() });
                        break;
                    }
                case "addr":
                    {
                        int eq = v.LastIndexOf('=');
                        if (eq < 0) { Problems.Add("addr without '=': " + v); break; }
                        Addrs.Add(new KeyValuePair<string, string>(v.Substring(0, eq).Trim(), v.Substring(eq + 1).Trim()));
                        break;
                    }
                case "ports": Ports.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)); break;
                case "rows": int n; if (int.TryParse(v, out n)) Rows = n; break;
                case "start": Start.Add(ParseRule(v)); break;
                case "rule": Model.Add(ParseRule(v)); break;
                case "columns": LeftTitle = p[0]; if (p.Length > 1) RightTitle = p[1]; break;
                case "test":
                    {
                        // test allow | 203.0.113.9 -> 10.0.2.10 | 443/TCP | why
                        var tm = Regex.Match(v, "^(allow|deny)\\s*\\|\\s*([\\d.]+)\\s*->\\s*([\\d.]+)\\s*\\|\\s*(\\d+)/(TCP|UDP|ICMP)\\s*(?:\\|(.*))?$", RegexOptions.IgnoreCase);
                        if (!tm.Success) { Problems.Add("test not understood: " + v); break; }
                        Tests.Add(new PbqTest { Allow = tm.Groups[1].Value.ToLowerInvariant() == "allow", Src = tm.Groups[2].Value, Dst = tm.Groups[3].Value, Port = int.Parse(tm.Groups[4].Value, CultureInfo.InvariantCulture), Proto = tm.Groups[5].Value.ToUpperInvariant(), Why = tm.Groups[6].Success ? tm.Groups[6].Value.Trim() : "" });
                        break;
                    }
            }
        }

        void Validate()
        {
            foreach (var q in Questions)
            {
                if (q.Choices.Count < 2) Problems.Add("question " + q.Id + " has fewer than 2 choices");
                if (!q.Multi && q.Right.Count != 1) Problems.Add("question " + q.Id + " needs exactly one '+'");
                if (q.Multi && q.Right.Count < 2) Problems.Add("question " + q.Id + " (??) needs two or more '+'");
            }
            if (Questions.Select(x => x.Id).Distinct().Count() != Questions.Count) Problems.Add("question ids repeat");
            foreach (var d in Devices) foreach (var z in d.Zones) if (!Zones.Any(x => x.Id == z)) Problems.Add("device " + d.Name + ": no zone " + z);
            foreach (var r in Start.Concat(Model))
            {
                if (r.Action != "ALLOW" && r.Action != "DENY") Problems.Add("rule action must be ALLOW or DENY: " + RuleText(r));
                foreach (var a in new[] { r.Src, r.Dst }) if (a != "Any" && !Addrs.Any(x => x.Key == a)) Problems.Add("rule names an unknown address: " + a);
                if (r.Port != "Any" && !Ports.Contains(r.Port)) Problems.Add("rule names a port not in 'ports': " + r.Port);
                if (r.Proto != "TCP" && r.Proto != "UDP" && r.Proto != "ICMP" && r.Proto != "ANY") Problems.Add("rule protocol must be TCP, UDP, ICMP or Any: " + r.Proto);
            }
            foreach (var a in Addrs) { uint b, mk; if (!PbqGrade.ParseCidr(a.Value, out b, out mk)) Problems.Add("address not understood: " + a.Value); }
            if (Model.Count > Rows) Problems.Add("the model answer has more rules than rows");
            int parts = PbqGrade.PartCount(this);
            if (parts == 0) Problems.Add("nothing to answer");
        }
    }
}
