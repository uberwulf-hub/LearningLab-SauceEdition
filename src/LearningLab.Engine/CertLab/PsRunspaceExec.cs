// Cert Lab - running script blocks from cmdlets (ForEach-Object, Where-Object, calculated properties...), the
// $MyInvocation and $PSCmdlet objects of functions, prompts (choices, credentials) and small session helpers.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsRunspace
    {
        /// <summary>Runs a script block with $_ set; dotSource runs it in the caller's scope (as ForEach-Object does).</summary>
        public List<object> InvokeBlock(PsScriptBlock sb, PsScope sc, object dollarUnder, object[] args, bool dotSource) { return InvokeBlock(sb, sc, dollarUnder, args, dotSource, null); }
        public List<object> InvokeBlock(PsScriptBlock sb, PsScope sc, object dollarUnder, object[] args, bool dotSource, object thisObj)
        {
            var outp = new List<object>();
            InvokeBlockTo(sb, sc, dollarUnder, args, dotSource, thisObj, CurrentStreams.WithOut(o => outp.Add(o)));
            return outp;
        }
        public Streams CurrentStreams = new Streams();

        public void InvokeBlockTo(PsScriptBlock sb, PsScope sc, object dollarUnder, object[] args, bool dotSource, object thisObj, Streams st)
        {
            var parent = sb.Home ?? sc;
            var fs = dotSource ? sc : new PsScope(this, parent, "local") { Module = sb.Module ?? parent.Module };
            PsVariable oldUnder = null, oldThis = null;
            if (dotSource) { fs.Vars.TryGetValue("_", out oldUnder); fs.Vars.TryGetValue("this", out oldThis); }
            fs.Vars["_"] = new PsVariable { Name = "_", Value = dollarUnder };
            if (thisObj != null) fs.Vars["this"] = new PsVariable { Name = "this", Value = thisObj };
            var a = args ?? new object[0];
            int used = 0;
            if (sb.Ast.Params != null)
            {
                foreach (var p in sb.Ast.Params)
                {
                    object v = used < a.Length ? a[used++] : (p.Default != null ? Eval(p.Default, fs, st) : null);
                    if (p.Type != null && v != null) { var t = PsTypes.Resolve(p.Type); if (t != null) v = PsConv.To(v, t); }
                    fs.Vars[p.Name] = new PsVariable { Name = p.Name, Value = v };
                }
            }
            if (!dotSource) fs.Vars["args"] = new PsVariable { Name = "args", Value = a.Skip(used).ToArray() };
            CallDepth++;
            try
            {
                if (CallDepth > 100) throw RtError(sb.Ast, "The script failed due to call depth overflow.", "CallDepthOverflow", "InvalidOperation", "System.Management.Automation.ScriptCallDepthException", null);
                try
                {
                    ExecBlock(sb.Ast.Begin, fs, st);
                    ExecBlock(sb.Ast.Process, fs, st);
                    ExecBlock(sb.Ast.EndBlock ?? sb.Ast.Body, fs, st);
                }
                catch (PsReturnEx) { }
            }
            finally
            {
                CallDepth--;
                if (dotSource) { if (oldUnder != null) fs.Vars["_"] = oldUnder; else fs.Vars.Remove("_"); if (oldThis != null) fs.Vars["this"] = oldThis; else if (thisObj != null) fs.Vars.Remove("this"); }
            }
        }

        public object InvokeBlockValue(PsScriptBlock sb, PsScope sc, object dollarUnder, object[] args, bool dotSource) { return PsConv.Collapse(InvokeBlock(sb, sc, dollarUnder, args, dotSource)); }
        public object InvokeBlockValue(PsScriptBlock sb, PsScope sc, object dollarUnder, object[] args, bool dotSource, object thisObj) { return PsConv.Collapse(InvokeBlock(sb, sc, dollarUnder, args, dotSource, thisObj)); }

        public PsObj InvocationInfo(string name, PsAst at, string scriptPath, PsBoundDict bound, int pos)
        {
            var o = new PsObj("System.Management.Automation.InvocationInfo", "System.Object");
            var line = at == null || at.Src == null ? InvocationText : at.Src.Text;
            o.Val("MyCommand", "System.Management.Automation.CommandInfo", name);
            o.Val("BoundParameters", "System.Collections.Generic.Dictionary[string,System.Object]", bound);
            o.Val("UnboundArguments", "System.Collections.Generic.List[System.Object]", new object[0]);
            o.Val("ScriptLineNumber", "int", 1);
            o.Val("OffsetInLine", "int", at == null ? 1 : at.Pos + 1);
            o.Val("ScriptName", "string", scriptPath ?? "");
            o.Val("Line", "string", line);
            o.Val("PSScriptRoot", "string", scriptPath == null ? "" : scriptPath.Substring(0, scriptPath.LastIndexOf('\\')));
            o.Val("PSCommandPath", "string", scriptPath);
            o.Val("InvocationName", "string", name);
            o.Val("PipelinePosition", "int", pos);
            o.Val("ExpectingInput", "bool", pos > 1);
            return o;
        }

        /// <summary>$PSCmdlet in an advanced function: ShouldProcess, ShouldContinue, ParameterSetName, WriteVerbose...</summary>
        public PsObj PsCmdletObj(ScriptProc proc, PsScope fs, string name, string set)
        {
            var o = new PsObj("System.Management.Automation.PSScriptCmdlet", "System.Management.Automation.PSCmdlet", "System.Object");
            o.Val("ParameterSetName", "string", set ?? "__AllParameterSets");
            o.Prop("MyInvocation", "System.Management.Automation.InvocationInfo", () => GetVar("MyInvocation", fs));
            o.Method("ShouldProcess", "bool ShouldProcess(string target), bool ShouldProcess(string target, string action)", (self, a) =>
            {
                string target = a.Length > 0 ? PsConv.ToStr(a[0]) : "";
                string action = a.Length > 1 ? PsConv.ToStr(a[1]) : name;
                if (PsConv.ToBool(GetVar("WhatIfPreference", fs))) { HostWrite("What if: Performing the operation \"" + action + "\" on target \"" + target + "\".\n"); return false; }
                string cp = Pref(fs, "ConfirmPreference");
                if (cp.Equals("Low", StringComparison.OrdinalIgnoreCase))
                    return AskChoice("Confirm", "Are you sure you want to perform this action?\nPerforming the operation \"" + action + "\" on target \"" + target + "\".", "Y") != "N";
                return true;
            });
            o.Method("ShouldContinue", "bool ShouldContinue(string query, string caption)", (self, a) => AskChoice(a.Length > 1 ? PsConv.ToStr(a[1]) : "Confirm", a.Length > 0 ? PsConv.ToStr(a[0]) : "", "Y") != "N");
            o.Method("WriteVerbose", "void WriteVerbose(string text)", (self, a) => { if (Pref(fs, "VerbosePreference") == "Continue") CurrentStreams.Verbose(PsConv.ToStr(a[0])); return AutoNull.Value; });
            o.Method("WriteWarning", "void WriteWarning(string text)", (self, a) => { CurrentStreams.Warn(PsConv.ToStr(a[0])); return AutoNull.Value; });
            o.Method("ThrowTerminatingError", "void ThrowTerminatingError(System.Management.Automation.ErrorRecord errorRecord)", (self, a) =>
            {
                var r = PsConv.Base(a[0]) as PsErrorRecord ?? MakeError(null, PsConv.ToStr(a[0]), "RuntimeException", "NotSpecified", null, null);
                throw new PsRuntimeException(r, true);
            });
            o.Method("WriteError", "void WriteError(System.Management.Automation.ErrorRecord errorRecord)", (self, a) => { var r = PsConv.Base(a[0]) as PsErrorRecord; if (r != null) { AddError(r); CurrentStreams.Err(r); } return AutoNull.Value; });
            return o;
        }

        /// <summary>A choice prompt like PowerShell's ([Y] Yes [A] Yes to All [N] No [L] No to All). Returns Y, A, N or L.</summary>
        public string AskChoice(string caption, string message, string def)
        {
            if (Host == null || !Host.Interactive) return def;
            HostWrite("\n" + caption + "\n" + message + "\n");
            for (int i = 0; i < 5; i++)
            {
                var a = Host.ReadLine("[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [S] Suspend  [?] Help (default is \"" + def + "\"): ", false);
                if (a == null) return def;
                a = a.Trim().ToUpperInvariant();
                if (a.Length == 0) return def;
                if (a == "Y" || a == "A" || a == "N" || a == "L") return a;
                if (a == "S") return "N";
                HostWrite("Y - Continue with only the next step of the operation.\nA - Continue with all the steps of the operation.\nN - Skip this operation and proceed with the next operation.\nL - Skip this operation and all subsequent operations.\n");
            }
            return def;
        }

        /// <summary>Get-Credential and a [pscredential] parameter given a user name: asks for the password in the console.</summary>
        public PsObj PromptCredential(string user)
        {
            if (Host == null || !Host.Interactive) return null;
            HostWrite("\nWindows PowerShell credential request\nEnter your credentials.\n");
            if (string.IsNullOrEmpty(user)) user = Host.ReadLine("User: ", false) ?? "";
            var pw = Host.ReadLine("Password for user " + user + ": ", true) ?? "";
            HostWrite("\n");
            return PsCred.Make(user, new PsSecureString { Plain = pw });
        }

        /// <summary>Why a script may not run under the execution policy (null: it may). The security part can refine it.</summary>
        public string ScriptBlockedReason(string path)
        {
            if (PsSecurityHook.Check != null) return PsSecurityHook.Check(this, path);
            string pol = M.EffectivePolicy(ProcessPolicy);
            if (pol == "Restricted") return "File " + path + " cannot be loaded because running scripts is disabled on this system. For more information, see about_Execution_Policies at https:/go.microsoft.com/fwlink/?LinkID=135170.";
            return null;
        }

        public int RunBatch(string path, NativeCall call)
        {
            HostWrite("Cert Lab: batch files (" + path + ") are not simulated; write the steps as a PowerShell script instead.\n");
            return 1;
        }

        public PsObj NewStopwatch()
        {
            var start = M.Now;
            var o = new PsObj("System.Diagnostics.Stopwatch", "System.Object");
            o.Prop("Elapsed", "timespan", () => M.Now - start);
            o.Prop("ElapsedMilliseconds", "long", () => (long)(M.Now - start).TotalMilliseconds);
            o.Val("IsRunning", "bool", true);
            o.Method("Stop", "void Stop()", (s, a) => AutoNull.Value);
            o.Method("Restart", "void Restart()", (s, a) => { start = M.Now; return AutoNull.Value; });
            return o;
        }
    }

    public static class PsSecurityHook { public static Func<PsRunspace, string, string> Check; }
}
