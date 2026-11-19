// Cert Lab - the ActiveDirectory module on the AZ-800 machines (DCs and SRV1, where RSAT-AD-PowerShell is installed):
// shared helpers (which DC a command talks to, -Identity from a name or a pipeline object, the -Filter language, the
// AD objects these commands print) and the user, group, computer and OU commands. Everything goes through WsLab, the
// same code the practice consoles use.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string AdMod = "ActiveDirectory";
        static Func<PsMachine, bool> HasAd = m => m.Ws != null && m.FeatureInstalled("RSAT-AD-PowerShell");

        // PowerShell property name -> attribute
        static readonly string[][] AdProps = {
            new[] { "Name", "name" }, new[] { "SamAccountName", "sAMAccountName" }, new[] { "UserPrincipalName", "userPrincipalName" }, new[] { "GivenName", "givenName" }, new[] { "Surname", "sn" },
            new[] { "DisplayName", "displayName" }, new[] { "Department", "department" }, new[] { "Title", "title" }, new[] { "Description", "description" }, new[] { "City", "l" }, new[] { "Company", "company" },
            new[] { "Office", "physicalDeliveryOfficeName" }, new[] { "EmailAddress", "mail" }, new[] { "Manager", "manager" }, new[] { "OperatingSystem", "operatingSystem" }, new[] { "OperatingSystemVersion", "operatingSystemVersion" },
            new[] { "DNSHostName", "dNSHostName" }, new[] { "GroupScope", "groupScope" }, new[] { "GroupCategory", "groupCategory" }, new[] { "ProtectedFromAccidentalDeletion", "protectedFromAccidentalDeletion" },
            new[] { "whenCreated", "whenCreated" }, new[] { "IPv4Address", "ipv4" } };
        static string AdAttr(string ps) { var p = AdProps.FirstOrDefault(x => WsText.Eq(x[0], ps) || WsText.Eq(x[1], ps)); return p == null ? ps : p[1]; }

        static WsLab Lab(CmdCtx c) { return c.M.Ws; }
        static string AdDc(CmdCtx c)
        {
            string err; var dc = Lab(c).LocateDc(c.M, c.Str("Server"), out err);
            if (dc == null) throw c.Fail(err, "ActiveDirectoryServer:0", "ResourceUnavailable", c.Str("Server"), "Microsoft.ActiveDirectory.Management.ADServerDownException");
            return dc;
        }
        static WsDir AdDir(CmdCtx c, out string dc) { dc = AdDc(c); return Lab(c).Dir(dc); }
        static PsRuntimeException NotFound(CmdCtx c, string key, WsLab lab)
        {
            return c.Fail("Cannot find an object with identity: '" + key + "' under: '" + lab.DomainDn + "'.", "ActiveDirectoryCmdlet:Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException", "ObjectNotFound", key, "Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException");
        }
        /// <summary>The object -Identity names: an AD object from the pipeline, a DN, GUID, SamAccountName or name.</summary>
        static WsAdObj AdFind(CmdCtx c, WsDir d, object v, string cls)
        {
            v = PsConv.Base(v);
            var po = v as PsObj;
            if (po != null && po.Base is WsAdObj) { var b = (WsAdObj)po.Base; return d.ById(b.Guid) ?? d.Find(b.Dn, cls); }
            string key = po != null && po.Props.Any(p => p.Name == "DistinguishedName") ? PsConv.ToStr(po.Get("DistinguishedName")) : PsConv.ToStr(v);
            var o = d.Find(key, cls);
            if (o == null) throw NotFound(c, key, Lab(c));
            return o;
        }
        static List<WsAdObj> AdFindAll(CmdCtx c, WsDir d, string param, string cls) { return c.Objs(param).Select(x => AdFind(c, d, x, cls)).ToList(); }

        /// <summary>An AD object as the module prints it: the class's default properties plus -Properties, in name order.</summary>
        public static PsObj AdObj(WsLab lab, WsDir d, WsAdObj o, string[] extra)
        {
            string cls = o.Class;
            string t = cls == "user" ? "ADUser" : cls == "group" ? "ADGroup" : cls == "computer" ? "ADComputer" : cls == "organizationalUnit" ? "ADOrganizationalUnit" : cls == "msDS-GroupManagedServiceAccount" ? "ADServiceAccount" : "ADObject";
            var p = new PsObj("Microsoft.ActiveDirectory.Management." + t, t == "ADObject" ? "Microsoft.ActiveDirectory.Management.ADEntity" : "Microsoft.ActiveDirectory.Management.ADPrincipal", "Microsoft.ActiveDirectory.Management.ADObject", "System.Object") { Base = o };
            var vals = new SortedDictionary<string, KeyValuePair<string, object>>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, object> put = (n, ty, val) => vals[n] = new KeyValuePair<string, object>(ty, val);
            put("DistinguishedName", "string", o.Dn); put("Name", "string", o.Name); put("ObjectClass", "string", o.Class); put("ObjectGUID", "System.Nullable[guid]", o.Guid);
            if (o.IsPrincipal) { put("SamAccountName", "string", o.Sam); put("SID", "System.Security.Principal.SecurityIdentifier", o.Get("objectSid")); }
            if (cls == "user") { put("Enabled", "bool", o.Get("enabled") != "FALSE"); put("GivenName", "string", o.Get("givenName")); put("Surname", "string", o.Get("sn")); put("UserPrincipalName", "string", o.Get("userPrincipalName")); }
            if (cls == "computer") { put("Enabled", "bool", o.Get("enabled") != "FALSE"); put("DNSHostName", "string", o.Get("dNSHostName")); put("UserPrincipalName", "string", null); }
            if (cls == "group") { put("GroupCategory", "System.Nullable[Microsoft.ActiveDirectory.Management.ADGroupCategory]", o.Get("groupCategory")); put("GroupScope", "System.Nullable[Microsoft.ActiveDirectory.Management.ADGroupScope]", o.Get("groupScope")); }
            if (cls == "organizationalUnit") { foreach (var n in new[] { "City", "Country", "ManagedBy", "PostalCode", "State", "StreetAddress" }) put(n, "string", null); put("LinkedGroupPolicyObjects", "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection", WsLab.Links(o).Select(l => (object)("cn={" + l[0].ToUpperInvariant() + "},cn=policies,cn=system," + lab.DomainDn)).ToArray()); }
            bool all = extra != null && extra.Contains("*");
            var want = all ? o.A.Keys.ToList() : (extra ?? new string[0]).ToList();
            foreach (var e in want)
            {
                string ps = AdProps.Where(x => WsText.Eq(x[1], e) || WsText.Eq(x[0], e)).Select(x => x[0]).FirstOrDefault() ?? e;
                switch (ps.ToLowerInvariant())
                {
                    case "memberof": put("MemberOf", "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection", lab.GroupsOf(d, o, false).Where(g => !WsText.Eq(g.Name, o.Get("primaryGroup"))).Select(g => (object)g.Dn).ToArray()); continue;
                    case "members": put("Members", "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection", lab.Members(d, o, false).Select(g => (object)g.Dn).ToArray()); continue;
                    case "lockedout": put("LockedOut", "bool", o.Yes("lockedOut")); continue;
                    case "passwordlastset": put("PasswordLastSet", "System.Nullable[datetime]", o.Get("pwdLastSet").Length > 1 ? (object)DateTime.Parse(o.Get("pwdLastSet"), CultureInfo.InvariantCulture) : null); continue;
                    case "canonicalname": put("CanonicalName", "string", Dn.Canonical(o.Dn)); continue;
                    case "protectedfromaccidentaldeletion": put("ProtectedFromAccidentalDeletion", "bool", o.Yes("protectedFromAccidentalDeletion")); continue;
                    case "objectclass": case "objectsid": case "password": case "primarygroup": case "enabled": continue;
                }
                var attr = AdAttr(ps);
                var l = o.List(attr);
                put(ps, l.Count > 1 ? "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection" : "string", l.Count > 1 ? (object)l.Cast<object>().ToArray() : (l.Count == 0 ? null : l[0]));
            }
            foreach (var kv in vals) p.Val(kv.Key, kv.Value.Key, kv.Value.Value);
            p.ToStr = x => o.Dn;
            return p;
        }
        static string[] AdExtra(CmdCtx c) { return c.Strs("Properties"); }

        /// <summary>The objects a -Filter / -SearchBase / -SearchScope pick (classes: user, group, computer, organizationalUnit, null = any).</summary>
        static IEnumerable<WsAdObj> AdSearch(CmdCtx c, WsDir d, string cls)
        {
            string baseDn = c.Str("SearchBase") ?? Lab(c).DomainDn;
            if (d.ByDn(baseDn) == null) throw c.Fail("Directory object not found", "ActiveDirectoryCmdlet:Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException", "ObjectNotFound", baseDn, "Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException");
            string scope = c.Str("SearchScope", "Subtree");
            var f = c.Get("Filter");
            string text = f is PsScriptBlock ? ((PsScriptBlock)f).Text : PsConv.ToStr(f ?? "*");
            var filter = AdFilter.Parse(text, n => PsConv.ToStr(c.R.GetVar(n, c.Scope)));
            foreach (var o in d.Live.OrderBy(x => x.Dn.Length).ThenBy(x => x.Dn, StringComparer.OrdinalIgnoreCase))
            {
                if (cls != null && !o.Is(cls) && !(cls == "user" && o.Is("msDS-GroupManagedServiceAccount") && false)) continue;
                if (!Dn.Under(o.Dn, baseDn)) continue;
                if (WsText.Eq(scope, "OneLevel") || scope == "1") { if (!Dn.Eq(Dn.Parent(o.Dn), baseDn)) continue; }
                else if (WsText.Eq(scope, "Base") || scope == "0") { if (!Dn.Eq(o.Dn, baseDn)) continue; }
                if (cls == null && (o.Is("dnsZone") || o.Is("groupPolicyContainer") && !c.Has("Filter"))) continue;
                if (filter.Match(o)) yield return o;
            }
        }
    }
}
