// Cert Lab - ActiveDirectory module: groups and membership, computers, OUs, and generic objects (move, rename,
// protect, delete, restore from the Recycle Bin).
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
        static partial void RegWsAd2()
        {
            AdCmd("New-ADGroup", "Name:string@0 ! prop; GroupScope:string@1 ! prop set=DomainLocal|Global|Universal|0|1|2; GroupCategory:string prop set=Distribution|Security|0|1; Path:string prop; SamAccountName:string prop; Description:string prop; DisplayName:string prop" + AdCommon, (c, o) =>
            {
                string dc = AdDc(c), err; var lab = Lab(c);
                var g = lab.NewGroup(dc, c.Str("Path"), c.Str("Name"), c.Str("SamAccountName"), c.Str("GroupScope"), c.Str("GroupCategory"), out err);
                AdFail(c, err, c.Str("Name"));
                if (c.Has("Description")) { g.Set("description", c.Str("Description")); lab.Touch(dc, g); }
                if (c.Sw("PassThru")) c.Out(AdObj(lab, lab.Dir(dc), g, null));
            });
            AdCmd("Set-ADGroup", "Identity:object@0 ! pipe; GroupScope:string set=DomainLocal|Global|Universal|0|1|2; GroupCategory:string set=Distribution|Security|0|1; Description:string; DisplayName:string; Replace:hashtable; Clear:string[]" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var g = AdFind(c, dir, c.Get("Identity"), "group");
                if (c.Has("GroupScope")) AdFail(c, lab.SetScope(dc, g, c.Str("GroupScope")), g.Dn);
                if (c.Has("GroupCategory")) g.Set("groupCategory", c.Str("GroupCategory").StartsWith("D", StringComparison.OrdinalIgnoreCase) || c.Str("GroupCategory") == "0" ? "Distribution" : "Security");
                SetAdAttrs(c, g, new[] { "Description", "DisplayName" });
                lab.Touch(dc, g);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, g, null));
            });
            AdCmd("Add-ADGroupMember", "Identity:object@0 ! pipe; Members:object[]@1 !" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var g = AdFind(c, dir, c.Get("Identity"), "group");
                foreach (var m in AdFindAll(c, dir, "Members", "principal")) AdFail(c, lab.AddMember(dc, g, m), m.Dn);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, g, null));
            });
            var rgm = AdCmd("Remove-ADGroupMember", "Identity:object@0 ! pipe; Members:object[]@1 !" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var g = AdFind(c, dir, c.Get("Identity"), "group");
                var ms = AdFindAll(c, dir, "Members", "principal");
                if (!c.ShouldProcess(g.Dn, "Remove member(s) " + string.Join(",", ms.Select(m => m.Dn).ToArray()))) return;
                foreach (var m in ms) lab.RemoveMember(dc, g, m);
            });
            rgm.ShouldProcess = true; rgm.ConfirmImpact = "High";
            AdCmd("Get-ADGroupMember", "Identity:object@0 ! pipe; Recursive:switch; Server:string; Credential:pscredential", (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var g = AdFind(c, dir, c.Get("Identity"), "group");
                foreach (var m in lab.Members(dir, g, c.Sw("Recursive"))) c.Out(PrincipalObj(m));
            });
            AdCmd("Get-ADPrincipalGroupMembership", "Identity:object@0 ! pipe; Server:string; Credential:pscredential", (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var p = AdFind(c, dir, c.Get("Identity"), "principal");
                foreach (var g in lab.GroupsOf(dir, p, false).OrderBy(x => x.Name)) c.Out(AdObj(lab, dir, g, null));
            });
            AdCmd("Add-ADPrincipalGroupMembership", "Identity:object@0 ! pipe; MemberOf:object[]@1 !" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var p = AdFind(c, dir, c.Get("Identity"), "principal");
                foreach (var g in AdFindAll(c, dir, "MemberOf", "group")) AdFail(c, lab.AddMember(dc, g, p), g.Dn);
            });
            AdCmd("New-ADComputer", "Name:string@0 ! prop; Path:string prop; SamAccountName:string prop; Enabled:bool prop; Description:string prop; DNSHostName:string prop" + AdCommon, (c, o) =>
            {
                string dc = AdDc(c), err; var lab = Lab(c);
                var x = lab.NewComputer(dc, c.Str("Path"), c.Str("Name"), out err);
                AdFail(c, err, c.Str("Name"));
                if (c.Has("Description")) x.Set("description", c.Str("Description"));
                if (c.Has("Enabled")) x.Set("enabled", PsConv.ToBool(c.Get("Enabled")) ? "TRUE" : "FALSE");
                lab.Touch(dc, x);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, lab.Dir(dc), x, null));
            });
            AdCmd("Set-ADComputer", "Identity:object@0 ! pipe; Description:string; Enabled:bool; Location:string; Replace:hashtable; Clear:string[]; PrincipalsAllowedToDelegateToAccount:object[]" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var x = AdFind(c, dir, c.Get("Identity"), "computer");
                SetAdAttrs(c, x, new[] { "Description", "Location" });
                if (c.Has("Enabled")) x.Set("enabled", PsConv.ToBool(c.Get("Enabled")) ? "TRUE" : "FALSE");
                if (c.Has("PrincipalsAllowedToDelegateToAccount")) x.SetList("allowedToActOnBehalf", AdFindAll(c, dir, "PrincipalsAllowedToDelegateToAccount", "principal").Select(p => p.Guid));
                lab.Touch(dc, x);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            AdCmd("New-ADOrganizationalUnit", "Name:string@0 ! prop; Path:string prop; ProtectedFromAccidentalDeletion:bool prop; Description:string prop; DisplayName:string prop" + AdCommon, (c, o) =>
            {
                string dc = AdDc(c), err; var lab = Lab(c);
                bool protect = !c.Has("ProtectedFromAccidentalDeletion") || PsConv.ToBool(c.Get("ProtectedFromAccidentalDeletion"));
                var x = lab.NewOu(dc, c.Str("Path") ?? lab.DomainDn, c.Str("Name"), protect, out err);
                AdFail(c, err, c.Str("Name"));
                if (c.Has("Description")) { x.Set("description", c.Str("Description")); lab.Touch(dc, x); }
                if (c.Sw("PassThru")) c.Out(AdObj(lab, lab.Dir(dc), x, null));
            });
            AdCmd("Set-ADOrganizationalUnit", "Identity:object@0 ! pipe; ProtectedFromAccidentalDeletion:bool; Description:string; DisplayName:string; City:string; Replace:hashtable; Clear:string[]" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var x = AdFind(c, dir, c.Get("Identity"), "organizationalUnit");
                if (c.Has("ProtectedFromAccidentalDeletion")) x.Set("protectedFromAccidentalDeletion", PsConv.ToBool(c.Get("ProtectedFromAccidentalDeletion")) ? "TRUE" : null);
                SetAdAttrs(c, x, new[] { "Description", "DisplayName", "City" });
                lab.Touch(dc, x);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            foreach (var n in new[] { "Remove-ADOrganizationalUnit|organizationalUnit", "Remove-ADObject|" })
            {
                var p = n.Split('|'); string cls = p[1].Length == 0 ? null : p[1];
                var rd = AdCmd(p[0], "Identity:object@0 ! pipe; Recursive:switch; Server:string; Credential:pscredential", (c, o) =>
                {
                    string dc; var dir = AdDir(c, out dc); var t = AdFind(c, dir, c.Get("Identity"), cls);
                    if (!c.ShouldProcess(t.Dn, "Remove")) return;
                    AdFail(c, Lab(c).Delete(dc, t, c.Sw("Recursive")), t.Dn);
                });
                rd.ShouldProcess = true; rd.ConfirmImpact = "High";
            }
            AdCmd("Set-ADObject", "Identity:object@0 ! pipe; ProtectedFromAccidentalDeletion:bool; Description:string; DisplayName:string; Replace:hashtable; Add:hashtable; Remove:hashtable; Clear:string[]" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var x = AdFind(c, dir, c.Get("Identity"), null);
                if (c.Has("ProtectedFromAccidentalDeletion")) x.Set("protectedFromAccidentalDeletion", PsConv.ToBool(c.Get("ProtectedFromAccidentalDeletion")) ? "TRUE" : null);
                SetAdAttrs(c, x, new[] { "Description", "DisplayName" });
                lab.Touch(dc, x);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            AdCmd("Move-ADObject", "Identity:object@0 ! pipe; TargetPath:string@1 !" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var x = AdFind(c, dir, c.Get("Identity"), null);
                AdFail(c, lab.Move(dc, x, c.Str("TargetPath")), x.Dn);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            AdCmd("Rename-ADObject", "Identity:object@0 ! pipe; NewName:string@1 !" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c); var x = AdFind(c, dir, c.Get("Identity"), null);
                AdFail(c, lab.Rename(dc, x, c.Str("NewName")), x.Dn);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            AdCmd("Restore-ADObject", "Identity:object@0 ! pipe; NewName:string; TargetPath:string" + AdCommon, (c, o) =>
            {
                string dc; var dir = AdDir(c, out dc); var lab = Lab(c);
                var v = PsConv.Base(c.Get("Identity")); var po = v as PsObj;
                WsAdObj x = po != null && po.Base is WsAdObj ? dir.Objs.Values.FirstOrDefault(y => y.Guid == ((WsAdObj)po.Base).Guid) : dir.Objs.Values.FirstOrDefault(y => WsText.Eq(y.Guid, PsConv.ToStr(v)) || WsText.Eq(y.Dn, PsConv.ToStr(v)));
                if (x == null) throw NotFound(c, PsConv.ToStr(v), lab);
                AdFail(c, lab.Restore(dc, x), x.Dn);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
        }

        /// <summary>What Get-ADGroupMember prints: an ADPrincipal (name, class, DN, SAM, SID).</summary>
        static PsObj PrincipalObj(WsAdObj m)
        {
            var p = new PsObj("Microsoft.ActiveDirectory.Management.ADPrincipal", "Microsoft.ActiveDirectory.Management.ADObject", "System.Object") { Base = m };
            p.Val("distinguishedName", "string", m.Dn); p.Val("name", "string", m.Name); p.Val("objectClass", "string", m.Class); p.Val("objectGUID", "guid", m.Guid);
            p.Val("SamAccountName", "string", m.Sam); p.Val("SID", "System.Security.Principal.SecurityIdentifier", m.Get("objectSid"));
            p.ToStr = x => m.Dn;
            return p;
        }
    }
}
