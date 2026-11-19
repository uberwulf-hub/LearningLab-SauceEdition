// Cert Lab - the MD-102 course's simulator behind ISim: the pretend tenant (EpTenant) and, for each Windows device,
// a pretend PC with the simulated PowerShell console (PsMachine / PsConsole through an inner PsSim). Machine "portal"
// is the practice admin portal: setup and solution lines there are portal commands (EpOps), checks there are tenant
// assertions (EpCheck). Lines for a device are typed into its PowerShell console, except the directives
// #settings join|register|enroll|disconnect|sync USER, #oobe [USER], #portal install "App", #laps use and #wait N.
// Nothing here signs in anywhere or talks to a real service.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class EpSim : ISim
    {
        public EpTenant T;
        public PsNet Net;
        readonly PsSim ps;
        public Action<string> SetupLog;
        public readonly List<string> Errors = new List<string>();   // portal lines that failed (tests show them)
        public string Kind { get { return "endpoint"; } }
        public const string Portal = "portal";
        public string LastResult = "";

        EpSim(int seed)
        {
            PsEnums.Init();
            Net = new PsNet { Seed = seed, Rng = new Random(seed) };
            ps = new PsSim { Net = Net };
            T = EpTenant.Create(Net);
            T.MachineFactory = MakeMachine;
        }
        public static EpSim Create(int seed) { return new EpSim(seed); }

        int nextIp = 31;
        PsMachine MakeMachine(EpDevice d)
        {
            string ip = "10.20.0." + (nextIp++);
            var m = new PsMachine(Net, d.Name, false, ip);
            Net.Machines.Add(m);
            Net.Hosts.Add(new LabHost { Name = d.Name, Ip = ip, Machine = m, Ports = new List<int> { 135, 445 } });
            m.EpDev = d;
            string ed = d["edition"].Length > 0 ? d["edition"] : "Pro";
            bool w11 = EpTenant.IsWin11(d["featureVersion"]);
            m.OsCaption = "Microsoft Windows " + (w11 ? "11 " : "10 ") + ed;
            m.OsEdition = ed == "Pro" ? "Professional" : ed == "Home" ? "Core" : ed;
            m.OsDisplayVersion = d["featureVersion"];
            m.OsBuild = d["osVersion"].Split('.').Length > 2 ? d["osVersion"].Split('.')[2] : "26100";
            m.OsVersion = "10.0." + m.OsBuild;
            if (d.Get("domain").Length > 0) { m.Domain = d["domain"]; m.DomainShort = d["domain"].Split('.')[0].ToUpperInvariant(); }
            else { m.Domain = "WORKGROUP"; m.DomainShort = d.Name; }
            EpDeviceSeed.Prepare(m, d);
            return m;
        }

        public List<string> MachineNames { get { return T.Devices.Where(d => d.Windows).Select(d => d.Name).ToList(); } }
        public PsConsole Console(string machine)
        {
            var d = T.Device(machine);
            if (d == null || !d.Windows) return null;
            T.MachineFor(d);
            return ps.Console(d.Machine.Name);
        }
        public ITerm Term(string machine) { return Console(machine); }
        public bool CanSave { get { return false; } }
        public void StartConsoles(bool autoLogin, IEnumerable<string> noLogin) { }

        public void RunSetup(List<SolutionStep> steps)
        {
            int i = 0;
            while (i < steps.Count)
            {
                string mach = steps[i].Machine;
                if (EpText.Eq(mach, Portal))
                {
                    var line = steps[i++].Line;
                    if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; if (long.TryParse(line.Substring(6).Trim(), out n)) T.Advance(n); continue; }
                    var r = EpOps.Run(T, line);
                    if (!r.Ok) Errors.Add("setup: " + line + " -> " + r.Text);
                    if (SetupLog != null) SetupLog("portal: " + line + " -> " + r.Text);
                    continue;
                }
                var sb = new StringBuilder();
                while (i < steps.Count && steps[i].Machine == mach) { sb.Append(steps[i].Line).Append('\n'); i++; }
                var d = T.Device(mach);
                var m = T.MachineFor(d);
                if (m == null) { Errors.Add("setup: no Windows device " + mach); continue; }
                int code;
                string err = EpTenant.RunPs(m, sb.ToString(), out code);
                if (SetupLog != null) SetupLog(mach + ": " + (err ?? "ok"));
            }
        }

        public bool Check(string machine, string script, out string output)
        {
            if (EpText.Eq(machine, Portal)) return EpCheck.Run(T, script, out output);
            var d = T.Device(machine);
            if (d == null || T.MachineFor(d) == null) { output = "no Windows device " + machine; return false; }
            return ps.Check(d.Machine.Name, script, out output);
        }

        public void RebootAll() { foreach (var m in Net.Machines) m.Reboot(); }
        public void Reboot(string machine) { var d = T.Device(machine); var m = T.MachineFor(d); if (m != null) m.Reboot(); }
        public void Power(string machine) { Reboot(machine); }
        public void Advance(long seconds) { T.Advance(seconds); }
        public string Save() { return ""; }
        public string Clock { get { return T.Now.ToString("ddd MMM d HH:mm", CultureInfo.InvariantCulture); } }
        public string Status(string machine)
        {
            var d = T.Device(machine);
            if (d == null) return "";
            return d.Name + " - " + (d.Is("state", "oobe") ? "out-of-box experience" : d.JoinType + (d.Enrolled ? ", Intune: " + d.Compliance : ", not enrolled"));
        }
        public bool Idle { get { return ps.Idle; } }
        /// <summary>What a device's console shows for typed lines (lesson examples, tests).</summary>
        public string Transcript(string machine, IEnumerable<string> lines, int width)
        {
            var d = T.Device(machine);
            if (d == null || T.MachineFor(d) == null) return "";
            return ps.Transcript(d.Machine.Name, lines, width);
        }
        public void Dispose() { ps.Dispose(); }

        /// <summary>Types a solution line: a portal command, a device directive, or a PowerShell line on a device.</summary>
        public void Type(string machine, string line)
        {
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; if (long.TryParse(line.Substring(6).Trim(), out n)) T.Advance(n); return; }
            if (EpText.Eq(machine, Portal))
            {
                var r = EpOps.Run(T, line);
                LastResult = r.Text;
                if (!r.Ok) Errors.Add(line + " -> " + r.Text);
                return;
            }
            var d = T.Device(machine);
            if (d == null) { Errors.Add("no device " + machine); return; }
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                string why = Directive(d, line);
                LastResult = why ?? "ok";
                if (why != null) Errors.Add(machine + ": " + line + " -> " + why);
                return;
            }
            var c = Console(machine);
            if (c == null) { Errors.Add(machine + " has no console"); return; }
            if (!c.Waiting) c.Start();
            c.Execute(line);
        }

        /// <summary>What a person does on the device outside the console (the pretend Settings app, setup, Company Portal).</summary>
        public string Directive(EpDevice d, string line)
        {
            var tok = EpOps.Tokens(line.Substring(1));
            if (tok.Count == 0) return null;
            string arg = tok.Count > 2 ? tok[2] : "";
            switch (tok[0].ToLowerInvariant())
            {
                case "settings":
                    switch (tok.Count > 1 ? tok[1].ToLowerInvariant() : "")
                    {
                        case "join": return T.JoinFromSettings(d, arg);
                        case "register": return T.RegisterFromSettings(d, arg);
                        case "enroll": return T.EnrollOnly(d, arg);
                        case "disconnect": return T.Disconnect(d);
                        case "sync": if (!d.Enrolled) return "This device isn't managed, so there is nothing to sync."; T.Sync(d); return null;
                    }
                    return "Unknown Settings action.";
                case "oobe": return T.RunOobe(d, tok.Count > 1 ? tok[1] : "");
                case "portal":
                    {
                        if (tok.Count < 3 || tok[1] != "install") return "#portal install \"App\"";
                        var app = T.Find("app", tok[2]);
                        if (app == null) return "Company Portal doesn't list " + tok[2] + ".";
                        EpAppState st;
                        if (!d.Apps.TryGetValue(app.Name, out st) || st.Status != "Available" && st.Status != "Failed") return app.Name + " isn't available to install in Company Portal on this device.";
                        T.Install(d, app, "available", 0);
                        return null;
                    }
                case "laps": T.LapsUsed(d); return null;
                case "power": d["state"] = tok.Count > 1 && tok[1] == "off" ? "off" : "running"; return null;
            }
            return "Unknown directive " + tok[0] + ".";
        }

        /// <summary>How a solution line reads for a person (the "show me" panel and the exam review).</summary>
        public static string SolutionText(string machine, string line)
        {
            if (EpText.Eq(machine, Portal)) return EpOps.Describe(line);
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) { long n; long.TryParse(line.Substring(6).Trim(), out n); return "(wait " + (n >= 3600 ? (n / 3600) + " hour" + (n >= 7200 ? "s" : "") : n >= 60 ? (n / 60) + " minutes" : n + " seconds") + ": use Skip time)"; }
            if (!line.StartsWith("#", StringComparison.Ordinal)) return line;
            var tok = EpOps.Tokens(line.Substring(1));
            string a = tok.Count > 2 ? tok[2] : tok.Count > 1 ? tok[1] : "";
            switch (tok.Count > 0 ? tok[0] : "")
            {
                case "settings":
                    switch (tok.Count > 1 ? tok[1] : "")
                    {
                        case "join": return "(Settings > Accounts > Access work or school > Connect > Join this device to Microsoft Entra ID, sign in as " + a + ")";
                        case "register": return "(Settings > Accounts > Access work or school > Connect, sign in as " + a + ": adds the work account)";
                        case "enroll": return "(Settings > Accounts > Access work or school > Enroll only in device management, sign in as " + a + ")";
                        case "disconnect": return "(Settings > Accounts > Access work or school > the work account > Disconnect)";
                        case "sync": return "(Settings > Accounts > Access work or school > the work account > Info > Sync)";
                    }
                    break;
                case "oobe": return tok.Count > 1 ? "(turn the device on and go through setup; sign in as " + tok[1] + ")" : "(turn the device on and let setup run)";
                case "portal": return "(open Company Portal on the device and install " + a + ")";
                case "laps": return "(sign in to the device with the local administrator password from the portal)";
            }
            return null;
        }
    }
}
