// Network Lab - the page (App Hub's NetworkLab.ps1 window): the toolbar (Move, Cable, Ping, Delete, Undo, Redo, Fit,
// Simulation, Labs, More), the canvas, the side panel (the lab: story, goals that tick themselves off, hints, the
// answer and "Do it for me"; and in Simulation mode the step controls, the event list and what's inside an envelope),
// the status line and the palette of devices along the bottom. On a phone the side panel takes the screen while open.
// The network is kept in <data>\NetworkLab\autosave.netlab (written safely about once a minute and when the page
// closes) and opens again next time; on Windows, App Hub's own autosave is brought across once. Open / Save use the
// device's file picker (.netlab files are the same as App Hub's).
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Saves;
using LearningLab.Services;
using LearningLab.Views;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

public sealed class NetLabPage : IPage, IKeyPage
{
    public string Title => "Network Lab";
    public Control View => root;

    public Project P { get; private set; }
    public NetCanvas Canvas { get; } = new();
    /// <summary>The physical view: campus, floor plans, racks.</summary>
    public PhysicalView Phys { get; } = new();
    /// <summary>The Physical view is shown (else the Logical view).</summary>
    public bool Physical { get; private set; }
    /// <summary>The drawing bar is shown along the bottom (instead of the devices).</summary>
    public bool Drawing { get; private set; }
    /// <summary>What the side panel shows: lab (the lab and Simulation mode) or draw (properties, layers, objects, map).</summary>
    public string SideMode { get; private set; } = "lab";
    /// <summary>The drawing tools of the view shown.</summary>
    public DrawTool CurrentDraw => Physical ? Phys.Draw : Canvas.Draw;
    /// <summary>The objects library in the side panel (while it shows the drawing panels).</summary>
    public ObjectLibrary Library { get; private set; }
    public NetSim Sim { get; } = new();
    public UndoStack Undo { get; } = new();
    public Lab Lab { get; private set; }
    public bool[] Goals { get; private set; } = Array.Empty<bool>();
    public string StatusText => status.Text;
    public bool PanelOpen { get; private set; }

    readonly Grid root = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
    readonly StackPanel physBar = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 8, 6) };
    readonly ScrollViewer physScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
    readonly ComboBox layoutPick = new() { MinHeight = 44, MinWidth = 150 }, floorPick = new() { MinHeight = 44, MinWidth = 170 }, cablePick = new() { MinHeight = 44, MinWidth = 150 };
    readonly StackPanel drawBar = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4, 8, 8) };
    ScrollViewer palScroll, drawScroll;
    Button viewButton, drawButton, finishButton;
    readonly Dictionary<string, Button> floorTools = new(), drawTools = new();
    bool filling;
    readonly Grid main = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    readonly WrapPanel bar = new() { Margin = new Thickness(8, 6, 8, 2) };
    readonly TextBlock status = new() { FontSize = 13, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 4) };
    readonly Border side = new() { Width = 360, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1, 0, 0, 0), IsVisible = false };
    readonly StackPanel sideBody = new() { Spacing = 8, Margin = new Thickness(14, 12, 14, 24) };
    readonly Dictionary<string, Button> toolButtons = new();
    Button simButton, labsButton, undoButton, redoButton;
    bool wide = true, labDirty;
    DateTime lastEdit = DateTime.MinValue, lastAutosave = DateTime.Now;
    int savedVersion;
    string filePath = "", hint = "";
    int hintIndex;
    bool showAnswer;
    readonly JsonObject settings;
    readonly string dataDir, settingsFile, autosaveFile;

    public NetLabPage()
    {
        dataDir = Data.For("NetworkLab");
        settingsFile = Path.Combine(dataDir, "settings.json");
        autosaveFile = Path.Combine(dataDir, "autosave.netlab");
        settings = LoadSettings();
        Brand.LoadBuiltIn(Str("brand"));
        Canvas.ShowAddresses = Bool("showAddresses", true);
        Canvas.ShowPorts = Bool("showPorts", false);

        // ---- the toolbar
        Tool("select", "✋  Move", "Move devices, pan and zoom; tap a device to open it");
        Tool("connect", "🔌  Cable", "Tap one device, then another: the cable and ports are picked for you");
        Tool("pdu", "✉  Ping", "Tap a computer, then the device to ping (watch it in Simulation mode)");
        Tool("delete", "🗑  Delete", "Tap a device, cable or note to delete it");
        viewButton = BarButton("🏢  Physical", "Switch between the Logical view (how things are connected) and the Physical view (where things are: buildings, floors, racks)", () => ShowPhysical(!Physical));
        drawButton = BarButton("✏  Draw", "Shapes, labels, objects and maps on the view", () => ShowDrawBar(!Drawing));
        undoButton = BarButton("↶", "Undo", () => { if (Undo.CanUndo) { Undo.Undo(); MarkEdited(); } });
        redoButton = BarButton("↷", "Redo", () => { if (Undo.CanRedo) { Undo.Redo(); MarkEdited(); } });
        BarButton("⤢  Fit", "Show the whole network", () => { if (Physical) Phys.Fit(); else Canvas.Fit(); });
        simButton = BarButton("⏱  Simulation", "Stop time and watch each message travel, step by step", () => SetSimulation(!Sim.Simulation));
        labsButton = BarButton("📚  Labs", "Guided practice: a story, goals that tick themselves off, hints and the answer", () => TogglePanel());
        var more = BarButton("⋯  More", "New, Open, Save, settings and help", null);
        more.Click += (s, e) => ShowMore(more);
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);
        BuildPhysBar();
        physScroll.Content = physBar;
        Grid.SetRow(physScroll, 1);
        root.Children.Add(physScroll);

        // ---- the canvas and the side panel
        main.Children.Add(Canvas);
        Phys.IsVisible = false;
        main.Children.Add(Phys);
        side.Child = new ScrollViewer { Content = sideBody };
        Grid.SetColumn(side, 1);
        main.Children.Add(side);
        Grid.SetRow(main, 2);
        root.Children.Add(main);
        Grid.SetRow(status, 3);
        root.Children.Add(status);

        // ---- the palette
        var pal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4, 8, 8) };
        foreach (var k in Catalog.Kinds)
        {
            string kind = k;
            var b = new Button
            {
                Content = new StackPanel { Spacing = 2, Children = { new DeviceIcon(kind, Catalog.DefaultOs(kind), 40) { HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = Catalog.KindName(kind), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Brush(Ui.TextC) } } },
                MinWidth = 76, MinHeight = 64, Padding = new Thickness(6, 4), Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC),
            };
            ToolTip.SetTip(b, Catalog.Explain(kind));
            b.Click += (s, e) => AddDevice(kind);
            pal.Children.Add(b);
        }
        palScroll = new ScrollViewer { Content = pal, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        BuildDrawBar();
        drawScroll = new ScrollViewer { Content = drawBar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
        root.Children.Add(new Border { Child = new Panel { Children = { palScroll, drawScroll } }, Background = Ui.Brush(Ui.Surface), [Grid.RowProperty] = 4 });

        // ---- wiring
        Canvas.Undo = Undo;
        Canvas.Status += Say;
        Canvas.Edited += MarkEdited;
        Canvas.DeviceOpened += OpenDevice;
        Canvas.MenuAsked += ShowMenu;
        Canvas.PortAsked += AskPort;
        Canvas.PduRequested += (a, b) => Sim.SimplePdu(a, b, m => Dispatcher.UIThread.Post(() => Say(m)));
        Canvas.EnvelopeClicked += e => FillSide();
        Canvas.SelectionChanged += DrawSelectionChanged;
        Canvas.ToolChanged += ToolButtons;
        Canvas.Draw.PictureWanted += (x, y) => _ = AddPictureAt(x, y);
        Phys.Undo = Undo;
        Phys.Status += Say;
        Phys.Edited += MarkEdited;
        Phys.DeviceOpened += OpenDevice;
        Phys.SelectionChanged += DrawSelectionChanged;
        Phys.ToolChanged += ToolButtons;
        Phys.LevelChanged += () => { FillFloors(); if (SideMode == "draw") FillSide(); UpdateButtons(); };
        Phys.Draw.PictureWanted += (x, y) => _ = AddPictureAt(x, y);
        Phys.ShowRange = Bool("showRange", true);
        Canvas.ShowVlans = Bool("showVlans", false);
        Sim.View = Canvas;
        Sim.Stepped += () => { if (Sim.LastMessage.Length > 0) Say(Sim.LastMessage); FillSide(); };
        Sim.Ticked += Tick;
        Undo.Changed += (s, e) => UpdateButtons();
        DevicePage.Edited = MarkEdited;
        DevicePage.Undo = Undo;
        root.SizeChanged += (s, e) => Layout(e.NewSize.Width);
        root.AttachedToVisualTree += (s, e) => { if (!Sim.Running) Sim.Start(); };

        // ---- the network from last time (or App Hub's, once)
        Project start = null;
        if (!File.Exists(autosaveFile)) BringAcrossFromAppHub();
        if (File.Exists(autosaveFile))
        {
            string text = SafeFile.LoadText(autosaveFile, t => t.TrimStart().StartsWith("{"), out var info);
            try { if (text != null) start = Persist.Load(text); } catch (Exception) { start = null; }
            if (start == null && info.Message.Length > 0) Say(info.Message);
            else if (start != null) Say("Your network from last time is back.");
        }
        Use(start ?? new Project(Environment.TickCount & 0xFFFF), "");
        if (start == null) Say("Add devices from the bar at the bottom, connect them with Cable, then tap a device to set it up. Or open Labs for guided practice.");
        SetTool("select");
    }

    // ------------------------------------------------------------ settings
    JsonObject LoadSettings()
    {
        try
        {
            string t = File.Exists(settingsFile) ? SafeFile.LoadText(settingsFile, x => x.TrimStart().StartsWith("{"), out _) : null;
            if (t != null && JsonNode.Parse(t) is JsonObject o) return o;
        }
        catch (Exception) { }
        return new JsonObject();
    }
    string Str(string k) => settings[k] is JsonValue v && v.TryGetValue(out string s) ? s : "";
    bool Bool(string k, bool d) => settings[k] is JsonValue v && v.TryGetValue(out bool b) ? b : d;
    void SaveSettings()
    {
        settings["showAddresses"] = Canvas.ShowAddresses; settings["showPorts"] = Canvas.ShowPorts;
        settings["showVlans"] = Canvas.ShowVlans; settings["showRange"] = Phys.ShowRange;
        settings["lastLab"] = Lab?.Id ?? Str("lastLab");
        try { SafeFile.WriteText(settingsFile, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); } catch (Exception) { }
    }

    /// <summary>On Windows: App Hub's autosave (the network left open there) comes across once.</summary>
    void BringAcrossFromAppHub()
    {
        if (Bool("broughtAcross", false)) return;
        settings["broughtAcross"] = true;
        string old = Data.AppHubFolder("NetworkLab");
        if (old == null) return;
        try
        {
            string theirs = Path.Combine(old, "autosave.netlab");
            if (File.Exists(theirs)) File.Copy(theirs, autosaveFile, false);
            var s = Path.Combine(old, "settings.json");
            if (File.Exists(s) && JsonNode.Parse(File.ReadAllText(s)) is JsonObject o && o["Brand"] is JsonValue b && b.TryGetValue(out string brand)) settings["brand"] = brand;
        }
        catch (Exception) { }
        SaveSettings();
    }

    // ------------------------------------------------------------ the network
    public void Use(Project p, string file)
    {
        DevicePage.ForgetSessions();
        P?.Sim.Clear();
        P = p;
        filePath = file ?? "";
        Canvas.P = p;
        Canvas.Selection.Clear();
        Canvas.CancelTool();
        Phys.P = p;
        Phys.Selection.Clear();
        Phys.CancelTool();
        Phys.Level = "floor";
        Phys.FloorKey = p.Layout.DefaultFloor?.Key ?? "";
        FillLayouts();
        FillFloors();
        Sim.Attach(p);
        if (Sim.Simulation) Sim.SetSimulation(true);
        Undo.Clear();
        savedVersion = p.Version;
        Lab = string.IsNullOrEmpty(p.LabId) ? null : Labs.Get(p.LabId);
        Goals = Lab == null ? Array.Empty<bool>() : new bool[Lab.Goals.Count];
        hint = ""; hintIndex = 0; showAnswer = false;
        labDirty = Lab != null;
        Dispatcher.UIThread.Post(() => { Canvas.Fit(); Phys.Fit(); }, DispatcherPriority.Loaded);
        FillSide();
        UpdateButtons();
    }

    public void MarkEdited()
    {
        labDirty = true;
        lastEdit = DateTime.Now;
        Canvas.InvalidateVisual();
        Phys.InvalidateVisual();
        UpdateButtons();
    }

    void Say(string m) { status.Text = m ?? ""; }

    public Device AddDevice(string kind)
    {
        var at = Canvas.NewSpot();
        var d = Edits.AddDevice(P, Undo, kind, at.X, at.Y);
        if (Physical) { Phys.PlaceHere(d); Phys.Selection.Clear(); Phys.Selection.Add(d); }
        Canvas.Selection.Clear(); Canvas.Selection.Add(d);
        Say(d.Name + " added. Drag it where you like, tap it to set it up, or use Cable to connect it.");
        MarkEdited();
        return d;
    }

    void OpenDevice(Device d) => MainView.Current.Push(new DevicePage(d));

    void Tick()
    {
        var now = DateTime.Now;
        if (labDirty && Lab != null && (now - lastEdit).TotalMilliseconds > 1200) { labDirty = false; CheckGoals(); }
        if ((now - lastAutosave).TotalSeconds >= 60) { lastAutosave = now; Autosave(); }
    }

    /// <summary>Writes the network to the autosave (safely: never half a file).</summary>
    public void Autosave()
    {
        if (P == null) return;
        try
        {
            if (P.Devices.Count == 0) { if (File.Exists(autosaveFile)) File.Delete(autosaveFile); return; }
            SafeFile.WriteText(autosaveFile, Persist.Save(P), false);
        }
        catch (Exception) { }
    }

    public void Closed()
    {
        Sim.Stop();
        Autosave();
        SaveSettings();
    }

    // ------------------------------------------------------------ the toolbar
    readonly Dictionary<Button, (string Full, string Short)> labels = new();

    Button BarButton(string text, string tip, Action a)
    {
        var b = Ui.Button(text, false, 14);
        int gap = text.IndexOf("  ", StringComparison.Ordinal);
        labels[b] = (text, gap > 0 ? text.Substring(0, gap) : text);
        b.MinHeight = 44; b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(12, 4);
        ToolTip.SetTip(b, tip);
        if (a != null) b.Click += (s, e) => a();
        bar.Children.Add(b);
        return b;
    }

    void Tool(string id, string text, string tip) { toolButtons[id] = BarButton(text, tip, () => SetTool(id)); }

    public void SetTool(string id)
    {
        Canvas.CancelTool();
        Phys.CancelTool();
        if (!Physical && (floorTools.ContainsKey(id) && id != "select")) id = "select";
        Canvas.Tool = DrawTool.IsDrawTool(id) || toolButtons.ContainsKey(id) ? id : "select";
        Phys.Tool = id;
        ToolButtons(id);
        if (id == "room") Say("Room: drag a box for the room, then give it a name.");
        if (id == "wall") Say("Wall: drag along where the wall goes (walls run straight across or down).");
        if (id == "door") Say("Door: tap a wall to put a door in it.");
        if (id == "label") Say("Label: tap where the label goes (Desk 4, Printer corner...).");
        if (id == "rack") Say("Rack: tap where the rack stands (usually in the wiring closet).");
        if (id.StartsWith("obj:")) Say("Tap where the " + (ObjectCatalog.Get(id.Substring(4))?.Name ?? "object").ToLowerInvariant() + " goes.");
        if (id.StartsWith("draw:")) Say(DrawTips.TryGetValue(id, out var tip) ? tip : "Tap or drag on the view.");
        if (id == "connect") Say(Physical ? "Cable: tap a device and pick its port, then the other device and its port." : "Cable: tap one device, then the other. For a particular port, hold a device and pick \"Cable from a port\".");
        if (id == "pdu") Say("Ping: tap a computer, then the device to ping. In Simulation mode you can watch it travel.");
        if (id == "delete") Say("Delete: tap a device, cable or note. Undo brings it back.");
    }

    void UpdateButtons()
    {
        if (undoButton != null) { undoButton.IsEnabled = Undo.CanUndo; redoButton.IsEnabled = Undo.CanRedo; }
        if (simButton != null) simButton.Background = Ui.Brush(Sim.Simulation ? "#7C3AED" : Ui.Surface3);
        if (labsButton != null) labsButton.Background = Ui.Brush(PanelOpen && SideMode == "lab" ? Ui.Accent : Ui.Surface3);
        if (viewButton != null)
        {
            labels[viewButton] = Physical ? ("🖧  Logical", "🖧") : ("🏢  Physical", "🏢");
            viewButton.Content = root.Bounds.Width > 0 && root.Bounds.Width < 640 ? labels[viewButton].Short : labels[viewButton].Full;
            drawButton.Background = Ui.Brush(Drawing ? Ui.Accent : Ui.Surface3);
        }
        if (finishButton != null) finishButton.IsVisible = CurrentDraw.DrawingLines;
        foreach (var b in floorTools.Values) b.IsVisible = Phys.Level == "floor";
    }

    void Layout(double w)
    {
        wide = w >= 820;
        foreach (var kv in labels) kv.Key.Content = w < 640 ? kv.Value.Short : kv.Value.Full;
        side.Width = wide ? 360 : double.NaN;
        Grid.SetColumn(side, wide ? 1 : 0);
        Grid.SetColumnSpan(side, wide ? 1 : 2);
        side.BorderThickness = new Thickness(wide ? 1 : 0, 0, 0, 0);
    }

    public void TogglePanel()
    {
        if (PanelOpen && SideMode != "lab") { SideMode = "lab"; FillSide(); UpdateButtons(); return; }
        SideMode = "lab";
        PanelOpen = !PanelOpen || Sim.Simulation && !PanelOpen; side.IsVisible = PanelOpen; FillSide(); UpdateButtons();
    }
    void OpenPanel() { SideMode = "lab"; PanelOpen = true; side.IsVisible = true; FillSide(); UpdateButtons(); }
    /// <summary>Opens the side panel with the drawing panels: properties of the selection, layers, objects, map, labels.</summary>
    public void ShowDrawPanel() { SideMode = "draw"; PanelOpen = true; side.IsVisible = true; FillSide(); UpdateButtons(); }
    void ClosePanel() { PanelOpen = false; side.IsVisible = false; UpdateButtons(); }

    public void SetSimulation(bool on)
    {
        Sim.SetSimulation(on);
        if (on) { OpenPanel(); Say("Simulation mode: time stops. Send a ping (the Ping tool, or ping from a PC's command line), then press Forward or Play to watch each message."); }
        else Say("Realtime: the network runs by itself again.");
        FillSide();
        UpdateButtons();
    }

    void ShowMore(Control anchor)
    {
        var m = new MenuFlyout();
        void Item(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => a(); m.Items.Add(mi); }
        Item("New network", () => ConfirmDiscard(() => { Use(new Project(Environment.TickCount & 0xFFFF), ""); Say("A new, empty network."); }));
        Item("Open a network...", () => ConfirmDiscard(() => _ = OpenFile()));
        Item("Save", () => _ = SaveFile(false));
        Item("Save as...", () => _ = SaveFile(true));
        m.Items.Add(new Separator());
        var a = new MenuItem { Header = "Show IP addresses under devices", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Canvas.ShowAddresses };
        a.Click += (s, e) => { Canvas.ShowAddresses = !Canvas.ShowAddresses; Canvas.InvalidateVisual(); SaveSettings(); };
        var b = new MenuItem { Header = "Show port names on cables", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Canvas.ShowPorts };
        b.Click += (s, e) => { Canvas.ShowPorts = !Canvas.ShowPorts; Canvas.InvalidateVisual(); SaveSettings(); };
        m.Items.Add(a); m.Items.Add(b);
        Item("Device brand...", AskBrand);
        m.Items.Add(new Separator());
        Item("How Network Lab works", () => MainView.Current.Push(new NetHelpPage()));
        m.ShowAt(anchor);
    }

    void ConfirmDiscard(Action then)
    {
        if (P == null || P.Version == savedVersion || P.Devices.Count == 0) { then(); return; }
        MainView.Current.Message("Network Lab", "Start something else? This network hasn't been saved to a file (it is kept only until you start another one).",
            ("Save it first", () => _ = SaveFile(false).ContinueWith(t => Dispatcher.UIThread.Post(() => { if (t.Result) then(); }))), ("Don't save", then), ("Cancel", null));
    }

    static readonly FilePickerFileType NetFiles = new("Network Lab networks") { Patterns = new[] { "*.netlab" }, MimeTypes = new[] { "application/json", "application/octet-stream" } };

    async Task OpenFile()
    {
        var top = TopLevel.GetTopLevel(View);
        if (top?.StorageProvider == null || !top.StorageProvider.CanOpen || Data.Testing) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a network", AllowMultiple = false, FileTypeFilter = new[] { NetFiles, FilePickerFileTypes.All } });
            if (files.Count == 0) return;
            string text;
            await using (var s = await files[0].OpenReadAsync())
            using (var r = new StreamReader(s)) text = await r.ReadToEndAsync();
            Use(Persist.Load(text), files[0].TryGetLocalPath() ?? "");
            Say("Opened " + files[0].Name + ".");
        }
        catch (Exception e) { Say("Couldn't open that file: " + e.Message); }
    }

    async Task<bool> SaveFile(bool asNew)
    {
        var top = TopLevel.GetTopLevel(View);
        string text = Persist.Save(P);
        try
        {
            if (!asNew && filePath.Length > 0 && File.Exists(filePath)) { SafeFile.WriteText(filePath, text, false); savedVersion = P.Version; Say("Saved " + Path.GetFileName(filePath) + "."); return true; }
            if (top?.StorageProvider == null || !top.StorageProvider.CanSave || Data.Testing) return false;
            string suggested = filePath.Length > 0 ? Path.GetFileName(filePath) : Lab != null ? Lab.Id + ".netlab" : "my network.netlab";
            var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save the network", SuggestedFileName = suggested, DefaultExtension = "netlab", FileTypeChoices = new[] { NetFiles } });
            if (f == null) return false;
            string local = f.TryGetLocalPath();
            if (local != null) SafeFile.WriteText(local, text, false);
            else
            {
                await using var s = await f.OpenWriteAsync();
                s.SetLength(0);
                await s.WriteAsync(new System.Text.UTF8Encoding(false).GetBytes(text));
            }
            filePath = local ?? "";
            savedVersion = P.Version;
            Say("Saved " + f.Name + ".");
            return true;
        }
        catch (Exception e) { Say("Couldn't save it: " + e.Message); return false; }
    }

    void AskBrand()
    {
        var box = new TextBox { Text = Str("brand"), Watermark = Brand.ShippedName, MinWidth = 220 };
        Ask("Device brand", "The made-up maker of the routers and switches (shown on labels, banners and show version). Leave it empty for the usual one: " + Brand.ShippedName + ".", box, () =>
        {
            settings["brand"] = (box.Text ?? "").Trim();
            Brand.SetName(Str("brand"));
            SaveSettings();
            Say("Device brand: " + Brand.Name + ".");
        });
    }

    /// <summary>A small question with a text box, over the page.</summary>
    void Ask(string title, string text, TextBox box, Action ok)
    {
        var fl = new Flyout();
        var go = Ui.Button("OK", true, 14); go.MinHeight = 40;
        var sp = new StackPanel { Spacing = 8, Width = 300, Children = { Ui.Text(title, 16, Ui.TextC, true), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Brush(Ui.Muted) }, box, go } };
        go.Click += (s, e) => { fl.Hide(); ok(); };
        box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { fl.Hide(); ok(); } };
        fl.Content = sp;
        fl.ShowAt(Canvas, true);
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
    }

    // ------------------------------------------------------------ menus on the canvas
    void ShowMenu(object o, Point at)
    {
        var m = new MenuFlyout();
        void Item(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => { try { a(); } catch (Exception ex) { Say(ex.Message); } }; m.Items.Add(mi); }
        if (o is Device d)
        {
            Item("Open " + d.Name, () => OpenDevice(d));
            Item("What is this?", () => MainView.Current.Message(d.Name, NetCanvas.DeviceTip(d)));
            Item("Rename...", () =>
            {
                var box = new TextBox { Text = d.Name, MinWidth = 220 };
                Ask("Rename", "New name for " + d.Name + ":", box, () => { var n = (box.Text ?? "").Trim(); if (n.Length > 0 && n != d.Name) { Edits.Rename(P, Undo, d, n); MarkEdited(); } });
            });
            Item(d.PoweredOn ? "Switch off" : "Switch on", () => { d.PoweredOn = !d.PoweredOn; P.Sim.TopologyChanged(); if (d.PoweredOn && d.Host != null) d.Host.Start(); if (d.PoweredOn && d.Ios != null) Persist.Reload(d); MarkEdited(); });
            var free = d.Ports.Where(p => !p.IsWireless && p.Links.Count == 0).ToList();
            if (free.Count > 0)
            {
                var sub = new MenuItem { Header = "Cable from a port" };
                foreach (var p in free)
                {
                    var pp = p;
                    var mi = new MenuItem { Header = PortLabel(p) };
                    mi.Click += (s, e) => { SetTool("connect"); Canvas.PortFrom = pp; Say("Cable from " + pp.FullName + ": now tap the other device."); };
                    sub.Items.Add(mi);
                }
                m.Items.Add(sub);
            }
            Item("Add a label...", () => Canvas.Draw.AskLabel(d));
            Item("Delete", () => { Edits.Delete(P, Undo, new List<object> { d }); Canvas.Selection.Clear(); MarkEdited(); });
        }
        else if (o is Link l)
        {
            Item("About this cable", () => MainView.Current.Message("Cable", NetCanvas.LinkTip(l)));
            Item("Add a label...", () => Canvas.Draw.AskLabel(l));
            if (!l.IsWireless) Item("Delete the cable", () => { Edits.Delete(P, Undo, new List<object> { l }); Canvas.Selection.Clear(); MarkEdited(); });
        }
        else if (o is Shape || o is Callout)
        {
            foreach (var (label, act) in Canvas.Draw.MenuItems(o)) Item(label, () => { act(); MarkEdited(); });
            Item("Properties...", ShowDrawPanel);
        }
        else if (o is Note n)
        {
            Item("Edit the note...", () => { var box = new TextBox { Text = n.Text, MinWidth = 240, AcceptsReturn = true }; Ask("Note", "", box, () => { Edits.EditNote(P, Undo, n, box.Text ?? ""); MarkEdited(); }); });
            Item("Delete the note", () => { Edits.Delete(P, Undo, new List<object> { n }); MarkEdited(); });
        }
        m.ShowAt(Canvas, true);
    }

    static string PortLabel(Port p) => p.Name + (p.Media == "sfp" ? (p.Sfp == "" ? "  (empty SFP slot)" : "  (" + (p.Sfp == "rj45" ? "copper" : p.Sfp == "mmf" ? "multimode" : "single-mode") + " module)") : p.IsSerial ? "  (console)" : "");

    /// <summary>A cable from a picked port reached its second device: which of its ports.</summary>
    void AskPort(Device d, Point at)
    {
        var from = Canvas.PortFrom;
        if (from == null) return;
        var m = new MenuFlyout();
        m.Items.Add(new MenuItem { Header = "Cable to " + d.Name + ": pick a port", IsEnabled = false });
        foreach (var p in d.Ports.Where(x => !x.IsWireless && x.IsSerial == from.IsSerial))
        {
            var pp = p;
            var mi = new MenuItem { Header = PortLabel(p) + (p.Links.Count > 0 ? "  (in use)" : ""), IsEnabled = p.Links.Count == 0 };
            mi.Click += (s, e) => { Canvas.PortFrom = null; Canvas.MakeCable(from, pp, "auto"); SetTool("select"); };
            m.Items.Add(mi);
        }
        if (m.Items.Count == 1) { Say(d.Name + " has no ports for this cable."); return; }
        m.ShowAt(Canvas, true);
    }

    // ------------------------------------------------------------ labs
    public void StartLab(Lab l)
    {
        try
        {
            Use(l.Build(), "");
            Lab = l;
            Goals = new bool[l.Goals.Count];
            settings["lastLab"] = l.Id; SaveSettings();
            OpenPanel();
            Say("Lab started: " + l.Title + ". Read the story; the goals tick themselves off as you go.");
        }
        catch (Exception e) { Say("The lab couldn't be built: " + e.Message); }
    }

    /// <summary>Checks the goals (on a settled copy; the network itself isn't touched).</summary>
    public void CheckGoals()
    {
        if (Lab == null || P == null) return;
        var r = Lab.Check(P);
        Goals = r;
        if (r.Length > 0 && r.All(x => x) && !P.LabDone.ContainsKey(Lab.Id))
        {
            P.LabDone[Lab.Id] = true;
            Say("Lab complete: " + Lab.Title + ". Well done! Try the next one.");
            Feel.Done();
        }
        FillSide();
    }

    /// <summary>"Do it for me": carries out the answer in the network.</summary>
    public List<string> DoAnswer()
    {
        if (Lab == null) return new List<string>();
        var errs = Script.Run(P, Lab.Answer);
        MarkEdited();
        Say(errs.Count > 0 ? "Some steps didn't work in your network: " + string.Join("; ", errs) : "Done. Watch the goals tick.");
        CheckGoals();
        return errs;
    }

    public string NextHint()
    {
        if (Lab == null) return "";
        var hints = new List<string>();
        for (int i = 0; i < Lab.Goals.Count; i++) if (i < Goals.Length && !Goals[i] && Lab.Goals[i].Hint.Length > 0) hints.Add(Lab.Goals[i].Hint);
        hints.AddRange(Lab.Hints);
        if (hints.Count == 0) return "";
        hint = hints[hintIndex++ % hints.Count];
        FillSide();
        return hint;
    }

    void FillSide()
    {
        if (!PanelOpen) return;
        sideBody.Children.Clear();
        Library = null;
        var top = new DockPanel();
        var close = Ui.Button(wide ? "✕" : "Back to the network", false, 13); close.MinHeight = 44;
        close.Click += (s, e) => ClosePanel();
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(Ui.Text(SideMode == "draw" ? "Drawing" : Sim.Simulation ? "Simulation" : "Labs", 18, Ui.TextC, true));
        sideBody.Children.Add(top);
        if (SideMode == "draw") { DrawSide(); return; }
        if (Sim.Simulation) SimPart();
        LabPart();
    }

    void LabPart()
    {
        if (Sim.Simulation) { var h = Ui.Text("Labs", 16, Ui.TextC, true); h.Margin = new Thickness(0, 14, 0, 0); sideBody.Children.Add(h); }
        var all = Labs.All;
        var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        pick.ItemsSource = new[] { "Free play (no lab)" }.Concat(all.Select(l => l.Title + "  ·  " + l.Level + (P.LabDone.ContainsKey(l.Id) ? "  ✓" : ""))).ToList();
        string sel = Lab?.Id ?? Str("lastLab");
        pick.SelectedIndex = Math.Max(0, all.FindIndex(l => l.Id == sel) + 1);
        var shown = pick.SelectedIndex > 0 ? all[pick.SelectedIndex - 1] : null;
        pick.SelectionChanged += (s, e) => { settings["lastLab"] = pick.SelectedIndex > 0 ? all[pick.SelectedIndex - 1].Id : ""; FillSide(); };
        sideBody.Children.Add(pick);
        if (shown == null)
        {
            sideBody.Children.Add(Wrap(Ui.Text("Build anything you like. Pick a lab for guided practice: each lab builds a starting network, tells a short story, and ticks off the goals as you reach them.", 13.5, Ui.Muted)));
            return;
        }
        bool active = Lab != null && Lab.Id == shown.Id;
        sideBody.Children.Add(Ui.Text(shown.Title, 17, Ui.TextC, true));
        sideBody.Children.Add(Ui.Text(shown.Level + "  ·  " + shown.Topic, 12.5, Ui.Muted));
        sideBody.Children.Add(Wrap(Ui.Text(shown.Story, 14, Ui.TextC)));
        var start = Ui.Button(active ? "Start again" : "Start this lab", !active, 15); start.MinHeight = 44; start.HorizontalAlignment = HorizontalAlignment.Left;
        start.Click += (s, e) => ConfirmDiscard(() => StartLab(shown));
        sideBody.Children.Add(start);
        int done = active ? Goals.Count(x => x) : 0;
        sideBody.Children.Add(Ui.Text(!active ? "Goals (start the lab to begin)" : done == shown.Goals.Count ? "All " + done + " goals done. Well done!" : "Goals: " + done + " of " + shown.Goals.Count + " done", 14, active && done == shown.Goals.Count ? Ui.Green : Ui.AccentText, true));
        for (int i = 0; i < shown.Goals.Count; i++)
        {
            bool ok = active && i < Goals.Length && Goals[i];
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Ui.Brush(ok ? "#16A34A" : Ui.Surface3), Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = ok ? "✓" : "", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold } };
            var row = new DockPanel();
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(Wrap(Ui.Text(shown.Goals[i].Text, 13.5, Ui.TextC)));
            sideBody.Children.Add(row);
        }
        if (!active) return;
        var btns = new WrapPanel();
        Button B(string t, Action a) { var b = Ui.Button(t, false, 14); b.MinHeight = 44; b.Margin = new Thickness(0, 0, 6, 6); b.Click += (s, e) => a(); btns.Children.Add(b); return b; }
        B("Check now", CheckGoals);
        B("Hint", () => NextHint());
        B(showAnswer ? "Hide the answer" : "Show me the answer", () => { showAnswer = !showAnswer; FillSide(); });
        sideBody.Children.Add(btns);
        if (hint.Length > 0) sideBody.Children.Add(Ui.Card(Wrap(Ui.Text("Hint: " + hint, 13.5, "#FDE68A")), "#2A2416", 10));
        if (showAnswer)
        {
            sideBody.Children.Add(Ui.Text("The answer, step by step:", 14, Ui.AccentText, true));
            sideBody.Children.Add(new SelectableTextBlock { Text = string.Join("\n", Lab.AnswerSteps()), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 12.5, Foreground = Ui.Brush(Ui.TextC) });
            var doIt = Ui.Button("Do it for me", false, 14); doIt.MinHeight = 44; doIt.HorizontalAlignment = HorizontalAlignment.Left;
            doIt.Click += (s, e) => DoAnswer();
            sideBody.Children.Add(doIt);
        }
    }

    void SimPart()
    {
        var row = new WrapPanel();
        void B(string t, string tip, Action a) { var b = Ui.Button(t, false, 15); b.MinHeight = 44; b.MinWidth = 52; b.Margin = new Thickness(0, 0, 6, 6); ToolTip.SetTip(b, tip); b.Click += (s, e) => a(); row.Children.Add(b); }
        B("⏮", "Back a step", () => Sim.Back());
        B(Sim.Playing ? "⏸" : "▶", Sim.Playing ? "Pause" : "Play", () => { Sim.Playing = !Sim.Playing; if (Sim.Playing && !Sim.Forward()) Sim.Playing = false; FillSide(); });
        B("⏭", "Forward a step", () => Sim.Forward());
        B("Reset", "Forget the steps so far", () => Sim.Reset());
        sideBody.Children.Add(row);
        sideBody.Children.Add(Wrap(Ui.Text(Sim.LastMessage.Length > 0 ? Sim.LastMessage : "Send a ping, then Forward or Play.", 13, Ui.Muted)));
        var list = new StackPanel { Spacing = 2 };
        foreach (var e in Sim.Events.Reverse().Take(40))
        {
            var ev = e;
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 36, Padding = new Thickness(8, 4),
                Background = Ui.Brush(ev == Canvas.Picked ? Ui.Surface3 : Ui.Surface2),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = Ui.Brush(ev.Color), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = ev.TimeText + "  " + ev.Type + "  " + ev.Last + " → " + ev.At, FontSize = 12.5, Foreground = Ui.Brush(Ui.TextC) } } },
            };
            b.Click += (s, a) => { Canvas.Picked = ev; Canvas.InvalidateVisual(); FillSide(); };
            list.Children.Add(b);
        }
        sideBody.Children.Add(list);
        if (Canvas.Picked != null)
        {
            sideBody.Children.Add(Ui.Text("Inside the envelope", 14, Ui.AccentText, true));
            sideBody.Children.Add(new SelectableTextBlock { Text = NetSim.Details(Canvas.Picked), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 12, Foreground = Ui.Brush(Ui.TextC) });
        }
    }

    static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; return t; }

    // ------------------------------------------------------------ the physical view
    void BuildPhysBar()
    {
        layoutPick.ItemsSource = Layouts.List.Select(l => l[1]).ToList();
        ToolTip.SetTip(layoutPick, "The site: house, small office, school or a blank floor plan to draw on");
        layoutPick.SelectionChanged += (s, e) =>
        {
            if (filling || P == null || layoutPick.SelectedIndex < 0) return;
            string id = Layouts.List[layoutPick.SelectedIndex][0];
            if (id == P.Layout.Id) return;
            if (P.Devices.Count == 0 || Data.Testing) { ChangeLayout(id); return; }
            MainView.Current.Message("Change the site?", "Switch the site to " + Layouts.List[layoutPick.SelectedIndex][1] + "? Your devices move to its first floor (the cables and settings stay). Undo brings the old site back.",
                ("Switch", () => ChangeLayout(id)), ("Cancel", FillLayouts));
        };
        ToolTip.SetTip(floorPick, "The campus map or one floor of a building");
        floorPick.SelectionChanged += (s, e) =>
        {
            if (filling || P == null || floorPick.SelectedIndex < 0) return;
            if (floorPick.SelectedIndex == 0) Phys.ShowCampus();
            else { var f = P.Layout.AllFloors().ElementAtOrDefault(floorPick.SelectedIndex - 1); if (f != null) Phys.ShowFloor(f.Key); }
        };
        string[][] cables = { new[] { "auto", "Automatic cable" }, new[] { "straight", "Straight-through" }, new[] { "cross", "Crossover" }, new[] { "fiber-mm", "Fibre (multimode)" }, new[] { "fiber-sm", "Fibre (single-mode)" }, new[] { "console", "Console cable" } };
        cablePick.ItemsSource = cables.Select(c => c[1]).ToList();
        cablePick.SelectedIndex = 0;
        ToolTip.SetTip(cablePick, "The kind of cable the Cable tool lays here (Automatic picks one that works)");
        cablePick.SelectionChanged += (s, e) => { if (cablePick.SelectedIndex >= 0) Phys.Cable = cables[cablePick.SelectedIndex][0]; };
        physBar.Children.Add(layoutPick);
        physBar.Children.Add(floorPick);
        physBar.Children.Add(cablePick);
        void FloorTool(string id, string text, string tip)
        {
            var b = Ui.Button(text, false, 13.5);
            b.MinHeight = 44; b.Padding = new Thickness(12, 4);
            ToolTip.SetTip(b, tip);
            b.Click += (s, e) => SetTool(Phys.Tool == id ? "select" : id);
            floorTools[id] = b;
            physBar.Children.Add(b);
        }
        FloorTool("room", "▭  Room", "Drag a box to add a room");
        FloorTool("wall", "┃  Wall", "Drag along a wall");
        FloorTool("door", "🚪  Door", "Tap a wall to put a door in it");
        FloorTool("label", "🏷  Label", "Tap to put a label on the plan");
        FloorTool("rack", "🗄  Rack", "Tap to stand a rack (usually in the wiring closet)");
        var range = Ui.Button("📶  Wi-Fi range", false, 13.5);
        range.MinHeight = 44; range.Padding = new Thickness(12, 4);
        void RangeLook() => range.Background = Ui.Brush(Phys.ShowRange ? Ui.AccentSoft : Ui.Surface3);
        range.Click += (s, e) => { Phys.ShowRange = !Phys.ShowRange; RangeLook(); Phys.InvalidateVisual(); SaveSettings(); };
        ToolTip.SetTip(range, "Show how far each access point's Wi-Fi reaches");
        range.AttachedToVisualTree += (s, e) => RangeLook();
        physBar.Children.Add(range);
        var plan = Ui.Button("🖼  Plan picture", false, 13.5);
        plan.MinHeight = 44; plan.Padding = new Thickness(12, 4);
        ToolTip.SetTip(plan, "Put a picture of a real floor plan (or a campus map) under this view");
        plan.Click += (s, e) => _ = SetMapPictureFromFile();
        physBar.Children.Add(plan);
    }

    void FillLayouts()
    {
        if (P == null) return;
        filling = true;
        try { layoutPick.SelectedIndex = Array.FindIndex(Layouts.List, l => l[0] == P.Layout.Id); }
        finally { filling = false; }
    }

    void FillFloors()
    {
        if (P == null) return;
        filling = true;
        try
        {
            var floors = P.Layout.AllFloors().ToList();
            floorPick.ItemsSource = new[] { "Campus map" }.Concat(floors.Select(f => (P.Layout.Buildings.Count > 1 ? f.B.Name + ", " : "") + f.Name)).ToList();
            floorPick.SelectedIndex = Phys.Level == "campus" ? 0 : floors.FindIndex(f => f.Key == Phys.FloorKey) + 1;
        }
        finally { filling = false; }
    }

    /// <summary>Changes the site (house, small office, school, blank plan); Undo brings the old one back.</summary>
    public void ChangeLayout(string id)
    {
        Phys.UseLayout(id);
        FillLayouts(); FillFloors();
        Say("The site is now: " + P.Layout.Name + ". Your devices are on its first floor; drag them where they belong.");
        MarkEdited();
    }

    /// <summary>Shows the Physical view (where things are) or the Logical view (how they connect).</summary>
    public void ShowPhysical(bool on)
    {
        if (Physical == on) return;
        var sel = (Physical ? Phys.Selection : Canvas.Selection).OfType<Device>().ToList();
        Physical = on;
        SetTool("select");
        Canvas.IsVisible = !on;
        Phys.IsVisible = on;
        physScroll.IsVisible = on;
        if (on)
        {
            Phys.Selection.Clear();
            foreach (var d in sel) Phys.Selection.Add(d);
            var first = sel.FirstOrDefault(d => d.Place.Length > 0 && d.Rack.Length == 0);
            if (first != null) Phys.ShowFloor(first.Place);
            else Dispatcher.UIThread.Post(() => Phys.Fit(), DispatcherPriority.Loaded);
            FillFloors();
            Say("Physical view: where things are. Pick a floor or the campus at the top; drag devices, tap one to open it, use Cable to pick ports. Room, Wall, Door, Label and Rack change the plan.");
        }
        else
        {
            Canvas.Selection.Clear();
            foreach (var d in Phys.Selection.OfType<Device>()) Canvas.Selection.Add(d);
            Say("Logical view: how things are connected.");
        }
        if (PanelOpen && SideMode == "draw") FillSide();
        UpdateButtons();
    }

    // ------------------------------------------------------------ drawing
    static readonly Dictionary<string, string> DrawTips = new()
    {
        ["draw:rect"] = "Rectangle: drag to draw one, or tap for the usual size.", ["draw:roundrect"] = "Rounded rectangle: drag to draw one, or tap.",
        ["draw:ellipse"] = "Ellipse: drag to draw one, or tap.", ["draw:line"] = "Line: drag from one end to the other.",
        ["draw:arrow"] = "Arrow: drag from its tail to its point.", ["draw:polyline"] = "Lines: tap corner after corner; tap the last corner again (or Finish) to finish.",
        ["draw:freehand"] = "Freehand: draw with your finger.", ["draw:text"] = "Text: tap where the text goes.", ["draw:note"] = "Sticky note: tap where the note goes.",
        ["draw:area"] = "Area: drag a box around the devices it groups, then give it a title.", ["draw:image"] = "Picture: tap where the picture goes.",
        ["draw:label"] = "Label: tap a device or a cable to stick a label on it.", ["draw:calibrate"] = "Set the scale: drag a line along something whose real length you know (a road, a wall).",
    };

    void BuildDrawBar()
    {
        void T(string id, string glyph, string name)
        {
            var b = new Button
            {
                Content = new StackPanel { Spacing = 1, Children = { new TextBlock { Text = glyph, FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Brush(Ui.TextC) }, new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Brush(Ui.TextC) } } },
                MinWidth = 62, MinHeight = 56, Padding = new Thickness(6, 3), Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC),
            };
            ToolTip.SetTip(b, DrawTips.TryGetValue(id, out var tip) ? tip : name);
            b.Click += (s, e) => { SetTool(id); if (!wide && PanelOpen) ClosePanel(); };
            drawTools[id] = b;
            drawBar.Children.Add(b);
        }
        T("select", "✋", "Move");
        T("draw:rect", "▭", "Rectangle");
        T("draw:roundrect", "▢", "Rounded");
        T("draw:ellipse", "◯", "Ellipse");
        T("draw:line", "╱", "Line");
        T("draw:arrow", "➚", "Arrow");
        T("draw:polyline", "⌇", "Lines");
        T("draw:freehand", "✎", "Freehand");
        T("draw:text", "T", "Text");
        T("draw:note", "🗒", "Note");
        T("draw:area", "⬚", "Area");
        T("draw:image", "🖼", "Picture");
        T("draw:label", "🏷", "Label");
        finishButton = Ui.Button("✓  Finish", true, 14);
        finishButton.MinHeight = 56; finishButton.IsVisible = false;
        finishButton.Click += (s, e) => { CurrentDraw.FinishPolyline(); MarkEdited(); };
        drawBar.Children.Add(finishButton);
        var props = Ui.Button("⚙  Properties", false, 14);
        props.MinHeight = 56;
        ToolTip.SetTip(props, "Colours, text, order, layers, objects and the map");
        props.Click += (s, e) => { if (PanelOpen && SideMode == "draw") ClosePanel(); else ShowDrawPanel(); };
        drawBar.Children.Add(props);
    }

    /// <summary>Shows the drawing bar (instead of the devices) along the bottom.</summary>
    public void ShowDrawBar(bool on)
    {
        Drawing = on;
        palScroll.IsVisible = !on;
        drawScroll.IsVisible = on;
        SetTool("select");
        if (on) { if (wide) ShowDrawPanel(); Say("Drawing: pick a shape at the bottom, then drag or tap on the view. Tap a drawing to select it; drag its handles to resize or turn it."); }
        else if (PanelOpen && SideMode == "draw") ClosePanel();
        UpdateButtons();
    }

    void ToolButtons(string id)
    {
        if (Canvas.Tool != id && (DrawTool.IsDrawTool(id) || toolButtons.ContainsKey(id))) Canvas.Tool = id;
        if (Phys.Tool != id) Phys.Tool = id;
        foreach (var kv in toolButtons.Concat(floorTools))
        {
            bool on = kv.Key == id;
            kv.Value.Background = Ui.Brush(on ? Ui.Accent : Ui.Surface3);
            kv.Value.BorderBrush = Ui.Brush(on ? Ui.Accent : Ui.BorderStrong);
        }
        foreach (var kv in drawTools)
        {
            bool on = kv.Key == id;
            kv.Value.Background = Ui.Brush(on ? Ui.Accent : Ui.Surface2);
            kv.Value.BorderBrush = Ui.Brush(on ? Ui.Accent : Ui.BorderC);
        }
        UpdateButtons();
    }

    void DrawSelectionChanged()
    {
        if (PanelOpen && SideMode == "draw") FillSide();
        UpdateButtons();
    }

    void DrawSide()
    {
        var tool = CurrentDraw;
        void Again() { MarkEdited(); FillSide(); }
        DrawPanels.Props(sideBody, tool, Again);
        Library = new ObjectLibrary();
        Library.Chosen += k => { SetTool("obj:" + k); if (!wide) ClosePanel(); };
        if (tool.CanDraw) sideBody.Children.Add(Library);
        // the map
        if (tool.CanDraw)
        {
            string view = tool.Host.DrawView;
            var h = Ui.Text(view == "logical" || view == "campus" ? "Map" : "Plan picture", 16, Ui.TextC, true); h.Margin = new Thickness(0, 14, 0, 0);
            sideBody.Children.Add(h);
            var row = new WrapPanel();
            if (view == "logical" || view == "campus")
            {
                sideBody.Children.Add(Ui.Text("A map under the " + (view == "logical" ? "network" : "campus") + ": it's decoration only and never changes how the network works.", 12.5, Ui.Muted));
                foreach (var st in Drawings.MapStyles)
                {
                    var id = st[0];
                    var b = DrawPanels.Small(st[1], () => { tool.ApplyMapStyle(id); Again(); }, st[2]);
                    if (Drawings.MapOf(P, view).Style == id) b.Background = Ui.Brush(Ui.AccentSoft);
                    row.Children.Add(b);
                }
            }
            row.Children.Add(DrawPanels.Small(view == "logical" || view == "campus" ? "Use a picture as the map..." : "Use a picture of the floor plan...", () => _ = SetMapPictureFromFile()));
            row.Children.Add(DrawPanels.Small("Remove the picture", () => { tool.RemoveMapPicture(); Again(); }));
            row.Children.Add(DrawPanels.Small("Set the scale", () => { SetTool("draw:calibrate"); if (!wide) ClosePanel(); }, "Drag a line along something whose length you know, then say how long it really is"));
            sideBody.Children.Add(row);
        }
        DrawPanels.Layers(sideBody, tool, Again);
        // labels on the view
        var lh = Ui.Text("Labels on the view", 16, Ui.TextC, true); lh.Margin = new Thickness(0, 14, 0, 0);
        sideBody.Children.Add(lh);
        CheckBox C(string text, bool on, Action<bool> set) { var c = new CheckBox { Content = text, IsChecked = on, MinHeight = 44, Foreground = Ui.Brush(Ui.TextC) }; c.Click += (s, e) => { set(c.IsChecked == true); Canvas.InvalidateVisual(); Phys.InvalidateVisual(); SaveSettings(); }; sideBody.Children.Add(c); return c; }
        C("IP addresses under devices", Canvas.ShowAddresses, v => { Canvas.ShowAddresses = v; Phys.ShowAddresses = v; });
        C("Port names on cables (Logical view)", Canvas.ShowPorts, v => Canvas.ShowPorts = v);
        C("VLANs on links (Logical view)", Canvas.ShowVlans, v => Canvas.ShowVlans = v);
        C("Wi-Fi range (Physical view)", Phys.ShowRange, v => Phys.ShowRange = v);
    }

    static readonly FilePickerFileType Pictures = new("Pictures") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" }, MimeTypes = new[] { "image/png", "image/jpeg" } };

    /// <summary>Asks for a picture with the device's file picker (never in the checks).</summary>
    async Task<byte[]> PickPicture()
    {
        var top = TopLevel.GetTopLevel(View);
        if (top?.StorageProvider == null || !top.StorageProvider.CanOpen || Data.Testing) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick a picture", AllowMultiple = false, FileTypeFilter = new[] { Pictures, FilePickerFileTypes.All } });
        if (files.Count == 0) return null;
        await using var s = await files[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    async Task AddPictureAt(double x, double y)
    {
        var tool = CurrentDraw;
        try { var b = await PickPicture(); if (b == null) return; tool.AddPicture(b, x, y); MarkEdited(); }
        catch (Exception e) { Say(e.Message); }
    }

    async Task SetMapPictureFromFile()
    {
        var tool = CurrentDraw;
        try
        {
            if (!tool.CanDraw) { Say("Pick the campus map or a floor first."); return; }
            var b = await PickPicture(); if (b == null) return;
            tool.SetMapPicture(b);
            MarkEdited();
            if (PanelOpen && SideMode == "draw") FillSide();
        }
        catch (Exception e) { Say(e.Message); }
    }

    // ------------------------------------------------------------ keys and Back
    public bool OnKey(KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool typing = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is TextBox;
        if (typing) return false;
        if (CurrentDraw.OnKey(e.Key, e.KeyModifiers)) { MarkEdited(); UpdateButtons(); return true; }
        if (e.Key == Key.Escape && Physical && Phys.ConnectFrom != null) { Phys.CancelTool(); return true; }
        if (e.Key == Key.Escape && (Canvas.Tool != "select")) { SetTool("select"); return true; }
        if (ctrl && e.Key == Key.Z) { if (Undo.CanUndo) { Undo.Undo(); MarkEdited(); } return true; }
        if (ctrl && e.Key == Key.Y) { if (Undo.CanRedo) { Undo.Redo(); MarkEdited(); } return true; }
        if (ctrl && e.Key == Key.S) { _ = SaveFile(false); return true; }
        var sel = Physical ? Phys.Selection : Canvas.Selection;
        if (e.Key == Key.Delete && sel.Count > 0) { Edits.Delete(P, Undo, sel.Where(o => o is not Rack && Canvas.CanChange(o)).ToList()); sel.Clear(); MarkEdited(); return true; }
        return false;
    }

    public bool OnBack()
    {
        if (!wide && PanelOpen) { ClosePanel(); return true; }
        if (CurrentDraw.Busy) { CurrentDraw.Cancel(); UpdateButtons(); return true; }
        if (Physical && Phys.Level != "floor" && Phys.Level != "campus") { Phys.ShowFloor(Phys.FloorKey); return true; }
        return false;
    }
}

/// <summary>How Network Lab works, in plain words.</summary>
public sealed class NetHelpPage : IPage
{
    public string Title => "How Network Lab works";
    public Control View { get; }
    public NetHelpPage()
    {
        var sp = new StackPanel { Spacing = 10 };
        void H(string t) { var h = Ui.Text(t, 18, Ui.AccentText, true); h.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(h); }
        void Tx(string t) { var x = Ui.Text(t, 14.5, Ui.TextC); x.TextWrapping = TextWrapping.Wrap; sp.Children.Add(x); }
        sp.Children.Add(Ui.Text("Network Lab", 26, Ui.TextC, true));
        Tx("A place to practise networking: put PCs, phones, servers, printers, switches, routers, Wi-Fi and the internet on the canvas, connect them with cables, set them up and watch the messages travel. Everything is simulated: nothing here reaches a real network.");
        H("Building");
        Tx("Tap a device in the bar at the bottom to add it, then drag it where you like. Drag empty space to move around; pinch (or the mouse wheel) to zoom; Fit shows everything.");
        Tx("Cable: tap one device, then the other. The ports and the kind of cable are picked for you. To choose a port yourself, hold a device (or right-click it) and pick Cable from a port. The lights at each end of a cable show whether it works: green working, amber blocked by spanning tree (it would make a loop), red not working.");
        H("Setting devices up");
        Tx("Tap a device to open it. Routers and switches have a command line like the real ones: ? shows what you can type next, Tab finishes a word, Up and Down bring back earlier commands. Computers have their Command Prompt, PowerShell or Terminal, and Config for the IP address (DHCP or static) and Wi-Fi. The wireless router and access point have a setup page.");
        H("Watching it work");
        Tx("Simulation stops time. Send a ping with the Ping tool (or ping from a computer's command line), then press Forward or Play: envelopes travel along the cables, and tapping one shows what's inside and what each device decided.");
        H("Labs");
        Tx("Labs are guided practice: a starting network, a short story and goals that tick themselves off as you reach them. Hint gives the next hint; Show me the answer lists the steps, and Do it for me carries them out.");
        H("Saving");
        Tx("Your network is kept automatically and is back next time. Save puts it in a .netlab file you can open again later or share (App Hub's Network Lab opens the same files).");
        H("The brand");
        Tx(Brand.About);
        View = Ui.Page(sp, 820);
    }
}
