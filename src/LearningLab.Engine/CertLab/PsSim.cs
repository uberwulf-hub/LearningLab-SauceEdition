// Cert Lab - the PowerShell course's simulator behind ISim: the pretend Windows network with a PowerShell console
// per machine. Setup scripts and checks are PowerShell run as the administrator; a check passes when it ends
// without an error and its last output is true. Checks see the console's session (its variables and functions),
// and $CertLabTranscript / $CertLabHistory (what the console showed and the commands typed).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class PsSim : ISim
    {
        public PsNet Net;
        readonly Dictionary<string, PsConsole> consoles = new Dictionary<string, PsConsole>(StringComparer.OrdinalIgnoreCase);
        public Action<string> SetupLog;
        public string Kind { get { return "powershell"; } }

        public static PsSim Create(int machines, int seed) { return new PsSim { Net = PsNet.Create(Math.Max(1, machines), seed) }; }

        public List<string> MachineNames { get { return Net.Machines.Select(m => m.Name).ToList(); } }
        public PsMachine M(string name) { return Net.Get(name); }
        public PsConsole Console(string machine)
        {
            PsConsole c;
            if (consoles.TryGetValue(machine, out c)) return c;
            var m = Net.Get(machine);
            if (m == null) return null;
            c = new PsConsole(m);
            consoles[m.Name] = c;
            return c;
        }
        public ITerm Term(string machine) { return Console(machine); }
        public bool CanSave { get { return false; } }

        public void StartConsoles(bool autoLogin, IEnumerable<string> noLogin) { foreach (var m in Net.Machines) Console(m.Name).Start(); }

        public void RunSetup(List<SolutionStep> steps)
        {
            int i = 0;
            while (i < steps.Count)
            {
                string mach = steps[i].Machine;
                var sb = new StringBuilder();
                while (i < steps.Count && steps[i].Machine == mach) { sb.Append(steps[i].Line).Append('\n'); i++; }
                var m = Net.Get(mach);
                if (m == null) continue;
                var host = new NullHost();
                var r = new PsRunspace(m, host, "Administrator");
                var errs = new List<string>();
                var st = new Streams { Err = e => errs.Add(PsRunspace.ErrorText(e)) };
                r.RunScript(sb.ToString(), "", st);
                m.Sessions.Remove(r);
                if (SetupLog != null) SetupLog(mach + ": " + (errs.Count == 0 ? "ok" : string.Join("", errs.ToArray())) + host.Text);
            }
        }

        public bool Check(string machine, string script, out string output)
        {
            output = "";
            var m = Net.Get(machine);
            if (m == null) { output = "no machine " + machine; return false; }
            PsConsole c; consoles.TryGetValue(m.Name, out c);
            PsRunspace r; bool temp = false;
            if (c != null && !c.Local.Closed) r = c.Local;
            else { r = new PsRunspace(m, new NullHost(), "Administrator"); temp = true; }
            var sc = new PsScope(r, r.Global, "local");
            string tr = c == null ? "" : c.Local.Transcript.ToString();
            sc.Vars["CertLabTranscript"] = new PsVariable { Name = "CertLabTranscript", Value = tr };
            sc.Vars["CertLabHistory"] = new PsVariable { Name = "CertLabHistory", Value = c == null ? new object[0] : c.Local.History.Select(h => (object)h.Key).ToArray() };
            var outp = new List<object>();
            var errs = new StringBuilder();
            var st = new Streams { Out = o => outp.Add(o), Err = e => errs.Append(PsRunspace.ErrorText(e)) };
            bool failed = false;
            var oldHost = r.Host; r.Host = new NullHost();
            int errCount = r.Errors.Count;
            var oldOk = r.LastOk;
            try
            {
                var ast = PsParser.Parse(script, "");
                r.RunChecked(ast, sc, st);
            }
            catch (PsParseError e) { errs.Append("parse error: " + e.Message); failed = true; }
            catch (PsRuntimeException e) { errs.Append(PsRunspace.ErrorText(e.Rec)); failed = true; }
            finally
            {
                r.Host = oldHost;
                while (r.Errors.Count > errCount) r.Errors.RemoveAt(0);
                r.LastOk = oldOk;
                if (temp) m.Sessions.Remove(r);
            }
            output = string.Join("\n", outp.Select(o => PsConv.ToStr(o)).ToArray()) + (errs.Length > 0 ? "\n" + errs : "");
            if (failed || outp.Count == 0) return false;
            return PsConv.ToBool(outp[outp.Count - 1]);
        }

        public void RebootAll() { foreach (var m in Net.Machines) m.Reboot(); }
        public void Reboot(string machine) { var m = M(machine); if (m != null) m.Reboot(); }
        public void Power(string machine) { Reboot(machine); }
        public void Advance(long seconds) { Net.Advance(seconds); }
        public string Save() { return ""; }
        public string Clock { get { return Net.Now.ToString("ddd MMM d HH:mm", PsConv.Us); } }
        public string Status(string machine)
        {
            var m = M(machine);
            if (m == null) return "";
            return m.Name + " - " + (m.IsServer ? "Windows Server 2022" : "Windows 11") + ", running";
        }
        public bool Idle { get { return consoles.Values.All(c => !c.Busy); } }
        public void Dispose() { foreach (var c in consoles.Values) c.Dispose(); consoles.Clear(); }

        /// <summary>What a console shows for typed lines, prompts included (tests, lesson examples, the dev runner).</summary>
        public string Transcript(string machine, IEnumerable<string> lines, int width)
        {
            var c = Console(machine);
            c.Width = width;
            if (!c.Waiting) c.Start();
            var sb = new StringBuilder();
            c.TakeOutput();
            foreach (var l in lines)
            {
                string p = c.Prompt;
                c.Execute(l);
                sb.Append(p).Append(c.Secret ? "" : l).Append('\n').Append(c.TakeOutput());
            }
            sb.Append(c.Prompt);
            return sb.ToString();
        }
        public static string Demo(string script, int width)
        {
            var s = Create(2, 1);
            try { return s.Transcript("WIN-SRV1", script.Replace("\r", "").Split('\n'), width); }
            finally { s.Dispose(); }
        }

        /// <summary>Types a line into a machine's console (solutions). "#wait N" lets N seconds pass.</summary>
        public void Type(string machine, string line)
        {
            var c = Console(machine);
            if (c == null) return;
            if (!c.Waiting) c.Start();
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; if (long.TryParse(line.Substring(6).Trim(), out n)) Net.Advance(n); return; }
            c.Execute(line);
        }
    }

    public partial class PsRunspace
    {
        public PsConsole Console;

        /// <summary>Runs a check in a scope of its own (it sees the session but its variables do not stay).</summary>
        public void RunChecked(ScriptBlockAst ast, PsScope sc, Streams st)
        {
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = PsConv.Us;
            CurrentStreams = st;
            Steps = 0;
            TryDepth++;
            try { ExecBlock(ast.EndBlock ?? ast.Body, sc, st); }
            catch (PsExitEx) { }
            catch (PsFlowEx) { }
            finally { TryDepth = 0; CallDepth = 0; System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; }
        }

        /// <summary>The profiles a new console runs (if they exist and the execution policy allows scripts).</summary>
        public void RunProfiles()
        {
            var prof = PsConv.ToStr(GetVar("PROFILE", Global));
            var notes = PsNotes.Get(prof);
            var files = new[] { "AllUsersAllHosts", "AllUsersCurrentHost", "CurrentUserAllHosts", "CurrentUserCurrentHost" }.Select(n => notes == null ? null : PsConv.ToStr(notes.Get(n))).Where(x => x != null).ToList();
            foreach (var f in files)
            {
                var node = FsNode(f);
                if (node == null || node.IsDir) continue;
                string why = ScriptBlockedReason(f);
                if (why != null)
                {
                    var r = MakeError(null, why, "UnauthorizedAccess", "SecurityError", "System.Management.Automation.PSSecurityException", null);
                    r.ActivityOverride = ""; r.Line = ". '" + f + "'"; r.Length = r.Line.Length;
                    Show(ErrorText(r));
                    continue;
                }
                ExecuteInteractive(". '" + f + "'");
                History.RemoveAt(History.Count - 1); HistoryId--;
            }
        }
    }
}
