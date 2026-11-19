// Cert Lab - networking on a machine: NetworkManager keyfile profiles (what nmcli edits) and the addresses they
// give the interfaces when activated, name resolution, firewalld zones (runtime and permanent), connections
// between machines (ssh, http, nfs) with the firewall's and SELinux's say, and the daemons that listen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class NmConn
    {
        public string Id = "", Uuid = "", Type = "ethernet", Iface = "", File = "";
        public bool Autoconnect = true;
        public string V4Method = "auto", V4Gw = "", V6Method = "auto", V6Gw = "";
        public List<string> V4Addrs = new List<string>(), V4Dns = new List<string>(), V4Search = new List<string>();
        public List<string> V6Addrs = new List<string>(), V6Dns = new List<string>();
        public string Raw = "";
        public int AutoPrio;
        public bool V4NeverDefault, V4IgnoreAutoDns;
    }

    public partial class Machine
    {
        public const string NmDir = "/etc/NetworkManager/system-connections";

        // ------------------------------------------------------------ profiles
        public List<NmConn> NmConns()
        {
            var l = new List<NmConn>();
            try
            {
                var d = V.G(NmDir);
                if (!d.Exists || !d.IsDir) return l;
                foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                {
                    if (!k.EndsWith(".nmconnection", StringComparison.Ordinal)) continue;
                    var f = V.G(NmDir + "/" + k);
                    if (!f.Exists || f.Node.T != 'f') continue;
                    // NetworkManager ignores keyfiles others can read or that root does not own
                    if (f.Node.Uid != 0 || (f.Node.Mode & 0x3F) != 0) continue;
                    var c = ParseNm(f.Node.Text ?? "");
                    c.File = NmDir + "/" + k;
                    if (c.Id.Length == 0) c.Id = k.Replace(".nmconnection", "");
                    l.Add(c);
                }
            }
            catch (VfsEx) { }
            return l;
        }

        public static NmConn ParseNm(string text)
        {
            var c = new NmConn { Raw = text };
            string sec = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal)) { sec = line.Trim('[', ']'); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (sec == "connection")
                {
                    if (k == "id") c.Id = v; else if (k == "uuid") c.Uuid = v; else if (k == "type") c.Type = v == "802-3-ethernet" ? "ethernet" : v;
                    else if (k == "interface-name") c.Iface = v; else if (k == "autoconnect") c.Autoconnect = v != "false" && v != "no" && v != "0";
                    else if (k == "autoconnect-priority") int.TryParse(v, out c.AutoPrio);
                }
                else if (sec == "ipv4" || sec == "ipv6")
                {
                    bool four = sec == "ipv4";
                    if (k == "method") { if (four) c.V4Method = v; else c.V6Method = v; }
                    else if (Regex.IsMatch(k, "^(address|addresses)\\d*$"))
                    {
                        // address1=192.168.1.5/24,192.168.1.1  (old form with the gateway)
                        foreach (var part in v.Split(';'))
                        {
                            if (part.Length == 0) continue;
                            var ag = part.Split(',');
                            if (four) c.V4Addrs.Add(ag[0]); else c.V6Addrs.Add(ag[0]);
                            if (ag.Length > 1 && ag[1].Length > 0) { if (four) c.V4Gw = ag[1]; else c.V6Gw = ag[1]; }
                        }
                    }
                    else if (k == "gateway") { if (four) c.V4Gw = v; else c.V6Gw = v; }
                    else if (k == "dns") { foreach (var x in v.Split(';')) if (x.Length > 0) { if (four) c.V4Dns.Add(x); else c.V6Dns.Add(x); } }
                    else if (k == "dns-search" && four) { foreach (var x in v.Split(';')) if (x.Length > 0) c.V4Search.Add(x); }
                    else if (k == "never-default" && four) c.V4NeverDefault = v == "true";
                    else if (k == "ignore-auto-dns" && four) c.V4IgnoreAutoDns = v == "true";
                }
            }
            return c;
        }

        public string NmText(NmConn c)
        {
            var sb = new StringBuilder();
            sb.Append("[connection]\nid=" + c.Id + "\nuuid=" + c.Uuid + "\ntype=" + c.Type + "\n");
            if (!c.Autoconnect) sb.Append("autoconnect=false\n");
            if (c.AutoPrio != 0) sb.Append("autoconnect-priority=" + c.AutoPrio + "\n");
            if (c.Iface.Length > 0) sb.Append("interface-name=" + c.Iface + "\n");
            sb.Append("\n[ethernet]\n\n[ipv4]\n");
            for (int i = 0; i < c.V4Addrs.Count; i++) sb.Append("address" + (i + 1) + "=" + c.V4Addrs[i] + "\n");
            if (c.V4Dns.Count > 0) sb.Append("dns=" + string.Join(";", c.V4Dns.ToArray()) + ";\n");
            if (c.V4Search.Count > 0) sb.Append("dns-search=" + string.Join(";", c.V4Search.ToArray()) + ";\n");
            if (c.V4Gw.Length > 0) sb.Append("gateway=" + c.V4Gw + "\n");
            if (c.V4IgnoreAutoDns) sb.Append("ignore-auto-dns=true\n");
            sb.Append("method=" + c.V4Method + "\n");
            if (c.V4NeverDefault) sb.Append("never-default=true\n");
            sb.Append("\n[ipv6]\naddr-gen-mode=eui64\n");
            for (int i = 0; i < c.V6Addrs.Count; i++) sb.Append("address" + (i + 1) + "=" + c.V6Addrs[i] + "\n");
            if (c.V6Dns.Count > 0) sb.Append("dns=" + string.Join(";", c.V6Dns.ToArray()) + ";\n");
            if (c.V6Gw.Length > 0) sb.Append("gateway=" + c.V6Gw + "\n");
            sb.Append("method=" + c.V6Method + "\n\n[proxy]\n");
            return sb.ToString();
        }

        public void SaveNm(NmConn c)
        {
            if (c.File.Length == 0) c.File = NmDir + "/" + c.Id.Replace("/", "_") + ".nmconnection";
            SysWrite(c.File, NmText(c), 0x180, null);
            try { var l = V.G(c.File); l.Node.Mode = 0x180; l.Node.Uid = 0; l.Node.Gid = 0; } catch (VfsEx) { }
        }

        // ------------------------------------------------------------ activating
        public NetIf Iface(string name) { return Ifs.FirstOrDefault(i => i.Name == name); }

        public string Mac(int i) { return i < Macs.Count ? Macs[i] : "52:54:00:00:00:0" + i; }

        /// <summary>nmcli con up: the profile's settings go onto its interface.</summary>
        public string ConnUp(NmConn c)
        {
            string ifname = c.Iface.Length > 0 ? c.Iface : NicNames[0];
            int idx = NicNames.IndexOf(ifname);
            if (idx < 0) return "Error: Connection activation failed: No suitable device found for this connection (device " + ifname + " not available because profile is not compatible with device (mismatching interface name)).";
            var nif = Iface(ifname);
            if (nif == null) { nif = new NetIf { Name = ifname, Mac = Mac(idx) }; Ifs.Add(nif); }
            nif.Conn = c.Id; nif.Up = true;
            nif.V4.Clear(); nif.V6.Clear(); nif.Dns.Clear(); nif.Search.Clear(); nif.Gw4 = ""; nif.Gw6 = "";
            if (c.V4Method == "manual")
            {
                if (c.V4Addrs.Count == 0) return "Error: Connection activation failed: IP configuration could not be reserved (no available address, timeout, etc.).";
                foreach (var a in c.V4Addrs) nif.V4.Add(a.Contains("/") ? a : a + "/32");
                nif.Gw4 = c.V4Gw;
            }
            else if (c.V4Method == "auto")
            {
                int mi = Net.Machines.IndexOf(this);
                nif.V4.Add("192.168.50." + (100 + Math.Max(0, mi) * 10 + idx + 1) + "/24");
                nif.Gw4 = c.V4NeverDefault ? "" : "192.168.50.254";
                if (!c.V4IgnoreAutoDns) { nif.Dns.Add("192.168.50.254"); nif.Search.Add("lab.example.com"); }
            }
            nif.Dns.InsertRange(0, c.V4Dns.Where(x => !nif.Dns.Contains(x)).ToList());
            nif.Search.InsertRange(0, c.V4Search.Where(x => !nif.Search.Contains(x)).ToList());   // the profile's domains first, in their order
            if (c.V6Method != "disabled" && c.V6Method != "ignore") nif.V6.Add(LinkLocal(nif.Mac));
            if (c.V6Method == "manual") { foreach (var a in c.V6Addrs) nif.V6.Insert(0, a.Contains("/") ? a : a + "/128"); nif.Gw6 = c.V6Gw; }
            nif.Dns.AddRange(c.V6Dns.Where(x => !nif.Dns.Contains(x)));
            WriteResolv();
            Log("NetworkManager", ServicePid("NetworkManager.service"), 6, "<info>  [" + Now + ".1234] device (" + ifname + "): Activation: successful, device activated.", "NetworkManager.service");
            return null;
        }

        public void ConnDown(string ifname)
        {
            var nif = Iface(ifname);
            if (nif == null) return;
            nif.V4.Clear(); nif.V6.Clear(); nif.Gw4 = ""; nif.Gw6 = ""; nif.Dns.Clear(); nif.Search.Clear(); nif.Conn = "";
            WriteResolv();
        }

        public static string LinkLocal(string mac)
        {
            var b = mac.Split(':').Select(x => Convert.ToInt32(x, 16)).ToArray();
            b[0] ^= 2;
            return string.Format("fe80::{0:x}:{1:x}:{2:x}:{3:x}/64", (b[0] << 8) | b[1], (b[2] << 8) | 0xff, 0xfe00 | b[3], (b[4] << 8) | b[5]);
        }

        void WriteResolv()
        {
            var sb = new StringBuilder("# Generated by NetworkManager\n");
            var search = Ifs.SelectMany(i => i.Search).Distinct().ToList();
            if (search.Count > 0) sb.Append("search " + string.Join(" ", search.ToArray()) + "\n");
            foreach (var d in Ifs.SelectMany(i => i.Dns).Distinct()) sb.Append("nameserver " + d + "\n");
            try { var l = V.GNoFollow("/etc/resolv.conf"); if (l.Exists && l.Node.T == 'l') return; SysWrite("/etc/resolv.conf", sb.ToString()); } catch (VfsEx) { }
        }

        /// <summary>NetworkManager started: every interface gets its best autoconnect profile.</summary>
        public void NmStart()
        {
            var conns = NmConns();
            for (int i = 0; i < NicNames.Count; i++)
            {
                string n = NicNames[i];
                if (Iface(n) == null) Ifs.Add(new NetIf { Name = n, Mac = Mac(i) });
                var c = conns.Where(x => x.Autoconnect && (x.Iface == n || (x.Iface.Length == 0 && i == 0))).OrderByDescending(x => x.AutoPrio).FirstOrDefault();
                if (c != null) ConnUp(c);
                else { var nif = Iface(n); nif.V6.Clear(); nif.V6.Add(LinkLocal(nif.Mac)); }
            }
            string hn = (V.Cat("/etc/hostname") ?? "").Trim();
            if (hn.Length > 0) Hostname = hn;
        }

        void ApplyNetworkFallback()
        {
            // without NetworkManager only the loopback works
            for (int i = 0; i < NicNames.Count; i++) if (Iface(NicNames[i]) == null) Ifs.Add(new NetIf { Name = NicNames[i], Mac = Mac(i), Up = false });
        }

        public string FirstV4()
        {
            foreach (var i in Ifs) if (i.Up) foreach (var a in i.V4) return a.Split('/')[0];
            return null;
        }
        public bool HasDnsServer()
        {
            string t = V.Cat("/etc/resolv.conf") ?? "";
            foreach (Match m in Regex.Matches(t, "(?m)^\\s*nameserver\\s+(\\S+)"))
                if (m.Groups[1].Value == Net.UtilityIp && Net.Reachable(this, Net.UtilityIp)) return true;
            return false;
        }
        public List<string> SearchDomains()
        {
            string t = V.Cat("/etc/resolv.conf") ?? "";
            var m = Regex.Match(t, "(?m)^\\s*search\\s+(.+)$");
            var l = m.Success ? m.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>();
            string hn = HostnameNow; int dot = hn.IndexOf('.');
            if (dot > 0 && !l.Contains(hn.Substring(dot + 1))) l.Add(hn.Substring(dot + 1));
            return l;
        }

        // ------------------------------------------------------------ firewalld
        public Dictionary<string, FwZone> FwPermanent() { return Firewall.LoadPermanent(this); }

        public void FwStart()
        {
            FwRuntime = FwPermanent();
            FwDefaultZone = Firewall.DefaultZone(this);
            FwRunning = true;
        }

        public FwZone FwZoneFor(string srcIp, string iface)
        {
            if (srcIp != null) foreach (var z in FwRuntime.Values) foreach (var s in z.Sources) if (LabNet.SameNet(s.Contains("/") ? s : s + "/32", srcIp)) return z;
            if (iface != null) foreach (var z in FwRuntime.Values) if (z.Interfaces.Contains(iface)) return z;
            FwZone d; FwRuntime.TryGetValue(FwDefaultZone, out d);
            return d;
        }

        /// <summary>Does the firewall let a connection in (port/proto from srcIp)?</summary>
        [NonSerialized] public string FwBlock = "";   // how the last refused connection was refused: reject (rich rule), drop, or "" (the zone: host prohibited)
        public bool FwAllowsIcmp() { return true; }   // firewalld lets ping through unless icmp-blocks say otherwise (not simulated)
        public bool FwAllows(int port, string proto, string srcIp)
        {
            FwBlock = "";
            if (!FwRunning) return true;
            if (srcIp == null || srcIp.StartsWith("127.", StringComparison.Ordinal)) return true;
            var z = FwZoneFor(srcIp, NicNames.Count > 0 ? NicNames[0] : null);
            if (z == null) return false;
            if (z.Target == "ACCEPT") return true;
            foreach (var r in z.Rich)
            {
                var m = Regex.Match(r, "source address=\"?([0-9./]+)\"?");
                bool srcOk = !m.Success || LabNet.SameNet(m.Groups[1].Value.Contains("/") ? m.Groups[1].Value : m.Groups[1].Value + "/32", srcIp);
                if (!srcOk) continue;
                bool portOk = true;
                var sm = Regex.Match(r, "service name=\"?([\\w-]+)\"?");
                var pm = Regex.Match(r, "port port=\"?(\\d+)(-(\\d+))?\"? protocol=\"?(\\w+)\"?");
                if (sm.Success) portOk = Firewall.ServicePorts(this, sm.Groups[1].Value).Any(p => PortMatch(p, port, proto));
                else if (pm.Success) portOk = PortMatch(pm.Groups[1].Value + (pm.Groups[3].Success && pm.Groups[3].Value.Length > 0 ? "-" + pm.Groups[3].Value : "") + "/" + pm.Groups[4].Value, port, proto);
                else if (!r.Contains(" service ") && !r.Contains(" port ")) portOk = true;
                if (!portOk) continue;
                if (r.Contains(" reject")) { FwBlock = r.Contains("host-prohibited") || r.Contains("host-unreach") ? "" : "reject"; return false; }
                if (r.Contains(" drop")) { FwBlock = "drop"; return false; }
                if (r.Contains(" accept")) return true;
            }
            if (z.Target == "DROP") { FwBlock = "drop"; return false; }
            if (z.Target == "REJECT" || z.Target == "%%REJECT%%") return false;
            foreach (var s in z.Services) foreach (var p in Firewall.ServicePorts(this, s)) if (PortMatch(p, port, proto)) return true;
            foreach (var p in z.Ports) if (PortMatch(p, port, proto)) return true;
            return false;
        }
        static bool PortMatch(string spec, int port, string proto)
        {
            var s = spec.Split('/');
            if (s.Length < 2 || s[1] != proto) return false;
            var r = s[0].Split('-');
            int a, b;
            if (!int.TryParse(r[0], out a)) return false;
            b = r.Length > 1 && int.TryParse(r[1], out b) ? b : a;
            return port >= a && port <= b;
        }

        // ------------------------------------------------------------ who listens
        /// <summary>The service listening on a port ("sshd.service", "container:web"), or null.</summary>
        public string Listener(int port, string proto)
        {
            foreach (var s in Services.Values) if (s.Active == "active" && s.Ports.Contains(port)) return s.Unit;
            foreach (var c in Containers) if (c.Running) foreach (var p in c.Ports) if (p.Host == port) return "container:" + c.Name;
            // Cyber Range: a pretend service or web app listening on a port of its own
            if (Net != null && Net.Range != null) return Net.Range.RangeListener(this, port);
            return null;
        }

        /// <summary>A TCP connection from this machine to host:port. Returns null (connected) or curl / ssh style text.</summary>
        public string Connect(string host, int port, out object target, out string ip)
        {
            target = null;
            ip = Net.Resolve(this, host);
            if (ip == null) return "resolve";
            if (ip.StartsWith("127.", StringComparison.Ordinal) || ip == FirstV4())
            {
                target = this;
                return Listener(port, "tcp") != null ? null : "refused";
            }
            if (!Net.Reachable(this, ip)) return "noroute";
            var owner = Net.Owner(ip);
            if (owner == null) return "noroute";
            // Cyber Range: traffic that crosses the router must be on a port it lets through
            if (Net.Range != null && !Net.Range.DirectlyConnected(this, ip) && !Net.Range.RouteAllows(this, ip, port)) return "timeout";
            // Cyber Range (Red vs defender): the defender may have blocked this source
            if (Net.Range != null && Net.Range.DefenderBlocks(this, ip)) return "timeout";
            var rh = owner as RangeHost;
            if (rh != null) { target = rh; return rh.Listens(port) ? null : "refused"; }
            if (owner is string)
            {
                target = "utility";
                return port == 80 || port == 22 || port == 2049 || port == 5000 || port == 123 || port == 53 ? null : "refused";
            }
            var m = (Machine)owner;
            target = m;
            if (!m.FwAllows(port, "tcp", FirstV4())) return m.FwBlock == "reject" ? "refused" : m.FwBlock == "drop" ? "timeout" : "noroute";
            return m.Listener(port, "tcp") != null ? null : "refused";
        }

        // ------------------------------------------------------------ chrony
        public List<string> ChronyServers()
        {
            var l = new List<string>();
            string t = V.Cat("/etc/chrony.conf") ?? "";
            foreach (Match m in Regex.Matches(t, "(?m)^\\s*(server|pool|peer)\\s+(\\S+)")) l.Add(m.Groups[1].Value + " " + m.Groups[2].Value);
            return l;
        }
        void ChronyBoot() { ChronySyncedAt = -1; ChronySource = ""; }
        void ChronyTick()
        {
            if (!ServiceActive("chronyd.service")) return;
            if (ChronySyncedAt >= 0) return;
            var st = Svc("chronyd.service", null);
            if (st == null || Now - st.Since < 8) return;
            foreach (var s in ChronyServers())
            {
                string host = s.Split(' ')[1];
                string ip = Net.Resolve(this, host);
                if (ip != null && ip == Net.UtilityIp && Net.Reachable(this, ip))
                {
                    ChronySyncedAt = Now; ChronySource = host;
                    Log("chronyd", st.Pid, 6, "Selected source " + ip + " (" + host + ")", "chronyd.service");
                    Log("chronyd", st.Pid, 6, "System clock TAI offset set to 37 seconds", "chronyd.service");
                    return;
                }
            }
        }
        public bool NtpSynced { get { return ServiceActive("chronyd.service") && ChronySyncedAt >= 0; } }

        // ------------------------------------------------------------ sshd's settings
        /// <summary>sshd_config with its Include lines; the first value of an option wins (sshd's rule).</summary>
        public Dictionary<string, string> SshdConfig(out string error)
        {
            error = null;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ports = new List<string>();
            ReadSshd("/etc/ssh/sshd_config", d, ports, ref error, 0);
            if (ports.Count > 0) d["Port"] = string.Join(",", ports.ToArray());
            return d;
        }
        static readonly HashSet<string> SshdOpts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Port", "AddressFamily", "ListenAddress", "HostKey", "SyslogFacility", "LogLevel", "LoginGraceTime", "PermitRootLogin", "StrictModes", "MaxAuthTries", "MaxSessions", "PubkeyAuthentication", "AuthorizedKeysFile", "PasswordAuthentication", "PermitEmptyPasswords", "ChallengeResponseAuthentication", "KbdInteractiveAuthentication", "GSSAPIAuthentication", "GSSAPICleanupCredentials", "UsePAM", "X11Forwarding", "PrintMotd", "PrintLastLog", "TCPKeepAlive", "ClientAliveInterval", "ClientAliveCountMax", "UseDNS", "Banner", "Subsystem", "AllowUsers", "DenyUsers", "AllowGroups", "DenyGroups", "Include", "Match", "AcceptEnv", "PermitTunnel", "AllowTcpForwarding", "Ciphers", "MACs", "KexAlgorithms", "HostKeyAlgorithms", "PubkeyAcceptedKeyTypes", "PubkeyAcceptedAlgorithms", "GSSAPIKeyExchange", "Compression", "MaxStartups", "PermitUserEnvironment", "PidFile", "ChrootDirectory", "ForceCommand", "AuthenticationMethods", "HostbasedAuthentication", "IgnoreRhosts", "X11DisplayOffset", "X11UseLocalhost", "UsePrivilegeSeparation", "RekeyLimit", "CASignatureAlgorithms", "RequiredRSASize" };
        void ReadSshd(string file, Dictionary<string, string> d, List<string> ports, ref string error, int depth)
        {
            if (depth > 4) return;
            string t = V.Cat(file);
            if (t == null) return;
            int n = 0;
            bool inMatch = false;
            foreach (var raw in t.Split('\n'))
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = Regex.Split(line, "\\s+", RegexOptions.None);
                string key = p[0].Split('=')[0];
                string val = p.Length > 1 ? string.Join(" ", p.Skip(1).ToArray()) : (p[0].Contains("=") ? p[0].Substring(p[0].IndexOf('=') + 1) : "");
                if (!SshdOpts.Contains(key)) { if (error == null) error = file + ": line " + n + ": Bad configuration option: " + key; continue; }
                if (key.Equals("Match", StringComparison.OrdinalIgnoreCase)) { inMatch = true; continue; }
                if (inMatch) continue;
                if (key.Equals("Include", StringComparison.OrdinalIgnoreCase))
                {
                    string pat = val;
                    string dir = Vfs.Parent(pat), filePat = Vfs.Base(pat);
                    if (!dir.StartsWith("/", StringComparison.Ordinal)) dir = "/etc/ssh/" + dir;
                    if (dir.StartsWith("/etc/crypto-policies", StringComparison.Ordinal)) continue;
                    try
                    {
                        var dl = V.G(dir);
                        if (dl.Exists && dl.IsDir)
                            foreach (var k in Vfs.Sorted(dl.Node.Kids.Keys).OrderBy(x => x, StringComparer.Ordinal))
                                if (Sh.GlobMatch(filePat, k)) ReadSshd(dir + "/" + k, d, ports, ref error, depth + 1);
                    }
                    catch (VfsEx) { }
                    continue;
                }
                if (key.Equals("Port", StringComparison.OrdinalIgnoreCase)) { ports.Add(val); continue; }
                if (!d.ContainsKey(key)) d[key] = val;
            }
        }
        public string SshdValue(string key, string def) { string e; var d = SshdConfig(out e); string v; return d.TryGetValue(key, out v) ? v : def; }
    }

    public static class Firewall
    {
        public static readonly FwZone[] Zones =
        {
            new FwZone { Name = "block", Target = "%%REJECT%%", Short = "Block" },
            new FwZone { Name = "dmz", Short = "DMZ", Services = new List<string> { "ssh" } },
            new FwZone { Name = "drop", Target = "DROP", Short = "Drop" },
            new FwZone { Name = "external", Short = "External", Services = new List<string> { "ssh" }, Masquerade = true },
            new FwZone { Name = "home", Short = "Home", Services = new List<string> { "ssh", "mdns", "samba-client", "dhcpv6-client", "cockpit" } },
            new FwZone { Name = "internal", Short = "Internal", Services = new List<string> { "ssh", "mdns", "samba-client", "dhcpv6-client", "cockpit" } },
            new FwZone { Name = "nm-shared", Target = "ACCEPT", Short = "NetworkManager Shared", Services = new List<string> { "dhcp", "dns", "ssh" } },
            new FwZone { Name = "public", Short = "Public", Services = new List<string> { "ssh", "dhcpv6-client", "cockpit" } },
            new FwZone { Name = "trusted", Target = "ACCEPT", Short = "Trusted" },
            new FwZone { Name = "work", Short = "Work", Services = new List<string> { "ssh", "dhcpv6-client", "cockpit" } },
        };

        // name, ports, description
        public static readonly string[][] ServiceDefs =
        {
            new[] { "ssh", "22/tcp", "Secure Shell (SSH) is a protocol for logging into and executing commands on remote machines." },
            new[] { "http", "80/tcp", "HTTP is the protocol used to serve Web pages." },
            new[] { "https", "443/tcp", "HTTPS is a modified HTTP used to serve Web pages when security is important." },
            new[] { "cockpit", "9090/tcp", "Cockpit lets you access and configure your server remotely." },
            new[] { "dhcpv6-client", "546/udp", "This option allows a DHCP for IPv6 (DHCPv6) client to obtain addresses and other IPv6 settings from DHCPv6 server." },
            new[] { "dhcp", "67/udp", "This allows a DHCP for IPv4 (DHCP) server to accept messages from DHCP clients and relay agents." },
            new[] { "dns", "53/tcp,53/udp", "The Domain Name System (DNS) is used to provide and request host and domain names." },
            new[] { "ntp", "123/udp", "The Network Time Protocol (NTP) allows to synchronize computers to a time server." },
            new[] { "nfs", "2049/tcp", "The NFS4 protocol is used to share files via TCP networking." },
            new[] { "mountd", "20048/tcp,20048/udp", "NFS Mount Lock Daemon" },
            new[] { "rpc-bind", "111/tcp,111/udp", "Remote Procedure Call Bind" },
            new[] { "samba", "139/tcp,445/tcp,137/udp,138/udp", "This option allows you to access and participate in Windows file and printer sharing networks." },
            new[] { "samba-client", "137/udp,138/udp", "This option allows you to access Windows file and printer sharing networks." },
            new[] { "mdns", "5353/udp", "mDNS provides the ability to use DNS programming interfaces, packet formats and operating semantics, in a small network without a conventional DNS server." },
            new[] { "ftp", "21/tcp", "FTP is a protocol used for remote file transfer." },
            new[] { "mysql", "3306/tcp", "MySQL Database Server" },
            new[] { "postgresql", "5432/tcp", "PostgreSQL Database Server" },
            new[] { "smtp", "25/tcp", "This option allows incoming SMTP mail delivery." },
            new[] { "imaps", "993/tcp", "The Internet Message Access Protocol over SSL (IMAPs)" },
            new[] { "pop3s", "995/tcp", "The Post Office Protocol version 3 over SSL (POP3s)" },
            new[] { "ldap", "389/tcp", "Lightweight Directory Access Protocol (LDAP) server" },
            new[] { "ldaps", "636/tcp", "Lightweight Directory Access Protocol (LDAP) server over SSL" },
            new[] { "kerberos", "88/tcp,88/udp", "Kerberos network authentication protocol server" },
            new[] { "syslog", "514/udp", "Syslog is a protocol used to log messages." },
            new[] { "rsyncd", "873/tcp,873/udp", "Rsync in daemon mode works as a central server" },
            new[] { "vnc-server", "5900-5903/tcp", "A VNC server provides an external accessible X session." },
            new[] { "squid", "3128/tcp", "Squid HTTP caching proxy" },
            new[] { "telnet", "23/tcp", "Telnet is a protocol for logging onto remote machines." },
            new[] { "tftp", "69/udp", "The Trivial File Transfer Protocol (TFTP) is a protocol used to transfer files to and from a remote machine." },
            new[] { "http-alt", "8080/tcp", "HTTP alternate port" },
            new[] { "nfs3", "2049/tcp,2049/udp", "The NFS protocol is used to share files." },
            new[] { "git", "9418/tcp", "The git daemon for supporting git:// access to git repositories." },
            new[] { "redis", "6379/tcp", "Redis is an open source in-memory data structure store." },
            new[] { "freeipa-ldap", "80/tcp,443/tcp,88/tcp,464/tcp,389/tcp,88/udp,464/udp", "FreeIPA with LDAP" },
            new[] { "libvirt", "16509/tcp", "Enable this option if you want to allow remote libvirt access." },
        };

        public static string ZoneXml(FwZone z)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<zone" + (z.Target != "default" ? " target=\"" + z.Target + "\"" : "") + ">\n");
            sb.Append("  <short>" + z.Short + "</short>\n");
            sb.Append("  <description>" + ZoneDescription(z.Name) + "</description>\n");
            foreach (var i in z.Interfaces) sb.Append("  <interface name=\"" + i + "\"/>\n");
            foreach (var s in z.Sources) sb.Append("  <source address=\"" + s + "\"/>\n");
            foreach (var s in z.Services) sb.Append("  <service name=\"" + s + "\"/>\n");
            foreach (var p in z.Ports) { var a = p.Split('/'); sb.Append("  <port port=\"" + a[0] + "\" protocol=\"" + (a.Length > 1 ? a[1] : "tcp") + "\"/>\n"); }
            if (z.Masquerade) sb.Append("  <masquerade/>\n");
            foreach (var r in z.Rich) sb.Append("  <rule>" + System.Security.SecurityElement.Escape(r) + "</rule>\n");
            sb.Append("  <forward/>\n</zone>\n");
            return sb.ToString();
        }
        public static string ZoneDescription(string z)
        {
            switch (z)
            {
                case "public": return "For use in public areas. You do not trust the other computers on networks to not harm your computer. Only selected incoming connections are accepted.";
                case "trusted": return "All network connections are accepted.";
                case "home": return "For use in home areas. You mostly trust the other computers on networks to not harm your computer. Only selected incoming connections are accepted.";
                case "internal": return "For use on internal networks. You mostly trust the other computers on the networks to not harm your computer. Only selected incoming connections are accepted.";
                case "work": return "For use in work areas. You mostly trust the other computers on networks to not harm your computer. Only selected incoming connections are accepted.";
                case "dmz": return "For computers in your demilitarized zone that are publicly-accessible with limited access to your internal network. Only selected incoming connections are accepted.";
                case "external": return "For use on external networks. You do not trust the other computers on networks to not harm your computer. Only selected incoming connections are accepted.";
                case "drop": return "Unsolicited incoming network packets are dropped. Incoming packets that are related to outgoing network connections are accepted. Outgoing network connections are allowed.";
                case "block": return "Unsolicited incoming network packets are rejected. Incoming packets that are related to outgoing network connections are accepted. Outgoing network connections are allowed.";
            }
            return "Zone " + z + ".";
        }
        public static string ServiceXml(string[] s)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<service>\n  <short>" + s[0].ToUpperInvariant() + "</short>\n  <description>" + System.Security.SecurityElement.Escape(s[2]) + "</description>\n");
            foreach (var p in s[1].Split(',')) { var a = p.Split('/'); sb.Append("  <port protocol=\"" + a[1] + "\" port=\"" + a[0] + "\"/>\n"); }
            sb.Append("</service>\n");
            return sb.ToString();
        }

        public static List<string> ServiceNames(Machine m)
        {
            var l = ServiceDefs.Select(s => s[0]).ToList();
            try { var d = m.V.G("/etc/firewalld/services"); if (d.Exists && d.IsDir) foreach (var k in d.Node.Kids.Keys) if (k.EndsWith(".xml", StringComparison.Ordinal)) l.Add(k.Substring(0, k.Length - 4)); } catch (VfsEx) { }
            return l.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        public static List<string> ServicePorts(Machine m, string name)
        {
            string custom = m.V.Cat("/etc/firewalld/services/" + name + ".xml");
            if (custom != null) return Regex.Matches(custom, "<port\\s+[^>]*>").Cast<Match>().Select(x => { var pp = Regex.Match(x.Value, "port=\"([^\"]+)\""); var pr = Regex.Match(x.Value, "protocol=\"([^\"]+)\""); return pp.Groups[1].Value + "/" + pr.Groups[1].Value; }).ToList();
            var s = ServiceDefs.FirstOrDefault(x => x[0] == name);
            return s == null ? new List<string>() : s[1].Split(',').ToList();
        }

        public static string DefaultZone(Machine m)
        {
            var t = m.V.Cat("/etc/firewalld/firewalld.conf") ?? "";
            var mm = Regex.Match(t, "(?m)^DefaultZone=(\\S+)");
            return mm.Success ? mm.Groups[1].Value : "public";
        }

        /// <summary>The permanent configuration: /usr/lib/firewalld/zones, overridden by /etc/firewalld/zones.</summary>
        public static Dictionary<string, FwZone> LoadPermanent(Machine m)
        {
            var d = new Dictionary<string, FwZone>(StringComparer.Ordinal);
            foreach (var z in Zones) d[z.Name] = z.Copy();
            foreach (var dir in new[] { "/usr/lib/firewalld/zones", "/etc/firewalld/zones" })
            {
                try
                {
                    var dl = m.V.G(dir);
                    if (!dl.Exists || !dl.IsDir) continue;
                    foreach (var k in dl.Node.Kids.Keys)
                    {
                        if (!k.EndsWith(".xml", StringComparison.Ordinal)) continue;
                        var z = ParseZone(k.Substring(0, k.Length - 4), m.V.Cat(dir + "/" + k) ?? "");
                        if (z != null) d[z.Name] = z;
                    }
                }
                catch (VfsEx) { }
            }
            // the interface is in the default zone unless a profile says otherwise
            return d;
        }
        public static FwZone ParseZone(string name, string xml)
        {
            var z = new FwZone { Name = name };
            var t = Regex.Match(xml, "<zone[^>]*target=\"([^\"]+)\"");
            if (t.Success) z.Target = t.Groups[1].Value;
            var sh = Regex.Match(xml, "<short>([^<]*)</short>"); if (sh.Success) z.Short = sh.Groups[1].Value;
            foreach (Match mm in Regex.Matches(xml, "<service\\s+name=\"([^\"]+)\"")) z.Services.Add(mm.Groups[1].Value);
            foreach (Match mm in Regex.Matches(xml, "<port\\s+([^>]*)/?>"))
            {
                var pp = Regex.Match(mm.Groups[1].Value, "port=\"([^\"]+)\""); var pr = Regex.Match(mm.Groups[1].Value, "protocol=\"([^\"]+)\"");
                if (pp.Success) z.Ports.Add(pp.Groups[1].Value + "/" + (pr.Success ? pr.Groups[1].Value : "tcp"));
            }
            foreach (Match mm in Regex.Matches(xml, "<source\\s+address=\"([^\"]+)\"")) z.Sources.Add(mm.Groups[1].Value);
            foreach (Match mm in Regex.Matches(xml, "<interface\\s+name=\"([^\"]+)\"")) z.Interfaces.Add(mm.Groups[1].Value);
            foreach (Match mm in Regex.Matches(xml, "<rule>(.*?)</rule>", RegexOptions.Singleline)) z.Rich.Add(System.Net.WebUtility.HtmlDecode(mm.Groups[1].Value));
            z.Masquerade = xml.Contains("<masquerade/>");
            return z;
        }
        public static void SavePermanent(Machine m, FwZone z)
        {
            m.V.EnsureDir("/etc/firewalld/zones", 0x1E8, null);
            m.SysWrite("/etc/firewalld/zones/" + z.Name + ".xml", ZoneXml(z));
        }
    }
}
