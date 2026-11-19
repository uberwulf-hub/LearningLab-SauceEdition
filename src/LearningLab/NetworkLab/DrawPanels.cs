// Network Lab - the panels of the drawing tools (App Hub's DrawPanels.cs for Avalonia, touch first): the properties
// of the selected drawings (colours, lines, see-through, text, arrowheads, lock, above the devices, order, line up,
// group, duplicate, copy and paste), the layers of the view (show and lock each one) and the library of objects.
// They live in Network Lab's side panel, which takes the whole screen on a phone.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

public static class DrawPanels
{
    public static readonly string[] Colours = { "#FFFFFF", "#E2E8F0", "#94A3B8", "#475569", "#1F2937", "#000000", "#EF4444", "#F97316", "#FACC15", "#FDE68A",
                                                "#22C55E", "#166534", "#14B8A6", "#38BDF8", "#3B82F6", "#1E3A5F", "#6366F1", "#A855F7", "#EC4899", "#92400E" };

    static TextBlock Head(string t) { var h = Ui.Text(t, 14, Ui.AccentText, true); h.Margin = new Thickness(0, 12, 0, 4); return h; }
    static TextBlock Label(string t) { var l = Ui.Text(t, 12.5, Ui.Muted); l.Margin = new Thickness(0, 6, 0, 3); return l; }
    static string N(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    public static Button Small(string text, Action a, string tip = null)
    {
        var b = Ui.Button(text, false, 13.5);
        b.MinHeight = 44; b.Padding = new Thickness(12, 4); b.Margin = new Thickness(0, 0, 6, 6);
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (s, e) => { try { a(); } catch (Exception ex) { MainView.Current?.Message("That didn't work", ex.Message); } };
        return b;
    }

    /// <summary>A row of colour squares big enough for a finger (and "none" if allowed); the chosen one has a frame.</summary>
    public static WrapPanel Swatches(string current, bool none, Action<string> pick)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var list = new List<string>();
        if (none) list.Add("none");
        list.AddRange(Colours);
        foreach (var c in list)
        {
            var cc = c;
            bool on = string.Equals(c, current, StringComparison.OrdinalIgnoreCase);
            var b = new Button
            {
                Width = 40, Height = 40, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(on ? 3 : 1), BorderBrush = Ui.Brush(on ? Ui.Accent : Ui.BorderStrong),
                Background = c == "none" ? Brushes.Transparent : NetIcons.B(c),
                Content = c == "none" ? new TextBlock { Text = "∅", FontSize = 18, Foreground = Ui.Brush(Ui.Red), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
            };
            ToolTip.SetTip(b, c == "none" ? "None (see-through)" : c);
            b.Click += (s, e) => pick(cc);
            w.Children.Add(b);
        }
        var tb = new TextBox { Width = 96, MinHeight = 40, Text = current != null && current.StartsWith('#') ? current : "", Watermark = "#RRGGBB", Margin = new Thickness(0, 0, 4, 4), FontSize = 13 };
        ToolTip.SetTip(tb, "Any colour as #RRGGBB, then Enter");
        tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { string v = (tb.Text ?? "").Trim(); if (!v.StartsWith('#')) v = "#" + v; if (DrawColor.IsColor(v)) pick(v.ToUpperInvariant()); e.Handled = true; } };
        w.Children.Add(tb);
        return w;
    }

    public static ComboBox Combo(IEnumerable<(string Key, string Text)> items, string current, Action<string> pick, double width)
    {
        var list = items.ToList();
        if (!list.Any(x => x.Key == current) && current != null) list.Add((current, current));
        var cb = new ComboBox { MinWidth = width, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), FontSize = 13, ItemsSource = list.Select(x => x.Text).ToList(), SelectedIndex = list.FindIndex(x => x.Key == current) };
        cb.SelectionChanged += (s, e) => { if (cb.SelectedIndex >= 0 && list[cb.SelectedIndex].Key != current) pick(list[cb.SelectedIndex].Key); };
        return cb;
    }
    static IEnumerable<(string, string)> Nums(params double[] v) => v.Select(x => (N(x), N(x)));

    static CheckBox Check(string text, bool on, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = on, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0), Foreground = Ui.Brush(Ui.TextC) };
        c.Click += (s, e) => set(c.IsChecked == true);
        return c;
    }

    // ================================================================= the properties of the selected drawings
    /// <summary>Fills a panel with the properties of the selection. refresh = build it again after a change.</summary>
    public static void Props(StackPanel body, DrawTool tool, Action refresh)
    {
        void Do(string name, Action<Shape> change) { tool.Apply(name, change); refresh(); }
        void DoC(string name, Action<Callout> change) { tool.ApplyCallouts(name, change); refresh(); }
        var title = Ui.Text("Drawing", 17, Ui.TextC, true);
        body.Children.Add(title);
        if (tool?.Host.DrawProject == null) return;
        var shapes = tool.SelectedShapes();
        var callouts = tool.SelectedCallouts();
        if (shapes.Count == 0 && callouts.Count == 0)
        {
            title.Text = "Nothing selected";
            body.Children.Add(Ui.Text(tool.CanDraw ? "Pick a tool in the drawing bar at the bottom (rectangle, line, text, sticky note, area...) or an object from the library, then tap or drag on the view. Tap a drawing to change it here."
                                                   : "Drawings go on the Logical view, the Campus map and the floor plans.", 13.5, Ui.Muted));
            var sel = tool.Host.DrawSelection.Where(o => o is Device || o is Link).ToList();
            if (sel.Count == 1 && tool.Host.CalloutView.Length > 0)
                body.Children.Add(Small("Add a label to " + (sel[0] is Device d ? d.Name : "this cable") + "...", () => tool.AskLabel(sel[0])));
            if (DrawTool.LastClip.Length > 0 && tool.CanDraw) body.Children.Add(Small("Paste", () => { tool.Paste(); refresh(); }));
            return;
        }
        if (callouts.Count > 0 && shapes.Count == 0) { CalloutProps(body, title, tool, callouts, DoC, refresh); return; }
        var s0 = shapes[0];
        title.Text = shapes.Count == 1 ? Drawings.KindName(s0) : shapes.Count + " drawings";
        bool anyLine = shapes.Any(s => s.IsLine), anyBox = shapes.Any(s => !s.IsLine);

        // text
        if (shapes.Count == 1 && s0.Kind != "image")
        {
            body.Children.Add(Label(s0.Kind == "area" ? "Title" : s0.Kind == "object" ? "Label under it" : "Text"));
            var tb = new TextBox { Text = s0.Text, AcceptsReturn = s0.Kind != "area", TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxHeight = 140 };
            void Commit() { if ((tb.Text ?? "") != s0.Text) { tool.SetText(s0, tb.Text ?? ""); refresh(); } }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter && (!tb.AcceptsReturn || (e.KeyModifiers & KeyModifiers.Control) != 0)) { Commit(); e.Handled = true; } };
            body.Children.Add(tb);
            body.Children.Add(Small("Set the text", Commit));
        }
        // fill and outline
        if (shapes.Any(s => s.CanFill && s.Kind != "image" && s.Kind != "object"))
        {
            body.Children.Add(Label(s0.Kind == "area" ? "Colour" : "Fill"));
            body.Children.Add(Swatches(s0.Fill, s0.Kind != "area" && s0.Kind != "note", c => Do("fill colour", x => { if (x.CanFill) x.Fill = c; if (x.Kind == "area" && c != "none") { x.Stroke = c; x.TextColor = DrawColor.Lighter(c, 0.55); } })));
        }
        body.Children.Add(Label(anyLine && !anyBox ? "Line colour" : "Outline"));
        body.Children.Add(Swatches(s0.Stroke, !(anyLine && !anyBox), c => Do("outline colour", x => { x.Stroke = c; if (x.StrokeWidth <= 0 && c != "none") x.StrokeWidth = 2; })));
        var row = new WrapPanel();
        row.Children.Add(Combo(new double[] { 0, 0.5, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24 }.Select(v => (N(v), v == 0 ? "No line" : N(v) + " px")), N(s0.StrokeWidth), v => Do("line width", x => x.StrokeWidth = NetAsk.Number(v)), 100));
        row.Children.Add(Combo(new[] { ("solid", "Solid"), ("dash", "Dashes"), ("dot", "Dots") }, s0.Dash, v => Do("line style", x => x.Dash = v), 100));
        body.Children.Add(row);
        var row2 = new WrapPanel();
        row2.Children.Add(new TextBlock { Text = "See-through", Foreground = Ui.Brush(Ui.Muted), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
        row2.Children.Add(Combo(new[] { 1, 0.9, 0.75, 0.6, 0.5, 0.35, 0.2 }.Select(v => (N(v), (int)Math.Round(v * 100) + "%")), N(s0.Opacity), v => Do("see-through", x => x.Opacity = NetAsk.Number(v)), 90));
        if (shapes.Any(s => s.Kind == "rect" || s.Kind == "roundrect" || s.Kind == "area" || s.Kind == "text"))
        {
            row2.Children.Add(new TextBlock { Text = "Corners", Foreground = Ui.Brush(Ui.Muted), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
            row2.Children.Add(Combo(Nums(0, 4, 8, 14, 20, 30, 50), N(s0.Corner), v => Do("corners", x => x.Corner = NetAsk.Number(v)), 80));
        }
        body.Children.Add(row2);
        // arrowheads
        if (anyLine)
        {
            var ar = new WrapPanel();
            ar.Children.Add(Check("Arrow at the start", s0.ArrowStart, v => Do("arrowhead", x => { if (x.IsLine) x.ArrowStart = v; })));
            ar.Children.Add(Check("Arrow at the end", s0.ArrowEnd, v => Do("arrowhead", x => { if (x.IsLine) x.ArrowEnd = v; })));
            body.Children.Add(ar);
            if (shapes.Any(s => s.Kind == "polyline" || s.Kind == "freehand"))
                body.Children.Add(Check("Closed shape (filled)", s0.Closed, v => Do("closed shape", x => { if (x.Kind == "polyline" || x.Kind == "freehand") { x.Closed = v; if (v && x.Fill == "none") x.Fill = "#1E3A5F"; } })));
        }
        // text look
        if (shapes.Any(s => s.Kind != "image"))
        {
            body.Children.Add(Head("Text"));
            var fr = new WrapPanel();
            fr.Children.Add(Combo(new[] { "Segoe UI", "Arial", "Georgia", "Times New Roman", "Consolas", "Comic Sans MS", "Verdana" }.Select(f => (f, f)), s0.Font, v => Do("font", x => x.Font = v), 150));
            fr.Children.Add(Combo(Nums(8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80), N(s0.FontSize), v => Do("text size", x => x.FontSize = NetAsk.Number(v)), 80));
            body.Children.Add(fr);
            var st = new WrapPanel();
            var bb = new ToggleButton { Content = "B", FontWeight = FontWeight.Bold, IsChecked = s0.Bold, MinWidth = 44, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(bb, "Bold");
            bb.Click += (s, e) => { bool v = bb.IsChecked == true; Do("bold", x => x.Bold = v); };
            var ib = new ToggleButton { Content = "I", FontStyle = FontStyle.Italic, IsChecked = s0.Italic, MinWidth = 44, MinHeight = 44, Margin = new Thickness(0, 0, 12, 6), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(ib, "Italic");
            ib.Click += (s, e) => { bool v = ib.IsChecked == true; Do("italic", x => x.Italic = v); };
            st.Children.Add(bb); st.Children.Add(ib);
            foreach (var (key, text) in new[] { ("left", "Left"), ("center", "Centre"), ("right", "Right") })
            {
                var k = key;
                var t = new ToggleButton { Content = text, IsChecked = s0.Align == key, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4), VerticalContentAlignment = VerticalAlignment.Center };
                t.Click += (s, e) => Do("align text", x => x.Align = k);
                st.Children.Add(t);
            }
            body.Children.Add(st);
            body.Children.Add(Label("Text colour"));
            body.Children.Add(Swatches(s0.TextColor, false, c => Do("text colour", x => x.TextColor = c)));
        }
        // turn, lock, above
        body.Children.Add(Head("Position"));
        if (anyBox && shapes.Count == 1)
        {
            var rr = new WrapPanel();
            rr.Children.Add(new TextBlock { Text = "Turned " + N(s0.Rotation) + "°", Foreground = Ui.Brush(Ui.Muted), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
            rr.Children.Add(Small("↺ 15°", () => Do("turn", x => { if (!x.IsLine) x.Rotation = Math.Round(x.Rotation - 15, 1); }), "Turn left"));
            rr.Children.Add(Small("↻ 15°", () => Do("turn", x => { if (!x.IsLine) x.Rotation = Math.Round(x.Rotation + 15, 1); }), "Turn right"));
            rr.Children.Add(Small("Straight", () => Do("turn", x => x.Rotation = 0)));
            body.Children.Add(rr);
        }
        body.Children.Add(Check("Lock its position", s0.Locked, v => Do(v ? "lock" : "unlock", x => x.Locked = v)));
        body.Children.Add(Check("Drawn above the devices", s0.Above, v => Do(v ? "drawing above devices" : "drawing under devices", x => x.Above = v)));
        // arrange
        body.Children.Add(Head("Arrange"));
        var or = new WrapPanel();
        or.Children.Add(Small("To front", () => { tool.Order("front"); refresh(); }));
        or.Children.Add(Small("Forward", () => { tool.Order("forward"); refresh(); }));
        or.Children.Add(Small("Backward", () => { tool.Order("backward"); refresh(); }));
        or.Children.Add(Small("To back", () => { tool.Order("back"); refresh(); }));
        body.Children.Add(or);
        if (shapes.Count >= 2)
        {
            body.Children.Add(Label("Line them up"));
            var al = new WrapPanel();
            foreach (var (key, text) in new[] { ("left", "Left"), ("center", "Centre"), ("right", "Right"), ("top", "Top"), ("middle", "Middle"), ("bottom", "Bottom") })
            {
                var k = key;
                al.Children.Add(Small(text, () => { tool.Align(k); refresh(); }));
            }
            body.Children.Add(al);
            if (shapes.Count >= 3)
            {
                var di = new WrapPanel();
                di.Children.Add(Small("Space across", () => { tool.Distribute(true); refresh(); }));
                di.Children.Add(Small("Space down", () => { tool.Distribute(false); refresh(); }));
                body.Children.Add(di);
            }
        }
        var ed = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (shapes.Count >= 2) ed.Children.Add(Small("Group", () => { tool.GroupSelection(); refresh(); }));
        if (shapes.Any(s => s.Group.Length > 0)) ed.Children.Add(Small("Ungroup", () => { tool.UngroupSelection(); refresh(); }));
        ed.Children.Add(Small("Duplicate", () => { tool.Duplicate(); refresh(); }));
        ed.Children.Add(Small("Copy", () => { tool.Copy(); refresh(); }));
        ed.Children.Add(Small("Paste", () => { tool.Paste(); refresh(); }));
        ed.Children.Add(Small("Delete", () => { tool.DeleteSelection(); refresh(); }));
        body.Children.Add(ed);
    }

    static void CalloutProps(StackPanel body, TextBlock title, DrawTool tool, List<Callout> list, Action<string, Action<Callout>> DoC, Action refresh)
    {
        var c0 = list[0];
        title.Text = list.Count == 1 ? "Label" : list.Count + " labels";
        body.Children.Add(Ui.Text("A label stuck to a device or cable: it moves with it. Drag it to place it.", 13, Ui.Muted));
        if (list.Count == 1)
        {
            body.Children.Add(Label("Text"));
            var tb = new TextBox { Text = c0.Text, MinHeight = 44 };
            void Commit() { var v = (tb.Text ?? "").Trim(); if (v.Length > 0 && v != c0.Text) DoC("edit label", x => x.Text = v); }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            body.Children.Add(tb);
            body.Children.Add(Small("Set the text", Commit));
        }
        body.Children.Add(Label("Text colour"));
        body.Children.Add(Swatches(c0.Color, false, c => DoC("label colour", x => x.Color = c)));
        body.Children.Add(Label("Background"));
        body.Children.Add(Swatches(c0.Fill, true, c => DoC("label background", x => x.Fill = c)));
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = "Size", Foreground = Ui.Brush(Ui.Muted), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
        row.Children.Add(Combo(Nums(9, 10, 11, 12, 13, 14, 16, 18, 22), N(c0.FontSize), v => DoC("label size", x => x.FontSize = NetAsk.Number(v)), 80));
        body.Children.Add(row);
        body.Children.Add(Small("Delete the label", () => { tool.DeleteSelection(); refresh(); }));
    }

    // ================================================================= layers
    public static readonly Dictionary<string, string> LayerTips = new()
    {
        ["map"] = "The map or picture under everything", ["drawings"] = "Shapes, objects, areas, notes and pictures", ["devices"] = "Computers, switches, routers...",
        ["cables"] = "Cables and Wi-Fi links", ["labels"] = "Text, labels on devices and cables, IP addresses, port names and VLANs",
    };

    public static void Layers(StackPanel body, DrawTool tool, Action refresh)
    {
        var h = Ui.Text("Layers", 16, Ui.TextC, true);
        h.Margin = new Thickness(0, 14, 0, 2);
        body.Children.Add(h);
        if (tool?.Host.DrawProject == null || !tool.CanDraw) { body.Children.Add(Ui.Text("Open the Logical view, the Campus map or a floor plan.", 13, Ui.Muted)); return; }
        string key = Drawings.LayerKeyOf(tool.Host.DrawView);
        h.Text = "Layers (" + (key == "logical" ? "Logical view" : key == "campus" ? "Campus map" : "floor plans") + ")";
        body.Children.Add(Ui.Text("A hidden layer isn't drawn; a locked one can't be moved (taps go through to what is under it, and locked devices still open).", 12.5, Ui.Muted));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < Drawings.LayerIds.Length; i++)
        {
            string id = Drawings.LayerIds[i];
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = Drawings.LayerNames[i], Foreground = Ui.Brush(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            ToolTip.SetTip(name, LayerTips[id]);
            var show = Check("Show", tool.LayerShows(id), v => { tool.SetLayer(id, true, v); refresh(); });
            var lk = Check("Lock", tool.LayerLocked(id), v => { tool.SetLayer(id, false, v); refresh(); });
            Grid.SetRow(name, i); Grid.SetRow(show, i); Grid.SetRow(lk, i); Grid.SetColumn(show, 1); Grid.SetColumn(lk, 2);
            g.Children.Add(name); g.Children.Add(show); g.Children.Add(lk);
        }
        body.Children.Add(g);
    }
}

/// <summary>The library of objects (buildings, map pieces, furniture, outdoor, telecom, fun): tap one, then tap where it goes.</summary>
public sealed class ObjectLibrary : StackPanel
{
    readonly TextBox search;
    readonly StackPanel list = new() { Spacing = 2 };
    public event Action<string> Chosen;
    public int Shown { get; private set; }

    public ObjectLibrary()
    {
        Spacing = 6;
        var h = Ui.Text("Objects", 16, Ui.TextC, true);
        h.Margin = new Thickness(0, 14, 0, 0);
        Children.Add(h);
        search = new TextBox { Watermark = "Search: tree, car, desk, road...", MinHeight = 44 };
        Children.Add(search);
        Children.Add(Ui.Text("Tap one, then tap where it goes. Drag its handles to size and turn it.", 12.5, Ui.Muted));
        Children.Add(list);
        search.TextChanged += (s, e) => Fill();
        Fill();
    }

    public string Search { get => search.Text; set { search.Text = value ?? ""; Fill(); } }

    void Fill()
    {
        list.Children.Clear();
        Shown = 0;
        foreach (var cat in ObjectCatalog.Categories)
        {
            var items = ObjectCatalog.Find(cat, search.Text);
            if (items.Count == 0) continue;
            var h = Ui.Text(cat, 13, Ui.AccentText, true); h.Margin = new Thickness(0, 8, 0, 4);
            list.Children.Add(h);
            var wp = new WrapPanel();
            foreach (var a in items)
            {
                var aa = a;
                var sp = new StackPanel { Spacing = 2, Children = { new ObjectIcon(a.Kind, 44) { HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = a.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(Ui.TextC) } } };
                var b = new Button { Content = sp, Width = 78, MinHeight = 84, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(2), Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC), HorizontalContentAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(b, a.Name + " (" + a.Category.ToLowerInvariant() + ")");
                b.Click += (s, e) => Chosen?.Invoke(aa.Kind);
                wp.Children.Add(b);
                Shown++;
            }
            list.Children.Add(wp);
        }
        if (Shown == 0) list.Children.Add(Ui.Text("Nothing matches. Try tree, car, desk, road or house.", 13, Ui.Muted));
    }
}
