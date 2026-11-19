// Cert Lab - ActiveDirectory module: users, groups, computers and OUs (Get / New / Set / Remove), group membership,
// account state (enable, disable, unlock, reset a password, Search-ADAccount).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static partial void RegisterWinServer()
        {
            RegWsAd(); RegWsAd2(); RegWsAd3(); RegWsAdPolicies(); RegWsAdDeploy(); RegWsGp(); RegWsDns(); RegWsDhcp(); RegWsHv(); RegWsFiles(); RegWsStorage(); RegWsDfs(); RegWsHybrid();
        }
        static partial void RegWsAd2(); static partial void RegWsAd3(); static partial void RegWsGp(); static partial void RegWsDns(); static partial void RegWsDhcp(); static partial void RegWsHv();
        static partial void RegWsFiles(); static partial void RegWsStorage(); static partial void RegWsDfs(); static partial void RegWsHybrid();

        const string AdGet = "Identity:object@0 pipe ps=Identity; Filter:object ! ps=Filter; LDAPFilter:string ! ps=LDAPFilter; SearchBase:string; SearchScope:string set=Base|OneLevel|Subtree|0|1|2; Properties:string[] alias=Property; Server:string; Credential:pscredential; ResultSetSize:int";
        const string AdCommon = "; Server:string; Credential:pscredential; PassThru:switch";

        static CmdletDef AdCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Add(name, AdMod, spec, run); d.Available = HasAd; d.FqClass = "Microsoft.ActiveDirectory.Management.Commands." + name.Replace("-", "") + "Cmdlet"; return d; }
        static void AdFail(CmdCtx c, string err, object target) { if (err != null) throw c.Fail(err, "ActiveDirectoryCmdlet:Microsoft.ActiveDirectory.Management.ADException", "NotSpecified", target, "Microsoft.ActiveDirectory.Management.ADException"); }

        static void AdGetCmd(string name, string cls)
        {
            var d = AdCmd(name, AdGet, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c);
                if (c.Has("Identity")) { c.Out(AdObj(lab, dir, AdFind(c, dir, c.Get("Identity"), cls), AdExtra(c))); return; }
                if (c.Has("LDAPFilter")) throw c.Fail("Cert Lab: use -Filter (the PowerShell filter language); -LDAPFilter isn't simulated.", "NotSimulated", "NotImplemented", null, "System.NotSupportedException");
                int n = 0, max = c.Int("ResultSetSize", int.MaxValue);
                foreach (var x in AdSearch(c, dir, cls).ToList()) { if (n++ >= max) break; c.Out(AdObj(lab, dir, x, AdExtra(c))); }
            });
            d.DefaultSet = "Filter";
        }

        static void SetAdAttrs(CmdCtx c, WsAdObj o, string[] names)
        {
            foreach (var n in names) if (c.Has(n)) o.Set(AdAttr(n), c.Str(n));
            var rep = c.Hash("Replace"); if (rep != null) foreach (DictionaryEntry e in rep) o.Set(AdAttr(PsConv.ToStr(e.Key)), PsConv.ToStr(e.Value));
            var add = c.Hash("Add"); if (add != null) foreach (DictionaryEntry e in add) foreach (var v in PsConv.Items(e.Value)) o.AddTo(AdAttr(PsConv.ToStr(e.Key)), PsConv.ToStr(v));
            var rem = c.Hash("Remove"); if (rem != null) foreach (DictionaryEntry e in rem) foreach (var v in PsConv.Items(e.Value)) o.RemoveFrom(AdAttr(PsConv.ToStr(e.Key)), PsConv.ToStr(v));
            if (c.Has("Clear")) foreach (var k in c.Strs("Clear")) o.Set(AdAttr(k), null);
        }
        static readonly string[] UserAttrs = { "GivenName", "Surname", "DisplayName", "Department", "Title", "Description", "City", "Company", "Office", "EmailAddress", "UserPrincipalName", "SamAccountName" };

        static void RegWsAd()
        {
            AdGetCmd("Get-ADUser", "user"); AdGetCmd("Get-ADGroup", "group"); AdGetCmd("Get-ADComputer", "computer"); AdGetCmd("Get-ADOrganizationalUnit", "organizationalUnit");
            var go = AdCmd("Get-ADObject", AdGet + "; IncludeDeletedObjects:switch", (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c);
                if (c.Has("Identity")) { c.Out(AdObj(lab, dir, AdFind(c, dir, c.Get("Identity"), null), AdExtra(c))); return; }
                var f = c.Get("Filter"); string text = f is PsScriptBlock ? ((PsScriptBlock)f).Text : PsConv.ToStr(f ?? "*");
                var filter = AdFilter.Parse(text, n => PsConv.ToStr(c.R.GetVar(n, c.Scope)));
                foreach (var x in dir.Objs.Values.Where(x => (c.Sw("IncludeDeletedObjects") || !x.Deleted) && !x.Is("dnsZone")).OrderBy(x => x.Dn.Length).ToList())
                    if (filter.Match(x) && (!c.Has("SearchBase") || Dn.Under(x.Deleted ? x.Get("lastKnownParent") : x.Dn, c.Str("SearchBase")))) { var p = AdObj(lab, dir, x, AdExtra(c)); if (x.Deleted) { p.Val("Deleted", "bool", true); p.Val("LastKnownParent", "string", x.Get("lastKnownParent")); } c.Out(p); }
            });
            go.DefaultSet = "Filter";

            AdCmd("New-ADUser", "Name:string@0 ! prop; SamAccountName:string prop; UserPrincipalName:string prop; GivenName:string prop; Surname:string prop; DisplayName:string prop; Path:string prop; AccountPassword:securestring prop; Enabled:bool prop; ChangePasswordAtLogon:bool prop; Department:string prop; Title:string prop; Description:string prop; City:string prop; Company:string prop; Office:string prop; EmailAddress:string prop; OtherAttributes:hashtable; PasswordNeverExpires:bool prop" + AdCommon, (c, o) =>
            {
                string dc = AdDc(c), err; var lab = Lab(c);
                string pw = c.Has("AccountPassword") ? Pw(c.Get("AccountPassword")) : null;
                bool enabled = c.Has("Enabled") && PsConv.ToBool(c.Get("Enabled"));
                var u = lab.NewUser(dc, c.Str("Path"), c.Str("Name"), c.Str("SamAccountName"), c.Str("UserPrincipalName"), enabled, pw, out err);
                AdFail(c, err, c.Str("Name"));
                SetAdAttrs(c, u, UserAttrs.Where(n => n != "SamAccountName").ToArray());
                var oa = c.Hash("OtherAttributes"); if (oa != null) foreach (DictionaryEntry e in oa) u.Set(PsConv.ToStr(e.Key), PsConv.ToStr(e.Value));
                if (c.Has("ChangePasswordAtLogon") && PsConv.ToBool(c.Get("ChangePasswordAtLogon"))) u.Set("pwdLastSet", "0");
                lab.Touch(dc, u);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, lab.Dir(dc), u, null));
            });
            AdCmd("Set-ADUser", "Identity:object@0 ! pipe; Replace:hashtable; Add:hashtable; Remove:hashtable; Clear:string[]; Enabled:bool; ChangePasswordAtLogon:bool; GivenName:string; Surname:string; DisplayName:string; Department:string; Title:string; Description:string; City:string; Company:string; Office:string; EmailAddress:string; UserPrincipalName:string; SamAccountName:string; Manager:object" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c);
                var u = AdFind(c, dir, c.Get("Identity"), "user");
                SetAdAttrs(c, u, UserAttrs);
                if (c.Has("Enabled")) u.Set("enabled", PsConv.ToBool(c.Get("Enabled")) ? "TRUE" : "FALSE");
                if (c.Has("ChangePasswordAtLogon")) u.Set("pwdLastSet", PsConv.ToBool(c.Get("ChangePasswordAtLogon")) ? "0" : WsText.Date(lab.Now));
                if (c.Has("Manager")) u.Set("manager", AdFind(c, dir, c.Get("Manager"), "user").Dn);
                lab.Touch(dc, u);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, u, null));
            });
            foreach (var x in new[] { "User|user", "Group|group", "Computer|computer" })
            {
                var p = x.Split('|'); string cls = p[1];
                var rd = AdCmd("Remove-AD" + p[0], "Identity:object@0 ! pipe; Server:string; Credential:pscredential", (c, o) =>
                {
                    string dc; var dir = AdDir(c, out dc);
                    var t = AdFind(c, dir, c.Get("Identity"), cls);
                    if (!c.ShouldProcess(t.Dn, "Remove")) return;
                    AdFail(c, Lab(c).Delete(dc, t, false), t.Dn);
                });
                rd.ShouldProcess = true; rd.ConfirmImpact = "High";
            }
            foreach (var x in new[] { "Enable", "Disable" })
            {
                bool en = x == "Enable";
                AdCmd(x + "-ADAccount", "Identity:object@0 ! pipe" + AdCommon, (c, o) =>
                {
                    string dc; var dir = AdDir(c, out dc); var t = AdFind(c, dir, c.Get("Identity"), "principal");
                    if (en && t.Is("user") && t.Get("password").Length == 0) AdFail(c, "The password does not meet the length, complexity, or history requirement of the domain: set a password before enabling the account.", t.Dn);
                    t.Set("enabled", en ? "TRUE" : "FALSE"); Lab(c).Touch(dc, t);
                    if (c.Sw("PassThru")) c.Out(AdObj(Lab(c), dir, t, null));
                });
            }
            AdCmd("Unlock-ADAccount", "Identity:object@0 ! pipe" + AdCommon, (c, o) => { string dc; var dir = AdDir(c, out dc); var t = AdFind(c, dir, c.Get("Identity"), "user"); t.Set("lockedOut", null); Lab(c).Touch(dc, t); });
            AdCmd("Set-ADAccountPassword", "Identity:object@0 ! pipe; NewPassword:securestring; OldPassword:securestring; Reset:switch" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var t = AdFind(c, dir, c.Get("Identity"), "user");
                string pw = c.Has("NewPassword") ? Pw(c.Get("NewPassword")) : Pw(PsConv.ToStr(c.Prompt("Password for user " + t.Name, true) ?? ""));
                if (!c.Sw("Reset") && Pw(c.Get("OldPassword")) != t.Get("password")) AdFail(c, "The specified network password is not correct.", t.Dn);
                AdFail(c, lab.PasswordProblem(dir, t, pw ?? ""), t.Dn);
                t.Set("password", pw).Set("pwdLastSet", WsText.Date(lab.Now)); lab.Touch(dc, t);
            });
            AdCmd("Search-ADAccount", "LockedOut:switch; AccountDisabled:switch; PasswordNeverExpires:switch; AccountExpired:switch; UsersOnly:switch; ComputersOnly:switch; SearchBase:string; Server:string; Credential:pscredential", (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c);
                foreach (var t in dir.Live.Where(x => (x.Is("user") && !c.Sw("ComputersOnly") || x.Is("computer") && !c.Sw("UsersOnly")) && (!c.Has("SearchBase") || Dn.Under(x.Dn, c.Str("SearchBase")))).OrderBy(x => x.Dn).ToList())
                {
                    bool hit = c.Sw("LockedOut") && t.Yes("lockedOut") || c.Sw("AccountDisabled") && t.Get("enabled") == "FALSE" || c.Sw("PasswordNeverExpires") && t.Yes("passwordNeverExpires");
                    if (hit) c.Out(AdObj(lab, dir, t, new[] { "LockedOut" }));
                }
            });
        }
    }
}
