// Learning Games - math problems for 2nd and 6th grade (Common Core aligned), with answers that accept equivalent forms,
// believable wrong choices for the click-the-answer games, and step-by-step hints that teach instead of telling.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LearningGames
{
    public sealed class Problem
    {
        public string Skill = "", Text = "", Answer = "", Unit = "";
        public string[] Choices = new string[0];
        public string[] Hints = new string[0];
        /// <summary>A worked explanation of this exact problem when the generator writes its own (Worked.Steps uses the hints otherwise).</summary>
        public string[] Steps = new string[0];
        public int Grade, Level;
        /// <summary>What to draw next to the question: "array:3x4", "coins:25,25,10,5", "clock:3:15", "fraction:3/4", "line:-5,5", "".</summary>
        public string Visual = "";
        /// <summary>"number" (typed), "fraction", "money", "time", "choice" (only the listed choices make sense).</summary>
        public string Kind = "number";
        public bool IsCorrect(string given) { return Answers.Same(given, Answer, Kind); }
    }

    public sealed class SkillInfo
    {
        public string Id = "", Name = "", Area = "";
        public int Grade;
        /// <summary>The Arizona standard(s) it practises (docs\math-standards.md).</summary>
        public string Std = "";
        /// <summary>Short and fine without its picture, so the fast games (Asteroid Blaster, Math Race, Boss Battle) use it.</summary>
        public bool Quick = true;
        public SkillInfo(int grade, string id, string name, string area) { Grade = grade; Id = id; Name = name; Area = area; }
        public SkillInfo(int grade, string id, string name, string area, string std, bool quick) { Grade = grade; Id = id; Name = name; Area = area; Std = std; Quick = quick; }
    }

    public static class Answers
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Reads 12, -3, 2.5, .5, 3/4, -1 1/2, $1.25, 125¢, 3:15 as a number (time as minutes). False when it is not a number.</summary>
        public static bool TryValue(string s, out double v)
        {
            v = 0;
            if (s == null) return false;
            s = s.Trim().Replace(" ", " ").Replace("−", "-").Replace("$", "").Replace("¢", "").Replace(",", "").Replace("%", "");
            if (s.EndsWith("c") || s.EndsWith("cents")) s = s.Replace("cents", "").TrimEnd('c').Trim();
            if (s.Length == 0) return false;
            if (s.Contains(":"))
            {
                string[] t = s.Split(':'); int h, m;
                if (t.Length == 2 && int.TryParse(t[0], out h) && int.TryParse(t[1], out m)) { v = h * 60 + m; return true; }
                return false;
            }
            bool neg = s.StartsWith("-"); if (neg) s = s.Substring(1).Trim();
            double whole = 0;
            string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) { if (!double.TryParse(parts[0], NumberStyles.Float, Inv, out whole)) return false; s = parts[1]; }
            if (s.Contains("/"))
            {
                string[] f = s.Split('/'); double a, b;
                if (f.Length != 2 || !double.TryParse(f[0], NumberStyles.Float, Inv, out a) || !double.TryParse(f[1], NumberStyles.Float, Inv, out b) || b == 0) return false;
                v = whole + a / b;
            }
            else
            {
                double a;
                if (!double.TryParse(s, NumberStyles.Float, Inv, out a)) return false;
                v = parts.Length == 2 ? whole : a;
                if (parts.Length == 2) return false;   // "3 4" is not a number
            }
            if (neg) v = -v;
            return true;
        }

        public static bool Same(string given, string answer, string kind)
        {
            if (given == null) return false;
            string g = given.Trim().ToLowerInvariant(), a = answer.Trim().ToLowerInvariant();
            if (g == a) return true;
            if (kind == "choice") return false;
            if (kind == "qr")
            {
                // "134 R 11", "134r11", "134 remainder 11"; a whole answer ("12") when nothing is left over
                long gq, gr, aq, ar;
                return TryQr(g, out gq, out gr) && TryQr(a, out aq, out ar) && gq == aq && gr == ar;
            }
            if (kind == "simplest")
            {
                // the same value, and the fraction written in lowest terms (3/4, not 6/8)
                double sx, sy;
                if (!TryValue(g, out sx) || !TryValue(a, out sy) || Math.Abs(sx - sy) > 1e-9) return false;
                var m = System.Text.RegularExpressions.Regex.Match(g, @"(\d+)\s*/\s*(\d+)\s*$");
                return !m.Success || Gcd(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) == 1;
            }
            if (kind == "mixed")
            {
                // a mixed number: the same value; a plain fraction or whole number only when the answer is one
                double mx, my;
                if (!TryValue(g, out mx) || !TryValue(a, out my) || Math.Abs(mx - my) > 1e-9) return false;
                return a.Contains(" ") == g.Contains(" ") || !g.Contains("/");
            }
            if (kind == "money")
            {
                // compare in cents: "$1.25", "1.25" and "125" all match $1.25; "38", "38¢", "0.38" and "$.38" all match 38¢
                double gv, av;
                if (!TryValue(g, out gv) || !TryValue(a, out av)) return false;
                bool aDollars = a.Contains("$") || a.Contains("."), gDollars = g.Contains("$") || g.Contains(".");
                bool gCents = g.Contains("¢") || g.EndsWith("c") || g.EndsWith("cents");
                double ac = aDollars ? av * 100 : av;
                if (gDollars) return Math.Abs(gv * 100 - ac) < 0.001;
                if (gCents) return Math.Abs(gv - ac) < 0.001;
                // a bare number: cents, or whole dollars when the answer is written in dollars ("2" for $2.00)
                return Math.Abs(gv - ac) < 0.001 || (aDollars && Math.Abs(gv * 100 - ac) < 0.001);
            }
            double x, y;
            return TryValue(g, out x) && TryValue(a, out y) && Math.Abs(x - y) < 1e-6;
        }

        /// <summary>Reads a quotient with a remainder: "134 R 11", "134r11", "134 rem 11", or just "12" (remainder 0).</summary>
        public static bool TryQr(string s, out long q, out long r)
        {
            q = 0; r = 0;
            if (s == null) return false;
            var m = System.Text.RegularExpressions.Regex.Match(s.Trim().ToLowerInvariant().Replace(",", ""), @"^(\d+)\s*(?:(?:r|rem|remainder)\.?\s*(\d+))?$");
            if (!m.Success) return false;
            q = long.Parse(m.Groups[1].Value, Inv);
            if (m.Groups[2].Success) r = long.Parse(m.Groups[2].Value, Inv);
            return true;
        }

        public static string Num(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9) return ((long)Math.Round(v)).ToString(Inv);
            return Math.Round(v, 4).ToString("0.####", Inv);
        }
        public static string Money(int cents) { return cents < 100 ? cents + "¢" : "$" + (cents / 100.0).ToString("0.00", Inv); }
        public static int Gcd(int a, int b) { a = Math.Abs(a); b = Math.Abs(b); while (b != 0) { int t = a % b; a = b; b = t; } return a == 0 ? 1 : a; }
        public static string Frac(int n, int d)
        {
            if (d < 0) { n = -n; d = -d; }
            int g = Gcd(n, d); n /= g; d /= g;
            if (d == 1) return n.ToString(Inv);
            return n + "/" + d;
        }
    }

    public static partial class Problems
    {
        // the first skills (2026-10-13); the ones added on 2026-10-24 are in MoreProblems.cs (MoreSkills)
        static readonly SkillInfo[] FirstSkills = {
            // 2nd grade
            new SkillInfo(2, "add20", "Adding within 20", "Facts"),
            new SkillInfo(2, "sub20", "Subtracting within 20", "Facts"),
            new SkillInfo(2, "add100", "Adding 2-digit numbers", "Add & subtract"),
            new SkillInfo(2, "sub100", "Subtracting 2-digit numbers", "Add & subtract"),
            new SkillInfo(2, "place", "Place value (hundreds, tens, ones)", "Numbers"),
            new SkillInfo(2, "skip", "Skip counting by 2s, 5s, 10s, 100s", "Numbers"),
            new SkillInfo(2, "compare", "Comparing numbers (<, >, =)", "Numbers"),
            new SkillInfo(2, "evenodd", "Even and odd", "Numbers"),
            new SkillInfo(2, "money", "Counting money", "Money & time"),
            new SkillInfo(2, "time", "Telling time (5 minutes)", "Money & time"),
            new SkillInfo(2, "arrays", "Arrays (rows and columns)", "Getting ready to multiply"),
            new SkillInfo(2, "words2", "Word problems", "Add & subtract"),
            new SkillInfo(2, "add1000", "Adding and subtracting to 1,000", "Add & subtract"),
            new SkillInfo(2, "tenmore", "10 or 100 more and less", "Numbers"),
            new SkillInfo(2, "measure", "Measuring length (inches, feet, cm)", "Measuring"),
            // 6th grade
            new SkillInfo(6, "ratio", "Equivalent ratios", "Ratios"),
            new SkillInfo(6, "rate", "Unit rates", "Ratios"),
            new SkillInfo(6, "percent", "Percent of a number", "Ratios"),
            new SkillInfo(6, "fracdiv", "Dividing fractions", "Fractions"),
            new SkillInfo(6, "decimals", "Decimal operations", "Decimals"),
            new SkillInfo(6, "negatives", "Negative numbers", "Number system"),
            new SkillInfo(6, "gcflcm", "GCF and LCM", "Number system"),
            new SkillInfo(6, "exponents", "Exponents and order of operations", "Expressions"),
            new SkillInfo(6, "evaluate", "Evaluating expressions", "Expressions"),
            new SkillInfo(6, "equations", "One-step equations", "Equations"),
            new SkillInfo(6, "area", "Area of triangles and shapes", "Geometry"),
            new SkillInfo(6, "volume", "Volume of boxes", "Geometry"),
            new SkillInfo(6, "stats", "Mean, median, range", "Statistics"),
        };

        /// <summary>Every skill: the first ones, then the newer ones, each with its standard and whether it suits the fast games.</summary>
        public static SkillInfo[] Skills { get { if (skills == null) skills = AllSkills(); return skills; } }   // built on first use: the parts live in two files
        static SkillInfo[] skills;
        static SkillInfo[] AllSkills()
        {
            var all = new List<SkillInfo>(FirstSkills);
            all.AddRange(MoreSkills);
            foreach (SkillInfo s in all) { string std, area; if (s.Std.Length == 0 && FirstStandards.TryGetValue(s.Id, out std)) s.Std = std; if (FirstAreas.TryGetValue(s.Id, out area)) s.Area = area; }
            // the areas in the order the "Practice a skill" list shows them
            string[] order = AreaOrder;
            return all.OrderBy(s => s.Grade).ThenBy(s => { int i = Array.IndexOf(order, s.Area); return i < 0 ? 99 : i; }).ToArray();
        }
        public static SkillInfo Info(string id) { return Skills.FirstOrDefault(s => s.Id == id); }
        public static string[] SkillsFor(int grade) { return Skills.Where(s => s.Grade == grade).Select(s => s.Id).ToArray(); }
        /// <summary>The skills that suit the fast games (short, no picture needed).</summary>
        public static string[] QuickSkillsFor(int grade) { return Skills.Where(s => s.Grade == grade && s.Quick).Select(s => s.Id).ToArray(); }
        public static string[] AreasFor(int grade) { return Skills.Where(s => s.Grade == grade).Select(s => s.Area).Distinct().ToArray(); }
        public static string[] SkillsIn(int grade, string area) { return Skills.Where(s => s.Grade == grade && s.Area == area).Select(s => s.Id).ToArray(); }

        static Random rng = new Random();
        /// <summary>Makes the problems repeatable (tests and pictures).</summary>
        public static void Seed(int seed) { lock (sync) rng = new Random(seed); }
        static readonly object sync = new object();
        static int R(int lo, int hi) { lock (sync) return rng.Next(lo, hi + 1); }
        static T Pick<T>(params T[] a) { return a[R(0, a.Length - 1)]; }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A problem for a skill at a level (1 easy .. 5 hard).</summary>
        public static Problem Make(string skill, int level)
        {
            level = Math.Max(1, Math.Min(5, level));
            Problem p;
            switch (skill)
            {
                case "add20": p = Add20(level); break;
                case "sub20": p = Sub20(level); break;
                case "add100": p = Add100(level); break;
                case "sub100": p = Sub100(level); break;
                case "place": p = Place(level); break;
                case "skip": p = Skip(level); break;
                case "compare": p = Compare(level); break;
                case "evenodd": p = EvenOdd(level); break;
                case "money": p = MoneyP(level); break;
                case "time": p = TimeP(level); break;
                case "arrays": p = Arrays(level); break;
                case "words2": p = Words2(level); break;
                case "add1000": p = Add1000(level); break;
                case "tenmore": p = TenMore(level); break;
                case "measure": p = Measure(level); break;
                case "ratio": p = Ratio(level); break;
                case "rate": p = Rate(level); break;
                case "percent": p = Percent(level); break;
                case "fracdiv": p = FracDiv(level); break;
                case "decimals": p = Decimals(level); break;
                case "negatives": p = Negatives(level); break;
                case "gcflcm": p = GcfLcm(level); break;
                case "exponents": p = Exponents(level); break;
                case "evaluate": p = Evaluate(level); break;
                case "equations": p = Equations(level); break;
                case "area": p = Area(level); break;
                case "volume": p = Volume(level); break;
                case "stats": p = Stats(level); break;
                default: p = MakeMore(skill, level); if (p == null) throw new ArgumentException("Unknown skill " + skill); break;
            }
            p.Skill = skill; p.Level = level;
            SkillInfo si = Info(skill); p.Grade = si != null ? si.Grade : 0;
            if (p.Choices.Length == 0) p.Choices = Distractors(p);
            return p;
        }

        // ---------------------------------------------------------------- wrong choices that look right
        static string[] Distractors(Problem p)
        {
            var set = new List<string> { p.Answer };
            double v;
            if ((p.Kind == "fraction" || p.Kind == "simplest") && System.Text.RegularExpressions.Regex.IsMatch(p.Answer, @"^-?\d+/\d+$"))
            {
                string[] f = p.Answer.Split('/'); int n = int.Parse(f[0]), d = int.Parse(f[1]);
                foreach (string c in new[] { Answers.Frac(d, n), Answers.Frac(n + 1, d), Answers.Frac(n, d + 1), Answers.Frac(Math.Max(1, n - 1), d), Answers.Frac(n * 2, d) })
                    if (c.Contains("/") && !set.Any(x => Answers.Same(x, c, "number")) && set.Count < 4) set.Add(c);   // a whole number among fractions gives it away
            }
            else if (p.Kind == "time")
            {
                double m; Answers.TryValue(p.Answer, out m);
                foreach (int off in new[] { 5, -5, 60, -60, 10, 15 })
                {
                    int t = (int)m + off; int h = ((t / 60) + 11) % 12 + 1, mi = ((t % 60) + 60) % 60;
                    string c = h + ":" + mi.ToString("00");
                    if (!set.Contains(c) && set.Count < 4) set.Add(c);
                }
            }
            else if (Answers.TryValue(p.Answer, out v))
            {
                bool money = p.Kind == "money";
                if (money && !p.Answer.Contains("$")) v = v / 100;   // "38¢": work in dollars
                bool dec = p.Answer.Contains(".") && !money;
                var offs = new List<double>();
                double mag = Math.Abs(v);
                if (dec) offs.AddRange(new[] { 0.1, -0.1, 1, -1, v * 9, 0.5 });          // misplaced decimal point (v*10)
                else if (money) offs.AddRange(new[] { 0.05, -0.05, 0.10, -0.10, 0.25, 0.01 });
                else if (mag <= 20) offs.AddRange(new[] { 1.0, -1, 2, -2, 10, -10 });
                else offs.AddRange(new[] { 10.0, -10, 1, -1, 100, 2 });
                if (v < 0 || p.Skill == "negatives") offs.Insert(0, -2 * v);              // sign mistake
                foreach (double o in offs.OrderBy(x => R(0, 1000)).Take(6).Concat(offs))
                {
                    double c = v + o;
                    if (c < 0 && v >= 0 && p.Skill != "negatives" && p.Skill != "equations") continue;   // no negatives where they make no sense
                    if (money && c < 0.005) continue;
                    string s = money ? (c * 100 < 100 ? Math.Round(c * 100) + "¢" : "$" + c.ToString("0.00", Inv)) : Answers.Num(c);
                    if (!set.Any(x => Answers.Same(x, s, p.Kind)) && set.Count < 4) set.Add(s);
                }
            }
            // fill up with choices of the same shape as the answer, never a second right answer or a repeat
            double av0; bool hasValue = Answers.TryValue(p.Answer, out av0);
            if (p.Kind == "money" && !p.Answer.Contains("$") && !p.Answer.Contains(".")) av0 = av0 / 100;
            for (int tries = 0; set.Count < 4 && tries < 400; tries++)
            {
                string c;
                if (p.Kind == "fraction") { int d = R(2, 12); int n = R(1, 2 * d); if (Answers.Gcd(n, d) != 1) continue; c = Answers.Frac(n, d); }
                else if (p.Kind == "time") c = R(1, 12) + ":" + (5 * R(0, 11)).ToString("00");
                else if (p.Kind == "money") { int cents = Math.Max(1, (int)Math.Round(av0 * 100) + R(-60, 60)); c = Answers.Money(cents); }
                else if (hasValue)
                {
                    int spread = Math.Max(5, (int)Math.Min(1000, Math.Abs(av0) / 2));
                    double cv = Math.Round(av0) + R(-spread, spread);
                    if (cv < 0 && av0 >= 0 && p.Skill != "negatives" && p.Skill != "equations") continue;
                    c = Answers.Num(cv);
                }
                else c = Answers.Num(R(1, 50));
                if (!set.Any(x => x == c || Answers.Same(x, c, p.Kind) || Answers.Same(c, x, p.Kind))) set.Add(c);
            }
            return set.OrderBy(x => R(0, 1000)).ToArray();
        }

        static Problem P(string text, string answer, params string[] hints) { Problem p = new Problem(); p.Text = text; p.Answer = answer; p.Hints = hints; return p; }
        /// <summary>Gives a problem its worked steps (Worked.Steps).</summary>
        static Problem W(Problem p, IEnumerable<string> steps) { p.Steps = steps.Where(s => !string.IsNullOrEmpty(s)).ToArray(); return p; }
        static Problem W(Problem p, params string[] steps) { return W(p, (IEnumerable<string>)steps); }

        // ================================================================ 2nd grade
        static Problem Add20(int lv)
        {
            int max = new[] { 0, 10, 12, 15, 18, 20 }[lv];
            int a = R(1, max - 1), b = R(1, max - a);
            if (lv >= 3 && a + b < 11) { a = R(5, 9); b = R(11 - a, 9); }   // make ten
            int ten = 10 - Math.Max(a, b);
            var hints = new List<string> {
                "Start with the bigger number, " + Math.Max(a, b) + ", and count on " + Math.Min(a, b) + ".",
                a + b > 10 && ten > 0 && ten < Math.Min(a, b) ? "Make a ten: " + Math.Max(a, b) + " + " + ten + " = 10. You still have " + (Math.Min(a, b) - ten) + " left to add." : "Use your fingers or picture " + Math.Min(a, b) + " dots." };
            if (a + b > 10) hints.Add("10 and " + (a + b - 10) + " more makes...");
            int big = Math.Max(a, b), small = Math.Min(a, b);
            Problem p20 = P(a + " + " + b + " = ?", (a + b).ToString(), hints.ToArray());
            if (a + b > 10 && ten > 0 && ten < small)
                return W(p20, "Start with the bigger number, " + big + ".", "Make a ten: " + big + " + " + ten + " = 10.",
                    "Take the " + ten + " out of " + small + ": " + small + " − " + ten + " = " + (small - ten) + " still to add.", "10 + " + (small - ten) + " = " + (a + b) + ".");
            return W(p20, "Start with the bigger number, " + big + ", and count on " + small + ": " + Worked.Count(big, small, 1) + ".", big + " + " + small + " = " + (a + b) + ".");
        }
        static Problem Sub20(int lv)
        {
            int max = new[] { 0, 10, 12, 15, 18, 20 }[lv];
            int a = R(2, max), b = R(1, a - 1);
            if (lv >= 3 && a > 10 && a <= 18 && b < a - 10) b = R(a - 9, 9);   // crosses ten
            Problem ps = P(a + " − " + b + " = ?", (a - b).ToString(),
                "Think addition: " + b + " + ? = " + a + ".",
                a > 10 && b > a - 10 ? "Take " + (a - 10) + " away first to get to 10, then take away " + (b - (a - 10)) + " more." : "Count back " + b + " from " + a + ".");
            if (a > 10 && b > a - 10)
                return W(ps, "Start at " + a + ". Take away " + (a - 10) + " first to get to 10: " + a + " − " + (a - 10) + " = 10.",
                    "You still have to take away " + b + " − " + (a - 10) + " = " + (b - (a - 10)) + " more.", "10 − " + (b - (a - 10)) + " = " + (a - b) + ".",
                    "Check by adding: " + (a - b) + " + " + b + " = " + a + ".");
            return W(ps, "Start at " + a + " and count back " + b + ": " + Worked.Count(a, b, -1) + ".", a + " − " + b + " = " + (a - b) + ".", "Check by adding: " + (a - b) + " + " + b + " = " + a + ".");
        }
        static Problem Add100(int lv)
        {
            bool regroup = lv >= 3;
            int a, b;
            do { a = R(11, lv <= 2 ? 59 : 89); b = R(10, 99 - a); } while (b < 10 || ((a % 10 + b % 10 >= 10) != regroup && lv != 5));
            if (lv == 5 && R(0, 1) == 1) { a = R(100, 500); b = R(10, 99); }
            return W(P(a + " + " + b + " = ?", (a + b).ToString(),
                "Add the tens: " + (a / 10 * 10) + " + " + (b / 10 * 10) + " = " + (a / 10 * 10 + b / 10 * 10) + ".",
                "Add the ones: " + (a % 10) + " + " + (b % 10) + " = " + (a % 10 + b % 10) + (a % 10 + b % 10 >= 10 ? ". That's more than 10, so it makes another ten." : "."),
                "Put the tens and ones together."), Worked.ColumnAdd(a, b));
        }
        static Problem Sub100(int lv)
        {
            bool regroup = lv >= 3;
            int a, b;
            do { a = R(20, 99); b = R(10, a - 1); } while ((a % 10 < b % 10) != regroup);
            return W(P(a + " − " + b + " = ?", (a - b).ToString(),
                regroup ? "You can't take " + (b % 10) + " from " + (a % 10) + ", so break one ten into 10 ones: " + (a % 10 + 10) + " − " + (b % 10) + " = " + (a % 10 + 10 - b % 10) + "." : "Ones first: " + (a % 10) + " − " + (b % 10) + " = " + (a % 10 - b % 10) + ".",
                "Now the tens: " + (regroup ? (a / 10 - 1) : a / 10) + " tens − " + (b / 10) + " tens.",
                "Check: " + b + " + your answer should make " + a + "."), Worked.ColumnSub(a, b));
        }
        static Problem Place(int lv)
        {
            int n, pos, digit;
            string s;
            do
            {
                // the asked-about digit is not 0 and appears only once ("the 3 in 353" would be unclear)
                n = lv <= 2 ? R(10, 99) : R(100, 999); s = n.ToString(); pos = R(0, s.Length - 1); digit = s[pos] - '0';
            } while (digit == 0 || s.Count(ch => ch == s[pos]) != 1);
            string[] names = s.Length == 3 ? new[] { "hundreds", "tens", "ones" } : new[] { "tens", "ones" };
            int value = digit * (int)Math.Pow(10, s.Length - 1 - pos);
            if (lv >= 4 && R(0, 1) == 1)
            {
                int h = n / 100, t = n / 10 % 10, o = n % 10;
                return W(P(h + " hundreds, " + t + " tens and " + o + " ones = ?", n.ToString(), "Each hundred is 100, each ten is 10.", h + " hundreds = " + (h * 100) + ", " + t + " tens = " + (t * 10) + ".", "Add them: " + (h * 100) + " + " + (t * 10) + " + " + o + "."),
                    "Each hundred is 100: " + h + " × 100 = " + (h * 100) + ".", "Each ten is 10: " + t + " × 10 = " + (t * 10) + ".", "Ones are just themselves: " + o + ".",
                    "Add them: " + (h * 100) + " + " + (t * 10) + " + " + o + " = " + n + ".");
            }
            int worth = names[pos] == "hundreds" ? 100 : names[pos] == "tens" ? 10 : 1;
            Problem p = P("What is the value of the " + digit + " in " + n + "?", value.ToString(),
                "The " + digit + " is in the " + names[pos] + " place.",
                names[pos] == "ones" ? "Ones are worth just themselves." : "Each one in the " + names[pos] + " place is worth " + (names[pos] == "hundreds" ? "100" : "10") + ".",
                digit + " × " + (names[pos] == "hundreds" ? 100 : names[pos] == "tens" ? 10 : 1) + " = ?");
            p.Visual = "place:" + n;
            return W(p, "Find where the " + digit + " is in " + n + ": the " + names[pos] + " place.",
                names[pos] == "ones" ? "A digit in the ones place is worth just itself." : "Each one in the " + names[pos] + " place is worth " + worth + ".",
                digit + " × " + worth + " = " + value + ".");
        }
        static Problem Skip(int lv)
        {
            int by = lv <= 1 ? Pick(2, 10) : lv == 2 ? Pick(2, 5, 10) : lv <= 4 ? Pick(5, 10, 100) : Pick(5, 10, 100, 25);
            int start = by * R(0, lv <= 2 ? 6 : 12) + (lv >= 4 && by != 100 ? 0 : 0);
            if (by == 100) start = 100 * R(0, 6);
            var seq = Enumerable.Range(0, 4).Select(i => start + i * by).ToArray();
            return W(P("What comes next?  " + string.Join(", ", seq.Select(x => x.ToString()).ToArray()) + ", ?", (start + 4 * by).ToString(),
                "How much does it grow each time? " + seq[1] + " − " + seq[0] + " = " + by + ".", "Add " + by + " to " + seq[3] + "."),
                "Find how much it grows each time: " + seq[1] + " − " + seq[0] + " = " + by + ", and " + seq[2] + " − " + seq[1] + " = " + by + " too.",
                "So it counts by " + by + "s. Add " + by + " to the last number: " + seq[3] + " + " + by + " = " + (start + 4 * by) + ".");
        }
        static Problem Compare(int lv)
        {
            int a = lv <= 2 ? R(10, 99) : R(100, 999), b = R(0, 4) == 0 ? a : (lv <= 2 ? R(10, 99) : R(100, 999));
            if (lv >= 3 && a != b && R(0, 1) == 1) b = a / 10 * 10 + (a % 10 + R(1, 8)) % 10;   // only the ones differ
            string ans = a > b ? ">" : a < b ? "<" : "=";
            Problem p = P(a + "  ?  " + b, ans, "Look at the biggest place first (" + (a >= 100 || b >= 100 ? "hundreds" : "tens") + ").", "If those are the same, look at the next place.", "The open mouth of > and < eats the bigger number.");
            p.Kind = "choice"; p.Choices = new[] { "<", "=", ">" };
            return W(p, CompareSteps(a, b));
        }
        /// <summary>Comparing two whole numbers place by place, biggest place first.</summary>
        static List<string> CompareSteps(int a, int b)
        {
            var l = new List<string>();
            string sa = a.ToString(Inv), sb = b.ToString(Inv);
            string sign = a > b ? "The open side of > faces the bigger number: " + a + " > " + b + "." : "The open side of < faces the bigger number: " + a + " < " + b + ".";
            if (sa.Length != sb.Length)
            {
                l.Add(sa.Length > sb.Length ? a + " has more digits than " + b + ", so it is bigger." : b + " has more digits than " + a + ", so it is bigger.");
                l.Add(sign);
                return l;
            }
            string[] names = sa.Length == 3 ? new[] { "hundreds", "tens", "ones" } : new[] { "tens", "ones" };
            for (int i = 0; i < sa.Length; i++)
            {
                if (sa[i] == sb[i]) { l.Add("Compare the " + names[i] + ": " + sa[i] + " and " + sb[i] + " are the same, so look at the next place."); continue; }
                l.Add("Compare the " + names[i] + ": " + sa[i] + (sa[i] > sb[i] ? " is more than " : " is less than ") + sb[i] + ", so " + a + (a > b ? " is bigger than " : " is smaller than ") + b + ".");
                l.Add(sign);
                return l;
            }
            l.Add("Every digit is the same, so the numbers are equal.");
            return l;
        }
        static Problem EvenOdd(int lv)
        {
            int n = lv <= 2 ? R(1, 20) : R(10, 999);
            Problem p = P("Is " + n + " even or odd?", n % 2 == 0 ? "even" : "odd", "Look only at the ones digit: " + (n % 10) + ".", "Even numbers end in 0, 2, 4, 6 or 8. You can share them into two equal groups.");
            p.Kind = "choice"; p.Choices = new[] { "even", "odd" };
            return W(p, "Look only at the ones digit of " + n + ": it is " + (n % 10) + ".",
                n % 2 == 0 ? (n % 10) + " is one of 0, 2, 4, 6, 8, so " + n + " is even." : (n % 10) + " is one of 1, 3, 5, 7, 9, so " + n + " is odd.",
                n % 2 == 0 ? "It shares into two equal groups: " + n + " ÷ 2 = " + (n / 2) + "." : "Two equal groups leave 1 over: " + n + " ÷ 2 = " + (n / 2) + " R 1.");
        }
        static Problem MoneyP(int lv)
        {
            var coins = new List<int>();
            int count = 2 + lv;
            int[] pool = lv <= 1 ? new[] { 1, 5, 10 } : lv <= 3 ? new[] { 1, 5, 10, 25 } : new[] { 1, 5, 10, 25, 100 };
            for (int i = 0; i < count; i++) coins.Add(pool[R(0, pool.Length - 1)]);
            coins = coins.OrderByDescending(c => c).ToList();
            int total = coins.Sum();
            string[] names = coins.Select(c => c == 100 ? "dollar" : c == 25 ? "quarter" : c == 10 ? "dime" : c == 5 ? "nickel" : "penny").ToArray();
            var grouped = names.GroupBy(n => n).Select(g => g.Count() + " " + (g.Count() > 1 ? (g.Key == "penny" ? "pennies" : g.Key + "s") : g.Key));
            if (lv >= 4 && R(0, 1) == 1 && total < 100)
            {
                Problem c = P("You pay $1.00 for something that costs " + Answers.Money(total) + ". How much change do you get?", Answers.Money(100 - total), "Count up from " + Answers.Money(total) + " to $1.00.", "First count up to the next ten cents, then by tens.");
                c.Kind = "money";
                return W(c, "$1.00 is 100¢.", "Change = what you pay − what it costs: 100¢ − " + total + "¢ = " + (100 - total) + "¢.",
                    "Check by counting up: " + total + "¢ + " + (100 - total) + "¢ = 100¢.");
            }
            Problem p = P("How much money?  " + string.Join(", ", grouped.ToArray()), Answers.Money(total),
                "Quarter = 25¢, dime = 10¢, nickel = 5¢, penny = 1¢" + (pool.Contains(100) ? ", dollar = 100¢" : "") + ".",
                "Start with the biggest coins and count on: " + string.Join(", ", coins.Select((c, i) => coins.Take(i + 1).Sum().ToString()).Take(3).ToArray()) + "...");
            p.Kind = "money"; p.Visual = "coins:" + string.Join(",", coins.Select(c => c.ToString()).ToArray());
            var ms = new List<string> { "Each coin's value: " + string.Join(", ", coins.Distinct().Select(c => (c == 100 ? "dollar" : c == 25 ? "quarter" : c == 10 ? "dime" : c == 5 ? "nickel" : "penny") + " " + c + "¢").ToArray()) + ".",
                "Start with the biggest coin, " + coins[0] + "¢, and add the others one at a time:" };
            int run = coins[0];
            for (int i = 1; i < coins.Count; i++) { ms.Add(run + "¢ + " + coins[i] + "¢ = " + (run + coins[i]) + "¢"); run += coins[i]; }
            if (total >= 100) ms.Add(total + "¢ is " + Answers.Money(total) + " (100¢ make a dollar).");
            return W(p, ms);
        }
        static Problem TimeP(int lv)
        {
            int h = R(1, 12), m = lv <= 1 ? Pick(0, 30) : lv == 2 ? Pick(0, 15, 30, 45) : 5 * R(0, 11);
            if (lv >= 4 && R(0, 1) == 1)
            {
                int add = lv == 4 ? Pick(15, 30, 60) : 5 * R(2, 9);
                int t = h * 60 + m + add; int nh = ((t / 60) - 1) % 12 + 1, nm = t % 60;
                Problem q = P("It is " + h + ":" + m.ToString("00") + ". What time will it be in " + add + " minutes?", nh + ":" + nm.ToString("00"),
                    "Add the minutes first: " + m + " + " + add + " = " + (m + add) + ".", m + add >= 60 ? "60 minutes is one hour: move the hour ahead by one and keep " + (m + add - 60) + " minutes." : "The hour stays the same.");
                q.Kind = "time";
                return W(q, "Add the minutes: " + m + " + " + add + " = " + (m + add) + ".",
                    m + add >= 60 ? "That's an hour or more: " + (m + add) + " − 60 = " + (m + add - 60) + " minutes, and the hour moves from " + h + " to " + nh + "." : "That's less than 60, so the hour stays " + h + ".",
                    "The time is " + nh + ":" + nm.ToString("00") + ".");
            }
            Problem p = P("What time does the clock show?", h + ":" + m.ToString("00"),
                "The short hand shows the hour. It points at (or just past) " + h + ".", "The long hand shows minutes. Count by 5s from the 12: each number is 5 minutes.");
            p.Kind = "time"; p.Visual = "clock:" + h + ":" + m;
            return W(p, "The short hand shows the hour. It points " + (m == 0 ? "at " : "just past ") + h + ", so the hour is " + h + ".",
                m == 0 ? "The long hand points straight up at the 12: that's 0 minutes, o'clock." : "The long hand points at the " + (m / 5) + ". Count by 5s from the 12: " + (m / 5) + " × 5 = " + m + " minutes.",
                "Write the hour, a colon, then the minutes: " + h + ":" + m.ToString("00") + ".");
        }
        static Problem Arrays(int lv)
        {
            int r = R(2, lv <= 2 ? 3 : 5), c = R(2, lv <= 2 ? 4 : 5);
            Problem p = P(r + " rows of " + c + ". How many in all?", (r * c).ToString(),
                "Add one row at a time: " + string.Join(" + ", Enumerable.Repeat(c.ToString(), r).ToArray()) + ".",
                "Or skip count by " + c + ", " + r + " times.");
            p.Visual = "array:" + r + "x" + c;
            return W(p, "Each row has " + c + ". There are " + r + " rows.", "Add one row at a time: " + Worked.Repeat(c, r) + ".", "That's " + r + " groups of " + c + ": " + r + " × " + c + " = " + (r * c) + ".");
        }
        static Problem Words2(int lv)
        {
            string[] names = { "Mia", "Leo", "Ava", "Sam", "Zoe", "Max", "Ella", "Ben" };
            string[] things = { "stickers", "marbles", "apples", "cards", "shells", "crayons" };
            string n = Pick(names), t = Pick(things);
            int a = R(5, lv <= 2 ? 20 : 60), b = R(2, lv <= 2 ? 9 : 30);
            if (R(0, 1) == 0)
                return P(n + " has " + a + " " + t + ". A friend gives " + n + " " + b + " more. How many " + t + " now?", (a + b).ToString(), "\"Gives more\" means the number gets bigger: add.", a + " + " + b + " = ?");
            if (b > a) { int x = a; a = b + 5; b = x; }
            return P(n + " had " + a + " " + t + " and gave away " + b + ". How many are left?", (a - b).ToString(), "\"Gave away\" means fewer: subtract.", a + " − " + b + " = ?");
        }
        // 3-digit adding and subtracting (2.NBT.7): whole hundreds first, then no regrouping, then carrying and borrowing
        static Problem Add1000(int lv)
        {
            bool sub = R(0, 1) == 1;
            string minus = " \u2212 ";
            int a, b;
            if (lv <= 1)
            {
                if (sub) { a = 100 * R(3, 9); b = 100 * R(1, a / 100 - 1); }
                else { a = 100 * R(1, 6); b = 100 * R(1, 9 - a / 100); }
                return P(a + (sub ? minus : " + ") + b + " = ?", (sub ? a - b : a + b).ToString(),
                    "Think in hundreds: " + (a / 100) + " hundreds " + (sub ? "take away " : "and ") + (b / 100) + " hundreds.",
                    (a / 100) + (sub ? minus : " + ") + (b / 100) + " = " + (sub ? a / 100 - b / 100 : a / 100 + b / 100) + ", so the answer is that many hundreds.");
            }
            for (int tries = 0; ; tries++)
            {
                a = R(100, 899); b = R(100, 899);
                if (sub && a < b) { int t = a; a = b; b = t; }
                if (sub && a == b) continue;
                if (!sub && a + b > 999) continue;
                bool regroup = sub ? (a % 10 < b % 10 || a / 10 % 10 < b / 10 % 10) : (a % 10 + b % 10 >= 10 || a / 10 % 10 + b / 10 % 10 >= 10);
                if (lv == 2 && regroup) continue;
                if (lv >= 4 && !regroup && tries < 60) continue;
                break;
            }
            int ao = a % 10, bo = b % 10;
            if (sub)
                return W(P(a + minus + b + " = ?", (a - b).ToString(),
                    "Line them up: hundreds under hundreds, tens under tens, ones under ones.",
                    ao < bo ? "Ones: you can't take " + bo + " from " + ao + ", so break a ten into 10 ones: " + (ao + 10) + minus + bo + " = " + (ao + 10 - bo) + "." : "Ones first: " + ao + minus + bo + " = " + (ao - bo) + ".",
                    "Then the tens, then the hundreds. Check: " + b + " + your answer should make " + a + "."), Worked.ColumnSub(a, b));
            return W(P(a + " + " + b + " = ?", (a + b).ToString(),
                "Line them up: hundreds under hundreds, tens under tens, ones under ones.",
                "Ones first: " + ao + " + " + bo + " = " + (ao + bo) + (ao + bo >= 10 ? ". That's 1 ten and " + (ao + bo - 10) + " ones: carry the ten." : "."),
                "Then add the tens, then the hundreds (with anything you carried)."), Worked.ColumnAdd(a, b));
        }
        // 10 more, 10 less, 100 more, 100 less (2.NBT.8): only one digit changes, unless it crosses to a new ten or hundred
        static Problem TenMore(int lv)
        {
            bool more = R(0, 1) == 1;
            bool cross = lv >= 4 && R(0, 2) == 0;   // 395 + 10 = 405, 402 - 10 = 392: the hundreds digit changes too
            int step = cross || lv <= 1 ? 10 : lv == 2 ? 100 : Pick(10, 100);
            int n, digit;
            while (true)
            {
                n = R(110, 889);
                if (cross) n = n / 100 * 100 + (more ? 90 : 0) + n % 10;
                digit = step == 10 ? n / 10 % 10 : n / 100;
                if (!cross && (more ? digit == 9 : digit == 0)) continue;
                if ((!more && n - step < 100) || (more && n + step > 999)) continue;
                break;
            }
            int ans = more ? n + step : n - step;
            string place = step == 10 ? "tens" : "hundreds";
            string how = cross
                ? (more ? "The tens digit is 9, so 10 more makes a new hundred: the hundreds digit goes up by 1 and the tens digit becomes 0."
                        : "The tens digit is 0, so break a hundred into 10 tens first: the hundreds digit goes down by 1 and the tens digit becomes 9.")
                : (more ? "Make the " + place + " digit one bigger. The other digits stay the same." : "Make the " + place + " digit one smaller. The other digits stay the same.");
            return P("What is " + step + " " + (more ? "more" : "less") + " than " + n + "?", ans.ToString(),
                "Look at the " + place + " digit of " + n + ": it is " + digit + ".", how,
                n + (more ? " + " : " \u2212 ") + step + " = ?");
        }
        // measuring length (2.MD.1-5): read a ruler, compare lengths, inches in a foot, centimeters in a meter, length stories
        static Problem Measure(int lv)
        {
            string[] things = { "ribbon", "rope", "stick", "toy snake", "string", "paper strip" };
            string th = Pick(things);
            if (lv <= 2 && R(0, 3) != 0)
            {
                bool cm = lv == 2 && R(0, 1) == 1;
                int max = cm ? 15 : 12, len = R(2, cm ? 14 : (lv == 1 ? 8 : 11));
                Problem r = P("How long is the " + th + "?", len.ToString(),
                    "The " + th + " starts at 0 on the ruler.", "Find the number right under the end of the " + th + ".");
                r.Unit = cm ? "centimeters" : "inches"; r.Visual = "ruler:" + len + "," + max + "," + (cm ? "cm" : "in");
                // wrong choices: other marks on the same ruler, never 0
                var marks = new List<string> { len.ToString() };
                foreach (int d in new[] { 1, -1, 2, -2, 3, -3 }.OrderBy(x => R(0, 1000)))
                    if (marks.Count < 4 && len + d >= 1 && len + d <= max) marks.Add((len + d).ToString());
                r.Choices = marks.OrderBy(x => R(0, 1000)).ToArray();
                return r;
            }
            if (lv <= 3)
            {
                string[] pair = { th, Pick(things.Where(other => other != th).ToArray()) };
                string unit = R(0, 1) == 1 ? "inches" : "centimeters";
                int a = R(6, lv <= 2 ? 15 : 40), b = R(2, a - 2);
                Problem c = P("A " + pair[0] + " is " + a + " " + unit + " long. A " + pair[1] + " is " + b + " " + unit + " long. How much longer is the " + pair[0] + "?", (a - b).ToString(),
                    "\"How much longer\" means find the difference: subtract.", a + " \u2212 " + b + " = ?");
                c.Unit = unit; return c;
            }
            int kind = R(0, 4);
            if (kind == 0) { Problem f = P("How many inches are in 1 foot?", "12", "A foot is as long as a 12-inch ruler.", "Count the inches on a school ruler."); f.Unit = "inches"; return f; }
            if (kind == 1) { Problem m = P("How many centimeters are in 1 meter?", "100", "A meter stick has 100 small centimeter marks.", "Centi- means one hundredth, like 100 cents in a dollar."); m.Unit = "centimeters"; return m; }
            if (kind == 2) { Problem y = P("How many feet are in 1 yard?", "3", "A yardstick is as long as 3 rulers of 1 foot each.", "Lay three 12-inch rulers end to end."); y.Unit = "feet"; return y; }
            string[] names = { "Mia", "Leo", "Ava", "Sam", "Zoe", "Max", "Ella", "Ben" };
            string who = Pick(names), u = R(0, 1) == 1 ? "inches" : "centimeters";
            if (kind == 3)
            {
                int a = R(30, 90), b = R(10, a - 5);
                Problem w = P(who + "'s ribbon is " + a + " " + u + " long. " + who + " cuts off " + b + " " + u + ". How long is the ribbon now?", (a - b).ToString(),
                    "Cutting off makes it shorter: subtract.", a + " \u2212 " + b + " = ?");
                w.Unit = u; return w;
            }
            int s1 = R(15, 60), s2 = R(10, 39);
            Problem t2 = P(who + " lines up two sticks end to end. One is " + s1 + " " + u + " long and the other is " + s2 + " " + u + ". How long are they together?", (s1 + s2).ToString(),
                "End to end means put the lengths together: add.", s1 + " + " + s2 + " = ?");
            t2.Unit = u; return t2;
        }

        // ================================================================ 6th grade
        static Problem Ratio(int lv)
        {
            int a = R(1, lv <= 2 ? 5 : 9), b = R(2, lv <= 2 ? 6 : 12); if (a == b) b++;
            int k = R(2, lv <= 3 ? 5 : 12);
            bool missFirst = lv >= 3 && R(0, 1) == 1;
            if (missFirst)
                return W(P(a + " : " + b + "  =  ? : " + (b * k), (a * k).ToString(), "How did " + b + " become " + (b * k) + "? Multiply by " + k + ".", "Do the same to the other side: " + a + " × " + k + "."),
                    "Equivalent ratios multiply both parts by the same number.", "The second part went from " + b + " to " + (b * k) + ": " + (b * k) + " ÷ " + b + " = " + k + ".",
                    "Multiply the first part by " + k + " too: " + a + " × " + k + " = " + (a * k) + ".");
            return W(P(a + " : " + b + "  =  " + (a * k) + " : ?", (b * k).ToString(), "What did " + a + " get multiplied by to become " + (a * k) + "? " + (a * k) + " ÷ " + a + " = " + k + ".", "Multiply " + b + " by " + k + " too."),
                "Equivalent ratios multiply both parts by the same number.", "The first part went from " + a + " to " + (a * k) + ": " + (a * k) + " ÷ " + a + " = " + k + ".",
                "Multiply the second part by " + k + " too: " + b + " × " + k + " = " + (b * k) + ".");
        }
        static Problem Rate(int lv)
        {
            int unit = R(2, lv <= 2 ? 9 : 25), n = R(2, 8);
            string[][] ctx = {
                new[] { "{n} notebooks cost ${t}. How much does 1 notebook cost?", "$" },
                new[] { "A car drives {t} miles in {n} hours. How many miles per hour?", "" },
                new[] { "{n} pizzas feed {t} people. How many people per pizza?", "" },
                new[] { "You read {t} pages in {n} days. How many pages per day?", "" } };
            string[] c = Pick(ctx);
            int total = unit * n;
            if (lv >= 4 && c[1] == "$") { double u = unit + 0.25 * R(1, 3); string txt = c[0].Replace("{n}", n.ToString()).Replace("{t}", (u * n).ToString("0.00", Inv)); Problem m = P(txt, "$" + u.ToString("0.00", Inv), "Unit rate means \"for 1\".", "Divide the total by " + n + "."); m.Kind = "money"; return W(m, "Unit rate means \"for 1\": divide the total by how many.", "$" + (u * n).ToString("0.00", Inv) + " ÷ " + n + " = $" + u.ToString("0.00", Inv) + ".", "Check: $" + u.ToString("0.00", Inv) + " × " + n + " = $" + (u * n).ToString("0.00", Inv) + "."); }
            Problem p = P(c[0].Replace("{n}", n.ToString()).Replace("{t}", total.ToString()), c[1] == "$" ? "$" + unit + ".00" : unit.ToString(),
                "Unit rate means \"for 1\".", "Divide: " + total + " ÷ " + n + ".");
            if (c[1] == "$") p.Kind = "money";
            return W(p, "Unit rate means \"for 1\": divide the total by how many.", total + " ÷ " + n + " = " + unit + ".", "Check: " + unit + " × " + n + " = " + total + ".");
        }
        static Problem Percent(int lv)
        {
            int pct = lv <= 1 ? Pick(10, 50) : lv == 2 ? Pick(10, 25, 50, 75) : lv <= 4 ? 5 * R(1, 19) : R(1, 99);
            int baseN = lv <= 2 ? 4 * R(2, 25) : 20 * R(1, 15);
            if (lv == 5) baseN = R(20, 400);
            double ans = pct * baseN / 100.0;
            if (lv == 4 && R(0, 2) == 0 && Math.Abs(ans - Math.Round(ans)) < 1e-9)
                return W(P(Answers.Num(ans) + " is what percent of " + baseN + "?", pct.ToString(), "Percent means \"out of 100\".", "Divide: " + Answers.Num(ans) + " ÷ " + baseN + ", then multiply by 100."),
                    "Percent means \"out of 100\": find what part of " + baseN + " the " + Answers.Num(ans) + " is.", "Divide: " + Answers.Num(ans) + " ÷ " + baseN + " = " + Answers.Num(pct / 100.0) + ".",
                    "Multiply by 100 to make it a percent: " + Answers.Num(pct / 100.0) + " × 100 = " + pct + ", so " + pct + "%.");
            string dec = Answers.Num(pct / 100.0);
            return W(P(pct + "% of " + baseN + " = ?", Answers.Num(ans),
                pct % 10 == 0 ? "10% of " + baseN + " is " + Answers.Num(baseN / 10.0) + ". " + pct + "% is " + (pct / 10) + " times that." : "Write " + pct + "% as " + (pct / 100.0).ToString("0.##", Inv) + " and multiply.",
                (pct / 100.0).ToString("0.##", Inv) + " × " + baseN + " = ?"),
                pct % 10 == 0
                    ? new[] { "10% is one tenth: " + baseN + " ÷ 10 = " + Answers.Num(baseN / 10.0) + ".", pct + "% is " + (pct / 10) + " times 10%: " + (pct / 10) + " × " + Answers.Num(baseN / 10.0) + " = " + Answers.Num(ans) + "." }
                    : new[] { "Percent means \"out of 100\": " + pct + "% = " + pct + "/100 = " + dec + ".", "Multiply: " + dec + " × " + baseN + " = " + Answers.Num(ans) + "." });
        }
        static Problem FracDiv(int lv)
        {
            int a = R(1, 5), b = R(a + 1, 8), c = R(1, 5), d = R(c + 1, 8);
            if (lv <= 1) { int w = R(2, 6); d = R(2, 5); Problem q = P(w + " ÷ 1/" + d + " = ?", (w * d).ToString(), "How many 1/" + d + "s fit in one whole? " + d + ".", "In " + w + " wholes: " + w + " × " + d + "."); q.Visual = "fracdiv:" + w + "/1,1/" + d; return W(q, "Dividing by 1/" + d + " asks: how many 1/" + d + "s fit in " + w + "?", "One whole holds " + d + " pieces of size 1/" + d + ".", w + " wholes hold " + w + " × " + d + " = " + (w * d) + " pieces."); }
            int n = a * d, m = b * c;
            int gg = Answers.Gcd(n, m);
            Problem p = P(a + "/" + b + " ÷ " + c + "/" + d + " = ?", Answers.Frac(n, m),
                "Keep, change, flip: keep " + a + "/" + b + ", change ÷ to ×, flip " + c + "/" + d + " to " + d + "/" + c + ".",
                a + "/" + b + " × " + d + "/" + c + " = " + n + "/" + m + ".", "Simplify if you can (divide top and bottom by the same number).");
            p.Kind = "fraction";
            return W(p, "Keep, change, flip: keep " + a + "/" + b + ", change ÷ to ×, and flip " + c + "/" + d + " to " + d + "/" + c + ".",
                "Now multiply " + a + "/" + b + " × " + d + "/" + c + ". Tops: " + a + " × " + d + " = " + n + ". Bottoms: " + b + " × " + c + " = " + m + ".",
                gg > 1 ? "Simplify " + n + "/" + m + ": divide the top and the bottom by " + gg + ". " + n + " ÷ " + gg + " = " + (n / gg) + " and " + m + " ÷ " + gg + " = " + (m / gg) + "." : n + "/" + m + " is already in lowest terms.");
        }
        static Problem Decimals(int lv)
        {
            int op = lv <= 2 ? R(0, 1) : R(0, 3);
            double a = R(10, lv <= 2 ? 99 : 999) / 10.0, b = R(10, 99) / 10.0;
            switch (op)
            {
                case 0:
                    {
                        long ta = (long)Math.Round(a * 10), tb = (long)Math.Round(b * 10);
                        return W(P(Answers.Num(a) + " + " + Answers.Num(b) + " = ?", Answers.Num(a + b), "Line up the decimal points.", "Add like whole numbers, then put the point back in the same column."),
                            "Line up the decimal points. Both numbers have tenths, so count in tenths: " + Answers.Num(a) + " is " + ta + " tenths and " + Answers.Num(b) + " is " + tb + " tenths.",
                            "Add like whole numbers: " + ta + " + " + tb + " = " + (ta + tb) + " tenths.", "Put the point back one place from the right: " + Answers.Num(a) + " + " + Answers.Num(b) + " = " + Answers.Num((ta + tb) / 10.0) + ".");
                    }
                case 1:
                    {
                        if (b > a) { double t = a; a = b; b = t; }
                        long ta = (long)Math.Round(a * 10), tb = (long)Math.Round(b * 10);
                        return W(P(Answers.Num(a) + " − " + Answers.Num(b) + " = ?", Answers.Num(a - b), "Line up the decimal points (add a zero if it helps).", "Subtract like whole numbers."),
                            "Line up the decimal points. Count in tenths: " + Answers.Num(a) + " is " + ta + " tenths and " + Answers.Num(b) + " is " + tb + " tenths.",
                            "Subtract like whole numbers: " + ta + " − " + tb + " = " + (ta - tb) + " tenths.", "Put the point back one place from the right: " + Answers.Num(a) + " − " + Answers.Num(b) + " = " + Answers.Num((ta - tb) / 10.0) + ".");
                    }
                case 2:
                    {
                        double x = R(2, 9) / 10.0 * (lv >= 5 ? 10 : 1) + R(1, 9); double y = R(2, 9) / 10.0;
                        string xs = Answers.Num(x), ys = Answers.Num(y), prod = Answers.Num(Math.Round(x * y, 4));
                        int px = xs.Contains(".") ? xs.Length - xs.IndexOf('.') - 1 : 0, py = ys.Contains(".") ? ys.Length - ys.IndexOf('.') - 1 : 0;
                        long wx = long.Parse(xs.Replace(".", ""), Inv), wy = long.Parse(ys.Replace(".", ""), Inv);
                        return W(P(xs + " × " + ys + " = ?", prod, "Multiply as if there were no decimal points.", "Count the decimal places in both numbers together; your answer has that many."),
                            "Multiply as if there were no decimal points: " + wx + " × " + wy + " = " + (wx * wy) + ".",
                            xs + " has " + px + " decimal place" + (px == 1 ? "" : "s") + " and " + ys + " has " + py + ": " + px + " + " + py + " = " + (px + py) + " in all.",
                            "Put the point " + (px + py) + " places from the right in " + (wx * wy) + ": " + xs + " × " + ys + " = " + prod + ".");
                    }
                default:
                    {
                        double y = R(2, 9) / 10.0; int q = R(2, 12); double x = Math.Round(q * y, 2);
                        return W(P(Answers.Num(x) + " ÷ " + Answers.Num(y) + " = ?", q.ToString(), "Move the decimal point in both numbers so you divide by a whole number: multiply both by 10.", Answers.Num(x * 10) + " ÷ " + Answers.Num(y * 10) + " = ?"),
                            "Make the number you divide by a whole number: multiply both numbers by 10. The answer stays the same.",
                            Answers.Num(x) + " × 10 = " + Answers.Num(x * 10) + " and " + Answers.Num(y) + " × 10 = " + Answers.Num(y * 10) + ".",
                            "Now divide: " + Answers.Num(x * 10) + " ÷ " + Answers.Num(y * 10) + " = " + q + ".", "Check: " + q + " × " + Answers.Num(y) + " = " + Answers.Num(x) + ".");
                    }
            }
        }
        static Problem Negatives(int lv)
        {
            int a = R(-12, 12), b = R(-12, 12); if (a == 0) a = -5;
            int kind = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : R(0, 3);
            Problem p;
            switch (kind)
            {
                case 0: p = P("Which is greater:  " + a + "  or  " + b + "?", (Math.Max(a, b)).ToString(), "On a number line, numbers to the right are greater.", "Any positive number is greater than any negative number. −2 is greater than −8."); p.Kind = "choice"; p.Choices = new[] { a.ToString(), b.ToString() }.Distinct().ToArray(); if (a == b) { p.Text = "Is " + a + " equal to " + b + "?"; p.Answer = "yes"; p.Choices = new[] { "yes", "no" }; } p.Visual = "line:-12,12";
                    if (a == b) return W(p, "Both numbers are " + a + ": they sit on the same spot of the number line, so they are equal.");
                    return W(p, "Picture a number line: bigger numbers are further to the right.",
                        Math.Max(a, b) + " is to the right of " + Math.Min(a, b) + (Math.Min(a, b) < 0 && Math.Max(a, b) >= 0 ? " (any positive number or 0 is to the right of every negative)." : Math.Max(a, b) < 0 ? " (between two negatives, the one closer to 0 is greater)." : "."),
                        "So " + Math.Max(a, b) + " is greater.");
                case 1: p = P("|" + a + "| = ?", Math.Abs(a).ToString(), "Absolute value is the distance from 0.", "Distance is never negative."); p.Visual = "line:-12,12";
                    return W(p, "Absolute value is how far a number is from 0 on the number line.", a + " is " + Math.Abs(a) + " steps " + (a < 0 ? "left" : "right") + " of 0.", "Distance is never negative, so |" + a + "| = " + Math.Abs(a) + ".");
                case 2: p = P(a + " + " + Paren(b) + " = ?", (a + b).ToString(), "Start at " + a + " on the number line.", b >= 0 ? "Adding a positive moves right " + b + "." : "Adding a negative moves left " + (-b) + "."); p.Visual = "line:-24,24";
                    return W(p, "Start at " + a + " on the number line.", b >= 0 ? "Adding a positive number moves right " + b + "." : "Adding a negative number moves left " + (-b) + ".",
                        "You land on " + (a + b) + ": " + a + " + " + Paren(b) + " = " + (a + b) + ".");
                default: p = P(a + " − " + Paren(b) + " = ?", (a - b).ToString(), "Subtracting is adding the opposite: " + a + " + " + Paren(-b) + ".", -b >= 0 ? "Move right " + (-b) + " from " + a + "." : "Move left " + b + " from " + a + "."); p.Visual = "line:-24,24";
                    return W(p, "Subtracting is the same as adding the opposite: " + a + " − " + Paren(b) + " becomes " + a + " + " + Paren(-b) + ".",
                        -b >= 0 ? "Start at " + a + " and move right " + (-b) + "." : "Start at " + a + " and move left " + b + ".",
                        "You land on " + (a - b) + ": " + a + " + " + Paren(-b) + " = " + (a - b) + ".");
            }
        }
        static string Paren(int v) { return v < 0 ? "(" + v + ")" : v.ToString(); }
        static Problem GcfLcm(int lv)
        {
            int g = R(2, lv <= 2 ? 6 : 12), x = R(1, 6), y = R(1, 6); while (Answers.Gcd(x, y) != 1 || x == y) { x = R(1, 7); y = R(2, 8); }
            int a = g * x, b = g * y;
            if (R(0, 1) == 0 || lv <= 1)
                return W(P("Greatest common factor of " + a + " and " + b + " = ?", g.ToString(), "List the factors of " + Math.Min(a, b) + ".", "Find the biggest one that also divides " + Math.Max(a, b) + "."),
                    "Factors of " + a + ": " + string.Join(", ", Enumerable.Range(1, a).Where(f => a % f == 0).Select(f => f.ToString(Inv)).ToArray()) + ".",
                    "Factors of " + b + ": " + string.Join(", ", Enumerable.Range(1, b).Where(f => b % f == 0).Select(f => f.ToString(Inv)).ToArray()) + ".",
                    "In both lists: " + string.Join(", ", Enumerable.Range(1, Math.Min(a, b)).Where(f => a % f == 0 && b % f == 0).Select(f => f.ToString(Inv)).ToArray()) + ". The greatest is " + g + ".",
                    "Check: " + a + " ÷ " + g + " = " + (a / g) + " and " + b + " ÷ " + g + " = " + (b / g) + ".");
            int l = a * b / Answers.Gcd(a, b);
            int hi = Math.Max(a, b), lo = Math.Min(a, b);
            return W(P("Least common multiple of " + a + " and " + b + " = ?", l.ToString(), "List multiples of " + Math.Max(a, b) + ": " + Math.Max(a, b) + ", " + (2 * Math.Max(a, b)) + ", " + (3 * Math.Max(a, b)) + "...", "The first one " + Math.Min(a, b) + " also divides is the LCM."),
                "List the multiples of the bigger number, " + hi + ": " + string.Join(", ", Enumerable.Range(1, l / hi).Select(k => (k * hi).ToString(Inv)).ToArray()) + ".",
                "Stop at the first one that " + lo + " also divides: " + l + " ÷ " + lo + " = " + (l / lo) + ".",
                "So the least common multiple is " + l + ".");
        }
        static Problem Exponents(int lv)
        {
            if (lv <= 2 || R(0, 1) == 0)
            {
                int b = R(2, lv <= 2 ? 5 : 10), e = R(2, b <= 3 ? 5 : 3);
                var pw = new List<string> { "The exponent " + e + " says: multiply " + b + " by itself " + e + " times." };
                long run = b;
                for (int i = 2; i <= e; i++) { pw.Add(run + " × " + b + " = " + (run * b) + "."); run *= b; }
                pw.Add("So " + string.Join(" × ", Enumerable.Repeat(b.ToString(), e).ToArray()) + " = " + run + ".");
                return W(P(b + "^" + e + " = ?", Math.Pow(b, e).ToString(Inv), "The exponent says how many times to multiply " + b + " by itself.", string.Join(" × ", Enumerable.Repeat(b.ToString(), e).ToArray()) + "."), pw);
            }
            int a = R(2, 9), c = R(2, 6), d = R(2, 5), f = R(1, 9);
            int form = R(0, 2);
            if (form == 0) return W(P(a + " + " + c + " × " + d + " = ?", (a + c * d).ToString(), "Multiply before you add (order of operations).", c + " × " + d + " = " + (c * d) + ", then add " + a + "."),
                "Order of operations: multiply before you add.", "Multiply first: " + c + " × " + d + " = " + (c * d) + ".", "Then add: " + a + " + " + (c * d) + " = " + (a + c * d) + ".");
            if (form == 1) return W(P("(" + a + " + " + c + ") × " + d + " = ?", ((a + c) * d).ToString(), "Parentheses first.", "(" + (a + c) + ") × " + d + "."),
                "Parentheses come first.", "Inside them: " + a + " + " + c + " = " + (a + c) + ".", "Then multiply: " + (a + c) + " × " + d + " = " + ((a + c) * d) + ".");
            return W(P(c + "² + " + f + " × " + d + " = ?", (c * c + f * d).ToString(), "Exponents first, then multiply, then add.", c + "² = " + (c * c) + ", " + f + " × " + d + " = " + (f * d) + "."),
                "Order of operations: exponents first, then multiply, then add.", "Exponent: " + c + "² means " + c + " × " + c + " = " + (c * c) + ".",
                "Multiply: " + f + " × " + d + " = " + (f * d) + ".", "Add: " + (c * c) + " + " + (f * d) + " = " + (c * c + f * d) + ".");
        }
        static Problem Evaluate(int lv)
        {
            int x = R(1, lv <= 2 ? 6 : 12), a = R(2, 9), b = R(1, 20);
            int form = lv <= 2 ? R(0, 1) : R(0, 3);
            switch (form)
            {
                case 0: return W(P("What is " + a + "x + " + b + " when x = " + x + "?", (a * x + b).ToString(), a + "x means " + a + " × x.", a + " × " + x + " = " + (a * x) + ", then add " + b + "."),
                    "Put " + x + " where x is. " + a + "x means " + a + " times x.", "Multiply: " + a + " × " + x + " = " + (a * x) + ".", "Add: " + (a * x) + " + " + b + " = " + (a * x + b) + ".");
                case 1: { int xv = Math.Min(x, b); return W(P("What is " + b + " − x when x = " + xv + "?", (b - xv).ToString(), "Put " + xv + " where x is.", b + " − " + xv + "."),
                    "Put " + xv + " where x is.", "Subtract: " + b + " − " + xv + " = " + (b - xv) + "."); }
                case 2: { int y = R(1, 9); return W(P("What is 2x + 3y when x = " + x + " and y = " + y + "?", (2 * x + 3 * y).ToString(), "2x = 2 × " + x + " = " + (2 * x) + ".", "3y = 3 × " + y + " = " + (3 * y) + ". Add them."),
                    "Put " + x + " where x is and " + y + " where y is.", "2x means 2 × " + x + ": 2 × " + x + " = " + (2 * x) + ".", "3y means 3 × " + y + ": 3 × " + y + " = " + (3 * y) + ".", "Add them: " + (2 * x) + " + " + (3 * y) + " = " + (2 * x + 3 * y) + "."); }
                default: return W(P("What is x² − " + a + " when x = " + x + "?", (x * x - a).ToString(), "x² = " + x + " × " + x + " = " + (x * x) + ".", "Then subtract " + a + "."),
                    "Put " + x + " where x is. x² means x × x.", x + " × " + x + " = " + (x * x) + ".", "Then subtract: " + (x * x) + " − " + a + " = " + (x * x - a) + ".");
            }
        }
        static Problem Equations(int lv)
        {
            int x = R(lv <= 2 ? 1 : -5, lv <= 2 ? 12 : 20), a = R(2, 12), b = R(1, 30);
            if (x == 0) x = 4;
            int form = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : R(0, 3);
            switch (form)
            {
                case 0: return W(P("x + " + b + " = " + (x + b) + "     x = ?", x.ToString(), "Undo adding " + b + " by subtracting " + b + " from both sides.", (x + b) + " − " + b + " = ?"),
                    "x has " + b + " added to it. Undo that: subtract " + b + " from both sides.", (x + b) + " − " + b + " = " + x + ", so x = " + x + ".", "Check: " + x + " + " + b + " = " + (x + b) + ".");
                case 1: return W(P(a + "x = " + (a * x) + "     x = ?", x.ToString(), a + "x means " + a + " times x. Undo it by dividing both sides by " + a + ".", (a * x) + " ÷ " + a + " = ?"),
                    a + "x means " + a + " times x. Undo the times: divide both sides by " + a + ".", (a * x) + " ÷ " + a + " = " + x + ", so x = " + x + ".", "Check: " + a + " × " + Paren(x) + " = " + (a * x) + ".");
                case 2: return W(P("x − " + b + " = " + (x - b) + "     x = ?", x.ToString(), "Undo subtracting " + b + " by adding " + b + " to both sides.", (x - b) + " + " + b + " = ?"),
                    "x has " + b + " taken away. Undo that: add " + b + " to both sides.", (x - b) + " + " + b + " = " + x + ", so x = " + x + ".", "Check: " + x + " − " + b + " = " + (x - b) + ".");
                default: { int r = a * x; return W(P("x / " + a + " = " + x + "     x = ?", r.ToString(), "Undo dividing by " + a + " by multiplying both sides by " + a + ".", x + " × " + a + " = ?"),
                    "x is divided by " + a + ". Undo that: multiply both sides by " + a + ".", x + " × " + a + " = " + r + ", so x = " + r + ".", "Check: " + r + " ÷ " + a + " = " + x + "."); }
            }
        }
        static Problem Area(int lv)
        {
            int b = R(2, 15), h = R(2, 12);
            int form = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : R(0, 2);
            Problem p;
            if (form == 0) { p = P("Rectangle: " + b + " by " + h + ". Area = ?", (b * h).ToString(), "Area of a rectangle = length × width.", b + " × " + h + "."); p.Visual = "rect:" + b + "x" + h;
                W(p, "The area of a rectangle is length times width: how many unit squares cover it.", b + " × " + h + " = " + (b * h) + "."); }
            else if (form == 1) { if (b * h % 2 == 1) b++; p = P("Triangle: base " + b + ", height " + h + ". Area = ?", (b * h / 2).ToString(), "A triangle is half of a rectangle.", "Area = base × height ÷ 2 = " + b + " × " + h + " ÷ 2."); p.Visual = "tri:" + b + "x" + h;
                W(p, "A triangle is half of a rectangle with the same base and height.", "The rectangle: " + b + " × " + h + " = " + (b * h) + ".", "Half of it: " + (b * h) + " ÷ 2 = " + (b * h / 2) + "."); }
            else { p = P("Parallelogram: base " + b + ", height " + h + ". Area = ?", (b * h).ToString(), "Cut off the triangle on one side and move it to the other: it becomes a rectangle.", "Area = base × height."); p.Visual = "para:" + b + "x" + h;
                W(p, "Cut the triangle off one side and move it to the other: the parallelogram becomes a rectangle " + b + " by " + h + ".", "Area = base × height: " + b + " × " + h + " = " + (b * h) + "."); }
            p.Unit = "square units";
            return p;
        }
        static Problem Volume(int lv)
        {
            int l = R(2, 10), w = R(2, 8), h = R(2, 6);
            if (lv >= 4)
            {
                // fractional edge length (6.G.2)
                Problem q = P("Box: " + l + " by " + w + " by 1/2. Volume = ?", Answers.Frac(l * w, 2), "Volume = length × width × height.", l + " × " + w + " = " + (l * w) + ", then × 1/2 (half of it).");
                q.Kind = "fraction"; q.Unit = "cubic units";
                return W(q, "Volume = length × width × height.", "Length times width: " + l + " × " + w + " = " + (l * w) + ".", "Times 1/2 means half of it: " + (l * w) + " ÷ 2 = " + Answers.Num(l * w / 2.0) + ", which is " + Answers.Frac(l * w, 2) + ".");
            }
            Problem p = P("Box: " + l + " by " + w + " by " + h + ". Volume = ?", (l * w * h).ToString(), "Volume = length × width × height.", l + " × " + w + " = " + (l * w) + " for one layer; there are " + h + " layers.");
            p.Unit = "cubic units"; p.Visual = "box:" + l + "x" + w + "x" + h;
            return W(p, "Volume = length × width × height: how many unit cubes fill the box.", "One layer on the bottom: " + l + " × " + w + " = " + (l * w) + " cubes.", "There are " + h + " layers: " + (l * w) + " × " + h + " = " + (l * w * h) + ".");
        }
        static Problem Stats(int lv)
        {
            int n = lv <= 2 ? 3 : 5;
            var v = Enumerable.Range(0, n).Select(i => R(1, lv <= 2 ? 10 : 30)).ToList();
            int kind = R(0, 3);
            string list = string.Join(", ", v.Select(x => x.ToString()).ToArray());
            if (kind == 3)
            {
                // mode: one value shows up more often than any other
                int m = v[R(0, n - 1)];
                v[(Array.IndexOf(v.ToArray(), m) + 1) % n] = m;
                var counts = v.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
                if (counts.Count > 1 && counts[0].Count() == counts[1].Count()) { v[(Array.IndexOf(v.ToArray(), counts[1].Key))] = counts[0].Key; counts = v.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList(); }
                list = string.Join(", ", v.Select(x => x.ToString()).ToArray());
                if (counts.Count > 1 && counts[0].Count() == counts[1].Count()) kind = 2;   // still a tie: ask for the range instead
                else
                {
                    return W(P("Mode of " + list + " = ?", counts[0].Key.ToString(), "The mode is the number that shows up most often.", "Count how many times each number appears."),
                        "The mode is the number that shows up most often.",
                        "Count each one: " + string.Join(", ", counts.OrderBy(g => g.Key).Select(g => g.Key + " shows up " + (g.Count() == 1 ? "once" : g.Count() + " times")).ToArray()) + ".",
                        counts[0].Key + " shows up the most.");
                }
            }
            if (kind == 0)
            {
                int extra = (n - v.Sum() % n) % n; v[R(0, n - 1)] += extra;   // whole-number mean
                list = string.Join(", ", v.Select(x => x.ToString()).ToArray());
                return W(P("Mean (average) of " + list + " = ?", (v.Sum() / n).ToString(), "Add them all: the total is " + v.Sum() + ".", "Divide the total by how many numbers there are (" + n + ")."),
                    "Add them all: " + string.Join(" + ", v.Select(x => x.ToString()).ToArray()) + " = " + v.Sum() + ".",
                    "There are " + n + " numbers, so divide the total by " + n + ": " + v.Sum() + " ÷ " + n + " = " + (v.Sum() / n) + ".");
            }
            if (kind == 1)
            {
                var s = v.OrderBy(x => x).ToList();
                return W(P("Median of " + list + " = ?", s[n / 2].ToString(), "Put them in order: " + string.Join(", ", s.Select(x => x.ToString()).ToArray()) + ".", "The median is the middle one."),
                    "Put them in order from smallest to biggest: " + string.Join(", ", s.Select(x => x.ToString()).ToArray()) + ".",
                    "There are " + n + " numbers, so the middle one is number " + (n / 2 + 1) + " from either end: " + s[n / 2] + ".");
            }
            return W(P("Range of " + list + " = ?", (v.Max() - v.Min()).ToString(), "Find the biggest (" + v.Max() + ") and the smallest (" + v.Min() + ").", "Range = biggest − smallest."),
                "Find the biggest number (" + v.Max() + ") and the smallest (" + v.Min() + ").", "Range = biggest − smallest: " + v.Max() + " − " + v.Min() + " = " + (v.Max() - v.Min()) + ".");
        }

        // ---------------------------------------------------------------- number line positions (Number Line Frog)
        /// <summary>A number line from Lo to Hi with tick marks every Step and a target to find.</summary>
        public sealed class LinePuzzle { public double Lo, Hi, Step, Target; public string Label = "", Text = ""; public string[] TickLabels = new string[0]; }

        public static LinePuzzle Line(int grade, int level)
        {
            LinePuzzle q = new LinePuzzle();
            if (grade <= 2)
            {
                // 0-20 by 2s, 0-100 by 10s, 0-100 by 5s (the halfway marks), 0-1000 by 100s or 50s (2.NBT stays within 1000);
                // every target is on a tick, so it always has a lily pad
                int span = level <= 2 ? 20 : level <= 4 ? 100 : 1000;
                int step = level <= 2 ? 2 : level == 3 ? 10 : level == 4 ? 5 : Pick(100, 50);
                int steps = span / step;
                q.Lo = 0; q.Hi = span; q.Step = step;
                q.Target = step * R(1, steps - 1);
                q.Label = Answers.Num(q.Target);
                q.Text = "Jump to " + q.Label;
            }
            else
            {
                int kind = level <= 1 ? 0 : level <= 3 ? R(0, 1) : R(0, 2);
                if (kind == 0) { q.Lo = -10; q.Hi = 10; q.Step = 1; q.Target = R(-9, 9); q.Label = Answers.Num(q.Target); q.Text = "Jump to " + q.Label; }
                else if (kind == 1) { int d = Pick(2, 3, 4, 5, 6, 8); q.Lo = 0; q.Hi = 2; q.Step = 1.0 / d; int n = R(1, 2 * d - 1); q.Target = (double)n / d; q.Label = Answers.Frac(n, d); q.Text = "Jump to " + q.Label; }
                else { int d = Pick(2, 4, 5, 10); q.Lo = -3; q.Hi = 3; q.Step = 1.0 / d; int n = R(-3 * d + 1, 3 * d - 1); if (n == 0) n = 1; q.Target = (double)n / d; q.Label = d == 10 ? Answers.Num(q.Target) : Answers.Frac(n, d); q.Text = "Jump to " + q.Label; }
            }
            return q;
        }
    }

    // ---------------------------------------------------------------- adaptive levels per skill
    public sealed class SkillStat
    {
        public int Level = 1, Attempts, Correct, Streak, Misses;
        public List<int> Recent = new List<int>();   // 1 correct, 0 wrong, last 20
        public double Accuracy { get { return Recent.Count == 0 ? 0 : Recent.Average(); } }
        public bool Mastered { get { return Level >= 4 && Recent.Count >= 10 && Accuracy >= 0.85; } }

        /// <summary>Up a level after 4 right in a row, down after 2 wrong in a row.</summary>
        public void Record(bool correct)
        {
            Attempts++; Recent.Add(correct ? 1 : 0); if (Recent.Count > 20) Recent.RemoveAt(0);
            if (correct) { Correct++; Streak++; Misses = 0; if (Streak >= 4 && Level < 5) { Level++; Streak = 0; } }
            else { Streak = 0; Misses++; if (Misses >= 2 && Level > 1) { Level--; Misses = 0; } }
        }
    }
}
