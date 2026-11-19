// Cert Lab - identity sync to the pretend Microsoft Entra tenant (harborline.onmicrosoft.example, verified domain
// harborline.example): Microsoft Entra Connect Sync (installed on one server; a delta sync every 30 minutes, password
// hash sync every 2 minutes, OU filtering, staging mode, sign-in by password hash sync or pass-through authentication
// with its agents, seamless single sign-on) or Microsoft Entra Cloud Sync (lightweight agents, every 2 minutes). A user
// whose UPN suffix isn't a verified domain gets the tenant's initial domain in the cloud. Sign-ins are evaluated here,
// never sent anywhere.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class CloudUser { public string Sam = "", Upn = "", Department = "", Source = "Windows Server AD", PasswordHash = ""; public bool Enabled = true; public DateTime Synced; }

    public partial class WsAzure
    {
        public string ConnectServer = "", SignIn = "PHS", CloudSyncScope = "";
        public bool Sso, Staging, CloudSync, SyncEnabled = true, PasswordWriteback;
        public readonly List<string> OuFilter = new List<string>();          // empty: every OU
        public readonly List<string> PtaAgents = new List<string>(), CloudAgents = new List<string>();
        public readonly Dictionary<string, CloudUser> CloudUsers = new Dictionary<string, CloudUser>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> VerifiedDomains = new List<string> { "harborline.example" };
        public DateTime? LastSync;

        public string InstallConnect(string server, string signIn, bool sso, List<string> ous, bool staging)
        {
            var s = Lab.Server(server);
            if (s == null || s.IsClient) return "Microsoft Entra Connect Sync is installed on a domain-joined Windows Server.";
            if (CloudSync) return "Cert Lab keeps one sync engine at a time: this tenant already uses Microsoft Entra Cloud Sync.";
            if (ConnectServer.Length > 0 && !staging) return "Microsoft Entra Connect Sync is already installed on " + ConnectServer + " (a second server can only be a staging server).";
            signIn = (signIn ?? "PHS").ToUpperInvariant();
            if (signIn != "PHS" && signIn != "PTA" && signIn != "FEDERATION") return "Sign-in method: PHS (password hash sync), PTA (pass-through authentication) or Federation.";
            if (staging) { Staging = true; Lab.Note("Microsoft Entra Connect: " + s.Name + " is a staging server (it doesn't export to the tenant)."); return null; }
            ConnectServer = s.Name; SignIn = signIn == "FEDERATION" ? "Federation" : signIn; Sso = sso;
            OuFilter.Clear(); if (ous != null) OuFilter.AddRange(ous);
            AddService(s, "ADSync", "Microsoft Azure AD Sync");
            if (SignIn == "PTA") { PtaAgents.Add(s.Name); AddService(s, "AzureADConnectAuthenticationAgent", "Microsoft Azure AD Connect Authentication Agent"); }
            if (sso) { string e; var dc = Lab.LocateDc(s.M, null, out e); if (dc != null) Lab.NewComputer(dc, Lab.ComputersDn, "AZUREADSSOACC", out e); }
            Sync(true);
            ScheduleSync();
            return null;
        }
        public static void AddService(WsServer s, string name, string display)
        {
            if (s.M.Svc(name) != null) return;
            var sv = new WinService { Name = name, DisplayName = display, StartType = "Automatic", Exe = name.ToLowerInvariant(), BinaryPath = "\"C:\\Program Files\\Microsoft Azure AD Sync\\Bin\\" + name.ToLowerInvariant() + ".exe\"", Feature = true, ServiceType = "Win32OwnProcess" };
            s.M.Services.Add(sv); s.M.StartService(sv, true);
        }
        public string AddPtaAgent(string server)
        {
            var s = Lab.Server(server);
            if (SignIn != "PTA") return "Pass-through authentication isn't the tenant's sign-in method.";
            if (s == null || s.IsClient) return "A pass-through authentication agent runs on a Windows Server.";
            if (!PtaAgents.Contains(s.Name)) PtaAgents.Add(s.Name);
            AddService(s, "AzureADConnectAuthenticationAgent", "Microsoft Azure AD Connect Authentication Agent");
            return null;
        }
        public string InstallCloudSync(string server, string scope)
        {
            var s = Lab.Server(server);
            if (s == null || s.IsClient) return "The provisioning agent is installed on a domain-joined Windows Server.";
            if (ConnectServer.Length > 0) return "Cert Lab keeps one sync engine at a time: this tenant already uses Microsoft Entra Connect Sync on " + ConnectServer + ".";
            CloudSync = true; CloudSyncScope = scope ?? "";
            if (!CloudAgents.Contains(s.Name)) CloudAgents.Add(s.Name);
            AddService(s, "AADConnectProvisioningAgent", "Microsoft Azure AD Connect Provisioning Agent");
            Sync(true); ScheduleSync();
            return null;
        }

        bool EngineUp
        {
            get
            {
                if (CloudSync) return CloudAgents.Any(a => { var s = Lab.Server(a); return s != null && s.Running && s.SvcRunning("AADConnectProvisioningAgent"); });
                var cs = Lab.Server(ConnectServer);
                return cs != null && cs.Running && cs.SvcRunning("ADSync") && SyncEnabled;
            }
        }
        bool InScope(WsAdObj u)
        {
            if (CloudSync) return CloudSyncScope.Length == 0 || Dn.Under(u.Dn, CloudSyncScope);
            return OuFilter.Count == 0 || OuFilter.Any(ou => Dn.Under(u.Dn, ou));
        }
        /// <summary>A sync cycle (delta or initial; Cert Lab does the same for both): users in scope are created or updated in
        /// the tenant, users that left the scope are deleted there. Built-in and service accounts don't sync.</summary>
        public int Sync(bool force)
        {
            if (!EngineUp && !force) return 0;
            if (!CloudSync && ConnectServer.Length == 0) return 0;
            var d = Lab.Main; int n = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in d.OfClass("user").Where(x => !x.Yes("isCriticalSystemObject") && x.Sam != "krbtgt" && x.Sam != "Guest" && x.Sam != "Administrator" && !x.Sam.StartsWith("svc-", StringComparison.Ordinal)))
            {
                if (!InScope(u)) continue;
                seen.Add(u.Sam);
                string upn = u.Get("userPrincipalName"); if (upn.Length == 0) upn = u.Sam + "@" + Lab.DomainDns;
                string sfx = upn.Substring(upn.IndexOf('@') + 1);
                string cloudUpn = VerifiedDomains.Contains(sfx, StringComparer.OrdinalIgnoreCase) ? upn : upn.Substring(0, upn.IndexOf('@')) + "@" + TenantDomain;
                CloudUser cu; if (!CloudUsers.TryGetValue(u.Sam, out cu)) { cu = new CloudUser { Sam = u.Sam }; CloudUsers[u.Sam] = cu; n++; }
                cu.Upn = cloudUpn; cu.Department = u.Get("department"); cu.Enabled = u.Get("enabled") != "FALSE"; cu.Synced = Lab.Now;
                if (SignIn == "PHS" || CloudSync) cu.PasswordHash = Hash(u.Get("password"));
            }
            foreach (var k in CloudUsers.Keys.Where(k => !seen.Contains(k)).ToList()) { CloudUsers.Remove(k); n++; }
            LastSync = Lab.Now;
            return n;
        }
        static string Hash(string pw) { return string.IsNullOrEmpty(pw) ? "" : "h" + Math.Abs(FwHash.Of(pw)).ToString(CultureInfo.InvariantCulture); }
        public void ScheduleSync()
        {
            Lab.At("aadsync:delta", Lab.Now.AddMinutes(CloudSync ? 2 : 30), () => { Sync(false); ScheduleSync(); }, false);
            Lab.At("aadsync:phs", Lab.Now.AddMinutes(2), () => { if (EngineUp) PasswordSync(); }, false);
        }
        void PasswordSync()
        {
            foreach (var cu in CloudUsers.Values) { var u = Lab.Main.Find(cu.Sam, "user"); if (u != null && (SignIn == "PHS" || CloudSync)) cu.PasswordHash = Hash(u.Get("password")); }
            Lab.At("aadsync:phs", Lab.Now.AddMinutes(2), () => { if (EngineUp) PasswordSync(); }, false);
        }

        /// <summary>A cloud sign-in: Allowed, or why not (no such cloud user, disabled, wrong password, no PTA agent reachable).</summary>
        public string SignInResult(string user, string password)
        {
            var cu = CloudUsers.Values.FirstOrDefault(x => WsText.Eq(x.Sam, user) || WsText.Eq(x.Upn, user));
            if (cu == null) return "No such user in Microsoft Entra ID";
            if (!cu.Enabled) return "Account disabled";
            var onprem = Lab.Main.Find(cu.Sam, "user");
            string pw = password ?? (onprem == null ? "" : onprem.Get("password"));
            if (SignIn == "PTA" && !CloudSync)
            {
                if (!PtaAgents.Any(a => { var s = Lab.Server(a); return s != null && s.Running && s.SvcRunning("AzureADConnectAuthenticationAgent"); })) return "Failed: no pass-through authentication agent is available";
                return onprem != null && onprem.Get("password") == pw && onprem.Get("enabled") != "FALSE" ? "Allowed" : "Wrong password";
            }
            if (SignIn == "Federation" && !CloudSync) return "Failed: federation (AD FS) isn't simulated";
            return cu.PasswordHash.Length > 0 && cu.PasswordHash == Hash(pw) ? "Allowed" : "Wrong password";
        }
    }
}
