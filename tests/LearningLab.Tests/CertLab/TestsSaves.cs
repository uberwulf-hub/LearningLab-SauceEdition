// Cert Lab - checks that people's progress (profiles.json) and saved machines are never lost: every layout that was
// written still opens with its data, a damaged file is copied aside (never written over) and a backup used, and a
// newer Cert Lab's file is kept whole. Part of "CertLab.ps1 -Test" (group "saves").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        // profiles.json as the first Cert Lab wrote it (19c2e92: no LastCourse, no Version)
        const string ProfilesFirst = "{\"Profiles\":[{\"Name\":\"Ann\",\"Created\":638640000000000000,\"Courses\":{\"rhcsa\":{\"Lessons\":[\"l0101\",\"l0102\"],\"Quiz\":{\"u01\":8},\"TriesDone\":[\"l0101-1\"],\"Labs\":{\"lab-01\":100},\"Exams\":[{\"Exam\":\"exam-1\",\"Seed\":42,\"When\":638641000000000000,\"Score\":210,\"Max\":300,\"Scaled\":245,\"Pass\":210,\"Passed\":true,\"PerObjective\":{\"storage\":[3,4]},\"Failed\":[\"t5\"]}],\"Cards\":{\"c1\":{\"Box\":3,\"DueDay\":20380,\"Seen\":5,\"Right\":4}},\"LastLesson\":\"l0102\",\"ExamMinutes\":150}}}],\"LastProfile\":\"Ann\"}";
        // ...and from 689c0dc until 2026-10-18 (LastCourse added, still no Version)
        const string ProfilesSecond = "{\"Profiles\":[{\"Name\":\"Ann\",\"Created\":638640000000000000,\"Courses\":{\"rhcsa\":{\"Lessons\":[\"l0101\"],\"Quiz\":{},\"TriesDone\":[],\"Labs\":{\"lab-02\":80},\"Exams\":[],\"Cards\":{},\"LastLesson\":\"l0101\",\"ExamMinutes\":0},\"powershell\":{\"Lessons\":[\"p01\"],\"Quiz\":{},\"TriesDone\":[],\"Labs\":{},\"Exams\":[],\"Cards\":{},\"LastLesson\":\"\",\"ExamMinutes\":0}}},{\"Name\":\"Ben\",\"Created\":0,\"Courses\":{}}],\"LastProfile\":\"Ben\",\"LastCourse\":\"powershell\"}";

        static void SafeSaveTests()
        {
            string root = Path.Combine(TmpRoot, "saves");
            if (Directory.Exists(root)) Directory.Delete(root, true);

            // 1. the first layout
            string f1 = SaveFixture(root, "first", "profiles.json", ProfilesFirst);
            var s = ProfileStore.Load(f1, false);
            var ann = s.Get("Ann");
            Check("the first profiles.json layout opens", s.LoadProblem == "" && ann != null && s.LastProfile == "Ann" && s.LastCourse == "", s.LoadProblem);
            var rh = ann == null ? new CourseProgress() : ann.For("rhcsa");
            Check("...with lessons, quizzes, tries, labs and the last lesson", rh.Lessons.Count == 2 && rh.Quiz["u01"] == 8 && rh.TriesDone.Contains("l0101-1") && rh.Labs["lab-01"] == 100 && rh.LastLesson == "l0102" && rh.ExamMinutes == 150);
            Check("...with exam attempts and flash card boxes", rh.Exams.Count == 1 && rh.Exams[0].Passed && rh.Exams[0].Scaled == 245 && rh.Exams[0].PerObjective["storage"][1] == 4 && rh.Exams[0].Failed[0] == "t5" && rh.Cards["c1"].Box == 3 && rh.Cards["c1"].Right == 4);
            s.Save();
            var s2 = ProfileStore.Load(f1, false);
            Check("...saved again (now with Version 1) and reopened, nothing lost; the copy before is profiles.json.bak1", s2.Get("Ann").For("rhcsa").Exams.Count == 1 && s2.Version == ProfileStore.CurrentVersion && File.ReadAllText(f1 + ".bak1") == ProfilesFirst && File.ReadAllText(f1).Contains("\"Version\""));

            // 2. the second layout
            string f2 = SaveFixture(root, "second", "profiles.json", ProfilesSecond);
            var t = ProfileStore.Load(f2, false);
            Check("the second profiles.json layout opens with both people and the chosen course", t.LoadProblem == "" && t.Profiles.Count == 2 && t.LastProfile == "Ben" && t.LastCourse == "powershell" && t.Get("Ann").For("powershell").Lessons[0] == "p01" && t.Get("Ann").For("rhcsa").Labs["lab-02"] == 80);

            // 3. a newer Cert Lab's file: opens, unknown parts skipped, kept whole before the first save
            string newer = ProfilesSecond.Replace("{\"Profiles\"", "{\"Version\":5,\"Streaks\":{\"Ann\":3},\"Profiles\"");
            string f3 = SaveFixture(root, "newer", "profiles.json", newer);
            var n = ProfileStore.Load(f3, false);
            Check("a newer Cert Lab's profiles.json opens and the person is told", n.Profiles.Count == 2 && n.LoadProblem.Contains("newer"), n.LoadProblem);
            n.Save();
            var kept = Directory.GetFiles(Path.GetDirectoryName(f3), "profiles.json.unreadable-*");
            Check("...and is kept whole before this one saves", kept.Length == 1 && File.ReadAllText(kept[0]) == newer);

            // 4. damaged with backups
            string f4 = SaveFixture(root, "damaged", "profiles.json", ProfilesSecond);
            var a = ProfileStore.Load(f4, false); a.Get("Ben").For("rhcsa").Lessons.Add("l0201"); a.Save(); a.Get("Ben").For("rhcsa").Lessons.Add("l0202"); a.Save();
            File.WriteAllText(f4, "{\"Profiles\":[{\"Name\":\"Ann\",\"Cour");
            byte[] broken = File.ReadAllBytes(f4);
            var b = ProfileStore.Load(f4, false);
            var q = Directory.GetFiles(Path.GetDirectoryName(f4), "profiles.json.unreadable-*");
            Check("a damaged profiles.json: the newest backup is used", b.Get("Ben") != null && b.Get("Ben").For("rhcsa").Lessons.Count == 1);
            Check("...the person is told and the damaged file is copied aside byte for byte", b.LoadProblem.Contains("couldn't be opened") && q.Length == 1 && File.ReadAllBytes(q[0]).SequenceEqual(broken), b.LoadProblem);
            Check("...opening it changed nothing on disk", File.ReadAllBytes(f4).SequenceEqual(broken));
            b.Save();
            Check("...saving afterwards keeps the damaged copy and writes good progress", Directory.GetFiles(Path.GetDirectoryName(f4), "profiles.json.unreadable-*").Length == 1 && ProfileStore.Load(f4, false).Get("Ben").For("rhcsa").Lessons.Count == 1);

            // 5. an older Cert Lab's single .bak is still used; damaged without any backup is kept
            string f5 = SaveFixture(root, "oldbak", "profiles.json", "nonsense");
            File.WriteAllText(f5 + ".bak", ProfilesFirst);
            Check("a damaged profiles.json with an older Cert Lab's .bak: the .bak is used", ProfileStore.Load(f5, false).Get("Ann") != null);
            string f6 = SaveFixture(root, "nobackup", "profiles.json", "{ broken");
            var e = ProfileStore.Load(f6, false);
            Check("damaged without backups: an empty start, and the person is told it is kept", e.Profiles.Count == 0 && e.LoadProblem.Contains("Nothing was written over it"), e.LoadProblem);
            e.Profiles.Add(new Profile { Name = "New" }); e.Save();
            var q6 = Directory.GetFiles(Path.GetDirectoryName(f6), "profiles.json.unreadable-*");
            Check("...and stays as profiles.json.unreadable-<time> after the next save", q6.Length == 1 && File.ReadAllText(q6[0]) == "{ broken");
            var ro = ProfileStore.Load(SaveFixture(root, "readonly", "profiles.json", ProfilesSecond), true); ro.Profiles.Clear(); ro.Save();
            Check("pictures and tests (read only) never write the file", ProfileStore.Load(Path.Combine(root, "readonly", "profiles.json"), true).Profiles.Count == 2);

            // 6. saved machines: open, damaged with a backup, damaged without
            string md = Path.Combine(root, "machines"); Directory.CreateDirectory(md);
            string mf = Path.Combine(md, "machines-Ann.json");
            var sim = RhelSim.Create(1, 7);
            string saved = sim.Save(); sim.Dispose();
            LearningLab.Saves.SafeFile.WriteText(mf, saved);
            LearningLab.Saves.SaveLoad info;
            var back = CertLabData.OpenMachines(mf, out info);
            Check("saved machines open again", back.MachineNames.Count == 1 && info.Message == "", info.Message);
            back.Dispose();
            LearningLab.Saves.SafeFile.WriteText(mf, saved);   // the first save is now .bak1
            File.WriteAllText(mf, saved.Substring(0, saved.Length / 2));
            var fromBak = CertLabData.OpenMachines(mf, out info);
            Check("damaged saved machines: the backup opens and the person is told", fromBak.MachineNames.Count == 1 && info.UsedBackup && info.Message.Contains(".unreadable-"), info.Message);
            fromBak.Dispose();
            string mf2 = Path.Combine(md, "machines-Ben.json");
            File.WriteAllText(mf2, "{\"Machines\":[");
            bool threw = false;
            try { CertLabData.OpenMachines(mf2, out info).Dispose(); } catch (LearningLab.Saves.SaveUnreadableException) { threw = true; }
            Check("damaged saved machines without a backup: refused, kept as they were and copied aside", threw && File.ReadAllText(mf2) == "{\"Machines\":[" && Directory.GetFiles(md, "machines-Ben.json.unreadable-*").Length == 1);
            try { Directory.Delete(root, true); } catch (Exception) { }
        }

        static string SaveFixture(string root, string name, string file, string text)
        {
            string d = Path.Combine(root, name); Directory.CreateDirectory(d);
            string f = Path.Combine(d, file); File.WriteAllText(f, text, new UTF8Encoding(false));
            return f;
        }
    }
}
