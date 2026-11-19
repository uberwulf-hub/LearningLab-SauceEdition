// Cyber Range - checks of the look and the progress (2026-10-23): ranks, streaks, badges, "Continue", the glossary,
// the progress file's version 2 (and that version 1 still opens and upgrades), the category styles for categories
// added later, and that every new page builds.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        // Cyber Range progress.json version 2 as this version writes it (badges, days played, tour seen)
        const string RangeSecond = "{\"Version\":2,\"AutoCapture\":true,\"LastCategory\":\"red\",\"LastLevel\":\"Beginner\",\"Scenarios\":{\"open-door\":{\"BestScore\":230,\"Completed\":true,\"BestSeconds\":742,\"Flags\":[\"user\",\"root\"],\"Attempts\":2,\"Notes\":\"\",\"LastPlayed\":638640000000000000}},\"Puzzles\":{},\"Badges\":{\"first-flag\":638640000000000000,\"streak-3\":638640864000000000},\"Days\":[\"2024-10-20\",\"2024-10-21\",\"2024-10-22\"],\"TourSeen\":true}";

        static IEnumerable<KeyValuePair<string, Action>> RangeLookGroups()
        {
            yield return new KeyValuePair<string, Action>("range-progress", RangeProgressTests);
            yield return new KeyValuePair<string, Action>("range-glossary", RangeGlossaryTests);
            yield return new KeyValuePair<string, Action>("range-look", RangeLookTests);
        }

        static void RangeProgressTests()
        {
            // ---- ranks
            var R = RangeProgress.Ranks;
            Check("ranks go Recruit, Operator, Specialist, Expert, Elite with rising thresholds from 0", string.Join(",", R.Select(r => r.Name).ToArray()) == "Recruit,Operator,Specialist,Expert,Elite" && R[0].Min == 0 && R.Zip(R.Skip(1), (a, b) => a.Min < b.Min).All(x => x) && R.Select((r, i) => r.Index == i).All(x => x));
            Check("a rank is reached exactly at its threshold", RangeProgress.RankFor(0).Name == "Recruit" && RangeProgress.RankFor(249).Name == "Recruit" && RangeProgress.RankFor(250).Name == "Operator" && RangeProgress.RankFor(999).Name == "Operator" && RangeProgress.RankFor(1000).Name == "Specialist" && RangeProgress.RankFor(5999).Name == "Expert" && RangeProgress.RankFor(6000).Name == "Elite" && RangeProgress.RankFor(99999).Name == "Elite");
            Check("the next rank and the share of the way there", RangeProgress.NextRank(0).Name == "Operator" && RangeProgress.NextRank(6000) == null && Math.Abs(RangeProgress.RankShare(625) - 0.5) < 1e-9 && RangeProgress.RankShare(7000) == 1);
            var st = new RangeStore();
            st.For(false, "a").BestScore = 200; st.For(true, "b").BestScore = 60; st.For(true, "c").BestScore = 0;
            Check("total points are the best score of every challenge", RangeProgress.TotalPoints(st) == 260);

            // ---- days and streaks
            var today = new DateTime(2026, 10, 23, 15, 0, 0);
            Func<int[], List<string>> days = ds => ds.Select(d => RangeProgress.Day(today.AddDays(-d))).ToList();
            Check("a streak counts days in a row up to today", RangeProgress.Streak(days(new[] { 0, 1, 2 }), today) == 3);
            Check("...or up to yesterday when today isn't played yet", RangeProgress.Streak(days(new[] { 1, 2, 3 }), today) == 3);
            Check("...a gap ends it", RangeProgress.Streak(days(new[] { 0, 2, 3 }), today) == 1 && RangeProgress.Streak(days(new[] { 2, 3 }), today) == 0 && RangeProgress.Streak(new List<string>(), today) == 0);
            Check("the best streak ever, ignoring repeats and broken entries", RangeProgress.BestStreak(days(new[] { 10, 9, 8, 5, 4, 4, 0 }).Concat(new[] { "not a day", "" })) == 3 && RangeProgress.BestStreak(new string[0]) == 0);
            var sd = new RangeStore();
            RangeProgress.MarkDay(sd, today); RangeProgress.MarkDay(sd, today.AddHours(2)); RangeProgress.MarkDay(sd, today.AddDays(-1));
            Check("marking a day keeps one entry per day, in order", sd.Days.Count == 2 && sd.Days[0] == RangeProgress.Day(today.AddDays(-1)));
            for (int i = 0; i < 450; i++) RangeProgress.MarkDay(sd, today.AddDays(-i));
            Check("...and keeps at most 400 days", sd.Days.Count == 400 && sd.Days.Last() == RangeProgress.Day(today));

            // ---- badges from progress
            LoadRange();
            var items = RangeItem.All(rangeScenarios, rangePuzzles);
            var s = new RangeStore();
            Check("nothing played: no badges", RangeProgress.Earned(s, items, null).Count == 0);
            var bp = rangePuzzles.First(p => p.Level == "Beginner");
            var r1 = s.For(true, bp.Id); r1.Completed = true; r1.BestScore = bp.Points; r1.Flags.Add("flag");
            Check("a solved puzzle earns First Capture", RangeProgress.Earned(s, items, null).Contains("first-flag"));
            var s2 = new RangeStore(); s2.For(false, "x").Flags.Add("user");
            Check("...and so does one captured flag in an unfinished scenario", RangeProgress.Earned(s2, items, null).Contains("first-flag"));
            var blue = rangeScenarios.FirstOrDefault(x => x.Category == "blue");
            if (blue != null) { s.For(false, blue.Id).Completed = true; Check("a finished Blue Team challenge earns Defender", RangeProgress.Earned(s, items, null).Contains("blue-win")); }
            var exp = items.FirstOrDefault(i => i.Level == "Expert");
            if (exp != null) { Check("no Expert done: no Into the Deep", !RangeProgress.Earned(s, items, null).Contains("expert")); s.For(exp.Puzzle, exp.Id).Completed = true; Check("an Expert challenge done earns Into the Deep", RangeProgress.Earned(s, items, null).Contains("expert")); }
            Check("one level not fully done: no Level Cleared", !RangeProgress.Earned(s, items, null).Contains("level-clear"));
            var lvl = items.Where(i => i.Category == "red" && i.Level == "Beginner").ToList();
            foreach (var i in lvl) s.For(i.Puzzle, i.Id).Completed = true;
            Check("every challenge of one level done earns Level Cleared, not yet Full House", RangeProgress.Earned(s, items, null).Contains("level-clear") && !RangeProgress.Earned(s, items, null).Contains("category-clear"));
            foreach (var i in items.Where(i => i.Category == "red")) s.For(i.Puzzle, i.Id).Completed = true;
            Check("every challenge of a category done earns Full House", RangeProgress.Earned(s, items, null).Contains("category-clear"));
            s.Days = days(new[] { 7, 6 });
            Check("two days in a row: no On a Roll", !RangeProgress.Earned(s, items, null).Contains("streak-3"));
            s.Days = days(new[] { 9, 8, 7 });
            Check("three days in a row, even long ago, earn On a Roll", RangeProgress.Earned(s, items, null).Contains("streak-3"));
            var fresh = RangeProgress.Award(s, items, null, today);
            Check("awarding adds the new badges with their time, in the badge list's order", fresh.Count >= 5 && fresh.SequenceEqual(RangeProgress.Badges.Select(b => b.Id).Where(fresh.Contains)) && s.Badges[fresh[0]] == today.Ticks, string.Join(",", fresh.ToArray()));
            Check("...and awarding again adds nothing", RangeProgress.Award(s, items, null, today.AddDays(1)).Count == 0 && s.Badges[fresh[0]] == today.Ticks);
            Check("every badge has a name, an icon that exists and a way to earn it", RangeProgress.Badges.All(b => b.Name.Length > 0 && b.How.Length > 0 && RangeLook.HasIcon(b.Icon)) && RangeProgress.Badges.Select(b => b.Id).Distinct().Count() == RangeProgress.Badges.Length && RangeProgress.Badges.Length >= 7);

            // ---- badges from a run: Unassisted (no hints), and not when a hint was used or the run was given up
            var pz = rangePuzzles.First(p => p.Id == "b01-base64");
            var run = RangeRun.ForPuzzle(pz, 5); run.StartConsole();
            bool ok; run.Submit(pz.Answer == "flag" ? run.Flags[""] : RangeData.Fill(pz.Answer, run.Vars, run.Flags), out ok);
            Check("a run solved without hints earns Unassisted", ok && RangeProgress.Earned(new RangeStore(), items, run).Contains("no-hints"));
            run.TakeHint(0);
            Check("...a hint taken (even free in Learning mode) does not", !RangeProgress.Earned(new RangeStore(), items, run).Contains("no-hints"));
            run.HintsUsed.Clear(); run.GaveUp = true;
            Check("...nor does a run given up", !RangeProgress.Earned(new RangeStore(), items, run).Contains("no-hints"));
            run.Dispose();

            // ---- Ghost: a Red vs Defender run won without one alert (its quiet model solution)
            var def = rangeScenarios.FirstOrDefault(x => x.Category == "defender" && x.Level == "Beginner");
            if (def != null)
            {
                var dr = RangeRun.ForScenario(def, 7); dr.StartConsole();
                RangeType(dr, def.Solution);
                foreach (var f in def.Flags) dr.Submit(dr.Flags[f.Id], out ok);
                Check("a defender beaten quietly earns Ghost", dr.AllFound && dr.Defender != null && dr.Defender.Alerts == 0 && RangeProgress.Earned(new RangeStore(), items, dr).Contains("ghost"), dr.Defender == null ? "no defender" : "alerts " + dr.Defender.Alerts);
                dr.Defender.Alerts = 1;
                Check("...and not with an alert", !RangeProgress.Earned(new RangeStore(), items, dr).Contains("ghost"));
                dr.Dispose();
            }

            // ---- what to play next
            bool resuming;
            var n0 = RangeProgress.Next(new RangeStore(), items, out resuming);
            Check("nothing played: Continue offers a Beginner Red Team challenge", n0 != null && n0.Level == "Beginner" && n0.Category == RangeData.Categories[0].Id && !resuming, n0 == null ? "null" : n0.Id);
            var sn = new RangeStore();
            var od = items.First(i => i.Id == "open-door");
            sn.For(false, od.Id).LastPlayed = today.Ticks; sn.For(false, od.Id).Attempts = 1;
            var n1 = RangeProgress.Next(sn, items, out resuming);
            Check("the last challenge played and not finished is offered to continue", n1 == od && resuming);
            sn.For(false, od.Id).Completed = true;
            var n2 = RangeProgress.Next(sn, items, out resuming);
            Check("...once it is done, the next unfinished one in its category and level comes next", n2 != null && n2 != od && n2.Category == od.Category && n2.Level == od.Level && !resuming, n2 == null ? "null" : n2.Id);
            var all = new RangeStore(); foreach (var i in items) all.For(i.Puzzle, i.Id).Completed = true;
            Check("...and nothing when everything is done", RangeProgress.Next(all, items, out resuming) == null);

            // ---- minutes: every challenge has a guide, from its file or its level
            Check("every challenge has a time guide", items.All(i => i.Minutes > 0));
            Check("a puzzle without minutes gets its level's default", RangeData.DefaultMinutes("Beginner", true) == 5 && RangeData.DefaultMinutes("Expert", true) == 25 && rangePuzzles.Where(p => !p.Raw.Contains("\nminutes:") && !p.Raw.StartsWith("minutes:")).All(p => p.Minutes == RangeData.DefaultMinutes(p.Level, true)));
            Check("a scenario without minutes gets its level's default", rangeScenarios.Where(x => x.Raw != null && !x.Raw.ContainsKey("minutes")).All(x => x.Minutes == RangeData.DefaultMinutes(x.Level, false)));
            var tp = RangeData.ParsePuzzleForTests("title: T\nlevel: Advanced\nkind: Decoding\nminutes: 12\nsimple: In plain words.\n@intro\nx\n@setup\ntrue\n");
            Check("a puzzle's minutes and simple lines are read", tp.Minutes == 12 && tp.Simple == "In plain words.");

            // ---- progress.json version 2, and version 1 upgraded
            string root = Path.Combine(TmpRoot, "range-saves2");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string f2 = SaveFixture(root, "second", "progress.json", RangeSecond);
            var v2 = RangeStore.Load(f2, false);
            Check("range progress v2 opens with badges, days and the tour", v2.LoadProblem == "" && v2.Version == 2 && v2.Badges.Count == 2 && v2.Badges["first-flag"] == 638640000000000000 && v2.Days.Count == 3 && v2.TourSeen && v2.For(false, "open-door").BestScore == 230, v2.LoadProblem);
            v2.Save();
            var v2b = RangeStore.Load(f2, false);
            Check("...saved and reopened, nothing lost", v2b.Badges.Count == 2 && v2b.Days.SequenceEqual(v2.Days) && v2b.TourSeen && File.ReadAllText(f2 + ".bak1") == RangeSecond);
            string f1 = SaveFixture(root, "first", "progress.json", RangeFirst);
            var v1 = RangeStore.Load(f1, false);
            string playedDay = RangeProgress.Day(new DateTime(638640000000000000));
            Check("a v1 file upgrades in memory: the day it was played counts for the streak", v1.LoadProblem == "" && v1.LoadedVersion == 1 && v1.Days.Count == 1 && v1.Days[0] == playedDay && v1.Badges.Count == 0 && !v1.TourSeen, string.Join(",", v1.Days.ToArray()));
            Check("...and the file is untouched until something is saved", File.ReadAllText(f1) == RangeFirst);
            var got = RangeProgress.Award(v1, items, null, today);
            Check("...its badges come from what it already shows", got.Contains("first-flag"));
            v1.Save();
            var v1b = RangeStore.Load(f1, false);
            Check("...saved as version 2 with the badges and days, results kept", v1b.Version == 2 && v1b.Badges.ContainsKey("first-flag") && v1b.Days.Contains(playedDay) && v1b.For(false, "open-door").Notes == "ftp had the password" && File.ReadAllText(f1 + ".bak1") == RangeFirst);
            string fn = SaveFixture(root, "noversion", "progress.json", RangeNoVersion);
            var vn = RangeStore.Load(fn, false);
            Check("a file from before the version number still opens (read as v1, no days to add)", vn.LoadProblem == "" && vn.LoadedVersion == 1 && vn.Days.Count == 0 && vn.For(false, "robots-and-roots").BestScore == 100);
            string f9 = SaveFixture(root, "newer", "progress.json", RangeSecond.Replace("\"Version\":2", "\"Version\":9"));
            var v9 = RangeStore.Load(f9, false);
            Check("a newer version's file opens, keeps its badges, and the person is told", v9.LoadProblem.Contains("newer") && v9.Badges.Count == 2);
            string fb = SaveFixture(root, "broken", "progress.json", "{\"Version\":2,\"Badges\":{\"first");
            var vb = RangeStore.Load(fb, false);
            Check("a damaged v2 file with no backup is never written over", vb.LoadProblem.Length > 0 && Directory.GetFiles(Path.GetDirectoryName(fb), "progress.json.unreadable-*").Length == 1);

            // ---- recording a run marks the day and the time it was played
            var rs = new RangeStore { File = Path.Combine(root, "rec", "progress.json") };
            var pr = RangeRun.ForPuzzle(pz, 9); pr.StartConsole(); pr.Submit(pz.Answer == "flag" ? pr.Flags[""] : RangeData.Fill(pz.Answer, pr.Vars, pr.Flags), out ok);
            rs.Record(pr, false);
            Check("recording a run marks today as played (without saving when asked not to)", rs.Days.Contains(RangeProgress.Day(DateTime.Now)) && rs.For(true, pz.Id).LastPlayed > 0 && !File.Exists(rs.File));
            pr.Dispose();
        }

        static void RangeGlossaryTests()
        {
            string file = Path.Combine(AppRootForTests, "Range", "glossary.txt");
            RangeGlossary.Load(file);
            Check("the glossary loads with no problems and about 40 terms or more", RangeGlossary.Problems.Count == 0 && RangeGlossary.Terms.Count >= 40, string.Join("; ", RangeGlossary.Problems.ToArray()) + " terms " + RangeGlossary.Terms.Count);
            Check("the glossary file is plain ASCII", File.ReadAllBytes(file).All(b => b < 128));
            var dupes = RangeGlossary.Terms.SelectMany(t => t.Names.Select(n => n.ToLowerInvariant())).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("no spelling belongs to two terms", dupes.Count == 0, string.Join(", ", dupes.ToArray()));
            var longOnes = RangeGlossary.Terms.Where(t => t.Text.Length > 260 || t.Text.Split(new[] { ". " }, StringSplitOptions.None).Length > 3).Select(t => t.Term).ToList();
            Check("every explanation is one or two short sentences", longOnes.Count == 0, string.Join(", ", longOnes.ToArray()));
            foreach (var w in new[] { "flag", "port", "hash", "sudo", "shell", "log", "IP address", "password cracking", "privilege", "root", "firewall", "encryption", "encoding" })
                Check("the glossary explains " + w, RangeGlossary.Find(w) != null);

            string sample = "Scan the open ports, then read the logs. The report says nothing.";
            var hits = RangeGlossary.Matches(sample);
            var names = hits.Select(h => h.Term.Term).ToList();
            int rep = sample.IndexOf("report", StringComparison.Ordinal);
            Check("words are found whole, plurals included, not inside other words", names.Contains("scan") && names.Contains("port") && names.Contains("log") && hits.All(h => h.Start + h.Length <= rep || h.Start >= rep + 6), string.Join(",", names.ToArray()));
            var hp = RangeGlossary.Matches("Try password cracking on the password hash.");
            Check("the longest spelling wins (password cracking, not password)", hp.Count >= 1 && hp[0].Term.Term == "password cracking" && hp[0].Length == "password cracking".Length);
            Check("words inside addresses, paths and file names are not linked", RangeGlossary.Matches("open http://shop.example/ then /var/log/secure and access.log").Count == 0);
            Check("each term is linked once per text", RangeGlossary.Matches("port port port").Count == 1);
            Check("terms in several texts, in order, once each", RangeGlossary.TermsIn("a flag on a server", "the flag again, and sudo").Select(t => t.Term).SequenceEqual(new[] { "flag", "server", "sudo" }));

            // a broken file is reported, not fatal
            RangeGlossary.Terms.Clear(); RangeGlossary.Problems.Clear();
            RangeGlossary.Parse("stray text\n== good | goods\nA good thing.\n== empty\n");
            Check("a broken glossary reports its problems and keeps the good entries", RangeGlossary.Terms.Count == 1 && RangeGlossary.Problems.Count == 2);
            RangeGlossary.Load(Path.Combine(TmpRoot, "no-such-glossary.txt"));
            Check("a missing glossary leaves it empty with a problem noted", RangeGlossary.Terms.Count == 0 && RangeGlossary.Problems.Count == 1 && RangeGlossary.Matches("a flag").Count == 0);
            RangeGlossary.Load(file);
        }

        static void RangeLookTests()
        {
            // styles: the known categories, and categories added later
            foreach (var c in RangeData.Categories)
            {
                var st = RangeLook.For(c);
                Check("category " + c.Id + " has a drawn icon and colours", RangeLook.HasIcon(st.Icon) && st.Accent.StartsWith("#") && st.Accent2.StartsWith("#") && st.Simple.Length > 20);
            }
            Check("a new web category gets the globe", RangeLook.For("web-lab", "Web Security Lab").Icon == "globe");
            Check("a new Linux ladder gets the terminal", RangeLook.For("linux", "Linux Ladder").Icon == "terminal");
            Check("a new SOC triage category gets the bell", RangeLook.For("soc", "SOC Alert Triage").Icon == "bell");
            Check("a new cloud category gets the cloud", RangeLook.For("cloud", "Cloud Misconfiguration").Icon == "cloud");
            var unk = RangeLook.For("zz-mystery", "Something New");
            Check("an unknown category still gets an icon, colours and plain words", RangeLook.HasIcon(unk.Icon) && unk.Accent.StartsWith("#") && unk.Simple.Length > 20 && RangeLook.For("zz-mystery", "Something New").Accent == unk.Accent);
            Check("four level styles in level order", RangeLook.Levels.Select(l => l.Name).SequenceEqual(RangeData.Levels) && RangeLook.Levels.All(l => l.Purpose.Length > 0 && l.Kid.Length > 0));
            Check("unwrapping keeps paragraphs and indented code", RangeLook.Unwrap("one\ntwo\n\nthree\n    code\nfour") == "one two\n\nthree\n    code\nfour");
        }
    }
}
