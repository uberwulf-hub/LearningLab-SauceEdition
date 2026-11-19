// Cert Lab - Group Policy objects of the pretend domain. A GPO is a directory object here (its SYSVOL part travels with
// it): display name, status (user / computer settings enabled), settings ("C|Id|Enabled|value"), permissions
// ("trustee|GpoApply"), a WMI filter, and version numbers. Links live on the site, domain or OU they are linked to
// (gPLink: "guid|enabled|enforced", the first is link order 1), and "Block Inheritance" is gPOptions = 1.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public string PoliciesDn { get { return "CN=Policies,CN=System," + DomainDn; } }
        public string WmiDn { get { return "CN=WMIPolicy,CN=System," + DomainDn; } }
        public const string DdpGuid = "31b2f340-016d-11d2-945f-00c04fb984f9", DdcpGuid = "6ac1786c-016f-11d2-945f-00c04fb984f9";
        public static readonly string[] PermLevels = { "None", "GpoRead", "GpoApply", "GpoEdit", "GpoEditDeleteModifySecurity" };

        public IEnumerable<WsAdObj> Gpos(WsDir d) { return d.Children(PoliciesDn).Where(o => o.Is("groupPolicyContainer")); }
        public WsAdObj FindGpo(WsDir d, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().Trim('{', '}');
            return Gpos(d).FirstOrDefault(g => WsText.Eq(g.Guid, k)) ?? Gpos(d).FirstOrDefault(g => WsText.Eq(g.Get("displayName"), key.Trim()));
        }
        public IEnumerable<WsAdObj> WmiFilters(WsDir d) { return d.Children(WmiDn).Where(o => o.Is("msWMI-Som")); }
        public WsAdObj FindWmi(WsDir d, string key) { return WmiFilters(d).FirstOrDefault(w => WsText.Eq(w.Get("msWMI-Name"), key) || WsText.Eq(w.Guid, (key ?? "").Trim('{', '}'))); }

        public WsAdObj NewGpo(string dc, string name, string comment, out string err)
        {
            var d = Dir(dc);
            err = null;
            if (string.IsNullOrEmpty(name)) { err = "A GPO needs a name."; return null; }
            if (FindGpo(d, name) != null) { err = "A GPO named \"" + name + "\" already exists in the domain " + DomainDns + "."; return null; }
            string guid = NewGuid();
            var g = new WsAdObj { Guid = guid, Dn = "CN={" + guid.ToUpperInvariant() + "}," + PoliciesDn, Class = "groupPolicyContainer" };
            g.Set("name", "{" + guid.ToUpperInvariant() + "}").Set("displayName", name).Set("flags", "0").Set("versionUser", "0").Set("versionComputer", "0");
            g.Set("description", comment).Set("whenCreated", WsText.Date(Now)).Set("owner", NetBios + "\\Domain Admins");
            g.SetList("gpPerm", new[] { "Authenticated Users|GpoApply", "Domain Admins|GpoEditDeleteModifySecurity", "Enterprise Admins|GpoEditDeleteModifySecurity", "SYSTEM|GpoEditDeleteModifySecurity", "ENTERPRISE DOMAIN CONTROLLERS|GpoRead" });
            Touch(dc, g);
            return g;
        }

        // ------------------------------------------------------------ settings
        public static string SettingKey(string scope, string id) { return scope + "|" + id; }
        /// <summary>The settings a GPO holds for a scope: id -> "state|value".</summary>
        public static Dictionary<string, string> Settings(WsAdObj g, string scope)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in g.List("gpSetting"))
            {
                var p = e.Split(new[] { '|' }, 4);
                if (p.Length >= 3 && p[0] == scope) r[p[1]] = p[2] + "|" + (p.Length > 3 ? p[3] : "");
            }
            return r;
        }
        /// <summary>Sets (state Enabled / Disabled) or removes (state "Not configured") a setting.</summary>
        public string SetSetting(string dc, WsAdObj g, string scope, string id, string state, string value)
        {
            var l = g.List("gpSetting").Where(e => !e.StartsWith(scope + "|" + id + "|", StringComparison.OrdinalIgnoreCase)).ToList();
            bool remove = string.IsNullOrEmpty(state) || state.Replace(" ", "").Equals("NotConfigured", StringComparison.OrdinalIgnoreCase);
            if (!remove) l.Add(scope + "|" + id + "|" + state + "|" + (value ?? ""));
            g.SetList("gpSetting", l);
            string vk = scope == "U" ? "versionUser" : "versionComputer";
            g.Set(vk, (int.Parse("0" + g.Get(vk), CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
            g.Set("whenChanged", WsText.Date(Now));
            Touch(dc, g);
            GpoChanged(dc, g);
            return null;
        }
        partial void OnGpoChanged(string dc, WsAdObj g);
        void GpoChanged(string dc, WsAdObj g) { OnGpoChanged(dc, g); }

        public static string Status(WsAdObj g)
        {
            switch (g.Get("flags")) { case "1": return "UserSettingsDisabled"; case "2": return "ComputerSettingsDisabled"; case "3": return "AllSettingsDisabled"; }
            return "AllSettingsEnabled";
        }
        public static bool ScopeEnabled(WsAdObj g, string scope) { string f = g.Get("flags"); return f != "3" && !(scope == "U" && f == "1") && !(scope == "C" && f == "2"); }

        // ------------------------------------------------------------ links and inheritance
        public static List<string[]> Links(WsAdObj container) { return container.List("gPLink").Select(x => x.Split('|')).Where(p => p.Length == 3).ToList(); }
        public IEnumerable<KeyValuePair<WsAdObj, string[]>> WhereLinked(WsDir d, WsAdObj g)
        {
            foreach (var c in d.Live.Where(o => o.A.ContainsKey("gPLink")))
                foreach (var l in Links(c)) if (WsText.Eq(l[0], g.Guid)) yield return new KeyValuePair<WsAdObj, string[]>(c, l);
        }
        public WsAdObj LinkTarget(WsDir d, string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            var o = d.ByDn(target);
            if (o == null && !target.Contains("=")) o = Sites(d).FirstOrDefault(s => WsText.Eq(s.Name, target)) ?? d.Live.FirstOrDefault(x => x.Is("organizationalUnit") && WsText.Eq(x.Name, target));
            return o != null && (o.IsContainerLike || o.Is("site")) && !o.Is("container") && !o.Is("builtinDomain") ? o : null;
        }
        public string Link(string dc, WsAdObj g, WsAdObj target, bool enabled, bool enforced, int order)
        {
            var l = target.List("gPLink").ToList();
            if (l.Any(x => x.StartsWith(g.Guid + "|", StringComparison.OrdinalIgnoreCase))) return "The GPO " + g.Get("displayName") + " is already linked to " + target.Dn + ".";
            string e = g.Guid + "|" + (enabled ? "1" : "0") + "|" + (enforced ? "1" : "0");
            if (order >= 1 && order <= l.Count) l.Insert(order - 1, e); else l.Add(e);
            target.SetList("gPLink", l); Touch(dc, target);
            GpoChanged(dc, g);
            return null;
        }
        public string SetLink(string dc, WsAdObj g, WsAdObj target, bool? enabled, bool? enforced, int order)
        {
            var l = target.List("gPLink").ToList();
            int i = l.FindIndex(x => x.StartsWith(g.Guid + "|", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return "The GPO " + g.Get("displayName") + " isn't linked to " + target.Dn + ".";
            var p = l[i].Split('|');
            string e = p[0] + "|" + (enabled.HasValue ? (enabled.Value ? "1" : "0") : p[1]) + "|" + (enforced.HasValue ? (enforced.Value ? "1" : "0") : p[2]);
            l.RemoveAt(i);
            if (order >= 1) l.Insert(Math.Min(order - 1, l.Count), e); else l.Insert(i, e);
            target.SetList("gPLink", l); Touch(dc, target);
            GpoChanged(dc, g);
            return null;
        }
        public string Unlink(string dc, WsAdObj g, WsAdObj target)
        {
            var l = target.List("gPLink").ToList();
            if (l.RemoveAll(x => x.StartsWith(g.Guid + "|", StringComparison.OrdinalIgnoreCase)) == 0) return "The GPO " + g.Get("displayName") + " isn't linked to " + target.Dn + ".";
            target.SetList("gPLink", l); Touch(dc, target);
            GpoChanged(dc, g);
            return null;
        }
        public void BlockInheritance(string dc, WsAdObj ou, bool block) { ou.Set("gPOptions", block ? "1" : null); Touch(dc, ou); }

        // ------------------------------------------------------------ permissions (security filtering, delegation)
        public static string PermOf(WsAdObj g, string trustee)
        {
            foreach (var e in g.List("gpPerm")) { var p = e.Split('|'); if (p.Length == 2 && WsText.Eq(StripDomain(p[0]), StripDomain(trustee))) return p[1]; }
            return "None";
        }
        public static string StripDomain(string n) { n = n ?? ""; int i = n.IndexOf('\\'); return i >= 0 ? n.Substring(i + 1) : n; }
        public void SetPerm(string dc, WsAdObj g, string trustee, string level)
        {
            var l = g.List("gpPerm").Where(e => !WsText.Eq(StripDomain(e.Split('|')[0]), StripDomain(trustee))).ToList();
            if (!WsText.Eq(level, "None")) l.Add(StripDomain(trustee) + "|" + level);
            g.SetList("gpPerm", l); Touch(dc, g);
            GpoChanged(dc, g);
        }
        /// <summary>Does a token read (and apply) the GPO? Apply needs GpoApply; Edit levels read but don't apply.</summary>
        public static bool CanRead(WsAdObj g, HashSet<string> token) { return g.List("gpPerm").Any(e => { var p = e.Split('|'); return p.Length == 2 && token.Contains(StripDomain(p[0])) && p[1] != "None"; }); }
        public static bool Applies(WsAdObj g, HashSet<string> token) { return g.List("gpPerm").Any(e => { var p = e.Split('|'); return p.Length == 2 && token.Contains(StripDomain(p[0])) && p[1] == "GpoApply"; }); }
    }
}
