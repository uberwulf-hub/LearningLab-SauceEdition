// Cert Lab and Cyber Range (Learning Lab) - the content that ships inside the app: the courses (Courses\<course>\:
// course.json, lessons, cards, cheat sheets, labs, exams, manual and help pages, PBQ sets) and the range's challenges
// (Range\: scenarios, puzzles, glossary, word lists). They are embedded resources of this assembly and are unpacked
// into a folder (Data.Cache("CertLab")\content) the first time they are needed, so the engine reads them as files
// exactly as App Hub did (CourseLoader.LoadAll(root\Courses), Man.Dir, PsHelp.Dir, RangeData.Load(root\Range)), on
// Windows and Android alike. A new version of the app (another build) unpacks them again from scratch, so a file a
// later version removed never lingers. The folder is a cache: nothing the person made is kept there.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CertLab
{
    public static class CertLabContent
    {
        const string Prefix = "CertLab/";
        const string StampFile = ".unpacked";
        static readonly object gate = new object();
        static string root;

        /// <summary>The shipped files by their path ("Courses/rhcsa/course.json", "Range/glossary.txt"), with the
        /// resource that holds each.</summary>
        public static SortedDictionary<string, string> Files
        {
            get
            {
                var l = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (string r in typeof(CertLabContent).Assembly.GetManifestResourceNames())
                {
                    string n = r.Replace('\\', '/');
                    if (n.StartsWith(Prefix + "Courses/", StringComparison.Ordinal) || n.StartsWith(Prefix + "Range/", StringComparison.Ordinal)) l[n.Substring(Prefix.Length)] = r;
                }
                return l;
            }
        }

        /// <summary>The folder App Hub called Cert Lab's app root: it holds Courses\ and Range\. Unpacked on first use.</summary>
        public static string Root
        {
            get
            {
                lock (gate)
                {
                    if (root == null) root = Unpack(Path.Combine(LearningLab.Data.Cache("CertLab"), "content"));
                    return root;
                }
            }
        }

        public static string CoursesDir { get { return Path.Combine(Root, "Courses"); } }
        public static string RangeDir { get { return Path.Combine(Root, "Range"); } }

        /// <summary>Writes the shipped files into dir (Courses\..., Range\...) unless this very build already did, and
        /// returns dir. Another build first empties the folder, so it holds exactly what this one ships.</summary>
        public static string Unpack(string dir)
        {
            var asm = typeof(CertLabContent).Assembly;
            string stamp = asm.ManifestModule.ModuleVersionId.ToString("N");
            string stampPath = Path.Combine(dir, StampFile);
            try { if (File.Exists(stampPath) && File.ReadAllText(stampPath).Trim() == stamp) return dir; } catch (IOException) { }
            foreach (string sub in new[] { "Courses", "Range" })
            {
                string d = Path.Combine(dir, sub);
                if (Directory.Exists(d)) Directory.Delete(d, true);
            }
            Directory.CreateDirectory(dir);
            foreach (var kv in Files)
            {
                string path = Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var s = asm.GetManifestResourceStream(kv.Value))
                using (var f = File.Create(path)) s.CopyTo(f);
            }
            File.WriteAllText(stampPath, stamp);
            return dir;
        }
    }
}
