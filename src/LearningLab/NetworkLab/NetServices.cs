// Network Lab - the Services tab of servers and storage boxes (App Hub's ServicesPanel.cs): the web server and its
// pages (edited right here), DNS records, DHCP pools and shared folders. Section buttons along the top, so it works the
// same on a wide screen and a phone. Everything stays in the simulation.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

/// <summary>Small pieces for the dark forms (Services, Config).</summary>
static class Dk
{
    public static TextBlock Head(string t) { var h = Ui.Text(t, 16, Ui.AccentText, true); h.Margin = new Thickness(0, 12, 0, 6); return h; }
    public static TextBlock Note(string t) { var n = Ui.Text(t, 13.5, Ui.Muted); n.Margin = new Thickness(0, 2, 0, 8); return n; }
    public static TextBlock Msg() { var m = Ui.Text("", 13.5, "#FCA5A5"); m.Margin = new Thickness(0, 2, 0, 6); return m; }
    public static TextBlock Mono(string t) => new() { Text = t, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 13, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    public static TextBox Field(Panel parent, string label, string value, double width = 220, string tip = null)
    {
        var t = new TextBox { Text = value ?? "", Width = width, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Left };
        if (!string.IsNullOrEmpty(tip)) ToolTip.SetTip(t, tip);
        parent.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { Ui.Text(label, 13, Ui.Muted), t } });
        return t;
    }

    public static Button Btn(string text, Action a, bool primary = false)
    {
        var b = Ui.Button(text, primary, 14);
        b.Margin = new Thickness(0, 0, 8, 8); b.HorizontalAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(14, 8);
        b.Click += (s, e) => Lt.Safe(a);
        return b;
    }

    /// <summary>A line of text with buttons after it (they wrap under it on a phone).</summary>
    public static Control Row(Control text, params Button[] buttons)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        text.Margin = new Thickness(0, 0, 12, 0); text.MaxWidth = 520;
        w.Children.Add(text);
        foreach (var b in buttons) w.Children.Add(b);
        return w;
    }

    /// <summary>Section buttons over the content; the chosen one is lit.</summary>
    public static (Control View, Action<string> Show, Func<string> Current) Sections(IList<(string Name, Func<Control> Make)> list, Control under = null)
    {
        var bar = new WrapPanel { Margin = new Thickness(12, 0, 12, 4) };
        var content = new ContentControl();
        string cur = null;
        void Show(string name)
        {
            var s = list.First(x => x.Name == name);
            cur = name;
            content.Content = new ScrollViewer { Content = new Border { Child = s.Make(), Padding = new Thickness(14, 4, 14, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left } };
            bar.Children.Clear();
            foreach (var x in list)
            {
                var n = x.Name;
                var b = Ui.Button(n, n == name, 14);
                b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(12, 8);
                b.Click += (o, e) => Lt.Safe(() => Show(n));
                bar.Children.Add(b);
            }
        }
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        if (under != null) { DockPanel.SetDock(under, Dock.Bottom); root.Children.Add(under); }
        root.Children.Add(content);
        Show(list[0].Name);
        return (root, Show, () => cur);
    }

    public static Control OnOff(string what, bool on, Action<bool> set)
    {
        var g = "g" + Guid.NewGuid().ToString("n");
        var a = new RadioButton { Content = "On", IsChecked = on, GroupName = g, MinHeight = 44, Margin = new Thickness(0, 0, 16, 0) };
        var b = new RadioButton { Content = "Off", IsChecked = !on, GroupName = g, MinHeight = 44 };
        a.IsCheckedChanged += (s, e) => { if (a.IsChecked == true) { set(true); DevicePage.Changed(); } };
        b.IsCheckedChanged += (s, e) => { if (b.IsChecked == true) { set(false); DevicePage.Changed(); } };
        var l = Ui.Text(what, 14, Ui.Muted); l.Width = 140; l.VerticalAlignment = VerticalAlignment.Center;
        return new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Children = { l, a, b } };
    }
}

public sealed class ServicesTab : ContentControl
{
    readonly Device D;
    readonly Action<string> show;
    readonly Func<string> current;
    public string Section => current();
    public void Show(string name) => show(name);
    /// <summary>The checks: the boxes and buttons of the section on screen, by name.</summary>
    public readonly Dictionary<string, TextBox> Boxes = new();
    public readonly Dictionary<string, Action> Actions = new();
    public TextBlock Message { get; private set; }

    public ServicesTab(Device d)
    {
        D = d;
        var (view, sh, cur) = Dk.Sections(new List<(string, Func<Control>)>
        {
            ("Web (HTTP)", Http),
            ("DNS", Dns),
            ("DHCP", Dhcp),
            ("Shared folders", Smb),
        });
        show = sh; current = cur;
        Content = view;
    }

    TextBox Field(Panel p, string key, string label, string value, double w = 220, string tip = null) { var t = Dk.Field(p, label, value, w, tip); Boxes[key] = t; return t; }
    Button Act(string key, string text, Action a, bool primary = false) { Actions[key] = a; return Dk.Btn(text, a, primary); }

    void Start() { Boxes.Clear(); Actions.Clear(); }

    Control Http()
    {
        Start();
        var h = D.Host;
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Web server (HTTP)"));
        sp.Children.Add(Dk.Note("Serves web pages to browsers on other computers: http://" + (h.Main != null && h.Main.Ip != 0 ? Ip.Str(h.Main.Ip) : "this-server's-address") + "/"));
        sp.Children.Add(Dk.OnOff("Web server", h.HttpOn, v => h.HttpOn = v));
        if (!h.Pages.ContainsKey("index.html")) h.Pages["index.html"] = "<h1>" + D.Name + "</h1><p>Welcome to " + D.Name + "'s web page.</p>";
        var items = new StackPanel();
        var editor = new StackPanel { IsVisible = false, Margin = new Thickness(0, 4, 0, 10) };
        var edTitle = Ui.Text("", 14, Ui.TextC, true);
        var edBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160, MaxHeight = 360, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 13 };
        Boxes["page"] = edBox;
        string editing = null;
        editor.Children.Add(edTitle);
        editor.Children.Add(Dk.Note("The page in HTML: h1, h2, p, b, i, a href, ul / li and simple forms work."));
        editor.Children.Add(edBox);
        void Fill()
        {
            items.Children.Clear();
            foreach (var kv in h.Pages.ToList())
            {
                var name = kv.Key;
                items.Children.Add(Dk.Row(Dk.Mono(name),
                    Dk.Btn("Edit", () => { editing = name; edTitle.Text = "Edit " + name; edBox.Text = h.Pages[name]; editor.IsVisible = true; edBox.Focus(); }),
                    Dk.Btn("Delete", () => { h.Pages.Remove(name); if (editing == name) editor.IsVisible = false; Fill(); DevicePage.Changed(); })));
            }
        }
        editor.Children.Add(new WrapPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                Act("save page", "Save the page", () => { if (editing != null) { h.Pages[editing] = edBox.Text ?? ""; editor.IsVisible = false; Fill(); DevicePage.Changed(); } }, true),
                Dk.Btn("Cancel", () => editor.IsVisible = false),
            },
        });
        Actions["edit index"] = () => { editing = "index.html"; edTitle.Text = "Edit index.html"; edBox.Text = h.Pages["index.html"]; editor.IsVisible = true; };
        var newName = Field(sp, "page name", "New page name", "about.html", 200);
        sp.Children.Add(Act("add page", "Add page", () => { string n = (newName.Text ?? "").Trim(); if (n.Length > 0 && !h.Pages.ContainsKey(n)) { h.Pages[n] = "<h1>" + n + "</h1><p>New page.</p>"; Fill(); DevicePage.Changed(); } }));
        sp.Children.Add(Dk.Head("Pages"));
        sp.Children.Add(items);
        sp.Children.Add(editor);
        Fill();
        return sp;
    }

    Control Dns()
    {
        Start();
        var s = D.Ip;
        if (s.DnsSrv == null) { s.DnsSrv = new DnsServer(s); s.DnsSrv.Enabled = false; }
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("DNS server"));
        sp.Children.Add(Dk.Note("DNS turns names into addresses. Add a record for each name; computers that use this server (their DNS server setting) can then use the names."));
        sp.Children.Add(Dk.OnOff("DNS service", s.DnsSrv.Enabled, v => s.DnsSrv.Enabled = v));
        var name = Field(sp, "name", "Name", "", 240, "For example www.lab.local");
        var addr = Field(sp, "address", "Address", "", 180, "The IP address the name stands for.");
        var m = Dk.Msg();
        Message = m;
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            foreach (var kv in s.DnsSrv.Records.ToList())
            {
                var k = kv.Key;
                items.Children.Add(Dk.Row(Dk.Mono(k + "   A   " + Ip.Str(kv.Value)), Dk.Btn("Remove", () => { s.DnsSrv.Records.Remove(k); Fill(); DevicePage.Changed(); })));
            }
            if (s.DnsSrv.Records.Count == 0) items.Children.Add(Dk.Note("No records yet."));
        }
        sp.Children.Add(Act("add record", "Add record", () =>
        {
            if ((name.Text ?? "").Trim().Length == 0 || !Ip.TryParse(addr.Text, out uint ip)) { m.Text = "Type a name and a valid IP address."; return; }
            s.DnsSrv.Records[name.Text.Trim()] = ip; m.Text = ""; name.Text = ""; addr.Text = ""; Fill(); DevicePage.Changed();
        }, true));
        sp.Children.Add(m);
        sp.Children.Add(Dk.Head("Records"));
        sp.Children.Add(items);
        Fill();
        return sp;
    }

    Control Dhcp()
    {
        Start();
        var s = D.Ip;
        if (s.Dhcp == null) { s.Dhcp = new DhcpServer(s); s.Dhcp.Enabled = false; }
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("DHCP server"));
        sp.Children.Add(Dk.Note("Hands out addresses. A pool for another network works when that network's router passes requests on (ip helper-address)."));
        sp.Children.Add(Dk.OnOff("DHCP service", s.Dhcp.Enabled, v => s.Dhcp.Enabled = v));
        var pool = Field(sp, "pool", "Pool name", "serverPool", 200);
        var gw = Field(sp, "gateway", "Default gateway", "", 180, "The router address the computers should use.");
        var dns = Field(sp, "dns", "DNS server", "", 180);
        var start = Field(sp, "start", "Start address", "", 180, "The first address to hand out; its network is the pool's network.");
        var mask = Field(sp, "mask", "Subnet mask", "255.255.255.0", 180);
        var max = Field(sp, "max", "Maximum users", "50", 100);
        var m = Dk.Msg();
        Message = m;
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            foreach (var p in s.Dhcp.Pools.ToList())
            {
                var pp = p;
                var line = Dk.Mono(p.Name + ":  " + Ip.Cidr(p.Network, p.Mask) + ", from " + Ip.Str(p.Start != 0 ? p.Start : p.Network + 1) + " (" + (p.Max > 0 ? p.Max.ToString() : "all") + "), gateway " + Ip.Str(p.Router) + ", DNS " + (p.Dns.Count > 0 ? Ip.Str(p.Dns[0]) : "none"));
                items.Children.Add(Dk.Row(line,
                    Dk.Btn("Edit", () => { pool.Text = pp.Name; gw.Text = Ip.Str(pp.Router); dns.Text = pp.Dns.Count > 0 ? Ip.Str(pp.Dns[0]) : ""; start.Text = Ip.Str(pp.Start); mask.Text = Ip.Str(pp.Mask); max.Text = pp.Max.ToString(); }),
                    Dk.Btn("Remove", () => { s.Dhcp.Pools.Remove(pp); Fill(); DevicePage.Changed(); })));
            }
            if (s.Dhcp.Pools.Count == 0) items.Children.Add(Dk.Note("No pools yet."));
            var leases = s.Dhcp.Leases.Values.ToList();
            if (leases.Count > 0) items.Children.Add(Dk.Note("Leased: " + string.Join(", ", leases.Select(l => Ip.Str(l.Ip)))));
        }
        sp.Children.Add(Act("save pool", "Add / save pool", () =>
        {
            uint g = 0, dn = 0;
            if (!Ip.TryParse(start.Text, out uint st) || !Ip.TryParseMask(mask.Text, out uint mk)) { m.Text = "The start address and mask must be valid."; return; }
            if ((gw.Text ?? "").Trim().Length > 0 && !Ip.TryParse(gw.Text, out g)) { m.Text = "The gateway isn't a valid address."; return; }
            if ((dns.Text ?? "").Trim().Length > 0 && !Ip.TryParse(dns.Text, out dn)) { m.Text = "The DNS server isn't a valid address."; return; }
            int.TryParse(max.Text, out int mx);
            var p = s.Dhcp.Pool((pool.Text ?? "").Trim(), true);
            p.Network = st & mk; p.Mask = mk; p.Router = g; p.Start = st; p.Max = mx; p.Dns.Clear(); if (dn != 0) p.Dns.Add(dn);
            m.Text = "Saved."; Fill(); DevicePage.Changed();
        }, true));
        sp.Children.Add(m);
        sp.Children.Add(Dk.Head("Pools"));
        sp.Children.Add(items);
        Fill();
        return sp;
    }

    Control Smb()
    {
        Start();
        var h = D.Host;
        var sp = new StackPanel();
        sp.Children.Add(Dk.Head("Shared folders (file sharing)"));
        sp.Children.Add(Dk.Note("Computers open them as \\\\" + h.HostName + "\\<folder> (the Windows-like File Explorer, net view)."));
        sp.Children.Add(Dk.OnOff("File sharing", h.SmbOn, v => h.SmbOn = v));
        var name = Field(sp, "folder", "Folder name", "Public", 200);
        var files = Field(sp, "files", "Files in it", "readme.txt, photo.jpg", 300, "Names separated by commas.");
        var items = new StackPanel();
        void Fill()
        {
            items.Children.Clear();
            foreach (var s in h.Shares.ToList())
            {
                var ss = s;
                items.Children.Add(Dk.Row(Dk.Mono(s.Name + ":  " + string.Join(", ", s.Files)), Dk.Btn("Remove", () => { h.Shares.Remove(ss); Fill(); DevicePage.Changed(); })));
            }
            if (h.Shares.Count == 0) items.Children.Add(Dk.Note("No shared folders yet."));
        }
        sp.Children.Add(Act("save folder", "Add / save folder", () =>
        {
            string n = (name.Text ?? "").Trim();
            if (n.Length == 0) return;
            h.Shares.RemoveAll(x => x.Name == n);
            h.Shares.Add(new SharedFolder { Name = n, Files = (files.Text ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() });
            Fill(); DevicePage.Changed();
        }, true));
        sp.Children.Add(Dk.Head("Folders"));
        sp.Children.Add(items);
        Fill();
        return sp;
    }
}
