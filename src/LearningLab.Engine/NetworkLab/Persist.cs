// Network Lab - saving and opening projects (.netlab, JSON). Routers and switches are saved as their running
// configuration and rebuilt by typing it back in, exactly like the real thing; everything else is saved as JSON.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public static class Persist
    {
        // ================================================================= running-config
        public static string RunningConfig(Device d)
        {
            var ios = d.Ios;
            var s = d.Ip;
            var sb = new StringBuilder();
            Action<string> L = x => sb.Append(x).Append('\n');
            L("!");
            L("version 1.0");
            L("no service timestamps log datetime msec");
            L("no service timestamps debug datetime msec");
            L(ios.PasswordEncryption ? "service password-encryption" : "no service password-encryption");
            L("!");
            L("hostname " + ios.Hostname);
            L("!");
            if (ios.EnableSecret.Length > 0) L("enable secret 5 " + ios.EnableSecret);
            if (ios.EnablePassword.Length > 0) L(ios.PasswordEncryption ? "enable password 7 " + Ios.Type7(ios.EnablePassword) : "enable password " + ios.EnablePassword);
            L("!");
            if (s.Dhcp != null)
            {
                foreach (var r in s.Dhcp.Excluded) L("ip dhcp excluded-address " + Ip.Str(r[0]) + (r[1] != r[0] ? " " + Ip.Str(r[1]) : ""));
                if (s.Dhcp.Excluded.Count > 0) L("!");
                foreach (var p in s.Dhcp.Pools)
                {
                    L("ip dhcp pool " + p.Name);
                    if (p.Mask != 0) L(" network " + Ip.Str(p.Network) + " " + Ip.Str(p.Mask));
                    if (p.Router != 0) L(" default-router " + Ip.Str(p.Router));
                    if (p.Dns.Count > 0) L(" dns-server " + string.Join(" ", p.Dns.Select(x => Ip.Str(x)).ToArray()));
                    if (p.Domain.Length > 0) L(" domain-name " + p.Domain);
                    L("!");
                }
            }
            if (!ios.DomainLookup) L("no ip domain-lookup");
            if (ios.DomainName.Length > 0) L("ip domain-name " + ios.DomainName);
            foreach (var n in ios.NameServers) L("ip name-server " + Ip.Str(n));
            foreach (var h in ios.HostTable) L("ip host " + h.Key + " " + Ip.Str(h.Value));
            L("!");
            if (d.Kind == "switch")
            {
                foreach (var v in d.Bridge.Vlans.Where(x => x.Key > 1 && x.Key < 1002))
                {
                    L("vlan " + v.Key);
                    if (v.Value != "VLAN" + v.Key.ToString("D4")) L(" name " + v.Value);
                    L("!");
                }
                L("spanning-tree mode " + ios.StpMode);
                foreach (var g in d.Bridge.StpPriority.GroupBy(x => x.Value)) L("spanning-tree vlan " + VlanList.Compact(g.Select(x => x.Key)) + " priority " + g.Key);
                L("!");
            }
            foreach (var p in d.Ports.Where(x => !x.IsSerial))
            {
                sb.Append(InterfaceBlock(d, p.Name));
                foreach (var sub in s.Ifs.Where(x => x.IsSub && x.Port == p).OrderBy(x => x.SubNumber)) sb.Append(InterfaceBlock(d, sub.Name));
            }
            foreach (var f in s.Ifs.Where(x => x.IsLoopback).OrderBy(x => x.SubNumber)) sb.Append(InterfaceBlock(d, f.Name));
            foreach (var f in s.Ifs.Where(x => x.IsSvi).OrderBy(x => x.Vlan)) sb.Append(InterfaceBlock(d, f.Name));
            if (s.Rip != null)
            {
                var r = s.Rip;
                L("router rip");
                if (r.VersionSet) L(" version " + r.Version);
                foreach (var p in r.Passive) L(" passive-interface " + p);
                foreach (var n in r.Networks) L(" network " + Ip.Str(n));
                if (r.DefaultOriginate) L(" default-information originate");
                if (!r.AutoSummary) L(" no auto-summary");
                L("!");
            }
            if (d.Kind == "switch" && s.DefaultGateway != 0) L("ip default-gateway " + Ip.Str(s.DefaultGateway));
            foreach (var p in s.Nat.Pools.Values) L("ip nat pool " + p.Name + " " + Ip.Str(p.Start) + " " + Ip.Str(p.End) + " netmask " + Ip.Str(p.Mask));
            foreach (var r in s.Nat.Rules)
            {
                if (r.IsStatic) L("ip nat inside source static " + Ip.Str(r.StaticLocal) + " " + Ip.Str(r.StaticGlobal));
                else L("ip nat inside source list " + r.Acl + (r.Interface.Length > 0 ? " interface " + r.Interface : " pool " + r.Pool) + (r.Overload ? " overload" : ""));
            }
            L("ip classless");
            foreach (var r in s.Static)
                L("ip route " + Ip.Str(r.Net) + " " + Ip.Str(r.Mask) + (r.ExitIf.Length > 0 ? " " + r.ExitIf : "") + (r.NextHop != 0 ? " " + Ip.Str(r.NextHop) : "") + (r.Ad != 1 ? " " + r.Ad : ""));
            L("!");
            foreach (var a in s.Acls.Values.Where(x => !x.Named).OrderBy(x => int.Parse(x.Name)))
                foreach (var l in a.Lines) L("access-list " + a.Name + " " + l.Text());
            foreach (var a in s.Acls.Values.Where(x => x.Named))
            {
                L("ip access-list " + (a.Extended ? "extended " : "standard ") + a.Name);
                foreach (var l in a.Lines) L(" " + l.Text());
            }
            L("!");
            if (ios.Banner.Length > 0) { L("banner motd ^C" + ios.Banner + "^C"); L("!"); }
            if (!ios.Cdp) L("no cdp run");
            L("!");
            L("line con 0");
            WriteLine(L, ios.Con, ios.PasswordEncryption);
            L("!");
            L("line vty 0 4");
            WriteLine(L, ios.Vty, ios.PasswordEncryption);
            L("!");
            L("end");
            return sb.ToString();
        }

        static void WriteLine(Action<string> L, LineCfg c, bool enc)
        {
            if (c.ExecTimeout != 10) L(" exec-timeout " + c.ExecTimeout + " 0");
            if (c.Password.Length > 0) L(enc ? " password 7 " + Ios.Type7(c.Password) : " password " + c.Password);
            if (c.LoggingSync) L(" logging synchronous");
            if (c.Login) L(" login");
            if (c.Transport.Length > 0) L(" transport input " + c.Transport);
        }

        /// <summary>The "interface ..." block of the running configuration (null if there is no such interface).</summary>
        public static string InterfaceBlock(Device d, string name)
        {
            var sb = new StringBuilder();
            Action<string> L = x => sb.Append(x).Append('\n');
            var s = d.Ip;
            var port = d.Ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var f = s.Ifs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (port == null && f == null) return null;
            L("interface " + (port != null ? port.Name : f.Name));
            if (d.Kind == "switch" && port != null)
            {
                if (port.Description.Length > 0) L(" description " + port.Description);
                if (port.Trunk)
                {
                    if (port.NativeVlan != 1) L(" switchport trunk native vlan " + port.NativeVlan);
                    if (port.Allowed != "all") L(" switchport trunk allowed vlan " + port.Allowed);
                    L(" switchport mode trunk");
                }
                else
                {
                    if (port.AccessVlan != 1) L(" switchport access vlan " + port.AccessVlan);
                    if (port.AccessVlan != 1 || port.Portfast) L(" switchport mode access");
                }
                if (port.VoiceVlan != 0) L(" switchport voice vlan " + port.VoiceVlan);
                if (port.Portfast) L(" spanning-tree portfast");
                if (port.Shutdown) L(" shutdown");
                L("!");
                return sb.ToString();
            }
            if (f == null) return null;
            if (f.Description.Length > 0) L(" description " + f.Description);
            if (f.IsSub && f.Vlan != 0) L(" encapsulation dot1Q " + f.Vlan + (f.NativeSub ? " native" : ""));
            if (f.Dhcp) L(" ip address dhcp");
            else if (f.Ip != 0) L(" ip address " + Ip.Str(f.Ip) + " " + Ip.Str(f.Mask));
            else L(" no ip address");
            foreach (var h in f.Helpers) L(" ip helper-address " + Ip.Str(h));
            if (f.AclIn.Length > 0) L(" ip access-group " + f.AclIn + " in");
            if (f.AclOut.Length > 0) L(" ip access-group " + f.AclOut + " out");
            if (f.Nat == 1) L(" ip nat inside");
            if (f.Nat == 2) L(" ip nat outside");
            if (!f.IsSub && !f.IsSvi && !f.IsLoopback) { L(" duplex auto"); L(" speed auto"); }
            if (f.Shutdown) L(" shutdown");
            L("!");
            return sb.ToString();
        }

        /// <summary>Types a configuration back in (the device should be fresh or reset).</summary>
        public static void ApplyConfig(Device d, string text)
        {
            var s = new CliSession(d) { Loading = true, Mode = "config", Started = true };
            // a saved configuration lists "shutdown" for every port that is off; the others are on
            if (text.Contains("interface "))
            {
                foreach (var p in d.Ports) if (!p.IsSerial) p.Shutdown = false;
                foreach (var f in d.Ip.Ifs) f.Shutdown = false;
            }
            try
            {
                foreach (var raw in text.Replace("\r", "").Split('\n'))
                {
                    string line = raw.TrimEnd();
                    string t = line.Trim();
                    if (t.Length == 0 || t == "!" || t == "end" || (t.StartsWith("version ") && !line.StartsWith(" ")) || t.StartsWith("Building") || t.StartsWith("Current configuration")) continue;
                    if (!line.StartsWith(" ")) { s.Mode = "config"; s.If = null; s.Range = null; s.Acl = null; }
                    s.Execute(t);
                }
            }
            finally { d.Ios.Sessions.Remove(s); }
            d.P.Sim.TopologyChanged();
        }

        /// <summary>Back to factory settings: no addresses, no routes, no lists, ports as new.</summary>
        public static void FactoryReset(Device d)
        {
            var s = d.Ip;
            var ios = d.Ios;
            ios.Hostname = d.Kind == "switch" ? "Switch" : "Router";
            ios.EnableSecret = ""; ios.EnablePassword = ""; ios.PasswordEncryption = false; ios.Banner = "";
            ios.DomainLookup = true; ios.DomainName = ""; ios.NameServers.Clear(); ios.HostTable.Clear();
            ios.Con.Password = ""; ios.Con.Login = false; ios.Con.ExecTimeout = 10; ios.Con.LoggingSync = false;
            ios.Vty.Password = ""; ios.Vty.Login = false; ios.Vty.ExecTimeout = 10; ios.Vty.Transport = "";
            ios.Cdp = true; ios.StpMode = "pvst";
            foreach (var f in s.Ifs.Where(x => x.IsSub || x.IsLoopback || (x.IsSvi && x.Vlan != 1)).ToList()) { s.Ifs.Remove(f); if (f.IsSvi) d.Bridge.Svis.Remove(f.Vlan); }
            foreach (var f in s.Ifs)
            {
                if (f.DhcpC != null) f.DhcpC.Stop();
                f.Dhcp = false; f.Ip = 0; f.Mask = 0; f.Description = ""; f.AclIn = ""; f.AclOut = ""; f.Nat = 0; f.Helpers.Clear();
                f.Shutdown = d.Kind == "router" || f.IsSvi;
            }
            foreach (var p in d.Ports)
            {
                p.Description = ""; p.Trunk = false; p.AccessVlan = 1; p.NativeVlan = 1; p.Allowed = "all"; p.Portfast = false; p.VoiceVlan = 0;
                p.Shutdown = d.Kind == "router" && !p.IsSerial;
            }
            s.Static.Clear();
            if (s.Rip != null) { s.Rip = null; s.Udp.Remove(520); }
            s.Acls.Clear();
            s.Nat.Rules.Clear(); s.Nat.Table.Clear(); s.Nat.Pools.Clear();
            if (s.Dhcp != null) { s.Dhcp = null; s.Udp.Remove(67); }
            s.DefaultGateway = 0;
            s.ClearArp();
            if (d.Bridge != null)
            {
                d.Bridge.ClearDynamic();
                foreach (var k in d.Bridge.Vlans.Keys.Where(v => v > 1 && v < 1002).ToList()) d.Bridge.Vlans.Remove(k);
                d.Bridge.StpPriority.Clear();
            }
        }

        public static void Reload(Device d)
        {
            foreach (var s in d.Ios.Sessions.ToList())
            {
                s.Mode = "user"; s.Pending = null; s.Started = false;
                s.Print("\n" + Brand.Os + " Software (Network Lab simulation), Version 1.0\n" + Brand.About + "\n\nRestarting...\n");
            }
            FactoryReset(d);
            d.Ios.BootTime = d.P.Sim.Now;
            if (d.Ios.Startup != null) ApplyConfig(d, d.Ios.Startup);
            else d.P.Sim.TopologyChanged();
            foreach (var s in d.Ios.Sessions.ToList())
            {
                if (d.Ios.Startup == null) s.Print("\n         --- System Configuration Dialog ---\n\nNo saved configuration was found, so the device starts with factory settings.\n");
                s.Print("\nPress RETURN to get started!\n");
                s.Exited = true;
            }
        }

        // ================================================================= projects
        public static string Save(Project p)
        {
            var root = new JObj();
            root["format"] = "netlab";
            root["version"] = 2.0;   // 2: drawings, labels, maps and layers (2026-10-16); version 1 files still open
            root["nextId"] = (double)p.NextId;
            root["lab"] = p.LabId;
            var done = new JObj();
            foreach (var kv in p.LabDone) done[kv.Key] = kv.Value;
            root["labDone"] = done;
            root["layout"] = Physical.SaveLayout(p.Layout);
            var devs = new List<object>();
            foreach (var d in p.Devices) devs.Add(SaveDevice(d));
            root["devices"] = devs;
            var links = new List<object>();
            foreach (var l in p.Links)
            {
                var o = new JObj();
                o["id"] = l.Id;
                o["a"] = l.A.Dev.Id + "/" + l.A.Name;
                o["b"] = l.B.Dev.Id + "/" + l.B.Name;
                o["cable"] = l.Cable;
                o["category"] = l.Category;
                if (l.ManualLength) o["length"] = l.Length;
                links.Add(o);
            }
            root["links"] = links;
            var notes = new List<object>();
            foreach (var n in p.Notes) notes.Add(new JObj().Set("id", n.Id).Set("x", n.X).Set("y", n.Y).Set("text", n.Text).Set("view", n.View));
            root["notes"] = notes;
            var racks = new List<object>();
            foreach (var r in p.Racks) racks.Add(new JObj().Set("id", r.Id).Set("name", r.Name).Set("place", r.Place).Set("px", r.PX).Set("py", r.PY).Set("units", (double)r.Units));
            root["racks"] = racks;
            Drawings.Save(p, root);
            return Json.Write(root);
        }

        static JObj SaveDevice(Device d)
        {
            var o = new JObj();
            o["id"] = d.Id; o["kind"] = d.Kind; o["name"] = d.Name;
            if (d.Os.Length > 0) o["os"] = d.Os;
            o["x"] = Math.Round(d.X, 1); o["y"] = Math.Round(d.Y, 1);
            o["place"] = d.Place; o["px"] = Math.Round(d.PX, 2); o["py"] = Math.Round(d.PY, 2);
            if (d.Rack.Length > 0) { o["rack"] = d.Rack; o["rackU"] = (double)d.RackU; }
            if (!d.PoweredOn) o["power"] = false;
            var ports = new List<object>();
            foreach (var pt in d.Ports)
            {
                var po = new JObj().Set("name", pt.Name).Set("mac", pt.Mac);
                if (pt.Sfp.Length > 0) po["sfp"] = pt.Sfp;
                if (!d.IsIos && pt.Shutdown) po["shutdown"] = true;
                ports.Add(po);
            }
            o["ports"] = ports;
            if (d.Bridge != null) o["bridgeMac"] = d.Bridge.Mac0;
            if (d.Bridge != null) { var svm = new JObj(); foreach (var kv in d.Bridge.Svis) svm[kv.Key.ToString()] = kv.Value.Mac; o["sviMacs"] = svm; }
            if (d.Ios != null)
            {
                o["config"] = RunningConfig(d);
                if (d.Ios.Startup != null) o["startup"] = d.Ios.Startup;
            }
            if (d.Host != null) o["host"] = SaveHost(d);
            if (d.Home != null)
            {
                var h = d.Home;
                o["home"] = new JObj().Set("wanMode", h.WanMode).Set("wanIp", h.WanIp).Set("wanMask", h.WanMask).Set("wanGw", h.WanGw).Set("wanDns", h.WanDns)
                    .Set("lanIp", h.LanIp).Set("lanMask", h.LanMask).Set("dhcpOn", h.DhcpOn).Set("dhcpStart", h.DhcpStart).Set("dhcpMax", (double)h.DhcpMax).Set("admin", h.AdminPassword);
            }
            if (d.Ap != null) o["ap"] = new JObj().Set("ssid", d.Ap.Ssid).Set("security", d.Ap.Security).Set("key", d.Ap.Key).Set("channel", (double)d.Ap.Channel).Set("enabled", d.Ap.Enabled);
            return o;
        }

        static JObj SaveHost(Device d)
        {
            var h = d.Host;
            var o = new JObj();
            o["hostname"] = h.ComputerName;
            var ads = new List<object>();
            foreach (var f in h.Adapters)
            {
                var a = new JObj().Set("name", f.Name).Set("dhcp", f.Dhcp);
                if (!f.Dhcp)
                {
                    a["ip"] = f.Ip == 0 ? "" : Ip.Str(f.Ip); a["mask"] = f.Mask == 0 ? "" : Ip.Str(f.Mask);
                    a["gw"] = f.Gateway == 0 ? "" : Ip.Str(f.Gateway);
                    a["dns"] = f.Dns.Select(x => (object)Ip.Str(x)).ToList();
                }
                ads.Add(a);
            }
            o["adapters"] = ads;
            var wifi = new List<object>();
            foreach (var w in h.Wifi) wifi.Add(new JObj().Set("port", w.Port.Name).Set("ssid", w.Ssid).Set("key", w.Key));
            o["wifi"] = wifi;
            o["http"] = h.HttpOn;
            var pages = new JObj(); foreach (var kv in h.Pages) pages[kv.Key] = kv.Value; o["pages"] = pages;
            o["smb"] = h.SmbOn;
            o["shares"] = h.Shares.Select(s => (object)new JObj().Set("name", s.Name).Set("files", s.Files.Select(x => (object)x).ToList())).ToList();
            o["print"] = h.PrintOn;
            o["printed"] = h.Printed.Select(x => (object)x).ToList();
            o["ssh"] = h.SshOn; o["user"] = h.User; o["password"] = h.Password;
            o["winFirewall"] = h.WinFirewall; o["fwSharing"] = h.FwSharing; o["fwWeb"] = h.FwWeb;
            o["ufw"] = h.Ufw;
            o["ufwRules"] = h.UfwRules.Select(r => (object)new JObj().Set("port", (double)r.Port).Set("proto", r.Proto).Set("allow", r.Allow)).ToList();
            var files = new JObj(); foreach (var kv in h.Files) files[kv.Key] = kv.Value; o["files"] = files;
            o["printers"] = h.Printers.Select(x => (object)x.Select(y => (object)y).ToList()).ToList();
            o["state"] = h.State;
            var ips = d.Ip;
            if (ips.DnsSrv != null)
            {
                var rec = new JObj(); foreach (var kv in ips.DnsSrv.Records) rec[kv.Key] = Ip.Str(kv.Value);
                o["dns"] = new JObj().Set("on", ips.DnsSrv.Enabled).Set("records", rec);
            }
            if (ips.Dhcp != null)
            {
                var pools = new List<object>();
                foreach (var p in ips.Dhcp.Pools)
                    pools.Add(new JObj().Set("name", p.Name).Set("network", Ip.Str(p.Network)).Set("mask", Ip.Str(p.Mask)).Set("router", Ip.Str(p.Router))
                        .Set("dns", p.Dns.Select(x => (object)Ip.Str(x)).ToList()).Set("start", Ip.Str(p.Start)).Set("max", (double)p.Max).Set("on", p.Enabled));
                o["dhcp"] = new JObj().Set("on", ips.Dhcp.Enabled).Set("pools", pools);
            }
            return o;
        }

        public static Project Load(string json)
        {
            var root = Json.ParseObj(json);
            if (root == null || root.Str("format") != "netlab") throw new FormatException("This isn't a Network Lab project file.");
            var p = new Project(1);
            p.LabId = root.Str("lab");
            var done = root.Obj("labDone");
            if (done != null) foreach (var kv in done) p.LabDone[kv.Key] = kv.Value is bool && (bool)kv.Value;
            var lay = root.Obj("layout");
            if (lay != null) p.Layout = Physical.LoadLayout(lay);
            var byId = new Dictionary<string, Device>();
            foreach (JObj o in root.Arr("devices"))
            {
                var d = new Device { P = p, Kind = o.Str("kind", "pc"), Id = o.Str("id"), Name = o.Str("name"), Os = o.Str("os"), X = o.Num("x", 0), Y = o.Num("y", 0) };
                Catalog.Build(d, p.Rng);
                d.Place = o.Str("place"); d.PX = o.Num("px", -1); d.PY = o.Num("py", -1);
                d.Rack = o.Str("rack"); d.RackU = o.Int("rackU", 0);
                d.PoweredOn = o.Bool("power", true);
                var ports = o.Arr("ports");
                foreach (JObj po in ports)
                {
                    var pt = d.Ports.FirstOrDefault(x => x.Name == po.Str("name"));
                    if (pt == null) continue;
                    pt.Mac = po.Str("mac", pt.Mac);
                    if (pt.L3 != null) pt.L3.Mac = pt.Mac;
                    if (po.ContainsKey("sfp")) pt.Sfp = po.Str("sfp");
                    if (po.Bool("shutdown", false)) pt.Shutdown = true;
                }
                if (d.Bridge != null && o.ContainsKey("bridgeMac")) d.Bridge.BaseMac = o.Str("bridgeMac");
                p.Devices.Add(d);
                byId[d.Id] = d;
                if (d.Bridge != null)
                {
                    var svm = o.Obj("sviMacs");
                    if (svm != null) foreach (var kv in svm) { IpIf f; int v; if (int.TryParse(kv.Key, out v) && d.Bridge.Svis.TryGetValue(v, out f)) f.Mac = Convert.ToString(kv.Value); }
                }
            }
            p.NextId = Math.Max(root.Int("nextId", 1), p.Devices.Count + 1);
            foreach (JObj o in root.Arr("links"))
            {
                Port a = PortRef(byId, o.Str("a")), b = PortRef(byId, o.Str("b"));
                if (a == null || b == null) continue;
                var l = new Link { A = a, B = b, Cable = o.Str("cable", "straight"), Category = o.Str("category", "cat6"), Id = o.Str("id") };
                if (o.ContainsKey("length")) { l.Length = o.Num("length", 2); l.ManualLength = true; }
                a.Links.Add(l); b.Links.Add(l);
                p.Links.Add(l);
            }
            foreach (JObj o in root.Arr("notes")) p.Notes.Add(new Note { Id = o.Str("id"), X = o.Num("x", 0), Y = o.Num("y", 0), Text = o.Str("text"), View = o.Str("view", "logical") });
            foreach (JObj o in root.Arr("racks")) p.Racks.Add(new Rack { Id = o.Str("id"), Name = o.Str("name"), Place = o.Str("place"), PX = o.Num("px", 0), PY = o.Num("py", 0), Units = o.Int("units", 24) });
            Drawings.Load(p, root);
            // configurations after the cables, so interfaces come up as they are configured
            p.Sim.TopologyChanged();
            foreach (JObj o in root.Arr("devices"))
            {
                Device d;
                if (!byId.TryGetValue(o.Str("id"), out d)) continue;
                if (d.Ios != null)
                {
                    ApplyConfig(d, o.Str("config"));
                    if (o.ContainsKey("startup")) d.Ios.Startup = o.Str("startup");
                }
                var h = o.Obj("home");
                if (h != null && d.Home != null)
                {
                    var c = d.Home;
                    c.WanMode = h.Str("wanMode", "dhcp"); c.WanIp = h.Str("wanIp"); c.WanMask = h.Str("wanMask"); c.WanGw = h.Str("wanGw"); c.WanDns = h.Str("wanDns");
                    c.LanIp = h.Str("lanIp", c.LanIp); c.LanMask = h.Str("lanMask", c.LanMask); c.DhcpOn = h.Bool("dhcpOn", true); c.DhcpStart = h.Str("dhcpStart", c.DhcpStart);
                    c.DhcpMax = h.Int("dhcpMax", 50); c.AdminPassword = h.Str("admin", "admin");
                }
                var ap = o.Obj("ap");
                if (ap != null && d.Ap != null) { d.Ap.Ssid = ap.Str("ssid"); d.Ap.Security = ap.Str("security", "open"); d.Ap.Key = ap.Str("key"); d.Ap.Channel = ap.Int("channel", 6); d.Ap.Enabled = ap.Bool("enabled", true); }
                if (d.Home != null) HomeRouter.Apply(d);
                var ho = o.Obj("host");
                if (ho != null && d.Host != null) LoadHost(d, ho);
            }
            p.Sim.TopologyChanged();
            foreach (var d in p.Devices) p.Sim.DeviceAdded(d);
            p.FilePath = "";
            return p;
        }

        static Port PortRef(Dictionary<string, Device> byId, string r)
        {
            int slash = r.IndexOf('/');
            if (slash < 0) return null;
            Device d;
            if (!byId.TryGetValue(r.Substring(0, slash), out d)) return null;
            string n = r.Substring(slash + 1);
            return d.Ports.FirstOrDefault(x => x.Name == n);
        }

        static void LoadHost(Device d, JObj o)
        {
            var h = d.Host;
            h.ComputerName = o.Str("hostname");
            foreach (JObj a in o.Arr("adapters"))
            {
                var f = h.Adapters.FirstOrDefault(x => x.Name == a.Str("name"));
                if (f == null) continue;
                f.Dhcp = a.Bool("dhcp", false);
                if (!f.Dhcp)
                {
                    uint ip, mask, gw;
                    if (Ip.TryParse(a.Str("ip"), out ip) && Ip.TryParseMask(a.Str("mask"), out mask)) { f.Ip = ip; f.Mask = mask; }
                    if (Ip.TryParse(a.Str("gw"), out gw)) f.Gateway = gw;
                    f.Dns.Clear();
                    foreach (var x in a.Arr("dns")) { uint v; if (Ip.TryParse(Convert.ToString(x), out v)) f.Dns.Add(v); }
                }
            }
            foreach (JObj w in o.Arr("wifi"))
            {
                var wc = h.Wifi.FirstOrDefault(x => x.Port.Name == w.Str("port"));
                if (wc != null) { wc.Ssid = w.Str("ssid"); wc.Key = w.Str("key"); }
            }
            h.HttpOn = o.Bool("http", false);
            h.Pages.Clear();
            var pages = o.Obj("pages"); if (pages != null) foreach (var kv in pages) h.Pages[kv.Key] = Convert.ToString(kv.Value);
            h.SmbOn = o.Bool("smb", false);
            h.Shares.Clear();
            foreach (JObj s in o.Arr("shares")) h.Shares.Add(new SharedFolder { Name = s.Str("name"), Files = s.Arr("files").Select(x => Convert.ToString(x)).ToList() });
            h.PrintOn = o.Bool("print", false);
            h.Printed.Clear(); foreach (var x in o.Arr("printed")) h.Printed.Add(Convert.ToString(x));
            h.SshOn = o.Bool("ssh", false); h.User = o.Str("user", "student"); h.Password = o.Str("password", "netlab");
            h.WinFirewall = o.Bool("winFirewall", false); h.FwSharing = o.Bool("fwSharing", false); h.FwWeb = o.Bool("fwWeb", false);
            h.Ufw = o.Bool("ufw", false);
            h.UfwRules.Clear(); foreach (JObj r in o.Arr("ufwRules")) h.UfwRules.Add(new FwRule { Port = r.Int("port", 0), Proto = r.Str("proto", "tcp"), Allow = r.Bool("allow", true) });
            h.Files.Clear();
            var files = o.Obj("files"); if (files != null) foreach (var kv in files) h.Files[kv.Key] = Convert.ToString(kv.Value);
            h.Printers.Clear(); foreach (var x in o.Arr("printers")) { var l = x as List<object>; if (l != null) h.Printers.Add(l.Select(y => Convert.ToString(y)).ToArray()); }
            var st = o.Obj("state"); if (st != null) h.State = st;
            var s2 = d.Ip;
            var dns = o.Obj("dns");
            if (dns != null)
            {
                if (s2.DnsSrv == null) s2.DnsSrv = new DnsServer(s2);
                s2.DnsSrv.Enabled = dns.Bool("on", true);
                s2.DnsSrv.Records.Clear();
                var rec = dns.Obj("records"); if (rec != null) foreach (var kv in rec) { uint v; if (Ip.TryParse(Convert.ToString(kv.Value), out v)) s2.DnsSrv.Records[kv.Key] = v; }
            }
            var dh = o.Obj("dhcp");
            if (dh != null)
            {
                if (s2.Dhcp == null) s2.Dhcp = new DhcpServer(s2);
                s2.Dhcp.Enabled = dh.Bool("on", true);
                s2.Dhcp.Pools.Clear();
                foreach (JObj po in dh.Arr("pools"))
                {
                    var pool = new DhcpPool { Name = po.Str("name"), Max = po.Int("max", 0), Enabled = po.Bool("on", true) };
                    Ip.TryParse(po.Str("network"), out pool.Network); Ip.TryParse(po.Str("mask"), out pool.Mask); Ip.TryParse(po.Str("router"), out pool.Router); Ip.TryParse(po.Str("start"), out pool.Start);
                    foreach (var x in po.Arr("dns")) { uint v; if (Ip.TryParse(Convert.ToString(x), out v)) pool.Dns.Add(v); }
                    s2.Dhcp.Pools.Add(pool);
                }
            }
            Personalities.Get(d.Os).Loaded(d);
            d.P.Sim.TopologyChanged();

        }

        /// <summary>A copy of the project with its own simulation (lab checks run in it, out of sight).</summary>
        public static Project Clone(Project p) { return Load(Save(p)); }
    }
}
