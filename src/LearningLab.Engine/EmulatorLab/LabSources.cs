// Emulator Lab (Learning Lab) - the assembly sources that ship inside the app: LabOS and its demos (os\), the lessons
// (os\examples\) and the processor conformance programs (tests\). They are embedded resources of this assembly and
// are unpacked into a folder (Data.Cache("EmulatorLab")) before LabOS is assembled, so Computer reads them as files
// exactly as App Hub did, on Windows and Android alike. Every byte the machine runs is ours: nothing is downloaded.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace EmulatorLab
{
    public static class LabSources
    {
        const string Prefix = "EmulatorLab/";

        /// <summary>Every shipped source by its path ("os/kernel.s", "os/examples/count.s", "tests/alu.s"), in order.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                var l = new List<string>();
                foreach (string r in typeof(LabSources).Assembly.GetManifestResourceNames())
                {
                    string n = r.Replace('\\', '/');
                    if (n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".s", StringComparison.OrdinalIgnoreCase)) l.Add(n.Substring(Prefix.Length));
                }
                l.Sort(StringComparer.OrdinalIgnoreCase);
                return l;
            }
        }

        /// <summary>The text of one shipped source ("os/examples/count.s"), or null.</summary>
        public static string Read(string name)
        {
            var asm = typeof(LabSources).Assembly;
            foreach (string r in asm.GetManifestResourceNames())
            {
                if (!string.Equals(r.Replace('\\', '/'), Prefix + name, StringComparison.OrdinalIgnoreCase)) continue;
                using (var s = asm.GetManifestResourceStream(r))
                using (var rd = new StreamReader(s, Encoding.UTF8)) return rd.ReadToEnd();
            }
            return null;
        }

        /// <summary>Writes the sources into dir (os\..., tests\...) where they differ from what is there, and returns
        /// dir: the folder App Hub called its app root (Computer takes dir\os).</summary>
        public static string Unpack(string dir)
        {
            foreach (string n in Names)
            {
                string path = Path.Combine(dir, n.Replace('/', Path.DirectorySeparatorChar));
                string text = Read(n);
                if (File.Exists(path) && File.ReadAllText(path) == text) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text);
                File.Move(tmp, path, true);
            }
            return dir;
        }
    }
}
