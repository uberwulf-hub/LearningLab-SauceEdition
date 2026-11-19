// Cert Lab - the Azure portal (practice) actions and the words a console line is shown with.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsOps
    {
        static partial void RunAzure(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            var z = lab.Azure;
            var a = Parse(tok, 2);
            switch (v + " " + noun)
            {
                case "arc onboard": r = R(z.Onboard(a.P(0), a["rg"], a["region"]), a.P(0) + " is connected to Azure Arc."); return;
                case "arc disconnect": r = R(z.Disconnect(a.P(0)), a.P(0) + " is no longer an Azure Arc-enabled server."); return;
                case "arc extension":
                    {
                        if (!z.Arc.ContainsKey(a.P(0) ?? "")) { r = WsResult.Fail(a.P(0) + " isn't an Azure Arc-enabled server."); return; }
                        if (z.ArcStatus(a.P(0)) != "Connected") { r = WsResult.Fail(a.P(0) + " is disconnected: extensions are installed through a connected agent."); return; }
                        var ext = z.Arc[a.P(0)].Extensions; if (!ext.Contains(a.P(1), StringComparer.OrdinalIgnoreCase)) ext.Add(a.P(1));
                        r = WsResult.Done("Installed the extension " + a.P(1) + " on " + a.P(0) + "."); return;
                    }
                case "update assess": r = R(z.Assess(a.P(0)), "Assessed " + a.P(0) + ": " + (z.Missing.ContainsKey(a.P(0) ?? "") ? z.Missing[a.P(0)].Count : 0) + " update(s) missing."); return;
                case "update install": r = R(z.InstallUpdates(a.P(0), a["classes"]), "Installed the updates on " + a.P(0) + "."); return;
                case "azure netadapter": r = R(z.AddNetworkAdapter(a.P(0)), "Added an Azure Network Adapter on " + a.P(0) + " (it connects once the gateway exists)."); return;
                case "azure vpn": z.SiteVpn = WsText.True(a.P(0) ?? "on"); r = WsResult.Done("The site-to-site VPN to " + z.Vnet + " is " + (z.SiteVpn ? "connected" : "down") + "."); return;
                case "azure vnetdns":
                    z.VnetDns.Clear(); z.VnetDns.AddRange((a.P(0) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && !WsText.Eq(x, "default")));
                    r = WsResult.Done(z.Vnet + " uses " + (z.VnetDns.Count == 0 ? "Azure-provided DNS" : "the DNS servers " + WsText.Join(z.VnetDns)) + "."); return;
                case "azure vm":
                    {
                        var vm = z.Vm(a.P(0));
                        if (vm == null) { r = WsResult.Fail("There is no Azure VM " + a.P(0) + "."); return; }
                        string act = a.P(1) ?? "set";
                        if (act == "disk")
                        {
                            string c = new[] { "None", "ReadOnly", "ReadWrite" }.FirstOrDefault(x => WsText.Eq(x, a["caching"] ?? "ReadOnly"));
                            if (c == null) { r = WsResult.Fail("Host caching: None, ReadOnly or ReadWrite."); return; }
                            vm.DataDisks.Add(new AzDataDisk { Name = a["name"] ?? (vm.Name + "-data" + vm.DataDisks.Count), SizeGb = a.Int("size", 32), Caching = c, Lun = vm.DataDisks.Count });
                            r = WsResult.Done("Attached a data disk to " + vm.Name + " (host caching " + c + ")."); return;
                        }
                        if (a.Has("ipallocation")) vm.IpAllocation = WsText.Eq(a["ipallocation"], "Static") ? "Static" : "Dynamic";
                        if (a.Has("state")) vm.State = WsText.Eq(a["state"], "Stopped") ? "Stopped (deallocated)" : "Running";
                        if (a.Has("size")) vm.Size = a["size"];
                        r = WsResult.Done("Saved " + vm.Name + "."); return;
                    }
                case "connect install": r = R(z.InstallConnect(a["server"] ?? "SRV1", a["signin"] ?? "PHS", a.Yes("sso", false), (a["ous"] ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), a.Yes("staging", false)), "Microsoft Entra Connect Sync is installed and ran its first sync."); return;
                case "connect sync": { int n = z.Sync(false); r = z.LastSync.HasValue ? WsResult.Done("Sync cycle done: " + n + " change(s) exported to Microsoft Entra ID.") : WsResult.Fail("No sync engine is installed."); return; }
                case "connect filter": z.OuFilter.Clear(); z.OuFilter.AddRange((a.P(0) ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0)); r = WsResult.Done("OU filtering: " + (z.OuFilter.Count == 0 ? "every OU" : WsText.Join(z.OuFilter)) + " (applies at the next sync)."); return;
                case "connect ptaagent": r = R(z.AddPtaAgent(a.P(0)), "Installed a pass-through authentication agent on " + a.P(0) + "."); return;
                case "connect scheduler": z.SyncEnabled = WsText.True(a.P(0) ?? "on"); r = WsResult.Done("The sync scheduler is " + (z.SyncEnabled ? "on" : "off") + "."); return;
                case "cloudsync install": r = R(z.InstallCloudSync(a["server"] ?? a.P(0), a["scope"]), "The provisioning agent is installed and Cloud Sync is configured."); return;
                case "filesync service": r = R(z.CreateSyncService(a["name"] ?? a.P(0)), "Created the Storage Sync Service."); return;
                case "filesync register": r = R(z.RegisterServer(a.P(0)), a.P(0) + " is registered with the Storage Sync Service."); return;
                case "filesync group": r = R(z.CreateSyncGroup(a["name"] ?? a.P(0), a["account"], a["share"]), "Created the sync group with its cloud endpoint."); return;
                case "filesync endpoint": r = R(z.AddServerEndpoint(a["group"], a["server"], a["path"], a.Yes("tiering", false), a.Int("free", 20), a.Int("days", 0)), "Added the server endpoint."); return;
                case "filesync now": { var g = z.SyncGroupNamed(a.P(0)); if (g == null) { r = WsResult.Fail("There is no sync group " + a.P(0) + "."); return; } z.SyncNow(g); r = WsResult.Done("Synced " + g.Name + "."); return; }
            }
        }

        static readonly Dictionary<string, string> Consoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "new", "Active Directory Users and Computers" }, { "set", "Active Directory Users and Computers" }, { "move", "Active Directory Users and Computers" }, { "rename", "Active Directory Users and Computers" },
            { "delete", "Active Directory Users and Computers" }, { "restore", "Active Directory Administrative Center" }, { "members", "Active Directory Users and Computers" }, { "password", "Active Directory Users and Computers" },
            { "unlock", "Active Directory Users and Computers" }, { "replicate", "Active Directory Sites and Services" }, { "fsmo", "Active Directory (operations masters)" }, { "enable", "Active Directory Administrative Center" },
            { "upnsuffix", "Active Directory Domains and Trusts" }, { "trust", "Active Directory Domains and Trusts" }, { "gpo", "Group Policy Management" }, { "wmi", "Group Policy Management (WMI Filters)" },
            { "inheritance", "Group Policy Management" }, { "gpupdate", "Group Policy Management (Group Policy Update)" }, { "centralstore", "File Explorer on the DC" }, { "dns", "DNS Manager" },
            { "dhcp", "Windows Admin Center: DHCP" }, { "wac", "Windows Admin Center" }, { "feature", "Windows Admin Center: Roles & features" }, { "service", "Windows Admin Center: Services" },
            { "share", "Windows Admin Center: Files & file sharing" }, { "ntfs", "Windows Admin Center: Files (Permissions)" }, { "vm", "Windows Admin Center: Virtual machines" }, { "vswitch", "Windows Admin Center: Virtual switches" },
            { "dfsn", "DFS Management" }, { "dfsr", "DFS Management" }, { "storage", "Windows Admin Center: Storage" }, { "arc", "Azure portal: Azure Arc" }, { "update", "Azure portal: Azure Update Manager" },
            { "azure", "Azure portal" }, { "connect", "Microsoft Entra Connect" }, { "cloudsync", "Microsoft Entra admin center: Cloud Sync" }, { "filesync", "Azure portal: Storage Sync Service" },
        };
        /// <summary>"(Group Policy Management) gpo link ..." - the console a line belongs to, then the line.</summary>
        public static string Describe(string line)
        {
            var tok = EpOps.Tokens(line.Trim());
            if (tok.Count > 1 && tok[0] == "new" && (tok[1] == "site" || tok[1] == "subnet" || tok[1] == "sitelink")) return "(Active Directory Sites and Services) " + line;
            if (tok.Count > 1 && tok[0] == "move" && tok[1] == "dc") return "(Active Directory Sites and Services) " + line;
            string c; return tok.Count > 0 && Consoles.TryGetValue(tok[0], out c) ? "(" + c + ") " + line : line;
        }
    }
}
