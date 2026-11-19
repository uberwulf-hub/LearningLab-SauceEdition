// Network Lab - the router and switch commands, mode by mode.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public static class CliCommands
    {
        static readonly Dictionary<string, CliTree> trees = new Dictionary<string, CliTree>();

        public static CliTree Tree(string mode, Device d)
        {
            string kind = d.Kind == "switch" ? "switch" : "router";
            string key = mode + "|" + kind;
            CliTree t;
            if (trees.TryGetValue(key, out t)) return t;
            Helps();
            t = new CliTree();
            bool sw = kind == "switch";
            switch (mode)
            {
                case "user": Exec(t, false, sw); break;
                case "priv": Exec(t, true, sw); break;
                case "config": Config(t, sw); break;
                case "config-if": case "config-subif": case "config-if-range": Interface(t, sw, mode); break;
                case "config-router": Router(t); break;
                case "config-line": Line(t); break;
                case "config-vlan": VlanMode(t); break;
                case "dhcp-config": DhcpMode(t); break;
                case "config-std-nacl": NaclMode(t, false); break;
                case "config-ext-nacl": NaclMode(t, true); break;
            }
            if (mode != "user" && mode != "priv")
            {
                t.Add("exit", c => Exit(c.S));
                t.Add("end", c => c.S.End());
                t.Add("do <line>", c => { });
            }
            trees[key] = t;
            return t;
        }

        static void Exit(CliSession s)
        {
            switch (s.Mode)
            {
                case "config": s.Mode = "priv"; break;
                case "priv": case "user": s.Exited = true; s.Mode = "user"; s.Print("\n" + s.D.Ios.Hostname + " con0 is now available\n\nPress RETURN to get started.\n"); break;
                default: s.Mode = "config"; s.If = null; s.Range = null; s.Acl = null; break;
            }
        }

        static bool helpsDone;
        static void Helps()
        {
            if (helpsDone) return;
            helpsDone = true;
            var h = CliTree.Helps;
            Action<string, string> a = (k, v) => h[k] = v;
            a("enable", "Turn on privileged commands"); a("disable", "Turn off privileged commands"); a("exit", "Exit from the current mode");
            a("logout", "Exit from the EXEC"); a("ping", "Send echo messages"); a("traceroute", "Trace route to destination");
            a("show", "Show running system information"); a("configure", "Enter configuration mode"); a("terminal", "Configure using the terminal (or set terminal options)");
            a("copy", "Copy from one file to another"); a("write", "Write running configuration to memory"); a("reload", "Halt and perform a cold restart");
            a("erase", "Erase a filesystem"); a("clear", "Reset functions"); a("end", "Exit configuration mode"); a("do", "To run exec commands in config mode");
            a("hostname", "Set system's network name"); a("interface", "Select an interface to configure"); a("ip", "Global IP configuration subcommands");
            a("router", "Enable a routing process"); a("vlan", "VLAN commands"); a("access-list", "Add an access list entry"); a("line", "Configure a terminal line");
            a("banner", "Define a login banner"); a("service", "Modify use of network based services"); a("spanning-tree", "Spanning Tree Subsystem");
            a("no", "Negate a command or set its defaults"); a("description", "Interface specific description"); a("shutdown", "Shutdown the selected interface");
            a("switchport", "Set switching mode characteristics"); a("encapsulation", "Set encapsulation type for an interface"); a("speed", "Configure speed operation.");
            a("duplex", "Configure duplex operation."); a("address", "Set the IP address of an interface"); a("route", "Establish static routes");
            a("nat", "NAT configuration commands"); a("access-group", "Specify access control for packets"); a("helper-address", "Specify a destination address for UDP broadcasts");
            a("dhcp", "Configure DHCP server and relay parameters"); a("domain-lookup", "Enable IP Domain Name System hostname translation"); a("domain-name", "Define the default domain name");
            a("name-server", "Specify address of name server to use"); a("host", "Add an entry to the ip hostname table"); a("default-gateway", "Specify default gateway (if not routing IP)");
            a("running-config", "Current operating configuration"); a("startup-config", "Contents of startup configuration"); a("version", "System hardware and software status");
            a("interfaces", "Interface status and configuration"); a("arp", "ARP table"); a("mac", "MAC configuration"); a("mac-address-table", "MAC forwarding table");
            a("cdp", "CDP information"); a("access-lists", "List access lists"); a("protocols", "IP routing protocol process parameters and statistics");
            a("brief", "Brief summary"); a("network", "Enable routing on an IP network"); a("redistribute", "Redistribute information from another routing protocol"); a("timers", "Adjust routing timers");
            a("auto-summary", "Enable automatic network number summarization"); a("passive-interface", "Suppress routing updates on an interface");
            a("default-information", "Control distribution of default information"); a("password", "Set a password"); a("login", "Enable password checking");
            a("exec-timeout", "Set the EXEC timeout"); a("logging", "Modify message logging facilities"); a("name", "Ascii name of the VLAN");
            a("default-router", "Default routers"); a("dns-server", "DNS servers"); a("lease", "Address lease time"); a("permit", "Specify packets to forward");
            a("deny", "Specify packets to reject"); a("remark", "Access list entry comment"); a("secret", "Assign the privileged level secret"); a("motd", "Set Message of the Day banner");
            a("password-encryption", "Encrypt system passwords"); a("pool", "Configure DHCP address pools (or a NAT pool)"); a("excluded-address", "Prevent DHCP from assigning certain addresses");
            a("inside", "Inside interface for address translation"); a("outside", "Outside interface for address translation"); a("source", "Source address translation");
            a("list", "Specify access list describing local addresses"); a("overload", "Overload an address translation"); a("static", "Specify static local->global mapping");
            a("translations", "Translation entries"); a("statistics", "Translation statistics"); a("binding", "DHCP address bindings"); a("route ", "IP routing table");
            a("mode", "Set trunking mode of the interface (or the spanning tree mode)"); a("access", "Set access mode characteristics of the interface"); a("trunk", "Set trunking characteristics of the interface");
            a("native", "Set trunking native characteristics when interface is in trunking mode"); a("allowed", "Set allowed VLAN characteristics when interface is in trunking mode");
            a("portfast", "Spanning tree portfast options"); a("priority", "Set the bridge priority for the spanning tree"); a("console", "Primary terminal line"); a("vty", "Virtual terminal");
            a("dot1q", "IEEE 802.1Q Virtual LAN"); a("standard", "Standard Access List"); a("extended", "Extended Access List"); a("in", "inbound packets"); a("out", "outbound packets");
            a("memory", "Write to NV memory"); a("clock", "Display the system clock"); a("history", "Display the session command history"); a("spanning-tree ", "Spanning tree topology");
            a("neighbors", "CDP neighbor entries"); a("dynamic", "Dynamic entries"); a("translation", "Clear dynamic translation"); a("arp-cache", "Clear the entire ARP cache");
            a("counters", "Clear counters"); a("routing", "Enable IP routing"); a("nonegotiate", "Device will not engage in negotiation protocol on this interface");
            a("range", "interface range command"); a("database", "IP routing database"); a("rip", "Routing Information Protocol (RIP)"); a("summary", "Summary of all VLANs");
            a("netmask", "Specify the network mask"); a("prefix-length", "Specify the prefix length"); a("information", "Information"); a("originate", "Distribute a default route");
            a("length", "Set number of lines on a screen"); a("users", "Display information about terminal lines"); a("flash:", "display information about flash: file system");
            a("detail", "Show detailed information"); a("add", "add VLANs to the current list"); a("remove", "remove VLANs from the current list"); a("all", "all VLANs"); a("none", "no VLANs");
            a("vlan ", "VLAN status");
        }

        // ================================================================= EXEC (> and #)
        static void Exec(CliTree t, bool priv, bool sw)
        {
            if (!priv)
            {
                t.Add("enable", c =>
                {
                    var s = c.S;
                    if (!c.Ios.NeedsEnablePassword) { s.Mode = "priv"; return; }
                    s.PendingPrompt = "Password: ";
                    s.PendingSecret = true;
                    int tries = 0;
                    s.Pending = typed =>
                    {
                        if (c.Ios.EnableOk(typed)) { s.Pending = null; s.PendingSecret = false; s.Mode = "priv"; return true; }
                        if (++tries >= 3) { s.Pending = null; s.PendingSecret = false; s.Print("% Bad secrets\n"); }
                        return true;
                    };
                });
            }
            else
            {
                t.Add("disable", c => c.S.Mode = "user");
                t.Add("enable", c => { });
                t.Add("configure terminal", c => { c.S.Mode = "config"; c.Out("Enter configuration commands, one per line.  End with CNTL/Z."); });
                t.Add("configure", c => { c.S.Mode = "config"; c.Out("Enter configuration commands, one per line.  End with CNTL/Z."); }).Hidden = false;
                t.Add("copy running-config startup-config", c => CopyRunStart(c));
                t.Add("copy startup-config running-config", c =>
                {
                    if (c.Ios.Startup == null) { c.Out("%% Non-volatile configuration memory is not present"); return; }
                    Persist.ApplyConfig(c.D, c.Ios.Startup);
                    c.Out(c.Ios.Startup.Length + " bytes copied in 0.1 secs");
                });
                t.Add("write", c => { c.Out("Building configuration...\n[OK]"); c.Ios.Startup = Persist.RunningConfig(c.D); });
                t.Add("write memory", c => { c.Out("Building configuration...\n[OK]"); c.Ios.Startup = Persist.RunningConfig(c.D); });
                t.Add("write erase", c => EraseStartup(c));
                t.Add("erase startup-config", c => EraseStartup(c));
                t.Add("reload", c =>
                {
                    var s = c.S;
                    if (s.Loading) return;
                    s.Ask("Proceed with reload? [confirm]", ans =>
                    {
                        if (ans.Trim().Length > 0 && !ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) return true;
                        Persist.Reload(c.D);
                        return true;
                    });
                });
                t.Add("clear mac address-table dynamic", c => { if (c.D.Bridge != null) c.D.Bridge.ClearDynamic(); });
                t.Add("clear mac-address-table dynamic", c => { if (c.D.Bridge != null) c.D.Bridge.ClearDynamic(); });
                t.Add("clear arp-cache", c => c.IpS.ClearArp());
                t.Add("clear ip nat translation *", c => c.IpS.Nat.Table.Clear(), null, null, null, null, "Delete all dynamic translations");
                t.Add("clear ip dhcp binding *", c => { if (c.IpS.Dhcp != null) c.IpS.Dhcp.ClearBindings(); });
                t.Add("clear access-list counters", c => { foreach (var a in c.IpS.Acls.Values) foreach (var l in a.Lines) l.Matches = 0; });
                t.Add("show running-config", c => c.Out(Persist.RunningConfig(c.D)));
                t.Add("show running-config interface <iface>", c => c.Out(CliShow.RunInterface(c)));
                t.Add("show startup-config", c => c.Out(c.Ios.Startup ?? "startup-config is not present"));
            }
            t.Add("exit", c => Exit(c.S));
            t.Add("logout", c => Exit(c.S));
            t.Add("ping <word>", c => CliShow.Ping(c, c.Arg(0)));
            t.Add("ping", c => c.Out("% Incomplete command. (type: ping <address or name>)"));
            t.Add("traceroute <word>", c => CliShow.Trace(c, c.Arg(0)));
            t.Add("terminal length <num:0-512>", c => { });
            t.Add("terminal monitor", c => { });
            t.Add("show version", c => c.Out(CliShow.Version(c.D)));
            t.Add("show clock", c => c.Out(CliShow.Clock(c.D)));
            t.Add("show history", c => c.Out(string.Join("\n", c.S.History.Select(x => "  " + x).ToArray())));
            t.Add("show ip interface brief", c => c.Out(CliShow.IpIntBrief(c.D)));
            t.Add("show ip interface", c => c.Out(CliShow.IpIntBrief(c.D)));
            t.Add("show interfaces", c => c.Out(CliShow.Interfaces(c.D, null)));
            t.Add("show interfaces <iface>", c => c.Out(CliShow.Interfaces(c.D, c.Arg(0))));
            t.Add("show arp", c => c.Out(CliShow.Arp(c.D)));
            t.Add("show ip arp", c => c.Out(CliShow.Arp(c.D)));
            t.Add("show cdp neighbors", c => c.Out(CliShow.Cdp(c.D, false)));
            t.Add("show cdp neighbors detail", c => c.Out(CliShow.Cdp(c.D, true)));
            t.Add("show access-lists", c => c.Out(CliShow.AccessLists(c.D, null)));
            t.Add("show access-lists <word>", c => c.Out(CliShow.AccessLists(c.D, c.Arg(0))));
            t.Add("show ip access-lists", c => c.Out(CliShow.AccessLists(c.D, null)));
            t.Add("show flash:", c => c.Out(CliShow.Flash(c.D)));
            t.Add("show users", c => c.Out("    Line       User       Host(s)              Idle       Location\n*  0 con 0                idle                 00:00:00"));
            if (sw)
            {
                t.Add("show vlan", c => c.Out(CliShow.Vlans(c.D, false)));
                t.Add("show vlan brief", c => c.Out(CliShow.Vlans(c.D, true)));
                t.Add("show mac address-table", c => c.Out(CliShow.MacTable(c.D)));
                t.Add("show mac-address-table", c => c.Out(CliShow.MacTable(c.D)));
                t.Add("show interfaces trunk", c => c.Out(CliShow.Trunks(c.D)));
                t.Add("show interfaces status", c => c.Out(CliShow.IfStatus(c.D)));
                t.Add("show spanning-tree", c => c.Out(CliShow.Stp(c.D)));
            }
            t.Add("show ip route", c => c.Out(CliShow.Routes(c.D)));
            if (!sw)
            {
                t.Add("show ip protocols", c => c.Out(CliShow.Protocols(c.D)));
                t.Add("show ip rip database", c => c.Out(CliShow.RipDb(c.D)));
                t.Add("show ip dhcp binding", c => c.Out(CliShow.DhcpBindings(c.D)));
                t.Add("show ip dhcp pool", c => c.Out(CliShow.DhcpPools(c.D)));
                t.Add("show ip nat translations", c => c.Out(CliShow.NatTable(c.D)));
                t.Add("show ip nat statistics", c => c.Out(CliShow.NatStats(c.D)));
            }
        }

        static void CopyRunStart(CliCtx c)
        {
            var s = c.S;
            if (s.Loading) { c.Ios.Startup = Persist.RunningConfig(c.D); return; }
            s.Ask("Destination filename [startup-config]? ", ans =>
            {
                c.Ios.Startup = Persist.RunningConfig(c.D);
                s.Print("Building configuration...\n[OK]");
                return true;
            });
        }

        static void EraseStartup(CliCtx c)
        {
            var s = c.S;
            if (s.Loading) { c.Ios.Startup = null; return; }
            s.Ask("Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]", ans =>
            {
                if (ans.Trim().Length > 0 && !ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) return true;
                c.Ios.Startup = null;
                s.Print("[OK]\nErase of nvram: complete");
                return true;
            });
        }

        // ================================================================= (config)#
        static void Config(CliTree t, bool sw)
        {
            t.Add("hostname <word>", c =>
            {
                if (c.No) { c.Ios.Hostname = c.D.Kind == "switch" ? "Switch" : "Router"; return; }
                string h = c.Arg(0);
                if (!char.IsLetter(h[0])) { c.Out("% Hostname contains one or more illegal characters."); return; }
                c.Ios.Hostname = h.Length > 63 ? h.Substring(0, 63) : h;
            });
            t.Add("enable secret <word>", c => { if (c.No) c.Ios.EnableSecret = ""; else c.Ios.EnableSecret = Ios.Secret(c.Arg(0), null); }, "Modify enable password parameters");
            t.Add("enable secret 5 <word>", c => { if (c.No) c.Ios.EnableSecret = ""; else c.Ios.EnableSecret = c.Arg(0); }, null, null, "Specifies an ENCRYPTED secret will follow");
            t.Add("enable secret 0 <word>", c => { if (c.No) c.Ios.EnableSecret = ""; else c.Ios.EnableSecret = Ios.Secret(c.Arg(0), null); }, null, null, "Specifies an UNENCRYPTED password will follow");
            t.Add("enable password <word>", c => { c.Ios.EnablePassword = c.No ? "" : c.Arg(0); }, null, "Assign the privileged level password");
            t.Add("enable password 7 <word>", c => { c.Ios.EnablePassword = c.No ? "" : (Ios.FromType7(c.Arg(0)) ?? ""); }, null, null, "Specifies a HIDDEN password will follow");
            t.Add("enable", c => c.Out("% Incomplete command."));
            t.Add("service password-encryption", c => c.Ios.PasswordEncryption = !c.No);
            t.Add("service timestamps <line>", c => { });
            t.Add("banner motd <line>", c =>
            {
                if (c.No) { c.Ios.Banner = ""; return; }
                string s = c.Arg(0).Trim();
                if (s.Length >= 2) { char d = s[0]; int e = s.IndexOf(d, 1); c.Ios.Banner = e > 0 ? s.Substring(1, e - 1) : s.Substring(1); }
                else c.Ios.Banner = s;
                c.Ios.Banner = c.Ios.Banner.Replace("^C", "");
            });
            t.Add("ip domain-lookup", c => c.Ios.DomainLookup = !c.No);
            t.Add("ip domain lookup", c => c.Ios.DomainLookup = !c.No);
            t.Add("ip domain-name <word>", c => c.Ios.DomainName = c.No ? "" : c.Arg(0));
            t.Add("ip name-server <ip>", c => { uint a = Ip.Parse(c.Arg(0)); c.Ios.NameServers.Remove(a); if (!c.No) c.Ios.NameServers.Add(a); });
            t.Add("ip host <word> <ip>", c => { if (c.No) c.Ios.HostTable.Remove(c.Arg(0)); else c.Ios.HostTable[c.Arg(0)] = Ip.Parse(c.Arg(1)); });
            t.Add("ip route <ip> <mask> <ip>", c => StaticRoute(c, c.Arg(2), "", 1));
            t.Add("ip route <ip> <mask> <ip> <num:1-255>", c => StaticRoute(c, c.Arg(2), "", int.Parse(c.Arg(3))));
            t.Add("ip route <ip> <mask> <iface>", c => StaticRoute(c, "", c.Arg(2), 1));
            t.Add("ip route <ip> <mask> <iface> <ip>", c => StaticRoute(c, c.Arg(3), c.Arg(2), 1));
            t.Add("ip classless", c => { });
            t.Add("ip routing", c => { if (sw && !c.No) c.Out("% This switch model can't route between VLANs: use a router (router-on-a-stick)."); });
            t.Add("ip cef", c => { });
            if (sw) t.Add("ip default-gateway <ip>", c => { c.IpS.DefaultGateway = c.No ? 0 : Ip.Parse(c.Arg(0)); });
            t.Add("interface <iface>", c => EnterInterface(c, c.Arg(0)));
            t.Add("interface range <line>", c => EnterRange(c, c.Arg(0)));
            if (sw)
            {
                t.Add("vlan <num:1-4094>", c => EnterVlan(c, int.Parse(c.Arg(0))));
                t.Add("vlan <vlanlist>", c => EnterVlanList(c, c.Arg(0))).Hidden = true;
                t.Add("spanning-tree vlan <vlanlist> priority <num:0-61440>", c => StpPriority(c, c.Arg(0), c.No ? 32768 : int.Parse(c.Arg(1))));
                t.Add("spanning-tree vlan <vlanlist> root primary", c => StpPriority(c, c.Arg(0), c.No ? 32768 : 24576));
                t.Add("spanning-tree vlan <vlanlist> root secondary", c => StpPriority(c, c.Arg(0), c.No ? 32768 : 28672));
                t.Add("spanning-tree mode pvst", c => c.Ios.StpMode = "pvst");
                t.Add("spanning-tree mode rapid-pvst", c => c.Ios.StpMode = c.No ? "pvst" : "rapid-pvst");
                t.Add("spanning-tree portfast default", c => { });
            }
            else
            {
                t.Add("router rip", c =>
                {
                    if (c.No) { c.IpS.Rip = null; c.IpS.Udp.Remove(520); return; }
                    if (c.IpS.Rip == null) c.IpS.Rip = new Rip(c.IpS);
                    c.S.Mode = "config-router";
                });
                t.Add("router ospf <num:1-65535>", c => c.Out("% OSPF isn't part of this simulator yet: use RIP (router rip) or static routes."));
                t.Add("ip dhcp pool <word>", c =>
                {
                    if (c.IpS.Dhcp == null) c.IpS.Dhcp = new DhcpServer(c.IpS);
                    if (c.No) { var p = c.IpS.Dhcp.Pool(c.Arg(0), false); if (p != null) c.IpS.Dhcp.Pools.Remove(p); return; }
                    c.IpS.Dhcp.Pool(c.Arg(0), true);
                    c.S.Pool = c.Arg(0);
                    c.S.Mode = "dhcp-config";
                });
                t.Add("ip dhcp excluded-address <ip>", c => Excluded(c, c.Arg(0), c.Arg(0)));
                t.Add("ip dhcp excluded-address <ip> <ip>", c => Excluded(c, c.Arg(0), c.Arg(1)));
                t.Add("ip nat inside source list <word> interface <iface> overload", c => NatList(c, c.Arg(0), c.Arg(1), "", true));
                t.Add("ip nat inside source list <word> interface <iface>", c => NatList(c, c.Arg(0), c.Arg(1), "", true));
                t.Add("ip nat inside source list <word> pool <word>", c => NatList(c, c.Arg(0), "", c.Arg(1), false));
                t.Add("ip nat inside source list <word> pool <word> overload", c => NatList(c, c.Arg(0), "", c.Arg(1), true));
                t.Add("ip nat inside source static <ip> <ip>", c =>
                {
                    uint l = Ip.Parse(c.Arg(0)), g = Ip.Parse(c.Arg(1));
                    c.IpS.Nat.Rules.RemoveAll(r => r.IsStatic && r.StaticLocal == l);
                    if (!c.No) c.IpS.Nat.Rules.Add(new NatRule { StaticLocal = l, StaticGlobal = g });
                });
                t.Add("ip nat pool <word> <ip> <ip> netmask <mask>", c =>
                {
                    if (c.No) { c.IpS.Nat.Pools.Remove(c.Arg(0)); return; }
                    c.IpS.Nat.Pools[c.Arg(0)] = new NatPool { Name = c.Arg(0), Start = Ip.Parse(c.Arg(1)), End = Ip.Parse(c.Arg(2)), Mask = Ip.Parse(c.Arg(3)) };
                });
            }
            var std = t.Add("access-list <num:1-99> permit <line>", c => AccessList(c, c.Arg(0), true, c.Arg(1), false));
            std.HelpFn = (s, rest) => AceHelp(rest, false);
            var stdd = t.Add("access-list <num:1-99> deny <line>", c => AccessList(c, c.Arg(0), false, c.Arg(1), false));
            stdd.HelpFn = (s, rest) => AceHelp(rest, false);
            t.Add("access-list <num:1-99> remark <line>", c => Remark(c, c.Arg(0), c.Arg(1), false));
            var ext = t.Add("access-list <num:100-199> permit <line>", c => AccessList(c, c.Arg(0), true, c.Arg(1), true));
            ext.HelpFn = (s, rest) => AceHelp(rest, true);
            var extd = t.Add("access-list <num:100-199> deny <line>", c => AccessList(c, c.Arg(0), false, c.Arg(1), true));
            extd.HelpFn = (s, rest) => AceHelp(rest, true);
            t.Add("access-list <num:100-199> remark <line>", c => Remark(c, c.Arg(0), c.Arg(1), true));
            t.Add("access-list <num:1-99>", c => { if (c.No) c.IpS.Acls.Remove(c.Arg(0)); else c.Out("% Incomplete command."); });
            t.Add("access-list <num:100-199>", c => { if (c.No) c.IpS.Acls.Remove(c.Arg(0)); else c.Out("% Incomplete command."); });
            t.Add("ip access-list standard <word>", c => NamedAcl(c, c.Arg(0), false));
            t.Add("ip access-list extended <word>", c => NamedAcl(c, c.Arg(0), true));
            t.Add("line console 0", c => { c.S.LineKind = "con"; c.S.Mode = "config-line"; }, null, null, "First Line number");
            t.Add("line vty <num:0-15> <num:0-15>", c => { c.S.LineKind = "vty"; c.Ios.VtyDefined = true; c.S.Mode = "config-line"; }, null, null, "First Line number", "Last Line number");
            t.Add("cdp run", c => c.Ios.Cdp = !c.No);
            t.Add("no", c => c.Out("% Incomplete command."));
        }

        static void StaticRoute(CliCtx c, string nh, string ifname, int ad)
        {
            uint net = Ip.Parse(c.Arg(0)), mask = Ip.Parse(c.Arg(1));
            if (!Ip.IsValidMask(mask)) { c.Out("%Inconsistent address and mask"); return; }
            if ((net & mask) != net) { c.Out("%Inconsistent address and mask"); return; }
            uint hop = nh.Length > 0 ? Ip.Parse(nh) : 0;
            string iff = "";
            if (ifname.Length > 0)
            {
                var f = c.IpS.IfByName(Ios.NormalizeIfName(ifname) ?? ifname);
                if (f == null) { c.Out("% Invalid input detected: no interface " + ifname); return; }
                iff = f.Name;
            }
            if (c.No)
            {
                c.IpS.Static.RemoveAll(r => r.Net == net && r.Mask == mask && (hop == 0 || r.NextHop == hop) && (iff == "" || r.ExitIf == iff));
                return;
            }
            if (hop != 0 && c.IpS.IsMine(hop)) { c.Out("%Invalid next hop address (it's this router)"); return; }
            c.IpS.Static.RemoveAll(r => r.Net == net && r.Mask == mask && r.NextHop == hop && r.ExitIf == iff);
            c.IpS.Static.Add(new StaticRoute { Net = net, Mask = mask, NextHop = hop, ExitIf = iff, Ad = ad });
        }

        static void Excluded(CliCtx c, string a, string b)
        {
            if (c.IpS.Dhcp == null) c.IpS.Dhcp = new DhcpServer(c.IpS);
            uint x = Ip.Parse(a), y = Ip.Parse(b);
            if (y < x) { c.Out("% Invalid range: the second address must be higher."); return; }
            c.IpS.Dhcp.Excluded.RemoveAll(r => r[0] == x && r[1] == y);
            if (!c.No) c.IpS.Dhcp.Excluded.Add(new[] { x, y });
        }

        static void NatList(CliCtx c, string acl, string ifname, string pool, bool overload)
        {
            string iff = "";
            if (ifname.Length > 0)
            {
                var f = c.IpS.IfByName(Ios.NormalizeIfName(ifname) ?? ifname);
                if (f == null) { c.Out("% Invalid interface " + ifname); return; }
                iff = f.Name;
            }
            c.IpS.Nat.Rules.RemoveAll(r => !r.IsStatic && r.Acl == acl);
            if (!c.No) c.IpS.Nat.Rules.Add(new NatRule { Acl = acl, Interface = iff, Pool = pool, Overload = overload || iff.Length > 0 });
        }

        static void StpPriority(CliCtx c, string vlans, int pr)
        {
            if (pr % 4096 != 0) { c.Out("% Bridge Priority must be in increments of 4096.\n% Allowed values are:\n  0     4096  8192  12288 16384 20480 24576 28672\n  32768 36864 40960 45056 49152 53248 57344 61440"); return; }
            for (int v = 1; v <= 4094; v++) if (VlanList.Allows(vlans, v)) { if (pr == 32768) c.D.Bridge.StpPriority.Remove(v); else c.D.Bridge.StpPriority[v] = pr; }
            c.D.P.Sim.TopologyChanged();
        }

        // ---------------------------------------------------------------- interfaces
        static void EnterInterface(CliCtx c, string name)
        {
            string n = Ios.NormalizeIfName(name);
            var d = c.D;
            if (n == null) { c.Out("% Invalid interface name"); return; }
            int dot = n.IndexOf('.');
            if (n.StartsWith("Vlan"))
            {
                if (d.Kind != "switch") { c.Out("% Interface Vlan isn't available on a router: use subinterfaces (interface g0/0.10)."); return; }
                int v = int.Parse(n.Substring(4));
                if (v < 1 || v > 4094) { c.Out("% Invalid VLAN"); return; }
                if (c.No) { if (v != 1 && d.Bridge.Svis.ContainsKey(v)) { var f0 = d.Bridge.Svis[v]; d.Bridge.Svis.Remove(v); d.Ip.Ifs.Remove(f0); } return; }
                bool isNew = !d.Bridge.Svis.ContainsKey(v);
                var f = d.Bridge.AddSvi(v, false);
                if (isNew && !c.S.Loading) c.Out("%LINK-5-CHANGED: Interface Vlan" + v + ", changed state to up");
                c.S.If = f; c.S.Mode = "config-if";
                return;
            }
            if (n.StartsWith("Loopback"))
            {
                if (d.Kind == "switch") { c.Out("% Loopback interfaces are for routers here."); return; }
                int k = int.Parse(n.Substring(8));
                if (c.No) { var lo = d.Ip.Ifs.FirstOrDefault(x => x.IsLoopback && x.SubNumber == k); if (lo != null) d.Ip.Ifs.Remove(lo); return; }
                bool isNew = !d.Ip.Ifs.Any(x => x.IsLoopback && x.SubNumber == k);
                c.S.If = d.Ip.AddLoopback(k);
                if (isNew && !c.S.Loading) c.Out("%LINK-5-CHANGED: Interface Loopback" + k + ", changed state to up\n%LINEPROTO-5-UPDOWN: Line protocol on Interface Loopback" + k + ", changed state to up");
                c.S.Mode = "config-if";
                return;
            }
            string baseName = dot > 0 ? n.Substring(0, dot) : n;
            var port = d.Ports.FirstOrDefault(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase) && !p.IsSerial);
            if (port == null) { c.Out("%Invalid interface type and number (this " + d.KindName.ToLowerInvariant() + " has " + string.Join(", ", d.Ports.Where(p => !p.IsSerial).Select(p => p.Short).Take(4).ToArray()) + "...)"); return; }
            if (dot > 0)
            {
                if (d.Kind == "switch") { c.Out("% Subinterfaces are for routers."); return; }
                int sub;
                if (!int.TryParse(n.Substring(dot + 1), out sub) || sub < 1 || sub > 4294967) { c.Out("% Invalid subinterface number"); return; }
                if (c.No) { var x = d.Ip.Ifs.FirstOrDefault(f => f.IsSub && f.Port == port && f.SubNumber == sub); if (x != null) d.Ip.Ifs.Remove(x); return; }
                c.S.If = d.Ip.AddSub(port, sub);
                c.S.Mode = "config-subif";
                return;
            }
            if (c.No) { c.Out("% Physical interfaces can't be removed"); return; }
            c.S.If = port.L3;
            c.S.Range = d.Kind == "switch" ? new List<Port> { port } : null;
            c.S.Mode = "config-if";
        }

        static void EnterRange(CliCtx c, string spec)
        {
            var ports = new List<Port>();
            foreach (var part0 in spec.Split(','))
            {
                string part = part0.Trim();
                if (part.Length == 0) continue;
                string a = part, bEnd = null;
                int dash = part.IndexOf('-');
                if (dash > 0) { a = part.Substring(0, dash).Trim(); bEnd = part.Substring(dash + 1).Trim(); }
                string n = Ios.NormalizeIfName(a);
                if (n == null) { c.Out("% Invalid interface range: " + part); return; }
                int slash = n.LastIndexOf('/');
                if (slash < 0) { c.Out("% Invalid interface range: " + part); return; }
                string prefix = n.Substring(0, slash + 1);
                int from = int.Parse(n.Substring(slash + 1)), to = from;
                if (bEnd != null && !int.TryParse(bEnd, out to)) { c.Out("% Invalid interface range: " + part); return; }
                for (int i = from; i <= to; i++)
                {
                    var p = c.D.Ports.FirstOrDefault(x => string.Equals(x.Name, prefix + i, StringComparison.OrdinalIgnoreCase));
                    if (p == null) { c.Out("% Interface " + prefix + i + " doesn't exist on this device"); return; }
                    if (!ports.Contains(p)) ports.Add(p);
                }
            }
            if (ports.Count == 0) { c.Out("% Incomplete command."); return; }
            c.S.Range = ports;
            c.S.If = c.D.Kind == "router" ? ports[0].L3 : null;
            c.S.Mode = "config-if-range";
        }

        static void EnterVlan(CliCtx c, int v)
        {
            var b = c.D.Bridge;
            if (v >= 1002 && v <= 1005) { c.Out("%Default VLAN " + v + " may not be modified."); return; }
            if (c.No)
            {
                if (v == 1) { c.Out("%Default VLAN 1 may not be deleted."); return; }
                b.Vlans.Remove(v);
                c.D.P.Sim.TopologyChanged();
                return;
            }
            if (!b.Vlans.ContainsKey(v)) { b.Vlans[v] = "VLAN" + v.ToString("D4"); c.D.P.Sim.TopologyChanged(); }
            c.S.VlanId = v;
            c.S.Mode = "config-vlan";
        }
        static void EnterVlanList(CliCtx c, string list)
        {
            for (int v = 2; v <= 1001; v++) if (VlanList.Allows(list, v)) { if (c.No) c.D.Bridge.Vlans.Remove(v); else if (!c.D.Bridge.Vlans.ContainsKey(v)) c.D.Bridge.Vlans[v] = "VLAN" + v.ToString("D4"); }
            c.D.P.Sim.TopologyChanged();
            if (!c.No) c.S.Mode = "config-vlan";
        }

        // ================================================================= (config-if)#
        static IEnumerable<Port> SwPorts(CliCtx c)
        {
            if (c.S.Range != null) return c.S.Range;
            if (c.S.If != null && c.S.If.Port != null && !c.S.If.IsSub) return new[] { c.S.If.Port };
            return new Port[0];
        }
        static IEnumerable<IpIf> L3Ifs(CliCtx c)
        {
            if (c.S.Mode == "config-if-range" && c.D.Kind == "router") return c.S.Range.Select(p => p.L3);
            if (c.S.If != null) return new[] { c.S.If };
            return new IpIf[0];
        }
        static bool IsSwitchPort(CliCtx c) { return c.D.Kind == "switch" && c.S.If != null && !c.S.If.IsSvi || c.D.Kind == "switch" && c.S.Mode == "config-if-range"; }

        static void Interface(CliTree t, bool sw, string mode)
        {
            t.Add("description <line>", c =>
            {
                foreach (var f in L3Ifs(c)) f.Description = c.No ? "" : c.Arg(0);
                foreach (var p in SwPorts(c)) p.Description = c.No ? "" : c.Arg(0);
            });
            t.Add("shutdown", c => Shutdown(c, !c.No));
            t.Add("ip address <ip> <mask>", c => IpAddress(c, c.Arg(0), c.Arg(1)));
            t.Add("ip address dhcp", c => IpDhcp(c));
            t.Add("ip address", c => { if (c.No) IpAddress(c, "", ""); else c.Out("% Incomplete command."); });
            t.Add("ip access-group <word> in", c => { foreach (var f in L3Ifs(c)) f.AclIn = c.No ? "" : c.Arg(0); });
            t.Add("ip access-group <word> out", c => { foreach (var f in L3Ifs(c)) f.AclOut = c.No ? "" : c.Arg(0); });
            t.Add("speed auto", c => { });
            t.Add("speed <num:10-1000>", c => { });
            t.Add("duplex auto", c => { });
            t.Add("duplex full", c => { });
            t.Add("duplex half", c => { });
            t.Add("cdp enable", c => { });
            if (sw)
            {
                t.Add("switchport mode access", c => SwMode(c, false));
                t.Add("switchport mode trunk", c => SwMode(c, !c.No));
                t.Add("switchport mode dynamic auto", c => SwMode(c, false));
                t.Add("switchport mode dynamic desirable", c => SwMode(c, false));
                t.Add("switchport access vlan <num:1-4094>", c => AccessVlan(c, c.No ? 1 : int.Parse(c.Arg(0))));
                t.Add("switchport trunk native vlan <num:1-4094>", c => { foreach (var p in SwPorts(c)) p.NativeVlan = c.No ? 1 : int.Parse(c.Arg(0)); c.D.P.Sim.TopologyChanged(); });
                t.Add("switchport trunk allowed vlan <vlanlist>", c => Allowed(c, c.No ? "all" : c.Arg(0), 0));
                t.Add("switchport trunk allowed vlan add <vlanlist>", c => Allowed(c, c.Arg(0), 1));
                t.Add("switchport trunk allowed vlan remove <vlanlist>", c => Allowed(c, c.Arg(0), -1));
                t.Add("switchport trunk allowed vlan all", c => Allowed(c, "all", 0));
                t.Add("switchport trunk allowed vlan none", c => Allowed(c, "none", 0));
                t.Add("switchport trunk encapsulation dot1q", c => { });
                t.Add("switchport nonegotiate", c => { });
                t.Add("switchport voice vlan <num:1-4094>", c => { foreach (var p in SwPorts(c)) p.VoiceVlan = c.No ? 0 : int.Parse(c.Arg(0)); });
                t.Add("switchport port-security", c => c.Out("% Port security isn't part of this simulator yet."));
                t.Add("switchport", c => { });
                t.Add("spanning-tree portfast", c => { foreach (var p in SwPorts(c)) p.Portfast = !c.No; if (!c.No && !c.S.Loading) c.Out("%Warning: portfast should only be enabled on ports connected to a single\n host. Connecting hubs, concentrators, switches, bridges, etc... to this\n interface  when portfast is enabled, can cause temporary bridging loops.\n Use with CAUTION"); });
            }
            else
            {
                t.Add("ip nat inside", c => { foreach (var f in L3Ifs(c)) f.Nat = c.No ? 0 : 1; });
                t.Add("ip nat outside", c => { foreach (var f in L3Ifs(c)) f.Nat = c.No ? 0 : 2; });
                t.Add("ip helper-address <ip>", c =>
                {
                    uint h = Ip.Parse(c.Arg(0));
                    foreach (var f in L3Ifs(c)) { f.Helpers.Remove(h); if (!c.No) f.Helpers.Add(h); }
                });
                t.Add("encapsulation dot1q <num:1-4094>", c => Encap(c, int.Parse(c.Arg(0)), false));
                t.Add("encapsulation dot1q <num:1-4094> native", c => Encap(c, int.Parse(c.Arg(0)), true));
                t.Add("ip rip <line>", c => { });
            }
        }

        static void Shutdown(CliCtx c, bool down)
        {
            var d = c.D;
            var changed = new List<string>();
            if (d.Kind == "switch" && c.S.If != null && c.S.If.IsSvi)
            {
                c.S.If.Shutdown = down;
                if (!c.S.Loading) c.Out("%LINK-5-CHANGED: Interface " + c.S.If.Name + ", changed state to " + (down ? "administratively down" : "up"));
                d.P.Sim.TopologyChanged();
                return;
            }
            if (d.Kind == "switch")
            {
                foreach (var p in SwPorts(c)) { if (p.Shutdown != down) { p.Shutdown = down; changed.Add(p.Name); } }
            }
            else
            {
                foreach (var f in L3Ifs(c))
                {
                    if (f.Shutdown != down) { f.Shutdown = down; changed.Add(f.Name); }
                    if (!f.IsSub && !f.IsLoopback && f.Port != null) f.Port.Shutdown = down;
                }
            }
            d.P.Sim.TopologyChanged();
            if (c.S.Loading) return;
            foreach (var n in changed)
            {
                var f = d.Ip.IfByName(n);
                var p = d.Ports.FirstOrDefault(x => x.Name == n);
                if (down) c.Out("%LINK-5-CHANGED: Interface " + n + ", changed state to administratively down");
                else
                {
                    bool up = f != null ? f.Up : p != null && p.IsUp;
                    c.Out("%LINK-5-CHANGED: Interface " + n + ", changed state to " + (up ? "up" : "down"));
                    if (up) c.Out("%LINEPROTO-5-UPDOWN: Line protocol on Interface " + n + ", changed state to up");
                }
            }
        }

        static void IpAddress(CliCtx c, string ip, string mask)
        {
            if (c.D.Kind == "switch" && (c.S.If == null || !c.S.If.IsSvi)) { c.Out("% IP addresses go on interface vlan 1 on this switch (its ports only switch)."); return; }
            foreach (var f in L3Ifs(c))
            {
                if (c.No || ip.Length == 0)
                {
                    if (f.DhcpC != null) f.DhcpC.Stop();
                    f.Dhcp = false;
                    c.IpS.SetAddress(f, 0, 0);
                    continue;
                }
                uint a = Ip.Parse(ip), m = Ip.Parse(mask);
                if (!Ip.IsValidMask(m)) { c.Out("% Bad mask 0x" + m.ToString("X8") + " for address " + ip); return; }
                if (m != 0xFFFFFFFF && ((a & ~m) == 0 || (a | m) == 0xFFFFFFFF)) { c.Out("Bad mask /" + Ip.PrefixOf(m) + " for address " + ip); return; }
                foreach (var o in c.IpS.Ifs)
                {
                    if (o == f || o.Ip == 0) continue;
                    if (Ip.SameNet(o.Ip, a, Math.Min(o.Mask, m) == o.Mask ? o.Mask : m) && !o.IsLoopback)
                    {
                        c.Out("% " + Ip.Str(a & m) + " overlaps with " + o.ShortName);
                        if (o.Up) return;
                    }
                }
                if (f.IsSub && f.Vlan == 0) { c.Out("% Configuring IP routing on a LAN subinterface is only allowed if that subinterface is already configured as part of an IEEE 802.10, IEEE 802.1Q, or ISL vLAN."); return; }
                if (f.DhcpC != null) f.DhcpC.Stop();
                f.Dhcp = false;
                c.IpS.SetAddress(f, a, m);
            }
            c.D.P.Sim.TopologyChanged();
        }

        static void IpDhcp(CliCtx c)
        {
            foreach (var f in L3Ifs(c))
            {
                if (c.No) { if (f.DhcpC != null) f.DhcpC.Release(); f.Dhcp = false; continue; }
                f.Dhcp = true;
                c.IpS.SetAddress(f, 0, 0);
                if (f.DhcpC == null) f.DhcpC = new DhcpClient(c.IpS, f);
                if (f.Up) f.DhcpC.Start();
            }
        }

        static void SwMode(CliCtx c, bool trunk)
        {
            foreach (var p in SwPorts(c)) p.Trunk = trunk;
            if (!c.S.Loading && trunk) foreach (var p in SwPorts(c)) if (p.IsUp) c.Out("%LINEPROTO-5-UPDOWN: Line protocol on Interface " + p.Name + ", changed state to down\n%LINEPROTO-5-UPDOWN: Line protocol on Interface " + p.Name + ", changed state to up");
            c.D.P.Sim.TopologyChanged();
        }

        static void AccessVlan(CliCtx c, int v)
        {
            var b = c.D.Bridge;
            if (v >= 1002 && v <= 1005) { c.Out("% VLAN " + v + " can't be used here."); return; }
            if (!b.Vlans.ContainsKey(v))
            {
                if (!c.S.Loading) c.Out("% Access VLAN does not exist. Creating vlan " + v);
                b.Vlans[v] = "VLAN" + v.ToString("D4");
            }
            foreach (var p in SwPorts(c)) p.AccessVlan = v;
            c.D.P.Sim.TopologyChanged();
        }

        static void Allowed(CliCtx c, string list, int how)
        {
            foreach (var p in SwPorts(c))
            {
                if (how == 0) p.Allowed = list;
                else p.Allowed = VlanList.Change(p.Allowed, list, how > 0);
            }
            c.D.P.Sim.TopologyChanged();
        }

        static void Encap(CliCtx c, int v, bool native)
        {
            var f = c.S.If;
            if (f == null || !f.IsSub) { c.Out("% Use encapsulation dot1Q on a subinterface (for example interface g0/0.10)."); return; }
            if (c.No) { f.Vlan = 0; f.NativeSub = false; return; }
            if (c.IpS.Ifs.Any(o => o != f && o.IsSub && o.Port == f.Port && o.Vlan == v)) { c.Out("% Configuration of multiple subinterfaces of the same main interface with the same VID (" + v + ") is not permitted."); return; }
            f.Vlan = v; f.NativeSub = native;
        }

        // ================================================================= (config-router)#
        static void Router(CliTree t)
        {
            t.Add("version <num:1-2>", c => { var r = c.IpS.Rip; r.Version = c.No ? 1 : int.Parse(c.Arg(0)); r.VersionSet = !c.No; r.Changed(); }, "Set routing protocol version");
            t.Add("network <ip>", c =>
            {
                var r = c.IpS.Rip;
                uint n = Ip.Parse(c.Arg(0));
                n &= Ip.MaskOf(Ip.ClassfulPrefix(n));   // RIP keeps the classful network
                r.Networks.Remove(n);
                if (!c.No) r.Networks.Add(n);
                r.Changed();
            });
            t.Add("auto-summary", c => c.IpS.Rip.AutoSummary = !c.No);
            t.Add("passive-interface <iface>", c =>
            {
                var f = c.IpS.IfByName(Ios.NormalizeIfName(c.Arg(0)) ?? c.Arg(0));
                if (f == null) { c.Out("% Invalid interface"); return; }
                c.IpS.Rip.Passive.RemoveAll(x => x == f.Name);
                if (!c.No) c.IpS.Rip.Passive.Add(f.Name);
            });
            t.Add("default-information originate", c => { c.IpS.Rip.DefaultOriginate = !c.No; c.IpS.Rip.Changed(); });
            t.Add("redistribute static", c => c.Out("% Redistribution isn't part of this simulator yet (default-information originate shares a default route)."));
            t.Add("timers basic <line>", c => { });
        }

        // ================================================================= (config-line)#
        static void Line(CliTree t)
        {
            Func<CliCtx, LineCfg> L = c => c.S.LineKind == "vty" ? c.Ios.Vty : c.Ios.Con;
            t.Add("password <word>", c => { L(c).Password = c.No ? "" : c.Arg(0); });
            t.Add("password 7 <word>", c => { L(c).Password = c.No ? "" : (Ios.FromType7(c.Arg(0)) ?? ""); }, null, "Specifies a HIDDEN password will follow");
            t.Add("login", c =>
            {
                var l = L(c);
                l.Login = !c.No;
                if (l.Login && l.Password.Length == 0 && !c.S.Loading) c.Out("% Login disabled on line, until 'password' is set");
            });
            t.Add("login local", c => { L(c).Login = !c.No; });
            t.Add("exec-timeout <num:0-35791>", c => { L(c).ExecTimeout = c.No ? 10 : int.Parse(c.Arg(0)); });
            t.Add("exec-timeout <num:0-35791> <num:0-2147483>", c => { L(c).ExecTimeout = c.No ? 10 : int.Parse(c.Arg(0)); });
            t.Add("logging synchronous", c => { L(c).LoggingSync = !c.No; });
            t.Add("transport input <line>", c => { L(c).Transport = c.No ? "" : c.Arg(0); });
        }

        // ================================================================= (config-vlan)#
        static void VlanMode(CliTree t)
        {
            t.Add("name <word>", c =>
            {
                int v = c.S.VlanId;
                if (v == 0) return;
                c.D.Bridge.Vlans[v] = c.No ? "VLAN" + v.ToString("D4") : (c.Arg(0).Length > 32 ? c.Arg(0).Substring(0, 32) : c.Arg(0));
            });
        }

        // ================================================================= (dhcp-config)#
        static void DhcpMode(CliTree t)
        {
            Func<CliCtx, DhcpPool> P = c => c.IpS.Dhcp.Pool(c.S.Pool, true);
            t.Add("network <ip> <prefixmask>", c =>
            {
                var p = P(c);
                if (c.No) { p.Network = 0; p.Mask = 0; return; }
                uint n = Ip.Parse(c.Arg(0)), m; Ip.TryParseMask(c.Arg(1), out m);
                if ((n & m) != n) { c.Out("% " + c.Arg(0) + " " + c.Arg(1) + " is an invalid network."); return; }
                p.Network = n; p.Mask = m;
            }, "Network number and mask");
            t.Add("default-router <ip>", c => { P(c).Router = c.No ? 0 : Ip.Parse(c.Arg(0)); });
            t.Add("default-router <ip> <ip>", c => { P(c).Router = c.No ? 0 : Ip.Parse(c.Arg(0)); });
            t.Add("dns-server <ip>", c => { var p = P(c); p.Dns.Clear(); if (!c.No) p.Dns.Add(Ip.Parse(c.Arg(0))); });
            t.Add("dns-server <ip> <ip>", c => { var p = P(c); p.Dns.Clear(); if (!c.No) { p.Dns.Add(Ip.Parse(c.Arg(0))); p.Dns.Add(Ip.Parse(c.Arg(1))); } });
            t.Add("domain-name <word>", c => { P(c).Domain = c.No ? "" : c.Arg(0); });
            t.Add("lease <num:0-365>", c => { });
            t.Add("lease infinite", c => { });
        }

        // ================================================================= access lists
        static void NamedAcl(CliCtx c, string name, bool ext)
        {
            Acl a;
            if (c.No) { c.IpS.Acls.Remove(name); return; }
            if (!c.IpS.Acls.TryGetValue(name, out a)) { a = new Acl { Name = name, Extended = ext, Named = true }; c.IpS.Acls[name] = a; }
            else if (a.Extended != ext) { c.Out("% A named " + (a.Extended ? "extended" : "standard") + " IP access list with this name already exists"); return; }
            c.S.Acl = a;
            c.S.Mode = ext ? "config-ext-nacl" : "config-std-nacl";
        }

        static void NaclMode(CliTree t, bool ext)
        {
            var p = t.Add("permit <line>", c => NaclLine(c, 0, true, c.Arg(0)));
            p.HelpFn = (s, rest) => AceHelp(rest, ext);
            var d = t.Add("deny <line>", c => NaclLine(c, 0, false, c.Arg(0)));
            d.HelpFn = (s, rest) => AceHelp(rest, ext);
            t.Add("remark <line>", c => { if (!c.No) c.S.Acl.Add(new Ace { Remark = c.Arg(0), Extended = ext }); });
            var sp = t.Add("<num:1-2147483647> permit <line>", c => NaclLine(c, int.Parse(c.Arg(0)), true, c.Arg(1)));
            sp.HelpFn = (s, rest) => AceHelp(rest, ext);
            var sd = t.Add("<num:1-2147483647> deny <line>", c => NaclLine(c, int.Parse(c.Arg(0)), false, c.Arg(1)));
            sd.HelpFn = (s, rest) => AceHelp(rest, ext);
            t.Add("<num:1-2147483647>", c => { if (c.No) c.S.Acl.Lines.RemoveAll(x => x.Seq == int.Parse(c.Arg(0))); else c.Out("% Incomplete command."); });
        }

        static void NaclLine(CliCtx c, int seq, bool permit, string rest)
        {
            var acl = c.S.Acl;
            Ace a; int err; string msg;
            if (!ParseAce(rest, acl.Extended, out a, out err, out msg)) { AceError(c, rest, err, msg); return; }
            a.Permit = permit; a.Seq = seq;
            if (c.No) { acl.Lines.RemoveAll(x => x.Text() == a.Text()); return; }
            if (seq != 0 && acl.Lines.Any(x => x.Seq == seq)) { c.Out("% Duplicate sequence number"); return; }
            if (acl.Lines.Any(x => x.Text() == a.Text())) return;
            acl.Add(a);
        }

        static void AccessList(CliCtx c, string num, bool permit, string rest, bool ext)
        {
            Acl acl;
            if (!c.IpS.Acls.TryGetValue(num, out acl)) { acl = new Acl { Name = num, Extended = ext }; }
            Ace a; int err; string msg;
            if (!ParseAce(rest, ext, out a, out err, out msg)) { AceError(c, rest, err, msg); return; }
            a.Permit = permit;
            if (c.No) { c.IpS.Acls.Remove(num); return; }   // like the real thing: "no access-list N ..." removes the whole list
            if (!c.IpS.Acls.ContainsKey(num)) c.IpS.Acls[num] = acl;
            if (acl.Lines.Any(x => x.Text() == a.Text())) return;
            acl.Add(a);
        }

        static void Remark(CliCtx c, string num, string text, bool ext)
        {
            Acl acl;
            if (!c.IpS.Acls.TryGetValue(num, out acl)) { acl = new Acl { Name = num, Extended = ext }; c.IpS.Acls[num] = acl; }
            if (!c.No) acl.Add(new Ace { Remark = text, Extended = ext });
        }

        static void AceError(CliCtx c, string rest, int pos, string msg)
        {
            int at = c.Line.Length - rest.Length + pos;
            if (at < 0) at = 0;
            c.Out(new string(' ', c.S.Prompt.Length + at) + "^\n% Invalid input detected at '^' marker." + (msg.Length > 0 ? "\n(" + msg + ")" : ""));
            c.S.Errors++;
        }

        /// <summary>Reads the part of an access list line after permit / deny. err is the character where it went wrong.</summary>
        public static bool ParseAce(string text, bool ext, out Ace a, out int err, out string msg)
        {
            a = new Ace { Extended = ext };
            err = 0; msg = "";
            var toks = new List<Tuple<string, int>>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && text[i] == ' ') i++;
                if (i >= text.Length) break;
                int st = i;
                while (i < text.Length && text[i] != ' ') i++;
                toks.Add(Tuple.Create(text.Substring(st, i - st).ToLowerInvariant(), st));
            }
            int k = 0;
            Func<int> pos = () => k < toks.Count ? toks[k].Item2 : text.Length;
            Func<string> cur = () => k < toks.Count ? toks[k].Item1 : null;
            if (ext)
            {
                string pr = cur();
                if (pr == null) { err = pos(); msg = "say which protocol: ip, tcp, udp or icmp"; return false; }
                if (pr != "ip" && pr != "tcp" && pr != "udp" && pr != "icmp") { err = pos(); msg = "the protocol must be ip, tcp, udp or icmp"; return false; }
                a.Proto = pr; k++;
            }
            // source
            if (!ReadAddr(toks, ref k, out a.SrcAny, out a.Src, out a.SrcWild, ext, out err, out msg, text.Length)) return false;
            if (!ext)
            {
                if (k < toks.Count && toks[k].Item1 == "log") k++;
                if (k < toks.Count) { err = toks[k].Item2; msg = "a standard list only looks at the source address"; return false; }
                return true;
            }
            if (a.Proto == "tcp" || a.Proto == "udp") { if (!ReadPorts(toks, ref k, out a.SrcOp, out a.SrcPort1, out a.SrcPort2, out err, out msg, text.Length)) return false; }
            if (!ReadAddr(toks, ref k, out a.DstAny, out a.Dst, out a.DstWild, true, out err, out msg, text.Length)) return false;
            if (a.Proto == "tcp" || a.Proto == "udp") { if (!ReadPorts(toks, ref k, out a.DstOp, out a.DstPort1, out a.DstPort2, out err, out msg, text.Length)) return false; }
            if (a.Proto == "icmp" && k < toks.Count && new[] { "echo", "echo-reply", "unreachable", "time-exceeded" }.Contains(toks[k].Item1)) { a.IcmpType = toks[k].Item1; k++; }
            if (k < toks.Count && (toks[k].Item1 == "log" || toks[k].Item1 == "established")) k++;
            if (k < toks.Count) { err = toks[k].Item2; msg = ""; return false; }
            return true;
        }

        static bool ReadAddr(List<Tuple<string, int>> toks, ref int k, out bool any, out uint ip, out uint wild, bool needWild, out int err, out string msg, int end)
        {
            any = false; ip = 0; wild = 0; err = 0; msg = "";
            if (k >= toks.Count) { err = end; msg = "an address is missing: any, host A.B.C.D, or A.B.C.D and wildcard bits"; return false; }
            string t = toks[k].Item1;
            if (t == "any") { any = true; k++; return true; }
            if (t == "host")
            {
                k++;
                if (k >= toks.Count || !Ip.TryParse(toks[k].Item1, out ip)) { err = k < toks.Count ? toks[k].Item2 : end; msg = "host needs an IP address after it"; return false; }
                k++; return true;
            }
            if (!Ip.TryParse(t, out ip)) { err = toks[k].Item2; msg = "expected any, host or an IP address"; return false; }
            k++;
            uint w;
            if (k < toks.Count && Ip.TryParse(toks[k].Item1, out w)) { wild = w; k++; }
            else if (needWild) { err = k < toks.Count ? toks[k].Item2 : end; msg = "after an address comes its wildcard mask (like 0.0.0.255), or write host before it"; return false; }
            ip &= ~wild;
            return true;
        }

        static bool ReadPorts(List<Tuple<string, int>> toks, ref int k, out string op, out int p1, out int p2, out int err, out string msg, int end)
        {
            op = ""; p1 = 0; p2 = 0; err = 0; msg = "";
            if (k >= toks.Count) return true;
            string t = toks[k].Item1;
            if (t != "eq" && t != "neq" && t != "gt" && t != "lt" && t != "range") return true;
            op = t; k++;
            if (k >= toks.Count || Ports.Parse(toks[k].Item1) < 0) { err = k < toks.Count ? toks[k].Item2 : end; msg = "a port number or name (www, domain...) is missing"; return false; }
            p1 = Ports.Parse(toks[k].Item1); k++;
            if (op == "range")
            {
                if (k >= toks.Count || Ports.Parse(toks[k].Item1) < 0) { err = k < toks.Count ? toks[k].Item2 : end; msg = "range needs two ports"; return false; }
                p2 = Ports.Parse(toks[k].Item1); k++;
            }
            return true;
        }

        /// <summary>? help inside an access list line.</summary>
        static string AceHelp(string rest, bool ext)
        {
            var toks = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool partial = rest.Length > 0 && !rest.EndsWith(" ");
            if (partial && toks.Count > 0) toks.RemoveAt(toks.Count - 1);
            var sb = new StringBuilder();
            Action<string, string> line = (w, h) => sb.Append("  " + w.PadRight(14) + h + "\n");
            int stage = 0;   // what comes next
            // a rough walk: protocol, source, (ports), destination, (ports)
            int k = 0;
            string proto = "ip";
            if (ext) { if (k < toks.Count) { proto = toks[k]; k++; stage = 1; } else stage = 0; }
            else stage = 1;
            int addrs = 0;
            while (k < toks.Count)
            {
                string t = toks[k];
                if (t == "any") { addrs++; k++; }
                else if (t == "host") { k += 2; addrs++; }
                else { uint x; if (Ip.TryParse(t, out x)) { k++; if (k < toks.Count) { uint y; if (Ip.TryParse(toks[k], out y)) k++; } addrs++; } else if (t == "eq" || t == "gt" || t == "lt" || t == "neq") k += 2; else if (t == "range") k += 3; else k++; }
            }
            if (ext && stage == 0)
            {
                line("icmp", "Internet Control Message Protocol"); line("ip", "Any Internet Protocol"); line("tcp", "Transmission Control Protocol"); line("udp", "User Datagram Protocol");
                return sb.ToString();
            }
            if (addrs == 0 || (ext && addrs == 1))
            {
                line("A.B.C.D", (addrs == 0 ? "Source" : "Destination") + " address");
                line("any", "Any " + (addrs == 0 ? "source" : "destination") + " host");
                line("host", "A single " + (addrs == 0 ? "source" : "destination") + " host");
                if (ext && addrs == 1 && (proto == "tcp" || proto == "udp")) { line("eq", "Match only packets on a given port number"); line("gt", "Match only packets with a greater port number"); line("lt", "Match only packets with a lower port number"); line("neq", "Match only packets not on a given port number"); line("range", "Match only packets in the range of port numbers"); }
                return sb.ToString();
            }
            if (ext && (proto == "tcp" || proto == "udp") && toks.Count > 0 && (toks[toks.Count - 1] == "eq" || toks[toks.Count - 1] == "neq" || toks[toks.Count - 1] == "gt" || toks[toks.Count - 1] == "lt"))
            {
                line("<0-65535>", "Port number"); line("domain", "Domain Name Service (53)"); line("ftp", "File Transfer Protocol (21)"); line("pop3", "Post Office Protocol v3 (110)"); line("smtp", "Simple Mail Transport Protocol (25)"); line("telnet", "Telnet (23)"); line("www", "World Wide Web (HTTP, 80)");
                return sb.ToString();
            }
            if (ext && (proto == "tcp" || proto == "udp")) { line("eq", "Match only packets on a given port number"); line("gt", "Match only packets with a greater port number"); line("lt", "Match only packets with a lower port number"); line("range", "Match only packets in the range of port numbers"); }
            if (ext && proto == "icmp") { line("echo", "Echo (ping)"); line("echo-reply", "Echo reply"); line("time-exceeded", "Time exceeded"); line("unreachable", "All unreachables"); }
            if (!ext && addrs == 1 && toks.Count == 1 && toks[0] != "any" && toks[0] != "host") line("A.B.C.D", "Source wildcard bits");
            line("log", "Log matches against this entry");
            sb.Append("  <cr>\n");
            return sb.ToString();
        }
    }
}
