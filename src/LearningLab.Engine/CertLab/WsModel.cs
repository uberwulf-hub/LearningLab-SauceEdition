// Cert Lab - the AZ-800 course's directory: Active Directory objects (users, groups, computers, OUs, GPO containers,
// WMI filters, sites, subnets, site links, trusts...) as property bags, and one copy of the directory per domain
// controller (WsDir). A change is written on one DC (its version goes up) and reaches the others by replication
// (WsLab.Replicate). Group members are kept by object id, so moving or renaming an object keeps memberships.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsAdObj
    {
        public string Guid = "", Dn = "", Class = "";
        public readonly Dictionary<string, List<string>> A = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public int Version; public DateTime Stamp; public string Origin = ""; public bool Deleted;

        public string Get(string k) { List<string> l; return A.TryGetValue(k, out l) && l.Count > 0 ? l[0] : ""; }
        public List<string> List(string k) { List<string> l; return A.TryGetValue(k, out l) ? l : new List<string>(); }
        public WsAdObj Set(string k, string v) { if (string.IsNullOrEmpty(v)) A.Remove(k); else A[k] = new List<string> { v }; return this; }
        public void SetList(string k, IEnumerable<string> v) { var l = v.ToList(); if (l.Count == 0) A.Remove(k); else A[k] = l; }
        public bool AddTo(string k, string v)
        {
            List<string> l; if (!A.TryGetValue(k, out l)) { l = new List<string>(); A[k] = l; }
            if (l.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase))) return false;
            l.Add(v); return true;
        }
        public bool RemoveFrom(string k, string v) { List<string> l; if (!A.TryGetValue(k, out l)) return false; int n = l.RemoveAll(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)); if (l.Count == 0) A.Remove(k); return n > 0; }
        public bool Yes(string k) { var v = Get(k); return v == "1" || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || v.Equals("Yes", StringComparison.OrdinalIgnoreCase); }
        public string Name { get { return Get("name"); } }
        public string Sam { get { return Get("sAMAccountName"); } }
        public bool Is(string cls) { return Class.Equals(cls, StringComparison.OrdinalIgnoreCase); }
        public bool IsPrincipal { get { return Is("user") || Is("group") || Is("computer") || Is("msDS-GroupManagedServiceAccount"); } }
        public bool IsContainerLike { get { return Is("organizationalUnit") || Is("container") || Is("domainDNS") || Is("builtinDomain"); } }

        public WsAdObj Clone()
        {
            var o = new WsAdObj { Guid = Guid, Dn = Dn, Class = Class, Version = Version, Stamp = Stamp, Origin = Origin, Deleted = Deleted };
            foreach (var kv in A) o.A[kv.Key] = new List<string>(kv.Value);
            return o;
        }
        public override string ToString() { return Dn; }
    }

    /// <summary>Distinguished name helpers (DNs here never contain escaped commas).</summary>
    public static class Dn
    {
        public static string Parent(string dn) { int i = dn.IndexOf(','); return i < 0 ? "" : dn.Substring(i + 1); }
        public static string Rdn(string dn) { int i = dn.IndexOf(','); return i < 0 ? dn : dn.Substring(0, i); }
        public static string RdnValue(string dn) { var r = Rdn(dn); int i = r.IndexOf('='); return i < 0 ? r : r.Substring(i + 1); }
        public static bool Eq(string a, string b) { return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase); }
        public static string Norm(string dn) { return string.Join(",", (dn ?? "").Split(',').Select(p => p.Trim()).ToArray()); }
        /// <summary>Is a below b (or b itself)?</summary>
        public static bool Under(string a, string b) { a = Norm(a); b = Norm(b); return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.EndsWith("," + b, StringComparison.OrdinalIgnoreCase); }
        /// <summary>DC=corp,DC=example for corp.example.</summary>
        public static string FromDns(string dns) { return string.Join(",", dns.Split('.').Select(p => "DC=" + p).ToArray()); }
        public static string ToDns(string dn) { return string.Join(".", dn.Split(',').Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).Select(p => p.Trim().Substring(3)).ToArray()); }
        /// <summary>A short path a person reads: corp.example/Staff/Sales.</summary>
        public static string Canonical(string dn)
        {
            var parts = Norm(dn).Split(',');
            var dom = ToDns(dn);
            var rest = parts.Where(p => !p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).Select(p => p.Substring(p.IndexOf('=') + 1)).Reverse();
            return dom + (rest.Any() ? "/" + string.Join("/", rest.ToArray()) : "");
        }
    }

    /// <summary>One domain controller's copy of the directory.</summary>
    public class WsDir
    {
        public string Dc = "";
        public readonly Dictionary<string, WsAdObj> Objs = new Dictionary<string, WsAdObj>(StringComparer.OrdinalIgnoreCase);
        public IEnumerable<WsAdObj> Live { get { return Objs.Values.Where(o => !o.Deleted); } }
        public WsAdObj ById(string g) { WsAdObj o; return g != null && Objs.TryGetValue(g, out o) && !o.Deleted ? o : null; }
        public WsAdObj ByDn(string dn) { return Live.FirstOrDefault(o => Dn.Eq(o.Dn, dn)); }
        public IEnumerable<WsAdObj> Children(string dn) { return Live.Where(o => Dn.Eq(Dn.Parent(o.Dn), dn)); }
        public IEnumerable<WsAdObj> Below(string dn) { return Live.Where(o => Dn.Under(o.Dn, dn)); }
        public IEnumerable<WsAdObj> OfClass(string cls) { return Live.Where(o => o.Is(cls)); }

        /// <summary>An object by DN, GUID, sAMAccountName (with or without $ for computers), UPN or name.</summary>
        public WsAdObj Find(string key, string cls)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim();
            if (k.Contains("\\")) k = k.Substring(k.IndexOf('\\') + 1);
            Func<WsAdObj, bool> ok = o => cls == null || o.Is(cls) || cls == "principal" && o.IsPrincipal || cls == "user" && o.Is("msDS-GroupManagedServiceAccount");
            var hit = ById(k.Trim('{', '}'));
            if (hit != null && ok(hit)) return hit;
            if (k.Contains("=")) { hit = ByDn(k); return hit != null && ok(hit) ? hit : null; }
            var live = Live.Where(ok).ToList();
            return live.FirstOrDefault(o => o.Sam.Equals(k, StringComparison.OrdinalIgnoreCase))
                ?? live.FirstOrDefault(o => o.Sam.Equals(k + "$", StringComparison.OrdinalIgnoreCase))
                ?? live.FirstOrDefault(o => o.Get("userPrincipalName").Equals(k, StringComparison.OrdinalIgnoreCase))
                ?? live.FirstOrDefault(o => o.Name.Equals(k, StringComparison.OrdinalIgnoreCase));
        }

        public WsDir Clone(string dc)
        {
            var d = new WsDir { Dc = dc };
            foreach (var kv in Objs) d.Objs[kv.Key] = kv.Value.Clone();
            return d;
        }
    }

    /// <summary>A result of a change: an error message for the person, or null when it worked.</summary>
    public static class WsText
    {
        public static bool Eq(string a, string b) { return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase); }
        public static string Yn(bool b) { return b ? "Yes" : "No"; }
        public static bool True(string v) { v = (v ?? "").Trim().TrimStart('$'); return Eq(v, "yes") || Eq(v, "true") || v == "1" || Eq(v, "on") || Eq(v, "enabled"); }
        public static string Join(IEnumerable<string> l) { return string.Join(", ", l.ToArray()); }
        public static string Date(DateTime d) { return d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
        public static bool Like(string s, string pattern)
        {
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern ?? "").Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(s ?? "", rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
