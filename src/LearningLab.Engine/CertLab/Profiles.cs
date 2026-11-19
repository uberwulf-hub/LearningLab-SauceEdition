// Cert Lab - people and their progress, per course: lessons read, quiz scores, labs passed, exam attempts and the
// flash cards' Leitner boxes. Kept in profiles.json in the data folder, saved safely (SafeSave.cs: a temporary file
// put in place, the last five copies kept as profiles.json.bak1..bak5; a file that can't be read is copied aside).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class CardState { public int Box = 1; public long DueDay; public int Seen, Right; }
    public class ExamAttempt { public string Exam = ""; public int Seed; public long When; public int Score, Max, Scaled, Pass; public bool Passed; public Dictionary<string, List<int>> PerObjective = new Dictionary<string, List<int>>(); public List<string> Failed = new List<string>(); }

    public class CourseProgress
    {
        public List<string> Lessons = new List<string>();
        public Dictionary<string, int> Quiz = new Dictionary<string, int>();
        public List<string> TriesDone = new List<string>();
        public Dictionary<string, int> Labs = new Dictionary<string, int>();
        public List<ExamAttempt> Exams = new List<ExamAttempt>();
        public Dictionary<string, CardState> Cards = new Dictionary<string, CardState>();
        public string LastLesson = "";
        public int ExamMinutes;
    }

    public class Profile
    {
        public string Name = "";
        public long Created;
        public Dictionary<string, CourseProgress> Courses = new Dictionary<string, CourseProgress>();
        public CourseProgress For(string course)
        {
            CourseProgress p;
            if (!Courses.TryGetValue(course, out p)) { p = new CourseProgress(); Courses[course] = p; }
            return p;
        }
    }

    public class ProfileStore
    {
        public List<Profile> Profiles = new List<Profile>();
        public string LastProfile = "", LastCourse = "";
        [NonSerialized] public string File;
        [NonSerialized] public bool ReadOnly;

        /// <summary>The layout of profiles.json this Cert Lab writes (files from before 2026-10-18 have no Version: 1).</summary>
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        /// <summary>A sentence for the person when profiles.json couldn't be opened (or came from a newer Cert Lab).</summary>
        [NonSerialized] public string LoadProblem = "";
        /// <summary>Why the last save failed (empty when it worked).</summary>
        [NonSerialized] public string SaveProblem = "";

        /// <summary>Opens profiles.json safely (SafeSave.cs): a file that can't be read is copied to
        /// profiles.json.unreadable-time and never written over, and the newest backup is used (.bak1..bak5, then an
        /// older Cert Lab's .bak). Fields it doesn't know are skipped, missing ones get their defaults.</summary>
        public static ProfileStore Load(string file, bool readOnly)
        {
            ProfileStore s = null;
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
                problem = "Your saved progress couldn't be opened, and no backup could either. Nothing was written over it: it is kept as " + Path.GetFileName(ex.Quarantined) + ".";
            }
            if (s == null) s = new ProfileStore();
            if (s.Version > CurrentVersion)
            {
                LearningLab.Saves.SafeFile.MarkUnreadable(file);   // a newer Cert Lab's file: copied aside whole before the first save
                if (problem == "") problem = "Your progress was saved by a newer Cert Lab. A copy of it is kept as it was before this one saves.";
            }
            if (s.Profiles == null) s.Profiles = new List<Profile>();
            s.Profiles.RemoveAll(p => p == null);
            foreach (var p in s.Profiles) { if (p.Courses == null) p.Courses = new Dictionary<string, CourseProgress>(); if (p.Name == null) p.Name = ""; }
            if (s.LastProfile == null) s.LastProfile = "";
            if (s.LastCourse == null) s.LastCourse = "";
            s.File = file; s.ReadOnly = readOnly; s.LoadProblem = problem;
            return s;
        }
        static ProfileStore Parse(byte[] bytes)
        {
            string text = new UTF8Encoding(false).GetString(bytes);
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            if (text.Trim().Length == 0) throw new InvalidDataException("The file is empty");
            var j = Json.Parse(text) as JObj;
            if (j == null) throw new InvalidDataException("Not a Cert Lab progress file");
            var s = Ser.FromJson<ProfileStore>(j);
            if (!j.ContainsKey("Version")) s.Version = 1;   // written before the version number existed
            return s;
        }
        public void Save()
        {
            if (ReadOnly || File == null) return;
            try
            {
                Version = CurrentVersion;
                LearningLab.Saves.SafeFile.WriteText(File, Json.Write(Ser.ToJson(this)));
                SaveProblem = "";
            }
            catch (Exception ex) { SaveProblem = "Your progress couldn't be saved: " + ex.Message; }
        }
        public Profile Get(string name) { return Profiles.FirstOrDefault(p => p.Name == name); }
    }

    /// <summary>Leitner boxes: right moves a card up a box (seen again after 1, 2, 4, 8, 16 days), wrong sends it to box 1.</summary>
    public static class Leitner
    {
        public static readonly int[] Days = { 0, 1, 2, 4, 8, 16 };
        public static long Today { get { return (long)(DateTime.Now.Date - new DateTime(1970, 1, 1)).TotalDays; } }

        public static void Answer(CardState s, bool right, long today)
        {
            s.Seen++;
            if (right) { s.Right++; s.Box = Math.Min(5, s.Box + 1); }
            else s.Box = 1;
            s.DueDay = today + Days[s.Box];
        }

        /// <summary>The cards to study now: due ones first (lowest box first), then new ones up to newMax.</summary>
        public static List<Card> Session(IEnumerable<Card> cards, CourseProgress p, long today, int newMax)
        {
            var due = new List<Card>(); var fresh = new List<Card>();
            foreach (var c in cards)
            {
                CardState s;
                if (!p.Cards.TryGetValue(c.Id, out s)) fresh.Add(c);
                else if (s.DueDay <= today) due.Add(c);
            }
            var l = due.OrderBy(c => p.Cards[c.Id].Box).ThenBy(c => p.Cards[c.Id].DueDay).ToList();
            l.AddRange(fresh.Take(newMax));
            return l;
        }

        /// <summary>Type-the-answer: the same command, ignoring extra spaces, quotes style and trailing slashes.</summary>
        public static bool Matches(string typed, Card c)
        {
            Func<string, string> n = s => System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim().Replace("\"", "'"), "\\s+", " ").TrimEnd('/').ToLowerInvariant();
            string t = n(typed);
            if (t.Length == 0) return false;
            if (c.Command != null && n(c.Command) == t) return true;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(c.Back, "`([^`]+)`")) if (n(m.Groups[1].Value) == t) return true;
            return n(c.Back) == t;
        }
    }
}
