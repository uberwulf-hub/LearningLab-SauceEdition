// Cert Lab - promoting and demoting domain controllers (ADDSDeployment), and group managed service accounts with the
// KDS root key they need (KdsSvc and ActiveDirectory modules).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        /// <summary>Makes a member server a domain controller: it gets a copy of the directory from source, a server object in
        /// the site, the SYSVOL share, DNS if asked; its computer account moves to the Domain Controllers OU.</summary>
        public string PromoteDc(string server, string site, bool dns, bool rodc, string source) { return PromoteDc(server, site, dns, rodc, source, true); }
        public string PromoteDc(string server, string site, bool dns, bool rodc, string source, bool gc)
        {
            var s = Server(server);
            if (s == null || s.IsClient) return "Only a Windows Server can become a domain controller.";
            if (Dirs.ContainsKey(s.Name)) return s.Name + " is already a domain controller.";
            if (!s.Has("AD-Domain-Services")) return "Verification of prerequisites for Domain Controller promotion failed. The Active Directory Domain Services role isn't installed: Install-WindowsFeature AD-Domain-Services -IncludeManagementTools first.";
            string src = source ?? DcNames.FirstOrDefault(DcUp);
            if (src == null || !DcUp(src)) return "No domain controller of " + DomainDns + " could be contacted to replicate from.";
            site = site ?? SiteOfIp(s.M.Ip) ?? "HQ";
            var d = Dir(src);
            var st = Sites(d).FirstOrDefault(x => WsText.Eq(x.Name, site));
            if (st == null) return "The site " + site + " doesn't exist.";
            Create(src, "server", "CN=" + s.Name + ",CN=Servers," + st.Dn, "dNSHostName", s.M.Fqdn, "options", gc ? "1" : "0");
            var comp = d.Find(s.Name, "computer");
            if (comp != null) { Move(src, comp, "OU=Domain Controllers," + DomainDn); comp.Set("primaryGroup", rodc ? "Read-only Domain Controllers" : "Domain Controllers").Set("isDc", "TRUE"); Touch(src, comp); }
            Dirs[s.Name] = d.Clone(s.Name);
            InstallFeatures(s.M, "RSAT-AD-PowerShell", "GPMC");
            if (dns) { InstallFeatures(s.M, "DNS", "RSAT-DNS-Server"); }
            var t = Now;
            s.M.MakeDir("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\Policies", t); s.M.MakeDir("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\scripts", t);
            s.M.MakeDir("C:\\Windows\\NTDS", t);
            if (rodc) s.M.File("C:\\Windows\\NTDS\\rodc.flag", "", t);
            if (!s.Shares.Any(x => x.Name == "SYSVOL")) { s.Shares.Add(new WsShare { Name = "SYSVOL", Path = "C:\\Windows\\SYSVOL\\sysvol", Description = "Logon server share" }); s.Shares.Add(new WsShare { Name = "NETLOGON", Path = "C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\scripts", Description = "Logon server share" }); }
            SysvolSync();
            Changed(src);
            Note(s.Name + " was promoted to a " + (rodc ? "read-only " : "") + "domain controller in the site " + st.Name + ".");
            return null;
        }
        public string DemoteDc(string server)
        {
            var s = Server(server);
            if (s == null || !Dirs.ContainsKey(s.Name)) return server + " isn't a domain controller.";
            var others = DcNames.Where(x => !WsText.Eq(x, s.Name) && DcUp(x)).ToList();
            if (others.Count == 0) return "This is the last domain controller of " + DomainDns + ": removing it removes the domain (Cert Lab doesn't do that).";
            foreach (var r in FsmoRoles.Where(r => WsText.Eq(Fsmo[r], s.Name)).ToList()) Fsmo[r] = others[0];
            var dc = others[0]; var d = Dir(dc);
            ReplicatePair(s.Name, dc, false);
            var so = ServerObj(d, s.Name); if (so != null) { so.Deleted = true; Touch(dc, so); }
            var comp = d.Find(s.Name, "computer");
            if (comp != null) { Move(dc, comp, ComputersDn); comp.Set("primaryGroup", "Domain Computers").Set("isDc", null); Touch(dc, comp); }
            Dirs.Remove(s.Name);
            s.Shares.RemoveAll(x => x.Name == "SYSVOL" || x.Name == "NETLOGON");
            Note(s.Name + " is no longer a domain controller.");
            return null;
        }
        public string KdsContainer { get { return "CN=Master Root Keys,CN=Group Key Distribution Service,CN=Services," + ConfigDn; } }
        public bool KdsReady(WsDir d) { return d.Children(KdsContainer).Any(k => DateTime.Parse(k.Get("effectiveTime"), CultureInfo.InvariantCulture) <= Now); }
    }

    public partial class PsCmdlets
    {
        static Func<PsMachine, bool> AdDeploy = m => m.Ws != null && m.FeatureInstalled("AD-Domain-Services");

        static void RegWsAdDeploy()
        {
            var idc = Add("Install-ADDSDomainController", "ADDSDeployment", "DomainName:string !; Credential:pscredential; SiteName:string; InstallDns:switch; ReadOnlyReplica:switch; SafeModeAdministratorPassword:securestring; ReplicationSourceDC:string; NoGlobalCatalog:switch; DatabasePath:string; LogPath:string; SysvolPath:string; NoRebootOnCompletion:switch; Force:switch; AllowPasswordReplicationAccountName:string[]; DelegatedAdministratorAccountName:string", (c, o) =>
            {
                var lab = Lab(c);
                if (!WsText.Eq(c.Str("DomainName"), lab.DomainDns)) AdFail(c, "The domain " + c.Str("DomainName") + " could not be contacted.", c.Str("DomainName"));
                string pw = c.Has("SafeModeAdministratorPassword") ? Pw(c.Get("SafeModeAdministratorPassword")) : c.Prompt("SafeModeAdministratorPassword", true);
                if (string.IsNullOrEmpty(pw) || lab.PasswordProblem(lab.Main, null, pw) != null) AdFail(c, "The Directory Services Restore Mode password doesn't meet the domain's password rules.", null);
                if (!c.Sw("Force") && !c.ShouldProcess(c.M.Name, "The target server will be configured as a domain controller and restarted when this operation is complete")) return;
                AdFail(c, lab.PromoteDc(c.M.Name, c.Str("SiteName"), c.Sw("InstallDns"), c.Sw("ReadOnlyReplica"), c.Str("ReplicationSourceDC"), !c.Sw("NoGlobalCatalog")), c.M.Name);
                var r = new PsObj("Microsoft.DirectoryServices.Deployment.Types.DeploymentResult", "System.Object");
                r.Val("Message", "string", "Operation completed successfully"); r.Val("Context", "string", "DCPromo.General.1"); r.Val("RebootRequired", "bool", true); r.Val("Status", "string", "Success");
                c.Out(r);
                if (!c.Sw("NoRebootOnCompletion")) c.M.Reboot();
            });
            idc.Available = AdDeploy; idc.ShouldProcess = true; idc.ConfirmImpact = "High";
            var unf = Add("Uninstall-ADDSDomainController", "ADDSDeployment", "Credential:pscredential; LocalAdministratorPassword:securestring; DemoteOperationMasterRole:switch; RemoveDnsDelegation:switch; NoRebootOnCompletion:switch; Force:switch; LastDomainControllerInDomain:switch", (c, o) =>
            {
                var lab = Lab(c);
                if (!c.Sw("Force") && !c.ShouldProcess(c.M.Name, "The target server will be demoted and restarted")) return;
                AdFail(c, lab.DemoteDc(c.M.Name), c.M.Name);
                if (!c.Sw("NoRebootOnCompletion")) c.M.Reboot();
            });
            unf.Available = AdDeploy; unf.ShouldProcess = true; unf.ConfirmImpact = "High";
            var ifo = Add("Install-ADDSForest", "ADDSDeployment", "DomainName:string !; DomainNetbiosName:string; ForestMode:string; DomainMode:string; InstallDns:switch; SafeModeAdministratorPassword:securestring; NoRebootOnCompletion:switch; Force:switch", (c, o) =>
            {
                AdFail(c, "Verification of prerequisites failed: " + c.M.Name + " is a member of " + Lab(c).DomainDns + ". A new forest is created on a stand-alone server (Cert Lab's practice network already has its forest; the course explains the steps).", c.Str("DomainName"));
            });
            ifo.Available = AdDeploy;
            var kds = Add("Add-KdsRootKey", "Kds", "EffectiveImmediately:switch; EffectiveTime:datetime; LocalTestOnly:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = AdDc(c);
                var when = c.Sw("EffectiveImmediately") ? lab.Now.AddHours(10) : c.Has("EffectiveTime") ? (DateTime)PsConv.Base(c.Get("EffectiveTime")) : lab.Now.AddHours(10);
                var g = lab.NewGuid();
                lab.Create(dc, "msKds-ProvRootKey", "CN=" + g + "," + lab.KdsContainer, "effectiveTime", when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                c.Out(new Guid(g));
            });
            kds.Available = HasAd;
            var gk = Add("Get-KdsRootKey", "Kds", "", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                foreach (var k in dir.Children(lab.KdsContainer)) { var p = new PsObj("Microsoft.KeyDistributionService.Cmdlets.KdsRootKey", "System.Object"); p.Val("KeyId", "guid", k.Name); p.Val("EffectiveTime", "datetime", DateTime.Parse(k.Get("effectiveTime"), CultureInfo.InvariantCulture)); p.Val("IsFormatValid", "bool", true); c.Out(p); }
            });
            gk.Available = HasAd;
            AdCmd("New-ADServiceAccount", "Name:string@0 !; DNSHostName:string; PrincipalsAllowedToRetrieveManagedPassword:object[]; RestrictToSingleComputer:switch; Path:string; Description:string; ManagedPasswordIntervalInDays:int" + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                if (!c.Sw("RestrictToSingleComputer") && !lab.KdsReady(dir)) AdFail(c, "Key does not exist: there is no KDS root key in effect yet (Add-KdsRootKey; a key is usable from its effective time, 10 hours after it is added unless you say otherwise).", c.Str("Name"));
                string name = c.Str("Name");
                if (dir.Find(name + "$", null) != null) AdFail(c, "The specified account already exists.", name);
                var x = lab.Create(dc, "msDS-GroupManagedServiceAccount", "CN=" + name + "," + (c.Str("Path") ?? "CN=Managed Service Accounts," + lab.DomainDn), "sAMAccountName", name + "$", "dNSHostName", c.Str("DNSHostName") ?? name.ToLowerInvariant() + "." + lab.DomainDns, "enabled", "TRUE");
                if (c.Has("PrincipalsAllowedToRetrieveManagedPassword")) x.SetList("principalsAllowed", AdFindAll(c, dir, "PrincipalsAllowedToRetrieveManagedPassword", "principal").Select(p => p.Guid));
                lab.Touch(dc, x);
                if (c.Sw("PassThru")) c.Out(AdObj(lab, dir, x, null));
            });
            AdCmd("Set-ADServiceAccount", "Identity:object@0 ! pipe; PrincipalsAllowedToRetrieveManagedPassword:object[]; Description:string" + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var x = AdFind(c, dir, c.Get("Identity"), "msDS-GroupManagedServiceAccount");
                if (c.Has("PrincipalsAllowedToRetrieveManagedPassword")) x.SetList("principalsAllowed", AdFindAll(c, dir, "PrincipalsAllowedToRetrieveManagedPassword", "principal").Select(p => p.Guid));
                lab.Touch(dc, x);
            });
            AdCmd("Get-ADServiceAccount", AdGet, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                if (c.Has("Identity")) { c.Out(AdObj(lab, dir, AdFind(c, dir, c.Get("Identity"), "msDS-GroupManagedServiceAccount"), AdExtra(c))); return; }
                foreach (var x in AdSearch(c, dir, "msDS-GroupManagedServiceAccount").ToList()) c.Out(AdObj(lab, dir, x, AdExtra(c)));
            }).DefaultSet = "Filter";
            foreach (var n in new[] { "Install", "Test" })
            {
                bool test = n == "Test";
                AdCmd(n + "-ADServiceAccount", "Identity:object@0 ! pipe; Force:switch", (c, o) =>
                {
                    var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                    var x = AdFind(c, dir, c.Get("Identity"), "msDS-GroupManagedServiceAccount");
                    var me = dir.Find(c.M.Name, "computer");
                    bool ok = me != null && (x.List("principalsAllowed").Contains(me.Guid) || lab.GroupsOf(dir, me, true).Any(g => x.List("principalsAllowed").Contains(g.Guid)));
                    if (test) { if (ok && x.List("installedOn").Contains(c.M.Name)) c.Out(true); else { if (!ok) c.Warn("Test failed for Managed Service Account " + x.Name + ". If standalone Managed Service Account, the account is linked to another computer object in the Active Directory. If group Managed Service Account, either this computer does not have permission to use the group MSA or this computer does not support all the Kerberos encryption types required for the gMSA."); c.Out(false); } return; }
                    if (!ok) AdFail(c, "Cannot install service account. Error Message: 'The provided context did not match the target.': " + c.M.Name + " isn't allowed to retrieve the managed password of " + x.Name + ".", x.Name);
                    x.AddTo("installedOn", c.M.Name); lab.Touch(dc, x);
                });
            }
        }
    }
}
