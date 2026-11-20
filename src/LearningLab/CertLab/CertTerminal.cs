// Cert Lab (Learning Lab) - the terminal: App Hub's TermView for Avalonia, touch first (like Network Lab's NetTerminal).
// It shows one console of a simulated machine (an ITerm: a RHEL Session or a PowerShell PsConsole) and is reused by the
// Cyber Range screens.
//
//   var t = new CertTerminal(sim.Term("server1"));   // add it to a page; a 60 ms timer polls it while it is shown
//   t.TypeLine("ls -l");                             // as if typed and Enter pressed (checks, solutions)
//   t.Typed = "syst"; t.Tab();                       // the line being typed and bash-style Tab (twice lists)
//   t.Text                                           // the whole screen (or the GRUB screen while it shows)
//   t.SendKey(Key.Down) / t.SendText("rd.break")     // keys and typed text to where keys go now (line, GRUB, vi)
//   t.Poll()                                         // pick up output now (the timer does it too)
//   t.PasteText(s) / t.PasteClipboard()              // paste: each line break runs a line (asks from 4 lines)
//   t.SelectAll(); t.CopySelection()                 // output selection to the clipboard (ClipText)
//   t.GrubShown, t.Mode ("shell" | "grub" | "vim" | "nano"), t.Vim (VimEngine), t.Nano (TextBox)
//   events Activity (new output), static QuietRun (checks: never ask), ConfirmPaste (checks answer the question)
//
// The typed line is a real TextBox (phone keyboards work); the output above it is selectable text kept to MaxLines.
// The key row under it changes with what is on the screen: Tab, Ctrl+C, Ctrl+D, Esc, history and copy / paste for the
// shell; arrows, Home / End, e, Ctrl+X, Esc, Enter, Backspace for the GRUB menu and editor (its keys act at once); Esc,
// arrows, Enter, Backspace for vi. GRUB and vi also have a "type here" box: a phone's keyboard types into it and every
// letter goes straight to GRUB or vi (the box stays empty: a one-space sentinel, an empty box = Backspace). nano is a
// text box with Save (Ctrl+O), Exit (Ctrl+X, asks to save) and Cut line (Ctrl+K).
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class CertTerminal : Grid
{
    public ITerm S { get; private set; }
    public string Title = "";
    /// <summary>New output arrived (a page can show the console's activity).</summary>
    public event Action Activity;
    public const int MaxLines = 3000;
    /// <summary>A paste that runs this many lines or more asks first.</summary>
    public const int PasteConfirmFrom = 4;
    public const int MaxPasteChars = 20000, MaxPasteLines = 200;
    /// <summary>The checks and pictures: never ask (a paste that would ask is refused) and no GRUB countdown.</summary>
    public static bool QuietRun;
    /// <summary>The checks answer "Paste N lines?" (terminal, lines that will run, cut short) instead of the person.</summary>
    public static Func<CertTerminal, int, bool, bool> ConfirmPaste;
    public static int GrubAutoSeconds = 5;

    readonly SelectableTextBlock output;
    readonly ScrollViewer scroll;
    readonly Panel screen;
    readonly TextBlock prompt;
    readonly TextBox input;
    readonly DockPanel lineRow;
    readonly WrapPanel keys;
    readonly TextBox typeBox;
    readonly Border frame;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly List<string> lines = new() { "" };
    readonly List<string> history = new();
    int histPos = -1;
    GrubView grub;
    ScrollViewer grubScroll;
    EditorHost editor;
    DateTime grubTick;
    string mode = "";
    static double charW;

    /// <summary>Checks: how many times Tab found nothing.</summary>
    public int Flashes { get; private set; }

    public CertTerminal(ITerm term)
    {
        S = term;
        RowDefinitions = new RowDefinitions("*,Auto,Auto");
        Background = Ui.Brush(CertUi.TermBg);
        output = new SelectableTextBlock { FontFamily = CertUi.Mono, FontSize = 14, Foreground = Ui.Brush(CertUi.TermFg), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8, 10, 0), SelectionBrush = Ui.Brush("#2F5A8A") };
        lineRow = new DockPanel();
        scroll = new ScrollViewer { Content = new StackPanel { Children = { output, lineRow } }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        screen = new Panel { Children = { scroll } };
        frame = new Border { Child = screen, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
        Children.Add(frame);

        prompt = new TextBlock { FontFamily = CertUi.Mono, FontSize = 14, Foreground = Ui.Brush(CertUi.TermFg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        input = new TextBox
        {
            FontFamily = CertUi.Mono, FontSize = 14, Foreground = Ui.Brush(CertUi.TermFg), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            MinHeight = 44, Padding = new Thickness(4, 8), VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Ui.Brush(CertUi.TermFg), AcceptsTab = false,
        };
        // the typed line looks like part of the terminal, as in a real terminal window: no box, no focus border
        input.FocusAdorner = null;
        foreach (var state in new[] { ":focus", ":pointerover", ":focus-within" })
            input.Styles.Add(new Style(x => x.OfType<TextBox>().Class(state).Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters = { new Setter(Border.BorderThicknessProperty, new Thickness(0)), new Setter(Border.BackgroundProperty, Brushes.Transparent), new Setter(Border.BorderBrushProperty, Brushes.Transparent) }
            });
        TextInputOptions.SetAutoCapitalization(input, false);
        input.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        input.AddHandler(TextInputEvent, (s, e) => { if (Blocked) e.Handled = true; tabCount = 0; }, RoutingStrategies.Tunnel);
        DockPanel.SetDock(prompt, Dock.Left);
        lineRow.Children.Add(prompt); lineRow.Children.Add(input);

        typeBox = new TextBox { Text = Sentinel, MinHeight = 44, FontSize = 14, Width = 170, InnerLeftContent = new TextBlock { Text = "Type ⌨", Foreground = Ui.Brush(Ui.Muted), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) }, Margin = new Thickness(0, 0, 6, 6), VerticalContentAlignment = VerticalAlignment.Center };
        TextInputOptions.SetAutoCapitalization(typeBox, false);
        ToolTip.SetTip(typeBox, "What you type here goes straight to the screen above");
        typeBox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) TypeBoxChanged(); };
        typeBox.AddHandler(KeyDownEvent, (s, e) => { string k = KeyName(e.Key, (e.KeyModifiers & KeyModifiers.Control) != 0); if (k != null && k != "Backspace") { e.Handled = true; KeyByName(k); } }, RoutingStrategies.Tunnel);

        keys = new WrapPanel { Margin = new Thickness(6, 4, 6, 0) };
        var keyBorder = new Border { Child = keys, Background = Ui.Brush("#111827") };
        Grid.SetRow(keyBorder, 2);
        Children.Add(keyBorder);

        if (charW <= 0) charW = new FormattedText("MMMMMMMMMM", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(CertUi.Mono), 14, Brushes.White).Width / 10;
        if (charW <= 0) charW = 8.4;
        timer.Tick += (s, e) => { try { Poll(); } catch (Exception ex) { AppendNote("\n(" + ex.Message + ")\n"); } };
        AttachedToVisualTree += (s, e) => timer.Start();
        DetachedFromVisualTree += (s, e) => timer.Stop();
        PointerPressed += (s, e) => { if (grub != null) grub.Focus(); else if (editor?.KeyTarget is Control c) c.Focus(); };
        // a click (or tap) anywhere on the terminal puts the cursor on the typed line, unless it selected some output
        scroll.AddHandler(PointerReleasedEvent, (s, e) =>
        {
            if (grub != null || editor != null) return;
            if (output.SelectionStart != output.SelectionEnd) return;
            input.Focus(); input.CaretIndex = (input.Text ?? "").Length;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        Poll();
    }

    /// <summary>A console that replaced this one (a machine reset): shows it from a clean screen.</summary>
    public void Attach(ITerm term) { S = term; lines.Clear(); lines.Add(""); ShowOutput(); Poll(); }

    public string Mode => mode;
    public bool GrubShown => grub != null;
    public VimEngine Vim => editor?.Vim;
    public TextBox Nano => editor?.Nano;
    public SelectableTextBlock Output => output;
    public TextBox InputBox => input;
    public bool Busy => S.Busy || S.WaitingForBreak;
    bool Blocked => S.Busy && !S.WaitingForBreak;
    int Cols => Math.Max(20, (int)(((scroll.Bounds.Width > 0 ? scroll.Bounds.Width : 640) - 24) / charW));

    public void FocusInput()
    {
        if (grub != null) grub.Focus();
        else if (editor != null) (editor.KeyTarget as Control)?.Focus();
        else input.Focus();
    }

    /// <summary>The terminal's text (output, prompt, typed line), or the GRUB screen while it shows.</summary>
    public string Text { get { var g = S.Grub; return g != null ? g.Text : string.Join("\n", lines) + (S.Busy ? "" : S.Prompt) + (S.Secret ? "" : input.Text ?? ""); } }
    public string Typed { get => input.Text ?? ""; set { input.Text = value ?? ""; input.CaretIndex = input.Text.Length; } }

    // ------------------------------------------------------------ output
    public void AppendNote(string s) { Append(s); }
    public void Clear() { lines.Clear(); lines.Add(""); ShowOutput(); }

    void Append(string s)
    {
        foreach (char ch in (s ?? "").Replace("\r", ""))
        {
            if (ch == '\n') lines.Add("");
            else if (ch == '\b') { var l = lines[^1]; if (l.Length > 0) lines[^1] = l.Substring(0, l.Length - 1); }
            else if (ch == '\u001b') continue;
            else lines[^1] += ch;
        }
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        ShowOutput();
    }

    void ShowOutput()
    {
        // the last line is where the prompt sits: it shows in the prompt row instead
        output.Text = lines.Count > 1 ? string.Join("\n", lines.Take(lines.Count - 1)) : "";
        ScrollEnd();
    }
    public void ScrollEnd() => Dispatcher.UIThread.Post(() => scroll.ScrollToEnd(), DispatcherPriority.Background);

    static bool IsShellPrompt(string p) => p != null && (p.EndsWith("$ ", StringComparison.Ordinal) || p.EndsWith("# ", StringComparison.Ordinal) || p.EndsWith("> ", StringComparison.Ordinal) && p.StartsWith("PS ", StringComparison.Ordinal));

    /// <summary>Picks up new output, the prompt, the editor and the GRUB screen (the timer, pages and checks).</summary>
    public void Poll()
    {
        if (S == null) return;
        if (S.ClearRequested) { S.ClearRequested = false; Clear(); }
        string o = S.TakeOutput();
        if (o.Length > 0) { Append(o); Activity?.Invoke(); }
        // a shell prompt always starts on its own line (output without a line break at the end, like base64 -d of a
        // flag, would otherwise run into it), as most terminals show it
        if (!S.Busy && lines[^1].Length > 0 && IsShellPrompt(S.Prompt)) Append("\n");
        string p = lines[^1] + (S.Busy ? "" : S.Prompt);
        if (prompt.Text != p) prompt.Text = p;
        input.PasswordChar = S.Secret ? '•' : default;
        input.IsReadOnly = Blocked;
        // an editor (vi, vim, nano, crontab -e, visudo...) covers the terminal until it closes
        var pe = S.PendingEdit;
        if (pe != null && !pe.Done && editor == null)
        {
            editor = new EditorHost(this, pe);
            screen.Children.Add(editor);
            Dispatcher.UIThread.Post(FocusInput, DispatcherPriority.Input);
        }
        else if ((pe == null || pe.Done) && editor != null) { screen.Children.Remove(editor); editor = null; input.Focus(); }
        // the GRUB screen; its countdown runs on the wall clock
        var g = S.Grub;
        if (g != null && grub == null)
        {
            grub = new GrubView(this);
            grubScroll = new ScrollViewer { Content = grub, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Black };
            screen.Children.Add(grubScroll);
            grubTick = DateTime.Now;
            Dispatcher.UIThread.Post(() => grub?.Focus(), DispatcherPriority.Input);
        }
        else if (g == null && grub != null) { screen.Children.Remove(grubScroll); grub = null; grubScroll = null; input.Focus(); }
        if (grub != null)
        {
            int secs = QuietRun ? 0 : GrubAutoSeconds;
            if (secs > 0 && g.TimerRunning)
            {
                if ((DateTime.Now - grubTick).TotalSeconds >= 1) { grubTick = grubTick.AddSeconds(1); if (grubTick < DateTime.Now.AddSeconds(-2)) grubTick = DateTime.Now; GrubInput(() => S.GrubTick()); }
            }
            else grubTick = DateTime.Now;
            grub?.InvalidateVisual();
            grub?.InvalidateMeasure();
        }
        string m = grub != null ? "grub" : editor != null ? (editor.Vim != null ? "vim" : "nano") : "shell";
        lineRow.IsVisible = m == "shell";
        if (m != mode) { mode = m; BuildKeys(); }
    }

    /// <summary>A key or the clock changed the GRUB screen: when it boots, the machine starts and prints.</summary>
    internal void GrubInput(Action a)
    {
        a();
        S.AfterSubmit();
        if (S.Grub == null) Poll();
        else grub?.InvalidateVisual();
    }

    // ------------------------------------------------------------ the key row
    void BuildKeys()
    {
        keys.Children.Clear();
        void Key(string text, string tip, Action a)
        {
            var b = new Button { Content = text, FontSize = 13, MinHeight = 44, MinWidth = 46, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 6), Background = Ui.Brush("#1F2937"), Foreground = Ui.Brush("#E5E7EB") };
            ToolTip.SetTip(b, tip);
            b.Click += (s, e) => { CertUi.Safe(a); FocusInput(); };
            keys.Children.Add(b);
        }
        switch (mode)
        {
            case "grub":
                keys.Children.Add(typeBox);
                Key("↑", "Up", () => KeyByName("Up")); Key("↓", "Down", () => KeyByName("Down"));
                Key("←", "Left", () => KeyByName("Left")); Key("→", "Right", () => KeyByName("Right"));
                Key("Home", "Start of the line", () => KeyByName("Home")); Key("End", "End of the line", () => KeyByName("End"));
                Key("e", "Edit the entry", () => TextToScreen("e"));
                Key("Ctrl+X", "Boot what you edited", () => KeyByName("Ctrl+X"));
                Key("Ctrl+C", "GRUB's command line", () => KeyByName("Ctrl+C"));
                Key("Esc", "Back (leave the editor without booting)", () => KeyByName("Esc"));
                Key("Enter", "Enter", () => KeyByName("Enter"));
                Key("⌫", "Backspace", () => KeyByName("Backspace"));
                Key("Paste", "Paste at the cursor", () => _ = PasteClipboard());
                break;
            case "vim":
                keys.Children.Add(typeBox);
                Key("Esc", "Esc: back to normal mode", () => KeyByName("Esc"));
                Key("↑", "Up", () => KeyByName("Up")); Key("↓", "Down", () => KeyByName("Down"));
                Key("←", "Left", () => KeyByName("Left")); Key("→", "Right", () => KeyByName("Right"));
                Key("Enter", "Enter", () => KeyByName("Enter"));
                Key("⌫", "Backspace", () => KeyByName("Backspace"));
                Key("Paste", "Paste at the cursor", () => _ = PasteClipboard());
                break;
            case "nano":
                break;
            default:
                Key("Tab", "Finish the word (twice: list them)", () => Tab());
                Key("Ctrl+C", "Stop the command", Break);
                Key("Ctrl+D", "End of input (at, cat > file)", Eof);
                Key("↑", "The command before", () => History(-1));
                Key("↓", "The next command", () => History(1));
                Key("Esc", "Clear the line", () => Typed = "");
                Key("Copy", "Copy what you selected", () => { if (!CopySelection()) CertApp.Current?.Status("Select some text first (or Select all)."); });
                Key("Paste", "Paste into the line", () => _ = PasteClipboard());
                Key("Select all", "Select everything on the screen", SelectAll);
                Key("Clear", "Clear the screen", Clear);
                break;
        }
    }

    // ------------------------------------------------------------ keys to GRUB and vi
    const string Sentinel = " ";
    bool typeBoxBusy;
    void TypeBoxChanged()
    {
        if (typeBoxBusy) return;
        string t = typeBox.Text ?? "";
        if (t == Sentinel) return;
        typeBoxBusy = true;
        try
        {
            if (t.Length == 0) KeyByName("Backspace");
            else
            {
                string typed = t.StartsWith(Sentinel, StringComparison.Ordinal) ? t.Substring(Sentinel.Length) : t;
                foreach (var part in typed.Replace("\r", "").Split('\n').Select((x, i) => (x, i)))
                {
                    if (part.i > 0) KeyByName("Enter");
                    if (part.x.Length > 0) TextToScreen(part.x);
                }
            }
            typeBox.Text = Sentinel;
            typeBox.CaretIndex = Sentinel.Length;
        }
        finally { typeBoxBusy = false; }
    }

    /// <summary>A key by GRUB's name (Up, Enter, Esc, Ctrl+X...) to GRUB or vi.</summary>
    public void KeyByName(string k)
    {
        if (grub != null) { GrubInput(() => S.GrubKey(k)); return; }
        if (editor?.Vim != null)
        {
            string vk = k == "Ctrl+C" ? "C-c" : k == "Ctrl+R" ? "C-r" : k == "Ctrl+[" ? "Esc" : k;
            if (vk.StartsWith("Ctrl+", StringComparison.Ordinal)) return;
            editor.VimKey(vk);
        }
    }

    /// <summary>Typed letters to GRUB or vi.</summary>
    public void TextToScreen(string t)
    {
        if (grub != null) { GrubInput(() => S.GrubType(t)); return; }
        if (editor?.Vim != null) foreach (char ch in t) if (ch >= 32 && ch != 127) editor.VimKey(ch.ToString());
    }

    /// <summary>The name GrubScreen.Key and VimEngine use for a key, or null for keys that arrive as typed text.</summary>
    internal static string KeyName(Key key, bool ctrl)
    {
        if (ctrl && key >= Key.A && key <= Key.Z) return "Ctrl+" + (char)('A' + (key - Key.A));
        if (ctrl && key == Key.OemOpenBrackets) return "Ctrl+[";
        switch (key)
        {
            case Key.Up: return "Up";
            case Key.Down: return "Down";
            case Key.Left: return "Left";
            case Key.Right: return "Right";
            case Key.Home: return "Home";
            case Key.End: return "End";
            case Key.PageUp: return "PageUp";
            case Key.PageDown: return "PageDown";
            case Key.Enter: return "Enter";
            case Key.Escape: return "Esc";
            case Key.Back: return "Backspace";
            case Key.Delete: return "Delete";
            case Key.Tab: return "Tab";
            case Key.F2: return "F2";
            case Key.F10: return "F10";
        }
        return null;
    }

    /// <summary>Where keys go now: the GRUB screen, the editor, else the typed line.</summary>
    Control KeyTarget => grub != null ? grub : editor?.KeyTarget as Control ?? input;

    /// <summary>The checks: a key as the keyboard sends it, to where keys go now.</summary>
    public bool SendKey(Key k, KeyModifiers mods = KeyModifiers.None)
    {
        var e = new KeyEventArgs { Key = k, KeyModifiers = mods, RoutedEvent = KeyDownEvent };
        KeyTarget.RaiseEvent(e);
        return e.Handled;
    }
    /// <summary>The checks: typed characters as the keyboard delivers them.</summary>
    public void SendText(string text)
    {
        var target = KeyTarget;
        if (target == input) { foreach (char ch in text) target.RaiseEvent(new TextInputEventArgs { Text = ch.ToString(), RoutedEvent = TextInputEvent }); return; }
        foreach (char ch in text) target.RaiseEvent(new TextInputEventArgs { Text = ch.ToString(), RoutedEvent = TextInputEvent });
    }

    // ------------------------------------------------------------ the shell line
    /// <summary>Types a line as if the person had (checks, solutions).</summary>
    public void TypeLine(string line) { Remember(line, S.Secret); Submit(line); }

    void Submit(string line)
    {
        lines[^1] += (S.Busy ? "" : S.Prompt) + (S.Secret ? "" : line);
        Append("\n");
        input.Text = "";
        tabCount = 0;
        S.Columns = Cols;
        S.Execute(line);
        S.AfterSubmit();
        Poll();
    }

    void Remember(string line, bool secret)
    {
        if (!secret && line.Trim().Length > 0 && (history.Count == 0 || history[^1] != line)) { history.Add(line); if (history.Count > 500) history.RemoveAt(0); }
        histPos = -1;
    }

    public void Break() { lines[^1] += (S.Busy ? "" : S.Prompt) + (input.Text ?? "") + "^C"; Append("\n"); input.Text = ""; S.Break(); S.AfterSubmit(); Poll(); }
    public void Eof() { if (S.Waiting) { lines[^1] += S.Prompt; Append("\n"); S.Eof(); S.AfterSubmit(); Poll(); } }

    void History(int dir)
    {
        if (S.Secret) return;
        if (dir < 0) { if (history.Count == 0) return; histPos = histPos < 0 ? history.Count - 1 : Math.Max(0, histPos - 1); input.Text = history[histPos]; }
        else { if (histPos < 0) return; histPos++; if (histPos >= history.Count) { histPos = -1; input.Text = ""; } else input.Text = history[histPos]; }
        input.CaretIndex = (input.Text ?? "").Length;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0, shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (e.Key != Key.Tab && e.Key != Key.LeftShift && e.Key != Key.RightShift) tabCount = 0;
        if (ctrl && e.Key == Key.C && (shift || HasSelection)) { CopySelection(); e.Handled = true; return; }
        if (IsPasteKey(e.Key, ctrl, shift)) { e.Handled = true; _ = PasteClipboard(); return; }
        string text = input.Text ?? "";
        int caret = Math.Clamp(input.CaretIndex, 0, text.Length);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.C: Break(); e.Handled = true; return;
                case Key.D: if (text.Length == 0) Eof(); else if (caret < text.Length) { input.Text = text.Remove(caret, 1); input.CaretIndex = caret; } e.Handled = true; return;
                case Key.L: Clear(); e.Handled = true; return;
                case Key.A: input.CaretIndex = 0; e.Handled = true; return;
                case Key.E: input.CaretIndex = text.Length; e.Handled = true; return;
                case Key.U: input.Text = text.Substring(caret); input.CaretIndex = 0; e.Handled = true; return;
                case Key.K: input.Text = text.Substring(0, caret); input.CaretIndex = caret; e.Handled = true; return;
                case Key.W: { int st = caret; while (st > 0 && text[st - 1] == ' ') st--; while (st > 0 && text[st - 1] != ' ') st--; input.Text = text.Remove(st, caret - st); input.CaretIndex = st; e.Handled = true; return; }
            }
        }
        if (e.Key == Key.PageUp) { scroll.PageUp(); e.Handled = true; return; }
        if (e.Key == Key.PageDown) { scroll.PageDown(); e.Handled = true; return; }
        if (Blocked) { if (e.Key == Key.Enter || e.Key == Key.Tab) e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.Enter: { string line = text; Remember(line, S.Secret); Submit(line); e.Handled = true; break; }
            case Key.Tab: Tab(); e.Handled = true; break;
            case Key.Up: History(-1); e.Handled = true; break;
            case Key.Down: History(1); e.Handled = true; break;
            case Key.Escape: if (text.Length > 0) { input.Text = ""; e.Handled = true; } break;
        }
    }

    internal static bool IsPasteKey(Key key, bool ctrl, bool shift) => (ctrl && key == Key.V) || (shift && !ctrl && key == Key.Insert);

    // ------------------------------------------------------------ Tab (bash: one match completes, a second Tab lists)
    int tabCount;
    string tabInput = "";

    public void Tab()
    {
        if (S.Secret) return;
        string text = input.Text ?? "";
        int caret = Math.Clamp(input.CaretIndex, 0, text.Length);
        string head = text.Substring(0, caret), tail = text.Substring(caret);
        var c = S.Candidates(head);
        if (c == null || c.Words.Count == 0) { tabCount = 0; Flash(); return; }
        int start = Math.Min(c.Start, head.Length);
        string typed = head.Substring(start);
        bool again = tabCount > 0 && head == tabInput;
        if (c.Words.Count == 1)
        {
            string pick = c.Words[0];
            string done = head.Substring(0, start) + pick + (c.NoSpace.Contains(pick) ? "" : " ");
            input.Text = done + tail.TrimStart(); input.CaretIndex = done.Length; tabCount = 0;
            return;
        }
        string common = CommonStart(c.Words);
        if (common.Length > typed.Length) { string done = head.Substring(0, start) + common; input.Text = done + tail; input.CaretIndex = done.Length; tabCount = 1; tabInput = done; return; }
        if (again)
        {
            ListCompletions(text, c.Words.Select(w => w.EndsWith('/') ? w.TrimEnd('/').Substring(w.TrimEnd('/').LastIndexOf('/') + 1) + "/" : w.Contains('/') ? w.Substring(w.LastIndexOf('/') + 1) : w).ToList());
            tabCount = 0;
            return;
        }
        tabCount = 1; tabInput = head;
    }

    static string CommonStart(List<string> words)
    {
        string p = words[0];
        foreach (var w in words.Skip(1)) { int i = 0; while (i < p.Length && i < w.Length && p[i] == w[i]) i++; p = p.Substring(0, i); }
        return p;
    }

    void ListCompletions(string line, List<string> words)
    {
        var sb = new StringBuilder(S.Prompt + line + "\n");
        int w = words.Max(x => x.Length) + 2;
        int per = Math.Max(1, Cols / w);
        for (int i = 0; i < words.Count; i++)
        {
            sb.Append(words[i].PadRight(w));
            if (i % per == per - 1 || i == words.Count - 1) { sb.Length = sb.ToString().TrimEnd().Length; sb.Append('\n'); }
        }
        Append(sb.ToString());
    }

    void Flash()
    {
        Flashes++;
        frame.BorderBrush = Ui.Brush(Ui.Red);
        DispatcherTimer.RunOnce(() => frame.BorderBrush = Brushes.Transparent, TimeSpan.FromMilliseconds(200));
    }

    // ------------------------------------------------------------ copy and paste
    public bool HasSelection => !string.IsNullOrEmpty(output.SelectedText);
    public string SelectedText => output.SelectedText ?? "";
    public void SelectAll() => output.SelectAll();
    /// <summary>Selects characters of the output (checks).</summary>
    public void Select(int start, int end) { output.SelectionStart = start; output.SelectionEnd = end; }
    /// <summary>Copies the selection (and clears it, as Windows Terminal does); false when nothing is selected.</summary>
    public bool CopySelection()
    {
        string t = string.Join("\n", SelectedText.Replace("\r", "").Split('\n').Select(l => l.TrimEnd(' ', '\t')));
        if (t.Length == 0) return false;
        bool ok = ClipText.SetText(t);
        output.ClearSelection();
        if (ok) CertApp.Current?.Status("Copied.");
        return ok;
    }

    /// <summary>Pastes the clipboard's text where the keys go now (the line, GRUB or the editor).</summary>
    public async Task<bool> PasteClipboard()
    {
        string t = await AppClipboard.Read();
        return PasteText(t);
    }

    /// <summary>Pastes text: into GRUB's editor or command line at the cursor, into vi / nano at the cursor, or into the
    /// typed line. Each line break submits the line, like a real terminal; the last piece stays typed.</summary>
    public bool PasteText(string raw)
    {
        if (string.IsNullOrEmpty(raw) || S == null) return false;
        string t = ClipText.Lf(raw);
        if (grub != null) return GrubPaste(t);
        if (editor != null) { editor.Paste(t.Length > MaxPasteChars * 10 ? t.Substring(0, MaxPasteChars * 10) : t); return true; }
        if (S.Finished || Blocked) return false;
        bool capped = false;
        if (t.Length > MaxPasteChars) { t = t.Substring(0, MaxPasteChars); capped = true; }
        var parts = t.Split('\n').ToList();
        if (parts.Count - 1 > MaxPasteLines) { parts = parts.Take(MaxPasteLines).ToList(); parts.Add(""); capped = true; }
        int runs = parts.Count - 1;
        if (runs >= PasteConfirmFrom || capped)
        {
            var h = ConfirmPaste;
            if (h != null) { if (!h(this, runs, capped)) return false; }
            else if (QuietRun || MainView.Current == null) return false;
            else
            {
                string msg = runs > 0 ? "Each line runs as a command, one after another, as if you typed it and pressed Enter." : "The text goes into the line being typed.";
                if (capped) msg += " The text is long: only the first " + MaxPasteLines + " lines (at most " + MaxPasteChars + " characters) are pasted.";
                MainView.Current.Message(runs > 0 ? "Paste " + runs + " lines into the terminal?" : "Paste into the terminal?", msg, ("Paste", () => PasteLines(parts)), ("Cancel", null));
                return false;
            }
        }
        PasteLines(parts);
        return true;
    }

    string PasteStop()
    {
        if (S.Finished) return "the console closed";
        if (S.Grub != null) return "the boot menu came up";
        var pe = S.PendingEdit;
        if (pe != null && !pe.Done) return "an editor opened";
        if (Busy && !S.WaitingForBreak) return "a command is still running";
        return null;
    }

    void PasteLines(List<string> parts)
    {
        output.ClearSelection();
        tabCount = 0; histPos = -1;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                string why = PasteStop();
                if (why != null)
                {
                    if (parts.Skip(i).Any(x => x.Length > 0)) AppendNote("[Cert Lab: the rest of the paste was not typed because " + why + ".]\n");
                    break;
                }
            }
            string text = input.Text ?? "";
            int at = Math.Clamp(input.CaretIndex, 0, text.Length);
            string piece = parts[i];
            if (i < parts.Count - 1) { string line = text.Insert(at, piece); input.Text = ""; Remember(line, S.Secret); Submit(line); }
            else { input.Text = text.Insert(at, piece); input.CaretIndex = at + piece.Length; }
        }
    }

    /// <summary>GRUB types pasted text at the cursor in the entry editor and on the grub> line (a line break is Enter);
    /// nothing on the menu itself, where letters are commands.</summary>
    bool GrubPaste(string t)
    {
        var g = S.Grub;
        if (g == null || (g.Mode != "edit" && g.Mode != "cmd")) return false;
        t = t.Replace("\t", " ");
        if (t.Length > 4000) t = t.Substring(0, 4000);
        GrubInput(() =>
        {
            var run = new StringBuilder();
            foreach (char ch in t)
            {
                if (S.Grub == null) break;
                if (ch != '\n') { run.Append(ch); continue; }
                if (run.Length > 0) { S.GrubType(run.ToString()); run.Clear(); }
                if (S.Grub != null) S.GrubKey("Enter");
            }
            if (run.Length > 0 && S.Grub != null) S.GrubType(run.ToString());
        });
        return true;
    }

    internal void EditDone(string result, bool saved) { S.EditDone(result, saved); Poll(); }
}

/// <summary>The GRUB screen over the terminal: 80 x 25 characters drawn from the console's GrubScreen; every key goes
/// straight to it (arrows, Home / End, Esc, Ctrl+X, F10...). Wider than a phone: it scrolls sideways there.</summary>
public sealed class GrubView : Control
{
    readonly CertTerminal owner;
    static readonly IBrush Bg = Brushes.Black, Fg = Ui.Brush("#C8C8C8"), HiBg = Ui.Brush("#E5E5E5"), HiFg = Brushes.Black, HintFg = Ui.Brush("#7DD3FC");
    double fs = 14;

    public GrubView(CertTerminal o) { owner = o; Focusable = true; }

    static FormattedText Mono(string s, double size, IBrush b) => new(s ?? "", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(CertUi.Mono), size, b);

    double Cw(double size) => Mono("MMMMMMMMMM", size, Fg).Width / 10;
    double Lh(double size) => Math.Ceiling(Mono("M", size, Fg).Height);

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 900 : available.Width;
        var parent = Parent as ScrollViewer;
        if (parent != null && parent.Bounds.Width > 0) w = parent.Bounds.Width;
        double cw1 = Cw(100) / 100;
        fs = Math.Max(11, Math.Min(18, (w - 16) / (GrubScreen.W * cw1)));
        return new Size(Math.Max(w, GrubScreen.W * Cw(fs) + 16), GrubScreen.H * Lh(fs) + 16);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Bg, new Rect(Bounds.Size));
        var g = owner.S.Grub;
        if (g == null) return;
        var rows = g.Rows();
        double cw = Cw(fs), lh = Lh(fs);
        double x0 = Math.Max(8, (Bounds.Width - GrubScreen.W * cw) / 2), y0 = 8;
        for (int i = 0; i < rows.Length; i++)
        {
            double y = y0 + i * lh;
            string row = rows[i];
            if (i == g.HighlightRow && row.Length >= 2 + GrubScreen.Inner)
            {
                dc.DrawText(Mono(row.Substring(0, 2), fs, Fg), new Point(x0, y));
                dc.FillRectangle(HiBg, new Rect(x0 + 2 * cw, y, GrubScreen.Inner * cw, lh));
                dc.DrawText(Mono(row.Substring(2, GrubScreen.Inner), fs, HiFg), new Point(x0 + 2 * cw, y));
                dc.DrawText(Mono(row.Substring(2 + GrubScreen.Inner), fs, Fg), new Point(x0 + (2 + GrubScreen.Inner) * cw, y));
            }
            else dc.DrawText(Mono(row.TrimEnd(), fs, i == g.HintRow ? HintFg : Fg), new Point(x0, y));
        }
        if (g.CursorRow >= 0) dc.FillRectangle(Fg, new Rect(x0 + g.CursorCol * cw, y0 + g.CursorRow * lh + lh - 3, cw, 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { Focus(); e.Handled = true; }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (char ch in e.Text ?? "") if (ch >= 32 && ch != 127) sb.Append(ch);
        e.Handled = true;
        if (sb.Length == 0) return;
        string t = sb.ToString();
        owner.GrubInput(() => owner.S.GrubType(t));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0, shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (CertTerminal.IsPasteKey(e.Key, ctrl, shift)) { e.Handled = true; _ = owner.PasteClipboard(); return; }
        string k = CertTerminal.KeyName(e.Key, ctrl);
        if (k == null) return;   // letters, digits, space: they come as TextInput
        e.Handled = true;        // never let the page move the focus (Tab, arrows) or take Esc
        owner.GrubInput(() => owner.S.GrubKey(k));
    }
}
