// Cert Lab - the roots of the AZ-800 checks for the directory and Group Policy: user, group, computer, ou, object,
// deleted, site, subnet, sitelink, dc, fsmo, domain, forest, trust, pso, gmsa, gpo, wmifilter, rsop, applied,
// centralstore, replicated.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsCheck
    {
        static WsVal Root(WsLab lab, string dc, string root, Dictionary<string, string> a, string ix)
        {
            var d = lab.Dir(dc) ?? lab.Main;
            switch (root.ToLowerInvariant())
            {
                case "user": case "group": case "computer": case "object": case "gmsa":
                    { var o = d.Find(ix, root == "object" ? null : root == "gmsa" ? "msDS-GroupManagedServiceAccount" : root); return o == null ? null : Obj(lab, d, o); }
                case "ou": { var o = d.ByDn(WsOps.ResolvePath(lab, ix)) ?? d.Live.FirstOrDefault(x => x.Is("organizationalUnit") && WsText.Eq(x.Name, ix)); return o == null ? null : Obj(lab, d, o); }
                case "deleted": { var o = d.Objs.Values.FirstOrDefault(x => x.Deleted && (WsText.Eq(x.Sam, ix) || WsText.Eq(x.Name, ix))); return o == null ? null : Obj(lab, d, o); }
                case "site": { var o = lab.Sites(d).FirstOrDefault(x => WsText.Eq(x.Name, ix)); return o == null ? null : Obj(lab, d, o); }
                case "subnet": { var o = lab.Subnets(d).FirstOrDefault(x => WsText.Eq(x.Name, ix)); return o == null ? null : Obj(lab, d, o); }
                case "sitelink": { var o = lab.SiteLinks(d).FirstOrDefault(x => WsText.Eq(x.Name, ix)); return o == null ? null : Obj(lab, d, o); }
                case "pso": { var o = d.Children(lab.PsoContainer).FirstOrDefault(x => WsText.Eq(x.Name, ix)); return o == null ? null : Obj(lab, d, o); }
                case "dc":
                    {
                        var s = lab.Server(ix); if (s == null || !lab.Dirs.ContainsKey(s.Name)) return null;
                        return WsVal.O(s.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "site": return WsVal.T(lab.SiteOfDc(s.Name) ?? "");
                                case "roles": return WsVal.L(lab.Fsmo.Where(kv => WsText.Eq(kv.Value, s.Name)).Select(kv => kv.Key));
                                case "running": return WsVal.T(WsText.Yn(lab.DcUp(s.Name)));
                                case "gc": { var so = lab.ServerObj(d, s.Name); return WsVal.T(WsText.Yn(so != null && so.Get("options") == "1")); }
                                case "readonly": return WsVal.T(WsText.Yn(lab.Server(s.Name).M.FindFile("C:\\Windows\\NTDS\\rodc.flag") != null));
                            }
                            return null;
                        });
                    }
                case "fsmo": return WsVal.O("", (m, i) => { var r = WsLab.FsmoRoles.FirstOrDefault(x => WsText.Eq(x, m)); return r == null ? null : WsVal.T(lab.Fsmo[r]); });
                case "domain": { var o = d.ByDn(lab.DomainDn); return o == null ? null : Obj(lab, d, o); }
                case "forest": return WsVal.O(lab.DomainDns, (m, i) => m == "upnsuffixes" ? WsVal.L(lab.UpnSuffixes) : m == "recyclebin" ? WsVal.T(WsText.Yn(lab.RecycleBin)) : m == "mode" ? WsVal.T(d.ByDn(lab.DomainDn).Get("forestMode")) : null);
                case "trust":
                    {
                        var t = lab.Trusts.FirstOrDefault(x => WsText.Eq(x.Name, ix)); if (t == null) return null;
                        return WsVal.O(t.Name, (m, i) => m == "type" ? WsVal.T(t.Type) : m == "direction" ? WsVal.T(t.Direction) : m == "selective" ? WsVal.T(WsText.Yn(t.Selective)) : m == "transitive" ? WsVal.T(WsText.Yn(t.Transitive)) : null);
                    }
                case "gpo": { var g = lab.FindGpo(d, ix); return g == null ? null : Gpo(lab, d, g); }
                case "wmifilter": { var w = lab.FindWmi(d, ix); return w == null ? null : WsVal.O(w.Get("msWMI-Name"), (m, i) => m == "query" ? WsVal.T(w.Get("query")) : m == "description" ? WsVal.T(w.Get("description")) : null); }
                case "rsop":
                    {
                        var comp = A(a, "computer", 0) == null ? null : d.Find(A(a, "computer", 0), "computer");
                        var user = A(a, "user", 1) == null ? null : d.Find(A(a, "user", 1), "user");
                        if (A(a, "computer", 0) != null && comp == null || A(a, "user", 1) != null && user == null) return null;
                        return Rsop(lab.Rsop(d.Dc, comp, user, A(a, "site", 9)));
                    }
                case "applied":
                    {
                        string m0 = A(a, "computer", 0), u0 = A(a, "user", 1);
                        RsopResult r;
                        if (u0 != null) { if (!lab.LastUser.TryGetValue(lab.Server(m0).Name + "|" + u0, out r)) return null; }
                        else if (!lab.LastComputer.TryGetValue(lab.Server(m0).Name, out r)) return null;
                        return Rsop(r);
                    }
                case "centralstore": return WsVal.T(WsText.Yn(lab.CentralStore(A(a, "dc", 0) ?? dc)));
                case "replicated": { string x = A(a, "from", 0), y = A(a, "to", 1); return WsVal.T(WsText.Yn(lab.Backlog(lab.Server(x).Name, lab.Server(y).Name) == 0)); }
                case "passwordpolicy":
                    {
                        var u = A(a, "user", 0) == null ? null : d.Find(A(a, "user", 0), "user");
                        var pso = u == null ? null : lab.ResultantPso(d, u);
                        return WsVal.O(pso == null ? "Default Domain Policy" : pso.Name, (m, i) => { string k = m == "minlength" ? "minPwdLength" : m == "history" ? "pwdHistoryLength" : m == "maxage" ? "maxPwdAge" : m == "lockout" ? "lockoutThreshold" : m == "complexity" ? "complexityEnabled" : null; if (k == null) return null; return WsVal.T(pso != null ? pso.Get(k) : lab.DomainPolicy(d, k, "")); });
                    }
            }
            return RootNet(lab, dc, root.ToLowerInvariant(), a, ix);
        }

        /// <summary>A directory object: its attributes, plus ou, path, enabled, members / allmembers, memberof, scope, links, blocked, protected...</summary>
        static WsVal Obj(WsLab lab, WsDir d, WsAdObj o)
        {
            return WsVal.O(o.Name, (m, i) =>
            {
                switch (m)
                {
                    case "dn": return WsVal.T(o.Dn);
                    case "ou": case "parent": return WsVal.T(Dn.Parent(o.Dn));
                    case "path": return WsVal.T(Dn.Canonical(Dn.Parent(o.Dn)));
                    case "class": return WsVal.T(o.Class);
                    case "enabled": return WsVal.T(o.Get("enabled") == "FALSE" ? "No" : "Yes");
                    case "lockedout": return WsVal.T(WsText.Yn(o.Yes("lockedOut")));
                    case "mustchange": return WsVal.T(WsText.Yn(o.Get("pwdLastSet") == "0"));
                    case "password": return WsVal.T(o.Get("password"));
                    case "members": return WsVal.L(lab.Members(d, o, false).Select(x => x.Is("computer") ? x.Name : x.Sam.Length > 0 ? x.Sam : x.Name));
                    case "allmembers": return WsVal.L(lab.Members(d, o, true).Select(x => x.Is("computer") ? x.Name : x.Sam.Length > 0 ? x.Sam : x.Name));
                    case "memberof": return WsVal.L(lab.GroupsOf(d, o, false).Select(x => x.Name));
                    case "allgroups": return WsVal.L(lab.GroupsOf(d, o, true).Select(x => x.Name));
                    case "scope": return WsVal.T(o.Get("groupScope"));
                    case "category": return WsVal.T(o.Get("groupCategory"));
                    case "protected": return WsVal.T(WsText.Yn(o.Yes("protectedFromAccidentalDeletion")));
                    case "blocked": return WsVal.T(WsText.Yn(o.Get("gPOptions") == "1"));
                    case "links": return WsVal.L(WsLab.Links(o).Select(l => { var g = d.ById(l[0]); return g == null ? l[0] : g.Get("displayName"); }));
                    case "sites": return WsVal.L(o.List("siteList"));
                    case "interval": return WsVal.T(o.Get("replInterval"));
                    case "site": return WsVal.T(o.Get("siteObject"));
                    case "servers": return WsVal.L(d.Children("CN=Servers," + o.Dn).Select(x => x.Name));
                    case "appliesto": return WsVal.L(o.List("appliesTo").Select(g => { var x = d.ById(g); return x == null ? g : x.Name; }));
                    case "deleted": return WsVal.T(WsText.Yn(o.Deleted));
                    case "upn": return WsVal.T(o.Get("userPrincipalName"));
                    case "sam": return WsVal.T(o.Sam);
                    case "city": return WsVal.T(o.Get("l"));
                    case "principals": return WsVal.L(o.List("principalsAllowed").Select(g => { var x = d.ById(g); return x == null ? g : x.Name; }));
                }
                if (o.A.ContainsKey(m)) return o.List(m).Count > 1 ? WsVal.L(o.List(m)) : WsVal.T(o.Get(m));
                var k = o.A.Keys.FirstOrDefault(x => WsText.Eq(x, m));
                return k == null ? WsVal.T("") : WsVal.T(o.Get(k));
            });
        }

        static WsVal Gpo(WsLab lab, WsDir d, WsAdObj g)
        {
            return WsVal.O(g.Get("displayName"), (m, i) =>
            {
                switch (m)
                {
                    case "links": return WsVal.L(lab.WhereLinked(d, g).Select(kv => kv.Key.Is("site") ? kv.Key.Name : kv.Key.Dn));
                    case "enforced": { var t = lab.WhereLinked(d, g).FirstOrDefault(kv => i == null || Dn.Eq(kv.Key.Dn, WsOps.ResolvePath(lab, i)) || WsText.Eq(kv.Key.Name, i)); return t.Key == null ? null : WsVal.T(t.Value[2] == "1" ? "Yes" : "No"); }
                    case "linkenabled": { var t = lab.WhereLinked(d, g).FirstOrDefault(kv => i == null || Dn.Eq(kv.Key.Dn, WsOps.ResolvePath(lab, i)) || WsText.Eq(kv.Key.Name, i)); return t.Key == null ? null : WsVal.T(t.Value[1] == "1" ? "Yes" : "No"); }
                    case "linkorder": { var t = lab.WhereLinked(d, g).FirstOrDefault(kv => Dn.Eq(kv.Key.Dn, WsOps.ResolvePath(lab, i)) || WsText.Eq(kv.Key.Name, i)); return t.Key == null ? null : WsVal.T((WsLab.Links(t.Key).FindIndex(l => l[0] == g.Guid) + 1).ToString(CultureInfo.InvariantCulture)); }
                    case "setting": { var def = GpCatalog.Get(i); string id = def == null ? i : def.Id; foreach (var sc in new[] { "C", "U" }) { var s = WsLab.Settings(g, sc); string v; if (s.TryGetValue(id, out v)) { var p = v.Split(new[] { '|' }, 2); return WsVal.O(p[0], (mm, ii) => mm == "value" ? WsVal.T(p.Length > 1 ? p[1] : "") : mm == "state" ? WsVal.T(p[0]) : null); } } return WsVal.T("Not configured"); }
                    case "filter": return WsVal.L(g.List("gpPerm").Where(e => e.EndsWith("|GpoApply", StringComparison.Ordinal)).Select(e => e.Split('|')[0]));
                    case "perm": return WsVal.T(WsLab.PermOf(g, i));
                    case "wmifilter": { var w = d.ById(g.Get("wmiFilter")); return WsVal.T(w == null ? "" : w.Get("msWMI-Name")); }
                    case "status": return WsVal.T(WsLab.Status(g));
                    case "guid": return WsVal.T(g.Guid);
                    case "backups": return WsVal.T(lab.GpoBackups.Count(b => b.Guid == g.Guid).ToString(CultureInfo.InvariantCulture));
                    case "comment": return WsVal.T(g.Get("description"));
                }
                return null;
            });
        }

        static WsVal Rsop(RsopResult r)
        {
            return WsVal.O(r.Computer + (r.User.Length > 0 ? "/" + r.User : ""), (m, i) =>
            {
                switch (m)
                {
                    case "applied": return WsVal.L(r.AppliedC.Concat(r.AppliedU).Select(x => x.Name).Distinct());
                    case "computergpos": return WsVal.L(r.AppliedC.Select(x => x.Name));
                    case "usergpos": return WsVal.L(r.AppliedU.Select(x => x.Name));
                    case "denied": return WsVal.L(r.DeniedC.Concat(r.DeniedU).Select(x => x.Name + ": " + x.Reason));
                    case "reason": { var x = r.DeniedC.Concat(r.DeniedU).FirstOrDefault(y => WsText.Eq(y.Name, i)); return WsVal.T(x == null ? "" : x.Reason); }
                    case "setting": { var v = r.Get(i); return v == null ? WsVal.T("Not configured") : WsVal.O(v.State, (mm, ii) => mm == "value" ? WsVal.T(v.Value) : mm == "gpo" || mm == "winner" ? WsVal.T(v.Gpo) : mm == "state" ? WsVal.T(v.State) : null); }
                    case "winner": { var v = r.Get(i); return WsVal.T(v == null ? "" : v.Gpo); }
                    case "loopback": return WsVal.T(r.Loopback.Length == 0 ? "None" : r.Loopback);
                    case "site": return WsVal.T(r.Site ?? "");
                    case "dc": return WsVal.T(r.Dc);
                }
                return null;
            });
        }
        static partial void RootNetPart(WsLab lab, string dc, string root, Dictionary<string, string> a, string ix, ref WsVal v);
        static WsVal RootNet(WsLab lab, string dc, string root, Dictionary<string, string> a, string ix) { WsVal v = null; RootNetPart(lab, dc, root, a, ix, ref v); return v; }
    }
}
