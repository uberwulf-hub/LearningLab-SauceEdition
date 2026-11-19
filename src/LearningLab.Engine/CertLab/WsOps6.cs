// Cert Lab - Windows Admin Center (practice) actions on files and shares, NTFS permissions, Hyper-V, DFS and storage.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsOps
    {
        static void RunShare(WsLab lab, string noun, Args a, ref WsResult r)
        {
            var s = lab.Server(a["server"] ?? "FS1");
            if (s == null) { r = WsResult.Fail("There is no server " + a["server"] + "."); return; }
            string name = a["name"] ?? a.P(0);
            if (noun == "new")
            {
                string path = a["path"];
                if (s.Share(name) != null) { r = WsResult.Fail("The name " + name + " has already been shared on " + s.Name + "."); return; }
                if (s.M.FindFile(path ?? "") == null || !s.M.FindFile(path).IsDir) { r = WsResult.Fail("The folder " + path + " doesn't exist on " + s.Name + "."); return; }
                var sh = new WsShare { Name = name, Path = path, Description = a["description"] ?? "", FolderEnumerationMode = a.Yes("abe", false) ? "AccessBased" : "Unrestricted", EncryptData = a.Yes("encrypt", false) };
                string acc = a["access"] ?? "Everyone:Read";
                foreach (var e in acc.Split(';').Where(x => x.Trim().Length > 0))
                {
                    var p = e.Split(':'); string right = p.Length > 1 ? p[1].Trim() : "Read";
                    if (!new[] { "Read", "Change", "Full" }.Contains(right, StringComparer.OrdinalIgnoreCase)) { r = WsResult.Fail("Share permissions are Read, Change or Full."); return; }
                    sh.Access.Add(new WsShareAce { Trustee = p[0].Trim(), Right = char.ToUpperInvariant(right[0]) + right.Substring(1).ToLowerInvariant() });
                }
                s.Shares.Add(sh);
                r = WsResult.Done("Shared " + path + " as \\\\" + s.Name + "\\" + name + "."); return;
            }
            var share = s.Share(name);
            if (share == null) { r = WsResult.Fail("There is no share " + name + " on " + s.Name + "."); return; }
            switch (noun)
            {
                case "remove": s.Shares.Remove(share); r = WsResult.Done("Stopped sharing " + name + "."); return;
                case "grant": case "revoke": case "deny":
                    {
                        string who = a["account"] ?? a.P(1), right = a["right"] ?? "Read";
                        share.Access.RemoveAll(x => WsText.Eq(x.Trustee, who) && (noun == "revoke" || x.Allow == (noun == "grant")));
                        if (noun != "revoke") share.Access.Add(new WsShareAce { Trustee = who, Right = char.ToUpperInvariant(right[0]) + right.Substring(1).ToLowerInvariant(), Allow = noun == "grant" });
                        r = WsResult.Done(name + ": " + who + (noun == "revoke" ? " removed." : " " + (noun == "deny" ? "denied " : "") + right + ".")); return;
                    }
                case "set":
                    if (a.Has("abe")) share.FolderEnumerationMode = a.Yes("abe", false) ? "AccessBased" : "Unrestricted";
                    if (a.Has("encrypt")) share.EncryptData = a.Yes("encrypt", false);
                    if (a.Has("description")) share.Description = a["description"];
                    r = WsResult.Done("Saved the share " + name + "."); return;
            }
        }

        static void RunNtfs(WsLab lab, string noun, Args a, ref WsResult r)
        {
            var s = lab.Server(a["server"] ?? "FS1");
            var f = s == null ? null : s.M.FindFile(a["path"] ?? a.P(0) ?? "");
            if (f == null) { r = WsResult.Fail("The path " + (a["path"] ?? a.P(0)) + " doesn't exist on " + (s == null ? a["server"] : s.Name) + "."); return; }
            var acl = s.AclOf(f, true);
            switch (noun)
            {
                case "grant": case "deny":
                    {
                        int m = NtfsRights.Parse(a["rights"] ?? "RX");
                        if (m <= 0) { r = WsResult.Fail("Rights: FullControl, Modify, ReadAndExecute, Read, Write (or F, M, RX, R, W)."); return; }
                        string who = a["account"];
                        if (string.IsNullOrEmpty(who)) { r = WsResult.Fail("account= the user or group."); return; }
                        string apply = (a["apply"] ?? "this,subfolders,files").ToLowerInvariant();
                        var ace = new WsAce { Trustee = who, Allow = noun == "grant", Rights = m, OI = apply.Contains("files"), CI = apply.Contains("subfolders"), IO = !apply.Contains("this") };
                        if (!f.IsDir) { ace.OI = ace.CI = ace.IO = false; }
                        acl.Explicit.Add(ace);
                        r = WsResult.Done((noun == "grant" ? "Allowed " : "Denied ") + who + " " + NtfsRights.Words(m) + " on " + f.FullPath + "."); return;
                    }
                case "remove": { int n = acl.Explicit.RemoveAll(x => WsText.Eq(x.Trustee, a["account"])); r = n == 0 ? WsResult.Fail(a["account"] + " has no entry of its own on " + f.FullPath + " (inherited entries are removed on the parent).") : WsResult.Done("Removed " + a["account"] + " from " + f.FullPath + "."); return; }
                case "inheritance":
                    {
                        string mode = ((a["path"] != null ? a.P(0) : a.P(1)) ?? a["mode"] ?? "").ToLowerInvariant();
                        if (mode == "enable") { acl.Protected = false; r = WsResult.Done("Inheritance is enabled on " + f.FullPath + "."); return; }
                        if (mode != "convert" && mode != "remove") { r = WsResult.Fail("ntfs inheritance PATH enable|convert|remove (disable and convert the inherited entries, or remove them)."); return; }
                        if (mode == "convert") foreach (var e in s.EffectiveAces(f).Where(x => x.Inherited)) { var c = e.Copy(); c.Inherited = false; acl.Explicit.Add(c); }
                        acl.Protected = true;
                        r = WsResult.Done("Inheritance is disabled on " + f.FullPath + (mode == "convert" ? " (inherited entries kept as its own)." : " (inherited entries removed).")); return;
                    }
                case "owner": acl.Owner = a["account"] ?? "BUILTIN\\Administrators"; r = WsResult.Done("The owner of " + f.FullPath + " is " + acl.Owner + "."); return;
            }
            r = WsResult.Fail("ntfs grant|deny|remove|inheritance|owner");
        }

        static void RunHv(WsLab lab, string v, string noun, Args a, ref WsResult r)
        {
            var s = lab.Server(a["host"] ?? "HV1"); var h = s == null ? null : s.Hv;
            if (h == null) { r = WsResult.Fail("Hyper-V isn't installed on " + (a["host"] ?? "HV1") + "."); return; }
            if (v == "vswitch")
            {
                if (noun == "new") { r = R(h.NewSwitch(a["name"] ?? a.P(0), a["type"] ?? "Internal", a["adapter"], a.Yes("management", true)), "Created the virtual switch " + (a["name"] ?? a.P(0)) + "."); return; }
                if (noun == "remove") { r = R(h.RemoveSwitch(a.P(0)), "Deleted the virtual switch " + a.P(0) + "."); return; }
                return;
            }
            if (noun == "new")
            {
                WsVm vm; string name = a["name"] ?? a.P(0);
                long mem = (long)a.Int("memorygb", 0) << 30, size = (long)a.Int("disksizegb", 0) << 30;
                string vhd = a["vhd"] ?? (size > 0 ? h.VhdPath + "\\" + name + ".vhdx" : null);
                r = R(h.NewVm(name, a.Int("generation", 2), mem, vhd, size, a["switch"], out vm), "Created the virtual machine " + name + "."); return;
            }
            var x = h.Vm(a.P(0));
            if (x == null) { r = WsResult.Fail("There is no virtual machine " + a.P(0) + " on " + s.Name + "."); return; }
            switch (noun)
            {
                case "start": r = R(h.Start(x), x.Name + " is running."); return;
                case "stop": r = R(h.Stop(x, a.Yes("turnoff", false) ? "turnoff" : a.Yes("save", false) ? "save" : "shutdown"), x.Name + " is " + x.State.ToLowerInvariant() + "."); return;
                case "checkpoint": r = R(h.Checkpoint(x, a.P(1)), "Created a checkpoint of " + x.Name + "."); return;
                case "restore": r = R(h.Restore(x, a.P(1)), "Applied the checkpoint " + a.P(1) + " to " + x.Name + "."); return;
                case "deletecheckpoint": r = R(h.RemoveCheckpoint(x, a.P(1), a.Yes("tree", false)), "Deleted the checkpoint " + a.P(1) + "."); return;
                case "set":
                    {
                        string e = null;
                        if (a.Has("nested") || a.Has("cpus")) e = h.SetProcessor(x, a.Int("cpus", 0), a.Has("nested") ? (bool?)a.Yes("nested", false) : null);
                        if (e == null && (a.Has("dynamic") || a.Has("memorygb"))) e = h.SetMemory(x, a.Has("dynamic") ? (bool?)a.Yes("dynamic", false) : null, (long)a.Int("memorygb", 0) << 30, (long)a.Int("mingb", 0) << 30, (long)a.Int("maxgb", 0) << 30);
                        if (e == null && a.Has("spoofing")) x.Nics.ForEach(n => n.Spoofing = a.Yes("spoofing", false) ? "On" : "Off");
                        if (e == null && a.Has("switch")) { if (h.Switch(a["switch"]) == null) e = "There is no virtual switch " + a["switch"] + "."; else x.Nics[0].Switch = h.Switch(a["switch"]).Name; }
                        if (e == null && a.Has("checkpointtype")) { var t = new[] { "Disabled", "Production", "ProductionOnly", "Standard" }.FirstOrDefault(k => WsText.Eq(k, a["checkpointtype"])); if (t == null) e = "Checkpoint type: Disabled, Production, ProductionOnly or Standard."; else x.CheckpointType = t; }
                        if (e == null && a.Has("startaction")) x.StartAction = a["startaction"];
                        if (e == null && a.Has("stopaction")) x.StopAction = a["stopaction"];
                        r = R(e, "Saved the settings of " + x.Name + "."); return;
                    }
                case "delete": if (x.State != "Off") { r = WsResult.Fail("Turn " + x.Name + " off before you delete it."); return; } h.Vms.Remove(x); r = WsResult.Done("Deleted " + x.Name + " (its virtual hard disks stay)."); return;
            }
            r = WsResult.Fail("vm new|start|stop|checkpoint|restore|deletecheckpoint|set|delete");
        }
    }
}
