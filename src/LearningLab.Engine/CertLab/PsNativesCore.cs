// Cert Lab - a few programs the PowerShell console can run on the pretend machines: hostname, whoami, notepad
// (Cert Lab's editor inside the console), powershell.exe (a new PowerShell process: -File, -Command, -NoProfile,
// -ExecutionPolicy) and cmd.exe /c with a handful of its built-in commands.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static partial void RegisterCoreNatives()
        {
            Add("hostname", null, c => { c.L(c.M.Name); return 0; });
            Add("whoami", null, c =>
            {
                string who = (c.M.DomainShort + "\\" + c.R.User).ToLowerInvariant();
                if (c.Args.Any(a => a.Equals("/user", StringComparison.OrdinalIgnoreCase)))
                {
                    c.L("\nUSER INFORMATION\n----------------\n\nUser Name" + new string(' ', Math.Max(1, who.Length - 8)) + "SID\n" + new string('=', who.Length) + " " + new string('=', 45) + "\n" + who + " S-1-5-21-3623811015-3361044348-30300820-500");
                    return 0;
                }
                if (c.Args.Any(a => a.Equals("/upn", StringComparison.OrdinalIgnoreCase))) { c.L(c.R.User.ToLowerInvariant() + "@" + c.M.Domain); return 0; }
                c.L(who);
                return 0;
            });
            Add("notepad", null, c =>
            {
                var con = c.R.Console;
                string path = c.Args.Count > 0 ? c.R.FullFsPath(c.Args[0], c.R.CurrentFsDir) : null;
                if (con == null) { c.Err("notepad opens a window; Cert Lab can show its editor only at the console."); return 1; }
                string text = path == null ? "" : (c.R.ReadFileText(path) ?? "");
                var res = con.EditFile(path ?? "Untitled.txt", text);
                if (res != null && path != null)
                {
                    var f = c.R.FsNode(path);
                    var why = c.R.WriteFile(path, res.Replace("\r\n", "\n").Replace("\n", "\r\n"), f != null ? f.Encoding : "utf8", false);
                    if (why != null) c.Err(why);
                }
                return 0;
            });
            Add("powershell", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", RunPowerShell);
            Add("cmd", null, RunCmd);
        }

        /// <summary>powershell.exe: a new PowerShell process on the same machine; its output comes back as text.</summary>
        public static int RunPowerShell(NativeCall c)
        {
            string file = null, command = null, policy = null; bool noProfile = false;
            var rest = new List<string>();
            for (int i = 0; i < c.Args.Count; i++)
            {
                string a = c.Args[i], la = a.ToLowerInvariant().TrimStart('/');
                if (file != null || command != null) { rest.Add(a); continue; }
                if (la == "-file" || la == "-f") { if (i + 1 < c.Args.Count) file = c.Args[++i]; continue; }
                if (la == "-command" || la == "-c") { command = string.Join(" ", c.Args.Skip(i + 1).ToArray()); break; }
                if (la == "-executionpolicy" || la == "-ep" || la == "-exec") { if (i + 1 < c.Args.Count) policy = c.Args[++i]; continue; }
                if (la == "-noprofile" || la == "-nop") { noProfile = true; continue; }
                if (la == "-noninteractive" || la == "-noni" || la == "-nologo" || la == "-windowstyle" || la == "-w") { if ((la == "-windowstyle" || la == "-w") && i + 1 < c.Args.Count) i++; continue; }
                if (la == "-version" || la == "-v") { i++; continue; }
                if (la.StartsWith("-", StringComparison.Ordinal)) { c.Err("Cert Lab's powershell.exe understands -File, -Command, -NoProfile, -ExecutionPolicy, -NonInteractive and -NoLogo."); return 1; }
                command = string.Join(" ", c.Args.Skip(i).ToArray()); break;
            }
            if (file == null && command == null)
            {
                c.Err("Cert Lab: a nested interactive PowerShell is not simulated. Use powershell.exe -Command \"...\" or -File script.ps1.");
                return 1;
            }
            var r = new PsRunspace(c.M, new NullHost(), c.R.User);
            r.SetLocation(c.R.CurrentFsDir);
            if (policy != null)
            {
                var e = PsEnums.Get("Microsoft.PowerShell.ExecutionPolicy").Parse(policy);
                if (e == null) { c.Err("Invalid ExecutionPolicy value: " + policy); return 1; }
                r.ProcessPolicy = e.Name;
            }
            var lines = new List<object>();
            var errs = new List<string>();
            var host = (NullHost)r.Host;
            var st = new Streams { Err = er => errs.Add(PsRunspace.ErrorText(er)), Warn = w => lines.Add("WARNING: " + w), Info = o => lines.Add(o is PsHostText ? ((PsHostText)o).Text : PsConv.ToStr(o)) };
            List<object> outp;
            if (file != null)
            {
                string q = "& '" + c.R.FullFsPath(file, c.R.CurrentFsDir).Replace("'", "''") + "'" + string.Concat(rest.Select(a => " '" + a.Replace("'", "''") + "'").ToArray());
                outp = r.RunScript(q, "", st);
            }
            else outp = r.RunScript(command, "", st);
            if (!noProfile) { }
            foreach (var x in PsFormat.ToLines(c.R, outp, c.R.Width)) lines.Add(x);
            c.M.Sessions.Remove(r);
            foreach (var l in lines) c.Out.Add(PsConv.ToStr(l));
            foreach (var e in errs) c.Err(e.TrimEnd('\n'));
            if (r.LastExit != 0) return r.LastExit;
            return errs.Count > 0 || r.Terminated ? 1 : 0;
        }

        static int RunCmd(NativeCall c)
        {
            int k = c.Args.FindIndex(a => a.Equals("/c", StringComparison.OrdinalIgnoreCase) || a.Equals("/k", StringComparison.OrdinalIgnoreCase));
            if (k < 0) { c.Err("Cert Lab: an interactive cmd.exe is not simulated. Use cmd /c COMMAND."); return 1; }
            string line = string.Join(" ", c.Args.Skip(k + 1).ToArray()).Trim().Trim('"');
            var w = line.Split(new[] { ' ' }, 2);
            string cmd = w[0].ToLowerInvariant(), arg = w.Length > 1 ? w[1] : "";
            switch (cmd)
            {
                case "echo": c.L(c.R.ExpandEnv(arg)); return 0;
                case "ver": c.L("\nMicrosoft Windows [Version " + c.M.OsVersion + "." + (c.M.IsServer ? "2849" : "4249") + "]"); return 0;
                case "hostname": c.L(c.M.Name); return 0;
                case "set": foreach (var kv in c.R.Env.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) if (arg.Length == 0 || kv.Key.StartsWith(arg, StringComparison.OrdinalIgnoreCase)) c.L(kv.Key + "=" + kv.Value); return 0;
                case "whoami": c.L((c.M.DomainShort + "\\" + c.R.User).ToLowerInvariant()); return 0;
                case "type":
                    {
                        var t = c.R.ReadFileText(c.R.FullFsPath(arg, c.R.CurrentFsDir));
                        if (t == null) { c.Err("The system cannot find the file specified."); return 1; }
                        foreach (var l in PsRunspace.SplitLines(t)) c.Out.Add(l);
                        return 0;
                    }
            }
            c.Err("Cert Lab simulates only a few cmd.exe commands (echo, ver, hostname, set, whoami, type). Use PowerShell instead.");
            return 1;
        }
    }
}
