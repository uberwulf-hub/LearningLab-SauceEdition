// Cert Lab and Cyber Range - when App Hub updates, it closes the open apps after a 30-second notice
// (UI\UpdateClose.ps1). Just before Cert Lab closes, SaveForUpdate keeps where the person is in a resume file:
//   Cert Lab:    <data folder>\resume.json        an exam in progress (which exam, its seed, the answers, flags and
//                                                  the item shown, the time that was left: the clock waits while
//                                                  closed) or the practice machines / lab / try-it on screen, with
//                                                  the RHEL machines themselves (the same JSON as Save machines)
//   Cyber Range: <data folder>\range\resume.json  a run in progress (the challenge, its seed: the same randomized
//                                                  details come back, the flags found, answers, hints, time, map,
//                                                  loot and notes, and how the blue team attacker's steps went)
// When the window opens again, ResumeOffer gives the banner's sentence and ResumeNow ("Carry on where you left
// off") brings it back; the file is then marked Used (never deleted). Both files go through LearningLab.Saves.SafeFile
// (backups, a file that can't be read is copied aside and never written over). Format version 1 (2026-10-24);
// every later version must keep loading these (fixtures: tools\SaveFixtures\CertLab and TestsResume.cs).
// Honest limits: the PowerShell, MD-102 and AZ-800 simulators can't be saved, so their machines start again from
// the beginning; Cyber Range's pretend machines are rebuilt from the seed (with the clock and the attacker's
// steps replayed), so changes the player made on them must be made again (blue team fixes count again then).
// Learning Lab: the files' formats (CertResume, RangeResume) are here; SaveForUpdate / ResumeNow, which App Hub had as
// parts of MainView, ExamRunPage and RangeView, come back with the screens.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class CertResume
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public long Saved;                 // DateTime ticks
        public bool Used;                  // brought back already
        public string Kind = "";           // exam, practice, lab, try
        public string Profile = "", Course = "", Set = "", Title = "";   // Set: the exam or lab id, the lesson id of a try-it
        public int Seed, Minutes, MachineCount;
        public long SecondsLeft;
        public Dictionary<string, int> Answers = new Dictionary<string, int>();
        public Dictionary<string, List<string>> More = new Dictionary<string, List<string>>();
        public List<string> Flags = new List<string>();
        public int CurrentItem;
        public string Machines = "";       // the RHEL machines (LabNet JSON); "" when they couldn't be kept

        public static string FileIn(string dataDir) { return Path.Combine(dataDir, "resume.json"); }

        /// <summary>null when there is none; problem = a sentence for the person when it couldn't be read (the file is
        /// then copied aside by SafeFile and never written over).</summary>
        public static CertResume Load(string file, out string problem)
        {
            problem = "";
            try
            {
                LearningLab.Saves.SaveLoad info;
                var r = LearningLab.Saves.SafeFile.Load(file, Parse, out info);
                if (r.Version > CurrentVersion) LearningLab.Saves.SafeFile.MarkUnreadable(file);   // a newer Cert Lab's: kept whole
                return r;
            }
            catch (FileNotFoundException) { return null; }
            catch (LearningLab.Saves.SaveUnreadableException ex)
            {
                problem = "Where you were before the App Hub update couldn't be opened. Nothing was written over it: it is kept as " + Path.GetFileName(ex.Quarantined) + ".";
                return null;
            }
        }
        static CertResume Parse(byte[] bytes)
        {
            string text = new UTF8Encoding(false).GetString(bytes);
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            var j = Json.Parse(text) as JObj;
            if (j == null || !j.ContainsKey("Kind")) throw new InvalidDataException("Not a Cert Lab resume file");
            var r = Ser.FromJson<CertResume>(j);
            if (!j.ContainsKey("Version")) r.Version = 1;
            if (r.Answers == null) r.Answers = new Dictionary<string, int>();
            if (r.More == null) r.More = new Dictionary<string, List<string>>();
            if (r.Flags == null) r.Flags = new List<string>();
            foreach (var k in r.More.Keys.ToList()) if (r.More[k] == null) r.More[k] = new List<string>();
            foreach (var f in new[] { "Kind", "Profile", "Course", "Set", "Title", "Machines" })
            {
                var fi = typeof(CertResume).GetField(f);
                if (fi.GetValue(r) == null) fi.SetValue(r, "");
            }
            return r;
        }
        public void Save(string file)
        {
            Version = CurrentVersion;
            var dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            LearningLab.Saves.SafeFile.WriteText(file, Json.Write(Ser.ToJson(this)));
        }
        /// <summary>Still worth offering: not brought back yet, this version or older, saved in the last 14 days.</summary>
        public bool Pending
        {
            get
            {
                if (Used || Version > CurrentVersion || Saved <= 0 || Saved >= DateTime.MaxValue.Ticks) return false;
                var age = DateTime.Now - new DateTime(Saved);
                return age < TimeSpan.FromDays(14) && age > TimeSpan.FromDays(-1);
            }
        }
        public static string Clock(long secs)
        {
            if (secs < 0) secs = 0;
            return (secs / 3600) + ":" + (secs % 3600 / 60).ToString("00") + ":" + (secs % 60).ToString("00");
        }
    }

    // ------------------------------------------------------------ Cyber Range
    public class RangeResume
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public long Saved;
        public bool Used;
        public bool Puzzle;
        public string Id = "", Title = "";
        public int Seed;
        public bool LearningMode, Breached;
        public long Seconds, Clock;        // Seconds: time in the run; Clock: seconds on the scenario's pretend clock
        public List<string> Found = new List<string>();
        public List<int> HintsUsed = new List<int>();
        public Dictionary<string, string> Answered = new Dictionary<string, string>();
        public List<string> Missed = new List<string>();
        public Dictionary<string, int> WrongTries = new Dictionary<string, int>();
        public List<string> AttackerSteps = new List<string>();
        public List<string> AttackerLog = new List<string>();
        public List<SeenHost> Seen = new List<SeenHost>();
        public List<LootItem> Loot = new List<LootItem>();
        public string Notes = "";

        public static string FileIn(string dataDir) { return Path.Combine(dataDir, "range", "resume.json"); }

        public static RangeResume Load(string file, out string problem)
        {
            problem = "";
            try
            {
                LearningLab.Saves.SaveLoad info;
                var r = LearningLab.Saves.SafeFile.Load(file, Parse, out info);
                if (r.Version > CurrentVersion) LearningLab.Saves.SafeFile.MarkUnreadable(file);
                return r;
            }
            catch (FileNotFoundException) { return null; }
            catch (LearningLab.Saves.SaveUnreadableException ex)
            {
                problem = "Your challenge from before the App Hub update couldn't be opened. Nothing was written over it: it is kept as " + Path.GetFileName(ex.Quarantined) + ".";
                return null;
            }
        }
        static RangeResume Parse(byte[] bytes)
        {
            string text = new UTF8Encoding(false).GetString(bytes);
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            var j = Json.Parse(text) as JObj;
            if (j == null || !j.ContainsKey("Id")) throw new InvalidDataException("Not a Cyber Range resume file");
            var r = Ser.FromJson<RangeResume>(j);
            if (!j.ContainsKey("Version")) r.Version = 1;
            if (r.Id == null) r.Id = "";
            if (r.Title == null) r.Title = "";
            if (r.Notes == null) r.Notes = "";
            if (r.Found == null) r.Found = new List<string>();
            if (r.HintsUsed == null) r.HintsUsed = new List<int>();
            if (r.Answered == null) r.Answered = new Dictionary<string, string>();
            if (r.Missed == null) r.Missed = new List<string>();
            if (r.WrongTries == null) r.WrongTries = new Dictionary<string, int>();
            if (r.AttackerSteps == null) r.AttackerSteps = new List<string>();
            if (r.AttackerLog == null) r.AttackerLog = new List<string>();
            if (r.Seen == null) r.Seen = new List<SeenHost>();
            if (r.Loot == null) r.Loot = new List<LootItem>();
            r.Seen.RemoveAll(x => x == null || x.Ip == null);
            r.Loot.RemoveAll(x => x == null);
            return r;
        }
        public void Save(string file)
        {
            Version = CurrentVersion;
            var dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            LearningLab.Saves.SafeFile.WriteText(file, Json.Write(Ser.ToJson(this)));
        }
        public bool Pending
        {
            get
            {
                if (Used || Version > CurrentVersion || Saved <= 0 || Saved >= DateTime.MaxValue.Ticks) return false;
                var age = DateTime.Now - new DateTime(Saved);
                return age < TimeSpan.FromDays(14) && age > TimeSpan.FromDays(-1);
            }
        }
    }

}
