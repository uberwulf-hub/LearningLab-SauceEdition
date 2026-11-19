// Cert Lab - the AZ-800 lessons' terminal examples: each block is typed into a fresh network (on DC1 unless the block
// says "#machine NAME"; "#setup admin:LINE", "#setup MACHINE:POWERSHELL" and "#setup wait:SECONDS" prepare it) and must
// show exactly what the simulator prints. WsFillExamples writes that output into a lesson file while authoring.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static string WsTypeExample(string block)
        {
            var lines = block.Replace("\r", "").Split('\n');
            var m = lines.FirstOrDefault(x => x.StartsWith("#machine "));
            string machine = m == null ? "DC1" : m.Substring(9).Trim();
            var sim = WsSim.Create(1);
            try
            {
                foreach (var su in lines.Where(x => x.StartsWith("#setup ")).Select(x => x.Substring(7)))
                {
                    int colon = su.IndexOf(':');
                    string where = su.Substring(0, colon), line = su.Substring(colon + 1);
                    if (where == "wait") { sim.Advance(long.Parse(line)); continue; }
                    if (where == WsSim.Admin) { sim.Type(WsSim.Admin, line); continue; }
                    sim.RunSetup(new List<SolutionStep> { new SolutionStep { Machine = where, Line = line } });
                }
                var typed = new List<string>();
                foreach (var cmd in ExampleCommands(block)) typed.AddRange(cmd.Value.Split('\n'));
                string got = sim.Transcript(machine, typed, 120).Replace("\r", "");
                return Regex.Replace(got, "\\n*PS [^>\\n]*> $", "").TrimEnd('\n', ' ');
            }
            finally { sim.Dispose(); }
        }

        static void WsExampleTests(CourseInfo c)
        {
            foreach (var l in c.AllLessons)
            {
                int n = 0;
                foreach (var b in l.Blocks.Where(x => x.Kind == "terminal"))
                {
                    n++;
                    if (ExampleCommands(b.Text).Count == 0) continue;
                    string g = string.Join("\n", WsTypeExample(b.Text).Split('\n').Select(x => x.TrimEnd()).ToArray());
                    string w = string.Join("\n", b.Text.Replace("\r", "").Split('\n').Where(x => !x.StartsWith("#setup ") && !x.StartsWith("#machine ")).Select(x => x.TrimEnd()).ToArray()).TrimEnd('\n', ' ');
                    Check(l.Id + " example " + n + " prints what the simulator prints", g == w, Diff(w, g));
                }
            }
        }

        /// <summary>Authoring help: fills each terminal example of an AZ-800 lesson file with what the simulator prints.</summary>
        public static string WsFillExamples(string text)
        {
            var lines = text.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append('\n');
                if (lines[i].Trim() != "```terminal") continue;
                var block = new List<string>();
                int j = i + 1;
                while (j < lines.Length && !lines[j].StartsWith("```")) block.Add(lines[j++]);
                string body = string.Join("\n", block.ToArray());
                if (ExampleCommands(body).Count > 0)
                {
                    foreach (var d in block.Where(x => x.StartsWith("#setup ") || x.StartsWith("#machine "))) sb.Append(d).Append('\n');
                    foreach (var gl in WsTypeExample(body).Split('\n')) sb.Append(gl.TrimEnd()).Append('\n');
                }
                else foreach (var b in block) sb.Append(b).Append('\n');
                i = j - 1;
            }
            var res = sb.ToString();
            if (!text.EndsWith("\n") && res.EndsWith("\n")) res = res.Substring(0, res.Length - 1);
            return res;
        }
    }
}
