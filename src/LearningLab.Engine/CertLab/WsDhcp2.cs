// Cert Lab - managing the pretend DHCP servers: authorizing a server in Active Directory, scopes, failover (the scope's
// settings are copied to the partner once; later changes to reservations or options reach the partner only when the
// failover is replicated - leases are shared automatically) and the starting state: SRV1 serves the HQ subnet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public string Authorize(string server, bool add)
        {
            var s = Server(server);
            if (s == null) return "There is no server " + server + ".";
            string err; string dc = LocateDc(s.M, null, out err);
            if (dc == null) return err;
            var d = Dir(dc);
            var o = d.Live.FirstOrDefault(x => x.Is("dHCPClass") && (WsText.Eq(x.Name, s.M.Fqdn) || x.Get("dhcpIp") == s.M.Ip));
            if (add && o == null) Create(dc, "dHCPClass", "CN=" + s.M.Fqdn + "," + NetServicesDn, "dhcpIp", s.M.Ip);
            if (!add && o != null) { o.Deleted = true; Touch(dc, o); }
            return null;
        }

        public string AddScope(string server, DhcpScope sc)
        {
            var d = DhcpAt(server);
            if (d == null) return "The DHCP Server role isn't installed on " + server + ".";
            uint s, e, m;
            if (!Ip4.TryParse(sc.Start, out s) || !Ip4.TryParse(sc.End, out e) || !Ip4.TryParse(sc.Mask, out m)) return "The start, end and subnet mask must be IPv4 addresses.";
            if (s > e) return "The start of the range must come before its end.";
            if ((s & m) != (e & m)) return "The start and end of the range must be in the same subnet.";
            sc.ScopeId = Ip4.Str(s & m);
            if (d.Scope(sc.ScopeId) != null) return "The scope " + sc.ScopeId + " already exists on " + server + ".";
            if (string.IsNullOrEmpty(sc.Name)) return "A scope needs a name.";
            d.Scopes.Add(sc);
            return null;
        }

        public string AddFailover(string server, string partner, string name, List<string> scopeIds, string mode, int lbPercent, int reservePercent, TimeSpan mclt, string secret)
        {
            var a = DhcpAt(server); var ps = Server(partner); var b = ps == null ? null : ps.Dhcp;
            if (a == null) return "The DHCP Server role isn't installed on " + server + ".";
            if (b == null || !ps.Running || !ps.SvcRunning("DHCPServer")) return "Failed to contact the partner server " + partner + ": the DHCP Server service must be installed and running there.";
            if (!b.Authorized) return "The partner server " + partner + " isn't authorized in Active Directory.";
            if (a.Failovers.Any(f => WsText.Eq(f.Name, name))) return "A failover relationship named " + name + " already exists.";
            var fo = new DhcpFailover { Name = name, PartnerA = Server(server).Name, PartnerB = ps.Name, Mode = WsText.Eq(mode, "HotStandby") ? "HotStandby" : "LoadBalance", LoadBalancePercent = lbPercent, ReservePercent = reservePercent, Mclt = mclt, SharedSecret = secret ?? "" };
            foreach (var id in scopeIds)
            {
                var sc = a.Scope(id);
                if (sc == null) return "The scope " + id + " doesn't exist on " + server + ".";
                if (sc.Failover.Length > 0) return "The scope " + id + " is already part of the failover relationship " + sc.Failover + ".";
                if (b.Scope(sc.ScopeId) != null) return "The scope " + sc.ScopeId + " already exists on the partner " + partner + ": remove it there first.";
            }
            foreach (var id in scopeIds)
            {
                var sc = a.Scope(id);
                sc.Failover = name; fo.ScopeIds.Add(sc.ScopeId);
                b.Scopes.Add(sc.CopyConfig());
            }
            a.Failovers.Add(fo);
            var fb = new DhcpFailover { Name = fo.Name, PartnerA = fo.PartnerA, PartnerB = fo.PartnerB, Mode = fo.Mode, LoadBalancePercent = fo.LoadBalancePercent, ReservePercent = fo.ReservePercent, Mclt = fo.Mclt, SharedSecret = fo.SharedSecret };
            fb.ScopeIds.AddRange(fo.ScopeIds);
            b.Failovers.Add(fb);
            return null;
        }
        /// <summary>Invoke-DhcpServerv4FailoverReplication: the partner's copies of the failover scopes get this server's settings.</summary>
        public string ReplicateFailover(string server, string name, string scopeId)
        {
            var a = DhcpAt(server);
            if (a == null) return "The DHCP Server role isn't installed on " + server + ".";
            int n = 0;
            foreach (var fo in a.Failovers.Where(f => name == null || WsText.Eq(f.Name, name)))
            {
                var b = DhcpAt(fo.Partner(a.S.Name));
                if (b == null || !b.S.Running) return "The partner " + fo.Partner(a.S.Name) + " can't be reached.";
                foreach (var id in fo.ScopeIds.Where(x => scopeId == null || x == scopeId))
                {
                    var src = a.Scope(id); if (src == null) continue;
                    var keep = b.Scope(id);
                    var copy = src.CopyConfig();
                    if (keep != null) { copy.Leases.Clear(); copy.Leases.AddRange(keep.Leases); b.Scopes.Remove(keep); }
                    b.Scopes.Add(copy); n++;
                }
            }
            return n == 0 && name != null ? "There is no failover relationship " + name + " on " + server + "." : null;
        }
        public string RemoveFailover(string server, string name)
        {
            var a = DhcpAt(server);
            var fo = a == null ? null : a.Failovers.FirstOrDefault(f => WsText.Eq(f.Name, name));
            if (fo == null) return "There is no failover relationship " + name + " on " + server + ".";
            var b = DhcpAt(fo.Partner(a.S.Name));
            foreach (var x in new[] { a, b }) if (x != null)
                {
                    x.Failovers.RemoveAll(f => WsText.Eq(f.Name, name));
                    foreach (var sc in x.Scopes.Where(s => s.Failover == fo.Name).ToList()) { if (x == a) sc.Failover = ""; else x.Scopes.Remove(sc); }
                }
            return null;
        }
        public DhcpClient DhcpClient(string name) { return DhcpClients.FirstOrDefault(c => WsText.Eq(c.Name, name) || WsText.Eq(c.Mac, name)); }
        /// <summary>The address of the DHCP server that gave a client its lease (blank without one).</summary>
        public string DhcpServerIp(DhcpClient c) { var s = string.IsNullOrEmpty(c.Server) ? null : Server(c.Server); return s == null ? "" : s.M.Ip; }
        /// <summary>ipconfig /renew on a machine (null when it isn't a DHCP client).</summary>
        public string Renew(string machine) { var c = DhcpClient(machine); return c == null ? "This machine has a static address." : Lease(c); }

        partial void SeedDhcp()
        {
            var srv = Server("SRV1");
            var sc = new DhcpScope { Name = "HQ clients", Start = "10.10.0.100", End = "10.10.0.199", Mask = "255.255.255.0", Description = "Head office PCs and devices" };
            AddScope("SRV1", sc);
            sc.Options[3] = "10.10.0.1"; sc.Options[6] = "10.10.0.10,10.20.0.11"; sc.Options[15] = DomainDns;
            srv.Dhcp.ServerOptions[6] = "10.10.0.10";
            Create("DC1", "container", NetServicesDn);
            Create("DC1", "dHCPClass", "CN=" + srv.M.Fqdn + "," + NetServicesDn, "dhcpIp", srv.M.Ip);
            var cl = Server("CL1").M;
            cl.Eth.Dhcp = true;
            DhcpClients.Add(new DhcpClient { Name = "CL1", Mac = cl.Eth.Mac, Pc = true });
            DhcpClients.Add(new DhcpClient { Name = "PRN-HQ-01", Mac = "00-1B-A9-4C-21-07" });
            DhcpClients.Add(new DhcpClient { Name = "TAB-SALES-01", Mac = "8C-85-90-1E-6A-33" });
            AfterSeed.Add(() => { foreach (var c in DhcpClients) Lease(c); });
        }
    }
}
