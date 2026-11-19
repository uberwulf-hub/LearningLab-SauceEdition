// Cert Lab - the checks behind App Hub's CertLab.ps1 -Test (groups certlab / certlab-full of Learning Lab's checks): the shell, the commands' output, permissions, SELinux, storage,
// boot, services, networking, scheduling, containers, the course content, every lab and exam (fail at the start,
// pass with the model solution typed into the consoles), saving and loading, and the flash card scheduler.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static List<string> log;
        static int pass, fail;
        static string group = "";

        /// <summary>Every check goes to Learning Lab's list (Program.Check), named "certlab <group>: <check>".</summary>
        static void Check(string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            LearningLab.Tests.Program.Check("certlab " + group + ": " + name, ok, detail);
        }
        static void Check(string name, bool ok) { Check(name, ok, null); }

        /// <summary>A terminal on a fresh network, logged in as root (or a user).</summary>
        public class T
        {
            public RhelSim Sim; public Session S; public string Last = "";
            public T(int machines) : this(machines, "root") { }
            public T(int machines, string user)
            {
                Sim = RhelSim.Create(machines, 7);
                S = Sim.Console("server1");
                S.AutoLogin = user;
                S.EditorHook = (s, p, text, ro) => EditResult == null ? null : EditResult(text ?? "");
                S.Start();
                S.TakeOutput();
            }
            public Func<string, string> EditResult;
            /// <summary>Runs a line on the console and returns what it printed (the echo of the line is not included).</summary>
            public string Run(string line)
            {
                S.Execute(line);
                Last = S.TakeOutput();
                return Last;
            }
            public string On(string machine, string line)
            {
                var s = Sim.Console(machine);
                if (!s.Waiting) { s.AutoLogin = "root"; s.Start(); s.TakeOutput(); }
                s.Execute(line);
                Last = s.TakeOutput();
                return Last;
            }
            public Machine M { get { return Sim.M("server1"); } }
            public string Prompt { get { return S.Prompt; } }
            public bool Ok(string script) { string o; return Sim.Check("server1", script, out o); }
            public void Dispose() { Sim.Dispose(); }
        }

        /// <summary>The scratch folder of the checks (under Learning Lab's test folder, never the person's data).</summary>
        public static string TmpRoot { get { return Path.Combine(Path.GetDirectoryName(LearningLab.Data.Root), "certlab-tmp"); } }

        /// <summary>Runs the checks App Hub ran with CertLab.ps1 -Test against the content shipped in the engine
        /// (CertLabContent: unpacked into the test cache). quick = App Hub's -Quick (one seed per challenge, fewer exam
        /// seeds): group "certlab"; full: group "certlab-full". only: a group name prefix, optionally :file (labs:lab-05).</summary>
        public static void Run(bool quick, string only)
        {
            log = new List<string>(); pass = 0; fail = 0;
            ClipText.UseMemory();   // checks never touch the real clipboard
            string appRoot = CertLabContent.Root;
            OnlyFile = null;
            if (!string.IsNullOrEmpty(only) && only.Contains(":")) { OnlyFile = only.Substring(only.IndexOf(':') + 1); only = only.Substring(0, only.IndexOf(':')); }
            Man.Dir = Path.Combine(appRoot, "Courses", "rhcsa", "man");
            var sw = Stopwatch.StartNew();
            var groups = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("shell", ShellTests),
            };
            AddGroups(groups, appRoot, quick);
            foreach (var g in groups)
            {
                if (!string.IsNullOrEmpty(only) && !g.Key.StartsWith(only)) continue;
                group = g.Key;
                var t0 = sw.ElapsedMilliseconds;
                try { g.Value(); }
                catch (Exception e) { Check("ran without an error", false, e.ToString()); }
                Console.WriteLine("   certlab " + g.Key + " (" + (sw.ElapsedMilliseconds - t0) + " ms)");
            }
            Console.WriteLine("   certlab: " + pass + " checks passed, " + fail + " failed in " + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s");
        }

        static void ShellTests()
        {
            var t = new T(1);
            Check("prompt", t.Prompt == "[root@server1 ~]# ", t.Prompt);
            Check("echo + arithmetic + braces", t.Run("echo $HOME $((1+2*3)) {a,b}c") == "/root 7 ac bc\n", t.Last);
            Check("pipes", t.Run("printf 'b\\na\\nc\\n' | sort | head -n 2") == "a\nb\n", t.Last);
            Check("for loop", t.Run("for i in 1 2 3; do echo -n $i; done; echo") == "123\n", t.Last);
            t.Dispose();
        }
    }
}
