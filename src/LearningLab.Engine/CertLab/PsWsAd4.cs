// Cert Lab - ActiveDirectory module: password policies (the domain's, fine-grained ones), optional features (the
// Recycle Bin), trusts, the KDS root key and group managed service accounts; ADDSDeployment: promoting a member server
// to a domain controller (Install-ADDSDomainController, also read-only) and demoting one.
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
        static object Days(string v) { int n; return int.TryParse(v, out n) ? (object)TimeSpan.FromDays(n) : TimeSpan.Zero; }
        static object Mins(string v) { int n; return int.TryParse(v, out n) ? (object)TimeSpan.FromMinutes(n) : TimeSpan.Zero; }
        static PsObj PasswordObj(string type, WsAdObj src, Func<string, string> get, string dn)
        {
            var p = new PsObj("Microsoft.ActiveDirectory.Management." + type, "Microsoft.ActiveDirectory.Management.ADEntity", "System.Object") { Base = src };
            p.Val("ComplexityEnabled", "bool", WsText.True(get("complexityEnabled"))); p.Val("DistinguishedName", "string", dn);
            p.Val("LockoutDuration", "timespan", Mins(get("lockoutDuration"))); p.Val("LockoutObservationWindow", "timespan", Mins(get("lockoutDuration")));
            p.Val("LockoutThreshold", "int", int.Parse("0" + get("lockoutThreshold"), CultureInfo.InvariantCulture)); p.Val("MaxPasswordAge", "timespan", Days(get("maxPwdAge")));
            p.Val("MinPasswordAge", "timespan", Days(get("minPwdAge"))); p.Val("MinPasswordLength", "int", int.Parse("0" + get("minPwdLength"), CultureInfo.InvariantCulture));
            p.Val("PasswordHistoryCount", "int", int.Parse("0" + get("pwdHistoryLength"), CultureInfo.InvariantCulture)); p.Val("ReversibleEncryptionEnabled", "bool", false);
            if (type == "ADFineGrainedPasswordPolicy") { p.Val("Name", "string", src.Name); p.Val("Precedence", "int", int.Parse("0" + get("precedence"), CultureInfo.InvariantCulture)); p.Val("AppliesTo", "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection", src.List("appliesTo").Cast<object>().ToArray()); }
            p.ToStr = x => dn;
            return p;
        }
        static readonly string[][] PwParams = { new[] { "MinPasswordLength", "minPwdLength" }, new[] { "PasswordHistoryCount", "pwdHistoryLength" }, new[] { "LockoutThreshold", "lockoutThreshold" }, new[] { "ComplexityEnabled", "complexityEnabled" }, new[] { "Precedence", "precedence" } };
        static void SetPw(CmdCtx c, WsAdObj o)
        {
            foreach (var p in PwParams) if (c.Has(p[0])) o.Set(p[1], p[0] == "ComplexityEnabled" ? (PsConv.ToBool(c.Get(p[0])) ? "TRUE" : "FALSE") : c.Str(p[0]));
            foreach (var p in new[] { "MaxPasswordAge|maxPwdAge", "MinPasswordAge|minPwdAge", "LockoutDuration|lockoutDuration" })
            {
                var q = p.Split('|'); if (!c.Has(q[0])) continue;
                var v = PsConv.Base(c.Get(q[0])); TimeSpan ts;
                if (v is TimeSpan) ts = (TimeSpan)v; else if (!TimeSpan.TryParse(PsConv.ToStr(v), CultureInfo.InvariantCulture, out ts)) ts = TimeSpan.FromDays(PsConv.ToInt(v));
                o.Set(q[1], (q[1] == "lockoutDuration" ? (int)ts.TotalMinutes : (int)ts.TotalDays).ToString(CultureInfo.InvariantCulture));
            }
        }
        const string PwSpec = "MinPasswordLength:int; PasswordHistoryCount:int; LockoutThreshold:int; ComplexityEnabled:bool; MaxPasswordAge:object; MinPasswordAge:object; LockoutDuration:object; LockoutObservationWindow:object; ReversibleEncryptionEnabled:bool";

        static void RegWsAdPolicies()
        {
            AdCmd("Get-ADDefaultDomainPasswordPolicy", "Identity:object@0 pipe; Current:string; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var dom = dir.ByDn(lab.DomainDn);
                c.Out(PasswordObj("ADDefaultDomainPasswordPolicy", dom, k => dom.Get(k), lab.DomainDn));
            });
            AdCmd("Set-ADDefaultDomainPasswordPolicy", "Identity:object@0 ! pipe; " + PwSpec + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var dom = dir.ByDn(lab.DomainDn);
                SetPw(c, dom); lab.Touch(dc, dom);
            });
            AdCmd("New-ADFineGrainedPasswordPolicy", "Name:string@0 !; Precedence:int@1 !; DisplayName:string; Description:string; ProtectedFromAccidentalDeletion:bool; " + PwSpec + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                if (dir.Children(lab.PsoContainer).Any(x => WsText.Eq(x.Name, c.Str("Name")))) AdFail(c, "A fine-grained password policy named " + c.Str("Name") + " already exists.", c.Str("Name"));
                var p = lab.Create(dc, "msDS-PasswordSettings", "CN=" + c.Str("Name") + "," + lab.PsoContainer, "minPwdLength", "7", "pwdHistoryLength", "24", "maxPwdAge", "42", "minPwdAge", "1", "complexityEnabled", "TRUE", "lockoutThreshold", "0", "lockoutDuration", "30");
                SetPw(c, p);
                if (!c.Has("ProtectedFromAccidentalDeletion") || PsConv.ToBool(c.Get("ProtectedFromAccidentalDeletion"))) p.Set("protectedFromAccidentalDeletion", "TRUE");
                lab.Touch(dc, p);
                if (c.Sw("PassThru")) c.Out(PasswordObj("ADFineGrainedPasswordPolicy", p, k => p.Get(k), p.Dn));
            });
            AdCmd("Get-ADFineGrainedPasswordPolicy", "Identity:object@0 pipe ps=Identity; Filter:string ps=Filter; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                foreach (var p in dir.Children(lab.PsoContainer).Where(x => !c.Has("Identity") || WsText.Eq(x.Name, PsConv.ToStr(PsConv.Base(c.Get("Identity"))))).ToList()) c.Out(PasswordObj("ADFineGrainedPasswordPolicy", p, k => p.Get(k), p.Dn));
            }).DefaultSet = "Filter";
            AdCmd("Add-ADFineGrainedPasswordPolicySubject", "Identity:object@0 ! pipe; Subjects:object[]@1 !" + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                var p = dir.Children(lab.PsoContainer).FirstOrDefault(x => WsText.Eq(x.Name, PsConv.ToStr(PsConv.Base(c.Get("Identity")))));
                if (p == null) throw NotFound(c, PsConv.ToStr(c.Get("Identity")), lab);
                foreach (var s in AdFindAll(c, dir, "Subjects", "principal"))
                {
                    if (s.Is("group") && s.Get("groupScope") != "Global") AdFail(c, "A fine-grained password policy applies to users and global security groups; " + s.Name + " is a " + s.Get("groupScope") + " group.", s.Dn);
                    p.AddTo("appliesTo", s.Guid);
                }
                lab.Touch(dc, p);
            });
            AdCmd("Get-ADUserResultantPasswordPolicy", "Identity:object@0 ! pipe; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                var u = AdFind(c, dir, c.Get("Identity"), "user");
                var p = lab.ResultantPso(dir, u);
                if (p != null) c.Out(PasswordObj("ADFineGrainedPasswordPolicy", p, k => p.Get(k), p.Dn));
            });
            var en = AdCmd("Enable-ADOptionalFeature", "Identity:object@0 !; Scope:string@1 ! set=ForestOrConfigurationSet|Domain; Target:string@2 !; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                if (!PsConv.ToStr(PsConv.Base(c.Get("Identity"))).StartsWith("Recycle Bin", StringComparison.OrdinalIgnoreCase)) AdFail(c, "Cert Lab simulates the Recycle Bin Feature only.", c.Get("Identity"));
                if (!c.ShouldProcess("Recycle Bin Feature", "Enable Recycle Bin Feature on '" + lab.ConfigDn + "' (this can't be undone)")) return;
                var f = dir.ByDn("CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services," + lab.ConfigDn);
                f.Set("enabled", "TRUE"); lab.Touch(dc, f);
            });
            en.ShouldProcess = true; en.ConfirmImpact = "High";
            AdCmd("Get-ADOptionalFeature", "Identity:object@0 pipe ps=Identity; Filter:string ps=Filter; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c);
                var p = Ado("ADOptionalFeature", "DistinguishedName", "CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services," + lab.ConfigDn, "EnabledScopes", lab.RecycleBin ? new object[] { "CN=Partitions," + lab.ConfigDn } : new object[0], "FeatureScope", new object[] { "ForestOrConfigurationSet" }, "IsDisableable", false, "Name", "Recycle Bin Feature", "RequiredForestMode", "Windows2008R2Forest");
                c.Out(p);
            }).DefaultSet = "Filter";
            AdCmd("Get-ADTrust", "Identity:object@0 pipe ps=Identity; Filter:string ps=Filter; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c);
                foreach (var t in lab.Trusts) c.Out(Ado("ADTrust", "Direction", t.Direction, "DisallowTransivity", !t.Transitive, "ForestTransitive", t.Type == "Forest", "Name", t.Name, "SelectiveAuthentication", t.Selective, "Source", lab.DomainDn, "Target", t.Name, "TrustType", t.Type == "Realm" ? "MIT" : "Uplevel"));
            }).DefaultSet = "Filter";
        }
    }
}
