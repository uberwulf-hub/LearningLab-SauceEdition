// Cert Lab (Learning Lab) - the pieces Cert Lab's pages are built from (App Hub's MainView helpers H1, H2, P, Muted,
// Pane, Bar, TermBlock and CoursePage.Rich, for Avalonia, touch first): selectable text with `code` in a monospaced
// font and **bold**, Copy chips for code worth copying (a row of 44 px buttons under the text, not tiny buttons inside
// it), terminal blocks with a Copy button, and the clipboard: ClipText (the engine's) goes to Avalonia's clipboard
// through AppClipboard while Cert Lab is open, and stays in memory in the checks.
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public static class CertUi
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
    public const string TermBg = "#0B0F14", TermFg = "#D8DEE9", Code = "#E2E8F0";

    /// <summary>What a Copy chip or block says when it copied (the page's status line).</summary>
    public static Action<string> Copied;

    public static TextBlock H1(string t) { var x = Ui.Text(t, 24, Ui.TextC, true); x.Margin = new Thickness(0, 0, 0, 6); return x; }
    public static TextBlock H2(string t) { var x = Ui.Text(t, 18, Ui.AccentText, true); x.Margin = new Thickness(0, 14, 0, 6); return x; }
    public static TextBlock Muted(string t) { var x = Ui.Text(t, 13.5, Ui.Muted); x.Margin = new Thickness(0, 0, 0, 6); return x; }

    /// <summary>A paragraph the person can select and copy.</summary>
    public static SelectableTextBlock P(string t)
    {
        return new SelectableTextBlock { Text = t, FontSize = 15, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), LineHeight = 22, SelectionBrush = Ui.Brush("#2F5A8A") };
    }

    public static Border Pane(Control c, double pad = 14)
    {
        return new Border { Child = c, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(pad), Margin = new Thickness(0, 0, 0, 12) };
    }

    /// <summary>A progress bar (0..1) that stretches to its space.</summary>
    public static Control Bar(double frac, string fill = null, double height = 8)
    {
        frac = Math.Max(0, Math.Min(1, double.IsNaN(frac) ? 0 : frac));
        var g = new Grid { Height = height, Margin = new Thickness(0, 4, 0, 4), ColumnDefinitions = new ColumnDefinitions(Ui.F(Math.Max(frac, 0.0001), "0.0000") + "*," + Ui.F(Math.Max(1 - frac, 0.0001), "0.0000") + "*") };
        var back = new Border { Background = Ui.Brush(Ui.Surface3), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(back, 2);
        g.Children.Add(back);
        if (frac > 0) g.Children.Add(new Border { Background = Ui.Brush(fill ?? Ui.Accent), CornerRadius = new CornerRadius(height / 2) });
        return g;
    }

    /// <summary>A small button for toolbars and lists (still 44 px high: touch first).</summary>
    public static Button Btn(string text, Action click, bool primary = false)
    {
        var b = Ui.Button(text, primary, 14);
        b.Padding = new Thickness(14, 8);
        b.Margin = new Thickness(0, 0, 8, 8);
        if (click != null) b.Click += (s, e) => Safe(click);
        return b;
    }
    public static Button Accent(string text, Action click) => Btn(text, click, true);

    /// <summary>Errors in handlers become a sentence on the page instead of a crash.</summary>
    public static Action<string> OnError;
    public static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { OnError?.Invoke("Something went wrong: " + ex.Message); }
    }

    /// <summary>A WrapPanel with buttons.</summary>
    public static WrapPanel Row(params Control[] items)
    {
        var w = new WrapPanel();
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    // ------------------------------------------------------------ rich text
    static readonly Regex RichRx = new("`([^`]+)`|\\*\\*([^*]+)\\*\\*|([^`*]+|\\*)", RegexOptions.Compiled);

    /// <summary>The text of a lesson paragraph, task or question: selectable, with `code` monospaced and **bold**.</summary>
    public static SelectableTextBlock RichText(string text, double size = 15)
    {
        var tb = new SelectableTextBlock { FontSize = size, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.47, SelectionBrush = Ui.Brush("#2F5A8A") };
        tb.Inlines = new InlineCollection();
        foreach (Match m in RichRx.Matches(text ?? ""))
        {
            if (m.Groups[1].Success && m.Groups[1].Value.Length > 0) tb.Inlines.Add(new Run(m.Groups[1].Value) { FontFamily = Mono, Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Code) });
            else if (m.Groups[2].Success && m.Groups[2].Value.Length > 0) tb.Inlines.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeight.SemiBold });
            else tb.Inlines.Add(new Run(m.Value));
        }
        return tb;
    }

    /// <summary>Plain (not selectable) rich text, for the content of answer choices: a tap picks them.</summary>
    public static TextBlock PlainRich(string text, double size = 15)
    {
        var tb = new TextBlock { FontSize = size, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap };
        tb.Inlines = new InlineCollection();
        foreach (Match m in RichRx.Matches(text ?? ""))
        {
            if (m.Groups[1].Success && m.Groups[1].Value.Length > 0) tb.Inlines.Add(new Run(m.Groups[1].Value) { FontFamily = Mono, Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Code) });
            else if (m.Groups[2].Success && m.Groups[2].Value.Length > 0) tb.Inlines.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeight.SemiBold });
            else tb.Inlines.Add(new Run(m.Value));
        }
        return tb;
    }

    /// <summary>The code in a text worth a Copy chip: a path, a command with options, a password (3+ characters).</summary>
    public static List<string> CodeIn(string text)
    {
        var l = new List<string>();
        foreach (Match m in RichRx.Matches(text ?? ""))
            if (m.Groups[1].Success && Worth(m.Groups[1].Value) && !l.Contains(m.Groups[1].Value)) l.Add(m.Groups[1].Value);
        return l;
    }
    /// <summary>Text for reading aloud: `code` and **bold** marks taken away.</summary>
    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = Regex.Replace(text, "`([^`]+)`", "$1");
        return Regex.Replace(s, "\\*\\*([^*]+)\\*\\*", "$1").Trim();
    }
    public static bool Worth(string code) => code != null && code.Trim().Length >= 3;

    /// <summary>Rich text and, under it, a Copy chip for each piece of code worth copying (CodeCopy).</summary>
    public static Control Rich(string text, double size = 15)
    {
        var tb = RichText(text, size);
        var codes = CodeIn(text);
        if (codes.Count == 0) return tb;
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(tb);
        var chips = new WrapPanel();
        foreach (var c in codes.Take(6)) chips.Children.Add(new CodeCopy(c));
        sp.Children.Add(chips);
        return sp;
    }

    /// <summary>A block of terminal text (examples in lessons, model solutions): its columns kept (long lines scroll
    /// sideways), selectable, with a Copy button.</summary>
    public static Border TermBlock(string text)
    {
        text ??= "";
        int widest = text.Replace("\r", "").Split('\n').Max(l => l.Length);
        var tb = new SelectableTextBlock { Text = text, FontFamily = Mono, FontSize = widest > 100 ? 12.5 : 13.5, Foreground = Ui.Brush(TermFg), TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(10, 8, 10, 8), SelectionBrush = Ui.Brush("#2F5A8A") };
        var sv = new ScrollViewer { Content = tb, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var copy = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = "Copy", FontSize = 13, MinHeight = 44, MinWidth = 64, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 0, 0), Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Ui.TextC), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Tag = text };
        ToolTip.SetTip(copy, "Copy this block");
        copy.Click += (s, e) => Safe(() => { bool ok = ClipText.SetText(text); Copied?.Invoke(ok ? "Copied the block." : "The clipboard is busy. Try again."); });
        var dock = new DockPanel();
        DockPanel.SetDock(copy, Dock.Right);
        dock.Children.Add(copy);
        dock.Children.Add(sv);
        return new Border { Child = dock, CornerRadius = new CornerRadius(8), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 12), Background = Ui.Brush(TermBg), Padding = new Thickness(0, 0, 4, 0) };
    }

    /// <summary>A question's text: lines indented by 4 spaces are code ("What does this print?"), shown as a block.</summary>
    public static Control QuestionView(string text)
    {
        var sp = new StackPanel();
        var prose = new List<string>(); var code = new List<string>();
        void Flush()
        {
            if (prose.Count > 0) { sp.Children.Add(Rich(string.Join("\n", prose))); prose.Clear(); }
            if (code.Count > 0) { var b = TermBlock(string.Join("\n", code)); b.Margin = new Thickness(0, 6, 0, 6); sp.Children.Add(b); code.Clear(); }
        }
        foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
        {
            bool isCode = line.StartsWith("    ", StringComparison.Ordinal);
            if (isCode && prose.Count > 0 || !isCode && code.Count > 0) Flush();
            if (isCode) code.Add(line.Substring(4)); else prose.Add(line);
        }
        Flush();
        return sp;
    }

    /// <summary>All the controls of a type inside a control (logical tree: the checks find buttons this way).</summary>
    public static IEnumerable<T> Find<T>(Control root) where T : class
    {
        if (root == null) yield break;
        if (root is T t) yield return t;
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(root))
            if (c is Control cc) foreach (var x in Find<T>(cc)) yield return x;
    }

    /// <summary>Presses the first button whose text is label (the checks).</summary>
    public static bool Press(Control root, string label)
    {
        foreach (var b in Find<Button>(root))
            if (ButtonText(b) == label && b.IsEffectivelyEnabled) { b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return true; }
        return false;
    }
    public static string ButtonText(Button b) => b.Content as string ?? (b.Content as TextBlock)?.Text ?? "";
}

/// <summary>A Copy chip for a piece of code in lesson or task text: a 44 px button showing the code.</summary>
public sealed class CodeCopy : Button
{
    public readonly string Code;
    public CodeCopy(string code)
    {
        Code = code;
        string shown = code.Length > 34 ? code.Substring(0, 32) + "…" : code;
        Content = new TextBlock { Inlines = new InlineCollection { new Run("⧉ ") { Foreground = Ui.Brush(Ui.AccentText) }, new Run(shown) { FontFamily = CertUi.Mono } }, FontSize = 13 };
        MinHeight = 44; Padding = new Thickness(10, 4); Margin = new Thickness(0, 0, 6, 6); VerticalContentAlignment = VerticalAlignment.Center;
        Background = Ui.Brush(Ui.Surface2); Foreground = Ui.Brush(CertUi.Code); BorderBrush = Ui.Brush(Ui.BorderStrong); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8);
        ToolTip.SetTip(this, "Copy " + code);
        Click += (s, e) => Copy();
    }
    public bool Copy()
    {
        bool ok = ClipText.SetText(Code);
        CertUi.Copied?.Invoke(ok ? "Copied " + Code : "The clipboard is busy. Try again.");
        return ok;
    }
}

/// <summary>The engine's clipboard on Avalonia's: setting goes to the device's clipboard (and is remembered); reading
/// is asynchronous on Avalonia, so pastes ask ReadAsync, and GetText gives what Cert Lab copied last.</summary>
public sealed class AppClipboard : IClipboard
{
    readonly Func<TopLevel> top;
    string last;
    public AppClipboard(Func<TopLevel> topLevel) { top = topLevel; }
    public string GetText() => last;
    public void SetText(string text)
    {
        last = text;
        var cb = top()?.Clipboard;
        if (cb != null) _ = Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(cb, text);
    }
    public async Task<string> ReadAsync()
    {
        var cb = top()?.Clipboard;
        if (cb == null) return last;
        try { return await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb) ?? last; }
        catch (Exception) { return last; }
    }

    /// <summary>The clipboard's text with LF line ends: the device's (asynchronously) or the in-memory one.</summary>
    public static async Task<string> Read()
    {
        if (ClipText.Current is AppClipboard a) { var t = await a.ReadAsync(); return t == null ? null : ClipText.Lf(t); }
        return ClipText.GetText();
    }
}
