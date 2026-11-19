// Cert Lab in Learning Lab - checks of what is new here: the content unpacked from the engine's resources
// (CertLabContent) and App Hub's saved data brought across once (CertLabData.BringAcross), never over ours.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LearningLab.Saves;

namespace CertLab
{
    public static partial class Tests
    {
        static void UnpackTests()
        {
            var files = CertLabContent.Files;
            string root = CertLabContent.Root;
            Check("the courses and the range ship inside the engine", files.Count > 1000 && files.ContainsKey("Courses/rhcsa/course.json") && files.ContainsKey("Range/glossary.txt"), files.Count.ToString());
            Check("every shipped file is unpacked", files.Keys.All(k => File.Exists(Path.Combine(root, k.Replace('/', Path.DirectorySeparatorChar)))), files.Keys.FirstOrDefault(k => !File.Exists(Path.Combine(root, k.Replace('/', Path.DirectorySeparatorChar)))));
            Check("the five courses load from there", CourseLoader.LoadAll(CertLabContent.CoursesDir).Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "az800", "md102", "powershell", "rhcsa", "secplus" }));
            // a file left from another build goes; the same build doesn't unpack again
            string stray = Path.Combine(TmpRoot, "unpack");
            if (Directory.Exists(stray)) Directory.Delete(stray, true);
            CertLabContent.Unpack(stray);
            string extra = Path.Combine(stray, "Courses", "rhcsa", "lessons", "zz-removed-later.txt");
            File.WriteAllText(extra, "old");
            var before = File.GetLastWriteTimeUtc(Path.Combine(stray, "Range", "glossary.txt"));
            CertLabContent.Unpack(stray);
            Check("unpacking again in the same build leaves the folder as it is", File.Exists(extra) && File.GetLastWriteTimeUtc(Path.Combine(stray, "Range", "glossary.txt")) == before);
            File.WriteAllText(Path.Combine(stray, ".unpacked"), "another build");
            CertLabContent.Unpack(stray);
            Check("another build empties the folder first, so a removed file doesn't linger", !File.Exists(extra) && File.Exists(Path.Combine(stray, "Courses", "rhcsa", "course.json")));
            try { Directory.Delete(stray, true); } catch (Exception) { }
        }

        static void BringAcrossTests()
        {
            string root = Path.Combine(TmpRoot, "bring-across");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string hub = Path.Combine(root, "AppHub", "CertLab"), mine = Path.Combine(root, "LearningLab", "CertLab");
            Directory.CreateDirectory(Path.Combine(hub, "range")); Directory.CreateDirectory(mine);
            File.WriteAllText(Path.Combine(hub, "profiles.json"), "{\"Version\":1,\"Profiles\":[{\"Name\":\"Theo\"}]}");
            File.WriteAllText(SafeFile.BackupName(Path.Combine(hub, "profiles.json"), 1), "{\"Version\":1,\"Profiles\":[]}");
            File.WriteAllText(Path.Combine(hub, "range", "progress.json"), "{\"Version\":2}");
            File.WriteAllText(Path.Combine(hub, "machines-Theo.json"), "{}");
            File.WriteAllText(Path.Combine(hub, "speech.json"), "{\"Rate\":3}");
            File.WriteAllText(Path.Combine(hub, "something-else.txt"), "not ours to take");
            File.WriteAllText(Path.Combine(mine, "speech.json"), "{\"Rate\":-2}");   // already ours
            int n = CertLabData.BringAcross(hub, mine);
            Check("App Hub's profiles, their backups, the range's progress and saved machines come across", n == 4 && File.ReadAllText(Path.Combine(mine, "profiles.json")).Contains("Theo") && File.Exists(SafeFile.BackupName(Path.Combine(mine, "profiles.json"), 1)) && File.Exists(Path.Combine(mine, "range", "progress.json")) && File.Exists(Path.Combine(mine, "machines-Theo.json")), n.ToString());
            Check("...never over a file of ours, and nothing else", File.ReadAllText(Path.Combine(mine, "speech.json")) == "{\"Rate\":-2}" && !File.Exists(Path.Combine(mine, "something-else.txt")));
            Check("...App Hub's folder stays as it was", File.Exists(Path.Combine(hub, "profiles.json")) && File.Exists(Path.Combine(hub, "range", "progress.json")));
            var store = ProfileStore.Load(Path.Combine(mine, "profiles.json"), true);
            Check("...and the profiles open", store.LoadProblem == "" && store.Get("Theo") != null, store.LoadProblem);
            File.Delete(Path.Combine(mine, "profiles.json"));
            Check("only once: a file deleted afterwards doesn't come back", CertLabData.BringAcross(hub, mine) == 0 && !File.Exists(Path.Combine(mine, "profiles.json")));
            Check("in the checks there is no App Hub folder to read", CertLabData.BringAcrossFromAppHub() == 0);
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }
}
