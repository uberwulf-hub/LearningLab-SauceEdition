// Learning Lab - Spelling Bee: one practice run, as in Andrew's spelling-bee.html. A word is right on the first try
// (counts as right, and leaves the Tricky Words), or "Not quite" once (try again or show the answer), then right on the
// second try (counts as missed, goes to Tricky Words) or shown with the wrong letters marked (missed too). "I don't
// know" shows it at once.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningLab.SpellingBee
{
    public enum BeeOutcome { Nothing, Right, Retry, Shown }

    public sealed class BeeRun
    {
        public IReadOnlyList<BeeWord> Words { get; }
        public int Index { get; private set; }
        public int Good { get; private set; }
        public int Bad { get; private set; }
        public List<BeeWord> Missed { get; } = new List<BeeWord>();
        public int Tries { get; private set; }
        public int HintLevel { get; private set; }
        public bool Answered { get; private set; }
        public bool Finished { get; private set; }
        public bool FirstTry { get; private set; }
        /// <summary>The last check (null before any).</summary>
        public BeeVerdict Last { get; private set; }

        /// <summary>A word missed (to My Tricky Words) / spelled right the first time (out of them).</summary>
        public event Action<BeeWord> Missing, KnownNow;

        public BeeRun(IEnumerable<BeeWord> words)
        {
            Words = words.ToList();
            if (Words.Count == 0) throw new ArgumentException("no words");
        }

        /// <summary>The words for a run: each list's words once, mixed up or not, cut to howMany (0 = all).</summary>
        public static List<BeeWord> Pick(IEnumerable<BeeWord> pool, bool shuffle, int howMany, Random rand)
        {
            var seen = new HashSet<string>();
            var l = pool.Where(w => w != null && seen.Add(w.Word)).ToList();
            if (shuffle) for (int i = l.Count - 1; i > 0; i--) { int j = rand.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
            if (howMany > 0 && l.Count > howMany) l = l.Take(howMany).ToList();
            return l;
        }

        public BeeWord Current => Words[Index];
        public bool IsLast => Index == Words.Count - 1;
        public int Total => Good + Bad;

        public BeeOutcome Submit(string text)
        {
            if (Answered || Finished) return BeeOutcome.Nothing;
            text = (text ?? "").Trim();
            if (text.Length == 0) return BeeOutcome.Nothing;
            var v = BeeCheck.Judge(text, Current);
            Last = v;
            Tries++;
            if (v.Ok)
            {
                Answered = true;
                FirstTry = Tries == 1;
                if (FirstTry) { Good++; KnownNow?.Invoke(Current); }
                else { Bad++; AddMissed(); }
                return BeeOutcome.Right;
            }
            if (Tries == 1) return BeeOutcome.Retry;
            Show();
            return BeeOutcome.Shown;
        }

        /// <summary>Shows the answer ("I don't know", "Show answer", or the second miss).</summary>
        public void Show()
        {
            if (Answered || Finished) return;
            Answered = true; FirstTry = false;
            Bad++;
            AddMissed();
        }

        void AddMissed()
        {
            if (!Missed.Any(m => m.Word == Current.Word)) Missed.Add(Current);
            Missing?.Invoke(Current);
        }

        public (string Blanks, int Letters) NextHint()
        {
            HintLevel = Math.Min(HintLevel + 1, 3);
            return BeeCheck.Hint(Current.Word, HintLevel);
        }

        /// <summary>On to the next word; false (and Finished) after the last.</summary>
        public bool Next()
        {
            if (Finished) return false;
            if (Index < Words.Count - 1)
            {
                Index++; Tries = 0; HintLevel = 0; Answered = false; FirstTry = false; Last = null;
                return true;
            }
            Finished = true;
            return false;
        }

        /// <summary>Stops early (the score so far counts).</summary>
        public void Stop() { Finished = true; }
    }
}
