// Learning Games - "Learn from it" (2026-11-11, docs\MATH.md): a worked, step-by-step explanation of exactly the
// question that was asked, in plain words with its own numbers. A generator that knows the best way to explain its
// problem fills Problem.Steps (column adding and subtracting, long division, keep-change-flip...); the others are
// explained from their hints, with "= ?" filled in by the computed answer. Every explanation ends with a line that
// states the answer. The checks (group math-mastery) make thousands of each skill at every level and re-check every
// sum, difference, product and quotient written in the steps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LearningGames
{
    public static class Worked
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string MN = " − ", TX = " × ", DV = " ÷ ";

        /// <summary>The steps for this problem: the generator's own, or its hints with the answer filled in; the last line
        /// always says the answer.</summary>
        public static string[] Steps(Problem p)
        {
            var lines = new List<string>();
            bool own = p.Steps != null && p.Steps.Length > 0;
            foreach (string h in own ? p.Steps : p.Hints)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                lines.Add(own ? h : Fill(h, p));
            }
            lines.Add(Final(p));
            return lines.ToArray();
        }

        /// <summary>The answer as it should be shown, with its unit ("12 square units").</summary>
        public static string AnswerText(Problem p)
        {
            return p.Answer + (string.IsNullOrEmpty(p.Unit) ? "" : " " + p.Unit);
        }

        /// <summary>The last line: "So the answer is 47." For a comparison ("45 ? 32"): "So 45 > 32."</summary>
        public static string Final(Problem p)
        {
            var m = Regex.Match(p.Text, @"^\s*(\S+)\s+\?\s+(\S+)\s*$");
            if (m.Success) return "So " + m.Groups[1].Value + " " + p.Answer + " " + m.Groups[2].Value + ".";
            if (p.Kind == "choice") return "So the answer is: " + p.Answer + ".";
            return "So the answer is " + AnswerText(p) + ".";
        }

        /// <summary>A hint made into a step: a last "= ?" gets the answer, "your answer" becomes the answer.</summary>
        static string Fill(string hint, Problem p)
        {
            string t = Regex.Replace(hint, @"=\s*\?\s*$", "= " + p.Answer);
            t = Regex.Replace(t, @"=\s*\?\s*\.\s*$", "= " + p.Answer + ".");
            t = t.Replace("your answer", p.Answer);
            return t;
        }

        // ---------------------------------------------------------------- column adding and subtracting
        static readonly string[] Places = { "ones", "tens", "hundreds", "thousands", "ten thousands", "hundred thousands" };
        static readonly string[] Place1 = { "one", "ten", "hundred", "thousand", "ten thousand", "hundred thousand" };
        static string Cap(string s) { return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1); }
        static int[] Digits(long n, int count)
        {
            var d = new int[count];
            for (int i = 0; i < count; i++) { d[i] = (int)(n % 10); n /= 10; }
            return d;
        }
        static int Len(long n) { return Math.Max(1, n.ToString(Inv).Length); }
        static string LineUp(long big)
        {
            return big >= 100 ? "Line them up: ones under ones, tens under tens, hundreds under hundreds. Start with the ones."
                              : "Line them up: ones under ones, tens under tens. Start with the ones.";
        }

        /// <summary>a + b column by column, carrying tens: every line is a small sum with its own numbers.</summary>
        public static List<string> ColumnAdd(long a, long b)
        {
            var lines = new List<string> { LineUp(Math.Max(a, b)) };
            int n = Math.Min(Places.Length, Math.Max(Len(a), Len(b)));
            int[] da = Digits(a, n), db = Digits(b, n);
            int carry = 0;
            for (int i = 0; i < n; i++)
            {
                int sum = da[i] + db[i] + carry;
                string expr = carry > 0 ? da[i] + " + " + db[i] + " + " + carry + " = " + sum : da[i] + " + " + db[i] + " = " + sum;
                string line = Cap(Places[i]) + ": " + expr + ".";
                if (sum >= 10 && i < n - 1) line += " Write " + (sum % 10) + " and carry 1 " + Place1[i + 1] + " to the " + Places[i + 1] + ".";
                lines.Add(line);
                carry = sum >= 10 && i < n - 1 ? 1 : 0;
            }
            lines.Add("So " + a + " + " + b + " = " + (a + b) + ".");
            return lines;
        }

        /// <summary>a - b column by column, trading a ten (or a hundred) when the top digit is too small.</summary>
        public static List<string> ColumnSub(long a, long b)
        {
            var lines = new List<string> { LineUp(a) };
            int n = Math.Min(Places.Length, Len(a));
            int[] top = Digits(a, n), bot = Digits(b, n);
            for (int i = 0; i < n; i++)
            {
                if (i == n - 1 && i > 0 && top[i] == 0 && bot[i] == 0) break;   // nothing left in the last column
                string name = Cap(Places[i]) + ": ";
                if (top[i] < bot[i])
                {
                    int j = i + 1;
                    while (j < n - 1 && top[j] == 0) j++;
                    string line = name + top[i] + " is less than " + bot[i] + ", so ";
                    if (j == i + 1) line += "trade 1 " + Place1[i + 1] + " for 10 " + Places[i] + ".";
                    else
                    {
                        line += "there are no " + Places[i + 1] + " to trade: trade 1 " + Place1[j] + " for 10 " + Places[j - 1];
                        for (int k = j - 1; k > i; k--) line += ", then 1 " + Place1[k] + " for 10 " + Places[k - 1];
                        line += ".";
                    }
                    top[j]--;
                    for (int k = j - 1; k > i; k--) top[k] = 9;
                    top[i] += 10;
                    line += " Now " + top[i] + MN + bot[i] + " = " + (top[i] - bot[i]) + ".";
                    lines.Add(line);
                }
                else lines.Add(name + top[i] + MN + bot[i] + " = " + (top[i] - bot[i]) + ".");
            }
            lines.Add("So " + a + MN + b + " = " + (a - b) + ".");
            lines.Add("Check by adding back: " + (a - b) + " + " + b + " = " + a + ".");
            return lines;
        }

        // ---------------------------------------------------------------- long division
        /// <summary>n / d with the standard steps (divide, multiply, subtract, bring down), the remainder and the check.</summary>
        public static List<string> LongDiv(long n, int d)
        {
            var lines = new List<string>();
            LongDivision ld = LongDivision.For(n, d, 0, false, false, false, false);
            for (int i = 0; i < ld.StepCount; i++)
            {
                LdStep s = ld.StepAt(i);
                if (s.Kind == "Divide")
                {
                    long prod = (long)s.Digit * d, diff = s.Partial - prod;
                    lines.Add("How many " + d + "s fit in " + s.Partial + "? " + s.Digit + ". " + s.Digit + TX + d + " = " + prod + ", and " + s.Partial + MN + prod + " = " + diff + ".");
                }
                else if (s.Kind == "BringDown") lines.Add("Bring down the " + s.Expected + ": now you have " + (s.Partial * 10 + int.Parse(s.Expected, Inv)) + ".");
            }
            long q = n / d, r = n % d;
            lines.Add(r == 0 ? "Nothing is left over, so there is no remainder." : "Nothing is left to bring down, so the remainder is " + r + ".");
            lines.Add("Check: " + q + TX + d + " = " + (q * d) + (r > 0 ? ", and " + (q * d) + " + " + r + " = " + n : "") + ".");
            lines.Add("So " + n + DV + d + " = " + (r == 0 ? q.ToString(Inv) : q + " R " + r) + ".");
            return lines;
        }

        /// <summary>"8 + 8 + 8 = 24": the same number added r times, with its total.</summary>
        public static string Repeat(int n, int times) { return string.Join(" + ", Enumerable.Repeat(n.ToString(Inv), times).ToArray()) + " = " + (n * times); }
        /// <summary>Counting on or back: "9, 10, 11".</summary>
        public static string Count(int from, int steps, int dir) { return string.Join(", ", Enumerable.Range(1, steps).Select(i => (from + dir * i).ToString(Inv)).ToArray()); }
    }
}
