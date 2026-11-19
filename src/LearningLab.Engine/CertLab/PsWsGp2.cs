// Cert Lab - GroupPolicy module: registry-based settings (Set-GPRegistryValue writes the same setting the editor's
// policy writes), permissions and security filtering, backups, reports (RSoP and GPO reports as files) and
// Invoke-GPUpdate (a remote refresh, after a random delay of up to 10 minutes unless -RandomDelayInMinutes 0).
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
        static partial void RegWsGpMore();
        static void RegWsGp2()
        {
            GpCmd("Set-GPRegistryValue", GpId + "; Key:string !; ValueName:string[]; Value:object; Type:string set=String|ExpandString|Binary|DWord|MultiString|QWord; Additive:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d);
                string key = GpCatalog.NormKey(c.Str("Key"));
                if (!key.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)) throw c.Fail("The key must start with HKLM\\ or HKCU\\ (HKEY_LOCAL_MACHINE or HKEY_CURRENT_USER).", "InvalidKey", "InvalidArgument", key, "System.ArgumentException");
                foreach (var vn in c.Strs("ValueName") ?? new[] { "" })
                {
                    var def = GpCatalog.ByRegistry(key, vn);
                    string data = PsConv.ToStr(c.Get("Value"));
                    if (def != null)
                    {
                        string state = WsText.Eq(data, def.Off) && def.Off.Length > 0 ? "Disabled" : "Enabled";
                        string val = def.Kind == "choice" ? (def.Options.Select(x => x.Split('=')).FirstOrDefault(x => (x.Length > 1 ? x[1] : (def.Options.IndexOf(string.Join("=", x)) + 1).ToString(CultureInfo.InvariantCulture)) == data) ?? new[] { data })[0] : def.Kind == "toggle" ? "" : data;
                        lab.SetSetting(dc, g, def.Scope, def.Id, state, val);
                    }
                    else lab.SetSetting(dc, g, key.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ? "U" : "C", "reg:" + key + "::" + vn, "Enabled", (c.Str("Type") ?? "String") + ":" + data);
                }
                c.Out(GpoObj(lab, d, g));
            });
            GpCmd("Get-GPRegistryValue", GpId + "; Key:string !; ValueName:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d);
                string key = GpCatalog.NormKey(c.Str("Key")); bool any = false;
                foreach (var sc in new[] { "C", "U" })
                    foreach (var kv in WsLab.Settings(g, sc))
                    {
                        var def = GpCatalog.Get(kv.Key);
                        string k2, vn, ty, data; var p = kv.Value.Split(new[] { '|' }, 2);
                        if (def != null) { if (!def.IsRegistry) continue; k2 = def.Key; vn = def.ValueName; ty = def.Type; data = def.Data(p[0], p.Length > 1 ? p[1] : ""); }
                        else if (kv.Key.StartsWith("reg:", StringComparison.Ordinal)) { var q = kv.Key.Substring(4).Split(new[] { "::" }, StringSplitOptions.None); k2 = q[0]; vn = q.Length > 1 ? q[1] : ""; var tv = (p.Length > 1 ? p[1] : "").Split(new[] { ':' }, 2); ty = tv[0]; data = tv.Length > 1 ? tv[1] : ""; }
                        else continue;
                        if (!WsText.Eq(GpCatalog.NormKey(k2), key) || c.Has("ValueName") && !WsText.Eq(vn, c.Str("ValueName"))) continue;
                        var r = new PsObj("Microsoft.GroupPolicy.PolicyRegistrySetting", "Microsoft.GroupPolicy.RegistrySetting", "System.Object");
                        r.Val("PolicyState", "Microsoft.GroupPolicy.PolicyState", "Set"); r.Val("Hive", "Microsoft.Win32.RegistryHive", k2.StartsWith("HKCU", StringComparison.Ordinal) ? "CurrentUser" : "LocalMachine");
                        r.Val("KeyPath", "string", k2.Substring(5)); r.Val("FullKeyPath", "string", (k2.StartsWith("HKCU", StringComparison.Ordinal) ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE") + k2.Substring(4));
                        r.Val("ValueName", "string", vn); r.Val("Type", "Microsoft.Win32.RegistryValueKind", ty); r.Val("Value", "object", ty == "DWord" ? (object)int.Parse("0" + data, CultureInfo.InvariantCulture) : data);
                        r.DefaultDisplay = new[] { "KeyPath", "FullKeyPath", "Hive", "PolicyState", "Value", "Type", "ValueName" };
                        c.Out(r); any = true;
                    }
                if (!any) throw c.Fail("The following key or value was not found in the GPO: " + c.Str("Key") + (c.Has("ValueName") ? "\\" + c.Str("ValueName") : ""), "RegistryValueNotFound", "ObjectNotFound", key, "System.ArgumentException");
            });
            GpCmd("Remove-GPRegistryValue", GpId + "; Key:string !; ValueName:string[]", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d); string key = GpCatalog.NormKey(c.Str("Key"));
                foreach (var vn in c.Strs("ValueName") ?? new string[0]) { var def = GpCatalog.ByRegistry(key, vn); if (def != null) lab.SetSetting(dc, g, def.Scope, def.Id, null, null); else lab.SetSetting(dc, g, key.StartsWith("HKCU", StringComparison.Ordinal) ? "U" : "C", "reg:" + key + "::" + vn, null, null); }
            });
            GpCmd("Set-GPPermission", GpId + "; TargetName:string !; TargetType:string ! set=Computer|User|Group|WellKnownGroup; PermissionLevel:string ! set=GpoRead|GpoApply|GpoEdit|GpoEditDeleteModifySecurity|None; Replace:switch; All:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d);
                string who = c.Str("TargetName"), level = c.Str("PermissionLevel");
                if (c.Str("TargetType") != "WellKnownGroup" && !new[] { "Authenticated Users", "Everyone" }.Contains(who, StringComparer.OrdinalIgnoreCase) && d.Find(who, "principal") == null) throw c.Fail("Trustee " + who + " could not be found.", "TrusteeNotFound", "ObjectNotFound", who, "System.ArgumentException");
                string have = WsLab.PermOf(g, who);
                if (Array.IndexOf(WsLab.PermLevels, level) < Array.IndexOf(WsLab.PermLevels, have) && !c.Sw("Replace")) throw c.Fail("The permission level " + have + " that " + who + " has is higher than " + level + ": use -Replace to lower it.", "PermissionDowngrade", "InvalidOperation", who, "System.InvalidOperationException");
                lab.SetPerm(dc, g, who, WsLab.PermLevels.First(x => WsText.Eq(x, level)));
                c.Out(GpoObj(lab, d, g));
            });
            GpCmd("Get-GPPermission", GpId + "; TargetName:string; TargetType:string set=Computer|User|Group|WellKnownGroup; All:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d);
                foreach (var e in g.List("gpPerm"))
                {
                    var p = e.Split('|'); if (c.Has("TargetName") && !WsText.Eq(WsLab.StripDomain(p[0]), WsLab.StripDomain(c.Str("TargetName")))) continue;
                    var r = new PsObj("Microsoft.GroupPolicy.GPPermission", "System.Object");
                    var tr = new PsObj("Microsoft.GroupPolicy.GPTrustee", "System.Object"); tr.Val("Name", "string", p[0]); var x = d.Find(p[0], "principal"); string dom = x == null ? "NT AUTHORITY" : lab.NetBios; tr.Val("Domain", "string", dom); tr.Val("SidType", "string", x == null ? "WellKnownGroup" : x.Is("user") ? "User" : x.Is("computer") ? "Computer" : "Group"); tr.ToStr = y => dom + "\\" + p[0];
                    r.Val("Trustee", "Microsoft.GroupPolicy.GPTrustee", tr); r.Val("TrusteeType", "string", x == null ? "WellKnownGroup" : x.Is("user") ? "User" : x.Is("computer") ? "Computer" : "Group"); r.Val("Permission", "Microsoft.GroupPolicy.GPPermissionType", p[1]); r.Val("Inherited", "bool", false);
                    c.Out(r);
                }
            });
            GpCmd("Backup-GPO", GpId + "; All:switch; Path:string !; Comment:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc);
                var list = c.Sw("All") ? lab.Gpos(d).ToList() : new List<WsAdObj> { GpFind(c, d) };
                foreach (var g in list)
                {
                    var b = lab.Backup(dc, g, c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir), c.Str("Comment"));
                    c.M.MakeDir(b.Path + "\\{" + b.Id.ToUpperInvariant() + "}", lab.Now);
                    var r = new PsObj("Microsoft.GroupPolicy.GpoBackup", "System.Object");
                    r.Val("DisplayName", "string", b.Gpo); r.Val("GpoId", "guid", new Guid(b.Guid)); r.Val("Id", "guid", new Guid(b.Id)); r.Val("BackupDirectory", "string", b.Path); r.Val("CreationTime", "datetime", b.When); r.Val("DomainName", "string", lab.DomainDns); r.Val("Comment", "string", b.Comment);
                    c.Out(r);
                }
            });
            GpCmd("Restore-GPO", GpId + "; All:switch; Path:string !; BackupId:object", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c);
                string key = c.Str("Name") ?? c.Str("Guid");
                var b = lab.GpoBackups.Where(x => WsText.Eq(x.Gpo, key) || WsText.Eq(x.Guid, (key ?? "").Trim('{', '}'))).OrderByDescending(x => x.When).FirstOrDefault();
                if (b == null) throw c.Fail("There is no backup of " + key + " in " + c.Str("Path") + ".", "BackupNotFound", "ObjectNotFound", key, "System.ArgumentException");
                lab.RestoreGpo(dc, b);
                c.Out(GpoObj(lab, lab.Dir(dc), lab.Dir(dc).ById(b.Guid)));
            });
            GpCmd("Invoke-GPUpdate", "Computer:string@0; Target:string set=Computer|User; Force:switch; RandomDelayInMinutes:int; Boot:switch; LogOff:switch; AsJob:switch", (c, o) =>
            {
                var lab = Lab(c); string name = c.Str("Computer") ?? c.M.Name;
                var s = lab.Server(name);
                if (s == null || !s.Running) throw c.Fail("Computer " + name + " is not responding. The target computer is either turned off or Remote Scheduled Tasks Management Firewall rules are disabled.", "Invoke-GPUpdate", "OperationTimeout", name, "System.Runtime.InteropServices.COMException");
                int delay = c.Has("RandomDelayInMinutes") ? c.Int("RandomDelayInMinutes", 10) : lab.Net.Rng.Next(1, 11);
                string target = c.Has("Target") ? c.Str("Target").ToLowerInvariant() : null;
                if (delay <= 0) lab.Refresh(s.Name, target);
                else lab.At("gpremote:" + s.Name, lab.Now.AddMinutes(delay), () => lab.Refresh(s.Name, target), false);
            });
            RegWsGpMore();
        }
    }
}
