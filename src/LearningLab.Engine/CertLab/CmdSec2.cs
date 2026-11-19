// Cert Lab - nmap for the Security+ labs: a TCP connect scan of a lab host. Each port's state comes from the same
// rules ssh and curl follow (Machine.Connect: routing, the target's firewalld zones and rich rules, who listens):
// open, closed (nothing listens: the host answers with a reset), filtered (the firewall rejects or drops it).
// Real nmap scans the 1000 most common ports by default; Cert Lab scans a shorter list of common ports plus every
// port a service listens on or the firewall opens, and reports the rest as the most common state.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public static partial class Cmd
    {
        static readonly int[] NmapTop = { 21, 22, 23, 25, 53, 80, 110, 111, 135, 139, 143, 443, 445, 465, 587, 631, 873, 993, 995, 1433, 2049, 3306, 3389, 5000, 5432, 5900, 8080, 8443, 9090 };
        static readonly Dictionary<int, string> NmapNames = new Dictionary<int, string>
        {
            { 21, "ftp" }, { 22, "ssh" }, { 23, "telnet" }, { 25, "smtp" }, { 53, "domain" }, { 80, "http" }, { 110, "pop3" }, { 111, "rpcbind" }, { 135, "msrpc" },
            { 139, "netbios-ssn" }, { 143, "imap" }, { 443, "https" }, { 445, "microsoft-ds" }, { 465, "smtps" }, { 587, "submission" }, { 631, "ipp" }, { 873, "rsync" },
            { 993, "imaps" }, { 995, "pop3s" }, { 1433, "ms-sql-s" }, { 2049, "nfs" }, { 3306, "mysql" }, { 3389, "ms-wbt-server" }, { 5000, "upnp" }, { 5432, "postgresql" },
            { 5900, "vnc" }, { 8080, "http-proxy" }, { 8443, "https-alt" }, { 9090, "zeus-admin" }
        };

        static int Nmap(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            bool sV = false, sn = false; string ports = null; var hosts = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-p" && i + 1 < a.Count) ports = a[++i];
                else if (s.StartsWith("-p", StringComparison.Ordinal) && s.Length > 2) ports = s.Substring(2);
                else if (s == "-sV" || s == "-A") sV = true;
                else if (s == "-sn") sn = true;
                else if (s == "-sS" && sh.P.Cr.Uid != 0) { io.L("You requested a scan type which requires root privileges.\nQUITTING!"); return 1; }
                else if (s == "-sT" || s == "-sS" || s == "-Pn" || s == "-n" || s == "-v" || s == "-T4" || s == "-F" || s == "--open") { }
                else if (s.StartsWith("-", StringComparison.Ordinal)) { io.E("nmap: unrecognized option '" + s + "' (Cert Lab simulates -sT, -sS, -sV, -sn, -p, -Pn, -F)"); return 1; }
                else hosts.Add(s);
            }
            if (hosts.Count == 0) { io.L("Nmap 7.92 ( https://nmap.org )\nUsage: nmap [Scan Type(s)] [Options] {target specification}\nSee the output of nmap -h or man nmap."); return 0; }
            bool onlyOpen = a.Contains("--open");
            // a whole network (10.10.1.0/24): every address in it; only the hosts that answer are reported
            int addresses = 0;
            var expanded = new List<string>();
            foreach (var h in hosts)
            {
                if (!h.Contains("/")) { expanded.Add(h); addresses++; continue; }
                var cp = h.Split('/'); int bits;
                if (!LabNet.IsIp(cp[0]) || !int.TryParse(cp[1], out bits) || bits < 0 || bits > 32) { io.L("Failed to resolve \"" + h + "\"."); continue; }
                if (bits < 22) { io.E("nmap: Cert Lab scans networks of up to 1024 addresses (/22 or smaller)."); return 1; }
                uint mask = bits == 0 ? 0 : 0xFFFFFFFFu << (32 - bits);
                uint start = LabNet.IpNum(cp[0]) & mask, count = bits == 32 ? 1u : (1u << (32 - bits));
                for (uint k = 0; k < count; k++)
                {
                    uint v = start + k;
                    if (bits <= 30 && (k == 0 || k == count - 1)) continue;   // the network and broadcast addresses
                    expanded.Add(((v >> 24) & 255) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255));
                }
                addresses += (int)count;
            }
            hosts = expanded;
            io.L("Starting Nmap 7.92 ( https://nmap.org ) at " + DateFormat(m, m.Now, "%Y-%m-%d %H:%M %Z", false));
            int up = 0;
            foreach (var host in hosts)
            {
                string ip = m.Net.Resolve(m, host);
                if (ip == null) { io.L("Failed to resolve \"" + host + "\"."); continue; }
                bool self = ip.StartsWith("127.", StringComparison.Ordinal) || ip == m.FirstV4() || m.Ifs.Any(f => f.Up && f.V4.Any(x => x.Split('/')[0] == ip));
                if (!self && (!m.Net.Reachable(m, ip) || m.Net.Owner(ip) == null)) continue;
                up++;
                var target = self ? m : m.Net.Owner(ip) as Machine;
                var rangeHost = self ? null : m.Net.Owner(ip) as RangeHost;
                io.L("Nmap scan report for " + (host == ip ? ip : host + " (" + ip + ")"));
                io.L("Host is up (0.00" + (31 + Math.Abs(FwHash.Of(ip)) % 60) + "s latency).");
                if (sn) { if (m.Net.Range != null && !self) m.Net.Range.Scanned(m, ip, null, 0, false); continue; }
                List<int> list;
                bool explicitList = ports != null;
                if (explicitList)
                {
                    list = new List<int>();
                    foreach (var part in ports.Split(','))
                    {
                        var r = part.Split('-'); int x, y;
                        if (!int.TryParse(r[0], out x)) { io.E("Error #485: Your port specifications are illegal."); return 1; }
                        y = r.Length > 1 && int.TryParse(r[1], out y) ? y : x;
                        for (int p = x; p <= y && list.Count < 70000; p++) list.Add(p);
                    }
                }
                else
                {
                    list = new List<int>(NmapTop);
                    if (target != null)
                    {
                        foreach (var sv in target.Services.Values) if (sv.Active == "active") list.AddRange(sv.Ports);
                        foreach (var c in target.Containers) if (c.Running) list.AddRange(c.Ports.Select(p => p.Host));
                    }
                    if (rangeHost != null) list.AddRange(rangeHost.Ports);
                    list = list.Where(p => p > 0 && p < 65536).Distinct().OrderBy(p => p).ToList();
                }
                var states = new List<KeyValuePair<int, string>>();
                foreach (var p in list.Distinct().OrderBy(x => x))
                {
                    object tg; string tip;
                    string r = m.Connect(ip, p, out tg, out tip);
                    states.Add(new KeyValuePair<int, string>(p, r == null ? "open" : r == "refused" ? "closed" : "filtered"));
                }
                if (m.Net.Range != null && !self) m.Net.Range.Scanned(m, ip, states.Where(x => x.Value == "open").Select(x => x.Key).ToList(), states.Count, sV);
                string common = explicitList ? null : states.GroupBy(s => s.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(k => k != "open");
                var shown = states.Where(s => (common == null || s.Value != common) && (!onlyOpen || s.Value == "open")).ToList();
                if (common != null) io.L("Not shown: " + (1000 - states.Count(s => s.Value != common)) + " " + common + " tcp ports (" + (common == "closed" ? "conn-refused" : "no-response") + ")");
                if (shown.Count == 0) { io.L("All " + (explicitList ? states.Count : 1000) + " scanned ports on " + host + " are in ignored states."); continue; }

                int w = Math.Max(5, shown.Max(s => (s.Key + "/tcp").Length));
                io.L("PORT".PadRight(w + 1) + "STATE    SERVICE" + (sV ? "       VERSION" : ""));
                foreach (var s in shown)
                {
                    string name; if (!NmapNames.TryGetValue(s.Key, out name)) name = "unknown";
                    string line = (s.Key + "/tcp").PadRight(w + 1) + s.Value.PadRight(9) + (sV ? name.PadRight(14) : name);
                    string rname;
                    if (rangeHost != null && rangeHost.Names.TryGetValue(s.Key, out rname)) line = (s.Key + "/tcp").PadRight(w + 1) + s.Value.PadRight(9) + (sV ? rname.PadRight(14) : rname);
                    if (sV && s.Value == "open") line += rangeHost != null ? rangeHost.Version(s.Key) : NmapVersion(target, s.Key, m);
                    io.L(line.TrimEnd());
                }
            }
            io.L("");
            if (sV) io.L("Service detection performed. Please report any incorrect results at https://nmap.org/submit/ .");
            io.L("Nmap done: " + addresses + " IP address" + (addresses == 1 ? "" : "es") + " (" + up + " host" + (up == 1 ? "" : "s") + " up) scanned in " + (0.08 + hosts.Count * 0.11).ToString("0.00", CultureInfo.InvariantCulture) + " seconds");
            return 0;
        }

        static string NmapVersion(Machine t, int port, Machine from)
        {
            if (t != null && from.Net.Range != null) { string rv = from.Net.Range.VersionOf(t, port); if (rv != null) return rv; }
            if (t == null) return port == 22 ? "OpenSSH 8.7 (protocol 2.0)" : port == 80 ? "Apache httpd 2.4.62 ((Red Hat Enterprise Linux))" : "";
            string u = t.Listener(port, "tcp") ?? "";
            if (u.StartsWith("sshd", StringComparison.Ordinal)) return "OpenSSH 8.7 (protocol 2.0)";
            if (u.StartsWith("httpd", StringComparison.Ordinal)) return "Apache httpd 2.4.62 ((Red Hat Enterprise Linux))";
            if (u.StartsWith("nginx", StringComparison.Ordinal)) return "nginx 1.20.1";
            if (u.StartsWith("vsftpd", StringComparison.Ordinal)) return "vsftpd 3.0.5";
            if (u.StartsWith("cockpit", StringComparison.Ordinal)) return "Cockpit web service";
            if (u.StartsWith("container:", StringComparison.Ordinal)) return "(a container: " + u.Substring(10) + ")";
            return "";
        }
    }
}
