// Cyber Range - a player's progress: best score, which flags they have captured, hints taken, best time, and the
// notes they kept, per scenario and per puzzle. Kept in %APPDATA%\CertLab\range\progress.json through the shared
// safe-save helper (LearningLab.Saves.SafeFile), the same way the courses' profiles.json is kept. A file that can't be
// opened is never written over; a newer version's file is copied aside before the first save; older versions load.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class RangeResult
    {
        public int BestScore;
        public bool Completed;
        public long BestSeconds;
        public List<string> Flags = new List<string>();   // flag ids ever captured (across runs)
        public int Attempts;
        public string Notes = "";
        public long LastPlayed;
    }

    public class RangeStore
    {
        // Version 1: results, AutoCapture, LastCategory / LastLevel.
        // Version 2 (2026-10-23): Badges (id -> when earned, ticks), Days (yyyy-MM-dd played, for the streak),
        // TourSeen; LastPlayed is also set when a run starts. A version 1 file gets Days from its results' LastPlayed
        // (in memory; written at the next save) and its badges from what it already shows (RangeView, at start).
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public Dictionary<string, RangeResult> Scenarios = new Dictionary<string, RangeResult>(StringComparer.Ordinal);
        public Dictionary<string, RangeResult> Puzzles = new Dictionary<string, RangeResult>(StringComparer.Ordinal);
        public bool AutoCapture = true;        // add credentials that worked to the loot list without asking
        public string LastCategory = "";       // the category / level last opened (a per-person convenience)
        public string LastLevel = "";
        public Dictionary<string, long> Badges = new Dictionary<string, long>(StringComparer.Ordinal);
        public List<string> Days = new List<string>();
        public bool TourSeen;
        [NonSerialized] public int LoadedVersion = CurrentVersion;

        [NonSerialized] public string File;
        [NonSerialized] public bool ReadOnly;
        [NonSerialized] public string LoadProblem = "";
        [NonSerialized] public string SaveProblem = "";

        public RangeResult For(bool puzzle, string id)
        {
            var d = puzzle ? Puzzles : Scenarios;
            RangeResult r;
            if (!d.TryGetValue(id, out r)) { r = new RangeResult(); d[id] = r; }
            return r;
        }

        public static RangeStore Load(string file, bool readOnly)
        {
            RangeStore s = null;
            string problem = "";
            try
            {
                LearningLab.Saves.SaveLoad info;
                s = LearningLab.Saves.SafeFile.Load(file, Parse, out info);
                problem = info.Message;
            }
            catch (FileNotFoundException) { }
            catch (LearningLab.Saves.SaveUnreadableException ex)
            {
                problem = "Your Cyber Range progress couldn't be opened, and no backup could either. Nothing was written over it: it is kept as " + Path.GetFileName(ex.Quarantined) + ".";
            }
            if (s == null) s = new RangeStore();
            if (s.Version > CurrentVersion)
            {
                LearningLab.Saves.SafeFile.MarkUnreadable(file);
                if (problem == "") problem = "Your Cyber Range progress was saved by a newer Cert Lab. A copy of it is kept as it was before this one saves.";
            }
            if (s.Scenarios == null) s.Scenarios = new Dictionary<string, RangeResult>(StringComparer.Ordinal);
            if (s.Puzzles == null) s.Puzzles = new Dictionary<string, RangeResult>(StringComparer.Ordinal);
            foreach (var r in s.Scenarios.Values.Concat(s.Puzzles.Values)) { if (r.Flags == null) r.Flags = new List<string>(); if (r.Notes == null) r.Notes = ""; }
            if (s.Badges == null) s.Badges = new Dictionary<string, long>(StringComparer.Ordinal);
            if (s.Days == null) s.Days = new List<string>();
            s.LoadedVersion = s.Version;
            if (s.Version < 2)
            {
                // upgrade in memory: the days played come from each result's LastPlayed
                foreach (var r in s.Scenarios.Values.Concat(s.Puzzles.Values))
                    if (r.LastPlayed > 0 && r.LastPlayed < DateTime.MaxValue.Ticks) RangeProgress.MarkDay(s, new DateTime(r.LastPlayed));
            }
            s.File = file; s.ReadOnly = readOnly; s.LoadProblem = problem;
            return s;
        }
        static RangeStore Parse(byte[] bytes)
        {
            string text = new UTF8Encoding(false).GetString(bytes);
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            if (text.Trim().Length == 0) throw new InvalidDataException("The file is empty");
            var j = Json.Parse(text) as JObj;
            if (j == null) throw new InvalidDataException("Not a Cyber Range progress file");
            var s = Ser.FromJson<RangeStore>(j);
            if (!j.ContainsKey("Version")) s.Version = 1;
            return s;
        }
        public void Save()
        {
            if (ReadOnly || File == null) return;
            try
            {
                Version = CurrentVersion;
                var dir = Path.GetDirectoryName(File);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                LearningLab.Saves.SafeFile.WriteText(File, Json.Write(Ser.ToJson(this)));
                SaveProblem = "";
            }
            catch (Exception ex) { SaveProblem = "Your Cyber Range progress couldn't be saved: " + ex.Message; }
        }

        /// <summary>Notes that a run started: last played, and today for the streak (saved with the next save).</summary>
        public void Touch(bool puzzle, string id, DateTime now)
        {
            For(puzzle, id).LastPlayed = now.Ticks;
            RangeProgress.MarkDay(this, now);
        }

        /// <summary>Records the end of a run (best score, flags, time, notes) and saves.</summary>
        public void Record(RangeRun run) { Record(run, true); }
        public void Record(RangeRun run, bool save)
        {
            var r = For(run.Pz != null, run.Id);
            r.Attempts++;
            r.LastPlayed = DateTime.Now.Ticks;
            RangeProgress.MarkDay(this, DateTime.Now);
            foreach (var f in run.Found) if (!r.Flags.Contains(f)) r.Flags.Add(f);
            if (run.Score > r.BestScore) r.BestScore = run.Score;
            if (run.AllFound)
            {
                r.Completed = true;
                if (r.BestSeconds == 0 || run.Seconds < r.BestSeconds) r.BestSeconds = run.Seconds;
            }
            if (save) Save();
        }
    }
}
