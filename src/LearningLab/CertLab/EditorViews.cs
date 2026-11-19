// Cert Lab (Learning Lab) - the editors over the terminal (App Hub's Editor.cs screens): vi / vim (VimView draws a
// VimEngine and feeds it keys; on a phone the terminal's "type here" box and key row type into it) and nano (a text box
// with Save = Ctrl+O, Exit = Ctrl+X which asks to save, Cut line = Ctrl+K, and the same as buttons).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

/// <summary>Draws a VimEngine (text, ~ for lines past the end, the cursor, the status line).</summary>
public sealed class VimView : Control
{
    public readonly VimEngine E;
    public event Action Closed;
    const double Fs = 14;
    static double lineH, charW;

    public VimView(VimEngine e)
    {
        E = e;
        Focusable = true;
        if (charW <= 0)
        {
            var ft = Mono("MMMMMMMMMM", Brushes.White);
            charW = ft.Width / 10; lineH = Math.Ceiling(ft.Height);
        }
    }
    static FormattedText Mono(string s, IBrush b) => new(s ?? "", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(CertUi.Mono), Fs, b);
    int Rows => Math.Max(3, (int)((Bounds.Height - 12) / lineH) - 1);

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Ui.Brush(CertUi.TermBg), new Rect(Bounds.Size));
        int rows = Rows;
        if (E.Row < E.Top) E.Top = E.Row;
        if (E.Row >= E.Top + rows) E.Top = E.Row - rows + 1;
        IBrush fg = Ui.Brush(CertUi.TermFg), dim = Ui.Brush("#4C6A92"), numc = Ui.Brush("#8C93A5");
        int gutter = E.Number ? 4 : 0;
        for (int i = 0; i < rows; i++)
        {
            int r = E.Top + i;
            double y = 6 + i * lineH;
            if (r < E.L.Count)
            {
                if (E.Number) dc.DrawText(Mono((r + 1).ToString().PadLeft(3) + " ", numc), new Point(6, y));
                dc.DrawText(Mono(E.L[r].Replace("\t", "        "), fg), new Point(6 + gutter * charW, y));
            }
            else dc.DrawText(Mono("~", dim), new Point(6, y));
        }
        string line = E.L[Math.Min(E.Row, E.L.Count - 1)];
        int vis = line.Substring(0, Math.Min(E.Col, line.Length)).Replace("\t", "        ").Length;
        double x = 6 + (gutter + vis) * charW, cy = 6 + (E.Row - E.Top) * lineH;
        if (E.Mode == "insert") dc.FillRectangle(fg, new Rect(x, cy, 2, lineH));
        else dc.FillRectangle(new SolidColorBrush(Color.FromArgb(150, 216, 222, 233)), new Rect(x, cy, charW, lineH));
        double sy = Bounds.Height - lineH - 6;
        string st = E.StatusLine ?? "";
        dc.DrawText(Mono(st, st.StartsWith('E') || st.StartsWith('W') ? Ui.Brush(Ui.Red) : fg), new Point(6, sy));
        string pos = (E.Row + 1) + "," + (E.Col + 1);
        var pf = Mono(pos.PadRight(10) + (E.L.Count <= rows ? "All" : E.Top == 0 ? "Top" : E.Top + rows >= E.L.Count ? "Bot" : (E.Top * 100 / Math.Max(1, E.L.Count - rows)) + "%"), numc);
        dc.DrawText(pf, new Point(Math.Max(0, Bounds.Width - pf.Width - 10), sy));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { Focus(); e.Handled = true; }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        foreach (char ch in e.Text ?? "") if (ch >= 32 && ch != 127) E.Key(ch.ToString());
        e.Handled = true;
        After();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0, shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (CertTerminal.IsPasteKey(e.Key, ctrl, shift)) { e.Handled = true; _ = PasteClipboard(); return; }
        string k = null;
        if (ctrl && e.Key == Avalonia.Input.Key.R) k = "C-r";
        else if (ctrl && e.Key == Avalonia.Input.Key.C) k = "C-c";
        else if (ctrl && e.Key == Avalonia.Input.Key.OemOpenBrackets) k = "Esc";
        else if (!ctrl)
        {
            k = CertTerminal.KeyName(e.Key, false);
            if (k == "F2" || k == "F10") k = null;
        }
        if (k == null) return;
        Key(k);
        e.Handled = true;
    }

    public void Key(string k) { E.Key(k); After(); }
    public async Task PasteClipboard() { var t = await AppClipboard.Read(); if (t != null) Paste(t); }
    public void Paste(string text) { E.Paste(text); After(); }
    void After() { InvalidateVisual(); if (E.Done) Closed?.Invoke(); }
}

/// <summary>The editor over the terminal: vi / vim (VimView) or nano.</summary>
public sealed class EditorHost : DockPanel
{
    readonly CertTerminal owner;
    readonly EditRequest req;
    readonly VimView vim;
    readonly TextBox nano;
    readonly TextBlock nanoStatus;
    readonly WrapPanel nanoButtons;
    string nanoSaved;
    bool nanoAskSave;
    const string NanoHelp = "^O Save   ^X Exit   ^K Cut a line";

    public EditorHost(CertTerminal o, EditRequest r)
    {
        owner = o; req = r;
        Background = Ui.Brush(CertUi.TermBg);
        if (r.Kind == "nano")
        {
            var title = new TextBlock { Text = "  GNU nano 5.6.1    " + r.Path, Foreground = Ui.Brush(CertUi.TermBg), Background = Ui.Brush("#D1D5DB"), FontFamily = CertUi.Mono, FontSize = 13.5, Padding = new Thickness(4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(title, Dock.Top);
            nanoButtons = new WrapPanel { Margin = new Thickness(6, 4, 6, 0) };
            DockPanel.SetDock(nanoButtons, Dock.Bottom);
            nanoStatus = new TextBlock { Foreground = Ui.Brush("#E5E7EB"), FontFamily = CertUi.Mono, FontSize = 13, Padding = new Thickness(8, 4), Text = NanoHelp, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(nanoStatus, Dock.Bottom);
            nano = new TextBox { AcceptsReturn = true, AcceptsTab = true, Text = r.Text ?? "", FontFamily = CertUi.Mono, FontSize = 14, Background = Ui.Brush(CertUi.TermBg), Foreground = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(0), CaretBrush = Ui.Brush("#E5E7EB"), Padding = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.NoWrap };
            Avalonia.Input.TextInput.TextInputOptions.SetAutoCapitalization(nano, false);
            nanoSaved = nano.Text;
            nano.AddHandler(KeyDownEvent, NanoKey, RoutingStrategies.Tunnel);
            Children.Add(title); Children.Add(nanoButtons); Children.Add(nanoStatus); Children.Add(nano);
            NanoButtons();
            return;
        }
        var e = new VimEngine(r.Path, r.Text, r.ReadOnly);
        e.OnWrite = t => { r.Path = e.Path; r.OnSave?.Invoke(t); r.Result = t; r.Saved = true; };
        vim = new VimView(e);
        vim.Closed += () => Finish(e.Saved ? (e.Result ?? e.Text) : null, e.Saved);
        Children.Add(vim);
    }

    public object KeyTarget => vim != null ? vim : nano;
    public VimEngine Vim => vim?.E;
    public TextBox Nano => nano;
    public void VimKey(string k) => vim?.Key(k);

    /// <summary>Pasted text at the cursor: vi as VimEngine.Paste does, nano where its cursor is.</summary>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (vim != null) { vim.Paste(text); return; }
        if (nano == null || nanoAskSave) return;
        string t = ClipText.Lf(text);
        string cur = nano.Text ?? "";
        int at = Math.Clamp(Math.Min(nano.SelectionStart, nano.SelectionEnd), 0, cur.Length);
        int end = Math.Clamp(Math.Max(nano.SelectionStart, nano.SelectionEnd), 0, cur.Length);
        nano.Text = cur.Substring(0, at) + t + cur.Substring(end);
        nano.CaretIndex = at + t.Length;
    }

    void NanoButtons()
    {
        nanoButtons.Children.Clear();
        void B(string text, Action a, bool primary = false)
        {
            var b = Ui.Button(text, primary, 13); b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(12, 6);
            b.Click += (s, e) => { CertUi.Safe(a); if (nano.IsVisible) nano.Focus(); };
            nanoButtons.Children.Add(b);
        }
        if (nanoAskSave)
        {
            B("Yes, save", () => AnswerSave('y'), true);
            B("No", () => AnswerSave('n'));
            B("Cancel", () => AnswerSave('c'));
            return;
        }
        B("Save (Ctrl+O)", NanoSave);
        B("Exit (Ctrl+X)", NanoExit);
        B("Cut line (Ctrl+K)", CutLine);
        B("Paste", () => _ = PasteAsync());
    }
    async Task PasteAsync() { var t = await AppClipboard.Read(); if (t != null) Paste(t); }

    string NanoText => (nano.Text ?? "").Replace("\r\n", "\n");

    public void NanoSave()
    {
        string text = NanoText;
        Save(text);
        nanoStatus.Text = "[ Wrote " + text.Split('\n').Length + " lines ]";
        nanoSaved = nano.Text;
    }
    public void NanoExit()
    {
        if (nano.Text != nanoSaved) { nanoAskSave = true; nanoStatus.Text = "Save modified buffer?  Y Yes   N No   ^C Cancel"; NanoButtons(); return; }
        Finish(req.Saved ? req.Result : null, req.Saved);
    }
    /// <summary>The answer to "Save modified buffer?": y, n or c (cancel).</summary>
    public void AnswerSave(char a)
    {
        if (!nanoAskSave) return;
        if (a == 'y') { string text = NanoText; Save(text); Finish(text + (text.EndsWith('\n') ? "" : "\n"), true); }
        else if (a == 'n') Finish(req.Saved ? req.Result : null, req.Saved);
        else { nanoAskSave = false; nanoStatus.Text = NanoHelp; NanoButtons(); }
    }
    void CutLine()
    {
        string t = nano.Text ?? "";
        int c = Math.Clamp(nano.CaretIndex, 0, t.Length);
        int st = t.LastIndexOf('\n', Math.Max(0, c - 1)); st = c == 0 ? 0 : st + 1;
        if (c > 0 && t[c - 1] == '\n') st = c;
        int en = t.IndexOf('\n', c); en = en < 0 ? t.Length : en + 1;
        nano.Text = t.Remove(st, en - st);
        nano.CaretIndex = Math.Min(st, nano.Text.Length);
    }

    void NanoKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0, shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (nanoAskSave)
        {
            if (e.Key == Key.Y) AnswerSave('y');
            else if (e.Key == Key.N) AnswerSave('n');
            else if (ctrl && e.Key == Avalonia.Input.Key.C || e.Key == Key.Escape) AnswerSave('c');
            e.Handled = true; return;
        }
        if (ctrl && shift && e.Key == Key.V) { e.Handled = true; _ = PasteAsync(); return; }
        if (ctrl && e.Key == Key.O) { NanoSave(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.X) { NanoExit(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.K) { CutLine(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.G) { nanoStatus.Text = "Ctrl+O saves, Ctrl+X leaves (asks to save), Ctrl+K cuts a line. The arrows move."; e.Handled = true; return; }
        if (e.Key == Key.Escape) e.Handled = true;   // Esc stays in nano (it isn't Back here)
    }

    void Save(string text)
    {
        string t = text.EndsWith('\n') ? text : text + "\n";
        try { req.OnSave?.Invoke(t); req.Result = t; req.Saved = true; }
        catch (Exception ex) { nanoStatus.Text = "[ Error writing " + req.Path + ": " + ex.Message + " ]"; }
    }

    void Finish(string result, bool saved) => owner.EditDone(result, saved);
}
