// Cert Lab - a simulated PowerShell session (runspace): its global scope and automatic variables, aliases, the
// process environment and the host (where text goes, where answers to prompts come from).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsRunspace
    {
        public Dictionary<string, string> Env;
        public bool Closed;
        public int CallDepth;
        public string ProcessPolicy = "Undefined";
        public string Home { get { return "C:\\Users\\" + User; } }
        public string ProcessDirectory = "C:\\Windows\\system32";

        // the aliases the course uses (a subset of Windows PowerShell 5.1's built-in ones)
        const string AliasData = "%=ForEach-Object ?=Where-Object foreach=ForEach-Object where=Where-Object select=Select-Object sort=Sort-Object group=Group-Object measure=Measure-Object " +
            "gci=Get-ChildItem dir=Get-ChildItem ls=Get-ChildItem gi=Get-Item gp=Get-ItemProperty sp=Set-ItemProperty ni=New-Item ri=Remove-Item rm=Remove-Item del=Remove-Item " +
            "cp=Copy-Item copy=Copy-Item mv=Move-Item move=Move-Item ren=Rename-Item cd=Set-Location sl=Set-Location pwd=Get-Location gl=Get-Location pushd=Push-Location popd=Pop-Location " +
            "cat=Get-Content gc=Get-Content type=Get-Content sc=Set-Content ac=Add-Content echo=Write-Output write=Write-Output cls=Clear-Host clear=Clear-Host " +
            "gsv=Get-Service sasv=Start-Service spsv=Stop-Service gps=Get-Process ps=Get-Process kill=Stop-Process spps=Stop-Process saps=Start-Process start=Start-Process " +
            "gcm=Get-Command gm=Get-Member gal=Get-Alias gv=Get-Variable sv=Set-Variable ft=Format-Table fl=Format-List fw=Format-Wide man=help h=Get-History history=Get-History " +
            "icm=Invoke-Command etsn=Enter-PSSession exsn=Exit-PSSession nsn=New-PSSession gsn=Get-PSSession rsn=Remove-PSSession sajb=Start-Job gjb=Get-Job rcjb=Receive-Job wjb=Wait-Job rjb=Remove-Job " +
            "ipmo=Import-Module gmo=Get-Module rmo=Remove-Module iex=Invoke-Expression sleep=Start-Sleep tee=Tee-Object sls=Select-String compare=Compare-Object diff=Compare-Object " +
            "epcsv=Export-Csv ipcsv=Import-Csv gwmi=Get-WmiObject curl=Invoke-WebRequest wget=Invoke-WebRequest md=mkdir";

        public PsRunspace(PsMachine m, IPsHost host, string user)
        {
            M = m; Host = host ?? new NullHost(); User = user ?? "Administrator";
            Pid = m.NextPid; m.NextPid += 4 + m.Rng.Next(400);
            Env = m.NewProcessEnv(User);
            Global = new PsScope(this, null, "global");
            foreach (var pair in AliasData.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                Aliases[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            if (m.Ws != null) foreach (var pair in WsLab.ModuleAliases) Aliases[pair.Key] = pair.Value;   // the Hyper-V module's checkpoint aliases (AZ-800)
            Location = ProcessDirectory;
            InitVariables();
            EnsureModuleLoaded("Microsoft.PowerShell.Management");
            EnsureModuleLoaded("Microsoft.PowerShell.Utility");
            EnsureModuleLoaded("PSReadline");
            InitProviders();
            InitAdmin();
            InitNet();
            InitRemoting();
            InitMore();
            m.Sessions.Add(this);
        }
        partial void InitProviders();
        partial void InitAdmin();
        partial void InitNet();
        partial void InitRemoting();
        partial void InitMore();

        void Const(string n, object v) { Global.Vars[n] = new PsVariable { Name = n, Value = v, Constant = true, Options = "Constant" }; }
        void Var(string n, object v) { Global.Vars[n] = new PsVariable { Name = n, Value = v }; }
        void Pref(string n, string v, string type) { Global.Vars[n] = new PsVariable { Name = n, Value = PsConv.To(v, type), Type = PsTypes.Resolve(type) }; }

        void InitVariables()
        {
            Const("true", true); Const("false", false); Const("null", null);
            Var("Error", Errors);
            Var("HOME", Home);
            Var("PSHOME", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0");
            Var("PID", Pid);
            Var("ShellId", "Microsoft.PowerShell");
            Var("PSCulture", "en-US"); Var("PSUICulture", "en-US");
            Var("NestedPromptLevel", 0);
            Var("FormatEnumerationLimit", 4);
            Var("MaximumHistoryCount", 4096);
            Var("PSEdition", "Desktop");
            Var("PSDefaultParameterValues", PsConv.NewHashtable());
            Var("PSModuleAutoLoadingPreference", "All");
            Var("PSSessionConfigurationName", "http://schemas.microsoft.com/powershell/Microsoft.PowerShell");
            Pref("ErrorActionPreference", "Continue", "System.Management.Automation.ActionPreference");
            Pref("WarningPreference", "Continue", "System.Management.Automation.ActionPreference");
            Pref("VerbosePreference", "SilentlyContinue", "System.Management.Automation.ActionPreference");
            Pref("DebugPreference", "SilentlyContinue", "System.Management.Automation.ActionPreference");
            Pref("InformationPreference", "SilentlyContinue", "System.Management.Automation.ActionPreference");
            Pref("ProgressPreference", "Continue", "System.Management.Automation.ActionPreference");
            Var("ConfirmPreference", "High");
            Var("WhatIfPreference", false);
            Var("PSVersionTable", VersionTable());
            Var("Host", HostObject());
            var prof = Home + "\\Documents\\WindowsPowerShell\\Microsoft.PowerShell_profile.ps1";
            var notes = PsNotes.For(prof);
            notes.Add("AllUsersAllHosts", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\profile.ps1");
            notes.Add("AllUsersCurrentHost", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\Microsoft.PowerShell_profile.ps1");
            notes.Add("CurrentUserAllHosts", Home + "\\Documents\\WindowsPowerShell\\profile.ps1");
            notes.Add("CurrentUserCurrentHost", prof);
            Var("PROFILE", prof);
        }

        OrderedDictionary VersionTable()
        {
            var h = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            h["PSVersion"] = new Version(M.PsVersionText);
            h["PSEdition"] = "Desktop";
            h["PSCompatibleVersions"] = new object[] { new Version(1, 0), new Version(2, 0), new Version(3, 0), new Version(4, 0), new Version(5, 0), new Version(5, 1, int.Parse(M.OsBuild), 1) };
            h["BuildVersion"] = new Version(M.OsVersion + "." + M.PsVersionText.Split('.').Last());
            h["CLRVersion"] = new Version(4, 0, 30319, 42000);
            h["WSManStackVersion"] = new Version(3, 0);
            h["PSRemotingProtocolVersion"] = new Version(2, 3);
            h["SerializationVersion"] = new Version(1, 1, 0, 1);
            return h;
        }

        PsObj HostObject()
        {
            var o = new PsObj("System.Management.Automation.Internal.Host.InternalHost", "System.Object");
            o.Val("Name", "string", RemoteFrom != null ? "ServerRemoteHost" : "ConsoleHost");
            o.Val("Version", "version", new Version(M.PsVersionText));
            o.Val("InstanceId", "guid", M.NewGuid());
            var ui = new PsObj("System.Management.Automation.Internal.Host.InternalHostUserInterface", "System.Object");
            var raw = new PsObj("System.Management.Automation.Internal.Host.InternalHostRawUserInterface", "System.Object");
            raw.Prop("WindowTitle", "string", () => "Administrator: Windows PowerShell", v => { });
            raw.Prop("BufferSize", "System.Management.Automation.Host.Size", () => Width + ",9001");
            raw.Val("ForegroundColor", "System.ConsoleColor", PsEnums.V("System.ConsoleColor", "Gray"));
            raw.Val("BackgroundColor", "System.ConsoleColor", PsEnums.V("System.ConsoleColor", "DarkBlue"));
            ui.Val("RawUI", "System.Management.Automation.Host.PSHostRawUserInterface", raw);
            o.Val("UI", "System.Management.Automation.Host.PSHostUserInterface", ui);
            o.Val("CurrentCulture", "cultureinfo", "en-US");
            o.Val("CurrentUICulture", "cultureinfo", "en-US");
            o.Val("PrivateData", "psobject", null);
            o.Val("DebuggerEnabled", "bool", true);
            o.Val("IsRunspacePushed", "bool", false);
            o.Val("Runspace", "runspace", null);
            return o;
        }

        // ------------------------------------------------------------ environment
        public object GetEnv(string name) { string v; return Env.TryGetValue(name, out v) ? v : null; }
        public void SetEnv(string name, string v) { if (v == null) Env.Remove(name); else Env[name] = v; }
        public string EnvFor(string target, string name)
        {
            string v = null;
            switch ((target ?? "Process").ToLowerInvariant())
            {
                case "machine": M.EnvMachine.TryGetValue(name, out v); break;
                case "user": M.EnvUser.TryGetValue(name, out v); break;
                default: Env.TryGetValue(name, out v); break;
            }
            return v;
        }
        public void SetEnvFor(string target, string name, string v)
        {
            Dictionary<string, string> d = Env;
            switch ((target ?? "Process").ToLowerInvariant()) { case "machine": d = M.EnvMachine; break; case "user": d = M.EnvUser; break; }
            if (v == null) d.Remove(name); else d[name] = v;
        }
        public IEnumerable<KeyValuePair<string, string>> EnvAll(string target)
        {
            switch ((target ?? "Process").ToLowerInvariant()) { case "machine": return M.EnvMachine; case "user": return M.EnvUser; }
            return Env;
        }
        public string ExpandEnv(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s ?? "", "%([^%]+)%", m => { string v; return Env.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value; });
        }

        // ------------------------------------------------------------ host
        public void HostWrite(string s) { if (Host != null) Host.Write(s); }
        public Action ClearRequested;
        public void ClearHost() { if (ClearRequested != null) ClearRequested(); }
    }
}
