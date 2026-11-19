// Learning Lab - Languages: cartoon conversations (Andrew, 2026-11-13: "a cartoon with a conversation that I have to
// try to understand, make the cartoons silly (animated in nature), with about 20-30 seconds of material and I have to
// try to tell what's going on"). <course>-cartoons.json beside the course: a scene, two or three silly characters, the
// lines they say (each with something they do while saying it), the word meanings, a "What's going on?" question and a
// few more. Everything is drawn and voiced on the device: the characters are drawn in code, the voices are the
// device's own Spanish voices with each character's pitch. Problems() holds every file to the rules below; the timing
// (CartoonTimeline) comes from the real length of each spoken line, or from an estimate when there is no voice.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LearningLab.Languages
{
    public class CartoonActor
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        /// <summary>What it is (Kinds): drawn by the app.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; }
        /// <summary>Its main colour, #RRGGBB.</summary>
        [JsonPropertyName("color")] public string Color { get; set; }
        /// <summary>Which of the device's Spanish voices (0 = the chosen one, 1 = another one when the device has it).</summary>
        [JsonPropertyName("voice")] public int Voice { get; set; }
        /// <summary>Higher or lower than the voice (0.8-1.3; above 1 is higher and a little faster).</summary>
        [JsonPropertyName("pitch")] public double Pitch { get; set; } = 1;

        public static readonly string[] Kinds = { "cat", "dog", "frog", "chicken", "robot", "cactus" };
    }

    public class CartoonLine
    {
        [JsonPropertyName("who")] public string Who { get; set; }
        [JsonPropertyName("es")] public string Target { get; set; }
        [JsonPropertyName("en")] public string English { get; set; }
        /// <summary>What the speaker does while saying it (Acts; "talk" when missing).</summary>
        [JsonPropertyName("act")] public string Act { get; set; }

        public static readonly string[] Acts = { "talk", "wave", "jump", "spin", "shake", "laugh", "surprise", "cry", "dance", "fall", "run", "eat", "sleep", "think", "point" };
    }

    public class LangCartoon
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("titleEn")] public string TitleEn { get; set; }
        [JsonPropertyName("after")] public string After { get; set; }
        [JsonPropertyName("scene")] public string Scene { get; set; }
        [JsonPropertyName("cast")] public List<CartoonActor> Cast { get; set; } = new List<CartoonActor>();
        [JsonPropertyName("lines")] public List<CartoonLine> Lines { get; set; } = new List<CartoonLine>();
        [JsonPropertyName("words")] public Dictionary<string, string> Words { get; set; } = new Dictionary<string, string>();
        /// <summary>"What's going on?": the main question, asked first.</summary>
        [JsonPropertyName("gist")] public StoryQuestion Gist { get; set; }
        [JsonPropertyName("questions")] public List<StoryQuestion> Questions { get; set; } = new List<StoryQuestion>();

        public static readonly string[] Scenes = { "kitchen", "park", "beach", "space", "market", "classroom", "desert" };
        public const double MinSeconds = 18, MaxSeconds = 34;

        public CartoonActor Actor(string id) { return Cast.FirstOrDefault(a => a.Id == id); }

        /// <summary>The gist question first, then the others.</summary>
        public List<StoryQuestion> AllQuestions()
        {
            var all = new List<StoryQuestion>();
            if (Gist != null) all.Add(Gist);
            all.AddRange(Questions ?? new List<StoryQuestion>());
            return all;
        }

        /// <summary>The cartoon as a story (its lines and words), so tapping a word works as in Stories.</summary>
        public LangStory AsStory()
        {
            return new LangStory { Id = Id, Level = Level, Title = Title, TitleEn = TitleEn, After = After, Words = Words ?? new Dictionary<string, string>(), Lines = Lines.Select(l => new LangLine { Target = l.Target, English = l.English }).ToList() };
        }

        /// <summary>About how long a voice takes to say the line, in seconds (no voice: the timing and the checks).
        /// Spanish at an easy pace is about 4.5 syllables a second; a syllable is counted as a group of vowels.</summary>
        public static double Estimate(string text)
        {
            int syl = 0; bool inV = false;
            foreach (char ch in (text ?? "").ToLowerInvariant())
            {
                bool v = "aeiouáéíóúü".IndexOf(ch) >= 0;
                if (v && !inV) syl++;
                inV = v;
            }
            return 0.25 + Math.Max(1, syl) / 4.5;
        }

        /// <summary>The gap after a line, in seconds: a little longer after a fall, a laugh or a surprise.</summary>
        public static double GapAfter(CartoonLine l)
        {
            switch (l == null ? "" : l.Act ?? "")
            {
                case "fall": case "laugh": case "surprise": case "spin": case "run": return 0.9;
                default: return 0.45;
            }
        }

        /// <summary>The estimated length of the whole cartoon (what the 20-30 second rule is checked against).</summary>
        public double EstimatedSeconds()
        {
            double t = 0.6;
            foreach (var l in Lines) t += Estimate(l.Target) + GapAfter(l);
            return t;
        }
    }

    /// <summary>When each line is said: Start[i] to End[i] seconds, the whole cartoon lasting Duration.</summary>
    public sealed class CartoonTimeline
    {
        public double[] Start = new double[0], End = new double[0];
        public double Duration;
        public const double Lead = 0.6;

        /// <summary>From each line's spoken length in seconds (null: estimated), with the cartoon's gaps.</summary>
        public static CartoonTimeline From(LangCartoon c, IList<double> lengths)
        {
            var t = new CartoonTimeline { Start = new double[c.Lines.Count], End = new double[c.Lines.Count] };
            double at = Lead;
            for (int i = 0; i < c.Lines.Count; i++)
            {
                double len = lengths != null && i < lengths.Count && lengths[i] > 0 ? lengths[i] : LangCartoon.Estimate(c.Lines[i].Target);
                t.Start[i] = at; t.End[i] = at + len;
                at += len + LangCartoon.GapAfter(c.Lines[i]);
            }
            t.Duration = at + 0.4;
            return t;
        }

        /// <summary>From the line marks of the cartoon's audio (each line's start); a line ends where the next gap begins.</summary>
        public static CartoonTimeline FromMarks(LangCartoon c, double[] starts, double duration)
        {
            var t = new CartoonTimeline { Start = new double[c.Lines.Count], End = new double[c.Lines.Count], Duration = duration };
            for (int i = 0; i < c.Lines.Count; i++)
            {
                t.Start[i] = i < starts.Length ? starts[i] : (i > 0 ? t.End[i - 1] : Lead);
                double next = i + 1 < starts.Length ? starts[i + 1] : duration;
                t.End[i] = Math.Max(t.Start[i] + 0.2, next - LangCartoon.GapAfter(c.Lines[i]));
            }
            return t;
        }

        /// <summary>The line being said at that time (-1 between lines).</summary>
        public int Speaking(double seconds)
        {
            for (int i = 0; i < Start.Length; i++) if (seconds >= Start[i] && seconds < End[i]) return i;
            return -1;
        }

        /// <summary>The last line that has started by that time (-1 before the first): its act keeps playing in the gap.</summary>
        public int LastStarted(double seconds)
        {
            int k = -1;
            for (int i = 0; i < Start.Length; i++) if (Start[i] <= seconds) k = i;
            return k;
        }
    }

    public sealed partial class Lesson
    {
        /// <summary>A cartoon as spoken steps. For the cartoon page (car = false): only its lines in each character's
        /// voice, with the gaps the animation needs. For the car (listening only): an English opening, the lines, the
        /// lines once more, then "What's going on?" with time to think and the answer in English.</summary>
        public static Lesson BuildCartoon(LangCartoon c, string lang, bool car, double pauseScale = 1)
        {
            var l = new Lesson { Title = c.Title, Style = "cartoon" };
            Func<int, int> ms = x => (int)Math.Round(x * Math.Clamp(pauseScale, 0.5, 3));
            if (car)
            {
                l.Steps.Add(new LessonStep { Kind = StepKind.Chime });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "A cartoon: " + c.TitleEn + ". Listen, and try to work out what's going on." });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1200) });
            }
            else l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = (int)(CartoonTimeline.Lead * 1000) });
            for (int round = 0; round < (car ? 2 : 1); round++)
            {
                if (round == 1)
                {
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1500) });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = "Once more." });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1000) });
                }
                for (int k = 0; k < c.Lines.Count; k++)
                {
                    var line = c.Lines[k];
                    var who = c.Actor(line.Who);
                    if (round == 0)
                    {
                        l.Steps.Add(new LessonStep { Kind = StepKind.Item, Index = k });
                        l.Items.Add(new LangItem { Target = line.Target, English = line.English, Id = LangCourse.ItemId(line.Target, line.English) });
                    }
                    l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = lang, Text = line.Target, Voice = who == null ? 0 : who.Voice, Pitch = who == null ? 1 : who.Pitch });
                    l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = (int)Math.Round(LangCartoon.GapAfter(line) * 1000 * (car ? Math.Clamp(pauseScale, 0.5, 3) : 1)) });
                }
            }
            if (car && c.Gist != null)
            {
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1200) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = c.Gist.Question ?? "What's going on?" });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(5000) });
                l.Steps.Add(new LessonStep { Kind = StepKind.Say, Lang = "en", Text = c.Gist.Answer });
                l.Steps.Add(new LessonStep { Kind = StepKind.Wait, Ms = ms(1500) });
            }
            return l;
        }
    }

    public partial class LangContent
    {
        public List<LangCartoon> Cartoons { get; } = new List<LangCartoon>();

        class CartoonFileJson { [JsonPropertyName("format")] public int Format { get; set; } [JsonPropertyName("common")] public Dictionary<string, string> Common { get; set; } [JsonPropertyName("cartoons")] public List<LangCartoon> Cartoons { get; set; } }

        void AddCartoons(string text)
        {
            var f = System.Text.Json.JsonSerializer.Deserialize<CartoonFileJson>(text, Options);
            if (f == null || f.Cartoons == null) throw new FormatException("no cartoons in it");
            foreach (var c in f.Cartoons)
            {
                if (c == null) continue;
                c.Cast ??= new List<CartoonActor>();
                c.Cast.RemoveAll(a => a == null);
                c.Lines ??= new List<CartoonLine>();
                c.Lines.RemoveAll(l => l == null);
                c.Words ??= new Dictionary<string, string>();
                c.Questions ??= new List<StoryQuestion>();
                c.Questions.RemoveAll(q => q == null);
                foreach (var q in c.AllQuestions()) q.Wrong ??= new List<string>();
                // the file's "common" words and the characters' names, for words the cartoon doesn't explain itself
                var words = new Dictionary<string, string>(c.Words, StringComparer.OrdinalIgnoreCase);
                foreach (var a in c.Cast) foreach (var part in (a.Name ?? "").Split(' ')) if (part.Length > 0 && !words.ContainsKey(part)) words[part] = a.Name + " (a name)";
                if (f.Common != null) foreach (var kv in f.Common) if (!words.ContainsKey(kv.Key)) words[kv.Key] = kv.Value;
                c.Words = words;
                Cartoons.Add(c);
            }
        }

        public LangCartoon Cartoon(string id) { return Cartoons.FirstOrDefault(c => c.Id == id); }

        void CartoonProblems(LangCourse course, List<string> p)
        {
            var ids = new HashSet<string>();
            foreach (var c in Cartoons)
            {
                string w = "cartoon " + (c.Id ?? "(no id)");
                if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) p.Add(w + ": id missing or repeated");
                if (c.Level < 1 || c.Level > 4) p.Add(w + ": level " + c.Level);
                if (string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.TitleEn)) p.Add(w + ": title or titleEn missing");
                if (course.Unit(c.After) == null) p.Add(w + ": \"after\" unit " + c.After + " is not in the course");
                if (!LangCartoon.Scenes.Contains(c.Scene)) p.Add(w + ": scene " + c.Scene + " (" + string.Join(", ", LangCartoon.Scenes) + ")");
                if (c.Cast.Count < 2 || c.Cast.Count > 3) p.Add(w + ": " + c.Cast.Count + " characters (2-3)");
                var castIds = new HashSet<string>();
                foreach (var a in c.Cast)
                {
                    if (string.IsNullOrWhiteSpace(a.Id) || !castIds.Add(a.Id)) p.Add(w + ": a character's id is missing or repeated");
                    if (string.IsNullOrWhiteSpace(a.Name)) p.Add(w + ": character " + a.Id + " has no name");
                    if (!CartoonActor.Kinds.Contains(a.Kind)) p.Add(w + ": character " + a.Id + " kind " + a.Kind + " (" + string.Join(", ", CartoonActor.Kinds) + ")");
                    if (!IsColor(a.Color)) p.Add(w + ": character " + a.Id + " colour " + a.Color + " (#RRGGBB)");
                    if (a.Voice < 0 || a.Voice > 1) p.Add(w + ": character " + a.Id + " voice " + a.Voice + " (0 or 1)");
                    if (a.Pitch < 0.8 || a.Pitch > 1.3) p.Add(w + ": character " + a.Id + " pitch " + a.Pitch + " (0.8-1.3)");
                }
                if (c.Lines.Count < 6 || c.Lines.Count > 14) p.Add(w + ": " + c.Lines.Count + " lines (6-14)");
                foreach (var l in c.Lines)
                {
                    if (string.IsNullOrWhiteSpace(l.Target) || string.IsNullOrWhiteSpace(l.English)) p.Add(w + ": a line without es or en");
                    if (c.Actor(l.Who) == null) p.Add(w + ": a line by \"" + l.Who + "\", who isn't in the cast");
                    if (l.Act != null && !CartoonLine.Acts.Contains(l.Act)) p.Add(w + ": act " + l.Act + " (" + string.Join(", ", CartoonLine.Acts) + ")");
                }
                foreach (var a in c.Cast) if (!c.Lines.Any(l => l.Who == a.Id)) p.Add(w + ": " + a.Name + " never speaks");
                foreach (var t in SpanishMarks(c.Lines.Select(l => l.Target))) p.Add(w + ": " + t);
                double secs = c.EstimatedSeconds();
                if (secs < LangCartoon.MinSeconds || secs > LangCartoon.MaxSeconds) p.Add(w + ": about " + Math.Round(secs) + " seconds (" + LangCartoon.MinSeconds + "-" + LangCartoon.MaxSeconds + ")");
                if (c.Gist == null) p.Add(w + ": no \"What's going on?\" question (gist)");
                if (c.Questions.Count < 1 || c.Questions.Count > 3) p.Add(w + ": " + c.Questions.Count + " more questions (1-3)");
                foreach (var q in c.AllQuestions())
                {
                    if (string.IsNullOrWhiteSpace(q.Question) || string.IsNullOrWhiteSpace(q.Answer) || q.Wrong.Count < 2) { p.Add(w + ": a question without q, answer or two wrong answers"); continue; }
                    var all = new List<string> { q.Answer }; all.AddRange(q.Wrong);
                    var norm = all.Select(x => Answers.Normalize(x ?? "", true)).ToList();
                    if (norm.Distinct().Count() != norm.Count || norm.Any(x => x.Length == 0)) p.Add(w + ": choices repeat in \"" + q.Question + "\"");
                }
                foreach (var kv in c.Words) if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) p.Add(w + ": an empty word meaning");
            }
        }

        static bool IsColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 7 || s[0] != '#') return false;
            return s.Skip(1).All(ch => Uri.IsHexDigit(ch));
        }
    }
}
