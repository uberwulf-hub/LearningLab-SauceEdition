// Cert Lab - core cmdlets, part 3: Get-Member, New-Object, Add-Member, Get-Date, New-TimeSpan, Start-Sleep,
// Get-Random, Measure-Command, Invoke-Expression, Read-Host, Clear-Host, history, variables and aliases.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static PsObj VarInfo(PsVariable v)
        {
            var o = new PsObj("System.Management.Automation.PSVariable", "System.Object");
            o.Val("Name", "string", v.Name); o.Val("Value", "System.Object", v.Value); o.Val("Description", "string", v.Description);
            o.Val("Options", "System.Management.Automation.ScopedItemOptions", v.Options); o.Val("Visibility", "System.Management.Automation.SessionStateEntryVisibility", "Public");
            o.DefaultDisplay = new[] { "Name", "Value" };
            o.Base = v;
            return o;
        }
        public static PsObj AliasInfo(string name, string target)
        {
            var d = Get(target);
            var o = new PsObj("System.Management.Automation.AliasInfo", "System.Management.Automation.CommandInfo", "System.Object");
            o.Val("CommandType", "System.Management.Automation.CommandTypes", PsEnums.V("System.Management.Automation.CommandTypes", "Alias"));
            o.Val("Name", "string", name);
            o.Val("DisplayName", "string", name + " -> " + target);
            o.Val("Definition", "string", target);
            o.Val("ReferencedCommand", "System.Management.Automation.CommandInfo", target);
            o.Val("ResolvedCommandName", "string", target);
            o.Val("Version", "version", null);
            o.Val("Source", "string", "");
            o.Val("Options", "string", "None");
            o.DefaultDisplay = new[] { "CommandType", "Name", "Version", "Source" };
            o.ToStr = x => name;
            return o;
        }

        static IEnumerable<PsVariable> AllVars(PsScope sc)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var s = sc; s != null; s = s.Parent) foreach (var v in s.Vars.Values) if (seen.Add(v.Name)) yield return v;
        }

        static partial void RegisterMore()
        {
            Add("Get-Member", Util, "Name:string[]@0; MemberType:string[] alias=Type; InputObject:psobject pipe; Static:switch; View:string; Force:switch", (c, o) =>
            {
                if (!c.HasInput && !c.Has("InputObject")) return;
                var x = c.HasInput ? o : c.Get("InputObject");
                var tn = PsMembers.TypeNames(x).FirstOrDefault() ?? "System.Object";
                if (x is PsObj && ((PsObj)x).IsCustom && !((PsObj)x).TypeName.StartsWith("Selected.", StringComparison.Ordinal)) tn = "System.Management.Automation.PSCustomObject";
                var seen = c.Data.ContainsKey("seen") ? (HashSet<string>)c.Data["seen"] : new HashSet<string>();
                c.Data["seen"] = seen;
                if (!seen.Add(tn)) return;
                var names = c.Strs("Name"); var types = c.Strs("MemberType");
                foreach (var m in PsMembers.ListMembers(x))
                {
                    if (names != null && !names.Any(n => PsConv.Like(m.Name, n, false))) continue;
                    if (types != null && !types.Any(t => m.MemberType.Equals(t, StringComparison.OrdinalIgnoreCase) || t.Equals("Properties", StringComparison.OrdinalIgnoreCase) && m.MemberType.EndsWith("Property", StringComparison.Ordinal) || t.Equals("Methods", StringComparison.OrdinalIgnoreCase) && m.MemberType.EndsWith("Method", StringComparison.Ordinal) || t.Equals("All", StringComparison.OrdinalIgnoreCase))) continue;
                    var d = new PsObj("Microsoft.PowerShell.Commands.MemberDefinition", "System.Object");
                    d.Val("TypeName", "string", tn); d.Val("Name", "string", m.Name);
                    d.Val("MemberType", "System.Management.Automation.PSMemberTypes", PsEnums.V("System.Management.Automation.PSMemberTypes", m.MemberType));
                    d.Val("Definition", "string", m.Definition);
                    d.DefaultDisplay = new[] { "Name", "MemberType", "Definition" };
                    c.Out(d);
                }
            }).End = c => { if (!c.Data.ContainsKey("seen")) throw c.Fail("You must specify an object for the Get-Member cmdlet.", "NoObjectInGetMember", "CloseError", null, "System.InvalidOperationException"); };
            Add("New-Object", Util, "TypeName:string@0; ArgumentList:object[]@1 alias=Args; Property:hashtable; ComObject:string", (c, o) =>
            {
                if (c.Has("ComObject")) throw c.Fail("COM objects reach the real computer, so Cert Lab's practice console does not create them.", "CertLabNotSimulated", "PermissionDenied", c.Str("ComObject"), null);
                string tn = c.Str("TypeName");
                if (tn == null) throw c.Fail("Cannot process command because of one or more missing mandatory parameters: TypeName.", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                var t = PsTypes.Resolve(tn);
                if (t == null) throw c.Fail("Cannot find type [" + tn + "]: verify that the assembly containing this type is loaded.", "TypeNotFound", "InvalidType", null, "System.Management.Automation.PSArgumentException");
                var args = c.Objs("ArgumentList").ToArray();
                object r;
                try { r = PsStatics.New(c.R, t, args, c.Ast); }
                catch (PsRuntimeException e) { throw c.Fail(e.Rec.Exception.Message.Replace("Cannot find an overload for", "Cannot find an overload for"), "ConstructorInvokedThrowException", "InvalidOperation", null, "System.Management.Automation.MethodException"); }
                var props = c.Hash("Property");
                if (props != null)
                {
                    var po = r as PsObj;
                    foreach (DictionaryEntry e in props)
                    {
                        if (po != null && (po.IsCustom || po.Has(PsConv.ToStr(e.Key)))) { if (po.Has(PsConv.ToStr(e.Key))) po.Set(PsConv.ToStr(e.Key), e.Value); else po.Add(PsConv.ToStr(e.Key), e.Value); }
                        else c.R.SetMember(r, PsConv.ToStr(e.Key), e.Value, c.Ast);
                    }
                }
                c.Out(r);
            });
            Add("Add-Member", Util, "MemberType:string@0 alias=Type; Name:string@1; Value:object@2; SecondValue:object@3; NotePropertyName:string; NotePropertyValue:object; NotePropertyMembers:hashtable; InputObject:psobject ! pipe; PassThru:switch; Force:switch; TypeName:string", (c, o) =>
            {
                var x = c.Get("InputObject");
                var target = x as PsObj ?? PsNotes.For(x);
                if (c.Has("TypeName")) target.TypeNames.Insert(0, c.Str("TypeName"));
                var adds = new List<KeyValuePair<string, object>>();
                string mt = c.Str("MemberType") ?? "NoteProperty";
                if (c.Has("NotePropertyName")) { adds.Add(new KeyValuePair<string, object>(c.Str("NotePropertyName"), c.Get("NotePropertyValue"))); mt = "NoteProperty"; }
                if (c.Has("NotePropertyMembers")) { foreach (DictionaryEntry e in c.Hash("NotePropertyMembers")) adds.Add(new KeyValuePair<string, object>(PsConv.ToStr(e.Key), e.Value)); mt = "NoteProperty"; }
                if (c.Has("Name")) adds.Add(new KeyValuePair<string, object>(c.Str("Name"), c.Get("Value")));
                foreach (var a in adds)
                {
                    if (target.Has(a.Key) && !c.Sw("Force")) { c.Error("Cannot add a member with the name \"" + a.Key + "\" because a member with that name already exists. To overwrite the member anyway, add the Force parameter to your command.", "MemberAlreadyExists", "InvalidOperation", x, "System.InvalidOperationException"); continue; }
                    switch (mt.ToLowerInvariant())
                    {
                        case "noteproperty": target.Add(a.Key, a.Value); break;
                        case "aliasproperty": target.Alias(a.Key, PsConv.ToStr(a.Value)); break;
                        case "scriptproperty": target.Put(new PsProp { Name = a.Key, Kind = "ScriptProperty", Script = a.Value as PsScriptBlock, SetScript = c.Get("SecondValue") as PsScriptBlock }); break;
                        case "scriptmethod": target.Methods[a.Key] = new PsMethod { Name = a.Key, Script = a.Value as PsScriptBlock, Definition = "System.Object " + a.Key + "();" }; break;
                        default: throw c.Fail("Cert Lab supports Add-Member with NoteProperty, AliasProperty, ScriptProperty and ScriptMethod.", "CertLabNotSimulated", "NotImplemented", null, null);
                    }
                }
                if (c.Sw("PassThru")) c.Out(x);
            });
            Add("Get-Date", Util, "Date:datetime@0 pipe alias=LastWriteTime; Year:int; Month:int; Day:int; Hour:int; Minute:int; Second:int; Millisecond:int; DisplayHint:string set=Date|Time|DateTime; Format:string; UFormat:string", (c, o) =>
            {
                var d = c.Has("Date") ? (DateTime)c.Get("Date") : c.M.Now;
                if (c.Has("Year")) d = new DateTime(c.Int("Year", 1), d.Month, Math.Min(d.Day, DateTime.DaysInMonth(c.Int("Year", 1), d.Month)), d.Hour, d.Minute, d.Second);
                if (c.Has("Month")) d = new DateTime(d.Year, c.Int("Month", 1), Math.Min(d.Day, DateTime.DaysInMonth(d.Year, c.Int("Month", 1))), d.Hour, d.Minute, d.Second);
                if (c.Has("Day")) d = new DateTime(d.Year, d.Month, c.Int("Day", 1), d.Hour, d.Minute, d.Second);
                if (c.Has("Hour")) d = d.Date.AddHours(c.Int("Hour", 0)).AddMinutes(d.Minute).AddSeconds(d.Second);
                if (c.Has("Minute")) d = d.Date.AddHours(d.Hour).AddMinutes(c.Int("Minute", 0)).AddSeconds(d.Second);
                if (c.Has("Second")) d = d.Date.AddHours(d.Hour).AddMinutes(d.Minute).AddSeconds(c.Int("Second", 0));
                if (c.Has("Format")) { c.Out(d.ToString(c.Str("Format"), PsConv.Us)); return; }
                if (c.Has("UFormat")) { c.Out(UFormat(d, c.Str("UFormat"))); return; }
                if (c.Has("DisplayHint") && c.Str("DisplayHint") != "DateTime")
                {
                    var hint = c.Str("DisplayHint");
                    c.Out(new PsObj("System.DateTime") { ToStr = x => hint.Equals("Date", StringComparison.OrdinalIgnoreCase) ? d.ToString("dddd, MMMM d, yyyy", PsConv.Us) : d.ToString("h:mm:ss tt", PsConv.Us), DefaultDisplay = new string[0], Base = d });
                    return;
                }
                c.Out(d);
            });
            Add("New-TimeSpan", Util, "Start:datetime@0 alias=LastWriteTime; End:datetime@1; Days:int; Hours:int; Minutes:int; Seconds:int", (c, o) =>
            {
                if (c.Has("Start") || c.Has("End"))
                {
                    var s = c.Has("Start") ? (DateTime)c.Get("Start") : c.M.Now;
                    var e = c.Has("End") ? (DateTime)c.Get("End") : c.M.Now;
                    c.Out(e - s); return;
                }
                c.Out(new TimeSpan(c.Int("Days", 0), c.Int("Hours", 0), c.Int("Minutes", 0), c.Int("Seconds", 0)));
            });
            Add("Start-Sleep", Util, "Seconds:double@0 alias=s; Milliseconds:int alias=ms", (c, o) =>
            {
                double secs = c.Has("Milliseconds") ? c.Int("Milliseconds", 0) / 1000.0 : (c.Has("Seconds") ? PsConv.ToDouble(c.Get("Seconds")) : 0);
                if (secs < 0) secs = 0;
                c.R.Sleep(secs);
            });
            Add("Get-Random", Util, "Maximum:object@0 alias=Max; Minimum:object alias=Min; InputObject:object[] pipe; Count:int; SetSeed:int", (c, o) => { if (c.HasInput) c.Items.Add(o); }).End = c =>
            {
                var rng = c.Has("SetSeed") ? new Random(c.Int("SetSeed", 0)) : c.M.Rng;
                if (c.Has("SetSeed")) c.M.Rng = rng;
                var pool = c.Items.Count > 0 ? c.Items : c.Objs("InputObject");
                if (pool.Count > 0)
                {
                    int n = Math.Min(c.Int("Count", 1), pool.Count);
                    var copy = pool.ToList();
                    for (int i = 0; i < n; i++) { int k = rng.Next(copy.Count); c.Out(copy[k]); copy.RemoveAt(k); }
                    return;
                }
                bool dbl = c.Get("Maximum") is double || c.Get("Minimum") is double;
                for (int i = 0; i < c.Int("Count", 1); i++)
                {
                    if (dbl) { double mn = c.Has("Minimum") ? PsConv.ToDouble(c.Get("Minimum")) : 0, mx = c.Has("Maximum") ? PsConv.ToDouble(c.Get("Maximum")) : double.MaxValue; c.Out(mn + rng.NextDouble() * (mx - mn)); continue; }
                    long lo = c.Has("Minimum") ? PsConv.ToLong(c.Get("Minimum")) : 0, hi = c.Has("Maximum") ? PsConv.ToLong(c.Get("Maximum")) : int.MaxValue;
                    if (hi <= lo) throw c.Fail("The Minimum value (" + lo + ") cannot be greater than or equal to the Maximum value (" + hi + ").", "MinGreaterThanOrEqualMax", "InvalidArgument", null, "System.ArgumentException");
                    c.Out((int)(lo + (long)(rng.NextDouble() * (hi - lo))));
                }
            };
            Add("Measure-Command", Util, "Expression:scriptblock@0 !; InputObject:psobject pipe", (c, o) =>
            {
                if (c.First && c.HasInput) return;
                var start = c.M.Now;
                long steps = c.R.Steps;
                c.R.InvokeBlock(c.Block("Expression"), c.Scope, c.HasInput ? o : null, null, false);
                var took = (c.M.Now - start).Add(TimeSpan.FromMilliseconds(3 + (c.R.Steps - steps) / 50.0));
                c.Out(took);
            });
            Add("Invoke-Expression", Util, "Command:string@0 ! pipe", (c, o) =>
            {
                var ast = PsParser.Parse(c.Str("Command") ?? "", "");
                c.R.ExecBlock(ast.EndBlock ?? ast.Body, c.Scope, c.St);
            });
            Add("Read-Host", Util, "Prompt:object@0 rest; AsSecureString:switch", (c, o) =>
            {
                string p = c.Has("Prompt") ? string.Join(" ", c.Objs("Prompt").Select(x => PsConv.ToStr(x)).ToArray()) + ": " : "";
                var s = c.Prompt(p, c.Sw("AsSecureString"));
                if (s == null) throw c.Fail("Read-Host needs someone to type an answer, and there is no console here (a lab check or a job).", "CertLabNoConsole", "InvalidOperation", null, "System.Management.Automation.PSInvalidOperationException");
                c.Out(c.Sw("AsSecureString") ? (object)new PsSecureString { Plain = s } : s);
            });
            Add("Clear-Host", Core, "", (c, o) => c.R.ClearHost());
            Add("Get-History", Core, "Id:long[]@0 pipe; Count:int@1", (c, o) =>
            {
                var l = c.R.History.Select((h, i) => new { h, id = i + 1 }).ToList();
                if (c.Has("Id")) l = l.Where(x => c.Objs("Id").Any(i => PsConv.ToLong(i) == x.id)).ToList();
                if (c.Has("Count")) l = l.Skip(Math.Max(0, l.Count - c.Int("Count", 32))).ToList();
                foreach (var x in l)
                {
                    var hi = new PsObj("Microsoft.PowerShell.Commands.HistoryInfo", "System.Object");
                    hi.Val("Id", "long", (long)x.id); hi.Val("CommandLine", "string", x.h.Key); hi.Val("ExecutionStatus", "System.Management.Automation.Runspaces.PipelineState", "Completed");
                    hi.Val("StartExecutionTime", "datetime", x.h.Value); hi.Val("EndExecutionTime", "datetime", x.h.Value);
                    hi.DefaultDisplay = new[] { "Id", "CommandLine" };
                    hi.ToStr = y => x.h.Key;
                    c.Out(hi);
                }
            });
            Add("Clear-History", Core, "Id:int[]@0; Count:int", (c, o) => c.R.History.Clear());
            Add("Get-Variable", Util, "Name:string[]@0 pipe; ValueOnly:switch; Include:string[]; Exclude:string[]; Scope:string", (c, o) =>
            {
                var names = c.Strs("Name");
                var vars = AllVars(c.Scope).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (names == null) { foreach (var v in vars) c.Out(c.Sw("ValueOnly") ? v.Value : VarInfo(v)); return; }
                foreach (var n in names)
                {
                    var hits = vars.Where(v => PsConv.Like(v.Name, n, false)).ToList();
                    if (hits.Count == 0 && !PsConv.HasWildcard(n)) { c.Error("Cannot find a variable with the name '" + n + "'.", "VariableNotFound", "ObjectNotFound", n, "System.Management.Automation.ItemNotFoundException"); continue; }
                    foreach (var v in hits) { if (c.Sw("ValueOnly")) PsRunspace.EmitValue(v.Value, c.Out); else c.Out(VarInfo(v)); }
                }
            });
            Add("Set-Variable", Util, "Name:string[]@0 !; Value:object@1 pipe; Option:string; Scope:string; PassThru:switch; Description:string; Force:switch", (c, o) =>
            {
                foreach (var n in c.Strs("Name"))
                {
                    var sc = c.R.ScopeFor(c.Str("Scope") ?? "", c.Scope) ?? c.Scope;
                    PsVariable v;
                    if (sc.Vars.TryGetValue(n, out v) && (v.ReadOnly && !c.Sw("Force") || v.Constant)) { c.Error("Cannot overwrite variable " + n + " because it is read-only or constant.", "VariableNotWritable", "WriteError", n, "System.Management.Automation.SessionStateUnauthorizedAccessException"); continue; }
                    if (v == null) { v = new PsVariable { Name = n }; sc.Vars[n] = v; }
                    if (c.Has("Value") || c.HasInput) v.Value = c.HasInput ? o : c.Get("Value");
                    if (c.Has("Description")) v.Description = c.Str("Description");
                    if (c.Has("Option")) { string op = c.Str("Option"); v.Options = op; v.ReadOnly = op.IndexOf("ReadOnly", StringComparison.OrdinalIgnoreCase) >= 0; v.Constant = op.IndexOf("Constant", StringComparison.OrdinalIgnoreCase) >= 0; v.Private = op.IndexOf("Private", StringComparison.OrdinalIgnoreCase) >= 0; }
                    if (c.Sw("PassThru")) c.Out(VarInfo(v));
                }
            });
            Add("New-Variable", Util, "Name:string@0 !; Value:object@1 pipe; Option:string; Scope:string; PassThru:switch; Description:string; Force:switch", (c, o) =>
            {
                string n = c.Str("Name");
                var sc = c.R.ScopeFor(c.Str("Scope") ?? "", c.Scope) ?? c.Scope;
                if (sc.Vars.ContainsKey(n) && !c.Sw("Force")) { c.Error("A variable with name '" + n + "' already exists.", "VariableAlreadyExists", "ResourceExists", n, "System.Management.Automation.SessionStateUnauthorizedAccessException"); return; }
                var v = new PsVariable { Name = n, Value = c.HasInput ? o : c.Get("Value"), Description = c.Str("Description") ?? "" };
                if (c.Has("Option")) { string op = c.Str("Option"); v.Options = op; v.ReadOnly = op.IndexOf("ReadOnly", StringComparison.OrdinalIgnoreCase) >= 0; v.Constant = op.IndexOf("Constant", StringComparison.OrdinalIgnoreCase) >= 0; }
                sc.Vars[n] = v;
                if (c.Sw("PassThru")) c.Out(VarInfo(v));
            });
            Add("Remove-Variable", Util, "Name:string[]@0 !; Scope:string; Force:switch", (c, o) =>
            {
                foreach (var n in c.Strs("Name"))
                {
                    bool found = false;
                    for (var s = c.Scope; s != null; s = s.Parent)
                    {
                        PsVariable v;
                        if (!s.Vars.TryGetValue(n, out v)) continue;
                        if (v.Constant || v.ReadOnly && !c.Sw("Force")) { c.Error("Cannot remove variable " + n + " because it is constant or read-only. If the variable is read-only, try the operation again specifying the Force option.", "VariableNotRemovable", "WriteError", n, "System.Management.Automation.SessionStateUnauthorizedAccessException"); found = true; break; }
                        s.Vars.Remove(n); found = true; break;
                    }
                    if (!found) c.Error("Cannot find a variable with the name '" + n + "'.", "VariableNotFound", "ObjectNotFound", n, "System.Management.Automation.ItemNotFoundException");
                }
            });
            Add("Clear-Variable", Util, "Name:string[]@0 !; Scope:string; Force:switch", (c, o) => { foreach (var n in c.Strs("Name")) { var v = c.R.FindVar(n, c.Scope); if (v != null) v.Value = null; } });
            Add("Get-Alias", Util, "Name:string[]@0; Definition:string[]; Exclude:string[]; Scope:string", (c, o) =>
            {
                var names = c.Strs("Name"); var defs = c.Strs("Definition");
                var all = c.R.Aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase).ToList();
                if (names != null)
                    foreach (var n in names)
                    {
                        var hits = all.Where(a => PsConv.Like(a.Key, n, false)).ToList();
                        if (hits.Count == 0 && !PsConv.HasWildcard(n)) c.Error("This command cannot find a matching alias because an alias with the name '" + n + "' does not exist.", "ItemNotFoundException", "ObjectNotFound", n, "System.Management.Automation.ItemNotFoundException");
                        foreach (var a in hits) c.Out(AliasInfo(a.Key, a.Value));
                    }
                else foreach (var a in all) if (defs == null || defs.Any(d => PsConv.Like(a.Value, d, false))) c.Out(AliasInfo(a.Key, a.Value));
            });
            Action<CmdCtx, object> setAlias = (c, o) =>
            {
                string n = c.Str("Name"), v = c.Str("Value");
                if (c.Def.Name == "New-Alias" && c.R.Aliases.ContainsKey(n) && !c.Sw("Force")) { c.Error("The alias is not allowed, because an alias with the name '" + n + "' already exists.", "AliasAlreadyExists", "ResourceExists", n, "System.Management.Automation.SessionStateException"); return; }
                c.R.Aliases[n] = v;
                if (c.Has("Description")) c.R.AliasDescriptions[n] = c.Str("Description");
                if (c.Sw("PassThru")) c.Out(AliasInfo(n, v));
            };
            Add("Set-Alias", Util, "Name:string@0 !; Value:string@1 !; Description:string; Option:string; PassThru:switch; Scope:string; Force:switch", setAlias);
            Add("New-Alias", Util, "Name:string@0 !; Value:string@1 !; Description:string; Option:string; PassThru:switch; Scope:string; Force:switch", setAlias);
            Add("Get-Host", Util, "", (c, o) => c.Out(c.R.GetVar("Host", c.R.Global)));
            Add("Get-Culture", Util, "", (c, o) =>
            {
                var o2 = new PsObj("System.Globalization.CultureInfo", "System.Object");
                o2.Val("LCID", "int", 1033); o2.Val("Name", "string", "en-US"); o2.Val("DisplayName", "string", "English (United States)");
                o2.DefaultDisplay = new[] { "LCID", "Name", "DisplayName" }; o2.ToStr = x => "en-US";
                c.Out(o2);
            });
            Add("Set-StrictMode", Core, "Version:string alias=v; Off:switch", (c, o) => c.R.SetVar("__StrictMode", !c.Sw("Off"), c.Scope));
            Add("Get-Verb", Util, "Verb:string[]@0", (c, o) =>
            {
                var verbs = "Add|Common,Clear|Common,Close|Common,Copy|Common,Enter|Common,Exit|Common,Find|Common,Format|Common,Get|Common,Hide|Common,Join|Common,Lock|Common,Move|Common,New|Common,Open|Common,Optimize|Common,Pop|Common,Push|Common,Redo|Common,Remove|Common,Rename|Common,Reset|Common,Resize|Common,Search|Common,Select|Common,Set|Common,Show|Common,Skip|Common,Split|Common,Step|Common,Switch|Common,Undo|Common,Unlock|Common,Watch|Common,Connect|Communications,Disconnect|Communications,Read|Communications,Receive|Communications,Send|Communications,Write|Communications,Backup|Data,Checkpoint|Data,Compare|Data,Compress|Data,Convert|Data,ConvertFrom|Data,ConvertTo|Data,Dismount|Data,Edit|Data,Expand|Data,Export|Data,Group|Data,Import|Data,Initialize|Data,Limit|Data,Merge|Data,Mount|Data,Out|Data,Publish|Data,Restore|Data,Save|Data,Sync|Data,Unpublish|Data,Update|Data,Debug|Diagnostic,Measure|Diagnostic,Ping|Diagnostic,Repair|Diagnostic,Resolve|Diagnostic,Test|Diagnostic,Trace|Diagnostic,Approve|Lifecycle,Assert|Lifecycle,Complete|Lifecycle,Confirm|Lifecycle,Deny|Lifecycle,Disable|Lifecycle,Enable|Lifecycle,Install|Lifecycle,Invoke|Lifecycle,Register|Lifecycle,Request|Lifecycle,Restart|Lifecycle,Resume|Lifecycle,Start|Lifecycle,Stop|Lifecycle,Submit|Lifecycle,Suspend|Lifecycle,Uninstall|Lifecycle,Unregister|Lifecycle,Wait|Lifecycle,Block|Security,Grant|Security,Protect|Security,Revoke|Security,Unblock|Security,Unprotect|Security,Use|Other";
                var want = c.Strs("Verb");
                foreach (var v in verbs.Split(','))
                {
                    var p = v.Split('|');
                    if (want != null && !want.Any(w => PsConv.Like(p[0], w, false))) continue;
                    var vo = new PsObj("System.Management.Automation.VerbInfo", "System.Object");
                    vo.Val("Verb", "string", p[0]); vo.Val("Group", "string", p[1]);
                    c.Out(vo);
                }
            });
        }

        static string UFormat(DateTime d, string f)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] != '%' || i + 1 >= f.Length) { sb.Append(f[i]); continue; }
                char k = f[++i];
                switch (k)
                {
                    case 'Y': sb.Append(d.ToString("yyyy", PsConv.Us)); break;
                    case 'y': sb.Append(d.ToString("yy", PsConv.Us)); break;
                    case 'm': sb.Append(d.ToString("MM", PsConv.Us)); break;
                    case 'd': sb.Append(d.ToString("dd", PsConv.Us)); break;
                    case 'H': sb.Append(d.ToString("HH", PsConv.Us)); break;
                    case 'M': sb.Append(d.ToString("mm", PsConv.Us)); break;
                    case 'S': sb.Append(d.ToString("ss", PsConv.Us)); break;
                    case 'A': sb.Append(d.ToString("dddd", PsConv.Us)); break;
                    case 'a': sb.Append(d.ToString("ddd", PsConv.Us)); break;
                    case 'B': sb.Append(d.ToString("MMMM", PsConv.Us)); break;
                    case 'b': sb.Append(d.ToString("MMM", PsConv.Us)); break;
                    case 'j': sb.Append(d.DayOfYear.ToString("000")); break;
                    case 's': sb.Append(((long)(d - new DateTime(1970, 1, 1)).TotalSeconds).ToString(CultureInfo.InvariantCulture)); break;
                    case 'F': sb.Append(d.ToString("yyyy-MM-dd", PsConv.Us)); break;
                    case 'T': sb.Append(d.ToString("HH:mm:ss", PsConv.Us)); break;
                    case 'u': sb.Append(((int)d.DayOfWeek == 0 ? 7 : (int)d.DayOfWeek).ToString()); break;
                    default: sb.Append('%').Append(k); break;
                }
            }
            return sb.ToString();
        }
    }

    public partial class PsRunspace
    {
        /// <summary>Start-Sleep: the clock moves on (jobs and scheduled tasks run on the way).</summary>
        public void Sleep(double secs)
        {
            if (SleepHook != null) { SleepHook(secs); return; }
            M.Net.Advance((long)Math.Ceiling(secs));
        }
        public Action<double> SleepHook;   // jobs keep their own time
    }
}
