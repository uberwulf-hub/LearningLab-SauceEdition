// Cert Lab and Cyber Range (Learning Lab) - where the person's data lives: Data.For("CertLab"), laid out as App Hub's
// %APPDATA%\CertLab was, so the same code reads it:
//   profiles.json              people, their courses' progress, flash card boxes, exam results (Profiles.cs)
//   machines-<name>.json       the practice machines someone saved (RhelSim.Save)
//   speech.json                reading aloud: speed, voice, code (ReadText.cs: SpeechSettings)
//   resume.json                where Cert Lab was when it closed for an update (Resume.cs: CertResume)
//   range\progress.json        Cyber Range's progress, badges, days played (RangeStore.cs)
//   range\resume.json          a Cyber Range run in progress (Resume.cs: RangeResume)
// Everything is written through LearningLab.Saves.SafeFile, with the same backups (.bak1..bak5) as App Hub.
// On Windows, App Hub's %APPDATA%\CertLab is brought across once (BringAcrossFromAppHub): each file and its backups
// are copied only when Learning Lab has none of that file yet, so nothing of ours is ever written over, and App Hub's
// folder is left as it is (the saved-data rule).
using System;
using System.Collections.Generic;
using System.IO;
using LearningLab;
using LearningLab.Saves;

namespace CertLab
{
    public static class CertLabData
    {
        /// <summary>Learning Lab's Cert Lab folder (made when first asked for).</summary>
        public static string Dir { get { return Data.For("CertLab"); } }
        public static string ProfilesFile { get { return Path.Combine(Dir, "profiles.json"); } }
        public static string RangeProgressFile { get { return Path.Combine(Dir, "range", "progress.json"); } }
        public static string SpeechFile { get { return Path.Combine(Dir, "speech.json"); } }

        /// <summary>The practice machines a person saved (Save in the practice page): machines-<name>.json.</summary>
        public static string MachinesFile(string dataDir, string person)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in person ?? "anyone") sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return Path.Combine(dataDir, "machines-" + (person == null ? "anyone" : sb.ToString()) + ".json");
        }

        /// <summary>Opens saved practice machines safely (SafeFile: a file that can't be read is copied aside and the
        /// newest backup that reads is used; SaveUnreadableException when none does). Was App Hub's SimPage.OpenMachines.</summary>
        public static RhelSim OpenMachines(string file, out SaveLoad info)
        {
            return SafeFile.Load(file, b =>
            {
                string text = new System.Text.UTF8Encoding(false).GetString(b);
                if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
                if (!(Json.Parse(text) is JObj)) throw new InvalidDataException("Not saved machines");
                var s = RhelSim.Load(text);
                if (s.MachineNames.Count == 0) { s.Dispose(); throw new InvalidDataException("No machines in the file"); }
                return s;
            }, out info);
        }

        /// <summary>Written once App Hub's data was looked at, so it is brought across only once (even if the person
        /// later deletes what came across).</summary>
        public const string BroughtMark = "apphub-brought-across.txt";

        /// <summary>On Windows, the first time: App Hub's Cert Lab data into Learning Lab's (never over ours).</summary>
        public static int BringAcrossFromAppHub()
        {
            string old = Data.AppHubFolder("CertLab");
            if (old == null) return 0;
            return BringAcross(old, Dir);
        }

        /// <summary>Copies App Hub's saves (and their backups) from one folder into another, once; returns how many
        /// files were copied. For the checks too.</summary>
        public static int BringAcross(string from, string dataDir)
        {
            string mark = Path.Combine(dataDir, BroughtMark);
            if (File.Exists(mark) || !Directory.Exists(from)) return 0;
            int copied = 0;
            var names = new List<string> { "profiles.json", "speech.json", "resume.json", Path.Combine("range", "progress.json"), Path.Combine("range", "resume.json") };
            try
            {
                foreach (string f in Directory.GetFiles(from, "machines-*.json"))
                    names.Add(Path.GetFileName(f));
            }
            catch (Exception) { }
            foreach (string name in names)
            {
                string mine = Path.Combine(dataDir, name);
                if (File.Exists(mine) || SafeFile.Backups(mine).Count > 0) continue;
                string theirs = Path.Combine(from, name);
                var copies = new List<string>();
                if (File.Exists(theirs)) copies.Add(theirs);
                copies.AddRange(SafeFile.Backups(theirs));
                foreach (string f in copies)
                {
                    try
                    {
                        string target = Path.Combine(Path.GetDirectoryName(mine), Path.GetFileName(f));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) continue;
                        File.Copy(f, target, false);
                        File.SetLastWriteTime(target, File.GetLastWriteTime(f));
                        copied++;
                    }
                    catch (Exception) { }
                }
            }
            try { Directory.CreateDirectory(dataDir); File.WriteAllText(mark, "App Hub's Cert Lab data (" + from + ") was looked at on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "; " + copied + " files copied.\n"); }
            catch (Exception) { }
            return copied;
        }
    }
}
