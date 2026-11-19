// Cyber Range - the player's overall progress: rank by points (Recruit -> Operator -> Specialist -> Expert -> Elite),
// the streak of days played, and the badges. Pure logic over RangeStore and the challenge list, so -Test checks every
// rule without a window. Badges are kept in progress.json (RangeStore.Badges) with the time they were earned.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public class RangeRank
    {
        public int Index, Min;
        public string Name = "", Line = "", Colour = "#94A3B8";
    }

    public class RangeBadgeDef
    {
        public string Id = "", Name = "", How = "", Icon = "star", Colour = "#A78BFA";
    }

    /// <summary>One challenge (scenario or puzzle) as the lists, the home page and the badges see it.</summary>
    public class RangeItem
    {
        public string Id = "", Title = "", Teaser = "", Level = "Beginner", Category = "", Topic = "";
        public int Points, Minutes, Order, Objectives = 1;
        public bool Puzzle;
        public RangeScenario Sc;
        public RangePuzzle Pz;

        public static RangeItem Of(RangeScenario s)
        {
            return new RangeItem { Id = s.Id, Title = s.Title, Teaser = s.Teaser, Level = s.Level, Category = s.Category, Topic = Platform(s), Points = s.MaxPoints, Minutes = s.Minutes, Order = s.Order, Objectives = s.ObjectiveCount, Sc = s };
        }
        public static RangeItem Of(RangePuzzle p)
        {
            return new RangeItem { Id = p.Id, Title = p.Title, Teaser = p.Teaser, Level = p.Level, Category = PuzzleCategory(p), Topic = p.Topic, Points = p.Points, Minutes = p.Minutes, Order = p.Order, Puzzle = true, Pz = p };
        }
        /// <summary>The category a puzzle is listed under (CTF Puzzles; the one place to change if puzzles get categories).</summary>
        public static string PuzzleCategory(RangePuzzle p) { return string.IsNullOrEmpty(p.Category) ? "ctf" : p.Category; }

        /// <summary>What a scenario runs on, for its topic chip: "Linux", "Windows", "Linux + Windows", plus "Web".</summary>
        public static string Platform(RangeScenario s)
        {
            bool lin = s.Machines.Any(m => m.Os != "windows"), win = s.Machines.Any(m => m.Os == "windows");
            string t = lin && win ? "Linux + Windows" : win ? "Windows" : "Linux";
            if (s.Web.Count > 0) t += " + Web";
            return t;
        }

        public static List<RangeItem> All(IEnumerable<RangeScenario> scenarios, IEnumerable<RangePuzzle> puzzles)
        {
            var l = new List<RangeItem>();
            foreach (var s in scenarios) l.Add(Of(s));
            foreach (var p in puzzles) l.Add(Of(p));
            return l;
        }
    }

    public static class RangeProgress
    {
        public static readonly RangeRank[] Ranks =
        {
            new RangeRank { Index = 0, Min = 0, Name = "Recruit", Colour = "#94A3B8", Line = "Everyone starts here. Capture your first flag." },
            new RangeRank { Index = 1, Min = 250, Name = "Operator", Colour = "#34D399", Line = "You know your way around a terminal." },
            new RangeRank { Index = 2, Min = 1000, Name = "Specialist", Colour = "#38BDF8", Line = "You chain steps together with confidence." },
            new RangeRank { Index = 3, Min = 3000, Name = "Expert", Colour = "#A78BFA", Line = "Hard targets don't scare you." },
            new RangeRank { Index = 4, Min = 6000, Name = "Elite", Colour = "#FBBF24", Line = "Top of the range." },
        };

        public static readonly RangeBadgeDef[] Badges =
        {
            new RangeBadgeDef { Id = "first-flag", Name = "First Capture", Icon = "flag", Colour = "#FB7185", How = "Capture your first flag or solve your first puzzle." },
            new RangeBadgeDef { Id = "blue-win", Name = "Defender", Icon = "shield", Colour = "#38BDF8", How = "Finish a Blue Team challenge: find what the attacker did and clean it up." },
            new RangeBadgeDef { Id = "no-hints", Name = "Unassisted", Icon = "bulb", Colour = "#FBBF24", How = "Finish a challenge without opening a single hint." },
            new RangeBadgeDef { Id = "ghost", Name = "Ghost", Icon = "ghost", Colour = "#C4B5FD", How = "Beat a Red vs Defender challenge without the defender raising one alert." },
            new RangeBadgeDef { Id = "level-clear", Name = "Level Cleared", Icon = "layers", Colour = "#34D399", How = "Finish every challenge in one level of a category." },
            new RangeBadgeDef { Id = "category-clear", Name = "Full House", Icon = "crown", Colour = "#F59E0B", How = "Finish every challenge in a whole category." },
            new RangeBadgeDef { Id = "streak-3", Name = "On a Roll", Icon = "flame", Colour = "#FB923C", How = "Play on three days in a row." },
            new RangeBadgeDef { Id = "expert", Name = "Into the Deep", Icon = "mountain", Colour = "#F87171", How = "Finish an Expert challenge." },
        };
        public static RangeBadgeDef Badge(string id) { return Badges.FirstOrDefault(b => b.Id == id); }

        /// <summary>Total points: the best score of every challenge.</summary>
        public static int TotalPoints(RangeStore s)
        {
            return s.Scenarios.Values.Sum(r => Math.Max(0, r.BestScore)) + s.Puzzles.Values.Sum(r => Math.Max(0, r.BestScore));
        }
        public static RangeRank RankFor(int points)
        {
            var r = Ranks[0];
            foreach (var k in Ranks) if (points >= k.Min) r = k;
            return r;
        }
        /// <summary>The next rank, or null at the top.</summary>
        public static RangeRank NextRank(int points)
        {
            var r = RankFor(points);
            return r.Index + 1 < Ranks.Length ? Ranks[r.Index + 1] : null;
        }
        /// <summary>How far through the current rank, 0..1 (1 at the top rank).</summary>
        public static double RankShare(int points)
        {
            var r = RankFor(points); var n = NextRank(points);
            if (n == null) return 1;
            return (points - r.Min) / (double)(n.Min - r.Min);
        }

        public static RangeResult ResultFor(RangeStore s, RangeItem it) { return s.For(it.Puzzle, it.Id); }
        static bool Done(RangeStore s, RangeItem it)
        {
            var d = it.Puzzle ? s.Puzzles : s.Scenarios;
            RangeResult r;
            return d.TryGetValue(it.Id, out r) && r.Completed;
        }
        public static int Completed(RangeStore s, IEnumerable<RangeItem> items) { return items.Count(i => Done(s, i)); }
        public static int FlagsCaptured(RangeStore s) { return s.Scenarios.Values.Sum(r => r.Flags.Count) + s.Puzzles.Values.Count(r => r.Flags.Count > 0); }

        // ---------------------------------------------------------------- days and streaks
        public static string Day(DateTime t) { return t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static DateTime? ParseDay(string d)
        {
            DateTime t;
            return DateTime.TryParseExact(d ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? (DateTime?)t.Date : null;
        }
        /// <summary>Notes that the person played today (kept to the last 400 days).</summary>
        public static void MarkDay(RangeStore s, DateTime now)
        {
            string d = Day(now);
            if (!s.Days.Contains(d)) s.Days.Add(d);
            s.Days.Sort(StringComparer.Ordinal);
            while (s.Days.Count > 400) s.Days.RemoveAt(0);
        }
        /// <summary>The current streak: days in a row up to today, or up to yesterday when today isn't played yet.</summary>
        public static int Streak(IEnumerable<string> days, DateTime today)
        {
            var set = new HashSet<DateTime>(days.Select(ParseDay).Where(x => x.HasValue).Select(x => x.Value));
            var d = today.Date;
            if (!set.Contains(d)) d = d.AddDays(-1);
            int n = 0;
            while (set.Contains(d)) { n++; d = d.AddDays(-1); }
            return n;
        }
        /// <summary>The longest run of days in a row ever played.</summary>
        public static int BestStreak(IEnumerable<string> days)
        {
            var list = days.Select(ParseDay).Where(x => x.HasValue).Select(x => x.Value).Distinct().OrderBy(x => x).ToList();
            int best = 0, run = 0;
            for (int i = 0; i < list.Count; i++)
            {
                run = i > 0 && (list[i] - list[i - 1]).TotalDays == 1 ? run + 1 : 1;
                best = Math.Max(best, run);
            }
            return best;
        }

        // ---------------------------------------------------------------- badges
        /// <summary>The badges the progress (and, when given, the run that just ended) has earned. Run-only badges
        /// (Unassisted, Ghost) need the run.</summary>
        public static List<string> Earned(RangeStore s, List<RangeItem> items, RangeRun run)
        {
            var l = new List<string>();
            if (s.Scenarios.Values.Any(r => r.Flags.Count > 0) || s.Puzzles.Values.Any(r => r.Flags.Count > 0 || r.Completed)) l.Add("first-flag");
            if (items.Any(i => !i.Puzzle && RangeData.BlueLike(i.Category) && Done(s, i))) l.Add("blue-win");
            if (run != null && run.AllFound && !run.GaveUp && run.HintsUsed.Count == 0) l.Add("no-hints");
            if (run != null && run.IsDefender && run.AllFound && !run.GaveUp && run.Defender != null && run.Defender.Alerts == 0) l.Add("ghost");
            var groups = items.GroupBy(i => i.Category + "|" + i.Level).Where(g => g.Any());
            if (groups.Any(g => g.All(i => Done(s, i)))) l.Add("level-clear");
            if (items.GroupBy(i => i.Category).Any(g => g.Any() && g.All(i => Done(s, i)))) l.Add("category-clear");
            if (BestStreak(s.Days) >= 3) l.Add("streak-3");
            if (items.Any(i => i.Level == "Expert" && Done(s, i))) l.Add("expert");
            return l;
        }

        /// <summary>Adds the badges not yet held; returns their ids in the order of the badge list.</summary>
        public static List<string> Award(RangeStore s, List<RangeItem> items, RangeRun run, DateTime now)
        {
            var fresh = new List<string>();
            var got = Earned(s, items, run);
            foreach (var b in Badges)
            {
                if (!got.Contains(b.Id) || s.Badges.ContainsKey(b.Id)) continue;
                s.Badges[b.Id] = now.Ticks;
                fresh.Add(b.Id);
            }
            return fresh;
        }

        // ---------------------------------------------------------------- what to play next
        /// <summary>The challenge to continue: the last one played if not finished, else the next unfinished one after
        /// it (same category, same level, then the next level), else the first Beginner challenge. Null when all are done.</summary>
        public static RangeItem Next(RangeStore s, List<RangeItem> items, out bool resuming)
        {
            resuming = false;
            RangeItem last = null; long lastAt = 0;
            foreach (var it in items)
            {
                var d = it.Puzzle ? s.Puzzles : s.Scenarios;
                RangeResult r;
                if (d.TryGetValue(it.Id, out r) && r.LastPlayed > lastAt) { lastAt = r.LastPlayed; last = it; }
            }
            if (last != null && !Done(s, last)) { resuming = true; return last; }
            Func<RangeItem, int> lvl = i => RangeData.LevelIndex(i.Level);
            if (last != null)
            {
                var same = items.Where(i => i.Category == last.Category && !Done(s, i)).OrderBy(i => lvl(i) < lvl(last) ? 1 : 0).ThenBy(lvl).ThenBy(i => i.Order).ThenBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault();
                if (same != null) return same;
            }
            var cats = RangeData.Categories.Select(c => c.Id).ToList();
            return items.Where(i => !Done(s, i)).OrderBy(lvl).ThenBy(i => { int k = cats.IndexOf(i.Category); return k < 0 ? 99 : k; }).ThenBy(i => i.Order).ThenBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
