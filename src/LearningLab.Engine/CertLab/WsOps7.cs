// Cert Lab - console actions for DFS, storage, and the Azure portal (practice): Azure Arc, Update Manager, Azure
// Network Adapter, Azure VMs, Microsoft Entra Connect / Cloud Sync and Azure File Sync. Describe() turns any console
// line into the words the "show me" panel uses.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsOps
    {
        static void RunDfs(WsLab lab, string v, string noun, Args a, ref WsResult r)
        {
            if (v == "dfsn")
            {
                if (noun == "root")
                {
                    string path = a["path"] ?? a.P(0), target = a["target"];
                    var t = lab.Locate(target ?? "");
                    if (t.Error != null) { r = WsResult.Fail("The root target must be an existing share: " + t.Error); return; }
                    var ts = lab.Server(t.Server);
                    if (!ts.Has("FS-DFS-Namespace")) { r = WsResult.Fail("The DFS Namespaces role service isn't installed on " + ts.Name + "."); return; }
                    var ns = lab.Namespace(path);
                    if (ns == null) { ns = new DfsNamespace { Path = path, Type = a["type"] ?? "DomainV2" }; lab.Namespaces.Add(ns); }
                    ns.Roots.Add(new DfsTarget { Path = target });
                    r = WsResult.Done("The namespace " + path + " has the root target " + target + "."); return;
                }
                if (noun == "folder")
                {
                    string path = a["path"] ?? a.P(0), target = a["target"];
                    var ns = lab.Namespaces.FirstOrDefault(n => path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase));
                    if (ns == null) { r = WsResult.Fail("There is no namespace for " + path + "."); return; }
                    var t = lab.Locate(target ?? "");
                    if (t.Error != null) { r = WsResult.Fail("A folder target must be an existing share: " + t.Error); return; }
                    string fn = path.Substring(ns.Path.Length + 1);
                    var f = ns.Folder(fn); if (f == null) { f = new DfsFolder { Name = fn }; ns.Folders.Add(f); }
                    if (f.Targets.Any(x => WsText.Eq(x.Path, target))) { r = WsResult.Fail("That folder target already exists."); return; }
                    f.Targets.Add(new DfsTarget { Path = target, State = a.Yes("online", true) ? "Online" : "Offline" });
                    r = WsResult.Done("The folder " + path + " points to " + target + "."); return;
                }
                if (noun == "abe") { var ns = lab.Namespace(a.P(0)); if (ns == null) { r = WsResult.Fail("There is no namespace " + a.P(0) + "."); return; } ns.Abe = a.Yes("on", true); r = WsResult.Done("Access-based enumeration is " + (ns.Abe ? "on" : "off") + "."); return; }
                return;
            }
            // dfsr
            switch (noun)
            {
                case "group":
                    {
                        string name = a["name"] ?? a.P(0);
                        if (lab.DfsrGroup(name) != null) { r = WsResult.Fail("A replication group named " + name + " already exists."); return; }
                        var members = (a["members"] ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        foreach (var m in members) { var s = lab.Server(m); if (s == null || !s.Has("FS-DFS-Replication")) { r = WsResult.Fail("DFS Replication isn't installed on " + m + "."); return; } }
                        var g = new DfsrGroup { Name = name, Changed = lab.Now };
                        g.Members.AddRange(members.Select(m => lab.Server(m).Name));
                        string folder = a["folder"];
                        if (folder != null) g.Folders.Add(folder);
                        foreach (var x in g.Members) foreach (var y in g.Members) if (x != y) g.Connections.Add(new[] { x, y });
                        foreach (var m in g.Members) foreach (var f in g.Folders) g.Memberships.Add(new DfsrMember { Server = m, Folder = f });
                        lab.DfsrGroups.Add(g);
                        r = WsResult.Done("Created the replication group " + name + " (full mesh)."); return;
                    }
                case "membership":
                    {
                        var g = lab.DfsrGroup(a["group"]);
                        if (g == null) { r = WsResult.Fail("There is no replication group " + a["group"] + "."); return; }
                        var m = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, a["server"]) && WsText.Eq(x.Folder, a["folder"]));
                        if (m == null) { r = WsResult.Fail(a["server"] + " isn't a member for the folder " + a["folder"] + "."); return; }
                        var s = lab.Server(m.Server);
                        if (a["path"] != null && s.M.FindFile(a["path"]) == null) s.M.MakeDir(a["path"], lab.Now);
                        if (a["path"] != null) m.ContentPath = a["path"];
                        if (a.Has("primary")) { if (a.Yes("primary", false)) g.Memberships.Where(x => WsText.Eq(x.Folder, m.Folder)).ToList().ForEach(x => x.Primary = false); m.Primary = a.Yes("primary", false); }
                        if (a.Has("readonly")) m.ReadOnly = a.Yes("readonly", false);
                        g.Changed = lab.Now;
                        r = WsResult.Done("Saved " + m.Server + "'s membership."); return;
                    }
                case "poll": { var s = lab.Server(a.P(0)); if (s == null) { r = WsResult.Fail("There is no server " + a.P(0) + "."); return; } lab.DfsrPoll(s.Name); r = WsResult.Done(s.Name + " read the DFS Replication configuration from Active Directory."); return; }
            }
        }

        static void RunStorage(WsLab lab, string noun, Args a, ref WsResult r)
        {
            var s = lab.Server(a["server"] ?? "FS1");
            if (s == null) { r = WsResult.Fail("There is no server " + a["server"] + "."); return; }
            switch (noun)
            {
                case "pool": r = R(s.NewPool(a["name"] ?? a.P(0), (a["disks"] ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList()), "Created the storage pool."); return;
                case "vdisk":
                    {
                        WsVDisk vd;
                        string e = s.NewVDisk(a["pool"], a["name"] ?? a.P(0), a["resiliency"] ?? "Simple", a.Int("copies", 2), a["provisioning"] ?? "Fixed", (long)a.Int("sizegb", 0) << 30, a.Yes("max", false), out vd);
                        if (e == null && a["letter"] != null)
                        {
                            s.Disk(vd.DiskNumber).Style = "GPT";
                            WsPart p; e = s.NewPartition(vd.DiskNumber, 0, true, a["letter"], out p);
                            if (e == null) e = s.Format(a["letter"], a["fs"] ?? "ReFS", a["label"] ?? vd.Name);
                        }
                        r = R(e, "Created the virtual disk " + (a["name"] ?? a.P(0)) + (a["letter"] != null ? " as " + a["letter"] + ":" : "") + "."); return;
                    }
                case "dedup": r = R(WsText.Eq(a.P(1), "off") ? (s.Dedup.Remove((a.P(0) ?? "").TrimEnd(':')) ? null : "Deduplication isn't on for that volume.") : s.EnableDedup(a.P(0) ?? "", a["usage"]), "Data Deduplication " + (WsText.Eq(a.P(1), "off") ? "is off" : "is on") + " for " + a.P(0) + "."); return;
                case "optimize": s.Optimize((a.P(0) ?? "").TrimEnd(':')); r = WsResult.Done("Ran an optimization job on " + a.P(0) + "."); return;
            }
        }
    }
}
