// Cert Lab - commands in the simulated PowerShell: cmdlet definitions (parameters with types, positions, pipeline
// binding ByValue / ByPropertyName, parameter sets, validation), command lookup (alias, function, cmdlet, script,
// application, module auto-loading), parameter binding with PowerShell's error messages, and the pipeline: every
// command is a processor with begin / process / end, objects stream through one at a time.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class ParamDef
    {
        public string Name = "", Type = "object";
        public List<string> Aliases = new List<string>();
        public int Position = -1;
        public bool Mandatory, Pipe, PipeProp, Rest;
        public List<string> Sets;                 // null: every parameter set
        public List<string> ValidateSet;
        public double? Min, Max;
        public string Pattern, Help = "";
        public bool NotNullOrEmpty, NotNull;
        public PsScriptBlock ValidateScript;
        public int? MinLen, MaxLen, MinCount, MaxCount;
        public ParamAst Ast;                      // script parameters: the default value
        public bool IsSwitch { get { return Type.Equals("switch", StringComparison.OrdinalIgnoreCase) || Type.Equals("System.Management.Automation.SwitchParameter", StringComparison.OrdinalIgnoreCase); } }
        public bool IsArray { get { return Type.EndsWith("[]", StringComparison.Ordinal); } }
        public bool InSet(string set) { return Sets == null || set == null || Sets.Contains(set, StringComparer.OrdinalIgnoreCase); }
        public string DisplayType
        {
            get
            {
                string t = Type;
                bool arr = t.EndsWith("[]", StringComparison.Ordinal); if (arr) t = t.Substring(0, t.Length - 2);
                string full = PsCmdlets.TypeFull(t);
                int i = full.LastIndexOf('.');
                string sh = i < 0 ? full : full.Substring(i + 1);
                if (sh == "Object" && t.Equals("object", StringComparison.OrdinalIgnoreCase)) sh = "Object";
                return sh + (arr ? "[]" : "");
            }
        }
    }

    public class CmdletDef
    {
        public string Name = "", Module = "Microsoft.PowerShell.Utility", Synopsis = "";
        public List<ParamDef> Params = new List<ParamDef>();
        public bool ShouldProcess; public string ConfirmImpact = "Medium";
        public bool IsFunction;   // a CIM-based (CDXML) or script command that 5.1 lists as a Function (Get-NetIPAddress, Get-ScheduledTask)
        public Action<CmdCtx> Begin, End;
        public Action<CmdCtx, object> Process;
        public string DefaultSet;
        public string FqClass;                    // Microsoft.PowerShell.Commands.GetItemCommand
        public Func<PsMachine, bool> Available;   // server-only cmdlets
        public string Verb { get { int i = Name.IndexOf('-'); return i > 0 ? Name.Substring(0, i) : Name; } }
        public string Noun { get { int i = Name.IndexOf('-'); return i > 0 ? Name.Substring(i + 1) : ""; } }
        public string FullClass { get { return FqClass ?? "Microsoft.PowerShell.Commands." + Verb + Noun.Replace("-", "") + "Command"; } }
        public bool TakesPipeline { get { return Params.Any(p => p.Pipe || p.PipeProp); } }
        public ParamDef P(string name) { return Params.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
    }

    /// <summary>What a cmdlet's code sees: bound parameters, the input object, the streams, the machine.</summary>
    public class CmdCtx
    {
        public PsRunspace R; public PsScope Scope; public Streams St; public CmdletDef Def; public CommandAst Ast;
        public readonly Dictionary<string, object> Args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public object Input; public bool HasInput, First = true; public int Position = 1;
        public readonly Dictionary<string, object> Data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public readonly List<object> Items = new List<object>();
        public string ErrorAction, WarningAction, InformationAction, ErrorVariable, WarningVariable, OutVariable, PipelineVariable;
        public bool VerboseOn, DebugOn, WhatIf, ConfirmOn, YesToAll, NoToAll;
        public string ActiveSet;
        public ArrayList OutVar, ErrVar;
        public PsMachine M { get { return R.M; } }

        public void Out(object o) { if (o is AutoNull) return; St.Out(o); }
        public void OutAll(IEnumerable<object> l) { foreach (var o in l) Out(o); }
        public bool Has(string n) { return Args.ContainsKey(n); }
        public object Get(string n) { object v; return Args.TryGetValue(n, out v) ? PsConv.Base(v) : null; }
        public string Str(string n) { var v = Get(n); return v == null ? null : PsConv.ToStr(v); }
        public string Str(string n, string def) { return Str(n) ?? def; }
        public string[] Strs(string n) { var v = Get(n); if (v == null) return null; return PsConv.Items(v).Select(x => PsConv.ToStr(x)).ToArray(); }
        public List<object> Objs(string n) { var v = Get(n); if (v == null) return new List<object>(); return PsConv.Items(v).ToList(); }
        public int Int(string n, int def) { var v = Get(n); return v == null ? def : PsConv.ToInt(v); }
        public long Long(string n, long def) { var v = Get(n); return v == null ? def : PsConv.ToLong(v); }
        public bool Sw(string n) { var v = Get(n); return v != null && PsConv.ToBool(v); }
        public PsScriptBlock Block(string n) { return Get(n) as PsScriptBlock; }
        public IDictionary Hash(string n) { return Get(n) as IDictionary; }

        public PsErrorRecord MakeError(string msg, string id, string cat, object target, string exType)
        {
            var r = R.MakeError(Ast, msg, id, cat, exType, target);
            r.CommandName = Def.Name; r.FullCommandName = Def.FullClass;
            return r;
        }
        /// <summary>A non-terminating error (the command goes on with the next item).</summary>
        public void Error(string msg, string id, string cat, object target, string exType) { WriteError(MakeError(msg, id, cat, target, exType)); }
        public void WriteError(PsErrorRecord r)
        {
            if (r.CommandName.Length == 0) { r.CommandName = Def.Name; r.FullCommandName = Def.FullClass; }
            string ea = ErrorAction ?? R.Pref(Scope, "ErrorActionPreference");
            if (ErrVar != null) ErrVar.Add(r);
            // -ErrorVariable of the functions this command runs inside
            for (var s = Scope; s != null; s = s.Parent) { PsVariable ev; if (s.Vars.TryGetValue("__ErrVar", out ev) && ev.Value is ArrayList && !((ArrayList)ev.Value).Contains(r)) ((ArrayList)ev.Value).Add(r); }
            R.CurrentOk = false;
            switch (ea)
            {
                case "Stop": R.AddError(r); throw new PsRuntimeException(r, true);
                case "Ignore": return;
                case "SilentlyContinue": R.AddError(r); return;
                default: R.AddError(r); St.Err(r); return;
            }
        }
        /// <summary>A terminating error (ends this command and the statement): throw c.Fail(...).</summary>
        public PsRuntimeException Fail(string msg, string id, string cat, object target, string exType)
        {
            var r = MakeError(msg, id, cat, target, exType);
            if (ErrVar != null) ErrVar.Add(r);
            return new PsRuntimeException(r, (ErrorAction ?? R.Pref(Scope, "ErrorActionPreference")) == "Stop");
        }
        public void Warn(string s)
        {
            string wa = WarningAction ?? R.Pref(Scope, "WarningPreference");
            if (wa == "SilentlyContinue" || wa == "Ignore") return;
            if (wa == "Stop") throw new PsRuntimeException(MakeError("The running command stopped because the preference variable \"WarningPreference\" or common parameter is set to Stop: " + s, "ActionPreferenceStop", "OperationStopped", null, "System.Management.Automation.ActionPreferenceStopException"), true);
            St.Warn(s);
        }
        public void Verbose(string s) { if (VerboseOn || R.Pref(Scope, "VerbosePreference") == "Continue") St.Verbose(s); }
        public void Debug(string s) { if (DebugOn || R.Pref(Scope, "DebugPreference") == "Continue") St.Debug(s); }
        public void Host(string s) { St.Info(new PsHostText { Text = s }); }

        /// <summary>-WhatIf / -Confirm for a change ("What if: Performing the operation ...").</summary>
        public bool ShouldProcess(string target, string action)
        {
            bool whatIf = WhatIf || (!Args.ContainsKey("__NoWhatIfPref") && PsConv.ToBool(R.GetVar("WhatIfPreference", Scope)) && !Data.ContainsKey("WhatIfFalse"));
            if (whatIf) { St.Info(new PsHostText { Text = "What if: Performing the operation \"" + action + "\" on target \"" + target + "\"." }); return false; }
            bool ask = ConfirmOn || ImpactAsks();
            if (!ask || YesToAll) return !NoToAll;
            if (NoToAll) return false;
            string a = R.AskChoice("Confirm", "Are you sure you want to perform this action?\nPerforming the operation \"" + action + "\" on target \"" + target + "\".", "Y");
            if (a == "A") { YesToAll = true; return true; }
            if (a == "L") { NoToAll = true; return false; }
            return a == "Y";
        }
        bool ImpactAsks()
        {
            string pref = R.Pref(Scope, "ConfirmPreference");
            if (Data.ContainsKey("ConfirmFalse")) return false;
            int lvl = Lvl(pref), mine = Lvl(Def.ConfirmImpact);
            return lvl > 0 && mine >= lvl;
        }
        static int Lvl(string s) { switch ((s ?? "").ToLowerInvariant()) { case "low": return 1; case "medium": return 2; case "high": return 3; } return 0; }
        public string Prompt(string text, bool secret) { return R.Host == null ? null : R.Host.ReadLine(text, secret); }
    }

    /// <summary>Text Write-Host wrote (the information stream).</summary>
    public class PsHostText { public string Text = ""; public bool NoNewline; public override string ToString() { return Text; } }

    public class PsCommandInfo
    {
        public string Kind = "Cmdlet", Name = "", Path, Module = "", Definition = "", Version = "";
        public CmdletDef Def; public PsFunction Func; public string AliasTarget;
        public override string ToString() { return Kind == "Alias" ? Name + " -> " + AliasTarget : Name; }
    }

    public partial class PsCmdlets
    {
        static readonly Dictionary<string, CmdletDef> all = new Dictionary<string, CmdletDef>(StringComparer.OrdinalIgnoreCase);
        static bool done;
        static readonly object gate = new object();

        public static Dictionary<string, CmdletDef> All { get { Init(); return all; } }
        public static CmdletDef Get(string name) { Init(); CmdletDef d; return all.TryGetValue(name, out d) ? d : null; }

        public static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                PsEnums.Init();
                RegisterCore();
                RegisterFormat();
                RegisterProviders();
                RegisterAdmin();
                RegisterNet();
                RegisterRemoting();
                RegisterModules();
                RegisterSecurity();
                RegisterDsc();
                RegisterMore();
                RegisterCore2();
                RegisterHelp();
                RegisterJobs();
                RegisterData();
                RegisterEndpoint();
                RegisterWinServer();   // the AZ-800 course (PsWs*.cs)
                RegisterWiring();   // hooks between the parts (PsWiring.cs)
            }
        }
        static partial void RegisterCore();
        static partial void RegisterFormat();
        static partial void RegisterProviders();
        static partial void RegisterAdmin();
        static partial void RegisterNet();
        static partial void RegisterRemoting();
        static partial void RegisterModules();
        static partial void RegisterSecurity();
        static partial void RegisterDsc();
        static partial void RegisterMore();
        static partial void RegisterCore2();
        static partial void RegisterHelp();
        static partial void RegisterJobs();
        static partial void RegisterData();
        static partial void RegisterWiring();
        static partial void RegisterEndpoint();
        static partial void RegisterWinServer();

        /// <summary>Defines a cmdlet. Parameter spec: "Name:string[]@0 ! pipe prop alias=A,B set=X|Y ps=Set1,Set2 rest; Force:switch".</summary>
        public static CmdletDef Add(string name, string module, string spec, Action<CmdCtx, object> process)
        {
            var d = new CmdletDef { Name = name, Module = module, Process = process };
            d.Params = ParseSpec(spec);
            all[name] = d;
            return d;
        }
        public static CmdletDef Add(string name, string module, string spec, Action<CmdCtx> begin, Action<CmdCtx, object> process, Action<CmdCtx> end)
        {
            var d = Add(name, module, spec, process);
            d.Begin = begin; d.End = end;
            return d;
        }

        public static List<ParamDef> ParseSpec(string spec)
        {
            var l = new List<ParamDef>();
            if (string.IsNullOrEmpty(spec)) return l;
            foreach (var raw in spec.Split(';'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var words = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var head = words[0];
                var pd = new ParamDef();
                int colon = head.IndexOf(':');
                string nt = colon < 0 ? head : head.Substring(0, colon);
                string type = colon < 0 ? "object" : head.Substring(colon + 1);
                int at = type.IndexOf('@');
                if (at >= 0) { pd.Position = int.Parse(type.Substring(at + 1), CultureInfo.InvariantCulture); type = type.Substring(0, at); }
                int at2 = nt.IndexOf('@');
                if (at2 >= 0) { pd.Position = int.Parse(nt.Substring(at2 + 1), CultureInfo.InvariantCulture); nt = nt.Substring(0, at2); }
                pd.Name = nt; pd.Type = type.Length == 0 ? "object" : type;
                foreach (var w in words.Skip(1))
                {
                    if (w == "!") pd.Mandatory = true;
                    else if (w == "pipe") pd.Pipe = true;
                    else if (w == "prop") pd.PipeProp = true;
                    else if (w == "rest") pd.Rest = true;
                    else if (w.StartsWith("@", StringComparison.Ordinal)) pd.Position = int.Parse(w.Substring(1), CultureInfo.InvariantCulture);
                    else if (w.StartsWith("alias=", StringComparison.Ordinal)) pd.Aliases.AddRange(w.Substring(6).Split(','));
                    else if (w.StartsWith("set=", StringComparison.Ordinal)) pd.ValidateSet = w.Substring(4).Split('|').ToList();
                    else if (w.StartsWith("ps=", StringComparison.Ordinal)) pd.Sets = w.Substring(3).Split(',').ToList();
                    else if (w.StartsWith("range=", StringComparison.Ordinal)) { var r = w.Substring(6).Split('~'); pd.Min = double.Parse(r[0], CultureInfo.InvariantCulture); pd.Max = double.Parse(r[1], CultureInfo.InvariantCulture); }
                    else if (w == "notnull") pd.NotNullOrEmpty = true;
                }
                l.Add(pd);
            }
            return l;
        }

        static readonly Dictionary<string, string> typeFull = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "string", "System.String" }, { "int", "System.Int32" }, { "long", "System.Int64" }, { "double", "System.Double" }, { "switch", "System.Management.Automation.SwitchParameter" },
            { "bool", "System.Boolean" }, { "object", "System.Object" }, { "psobject", "System.Management.Automation.PSObject" }, { "scriptblock", "System.Management.Automation.ScriptBlock" }, { "hashtable", "System.Collections.Hashtable" },
            { "datetime", "System.DateTime" }, { "timespan", "System.TimeSpan" }, { "securestring", "System.Security.SecureString" }, { "pscredential", "System.Management.Automation.PSCredential" },
            { "ServiceController", "System.ServiceProcess.ServiceController" }, { "Process", "System.Diagnostics.Process" }, { "uint32", "System.UInt32" }, { "char", "System.Char" },
            { "PSSession", "System.Management.Automation.Runspaces.PSSession" }, { "Job", "System.Management.Automation.Job" }, { "CimSession", "Microsoft.Management.Infrastructure.CimSession" },
            { "CimInstance", "Microsoft.Management.Infrastructure.CimInstance" }, { "LocalUser", "Microsoft.PowerShell.Commands.LocalUser" }, { "LocalGroup", "Microsoft.PowerShell.Commands.LocalGroup" },
            { "LocalPrincipal", "Microsoft.PowerShell.Commands.LocalPrincipal" }, { "PSModuleInfo", "System.Management.Automation.PSModuleInfo" }, { "Encoding", "System.Text.Encoding" }, { "version", "System.Version" },
            { "ScheduledTask", "Microsoft.Management.Infrastructure.CimInstance#MSFT_ScheduledTask" }, { "uint16", "System.UInt16" }, { "byte", "System.Byte" }, { "decimal", "System.Decimal" },
            { "ActionPreference", "System.Management.Automation.ActionPreference" }, { "ExecutionPolicy", "Microsoft.PowerShell.ExecutionPolicy" }, { "ExecutionPolicyScope", "Microsoft.PowerShell.ExecutionPolicyScope" },
            { "ConsoleColor", "System.ConsoleColor" }, { "ServiceStartupType", "Microsoft.PowerShell.Commands.ServiceStartupType" }, { "ServiceControllerStatus", "System.ServiceProcess.ServiceControllerStatus" },
            { "FileAttributes", "System.IO.FileAttributes" }, { "RegistryValueKind", "Microsoft.Win32.RegistryValueKind" }, { "CommandTypes", "System.Management.Automation.CommandTypes" },
            { "PSMemberTypes", "System.Management.Automation.PSMemberTypes" }, { "type", "System.Type" }, { "ipaddress", "System.Net.IPAddress" }, { "X509Certificate2", "System.Security.Cryptography.X509Certificates.X509Certificate2" },
            { "regex", "System.Text.RegularExpressions.Regex" }, { "dictionary", "System.Collections.IDictionary" },
        };
        public static string TypeFull(string t) { string f; if (typeFull.TryGetValue(t, out f)) return f; var r = PsTypes.FullName(t); return r ?? t; }
        public static void AddType(string shortName, string full) { typeFull[shortName] = full; }
    }

    public partial class PsRunspace
    {
        public readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> AliasDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PsFunction FindFunction(string name, PsScope sc)
        {
            for (var s = sc; s != null; s = s.Parent) { PsFunction f; if (s.Funcs.TryGetValue(name, out f)) return f; }
            // module functions are visible through the global scope
            foreach (var m in LoadedModules) { PsFunction f; if (m.Exported.TryGetValue(name, out f)) return f; }
            return null;
        }

        /// <summary>Finds a command the way PowerShell does: alias, function, cmdlet, then scripts and programs.</summary>
        public PsCommandInfo Resolve(string name, PsScope sc, bool autoload)
        {
            var ci = Resolve2(name, sc, autoload);
            if (ci != null && AllowedCommands != null) { string cn = ci.Kind == "Alias" ? (ci.Def != null ? ci.Def.Name : ci.AliasTarget) : ci.Name; if (!AllowedCommands.Contains(cn) && !AllowedCommands.Any(p => PsConv.HasWildcard(p) && PsConv.Like(cn, p, false))) return null; }
            return ci;
        }
        PsCommandInfo Resolve2(string name, PsScope sc, bool autoload)
        {
            string n = name;
            int bs = n.IndexOf('\\');
            string moduleQ = null;
            if (bs > 0 && bs < n.Length - 1 && !n.Contains(":") && !n.StartsWith(".", StringComparison.Ordinal) && n.IndexOf('\\', bs + 1) < 0 && !n.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && !n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                moduleQ = n.Substring(0, bs); n = n.Substring(bs + 1);
            }
            if (moduleQ == null)
            {
                string target;
                if (Aliases.TryGetValue(n, out target))
                {
                    var t = Resolve(target, sc, autoload);
                    if (t != null) return new PsCommandInfo { Kind = "Alias", Name = n, AliasTarget = target, Def = t.Def, Func = t.Func, Path = t.Path, Module = t.Module, Definition = target };
                    return new PsCommandInfo { Kind = "Alias", Name = n, AliasTarget = target, Definition = target };
                }
                var f = FindFunction(n, sc);
                if (f != null) return new PsCommandInfo { Kind = f.IsConfiguration ? "Configuration" : f.Filter ? "Filter" : "Function", Name = f.Name, Func = f, Module = f.Module == null ? "" : f.Module.Name, Version = f.Module == null ? "" : f.Module.Version.ToString(), Definition = f.Definition };
            }
            var d = PsCmdlets.Get(n);
            if (d != null && (d.Available == null || d.Available(M)) && (moduleQ == null || d.Module.Equals(moduleQ, StringComparison.OrdinalIgnoreCase)))
            {
                if (autoload) EnsureModuleLoaded(d.Module);
                return new PsCommandInfo { Kind = d.IsFunction ? "Function" : "Cmdlet", Name = d.Name, Def = d, Module = d.Module, Version = ModuleVersion(d.Module) };
            }
            if (moduleQ == null && autoload)
            {
                var fm = AutoloadFunction(n);
                if (fm != null) return Resolve(name, sc, false);
            }
            // a path to a script or program
            if (n.Contains("\\") || n.Contains("/") || n.StartsWith(".", StringComparison.Ordinal))
            {
                string full = FullFsPath(n, CurrentFsDir);
                return CommandAtPath(full, n);
            }
            // programs and scripts on the PATH
            foreach (var dir in (GetEnv("Path") as string ?? "").Split(';').Where(x => x.Length > 0))
            {
                foreach (var ext in new[] { "", ".ps1", ".exe", ".cmd", ".bat", ".com" })
                {
                    string cand = dir.TrimEnd('\\') + "\\" + n + (n.Contains(".") && ext.Length > 0 && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? "" : ext);
                    if (ext.Length == 0 && !n.Contains(".")) continue;
                    var ci = CommandAtPath(cand, n);
                    if (ci != null) return ci;
                }
            }
            string bare = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
            var nat = PsNatives.Get(bare);
            if (nat != null && (nat.Available == null || nat.Available(M))) return new PsCommandInfo { Kind = "Application", Name = bare + ".exe", Path = nat.Path, Definition = nat.Path };
            return null;
        }

        PsCommandInfo CommandAtPath(string full, string typed)
        {
            if (full == null) return null;
            var node = FsNode(full);
            if (node == null || node.IsDir)
            {
                // a program in System32 named by its path
                string fn = full.Substring(full.LastIndexOf('\\') + 1);
                if (fn.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var nat = PsNatives.Get(fn.Substring(0, fn.Length - 4));
                    if (nat != null && nat.Path.Equals(full, StringComparison.OrdinalIgnoreCase)) return new PsCommandInfo { Kind = "Application", Name = fn, Path = full, Definition = full };
                }
                return null;
            }
            string ext = full.Substring(full.LastIndexOf('.') < 0 ? full.Length : full.LastIndexOf('.')).ToLowerInvariant();
            if (ext == ".ps1") return new PsCommandInfo { Kind = "ExternalScript", Name = full.Substring(full.LastIndexOf('\\') + 1), Path = full, Definition = full };
            if (ext == ".exe" || ext == ".cmd" || ext == ".bat")
            {
                string fn = full.Substring(full.LastIndexOf('\\') + 1);
                return new PsCommandInfo { Kind = "Application", Name = fn, Path = full, Definition = full };
            }
            return null;
        }

        public string CommandNotFoundText(string name)
        {
            var msg = "The term '" + name + "' is not recognized as the name of a cmdlet, function, script file, or operable program. Check the spelling of the name, or if a path was included, verify that the path is correct and try again.";
            return msg;
        }

        // ------------------------------------------------------------ the pipeline
        public void RunPipeline(PsAst a, PsScope sc, Streams st)
        {
            var elems = new List<PsAst>();
            if (a is PipelineAst) elems.AddRange(((PipelineAst)a).Elements); else elems.Add(a);
            CurrentOk = true;
            var procs = new List<PsProc>();
            var finals = new List<Action>();
            try
            {
                for (int i = 0; i < elems.Count; i++)
                {
                    var p = MakeProc(elems[i], sc, st, i + 1, elems.Count);
                    procs.Add(p);
                }
                for (int i = procs.Count - 1; i >= 0; i--)
                {
                    Action<object> sink;
                    if (i == procs.Count - 1) sink = st.Out;
                    else { var nxt = procs[i + 1]; sink = o => nxt.InputSafe(o); }
                    procs[i].Wire(sink, st, finals);
                }
                foreach (var p in procs) p.BeginSafe();
                procs[0].RunSafe();
                foreach (var p in procs) p.EndSafe();
            }
            catch (PsSelectStop) { foreach (var p in procs) { try { p.EndSafe(); } catch (PsSelectStop) { } } }
            finally
            {
                foreach (var f in finals) f();
            }
            LastOk = CurrentOk;
        }

        PsProc MakeProc(PsAst e, PsScope sc, Streams st, int pos, int count)
        {
            var ce = e as CmdExprAst;
            if (ce != null) return new ExprProc { R = this, Scope = sc, Expr = ce.Expr, Redirs = ce.Redirs, At = ce, Position = pos };
            var c = (CommandAst)e;
            string invoke = c.Invoke;
            object nameVal = null;
            if (c.NameExpr != null) nameVal = PsConv.Base(Eval(c.NameExpr, sc, st));
            var args = EvalArgs(c, sc, st);
            if (nameVal is PsScriptBlock)
            {
                var sb = (PsScriptBlock)nameVal;
                return new ScriptProc { R = this, Scope = sc, At = c, Redirs = c.Redirs, Position = pos, Block = sb.Ast, BlockModule = sb.Module, Home = sb.Home, DotSource = invoke == ".", Args = args, Name = "", ScopeKind = "local" };
            }
            if (nameVal is PsCommandInfo) return ProcFor((PsCommandInfo)nameVal, c, sc, st, args, pos, invoke, ((PsCommandInfo)nameVal).Name);
            if (nameVal is PsObj && ((PsObj)nameVal).Base is PsCommandInfo) return ProcFor((PsCommandInfo)((PsObj)nameVal).Base, c, sc, st, args, pos, invoke, ((PsCommandInfo)((PsObj)nameVal).Base).Name);
            string name = nameVal != null ? PsConv.ToStr(nameVal) : c.Name;
            if (name.Length == 0) throw RtError(c, "The expression after '&' in a pipeline element produced an object that was not valid. It must result in a command name, a script block, or a CommandInfo object.", "BadExpression", "InvalidOperation", null, null);
            var ci = Resolve(name, sc, true);
            if (ci == null)
            {
                var r = MakeError(c, CommandNotFoundText(name), "CommandNotFoundException", "ObjectNotFound", "System.Management.Automation.CommandNotFoundException", name);
                r.CommandName = name; r.ActivityOverride = "";
                // the script exists in the current folder: PowerShell suggests .\name
                var here = FsNode(FullFsPath(name, CurrentFsDir));
                if (here != null && !here.IsDir) Host_Suggest = name;
                if (IsRealCmdletNotSimulated(name)) { r.Exception.Message = name + " is a real Windows PowerShell 5.1 command, but Cert Lab's practice console does not simulate it."; r.ErrorId = "CertLabNotSimulated"; }
                throw new PsRuntimeException(r, false);
            }
            return ProcFor(ci, c, sc, st, args, pos, invoke, name);
        }
        public string Host_Suggest;

        PsProc ProcFor(PsCommandInfo ci, CommandAst c, PsScope sc, Streams st, List<ArgItem> args, int pos, string invoke, string invokedAs)
        {
            if (ci.Kind == "Alias" && ci.Def == null && ci.Func == null && ci.Path == null)
                throw new PsRuntimeException(Fix(MakeError(c, CommandNotFoundText(ci.AliasTarget), "CommandNotFoundException", "ObjectNotFound", "System.Management.Automation.CommandNotFoundException", ci.AliasTarget), ci.AliasTarget), false);
            if (ci.Def != null) return new CmdletProc(this, sc, ci.Def, c, args, pos, invokedAs);
            if (ci.Func != null)
            {
                var f = ci.Func;
                if (f.IsConfiguration && PsDscHook.InvokeFn != null) return PsDscHook.InvokeFn(this, f, c, sc, args, pos);
                return new ScriptProc { R = this, Scope = sc, At = c, Redirs = c.Redirs, Position = pos, Block = f.Ast, BlockModule = f.Module, Home = f.Home, DotSource = invoke == ".", Args = args, Name = f.Name, IsFilter = f.Filter, ScopeKind = "function", Func = f };
            }
            if (ci.Kind == "ExternalScript")
            {
                string why = ScriptBlockedReason(ci.Path);
                if (why != null)
                {
                    var r = MakeError(c, why, "UnauthorizedAccess", "SecurityError", "System.Management.Automation.PSSecurityException", null);
                    r.ActivityOverride = ""; r.Reason = "PSSecurityException"; r.TargetName = ""; r.TargetType = ""; r.CommandName = invokedAs ?? ci.Name;
                    throw new PsRuntimeException(r, false);
                }
                var text = ReadFileText(ci.Path) ?? "";
                ScriptBlockAst ast;
                try { ast = PsParser.Parse(text, ci.Path); }
                catch (PsParseError pe) { throw new PsRuntimeException(ParseErrorRecord(pe), false); }
                return new ScriptProc { R = this, Scope = sc, At = c, Redirs = c.Redirs, Position = pos, Block = ast, DotSource = invoke == ".", Args = args, Name = ci.Name, ScriptPath = ci.Path, ScopeKind = "script" };
            }
            if (ci.Kind == "Application") return new NativeProc { R = this, Scope = sc, At = c, Redirs = c.Redirs, Position = pos, Path = ci.Path, Name = ci.Name, Args = args };
            throw RtError(c, "Cannot run " + ci.Name + ".", "CannotRun", "InvalidOperation", null, null);
        }
        PsErrorRecord Fix(PsErrorRecord r, string name) { r.CommandName = name; r.ActivityOverride = ""; return r; }

        public PsErrorRecord ParseErrorRecord(PsParseError pe)
        {
            var src = new PsSource { Text = pe.Text, File = pe.File };
            var at = new ConstAst { Pos = pe.Pos, End = pe.Pos + Math.Max(1, pe.Len), Src = src };
            var r = MakeError(at, pe.Message, pe.Id, "ParserError", "System.Management.Automation.ParseException", null);
            r.ActivityOverride = ""; r.Reason = "ParentContainsErrorRecordException"; r.ErrorId = pe.Id;
            r.TargetName = ""; r.TargetType = "";
            r.Category = "ParserError";
            return r;
        }

        static readonly HashSet<string> realCmdlets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Out-GridView", "Show-Command", "Get-Clipboard", "Set-Clipboard", "Invoke-WebRequest", "Invoke-RestMethod", "Send-MailMessage", "Get-ComputerInfo", "Start-Transcript", "Stop-Transcript",
            "Get-Disk", "Get-Partition", "New-Partition", "Format-Volume", "Initialize-Disk", "Get-PhysicalDisk", "Get-StoragePool", "Get-Printer", "Add-Printer", "Get-PrintJob", "Get-BitLockerVolume",
            "Enable-BitLocker", "Get-MpComputerStatus", "Set-MpPreference", "Start-MpScan", "Get-AppxPackage", "Get-WindowsUpdateLog", "Get-ADUser", "New-ADUser", "Get-ADComputer", "Get-ADGroup",
            "Get-ADGroupMember", "Add-ADGroupMember", "Get-GPO", "New-GPO", "Get-SmbShare", "New-SmbShare", "Get-SmbSession", "Get-VM", "Start-VM", "New-VM", "Get-Acl", "Set-Acl", "Export-Clixml",
            "Import-Clixml", "Get-WmiObject", "Invoke-WmiMethod", "Set-WmiInstance", "Register-ObjectEvent", "Get-Event", "Wait-Event", "New-WinEvent", "Get-Counter", "Export-Counter", "Get-HotFix",
            "Add-Computer", "Remove-Computer", "Rename-Computer", "Checkpoint-Computer", "Get-ItemPropertyValue", "Get-FileHash", "Expand-Archive", "Compress-Archive", "ConvertTo-Html", "Get-PfxCertificate",
            "Get-WSManInstance", "Test-WSMan", "Connect-PSSession", "Disconnect-PSSession", "Receive-PSSession", "Get-PSSnapin", "Add-PSSnapin", "Update-TypeData", "Update-FormatData",
            "Get-TypeData", "Find-Module", "Install-Module", "Update-Module", "Uninstall-Module", "Publish-Module", "Find-Package", "Install-Package", "Get-Package", "Register-PSRepository", "Save-Module",
            "Get-WindowsOptionalFeature", "Enable-WindowsOptionalFeature", "Disable-WindowsOptionalFeature", "Get-WindowsCapability", "Get-NetRoute", "New-NetRoute", "Get-NetNeighbor", "Get-DnsClientCache",
            "Clear-DnsClientCache", "Get-NetConnectionProfile", "Set-NetConnectionProfile", "Get-NetFirewallProfile", "Set-NetFirewallProfile", "Get-NetAdapterAdvancedProperty", "Rename-NetAdapter",
            "Disable-NetAdapter", "Enable-NetAdapter", "Restart-NetAdapter", "Get-NetIPInterface", "Set-NetIPInterface", "Set-NetIPAddress", "Get-Volume", "Get-PSReadLineOption", "Set-PSReadLineOption",
            "Get-PSBreakpoint", "Set-PSBreakpoint", "Debug-Process", "Enter-PSHostProcess", "Trace-Command", "Get-TraceSource", "Register-WmiEvent", "Get-EventSubscriber", "Get-ControlPanelItem",
            "Get-TimeZone", "Set-TimeZone", "Set-Date", "Get-Uptime", "Get-Error", "Test-Json", "ConvertFrom-Markdown", "ForEach-Object -Parallel", "Get-ScheduledJob", "Register-ScheduledJob",
            "Get-LocalGroupMember", "Get-CimAssociatedInstance", "Register-CimIndicationEvent", "Get-CimClass", "New-CimInstance", "Set-CimInstance", "Remove-CimInstance", "Get-Certificate"
        };
        public bool IsRealCmdletNotSimulated(string name) { return realCmdlets.Contains(name) && PsCmdlets.Get(name) == null; }

        // ------------------------------------------------------------ arguments
        public class ArgItem
        {
            public string Param; public object Value; public bool HasValue, Splat, FromColon; public PsAst At; public string Text = "";
            public override string ToString() { return Param != null ? "-" + Param : PsConv.ToStr(Value); }
        }

        List<ArgItem> EvalArgs(CommandAst c, PsScope sc, Streams st)
        {
            var l = new List<ArgItem>();
            foreach (var e in c.Elems)
            {
                var at = new ConstAst { Pos = e.Pos, End = e.Pos + Math.Max(1, e.Text.Length), Src = c.Src };
                if (e.Param != null)
                {
                    if (e.Param == "--") { l.Add(new ArgItem { Param = "--", At = at, Text = e.Text }); continue; }
                    var it = new ArgItem { Param = e.Param, At = at, Text = e.Text };
                    if (e.ColonArg) { it.Value = ArgValue(e.Arg, sc, st); it.HasValue = true; it.FromColon = true; }
                    l.Add(it);
                    continue;
                }
                if (e.Splat)
                {
                    var v = PsConv.Base(ReadVariable((VarAst)e.Arg, sc));
                    var d = v as IDictionary;
                    if (d != null) { foreach (DictionaryEntry kv in d) l.Add(new ArgItem { Param = PsConv.ToStr(kv.Key), Value = kv.Value, HasValue = true, Splat = true, At = at, FromColon = true }); continue; }
                    if (v != null) foreach (var x in PsConv.Items(v)) l.Add(new ArgItem { Value = x, HasValue = true, Splat = true, At = at });
                    continue;
                }
                l.Add(new ArgItem { Value = ArgValue(e.Arg, sc, st), HasValue = true, At = at, Text = e.Text });
            }
            return l;
        }
        object ArgValue(PsAst a, PsScope sc, Streams st)
        {
            var v = Eval(a, sc, st);
            if (v is AutoNull) return null;
            return v;
        }
    }

    // ------------------------------------------------------------ binding
    public class BindResult
    {
        public readonly Dictionary<string, object> Bound = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public readonly List<object> Extra = new List<object>();      // simple functions: $args
        public readonly Dictionary<string, object> Common = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public string ActiveSet;
    }

    public static class PsBinder
    {
        public static readonly string[] CommonNames = { "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable" };
        static readonly Dictionary<string, string> commonAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "vb", "Verbose" }, { "db", "Debug" }, { "ea", "ErrorAction" }, { "wa", "WarningAction" }, { "infa", "InformationAction" }, { "ev", "ErrorVariable" }, { "wv", "WarningVariable" },
            { "iv", "InformationVariable" }, { "ov", "OutVariable" }, { "ob", "OutBuffer" }, { "pv", "PipelineVariable" }, { "wi", "WhatIf" }, { "cf", "Confirm" }
        };
        public static List<ParamDef> CommonDefs(bool shouldProcess)
        {
            var l = PsCmdlets.ParseSpec("Verbose:switch alias=vb; Debug:switch alias=db; ErrorAction:ActionPreference alias=ea; WarningAction:ActionPreference alias=wa; InformationAction:ActionPreference alias=infa; ErrorVariable:string alias=ev; WarningVariable:string alias=wv; InformationVariable:string alias=iv; OutVariable:string alias=ov; OutBuffer:int alias=ob; PipelineVariable:string alias=pv");
            if (shouldProcess) l.AddRange(PsCmdlets.ParseSpec("WhatIf:switch alias=wi; Confirm:switch alias=cf"));
            return l;
        }

        public class BindError : Exception
        {
            public string Id, Category = "InvalidArgument", Target; public PsAst At; public string ExType = "System.Management.Automation.ParameterBindingException";
            public BindError(string msg, string id, PsAst at) : base(msg) { Id = id; At = at; }
        }

        public static ParamDef Find(List<ParamDef> defs, string name, PsAst at, bool allowUnknown)
        {
            var exact = defs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));
            if (exact != null) return exact;
            var pre = defs.Where(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pre.Count == 1) return pre[0];
            if (pre.Count > 1)
            {
                // a prefix of a parameter and of a common parameter: the command's own parameter wins only if unique among them
                var own = pre.Where(p => !CommonNames.Contains(p.Name) && p.Name != "WhatIf" && p.Name != "Confirm").ToList();
                if (own.Count == 1) return own[0];
                throw new BindError("Parameter cannot be processed because the parameter name '" + name + "' is ambiguous. Possible matches include: " + string.Join(" ", pre.Select(p => "-" + p.Name).ToArray()) + ".", "AmbiguousParameter", at);
            }
            if (allowUnknown) return null;
            throw new BindError("A parameter cannot be found that matches parameter name '" + name + "'.", "NamedParameterNotFound", at);
        }

        /// <summary>Binds named and positional arguments (pipeline input comes later, per object).</summary>
        public static BindResult Bind(List<PsRunspace.ArgItem> items, List<ParamDef> defs, bool simpleFunction, PsRunspace R, PsScope sc)
        {
            var res = new BindResult();
            var positional = new List<PsRunspace.ArgItem>();
            bool endOfParams = false;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Param == "--") { endOfParams = true; continue; }
                if (it.Param == null || endOfParams)
                {
                    if (it.Param != null) positional.Add(new PsRunspace.ArgItem { Value = "-" + it.Param, HasValue = true, At = it.At });
                    else positional.Add(it);
                    continue;
                }
                var pd = Find(defs, it.Param, it.At, simpleFunction);
                if (pd == null)
                {
                    // a simple function gets unknown -names in $args
                    res.Extra.Add(it.HasValue ? it.Value : "-" + it.Param);
                    continue;
                }
                object v;
                if (pd.IsSwitch) v = it.HasValue ? (object)PsConv.ToBool(it.Value) : true;
                else if (it.HasValue) v = it.Value;
                else
                {
                    if (i + 1 < items.Count && items[i + 1].Param == null) { v = items[i + 1].Value; i++; }
                    else throw new BindError("Missing an argument for parameter '" + pd.Name + "'. Specify a parameter of type '" + PsCmdlets.TypeFull(pd.Type.TrimEnd('[', ']')) + (pd.IsArray ? "[]" : "") + "' and try again.", "MissingArgument", it.At);
                }
                if (res.Bound.ContainsKey(pd.Name) && !it.Splat) throw new BindError("Cannot bind parameter because parameter '" + pd.Name + "' is specified more than once. To provide multiple values to parameters that can accept multiple values, use the array syntax. For example, \"-parameter value1,value2,value3\".", "ParameterAlreadyBound", it.At);
                res.Bound[pd.Name] = v;
            }
            // which parameter set do the named parameters pick?
            var sets = ActiveSets(res.Bound.Keys, defs);
            var pos = defs.Where(p => p.Position >= 0 && !res.Bound.ContainsKey(p.Name) && (sets == null || p.Sets == null || p.Sets.Any(s => sets.Contains(s)))).OrderBy(p => p.Position).ToList();
            var rest = defs.FirstOrDefault(p => p.Rest && !res.Bound.ContainsKey(p.Name));
            int pi = 0;
            foreach (var it in positional)
            {
                while (pi < pos.Count && res.Bound.ContainsKey(pos[pi].Name)) pi++;
                if (pi < pos.Count && !(pos[pi].Rest && pi > 0 && false))
                {
                    var pd = pos[pi];
                    // several parameters at this position (other parameter sets): take the one the value fits
                    var same = pos.Where(p => p.Position == pd.Position && !res.Bound.ContainsKey(p.Name)).ToList();
                    if (same.Count > 1)
                    {
                        var exactFit = same.FirstOrDefault(p => ExactType(PsConv.Base(it.Value), p.Type) && !p.Type.StartsWith("object", StringComparison.OrdinalIgnoreCase));
                        var fit = exactFit ?? same.FirstOrDefault(p => { try { ConvertTo(it.Value, p.Type, null); return true; } catch (PsConvertError) { return false; } catch (FormatException) { return false; } });
                        if (fit != null) { pd = fit; foreach (var other in same.Where(p => p != fit)) pos.Remove(other); }
                    }
                    if (pd.Rest) { var l = new List<object>(); if (res.Bound.ContainsKey(pd.Name)) l.AddRange(PsConv.Items(res.Bound[pd.Name])); l.Add(it.Value); res.Bound[pd.Name] = l.ToArray(); continue; }
                    res.Bound[pd.Name] = it.Value;
                    pi++;
                    continue;
                }
                if (rest != null)
                {
                    var l = new List<object>();
                    if (res.Bound.ContainsKey(rest.Name)) l.AddRange(PsConv.Items(res.Bound[rest.Name]));
                    l.Add(it.Value);
                    res.Bound[rest.Name] = l.ToArray();
                    continue;
                }
                if (simpleFunction) { res.Extra.Add(it.Value); continue; }
                throw new BindError("A positional parameter cannot be found that accepts argument '" + PsConv.ToStr(it.Value) + "'.", "PositionalParameterNotFound", it.At) { Target = PsConv.ToStr(it.Value) };
            }
            // a "rest" parameter given one value is still an array
            if (rest != null && res.Bound.ContainsKey(rest.Name) && !(res.Bound[rest.Name] is object[])) res.Bound[rest.Name] = new[] { res.Bound[rest.Name] };
            // common parameters move out
            foreach (var cn in CommonNames.Concat(new[] { "WhatIf", "Confirm" }))
                if (res.Bound.ContainsKey(cn) && !defs.Any(d => d.Name == cn && d.Ast != null)) { res.Common[cn] = res.Bound[cn]; if (defs.Any(d => d.Name == cn && !IsCommonDef(d))) continue; res.Bound.Remove(cn); }
            var final = ActiveSets(res.Bound.Keys, defs);
            res.ActiveSet = final == null ? null : final.FirstOrDefault();
            return res;
        }
        static bool IsCommonDef(ParamDef d) { return CommonNames.Contains(d.Name) || d.Name == "WhatIf" || d.Name == "Confirm"; }

        public static List<string> ActiveSets(IEnumerable<string> bound, List<ParamDef> defs)
        {
            List<string> sets = null;
            foreach (var b in bound)
            {
                var pd = defs.FirstOrDefault(p => p.Name.Equals(b, StringComparison.OrdinalIgnoreCase));
                if (pd == null || pd.Sets == null) continue;
                sets = sets == null ? pd.Sets.ToList() : sets.Intersect(pd.Sets, StringComparer.OrdinalIgnoreCase).ToList();
            }
            return sets;
        }

        /// <summary>Converts and validates a value for a parameter ("Cannot bind parameter 'Id'. Cannot convert value...").</summary>
        public static object Convert(ParamDef pd, object v, PsRunspace R, PsScope sc, PsAst at)
        {
            v = PsConv.Base(v);
            string t = pd.Type;
            object res;
            try { res = ConvertTo(v, t, R); }
            catch (PsConvertError e)
            {
                if (e is PsEnumError)
                {
                    var en = PsEnums.Get(PsCmdlets.TypeFull(t.TrimEnd('[', ']')));
                    throw new BindError("Cannot bind parameter '" + pd.Name + "'. Cannot convert value \"" + PsConv.ToStr(v) + "\" to type \"" + en.FullName + "\". Error: \"Unable to match the identifier name " + PsConv.ToStr(v) + " to a valid enumerator name. Specify one of the following enumerator names and try again:\n" + string.Join(", ", en.Values.Select(x => x.Key).ToArray()) + "\"", "CannotConvertArgumentNoMessage", at);
                }
                throw new BindError("Cannot bind parameter '" + pd.Name + "'. " + e.Message, "CannotConvertArgumentNoMessage", at);
            }
            Validate(pd, res, R, sc, at);
            return res;
        }

        public static object ConvertTo(object v, string t, PsRunspace R)
        {
            bool arr = t.EndsWith("[]", StringComparison.Ordinal);
            string et = arr ? t.Substring(0, t.Length - 2) : t;
            if (arr)
            {
                if (v == null) return null;
                return PsConv.Items(v).Select(x => ConvertOne(x, et, R)).ToArray();
            }
            if (PsConv.IsCollection(v) && !(et.Equals("object", StringComparison.OrdinalIgnoreCase) || et.Equals("psobject", StringComparison.OrdinalIgnoreCase)))
            {
                var l = PsConv.List(v);
                if (et.Equals("string", StringComparison.OrdinalIgnoreCase)) return string.Join(" ", l.Select(x => PsConv.ToStr(x)).ToArray());
                if (l.Count == 1) return ConvertOne(l[0], et, R);
                if (et.Equals("scriptblock", StringComparison.OrdinalIgnoreCase) || et.Equals("hashtable", StringComparison.OrdinalIgnoreCase) || IsSimType(et)) throw new PsConvertError(v, PsCmdlets.TypeFull(et), null);
            }
            return ConvertOne(v, et, R);
        }
        static bool IsSimType(string et) { var f = PsCmdlets.TypeFull(et); return PsTypes.Resolve(f) == null; }

        static object ConvertOne(object v, string et, PsRunspace R)
        {
            v = PsConv.Base(v);
            switch (et.ToLowerInvariant())
            {
                case "object": case "psobject": return v;
                case "string": return v == null ? null : PsConv.ToStr(v);
                case "switch": return PsConv.ToBool(v);
                case "scriptblock": if (v is PsScriptBlock || v == null) return v; throw new PsConvertError(v, "System.Management.Automation.ScriptBlock", null);
                case "hashtable": case "dictionary": if (v is IDictionary || v == null) return v; throw new PsConvertError(v, "System.Collections.Hashtable", null);
                case "securestring": if (v is PsSecureString || v == null) return v; throw new PsConvertError(v, "System.Security.SecureString", null);
                case "pscredential":
                    if (v == null) return null;
                    if (v is PsObj && ((PsObj)v).TypeName == "System.Management.Automation.PSCredential") return v;
                    if (v is string && R != null) return R.PromptCredential((string)v);
                    throw new PsConvertError(v, "System.Management.Automation.PSCredential", null);
            }
            string full = PsCmdlets.TypeFull(et);
            var pt = PsTypes.Resolve(full);
            // a [string] parameter of a script or function turns $null into "" (a classic PowerShell surprise)
            if (pt != null && v == null && et == "System.String") return "";
            if (pt != null) return v == null ? (pt.Clr != null && pt.Clr.IsValueType ? PsConv.To(null, pt) : null) : PsConv.To(v, pt);
            // a simulated type: the object must be one
            if (v == null) return null;
            var po = v as PsObj;
            if (po != null && PsTypes.IsInstance(po, new PsType { FullName = full })) return v;
            if (po != null && po.TypeNames.Any(n => n.EndsWith("." + et, StringComparison.Ordinal) || n.EndsWith("#" + et, StringComparison.Ordinal) || n == et)) return v;
            throw new PsConvertError(v, full, null);
        }

        static void Validate(ParamDef pd, object v, PsRunspace R, PsScope sc, PsAst at)
        {
            if (pd.ValidateSet != null && v != null)
            {
                foreach (var x in PsConv.Items(v))
                {
                    string s = PsConv.ToStr(x);
                    if (!pd.ValidateSet.Any(o => o.Equals(s, StringComparison.OrdinalIgnoreCase)))
                        throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The argument \"" + s + "\" does not belong to the set \"" + string.Join(",", pd.ValidateSet.ToArray()) + "\" specified by the ValidateSet attribute. Supply an argument that is in the set and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                }
            }
            if ((pd.NotNullOrEmpty || pd.NotNull) && (v == null || (pd.NotNullOrEmpty && (v is string && ((string)v).Length == 0 || PsConv.IsCollection(v) && PsConv.List(v).Count == 0))))
                throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The argument is null or empty. Provide an argument that is not null or empty, and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
            if ((pd.Min.HasValue || pd.Max.HasValue) && v != null)
            {
                foreach (var x in PsConv.Items(v))
                {
                    double d; try { d = PsConv.ToDouble(x); } catch (PsConvertError) { continue; }
                    if (pd.Min.HasValue && d < pd.Min.Value) throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The " + PsConv.ToStr(x) + " argument is less than the minimum allowed range of " + PsConv.ToStr(pd.Min.Value) + ". Supply an argument that is greater than or equal to " + PsConv.ToStr(pd.Min.Value) + " and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                    if (pd.Max.HasValue && d > pd.Max.Value) throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The " + PsConv.ToStr(x) + " argument is greater than the maximum allowed range of " + PsConv.ToStr(pd.Max.Value) + ". Supply an argument that is less than or equal to " + PsConv.ToStr(pd.Max.Value) + " and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                }
            }
            if (pd.Pattern != null && v != null)
                foreach (var x in PsConv.Items(v))
                    if (!Regex.IsMatch(PsConv.ToStr(x), pd.Pattern, RegexOptions.IgnoreCase))
                        throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The argument \"" + PsConv.ToStr(x) + "\" does not match the \"" + pd.Pattern + "\" pattern. Supply an argument that matches \"" + pd.Pattern + "\" and try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
            if ((pd.MinLen.HasValue || pd.MaxLen.HasValue) && v != null)
                foreach (var x in PsConv.Items(v))
                {
                    int n = PsConv.ToStr(x).Length;
                    if (pd.MinLen.HasValue && n < pd.MinLen.Value) throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The number of characters (" + n + ") in the argument is too small. Specify an argument whose length is greater than or equal to \"" + pd.MinLen.Value + "\" and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                    if (pd.MaxLen.HasValue && n > pd.MaxLen.Value) throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The character length of the " + n + " argument is too long. Shorten the character length of the argument so it is fewer than or equal to \"" + pd.MaxLen.Value + "\" characters, and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                }
            if ((pd.MinCount.HasValue || pd.MaxCount.HasValue) && v != null)
            {
                int n = PsConv.Items(v).Count();
                if (pd.MinCount.HasValue && n < pd.MinCount.Value || pd.MaxCount.HasValue && n > pd.MaxCount.Value)
                    throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The parameter requires at least " + (pd.MinCount ?? 0) + " value(s) and no more than " + (pd.MaxCount ?? int.MaxValue) + " value(s) - " + n + " value(s) were provided.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
            }
            if (pd.ValidateScript != null && v != null && R != null)
                foreach (var x in PsConv.Items(v))
                {
                    object ok;
                    try { ok = R.InvokeBlockValue(pd.ValidateScript, sc, x, null, false); }
                    catch (PsRuntimeException e) { throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. " + e.Rec.Exception.Message, "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" }; }
                    if (!PsConv.ToBool(ok))
                        throw new BindError("Cannot validate argument on parameter '" + pd.Name + "'. The \"" + pd.ValidateScript.Text.Trim() + "\" validation script for the argument with value \"" + PsConv.ToStr(x) + "\" did not return a result of True. Determine why the validation script failed, and then try the command again.", "ParameterArgumentValidationError", at) { ExType = "System.Management.Automation.ParameterBindingValidationException" };
                }
        }

        /// <summary>Binds one pipeline object: ByValue (same type), ByPropertyName, then ByValue with conversion.</summary>
        public static List<string> BindPipeline(object input, List<ParamDef> defs, Dictionary<string, object> args, HashSet<string> fromCommandLine, ref string activeSet, PsRunspace R, PsScope sc, PsAst at)
        {
            var bound = new List<string>();
            var o = PsConv.Base(input);
            List<string> sets = activeSet == null ? null : new List<string> { activeSet };
            var cmdSets = ActiveSets(fromCommandLine, defs);
            Func<ParamDef, bool> ok = p => !fromCommandLine.Contains(p.Name) && !bound.Contains(p.Name) && (cmdSets == null || p.Sets == null || p.Sets.Any(s => cmdSets.Contains(s, StringComparer.OrdinalIgnoreCase)))
                && (bound.Count == 0 || p.Sets == null || bound.All(b => { var bd = defs.First(d => d.Name == b); return bd.Sets == null || bd.Sets.Intersect(p.Sets, StringComparer.OrdinalIgnoreCase).Any(); }));
            // 1. by value, same type
            foreach (var p in defs.Where(p => p.Pipe && ok(p)))
            {
                if (ExactType(o, p.Type)) { args[p.Name] = WrapArr(o, p); bound.Add(p.Name); break; }
            }
            // 2. by property name
            foreach (var p in defs.Where(p => p.PipeProp && ok(p)))
            {
                object pv; string found = null;
                foreach (var nm in new[] { p.Name }.Concat(p.Aliases))
                {
                    bool f; pv = PropValue(R, o, nm, out f);
                    if (f) { found = nm; try { args[p.Name] = Convert(p, pv, R, sc, at); bound.Add(p.Name); } catch (BindError) { if (bound.Count == 0 && !defs.Any(d => d.Pipe && ok(d))) throw; } break; }
                }
            }
            // 3. by value, converted
            if (bound.Count == 0)
                foreach (var p in defs.Where(p => p.Pipe && ok(p)))
                {
                    try { args[p.Name] = Convert(p, WrapArr(o, p), R, sc, at); bound.Add(p.Name); break; }
                    catch (BindError) { }
                    catch (PsConvertError) { }
                }
            if (bound.Count > 0)
            {
                var all = ActiveSets(bound.Concat(fromCommandLine), defs);
                activeSet = all == null ? activeSet : all.FirstOrDefault();
            }
            return bound;
        }
        static object WrapArr(object o, ParamDef p) { return p.IsArray ? (object)new[] { o } : o; }
        static object PropValue(PsRunspace R, object o, string name, out bool found)
        {
            found = false;
            if (o == null) return null;
            var po = o as PsObj;
            if (po != null) { var pp = po.Find(name); if (pp != null) { found = true; return po.ValueOf(pp); } return null; }
            var notes = PsNotes.Get(o);
            if (notes != null && notes.Has(name)) { found = true; return notes.Get(name); }
            if (o is IDictionary) return null;
            if (o is string || PsConv.IsNumber(o)) return null;
            bool f; var v = PsMembers.Get(R, o, name, out f);
            found = f;
            return v;
        }

        public static bool ExactType(object o, string t)
        {
            if (o == null) return false;
            string et = t.EndsWith("[]", StringComparison.Ordinal) ? t.Substring(0, t.Length - 2) : t;
            switch (et.ToLowerInvariant())
            {
                case "object": case "psobject": return true;
                case "string": return o is string;
                case "int": return o is int;
                case "long": return o is long || o is int;
                case "scriptblock": return o is PsScriptBlock;
                case "hashtable": return o is Hashtable;
                case "securestring": return o is PsSecureString;
                case "datetime": return o is DateTime;
                case "switch": case "bool": return o is bool;
            }
            string full = PsCmdlets.TypeFull(et);
            var po = o as PsObj;
            if (po != null) return po.TypeNames.Any(n => n.Equals(full, StringComparison.OrdinalIgnoreCase) || n.Equals("Deserialized." + full, StringComparison.OrdinalIgnoreCase) && false || n.EndsWith("#" + et, StringComparison.OrdinalIgnoreCase));
            var pt = PsTypes.Resolve(full);
            return pt != null && PsTypes.IsInstance(o, pt);
        }
    }

    // ------------------------------------------------------------ processors
    public abstract class PsProc
    {
        public PsRunspace R; public PsScope Scope; public PsAst At; public List<Redir> Redirs = new List<Redir>(); public int Position;
        public Action<object> Next; public Streams St;
        public string PipelineVariable;

        /// <summary>Connects the output and applies redirections (2>&1, > file, 2>$null...).</summary>
        public virtual void Wire(Action<object> next, Streams parent, List<Action> finals)
        {
            Next = next;
            St = parent.WithOut(next);
            foreach (var r in Redirs)
            {
                if (r.MergeTo != null)
                {
                    var outp = St.Out;
                    if (r.Stream == "2" || r.Stream == "*") St.Err = e => outp(e);
                    if (r.Stream == "3" || r.Stream == "*") St.Warn = w => outp(new PsObj("System.Management.Automation.WarningRecord") { ToStr = x => w }.Add("Message", w));
                    if (r.Stream == "4" || r.Stream == "*") St.Verbose = w => outp(new PsObj("System.Management.Automation.VerboseRecord") { ToStr = x => w }.Add("Message", w));
                    if (r.Stream == "6" || r.Stream == "*") St.Info = w => outp(w is PsHostText ? (object)((PsHostText)w).Text : w);
                    continue;
                }
                var target = PsConv.Base(R.Eval(r.Target, Scope, parent));
                bool toNull = target == null;
                string path = toNull ? null : PsConv.ToStr(target);
                if (path != null && (path.Equals("$null", StringComparison.OrdinalIgnoreCase))) toNull = true;
                var collected = new List<object>();
                bool append = r.Op == ">>";
                Action<object> sink = o => { if (!toNull) collected.Add(o); };
                switch (r.Stream)
                {
                    case "1": St.Out = sink; break;
                    case "2": St.Err = e => sink(e); break;
                    case "3": St.Warn = w => sink("WARNING: " + w); break;
                    case "4": St.Verbose = w => sink("VERBOSE: " + w); break;
                    case "5": St.Debug = w => sink("DEBUG: " + w); break;
                    case "6": St.Info = w => sink(w is PsHostText ? ((PsHostText)w).Text : w); break;
                    case "*": St.Out = sink; St.Err = e => sink(e); St.Warn = w => sink("WARNING: " + w); St.Verbose = w => sink("VERBOSE: " + w); St.Info = w => sink(w is PsHostText ? ((PsHostText)w).Text : w); break;
                }
                if (!toNull)
                {
                    var self = this;
                    finals.Add(() => R.RedirectToFile(path, collected, append, self.Scope, self.At));
                }
            }
        }

        public void BeginSafe() { Guard(Begin); }
        public void RunSafe() { Guard(Run); }
        public void EndSafe() { Guard(End); }
        public void InputSafe(object o)
        {
            if (PipelineVariable != null) R.SetVar(PipelineVariable, o, Scope);
            Guard(() => Input(o));
        }
        void Guard(Action a)
        {
            try { a(); }
            catch (PsRuntimeException) { throw; }
            catch (PsFlowEx) { throw; }
            catch (PsSelectStop) { throw; }
            catch (PsBinder.BindError e) { throw BindFail(e); }
            catch (PsConvertError e) { throw R.RtError(At, e.Message, "RuntimeException", "InvalidArgument", "System.Management.Automation.PSInvalidCastException", null); }
            catch (PsParseError e) { throw new PsRuntimeException(R.ParseErrorRecord(e), false); }
            catch (ProvError e) { throw R.RtError(At, e.Message, e.Id, e.Cat, e.ExType, e.Target); }   // Cyber Range: a standard user reading a protected file
            catch (StackOverflowException) { throw; }
            catch (InsufficientExecutionStackException) { throw R.RtError(At, "The script failed due to call depth overflow.", "CallDepthOverflow", "InvalidOperation", "System.Management.Automation.ScriptCallDepthException", null); }
            catch (Exception e)
            {
                if (e.GetType().Name.Contains("ThreadAbort") || e is System.Threading.ThreadInterruptedException) throw;
                throw R.RtError(At, "Cert Lab's simulator had a problem running this command (" + e.GetType().Name + ": " + e.Message + "). Please tell the Cert Lab team.", "CertLabSimulatorProblem", "NotSpecified", null, null);
            }
        }
        protected virtual PsRuntimeException BindFail(PsBinder.BindError e)
        {
            var r = R.MakeError(e.At ?? At, e.Message, e.Id, e.Category, e.ExType, e.Target);
            r.CommandName = CommandName; r.FullCommandName = FullCommandName; r.Reason = e.ExType.Substring(e.ExType.LastIndexOf('.') + 1);
            r.TargetName = e.Target ?? ""; r.TargetType = e.Target != null ? "String" : "";
            return new PsRuntimeException(r, false);
        }
        public virtual string CommandName { get { return ""; } }
        public virtual string FullCommandName { get { return ""; } }

        public virtual void Begin() { }
        public virtual void Input(object o) { }
        public virtual void Run() { }
        public virtual void End() { }
    }

    public class PsSelectStop : Exception { }   // Select-Object -First N stops the commands before it

    class ExprProc : PsProc
    {
        public PsAst Expr;
        public override void Run()
        {
            var v = R.Eval(Expr, Scope, St);
            var u = Expr as UnAst;
            if (u != null && (u.Op == "++" || u.Op == "--")) return;
            if (Expr is VarAst && v is AutoNull) return;
            PsRunspace.EmitValue(v, St.Out);
        }
    }

    class CmdletProc : PsProc
    {
        readonly CmdCtx c;
        readonly HashSet<string> fromLine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, object> baseArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        List<string> lastPipeBound = new List<string>();
        readonly Dictionary<string, PsScriptBlock> delayBind = new Dictionary<string, PsScriptBlock>(StringComparer.OrdinalIgnoreCase);
        readonly List<ParamDef> defs;
        readonly string invokedAs;

        public CmdletProc(PsRunspace r, PsScope sc, CmdletDef d, CommandAst ast, List<PsRunspace.ArgItem> args, int pos, string invokedAs)
        {
            R = r; Scope = sc; At = ast; Redirs = ast.Redirs; Position = pos; this.invokedAs = invokedAs;
            c = new CmdCtx { R = r, Scope = sc, Def = d, Ast = ast, Position = pos, First = pos == 1 };
            defs = d.Params.Concat(PsBinder.CommonDefs(d.ShouldProcess)).ToList();
            BindResult b;
            try { b = PsBinder.Bind(args, defs, false, r, sc); }
            catch (PsBinder.BindError e) { throw BindFail(e); }
            foreach (var kv in b.Bound)
            {
                var pd = defs.First(p => p.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                object v;
                // a script block for a pipeline parameter of another type runs once per input object (delay-bind: -NewName { ... })
                var dsb = PsConv.Base(kv.Value) as PsScriptBlock;
                if (dsb != null && (pd.PipeProp || pd.Pipe) && d.TakesPipeline && !(pd.Type.StartsWith("scriptblock", StringComparison.OrdinalIgnoreCase) || pd.Type.StartsWith("object", StringComparison.OrdinalIgnoreCase) || pd.Type.StartsWith("psobject", StringComparison.OrdinalIgnoreCase)) && pos > 1)
                { delayBind[pd.Name] = dsb; fromLine.Add(pd.Name); continue; }
                try { v = PsBinder.Convert(pd, kv.Value, r, sc, ArgAt(args, pd)); }
                catch (PsBinder.BindError e) { throw BindFail(e); }
                c.Args[pd.Name] = v; baseArgs[pd.Name] = v; fromLine.Add(pd.Name);
            }
            c.ActiveSet = b.ActiveSet ?? d.DefaultSet;
            foreach (var kv in b.Common)
            {
                switch (kv.Key)
                {
                    case "ErrorAction": c.ErrorAction = PsConv.ToStr(PsConv.To(kv.Value, "System.Management.Automation.ActionPreference")); break;
                    case "WarningAction": c.WarningAction = PsConv.ToStr(PsConv.To(kv.Value, "System.Management.Automation.ActionPreference")); break;
                    case "InformationAction": c.InformationAction = PsConv.ToStr(kv.Value); break;
                    case "Verbose": c.VerboseOn = PsConv.ToBool(kv.Value); break;
                    case "Debug": c.DebugOn = PsConv.ToBool(kv.Value); break;
                    case "WhatIf": c.WhatIf = PsConv.ToBool(kv.Value); if (!c.WhatIf) c.Data["WhatIfFalse"] = true; break;
                    case "Confirm": c.ConfirmOn = PsConv.ToBool(kv.Value); if (!c.ConfirmOn) c.Data["ConfirmFalse"] = true; break;
                    case "ErrorVariable": c.ErrorVariable = PsConv.ToStr(kv.Value); break;
                    case "WarningVariable": c.WarningVariable = PsConv.ToStr(kv.Value); break;
                    case "OutVariable": c.OutVariable = PsConv.ToStr(kv.Value); break;
                    case "PipelineVariable": PipelineVariable = PsConv.ToStr(kv.Value); break;
                }
            }
            if (c.ErrorAction == "Suspend") throw r.RtError(ast, "The value Suspend is not supported for an ActionPreference variable. The provided value should be used only as a value for a preference variable where action preference is supported.", "NotSupported", "InvalidArgument", null, null);
        }
        /// <summary>Where the value of a parameter was typed (binding errors point at the value).</summary>
        public static PsAst ArgAt(List<PsRunspace.ArgItem> args, ParamDef pd)
        {
            int i = args.FindIndex(x => x.Param != null && (pd.Name.StartsWith(x.Param, StringComparison.OrdinalIgnoreCase) || pd.Aliases.Any(al => al.Equals(x.Param, StringComparison.OrdinalIgnoreCase))));
            if (i < 0) return null;
            if (!args[i].FromColon && i + 1 < args.Count && args[i + 1].Param == null && args[i + 1].At != null) return args[i + 1].At;
            return args[i].At;
        }
        public override string CommandName { get { return c.Def.Name; } }
        public override string FullCommandName { get { return c.Def.FullClass; } }

        public override void Wire(Action<object> next, Streams parent, List<Action> finals)
        {
            base.Wire(next, parent, finals);
            if (c.OutVariable != null)
            {
                string n = c.OutVariable.TrimStart('+');
                var list = c.OutVariable.StartsWith("+", StringComparison.Ordinal) && R.GetVar(n, Scope) is ArrayList ? (ArrayList)R.GetVar(n, Scope) : new ArrayList();
                R.SetVar(n, list, Scope);
                var o = St.Out;
                St.Out = x => { list.Add(x); o(x); };
            }
            if (c.ErrorVariable != null)
            {
                string n = c.ErrorVariable.TrimStart('+');
                var list = c.ErrorVariable.StartsWith("+", StringComparison.Ordinal) && R.GetVar(n, Scope) is ArrayList ? (ArrayList)R.GetVar(n, Scope) : new ArrayList();
                R.SetVar(n, list, Scope);
                c.ErrVar = list;
            }
            if (c.WarningVariable != null)
            {
                string n = c.WarningVariable.TrimStart('+');
                var list = new ArrayList();
                R.SetVar(n, list, Scope);
                var w = St.Warn;
                St.Warn = s => { list.Add(s); w(s); };
            }
            c.St = St;
        }

        void CheckMandatory(bool prompt)
        {
            var missing = c.Def.Params.Where(p => p.Mandatory && !c.Args.ContainsKey(p.Name) && p.InSet(c.ActiveSet) && (c.ActiveSet != null || p.Sets == null || PsBinder.ActiveSets(c.Args.Keys, defs) == null || p.Sets.Any(s => PsBinder.ActiveSets(c.Args.Keys, defs).Contains(s)))).ToList();
            if (missing.Count == 0) return;
            // only the parameters of one set: the first set that a mandatory belongs to
            if (missing.Any(p => p.Sets != null))
            {
                string set = c.ActiveSet ?? c.Def.DefaultSet ?? missing.Where(p => p.Sets != null).Select(p => p.Sets[0]).First();
                missing = missing.Where(p => p.InSet(set)).ToList();
                if (missing.Count == 0) return;
            }
            if (prompt && R.Host != null && R.Host.Interactive && c.First)
            {
                R.HostWrite("\ncmdlet " + c.Def.Name + " at command pipeline position " + Position + "\nSupply values for the following parameters:\n");
                foreach (var p in missing)
                {
                    object v;
                    if (p.IsArray)
                    {
                        var l = new List<object>();
                        for (int i = 0; ; i++) { var s = R.Host.ReadLine(p.Name + "[" + i + "]: ", false); if (string.IsNullOrEmpty(s)) break; l.Add(s); }
                        v = l.ToArray();
                    }
                    else if (p.Type.Equals("securestring", StringComparison.OrdinalIgnoreCase)) v = new PsSecureString { Plain = R.Host.ReadLine(p.Name + ": ", true) ?? "" };
                    else if (p.Type.Equals("pscredential", StringComparison.OrdinalIgnoreCase)) v = R.PromptCredential(null);
                    else v = R.Host.ReadLine(p.Name + ": ", false) ?? "";
                    try { c.Args[p.Name] = PsBinder.Convert(p, v, R, Scope, At); }
                    catch (PsBinder.BindError e) { throw BindFail(e); }
                }
                return;
            }
            var r = c.MakeError("Cannot process command because of one or more missing mandatory parameters: " + string.Join(" ", missing.Select(p => p.Name).ToArray()) + ".", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
            throw new PsRuntimeException(r, false);
        }

        public override void Begin() { if (c.Def.Begin != null) c.Def.Begin(c); }
        public override void Run()
        {
            CheckMandatory(true);
            c.HasInput = false; c.Input = null;
            if (c.Def.Process != null) c.Def.Process(c, null);
        }
        public override void Input(object o)
        {
            c.First = false;
            foreach (var k in lastPipeBound) { object bv; if (baseArgs.TryGetValue(k, out bv)) c.Args[k] = bv; else c.Args.Remove(k); }
            c.HasInput = true; c.Input = o;
            foreach (var db in delayBind)
            {
                var pd = c.Def.P(db.Key);
                try { c.Args[pd.Name] = PsBinder.Convert(pd, R.InvokeBlockValue(db.Value, Scope, o, null, false), R, Scope, At); }
                catch (PsBinder.BindError e) { c.WriteError(BindFail(e).Rec); return; }
            }
            if (c.Def.TakesPipeline)
            {
                string set = c.ActiveSet;
                List<string> pb;
                try { pb = PsBinder.BindPipeline(o, c.Def.Params, c.Args, fromLine, ref set, R, Scope, At); }
                catch (PsBinder.BindError e) { var r = BindFail(e).Rec; c.WriteError(r); return; }
                lastPipeBound = pb;
                if (pb.Count == 0)
                {
                    var r = c.MakeError("The input object cannot be bound to any parameters for the command either because the command does not take pipeline input or the input and its properties do not match any of the parameters that take pipeline input.", "InputObjectNotBound", "InvalidArgument", o, "System.Management.Automation.ParameterBindingException");
                    r.Reason = "ParameterBindingException";
                    c.WriteError(r);
                    return;
                }
                var keep = c.ActiveSet; c.ActiveSet = set;
                try { CheckMandatory(false); }
                catch (PsRuntimeException e) { c.ActiveSet = keep; c.WriteError(e.Rec); return; }
                if (c.Def.Process != null) c.Def.Process(c, o);
                c.ActiveSet = keep;
                return;
            }
            if (c.Def.Process != null && c.Def.End == null)
            {
                var r = c.MakeError("The input object cannot be bound to any parameters for the command either because the command does not take pipeline input or the input and its properties do not match any of the parameters that take pipeline input.", "InputObjectNotBound", "InvalidArgument", o, "System.Management.Automation.ParameterBindingException");
                c.WriteError(r);
                return;
            }
            c.Items.Add(o);
            if (c.Def.Process != null) c.Def.Process(c, o);
        }
        public override void End() { if (c.Def.End != null) c.Def.End(c); }
    }

    /// <summary>A function, filter, script file or script block (& { } / . { }) in the pipeline.</summary>
    public class ScriptProc : PsProc
    {
        public ScriptBlockAst Block; public PsModule BlockModule; public PsScope Home; public bool DotSource, IsFilter; public string Name = "", ScriptPath, ScopeKind = "function";
        public List<PsRunspace.ArgItem> Args; public PsFunction Func;
        PsScope fs;
        List<ParamDef> defs;
        bool advanced;
        readonly List<object> input = new List<object>();
        HashSet<string> fromLine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string activeSet;
        List<string> lastPipe = new List<string>();
        Dictionary<string, object> bound;
        PsBoundDict psBound;
        public override string CommandName { get { return Name; } }
        public override string FullCommandName { get { return Name; } }

        void Setup()
        {
            if (fs != null) return;
            var parent = Home ?? Scope;
            fs = DotSource ? Scope : new PsScope(R, parent, ScopeKind) { Module = BlockModule ?? parent.Module };
            if (ScriptPath != null && !DotSource) fs.Kind = "script";
            advanced = Block.Advanced;
            defs = PsScriptParams.Defs(Block, R, fs);
            bool simple = !advanced;
            if (advanced) defs.AddRange(PsBinder.CommonDefs(Block.BlockAttrs.Any(a => a.Named.Any(n => n.Key.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase) && PsConv.ToBool(R.Eval(n.Value, fs, St))))));
            BindResult b;
            try { b = PsBinder.Bind(Args ?? new List<PsRunspace.ArgItem>(), defs, simple, R, fs); }
            catch (PsBinder.BindError e) { throw BindFail(e); }
            bound = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            psBound = new PsBoundDict();
            foreach (var kv in b.Bound)
            {
                var pd = defs.First(p => p.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                object v;
                try { v = PsBinder.Convert(pd, kv.Value, R, fs, CmdletProc.ArgAt(Args ?? new List<PsRunspace.ArgItem>(), pd) ?? At); }
                catch (PsBinder.BindError e) { throw BindFail(e); }
                bound[pd.Name] = v; fromLine.Add(pd.Name); psBound[pd.Name] = pd.IsSwitch ? (object)new PsSwitch(PsConv.ToBool(v)) : v;
            }
            activeSet = b.ActiveSet;
            // variables
            var cmdInfo = new PsCommandInfo { Kind = ScriptPath != null ? "ExternalScript" : "Function", Name = Name, Path = ScriptPath };
            fs.Command = cmdInfo; fs.CallAt = At;
            R.NewVar(fs, "args", b.Extra.ToArray());
            R.NewVar(fs, "PSBoundParameters", psBound);
            R.NewVar(fs, "MyInvocation", R.InvocationInfo(Name, At, ScriptPath, psBound, Position));
            if (ScriptPath != null)
            {
                R.NewVar(fs, "PSScriptRoot", ScriptPath.Substring(0, ScriptPath.LastIndexOf('\\')));
                R.NewVar(fs, "PSCommandPath", ScriptPath);
            }
            if (advanced) R.NewVar(fs, "PSCmdlet", R.PsCmdletObj(this, fs, Name, activeSet));
            foreach (var kv in b.Common)
            {
                switch (kv.Key)
                {
                    case "Verbose": R.NewVar(fs, "VerbosePreference", PsEnums.V("System.Management.Automation.ActionPreference", PsConv.ToBool(kv.Value) ? "Continue" : "SilentlyContinue")); break;
                    case "Debug": R.NewVar(fs, "DebugPreference", PsEnums.V("System.Management.Automation.ActionPreference", PsConv.ToBool(kv.Value) ? "Continue" : "SilentlyContinue")); break;
                    case "ErrorAction": R.NewVar(fs, "ErrorActionPreference", PsConv.To(kv.Value, "System.Management.Automation.ActionPreference")); break;
                    case "WarningAction": R.NewVar(fs, "WarningPreference", PsConv.To(kv.Value, "System.Management.Automation.ActionPreference")); break;
                    case "WhatIf": R.NewVar(fs, "WhatIfPreference", PsConv.ToBool(kv.Value)); break;
                    case "Confirm": R.NewVar(fs, "ConfirmPreference", PsConv.ToBool(kv.Value) ? "Low" : "None"); break;
                    case "OutVariable": { var l = new ArrayList(); R.SetVar(PsConv.ToStr(kv.Value).TrimStart('+'), l, Scope); var o = St.Out; St = St.WithOut(x => { l.Add(x); o(x); }); break; }
                    case "ErrorVariable": { var l = new ArrayList(); R.SetVar(PsConv.ToStr(kv.Value).TrimStart('+'), l, Scope); fs.Vars["__ErrVar"] = new PsVariable { Name = "__ErrVar", Value = l, Private = true }; break; }
                    case "PipelineVariable": PipelineVariable = PsConv.ToStr(kv.Value); break;
                }
                psBound[kv.Key] = kv.Key == "Verbose" || kv.Key == "Debug" || kv.Key == "WhatIf" || kv.Key == "Confirm" ? (object)new PsSwitch(PsConv.ToBool(kv.Value)) : kv.Value;
            }
            foreach (var pd in defs.Where(p => p.Ast != null))
            {
                object v;
                if (bound.TryGetValue(pd.Name, out v)) { SetParam(pd, v); continue; }
            }
        }

        void SetParam(ParamDef pd, object v)
        {
            PsType t = null;
            if (pd.Ast != null && pd.Ast.Type != null) t = PsTypes.Resolve(pd.Ast.Type);
            if (pd.IsSwitch) v = new PsSwitch(PsConv.ToBool(v));
            var var = new PsVariable { Name = pd.Name, Value = v, Type = t != null && t.FullName != "System.Management.Automation.SwitchParameter" ? t : null };
            fs.Vars[pd.Name] = var;
        }

        void Defaults()
        {
            foreach (var pd in defs.Where(p => p.Ast != null))
            {
                if (bound.ContainsKey(pd.Name)) continue;
                object v = null;
                if (pd.Ast.Default != null) v = R.Eval(pd.Ast.Default, fs, St);
                else if (pd.IsSwitch) v = false;
                else if (pd.Ast.Type != null)
                {
                    var t = PsTypes.Resolve(pd.Ast.Type);
                    if (t != null && t.Clr != null && t.Clr.IsValueType) v = Activator.CreateInstance(t.Clr);
                    else if (t != null && t.FullName == "System.String") v = "";
                }
                if (v is AutoNull) v = null;
                if (pd.Ast.Type != null && v != null) { var t = PsTypes.Resolve(pd.Ast.Type); if (t != null && !pd.IsSwitch) v = PsConv.To(v, t); }
                SetParam(pd, v);
            }
        }

        void CheckMandatory(bool prompt)
        {
            var sets = PsBinder.ActiveSets(bound.Keys, defs);
            var missing = defs.Where(p => p.Mandatory && !bound.ContainsKey(p.Name) && (sets == null || p.Sets == null || p.Sets.Any(s => sets.Contains(s)))).ToList();
            if (missing.Count == 0) return;
            if (prompt && R.Host != null && R.Host.Interactive && Position == 1)
            {
                R.HostWrite("\ncmdlet " + Name + " at command pipeline position " + Position + "\nSupply values for the following parameters:\n");
                foreach (var p in missing)
                {
                    if (p.Help.Length > 0) R.HostWrite("(Type !? for Help.)\n");
                    object v;
                    if (p.IsArray) { var l = new List<object>(); for (int i = 0; ; i++) { var s = R.Host.ReadLine(p.Name + "[" + i + "]: ", false); if (string.IsNullOrEmpty(s)) break; l.Add(s); } v = l.ToArray(); }
                    else { var s = R.Host.ReadLine(p.Name + ": ", p.Type.Equals("securestring", StringComparison.OrdinalIgnoreCase)); if (s == "!?" && p.Help.Length > 0) { R.HostWrite(p.Help + "\n"); s = R.Host.ReadLine(p.Name + ": ", false); } v = p.Type.Equals("securestring", StringComparison.OrdinalIgnoreCase) ? (object)new PsSecureString { Plain = s ?? "" } : s ?? ""; }
                    try { v = PsBinder.Convert(p, v, R, fs, At); } catch (PsBinder.BindError e) { throw BindFail(e); }
                    bound[p.Name] = v; psBound[p.Name] = v; SetParam(p, v);
                }
                return;
            }
            var r = R.MakeError(At, "Cannot process command because of one or more missing mandatory parameters: " + string.Join(" ", missing.Select(p => p.Name).ToArray()) + ".", "MissingMandatoryParameter", "InvalidArgument", "System.Management.Automation.ParameterBindingException", null);
            r.CommandName = Name; r.FullCommandName = Name; r.Reason = "ParameterBindingException";
            throw new PsRuntimeException(r, false);
        }

        void RunBlock(StmtBlock b, object dollarUnder, bool setUnder)
        {
            if (b == null) return;
            if (setUnder) fs.Vars["_"] = new PsVariable { Name = "_", Value = dollarUnder };
            R.CallDepth++;
            if (R.CallDepth > 100) { R.CallDepth = 0; throw R.RtError(At, "The script failed due to call depth overflow.", "CallDepthOverflow", "InvalidOperation", "System.Management.Automation.ScriptCallDepthException", null); }
            try { R.ExecBlock(b, fs, St); }
            catch (PsReturnEx) { }
            finally { R.CallDepth--; }
        }

        public override void Begin()
        {
            Setup();
            if (Block.Begin != null) { Defaults(); defaultsDone = true; RunBlock(Block.Begin, null, false); }
        }
        bool defaultsDone, mandatoryDone;

        public override void Run()
        {
            if (!mandatoryDone) { CheckMandatory(true); mandatoryDone = true; }
            if (!defaultsDone) { Defaults(); defaultsDone = true; }
            if (ProcessBlock != null) RunBlock(ProcessBlock, null, false);
        }
        StmtBlock ProcessBlock { get { return Block.Process ?? (IsFilter ? Block.Body : null); } }

        public override void Input(object o)
        {
            if (!defaultsDone) { Defaults(); defaultsDone = true; }
            if (ProcessBlock == null) { input.Add(o); return; }
            if (advanced)
            {
                foreach (var k in lastPipe) { bound.Remove(k); psBound.Remove(k); }
                var args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var pb = PsBinder.BindPipeline(o, defs, args, fromLine, ref activeSet, R, fs, At);
                lastPipe = pb;
                foreach (var k in pb) { bound[k] = args[k]; psBound[k] = args[k]; SetParam(defs.First(d => d.Name == k), args[k]); }
                if (pb.Count == 0 && defs.Any(d => d.Pipe || d.PipeProp))
                {
                    var r = R.MakeError(At, "The input object cannot be bound to any parameters for the command either because the command does not take pipeline input or the input and its properties do not match any of the parameters that take pipeline input.", "InputObjectNotBound", "InvalidArgument", "System.Management.Automation.ParameterBindingException", o);
                    r.CommandName = Name; r.FullCommandName = Name; r.Reason = "ParameterBindingException";
                    R.AddError(r); St.Err(r);
                    return;
                }
                if (!mandatoryDone)
                {
                    try { CheckMandatory(false); }
                    catch (PsRuntimeException e) { R.AddError(e.Rec); St.Err(e.Rec); return; }
                }
            }
            RunBlock(ProcessBlock, o, true);
        }

        public override void End()
        {
            Setup();
            if (!mandatoryDone && Position > 1 && ProcessBlock == null && defs.Any(d => d.Mandatory && !bound.ContainsKey(d.Name)))
            {
                // a function without a process block binds its last input object to its pipeline parameters
                if (advanced && input.Count > 0)
                {
                    var args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    var pb = PsBinder.BindPipeline(input[input.Count - 1], defs, args, fromLine, ref activeSet, R, fs, At);
                    foreach (var k in pb) { bound[k] = args[k]; psBound[k] = args[k]; SetParam(defs.First(d => d.Name == k), args[k]); }
                }
                CheckMandatory(false); mandatoryDone = true;
            }
            else if (advanced && ProcessBlock == null && input.Count > 0)
            {
                var args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var pb = PsBinder.BindPipeline(input[input.Count - 1], defs, args, fromLine, ref activeSet, R, fs, At);
                foreach (var k in pb) { bound[k] = args[k]; psBound[k] = args[k]; SetParam(defs.First(d => d.Name == k), args[k]); }
            }
            if (!defaultsDone) { Defaults(); defaultsDone = true; }
            var endBlock = Block.EndBlock ?? (IsFilter ? null : Block.Body);
            // $input: the pipeline input, readable once (it overwrites a parameter named $Input)
            fs.Vars["input"] = new PsVariable { Name = "input", Value = new PsInputEnum(input) };
            if (endBlock != null) RunBlock(endBlock, input.Count > 0 ? input[input.Count - 1] : null, ProcessBlock != null || input.Count > 0);
        }
    }

    public class PsBoundDict : OrderedDictionary
    {
        public PsBoundDict() : base(StringComparer.OrdinalIgnoreCase) { }
        public bool ContainsKey(string k) { return Contains(k); }
        public bool TryGetValue(string k, out object v) { v = Contains(k) ? this[k] : null; return Contains(k); }
        public new object[] Keys { get { return base.Keys.Cast<object>().ToArray(); } }
    }

    public static class PsScriptParams
    {
        /// <summary>The parameters of a script block, function or script, from its param block and attributes.</summary>
        public static List<ParamDef> Defs(ScriptBlockAst b, PsRunspace R, PsScope sc)
        {
            var l = new List<ParamDef>();
            if (b.Params == null) return l;
            bool advanced = b.Advanced;
            int pos = 0;
            bool positional = !b.BlockAttrs.Any(a => a.Named.Any(n => n.Key.Equals("PositionalBinding", StringComparison.OrdinalIgnoreCase) && !PsConv.ToBool(R.Eval(n.Value, sc, new Streams()))));
            foreach (var p in b.Params)
            {
                var pd = new ParamDef { Name = p.Name, Ast = p, Type = p.Type == null ? "object" : TypeWord(p.Type) };
                bool explicitPos = false;
                foreach (var a in p.Attrs)
                {
                    string an = a.Name.ToLowerInvariant();
                    if (an == "parameter" || an == "system.management.automation.parameter" || an == "parameterattribute")
                    {
                        foreach (var kv in a.Named)
                        {
                            object v = R.Eval(kv.Value, sc, new Streams());
                            switch (kv.Key.ToLowerInvariant())
                            {
                                case "mandatory": pd.Mandatory = PsConv.ToBool(v); break;
                                case "position": pd.Position = PsConv.ToInt(v); explicitPos = true; break;
                                case "valuefrompipeline": pd.Pipe = PsConv.ToBool(v); break;
                                case "valuefrompipelinebypropertyname": pd.PipeProp = PsConv.ToBool(v); break;
                                case "valuefromremainingarguments": pd.Rest = PsConv.ToBool(v); break;
                                case "parametersetname": pd.Sets = new List<string> { PsConv.ToStr(v) }; break;
                                case "helpmessage": pd.Help = PsConv.ToStr(v); break;
                            }
                        }
                    }
                    else if (an == "alias") pd.Aliases.AddRange(a.Args.SelectMany(x => PsConv.Items(R.Eval(x, sc, new Streams()))).Select(x => PsConv.ToStr(x)));
                    else if (an == "validateset") pd.ValidateSet = a.Args.SelectMany(x => PsConv.Items(R.Eval(x, sc, new Streams()))).Select(x => PsConv.ToStr(x)).ToList();
                    else if (an == "validaterange") { if (a.Args.Count >= 2) { pd.Min = PsConv.ToDouble(R.Eval(a.Args[0], sc, new Streams())); pd.Max = PsConv.ToDouble(R.Eval(a.Args[1], sc, new Streams())); } }
                    else if (an == "validatepattern") { if (a.Args.Count >= 1) pd.Pattern = PsConv.ToStr(R.Eval(a.Args[0], sc, new Streams())); }
                    else if (an == "validatenotnullorempty") pd.NotNullOrEmpty = true;
                    else if (an == "validatenotnull") pd.NotNull = true;
                    else if (an == "validatescript") { if (a.Args.Count >= 1) pd.ValidateScript = PsConv.Base(R.Eval(a.Args[0], sc, new Streams())) as PsScriptBlock; }
                    else if (an == "validatelength") { if (a.Args.Count >= 2) { pd.MinLen = PsConv.ToInt(R.Eval(a.Args[0], sc, new Streams())); pd.MaxLen = PsConv.ToInt(R.Eval(a.Args[1], sc, new Streams())); } }
                    else if (an == "validatecount") { if (a.Args.Count >= 2) { pd.MinCount = PsConv.ToInt(R.Eval(a.Args[0], sc, new Streams())); pd.MaxCount = PsConv.ToInt(R.Eval(a.Args[1], sc, new Streams())); } }
                }
                if (!explicitPos && positional && !pd.IsSwitch) pd.Position = pos++;
                else if (explicitPos) pos = Math.Max(pos, pd.Position + 1);
                l.Add(pd);
            }
            // with explicit positions on some parameters, PowerShell stops numbering the others
            if (advanced && b.Params.Any(p => p.Attrs.Any(a => a.Named.Any(n => n.Key.Equals("Position", StringComparison.OrdinalIgnoreCase)))))
                foreach (var pd in l) if (!pd.Ast.Attrs.Any(a => a.Named.Any(n => n.Key.Equals("Position", StringComparison.OrdinalIgnoreCase)))) pd.Position = -1;
            return l;
        }
        static string TypeWord(string t)
        {
            var r = PsTypes.Resolve(t);
            if (r == null) return t;
            if (r.FullName == "System.Management.Automation.SwitchParameter") return "switch";
            if (r.IsArray) return r.Element.FullName + "[]";
            return r.FullName;
        }
    }

    /// <summary>A program run from PowerShell (hostname, whoami, ipconfig, ping, notepad...): text in, text out.</summary>
    public class PsNative
    {
        public string Name = "", Path = "";
        public Func<PsMachine, bool> Available;
        public Func<NativeCall, int> Run;
    }
    public class NativeCall
    {
        public PsRunspace R; public PsScope Scope; public List<string> Args = new List<string>(); public List<object> Input = new List<object>();
        public readonly List<string> Out = new List<string>();
        public PsMachine M { get { return R.M; } }
        public void L(string s) { foreach (var x in s.Replace("\r", "").Split('\n')) Out.Add(x); }
        public void Err(string s) { R.HostWrite(s + "\n"); }
        public Streams St;
    }
    public static partial class PsNatives
    {
        static readonly Dictionary<string, PsNative> all = new Dictionary<string, PsNative>(StringComparer.OrdinalIgnoreCase);
        static bool done;
        public static void Add(string name, string path, Func<NativeCall, int> run) { all[name] = new PsNative { Name = name, Path = path ?? "C:\\Windows\\system32\\" + name + ".exe", Run = run }; }
        public static PsNative Get(string name) { Init(); PsNative n; return all.TryGetValue(name, out n) ? n : null; }
        public static IEnumerable<PsNative> All { get { Init(); return all.Values; } }
        static readonly object gate = new object();
        static void Init() { lock (gate) { if (done) return; done = true; RegisterCoreNatives(); RegisterAdminNatives(); RegisterNetNatives(); RegisterEndpointNatives(); RegisterWsNatives(); } }
        static partial void RegisterWsNatives();
        static partial void RegisterCoreNatives();
        static partial void RegisterAdminNatives();
        static partial void RegisterNetNatives();
        static partial void RegisterEndpointNatives();
    }

    class NativeProc : PsProc
    {
        public string Path, Name; public List<PsRunspace.ArgItem> Args;
        readonly List<object> input = new List<object>();
        public override void Input(object o) { input.Add(o); }
        public override void End()
        {
            string bare = Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Name.Substring(0, Name.Length - 4) : Name;
            var nat = PsNatives.Get(bare);
            var call = new NativeCall { R = R, Scope = Scope, St = St };
            foreach (var a in Args) { if (a.Param != null) { call.Args.Add(a.Text.Length > 0 ? a.Text : "-" + a.Param); if (a.HasValue) call.Args.Add(PsConv.ToStr(a.Value)); } else foreach (var x in PsConv.Items(a.Value)) call.Args.Add(PsConv.ToStr(x)); }
            call.Input.AddRange(input);
            int code;
            if (nat == null || !nat.Path.Equals(Path, StringComparison.OrdinalIgnoreCase) && Path != null && !Path.StartsWith("C:\\Windows", StringComparison.OrdinalIgnoreCase))
            {
                if (Path != null && (Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || Path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))) code = R.RunBatch(Path, call);
                else
                {
                    R.HostWrite("Cert Lab: the program " + Name + " is not simulated on the practice machines.\n");
                    code = 1;
                }
            }
            else code = nat.Run(call);
            foreach (var l in call.Out) St.Out(l);
            R.SetVar("LASTEXITCODE", code, R.Global);
            if (code != 0) R.CurrentOk = false;
        }
    }
}
