// Cert Lab - core cmdlets of the simulated PowerShell, part 1: the Write-* cmdlets, ForEach-Object, Where-Object.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        public const string Core = "Microsoft.PowerShell.Core", Util = "Microsoft.PowerShell.Utility", Mgmt = "Microsoft.PowerShell.Management";

        static partial void RegisterCore()
        {
            Add("Write-Output", Util, "InputObject:psobject[]@0 pipe rest; NoEnumerate:switch", (c, o) =>
            {
                if (c.HasInput) { c.Out(o); return; }
                var v = c.Get("InputObject");
                if (v == null) return;
                if (c.Sw("NoEnumerate")) { c.Out(v); return; }
                foreach (var x in PsConv.Items(v)) c.Out(x);
            });
            Add("Write-Host", Util, "Object:object@0 pipe rest; NoNewline:switch; Separator:object; ForegroundColor:ConsoleColor; BackgroundColor:ConsoleColor", (c, o) =>
            {
                string sep = c.Has("Separator") ? PsConv.ToStr(c.Get("Separator")) : " ";
                var v = c.HasInput ? o : c.Get("Object");
                var parts = new List<string>();
                foreach (var x in PsConv.Items(v)) foreach (var y in PsConv.Items(x)) parts.Add(PsConv.ToStr(y));
                c.St.Info(new PsHostText { Text = string.Join(sep, parts.ToArray()), NoNewline = c.Sw("NoNewline") });
            });
            Add("Write-Error", Util, "Message:string@0 pipe alias=Msg; Category:string; ErrorId:string; TargetObject:object; Exception:object; ErrorRecord:object; RecommendedAction:string; CategoryActivity:string alias=Activity; CategoryReason:string alias=Reason; CategoryTargetName:string alias=TargetName; CategoryTargetType:string alias=TargetType", (c, o) =>
            {
                var given = c.Get("ErrorRecord") as PsErrorRecord;
                if (given != null) { c.WriteError(given); return; }
                var ex = c.Get("Exception") as PsException;
                string msg = c.Str("Message") ?? (ex != null ? ex.Message : "");
                var r = c.R.MakeError(null, msg, c.Str("ErrorId") ?? "Microsoft.PowerShell.Commands.WriteErrorException", c.Str("Category") ?? "NotSpecified", ex != null ? ex.TypeFullName : "Microsoft.PowerShell.Commands.WriteErrorException", c.Get("TargetObject"));
                if (ex != null) r.Exception = ex;
                r.Activity = c.Str("CategoryActivity") ?? "Write-Error";
                if (c.Has("CategoryReason")) r.Reason = c.Str("CategoryReason");
                if (c.Has("CategoryTargetName")) r.TargetName = c.Str("CategoryTargetName");
                if (c.Has("CategoryTargetType")) r.TargetType = c.Str("CategoryTargetType");
                // the error belongs to the function or script that called Write-Error
                PsScope owner = c.Scope;
                while (owner != null && owner.Command == null) owner = owner.Parent;
                if (owner != null && owner.Command != null && owner.Command.Name.Length > 0)
                {
                    r.CommandName = owner.Command.Name; r.FullCommandName = owner.Command.Name;
                    c.R.Locate(r, owner.CallAt);
                }
                else
                {
                    r.CommandName = c.Ast == null ? "Write-Error" : c.Ast.Extent; r.FullCommandName = "";
                    r.NoPosition = true;
                }
                c.WriteError(r);
            });
            Add("Write-Warning", Util, "Message:string@0 ! pipe alias=Msg", (c, o) => c.Warn(c.Str("Message") ?? ""));
            Add("Write-Verbose", Util, "Message:string@0 ! pipe alias=Msg", (c, o) => c.Verbose(c.Str("Message") ?? ""));
            Add("Write-Debug", Util, "Message:string@0 ! pipe alias=Msg", (c, o) => c.Debug(c.Str("Message") ?? ""));
            Add("Write-Information", Util, "MessageData:object@0 ! alias=Msg; Tags:string[]@1", (c, o) =>
            {
                string pref = c.InformationAction ?? c.R.Pref(c.Scope, "InformationPreference");
                if (pref == "Continue") c.St.Info(new PsHostText { Text = PsConv.ToStr(c.Get("MessageData")) });
            });
            Add("Write-Progress", Util, "Activity:string@0; Status:string@1; Id:int@2; PercentComplete:int; SecondsRemaining:int; CurrentOperation:string; ParentId:int; Completed:switch", (c, o) => { });

            Add("ForEach-Object", Core, "Process:object[]@0 rest; Begin:scriptblock; End:scriptblock; InputObject:psobject pipe; ArgumentList:object[] alias=Args; MemberName:string", c =>
            {
                var blocks = c.Objs("Process");
                if (blocks.Count == 1 && !(PsConv.Base(blocks[0]) is PsScriptBlock)) { c.Data["member"] = PsConv.ToStr(blocks[0]); blocks.Clear(); }
                if (c.Has("MemberName")) c.Data["member"] = c.Str("MemberName");
                var sbs = blocks.Select(b => PsConv.Base(b) as PsScriptBlock).ToList();
                if (sbs.Any(b => b == null)) throw c.Fail("Cannot bind parameter 'Process'. Cannot convert the value to type \"System.Management.Automation.ScriptBlock\".", "CannotConvertArgument", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                PsScriptBlock begin = c.Block("Begin"), proc = null, end = c.Block("End");
                if (sbs.Count == 1) proc = sbs[0];
                else if (sbs.Count == 2) { begin = begin ?? sbs[0]; proc = sbs[1]; }
                else if (sbs.Count >= 3) { begin = begin ?? sbs[0]; proc = sbs[1]; end = end ?? sbs[2]; }
                c.Data["proc"] = proc; c.Data["end"] = end;
                if (begin != null) c.R.InvokeBlockTo(begin, c.Scope, null, null, true, null, c.St);
            }, (c, o) =>
            {
                object member;
                var x = c.HasInput ? o : c.Get("InputObject");
                if (c.Data.TryGetValue("member", out member))
                {
                    string n = (string)member;
                    if (x == null) return;
                    var args = c.Objs("ArgumentList").ToArray();
                    if (args.Length > 0 || PsMembers.ClrHasMethod(x, n) && !(x is PsObj && ((PsObj)x).Has(n)) || x is PsObj && ((PsObj)x).Methods.ContainsKey(n) && !((PsObj)x).Has(n))
                        PsRunspace.EmitValue(c.R.CallMethod(x, n, args, c.Ast, c.Scope), c.St.Out);
                    else PsRunspace.EmitValue(c.R.GetMember(x, n, c.Ast), c.St.Out);
                    return;
                }
                var proc = c.Data["proc"] as PsScriptBlock;
                if (proc != null) c.R.InvokeBlockTo(proc, c.Scope, x, c.Objs("ArgumentList").ToArray(), true, null, c.St);
            }, c =>
            {
                object e;
                if (c.Data.TryGetValue("end", out e) && e != null) c.R.InvokeBlockTo((PsScriptBlock)e, c.Scope, null, null, true, null, c.St);
            });

            var whereOps = new[] { "EQ", "NE", "GT", "GE", "LT", "LE", "Like", "NotLike", "Match", "NotMatch", "Contains", "NotContains", "In", "NotIn", "Is", "IsNot", "CEQ", "CNE", "CLike", "CMatch", "CContains", "CIn" };
            Add("Where-Object", Core, "FilterScript:object@0 alias=Property; Value:object@1; InputObject:psobject pipe; " + string.Join("; ", whereOps.Select(w => w + ":switch").ToArray()), (c, o) =>
            {
                var x = c.HasInput ? o : c.Get("InputObject");
                var f = c.Get("FilterScript");
                var sb = f as PsScriptBlock;
                bool keep;
                if (sb != null) keep = PsConv.ToBool(c.R.InvokeBlockValue(sb, c.Scope, x, null, true));
                else
                {
                    string prop = PsConv.ToStr(f);
                    var pv = x == null ? null : c.R.GetMember(x, prop, c.Ast);
                    string op = whereOps.FirstOrDefault(w => c.Sw(w));
                    if (op == null) keep = PsConv.ToBool(pv);
                    else
                    {
                        var val = c.Get("Value");
                        string psop = op.ToLowerInvariant();
                        if (psop == "is" || psop == "isnot")
                        {
                            var t = PsTypes.Resolve(val is PsTypeInfo ? ((PsTypeInfo)val).FullName : PsConv.ToStr(val));
                            bool isit = t != null && PsTypes.IsInstance(pv, t);
                            keep = psop == "is" ? isit : !isit;
                        }
                        else keep = PsConv.ToBool(c.R.Binary(psop, pv, val, c.Ast, c.Scope));
                    }
                }
                if (keep) c.Out(x);
            });
        }
    }
}
