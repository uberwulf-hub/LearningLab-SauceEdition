// Learning Lab - Languages: lessons, stories and verbs (docs\LANGUAGES.md has the formats). They sit beside a course
// file: <course>-lessons*.json (every one is read), <course>-stories.json, <course>-verbs.json and <course>-cartoons.json (Cartoons.cs). Any of them may be
// missing (the pages then offer what there is). A file that can't be read is left out and named in LoadProblems, so a
// half-written file never stops the app. Problems(course) is what the checks run on every file.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LearningLab.Languages
{
    public class LangLine
    {
        [JsonPropertyName("who")] public string Who { get; set; }
        [JsonPropertyName("es")] public string Target { get; set; }
        [JsonPropertyName("en")] public string English { get; set; }
    }

    public class LangTip
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; }
    }

    public class LangExercise
    {
        public static readonly string[] Kinds = { "choose", "listen", "gap", "build", "type", "match", "say", "reply" };

        [JsonPropertyName("kind")] public string Kind { get; set; }
        [JsonPropertyName("prompt")] public string Prompt { get; set; }
        [JsonPropertyName("es")] public string Target { get; set; }
        [JsonPropertyName("en")] public string English { get; set; }
        [JsonPropertyName("answer")] public string Answer { get; set; }
        [JsonPropertyName("wrong")] public List<string> Wrong { get; set; } = new List<string>();
        [JsonPropertyName("extra")] public List<string> Extra { get; set; } = new List<string>();
        [JsonPropertyName("accept")] public List<string> Accept { get; set; } = new List<string>();
        [JsonPropertyName("pairs")] public List<List<string>> Pairs { get; set; } = new List<List<string>>();

        /// <summary>The answer and the wrong ones, shuffled (choose, listen, gap, reply).</summary>
        public List<string> Choices(Random rnd)
        {
            var all = new List<string> { Answer };
            all.AddRange(Wrong ?? new List<string>());
            for (int i = all.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = all[i]; all[i] = all[j]; all[j] = t; }
            return all;
        }

        /// <summary>The words to put in order (build): the sentence's words without ¿ ¡ and end marks.</summary>
        public List<string> BuildWords() { return Answers.Words(Target); }

        /// <summary>The build tiles: the sentence's words and the extra ones, shuffled.</summary>
        public List<string> Tiles(Random rnd)
        {
            var all = BuildWords();
            foreach (var x in Extra ?? new List<string>()) all.Add(Answers.Bare(x));
            for (int i = all.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = all[i]; all[i] = all[j]; all[j] = t; }
            return all;
        }

        /// <summary>The full Spanish sentence of a gap exercise.</summary>
        public string Filled => Target == null || Answer == null ? Target : Target.Replace("___", Answer);

        /// <summary>The Spanish this exercise practises, to find the course's items (each counts as practised).</summary>
        public IEnumerable<string> SpanishTexts()
        {
            switch (Kind)
            {
                case "choose": yield return Answer; break;
                case "gap": yield return Filled; break;
                case "reply": yield return Answer; yield return Target; break;
                case "match": foreach (var p in Pairs ?? new List<List<string>>()) if (p != null && p.Count > 0) yield return p[0]; break;
                default: yield return Target; break;
            }
        }

        /// <summary>The Spanish to say aloud after the answer (the right answer, as the plan says).</summary>
        public string SayAfter
        {
            get
            {
                switch (Kind)
                {
                    case "choose": case "reply": return Answer;
                    case "gap": return Filled;
                    case "match": return null;
                    default: return Target;
                }
            }
        }
    }

    public class LangLesson
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("unit")] public string Unit { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("goal")] public string Goal { get; set; }
        [JsonPropertyName("minutes")] public int Minutes { get; set; }
        [JsonPropertyName("new")] public List<string> New { get; set; } = new List<string>();
        [JsonPropertyName("dialogue")] public List<LangLine> Dialogue { get; set; } = new List<LangLine>();
        [JsonPropertyName("tip")] public LangTip Tip { get; set; }
        [JsonPropertyName("exercises")] public List<LangExercise> Exercises { get; set; } = new List<LangExercise>();
        [JsonIgnore] public string FileName { get; set; }
        /// <summary>Where it sits in its unit (1, 2, ...).</summary>
        [JsonIgnore] public int Number { get; set; }
    }

    public class StoryQuestion
    {
        [JsonPropertyName("q")] public string Question { get; set; }
        [JsonPropertyName("answer")] public string Answer { get; set; }
        [JsonPropertyName("wrong")] public List<string> Wrong { get; set; } = new List<string>();
        public List<string> Choices(Random rnd)
        {
            var all = new List<string> { Answer };
            all.AddRange(Wrong ?? new List<string>());
            for (int i = all.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = all[i]; all[i] = all[j]; all[j] = t; }
            return all;
        }
    }

    public class LangStory
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("titleEn")] public string TitleEn { get; set; }
        [JsonPropertyName("after")] public string After { get; set; }
        [JsonPropertyName("lines")] public List<LangLine> Lines { get; set; } = new List<LangLine>();
        [JsonPropertyName("words")] public Dictionary<string, string> Words { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("questions")] public List<StoryQuestion> Questions { get; set; } = new List<StoryQuestion>();

        /// <summary>A line's words as they are shown (each tappable).</summary>
        public static List<string> LineWords(string line) { return (line ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }

        /// <summary>What a tapped word means: the story's own "words" (also two-word entries such as "se ríe" or
        /// "me llamo"), else a course phrase that is just that word (or "el / la / un ... word"), else null.</summary>
        public string Meaning(LangCourse c, IList<string> words, int k, out string shown)
        {
            shown = k >= 0 && k < words.Count ? Answers.Bare(words[k]) : "";
            if (shown.Length == 0) return null;
            string w = shown.ToLowerInvariant();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Words ?? new Dictionary<string, string>()) if (!string.IsNullOrWhiteSpace(kv.Key)) dict[kv.Key.Trim()] = kv.Value;
            string prev = k > 0 ? Answers.Bare(words[k - 1]).ToLowerInvariant() : null;
            string next = k + 1 < words.Count ? Answers.Bare(words[k + 1]).ToLowerInvariant() : null;
            string m;
            if (dict.TryGetValue(w, out m)) return m;
            if (prev != null && dict.TryGetValue(prev + " " + w, out m)) { shown = prev + " " + shown; return m; }
            if (next != null && dict.TryGetValue(w + " " + next, out m)) { shown = shown + " " + next; return m; }
            if (c != null)
            {
                string[] arts = { "el", "la", "los", "las", "un", "una" };
                foreach (var it in c.AllItems)
                {
                    var iw = Answers.Words(it.Target).Select(x => x.ToLowerInvariant()).ToList();
                    if (iw.Count == 1 && iw[0] == w) return it.English;
                    if (iw.Count == 2 && arts.Contains(iw[0]) && iw[1] == w) return it.English;
                }
                // a two-word phrase of the course ("Buenos días", "Muy bien")
                foreach (var it in c.AllItems)
                {
                    var iw = Answers.Words(it.Target).Select(x => x.ToLowerInvariant()).ToList();
                    if (iw.Count != 2) continue;
                    if (iw[0] == w && iw[1] == next) { shown = shown + " " + next; return it.English; }
                    if (iw[1] == w && iw[0] == prev) { shown = prev + " " + shown; return it.English; }
                }
            }
            return null;
        }
    }

    /// <summary>Lessons, stories and verbs of one course.</summary>
    public partial class LangContent
    {
        public List<LangLesson> Lessons { get; } = new List<LangLesson>();
        public List<LangStory> Stories { get; } = new List<LangStory>();
        public List<LangVerb> Verbs { get; } = new List<LangVerb>();
        /// <summary>Files that couldn't be read ("name: why").</summary>
        public List<string> LoadProblems { get; } = new List<string>();
        /// <summary>The names of the files read.</summary>
        public List<string> Files { get; } = new List<string>();

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>Reads every file of the course (name, text); orders lessons by the course's units.</summary>
        public static LangContent Load(string courseId, IEnumerable<KeyValuePair<string, string>> files, LangCourse course)
        {
            var c = new LangContent();
            foreach (var f in files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = f.Key;
                string text = (f.Value ?? "").TrimStart('﻿');
                try
                {
                    if (name.StartsWith(courseId + "-lessons", StringComparison.OrdinalIgnoreCase)) c.AddLessons(name, text);
                    else if (name.Equals(courseId + "-stories.json", StringComparison.OrdinalIgnoreCase)) c.AddStories(text);
                    else if (name.Equals(courseId + "-verbs.json", StringComparison.OrdinalIgnoreCase)) c.Verbs.AddRange(LangVerb.ParseFile(text));
                    else if (name.Equals(courseId + "-cartoons.json", StringComparison.OrdinalIgnoreCase)) c.AddCartoons(text);
                    else continue;
                    c.Files.Add(name);
                }
                catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException)
                {
                    c.LoadProblems.Add(name + ": " + ex.Message);
                }
            }
            if (course != null)
            {
                var order = course.Units.ToDictionary(u => u.Id, u => u.Number);
                var sorted = c.Lessons.Select((l, i) => (l, i)).OrderBy(x => x.l.Unit != null && order.ContainsKey(x.l.Unit) ? order[x.l.Unit] : 999).ThenBy(x => x.i).Select(x => x.l).ToList();
                c.Lessons.Clear(); c.Lessons.AddRange(sorted);
            }
            foreach (var g in c.Lessons.GroupBy(l => l.Unit ?? "")) { int n = 0; foreach (var l in g) l.Number = ++n; }
            return c;
        }

        class LessonFileJson { [JsonPropertyName("format")] public int Format { get; set; } [JsonPropertyName("lessons")] public List<LangLesson> Lessons { get; set; } }
        class StoryFileJson { [JsonPropertyName("format")] public int Format { get; set; } [JsonPropertyName("stories")] public List<LangStory> Stories { get; set; } }

        void AddLessons(string name, string text)
        {
            var f = JsonSerializer.Deserialize<LessonFileJson>(text, Options);
            if (f == null || f.Lessons == null) throw new FormatException("no lessons in it");
            foreach (var l in f.Lessons)
            {
                if (l == null) continue;
                l.FileName = name;
                l.New ??= new List<string>();
                l.Dialogue ??= new List<LangLine>();
                l.Exercises ??= new List<LangExercise>();
                foreach (var e in l.Exercises.Where(e => e != null)) { e.Wrong ??= new List<string>(); e.Extra ??= new List<string>(); e.Accept ??= new List<string>(); e.Pairs ??= new List<List<string>>(); }
                l.Exercises.RemoveAll(e => e == null);
                Lessons.Add(l);
            }
        }

        void AddStories(string text)
        {
            var f = JsonSerializer.Deserialize<StoryFileJson>(text, Options);
            if (f == null || f.Stories == null) throw new FormatException("no stories in it");
            foreach (var s in f.Stories)
            {
                if (s == null) continue;
                s.Lines ??= new List<LangLine>();
                s.Words ??= new Dictionary<string, string>();
                s.Questions ??= new List<StoryQuestion>();
                foreach (var q in s.Questions) if (q != null) q.Wrong ??= new List<string>();
                s.Questions.RemoveAll(q => q == null);
                Stories.Add(s);
            }
        }

        public LangLesson Lesson(string id) { return Lessons.FirstOrDefault(l => l.Id == id); }
        public List<LangLesson> LessonsOf(string unit) { return Lessons.Where(l => l.Unit == unit).ToList(); }
        public LangVerb Verb(string infinitive) { return Verbs.FirstOrDefault(v => v.Infinitive == infinitive); }

        /// <summary>The course's items that a lesson brings (its "new" list), in order; texts not found are left out.</summary>
        public static List<LangItem> NewItems(LangCourse c, LangLesson l)
        {
            var u = c.Unit(l.Unit);
            if (u == null) return new List<LangItem>();
            return l.New.Select(t => u.Items.FirstOrDefault(i => i.Target == t)).Where(i => i != null).ToList();
        }

        // ------------------------------------------------------------------ the checks
        /// <summary>Problems with the lessons, stories and verbs (empty when all is well).</summary>
        public List<string> Problems(LangCourse c)
        {
            var p = new List<string>();
            p.AddRange(LoadProblems.Select(x => "can't read " + x));
            LessonProblems(c, p);
            StoryProblems(c, p);
            CartoonProblems(c, p);
            VerbProblems(p);
            return p;
        }

        void LessonProblems(LangCourse c, List<string> p)
        {
            var ids = new HashSet<string>();
            var newIn = new Dictionary<string, string>();   // item id -> lesson id
            foreach (var l in Lessons)
            {
                string w = "lesson " + (l.Id ?? "(no id)");
                if (string.IsNullOrWhiteSpace(l.Id) || !ids.Add(l.Id)) { p.Add(w + ": id missing or repeated"); }
                var u = c.Unit(l.Unit);
                if (u == null) { p.Add(w + ": no unit \"" + l.Unit + "\" in the course"); continue; }
                if (string.IsNullOrWhiteSpace(l.Title) || string.IsNullOrWhiteSpace(l.Goal)) p.Add(w + ": title or goal missing");
                if (l.New.Count == 0) p.Add(w + ": no new items");
                foreach (var t in l.New)
                {
                    var it = u.Items.FirstOrDefault(i => i.Target == t);
                    if (it == null) { p.Add(w + ": \"" + t + "\" is not an item of unit " + u.Id); continue; }
                    string other;
                    if (newIn.TryGetValue(it.Id, out other)) p.Add(w + ": \"" + t + "\" is new in " + other + " too");
                    else newIn[it.Id] = l.Id;
                }
                foreach (var d in l.Dialogue)
                    if (d == null || string.IsNullOrWhiteSpace(d.Who) || string.IsNullOrWhiteSpace(d.Target) || string.IsNullOrWhiteSpace(d.English)) p.Add(w + ": a dialogue line without who, es or en");
                if (l.Dialogue.Count < 2) p.Add(w + ": dialogue has fewer than 2 lines");
                if (l.Tip == null || string.IsNullOrWhiteSpace(l.Tip.Title) || string.IsNullOrWhiteSpace(l.Tip.Text)) p.Add(w + ": tip title or text missing");
                if (l.Exercises.Count < 8 || l.Exercises.Count > 14) p.Add(w + ": " + l.Exercises.Count + " exercises (8-14)");
                int k = 0;
                foreach (var e in l.Exercises) { k++; foreach (var x in ExerciseProblems(e)) p.Add(w + " exercise " + k + " (" + e.Kind + "): " + x); }
                foreach (var t in SpanishMarks(l.Dialogue.Select(d => d?.Target))) p.Add(w + ": " + t);
            }
            // every unit with lessons: each of its items is new in exactly one lesson
            foreach (var u in c.Units.Where(u => Lessons.Any(l => l.Unit == u.Id)))
                foreach (var it in u.Items.Where(i => !newIn.ContainsKey(i.Id)))
                    p.Add("unit " + u.Id + ": \"" + it.Target + "\" is not new in any lesson");
        }

        static IEnumerable<string> SpanishMarks(IEnumerable<string> texts)
        {
            foreach (var t in texts.Where(x => x != null))
            {
                if (t.EndsWith("?") && !t.Contains('¿')) yield return "\"" + t + "\" has no opening ¿";
                if (t.EndsWith("!") && !t.Contains('¡')) yield return "\"" + t + "\" has no opening ¡";
            }
        }

        /// <summary>The rules of one exercise kind (docs\LANGUAGES.md).</summary>
        public static List<string> ExerciseProblems(LangExercise e)
        {
            var p = new List<string>();
            bool Empty(string s) => string.IsNullOrWhiteSpace(s);
            void Choices()
            {
                if (Empty(e.Answer)) { p.Add("no answer"); return; }
                if (e.Wrong.Count < 1) p.Add("no wrong choices");
                var all = new List<string> { e.Answer }; all.AddRange(e.Wrong);
                if (all.Any(Empty)) p.Add("an empty choice");
                var norm = all.Where(x => !Empty(x)).Select(x => Answers.Normalize(x, true)).ToList();
                if (norm.Distinct().Count() != norm.Count) p.Add("choices repeat (or a wrong one is the answer)");
            }
            switch (e.Kind)
            {
                case "choose":
                    if (Empty(e.Prompt)) p.Add("no prompt");
                    Choices(); break;
                case "listen":
                    if (Empty(e.Target)) p.Add("no es to hear");
                    Choices(); break;
                case "gap":
                    if (Empty(e.Target)) { p.Add("no es"); break; }
                    int n = 0, at = 0;
                    while ((at = e.Target.IndexOf("___", at, StringComparison.Ordinal)) >= 0) { n++; at += 3; }
                    if (n != 1) p.Add("es must have exactly one ___ (has " + n + ")");
                    if (e.Target.Contains("____")) p.Add("es has a gap longer than ___");
                    Choices(); break;
                case "build":
                    if (Empty(e.Target) || Empty(e.English)) { p.Add("en or es missing"); break; }
                    var words = Answers.Words(e.Target);
                    if (words.Count < 2) p.Add("fewer than 2 words to put in order");
                    foreach (var x in e.Extra)
                    {
                        if (Empty(x) || Answers.Words(x).Count != 1) p.Add("extra tile \"" + x + "\" is not one word");
                        else if (words.Any(wd => Answers.Normalize(wd, true) == Answers.Normalize(x, true))) p.Add("extra tile \"" + x + "\" is also in the sentence");
                    }
                    break;
                case "type":
                    if (Empty(e.Target) || Empty(e.English)) p.Add("en or es missing");
                    if (e.Accept.Any(Empty)) p.Add("an empty accepted answer");
                    break;
                case "match":
                    if (e.Pairs.Count < 3) p.Add("fewer than 3 pairs");
                    if (e.Pairs.Any(x => x == null || x.Count != 2 || Empty(x[0]) || Empty(x[1]))) { p.Add("a pair is not [es, en]"); break; }
                    if (e.Pairs.Select(x => Answers.Normalize(x[0], true)).Distinct().Count() != e.Pairs.Count) p.Add("a Spanish side repeats");
                    if (e.Pairs.Select(x => Answers.Normalize(x[1], true)).Distinct().Count() != e.Pairs.Count) p.Add("an English side repeats");
                    break;
                case "say":
                    if (Empty(e.Target) || Empty(e.English)) p.Add("en or es missing");
                    break;
                case "reply":
                    if (Empty(e.Target)) p.Add("no es line to answer");
                    Choices(); break;
                default:
                    p.Add("unknown kind"); break;
            }
            foreach (var t in SpanishMarks(e.SpanishTexts())) p.Add(t);
            return p;
        }

        void StoryProblems(LangCourse c, List<string> p)
        {
            var ids = new HashSet<string>();
            foreach (var s in Stories)
            {
                string w = "story " + (s.Id ?? "(no id)");
                if (string.IsNullOrWhiteSpace(s.Id) || !ids.Add(s.Id)) p.Add(w + ": id missing or repeated");
                if (s.Level < 1 || s.Level > 4) p.Add(w + ": level " + s.Level);
                if (string.IsNullOrWhiteSpace(s.Title) || string.IsNullOrWhiteSpace(s.TitleEn)) p.Add(w + ": title or titleEn missing");
                if (c.Unit(s.After) == null) p.Add(w + ": \"after\" unit " + s.After + " is not in the course");
                if (s.Lines.Count < 6 || s.Lines.Count > 20) p.Add(w + ": " + s.Lines.Count + " lines (6-20)");
                if (s.Lines.Any(l => l == null || string.IsNullOrWhiteSpace(l.Target) || string.IsNullOrWhiteSpace(l.English))) p.Add(w + ": a line without es or en");
                foreach (var t in SpanishMarks(s.Lines.Select(l => l?.Target))) p.Add(w + ": " + t);
                if (s.Questions.Count < 1) p.Add(w + ": no questions");
                foreach (var q in s.Questions)
                {
                    if (string.IsNullOrWhiteSpace(q.Question) || string.IsNullOrWhiteSpace(q.Answer) || q.Wrong.Count < 1) { p.Add(w + ": a question without q, answer or wrong"); continue; }
                    var all = new List<string> { q.Answer }; all.AddRange(q.Wrong);
                    var norm = all.Select(x => Answers.Normalize(x ?? "", true)).ToList();
                    if (norm.Distinct().Count() != norm.Count || norm.Any(x => x.Length == 0)) p.Add(w + ": choices repeat in \"" + q.Question + "\"");
                }
                foreach (var kv in s.Words) if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) p.Add(w + ": an empty word meaning");
            }
        }

        void VerbProblems(List<string> p)
        {
            var seen = new HashSet<string>();
            foreach (var v in Verbs)
            {
                string w = "verb " + (v.Infinitive ?? "(none)");
                if (string.IsNullOrWhiteSpace(v.Infinitive) || !seen.Add(v.Infinitive)) p.Add(w + ": infinitive missing or repeated");
                if (Conjugator.Ending(v.Infinitive) == null) p.Add(w + ": doesn't end in -ar, -er or -ir");
                if (string.IsNullOrWhiteSpace(v.English)) p.Add(w + ": no English");
                if (v.Level < 1 || v.Level > 4) p.Add(w + ": level " + v.Level);
                if (v.Tenses.Count == 0) p.Add(w + ": no tenses");
                foreach (var kv in v.IrregularLengths) if (kv.Value != 5) p.Add(w + ": " + kv.Key + " list has " + kv.Value + " entries (5: yo, tú, él, nosotros, ellos)");
                foreach (var t in v.UnknownTenses) p.Add(w + ": unknown tense " + t);
                if (v.StemProblem != null) p.Add(w + ": " + v.StemProblem);
                foreach (var t in v.Tenses)
                    for (int k = 0; k < 5; k++)
                    {
                        var f = Conjugator.Form(v, t, k);
                        if (string.IsNullOrWhiteSpace(f.Text)) p.Add(w + ": no " + Conjugator.TenseName(t) + " form for " + Conjugator.Persons[k]);
                    }
            }
        }
    }
}
