// Cert Lab - the AZ-800 course's pretend Azure subscription (nothing here talks to Azure; no sign-in anywhere): Azure
// Arc-enabled servers (the Connected Machine agent's himds service keeps them Connected), update assessments, Azure
// virtual machines running Windows Server (static or dynamic private IP, data disks and their host caching, the
// virtual network's DNS servers), Azure Network Adapter (a point-to-site VPN from one server to the virtual network,
// whose gateway takes about 25 minutes to create the first time) and Windows Admin Center's connections.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class ArcServer { public string Name = "", ResourceGroup = "rg-hybrid", Region = "West US 2", AgentVersion = "1.47"; public DateTime Onboarded; public readonly List<string> Extensions = new List<string>(); public readonly Dictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    public class AzDataDisk { public string Name = "", Caching = "ReadOnly"; public int SizeGb = 32, Lun; }
    public class AzVm
    {
        public string Name = "", Size = "Standard_D2s_v5", Os = "Windows Server 2022 Datacenter: Azure Edition", PrivateIp = "", IpAllocation = "Dynamic", Subnet = "snet-servers", State = "Running", Domain = "";
        public readonly List<AzDataDisk> DataDisks = new List<AzDataDisk>();
    }
    public class AzUpdate { public string Kb = "", Title = "", Classification = "Security"; }

    public partial class WsAzure
    {
        public WsLab Lab;
        public string Subscription = "Harborline Production", SubscriptionId = "5c1e7a9d-3b2f-4e61-9a80-1d2c3b4a5f60", TenantDomain = "harborline.onmicrosoft.example", VerifiedDomain = "harborline.example";
        public string ResourceGroup = "rg-hybrid", Region = "West US 2", Vnet = "vnet-hybrid", VnetSpace = "10.50.0.0/16";
        public readonly List<string> VnetDns = new List<string>();          // empty: Azure-provided DNS
        public readonly HashSet<string> VnetReach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // Azure Network Adapter connections
        public bool SiteVpn;
        public DateTime? GatewayReady;                                        // the virtual network gateway (point-to-site) exists from then
        public readonly Dictionary<string, ArcServer> Arc = new Dictionary<string, ArcServer>(StringComparer.OrdinalIgnoreCase);
        public readonly List<AzVm> Vms = new List<AzVm>();
        public readonly Dictionary<string, List<AzUpdate>> Missing = new Dictionary<string, List<AzUpdate>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, DateTime> Assessed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> WacConnections = new List<string>();
        public bool WacRegistered;
        public bool Reaches(string machine) { return SiteVpn || VnetReach.Contains(machine) && GatewayReady.HasValue && GatewayReady.Value <= Lab.Now; }
        public AzVm Vm(string n) { return Vms.FirstOrDefault(v => WsText.Eq(v.Name, n)); }
        partial void OnSeed();

        public void Seed()
        {
            Vms.Add(new AzVm { Name = "AZ-APP1", PrivateIp = "10.50.1.4" });
            Vms.Add(new AzVm { Name = "AZ-DC3", PrivateIp = "10.50.1.5", Size = "Standard_D2s_v5" });
            Missing["SRV1"] = new List<AzUpdate> { new AzUpdate { Kb = "KB5055526", Title = "2026-02 Cumulative Update for Microsoft server operating system version 21H2 for x64-based Systems" }, new AzUpdate { Kb = "KB5054980", Title = "2026-02 Cumulative Update for .NET Framework 3.5, 4.8 and 4.8.1", Classification = "Security" } };
            Missing["FS1"] = new List<AzUpdate> { new AzUpdate { Kb = "KB5055526", Title = "2026-02 Cumulative Update for Microsoft server operating system version 21H2 for x64-based Systems" } };
            Missing["CORE1"] = new List<AzUpdate> { new AzUpdate { Kb = "KB5055526", Title = "2026-02 Cumulative Update for Microsoft server operating system version 21H2 for x64-based Systems" }, new AzUpdate { Kb = "KB890830", Title = "Windows Malicious Software Removal Tool x64 - v5.134", Classification = "Update rollups" } };
            WacConnections.AddRange(new[] { "SRV1", "DC1", "FS1" });
            var srv = Lab.Server("SRV1").M;
            srv.Services.Add(new WinService { Name = "ServerManagementGateway", DisplayName = "Windows Admin Center", StartType = "Automatic", Exe = "sme", BinaryPath = "\"C:\\Program Files\\WindowsAdminCenter\\sme.exe\"", Feature = true, ServiceType = "Win32OwnProcess" });
            OnSeed();
        }

        /// <summary>Is the Arc-enabled server Connected (its agent's himds service runs and the machine is on)?</summary>
        public string ArcStatus(string name)
        {
            if (!Arc.ContainsKey(name)) return "Not onboarded";
            var s = Lab.Server(name);
            return s != null && s.Running && s.SvcRunning("himds") ? "Connected" : "Disconnected";
        }
        /// <summary>Installs the Connected Machine agent on a server (if missing) and connects it to the subscription.</summary>
        public string Onboard(string name, string rg, string region)
        {
            var s = Lab.Server(name);
            if (s == null || s.IsClient) return "Azure Arc for servers onboards Windows Server machines; " + name + " isn't one here.";
            if (!s.Running) return name + " isn't running.";
            if (Arc.ContainsKey(name)) return name + " is already connected to Azure Arc.";
            InstallAgent(s);
            Arc[name] = new ArcServer { Name = s.Name, ResourceGroup = string.IsNullOrEmpty(rg) ? ResourceGroup : rg, Region = string.IsNullOrEmpty(region) ? Region : region, Onboarded = Lab.Now };
            Lab.Note("Azure Arc: " + s.Name + " is connected (resource group " + Arc[name].ResourceGroup + ").");
            return null;
        }
        public static void InstallAgent(WsServer s)
        {
            if (s.M.Svc("himds") != null) return;
            foreach (var n in new[] { "himds|Azure Hybrid Instance Metadata Service", "GCArcService|Guest Configuration Arc Service", "ExtensionService|Guest Configuration Extension Service" })
            {
                var p = n.Split('|');
                var sv = new WinService { Name = p[0], DisplayName = p[1], StartType = "Automatic", Exe = p[0].ToLowerInvariant(), BinaryPath = "\"C:\\Program Files\\AzureConnectedMachineAgent\\" + p[0].ToLowerInvariant() + ".exe\"", Feature = true, ServiceType = "Win32OwnProcess" };
                s.M.Services.Add(sv); s.M.StartService(sv, true);
            }
            s.M.MakeDir("C:\\Program Files\\AzureConnectedMachineAgent", s.Lab.Now);
        }
        public string Disconnect(string name) { if (!Arc.Remove(name)) return name + " isn't connected to Azure Arc."; return null; }

        public string AddNetworkAdapter(string server)
        {
            var s = Lab.Server(server);
            if (s == null || s.IsClient) return "Azure Network Adapter is added to a Windows Server machine from Windows Admin Center.";
            if (!WacRegistered) return "Register Windows Admin Center with Azure first (Settings > Azure).";
            if (!GatewayReady.HasValue) { GatewayReady = Lab.Now.AddMinutes(25); Lab.Note("Azure: creating the virtual network gateway for " + Vnet + " (about 25 minutes)."); }
            VnetReach.Add(s.Name);
            return null;
        }
        public string Assess(string server)
        {
            if (ArcStatus(server) != "Connected" && Vm(server) == null) return server + " must be an Azure VM or a Connected Azure Arc-enabled server to be assessed.";
            Assessed[server] = Lab.Now;
            return null;
        }
        /// <summary>Installs the missing updates of the classifications given (all when empty); the server shows them in Get-HotFix.</summary>
        public string InstallUpdates(string server, string classes)
        {
            if (!Assessed.ContainsKey(server)) return "Assess " + server + " first: Azure Update Manager installs what an assessment found.";
            List<AzUpdate> l; if (!Missing.TryGetValue(server, out l)) return null;
            var pick = l.Where(u => string.IsNullOrEmpty(classes) || classes.Split(',').Any(c => WsText.Eq(c.Trim(), u.Classification))).ToList();
            var m = Lab.M(server);
            foreach (var u in pick) { l.Remove(u); if (m != null) m.Hotfixes.Add(new WinHotfix { Id = u.Kb, Description = u.Classification == "Security" ? "Security Update" : "Update", On = Lab.Now.Date }); }
            return null;
        }
    }

    public partial class WsLab
    {
        partial void SeedAzure() { Azure = new WsAzure { Lab = this }; Azure.Seed(); }
    }
}
