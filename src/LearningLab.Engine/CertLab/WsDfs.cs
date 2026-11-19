// Cert Lab - DFS Namespaces and DFS Replication in the AZ-800 network. A domain-based namespace (\\corp.example\Files)
// has root targets (shares on namespace servers) and folders with one or more folder targets; a client is referred to
// a target in its own site first, then the others, skipping offline targets. A replication group copies replicated
// folders between members over its connections, once each member has read the configuration from Active Directory
// (it polls every 60 minutes, or at once with Update-DfsrConfigurationFromAD); the primary member's copy wins the first
// sync. SYSVOL travels between the DCs the same way. Deleting a file doesn't replicate in Cert Lab.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class DfsTarget { public string Path = "", State = "Online", Priority = "SiteCostNormal"; }
    public class DfsFolder { public string Name = "", Description = ""; public readonly List<DfsTarget> Targets = new List<DfsTarget>(); }
    public class DfsNamespace
    {
        public string Path = "", Type = "DomainV2", Description = ""; public bool Abe;
        public readonly List<DfsTarget> Roots = new List<DfsTarget>();
        public readonly List<DfsFolder> Folders = new List<DfsFolder>();
        public DfsFolder Folder(string n) { return Folders.FirstOrDefault(f => WsText.Eq(f.Name, n.Trim('\\'))); }
    }
    public class DfsrMember { public string Server = "", ContentPath = "", Folder = ""; public bool Primary, ReadOnly, Enabled = true; }
    public class DfsrGroup
    {
        public string Name = "", Description = ""; public DateTime Changed;
        public readonly List<string> Members = new List<string>(), Folders = new List<string>();
        public readonly List<DfsrMember> Memberships = new List<DfsrMember>();
        public readonly List<string[]> Connections = new List<string[]>();   // from, to
        public readonly HashSet<string> InitialDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "server|folder" after its first sync
    }

    public partial class WsLab
    {
        public readonly List<DfsNamespace> Namespaces = new List<DfsNamespace>();
        public readonly List<DfsrGroup> DfsrGroups = new List<DfsrGroup>();
        public readonly Dictionary<string, DateTime> DfsrPolled = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public DfsNamespace Namespace(string path) { return Namespaces.FirstOrDefault(n => WsText.Eq(NormUnc(n.Path), NormUnc(path))); }
        public DfsrGroup DfsrGroup(string name) { return DfsrGroups.FirstOrDefault(g => WsText.Eq(g.Name, name)); }
        string NormUnc(string p)
        {
            var parts = (p ?? "").Trim().TrimStart('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0 && (WsText.Eq(parts[0], NetBios) || WsText.Eq(parts[0], DomainDns))) parts[0] = DomainDns;
            return "\\\\" + string.Join("\\", parts.ToArray());
        }

        /// <summary>A namespace path turned into the folder target a client in site would be referred to (null: not a namespace path).</summary>
        public string DfsResolve(string path) { return DfsResolve(path, null); }
        public string DfsResolve(string path, string clientSite)
        {
            var parts = NormUnc(path).TrimStart('\\').Split('\\');
            if (parts.Length >= 2 && WsText.Eq(parts[0], DomainDns) && (WsText.Eq(parts[1], "SYSVOL") || WsText.Eq(parts[1], "NETLOGON")))
            {
                var dc = DcNames.FirstOrDefault(DcUp);
                return dc == null ? null : "\\\\" + dc + "\\" + string.Join("\\", parts.Skip(1).ToArray());
            }
            if (parts.Length < 2) return null;
            var ns = Namespace("\\\\" + parts[0] + "\\" + parts[1]);
            if (ns == null) return null;
            string rest = parts.Length > 2 ? string.Join("\\", parts.Skip(3).ToArray()) : "";
            var folder = parts.Length > 2 ? ns.Folder(parts[2]) : null;
            var targets = folder != null ? folder.Targets : ns.Roots;
            if (folder == null) rest = string.Join("\\", parts.Skip(2).ToArray());
            var t = Referral(targets, clientSite).FirstOrDefault();
            if (t == null) return null;
            return t.Path.TrimEnd('\\') + (rest.Length > 0 ? "\\" + rest : "");
        }
        /// <summary>The order a client gets the targets in: online ones, its own site first, then by site cost (Cert Lab: HQ, Branch).</summary>
        public List<DfsTarget> Referral(List<DfsTarget> targets, string clientSite)
        {
            Func<DfsTarget, int> rank = t =>
            {
                var s = Server(t.Path.TrimStart('\\').Split('\\')[0]);
                string site = s == null ? null : SiteOfIp(s.M.Ip);
                int r = t.Priority == "GlobalHigh" ? 0 : t.Priority == "GlobalLow" ? 400 : 100;
                if (clientSite != null && !WsText.Eq(site, clientSite)) r += 100;
                if (s == null || !s.Running) r += 1000;
                return r;
            };
            return targets.Where(t => t.State == "Online").OrderBy(rank).ToList();
        }

        // ------------------------------------------------------------ DFS Replication
        public void DfsrPoll(string server) { DfsrPolled[server] = Now; DfsrSyncAll(); }
        public void DfsrSyncAll()
        {
            foreach (var g in DfsrGroups)
                foreach (var c in g.Connections)
                    foreach (var f in g.Folders) DfsrCopy(g, f, c[0], c[1]);
        }
        void DfsrCopy(DfsrGroup g, string folder, string from, string to)
        {
            var a = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, from) && WsText.Eq(x.Folder, folder));
            var b = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, to) && WsText.Eq(x.Folder, folder));
            if (a == null || b == null || a.ContentPath.Length == 0 || b.ContentPath.Length == 0 || !a.Enabled || !b.Enabled || b.ReadOnly && false) return;
            var sa = Server(from); var sb = Server(to);
            if (sa == null || sb == null || !sa.SvcRunning("DFSR") || !sb.SvcRunning("DFSR")) return;
            DateTime pa, pb;
            if (!DfsrPolled.TryGetValue(sa.Name, out pa) || !DfsrPolled.TryGetValue(sb.Name, out pb) || pa < g.Changed || pb < g.Changed) return;
            // the first sync: a non-primary member waits for a member that has finished its own first sync (or is primary)
            if (!a.Primary && !g.InitialDone.Contains(sa.Name + "|" + folder)) return;
            var src = sa.M.FindFile(a.ContentPath);
            var dst = sb.M.FindFile(b.ContentPath) ?? sb.M.MakeDir(b.ContentPath, Now);
            if (src == null) return;
            int n = CopyTree(src, dst, !g.InitialDone.Contains(sb.Name + "|" + folder));
            g.InitialDone.Add(sa.Name + "|" + folder);
            if (!g.InitialDone.Contains(sb.Name + "|" + folder)) { g.InitialDone.Add(sb.Name + "|" + folder); Note("DFS Replication: " + sb.Name + " finished the initial sync of " + folder + " from " + sa.Name + "."); }
            else if (n > 0) Note("DFS Replication: " + n + " file(s) of " + folder + " replicated from " + sa.Name + " to " + sb.Name + ".");
        }
        /// <summary>Copies files that are missing or older on the destination (initial: the source's version always wins).</summary>
        public static int CopyTree(WinFile src, WinFile dst, bool initial)
        {
            int n = 0;
            foreach (var k in src.Kids.Values.ToList())
            {
                var have = dst.Kid(k.Name);
                if (k.IsDir)
                {
                    if (have == null) have = dst.Add(new WinFile { Name = k.Name, IsDir = true, Attr = k.Attr, Created = k.Created, Modified = k.Modified, Accessed = k.Accessed });
                    n += CopyTree(k, have, initial);
                    continue;
                }
                if (have != null && !initial && have.Modified >= k.Modified) continue;
                if (have != null && have.Text == k.Text && have.FakeSize == k.FakeSize && have.Modified == k.Modified) continue;
                dst.Kids.Remove(k.Name);
                dst.Add(new WinFile { Name = k.Name, Text = k.Text, Bytes = k.Bytes, Encoding = k.Encoding, Attr = k.Attr, Created = k.Created, Modified = k.Modified, Accessed = k.Accessed, FakeSize = k.FakeSize, Owner = k.Owner });
                n++;
            }
            return n;
        }
        /// <summary>SYSVOL between the DCs (DFS Replication of the SYSVOL share): newer files win.</summary>
        public void SysvolSync()
        {
            foreach (var a in DcNames.Where(DcUp).ToList())
                foreach (var b in DcNames.Where(x => !WsText.Eq(x, a) && DcUp(x)).ToList())
                {
                    var src = M(a).FindFile("C:\\Windows\\SYSVOL\\sysvol"); var dst = M(b).FindFile("C:\\Windows\\SYSVOL\\sysvol");
                    if (src != null && dst != null) CopyTree(src, dst, false);
                }
        }
        public void ScheduleDfsr()
        {
            At("dfsr:sync", Now.AddMinutes(15), () => { SysvolSync(); DfsrSyncAll(); ScheduleDfsr(); }, false);
            foreach (var s in Servers.Values.Where(x => x.SvcRunning("DFSR")))
            {
                string n = s.Name;
                if (!Due("dfsr:poll:" + n).HasValue) At("dfsr:poll:" + n, Now.AddMinutes(60), () => { DfsrPolled[n] = Now; DfsrSyncAll(); Cancel("dfsr:poll:" + n); }, false);
            }
        }
    }
}
