// Cert Lab - the AZ-800 course's practice area (App Hub's UiWsWorkspace / UiWsKit for Avalonia and touch): a
// PowerShell console on each server, and the practice consoles (Users and Computers, Group Policy Management, DNS
// Manager, Windows Admin Center, the Azure portal) drawn by Cert Lab in its own neutral look. Every tap builds a console
// line (WsOps) and runs it, so what the person does and a lab's model solution go through the same rules. Lab and exam
// pages host it in place of terminals. On a wide screen each console has its tree on the left; under 760 px the tree
// folds behind a "Show the tree" button above the page, forms put labels above their fields, and tables become cards.
// A pretend network and a pretend Azure subscription: nothing reaches a real server and nothing signs in anywhere.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

/// <summary>A small form: labelled fields; Pairs() builds "key=value ..." for a console line from what was typed or picked.</summary>
public class WsForm
{
    public readonly StackPanel Panel = new() { Margin = new Thickness(0, 4, 0, 8) };
    readonly Dictionary<string, Func<string>> get = new();
    public readonly Dictionary<string, Control> Fields = new(StringComparer.OrdinalIgnoreCase);
    public Button SubmitButton;
    Action submit;
    readonly bool narrow;

    public WsForm(string title, bool narrow)
    {
        this.narrow = narrow;
        if (!string.IsNullOrEmpty(title)) { var h = Pk.H(title); h.Margin = new Thickness(0, 2, 0, 8); Panel.Children.Add(h); }
    }

    void Add(string label, Control field)
    {
        var l = Pk.Txt(label, 14, false, Pk.LabelC);
        if (narrow)
        {
            l.Margin = new Thickness(0, 2, 0, 2);
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (field is TextBox) field.Width = double.NaN;
            Panel.Children.Add(l); Panel.Children.Add(field);
            return;
        }
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(190)));
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 8, 6);
        Grid.SetColumn(field, 1);
        g.Children.Add(l); g.Children.Add(field);
        Panel.Children.Add(g);
    }
    public TextBox Text(string key, string label, string value)
    {
        var t = Pk.Box(value, 240); t.MaxWidth = 420;
        Add(label, t);
        get[key] = () => Pk.T(t).Trim(); Fields[key] = t; return t;
    }
    public ComboBox Choice(string key, string label, IEnumerable<string> items, string selected)
    {
        var cb = Pk.Combo(items, selected, 240);
        if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
        Add(label, cb);
        get[key] = () => Pk.Val(cb); Fields[key] = cb; return cb;
    }
    public CheckBox Check(string key, string label, bool value)
    {
        var c = Pk.Check(label, value);
        c.Margin = new Thickness(narrow ? 0 : 190, 0, 0, 4);
        Panel.Children.Add(c); get[key] = () => c.IsChecked == true ? "yes" : "no"; Fields[key] = c; return c;
    }
    public string V(string key) => get.TryGetValue(key, out var f) ? f() : "";
    public void Set(string key, string value)
    {
        if (!Fields.TryGetValue(key, out var c)) return;
        if (c is TextBox tb) tb.Text = value; else if (c is ComboBox cb) Pk.Select(cb, value); else if (c is CheckBox ch) ch.IsChecked = WsText.True(value);
    }
    /// <summary>key=value pairs for the keys given (empty values are left out).</summary>
    public string Pairs(params string[] keys) => string.Join(" ", keys.Select(k => { var v = V(k); return v.Length == 0 ? null : k + "=" + WsWorkspace.Q(v); }).Where(x => x != null));
    public Button Submit(string text, Action click)
    {
        submit = click;
        SubmitButton = Pk.Accent(text, click);
        SubmitButton.Margin = new Thickness(narrow ? 0 : 190, 6, 6, 4);
        SubmitButton.HorizontalAlignment = HorizontalAlignment.Left;
        Panel.Children.Add(SubmitButton);
        return SubmitButton;
    }
    /// <summary>Taps the form's button (the checks).</summary>
    public void Press() => Pk.Safe(submit);
}

public partial class WsWorkspace : Grid
{
    public readonly WsSim Sim;
    public readonly bool Exam;
    readonly Action<string> status;
    /// <summary>The consoles open in the workspace (terminal controls; polled by Tick).</summary>
    public readonly List<Control> Terms = new();
    public WsLab Lab => Sim.Lab;
    readonly StackPanel tabBar = new();
    readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(4, 0, 4, 6) };
    readonly Border body = new();
    public string Tab = "servers";
    public string Machine = "DC1";
    public string Node = "";                      // what the console's tree has picked (a DN, a GPO, a zone...)
    public string Sub = "";                       // a page inside the node (a GPO's tab, a WAC tool...)
    readonly Dictionary<string, Control> consoles = new(StringComparer.OrdinalIgnoreCase);
    public static readonly string[][] Tabs = { new[] { "servers", "Servers" }, new[] { "aduc", "Users and Computers" }, new[] { "gpmc", "Group Policy" }, new[] { "dns", "DNS" }, new[] { "wac", "Admin Center" }, new[] { "azure", "Azure" } };
    public const double NarrowBelow = 760;
    bool narrow, treeOpen;
    public bool Narrow => narrow;
    public bool TreeOpen => treeOpen;
    public string LastError;   // a page that failed to draw (checks)

    public WsWorkspace(WsSim sim, bool exam, Action<string> status = null)
    {
        Sim = sim; Exam = exam; this.status = status;
        narrow = exam;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        var strip = Pk.Strip(tabBar);
        strip.Margin = new Thickness(0, 0, 0, 4);
        Children.Add(strip);
        Grid.SetRow(message, 1); Children.Add(message);
        Grid.SetRow(body, 2); Children.Add(body);
        SizeChanged += (s, e) =>
        {
            bool n = e.NewSize.Width < NarrowBelow;
            if (n == narrow) return;
            narrow = n;
            Dispatcher.UIThread.Post(Show);
        };
        Show();
    }

    /// <summary>The phone's Back button (the host page's OnBack): folds the tree away; false otherwise.</summary>
    public bool OnBack()
    {
        if (treeOpen) { treeOpen = false; Show(); return true; }
        return false;
    }

    WsForm F(string title) => new(title, Narrow);

    /// <summary>Opens a place: "servers|DC1", "aduc|DN|object", "gpmc|gpo:Name|scope", "gpmc|som:DN", "dns|DC1|zone:corp.example",
    /// "wac|SRV1|node|tool", "azure|arc". Machine-based tabs take a machine first; the rest a node and a sub-page.</summary>
    public void Go(string where)
    {
        var p = (where ?? "").Split('|');
        Tab = p[0];
        int k = 1;
        if ((Tab == "servers" || Tab == "wac" || Tab == "dns") && p.Length > 1) { if (p[1].Length > 0) Machine = p[1]; k = 2; }
        Node = p.Length > k ? p[k] : "";
        Sub = p.Length > k + 1 ? p[k + 1] : "";
        treeOpen = false;
        AducForm = null; GpForm = null;
        Show();
    }

    /// <summary>Runs a console line, says what happened, and redraws the page.</summary>
    public WsResult Run(string line)
    {
        var r = WsOps.Run(Lab, line);
        LastLine = line;
        Say(r.Text, !r.Ok);
        Show();
        return r;
    }
    /// <summary>The last console line a tap ran (the checks).</summary>
    public string LastLine;
    public void Say(string text, bool bad)
    {
        message.Text = text ?? "";
        message.Foreground = Pk.B(bad ? Pk.BadC : Pk.LabelC);
        message.IsVisible = message.Text.Length > 0;
        if (!string.IsNullOrEmpty(text)) status?.Invoke(text);
    }
    public string LastMessage => message.Text ?? "";

    public void Tick()
    {
        foreach (var t in Terms) PortalWorkspaces.Poll(t);
        if (Tab == "servers") UpdateHead();
    }

    public void Show()
    {
        tabBar.Children.Clear();
        foreach (var t in Tabs)
        {
            string id = t[0];
            var b = Pk.Small(t[1], () => { Tab = id; Node = ""; Sub = ""; treeOpen = false; Say("", false); Show(); });
            Pk.Picked(b, id == Tab);
            tabBar.Children.Add(b);
        }
        Control page;
        LastError = null;
        try
        {
            switch (Tab)
            {
                case "aduc": page = BuildAduc(); break;
                case "gpmc": page = BuildGpmc(); break;
                case "dns": page = BuildDns(); break;
                case "wac": page = BuildWac(); break;
                case "azure": page = BuildAzure(); break;
                default: page = BuildServers(); break;
            }
        }
        catch (Exception e) { LastError = e.ToString(); page = Pk.Error("This page couldn't be drawn: " + e.Message); }
        body.Child = page;
    }

    // ------------------------------------------------------------ the server consoles
    TextBlock head;
    Control BuildServers()
    {
        var dock = new DockPanel();
        var bar = new StackPanel();
        foreach (var n in Sim.MachineNames)
        {
            string m = n;
            var b = Pk.Small(n, () => { Machine = m; Show(); });
            Pk.Picked(b, WsText.Eq(n, Machine));
            if (!Lab.Server(n).Running) b.Foreground = Pk.B(Ui.Muted);
            bar.Children.Add(b);
        }
        var strip = Pk.Strip(bar);
        DockPanel.SetDock(strip, Dock.Top); dock.Children.Add(strip);
        var acts = Pk.Row(Pk.Small("Restart " + Machine, () => { Sim.Reboot(Machine); Say(Machine + " restarted.", false); Show(); }),
            Pk.Small(Lab.Server(Machine).Running ? "Switch off" : "Switch on", () => { Sim.Power(Machine); Show(); }));
        DockPanel.SetDock(acts, Dock.Top); dock.Children.Add(acts);
        head = new TextBlock { Foreground = Pk.B(Ui.TextC), FontSize = 13, Margin = new Thickness(8, 6), TextWrapping = TextWrapping.Wrap };
        var hb = new Border { Background = Pk.B(Ui.Surface2), Child = head };
        DockPanel.SetDock(hb, Dock.Top); dock.Children.Add(hb);
        UpdateHead();
        if (!consoles.TryGetValue(Machine, out var tv))
        {
            tv = PortalWorkspaces.Terminal(Sim.Term(Machine));
            consoles[Machine] = tv; Terms.Add(tv);
        }
        if (tv.Parent is Border pb) pb.Child = null;
        dock.Children.Add(new Border { Child = tv, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1) });
        return dock;
    }
    void UpdateHead() { if (head != null) head.Text = Sim.Status(Machine) + "    |    " + Sim.Clock; }
    /// <summary>A server's console (opens the Servers tab on it).</summary>
    public Control ConsoleView(string machine) { Machine = machine; Tab = "servers"; Show(); return consoles[Machine]; }

    // ------------------------------------------------------------ the kit
    public static string Q(string s) { s ??= ""; return s.Length == 0 || s.Any(ch => char.IsWhiteSpace(ch) || ch == '"' || ch == ';') ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : s; }

    /// <summary>A tree and a page: side by side on a wide screen; on a phone the tree folds above the page.</summary>
    public Control TwoPane(Control left, Control right)
    {
        if (!Narrow)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(260)));
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var l = new Border { Background = Pk.B(Ui.Surface), BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } };
            var r = new Border { BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 1, 1, 1), Padding = new Thickness(14, 8, 10, 8), Child = new ScrollViewer { Content = right, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
            Grid.SetColumn(r, 1);
            g.Children.Add(l); g.Children.Add(r);
            return g;
        }
        var dock = new DockPanel { Margin = new Thickness(4, 0, 0, 0) };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(Pk.Icon(treeOpen ? "close" : "menu", 18, Ui.TextC));
        label.Children.Add(new TextBlock { Text = treeOpen ? "Hide the tree" : "Show the tree", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = Pk.B(Ui.TextC) });
        var toggle = new Button { Content = label, MinHeight = 44, Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 6), Background = Pk.B(Ui.Surface3), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
        toggle.Click += (s, e) => { treeOpen = !treeOpen; Show(); };
        TreeToggle = toggle;
        DockPanel.SetDock(toggle, Dock.Top); dock.Children.Add(toggle);
        if (treeOpen)
        {
            var tb = new Border { Background = Pk.B(Ui.Surface), BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8), MaxHeight = 420, Child = new ScrollViewer { Content = left } };
            DockPanel.SetDock(tb, Dock.Top); dock.Children.Add(tb);
        }
        dock.Children.Add(new ScrollViewer { Content = right, Padding = new Thickness(6, 0, 14, 16), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        return dock;
    }
    /// <summary>The phone's "Show the tree" button (null on a wide screen).</summary>
    public Button TreeToggle;

    /// <summary>One line of a tree; a tap also folds the tree away on a phone.</summary>
    public Control TreeItem(string text, int depth, bool selected, string icon, Action click)
    {
        if (click == null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6 + depth * 14, 10, 6, 4) };
            if (icon != null) sp.Children.Add(Pk.Icon(icon, 16, Pk.LabelC));
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13.5, Foreground = Pk.B(Pk.LabelC), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
        return Pk.TreeItem(text, depth, selected, icon, () => { treeOpen = false; click(); });
    }
    public const string IcDomain = "domain", IcFolder = "folder", IcScroll = "doc", IcUser = "user", IcGroup = "group", IcPc = "pc", IcServer = "server", IcFilter = "filter", IcGlobe = "globe", IcCloud = "cloud", IcLink = "link";

    /// <summary>A labelled action button for a page's toolbar.</summary>
    public static Button Act(string text, Action click) => Pk.Small(text, click);
    public static WrapPanel Bar(params Control[] items) { var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; foreach (var i in items) w.Children.Add(i); return w; }
    public static Control Title(string text, string sub)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        s.Children.Add(Pk.Txt(text, 18, true));
        if (!string.IsNullOrEmpty(sub)) s.Children.Add(Pk.Txt(sub, 13, false, Ui.Muted));
        return s;
    }
}
