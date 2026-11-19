// Cyber Range (Learning Lab) - the run page (App Hub's UiRangeRun.cs, touch first): the player's terminal (Cert Lab's
// CertTerminal) in a frame whose title bar says which machine you are on; the objective card (the answer box with a
// "captured X of Y" meter and a short celebration when a flag is right; the report follows 1.6 s after the last one, at
// once in the checks) and the tabs Brief (with "Explain it simply" on Beginner), Findings (Blue-Team-like: questions and
// fixes), Map (fog of war), Notes, Loot, Hints (hidden until asked, each press reveals the next one with its cost; on
// Beginner a Learning mode makes them free) and Defender (Red vs Defender: what it has noticed). Beside the terminal at
// 900 px and wider; on a phone the terminal fills the page, a strip above it shows the meter, and "Mission" opens the
// card and tabs over the terminal (Back closes them). The walkthrough is never on this page: only the report shows it.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CertLab;
using LearningLab.CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

/// <summary>A row of 44 px tab buttons (scrolling sideways on a phone) and the chosen tab's content under it.</summary>
public sealed class RangeTabs : DockPanel
{
    readonly StackPanel heads = new() { Orientation = Orientation.Horizontal };
    readonly ContentControl body = new();
    readonly List<Control> pages = new();
    readonly List<Button> headers = new();
    readonly List<TextBlock> labels = new(), badges = new();
    readonly string accent;
    public int Selected = -1;
    public int Count => pages.Count;
    public string SelectedName => Selected < 0 ? "" : labels[Selected].Text ?? "";

    public RangeTabs(string accent)
    {
        this.accent = accent;
        var strip = new Border { Child = new ScrollViewer { Content = heads, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 10) };
        SetDock(strip, Dock.Top);
        Children.Add(strip);
        Children.Add(body);
    }
    public int Add(string header, Control content)
    {
        int i = pages.Count;
        pages.Add(content);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var lab = new TextBlock { Text = header, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var badge = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(5, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        sp.Children.Add(lab); sp.Children.Add(badge);
        var h = new Button { Content = sp, MinHeight = 44, Padding = new Thickness(12, 6, 12, 6), Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(0), VerticalContentAlignment = VerticalAlignment.Center };
        h.Resources["ButtonBackgroundPointerOver"] = Rg.Soft("#FFFFFF", 0.04);
        h.Resources["ButtonBackgroundPressed"] = Rg.Soft("#FFFFFF", 0.07);
        h.Click += (s, e) => RangeUi.Safe(() => Select(i));
        Avalonia.Automation.AutomationProperties.SetName(h, header);
        headers.Add(h); labels.Add(lab); badges.Add(badge);
        heads.Children.Add(h);
        if (Selected < 0) Select(0); else Paint();
        return i;
    }
    public void SetBadge(int i, string text, string colour)
    {
        if (i < 0 || i >= badges.Count) return;
        badges[i].Text = text ?? "";
        badges[i].Foreground = Rg.B(colour ?? Ui.Muted);
        badges[i].IsVisible = !string.IsNullOrEmpty(text);
    }
    public void Select(int i)
    {
        if (i < 0 || i >= pages.Count) return;
        Selected = i;
        body.Content = pages[i];
        Paint();
    }
    public bool Select(string name) { int i = labels.FindIndex(l => l.Text == name); if (i < 0) return false; Select(i); return true; }
    public IEnumerable<string> Names => labels.Select(l => l.Text ?? "");
    void Paint()
    {
        for (int k = 0; k < headers.Count; k++)
        {
            bool on = k == Selected;
            headers[k].BorderBrush = on ? Rg.B(accent) : Brushes.Transparent;
            labels[k].Foreground = on ? Rg.B(Ui.TextC) : Rg.B(Ui.Muted);
            labels[k].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}

public sealed class RangeRunPage : RangePage
{
    readonly RangeRun run;
    public RangeRun Run => run;
    public readonly CertTerminal Term;
    readonly TextBlock clock, score, objTitle, stripTitle;
    readonly StackPanel objList, mapList, lootList, hintList, findList, defenderList, briefList;
    public readonly TextBox FlagBox, NotesBox;
    readonly ContentControl meter = new(), stripMeter = new();
    readonly Border objCard, celebrate, side, strip;
    readonly Grid layout;
    readonly Control termFrame;
    readonly Button missionBtn;
    Button moreBtn;
    Control[] wideOnly;
    public readonly RangeTabs Tabs;
    readonly RangeCatStyle st;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    DispatcherTimer finishSoon;
    DateTime last;
    int seenShown = -1, lootShown = -1;
    bool narrow, panelOpen, leaving;
    public int HintsTabIndex = -1, FindingsTabIndex = -1, DefenderTabIndex = -1, MapTabIndex = -1, BriefTabIndex = -1, LootTabIndex = -1, NotesTabIndex = -1;
    /// <summary>The last sentence of the objective card (a capture, a wrong answer).</summary>
    public string Said { get; private set; } = "";
    static readonly string[] cheers = { "Nice work.", "Clean capture.", "That's how it's done.", "Sharp eyes.", "Well found." };

    public override string Title => run.Title;
    protected override bool ShowRankChip => false;

    public RangeRunPage(RangeApp app, RangeRun r) : base(app)
    {
        run = r;
        st = RangeLook.ForId(run.Category);
        var dock = new DockPanel();

        // ---- the bar: category / level, the clock and score, skip time, give up, and (on a phone) Mission
        var bar = new WrapPanel { Margin = new Thickness(12, 8, 12, 2) };
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        sub.Children.Add(Rg.IconTile(st.Icon, st.Accent, st.Accent2, 34));
        var subText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        subText.Children.Add(new TextBlock { Text = RangeApp.CatTitle(run.Category), FontSize = 12.5, Foreground = Rg.B(st.Accent) });
        subText.Children.Add(new TextBlock { Text = run.Level, FontSize = 12.5, Foreground = Rg.B(RangeLook.Level(run.Level).Color) });
        sub.Children.Add(subText);
        bar.Children.Add(sub);
        clock = new TextBlock { Foreground = Rg.B(Ui.TextC), FontSize = 13.5, FontFamily = Rg.Mono, VerticalAlignment = VerticalAlignment.Center };
        score = new TextBlock { Foreground = Rg.B(Ui.TextC), FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(Pill("clock", "#94A3B8", clock, "Time on this run"));
        bar.Children.Add(Pill("star", "#FBBF24", score, "Your score now, out of the most you can get"));
        missionBtn = RangeUi.Small("Mission", () => SetPanel(!panelOpen));
        missionBtn.Margin = new Thickness(0, 0, 8, 8);
        bar.Children.Add(missionBtn);
        var skip = RangeUi.Small("Skip 1 min", () => { run.Tick(60); Tick(); A.Status("The pretend clock moved on a minute."); });
        ToolTip.SetTip(skip, "Move the pretend clock on a minute, for things that happen on a timer (like cron jobs)");
        skip.Margin = new Thickness(0, 0, 8, 8);
        bar.Children.Add(skip);
        var give = RangeUi.Small(run.Pz != null ? "Give up" : "End and show the way", Confirm);
        give.Margin = new Thickness(0, 0, 8, 8);
        bar.Children.Add(give);
        // on a phone the less used buttons wait behind More (one row for the bar, the rest for the terminal)
        var moreSkip = RangeUi.Small("Skip 1 min", () => { moreBtn.Flyout?.Hide(); run.Tick(60); Tick(); A.Status("The pretend clock moved on a minute."); });
        var moreGive = RangeUi.Small(run.Pz != null ? "Give up" : "End and show the way", () => { moreBtn.Flyout?.Hide(); Confirm(); });
        moreSkip.HorizontalAlignment = moreGive.HorizontalAlignment = HorizontalAlignment.Stretch;
        moreBtn = RangeUi.Small("More", null);
        moreBtn.Margin = new Thickness(0, 0, 8, 8);
        moreBtn.Flyout = new Flyout { Content = new StackPanel { Spacing = 8, MinWidth = 220, Children = { moreSkip, moreGive } } };
        bar.Children.Add(moreBtn);
        wideOnly = new Control[] { sub, skip, give };
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        // ---- on a phone: the meter above the terminal
        stripTitle = new TextBlock { FontSize = 13.5, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
        strip = new Border { Child = new StackPanel { Children = { stripTitle, stripMeter } }, Margin = new Thickness(12, 0, 12, 8), IsVisible = false };
        DockPanel.SetDock(strip, Dock.Top);
        dock.Children.Add(strip);

        // ---- the terminal in its frame
        Term = new CertTerminal(run.Console) { Title = run.StartMachine };
        var frame = new DockPanel();
        var titleBar = new Border { Background = Rg.B("#121823"), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 7, 10, 7) };
        var tb = new DockPanel();
        var simChip = Rg.Chip("SIMULATED", "#7DD3FC", 0.10); simChip.Margin = new Thickness(6, 0, 0, 0);
        ToolTip.SetTip(simChip, "Every computer, network and attack here is pretend. Nothing leaves this app.");
        DockPanel.SetDock(simChip, Dock.Right); tb.Children.Add(simChip);
        var ticon = Rg.Icon("terminal", 15, st.Accent, 2.1); ticon.Margin = new Thickness(0, 0, 9, 0);
        DockPanel.SetDock(ticon, Dock.Left); tb.Children.Add(ticon);
        string user = run.Pz != null ? (string.IsNullOrEmpty(run.Pz.Login) ? "player" : run.Pz.Login) : "root";
        string ip = "";
        try { ip = run.Net.IpOf(run.Net.Lab.Get(run.StartMachine)); } catch (Exception) { }
        var who = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        who.Inlines = new Avalonia.Controls.Documents.InlineCollection
        {
            new Avalonia.Controls.Documents.Run(user + "@" + run.StartMachine) { FontFamily = Rg.Mono, FontSize = 13, Foreground = Rg.B(Ui.TextC), FontWeight = FontWeight.SemiBold },
            new Avalonia.Controls.Documents.Run("   " + (ip.Length > 0 ? ip + " - " : "") + "your box" + (run.Pz != null && run.Pz.IsWindows ? ", signed in to " + RangeRun.WinPuzzleName + " over PowerShell remoting" : "")) { FontSize = 12.5, Foreground = Rg.B(Ui.Muted) },
        };
        tb.Children.Add(who);
        titleBar.Child = tb;
        DockPanel.SetDock(titleBar, Dock.Top);
        frame.Children.Add(titleBar);
        frame.Children.Add(Term);
        termFrame = new Border { Child = frame, BorderBrush = Rg.B(Ui.BorderStrong), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), ClipToBounds = true, Background = Rg.B(CertUi.TermBg), Margin = new Thickness(12, 0, 6, 12) };

        // ---- the objective card, then the tabs
        var right = new DockPanel();
        var obj = new StackPanel();
        obj.Children.Add(Rg.Eyebrow(run.Pz != null ? "Answer" : run.IsBlue ? "Findings" : "Flags", st.Accent));
        objTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        obj.Children.Add(objTitle);
        meter.Margin = new Thickness(0, 0, 0, 10);
        obj.Children.Add(meter);
        objList = new StackPanel();
        obj.Children.Add(objList);
        celebrate = new Border { IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
        if (!run.IsBlue || run.Sc.Flags.Count > 0)
        {
            var subRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var submit = RangeUi.Primary("Submit", DoSubmit);
            submit.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(submit, Dock.Right);
            FlagBox = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = Rg.Mono, FontSize = 14, PlaceholderText = run.Pz != null && run.Pz.Answer != "flag" ? "Type the answer" : "FLAG{...}" };
            Avalonia.Input.TextInput.TextInputOptions.SetAutoCapitalization(FlagBox, false);
            FlagBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; RangeUi.Safe(DoSubmit); } };
            subRow.Children.Add(submit); subRow.Children.Add(FlagBox);
            obj.Children.Add(subRow);
        }
        obj.Children.Add(celebrate);
        objCard = new Border { Child = obj, Background = Rg.B(Ui.Surface), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(objCard, Dock.Top);

        Tabs = new RangeTabs(st.Accent);
        mapList = new StackPanel(); lootList = new StackPanel(); hintList = new StackPanel(); findList = new StackPanel(); defenderList = new StackPanel(); briefList = new StackPanel();
        NotesBox = new TextBox { Text = A.Store.For(run.Pz != null, run.Id).Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 200, FontSize = 14, VerticalAlignment = VerticalAlignment.Stretch, PlaceholderText = "Your notes: addresses, passwords, ideas. They are kept for this challenge." };
        NotesBox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) A.Store.For(run.Pz != null, run.Id).Notes = NotesBox.Text ?? ""; };
        BriefTabIndex = Tabs.Add("Brief", Scroll(briefList));
        if (run.IsBlue && (run.Sc.Questions.Count > 0 || run.Sc.Tasks.Count > 0)) FindingsTabIndex = Tabs.Add("Findings", Scroll(findList));
        MapTabIndex = Tabs.Add("Map", Scroll(mapList));
        NotesTabIndex = Tabs.Add("Notes", NotesBox);
        if (!run.IsBlue) LootTabIndex = Tabs.Add("Loot", Scroll(lootList));
        HintsTabIndex = Tabs.Add("Hints", Scroll(hintList));
        if (run.IsDefender) DefenderTabIndex = Tabs.Add("Defender", Scroll(defenderList));
        Tabs.Select(run.IsBlue && FindingsTabIndex >= 0 ? FindingsTabIndex : run.Pz != null ? BriefTabIndex : MapTabIndex);

        // the card scrolls with the tabs on a phone (the panel takes the screen), and stays on top beside the terminal
        right.Children.Add(objCard);
        right.Children.Add(Tabs);
        side = new Border { Child = right, Margin = new Thickness(6, 0, 12, 12) };

        layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        layout.Children.Add(termFrame);
        Grid.SetColumn(side, 1);
        layout.Children.Add(side);
        dock.Children.Add(layout);
        layout.SizeChanged += (s, e) => Fit(e.NewSize.Width);
        Body = dock;
        Fit(1000);

        BuildBrief(); BuildObjective(); BuildHints(); BuildFindings(); RefreshMap(); RefreshLoot(); RefreshDefender(); UpdateScore();
        last = DateTime.Now;
        timer.Tick += (s, e) => RangeUi.Safe(Tick);
        View.AttachedToVisualTree += (s, e) => { if (!A.Quiet) timer.Start(); };
        View.DetachedFromVisualTree += (s, e) => timer.Stop();
    }

    static Border Pill(string icon, string colour, TextBlock text, string tip)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(Rg.Icon(icon, 13, colour, 2.3));
        sp.Children.Add(text);
        var b = new Border { Child = sp, Background = Rg.Soft("#FFFFFF", 0.05), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 4, 12, 5), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center, MinHeight = 32 };
        ToolTip.SetTip(b, tip);
        return b;
    }
    static ScrollViewer Scroll(Control c) => new() { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 6) };

    // ---- wide: the panel beside the terminal; narrow: the panel over it (Mission / Back)
    void Fit(double w)
    {
        narrow = w < 900;
        if (narrow)
        {
            Grid.SetColumn(side, 0);
            side.Width = double.NaN;
            side.Margin = new Thickness(12, 0, 12, 12);
            side.IsVisible = panelOpen;
            termFrame.IsVisible = !panelOpen;
            termFrame.Margin = new Thickness(12, 0, 12, 12);
        }
        else
        {
            Grid.SetColumn(side, 1);
            side.Width = Math.Min(400, Math.Max(340, w * 0.36));
            side.Margin = new Thickness(6, 0, 12, 12);
            side.IsVisible = true;
            termFrame.IsVisible = true;
            termFrame.Margin = new Thickness(12, 0, 6, 12);
        }
        missionBtn.IsVisible = narrow;
        moreBtn.IsVisible = narrow;
        foreach (var c in wideOnly) c.IsVisible = !narrow;
        strip.IsVisible = narrow && !panelOpen;
    }
    /// <summary>On a phone: the card and tabs over the terminal (true) or the terminal (false).</summary>
    public void SetPanel(bool open)
    {
        panelOpen = open;
        missionBtn.Content = open ? "Back to the terminal" : "Mission";
        Fit(layout.Bounds.Width > 0 ? layout.Bounds.Width : 1000);
        if (!open) Term.FocusInput();
    }
    public bool PanelOpen => panelOpen && narrow;
    public bool Narrow => narrow;

    public override bool OnBack()
    {
        if (base.OnBack()) return true;
        if (PanelOpen) { SetPanel(false); return true; }
        if (leaving || run.Finished) return false;
        Mv.Message("Leave this challenge?", "Your notes are kept, but this run ends without a score. You can start it again any time: the details change every run.",
            ("Leave", () => { leaving = true; Mv.Back(); }), ("Stay", null));
        return true;
    }
    public override void Closed()
    {
        Stop();
        if (A.RunPage == this) { A.Store.Save(); A.EndRun(); }
    }

    public void Stop() { timer.Stop(); finishSoon?.Stop(); try { Term.Poll(); } catch (Exception) { } }

    /// <summary>Picks up the terminal's output and refreshes the panels now (the checks; the timer does it on a screen).</summary>
    public void Refresh() { Term.Poll(); RefreshMap(); RefreshLoot(); RefreshDefender(); BuildObjective(); BuildFindings(); BuildHints(); UpdateScore(); }

    void Tick()
    {
        var now = DateTime.Now;
        long secs = (long)(now - last).TotalSeconds;
        if (secs >= 1) { run.Tick(secs); last = now; }
        if (run.Net.Seen.Count != seenShown) RefreshMap();
        if (run.Net.Worked.Count != lootShown) RefreshLoot();
        if (run.IsDefender) RefreshDefender();
        UpdateScore();
        if (run.IsBlue && run.Breached && !run.Finished) { BuildFindings(); BuildObjective(); }
    }

    void UpdateScore()
    {
        score.Text = run.Score + " / " + run.MaxPoints;
        clock.Text = RangeRun.TimeText(run.Seconds);
        int used = run.HintsUsed.Count;
        Tabs.SetBadge(HintsTabIndex, used > 0 ? used + "/" + run.Hints.Count : "", "#FCD34D");
        if (DefenderTabIndex >= 0 && run.Defender != null) Tabs.SetBadge(DefenderTabIndex, run.Defender.Alerts > 0 ? run.Defender.Alerts.ToString(CultureInfo.InvariantCulture) : "", "#FCA5A5");
        Tabs.SetBadge(MapTabIndex, run.Net.Seen.Count > 0 ? (run.Net.Seen.Count + 1).ToString(CultureInfo.InvariantCulture) : "", Ui.Muted);
    }

    // ---- the objective card
    void BuildObjective()
    {
        objList.Children.Clear();
        int total, got;
        if (run.Pz != null) { total = 1; got = run.Found.Count > 0 ? 1 : 0; }
        else { total = run.Sc.ObjectiveCount; got = run.Found.Count + run.Answered.Count + run.TasksDone.Count; }
        string what = run.Pz != null ? "" : run.IsBlue ? (run.Sc.Flags.Count > 0 ? "objectives" : "findings") : total == 1 ? "flag" : "flags";
        if (run.Pz != null) objTitle.Text = got > 0 ? "Solved!" : "Find the answer, then type it below";
        else objTitle.Text = (run.IsBlue ? "You've completed " : "You captured ") + got + " of " + total + " " + what;
        meter.Content = Rg.Segments(got, total, "#34D399", 7);
        stripTitle.Text = objTitle.Text;
        stripMeter.Content = Rg.Segments(got, total, "#34D399", 5);
        if (run.Pz != null) return;
        foreach (var f in run.Sc.Flags) objList.Children.Add(ObjRow(run.Found.Contains(f.Id), f.Name, f.Points));
        if (run.IsBlue)
        {
            if (run.Sc.Questions.Count > 0) objList.Children.Add(ObjRow(run.Answered.Count == run.Sc.Questions.Count, "Questions answered: " + run.Answered.Count + " of " + run.Sc.Questions.Count, run.Sc.Questions.Sum(q => q.Points)));
            if (run.Sc.Tasks.Count > 0) objList.Children.Add(ObjRow(run.TasksDone.Count == run.Sc.Tasks.Count, "Fixes made: " + run.TasksDone.Count + " of " + run.Sc.Tasks.Count, run.Sc.Tasks.Sum(t => t.Points)));
            if (run.Sc.GoalPoints > 0)
            {
                var t = Rg.Body(run.Breached ? "The attacker reached its goal: the containment points are gone." : "The attacker is still at work. Stop it before it can " + (run.Sc.GoalName.Length > 0 ? run.Sc.GoalName : "reach its goal") + ".", 13, run.Breached ? "#FCA5A5" : "#FCD34D");
                t.Margin = new Thickness(0, 6, 0, 0);
                objList.Children.Add(t);
            }
        }
    }
    static Control ObjRow(bool got, string name, int points)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var pts = new TextBlock { Text = (got ? "+" : "") + points, FontSize = 13, Foreground = got ? Rg.B("#6EE7B7") : Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(pts, Dock.Right); dp.Children.Add(pts);
        var circle = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        circle.Children.Add(new Ellipse { Fill = got ? Rg.B("#34D399") : Brushes.Transparent, Stroke = got ? Rg.B("#34D399") : Rg.B(Ui.BorderStrong), StrokeThickness = 1.5 });
        if (got) { var ck = Rg.Icon("check", 11, "#06281C", 3.6); ck.HorizontalAlignment = HorizontalAlignment.Center; circle.Children.Add(ck); }
        DockPanel.SetDock(circle, Dock.Left); dp.Children.Add(circle);
        dp.Children.Add(new TextBlock { Text = name, FontSize = 13.5, Foreground = got ? Rg.B(Ui.TextC) : Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return dp;
    }

    void Say(string msg, bool right, bool captured)
    {
        Said = msg;
        A.Status(msg);
        string colour = right ? "#34D399" : "#F87171";
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var badge = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(new Ellipse { Fill = Rg.Soft(colour, 0.18), Stroke = Rg.B(colour), StrokeThickness = 1.5 });
        Control ic = right ? Rg.Icon("check", 17, colour, 3) : new TextBlock { Text = "!", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Rg.B(colour), VerticalAlignment = VerticalAlignment.Center };
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(ic);
        g.Children.Add(badge);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (captured)
        {
            sp.Children.Add(new TextBlock { Text = run.Pz != null ? "Solved!" : run.AllFound ? "Last flag captured!" : "Flag captured!", FontSize = 15.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#6EE7B7") });
            sp.Children.Add(Rg.Body(msg.Replace("Flag captured: ", "").Replace("Solved! ", "") + "  " + cheers[(run.Found.Count + run.Id.Length) % cheers.Length], 13, "#A7F3D0"));
        }
        else sp.Children.Add(Rg.Body(msg, 13, right ? "#A7F3D0" : "#FCA5A5"));
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        celebrate.Child = g;
        celebrate.IsVisible = true;
        objCard.BorderBrush = Rg.B(right ? "#2F7D5B" : "#7F3B3B");
        if (captured) { objCard.Background = Rg.Gradient("#123826", Ui.Surface, 90); RangeFx.FadeIn(celebrate, 300); }
    }

    /// <summary>Submits what is in the answer box (the Submit button, Enter).</summary>
    public void DoSubmit()
    {
        if (FlagBox == null) return;
        int before = run.Found.Count;
        string msg = run.Submit(FlagBox.Text ?? "", out bool right);
        bool captured = right && run.Found.Count > before;
        Say(msg, right, captured);
        if (right) FlagBox.Text = "";
        BuildObjective(); RefreshMap(); UpdateScore();
        if (run.AllFound) FinishSoon();
    }

    /// <summary>Everything captured: a moment to enjoy it, then the report (at once in the checks).</summary>
    void FinishSoon()
    {
        if (!RangeFx.On) { if (A.Run == run) A.FinishRun(false); return; }
        if (finishSoon != null) return;
        finishSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        finishSoon.Tick += (s, e) => { finishSoon.Stop(); RangeUi.Safe(() => { if (A.Run == run) A.FinishRun(false); }); };
        finishSoon.Start();
    }

    // ---- the brief: the task in words, and on Beginner "Explain it simply"
    void BuildBrief()
    {
        briefList.Children.Clear();
        bool beginner = RangeData.LevelIndex(run.Level) == 0;
        if (run.Pz != null)
        {
            briefList.Children.Add(Rg.PaneHead("flag", "Your task"));
            briefList.Children.Add(Glossed(run.Pz.Intro, 14.5, Ui.TextC, beginner));
            if (beginner) { var t = RangeExplain.Toggle(this, A, st, run.Pz.Simple, run.Pz.Intro); t.Margin = new Thickness(0, 14, 0, 0); briefList.Children.Add(t); }
            var how = Rg.Body(run.Pz.Answer == "flag" ? "The answer is a flag: FLAG{...}. Copy it into the answer box." : "Type the answer in the answer box (upper or lower case doesn't matter).", 13);
            how.Margin = new Thickness(0, 12, 0, 0);
            briefList.Children.Add(how);
            return;
        }
        var s = run.Sc;
        briefList.Children.Add(Rg.PaneHead("flag", "Your goal"));
        briefList.Children.Add(Glossed(s.Goal, 14.5, Ui.TextC, beginner));
        if (beginner) { var t = RangeExplain.Toggle(this, A, st, s.Simple, s.Story, s.Goal, s.Summary); t.Margin = new Thickness(0, 14, 0, 0); briefList.Children.Add(t); }
        var story = new TextBlock { Text = "The story", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), Margin = new Thickness(0, 14, 0, 4) };
        briefList.Children.Add(story);
        briefList.Children.Add(Glossed(s.Story, 13.5, Ui.Muted, beginner));
    }

    // ---- blue team: questions and fixes
    /// <summary>The checks: the answer box of each open question.</summary>
    public readonly Dictionary<string, TextBox> AnswerBoxes = new();
    void BuildFindings()
    {
        if (FindingsTabIndex < 0) return;
        findList.Children.Clear();
        AnswerBoxes.Clear();
        if (run.Sc.Questions.Count > 0)
        {
            findList.Children.Add(Head("Questions"));
            foreach (var q0 in run.Sc.Questions)
            {
                var q = q0;
                bool done = run.Answered.ContainsKey(q.Id);
                bool missed = run.Missed.Contains(q.Id);   // closed after its last try (SOC verdicts): no points
                var sp = new StackPanel();
                var top = new DockPanel();
                var pts = new TextBlock { Text = missed ? "0" : (done ? "+" : "") + q.Points, FontSize = 13, Foreground = done && !missed ? Rg.B("#6EE7B7") : Rg.B(Ui.Muted), Margin = new Thickness(8, 1, 0, 0) };
                DockPanel.SetDock(pts, Dock.Right); top.Children.Add(pts);
                top.Children.Add(new TextBlock { Text = q.Ask, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = missed ? Rg.B(Ui.Muted) : done ? Rg.B("#A7F3D0") : Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
                sp.Children.Add(top);
                if (missed) { var m = Rg.Body("No points - you answered " + run.Answered[q.Id] + "; the answer was " + RangeData.Fill(q.Answer, run.Vars, run.Flags) + ".", 13); m.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(m); }
                else if (done) { var m = Rg.Body("Answered: " + run.Answered[q.Id], 13); m.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(m); }
                else
                {
                    var dp = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
                    var ans = new TextBox { MinHeight = 44, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = "Your answer" };
                    Avalonia.Input.TextInput.TextInputOptions.SetAutoCapitalization(ans, false);
                    AnswerBoxes[q.Id] = ans;
                    void Go()
                    {
                        string m = run.Answer(q, ans.Text ?? "", out bool ok);
                        Say(m, ok, false);
                        if (ok || run.Answered.ContainsKey(q.Id)) { BuildFindings(); BuildObjective(); UpdateScore(); FinishIfDone(); }
                    }
                    var btn = RangeUi.Small("Answer", Go);
                    btn.Margin = new Thickness(8, 0, 0, 0);
                    DockPanel.SetDock(btn, Dock.Right);
                    ans.KeyDown += (o, e) => { if (e.Key == Key.Enter) { e.Handled = true; RangeUi.Safe(Go); } };
                    dp.Children.Add(btn); dp.Children.Add(ans);
                    sp.Children.Add(dp);
                }
                findList.Children.Add(Item(sp, done && !missed));
            }
        }
        if (run.Sc.Tasks.Count > 0)
        {
            findList.Children.Add(Head("Fixes to make"));
            var hint = Rg.Body("Make each fix in the terminal, then press Check.", 13); hint.Margin = new Thickness(0, 0, 0, 8);
            findList.Children.Add(hint);
            foreach (var t0 in run.Sc.Tasks)
            {
                var t = t0;
                bool done = run.TasksDone.Contains(t.Id);
                var row = new DockPanel();
                var chk = RangeUi.Small(done ? "Done" : "Check", () => { string m = run.CheckTask(t, out bool ok); Say(m, ok, false); BuildFindings(); BuildObjective(); UpdateScore(); FinishIfDone(); });
                chk.IsEnabled = !done;
                chk.Tag = "check:" + t.Id;
                DockPanel.SetDock(chk, Dock.Right); chk.Margin = new Thickness(8, 0, 0, 0); chk.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(chk);
                var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                nm.Children.Add(new TextBlock { Text = t.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = done ? Rg.B("#A7F3D0") : Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
                nm.Children.Add(new TextBlock { Text = (done ? "+" : "") + t.Points + " points", FontSize = 12.5, Foreground = done ? Rg.B("#6EE7B7") : Rg.B(Ui.Muted) });
                row.Children.Add(nm);
                findList.Children.Add(Item(row, done));
            }
        }
    }
    static Border Item(Control c, bool done)
        => new() { Child = c, Background = done ? Rg.Soft("#34D399", 0.06) : Rg.Soft("#FFFFFF", 0.03), BorderBrush = done ? Rg.Soft("#34D399", 0.3) : Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 0, 8) };
    void FinishIfDone() { if (run.AllFound) FinishSoon(); }
    static TextBlock Head(string t) => new() { Text = t.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.Muted), Margin = new Thickness(0, 2, 0, 8) };

    // ---- map (fog of war)
    void RefreshMap()
    {
        seenShown = run.Net.Seen.Count;
        mapList.Children.Clear();
        mapList.Children.Add(Host(run.Net.IpOf(run.Net.Lab.Get(run.StartMachine)), run.StartMachine, run.Pz != null ? "Linux" : "your box", null, "self"));
        if (run.Net.Seen.Count == 0)
        {
            var t = Rg.Body("No other machines found yet. Scan the network (for example nmap -sn) to fill this in.", 13); t.Margin = new Thickness(2, 6, 2, 0);
            mapList.Children.Add(t);
            return;
        }
        foreach (var s in run.Net.Seen.Values.OrderBy(x => x.Ip, StringComparer.Ordinal)) mapList.Children.Add(Host(s.Ip, s.Name, s.Os, s.Ports, s.Owned));
    }
    /// <summary>The checks: the hosts on the map (yours first).</summary>
    public int MapHosts => mapList.Children.Count(c => c is Border);

    Border Host(string ip, string name, string os, List<int> ports, string owned)
    {
        string colour = owned == "admin" ? "#FB7185" : owned == "user" ? "#FBBF24" : owned == "self" ? st.Accent : "#94A3B8";
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var ic = Rg.Icon("server", 16, colour, 2); ic.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Rg.Soft(colour, 0.13), Child = ic, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top });
        var sp = new StackPanel();
        var top = new WrapPanel();
        top.Children.Add(new SelectableTextBlock { Text = ip, FontFamily = Rg.Mono, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (!string.IsNullOrEmpty(name)) top.Children.Add(new TextBlock { Text = name, FontSize = 13.5, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (owned == "admin") top.Children.Add(Rg.Chip("owned", colour, 0.15));
        else if (owned == "user") top.Children.Add(Rg.Chip("foothold", colour, 0.15));
        else if (owned == "self") top.Children.Add(Rg.Chip("you are here", colour, 0.15));
        sp.Children.Add(top);
        if (!string.IsNullOrEmpty(os)) sp.Children.Add(new TextBlock { Text = os, FontSize = 12.5, Foreground = Rg.B(Ui.Muted), Margin = new Thickness(0, 1, 0, 0) });
        if (ports != null && ports.Count > 0)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            foreach (var p in ports) wp.Children.Add(new Border { Background = Rg.Soft("#FFFFFF", 0.06), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 4), Child = new TextBlock { Text = p.ToString(CultureInfo.InvariantCulture), FontFamily = Rg.Mono, FontSize = 12, Foreground = Rg.B("#CBD5E1") } });
            sp.Children.Add(wp);
            sp.Children.Add(new TextBlock { Text = "open ports: programs answer there", FontSize = 11.5, Foreground = Rg.B(Ui.Dim) });
        }
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return new Border { Child = g, Background = Rg.Soft("#FFFFFF", 0.03), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 9, 10, 8), Margin = new Thickness(0, 0, 0, 8) };
    }

    // ---- loot
    void RefreshLoot()
    {
        if (run.IsBlue) return;
        lootShown = run.Net.Worked.Count;
        lootList.Children.Clear();
        var auto = new RangeSwitch("Add credentials that work automatically", "key", "#FBBF24", A.Store.AutoCapture) { Margin = new Thickness(0, 0, 0, 10) };
        auto.Changed += on => { A.Store.AutoCapture = on; RefreshLoot(); };
        lootList.Children.Add(auto);
        var shown = A.Store.AutoCapture ? run.Net.Worked : run.Net.Worked.Where(x => !x.Auto).ToList();
        if (shown.Count == 0)
        {
            lootList.Children.Add(Rg.Body("Nothing yet. Credentials that let you in (a password, a key, a share) appear here.", 13));
            return;
        }
        foreach (var w in shown)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var ic = Rg.Icon("key", 16, "#FCD34D", 2); ic.Margin = new Thickness(0, 2, 10, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(ic);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = w.User + " @ " + w.Host, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new SelectableTextBlock { Text = w.Secret, FontFamily = Rg.Mono, FontSize = 13, Foreground = Rg.B("#FDE68A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(Rg.Body(w.Where, 12.5));
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            lootList.Children.Add(new Border { Child = g, Background = Rg.Soft("#FFFFFF", 0.03), BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 8) });
        }
    }

    // ---- defender (red vs defender)
    void RefreshDefender()
    {
        if (DefenderTabIndex < 0 || run.Defender == null) return;
        defenderList.Children.Clear();
        var d = run.Defender;
        bool quiet = d.Alerts == 0;
        string colour = quiet ? "#34D399" : "#FBBF24";
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var ic = Rg.Icon(quiet ? "ghost" : "eye", 20, colour, 2); ic.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Rg.Soft(colour, 0.15), Child = ic, Margin = new Thickness(0, 0, 12, 0) });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = quiet ? "Unnoticed" : d.Alerts + (d.Alerts == 1 ? " alert" : " alerts") + (d.Blocks > 0 ? ", " + d.Blocks + (d.Blocks == 1 ? " block" : " blocks") : ""), FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(colour) });
        sp.Children.Add(Rg.Body(d.Name + (quiet ? " has not noticed you. The quiet bonus is still yours." : ". The quiet bonus is slipping."), 13));
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        defenderList.Children.Add(new Border { Child = g, Background = Rg.Soft(colour, 0.06), BorderBrush = Rg.Soft(colour, 0.3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) });
        var rules = run.Sc.DefenderRules;
        if (rules != null && rules.Watches.Length > 0) { var w = Rg.Body("It looks out for " + rules.Watches + ".", 13.5, Ui.TextC); w.Margin = new Thickness(0, 0, 0, 10); defenderList.Children.Add(w); }
        if (d.Report.Count > 0) defenderList.Children.Add(Head("What it noticed"));
        foreach (var l in d.Report)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Rg.B("#FBBF24"), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 7, 0, 0) });
            var t = Rg.Body(l, 13, Ui.TextC);
            Grid.SetColumn(t, 1); row.Children.Add(t);
            defenderList.Children.Add(row);
        }
        if (d.Report.Count == 0) defenderList.Children.Add(Rg.Body("Keep the noise down: avoid big scans, failed sign-ins and loud password guessing. Reach your goal by the quiet path.", 13));
    }

    // ---- hints (hidden until asked)
    void BuildHints()
    {
        hintList.Children.Clear();
        if (RangeData.LevelIndex(run.Level) == 0)
        {
            var learn = new RangeSwitch("Learning mode: hints are free", "book", "#34D399", run.LearningMode) { Margin = new Thickness(0, 0, 0, 10) };
            learn.Changed += on => { run.LearningMode = on; BuildHints(); UpdateScore(); };
            hintList.Children.Add(learn);
        }
        var intro = Rg.Body("Try on your own first. Each hint stays hidden until you ask for it.", 13); intro.Margin = new Thickness(0, 0, 0, 10);
        hintList.Children.Add(intro);
        int total = run.Hints.Count;
        for (int i = 0; i < total; i++)
        {
            int idx = i;
            var h = run.Hints[i];
            if (run.HintsUsed.Contains(idx))
            {
                var sp = new StackPanel();
                var hd = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
                hd.Children.Add(Rg.Icon("bulb", 13, "#FCD34D", 2.2));
                hd.Children.Add(new TextBlock { Text = "Hint " + (idx + 1) + " of " + total, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#FCD34D"), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(hd);
                sp.Children.Add(Rg.Sel(h.Text, 14));
                hintList.Children.Add(new Border { Child = sp, Background = Rg.Soft("#FBBF24", 0.06), BorderBrush = Rg.Soft("#FBBF24", 0.25), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 8), Tag = "hint" });
                continue;
            }
            // the next hint not shown has its button; later ones stay locked until this one is taken
            bool next = Enumerable.Range(0, idx).All(j => run.HintsUsed.Contains(j));
            if (next)
            {
                string cost = run.LearningMode ? "free" : "-" + h.Cost + " points";
                var b = RangeUi.Small("Show hint " + (idx + 1) + " of " + total + "  (" + cost + ")", () => { run.TakeHint(idx); BuildHints(); UpdateScore(); });
                b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(0, 0, 0, 8);
                hintList.Children.Add(b);
            }
            else
            {
                var lk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                lk.Children.Add(Rg.Icon("lock", 13, Ui.Dim, 2.2));
                lk.Children.Add(new TextBlock { Text = "Hint " + (idx + 1) + " of " + total + " - take the one before first", FontSize = 13, Foreground = Rg.B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                hintList.Children.Add(new Border { Child = lk, BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 0, 8) });
            }
        }
        if (total == 0) hintList.Children.Add(Rg.Body("No hints for this one.", 13));
    }
    /// <summary>The checks: the hints shown on the page now.</summary>
    public int HintsShown => hintList.Children.OfType<Border>().Count(b => (b.Tag as string) == "hint");

    void Confirm()
    {
        if (A.Quiet) { A.FinishRun(!run.AllFound); return; }
        Mv.Message(run.AllFound ? "Show your report?" : run.Pz != null ? "Give up?" : "End the run?", run.AllFound ? "Show your report now?" : "This ends the run and shows the intended path. You can always try again later: the details change every run.",
            (run.AllFound ? "Show the report" : run.Pz != null ? "Give up" : "End and show the way", () => A.FinishRun(!run.AllFound)), ("Keep going", null));
    }
}
