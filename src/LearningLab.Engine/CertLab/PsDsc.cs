// Cert Lab - Desired State Configuration in the simulated Windows PowerShell 5.1, part 1: the resource registry, the
// built-in resources (File, Registry, Environment, Script, Log), the documents a configuration compiles to, and the
// MOF text (our own MOF-like layout: one "instance of" block per resource plus OMI_ConfigurationDocument) that is
// written to <node>.mof and read back by Start-DscConfiguration.
//
// Adding a resource (Service, WindowsFeature, User, Group... live in other files): call PsDsc.Register once, for
// example from a RegisterXxx partial method:
//
//   PsDsc.Register(new DscResourceDef { Name = "Service", MofClass = "MSFT_ServiceResource",
//       Props = DscProp.List("Name:string key; State:string=Running|Stopped; StartupType:string=Automatic|Manual|Disabled; Ensure:string=Present|Absent"),
//       Test = call => ..., Set = call => ..., Get = call => new Dictionary<string, object> { ... } });
//
// Test returns true when the machine is already as described; Set makes it so (call.Verbose writes the
// "[[Service]Spooler] ..." lines, throw call.Fail("why") reports a failure); Get returns the current values.
// call.P holds the properties from the configuration (strings, bools, string arrays), call.M is the machine.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class DscProp
    {
        public string Name = "", Type = "string";
        public bool Key, Required;
        public string[] Values;
        /// <summary>"Name:type key; Other:string=A|B; Flag:bool" (key = key property, req = required, =A|B = the allowed values).</summary>
        public static List<DscProp> List(string spec)
        {
            var l = new List<DscProp>();
            foreach (var raw in spec.Split(';'))
            {
                var w = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                var p = new DscProp();
                var head = w[0]; int eq = head.IndexOf('=');
                if (eq > 0) { p.Values = head.Substring(eq + 1).Split('|'); head = head.Substring(0, eq); }
                int colon = head.IndexOf(':');
                p.Name = colon < 0 ? head : head.Substring(0, colon);
                if (colon > 0) p.Type = head.Substring(colon + 1);
                p.Key = w.Contains("key"); p.Required = p.Key || w.Contains("req");
                l.Add(p);
            }
            return l;
        }
    }

    public class DscCall
    {
        public PsMachine M; public PsRunspace R; public string Id = "";
        public Dictionary<string, object> P = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public Action<string> VerboseFn;
        public void Verbose(string s) { if (VerboseFn != null) VerboseFn(s); }
        public bool Has(string n) { return P.ContainsKey(n) && P[n] != null; }
        public string Str(string n) { object v; return P.TryGetValue(n, out v) && v != null ? PsConv.ToStr(v) : null; }
        public string Str(string n, string def) { return Str(n) ?? def; }
        public bool Bool(string n) { object v; return P.TryGetValue(n, out v) && v != null && PsConv.ToBool(v); }
        public string[] Strs(string n) { object v; return P.TryGetValue(n, out v) && v != null ? PsConv.Items(v).Select(x => PsConv.ToStr(x)).ToArray() : null; }
        public DscFailure Fail(string msg) { return new DscFailure(msg); }
    }
    public class DscFailure : Exception { public DscFailure(string m) : base(m) { } }

    public class DscResourceDef
    {
        public string Name = "", Module = "PSDesiredStateConfiguration", Version = "1.1", ImplementedAs = "PowerShell", MofClass = "", ModuleVersionInMof = "1.0";
        public List<DscProp> Props = new List<DscProp>();
        public Func<DscCall, bool> Test;
        public Action<DscCall> Set;
        public Func<DscCall, Dictionary<string, object>> Get;
        public IEnumerable<DscProp> AllProps
        {
            get
            {
                foreach (var p in Props) yield return p;
                yield return new DscProp { Name = "DependsOn", Type = "string[]" };
                yield return new DscProp { Name = "PsDscRunAsCredential", Type = "PSCredential" };
            }
        }
    }

    /// <summary>One resource of a compiled configuration.</summary>
    public class DscRes
    {
        public string Type = "", Name = "", SourceInfo = "", ConfigurationName = "", ModuleName = "PSDesiredStateConfiguration", ModuleVersion = "1.0";
        public Dictionary<string, object> Props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public string Id { get { return "[" + Type + "]" + Name; } }
        public List<string> DependsOn { get { object v; return Props.TryGetValue("DependsOn", out v) && v != null ? PsConv.Items(v).Select(x => PsConv.ToStr(x)).ToList() : new List<string>(); } }
    }

    public class DscDoc
    {
        public string Name = "", Node = "localhost", Author = "", Host = "", Date = "";
        public List<DscRes> Resources = new List<DscRes>();
    }

    public partial class PsMachine
    {
        public DscDoc DscCurrent, DscPrevious, DscPending;
        public string LcmState = "Idle";
        public string LcmAgentId;
        public readonly List<PsObj> DscRuns = new List<PsObj>();
    }

    public static class PsDsc
    {
        static readonly Dictionary<string, DscResourceDef> defs = new Dictionary<string, DscResourceDef>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();
        static bool done;

        public static void Register(DscResourceDef d) { lock (gate) defs[d.Name] = d; }
        public static DscResourceDef Find(string name) { Init(); lock (gate) { DscResourceDef d; return defs.TryGetValue(name, out d) ? d : null; } }
        public static DscResourceDef ByClass(string cls) { Init(); lock (gate) return defs.Values.FirstOrDefault(d => d.MofClass.Equals(cls, StringComparison.OrdinalIgnoreCase)); }
        public static List<DscResourceDef> All { get { Init(); lock (gate) return defs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }

        static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                RegisterBuiltins();
                PsDscAdmin.RegisterAll();   // Service, User, Group, WindowsFeature (PsDscAdmin.cs)
            }
        }

        // ------------------------------------------------------------ the built-in resources
        static WinFile Node(DscCall c, string path) { return c.M.FindFile(c.R.FullFsPath(path, "C:\\")); }

        static void RegisterBuiltins()
        {
            Register(new DscResourceDef
            {
                Name = "File", ImplementedAs = "Binary", Module = "", Version = "", MofClass = "MSFT_FileDirectoryConfiguration",
                Props = DscProp.List("DestinationPath:string key; Attributes:string[]=Archive|Hidden|ReadOnly|System; Checksum:string=CreatedDate|ModifiedDate|SHA-1|SHA-256|SHA-512; Contents:string; Credential:PSCredential; Ensure:string=Absent|Present; Force:bool; MatchSource:bool; Recurse:bool; SourcePath:string; Type:string=Directory|File"),
                Test = c =>
                {
                    string path = c.R.FullFsPath(c.Str("DestinationPath"), "C:\\");
                    var f = c.M.FindFile(path);
                    bool dir = c.Str("Type", "File").Equals("Directory", StringComparison.OrdinalIgnoreCase);
                    bool absent = c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase);
                    if (absent) { if (f != null) c.Verbose("The destination object was found and is not in the desired state (Ensure Absent): " + path + "."); return f == null; }
                    if (f == null) { c.Verbose("The system cannot find the file specified."); c.Verbose("The related file/directory is: " + path + "."); return false; }
                    if (f.IsDir != dir) { c.Verbose("The destination object is a " + (f.IsDir ? "directory" : "file") + ", not a " + (dir ? "directory" : "file") + ": " + path + "."); return false; }
                    if (!dir && c.Has("Contents") && f.Text != c.Str("Contents")) { c.Verbose("The file contents are not as specified: " + path + "."); return false; }
                    if (c.Has("SourcePath"))
                    {
                        var src = Node(c, c.Str("SourcePath"));
                        if (src != null && !src.IsDir && !dir && src.Text != f.Text) { c.Verbose("The destination file differs from the source file: " + path + "."); return false; }
                        if (src != null && src.IsDir && dir && c.Bool("Recurse") && !SameTree(src, f)) { c.Verbose("The destination folder is missing files from the source folder: " + path + "."); return false; }
                    }
                    return true;
                },
                Set = c =>
                {
                    string path = c.R.FullFsPath(c.Str("DestinationPath"), "C:\\");
                    var f = c.M.FindFile(path);
                    bool dir = c.Str("Type", "File").Equals("Directory", StringComparison.OrdinalIgnoreCase);
                    if (c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase))
                    {
                        if (f != null && f.Parent != null) { f.Parent.Kids.Remove(f.Name); c.Verbose("Deleted " + path + "."); }
                        return;
                    }
                    WinFile src = c.Has("SourcePath") ? Node(c, c.Str("SourcePath")) : null;
                    if (c.Has("SourcePath") && src == null) throw c.Fail("The system cannot find the path specified. The related file/directory is: " + c.Str("SourcePath") + ".");
                    if (f != null && f.IsDir != dir) { if (!c.Bool("Force")) throw c.Fail("The destination object exists as a " + (f.IsDir ? "directory" : "file") + ". Use Force = $true to replace it: " + path + "."); f.Parent.Kids.Remove(f.Name); f = null; }
                    if (dir)
                    {
                        var d = c.M.MakeDir(path, c.M.Now);
                        if (src != null && src.IsDir) CopyTree(c.M, src, d, c.Bool("Recurse"));
                        c.Verbose("Created the directory " + path + ".");
                        return;
                    }
                    string text = src != null && !src.IsDir ? src.Text : c.Str("Contents", "");
                    int i = path.LastIndexOf('\\');
                    c.M.MakeDir(path.Substring(0, i), c.M.Now);
                    if (f == null) { f = c.M.File(path, text, c.M.Now); f.Encoding = "utf8"; } else { f.Text = text; f.Modified = c.M.Now; f.FakeSize = -1; f.Bytes = null; }
                    c.Verbose("Wrote the file " + path + ".");
                },
                Get = c =>
                {
                    string path = c.R.FullFsPath(c.Str("DestinationPath"), "C:\\");
                    var f = c.M.FindFile(path);
                    return new Dictionary<string, object> { { "Attributes", f == null ? null : new object[] { f.IsDir ? "Directory" : "Archive" } }, { "Checksum", null }, { "Contents", null }, { "CreatedDate", f == null ? null : (object)f.Created }, { "Credential", null }, { "DestinationPath", path }, { "Ensure", f == null ? "Absent" : "Present" }, { "Force", null }, { "MatchSource", null }, { "ModifiedDate", f == null ? null : (object)f.Modified }, { "Recurse", null }, { "Size", f == null ? null : (object)f.Length }, { "SourcePath", c.Str("SourcePath") }, { "SubItems", null }, { "Type", f == null ? null : f.IsDir ? "Directory" : "File" } };
                }
            });
            Register(new DscResourceDef
            {
                Name = "Registry", MofClass = "MSFT_RegistryResource",
                Props = DscProp.List("Key:string key; ValueName:string key; Ensure:string=Present|Absent; Force:bool; Hex:bool; ValueData:string[]; ValueType:string=String|Binary|Dword|Qword|MultiString|ExpandString"),
                Test = c =>
                {
                    var k = RegFind(c, false);
                    bool absent = c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase);
                    string vn = c.Str("ValueName", "");
                    if (k == null) { if (!absent) c.Verbose("Registry key '" + c.Str("Key") + "' does not exist."); return absent; }
                    if (vn.Length == 0 && !c.Has("ValueData")) return !absent;
                    var v = k.Value(vn);
                    if (absent) return v == null;
                    if (v == null) { c.Verbose("Registry key value '" + c.Str("Key") + "\\" + vn + "' does not exist."); return false; }
                    if (c.Has("ValueData") && RegText(v.Data) != string.Join("\n", c.Strs("ValueData"))) { c.Verbose("Registry key value '" + c.Str("Key") + "\\" + vn + "' has a different value."); return false; }
                    if (c.Has("ValueType") && !v.Kind.Equals(c.Str("ValueType"), StringComparison.OrdinalIgnoreCase)) { c.Verbose("Registry key value '" + c.Str("Key") + "\\" + vn + "' is of a different type."); return false; }
                    return true;
                },
                Set = c =>
                {
                    string vn = c.Str("ValueName", "");
                    if (c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase))
                    {
                        var k0 = RegFind(c, false);
                        if (k0 == null) return;
                        if (vn.Length > 0) k0.Values.RemoveAll(x => x.Name.Equals(vn, StringComparison.OrdinalIgnoreCase));
                        else if (k0.Parent != null) k0.Parent.Kids.Remove(k0.Name);
                        return;
                    }
                    var k = RegFind(c, true);
                    if (k == null) throw c.Fail("The registry key path '" + c.Str("Key") + "' does not start with a valid hive (HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER...).");
                    if (vn.Length == 0 && !c.Has("ValueData")) return;
                    string type = c.Str("ValueType", "String");
                    var data = c.Strs("ValueData") ?? new string[0];
                    object v;
                    switch (type.ToLowerInvariant())
                    {
                        case "dword": v = c.Bool("Hex") ? Convert.ToInt32(data.FirstOrDefault() ?? "0", 16) : int.Parse(data.FirstOrDefault() ?? "0", CultureInfo.InvariantCulture); type = "DWord"; break;
                        case "qword": v = c.Bool("Hex") ? Convert.ToInt64(data.FirstOrDefault() ?? "0", 16) : long.Parse(data.FirstOrDefault() ?? "0", CultureInfo.InvariantCulture); type = "QWord"; break;
                        case "multistring": v = data; type = "MultiString"; break;
                        case "expandstring": v = data.FirstOrDefault() ?? ""; type = "ExpandString"; break;
                        case "binary": v = data.FirstOrDefault() ?? ""; type = "Binary"; break;
                        default: v = data.FirstOrDefault() ?? ""; type = "String"; break;
                    }
                    k.Set(vn, v, type);
                    c.Verbose("Registry key value '" + c.Str("Key") + "\\" + vn + "' was set.");
                },
                Get = c =>
                {
                    var k = RegFind(c, false);
                    var v = k == null ? null : k.Value(c.Str("ValueName", ""));
                    return new Dictionary<string, object> { { "Ensure", v != null || (k != null && c.Str("ValueName", "").Length == 0) ? "Present" : "Absent" }, { "Force", null }, { "Hex", null }, { "Key", c.Str("Key") }, { "ValueData", v == null ? null : (object)RegText(v.Data).Split('\n').Cast<object>().ToArray() }, { "ValueName", c.Str("ValueName") }, { "ValueType", v == null ? null : v.Kind } };
                }
            });
            Register(new DscResourceDef
            {
                Name = "Environment", MofClass = "MSFT_EnvironmentResource",
                Props = DscProp.List("Name:string key; Ensure:string=Present|Absent; Path:bool; Value:string"),
                Test = c =>
                {
                    string cur; c.M.EnvMachine.TryGetValue(c.Str("Name"), out cur);
                    bool absent = c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase);
                    if (c.Bool("Path"))
                    {
                        var parts = (c.Str("Value") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        var have = (cur ?? "").Split(';');
                        bool all = parts.All(p => have.Contains(p, StringComparer.OrdinalIgnoreCase)), none = parts.All(p => !have.Contains(p, StringComparer.OrdinalIgnoreCase));
                        return absent ? none : all;
                    }
                    if (absent) return cur == null;
                    if (cur == null) { c.Verbose("Environment variable '" + c.Str("Name") + "' does not exist."); return false; }
                    if (c.Has("Value") && cur != c.Str("Value")) { c.Verbose("Environment variable '" + c.Str("Name") + "' has the value '" + cur + "'."); return false; }
                    return true;
                },
                Set = c =>
                {
                    string n = c.Str("Name"), cur; c.M.EnvMachine.TryGetValue(n, out cur);
                    bool absent = c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase);
                    if (c.Bool("Path"))
                    {
                        var have = (cur ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        foreach (var p in (c.Str("Value") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (absent) have.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase));
                            else if (!have.Contains(p, StringComparer.OrdinalIgnoreCase)) have.Add(p);
                        }
                        c.M.EnvMachine[n] = string.Join(";", have.ToArray());
                    }
                    else if (absent) c.M.EnvMachine.Remove(n);
                    else c.M.EnvMachine[n] = c.Str("Value") ?? "";
                    c.Verbose("Environment variable '" + n + "' was " + (absent ? "removed." : "set."));
                },
                Get = c =>
                {
                    string cur; c.M.EnvMachine.TryGetValue(c.Str("Name"), out cur);
                    return new Dictionary<string, object> { { "Ensure", cur == null ? "Absent" : "Present" }, { "Name", c.Str("Name") }, { "Path", null }, { "Value", cur } };
                }
            });
            Register(new DscResourceDef
            {
                Name = "Script", MofClass = "MSFT_ScriptResource",
                Props = DscProp.List("GetScript:string req; SetScript:string req; TestScript:string req; Credential:PSCredential; Result:string"),
                Test = c =>
                {
                    var outp = RunScript(c, "TestScript");
                    if (outp.Count == 0 || !(PsConv.Base(outp[outp.Count - 1]) is bool)) throw c.Fail("Failure to get a valid result from the execution of TestScript. The Test script should return True or False.");
                    return (bool)PsConv.Base(outp[outp.Count - 1]);
                },
                Set = c => { RunScript(c, "SetScript"); },
                Get = c =>
                {
                    var outp = RunScript(c, "GetScript");
                    var h = outp.Select(x => PsConv.Base(x) as IDictionary).FirstOrDefault(x => x != null);
                    if (h == null) throw c.Fail("Failure to get the results from the script in a hash table format.");
                    return new Dictionary<string, object> { { "GetScript", c.Str("GetScript") }, { "Result", PsModMgr.Key(h, "Result") }, { "SetScript", c.Str("SetScript") }, { "TestScript", c.Str("TestScript") } };
                }
            });
            Register(new DscResourceDef
            {
                Name = "Log", MofClass = "MSFT_LogResource",
                Props = DscProp.List("Message:string key"),
                Test = c => false,
                Set = c => c.Verbose(c.Str("Message")),
                Get = c => new Dictionary<string, object> { { "Message", c.Str("Message") } }
            });
        }

        static bool SameTree(WinFile src, WinFile dst)
        {
            foreach (var k in src.Kids.Values)
            {
                var d = dst.Kid(k.Name);
                if (d == null || d.IsDir != k.IsDir) return false;
                if (k.IsDir && !SameTree(k, d)) return false;
                if (!k.IsDir && d.Text != k.Text) return false;
            }
            return true;
        }
        static void CopyTree(PsMachine m, WinFile src, WinFile dst, bool recurse)
        {
            foreach (var k in src.Kids.Values.ToList())
            {
                if (k.IsDir) { if (recurse) CopyTree(m, k, m.MakeDir(dst.FullPath + "\\" + k.Name, m.Now), true); continue; }
                var c = k.CloneTree(); c.Modified = m.Now; dst.Add(c);
            }
        }

        static RegKey RegFind(DscCall c, bool create)
        {
            string key = c.Str("Key", "").Replace("/", "\\");
            RegKey hive = null; string rest = null;
            foreach (var pre in new[] { "HKEY_LOCAL_MACHINE\\", "HKLM:\\", "HKLM\\" }) if (key.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) { hive = c.M.HKLM; rest = key.Substring(pre.Length); }
            foreach (var pre in new[] { "HKEY_CURRENT_USER\\", "HKCU:\\", "HKCU\\" }) if (key.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) { hive = c.M.HKCU; rest = key.Substring(pre.Length); }
            if (hive == null) return null;
            return create ? hive.Sub(rest) : hive.Find(rest);
        }
        static string RegText(object v) { if (v is string[]) return string.Join("\n", (string[])v); return PsConv.ToStr(v); }

        static List<object> RunScript(DscCall c, string which)
        {
            var text = c.Str(which) ?? "";
            ScriptBlockAst ast;
            try { ast = PsParser.Parse(text, ""); }
            catch (PsParseError e) { throw c.Fail(e.Message); }
            try { return c.R.InvokeBlock(new PsScriptBlock { Ast = ast }, c.R.Global, null, null, false); }
            catch (PsRuntimeException e) { throw c.Fail(e.Rec.Exception.Message); }
        }

        // ------------------------------------------------------------ MOF
        static string MofValue(object v)
        {
            v = PsConv.Base(v);
            if (v is bool) return (bool)v ? "True" : "False";
            if (v is PsSwitch) return PsConv.ToBool(v) ? "True" : "False";
            if (PsConv.IsNumber(v)) return PsConv.ToStr(v);
            if (v is PsScriptBlock) v = ((PsScriptBlock)v).Text;
            if (PsConv.IsCollection(v) && !(v is string)) return "{\n" + string.Join(",\n", PsConv.Items(v).Select(x => "    " + MofValue(x)).ToArray()) + "}";
            var s = PsConv.ToStr(v);
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }

        public static string ToMof(DscDoc d)
        {
            var sb = new StringBuilder();
            sb.Append("/*\n@TargetNode='").Append(d.Node).Append("'\n@GeneratedBy=").Append(d.Author).Append("\n@GenerationDate=").Append(d.Date).Append("\n@GenerationHost=").Append(d.Host).Append("\n*/\n\n");
            var count = new Dictionary<string, int>();
            foreach (var r in d.Resources)
            {
                var def = Find(r.Type);
                string cls = def == null ? r.Type : def.MofClass;
                int n; count.TryGetValue(cls, out n); count[cls] = ++n;
                sb.Append("instance of ").Append(cls).Append(" as $").Append(cls).Append(n).Append("ref\n{\n");
                sb.Append("ResourceID = ").Append(MofValue(r.Id)).Append(";\n");
                foreach (var kv in r.Props) if (kv.Value != null) sb.Append(" ").Append(kv.Key).Append(" = ").Append(MofValue(kv.Value)).Append(";\n");
                sb.Append(" SourceInfo = ").Append(MofValue(r.SourceInfo)).Append(";\n");
                sb.Append(" ModuleName = ").Append(MofValue(r.ModuleName)).Append(";\n");
                sb.Append(" ModuleVersion = ").Append(MofValue(r.ModuleVersion)).Append(";\n\n");
                sb.Append(" ConfigurationName = ").Append(MofValue(d.Name)).Append(";\n\n};\n");
            }
            sb.Append("instance of OMI_ConfigurationDocument\n\n                    {\n Version=\"2.0.0\";\n \n                        MinimumCompatibleVersion = \"1.0.0\";\n \n                        CompatibleVersionAdditionalProperties= {\"Omi_BaseResource:ConfigurationName\"};\n \n                        Author=")
              .Append(MofValue(d.Author)).Append(";\n \n                        GenerationDate=").Append(MofValue(d.Date)).Append(";\n \n                        GenerationHost=").Append(MofValue(d.Host)).Append(";\n \n                        Name=").Append(MofValue(d.Name)).Append(";\n\n                    };\n");
            return sb.ToString().Replace("\n", "\r\n");
        }

        /// <summary>Reads a .mof file written by ToMof (or edited by hand in the same layout). Null when it is not one.</summary>
        public static DscDoc FromMof(string text, out string error)
        {
            error = null;
            var d = new DscDoc();
            var t = text ?? "";
            int i = 0;
            var tn = System.Text.RegularExpressions.Regex.Match(t, "@TargetNode='([^']*)'");
            if (tn.Success) d.Node = tn.Groups[1].Value;
            try
            {
                while (true)
                {
                    int at = t.IndexOf("instance of ", i, StringComparison.Ordinal);
                    if (at < 0) break;
                    i = at + 12;
                    int e = i; while (e < t.Length && !char.IsWhiteSpace(t[e]) && t[e] != '{') e++;
                    string cls = t.Substring(i, e - i);
                    int open = t.IndexOf('{', e);
                    i = open + 1;
                    var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    while (true)
                    {
                        while (i < t.Length && (char.IsWhiteSpace(t[i]) || t[i] == ';')) i++;
                        if (t[i] == '}') { i++; break; }
                        int ns = i; while (char.IsLetterOrDigit(t[i]) || t[i] == '_') i++;
                        string name = t.Substring(ns, i - ns);
                        while (char.IsWhiteSpace(t[i])) i++;
                        if (t[i] != '=') throw new FormatException("expected = after " + name);
                        i++;
                        props[name] = ReadValue(t, ref i);
                    }
                    if (cls == "OMI_ConfigurationDocument")
                    {
                        d.Name = PsConv.ToStr(Get(props, "Name") ?? d.Name); d.Author = PsConv.ToStr(Get(props, "Author") ?? ""); d.Host = PsConv.ToStr(Get(props, "GenerationHost") ?? ""); d.Date = PsConv.ToStr(Get(props, "GenerationDate") ?? "");
                        continue;
                    }
                    var def = ByClass(cls);
                    var r = new DscRes { Type = def == null ? cls : def.Name };
                    string id = PsConv.ToStr(Get(props, "ResourceID") ?? "");
                    if (id.StartsWith("[", StringComparison.Ordinal) && id.Contains("]")) { r.Type = id.Substring(1, id.IndexOf(']') - 1); r.Name = id.Substring(id.IndexOf(']') + 1); }
                    r.SourceInfo = PsConv.ToStr(Get(props, "SourceInfo") ?? "");
                    r.ModuleName = PsConv.ToStr(Get(props, "ModuleName") ?? "");
                    r.ModuleVersion = PsConv.ToStr(Get(props, "ModuleVersion") ?? "");
                    r.ConfigurationName = PsConv.ToStr(Get(props, "ConfigurationName") ?? "");
                    foreach (var kv in props) if (!new[] { "ResourceID", "SourceInfo", "ModuleName", "ModuleVersion", "ConfigurationName" }.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) r.Props[kv.Key] = kv.Value;
                    d.Resources.Add(r);
                }
            }
            catch (Exception ex)
            {
                if (ex is IndexOutOfRangeException || ex is FormatException || ex is ArgumentOutOfRangeException) { error = "The MOF file could not be read: " + ex.Message; return null; }
                throw;
            }
            if (d.Name.Length == 0 && d.Resources.Count > 0) d.Name = d.Resources[0].ConfigurationName;
            return d;
        }
        static object Get(Dictionary<string, object> p, string k) { object v; return p.TryGetValue(k, out v) ? v : null; }
        static object ReadValue(string t, ref int i)
        {
            while (char.IsWhiteSpace(t[i])) i++;
            if (t[i] == '"')
            {
                var sb = new StringBuilder(); i++;
                while (t[i] != '"')
                {
                    if (t[i] == '\\') { i++; char e = t[i]; sb.Append(e == 'n' ? '\n' : e == 'r' ? '\r' : e == 't' ? '\t' : e); i++; continue; }
                    sb.Append(t[i]); i++;
                }
                i++;
                return sb.ToString();
            }
            if (t[i] == '{')
            {
                i++;
                var l = new List<object>();
                while (true)
                {
                    while (char.IsWhiteSpace(t[i]) || t[i] == ',') i++;
                    if (t[i] == '}') { i++; break; }
                    l.Add(ReadValue(t, ref i));
                }
                return l.ToArray();
            }
            int s = i; while (t[i] != ';' && t[i] != ',' && t[i] != '}' && !char.IsWhiteSpace(t[i])) i++;
            string w = t.Substring(s, i - s);
            if (w.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
            if (w.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
            long n; if (long.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n >= int.MinValue && n <= int.MaxValue ? (object)(int)n : n;
            return w;
        }
    }
}
