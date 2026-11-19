// Learning Games - the mastery score (2026-11-11, docs\MATH.md step 1): one number 0-100 per player per skill, with our
// own curve. It rises quickly at first and slowly near the top; a miss costs little at low scores and much more in the
// Challenge zone (90-99); 100 = Mastered. Every number of the curve is in this file (Gain, Loss, the zone limits, the
// difficulty per zone), tuned by simulation in the checks (tests\LearningLab.Tests\MasteryChecks.cs): a player right
// 9 times in 10 masters a skill in about 15-25 questions; right 7 times in 10 reaches Strong but rarely Mastered; three
// misses in a row in the Challenge zone always drop below 90. Pure: no saving, no clock, no randomness.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningGames
{
    public static class Mastery
    {
        public const int Max = 100;

        // ---------------------------------------------------------------- the curve (the only place its numbers live)
        /// <summary>Where each band starts: 0 Getting started, 40 Building, 70 Strong, 90 Challenge zone (90-94, 95-99), 100 Mastered.</summary>
        static readonly int[] BandStart = { 0, 40, 70, 90, 95 };
        /// <summary>Points for a right answer in each band (fast at first, slow near the top).</summary>
        static readonly int[] Gain = { 20, 13, 5, 2, 1 };
        /// <summary>Points lost for a miss in each band (little at first, a lot in the Challenge zone).</summary>
        static readonly int[] Loss = { 2, 6, 15, 16, 16 };
        /// <summary>The generators' level (1 easy .. 5 hard) for a score: 0-19, 20-39, 40-69, 70-89, 90+.</summary>
        static readonly int[] LevelStart = { 0, 20, 40, 70, 90 };

        static int Band(int score)
        {
            int b = 0;
            for (int i = 0; i < BandStart.Length; i++) if (score >= BandStart[i]) b = i;
            return b;
        }

        /// <summary>The score after one answer. A Mastered skill stays Mastered: practising it again can't take it away.</summary>
        public static int Next(int score, bool right)
        {
            score = Clamp(score);
            if (score >= Max) return Max;
            int b = Band(score);
            return right ? Math.Min(Max, score + Gain[b]) : Math.Max(0, score - Loss[b]);
        }

        public static int Clamp(int score) { return Math.Max(0, Math.Min(Max, score)); }

        /// <summary>The question difficulty (the generators' level 1-5) for a score.</summary>
        public static int LevelFor(int score)
        {
            int lv = 1;
            for (int i = 0; i < LevelStart.Length; i++) if (Clamp(score) >= LevelStart[i]) lv = i + 1;
            return lv;
        }

        // ---------------------------------------------------------------- names
        public enum Zone { GettingStarted, Building, Strong, Challenge, Mastered }

        public static Zone ZoneOf(int score)
        {
            score = Clamp(score);
            if (score >= Max) return Zone.Mastered;
            if (score >= 90) return Zone.Challenge;
            if (score >= 70) return Zone.Strong;
            if (score >= 40) return Zone.Building;
            return Zone.GettingStarted;
        }

        public static string ZoneName(int score) { return Name(ZoneOf(score)); }
        public static string Name(Zone z)
        {
            switch (z)
            {
                case Zone.Building: return "Building";
                case Zone.Strong: return "Strong";
                case Zone.Challenge: return "Challenge zone";
                case Zone.Mastered: return "Mastered";
                default: return "Getting started";
            }
        }

        /// <summary>A short line for the person: what the zone means and what's next.</summary>
        public static string Encourage(int score)
        {
            switch (ZoneOf(score))
            {
                case Zone.Mastered: return "Mastered! You can still practise it any time.";
                case Zone.Challenge: return "Challenge zone: the questions are the hardest now, and a miss costs more. Get to 100!";
                case Zone.Strong: return "Strong: you know this well. Keep going to reach the Challenge zone at 90.";
                case Zone.Building: return "Building: you're getting it. Strong starts at 70.";
                default: return "Getting started: every right answer moves you up quickly.";
            }
        }

        // ---------------------------------------------------------------- what to practise next
        /// <summary>Up next: skills started but not mastered (highest score first: closest to done), then the next
        /// skills not started yet in standard order. ordered = the grade's skills in standard order; score and answered
        /// read the player's progress.</summary>
        public static List<string> UpNext(IEnumerable<string> ordered, Func<string, int> score, Func<string, int> answered, int max)
        {
            var list = ordered.ToList();
            var started = list.Where(id => answered(id) > 0 && score(id) < Max)
                              .Select((id, i) => new { id, i, s = score(id) })
                              .OrderByDescending(x => x.s).ThenBy(x => x.i).Select(x => x.id);
            var fresh = list.Where(id => answered(id) == 0 && score(id) < Max);
            return started.Concat(fresh).Take(Math.Max(0, max)).ToList();
        }
    }
}
