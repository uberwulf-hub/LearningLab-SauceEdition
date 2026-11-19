// Learning Games - checks for the math engine (App Hub's LearningGames.ps1 -Test, here the groups games-math and
// games-net; ported unchanged except that the pictures are Avalonia controls now): the long-division steps against plain
// arithmetic for many thousands of divisions, every skill's generated problems at every level (the answer is accepted,
// exactly one choice is right, 2nd grade never goes negative, the picture draws) and, where a problem can be read
// back, its answer worked out again a different way from the problem's own numbers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LearningGames
{
    public static partial class MathTests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static int checks, fails;
        static StringBuilder log;
        static readonly Dictionary<string, int> failKinds = new Dictionary<string, int>();

        static void Ok(bool cond, string what)
        {
            checks++;
            if (cond) return;
            fails++;
            string key = what.Length > 60 ? what.Substring(0, 60) : what;
            int n; failKinds.TryGetValue(key, out n); failKinds[key] = n + 1;
            if (n < 3) log.AppendLine("  FAIL  " + what);
        }

        /// <summary>Runs everything; returns the report. Failures is set to the number of failed checks.</summary>
        public static int Failures;
        public static int Checks;
        public static bool Verbose;
        public static string Run(int seed) { return Run(seed, true, true); }
        /// <summary>Learning Lab runs the math and the network parts as separate groups (games-math, games-net).</summary>
        public static string Run(int seed, bool math, bool net)
        {
            checks = 0; fails = 0; log = new StringBuilder(); failKinds.Clear();
            Problems.Seed(seed);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (math)
            {
                Section("answers", Answers_);
                Section("long division (every dividend 0-2000 by 1-45)", LongDivisionExhaustive);
                Section("long division (40,000 random, with decimals)", LongDivisionRandom);
                Section("long division (the steps a child sees)", LongDivisionWalk);
                Section("long division workshop problems", WorkshopProblems);
                Section("every skill, every level", AllSkills);
                Section("problems worked out again", Recheck);
                Section("rewards", Rewards_);
            }
            if (net) Section("network: the end of a game (127.0.0.1)", NetRounds_);
            log.AppendLine(string.Format(Inv, "{0} checks, {1} failed ({2:0.0} s)", checks, fails, sw.Elapsed.TotalSeconds));
            Failures = fails; Checks = checks;
            return log.ToString();
        }
        static void Section(string name, Action a)
        {
            int c0 = checks, f0 = fails;
            if (Verbose) Console.Error.WriteLine("section " + name);
            try { a(); } catch (Exception ex) { fails++; log.AppendLine("  FAIL  " + name + " threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            log.AppendLine((fails == f0 ? "  ok    " : "  FAIL  ") + name + "  (" + (checks - c0) + " checks" + (fails > f0 ? ", " + (fails - f0) + " failed" : "") + ")");
        }

        // ---------------------------------------------------------------- answers
        static void Answers_()
        {
            long q, r;
            Ok(Answers.TryQr("134 R 11", out q, out r) && q == 134 && r == 11, "qr 134 R 11");
            Ok(Answers.TryQr("134r11", out q, out r) && q == 134 && r == 11, "qr 134r11");
            Ok(Answers.TryQr("12", out q, out r) && q == 12 && r == 0, "qr 12");
            Ok(!Answers.TryQr("12 R", out q, out r), "qr '12 R' is not an answer");
            Ok(Answers.Same("134 remainder 11", "134 R 11", "qr"), "qr words");
            Ok(!Answers.Same("135 R 11", "134 R 11", "qr"), "qr wrong quotient");
            Ok(Answers.Same("3/4", "3/4", "simplest") && !Answers.Same("6/8", "3/4", "simplest") && Answers.Same("0.75", "3/4", "simplest"), "simplest");
            Ok(Answers.Same("1 3/4", "1 3/4", "mixed") && !Answers.Same("7/4", "1 3/4", "mixed") && Answers.Same("1 6/8", "1 3/4", "mixed"), "mixed");
            Ok(Answers.Same("50", "50%", "number") && Answers.Same("50%", "50%", "number") && !Answers.Same("0.5", "50%", "number"), "percent");
            Ok(Answers.Same("$1.35", "$1.35", "money") && Answers.Same("135", "$1.35", "money") && Answers.Same("35", "35\u00A2", "money"), "money");
            Ok(Problems.Words(406) == "four hundred six" && Problems.Words(72) == "seventy-two" && Problems.Words(910) == "nine hundred ten" && Problems.Words(115) == "one hundred fifteen", "number words");
            double mn, q1, md, q3, mx;
            Problems.FiveNumbers(new[] { 1, 3, 5, 7, 9, 11, 13 }, out mn, out q1, out md, out q3, out mx);
            Ok(mn == 1 && q1 == 3 && md == 7 && q3 == 11 && mx == 13, "five numbers, odd count");
            Problems.FiveNumbers(new[] { 2, 4, 6, 8, 10, 12 }, out mn, out q1, out md, out q3, out mx);
            Ok(q1 == 4 && md == 7 && q3 == 10, "five numbers, even count");
        }

        // ---------------------------------------------------------------- long division
        /// <summary>The steps of one division agree with plain arithmetic.</summary>
        static void CheckDivision(long n, int d, int places)
        {
            LongDivision L = LongDivision.For(n, d, places, true, true, true, true);
            string id = n + "/" + d + (places > 0 ? " (" + places + " places)" : "");
            long scale = 1; int produced = 0;
            foreach (LdStep s in L.Steps)
            {
                switch (s.Kind)
                {
                    case "Divide":
                        Ok(s.Digit >= 0 && s.Digit <= 9 && s.Partial / d == s.Digit, id + ": divide digit " + s.Digit + " for " + s.Partial);
                        if (s.Col >= L.DigitsBeforePoint) produced++;
                        break;
                    case "Multiply": Ok(long.Parse(s.Expected, Inv) == (long)s.Digit * d, id + ": product"); break;
                    case "Subtract": { long v = long.Parse(s.Expected, Inv); Ok(v >= 0 && v < d && v == s.Partial - (long)s.Digit * d, id + ": difference"); break; }
                    case "BringDown": Ok(s.Expected == L.Digits[s.Col].ToString(), id + ": brought-down digit"); break;
                }
            }
            for (int i = 0; i < produced; i++) scale *= 10;
            if (places == 0)
            {
                Ok(L.QuotientWhole == n / d && L.Remainder == n % d, id + ": quotient " + L.QuotientWhole + " R " + L.Remainder);
                Ok(L.QuotientText == (n / d).ToString(Inv), id + ": quotient text " + L.QuotientText);
            }
            else
            {
                // the quotient's digits (without the point), times the divisor, plus the last remainder, is n with as many zeros as decimals produced
                decimal qv = decimal.Parse(L.QuotientText, Inv);
                Ok(qv * d * scale + L.Remainder == (decimal)n * scale || (scale == 1 && qv * d + L.Remainder == n), id + ": decimal quotient " + L.QuotientText + " R " + L.Remainder);
                if (L.Remainder == 0) Ok(qv * d == n, id + ": exact decimal");
                Ok(produced <= places, id + ": no more decimals than asked");
            }
            // the estimate is a real division and not far off
            long ed, ev, eq; LongDivision.Estimate(n, d, out ed, out ev, out eq);
            Ok(eq * ev == ed && eq >= 1, id + ": estimate is a fact " + ed + "/" + ev + "=" + eq);
            if (n / d >= 2) Ok(eq <= 3 * (n / (double)d) + 2 && eq >= (n / (double)d) / 3 - 2, id + ": estimate " + eq + " near " + (n / d));
            foreach (LdStep s in L.Steps)
            {
                if (s.Kind == "Mixed") Ok(Answers.Same(s.Expected, Answers.Frac((int)n, d), "number") || n > int.MaxValue, id + ": mixed number");
                if (s.Kind == "Decimal") { double want = Math.Floor(10.0 * n / d + 0.5) / 10; Ok(Math.Abs(double.Parse(s.Expected, Inv) - want) < 1e-9, id + ": rounded " + s.Expected + " vs " + want); }
                if (s.Kind == "CheckAdd") Ok(s.Expected == n.ToString(Inv), id + ": check adds back to the dividend");
                if (s.Kind == "CheckMultiply" && L.Remainder == 0) Ok(s.Expected == n.ToString(Inv), id + ": check multiplies back to the dividend");
            }
            // answering every step with its expected answer finishes it with no mistakes
            foreach (LdStep s in L.Steps.ToList()) { LdCheck c = L.Check(s.Expected, true); if (!c.Ok) { Ok(false, id + ": its own answer was refused at " + s.Kind + " (" + s.Expected + ")"); return; } }
            Ok(L.Done && L.Mistakes == 0, id + ": done");
        }
        static void LongDivisionExhaustive()
        {
            for (int d = 1; d <= 45; d++) for (long n = 0; n <= 2000; n++) CheckDivision(n, d, 0);
        }
        static void LongDivisionRandom()
        {
            Random r = new Random(7);
            for (int i = 0; i < 40000; i++) CheckDivision(r.Next(0, 100000), r.Next(1, 100), r.Next(0, 3) == 0 ? 2 : 0);
            // the classic: 4567 / 34 = 134 R 11
            LongDivision L = LongDivision.For(4567, 34, 0, true, true, true, true);
            Ok(L.QuotientWhole == 134 && L.Remainder == 11, "4567 / 34 = 134 R 11");
            Ok(string.Join(",", L.Steps.Where(s => s.Kind == "Divide").Select(s => s.Expected).ToArray()) == "1,3,4", "4567 / 34 quotient digits 1,3,4");
            Ok(L.Steps.First().Kind == "Estimate" && L.Steps.First().Expected == "150", "4567 / 34 estimate 4500 / 30 = 150 (got " + L.Steps.First().Expected + ")");
            Ok(L.Steps.Any(s => s.Kind == "Mixed" && s.Expected == "134 11/34"), "4567 / 34 = 134 11/34");
            Ok(L.Steps.Any(s => s.Kind == "Decimal" && s.Expected == "134.3"), "4567 / 34 is about 134.3");
            LongDivision L2 = LongDivision.For(51, 4, 2, false, true, false, false);
            Ok(L2.QuotientText == "12.75" && L2.Remainder == 0, "51 / 4 = 12.75 (got " + L2.QuotientText + ")");
            LongDivision L3 = LongDivision.For(3, 4, 2, false, false, false, false);
            Ok(L3.QuotientText == "0.75", "3 / 4 = 0.75 (got " + L3.QuotientText + ")");
            LongDivision L4 = LongDivision.For(612, 6, 0, false, false, false, false);
            Ok(L4.QuotientText == "102" && L4.Steps.Count(s => s.Kind == "Divide") == 3, "612 / 6 = 102 with a 0 written in the middle");
        }
        static void LongDivisionWalk()
        {
            // wrong answers: kind words that mention the right numbers, and never move on by themselves
            LongDivision L = LongDivision.For(4567, 34, 0, false, false, false, false);
            LdCheck c = L.Check("2", true);
            Ok(!c.Ok && c.Message.Contains("68") && c.Message.Contains("too big"), "too big a digit is explained: " + c.Message);
            Ok(L.Current == 0 && L.Mistakes == 1, "a wrong answer stays on the step");
            c = L.Check("0", true);
            Ok(!c.Ok && c.Message.Contains("another 34 fits"), "too small a digit is explained: " + c.Message);
            Ok(L.Check("1", true).Ok, "then the right digit");
            c = L.Check("35", true);
            Ok(!c.Ok && c.Message.Contains("1 \u00D7 34"), "a multiplying slip names the multiplication: " + c.Message);
            Ok(L.Check("34", true).Ok && !L.Check("12", true).Ok, "a subtracting slip");
            c = L.Check("12", false);
            Ok(!c.Ok && !c.Message.Contains("45"), "practice mode doesn't give the numbers away: " + c.Message);
            Ok(L.Check("11", true).Ok, "45 - 34 = 11");
            c = L.Check("7", true);
            Ok(!c.Ok && c.Message.Contains("next digit"), "bring down the wrong digit: " + c.Message);
            Ok(L.Check("6", true).Ok, "bring down 6");
            // the story's answer
            Ok(LongDivision.StoryAnswer(4567, 34, "up") == "135" && LongDivision.StoryAnswer(4567, 34, "drop") == "134" && LongDivision.StoryAnswer(4567, 34, "use") == "11", "remainder stories: round up, drop, use");
            // mixed number answers
            LongDivision M = LongDivision.For(4567, 34, 0, false, false, true, false);
            while (!M.Done && M.Step.Kind != "Mixed") M.Check(M.Step.Expected, true);
            Ok(!M.Done && !M.Matches(M.Step, "4567/34") && M.Matches(M.Step, "134 11/34") && M.Matches(M.Step, "134 22/68") && !M.Matches(M.Step, "134 11/35"), "mixed number step");
        }
        static void WorkshopProblems()
        {
            for (int lv = 1; lv <= 5; lv++)
                for (int i = 0; i < 400; i++)
                {
                    bool story = i % 3 == 0;
                    LongDivision L = Problems.MakeLongDivision(lv, story);
                    string id = "workshop level " + lv + " " + L.Dividend + "/" + L.Divisor;
                    Ok(L.Dividend >= 2 * L.Divisor, id + ": dividend at least twice the divisor");
                    if (lv == 1 && !story) Ok(L.Remainder == 0 && L.Divisor < 10, id + ": level 1 is exact by one digit");
                    if (lv >= 3) Ok(L.Divisor >= 10, id + ": two-digit divisor");
                    if (lv <= 2) Ok(L.Divisor < 10, id + ": one-digit divisor");
                    if (L.Places > 0) Ok(L.Remainder == 0 && L.QuotientText.Contains("."), id + ": decimal answers end");
                    if (story) Ok(L.Remainder > 0 && L.Steps.Last().Kind == "Story" && L.Story.Length > 20, id + ": story with a remainder");
                    foreach (LdStep s in L.Steps.ToList()) if (!L.Check(s.Expected, false).Ok) { Ok(false, id + ": own answer refused at " + s.Kind); break; }
                    Ok(L.Done, id + ": finishes");
                }
        }

        // ---------------------------------------------------------------- every skill
        static readonly Regex Neg = new Regex(@"(^|[\s(=])[-\u2212]\d");
        static void AllSkills()
        {
            foreach (SkillInfo si in Problems.Skills)
            {
                Ok(si.Std.Length > 0, si.Id + ": has a standard");
                Ok(Array.IndexOf(new[] { "Facts", "Numbers & place value", "Add & subtract", "Word problems", "Money & time", "Measuring & data", "Shapes & arrays", "Long division", "Fractions", "Ratios & rates", "Percents", "Decimals", "Number system", "Expressions & equations", "Geometry", "Statistics", "Science lab" }, si.Area) >= 0, si.Id + ": listed under a known area (" + si.Area + ")");
                for (int lv = 1; lv <= 5; lv++)
                {
                    if (Verbose) Console.Error.WriteLine("  skill " + si.Id + " L" + lv);
                    var seen = new HashSet<string>();
                    for (int i = 0; i < 250; i++)
                    {
                        Problem p = Problems.Make(si.Id, lv);
                        string id = si.Id + " L" + lv + " \"" + p.Text + "\" = " + p.Answer;
                        seen.Add(p.Text + "|" + p.Visual + "|" + p.Answer);
                        Ok(p.Text.Length > 0 && p.Answer.Length > 0, id + ": text and answer");
                        Ok(p.Hints.Length >= 1 && p.Hints.All(h => !string.IsNullOrEmpty(h)), id + ": hints");
                        Ok(p.IsCorrect(p.Answer), id + ": its answer is accepted");
                        Ok(p.Choices.Length >= 2 && p.Choices.Length <= 4, id + ": 2-4 choices (" + p.Choices.Length + ")");
                        Ok(p.Choices.Distinct().Count() == p.Choices.Length, id + ": choices differ: " + string.Join(" | ", p.Choices));
                        int right = p.Choices.Count(ch => p.IsCorrect(ch));
                        Ok(right == 1, id + ": exactly one right choice (" + right + "): " + string.Join(" | ", p.Choices));
                        Ok(!p.Text.Contains("NaN") && !p.Text.Contains("Infinity") && !p.Answer.Contains("NaN"), id + ": no NaN");
                        if (si.Grade == 2)
                        {
                            double v;
                            if (Answers.TryValue(p.Answer, out v)) Ok(v >= 0, id + ": 2nd grade answers are never negative");
                            Ok(!Neg.IsMatch(p.Text), id + ": no negative numbers in 2nd grade");
                            foreach (string ch in p.Choices) { double cv; if (Answers.TryValue(ch, out cv)) Ok(cv >= 0, id + ": no negative choices in 2nd grade (" + ch + ")"); }
                        }
                        if (p.Visual.Length > 0 && !IsOldVisual(p.Visual))
                        {
                            Avalonia.Controls.Control pic = Visuals.Draw(p.Visual);
                            Ok(pic != null, id + ": draws its picture " + p.Visual);
                            if (pic != null) Ok(pic.Width > 20 && pic.Height > 20 && pic.Width < 800 && pic.Height < 420, id + ": picture fits (" + pic.Width + " x " + pic.Height + ")");
                        }
                    }
                    Ok(seen.Count >= (si.Id == "estimate2" || si.Id == "shapes2" || si.Id == "parts2" || si.Id == "fracwhy" || si.Id == "evenodd" || si.Id == "arrays" ? 3 : 8), si.Id + " L" + lv + ": varied problems (" + seen.Count + " different)");
                }
            }
        }
        static bool IsOldVisual(string v) { string k = v.Split(':')[0]; return k == "array" || k == "coins" || k == "clock" || k == "place" || k == "rect" || k == "tri" || k == "para" || k == "line" || k == "box" || k == "ruler"; }

        // ---------------------------------------------------------------- answers worked out again from the problem itself
        static long L(string s) { return long.Parse(s.Replace(",", ""), Inv); }
        static double Dv(string s) { return double.Parse(s, Inv); }
        static double FracVal(string s) { string[] f = s.Split('/'); return f.Length == 1 ? Dv(f[0]) : Dv(f[0]) / Dv(f[1]); }
        static void Recheck()
        {
            for (int lv = 1; lv <= 5; lv++)
                for (int i = 0; i < 300; i++)
                {
                    Problem p; Match m;
                    // long division: the typed answer is the quotient and remainder of the numbers shown
                    p = Problems.Make("longdiv", lv);
                    m = Regex.Match(p.Text, @"^([\d,]+) \u00F7 (\d+)");
                    if (Ok2(m.Success, "longdiv text " + p.Text)) { long n = L(m.Groups[1].Value), d = L(m.Groups[2].Value); long q, r; Answers.TryQr(p.Answer, out q, out r); Ok(q * d + r == n && r < d && r >= 0, "longdiv " + p.Text + " = " + p.Answer); if (lv == 1) Ok(r == 0, "longdiv level 1 is exact: " + p.Text); }
                    // regrouping
                    p = Problems.Make("regroup", lv);
                    m = Regex.Match(p.Text, @"^(\d+) (\+|\u2212) (\d+)");
                    if (Ok2(m.Success, "regroup text")) { long a = L(m.Groups[1].Value), b = L(m.Groups[3].Value); long want = m.Groups[2].Value == "+" ? a + b : a - b; Ok(L(p.Answer) == want && want >= 0 && want <= 999, "regroup " + p.Text + " = " + p.Answer); Ok(Regroups(a, b, m.Groups[2].Value == "+"), "regroup needs a trade: " + p.Text); }
                    // fractions: add / subtract / multiply / divide checked with doubles
                    foreach (string sk in new[] { "fracadd", "fracmul", "fracdiv" })
                    {
                        p = Problems.Make(sk, lv);
                        m = Regex.Match(p.Text, @"^(?:(\d+) )?(\d+(?:/\d+)?) (\+|\u2212|\u00D7|\u00F7) (\d+(?:/\d+)?) =");
                        if (!Ok2(m.Success, sk + " text " + p.Text)) continue;
                        double x = FracVal(m.Groups[2].Value) + (m.Groups[1].Success ? Dv(m.Groups[1].Value) : 0), y = FracVal(m.Groups[4].Value);
                        string op = m.Groups[3].Value;
                        double want = op == "+" ? x + y : op == "\u2212" ? x - y : op == "\u00D7" ? x * y : x / y;
                        double got; Answers.TryValue(p.Answer, out got);
                        Ok(Math.Abs(got - want) < 1e-9 && want > 0, sk + " " + p.Text + " " + p.Answer + " (want " + want + ")");
                    }
                    // simplifying: lowest terms, same value
                    p = Problems.Make("fracequiv", lv);
                    m = Regex.Match(p.Text, @"^Simplify (\d+)/(\d+)");
                    if (m.Success) { double v = Dv(m.Groups[1].Value) / Dv(m.Groups[2].Value); double got; Answers.TryValue(p.Answer, out got); string[] f = p.Answer.Split('/'); Ok(Math.Abs(got - v) < 1e-12 && (f.Length == 1 || Answers.Gcd(int.Parse(f[0]), int.Parse(f[1])) == 1), "simplify " + p.Text + " = " + p.Answer); }
                    // percent of a number is the part
                    p = Problems.Make("pctwhole", Math.Min(lv, 3));
                    m = Regex.Match(p.Text, @"^(\d+)% of a number is (\d+)\.");
                    if (Ok2(m.Success, "pctwhole text " + p.Text)) Ok(L(p.Answer) * L(m.Groups[1].Value) == 100 * L(m.Groups[2].Value), "pctwhole " + p.Text + " = " + p.Answer);
                    // money answers are whole cents
                    p = Problems.Make("pctmoney", lv);
                    Ok(Regex.IsMatch(p.Answer, @"^\$\d+\.\d\d$"), "pctmoney answer in dollars and cents: " + p.Answer);
                    if (lv <= 2) { m = Regex.Match(p.Text, @"costs \$(\d+\.\d\d)\. It is (\d+)% off\. (How much do you save|What is the sale price)"); if (Ok2(m.Success, "pctmoney text " + p.Text)) { decimal price = decimal.Parse(m.Groups[1].Value, Inv), pc = decimal.Parse(m.Groups[2].Value, Inv); decimal save = price * pc / 100; decimal want = m.Groups[3].Value.StartsWith("How") ? save : price - save; Ok(decimal.Parse(p.Answer.Substring(1), Inv) == want, "pctmoney " + p.Text + " = " + p.Answer); } }
                    // fractions to percents and decimals
                    p = Problems.Make("fdp", lv);
                    m = Regex.Match(p.Text, @"^(\d+)/(\d+) = \?%");
                    if (m.Success) Ok(Math.Abs(Dv(p.Answer.TrimEnd('%')) - 100 * Dv(m.Groups[1].Value) / Dv(m.Groups[2].Value)) < 1e-9, "fdp " + p.Text + " = " + p.Answer);
                    // distributive property
                    p = Problems.Make("distrib", lv);
                    m = Regex.Match(p.Text, @"^(\d+)\((\d+) \+ (\d+)\) = \?$");
                    if (m.Success) Ok(L(p.Answer) == L(m.Groups[1].Value) * L(m.Groups[2].Value) + L(m.Groups[1].Value) * L(m.Groups[3].Value), "distrib " + p.Text);
                    m = Regex.Match(p.Text, @"^(\d+) \+ (\d+) = \?\((\d+) \+ (\d+)\)$");
                    if (m.Success) Ok(L(p.Answer) * L(m.Groups[3].Value) == L(m.Groups[1].Value) && L(p.Answer) * L(m.Groups[4].Value) == L(m.Groups[2].Value), "factor " + p.Text + " = " + p.Answer);
                    // distance on the coordinate plane
                    p = Problems.Make("coords", lv);
                    m = Regex.Match(p.Text, @"^How far apart are \((-?\d+), (-?\d+)\) and \((-?\d+), (-?\d+)\)");
                    if (m.Success) { long x1 = L(m.Groups[1].Value), y1 = L(m.Groups[2].Value), x2 = L(m.Groups[3].Value), y2 = L(m.Groups[4].Value); Ok(L(p.Answer) == Math.Abs(x1 - x2) + Math.Abs(y1 - y2) && (x1 == x2 || y1 == y2), "distance " + p.Text + " = " + p.Answer); }
                    // areas from the picture's measurements, cut up a different way
                    p = Problems.Make("polyarea", lv);
                    int[] a2 = p.Visual.Substring(p.Visual.IndexOf(':') + 1).Split(',').Select(s => int.Parse(s, Inv)).ToArray();
                    string pk = p.Visual.Split(':')[0];
                    long area = pk == "lshape" ? (long)(a2[0] + a2[2]) * a2[1] - (long)a2[2] * (a2[1] - a2[3])      // the bounding box minus the missing corner
                              : pk == "house" ? (long)a2[0] * (2 * a2[1] + a2[2]) / 2                                  // a trapezoid-ish rewrite: w(2h + rh)/2
                              : pk == "trap" ? (long)a2[0] * a2[2] + (long)(a2[1] - a2[0]) * a2[2] / 2                 // the middle rectangle plus the two side triangles
                              : (long)a2[0] * a2[1] - (long)a2[2] * a2[2];
                    Ok(L(p.Answer) == area, "area " + p.Visual + " = " + p.Answer + " (want " + area + ")");
                    // surface area from the net's faces
                    p = Problems.Make("surface", lv);
                    if (p.Kind != "choice" && p.Visual.StartsWith("net:") && !p.Visual.Contains("pyramid"))
                    {
                        string[] f = p.Visual.Substring(4).Split(',');
                        long l = L(f[0]), w = L(f[1]), h = L(f[2]);
                        var faces = new List<long> { l * w, l * w, l * h, l * h, w * h, w * h };
                        if (f.Length > 3) faces.RemoveAt(0);   // no lid
                        Ok(L(p.Answer) == faces.Sum(), "surface " + p.Visual + " = " + p.Answer);
                    }
                    if (p.Visual.StartsWith("net:pyramid") && p.Kind != "choice") { string[] f = p.Visual.Split(','); long b = L(f[1]), sl = L(f[2]); Ok(L(p.Answer) == b * b + 2 * b * sl, "pyramid " + p.Text); }
                    // data displays: recount from the plotted data
                    p = Problems.Make("plots", lv);
                    if (p.Visual.StartsWith("dots:"))
                    {
                        int[] data = p.Visual.Split(';')[1].Split(',').Select(s => int.Parse(s, Inv)).OrderBy(x => x).ToArray();
                        if (p.Text.Contains("How many data")) Ok(L(p.Answer) == data.Length, "dot count");
                        if (p.Text.Contains("mode")) { var cnt = data.GroupBy(x => x).Select(g => new { g.Key, N = g.Count() }).ToList(); int top = cnt.Max(x => x.N); Ok(cnt.Count(x => x.N == top) == 1 && L(p.Answer) == cnt.First(x => x.N == top).Key, "mode " + p.Visual); }
                        if (p.Text.Contains("median")) { Ok(data.Length % 2 == 1 && Dv(p.Answer) == data[data.Length / 2], "median " + p.Visual); }
                        if (p.Text.Contains("range")) Ok(L(p.Answer) == data.Last() - data.First(), "range");
                    }
                    if (p.Visual.StartsWith("boxplot:"))
                    {
                        double[] f = p.Visual.Split(';')[1].Split(',').Select(s => Dv(s)).ToArray();
                        Ok(f[0] <= f[1] && f[1] <= f[2] && f[2] <= f[3] && f[3] <= f[4], "box plot in order " + p.Visual);
                        if (p.Text.Contains("IQR")) Ok(Math.Abs(Dv(p.Answer) - (f[3] - f[1])) < 1e-9, "IQR");
                        if (p.Text.Contains("median")) Ok(Math.Abs(Dv(p.Answer) - f[2]) < 1e-9, "box median");
                    }
                    // ratio tables: every column keeps the first column's ratio once the answer is filled in
                    p = Problems.Make("ratiotable", lv);
                    {
                        string[] parts = p.Visual.Split(':')[1].Split(';');
                        string[] t = parts[2].Split(','), b = parts[3].Split(',');
                        for (int k = 0; k < t.Length; k++) { if (t[k] == "?") t[k] = p.Answer; if (b[k] == "?") b[k] = p.Answer; }
                        int k0 = Array.FindIndex(t, s => s != "0");
                        long a0 = L(t[k0]), b0 = L(b[k0]);
                        bool same = true; for (int k = 0; k < t.Length; k++) if (L(t[k]) * b0 != L(b[k]) * a0) same = false;
                        Ok(same, "ratio table keeps its ratio " + p.Visual + " answer " + p.Answer);
                    }
                    // 2nd grade: blocks, cash and graphs recounted from their pictures
                    p = Problems.Make("placeblocks", lv);
                    if (p.Text.StartsWith("What number do the blocks")) Ok(p.Visual == "blocks:" + p.Answer, "blocks show the answer");
                    p = Problems.Make("money2", lv);
                    if (p.Text.StartsWith("How much money")) { long cents = p.Visual.Substring(5).Split(',').Sum(s => L(s)); Ok(Math.Abs(Dv(p.Answer.Substring(1)) * 100 - cents) < 1e-6, "cash adds up " + p.Visual + " = " + p.Answer); }
                    else { m = Regex.Match(p.Text, @"costs (\$?)(\d+(?:\.\d\d)?)(\u00A2?)"); long paid = L(p.Visual.Substring(5)); long price = m.Groups[1].Value == "$" ? (long)Math.Round(Dv(m.Groups[2].Value) * 100) : L(m.Groups[2].Value); double change; Answers.TryValue(p.Answer, out change); long ch = p.Answer.Contains("$") ? (long)Math.Round(change * 100) : (long)change; Ok(ch == paid - price && ch > 0, "change " + p.Text + " = " + p.Answer); }
                    p = Problems.Make("graphs2", lv);
                    {
                        string[] g = p.Visual.Split(':')[1].Split(';'); string[] labels = g[2].Split(','); long[] vals = g[3].Split(',').Select(s => L(s)).ToArray();
                        if (p.Text.Contains("in all?") && !p.Text.Contains(" and ")) Ok(L(p.Answer) == vals.Sum(), "graph total");
                        m = Regex.Match(p.Text, @"How many more (.+) than (.+)\?");
                        if (m.Success) Ok(L(p.Answer) == vals[Array.IndexOf(labels, m.Groups[1].Value)] - vals[Array.IndexOf(labels, m.Groups[2].Value)], "graph difference");
                    }
                    // science: neutrons from the element tile
                    p = Problems.Make("sci6", lv);
                    if (p.Text.Contains("neutrons")) { string[] e = p.Visual.Split(':')[1].Split(','); Ok(L(p.Answer) == L(e[3]) - L(e[0]), "neutrons " + p.Text); }
                    // division stories: the answer is the quotient, one more, or the remainder
                    p = Problems.Make("divword", lv);
                    {
                        long ans = L(p.Answer);
                        long[] nums = Regex.Matches(p.Text, @"\d[\d,]*").Cast<Match>().Select(x => L(x.Value)).ToArray();
                        long n = nums.Max(); bool found = false;
                        foreach (long d in nums) if (d > 1 && d < n) { long q = n / d, r = n % d; if (r > 0 && (ans == q || ans == q + 1 || ans == r)) found = true; }
                        bool up = p.Text.Contains("needed") || p.Text.Contains("need") || p.Text.Contains("loads");
                        Ok(found, "divword " + p.Text + " = " + p.Answer);
                        if (up && !p.Text.Contains("left")) foreach (long d in nums) if (d > 1 && d < n && ans == n / d + 1) Ok(n % d > 0, "rounding up only with a remainder");
                    }
                }
        }
        static bool Ok2(bool cond, string what) { Ok(cond, what); return cond; }
        static bool Regroups(long a, long b, bool add)
        {
            if (add) return a % 10 + b % 10 >= 10 || a / 10 % 10 + b / 10 % 10 >= 10;
            return a % 10 < b % 10 || a / 10 % 10 < b / 10 % 10;
        }

        // ---------------------------------------------------------------- rewards
        static void Rewards_()
        {
            Ok(Rewards.Plans.All(p => p.Count >= 40 && p.Rows.All(r => r.Length == 16)), "build plans: 16 wide, enough blocks");
            Ok(Rewards.Plans.All(p => p.Rows.All(r => r.All(ch => ch == '.' || "GDSPLWROFTYBQ".IndexOf(ch) >= 0))), "build plans use known blocks");
            int lastPlan = 0, lastPlaced = -1;
            for (int pts = 0; pts <= Rewards.TotalBlocks + 50; pts++)
            {
                int plan, placed; Rewards.BuildState(pts, out plan, out placed);
                Ok(plan >= lastPlan && (plan > lastPlan || placed >= lastPlaced), "builds only move forward at " + pts);
                lastPlan = plan; lastPlaced = placed;
            }
            Ok(Rewards.Status(2, 0).StartsWith("Building a cozy house: 0 of"), "build status: " + Rewards.Status(2, 0));
            Ok(Rewards.Status(2, 100000).StartsWith("Master builder"), "every build done");
            Ok(Rewards.Unlocked(2, 3, 8) == "You placed 5 new blocks!", "unlocked blocks: " + Rewards.Unlocked(2, 3, 8));
            Ok(Rewards.Unlocked(6, 0, Rewards.PointsPerSample).Contains("Hydrogen (H, 1)"), "the first lab sample is hydrogen");
            Ok(Rewards.Lab.Last().Symbol == "U" && Rewards.Lab.Last().Z == 92, "uranium is the last sample");
            Ok(Rewards.Lab.Where(x => x.Kind == "element").Select(x => x.Z).SequenceEqual(Rewards.Lab.Where(x => x.Kind == "element").Select(x => x.Z).OrderBy(z => z)), "elements in order of atomic number");
            Ok(Rewards.Status(6, 100000).StartsWith("Your lab is complete"), "lab complete");
            foreach (int g in new[] { 2, 6 }) foreach (int pts in new[] { 0, 1, 37, 99, 150, 400 }) { Avalonia.Controls.Control e = Rewards.Picture(g, pts, true); Ok(e != null && e.Width > 50 && e.Height > 50 && e.Width < 1100, "reward picture " + g + "/" + pts + " " + (e == null ? "" : e.Width + "x" + e.Height)); }
        }
    }
}
