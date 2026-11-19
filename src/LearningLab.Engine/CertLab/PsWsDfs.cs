// Cert Lab - the DFSN and DFSR modules (DFS Namespaces and DFS Replication) and the ADSync module (Microsoft Entra
// Connect Sync's scheduler, on the server where it is installed).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static Func<PsMachine, bool> HasDfsn = m => m.Ws != null && (m.FeatureInstalled("FS-DFS-Namespace") || m.IsServer && m.Ws.Dirs.ContainsKey(m.Name));
        static Func<PsMachine, bool> HasDfsr = m => m.Ws != null && (m.FeatureInstalled("FS-DFS-Replication") || m.Ws.Dirs.ContainsKey(m.Name));
        static CmdletDef DfsCmd(string name, string mod, Func<PsMachine, bool> av, string spec, Action<CmdCtx, object> run) { var d = Cdxml(Add(name, mod, spec, run)); d.Available = av; return d; }
        static void DfsOk(CmdCtx c, WsResult r) { if (!r.Ok) throw c.Fail(r.Text, "Dfs", "InvalidOperation", null, "Microsoft.Management.Infrastructure.CimException"); }
        static PsObj NsObj(string kind, params object[] kv) { var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#" + kind, "Microsoft.Management.Infrastructure.CimInstance"); for (int i = 0; i + 1 < kv.Length; i += 2) p.Val((string)kv[i], kv[i + 1] is bool ? "bool" : "string", kv[i + 1]); return p; }
        static string Qd(string s) { return "\"" + (s ?? "").Replace("\"", "").Replace("\\", "\\\\") + "\""; }   // inside quotes a backslash pair is an escape (EpOps.Tokens)

        static partial void RegWsDfs()
        {
            DfsCmd("New-DfsnRoot", "DFSN", HasDfsn, "Path:string@0 !; TargetPath:string@1 !; Type:string ! set=Standalone|DomainV1|DomainV2; Description:string; EnableAccessBasedEnumeration:bool", (c, o) =>
            {
                DfsOk(c, WsOps.Run(Lab(c), "dfsn root path=" + Qd(c.Str("Path")) + " target=" + Qd(c.Str("TargetPath")) + " type=" + c.Str("Type")));
                var ns = Lab(c).Namespace(c.Str("Path")); if (c.Has("EnableAccessBasedEnumeration")) ns.Abe = PsConv.ToBool(c.Get("EnableAccessBasedEnumeration")); ns.Description = c.Str("Description") ?? "";
                c.Out(NsObj("DfsNamespace", "Path", ns.Path, "Type", ns.Type == "DomainV2" ? "Domain V2" : ns.Type, "Properties", "{}", "TimeToLiveSec", "300", "State", "Online", "Description", ns.Description));
            });
            DfsCmd("Get-DfsnRoot", "DFSN", HasDfsn, "Path:string[]@0; Domain:string", (c, o) =>
            {
                foreach (var ns in Lab(c).Namespaces.Where(x => !c.Has("Path") || c.Strs("Path").Any(p => WsText.Like(x.Path, p)))) c.Out(NsObj("DfsNamespace", "Path", ns.Path, "Type", ns.Type == "DomainV2" ? "Domain V2" : ns.Type, "Properties", ns.Abe ? "{Site Costing, AccessBased Enumeration}" : "{Site Costing}", "TimeToLiveSec", "300", "State", "Online", "Description", ns.Description));
            });
            DfsCmd("Set-DfsnRoot", "DFSN", HasDfsn, "Path:string@0 !; Description:string; EnableAccessBasedEnumeration:bool; EnableSiteCosting:bool; TimeToLiveSec:int; State:string set=Online|Offline", (c, o) =>
            {
                var ns = Lab(c).Namespace(c.Str("Path"));
                if (ns == null) throw c.Fail("The namespace " + c.Str("Path") + " could not be found.", "Dfs", "ObjectNotFound", null, "Microsoft.Management.Infrastructure.CimException");
                if (c.Has("EnableAccessBasedEnumeration")) ns.Abe = PsConv.ToBool(c.Get("EnableAccessBasedEnumeration"));
                if (c.Has("Description")) ns.Description = c.Str("Description") ?? "";
                c.Out(NsObj("DfsNamespace", "Path", ns.Path, "Type", ns.Type == "DomainV2" ? "Domain V2" : ns.Type, "Properties", ns.Abe ? "{Site Costing, AccessBased Enumeration}" : "{Site Costing}", "TimeToLiveSec", "300", "State", "Online", "Description", ns.Description));
            });
            DfsCmd("New-DfsnFolder", "DFSN", HasDfsn, "Path:string@0 !; TargetPath:string@1 !; Description:string; State:string set=Online|Offline", (c, o) =>
            {
                DfsOk(c, WsOps.Run(Lab(c), "dfsn folder path=" + Qd(c.Str("Path")) + " target=" + Qd(c.Str("TargetPath"))));
                c.Out(NsObj("DfsNamespaceFolder", "Path", c.Str("Path"), "State", "Online", "TimeToLiveSec", "1800", "Description", c.Str("Description") ?? ""));
            });
            DfsCmd("New-DfsnFolderTarget", "DFSN", HasDfsn, "Path:string@0 !; TargetPath:string@1 !; State:string set=Online|Offline; ReferralPriorityClass:string", (c, o) =>
            {
                DfsOk(c, WsOps.Run(Lab(c), "dfsn folder path=" + Qd(c.Str("Path")) + " target=" + Qd(c.Str("TargetPath")) + " online=" + (c.Str("State") == "Offline" ? "no" : "yes")));
                c.Out(NsObj("DfsNamespaceFolderTarget", "Path", c.Str("Path"), "TargetPath", c.Str("TargetPath"), "State", c.Str("State") ?? "Online", "ReferralPriorityClass", "sitecost-normal", "ReferralPriorityRank", "0"));
            });
            DfsCmd("Get-DfsnFolder", "DFSN", HasDfsn, "Path:string@0 !", (c, o) =>
            {
                var lab = Lab(c); string path = c.Str("Path");
                foreach (var ns in lab.Namespaces) foreach (var f in ns.Folders) { string full = ns.Path + "\\" + f.Name; if (WsText.Like(full, path) || WsText.Like(full, path.TrimEnd('*', '\\') + "\\*")) c.Out(NsObj("DfsNamespaceFolder", "Path", full, "State", "Online", "TimeToLiveSec", "1800", "Description", f.Description)); }
            });
            DfsCmd("Get-DfsnFolderTarget", "DFSN", HasDfsn, "Path:string@0 !; TargetPath:string", (c, o) =>
            {
                var lab = Lab(c); string path = c.Str("Path");
                var ns = lab.Namespaces.FirstOrDefault(n => path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase));
                var f = ns == null ? null : ns.Folder(path.Substring(ns.Path.Length + 1));
                if (f == null) throw c.Fail("Cannot get DFS folder properties on \"" + path + "\": the element was not found.", "Cannot get DFS folder properites", "ObjectNotFound", path, "Microsoft.Management.Infrastructure.CimException");
                foreach (var t in f.Targets.Where(x => !c.Has("TargetPath") || WsText.Eq(x.Path, c.Str("TargetPath")))) c.Out(NsObj("DfsNamespaceFolderTarget", "Path", path, "TargetPath", t.Path, "State", t.State, "ReferralPriorityClass", t.Priority == "SiteCostNormal" ? "sitecost-normal" : t.Priority, "ReferralPriorityRank", "0"));
            });
            DfsCmd("Set-DfsnFolderTarget", "DFSN", HasDfsn, "Path:string@0 !; TargetPath:string@1 !; State:string set=Online|Offline; ReferralPriorityClass:string set=GlobalHigh|SiteCostHigh|SiteCostNormal|SiteCostLow|GlobalLow", (c, o) =>
            {
                var lab = Lab(c); string path = c.Str("Path");
                var ns = lab.Namespaces.FirstOrDefault(n => path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase));
                var f = ns == null ? null : ns.Folder(path.Substring(ns.Path.Length + 1)); var t = f == null ? null : f.Targets.FirstOrDefault(x => WsText.Eq(x.Path, c.Str("TargetPath")));
                if (t == null) throw c.Fail("The folder target " + c.Str("TargetPath") + " of " + path + " was not found.", "Dfs", "ObjectNotFound", path, "Microsoft.Management.Infrastructure.CimException");
                if (c.Has("State")) t.State = c.Str("State"); if (c.Has("ReferralPriorityClass")) t.Priority = c.Str("ReferralPriorityClass");
            });
            DfsCmd("Remove-DfsnFolder", "DFSN", HasDfsn, "Path:string@0 !; Force:switch", (c, o) =>
            {
                var lab = Lab(c); string path = c.Str("Path");
                var ns = lab.Namespaces.FirstOrDefault(n => path.StartsWith(n.Path + "\\", StringComparison.OrdinalIgnoreCase));
                if (ns == null || ns.Folders.RemoveAll(f => WsText.Eq(ns.Path + "\\" + f.Name, path)) == 0) throw c.Fail("The DFS folder " + path + " was not found.", "Dfs", "ObjectNotFound", path, "Microsoft.Management.Infrastructure.CimException");
            });
            RegWsDfsr();
        }
    }
}
