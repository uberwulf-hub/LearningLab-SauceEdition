// Cert Lab - the AZ-800 course's simulator behind ISim: the Harborline network (WsLab) with a PowerShell console on each
// machine (DC1, DC2, FS1, HV1, CORE1, SRV1, CL1) through an inner PsSim, and machine "admin": the practice consoles
// (Active Directory Users and Computers, Group Policy Management, DNS Manager, Windows Admin Center, the Azure portal).
// Setup and solution lines for "admin" are console actions (WsOps), checks there are assertions on the lab (WsCheck);
// lines for a machine are PowerShell typed into its console, plus "#power off|on" and "#wait N".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsSim : ISim
    {
        public WsLab Lab;
        readonly PsSim ps;
        public const string Admin = "admin";
        public Action<string> SetupLog;
        public readonly List<string> Errors = new List<string>();
        public string LastResult = "";
        public string Kind { get { return "winserver"; } }

        WsSim(int seed) { Lab = WsLab.Create(seed); ps = new PsSim { Net = Lab.Net }; }
        public static WsSim Create(int seed) { return new WsSim(seed); }

        public List<string> MachineNames { get { return WsLab.MachineList.Select(r => r[0]).ToList(); } }
        public PsConsole Console(string machine) { var m = Lab.M(machine); return m == null ? null : ps.Console(m.Name); }
        public ITerm Term(string machine) { return Console(machine); }
        public bool CanSave { get { return false; } }
        public void StartConsoles(bool autoLogin, IEnumerable<string> noLogin) { }

        public void RunSetup(List<SolutionStep> steps)
        {
            int i = 0;
            while (i < steps.Count)
            {
                string mach = steps[i].Machine;
                if (WsText.Eq(mach, Admin) || steps[i].Line.StartsWith("#", StringComparison.Ordinal))
                {
                    var line = steps[i++].Line;
                    string r = Directive(mach, line);
                    if (r != null) Errors.Add("setup: " + mach + ": " + line + " -> " + r);
                    if (SetupLog != null) SetupLog(mach + ": " + line + " -> " + (r ?? "ok"));
                    continue;
                }
                var sb = new StringBuilder();
                while (i < steps.Count && steps[i].Machine == mach && !steps[i].Line.StartsWith("#", StringComparison.Ordinal)) { sb.Append(steps[i].Line).Append('\n'); i++; }
                var m = Lab.M(mach);
                if (m == null) { Errors.Add("setup: no machine " + mach); continue; }
                var host = new NullHost();
                var rs = new PsRunspace(m, host, "Administrator");
                var errs = new List<string>();
                var st = new Streams { Err = e => errs.Add(PsRunspace.ErrorText(e)) };
                rs.RunScript(sb.ToString(), "", st);
                m.Sessions.Remove(rs);
                if (errs.Count > 0) Errors.Add("setup: " + mach + ": " + string.Join("", errs.ToArray()));
                if (SetupLog != null) SetupLog(mach + ": " + (errs.Count == 0 ? "ok" : string.Join("", errs.ToArray())));
            }
        }

        /// <summary>An admin console action, "#wait N", or a directive for a machine; null when it worked.</summary>
        public string Directive(string machine, string line)
        {
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; if (long.TryParse(line.Substring(6).Trim(), out n)) Lab.Advance(n); return null; }
            if (WsText.Eq(machine, Admin)) { var r = WsOps.Run(Lab, line); LastResult = r.Text; return r.Ok ? null : r.Text; }
            var s = Lab.Server(machine);
            if (s == null) return "no machine " + machine;
            var tok = EpOps.Tokens(line.Substring(1));
            switch (tok.Count > 0 ? tok[0].ToLowerInvariant() : "")
            {
                case "power":
                    if (tok.Count > 1 && tok[1] == "off") { s.M.State = "off"; foreach (var r in s.M.Sessions.ToList()) r.Closed = true; Lab.Note(s.Name + " was switched off."); return null; }
                    if (s.M.State == "off") { s.M.Boot(false); s.AfterBoot(); Lab.Note(s.Name + " was switched on."); }
                    return null;
                case "reboot": s.M.Reboot(); return null;
                case "renew": return Lab.Renew(s.Name);
            }
            return "unknown directive " + line;
        }

        public bool Check(string machine, string script, out string output)
        {
            if (WsText.Eq(machine, Admin)) return WsCheck.Run(Lab, script, out output);
            var m = Lab.M(machine);
            if (m == null) { output = "no machine " + machine; return false; }
            if (m.State != "running") { output = machine + " is switched off"; return false; }
            return ps.Check(m.Name, script, out output);
        }

        /// <summary>The graders' restart: what is due replicates first, every running machine restarts (computers apply Group
        /// Policy at startup), then a minute passes so the timers that came due on the way run.</summary>
        public void RebootAll() { Lab.Advance(30); foreach (var m in Lab.Net.Machines.Where(x => x.State == "running").ToList()) m.Reboot(); Lab.Advance(60); }
        public void Reboot(string machine) { var m = Lab.M(machine); if (m != null && m.State == "running") { m.Reboot(); Lab.Advance(1); } }
        public void Power(string machine) { var s = Lab.Server(machine); if (s == null) return; Directive(machine, s.M.State == "off" ? "#power on" : "#power off"); }
        public void Advance(long seconds) { Lab.Advance(seconds); }
        public string Save() { return ""; }
        public string Clock { get { return Lab.Now.ToString("ddd MMM d HH:mm", CultureInfo.InvariantCulture); } }
        public string Status(string machine)
        {
            var s = Lab.Server(machine);
            if (s == null) return "";
            if (!s.Running) return s.Name + " - switched off";
            var roles = new List<string>();
            if (s.IsDc) roles.Add("domain controller (" + (Lab.SiteOfDc(s.Name) ?? "no site") + ")");
            if (s.Dns != null) roles.Add("DNS");
            if (s.Dhcp != null) roles.Add("DHCP");
            if (s.Hv != null) roles.Add("Hyper-V");
            if (s.Kind == "files") roles.Add("file server");
            if (s.Kind == "core") roles.Add("Server Core");
            if (s.IsClient) roles.Add("Windows 11, " + s.M.Ip);
            return s.Name + " - " + (roles.Count == 0 ? "member server" : string.Join(", ", roles.ToArray()));
        }
        public bool Idle { get { return ps.Idle; } }
        public void Dispose() { ps.Dispose(); }
        public string Transcript(string machine, IEnumerable<string> lines, int width) { var m = Lab.M(machine); return m == null ? "" : ps.Transcript(m.Name, lines, width); }

        public void Type(string machine, string line)
        {
            if (WsText.Eq(machine, Admin) || line.StartsWith("#", StringComparison.Ordinal) && !line.StartsWith("#<", StringComparison.Ordinal))
            {
                string r = Directive(machine, line);
                if (r != null) Errors.Add(machine + ": " + line + " -> " + r);
                return;
            }
            var c = Console(machine);
            if (c == null) { Errors.Add("no console on " + machine); return; }
            if (!c.Waiting) c.Start();
            c.Execute(line);
        }

        /// <summary>How a solution line reads for a person: the console action in words, or the PowerShell line.</summary>
        public static string SolutionText(string machine, string line)
        {
            if (WsText.Eq(machine, Admin)) return WsOps.Describe(line);
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; long.TryParse(line.Substring(6).Trim(), out n); return "(wait " + (n >= 3600 ? (n / 3600) + " hour" + (n >= 7200 ? "s" : "") : n >= 60 ? (n / 60) + " minutes" : n + " seconds") + ": use Skip time)"; }
            if (line.StartsWith("#power", StringComparison.Ordinal)) return "(switch the machine " + (line.Contains("off") ? "off" : "on") + ")";
            if (line.StartsWith("#", StringComparison.Ordinal)) return null;
            return line;
        }
    }
}
