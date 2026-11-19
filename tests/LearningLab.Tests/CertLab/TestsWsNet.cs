// Cert Lab - checks of the AZ-800 network's DNS and DHCP.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static partial void WsGroupsMore(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ws-dns", WsDnsTests));
            l.Add(new KeyValuePair<string, Action>("ws-dhcp", WsDhcpTests));
            l.Add(new KeyValuePair<string, Action>("ws-files", WsFileTests));
            l.Add(new KeyValuePair<string, Action>("ws-storage", WsStorageTests));
            l.Add(new KeyValuePair<string, Action>("ws-hyperv", WsHyperVTests));
            l.Add(new KeyValuePair<string, Action>("ws-hybrid", WsHybridTests));
            WsGroupsMore2(l);
        }
        static partial void WsGroupsMore2(List<KeyValuePair<string, Action>> l);

        static void WsDnsTests()
        {
            var w = new W();
            w.Yes("resolve(client=CL1, name=fs1.corp.example) == 10.10.0.20");
            w.Yes("resolve(client=CL1, name=fs1) == 10.10.0.20");
            w.Yes("resolve(client=CL1, name=intranet.corp.example) has 10.10.0.50");
            w.Yes("resolve(client=CL1, name=www.harborline.example) == 198.51.100.20");
            w.Yes("resolve(client=CL1, name=files.tidewater.example) == NXDOMAIN");
            w.Bad("trust create tidewater.example type=Forest", "conditional forwarder");
            w.Do("dns zone add server=DC1 name=tidewater.example type=Forwarder masters=192.168.50.10 ad=Domain");
            w.Yes("resolve(client=CL1, name=files.tidewater.example) == 192.168.50.20");
            w.No("resolve(client=CL1, name=files.tidewater.example, server=DC2) == 192.168.50.20");
            w.Wait(20);
            w.Yes("resolve(name=files.tidewater.example, server=DC2) == 192.168.50.20");
            w.Do("trust create tidewater.example type=Forest direction=Bidirectional auth=Selective");
            w.Yes("trust[\"tidewater.example\"].selective == Yes");
            // records, an AD zone replicating, CNAME rules, PTR
            w.Do("dns record add server=DC1 zone=corp.example name=web type=A data=10.10.0.80 ptr=yes");
            w.Yes("resolve(client=CL1, name=web.corp.example) == 10.10.0.80");
            w.Yes("dns(DC1).zone[\"0.10.10.in-addr.arpa\"].record[\"80/PTR\"] == web.corp.example");
            w.Bad("dns record add server=DC1 zone=corp.example name=web type=CNAME data=srv1.corp.example", "CNAME");
            w.Yes("resolve(client=CL1, name=10.10.0.20, type=PTR) == fs1.corp.example");
            // a file-backed primary zone on DC1, a secondary on DC2 (transfer refused, then allowed)
            w.Do("dns zone add server=DC1 name=lab.harborline.example type=Primary");
            w.Do("dns record add server=DC1 zone=lab.harborline.example name=test type=A data=10.10.0.99");
            w.Do("dns zone add server=DC2 name=lab.harborline.example type=Secondary masters=10.10.0.10");
            w.Yes("dns(DC2).zone[\"lab.harborline.example\"].loaded == No");
            w.Do("dns zone set server=DC1 name=lab.harborline.example transfers=TransferToSecureServers secondaries=10.20.0.11");
            w.Do("dns zone transfer server=DC2 lab.harborline.example");
            w.Yes("dns(DC2).zone[\"lab.harborline.example\"].record[\"test/A\"] == 10.10.0.99");
            w.Bad("dns zone add server=DC1 name=bad.example type=Secondary masters=10.10.0.10 ad=Domain", "Active Directory");
            w.Bad("dns zone add server=SRV1 name=x.example", "isn't installed");
            // recursion and forwarders
            w.Do("dns recursion server=DC1 off");
            w.Yes("resolve(name=www.harborline.example, server=DC1) == FAILED");
            w.Yes("resolve(client=CL1, name=www.harborline.example) == 198.51.100.20");
            w.Do("dns recursion server=DC1 on");
            w.Yes("resolve(name=www.harborline.example, server=DC1) == 198.51.100.20");
            // DNS on DC1 stopped: the client asks DC2
            w.Ps("DC1", "Stop-Service DNS -Force");
            w.Yes("resolve(client=CL1, name=fs1.corp.example) == 10.10.0.20");
            w.Ps("DC1", "Start-Service DNS");
            w.Dispose();
        }

        static void WsDhcpTests()
        {
            var w = new W();
            w.Yes("lease(CL1).ip == 10.10.0.100");
            w.Yes("lease(CL1).server == SRV1");
            w.Yes("lease(CL1).dns has 10.10.0.10");
            w.Yes("lease(\"PRN-HQ-01\").ip == 10.10.0.101");
            w.Yes("resolve(client=CL1, name=cl1.corp.example) == 10.10.0.100");
            w.Do("dhcp scope reserve server=SRV1 id=10.10.0.0 ip=10.10.0.150 mac=00-1B-A9-4C-21-07 name=PRN-HQ-01");
            w.Bad("dhcp scope reserve server=SRV1 id=10.10.0.0 ip=10.20.0.5 mac=00-1B-A9-4C-21-08", "subnet");
            w.Do("dhcp scope option server=SRV1 id=10.10.0.0 option=15 value=corp.example");
            Check("renew gives the reservation", w.L.Renew("PRN-HQ-01") == null && w.L.DhcpClient("PRN-HQ-01").Ip == "10.10.0.150", w.L.DhcpClient("PRN-HQ-01").Ip);
            w.Do("dhcp scope exclude server=SRV1 id=10.10.0.0 start=10.10.0.100 end=10.10.0.120");
            var nc = new DhcpClient { Name = "NEW-PC", Mac = "00-15-5D-99-00-01" };
            w.L.DhcpClients.Add(nc);
            Check("an excluded address isn't leased to a new client", w.L.Lease(nc) == null && nc.Ip == "10.10.0.121", nc.Ip);
            // ipconfig /renew and /all on CL1 use the lab's DHCP; nslookup asks the lab's DNS
            w.Do("dhcp scope reserve server=SRV1 id=10.10.0.0 ip=10.10.0.160 mac=" + w.L.DhcpClient("CL1").Mac + " name=CL1");
            var rn = w.Ps("CL1", "ipconfig /renew");
            Check("ipconfig /renew renews the lease", w.Is("lease(CL1).ip == 10.10.0.160") && rn.Contains("10.10.0.160"), rn);
            Check("ipconfig /renew prints its title once", rn.Split(new[] { "Windows IP Configuration" }, StringSplitOptions.None).Length == 2, rn);
            var all = w.Ps("CL1", "ipconfig /all");
            Check("ipconfig /all names the lab's DHCP server", all.Contains("DHCP Server . . . . . . . . . . . : 10.10.0.50"), all);
            var ns = w.Ps("CL1", "nslookup fs1.corp.example");
            Check("nslookup asks the lab's DNS server", ns.Contains("dc1.corp.example") && ns.Contains("10.10.0.20"), ns);
            var ns2 = w.Ps("CL1", "nslookup files.tidewater.example");
            Check("nslookup reports a missing name", ns2.Contains("Non-existent domain"), ns2);
            // failover with CORE1
            w.Bad("dhcp failover add server=SRV1 partner=CORE1 scopes=10.10.0.0", "running");
            w.Do("feature install CORE1 DHCP");
            w.Bad("dhcp failover add server=SRV1 partner=CORE1 scopes=10.10.0.0", "authorized");
            w.Do("dhcp authorize server=CORE1");
            w.Do("dhcp failover add server=SRV1 partner=CORE1 name=HQ-Failover scopes=10.10.0.0 mode=LoadBalance percent=50 secret=Sh4red!");
            w.Yes("dhcp(CORE1).scope[\"10.10.0.0\"].reservation[\"10.10.0.150\"].mac == 00-1B-A9-4C-21-07");
            w.Yes("dhcp(SRV1).failover[\"HQ-Failover\"].partner == CORE1");
            w.Do("dhcp scope reserve server=SRV1 id=10.10.0.0 ip=10.10.0.151 mac=8C-85-90-1E-6A-33 name=TAB");
            w.No("dhcp(CORE1).scope[\"10.10.0.0\"].reservation[\"10.10.0.151\"]");
            w.Do("dhcp failover replicate server=SRV1 name=HQ-Failover");
            w.Yes("dhcp(CORE1).scope[\"10.10.0.0\"].reservation[\"10.10.0.151\"].name == TAB");
            w.Ps("SRV1", "Stop-Service DHCPServer");
            Check("with SRV1's DHCP stopped, CORE1 serves the scope", w.L.Renew("CL1") == null && w.L.DhcpClient("CL1").Server == "CORE1", w.L.DhcpClient("CL1").Server);
            w.Ps("CORE1", "Stop-Service DHCPServer");
            string keep = w.L.DhcpClient("CL1").Ip;
            Check("with no DHCP server a valid lease is kept", w.L.Renew("CL1") != null && w.L.DhcpClient("CL1").Ip == keep, w.L.DhcpClient("CL1").Ip);
            w.Wait(8 * 86400 + 60);
            Check("once the lease runs out the client gets APIPA", w.L.DhcpClient("CL1").Ip.StartsWith("169.254."), w.L.DhcpClient("CL1").Ip);
            w.Yes("lease(CL1).apipa == Yes");
            w.Dispose();
        }
    }
}
