// Network Lab - a terminal (App Hub's ConsoleView for Avalonia): the routers' and switches' command line, the PCs'
// Command Prompt and PowerShell, and the Linux and macOS terminals, with the little nano-style editor. Touch first: the
// line being typed is a real text box (so a phone's keyboard works), with a key row for Tab, ?, Up, Down, Ctrl+C and
// Ctrl+Z. Output is selectable (copy works). Tab: one match completes the word, several complete what they share and a
// second Tab lists them; Windows-like terminals show the next match on each Tab. Up / Down bring back earlier commands
// (never passwords).
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

/// <summary>What a terminal talks to.</summary>
public interface IConsoleBackend
{
    string Prompt { get; }
    bool Busy { get; }
    bool Secret { get; }
    /// <summary>? shows help at once (router CLI).</summary>
    bool HelpKey { get; }
    void Execute(string line);
    void Help(string line);
    Completion Candidates(string line);
    /// <summary>ios / bash (complete, a second Tab lists) or cycle (Windows: each Tab shows the next match).</summary>
    string TabStyle { get; }
    void Break();
    void CtrlZ();
    string TakeOutput();
    bool Closed { get; }
    bool TakeClear();
    Shell EditorShell { get; }
    string Banner { get; }
}

public sealed class CliBackend : IConsoleBackend
{
    public readonly CliSession S;
    public CliBackend(CliSession s) { S = s; }
    public string Prompt => S.Prompt;
    public bool Busy => S.Busy != null;
    public bool Secret => S.Pending != null && S.PendingSecret;
    public bool HelpKey => S.Pending == null;
    public void Execute(string line) => S.Execute(line);
    public void Help(string line) => S.Help(line);
    public Completion Candidates(string line) => S.Candidates(line);
    public string TabStyle => "ios";
    public void Break() => S.Break();
    public void CtrlZ() => S.End();
    public string TakeOutput() => S.TakeOutput();
    public bool Closed => false;
    public bool TakeClear() => false;
    public Shell EditorShell => null;
    public string Banner => "";
}

public sealed class ShellBackend : IConsoleBackend
{
    public readonly Shell S;
    public ShellBackend(Shell s) { S = s; }
    public string Prompt => S.CurrentPrompt;
    public bool Busy => S.Busy != null;
    public bool Secret => S.Pending != null && S.PendingSecret;
    public bool HelpKey => false;
    public void Execute(string line) => S.Execute(line);
    public void Help(string line) { }
    public Completion Candidates(string line) => S.Candidates(line);
    public string TabStyle => S.TabStyle;
    public void Break() => S.Break();
    public void CtrlZ() { }
    public string TakeOutput() => S.TakeOutput();
    public bool Closed => S.Closed;
    public bool TakeClear() { bool c = S.ClearRequested; S.ClearRequested = false; return c; }
    public Shell EditorShell => S.EditorFile != null ? S : null;
    public string Banner => S.Banner;
}

public sealed class NetTerminal : Grid
{
    public IConsoleBackend Backend { get; }
    public event Action ClosedByUser;
    readonly SelectableTextBlock output;
    readonly ScrollViewer scroll;
    readonly TextBlock prompt;
    readonly TextBox input;
    readonly Grid editor;
    readonly TextBox editorBox;
    readonly TextBlock editorTitle, editorStatus;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly List<string> lines = new() { "" };
    readonly List<string> history = new();
    int histPos = -1;
    Shell editing;
    public const int MaxLines = 3000;
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>Tests: how many times Tab found nothing.</summary>
    public int Flashes { get; private set; }

    public NetTerminal(IConsoleBackend backend, string bg, string fg)
    {
        Backend = backend;
        RowDefinitions = new RowDefinitions("*,Auto,Auto");
        Background = Ui.Brush(bg);
        output = new SelectableTextBlock { FontFamily = Mono, FontSize = 14, Foreground = Ui.Brush(fg), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8, 10, 0) };
        scroll = new ScrollViewer { Content = output, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(scroll);

        prompt = new TextBlock { FontFamily = Mono, FontSize = 14, Foreground = Ui.Brush(fg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
        input = new TextBox
        {
            FontFamily = Mono, FontSize = 14, Foreground = Ui.Brush(fg), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            MinHeight = 40, Padding = new Thickness(4, 8), VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Ui.Brush(fg), AcceptsTab = false,
        };
        TextInputOptions.SetAutoCapitalization(input, false);
        input.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        input.AddHandler(TextInputEvent, OnText, RoutingStrategies.Tunnel);
        var line = new DockPanel { Background = Ui.Brush(bg) };
        DockPanel.SetDock(prompt, Dock.Left);
        line.Children.Add(prompt); line.Children.Add(input);
        Grid.SetRow(line, 1);
        Children.Add(line);

        var keys = new WrapPanel { Margin = new Thickness(6, 2, 6, 6) };
        void Key(string text, string tip, Action a)
        {
            var b = new Button { Content = text, FontSize = 13, MinHeight = 44, MinWidth = 48, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 0), Background = Ui.Brush("#1F2937"), Foreground = Ui.Brush("#E5E7EB") };
            ToolTip.SetTip(b, tip);
            b.Click += (s, e) => { a(); input.Focus(); };
            keys.Children.Add(b);
        }
        Key("Tab", "Finish the word", () => Tab(false));
        if (backend.HelpKey) Key("?", "What can I type next?", () => HelpNow());
        Key("↑", "The command before", () => History(-1));
        Key("↓", "The next command", () => History(1));
        Key("Ctrl+C", "Stop (a ping that keeps going)", Break);
        if (backend is CliBackend) Key("Ctrl+Z", "Back to the # prompt", CtrlZ);
        Key("Clear", "Clear the screen", () => { lines.Clear(); lines.Add(""); ShowOutput(); });
        var keyBorder = new Border { Child = keys, Background = Ui.Brush("#111827") };
        Grid.SetRow(keyBorder, 2);
        Children.Add(keyBorder);

        // the nano-style editor, over everything while a file is open
        editor = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = Ui.Brush("#0B0F14"), IsVisible = false };
        Grid.SetRowSpan(editor, 3);
        editorTitle = new TextBlock { Foreground = Ui.Brush("#0B0F14"), Background = Ui.Brush("#D1D5DB"), FontFamily = Mono, FontSize = 13.5, Padding = new Thickness(6, 3), TextAlignment = TextAlignment.Center };
        editorBox = new TextBox { AcceptsReturn = true, FontFamily = Mono, FontSize = 14, Background = Ui.Brush("#0B0F14"), Foreground = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(6) };
        editorBox.AddHandler(KeyDownEvent, EditorKey, RoutingStrategies.Tunnel);
        Grid.SetRow(editorBox, 1);
        var eb = new WrapPanel { Margin = new Thickness(6) };
        editorStatus = new TextBlock { Foreground = Ui.Brush("#E5E7EB"), FontFamily = Mono, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0), Text = "^O Save   ^X Exit" };
        var save = Ui.Button("Save (Ctrl+O)", false, 13); save.Margin = new Thickness(0, 0, 6, 0); save.Click += (s, e) => EditorSave();
        var exit = Ui.Button("Exit (Ctrl+X)", false, 13); exit.Click += (s, e) => EditorExit();
        eb.Children.Add(save); eb.Children.Add(exit); eb.Children.Add(editorStatus);
        Grid.SetRow(eb, 2);
        editor.Children.Add(editorTitle); editor.Children.Add(editorBox); editor.Children.Add(eb);
        Children.Add(editor);

        if (backend.Banner.Length > 0) Append(backend.Banner);
        timer.Tick += (s, e) => { try { Poll(); } catch (Exception ex) { Append("\n(" + ex.Message + ")\n"); } };
        AttachedToVisualTree += (s, e) => { timer.Start(); Dispatcher.UIThread.Post(() => input.Focus(), DispatcherPriority.Input); };
        DetachedFromVisualTree += (s, e) => timer.Stop();
        Poll();
    }

    public void FocusInput() { if (editing != null) editorBox.Focus(); else input.Focus(); }

    /// <summary>Everything shown: the output, the prompt and the line being typed.</summary>
    public string Text => string.Join("\n", lines) + Backend.Prompt + (input.Text ?? "");
    public string Typed { get => input.Text ?? ""; set { input.Text = value ?? ""; input.CaretIndex = input.Text.Length; } }
    public bool Editing => editing != null;
    public string EditorText { get => editorBox.Text; set => editorBox.Text = value; }

    void Append(string s)
    {
        foreach (char ch in (s ?? "").Replace("\r", ""))
        {
            if (ch == '\n') lines.Add("");
            else lines[^1] += ch;
        }
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        ShowOutput();
    }

    void ShowOutput()
    {
        // the last line is where the prompt sits: it is shown in the prompt row instead
        output.Text = lines.Count > 1 ? string.Join("\n", lines.Take(lines.Count - 1)) : "";
        Dispatcher.UIThread.Post(() => scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>Picks up new output (the timer, and the checks).</summary>
    public void Poll()
    {
        if (Backend.TakeClear()) { lines.Clear(); lines.Add(""); ShowOutput(); }
        string o = Backend.TakeOutput();
        if (o.Length > 0) Append(o);
        bool busy = Backend.Busy;
        string p = lines[^1] + (busy ? "" : Backend.Prompt);
        if (prompt.Text != p) prompt.Text = p;
        input.PasswordChar = Backend.Secret ? '•' : default;
        input.IsReadOnly = busy;
        var es = Backend.EditorShell;
        if (es != null && editing == null)
        {
            editing = es;
            editorTitle.Text = "nano-like editor (simulated)      " + es.EditorFile;
            editorBox.Text = es.EditorText ?? "";
            editor.IsVisible = true;
            editorStatus.Text = "^O Save   ^X Exit";
            Dispatcher.UIThread.Post(() => editorBox.Focus(), DispatcherPriority.Input);
        }
        else if (es == null && editing != null) { editing = null; editor.IsVisible = false; input.Focus(); }
        if (Backend.Closed) { timer.Stop(); ClosedByUser?.Invoke(); }
    }

    /// <summary>Types a line as if the person had (the checks).</summary>
    public void TypeLine(string line) { input.Text = line; Submit(); }

    void Submit()
    {
        if (Backend.Busy) return;
        string line = input.Text ?? "";
        bool secret = Backend.Secret;
        if (!secret && line.Trim().Length > 0) { history.Add(line); if (history.Count > 100) history.RemoveAt(0); }
        histPos = -1;
        lines[^1] += Backend.Prompt + (secret ? "" : line);
        Append("\n");
        input.Text = "";
        tabCount = 0; cycleList = null;
        Backend.Execute(line);
        Poll();
    }

    void HelpNow()
    {
        string l = input.Text ?? "";
        lines[^1] += Backend.Prompt + l + "?";
        Append("\n");
        Backend.Help(l + "?");
        Poll();
        input.Text = l; input.CaretIndex = l.Length;
    }

    void Break() { lines[^1] += Backend.Prompt + (input.Text ?? "") + "^C"; Append("\n"); input.Text = ""; Backend.Break(); Poll(); }
    void CtrlZ() { lines[^1] += Backend.Prompt + (input.Text ?? "") + "^Z"; Append("\n"); input.Text = ""; Backend.CtrlZ(); Poll(); }

    void History(int dir)
    {
        if (dir < 0) { if (history.Count == 0) return; histPos = histPos < 0 ? history.Count - 1 : Math.Max(0, histPos - 1); input.Text = history[histPos]; }
        else { if (histPos < 0) return; histPos++; if (histPos >= history.Count) { histPos = -1; input.Text = ""; } else input.Text = history[histPos]; }
        input.CaretIndex = (input.Text ?? "").Length;
    }

    void OnText(object sender, TextInputEventArgs e)
    {
        if (Backend.Busy) { e.Handled = true; return; }
        if (e.Text == "?" && Backend.HelpKey && !Backend.Secret) { e.Handled = true; HelpNow(); return; }
        tabCount = 0; cycleList = null;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0, shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (e.Key != Key.Tab && e.Key != Key.LeftShift && e.Key != Key.RightShift) { tabCount = 0; cycleList = null; }
        if (ctrl && (e.Key == Key.C && string.IsNullOrEmpty(input.SelectedText) || e.Key == Key.D6 && shift)) { Break(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Z) { CtrlZ(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.L) { lines.Clear(); lines.Add(""); ShowOutput(); e.Handled = true; return; }
        if (Backend.Busy) { if (e.Key == Key.Enter || e.Key == Key.Tab) e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.Enter: Submit(); e.Handled = true; break;
            case Key.Tab: Tab(shift); e.Handled = true; break;
            case Key.Up: History(-1); e.Handled = true; break;
            case Key.Down: History(1); e.Handled = true; break;
            case Key.Escape: if ((input.Text ?? "").Length > 0) { input.Text = ""; e.Handled = true; } break;
            case Key.PageUp: scroll.PageUp(); e.Handled = true; break;
            case Key.PageDown: scroll.PageDown(); e.Handled = true; break;
        }
    }

    // ------------------------------------------------------------ Tab
    int tabCount;
    string tabInput = "";
    List<string> cycleList;
    int cycleIdx;
    string cycleBase = "", cycleResult = "";

    /// <summary>Tab (the checks call it too): see the file's notes.</summary>
    public void Tab(bool back)
    {
        if (Backend.Secret) return;
        string text = input.Text ?? "";
        int caret = Math.Clamp(input.CaretIndex, 0, text.Length);
        string head = text.Substring(0, caret), tail = text.Substring(caret);
        Completion c;
        if (Backend.TabStyle == "cycle")
        {
            if (cycleList != null && head == cycleResult) { int n = cycleList.Count; cycleIdx = ((cycleIdx + (back ? -1 : 1)) % n + n) % n; }
            else
            {
                c = Backend.Candidates(head);
                if (c == null || c.Words.Count == 0) { Flashes++; return; }
                cycleBase = head.Substring(0, Math.Min(c.Start, head.Length));
                cycleList = c.Words;
                cycleIdx = back ? cycleList.Count - 1 : 0;
            }
            cycleResult = cycleBase + cycleList[cycleIdx];
            SetInput(cycleResult + tail, cycleResult.Length);
            return;
        }
        c = Backend.Candidates(head);
        if (c == null || c.Words.Count == 0) { tabCount = 0; Flashes++; return; }
        int start = Math.Min(c.Start, head.Length);
        string typed = head.Substring(start);
        bool again = tabCount > 0 && head == tabInput;
        string pick = c.Words.Count == 1 ? c.Words[0] : Backend.TabStyle == "ios" ? c.Words.FirstOrDefault(w => string.Equals(w, typed, StringComparison.OrdinalIgnoreCase)) : null;
        if (pick != null)
        {
            string done = head.Substring(0, start) + pick + (c.NoSpace.Contains(pick) ? "" : " ");
            SetInput(done + tail.TrimStart(), done.Length);
            tabCount = 0;
            return;
        }
        string common = CommonStart(c.Words);
        if (common.Length > typed.Length)
        {
            string done = head.Substring(0, start) + common;
            SetInput(done + tail, done.Length);
            tabCount = 1; tabInput = done;
            return;
        }
        if (again) { ListCompletions(text, c.Words); tabCount = 0; return; }
        tabCount = 1; tabInput = head;
    }

    void SetInput(string text, int caret) { input.Text = text; input.CaretIndex = caret; }

    static string CommonStart(List<string> words)
    {
        string p = words[0];
        foreach (var w in words.Skip(1))
        {
            int i = 0;
            while (i < p.Length && i < w.Length && char.ToLowerInvariant(p[i]) == char.ToLowerInvariant(w[i])) i++;
            p = p.Substring(0, i);
        }
        return p;
    }

    void ListCompletions(string line, List<string> words)
    {
        var sb = new StringBuilder(Backend.Prompt + line + "\n");
        int w = words.Max(x => x.Length) + 2;
        int per = Math.Max(1, 60 / w);
        for (int i = 0; i < words.Count; i++)
        {
            sb.Append(words[i].PadRight(w));
            if (i % per == per - 1 || i == words.Count - 1) { sb.Length = sb.ToString().TrimEnd().Length; sb.Append('\n'); }
        }
        lines[^1] += sb.ToString().TrimEnd('\n');
        Append("\n");
    }

    // ------------------------------------------------------------ the editor
    void EditorKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl && e.Key == Key.O) { EditorSave(); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { EditorExit(); e.Handled = true; }
        else if (e.Key == Key.Tab) { int c = editorBox.CaretIndex; editorBox.Text = (editorBox.Text ?? "").Insert(c, "  "); editorBox.CaretIndex = c + 2; e.Handled = true; }
    }

    public void EditorSave()
    {
        var sh = editing;
        if (sh == null) return;
        sh.EditorText = editorBox.Text ?? "";
        if (sh.EditorSaved != null && !sh.EditorReadOnly) sh.EditorSaved(sh, editorBox.Text ?? "");
        editorStatus.Text = "[ Saved " + (editorBox.Text ?? "").Split('\n').Length + " lines ]   ^X Exit";
        Poll();
    }

    public void EditorExit()
    {
        var sh = editing;
        if (sh == null) return;
        sh.CloseEditor(editorBox.Text ?? "", false);
        Poll();
    }
}
