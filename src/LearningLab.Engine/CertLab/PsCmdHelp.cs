// Cert Lab - Get-Command, Get-Help, Update-Help and Save-Help in the simulated PowerShell.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        public static PsObj CommandObj(PsRunspace R, PsCommandInfo ci)
        {
            string kind = ci.Kind;
            var o = new PsObj(kind == "Cmdlet" ? "System.Management.Automation.CmdletInfo" : kind == "Alias" ? "System.Management.Automation.AliasInfo" : kind == "ExternalScript" ? "System.Management.Automation.ExternalScriptInfo" : kind == "Application" ? "System.Management.Automation.ApplicationInfo" : "System.Management.Automation.FunctionInfo", "System.Management.Automation.CommandInfo", "System.Object");
            o.Val("CommandType", "System.Management.Automation.CommandTypes", PsEnums.V("System.Management.Automation.CommandTypes", kind));
            o.Val("Name", "string", ci.Name);
            o.Val("DisplayName", "string", kind == "Alias" ? ci.Name + " -> " + ci.AliasTarget : ci.Name);
            string ver = kind == "Cmdlet" ? R.ModuleVersion(ci.Module) : kind == "Application" ? "10.0." + R.M.OsBuild + ".1" : "";
            o.Val("Version", "version", ver.Length > 0 ? new Version(ver) : null);
            o.Val("Source", "string", kind == "Application" || kind == "ExternalScript" ? ci.Path : kind == "Alias" ? "" : ci.Module ?? "");
            o.Val("ModuleName", "string", ci.Module ?? "");
            o.Val("Module", "psmoduleinfo", ci.Module ?? "");
            o.Val("Definition", "string", kind == "Cmdlet" ? "\n" + string.Join("\n\n", PsHelp.Syntax(ci.Def).ToArray()) + "\n" : ci.Definition);
            if (ci.Def != null) { o.Val("Verb", "string", ci.Def.Verb); o.Val("Noun", "string", ci.Def.Noun); o.Val("Parameters", "System.Collections.Generic.Dictionary[string,System.Management.Automation.ParameterMetadata]", ParamTable(ci.Def.Params.Concat(PsBinder.CommonDefs(ci.Def.ShouldProcess)))); }
            if (ci.Func != null) { o.Val("Verb", "string", ci.Func.Verb); o.Val("Noun", "string", ci.Func.Noun); o.Val("ScriptBlock", "scriptblock", new PsScriptBlock { Ast = ci.Func.Ast }); }
            if (ci.Path != null) o.Val("Path", "string", ci.Path);
            if (kind == "Alias") { o.Val("ResolvedCommandName", "string", ci.AliasTarget); o.Val("ReferencedCommand", "System.Management.Automation.CommandInfo", ci.AliasTarget); }
            o.DefaultDisplay = new[] { "CommandType", "Name", "Version", "Source" };
            o.Base = ci;
            o.ToStr = x => kind == "Alias" ? ci.Name : ci.Name;
            return o;
        }
        static System.Collections.Specialized.OrderedDictionary ParamTable(IEnumerable<ParamDef> ps)
        {
            var h = PsConv.NewOrdered();
            foreach (var p in ps) h[p.Name] = p.Name;
            return h;
        }

        static IEnumerable<PsCommandInfo> AllCommands(PsRunspace R)
        {
            var l = new List<PsCommandInfo>();
            foreach (var a in R.Aliases.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) { var ci = R.Resolve(a.Key, R.Global, false); if (ci != null) l.Add(ci); }
            var funcs = new Dictionary<string, PsFunction>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in R.LoadedModules) foreach (var f in m.Exported.Values) funcs[f.Name] = f;
            foreach (var f in R.Global.Funcs.Values) funcs[f.Name] = f;
            foreach (var f in funcs.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) l.Add(new PsCommandInfo { Kind = f.IsConfiguration ? "Configuration" : f.Filter ? "Filter" : "Function", Name = f.Name, Func = f, Module = f.Module == null ? "" : f.Module.Name, Version = f.Module == null ? "" : f.Module.Version.ToString(), Definition = f.Definition });
            foreach (var d in All.Values.Where(x => x.Available == null || x.Available(R.M)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) l.Add(new PsCommandInfo { Kind = d.IsFunction ? "Function" : "Cmdlet", Name = d.Name, Def = d, Module = d.Module, Version = R.ModuleVersion(d.Module) });
            return l;
        }

        static partial void RegisterHelp()
        {
            Add("Get-Command", Core, "Name:string[]@0 pipe; Verb:string[]; Noun:string[]; Module:string[] alias=PSSnapin; CommandType:CommandTypes alias=Type; Syntax:switch; TotalCount:int; ListImported:switch; ParameterName:string[]; All:switch; ArgumentList:object[]@1", (c, o) =>
            {
                var names = c.Strs("Name");
                IEnumerable<PsCommandInfo> found;
                if (names != null)
                {
                    var l = new List<PsCommandInfo>();
                    foreach (var n in names)
                    {
                        if (PsConv.HasWildcard(n)) { l.AddRange(AllCommands(c.R).Where(x => PsConv.Like(x.Name, n, false))); continue; }
                        var ci = c.R.Resolve(n, c.Scope, true);   // an exact name loads the module that has it, as in 5.1
                        if (ci == null)
                        {
                            var r = c.MakeError(c.R.CommandNotFoundText(n), "CommandNotFoundException", "ObjectNotFound", n, "System.Management.Automation.CommandNotFoundException");
                            c.WriteError(r); continue;
                        }
                        l.Add(ci);
                    }
                    found = l;
                }
                else found = AllCommands(c.R);
                if (names == null && c.Has("CommandType") && (((PsEnum)c.Get("CommandType")).Value & 32) != 0)
                {
                    // programs are listed only when asked for (-CommandType Application or All), as in 5.1
                    var apps = PsNatives.All.Select(n => c.R.Resolve(n.Name, c.Scope, false)).Where(x => x != null && x.Kind == "Application").GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    found = found.Concat(apps).ToList();
                }
                if (c.Has("Verb")) found = found.Where(x => x.Kind != "Alias" && x.Def != null && c.Strs("Verb").Any(v => PsConv.Like(x.Def.Verb, v, false)) || x.Func != null && c.Strs("Verb").Any(v => PsConv.Like(x.Func.Verb, v, false)));
                if (c.Has("Noun")) found = found.Where(x => x.Kind != "Alias" && x.Def != null && c.Strs("Noun").Any(v => PsConv.Like(x.Def.Noun, v, false)) || x.Func != null && c.Strs("Noun").Any(v => PsConv.Like(x.Func.Noun, v, false)));
                if (c.Has("Module")) found = found.Where(x => c.Strs("Module").Any(m => PsConv.Like(x.Module ?? "", m, false)));
                if (c.Has("CommandType")) { var ct = (PsEnum)c.Get("CommandType"); found = found.Where(x => { var e = PsEnums.V("System.Management.Automation.CommandTypes", x.Kind); return e != null && (e.Value & ct.Value) != 0; }); }
                if (c.Has("ParameterName")) found = found.Where(x => x.Def != null && c.Strs("ParameterName").Any(pn => x.Def.Params.Any(p => PsConv.Like(p.Name, pn, false))));
                if (c.Has("TotalCount")) found = found.Take(c.Int("TotalCount", 0));
                foreach (var ci in found)
                {
                    if (c.Sw("Syntax"))
                    {
                        if (ci.Kind == "Alias") { c.Out(ci.AliasTarget); continue; }
                        if (ci.Def != null) { foreach (var s in PsHelp.Syntax(ci.Def)) c.Out("\n" + s + "\n"); continue; }
                        if (ci.Func != null) { c.Out("\n" + PsHelp.FunctionSyntax(ci.Func) + "\n"); continue; }
                        c.Out(ci.Path); continue;
                    }
                    c.Out(CommandObj(c.R, ci));
                }
            });
            Add("Get-Help", Core, "Name:string@0 pipe; Path:string; Category:string[]; Component:string[]; Functionality:string[]; Role:string[]; Detailed:switch; Full:switch; Examples:switch; Parameter:string; Online:switch; ShowWindow:switch", (c, o) =>
            {
                string name = c.Str("Name");
                if (name == null) { c.Out(PsHelp.Raw("default") ?? "Get-Help NAME shows help for a command, for example Get-Help Get-Service."); return; }
                if (c.Sw("Online")) { c.Warn("The practice machines have no internet and no browser: Get-Help -Online would open docs.microsoft.com on a real PC. The help is shown here instead."); }
                if (c.Sw("ShowWindow")) throw c.Fail("Get-Help -ShowWindow opens a window; Cert Lab's console shows the help as text instead (leave out -ShowWindow).", "CertLabNotSimulated", "NotImplemented", null, null);
                if (PsConv.HasWildcard(name) || (c.R.Resolve(name, c.Scope, false) == null && PsHelp.Raw(name) == null))
                {
                    var hits = new List<PsObj>();
                    foreach (var ci in AllCommands(c.R).Where(x => x.Kind != "Alias" && (PsConv.HasWildcard(name) ? PsConv.Like(x.Name, name, false) : x.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        var page = PsHelp.Page(ci.Name);
                        var h = new PsObj("HelpInfoShort", "System.Object");
                        h.Val("Name", "string", ci.Name); h.Val("Category", "string", ci.Kind); h.Val("Module", "string", ci.Module ?? ""); h.Val("Synopsis", "string", page == null ? "" : page.Synopsis);
                        hits.Add(h);
                    }
                    foreach (var a in PsHelp.Names.Where(n => n.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && (PsConv.HasWildcard(name) ? PsConv.Like(n, name, false) : n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(n => n))
                    {
                        var h = new PsObj("HelpInfoShort", "System.Object");
                        h.Val("Name", "string", a); h.Val("Category", "string", "HelpFile"); h.Val("Module", "string", ""); h.Val("Synopsis", "string", AboutSynopsis(a));
                        hits.Add(h);
                    }
                    if (hits.Count == 1 && !PsConv.HasWildcard(name)) { c.Out(PsHelp.Render(c.R, PsConv.ToStr(hits[0].Get("Name")), null, null)); return; }
                    if (hits.Count == 0) throw c.Fail("Get-Help could not find " + name + " in a help file in this session. To download updated help topics type: \"Update-Help\". To get help online, search for the help topic in the TechNet library at https:/go.microsoft.com/fwlink/?LinkID=107116.", "HelpNotFound", "ResourceUnavailable", name, "Microsoft.PowerShell.Commands.HelpNotFoundException");
                    foreach (var h in hits) c.Out(h);
                    return;
                }
                string mode = c.Sw("Full") ? "full" : c.Sw("Detailed") ? "detailed" : c.Sw("Examples") ? "examples" : null;
                c.Out(PsHelp.Render(c.R, name, mode, c.Str("Parameter")));
            });
            Add("Update-Help", Core, "Module:string[]@0; SourcePath:string[]; Force:switch; UICulture:string[]; Recurse:switch", (c, o) =>
                c.Warn("The practice machines have no internet. Cert Lab's help pages are already complete, so there is nothing to update. (On a real PC, Update-Help downloads the newest help files and needs an administrator.)"));
            Add("Save-Help", Core, "DestinationPath:string[]@0 !; Module:string[]@1; Force:switch; UICulture:string[]", (c, o) =>
                c.Warn("The practice machines have no internet, so Save-Help cannot download help files here."));
            AutoTable("HelpInfoShort", P("Name", 0), P("Category", 0), P("Module", 0), P("Synopsis", 0));
        }
        static void AutoTable(string t, params FmtCol[] cols) { PsFormat.AutoTable(t, cols); }
        static FmtCol P(string n, int w) { return PsFormat.P(n, w); }

        static string AboutSynopsis(string name)
        {
            var t = PsHelp.Raw(name);
            if (t == null) return "";
            var lines = t.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++) if (lines[i].Trim().Equals("SHORT DESCRIPTION", StringComparison.OrdinalIgnoreCase)) return lines[i + 1].Trim();
            return "";
        }
    }
}
