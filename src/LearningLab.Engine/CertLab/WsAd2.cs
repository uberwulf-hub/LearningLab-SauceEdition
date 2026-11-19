// Cert Lab - creating users, groups, computers and OUs in the pretend Active Directory, group membership with the
// scope rules (AGDLP: accounts go in global groups, global groups in domain local groups, which get the permissions),
// changing a group's scope, and the account flags (enabled, locked out, password must change).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public string UsersDn { get { return "CN=Users," + DomainDn; } }
        public string ComputersDn { get { return "CN=Computers," + DomainDn; } }

        public WsAdObj NewOu(string dc, string parentDn, string name, bool protect, out string err)
        {
            var d = Dir(dc);
            err = CanCreate(d, parentDn, "OU=" + name, null);
            if (err != null) return null;
            var o = Create(dc, "organizationalUnit", "OU=" + name + "," + Dn.Norm(parentDn));
            if (protect) { o.Set("protectedFromAccidentalDeletion", "TRUE"); }
            return o;
        }

        public WsAdObj NewUser(string dc, string parentDn, string name, string sam, string upn, bool enabled, string password, out string err)
        {
            var d = Dir(dc);
            if (string.IsNullOrEmpty(sam)) sam = name.Replace(" ", "").ToLowerInvariant();
            if (sam.Length > 20) { err = "The sAMAccountName (pre-Windows 2000 logon name) can be at most 20 characters."; return null; }
            err = CanCreate(d, string.IsNullOrEmpty(parentDn) ? UsersDn : parentDn, "CN=" + name, sam);
            if (err != null) return null;
            if (enabled && !string.IsNullOrEmpty(password)) { err = PasswordProblem(d, null, password); if (err != null) return null; }
            if (enabled && string.IsNullOrEmpty(password)) { err = "The password does not meet the length, complexity, or history requirement of the domain: an account can't be enabled without a password."; return null; }
            var o = Create(dc, "user", "CN=" + name + "," + Dn.Norm(string.IsNullOrEmpty(parentDn) ? UsersDn : parentDn), "sAMAccountName", sam, "primaryGroup", "Domain Users");
            if (!string.IsNullOrEmpty(upn)) o.Set("userPrincipalName", upn);
            o.Set("enabled", enabled ? "TRUE" : "FALSE");
            if (!string.IsNullOrEmpty(password)) { o.Set("password", password); o.Set("pwdLastSet", WsText.Date(Now)); }
            Touch(dc, o);
            return o;
        }

        public WsAdObj NewGroup(string dc, string parentDn, string name, string sam, string scope, string category, out string err)
        {
            var d = Dir(dc);
            scope = NormScope(scope);
            if (scope == null) { err = "GroupScope must be DomainLocal, Global or Universal."; return null; }
            if (string.IsNullOrEmpty(sam)) sam = name;
            err = CanCreate(d, string.IsNullOrEmpty(parentDn) ? UsersDn : parentDn, "CN=" + name, sam);
            if (err != null) return null;
            return Create(dc, "group", "CN=" + name + "," + Dn.Norm(string.IsNullOrEmpty(parentDn) ? UsersDn : parentDn), "sAMAccountName", sam, "groupScope", scope, "groupCategory", string.IsNullOrEmpty(category) ? "Security" : (category.StartsWith("D", StringComparison.OrdinalIgnoreCase) ? "Distribution" : "Security"));
        }
        public static string NormScope(string s)
        {
            switch ((s ?? "global").Trim().ToLowerInvariant().Replace(" ", "")) { case "0": case "domainlocal": return "DomainLocal"; case "1": case "global": return "Global"; case "2": case "universal": return "Universal"; }
            return null;
        }

        public WsAdObj NewComputer(string dc, string parentDn, string name, out string err)
        {
            var d = Dir(dc);
            string sam = name.ToUpperInvariant() + "$";
            err = CanCreate(d, string.IsNullOrEmpty(parentDn) ? ComputersDn : parentDn, "CN=" + name, sam);
            if (err != null) return null;
            return Create(dc, "computer", "CN=" + name + "," + Dn.Norm(string.IsNullOrEmpty(parentDn) ? ComputersDn : parentDn), "sAMAccountName", sam, "primaryGroup", "Domain Computers", "dNSHostName", name.ToLowerInvariant() + "." + DomainDns, "enabled", "TRUE");
        }

        /// <summary>Adds a member, following the scope rules of a single domain (member groups from other domains aren't simulated).</summary>
        public string AddMember(string dc, WsAdObj g, WsAdObj m)
        {
            if (m.Guid == g.Guid) return "A group can't be a member of itself.";
            if (m.Is("group"))
            {
                string gs = g.Get("groupScope"), ms = m.Get("groupScope");
                if (gs == "Global" && ms != "Global") return "The group " + m.Name + " (" + ms + ") can't be a member of the global group " + g.Name + ": a global group holds only accounts and other global groups of its own domain.";
                if (gs == "Universal" && ms == "DomainLocal") return "A domain local group (" + m.Name + ") can't be a member of a universal group.";
            }
            if (!g.AddTo("member", m.Guid)) return null;
            Touch(dc, g);
            return null;
        }
        public string RemoveMember(string dc, WsAdObj g, WsAdObj m)
        {
            if (g.RemoveFrom("member", m.Guid)) Touch(dc, g);
            return null;
        }

        /// <summary>Changes a group's scope as Windows allows: global and domain local only through universal.</summary>
        public string SetScope(string dc, WsAdObj g, string scope)
        {
            var d = Dir(dc);
            string to = NormScope(scope), from = g.Get("groupScope");
            if (to == null) return "GroupScope must be DomainLocal, Global or Universal.";
            if (to == from) return null;
            if (from == "Global" && to == "DomainLocal" || from == "DomainLocal" && to == "Global") return "A " + from + " group can't become " + to + " directly: change it to Universal first.";
            var mems = Members(d, g, false).ToList();
            var parents = GroupsOf(d, g, false);
            if (to == "Universal" && from == "Global" && parents.Any(p => p.Get("groupScope") == "Global")) return "The group is a member of a global group, so it can't become universal.";
            if (to == "Universal" && from == "DomainLocal" && mems.Any(x => x.Is("group") && x.Get("groupScope") == "DomainLocal")) return "The group has domain local groups as members, so it can't become universal.";
            if (to == "Global" && mems.Any(x => x.Is("group") && x.Get("groupScope") == "Universal")) return "The group has universal groups as members, so it can't become global.";
            g.Set("groupScope", to); Touch(dc, g);
            return null;
        }

        /// <summary>Why a password isn't accepted for a user (the user's fine-grained policy, or the domain's), or null.</summary>
        public string PasswordProblem(WsDir d, WsAdObj user, string pw)
        {
            var pol = user == null ? null : ResultantPso(d, user);
            int min = PolicyInt(d, pol, "minPwdLength", 7);
            bool cx = pol != null ? pol.Yes("complexityEnabled") : DomainPolicy(d, "complexityEnabled", "TRUE") == "TRUE";
            if (pw.Length < min) return "The password does not meet the length, complexity, or history requirement of the domain (at least " + min + " characters).";
            if (cx)
            {
                int kinds = (pw.Any(char.IsUpper) ? 1 : 0) + (pw.Any(char.IsLower) ? 1 : 0) + (pw.Any(char.IsDigit) ? 1 : 0) + (pw.Any(ch => !char.IsLetterOrDigit(ch)) ? 1 : 0);
                if (kinds < 3 || user != null && pw.IndexOf(user.Sam, StringComparison.OrdinalIgnoreCase) >= 0) return "The password does not meet the length, complexity, or history requirement of the domain (complexity: three of upper case, lower case, digits and symbols, and not the account name).";
            }
            return null;
        }
        public string DomainPolicy(WsDir d, string key, string def) { var dom = d.ByDn(DomainDn); var v = dom == null ? "" : dom.Get(key); return v.Length > 0 ? v : def; }
        int PolicyInt(WsDir d, WsAdObj pso, string key, int def)
        {
            int v; string s = pso != null ? pso.Get(key) : DomainPolicy(d, key, def.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(s, out v) ? v : def;
        }

        public string PsoContainer { get { return "CN=Password Settings Container,CN=System," + DomainDn; } }
        /// <summary>The fine-grained password policy that applies to a user: one linked to the user wins, else the lowest
        /// precedence among the user's global groups' policies; null: the domain's policy applies.</summary>
        public WsAdObj ResultantPso(WsDir d, WsAdObj user)
        {
            var psos = d.Children(PsoContainer).Where(p => p.Is("msDS-PasswordSettings")).ToList();
            Func<WsAdObj, int> prec = p => { int v; return int.TryParse(p.Get("precedence"), out v) ? v : 999999; };
            var direct = psos.Where(p => p.List("appliesTo").Contains(user.Guid, StringComparer.OrdinalIgnoreCase)).OrderBy(prec).FirstOrDefault();
            if (direct != null) return direct;
            var groups = new HashSet<string>(GroupsOf(d, user, true).Where(g => g.Get("groupScope") == "Global").Select(g => g.Guid), StringComparer.OrdinalIgnoreCase);
            return psos.Where(p => p.List("appliesTo").Any(groups.Contains)).OrderBy(prec).FirstOrDefault();
        }
    }
}
