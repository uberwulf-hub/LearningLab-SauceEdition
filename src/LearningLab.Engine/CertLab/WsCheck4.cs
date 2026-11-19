// Cert Lab - the roots of the AZ-800 checks for files, storage, services and the Azure side.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsCheck
    {
        static readonly string[] RightNames = { "Read", "Write", "Execute", "Delete", "DeleteChildren", "ReadPermissions", "ChangePermissions", "TakeOwnership" };
        static List<string> Rights(int m) { var l = new List<string>(); for (int b = 0; b < 8; b++) if ((m & (1 << b)) != 0) l.Add(RightNames[b]); if ((m & NtfsRights.Modify) == NtfsRights.Modify) l.Add("Modify"); if (m == NtfsRights.Full) l.Add("FullControl"); return l; }

        static partial void RootFilesPart(WsLab lab, WsServer srv, string root, Dictionary<string, string> a, string ix, ref WsVal v)
        {
            switch (root)
            {
                case "share":
                    {
                        string key = ix ?? A(a, "name", 1);
                        var s = srv; if (s == null && key != null && key.Contains("\\")) { s = lab.Server(key.Split('\\')[0]); key = key.Substring(key.IndexOf('\\') + 1); }
                        var sh = s == null ? null : s.Share(key); if (sh == null) return;
                        v = WsVal.O(sh.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "path": return WsVal.T(sh.Path);
                                case "access": if (i == null) return WsVal.L(sh.Access.Select(x => x.Trustee + ":" + (x.Allow ? "" : "Deny ") + x.Right)); { var e = sh.Access.FirstOrDefault(x => WsText.Eq(x.Trustee, i) || WsText.Eq(WsLab.StripDomain(x.Trustee), i)); return WsVal.T(e == null ? "None" : (e.Allow ? "" : "Deny ") + e.Right); }
                                case "abe": return Yn(sh.FolderEnumerationMode == "AccessBased");
                                case "encrypt": return Yn(sh.EncryptData);
                                case "description": return WsVal.T(sh.Description);
                            }
                            return null;
                        });
                        return;
                    }
                case "ntfs":
                    {
                        var f = srv == null ? null : srv.M.FindFile(A(a, "path", 1) ?? ""); if (f == null) return;
                        var acl = srv.AclOf(f, false);
                        v = WsVal.O(f.FullPath, (m, i) =>
                        {
                            switch (m)
                            {
                                case "aces": return WsVal.L(srv.EffectiveAces(f).Where(x => !x.Inherited).Select(x => x.Trustee + ":" + (x.Allow ? "" : "Deny ") + NtfsRights.Short(x.Rights)));
                                case "inherited": return WsVal.L(srv.EffectiveAces(f).Where(x => x.Inherited).Select(x => x.Trustee + ":" + (x.Allow ? "" : "Deny ") + NtfsRights.Short(x.Rights)));
                                case "all": return WsVal.L(srv.EffectiveAces(f).Select(x => x.Trustee + ":" + (x.Allow ? "" : "Deny ") + NtfsRights.Short(x.Rights)));
                                case "protected": return Yn(acl != null && acl.Protected);
                                case "owner": return WsVal.T(acl == null ? "BUILTIN\\Administrators" : acl.Owner);
                            }
                            return null;
                        });
                        return;
                    }
                case "access":
                    {
                        var r = lab.Access(A(a, "user", 0), A(a, "path", 1));
                        if (r.Error != null) { v = WsVal.T("Error: " + r.Error); return; }
                        v = WsVal.O(NtfsRights.Words(r.Effective), (m, i) => m == "rights" ? WsVal.L(Rights(r.Effective)) : m == "ntfs" ? WsVal.T(NtfsRights.Words(r.Ntfs)) : m == "share" ? WsVal.T(r.Share < 0 ? "" : NtfsRights.Words(r.Share)) : m == "server" ? WsVal.T(r.Server) : m == "localpath" ? WsVal.T(r.LocalPath) : null);
                        return;
                    }
                case "file":
                    {
                        var f = srv == null ? null : srv.M.FindFile(A(a, "path", 1) ?? ""); if (f == null) return;
                        v = WsVal.O(f.FullPath, (m, i) => m == "tiered" ? Yn(WsAzure.Tiered(f)) : m == "kids" ? WsVal.L(f.Kids.Keys) : m == "size" ? N(f.FakeSize) : null);
                        return;
                    }
                case "pool": { var s = srv ?? lab.Server("FS1"); var p = s.Pool(ix ?? A(a, "name", 1)); if (p != null) v = WsVal.O(p.Name, (m, i) => m == "disks" ? N(p.Disks.Count) : m == "sizegb" ? N(s.PoolSize(p) >> 30) : null); return; }
                case "vdisk":
                    {
                        var s = srv ?? lab.Server("FS1"); var vd = s.VDisk(ix ?? A(a, "name", 1)); if (vd == null) return;
                        v = WsVal.O(vd.Name, (m, i) => m == "resiliency" ? WsVal.T(vd.Resiliency) : m == "copies" ? N(vd.Copies) : m == "provisioning" ? WsVal.T(vd.Provisioning) : m == "sizegb" ? N(vd.Size >> 30) : m == "pool" ? WsVal.T(vd.Pool) : m == "letter" ? WsVal.T(s.Disk(vd.DiskNumber).Parts.Select(p => p.Letter).FirstOrDefault() ?? "") : null);
                        return;
                    }
                case "volume":
                    {
                        string letter = (A(a, "letter", 1) ?? "").TrimEnd(':'); var p = srv == null ? null : srv.PartOf(letter); if (p == null) return;
                        v = WsVal.O(letter, (m, i) => m == "fs" || m == "filesystem" ? WsVal.T(p.FileSystem) : m == "label" ? WsVal.T(p.Label) : m == "sizegb" ? N(p.Size >> 30) : null);
                        return;
                    }
                case "dedup":
                    {
                        WsDedup d; if (srv == null || !srv.Dedup.TryGetValue((A(a, "volume", 1) ?? "").TrimEnd(':'), out d)) { v = WsVal.O("Off", (m, i) => m == "enabled" ? Yn(false) : null); return; }
                        v = WsVal.O("On", (m, i) => m == "enabled" ? Yn(d.Enabled) : m == "usage" ? WsVal.T(d.UsageType) : m == "minage" ? N(d.MinAgeDays) : m == "savedgb" ? WsVal.T((d.Saved / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture)) : m == "optimized" ? N(d.Optimized) : null);
                        return;
                    }
                case "feature": { var f = srv == null ? null : srv.M.FindFeature(A(a, "name", 1) ?? ""); if (f != null) v = WsVal.T(f.State); return; }
                case "service": { var sv = srv == null ? null : srv.M.Svc(A(a, "name", 1) ?? ""); v = WsVal.O(sv == null ? "Missing" : sv.Status, (m, i) => m == "starttype" ? WsVal.T(sv == null ? "" : sv.StartType) : null); return; }
                case "namespace":
                    {
                        var ns = lab.Namespace(ix ?? ""); if (ns == null) return;
                        v = WsVal.O(ns.Path, (m, i) => m == "type" ? WsVal.T(ns.Type) : m == "roots" ? WsVal.L(ns.Roots.Select(t => t.Path)) : m == "folders" ? WsVal.L(ns.Folders.Select(f => f.Name)) : m == "abe" ? Yn(ns.Abe) : m == "targets" ? (ns.Folder(i ?? "") == null ? null : WsVal.L(ns.Folder(i).Targets.Select(t => t.Path))) : null);
                        return;
                    }
                case "dfsresolve": { var t = lab.DfsResolve(A(a, "path", 0), A(a, "site", 1)); v = WsVal.T(t ?? ""); return; }
                case "dfsr":
                    {
                        var g = lab.DfsrGroup(ix ?? ""); if (g == null) return;
                        v = WsVal.O(g.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "members": return WsVal.L(g.Members);
                                case "folders": return WsVal.L(g.Folders);
                                case "connections": return WsVal.L(g.Connections.Select(c => c[0] + ">" + c[1]));
                                case "primary": return WsVal.L(g.Memberships.Where(x => x.Primary).Select(x => x.Server));
                                case "contentpath": { var mm = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, i)); return mm == null ? null : WsVal.T(mm.ContentPath); }
                                case "readonly": { var mm = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, i)); return mm == null ? null : Yn(mm.ReadOnly); }
                                case "synced": return WsVal.L(g.InitialDone.Select(x => x.Split('|')[0]).Distinct());
                            }
                            return null;
                        });
                        return;
                    }
                case "hotfix": { var m0 = srv == null ? null : srv.M; v = m0 == null ? null : WsVal.L(m0.Hotfixes.Select(h => h.Id)); return; }
            }
            RootAzure(lab, srv, root, a, ix, ref v);
        }
        static partial void RootAzure(WsLab lab, WsServer srv, string root, Dictionary<string, string> a, string ix, ref WsVal v);
    }
}
