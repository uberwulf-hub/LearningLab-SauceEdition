// Network Lab - a small script language for building networks and doing things in them: the labs' starting
// networks and answers are written in it, and the tests use it. One action per line:
//   add <kind> <name> <x> <y> [os]            cable <dev> <port> <dev> <port> [cable] [length]
//   ip <dev> <ip/prefix> [gateway] [dns]      dhcp <dev>        wifi <dev> <ssid> [key]
//   <dev>: <router or switch command>          (typed at the device's command line, in order)
//   sh <dev>: <command>                        (typed in a PC's terminal; sudo gets the lab password)
//   file <dev> <path>  then lines starting with "| " (the file's text)
//   ap <dev> ssid=.. security=open|wpa2 key=..     home <dev> lan=.. dhcp=on|off start=.. wan=dhcp|static ...
//   dns <server> <name> <ip>    web <server> on|off [page text]    dhcpd <server> <pool> <net/prefix> <gw> <dns> <start> <max>
//   smb <dev> <share> [file...]   firewall <dev> on|off|allow-ping   ufw <dev> on|off|allow <port>   service <dev> apache2|ssh on|off
//   printer <pc> <name> <address>   print <pc> <printer>     sfp <dev> <port> mmf|smf|rj45|none   os <dev> <id>
//   place <dev> <building/floor> <x> <y>   rack <dev> <rack id> <u>   newrack <id> <building/floor> <x> <y> <name>
//   drawings (views: logical, campus, building/floor):  area <view> x y w h #colour title...
//   shape <kind> <view> x y w h [fill= stroke= width= text=a_b size= above=yes ...]   arrow <view> x1 y1 x2 y2 [text]
//   object <art> <view> x y [size] [text]   label <dev> text...   cablelabel <dev> <dev> text...   map <view> <style>
//   power <dev> on|off   shutdown <dev> <port>   name <dev> <new name>   uncable <dev> <port>   run <ms>   note <x> <y> <text>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public static class Script
    {
        /// <summary>Runs a script. Returns the problems (empty = everything worked).</summary>
        public static List<string> Run(Project p, string script)
        {
            var errors = new List<string>();
            var lines = script.Replace("\r", "").Split('\n');
            var sessions = new Dictionary<Device, CliSession>();
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                try
                {
                    // a file's text follows on lines starting with "| "
                    if (line.StartsWith("file "))
                    {
                        var w0 = Shell.Split(line);
                        var d0 = Dev(p, w0[1]);
                        var sb = new StringBuilder();
                        while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("|")) { i++; string t = lines[i].TrimStart(); sb.Append(t.Length > 2 ? t.Substring(2) : "").Append('\n'); }
                        d0.Host.Files[w0[2]] = sb.ToString();
                        continue;
                    }
                    string err = One(p, line, sessions);
                    if (err.Length > 0) errors.Add("line " + (i + 1) + " (" + line + "): " + err);
                }
                catch (Exception ex) { errors.Add("line " + (i + 1) + " (" + line + "): " + ex.Message); }
            }
            foreach (var s in sessions.Values) s.D.Ios.Sessions.Remove(s);
            p.Sim.TopologyChanged();
            return errors;
        }

        static Device Dev(Project p, string name)
        {
            var d = p.Find(name);
            if (d == null) throw new Exception("no device called " + name);
            return d;
        }

        static string One(Project p, string line, Dictionary<Device, CliSession> sessions)
        {
            int colon = line.IndexOf(": ");
            var first = line.Split(' ')[0];
            // "R1: command" (router / switch) and "sh PC1: command" (end device terminal)
            if (colon > 0 && !first.Contains("=") && (first.EndsWith(":") || line.StartsWith("sh ")))
            {
                bool shell = line.StartsWith("sh ");
                string dn = shell ? line.Substring(3, colon - 3).Trim() : line.Substring(0, colon).Trim();
                string cmd = line.Substring(colon + 2);
                var d = Dev(p, dn);
                if (shell) return Sh(p, d, cmd);
                if (d.Ios == null) return d.Name + " has no command line";
                CliSession s;
                if (!sessions.TryGetValue(d, out s)) { s = new CliSession(d) { Started = true }; sessions[d] = s; }
                int before = s.Errors;
                s.Execute(cmd);
                // answer questions (copy run start, reload...) with Enter
                for (int g = 0; g < 3 && s.Pending != null; g++) s.Execute("");
                string o = s.TakeOutput();
                if (s.Errors > before) return "the device said: " + o.Trim();
                return "";
            }
            var w = Shell.Split(line);
            string op = w[0].ToLowerInvariant();
            Func<int, string> W = k => k < w.Count ? w[k] : "";
            switch (op)
            {
                case "add":
                    {
                        var d = p.Add(W(1), Num(W(3)), Num(W(4)), W(2));
                        if (W(5).Length > 0 && d.Host != null) Personalities.Change(d, W(5));
                        return "";
                    }
                case "cable":
                    {
                        var a = Dev(p, W(1)); var b = Dev(p, W(3));
                        var pa = a.Port(W(2)); var pb = b.Port(W(4));
                        if (pa == null) return "no port " + W(2) + " on " + a.Name;
                        if (pb == null) return "no port " + W(4) + " on " + b.Name;
                        string cable = W(5).Length > 0 ? W(5) : "auto";
                        var l = p.Connect(pa, pb, cable);
                        if (W(6).Length > 0) { l.Length = Num(W(6)); l.ManualLength = true; p.Sim.TopologyChanged(); }
                        return "";
                    }
                case "uncable":
                    {
                        var pt = Dev(p, W(1)).Port(W(2));
                        if (pt == null || pt.Link == null) return "no cable there";
                        p.Disconnect(pt.Link);
                        return "";
                    }
                case "ip":
                    {
                        var d = Dev(p, W(1));
                        var f = AdapterOf(d, W(5));
                        string cidr = W(2); int s = cidr.IndexOf('/');
                        string mask = s > 0 ? cidr.Substring(s + 1) : "24";
                        string ip = s > 0 ? cidr.Substring(0, s) : cidr;
                        string gw = W(3) == "-" ? "" : W(3);
                        string e = d.Host.SetStatic(f, ip, mask, gw, W(4) == "-" ? "" : W(4), "");
                        p.Sim.TopologyChanged();
                        return e;
                    }
                case "dhcp": { var d = Dev(p, W(1)); d.Host.SetDhcp(AdapterOf(d, W(2))); return ""; }
                case "wifi":
                    {
                        var d = Dev(p, W(1));
                        var port = d.Ports.FirstOrDefault(x => x.IsWireless);
                        if (port == null) return d.Name + " has no Wi-Fi";
                        d.Host.SetWifi(port, W(2), W(3));
                        var f = port.L3;
                        if (!f.Dhcp && f.Ip == 0) d.Host.SetDhcp(f); else if (f.Dhcp) d.Host.EnsureDhcp(f);
                        return "";
                    }
                case "ap":
                    {
                        var d = Dev(p, W(1));
                        if (d.Ap == null) return d.Name + " isn't an access point";
                        foreach (var kv in Pairs(w, 2))
                        {
                            if (kv.Key == "ssid") d.Ap.Ssid = kv.Value;
                            else if (kv.Key == "security") d.Ap.Security = kv.Value;
                            else if (kv.Key == "key") d.Ap.Key = kv.Value;
                            else if (kv.Key == "range") d.Ap.Range = Num(kv.Value);
                        }
                        p.Sim.TopologyChanged();
                        return "";
                    }
                case "home":
                    {
                        var d = Dev(p, W(1));
                        var c = d.Home;
                        foreach (var kv in Pairs(w, 2))
                        {
                            switch (kv.Key)
                            {
                                case "lan": c.LanIp = kv.Value; break;
                                case "mask": c.LanMask = kv.Value; break;
                                case "dhcp": c.DhcpOn = kv.Value == "on"; break;
                                case "start": c.DhcpStart = kv.Value; break;
                                case "max": c.DhcpMax = (int)Num(kv.Value); break;
                                case "wan": c.WanMode = kv.Value; break;
                                case "wanip": c.WanIp = kv.Value; break;
                                case "wanmask": c.WanMask = kv.Value; break;
                                case "wangw": c.WanGw = kv.Value; break;
                                case "wandns": c.WanDns = kv.Value; break;
                                case "ssid": d.Ap.Ssid = kv.Value; break;
                                case "security": d.Ap.Security = kv.Value; break;
                                case "key": d.Ap.Key = kv.Value; break;
                            }
                        }
                        string e = HomeRouter.Apply(d);
                        p.Sim.TopologyChanged();
                        return e;
                    }
                case "dns":
                    {
                        var d = Dev(p, W(1));
                        if (d.Ip.DnsSrv == null) d.Ip.DnsSrv = new DnsServer(d.Ip);
                        if (W(2) == "on" || W(2) == "off") { d.Ip.DnsSrv.Enabled = W(2) == "on"; return ""; }
                        d.Ip.DnsSrv.Records[W(2)] = Ip.Parse(W(3));
                        return "";
                    }
                case "web":
                    {
                        var d = Dev(p, W(1));
                        d.Host.HttpOn = W(2) != "off";
                        if (W(3).Length > 0) d.Host.Pages["index.html"] = string.Join(" ", w.Skip(3).ToArray());
                        else if (!d.Host.Pages.ContainsKey("index.html")) d.Host.Pages["index.html"] = "<h1>" + d.Name + "</h1><p>Hello from " + d.Name + "'s web server.</p>";
                        return "";
                    }
                case "dhcpd":
                    {
                        var d = Dev(p, W(1));
                        if (d.Ip.Dhcp == null) d.Ip.Dhcp = new DhcpServer(d.Ip);
                        if (W(2) == "on" || W(2) == "off") { d.Ip.Dhcp.Enabled = W(2) == "on"; return ""; }
                        var pool = d.Ip.Dhcp.Pool(W(2), true);
                        string cidr = W(3); int s = cidr.IndexOf('/');
                        pool.Network = Ip.Parse(cidr.Substring(0, s)); Ip.TryParseMask(cidr.Substring(s + 1), out pool.Mask);
                        pool.Router = W(4) == "-" ? 0 : Ip.Parse(W(4));
                        pool.Dns.Clear(); if (W(5).Length > 0 && W(5) != "-") pool.Dns.Add(Ip.Parse(W(5)));
                        pool.Start = W(6).Length > 0 ? Ip.Parse(W(6)) : 0;
                        pool.Max = W(7).Length > 0 ? (int)Num(W(7)) : 0;
                        return "";
                    }
                case "smb":
                    {
                        var d = Dev(p, W(1));
                        d.Host.SmbOn = true;
                        d.Host.Shares.RemoveAll(x => x.Name == W(2));
                        d.Host.Shares.Add(new SharedFolder { Name = W(2), Files = w.Skip(3).ToList() });
                        return "";
                    }
                case "firewall":
                    {
                        var d = Dev(p, W(1));
                        if (W(2) == "allow-ping") d.Host.FwSharing = true;
                        else if (W(2) == "allow-web") d.Host.FwWeb = true;
                        else d.Host.WinFirewall = W(2) == "on";
                        return "";
                    }
                case "ufw":
                    {
                        var d = Dev(p, W(1));
                        if (W(2) == "allow" || W(2) == "deny") { int port = (int)Num(W(3)); d.Host.UfwRules.RemoveAll(r => r.Port == port); d.Host.UfwRules.Add(new FwRule { Port = port, Proto = "any", Allow = W(2) == "allow" }); }
                        else d.Host.Ufw = W(2) == "on";
                        return "";
                    }
                case "service":
                    {
                        var d = Dev(p, W(1));
                        bool on = W(3) != "off";
                        if (W(2) == "apache2" || W(2) == "http") d.Host.HttpOn = on;
                        else if (W(2) == "ssh") d.Host.SshOn = on;
                        else if (W(2) == "print") d.Host.PrintOn = on;
                        else return "unknown service " + W(2);
                        return "";
                    }
                case "printer":
                    {
                        var d = Dev(p, W(1));
                        d.Host.Printers.RemoveAll(x => x[0] == W(2));
                        d.Host.Printers.Add(new[] { W(2), W(3) });
                        return "";
                    }
                case "print":
                    {
                        var d = Dev(p, W(1));
                        var pr = d.Host.Printers.FirstOrDefault(x => x[0] == W(2));
                        string addr = pr != null ? pr[1] : W(2);
                        bool done = false, ok = false;
                        Request.Print(d, addr, "Test page from " + d.Name, (o, e) => { done = true; ok = o; });
                        p.Sim.RunUntil(p.Sim.Now + 15000, () => done);
                        return ok ? "" : "the page didn't print";
                    }
                case "sfp":
                    {
                        var pt = Dev(p, W(1)).Port(W(2));
                        if (pt == null || pt.Media != "sfp") return "no SFP slot " + W(2);
                        pt.Sfp = W(3) == "none" ? "" : W(3);
                        p.Sim.TopologyChanged();
                        return "";
                    }
                case "os": { Personalities.Change(Dev(p, W(1)), W(2)); return ""; }
                case "place":
                    {
                        var d = Dev(p, W(1));
                        if (p.Layout.Floor(W(2)) == null) return "no floor " + W(2);
                        d.Place = W(2); d.PX = Num(W(3)); d.PY = Num(W(4)); d.Rack = "";
                        p.Sim.TopologyChanged();
                        return "";
                    }
                case "newrack":
                    {
                        p.Racks.Add(new Rack { Id = W(1), Place = W(2), PX = Num(W(3)), PY = Num(W(4)), Name = W(5).Length > 0 ? string.Join(" ", w.Skip(5).ToArray()) : W(1) });
                        return "";
                    }
                case "rack":
                    {
                        var d = Dev(p, W(1));
                        var r = p.Racks.FirstOrDefault(x => x.Id == W(2));
                        if (r == null) return "no rack " + W(2);
                        d.Rack = r.Id; d.RackU = (int)Num(W(3)); d.Place = r.Place; d.PX = r.PX; d.PY = r.PY;
                        p.Sim.TopologyChanged();
                        return "";
                    }
                case "power": { var d = Dev(p, W(1)); d.PoweredOn = W(2) != "off"; p.Sim.TopologyChanged(); return ""; }
                case "shutdown": { var pt = Dev(p, W(1)).Port(W(2)); pt.Shutdown = W(3) != "no"; p.Sim.TopologyChanged(); return ""; }
                case "name": { var d = Dev(p, W(1)); d.Name = W(2); return ""; }
                case "hostname": { var d = Dev(p, W(1)); if (d.Host != null) d.Host.ComputerName = W(2); return ""; }
                case "password": { var d = Dev(p, W(1)); d.Host.Password = W(2); return ""; }
                case "run": p.Sim.RunFor((long)Num(W(1))); return "";
                case "note": p.Notes.Add(new Note { Id = p.NewId("n"), X = Num(W(1)), Y = Num(W(2)), Text = string.Join(" ", w.Skip(3).ToArray()) }); return "";
                case "layout": { p.Layout = Layouts.Create(W(1)); if (W(2).Length > 0) p.Layout.ActiveFloor = W(2); return ""; }
                case "activefloor": p.Layout.ActiveFloor = W(1); return "";
                case "range": { var d = Dev(p, W(1)); d.Ap.Range = Num(W(2)); p.Sim.TopologyChanged(); return ""; }
                // ---- drawings (decoration only): area, shape, arrow, object, label, cablelabel, map
                case "area":
                    {
                        // area <view> x y w h <colour> <title...>
                        var s = Drawings.Make(p, "area", W(1), Num(W(2)), Num(W(3)), Num(W(4)), Num(W(5)));
                        if (W(6).StartsWith("#")) { s.Fill = W(6); s.Stroke = W(6); s.TextColor = DrawColor.Lighter(W(6), 0.55); }
                        s.Text = string.Join(" ", w.Skip(7).ToArray());
                        p.Shapes.Add(s); return "";
                    }
                case "shape":
                    {
                        // shape <kind> <view> x y w h [fill=.. stroke=.. width=.. text=.. above=yes rotation=.. size=.. dash=..]
                        var s = Drawings.Make(p, W(1), W(2), Num(W(3)), Num(W(4)), Num(W(5)), Num(W(6)));
                        if (s.IsLine) { s.Points.Add(new Point2(s.X, s.Y)); s.Points.Add(new Point2(s.X + s.W, s.Y + s.H)); s.FitPoints(); }
                        foreach (var kv in Pairs(w, 7)) ShapeOption(s, kv.Key, kv.Value);
                        p.Shapes.Add(s); return "";
                    }
                case "arrow":
                    {
                        // arrow <view> x1 y1 x2 y2 [text...]
                        var s = Drawings.Make(p, "arrow", W(1), 0, 0, 0, 0);
                        s.Points.Add(new Point2(Num(W(2)), Num(W(3)))); s.Points.Add(new Point2(Num(W(4)), Num(W(5)))); s.FitPoints();
                        s.Text = string.Join(" ", w.Skip(6).ToArray());
                        p.Shapes.Add(s); return "";
                    }
                case "object":
                    {
                        // object <art> <view> x y [size] [text...]
                        var s = Drawings.MakeObject(p, W(1), W(2), Num(W(3)), Num(W(4)));
                        double size = Num(W(5));
                        if (size > 0) { double k = size / Math.Max(s.W, s.H); double cx = s.CX, cy = s.CY; s.W *= k; s.H *= k; s.X = cx - s.W / 2; s.Y = cy - s.H / 2; }
                        s.Text = string.Join(" ", w.Skip(6).ToArray());
                        p.Shapes.Add(s); return "";
                    }
                case "label":
                    {
                        // label <device> <text...>
                        var d = Dev(p, W(1));
                        var c = new Callout { Id = p.NewId("c"), Target = d.Id, Text = string.Join(" ", w.Skip(2).ToArray()) };
                        p.Callouts.Add(c); return "";
                    }
                case "cablelabel":
                    {
                        // cablelabel <device> <device> <text...>
                        var a = Dev(p, W(1)); var b = Dev(p, W(2));
                        var l = p.Links.FirstOrDefault(x => (x.A.Dev == a && x.B.Dev == b) || (x.A.Dev == b && x.B.Dev == a));
                        if (l == null) return "no cable between " + a.Name + " and " + b.Name;
                        p.Callouts.Add(new Callout { Id = p.NewId("c"), Target = l.Id, Text = string.Join(" ", w.Skip(3).ToArray()), DY = -18 });
                        return "";
                    }
                case "map":
                    {
                        // map <view> <style>
                        string view = W(1);
                        Rect ext = view == "campus" ? new Rect(0, 0, p.Layout.CampusW, p.Layout.CampusH) : new Rect(Num(W(3)), Num(W(4)), Num(W(5)) > 0 ? Num(W(5)) : 1200, Num(W(6)) > 0 ? Num(W(6)) : 800);
                        Drawings.ApplyMapStyle(p, null, view, W(2), ext);
                        return "";
                    }
            }
            return "unknown action " + w[0];
        }

        static void ShapeOption(Shape s, string k, string v)
        {
            switch (k)
            {
                case "fill": s.Fill = v; break;
                case "stroke": s.Stroke = v; break;
                case "width": s.StrokeWidth = Num(v); break;
                case "text": s.Text = v.Replace('_', ' '); break;
                case "color": s.TextColor = v; break;
                case "size": s.FontSize = Num(v); break;
                case "dash": s.Dash = v; break;
                case "rotation": s.Rotation = Num(v); break;
                case "corner": s.Corner = Num(v); break;
                case "opacity": s.Opacity = Num(v); break;
                case "above": s.Above = v == "yes" || v == "true"; break;
                case "locked": s.Locked = v == "yes" || v == "true"; break;
                case "bold": s.Bold = v == "yes" || v == "true"; break;
            }
        }

        static IpIf AdapterOf(Device d, string name)
        {
            if (name.Length > 0) { var f = Personalities.Get(d.Os).AdapterByName(d, name); if (f != null) return f; var pt = d.Port(name); if (pt != null && pt.L3 != null) return pt.L3; }
            var a = d.Host.Adapters;
            return a.FirstOrDefault(x => !x.Port.IsWireless) ?? a.First();
        }

        static IEnumerable<KeyValuePair<string, string>> Pairs(List<string> w, int from)
        {
            for (int i = from; i < w.Count; i++)
            {
                int eq = w[i].IndexOf('=');
                if (eq > 0) yield return new KeyValuePair<string, string>(w[i].Substring(0, eq).ToLowerInvariant(), w[i].Substring(eq + 1));
            }
        }

        static double Num(string s) { double d; double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d); return d; }

        /// <summary>Types a command in an end device's terminal and runs the network until it finishes.</summary>
        public static string Sh(Project p, Device d, string cmd)
        {
            var pers = Personalities.Get(d.Os);
            var s = pers.NewShell(d) ?? new WinShell(d, false);
            if (cmd.StartsWith("ps ")) { var ws = s as WinShell; if (ws != null) { ws.PowerShell = true; cmd = cmd.Substring(3); } }
            s.Execute(cmd);
            for (int g = 0; g < 50; g++)
            {
                if (s.Pending != null) { s.Execute(s.PendingSecret ? d.Host.Password : "yes"); continue; }
                if (s.EditorFile != null) { s.CloseEditor(s.EditorText, false); continue; }
                if (s.Busy == null) break;
                var job = s.Busy;
                p.Sim.RunUntil(p.Sim.Now + 20000, () => s.Busy == null);
                if (s.Busy == job) s.Break();
            }
            Last = s.TakeOutput();
            return "";
        }
        /// <summary>What the last "sh" command printed (tests read it).</summary>
        public static string Last = "";

        /// <summary>A line of a script in plain words, for "Show me the answer".</summary>
        public static string Describe(string line)
        {
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) return t.TrimStart('#').Trim();
            int colon = t.IndexOf(": ");
            var w = Shell.Split(t);
            if (colon > 0 && (w[0].EndsWith(":") || t.StartsWith("sh "))) return t.StartsWith("sh ") ? "On " + t.Substring(3, colon - 3).Trim() + "'s command line, type:  " + t.Substring(colon + 2) : "On " + t.Substring(0, colon) + "'s CLI, type:  " + t.Substring(colon + 2);
            Func<int, string> W = k => k < w.Count ? w[k] : "";
            switch (w[0])
            {
                case "add": return "Add a " + Catalog.KindName(W(1)) + " called " + W(2) + ".";
                case "cable": return "Connect " + W(1) + " " + W(2) + " to " + W(3) + " " + W(4) + (W(5).Length > 0 && W(5) != "auto" ? " with a " + new Link { Cable = W(5) }.CableName.ToLowerInvariant() + " cable" : "") + ".";
                case "ip": return "Give " + W(1) + " the address " + W(2) + (W(3).Length > 0 && W(3) != "-" ? ", default gateway " + W(3) : "") + (W(4).Length > 0 && W(4) != "-" ? ", DNS server " + W(4) : "") + " (IP Configuration, Static).";
                case "dhcp": return "Set " + W(1) + " to get its address automatically (DHCP).";
                case "wifi": return "Join " + W(1) + " to the Wi-Fi network " + W(2) + (W(3).Length > 0 ? " with the password " + W(3) : "") + ".";
                case "dns": return W(2) == "on" || W(2) == "off" ? "Turn " + W(1) + "'s DNS service " + W(2) + "." : "On " + W(1) + ", add a DNS record: " + W(2) + " = " + W(3) + ".";
                case "web": return "Turn the web server (HTTP) on " + W(1) + " " + (W(2) == "off" ? "off" : "on") + ".";
                case "dhcpd": return W(2) == "on" || W(2) == "off" ? "Turn " + W(1) + "'s DHCP service " + W(2) + "." : "On " + W(1) + ", set up the DHCP pool " + W(2) + ": network " + W(3) + ", gateway " + W(4) + ", DNS " + W(5) + ", first address " + W(6) + ".";
                case "firewall": return W(2) == "allow-ping" ? "On " + W(1) + ", allow 'File and printer sharing' (pings) through the firewall." : "Turn " + W(1) + "'s firewall " + W(2) + ".";
                case "printer": return "On " + W(1) + ", add the printer " + W(2) + " at " + W(3) + " (Printers & scanners > Add a printer by IP).";
                case "print": return "On " + W(1) + ", print a test page to " + W(2) + ".";
                case "sfp": return "Put a " + (W(3) == "mmf" ? "multimode fibre" : W(3) == "smf" ? "single-mode fibre" : "copper") + " module in " + W(1) + " " + W(2) + ".";
                case "place": return "Move " + W(1) + " to " + W(2) + ".";
                case "rack": return "Mount " + W(1) + " in rack " + W(2) + " at U" + W(3) + ".";
                case "ap": case "home": return "In " + W(1) + "'s Config tab, set " + string.Join(", ", w.Skip(2).ToArray()) + ".";
                case "service": return "Start the " + W(2) + " service on " + W(1) + ".";
                case "os": return "Give " + W(1) + " the " + Personalities.Title(W(2)) + " system.";
                case "uncable": return "Unplug the cable from " + W(1) + " " + W(2) + ".";
                case "power": return "Switch " + W(1) + " " + W(2) + ".";
                case "file": return "Edit the file " + W(2) + " on " + W(1) + " so it says:";
            }
            if (t.StartsWith("|")) return "    " + (t.Length > 2 ? t.Substring(2) : "");
            return t;
        }
    }

    /// <summary>Questions the labs and tests ask a network. Each runs the simulation as long as it needs.</summary>
    public static class Check
    {
        public static bool CanPing(Project p, string from, string to)
        {
            var d = p.Find(from);
            if (d == null) return false;
            string target = to;
            var td = p.Find(to);
            if (td != null) { var f = td.Ip.Ifs.FirstOrDefault(x => x.Ip != 0 && x.Up && !x.IsLoopback); if (f == null) return false; target = Ip.Str(f.Ip); }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool done = false; int got = 0;
                PingJob.Start(d, target, 3, j => { j.IntervalMs = 0; j.OnDone = jj => { done = true; got = jj.Received; }; });
                p.Sim.RunUntil(p.Sim.Now + 30000, () => done);
                if (got > 0) return true;
            }
            return false;
        }

        public static string Resolve(Project p, string from, string name)
        {
            var d = p.Find(from);
            uint res = 0; bool done = false;
            if (d.Host != null) d.Host.DnsCache.Clear();
            Resolver.Resolve(d, name, (ip, err, srv) => { res = ip; done = true; });
            p.Sim.RunUntil(p.Sim.Now + 15000, () => done);
            return res == 0 ? "" : Ip.Str(res);
        }

        public static TextMsg Web(Project p, string from, string url)
        {
            var d = p.Find(from);
            TextMsg res = null; bool done = false;
            for (int attempt = 0; attempt < 2 && res == null; attempt++)
            {
                done = false;
                Request.Http(d, url, "GET", null, (m, err) => { res = m; done = true; });
                p.Sim.RunUntil(p.Sim.Now + 20000, () => done);
            }
            return res;
        }
        public static bool WebOk(Project p, string from, string url) { var m = Web(p, from, url); return m != null && m.Line.StartsWith("200"); }

        public static bool HasRoute(Project p, string dev, string cidr)
        {
            var d = p.Find(dev);
            int s = cidr.IndexOf('/');
            uint net = Ip.Parse(cidr.Substring(0, s)), mask = Ip.MaskOf(int.Parse(cidr.Substring(s + 1)));
            return d.Ip.Routes().Any(r => r.Net == net && r.Mask == mask);
        }

        public static string Address(Project p, string dev)
        {
            var d = p.Find(dev);
            var f = d.Host != null ? d.Host.Main : d.Ip.Ifs.FirstOrDefault(x => x.Ip != 0);
            return f == null || f.Ip == 0 ? "" : Ip.Str(f.Ip);
        }

        public static bool LinkUp(Project p, string a, string b)
        {
            var da = p.Find(a); var db = p.Find(b);
            if (da == null || db == null) return false;
            foreach (var pt in da.Ports)
                foreach (var l in pt.Links)
                {
                    if (l.Status != "up") continue;
                    var far = l.IsWireless ? l.Other(pt) : Wiring.FarEnd(l.Other(pt));
                    if (far != null && far.Dev == db) return true;
                }
            return false;
        }

        /// <summary>Settles a copy: addresses from DHCP, routes from RIP, Wi-Fi, so checks see a calm network.</summary>
        public static Project Settled(Project p, long ms)
        {
            var c = Persist.Clone(p);
            c.Sim.RunFor(ms);
            return c;
        }
    }
}
