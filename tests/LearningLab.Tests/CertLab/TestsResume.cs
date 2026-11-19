// Cert Lab - checks that closing for an App Hub update keeps where the person is and that "Carry on where you left
// off" brings it back (Resume.cs): an exam's answers, flags, item and time left, the RHEL machines of an exam and of a
// lab, a Cyber Range run with the same seed, flags, hints, time, map, loot and the blue team attacker's steps; the
// version 1 files (tools\SaveFixtures\CertLab, the same text inline here) still open, and a damaged one is copied
// aside and never written over. Part of "CertLab.ps1 -Test" (group "resume").
// Learning Lab: only the files' formats are checked here (the version 1 fixtures in Fixtures\CertLab, damaged files).
// Keeping and bringing back an exam, a lab and a Cyber Range run went through MainView / RangeView (SaveForUpdate,
// ResumeOffer, ResumeNow): those checks come back with the screens.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        // resume.json and range\resume.json, version 1 (2026-10-24)
        const string CertResumeV1 = @"{""Version"":1,""Saved"":638650000000000000,""Used"":false,""Kind"":""exam"",""Profile"":""Ann"",""Course"":""secplus"",""Set"":""exam-1"",""Title"":""Practice exam 1"",""Seed"":3,""Minutes"":90,""MachineCount"":0,""SecondsLeft"":3000,""Answers"":{""q001"":1,""q002"":0},""More"":{""p01"":[]},""Flags"":[""q003""],""CurrentItem"":7,""Machines"":""""}";
        const string RangeResumeV1 = @"{""Version"":1,""Saved"":638650000000000000,""Used"":false,""Puzzle"":true,""Id"":""b01-base64"",""Title"":""Base 64, base case"",""Seed"":4,""LearningMode"":false,""Breached"":false,""Seconds"":95,""Clock"":95,""Found"":[],""HintsUsed"":[0],""Answered"":{},""Missed"":[],""WrongTries"":{},""AttackerSteps"":[],""AttackerLog"":[],""Seen"":[{""Ip"":""10.99.0.10"",""Name"":""puzzlebox"",""Os"":""Linux"",""Ports"":[22],""Owned"":""user"",""Users"":[""player""]}],""Loot"":[{""Host"":""10.99.0.10"",""User"":""player"",""Secret"":""pw"",""Where"":""signed in with ssh"",""Auto"":true}],""Notes"":""my notes from before""}";

        static string ResumeFixture(string name, string inline)
        {
            string f = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CertLab", name);
            Check("fixture " + name + " is there", File.Exists(f), f);
            if (!File.Exists(f)) return inline;
            string t = File.ReadAllText(f).Replace("\r\n", "\n").TrimEnd('\n');
            Check("fixture " + name + " is the same as the one inline", t == inline);
            return t;
        }

        static void ResumeTests()
        {
            string root = Path.Combine(TmpRoot, "resume");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            string problem;

            // ---- the version 1 file, a damaged one
            string d4 = Path.Combine(root, "fixture"); Directory.CreateDirectory(d4);
            string f4 = CertResume.FileIn(d4);
            File.WriteAllText(f4, ResumeFixture("resume-v1.json", CertResumeV1), new UTF8Encoding(false));
            var r1 = CertResume.Load(f4, out problem);
            Check("resume.json version 1 opens", r1 != null && problem == "" && r1.Kind == "exam" && r1.Course == "secplus" && r1.Set == "exam-1" && r1.Seed == 3 && r1.SecondsLeft == 3000 && r1.Answers["q001"] == 1 && r1.Flags[0] == "q003" && r1.CurrentItem == 7 && r1.More["p01"].Count == 0, problem);
            Check("...an old one (more than 14 days) is not offered", r1 != null && !r1.Pending);
            r1.Saved = DateTime.Now.Ticks; r1.Save(f4);
            var r1b = CertResume.Load(f4, out problem);
            Check("...a recent one is offered, and saving it again keeps everything", r1b != null && r1b.Pending && r1b.Answers["q001"] == 1 && r1b.Flags.Contains("q003") && r1b.SecondsLeft == 3000, problem);
            r1b.Used = true; r1b.Save(f4);
            Check("...once used it is not offered again (the file stays)", CertResume.Load(f4, out problem).Pending == false && File.Exists(f4));
            string d5 = Path.Combine(root, "damaged"); Directory.CreateDirectory(d5);
            string f5 = CertResume.FileIn(d5);
            File.WriteAllText(f5, "{\"Kind\":\"exam\",\"Answ");
            var bad = CertResume.Load(f5, out problem);
            Check("a damaged resume.json isn't offered", bad == null && problem.Length > 0, problem);
            Check("...it is copied aside and left as it was", File.ReadAllText(f5) == "{\"Kind\":\"exam\",\"Answ" && Directory.GetFiles(d5, "resume.json.unreadable-*").Length == 1);

            // ---- Cyber Range: the version 1 file, a damaged one
            string d7 = Path.Combine(root, "range-fixture"); Directory.CreateDirectory(d7);
            string f7 = RangeResume.FileIn(d7);
            Directory.CreateDirectory(Path.GetDirectoryName(f7));
            File.WriteAllText(f7, ResumeFixture("range-resume-v1.json", RangeResumeV1), new UTF8Encoding(false));
            var rr = RangeResume.Load(f7, out problem);
            Check("range\\resume.json version 1 opens", rr != null && problem == "" && rr.Puzzle && rr.Id == "b01-base64" && rr.Seed == 4 && rr.HintsUsed[0] == 0 && rr.Seconds == 95 && rr.Seen[0].Ports[0] == 22 && rr.Loot[0].Auto && rr.Notes == "my notes from before", problem);
            Check("...an old one is not offered", rr != null && !rr.Pending);
            rr.Saved = DateTime.Now.Ticks; rr.Save(f7);
            var rr2 = RangeResume.Load(f7, out problem);
            Check("...a recent one is offered and keeps its details", rr2 != null && rr2.Pending && rr2.Seed == 4 && rr2.Notes == "my notes from before" && rr2.Loot[0].Secret == "pw", problem);
            var puzzles = new List<RangePuzzle>(); var scen = new List<RangeScenario>(); var probs = new List<string>();
            RangeData.Load(Path.Combine(AppRootForTests, "Range"), scen, puzzles, probs);
            var pz = puzzles.FirstOrDefault(p => p.Id == "b01-base64");
            Check("the fixture's puzzle exists", pz != null);
            if (pz != null)
            {
                var a = RangeRun.ForPuzzle(pz, 4); var b = RangeRun.ForPuzzle(pz, 4);
                Check("...and its seed gives the same flag again (what carrying on relies on)", a.Flags[""] == b.Flags[""]);
                a.Dispose(); b.Dispose();
            }
            string d8 = Path.Combine(root, "range-damaged"); Directory.CreateDirectory(Path.Combine(d8, "range"));
            string f8 = RangeResume.FileIn(d8);
            File.WriteAllText(f8, "{\"Id\":");
            var rbad = RangeResume.Load(f8, out problem);
            Check("a damaged range\\resume.json isn't offered, is copied aside and left as it was", rbad == null && File.ReadAllText(f8) == "{\"Id\":" && Directory.GetFiles(Path.GetDirectoryName(f8), "resume.json.unreadable-*").Length == 1);
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }
}
