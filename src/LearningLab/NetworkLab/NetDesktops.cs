// Network Lab - the Desktop tab of end devices (App Hub's Desktops.cs): which screen a device gets, the simple desktop
// (tiles for IP Configuration, Command Prompt, Web Browser, Wi-Fi, Printers and Terminal, like the classic practice
// software), a Linux server's console and the NAS / camera status screen. The Windows-like, Linux-like, macOS-like,
// phone, printer and TV screens are in NetWinDesktop.cs and NetOtherDesktops.cs.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

/// <summary>A pretend desktop: it opens its apps by id (settings, browser, cmd, printers...).</summary>
public interface IDesktop
{
    /// <summary>Opens an app and returns what it shows (its window's content), or null when this desktop has no such app.</summary>
    Control OpenApp(string id);
}

public static class Desktops
{
    /// <summary>The device's screen, or null (a Linux server has only its terminal, which DevicePage shows).</summary>
    public static Control Create(DevicePage w, Device d)
    {
        var p = Personalities.Get(d.Os);
        switch (p.Family)
        {
            case "windows": return new WinDesktop(w, d, p.Id == "win11");
            case "linux": return ((LinuxPersonality)p).Desktop ? new LinuxDesktop(w, d) : null;
            case "mac": return new MacDesktop(w, d);
            case "phone": return new PhoneScreen(w, d, p.Id == "iphone");
            case "printer": return new PrinterPanel(w, d);
            case "appliance": return p.Id == "tv" || p.Id == "console" ? new TvScreen(w, d) : new ApplianceScreen(w, d);
        }
        return new GenericDesktop(w, d);
    }

    /// <summary>The tab's name for that screen.</summary>
    public static string TabName(Device d)
    {
        var fam = Personalities.Get(d.Os).Family;
        return fam == "printer" ? "Front panel" : fam == "appliance" ? "Screen" : fam == "phone" ? "Screen" : "Desktop";
    }
}

/// <summary>The simple desktop: tiles that open each app over the whole area, with "Desktop" to go back.</summary>
public sealed class GenericDesktop : DockPanel, IDesktop
{
    readonly DevicePage W;
    readonly Device D;
    readonly ContentControl area = new();
    readonly Dictionary<string, Control> apps = new();
    readonly WrapPanel tiles = new() { Margin = new Thickness(16) };
    readonly Button home;
    public string Current { get; private set; } = "";

    public GenericDesktop(DevicePage w, Device d)
    {
        W = w; D = d;
        Background = Ui.Brush("#101826");
        home = Ui.Button("< Desktop", false, 14);
        home.Margin = new Thickness(8); home.HorizontalAlignment = HorizontalAlignment.Left; home.IsVisible = false;
        home.Click += (s, e) => ShowApp(null);
        SetDock(home, Dock.Top);
        Children.Add(home);
        Children.Add(area);
        Tile("IP Configuration", "ip", "Set this computer's address, mask, gateway and DNS server.");
        Tile("Command Prompt", "cmd", "ipconfig, ping, tracert, nslookup, arp -a");
        Tile("Web Browser", "browser", "Open web pages from servers in the network.");
        if (d.Ports.Any(p => p.IsWireless)) Tile("Wi-Fi", "wifi", "Join a wireless network.");
        Tile("Printers", "printers", "Add a network printer and print a test page.");
        if (d.Ports.Any(p => p.Media == "rs232")) Tile("Terminal", "terminal", "Type commands on a router or switch over a console cable.");
        area.Content = new ScrollViewer { Content = tiles };
    }

    void Tile(string name, string glyph, string tip)
    {
        var sp = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var g = Glyphs.App(glyph, 50); g.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(g);
        sp.Children.Add(new TextBlock { Text = name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(Ui.TextC), FontSize = 14 });
        var b = Ui.Tile(sp, "#38BDF8", () => Lt.Safe(() => ShowApp(name)));
        b.Width = 150; b.Height = 124; b.Margin = new Thickness(0, 0, 12, 12); b.Padding = new Thickness(8);
        ToolTip.SetTip(b, tip);
        tiles.Children.Add(b);
    }

    public Control OpenApp(string id) => ShowApp(id switch
    {
        "ip" or "settings" => "IP Configuration", "cmd" => "Command Prompt", "browser" => "Web Browser", "wifi" => "Wi-Fi",
        "printers" => "Printers", "terminal" => "Terminal", _ => id,
    });

    public Control ShowApp(string name)
    {
        Current = name ?? "";
        if (name == null) { area.Content = new ScrollViewer { Content = tiles }; home.IsVisible = false; return null; }
        if (!apps.TryGetValue(name, out var a))
        {
            switch (name)
            {
                case "IP Configuration":
                    {
                        var sp = new StackPanel { Margin = new Thickness(16) };
                        foreach (var f in D.Host.Adapters)
                        {
                            sp.Children.Add(Ui.Text(Personalities.Get(D.Os).IfName(f.Port), 16, Ui.AccentText, true));
                            sp.Children.Add(W.IpPanel(f));
                        }
                        a = new ScrollViewer { Content = sp };
                        break;
                    }
                case "Command Prompt": a = DesktopApps.Prompt(D, false); break;
                case "Web Browser": a = Lt.Scope(new MiniBrowser(D, true, "")); break;
                case "Wi-Fi":
                    {
                        var sp = new StackPanel { Margin = new Thickness(16) };
                        sp.Children.Add(Ui.Text("Wi-Fi networks", 17, Ui.AccentText, true));
                        var key = new TextBox { Width = 220, MinHeight = 40, PasswordChar = '*', HorizontalAlignment = HorizontalAlignment.Left };
                        var pending = Ui.Text("", 14, Ui.Muted);
                        string wanted = null;
                        var join = Ui.Button("Connect", true, 14);
                        var keyRow = new StackPanel { Spacing = 6, IsVisible = false, Children = { Ui.Text("Password", 13, Ui.Muted), key, join } };
                        join.HorizontalAlignment = HorizontalAlignment.Left;
                        join.Click += (s, e) => Lt.Safe(() =>
                        {
                            if (wanted == null) return;
                            string st = DesktopApps.Join(D, wanted, key.Text);
                            pending.Text = st == "connected" ? "Connected to " + wanted + "." : st == "wrong-key" ? "The password is wrong." : "Couldn't connect.";
                            keyRow.IsVisible = st != "connected";
                        });
                        sp.Children.Add(DesktopApps.WifiList(W, D, false, ssid => { wanted = ssid; pending.Text = "Type the password for " + ssid + "."; keyRow.IsVisible = true; key.Text = ""; key.Focus(); }));
                        sp.Children.Add(pending);
                        sp.Children.Add(keyRow);
                        a = new ScrollViewer { Content = sp };
                        break;
                    }
                case "Printers": a = new ScrollViewer { Content = new DesktopApps.PrintersApp(D, false) }; break;
                case "Terminal": a = DesktopApps.Terminal(D); break;
                default: return null;
            }
            apps[name] = a;
        }
        area.Content = a;
        home.IsVisible = true;
        if (a is NetTerminal nt) Avalonia.Threading.Dispatcher.UIThread.Post(nt.FocusInput, Avalonia.Threading.DispatcherPriority.Input);
        return a;
    }
}

/// <summary>A Linux server has no desktop: its tty with a note above the terminal (used for its Terminal tab).</summary>
public sealed class ServerScreen : DockPanel
{
    public readonly NetTerminal Console;
    public ServerScreen(Device d)
    {
        Background = Ui.Brush("#000000");
        var head = new TextBlock { Text = Personalities.Get(d.Os).Title + " (simulated), " + d.Host.HostName + " tty1. This server has no desktop: everything is done with commands.", Foreground = Ui.Brush("#9CA3AF"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 4) };
        SetDock(head, Dock.Top);
        Children.Add(head);
        Console = new NetTerminal(new ShellBackend(Personalities.Get(d.Os).NewShell(d)), "#000000", "#D1D5DB");
        Children.Add(Console);
    }
}

/// <summary>A NAS or an IP camera: a status screen (they are set up through their web page).</summary>
public sealed class ApplianceScreen : StackPanel
{
    public ApplianceScreen(DevicePage w, Device d)
    {
        Margin = new Thickness(16);
        Children.Add(Ui.Text(d.Model + ": " + d.Name, 17, Ui.AccentText, true));
        var note = Ui.Text(Catalog.Explain(d.Kind), 14, Ui.Muted); note.Margin = new Thickness(0, 4, 0, 10);
        Children.Add(note);
        var st = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 13, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap };
        Children.Add(st);
        w.OnRefresh(() =>
        {
            var f = d.Host.Main;
            var lines = new List<string>
            {
                "Address: " + (f != null && f.Ip != 0 ? Ip.Cidr(f.Ip, f.Mask) + (f.Dhcp ? "  (from DHCP)" : "") : "none yet"),
                "Its web page: " + (f != null && f.Ip != 0 ? "http://" + Ip.Str(f.Ip) + "/" : "(needs an address)"),
            };
            if (d.Kind == "nas") lines.Add("Shared folders: " + string.Join(", ", d.Host.Shares.Select(s => s.Name)) + "   (open as \\\\" + d.Host.HostName + "\\<folder>)");
            st.Text = string.Join("\n", lines);
        });
    }
}
