// Cert Lab - the roots of the AZ-800 checks for the pretend Azure subscription and the tenant: arc, azure, cloud,
// signin, connect, filesync, wac, updates.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsCheck
    {
        static partial void RootAzure(WsLab lab, WsServer srv, string root, Dictionary<string, string> a, string ix, ref WsVal v)
        {
            var z = lab.Azure;
            switch (root)
            {
                case "arc":
                    {
                        if (ix == null) { v = WsVal.L(z.Arc.Keys); return; }
                        ArcServer s; if (!z.Arc.TryGetValue(ix, out s)) { v = WsVal.O("Not onboarded", (m, i) => m == "status" ? WsVal.T("Not onboarded") : null); return; }
                        v = WsVal.O(s.Name, (m, i) => m == "status" ? WsVal.T(z.ArcStatus(s.Name)) : m == "rg" || m == "resourcegroup" ? WsVal.T(s.ResourceGroup) : m == "region" ? WsVal.T(s.Region) : m == "extensions" ? WsVal.L(s.Extensions) : null);
                        return;
                    }
                case "updates":
                    {
                        string n = ix ?? A(a, "server", 0);
                        List<AzUpdate> l; z.Missing.TryGetValue(n ?? "", out l);
                        v = WsVal.O(n, (m, i) => m == "missing" ? WsVal.L(l == null ? new List<string>() : l.Select(u => u.Kb)) : m == "assessed" ? Yn(z.Assessed.ContainsKey(n)) : null);
                        return;
                    }
                case "azure":
                    v = WsVal.O(z.Subscription, (m, i) =>
                    {
                        switch (m)
                        {
                            case "vm":
                                {
                                    var vm = z.Vm(i); if (vm == null) return null;
                                    return WsVal.O(vm.Name, (mm, ii) =>
                                    {
                                        switch (mm)
                                        {
                                            case "ipallocation": return WsVal.T(vm.IpAllocation);
                                            case "ip": return WsVal.T(vm.PrivateIp);
                                            case "state": return WsVal.T(vm.State);
                                            case "size": return WsVal.T(vm.Size);
                                            case "disks": return WsVal.L(vm.DataDisks.Select(dd => dd.Name));
                                            case "disk": { var dd = vm.DataDisks.FirstOrDefault(x => WsText.Eq(x.Name, ii)); return dd == null ? null : WsVal.O(dd.Name, (m3, i3) => m3 == "caching" ? WsVal.T(dd.Caching) : m3 == "sizegb" ? N(dd.SizeGb) : null); }
                                        }
                                        return null;
                                    });
                                }
                            case "vnetdns": return WsVal.L(z.VnetDns);
                            case "reach": return Yn(z.Reaches(i ?? ""));
                            case "gateway": return Yn(z.GatewayReady.HasValue && z.GatewayReady.Value <= lab.Now);
                            case "vpn": return Yn(z.SiteVpn);
                        }
                        return null;
                    });
                    return;
                case "connect":
                    v = WsVal.O(z.ConnectServer, (m, i) =>
                    {
                        switch (m)
                        {
                            case "server": return WsVal.T(z.ConnectServer);
                            case "signin": return WsVal.T(z.ConnectServer.Length == 0 && !z.CloudSync ? "" : z.SignIn);
                            case "sso": return Yn(z.Sso);
                            case "staging": return Yn(z.Staging);
                            case "ous": return WsVal.L(z.OuFilter);
                            case "ptaagents": return WsVal.L(z.PtaAgents);
                            case "cloudsync": return Yn(z.CloudSync);
                            case "agents": return WsVal.L(z.CloudAgents);
                            case "scheduler": return Yn(z.SyncEnabled);
                            case "lastsync": return WsVal.T(z.LastSync.HasValue ? WsText.Date(z.LastSync.Value) : "");
                        }
                        return null;
                    });
                    return;
                case "cloud":
                    v = WsVal.O(z.TenantDomain, (m, i) =>
                    {
                        if (m == "users") return WsVal.L(z.CloudUsers.Keys);
                        if (m == "domains") return WsVal.L(z.VerifiedDomains);
                        if (m != "user") return null;
                        CloudUser cu; if (!z.CloudUsers.TryGetValue(i ?? "", out cu)) return null;
                        return WsVal.O(cu.Upn, (mm, ii) => mm == "upn" ? WsVal.T(cu.Upn) : mm == "department" ? WsVal.T(cu.Department) : mm == "enabled" ? Yn(cu.Enabled) : mm == "source" ? WsVal.T(cu.Source) : null);
                    });
                    return;
                case "signin": v = WsVal.T(z.SignInResult(A(a, "user", 0), A(a, "password", 1))); return;
                case "filesync":
                    {
                        if (ix == null) { v = WsVal.O(z.SyncService, (m, i) => m == "service" ? WsVal.T(z.SyncService) : m == "servers" ? WsVal.L(z.SyncRegistered) : m == "groups" ? WsVal.L(z.SyncGroups.Select(g => g.Name)) : null); return; }
                        var gr = z.SyncGroupNamed(ix); if (gr == null) return;
                        v = WsVal.O(gr.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "share": return WsVal.T(gr.StorageAccount + "/" + gr.FileShare);
                                case "servers": return WsVal.L(gr.Servers.Select(x => x.Server));
                                case "path": { var ep = gr.Servers.FirstOrDefault(x => WsText.Eq(x.Server, i)); return ep == null ? null : WsVal.T(ep.Path); }
                                case "tiering": { var ep = gr.Servers.FirstOrDefault(x => WsText.Eq(x.Server, i)); return ep == null ? null : Yn(ep.Tiering); }
                                case "tierdays": { var ep = gr.Servers.FirstOrDefault(x => WsText.Eq(x.Server, i)); return ep == null ? null : N(ep.TierDays); }
                                case "cloudfiles": { var l = new List<string>(); Action<WinFile, string> walk = null; walk = (f, p) => { foreach (var k in f.Kids.Values) { if (k.IsDir) walk(k, p + k.Name + "\\"); else l.Add(p + k.Name); } }; walk(gr.Cloud, ""); return WsVal.L(l); }
                            }
                            return null;
                        });
                        return;
                    }
                case "wac": v = WsVal.O("Windows Admin Center", (m, i) => m == "connections" ? WsVal.L(z.WacConnections) : m == "registered" ? Yn(z.WacRegistered) : null); return;
            }
        }
    }
}
