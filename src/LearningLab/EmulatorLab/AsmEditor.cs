// Emulator Lab - the Assembler's editor, as App Hub's: a plain TextBox for typing (so the soft keyboard, selection,
// copy and paste and undo work everywhere), with a layer on top that draws the same text in colours (the TextBox's own
// text is transparent), a gutter with line numbers, and error marks. Positions come from the TextBox's own text layout.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmulatorLab;
using LearningLab.Views;

namespace LearningLab.EmulatorLab;

public sealed class AsmEditor : Grid
{
    public readonly TextBox Box = new();
    readonly Layer layer;
    readonly Gutter gutter;
    readonly HashSet<int> errorLines = new();
    internal const double Size = 14;
    internal static readonly Typeface Face = new(ScreenFont.Mono);

    public static readonly IBrush Plain = Ui.Brush("#D6DEEB"), Comment = Ui.Brush("#6A9955"), Directive = Ui.Brush("#C586C0"),
        Mnemonic = Ui.Brush("#569CD6"), Register = Ui.Brush("#9CDCFE"), Number = Ui.Brush("#B5CEA8"),
        Str = Ui.Brush("#CE9178"), LabelDef = Ui.Brush("#DCDCAA"), ErrorBand = Ui.Brush("#F87171", 0x38);

    public AsmEditor()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        Background = Ui.Brush("#0D1117");
        Box.FontFamily = ScreenFont.Mono;
        Box.FontSize = Size;
        Box.AcceptsReturn = true;
        Box.AcceptsTab = false;
        Box.TextWrapping = TextWrapping.NoWrap;
        Box.Foreground = Brushes.Transparent;
        Box.SelectionForegroundBrush = Brushes.Transparent;
        Box.CaretBrush = Ui.Brush("#F0F0F0");
        Box.Background = Brushes.Transparent;
        Box.BorderThickness = new Thickness(0);
        Box.Padding = new Thickness(6, 4, 6, 4);
        Box.VerticalContentAlignment = VerticalAlignment.Top;
        Box.HorizontalContentAlignment = HorizontalAlignment.Left;
        Box.SelectionBrush = Ui.Brush("#3B82F6", 90);
        Box.MinHeight = 0;
        Box.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        Box.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        Box.Resources["TextControlForegroundFocused"] = Brushes.Transparent;
        Box.Resources["TextControlForegroundPointerOver"] = Brushes.Transparent;
        Box.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        Box.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(Box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(Box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        SetColumn(Box, 1);
        layer = new Layer(this) { IsHitTestVisible = false, ClipToBounds = true };
        SetColumn(layer, 1);
        gutter = new Gutter(this) { Width = 48, ClipToBounds = true };
        SetColumn(gutter, 0);
        Children.Add(gutter);
        Children.Add(Box);
        Children.Add(layer);
        Box.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) Refresh(); };
        Box.SizeChanged += (s, e) => Refresh();
        Box.TemplateApplied += (s, e) =>
        {
            var sv = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (sv != null) sv.ScrollChanged += (s2, e2) => Refresh();
            Refresh();
        };
        Box.AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    bool refreshQueued;
    /// <summary>Redraws the colours and line numbers once the TextBox has laid out its new text.</summary>
    public void Refresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.UIThread.Post(() => { refreshQueued = false; layer.InvalidateVisual(); gutter.InvalidateVisual(); }, DispatcherPriority.Render);
    }

    public string Text
    {
        get => (Box.Text ?? "").Replace("\r\n", "\n");
        set { Box.Text = (value ?? "").Replace("\r\n", "\n"); Box.CaretIndex = 0; Refresh(); }
    }

    public void SetErrors(IEnumerable<int> lines)
    {
        errorLines.Clear();
        if (lines != null) foreach (int l in lines) errorLines.Add(l);
        Refresh();
    }

    public IReadOnlyCollection<int> ErrorLines => errorLines;

    /// <summary>Selects a line (1-based) and scrolls to it.</summary>
    public void GoToLine(int line)
    {
        string t = Box.Text ?? "";
        int idx = 0;
        for (int n = 1; n < line && idx >= 0; n++) { idx = t.IndexOf('\n', idx); if (idx >= 0) idx++; }
        if (idx < 0) idx = t.Length;
        int end = t.IndexOf('\n', idx);
        if (end < 0) end = t.Length;
        if (end > idx && t[end - 1] == '\r') end--;
        Box.Focus();
        Box.CaretIndex = idx;
        Box.SelectionStart = idx;
        Box.SelectionEnd = end;
        Refresh();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            // Tab: spaces to the next multiple of 4 (easier than real tab characters)
            string t = Box.Text ?? "";
            int caret = Math.Min(Box.CaretIndex, t.Length);
            int lineStart = caret == 0 ? 0 : t.LastIndexOf('\n', caret - 1) + 1;
            string pad = new string(' ', 4 - ((caret - lineStart) % 4));
            int a = Math.Min(Box.SelectionStart, Box.SelectionEnd), b = Math.Max(Box.SelectionStart, Box.SelectionEnd);
            if (b > a) { t = t.Remove(a, b - a); caret = a; }
            Box.Text = t.Insert(caret, pad);
            Box.CaretIndex = caret + pad.Length;
            Box.SelectionStart = Box.SelectionEnd = Box.CaretIndex;
            e.Handled = true;
        }
    }

    internal bool IsError(int line1) => errorLines.Contains(line1);

    /// <summary>The visible lines: index (0-based), the line's text, and its top and height relative to target.
    /// Empty when the TextBox hasn't laid out yet.</summary>
    internal IEnumerable<(int Line, string Text, double Top, double Height, double Left)> Lines(Visual target, out Rect viewport)
    {
        viewport = default;
        var list = new List<(int, string, double, double, double)>();
        var presenter = Box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        var sv = Box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (presenter == null || sv == null) return list;
        var origin = presenter.TranslatePoint(new Point(0, 0), target);
        var vpo = sv.TranslatePoint(new Point(0, 0), target);
        if (origin == null || vpo == null) return list;
        viewport = new Rect(vpo.Value, sv.Viewport);
        var layout = presenter.TextLayout;
        if (layout == null) return list;
        string text = presenter.Text ?? "";
        double y = origin.Value.Y;
        int i = 0;
        foreach (TextLine tl in layout.TextLines)
        {
            double h = tl.Height;
            if (y + h >= viewport.Top && y <= viewport.Bottom)
            {
                int start = Math.Min(tl.FirstTextSourceIndex, text.Length);
                int len = Math.Max(0, Math.Min(tl.Length, text.Length - start));
                string s = text.Substring(start, len).TrimEnd('\r', '\n');
                list.Add((i, s, y, h, origin.Value.X + tl.Start));
            }
            y += h;
            i++;
        }
        return list;
    }

    // ------------------------------------------------------------------ colours
    static bool IdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$';

    /// <summary>Splits a line into coloured pieces: (start, length, brush). (App Hub's rules, unchanged.)</summary>
    internal static List<(int Start, int Length, IBrush Brush)> Colour(string s)
    {
        var parts = new List<(int, int, IBrush)>();
        int i = 0;
        bool first = true;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == ',' || c == '(' || c == ')') { i++; continue; }
            if (c == '#' || c == ';' || (c == '/' && i + 1 < s.Length && s[i + 1] == '/')) { parts.Add((i, s.Length - i, Comment)); break; }
            if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < s.Length && s[j] != c) { if (s[j] == '\\') j++; j++; }
                j = Math.Min(s.Length, j + 1);
                parts.Add((i, j - i, Str));
                i = j; continue;
            }
            if (c == '%' && i + 1 < s.Length && char.IsLetter(s[i + 1]))
            {
                int j = i + 1;
                while (j < s.Length && char.IsLetter(s[j])) j++;
                parts.Add((i, j - i, Directive));
                i = j; continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1]) && !first))
            {
                int j = i + 1;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                bool label = j < s.Length && s[j] == ':';
                parts.Add((i, j - i + (label ? 1 : 0), label ? LabelDef : Number));
                i = j + (label ? 1 : 0); continue;
            }
            if (IdentChar(c))
            {
                int j = i;
                while (j < s.Length && IdentChar(s[j])) j++;
                string w = s.Substring(i, j - i);
                int k = j;
                while (k < s.Length && (s[k] == ' ' || s[k] == '\t')) k++;
                IBrush b;
                if (k < s.Length && s[k] == ':' && first) { b = LabelDef; j = k + 1; }
                else if (w.StartsWith(".")) b = Directive;
                else if (first) { b = Mnemonic; first = false; }
                else if (Assembler.IsRegister(w)) b = Register;
                else b = Plain;
                parts.Add((i, j - i, b));
                i = j; continue;
            }
            i++;
        }
        return parts;
    }

    sealed class Layer : Control
    {
        readonly AsmEditor ed;
        public Layer(AsmEditor e) { ed = e; }
        public override void Render(DrawingContext dc)
        {
            var lines = ed.Lines(this, out Rect vp);
            using (dc.PushClip(vp))
            {
                foreach (var (line, text, top, height, left) in lines)
                {
                    if (ed.IsError(line + 1)) dc.FillRectangle(ErrorBand, new Rect(vp.X, top, vp.Width, height));
                    if (text.Length == 0) continue;
                    var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, Size, Plain);
                    foreach (var p in Colour(text)) ft.SetForegroundBrush(p.Brush, p.Start, Math.Min(p.Length, text.Length - p.Start));
                    dc.DrawText(ft, new Point(left, top + (height - ft.Height) / 2));
                }
            }
        }
    }

    sealed class Gutter : Control
    {
        readonly AsmEditor ed;
        static readonly IBrush Num = Ui.Brush("#5B6778"), Err = Ui.Brush("#F87171"), Back = Ui.Brush("#131821");
        public Gutter(AsmEditor e) { ed = e; }
        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(Back, new Rect(Bounds.Size));
            foreach (var (line, _, top, height, _) in ed.Lines(this, out _))
            {
                bool err = ed.IsError(line + 1);
                var ft = new FormattedText((line + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, Size - 2, err ? Err : Num);
                dc.DrawText(ft, new Point(Bounds.Width - ft.Width - 8, top + (height - ft.Height) / 2));
                if (err) dc.DrawEllipse(Err, null, new Point(7, top + height / 2), 3.5, 3.5);
            }
        }
    }
}
