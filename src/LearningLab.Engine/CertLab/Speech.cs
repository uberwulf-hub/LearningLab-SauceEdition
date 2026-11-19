// Cert Lab - reading aloud (adapted from Renewal's Engine\Speech.cs: apps don't share engine files). ReadAloud walks
// the units of a page (paragraphs, questions, answer choices, cheat sheet rows, a card's two sides) sentence by
// sentence through an ISpeaker (in App Hub: Windows' SAPI voices or the newer Windows voices, which stay with the
// screens in Learning Lab, where Platform.Speech speaks; FakeSpeaker in tests, which never makes a sound), tells the
// window which sentence it is on, and can go back and forward one sentence. What is spoken goes through Transform
// first (Pronounce: "nmcli" -> "N M C L I"), so the sentences shown and the sentences said stay the same in number.
// PauseMark is a silent moment (a flash card between its sides). Nothing here touches a UI toolkit.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CertLab
{
    public interface ISpeaker
    {
        event Action<int> Done;            // the utterance with this id finished (never for one that was stopped)
        void Speak(int id, string text);   // text may be ReadAloud.PauseMark: a silent pause
        void Pause();
        void Resume();
        void Stop();
        int Rate { get; set; }             // -10..10
        string Voice { get; set; }
        List<string> Voices();
    }


    /// <summary>For tests and pictures: records what would be said and finishes only when Finish() is called. No sound.</summary>
    public sealed class FakeSpeaker : ISpeaker
    {
        public readonly List<string> Spoken = new List<string>();
        public int Current;          // id being "spoken", 0 = none
        public bool Paused;
        public int Stops, Pauses, Resumes;
        public event Action<int> Done;
        public void Speak(int id, string text) { int ms; Spoken.Add(ReadAloud.IsPause(text, out ms) ? "[pause " + ms + " ms]" : text); Current = id; }
        public void Pause() { Paused = true; Pauses++; }
        public void Resume() { Paused = false; Resumes++; }
        public void Stop() { Current = 0; Paused = false; Stops++; }
        public int Rate { get; set; }
        public string Voice { get; set; }
        public List<string> Voices() { return new List<string> { "Test Voice", "Another Test Voice" }; }
        public void Finish()
        {
            int id = Current;
            if (id == 0) return;
            Current = 0;
            var h = Done;
            if (h != null) h(id);
        }
    }


    public sealed class ReadAloud
    {
        readonly ISpeaker speaker;
        List<List<string>> sentences = new List<List<string>>();
        int pending, counter;

        /// <summary>A silent pause of this many milliseconds when spoken ("\u0001pause:1500").</summary>
        public const string PauseMark = "\u0001pause:";
        public static string Pause(int ms) { return PauseMark + Math.Max(100, ms); }
        public static bool IsPause(string text, out int ms)
        {
            ms = 0;
            if (text == null || !text.StartsWith(PauseMark, StringComparison.Ordinal)) return false;
            int.TryParse(text.Substring(PauseMark.Length), out ms);
            ms = Math.Max(100, Math.Min(10000, ms));
            return true;
        }

        public string SourceKey = "";
        public int Unit, Sentence;
        public bool Playing, Paused;
        /// <summary>What is said for a sentence that is shown (pronunciation help). Null: as shown.</summary>
        public Func<string, string> Transform;
        public event Action<string, int, int> Position;   // source, unit, sentence: raised before each sentence
        public event Action Finished;

        public ReadAloud(ISpeaker speaker)
        {
            this.speaker = speaker;
            speaker.Done += OnDone;
        }

        public ISpeaker Speaker { get { return speaker; } }
        public int Rate { get { return speaker.Rate; } set { speaker.Rate = value; } }
        public int UnitCount { get { return sentences.Count; } }
        public List<string> SentencesOf(int unit) { return unit >= 0 && unit < sentences.Count ? sentences[unit] : new List<string>(); }
        public string CurrentSentence { get { var s = SentencesOf(Unit); return Sentence >= 0 && Sentence < s.Count ? s[Sentence] : ""; } }

        /// <summary>Loads the units of a page (a unit may be PauseMark); reading starts at unit / sentence.</summary>
        public void Load(string sourceKey, IList<string> units, int unit, int sentence)
        {
            Halt();
            SourceKey = sourceKey ?? "";
            sentences = new List<List<string>>();
            if (units != null) foreach (var u in units) sentences.Add(u != null && u.StartsWith(PauseMark, StringComparison.Ordinal) ? new List<string> { u } : Sentences(u));
            Unit = Math.Max(0, unit);
            Sentence = Math.Max(0, sentence);
        }

        public void Play()
        {
            if (Paused) { Resume(); return; }
            if (Playing) return;
            if (!Settle()) { Unit = 0; Sentence = 0; RaiseFinished(); return; }
            Playing = true;
            SpeakCurrent();
        }

        public void Pause() { if (Playing && !Paused) { speaker.Pause(); Paused = true; } }
        public void Resume() { if (Playing && Paused) { speaker.Resume(); Paused = false; } }

        /// <summary>Stops reading; Play starts again at the beginning of the same sentence.</summary>
        public void Stop() { Halt(); }

        void Halt()
        {
            if (!Playing) return;
            pending = 0;
            Playing = false;
            Paused = false;
            speaker.Stop();
        }

        /// <summary>The next sentence (into the next unit at the end of one).</summary>
        public void Next() { Step(+1); }
        /// <summary>The sentence before (the end of the unit before at the start of one; the first stays first).</summary>
        public void Previous() { Step(-1); }

        void Step(int dir)
        {
            bool was = Playing;
            Halt();
            // every sentence in reading order (silent pauses are skipped over)
            var pos = new List<int[]>();
            for (int u = 0; u < sentences.Count; u++) if (!IsPauseUnit(u)) for (int s = 0; s < sentences[u].Count; s++) pos.Add(new[] { u, s });
            if (pos.Count == 0) return;
            int ci = pos.FindIndex(p => p[0] > Unit || p[0] == Unit && p[1] >= Sentence);
            if (ci < 0) ci = pos.Count;
            bool exact = ci < pos.Count && pos[ci][0] == Unit && pos[ci][1] == Sentence;
            int ni = exact ? ci + dir : (dir > 0 ? ci : ci - 1);
            if (ni < 0) ni = 0;
            if (ni >= pos.Count) { Unit = sentences.Count; Sentence = 0; if (was) RaiseFinished(); return; }
            Unit = pos[ni][0]; Sentence = pos[ni][1];
            if (was) { Playing = true; SpeakCurrent(); }
            else { var h = Position; if (h != null) h(SourceKey, Unit, Sentence); }
        }
        bool IsPauseUnit(int u) { return u >= 0 && u < sentences.Count && sentences[u].Count == 1 && sentences[u][0].StartsWith(PauseMark, StringComparison.Ordinal); }
        int FirstUnit() { for (int u = 0; u < sentences.Count; u++) if (sentences[u].Count > 0 && !IsPauseUnit(u)) return u; return -1; }

        void OnDone(int id)
        {
            if (id != pending || !Playing) return;
            pending = 0;
            Sentence++;
            if (!Settle()) { Playing = false; Paused = false; Unit = 0; Sentence = 0; RaiseFinished(); return; }
            SpeakCurrent();
        }

        /// <summary>Moves the position forward past empty units and ended units.</summary>
        bool Settle()
        {
            while (Unit < sentences.Count && Sentence >= sentences[Unit].Count) { Unit++; Sentence = 0; }
            return Unit < sentences.Count;
        }

        void SpeakCurrent()
        {
            var h = Position; if (h != null) h(SourceKey, Unit, Sentence);
            if (!Playing) return;   // a Position handler may have stopped us
            pending = ++counter;
            string shown = sentences[Unit][Sentence];
            string said = shown.StartsWith(PauseMark, StringComparison.Ordinal) || Transform == null ? shown : Transform(shown);
            speaker.Speak(pending, string.IsNullOrWhiteSpace(said) ? shown : said);
        }

        void RaiseFinished() { var h = Finished; if (h != null) h(); }

        // ---- sentences ----

        static readonly HashSet<string> Abbrev = new HashSet<string>(StringComparer.Ordinal)
        {
            "mr", "mrs", "ms", "dr", "st", "jr", "sr", "vs", "etc", "cf", "no", "nos", "vol", "ch", "pp", "ed", "ps",
            "co", "inc", "ltd", "dept", "approx", "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept",
            "oct", "nov", "dec", "e.g", "i.e", "viz", "fig", "ex", "min", "max", "sec", "approx"
        };
        static readonly Regex Dotted = new Regex(@"^([a-z]\.)+[a-z]$", RegexOptions.CultureInvariant);
        static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        static bool IsEnd(char c) { return c == '.' || c == '!' || c == '?' || c == '\u2026'; }
        static bool IsCloser(char c) { return c == '"' || c == '\'' || c == ')' || c == ']' || c == '`' || c == '\u201D' || c == '\u2019' || c == '\u00BB'; }

        /// <summary>Splits text into sentences at . ! ? followed by a space (with any closing quotes or brackets), but not
        /// after abbreviations (e.g., etc.), initials, a list number at the start ("1.") or before a lower-case word.
        /// File names, versions and addresses (/etc/fstab.d, 192.168.1.1, v1.2) never split: the dot has no space after it.</summary>
        public static List<string> Sentences(string text)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(text)) return r;
            text = Spaces.Replace(text, " ").Trim();
            int start = 0, i = 0;
            while (i < text.Length)
            {
                if (!IsEnd(text[i])) { i++; continue; }
                int j = i;
                while (j < text.Length && IsEnd(text[j])) j++;
                bool singleDot = text[i] == '.' && j == i + 1;
                while (j < text.Length && IsCloser(text[j])) j++;
                if (j >= text.Length) break;
                if (text[j] != ' ') { i = j; continue; }
                bool split = true;
                if (singleDot)
                {
                    int ws = text.LastIndexOf(' ', i - 1 < 0 ? 0 : i - 1);
                    int ts = ws < start ? start : ws + 1;
                    string word = text.Substring(ts, i - ts).TrimStart('"', '\'', '(', '[', '`', '\u201C', '\u2018').ToLowerInvariant();
                    if (word.Length == 0) split = true;
                    else if (Abbrev.Contains(word) || (word.Length == 1 && char.IsLetter(word[0])) || Dotted.IsMatch(word)) split = false;
                    else if (ts == start && IsDigits(word)) split = false;
                }
                if (split && j + 1 < text.Length && char.IsLower(text[j + 1])) split = false;
                if (split)
                {
                    r.Add(text.Substring(start, j - start).Trim());
                    start = j + 1;
                }
                i = j;
            }
            if (start < text.Length) { var t = text.Substring(start).Trim(); if (t.Length > 0) r.Add(t); }
            return r;
        }

        static bool IsDigits(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return s.Length > 0; }
    }
}
