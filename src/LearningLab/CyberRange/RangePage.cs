// Cyber Range (Learning Lab) - the frame of every Range page (App Hub's RangeView chrome): the page, the status line under
// it, the rank chip in the top bar (a tap opens Badges and ranks), and the layer the glossary card is shown on. On
// Beginner pages the glossary's words are dotted (GlossText): a tap shows the word's card over the page (a card in the
// page's own layer, so pictures show it; tap outside it, Close or Back closes it). "Explain it simply" (RangeExplain)
// is a switch and a panel with the challenge in plain words and the words to know as 44 px buttons. No hover-only tips.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

public abstract class RangePage : IPage
{
    protected readonly RangeApp A;
    readonly Panel root = new();
    readonly DockPanel frame = new();
    readonly ContentControl body = new();
    readonly TextBlock status;
    readonly Border statusBar;
    readonly Canvas overlay = new() { IsVisible = false, Background = Brushes.Transparent };
    Border card;

    protected RangePage(RangeApp app)
    {
        A = app;
        status = Ui.Text("", 13, Ui.Muted);
        statusBar = new Border { Child = status, Padding = new Thickness(14, 6), Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(0, 1, 0, 0), IsVisible = false };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        frame.Children.Add(statusBar);
        frame.Children.Add(body);
        root.Children.Add(frame);
        root.Children.Add(overlay);
        root.Background = Ui.Brush(Ui.Bg);
        overlay.PointerPressed += (s, e) => { if (e.Source == overlay) { CloseTerm(); e.Handled = true; } };
        root.AttachedToVisualTree += (s, e) => { A.StatusChanged += OnStatus; };
        root.DetachedFromVisualTree += (s, e) => { A.StatusChanged -= OnStatus; };
    }

    void OnStatus(string s) { status.Text = s; statusBar.IsVisible = s.Length > 0; }
    public string StatusText => status.Text ?? "";

    public abstract string Title { get; }
    public Control View => root;
    protected Control Body { get => body.Content as Control; set => body.Content = value; }
    public virtual void Closed() { }
    public virtual bool OnBack() { if (TermShown) { CloseTerm(); return true; } return false; }

    /// <summary>The top bar's rank chip: the rank, the points and the streak; a tap opens Badges and ranks.</summary>
    public virtual Control Extras => ShowRankChip ? RankChip() : null;
    protected virtual bool ShowRankChip => true;
    Control RankChip()
    {
        int pts = RangeProgress.TotalPoints(A.Store);
        var rk = RangeProgress.RankFor(pts);
        int streak = RangeProgress.Streak(A.Store.Days, DateTime.Today);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(Rg.Insignia(rk.Index, 20, rk.Colour));
        sp.Children.Add(new TextBlock { Text = Rg.N(pts), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(rk.Colour), VerticalAlignment = VerticalAlignment.Center });
        if (streak > 0)
        {
            sp.Children.Add(Rg.Icon("flame", 14, "#FB923C", 2.2));
            sp.Children.Add(new TextBlock { Text = streak.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#FDBA74"), VerticalAlignment = VerticalAlignment.Center });
        }
        var b = new Button { Content = sp, MinHeight = 44, Padding = new Thickness(10, 4, 12, 4), CornerRadius = new CornerRadius(22), Background = Rg.Soft(rk.Colour, 0.1), BorderBrush = Rg.Soft(rk.Colour, 0.4), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(b, rk.Name + ", " + pts + " points. Badges and ranks");
        ToolTip.SetTip(b, rk.Name + " - " + Rg.Pts(pts) + ". Tap for badges and ranks.");
        b.Click += (s, e) => RangeUi.Safe(() => { if (!(MainView.Current.Page is RangeBadgesPage)) MainView.Current.Push(new RangeBadgesPage(A)); });
        return b;
    }

    // ------------------------------------------------------------ the glossary card
    public bool TermShown => overlay.IsVisible;
    public GlossTerm TermOnShow { get; private set; }

    /// <summary>Shows a word's card near a point of the page (below it, or above when there is no room).</summary>
    public void ShowTerm(GlossTerm t, Point at)
    {
        if (t == null) return;
        CloseTerm();
        TermOnShow = t;
        card = TermCard(t, CloseTerm);
        double w = root.Bounds.Width > 0 ? root.Bounds.Width : 1000, h = root.Bounds.Height > 0 ? root.Bounds.Height : 800;
        double cw = Math.Min(340, w - 32);
        card.Width = cw;
        card.Measure(new Size(cw, double.PositiveInfinity));
        double ch = card.DesiredSize.Height;
        double x = Math.Max(16, Math.Min(at.X - 18, w - cw - 16));
        double y = at.Y + 12;
        if (y + ch > h - 12) y = Math.Max(12, at.Y - ch - 30);
        Canvas.SetLeft(card, x); Canvas.SetTop(card, y);
        overlay.Children.Add(card);
        overlay.Width = w; overlay.Height = h;
        overlay.IsVisible = true;
        RangeFx.FadeIn(card, 140);
    }
    public void CloseTerm() { overlay.Children.Clear(); overlay.IsVisible = false; TermOnShow = null; card = null; }

    /// <summary>The card of a glossary word: the word, what it means, its other spellings, Close.</summary>
    public static Border TermCard(GlossTerm t, Action close)
    {
        var sp = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (close != null)
        {
            var x = RangeUi.Small("Close", close);
            x.Margin = new Thickness(8, -4, -6, 0);
            DockPanel.SetDock(x, Dock.Right);
            top.Children.Add(x);
        }
        var ic = Rg.Icon("book", 16, "#C4B5FD", 2); ic.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(ic, Dock.Left);
        top.Children.Add(ic);
        top.Children.Add(new TextBlock { Text = t.Term, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(top);
        sp.Children.Add(Rg.Sel(t.Text, 14, Ui.TextC));
        if (t.Names.Count > 1) { var also = Rg.Body("Also: " + string.Join(", ", t.Names.Skip(1).Take(5)), 12.5); also.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(also); }
        return new Border
        {
            Child = sp, Background = Rg.B("#1A1730"), BorderBrush = Rg.B("#5B4B99"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 13), BoxShadow = BoxShadows.Parse("0 4 24 0 #8C000000"),
        };
    }

    /// <summary>Text with the glossary's words dotted (Beginner pages; on: false = plain text). A tap on a word shows its card.</summary>
    public Control Glossed(string text, double size, string colour, bool on)
    {
        text = RangeLook.Unwrap(text ?? "");
        if (!on) return Rg.Sel(text, size, colour);
        return new GlossText(text, size, colour, (t, p) => ShowTerm(t, p), root);
    }

    /// <summary>The checks: taps the first dotted word on the page that is this term (or any, for null).</summary>
    public bool TapTerm(string term)
    {
        foreach (var g in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<GlossText>())
        {
            var h = g.Hits.FirstOrDefault(x => term == null || x.Term.Term == term);
            if (h != null) { g.Tap(h); return true; }
        }
        return false;
    }

    protected static MainView Mv => MainView.Current;
}

/// <summary>A paragraph with the glossary's words dotted in violet; a tap on one asks for its card (found from the
/// tapped character through the text's layout, so it works by touch, not only by hover).</summary>
public sealed class GlossText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);
    public readonly List<GlossHit> Hits;
    readonly Action<GlossTerm, Point> onTap;
    readonly Visual host;

    public GlossText(string text, double size, string colour, Action<GlossTerm, Point> tap, Visual hostRoot)
    {
        onTap = tap; host = hostRoot;
        FontSize = size; Foreground = Rg.B(colour); TextWrapping = TextWrapping.Wrap; LineHeight = Math.Round(size * 1.55);
        Hits = RangeGlossary.Matches(text);
        Inlines = new InlineCollection();
        int at = 0;
        var dotted = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline, Stroke = Rg.B("#A78BFA"), StrokeThickness = 1.3, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 1, 2 }, StrokeOffset = 2, StrokeOffsetUnit = TextDecorationUnit.Pixel } };
        foreach (var h in Hits)
        {
            if (h.Start > at) Inlines.Add(new Run(text.Substring(at, h.Start - at)));
            Inlines.Add(new Run(text.Substring(h.Start, h.Length)) { Foreground = Rg.B("#C4B5FD"), TextDecorations = dotted });
            at = h.Start + h.Length;
        }
        if (at < text.Length) Inlines.Add(new Run(text.Substring(at)));
        if (Hits.Count > 0) Cursor = new Cursor(StandardCursorType.Hand);
        PointerReleased += (s, e) =>
        {
            var p = e.GetPosition(this);
            var hit = HitAt(p);
            if (hit != null) { e.Handled = true; RangeUi.Safe(() => onTap(hit.Term, this.TranslatePoint(p, host) ?? p)); }
        };
    }

    GlossHit HitAt(Point p)
    {
        if (Hits.Count == 0) return null;
        try
        {
            var r = TextLayout.HitTestPoint(new Point(p.X - Padding.Left, p.Y - Padding.Top));
            if (!r.IsInside) return null;
            int i = r.TextPosition;
            return Hits.FirstOrDefault(h => i >= h.Start && i < h.Start + h.Length);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The checks: as if the word was tapped.</summary>
    public void Tap(GlossHit h)
    {
        Point p = new(0, Bounds.Height);
        try { var rect = TextLayout.HitTestTextPosition(h.Start); p = new Point(rect.X, rect.Bottom); } catch (Exception) { }
        onTap(h.Term, this.TranslatePoint(p, host) ?? p);
    }
}

/// <summary>"Explain it simply": a switch and the panel it opens (the challenge in plain words, the words to know).</summary>
public static class RangeExplain
{
    public static Control Toggle(RangePage page, RangeApp v, RangeCatStyle st, string simple, params string[] texts)
    {
        var host = new StackPanel();
        var sw = new RangeSwitch("Explain it simply", "bulb", "#2DD4BF", v.ExplainOn) { Margin = new Thickness(0, 0, 0, 8) };
        var panel = Panel(page, st, simple, texts);
        panel.IsVisible = v.ExplainOn;
        sw.Changed += on => { v.ExplainOn = on; panel.IsVisible = on; };
        host.Children.Add(sw);
        host.Children.Add(panel);
        return host;
    }
    public static Control Panel(RangePage page, RangeCatStyle st, string simple, params string[] texts)
    {
        var sp = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(Rg.Icon("bulb", 16, "#5EEAD4", 2.1));
        top.Children.Add(new TextBlock { Text = "In plain words", FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#5EEAD4"), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(top);
        sp.Children.Add(Rg.Body(string.IsNullOrEmpty(simple) ? st.Simple : simple, 14.5, Ui.TextC));
        var terms = RangeGlossary.TermsIn(texts);
        if (terms.Count > 0)
        {
            var h = Rg.Body("Words to know (tap one)", 12.5); h.FontWeight = FontWeight.SemiBold; h.Margin = new Thickness(0, 12, 0, 6);
            sp.Children.Add(h);
            var wp = new WrapPanel();
            foreach (var t0 in terms.Take(14))
            {
                var t = t0;
                var chip = new Button
                {
                    Content = t.Term, FontSize = 13.5, MinHeight = 44, Padding = new Thickness(12, 4), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(22),
                    Background = Rg.Soft("#A78BFA", 0.13), BorderBrush = Rg.Soft("#A78BFA", 0.35), BorderThickness = new Thickness(1), Foreground = Rg.B("#DDD6FE"), VerticalContentAlignment = VerticalAlignment.Center,
                };
                chip.Resources["ButtonForegroundPointerOver"] = Rg.B("#EDE9FE");
                chip.Click += (s, e) => RangeUi.Safe(() =>
                {
                    var p = chip.TranslatePoint(new Point(0, chip.Bounds.Height), page.View) ?? new Point(40, 120);
                    page.ShowTerm(t, p);
                });
                wp.Children.Add(chip);
            }
            sp.Children.Add(wp);
        }
        return Rg.Callout(sp, "#2DD4BF");
    }
}
