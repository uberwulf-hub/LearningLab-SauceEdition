// Cert Lab - how the simulated PowerShell shows objects: format views per type (fixed-width tables like
// Get-Service's and Get-Process's, the file listing grouped by folder, list views), and the default rules for other
// objects (a table for up to 4 properties, a list for 5 or more; collections in a cell as {a, b, c, d...}).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class FmtCol
    {
        public string Header = "", Prop; public int Width; public bool Right; public Func<object, object> Get; public string FormatString;
        public bool MultiLine;                 // the last column shows every line of its text (a registry key's Property column)
        public object Value(PsRunspace R, object o)
        {
            if (Get != null) return Get(o);
            bool f;
            var po = PsConv.Base(o) as PsObj;
            if (po != null) return po.Get(Prop);
            return R == null ? null : R.GetMember(o, Prop, null);
        }
    }

    public class FmtView
    {
        public string Kind = "table";          // table, list, wide
        public List<FmtCol> Cols = new List<FmtCol>();
        public Func<object, string> GroupKey;  // the value that starts a new group
        public Func<object, string> GroupHeader;
        public bool Auto;                      // widths from the data
        public string WideProp;
        public int GroupGap;                   // blank lines after a group header (0: the usual rule)
        public Func<object, string> Custom;    // kind "custom": each object is this text (Select-String's matches)
        public bool GroupOneBlank;           // one blank line after the group header (Get-WinEvent, Get-CimClass), not two
    }

    public static partial class PsFormat
    {
        static readonly Dictionary<string, FmtView> views = new Dictionary<string, FmtView>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string[]> listProps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        static bool done;
        static readonly object gate = new object();

        public static FmtCol Col(string header, int width, bool right, Func<object, object> get) { return new FmtCol { Header = header, Width = width, Right = right, Get = get }; }
        public static FmtCol P(string prop, int width) { return new FmtCol { Header = prop, Prop = prop, Width = width }; }
        public static FmtCol PR(string prop, int width) { return new FmtCol { Header = prop, Prop = prop, Width = width, Right = true }; }
        static object G(object o, string p) { var po = PsConv.Base(o) as PsObj; return po == null ? null : po.Get(p); }

        public static void Table(string type, params FmtCol[] cols) { views[type] = new FmtView { Cols = cols.ToList() }; }
        public static void AutoTable(string type, params FmtCol[] cols) { views[type] = new FmtView { Cols = cols.ToList(), Auto = true }; }
        public static void View(string type, FmtView v) { views[type] = v; }
        /// <summary>What Format-List shows for a type when no properties are named.</summary>
        public static void ListProps(string type, params string[] props) { listProps[type] = props; }

        static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                Table("System.Collections.DictionaryEntry", P("Name", 30), P("Value", 0));
                Table("System.Collections.Generic.KeyValuePair", P("Key", 0), P("Value", 0));
                Table("System.RuntimeType", P("IsPublic", 8), P("IsSerial", 8), P("Name", 40), P("BaseType", 0));
                Table("System.ServiceProcess.ServiceController", P("Status", 8), P("Name", 18), P("DisplayName", 38));
                Table("System.Version", P("Major", 6), P("Minor", 6), P("Build", 6), P("Revision", 0));
                Table("System.Diagnostics.Process",
                    Col("Handles", 7, true, o => G(o, "HandleCount") ?? G(o, "Handles")), Col("NPM(K)", 7, true, o => K(G(o, "NPM"))), Col("PM(K)", 8, true, o => K(G(o, "PM"))),
                    Col("WS(K)", 10, true, o => K(G(o, "WS"))), Col("CPU(s)", 10, true, o => { var c = G(o, "CPU"); return c == null ? "" : PsConv.ToDouble(c).ToString("N2", PsConv.Us); }),
                    Col("Id", 6, true, o => G(o, "Id")), Col("SI", 3, true, o => G(o, "SI") ?? G(o, "SessionId")), P("ProcessName", 0));
                var fs = new FmtView
                {
                    Cols = new List<FmtCol> { P("Mode", 7), Col("LastWriteTime", 25, true, o => { var d = G(o, "LastWriteTime"); if (!(d is DateTime)) return ""; var t = (DateTime)d; return string.Format(PsConv.Us, "{0,10}  {1,8}", t.ToString("d", PsConv.Us), t.ToString("t", PsConv.Us)); }), Col("Length", 14, true, o => G(o, "PSIsContainer") is bool && (bool)G(o, "PSIsContainer") ? "" : G(o, "Length")), P("Name", 0) },
                    GroupKey = o => PsConv.ToStr(G(o, "PSParentPath")),
                    GroupHeader = o => "    Directory: " + ParentDisplay(PsConv.ToStr(G(o, "PSParentPath")))
                };
                views["System.IO.FileInfo"] = fs; views["System.IO.DirectoryInfo"] = fs;
                AutoTable("System.Management.Automation.CommandInfo", P("CommandType", 15), Col("Name", 50, false, o => G(o, "DisplayName") ?? G(o, "Name")), Col("Version", 10, false, o => G(o, "Version")), P("Source", 0));
                views["System.Management.Automation.CommandInfo"].Auto = false;
                AutoTable("Microsoft.PowerShell.Commands.MemberDefinition", P("Name", 0), P("MemberType", 0), P("Definition", 0));
                views["Microsoft.PowerShell.Commands.MemberDefinition"].GroupKey = o => PsConv.ToStr(G(o, "TypeName"));
                views["Microsoft.PowerShell.Commands.MemberDefinition"].GroupHeader = o => "   TypeName: " + PsConv.ToStr(G(o, "TypeName"));
                Table("System.Management.Automation.PSModuleInfo", P("ModuleType", 10), Col("Version", 10, false, o => G(o, "Version")), P("Name", 35), Col("ExportedCommands", 0, false, o => G(o, "ExportedCommandsText")));
                // Get-Module -ListAvailable groups by folder (ListDirectory); loaded modules have none, so no group header
                views["System.Management.Automation.PSModuleInfo"].GroupKey = o => { var po = PsConv.Base(o) as PsObj; var pm = po == null ? null : po.Base as PsModule; return pm == null ? null : pm.ListDirectory; };
                views["System.Management.Automation.PSModuleInfo"].GroupHeader = o => "    Directory: " + ((PsModule)((PsObj)PsConv.Base(o)).Base).ListDirectory;
                AutoTable("Microsoft.PowerShell.Commands.HistoryInfo", PR("Id", 0), P("CommandLine", 0));
                AutoTable("Microsoft.PowerShell.Commands.GroupInfo", PR("Count", 0), P("Name", 0), P("Group", 0));
                AutoTable("Microsoft.PowerShell.Commands.GroupInfoNoElement", PR("Count", 0), P("Name", 0));
                Table("System.Management.Automation.PSVariable", P("Name", 30), P("Value", 0));
                AutoTable("System.Management.Automation.PathInfo", P("Path", 0));
                AutoTable("System.Management.Automation.PSDriveInfo", P("Name", 0), Col("Used (GB)", 0, true, o => { var u = G(o, "Used"); return u == null ? "" : (PsConv.ToDouble(u) / 1073741824).ToString("0.00", PsConv.Us); }), Col("Free (GB)", 0, true, o => { var u = G(o, "Free"); return u == null ? "" : (PsConv.ToDouble(u) / 1073741824).ToString("0.00", PsConv.Us); }), Col("Provider", 0, false, o => { var p = G(o, "Provider") as PsObj; return p == null ? null : p.Get("Name"); }), P("Root", 0), PR("CurrentLocation", 0));
                ListProps("Microsoft.PowerShell.Commands.GenericMeasureInfo", "Count", "Average", "Sum", "Maximum", "Minimum", "Property");
                ListProps("Microsoft.PowerShell.Commands.TextMeasureInfo", "Lines", "Words", "Characters", "Property");
                ListProps("System.Diagnostics.Process", "Id", "Handles", "CPU", "SI", "Name");
                ListProps("System.ServiceProcess.ServiceController", "Name", "DisplayName", "Status", "DependentServices", "ServicesDependedOn", "CanPauseAndContinue", "CanShutdown", "CanStop", "ServiceType");
                ListProps("System.IO.FileInfo", "Name", "Length", "CreationTime", "LastWriteTime", "LastAccessTime", "Mode", "LinkType", "Target", "VersionInfo");
                ListProps("System.IO.DirectoryInfo", "Name", "CreationTime", "LastWriteTime", "LastAccessTime", "Mode", "LinkType", "Target");
                ListProps("System.TimeSpan", "Days", "Hours", "Minutes", "Seconds", "Milliseconds", "Ticks", "TotalDays", "TotalHours", "TotalMinutes", "TotalSeconds", "TotalMilliseconds");
                RegisterViews();
            }
        }
        static partial void RegisterViews();

        static object K(object v) { if (v == null) return ""; return (long)(PsConv.ToDouble(v) / 1024); }
        static string ParentDisplay(string p)
        {
            int i = p.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? p.Substring(i + 2) : p;
        }

        public static FmtView ViewFor(object o)
        {
            Init();
            o = PsConv.Base(o);
            if (o is DictionaryEntry) return views["System.Collections.DictionaryEntry"];
            if (o is PsTypeInfo) return views["System.RuntimeType"];
            var tn = TypeNamesOf(o);
            foreach (var n in tn)
            {
                FmtView v;
                string k = n.StartsWith("Deserialized.", StringComparison.Ordinal) ? n.Substring(13) : n;
                if (views.TryGetValue(k, out v)) return v;
            }
            if (o != null && o.GetType().IsGenericType && o.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) return views["System.Collections.Generic.KeyValuePair"];
            return null;
        }
        public static string[] ListPropsFor(object o)
        {
            Init();
            foreach (var n in TypeNamesOf(o)) { string[] l; string k = n.StartsWith("Deserialized.", StringComparison.Ordinal) ? n.Substring(13) : n; if (listProps.TryGetValue(k, out l)) return l; }
            return null;
        }
        static List<string> TypeNamesOf(object o) { return PsMembers.TypeNames(o); }

        /// <summary>A value as it appears in a table cell or after "Name : " in a list.</summary>
        public static string Cell(PsRunspace R, object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "";
            if (v is string) return (string)v;
            if (v is DateTime) return ((DateTime)v).ToString(PsConv.Us);
            if (v is double) return PsConv.DoubleStr((double)v);
            if (v is bool) return (bool)v ? "True" : "False";
            if (v is IDictionary) return "{" + string.Join(", ", ((IDictionary)v).Keys.Cast<object>().Take(Limit(R)).Select(k => PsConv.ToStr(k)).ToArray()) + (((IDictionary)v).Count > Limit(R) ? "..." : "") + "}";
            if (PsConv.IsCollection(v))
            {
                var l = ((IEnumerable)v).Cast<object>().ToList();
                int lim = Limit(R);
                return "{" + string.Join(", ", l.Take(lim).Select(x => Cell(R, x)).ToArray()) + (l.Count > lim ? "..." : "") + "}";
            }
            if (v is PsScriptBlock) return ((PsScriptBlock)v).Text;
            return PsConv.ToStr(v);
        }
        static int Limit(PsRunspace R) { if (R == null) return 4; var v = R.GetVar("FormatEnumerationLimit", R.Global); try { return v == null ? 4 : PsConv.ToInt(v); } catch (PsConvertError) { return 4; } }

        /// <summary>Is this shown on its own line with ToString (strings, numbers, dates...)?</summary>
        public static bool IsScalar(object o)
        {
            o = PsConv.Base(o);
            return o == null || o is string || PsConv.IsNumber(o) || o is bool || o is char || o is PsEnum || o is DateTime || o is Guid || o is PsSecureString || o is PsScriptBlock || o is PsSwitch && false
                || o is PsException || o is System.Text.RegularExpressions.Regex || (o is PsObj && ((PsObj)o).DefaultDisplay != null && ((PsObj)o).DefaultDisplay.Length == 0);
        }
        public static string ScalarText(object o)
        {
            o = PsConv.Base(o);
            if (o is DateTime) return ((DateTime)o).ToString("dddd, MMMM d, yyyy h:mm:ss tt", PsConv.Us);
            if (o is double) return PsConv.DoubleStr((double)o);
            if (o is PsScriptBlock) return ((PsScriptBlock)o).Text;
            return PsConv.ToStr(o);
        }

        /// <summary>The properties shown by default (DefaultDisplayPropertySet, or all of them).</summary>
        public static List<string> DefaultProps(object o)
        {
            var po = PsConv.Base(o) as PsObj;
            if (po != null && po.DefaultDisplay != null && po.DefaultDisplay.Length > 0) return po.DefaultDisplay.ToList();
            return PsMembers.Properties(o).Select(kv => kv.Key).ToList();
        }

        public static string Pad(string s, int w, bool right)
        {
            s = (s ?? "").Replace("\r", "").Replace("\n", " ");
            if (w <= 0) return s;
            if (s.Length > w) s = w <= 3 ? s.Substring(0, w) : (right ? "..." + s.Substring(s.Length - (w - 3)) : s.Substring(0, w - 3) + "...");
            return right ? s.PadLeft(w) : s.PadRight(w);
        }
    }
}
