// Cyber Range (Learning Lab) - the pages before a run (App Hub's UiRange.cs, UiRange2.cs's briefing, UiRangeTour.cs):
// home ("operations console": a hero with rank, points, streak and badges, a Continue card from RangeProgress.Next,
// one card per category with its drawn icon, ring and per-level bars, all from RangeLook so categories added later look
// right), a category's four levels as a path (never locked), a level's challenge cards with chips, the briefing (with
// "Explain it simply" and tappable glossary words on Beginner), the New here? tour with the ethics note, Badges and
// ranks, and the Glossary. Two columns become one on a phone; side parts go above or below the main part.
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

// ------------------------------------------------------------ home
public sealed class RangeHomePage : RangePage
{
    public override string Title => "Cyber Range";
    public string Offer { get; private set; } = "";

    public RangeHomePage() : this(RangeApp.Open(false)) { }
    public RangeHomePage(RangeApp app) : base(app) { Build(); }
    public void Refresh() { Build(); Mv?.RefreshBar(); }

    void Build()
    {
        var root = new StackPanel();
        Offer = A.ResumeOffer();
        if (Offer.Length > 0)
        {
            var sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(Rg.Title("Carry on where you left off?", 17));
            sp.Children.Add(Rg.Body(Offer, 14, Ui.TextC));
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(RangeUi.Primary("Carry on where you left off", () => A.ResumeNow()));
            row.Children.Add(RangeUi.Secondary("Not now", () => { A.DismissResume(); Build(); }));
            sp.Children.Add(row);
            var pane = Rg.Panel(sp, 16);
            pane.BorderBrush = Ui.Brush(Ui.Accent);
            root.Children.Add(pane);
        }
        root.Children.Add(Hero());

        var row2 = new FlowGrid { MinCell = 460, MaxCols = 2 };
        row2.Children.Add(ContinueCard());
        row2.Children.Add(BadgeStrip());
        root.Children.Add(row2);

        int all = A.Items.Count, done = A.Items.Count(A.IsDone);
        root.Children.Add(Rg.SectionHead("Categories", "Choose your mission", done + " of " + all + " challenges done"));
        var cards = new FlowGrid { MinCell = 460, MaxCols = 2 };
        foreach (var c in RangeData.Categories)
        {
            int d = A.DoneInCategory(c.Id, out int total);
            if (total == 0) continue;
            cards.Children.Add(CategoryCard(c, d, total));
        }
        root.Children.Add(cards);
        var eth = Rg.Body("Only ever test computers you own or have clear permission to test. Everything in Cyber Range is pretend, made for practising safely.", 13, Ui.Dim);
        eth.Margin = new Thickness(2, 4, 0, 0);
        root.Children.Add(eth);
        Body = RangeUi.Page(root, 1240);
    }

    Control Hero()
    {
        var s = A.Store;
        int pts = RangeProgress.TotalPoints(s);
        var rk = RangeProgress.RankFor(pts); var next = RangeProgress.NextRank(pts);
        int streak = RangeProgress.Streak(s.Days, DateTime.Today), best = RangeProgress.BestStreak(s.Days);

        var g = new Grid();
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Rg.Gradient("#141A2A", "#0E121B", 20) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Rg.Glow("#6366F1", 70, 0.85, 0.15, 0.7) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), BorderBrush = Rg.Soft("#818CF8", 0.28), BorderThickness = new Thickness(1) });

        var inner = new StackPanel { Margin = new Thickness(20, 20, 20, 16) };
        var split = new RangeSplit { Side = 300, StackBelow = 760, SideFirst = false, Gap = 24 };
        var left = new StackPanel();
        left.Children.Add(Rg.Eyebrow("Capture the flag practice", "#A5B4FC"));
        left.Children.Add(Rg.Title("Break in. Defend. Solve.", 30));
        var intro = Rg.Body("Hands-on security challenges on pretend networks. Nothing here is real, so you can't break anything - but the skills you practise are.", 14.5);
        intro.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(intro);
        var btns = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var nx = A.Next(out bool resuming);
        if (nx != null) btns.Children.Add(RangeUi.Primary(resuming ? "Continue: " + nx.Title : A.Items.Any(A.IsTried) ? "Next: " + nx.Title : "Start your first challenge", () => A.Open(nx)));
        btns.Children.Add(RangeUi.Secondary(s.TourSeen ? "How it works" : "New here? Take the tour", () => Mv.Push(new RangeTourPage(A))));
        btns.Children.Add(RangeUi.Secondary("Glossary", () => Mv.Push(new RangeGlossaryPage(A))));
        left.Children.Add(btns);
        split.Children.Add(left);

        // the rank block (a tap opens badges and ranks)
        var rb = new StackPanel();
        var rtop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        rtop.Children.Add(Rg.Insignia(rk.Index, 46, rk.Colour));
        var rname = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        rname.Children.Add(Rg.Eyebrow("Your rank"));
        rname.Children.Add(new TextBlock { Text = rk.Name, FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(rk.Colour) });
        rtop.Children.Add(rname);
        rb.Children.Add(rtop);
        rb.Children.Add(new TextBlock { Text = Rg.N(pts) + " points", FontSize = 14, Foreground = Rg.B(Ui.TextC), Margin = new Thickness(0, 10, 0, 6) });
        rb.Children.Add(Rg.Bar(RangeProgress.RankShare(pts), Rg.Gradient(rk.Colour, next != null ? next.Colour : rk.Colour, 0), 7));
        var nt = Rg.Body(next != null ? Rg.N(next.Min - pts) + " points to " + next.Name : "Top rank. Every challenge done adds to your total.", 12.5);
        nt.Margin = new Thickness(0, 6, 0, 0);
        rb.Children.Add(nt);
        var rbCard = new RangeCard(rk.Colour, rb, () => Mv.Push(new RangeBadgesPage(A)), 14) { Background = Rg.Soft("#000000", 0.22) };
        Avalonia.Automation.AutomationProperties.SetName(rbCard, "Your rank: see the ranks and badges");
        split.Children.Add(rbCard);
        inner.Children.Add(split);

        var stats = new FlowGrid { MinCell = 150, MaxCols = 4, Gap = 10 };
        int all = A.Items.Count, done = A.Items.Count(A.IsDone);
        stats.Children.Add(RangeUi.Stat("trophy", "#34D399", done + " / " + all, "challenges done"));
        stats.Children.Add(RangeUi.Stat("flag", "#FB7185", RangeProgress.FlagsCaptured(s).ToString(CultureInfo.InvariantCulture), "flags and answers captured"));
        stats.Children.Add(RangeUi.Stat("flame", "#FB923C", streak + (streak == 1 ? " day" : " days"), best > streak ? "streak (best " + best + ")" : "streak of days played"));
        stats.Children.Add(RangeUi.Stat("star", "#FBBF24", s.Badges.Count + " / " + RangeProgress.Badges.Length, "badges earned"));
        inner.Children.Add(new Border { Child = stats, BorderBrush = Rg.Soft("#FFFFFF", 0.07), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 16, 0, 0), Margin = new Thickness(0, 18, 0, 0) });
        g.Children.Add(inner);
        return new Border { Child = g, Margin = new Thickness(0, 4, 0, 16) };
    }

    Control ContinueCard()
    {
        var it = A.Next(out bool resuming);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (it == null)
        {
            g.Children.Add(Rg.IconTile("trophy", "#34D399", "#14B8A6", 50));
            var sp0 = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp0.Children.Add(Rg.Eyebrow("All done", "#6EE7B7"));
            sp0.Children.Add(Rg.Title("You have finished every challenge.", 16));
            sp0.Children.Add(Rg.Body("Play any again for a faster time or a no-hints run.", 13));
            Grid.SetColumn(sp0, 1); g.Children.Add(sp0);
            var c0 = new RangeCard("#34D399", g, () => Mv.Push(new RangeBadgesPage(A))) { Margin = new Thickness(0, 0, 0, 12) };
            return c0;
        }
        var st = RangeLook.ForId(it.Category);
        g.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 50));
        var sp = new StackPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Rg.Eyebrow(resuming ? "Continue where you left off" : A.Items.Any(A.IsTried) ? "Up next" : "Start here", st.Accent));
        sp.Children.Add(new TextBlock { Text = it.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        chips.Children.Add(Rg.LevelChip(it.Level));
        chips.Children.Add(Rg.Chip(RangeApp.CatTitle(it.Category), st.Accent, 0.12));
        chips.Children.Add(Rg.Tag("clock", "~" + it.Minutes + " min"));
        var res = A.Store.For(it.Puzzle, it.Id);
        if (res.Attempts > 0) chips.Children.Add(Rg.Tag(null, "best " + res.BestScore + " of " + it.Points));
        sp.Children.Add(chips);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        var go = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Rg.Soft(st.Accent, 0.16), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "→", FontSize = 18, Foreground = Rg.B(st.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        Grid.SetColumn(go, 2); g.Children.Add(go);
        var card = new RangeCard(st.Accent, g, () => A.Open(it)) { Margin = new Thickness(0, 0, 0, 12) };
        Avalonia.Automation.AutomationProperties.SetName(card, "Continue: " + it.Title);
        return card;
    }

    Control BadgeStrip()
    {
        var sp = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var cnt = new TextBlock { Text = A.Store.Badges.Count + " of " + RangeProgress.Badges.Length + "  →", FontSize = 13, Foreground = Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(cnt, Dock.Right); top.Children.Add(cnt);
        top.Children.Add(Rg.Eyebrow("Badges", "#FCD34D"));
        sp.Children.Add(top);
        var g = new FlowGrid { MinCell = 38, MaxCols = 8, Gap = 6, RowGap = 6 };
        foreach (var b in RangeProgress.Badges)
        {
            bool got = A.Store.Badges.ContainsKey(b.Id);
            var m = Rg.Medal(b.Icon, b.Colour, got, 38);
            m.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(m, b.Name + (got ? " (earned)" : "") + "\n" + b.How);
            g.Children.Add(m);
        }
        sp.Children.Add(g);
        var nb = RangeProgress.Badges.FirstOrDefault(b => !A.Store.Badges.ContainsKey(b.Id));
        var t = Rg.Body(nb != null ? "Next to aim for: " + nb.Name + " - " + nb.How : "Every badge earned. Impressive.", 12.5);
        t.Margin = new Thickness(0, 9, 0, 0);
        sp.Children.Add(t);
        var card = new RangeCard("#FBBF24", sp, () => Mv.Push(new RangeBadgesPage(A))) { Margin = new Thickness(0, 0, 0, 12) };
        Avalonia.Automation.AutomationProperties.SetName(card, "Badges");
        return card;
    }

    Control CategoryCard(RangeCategory c, int done, int total)
    {
        var st = RangeLook.For(c);
        var layers = new Grid();
        layers.Children.Add(new Border { Background = Rg.Glow(st.Accent, 26, 0.05, 0, 0.9), Margin = new Thickness(-18) });
        layers.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Background = Rg.Gradient(st.Accent, st.Accent2, 0), Opacity = 0.9, Margin = new Thickness(-18, -18, -18, 0) });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        g.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 54));
        var col = new StackPanel { Margin = new Thickness(14, 0, 10, 0) };
        col.Children.Add(new TextBlock { Text = c.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
        var line = Rg.Body(c.Line, 13.5); line.Margin = new Thickness(0, 3, 0, 0);
        col.Children.Add(line);
        var lv = new FlowGrid { MinCell = 76, MaxCols = 4, Gap = 8, RowGap = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var level in RangeData.Levels)
        {
            int ld = A.DoneInLevel(c.Id, level, out int lt);
            if (lt == 0) continue;
            var cell = new StackPanel();
            cell.Children.Add(Rg.Bar((double)ld / lt, RangeLook.Level(level).Color, 4));
            var lab = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var n = new TextBlock { Text = ld + "/" + lt, FontSize = 11.5, Foreground = Rg.B(Ui.Muted) };
            DockPanel.SetDock(n, Dock.Right); lab.Children.Add(n);
            lab.Children.Add(new TextBlock { Text = level, FontSize = 11.5, Foreground = Rg.B(Ui.Dim), TextTrimming = TextTrimming.CharacterEllipsis });
            cell.Children.Add(lab);
            lv.Children.Add(cell);
        }
        col.Children.Add(lv);
        Grid.SetColumn(col, 1); g.Children.Add(col);
        var ring = Rg.Ring(total == 0 ? 0 : done / (double)total, st.Accent, 62, done + "/" + total, "done");
        Grid.SetColumn(ring, 2); g.Children.Add(ring);
        layers.Children.Add(g);
        var card = new RangeCard(st.Accent, layers, () => Mv.Push(new RangeCategoryPage(A, c)), 18) { Margin = new Thickness(0, 0, 0, 14) };
        Avalonia.Automation.AutomationProperties.SetName(card, c.Title);
        card.Tag = "category:" + c.Id;
        return card;
    }
}

// ------------------------------------------------------------ a category's levels, as a path
public sealed class RangeCategoryPage : RangePage
{
    public readonly RangeCategory Cat;
    public override string Title => Cat.Title;
    public RangeCategoryPage(RangeApp app, RangeCategory c) : base(app)
    {
        Cat = c;
        A.Store.LastCategory = c.Id;
        var st = RangeLook.For(c);
        var root = new StackPanel();
        int done = A.DoneInCategory(c.Id, out int total);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 14) };
        head.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 60));
        var hc = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        hc.Children.Add(Rg.Eyebrow("Category", st.Accent));
        hc.Children.Add(Rg.Title(c.Title, 26));
        var l = Rg.Body(c.Line, 14); l.Margin = new Thickness(0, 4, 0, 0);
        hc.Children.Add(l);
        Grid.SetColumn(hc, 1); head.Children.Add(hc);
        var ring = Rg.Ring(total == 0 ? 0 : done / (double)total, st.Accent, 72, done + "/" + total, "done");
        Grid.SetColumn(ring, 2); head.Children.Add(ring);
        root.Children.Add(head);

        var plain = new StackPanel();
        plain.Children.Add(Rg.Eyebrow("In plain words", st.Accent));
        plain.Children.Add(Rg.Body(st.Simple, 14, Ui.TextC));
        root.Children.Add(Rg.Callout(plain, st.Accent));

        root.Children.Add(Rg.SectionHead("Your path", "Pick a level", "Every level is open: start wherever you like"));
        int firstOpen = -1;
        for (int i = 0; i < RangeData.Levels.Length; i++) { int d = A.DoneInLevel(c.Id, RangeData.Levels[i], out int t); if (t > 0 && d < t) { firstOpen = i; break; } }
        var shown = Enumerable.Range(0, RangeData.Levels.Length).Where(i => { A.DoneInLevel(c.Id, RangeData.Levels[i], out int t); return t > 0; }).ToList();
        for (int k = 0; k < shown.Count; k++) root.Children.Add(Step(st, shown[k], k == 0, k == shown.Count - 1, shown[k] == firstOpen));
        Body = RangeUi.Page(root, 1040);
    }

    Control Step(RangeCatStyle st, int i, bool first, bool last, bool recommended)
    {
        string level = RangeData.Levels[i];
        var ls = RangeLook.Levels[i];
        var items = A.ItemsIn(Cat.Id, level).ToList();
        int total = items.Count, done = items.Count(A.IsDone);
        bool started = items.Any(A.IsTried), complete = total > 0 && done == total;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*") };
        if (!(first && last))
        {
            var line = new Rectangle { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Fill = started ? Rg.Soft(ls.Color, 0.45) : Rg.B(Ui.BorderC), Margin = new Thickness(0, first ? 30 : 0, 0, 0) };
            if (last) { line.VerticalAlignment = VerticalAlignment.Top; line.Height = 30; }
            row.Children.Add(line);
        }
        var node = new Grid { Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
        string num = (i + 1).ToString(CultureInfo.InvariantCulture);
        if (started)
        {
            node.Children.Add(new Ellipse { Fill = complete ? Rg.B(ls.Color) : Rg.B(Ui.Bg), Stroke = Rg.B(ls.Color), StrokeThickness = 2 });
            if (complete) { var ck = Rg.Icon("check", 18, "#0B0D12", 3.2); ck.HorizontalAlignment = HorizontalAlignment.Center; node.Children.Add(ck); }
            else node.Children.Add(new TextBlock { Text = num, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(ls.Color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            node.Children.Add(new Ellipse { Fill = Rg.B(Ui.Bg), Stroke = Rg.B(Ui.BorderStrong), StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 2.5, 2 } });
            node.Children.Add(new TextBlock { Text = num, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        row.Children.Add(node);

        var split = new RangeSplit { Side = 170, StackBelow = 540, SideFirst = false, Gap = 16 };
        var sp = new StackPanel();
        var t = new WrapPanel();
        t.Children.Add(new TextBlock { Text = level, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = started ? Rg.B(ls.Color) : Rg.B(Ui.Dim), Margin = new Thickness(0, 0, 10, 4), VerticalAlignment = VerticalAlignment.Center });
        if (complete) t.Children.Add(Rg.Chip("Cleared", "#34D399"));
        else if (recommended) t.Children.Add(Rg.Chip(started ? "Keep going" : "Recommended next", "#93C5FD"));
        else if (started) t.Children.Add(Rg.Chip("In progress", "#FBBF24", 0.12));
        sp.Children.Add(t);
        var purpose = Rg.Body(Cat.LevelLines.Length > i && !string.IsNullOrEmpty(Cat.LevelLines[i]) ? Cat.LevelLines[i] : ls.Purpose, 14, started ? Ui.TextC : Ui.Muted);
        purpose.Margin = new Thickness(0, 2, 0, 6);
        sp.Children.Add(purpose);
        var tags = new WrapPanel();
        int lo = items.Count > 0 ? items.Min(x => x.Minutes) : 0, hi = items.Count > 0 ? items.Max(x => x.Minutes) : 0;
        tags.Children.Add(Rg.Tag(null, total + (total == 1 ? " challenge" : " challenges")));
        tags.Children.Add(Rg.Tag("clock", lo == hi ? "~" + lo + " min" : lo + "-" + hi + " min"));
        tags.Children.Add(Rg.Tag("star", Rg.N(items.Sum(x => x.Points)) + " pts"));
        if (!started) tags.Children.Add(Rg.Tag("unlock", "Not started - open any time"));
        sp.Children.Add(tags);
        split.Children.Add(sp);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = done + " of " + total + " done", FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = complete ? Rg.B("#6EE7B7") : Rg.B(Ui.TextC), Margin = new Thickness(0, 0, 0, 7) });
        right.Children.Add(Rg.Bar(total == 0 ? 0 : done / (double)total, ls.Color, 6));
        var kid = Rg.Body(ls.Kid, 12.5); kid.Margin = new Thickness(0, 7, 0, 0);
        right.Children.Add(kid);
        split.Children.Add(right);
        var card = new RangeCard(ls.Color, split, () => Mv.Push(new RangeLevelPage(A, Cat, level))) { Margin = new Thickness(4, 0, 0, 14) };
        if (!started) card.Background = Rg.Soft("#FFFFFF", 0.012);
        Avalonia.Automation.AutomationProperties.SetName(card, level);
        card.Tag = "level:" + level;
        Grid.SetColumn(card, 1);
        row.Children.Add(card);
        return row;
    }
}

// ------------------------------------------------------------ a level's challenges
public sealed class RangeLevelPage : RangePage
{
    public readonly RangeCategory Cat;
    public readonly string Level;
    public override string Title => Cat.Title + ": " + Level;
    public RangeLevelPage(RangeApp app, RangeCategory c, string level) : base(app)
    {
        Cat = c; Level = level;
        A.Store.LastCategory = c.Id; A.Store.LastLevel = level;
        Build();
    }
    public void Refresh() => Build();

    void Build()
    {
        var c = Cat; var level = Level;
        var st = RangeLook.For(c);
        var ls = RangeLook.Level(level);
        int idx = RangeData.LevelIndex(level);
        var items = A.ItemsIn(c.Id, level).OrderBy(i => i.Order).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        int done = items.Count(A.IsDone);
        var root = new StackPanel();

        var split = new RangeSplit { Side = 200, StackBelow = 680, SideFirst = false, Gap = 20 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 54));
        var hc = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var tl = new WrapPanel();
        var tt = Rg.Title(c.Title, 24); tt.Margin = new Thickness(0, 0, 12, 0);
        tl.Children.Add(tt);
        tl.Children.Add(Rg.LevelChip(level));
        hc.Children.Add(tl);
        var pl = Rg.Body(c.LevelLines.Length > idx && !string.IsNullOrEmpty(c.LevelLines[idx]) ? c.LevelLines[idx] : ls.Purpose, 14); pl.Margin = new Thickness(0, 4, 0, 0);
        hc.Children.Add(pl);
        Grid.SetColumn(hc, 1); head.Children.Add(hc);
        split.Children.Add(head);
        var pr = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pr.Children.Add(new TextBlock { Text = done + " of " + items.Count + " done", FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), Margin = new Thickness(0, 0, 0, 7) });
        pr.Children.Add(Rg.Bar(items.Count == 0 ? 0 : done / (double)items.Count, ls.Color, 6));
        split.Children.Add(pr);
        split.Margin = new Thickness(0, 4, 0, 16);
        root.Children.Add(split);

        if (items.Count == 0) root.Children.Add(Rg.Body("No challenges here yet."));
        var groups = items.GroupBy(i => i.Puzzle ? i.Topic : "").OrderBy(gr => { int k = Array.IndexOf(RangeData.TopicsOf(c.Id), gr.Key); return k < 0 ? 99 : k; }).ToList();
        foreach (var grp in groups)
        {
            if (grp.Key.Length > 0)
            {
                var gh = new DockPanel { Margin = new Thickness(0, 10, 0, 9) };
                var cnt = new TextBlock { Text = grp.Count(A.IsDone) + " / " + grp.Count(), FontSize = 12.5, Foreground = Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(cnt, Dock.Right); gh.Children.Add(cnt);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = Rg.B(st.Accent), Margin = new Thickness(1, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(dot, Dock.Left); gh.Children.Add(dot);
                gh.Children.Add(new TextBlock { Text = grp.Key, FontSize = 15.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center });
                root.Children.Add(gh);
            }
            var cards = new FlowGrid { MinCell = 460, MaxCols = 2 };
            foreach (var it in grp) cards.Children.Add(Card(it, st));
            root.Children.Add(cards);
        }
        Body = RangeUi.Page(root, 1240);
    }

    Control Card(RangeItem it, RangeCatStyle st)
    {
        bool done = A.IsDone(it);
        var layers = new Grid();
        layers.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = done ? Rg.B("#34D399") : Rg.Gradient(st.Accent, st.Accent2, 90), Opacity = done ? 0.9 : 0.75, Margin = new Thickness(-16, -14, 0, -14) });
        var sp = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var stp = RangeUi.Status(A, it); stp.Margin = new Thickness(10, 1, 0, 0);
        DockPanel.SetDock(stp, Dock.Right); top.Children.Add(stp);
        top.Children.Add(new TextBlock { Text = it.Title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(top);
        var teaser = Rg.Body(it.Teaser, 13.5); teaser.MaxLines = 3; teaser.TextTrimming = TextTrimming.WordEllipsis; teaser.Margin = new Thickness(0, 0, 0, 9);
        sp.Children.Add(teaser);
        var chips = new WrapPanel();
        chips.Children.Add(Rg.LevelChip(it.Level));
        if (it.Topic.Length > 0) chips.Children.Add(Rg.Chip(it.Topic, "#CBD5E1", 0.08));
        chips.Children.Add(Rg.Tag("star", it.Points + " pts"));
        chips.Children.Add(Rg.Tag("clock", "~" + it.Minutes + " min"));
        bool blue = RangeData.BlueLike(it.Category);
        if (!it.Puzzle) chips.Children.Add(Rg.Tag(blue ? "search" : "flag", it.Objectives + (blue ? " findings" : it.Objectives == 1 ? " flag" : " flags")));
        sp.Children.Add(chips);
        layers.Children.Add(sp);
        var card = new RangeCard(st.Accent, layers, () => A.Open(it), 0) { Padding = new Thickness(16, 14, 14, 12), Margin = new Thickness(0, 0, 0, 12) };
        Avalonia.Automation.AutomationProperties.SetName(card, it.Title);
        card.Tag = "item:" + it.Id;
        return card;
    }
}

// ------------------------------------------------------------ the briefing
public sealed class RangeBriefingPage : RangePage
{
    public readonly RangeScenario Sc;
    public override string Title => Sc.Title;
    public RangeBriefingPage(RangeApp app, RangeScenario s) : base(app)
    {
        Sc = s;
        var st = RangeLook.ForId(s.Category);
        var it = RangeItem.Of(s);
        bool beginner = RangeData.LevelIndex(s.Level) == 0;
        var root = new StackPanel();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 16) };
        head.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 60));
        var hc = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        hc.Children.Add(Rg.Eyebrow(RangeApp.CatTitle(s.Category) + "  /  " + s.Level, st.Accent));
        hc.Children.Add(Rg.Title(s.Title, 28));
        var te = Rg.Body(s.Teaser, 15); te.Margin = new Thickness(0, 6, 0, 0);
        hc.Children.Add(te);
        Grid.SetColumn(hc, 1); head.Children.Add(hc);
        root.Children.Add(head);

        var split = new RangeSplit { Side = 320, StackBelow = 880, SideFirst = true };
        var main = new StackPanel();
        if (s.Category == "defender") main.Children.Add(Rg.Banner("eye", "A defender is watching", s.DefenderRules != null && s.DefenderRules.Watches.Length > 0
            ? "This one looks out for " + s.DefenderRules.Watches + ". Do too much of that and you get noticed and blocked. Quiet, patient work scores the bonus."
            : "Noisy work - scans, failed sign-ins, loud guessing - gets noticed and blocked. Quiet, patient work scores the bonus.", "#FBBF24"));
        if (s.Category == "soc") main.Children.Add(Rg.Banner("bell", "You are the analyst on shift", "Everything already happened: read the alerts and logs in the SIEM (type siem in the console), then answer the questions in the Findings tab.", "#FB923C"));
        else if (RangeData.BlueLike(s.Category)) main.Children.Add(Rg.Banner("shield", "You are defending", "A simulated attacker is (or has been) at work. Read the logs, answer the questions, and make the fixes before time runs out.", "#38BDF8"));
        if (beginner) main.Children.Add(RangeExplain.Toggle(this, A, st, s.Simple, s.Story, s.Goal, s.Summary, s.Skills));
        main.Children.Add(Pane("book", "The story", s.Story, beginner));
        main.Children.Add(Pane("flag", "Your goal", s.Goal, beginner));
        if (s.Skills.Length > 0)
        {
            var sk = new StackPanel();
            sk.Children.Add(Rg.PaneHead("bolt", "Skills you will use"));
            var wp = new WrapPanel();
            foreach (var part in s.Skills.Split(',')) { var t = part.Trim(); if (t.Length > 0) wp.Children.Add(Rg.Chip(t, "#CBD5E1", 0.07)); }
            sk.Children.Add(wp);
            main.Children.Add(Rg.Panel(sk, 18));
        }
        split.Children.Add(main);
        split.Children.Add(Side(s, it, st));
        root.Children.Add(split);
        Body = RangeUi.Page(root, 1180);
    }

    Control Pane(string icon, string title, string body, bool glossed)
    {
        var sp = new StackPanel();
        sp.Children.Add(Rg.PaneHead(icon, title));
        sp.Children.Add(Glossed(body, 15, Ui.TextC, glossed));
        return Rg.Panel(sp, 18);
    }

    Control Side(RangeScenario s, RangeItem it, RangeCatStyle st)
    {
        var sp = new StackPanel();
        sp.Children.Add(Rg.Eyebrow("Mission", st.Accent));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 4, 0, 12) };
        bool blue = RangeData.BlueLike(s.Category);
        void Fact(int r, int c, string icon, string value, string label)
        {
            var f = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(0, 4) };
            f.Children.Add(Rg.Icon(icon, 15, Ui.Muted, 2.1));
            f.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center });
            f.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(f, r); Grid.SetColumn(f, c); grid.Children.Add(f);
        }
        Fact(0, 0, "star", Rg.N(s.MaxPoints), "points");
        Fact(0, 1, blue ? "search" : "flag", s.ObjectiveCount.ToString(CultureInfo.InvariantCulture), blue ? "findings" : s.ObjectiveCount == 1 ? "flag" : "flags");
        Fact(1, 0, "clock", "~" + s.Minutes, "minutes");
        Fact(1, 1, "server", s.Machines.Count.ToString(CultureInfo.InvariantCulture), s.Machines.Count == 1 ? "machine" : "machines");
        sp.Children.Add(grid);
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        chips.Children.Add(Rg.LevelChip(s.Level));
        if (it.Topic.Length > 0) chips.Children.Add(Rg.Chip(it.Topic, "#CBD5E1", 0.08));
        sp.Children.Add(chips);
        var res = A.Store.For(false, s.Id);
        if (res.Attempts > 0)
        {
            var rec = res.Completed ? "Finished. Best score " + res.BestScore + " of " + s.MaxPoints + ", best time " + RangeRun.TimeText(res.BestSeconds) + "." : "Tried " + res.Attempts + (res.Attempts == 1 ? " time" : " times") + ". Best so far: " + res.BestScore + " of " + s.MaxPoints + ".";
            sp.Children.Add(new Border { Background = Rg.Soft(res.Completed ? "#34D399" : "#FBBF24", 0.09), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 12), Child = Rg.Body(rec, 13, res.Completed ? "#6EE7B7" : "#FCD34D") });
        }
        var start = RangeUi.Primary(res.Attempts > 0 ? "Start again" : "Start the mission", () => A.StartScenario(s));
        start.HorizontalAlignment = HorizontalAlignment.Stretch; start.Margin = new Thickness(0, 0, 0, 8);
        sp.Children.Add(start);
        var note = Rg.Body("Names, passwords, addresses and flags change every run. Only test systems you own or are allowed to test: this range is all pretend.", 12.5);
        note.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(note);
        var p = Rg.Panel(sp, 18);
        p.BorderBrush = Rg.Soft(st.Accent, 0.35);
        return p;
    }
}

// ------------------------------------------------------------ the tour
public sealed class RangeTourPage : RangePage
{
    public override string Title => "New here?";
    public RangeTourPage(RangeApp app) : base(app)
    {
        if (!A.Store.TourSeen) { A.Store.TourSeen = true; A.Store.Save(); }
        var root = new StackPanel();
        root.Children.Add(Rg.Eyebrow("New here? Start here", "#A5B4FC"));
        root.Children.Add(Rg.Title("How Cyber Range works", 26));
        var intro = Rg.Body("A one-minute tour. You play on pretend computers inside this app: there is no real network, nothing to download, and nothing you can break.", 15);
        intro.Margin = new Thickness(0, 6, 0, 16);
        root.Children.Add(intro);
        var steps = new FlowGrid { MinCell = 290, MaxCols = 3 };
        steps.Children.Add(Step(1, "terminal", "#4ADE80", "You get your own computer", "Each challenge gives you a pretend computer you control by typing commands in a terminal. Type ls to list files or cat to read one."));
        steps.Children.Add(Step(2, "network", "#38BDF8", "Look around first", "Explorers call this recon: find which other computers are on the network and which doors (ports) they have open. nmap is the tool for it."));
        steps.Children.Add(Step(3, "door", "#FBBF24", "Find a way in", "Look for a mistake: a weak password, a file left where anyone can read it, a careless setting. Then look for a way to get more power."));
        steps.Children.Add(Step(4, "flag", "#FB7185", "Capture the flag", "A flag is a secret like FLAG{...} hidden where only the right person should reach. Copy it into the answer box to score."));
        steps.Children.Add(Step(5, "shield", "#A78BFA", "Or be the defender", "In Blue Team challenges you read the logs (the computer's diary), work out what an attacker did, and clean it up."));
        steps.Children.Add(Step(6, "bulb", "#2DD4BF", "Stuck? Ask for a hint", "Hints stay hidden until you ask. Each one costs a few points; on Beginner, Learning mode makes them free. Tap a dotted word to learn what it means."));
        root.Children.Add(steps);

        var pr = new StackPanel();
        pr.Children.Add(Rg.Eyebrow("Points, ranks and badges", "#FCD34D"));
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var rk in RangeProgress.Ranks)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 18, 6) };
            sp.Children.Add(Rg.Insignia(rk.Index, 22, rk.Colour));
            sp.Children.Add(new TextBlock { Text = rk.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(rk.Colour), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = Rg.N(rk.Min) + "+", FontSize = 12.5, Foreground = Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(sp);
        }
        pr.Children.Add(row);
        pr.Children.Add(Rg.Body("Your best score in each challenge adds up to your rank. Badges mark milestones: your first flag, a win without hints, three days in a row, and more. Every challenge changes a little each run (names, passwords, flags), so answers can't just be copied.", 14, Ui.TextC));
        root.Children.Add(Rg.Panel(pr, 18));

        var eth = new StackPanel();
        eth.Children.Add(Rg.Eyebrow("The one rule", "#FCD34D"));
        eth.Children.Add(Rg.Body("Only ever test computers you own or have clear permission to test. Trying these techniques on other people's computers or networks is against the law in most places. Cyber Range is all pretend, made so you can practise safely.", 14, "#FDE68A"));
        root.Children.Add(Rg.Callout(eth, "#FBBF24"));

        var btns = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var first = A.Next(out bool resuming);
        if (first != null) btns.Children.Add(RangeUi.Primary(resuming ? "Continue: " + first.Title : "Try my first challenge", () => A.Open(first)));
        btns.Children.Add(RangeUi.Secondary("Show all the challenges", () => Mv.BackTo<RangeHomePage>()));
        btns.Children.Add(RangeUi.Secondary("Words to know", () => Mv.Push(new RangeGlossaryPage(A))));
        root.Children.Add(btns);
        Body = RangeUi.Page(root, 1100);
    }

    static Control Step(int n, string art, string colour, string title, string text)
    {
        var c = Rg.C(colour);
        var sp = new StackPanel();
        sp.Children.Add(new Border { Height = 128, CornerRadius = new CornerRadius(13, 13, 0, 0), Background = Rg.Gradient(Color.FromArgb(40, c.R, c.G, c.B), Color.FromArgb(6, c.R, c.G, c.B), 90), Child = Illustration(art, colour) });
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        var t = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        t.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Rg.Soft(colour, 0.18), Child = new TextBlock { Text = n.ToString(CultureInfo.InvariantCulture), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        t.Children.Add(new TextBlock { Text = title, FontSize = 16.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(t);
        body.Children.Add(Rg.Body(text, 14));
        sp.Children.Add(body);
        return new Border { Child = sp, Background = Rg.CardFill(), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 14), ClipToBounds = true };
    }

    // ---------------------------------------------------------------- the pictures (220 x 110, drawn)
    static T S<T>(T s, string stroke, double w) where T : Shape { s.Stroke = Rg.B(stroke); s.StrokeThickness = w; s.StrokeJoin = PenLineJoin.Round; s.StrokeLineCap = PenLineCap.Round; return s; }
    static void At(Canvas c, Control e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }
    static Path P(string data, string stroke, double w, IBrush fill) { var p = S(new Path { Data = Geometry.Parse(data) }, stroke, w); p.Fill = fill; return p; }
    static Rectangle R(double w, double h, double r, IBrush fill) => new() { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill };
    static Ellipse E(double w, double h, IBrush fill) => new() { Width = w, Height = h, Fill = fill };
    static Line L(double x1, double y1, double x2, double y2) => new() { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };

    public static Control Illustration(string kind, string colour)
    {
        var c = new Canvas { Width = 220, Height = 110 };
        var soft = Rg.Soft(colour, 0.16);
        var panel = Rg.B("#0F141E");
        switch (kind)
        {
            case "terminal":
                {
                    var win = R(150, 88, 9, panel); win.Stroke = Rg.Soft(colour, 0.55); win.StrokeThickness = 1.5;
                    At(c, win, 35, 11);
                    At(c, R(150, 18, 9, Rg.Soft(colour, 0.12)), 35, 11);
                    foreach (var dx in new[] { 0, 10, 20 }) At(c, E(6, 6, Rg.Soft(colour, 0.7)), 45 + dx, 17);
                    c.Children.Add(P("M49,45 L56,50 L49,55", colour, 2.4, null));
                    At(c, R(46, 4, 2, Rg.Soft("#E6E9F0", 0.7)), 62, 48);
                    At(c, R(90, 4, 2, Rg.Soft("#E6E9F0", 0.25)), 49, 62);
                    At(c, R(70, 4, 2, Rg.Soft("#E6E9F0", 0.25)), 49, 72);
                    c.Children.Add(P("M49,82 L56,87 L49,92", colour, 2.4, null));
                    At(c, R(9, 13, 0, Rg.B(colour)), 62, 81);
                    break;
                }
            case "network":
                {
                    var nodes = new[] { new Point(110, 55), new Point(45, 30), new Point(175, 28), new Point(60, 88), new Point(168, 86) };
                    for (int i = 1; i < nodes.Length; i++) { var ln = S(L(nodes[0].X, nodes[0].Y, nodes[i].X, nodes[i].Y), "#5C6375", 1.5); if (i % 2 == 0) ln.StrokeDashArray = new AvaloniaList<double> { 3, 3 }; c.Children.Add(ln); }
                    foreach (var r in new[] { 22, 36, 50 }) { var el = S(new Ellipse { Width = r * 2, Height = r * 2, Opacity = Math.Min(1, 1.1 - r / 50.0) }, colour, 1.2); At(c, el, 110 - r, 55 - r); }
                    for (int i = 1; i < nodes.Length; i++)
                    {
                        var box = R(26, 20, 4, panel); box.Stroke = i <= 2 ? Rg.B(colour) : Rg.B(Ui.BorderStrong); box.StrokeThickness = 1.5;
                        At(c, box, nodes[i].X - 13, nodes[i].Y - 10);
                        if (i <= 2) At(c, E(5, 5, Rg.B("#34D399")), nodes[i].X + 7, nodes[i].Y - 13);
                    }
                    At(c, E(22, 22, Rg.B(colour)), 99, 44);
                    At(c, E(8, 8, panel), 106, 51);
                    break;
                }
            case "door":
                {
                    var fr = R(60, 86, 4, panel); fr.Stroke = Rg.B(Ui.BorderStrong); fr.StrokeThickness = 1.5;
                    At(c, fr, 50, 14);
                    c.Children.Add(P("M58,22 L96,16 L96,96 L58,92 Z", colour, 1.8, soft));
                    At(c, E(6, 6, Rg.B(colour)), 86, 54);
                    At(c, R(130, 2, 0, Rg.B(Ui.BorderStrong)), 30, 100);
                    At(c, S(new Ellipse { Width = 24, Height = 24, Fill = panel }, "#FDE68A", 2.4), 128, 38);
                    c.Children.Add(P("M152,50 H188 M180,50 V58 M172,50 V56", "#FDE68A", 2.4, null));
                    c.Children.Add(P("M118,30 L122,22 M112,28 L110,20 M124,36 L132,32", "#FDE68A", 1.6, null));
                    break;
                }
            case "flag":
                {
                    c.Children.Add(P("M20,100 C60,70 80,62 110,60 C140,62 165,72 200,100 Z", Ui.BorderStrong, 1.5, panel));
                    c.Children.Add(S(L(110, 62, 110, 14), "#E6E9F0", 2.4));
                    c.Children.Add(P("M110,16 H150 L141,28 L150,40 H110 Z", colour, 2, Rg.Soft(colour, 0.85)));
                    foreach (var p in new[] { new Point(70, 30), new Point(160, 56), new Point(84, 50), new Point(172, 22) })
                        c.Children.Add(P(string.Format(CultureInfo.InvariantCulture, "M{0},{1} V{2} M{3},{4} H{5}", p.X, p.Y - 5, p.Y + 5, p.X - 5, p.Y, p.X + 5), "#FDE68A", 1.6, null));
                    At(c, R(58, 14, 3, Rg.Soft(colour, 0.2)), 124, 77);
                    At(c, new TextBlock { Text = "FLAG{...}", FontFamily = Rg.Mono, FontSize = 9, Foreground = Rg.B(colour) }, 129, 79);
                    break;
                }
            case "shield":
                {
                    c.Children.Add(P("M70,14 L104,26 V52 C104,74 89,90 70,98 C51,90 36,74 36,52 V26 Z", colour, 2.2, soft));
                    c.Children.Add(P("M55,55 L66,66 L86,44", colour, 3, null));
                    var log = R(78, 80, 6, panel); log.Stroke = Rg.B(Ui.BorderStrong); log.StrokeThickness = 1.5;
                    At(c, log, 122, 15);
                    for (int i = 0; i < 6; i++)
                    {
                        bool bad = i == 3;
                        At(c, R(6, 6, 1, bad ? Rg.B("#F87171") : Rg.Soft("#8C93A5", 0.6)), 130, 25 + i * 11);
                        At(c, R(bad ? 50 : 40 + (i * 7) % 15, 4, 2, bad ? Rg.Soft("#F87171", 0.7) : Rg.Soft("#E6E9F0", 0.25)), 141, 26 + i * 11);
                    }
                    break;
                }
            case "bulb":
                {
                    c.Children.Add(P("M110,16 C92,16 78,30 78,47 C78,59 85,66 91,72 C95,76 96,80 96,84 H124 C124,80 125,76 129,72 C135,66 142,59 142,47 C142,30 128,16 110,16 Z", colour, 2.2, soft));
                    c.Children.Add(P("M97,92 H123 M101,100 H119", colour, 2.4, null));
                    c.Children.Add(P("M102,60 L110,48 L118,60", colour, 2.2, null));
                    foreach (var a in new[] { -60, -30, 0, 30, 60 })
                    {
                        double r = a * Math.PI / 180;
                        c.Children.Add(S(L(110 + Math.Sin(r) * 44, 47 - Math.Cos(r) * 44, 110 + Math.Sin(r) * 52, 47 - Math.Cos(r) * 52), "#FDE68A", 2));
                    }
                    At(c, new Border { Background = Rg.Soft("#A78BFA", 0.15), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 2), Child = new TextBlock { Text = "port", FontSize = 11, Foreground = Rg.B("#C4B5FD"), TextDecorations = TextDecorations.Underline } }, 160, 22);
                    break;
                }
        }
        return new Viewbox { Child = c, Stretch = Stretch.Uniform, Margin = new Thickness(10, 8, 10, 6) };
    }
}

// ------------------------------------------------------------ badges and ranks
public sealed class RangeBadgesPage : RangePage
{
    public override string Title => "Badges and ranks";
    protected override bool ShowRankChip => false;
    public RangeBadgesPage(RangeApp app) : base(app)
    {
        var root = new StackPanel();
        int pts = RangeProgress.TotalPoints(A.Store);
        var rk = RangeProgress.RankFor(pts); var next = RangeProgress.NextRank(pts);
        root.Children.Add(Rg.Eyebrow("Your progress", "#A5B4FC"));
        root.Children.Add(Rg.Title("Badges and ranks", 26));
        var intro = Rg.Body("Your rank comes from your points: the best score of every challenge, added up. Badges mark milestones along the way.", 14.5);
        intro.Margin = new Thickness(0, 6, 0, 16);
        root.Children.Add(intro);

        var ladder = new FlowGrid { MinCell = 120, MaxCols = 5, Gap = 8, RowGap = 14 };
        foreach (var r in RangeProgress.Ranks)
        {
            bool reached = pts >= r.Min, current = r == rk;
            var sp = new StackPanel();
            var circle = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
            circle.Children.Add(new Ellipse { Fill = reached ? Rg.Soft(r.Colour, current ? 0.22 : 0.12) : Rg.Soft("#FFFFFF", 0.03), Stroke = reached ? Rg.B(r.Colour) : Rg.B(Ui.BorderStrong), StrokeThickness = current ? 2.5 : 1.3, StrokeDashArray = reached ? null : new AvaloniaList<double> { 3, 2.5 } });
            var ins = Rg.Insignia(r.Index, 34, reached ? r.Colour : Ui.Dim); ins.HorizontalAlignment = HorizontalAlignment.Center;
            circle.Children.Add(ins);
            sp.Children.Add(circle);
            sp.Children.Add(new TextBlock { Text = r.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = reached ? Rg.B(r.Colour) : Rg.B(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            sp.Children.Add(new TextBlock { Text = Rg.N(r.Min) + "+ points", FontSize = 12.5, Foreground = Rg.B(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = r.Line, FontSize = 12.5, Foreground = current ? Rg.B(Ui.TextC) : Rg.B(Ui.Dim), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(4, 5, 4, 0) });
            if (current) sp.Children.Add(new Border { Child = Rg.Chip("You are here", r.Colour, 0.16), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            ladder.Children.Add(sp);
        }
        var lp = new StackPanel();
        lp.Children.Add(ladder);
        var barRow = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        barRow.Children.Add(Rg.Bar(RangeProgress.RankShare(pts), Rg.Gradient(rk.Colour, next != null ? next.Colour : rk.Colour, 0), 8));
        var bt = Rg.Body(Rg.N(pts) + " points" + (next != null ? "  -  " + Rg.N(next.Min - pts) + " more to reach " + next.Name : "  -  the top rank"), 13);
        bt.Margin = new Thickness(0, 7, 0, 0);
        barRow.Children.Add(bt);
        lp.Children.Add(barRow);
        root.Children.Add(Rg.Panel(lp, 20));

        root.Children.Add(Rg.SectionHead("Badges", "Milestones", A.Store.Badges.Count + " of " + RangeProgress.Badges.Length + " earned"));
        var cards = new FlowGrid { MinCell = 420, MaxCols = 2 };
        foreach (var b in RangeProgress.Badges)
        {
            bool got = A.Store.Badges.TryGetValue(b.Id, out long when);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(Rg.Medal(b.Icon, b.Colour, got, 56));
            var sp = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = b.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = got ? Rg.B(Ui.TextC) : Rg.B(Ui.Dim) });
            var how = Rg.Body(b.How, 13.5); how.Margin = new Thickness(0, 2, 0, 4);
            sp.Children.Add(how);
            sp.Children.Add(new TextBlock { Text = got ? "Earned " + (when > 0 ? new DateTime(when).ToString("d MMMM yyyy", CultureInfo.InvariantCulture) : "") : "Not yet", FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = got ? Rg.B(b.Colour) : Rg.B(Ui.Dim) });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            var panel = Rg.Panel(g, 16);
            if (got) panel.BorderBrush = Rg.Soft(b.Colour, 0.35);
            cards.Children.Add(panel);
        }
        root.Children.Add(cards);
        Body = RangeUi.Page(root, 1100);
    }
}

// ------------------------------------------------------------ the glossary
public sealed class RangeGlossaryPage : RangePage
{
    public override string Title => "Glossary";
    readonly FlowGrid cards = new() { MinCell = 420, MaxCols = 2 };
    readonly TextBlock none;
    public readonly TextBox Search;
    public int Shown => cards.Children.Count;

    public RangeGlossaryPage(RangeApp app) : base(app)
    {
        var root = new StackPanel();
        root.Children.Add(Rg.Eyebrow("Words to know", "#C4B5FD"));
        root.Children.Add(Rg.Title("Glossary", 26));
        var intro = Rg.Body("The words you will meet in Cyber Range, explained simply. On Beginner challenges they are dotted: tap one to see what it means.", 14.5);
        intro.Margin = new Thickness(0, 6, 0, 14);
        root.Children.Add(intro);
        Search = new TextBox { MinHeight = 44, FontSize = 15, PlaceholderText = "Search the words...", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
        Search.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) Fill(Search.Text); };
        root.Children.Add(Search);
        none = Rg.Body("No word matches that.", 14);
        none.IsVisible = false;
        root.Children.Add(none);
        root.Children.Add(cards);
        if (RangeGlossary.Terms.Count == 0) root.Children.Add(Rg.Body("The glossary file could not be read."));
        Fill("");
        Body = RangeUi.Page(root, 1100);
    }

    void Fill(string q)
    {
        cards.Children.Clear();
        q = (q ?? "").Trim();
        var list = RangeGlossary.Terms.Where(t => q.Length == 0 || t.Names.Any(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)) || t.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Term, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var t in list)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = t.Term, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#DDD6FE"), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(Rg.Sel(t.Text, 14));
            if (t.Names.Count > 1) { var a = Rg.Body("Also: " + string.Join(", ", t.Names.Skip(1)), 12.5); a.Margin = new Thickness(0, 5, 0, 0); sp.Children.Add(a); }
            var p = Rg.Panel(sp, 15);
            p.Margin = new Thickness(0, 0, 0, 12);
            cards.Children.Add(p);
        }
        none.IsVisible = list.Count == 0;
    }
}
