// Cert Lab - the starting state of the AZ-800 course's network: the corp.example forest with its OUs, people, groups
// and computers (Harborline Logistics; every name is made up), the HQ site, the two default GPOs, and the roles each
// machine has before a lab begins. Later parts (DNS, DHCP, files, Hyper-V, Azure) seed their own state.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        // name|sam|ou (under Staff)|department|title|groups
        public static readonly string[] People = {
            "Alex Rivera|arivera|IT|IT|Systems administrator|IT Admins,Domain Admins",
            "Jamal Price|jprice|IT|IT|Help desk lead|Helpdesk",
            "Hugo Brandt|hbrandt|IT|IT|Help desk technician|Helpdesk",
            "Bianca Cole|bcole|Sales|Sales|Account manager|Sales",
            "Dev Patel|dpatel|Sales|Sales|Sales representative|Sales",
            "Ines Moreau|imoreau|Sales|Sales|Sales manager|Sales,Sales Managers",
            "Erin Walsh|ewalsh|Finance|Finance|Accountant|Finance",
            "Farid Haddad|fhaddad|Finance|Finance|Finance manager|Finance",
            "Grace Kim|gkim|HR|HR|HR partner|HR",
            "Kara Novak|knovak|HR|HR|Recruiter|HR" };
        public const string SeedPassword = "Harbor#2026";

        void Seed()
        {
            foreach (var r in FsmoRoles) Fsmo[r] = "DC1";
            Dirs["DC1"] = new WsDir { Dc = "DC1" };
            const string dc = "DC1";
            var dom = Create(dc, "domainDNS", DomainDn);
            dom.Set("name", "corp").Set("minPwdLength", "7").Set("pwdHistoryLength", "24").Set("maxPwdAge", "42").Set("minPwdAge", "1").Set("complexityEnabled", "TRUE").Set("lockoutThreshold", "0").Set("lockoutDuration", "10");
            dom.Set("domainMode", "Windows2016Domain").Set("forestMode", "Windows2016Forest");
            foreach (var c in new[] { "Users", "Computers", "System", "Builtin", "Configuration" }) Create(dc, c == "Builtin" ? "builtinDomain" : "container", "CN=" + c + "," + DomainDn);
            Create(dc, "container", PsoContainer);
            foreach (var c in new[] { "Sites", "Services" }) Create(dc, "container", "CN=" + c + "," + ConfigDn);
            Create(dc, "container", "CN=Subnets," + SitesDn);
            Create(dc, "container", "CN=Inter-Site Transports," + SitesDn);
            Create(dc, "container", "CN=IP,CN=Inter-Site Transports," + SitesDn);
            Create(dc, "msDS-OptionalFeature", "CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services," + ConfigDn, "enabled", "FALSE");
            Create(dc, "container", "CN=Master Root Keys,CN=Group Key Distribution Service,CN=Services," + ConfigDn);
            Create(dc, "container", "CN=Managed Service Accounts," + DomainDn);
            Create(dc, "container", "CN=Policies,CN=System," + DomainDn);
            Create(dc, "container", "CN=WMIPolicy,CN=System," + DomainDn);
            string err;
            var ouDc = NewOu(dc, DomainDn, "Domain Controllers", false, out err);
            ouDc.Set("protectedFromAccidentalDeletion", null);
            foreach (var ou in new[] { "Staff", "Workstations", "Servers", "Groups" }) NewOu(dc, DomainDn, ou, true, out err);
            foreach (var ou in new[] { "Sales", "Finance", "HR", "IT" }) NewOu(dc, "OU=Staff," + DomainDn, ou, true, out err);
            // built-in groups
            string b = "CN=Builtin," + DomainDn;
            foreach (var g in new[] { "Administrators", "Account Operators", "Server Operators", "Backup Operators", "Remote Desktop Users", "Event Log Readers" }) NewGroup(dc, b, g, g, "DomainLocal", "Security", out err).Set("isCriticalSystemObject", "TRUE");
            foreach (var g in new[] { "Domain Admins", "Domain Users", "Domain Computers", "Domain Controllers", "Group Policy Creator Owners", "Protected Users", "Read-only Domain Controllers" }) NewGroup(dc, UsersDn, g, g, "Global", "Security", out err).Set("isCriticalSystemObject", "TRUE");
            foreach (var g in new[] { "Enterprise Admins", "Schema Admins" }) NewGroup(dc, UsersDn, g, g, "Universal", "Security", out err).Set("isCriticalSystemObject", "TRUE");
            foreach (var g in new[] { "DnsAdmins", "Allowed RODC Password Replication Group", "Denied RODC Password Replication Group" }) NewGroup(dc, UsersDn, g, g, "DomainLocal", "Security", out err).Set("isCriticalSystemObject", "TRUE");
            var d = Dir(dc);
            var admin = NewUser(dc, UsersDn, "Administrator", "Administrator", "Administrator@" + DomainDns, true, SeedPassword, out err);
            admin.Set("description", "Built-in account for administering the computer/domain");
            foreach (var g in new[] { "Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators", "Group Policy Creator Owners" }) AddMember(dc, d.Find(g, "group"), admin);
            AddMember(dc, d.Find("Administrators", "group"), d.Find("Domain Admins", "group"));
            AddMember(dc, d.Find("Administrators", "group"), d.Find("Enterprise Admins", "group"));
            NewUser(dc, UsersDn, "Guest", "Guest", null, false, null, out err);
            NewUser(dc, UsersDn, "krbtgt", "krbtgt", null, false, null, out err).Set("description", "Key Distribution Center Service Account");
            // the company's groups (global) and the permission groups on the file server (domain local)
            string grp = "OU=Groups," + DomainDn;
            foreach (var g in new[] { "Sales", "Finance", "HR", "IT Admins", "Helpdesk", "Sales Managers" }) NewGroup(dc, grp, g, g, "Global", "Security", out err);
            foreach (var g in new[] { "FS-Sales-Modify", "FS-Finance-Modify", "FS-Public-Read" }) NewGroup(dc, grp, g, g, "DomainLocal", "Security", out err);
            AddMember(dc, d.Find("FS-Sales-Modify", "group"), d.Find("Sales", "group"));
            AddMember(dc, d.Find("FS-Finance-Modify", "group"), d.Find("Finance", "group"));
            AddMember(dc, d.Find("FS-Public-Read", "group"), d.Find("Domain Users", "group"));
            foreach (var row in People)
            {
                var p = row.Split('|');
                var parts = p[0].Split(' ');
                var u = NewUser(dc, "OU=" + p[2] + ",OU=Staff," + DomainDn, p[0], p[1], p[1] + "@" + DomainDns, true, SeedPassword, out err);
                u.Set("givenName", parts[0]).Set("sn", parts[1]).Set("displayName", p[0]).Set("department", p[3]).Set("title", p[4]).Set("company", Company).Set("l", "Portland");
                foreach (var g in p[5].Split(',')) AddMember(dc, d.Find(g, "group"), u);
            }
            var svc = NewUser(dc, UsersDn, "Backup Service", "svc-backup", "svc-backup@" + DomainDns, true, "Svc!Backup2026", out err);
            svc.Set("description", "Runs the nightly backup jobs");
            AddMember(dc, d.Find("Backup Operators", "group"), svc);
            // computers
            foreach (var row in MachineList)
            {
                string where = row[2] == "dc" ? "OU=Domain Controllers," + DomainDn : row[2] == "client" ? "OU=Workstations," + DomainDn : "OU=Servers," + DomainDn;
                var c = NewComputer(dc, where, row[0], out err);
                var m = Servers[row[0]].M;
                c.Set("operatingSystem", row[2] == "client" ? "Windows 11 Enterprise" : "Windows Server 2022 Datacenter").Set("operatingSystemVersion", row[2] == "client" ? "10.0 (22631)" : "10.0 (20348)");
                c.Set("ipv4", row[1]);
                if (row[2] == "dc") { c.Set("primaryGroup", "Domain Controllers"); c.Set("isDc", "TRUE"); }
            }
            // the site, its subnet, the default site link and the DCs' server objects
            var hq = Create(dc, "site", "CN=HQ," + SitesDn, "description", "Head office, Portland");
            Create(dc, "container", "CN=Servers,CN=HQ," + SitesDn);
            Create(dc, "subnet", "CN=10.10.0.0/24,CN=Subnets," + SitesDn, "siteObject", "HQ", "location", "Portland");
            var link = Create(dc, "siteLink", "CN=DEFAULTIPSITELINK,CN=IP,CN=Inter-Site Transports," + SitesDn, "cost", "100", "replInterval", "180");
            link.SetList("siteList", new[] { "HQ" });
            foreach (var n in new[] { "DC1", "DC2" }) Create(dc, "server", "CN=" + n + ",CN=Servers,CN=HQ," + SitesDn, "dNSHostName", n.ToLowerInvariant() + "." + DomainDns, "options", "1");   // options 1: global catalog
            SeedGpos(dc);
            SeedMachines();
            Dirs["DC2"] = Dirs["DC1"].Clone("DC2");
            ClearTimers();
        }
        void ClearTimers() { timers.Clear(); }
        partial void SeedGpos(string dc);
        partial void SeedDns(); partial void SeedDhcp(); partial void SeedFiles(); partial void SeedHyperV(); partial void SeedAzure();

        void SeedMachines()
        {
            foreach (var s in Servers.Values)
            {
                var m = s.M;
                if (s.Kind == "dc") InstallFeatures(m, "AD-Domain-Services", "DNS", "GPMC", "RSAT-AD-PowerShell", "RSAT-DNS-Server");
                if (s.Kind == "member") InstallFeatures(m, "DHCP", "RSAT-DHCP", "RSAT-AD-PowerShell", "GPMC");
                if (s.Kind == "hyperv") InstallFeatures(m, "Hyper-V");
                if (s.Kind == "files") InstallFeatures(m, "FS-FileServer");
            }
            SeedDns(); SeedDhcp(); SeedFiles(); SeedHyperV(); SeedAzure();
        }
        /// <summary>Marks features installed (with their parents) and adds their services (they start at boot).</summary>
        public static void InstallFeatures(PsMachine m, params string[] names)
        {
            foreach (var n in names)
            {
                var f = m.FindFeature(n);
                if (f == null) continue;
                for (var x = f; x != null; x = x.Parent == null ? null : m.FindFeature(x.Parent)) x.State = "Installed";
                foreach (var k in m.Features.Where(k => k.Parent == f.Name && k.Default)) k.State = "Installed";
                foreach (var sv in f.Services) PsCmdlets.AddFeatureService(m, sv, false);
            }
        }
    }
}
