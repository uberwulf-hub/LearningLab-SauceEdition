// Cert Lab - the simulated PowerShell's interpreter: scopes and variables, statements (if, switch, loops, try /
// catch / finally, throw, functions), expressions and PowerShell's operators (-eq on arrays filters, -match sets
// $Matches, -replace, -split, -join, -f, ranges, casts), member access with member enumeration, and method calls.
// Errors follow Windows PowerShell 5.1: non-terminating errors are written and the script goes on; an error in an
// expression ends only that statement unless a try is around it; throw and -ErrorAction Stop end the script.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class PsVariable
    {
        public string Name = "", Description = "", Options = "None";
        public object Value;
        public bool ReadOnly, Constant, Private;
        public PsType Type;
        public PsModule Module;
    }

    public class PsFunction
    {
        public string Name = "";
        public ScriptBlockAst Ast;
        public bool Filter, IsConfiguration;
        public PsModule Module;
        public PsScope Home;           // a module's functions run in the module's scope
        public string Help { get { return Ast == null ? null : Ast.Help; } }
        public string Definition { get { return Ast == null ? "" : Ast.Text; } }
        public string Verb { get { int i = Name.IndexOf('-'); return i > 0 ? Name.Substring(0, i) : ""; } }
        public string Noun { get { int i = Name.IndexOf('-'); return i > 0 ? Name.Substring(i + 1) : ""; } }
    }

    public class PsScriptBlock
    {
        public ScriptBlockAst Ast;
        public PsModule Module;
        public PsScope Home;
        public string Text { get { return Ast == null ? "" : Ast.Text; } }
        public override string ToString() { return Text; }
    }

    public class PsScope
    {
        public PsScope Parent;
        public PsRunspace R;
        public string Kind = "local";    // global, script, function, module, local
        public PsModule Module;
        public readonly Dictionary<string, PsVariable> Vars = new Dictionary<string, PsVariable>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, PsFunction> Funcs = new Dictionary<string, PsFunction>(StringComparer.OrdinalIgnoreCase);
        public PsCommandInfo Command;     // the function or script this scope runs ($MyInvocation)
        public PsAst CallAt;              // where it was called (errors from Write-Error point there)
        public bool DotSourced;
        public PsScope(PsRunspace r, PsScope parent, string kind) { R = r; Parent = parent; Kind = kind; if (parent != null) Module = parent.Module; }
        public PsScope ScriptScope { get { var s = this; while (s.Parent != null && s.Kind != "script" && s.Kind != "module") s = s.Parent; return s; } }
    }

    public class PsFlowEx : Exception { public string Label; }
    public class PsBreakEx : PsFlowEx { }
    public class PsContinueEx : PsFlowEx { }
    public class PsReturnEx : PsFlowEx { }
    public class PsExitEx : PsFlowEx { public int Code; }

    public class PsRuntimeException : Exception
    {
        public PsErrorRecord Rec; public bool Terminating;
        public PsRuntimeException(PsErrorRecord r, bool terminating) : base(r.Exception.Message) { Rec = r; Terminating = terminating; }
    }

    /// <summary>Where a command's streams go: output, errors, warnings, verbose, debug, host (Write-Host).</summary>
    public class Streams
    {
        public Action<object> Out = o => { };
        public Action<PsErrorRecord> Err = e => { };
        public Action<string> Warn = s => { }, Verbose = s => { }, Debug = s => { };
        public Action<object> Info = o => { };
        public Streams Copy() { return (Streams)MemberwiseClone(); }
        public Streams WithOut(Action<object> o) { var s = Copy(); s.Out = o; return s; }
    }

    public interface IPsHost
    {
        void Write(string text);
        string ReadLine(string prompt, bool secret);   // null when no one can answer (scripts run by the checker)
        bool Interactive { get; }
        int Width { get; }
    }
    public class NullHost : IPsHost
    {
        public readonly StringBuilder Text = new StringBuilder();
        public void Write(string t) { Text.Append(t); if (Text.Length > 200000) Text.Remove(0, 100000); }
        public string ReadLine(string p, bool s) { return null; }
        public bool Interactive { get { return false; } }
        public int Width { get { return 120; } }
    }

    /// <summary>Extra members added to any object (Add-Member on a string, $PROFILE's note properties, PSComputerName).</summary>
    public static class PsNotes
    {
        static readonly ConditionalWeakTable<object, PsObj> table = new ConditionalWeakTable<object, PsObj>();
        public static PsObj Get(object o) { if (o == null || o is PsObj || o.GetType().IsValueType) return null; PsObj n; return table.TryGetValue(o, out n) ? n : null; }
        public static PsObj For(object o) { return table.GetValue(o, k => new PsObj()); }
    }

    /// <summary>The pipeline input of a function's end block ($input): it can be read only once.</summary>
    public class PsInputEnum : IEnumerable
    {
        readonly List<object> items; int pos;
        public PsInputEnum(List<object> l) { items = l; }
        public IEnumerator GetEnumerator() { while (pos < items.Count) yield return items[pos++]; }
        public override string ToString() { return "System.Collections.ArrayList+ArrayListEnumeratorSimple"; }
    }

    public partial class PsRunspace
    {
        public PsMachine M;
        public PsScope Global;
        public IPsHost Host;
        public readonly ArrayList Errors = new ArrayList();
        public readonly List<string> HistoryLines = new List<string>();
        public bool LastOk = true, CurrentOk = true;
        public int TryDepth;
        public long Steps;
        public static long StepLimit = 3000000;
        public string RemoteFrom;                   // set when this runspace is a remote session (Enter-PSSession / Invoke-Command)
        public Dictionary<string, object> Using;     // $using: values for a remote script block
        public string User = "Administrator";
        public bool Elevated = true;
        public int Width { get { return Host == null ? 120 : Math.Max(40, Host.Width); } }
        public int Pid;
        public string InvocationText = "";          // the current top-level line (for $^, $$)

        // ------------------------------------------------------------ variables
        public PsVariable FindVar(string name, PsScope sc)
        {
            if (name == "PSItem") name = "_";
            for (var s = sc; s != null; s = s.Parent)
            {
                PsVariable v;
                if (s.Vars.TryGetValue(name, out v) && (!v.Private || s == sc)) return v;
            }
            return null;
        }
        public object GetVar(string name, PsScope sc)
        {
            if (name == "?") return LastOk;
            var v = FindVar(name, sc);
            return v == null ? null : v.Value;
        }
        public PsScope ScopeFor(string qual, PsScope sc)
        {
            switch ((qual ?? "").ToLowerInvariant())
            {
                case "global": return Global;
                case "script": return sc.ScriptScope;
                case "local": case "private": case "": return sc;
            }
            return null;
        }
        public void SetVar(string name, object value, PsScope sc) { SetVar(name, "", value, sc, null); }
        public void SetVar(string name, string qual, object value, PsScope sc, PsType constraint)
        {
            if (name == "PSItem") name = "_";
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase) && qual.Length == 0) return;
            var target = ScopeFor(qual, sc) ?? sc;
            PsVariable v;
            if (!target.Vars.TryGetValue(name, out v) && qual.Length == 0)
            {
                // assignment without a scope writes the variable in the current scope (a copy, unless it is the same scope)
                v = null;
            }
            if (v == null)
            {
                var outer = qual.Length == 0 ? FindVar(name, sc) : null;
                if (outer != null && (outer.Constant || outer.ReadOnly) && IsAutomaticConstant(name)) throw VarNotWritable(name);
                v = new PsVariable { Name = name };
                if (outer != null && outer.Type != null && constraint == null) v.Type = outer.Type;
                target.Vars[name] = v;
                if (qual.Equals("private", StringComparison.OrdinalIgnoreCase)) v.Private = true;
            }
            if (v.Constant || v.ReadOnly) throw VarNotWritable(name);
            if (constraint != null) v.Type = constraint;
            if (v.Type != null && !(value is AutoNull && v.Type.FullName == "System.Object"))
            {
                try { value = PsConv.To(value, v.Type); }
                catch (PsConvertError e) { throw RtError(null, e.Message, "RuntimeException", "MetadataError", "System.Management.Automation.ArgumentTransformationMetadataException", null); }
            }
            v.Value = value;
        }
        static bool IsAutomaticConstant(string n) { return n.Equals("true", StringComparison.OrdinalIgnoreCase) || n.Equals("false", StringComparison.OrdinalIgnoreCase) || n.Equals("null", StringComparison.OrdinalIgnoreCase); }
        PsRuntimeException VarNotWritable(string name) { return RtError(null, "Cannot overwrite variable " + name + " because it is read-only or constant.", "VariableNotWritable", "WriteError", "System.Management.Automation.SessionStateUnauthorizedAccessException", name); }

        public PsVariable NewVar(PsScope sc, string name, object value)
        {
            var v = new PsVariable { Name = name, Value = value };
            sc.Vars[name] = v;
            return v;
        }

        /// <summary>A preference ($ErrorActionPreference, $VerbosePreference...) as seen from a scope.</summary>
        public string Pref(PsScope sc, string name)
        {
            var v = GetVar(name, sc);
            if (v == null) return name == "ErrorActionPreference" || name == "WarningPreference" ? "Continue" : "SilentlyContinue";
            if (v is bool) return (bool)v ? "Continue" : "SilentlyContinue";
            return PsConv.ToStr(v);
        }

        // ------------------------------------------------------------ errors
        public PsErrorRecord MakeError(PsAst at, string message, string errorId, string category, string exceptionType, object target)
        {
            var r = new PsErrorRecord { ErrorId = errorId ?? "RuntimeException", Category = category ?? "NotSpecified" };
            r.Exception = new PsException { TypeFullName = exceptionType ?? "System.Management.Automation.RuntimeException", Message = message };
            r.TargetObject = target;
            if (target != null) { r.TargetName = PsConv.ToStr(target); r.TargetType = PsTypes.ShortNameOf(target); }
            Locate(r, at);
            return r;
        }
        public void Locate(PsErrorRecord r, PsAst at)
        {
            if (at == null || at.Src == null) { r.Line = InvocationText; r.Length = Math.Max(1, InvocationText.Length); return; }
            string t = at.Src.Text;
            int pos = Math.Max(0, Math.Min(at.Pos, t.Length));
            int ls = t.LastIndexOf('\n', Math.Max(0, pos - 1)); if (pos == 0 || ls < 0) ls = -1;
            if (pos > 0 && t[pos - 1] == '\n') ls = pos - 1;
            int le = t.IndexOf('\n', pos); if (le < 0) le = t.Length;
            r.LineNumber = 1 + t.Take(pos).Count(c => c == '\n');
            r.Column = pos - ls;
            r.Line = t.Substring(ls + 1, le - ls - 1).TrimEnd('\r');
            r.Length = Math.Max(1, Math.Min(at.End, le) - pos);
            r.ScriptName = at.Src.File ?? "";
        }
        public PsRuntimeException RtError(PsAst at, string message, string errorId, string category, string exceptionType, object target)
        {
            return new PsRuntimeException(MakeError(at, message, errorId, category, exceptionType, target), false);
        }

        /// <summary>The red text Windows PowerShell 5.1 prints for an error.</summary>
        public static string ErrorText(PsErrorRecord r)
        {
            var sb = new StringBuilder();
            string msg = r.ErrorDetails ?? r.Exception.Message;
            if (r.ErrorId == "ParserError" || r.Category == "ParserError")
            {
                sb.Append(Where(r)).Append(msg).Append('\n');
            }
            else
            {
                if (r.CommandName.Length > 0) sb.Append(r.CommandName).Append(" : ");
                sb.Append(msg).Append('\n');
                if (!r.NoPosition) sb.Append(Where(r));
            }
            sb.Append("    + CategoryInfo          : ").Append(r.CategoryText).Append('\n');
            sb.Append("    + FullyQualifiedErrorId : ").Append(r.FqId).Append('\n');
            if (r.Computer != null) sb.Append("    + PSComputerName        : ").Append(r.Computer).Append('\n');
            sb.Append('\n');
            return sb.ToString();
        }
        static string Where(PsErrorRecord r)
        {
            var sb = new StringBuilder();
            sb.Append(r.ScriptName.Length > 0 ? "At " + r.ScriptName + ":" + r.LineNumber + " char:" + r.Column : "At line:" + r.LineNumber + " char:" + r.Column).Append('\n');
            sb.Append("+ ").Append(r.Line).Append('\n');
            int len = Math.Max(1, Math.Min(r.Length, Math.Max(1, r.Line.Length - (r.Column - 1))));
            sb.Append("+ ").Append(new string(' ', Math.Max(0, r.Column - 1))).Append(new string('~', len)).Append('\n');
            return sb.ToString();
        }

        /// <summary>A non-terminating error reached the error stream: $Error, $?, and the stream (unless redirected).</summary>
        public void AddError(PsErrorRecord r)
        {
            Errors.Insert(0, r);
            while (Errors.Count > 256) Errors.RemoveAt(Errors.Count - 1);
            CurrentOk = false;
        }

        // ------------------------------------------------------------ running statements
        public void Tick(PsAst at)
        {
            if (++Steps > StepLimit)
            {
                Steps = 0;
                throw new PsRuntimeException(MakeError(at, "Cert Lab stopped the command because it ran too long (an endless loop?). Check the loop's condition and try again.", "CertLabTooLong", "OperationStopped", "System.Management.Automation.PipelineStoppedException", null), true);
            }
        }

        public void ExecBlock(StmtBlock b, PsScope sc, Streams st)
        {
            if (b == null) return;
            foreach (var s in b.Stmts) ExecSafe(s, sc, st);
        }

        /// <summary>One statement; an error that ends only this statement is written and the next one runs.</summary>
        public void ExecSafe(PsAst s, PsScope sc, Streams st)
        {
            try { Exec(s, sc, st); }
            catch (PsRuntimeException e)
            {
                if (e.Terminating || TryDepth > 0) throw;
                AddError(e.Rec);
                st.Err(e.Rec);
            }
        }

        public void Exec(PsAst a, PsScope sc, Streams st)
        {
            Tick(a);
            if (a is PipelineAst || a is CommandAst || a is CmdExprAst) { RunPipeline(a, sc, st); return; }
            var asg = a as AssignAst;
            if (asg != null) { Assign(asg, sc, st); return; }
            var ia = a as IfAst;
            if (ia != null)
            {
                for (int i = 0; i < ia.Conds.Count; i++)
                    if (PsConv.ToBool(EvalCond(ia.Conds[i], sc, st))) { ExecBlock(ia.Blocks[i], sc, st); return; }
                if (ia.Else != null) ExecBlock(ia.Else, sc, st);
                return;
            }
            var fe = a as ForEachAst;
            if (fe != null) { ForEach(fe, sc, st); return; }
            var fa = a as ForAst;
            if (fa != null) { For(fa, sc, st); return; }
            var wa = a as WhileAst;
            if (wa != null) { While(wa, sc, st); return; }
            var sw = a as SwitchAst;
            if (sw != null) { Switch(sw, sc, st); return; }
            var fn = a as FunctionAst;
            if (fn != null) { DefineFunction(fn, sc); return; }
            var ta = a as TryAst;
            if (ta != null) { Try(ta, sc, st); return; }
            var fl = a as FlowAst;
            if (fl != null) { Flow(fl, sc, st); return; }
            var ca = a as ConfigAst;
            if (ca != null) { PsDscHook.Define(this, ca, sc); return; }
            if (a is NodeAst || a is ResourceAst) { PsDscHook.Statement(this, a, sc, st); return; }
            var da = a as DataAst;
            if (da != null) { ExecBlock(da.Body, sc, st); return; }
            if (a is TrapAst) throw RtError(a, "trap is real PowerShell, but Cert Lab's practice console does not simulate it. Use try / catch instead.", "NotSimulated", "NotImplemented", null, null);
            // an expression used as a statement
            EmitValue(Eval(a, sc, st), st.Out);
        }

        object EvalCond(PsAst c, PsScope sc, Streams st)
        {
            if (c is AssignAst) { Assign((AssignAst)c, sc, st); return AssignedValue((AssignAst)c, sc, st); }
            if (c is CmdExprAst && ((CmdExprAst)c).Redirs.Count == 0) return Eval(((CmdExprAst)c).Expr, sc, st);
            if (c is PipelineAst || c is CommandAst || c is CmdExprAst) return PsConv.Collapse(Collect(c, sc, st));
            return Eval(c, sc, st);
        }

        public static void EmitValue(object v, Action<object> o)
        {
            if (v is AutoNull) return;
            if (PsConv.IsCollection(v) || v is PsInputEnum) { foreach (var x in (IEnumerable)v) o(x); return; }
            o(v);
        }

        /// <summary>Runs a statement and returns everything it wrote to the output stream.</summary>
        public List<object> Collect(PsAst a, PsScope sc, Streams st)
        {
            var l = new List<object>();
            var st2 = st.WithOut(x => l.Add(x));
            if (a is StmtBlock) ExecBlock((StmtBlock)a, sc, st2);
            else Exec(a, sc, st2);
            return l;
        }
        public List<object> CollectBlock(StmtBlock b, PsScope sc, Streams st)
        {
            var l = new List<object>();
            ExecBlock(b, sc, st.WithOut(x => l.Add(x)));
            return l;
        }

        static bool LabelMatches(PsFlowEx e, string label) { return e.Label == null || e.Label.Length == 0 || (label != null && string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase)); }

        void ForEach(ForEachAst a, PsScope sc, Streams st)
        {
            object coll;
            if (a.Coll is CommandAst || a.Coll is PipelineAst)
            {
                // a command: the loop walks its output objects as they are (a list it returned with , stays one item)
                var outItems = Collect(a.Coll, sc, st);
                foreach (var x in outItems)
                {
                    Tick(a);
                    SetVar(a.Var, x, sc);
                    try { ExecBlock(a.Body, sc, st); }
                    catch (PsBreakEx e) { if (LabelMatches(e, a.Label)) break; throw; }
                    catch (PsContinueEx e) { if (LabelMatches(e, a.Label)) continue; throw; }
                }
                return;
            }
            if (a.Coll is CmdExprAst && ((CmdExprAst)a.Coll).Redirs.Count == 0) coll = Eval(((CmdExprAst)a.Coll).Expr, sc, st);
            else if (a.Coll is PipelineAst || a.Coll is CommandAst || a.Coll is CmdExprAst) coll = PsConv.Collapse(Collect(a.Coll, sc, st));
            else coll = Eval(a.Coll, sc, st);
            if (coll == null) return;
            var items = coll is PsInputEnum ? ((PsInputEnum)coll).Cast<object>().ToList() : PsConv.Items(coll).ToList();
            foreach (var x in items)
            {
                Tick(a);
                SetVar(a.Var, x, sc);
                try { ExecBlock(a.Body, sc, st); }
                catch (PsBreakEx e) { if (LabelMatches(e, a.Label)) break; throw; }
                catch (PsContinueEx e) { if (LabelMatches(e, a.Label)) continue; throw; }
            }
        }

        void For(ForAst a, PsScope sc, Streams st)
        {
            if (a.Init != null) Exec(a.Init, sc, st);
            while (true)
            {
                Tick(a);
                if (a.Cond != null && !PsConv.ToBool(EvalCond(a.Cond, sc, st))) break;
                try { ExecBlock(a.Body, sc, st); }
                catch (PsBreakEx e) { if (LabelMatches(e, a.Label)) break; throw; }
                catch (PsContinueEx e) { if (!LabelMatches(e, a.Label)) throw; }
                if (a.Iter != null) Exec(a.Iter, sc, st.WithOut(o => { }));
            }
        }

        void While(WhileAst a, PsScope sc, Streams st)
        {
            if (!a.Do)
            {
                while (true)
                {
                    Tick(a);
                    if (!PsConv.ToBool(EvalCond(a.Cond, sc, st))) break;
                    try { ExecBlock(a.Body, sc, st); }
                    catch (PsBreakEx e) { if (LabelMatches(e, a.Label)) break; throw; }
                    catch (PsContinueEx e) { if (!LabelMatches(e, a.Label)) throw; }
                }
                return;
            }
            while (true)
            {
                Tick(a);
                try { ExecBlock(a.Body, sc, st); }
                catch (PsBreakEx e) { if (LabelMatches(e, a.Label)) break; throw; }
                catch (PsContinueEx e) { if (!LabelMatches(e, a.Label)) throw; }
                bool c = PsConv.ToBool(EvalCond(a.Cond, sc, st));
                if (a.Until ? c : !c) break;
            }
        }

        void Switch(SwitchAst a, PsScope sc, Streams st)
        {
            List<object> items;
            if (a.File)
            {
                string path = PsConv.ToStr(Eval(a.Value, sc, st));
                items = ReadFileLines(path, sc).Cast<object>().ToList();
            }
            else
            {
                object v;
                if (a.Value is CmdExprAst && ((CmdExprAst)a.Value).Redirs.Count == 0) v = Eval(((CmdExprAst)a.Value).Expr, sc, st);
                else v = EvalCond(a.Value, sc, st);
                items = PsConv.Items(v).ToList();
            }
            var oldUnder = FindVar("_", sc);
            object saved = oldUnder == null ? null : oldUnder.Value;
            try
            {
                foreach (var x in items)
                {
                    Tick(a);
                    bool any = false;
                    SetVar("_", x, sc);
                    try
                    {
                        foreach (var cl in a.Clauses.Where(c => !c.IsDefault))
                        {
                            if (!SwitchMatch(a, cl, x, sc, st)) continue;
                            any = true;
                            SetVar("_", x, sc);
                            ExecBlock(cl.Body, sc, st);
                        }
                        if (!any) foreach (var cl in a.Clauses.Where(c => c.IsDefault)) ExecBlock(cl.Body, sc, st);
                    }
                    catch (PsContinueEx e) { if (!LabelMatches(e, a.Label)) throw; }
                }
            }
            catch (PsBreakEx e) { if (!LabelMatches(e, a.Label)) throw; }
            finally { if (oldUnder != null) SetVar("_", saved, sc); }
        }

        bool SwitchMatch(SwitchAst a, SwitchClause cl, object x, PsScope sc, Streams st)
        {
            if (cl.Cond is ScriptBlockAst)
            {
                var r = InvokeBlockValue(new PsScriptBlock { Ast = (ScriptBlockAst)cl.Cond, Module = sc.Module }, sc, x, null, true);
                return PsConv.ToBool(r);
            }
            object cv = Eval(cl.Cond, sc, st);
            string xs = PsConv.ToStr(x);
            if (a.Regex)
            {
                var m = Regex.Match(xs, PsConv.ToStr(cv), a.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                if (m.Success) SetVar("Matches", MatchesTable(m), sc);
                return m.Success;
            }
            if (a.Wildcard) return PsConv.Like(xs, PsConv.ToStr(cv), a.CaseSensitive);
            if (cv is string || x is string) return string.Compare(xs, PsConv.ToStr(cv), a.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0;
            return PsConv.Eq(x, cv, a.CaseSensitive);
        }

        public Hashtable MatchesTable(Match m)
        {
            var h = PsConv.NewHashtable();
            var rx = m.Groups;
            for (int i = rx.Count - 1; i >= 0; i--)
            {
                var g = rx[i];
                if (!g.Success) continue;
                int n;
                string name = GroupName(m, i);
                if (int.TryParse(name, out n)) h[n] = g.Value; else h[name] = g.Value;
            }
            return h;
        }
        static string GroupName(Match m, int i)
        {
            // Match has no direct access to its Regex; the Groups collection keeps names in .NET 4.7+ (Group.Name)
            var prop = typeof(Group).GetProperty("Name");
            if (prop != null) { var v = prop.GetValue(m.Groups[i], null) as string; if (v != null) return v; }
            return i.ToString(CultureInfo.InvariantCulture);
        }

        void DefineFunction(FunctionAst f, PsScope sc)
        {
            string name = f.Name;
            var target = sc;
            int colon = name.IndexOf(':');
            if (colon > 0)
            {
                string q = name.Substring(0, colon);
                var t = ScopeFor(q, sc);
                if (t != null) { target = t; name = name.Substring(colon + 1); }
            }
            var fn = new PsFunction { Name = name, Ast = f.Body, Filter = f.Filter, Module = sc.Module, Home = sc.Module != null ? sc.Module.Scope : null };
            target.Funcs[name] = fn;
        }

        void Try(TryAst a, PsScope sc, Streams st)
        {
            try
            {
                TryDepth++;
                bool inTry = true;
                try { ExecBlock(a.Body, sc, st); }
                catch (PsRuntimeException e)
                {
                    TryDepth--; inTry = false;
                    if (!e.Rec.Exception.TypeFullName.EndsWith("PipelineStoppedException", StringComparison.Ordinal) || e.Rec.ErrorId != "CertLabTooLong") { }
                    AddErrorOnce(e.Rec);
                    CatchClause hit = null;
                    foreach (var c in a.Catches)
                    {
                        if (c.Types.Count == 0) { hit = c; break; }
                        foreach (var tn in c.Types)
                        {
                            var t = PsTypes.Resolve(tn);
                            string full = t != null ? t.FullName : tn;
                            if (t == null && PsTypes.FullName(tn) == null && !PsException.IsExceptionType(tn) && !PsException.IsExceptionType("System." + tn))
                                throw RtError(a, "Unable to find type [" + tn + "].", "TypeNotFound", "InvalidOperation", null, tn);
                            if (e.Rec.Exception.Chain().Any(n => string.Equals(n, full, StringComparison.OrdinalIgnoreCase) || string.Equals(n, "System." + tn, StringComparison.OrdinalIgnoreCase))) { hit = c; break; }
                        }
                        if (hit != null) break;
                    }
                    if (hit == null) throw;
                    var old = FindVar("_", sc);
                    object saved = old == null ? null : old.Value;
                    SetVar("_", e.Rec, sc);
                    currentCatch.Push(e.Rec);
                    try { ExecBlock(hit.Body, sc, st); }
                    finally { currentCatch.Pop(); if (old != null) SetVar("_", saved, sc); }
                }
                finally { if (inTry) TryDepth--; }
            }
            finally
            {
                if (a.Finally != null) ExecBlock(a.Finally, sc, st);
            }
        }
        readonly Stack<PsErrorRecord> currentCatch = new Stack<PsErrorRecord>();
        void AddErrorOnce(PsErrorRecord r) { if (Errors.Count == 0 || Errors[0] != r) AddError(r); }

        void Flow(FlowAst a, PsScope sc, Streams st)
        {
            switch (a.Kind)
            {
                case "break": throw new PsBreakEx { Label = a.Label };
                case "continue": throw new PsContinueEx { Label = a.Label };
                case "return":
                    if (a.Value != null) EmitValue(EvalStatementValue(a.Value, sc, st), st.Out);
                    throw new PsReturnEx();
                case "exit":
                    {
                        int code = 0;
                        if (a.Value != null) { try { code = PsConv.ToInt(EvalStatementValue(a.Value, sc, st)); } catch (PsConvertError) { code = 0; } }
                        throw new PsExitEx { Code = code };
                    }
                case "throw":
                    {
                        if (a.Value == null)
                        {
                            if (currentCatch.Count > 0) throw new PsRuntimeException(currentCatch.Peek(), true);
                            var r0 = MakeError(a, "ScriptHalted", "ScriptHalted", "OperationStopped", null, null);
                            throw new PsRuntimeException(r0, true);
                        }
                        var v = PsConv.Base(EvalStatementValue(a.Value, sc, st));
                        if (v is PsErrorRecord) throw new PsRuntimeException((PsErrorRecord)v, true);
                        if (v is PsException)
                        {
                            var ex = (PsException)v;
                            var r1 = MakeError(a, ex.Message, ex.ShortName.Length > 0 ? ex.Message : "", "OperationStopped", ex.TypeFullName, null);
                            r1.Exception = ex; r1.ErrorId = ex.Message; r1.Reason = ex.ShortName;
                            throw new PsRuntimeException(r1, true);
                        }
                        string msg = PsConv.ToStr(v);
                        var r = MakeError(a, msg, msg, "OperationStopped", null, v);
                        r.TargetName = msg; r.TargetType = PsTypes.ShortNameOf(v);
                        throw new PsRuntimeException(r, true);
                    }
            }
        }

        object EvalStatementValue(PsAst a, PsScope sc, Streams st)
        {
            if (a is CmdExprAst && ((CmdExprAst)a).Redirs.Count == 0) return Eval(((CmdExprAst)a).Expr, sc, st);
            if (a is PipelineAst || a is CommandAst || a is CmdExprAst) return PsConv.Collapse(Collect(a, sc, st));
            if (a is AssignAst) { Assign((AssignAst)a, sc, st); return AutoNull.Value; }
            if (a is IfAst || a is SwitchAst || a is ForEachAst || a is ForAst || a is WhileAst || a is TryAst) return PsConv.Collapse(Collect(a, sc, st));
            return Eval(a, sc, st);
        }

        // ------------------------------------------------------------ assignment
        void Assign(AssignAst a, PsScope sc, Streams st)
        {
            object v;
            if (a.Value is CmdExprAst && ((CmdExprAst)a.Value).Redirs.Count == 0) v = Eval(((CmdExprAst)a.Value).Expr, sc, st);
            else v = EvalStatementValue(a.Value, sc, st);
            if (a.Op != "=")
            {
                var cur = Eval(a.Target is CastAst ? ((CastAst)a.Target).X : a.Target, sc, st);
                string op = a.Op.Substring(0, 1);
                v = Binary(op, cur, v, a, sc);
            }
            AssignTo(a.Target, v, sc, st, a);
        }
        object AssignedValue(AssignAst a, PsScope sc, Streams st) { return Eval(a.Target is CastAst ? ((CastAst)a.Target).X : a.Target, sc, st); }

        void AssignTo(PsAst target, object v, PsScope sc, Streams st, PsAst at)
        {
            var va = target as VarAst;
            if (va != null) { SetVariable(va, v, sc, null); return; }
            var ca = target as CastAst;
            if (ca != null)
            {
                var t = PsTypes.Resolve(ca.Type);
                if (t == null) throw RtError(ca, "Unable to find type [" + ca.Type + "].", "TypeNotFound", "InvalidOperation", null, ca.Type);
                SetVariable((VarAst)ca.X, v, sc, t);
                return;
            }
            var ma = target as MemberAst;
            if (ma != null)
            {
                var obj = Eval(ma.Target, sc, st);
                string name = MemberName(ma.Name, sc, st);
                SetMember(obj, name, v, ma);
                return;
            }
            var ix = target as IndexAst;
            if (ix != null)
            {
                var obj = PsConv.Base(Eval(ix.Target, sc, st));
                var idx = PsConv.Base(Eval(ix.Index, sc, st));
                SetIndex(obj, idx, v, ix);
                return;
            }
            var arr = target as ArrayLitAst;
            if (arr != null)
            {
                var items = PsConv.List(v);
                for (int i = 0; i < arr.Items.Count; i++)
                {
                    object val;
                    if (i < arr.Items.Count - 1) val = i < items.Count ? items[i] : null;
                    else val = items.Count - i > 1 ? (object)items.Skip(i).ToArray() : (i < items.Count ? items[i] : null);
                    AssignTo(arr.Items[i], val, sc, st, at);
                }
                return;
            }
            throw RtError(at, "The assignment expression is not valid.", "InvalidLeftHandSide", "ParserError", null, null);
        }

        void SetVariable(VarAst va, object v, PsScope sc, PsType constraint)
        {
            string q = va.Scope.ToLowerInvariant();
            if (q == "env") { SetEnv(va.Name, v == null || v is AutoNull ? null : PsConv.ToStr(v)); return; }
            if (q == "variable") q = "";
            if (q == "function") { DefineFunctionValue(va.Name, v, sc); return; }
            if (q == "using") throw RtError(va, "A Using variable cannot be assigned a value.", "UsingVariableAssignment", "InvalidOperation", null, null);
            if (q != "" && q != "global" && q != "script" && q != "local" && q != "private")
            {
                // ${C:\file.txt} = 'text'
                ProviderSetContent(va.Scope + ":" + va.Name, v, sc);
                return;
            }
            SetVar(va.Name, q, v, sc, constraint);
        }

        void DefineFunctionValue(string name, object v, PsScope sc)
        {
            var sb = PsConv.Base(v) as PsScriptBlock;
            if (sb == null) { var p = PsParser.Parse(PsConv.ToStr(v), ""); sb = new PsScriptBlock { Ast = p }; }
            sc.Funcs[name] = new PsFunction { Name = name, Ast = sb.Ast, Module = sb.Module };
        }

        // ------------------------------------------------------------ expressions
        public object Eval(PsAst a, PsScope sc, Streams st)
        {
            try { return Eval2(a, sc, st); }
            catch (PsConvertError e) { var ex = RtError(a, e.Message, e is PsEnumError ? "RuntimeException" : ConvertErrorId(e), "InvalidArgument", "System.Management.Automation.PSInvalidCastException", null); ex.Rec.Reason = "RuntimeException"; throw ex; }   // 5.1 shows "[], RuntimeException" for a failed cast
            catch (DivideByZeroException) { throw RtError(a, "Attempted to divide by zero.", "RuntimeException", "NotSpecified", "System.DivideByZeroException", null); }
            catch (OverflowException) { throw RtError(a, "Arithmetic operation resulted in an overflow.", "RuntimeException", "NotSpecified", "System.OverflowException", null); }
            catch (ArgumentException e) { throw RtError(a, e.Message, "RuntimeException", "NotSpecified", "System.ArgumentException", null); }
            catch (RegexMatchTimeoutException) { throw RtError(a, "The regular expression took too long to run.", "RuntimeException", "NotSpecified", "System.Text.RegularExpressions.RegexMatchTimeoutException", null); }
            catch (PsOpError e) { throw RtError(a, e.Message, e.Id.StartsWith("System.", StringComparison.Ordinal) ? "RuntimeException" : e.Id, e.Id == "MethodNotFound" ? "InvalidOperation" : "NotSpecified", e.Id.StartsWith("System.", StringComparison.Ordinal) ? e.Id : null, null); }
            catch (PsRegexError e) { throw RtError(a, "The regular expression pattern is not valid: " + e.Message, "InvalidRegularExpression", "InvalidOperation", null, null); }
        }
        static string ConvertErrorId(PsConvertError e)
        {
            if (e.Value is string && e.TypeName == "System.Int32") return "InvalidCastFromStringToInteger";
            if (e.Value is string && e.TypeName == "System.DateTime") return "InvalidCastParseTargetInvocation";
            return "ConvertToFinalInvalidCastException";
        }

        object Eval2(PsAst a, PsScope sc, Streams st)
        {
            var k = a as ConstAst;
            if (k != null) return k.Value;
            var v = a as VarAst;
            if (v != null) return ReadVariable(v, sc);
            var s = a as StrAst;
            if (s != null) return Expand(s, sc, st);
            var b = a as BinAst;
            if (b != null) return EvalBinary(b, sc, st);
            var u = a as UnAst;
            if (u != null) return EvalUnary(u, sc, st);
            var m = a as MemberAst;
            if (m != null)
            {
                if (m.Static)
                {
                    var ty = StaticTarget(m.Target, sc, st);
                    return StaticMember(ty, MemberName(m.Name, sc, st), m);
                }
                return GetMember(Eval(m.Target, sc, st), MemberName(m.Name, sc, st), m, true);
            }
            var inv = a as InvokeAst;
            if (inv != null)
            {
                var args = inv.Args.Select(x => Eval(x, sc, st)).ToArray();
                string name = MemberName(inv.Name, sc, st);
                if (inv.Static) return StaticCall(StaticTarget(inv.Target, sc, st), name, args, inv, sc);
                return CallMethod(Eval(inv.Target, sc, st), name, args, inv, sc);
            }
            var ix = a as IndexAst;
            if (ix != null) return Index(Eval(ix.Target, sc, st), Eval(ix.Index, sc, st), ix);
            var arr = a as ArrayLitAst;
            if (arr != null) return arr.Items.Select(x => { var r = Eval(x, sc, st); return r is AutoNull ? null : r; }).ToArray();
            var ae = a as ArrayExprAst;
            if (ae != null) return CollectBlock(ae.Body, sc, st).ToArray();
            var se = a as SubExprAst;
            if (se != null) return PsConv.Collapse(CollectBlock(se.Body, sc, st));
            var pa = a as ParenAst;
            if (pa != null)
            {
                if (pa.Inner is AssignAst) { Assign((AssignAst)pa.Inner, sc, st); return AssignedValue((AssignAst)pa.Inner, sc, st); }
                if (pa.Inner is CmdExprAst && ((CmdExprAst)pa.Inner).Redirs.Count == 0) return Eval(((CmdExprAst)pa.Inner).Expr, sc, st);
                return PsConv.Collapse(Collect(pa.Inner, sc, st));
            }
            var h = a as HashAst;
            if (h != null) return MakeHash(h, sc, st, false);
            var sbk = a as ScriptBlockAst;
            if (sbk != null) return new PsScriptBlock { Ast = sbk, Module = sc.Module, Home = sc.Module != null ? sc.Module.Scope : null };
            var ta = a as TypeAst;
            if (ta != null)
            {
                var t = ResolveType(ta.Type, ta);
                return new PsTypeInfo { FullName = t.Display, Type = t };
            }
            var ca = a as CastAst;
            if (ca != null) return Cast(ca, sc, st);
            if (a is AssignAst) { Assign((AssignAst)a, sc, st); return AssignedValue((AssignAst)a, sc, st); }
            if (a is CmdExprAst && ((CmdExprAst)a).Redirs.Count == 0) return Eval(((CmdExprAst)a).Expr, sc, st);
            if (a is PipelineAst || a is CommandAst || a is CmdExprAst || a is StmtBlock) return PsConv.Collapse(Collect(a, sc, st));
            return EvalStatementValue(a, sc, st);
        }

        public PsType ResolveType(string name, PsAst at)
        {
            var t = PsTypes.Resolve(name);
            if (t == null) throw RtError(at, "Unable to find type [" + name + "].", "TypeNotFound", "InvalidOperation", null, name);
            return t;
        }

        string MemberName(PsAst n, PsScope sc, Streams st)
        {
            var c = n as ConstAst;
            if (c != null) return PsConv.ToStr(c.Value);
            return PsConv.ToStr(Eval(n, sc, st));
        }

        PsType StaticTarget(PsAst t, PsScope sc, Streams st)
        {
            var ta = t as TypeAst;
            if (ta != null) return ResolveType(ta.Type, ta);
            var v = PsConv.Base(Eval(t, sc, st));
            var ti = v as PsTypeInfo;
            if (ti != null) return ti.Type ?? PsTypes.Resolve(ti.FullName) ?? ResolveType(ti.FullName, t);
            if (v is string) return ResolveType((string)v, t);
            throw RtError(t, "Unable to find type [" + PsConv.ToStr(v) + "].", "TypeNotFound", "InvalidOperation", null, null);
        }

        public object ReadVariable(VarAst v, PsScope sc)
        {
            string q = v.Scope.ToLowerInvariant();
            if (q.Length == 0 || q == "variable" || q == "local")
            {
                if (v.Name == "?") return LastOk;
                if (v.Name == "^") { var w = InvocationText.Trim().Split(' '); return w.Length > 0 ? w[0] : ""; }
                if (v.Name == "$") { var w = InvocationText.Trim().Split(' '); return w.Length > 0 ? w[w.Length - 1] : ""; }
                var vr = FindVar(v.Name, sc);
                if (vr == null)
                {
                    if (StrictMode(sc)) throw RtError(v, "The variable '$" + v.Name + "' cannot be retrieved because it has not been set.", "VariableIsUndefined", "InvalidOperation", null, v.Name);
                    return null;
                }
                return vr.Value;
            }
            if (q == "global" || q == "script" || q == "private")
            {
                var s = ScopeFor(q, sc);
                PsVariable vr;
                if (q == "global") { for (var x = s; x != null; x = x.Parent) if (x.Vars.TryGetValue(v.Name, out vr)) return vr.Value; return null; }
                for (var x = s; x != null; x = x.Parent) if (x.Vars.TryGetValue(v.Name, out vr)) return vr.Value;
                return null;
            }
            if (q == "env") return GetEnv(v.Name);
            if (q == "using")
            {
                object u;
                if (Using != null && Using.TryGetValue(v.Name, out u)) return u;
                return GetVar(v.Name, sc);
            }
            if (q == "function")
            {
                var f = FindFunction(v.Name, sc);
                return f == null ? null : new PsScriptBlock { Ast = f.Ast, Module = f.Module };
            }
            if (q == "alias") { string t; return Aliases.TryGetValue(v.Name, out t) ? t : null; }
            // ${C:\file.txt}: the file's text
            return ProviderGetContent(v.Scope + ":" + v.Name, sc, v);
        }

        bool StrictMode(PsScope sc) { var s = GetVar("__StrictMode", sc); return s != null && PsConv.ToBool(s); }

        public string Expand(StrAst s, PsScope sc, Streams st)
        {
            var sb = new StringBuilder();
            foreach (var p in s.Parts)
            {
                if (p is string) { sb.Append((string)p); continue; }
                var a = (PsAst)p;
                object v;
                if (a is SubExprAst) { var l = CollectBlock(((SubExprAst)a).Body, sc, st); v = l.Count == 1 ? l[0] : (object)l.ToArray(); if (l.Count == 0) v = null; }
                else v = Eval(a, sc, st);
                sb.Append(ToStrOfs(v, sc));
            }
            return sb.ToString();
        }
        public string ToStrOfs(object v, PsScope sc)
        {
            v = PsConv.Base(v);
            if (PsConv.IsCollection(v) || v is PsInputEnum)
            {
                var ofs = GetVar("OFS", sc);
                string sep = ofs == null ? " " : PsConv.ToStr(ofs);
                return string.Join(sep, ((IEnumerable)v).Cast<object>().Select(x => PsConv.ToStr(x)).ToArray());
            }
            return PsConv.ToStr(v);
        }

        object MakeHash(HashAst h, PsScope sc, Streams st, bool ordered)
        {
            IDictionary d = ordered ? (IDictionary)PsConv.NewOrdered() : PsConv.NewHashtable();
            foreach (var kv in h.Pairs)
            {
                var key = PsConv.Base(Eval(kv.Key, sc, st));
                if (key == null) throw RtError(kv.Key, "A null key is not allowed in a hash literal.", "InvalidNullKey", "InvalidOperation", null, null);
                var val = EvalStatementValue(kv.Value, sc, st);
                if (val is AutoNull) val = null;
                if (d.Contains(key)) throw RtError(kv.Key, "Duplicate keys '" + PsConv.ToStr(key) + "' are not allowed in hash literals.", "DuplicateKeyInHashLiteral", "InvalidOperation", null, null);
                d[key] = val;
            }
            return d;
        }

        object Cast(CastAst c, PsScope sc, Streams st)
        {
            string tn = c.Type.Trim();
            if (tn.Equals("ordered", StringComparison.OrdinalIgnoreCase))
            {
                var h = c.X as HashAst;
                if (h == null) throw RtError(c, "The ordered attribute can be specified only on a hash literal node.", "OrderedAttributeOnlyOnHashLiteralNode", "ParserError", null, null);
                return MakeHash(h, sc, st, true);
            }
            if ((tn.Equals("pscustomobject", StringComparison.OrdinalIgnoreCase) || tn.Equals("psobject", StringComparison.OrdinalIgnoreCase) && false) && c.X is HashAst)
            {
                var o = PsObj.Custom();
                foreach (var kv in ((HashAst)c.X).Pairs)
                {
                    var val = EvalStatementValue(kv.Value, sc, st);
                    o.Add(PsConv.ToStr(Eval(kv.Key, sc, st)), val is AutoNull ? null : val);
                }
                return o;
            }
            var t = ResolveType(tn, c);
            var v = Eval(c.X, sc, st);
            if (t.FullName == "System.Void") return AutoNull.Value;
            if (t.FullName == "System.Management.Automation.PSObject" && tn.Equals("pscustomobject", StringComparison.OrdinalIgnoreCase) && PsConv.Base(v) is IDictionary) return PsConv.To(v, t);
            if (t.FullName == "System.Management.Automation.ScriptBlock" && PsConv.Base(v) is string) return new PsScriptBlock { Ast = PsParser.Parse((string)PsConv.Base(v), "") };
            if (t.FullName == "System.Xml.XmlDocument") throw RtError(c, "[xml] is real PowerShell, but Cert Lab's practice console does not simulate XML documents.", "NotSimulated", "NotImplemented", null, null);
            return PsConv.To(v, t);
        }

        // ------------------------------------------------------------ operators
        object EvalBinary(BinAst b, PsScope sc, Streams st)
        {
            string op = b.Op;
            if (op == "and") { if (!PsConv.ToBool(Eval(b.L, sc, st))) return false; return PsConv.ToBool(Eval(b.R, sc, st)); }
            if (op == "or") { if (PsConv.ToBool(Eval(b.L, sc, st))) return true; return PsConv.ToBool(Eval(b.R, sc, st)); }
            var l = Eval(b.L, sc, st);
            if (op == "is" || op == "isnot" || op == "as")
            {
                PsType t;
                if (b.R is TypeAst) t = ResolveType(((TypeAst)b.R).Type, b.R);
                else { var rv = PsConv.Base(Eval(b.R, sc, st)); t = rv is PsTypeInfo ? (((PsTypeInfo)rv).Type ?? ResolveType(((PsTypeInfo)rv).FullName, b.R)) : ResolveType(PsConv.ToStr(rv), b.R); }
                if (op == "as") { try { if (t.FullName == "System.Management.Automation.PSObject" && PsConv.Base(l) is IDictionary) return PsConv.To(l, t); return PsTypes.IsInstance(l, t) ? PsConv.Base(l) : PsConv.To(l, t); } catch (PsConvertError) { return null; } catch (FormatException) { return null; } }
                bool isit = PsTypes.IsInstance(l, t);
                return op == "is" ? isit : !isit;
            }
            var r = Eval(b.R, sc, st);
            return Binary(op, l, r, b, sc);
        }

        public object Binary(string op, object l, object r, PsAst at, PsScope sc)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            bool cs = false;
            string bop = op;
            if (op.Length > 2 && (op[0] == 'c' || op[0] == 'i') && op != "contains" && op != "in" && op != "is" && op != "isnot" && op != "as" && !op.StartsWith("con", StringComparison.Ordinal) && op != "join")
            {
                cs = op[0] == 'c'; bop = op.Substring(1);
            }
            if (op == "cin" || op == "iin") { cs = op[0] == 'c'; bop = "in"; }
            if (op == "ccontains" || op == "icontains") { cs = op[0] == 'c'; bop = "contains"; }
            if (op == "cnotcontains" || op == "inotcontains") { cs = op[0] == 'c'; bop = "notcontains"; }
            if (op == "cnotin" || op == "inotin") { cs = op[0] == 'c'; bop = "notin"; }
            switch (bop)
            {
                case "+": return PsOps.Add(l, r);
                case "-": return PsOps.Sub(l, r);
                case "*": return PsOps.Mul(l, r);
                case "/": return PsOps.Div(l, r);
                case "%": return PsOps.Mod(l, r);
                case "..": return PsOps.Range(l, r);
                case "f": return Format(l, r, at);
                case "xor": return PsConv.ToBool(l) ^ PsConv.ToBool(r);
                case "band": return PsOps.Bits(l, r, "and");
                case "bor": return PsOps.Bits(l, r, "or");
                case "bxor": return PsOps.Bits(l, r, "xor");
                case "shl": return PsOps.Bits(l, r, "shl");
                case "shr": return PsOps.Bits(l, r, "shr");
                case "eq": case "ne": case "gt": case "ge": case "lt": case "le": case "like": case "notlike": case "match": case "notmatch":
                    if (PsConv.IsCollection(l))
                    {
                        var res = new List<object>();
                        foreach (var x in (IEnumerable)l) if (Compare1(bop, x, r, cs, sc, false)) res.Add(x);
                        return res.ToArray();
                    }
                    return Compare1(bop, l, r, cs, sc, true);
                case "contains": case "notcontains":
                    {
                        bool has = PsConv.Items(l).Any(x => PsConv.Eq(x, r, cs));
                        return bop == "contains" ? has : !has;
                    }
                case "in": case "notin":
                    {
                        bool has = PsConv.Items(r).Any(x => PsConv.Eq(x, l, cs));
                        return bop == "in" ? has : !has;
                    }
                case "replace": return Replace(l, r, cs, at);
                case "split": return Split(l, r, cs, at, sc);
                case "join": return Join(l, r);
            }
            throw RtError(at, "The operator -" + op + " is not supported here.", "OperatorNotSupported", "InvalidOperation", null, null);
        }

        bool Compare1(string op, object x, object r, bool cs, PsScope sc, bool setMatches)
        {
            switch (op)
            {
                case "eq": return PsConv.Eq(x, r, cs);
                case "ne": return !PsConv.Eq(x, r, cs);
                case "gt": return PsConv.Compare(x, r, cs) > 0;
                case "ge": return PsConv.Compare(x, r, cs) >= 0;
                case "lt": return PsConv.Compare(x, r, cs) < 0;
                case "le": return PsConv.Compare(x, r, cs) <= 0;
                case "like": return PsConv.Like(PsConv.ToStr(x), PsConv.ToStr(r), cs);
                case "notlike": return !PsConv.Like(PsConv.ToStr(x), PsConv.ToStr(r), cs);
                case "match": case "notmatch":
                    {
                        var rx = MakeRegex(PsConv.ToStr(r), cs);
                        var m = rx.Match(PsConv.ToStr(x));
                        if (setMatches && m.Success) SetVar("Matches", MatchesTable(m), sc);
                        return op == "match" ? m.Success : !m.Success;
                    }
            }
            return false;
        }

        public static Regex MakeRegex(string pattern, bool cs)
        {
            try { return new Regex(pattern, cs ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException e) { throw new PsRegexError(pattern, e.Message); }
        }

        object Replace(object l, object r, bool cs, PsAst at)
        {
            var parts = PsConv.List(r);
            if (parts.Count > 2) throw RtError(at, "The -replace operator allows only two elements to follow it, not " + parts.Count + ".", "BadReplaceArgument", "InvalidOperation", null, null);
            string pat = parts.Count > 0 ? PsConv.ToStr(parts[0]) : "";
            string rep = parts.Count > 1 ? PsConv.ToStr(parts[1]) : "";
            Regex rx;
            try { rx = MakeRegex(pat, cs); }
            catch (PsRegexError e) { throw RtError(at, "The regular expression pattern " + pat + " is not valid.", "InvalidRegularExpression", "InvalidOperation", null, pat); }
            if (PsConv.IsCollection(l)) return PsConv.Items(l).Select(x => (object)rx.Replace(PsConv.ToStr(x), rep)).ToArray();
            return rx.Replace(PsConv.ToStr(l), rep);
        }

        object Split(object l, object r, bool cs, PsAst at, PsScope sc)
        {
            var parts = PsConv.List(r);
            object pat = parts.Count > 0 ? parts[0] : " ";
            int max = parts.Count > 1 ? PsConv.ToInt(parts[1]) : 0;
            string opts = parts.Count > 2 ? PsConv.ToStr(parts[2]) : "";
            bool simple = opts.IndexOf("SimpleMatch", StringComparison.OrdinalIgnoreCase) >= 0;
            var res = new List<object>();
            foreach (var x in PsConv.Items(l))
            {
                string s = PsConv.ToStr(x);
                if (pat is PsScriptBlock)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (PsConv.ToBool(InvokeBlockValue((PsScriptBlock)pat, sc, s[i].ToString(), null, true))) { res.Add(sb.ToString()); sb.Clear(); }
                        else sb.Append(s[i]);
                    }
                    res.Add(sb.ToString());
                    continue;
                }
                string p = PsConv.ToStr(pat);
                if (simple) p = Regex.Escape(p);
                Regex rx;
                try { rx = MakeRegex(p, cs || opts.IndexOf("CaseSensitive", StringComparison.OrdinalIgnoreCase) >= 0); }
                catch (PsRegexError) { throw RtError(at, "The regular expression pattern " + p + " is not valid.", "InvalidRegularExpression", "InvalidOperation", null, p); }
                var pieces = max > 0 ? rx.Split(s, max) : rx.Split(s);
                foreach (var pc in pieces) res.Add(pc);
            }
            return res.ToArray();
        }

        static object Join(object l, object r)
        {
            string sep = PsConv.ToStr(r);
            return string.Join(sep, PsConv.Items(l).Select(x => PsConv.ToStr(x)).ToArray());
        }

        object Format(object l, object r, PsAst at)
        {
            var args = PsConv.IsCollection(r) ? PsConv.List(r).Select(FormatArg).ToArray() : new[] { FormatArg(r) };
            try { return string.Format(PsConv.Us, PsConv.ToStr(l), args); }
            catch (FormatException e) { throw RtError(at, "Error formatting a string: " + e.Message + ".", "FormatError", "InvalidOperation", "System.FormatException", PsConv.ToStr(l)); }
        }
        static object FormatArg(object x)
        {
            x = PsConv.Base(x);
            if (x is PsEnum) return ((PsEnum)x).Name;
            if (x is PsObj || x is PsSwitch || x is IDictionary) return PsConv.ToStr(x);
            if (PsConv.IsCollection(x)) return PsConv.ToStr(x);
            if (x is bool) return (bool)x ? "True" : "False";
            return PsConv.FwNum(x);
        }

        object EvalUnary(UnAst u, PsScope sc, Streams st)
        {
            switch (u.Op)
            {
                case "not": return !PsConv.ToBool(Eval(u.X, sc, st));
                case "bnot": { long bx = ~PsConv.ToLong(Eval(u.X, sc, st)); return bx >= int.MinValue && bx <= int.MaxValue ? (object)(int)bx : bx; }
                case "-":
                    {
                        var v = PsConv.Base(Eval(u.X, sc, st));
                        var n = PsConv.ToNumber(v);
                        if (n is int) return (int)n == int.MinValue ? (object)(-(long)(int)n) : -(int)n;
                        if (n is long) return -(long)n;
                        if (n is decimal) return -(decimal)n;
                        return -Convert.ToDouble(n, PsConv.Inv);
                    }
                case "+": return PsConv.ToNumber(Eval(u.X, sc, st));
                case "split":
                    {
                        var v = Eval(u.X, sc, st);
                        var res = new List<object>();
                        foreach (var x in PsConv.Items(v)) foreach (var w in PsConv.ToStr(x).Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)) res.Add(w);
                        return res.ToArray();
                    }
                case "join": return string.Concat(PsConv.Items(Eval(u.X, sc, st)).Select(x => PsConv.ToStr(x)).ToArray());
                case "++": case "--":
                    {
                        var cur = PsConv.Base(Eval(u.X, sc, st));
                        var nv = u.Op == "++" ? PsOps.Add(cur ?? 0, 1) : PsOps.Sub(cur ?? 0, 1);
                        AssignTo(u.X, nv, sc, st, u);
                        return u.Postfix ? (cur ?? 0) : nv;
                    }
            }
            throw RtError(u, "Unknown operator " + u.Op, "OperatorNotSupported", "InvalidOperation", null, null);
        }

        // ------------------------------------------------------------ members
        public object GetMember(object target, string name, PsAst at) { return GetMember(target, name, at, false); }
        /// <summary>intrinsic: a member-access expression ($x.Count, $x.Length), where every object has a Count and Length
        /// (0 for $null, 1 for one object). Cmdlets and formatting read real properties only (Select-Object Length on a folder is empty).</summary>
        public object GetMember(object target, string name, PsAst at, bool intrinsic)
        {
            target = PsConv.Base(target);
            if (target == null)
            {
                if (StrictModeAt(at)) throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists.", "PropertyNotFoundStrict", "NotSpecified", "System.Management.Automation.PropertyNotFoundException", null);
                // Since PowerShell 3.0, $null has a Count and a Length of 0.
                if (intrinsic && (name.Equals("Count", StringComparison.OrdinalIgnoreCase) || name.Equals("Length", StringComparison.OrdinalIgnoreCase))) return 0;
                return null;
            }
            var notes = PsNotes.Get(target);
            if (notes != null && notes.Has(name)) return notes.Get(name);
            bool found;
            var v = PsMembers.Get(this, target, name, out found);
            if (found) return v;
            // member enumeration over a collection
            if (PsConv.IsCollection(target) || target is PsInputEnum)
            {
                var res = new List<object>();
                foreach (var x in (IEnumerable)target)
                {
                    var xv = GetMember(x, name, at, intrinsic);
                    if (xv == null) continue;
                    if (PsConv.IsCollection(xv)) foreach (var y in (IEnumerable)xv) res.Add(y); else res.Add(xv);
                }
                if (res.Count == 0) return null;
                if (res.Count == 1) return res[0];
                return res.ToArray();
            }
            if (intrinsic && (name.Equals("Count", StringComparison.OrdinalIgnoreCase) || name.Equals("Length", StringComparison.OrdinalIgnoreCase))) return 1;
            if (StrictModeAt(at)) throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists.", "PropertyNotFoundStrict", "NotSpecified", "System.Management.Automation.PropertyNotFoundException", null);
            return null;
        }
        bool StrictModeAt(PsAst at) { return false; }

        public void SetMember(object target, string name, object v, PsAst at)
        {
            target = PsConv.Base(target);
            if (v is AutoNull) v = null;
            if (target == null) throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.", "PropertyNotFound", "InvalidOperation", "System.Management.Automation.RuntimeException", null);
            var d = target as IDictionary;
            if (d != null) { d[name] = v; return; }
            var o = target as PsObj;
            if (o != null)
            {
                var p = o.Find(name);
                if (p == null) throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.", "PropertyNotFound", "InvalidOperation", "System.Management.Automation.RuntimeException", null);
                if (p.ReadOnly && p.Setter == null) throw RtError(at, "'" + name + "' is a ReadOnly property.", "PropertyAssignmentException", "InvalidOperation", "System.Management.Automation.SetValueInvocationException", null);
                if (p.SetScript != null) { InvokeBlockValue(p.SetScript, Global, v, new object[] { v }, false, o); return; }
                if (p.Kind == "Property" && p.Value != null && v != null && p.TypeName != null && p.TypeName.Length > 0 && p.Setter == null)
                {
                    var t = PsTypes.Resolve(p.TypeName);
                    if (t != null) { try { v = PsConv.To(v, t); } catch (PsConvertError e) { throw RtError(at, "Exception setting \"" + name + "\": \"" + e.Message + "\"", "ExceptionWhenSetting", "NotSpecified", "System.Management.Automation.SetValueInvocationException", null); } }
                }
                try { o.Set(name, v); }
                catch (PsRuntimeException) { throw; }
                catch (Exception e) { throw RtError(at, "Exception setting \"" + name + "\": \"" + e.Message + "\"", "ExceptionWhenSetting", "NotSpecified", "System.Management.Automation.SetValueInvocationException", null); }
                return;
            }
            var notes = PsNotes.Get(target);
            if (notes != null && notes.Has(name)) { notes.Set(name, v); return; }
            if (PsMembers.SetClr(this, target, name, v)) return;
            if (PsConv.IsCollection(target)) throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.", "PropertyAssignmentException", "InvalidOperation", "System.Management.Automation.RuntimeException", null);
            throw RtError(at, "The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.", "PropertyNotFound", "InvalidOperation", "System.Management.Automation.RuntimeException", null);
        }

        public object Index(object target, object idx, PsAst at)
        {
            target = PsConv.Base(target); idx = PsConv.Base(idx);
            if (target == null) throw RtError(at, "Cannot index into a null array.", "NullArray", "InvalidOperation", null, null);
            var d = target as IDictionary;
            if (d != null)
            {
                if (PsConv.IsCollection(idx)) return PsConv.Items(idx).Select(k => d.Contains(k) ? d[k] : DictFind(d, k)).ToArray();
                return d.Contains(idx ?? "") ? d[idx] : DictFind(d, idx);
            }
            if (target is string)
            {
                string s = (string)target;
                if (PsConv.IsCollection(idx)) return PsConv.Items(idx).Select(i => { int n = PsConv.ToInt(i); if (n < 0) n += s.Length; return n >= 0 && n < s.Length ? (object)s[n] : null; }).Where(x => x != null).ToArray();
                int k = PsConv.ToInt(idx); if (k < 0) k += s.Length;
                return k >= 0 && k < s.Length ? (object)s[k] : null;
            }
            if (target is PsObj && !PsConv.IsCollection(target))
            {
                var o = (PsObj)target;
                if (o.Methods.ContainsKey("get_Item")) return o.Methods["get_Item"].Fn(o, new[] { idx });
                if (idx is int && ((int)idx == 0 || (int)idx == -1)) return o;
                throw RtError(at, "Unable to index into an object of type " + o.TypeName + ".", "CannotIndex", "InvalidOperation", null, null);
            }
            var list = target as IList;
            if (list == null && PsConv.IsCollection(target)) list = PsConv.List(target);
            if (list != null)
            {
                if (PsConv.IsCollection(idx)) { var res = new List<object>(); foreach (var i in PsConv.Items(idx)) { int n = PsConv.ToInt(i); if (n < 0) n += list.Count; if (n >= 0 && n < list.Count) res.Add(list[n]); } return res.ToArray(); }
                int k = PsConv.ToInt(idx); if (k < 0) k += list.Count;
                return k >= 0 && k < list.Count ? list[k] : null;
            }
            if (idx is int && ((int)idx == 0 || (int)idx == -1)) return target;
            if (idx is int) return null;
            throw RtError(at, "Unable to index into an object of type " + PsTypes.TypeNameOf(target) + ".", "CannotIndex", "InvalidOperation", null, null);
        }
        static object DictFind(IDictionary d, object k)
        {
            // keys of other types: 1 vs "1"
            foreach (DictionaryEntry e in d) if (PsConv.Eq(e.Key, k, false)) return e.Value;
            return null;
        }

        void SetIndex(object target, object idx, object v, PsAst at)
        {
            if (v is AutoNull) v = null;
            if (target == null) throw RtError(at, "Cannot index into a null array.", "NullArray", "InvalidOperation", null, null);
            var d = target as IDictionary;
            if (d != null) { d[idx] = v; return; }
            var list = target as IList;
            if (list != null)
            {
                int k = PsConv.ToInt(idx); if (k < 0) k += list.Count;
                if (k < 0 || k >= list.Count) throw RtError(at, "Index was outside the bounds of the array.", "IndexOutOfRange", "InvalidOperation", "System.IndexOutOfRangeException", null);
                if (target is Array && target.GetType().GetElementType() != typeof(object)) v = PsConv.To(v, PsConv.Resolve(target.GetType().GetElementType()));
                list[k] = v;
                return;
            }
            throw RtError(at, "Unable to index into an object of type " + PsTypes.TypeNameOf(target) + ".", "CannotIndex", "InvalidOperation", null, null);
        }

        public object CallMethod(object target, string name, object[] args, PsAst at, PsScope sc)
        {
            target = PsConv.Base(target);
            for (int i = 0; i < args.Length; i++) if (args[i] is AutoNull) args[i] = null;
            if (target == null) throw RtError(at, "You cannot call a method on a null-valued expression.", "InvokeMethodOnNull", "InvalidOperation", null, null);
            if (name.Equals("GetType", StringComparison.OrdinalIgnoreCase) && args.Length == 0) return PsTypeInfo.Of(target);
            if (name.Equals("ForEach", StringComparison.OrdinalIgnoreCase) && args.Length >= 1)
            {
                var res = new List<object>();
                var sb = PsConv.Base(args[0]) as PsScriptBlock;
                foreach (var x in PsConv.Items(target))
                {
                    if (sb != null) foreach (var y in InvokeBlock(sb, sc, x, args.Skip(1).ToArray(), true)) res.Add(y);
                    else if (args[0] is string) res.Add(GetMember(x, (string)args[0], at));
                }
                return new ArrayList(res);
            }
            if (name.Equals("Where", StringComparison.OrdinalIgnoreCase) && args.Length >= 1 && PsConv.Base(args[0]) is PsScriptBlock)
            {
                var sb = (PsScriptBlock)PsConv.Base(args[0]);
                string mode = args.Length > 1 ? PsConv.ToStr(args[1]) : "Default";
                int n = args.Length > 2 ? PsConv.ToInt(args[2]) : 0;
                var all = PsConv.Items(target).ToList();
                var hits = new List<object>();
                foreach (var x in all) if (PsConv.ToBool(InvokeBlockValue(sb, sc, x, null, true))) hits.Add(x);
                if (mode.Equals("First", StringComparison.OrdinalIgnoreCase)) hits = hits.Take(n > 0 ? n : 1).ToList();
                else if (mode.Equals("Last", StringComparison.OrdinalIgnoreCase)) hits = hits.Skip(Math.Max(0, hits.Count - (n > 0 ? n : 1))).ToList();
                else if (mode.Equals("SkipUntil", StringComparison.OrdinalIgnoreCase)) { int i = all.FindIndex(x => hits.Contains(x)); hits = i < 0 ? new List<object>() : all.Skip(i).ToList(); }
                else if (mode.Equals("Until", StringComparison.OrdinalIgnoreCase)) { int i = all.FindIndex(x => hits.Contains(x)); hits = i < 0 ? all : all.Take(i).ToList(); }
                return new ArrayList(hits);
            }
            var notes = PsNotes.Get(target);
            if (notes != null && notes.Methods.ContainsKey(name)) return RunMethod(notes.Methods[name], target as PsObj ?? notes, args, sc, target);
            bool found;
            var r = PsMembers.Call(this, target, name, args, at, sc, out found);
            if (found) return r;
            if (PsConv.IsCollection(target) && !(target is IList && PsMembers.ClrHasMethod(target, name)))
            {
                var res = new List<object>();
                foreach (var x in (IEnumerable)target) { var y = CallMethod(x, name, args, at, sc); if (!(y is AutoNull) && y != null) res.Add(y); }
                return PsConv.Collapse(res);
            }
            throw RtError(at, "Method invocation failed because [" + PsTypes.TypeNameOf(target) + "] does not contain a method named '" + name + "'.", "MethodNotFound", "InvalidOperation", null, null);
        }

        public object RunMethod(PsMethod m, PsObj self, object[] args, PsScope sc, object thisObj)
        {
            if (m.Script != null) return PsConv.Collapse(InvokeBlock(m.Script, Global, null, args, false, thisObj ?? self));
            return m.Fn(self, args);
        }

        object StaticMember(PsType t, string name, PsAst at)
        {
            bool found;
            var v = PsStatics.Get(this, t, name, out found);
            if (found) return v;
            throw RtError(at, StaticMissing(t, name), "PropertyNotFound", "InvalidOperation", null, null);
        }
        object StaticCall(PsType t, string name, object[] args, PsAst at, PsScope sc)
        {
            for (int i = 0; i < args.Length; i++) if (args[i] is AutoNull) args[i] = null;
            bool found;
            var v = PsStatics.Call(this, t, name, args, at, sc, out found);
            if (found) return v;
            throw RtError(at, "Method invocation failed because [" + t.FullName + "] does not contain a method named '" + name + "'.", "MethodNotFound", "InvalidOperation", null, null);
        }
        static string StaticMissing(PsType t, string name) { return "The property '" + name + "' cannot be found on type [" + t.FullName + "]."; }
    }

    public class PsRegexError : Exception { public PsRegexError(string p, string m) : base(m) { } }

    /// <summary>Arithmetic with PowerShell's rules (the left side decides: "1" + 2 is "12", 1 + "2" is 3).</summary>
    public static class PsOps
    {
        public static object Add(object l, object r)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            if (l == null) { if (PsConv.IsCollection(r)) return PsConv.List(r).ToArray(); return r; }
            if (l is string) return (string)l + PsConv.ToStr(r);
            if (l is char && r is string) return l.ToString() + (string)r;
            if (PsConv.IsCollection(l))
            {
                var res = PsConv.List(l);
                if (PsConv.IsCollection(r)) res.AddRange(PsConv.List(r)); else res.Add(r);
                return res.ToArray();
            }
            if (l is IDictionary)
            {
                if (!(r is IDictionary)) throw new PsOpError("A hash table can only be added to another hash table.", "AddHashTableToNonHashTable");
                var h = l is OrderedDictionary ? (IDictionary)PsConv.NewOrdered() : PsConv.NewHashtable();
                foreach (DictionaryEntry e in (IDictionary)l) h[e.Key] = e.Value;
                foreach (DictionaryEntry e in (IDictionary)r)
                {
                    if (h.Contains(e.Key)) throw new PsOpError("Item has already been added. Key in dictionary: '" + PsConv.ToStr(e.Key) + "'  Key being added: '" + PsConv.ToStr(e.Key) + "'", "System.ArgumentException");
                    h[e.Key] = e.Value;
                }
                return h;
            }
            if (l is DateTime)
            {
                if (r is TimeSpan) return ((DateTime)l).Add((TimeSpan)r);
                if (PsConv.IsNumber(r)) return ((DateTime)l).AddTicks(PsConv.ToLong(r));
                throw new PsOpError("Method invocation failed because [System.DateTime] does not contain a method named 'op_Addition'.", "MethodNotFound");
            }
            if (l is TimeSpan && r is TimeSpan) return ((TimeSpan)l).Add((TimeSpan)r);
            if (l is TimeSpan && r is DateTime) return ((DateTime)r).Add((TimeSpan)l);
            if (l is PsObj) throw new PsOpError("Method invocation failed because [" + ((PsObj)l).TypeName + "] does not contain a method named 'op_Addition'.", "MethodNotFound");
            return Num(l, r, "+");
        }

        public static object Sub(object l, object r)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            if (l is DateTime)
            {
                if (r is DateTime) return (DateTime)l - (DateTime)r;
                if (r is TimeSpan) return ((DateTime)l) - (TimeSpan)r;
            }
            if (l is TimeSpan && r is TimeSpan) return (TimeSpan)l - (TimeSpan)r;
            if (l is string || PsConv.IsCollection(l) || l is IDictionary || l is PsObj)
            {
                if (l is string && PsConv.IsNumber(r)) { object n; if (PsConv.TryNumber((string)l, out n)) return Num(n, r, "-"); }
                throw new PsOpError("Method invocation failed because [" + PsTypes.TypeNameOf(l) + "] does not contain a method named 'op_Subtraction'.", "MethodNotFound");
            }
            return Num(l ?? 0, r, "-");
        }

        public static object Mul(object l, object r)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            if (l is string) { int n = PsConv.ToInt(r); var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append((string)l); return sb.ToString(); }
            if (PsConv.IsCollection(l)) { int n = PsConv.ToInt(r); var src = PsConv.List(l); var res = new List<object>(); for (int i = 0; i < n; i++) res.AddRange(src); return res.ToArray(); }
            if (l is IDictionary || l is PsObj) throw new PsOpError("Method invocation failed because [" + PsTypes.TypeNameOf(l) + "] does not contain a method named 'op_Multiply'.", "MethodNotFound");
            return Num(l ?? 0, r, "*");
        }

        public static object Div(object l, object r)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            if (l is string || l is IDictionary || PsConv.IsCollection(l) || l is PsObj)
            {
                if (l is string) { object n; if (PsConv.TryNumber((string)l, out n)) return Div(n, r); }
                throw new PsOpError("Method invocation failed because [" + PsTypes.TypeNameOf(l) + "] does not contain a method named 'op_Division'.", "MethodNotFound");
            }
            if (l is TimeSpan && PsConv.IsNumber(r)) return TimeSpan.FromTicks((long)(((TimeSpan)l).Ticks / PsConv.ToDouble(r)));
            return Num(l ?? 0, r, "/");
        }
        public static object Mod(object l, object r) { return Num(PsConv.Base(l) ?? 0, PsConv.Base(r), "%"); }

        static object Num(object l, object r, string op)
        {
            var a = PsConv.ToNumber(l);
            var b = PsConv.ToNumber(r);
            if (a is decimal || b is decimal)
            {
                decimal x = Convert.ToDecimal(a, PsConv.Inv), y = Convert.ToDecimal(b, PsConv.Inv);
                switch (op) { case "+": return x + y; case "-": return x - y; case "*": return x * y; case "/": if (y == 0) throw new DivideByZeroException(); return x / y; case "%": if (y == 0) throw new DivideByZeroException(); return x % y; }
            }
            if (a is double || b is double || a is float || b is float)
            {
                double x = Convert.ToDouble(a, PsConv.Inv), y = Convert.ToDouble(b, PsConv.Inv);
                switch (op) { case "+": return x + y; case "-": return x - y; case "*": return x * y; case "/": if (y == 0) throw new DivideByZeroException(); return x / y; case "%": if (y == 0) throw new DivideByZeroException(); return x % y; }
            }
            long lx = Convert.ToInt64(a, PsConv.Inv), ly = Convert.ToInt64(b, PsConv.Inv);
            bool wasInt = (a is int || a is byte || a is short || a is char) && (b is int || b is byte || b is short || b is char);
            switch (op)
            {
                case "+": { long s; try { s = checked(lx + ly); } catch (OverflowException) { return (double)lx + ly; } return Narrow(s, wasInt); }
                case "-": { long s; try { s = checked(lx - ly); } catch (OverflowException) { return (double)lx - ly; } return Narrow(s, wasInt); }
                case "*": { long s; try { s = checked(lx * ly); } catch (OverflowException) { return (double)lx * ly; } return Narrow(s, wasInt); }
                case "/":
                    if (ly == 0) throw new DivideByZeroException();
                    if (lx % ly == 0) return Narrow(lx / ly, wasInt);
                    return (double)lx / ly;
                case "%": if (ly == 0) throw new DivideByZeroException(); return Narrow(lx % ly, wasInt);
            }
            return null;
        }
        static object Narrow(long v, bool wasInt) { if (wasInt) { if (v >= int.MinValue && v <= int.MaxValue) return (int)v; return (double)v; } return v; }

        public static object Bits(object l, object r, string op)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            var le = l as PsEnum;
            long a = PsConv.ToLong(l), b = PsConv.ToLong(r is PsEnum ? (object)((PsEnum)r).Value : r);
            long v = 0;
            switch (op) { case "and": v = a & b; break; case "or": v = a | b; break; case "xor": v = a ^ b; break; case "shl": v = a << (int)b; break; case "shr": v = a >> (int)b; break; }
            if (le != null && (op == "and" || op == "or" || op == "xor")) return le.Type.Of(v);
            if (l is int && (r is int || r is PsEnum) && v >= int.MinValue && v <= int.MaxValue) return (int)v;
            return v >= int.MinValue && v <= int.MaxValue && !(l is long) ? (object)(int)v : v;
        }

        public static object Range(object l, object r)
        {
            l = PsConv.Base(l); r = PsConv.Base(r);
            if (l is string && ((string)l).Length == 1 && !char.IsDigit(((string)l)[0])) throw new PsConvertError(l, "System.Int32", "Input string was not in a correct format.");
            int a = PsConv.ToInt(l), b = PsConv.ToInt(r);
            int n = Math.Abs(b - a) + 1;
            if (n > 50000000) throw new PsOpError("The range is too big.", "RangeTooBig");
            var res = new object[n];
            int step = a <= b ? 1 : -1;
            for (int i = 0; i < n; i++) res[i] = a + i * step;
            return res;
        }
    }

    public class PsOpError : Exception { public string Id; public PsOpError(string m, string id) : base(m) { Id = id; } }

    /// <summary>The hook the DSC part fills in (Configuration keyword, Node and resource blocks).</summary>
    public static class PsDscHook
    {
        public static Action<PsRunspace, ConfigAst, PsScope> DefineFn;
        public static Action<PsRunspace, PsAst, PsScope, Streams> StatementFn;
        public static Func<PsRunspace, PsFunction, CommandAst, PsScope, List<PsRunspace.ArgItem>, int, PsProc> InvokeFn;
        public static void Define(PsRunspace r, ConfigAst a, PsScope sc)
        {
            if (DefineFn != null) { DefineFn(r, a, sc); return; }
            sc.Funcs[a.Name] = new PsFunction { Name = a.Name, Ast = a.Body, IsConfiguration = true };
        }
        public static void Statement(PsRunspace r, PsAst a, PsScope sc, Streams st)
        {
            if (StatementFn != null) { StatementFn(r, a, sc, st); return; }
            throw r.RtError(a, "Node and resource blocks can only be used inside a Configuration.", "InvalidDscKeyword", "ParserError", null, null);
        }
    }
}
