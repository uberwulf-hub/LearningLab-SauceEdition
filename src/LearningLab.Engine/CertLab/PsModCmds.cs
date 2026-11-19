// Cert Lab - module commands of the simulated Windows PowerShell 5.1: Get-Module (loaded, -ListAvailable grouped by
// folder), Import-Module (by name, by path, .psm1 and .psd1 manifests with RootModule, ModuleVersion, FunctionsToExport,
// RequiredModules, NestedModules; -Force, -PassThru, -Prefix, -Verbose), Remove-Module, Export-ModuleMember,
// New-ModuleManifest / Test-ModuleManifest, and the PowerShellGet commands, which explain that the practice network
// has no internet. Module code runs in the module's own scope: only what is exported is visible to the session.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class PsModMgr
    {
        static readonly Dictionary<string, string[]> known = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "PSReadline", new[] { "Get-PSReadLineKeyHandler", "Get-PSReadLineOption", "Remove-PSReadLineKeyHandler", "Set-PSReadLineKeyHandler", "Set-PSReadLineOption", "PSConsoleHostReadLine" } },
            { "Pester", new[] { "Describe", "Context", "It", "Should", "Mock", "Assert-MockCalled", "BeforeEach", "AfterEach", "Invoke-Pester", "New-Fixture" } },
            { "PackageManagement", new[] { "Find-Package", "Get-Package", "Get-PackageProvider", "Get-PackageSource", "Install-Package", "Install-PackageProvider", "Register-PackageSource", "Save-Package", "Uninstall-Package" } },
        };

        public static List<string> CommandsOf(PsModule m)
        {
            if (m.KnownCommands != null) return m.KnownCommands;
            if (m.BuiltIn)
            {
                var l = m.CommandNames.ToList();
                string[] k;
                if (l.Count == 0 && known.TryGetValue(m.Name, out k)) l = k.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                return l;
            }
            return m.Exported.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A PSModuleInfo object for Get-Module, Import-Module -PassThru and Test-ModuleManifest.</summary>
        public static PsObj Info(PsModule m)
        {
            var o = new PsObj("System.Management.Automation.PSModuleInfo", "System.Object");
            var cmds = CommandsOf(m);
            var all = PsConv.NewOrdered(); foreach (var c in cmds) all[c] = c;
            var fns = PsConv.NewOrdered(); if (!m.BuiltIn) foreach (var c in cmds) fns[c] = c;
            var cmdlets = PsConv.NewOrdered(); if (m.BuiltIn) foreach (var c in cmds) cmdlets[c] = c;
            var vars = PsConv.NewOrdered(); foreach (var v in m.ExportedVariables) vars[v] = v;
            var als = PsConv.NewOrdered(); foreach (var a in m.ExportedAliases) als[a] = a;
            o.Val("Name", "string", m.Name);
            o.Val("Path", "string", m.Path);
            o.Val("Description", "string", m.Description);
            o.Val("Guid", "guid", string.IsNullOrEmpty(m.Guid) ? (object)Guid.Empty : (object)new Guid(m.Guid));
            o.Val("ModuleBase", "string", m.ModuleBase);
            o.Val("ModuleType", "System.Management.Automation.ModuleType", PsEnums.V("System.Management.Automation.ModuleType", m.Type));
            o.Val("Version", "version", m.Version);
            o.Val("Author", "string", m.Author);
            o.Val("CompanyName", "string", m.CompanyName);
            o.Val("Copyright", "string", m.Copyright);
            o.Val("PowerShellVersion", "version", m.PowerShellVersion.Length > 0 ? new Version(m.PowerShellVersion) : null);
            o.Val("RequiredModules", "System.Collections.ObjectModel.ReadOnlyCollection[psmoduleinfo]", m.RequiredModules.Cast<object>().ToArray());
            o.Val("ExportedCommands", "System.Collections.Generic.Dictionary[string,System.Management.Automation.CommandInfo]", all);
            o.Val("ExportedFunctions", "System.Collections.Generic.Dictionary[string,System.Management.Automation.FunctionInfo]", fns);
            o.Val("ExportedCmdlets", "System.Collections.Generic.Dictionary[string,System.Management.Automation.CmdletInfo]", cmdlets);
            o.Val("ExportedVariables", "System.Collections.Generic.Dictionary[string,System.Management.Automation.PSVariable]", vars);
            o.Val("ExportedAliases", "System.Collections.Generic.Dictionary[string,System.Management.Automation.AliasInfo]", als);
            o.Prop("ExportedCommandsText", "string", () => "{" + string.Join(", ", cmds.Take(4).ToArray()) + (cmds.Count > 4 ? "..." : "") + "}");
            o.ToStr = x => m.Name;
            o.Base = m;
            return o;
        }

        static bool Listed(List<string> pats, string name) { return pats == null || pats.Any(p => PsConv.Like(name, p, false)); }

        /// <summary>Reads a .psd1 file: a hashtable in PowerShell syntax.</summary>
        public static IDictionary ReadData(PsRunspace R, string file, out string error)
        {
            error = null;
            var text = R.ReadFileText(file);
            if (text == null) { error = "Cannot find path '" + file + "' because it does not exist."; return null; }
            try
            {
                var ast = PsParser.Parse(text, file);
                var outp = R.InvokeBlock(new PsScriptBlock { Ast = ast }, R.Global, null, null, false);
                var h = outp.Select(x => PsConv.Base(x) as IDictionary).FirstOrDefault(x => x != null);
                if (h == null) error = "The file '" + file + "' does not contain a hashtable.";
                return h;
            }
            catch (PsParseError e) { error = e.Message; return null; }
            catch (PsRuntimeException e) { error = e.Rec.Exception.Message; return null; }
        }
        public static object Key(IDictionary h, string k) { foreach (DictionaryEntry e in h) if (PsConv.ToStr(e.Key).Equals(k, StringComparison.OrdinalIgnoreCase)) return PsConv.Base(e.Value); return null; }
        public static List<string> KeyList(IDictionary h, string k)
        {
            var v = Key(h, k);
            if (v == null) return null;
            return PsConv.Items(v).Select(x => { var d = PsConv.Base(x) as IDictionary; return d != null ? PsConv.ToStr(Key(d, "ModuleName")) : PsConv.ToStr(x); }).ToList();
        }

        public class ImportError : Exception
        {
            public string Id, Category, ExType;
            public ImportError(string msg, string id, string cat, string ex) : base(msg) { Id = id; Category = cat; ExType = ex; }
        }

        /// <summary>Imports a module file (.psm1, .psd1 or .ps1). verbose gets the VERBOSE lines.</summary>
        public static PsModule Import(PsRunspace R, string file, Streams st, Action<string> verbose, string prefix)
        {
            if (verbose == null) verbose = s => { };
            string dir = file.Substring(0, file.LastIndexOf('\\'));
            string name = file.Substring(file.LastIndexOf('\\') + 1); name = name.Substring(0, name.LastIndexOf('.'));
            string why = R.ScriptBlockedReason(file);
            if (why != null) throw new ImportError(why, "UnauthorizedAccess", "SecurityError", "System.Management.Automation.PSSecurityException");
            verbose("Loading module from path '" + file + "'.");
            var m = new PsModule { Name = name, Path = file, ModuleBase = dir, Type = "Script", Version = new Version(0, 0) };
            m.Scope = new PsScope(R, R.Global, "module") { Module = m };
            List<string> manifestFns = null, manifestVars = null, manifestAliases = null;
            var scripts = new List<string>();
            if (file.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                string err;
                var h = ReadData(R, file, out err);
                if (h == null) throw new ImportError("The module manifest '" + file + "' could not be processed because it is not a valid Windows PowerShell restricted language file. " + err, "Modules_InvalidManifest", "InvalidData", "System.Management.Automation.PSInvalidOperationException");
                var ver = Key(h, "ModuleVersion");
                if (ver == null) throw new ImportError("The module manifest '" + file + "' does not contain a valid 'ModuleVersion' key. The ModuleVersion key is required in a module manifest.", "Modules_InvalidManifest", "InvalidData", "System.Management.Automation.PSInvalidOperationException");
                Version v; if (!Version.TryParse(PsConv.ToStr(ver), out v)) v = new Version(0, 0);
                m.Version = v;
                m.Description = PsConv.ToStr(Key(h, "Description") ?? "");
                m.Author = PsConv.ToStr(Key(h, "Author") ?? "");
                m.CompanyName = PsConv.ToStr(Key(h, "CompanyName") ?? "");
                m.Copyright = PsConv.ToStr(Key(h, "Copyright") ?? "");
                m.Guid = PsConv.ToStr(Key(h, "GUID") ?? "");
                m.PowerShellVersion = PsConv.ToStr(Key(h, "PowerShellVersion") ?? "");
                Guid gd; if (!Guid.TryParse(m.Guid, out gd)) m.Guid = "";
                foreach (var rq in KeyList(h, "RequiredModules") ?? new List<string>())
                {
                    if (R.LoadedModule(rq) != null) { m.RequiredModules.Add(rq); continue; }
                    string rf = R.FindModuleFile(rq);
                    if (rf != null) { Import(R, rf, st, verbose, null); m.RequiredModules.Add(rq); continue; }
                    if (PsBuiltinModules.Make(rq) != null) { R.EnsureModuleLoaded(rq); m.RequiredModules.Add(rq); continue; }
                    throw new ImportError("The required module '" + rq + "' is not loaded. Load the module or remove the module from 'RequiredModules' in the file '" + file + "'.", "Modules_InvalidManifest", "ResourceUnavailable", "System.IO.FileNotFoundException");
                }
                manifestFns = KeyList(h, "FunctionsToExport");
                manifestVars = KeyList(h, "VariablesToExport");
                manifestAliases = KeyList(h, "AliasesToExport");
                var root = PsConv.ToStr(Key(h, "RootModule") ?? Key(h, "ModuleToProcess") ?? "");
                foreach (var nm in KeyList(h, "NestedModules") ?? new List<string>()) scripts.Add(nm);
                if (root.Length > 0) scripts.Add(root);
                m.Type = root.Length > 0 ? "Script" : "Manifest";
                for (int i = 0; i < scripts.Count; i++)
                {
                    string s = scripts[i];
                    string full = R.FullFsPath(s, dir);
                    if (R.FsNode(full) == null && !s.Contains(".")) { if (R.FsNode(full + ".psm1") != null) full += ".psm1"; else if (R.FsNode(full + ".ps1") != null) full += ".ps1"; }
                    if (R.FsNode(full) == null)
                        throw new ImportError("The module to process '" + s + "', listed in field 'ModuleToProcess/RootModule' of module manifest '" + file + "' was not processed because no valid module was found in any module directory.", "Modules_ModuleFileNotFound", "ResourceUnavailable", "System.IO.FileNotFoundException");
                    scripts[i] = full;
                }
            }
            else scripts.Add(file);
            m.Scope.Vars["PSScriptRoot"] = new PsVariable { Name = "PSScriptRoot", Value = dir };
            foreach (var s in scripts)
            {
                if (s != file)
                {
                    string w = R.ScriptBlockedReason(s);
                    if (w != null) throw new ImportError(w, "UnauthorizedAccess", "SecurityError", "System.Management.Automation.PSSecurityException");
                    verbose("Loading module from path '" + s + "'.");
                }
                m.Scope.Vars["PSCommandPath"] = new PsVariable { Name = "PSCommandPath", Value = s };
                ScriptBlockAst ast;
                try { ast = PsParser.Parse(R.ReadFileText(s) ?? "", s); }
                catch (PsParseError e) { throw new PsRuntimeException(R.ParseErrorRecord(e), false); }
                R.ExecBlock(ast.Body ?? ast.EndBlock, m.Scope, st ?? new Streams());
            }
            foreach (var f in m.Scope.Funcs.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!Listed(m.ExportList, f.Name)) continue;
                if (manifestFns != null && !Listed(manifestFns, f.Name)) continue;
                string key = f.Name;
                if (!string.IsNullOrEmpty(prefix) && key.Contains("-")) key = key.Substring(0, key.IndexOf('-') + 1) + prefix + key.Substring(key.IndexOf('-') + 1);
                m.Exported[key] = key == f.Name ? f : new PsFunction { Name = key, Ast = f.Ast, Filter = f.Filter, Module = f.Module, Home = f.Home };
            }
            foreach (var v in m.Scope.Vars.Values.Where(x => m.VariableExportList != null && Listed(m.VariableExportList, x.Name) && (manifestVars == null || Listed(manifestVars, x.Name))))
            {
                m.ExportedVariables.Add(v.Name);
                R.Global.Vars[v.Name] = v;
            }
            if (m.AliasExportList != null) foreach (var a in R.Aliases.Keys.Where(a => Listed(m.AliasExportList, a) && (manifestAliases == null || Listed(manifestAliases, a))).ToList()) m.ExportedAliases.Add(a);
            foreach (var k in m.Exported.Keys) verbose("Importing function '" + k + "'.");
            foreach (var v in m.ExportedVariables) verbose("Importing variable '" + v + "'.");
            foreach (var a in m.ExportedAliases) verbose("Importing alias '" + a + "'.");
            Unload(R, R.LoadedModule(name));
            R.LoadedModules.Add(m);
            return m;
        }

        public static void Unload(PsRunspace R, PsModule m)
        {
            if (m == null) return;
            R.LoadedModules.Remove(m);
            if (m.Scope != null)
                foreach (var v in m.ExportedVariables) { PsVariable g; if (R.Global.Vars.TryGetValue(v, out g) && m.Scope.Vars.ContainsKey(v) && m.Scope.Vars[v] == g) R.Global.Vars.Remove(v); }
            foreach (var a in m.ExportedAliases) R.Aliases.Remove(a);
        }

        /// <summary>What Get-Module -ListAvailable finds: the module folders of $env:PSModulePath and the built-in modules.</summary>
        public static List<PsModule> Available(PsRunspace R)
        {
            var res = new List<PsModule>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in R.ModuleDirs)
            {
                string dir = R.FullFsPath(R.ExpandEnv(raw), null).TrimEnd('\\');
                var node = R.FsNode(dir);
                var here = new List<PsModule>();
                if (node != null && node.IsDir)
                    foreach (var md in node.Kids.Values.Where(k => k.IsDir))
                    {
                        string f = R.FindModuleFile(md.Name);
                        if (f == null || !f.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                        var d = Describe(R, f);
                        if (d != null) { here.Add(d); seen.Add(d.Name + "|" + dir); }
                    }
                foreach (var n in PsBuiltinModules.Names)
                {
                    var b = PsBuiltinModules.Make(n);
                    if (b.Name == "ServerManager" && !R.M.IsServer) continue;
                    if (!b.ModuleBase.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (here.Any(x => x.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    here.Add(b);
                }
                string shown = raw.TrimEnd('\\');
                foreach (var m in here.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) { m.ListDirectory = shown; res.Add(m); }
            }
            return res;
        }

        /// <summary>A module on disk as -ListAvailable and Test-ModuleManifest describe it, without running it.</summary>
        public static PsModule Describe(PsRunspace R, string file)
        {
            string dir = file.Substring(0, file.LastIndexOf('\\'));
            string name = file.Substring(file.LastIndexOf('\\') + 1); name = name.Substring(0, name.LastIndexOf('.'));
            var m = new PsModule { Name = name, Path = file, ModuleBase = dir, Type = "Script", Version = new Version(0, 0) };
            string psm = null;
            List<string> fns = null;
            if (file.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                string err;
                var h = ReadData(R, file, out err);
                if (h == null) return null;
                Version v; if (Version.TryParse(PsConv.ToStr(Key(h, "ModuleVersion") ?? "0.0"), out v)) m.Version = v;
                m.Description = PsConv.ToStr(Key(h, "Description") ?? "");
                m.Author = PsConv.ToStr(Key(h, "Author") ?? "");
                m.Guid = PsConv.ToStr(Key(h, "GUID") ?? ""); Guid g; if (!Guid.TryParse(m.Guid, out g)) m.Guid = "";
                var root = PsConv.ToStr(Key(h, "RootModule") ?? Key(h, "ModuleToProcess") ?? "");
                m.Type = root.Length > 0 ? "Script" : "Manifest";
                fns = KeyList(h, "FunctionsToExport");
                if (root.Length > 0) { var rp = R.FullFsPath(root, dir); psm = R.ReadFileText(rp) ?? R.ReadFileText(rp + ".psm1"); }
                foreach (var rq in KeyList(h, "RequiredModules") ?? new List<string>()) m.RequiredModules.Add(rq);
            }
            else psm = R.ReadFileText(file);
            var names = new List<string>();
            if (psm != null) foreach (Match mt in Regex.Matches(psm, "(?im)^\\s*function\\s+([A-Za-z][\\w-]*)")) names.Add(mt.Groups[1].Value);
            if (fns != null && !(fns.Count == 1 && fns[0] == "*")) names = fns.Where(x => !x.Contains("*")).ToList().Count > 0 && names.Count == 0 ? fns : names.Where(n => Listed(fns, n)).ToList();
            m.KnownCommands = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            return m;
        }

        // ------------------------------------------------------------ New-ModuleManifest
        static string Q(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }
        static string QList(string[] l, string empty)
        {
            if (l == null) return empty;
            if (l.Length == 0) return "@()";
            if (l.Length == 1) return Q(l[0]);
            return string.Join(", ", l.Select(Q).ToArray());
        }
        public static string ManifestText(string name, string root, string version, string guid, string author, string company, string copyright, string description, string psVersion, string[] required, string[] functions, string[] cmdlets, string[] variables, string[] aliases, string[] nested, DateTime now)
        {
            var sb = new StringBuilder();
            sb.Append("#\r\n# Module manifest for module '").Append(name).Append("'\r\n#\r\n# Generated by: ").Append(author).Append("\r\n#\r\n# Generated on: ").Append(now.ToString("M/d/yyyy", PsConv.Us)).Append("\r\n#\r\n\r\n@{\r\n\r\n");
            Action<string, string, bool> line = (comment, value, on) => { sb.Append("# ").Append(comment).Append("\r\n").Append(on ? "" : "# ").Append(value).Append("\r\n\r\n"); };
            line("The script module (.psm1) or other file that runs when the module is imported.", "RootModule = " + Q(root ?? ""), root != null);
            line("The version of this module.", "ModuleVersion = " + Q(version), true);
            line("A unique ID for this module.", "GUID = " + Q(guid), true);
            line("Who wrote this module.", "Author = " + Q(author), true);
            line("The company or vendor of this module.", "CompanyName = " + Q(company), true);
            line("Copyright statement for this module.", "Copyright = " + Q(copyright), true);
            line("What the module does.", "Description = " + Q(description ?? ""), description != null);
            line("The oldest Windows PowerShell version this module works with.", "PowerShellVersion = " + Q(psVersion ?? ""), psVersion != null);
            line("Modules that must be loaded before this one.", "RequiredModules = @(" + (required == null ? "" : string.Join(", ", required.Select(Q).ToArray())) + ")", required != null);
            line("Modules to load inside this module (its scope) before RootModule.", "NestedModules = @(" + (nested == null ? "" : string.Join(", ", nested.Select(Q).ToArray())) + ")", nested != null);
            line("Functions the module makes visible. List them by name: '*' works, but slows down module auto-loading.", "FunctionsToExport = " + QList(functions, "'*'"), true);
            line("Cmdlets to export from this module.", "CmdletsToExport = " + QList(cmdlets, "'*'"), true);
            line("Variables to export from this module.", "VariablesToExport = " + QList(variables, "'*'"), true);
            line("Aliases to export from this module.", "AliasesToExport = " + QList(aliases, "'*'"), true);
            sb.Append("# Extra data for the PowerShell Gallery (tags, project links...).\r\nPrivateData = @{\r\n\r\n    PSData = @{\r\n\r\n        # Tags = @()\r\n\r\n    }\r\n\r\n}\r\n\r\n}\r\n\r\n");
            return sb.ToString();
        }
    }

    public partial class PsCmdlets
    {
        static void ImportFail(CmdCtx c, PsModMgr.ImportError e, object target)
        {
            var r = c.MakeError(e.Message, e.Id, e.Category, target, e.ExType);
            if (target == null) { r.TargetName = ""; r.TargetType = ""; }
            c.WriteError(r);
        }

        static partial void RegisterModules()
        {
            PsModuleHook.Import = (R, file, st) =>
            {
                try { return PsModMgr.Import(R, file, st, null, null); }
                catch (PsModMgr.ImportError e) { throw R.RtError(null, e.Message, e.Id, e.Category, e.ExType, null); }
            };

            Add("Get-Module", Core, "Name:string[]@0 pipe; ListAvailable:switch; All:switch; Refresh:switch; FullyQualifiedName:object[]; PSEdition:string", (c, o) =>
            {
                var R = c.R;
                var pats = c.Strs("Name");
                if (c.Sw("ListAvailable"))
                {
                    foreach (var m in PsModMgr.Available(R).Where(x => pats == null || pats.Any(p => PsConv.Like(x.Name, p, false)))) c.Out(PsModMgr.Info(m));
                    return;
                }
                foreach (var m in R.LoadedModules.Where(x => pats == null || pats.Any(p => PsConv.Like(x.Name, p, false))).ToList()) c.Out(PsModMgr.Info(m));
            });
            Add("Import-Module", Core, "Name:string[]@0 ! pipe prop; ModuleInfo:object; Force:switch; PassThru:switch; Prefix:string; Global:switch; Scope:string set=Local|Global; Function:string[]; Cmdlet:string[]; Variable:string[]; Alias:string[]; MinimumVersion:version alias=Version; RequiredVersion:version; MaximumVersion:string; DisableNameChecking:switch; NoClobber:switch; AsCustomObject:switch; ArgumentList:object[] alias=Args", (c, o) =>
            {
                var R = c.R;
                var names = new List<object>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is PsModule) names.Add(((PsModule)((PsObj)PsConv.Base(o)).Base).Path);
                else names.AddRange(c.Objs("Name"));
                Action<string> verbose = s => c.Verbose(s);
                foreach (var nv in names)
                {
                    string name = PsConv.ToStr(nv);
                    bool isPath = name.Contains("\\") || name.Contains("/") || name.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
                    string file = null;
                    if (isPath)
                    {
                        string full = R.FullFsPath(name, R.CurrentFsDir);
                        var node = R.FsNode(full);
                        if (node != null && node.IsDir) { foreach (var ext in new[] { ".psd1", ".psm1" }) { var k = node.Kid(node.Name + ext); if (k != null) { file = k.FullPath; break; } } }
                        else if (node != null) file = node.FullPath;
                        if (file == null) { NotLoaded(c, name); continue; }
                    }
                    else
                    {
                        var loaded = R.LoadedModule(name);
                        if (loaded != null && !c.Sw("Force")) { if (c.Sw("PassThru")) c.Out(PsModMgr.Info(loaded)); continue; }
                        file = R.FindModuleFile(name);
                        if (file == null)
                        {
                            var b = PsBuiltinModules.Make(name);
                            if (b == null || (b.Name == "ServerManager" && !R.M.IsServer)) { NotLoaded(c, name); continue; }
                            verbose("Loading module from path '" + b.Path + "'.");
                            if (loaded != null) R.LoadedModules.Remove(loaded);
                            R.EnsureModuleLoaded(b.Name);
                            var lm = R.LoadedModule(b.Name) ?? b;
                            foreach (var cmd in PsModMgr.CommandsOf(lm)) verbose("Importing cmdlet '" + cmd + "'.");
                            if (c.Sw("PassThru")) c.Out(PsModMgr.Info(lm));
                            continue;
                        }
                    }
                    try
                    {
                        string mname = file.Substring(file.LastIndexOf('\\') + 1); mname = mname.Substring(0, mname.LastIndexOf('.'));
                        if (isPath && R.LoadedModule(mname) != null && R.LoadedModule(mname).Path.Equals(file, StringComparison.OrdinalIgnoreCase) && !c.Sw("Force")) { if (c.Sw("PassThru")) c.Out(PsModMgr.Info(R.LoadedModule(mname))); continue; }
                        var m = PsModMgr.Import(R, file, c.St, verbose, c.Str("Prefix"));
                        if (c.Has("Function")) foreach (var k in m.Exported.Keys.ToList()) if (!c.Strs("Function").Any(p => PsConv.Like(k, p, false))) m.Exported.Remove(k);
                        if (c.Has("RequiredVersion") && !m.Version.Equals(c.Get("RequiredVersion")))
                        {
                            PsModMgr.Unload(R, m);
                            c.WriteError(c.MakeError("The specified module '" + name + "' with version '" + c.Str("RequiredVersion") + "' was not loaded because no valid module file was found in any module directory.", "Modules_ModuleWithVersionNotFound", "ResourceUnavailable", name, "System.IO.FileNotFoundException"));
                            continue;
                        }
                        if (c.Sw("PassThru")) c.Out(PsModMgr.Info(m));
                    }
                    catch (PsModMgr.ImportError e) { ImportFail(c, e, e.Category == "SecurityError" ? null : (object)name); }
                }
            }).FqClass = "Microsoft.PowerShell.Commands.ImportModuleCommand";
            Add("Remove-Module", Core, "Name:string[]@0 pipe; ModuleInfo:object; FullyQualifiedName:object[]; Force:switch", (c, o) =>
            {
                var R = c.R;
                var hit = new List<PsModule>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is PsModule) hit.Add(R.LoadedModule(((PsModule)((PsObj)PsConv.Base(o)).Base).Name));
                else foreach (var n in c.Strs("Name") ?? new string[0]) hit.AddRange(R.LoadedModules.Where(m => PsConv.Like(m.Name, n, false)));
                hit = hit.Where(x => x != null).Distinct().ToList();
                if (hit.Count == 0)
                {
                    var r = c.MakeError("No modules were removed. Verify that the specification of modules to remove is correct and those modules exist in the runspace.", "Modules_NoModulesRemoved", "ResourceUnavailable", null, "System.InvalidOperationException");
                    c.WriteError(r);
                    return;
                }
                foreach (var m in hit) if (c.ShouldProcess(m.Name, "Remove-Module")) PsModMgr.Unload(R, m);
            }).ShouldProcess = true;
            Add("Export-ModuleMember", Core, "Function:string[]@0; Cmdlet:string[]; Variable:string[]@1; Alias:string[]", (c, o) =>
            {
                PsModule m = null;
                for (var s = c.Scope; s != null && m == null; s = s.Parent) m = s.Module;
                if (m == null || m.BuiltIn)
                {
                    var r = c.MakeError("The Export-ModuleMember cmdlet can only be called from inside a module.", "Modules_CanOnlyExecuteExportModuleMemberInsideAModule", "PermissionDenied", null, "System.InvalidOperationException");
                    throw new PsRuntimeException(r, false);
                }
                if (m.ExportList == null) m.ExportList = new List<string>();
                if (c.Has("Function")) m.ExportList.AddRange(c.Strs("Function"));
                if (c.Has("Variable")) { if (m.VariableExportList == null) m.VariableExportList = new List<string>(); m.VariableExportList.AddRange(c.Strs("Variable")); }
                if (c.Has("Alias")) { if (m.AliasExportList == null) m.AliasExportList = new List<string>(); m.AliasExportList.AddRange(c.Strs("Alias")); }
            });
            Add("New-ModuleManifest", Core, "Path:string@0 !; RootModule:string alias=ModuleToProcess; ModuleVersion:version; Guid:guid; Author:string; CompanyName:string; Copyright:string; Description:string; PowerShellVersion:version; RequiredModules:object[]; NestedModules:object[]; FunctionsToExport:string[]; CmdletsToExport:string[]; VariablesToExport:string[]; AliasesToExport:string[]; Tags:string[]; ProjectUri:string; PassThru:switch", (c, o) =>
            {
                var R = c.R;
                string full = R.FullFsPath(c.Str("Path"), R.CurrentFsDir);
                if (!full.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    var r = c.MakeError("The module manifest file name '" + c.Str("Path") + "' is not valid: a module manifest must have the .psd1 extension.", "Modules_InvalidManifestExtension", "InvalidArgument", c.Str("Path"), "System.ArgumentException");
                    c.WriteError(r);
                    return;
                }
                string name = full.Substring(full.LastIndexOf('\\') + 1); name = name.Substring(0, name.Length - 5);
                string author = c.Str("Author") ?? R.User;
                Func<string, string[]> list = k => c.Has(k) ? c.Objs(k).Select(x => { var d = PsConv.Base(x) as IDictionary; return d != null ? PsConv.ToStr(PsModMgr.Key(d, "ModuleName")) : PsConv.ToStr(x); }).ToArray() : null;
                string text = PsModMgr.ManifestText(name, c.Str("RootModule"), c.Has("ModuleVersion") ? c.Str("ModuleVersion") : "1.0", c.Has("Guid") ? c.Str("Guid") : PsConv.ToStr(R.M.NewGuid()), author,
                    c.Str("CompanyName") ?? "Unknown", c.Str("Copyright") ?? "(c) " + R.M.Now.Year + " " + author + ". All rights reserved.", c.Str("Description"), c.Str("PowerShellVersion"),
                    list("RequiredModules"), c.Has("FunctionsToExport") ? c.Strs("FunctionsToExport") : null, c.Has("CmdletsToExport") ? c.Strs("CmdletsToExport") : null,
                    c.Has("VariablesToExport") ? c.Strs("VariablesToExport") : null, c.Has("AliasesToExport") ? c.Strs("AliasesToExport") : null, list("NestedModules"), R.M.Now);
                if (!c.ShouldProcess(full, "Create Module Manifest")) return;
                if (c.Sw("PassThru")) { c.Out(text); return; }
                string why = R.WriteFile(full, text, "unicode", false);
                if (why != null) c.WriteError(c.MakeError(why, "System.IO.DirectoryNotFoundException,Microsoft.PowerShell.Commands.NewModuleManifestCommand", "NotSpecified", null, "System.IO.DirectoryNotFoundException"));
            }).ShouldProcess = true;
            Add("Test-ModuleManifest", Core, "Path:string@0 ! pipe prop", (c, o) =>
            {
                var R = c.R;
                string full = R.FullFsPath(c.Str("Path"), R.CurrentFsDir);
                if (R.FsNode(full) == null)
                {
                    c.WriteError(c.MakeError("The module manifest '" + full + "' could not be found. Check the spelling of the path.", "Modules_ModuleManifestNotFound", "ObjectNotFound", full, "System.IO.FileNotFoundException"));
                    return;
                }
                string err;
                var h = PsModMgr.ReadData(R, full, out err);
                if (h == null)
                {
                    c.WriteError(c.MakeError("The module manifest '" + full + "' could not be processed because it is not a valid Windows PowerShell restricted language file. " + err, "Modules_InvalidManifest", "InvalidData", full, "System.Management.Automation.PSInvalidOperationException"));
                    return;
                }
                string dir = full.Substring(0, full.LastIndexOf('\\'));
                var root = PsConv.ToStr(PsModMgr.Key(h, "RootModule") ?? PsModMgr.Key(h, "ModuleToProcess") ?? "");
                if (PsModMgr.Key(h, "ModuleVersion") == null)
                {
                    c.WriteError(c.MakeError("The module manifest '" + full + "' does not contain a valid 'ModuleVersion' key. The ModuleVersion key is required in a module manifest.", "Modules_InvalidManifest", "InvalidData", full, "System.Management.Automation.PSInvalidOperationException"));
                    return;
                }
                if (root.Length > 0 && R.FsNode(R.FullFsPath(root, dir)) == null && R.FsNode(R.FullFsPath(root, dir) + ".psm1") == null)
                {
                    c.WriteError(c.MakeError("The module to process '" + root + "', listed in field 'ModuleToProcess/RootModule' of module manifest '" + full + "' was not processed because no valid module was found in any module directory.", "Modules_InvalidRootModuleInModuleManifest", "ResourceUnavailable", full, "System.IO.FileNotFoundException"));
                    return;
                }
                var d = PsModMgr.Describe(R, full);
                if (d != null) c.Out(PsModMgr.Info(d));
            });

            // ---------------------------------------------------- PowerShellGet: no internet on the practice network
            Add("Get-PSRepository", "PowerShellGet", "Name:string[]@0", (c, o) =>
            {
                foreach (var r in Repos(c.M).Where(x => MatchAny(c.Strs("Name"), x[0])))
                {
                    var x = new PsObj("Microsoft.PowerShell.Commands.PSRepository", "System.Object");
                    x.Val("Name", "string", r[0]); x.Val("InstallationPolicy", "string", r[1]); x.Val("SourceLocation", "string", r[2]);
                    x.Val("Trusted", "bool", r[1] == "Trusted"); x.Val("Registered", "bool", true); x.Val("PackageManagementProvider", "string", "NuGet");
                    x.Val("PublishLocation", "string", r[0] == "PSGallery" ? "https://www.powershellgallery.com/api/v2/package/" : r[2]);
                    x.ToStr = y => r[0];
                    c.Out(x);
                }
            });
            Add("Set-PSRepository", "PowerShellGet", "Name:string@0 !; InstallationPolicy:string set=Trusted|Untrusted; SourceLocation:string", (c, o) =>
            {
                var r = Repos(c.M).FirstOrDefault(x => x[0].Equals(c.Str("Name"), StringComparison.OrdinalIgnoreCase));
                if (r == null) { c.WriteError(c.MakeError("Unable to find repository '" + c.Str("Name") + "'. Use Get-PSRepository to see all available repositories.", "SourceNotFound,Set-PSRepository", "ObjectNotFound", c.Str("Name"), "System.Exception")); return; }
                if (c.Has("InstallationPolicy")) r[1] = c.Str("InstallationPolicy");
                if (c.Has("SourceLocation")) r[2] = c.Str("SourceLocation");
            });
            Add("Register-PSRepository", "PowerShellGet", "Name:string@0 !; SourceLocation:string@1 !; InstallationPolicy:string set=Trusted|Untrusted; PublishLocation:string", (c, o) =>
            {
                if (Repos(c.M).Any(x => x[0].Equals(c.Str("Name"), StringComparison.OrdinalIgnoreCase))) { c.WriteError(c.MakeError("The repository could not be registered because there exists a registered repository with Name '" + c.Str("Name") + "'.", "RepositoryAlreadyRegistered,Register-PSRepository", "InvalidArgument", c.Str("Name"), "System.ArgumentException")); return; }
                Repos(c.M).Add(new[] { c.Str("Name"), c.Str("InstallationPolicy", "Untrusted"), c.Str("SourceLocation") });
            });
            Add("Unregister-PSRepository", "PowerShellGet", "Name:string[]@0 !", (c, o) =>
            {
                foreach (var n in c.Strs("Name")) if (Repos(c.M).RemoveAll(x => x[0].Equals(n, StringComparison.OrdinalIgnoreCase)) == 0) c.WriteError(c.MakeError("Unable to find repository '" + n + "'. Use Get-PSRepository to see all available repositories.", "SourceNotFound,Unregister-PSRepository", "ObjectNotFound", n, "System.Exception"));
            });
            foreach (var verb in new[] { "Find", "Install", "Save", "Update" })
            {
                string v = verb;
                Add(v + "-Module", "PowerShellGet", "Name:string[]@0 pipe prop; Repository:string[]; RequiredVersion:string; MinimumVersion:string; MaximumVersion:string; Scope:string set=CurrentUser|AllUsers; Force:switch; AllowClobber:switch; SkipPublisherCheck:switch; Path:string; Credential:pscredential; Proxy:string; AcceptLicense:switch", (c, o) =>
                {
                    string name = c.Has("Name") ? string.Join(",", c.Strs("Name")) : "";
                    if (v == "Update" && c.Has("Name") && !c.Strs("Name").Any(n => c.R.FindModuleFile(n) != null))
                    {
                        c.WriteError(c.MakeError("Module '" + name + "' was not installed by using Install-Module, so it cannot be updated.", "ModuleNotInstalledUsingInstallModuleCmdlet,Update-Module", "InvalidOperation", name, "System.InvalidOperationException"));
                        return;
                    }
                    c.Warn("Unable to resolve package source 'https://www.powershellgallery.com/api/v2'.");
                    c.Warn("Cert Lab: the practice network has no internet, so the PowerShell Gallery cannot be reached. Copy a module folder into C:\\Program Files\\WindowsPowerShell\\Modules instead.");
                    string pkg = v == "Find" ? "Find-Package" : v == "Install" ? "Install-Package" : v == "Save" ? "Save-Package" : "Install-Package";
                    var r = c.MakeError("No match was found for the specified search criteria and module name '" + name + "'. Try Get-PSRepository to see all available registered module repositories.", "NoMatchFoundForCriteria,Microsoft.PowerShell.PackageManagement.Cmdlets." + pkg.Replace("-", "") + "Command", "ObjectNotFound", "Microsoft.Power....Cmdlets." + pkg.Replace("-", "") + ":" + pkg.Replace("-", ""), "System.Exception");
                    r.TargetName = "Microsoft.Power...." + pkg.Replace("-", "") + "Command"; r.TargetType = pkg.Replace("-", "") + "Command";
                    r.CommandName = "PackageManagement\\" + pkg; r.Activity = pkg; r.Reason = "Exception"; r.FullCommandName = "";
                    c.WriteError(r);
                });
            }
            Add("Get-InstalledModule", "PowerShellGet", "Name:string[]@0", (c, o) =>
            {
                if (c.Has("Name")) c.WriteError(c.MakeError("No match was found for the specified search criteria and module names '" + string.Join(",", c.Strs("Name")) + "'.", "NoMatchFound,Microsoft.PowerShell.PackageManagement.Cmdlets.GetPackage", "ObjectNotFound", null, "System.Exception"));
            });
            PsFormat.Table("Microsoft.PowerShell.Commands.PSRepository", PsFormat.P("Name", 25), PsFormat.P("InstallationPolicy", 20), PsFormat.P("SourceLocation", 0));
        }
        static void NotLoaded(CmdCtx c, string name)
        {
            var r = c.MakeError("The specified module '" + name + "' was not loaded because no valid module file was found in any module directory.", "Modules_ModuleNotFound", "ResourceUnavailable", name, "System.IO.FileNotFoundException");
            c.WriteError(r);
        }

        static List<string[]> Repos(PsMachine m) { return m.Repositories; }
    }

    public partial class PsMachine
    {
        /// <summary>The PowerShellGet repositories: name, installation policy, source location.</summary>
        public readonly List<string[]> Repositories = new List<string[]> { new[] { "PSGallery", "Untrusted", "https://www.powershellgallery.com/api/v2" } };
    }
}
