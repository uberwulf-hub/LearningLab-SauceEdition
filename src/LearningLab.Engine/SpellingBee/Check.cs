// Learning Lab - Spelling Bee: checking a spelling the way Andrew's spelling-bee.html does. Accents and capitals don't
// count ("eclair" spells éclair, "tripoli" spells Tripoli); any accepted spelling counts ("fertiliser"); spaces, dashes
// and apostrophes are forgiven with a note ("acappella" for a cappella). Spoken letters ("see, ay, bee, eye, en",
// "double ell", "dash") become a spelling with ParseSpoken. Also the hint (blanks, then the first letter, then the
// last) and the spell-out the voice reads ("C, A, B, I, N").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LearningLab.SpellingBee
{
    public sealed class BeeVerdict
    {
        public bool Ok;
        /// <summary>What was checked (the letters, when the answer was spoken letter by letter).</summary>
        public string Typed = "";
        /// <summary>The accepted spelling it matched.</summary>
        public string Matched;
        /// <summary>Right apart from spaces, dashes or apostrophes.</summary>
        public bool Close;
    }

    public static class BeeCheck
    {
        /// <summary>Without accents, lower case, curly apostrophes made straight, spaces squeezed.</summary>
        public static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            string t = sb.ToString().ToLowerInvariant().Replace('’', '\'').Replace('`', '\'').Replace('‘', '\'');
            return Regex.Replace(t, @"\s+", " ").Trim();
        }

        /// <summary>Norm without spaces, dashes and apostrophes.</summary>
        public static string Squash(string s) => Regex.Replace(Norm(s), @"[\s\-']", "");

        public static BeeVerdict Judge(string input, BeeWord word)
        {
            input = input ?? "";
            var cands = new List<string> { input };
            if (Regex.IsMatch(input.Trim(), @"\s")) cands.Add(ParseSpoken(input));
            foreach (var c in cands) foreach (var a in word.Alts) if (Norm(c) == Norm(a)) return new BeeVerdict { Ok = true, Typed = c, Matched = a };
            foreach (var c in cands) foreach (var a in word.Alts) if (Squash(c) == Squash(a) && Squash(a).Length > 0) return new BeeVerdict { Ok = true, Typed = c, Matched = a, Close = true };
            return new BeeVerdict { Ok = false, Typed = cands.Count > 1 ? cands[1] : cands[0] };
        }

        /// <summary>For each letter of the right spelling, true when the typed spelling missed or changed it. The two are
        /// lined up first (longest common subsequence), so "haf" for "half" marks only the l, not every letter after it.</summary>
        public static bool[] Wrong(string correct, string typed)
        {
            string t = Norm(typed ?? "");
            int n = correct.Length, m = t.Length;
            var want = new string[n];
            for (int i = 0; i < n; i++) want[i] = Norm(correct[i].ToString());
            var len = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    len[i, j] = want[i].Length == 1 && want[i][0] == t[j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
            var r = new bool[n];
            for (int k = 0; k < n; k++) r[k] = true;
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (want[a].Length == 1 && want[a][0] == t[b]) { r[a] = false; a++; b++; }
                else if (len[a + 1, b] >= len[a, b + 1]) a++;
                else b++;
            }
            return r;
        }

        /// <summary>The hint at a level (1 blanks, 2 + the first letter, 3 + the last): "c _ _ _ n", and the letter count.</summary>
        public static (string Blanks, int Letters) Hint(string word, int level)
        {
            var parts = new List<string>();
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                if (c == ' ') parts.Add(" ");
                else if (c == '-') parts.Add("-");
                else if (level >= 2 && i == 0) parts.Add(c.ToString());
                else if (level >= 3 && i == word.Length - 1) parts.Add(c.ToString());
                else parts.Add("_");
            }
            int letters = word.Count(c => c != ' ' && c != '-');
            return (string.Join(" ", parts), letters);
        }

        /// <summary>The letters as the voice reads them: capital letters separated by commas, "space", "dash".</summary>
        public static string SpellOut(string word)
        {
            var parts = new List<string>();
            foreach (char c in word)
            {
                if (c == ' ') parts.Add("space");
                else if (c == '-') parts.Add("dash");
                else if (c == '\'' || c == '’') parts.Add("apostrophe");
                else parts.Add(c.ToString().Normalize(NormalizationForm.FormD).Substring(0, 1).ToUpperInvariant());
            }
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ spoken letters
        static readonly Dictionary<string, string> sayMap = MakeSayMap();
        static readonly HashSet<string> skip = new HashSet<string> { "capital", "uppercase", "upper", "lowercase", "letter", "the", "then", "um", "uh", "comma", "period", "dot" };

        static Dictionary<string, string> MakeSayMap()
        {
            var m = new Dictionary<string, string>();
            foreach (var kv in new Dictionary<char, string>
            {
                ['a'] = "a ay eh hey ae", ['b'] = "b be bee bea", ['c'] = "c see sea cee si", ['d'] = "d dee de", ['e'] = "e ee", ['f'] = "f ef eff",
                ['g'] = "g gee ji jee", ['h'] = "h aitch haitch", ['i'] = "i eye aye ai", ['j'] = "j jay jae", ['k'] = "k kay cay kaye",
                ['l'] = "l el elle ell", ['m'] = "m em", ['n'] = "n en", ['o'] = "o oh owe", ['p'] = "p pee pea pe", ['q'] = "q cue queue que kyu",
                ['r'] = "r are ar", ['s'] = "s es ess", ['t'] = "t tee tea ti", ['u'] = "u you yu ewe yoo", ['v'] = "v vee ve", ['w'] = "w",
                ['x'] = "x ex ecks", ['y'] = "y why wye", ['z'] = "z zee zed",
            })
                foreach (var w in kv.Value.Split(' ')) m[w] = kv.Key.ToString();
            m["dash"] = "-"; m["hyphen"] = "-"; m["minus"] = "-"; m["space"] = " "; m["apostrophe"] = "'";
            return m;
        }

        /// <summary>Letters said out loud, as a recognizer wrote them, made into a spelling ("see ay bee" -> "cab",
        /// "double ell" -> "ll", "double u" -> "w", "dash" -> "-"). Words it doesn't know are kept as they are (a
        /// recognizer that grouped the letters, or the whole word said).</summary>
        public static string ParseSpoken(string raw)
        {
            var toks = Regex.Split(Regex.Replace((raw ?? "").ToLowerInvariant(), "[.,!?;:\"“”]", " "), @"\s+").Where(t => t.Length > 0).ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < toks.Count; i++)
            {
                string t = toks[i];
                if (skip.Contains(t)) continue;
                if (t == "double" && i + 1 < toks.Count)
                {
                    string nx = toks[i + 1];
                    if (nx == "you" || nx == "u" || nx == "yu") { sb.Append('w'); i++; continue; }
                    string l = sayMap.TryGetValue(nx, out var x) ? x : nx.Length == 1 ? nx : null;
                    if (l != null) { sb.Append(l).Append(l); i++; continue; }
                }
                if (t == "doubleyou" || t == "doubleu") { sb.Append('w'); continue; }
                if (t.Length == 1) { sb.Append(t); continue; }
                if (sayMap.TryGetValue(t, out var s)) { sb.Append(s); continue; }
                sb.Append(t);
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>The words a recognizer listens for when someone spells out loud (every letter's names, double,
        /// dash, space, apostrophe and the words people put in between).</summary>
        public static IReadOnlyList<string> SpokenVocabulary()
        {
            var l = new List<string>(sayMap.Keys);
            l.Add("double"); l.Add("double you"); l.AddRange(skip);
            return l.Distinct().ToList();
        }

        /// <summary>What the person hears after a run (the results page's line).</summary>
        public static (string Emoji, string Message) Cheer(int good, int total)
        {
            double pct = total > 0 ? (double)good / total : 0;
            if (pct >= 1) return ("\U0001F3C6", "Perfect score! You're ready for the bee!");
            if (pct >= 0.8) return ("\U0001F31F", "Awesome work. Just a few to review.");
            if (pct >= 0.5) return ("\U0001F41D", "Nice job! Practice the tricky ones below.");
            return ("\U0001F4AA", "Keep going. Every try makes you better!");
        }
    }
}
