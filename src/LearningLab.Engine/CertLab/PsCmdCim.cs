// Cert Lab - Windows administration cmdlets, part 5: CIM and WMI. Get-CimInstance (-ClassName, -Filter, -Query,
// -Property, -ComputerName, -CimSession, -Namespace), Get-CimClass, Invoke-CimMethod, New-, Get-, Remove-CimSession,
// and Windows PowerShell 5.1's Get-WmiObject / Invoke-WmiMethod (gone in PowerShell 7).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string CimEx = "Microsoft.Management.Infrastructure.CimException";
        const string CimCls = "Microsoft.Management.Infrastructure.CimCmdlets.";

        static string CimTypeOf(object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "string";
            if (v is DateTime) return "CimInstance#DateTime";
            if (v is bool) return "bool"; if (v is string) return "string"; if (v is ulong) return "uint64"; if (v is long) return "uint32"; if (v is ushort) return "uint16"; if (v is byte) return "byte"; if (v is short) return "int16";
            if (v is object[]) return "string[]";
            return PsTypes.TypeNameOf(v);
        }
        static object Narrow(object v)
        {
            if (v is long && (long)v >= int.MinValue && (long)v <= uint.MaxValue) return (long)v <= int.MaxValue ? (object)(uint)(long)v : (uint)(long)v;
            return v;
        }
        public static PsObj CimInstanceObj(PsMachine m, CimClassInfo ci, CimRow r, string computer, string[] only)
        {
            if (ci.Name == "MSFT_ScheduledTask") return TaskObj(m, (WinTask)r.Base, computer);
            var o = CimObj(ci.Namespace, ci.Name, ci.Parents, computer);
            o.Base = r;
            foreach (var kv in r.Props)
            {
                bool keep = only == null || only.Any(p => p == "*" || p.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) || r.Keys.Contains(kv.Key);
                o.Val(kv.Key, CimTypeOf(kv.Value), keep ? Narrow(kv.Value) : null);
            }
            if (ci.Name == "Win32_QuickFixEngineering") { o.Alias("Source", "CSName"); }
            if (ci.Name == "Win32_Process") { }
            CimEnd(o, ci.Namespace, ci.Name, computer);
            o.DefaultDisplay = ci.View;
            string keys = string.Join(", ", r.Keys.Where(k => r.Get(k) != null).Select(k => k + " = \"" + PsConv.ToStr(r.Get(k)) + "\"").ToArray());
            o.ToStr = x => r.Keys.Length == 0 || ci.Name == "Win32_OperatingSystem" || ci.Name == "Win32_ComputerSystem" && false ? ci.Name + (keys.Length > 0 ? " (" + keys + ")" : "") : ci.Name + " (" + keys + ")";
            return o;
        }

        public static PsObj WmiObj(PsMachine m, CimClassInfo ci, CimRow r, string computer, string[] only, PsRunspace R)
        {
            string ns = "root\\" + PsCim.Norm(ci.Namespace).Substring(5).Replace('/', '\\');
            var o = new PsObj();
            o.TypeNames.Add("System.Management.ManagementObject#" + ns + "\\" + ci.Name);
            foreach (var p in ci.Parents) o.TypeNames.Add("System.Management.ManagementObject#" + ns + "\\" + p);
            o.TypeNames.AddRange(new[] { "System.Management.ManagementObject", "System.Management.ManagementBaseObject", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object" });
            o.Base = r;
            string server = computer ?? m.Name;
            string keys = string.Join(",", r.Keys.Where(k => r.Get(k) != null).Select(k => k + "=\"" + PsConv.ToStr(r.Get(k)).Replace("\\", "\\\\") + "\"").ToArray());
            string relpath = ci.Name + (r.Keys.Length == 0 ? "=@" : "." + keys);
            if (ci.Name == "Win32_OperatingSystem") relpath = "Win32_OperatingSystem=@";
            var props = r.Props.Where(kv => only == null || only.Any(p => p == "*" || p.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))).ToList();
            o.Val("__GENUS", "int", only == null ? 2 : 2);
            o.Val("__CLASS", "string", ci.Name);
            o.Val("__SUPERCLASS", "string", only == null ? (ci.Parents.Length > 0 ? ci.Parents[0] : null) : null);
            o.Val("__DYNASTY", "string", only == null ? (ci.Parents.Length > 0 ? ci.Parents.Last() : ci.Name) : null);
            o.Val("__RELPATH", "string", only == null ? relpath : null);
            o.Val("__PROPERTY_COUNT", "int", props.Count);
            o.Val("__DERIVATION", "string[]", only == null ? ci.Parents.Select(p => (object)p).ToArray() : new object[0]);
            o.Val("__SERVER", "string", only == null ? server : null);
            o.Val("__NAMESPACE", "string", only == null ? ns : null);
            o.Val("__PATH", "string", only == null ? "\\\\" + server + "\\" + ns + ":" + relpath : null);
            foreach (var kv in props)
            {
                var v = Narrow(kv.Value);
                if (v is DateTime) v = PsCim.Dmtf((DateTime)v, m.UtcOffsetHours);
                if (ci.Name == "Win32_QuickFixEngineering" && kv.Key == "InstalledOn") v = ((DateTime)kv.Value).ToString("M/d/yyyy", CultureInfo.InvariantCulture);
                o.Val(kv.Key, CimTypeOf(v), v);
            }
            o.Val("PSComputerName", "string", server);
            o.Alias("Path", "__PATH");
            if (ci.Name == "Win32_QuickFixEngineering") o.Alias("Source", "CSName");
            if (ci.Name == "Win32_OperatingSystem" || ci.Name == "Win32_Process")
            {
                o.Method("ConvertToDateTime", "System.Object ConvertToDateTime(System.Object Object)", (self, a) => FromDmtf(PsConv.ToStr(a.Length > 0 ? a[0] : null)));
                o.Method("ConvertFromDateTime", "System.Object ConvertFromDateTime(System.Object Object)", (self, a) => PsCim.Dmtf((DateTime)PsConv.To(a[0], "datetime"), m.UtcOffsetHours));
            }
            o.DefaultDisplay = WmiView(ci);
            o.ToStr = x => "\\\\" + server + "\\" + ns + ":" + relpath;
            foreach (var meth in ci.Methods)
            {
                var mn = meth;
                o.Method(mn, "System.Management.ManagementBaseObject " + mn + "()", (self, a) =>
                {
                    var args = PsConv.NewOrdered();
                    if (mn == "ChangeStartMode" && a.Length > 0) args["StartMode"] = a[0];
                    if (mn == "Create" && a.Length > 0) args["CommandLine"] = a[0];
                    if (mn == "Rename" && a.Length > 0) args["Name"] = a[0];
                    var res = CallCimMethod(m, ci, r, mn, args, R);
                    return ParamsObj(res, server);
                });
            }
            return o;
        }
        static object FromDmtf(string s)
        {
            if (s == null || s.Length < 14) return null;
            DateTime d;
            if (DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }
        static string[] WmiView(CimClassInfo ci)
        {
            switch (ci.Name)
            {
                case "Win32_OperatingSystem": return new[] { "SystemDirectory", "Organization", "BuildNumber", "RegisteredUser", "SerialNumber", "Version" };
                case "Win32_ComputerSystem": return new[] { "Domain", "Manufacturer", "Model", "Name", "PrimaryOwnerName", "TotalPhysicalMemory" };
                case "Win32_LogicalDisk": return new[] { "DeviceID", "DriveType", "ProviderName", "FreeSpace", "Size", "VolumeName" };
                case "Win32_Service": return new[] { "ExitCode", "Name", "ProcessId", "StartMode", "State", "Status" };
                case "Win32_Process": return new[] { "ProcessName", "Handles", "VM", "WS", "Path" };
                case "Win32_Processor": return new[] { "Caption", "DeviceID", "Manufacturer", "MaxClockSpeed", "Name", "SocketDesignation" };
                case "Win32_UserAccount": return new[] { "AccountType", "Caption", "Domain", "SID", "FullName", "Name" };
                case "Win32_Group": return new[] { "Caption", "Domain", "Name", "SID" };
                case "Win32_NetworkAdapterConfiguration": return new[] { "DHCPEnabled", "IPAddress", "DefaultIPGateway", "DNSDomain", "ServiceName", "Description", "Index" };
                case "Win32_QuickFixEngineering": return new[] { "Source", "Description", "HotFixID", "InstalledBy", "InstalledOn" };
            }
            return ci.View;
        }
        static PsObj ParamsObj(OrderedDictionary res, string server)
        {
            var o = new PsObj("System.Management.ManagementBaseObject#\\__PARAMETERS", "System.Management.ManagementBaseObject", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            o.Val("__GENUS", "int", 2); o.Val("__CLASS", "string", "__PARAMETERS"); o.Val("__SUPERCLASS", "string", null); o.Val("__DYNASTY", "string", "__PARAMETERS"); o.Val("__RELPATH", "string", null);
            o.Val("__PROPERTY_COUNT", "int", res.Count); o.Val("__DERIVATION", "string[]", new object[0]); o.Val("__SERVER", "string", null); o.Val("__NAMESPACE", "string", null); o.Val("__PATH", "string", null);
            foreach (DictionaryEntry kv in res) o.Val(PsConv.ToStr(kv.Key), CimTypeOf(kv.Value), kv.Value);
            o.Val("PSComputerName", "string", null);
            return o;
        }

        /// <summary>Runs a CIM method: Win32_Process Create / Terminate / GetOwner, Win32_Service Start / Stop / Pause / Resume / ChangeStartMode / Delete, Win32_OperatingSystem Reboot / Shutdown.</summary>
        static OrderedDictionary CallCimMethod(PsMachine m, CimClassInfo ci, CimRow r, string method, IDictionary args, PsRunspace R)
        {
            var res = PsConv.NewOrdered();
            Func<string, object> arg = n => { if (args == null) return null; foreach (DictionaryEntry kv in args) if (PsConv.ToStr(kv.Key).Equals(n, StringComparison.OrdinalIgnoreCase)) return PsConv.Base(kv.Value); return null; };
            string mn = method.ToLowerInvariant();
            uint rv = 0;
            if (ci.Name == "Win32_Process")
            {
                if (mn == "create")
                {
                    string cmd = PsConv.ToStr(arg("CommandLine") ?? "").Trim();
                    var words = PsMachine.SplitArgs(cmd);
                    string exe = words.Count == 0 ? "" : words[0];
                    string bare = WinPath.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                    if (bare.Length == 0) { rv = 9; }
                    else
                    {
                        string path = exe.Contains("\\") ? exe : (bare == "notepad" ? "C:\\Windows\\system32\\notepad.exe" : "C:\\Windows\\system32\\" + bare + ".exe");
                        var known = bare == "notepad" || bare == "calc" || bare == "mspaint" || bare == "cmd" || bare == "powershell" || PsNatives.Get(bare) != null || m.FindFile(path) != null;
                        if (!known) rv = 9;
                        else
                        {
                            var p = m.NewProcess(bare, path, 0, m.DomainShort + "\\Administrator", m.LiveProcesses().Where(x => x.Name == "WmiPrvSE").Select(x => x.Id).FirstOrDefault());
                            p.CommandLine = cmd; p.Ws = 15700000; p.Pm = 3200000; p.Handles = 220; p.Description = bare == "notepad" ? "Notepad" : bare;
                            if (bare == "powershell") { int code = m.RunPowerShellCommandLine(string.Join(" ", words.Skip(1).Select(w => w.Contains(" ") ? "\"" + w + "\"" : w).ToArray()), "Administrator", null, true); m.EndProcess(p, code); }
                            else if (bare == "cmd") m.EndProcess(p, 0);
                            res["ProcessId"] = (uint)p.Id;
                        }
                    }
                    if (rv != 0) res["ProcessId"] = null;
                }
                else if (mn == "terminate")
                {
                    var p = r == null ? null : r.Base as WinProcess;
                    if (p == null) rv = 2; else if (p.Protected || p.Id <= 4) rv = 2; else m.EndProcess(p, PsConv.ToInt(arg("Reason") ?? 0));
                }
                else if (mn == "getowner")
                {
                    var p = r == null ? null : r.Base as WinProcess;
                    if (p == null || p.Id <= 4) { rv = 2; }
                    else { int bs = p.User.IndexOf('\\'); res["Domain"] = bs > 0 ? p.User.Substring(0, bs) : ""; res["User"] = bs > 0 ? p.User.Substring(bs + 1) : p.User; }
                }
                else if (mn == "getownersid") { var p = r == null ? null : r.Base as WinProcess; res["Sid"] = p == null ? null : (p.User.StartsWith("NT AUTHORITY\\SYSTEM", StringComparison.Ordinal) ? "S-1-5-18" : m.AdminUserSid); }
                else rv = 21;
            }
            else if (ci.Name == "Win32_Service")
            {
                var s = r == null ? null : r.Base as WinService;
                if (s == null) rv = 6;
                else switch (mn)
                {
                    case "startservice": rv = s.Running ? 10u : (s.StartType == "Disabled" ? 14u : (m.StartService(s, true) == null ? 0u : 8u)); break;
                    case "stopservice": if (!s.Running) rv = 5; else { var w = m.StopService(s, false, true); rv = w == null ? 0u : w == "1051" ? 3u : 5u; } break;
                    case "pauseservice": if (!s.CanPause) rv = 1; else if (!s.Running) rv = 7; else { s.Status = "Paused"; m.ServiceEvent(s, "Paused"); } break;
                    case "resumeservice": if (s.Status != "Paused") rv = 6; else { s.Status = "Running"; m.ServiceEvent(s, "Running"); } break;
                    case "changestartmode":
                        string mode = PsConv.ToStr(arg("StartMode") ?? "");
                        string st = mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) || mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "Automatic" : mode.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : null;
                        if (st == null) rv = 21; else m.SetStartType(s, st, true);
                        break;
                    case "delete": if (s.Running) rv = 16; else { s.Deleted = true; } break;
                    default: rv = 21; break;
                }
                if (s != null) m.AdminChanged("service:" + s.Name);
            }
            else if (ci.Name == "Win32_OperatingSystem" && (mn == "reboot" || mn == "shutdown" || mn == "win32shutdown"))
            {
                res["ReturnValue"] = 0u;
                if (mn == "shutdown") m.ShutdownKind = "power off";
                m.Reboot();
                return res;
            }
            else throw new PsMethodError("Method not found", "Microsoft.Management.Infrastructure.CimException");
            res["ReturnValue"] = rv;
            return res;
        }

        static PsErrorRecord CimFail(CmdCtx c, string msg, string hresult, string cat, string target)
        {
            var r = c.MakeError(msg, "HRESULT " + hresult, cat, target, CimEx);
            return r;
        }
        /// <summary>The machines a CIM command reaches: -CimSession, -ComputerName (over WinRM), or this one. Errors for unreachable names.</summary>
        static List<KeyValuePair<PsMachine, string>> CimTargets(CmdCtx c)
        {
            var l = new List<KeyValuePair<PsMachine, string>>();
            if (c.Has("CimSession"))
            {
                foreach (var x in c.Objs("CimSession")) { var s = PsConv.Base(x) as PsObj; var sm = s == null ? null : s.Base as PsMachine; if (sm != null) l.Add(new KeyValuePair<PsMachine, string>(sm, sm == c.M && PsConv.ToStr(s.Get("ComputerName")).Equals("localhost", StringComparison.OrdinalIgnoreCase) ? null : PsConv.ToStr(s.Get("ComputerName")))); }
                return l;
            }
            foreach (var cn in c.Strs("ComputerName") ?? new string[] { null })
            {
                var m = RemoteMachine(c, cn);
                if (cn != null && (cn == "." || cn.Equals("localhost", StringComparison.OrdinalIgnoreCase))) { l.Add(new KeyValuePair<PsMachine, string>(c.M, null)); continue; }
                string why = CimReach(c, m, cn);
                if (why != null) continue;
                l.Add(new KeyValuePair<PsMachine, string>(m, cn == null ? null : cn));
            }
            return l;
        }
        static string CimReach(CmdCtx c, PsMachine m, string cn)
        {
            if (cn == null) return null;
            if (m == null)
            {
                c.WriteError(CimFail(c, "WinRM cannot process the request. The following error with errorcode 0x80090311 occurred while using Kerberos authentication: We can't sign you in with this credential because your domain isn't available. Make sure your device is connected to your organization's network and try again. If you previously signed in on this device with another credential, you can sign in with that credential. \r\n Possible causes are:\r\n  -The user name or password specified are invalid.\r\n  -Kerberos is used when no authentication method and no user name are specified.\r\n  -Kerberos accepts domain user names, but not local user names.\r\n  -The Service Principal Name (SPN) for the remote computer name and port does not exist.\r\n  -The client and remote computers are in different domains and there is no trust between the two domains.\r\n After checking for the above issues, try the following:\r\n  -Check the Event Viewer for events related to authentication.\r\n  -Change the authentication method; add the destination computer to the WinRM TrustedHosts configuration setting or use HTTPS transport.\r\n Note that computers in the TrustedHosts list might not be authenticated.\r\n   -For more information about WinRM configuration, run the following command: winrm help config.", "0x80090311", "AuthenticationError", cn));
                return "x";
            }
            if (m != c.M && !m.WinRmReady)
            {
                c.WriteError(CimFail(c, "WinRM cannot complete the operation. Verify that the specified computer name is valid, that the computer is accessible over the network, and that a firewall exception for the WinRM service is enabled and allows access from this computer. By default, the WinRM firewall exception for public profiles limits access to remote computers within the same local subnet. ", "0x80338126", "ConnectionError", cn));
                return "x";
            }
            return null;
        }

        static void RegCim()
        {
            Func<string, string, Action<CmdCtx, object>, CmdletDef> add = (name, spec, f) => { var d = Add(name, Cim, spec, f); d.FqClass = CimCls + name.Replace("-", "") + "Command"; return d; };
            var gci = add("Get-CimInstance", "ClassName:string@0 ! alias=Class ps=ClassNameComputerSet,ClassNameSessionSet; Query:string ! ps=QueryComputerSet,QuerySessionSet; InputObject:CimInstance ! pipe alias=CimInstance ps=CimInstanceComputerSet,CimInstanceSessionSet; Filter:string ps=ClassNameComputerSet,ClassNameSessionSet; Property:string[] alias=SelectProperties ps=ClassNameComputerSet,ClassNameSessionSet; Namespace:string; ComputerName:string[] alias=CN,ServerName ps=ClassNameComputerSet,QueryComputerSet,CimInstanceComputerSet; CimSession:CimSession[] ! ps=ClassNameSessionSet,QuerySessionSet,CimInstanceSessionSet; KeyOnly:switch; Shallow:switch; QueryDialect:string; OperationTimeoutSec:uint32 alias=OT", (c, o) =>
            {
                string ns = c.Str("Namespace") ?? "root/cimv2";
                if (c.Has("InputObject"))
                {
                    var x = c.Get("InputObject") as PsObj; var row = x == null ? null : x.Base as CimRow;
                    if (row == null) { if (x != null) c.Out(x); return; }
                    var ci0 = PsCim.Find(ns, row.Class) ?? PsCim.All.First(k => k.Name == row.Class);
                    var fresh = ci0.Rows(c.M).FirstOrDefault(rr => rr.Keys.All(k => PsConv.Eq(rr.Get(k), row.Get(k), false)));
                    if (fresh != null) c.Out(CimInstanceObj(c.M, ci0, fresh, x.Computer, null));
                    return;
                }
                string cls = c.Str("ClassName"); string[] props = c.Strs("Property"); string where = c.Str("Filter");
                if (c.Has("Query"))
                {
                    if (!PsCim.ParseQuery(c.Str("Query"), out cls, out props, out where)) { c.WriteError(CimFail(c, "Invalid query ", "0x80041017", "InvalidArgument", null)); return; }
                }
                if (!PsCim.KnownNamespace(ns)) { c.WriteError(CimFail(c, "Invalid namespace ", "0x8004100e", "MetadataError", PsCim.Norm(ns))); return; }
                var ci = PsCim.Find(ns, cls);
                if (ci == null) { c.WriteError(CimFail(c, "Invalid class ", "0x80041010", "MetadataError", PsCim.Norm(ns) + ":" + cls)); return; }
                Func<CimRow, bool> test = r => true;
                if (where != null) { try { test = PsCim.Where(where); } catch (FormatException) { c.WriteError(CimFail(c, "Invalid query ", "0x80041017", "InvalidArgument", null)); return; } }
                foreach (var t in CimTargets(c))
                    foreach (var r in ci.Rows(t.Key).Where(test))
                        c.Out(CimInstanceObj(t.Key, ci, r, t.Value, props));
            });
            gci.DefaultSet = "ClassNameComputerSet";

            var gcc = add("Get-CimClass", "ClassName:string@0; Namespace:string@1; MethodName:string; PropertyName:string; QualifierName:string; ComputerName:string[] alias=CN,ServerName; CimSession:CimSession[]", (c, o) =>
            {
                string ns = c.Str("Namespace") ?? "root/cimv2";
                if (!PsCim.KnownNamespace(ns)) { c.WriteError(CimFail(c, "Invalid namespace ", "0x8004100e", "MetadataError", PsCim.Norm(ns))); return; }
                string pat = c.Str("ClassName") ?? "*";
                var l = PsCim.All.Where(k => PsCim.NsEq(k.Namespace, ns) && PsConv.Like(k.Name, pat, false)).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (l.Count == 0 && !PsConv.HasWildcard(pat)) { c.WriteError(CimFail(c, "Not found ", "0x80041002", "ObjectNotFound", PsCim.Norm(ns) + ":" + pat)); return; }
                foreach (var k in l)
                {
                    var sample = k.Name == "MSFT_ScheduledTask" ? new List<CimRow>() : k.Rows(c.M);
                    var pnames = sample.Count > 0 ? sample[0].Props.Select(kv => kv.Key).ToList() : new List<string> { "Actions", "Author", "Date", "Description", "State", "TaskName", "TaskPath", "Triggers" };
                    if (c.Has("MethodName") && !k.Methods.Any(mm => PsConv.Like(mm, c.Str("MethodName"), false))) continue;
                    if (c.Has("PropertyName") && !pnames.Any(p => PsConv.Like(p, c.Str("PropertyName"), false))) continue;
                    var co = new PsObj("Microsoft.Management.Infrastructure.CimClass", "System.Object");
                    co.Val("CimSuperClassName", "string", k.Parents.Length > 0 ? k.Parents[0] : null);
                    co.Val("CimSuperClass", "Microsoft.Management.Infrastructure.CimClass", k.Parents.Length > 0 ? PsCim.Norm(k.Namespace) + ":" + k.Parents[0] : null);
                    co.Val("CimClassProperties", "Microsoft.Management.Infrastructure.Internal.Data.CimClassPropertiesCollection", pnames.Select(p => (object)p).ToArray());
                    co.Val("CimClassQualifiers", "Microsoft.Management.Infrastructure.Internal.Data.CimClassQualifierCollection", new object[] { "dynamic", "Locale", "provider", "UUID" });
                    co.Val("CimClassMethods", "Microsoft.Management.Infrastructure.Internal.Data.CimMethodDeclarationCollection", k.Methods.Select(mm => (object)mm).ToArray());
                    var sys = new PsObj("Microsoft.Management.Infrastructure.CimSystemProperties", "System.Object"); sys.Val("Namespace", "string", PsCim.Norm(k.Namespace)); sys.Val("ServerName", "string", c.M.Name); sys.Val("ClassName", "string", k.Name); sys.Val("Path", "string", null);
                    co.Val("CimSystemProperties", "Microsoft.Management.Infrastructure.CimSystemProperties", sys);
                    co.Val("CimClassName", "string", k.Name);
                    co.Val("Namespace", "string", PsCim.Norm(k.Namespace).Replace("root/", "ROOT/"));
                    co.ToStr = x => PsCim.Norm(k.Namespace) + ":" + k.Name;
                    c.Out(co);
                }
            });

            var icm = add("Invoke-CimMethod", "ClassName:string@0 ! alias=Class ps=ClassNameComputerSet,ClassNameSessionSet; InputObject:CimInstance ! pipe alias=CimInstance ps=CimInstanceComputerSet,CimInstanceSessionSet; Query:string ! ps=QueryComputerSet,QuerySessionSet; MethodName:string@1 ! alias=Name; Arguments:hashtable@2; Namespace:string; ComputerName:string[] alias=CN,ServerName; CimSession:CimSession[]; OperationTimeoutSec:uint32 alias=OT", (c, o) =>
            {
                string ns = c.Str("Namespace") ?? "root/cimv2";
                var targets = new List<KeyValuePair<CimRow, KeyValuePair<PsMachine, string>>>();
                CimClassInfo ci = null;
                if (c.Has("InputObject"))
                {
                    var x = c.Get("InputObject") as PsObj; var row = x == null ? null : x.Base as CimRow;
                    if (row == null) { c.WriteError(CimFail(c, "Not found ", "0x80041002", "ObjectNotFound", null)); return; }
                    ci = PsCim.All.First(k => k.Name == row.Class);
                    var m = x.Computer == null ? c.M : (c.M.Net.Get(x.Computer) ?? c.M);
                    var fresh = ci.Rows(m).FirstOrDefault(rr => rr.Keys.All(k => PsConv.Eq(rr.Get(k), row.Get(k), false)));
                    if (fresh == null) { c.WriteError(CimFail(c, "Not found ", "0x80041002", "ObjectNotFound", null)); return; }
                    targets.Add(new KeyValuePair<CimRow, KeyValuePair<PsMachine, string>>(fresh, new KeyValuePair<PsMachine, string>(m, x.Computer)));
                }
                else
                {
                    string cls = c.Str("ClassName"), where = null; string[] props;
                    if (c.Has("Query") && !PsCim.ParseQuery(c.Str("Query"), out cls, out props, out where)) { c.WriteError(CimFail(c, "Invalid query ", "0x80041017", "InvalidArgument", null)); return; }
                    ci = PsCim.Find(ns, cls);
                    if (ci == null) { c.WriteError(CimFail(c, "Invalid class ", "0x80041010", "MetadataError", PsCim.Norm(ns) + ":" + cls)); return; }
                    foreach (var t in CimTargets(c))
                    {
                        if (c.Has("Query")) { Func<CimRow, bool> test; try { test = where == null ? (Func<CimRow, bool>)(r => true) : PsCim.Where(where); } catch (FormatException) { c.WriteError(CimFail(c, "Invalid query ", "0x80041017", "InvalidArgument", null)); return; } foreach (var r in ci.Rows(t.Key).Where(test)) targets.Add(new KeyValuePair<CimRow, KeyValuePair<PsMachine, string>>(r, t)); }
                        else targets.Add(new KeyValuePair<CimRow, KeyValuePair<PsMachine, string>>(null, t));
                    }
                }
                string mn = c.Str("MethodName");
                if (!ci.Methods.Any(x => x.Equals(mn, StringComparison.OrdinalIgnoreCase))) { c.WriteError(CimFail(c, "Method not found ", "0x80041055", "MetadataError", PsCim.Norm(ci.Namespace) + ":" + ci.Name)); return; }
                bool isStatic = mn.Equals("Create", StringComparison.OrdinalIgnoreCase);
                foreach (var t in targets)
                {
                    if (t.Key == null && !isStatic) { c.WriteError(CimFail(c, "Invalid method Parameter(s) ", "0x80041008", "InvalidArgument", null)); continue; }
                    string label = ci.Name + (t.Key == null ? "" : " (" + string.Join(", ", t.Key.Keys.Select(k => k + " = \"" + PsConv.ToStr(t.Key.Get(k)) + "\"").ToArray()) + ")");
                    if (!c.ShouldProcess(label, mn)) continue;
                    var res = CallCimMethod(t.Value.Key, ci, t.Key, mn, c.Hash("Arguments"), c.R);
                    var ro = new PsObj("Microsoft.Management.Infrastructure.CimMethodResult", "System.Object");
                    foreach (DictionaryEntry kv in res) ro.Val(PsConv.ToStr(kv.Key), CimTypeOf(kv.Value), kv.Value);
                    ro.Val("PSComputerName", "string", t.Value.Value);
                    ro.DefaultDisplay = res.Keys.Cast<object>().Select(k => PsConv.ToStr(k)).Concat(new[] { "PSComputerName" }).ToArray();
                    c.Out(ro);
                }
            });
            icm.ShouldProcess = true; icm.DefaultSet = "ClassNameComputerSet";

            add("New-CimSession", "ComputerName:string[]@0 alias=CN,ServerName pipe prop; Credential:pscredential; Authentication:string; Name:string; Port:uint32; SessionOption:object; SkipTestConnection:switch; OperationTimeoutSec:uint32 alias=OT", (c, o) =>
            {
                foreach (var cn in c.Strs("ComputerName") ?? new[] { "localhost" })
                {
                    var m = RemoteMachine(c, cn);
                    bool local = cn.Equals("localhost", StringComparison.OrdinalIgnoreCase) || cn == "." || m == c.M;
                    if (!local && CimReach(c, m, cn) != null) continue;
                    c.R.CimSessionCount++;
                    var s = new PsObj("Microsoft.Management.Infrastructure.CimSession", "System.Object");
                    s.Base = m;
                    s.Val("Id", "uint32", (uint)c.R.CimSessionCount);
                    s.Val("Name", "string", c.Str("Name") ?? "CimSession" + c.R.CimSessionCount);
                    s.Val("InstanceId", "guid", c.M.NewGuid());
                    s.Val("ComputerName", "string", cn);
                    s.Val("Protocol", "string", "WSMAN");
                    s.ToStr = x => "CimSession: " + cn;
                    s.DefaultDisplay = new[] { "Id", "Name", "InstanceId", "ComputerName", "Protocol" };
                    c.R.CimSessions.Add(s);
                    c.Out(s);
                }
            });
            add("Get-CimSession", "ComputerName:string[]@0 alias=CN,ServerName ps=ComputerNameSet; Id:uint32[] ! ps=SessionIdSet; InstanceId:guid[] ! ps=InstanceIdSet; Name:string[] ! ps=NameSet", (c, o) =>
            {
                foreach (var s in c.R.CimSessions.ToList())
                {
                    if (c.Has("ComputerName") && !c.Strs("ComputerName").Any(n => PsConv.Like(PsConv.ToStr(s.Get("ComputerName")), n, false))) continue;
                    if (c.Has("Id") && !c.Objs("Id").Any(i => PsConv.ToLong(i) == PsConv.ToLong(s.Get("Id")))) continue;
                    if (c.Has("Name") && !c.Strs("Name").Any(n => PsConv.Like(PsConv.ToStr(s.Get("Name")), n, false))) continue;
                    c.Out(s);
                }
            }).DefaultSet = "ComputerNameSet";
            var rcs = add("Remove-CimSession", "CimSession:CimSession[]@0 ! pipe ps=CimSessionSet; ComputerName:string[] ! alias=CN,ServerName ps=ComputerNameSet; Id:uint32[] ! ps=SessionIdSet; Name:string[] ! ps=NameSet; InstanceId:guid[] ! ps=InstanceIdSet", (c, o) =>
            {
                var l = c.Has("CimSession") ? c.Objs("CimSession").Select(x => PsConv.Base(x) as PsObj).ToList()
                    : c.R.CimSessions.Where(s => c.Has("ComputerName") && c.Strs("ComputerName").Any(n => PsConv.Like(PsConv.ToStr(s.Get("ComputerName")), n, false)) || c.Has("Id") && c.Objs("Id").Any(i => PsConv.ToLong(i) == PsConv.ToLong(s.Get("Id"))) || c.Has("Name") && c.Strs("Name").Any(n => PsConv.Like(PsConv.ToStr(s.Get("Name")), n, false))).ToList();
                foreach (var s in l) { if (s == null) continue; if (!c.ShouldProcess(PsConv.ToStr(s.Get("Name")), "Remove-CimSession")) continue; c.R.CimSessions.Remove(s); }
            });
            rcs.ShouldProcess = true; rcs.DefaultSet = "CimSessionSet";

            // ---------------- WMI (Windows PowerShell 5.1 only)
            var gw = Add("Get-WmiObject", Mgmt, "Class:string@0 ! alias=ClassName ps=query,list; Property:string[]@1 ps=query; Filter:string ps=query; Query:string ! ps=WQLQuery; List:switch ! ps=list; Recurse:switch ps=list; Namespace:string alias=NS; ComputerName:string[] alias=Cn; Credential:pscredential; Amended:switch; DirectRead:switch; AsJob:switch; Impersonation:string; Authentication:string", (c, o) =>
            {
                string ns = c.Str("Namespace") ?? "root\\cimv2";
                if (!PsCim.KnownNamespace(ns)) throw c.Fail("Invalid namespace \"" + ns + "\"", "GetWMIManagementException", "InvalidArgument", null, "System.Management.ManagementException");
                if (c.Sw("List"))
                {
                    foreach (var k in PsCim.All.Where(x => PsCim.NsEq(x.Namespace, ns) && (!c.Has("Class") || PsConv.Like(x.Name, c.Str("Class"), false))).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var co = new PsObj("System.Management.ManagementClass#" + ns.Replace('/', '\\') + "\\" + k.Name, "System.Management.ManagementClass", "System.Management.ManagementObject", "System.Object");
                        co.Val("Name", "string", k.Name);
                        co.Val("Methods", "string", "{" + string.Join(", ", k.Methods.Take(3).ToArray()) + (k.Methods.Length > 3 ? "..." : "") + "}");
                        var smp = k.Name == "MSFT_ScheduledTask" ? new List<CimRow>() : k.Rows(c.M);
                        co.Val("Properties", "string", smp.Count == 0 ? "{}" : "{" + string.Join(", ", smp[0].Props.Take(3).Select(kv => kv.Key).ToArray()) + "...}");
                        co.DefaultDisplay = new[] { "Name", "Methods", "Properties" };
                        c.Out(co);
                    }
                    return;
                }
                string cls = c.Str("Class"); string[] props = c.Strs("Property"); string where = c.Str("Filter");
                if (c.Has("Query") && !PsCim.ParseQuery(c.Str("Query"), out cls, out props, out where)) throw c.Fail("Invalid query \"" + c.Str("Query") + "\"", "GetWMIManagementException", "InvalidOperation", null, "System.Management.ManagementException");
                var ci = PsCim.Find(ns, cls);
                if (ci == null || ci.Name == "MSFT_ScheduledTask") throw c.Fail("Invalid class \"" + cls + "\"", "GetWMIManagementException", "InvalidType", null, "System.Management.ManagementException");
                Func<CimRow, bool> test = r => true;
                if (where != null) { try { test = PsCim.Where(where); } catch (FormatException) { throw c.Fail("Invalid query \"select * from " + cls + " where " + where + "\"", "GetWMIManagementException", "InvalidOperation", null, "System.Management.ManagementException"); } }
                foreach (var cn in c.Strs("ComputerName") ?? new string[] { null })
                {
                    var m = RemoteRpc(c, cn);
                    if (m == null) { c.Error("The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)", "GetWMICOMException", "InvalidOperation", null, "System.Runtime.InteropServices.COMException"); continue; }
                    foreach (var r in ci.Rows(m).Where(test)) c.Out(WmiObj(m, ci, r, cn == null || m == c.M ? null : m.Name, props, c.R));
                }
            });
            gw.DefaultSet = "query";
            var iwm = Add("Invoke-WmiMethod", Mgmt, "Class:string@0 ! ps=class; Name:string@1 !; ArgumentList:object[]@2 alias=Args; InputObject:object ! pipe ps=object; Path:string ! ps=path; Namespace:string alias=NS; ComputerName:string[] alias=Cn; Credential:pscredential", (c, o) =>
            {
                CimClassInfo ci; CimRow row = null; PsMachine m = c.M;
                if (c.Has("InputObject")) { var x = c.Get("InputObject") as PsObj; row = x == null ? null : x.Base as CimRow; if (row == null) throw c.Fail("Invalid object", "InvokeWMIManagementException", "InvalidOperation", null, "System.Management.ManagementException"); ci = PsCim.All.First(k => k.Name == row.Class); }
                else { ci = PsCim.Find(c.Str("Namespace") ?? "root/cimv2", c.Str("Class") ?? ""); if (ci == null) throw c.Fail("Invalid class \"" + c.Str("Class") + "\"", "InvokeWMIManagementException", "InvalidOperation", null, "System.Management.ManagementException"); }
                string mn = c.Str("Name");
                if (!ci.Methods.Any(x => x.Equals(mn, StringComparison.OrdinalIgnoreCase))) throw c.Fail("Method not found", "InvokeWMIManagementException", "InvalidOperation", null, "System.Management.ManagementException");
                var args = PsConv.NewOrdered(); var al = c.Objs("ArgumentList");
                if (al.Count > 0) { if (mn.Equals("Create", StringComparison.OrdinalIgnoreCase)) args["CommandLine"] = al[0]; if (mn.Equals("ChangeStartMode", StringComparison.OrdinalIgnoreCase)) args["StartMode"] = al[0]; }
                if (!c.ShouldProcess(ci.Name, mn)) return;
                c.Out(ParamsObj(CallCimMethod(m, ci, row, mn, args, c.R), m.Name));
            });
            iwm.ShouldProcess = true; iwm.DefaultSet = "class";
        }
    }
}
