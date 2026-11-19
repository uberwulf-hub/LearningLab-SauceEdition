// Cert Lab - console actions of Windows Admin Center (practice) on a server: DHCP, roles and features, services, files
// and shares, NTFS permissions, storage, DFS, Hyper-V virtual machines and switches, and the gateway's connections.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsOps
    {
        static void RunDhcp(WsLab lab, WsServer srv, string noun, Args a, ref WsResult r)
        {
            string act = a.P(0) ?? "";
            if (noun == "authorize") { r = R(lab.Authorize(srv.Name, !WsText.Eq(act, "remove")), srv.Name + (WsText.Eq(act, "remove") ? " is no longer authorized." : " is authorized in Active Directory.")); return; }
            var d = srv.Dhcp;
            if (d == null) { r = WsResult.Fail("The DHCP Server role isn't installed on " + srv.Name + "."); return; }
            switch (noun)
            {
                case "scope":
                    if (act == "add")
                    {
                        var sc = new DhcpScope { Name = a["name"] ?? "", Start = a["start"] ?? "", End = a["end"] ?? "", Mask = a["mask"] ?? "255.255.255.0", State = a.Yes("active", true) ? "Active" : "InActive", Lease = TimeSpan.FromDays(a.Int("days", 8)) };
                        r = R(lab.AddScope(srv.Name, sc), "Created the scope " + sc.ScopeId + " (" + sc.Name + ") on " + srv.Name + "."); return;
                    }
                    {
                        var sc = d.Scope(a["id"] ?? a.P(1));
                        if (sc == null) { r = WsResult.Fail("There is no scope " + (a["id"] ?? a.P(1)) + " on " + srv.Name + "."); return; }
                        if (act == "set") { if (a.Has("active")) sc.State = a.Yes("active", true) ? "Active" : "InActive"; if (a.Has("days")) sc.Lease = TimeSpan.FromDays(a.Int("days", 8)); if (a.Has("name")) sc.Name = a["name"]; r = WsResult.Done("Saved the scope " + sc.ScopeId + "."); return; }
                        if (act == "remove") { d.Scopes.Remove(sc); r = WsResult.Done("Deleted the scope " + sc.ScopeId + "."); return; }
                        if (act == "exclude") { if (!sc.InRange(a["start"]) || !sc.InRange(a["end"] ?? a["start"])) { r = WsResult.Fail("An exclusion must be inside the scope's range."); return; } sc.Exclusions.Add(new[] { a["start"], a["end"] ?? a["start"] }); r = WsResult.Done("Excluded " + a["start"] + "-" + (a["end"] ?? a["start"]) + "."); return; }
                        if (act == "reserve")
                        {
                            string ip = a["ip"], mac = (a["mac"] ?? "").ToUpperInvariant().Replace(":", "-");
                            uint x; if (!Ip4.TryParse(ip, out x) || !Ip4.SameNet(ip, sc.ScopeId, sc.Prefix)) { r = WsResult.Fail("A reservation's address must be in the scope's subnet."); return; }
                            if (mac.Replace("-", "").Length != 12) { r = WsResult.Fail("A reservation needs the client's MAC address (12 hex digits)."); return; }
                            if (sc.Reservations.Any(z => z.Ip == ip || z.Mac == mac)) { r = WsResult.Fail("That address or MAC address already has a reservation."); return; }
                            sc.Reservations.Add(new DhcpRes { Ip = ip, Mac = mac.Length == 12 ? string.Join("-", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)).ToArray()) : mac, Name = a["name"] ?? "" });
                            r = WsResult.Done("Reserved " + ip + " for " + mac + "."); return;
                        }
                        if (act == "option") { int id = a.Int("option", 0); if (id <= 0) { r = WsResult.Fail("option= the option number (3 router, 6 DNS servers, 15 DNS domain name)."); return; } sc.Options[id] = a["value"] ?? ""; r = WsResult.Done("Scope option " + id.ToString("000", CultureInfo.InvariantCulture) + " = " + a["value"] + "."); return; }
                    }
                    r = WsResult.Fail("dhcp scope add|set|remove|exclude|reserve|option"); return;
                case "option": { int id = a.Int("option", 0); if (id <= 0) { r = WsResult.Fail("option= the option number."); return; } d.ServerOptions[id] = a["value"] ?? ""; r = WsResult.Done("Server option " + id + " = " + a["value"] + "."); return; }
                case "failover":
                    if (act == "add")
                    {
                        var ids = (a["scopes"] ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        r = R(lab.AddFailover(srv.Name, a["partner"], a["name"] ?? (srv.Name + "-" + a["partner"]), ids, a["mode"] ?? "LoadBalance", a.Int("percent", 50), a.Int("reserve", 5), TimeSpan.FromMinutes(a.Int("mclt", 60)), a["secret"]), "Created the failover relationship with " + a["partner"] + "."); return;
                    }
                    if (act == "replicate") { r = R(lab.ReplicateFailover(srv.Name, a["name"], a["scope"]), "Replicated the failover scopes to the partner."); return; }
                    if (act == "remove") { r = R(lab.RemoveFailover(srv.Name, a["name"]), "Deleted the failover relationship."); return; }
                    r = WsResult.Fail("dhcp failover add|replicate|remove"); return;
            }
        }

        static partial void RunServer(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            var a = Parse(tok, 2);
            switch (v)
            {
                case "wac":
                    if (noun == "register") { lab.Azure.WacRegistered = true; r = WsResult.Done("Windows Admin Center is registered with Azure (subscription " + lab.Azure.Subscription + ")."); return; }
                    {
                        var s = lab.Server(a.P(0));
                        if (s == null) { r = WsResult.Fail("There is no machine " + a.P(0) + "."); return; }
                        if (noun == "add") { if (!s.M.WinRmListening || !s.Running) { r = WsResult.Fail("Windows Admin Center can't connect to " + s.Name + ": WinRM isn't reachable (Enable-PSRemoting there)."); return; } if (!lab.Azure.WacConnections.Contains(s.Name)) lab.Azure.WacConnections.Add(s.Name); r = WsResult.Done("Added the connection " + s.Name + "."); return; }
                        if (noun == "remove") { lab.Azure.WacConnections.Remove(s.Name); r = WsResult.Done("Removed the connection " + s.Name + "."); return; }
                    }
                    return;
                case "feature":
                    {
                        var s = lab.Server(a.P(0));
                        if (s == null || s.IsClient) { r = WsResult.Fail("Roles and features are installed on a server."); return; }
                        var f = s.M.FindFeature(a.P(1) ?? "");
                        if (f == null) { r = WsResult.Fail("The role or feature " + a.P(1) + " isn't known."); return; }
                        if (noun == "install") { WsLab.InstallFeatures(s.M, f.Name); foreach (var sv in f.Services) { var x = s.M.Svc(sv); if (x != null && !f.Restart) s.M.StartService(x, true); } if (f.Restart) { f.State = "InstallPending"; r = WsResult.Done("Installed " + f.Display + " on " + s.Name + ": restart the server to finish."); return; } r = WsResult.Done("Installed " + f.Display + " on " + s.Name + "."); return; }
                        if (noun == "remove") { f.State = "Available"; r = WsResult.Done("Removed " + f.Display + " from " + s.Name + "."); return; }
                        return;
                    }
                case "service":
                    {
                        var s = lab.Server(a.P(0)); var sv = s == null ? null : s.M.Svc(a.P(1) ?? "");
                        if (sv == null) { r = WsResult.Fail("There is no service " + a.P(1) + " on " + a.P(0) + "."); return; }
                        if (noun == "start") s.M.StartService(sv, true); else if (noun == "stop") s.M.StopService(sv, true, true); else { r = WsResult.Fail("service start|stop SERVER NAME"); return; }
                        r = WsResult.Done(sv.DisplayName + " on " + s.Name + " is " + sv.Status.ToLowerInvariant() + "."); return;
                    }
                case "share": RunShare(lab, noun, a, ref r); return;
                case "ntfs": RunNtfs(lab, noun, a, ref r); return;
                case "vm": case "vswitch": RunHv(lab, v, noun, a, ref r); return;
                case "dfsn": case "dfsr": RunDfs(lab, v, noun, a, ref r); return;
                case "storage": RunStorage(lab, noun, a, ref r); return;
            }
        }
    }
}
