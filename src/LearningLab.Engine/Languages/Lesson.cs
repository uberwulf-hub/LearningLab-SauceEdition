// Learning Lab - Languages: a spoken lesson as a list of steps (say this in that language, wait, ...). The audio maker
// turns the steps into one WAV with a mark at each item, so the car player can show where it is, skip and go back.
// Styles:
//   learn  - English, short wait, Spanish, a wait to repeat it (as long as the phrase plus a bit), Spanish again slower,
//            a wait to repeat again: for new units.
//   recall - English, a wait to try saying it in Spanish, Spanish, a wait to repeat: for review ("Test yourself").
//   listen - Spanish, a wait, the English: for understanding by ear.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningLab.Languages
{
    public enum StepKind { Say, Wait, Echo, Item, Chime }

    public sealed class LessonStep
    {
        public StepKind Kind;
        public string Text;       // Say
        public string Lang;       // Say: "en" or the course's language
        public double Rate = 1;   // Say: 1 = the chosen speed; less is slower
        public int Ms;            // Wait: milliseconds; Echo: extra milliseconds after the last phrase's own length
        public double Factor;     // Echo: times the length of the last thing said in the course's language
        public int Index;         // Item: which item of Lesson.Items starts here
        public int Voice;         // Say: 0 = the chosen voice, 1 = another voice of that language when there is one (cartoons)
        public double Pitch = 1;  // Say: higher (above 1, also a little faster) or lower, for a cartoon character
        public override string ToString()
        {
            switch (Kind)
            {
                case StepKind.Say: return "say " + Lang + " " + Text + (Rate != 1 ? " @" + Rate : "") + (Voice != 0 ? " v" + Voice : "") + (Pitch != 1 ? " ^" + Pitch : "");
                case StepKind.Wait: return "wait " + Ms;
                case StepKind.Echo: return "echo " + Factor + "+" + Ms;
                case StepKind.Item: return "item " + Index;
                default: return "chime";
            }
        }
    }

    public sealed partial class Lesson
    {
        public string Title;
        public string Style;
        public List<LangItem> Items = new List<LangItem>();
        public List<LessonStep> Steps = new List<LessonStep>();

        /// <summary>The styles for a unit (a story has its own, "story").</summary>
        public static readonly string[] Styles = { "learn", "recall", "listen", "drill" };
        public static string StyleName(string s)
        {
            switch (s)
            {
                case "recall": return "Test yourself";
                case "listen": return "Listen and understand";
                case "drill": return "Conversation drill";
                case "story": return "Listen to a story";
                case "cartoon": return "Listen to a cartoon";
                default: return "Learn new words";
            }
        }
        public static string StyleText(string s)
        {
            switch (s)
            {
                case "recall": return "You hear the English, then a pause to say it in Spanish yourself, then the Spanish.";
                case "listen": return "You hear the Spanish, a pause to work out what it means, then the English.";
                case "drill": return "The voice asks \"How would you say...?\" and waits while you say it, then gives the answer. Long phrases are built up from the end, and earlier ones come back later to check they stuck.";
                case "story": return "A short story, a sentence at a time: the Spanish, a pause, then the English.";
                case "cartoon": return "A cartoon conversation, twice, all in Spanish. Then \"What's going on?\": a pause to think, and the answer in English.";
                default: return "You hear the English, then the Spanish twice (the second time slower) with pauses to repeat it.";
            }
        }

        // ------------------------------------------------------------------ conversation drill
        /// <summary>How many asks later an item comes back (each time a longer gap).</summary>
        public static readonly int[] DrillGaps = { 2, 5, 11 };

        /// <summary>A conversation drill: each new phrase is given ("Here's how to say ..."), built up from its end when it
        /// is long, then earlier phrases come back as questions ("How would you say ...?", a pause, the answer) after
        /// 2, 5 and 11 more asks. think: text spoken before the new item with that index ("Think it through: ...").
        /// Items has one entry per time a phrase is asked, so the player's marks stay in order.</summary>
        public static Lesson BuildDrill(string title, string lang, IList<LangItem> items, double pauseScale = 1, string introText = null, IDictionary<int, string> think = null)
        {
            var l = new Lesson { Title = title, Style = "drill" };
            Func<int, int> ms = x => (int)Math.Round(x * Math.Clamp(pauseScale, 0.5, 3));
            l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
            l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = introText ?? title });
            l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(700) });
            l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "I'll ask you how to say something. Say it out loud in the pause, then listen." });
            l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(900) });
            var pending = new List<(int Due, LangItem Item, int Round)>();
            int slot = 0;
            void Ask(LangItem it)
            {
                l.Steps.Add(new LessonStep { Kind = StepKind.Item, Index = l.Items.Count });
                l.Items.Add(it);
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "How would you say: " + Spoken(it.English) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(3500) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target });
                l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.2, Ms = ms(1200) });
            }
            void Due()
            {
                while (true)
                {
                    int k = pending.FindIndex(p => p.Due <= slot);
                    if (k < 0) return;
                    var p = pending[k];
                    pending.RemoveAt(k);
                    Ask(p.Item);
                    slot++;
                    if (p.Round + 1 < DrillGaps.Length) pending.Add((slot + DrillGaps[p.Round + 1], p.Item, p.Round + 1));
                }
            }
            for (int n = 0; n < items.Count; n++)
            {
                var it = items[n];
                string t;
                if (think != null && think.TryGetValue(n, out t) && !string.IsNullOrWhiteSpace(t))
                {
                    l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = t });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(900) });
                }
                l.Steps.Add(new LessonStep { Kind = StepKind.Item, Index = l.Items.Count });
                l.Items.Add(it);
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "Here's how to say: " + Spoken(it.English) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(500) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target });
                l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.2, Ms = ms(1200) });
                var chunks = Backchain(it.Target);
                if (chunks.Count > 1)
                {
                    l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "Piece by piece, from the end." });
                    foreach (var ch in chunks)
                    {
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = ch, Rate = 0.85 });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.3, Ms = ms(900) });
                    }
                }
                slot++;
                pending.Add((slot + DrillGaps[0], it, 0));
                Due();
            }
            // the last ones still come back at their gaps
            while (pending.Count > 0) { slot = Math.Max(slot, pending.Min(p => p.Due)); Due(); }
            l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
            return l;
        }

        /// <summary>Building a phrase up from its end, as heard in the drill: "¿Cómo estás?" -> "tás", "estás",
        /// "¿Cómo estás?". Only for phrases of 4 syllables or more; the last item is always the whole phrase.</summary>
        public static List<string> Backchain(string phrase)
        {
            var r = new List<string>();
            var words = (phrase ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var bare = words.Select(Answers.Bare).ToList();
            int syll = bare.Sum(w => Syllables(w).Count);
            if (syll < 4 || words.Count == 0) { r.Add(phrase); return r; }
            var last = Syllables(bare[bare.Count - 1]);
            if (last.Count >= 2 && words.Count <= 3) r.Add(last[last.Count - 1].ToLowerInvariant());
            int n = words.Count;
            var sizes = new List<int> { 1 };
            if (n <= 4) for (int k = 2; k < n; k++) sizes.Add(k);
            else { sizes.Add(2); sizes.Add(n / 2 + 1); }
            foreach (int k in sizes.Distinct().Where(k => k < n))
            {
                string part = string.Join(" ", bare.Skip(n - k)).ToLowerInvariant();
                if (part.Length > 0 && !r.Contains(part)) r.Add(part);
            }
            r.Add(phrase);
            return r;
        }

        static bool IsVowel(char c) { return "aeiouáéíóúü".IndexOf(char.ToLowerInvariant(c)) >= 0; }
        static bool IsStrong(char c) { return "aeoáéóíú".IndexOf(char.ToLowerInvariant(c)) >= 0; }
        static readonly string[] Clusters = { "pl", "pr", "bl", "br", "cl", "cr", "dr", "fl", "fr", "gl", "gr", "tr", "kr", "tl" };

        /// <summary>A Spanish word split into syllables (good enough for building phrases up: es-tás, gra-cias, ma-ña-na).</summary>
        public static List<string> Syllables(string word)
        {
            var w = word ?? "";
            var res = new List<string>();
            if (w.Length == 0) return res;
            // letters, with ch / ll / rr and the silent u of que / qui / gue / gui kept to their consonant
            var units = new List<string>();
            var vowelUnit = new List<bool>();
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i], lc = char.ToLowerInvariant(c);
                string two = i + 1 < w.Length ? w.Substring(i, 2).ToLowerInvariant() : "";
                if (two == "ch" || two == "ll" || two == "rr") { units.Add(w.Substring(i, 2)); vowelUnit.Add(false); i++; continue; }
                if ((lc == 'q' || lc == 'g') && i + 2 < w.Length && char.ToLowerInvariant(w[i + 1]) == 'u' && "eiéí".IndexOf(char.ToLowerInvariant(w[i + 2])) >= 0)
                { units.Add(w.Substring(i, 2)); vowelUnit.Add(false); i++; continue; }
                bool v = IsVowel(c) || (lc == 'y' && (i == w.Length - 1 || !IsVowel(w[i + 1])) && i > 0);
                units.Add(c.ToString()); vowelUnit.Add(v);
            }
            // nuclei: runs of vowels, split between two strong ones
            var nuclei = new List<(int Start, int End)>();
            for (int i = 0; i < units.Count; i++)
            {
                if (!vowelUnit[i]) continue;
                int s = i;
                while (i + 1 < units.Count && vowelUnit[i + 1] && !(IsStrong(units[i][0]) && IsStrong(units[i + 1][0]))) i++;
                nuclei.Add((s, i));
            }
            if (nuclei.Count <= 1) { res.Add(w); return res; }
            var cuts = new List<int>();   // unit index where each syllable after the first starts
            for (int k = 0; k + 1 < nuclei.Count; k++)
            {
                int a = nuclei[k].End + 1, b = nuclei[k + 1].Start;   // consonants a..b-1
                int n = b - a;
                int cut;
                if (n <= 1) cut = a;
                else
                {
                    string lastTwo = (units[b - 2] + units[b - 1]).ToLowerInvariant();
                    bool together = units[b - 2].Length == 1 && units[b - 1].Length == 1 && Clusters.Contains(lastTwo);
                    if (n == 2) cut = together ? a : a + 1;
                    else if (n == 3) cut = together ? a + 1 : a + 2;
                    else cut = a + 2;
                }
                cuts.Add(cut);
            }
            int from = 0;
            foreach (int c in cuts) { res.Add(string.Concat(units.Skip(from).Take(c - from))); from = c; }
            res.Add(string.Concat(units.Skip(from)));
            return res;
        }

        // ------------------------------------------------------------------ stories in the car
        /// <summary>A story to listen to: its title, then each line in Spanish, a pause, and the English (or only the
        /// Spanish with english false: "read it all" in the reader). Items are the lines.</summary>
        public static Lesson BuildStory(LangStory s, string lang, double pauseScale = 1, bool english = true)
        {
            var l = new Lesson { Title = s.Title, Style = "story" };
            Func<int, int> ms = x => (int)Math.Round(x * Math.Clamp(pauseScale, 0.5, 3));
            if (english)
            {
                l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "A story: " + s.TitleEn });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(600) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = s.Title });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1200) });
            }
            for (int k = 0; k < s.Lines.Count; k++)
            {
                var line = s.Lines[k];
                l.Steps.Add(new LessonStep { Kind = StepKind.Item, Index = k });
                l.Items.Add(new LangItem { Target = line.Target, English = line.English, Id = LangCourse.ItemId(line.Target, line.English) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = line.Target });
                if (english)
                {
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1600) });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = line.English });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1000) });
                }
                else l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(600) });
            }
            if (english) l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
            return l;
        }

        /// <summary>Builds a lesson. pauseScale stretches every wait (1 = normal; 1.5 for little ones or a busy road).</summary>
        public static Lesson Build(string title, string lang, IList<LangItem> items, string style, bool intro, double pauseScale = 1, string introText = null)
        {
            if (style == "drill") return BuildDrill(title, lang, items, pauseScale, intro ? introText : null);
            var l = new Lesson { Title = title, Style = Styles.Contains(style) ? style : "learn" };
            l.Items.AddRange(items);
            Func<int, int> ms = x => (int)Math.Round(x * Math.Clamp(pauseScale, 0.5, 3));
            if (intro)
            {
                l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = introText ?? title });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(900) });
            }
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                l.Steps.Add(new LessonStep { Kind = StepKind.Item, Index = k });
                string en = Spoken(it.English);
                switch (l.Style)
                {
                    case "recall":
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = en });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(3200) });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.2, Ms = ms(1300) });
                        break;
                    case "listen":
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(2400) });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = en });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1200) });
                        break;
                    default:
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = en });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(700) });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.2, Ms = ms(1300) });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = it.Target, Rate = 0.8 });
                        l.Steps.Add(new LessonStep { Kind = StepKind.Echo, Factor = 1.2, Ms = ms(1500) });
                        break;
                }
            }
            l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
            return l;
        }

        /// <summary>The English as it should be read aloud: "25 (twenty-five)" is read once, not twice.</summary>
        public static string Spoken(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            int a = en.IndexOf(" (", StringComparison.Ordinal);
            if (a > 0 && en.EndsWith(")") && char.IsDigit(en[0])) return en.Substring(a + 2, en.Length - a - 3);
            return en;
        }

        /// <summary>A shuffled mix for review: due items first, then others met before (at most n).</summary>
        public static List<LangItem> ReviewMix(LangCourse c, LangProgress p, int n, Random rnd)
        {
            var due = p.DueItems(c);
            var rest = c.AllItems.Where(i => p.Seen(c.Language, i.Id) && !due.Contains(i)).OrderBy(i => p.Box(c.Language, i.Id)).ThenBy(_ => rnd.Next()).ToList();
            var pick = due.Take(n).Concat(rest).Take(n).ToList();
            for (int i = pick.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = pick[i]; pick[i] = pick[j]; pick[j] = t; }
            return pick;
        }
    }
}
