// Cert Lab - modules in the simulated PowerShell: the built-in modules (each cmdlet names its module; a module
// loads the first time one of its commands runs), and script modules (.psm1 with an optional .psd1 manifest) found
// in the folders of $env:PSModulePath, run in their own scope and exporting their functions.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public class PsModule
    {
        public string Name = "", Path = "", ModuleBase = "", Type = "Script", Description = "", Author = "", Guid = "";
        public Version Version = new Version(0, 0);
        public PsScope Scope;
        public readonly Dictionary<string, PsFunction> Exported = new Dictionary<string, PsFunction>(StringComparer.OrdinalIgnoreCase);
        public List<string> ExportList;          // Export-ModuleMember -Function (null: everything)
        public List<string> ManifestFunctions;   // FunctionsToExport from the manifest
        public bool BuiltIn;
        public string ListDirectory;             // Get-Module -ListAvailable: the module folder it was found in (groups the table)
        public string CompanyName = "", Copyright = "", PowerShellVersion = "";
        public readonly List<string> RequiredModules = new List<string>();
        public readonly List<string> ExportedVariables = new List<string>(), ExportedAliases = new List<string>();
        public List<string> VariableExportList, AliasExportList;   // Export-ModuleMember -Variable / -Alias (null: none given)
        public List<string> KnownCommands;       // modules that are not simulated: their command names for Get-Module
        public override string ToString() { return Name; }

        public IEnumerable<string> CommandNames
        {
            get
            {
                if (BuiltIn) return PsCmdlets.All.Values.Where(c => c.Module.Equals(Name, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                return Exported.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            }
        }

        public object Member(string name, out bool found)
        {
            found = true;
            foreach (var kv in Properties()) if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            found = false; return null;
        }
        public List<KeyValuePair<string, object>> Properties()
        {
            var l = new List<KeyValuePair<string, object>>();
            l.Add(new KeyValuePair<string, object>("Name", Name));
            l.Add(new KeyValuePair<string, object>("Path", Path));
            l.Add(new KeyValuePair<string, object>("Description", Description));
            l.Add(new KeyValuePair<string, object>("ModuleType", PsEnums.V("System.Management.Automation.ModuleType", Type)));
            l.Add(new KeyValuePair<string, object>("Version", Version));
            l.Add(new KeyValuePair<string, object>("ModuleBase", ModuleBase));
            l.Add(new KeyValuePair<string, object>("Author", Author));
            var cmds = PsConv.NewOrdered();
            foreach (var c in CommandNames) cmds[c] = c;
            l.Add(new KeyValuePair<string, object>("ExportedCommands", cmds));
            var fns = PsConv.NewOrdered();
            if (!BuiltIn) foreach (var c in CommandNames) fns[c] = c;
            l.Add(new KeyValuePair<string, object>("ExportedFunctions", fns));
            var cmdlets = PsConv.NewOrdered();
            if (BuiltIn) foreach (var c in CommandNames) cmdlets[c] = c;
            l.Add(new KeyValuePair<string, object>("ExportedCmdlets", cmdlets));
            return l;
        }
    }

    public static class PsBuiltinModules
    {
        // name|version|type
        static readonly string[] data = {
            "Microsoft.PowerShell.Core|3.0.0.0|Manifest", "Microsoft.PowerShell.Management|3.1.0.0|Manifest", "Microsoft.PowerShell.Utility|3.1.0.0|Manifest",
            "Microsoft.PowerShell.Security|3.0.0.0|Manifest", "Microsoft.PowerShell.Diagnostics|3.0.0.0|Manifest", "Microsoft.PowerShell.LocalAccounts|1.0.0.0|Manifest",
            "Microsoft.PowerShell.Archive|1.0.1.0|Script", "Microsoft.WSMan.Management|3.0.0.0|Manifest", "CimCmdlets|1.0.0.0|Manifest", "NetTCPIP|1.0.0.0|Manifest",
            "DnsClient|1.0.0.0|Manifest", "NetAdapter|2.0.0.0|Manifest", "NetSecurity|2.0.0.0|Manifest", "ScheduledTasks|1.0.0.0|Manifest", "PSDesiredStateConfiguration|1.1|Manifest",
            "ServerManager|2.0.0.0|Manifest", "PSReadline|2.0.0|Script", "PowerShellGet|1.0.0.1|Script", "PackageManagement|1.0.0.1|Script", "Storage|2.0.0.0|Manifest",
            "Pester|3.4.0|Script", "NetConnection|1.0.0.0|Manifest", "PKI|1.0.0.0|Manifest",
            "BitLocker|1.0.0.0|Manifest", "Defender|1.0|Manifest", "TrustedPlatformModule|2.0.0.0|Manifest", "SecureBoot|2.0.0.0|Manifest", "Provisioning|3.0|Manifest", "LAPS|1.0.0.0|Manifest"
        };
        public static PsModule Make(string name)
        {
            foreach (var d in data)
            {
                var p = d.Split('|');
                if (!p[0].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                string bas = p[0] == "PSReadline" || p[0] == "PowerShellGet" || p[0] == "PackageManagement" || p[0] == "Pester" ? "C:\\Program Files\\WindowsPowerShell\\Modules\\" + p[0] + "\\" + p[1] : "C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\Modules\\" + p[0];
                return new PsModule { Name = p[0], Version = new Version(p[1]), Type = p[2], BuiltIn = true, ModuleBase = bas, Path = bas + "\\" + p[0] + ".psd1" };
            }
            return null;
        }
        public static IEnumerable<string> Names { get { return data.Select(d => d.Split('|')[0]); } }
    }

    public partial class PsRunspace
    {
        public readonly List<PsModule> LoadedModules = new List<PsModule>();

        public PsModule LoadedModule(string name) { return LoadedModules.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }

        public void EnsureModuleLoaded(string name)
        {
            if (name == "Microsoft.PowerShell.Core" || LoadedModule(name) != null) return;
            var m = PsBuiltinModules.Make(name);
            if (m != null) LoadedModules.Add(m);
        }
        public string ModuleVersion(string name) { var m = PsBuiltinModules.Make(name); return m == null ? "" : m.Version.ToString(); }

        public IEnumerable<string> ModuleDirs { get { return ((GetEnv("PSModulePath") as string) ?? "").Split(';').Where(x => x.Length > 0); } }

        /// <summary>A script module's file in the module folders: NAME\NAME.psd1, NAME\NAME.psm1, NAME\VERSION\NAME.psd1.</summary>
        public string FindModuleFile(string name)
        {
            foreach (var dir in ModuleDirs)
            {
                var d = FsNode(FullFsPath(dir + "\\" + name, null));
                if (d == null || !d.IsDir) continue;
                foreach (var ext in new[] { ".psd1", ".psm1" }) { var f = d.Kid(d.Name + ext); if (f != null && !f.IsDir) return f.FullPath; }
                foreach (var v in d.Kids.Values.Where(k => k.IsDir).OrderByDescending(k => k.Name))
                    foreach (var ext in new[] { ".psd1", ".psm1" }) { var f = v.Kid(d.Name + ext); if (f != null && !f.IsDir) return f.FullPath; }
            }
            return null;
        }

        /// <summary>Loads a script module the first time one of its functions is used (module auto-loading).</summary>
        PsModule AutoloadFunction(string name)
        {
            if (!name.Contains("-")) return null;
            foreach (var dir in ModuleDirs)
            {
                var d = FsNode(FullFsPath(dir, null));
                if (d == null || !d.IsDir) continue;
                foreach (var md in d.Kids.Values.Where(k => k.IsDir))
                {
                    if (LoadedModule(md.Name) != null) continue;
                    string file = FindModuleFile(md.Name);
                    if (file == null) continue;
                    var text = ReadFileText(file) ?? "";
                    string psm = file.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) ? null : text;
                    if (psm == null)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(text, "FunctionsToExport\\s*=[^\\n]*'" + System.Text.RegularExpressions.Regex.Escape(name) + "'", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return ImportModuleFile(file, null);
                        var root = System.Text.RegularExpressions.Regex.Match(text, "(RootModule|ModuleToProcess)\\s*=\\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (root.Success) psm = ReadFileText(file.Substring(0, file.LastIndexOf('\\')) + "\\" + root.Groups[2].Value) ?? "";
                    }
                    if (psm != null && System.Text.RegularExpressions.Regex.IsMatch(psm, "function\\s+" + System.Text.RegularExpressions.Regex.Escape(name) + "\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        try { return ImportModuleFile(file, null); } catch (PsRuntimeException) { return null; }
                    }
                }
            }
            return null;
        }

        /// <summary>Imports a .psm1 or .psd1 file; the module's functions become visible to the session.</summary>
        public PsModule ImportModuleFile(string file, Streams st)
        {
            if (PsModuleHook.Import != null) return PsModuleHook.Import(this, file, st);
            string why = ScriptBlockedReason(file);
            if (why != null) throw RtError(null, why, "UnauthorizedAccess", "SecurityError", "System.Management.Automation.PSSecurityException", null);
            string name = file.Substring(file.LastIndexOf('\\') + 1); name = name.Substring(0, name.LastIndexOf('.'));
            var m = new PsModule { Name = name, Path = file, ModuleBase = file.Substring(0, file.LastIndexOf('\\')), Type = "Script", Version = new Version(0, 0) };
            m.Scope = new PsScope(this, Global, "module") { Module = m };
            var ast = PsParser.Parse(ReadFileText(file) ?? "", file);
            ExecBlock(ast.Body ?? ast.EndBlock, m.Scope, st ?? new Streams());
            foreach (var f in m.Scope.Funcs.Values) if (m.ExportList == null || m.ExportList.Contains(f.Name, StringComparer.OrdinalIgnoreCase)) m.Exported[f.Name] = f;
            LoadedModules.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            LoadedModules.Add(m);
            return m;
        }
    }

    public static class PsModuleHook { public static Func<PsRunspace, string, Streams, PsModule> Import; }
}
