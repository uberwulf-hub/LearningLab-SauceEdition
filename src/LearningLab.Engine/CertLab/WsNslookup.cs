// Cert Lab - nslookup on the AZ-800 lab's machines: asks the lab's DNS servers (the first one the machine uses, or
// the server named after the name), so forwarders, stub zones and policies behave as they do for Resolve-DnsName;
// and which ports answer at addresses outside the office for Test-NetConnection.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public partial class WsLab
    {
        public int Nslookup(NativeCall call)
        {
            var m = call.M;
            string name = null, server = null, type = "A";
            foreach (var a in call.Args)
            {
                if (a.StartsWith("-type=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("-q=", StringComparison.OrdinalIgnoreCase)) { type = a.Substring(a.IndexOf('=') + 1).ToUpperInvariant(); continue; }
                if (name == null) name = a; else if (server == null) server = a;
            }
            string ip = null;
            if (server != null) { var s = Server(server); ip = s != null ? s.M.Ip : server; }
            else { var l = m.DnsFor(m.Eth.IfIndex); if (l.Count > 0) ip = l[0]; }
            if (ip == null) { call.L("Default Server:  UnKnown"); call.L("Address:  127.0.0.1"); call.L(""); return 1; }
            var ds = Server(ip);
            bool up = ds != null && ds.Dns != null && ds.Dns.Up && Delivers(m, ip);
            string label = up ? ds.M.Fqdn : "UnKnown";
            if (!up) { call.L("DNS request timed out."); call.L("    timeout was 2 seconds."); }
            call.L((name == null ? "Default Server:  " : "Server:  ") + label);
            call.L("Address:  " + ip);
            call.L("");
            if (name == null) { call.L("> "); return 0; }
            if (!up) { call.L("DNS request timed out."); call.L("    timeout was 2 seconds."); call.L("*** Request to UnKnown timed-out"); return 1; }
            string err, fq;
            var ans = ClientResolve(m, name, Ip4.IsV4(name) ? "PTR" : type, ip, out err, out fq);
            if (err != null || ans.Count == 0)
            {
                call.L("*** " + label + " can't find " + name + ": " + (err == "NXDOMAIN" || err == null ? "Non-existent domain" : err == "TIMEOUT" ? "Server failed" : "Query refused"));
                return 1;
            }
            var zone = ds.Dns.ZoneFor(fq);
            if (zone == null || zone.Type == "Forwarder" || zone.Type == "Stub") call.L("Non-authoritative answer:");
            var ptr = ans.FirstOrDefault(x => x.Type == "PTR");
            if (ptr != null) { call.L("Name:    " + ptr.Data); call.L("Address:  " + name); return 0; }
            foreach (var x in ans.Where(x => x.Type != "A" && x.Type != "CNAME"))
            {
                if (x.Type == "NS") call.L(x.Name + "\tnameserver = " + x.Data);
                else if (x.Type == "TXT") call.L(x.Name + "\ttext =\n\n\t\"" + x.Data + "\"");
                else call.L(x.Name + "\t" + x.Type + " = " + x.Data);
            }
            var a4 = ans.Where(x => x.Type == "A").ToList();
            if (a4.Count > 0)
            {
                call.L("Name:    " + a4[0].Name);
                call.L((a4.Count > 1 ? "Addresses:  " : "Address:  ") + string.Join("\n\t  ", a4.Select(x => x.Data).ToArray()));
            }
            var cn = ans.Where(x => x.Type == "CNAME").ToList();
            if (cn.Count > 0) call.L("Aliases:  " + string.Join("\n\t  ", cn.Select(x => x.Name).ToArray()));
            return 0;
        }

        /// <summary>Test-NetConnection -Port to an address outside the office (Azure, the ISP, the partner) that the lab
        /// can reach: DNS servers answer on 53, everything else on the usual web, SMB, remote desktop and remoting ports.</summary>
        public bool OutsidePortOpen(string ip, int port)
        {
            if (ip == IspDns || ip == PartnerDns || ip == AzureDns) return port == 53;
            return port == 80 || port == 443 || port == 445 || port == 3389 || port == 5985 || port == 5986;
        }
    }
}
