// Learning Lab - Languages: checking answers. Answers are compared ignoring case, extra spaces, commas and the end
// marks (. ! ? ...). Typed answers also forgive missing accents and a missing opening ¿ or ¡, but say so ("Watch the
// accent: está"). Spoken answers are compared word by word.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LearningLab.Languages
{
    public sealed class AnswerResult
    {
        public bool Right;
        /// <summary>A friendly note on a right answer that wasn't quite exact (accents, ¿ ¡), else null.</summary>
        public string Note;
        /// <summary>The right answer to show.</summary>
        public string Expected;
    }

    public sealed class SaidWord
    {
        public string Word;
        public bool Heard;
    }

    public static class Answers
    {
        static readonly char[] EndMarks = { '.', '!', '?', '…', ';', ':' };

        /// <summary>Lower case, single spaces, no commas, no end marks; keepAccents false also drops accents and ¿ ¡.</summary>
        public static string Normalize(string s, bool keepAccents)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char ch0 in s.Trim().ToLowerInvariant())
            {
                char ch = ch0;
                if (ch == '’') ch = '\'';
                if (ch == ',' || ch == '"' || ch == '“' || ch == '”' || ch == '«' || ch == '»') continue;
                if (char.IsWhiteSpace(ch)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(ch);
            }
            string r = sb.ToString();
            // end marks at the end (and before a closing space)
            r = r.TrimEnd(EndMarks).TrimEnd();
            // marks inside ("Hola. ¿Qué tal?" keeps its words apart but drops the dots)
            r = string.Join(" ", r.Split(' ').Select(w => w.TrimEnd(EndMarks)).Where(w => w.Length > 0));
            if (!keepAccents) r = Plain(r).Replace("¿", "").Replace("¡", "").Trim();
            return r;
        }

        /// <summary>Removes accents (á -> a, ñ -> n, ü -> u): only for forgiving typed answers.</summary>
        public static string Plain(string s)
        {
            var d = (s ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>A word without ¿ ¡ and the marks around it (for tiles and looking words up).</summary>
        public static string Bare(string w)
        {
            if (w == null) return "";
            return w.Trim().Trim('¿', '¡', '.', ',', '!', '?', ';', ':', '"', '“', '”', '«', '»', '(', ')', '…', '—', '–');
        }

        /// <summary>The words of a sentence, each without its marks (the build tiles).</summary>
        public static List<string> Words(string s)
        {
            return (s ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(Bare).Where(w => w.Length > 0).ToList();
        }

        /// <summary>A picked or ordered answer: right when it matches, ignoring case, spaces and end marks.</summary>
        public static bool Same(string given, string expected)
        {
            return Normalize(given, true) == Normalize(expected, true);
        }

        /// <summary>A typed answer. Accents and the opening ¿ ¡ are forgiven, with a note.</summary>
        public static AnswerResult CheckTyped(string given, string expected, IEnumerable<string> accept = null)
        {
            var r = new AnswerResult { Expected = expected };
            var all = new List<string> { expected };
            if (accept != null) all.AddRange(accept.Where(a => !string.IsNullOrWhiteSpace(a)));
            string g = Normalize(given, true);
            if (g.Length == 0) return r;
            foreach (var a in all) if (Normalize(a, true) == g) { r.Right = true; return r; }
            string gp = Normalize(given, false);
            foreach (var a in all)
            {
                if (Normalize(a, false) != gp) continue;
                r.Right = true;
                r.Note = AccentNote(given, a);
                return r;
            }
            return r;
        }

        /// <summary>"Watch the accent: está" for the first word that only differs by accents; else a note on ¿ ¡.</summary>
        static string AccentNote(string given, string expected)
        {
            var gw = Words(given).Select(w => w.ToLowerInvariant()).ToList();
            var ew = Words(expected);
            var accented = new List<string>();
            for (int k = 0; k < ew.Count && k < gw.Count; k++)
                if (ew[k].ToLowerInvariant() != gw[k] && Plain(ew[k]).ToLowerInvariant() == Plain(gw[k])) accented.Add(ew[k].ToLowerInvariant());
            if (accented.Count > 0) return "Watch the accent: " + string.Join(", ", accented.Distinct());
            string e = (expected ?? "").Trim();
            if ((e.StartsWith("¿") || e.StartsWith("¡")) && !(given ?? "").Contains(e[0]))
                return "Watch the " + e[0] + " at the start: " + e;
            return "Watch the spelling: " + e;
        }

        /// <summary>Word tiles put in order: right when they make the sentence.</summary>
        public static bool CheckBuild(IEnumerable<string> tiles, string expected)
        {
            return Normalize(string.Join(" ", tiles), true) == Normalize(string.Join(" ", Words(expected)), true);
        }

        /// <summary>What speech recognition heard, word by word against the answer (accents forgiven).</summary>
        public static List<SaidWord> CheckSaid(string heard, string expected)
        {
            var pool = Words(heard).Select(w => Normalize(w, false)).ToList();
            var result = new List<SaidWord>();
            foreach (var w in Words(expected))
            {
                string n = Normalize(w, false);
                int k = pool.IndexOf(n);
                if (k >= 0) pool.RemoveAt(k);
                result.Add(new SaidWord { Word = w, Heard = k >= 0 });
            }
            return result;
        }

        /// <summary>Right when every word was heard.</summary>
        public static bool SaidRight(List<SaidWord> words) { return words.Count > 0 && words.All(w => w.Heard); }
    }
}
