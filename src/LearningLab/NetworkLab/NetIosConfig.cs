// Network Lab - the Config tab of routers and switches (App Hub's IosConfig.cs): simple forms that type the matching
// commands for you and show them under "Equivalent commands", so learners see the command line behind every tap.
// Sections: Settings (hostname, save, restart, the running configuration), static routes and RIP (routers), the VLAN
// database and Vlan1 (switches), and Interfaces (pick a port, then its settings).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

public sealed class IosConfigTab : ContentControl
{
    readonly DevicePage W;
    readonly Device D;
    readonly CliSession session;
    readonly TextBox log;
    readonly Action<string> show;
    readonly Func<string> current;
    public string Section => current();
    public void Show(string name) => show(name);
    /// <summary>What the forms typed, as on the router's own prompt.</summary>
    public string Log => log.Text ?? "";
    /// <summary>The checks: the boxes and buttons of the section on screen, by name.</summary>
    public readonly Dictionary<string, TextBox> Boxes = new();
    public readonly Dictionary<string, Action> Actions = new();
    public TextBlock Message { get; private set; }
    public ComboBox PortPicker { get; private set; }
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    public IosConfigTab(DevicePage w, Device d)
    {
        W = w; D = d;
        session = new CliSession(d) { Mode = "config", Started = true };
        d.Ios.Sessions.Remove(session);   // a form's session isn't a line someone is logged in on
        log = new TextBox { IsReadOnly = true, FontFamily = Mono, FontSize = 13, Height = 120, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, Margin = new Thickness(0, 4, 0, 0) };
        var head = Ui.Text("Equivalent commands (what these forms typed for you):", 13, Ui.AccentText, true);
        var bottom = new StackPanel { Margin = new Thickness(12, 4, 12, 10), Children = { head, log } };
        var sections = new List<(string, Func<Control>)> { ("Settings", Settings) };
        if (d.Kind == "router") { sections.Add(("Static routes", Static)); sections.Add(("RIP", Rip)); }
        if (d.Kind == "switch") { sections.Add(("VLANs", Vlans)); sections.Add(("Vlan1 (address)", Svi)); }
        sections.Add(("Interfaces", Interfaces));
        var (view, sh, cur) = Dk.Sections(sections, bottom);
        show = sh; current = cur;
        Content = view;
    }

    /// <summary>Types the commands in configuration mode; returns the error lines (empty when all went in).</summary>
    public string Run(params string[] cmds)
    {
        var errs = new List<string>();
        foreach (var c in cmds)
        {
            string prompt = session.Prompt;
            session.Execute(c);
            for (int g = 0; g < 3 && session.Pending != null; g++) session.Execute("");
            string o = session.TakeOutput();
            log.Text = (log.Text ?? "") + prompt + c + "\n";
            foreach (var line in o.Split('\n')) if (line.StartsWith("%") && !line.StartsWith("%LINK") && !line.StartsWith("%LINEPROTO")) errs.Add(line);
        }
        session.Mode = "config"; session.If = null; session.Range = null;
        log.CaretIndex = log.Text?.Length ?? 0;
        DevicePage.Changed();
        return string.Join("\n", errs);
    }

    TextBox Field(Panel p, string key, string label, string value, double w = 200, string tip = null) { var t = Dk.Field(p, label, value, w, tip); Boxes[key] = t; return t; }
    Button Act(string key, string text, Action a, bool primary = false) { Actions[key] = a; return Dk.Btn(text, a, primary); }
    TextBlock NewMsg() { Boxes.Clear(); Actions.Clear(); Message = Dk.Msg(); return Message; }

    Control Settings()
    {
        var m = NewMsg();
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Global settings"));
        var host = Field(sp, "hostname", "Hostname", D.Ios.Hostname, 220, "The name in the command prompt (Router>, Switch#).");
        sp.Children.Add(Act("hostname", "Set hostname", () => m.Text = Run("hostname " + (host.Text ?? "").Trim()), true));
        sp.Children.Add(m);
        sp.Children.Add(Dk.Head("Saved configuration (NVRAM)"));
        sp.Children.Add(Dk.Note("The running configuration is lost when the device restarts unless it's saved."));
        var cfg = new TextBox { IsReadOnly = true, FontFamily = Mono, FontSize = 13, TextWrapping = TextWrapping.NoWrap, Height = 260, AcceptsReturn = true, Text = Persist.RunningConfig(D) };
        sp.Children.Add(new WrapPanel
        {
            Children =
            {
                Act("save", "Save", () => m.Text = Run("do write memory")),
                Act("erase", "Erase the saved one", () => m.Text = Run("do erase startup-config")),
                Act("reload", "Restart (reload)", () => { Persist.Reload(D); DevicePage.Changed(); cfg.Text = Persist.RunningConfig(D); }),
            },
        });
        sp.Children.Add(Dk.Head("Running configuration"));
        sp.Children.Add(Act("refresh", "Refresh", () => cfg.Text = Persist.RunningConfig(D)));
        sp.Children.Add(cfg);
        return sp;
    }

    Control Static()
    {
        var m = NewMsg();
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Static routes"));
        sp.Children.Add(Dk.Note("A static route tells the router: to reach this network, send packets to that next router."));
        var net = Field(sp, "network", "Network", "", 180, "The far network, for example 192.168.3.0");
        var mask = Field(sp, "mask", "Mask", "255.255.255.0", 180);
        var hop = Field(sp, "next hop", "Next hop", "", 180, "The neighbour router's address on a shared link.");
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            foreach (var r in D.Ip.Static.ToList())
            {
                string cmd = "ip route " + Ip.Str(r.Net) + " " + Ip.Str(r.Mask) + (r.ExitIf.Length > 0 ? " " + r.ExitIf : "") + (r.NextHop != 0 ? " " + Ip.Str(r.NextHop) : "");
                items.Children.Add(Dk.Row(Dk.Mono(cmd), Dk.Btn("Remove", () => { m.Text = Run("no " + cmd); Fill(); })));
            }
            if (D.Ip.Static.Count == 0) items.Children.Add(Dk.Note("No static routes yet."));
        }
        sp.Children.Add(Act("add", "Add", () => { m.Text = Run("ip route " + (net.Text ?? "").Trim() + " " + (mask.Text ?? "").Trim() + " " + (hop.Text ?? "").Trim()); Fill(); }, true));
        sp.Children.Add(m);
        sp.Children.Add(Dk.Head("Routes"));
        sp.Children.Add(items);
        Fill();
        return sp;
    }

    Control Rip()
    {
        var m = NewMsg();
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("RIP"));
        sp.Children.Add(Dk.Note("RIP lets routers tell each other which networks they can reach. Add each network this router is connected to."));
        var v2 = new CheckBox { Content = "Version 2 (sends the subnet masks; use this)", IsChecked = D.Ip.Rip != null && D.Ip.Rip.Version == 2, MinHeight = 44 };
        sp.Children.Add(v2);
        var net = Field(sp, "network", "Network", "", 180, "A network to share, for example 10.0.0.0 or 192.168.1.0");
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            if (D.Ip.Rip != null)
                foreach (var n in D.Ip.Rip.Networks.ToList())
                {
                    var nn = n;
                    items.Children.Add(Dk.Row(Dk.Mono("network " + Ip.Str(n)), Dk.Btn("Remove", () => { m.Text = Run("router rip", "no network " + Ip.Str(nn)); Fill(); })));
                }
            if (D.Ip.Rip == null || D.Ip.Rip.Networks.Count == 0) items.Children.Add(Dk.Note("RIP shares no networks yet."));
        }
        v2.IsCheckedChanged += (s, e) => Lt.Safe(() => m.Text = Run("router rip", v2.IsChecked == true ? "version 2" : "no version", v2.IsChecked == true ? "no auto-summary" : "auto-summary"));
        sp.Children.Add(Act("add", "Add", () => { m.Text = Run("router rip", "network " + (net.Text ?? "").Trim()); Fill(); }, true));
        sp.Children.Add(m);
        sp.Children.Add(items);
        Fill();
        return sp;
    }

    Control Vlans()
    {
        var m = NewMsg();
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("VLAN database"));
        sp.Children.Add(Dk.Note("A VLAN splits one switch into separate networks. Ports join a VLAN in their own settings (Interfaces, Access VLAN)."));
        var num = Field(sp, "number", "VLAN number", "", 100, "2 to 1001");
        var name = Field(sp, "name", "VLAN name", "", 200);
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            foreach (var kv in D.Bridge.Vlans.ToList())
            {
                var v = kv.Key;
                items.Children.Add(v > 1 && v < 1002 ? Dk.Row(Dk.Mono(v.ToString().PadRight(6) + kv.Value), Dk.Btn("Remove", () => { m.Text = Run("no vlan " + v); Fill(); })) : Dk.Row(Dk.Mono(v.ToString().PadRight(6) + kv.Value)));
            }
        }
        sp.Children.Add(Act("add", "Add", () =>
        {
            var cmds = new List<string> { "vlan " + (num.Text ?? "").Trim() };
            if ((name.Text ?? "").Trim().Length > 0) cmds.Add("name " + name.Text.Trim().Replace(" ", "_"));
            m.Text = Run(cmds.ToArray()); Fill();
        }, true));
        sp.Children.Add(m);
        sp.Children.Add(items);
        Fill();
        return sp;
    }

    Control Interfaces()
    {
        var m = NewMsg();
        var ports = D.Ports.Where(x => !x.IsSerial).ToList();
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Interfaces"));
        PortPicker = new ComboBox { MinWidth = 260, MinHeight = 44, ItemsSource = ports.Select(p => p.Name + (p.Links.Count > 0 ? "  (cable)" : "")).ToList(), SelectedIndex = Math.Max(0, ports.FindIndex(p => p.Links.Count > 0)), Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new StackPanel { Spacing = 3, Children = { Ui.Text("Port", 13, Ui.Muted), PortPicker } });
        var form = new ContentControl();
        sp.Children.Add(form);
        sp.Children.Add(m);
        void ShowPort() { if (PortPicker.SelectedIndex >= 0) form.Content = Iface(ports[PortPicker.SelectedIndex], m); }
        PortPicker.SelectionChanged += (s, e) => Lt.Safe(ShowPort);
        ShowPort();
        return sp;
    }

    Control Iface(Port p, TextBlock m)
    {
        m.Text = "";
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head(p.Name));
        var status = Dk.Note("");
        sp.Children.Add(status);
        var on = new CheckBox { Content = "Port on (no shutdown)", IsChecked = !p.Shutdown, MinHeight = 44 };
        on.IsCheckedChanged += (s, e) => Lt.Safe(() => m.Text = Run("interface " + p.Name, on.IsChecked == true ? "no shutdown" : "shutdown"));
        sp.Children.Add(on);
        Actions["port on"] = () => on.IsChecked = true;
        if (D.Kind == "router")
        {
            var f = p.L3;
            var ip = Field(sp, "ip", "IP address", Lt.IpOrEmpty(f.Ip), 180);
            var mask = Field(sp, "mask", "Subnet mask", Lt.IpOrEmpty(f.Mask), 180);
            sp.Children.Add(Act("set address", "Set address", () => m.Text = Run((ip.Text ?? "").Trim().Length == 0 ? new[] { "interface " + p.Name, "no ip address" } : new[] { "interface " + p.Name, "ip address " + ip.Text.Trim() + " " + (mask.Text ?? "").Trim() }), true));
        }
        else
        {
            var mode = new ComboBox { MinWidth = 240, MinHeight = 44, ItemsSource = new[] { "Access (one VLAN)", "Trunk (all VLANs, tagged)" }, SelectedIndex = p.Trunk ? 1 : 0 };
            sp.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { Ui.Text("Mode", 13, Ui.Muted), mode } });
            var vl = D.Bridge.Vlans.Where(x => x.Key < 1002).ToList();
            var vlan = new ComboBox { MinWidth = 240, MinHeight = 44, ItemsSource = vl.Select(kv => kv.Key + " " + kv.Value).ToList(), SelectedIndex = vl.FindIndex(kv => kv.Key == p.AccessVlan) };
            sp.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { Ui.Text("Access VLAN", 13, Ui.Muted), vlan } });
            mode.SelectionChanged += (s, e) => Lt.Safe(() => m.Text = Run("interface " + p.Name, mode.SelectedIndex == 1 ? "switchport mode trunk" : "switchport mode access"));
            vlan.SelectionChanged += (s, e) => Lt.Safe(() => { if (vlan.SelectedIndex >= 0) m.Text = Run("interface " + p.Name, "switchport access vlan " + vl[vlan.SelectedIndex].Key); });
        }
        var desc = Field(sp, "description", "Description", D.Kind == "router" ? p.L3.Description : p.Description, 260, "A note for people, like 'to the office switch'.");
        sp.Children.Add(Act("set description", "Set description", () => m.Text = Run("interface " + p.Name, (desc.Text ?? "").Trim().Length > 0 ? "description " + desc.Text.Trim() : "no description")));
        void Upd()
        {
            var l = p.Link;
            status.Text = (p.Shutdown ? "Shut down (administratively down)." : p.IsUp ? "Up: the cable works." : l == null ? "Nothing plugged in." : "Down: " + l.Problem) + (l != null ? "  Cable to " + l.Other(p).FullName + "." : "");
        }
        Upd(); W.OnRefresh(Upd);
        return sp;
    }

    Control Svi()
    {
        var m = NewMsg();
        var f = D.Bridge.Svis[1];
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Vlan1: the switch's own address"));
        sp.Children.Add(Dk.Note("A switch doesn't need an address to switch, but with one you can ping it (and manage it). Its default gateway lets it answer from other networks."));
        var ip = Field(sp, "ip", "IP address", Lt.IpOrEmpty(f.Ip), 180);
        var mask = Field(sp, "mask", "Subnet mask", Lt.IpOrEmpty(f.Mask), 180);
        var gw = Field(sp, "gateway", "Default gateway", Lt.IpOrEmpty(D.Ip.DefaultGateway), 180);
        sp.Children.Add(Act("apply", "Apply", () =>
        {
            var c = new List<string> { "interface vlan 1", (ip.Text ?? "").Trim().Length > 0 ? "ip address " + ip.Text.Trim() + " " + (mask.Text ?? "").Trim() : "no ip address", "no shutdown", "exit" };
            if ((gw.Text ?? "").Trim().Length > 0) c.Add("ip default-gateway " + gw.Text.Trim());
            m.Text = Run(c.ToArray());
        }, true));
        sp.Children.Add(m);
        return sp;
    }
}
