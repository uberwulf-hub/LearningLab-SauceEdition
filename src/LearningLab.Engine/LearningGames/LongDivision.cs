// Learning Games - long division with the standard algorithm (6.NS.B.2), one step at a time:
// estimate, then Divide, Multiply, Subtract, Bring down ("Dad, Mom, Sister, Brother"), the remainder, checking by
// multiplying, and the answer as a mixed number or a decimal. Every number the child is asked for is computed here,
// and every wrong answer gets a kind sentence that says what to look at. The window (LongDivisionView) only draws.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LearningGames
{
    /// <summary>One thing the child writes: a quotient digit, a product, a difference, a brought-down digit, ...</summary>
    public sealed class LdStep
    {
        /// <summary>Estimate, Divide, Multiply, Subtract, BringDown, Remainder, CheckMultiply, CheckAdd, Mixed, Decimal, Story.</summary>
        public string Kind = "";
        /// <summary>The exact answer (digits, or "134 11/34", "134.3", "$12.75").</summary>
        public string Expected = "";
        /// <summary>Guided mode: what to do, with the numbers. Practice mode: Short.</summary>
        public string Prompt = "", Short = "", Hint = "";
        /// <summary>Where it goes on the sheet: -1 = the answer row above the dividend, 0.. = work rows below it;
        /// Col = the column of its last digit (column 0 is the dividend's first digit). Row -2 = not on the sheet.</summary>
        public int Row = -2, Col;
        /// <summary>For Divide / Multiply / Subtract: the number being divided at this point (the "partial dividend").</summary>
        public long Partial;
        /// <summary>For Divide: the quotient digit; Multiply: the digit used.</summary>
        public int Digit;
    }

    public sealed class LdCheck
    {
        public bool Ok;
        public string Message = "";
    }

    public sealed class LongDivision
    {
        public long Dividend;
        public int Divisor;
        /// <summary>Decimal places to keep dividing past the decimal point (0 = stop with a remainder).</summary>
        public int Places;
        public long QuotientWhole, Remainder;
        /// <summary>The quotient as written above the bracket ("134", "12.75").</summary>
        public string QuotientText = "";
        /// <summary>The dividend's digits followed by Places zeros; the decimal point sits after DigitsBeforePoint.</summary>
        public string Digits = "";
        public int DigitsBeforePoint;
        /// <summary>The column of the first quotient digit.</summary>
        public int FirstCol;
        public List<LdStep> Steps = new List<LdStep>();
        public int Current;
        public int Mistakes, WrongThisStep;
        /// <summary>A word problem around the numbers (Story step at the end), or "".</summary>
        public string Story = "", StoryQuestion = "", StoryRule = "", StoryUnit = "";
        public int Level;

        public LdStep Step { get { return Current < Steps.Count ? Steps[Current] : null; } }
        public bool Done { get { return Current >= Steps.Count; } }
        public int StepCount { get { return Steps.Count; } }
        public LdStep StepAt(int i) { return Steps[i]; }
        public int WorkRows { get { int m = 0; foreach (LdStep s in Steps) m = Math.Max(m, s.Row + 1); return m; } }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string Times = " \u00D7 ", Minus = " \u2212 ", Div = " \u00F7 ";

        /// <summary>Lays out the whole division. Places > 0 keeps dividing past the decimal point (bringing down zeros)
        /// until nothing is left or the places run out.</summary>
        public static LongDivision For(long dividend, int divisor, int places, bool estimate, bool check, bool mixed, bool decimalRound)
        {
            if (divisor <= 0) throw new ArgumentException("divisor");
            if (dividend < 0) throw new ArgumentException("dividend");
            LongDivision d = new LongDivision();
            d.Dividend = dividend; d.Divisor = divisor; d.Places = Math.Max(0, places);
            string whole = dividend.ToString(Inv);
            d.DigitsBeforePoint = whole.Length;
            d.Digits = whole + new string('0', d.Places);

            if (estimate) d.AddEstimate();

            // the first partial dividend: the fewest digits from the left that make a number at least as big as the divisor
            int col = 0; long partial = d.Digits[0] - '0';
            while (partial < divisor && col < whole.Length - 1) { col++; partial = partial * 10 + (d.Digits[col] - '0'); }
            d.FirstCol = col;
            int row = 0;
            var qdigits = new System.Text.StringBuilder();
            long diff;
            bool pointShown = false;
            while (true)
            {
                int q = (int)(partial / divisor);
                long prod = (long)q * divisor;
                diff = partial - prod;
                bool afterPoint = col >= d.DigitsBeforePoint;
                LdStep s1 = new LdStep { Kind = "Divide", Expected = q.ToString(Inv), Row = -1, Col = col, Partial = partial, Digit = q };
                s1.Short = "Divide";
                s1.Prompt = "Divide: how many times does " + divisor + " fit into " + partial + "? Write that digit above the " + Ordinal(col, d) + ".";
                s1.Hint = TimesTable(divisor, partial);
                if (afterPoint && !pointShown) { s1.Prompt = "The answer gets a decimal point right above the one in the dividend. " + s1.Prompt; pointShown = true; }
                d.Steps.Add(s1);
                LdStep s2 = new LdStep { Kind = "Multiply", Expected = prod.ToString(Inv), Row = row, Col = col, Partial = partial, Digit = q };
                s2.Short = "Multiply";
                s2.Prompt = "Multiply: " + q + Times + divisor + " = ? Write it under " + partial + ".";
                s2.Hint = MultiplyHint(q, divisor);
                d.Steps.Add(s2);
                LdStep s3 = new LdStep { Kind = "Subtract", Expected = diff.ToString(Inv), Row = row + 1, Col = col, Partial = partial, Digit = q };
                s3.Short = "Subtract";
                s3.Prompt = "Subtract: " + partial + Minus + prod + " = ?";
                s3.Hint = "Line up the ones. If a top digit is smaller than the one under it, borrow 10 from the next place. The answer must be smaller than " + divisor + ".";
                d.Steps.Add(s3);
                qdigits.Append((char)('0' + q));
                bool more = col + 1 < d.Digits.Length && (col + 1 < d.DigitsBeforePoint || diff != 0);
                if (!more) break;
                int next = d.Digits[col + 1] - '0';
                bool intoPoint = col + 1 == d.DigitsBeforePoint;
                LdStep s4 = new LdStep { Kind = "BringDown", Expected = next.ToString(Inv), Row = row + 1, Col = col + 1, Partial = diff };
                s4.Short = "Bring down";
                s4.Prompt = intoPoint
                    ? "Bring down: there are no more digits, so put a decimal point after " + whole + " and a 0 after it. Bring that 0 down next to " + diff + "."
                    : "Bring down the next digit of " + whole + " and write it next to " + diff + ". What digit is it?";
                s4.Hint = "Look at the dividend, in the column just right of the last digit you used. Bring it straight down.";
                d.Steps.Add(s4);
                partial = diff * 10 + next; col++; row += 2;
            }
            d.Remainder = diff;
            // the quotient: the digits above the bracket, with the decimal point where it belongs
            string qd = qdigits.ToString();
            int intDigits = d.DigitsBeforePoint - d.FirstCol;   // quotient digits before the point
            string qi = qd.Substring(0, Math.Min(intDigits, qd.Length)).TrimStart('0');
            if (qi.Length == 0) qi = "0";
            string qf = qd.Length > intDigits ? qd.Substring(intDigits) : "";
            d.QuotientWhole = long.Parse(qi, Inv);
            d.QuotientText = qf.Length > 0 ? qi + "." + qf : qi;

            if (d.Places == 0 && d.Remainder > 0)
            {
                LdStep r = new LdStep { Kind = "Remainder", Expected = d.Remainder.ToString(Inv), Row = -2 };
                r.Short = "Remainder";
                r.Prompt = "There are no more digits to bring down. What is left over? That is the remainder.";
                r.Hint = "The remainder is the number at the bottom after your last subtraction. It is always smaller than " + divisor + ".";
                d.Steps.Add(r);
            }
            if (check)
            {
                long back = d.Places == 0 ? d.QuotientWhole * divisor : 0;
                if (d.Places == 0)
                {
                    LdStep c1 = new LdStep { Kind = "CheckMultiply", Expected = back.ToString(Inv), Row = -2 };
                    c1.Short = "Check";
                    c1.Prompt = "Check by multiplying: " + d.QuotientWhole + Times + divisor + " = ?";
                    c1.Hint = MultiplyHint(d.QuotientWhole, divisor);
                    d.Steps.Add(c1);
                    if (d.Remainder > 0)
                    {
                        LdStep c2 = new LdStep { Kind = "CheckAdd", Expected = (back + d.Remainder).ToString(Inv), Row = -2 };
                        c2.Short = "Check";
                        c2.Prompt = "Now add the remainder: " + back + " + " + d.Remainder + " = ? It should be " + dividend + " again.";
                        c2.Hint = "Add the ones, then the tens, then the hundreds. If you get " + dividend + ", your division was right.";
                        d.Steps.Add(c2);
                    }
                }
                else
                {
                    LdStep c1 = new LdStep { Kind = "CheckMultiply", Expected = d.Remainder == 0 ? dividend.ToString(Inv) : "", Row = -2 };
                    if (d.Remainder == 0)
                    {
                        c1.Short = "Check";
                        c1.Prompt = "Check by multiplying: " + d.QuotientText + Times + divisor + " = ?";
                        c1.Hint = "Multiply as if there were no decimal point, then put the point back: count the decimal places in " + d.QuotientText + ".";
                        d.Steps.Add(c1);
                    }
                }
            }
            if (mixed && d.Places == 0 && d.Remainder > 0)
            {
                LdStep m = new LdStep { Kind = "Mixed", Expected = d.QuotientWhole + " " + d.Remainder + "/" + divisor, Row = -2 };
                m.Short = "Mixed number";
                m.Prompt = "Write the answer as a mixed number: the whole part is " + d.QuotientWhole + ", and the remainder goes over the divisor. (Type it like 3 1/4.)";
                m.Hint = "The remainder " + d.Remainder + " is " + d.Remainder + " out of the " + divisor + " you needed for one more, so it is the fraction " + d.Remainder + "/" + divisor + ".";
                d.Steps.Add(m);
            }
            if (decimalRound && d.Places == 0 && d.Remainder > 0)
            {
                LdStep t = new LdStep { Kind = "Decimal", Expected = RoundTenths(dividend, divisor), Row = -2 };
                t.Short = "Decimal";
                t.Prompt = "Now as a decimal, rounded to the nearest tenth: " + dividend + Div + divisor + " is about ?";
                t.Hint = "The remainder " + d.Remainder + " out of " + divisor + " is " + Answers.Num(Math.Floor((double)d.Remainder / divisor * 100) / 100) + "... of one more. Round that to one decimal place and add it to " + d.QuotientWhole + ".";
                d.Steps.Add(t);
            }
            return d;
        }

        // ---------------------------------------------------------------- estimating first
        /// <summary>A friendly estimate: the divisor rounded to its leading digit (34 -> 30) and the dividend to a
        /// multiple of it that divides easily (4567 -> 4500), so 4500 / 30 = 150.</summary>
        public static void Estimate(long dividend, int divisor, out long eDividend, out long eDivisor, out long eQuotient)
        {
            eDivisor = divisor;
            if (divisor >= 10)
            {
                long p = 1; while (divisor / p >= 10) p *= 10;
                eDivisor = (long)Math.Round((double)divisor / p, MidpointRounding.AwayFromZero) * p;
                if (eDivisor == 0) eDivisor = p;
            }
            double raw = (double)dividend / eDivisor;
            eQuotient = Nice(raw);
            if (eQuotient < 1) eQuotient = 1;
            eDividend = eQuotient * eDivisor;
        }
        static long Nice(double x)
        {
            if (x < 10) return (long)Math.Max(1, Math.Round(x, MidpointRounding.AwayFromZero));
            long p = 1; while (x / p >= 100) p *= 10;   // two significant digits
            if (x < 100) p = 10;                         // one significant digit for two-digit answers
            return (long)Math.Round(x / p, MidpointRounding.AwayFromZero) * p;
        }
        void AddEstimate()
        {
            long ed, ev, eq;
            Estimate(Dividend, Divisor, out ed, out ev, out eq);
            LdStep s = new LdStep { Kind = "Estimate", Expected = eq.ToString(Inv), Row = -2 };
            s.Short = "Estimate";
            s.Prompt = "Estimate first. " + (ev != Divisor ? Divisor + " is close to " + ev + ", and " : "") + Dividend + " is close to " + ed + ". What is " + ed + Div + ev + "?";
            s.Hint = "Cover the zeros: think of a basic fact you know, then put the zeros back. Your real answer should be close to this.";
            Steps.Add(s);
        }

        // ---------------------------------------------------------------- answers and kind words
        /// <summary>Checks an answer for the current step. Right: moves on. Wrong: a sentence about what to look at
        /// (guided mode explains with the numbers; practice mode says which step to look at).</summary>
        public LdCheck Check(string given, bool guided)
        {
            LdCheck r = new LdCheck();
            LdStep s = Step;
            if (s == null) { r.Ok = true; return r; }
            string g = (given ?? "").Trim();
            r.Ok = Matches(s, g);
            if (r.Ok)
            {
                Current++; WrongThisStep = 0;
                r.Message = Praise(s);
                return r;
            }
            Mistakes++; WrongThisStep++;
            r.Message = guided ? Explain(s, g) : Nudge(s);
            return r;
        }
        /// <summary>Skips the current step (after a few tries the app writes it in and moves on).</summary>
        public void GiveUp() { if (!Done) { Current++; WrongThisStep = 0; } }

        public bool Matches(LdStep s, string g)
        {
            if (g.Length == 0) return false;
            switch (s.Kind)
            {
                case "Mixed":
                    // a whole number and a fraction ("134 11/34"); an equivalent fraction part ("134 22/68") is fine
                    if (!g.Contains(" ") && !Answers.Same(g, s.Expected, "number")) return false;
                    if (!g.Contains(" ")) return Remainder == 0;
                    return Answers.Same(g, s.Expected, "number");
                case "Story":
                    return Answers.Same(g, s.Expected, StoryUnit == "$" ? "money" : (s.Expected.Contains("/") ? "number" : "number"));
                default:
                    {
                        string a = g.Replace(",", "").Replace(" ", "");
                        double x, y;
                        if (!Answers.TryValue(a, out x) || !Answers.TryValue(s.Expected, out y)) return false;
                        return Math.Abs(x - y) < 1e-9;
                    }
            }
        }

        string Praise(LdStep s)
        {
            switch (s.Kind)
            {
                case "Estimate": return "Good estimate! The answer should be near " + s.Expected + ".";
                case "Divide": return s.Digit == 0 ? "Right: " + Divisor + " doesn't fit, so write 0." : "Yes! " + s.Digit + " is the most that fits.";
                case "Multiply": return "Right, " + s.Digit + Times + Divisor + " = " + s.Expected + ".";
                case "Subtract": return "Nice subtracting.";
                case "BringDown": return "Brought down!";
                case "Remainder": return "That's the remainder.";
                case "CheckMultiply": return Remainder == 0 || Places > 0 ? "It matches. Your answer is right!" : "Good. One more step to check.";
                case "CheckAdd": return "It matches " + Dividend + ". Your division is right!";
                case "Mixed": return "That's the mixed number.";
                case "Decimal": return "That's the decimal.";
                default: return "Correct!";
            }
        }

        string Nudge(LdStep s)
        {
            switch (s.Kind)
            {
                case "Divide": return "Not quite. Think: which times-table fact is closest to " + s.Partial + " without going over?";
                case "Multiply": return "Not quite. Check your multiplying.";
                case "Subtract": return "Not quite. Check your subtracting, and watch for borrowing.";
                case "BringDown": return "Not quite. Which digit is next in the dividend?";
                case "Remainder": return "Look at the bottom number of your work.";
                default: return "Not quite. Try again, or ask for a hint.";
            }
        }

        string Explain(LdStep s, string g)
        {
            double gv; bool num = Answers.TryValue(g.Replace(",", "").Replace(" ", ""), out gv);
            switch (s.Kind)
            {
                case "Estimate":
                    return "Close! Use the easy numbers from the question. " + StepHintFirst(s);
                case "Divide":
                    {
                        if (!num || gv < 0 || gv != Math.Floor(gv)) return "Type one digit, 0 to 9: how many " + Divisor + "s fit into " + s.Partial + ".";
                        long gi = (long)gv;
                        if (gi > 9) return "Only one digit goes in each place, so the answer here is 9 or less. How many " + Divisor + "s fit into " + s.Partial + "?";
                        if (gi * Divisor > s.Partial) return gi + Times + Divisor + " = " + (gi * Divisor) + ". That's more than " + s.Partial + ", so " + gi + " is too big. Try a smaller number.";
                        return gi + Times + Divisor + " = " + (gi * Divisor) + ", and " + s.Partial + Minus + (gi * Divisor) + " = " + (s.Partial - gi * Divisor) + ". That's still " + Divisor + " or more, so another " + Divisor + " fits. Try a bigger number.";
                    }
                case "Multiply":
                    return "Multiply the digit you just wrote by the divisor: " + s.Digit + Times + Divisor + ". " + MultiplyHint(s.Digit, Divisor);
                case "Subtract":
                    {
                        long prod = s.Partial - long.Parse(s.Expected, Inv);
                        if (num && gv < 0) return "Take the bottom number away from the top one: " + s.Partial + Minus + prod + ". The top is bigger, so the answer can't be negative.";
                        if (num && gv >= Divisor) return "Hmm, " + Answers.Num(gv) + " is " + Divisor + " or more. Check " + s.Partial + Minus + prod + " again, digit by digit from the ones.";
                        return s.Partial + Minus + prod + ": start with the ones. If the top digit is smaller, borrow 10 from the place to its left.";
                    }
                case "BringDown":
                    return "Bring down the next digit of the dividend: look in the column just right of the digits you've used. It's the " + Ordinal(s.Col, this) + ".";
                case "Remainder":
                    return "The remainder is what is left at the bottom after the last subtraction. It is smaller than " + Divisor + ".";
                case "CheckMultiply":
                    return "Multiply your answer by the divisor. " + StepHintFirst(s);
                case "CheckAdd":
                    return "Add the remainder to the number you just got. Line up the ones.";
                case "Mixed":
                    return "Write the whole-number part, a space, then remainder/divisor. Like this: " + QuotientWhole + " ?/" + Divisor + ".";
                case "Decimal":
                    return "Divide the remainder by the divisor to see how much of one more there is, then round to one decimal place.";
                case "Story":
                    return StoryHelp();
                default:
                    return "Not quite. Try again.";
            }
        }
        string StepHintFirst(LdStep s) { return s.Hint; }

        string StoryHelp()
        {
            switch (StoryRule)
            {
                case "up": return "Think about the remainder: " + Remainder + " " + StoryUnit + " would be left out. They still need a place, so you need one more. The answer is not " + QuotientWhole + ".";
                case "drop": return "Only full ones count, so the leftover " + Remainder + " can't make another one. Leave the remainder off.";
                case "use": return "The question asks what is left over. That is the remainder.";
                case "share": return "Share the leftover too: keep dividing past the decimal point, or write the remainder as a fraction of " + Divisor + ".";
                default: return "Read the question again: what does it ask for?";
            }
        }

        /// <summary>Adds the word-problem question at the end: the answer depends on what the remainder means.</summary>
        public void AddStory(string story, string question, string rule, string unit, string answer)
        {
            Story = story; StoryQuestion = question; StoryRule = rule; StoryUnit = unit;
            LdStep s = new LdStep { Kind = "Story", Expected = answer, Row = -2, Short = "Answer the story" };
            s.Prompt = question;
            switch (rule)
            {
                case "up": s.Hint = "Is the leftover part important? If something left over still needs a place, round up."; break;
                case "drop": s.Hint = "Can the leftover make a whole one? If not, only count the full ones."; break;
                case "use": s.Hint = "This question is about what is left over."; break;
                default: s.Hint = "Share everything, including the leftover."; break;
            }
            Steps.Add(s);
        }

        /// <summary>What the story's answer is for a remainder rule.</summary>
        public static string StoryAnswer(long dividend, int divisor, string rule)
        {
            long q = dividend / divisor, r = dividend % divisor;
            switch (rule)
            {
                case "up": return (q + (r > 0 ? 1 : 0)).ToString(Inv);
                case "drop": return q.ToString(Inv);
                case "use": return r.ToString(Inv);
                default: return Answers.Frac((int)dividend, divisor);
            }
        }

        // ---------------------------------------------------------------- small helpers
        static string Ordinal(int col, LongDivision d)
        {
            string[] names = { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth" };
            if (col >= d.DigitsBeforePoint) return col - d.DigitsBeforePoint == 0 ? "first 0 after the decimal point" : "next 0 after the decimal point";
            return (col < names.Length ? names[col] : (col + 1) + "th") + " digit of " + d.Dividend;
        }
        public static string TimesTable(int divisor, long partial)
        {
            var parts = new List<string>();
            for (int k = 1; k <= 9; k++)
            {
                long v = (long)k * divisor;
                parts.Add(k + Times.Trim() + divisor + " = " + v);
                if (v > partial) break;
            }
            return "Count up in " + divisor + "s: " + string.Join(",  ", parts.ToArray()) + ".";
        }
        public static string MultiplyHint(long q, int divisor)
        {
            if (divisor < 10 || q < 1) return "Use your times tables: " + q + Times + divisor + ".";
            int tens = divisor / 10 * 10, ones = divisor % 10;
            if (ones == 0) return q + Times + (divisor / 10) + " = " + (q * (divisor / 10)) + ", then put a 0 on the end.";
            return "Split " + divisor + " into " + tens + " + " + ones + ": " + q + Times + tens + " = " + (q * tens) + " and " + q + Times + ones + " = " + (q * ones) + ". Add them.";
        }
        /// <summary>dividend / divisor rounded to tenths (halves round up), exact with whole numbers.</summary>
        public static string RoundTenths(long dividend, int divisor)
        {
            long t = (dividend * 20 + divisor) / (2L * divisor);   // round(10 * a / b)
            return (t / 10).ToString(Inv) + "." + (t % 10).ToString(Inv);
        }

        /// <summary>The number shown in work row r ending at column c, for drawing: text of every step already done.</summary>
        public string Summary() { return Dividend + Div.Trim() + Divisor + " = " + (Places == 0 && Remainder > 0 ? QuotientWhole + " R " + Remainder : QuotientText); }
    }
}
