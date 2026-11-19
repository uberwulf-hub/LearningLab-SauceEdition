// Cert Lab - Windows administration cmdlets, part 6: the computer (Restart-Computer, Stop-Computer, Rename-Computer,
// Get-HotFix, Get-ComputerInfo), Windows Server features (Get-, Install-, Uninstall-WindowsFeature; server only) and
// the table views of the administration objects.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static void RegComputer()
        {
            Func<string, string, CmdletDef> power = (name, kind) =>
            {
                var d = Add(name, Mgmt, "ComputerName:string[]@0 pipe prop alias=CN,__SERVER,Server,IPAddress; Force:switch alias=f; Credential:pscredential; Wait:switch; Timeout:int; For:string; Delay:int; Protocol:string; WsmanAuthentication:string; DcomAuthentication:string; Impersonation:string; AsJob:switch; ThrottleLimit:int", (c, o) =>
                {
                    foreach (var cn in c.Strs("ComputerName") ?? new[] { "localhost" })
                    {
                        var m = RemoteRpc(c, cn);
                        if (m == null) { c.Error("Failed to " + (kind == "restart" ? "restart" : "stop") + " the computer " + cn + " with the following error message: The RPC server is unavailable. ", "Command" + (kind == "restart" ? "Restart" : "Stop") + "ComputerFailed", "OperationStopped", cn, "System.InvalidOperationException"); continue; }
                        if (!c.ShouldProcess(m.Name, name)) continue;
                        bool others = m.Sessions.Count(s => !s.Closed && s.Console != null) > 1 || m != c.M && m.Sessions.Any(s => !s.Closed && s.Console != null);
                        if (others && !c.Sw("Force"))
                        {
                            c.Error("Failed to " + (kind == "restart" ? "restart" : "stop") + " the computer " + m.Name + " with the following error message: The system shutdown cannot be initiated because there are other users logged on to the computer.", "Command" + (kind == "restart" ? "Restart" : "Stop") + "ComputerFailed", "OperationStopped", m.Name, "System.InvalidOperationException");
                            continue;
                        }
                        m.ShutdownKind = kind == "restart" ? "restart" : "power off";
                        if (kind != "restart") c.Host("[Cert Lab: " + m.Name + " was shut down. On the practice network it starts again at once, as if someone pressed its power button.]");
                        if (c.Sw("Wait") && m != c.M) c.Host("");
                        m.Reboot();
                    }
                });
                d.ShouldProcess = true;
                return d;
            };
            power("Restart-Computer", "restart");
            power("Stop-Computer", "stop");

            var rc = Add("Rename-Computer", Mgmt, "NewName:string@0 !; ComputerName:string alias=CN; DomainCredential:pscredential; LocalCredential:pscredential; Force:switch; Restart:switch; PassThru:switch; Protocol:string; WsmanAuthentication:string", (c, o) =>
            {
                var m = RemoteRpc(c, c.Str("ComputerName"));
                string nn = c.Str("NewName");
                if (m == null) { c.Error("Cannot establish the WMI connection to the computer '" + c.Str("ComputerName") + "' with the following error message: The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)", "RenameComputerException", "OperationStopped", c.Str("ComputerName"), "System.InvalidOperationException"); return; }
                string current = m.PendingName ?? m.Name;
                if (nn.Equals(current, StringComparison.OrdinalIgnoreCase)) { c.Error("Skip computer '" + m.Name + "' with new name '" + nn + "' because the new name is the same as the current name.", "NewNameIsOldName", "InvalidArgument", nn, "System.InvalidOperationException"); return; }
                if (nn.Length > 15 || nn.Length == 0 || nn.All(char.IsDigit) || nn.IndexOfAny(" \\/:*?\"<>|.,~!@#$%^&'(){}_".ToCharArray()) >= 0)
                { c.Error("Skip computer '" + m.Name + "' with new name '" + nn + "' because the new name is not valid. The new computer name might contain not valid characters.", "InvalidNewName", "InvalidArgument", nn, "System.InvalidOperationException"); return; }
                if (m.Net.FindHost(nn) != null && m.Net.FindHost(nn).Machine != m) { c.Error("Fail to rename computer '" + m.Name + "' to '" + nn + "' due to the following exception: The account already exists.", "FailToRenameComputer", "OperationStopped", m.Name, "System.InvalidOperationException"); return; }
                if (!c.ShouldProcess(m.Name, "Rename-Computer")) return;
                m.PendingName = nn;
                m.HKLM.Sub("SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName").S("ComputerName", nn);
                if (c.Sw("PassThru"))
                {
                    var r = new PsObj("Microsoft.PowerShell.Commands.RenameComputerChangeInfo", "System.Object");
                    r.Val("HasSucceeded", "bool", true); r.Val("NewComputerName", "string", nn); r.Val("OldComputerName", "string", m.Name);
                    c.Out(r);
                }
                if (c.Sw("Restart")) { m.ShutdownKind = "restart"; m.Reboot(); }
                else c.Warn("The changes will take effect after you restart the computer " + m.Name + ".");
            });
            rc.ShouldProcess = true;

            Add("Get-HotFix", Mgmt, "Id:string[]@0 alias=HFID ps=Default; Description:string[] ps=Description; ComputerName:string[] alias=CN,__Server,IPAddress; Credential:pscredential", (c, o) =>
            {
                var ci = PsCim.Find("root/cimv2", "Win32_QuickFixEngineering");
                foreach (var cn in c.Strs("ComputerName") ?? new string[] { null })
                {
                    var m = RemoteRpc(c, cn);
                    if (m == null) { c.Error("The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)", "GetHotFixNoEntriesFound", "InvalidOperation", null, "System.Runtime.InteropServices.COMException"); continue; }
                    var rows = ci.Rows(m);
                    foreach (var id in c.Strs("Id") ?? new[] { "*" })
                    {
                        var hit = rows.Where(r => PsConv.Like(PsConv.ToStr(r.Get("HotFixID")), id, false) && (!c.Has("Description") || c.Strs("Description").Any(d => PsConv.Like(PsConv.ToStr(r.Get("Description")), d, false)))).ToList();
                        if (hit.Count == 0 && !PsConv.HasWildcard(id)) { c.Error("Cannot find the requested hotfix on the '" + (cn ?? "localhost") + "' computer. Verify the input and run the command again.", "GetHotFixNoEntriesFound", "ObjectNotFound", id, "System.ArgumentException"); continue; }
                        foreach (var r in hit)
                        {
                            var w = WmiObj(m, ci, r, cn == null || m == c.M ? null : m.Name, null, c.R);
                            var on = (DateTime)r.Get("InstalledOn");
                            w.Remove("InstalledOn");
                            w.Val("InstalledOn", "System.Nullable[datetime]", on);
                            w.DefaultDisplay = new[] { "Source", "Description", "HotFixID", "InstalledBy", "InstalledOn" };
                            c.Out(w);
                        }
                    }
                }
            }).DefaultSet = "Default";

            Add("Get-ComputerInfo", Mgmt, "Property:string[]@0", (c, o) =>
            {
                var m = c.M;
                var os = PsCim.Find("root/cimv2", "Win32_OperatingSystem").Rows(m)[0];
                var cs = PsCim.Find("root/cimv2", "Win32_ComputerSystem").Rows(m)[0];
                var x = new PsObj("Microsoft.PowerShell.Commands.ComputerInfo", "System.Object");
                Action<string, string, object> p = (n, t, v) => x.Val(n, t, v);
                p("WindowsBuildLabEx", "string", m.OsBuild + "." + (m.IsServer ? "1.amd64fre.fe_release.210507-1500" : "1.amd64fre.ni_release.220506-1250"));
                p("WindowsCurrentVersion", "string", "6.3"); p("WindowsEditionId", "string", m.OsEdition); p("WindowsInstallationType", "string", m.IsServer ? "Server" : "Client");
                p("WindowsInstallDateFromRegistry", "System.Nullable[datetime]", m.InstallDate.AddHours(-m.UtcOffsetHours).AddHours(m.UtcOffsetHours));
                p("WindowsProductId", "string", m.ProductId); p("WindowsProductName", "string", m.IsServer ? "Windows Server 2022 Datacenter" : "Windows 10 Enterprise");
                p("WindowsRegisteredOrganization", "string", os.Get("Organization")); p("WindowsRegisteredOwner", "string", os.Get("RegisteredUser")); p("WindowsSystemRoot", "string", "C:\\Windows"); p("WindowsVersion", "string", "2009");
                p("OSDisplayVersion", "string", m.OsDisplayVersion);
                p("BiosCharacteristics", "uint16[]", new object[] { 3, 9, 15, 16, 17, 19, 26, 27, 28, 32, 33, 39, 42, 43 }); p("BiosBIOSVersion", "string[]", new object[] { "VRTUAL - 4001", "Hyper-V UEFI Release v4.1", "EDK II - 10000" });
                p("BiosManufacturer", "string", "Microsoft Corporation"); p("BiosName", "string", "Hyper-V UEFI Release v4.1"); p("BiosReleaseDate", "System.Nullable[datetime]", new DateTime(2023, 4, 5)); p("BiosSeralNumber", "string", m.BiosSerial); p("BiosSMBIOSBIOSVersion", "string", "Hyper-V UEFI Release v4.1"); p("BiosVersion", "string", "VRTUAL - 4001 Hyper-V UEFI Release v4.1");
                p("CsCaption", "string", m.Name); p("CsDNSHostName", "string", m.Name); p("CsDomain", "string", m.Domain); p("CsDomainRole", "string", m.IsServer ? "MemberServer" : "MemberWorkstation"); p("CsHypervisorPresent", "System.Nullable[bool]", true);
                p("CsManufacturer", "string", "Microsoft Corporation"); p("CsModel", "string", "Virtual Machine"); p("CsName", "string", m.Name); p("CsNumberOfLogicalProcessors", "System.Nullable[uint32]", (uint)m.Cpus); p("CsNumberOfProcessors", "System.Nullable[uint32]", 1u);
                p("CsPartOfDomain", "System.Nullable[bool]", true); p("CsPCSystemType", "string", "Desktop"); p("CsPrimaryOwnerName", "string", "Lab Administrator"); p("CsProcessors", "string[]", new object[] { "Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz" }); p("CsSystemFamily", "string", "Virtual Machine");
                p("CsSystemType", "string", "x64-based PC"); p("CsTotalPhysicalMemory", "System.Nullable[uint64]", cs.Get("TotalPhysicalMemory")); p("CsPhyicallyInstalledMemory", "System.Nullable[uint64]", (ulong)(m.MemoryBytes / 1024)); p("CsUserName", "string", cs.Get("UserName")); p("CsWorkgroup", "string", null);
                p("OsName", "string", m.OsCaption); p("OsType", "string", "WINNT"); p("OsOperatingSystemSKU", "string", m.IsServer ? "DatacenterServerEdition" : "EnterpriseEdition"); p("OsVersion", "string", m.OsVersion); p("OsBuildNumber", "string", m.OsBuild);
                p("OsSerialNumber", "string", m.SerialNumber); p("OsSystemDevice", "string", os.Get("SystemDevice")); p("OsSystemDirectory", "string", "C:\\Windows\\system32"); p("OsSystemDrive", "string", "C:"); p("OsWindowsDirectory", "string", "C:\\Windows");
                p("OsCountryCode", "string", "1"); p("OsCurrentTimeZone", "System.Nullable[int16]", (short)(m.UtcOffsetHours * 60)); p("OsLocaleID", "string", "0409"); p("OsLocale", "string", "en-US"); p("OsLocalDateTime", "System.Nullable[datetime]", m.Now);
                p("OsLastBootUpTime", "System.Nullable[datetime]", m.BootTime); p("OsUptime", "System.Nullable[timespan]", m.Now - m.BootTime); p("OsBuildType", "string", "Multiprocessor Free"); p("OsCodeSet", "string", "1252");
                p("OsTotalVisibleMemorySize", "System.Nullable[uint64]", os.Get("TotalVisibleMemorySize")); p("OsFreePhysicalMemory", "System.Nullable[uint64]", os.Get("FreePhysicalMemory")); p("OsTotalVirtualMemorySize", "System.Nullable[uint64]", os.Get("TotalVirtualMemorySize")); p("OsFreeVirtualMemory", "System.Nullable[uint64]", os.Get("FreeVirtualMemory"));
                p("OsInstallDate", "System.Nullable[datetime]", m.InstallDate); p("OsManufacturer", "string", "Microsoft Corporation"); p("OsOrganization", "string", os.Get("Organization")); p("OsArchitecture", "string", "64-bit"); p("OsLanguage", "string", "en-US"); p("OsProductType", "string", m.IsServer ? "Server" : "WorkStation");
                p("OsRegisteredUser", "string", os.Get("RegisteredUser")); p("OsNumberOfProcesses", "System.Nullable[uint32]", (uint)m.LiveProcesses().Count); p("OsNumberOfUsers", "System.Nullable[uint32]", (uint)(m.IsServer ? 2 : 1)); p("OsHotFixes", "Microsoft.PowerShell.Commands.HotFix[]", m.Hotfixes.Select(h => (object)h.Id).ToArray());
                p("OsPagingFiles", "string[]", new object[] { "C:\\pagefile.sys" }); p("OsMuiLanguages", "string[]", new object[] { "en-US" });
                p("KeyboardLayout", "string", "en-US"); p("TimeZone", "string", "(UTC-08:00) Pacific Time (US & Canada)"); p("LogonServer", "string", "\\\\DC01"); p("PowerPlatformRole", "System.Nullable[Microsoft.PowerShell.Commands.PowerPlatformRole]", "Desktop");
                p("HyperVisorPresent", "System.Nullable[bool]", true); p("HyperVRequirementDataExecutionPreventionAvailable", "System.Nullable[bool]", null); p("DeviceGuardSmartStatus", "System.Nullable[Microsoft.PowerShell.Commands.DeviceGuardSmartStatus]", "Off");
                var props = c.Strs("Property");
                if (props != null)
                {
                    var sel = new PsObj("Microsoft.PowerShell.Commands.ComputerInfo", "System.Object");
                    foreach (var pp in x.Props) if (props.Any(q => PsConv.Like(pp.Name, q, false))) sel.Put(pp.Clone());
                    foreach (var q in props) if (!PsConv.HasWildcard(q) && !x.Has(q)) c.Warn("Cert Lab: Get-ComputerInfo has no property named " + q + " on the practice machine.");
                    x = sel;
                }
                c.Out(x);
            });
        }

        // ------------------------------------------------------------ Windows features (Windows Server only)
        static PsObj FeatureObj(PsMachine m, WinFeature f)
        {
            var o = new PsObj("Microsoft.Windows.ServerManager.Commands.Feature", "System.Object");
            o.Base = f;
            o.Val("Name", "string", f.Name);
            o.Val("DisplayName", "string", f.Display);
            o.Val("Description", "string", f.Description.Length > 0 ? f.Description : f.Display + " is a Windows Server " + f.Type.ToLowerInvariant() + ".");
            o.Prop("Installed", "bool", () => f.State == "Installed");
            o.Prop("InstallState", "Microsoft.Windows.ServerManager.Commands.InstallState", () => En("Microsoft.Windows.ServerManager.Commands.InstallState", f.State));
            o.Val("FeatureType", "string", f.Type);
            o.Val("Path", "string", PathOf(m, f));
            o.Val("Depth", "int", f.Depth + 1);
            o.Val("DependsOn", "string[]", new object[0]);
            o.Val("Parent", "string", f.Parent);
            o.Val("ServerComponentDescriptor", "string", "ServerComponent_" + f.Name.Replace("-", "_"));
            o.Val("SubFeatures", "string[]", m.Features.Where(x => x.Parent == f.Name).Select(x => (object)x.Name).ToArray());
            o.Val("SystemService", "string[]", f.Services.Select(s => (object)s).ToArray());
            o.Val("Notification", "Microsoft.Windows.ServerManager.Commands.Notification[]", new object[0]);
            o.Val("BestPracticesModelId", "string", null);
            o.Val("EventQuery", "string", null);
            o.Val("PostConfigurationNeeded", "bool", false);
            o.Val("AdditionalInfo", "System.Collections.Hashtable", PsConv.NewHashtable());
            o.ToStr = x => f.Name;
            o.DefaultDisplay = new[] { "DisplayName", "Name", "InstallState" };
            return o;
        }
        static string PathOf(PsMachine m, WinFeature f) { var parts = new List<string>(); for (var x = f; x != null; x = x.Parent == null ? null : m.FindFeature(x.Parent)) parts.Insert(0, x.Display); return string.Join("\\", parts.ToArray()); }

        static void RegFeatures()
        {
            Func<PsMachine, bool> srv = m => m.IsServer;
            var gf = Add("Get-WindowsFeature", SrvMgr, "Name:string[]@0 pipe prop alias=FeatureName; Vhd:string; ComputerName:string alias=Cn; Credential:pscredential; LogPath:string", (c, o) =>
            {
                var m = RemoteRpc(c, c.Str("ComputerName"));
                if (m == null || !m.IsServer) { c.Error("The target of the specified cmdlet cannot be a Windows client-based operating system.", "UnSupportedTargetDevice", "DeviceError", c.Str("ComputerName"), "System.Exception"); return; }
                var names = c.Strs("Name");
                if (names == null) { foreach (var f in Ordered(m)) c.Out(FeatureObj(m, f)); return; }
                foreach (var n in names)
                {
                    var hit = Ordered(m).Where(f => PsConv.Like(f.Name, n, false)).ToList();
                    foreach (var f in hit) c.Out(FeatureObj(m, f));
                }
            });
            gf.Available = srv; gf.FqClass = "Microsoft.Windows.ServerManager.Commands.GetWindowsFeatureCommand";

            var inf = Add("Install-WindowsFeature", SrvMgr, "Name:object[]@0 ! pipe prop alias=FeatureName; IncludeAllSubFeature:switch; IncludeManagementTools:switch; Restart:switch; Source:string[]; ComputerName:string alias=Cn; Credential:pscredential; Vhd:string; LogPath:string", (c, o) =>
            {
                var m = RemoteMachine(c, c.Str("ComputerName")) ?? c.M;
                var picked = ResolveFeatures(c, m, "Install-WindowsFeature");
                if (picked == null) return;
                var add = new List<WinFeature>();
                Action<WinFeature> want = null;
                want = f =>
                {
                    if (add.Contains(f) || f.State == "Installed" || f.State == "InstallPending") return;
                    add.Add(f);
                    for (var p = f.Parent == null ? null : m.FindFeature(f.Parent); p != null; p = p.Parent == null ? null : m.FindFeature(p.Parent)) if (!add.Contains(p) && p.State != "Installed") add.Add(p);
                    foreach (var k in m.Features.Where(x => x.Parent == f.Name)) if (c.Sw("IncludeAllSubFeature") || k.Default || k.ManagementTool && c.Sw("IncludeManagementTools")) want(k);
                };
                foreach (var f in picked) want(f);
                if (c.Sw("IncludeManagementTools") && picked.Any(f => f.Name == "Web-Server")) { want(m.FindFeature("Web-Mgmt-Tools")); want(m.FindFeature("Web-Mgmt-Console")); }
                if (picked.Any(f => f.Name == "AD-Domain-Services" || f.Name == "DNS" || f.Name == "DHCP") && c.Sw("IncludeManagementTools")) { var t = m.FindFeature(picked.Any(f => f.Name == "DNS") ? "RSAT-DNS-Server" : picked.Any(f => f.Name == "DHCP") ? "RSAT-DHCP" : "RSAT-AD-PowerShell"); if (t != null) want(t); }
                if (!c.ShouldProcess(string.Join(", ", picked.Select(f => f.Display).ToArray()), "Install-WindowsFeature")) return;
                bool restart = add.Any(f => f.Restart);
                foreach (var f in add) { f.State = f.Restart ? "InstallPending" : "Installed"; foreach (var s in f.Services) AddFeatureService(m, s, false); }
                foreach (var f in add) foreach (var s in f.Services) { var sv = m.Svc(s); if (sv != null && sv.StartType == "Automatic" && !f.Restart) m.StartService(sv, true); }
                if (add.Any(f => f.Name == "Web-Server")) SetUpIis(m);
                if (restart) c.Warn("You must restart this server to finish the installation process.");
                if (restart && c.Sw("Restart")) { c.Out(ResultObj(m, true, "Yes", "SuccessRestartRequired", add)); m.Reboot(); return; }
                c.Out(ResultObj(m, true, restart ? "Yes" : "No", add.Count == 0 ? "NoChangeNeeded" : (restart ? "SuccessRestartRequired" : "Success"), add));
                m.AdminChanged("feature");
            });
            inf.Available = srv; inf.ShouldProcess = true; inf.FqClass = "Microsoft.Windows.ServerManager.Commands.AddWindowsFeatureCommand";

            var unf = Add("Uninstall-WindowsFeature", SrvMgr, "Name:object[]@0 ! pipe prop alias=FeatureName; IncludeManagementTools:switch; Remove:switch; Restart:switch; ComputerName:string alias=Cn; Credential:pscredential; Vhd:string; LogPath:string", (c, o) =>
            {
                var m = RemoteMachine(c, c.Str("ComputerName")) ?? c.M;
                var picked = ResolveFeatures(c, m, "Uninstall-WindowsFeature");
                if (picked == null) return;
                var rem = new List<WinFeature>();
                Action<WinFeature> drop = null;
                drop = f => { if (rem.Contains(f) || f.State == "Available" || f.State == "Removed") return; rem.Add(f); foreach (var k in m.Features.Where(x => x.Parent == f.Name)) drop(k); };
                foreach (var f in picked) drop(f);
                if (!c.ShouldProcess(string.Join(", ", picked.Select(f => f.Display).ToArray()), "Uninstall-WindowsFeature")) return;
                bool restart = rem.Any(f => f.Restart || f.Name == "Web-Server");
                foreach (var f in rem)
                {
                    f.State = c.Sw("Remove") ? "Removed" : "Available";
                    foreach (var sn in f.Services) { var s = m.Svc(sn); if (s != null) { m.StopService(s, true, true); s.Deleted = true; } }
                }
                if (restart) c.Warn("You must restart this server to finish the removal process.");
                c.Out(ResultObj(m, true, restart ? "Yes" : "No", rem.Count == 0 ? "NoChangeNeeded" : (restart ? "SuccessRestartRequired" : "Success"), rem));
                if (restart && c.Sw("Restart")) m.Reboot();
                m.AdminChanged("feature");
            });
            unf.Available = srv; unf.ShouldProcess = true; unf.FqClass = "Microsoft.Windows.ServerManager.Commands.RemoveWindowsFeatureCommand";
        }
        static IEnumerable<WinFeature> Ordered(PsMachine m)
        {
            var res = new List<WinFeature>();
            Action<WinFeature> walk = null;
            walk = f => { res.Add(f); foreach (var k in m.Features.Where(x => x.Parent == f.Name).OrderBy(x => x.Display, StringComparer.OrdinalIgnoreCase)) walk(k); };
            foreach (var t in new[] { "Role", "Feature" })
                foreach (var f in m.Features.Where(x => x.Parent == null && x.Type == t).OrderBy(x => x.Display, StringComparer.OrdinalIgnoreCase)) walk(f);
            return res;
        }
        static List<WinFeature> ResolveFeatures(CmdCtx c, PsMachine m, string cmd)
        {
            var l = new List<WinFeature>();
            foreach (var x in c.Objs("Name"))
            {
                var po = PsConv.Base(x) as PsObj;
                string n = po != null ? PsConv.ToStr(po.Get("Name")) : PsConv.ToStr(x);
                var f = m.FindFeature(n);
                if (f == null)
                {
                    c.WriteError(c.MakeError("ArgumentNotValid: The role, role service, or feature name is not valid: '" + n + "'. The name was not found.", "NameDoesNotExist", "InvalidArgument", n, "System.Exception"));
                    return null;
                }
                l.Add(f);
            }
            return l;
        }
        static PsObj ResultObj(PsMachine m, bool ok, string restart, string code, List<WinFeature> fs)
        {
            var o = new PsObj("Microsoft.Windows.ServerManager.Commands.FeatureOperationResult", "System.Object");
            o.Val("Success", "bool", ok);
            o.Val("RestartNeeded", "Microsoft.Windows.ServerManager.Commands.RestartState", En("Microsoft.Windows.ServerManager.Commands.RestartState", restart));
            o.Val("FeatureResult", "Microsoft.Windows.ServerManager.Commands.FeatureOperationResult[]", Ordered(m).Where(fs.Contains).Select(f => { var r = new PsObj("Microsoft.Windows.ServerManager.Commands.FeatureOperationResultEntry", "System.Object"); r.Val("DisplayName", "string", f.Display); r.Val("Name", "string", f.Name); r.Val("RestartNeeded", "bool", f.Restart); r.Val("SkipReason", "string", "NotSkipped"); r.Val("Success", "bool", true); r.Val("Id", "int", Math.Abs(FwHash.Of(f.Name) % 1000)); r.ToStr = x => f.Display; return (object)r; }).ToArray());
            o.Val("ExitCode", "Microsoft.Windows.ServerManager.Commands.FeatureOperationExitCode", En("Microsoft.Windows.ServerManager.Commands.FeatureOperationExitCode", code == "SuccessRestartRequired" ? "Success" : code));
            if (code == "SuccessRestartRequired") o.Set("ExitCode", "SuccessRestartRequired");
            o.DefaultDisplay = new[] { "Success", "RestartNeeded", "ExitCode", "FeatureResult" };
            return o;
        }
        public static void AddFeatureService(PsMachine m, string name, bool start)
        {
            var s = m.Services.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (s == null)
            {
                string display = name;
                switch (name)
                {
                    case "W3SVC": display = "World Wide Web Publishing Service"; break; case "WAS": display = "Windows Process Activation Service"; break;
                    case "DHCPServer": display = "DHCP Server"; break; case "DNS": display = "DNS Server"; break; case "SNMP": display = "SNMP Service"; break;
                    case "wbengine": display = "Block Level Backup Engine Service"; break; case "Dfs": display = "DFS Namespace"; break; case "DFSR": display = "DFS Replication"; break;
                    case "SrmSvc": display = "File Server Resource Manager"; break; case "CertSvc": display = "Active Directory Certificate Services"; break; case "WsusService": display = "WSUS Service"; break;
                    case "WDSServer": display = "Windows Deployment Services Server"; break; case "ClusSvc": display = "Cluster Service"; break; case "ddpsvc": display = "Data Deduplication Service"; break;
                    case "vmms": display = "Hyper-V Virtual Machine Management"; break; case "NTDS": display = "Active Directory Domain Services"; break; case "ADWS": display = "Active Directory Web Services"; break; case "Kdc": display = "Kerberos Key Distribution Center"; break;
                }
                s = new WinService { Name = name, DisplayName = display, Exe = name == "W3SVC" || name == "WAS" ? "svchost" : name.ToLowerInvariant(), Feature = true, ServiceType = "Win32ShareProcess" };
                s.BinaryPath = s.Exe == "svchost" ? "C:\\Windows\\system32\\svchost.exe -k iissvcs" : "C:\\Windows\\system32\\" + s.Exe + ".exe";
                s.StartType = name == "wbengine" ? "Manual" : "Automatic";
                if (name == "W3SVC") s.DependsOn.AddRange(new[] { "WAS", "HTTP" });
                if (name == "W3SVC" || name == "WAS") s.CanPause = name == "W3SVC";
                m.Services.Add(s);
            }
            s.Deleted = false;
            if (start && s.StartType == "Automatic") m.StartService(s, true);
        }
        static void SetUpIis(PsMachine m)
        {
            var t = m.Now;
            m.MakeDir("C:\\inetpub\\wwwroot", t);
            m.MakeDir("C:\\inetpub\\logs\\LogFiles", t);
            m.MakeDir("C:\\inetpub\\temp", t);
            if (m.FindFile("C:\\inetpub\\wwwroot\\iisstart.htm") == null)
                m.File("C:\\inetpub\\wwwroot\\iisstart.htm", "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\r\n<head>\r\n<meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\" />\r\n<title>IIS Windows Server</title>\r\n</head>\r\n<body>\r\n<div id=\"container\">\r\n<a href=\"http://go.microsoft.com/fwlink/?linkid=66138&amp;clcid=0x409\"><img src=\"iisstart.png\" alt=\"IIS\" width=\"960\" height=\"600\" /></a>\r\n</div>\r\n</body>\r\n</html>", t);
            if (m.FindFile("C:\\inetpub\\wwwroot\\iisstart.png") == null) { var f = m.File("C:\\inetpub\\wwwroot\\iisstart.png", "", t); f.FakeSize = 98757; f.Bytes = new byte[0]; }
            m.MakeDir("C:\\Windows\\System32\\inetsrv\\config", t);
            if (!m.ExtraIps.Contains("") && m.Net.Hosts.FirstOrDefault(h => h.Machine == m) != null) { var h = m.Net.Hosts.First(x => x.Machine == m); if (!h.Ports.Contains(80)) h.Ports.Add(80); }
        }

        // ------------------------------------------------------------ views
        static void AdminViews()
        {
            Func<object, string, object> G = (o, p) => { var po = PsConv.Base(o) as PsObj; return po == null ? null : po.Get(p); };
            PsFormat.Table("System.Diagnostics.Process#IncludeUserName", PsFormat.Col("Handles", 7, true, o => G(o, "HandleCount")), PsFormat.Col("WS(K)", 10, true, o => { var v = G(o, "WS"); return v == null ? "" : (object)(long)(PsConv.ToDouble(v) / 1024); }),
                PsFormat.Col("CPU(s)", 8, true, o => { var v = G(o, "CPU"); return v == null ? "" : PsConv.ToDouble(v).ToString("N2", PsConv.Us); }), PsFormat.Col("Id", 6, true, o => G(o, "Id")), PsFormat.P("UserName", 22), PsFormat.P("ProcessName", 0));
            PsFormat.AutoTable("System.Diagnostics.FileVersionInfo", PsFormat.P("ProductVersion", 0), PsFormat.P("FileVersion", 0), PsFormat.P("FileName", 0));
            PsFormat.AutoTable("System.Diagnostics.ProcessModule", PsFormat.PR("Size(K)", 0), PsFormat.P("ModuleName", 0), PsFormat.P("FileName", 0));
            // events
            var ev = new FmtView
            {
                Cols = new List<FmtCol> { PsFormat.Col("TimeCreated", 24, false, o => G(o, "TimeCreated")), PsFormat.Col("Id", 10, true, o => G(o, "Id")), PsFormat.P("LevelDisplayName", 16), PsFormat.P("Message", 0) },
                GroupKey = o => PsConv.ToStr(G(o, "ProviderName")),
                GroupHeader = o => "   ProviderName: " + PsConv.ToStr(G(o, "ProviderName")),
                GroupOneBlank = true
            };
            PsFormat.View("System.Diagnostics.Eventing.Reader.EventLogRecord", ev);
            PsFormat.ListProps("System.Diagnostics.Eventing.Reader.EventLogRecord", "TimeCreated", "ProviderName", "Id", "Message");
            PsFormat.Table("System.Diagnostics.EventLogEntry", PsFormat.PR("Index", 8), PsFormat.Col("Time", 13, false, o => { var t = G(o, "TimeGenerated"); return t is DateTime ? ((DateTime)t).ToString("MMM dd HH:mm", PsConv.Us) : ""; }), PsFormat.P("EntryType", 11), PsFormat.P("Source", 20), PsFormat.Col("InstanceID", 12, true, o => G(o, "InstanceId")), PsFormat.P("Message", 0));
            PsFormat.ListProps("System.Diagnostics.EventLogEntry", "Index", "EntryType", "InstanceId", "Message", "Category", "CategoryNumber", "ReplacementStrings", "Source", "TimeGenerated", "TimeWritten", "UserName");
            PsFormat.Table("System.Diagnostics.EventLog", PsFormat.Col("Max(K)", 8, true, o => { var v = G(o, "MaximumKilobytes"); return v == null ? "" : PsConv.ToLong(v).ToString("N0", PsConv.Us); }), PsFormat.PR("Retain", 6), PsFormat.P("OverflowAction", 21), PsFormat.Col("Entries", 7, true, o => { var v = G(o, "Entries"); return v == null ? "" : PsConv.Items(v).Count().ToString("N0", PsConv.Us); }), PsFormat.P("Log", 0));
            views_set("System.Diagnostics.EventLog", "Retain", o => G(o, "MinimumRetentionDays"));
            PsFormat.Table("System.Diagnostics.Eventing.Reader.EventLogConfiguration", PsFormat.P("LogMode", 9), PsFormat.PR("MaximumSizeInBytes", 18), PsFormat.PR("RecordCount", 11), PsFormat.P("LogName", 0));
            PsFormat.AutoTable("System.Diagnostics.Eventing.Reader.ProviderMetadata", PsFormat.P("Name", 0), PsFormat.P("LogLinks", 0));
            // tasks
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#Root/Microsoft/Windows/TaskScheduler/MSFT_ScheduledTask", PsFormat.P("TaskPath", 46), PsFormat.P("TaskName", 33), PsFormat.P("State", 0));
            // accounts
            PsFormat.AutoTable("Microsoft.PowerShell.Commands.LocalUser", PsFormat.P("Name", 0), PsFormat.P("Enabled", 0), PsFormat.P("Description", 0));
            PsFormat.AutoTable("Microsoft.PowerShell.Commands.LocalGroup", PsFormat.P("Name", 0), PsFormat.P("Description", 0));
            PsFormat.AutoTable("Microsoft.PowerShell.Commands.LocalPrincipal", PsFormat.P("ObjectClass", 0), PsFormat.P("Name", 0), PsFormat.P("PrincipalSource", 0));
            // CIM classes with a table view
            foreach (var ci in PsCim.All)
            {
                if (ci.Name == "Win32_BIOS" || ci.Name == "Win32_ComputerSystemProduct" || ci.Name == "MSFT_ScheduledTask") continue;
                string key = "Microsoft.Management.Infrastructure.CimInstance#" + ci.Namespace + "/" + ci.Name;
                if (ci.Name == "Win32_QuickFixEngineering") continue;
                if (ci.Name == "Win32_PhysicalMemory") { PsFormat.AutoTable(key, PsFormat.P("Manufacturer", 0), new FmtCol { Header = "Banklabel", Prop = "BankLabel" }, new FmtCol { Header = "Configuredclockspeed", Prop = "ConfiguredClockSpeed" }, new FmtCol { Header = "Devicelocator", Prop = "DeviceLocator" }, new FmtCol { Header = "Capacity", Prop = "Capacity" }, new FmtCol { Header = "Serialnumber", Prop = "SerialNumber" }); continue; }
                PsFormat.AutoTable(key, ci.View.Select(p => PsFormat.P(p, 0)).ToArray());
            }
            var qfe = new[] { PsFormat.P("Source", 13), PsFormat.P("Description", 16), PsFormat.P("HotFixID", 13), PsFormat.P("InstalledBy", 20), PsFormat.Col("InstalledOn", 0, false, o => G(o, "InstalledOn")) };
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#root/cimv2/Win32_QuickFixEngineering", qfe);
            PsFormat.Table("System.Management.ManagementObject#root\\cimv2\\Win32_QuickFixEngineering", qfe);
            PsFormat.View("Microsoft.Management.Infrastructure.CimClass", new FmtView
            {
                Cols = new List<FmtCol> { PsFormat.P("CimClassName", 35), PsFormat.P("CimClassMethods", 20), PsFormat.P("CimClassProperties", 0) },
                GroupKey = o => PsConv.ToStr(G(o, "Namespace")),
                GroupHeader = o => "   NameSpace: " + PsConv.ToStr(G(o, "Namespace")),
                GroupOneBlank = true
            });
            // features
            PsFormat.Table("Microsoft.Windows.ServerManager.Commands.Feature",
                PsFormat.Col("Display Name", 55, false, o => { var f = (PsConv.Base(o) as PsObj).Base as WinFeature; if (f == null) return G(o, "DisplayName"); return new string(' ', f.Depth * 4) + (f.State == "Installed" ? "[X] " : "[ ] ") + f.Display; }),
                PsFormat.P("Name", 26), PsFormat.Col("Install State", 13, true, o => G(o, "InstallState")));
            PsFormat.Table("Microsoft.Windows.ServerManager.Commands.FeatureOperationResult", PsFormat.P("Success", 7), PsFormat.Col("Restart Needed", 14, false, o => G(o, "RestartNeeded")), PsFormat.Col("Exit Code", 14, false, o => G(o, "ExitCode")), PsFormat.Col("Feature Result", 0, false, o => G(o, "FeatureResult")));
        }
        static void views_set(string type, string header, Func<object, object> get)
        {
            var v = PsFormat.ViewFor(new PsObj(type));
            if (v == null) return;
            foreach (var col in v.Cols) if (col.Header == header) { col.Get = get; col.Prop = null; }
        }
    }
}
