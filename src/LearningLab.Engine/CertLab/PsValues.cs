// Cert Lab - the simulated PowerShell's values: PsObj (every simulated object: PSCustomObject, services, files,
// processes...), enums, the type table ([int], [string], [datetime]...), and PowerShell's conversion rules (what is
// true, how a value turns into text, how -eq and sorting compare). Only a short whitelist of .NET types (strings,
// numbers, dates, collections, Math, Regex, StringBuilder) is ever touched by reflection, so nothing a learner types
// can reach the real PC.
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
    /// <summary>"Nothing came out" (AutomationNull): a command or function that wrote no output.</summary>
    public sealed class AutoNull { public static readonly AutoNull Value = new AutoNull(); AutoNull() { } public override string ToString() { return ""; } }

    public class PsProp
    {
        public string Name = "", Kind = "NoteProperty", TypeName = "System.Object", AliasOf;
        public object Value;
        public Func<object> Getter;
        public Action<object> Setter;
        public bool ReadOnly;
        public PsScriptBlock Script, SetScript;   // ScriptProperty
        public PsProp Clone() { return (PsProp)MemberwiseClone(); }
    }

    public delegate object PsMethodFn(PsObj self, object[] args);
    public class PsMethod { public string Name = "", Definition = ""; public PsMethodFn Fn; public PsScriptBlock Script; }

    /// <summary>A simulated object: ordered properties, methods and type names (TypeNames[0] picks the format).</summary>
    public class PsObj
    {
        public readonly List<string> TypeNames = new List<string>();
        readonly List<PsProp> props = new List<PsProp>();
        readonly Dictionary<string, PsProp> index = new Dictionary<string, PsProp>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, PsMethod> Methods = new Dictionary<string, PsMethod>(StringComparer.OrdinalIgnoreCase);
        public string[] DefaultDisplay;       // the DefaultDisplayPropertySet (what a table / list shows by default)
        public Func<PsObj, string> ToStr;     // what "$obj" and ToString() give
        public object Base;                   // the machine's own record behind it (a service, a file...)
        public string Computer;               // set on objects that came back from another machine
        public bool FileNameOnly;             // Get-ChildItem of a folder: ToString() is only the name (Windows PowerShell 5.1)

        public PsObj() { }
        public PsObj(params string[] typeNames) { TypeNames.AddRange(typeNames); }
        public static PsObj Custom()
        {
            var o = new PsObj();
            o.TypeNames.Add("System.Management.Automation.PSCustomObject"); o.TypeNames.Add("System.Object");
            return o;
        }
        public bool IsCustom { get { return TypeNames.Count > 0 && (TypeNames[0] == "System.Management.Automation.PSCustomObject" || TypeNames[0].StartsWith("Selected.", StringComparison.Ordinal) || TypeNames[0].StartsWith("Deserialized.", StringComparison.Ordinal) && TypeNames.Contains("Deserialized.System.Management.Automation.PSCustomObject")); } }
        public string TypeName { get { return TypeNames.Count > 0 ? TypeNames[0] : "System.Object"; } }
        public IList<PsProp> Props { get { return props; } }

        public PsObj Add(string name, object value) { return Add(name, value, null); }
        public PsObj Add(string name, object value, string typeName)
        {
            var p = new PsProp { Name = name, Value = value, TypeName = typeName ?? GuessType(value) };
            Put(p);
            return this;
        }
        /// <summary>A .NET-style property (MemberType Property) with a getter.</summary>
        public PsObj Prop(string name, string typeName, Func<object> get) { Put(new PsProp { Name = name, Kind = "Property", TypeName = typeName, Getter = get, ReadOnly = true }); return this; }
        public PsObj Prop(string name, string typeName, Func<object> get, Action<object> set) { Put(new PsProp { Name = name, Kind = "Property", TypeName = typeName, Getter = get, Setter = set }); return this; }
        /// <summary>A stored .NET-style property (MemberType Property).</summary>
        public PsObj Val(string name, string typeName, object value) { Put(new PsProp { Name = name, Kind = "Property", TypeName = typeName, Value = value }); return this; }
        public PsObj Alias(string name, string target) { Put(new PsProp { Name = name, Kind = "AliasProperty", AliasOf = target, TypeName = "" }); return this; }
        public PsObj Method(string name, string definition, PsMethodFn fn) { Methods[name] = new PsMethod { Name = name, Definition = definition, Fn = fn }; return this; }

        public void Put(PsProp p)
        {
            PsProp old;
            if (index.TryGetValue(p.Name, out old)) { int i = props.IndexOf(old); props[i] = p; }
            else props.Add(p);
            index[p.Name] = p;
        }
        public bool Remove(string name) { PsProp p; if (!index.TryGetValue(name, out p)) return false; props.Remove(p); index.Remove(name); return true; }
        public PsProp Find(string name) { PsProp p; return index.TryGetValue(name, out p) ? p : null; }
        public bool Has(string name) { return index.ContainsKey(name); }

        /// <summary>The value of a property (alias and script properties resolved; script properties need the interpreter).</summary>
        public object Get(string name)
        {
            var p = Find(name);
            if (p == null) return null;
            return ValueOf(p);
        }
        public object ValueOf(PsProp p)
        {
            if (p.Kind == "AliasProperty") return Get(p.AliasOf);
            if (p.Script != null) return PsScriptProps.Eval == null ? null : PsScriptProps.Eval(p.Script, this);
            if (p.Getter != null) return p.Getter();
            return p.Value;
        }
        public string S(string name) { return PsConv.ToStr(Get(name)); }
        public void Set(string name, object v)
        {
            var p = Find(name);
            if (p == null) { Add(name, v); return; }
            if (p.Kind == "AliasProperty") { Set(p.AliasOf, v); return; }
            if (p.Setter != null) { p.Setter(v); return; }
            p.Value = v;
        }

        public PsObj Copy()
        {
            var o = new PsObj();
            o.TypeNames.AddRange(TypeNames);
            foreach (var p in props) o.Put(p.Clone());
            foreach (var m in Methods) o.Methods[m.Key] = m.Value;
            o.DefaultDisplay = DefaultDisplay; o.ToStr = ToStr; o.Base = Base; o.Computer = Computer; o.FileNameOnly = FileNameOnly;
            return o;
        }

        public override string ToString()
        {
            if (ToStr != null) return ToStr(this);
            if (IsCustom || TypeName.StartsWith("Deserialized.", StringComparison.Ordinal))
            {
                var sb = new StringBuilder("@{");
                bool first = true;
                foreach (var p in props) { if (!first) sb.Append("; "); first = false; sb.Append(p.Name).Append('=').Append(PsConv.ToStr(ValueOf(p))); }
                return sb.Append('}').ToString();
            }
            return TypeName;
        }

        static string GuessType(object v)
        {
            if (v == null) return "System.Object";
            var t = PsTypes.TypeNameOf(v);
            return t;
        }
    }

    public static class PsScriptProps { public static Func<PsScriptBlock, PsObj, object> Eval; }

    /// <summary>An enum value (ServiceControllerStatus.Running, ...). Compares and sorts by its number.</summary>
    public class PsEnum : IComparable
    {
        public PsEnumType Type; public long Value;
        public string Name
        {
            get
            {
                string n;
                if (Type.ByValue.TryGetValue(Value, out n)) return n;
                if (Type.Flags)
                {
                    var parts = new List<string>();
                    foreach (var kv in Type.Values) if (kv.Value != 0 && (Value & kv.Value) == kv.Value) parts.Add(kv.Key);
                    if (parts.Count > 0) return string.Join(", ", parts.ToArray());
                }
                return Value.ToString(CultureInfo.InvariantCulture);
            }
        }
        public override string ToString() { return Name; }
        public override bool Equals(object o) { var e = o as PsEnum; return e != null && e.Type == Type && e.Value == Value; }
        public override int GetHashCode() { return Value.GetHashCode(); }
        public int CompareTo(object o) { var e = o as PsEnum; return e == null ? 1 : Value.CompareTo(e.Value); }
    }

    public class PsEnumType
    {
        public string FullName = "";
        public bool Flags;
        public readonly List<KeyValuePair<string, long>> Values = new List<KeyValuePair<string, long>>();
        public readonly Dictionary<long, string> ByValue = new Dictionary<long, string>();
        public string Short { get { int i = FullName.LastIndexOf('.'); return i < 0 ? FullName : FullName.Substring(i + 1); } }
        public PsEnum Of(string name)
        {
            foreach (var kv in Values) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return new PsEnum { Type = this, Value = kv.Value };
            return null;
        }
        public PsEnum Of(long v) { return new PsEnum { Type = this, Value = v }; }
        /// <summary>"Running", "4", "ReadOnly, Hidden" -> the value, or null.</summary>
        public PsEnum Parse(string s)
        {
            s = (s ?? "").Trim();
            long n;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return Of(n);
            if (Flags && s.Contains(","))
            {
                long all = 0;
                foreach (var part in s.Split(',')) { var e = Of(part.Trim()); if (e == null) return null; all |= e.Value; }
                return Of(all);
            }
            return Of(s);
        }
    }

    public static class PsEnums
    {
        static readonly Dictionary<string, PsEnumType> all = new Dictionary<string, PsEnumType>(StringComparer.OrdinalIgnoreCase);
        public static PsEnumType Define(string fullName, string values) { return Define(fullName, values, false); }
        public static PsEnumType Define(string fullName, string values, bool flags)
        {
            var t = new PsEnumType { FullName = fullName, Flags = flags };
            long next = 0;
            foreach (var part in values.Split(','))
            {
                var kv = part.Split('=');
                string name = kv[0].Trim();
                long v = kv.Length > 1 ? long.Parse(kv[1].Trim(), CultureInfo.InvariantCulture) : next;
                t.Values.Add(new KeyValuePair<string, long>(name, v));
                if (!t.ByValue.ContainsKey(v)) t.ByValue[v] = name;
                next = v + 1;
            }
            all[fullName] = t;
            return t;
        }
        public static PsEnumType Get(string name)
        {
            Init();
            PsEnumType t;
            if (all.TryGetValue(name, out t)) return t;
            if (all.TryGetValue("System." + name, out t)) return t;
            return all.Values.FirstOrDefault(x => string.Equals(x.Short, name, StringComparison.OrdinalIgnoreCase) && name.IndexOf('.') < 0);
        }
        public static PsEnum V(string type, string name) { var t = Get(type); return t == null ? null : t.Of(name); }
        public static IEnumerable<PsEnumType> All { get { Init(); return all.Values; } }

        static bool done;
        static readonly object gate = new object();
        public static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                Define("System.ServiceProcess.ServiceControllerStatus", "Stopped=1,StartPending=2,StopPending=3,Running=4,ContinuePending=5,PausePending=6,Paused=7");
                Define("System.ServiceProcess.ServiceStartMode", "Boot=0,System=1,Automatic=2,Manual=3,Disabled=4");
                Define("System.ServiceProcess.ServiceType", "KernelDriver=1,FileSystemDriver=2,Adapter=4,RecognizerDriver=8,Win32OwnProcess=16,Win32ShareProcess=32,InteractiveProcess=256", true);
                Define("Microsoft.PowerShell.Commands.ServiceStartupType", "Boot=0,System=1,Automatic=2,Manual=3,Disabled=4,AutomaticDelayedStart=10");
                Define("System.IO.FileAttributes", "ReadOnly=1,Hidden=2,System=4,Directory=16,Archive=32,Device=64,Normal=128,Temporary=256,SparseFile=512,ReparsePoint=1024,Compressed=2048,Offline=4096,NotContentIndexed=8192,Encrypted=16384", true);
                Define("System.Management.Automation.ActionPreference", "SilentlyContinue=0,Stop=1,Continue=2,Inquire=3,Ignore=4,Suspend=5");
                Define("Microsoft.PowerShell.ExecutionPolicy", "Unrestricted=0,RemoteSigned=1,AllSigned=2,Restricted=3,Bypass=4,Undefined=5");
                Define("Microsoft.PowerShell.ExecutionPolicyScope", "Process=0,CurrentUser=1,LocalMachine=2,UserPolicy=3,MachinePolicy=4");
                Define("System.ConsoleColor", "Black,DarkBlue,DarkGreen,DarkCyan,DarkRed,DarkMagenta,DarkYellow,Gray,DarkGray,Blue,Green,Cyan,Red,Magenta,Yellow,White");
                Define("System.Management.Automation.JobState", "NotStarted=0,Running=1,Completed=2,Failed=3,Stopped=4,Blocked=5,Suspended=6,Disconnected=7,Suspending=8,Stopping=9,AtBreakpoint=10");
                Define("Microsoft.Management.Infrastructure.CimCmdlets.StateEnum", "Unknown=0,Disabled=1,Queued=2,Ready=3,Running=4");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.ScheduledTask.StateEnum", "Unknown=0,Disabled=1,Queued=2,Ready=3,Running=4");
                Define("Microsoft.PowerShell.Commands.PrincipalSource", "Unknown=0,Local=1,ActiveDirectory=2,AzureAD=3,MicrosoftAccount=4");
                Define("System.Diagnostics.Eventing.Reader.StandardEventLevel", "LogAlways=0,Critical=1,Error=2,Warning=3,Informational=4,Verbose=5");
                Define("System.Diagnostics.EventLogEntryType", "Error=1,Warning=2,Information=4,SuccessAudit=8,FailureAudit=16");
                Define("System.Net.Sockets.AddressFamily", "Unspecified=0,InterNetwork=2,InterNetworkV6=23");
                Define("AddressFamily", "IPv4=2,IPv6=23");
                Define("PrefixOrigin", "Other=0,Manual=1,WellKnown=2,Dhcp=3,RouterAdvertisement=4");
                Define("SuffixOrigin", "Other=0,Manual=1,WellKnown=2,Dhcp=3,Link=4,Random=5");
                Define("AddressState", "Invalid=0,Tentative=1,Duplicate=2,Deprecated=3,Preferred=4");
                Define("Store", "PersistentStore=0,ActiveStore=1");
                Define("Microsoft.Win32.RegistryValueKind", "Unknown=0,String=1,ExpandString=2,Binary=3,DWord=4,MultiString=7,QWord=11,None=-1");
                Define("System.Management.Automation.CommandTypes", "Alias=1,Function=2,Filter=4,Cmdlet=8,ExternalScript=16,Application=32,Script=64,Workflow=128,Configuration=256,All=511", true);
                Define("System.Management.Automation.ModuleType", "Script=0,Binary=1,Manifest=2,Cim=3,Workflow=4");
                Define("System.Management.Automation.PSMemberTypes", "AliasProperty=1,CodeProperty=2,Property=4,NoteProperty=8,ScriptProperty=16,Properties=31,PropertySet=32,Method=64,CodeMethod=128,ScriptMethod=256,Methods=448,ParameterizedProperty=512,MemberSet=1024,Event=2048,Dynamic=4096,All=8191", true);
                Define("System.Management.Automation.Runspaces.RunspaceAvailability", "None=0,Available=1,AvailableForNestedCommand=2,Busy=3,RemoteDebug=4");
                Define("System.Management.Automation.Runspaces.RunspaceState", "BeforeOpen=0,Opening=1,Opened=2,Closed=3,Closing=4,Broken=5,Disconnecting=6,Disconnected=7,Connecting=8");
                Define("System.Management.Automation.SignatureStatus", "Valid=0,UnknownError=1,NotSigned=2,HashMismatch=3,NotTrusted=4,NotSupportedFileFormat=5,Incompatible=6");
                Define("System.DayOfWeek", "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday");
                Define("System.StringSplitOptions", "None=0,RemoveEmptyEntries=1");
                Define("System.Text.RegularExpressions.RegexOptions", "None=0,IgnoreCase=1,Multiline=2,ExplicitCapture=4,Compiled=8,Singleline=16,IgnorePatternWhitespace=32,RightToLeft=64,ECMAScript=256,CultureInvariant=512", true);
                Define("System.Management.Automation.SwitchParameter", "False=0,True=1");
                Define("NetConnectionProfile.NetworkCategory", "Public=0,Private=1,DomainAuthenticated=2");
                Define("Microsoft.PowerShell.Commands.TestConnectionCommand+DisplayType", "Default=0");
                Define("System.Net.NetworkInformation.IPStatus", "Success=0,TimedOut=11010,DestinationHostUnreachable=11003");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetTCPConnection.State", "Closed=1,Listen=2,SynSent=3,SynReceived=4,Established=5,FinWait1=6,FinWait2=7,CloseWait=8,Closing=9,LastAck=10,TimeWait=11,DeleteTCB=12,Bound=100");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetSecurity.Direction", "Inbound=1,Outbound=2");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetSecurity.Action", "NotConfigured=0,Allow=2,Block=4");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetSecurity.Enabled", "True=1,False=2");
                Define("Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetSecurity.Profile", "Any=0,Domain=1,Private=2,Public=4,NotApplicable=1073741824", true);
                Define("Microsoft.Windows.ServerManager.Commands.InstallState", "Available=0,Installed=1,InstallPending=2,Removed=3,UninstallPending=4");
                Define("Microsoft.Windows.ServerManager.Commands.FeatureOperationExitCode", "Success=0,NoChangeNeeded=1003,Failed=1");
                Define("Microsoft.Windows.ServerManager.Commands.RestartState", "No=0,Yes=1,Maybe=2");
            }
        }
    }

    /// <summary>A PowerShell type: [int], [string], [datetime], [hashtable]... with its conversion.</summary>
    public class PsType
    {
        public string FullName = "System.Object", BaseName = "System.Object";
        public Type Clr;                    // a real .NET type the simulator may use (only whitelisted ones)
        public PsEnumType Enum;
        public PsType Element;              // [int[]]: Element = [int]
        public bool IsArray, Ordered;
        public List<PsType> Generic = new List<PsType>();
        public string Short { get { int i = FullName.LastIndexOf('.'); string s = i < 0 ? FullName : FullName.Substring(i + 1); int g = s.IndexOf('`'); return g > 0 ? s : s; } }
        public string Display { get { return IsArray ? Element.Display + "[]" : FullName; } }
        public override string ToString() { return Display; }
    }

    public static class PsTypes
    {
        static readonly Dictionary<string, string> accel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "int", "System.Int32" }, { "int32", "System.Int32" }, { "long", "System.Int64" }, { "int64", "System.Int64" }, { "string", "System.String" },
            { "char", "System.Char" }, { "bool", "System.Boolean" }, { "boolean", "System.Boolean" }, { "byte", "System.Byte" }, { "double", "System.Double" },
            { "float", "System.Single" }, { "single", "System.Single" }, { "decimal", "System.Decimal" }, { "datetime", "System.DateTime" },
            { "timespan", "System.TimeSpan" }, { "array", "System.Array" }, { "hashtable", "System.Collections.Hashtable" },
            { "pscustomobject", "System.Management.Automation.PSObject" }, { "psobject", "System.Management.Automation.PSObject" }, { "object", "System.Object" },
            { "regex", "System.Text.RegularExpressions.Regex" }, { "scriptblock", "System.Management.Automation.ScriptBlock" }, { "switch", "System.Management.Automation.SwitchParameter" },
            { "void", "System.Void" }, { "version", "System.Version" }, { "guid", "System.Guid" }, { "ipaddress", "System.Net.IPAddress" },
            { "securestring", "System.Security.SecureString" }, { "pscredential", "System.Management.Automation.PSCredential" }, { "math", "System.Math" },
            { "convert", "System.Convert" }, { "uint32", "System.UInt32" }, { "uint64", "System.UInt64" }, { "int16", "System.Int16" }, { "uint16", "System.UInt16" }, { "sbyte", "System.SByte" },
            { "xml", "System.Xml.XmlDocument" }, { "wmi", "System.Management.ManagementObject" }, { "ciminstance", "Microsoft.Management.Infrastructure.CimInstance" },
            { "ordered", "System.Collections.Specialized.OrderedDictionary" }, { "type", "System.Type" }, { "mailaddress", "System.Net.Mail.MailAddress" },
            { "cmdletbinding", "System.Management.Automation.CmdletBindingAttribute" }, { "parameter", "System.Management.Automation.ParameterAttribute" },
            { "errorrecord", "System.Management.Automation.ErrorRecord" }, { "uri", "System.Uri" }, { "bigint", "System.Numerics.BigInteger" },
        };

        static readonly Dictionary<string, Type> clr = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> bases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool done;
        static readonly object gate = new object();

        static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                foreach (var t in new[] { typeof(int), typeof(long), typeof(string), typeof(char), typeof(bool), typeof(byte), typeof(double), typeof(float), typeof(decimal), typeof(DateTime), typeof(TimeSpan),
                    typeof(short), typeof(ushort), typeof(uint), typeof(ulong), typeof(sbyte), typeof(Version), typeof(Guid), typeof(Math), typeof(Convert), typeof(Regex), typeof(Match), typeof(Group), typeof(Capture), typeof(MatchCollection),
                    typeof(StringBuilder), typeof(ArrayList), typeof(Hashtable), typeof(OrderedDictionary), typeof(Array), typeof(object), typeof(Uri), typeof(DayOfWeek), typeof(StringComparison), typeof(Encoding), typeof(BitConverter), typeof(Char), typeof(Random), typeof(Stack), typeof(Queue) })
                    clr[t.FullName] = t;
                clr["System.Collections.Generic.List`1"] = typeof(List<>);
                clr["System.Collections.Generic.Dictionary`2"] = typeof(Dictionary<,>);
                clr["System.Collections.Generic.HashSet`1"] = typeof(HashSet<>);
                clr["System.Collections.Generic.Queue`1"] = typeof(Queue<>);
                clr["System.Collections.Generic.Stack`1"] = typeof(Stack<>);
                clr["System.Collections.Generic.KeyValuePair`2"] = typeof(KeyValuePair<,>);
                foreach (var kv in new[] {
                    "System.Int32|System.ValueType", "System.Int64|System.ValueType", "System.String|System.Object", "System.Char|System.ValueType", "System.Boolean|System.ValueType",
                    "System.Double|System.ValueType", "System.Decimal|System.ValueType", "System.DateTime|System.ValueType", "System.TimeSpan|System.ValueType", "System.Byte|System.ValueType",
                    "System.ValueType|System.Object", "System.Array|System.Object", "System.Object[]|System.Array", "System.Collections.Hashtable|System.Object", "System.Collections.ArrayList|System.Object",
                    "System.Collections.Specialized.OrderedDictionary|System.Object", "System.Management.Automation.PSCustomObject|System.Object", "System.Management.Automation.PSObject|System.Object",
                    "System.Management.Automation.ScriptBlock|System.Object", "System.ServiceProcess.ServiceController|System.ComponentModel.Component", "System.ComponentModel.Component|System.MarshalByRefObject",
                    "System.MarshalByRefObject|System.Object", "System.Diagnostics.Process|System.ComponentModel.Component", "System.IO.FileInfo|System.IO.FileSystemInfo", "System.IO.DirectoryInfo|System.IO.FileSystemInfo",
                    "System.IO.FileSystemInfo|System.MarshalByRefObject", "Microsoft.Win32.RegistryKey|System.MarshalByRefObject", "System.Management.Automation.ErrorRecord|System.Object",
                    "System.Exception|System.Object", "System.SystemException|System.Exception", "System.Management.Automation.RuntimeException|System.SystemException", "System.Security.SecureString|System.Object",
                    "System.Management.Automation.PSCredential|System.Object", "Microsoft.Management.Infrastructure.CimInstance|System.Object", "System.Diagnostics.Eventing.Reader.EventLogRecord|System.Diagnostics.Eventing.Reader.EventRecord",
                    "System.Diagnostics.Eventing.Reader.EventRecord|System.Object", "System.Version|System.Object", "System.Guid|System.ValueType", "System.Text.StringBuilder|System.Object", "System.Single|System.ValueType",
                    "System.Text.RegularExpressions.Regex|System.Object", "System.Enum|System.ValueType", "System.Management.Automation.SwitchParameter|System.ValueType" })
                { var p = kv.Split('|'); bases[p[0]] = p[1]; known.Add(p[0]); }
                foreach (var v in accel.Values) known.Add(v);
                foreach (var n in new[] { "System.IO.File", "System.IO.Directory", "System.Environment", "System.Console", "System.Net.Dns", "System.Management.Automation.PSCustomObject", "System.Collections.Generic.List`1",
                    "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.HashSet`1", "System.Exception", "System.ArgumentException", "System.InvalidOperationException", "System.IO.FileNotFoundException",
                    "System.IO.DirectoryNotFoundException", "System.IO.IOException", "System.UnauthorizedAccessException", "System.NotImplementedException", "System.NotSupportedException", "System.FormatException",
                    "System.ArgumentNullException", "System.ArgumentOutOfRangeException", "System.DivideByZeroException", "System.Management.Automation.RuntimeException", "System.Management.Automation.ItemNotFoundException",
                    "System.Management.Automation.CommandNotFoundException", "System.Management.Automation.ParameterBindingException", "System.Management.Automation.ActionPreferenceStopException",
                    "System.Management.Automation.MethodInvocationException", "System.Management.Automation.PSArgumentException", "System.Management.Automation.PSInvalidOperationException",
                    "System.Management.Automation.ValidationMetadataException", "System.Management.Automation.ParameterBindingValidationException", "System.Management.Automation.PSInvalidCastException",
                    "System.Management.Automation.SessionStateException", "System.Management.Automation.DriveNotFoundException", "System.InvalidCastException", "System.TimeoutException", "System.Collections.Queue", "System.Collections.Stack",
                    "System.Management.Automation.WildcardPattern", "System.IO.Path", "System.Management.Automation.VerboseRecord", "System.Management.Automation.WarningRecord", "System.Management.Automation.InformationRecord", "System.Management.Automation.DebugRecord", "System.Text.Encoding", "System.BitConverter", "System.Random", "System.DayOfWeek", "System.Uri", "System.Diagnostics.Stopwatch" })
                    known.Add(n);
            }
        }

        /// <summary>A type the simulator builds itself (New-Object through PsStatics.NewHook), e.g. FileSystemAccessRule.</summary>
        public static void AddKnown(string fullName, string baseName) { Init(); lock (cache) { known.Add(fullName); bases[fullName] = baseName; } }

        /// <summary>"int" -> "System.Int32"; "Collections.ArrayList" -> "System.Collections.ArrayList".</summary>
        public static string FullName(string name)
        {
            Init();
            string f;
            if (accel.TryGetValue(name, out f)) return f;
            if (known.Contains(name)) return CanonicalCase(name);
            if (known.Contains("System." + name)) return CanonicalCase("System." + name);
            var e = PsEnums.Get(name);
            if (e != null) return e.FullName;
            return null;
        }
        static string CanonicalCase(string n) { foreach (var k in known) if (string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) return k; return n; }

        static readonly Dictionary<string, PsType> cache = new Dictionary<string, PsType>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parses a type name as written in [ ]: "int", "string[]", "System.Collections.Generic.List[string]". Null if unknown.</summary>
        public static PsType Resolve(string text)
        {
            Init();
            text = text.Trim();
            lock (cache) { PsType c; if (cache.TryGetValue(text, out c)) return c; }
            var t = ResolveRaw(text);
            if (t != null) lock (cache) cache[text] = t;
            return t;
        }

        static PsType ResolveRaw(string text)
        {
            if (text.EndsWith("[]", StringComparison.Ordinal))
            {
                var el = Resolve(text.Substring(0, text.Length - 2));
                if (el == null) return null;
                return new PsType { FullName = el.FullName + "[]", BaseName = "System.Array", IsArray = true, Element = el, Clr = el.Clr != null && el.Clr.IsValueType || el.Clr == typeof(string) ? el.Clr.MakeArrayType() : typeof(object[]) };
            }
            int lb = text.IndexOf('[');
            if (lb > 0 && text.EndsWith("]", StringComparison.Ordinal))
            {
                // generic: List[string], Dictionary[string,int]
                string gname = text.Substring(0, lb).Trim();
                var args = SplitGeneric(text.Substring(lb + 1, text.Length - lb - 2));
                var gargs = args.Select(a => Resolve(a.Trim('[', ']', ' '))).ToList();
                if (gargs.Any(g => g == null)) return null;
                string full = FullName(gname + "`" + gargs.Count) ?? (gname.Contains(".") ? null : FullName("System.Collections.Generic." + gname + "`" + gargs.Count));
                if (full == null) return null;
                Type def;
                if (!clr.TryGetValue(full, out def)) return null;
                var targs = gargs.Select(g => g.Clr != null && (g.Clr.IsPrimitive || g.Clr == typeof(string) || g.Clr == typeof(DateTime) || g.Clr == typeof(decimal) || g.Clr == typeof(object)) ? g.Clr : typeof(object)).ToArray();
                var t = new PsType { FullName = full.Substring(0, full.IndexOf('`')) + "[" + string.Join(",", gargs.Select(g => g.FullName).ToArray()) + "]", Clr = def.MakeGenericType(targs) };
                t.Generic.AddRange(gargs);
                return t;
            }
            string fn = FullName(text);
            if (fn == null) return null;
            var r = new PsType { FullName = fn };
            string b; if (bases.TryGetValue(fn, out b)) r.BaseName = b;
            Type ct; if (clr.TryGetValue(fn, out ct)) r.Clr = ct;
            var en = PsEnums.Get(fn);
            if (en != null) { r.Enum = en; r.BaseName = "System.Enum"; }
            if (fn == "System.Collections.Specialized.OrderedDictionary" && text.Equals("ordered", StringComparison.OrdinalIgnoreCase)) r.Ordered = true;
            return r;
        }

        static List<string> SplitGeneric(string s)
        {
            var l = new List<string>(); int depth = 0, st = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '[') depth++; else if (s[i] == ']') depth--;
                else if (s[i] == ',' && depth == 0) { l.Add(s.Substring(st, i - st)); st = i + 1; }
            }
            l.Add(s.Substring(st));
            return l;
        }

        public static string BaseOf(string fullName) { Init(); string b; if (bases.TryGetValue(fullName, out b)) return b; if (fullName.EndsWith("[]", StringComparison.Ordinal)) return "System.Array"; if (fullName.EndsWith("Exception", StringComparison.Ordinal)) return "System.SystemException"; return "System.Object"; }

        /// <summary>The full .NET type name PowerShell would report for a value.</summary>
        public static string TypeNameOf(object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "System.Object";
            var po = v as PsObj;
            if (po != null) return po.IsCustom && !po.TypeName.StartsWith("Deserialized.", StringComparison.Ordinal) ? "System.Management.Automation.PSCustomObject" : po.TypeName;
            if (v is PsEnum) return ((PsEnum)v).Type.FullName;
            if (v is PsScriptBlock) return "System.Management.Automation.ScriptBlock";
            if (v is PsSecureString) return "System.Security.SecureString";
            if (v is PsTypeInfo) return "System.RuntimeType";
            if (v is PsErrorRecord) return "System.Management.Automation.ErrorRecord";
            if (v is PsException) return ((PsException)v).TypeFullName;
            if (v is object[]) return "System.Object[]";
            var t = v.GetType();
            if (t.IsGenericType)
            {
                var gd = t.GetGenericTypeDefinition();
                string n = gd.FullName.Substring(0, gd.FullName.IndexOf('`'));
                return n + "[[" + string.Join("],[", t.GetGenericArguments().Select(a => a.FullName).ToArray()) + "]]";
            }
            return t.FullName;
        }

        /// <summary>The short name GetType().Name shows: Int32, String, Object[], PSCustomObject, ServiceController...</summary>
        public static string ShortNameOf(object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "Object";
            var t = v.GetType();
            if (t.IsGenericType && !(v is PsObj)) { var n = t.Name; return n; }
            var full = TypeNameOf(v);
            int i = full.LastIndexOf('.');
            return i < 0 ? full : full.Substring(i + 1);
        }

        /// <summary>value -is [type]</summary>
        public static bool IsInstance(object v, PsType t)
        {
            v = PsConv.Base(v);
            if (v == null) return false;
            string f = t.FullName;
            if (f == "System.Object") return true;
            if (f == "System.Management.Automation.PSObject") return v is PsObj;
            if (t.IsArray) return v is Array && (t.Element.FullName == "System.Object" ? v is object[] : t.Clr != null && t.Clr.IsInstanceOfType(v));
            if (f == "System.Array") return v is Array;
            if (t.Enum != null) { var e = v as PsEnum; return e != null && e.Type == t.Enum; }
            if (f == "System.Management.Automation.ScriptBlock") return v is PsScriptBlock;
            if (f == "System.Security.SecureString") return v is PsSecureString;
            if (f == "System.Management.Automation.ErrorRecord") return v is PsErrorRecord;
            if (f == "System.ValueType") return v.GetType().IsValueType || v is PsEnum;
            if (f == "System.Enum") return v is PsEnum;
            if (f == "System.Collections.Hashtable") return v is Hashtable;
            if (f == "System.Collections.IDictionary") return v is IDictionary;
            var po = v as PsObj;
            if (po != null) return po.TypeNames.Any(n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase) || string.Equals(n, "Deserialized." + f, StringComparison.OrdinalIgnoreCase)) || ChainContains(po.TypeName, f);
            var pe = v as PsException;
            if (pe != null) return pe.Chain().Any(n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase));
            if (t.Clr != null) return t.Clr.IsInstanceOfType(v);
            return string.Equals(TypeNameOf(v), f, StringComparison.OrdinalIgnoreCase);
        }
        static bool ChainContains(string name, string f)
        {
            for (int i = 0; i < 10 && name != null && name != "System.Object"; i++) { if (string.Equals(name, f, StringComparison.OrdinalIgnoreCase)) return true; name = BaseOf(name); }
            return false;
        }

        public static bool IsAllowedClr(Type t)
        {
            Init();
            if (t == null) return false;
            if (t.IsArray) return IsAllowedClr(t.GetElementType());
            if (t.IsGenericType) t = t.GetGenericTypeDefinition();
            for (var b = t.BaseType; b != null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType) if (clr.Values.Contains(b)) return true;
            return clr.Values.Contains(t) || t.IsPrimitive || t == typeof(string) || t == typeof(DateTime) || t == typeof(TimeSpan) || t == typeof(decimal) || t == typeof(DayOfWeek)
                || t == typeof(System.Collections.IEnumerator) || t == typeof(CharEnumerator) || t == typeof(GroupCollection) || t == typeof(CaptureCollection) || t == typeof(DateTimeKind);
        }
    }

    /// <summary>What GetType() returns: a description of a type (Name, FullName, BaseType).</summary>
    public class PsTypeInfo
    {
        public string FullName = "System.Object";
        public PsType Type;
        public string Name { get { var f = FullName; int g = f.IndexOf('['); string head = g > 0 && !f.EndsWith("[]", StringComparison.Ordinal) ? f.Substring(0, g) : f; int i = head.LastIndexOf('.'); string s = i < 0 ? head : head.Substring(i + 1); if (g > 0 && !f.EndsWith("[]", StringComparison.Ordinal)) s += "`" + Math.Max(1, f.Substring(g).Count(c => c == ',') + 1); return s; } }
        public string Namespace { get { var f = FullName; int g = f.IndexOf('['); if (g > 0) f = f.Substring(0, g); int i = f.LastIndexOf('.'); return i < 0 ? "" : f.Substring(0, i); } }
        public string BaseType { get { return PsTypes.BaseOf(FullName.Contains("[[") ? "System.Object" : FullName); } }
        public override string ToString() { return FullName; }
        public static PsTypeInfo Of(object v) { return new PsTypeInfo { FullName = PsTypes.TypeNameOf(v) }; }
    }

    public class PsSecureString
    {
        public string Plain = "";   // the simulator keeps it to compare passwords; it never prints it
        public int Length { get { return Plain.Length; } }
        public override string ToString() { return "System.Security.SecureString"; }
    }

    /// <summary>An exception object ($_.Exception): a .NET-style type name chain and a message.</summary>
    public class PsException
    {
        public string TypeFullName = "System.Management.Automation.RuntimeException", Message = "";
        public PsException Inner;
        public object Wrapped;          // a real .NET exception thrown by a whitelisted method
        static readonly Dictionary<string, string> parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "System.Management.Automation.ItemNotFoundException", "System.Management.Automation.SessionStateException" },
            { "System.Management.Automation.DriveNotFoundException", "System.Management.Automation.SessionStateException" },
            { "System.Management.Automation.SessionStateException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.CommandNotFoundException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.ParameterBindingException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.ParameterBindingValidationException", "System.Management.Automation.ParameterBindingException" },
            { "System.Management.Automation.ParameterBindingArgumentTransformationException", "System.Management.Automation.ParameterBindingException" },
            { "System.Management.Automation.ValidationMetadataException", "System.Management.Automation.MetadataException" },
            { "System.Management.Automation.MetadataException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.ActionPreferenceStopException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.MethodInvocationException", "System.Management.Automation.MethodException" },
            { "System.Management.Automation.MethodException", "System.Management.Automation.ExtendedTypeSystemException" },
            { "System.Management.Automation.ExtendedTypeSystemException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.PSInvalidCastException", "System.InvalidCastException" },
            { "System.Management.Automation.PSArgumentException", "System.ArgumentException" },
            { "System.Management.Automation.PSInvalidOperationException", "System.InvalidOperationException" },
            { "System.Management.Automation.PSNotSupportedException", "System.NotSupportedException" },
            { "System.Management.Automation.SetValueInvocationException", "System.Management.Automation.SetValueException" },
            { "System.Management.Automation.SetValueException", "System.Management.Automation.ExtendedTypeSystemException" },
            { "System.Management.Automation.PropertyNotFoundException", "System.Management.Automation.ExtendedTypeSystemException" },
            { "System.Management.Automation.RuntimeException", "System.SystemException" },
            { "System.Management.Automation.PSSecurityException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.Remoting.PSRemotingTransportException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.ParseException", "System.Management.Automation.RuntimeException" },
            { "System.Management.Automation.IncompleteParseException", "System.Management.Automation.ParseException" },
            { "System.IO.FileNotFoundException", "System.IO.IOException" },
            { "System.IO.DirectoryNotFoundException", "System.IO.IOException" },
            { "System.IO.IOException", "System.SystemException" },
            { "System.ArgumentNullException", "System.ArgumentException" },
            { "System.ArgumentOutOfRangeException", "System.ArgumentException" },
            { "System.ArgumentException", "System.SystemException" },
            { "System.InvalidOperationException", "System.SystemException" },
            { "System.ObjectDisposedException", "System.InvalidOperationException" },
            { "System.UnauthorizedAccessException", "System.SystemException" },
            { "System.NotSupportedException", "System.SystemException" },
            { "System.NotImplementedException", "System.SystemException" },
            { "System.FormatException", "System.SystemException" },
            { "System.InvalidCastException", "System.SystemException" },
            { "System.DivideByZeroException", "System.ArithmeticException" },
            { "System.OverflowException", "System.ArithmeticException" },
            { "System.ArithmeticException", "System.SystemException" },
            { "System.IndexOutOfRangeException", "System.SystemException" },
            { "System.TimeoutException", "System.SystemException" },
            { "System.Management.Automation.RemoteException", "System.Management.Automation.RuntimeException" },
            { "Microsoft.PowerShell.Commands.ServiceCommandException", "System.SystemException" },
            { "System.ComponentModel.Win32Exception", "System.Runtime.InteropServices.ExternalException" },
            { "System.Runtime.InteropServices.ExternalException", "System.SystemException" },
            { "Microsoft.Management.Infrastructure.CimException", "System.SystemException" },
            { "System.Security.SecurityException", "System.SystemException" },
            { "System.SystemException", "System.Exception" },
            { "System.Exception", "System.Object" },
        };
        public IEnumerable<string> Chain()
        {
            string n = TypeFullName;
            for (int i = 0; i < 12 && n != null; i++)
            {
                yield return n;
                if (n == "System.Object") yield break;
                string p;
                n = parents.TryGetValue(n, out p) ? p : (n == "System.Exception" ? "System.Object" : "System.Exception");
            }
        }
        public static bool IsExceptionType(string fullName) { return fullName != null && (parents.ContainsKey(fullName) || fullName.EndsWith("Exception", StringComparison.Ordinal)); }
        public string ShortName { get { int i = TypeFullName.LastIndexOf('.'); return i < 0 ? TypeFullName : TypeFullName.Substring(i + 1); } }
        public override string ToString() { return TypeFullName + ": " + Message; }
    }

    /// <summary>An error ($Error[0], $_ in catch): the exception, category, id, target and where it happened.</summary>
    public class PsErrorRecord
    {
        public PsException Exception = new PsException();
        public string Category = "NotSpecified", ErrorId = "RuntimeException", Activity = "", Reason = "", TargetName = "", TargetType = "";
        public object TargetObject;
        public string CommandName = "";      // the command that wrote it ("Get-Item"); empty for script errors
        public string Line = "", ScriptName = "";
        public int LineNumber = 1, Column = 1, Length = 1;
        public string StackTrace = "at <ScriptBlock>, <No file>: line 1";
        public string FqId { get { return ErrorId + (CommandName.Length > 0 && FullCommandName.Length > 0 ? "," + FullCommandName : ""); } }
        public string FullCommandName = "";   // Microsoft.PowerShell.Commands.GetItemCommand
        public string ErrorDetails;           // Write-Error -Message text shown instead of the exception's
        public string ActivityOverride;       // "" for errors that show [] (command not found, parser errors)
        public bool NoPosition;               // Write-Error at the prompt: no "At line:" part
        public string Computer;               // an error that came back from another machine (PSComputerName)
        public override string ToString() { return ErrorDetails ?? Exception.Message; }
        public string CategoryText
        {
            get
            {
                string tgt = TargetName, tt = TargetType;
                string act = ActivityOverride ?? (Activity.Length > 0 ? Activity : CommandName);
                return Category + ": (" + tgt + ":" + tt + ") [" + act + "], " + (Reason.Length > 0 ? Reason : Exception.ShortName);
            }
        }
    }

    /// <summary>The keys of a PowerShell hashtable: case doesn't matter. The hash codes are fixed (FwHash), so a
    /// hashtable lists its keys in the same order every time: .NET 10's culture-aware hash codes differ from run to run.</summary>
    public sealed class PsKeys : IEqualityComparer
    {
        public static readonly PsKeys Instance = new PsKeys();
        public new bool Equals(object a, object b)
        {
            var sa = a as string; var sb = b as string;
            if (sa != null && sb != null) return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
            return a == null ? b == null : a.Equals(b);
        }
        public int GetHashCode(object o)
        {
            var s = o as string;
            return s != null ? FwHash.Of(s.ToUpperInvariant()) : o.GetHashCode();
        }
    }

    /// <summary>A double formatted as .NET Framework did (PsConv.FwNum).</summary>
    public sealed class FwDouble : IFormattable
    {
        public readonly double D;
        public FwDouble(double d) { D = d; }
        public override string ToString() { return ToString(null, PsConv.Us); }
        public string ToString(string format, IFormatProvider provider)
        {
            string f = (format ?? "").Trim();
            if (f.Length == 0 || f == "G" || f == "g") return D.ToString("G15", provider);
            if (f == "R" || f == "r" || double.IsNaN(D) || double.IsInfinity(D) || Math.Abs(D) >= 7.9e27) return D.ToString(format, provider);
            return ((decimal)D).ToString(format, provider);
        }
    }

    public static class PsConv
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly CultureInfo Us = MakeUs();
        /// <summary>en-US as Windows PowerShell 5.1 shows it (.NET Framework, Windows' own culture data). .NET 10 reads
        /// cultures from ICU, which puts a narrow no-break space before AM / PM and says the infinity sign: put back what
        /// 5.1 prints, so the pretend machines print the same on every platform.</summary>
        static CultureInfo MakeUs()
        {
            var c = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
            var d = c.DateTimeFormat;
            Func<string, string> fix = s => s.Replace('\u202F', ' ').Replace('\u00A0', ' ');
            d.ShortTimePattern = fix(d.ShortTimePattern); d.LongTimePattern = fix(d.LongTimePattern);
            d.ShortDatePattern = fix(d.ShortDatePattern); d.LongDatePattern = fix(d.LongDatePattern);
            d.FullDateTimePattern = fix(d.FullDateTimePattern);
            d.MonthDayPattern = fix(d.MonthDayPattern); d.YearMonthPattern = fix(d.YearMonthPattern);
            d.AMDesignator = "AM"; d.PMDesignator = "PM";
            var n = c.NumberFormat;
            n.PositiveInfinitySymbol = "Infinity"; n.NegativeInfinitySymbol = "-Infinity"; n.NaNSymbol = "NaN";
            return CultureInfo.ReadOnly(c);
        }
        /// <summary>A double for a format string ({0:N1}, .ToString('F2')): .NET Framework rounded to 15 digits first and
        /// then half away from zero (2.25 -> 2.3), .NET 10 rounds the exact value (2.2). Formatting through decimal gives
        /// 5.1's digits.</summary>
        public static object FwNum(object x)
        {
            if (x is double) return new FwDouble((double)x);
            return x;
        }
        public static object Base(object o) { return o is AutoNull ? null : o; }
        public static bool IsNull(object o) { return o == null || o is AutoNull; }

        public static bool IsCollection(object o)
        {
            if (o == null || o is string || o is AutoNull || o is PsObj || o is IDictionary) return false;
            if (o is Array || o is IList) return true;
            if (o is ICollection) return true;
            var t = o.GetType();
            if (t.IsGenericType && o is IEnumerable) return true;
            return false;
        }
        /// <summary>The elements PowerShell enumerates when a value goes down the pipeline (one level).</summary>
        public static IEnumerable<object> Items(object o)
        {
            if (o is AutoNull) yield break;
            if (IsCollection(o)) { foreach (var x in (IEnumerable)o) yield return x; yield break; }
            yield return o;
        }
        public static List<object> List(object o) { return Items(o).ToList(); }
        /// <summary>Collected output: nothing -> AutoNull, one -> it, several -> object[].</summary>
        public static object Collapse(List<object> l)
        {
            if (l.Count == 0) return AutoNull.Value;
            if (l.Count == 1) return l[0];
            return l.ToArray();
        }

        public static bool ToBool(object o)
        {
            o = Base(o);
            if (o == null) return false;
            if (o is bool) return (bool)o;
            if (o is string) return ((string)o).Length > 0;
            if (o is PsEnum) return ((PsEnum)o).Value != 0;
            if (IsNumber(o)) return Convert.ToDouble(o, Inv) != 0;
            if (o is char) return (char)o != '\0';
            if (IsCollection(o))
            {
                var l = ((IEnumerable)o).Cast<object>().Take(2).ToList();
                if (l.Count == 0) return false;
                if (l.Count == 1) return l[0] is IEnumerable && !(l[0] is string) ? true : ToBool(l[0]);
                return true;
            }
            if (o is PsSwitch) return ((PsSwitch)o).IsPresent;
            return true;
        }

        public static bool IsNumber(object o) { return o is int || o is long || o is double || o is decimal || o is float || o is byte || o is short || o is uint || o is ulong || o is ushort || o is sbyte; }
        public static bool IsInteger(object o) { return o is int || o is long || o is byte || o is short || o is uint || o is ulong || o is ushort || o is sbyte; }

        public static string Ofs = " ";
        public static string ToStr(object o)
        {
            o = Base(o);
            if (o == null) return "";
            if (o is string) return (string)o;
            if (o is bool) return (bool)o ? "True" : "False";
            if (o is double) return DoubleStr((double)o);
            if (o is float) return DoubleStr((float)o);
            if (o is decimal) return ((decimal)o).ToString(Inv);
            if (IsNumber(o)) return Convert.ToString(o, Inv);
            if (o is char) return o.ToString();
            if (o is DateTime) return ((DateTime)o).ToString("MM/dd/yyyy HH:mm:ss", Inv);
            if (o is TimeSpan) return ((TimeSpan)o).ToString();
            if (o is PsSwitch) return ((PsSwitch)o).IsPresent ? "True" : "False";
            if (o is IDictionary) return o is OrderedDictionary ? "System.Collections.Specialized.OrderedDictionary" : "System.Collections.Hashtable";
            if (IsCollection(o))
            {
                var parts = new List<string>();
                foreach (var x in (IEnumerable)o) parts.Add(ToStr(x));
                return string.Join(Ofs, parts.ToArray());
            }
            if (o is PsScriptBlock) return ((PsScriptBlock)o).Text;
            if (o is Type) return ((Type)o).FullName;
            return o.ToString();
        }
        public static string DoubleStr(double d)
        {
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (double.IsNaN(d)) return "NaN";
            return d.ToString("G15", Inv);   // .NET Framework's default: 15 digits (0.1 + 0.2 = 0.3)
        }

        /// <summary>Parses a number the way PowerShell does in a conversion ("0x1F", "12", "1.5", "1e3", " 7 ").</summary>
        public static bool TryNumber(string s, out object v)
        {
            v = null;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) { v = 0; return true; }
            int i; long l; double d;
            if (Regex.IsMatch(t, "^[+-]?0[xX][0-9a-fA-F]+$"))
            {
                bool neg = t.StartsWith("-", StringComparison.Ordinal); string h = t.TrimStart('+', '-').Substring(2);
                if (long.TryParse(h, NumberStyles.HexNumber, Inv, out l)) { if (neg) l = -l; v = l >= int.MinValue && l <= int.MaxValue ? (object)(int)l : l; return true; }
                return false;
            }
            var m = Regex.Match(t, "^([+-]?[0-9.]+(?:[eE][+-]?[0-9]+)?)(kb|mb|gb|tb|pb)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            string num = m.Groups[1].Value; long mult = 1;
            switch (m.Groups[2].Value.ToLowerInvariant()) { case "kb": mult = 1024; break; case "mb": mult = 1048576; break; case "gb": mult = 1073741824; break; case "tb": mult = 1099511627776; break; case "pb": mult = 1125899906842624; break; }
            if (int.TryParse(num, NumberStyles.AllowLeadingSign, Inv, out i)) { long r = i * mult; v = r >= int.MinValue && r <= int.MaxValue ? (object)(int)r : r; return true; }
            if (long.TryParse(num, NumberStyles.AllowLeadingSign, Inv, out l)) { v = l * mult; return true; }
            if (double.TryParse(num, NumberStyles.Float, Inv, out d)) { v = d * mult; return true; }
            return false;
        }

        /// <summary>A value as a number for arithmetic: numbers stay, strings parse, bools are 0/1, null is 0.</summary>
        public static object ToNumber(object o)
        {
            o = Base(o);
            if (o == null) return 0;
            if (IsNumber(o)) return o;
            if (o is bool) return (bool)o ? 1 : 0;
            if (o is char) return (int)(char)o;
            if (o is PsEnum) return (int)((PsEnum)o).Value;
            if (o is PsSwitch) return ((PsSwitch)o).IsPresent ? 1 : 0;
            if (o is string)
            {
                object v;
                if (TryNumber((string)o, out v)) return v;
                throw new PsConvertError(o, "System.Int32", "Input string was not in a correct format.");
            }
            if (IsCollection(o))
            {
                var l = List(o);
                if (l.Count == 1) return ToNumber(l[0]);
            }
            throw new PsConvertError(o, "System.Int32", null);
        }
        public static double ToDouble(object o) { return Convert.ToDouble(ToNumber(o), Inv); }
        public static long ToLong(object o) { var n = ToNumber(o); if (n is double || n is decimal || n is float) return (long)Math.Round(Convert.ToDouble(n, Inv), MidpointRounding.ToEven); return Convert.ToInt64(n, Inv); }
        public static int ToInt(object o)
        {
            var n = ToNumber(o);
            if (n is double || n is decimal || n is float)
            {
                double d = Convert.ToDouble(n, Inv);
                if (d > int.MaxValue || d < int.MinValue) throw new PsConvertError(o, "System.Int32", "Value was either too large or too small for an Int32.");
                return (int)Math.Round(d, MidpointRounding.ToEven);
            }
            long l = Convert.ToInt64(n, Inv);
            if (l > int.MaxValue || l < int.MinValue) throw new PsConvertError(o, "System.Int32", "Value was either too large or too small for an Int32.");
            return (int)l;
        }

        static readonly string[] dateFormats = { "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm", "M/d/yyyy h:mm tt", "M/d/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ssK", "yyyy/MM/dd", "MMMM d, yyyy", "MMMM d yyyy", "d MMMM yyyy", "h:mm tt", "H:mm", "htt", "h tt", "dddd, MMMM d, yyyy h:mm:ss tt" };
        public static bool TryDate(string s, out DateTime d)
        {
            s = (s ?? "").Trim();
            if (DateTime.TryParseExact(s, dateFormats, Us, DateTimeStyles.AllowWhiteSpaces, out d)) return true;
            return DateTime.TryParse(s, Us, DateTimeStyles.AllowWhiteSpaces, out d);
        }

        /// <summary>Converts a value to a PowerShell type ([int]"5", [string[]]$x, [datetime]"2026-03-01"...). Throws PsConvertError.</summary>
        public static object To(object o, PsType t)
        {
            o = Base(o);
            string f = t.FullName;
            if (t.IsArray)
            {
                var items = o == null ? new List<object>() : List(o);
                var conv = items.Select(x => To(x, t.Element)).ToList();
                if (t.Clr != null && t.Clr != typeof(object[]))
                {
                    var arr = Array.CreateInstance(t.Clr.GetElementType(), conv.Count);
                    for (int i = 0; i < conv.Count; i++) arr.SetValue(conv[i], i);
                    return arr;
                }
                return conv.ToArray();
            }
            switch (f)
            {
                case "System.Object": return o;
                case "System.Void": return AutoNull.Value;
                case "System.String":
                    if (o == null) return "";
                    return ToStr(o);
                case "System.Int32":
                    if (o == null) return 0;
                    try { return ToInt(o); } catch (PsConvertError) { throw new PsConvertError(o, "System.Int32", o is string ? "Input string was not in a correct format." : null); }
                case "System.Int64": if (o == null) return 0L; try { return ToLong(o); } catch (PsConvertError) { throw new PsConvertError(o, "System.Int64", o is string ? "Input string was not in a correct format." : null); }
                case "System.Byte": { int v = o == null ? 0 : ToInt(o); if (v < 0 || v > 255) throw new PsConvertError(o, "System.Byte", "Value was either too large or too small for an unsigned byte."); return (byte)v; }
                case "System.Int16": return (short)ToInt(o);
                case "System.UInt32": return (uint)ToLong(o);
                case "System.UInt64": return (ulong)ToLong(o);
                case "System.UInt16": return (ushort)ToInt(o);
                case "System.Double": case "System.Single":
                    if (o == null) return 0.0;
                    try { double d = ToDouble(o); return f == "System.Single" ? (object)(float)d : d; } catch (PsConvertError) { throw new PsConvertError(o, f, "Input string was not in a correct format."); }
                case "System.Decimal": if (o == null) return 0m; return Convert.ToDecimal(ToNumber(o), Inv);
                case "System.Boolean":
                    if (o is string) return ((string)o).Length > 0;
                    return ToBool(o);
                case "System.Management.Automation.SwitchParameter": return new PsSwitch(ToBool(o));
                case "System.Char":
                    if (o is char) return o;
                    if (o is string && ((string)o).Length == 1) return ((string)o)[0];
                    if (IsNumber(o)) return (char)ToInt(o);
                    throw new PsConvertError(o, "System.Char", "String must be exactly one character long.");
                case "System.DateTime":
                    {
                        if (o is DateTime) return o;
                        if (o is string) { DateTime d; if (TryDate((string)o, out d)) return d; throw new PsConvertError(o, "System.DateTime", "String was not recognized as a valid DateTime."); }
                        if (IsInteger(o)) return new DateTime(ToLong(o));
                        throw new PsConvertError(o, "System.DateTime", null);
                    }
                case "System.TimeSpan":
                    {
                        if (o is TimeSpan) return o;
                        if (o is string) { TimeSpan ts; if (TimeSpan.TryParse((string)o, Inv, out ts)) return ts; throw new PsConvertError(o, "System.TimeSpan", "String was not recognized as a valid TimeSpan."); }
                        if (IsInteger(o)) return new TimeSpan(ToLong(o));
                        throw new PsConvertError(o, "System.TimeSpan", null);
                    }
                case "System.Version":
                    {
                        if (o is Version) return o;
                        Version v; if (Version.TryParse(ToStr(o), out v)) return v;
                        throw new PsConvertError(o, "System.Version", "Version string portion was too short or too long.");
                    }
                case "System.Guid": { Guid g; if (o is Guid) return o; if (Guid.TryParse(ToStr(o), out g)) return g; throw new PsConvertError(o, "System.Guid", "Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)."); }
                case "System.Text.RegularExpressions.Regex": return o is Regex ? o : new Regex(ToStr(o), RegexOptions.IgnoreCase);
                case "System.Array": return o == null ? new object[0] : (o is Array ? o : List(o).ToArray());
                case "System.Collections.Hashtable":
                    if (o is Hashtable) return o;
                    if (o is IDictionary) { var h = NewHashtable(); foreach (DictionaryEntry e in (IDictionary)o) h[e.Key] = e.Value; return h; }
                    throw new PsConvertError(o, f, null);
                case "System.Collections.Specialized.OrderedDictionary":
                    if (o is OrderedDictionary) return o;
                    throw new PsConvertError(o, f, null);
                case "System.Collections.ArrayList":
                    { var a = new ArrayList(); foreach (var x in Items(o)) a.Add(x); return a; }
                case "System.Management.Automation.PSObject":
                    if (o is IDictionary)
                    {
                        var c = PsObj.Custom();
                        foreach (DictionaryEntry e in (IDictionary)o) c.Add(ToStr(e.Key), e.Value);
                        return c;
                    }
                    return o;
                case "System.Management.Automation.ScriptBlock":
                    if (o is PsScriptBlock) return o;
                    throw new PsConvertError(o, f, null);
                case "System.Security.SecureString":
                    if (o is PsSecureString) return o;
                    throw new PsConvertError(o, f, null);
                case "System.Net.IPAddress":
                    {
                        var s = ToStr(o);
                        System.Net.IPAddress ip;
                        if (System.Net.IPAddress.TryParse(s, out ip)) { var r = new PsObj("System.Net.IPAddress", "System.Object"); r.Val("IPAddressToString", "string", ip.ToString()); r.Val("AddressFamily", "System.Net.Sockets.AddressFamily", PsEnums.V("System.Net.Sockets.AddressFamily", ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "InterNetworkV6" : "InterNetwork")); r.ToStr = x => ip.ToString(); return r; }
                        throw new PsConvertError(o, f, "An invalid IP address was specified.");
                    }
                case "System.Type":
                    {
                        if (o is PsTypeInfo) return o;
                        var tt = PsTypes.Resolve(ToStr(o));
                        if (tt == null) throw new PsConvertError(o, f, null);
                        return new PsTypeInfo { FullName = tt.FullName, Type = tt };
                    }
            }
            if (t.Enum != null)
            {
                if (o is PsEnum && ((PsEnum)o).Type == t.Enum) return o;
                if (o is PsEnum) return t.Enum.Of(((PsEnum)o).Value);
                if (IsNumber(o)) return t.Enum.Of(ToLong(o));
                if (t.Enum.Flags && IsCollection(o))
                {
                    // a flags enum takes a list: -CommandType Cmdlet, Function
                    long all = 0;
                    foreach (var x in Items(o)) { var one = To(x, t) as PsEnum; if (one != null) all |= one.Value; }
                    return t.Enum.Of(all);
                }
                var e = t.Enum.Parse(ToStr(o));
                if (e == null) throw new PsEnumError(ToStr(o), t.Enum);
                return e;
            }
            if (t.Clr != null && t.Clr.IsGenericType)
            {
                var inst = Activator.CreateInstance(t.Clr);
                var add = t.Clr.GetMethod("Add");
                var et = t.Clr.GetGenericArguments()[0];
                var pt = Resolve(et);
                foreach (var x in Items(o)) add.Invoke(inst, new[] { pt == null ? x : To(x, pt) });
                return inst;
            }
            if (PsTypes.IsInstance(o, t)) return o;
            throw new PsConvertError(o, f, null);
        }
        public static PsType Resolve(Type clrType)
        {
            if (clrType == typeof(object)) return PsTypes.Resolve("object");
            return PsTypes.Resolve(clrType.FullName);
        }
        public static object To(object o, string typeName) { var t = PsTypes.Resolve(typeName); return t == null ? o : To(o, t); }

        public static Hashtable NewHashtable() { return new Hashtable(PsKeys.Instance); }
        public static OrderedDictionary NewOrdered() { return new OrderedDictionary(StringComparer.InvariantCultureIgnoreCase); }

        /// <summary>Scalar equality with PowerShell's rules: the right side is converted to the left side's type.</summary>
        public static bool Eq(object a, object b, bool caseSensitive)
        {
            a = Base(a); b = Base(b);
            if (a == null) return b == null;
            if (b == null) return false;
            if (a is string)
            {
                string bs = ToStr(b);
                return string.Compare((string)a, bs, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0 || (!caseSensitive && string.Compare((string)a, bs, StringComparison.CurrentCultureIgnoreCase) == 0);
            }
            if (a is char)
            {
                if (b is char) return caseSensitive ? (char)a == (char)b : char.ToUpperInvariant((char)a) == char.ToUpperInvariant((char)b);
                if (b is string) return Eq(a.ToString(), b, caseSensitive);
                if (IsNumber(b)) return (int)(char)a == ToDouble(b);
            }
            if (a is bool) return (bool)a == ToBool(b);
            if (a is PsEnum)
            {
                var ea = (PsEnum)a;
                if (b is PsEnum) return ea.Value == ((PsEnum)b).Value;
                if (IsNumber(b)) return ea.Value == ToLong(b);
                var eb = ea.Type.Parse(ToStr(b));
                return eb != null && eb.Value == ea.Value;
            }
            if (IsNumber(a))
            {
                if (b is string) { object nb; if (!TryNumber((string)b, out nb)) return false; b = nb; }
                else if (b is bool) b = (bool)b ? 1 : 0;
                else if (b is char) b = (int)(char)b;
                else if (b is PsEnum) b = ((PsEnum)b).Value;
                else if (!IsNumber(b)) return false;
                if (a is decimal || b is decimal) return Convert.ToDecimal(a, Inv) == Convert.ToDecimal(b, Inv);
                return Convert.ToDouble(a, Inv) == Convert.ToDouble(b, Inv);
            }
            if (a is DateTime)
            {
                if (b is DateTime) return (DateTime)a == (DateTime)b;
                DateTime d; return b is string && TryDate((string)b, out d) && d == (DateTime)a;
            }
            if (a is TimeSpan) { try { return (TimeSpan)a == (TimeSpan)To(b, PsTypes.Resolve("timespan")); } catch (PsConvertError) { return false; } }
            if (a is Version) { Version v; return Version.TryParse(ToStr(b), out v) && v.Equals(a); }
            if (a is PsSwitch) return ((PsSwitch)a).IsPresent == ToBool(b);
            return a.Equals(b) || ReferenceEquals(a, b);
        }

        /// <summary>Text order as Windows sorts it for en-US ("word sort"): hyphens and apostrophes count only when the
        /// strings are otherwise the same (WEB2 before WEB-TEST). .NET 10's ICU order counts them first; this keeps the
        /// order Windows PowerShell 5.1 shows on every platform.</summary>
        public static int WordSort(string a, string b, bool ignoreCase)
        {
            var ci = Us.CompareInfo;
            var o = ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
            int r = ci.Compare(StripWordMarks(a), StripWordMarks(b), o);
            return r != 0 ? r : ci.Compare(a, b, o);
        }
        static string StripWordMarks(string s) { return s == null || (s.IndexOf('-') < 0 && s.IndexOf('\'') < 0) ? s : s.Replace("-", "").Replace("'", ""); }
        /// <summary>WordSort as a comparer, for OrderBy on names.</summary>
        public static readonly IComparer<string> NameOrder = Comparer<string>.Create((x, y) => WordSort(x, y, false));

        /// <summary>Comparison for -lt / -gt and Sort-Object: right side converted to the left side's type.</summary>
        public static int Compare(object a, object b, bool caseSensitive)
        {
            a = Base(a); b = Base(b);
            if (a == null && b == null) return 0;
            if (a == null) return b is string && ((string)b).Length == 0 ? 0 : -1;
            if (b == null) return a is string && ((string)a).Length == 0 ? 0 : 1;
            if (a is string)
            {
                return WordSort((string)a, ToStr(b), !caseSensitive);
            }
            if (a is PsEnum)
            {
                var ea = (PsEnum)a;
                long bv;
                if (b is PsEnum) bv = ((PsEnum)b).Value;
                else if (IsNumber(b)) bv = ToLong(b);
                else { var eb = ea.Type.Parse(ToStr(b)); if (eb == null) return string.Compare(ea.Name, ToStr(b), StringComparison.OrdinalIgnoreCase); bv = eb.Value; }
                return ea.Value.CompareTo(bv);
            }
            if (IsNumber(a) || a is bool || a is char)
            {
                object na = ToNumber(a), nb;
                if (b is string) { if (!TryNumber((string)b, out nb)) return string.Compare(ToStr(a), (string)b, StringComparison.OrdinalIgnoreCase); }
                else { try { nb = ToNumber(b); } catch (PsConvertError) { return string.Compare(ToStr(a), ToStr(b), StringComparison.OrdinalIgnoreCase); } }
                if (na is decimal || nb is decimal) return Convert.ToDecimal(na, Inv).CompareTo(Convert.ToDecimal(nb, Inv));
                return Convert.ToDouble(na, Inv).CompareTo(Convert.ToDouble(nb, Inv));
            }
            if (a is DateTime)
            {
                DateTime db;
                if (b is DateTime) db = (DateTime)b;
                else if (!TryDate(ToStr(b), out db)) throw new PsConvertError(b, "System.DateTime", "String was not recognized as a valid DateTime.");
                return ((DateTime)a).CompareTo(db);
            }
            if (a is TimeSpan && b is TimeSpan) return ((TimeSpan)a).CompareTo((TimeSpan)b);
            if (a is Version) { Version v; if (Version.TryParse(ToStr(b), out v)) return ((Version)a).CompareTo(v); }
            if (a is IComparable && a.GetType() == b.GetType()) return ((IComparable)a).CompareTo(b);
            return string.Compare(ToStr(a), ToStr(b), caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>PowerShell's wildcard (-like, Get-ChildItem *.log): * ? [abc] [a-z], ` escapes.</summary>
        public static Regex Wildcard(string pattern, bool caseSensitive)
        {
            var sb = new StringBuilder("^");
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '`' && i + 1 < pattern.Length) { sb.Append(Regex.Escape(pattern[++i].ToString())); continue; }
                if (c == '*') { sb.Append(".*"); continue; }
                if (c == '?') { sb.Append('.'); continue; }
                if (c == '[')
                {
                    int j = pattern.IndexOf(']', i + 1);
                    if (j > i + 1) { string set = pattern.Substring(i + 1, j - i - 1); sb.Append('[').Append(set.Replace("\\", "\\\\").Replace("^", "\\^")).Append(']'); i = j; continue; }
                }
                sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            return new Regex(sb.ToString(), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
        public static bool HasWildcard(string s) { return s != null && s.IndexOfAny(new[] { '*', '?', '[' }) >= 0; }
        public static bool Like(string s, string pattern, bool cs) { return Wildcard(pattern, cs).IsMatch(s ?? ""); }
    }

    public class PsSwitch
    {
        public bool IsPresent;
        public PsSwitch(bool b) { IsPresent = b; }
        public override string ToString() { return IsPresent ? "True" : "False"; }
        public override bool Equals(object o) { var s = o as PsSwitch; return s != null && s.IsPresent == IsPresent; }
        public override int GetHashCode() { return IsPresent ? 1 : 0; }
    }

    public class PsConvertError : Exception
    {
        public object Value; public string TypeName; public string Detail;
        public PsConvertError(object v, string t, string detail) : base(Msg(v, t, detail)) { Value = v; TypeName = t; Detail = detail; }
        static string Msg(object v, string t, string detail)
        {
            v = PsConv.Base(v);
            string vt = v == null ? "null" : PsTypes.TypeNameOf(v);
            // no conversion exists at all (a string is never a SecureString): 5.1 uses the short form
            if (detail == null && t == "System.Security.SecureString") return "Cannot convert the \"" + PsConv.ToStr(v) + "\" value of type \"" + vt + "\" to type \"" + t + "\".";
            if (v is string || PsConv.IsNumber(v)) return "Cannot convert value \"" + PsConv.ToStr(v) + "\" to type \"" + t + "\". Error: \"" + (detail ?? "Invalid cast from '" + vt + "' to '" + t + "'.") + "\"";
            return "Cannot convert the \"" + (v == null ? "" : PsConv.ToStr(v)) + "\" value of type \"" + vt + "\" to type \"" + t + "\".";
        }
    }
    public class PsEnumError : PsConvertError
    {
        public PsEnumError(string v, PsEnumType e) : base(v, e.FullName, "Unable to match the identifier name " + v + " to a valid enumerator name. Specify one of the following enumerator names and try again:\n" + string.Join(", ", e.Values.Select(x => x.Key).ToArray())) { }
    }
}
