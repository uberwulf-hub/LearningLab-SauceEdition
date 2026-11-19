// Cert Lab - Windows administration cmdlets, part 2: the event logs. Get-WinEvent (logs, providers, -FilterHashtable,
// a simple -FilterXPath), the classic Get-EventLog / Write-EventLog / New-EventLog / Remove-EventLog / Clear-EventLog /
// Limit-EventLog of Windows PowerShell 5.1 (gone in PowerShell 7), all over the pretend machine's logs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static string LevelName(int level)
        {
            switch (level) { case 1: return "Critical"; case 2: return "Error"; case 3: return "Warning"; case 5: return "Verbose"; }
            return "Information";
        }
        static string EntryTypeOf(WinEvent e)
        {
            if (e.Log == "Security") return e.Keywords == "Audit Failure" ? "FailureAudit" : "SuccessAudit";
            return e.Level == 1 || e.Level == 2 ? "Error" : e.Level == 3 ? "Warning" : "Information";
        }
        static long InstanceIdOf(WinEvent e) { return e.Qualifiers > 0 ? (e.Qualifiers << 16) + e.Id : e.Id; }
        static string SidName(PsMachine m, string sid)
        {
            if (sid == null) return null;
            if (sid == "S-1-5-18") return "NT AUTHORITY\\SYSTEM";
            if (sid == "S-1-5-19") return "NT AUTHORITY\\LOCAL SERVICE";
            if (sid == "S-1-5-20") return "NT AUTHORITY\\NETWORK SERVICE";
            if (sid == m.AdminUserSid) return m.DomainShort + "\\Administrator";
            var u = m.Users.FirstOrDefault(x => x.Sid == sid);
            return u != null ? m.Name + "\\" + u.Name : sid;
        }
        public static PsObj SidObj(string sid)
        {
            if (sid == null) return null;
            var o = new PsObj("System.Security.Principal.SecurityIdentifier", "System.Security.Principal.IdentityReference", "System.Object");
            o.Val("BinaryLength", "int", 8 + 4 * Math.Max(0, sid.Count(ch => ch == '-') - 2));
            int last = sid.LastIndexOf('-');
            o.Val("AccountDomainSid", "System.Security.Principal.SecurityIdentifier", sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && last > 9 ? sid.Substring(0, last) : null);
            o.Val("Value", "string", sid);
            o.ToStr = x => sid;
            o.DefaultDisplay = new[] { "BinaryLength", "AccountDomainSid", "Value" };
            o.Method("Translate", "System.Security.Principal.IdentityReference Translate(type targetType)", (self, a) => sid);
            return o;
        }

        static readonly Dictionary<string, string> taskNames = new Dictionary<string, string> { { "4624", "Logon" }, { "4625", "Logon" }, { "4634", "Logoff" }, { "4672", "Special Logon" }, { "4608", "Security State Change" }, { "4720", "User Account Management" }, { "4722", "User Account Management" }, { "4724", "User Account Management" }, { "4725", "User Account Management" }, { "4726", "User Account Management" }, { "4738", "User Account Management" }, { "4781", "User Account Management" }, { "4731", "Security Group Management" }, { "4732", "Security Group Management" }, { "4733", "Security Group Management" }, { "4734", "Security Group Management" }, { "4735", "Security Group Management" }, { "4798", "User Account Management" }, { "1102", "Log clear" } };

        public static PsObj EventRecordObj(PsMachine m, WinEvent e)
        {
            var o = new PsObj("System.Diagnostics.Eventing.Reader.EventLogRecord", "System.Diagnostics.Eventing.Reader.EventRecord", "System.Object");
            o.Base = e;
            bool sec = e.Log == "Security";
            string task; taskNames.TryGetValue(e.Id.ToString(CultureInfo.InvariantCulture), out task);
            o.Val("Message", "string", e.Message);
            o.Val("Id", "int", e.Id);
            o.Val("Version", "System.Nullable[byte]", sec ? (object)(byte)(e.Id == 4624 ? 2 : 0) : (e.Qualifiers >= 0 ? null : (object)(byte)0));
            o.Val("Qualifiers", "System.Nullable[int]", e.Qualifiers >= 0 ? (object)(int)e.Qualifiers : null);
            o.Val("Level", "System.Nullable[byte]", (byte)e.Level);
            o.Val("Task", "System.Nullable[int]", sec && task != null ? 12544 + Math.Abs(FwHash.Of(task) % 50) : 0);
            o.Val("Opcode", "System.Nullable[short]", (short)0);
            o.Val("Keywords", "System.Nullable[long]", sec ? (e.Keywords == "Audit Failure" ? -9218868437227405312L : -9214364837600034816L) : 36028797018963968L);
            o.Val("RecordId", "System.Nullable[long]", e.RecordId);
            o.Val("ProviderName", "string", e.Provider);
            o.Val("ProviderId", "System.Nullable[guid]", e.Qualifiers >= 0 ? null : (object)new Guid(Math.Abs(FwHash.Of(e.Provider)), 0x1234, 0x5678, 0x9a, 0xbc, 0xde, 0xf0, 0x12, 0x34, 0x56, 0x78));
            o.Val("LogName", "string", e.Log);
            o.Val("ProcessId", "System.Nullable[int]", e.Pid);
            o.Val("ThreadId", "System.Nullable[int]", e.Tid);
            o.Val("MachineName", "string", m.Fqdn.Substring(0, m.Name.Length).ToUpperInvariant() + m.Fqdn.Substring(m.Name.Length));
            o.Val("UserId", "System.Security.Principal.SecurityIdentifier", SidObj(e.User));
            o.Val("TimeCreated", "System.Nullable[datetime]", e.Time);
            o.Val("ActivityId", "System.Nullable[guid]", null);
            o.Val("RelatedActivityId", "System.Nullable[guid]", null);
            o.Val("ContainerLog", "string", e.Log);
            o.Val("MatchedQueryIds", "uint32[]", new object[0]);
            o.Val("Bookmark", "System.Diagnostics.Eventing.Reader.EventBookmark", "System.Diagnostics.Eventing.Reader.EventBookmark");
            o.Val("LevelDisplayName", "string", LevelName(e.Level));
            o.Val("OpcodeDisplayName", "string", "Info");
            o.Val("TaskDisplayName", "string", sec ? task : (e.Qualifiers >= 0 ? null : "None"));
            o.Val("KeywordsDisplayNames", "System.Collections.Generic.IEnumerable[string]", new object[] { sec ? e.Keywords : "Classic" });
            o.Val("Properties", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventProperty]", e.Strings.Select(s => (object)s).ToArray());
            o.Method("ToXml", "string ToXml()", (self, a) => "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='" + e.Provider + "'/><EventID" + (e.Qualifiers >= 0 ? " Qualifiers='" + e.Qualifiers + "'" : "") + ">" + e.Id + "</EventID><Level>" + e.Level + "</Level><TimeCreated SystemTime='" + e.Time.AddHours(-m.UtcOffsetHours).ToString("yyyy-MM-ddTHH:mm:ss.fffffff00Z", CultureInfo.InvariantCulture) + "'/><EventRecordID>" + e.RecordId + "</EventRecordID><Channel>" + e.Log + "</Channel><Computer>" + m.Fqdn + "</Computer>" + (e.User != null ? "<Security UserID='" + e.User + "'/>" : "<Security/>") + "</System><EventData>" + string.Join("", e.Strings.Select(s => "<Data>" + s + "</Data>").ToArray()) + "</EventData></Event>");
            o.Method("FormatDescription", "string FormatDescription()", (self, a) => e.Message);
            return o;
        }

        public static PsObj EventEntryObj(PsMachine m, WinEvent e, string machineName)
        {
            var o = new PsObj("System.Diagnostics.EventLogEntry", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            o.Base = e;
            o.Val("EventID", "int", e.Id);
            o.Val("MachineName", "string", machineName);
            o.Val("Data", "byte[]", new object[0]);
            o.Val("Index", "int", (int)e.RecordId);
            string task; taskNames.TryGetValue(e.Id.ToString(CultureInfo.InvariantCulture), out task);
            o.Val("Category", "string", e.Log == "Security" && task != null ? task : "(" + e.Category + ")");
            o.Val("CategoryNumber", "short", (short)e.Category);
            o.Val("EntryType", "System.Diagnostics.EventLogEntryType", En("System.Diagnostics.EventLogEntryType", EntryTypeOf(e)));
            o.Val("Message", "string", e.Message);
            o.Val("Source", "string", e.ClassicSource);
            o.Val("ReplacementStrings", "string[]", e.Strings.Length > 0 ? e.Strings.Select(s => (object)s).ToArray() : new object[] { e.Message });
            o.Val("InstanceId", "long", InstanceIdOf(e));
            o.Val("TimeGenerated", "datetime", e.Time);
            o.Val("TimeWritten", "datetime", e.Time);
            o.Val("UserName", "string", SidName(m, e.User));
            o.Val("Site", "System.ComponentModel.ISite", null);
            o.Val("Container", "System.ComponentModel.IContainer", null);
            o.ToStr = x => "System.Diagnostics.EventLogEntry";
            return o;
        }

        static PsObj ClassicLogObj(PsMachine m, WinLog l, string machineName)
        {
            var o = new PsObj("System.Diagnostics.EventLog", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            o.Base = l;
            o.Prop("Entries", "System.Diagnostics.EventLogEntryCollection", () => l.Events.Select(e => (object)EventEntryObj(m, e, machineName)).ToArray());
            o.Val("Log", "string", l.Name);
            o.Val("LogDisplayName", "string", l.Display ?? l.Name);
            o.Val("Source", "string", "");
            o.Val("EnableRaisingEvents", "bool", false);
            o.Val("MachineName", "string", machineName);
            o.Prop("MaximumKilobytes", "long", () => l.MaxKB);
            o.Prop("OverflowAction", "System.Diagnostics.OverflowAction", () => En("System.Diagnostics.OverflowAction", l.Overflow));
            o.Prop("MinimumRetentionDays", "int", () => l.Retention);
            o.Val("SynchronizingObject", "System.ComponentModel.ISynchronizeInvoke", null);
            o.Val("Site", "System.ComponentModel.ISite", null);
            o.Val("Container", "System.ComponentModel.IContainer", null);
            o.ToStr = x => l.Name;
            o.Method("Clear", "void Clear()", (self, a) => { l.Events.Clear(); return AutoNull.Value; });
            return o;
        }

        static PsObj LogConfigObj(PsMachine m, WinLog l)
        {
            var o = new PsObj("System.Diagnostics.Eventing.Reader.EventLogConfiguration", "System.Object");
            o.Base = l;
            long size = Math.Min(l.MaxKB * 1024, 69632 + l.Events.Count * 1180L);
            size = (size + 65535) / 65536 * 65536;
            o.Val("FileSize", "System.Nullable[long]", l.Enabled ? (object)size : null);
            o.Val("IsLogFull", "System.Nullable[bool]", false);
            o.Val("LastAccessTime", "System.Nullable[datetime]", m.BootTime);
            o.Val("LastWriteTime", "System.Nullable[datetime]", l.Events.Count > 0 ? (object)l.Events.Last().Time : m.InstallDate);
            o.Val("OldestRecordNumber", "System.Nullable[long]", l.Events.Count > 0 ? l.Events[0].RecordId : 0);
            o.Val("RecordCount", "System.Nullable[long]", (long)l.Events.Count);
            o.Val("LogName", "string", l.Name);
            o.Val("LogType", "System.Diagnostics.Eventing.Reader.EventLogType", En("System.Diagnostics.Eventing.Reader.EventLogType", l.Classic ? "Administrative" : "Operational"));
            o.Val("LogIsolation", "string", l.Name == "Security" ? "Custom" : l.Classic && l.Name != "Windows PowerShell" ? "System" : "Application");
            o.Prop("IsEnabled", "bool", () => l.Enabled, v => l.Enabled = PsConv.ToBool(v));
            o.Val("IsClassicLog", "bool", l.Classic);
            o.Val("SecurityDescriptor", "string", "O:BAG:SYD:(A;;0x2;;;S-1-15-2-1)(A;;0xf0007;;;SY)(A;;0x7;;;BA)(A;;0x3;;;BO)(A;;0x5;;;SO)(A;;0x1;;;IU)(A;;0x3;;;SU)(A;;0x1;;;S-1-5-3)(A;;0x2;;;S-1-5-33)(A;;0x1;;;S-1-5-32-573)");
            o.Val("LogFilePath", "string", l.Path);
            o.Prop("MaximumSizeInBytes", "long", () => l.MaxKB * 1024, v => l.MaxKB = PsConv.ToLong(v) / 1024);
            o.Prop("LogMode", "System.Diagnostics.Eventing.Reader.EventLogMode", () => En("System.Diagnostics.Eventing.Reader.EventLogMode", l.Overflow == "DoNotOverwrite" ? "Retain" : "Circular"));
            o.Val("OwningProviderName", "string", l.Classic ? "" : l.Sources.FirstOrDefault() ?? "");
            o.Val("ProviderNames", "System.Collections.Generic.IEnumerable[string]", l.Sources.Select(s => (object)(s == "DCOM" ? "Microsoft-Windows-DistributedCOM" : s)).ToArray());
            o.Val("ProviderLevel", "System.Nullable[int]", null);
            o.Val("ProviderKeywords", "System.Nullable[long]", null);
            o.Val("ProviderBufferSize", "System.Nullable[int]", null);
            o.Val("ProviderMinimumNumberOfBuffers", "System.Nullable[int]", null);
            o.Val("ProviderMaximumNumberOfBuffers", "System.Nullable[int]", null);
            o.Val("ProviderLatency", "System.Nullable[int]", null);
            o.Val("ProviderControlGuid", "System.Nullable[guid]", null);
            o.Method("SaveChanges", "void SaveChanges()", (self, a) => AutoNull.Value);
            o.ToStr = x => "System.Diagnostics.Eventing.Reader.EventLogConfiguration";
            return o;
        }

        /// <summary>Events of the logs that match, newest first (or oldest first), with the hashtable / XPath filters.</summary>
        static List<WinEvent> QueryEvents(PsMachine m, IEnumerable<WinLog> logs, Func<WinEvent, bool> filter, bool oldest)
        {
            var all = new List<WinEvent>();
            foreach (var l in logs) all.AddRange(l.Events.Where(filter));
            return oldest ? all.OrderBy(e => e.Time).ThenBy(e => e.RecordId).ToList() : all.OrderByDescending(e => e.Time).ThenByDescending(e => e.RecordId).ToList();
        }
        static string ProvName(string p) { return p == "DCOM" ? "Microsoft-Windows-DistributedCOM" : p; }

        static Func<WinEvent, bool> HashFilter(CmdCtx c, PsMachine m, IDictionary h, out List<WinLog> logs)
        {
            var preds = new List<Func<WinEvent, bool>>();
            logs = null;
            string[] provs = null;
            foreach (DictionaryEntry kv in h)
            {
                string k = PsConv.ToStr(kv.Key).ToLowerInvariant();
                var vals = PsConv.Items(PsConv.Base(kv.Value)).Select(x => PsConv.Base(x)).ToList();
                switch (k)
                {
                    case "logname":
                        var names = vals.Select(x => PsConv.ToStr(x)).ToList();
                        logs = m.EventLogs.Where(l => names.Any(n => PsConv.Like(l.Name, n, false))).ToList();
                        foreach (var n in names) if (!PsConv.HasWildcard(n) && m.EvLog(n) == null) throw c.Fail("There is not an event log on the localhost computer that matches \"" + n + "\".", "NoMatchingLogsFound", "ObjectNotFound", n, "System.Exception");
                        break;
                    case "providername":
                        provs = vals.Select(x => PsConv.ToStr(x)).ToArray();
                        var pv = provs;
                        preds.Add(e => pv.Any(p => PsConv.Like(e.Provider, p, false) || PsConv.Like(e.ClassicSource, p, false)));
                        break;
                    case "id": case "eventid":
                        var ids = vals.Select(x => PsConv.ToInt(x)).ToList();
                        preds.Add(e => ids.Contains(e.Id));
                        break;
                    case "level":
                        var lv = vals.Select(x => x is PsEnum ? (int)((PsEnum)x).Value : PsConv.ToInt(x)).ToList();
                        preds.Add(e => lv.Contains(e.Level) || lv.Contains(4) && e.Level == 0 && false);
                        break;
                    case "starttime":
                        var st = (DateTime)PsConv.To(vals[0], "datetime");
                        preds.Add(e => e.Time >= st);
                        break;
                    case "endtime":
                        var et = (DateTime)PsConv.To(vals[0], "datetime");
                        preds.Add(e => e.Time <= et);
                        break;
                    case "userid":
                        var users = vals.Select(x => PsConv.ToStr(x)).ToList();
                        preds.Add(e => e.User != null && users.Any(u => u.Equals(e.User, StringComparison.OrdinalIgnoreCase) || u.Equals(SidName(m, e.User), StringComparison.OrdinalIgnoreCase)));
                        break;
                    case "keywords":
                        var kw = vals.Select(x => PsConv.ToLong(x)).ToList();
                        preds.Add(e => kw.Any(x => e.Log == "Security" ? (x == 9007199254740992L && e.Keywords == "Audit Success" || x == 4503599627370496L && e.Keywords == "Audit Failure") : (x & 36028797018963968L) != 0));
                        break;
                    case "data":
                        var data = vals.Select(x => PsConv.ToStr(x)).ToList();
                        preds.Add(e => e.Strings.Any(s => data.Contains(s, StringComparer.OrdinalIgnoreCase)) || data.Any(d => e.Message.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0));
                        break;
                    case "path":
                        throw c.Fail("Cert Lab: the practice machines have no saved .evtx files; read a live log with LogName.", "CertLabNotSimulated", "ObjectNotFound", null, "System.Exception");
                    default:
                        throw c.Fail("The \"" + PsConv.ToStr(kv.Key) + "\" key is not valid in a hash table query. Use LogName, ProviderName, Path, Keywords, ID, Level, StartTime, EndTime, UserID or Data.", "InvalidHashtableKey", "InvalidArgument", null, "System.Exception");
                }
            }
            if (logs == null)
            {
                if (provs == null) throw c.Fail("The hash table must contain LogName, ProviderName or Path.", "LogsAndProvidersDontOverlap", "InvalidArgument", null, "System.Exception");
                var pv2 = provs;
                logs = m.EventLogs.Where(l => l.Sources.Any(s => pv2.Any(p => PsConv.Like(s, p, false) || PsConv.Like(ProvName(s), p, false)))).ToList();
                if (logs.Count == 0) throw c.Fail("There is not an event provider on the localhost computer that matches \"" + pv2[0] + "\".", "NoMatchingProvidersFound", "ObjectNotFound", pv2[0], "System.Exception");
            }
            var all = preds;
            return e => all.All(p => p(e));
        }

        /// <summary>A small XPath reader: EventID=, Level=, Provider[@Name=''], TimeCreated[timediff(@SystemTime) &lt;= ms], joined by and / or.</summary>
        static Func<WinEvent, bool> XPathFilter(CmdCtx c, PsMachine m, string xp)
        {
            if (string.IsNullOrWhiteSpace(xp) || xp.Trim() == "*") return e => true;
            var preds = new List<Func<WinEvent, bool>>();
            foreach (Match mm in Regex.Matches(xp, @"EventID\s*=\s*(\d+)", RegexOptions.IgnoreCase)) { }
            var idList = Regex.Matches(xp, @"EventID\s*=\s*(\d+)", RegexOptions.IgnoreCase).Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).ToList();
            if (idList.Count > 0) preds.Add(e => idList.Contains(e.Id));
            var lvl = Regex.Matches(xp, @"Level\s*=\s*(\d+)", RegexOptions.IgnoreCase).Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).ToList();
            if (lvl.Count > 0) preds.Add(e => lvl.Contains(e.Level));
            var prov = Regex.Matches(xp, @"Provider\s*\[\s*@Name\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase).Cast<Match>().Select(x => x.Groups[1].Value).ToList();
            if (prov.Count > 0) preds.Add(e => prov.Any(p => p.Equals(e.Provider, StringComparison.OrdinalIgnoreCase) || p.Equals(e.ClassicSource, StringComparison.OrdinalIgnoreCase)));
            var td = Regex.Match(xp, @"timediff\s*\(\s*@SystemTime\s*\)\s*<=\s*(\d+)", RegexOptions.IgnoreCase);
            if (td.Success) { long ms = long.Parse(td.Groups[1].Value); var now = m.Now; preds.Add(e => (now - e.Time).TotalMilliseconds <= ms); }
            if (preds.Count == 0 && !xp.Contains("*[System")) throw c.Fail("The specified query is invalid.", "System.Diagnostics.Eventing.Reader.EventLogException", "NotSpecified", null, "System.Diagnostics.Eventing.Reader.EventLogException");
            return e => preds.All(p => p(e));
        }

        static void RegEvents()
        {
            var gw = Add("Get-WinEvent", Diag, "LogName:string[]@0 ps=GetLogSet; ListLog:string[] ! ps=ListLogSet; ListProvider:string[] ! ps=ListProviderSet; ProviderName:string[] ! ps=GetProviderSet; Path:string[] ! alias=PSPath ps=FileSet; FilterHashtable:hashtable[] ! ps=HashQuerySet; FilterXml:object ! ps=XmlQuerySet; FilterXPath:string@1 ps=GetLogSet,GetProviderSet,FileSet; MaxEvents:long range=1~9223372036854775807; ComputerName:string alias=Cn; Credential:pscredential; Oldest:switch; Force:switch", (c, o) =>
            {
                var m = RemoteRpc(c, c.Str("ComputerName"));
                if (m == null) throw c.Fail("The RPC server is unavailable", "System.Diagnostics.Eventing.Reader.EventLogException", "NotSpecified", null, "System.Diagnostics.Eventing.Reader.EventLogException");
                if (c.Has("ListLog"))
                {
                    foreach (var n in c.Strs("ListLog"))
                    {
                        var l = m.EventLogs.Where(x => PsConv.Like(x.Name, n, false)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                        if (l.Count == 0) { c.Error("There is not an event log on the localhost computer that matches \"" + n + "\".", "NoMatchingLogsFound", "ObjectNotFound", n, "System.Exception"); continue; }
                        foreach (var x in l) if (c.Sw("Force") || x.Classic || x.Enabled || PsConv.HasWildcard(n) == false) c.Out(LogConfigObj(m, x));
                    }
                    return;
                }
                if (c.Has("ListProvider"))
                {
                    foreach (var n in c.Strs("ListProvider"))
                    {
                        var provs = m.EventLogs.SelectMany(l => l.Sources.Select(s => new KeyValuePair<string, WinLog>(ProvName(s), l))).Where(kv => PsConv.Like(kv.Key, n, false)).GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
                        if (provs.Count == 0) { c.Error("There is not an event provider on the localhost computer that matches \"" + n + "\".", "NoMatchingProvidersFound", "ObjectNotFound", n, "System.Exception"); continue; }
                        foreach (var g in provs)
                        {
                            var p = new PsObj("System.Diagnostics.Eventing.Reader.ProviderMetadata", "System.Object");
                            p.Val("Name", "string", g.Key);
                            p.Val("Id", "guid", new Guid(Math.Abs(FwHash.Of(g.Key)), 0x1234, 0x5678, 0x9a, 0xbc, 0xde, 0xf0, 0x12, 0x34, 0x56, 0x78));
                            p.Val("MessageFilePath", "string", "C:\\Windows\\system32\\" + (g.Key.StartsWith("Microsoft-Windows-", StringComparison.Ordinal) ? g.Key.Substring(18).ToLowerInvariant().Replace("-", "") : "eventlog") + ".dll");
                            p.Val("LogLinks", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventLogLink]", g.Select(kv => (object)kv.Value.Name).Distinct().ToArray());
                            p.Val("Levels", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventLevel]", new object[] { "win:Error", "win:Warning", "win:Informational" });
                            p.Val("Opcodes", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventOpcode]", new object[0]);
                            p.Val("Keywords", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventKeyword]", new object[0]);
                            p.Val("Tasks", "System.Collections.Generic.IList[System.Diagnostics.Eventing.Reader.EventTask]", new object[0]);
                            p.Val("Events", "System.Collections.Generic.IEnumerable[System.Diagnostics.Eventing.Reader.EventMetadata]", new object[0]);
                            p.DefaultDisplay = new[] { "Name", "LogLinks" };
                            c.Out(p);
                        }
                    }
                    return;
                }
                if (c.Has("Path")) throw c.Fail("Cert Lab: the practice machines have no saved .evtx files; read a live log with -LogName.", "CertLabNotSimulated", "ObjectNotFound", c.Strs("Path")[0], "System.Exception");
                if (c.Has("FilterXml")) throw c.Fail("Cert Lab: -FilterXml is not simulated; use -FilterHashtable or -FilterXPath.", "CertLabNotSimulated", "NotImplemented", null, "System.Exception");
                List<WinLog> logs; Func<WinEvent, bool> filter = e => true;
                var notFound = new List<string>();
                if (c.Has("FilterHashtable"))
                {
                    var fs = new List<Func<WinEvent, bool>>(); var ls = new List<WinLog>();
                    foreach (var h in c.Objs("FilterHashtable"))
                    {
                        var d = PsConv.Base(h) as IDictionary;
                        if (d == null) continue;
                        List<WinLog> lg; var f = HashFilter(c, m, d, out lg);
                        var set = new HashSet<WinLog>(lg); fs.Add(e => set.Contains(m.EvLog(e.Log)) && f(e));
                        foreach (var x in lg) if (!ls.Contains(x)) ls.Add(x);
                    }
                    logs = ls; filter = e => fs.Any(f => f(e));
                }
                else if (c.Has("ProviderName"))
                {
                    var provs = c.Strs("ProviderName");
                    foreach (var p in provs) if (!m.EventLogs.Any(l => l.Sources.Any(s => PsConv.Like(s, p, false) || PsConv.Like(ProvName(s), p, false)))) c.Error("There is not an event provider on the localhost computer that matches \"" + p + "\".", "NoMatchingProvidersFound", "ObjectNotFound", p, "System.Exception");
                    logs = m.EventLogs.Where(l => l.Sources.Any(s => provs.Any(p => PsConv.Like(s, p, false) || PsConv.Like(ProvName(s), p, false)))).ToList();
                    filter = e => provs.Any(p => PsConv.Like(e.Provider, p, false) || PsConv.Like(e.ClassicSource, p, false));
                }
                else
                {
                    var names = c.Strs("LogName") ?? new[] { "*" };
                    logs = new List<WinLog>();
                    foreach (var n in names)
                    {
                        var l = m.EventLogs.Where(x => PsConv.Like(x.Name, n, false)).ToList();
                        if (l.Count == 0) { c.Error("There is not an event log on the localhost computer that matches \"" + n + "\".", "NoMatchingLogsFound", "ObjectNotFound", n, "System.Exception"); continue; }
                        foreach (var x in l) if (!logs.Contains(x)) logs.Add(x);
                    }
                    if (logs.Count == 0) return;
                }
                if (c.Has("FilterXPath")) { var xf = XPathFilter(c, m, c.Str("FilterXPath")); var f0 = filter; filter = e => f0(e) && xf(e); }
                if (logs.Count == 0) return;
                var evs = QueryEvents(m, logs, filter, c.Sw("Oldest"));
                if (c.Has("MaxEvents")) evs = evs.Take((int)Math.Min(int.MaxValue, c.Long("MaxEvents", 0))).ToList();
                if (evs.Count == 0) { c.Error("No events were found that match the specified selection criteria.", "NoMatchingEventsFound", "ObjectNotFound", null, "System.Exception"); return; }
                foreach (var e in evs) c.Out(EventRecordObj(m, e));
            });
            gw.DefaultSet = "GetLogSet";

            var ge = Add("Get-EventLog", Mgmt, "LogName:string@0 ! alias=LN ps=LogName; InstanceId:long[]@1 ps=LogName; List:switch ps=List; AsString:switch ps=List; ComputerName:string[] alias=Cn; Newest:int range=0~2147483647 ps=LogName; After:datetime ps=LogName; Before:datetime ps=LogName; UserName:string[] ps=LogName; Index:int[] ps=LogName; EntryType:string[] alias=ET set=Error|Information|FailureAudit|SuccessAudit|Warning ps=LogName; Source:string[] alias=ABO ps=LogName; Message:string alias=MSG ps=LogName; AsBaseObject:switch ps=LogName", (c, o) =>
            {
                foreach (var cn in c.Strs("ComputerName") ?? new[] { "." })
                {
                    var m = RemoteRpc(c, cn);
                    if (m == null) { c.Error("The network path was not found.", "System.IO.IOException", "NotSpecified", null, "System.IO.IOException"); continue; }
                    string ml = m == c.M ? m.Name : cn;
                    if (c.Sw("List"))
                    {
                        foreach (var l in m.EventLogs.Where(x => x.Classic).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                            c.Out(c.Sw("AsString") ? (object)l.Name : ClassicLogObj(m, l, ml));
                        continue;
                    }
                    string ln = c.Str("LogName");
                    var log = m.EventLogs.FirstOrDefault(x => x.Classic && x.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (log == null) { c.Error("The event log '" + ln + "' on computer '" + (m == c.M ? "." : cn) + "' does not exist.", "System.InvalidOperationException", "NotSpecified", null, "System.InvalidOperationException"); continue; }
                    IEnumerable<WinEvent> q = log.Events.AsEnumerable().Reverse();
                    if (c.Has("InstanceId")) { var ids = c.Objs("InstanceId").Select(x => PsConv.ToLong(x)).ToList(); q = q.Where(e => ids.Contains(InstanceIdOf(e)) || ids.Contains(e.Id)); }
                    if (c.Has("EntryType")) { var ets = c.Strs("EntryType"); q = q.Where(e => ets.Any(x => x.Equals(EntryTypeOf(e), StringComparison.OrdinalIgnoreCase))); }
                    if (c.Has("Source")) { var srcs = c.Strs("Source"); q = q.Where(e => srcs.Any(s => PsConv.Like(e.ClassicSource, s, false))); }
                    if (c.Has("Message")) { var msg = c.Str("Message"); q = q.Where(e => PsConv.Like(e.Message, msg, false)); }
                    if (c.Has("After")) { var a = (DateTime)c.Get("After"); q = q.Where(e => e.Time > a); }
                    if (c.Has("Before")) { var b = (DateTime)c.Get("Before"); q = q.Where(e => e.Time < b); }
                    if (c.Has("UserName")) { var us = c.Strs("UserName"); q = q.Where(e => e.User != null && us.Any(u => PsConv.Like(SidName(m, e.User), u, false))); }
                    if (c.Has("Index")) { var ix = c.Objs("Index").Select(x => PsConv.ToLong(x)).ToList(); q = q.Where(e => ix.Contains(e.RecordId)); }
                    if (c.Has("Newest")) q = q.Take(c.Int("Newest", 0));
                    var l2 = q.ToList();
                    if (l2.Count == 0) { c.Error("No matches found", "GetEventLogNoEntriesFound", "ObjectNotFound", null, "System.ArgumentException"); continue; }
                    foreach (var e in l2) c.Out(EventEntryObj(m, e, ml));
                }
            });
            ge.DefaultSet = "LogName";

            var we = Add("Write-EventLog", Mgmt, "LogName:string@0 ! alias=LN; Source:string@1 ! alias=SRC; EventId:int@2 ! alias=ID,EID range=0~65535; EntryType:string@3 alias=ET set=Error|Warning|Information|SuccessAudit|FailureAudit; Message:string@4 ! alias=MSG; Category:int; RawData:byte[]; ComputerName:string alias=CN", (c, o) =>
            {
                var m = RemoteRpc(c, c.Str("ComputerName"));
                if (m == null) throw c.Fail("The network path was not found.", "System.IO.IOException", "NotSpecified", null, "System.IO.IOException");
                string src = c.Str("Source"), ln = c.Str("LogName");
                var owner = m.EventLogs.FirstOrDefault(l => l.Classic && l.Sources.Contains(src, StringComparer.OrdinalIgnoreCase));
                if (owner == null) { c.Error("The source name \"" + src + "\" does not exist on computer \"localhost\".", "Microsoft.PowerShell.Commands.WriteEventLogCommand", "InvalidOperation", null, "System.Exception"); return; }
                var log = m.EventLogs.FirstOrDefault(l => l.Classic && l.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                if (log == null) { c.Error("The Log name \"" + ln + "\" does not exist in the computer \"localhost\".", "Microsoft.PowerShell.Commands.WriteEventLogCommand", "InvalidOperation", null, "System.Exception"); return; }
                if (owner != log) { c.Error("The source '" + src + "' is not registered in log '" + log.Name + "'. (It is registered in log '" + owner.Name + "'.) \" The Source and Log properties must be matched, or you may set Log to the empty string, and it will automatically be matched to the Source property.", "Microsoft.PowerShell.Commands.WriteEventLogCommand", "InvalidOperation", null, "System.Exception"); return; }
                string et = c.Str("EntryType") ?? "Information";
                int level = et.Equals("Error", StringComparison.OrdinalIgnoreCase) ? 2 : et.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
                var e = m.Event(log.Name, owner.Sources.First(s => s.Equals(src, StringComparison.OrdinalIgnoreCase)), c.Int("EventId", 0), level, c.Str("Message"), null);
                if (e != null)
                {
                    e.Qualifiers = 0; e.Category = c.Int("Category", 1); e.Strings = new[] { c.Str("Message") };
                    if (et.Equals("FailureAudit", StringComparison.OrdinalIgnoreCase)) { e.Level = 0; e.Keywords = "Audit Failure"; }
                    if (et.Equals("SuccessAudit", StringComparison.OrdinalIgnoreCase)) { e.Level = 0; e.Keywords = "Audit Success"; }
                }
            });

            var nl = Add("New-EventLog", Mgmt, "LogName:string@0 ! alias=LN; Source:string[]@1 ! alias=SRC; ComputerName:string[] alias=CN; CategoryResourceFile:string alias=CRF; MessageResourceFile:string alias=MRF; ParameterResourceFile:string alias=PRF", (c, o) =>
            {
                var m = c.M;
                string ln = c.Str("LogName");
                foreach (var src in c.Strs("Source"))
                {
                    var owner = m.EventLogs.FirstOrDefault(l => l.Sources.Contains(src, StringComparer.OrdinalIgnoreCase));
                    if (owner != null) { c.Error("The \"" + src + "\" source is already registered on the \"localhost\" computer.", "Microsoft.PowerShell.Commands.NewEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    var log = m.EventLogs.FirstOrDefault(l => l.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (log == null)
                    {
                        if (m.EventLogs.Any(l => l.Classic && l.Name.Length >= 8 && ln.Length >= 8 && l.Name.Substring(0, 8).Equals(ln.Substring(0, 8), StringComparison.OrdinalIgnoreCase)))
                        { c.Error("Only the first eight characters of a custom log name are significant, and there is already another log on the system using the first eight characters of the name given. Name given: '" + ln + "', name of existing log: '" + m.EventLogs.First(l => l.Classic && l.Name.Length >= 8 && l.Name.Substring(0, 8).Equals(ln.Substring(0, 8), StringComparison.OrdinalIgnoreCase)).Name + "'.", "Microsoft.PowerShell.Commands.NewEventLogCommand", "InvalidOperation", null, "System.ArgumentException"); return; }
                        log = new WinLog { Name = ln, MaxKB = 512, Overflow = "OverwriteOlder", Retention = 7 };
                        m.EventLogs.Add(log);
                        log.Sources.Add(ln);
                    }
                    if (!log.Classic) { c.Error("Cert Lab: sources can be added only to classic logs such as Application.", "Microsoft.PowerShell.Commands.NewEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    log.Sources.Add(src);
                    m.HKLM.Sub("SYSTEM\\CurrentControlSet\\Services\\EventLog\\" + log.Name + "\\" + src).Set("EventMessageFile", "C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\EventLogMessages.dll", "ExpandString");
                }
                m.AdminChanged("eventlog:" + ln);
            });

            var rl = Add("Remove-EventLog", Mgmt, "LogName:string[]@0 ! alias=LN ps=Default; Source:string[] ! alias=SRC ps=Source; ComputerName:string[] alias=CN", (c, o) =>
            {
                var m = c.M;
                if (c.Has("Source"))
                {
                    foreach (var src in c.Strs("Source"))
                    {
                        var owner = m.EventLogs.FirstOrDefault(l => l.Classic && l.Sources.Contains(src, StringComparer.OrdinalIgnoreCase));
                        if (owner == null) { c.Error("The source '" + src + "' is not registered on machine 'localhost', or you do not have write access to the registry.", "Microsoft.PowerShell.Commands.RemoveEventLogCommand", "InvalidOperation", null, "System.ArgumentException"); continue; }
                        if (!c.ShouldProcess(src, "Remove-EventLog")) continue;
                        owner.Sources.RemoveAll(s => s.Equals(src, StringComparison.OrdinalIgnoreCase));
                    }
                    return;
                }
                foreach (var ln in c.Strs("LogName"))
                {
                    var log = m.EventLogs.FirstOrDefault(l => l.Classic && l.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (log == null) { c.Error("The event log '" + ln + "' on computer '.' does not exist.", "Microsoft.PowerShell.Commands.RemoveEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    if (new[] { "Application", "System", "Security", "Windows PowerShell", "HardwareEvents", "Internet Explorer", "Key Management Service" }.Contains(log.Name)) { c.Error("Cert Lab: Windows' own log " + log.Name + " cannot be removed on the practice machine.", "Microsoft.PowerShell.Commands.RemoveEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    if (!c.ShouldProcess(ln, "Remove-EventLog")) continue;
                    m.EventLogs.Remove(log);
                }
            });
            rl.ShouldProcess = true; rl.DefaultSet = "Default";

            var cl = Add("Clear-EventLog", Mgmt, "LogName:string[]@0 ! alias=LN; ComputerName:string[] alias=CN", (c, o) =>
            {
                var m = c.M;
                foreach (var ln in c.Strs("LogName"))
                {
                    var log = m.EventLogs.FirstOrDefault(l => l.Classic && l.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (log == null) { c.Error("The event log '" + ln + "' on computer '.' does not exist.", "Microsoft.PowerShell.Commands.ClearEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    if (!c.ShouldProcess(log.Name, "Clear-EventLog")) continue;
                    log.Events.Clear();
                    if (log.Name == "Security") m.Event("Security", "Microsoft-Windows-Eventlog", 1102, 4, "The audit log was cleared.\r\nSubject:\r\n\tSecurity ID:\t" + m.DomainShort + "\\Administrator\r\n\tAccount Name:\tAdministrator\r\n\tDomain Name:\t" + m.DomainShort + "\r\n\tLogon ID:\t0x3E7", m.AdminUserSid);
                    else m.Event("System", "Microsoft-Windows-Eventlog", 104, 4, "The " + log.Name + " log file was cleared.", m.AdminUserSid);
                }
            });
            cl.ShouldProcess = true;

            var lim = Add("Limit-EventLog", Mgmt, "LogName:string[]@0 ! alias=LN; ComputerName:string[] alias=CN; RetentionDays:int alias=MRD range=1~365; OverflowAction:string alias=OFA set=OverwriteOlder|OverwriteAsNeeded|DoNotOverwrite; MaximumSize:long alias=MSKB", (c, o) =>
            {
                var m = c.M;
                foreach (var ln in c.Strs("LogName"))
                {
                    var log = m.EventLogs.FirstOrDefault(l => l.Classic && l.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (log == null) { c.Error("The event log '" + ln + "' on computer '.' does not exist.", "Microsoft.PowerShell.Commands.LimitEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; }
                    if (c.Has("MaximumSize"))
                    {
                        long kb = c.Long("MaximumSize", 0) / 1024;
                        if (kb < 64 || kb > 4194240 || kb % 64 != 0) { c.Error("The value supplied for MaximumSize parameter has to be in the range of 64 KB to 4GB with an increment of 64 KB. Please enter a proper value and then retry.", "ValueOutofRange", "InvalidArgument", null, "System.Exception"); continue; }
                    }
                    if (!c.ShouldProcess(log.Name, "Limit-EventLog")) continue;
                    if (c.Has("MaximumSize")) log.MaxKB = c.Long("MaximumSize", 0) / 1024;
                    if (c.Has("OverflowAction")) { log.Overflow = c.Str("OverflowAction"); if (log.Overflow == "OverwriteOlder" && log.Retention == 0) log.Retention = 7; if (log.Overflow != "OverwriteOlder") log.Retention = log.Overflow == "DoNotOverwrite" ? -1 : 0; }
                    if (c.Has("RetentionDays")) { if (log.Overflow != "OverwriteOlder") { c.Error("RetentionDays cannot be set unless the OverflowAction is OverwriteOlder.", "Microsoft.PowerShell.Commands.LimitEventLogCommand", "InvalidOperation", null, "System.InvalidOperationException"); continue; } log.Retention = c.Int("RetentionDays", 7); }
                }
            });
            lim.ShouldProcess = true;
        }
    }
}
