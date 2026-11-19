// Emulator Lab - the page: the control bar (power, run, step, speed, screen), then the Computer (the monitor, the
// keyboard into the machine, "What's happening?"), Look inside (registers, code with breakpoints, memory, traps), the
// Assembler (editor, examples, open / save, assemble and run) and Learn. On a wide window Look inside can sit beside the
// monitor, as in App Hub; on a phone it is its own tab, and an on-screen key row gives Esc, Tab, Ctrl, the arrows and
// the F keys while the phone's keyboard types letters.
//
// Threads (as in App Hub): the machine runs on Runner's background thread; this page only reads it from a
// DispatcherTimer (30 ms) on the UI thread. Nothing on the runner thread touches Avalonia.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EmulatorLab;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.EmulatorLab;

public sealed class EmulatorPage : IPage, IKeyPage
{
    public string Title => "Emulator Lab";
    public Control View => root;

    // ---------------------------------------------------------------- the machine
    public readonly Computer C;
    public readonly Runner R;
    public readonly Inspector Insp;
    public readonly EmuSettings Settings;
    public readonly string DataDir, SourceRoot;
    /// <summary>Sentences for the person about saves that couldn't be read (shown once when the page opens).</summary>
    public readonly List<string> Problems = new();
    readonly bool built;

    static readonly int[] SpeedSteps = { 1, 10, 100, 1000, 10000, 100000, 1000000, 0 };
    static readonly string[] SpeedNames = { "1 a second", "10 a second", "100 a second", "1 000 a second", "10 000 a second", "100 000 a second", "1 million a second", "Full speed" };
    const string NewProgram = "# My program. It runs under LabOS: see _syscalls.s in the Examples for the system calls.\n.include \"_syscalls.s\"\n\n.text\n_start:\n    la   a0, message\n    li   a7, SYS_PRINT\n    ecall\n    li   a0, 0\n    li   a7, SYS_EXIT\n    ecall\n\n.data\nmessage:\n    .asciz \"Hello from my program!\\n\"\n";

    // ---------------------------------------------------------------- the page
    readonly DockPanel root = new();
    readonly DispatcherTimer frameTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    public enum Tab { Computer, Inside, Assembler, Learn }
    public Tab Current { get; private set; } = Tab.Computer;
    bool wide = true;
    /// <summary>A phone held upright (under 700 px): less used buttons go behind a "More" button.</summary>
    public bool Compact { get; private set; }
    readonly ContentControl tabHost = new();
    readonly WrapPanel tabRow = new() { Margin = new Thickness(12, 4, 12, 4) };
    readonly Dictionary<Tab, Button> tabButtons = new();
    static readonly Dictionary<Tab, (string Wide, string Narrow)> tabNames = new()
    {
        [Tab.Computer] = ("Computer", "Computer"), [Tab.Inside] = ("Look inside", "Inside"), [Tab.Assembler] = ("Assembler", "Assembler"), [Tab.Learn] = ("What's happening?", "Learn"),
    };

    // control bar
    Button btnPower, btnRun, btnStep, btnOver, btnReset, btnInside;
    readonly Button btnBarMore = Small("⋯", "Reset, speed and screen");
    readonly StackPanel barMore = new() { Spacing = 6 };
    readonly WrapPanel barWrap = new();
    StackPanel speed;
    Ellipse ledPower, ledDisk, ledButton;
    Slider sldSpeed;
    TextBlock txtSpeed, txtIps;
    ComboBox cmbScreen;

    // computer
    public readonly ScreenView Screen = new();
    Border glass;
    TextBlock txtHint, txtStory;
    internal TextBox typeBox;
    Button btnCtrl;
    bool ctrlLatch;
    Grid computerGrid;
    readonly ContentControl insideBeside = new(), insideTab = new();
    Border insideFrame;   // the Inside tab's frame, made once: a control can only have one parent
    Control computerView, assemblerView, learnView;

    // look inside
    Control insidePanel;
    public int InsideTab { get; private set; }
    readonly Dictionary<int, Button> insideTabButtons = new();
    readonly ContentControl insideHost = new();
    TextBlock txtNow, txtExplain, txtBreaks, txtRegion, txtHex, txtStack, txtTrapCounts, txtTraps;
    readonly List<(TextBlock Name, TextBlock Hex, TextBlock Dec, Border Row)> regRows = new(), csrRows = new();
    readonly StackPanel codeList = new();
    TextBox txtMemAddr, txtCodeAddr;
    CheckBox chkStopCrash;
    Control procView, codeView, memView, trapView;
    uint memAddr = Map.RamBase;
    uint? selectedCode;

    // assembler
    public readonly AsmEditor Editor = new();
    public string EditorFile { get; private set; } = "";
    string editorSaved = "";
    TextBox txtProgName;
    TextBlock txtAsmStatus;
    Button asmExamples, asmNew, asmOpen, asmSave, asmAssemble, asmRun, asmBare, asmDisk;
    Control asmName;
    readonly WrapPanel asmRow1 = new(), asmRow2 = new();
    readonly Button asmMoreButton = Small("More...", "New, Open, Save, the name on the disk, Run without LabOS");
    readonly StackPanel asmMore = new() { Spacing = 6, MaxWidth = 340 };
    ScrollViewer errors;
    Border edBorder;
    internal readonly StackPanel errorList = new() { Spacing = 2 };
    public AsmResult LastAsm { get; private set; }

    // learn
    TextBlock txtTopicTitle, txtTopic;
    readonly List<Button> topicButtons = new();
    int topic;

    public EmulatorPage() : this(!Data.Testing) { }

    /// <param name="start">Power on and start the frame timer (the checks pass false and drive it themselves).</param>
    public EmulatorPage(bool start)
    {
        DataDir = Data.For("EmulatorLab");
        EmuSettings.BringAcrossFromAppHub(DataDir);
        Settings = EmuSettings.Load(Path.Combine(DataDir, "settings.json"));
        if (Settings.Message.Length > 0) Problems.Add(Settings.Message);
        SourceRoot = LabSources.Unpack(Path.Combine(Data.Cache("EmulatorLab"), "sources"));
        C = new Computer(Path.Combine(SourceRoot, "os"));
        built = C.Build();
        string disk = Path.Combine(DataDir, "disk.img");
        try { string m = Computer.PrepareDisk(disk); if (m.Length > 0) Problems.Add(m); } catch (Exception e) { Problems.Add("The LabOS disk couldn't be backed up: " + e.Message); }
        try { C.AttachDisk(disk); } catch (Exception e) { Problems.Add("The LabOS disk couldn't be opened: " + e.Message); }
        R = new Runner(C);
        Insp = new Inspector(C, R);

        BuildUi();
        ApplySettings();
        frameTimer.Tick += (s, e) => Safe(() => Frame(false));
        saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveSettings(); };
        Editor.Box.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) SaveSoon(); };

        if (!built) Problems.Insert(0, "LabOS could not be assembled:\n\n" + C.BuildError);
        if (start)
        {
            View.AttachedToVisualTree += (s, e) =>
            {
                if (Problems.Count > 0) { MainView.Current?.Message("Emulator Lab", string.Join("\n\n", Problems.Distinct())); Problems.Clear(); }
            };
            if (built) PowerOn();
            frameTimer.Start();
        }
    }

    static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("Emulator Lab: " + e); }
    }

    // ================================================================ the layout
    void BuildUi()
    {
        var bar = BuildControlBar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        foreach (var t in new[] { (Tab.Computer, "Computer"), (Tab.Inside, "Look inside"), (Tab.Assembler, "Assembler"), (Tab.Learn, "What's happening?") })
        {
            var b = Seg(t.Item2);
            var tab = t.Item1;
            b.Click += (s, e) => ShowTab(tab);
            tabButtons[tab] = b;
            tabRow.Children.Add(b);
        }
        DockPanel.SetDock(tabRow, Dock.Top);
        root.Children.Add(tabRow);
        root.Children.Add(tabHost);

        insidePanel = BuildInside();
        computerView = BuildComputer();
        assemblerView = BuildAssembler();
        learnView = BuildLearn();
        btnBarMore.Flyout = new Flyout { Content = barMore, Placement = PlacementMode.BottomEdgeAlignedRight };
        asmMoreButton.Flyout = new Flyout { Content = asmMore, Placement = PlacementMode.BottomEdgeAlignedLeft };
        root.SizeChanged += (s, e) => { Arrange(e.NewSize.Width < 700); SetWide(e.NewSize.Width >= 1100); };
        Arrange(false, true);
        SetWide(true, true);
    }

    /// <summary>Puts the buttons in their rows: everything in view on a wide window; on a phone, the less used ones
    /// behind "More" buttons so the monitor and the editor keep their room.</summary>
    void Arrange(bool compact, bool force = false)
    {
        if (compact == Compact && !force) return;
        Compact = compact;
        void Move(Panel to, params Control[] cs) { foreach (var c in cs) { (c.Parent as Panel)?.Children.Remove(c); to.Children.Add(c); } }
        barWrap.Children.Clear(); barMore.Children.Clear();
        asmRow1.Children.Clear(); asmRow2.Children.Clear(); asmMore.Children.Clear();
        if (compact)
        {
            Move(barWrap, btnPower, btnRun, btnStep, btnOver, btnBarMore);
            Move(barMore, btnReset, speed, cmbScreen);
            Move(asmRow1, asmExamples, asmAssemble, asmRun, asmMoreButton);
            Move(asmMore, asmName, asmNew, asmOpen, asmSave, asmBare, asmDisk);
            foreach (var b in new[] { asmNew, asmOpen, asmSave, asmBare, asmDisk, btnReset }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Move(barWrap, btnPower, btnRun, btnStep, btnOver, btnReset, btnInside, speed, cmbScreen);
            Move(asmRow1, asmExamples, asmNew, asmOpen, asmSave);
            Move(asmRow2, asmName, asmAssemble, asmRun, asmBare, asmDisk);
            foreach (var b in new[] { asmNew, asmOpen, asmSave, asmBare, asmDisk, btnReset }) b.HorizontalAlignment = HorizontalAlignment.Left;
        }
        btnInside.IsVisible = !compact;
        foreach (var b in new[] { btnPower, btnRun, btnStep, btnOver, btnBarMore }) b.Padding = compact ? new Thickness(9, 8) : new Thickness(12, 8);
        btnRun.MinWidth = compact ? 62 : 80;
        txtSpeed.MinWidth = compact ? 0 : 120;
        asmRow2.IsVisible = !compact;
        errors.MaxHeight = compact ? 96 : 150;
        edBorder.MinHeight = compact ? 120 : 240;
        foreach (var kv in tabButtons)
        {
            kv.Value.Content = compact ? tabNames[kv.Key].Narrow : tabNames[kv.Key].Wide;
            kv.Value.Padding = compact ? new Thickness(10, 8) : new Thickness(14, 8);
        }
    }

    static Button Seg(string text)
    {
        var b = Ui.Button(text, false, 14);
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Padding = new Thickness(14, 8);
        return b;
    }

    static void SegOn(Button b, bool on)
    {
        b.Background = Ui.Brush(on ? Ui.AccentSoft : Ui.Surface3);
        b.BorderBrush = Ui.Brush(on ? Ui.Accent : Ui.BorderStrong);
        b.Foreground = Ui.Brush(on ? Ui.AccentText : Ui.TextC);
    }

    static Button Small(string text, string tip = null, bool primary = false)
    {
        var b = Ui.Button(text, primary, 14);
        b.Padding = new Thickness(12, 8);
        b.Margin = new Thickness(0, 0, 6, 6);
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }

    static TextBlock Mono(string text = "", double size = 13, string color = Ui.TextC)
    {
        return new TextBlock { Text = text, FontFamily = ScreenFont.Mono, FontSize = size, Foreground = Ui.Brush(color) };
    }

    Control BuildControlBar()
    {
        barWrap.Margin = new Thickness(12, 10, 12, 0);
        var wrap = barWrap;
        ledButton = new Ellipse { Width = 10, Height = 10, Fill = Ui.Brush("#3F4654"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        btnPower = Small("Power", "Switch the computer on or off");
        btnPower.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { ledButton, new TextBlock { Text = "Power on", VerticalAlignment = VerticalAlignment.Center } } };
        btnPower.Click += (s, e) => { if (C.M.PoweredOff) PowerOn(); else PowerOff(); };
        btnRun = Small("Run", "Run or pause the processor (F5 / Shift+F5)", true);
        btnRun.MinWidth = 80;
        btnRun.Click += (s, e) => ToggleRun();
        btnStep = Small("Step", "Run one instruction (F10)");
        btnStep.Click += (s, e) => DoStep(false);
        btnOver = Small("Step over", "Run one instruction; a call runs until it returns (F11)");
        btnOver.Click += (s, e) => DoStep(true);
        btnReset = Small("Reset", "Restart from the boot ROM (memory and the disk are kept)");
        btnReset.Click += (s, e) => ResetMachine();
        btnInside = Small("Look inside", "Registers, code, memory and traps (F12)");
        btnInside.Click += (s, e) => ToggleInside();

        sldSpeed = new Slider { Minimum = 0, Maximum = 7, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(sldSpeed, "How many instructions a second: from 1 (watch every step) to as fast as possible");
        txtSpeed = Ui.Text("Full speed", 13, Ui.Muted);
        txtSpeed.VerticalAlignment = VerticalAlignment.Center;
        txtSpeed.MinWidth = 120;
        sldSpeed.PropertyChanged += (s, e) => { if (e.Property == RangeBase.ValueProperty) SetSpeed(); };
        speed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 14, 6), Height = 44, Children = { Ui.Text("Speed", 13, Ui.Muted), sldSpeed, txtSpeed } };
        ((TextBlock)speed.Children[0]).VerticalAlignment = VerticalAlignment.Center;

        cmbScreen = new ComboBox { ItemsSource = new[] { "Screen: automatic", "Text console", "Graphics", "Graphics + text" }, MinHeight = 44, MinWidth = 180, Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(cmbScreen, "What the screen shows. Automatic: what the running program chose");
        cmbScreen.SelectionChanged += (s, e) =>
        {
            if (cmbScreen.SelectedIndex < 0) return;
            Screen.Display.ModeOverride = cmbScreen.SelectedIndex - 1; Settings.Screen = cmbScreen.SelectedIndex; forceRedraw = true; SaveSoon();
        };

        txtIps = Ui.Text("", 13, Ui.Muted);
        txtIps.Margin = new Thickness(0, 0, 0, 4);
        var sp = new StackPanel { Children = { wrap } };
        var status = new Border { Child = txtIps, Margin = new Thickness(12, 0, 12, 0) };
        sp.Children.Add(status);
        return sp;
    }

    Control BuildComputer()
    {
        // the monitor: a bezel with the picture, the lights under it
        glass = new Border
        {
            Child = Screen, Background = Brushes.Black, BorderBrush = Ui.Brush("#10141A"), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Focusable = true, Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        glass.PointerPressed += (s, e) => FocusMachine();
        glass.GotFocus += (s, e) => MachineFocus(true);
        glass.LostFocus += (s, e) => MachineFocus(false);
        glass.AddHandler(InputElement.TextInputEvent, (object s, TextInputEventArgs e) => { TypeText(e.Text); e.Handled = true; });
        ledPower = new Ellipse { Width = 10, Height = 10, Fill = Ui.Brush("#22332A") };
        ledDisk = new Ellipse { Width = 10, Height = 10, Fill = Ui.Brush("#3A2222") };
        var lights = new WrapPanel
        {
            Margin = new Thickness(4, 6, 0, 0),
            Children = { ledPower, Ui.Text("POWER", 11, Ui.Dim), new Border { Width = 10 }, ledDisk, Ui.Text("DISK", 11, Ui.Dim), new Border { Width = 10 }, Ui.Text("EMULATOR LAB  -  RISC-V RV32IM  -  16 MB", 11, Ui.Dim) },
        };
        foreach (var c in lights.Children) { c.VerticalAlignment = VerticalAlignment.Center; c.Margin = new Thickness(0, 0, 8, 0); }
        var monitor = new Border
        {
            Background = Ui.Brush("#1C2028"), BorderBrush = Ui.Brush("#2A303C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10),
            Child = new StackPanel { Children = { glass, lights } }, MaxWidth = 1000,
        };
        txtHint = Ui.Text("", 13, Ui.Muted);
        // the phone's keyboard types into this box; what is typed goes to the computer (the box itself stays empty)
        typeBox = new TextBox
        {
            Text = Sentinel, AcceptsReturn = true, MinHeight = 44, FontSize = 15, PlaceholderText = "Type here to the computer",
            HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = Platform.Touch,
        };
        typeBox.GotFocus += (s, e) => MachineFocus(true);
        typeBox.LostFocus += (s, e) => MachineFocus(false);
        typeBox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) TypeBoxChanged(); };
        var keys = BuildKeyRow();
        txtStory = Ui.Text("", 14, Ui.TextC);
        var story = Ui.Card(new StackPanel { Spacing = 4, Children = { Ui.Text("What's happening?", 15, Ui.AccentText, true), txtStory } }, Ui.Surface, 12);
        var left = new StackPanel { Spacing = 8, Children = { monitor, txtHint, typeBox, keys, story } };
        var leftScroll = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // the whole monitor fits the height of the page (the rest scrolls under it)
        leftScroll.SizeChanged += (s, e) => monitor.MaxWidth = Math.Min(1000, Math.Max(300, (e.NewSize.Height - 70) / 0.625 + 36));
        computerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 4, 12, 12) };
        computerGrid.Children.Add(leftScroll);
        insideBeside.Margin = new Thickness(12, 0, 0, 0);
        insideBeside.Width = 470;
        Grid.SetColumn(insideBeside, 1);
        computerGrid.Children.Add(insideBeside);
        MachineFocus(false);
        return computerGrid;
    }

    Control BuildKeyRow()
    {
        var wrap = new WrapPanel();
        void Add(string label, Action act, string tip = null)
        {
            var b = Ui.Button(label, false, 13);
            b.Padding = new Thickness(10, 6); b.MinWidth = 44; b.Margin = new Thickness(0, 0, 4, 4);
            b.Focusable = false;
            if (tip != null) ToolTip.SetTip(b, tip);
            b.Click += (s, e) => act();
            wrap.Children.Add(b);
        }
        Add("Esc", () => PushKey("Escape"));
        Add("Tab", () => PushKey("Tab"));
        btnCtrl = Ui.Button("Ctrl", false, 13);
        btnCtrl.Padding = new Thickness(10, 6); btnCtrl.MinWidth = 44; btnCtrl.Margin = new Thickness(0, 0, 4, 4); btnCtrl.Focusable = false;
        ToolTip.SetTip(btnCtrl, "Hold Ctrl down for the next letter");
        btnCtrl.Click += (s, e) => { ctrlLatch = !ctrlLatch; SegOn(btnCtrl, ctrlLatch); };
        wrap.Children.Add(btnCtrl);
        Add("Ctrl+C", () => PushKey("C", true), "Stops a LabOS program");
        Add("←", () => PushKey("Left"));
        Add("↑", () => PushKey("Up"));
        Add("↓", () => PushKey("Down"));
        Add("→", () => PushKey("Right"));
        Add("⌫", () => PushKey("Back"), "Backspace");
        Add("Enter", () => PushKey("Enter"));
        for (int f = 1; f <= 10; f++) { string k = "F" + f; Add(k, () => PushKey(k)); }
        return new Expander
        {
            Header = Ui.Text("Keys the computer needs: Esc, Tab, Ctrl, arrows, F1-F10", 13, Ui.Muted),
            Content = wrap, IsExpanded = Platform.Touch, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    Control BuildInside()
    {
        txtNow = Mono("", 13, Ui.AccentText);
        txtNow.TextWrapping = TextWrapping.Wrap;
        txtExplain = Ui.Text("", 13, Ui.Muted);
        var subRow = new WrapPanel();
        foreach (var (i, name) in new[] { (0, "Processor"), (1, "Code"), (2, "Memory"), (3, "Traps") })
        {
            var b = Seg(name);
            int k = i;
            b.Click += (s, e) => ShowInsideTab(k);
            insideTabButtons[i] = b;
            subRow.Children.Add(b);
        }
        procView = BuildRegisters();
        codeView = BuildCode();
        memView = BuildMemory();
        trapView = BuildTraps();
        var head = new StackPanel { Spacing = 4, Children = { txtNow, txtExplain, subRow } };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(insideHost);
        return Ui.Card(dock, Ui.Surface, 12);
    }

    Control BuildRegisters()
    {
        var sp = new StackPanel { Spacing = 1 };
        sp.Children.Add(Ui.Text("Registers  (lit up = just changed)", 13, Ui.Muted, true));
        for (int i = 0; i < 32; i++) regRows.Add(RegRowView(sp));
        var csrHead = Ui.Text("Control and status registers (CSRs)", 13, Ui.Muted, true);
        csrHead.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(csrHead);
        for (int i = 0; i < 12; i++) csrRows.Add(RegRowView(sp));
        return new ScrollViewer { Content = sp, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    static (TextBlock, TextBlock, TextBlock, Border) RegRowView(StackPanel into)
    {
        var name = Mono("", 13, Ui.AccentText); name.Width = 112;
        var hex = Mono(); hex.Width = 150;
        var dec = Mono("", 13, Ui.Muted);
        var row = new Border { Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(4), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, hex, dec } } };
        into.Children.Add(row);
        return (name, hex, dec, row);
    }

    Control BuildCode()
    {
        txtCodeAddr = new TextBox { Width = 160, MinHeight = 40, FontFamily = ScreenFont.Mono, PlaceholderText = "address or label", Margin = new Thickness(0, 0, 6, 6) };
        ToolTip.SetTip(txtCodeAddr, "An address in hex, or a label like shell");
        txtCodeAddr.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CodeGo(); e.Handled = true; } };
        var go = Small("Show"); go.Click += (s, e) => CodeGo();
        var pc = Small("Follow pc"); pc.Click += (s, e) => { Insp.CodeAt = null; insideDirty = true; };
        var brk = Small("Breakpoint", "Set or remove a breakpoint on the chosen line"); brk.Click += (s, e) => { if (selectedCode.HasValue) ToggleBreak(selectedCode.Value); };
        var clear = Small("Clear breakpoints"); clear.Click += (s, e) => { R.ClearBreaks(); insideDirty = true; };
        txtBreaks = Ui.Text("", 12.5, Ui.Muted);
        var top = new StackPanel { Children = { new WrapPanel { Children = { txtCodeAddr, go, pc, brk, clear } }, txtBreaks } };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = codeList, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        return dock;
    }

    Control BuildMemory()
    {
        txtMemAddr = new TextBox { Width = 160, MinHeight = 40, FontFamily = ScreenFont.Mono, Text = "80000000", Margin = new Thickness(0, 0, 6, 6) };
        ToolTip.SetTip(txtMemAddr, "An address in hex, or a label like shell");
        txtMemAddr.KeyDown += (s, e) => { if (e.Key == Key.Enter) { MemGo(); e.Handled = true; } };
        var go = Small("Show"); go.Click += (s, e) => MemGo();
        var up = Small("<", "Back 256 bytes"); up.Click += (s, e) => ShowMemory(unchecked(memAddr - 256));
        var down = Small(">", "On 256 bytes"); down.Click += (s, e) => ShowMemory(unchecked(memAddr + 256));
        var row1 = new WrapPanel { Children = { txtMemAddr, go, up, down } };
        var row2 = new WrapPanel();
        foreach (var (label, addr) in new (string, Func<uint>)[] { ("pc", () => C.M.Pc), ("sp", () => C.M.X[2]), ("Boot ROM", () => Map.RomBase), ("Flash", () => Map.FlashBase), ("LabOS", () => Map.RamBase), ("Program", () => Computer.ProgramBase), ("Screen", () => Map.FbBase) })
        {
            var b = Small(label);
            var a = addr;
            b.Click += (s, e) => ShowMemory(a());
            row2.Children.Add(b);
        }
        txtRegion = Ui.Text("", 13, Ui.Muted);
        txtHex = Mono("", 12.5);
        txtStack = Mono("", 12.5);
        var sp = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                row1, row2, txtRegion,
                new ScrollViewer { Content = txtHex, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
                Ui.Text("The stack", 13, Ui.Muted, true),
                new ScrollViewer { Content = txtStack, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
            },
        };
        return new ScrollViewer { Content = sp, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    Control BuildTraps()
    {
        txtTrapCounts = Ui.Text("", 13, Ui.Muted);
        chkStopCrash = new CheckBox { Content = "Pause when a program crashes", MinHeight = 44, Foreground = Ui.Brush(Ui.TextC) };
        chkStopCrash.IsCheckedChanged += (s, e) => { R.StopOnCrash = chkStopCrash.IsChecked == true; Settings.StopOnCrash = R.StopOnCrash; SaveSoon(); };
        txtTraps = Mono("", 12.5);
        var top = new StackPanel { Children = { txtTrapCounts, chkStopCrash } };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = txtTraps, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        return dock;
    }

    Control BuildAssembler()
    {
        var examples = Small("Examples...", "Programs to start from");
        var menu = new MenuFlyout();
        foreach (string n in LabSources.Names.Where(n => n.StartsWith("os/examples/")))
            menu.Items.Add(ExampleItem(n, "Learn: " + System.IO.Path.GetFileNameWithoutExtension(n)));
        menu.Items.Add(new Separator());
        foreach (string n in LabSources.Names.Where(n => n.StartsWith("os/programs/") && !System.IO.Path.GetFileName(n).StartsWith("_")))
            menu.Items.Add(ExampleItem(n, "LabOS demo: " + System.IO.Path.GetFileNameWithoutExtension(n)));
        menu.Items.Add(new Separator());
        menu.Items.Add(ExampleItem("os/programs/_syscalls.s", "The system calls (_syscalls.s)"));
        examples.Flyout = menu;
        var btnNew = Small("New"); btnNew.Click += (s, e) => Confirm(Editor.Text == editorSaved, "Start a new program? The text in the editor will be replaced.", "Start a new one", () => SetEditorText(NewProgram, ""));
        var btnOpen = Small("Open..."); btnOpen.Click += async (s, e) => await OpenFile();
        var btnSave = Small("Save as..."); btnSave.Click += async (s, e) => await SaveFile();
        asmExamples = examples; asmNew = btnNew; asmOpen = btnOpen; asmSave = btnSave;

        txtProgName = new TextBox { Width = 150, MinHeight = 40, Margin = new Thickness(0, 0, 6, 6), FontFamily = ScreenFont.Mono };
        ToolTip.SetTip(txtProgName, "Then type  run myprog  in LabOS");
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Ui.Text("Name on the disk", 13, Ui.Muted), txtProgName } };
        ((TextBlock)name.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        ((TextBlock)name.Children[0]).Margin = new Thickness(0, 0, 0, 6);
        var asm = Small("Assemble", "Check the program and turn it into machine code"); asm.Click += (s, e) => AssembleProgram(false);
        var run = Small("Assemble & run", "Save it on the LabOS disk and run it (F5)", true); run.Click += (s, e) => Try(AssembleAndRun);
        var bare = Small("Run without LabOS", "Bare metal: the program alone at 0x80000000, no operating system"); bare.Click += (s, e) => Try(RunBare);
        var disk = Small("Save to LabOS disk"); disk.Click += (s, e) => Try(SaveToDiskClicked);
        asmName = name; asmAssemble = asm; asmRun = run; asmBare = bare; asmDisk = disk;
        txtAsmStatus = Ui.Text("Write a program, or pick one from Examples. Then press Assemble & run.", 13.5, Ui.TextC);
        txtAsmStatus.Margin = new Thickness(0, 0, 0, 6);
        var top = new StackPanel { Children = { asmRow1, asmRow2, txtAsmStatus } };
        errors = new ScrollViewer { Content = errorList, MaxHeight = 150, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 6, 0, 0) };
        edBorder = new Border { Child = Editor, BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, MinHeight = 240 };
        var dock = new DockPanel { Margin = new Thickness(12, 4, 12, 12) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(errors, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(errors);
        dock.Children.Add(edBorder);
        return dock;
    }

    MenuItem ExampleItem(string name, string header)
    {
        var mi = new MenuItem { Header = header, MinHeight = 40 };
        mi.Click += (s, e) => LoadExample(name);
        return mi;
    }

    Control BuildLearn()
    {
        txtTopicTitle = Ui.Text("", 22, Ui.TextC, true);
        txtTopic = new SelectableTextBlock { FontSize = 15, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap };
        var list = new WrapPanel();
        for (int i = 0; i < Learn.Topics.Length; i++)
        {
            var b = Seg(Learn.Topics[i][0]);
            int k = i;
            b.Click += (s, e) => ShowTopic(k);
            topicButtons.Add(b);
            list.Children.Add(b);
        }
        var body = new StackPanel { Spacing = 10, Children = { list, Ui.Card(new StackPanel { Spacing = 10, Children = { txtTopicTitle, txtTopic } }, Ui.Surface, 18) } };
        ShowTopic(0);
        return Ui.Page(body, 900);
    }

    // ================================================================ tabs and layout
    void SetWide(bool w, bool force = false)
    {
        if (w == wide && !force) return;
        wide = w;
        PlaceInside();
        ShowTab(Current == Tab.Inside && wide && Settings.Inside ? Tab.Computer : Current);
    }

    void PlaceInside()
    {
        bool beside = wide && Settings.Inside;
        insideBeside.Content = null; insideTab.Content = null;
        if (beside) insideBeside.Content = insidePanel; else insideTab.Content = insidePanel;
        insideBeside.IsVisible = beside;
        tabButtons[Tab.Inside].IsVisible = !beside;
        SegOn(btnInside, Settings.Inside || Current == Tab.Inside);
        insideDirty = true;
    }

    public void ShowTab(Tab t)
    {
        if (t == Tab.Inside && wide && Settings.Inside) t = Tab.Computer;
        Current = t;
        foreach (var kv in tabButtons) SegOn(kv.Value, kv.Key == t);
        tabHost.Content = t switch
        {
            Tab.Computer => computerView,
            Tab.Inside => insideFrame ??= new Border { Child = insideTab, Margin = new Thickness(12, 4, 12, 12) },
            Tab.Assembler => assemblerView,
            _ => learnView,
        };
        SegOn(btnInside, Settings.Inside && wide || Current == Tab.Inside);
        insideDirty = true;
    }

    /// <summary>Look inside (F12): beside the monitor on a wide window, its own tab on a narrow one.</summary>
    public void ToggleInside()
    {
        if (wide)
        {
            Settings.Inside = !Settings.Inside;
            PlaceInside();
            if (Current == Tab.Inside || Settings.Inside) ShowTab(Tab.Computer);
        }
        else ShowTab(Current == Tab.Inside ? Tab.Computer : Tab.Inside);
        SaveSoon();
    }

    public void ShowInsideTab(int i)
    {
        InsideTab = Math.Max(0, Math.Min(3, i));
        Settings.InsideTab = InsideTab;
        foreach (var kv in insideTabButtons) SegOn(kv.Value, kv.Key == InsideTab);
        insideHost.Content = InsideTab switch { 0 => procView, 1 => codeView, 2 => memView, _ => trapView };
        insideDirty = true;
        SaveSoon();
    }

    public void ShowTopic(int i)
    {
        topic = Math.Max(0, Math.Min(Learn.Topics.Length - 1, i));
        txtTopicTitle.Text = Learn.Topics[topic][0];
        txtTopic.Text = Learn.Topics[topic][1];
        for (int k = 0; k < topicButtons.Count; k++) SegOn(topicButtons[k], k == topic);
    }

    void ApplySettings()
    {
        sldSpeed.Value = Settings.Speed;
        SetSpeed();
        cmbScreen.SelectedIndex = Settings.Screen;
        chkStopCrash.IsChecked = Settings.StopOnCrash;
        R.StopOnCrash = Settings.StopOnCrash;
        txtProgName.Text = Settings.ProgName;
        if (Settings.EditorText.Length > 0) { SetEditorText(Settings.EditorText, Settings.EditorFile); editorSaved = ""; }
        else SetEditorText(NewProgram, "");
        ShowInsideTab(Settings.InsideTab);
        PlaceInside();
        ShowTab(Tab.Computer);
        if (Settings.KeptForUpdate.Length > 0)
        {
            // App Hub closed Emulator Lab for an update with a program not saved to a file
            if (Settings.EditorText.Length > 0)
            {
                ShowTab(Tab.Assembler);
                AsmStatus("Your program from before the update is back in the editor. Save it to a file to keep a copy.", Ui.Yellow);
            }
            Settings.KeptForUpdate = "";
            SaveSettings();
        }
    }

    // ================================================================ power, run, step
    bool insideDirty = true, forceRedraw = true, markChanges;
    int lastVersion = -1;

    public void PowerOn()
    {
        if (!built) return;
        R.Pause();
        C.PowerOn();
        Insp.CodeAt = null;
        R.Resume();
        insideDirty = true;
    }

    void PowerOff()
    {
        R.Pause("Powered off");
        C.M.PoweredOff = true;
        insideDirty = true;
    }

    void ToggleRun()
    {
        if (C.M.PoweredOff) { PowerOn(); return; }
        if (R.Running) R.Pause(); else { Insp.CodeAt = null; R.Resume(); }
        insideDirty = true;
    }

    public void DoStep(bool over)
    {
        if (C.M.PoweredOff) return;
        Insp.CodeAt = null;
        if (over) R.StepOver(); else R.StepOne();
        if (!(wide && Settings.Inside) && Current != Tab.Inside)
        {
            if (wide) { Settings.Inside = true; PlaceInside(); ShowTab(Tab.Computer); }
            else ShowTab(Tab.Inside);
        }
        insideDirty = true;
        markChanges = true;
    }

    void ResetMachine()
    {
        if (C.M.PoweredOff) { PowerOn(); return; }
        bool was = R.Running;
        R.Pause();
        C.Reset();
        Insp.CodeAt = null;
        if (was) R.Resume();
        insideDirty = true;
    }

    void SetSpeed()
    {
        int i = (int)Math.Round(sldSpeed.Value);
        i = Math.Max(0, Math.Min(7, i));
        R.Speed = SpeedSteps[i];
        txtSpeed.Text = SpeedNames[i];
        Settings.Speed = i;
        SaveSoon();
    }

    // ================================================================ the keyboard into the machine
    const string Sentinel = " ";
    bool typeBoxBusy;
    public bool MachineHasFocus => glass.IsFocused || typeBox.IsFocused;

    /// <summary>Gives the keyboard to the computer: the screen (or, on a phone, the box its keyboard types into).</summary>
    public void FocusMachine() { if (typeBox.IsVisible) typeBox.Focus(); else glass.Focus(); }

    void MachineFocus(bool on)
    {
        glass.BorderBrush = Ui.Brush(on ? Ui.Accent : "#10141A");
        txtHint.Text = on
            ? (Platform.Touch ? "Typing goes to the computer. The key row gives Esc, Ctrl, arrows and the F keys; Ctrl+C stops a LabOS program." : "Typing goes to the computer. Ctrl+C stops a LabOS program; click outside the screen to use the page again.")
            : (Platform.Touch ? "Tap the screen, then type: the keys go to the computer." : "Click the screen, then type: the keys go to the computer.");
    }

    /// <summary>A key by name (Escape, Enter, F1, A...) into the machine's keyboard.</summary>
    public void PushKey(string key, bool ctrl = false)
    {
        if (KeyMap.Special(key, ctrl, out byte a, out byte s)) C.M.Keyboard.Push(a, s);
    }

    /// <summary>Typed characters into the machine (the Ctrl key on the key row makes the next letter Ctrl + letter).</summary>
    public void TypeText(string text)
    {
        foreach (char ch in text ?? "")
        {
            if (ch == '\n' || ch == '\r') { if (ch == '\n') PushKey("Enter"); continue; }
            if (ctrlLatch && char.IsLetter(ch) && ch < 128)
            {
                PushKey(char.ToUpperInvariant(ch).ToString(), true);
                ctrlLatch = false; SegOn(btnCtrl, false);
                continue;
            }
            if (ch >= 32 && ch < 127) C.M.Keyboard.Push((byte)ch, KeyMap.ScanForChar(ch));
        }
    }

    void TypeBoxChanged()
    {
        if (typeBoxBusy) return;
        string t = typeBox.Text ?? "";
        if (t == Sentinel) return;
        typeBoxBusy = true;
        try
        {
            if (t.Length == 0) PushKey("Back");                       // the phone's backspace took the space away
            else TypeText(t.StartsWith(Sentinel) ? t.Substring(Sentinel.Length) : t);
            typeBox.Text = Sentinel;
            typeBox.CaretIndex = Sentinel.Length;
        }
        finally { typeBoxBusy = false; }
    }

    public bool OnKey(KeyEventArgs e)
    {
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0, ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        switch (e.Key)
        {
            case Key.F5:
                if (Current == Tab.Assembler && !shift) Try(AssembleAndRun);
                else if (shift) { if (R.Running) { R.Pause(); insideDirty = true; } }
                else if (!R.Running) ToggleRun();
                return true;
            case Key.F10: DoStep(false); return true;
            case Key.F11: DoStep(true); return true;
            case Key.F12: ToggleInside(); return true;
        }
        if (!MachineHasFocus) return false;
        if (KeyMap.Special(e.Key.ToString(), ctrl, out byte a, out byte s))
        {
            C.M.Keyboard.Push(a, s);
            return true;
        }
        return false;
    }

    // ================================================================ Look inside
    bool ParseAddress(string text, out uint value)
    {
        value = 0;
        string t = (text ?? "").Trim();
        string h = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t.Substring(2) : t;
        if (h.Length is >= 1 and <= 8 && uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return true;
        // a label of LabOS or of the last assembled program
        if (C.KernelAsm != null && C.KernelAsm.Symbols.TryGetValue(t, out uint v)) { value = v; return true; }
        if (Insp.UserProgram != null && Insp.UserProgram.Symbols.TryGetValue(t, out v)) { value = v; return true; }
        return false;
    }

    void MemGo() { if (ParseAddress(txtMemAddr.Text, out uint v)) ShowMemory(v); }
    void CodeGo() { if (ParseAddress(txtCodeAddr.Text, out uint v)) { Insp.CodeAt = v; insideDirty = true; } }

    public void ShowMemory(uint addr)
    {
        memAddr = addr;
        txtMemAddr.Text = addr.ToString("x8");
        insideDirty = true;
    }

    public void ToggleBreak(uint addr)
    {
        if (!Insp.CodeAt.HasValue) Insp.CodeAt = C.M.Pc;   // don't jump away from the line just chosen
        R.ToggleBreak(addr);
        insideDirty = true;
    }

    void UpdateInside()
    {
        var m = C.M;
        bool running = R.Running;
        Insp.RefreshRegs(markChanges || running);
        markChanges = false;
        uint pc = m.Pc;
        uint inst = m.ReadWord(pc);
        string where = Insp.Where(pc);
        txtNow.Text = "pc = 0x" + pc.ToString("x8") + "   " + Disassembler.Disassemble(inst, pc, null);
        txtExplain.Text = m.PoweredOff ? "The computer is off." : running ? "Running" + (where.Length > 0 ? ": " + where : "") + ". Pause or Step to look at one instruction." : (where.Length > 0 ? where + ". " : "") + Disassembler.Explain(inst, pc, m.X);
        switch (InsideTab)
        {
            case 0:
                for (int i = 0; i < regRows.Count && i < Insp.Regs.Count; i++) FillReg(regRows[i], Insp.Regs[i]);
                for (int i = 0; i < csrRows.Count && i < Insp.Csrs.Count; i++) FillReg(csrRows[i], Insp.Csrs[i]);
                break;
            case 1:
                Insp.RefreshCode();
                var breaks = R.UserBreaks;
                txtBreaks.Text = breaks.Length > 0
                    ? "Breakpoints: " + string.Join(", ", breaks.Select(b => "0x" + b.ToString("x8"))) + ". Double-click (or double-tap) a line to set or remove one."
                    : "Double-click (or double-tap) a line to set or remove a breakpoint: the processor pauses before running it.";
                FillCode();
                break;
            case 2:
                txtHex.Text = Insp.HexDump(memAddr, 16).TrimEnd('\n');
                txtRegion.Text = "0x" + memAddr.ToString("x8") + ": " + Inspector.Region(memAddr);
                txtStack.Text = Insp.Stack(10).TrimEnd('\n');
                break;
            case 3:
                Insp.RefreshTraps();
                txtTrapCounts.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} interrupts and {1:N0} exceptions since the last reset ({2:N0} of them system calls, {3:N0} problems). Newest first.", m.InterruptCount, m.ExceptionCount, m.ExceptionCount - m.FaultCount, m.FaultCount);
                txtTraps.Text = string.Join("\n", Insp.Traps);
                break;
        }
    }

    static void FillReg((TextBlock Name, TextBlock Hex, TextBlock Dec, Border Row) v, RegRow r)
    {
        v.Name.Text = r.Name; v.Hex.Text = r.Hex; v.Dec.Text = r.Dec;
        ToolTip.SetTip(v.Row, r.Tip);
        v.Row.Background = r.Changed ? Ui.Brush(Ui.Yellow, 60) : Brushes.Transparent;
    }

    readonly List<(Border Row, TextBlock Text)> codeRows = new();

    void FillCode()
    {
        while (codeRows.Count < Insp.Code.Count)
        {
            var t = Mono("", 12.5);
            var row = new Border { Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(4), Child = t, MinHeight = 24, Cursor = new Cursor(StandardCursorType.Hand) };
            int index = codeRows.Count;
            row.Tapped += (s, e) => { if (index < Insp.Code.Count) { selectedCode = Insp.Code[index].Address; if (!Insp.CodeAt.HasValue) Insp.CodeAt = C.M.Pc; insideDirty = true; } };
            row.DoubleTapped += (s, e) => { if (index < Insp.Code.Count) { selectedCode = Insp.Code[index].Address; ToggleBreak(Insp.Code[index].Address); } };
            codeRows.Add((row, t));
            codeList.Children.Add(row);
        }
        for (int i = 0; i < codeRows.Count; i++)
        {
            var (row, t) = codeRows[i];
            row.IsVisible = i < Insp.Code.Count;
            if (!row.IsVisible) continue;
            var c = Insp.Code[i];
            string label = c.HasLabel ? c.Label + ":" : "";
            t.Text = c.Marker + " " + c.AddrText + "  " + (label.Length > 0 ? label.PadRight(14) + " " : new string(' ', 15)) + c.Code.PadRight(30) + " " + c.Source;
            t.Foreground = Ui.Brush(c.IsBreak ? Ui.Red : c.IsPc ? Ui.Yellow : Ui.TextC);
            row.Background = c.IsPc ? Ui.Brush(Ui.Yellow, 40) : selectedCode == c.Address ? Ui.Brush(Ui.Accent, 50) : Brushes.Transparent;
        }
    }

    // ================================================================ the frame: screen, lights, status, Look inside
    bool lastBlink;
    DateTime lastInside = DateTime.MinValue, lastStory = DateTime.MinValue, diskOnUntil = DateTime.MinValue;
    long lastDisk;

    /// <summary>One frame (the timer calls it every 30 ms; the checks call it with force = true).</summary>
    public void Frame(bool force)
    {
        var m = C.M;
        var now = DateTime.Now;
        bool blink = (Environment.TickCount & 512) == 0;
        if (force) { forceRedraw = true; insideDirty = true; }
        if (m.Video.Dirty || C.Term.Dirty || blink != lastBlink || forceRedraw)
        {
            m.Video.Dirty = false; C.Term.Dirty = false; forceRedraw = false; lastBlink = blink;
            Screen.Display.Render(m, C.Term, blink, m.PoweredOff);
            Screen.Present();
            m.Video.Frames++;
        }
        bool running = R.Running;
        ledPower.Fill = Ui.Brush(m.PoweredOff ? "#22332A" : !running ? "#FBBF24" : m.Waiting ? "#2F7A4C" : "#4ADE80");
        ledButton.Fill = Ui.Brush(m.PoweredOff ? "#3F4654" : "#4ADE80");
        ((TextBlock)((StackPanel)btnPower.Content).Children[1]).Text = m.PoweredOff ? "Power on" : "Power off";
        long disk = m.Disk.Reads + m.Disk.Writes;
        if (disk != lastDisk) { lastDisk = disk; diskOnUntil = now.AddMilliseconds(180); }
        ledDisk.Fill = Ui.Brush(now < diskOnUntil ? "#F87171" : "#3A2222");
        btnRun.Content = running ? "Pause" : "Run";
        string count = m.Instret.ToString("N0", CultureInfo.InvariantCulture);
        string status;
        if (m.PoweredOff) status = "Switched off";
        else if (!running) status = (string.IsNullOrEmpty(R.StopReason) ? "Paused" : R.StopReason) + "   -   " + count + " instructions so far";
        else if (R.Speed != 0) status = R.Ips.ToString("N0", CultureInfo.InvariantCulture) + " instructions a second   -   " + count + " so far";
        else if (m.Waiting && R.Ips < 1000000) status = "Resting (waiting for a key or the timer)   -   " + count + " instructions";
        else status = (R.Ips / 1000000).ToString("N1", CultureInfo.InvariantCulture) + " million instructions a second   -   " + count + " so far";
        txtIps.Text = status;
        bool changed = R.StateVersion != lastVersion;
        if (changed) { lastVersion = R.StateVersion; insideDirty = true; }
        bool insideShown = insidePanel.IsEffectivelyVisible && (Current == Tab.Inside || (wide && Settings.Inside && Current == Tab.Computer));
        if (insideShown && (insideDirty || (running && (now - lastInside).TotalMilliseconds >= 300)) || force)
        {
            insideDirty = false; lastInside = now;
            UpdateInside();
        }
        if (changed || force || (now - lastStory).TotalMilliseconds >= 700)
        {
            lastStory = now;
            txtStory.Text = Insp.Narrate();
        }
    }

    // ================================================================ the assembler
    public void SetEditorText(string text, string file)
    {
        Editor.Text = text;
        EditorFile = file ?? "";
        editorSaved = Editor.Text;
        Editor.SetErrors(null);
        errorList.Children.Clear();
    }

    void AsmStatus(string text, string color = Ui.TextC)
    {
        txtAsmStatus.Text = text;
        txtAsmStatus.Foreground = Ui.Brush(color);
    }

    void Confirm(bool skip, string question, string yes, Action act)
    {
        if (skip || Data.Testing) { act(); return; }
        MainView.Current.Message("Emulator Lab", question, (yes, act), ("Cancel", null));
    }

    void Try(Action a)
    {
        try { a(); }
        catch (Exception e) { AsmStatus(e.Message, "#FCA5A5"); }
    }

    public void LoadExample(string name)
    {
        Confirm(Editor.Text == editorSaved, "Replace the program in the editor with this example?", "Replace it", () =>
        {
            SetEditorText(LabSources.Read(name) ?? "", "");
            string nm = new string(System.IO.Path.GetFileNameWithoutExtension(name).Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            if (nm.Length > 20) nm = nm.Substring(0, 20);
            txtProgName.Text = "my" + nm.TrimStart('_');
            AsmStatus("Example " + System.IO.Path.GetFileName(name) + ". Press Assemble & run" + (name.Contains("bare") ? " - this one is for Run without LabOS" : "") + ".");
            ShowTab(Tab.Assembler);
        });
    }

    string ProgName()
    {
        string n = (txtProgName.Text ?? "").Trim();
        string bad = LabFs.CheckName(n);
        if (bad != null) throw new InvalidOperationException(bad);
        Settings.ProgName = n;
        return n;
    }

    /// <summary>Assembles the editor's text; shows the errors; returns the result (null if it failed).</summary>
    public AsmResult AssembleProgram(bool bare)
    {
        string file = EditorFile.Length > 0 ? EditorFile : null;
        var res = C.AssembleUser(Editor.Text, file, bare);
        errorList.Children.Clear();
        var lines = new List<int>();
        foreach (var err in res.Errors)
        {
            bool inThis = string.IsNullOrEmpty(err.File) || (file != null && err.File == file);
            string text = inThis ? "Line " + err.Line + ": " + err.Message : System.IO.Path.GetFileName(err.File) + ", line " + err.Line + ": " + err.Message;
            var b = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("#FCA5A5"), FontSize = 13.5 },
                Background = Ui.Brush(Ui.Surface2), BorderThickness = new Thickness(0), MinHeight = 36, Padding = new Thickness(10, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            int line = inThis ? err.Line : 0;
            b.Click += (s, e) => { if (line > 0) Editor.GoToLine(line); };
            errorList.Children.Add(b);
            if (inThis) lines.Add(err.Line);
        }
        Editor.SetErrors(lines);
        if (!res.Ok)
        {
            AsmStatus(res.Errors.Count + " problem" + (res.Errors.Count != 1 ? "s" : "") + " found. " + (Platform.Touch ? "Tap" : "Click") + " one to go to its line.", "#FCA5A5");
            return null;
        }
        LastAsm = res;
        Insp.UserProgram = res;
        AsmStatus(string.Format(CultureInfo.InvariantCulture, "Assembled: {0:N0} bytes of code and {1:N0} bytes of data, starting at 0x{2}. {3}", res.TextSize, res.DataSize, res.Origin.ToString("x8"), bare ? "Bare metal: no LabOS, no system calls." : "Ready for LabOS."));
        return res;
    }

    string SaveToDisk(AsmResult res)
    {
        string name = ProgName();
        if (res.Image.Length > 65536) throw new InvalidOperationException("The program is bigger than 64 KB, the most a LabOS file can hold.");
        C.Fs.Write(name, res.Image);
        SaveSoon();
        return name;
    }

    public void AssembleAndRun()
    {
        var res = AssembleProgram(false);
        if (res == null) return;
        string name = SaveToDisk(res);
        // switched off, or running a bare-metal program (no LabOS in the flash): start LabOS first
        if (C.M.PoweredOff || C.M.ReadWord(Map.FlashBase) != Computer.FlashMagic) PowerOn();
        // a program already running? Ctrl+C first
        if (C.KernelAsm.Symbols.TryGetValue("program_running", out uint busyAt) && C.M.ReadWord(busyAt) != 0) C.M.Keyboard.Push(3, 0x2E);
        C.M.Keyboard.Type("run " + name + "\n");
        if (!R.Running && !Data.Testing) R.Resume();
        txtAsmStatus.Text += " Saved as '" + name + "' on the LabOS disk and started with: run " + name;
        ShowTab(Tab.Computer);
        FocusMachine();
    }

    void SaveToDiskClicked()
    {
        var res = AssembleProgram(false);
        if (res == null) return;
        string name = SaveToDisk(res);
        txtAsmStatus.Text += " Saved as '" + name + "' on the LabOS disk: type  run " + name + "  in LabOS.";
    }

    void RunBare()
    {
        var res = AssembleProgram(true);
        if (res == null) return;
        R.Pause();
        C.PowerOnBare(res.Image);
        if (!Data.Testing) R.Resume();
        txtAsmStatus.Text += " Running without LabOS. Press Power off and Power on to start LabOS again.";
        ShowTab(Tab.Computer);
    }

    static readonly FilePickerFileType AsmFiles = new("Assembly programs") { Patterns = new[] { "*.s" }, MimeTypes = new[] { "text/x-asm", "text/plain", "application/octet-stream" } };

    async Task OpenFile()
    {
        var top = TopLevel.GetTopLevel(View);
        if (top?.StorageProvider == null || !top.StorageProvider.CanOpen || Data.Testing) return;
        var proceed = Editor.Text == editorSaved;
        if (!proceed)
        {
            var tcs = new TaskCompletionSource<bool>();
            MainView.Current.Message("Emulator Lab", "Open a program? The text in the editor will be replaced.", ("Open", () => tcs.TrySetResult(true)), ("Cancel", () => tcs.TrySetResult(false)));
            if (!await tcs.Task) return;
        }
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a program", AllowMultiple = false, FileTypeFilter = new[] { AsmFiles, FilePickerFileTypes.All } });
            if (files.Count == 0) return;
            string text;
            await using (var s = await files[0].OpenReadAsync())
            using (var r = new StreamReader(s)) text = await r.ReadToEndAsync();
            SetEditorText(text, files[0].TryGetLocalPath() ?? "");
            AsmStatus("Opened " + files[0].Name);
            SaveSoon();
        }
        catch (Exception e) { AsmStatus("Couldn't open it: " + e.Message, "#FCA5A5"); }
    }

    async Task SaveFile()
    {
        var top = TopLevel.GetTopLevel(View);
        if (top?.StorageProvider == null || !top.StorageProvider.CanSave || Data.Testing) return;
        try
        {
            string suggested = EditorFile.Length > 0 ? System.IO.Path.GetFileName(EditorFile) : (txtProgName.Text ?? "myprog").Trim() + ".s";
            var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save the program", SuggestedFileName = suggested, DefaultExtension = "s", FileTypeChoices = new[] { AsmFiles } });
            if (f == null) return;
            string text = Editor.Text;
            string local = f.TryGetLocalPath();
            if (local != null) LearningLab.Saves.SafeFile.WriteText(local, text, false);   // safely; the person's own file, so no backups beside it
            else
            {
                await using var s = await f.OpenWriteAsync();
                s.SetLength(0);
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                await s.WriteAsync(bytes);
            }
            EditorFile = local ?? "";
            editorSaved = text;
            AsmStatus("Saved " + f.Name);
            SaveSoon();
        }
        catch (Exception e) { AsmStatus("Couldn't save it: " + e.Message, "#FCA5A5"); }
    }

    // ================================================================ saving and closing
    void SaveSoon() { if (saveTimer == null) return; saveTimer.Stop(); saveTimer.Start(); }

    public void SaveSettings()
    {
        try
        {
            Settings.EditorText = Editor.Text;
            Settings.EditorFile = EditorFile;
            Settings.Save();
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("Emulator Lab settings: " + e.Message); }
    }

    bool closed;
    public void Closed()
    {
        if (closed) return;
        closed = true;
        frameTimer.Stop();
        saveTimer.Stop();
        R.Dispose();
        C.Dispose();
        SaveSettings();
    }

    // ================================================================ pictures for the checks (-Snapshot)
    /// <summary>Sets up one of App Hub's snapshot scenes: Shell, Graphics, Snake, Inside, AsmError, Learn.</summary>
    public void Scene(string scene)
    {
        C.M.Rtc.Fixed = new DateTime(2026, 10, 15, 9, 41, 7);
        if (scene == "AsmError")
        {
            ShowTab(Tab.Assembler);
            string text = LabSources.Read("os/examples/count.s").Replace("\r\n", "\n");
            SetEditorText(text.Replace("addi s0, s0, 1          # next number", "addi s0, s0             # next number").Replace("ble  s0, s1, loop", "ble  s0, s1, lopp"), "");
            txtProgName.Text = "mycount";
            AssembleProgram(false);
            return;
        }
        if (scene == "Learn") { ShowTab(Tab.Learn); ShowTopic(1); return; }
        R.Pause();
        C.PowerOn();
        C.RunVirtual(60000000);
        switch (scene)
        {
            case "Graphics": C.M.Keyboard.Type("run bounce\n"); C.RunVirtual(6000000); break;
            case "Snake": C.M.Keyboard.Type("run snake demo\n"); C.RunVirtual(16000000); break;
            case "Inside":
                var res = C.AssembleUser(LabSources.Read("os/examples/count.s"), null, false);
                C.Fs.Write("count", res.Image);
                Insp.UserProgram = res;
                // start it and stop inside the program's loop
                C.M.Keyboard.Type("run count\n");
                res.Symbols.TryGetValue("loop", out uint loop);
                R.ToggleBreak(loop);
                C.StopWhenWaitingForKeys = false;
                for (int i = 0; i < 4; i++) { C.M.SkipBreakOnce = i > 0; C.RunVirtual(20000000); }   // round the loop a few times
                C.StopWhenWaitingForKeys = true;
                Insp.RefreshRegs(false);
                for (int i = 0; i < 2; i++) R.StepOne();
                markChanges = true;
                if (wide) { Settings.Inside = true; PlaceInside(); ShowTab(Tab.Computer); } else ShowTab(Tab.Inside);
                ShowInsideTab(0);
                R.StopReason = "Stepped";
                break;
            default: C.TypeAndRun("ls\n"); C.TypeAndRun("date\n"); break;
        }
    }
}
