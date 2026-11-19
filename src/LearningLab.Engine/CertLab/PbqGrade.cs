// Cert Lab - scoring performance-based questions: every part (a matched item, a test packet, a placed device, a
// sub-question, a calculation, the log indicators) is worth an equal share of the item's points, rounded down.
// Answers are "key=value" strings: m:<i>=<target> (match), r:<i>=ACTION|SRC|DST|PORT|PROTO (a firewall row),
// l:<exhibit>:<line> (a marked log line), q:<id>=<i>[,<i>] (a sub-question), c:<id>=<number> (a calculation),
// d:<i>=<zone id> (a placed device). The firewall is evaluated the way real ones are: rules top to bottom, the first
// rule that matches decides, and a packet no rule matches is denied (the implicit deny).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public class PbqPart { public string Label = "", Yours = "", Right = ""; public double Got; public bool Pass { get { return Got >= 0.999; } } }
    public class PbqResult { public List<PbqPart> Parts = new List<PbqPart>(); public double Fraction; public int Points; }

    public static class PbqGrade
    {
        public static string Get(List<string> ans, string key)
        {
            if (ans == null) return null;
            string p = key + "=";
            for (int i = ans.Count - 1; i >= 0; i--) if (ans[i] != null && ans[i].StartsWith(p, StringComparison.Ordinal)) return ans[i].Substring(p.Length);
            return null;
        }
        public static void Set(List<string> ans, string key, string value)
        {
            ans.RemoveAll(x => x != null && x.StartsWith(key + "=", StringComparison.Ordinal));
            if (value != null) ans.Add(key + "=" + value);
        }
        public static bool Marked(List<string> ans, int ex, int line) { return ans != null && ans.Contains("l:" + ex + ":" + line); }

        public static int PartCount(PbqSpec s)
        {
            return s.Pairs.Count + s.Tests.Count + s.Devices.Count + s.Questions.Count + s.Calcs.Count + (s.IndicatorCount > 0 ? 1 : 0);
        }

        /// <summary>The model answer (tests, the review's "right answer", snapshots).</summary>
        public static List<string> ModelAnswer(PbqSpec s)
        {
            var a = new List<string>();
            for (int i = 0; i < s.Pairs.Count; i++) a.Add("m:" + i + "=" + s.Pairs[i].Right);
            if (s.Tests.Count > 0) for (int i = 0; i < Math.Max(s.Rows, s.Start.Count); i++) a.Add("r:" + i + "=" + (i < s.Model.Count ? Row(s.Model[i]) : ""));
            for (int e = 0; e < s.Exhibits.Count; e++) for (int i = 0; i < s.Exhibits[e].Marked.Count; i++) if (s.Exhibits[e].Marked[i]) a.Add("l:" + e + ":" + i);
            foreach (var q in s.Questions) a.Add("q:" + q.Id + "=" + string.Join(",", q.Right.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            foreach (var c in s.Calcs) a.Add("c:" + c.Id + "=" + c.Value.ToString("0.##", CultureInfo.InvariantCulture));
            for (int i = 0; i < s.Devices.Count; i++) a.Add("d:" + i + "=" + s.Devices[i].Zones[0]);
            return a;
        }
        public static string Row(PbqRule r) { return r.Action + "|" + r.Src + "|" + r.Dst + "|" + r.Port + "|" + r.Proto; }

        /// <summary>The rules a person entered (rows they left incomplete are skipped, as a firewall would refuse them).</summary>
        public static List<PbqRule> Rules(PbqSpec s, List<string> ans)
        {
            var l = new List<PbqRule>();
            for (int i = 0; i < Math.Max(s.Rows, s.Start.Count); i++)
            {
                string v = Get(ans, "r:" + i);
                PbqRule r = v != null ? PbqSpec.ParseRule(v) : (i < s.Start.Count ? s.Start[i] : null);
                if (r != null && r.Complete) l.Add(r);
            }
            return l;
        }

        /// <summary>What the rule table does with a packet: the number of the rule that decided (1-based) or 0 for the implicit deny.</summary>
        public static bool Evaluate(PbqSpec s, List<PbqRule> rules, PbqTest t, out int ruleNo)
        {
            ruleNo = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (!AddrMatch(s, r.Src, t.Src) || !AddrMatch(s, r.Dst, t.Dst)) continue;
                string pr = r.Proto.ToUpperInvariant();
                if (pr != "ANY" && pr != t.Proto) continue;
                if (r.Port != "Any" && t.Proto != "ICMP" && r.Port != t.Port.ToString(CultureInfo.InvariantCulture)) continue;
                ruleNo = i + 1;
                return r.Action.ToUpperInvariant() == "ALLOW";
            }
            return false;
        }
        static bool AddrMatch(PbqSpec s, string name, string ip)
        {
            if (name == "Any") return true;
            var a = s.Addrs.FirstOrDefault(x => x.Key == name);
            uint b, m, v, vm;
            if (a.Key == null || !ParseCidr(a.Value, out b, out m) || !ParseCidr(ip, out v, out vm)) return false;
            return (v & m) == (b & m);
        }
        public static bool ParseCidr(string s, out uint addr, out uint mask)
        {
            addr = 0; mask = 0xFFFFFFFF;
            var p = (s ?? "").Split('/');
            var o = p[0].Split('.');
            if (o.Length != 4) return false;
            foreach (var x in o) { int n; if (!int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n > 255) return false; addr = (addr << 8) | (uint)n; }
            if (p.Length > 1) { int bits; if (!int.TryParse(p[1], out bits) || bits < 0 || bits > 32) return false; mask = bits == 0 ? 0 : 0xFFFFFFFF << (32 - bits); }
            return true;
        }

        /// <summary>Scores an answer: the parts with what was given and what was right, and the points (rounded down).</summary>
        public static PbqResult Grade(PbqSpec s, int points, List<string> ans)
        {
            var r = new PbqResult();
            ans = ans ?? new List<string>();
            for (int i = 0; i < s.Pairs.Count; i++)
            {
                string v = Get(ans, "m:" + i) ?? "";
                r.Parts.Add(new PbqPart { Label = s.Pairs[i].Left, Yours = v.Length == 0 ? "(none)" : v, Right = s.Pairs[i].Right, Got = v == s.Pairs[i].Right ? 1 : 0 });
            }
            if (s.Tests.Count > 0)
            {
                var rules = Rules(s, ans);
                var res = s.Tests.Select(t => { int no; bool al = Evaluate(s, rules, t, out no); return new { t, al, no }; }).ToList();
                // a packet that should be blocked earns its share only once the table lets a wanted packet through:
                // an empty table blocks everything and so earns nothing
                bool anyAllowWanted = s.Tests.Any(t => t.Allow), someAllowed = res.Any(x => x.t.Allow && x.al);
                foreach (var x in res)
                {
                    bool right = x.al == x.t.Allow && (x.t.Allow || !anyAllowWanted || someAllowed);
                    string what = x.t.Src + " -> " + x.t.Dst + " " + (x.t.Proto == "ICMP" ? "ICMP" : x.t.Port + "/" + x.t.Proto) + (x.t.Why.Length > 0 ? " (" + x.t.Why + ")" : "");
                    r.Parts.Add(new PbqPart { Label = what, Yours = (x.al ? "allowed" : "blocked") + (x.no > 0 ? " by rule " + x.no : " (no rule matched: implicit deny)"), Right = x.t.Allow ? "allowed" : "blocked", Got = right ? 1 : 0 });
                }
            }
            for (int i = 0; i < s.Devices.Count; i++)
            {
                string v = Get(ans, "d:" + i) ?? "";
                var z = s.Zones.FirstOrDefault(x => x.Id == v);
                r.Parts.Add(new PbqPart { Label = s.Devices[i].Name, Yours = z == null ? "(not placed)" : z.Title, Right = string.Join(" or ", s.Devices[i].Zones.Select(id => s.Zones.First(x => x.Id == id).Title).ToArray()), Got = s.Devices[i].Zones.Contains(v) ? 1 : 0 });
            }
            int ind = s.IndicatorCount;
            if (ind > 0)
            {
                int hits = 0, wrong = 0;
                for (int e = 0; e < s.Exhibits.Count; e++) for (int i = 0; i < s.Exhibits[e].Lines.Count; i++) if (Marked(ans, e, i)) { if (s.Exhibits[e].Marked[i]) hits++; else wrong++; }
                double got = Math.Max(0, hits - wrong) / (double)ind;
                r.Parts.Add(new PbqPart { Label = "Indicators in the logs", Yours = hits + " of " + ind + " found" + (wrong > 0 ? ", " + wrong + " wrongly marked (each takes one away)" : ""), Right = ind + " lines", Got = got });
            }
            foreach (var q in s.Questions)
            {
                string v = Get(ans, "q:" + q.Id) ?? "";
                var picked = v.Split(',').Select(x => { int n; return int.TryParse(x, out n) ? n : -1; }).Where(x => x >= 0 && x < q.Choices.Count).Distinct().OrderBy(x => x).ToList();
                bool ok = picked.SequenceEqual(q.Right.OrderBy(x => x));
                r.Parts.Add(new PbqPart { Label = q.Text, Yours = picked.Count == 0 ? "(none)" : string.Join("; ", picked.Select(x => q.Choices[x]).ToArray()), Right = string.Join("; ", q.Right.Select(x => q.Choices[x]).ToArray()), Got = ok ? 1 : 0 });
            }
            foreach (var c in s.Calcs)
            {
                string v = Get(ans, "c:" + c.Id) ?? "";
                double x; bool num = TryNumber(v, out x);
                bool ok = num && Math.Abs(x - c.Value) <= Math.Max(0.51, Math.Abs(c.Value) * 0.002);
                r.Parts.Add(new PbqPart { Label = c.Label, Yours = v.Trim().Length == 0 ? "(none)" : v.Trim(), Right = Money(c.Value), Got = ok ? 1 : 0 });
            }
            int n0 = r.Parts.Count;
            r.Fraction = n0 == 0 ? 0 : r.Parts.Sum(p => p.Got) / n0;
            r.Points = (int)Math.Floor(points * r.Fraction + 1e-9);
            return r;
        }
        public static string Money(double v) { return v == Math.Floor(v) ? v.ToString("#,0", CultureInfo.InvariantCulture) : v.ToString("#,0.##", CultureInfo.InvariantCulture); }

        /// <summary>A number as a person types it: "$12,500", "12500.00", "0.25".</summary>
        public static bool TryNumber(string s, out double v)
        {
            s = (s ?? "").Trim().Replace("$", "").Replace(",", "").Replace(" ", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Arithmetic for calc lines: numbers, + - * /, parentheses and unary minus.</summary>
        public static bool TryEval(string expr, out double v, out string err)
        {
            v = 0; err = null;
            int pos = 0;
            try
            {
                v = Sum(expr, ref pos);
                Skip(expr, ref pos);
                if (pos != expr.Length) { err = "unexpected '" + expr.Substring(pos) + "'"; return false; }
                if (double.IsNaN(v) || double.IsInfinity(v)) { err = "not a number"; return false; }
                return true;
            }
            catch (FormatException e) { err = e.Message; return false; }
        }
        static void Skip(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }
        static double Sum(string s, ref int p)
        {
            double v = Product(s, ref p);
            for (Skip(s, ref p); p < s.Length && (s[p] == '+' || s[p] == '-'); Skip(s, ref p)) { char o = s[p++]; double r = Product(s, ref p); v = o == '+' ? v + r : v - r; }
            return v;
        }
        static double Product(string s, ref int p)
        {
            double v = Unary(s, ref p);
            for (Skip(s, ref p); p < s.Length && (s[p] == '*' || s[p] == '/' || s[p] == 'x'); Skip(s, ref p)) { char o = s[p++]; double r = Unary(s, ref p); v = o == '/' ? v / r : v * r; }
            return v;
        }
        static double Unary(string s, ref int p)
        {
            Skip(s, ref p);
            if (p < s.Length && s[p] == '-') { p++; return -Unary(s, ref p); }
            if (p < s.Length && s[p] == '(') { p++; double v = Sum(s, ref p); Skip(s, ref p); if (p >= s.Length || s[p] != ')') throw new FormatException("')' missing"); p++; return v; }
            int st = p;
            while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == ',')) p++;
            if (st == p) throw new FormatException("a number is missing at position " + (st + 1));
            return double.Parse(s.Substring(st, p - st).Replace(",", ""), CultureInfo.InvariantCulture);
        }
    }
}
