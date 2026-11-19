// Cyber Range - checks of the saves and the window (see TestsRange.cs for the rest).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        // Cyber Range progress.json as the first version wrote it (Version 1)
        const string RangeFirst = "{\"Version\":1,\"AutoCapture\":true,\"Scenarios\":{\"open-door\":{\"BestScore\":230,\"Completed\":true,\"BestSeconds\":742,\"Flags\":[\"user\",\"root\"],\"Attempts\":2,\"Notes\":\"ftp had the password\",\"LastPlayed\":638640000000000000}},\"Puzzles\":{\"b01-base64\":{\"BestScore\":40,\"Completed\":true,\"BestSeconds\":65,\"Flags\":[\"flag\"],\"Attempts\":1,\"Notes\":\"\",\"LastPlayed\":0}}}";
        // a pre-version file (no Version field), the shape before the number existed (read as Version 1)
        const string RangeNoVersion = "{\"AutoCapture\":false,\"Scenarios\":{\"robots-and-roots\":{\"BestScore\":100,\"Flags\":[\"user\"],\"Attempts\":1}},\"Puzzles\":{}}";

        static void RangeSaveTests()
        {
            string root = Path.Combine(TmpRoot, "range-saves");
            if (Directory.Exists(root)) Directory.Delete(root, true);

            // 1. the first (and current) layout opens with everything
            string f1 = SaveFixture(root, "first", "progress.json", RangeFirst);
            var s = RangeStore.Load(f1, false);
            var od = s.For(false, "open-door");
            Check("range progress v1 opens with scenarios, puzzles and notes", s.LoadProblem == "" && od.Completed && od.BestScore == 230 && od.BestSeconds == 742 && od.Flags.Contains("root") && od.Notes == "ftp had the password" && s.For(true, "b01-base64").Completed && s.AutoCapture, s.LoadProblem);
            s.Save();
            var s2 = RangeStore.Load(f1, false);
            Check("...saved again and reopened, nothing lost; the copy before is .bak1", s2.For(false, "open-door").BestScore == 230 && s2.Version == RangeStore.CurrentVersion && File.ReadAllText(f1 + ".bak1") == RangeFirst);

            // 2. a file from before the version number (no Version): opens, read as version 1
            string f2 = SaveFixture(root, "noversion", "progress.json", RangeNoVersion);
            var t = RangeStore.Load(f2, false);
            Check("range progress with no Version opens (read as v1)", t.LoadProblem == "" && t.Version == 1 && t.For(false, "robots-and-roots").BestScore == 100 && !t.AutoCapture);

            // 3. a newer version's file: opens, unknown parts skipped, kept whole before the first save
            string newer = RangeFirst.Replace("{\"Version\":1,", "{\"Version\":9,\"Streak\":5,");
            string f3 = SaveFixture(root, "newer", "progress.json", newer);
            var n = RangeStore.Load(f3, false);
            Check("a newer Cyber Range's progress opens and the person is told", n.For(false, "open-door").BestScore == 230 && n.LoadProblem.Contains("newer"), n.LoadProblem);
            n.Save();
            var kept = Directory.GetFiles(Path.GetDirectoryName(f3), "progress.json.unreadable-*");
            Check("...and is kept whole before this one saves", kept.Length == 1 && File.ReadAllText(kept[0]) == newer);

            // 4. a damaged file with backups: the newest backup is used, the damaged file copied aside, nothing lost
            string f4 = SaveFixture(root, "damaged", "progress.json", RangeFirst);
            var a = RangeStore.Load(f4, false); a.For(false, "open-door").Notes = "one"; a.Save(); a.For(false, "open-door").Notes = "two"; a.Save();
            File.WriteAllText(f4, "{\"Scenarios\":{\"open");
            byte[] broken = File.ReadAllBytes(f4);
            var b = RangeStore.Load(f4, false);
            var q = Directory.GetFiles(Path.GetDirectoryName(f4), "progress.json.unreadable-*");
            Check("a damaged progress.json: the newest backup is used", b.For(false, "open-door").Notes == "one", b.For(false, "open-door").Notes);
            Check("...the person is told and the damaged file is copied aside byte for byte", b.LoadProblem.Contains("couldn't be opened") && q.Length == 1 && File.ReadAllBytes(q[0]).SequenceEqual(broken), b.LoadProblem);
            Check("...opening it changed nothing on disk", File.ReadAllBytes(f4).SequenceEqual(broken));

            // 5. recording a run updates best score, flags and time
            LoadRange();
            string f5 = Path.Combine(root, "record", "progress.json");
            var st = RangeStore.Load(f5, false);
            var p = rangePuzzles != null && rangePuzzles.Count > 0 ? rangePuzzles[0] : null;
            if (p != null)
            {
                var run = RangeRun.ForPuzzle(p, 3); run.StartConsole();
                string answer = p.Answer == "flag" ? run.Flags[""] : RangeData.Fill(p.Answer, run.Vars, run.Flags);
                bool ok; run.Submit(answer, out ok); run.Seconds = 90;
                st.Record(run);
                var r = st.For(true, p.Id);
                Check("recording a solved run sets best score, flags and completed", ok && r.Completed && r.BestScore == p.Points && r.Flags.Contains("flag") && r.BestSeconds == 90 && File.Exists(f5), "score " + r.BestScore + " ok=" + ok);
                run.Dispose();
            }

            // 6. each Linux Ladder step keeps its own progress (same file and layout), and it survives reopening
            var steps = rangePuzzles == null ? new List<RangePuzzle>() : rangePuzzles.Where(x => x.Category == "ladder").OrderBy(x => x.Order).Take(2).ToList();
            if (steps.Count == 2)
            {
                string f6 = Path.Combine(root, "ladder", "progress.json");
                var sl = RangeStore.Load(f6, false);
                var run = RangeRun.ForPuzzle(steps[0], 5); run.StartConsole();
                bool ok; run.Submit(RangeData.Fill(steps[0].Answer, run.Vars, run.Flags), out ok); run.Seconds = 40;
                sl.Record(run); run.Dispose();
                var back = RangeStore.Load(f6, false);
                Check("a Linux Ladder step's progress is saved per step and reopens (step 1 done, step 2 untouched)", ok && back.LoadProblem == "" && back.For(true, steps[0].Id).Completed && back.For(true, steps[0].Id).BestScore == steps[0].Points && !back.For(true, steps[1].Id).Completed && back.Version == RangeStore.CurrentVersion, back.LoadProblem);
                // the first layout's file (version 1, from before the ladder) still opens and gains ladder steps
                string f7 = SaveFixture(root, "ladder-old", "progress.json", RangeFirst);
                var old = RangeStore.Load(f7, false);
                old.For(true, steps[0].Id).Completed = true; old.Save();
                var old2 = RangeStore.Load(f7, false);
                Check("...and a version 1 progress file gains ladder steps without losing anything", old2.LoadProblem == "" && old2.For(false, "open-door").BestScore == 230 && old2.For(true, "b01-base64").Completed && old2.For(true, steps[0].Id).Completed && old2.Version == RangeStore.CurrentVersion);
                // and a version 2 file (badges, days, tour) keeps all of that next to ladder steps
                string f8 = SaveFixture(root, "ladder-v2", "progress.json", RangeSecond);
                var v2 = RangeStore.Load(f8, false);
                v2.For(true, steps[0].Id).Completed = true; v2.For(true, steps[1].Id).BestScore = 10; v2.Save();
                var v2b = RangeStore.Load(f8, false);
                Check("...and a version 2 progress file keeps its badges, days and tour next to ladder steps", v2b.LoadProblem == "" && v2b.Badges.ContainsKey("first-flag") && v2b.Days.Count >= 3 && v2b.TourSeen && v2b.For(false, "open-door").BestScore == 230 && v2b.For(true, steps[0].Id).Completed && v2b.For(true, steps[1].Id).BestScore == 10, v2b.LoadProblem);
            }
        }

        static string Net24(RangeScenario s) { var ip = s.Machines.First(m => m.Name == s.Start).FirstIp.Split('.'); return ip[0] + "." + ip[1] + "." + ip[2] + ".0/24"; }
    }
}
