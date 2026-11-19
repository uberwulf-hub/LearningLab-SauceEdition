// Learning Games - the second set of math skills (added 2026-10-24): long division and remainder stories, fractions,
// percents, ratio tables, coordinates, expressions, inequalities, nets, data displays and science lab stories for 6th
// grade; place value with blocks, regrouping, bills and coins, graphs, shapes, equal parts and blocky-world stories for
// 2nd grade. Every answer is computed from the numbers chosen; nothing is typed in by hand. docs\math-standards.md maps
// each skill to its Arizona standard; docs\math-facts-to-check.md lists every science fact used in the stories.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LearningGames
{
    public static partial class Problems
    {
        const string TX = " \u00D7 ", DV = " \u00F7 ", MN = " \u2212 ";

        static readonly SkillInfo[] MoreSkills = {
            // 2nd grade
            new SkillInfo(2, "placeblocks", "Hundreds, tens and ones blocks", "Numbers & place value", "2.NBT.A.1, 2.NBT.A.3", false),
            new SkillInfo(2, "regroup", "Regrouping with blocks", "Add & subtract", "2.NBT.B.7, 2.NBT.B.9", true),
            new SkillInfo(2, "words2step", "Two-step stories (Block World)", "Word problems", "2.OA.A.1", false),
            new SkillInfo(2, "blockwords", "Block World stories", "Word problems", "2.OA.A.1, 2.NBT.B.5", false),
            new SkillInfo(2, "money2", "Bills, coins and change", "Money & time", "2.MD.C.8", false),
            new SkillInfo(2, "estimate2", "Estimating length", "Measuring & data", "2.MD.A.1-3", false),
            new SkillInfo(2, "graphs2", "Picture graphs and bar graphs", "Measuring & data", "2.MD.D.10", false),
            new SkillInfo(2, "shapes2", "Shapes: sides, corners, faces", "Shapes & arrays", "2.G.A.1", false),
            new SkillInfo(2, "parts2", "Equal parts and rows of squares", "Shapes & arrays", "2.G.A.2, 2.G.A.3", false),
            // 6th grade
            new SkillInfo(6, "longdiv", "Long division (with remainders)", "Long division", "6.NS.B.2", false),
            new SkillInfo(6, "divword", "Remainder stories (round up, drop, or keep)", "Long division", "6.NS.B.2", false),
            new SkillInfo(6, "fracequiv", "Equivalent fractions and simplifying", "Fractions", "review of 4.NF.A.1, 5.NF.A.1", true),
            new SkillInfo(6, "fraccompare", "Comparing fractions", "Fractions", "review of 4.NF.A.2", true),
            new SkillInfo(6, "fracadd", "Adding and subtracting fractions", "Fractions", "review of 5.NF.A.1-2", true),
            new SkillInfo(6, "fracmul", "Multiplying fractions", "Fractions", "review of 5.NF.B.4", true),
            new SkillInfo(6, "fracwhy", "Why dividing fractions works", "Fractions", "6.NS.A.1", false),
            new SkillInfo(6, "mixed", "Mixed numbers", "Fractions", "review of 5.NF.A.1, 6.NS.A.1", true),
            new SkillInfo(6, "fdp", "Fractions, decimals and percents", "Percents", "AZ.6.NS.C.9, 6.RP.A.3c", true),
            new SkillInfo(6, "pctwhole", "Finding the whole (tape diagrams)", "Percents", "6.RP.A.3c", false),
            new SkillInfo(6, "pctmoney", "Discounts, tax and tips", "Percents", "6.RP.A.3c", false),
            new SkillInfo(6, "ratiotable", "Ratio tables and double number lines", "Ratios & rates", "6.RP.A.3a", false),
            new SkillInfo(6, "distrib", "The distributive property", "Expressions & equations", "6.NS.B.4c, 6.EE.A.3", true),
            new SkillInfo(6, "coords", "The coordinate plane (4 quadrants)", "Number system", "6.NS.C.6, 6.NS.C.8", false),
            new SkillInfo(6, "writeexpr", "Writing expressions", "Expressions & equations", "6.EE.A.2a-b, 6.EE.B.6", false),
            new SkillInfo(6, "inequal", "Inequalities", "Expressions & equations", "6.EE.B.5, 6.EE.B.8", false),
            new SkillInfo(6, "polyarea", "Area of polygons (cut into pieces)", "Geometry", "6.G.A.1", false),
            new SkillInfo(6, "surface", "Nets and surface area", "Geometry", "6.G.A.4", false),
            new SkillInfo(6, "plots", "Dot plots and box plots", "Statistics", "6.SP.B.4, 6.SP.B.5", false),
            new SkillInfo(6, "sci6", "Science lab stories", "Science lab", "6.RP.A.3, 6.NS.B.3, 6.NS.C.5, 6.SP.B.5", false),
        };

        // the first skills' standards and the areas they are listed under now
        static readonly Dictionary<string, string> FirstStandards = new Dictionary<string, string> {
            { "add20", "2.OA.B.2" }, { "sub20", "2.OA.B.2" }, { "add100", "2.NBT.B.5" }, { "sub100", "2.NBT.B.5" }, { "place", "2.NBT.A.1" },
            { "skip", "2.NBT.A.2" }, { "compare", "2.NBT.A.4" }, { "evenodd", "2.OA.C.3" }, { "money", "2.MD.C.8" }, { "time", "2.MD.C.7" },
            { "arrays", "2.OA.C.4" }, { "words2", "2.OA.A.1" }, { "add1000", "2.NBT.B.7" }, { "tenmore", "2.NBT.B.8" }, { "measure", "2.MD.A.1-4, 2.MD.B.5" },
            { "ratio", "6.RP.A.1, 6.RP.A.3a" }, { "rate", "6.RP.A.2, 6.RP.A.3b" }, { "percent", "6.RP.A.3c" }, { "fracdiv", "6.NS.A.1" },
            { "decimals", "6.NS.B.3" }, { "negatives", "6.NS.C.5-7" }, { "gcflcm", "6.NS.B.4a-b" }, { "exponents", "6.EE.A.1, 6.EE.A.2c" },
            { "evaluate", "6.EE.A.2c" }, { "equations", "6.EE.B.7" }, { "area", "6.G.A.1" }, { "volume", "6.G.A.2" }, { "stats", "6.SP.A.3, 6.SP.B.5c" },
        };
        static readonly Dictionary<string, string> FirstAreas = new Dictionary<string, string> {
            { "place", "Numbers & place value" }, { "skip", "Numbers & place value" }, { "compare", "Numbers & place value" }, { "evenodd", "Numbers & place value" },
            { "tenmore", "Numbers & place value" }, { "words2", "Word problems" }, { "measure", "Measuring & data" }, { "arrays", "Shapes & arrays" },
            { "ratio", "Ratios & rates" }, { "rate", "Ratios & rates" }, { "percent", "Percents" }, { "exponents", "Expressions & equations" },
            { "evaluate", "Expressions & equations" }, { "equations", "Expressions & equations" },
        };
        static readonly string[] AreaOrder = {
            "Facts", "Numbers & place value", "Add & subtract", "Word problems", "Money & time", "Measuring & data", "Shapes & arrays",
            "Long division", "Fractions", "Ratios & rates", "Percents", "Decimals", "Number system", "Expressions & equations", "Geometry", "Statistics", "Science lab" };

        static Problem MakeMore(string skill, int lv)
        {
            Problem p = MakeMore2(skill, lv);
            // story problems get a small picture of what they are about (our own pixel sprites, Visuals.Scene)
            if (p != null && p.Visual.Length == 0 && (skill == "blockwords" || skill == "words2step" || skill == "sci6" || skill == "divword")) { string s = SceneFor(p.Text, skill == "sci6" || skill == "divword"); if (s.Length > 0) p.Visual = "scene:" + s; }
            return p;
        }
        static bool Has(string t, string w) { return System.Text.RegularExpressions.Regex.IsMatch(t, @"\b" + w + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        static string SceneFor(string t, bool science)
        {
            var items = new List<string>();
            foreach (string m in new[] { "Gloomling", "Bouncy Blob", "Clatterbones" }) if (t.Contains(m)) items.Add(m == "Gloomling" ? "gloomling" : m == "Bouncy Blob" ? "blob" : "clatterbones");
            foreach (string b in BlockKinds) if (t.Contains(" " + b + " block")) { items.Add("pickaxe"); items.Add("block-" + b); }
            if (t.Contains("gems")) items.Add("gem");
            if (t.Contains("lantern")) items.Add("lantern");
            if (t.Contains("carrot")) items.Add("carrot");
            if (t.Contains("wood blocks") || t.Contains("barn")) items.Add("block-wood");
            if (t.Contains("tower")) items.Add("block-stone");
            if (science && (Has(t, "rover") || Has(t, "ore") || Has(t, "vein") || Has(t, "mining"))) items.Add("rover");
            if (science && (Has(t, "terrariums?") || Has(t, "fern") || Has(t, "seeds?"))) items.Add("jar");
            if (science && (Has(t, "beaker") || Has(t, "test tubes?") || Has(t, "solution") || Has(t, "lab"))) items.Add("beaker");
            return string.Join(",", items.Distinct().Take(3).ToArray());
        }
        static Problem MakeMore2(string skill, int lv)
        {
            switch (skill)
            {
                case "placeblocks": return PlaceBlocks(lv);
                case "regroup": return Regroup(lv);
                case "words2step": return Words2Step(lv);
                case "blockwords": return BlockWords(lv);
                case "money2": return Money2(lv);
                case "estimate2": return Estimate2(lv);
                case "graphs2": return Graphs2(lv);
                case "shapes2": return Shapes2(lv);
                case "parts2": return Parts2(lv);
                case "longdiv": return LongDivP(lv);
                case "divword": return DivWord(lv);
                case "fracequiv": return FracEquiv(lv);
                case "fraccompare": return FracCompare(lv);
                case "fracadd": return FracAdd(lv);
                case "fracmul": return FracMul(lv);
                case "fracwhy": return FracWhy(lv);
                case "mixed": return Mixed(lv);
                case "fdp": return Fdp(lv);
                case "pctwhole": return PctWhole(lv);
                case "pctmoney": return PctMoney(lv);
                case "ratiotable": return RatioTable(lv);
                case "distrib": return Distrib(lv);
                case "coords": return Coords(lv);
                case "writeexpr": return WriteExpr(lv);
                case "inequal": return Inequal(lv);
                case "polyarea": return PolyArea(lv);
                case "surface": return Surface(lv);
                case "plots": return Plots(lv);
                case "sci6": return Sci6(lv);
            }
            return null;
        }

        // ---------------------------------------------------------------- shared helpers
        static Problem Choice(string text, string answer, IEnumerable<string> wrong, params string[] hints)
        {
            Problem p = P(text, answer, hints);
            p.Kind = "choice";
            var set = new List<string> { answer };
            foreach (string w in wrong) if (w != null && !set.Contains(w) && set.Count < 4) set.Add(w);
            p.Choices = set.OrderBy(x => R(0, 1000)).ToArray();
            return p;
        }
        /// <summary>Up to four different choices built around a number answer (never negative unless the answer is).</summary>
        static string[] NumChoices(long answer, params long[] near)
        {
            var set = new List<string> { answer.ToString(Inv) };
            foreach (long n in near) { if ((n >= 0 || answer < 0) && !set.Contains(n.ToString(Inv)) && set.Count < 4) set.Add(n.ToString(Inv)); }
            for (int k = 1; set.Count < 4 && k < 50; k++)
            {
                long c = answer + (k % 2 == 0 ? k / 2 : -(k + 1) / 2) * Math.Max(1, Math.Abs(answer) / 10);
                if ((c >= 0 || answer < 0) && !set.Contains(c.ToString(Inv))) set.Add(c.ToString(Inv));
            }
            return set.OrderBy(x => R(0, 1000)).ToArray();
        }
        static string Fr(int n, int d) { return Answers.Frac(n, d); }
        static string MixedText(int n, int d)
        {
            // n/d (positive) as a mixed number in lowest terms: "1 3/4", "2", "3/5"
            int g = Answers.Gcd(n, d); n /= g; d /= g;
            if (d == 1) return n.ToString(Inv);
            if (n < d) return n + "/" + d;
            return (n / d) + " " + (n % d) + "/" + d;
        }
        static int Lcm(int a, int b) { return a / Answers.Gcd(a, b) * b; }
        static string Cents(long cents) { return "$" + (cents / 100) + "." + (cents % 100).ToString("00", Inv); }
        static string Comma(long n) { return n >= 10000 ? n.ToString("#,0", Inv) : n.ToString(Inv); }

        static readonly string[] Kids2 = { "Pip", "Zara", "Theo", "Lily", "Finn", "Ruby", "Omar", "Nia" };
        static readonly string[] Kids6 = { "Kai", "Nora", "Omar", "Priya", "Jonah", "Lena", "Mateo", "Ivy" };

        static readonly string[] Ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        static readonly string[] TensW = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
        /// <summary>A number below 1,000 in words: "four hundred six", "seventy-two".</summary>
        public static string Words(int n)
        {
            if (n < 20) return Ones[n];
            if (n < 100) return TensW[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");
            return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " " + Words(n % 100) : "");
        }

        // ================================================================ 2nd grade
        // hundreds, tens and ones as blocks (flats, rods, cubes), expanded form and number names
        static Problem PlaceBlocks(int lv)
        {
            int kind = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : lv == 3 ? R(1, 2) : lv == 4 ? R(2, 3) : R(2, 4);
            if (kind <= 1)
            {
                int n = kind == 0 ? R(12, 99) : R(101, 999);
                if (kind == 1 && R(0, 3) == 0) n = n / 100 * 100 + n % 10;   // sometimes no tens: 406
                int h = n / 100, t = n / 10 % 10, o = n % 10;
                Problem p = P("What number do the blocks show?", n.ToString(Inv),
                    "A flat is 100, a rod is 10, a little cube is 1.",
                    (h > 0 ? h + " hundreds = " + (h * 100) + ", " : "") + t + " tens = " + (t * 10) + ", " + o + " ones = " + o + ".",
                    "Put them together: " + (h > 0 ? (h * 100) + " + " : "") + (t * 10) + " + " + o + ".");
                p.Visual = "blocks:" + n;
                return W(p, "A flat is 100, a rod is 10, a little cube is 1.",
                    h > 0 ? h + " flats: " + h + " × 100 = " + (h * 100) + "." : "",
                    t + " rods: " + t + " × 10 = " + (t * 10) + ".", o + " little cubes: " + o + ".",
                    "Put them together: " + (h > 0 ? (h * 100) + " + " : "") + (t * 10) + " + " + o + " = " + n + ".");
            }
            if (kind == 2)
            {
                int n = R(101, 999); int h = n / 100, t = n / 10 % 10, o = n % 10;
                if (t == 0) { t = R(1, 9); n = h * 100 + t * 10 + o; }
                int which = R(0, 2);
                string[] partsS = { (h * 100).ToString(Inv), (t * 10).ToString(Inv), o.ToString(Inv) };
                string ans = partsS[which];
                partsS[which] = "?";
                Problem p = P(n + " = " + string.Join(" + ", partsS.Where(x => x != "0").ToArray()), ans,
                    "Expanded form shows what each digit is worth.", "In " + n + ": the " + h + " means " + (h * 100) + ", the " + t + " means " + (t * 10) + ", the " + o + " means " + o + ".");
                if (ans == "0") return PlaceBlocks(lv);
                p.Visual = "blocks:" + n;
                string[] places = { "hundreds", "tens", "ones" };
                return W(p, "Expanded form shows what each digit of " + n + " is worth.",
                    "The " + h + " is in the hundreds place: " + (h * 100) + ". The " + t + " is in the tens place: " + (t * 10) + ". The " + o + " is in the ones place: " + o + ".",
                    "The missing part is the " + places[which] + ": " + ans + ". Check: " + (h * 100) + " + " + (t * 10) + " + " + o + " = " + n + ".");
            }
            if (kind == 3)
            {
                int n = R(101, 999); if (R(0, 2) == 0) n = n / 100 * 100 + n % 10;
                int h = n / 100, t = n / 10 % 10, o = n % 10;
                var wrong = new List<string> { (h * 100 + o * 10 + t).ToString(Inv), (h * 10 + o).ToString(Inv), (h * 1000 + t * 10 + o).ToString(Inv), (t * 100 + h * 10 + o).ToString(Inv) };
                return Choice("Which number is " + Words(n) + "?", n.ToString(Inv), wrong.Where(w => w != n.ToString(Inv)),
                    "Listen for the hundreds first: " + Ones[h] + " hundred.", t == 0 ? "No tens are said, so the tens digit is 0." : "Then the tens and the ones.");
            }
            // tens in a whole number of tens; ten tens make a hundred
            if (R(0, 1) == 0)
            {
                int tens = R(11, 60); int n = tens * 10;
                return P("How many tens make " + n + "?", tens.ToString(Inv), "Each ten is 10. 10 tens make 100.", n + " is " + (n / 100) + " hundreds and " + (tens % 10) + " tens. " + (n / 100) + " hundreds = " + (n / 100 * 10) + " tens.");
            }
            int k = R(2, 9);
            return P(k + " hundreds = how many tens?", (k * 10).ToString(Inv), "1 hundred is the same as 10 tens.", k + " hundreds is " + k + " groups of 10 tens.");
        }

        // regrouping: add or subtract with a trade of 10 ones for a ten (or a ten for 10 ones), drawn with blocks
        static Problem Regroup(int lv)
        {
            int a, b; bool sub;
            switch (lv)
            {
                case 1: sub = false; do { a = R(15, 69); b = R(12, 99 - a); } while (b < 10 || a % 10 + b % 10 < 10); break;
                case 2: sub = true; do { a = R(31, 99); b = R(12, a - 5); } while (a % 10 >= b % 10); break;
                case 3:
                    // one trade only: the ones
                    sub = R(0, 1) == 1;
                    do { a = R(120, 680); b = R(110, 299); }
                    while (sub ? (b >= a || !(a % 10 < b % 10) || a / 10 % 10 < b / 10 % 10 + 1) : (a + b > 999 || a % 10 + b % 10 < 10 || a / 10 % 10 + b / 10 % 10 >= 9));
                    break;
                case 4: sub = R(0, 1) == 1; do { a = R(200, 799); b = R(120, 499); } while (sub ? b >= a || !(a / 10 % 10 < b / 10 % 10) : a + b > 999 || a / 10 % 10 + b / 10 % 10 < 10); break;
                default:
                    sub = true;
                    if (R(0, 1) == 0) { a = R(2, 9) * 100 + R(1, 8); b = R(110, a - 10); while (b % 10 <= a % 10 || b >= a) b = R(102, a - 1); }   // across a zero: 403 - 158
                    else { do { a = R(300, 950); b = R(120, a - 1); } while (!(a % 10 < b % 10 && a / 10 % 10 <= b / 10 % 10)); }
                    break;
            }
            int ans = sub ? a - b : a + b;
            Problem p;
            if (sub)
            {
                bool onesTrade = a % 10 < b % 10;
                p = P(a + MN + b + " = ?", ans.ToString(Inv),
                    onesTrade && a / 10 % 10 == 0 ? "Ones: " + (a % 10) + " is less than " + (b % 10) + ", and there are no tens to trade. First trade 1 hundred for 10 tens, then 1 ten for 10 ones: now you have " + (a % 10 + 10) + " ones and 9 tens."
                    : onesTrade ? "Ones: " + (a % 10) + " is less than " + (b % 10) + ". Trade 1 ten for 10 ones, so you have " + (a % 10 + 10) + " ones." : "Ones: " + (a % 10) + MN + (b % 10) + " = " + (a % 10 - b % 10) + ". No trade needed.",
                    "Tens next. If there aren't enough tens, trade 1 hundred for 10 tens.",
                    "Check: " + b + " + your answer should make " + a + ".");
            }
            else
            {
                int os = a % 10 + b % 10, ts = a / 10 % 10 + b / 10 % 10 + (os >= 10 ? 1 : 0);
                p = P(a + " + " + b + " = ?", ans.ToString(Inv),
                    "Ones: " + (a % 10) + " + " + (b % 10) + " = " + os + (os >= 10 ? ". That's 10 or more: trade 10 ones for 1 ten." : "."),
                    "Tens: " + (a / 10 % 10) + " + " + (b / 10 % 10) + (os >= 10 ? " + the 1 you traded" : "") + " = " + ts + (ts >= 10 ? ". Trade 10 tens for 1 hundred." : "."),
                    "Hundreds last, then read the number.");
            }
            p.Visual = "regroup:" + a + "," + b + "," + (sub ? "-" : "+");
            return W(p, sub ? Worked.ColumnSub(a, b) : Worked.ColumnAdd(a, b));
        }

        // ---- Block World stories (our own blocky building and mining world: miners, village traders, gems, the
        // Gloomlings that come out at night, Bouncy Blobs, Clatterbones)
        static readonly string[] BlockKinds = { "stone", "dirt", "sand", "wood", "glass", "brick", "snow", "clay" };
        static readonly string[] Mobs = { "Gloomlings", "Bouncy Blobs", "Clatterbones" };
        static Problem BlockWords(int lv)
        {
            string who = Pick(Kids2), blk = Pick(BlockKinds);
            int big = lv <= 2 ? 50 : lv <= 3 ? 100 : 500;
            int kind = R(0, lv <= 1 ? 3 : 6);
            switch (kind)
            {
                case 0: { int a = R(8, big / 2 + 5), b = R(5, big / 2); return P(who + " mined " + a + " " + blk + " blocks, then " + b + " more. How many " + blk + " blocks now?", (a + b).ToString(Inv), "Mined more: the pile gets bigger. Add.", a + " + " + b + " = ?"); }
                case 1: { int a = R(15, big), b = R(4, a - 3); return P(who + " had " + a + " " + blk + " blocks and used " + b + " to build a bridge. How many are left?", (a - b).ToString(Inv), "Used some: fewer are left. Subtract.", a + MN + b + " = ?"); }
                case 2: { int a = R(10, big), b = R(3, a - 2); string m = Pick(Mobs); return P("At night " + a + " " + m + " came out. The sun came up and " + b + " of them hopped away. How many " + m + " are still here?", (a - b).ToString(Inv), "Some went away: subtract.", a + MN + b + " = ?"); }
                case 3: { int need = R(20, big), have = R(5, need - 3); return P(who + "'s house needs " + need + " blocks. " + who + " has " + have + ". How many more blocks are needed?", (need - have).ToString(Inv), "How many more: start at " + have + " and count up to " + need + ", or subtract.", need + MN + have + " = ?"); }
                case 4: { int a = R(12, big), b = R(12, big); if (a == b) b++; string w2 = Pick(Kids2.Where(x => x != who).ToArray()); return P(who + " has " + a + " gems. " + w2 + " has " + b + " gems. How many more gems does " + (a > b ? who : w2) + " have?", Math.Abs(a - b).ToString(Inv), "Compare: find the difference between the bigger and the smaller number.", Math.Max(a, b) + MN + Math.Min(a, b) + " = ?"); }
                case 5: { int r = R(2, 5), c = R(2, 5); Problem p = P(who + " puts torches on a wall in " + r + " rows with " + c + " torches in each row. How many torches?", (r * c).ToString(Inv), "Rows of the same size: add the row " + r + " times.", string.Join(" + ", Enumerable.Repeat(c.ToString(Inv), r).ToArray()) + " = ?"); p.Visual = "array:" + r + "x" + c; return p; }
                default: { int price = R(3, 9) * 5, n = R(2, 3); return P("A village trader sells a lantern for " + price + " gems. " + who + " buys " + n + " lanterns. How many gems is that?", (price * n).ToString(Inv), "The same price " + n + " times: add it " + n + " times.", string.Join(" + ", Enumerable.Repeat(price.ToString(Inv), n).ToArray()) + " = ?"); }
            }
        }
        static Problem Words2Step(int lv)
        {
            string who = Pick(Kids2), blk = Pick(BlockKinds);
            int max = lv <= 2 ? 50 : lv <= 4 ? 100 : 300;
            int kind = R(0, lv <= 2 ? 2 : 4);
            int a, b, c, ans;
            switch (kind)
            {
                case 0:
                    do { a = R(10, max / 2); b = R(5, max / 2); c = R(3, a + b - 2); } while (a + b > max);
                    ans = a + b - c;
                    return P(who + " mined " + a + " " + blk + " blocks in the morning and " + b + " more after lunch. Then " + who + " used " + c + " to build a wall. How many " + blk + " blocks are left?", ans.ToString(Inv),
                        "Step 1: how many were mined in all? " + a + " + " + b + " = " + (a + b) + ".", "Step 2: some were used, so subtract: " + (a + b) + MN + c + " = ?");
                case 1:
                    do { a = R(25, max); b = R(5, a / 2); c = R(3, a - b - 1); } while (a - b - c < 0);
                    ans = a - b - c;
                    return P(who + " had " + a + " gems. " + who + " spent " + b + " gems on a pickaxe and " + c + " gems on a map. How many gems are left?", ans.ToString(Inv),
                        "Step 1: take away the pickaxe: " + a + MN + b + " = " + (a - b) + ".", "Step 2: take away the map: " + (a - b) + MN + c + " = ?");
                case 2:
                    {
                        string m = Pick(Mobs);
                        do { a = R(20, max); b = R(3, a - 5); c = R(3, max / 3); } while (a - b + c > max);
                        ans = a - b + c;
                        return P(who + "'s tower had " + a + " blocks. " + m + " knocked off " + b + " blocks in the night, so " + who + " added " + c + " new blocks in the morning. How many blocks is the tower now?", ans.ToString(Inv),
                            "Step 1: knocked off means fewer: " + a + MN + b + " = " + (a - b) + ".", "Step 2: added means more: " + (a - b) + " + " + c + " = ?");
                    }
                case 3:
                    {
                        int need = R(40, max); a = R(8, need / 2); b = R(5, need - a - 3);
                        ans = need - a - b;
                        return P("A barn needs " + need + " wood blocks. " + who + " chopped " + a + " blocks and a friend brought " + b + ". How many more blocks are needed?", ans.ToString(Inv),
                            "Step 1: how many do they have? " + a + " + " + b + " = " + (a + b) + ".", "Step 2: how many more to reach " + need + "? " + need + MN + (a + b) + " = ?");
                    }
                default:
                    {
                        int r = R(2, 5), k = R(2, 5); c = R(3, 15);
                        ans = r * k + c;
                        Problem p = P(who + " plants a garden of carrots in " + r + " rows of " + k + ". Then " + who + " plants " + c + " more carrots by the fence. How many carrots in all?", ans.ToString(Inv),
                            "Step 1: the rows: " + string.Join(" + ", Enumerable.Repeat(k.ToString(Inv), r).ToArray()) + " = " + (r * k) + ".", "Step 2: add the " + c + " by the fence.");
                        p.Visual = "array:" + r + "x" + k;
                        return p;
                    }
            }
        }

        // bills ($1, $5, $10) and coins, counting and making change; visual "cash:<cents>,..."
        static Problem Money2(int lv)
        {
            var items = new List<int>();
            if (lv <= 3)
            {
                int bills = lv == 1 ? R(2, 4) : R(1, 3);
                int[] billPool = lv == 1 ? new[] { 100, 500 } : new[] { 100, 500, 1000 };
                for (int i = 0; i < bills; i++) items.Add(billPool[R(0, billPool.Length - 1)]);
                if (lv >= 2) { int coins = R(1, lv == 2 ? 3 : 4); int[] cp = lv == 2 ? new[] { 25 } : new[] { 25, 10, 5, 1 }; for (int i = 0; i < coins; i++) items.Add(cp[R(0, cp.Length - 1)]); }
                items = items.OrderByDescending(x => x).ToList();
                int total = items.Sum();
                Problem p = P("How much money is this?", Cents(total),
                    "Count the bills first: a $1 bill is one dollar, $5 is five dollars, $10 is ten dollars.",
                    "Then count on with the coins: quarter 25\u00A2, dime 10\u00A2, nickel 5\u00A2, penny 1\u00A2.",
                    "Write dollars, a point, then cents: $" + (total / 100) + "." + (total % 100).ToString("00", Inv) + "?");
                p.Kind = "money"; p.Visual = "cash:" + string.Join(",", items.Select(x => x.ToString(Inv)).ToArray());
                var billsL = items.Where(x => x >= 100).ToList(); var coinsL = items.Where(x => x < 100).ToList();
                int bsum = billsL.Sum() / 100, csum = coinsL.Sum();
                return W(p, billsL.Count == 1 ? "Count the bills first: one $" + bsum + " bill is " + bsum + " dollars." : "Count the bills first: " + string.Join(" + ", billsL.Select(x => (x / 100).ToString(Inv)).ToArray()) + " = " + bsum + " dollars.",
                    coinsL.Count == 0 ? "There are no coins, so there are 0 cents." : coinsL.Count == 1 ? "Then the coin: " + csum + " cents." : "Then the coins: " + string.Join(" + ", coinsL.Select(x => x.ToString(Inv)).ToArray()) + " = " + csum + " cents.",
                    "Write dollars, a point, then the cents with two digits: " + bsum + " dollars and " + csum + " cents is " + Cents(total) + ".");
            }
            // making change
            int paid = lv == 4 ? 100 : Pick(500, 1000);
            int price = lv == 4 ? R(5, 19) * 5 : R(paid / 100 - 4 > 0 ? paid / 100 - 4 : 1, paid / 100 - 1) * 100 + R(0, 19) * 5;
            if (price >= paid) price = paid - 35;
            // under $1: small things; with a $5 or $10 bill: toys and craft things
            string what = paid == 100 ? Pick("an eraser", "a pencil", "a sticker", "a bouncy ball", "a bookmark", "a pack of seeds")
                : Pick("a toy pickaxe", "a box of crayons", "a block-building set", "a yo-yo", "a sticker book", "a kite");
            Problem c = P("You pay with " + (paid == 100 ? "a $1 bill" : paid == 500 ? "a $5 bill" : "a $10 bill") + " for " + what + " that costs " + Answers.Money(price) + ". How much change do you get?", Answers.Money(paid - price),
                "Count up from " + Answers.Money(price) + " to " + Answers.Money(paid) + ".",
                "First count up to the next whole dollar with coins, then add whole dollars.",
                Answers.Money(paid) + MN + Answers.Money(price) + " = ?");
            c.Kind = "money"; c.Visual = "cash:" + paid;
            W(c, "Change = what you pay" + MN + "what it costs.", "Work in cents: " + Answers.Money(paid) + " is " + paid + "¢ and " + Answers.Money(price) + " is " + price + "¢.",
                paid + "¢" + MN + price + "¢ = " + (paid - price) + "¢" + (paid - price >= 100 ? ", which is " + Answers.Money(paid - price) : "") + ".",
                "Check by counting up: " + price + "¢ + " + (paid - price) + "¢ = " + paid + "¢.");
            return c;
        }

        // estimating lengths of everyday things and choosing units (sizes are typical, see docs\math-facts-to-check.md)
        static readonly string[][] Lengths = {
            // thing, right answer, wrong, wrong
            new[] { "a new crayon", "about 4 inches", "about 4 feet", "about 4 yards" },
            new[] { "a new pencil", "about 7 inches", "about 7 feet", "about 7 centimeters" },
            new[] { "a paper clip", "about 3 centimeters", "about 3 meters", "about 3 feet" },
            new[] { "a school bus", "about 40 feet", "about 40 inches", "about 4 feet" },
            new[] { "a door (how tall)", "about 2 meters", "about 2 centimeters", "about 20 meters" },
            new[] { "a sheet of notebook paper (the long side)", "about 11 inches", "about 11 feet", "about 11 centimeters" },
            new[] { "a baseball bat", "about 1 yard", "about 1 inch", "about 10 yards" },
            new[] { "a blueberry", "about 1 centimeter", "about 1 meter", "about 10 inches" },
            new[] { "a football field (from one goal line to the other)", "about 100 yards", "about 100 inches", "about 10 feet" },
            new[] { "your hand (how wide)", "about 3 inches", "about 3 feet", "about 30 inches" },
        };
        static Problem Estimate2(int lv)
        {
            if (lv >= 3 && R(0, 2) == 0)
            {
                // more units when the unit is smaller (2.MD.A.2)
                string thing = Pick("a table", "a rug", "a jump rope", "a couch");
                bool askMore = R(0, 1) == 0;
                string a1 = Pick("inches", "centimeters"), a2 = a1 == "inches" ? "feet" : "meters";
                return Choice("You measure " + thing + " in " + a1 + " and in " + a2 + ". Which needs " + (askMore ? "more" : "fewer") + " units?", askMore ? a1 : a2, new[] { askMore ? a2 : a1, "the same number" },
                    "Which unit is smaller?", "Small units fit more times: it takes more " + a1 + " than " + a2 + " to cover the same length.");
            }
            if (lv >= 2 && R(0, 2) == 0)
            {
                string[][] tools = { new[] { "how long a bus is", "feet", "inches" }, new[] { "how long an ant is", "centimeters", "meters" }, new[] { "how long a hallway is", "meters", "centimeters" }, new[] { "how long an eraser is", "inches", "yards" } };
                string[] t = Pick(tools);
                return Choice("Which unit is best for measuring " + t[0] + "?", t[1], new[] { t[2] }, "Short things: small units. Long things: big units.", "Imagine counting: would you count a few, or thousands?");
            }
            string[] row = Pick(Lengths);
            return Choice("About how long is " + row[0] + "?", row[1], new[] { row[2], row[3] },
                "Think of something you know: a paper clip is a little longer than an inch (about 3 centimeters), a foot is a school ruler, a centimeter is about as wide as the nail on your little finger.",
                "Picture it next to a ruler. Which answer makes sense?");
        }

        // picture graphs (1 picture = 1) and bar graphs; visual "graph:<pic|bar>;<title>;<labels>;<values>"
        static Problem Graphs2(int lv)
        {
            string[][] sets = {
                new[] { "Blocks mined today", "Stone", "Dirt", "Coal", "Gold" },
                new[] { "Favorite biome", "Forest", "Desert", "Snow", "Jungle" },
                new[] { "Creatures seen at night", "Gloomlings", "Bouncy Blobs", "Clatterbones", "Bats" },
                new[] { "Pets in our class", "Dogs", "Cats", "Fish", "Birds" },
            };
            string[] s = Pick(sets);
            int n = lv <= 2 ? 3 : 4;
            bool bar = lv >= 3;
            int max = bar ? 12 : 8;
            var vals = new int[n];
            do { for (int i = 0; i < n; i++) vals[i] = R(1, max); } while (vals.Distinct().Count() < n);
            string[] labels = s.Skip(1).Take(n).ToArray();
            string vis = "graph:" + (bar ? "bar" : "pic") + ";" + s[0] + ";" + string.Join(",", labels) + ";" + string.Join(",", vals.Select(v => v.ToString(Inv)).ToArray());
            int i1 = R(0, n - 1), i2; do { i2 = R(0, n - 1); } while (i2 == i1);
            int kind = lv <= 1 ? R(0, 1) : lv == 2 ? R(1, 2) : lv == 3 ? R(0, 2) : R(2, 4);
            Problem p;
            string count = bar ? "Find the top of the bar and read across to the numbers." : "Count the pictures in that row: each picture means 1.";
            switch (kind)
            {
                case 0: p = P("Look at the graph. How many " + labels[i1] + "?", vals[i1].ToString(Inv), count); W(p, count, (bar ? "The bar for " + labels[i1] + " reaches " : "The row for " + labels[i1] + " has ") + vals[i1] + (bar ? "." : " pictures.")); break;
                case 1:
                    {
                        int most = Array.IndexOf(vals, vals.Max());
                        p = Choice("Look at the graph. Which has the most?", labels[most], labels.Where(l => l != labels[most]), "Find the longest row or the tallest bar.");
                        break;
                    }
                case 2:
                    {
                        if (vals[i1] < vals[i2]) { int t = i1; i1 = i2; i2 = t; }
                        p = P("Look at the graph. How many more " + labels[i1] + " than " + labels[i2] + "?", (vals[i1] - vals[i2]).ToString(Inv), count, "How many more: subtract the smaller from the bigger. " + vals[i1] + MN + vals[i2] + " = ?");
                        break;
                    }
                case 3: p = P("Look at the graph. How many " + labels[i1] + " and " + labels[i2] + " in all?", (vals[i1] + vals[i2]).ToString(Inv), count, "In all: add them. " + vals[i1] + " + " + vals[i2] + " = ?"); break;
                default: p = P("Look at the graph. How many in all?", vals.Sum().ToString(Inv), count, "Add all the numbers: " + string.Join(" + ", vals.Select(v => v.ToString(Inv)).ToArray()) + " = ?"); break;
            }
            p.Visual = vis;
            return p;
        }

        // shapes by sides, corners (angles) and faces; visual "shape:<name>"
        static readonly string[][] Shapes = {
            // name, sides
            new[] { "triangle", "3" }, new[] { "quadrilateral", "4" }, new[] { "pentagon", "5" }, new[] { "hexagon", "6" },
        };
        static Problem Shapes2(int lv)
        {
            int kind = lv <= 1 ? R(0, 1) : lv <= 3 ? R(0, 3) : R(1, 5);
            string[] s = Pick(Shapes);
            Problem p;
            switch (kind)
            {
                case 0: p = P("How many sides does this " + s[0] + " have?", s[1], "Put your finger on one side and count as you go around.", "Sides are the straight lines."); p.Visual = "shape:" + s[0]; return W(p, "Sides are the straight lines. Put your finger on one and count as you go around.", "A " + s[0] + " has " + s[1] + " sides: " + string.Join(", ", Enumerable.Range(1, int.Parse(s[1])).Select(i => i.ToString(Inv)).ToArray()) + ".");
                case 1: p = P("How many corners (angles) does this shape have?", s[1], "Count the pointy corners where two sides meet.", "A flat shape has the same number of corners as sides."); p.Visual = "shape:" + s[0]; return W(p, "Corners are where two sides meet. This shape is a " + s[0] + ".", "Count them as you go around: " + string.Join(", ", Enumerable.Range(1, int.Parse(s[1])).Select(i => i.ToString(Inv)).ToArray()) + ". A flat shape has as many corners as sides.");
                case 2: p = Choice("What is the name of this shape?", s[0], Shapes.Select(x => x[0]).Where(x => x != s[0]), "Count the sides first: 3 triangle, 4 quadrilateral, 5 pentagon, 6 hexagon.", "Penta- means 5, hexa- means 6."); p.Visual = "shape:" + s[0]; return p;
                case 3: p = Choice("Which shape has 4 equal sides and 4 square corners?", "square", new[] { "pentagon", "triangle", "hexagon" }, "Square corners look like the corner of a page.", "Count the sides first: 4 sides. A long rectangle has square corners too, but its sides are not all the same length. A square is a special rectangle."); p.Visual = "shape:square"; return p;
                case 4: p = P("How many faces does a cube have?", "6", "A face is a flat side. A number cube (a die) shows one number on each face.", "Count: top, bottom, front, back, left, right."); p.Visual = "shape:cube"; return W(p, "A face is a flat side, like each side of a number cube.", "Count them: top 1, bottom 2, front 3, back 4, left 5, right 6.");
                default:
                    {
                        int k = R(0, 3), sides = int.Parse(Shapes[k][1]);
                        p = P("Shape A is a " + Shapes[k][0] + ". Shape B has 2 more sides. How many sides does shape B have?", (sides + 2).ToString(Inv), "First: how many sides does a " + Shapes[k][0] + " have? " + sides + ".", sides + " + 2 = ?");
                        p.Visual = "shape:" + Shapes[k][0];
                        return p;
                    }
            }
        }

        // equal parts: rows and columns of squares (2.G.A.2), halves / thirds / fourths (2.G.A.3); visual "parts:<rows>,<cols>,<shaded>"
        static Problem Parts2(int lv)
        {
            int kind = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : lv == 3 ? R(1, 2) : R(1, 3);
            Problem p;
            if (kind == 0)
            {
                int r = R(2, lv <= 1 ? 3 : 4), c = R(2, lv <= 1 ? 4 : 5);
                p = P("The rectangle is cut into rows and columns of same-size squares. How many squares?", (r * c).ToString(Inv), "Count the squares in one row: " + c + ".", "There are " + r + " rows. Add " + string.Join(" + ", Enumerable.Repeat(c.ToString(Inv), r).ToArray()) + ".");
                p.Visual = "parts:" + r + "," + c + ",0";
                return p;
            }
            int d = R(2, 4); string[] names = { "", "", "half", "third", "fourth" }; string[] plural = { "", "", "halves", "thirds", "fourths" };
            if (kind == 1)
            {
                p = Choice("The shape is cut into equal parts. What is one part called?", "1 " + names[d], new[] { "1 half", "1 third", "1 fourth" }.Where(x => x != "1 " + names[d]),
                    "Count the equal parts.", "2 parts: halves. 3 parts: thirds. 4 parts: fourths.");
                p.Visual = "parts:1," + d + ",1";
                return p;
            }
            if (kind == 2)
            {
                int sh = R(1, d - 1);
                string ans = sh + " " + (sh == 1 ? names[d] : plural[d]);
                var wrong = new List<string>();
                for (int dd = 2; dd <= 4; dd++) for (int s2 = 1; s2 < dd; s2++) wrong.Add(s2 + " " + (s2 == 1 ? names[dd] : plural[dd]));
                p = Choice("How much of the shape is colored?", ans, wrong.Where(x => x != ans).OrderBy(x => R(0, 99)),
                    "How many equal parts are there? " + d + ", so each part is 1 " + names[d] + ".", "Count the colored parts.");
                p.Visual = "parts:1," + d + "," + sh;
                return p;
            }
            p = P("How many " + plural[d] + " make one whole?", d.ToString(Inv), "Cut a whole into equal " + plural[d] + ": how many pieces?", "Two halves, three thirds, four fourths.");
            W(p, "One " + names[d] + " is one of " + d + " equal parts of a whole.", "Count the parts that fill the whole: " + string.Join(" + ", Enumerable.Repeat("1/" + d, d).ToArray()) + " = 1, that's " + d + " " + plural[d] + ".");
            p.Visual = "parts:1," + d + "," + d;
            return p;
        }

        // ================================================================ 6th grade: long division
        /// <summary>Numbers for a long division at a level: 1 = 2-3 digits by 1 digit, exact; 2 = 3-4 digits by 1 digit,
        /// remainders; 3 = 3-4 digits by 2 digits; 4 = 4 digits by 2 digits, sometimes a 0 in the quotient; 5 = 4-5 digits by
        /// 2 digits. Decimal = the answer ends within two decimal places.</summary>
        public static void LongDivisionNumbers(int lv, bool exact, bool decimals, out long dividend, out int divisor)
        {
            lv = Math.Max(1, Math.Min(5, lv));
            for (int tries = 0; ; tries++)
            {
                switch (lv)
                {
                    case 1: divisor = R(3, 9); dividend = R(20, 999); break;
                    case 2: divisor = R(3, 9); dividend = R(100, 9999); break;
                    case 3: divisor = R(11, 49); dividend = R(300, 4999); break;
                    case 4: divisor = R(12, 99); dividend = R(1000, 9999); break;
                    default: divisor = R(12, 99); dividend = R(2000, 99999); break;
                }
                if (dividend < divisor * 2) continue;
                long r = dividend % divisor;
                if (lv == 1 || exact) { dividend -= r; if (dividend < divisor * 2) continue; r = 0; }
                if (decimals)
                {
                    if (r == 0 || (r * 100) % divisor != 0) { if (tries < 4000) continue; }
                }
                else if (!exact && lv >= 2 && r == 0 && tries < 50) continue;   // remainders are the point here
                if (lv == 4 && tries < 30 && !(dividend / divisor).ToString(Inv).Contains("0") && R(0, 2) == 0) continue;   // a 0 in the answer now and then
                return;
            }
        }
        /// <summary>A long division for the Workshop: level numbers, the steps for that level, sometimes a story.</summary>
        public static LongDivision MakeLongDivision(int lv, bool story)
        {
            lv = Math.Max(1, Math.Min(5, lv));
            bool decimals = !story && lv == 5 && R(0, 1) == 0;
            long dividend; int divisor;
            LongDivisionNumbers(lv, false, decimals, out dividend, out divisor);
            if (decimals && (dividend % divisor) * 100 % divisor != 0) decimals = false;   // only answers that end within two places
            if (story && dividend % divisor == 0) dividend += R(1, divisor - 1);
            LongDivision d = LongDivision.For(dividend, divisor, decimals ? 2 : 0, lv >= 2, true, lv >= 3 && !story, lv >= 4 && !story && !decimals);
            d.Level = lv;
            if (story)
            {
                string s, q, rule, unit;
                StoryFor(dividend, divisor, out s, out q, out rule, out unit);
                d.AddStory(s, q, rule, unit, LongDivision.StoryAnswer(dividend, divisor, rule));
            }
            return d;
        }
        /// <summary>A science story for a division with a remainder, and which way the remainder matters.</summary>
        static void StoryFor(long n, int d, out string story, out string question, out string rule, out string unit)
        {
            string who = Pick(Kids6);
            rule = Pick("up", "drop", "use");
            // only stories where the amount is realistic: a class doesn't collect 50,000 pebbles or rock samples
            var fits = new List<int> { 2, 3 };
            if (n <= 20000) { fits.Add(0); fits.Add(5); }
            if (n <= 3000) { fits.Add(1); fits.Add(4); }
            int ctx = Pick(fits.ToArray());
            switch (ctx)
            {
                case 0:
                    {
                        unit = "mL";
                        string tube = d <= 40 ? "test tube" : "beaker";   // test tubes hold up to about 40 mL
                        story = "The science lab has " + Comma(n) + " mL of distilled water. Each " + tube + " holds " + d + " mL.";
                        question = rule == "up" ? "How many " + tube + "s are needed to hold all of the water?" : rule == "drop" ? "How many " + tube + "s can be filled all the way to the top?" : "After filling as many " + tube + "s as possible to the top, how many mL of water are left?";
                        break;
                    }
                case 1:
                    unit = "pebbles";
                    story = who + "'s class is building terrariums. They have " + Comma(n) + " pebbles for the drainage layers, and each terrarium gets " + d + " pebbles.";
                    question = rule == "up" ? "Every pebble has to go into a terrarium (the last one can have fewer). How many terrariums do they need?" : rule == "drop" ? "How many terrariums can get a full layer of " + d + " pebbles?" : "After every full layer, how many pebbles are left over?";
                    break;
                case 2:
                    unit = "chunks of ore";
                    story = "In a space mining game, " + who + "'s rover dug up " + Comma(n) + " chunks of iron ore. The smelter turns " + d + " chunks into 1 iron bar.";
                    question = rule == "up" ? "The smelter only takes full loads of " + d + " chunks, so " + who + " will mine more to fill the last load. How many loads will that be?" : rule == "drop" ? "How many iron bars can " + who + " make right now?" : "How many chunks of ore are left after making as many bars as possible?";
                    break;
                case 3:
                    unit = "pellets";
                    story = "Nuclear fuel is made of small pellets stacked inside long metal tubes called fuel rods. In this practice problem, a team has " + Comma(n) + " pellets and each rod holds " + d + ".";
                    question = rule == "up" ? "Every pellet must go into a rod (the last rod may not be full). How many rods are needed?" : rule == "drop" ? "How many rods can be filled completely?" : "After filling as many rods as possible, how many pellets are left?";
                    break;
                case 4:
                    unit = "rock samples";
                    story = "The geology club collected " + Comma(n) + " rock samples. A display box holds " + d + " samples.";
                    question = rule == "up" ? "How many boxes do they need to put away every sample?" : rule == "drop" ? "How many boxes can they fill completely?" : "If they fill as many boxes as they can, how many samples are left out?";
                    break;
                default:
                    unit = "seeds";
                    story = who + " has " + Comma(n) + " seeds for the class garden. Each seed packet holds " + d + " seeds.";
                    question = rule == "up" ? "Every seed must go in a packet (the last packet can have fewer). How many packets are needed?" : rule == "drop" ? "How many packets can be filled all the way?" : "How many seeds are left after filling as many full packets as possible?";
                    break;
            }
        }

        // quick long division: "4567 \u00F7 34 = ?" typed as "134 R 11"
        static Problem LongDivP(int lv)
        {
            long n; int d;
            LongDivisionNumbers(lv, false, false, out n, out d);
            long q = n / d, r = n % d;
            string ans = r == 0 ? q.ToString(Inv) : q + " R " + r;
            long en, ed, eq; LongDivision.Estimate(n, d, out en, out ed, out eq);
            Problem p = P(Comma(n) + DV + d + " = ?", ans,
                "Estimate: " + n + " is close to " + en + " and " + d + (ed != d ? " is close to " + ed : "") + ". " + en + DV + ed + " = " + eq + ", so the answer is near " + eq + ".",
                "Divide, Multiply, Subtract, Bring down (Dad, Mom, Sister, Brother), one digit at a time.",
                "Type the remainder after an R, like 134 R 11. Check: answer" + TX + d + " + remainder should be " + n + ".");
            p.Kind = "qr";
            p.Visual = "bracket:" + n + "," + d;
            var ch = new List<string> { ans };
            Action<string> add = c => { if (!ch.Contains(c) && ch.Count < 4) ch.Add(c); };
            if (r > 0)
            {
                add((q + 1) + " R " + r);
                add(q + " R " + (r + 1 < d ? r + 1 : r - 1));
                add((q - 1) + " R " + (r + d));          // the remainder is too big: another one fits
                add((q * 10) + " R " + r);               // a place-value slip
            }
            else
            {
                add((q + 1).ToString(Inv)); add((q - 1).ToString(Inv)); add((q * 10).ToString(Inv)); add(q + " R 1");
            }
            p.Choices = ch.OrderBy(x => R(0, 1000)).ToArray();
            var ls = Worked.LongDiv(n, d);
            ls.Insert(0, "Estimate first: " + en + DV + ed + " = " + eq + ", so the answer is near " + eq + ".");
            return W(p, ls);
        }

        // division stories where the remainder matters: round up, drop it, or it is the answer
        static Problem DivWord(int lv)
        {
            long n; int d;
            LongDivisionNumbers(Math.Max(2, lv), false, false, out n, out d);
            if (n % d == 0) n += R(1, d - 1);
            string s, q, rule, unit;
            StoryFor(n, d, out s, out q, out rule, out unit);
            string ans = LongDivision.StoryAnswer(n, d, rule);
            long qq = n / d, rr = n % d;
            Problem p = P(s + " " + q, ans,
                "First divide: " + Comma(n) + DV + d + " = " + qq + " R " + rr + ".",
                rule == "up" ? "The leftover " + rr + " still needs a place, so you need one more than " + qq + "." : rule == "drop" ? "The leftover " + rr + " is not enough for a full one, so it doesn't count." : "The question is about what is left over: the remainder.",
                "Does your answer make sense in the story?");
            p.Choices = NumChoices(long.Parse(ans, Inv), qq, qq + 1, rr, qq - 1);
            return p;
        }

        // ================================================================ fractions
        static Problem FracEquiv(int lv)
        {
            int d = R(2, lv <= 2 ? 6 : 10), n = R(1, d - 1); int g0 = Answers.Gcd(n, d); n /= g0; d /= g0;
            if (d == 1) { n = 1; d = 2; }
            int k = R(2, lv <= 2 ? 4 : 6);
            Problem p;
            switch (lv)
            {
                case 1: p = P(n + "/" + d + " = ?/" + (d * k), (n * k).ToString(Inv), "The bottom went from " + d + " to " + (d * k) + ": that's" + TX + k + ".", "Do the same to the top: " + n + TX + k + " = ?"); p.Visual = "fracbars:" + n + "/" + d + "," + (n * k) + "/" + (d * k);
                    return W(p, "Equal fractions: multiply the top and the bottom by the same number.", "The bottom went from " + d + " to " + (d * k) + ": " + (d * k) + DV + d + " = " + k + ".", "Do the same to the top: " + n + TX + k + " = " + (n * k) + ".");
                case 2: p = P(n + "/" + d + " = " + (n * k) + "/?", (d * k).ToString(Inv), "The top went from " + n + " to " + (n * k) + ": that's" + TX + k + ".", "Do the same to the bottom: " + d + TX + k + " = ?"); p.Visual = "fracbars:" + n + "/" + d + "," + (n * k) + "/" + (d * k);
                    return W(p, "Equal fractions: multiply the top and the bottom by the same number.", "The top went from " + n + " to " + (n * k) + ": " + (n * k) + DV + n + " = " + k + ".", "Do the same to the bottom: " + d + TX + k + " = " + (d * k) + ".");
                case 3:
                case 4:
                    {
                        int N = n * k, D = d * k, G = Answers.Gcd(N, D);
                        p = P("Simplify " + N + "/" + D + " (lowest terms).", Fr(N, D), "Find the greatest number that divides both " + N + " and " + D + ": it's " + G + ".", "Divide the top and the bottom by " + G + ".");
                        p.Kind = "simplest";
                        return W(p, "Find the greatest number that divides both " + N + " and " + D + ": it's " + G + ".", "Divide the top: " + N + DV + G + " = " + (N / G) + ".", "Divide the bottom: " + D + DV + G + " = " + (D / G) + ".",
                            "Nothing else divides both " + (N / G) + " and " + (D / G) + " (except 1), so " + Fr(N, D) + " is in lowest terms.");
                    }
                default:
                    {
                        string right = (n * k) + "/" + (d * k);
                        var wrong = new[] { (n + k) + "/" + (d + k), (n * k) + "/" + (d + k), (n + 1) + "/" + (d * k), d + "/" + n };
                        return W(Choice("Which fraction is equal to " + n + "/" + d + "?", right, wrong.Where(w => !Answers.Same(w, right, "number")), "Equal fractions: multiply the top and the bottom by the same number.", "Adding the same number to both does not keep them equal."),
                            "Equal fractions: multiply the top and the bottom by the same number. Adding the same number to both does not keep them equal.",
                            "Multiply " + n + "/" + d + " by " + k + " on top and bottom: " + n + TX + k + " = " + (n * k) + " and " + d + TX + k + " = " + (d * k) + ".", "That gives " + right + ".");
                    }
            }
        }
        static Problem FracCompare(int lv)
        {
            int a, b, c, d;
            switch (lv)
            {
                case 1: b = d = R(3, 12); a = R(1, b - 1); c = R(1, d - 1); break;
                case 2: a = c = R(1, 4); b = R(a + 1, 10); d = R(c + 1, 10); break;
                default: b = R(2, 9); d = R(2, 12); a = R(1, b - 1 + (lv >= 5 ? b : 0)); c = R(1, d - 1 + (lv >= 5 ? d : 0)); if (lv >= 4 && R(0, 3) == 0) { int k = R(2, 4); c = a * k; d = b * k; } break;
            }
            long lhs = (long)a * d, rhs = (long)c * b;
            string ans = lhs > rhs ? ">" : lhs < rhs ? "<" : "=";
            int L = Lcm(b, d);
            Problem p = P(a + "/" + b + "  ?  " + c + "/" + d, ans,
                b == d ? "Same-size pieces (the bottoms match): compare the tops." : a == c ? "Same number of pieces: the smaller the pieces (bigger bottom), the smaller the fraction." : "Make the bottoms the same: " + L + " works.",
                b == d ? "More pieces of the same size is more." : a + "/" + b + " = " + (a * L / b) + "/" + L + " and " + c + "/" + d + " = " + (c * L / d) + "/" + L + ".",
                "The open side of > or < faces the bigger number.");
            p.Kind = "choice"; p.Choices = new[] { "<", "=", ">" };
            if (lv <= 3 && a <= b && c <= d) p.Visual = "fracbars:" + a + "/" + b + "," + c + "/" + d;
            string word = ans == ">" ? " is bigger than " : ans == "<" ? " is smaller than " : " is equal to ";
            if (b == d)
                return W(p, "The bottoms match (" + b + "), so the pieces are the same size: compare the tops.", a + word + c + ", so " + a + "/" + b + " " + ans + " " + c + "/" + d + ".");
            int A = a * (L / b), C = c * (L / d);
            return W(p, "Make the bottoms the same. " + L + " is a multiple of both " + b + " and " + d + ".",
                a + "/" + b + ": " + L + DV + b + " = " + (L / b) + ", so multiply the top too: " + a + TX + (L / b) + " = " + A + ". That's " + A + "/" + L + ".",
                c + "/" + d + ": " + L + DV + d + " = " + (L / d) + ", so multiply the top too: " + c + TX + (L / d) + " = " + C + ". That's " + C + "/" + L + ".",
                "Now compare the tops: " + A + word + C + ", so " + a + "/" + b + " " + ans + " " + c + "/" + d + ".");
        }
        static Problem FracAdd(int lv)
        {
            int b, d;
            switch (lv)
            {
                case 1: b = d = R(3, 12); break;
                case 2: b = R(2, 5); d = b * R(2, 3); break;
                default: do { b = R(2, 8); d = R(2, 9); } while (b == d); break;
            }
            int a = R(1, b - 1), c = R(1, d - 1);
            bool sub = lv >= 4 ? R(0, 2) != 0 : lv >= 2 && R(0, 2) == 0;
            if (sub && (long)a * d < (long)c * b) { int t = a; a = c; c = t; t = b; b = d; d = t; }
            if (sub && (long)a * d == (long)c * b) return FracAdd(lv);
            int L = Lcm(b, d);
            int num = sub ? a * (L / b) - c * (L / d) : a * (L / b) + c * (L / d);
            Problem p = P(a + "/" + b + (sub ? MN : " + ") + c + "/" + d + " = ?", Fr(num, L),
                b == d ? "The bottoms are the same: " + (sub ? "subtract" : "add") + " the tops and keep the bottom." : "Make the bottoms the same. " + L + " is a multiple of both " + b + " and " + d + ".",
                b == d ? a + (sub ? MN : " + ") + c + " = " + num + ", so " + num + "/" + L + "." : a + "/" + b + " = " + (a * L / b) + "/" + L + " and " + c + "/" + d + " = " + (c * L / d) + "/" + L + ".",
                "Simplify if you can" + (num > L ? ", or write it as a mixed number." : "."));
            p.Kind = "fraction";
            var fs = new List<string>();
            int A2 = a * (L / b), C2 = c * (L / d);
            if (b == d) fs.Add("The bottoms are the same (" + b + "), so " + (sub ? "subtract" : "add") + " the tops and keep the bottom.");
            else
            {
                fs.Add("Make the bottoms the same: " + L + " is the smallest number both " + b + " and " + d + " go into.");
                fs.Add(a + "/" + b + " = " + A2 + "/" + L + " (" + a + TX + (L / b) + " = " + A2 + ") and " + c + "/" + d + " = " + C2 + "/" + L + " (" + c + TX + (L / d) + " = " + C2 + ").");
            }
            fs.Add((sub ? "Subtract" : "Add") + " the tops: " + A2 + (sub ? MN : " + ") + C2 + " = " + num + ", so " + num + "/" + L + ".");
            int gF = Answers.Gcd(num, L);
            if (gF > 1) fs.Add("Simplify: divide the top and the bottom by " + gF + ": " + num + DV + gF + " = " + (num / gF) + " and " + L + DV + gF + " = " + (L / gF) + ".");
            return W(p, fs);
        }
        static Problem FracMul(int lv)
        {
            Problem p;
            switch (lv)
            {
                case 1: { int w = R(2, 9), d = R(2, 8); p = P(w + TX + "1/" + d + " = ?", Fr(w, d), w + " pieces, each 1/" + d + " big.", "That is " + w + "/" + d + ". Simplify or make it a mixed number if you like.");
                        W(p, w + TX + "1/" + d + " means " + w + " pieces, each 1/" + d + " big.", "That's " + w + "/" + d + " (the top counts the pieces).", Answers.Gcd(w, d) > 1 ? "Simplify: divide the top and the bottom by " + Answers.Gcd(w, d) + "." : (w > d ? "You can also write it as a mixed number: " + MixedText(w, d) + "." : "")); break; }
                case 2: { int d = R(2, 6), n = R(1, d - 1), w = d * R(1, 5); p = P(n + "/" + d + TX + w + " = ?", Fr(n * w, d), "Find 1/" + d + " of " + w + " first: " + w + DV + d + " = " + (w / d) + ".", "Then " + n + " of those: " + n + TX + (w / d) + ".");
                        W(p, n + "/" + d + " of " + w + ": find 1/" + d + " of " + w + " first: " + w + DV + d + " = " + (w / d) + ".", "Then " + n + " of those: " + n + TX + (w / d) + " = " + (n * w / d) + "."); break; }
                case 5:
                    {
                        int w = R(1, 3), b = R(2, 4), a = R(1, b - 1), d = R(2, 6), c = R(1, d - 1);
                        int top = (w * b + a) * c, bot = b * d;
                        p = P(w + " " + a + "/" + b + TX + c + "/" + d + " = ?", Fr(top, bot), "Change the mixed number to a fraction first: " + w + " " + a + "/" + b + " = " + (w * b + a) + "/" + b + ".", "Multiply tops and bottoms: " + (w * b + a) + TX + c + " over " + b + TX + d + ".", "Simplify.");
                        int g5 = Answers.Gcd(top, bot);
                        W(p, "Change the mixed number to a fraction: " + w + TX + b + " = " + (w * b) + ", and " + (w * b) + " + " + a + " = " + (w * b + a) + ", so " + w + " " + a + "/" + b + " = " + (w * b + a) + "/" + b + ".",
                            "Multiply the tops: " + (w * b + a) + TX + c + " = " + top + ". Multiply the bottoms: " + b + TX + d + " = " + bot + ".",
                            g5 > 1 ? "Simplify " + top + "/" + bot + ": divide both by " + g5 + ". " + top + DV + g5 + " = " + (top / g5) + " and " + bot + DV + g5 + " = " + (bot / g5) + "." : top + "/" + bot + " is already in lowest terms.");
                        break;
                    }
                default:
                    {
                        int b = R(2, 9), a = R(1, b - 1), d = R(2, 9), c = R(1, d - 1);
                        if (lv == 4 && Answers.Gcd(a * c, b * d) == 1) { c = b; d = R(c + 1, c + 6); }   // something to simplify
                        p = P(a + "/" + b + TX + c + "/" + d + " = ?", Fr(a * c, b * d), "Multiply the tops: " + a + TX + c + " = " + (a * c) + ".", "Multiply the bottoms: " + b + TX + d + " = " + (b * d) + ".", "Simplify: " + (a * c) + "/" + (b * d) + " = " + Fr(a * c, b * d) + "?");
                        int gm = Answers.Gcd(a * c, b * d);
                        W(p, "Multiply the tops: " + a + TX + c + " = " + (a * c) + ".", "Multiply the bottoms: " + b + TX + d + " = " + (b * d) + ".",
                            gm > 1 ? "Simplify " + (a * c) + "/" + (b * d) + ": divide both by " + gm + ". " + (a * c) + DV + gm + " = " + (a * c / gm) + " and " + (b * d) + DV + gm + " = " + (b * d / gm) + "." : (a * c) + "/" + (b * d) + " is already in lowest terms.");
                        break;
                    }
            }
            p.Kind = "fraction";
            return p;
        }

        // why "multiply by the reciprocal" works: how many groups fit, with a picture (visual "fracdiv:<a/b>,<c/d>")
        static Problem FracWhy(int lv)
        {
            int kind = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : lv == 3 ? R(1, 3) : R(2, 5);
            Problem p;
            switch (kind)
            {
                case 0:
                    {
                        int w = R(2, 4), d = R(2, 4);
                        p = P("How many 1/" + d + "s fit in " + w + "?", (w * d).ToString(Inv), "Each whole has " + d + " pieces of size 1/" + d + ".", w + " wholes" + TX + d + " pieces each = ?");
                        p.Visual = "fracdiv:" + w + "/1,1/" + d;
                        return p;
                    }
                case 1:
                    {
                        // a/b \u00F7 c/d with a whole-number answer: b divides into d's pieces evenly
                        int d = Pick(4, 6, 8, 12), b = Pick(2, 3, 4, 6); if (d % b != 0) { b = 2; d = Pick(4, 6, 8); }
                        int a = R(1, b - 1), c = 1;
                        int ans = a * d / b;
                        string[] ctx = { "A terrarium path is " + a + "/" + b + " of a meter long. Each stepping stone covers 1/" + d + " of a meter. How many stones fit along the path?",
                                         "A recipe for plant food needs 1/" + d + " cup of powder per batch. You have " + a + "/" + b + " cup. How many batches can you make?",
                                         "Each vial in the lab holds 1/" + d + " liter. How many vials can you fill from " + a + "/" + b + " liter?" };
                        p = P(Pick(ctx), ans.ToString(Inv), "This is " + a + "/" + b + DV + c + "/" + d + ": how many 1/" + d + "s fit in " + a + "/" + b + "?", a + "/" + b + " = " + (a * d / b) + "/" + d + ". Count the " + d + "ths.");
                        p.Visual = "fracdiv:" + a + "/" + b + ",1/" + d;
                        return p;
                    }
                case 2:
                    {
                        int d = R(3, 5), c = R(2, d - 1), groups = R(2, 4);
                        if (Answers.Gcd(c, d) != 1) c = 1;
                        p = P("How many " + c + "/" + d + "s fit in " + Fr(groups * c, d) + "?", groups.ToString(Inv), "Write both with the same bottom: " + Fr(groups * c, d) + " is " + (groups * c) + "/" + d + ".", "How many groups of " + c + " pieces are in " + (groups * c) + " pieces? " + (groups * c) + DV + c + ".");
                        p.Visual = "fracdiv:" + (groups * c) + "/" + d + "," + c + "/" + d;
                        return p;
                    }
                case 3:
                    {
                        int d = R(2, 6);
                        return Choice("Why is " + R(2, 9) + DV + "1/" + d + " the same as multiplying by " + d + "?", "Each whole holds " + d + " pieces of size 1/" + d + ", so you count " + d + " pieces for every whole",
                            new[] { "Dividing always makes a number bigger", "You flip both numbers and divide", "Because 1/" + d + " + " + d + " is a whole number" },
                            "Think about how many 1/" + d + "s fit in one whole.", "Dividing asks: how many of these fit?");
                    }
                case 4:
                    {
                        int b = R(2, 9), a = R(1, b - 1), d = R(2, 9), c = R(1, d - 1);
                        string right = a + "/" + b + TX + d + "/" + c;
                        return Choice("Which multiplication gives the same answer as " + a + "/" + b + DV + c + "/" + d + "?", right,
                            new[] { b + "/" + a + TX + c + "/" + d, a + "/" + b + TX + c + "/" + d, b + "/" + a + TX + d + "/" + c },
                            "Keep the first fraction, change " + DV.Trim() + " to " + TX.Trim() + ", flip the second (its reciprocal).", "Only the fraction you divide BY gets flipped.");
                    }
                default:
                    {
                        int b = R(2, 6), a = R(1, b - 1), d = R(2, 8), c = R(1, d - 1);
                        string q = Fr(a * d, b * c);
                        p = P(a + "/" + b + DV + c + "/" + d + " = " + q + ". Check it: " + q + TX + c + "/" + d + " = ?", Fr(a, b), "Dividing and multiplying undo each other.", "If the division is right, multiplying back gives " + Fr(a, b) + ".");
                        p.Kind = "fraction";
                        return p;
                    }
            }
        }

        static Problem Mixed(int lv)
        {
            Problem p;
            switch (lv)
            {
                case 1:
                    {
                        int d = R(2, 8), w = R(1, 4), n = R(1, d - 1); if (Answers.Gcd(n, d) != 1) n = 1;
                        int top = w * d + n;
                        p = P(top + "/" + d + " as a mixed number = ?", w + " " + n + "/" + d, "How many whole " + d + "/" + d + "s are in " + top + "/" + d + "? " + top + DV + d + " = " + w + " R " + n + ".", "The whole number is " + w + "; the leftover " + n + " stays over " + d + ".");
                        p.Kind = "mixed";
                        p.Choices = new[] { w + " " + n + "/" + d, (w + 1) + " " + n + "/" + d, n + " " + w + "/" + d, w + " " + d + "/" + n }.Distinct().OrderBy(x => R(0, 99)).ToArray();
                        return p;
                    }
                case 2:
                    {
                        int d = R(2, 9), w = R(1, 5), n = R(1, d - 1);
                        p = P(w + " " + n + "/" + d + " = ?/" + d, (w * d + n).ToString(Inv), "Each whole is " + d + "/" + d + ". " + w + " wholes = " + (w * d) + "/" + d + ".", "Add the " + n + " more: " + (w * d) + " + " + n + ".");
                        return W(p, "Each whole is " + d + "/" + d + ", so " + w + " wholes are " + w + TX + d + " = " + (w * d) + " pieces.", "Add the " + n + " more pieces: " + (w * d) + " + " + n + " = " + (w * d + n) + ".", "So " + w + " " + n + "/" + d + " = " + (w * d + n) + "/" + d + ".");
                    }
                default:
                    {
                        int b, d;
                        if (lv <= 4) { b = d = R(3, 8); } else { do { b = R(2, 6); d = R(2, 8); } while (b == d); }
                        // w1 > w2, so the first mixed number is always the bigger one
                        int w1 = R(2, 6), w2 = R(1, w1 - 1), a = R(1, b - 1), c = R(1, d - 1);
                        bool sub = lv == 4;
                        int L = Lcm(b, d);
                        int top1 = (w1 * b + a) * (L / b), top2 = (w2 * d + c) * (L / d);
                        bool borrow = sub && a * (L / b) < c * (L / d);
                        int res = sub ? top1 - top2 : top1 + top2;
                        string ans = MixedText(res, L);
                        p = P(w1 + " " + a + "/" + b + (sub ? MN : " + ") + w2 + " " + c + "/" + d + " = ?", ans,
                            borrow ? "The first fraction part is smaller than the second, so borrow 1 whole from " + w1 + ": it becomes " + L + "/" + L + " more." : sub ? "Subtract the whole numbers, then the fractions." : "Add the whole numbers, then the fractions.",
                            b == d ? "The bottoms match (" + b + "), so work with the tops." : "Use the same bottom for both fractions: " + L + ".",
                            "If the fraction part is " + L + "/" + L + " or more, make another whole.");
                        p.Kind = "mixed";
                        var ch = new List<string> { ans };
                        foreach (int delta in new[] { 1, -1, L, -L, 2 })
                        {
                            int r2 = res + delta; if (r2 <= 0) continue;
                            string c2 = MixedText(r2, L); if (!ch.Contains(c2) && ch.Count < 4) ch.Add(c2);
                        }
                        p.Choices = ch.OrderBy(x => R(0, 99)).ToArray();
                        return p;
                    }
            }
        }

        // ================================================================ percents
        static readonly int[][] NiceFracs = { new[] { 1, 2 }, new[] { 1, 4 }, new[] { 3, 4 }, new[] { 1, 5 }, new[] { 2, 5 }, new[] { 3, 5 }, new[] { 4, 5 }, new[] { 1, 10 }, new[] { 3, 10 }, new[] { 7, 10 }, new[] { 9, 10 }, new[] { 1, 20 }, new[] { 1, 25 }, new[] { 3, 20 }, new[] { 1, 8 }, new[] { 3, 8 } };
        static Problem Fdp(int lv)
        {
            int[] f = Pick(NiceFracs.Take(lv <= 2 ? 11 : NiceFracs.Length).ToArray());
            int n = f[0], d = f[1];
            decimal dec = (decimal)n / d;
            string pct = Answers.Num((double)(dec * 100)) + "%", decS = Answers.Num((double)dec);
            Problem p;
            switch (lv)
            {
                case 1: p = P(n + "/" + d + " = ?%", pct, "Percent means out of 100.", "Make the bottom 100: " + n + "/" + d + " = ?/100." + (100 % d == 0 ? " Multiply top and bottom by " + (100 / d) + "." : ""));
                    if (100 % d == 0) return W(p, "Percent means out of 100, so make the bottom 100.", "100" + DV + d + " = " + (100 / d) + ", so multiply the top and the bottom by " + (100 / d) + ".",
                        n + TX + (100 / d) + " = " + (n * 100 / d) + ", so " + n + "/" + d + " = " + (n * 100 / d) + "/100 = " + pct + ".");
                    return W(p, "Percent means out of 100.", "A fraction bar means divide: " + n + DV + d + " = " + decS + ".", "Multiply by 100: " + decS + TX + "100 = " + Answers.Num((double)(dec * 100)) + ", so " + pct + ".");
                case 2: p = P(decS + " = ?%", pct, "Percent means hundredths.", "Multiply by 100: move the decimal point two places right.");
                    return W(p, "Percent means hundredths: multiply by 100.", decS + TX + "100 = " + Answers.Num((double)(dec * 100)) + " (the decimal point moves two places right).", "So " + decS + " = " + pct + ".");
                case 3: p = P(pct + " as a fraction in lowest terms = ?", Fr(n, d), pct + " = " + Answers.Num((double)(dec * 100)) + "/100.", "Simplify: divide the top and the bottom by the same number."); p.Kind = "simplest"; return p;
                case 4: p = P(n + "/" + d + " as a decimal = ?", decS, "A fraction bar means divide: " + n + DV + d + ".", 100 % d == 0 ? "Or make the bottom 100 first: ?/100." : "Use long division: " + n + ".000" + DV + d + ".");
                    if (100 % d == 0) return W(p, "Make the bottom 100: 100" + DV + d + " = " + (100 / d) + ", and " + n + TX + (100 / d) + " = " + (n * 100 / d) + ", so " + n + "/" + d + " = " + (n * 100 / d) + "/100.",
                        (n * 100 / d) + " hundredths is " + decS + ".", "Check: " + n + DV + d + " = " + decS + ".");
                    return W(p, "A fraction bar means divide: " + n + DV + d + ".", "Long division of " + n + ".000 by " + d + " gives " + decS + ": " + n + DV + d + " = " + decS + ".", "Check: " + decS + TX + d + " = " + n + ".");
                default:
                    {
                        // which is greatest? three forms of different numbers
                        var vals = new List<int[]>(NiceFracs.OrderBy(x => R(0, 99)).Take(3));
                        while (vals.Select(x => (double)x[0] / x[1]).Distinct().Count() < 3) vals = new List<int[]>(NiceFracs.OrderBy(x => R(0, 99)).Take(3));
                        string a1 = vals[0][0] + "/" + vals[0][1];
                        string a2 = Answers.Num((double)vals[1][0] / vals[1][1]);
                        string a3 = Answers.Num(100.0 * vals[2][0] / vals[2][1]) + "%";
                        double v1 = (double)vals[0][0] / vals[0][1], v2 = (double)vals[1][0] / vals[1][1], v3 = (double)vals[2][0] / vals[2][1];
                        string ans = v1 >= v2 && v1 >= v3 ? a1 : v2 >= v3 ? a2 : a3;
                        return Choice("Which is greatest: " + a1 + ", " + a2 + " or " + a3 + "?", ans, new[] { a1, a2, a3 }.Where(x => x != ans),
                            "Write them all the same way, for example as percents.", a1 + " = " + Answers.Num(v1 * 100) + "%, " + a2 + " = " + Answers.Num(v2 * 100) + "%.");
                    }
            }
        }
        // finding the whole (and percent as a rate per 100), with a tape diagram: visual "tape:<percent>,<part>"
        static Problem PctWhole(int lv)
        {
            int pct, whole;
            switch (lv)
            {
                case 1: pct = Pick(50, 25, 10); whole = pct == 25 ? 4 * R(2, 25) : pct == 50 ? 2 * R(3, 50) : 10 * R(2, 30); break;
                case 2: pct = 10 * R(1, 9); whole = 10 * R(2, 30); break;
                case 3: pct = 5 * R(1, 19); whole = 20 * R(1, 20); break;
                default: pct = R(2, 98); whole = 100 / Answers.Gcd(pct, 100) * R(1, 8); if (whole > 600) whole = 100 / Answers.Gcd(pct, 100); break;   // pct x whole / 100 is whole
            }
            int part = pct * whole / 100;
            if (part * 100 != pct * whole || part == 0) return PctWhole(lv);
            Problem p;
            if (lv == 4 && R(0, 1) == 0)
            {
                // a rate per 100
                int total = 100 * R(2, 9);
                string[] ctx = { "In a seed test, " + pct + " out of every 100 seeds sprouted. At that rate, how many of " + total + " seeds would sprout?",
                                 "A rock sample has " + pct + " grams of quartz per 100 grams of rock. How many grams of quartz are in " + total + " grams of rock?" };
                p = P(Pick(ctx), (pct * total / 100).ToString(Inv), pct + " per 100 is " + pct + "%.", total + " is " + (total / 100) + " hundreds, so " + pct + TX + (total / 100) + ".");
                p.Visual = "tape:" + pct + "," + pct;
                return W(p, pct + " out of every 100 is a rate per 100.", total + " is " + (total / 100) + " hundreds: " + total + DV + "100 = " + (total / 100) + ".",
                    "So multiply the " + pct + " by " + (total / 100) + ": " + pct + TX + (total / 100) + " = " + (pct * total / 100) + ".");
            }
            if (lv >= 5)
            {
                string[] ctx = { "A space miner's cargo hold is " + pct + "% full with " + part + " tons of ore. How many tons does the hold carry when it's full?",
                                 "In a pretend practice sample, " + pct + "% of a rock's mass is iron, and the iron weighs " + part + " grams. What is the mass of the whole rock in grams?",
                                 "A plant mister bottle is " + pct + "% full with " + part + " mL of water. How many mL does it hold when full?" };
                p = P(Pick(ctx), whole.ToString(Inv), pct + "% is " + part + ". Find 1% first: " + part + DV + pct + ".", "Then 100%: multiply 1% by 100. Or: " + part + DV + Answers.Num(pct / 100.0) + ".");
            }
            else
                p = P(pct + "% of a number is " + part + ". What is the number?", whole.ToString(Inv),
                    "The whole is 100%. Draw a tape split into " + (100 % pct == 0 ? (100 / pct) + " equal parts of " + pct + "% each." : "10 parts of 10% each."),
                    100 % pct == 0 ? "Each " + pct + "% part is " + part + ". There are " + (100 / pct) + " parts: " + part + TX + (100 / pct) + "." : "Find 1%: " + part + DV + pct + ", then multiply by 100.");
            p.Visual = "tape:" + pct + "," + part;
            if (100 % pct == 0)
                return W(p, "The whole is 100%. " + pct + "% fits into 100% " + (100 / pct) + " times: 100" + DV + pct + " = " + (100 / pct) + ".",
                    "Each " + pct + "% part is " + part + ", so the whole is " + (100 / pct) + " parts: " + part + TX + (100 / pct) + " = " + whole + ".",
                    "Check: " + pct + "% of " + whole + " is " + whole + TX + pct + DV + "100 = " + part + ".");
            return W(p, "The whole is 100%. Find 1% first: " + part + DV + pct + " = " + Answers.Num(whole / 100.0) + ".",
                "Then 100%: " + Answers.Num(whole / 100.0) + TX + "100 = " + whole + ".",
                "Check: " + pct + "% of " + whole + " is " + whole + TX + pct + DV + "100 = " + part + ".");
        }
        // discounts, tax and tips with exact cents
        static Problem PctMoney(int lv)
        {
            // each thing with a sensible price range in whole dollars, so a poster never costs $114
            string[] items = { "a telescope", "a terrarium kit", "a rock tumbler", "a microscope", "a model rocket", "a chemistry set", "a pair of hiking boots", "a periodic table poster" };
            int[] lowD = { 60, 20, 30, 40, 15, 20, 40, 8 }, highD = { 200, 80, 100, 150, 60, 80, 120, 25 };
            int which = R(0, items.Length - 1);
            string item = items[which];
            for (int tries = 0; ; tries++)
            {
                int pct = lv <= 1 ? Pick(10, 50) : lv == 2 ? Pick(20, 25, 50, 10, 30) : lv == 3 ? Pick(5, 6, 8, 10) : lv == 4 ? Pick(15, 18, 20) : Pick(10, 20, 25);
                long price = lv <= 2 ? 100 * R(8, 120) : lv == 3 ? 50 * R(10, 200) : lv == 4 ? 100 * R(12, 90) + Pick(0, 50) : 100 * R(20, 200);
                if (price * pct % 100 != 0) continue;
                if (lv != 4 && tries < 2000 && (price < 100L * lowD[which] || price > 100L * highD[which])) continue;
                long amt = price * pct / 100;
                Problem p;
                switch (lv)
                {
                    case 1:
                    case 2:
                        bool save = R(0, 1) == 0;
                        if (save)
                            p = P(item.Substring(0, 1).ToUpperInvariant() + item.Substring(1) + " costs " + Cents(price) + ". It is " + pct + "% off. How much do you save?", Cents(amt), "10% of " + Cents(price) + " is " + Cents(price / 10) + ".", pct + "% is " + Answers.Num(pct / 10.0) + TX + "10%.");
                        else
                            p = P(item.Substring(0, 1).ToUpperInvariant() + item.Substring(1) + " costs " + Cents(price) + ". It is " + pct + "% off. What is the sale price?", Cents(price - amt), "Find the discount first: " + pct + "% of " + Cents(price) + " = " + Cents(amt) + ".", "Subtract it from the price. Or: you pay " + (100 - pct) + "% of the price.");
                        p.Visual = "dnl:Dollars;Percent;$0,?," + Cents(price) + ";0," + (save ? pct : 100 - pct) + ",100";
                        break;
                    case 3:
                        p = P(item.Substring(0, 1).ToUpperInvariant() + item.Substring(1) + " costs " + Cents(price) + ". The sales tax is " + pct + "%. What is the total with tax?", Cents(price + amt), "Tax is " + pct + "% of " + Cents(price) + ": find 1% (" + Cents(price / 100) + (price % 100 != 0 ? " and a bit" : "") + ") and multiply by " + pct + ".", "Add the tax to the price.");
                        break;
                    case 4:
                        p = P("Lunch after the science fair costs " + Cents(price) + ". The family leaves a " + pct + "% tip. How much is the tip?", Cents(amt), "10% of " + Cents(price) + " is " + Cents(price / 10) + (price % 10 != 0 ? " (about)" : "") + ".", pct == 20 ? "20% is 2" + TX + "10%." : pct == 15 ? "15% is 10% + half of 10%." : "18% is 20% minus 2%.");
                        break;
                    default:
                        {
                            int tax = Pick(5, 8, 10);
                            long sale = price - amt;
                            if (sale * tax % 100 != 0) continue;
                            long total = sale + sale * tax / 100;
                            p = P(item.Substring(0, 1).ToUpperInvariant() + item.Substring(1) + " costs " + Cents(price) + ". It is " + pct + "% off, then " + tax + "% sales tax is added to the sale price. What do you pay?", Cents(total), "Step 1: the sale price: " + Cents(price) + MN + Cents(amt) + " = " + Cents(sale) + ".", "Step 2: " + tax + "% of " + Cents(sale) + " is " + Cents(sale * tax / 100) + ". Add it.");
                            break;
                        }
                }
                p.Kind = "money";
                string of = pct + "% of " + Cents(price) + ": " + Cents(price) + TX + pct + DV + "100 = " + Cents(amt) + ".";
                switch (lv)
                {
                    case 1:
                    case 2:
                        if (p.Answer == Cents(amt)) W(p, "Percent off is the part you save: find " + of, "You save " + Cents(amt) + ".");
                        else W(p, "First find the discount, " + of, "Take it off the price: " + Cents(price) + MN + Cents(amt) + " = " + Cents(price - amt) + ".", "Or: you pay " + (100 - pct) + "% of the price.");
                        break;
                    case 3: W(p, "The tax is " + of, "Add it to the price: " + Cents(price) + " + " + Cents(amt) + " = " + Cents(price + amt) + "."); break;
                    case 4: W(p, "The tip is " + of, pct == 20 ? "Another way: find 10% and double it." : pct == 15 ? "Another way: 10% plus half of 10%." : "Another way: 20% minus 2%."); break;
                    default:
                        {
                            int tax = int.Parse(System.Text.RegularExpressions.Regex.Match(p.Text, @"then (\d+)% sales tax").Groups[1].Value, Inv);
                            long sale = price - amt, taxAmt = sale * tax / 100;
                            W(p, "Step 1, the discount: " + of, "The sale price: " + Cents(price) + MN + Cents(amt) + " = " + Cents(sale) + ".",
                                "Step 2, the tax on the sale price: " + Cents(sale) + TX + tax + DV + "100 = " + Cents(taxAmt) + ".", "Add it: " + Cents(sale) + " + " + Cents(taxAmt) + " = " + Cents(sale + taxAmt) + ".");
                            break;
                        }
                }
                return p;
            }
        }

        // ================================================================ ratios: tables and double number lines
        static readonly string[][] RatioPairs = {
            new[] { "Seeds", "Pots" }, new[] { "Iron ore", "Iron bars" }, new[] { "Water (mL)", "Soil (cups)" }, new[] { "Moss patches", "Terrariums" },
            new[] { "Copper chunks", "Wires" }, new[] { "Drops of dye", "Beakers" }, new[] { "Miles", "Hours" }, new[] { "Pages", "Days" } };
        static Problem RatioTable(int lv)
        {
            string[] pair = Pick(RatioPairs);
            int a = R(1, 6), b = R(1, 6); if (a == b) b = a + 1; int g = Answers.Gcd(a, b); a /= g; b /= g;
            int k0 = 1;
            if (lv >= 3) { k0 = R(2, 3); a *= k0; b *= k0; }   // the table starts with a ratio that isn't in lowest terms
            var ks = new List<int> { 1, 2, 3, R(4, 6), R(7, 10) };
            if (lv >= 3) ks = new List<int> { 1, R(2, 3), R(4, 5), R(6, 8), R(9, 12) };
            ks = ks.Distinct().OrderBy(x => x).ToList();
            int missCol = R(1, ks.Count - 1), missRow = lv == 1 ? 1 : R(0, 1);
            if (lv >= 3)
            {
                // the asked-for amount is not a multiple of the first column: go through the unit rate
                int unitA = a / Answers.Gcd(a, b), unitB = b / Answers.Gcd(a, b);
                int m;
                do { m = R(2, 9); } while (m % k0 == 0);   // not a column already in the table (a or 2a)
                var top = new List<int> { a, a * 2, unitA * m };
                var bot = new List<int> { b, b * 2, unitB * m };
                bool askTop = R(0, 1) == 0;
                string ans = (askTop ? top[2] : bot[2]).ToString(Inv);
                if (lv == 4 || lv == 5)
                {
                    // a double number line
                    var tl = new List<string> { "0", a.ToString(Inv), (2 * a).ToString(Inv), askTop ? "?" : (unitA * m).ToString(Inv) };
                    var bl = new List<string> { "0", b.ToString(Inv), (2 * b).ToString(Inv), askTop ? (unitB * m).ToString(Inv) : "?" };
                    Problem q = P("The double number line shows " + pair[0].ToLowerInvariant() + " and " + pair[1].ToLowerInvariant() + " in the same ratio. What number goes where the ? is?", ans,
                        "Find the unit rate: " + a + " : " + b + " is the same as " + unitA + " : " + unitB + ".",
                        askTop ? (unitB * m) + " is " + unitB + TX + m + ", so the top is " + unitA + TX + m + "." : (unitA * m) + " is " + unitA + TX + m + ", so the bottom is " + unitB + TX + m + ".");
                    q.Visual = "dnl:" + pair[0] + ";" + pair[1] + ";" + string.Join(",", tl.ToArray()) + ";" + string.Join(",", bl.ToArray());
                    return q;
                }
                var t1 = top.Select(x => x.ToString(Inv)).ToArray(); var b1 = bot.Select(x => x.ToString(Inv)).ToArray();
                if (askTop) t1[2] = "?"; else b1[2] = "?";
                Problem p3 = P("Fill in the ratio table: what number goes where the ? is?", ans,
                    "Simplify the first column: " + a + " : " + b + " = " + unitA + " : " + unitB + ".",
                    askTop ? (unitB * m) + DV + unitB + " = " + m + ", so multiply " + unitA + " by " + m + "." : (unitA * m) + DV + unitA + " = " + m + ", so multiply " + unitB + " by " + m + ".");
                p3.Visual = "rtable:" + pair[0] + ";" + pair[1] + ";" + string.Join(",", t1) + ";" + string.Join(",", b1);
                return p3;
            }
            var topS = ks.Select(k => (a * k).ToString(Inv)).ToArray(); var botS = ks.Select(k => (b * k).ToString(Inv)).ToArray();
            string answer = missRow == 0 ? topS[missCol] : botS[missCol];
            if (missRow == 0) topS[missCol] = "?"; else botS[missCol] = "?";
            Problem p = P("Fill in the ratio table: what number goes where the ? is?", answer,
                "Every column has the same ratio, " + a + " : " + b + ".",
                "That column is " + ks[missCol] + " times the first column: " + (missRow == 0 ? a : b) + TX + ks[missCol] + " = ?");
            p.Visual = "rtable:" + pair[0] + ";" + pair[1] + ";" + string.Join(",", topS) + ";" + string.Join(",", botS);
            return p;
        }

        // ================================================================ expressions, equations
        static Problem Distrib(int lv)
        {
            switch (lv)
            {
                case 1: { int a = R(2, 9), b = R(2, 9), c = R(2, 9); return P(a + TX + "(" + b + " + " + c + ") = " + a + TX + b + " + " + a + TX + "?", c.ToString(Inv), "The " + a + " multiplies everything inside the parentheses.", a + TX + b + " and " + a + TX + c + "."); }
                case 2: { int a = R(2, 9), b = R(2, 12), c = R(2, 12); return W(P(a + "(" + b + " + " + c + ") = ?", (a * (b + c)).ToString(Inv), "Either add inside first: " + b + " + " + c + " = " + (b + c) + ", then" + TX + a + ".", "Or distribute: " + a + TX + b + " + " + a + TX + c + " = " + (a * b) + " + " + (a * c) + "."),
                    "Distribute: the " + a + " multiplies each number inside the parentheses.", a + TX + b + " = " + (a * b) + " and " + a + TX + c + " = " + (a * c) + ".", "Add them: " + (a * b) + " + " + (a * c) + " = " + (a * (b + c)) + ".",
                    "Check by adding inside first: " + b + " + " + c + " = " + (b + c) + ", and " + (b + c) + TX + a + " = " + (a * (b + c)) + "."); }
                case 3:
                    {
                        int g = R(2, 12), x = R(1, 6), y = R(1, 7); while (Answers.Gcd(x, y) != 1 || x == y) { x = R(1, 6); y = R(2, 7); }
                        return P(g * x + " + " + g * y + " = ?(" + x + " + " + y + ")", g.ToString(Inv), "Find the greatest common factor of " + (g * x) + " and " + (g * y) + ".", (g * x) + " = " + g + TX + x + " and " + (g * y) + " = " + g + TX + y + ".");
                    }
                case 4: { int a = R(2, 9), b = R(2, 12); return P(a + "(x + " + b + ") = " + a + "x + ?", (a * b).ToString(Inv), "Multiply " + a + " by each part inside: " + a + TX + "x and " + a + TX + b + ".", a + TX + b + " = ?"); }
                default:
                    {
                        int a = R(2, 6), b = R(2, 5), c = R(1, 9);
                        string right = (a * b) + "x + " + (a * c);
                        return Choice("Which is the same as " + a + "(" + b + "x + " + c + ")?", right, new[] { (a * b) + "x + " + c, (a + b) + "x + " + (a + c), b + "x + " + (a * c) },
                            "Multiply " + a + " by BOTH parts: " + a + TX + b + "x and " + a + TX + c + ".", "Forgetting the second part is the most common slip.");
                    }
            }
        }
        // the coordinate plane: visual "grid:x,y[;x2,y2]"
        static string Pt(int x, int y) { return "(" + x + ", " + y + ")"; }
        static Problem Coords(int lv)
        {
            int x = R(1, 8), y = R(1, 8);
            if (lv >= 2) { if (R(0, 1) == 0) x = -x; if (R(0, 1) == 0) y = -y; }
            Problem p;
            switch (lv <= 2 ? 0 : lv == 3 ? R(0, 1) : lv == 4 ? R(1, 2) : R(2, 3))
            {
                case 0:
                    p = Choice("What are the coordinates of the point?", Pt(x, y), new[] { Pt(y, x), Pt(-x, y), Pt(x, -y), Pt(-y, -x) }.Where(c => c != Pt(x, y)),
                        "Start at the origin (0, 0). How far right (+) or left (\u2212)? That's x, written first.", "Then how far up (+) or down (\u2212)? That's y, written second.");
                    p.Visual = "grid:" + x + "," + y;
                    return p;
                case 1:
                    {
                        string q = x > 0 ? (y > 0 ? "I" : "IV") : (y > 0 ? "II" : "III");
                        p = Choice("Which quadrant is the point " + Pt(x, y) + " in?", "Quadrant " + q, new[] { "Quadrant I", "Quadrant II", "Quadrant III", "Quadrant IV" }.Where(c => c != "Quadrant " + q),
                            "Quadrant I is top right; then go counterclockwise: II top left, III bottom left, IV bottom right.", "x " + (x > 0 ? "positive (right)" : "negative (left)") + ", y " + (y > 0 ? "positive (up)" : "negative (down)") + ".");
                        p.Visual = "grid:" + x + "," + y;
                        return p;
                    }
                case 2:
                    {
                        bool sameY = R(0, 1) == 0; int x2 = R(-8, 8), y2 = R(-8, 8);
                        if (sameY) { y2 = y; if (x2 == x) x2 = -x; } else { x2 = x; if (y2 == y) y2 = -y; }
                        int dist = sameY ? Math.Abs(x2 - x) : Math.Abs(y2 - y);
                        p = P("How far apart are " + Pt(x, y) + " and " + Pt(x2, y2) + "?", dist.ToString(Inv),
                            sameY ? "They have the same y, so they're on a flat line. Look at the x's: " + x + " and " + x2 + "." : "They have the same x, so they're on an up-and-down line. Look at the y's: " + y + " and " + y2 + ".",
                            (Math.Sign(sameY ? x : y) != Math.Sign(sameY ? x2 : y2) && (sameY ? x * x2 : y * y2) != 0) ? "They're on opposite sides of 0: add the distances to 0." : "Same side of 0: subtract the distances.");
                        p.Visual = "grid:" + x + "," + y + ";" + x2 + "," + y2;
                        return p;
                    }
                default:
                    {
                        bool overX = R(0, 1) == 0;
                        string ans = overX ? Pt(x, -y) : Pt(-x, y);
                        p = Choice("Reflect " + Pt(x, y) + " over the " + (overX ? "x-axis" : "y-axis") + ". Where does it land?", ans, new[] { overX ? Pt(-x, y) : Pt(x, -y), Pt(-x, -y), Pt(y, x) }.Where(c => c != ans),
                            overX ? "Over the x-axis: flip up and down. x stays the same, y changes sign." : "Over the y-axis: flip left and right. y stays the same, x changes sign.", "It is the same distance from the axis, on the other side.");
                        p.Visual = "grid:" + x + "," + y;
                        return p;
                    }
            }
        }
        static Problem WriteExpr(int lv)
        {
            string v = Pick("n", "x", "m", "t", "w");
            int a = R(2, 12), b = R(2, 9);
            int kind = lv <= 1 ? R(0, 2) : lv == 2 ? R(0, 4) : lv == 3 ? R(3, 6) : lv == 4 ? R(5, 7) : R(6, 8);
            switch (kind)
            {
                case 0: return Choice("Which expression means \"" + a + " more than " + v + "\"?", v + " + " + a, new[] { v + MN + a, a + v, a + MN + v }, "\"More than\" means add.", "Adding works in any order, so " + a + " + " + v + " is the same.");
                case 1: return Choice("Which expression means \"" + a + " less than " + v + "\"?", v + MN + a, new[] { a + MN + v, v + " + " + a, a + v }, "\"Less than\" means subtract.", "Careful: " + a + " less than " + v + " starts with " + v + ", then takes " + a + " away.");
                case 2: return Choice("Which expression means \"" + a + " times " + v + "\"?", a + v, new[] { v + " + " + a, a + " + " + v, v + DV.Trim() + " " + a }, "A number next to a letter means multiply: " + a + v + " = " + a + TX + v + ".", "Times means multiply.");
                case 3: return Choice("Which expression means \"" + v + " divided by " + b + "\"?", v + DV + b, new[] { b + DV + v, b + v, v + MN + b }, "Divided by: the first thing named is split up.", v + " comes first.");
                case 4: return Choice("Which expression means \"" + b + " times the sum of " + v + " and " + a + "\"?", b + "(" + v + " + " + a + ")", new[] { b + v + " + " + a, b + " + " + v + " + " + a, v + "(" + b + " + " + a + ")" }, "\"The sum of\" makes a group: put it in parentheses.", "Then multiply the whole group by " + b + ".");
                case 5: return P("How many terms are in " + a + v + " + " + b + "y + " + (a + b) + "?", "3", "Terms are the parts joined by + or \u2212.", "Count them: " + a + v + ", " + b + "y, and " + (a + b) + ".");
                case 6: return P("What is the coefficient of " + v + " in " + a + v + " + " + b + "?", a.ToString(Inv), "The coefficient is the number multiplying the letter.", "In " + a + v + ", the number in front is " + a + ".");
                case 7:
                    {
                        string who = Pick(Kids6);
                        return Choice(who + " has " + v + " plants. Each plant needs " + b + " cups of soil, and the terrarium needs " + a + " more cups for a hill. Which expression shows all the soil?", b + v + " + " + a, new[] { a + v + " + " + b, b + " + " + v + " + " + a, b + "(" + v + MN + a + ")" },
                            b + " cups for each plant: " + b + TX + v + " = " + b + v + ".", "Then add the " + a + " cups for the hill.");
                    }
                default:
                    {
                        int depth = R(5, 20);
                        return Choice("A mining rover is at the surface (0 m). It digs down " + depth + " meters every hour. Which expression gives its height after h hours?", "\u2212" + depth + "h", new[] { depth + "h", "h" + MN + depth, depth + MN + "h" },
                            "Going down means negative numbers.", "Every hour adds another \u2212" + depth + ", so h hours give \u2212" + depth + TX + "h.");
                    }
            }
        }
        static Problem Inequal(int lv)
        {
            string gt = ">", lt = "<", ge = "\u2265", le = "\u2264";
            int k = R(2, 15);
            switch (lv)
            {
                case 1:
                    {
                        int t = R(k - 4, k + 4); string op = Pick(gt, lt);
                        bool yes = op == gt ? t > k : t < k;
                        return Choice("Is " + t + " a solution of x " + op + " " + k + "?", yes ? "yes" : "no", new[] { yes ? "no" : "yes" }, "Put " + t + " in place of x: is " + t + " " + op + " " + k + " true?", op == gt ? "x > " + k + " means bigger than " + k + " (not equal)." : "x < " + k + " means smaller than " + k + " (not equal).");
                    }
                case 2:
                    {
                        string op = Pick(gt, lt, ge, le);
                        int good = op == gt ? k + R(1, 5) : op == lt ? k - R(1, 5) : op == ge ? k : k;
                        var wrong = new List<string>();
                        foreach (int c in new[] { k - 3, k - 1, k + 1, k + 3, k })
                        {
                            bool ok = op == gt ? c > k : op == lt ? c < k : op == ge ? c >= k : c <= k;
                            if (!ok && c != good) wrong.Add(c.ToString(Inv));
                        }
                        return Choice("Which number is a solution of x " + op + " " + k + "?", good.ToString(Inv), wrong.Take(3), "Try each number in place of x.", op == ge || op == le ? "The line under the sign means \"or equal to\", so " + k + " itself works." : "The sign has no line under it, so " + k + " itself is not a solution.");
                    }
                case 3:
                    {
                        string[][] s = {
                            new[] { "The temperature t in the lab freezer is below 0 \u00B0C.", "t < 0", "t > 0", "t \u2264 0", "t \u2265 0" },
                            new[] { "At least " + k + " seeds s must sprout for the terrarium to look full.", "s \u2265 " + k, "s > " + k, "s \u2264 " + k, "s < " + k },
                            new[] { "The rover can carry at most " + (k * 10) + " kg of ore, c.", "c \u2264 " + (k * 10), "c < " + (k * 10), "c \u2265 " + (k * 10), "c > " + (k * 10) },
                            new[] { "A cave is deeper than " + (k * 5) + " m below the surface, so its height h is less than \u2212" + (k * 5) + ".", "h < \u2212" + (k * 5), "h > \u2212" + (k * 5), "h \u2264 \u2212" + (k * 5), "h > " + (k * 5) },
                        };
                        string[] row = Pick(s);
                        return Choice(row[0] + " Which inequality fits?", row[1], row.Skip(2), "\"At least\" means \u2265, \"at most\" means \u2264.", "\"Below\" or \"less than\" means <; \"more than\" means >.");
                    }
                case 4:
                    {
                        int a = R(2, 9), t = R(1, 12); string op = Pick(gt, lt);
                        bool yes = op == gt ? t + a > k + a : t + a < k + a;
                        int rhs = k + a;
                        return Choice("Is x = " + t + " a solution of x + " + a + " " + op + " " + rhs + "?", yes ? "yes" : "no", new[] { yes ? "no" : "yes" },
                            "Put " + t + " in for x: " + t + " + " + a + " = " + (t + a) + ".", "Is " + (t + a) + " " + op + " " + rhs + " true?");
                    }
                default:
                    {
                        string op = Pick(gt, lt, ge, le);
                        bool closed = op == ge || op == le, right = op == gt || op == ge;
                        Problem p = Choice("Which inequality does the number line show?", "x " + op + " " + k, new[] { gt, lt, ge, le }.Where(o => o != op).Select(o => "x " + o + " " + k),
                            "An open circle means the number itself is not included (< or >). A filled circle means it is (\u2264 or \u2265).", "The arrow points to the numbers that work: right is bigger, left is smaller.");
                        p.Visual = "ineq:" + k + "," + (closed ? "closed" : "open") + "," + (right ? "right" : "left");
                        return p;
                    }
            }
        }

        // ================================================================ geometry: polygons and nets
        static Problem PolyArea(int lv)
        {
            int kind = lv <= 1 ? 0 : lv == 2 ? R(0, 1) : lv == 3 ? R(1, 2) : R(1, 3);
            Problem p;
            switch (kind)
            {
                case 0:
                    {
                        // an L shape: a w1 x h1 rectangle with a w2 x h2 rectangle on its right, sharing the bottom
                        int w1 = R(2, 6), h1 = R(4, 9), w2 = R(2, 6), h2 = R(1, h1 - 1);
                        int a = w1 * h1 + w2 * h2;
                        p = P("Find the area of the L-shaped floor.", a.ToString(Inv), "Cut it into two rectangles.", "Left part: " + w1 + TX + h1 + " = " + (w1 * h1) + ". Right part: " + w2 + TX + h2 + " = " + (w2 * h2) + ". Add them.");
                        p.Visual = "lshape:" + w1 + "," + h1 + "," + w2 + "," + h2;
                        W(p, "Cut the L into two rectangles.", "Left part: " + w1 + TX + h1 + " = " + (w1 * h1) + ".", "Right part: " + w2 + TX + h2 + " = " + (w2 * h2) + ".", "Add them: " + (w1 * h1) + " + " + (w2 * h2) + " = " + a + ".");
                        break;
                    }
                case 1:
                    {
                        // a house shape: a rectangle with a triangle roof
                        int w = 2 * R(2, 6), h = R(3, 8), rh = R(2, 6);
                        int a = w * h + w * rh / 2;
                        p = P("Find the area of the barn wall (a rectangle with a triangle on top).", a.ToString(Inv), "Rectangle: " + w + TX + h + " = " + (w * h) + ".", "Triangle: base" + TX + "height" + DV + "2 = " + w + TX + rh + DV + "2 = " + (w * rh / 2) + ". Add them.");
                        p.Visual = "house:" + w + "," + h + "," + rh;
                        W(p, "Cut it into a rectangle and a triangle.", "Rectangle: " + w + TX + h + " = " + (w * h) + ".", "Triangle: base times height, then half: " + w + TX + rh + " = " + (w * rh) + ", and " + (w * rh) + DV + "2 = " + (w * rh / 2) + ".", "Add them: " + (w * h) + " + " + (w * rh / 2) + " = " + a + ".");
                        break;
                    }
                case 2:
                    {
                        // a trapezoid split into a rectangle and two triangles (or the formula)
                        int b1 = R(3, 9), extra = 2 * R(1, 3), h = R(2, 8);
                        int b2 = b1 + extra;
                        int a = (b1 + b2) * h / 2;
                        p = P("Find the area of the trapezoid: top " + b1 + ", bottom " + b2 + ", height " + h + ".", a.ToString(Inv), "Cut it into a rectangle (" + b1 + TX + h + ") and two triangles on the sides.", "Each side triangle has base " + (extra / 2) + " and height " + h + ". Or: (top + bottom)" + DV + "2" + TX + "height.");
                        p.Visual = "trap:" + b1 + "," + b2 + "," + h;
                        W(p, "A trapezoid's area is the average of the two parallel sides times the height.", "Add the top and the bottom: " + b1 + " + " + b2 + " = " + (b1 + b2) + ".", "Half of that: " + (b1 + b2) + DV + "2 = " + Answers.Num((b1 + b2) / 2.0) + ".", "Times the height: " + Answers.Num((b1 + b2) / 2.0) + TX + h + " = " + a + ".");
                        break;
                    }
                default:
                    {
                        // a rectangle with a square hole cut out
                        int w = R(6, 12), h = R(5, 10), s = R(1, Math.Min(w, h) - 3);
                        p = P("A garden bed is " + w + " by " + h + ". A square pond " + s + " by " + s + " is in the middle. What is the area of the soil around the pond?", (w * h - s * s).ToString(Inv), "Whole rectangle: " + w + TX + h + " = " + (w * h) + ".", "Take away the pond: " + s + TX + s + " = " + (s * s) + ".");
                        p.Visual = "hole:" + w + "," + h + "," + s;
                        W(p, "Find the whole rectangle, then take away the pond.", "Whole rectangle: " + w + TX + h + " = " + (w * h) + ".", "Pond: " + s + TX + s + " = " + (s * s) + ".", "Soil: " + (w * h) + MN + (s * s) + " = " + (w * h - s * s) + ".");
                        break;
                    }
            }
            p.Unit = "square units";
            return p;
        }
        static Problem Surface(int lv)
        {
            Problem p;
            switch (lv)
            {
                case 1: { int s = R(2, 9); p = P("A cube has edges of " + s + ". Use its net to find the surface area.", (6 * s * s).ToString(Inv), "The net has 6 squares, one for each face.", "Each square is " + s + TX + s + " = " + (s * s) + ". Six of them: 6" + TX + (s * s) + "."); p.Visual = "net:" + s + "," + s + "," + s; break; }
                case 2:
                case 3:
                    {
                        int l = R(2, 9), w = R(2, 7), h = R(2, 6);
                        int sa = 2 * (l * w + l * h + w * h);
                        p = P("Find the surface area of the box: " + l + " by " + w + " by " + h + ".", sa.ToString(Inv), "The net has 3 pairs of matching rectangles.", "Top and bottom " + l + TX + w + " = " + (l * w) + ", front and back " + l + TX + h + " = " + (l * h) + ", sides " + w + TX + h + " = " + (w * h) + ".", "Add one of each, then double: 2" + TX + "(" + (l * w) + " + " + (l * h) + " + " + (w * h) + ").");
                        p.Visual = "net:" + l + "," + w + "," + h;
                        if (lv == 3 && R(0, 1) == 0)
                        {
                            bool prism = R(0, 1) == 0;
                            p = Choice("How many faces does the net of a " + (prism ? "triangular prism" : "square pyramid") + " have?", "5", new[] { "4", "6", "3" },
                                prism ? "A triangular prism: 2 triangles (the ends) and 3 rectangles." : "A square pyramid: 1 square (the base) and 4 triangles.", "Count each flat piece of the net.");
                            p.Visual = prism ? "net:prism" : "net:pyramid,4,5";
                        }
                        break;
                    }
                case 4:
                    {
                        int b = 2 * R(2, 5), sl = R(3, 9);
                        int sa = b * b + 4 * (b * sl / 2);
                        p = P("A square pyramid has a base " + b + " by " + b + ". Each triangle has base " + b + " and height " + sl + ". Find the surface area.", sa.ToString(Inv), "The net: 1 square and 4 triangles.", "Square: " + b + TX + b + " = " + (b * b) + ". Each triangle: " + b + TX + sl + DV + "2 = " + (b * sl / 2) + ". Four triangles.");
                        p.Visual = "net:pyramid," + b + "," + sl;
                        break;
                    }
                default:
                    {
                        int l = R(3, 10), w = R(2, 6), h = R(2, 6);
                        int glass = l * w + 2 * l * h + 2 * w * h;
                        p = P("A glass terrarium is a box " + l + " by " + w + " by " + h + " inches with no lid. How many square inches of glass does it use?", glass.ToString(Inv), "No lid: the bottom plus 4 sides (5 faces, not 6).", "Bottom " + l + TX + w + " = " + (l * w) + ", front and back 2" + TX + l + TX + h + " = " + (2 * l * h) + ", ends 2" + TX + w + TX + h + " = " + (2 * w * h) + ".");
                        p.Visual = "net:" + l + "," + w + "," + h + ",open";
                        break;
                    }
            }
            if (p.Kind != "choice") p.Unit = "square units";
            return p;
        }

        // ================================================================ statistics: dot plots and box plots
        /// <summary>Quartiles the way US schools teach them: the median, then the medians of the lower and upper halves
        /// (leaving the middle number out when the count is odd).</summary>
        public static void FiveNumbers(int[] data, out double min, out double q1, out double med, out double q3, out double max)
        {
            int[] s = data.OrderBy(x => x).ToArray(); int n = s.Length;
            min = s[0]; max = s[n - 1];
            med = MedianOf(s, 0, n);
            int half = n / 2;
            q1 = MedianOf(s, 0, half);
            q3 = MedianOf(s, n - half, half);
        }
        static double MedianOf(int[] s, int start, int count) { return count % 2 == 1 ? s[start + count / 2] : (s[start + count / 2 - 1] + s[start + count / 2]) / 2.0; }
        static Problem Plots(int lv)
        {
            string[] topics = { "Heights of fern sprouts (cm)", "Seeds sprouted each day", "Ore chunks per mining trip", "Snails counted in the terrarium each week", "Minutes to dissolve a salt crystal" };
            string topic = Pick(topics);
            int n = lv <= 2 ? R(7, 11) : R(9, 13);
            if (lv >= 4 && n % 2 == 0) n++;   // an odd count keeps the quartiles whole more often
            int lo = R(1, 4), hi = lo + R(5, 8);
            int[] data = new int[n];
            for (int i = 0; i < n; i++) data[i] = R(lo, hi);
            int kind = lv <= 1 ? R(0, 1) : lv == 2 ? R(1, 2) : lv == 3 ? R(2, 3) : R(4, 6);
            Problem p;
            string dots = "dots:" + topic + ";" + string.Join(",", data.Select(x => x.ToString(Inv)).ToArray());
            switch (kind)
            {
                case 0:
                    {
                        p = P("The dot plot shows " + topic.ToLowerInvariant() + ". How many data points are there?", n.ToString(Inv), "Each dot is one data point.", "Count every dot in every column."); p.Visual = dots;
                        var cols = data.GroupBy(x => x).OrderBy(g => g.Key).ToList();
                        return W(p, "Each dot is one data point. Count the dots in each column: " + string.Join(", ", cols.Select(g => g.Key + " has " + g.Count()).ToArray()) + ".",
                            "Add the columns: " + string.Join(" + ", cols.Select(g => g.Count().ToString(Inv)).ToArray()) + " = " + n + ".");
                    }
                case 1:
                    {
                        var groups = data.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
                        if (groups.Count > 1 && groups[0].Count() == groups[1].Count()) { data[Array.IndexOf(data, groups[1].Key)] = groups[0].Key; dots = "dots:" + topic + ";" + string.Join(",", data.Select(x => x.ToString(Inv)).ToArray()); groups = data.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList(); }
                        if (groups.Count > 1 && groups[0].Count() == groups[1].Count()) return Plots(lv);
                        p = P("The dot plot shows " + topic.ToLowerInvariant() + ". What is the mode?", groups[0].Key.ToString(Inv), "The mode is the value that shows up most often.", "Find the tallest stack of dots."); p.Visual = dots;
                        return W(p, "The mode is the value that shows up most often: the tallest stack of dots.",
                            "Count each stack: " + string.Join(", ", groups.OrderBy(g => g.Key).Select(g => g.Key + " has " + g.Count()).ToArray()) + ".",
                            "The tallest is " + groups[0].Key + " with " + groups[0].Count() + " dots.");
                    }
                case 2:
                    {
                        if (n % 2 == 0) { data = data.Take(n - 1).ToArray(); n--; dots = "dots:" + topic + ";" + string.Join(",", data.Select(x => x.ToString(Inv)).ToArray()); }
                        double med = MedianOf(data.OrderBy(x => x).ToArray(), 0, n);
                        p = P("The dot plot shows " + topic.ToLowerInvariant() + ". What is the median?", Answers.Num(med), "There are " + n + " dots. The median is the middle one: number " + (n / 2 + 1) + " counting from the left.", "Count dots column by column from the left until you reach number " + (n / 2 + 1) + ".");
                        p.Visual = dots; return p;
                    }
                case 3: p = P("The dot plot shows " + topic.ToLowerInvariant() + ". What is the range?", (data.Max() - data.Min()).ToString(Inv), "Range = biggest value \u2212 smallest value.", "The biggest is " + data.Max() + " and the smallest is " + data.Min() + "."); p.Visual = dots; return p;
                default:
                    {
                        double mn, q1, md, q3, mx; FiveNumbers(data, out mn, out q1, out md, out q3, out mx);
                        string box = "boxplot:" + topic + ";" + string.Join(",", new[] { mn, q1, md, q3, mx }.Select(v => Answers.Num(v)).ToArray());
                        int which = kind == 4 ? 0 : kind == 5 ? 1 : 2;
                        if (which == 0) W(p = P("The box plot shows " + topic.ToLowerInvariant() + ". What is the median?", Answers.Num(md), "The line inside the box is the median.", "Read the number under that line."),
                            "A box plot shows five numbers: the smallest " + Answers.Num(mn) + ", Q1 " + Answers.Num(q1) + ", the median " + Answers.Num(md) + ", Q3 " + Answers.Num(q3) + " and the biggest " + Answers.Num(mx) + ".",
                            "The median is the line inside the box: it is at " + Answers.Num(md) + ".");
                        else if (which == 1) p = P("The box plot shows " + topic.ToLowerInvariant() + ". What is the interquartile range (IQR)?", Answers.Num(q3 - q1), "The box goes from the first quartile (Q1) to the third quartile (Q3).", "IQR = Q3 \u2212 Q1 = " + Answers.Num(q3) + MN + Answers.Num(q1) + ".");
                        else p = Choice("The box plot shows " + topic.ToLowerInvariant() + ". About what part of the data is between Q1 and Q3 (inside the box)?", "about half", new[] { "about a quarter", "almost all", "about three quarters" }, "A box plot cuts the data into four parts with about the same number of values.", "The box holds the middle two of those four parts.");
                        p.Visual = box; return p;
                    }
            }
        }

        // ================================================================ science lab stories
        // isotopes: symbol, name, atomic number (protons), mass number
        static readonly object[][] Isotopes = {
            new object[] { "U", "uranium", 92, 238 }, new object[] { "U", "uranium", 92, 235 }, new object[] { "C", "carbon", 6, 14 }, new object[] { "C", "carbon", 6, 12 },
            new object[] { "O", "oxygen", 8, 16 }, new object[] { "Fe", "iron", 26, 56 }, new object[] { "Au", "gold", 79, 197 }, new object[] { "Pb", "lead", 82, 208 } };
        static Problem Sci6(int lv)
        {
            int kind = R(0, lv <= 2 ? 8 : 13);
            string who = Pick(Kids6);
            Problem p;
            switch (kind)
            {
                case 0:
                    {
                        object[] iso = Pick(Isotopes); int z = (int)iso[2], a = (int)iso[3];
                        string nm = (string)iso[1];
                        p = P(nm.Substring(0, 1).ToUpperInvariant() + nm.Substring(1) + "-" + a + " has " + z + " protons. Its mass number, " + a + ", counts protons plus neutrons. How many neutrons does it have?", (a - z).ToString(Inv),
                            "Mass number = protons + neutrons.", "So neutrons = " + a + MN + z + ".");
                        p.Visual = "element:" + z + "," + iso[0] + "," + nm + "," + a;
                        return p;
                    }
                case 1:
                    {
                        object[] iso = Pick(Isotopes); int z = (int)iso[2];
                        string nm = (string)iso[1];
                        int k = R(2, 5);
                        p = P("The atomic number of " + nm + " is the number of protons in each atom. It is " + z + ". A model shows " + k + " " + nm + " atoms. How many protons are in all of them together?", (k * z).ToString(Inv),
                            "Each " + nm + " atom has " + z + " protons.", k + TX + z + " = ?");
                        p.Visual = "element:" + z + "," + iso[0] + "," + nm + ",";
                        return p;
                    }
                case 2:
                    {
                        // halving: a made-up lab isotope with a half-life in days
                        int k = R(2, 4), h = Pick(2, 3, 5, 6, 8), start = (1 << k) * R(3, 25);
                        p = P("A pretend lab sample has a half-life of " + h + " days: every " + h + " days, the amount left is cut in half. It starts at " + start + " grams. How many grams are left after " + (h * k) + " days?", (start >> k).ToString(Inv),
                            (h * k) + " days is " + k + " half-lives (" + (h * k) + DV + h + ").", "Halve " + k + " times: " + start + " \u2192 " + (start / 2) + " \u2192 ...");
                        return p;
                    }
                case 3:
                    {
                        int kg = 1000 * R(1, 9);
                        p = P("Natural uranium is about 99.3% uranium-238 and about 0.7% uranium-235. About how many kilograms of uranium-235 are in " + Comma(kg) + " kg of natural uranium?", (kg / 1000 * 7).ToString(Inv), "0.7% means 0.7 out of 100, or 7 out of 1,000.", Comma(kg) + " kg is " + (kg / 1000) + TX + "1,000 kg, so " + (kg / 1000) + TX + "7.");
                        return p;
                    }
                case 4:
                    {
                        int pct = Pick(3, 4, 5), kg = 100 * R(1, 9);
                        p = P("Fuel for many nuclear power plants is enriched so that about 3% to 5% of its uranium is uranium-235. A batch of " + kg + " kg of fuel is " + pct + "% uranium-235. How many kilograms is that?", (kg * pct / 100).ToString(Inv), pct + "% of " + kg + " = " + pct + "/100" + TX + kg + ".", "1% of " + kg + " is " + (kg / 100) + ". Multiply by " + pct + ".");
                        return p;
                    }
                case 5:
                    {
                        int H = 12 * R(2, 4); int[] fr = { Pick(4, 6), Pick(3, 4) };
                        int g = H / fr[0], s = H / fr[1];
                        p = P(who + " fills a jar " + H + " cm tall. Gravel fills the bottom 1/" + fr[0] + " of the jar and soil fills the next 1/" + fr[1] + ". How many cm of the jar do gravel and soil fill together?", (g + s).ToString(Inv), "1/" + fr[0] + " of " + H + " = " + H + DV + fr[0] + " = " + g + " cm.", "1/" + fr[1] + " of " + H + " = " + s + " cm. Add them.");
                        return p;
                    }
                case 6:
                    {
                        int days = Pick(2, 3, 4), ml = days * R(5, 20), total = days * R(3, 7);
                        p = P("A terrarium needs " + ml + " mL of water every " + days + " days. At that rate, how many mL does it need in " + total + " days?", (ml / days * total).ToString(Inv), "Unit rate: " + ml + DV + days + " = " + (ml / days) + " mL per day.", "Multiply by " + total + " days.");
                        return p;
                    }
                case 7:
                    {
                        int d1 = R(10, 60), d2 = R(5, 40);
                        p = P("A space rover starts at the surface (0 m). It digs down " + d1 + " m, then " + d2 + " m more. What is its height now, as a positive or negative number?", (-(d1 + d2)).ToString(Inv), "Below the surface is negative.", "0 \u2212 " + d1 + " \u2212 " + d2 + " = ?");
                        return p;
                    }
                case 8:
                    {
                        int v = 5 * R(3, 20), c = 5 * R(v / 5 + 2, 60);
                        p = P("A copper vein is at \u2212" + v + " m and an ice cave is at \u2212" + c + " m. How far apart are they?", (c - v).ToString(Inv), "Both are below 0. Distance is how far apart on the number line.", c + MN + v + " = ?");
                        return p;
                    }
                case 9:
                    {
                        int pct = Pick(2, 4, 5, 10), g = 100 * R(1, 6);
                        p = P("A salt water solution is " + pct + "% salt by mass. How many grams of salt are in " + g + " g of the solution?", (g * pct / 100).ToString(Inv), pct + "% means " + pct + " grams of salt in every 100 grams.", g + " g is " + (g / 100) + " hundreds: " + pct + TX + (g / 100) + ".");
                        return p;
                    }
                case 10:
                    {
                        int a = R(400, 1999), b = R(100, a - 50); // hundredths
                        p = P("A test tube holds " + Answers.Num(a / 100.0) + " mL of a solution. " + who + " pours out " + Answers.Num(b / 100.0) + " mL. How much is left?", Answers.Num((a - b) / 100.0), "Line up the decimal points.", "Subtract like whole numbers, then put the point back.");
                        return W(p, "Pouring out means less is left: subtract. Line up the decimal points.",
                            "Count in hundredths: " + Answers.Num(a / 100.0) + " is " + a + " hundredths and " + Answers.Num(b / 100.0) + " is " + b + " hundredths.",
                            a + MN + b + " = " + (a - b) + " hundredths, which is " + Answers.Num((a - b) / 100.0) + ".");
                    }
                case 11:
                    {
                        int L = R(2, 9), half = R(0, 1);
                        double liters = L + half * 0.5;
                        p = P("1 liter = 1,000 mL. How many mL are in " + Answers.Num(liters) + " liters?", Answers.Num(liters * 1000), "Multiply by 1,000.", "Move the decimal point three places to the right.");
                        return p;
                    }
                case 12:
                    {
                        int min = R(2, 5), rate = R(12, 40);
                        p = P("A Geiger counter clicks each time it detects radiation. Near a rock sample it counted " + (min * rate) + " clicks in " + min + " minutes. How many clicks per minute is that?", rate.ToString(Inv), "Clicks per minute: divide clicks by minutes.", (min * rate) + DV + min + " = ?");
                        return p;
                    }
                default:
                    {
                        int[] g = new int[4]; for (int i = 0; i < 4; i++) g[i] = R(4, 14) * 5;   // tenths of a cm in steps of 0.5
                        int extra = (4 - g.Sum() % 4) % 4; // make the mean end in .x5 at most: keep it exact in hundredths
                        g[R(0, 3)] += extra * 5;
                        double mean = g.Sum() / 4.0 / 10.0;
                        p = P("A fern in the terrarium grew " + string.Join(", ", g.Select(x => Answers.Num(x / 10.0)).ToArray()) + " cm in four weeks. What was its mean growth per week?", Answers.Num(mean), "Add the four weeks: " + Answers.Num(g.Sum() / 10.0) + " cm.", "Divide by 4.");
                        return p;
                    }
            }
        }
    }
}
