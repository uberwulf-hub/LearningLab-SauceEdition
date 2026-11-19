// Cert Lab - Desired State Configuration, part 2: the Configuration keyword at run time (calling a configuration
// compiles its Node and resource blocks into <node>.mof files, with -OutputPath and -ConfigurationData), the Local
// Configuration Manager (Start-DscConfiguration applies a MOF in DependsOn order: Test, then Set when needed;
// Test-DscConfiguration finds drift) and the other DSC cmdlets.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    /// <summary>What a running configuration has collected so far.</summary>
    public class DscBuild
    {
        public string Config = "";
        public readonly List<KeyValuePair<string, List<DscRes>>> Nodes = new List<KeyValuePair<string, List<DscRes>>>();
        public string Node;
        public List<DscRes> NodeList(string n)
        {
            foreach (var kv in Nodes) if (kv.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            var l = new List<DscRes>(); Nodes.Add(new KeyValuePair<string, List<DscRes>>(n, l)); return l;
        }
    }

    /// <summary>Calling a configuration: binds its parameters, runs its body, writes the MOF files.</summary>
    class DscConfigProc : PsProc
    {
        public PsFunction Func; public List<PsRunspace.ArgItem> Args;
        public override string CommandName { get { return Func.Name; } }
        public override void End() { PsCmdlets.RunConfiguration(R, Scope, St, Func, Args, At); }
    }

    public partial class PsCmdlets
    {
        public const string DscMod = "PSDesiredStateConfiguration";
        [ThreadStatic] static DscBuild dscBuild;

        static PsRuntimeException DscError(PsRunspace R, PsAst at, string msg, string id)
        {
            var r = R.MakeError(at, msg, id, "InvalidOperation", "System.InvalidOperationException", null);
            r.ActivityOverride = "";
            return new PsRuntimeException(r, false);
        }

        public static void RunConfiguration(PsRunspace R, PsScope sc, Streams st, PsFunction f, List<PsRunspace.ArgItem> args, PsAst at)
        {
            // the arguments: -OutputPath, -ConfigurationData, -InstanceName, then the configuration's own parameters
            var named = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<object>();
            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                if (a.Param == null) { positional.Add(a.Value); continue; }
                if (a.HasValue) { named[a.Param] = a.Value; continue; }
                if (i + 1 < args.Count && args[i + 1].Param == null) { named[a.Param] = args[i + 1].Value; i++; }
                else named[a.Param] = true;
            }
            Func<string, object> take = n => { foreach (var k in named.Keys.ToList()) if (n.StartsWith(k, StringComparison.OrdinalIgnoreCase) && k.Length >= 3) { var v = named[k]; named.Remove(k); return v; } return null; };
            var outPath = take("OutputPath");
            var cdata = PsConv.Base(take("ConfigurationData"));
            take("InstanceName");
            var fs = new PsScope(R, sc, "function");
            var ps = f.Ast.Params ?? new List<ParamAst>();
            int pi = 0;
            foreach (var p in ps)
            {
                object v;
                if (named.TryGetValue(p.Name, out v)) named.Remove(p.Name);
                else if (pi < positional.Count) v = positional[pi++];
                else v = p.Default != null ? R.Eval(p.Default, fs, st) : null;
                if (v is AutoNull) v = null;
                if (p.Type != null && v != null) { var t = PsTypes.Resolve(p.Type); if (t != null) v = PsConv.To(v, t); }
                fs.Vars[p.Name] = new PsVariable { Name = p.Name, Value = v };
            }
            if (named.Count > 0) throw DscError(R, at, "A parameter cannot be found that matches parameter name '" + named.Keys.First() + "'.", "NamedParameterNotFound");
            // configuration data
            if (cdata is string)
            {
                string err;
                var h = PsModMgr.ReadData(R, R.FullFsPath((string)cdata, R.CurrentFsDir), out err);
                if (h == null) throw DscError(R, at, "Cannot read the configuration data file " + cdata + ": " + err, "InvalidConfigurationData");
                cdata = h;
            }
            var cd = cdata as IDictionary;
            if (cd != null)
            {
                var all = PsModMgr.Key(cd, "AllNodes");
                if (all == null) throw DscError(R, at, "ConfigurationData parameter need to have property AllNodes.", "ConfiguratonDataNeedAllNodes");
                var nodes = PsConv.Items(all).Select(x => PsConv.Base(x) as IDictionary).ToList();
                if (nodes.Any(n => n == null || PsModMgr.Key(n, "NodeName") == null)) throw DscError(R, at, "all elements of AllNodes need to be hashtable and has a property NodeName.", "ConfiguratonDataAllNodesNeedHashtable");
                var star = nodes.FirstOrDefault(n => PsConv.ToStr(PsModMgr.Key(n, "NodeName")) == "*");
                var merged = new List<object>();
                foreach (var n in nodes.Where(n => n != star))
                {
                    var h = PsConv.NewHashtable();
                    if (star != null) foreach (DictionaryEntry e in star) if (PsConv.ToStr(e.Key) != "NodeName") h[e.Key] = e.Value;
                    foreach (DictionaryEntry e in n) h[e.Key] = e.Value;
                    merged.Add(h);
                }
                fs.Vars["AllNodes"] = new PsVariable { Name = "AllNodes", Value = merged.ToArray() };
                fs.Vars["ConfigurationData"] = new PsVariable { Name = "ConfigurationData", Value = cd };
            }
            var build = new DscBuild { Config = f.Name };
            var old = dscBuild;
            dscBuild = build;
            try
            {
                try { R.ExecBlock(f.Ast.Body ?? f.Ast.EndBlock, fs, st); }
                catch (PsReturnEx) { }
            }
            finally { dscBuild = old; }
            if (build.Nodes.Count == 0) return;
            string dir = outPath != null ? R.FullFsPath(PsConv.ToStr(outPath), R.CurrentFsDir) : R.FullFsPath(f.Name, R.CurrentFsDir);
            R.M.MakeDir(dir, R.M.Now);
            foreach (var kv in build.Nodes)
            {
                var doc = new DscDoc { Name = f.Name, Node = kv.Key, Author = R.User, Host = R.M.Name, Date = R.M.Now.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture), Resources = kv.Value };
                foreach (var r in doc.Resources)
                    foreach (var dep in r.DependsOn)
                        if (!doc.Resources.Any(x => x.Id.Equals(dep, StringComparison.OrdinalIgnoreCase)))
                            throw DscError(R, at, "The resource " + r.Id + " depends on " + dep + ", which is not defined for node '" + kv.Key + "'. Check the DependsOn value (it has the form [ResourceType]ResourceName).", "DependsOnLinkNotFound");
                string file = dir.TrimEnd('\\') + "\\" + kv.Key + ".mof";
                string why = R.WriteFile(file, PsDsc.ToMof(doc), "ascii", false);
                if (why != null) throw DscError(R, at, why, "MofWriteFailed");
                st.Out(FileInfoObj(R.FsNode(file)));
            }
        }

        static void DscStatement(PsRunspace R, PsAst a, PsScope sc, Streams st)
        {
            var build = dscBuild;
            if (build == null) throw R.RtError(a, "Node and resource blocks can only be used inside a Configuration.", "InvalidDscKeyword", "ParserError", null, null);
            var node = a as NodeAst;
            if (node != null)
            {
                var names = PsConv.Items(R.Eval(node.Names, sc, st)).Select(x => PsConv.ToStr(x)).Where(x => x.Length > 0).ToList();
                var all = PsConv.Base(R.GetVar("AllNodes", sc));
                foreach (var n in names)
                {
                    var ns = new PsScope(R, sc, "local");
                    object nodeData = PsConv.Items(all).Select(x => PsConv.Base(x) as IDictionary).FirstOrDefault(x => x != null && PsConv.ToStr(PsModMgr.Key(x, "NodeName")).Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (nodeData == null) { var h = PsConv.NewHashtable(); h["NodeName"] = n; nodeData = h; }
                    ns.Vars["Node"] = new PsVariable { Name = "Node", Value = nodeData };
                    string keep = build.Node;
                    build.Node = n;
                    build.NodeList(n);
                    try { R.ExecBlock(node.Body, ns, st); }
                    finally { build.Node = keep; }
                }
                return;
            }
            var ra = (ResourceAst)a;
            var def = PsDsc.Find(ra.Type);
            if (def == null) throw R.RtError(a, "Undefined DSC resource '" + ra.Type + "'. Use Import-DSCResource to import the resource.", "ResourceNotDefined", "ParserError", "System.Management.Automation.ParseException", null);
            string name = PsConv.ToStr(R.Eval(ra.Name, sc, st));
            var props = PsConv.Base(R.Eval(ra.Props, sc, st)) as IDictionary;
            var res = new DscRes { Type = def.Name, Name = name, ConfigurationName = build.Config, ModuleName = def.Module.Length > 0 ? def.Module : "PSDesiredStateConfiguration", ModuleVersion = def.ModuleVersionInMof };
            int line = 1, col = 1;
            if (a.Src != null) { var t = a.Src.Text; int pos = Math.Min(a.Pos, t.Length); line = 1 + t.Take(pos).Count(ch => ch == '\n'); int ls = t.LastIndexOf('\n', Math.Max(0, pos - 1)); col = pos - (pos == 0 ? -1 : ls); }
            res.SourceInfo = (a.Src != null && !string.IsNullOrEmpty(a.Src.File) ? a.Src.File : "") + "::" + line + "::" + col + "::" + def.Name;
            if (props != null)
                foreach (DictionaryEntry e in props)
                {
                    string k = PsConv.ToStr(e.Key);
                    var p = def.AllProps.FirstOrDefault(x => x.Name.Equals(k, StringComparison.OrdinalIgnoreCase));
                    if (p == null) throw R.RtError(a, "The member '" + k + "' is not valid. Valid members are '" + string.Join("', '", def.AllProps.Select(x => x.Name).OrderBy(x => x).ToArray()) + "'.", "InvalidInstanceProperty", "ParserError", "System.Management.Automation.ParseException", null);
                    object v = PsConv.Base(e.Value);
                    if (v is PsScriptBlock) v = ((PsScriptBlock)v).Text;
                    if (p.Values != null && v != null && PsConv.Items(v).Any(x => !p.Values.Contains(PsConv.ToStr(x), StringComparer.OrdinalIgnoreCase)))
                        throw R.RtError(a, "At least one of the values '" + PsConv.ToStr(v) + "' is not supported or valid for property '" + p.Name + "' on class '" + def.Name + "'. Please specify only supported values: " + string.Join(", ", p.Values) + ".", "UnsupportedValueForProperty", "InvalidOperation", null, null);
                    if (v is bool || p.Type == "bool") v = PsConv.ToBool(v);
                    else if (p.Type.EndsWith("[]", StringComparison.Ordinal) && v != null) v = PsConv.Items(v).Select(x => (object)PsConv.ToStr(x)).ToArray();
                    else if (v != null && !(v is PsObj)) v = PsConv.ToStr(v);
                    res.Props[p.Name] = v;
                }
            foreach (var p in def.Props.Where(x => x.Required && !res.Props.ContainsKey(x.Name)))
                throw R.RtError(a, "The required property '" + p.Name + "' of resource " + res.Id + " has no value. Add " + p.Name + " = ... to the resource block.", "MissingValueForMandatoryProperty", "InvalidOperation", null, null);
            var list = build.NodeList(build.Node ?? "localhost");
            if (list.Any(x => x.Id.Equals(res.Id, StringComparison.OrdinalIgnoreCase)))
                throw R.RtError(a, "A duplicate resource identifier '" + res.Id + "' was found while processing the specification for node '" + (build.Node ?? "localhost") + "'. Change the name of this resource so that it is unique within the node specification.", "DuplicateResourceIdInNodeStatement", "InvalidOperation", null, null);
            list.Add(res);
        }

        // ------------------------------------------------------------ the Local Configuration Manager
        static List<DscRes> InOrder(List<DscRes> l)
        {
            var res = new List<DscRes>();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<DscRes, int> visit = null;
            visit = (r, depth) =>
            {
                if (done.Contains(r.Id) || depth > 50) return;
                foreach (var d in r.DependsOn) { var x = l.FirstOrDefault(y => y.Id.Equals(d, StringComparison.OrdinalIgnoreCase)); if (x != null) visit(x, depth + 1); }
                if (done.Add(r.Id)) res.Add(r);
            };
            foreach (var r in l) visit(r, 0);
            return res;
        }

        static DscCall CallFor(PsRunspace lcm, DscRes r, Action<string> verbose)
        {
            var call = new DscCall { M = lcm.M, R = lcm, Id = r.Id, VerboseFn = verbose };
            foreach (var kv in r.Props) call.P[kv.Key] = kv.Value;
            return call;
        }
        static PsRunspace LcmRunspace(PsMachine m)
        {
            var r = new PsRunspace(m, new NullHost(), "SYSTEM");
            m.Sessions.Remove(r);
            return r;
        }

        /// <summary>Applies a document (Start-DscConfiguration): for each resource Test, then Set if it is not in the desired state.</summary>
        static bool ApplyDoc(PsMachine m, DscDoc doc, Action<string> v, Action<string> fail)
        {
            var lcm = LcmRunspace(m);
            string pre = "[" + m.Name + "]: ";
            string pad = pre + new string(' ', 27);
            bool ok = true;
            m.LcmState = "Busy";
            v(pre + "LCM:  [ Start  Set      ]");
            foreach (var r in InOrder(doc.Resources))
            {
                var def = PsDsc.Find(r.Type);
                string id = "[" + r.Id + "]";
                v(pre + "LCM:  [ Start  Resource ]  " + id);
                if (def == null) { fail("The resource " + r.Id + " uses the class of an unknown resource type " + r.Type + "."); ok = false; break; }
                var call = CallFor(lcm, r, s => v(pad + id + " " + s));
                try
                {
                    v(pre + "LCM:  [ Start  Test     ]  " + id);
                    bool inState = def.Test(call);
                    v(pre + "LCM:  [ End    Test     ]  " + id + "  in 0.0150 seconds.");
                    if (inState) v(pre + "LCM:  [ Skip   Set      ]  " + id);
                    else
                    {
                        v(pre + "LCM:  [ Start  Set      ]  " + id);
                        def.Set(call);
                        v(pre + "LCM:  [ End    Set      ]  " + id + "  in 0.0310 seconds.");
                    }
                }
                catch (DscFailure e)
                {
                    fail("PowerShell DSC resource " + def.MofClass + "  failed to execute Set-TargetResource functionality with error message: " + e.Message);
                    ok = false;
                    break;
                }
                v(pre + "LCM:  [ End    Resource ]  " + id);
            }
            v(pre + "LCM:  [ End    Set      ]");
            v(pre + "LCM:  [ End    Set      ]    in  0.2810 seconds.");
            m.LcmState = "Idle";
            var run = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/MSFT_DSCConfigurationStatus", "Microsoft.Management.Infrastructure.CimInstance", "System.Object");
            run.Val("Status", "string", ok ? "Success" : "Failure");
            run.Val("StartDate", "datetime", m.Now);
            run.Val("Type", "string", m.DscRuns.Count == 0 ? "Initial" : "Reconfiguration");
            run.Val("Mode", "string", "PUSH");
            run.Val("RebootRequested", "string", "False");
            run.Val("NumberOfResources", "uint32", doc.Resources.Count);
            run.Val("ResourcesInDesiredState", "object[]", ok ? doc.Resources.Select(x => (object)x.Id).ToArray() : new object[0]);
            run.Val("ResourcesNotInDesiredState", "object[]", ok ? new object[0] : doc.Resources.Select(x => (object)x.Id).ToArray());
            run.Val("PSComputerName", "string", null);
            m.DscRuns.Insert(0, run);
            return ok;
        }

        static List<KeyValuePair<DscRes, bool>> TestDoc(PsMachine m, DscDoc doc, Action<string> v)
        {
            var lcm = LcmRunspace(m);
            var res = new List<KeyValuePair<DscRes, bool>>();
            string pre = "[" + m.Name + "]: ";
            v(pre + "LCM:  [ Start  Test     ]");
            foreach (var r in InOrder(doc.Resources))
            {
                var def = PsDsc.Find(r.Type);
                string id = "[" + r.Id + "]";
                bool ok = false;
                v(pre + "LCM:  [ Start  Resource ]  " + id);
                v(pre + "LCM:  [ Start  Test     ]  " + id);
                if (def != null) { try { ok = def.Test(CallFor(lcm, r, s => v(pre + new string(' ', 27) + id + " " + s))); } catch (DscFailure) { ok = false; } }
                v(pre + "LCM:  [ End    Test     ]  " + id + " " + (ok ? "True" : "False") + " in 0.0150 seconds.");
                v(pre + "LCM:  [ End    Resource ]  " + id);
                res.Add(new KeyValuePair<DscRes, bool>(r, ok));
            }
            v(pre + "LCM:  [ End    Test     ]     Completed processing test operation. The operation returned " + (res.All(x => x.Value) ? "True" : "False") + ".");
            return res;
        }

        static PsMachine DscTarget(CmdCtx c, string name)
        {
            if (name == null || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "." || name.Equals(c.M.Name, StringComparison.OrdinalIgnoreCase)) return c.M;
            var t = c.M.Net.Get(name);
            if (t == null) { c.WriteError(c.MakeError("The WinRM client cannot process the request because the server name cannot be resolved.", "HRESULT 0x80338000", "ConnectionError", name, "Microsoft.Management.Infrastructure.CimException")); return null; }
            if (!t.WinRmListening) { c.WriteError(WinRmDown(c)); return null; }
            return t;
        }

        static PsErrorRecord NoCurrent(CmdCtx c, string who)
        {
            var r = c.MakeError("Current configuration does not exist. Execute Start-DscConfiguration command with -Path parameter to specify a configuration file and create a current configuration first.", "MI RESULT 1", "NotSpecified", null, "Microsoft.Management.Infrastructure.CimException");
            r.TargetName = "MSFT_DSCLocalConfigurationManager"; r.TargetType = "root/Microsoft/...gurationManager"; r.Reason = "CimException"; r.FullCommandName = who;
            return r;
        }

        static partial void RegisterDsc()
        {
            PsDscHook.StatementFn = DscStatement;
            PsDscHook.InvokeFn = (R, f, c, sc, args, pos) => new DscConfigProc { R = R, Scope = sc, At = c, Redirs = c.Redirs, Position = pos, Func = f, Args = args };
            PsFormat.Table("Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo", PsFormat.P("ImplementedAs", 15), PsFormat.P("Name", 25), PsFormat.P("ModuleName", 30), PsFormat.P("Version", 10), PsFormat.Col("Properties", 0, false, o => Gv(o, "Properties")));
            PsFormat.Table("DscConfigurationJob", PsFormat.PR("Id", 6), PsFormat.P("Name", 15), PsFormat.P("PSJobTypeName", 15), PsFormat.P("State", 13), PsFormat.P("HasMoreData", 13), PsFormat.P("Location", 20), PsFormat.P("Command", 0));
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/MSFT_DSCConfigurationStatus", PsFormat.P("Status", 10), PsFormat.P("StartDate", 25), PsFormat.P("Type", 15), PsFormat.P("Mode", 5), PsFormat.P("RebootRequested", 20), PsFormat.P("NumberOfResources", 0));

            Add("Import-DscResource", DscMod, "ModuleName:object[]@0; Name:string[]; ModuleVersion:string", (c, o) =>
            {
                if (dscBuild == null)
                {
                    var r = c.MakeError("Import-DscResource can only be used inside a Configuration block, where it names the modules whose resources the configuration uses.", "CommandNotFoundException", "ObjectNotFound", "Import-DscResource", "System.Management.Automation.CommandNotFoundException");
                    throw new PsRuntimeException(r, false);
                }
                foreach (var mv in c.Objs("ModuleName"))
                {
                    var h = PsConv.Base(mv) as IDictionary;
                    string mn = h != null ? PsConv.ToStr(PsModMgr.Key(h, "ModuleName")) : PsConv.ToStr(mv);
                    if (!mn.Equals("PSDesiredStateConfiguration", StringComparison.OrdinalIgnoreCase) && !PsDsc.All.Any(d => d.Module.Equals(mn, StringComparison.OrdinalIgnoreCase)) && c.R.FindModuleFile(mn) == null)
                        throw c.Fail("Could not find the module '" + mn + "'.", "ModuleNotFoundDuringParse", "ParserError", null, "System.Management.Automation.ParseException");
                }
            });
            Add("Start-DscConfiguration", DscMod, "Path:string@0; ComputerName:string[] alias=CN; Wait:switch; Force:switch; UseExisting:switch; JobName:string; Credential:pscredential; CimSession:object; ThrottleLimit:int", (c, o) =>
            {
                var R = c.R;
                var work = new List<KeyValuePair<PsMachine, DscDoc>>();
                if (c.Sw("UseExisting"))
                {
                    foreach (var n in c.Strs("ComputerName") ?? new[] { "localhost" }) { var t = DscTarget(c, n); if (t == null) continue; if (t.DscCurrent == null) { c.WriteError(NoCurrent(c, "Start-DscConfiguration")); continue; } work.Add(new KeyValuePair<PsMachine, DscDoc>(t, t.DscCurrent)); }
                }
                else
                {
                    if (!c.Has("Path")) throw c.Fail("Specify -Path (the folder with the .mof files a configuration wrote) or -UseExisting.", "MissingPath", "InvalidArgument", null, "System.ArgumentException");
                    string dir = R.FullFsPath(c.Str("Path"), R.CurrentFsDir);
                    var d = R.FsNode(dir);
                    if (d == null || !d.IsDir) throw c.Fail("Cannot find path '" + dir + "' because it does not exist.", "PathNotFound", "ObjectNotFound", dir, "System.Management.Automation.ItemNotFoundException");
                    var mofs = d.Kids.Values.Where(k => !k.IsDir && k.Name.EndsWith(".mof", StringComparison.OrdinalIgnoreCase) && !k.Name.EndsWith(".meta.mof", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k.Name).ToList();
                    if (c.Has("ComputerName")) mofs = mofs.Where(k => c.Strs("ComputerName").Any(n => k.Name.Equals(n + ".mof", StringComparison.OrdinalIgnoreCase))).ToList();
                    if (mofs.Count == 0) throw c.Fail("The path " + dir + " does not contain any .mof file to apply. Run the configuration first: it writes <node>.mof files there.", "NoMofFound", "ObjectNotFound", dir, "System.IO.FileNotFoundException");
                    foreach (var f in mofs)
                    {
                        string err;
                        var doc = PsDsc.FromMof(f.Text, out err);
                        if (doc == null) { c.WriteError(c.MakeError(err, "MofParseError", "InvalidData", f.FullPath, "System.Exception")); continue; }
                        var t = DscTarget(c, f.Name.Substring(0, f.Name.Length - 4));
                        if (t != null) work.Add(new KeyValuePair<PsMachine, DscDoc>(t, doc));
                    }
                }
                bool wait = c.Sw("Wait");
                bool allOk = true;
                var failures = new List<string>();
                foreach (var w in work)
                {
                    var m = w.Key;
                    if (wait) c.Verbose("Perform operation 'Invoke CimMethod' with following parameters, ''methodName' = SendConfigurationApply,'className' = MSFT_DSCLocalConfigurationManager,'namespaceName' = root/Microsoft/Windows/DesiredStateConfiguration'.");
                    if (wait) c.Verbose("An LCM method call arrived from computer " + c.M.Name + " with user sid S-1-5-21-3623811015-3361044348-30300820-500.");
                    if (w.Value != m.DscCurrent) { m.DscPrevious = m.DscCurrent; m.DscCurrent = w.Value; }
                    m.DscPending = null;
                    bool ok = ApplyDoc(m, w.Value, s => { if (wait) c.Verbose(s); }, s => failures.Add(s));
                    allOk &= ok;
                    if (wait) c.Verbose("Operation 'Invoke CimMethod' complete.");
                }
                if (wait)
                {
                    foreach (var f in failures)
                    {
                        var r = c.MakeError(f, "ProviderOperationExecutionFailure", "InvalidOperation", null, "Microsoft.Management.Infrastructure.CimException");
                        r.TargetName = "root/Microsoft/...gurationManager"; r.TargetType = "String"; r.Reason = "CimException";
                        c.WriteError(r);
                    }
                    if (failures.Count > 0) { var r = c.MakeError("The SendConfigurationApply function did not succeed.", "MI RESULT 1", "NotSpecified", null, "Microsoft.Management.Infrastructure.CimException"); r.TargetName = "root/Microsoft/...gurationManager"; r.TargetType = "String"; r.Reason = "CimException"; c.WriteError(r); }
                    c.Verbose("Time taken for configuration job to complete is 0.281 seconds");
                    return;
                }
                var job = new PsObj("DscConfigurationJob", "System.Management.Automation.Job", "System.Object");
                job.Val("Id", "int", c.M.NextPid % 97 + 1);
                job.Val("Name", "string", c.Str("JobName") ?? "Job" + (c.M.NextPid % 97 + 1));
                job.Val("PSJobTypeName", "string", "Configuration...");
                job.Val("State", "string", allOk ? "Completed" : "Failed");
                job.Val("HasMoreData", "bool", true);
                job.Val("Location", "string", string.Join(", ", work.Select(x => x.Key.Name).ToArray()));
                job.Val("Command", "string", "Start-DscConfiguration...");
                c.Out(job);
            });
            Add("Test-DscConfiguration", DscMod, "ComputerName:string[]@0 alias=CN; Detailed:switch; Path:string; ReferenceConfiguration:string; CimSession:object", (c, o) =>
            {
                foreach (var n in c.Strs("ComputerName") ?? new[] { "localhost" })
                {
                    var m = DscTarget(c, n);
                    if (m == null) continue;
                    DscDoc doc = m.DscCurrent;
                    if (c.Has("ReferenceConfiguration")) { string err; var f = c.R.FsNode(c.R.FullFsPath(c.Str("ReferenceConfiguration"), c.R.CurrentFsDir)); doc = f == null ? null : PsDsc.FromMof(f.Text, out err); }
                    if (doc == null) { c.WriteError(NoCurrent(c, "Test-DscConfiguration")); continue; }
                    var res = TestDoc(m, doc, s => c.Verbose(s));
                    bool all = res.All(x => x.Value);
                    if (!c.Sw("Detailed")) { c.Out(all); continue; }
                    var x2 = PsObj.Custom();
                    x2.TypeNames.Insert(0, "Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/MSFT_DSCConfigurationStatus#Test");
                    x2.Add("PSComputerName", m.Name);
                    x2.Add("ResourcesInDesiredState", res.Where(r => r.Value).Select(r => (object)r.Key.Id).ToArray());
                    x2.Add("ResourcesNotInDesiredState", res.Where(r => !r.Value).Select(r => (object)r.Key.Id).ToArray());
                    x2.Add("InDesiredState", all);
                    c.Out(x2);
                }
            });
            PsFormat.AutoTable("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/MSFT_DSCConfigurationStatus#Test", PsFormat.P("PSComputerName", 0), PsFormat.Col("ResourcesInDesiredState", 0, false, o => Braces(Gv(o, "ResourcesInDesiredState"))), PsFormat.Col("ResourcesNotInDesiredState", 0, false, o => Braces(Gv(o, "ResourcesNotInDesiredState"))), PsFormat.P("InDesiredState", 0));
            Add("Get-DscConfiguration", DscMod, "CimSession:object; ThrottleLimit:int", (c, o) =>
            {
                var m = c.M;
                if (m.DscCurrent == null) { c.WriteError(NoCurrent(c, "Get-DscConfiguration")); return; }
                var lcm = LcmRunspace(m);
                foreach (var r in m.DscCurrent.Resources)
                {
                    var def = PsDsc.Find(r.Type);
                    if (def == null) continue;
                    Dictionary<string, object> got;
                    try { got = def.Get(CallFor(lcm, r, s => { })); }
                    catch (DscFailure e) { c.WriteError(c.MakeError(e.Message, "ProviderOperationExecutionFailure", "InvalidOperation", null, "Microsoft.Management.Infrastructure.CimException")); continue; }
                    var x = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/" + def.MofClass, "Microsoft.Management.Infrastructure.CimInstance#" + def.MofClass, "Microsoft.Management.Infrastructure.CimInstance", "System.Object");
                    x.Val("ConfigurationName", "string", m.DscCurrent.Name);
                    x.Val("DependsOn", "string[]", r.DependsOn.Count == 0 ? null : r.DependsOn.Cast<object>().ToArray());
                    x.Val("ModuleName", "string", r.ModuleName);
                    x.Val("ModuleVersion", "string", r.ModuleVersion);
                    x.Val("PsDscRunAsCredential", "pscredential", null);
                    x.Val("ResourceId", "string", r.Id);
                    x.Val("SourceInfo", "string", r.SourceInfo);
                    foreach (var kv in got) x.Val(kv.Key, "object", kv.Value);
                    x.Val("PSComputerName", "string", null);
                    x.ToStr = y => r.Id;
                    c.Out(x);
                }
            });
            Add("Get-DscResource", DscMod, "Name:string[]@0; Module:object@1; Syntax:switch", (c, o) =>
            {
                foreach (var d in PsDsc.All.Where(dr => MatchAny(c.Strs("Name"), dr.Name) && (!c.Has("Module") || PsConv.Like(dr.Module.Length == 0 ? "PSDesiredStateConfiguration" : dr.Module, c.Str("Module"), false))))
                {
                    var props = d.AllProps.OrderBy(p => p.Key ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    if (c.Sw("Syntax"))
                    {
                        var sb = new StringBuilder(d.Name + " [String] #ResourceName\n{\n");
                        foreach (var p in props)
                        {
                            string t = "[" + p.Type + "]" + (p.Values != null ? "{ " + string.Join(" | ", p.Values.OrderBy(q => q).ToArray()) + " }" : "");
                            sb.Append("    ").Append(p.Required ? p.Name + " = " + t : "[" + p.Name + " = " + t + "]").Append("\n");
                        }
                        sb.Append("}\n");
                        c.Out(sb.ToString());
                        continue;
                    }
                    var x = new PsObj("Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo", "System.Object");
                    x.Val("ImplementedAs", "string", d.ImplementedAs);
                    x.Val("Name", "string", d.Name);
                    x.Val("ResourceType", "string", d.MofClass);
                    x.Val("ModuleName", "string", d.Module);
                    x.Val("Version", "string", d.Version);
                    x.Val("Properties", "object[]", props.Select(p => (object)p.Name).ToArray());
                    x.Val("FriendlyName", "string", d.Name);
                    x.Val("ParentPath", "string", "C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\Modules\\PSDesiredStateConfiguration\\DSCResources\\" + d.MofClass);
                    x.ToStr = y => d.Name;
                    c.Out(x);
                }
            });
            Add("Get-DscLocalConfigurationManager", DscMod, "CimSession:object", (c, o) =>
            {
                var m = c.M;
                if (m.LcmAgentId == null) m.LcmAgentId = m.RandomHex(8).ToUpperInvariant() + "-" + m.RandomHex(4).ToUpperInvariant() + "-11F0-" + m.RandomHex(4).ToUpperInvariant() + "-" + m.RandomHex(12).ToUpperInvariant();
                var x = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DesiredStateConfiguration/MSFT_DSCMetaConfiguration", "Microsoft.Management.Infrastructure.CimInstance", "System.Object");
                x.Val("ActionAfterReboot", "string", "ContinueConfiguration"); x.Val("AgentId", "string", m.LcmAgentId); x.Val("AllowModuleOverWrite", "bool", false);
                x.Val("CertificateID", "string", null); x.Val("ConfigurationDownloadManagers", "object[]", new object[0]); x.Val("ConfigurationID", "string", null);
                x.Val("ConfigurationMode", "string", "ApplyAndMonitor"); x.Val("ConfigurationModeFrequencyMins", "uint32", 15); x.Val("Credential", "pscredential", null);
                x.Val("DebugMode", "string[]", new object[] { "NONE" }); x.Val("DownloadManagerCustomData", "object", null); x.Val("DownloadManagerName", "string", null);
                x.Val("LCMCompatibleVersions", "string[]", new object[] { "1.0", "2.0" }); x.Val("LCMState", "string", m.LcmState); x.Val("LCMStateDetail", "string", null); x.Val("LCMVersion", "string", "2.0");
                x.Val("StatusRetentionTimeInDays", "uint32", 10); x.Val("SignatureValidationPolicy", "string", "NONE"); x.Val("SignatureValidations", "object[]", new object[0]);
                x.Val("MaximumDownloadSizeMB", "uint32", 500); x.Val("PartialConfigurations", "object", null); x.Val("RebootNodeIfNeeded", "bool", false);
                x.Val("RefreshFrequencyMins", "uint32", 30); x.Val("RefreshMode", "string", "PUSH"); x.Val("ReportManagers", "object[]", new object[0]); x.Val("ResourceModuleManagers", "object[]", new object[0]);
                x.Val("PSComputerName", "string", null);
                c.Out(x);
            });
            Add("Remove-DscConfigurationDocument", DscMod, "Stage:string[]@0 ! set=Current|Pending|Previous; Force:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                foreach (var s in c.Strs("Stage"))
                {
                    if (!c.ShouldProcess(s, "Remove-DscConfigurationDocument")) continue;
                    if (s.Equals("Current", StringComparison.OrdinalIgnoreCase)) m.DscCurrent = null;
                    else if (s.Equals("Pending", StringComparison.OrdinalIgnoreCase)) m.DscPending = null;
                    else m.DscPrevious = null;
                }
            }).ShouldProcess = true;
            Add("Get-DscConfigurationStatus", DscMod, "All:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                if (m.DscRuns.Count == 0) { c.WriteError(c.MakeError("Cannot find the status of any configuration run: no configuration was applied to this computer yet.", "MI RESULT 1", "ObjectNotFound", null, "Microsoft.Management.Infrastructure.CimException")); return; }
                foreach (var r in c.Sw("All") ? m.DscRuns : m.DscRuns.Take(1).ToList()) c.Out(r);
            });
        }
        static string Braces(object v) { var l = PsConv.Items(v).Select(x => PsConv.ToStr(x)).ToList(); return l.Count == 0 ? "" : "{" + string.Join(", ", l.ToArray()) + "}"; }
    }
}
