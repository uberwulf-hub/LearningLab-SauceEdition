// Learning Games - the math home page (today's goal, the games, practising a skill), the results after a game, the
// rewards (My Build for younger players, My Lab for older ones) and the math progress report.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using Problems = LearningGames.Problems;
using Rewards = LearningGames.Rewards;
using Snd = LearningGames.Sound;

namespace LearningLab.LearningGames;

public class MathHomePage : IPage
{
    public string Title => "Math";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel body = new();
    readonly ContentControl invite = new();

    public MathHomePage()
    {
        View = Ui.Page(body);
        Fill();
        View.AttachedToVisualTree += (s, e) => { Fill(); Family.Changed += OnFamily; Family.Looking = Lg.Me != null && Lg.Me.Grade > 0; };
        View.DetachedFromVisualTree += (s, e) => { Family.Changed -= OnFamily; Family.Looking = false; };
    }

    void OnFamily() { UpdateInvite(); MainView.Current.RefreshBar(); }

    void Fill()
    {
        var p = Lg.Me; if (p == null) return;
        body.Children.Clear();
        var t = p.TodayCount;
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var hello = new StackPanel { Margin = new Thickness(0, 0, 24, 8), VerticalAlignment = VerticalAlignment.Center };
        hello.Children.Add(Ui.Text("Hi, " + p.Name + "!", 30, Ui.TextC, true));
        hello.Children.Add(Ui.Text(t.Done == 0 ? "Ready for some math? Pick a game." : t.Done < Profiles.DailyGoal ? "You've answered " + t.Done + " today. Keep going!" : "Daily goal done. Awesome!", 15, Ui.Muted));
        top.Children.Add(hello);
        double frac = Math.Min(1.0, t.Done / (double)Profiles.DailyGoal);
        var ring = new Grid { Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0) };
        ring.Children.Add(new Ellipse { Stroke = Ui.Brush(Ui.Surface3), StrokeThickness = 7 });
        ring.Children.Add(new Path { Data = Arc(frac, 28, 24.5), Stroke = Ui.Brush(Ui.Green), StrokeThickness = 7, StrokeLineCap = PenLineCap.Round });
        ring.Children.Add(new TextBlock { Text = (int)(frac * 100) + "%", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(Ui.TextC), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        int ds = p.DayStreak;
        var goal = Ui.Card(new StackPanel { Orientation = Orientation.Horizontal, Children = { ring, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text("Today's goal", 13, Ui.Muted), Ui.Text(Math.Min(t.Done, Profiles.DailyGoal) + " of " + Profiles.DailyGoal + " answers", 16, Ui.TextC, true), Ui.Text(ds > 1 ? ds + " days in a row" : "", 13, Ui.Muted) } } } }, Ui.Surface, 12);
        top.Children.Add(goal);
        body.Children.Add(top);

        body.Children.Add(Lg.H2("Games"));
        var tiles = new List<Control>();
        foreach (var g in Lg.Games.Concat(Lg.GradeGames(p.Grade)))
        {
            int best = p.Best(g.Id);
            string id = g.Id;
            tiles.Add(Lg.Tile(g.Glyph, g.Color, g.Name, g.Text, best > 0 ? "Best: " + best : "", Ui.Yellow, () => OpenTile(id), 170));
        }
        var grid = Ui.Tiles(tiles, 200); grid.Margin = new Thickness(-6, 0, -6, 8);
        body.Children.Add(grid);
        body.Children.Add(invite);
        UpdateInvite();

        var bar = new WrapPanel { Margin = new Thickness(0, 18, 0, 10) };
        var h = Lg.H2("Practice a skill"); h.Margin = new Thickness(0, 0, 16, 6); h.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(h);
        var mine = Ui.Button("My progress"); mine.Margin = new Thickness(0, 0, 8, 6);
        mine.Click += (s, e) => MainView.Current.Push(new ProgressPage(false));
        bar.Children.Add(mine);
        if (p.Grade > 0)
        {
            var fam = Ui.Button("Play with family (home network)..."); fam.Margin = new Thickness(0, 0, 8, 6);
            fam.Click += (s, e) => MainView.Current.Push(new LobbyPage());
            bar.Children.Add(fam);
        }
        body.Children.Add(bar);
        foreach (var area in Problems.AreasFor(p.MathGrade))
        {
            var ah = Ui.Text(area, 15, Ui.AccentText, true); ah.Margin = new Thickness(4, 14, 0, 2);
            body.Children.Add(ah);
            var skills = new List<Control>();
            foreach (var id in Problems.SkillsIn(p.MathGrade, area))
            {
                var st = p.Skill(id);
                var row = new DockPanel();
                var lv = Ui.Text(st.Mastered ? "Mastered" : "Level " + st.Level, 12.5, st.Mastered ? Ui.Green : Ui.AccentText);
                lv.VerticalAlignment = VerticalAlignment.Center; lv.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(lv, Dock.Right); row.Children.Add(lv);
                var inner = new StackPanel();
                var n = Ui.Text(Profiles.SkillName(id), 14.5); n.TextWrapping = TextWrapping.NoWrap; n.TextTrimming = TextTrimming.CharacterEllipsis;
                inner.Children.Add(n);
                var b2 = Lg.Bar(Math.Min(1.0, (st.Level - 1 + st.Accuracy) / 5), st.Mastered ? Ui.Green : Ui.Accent); b2.Margin = new Thickness(0, 7, 10, 0);
                inner.Children.Add(b2);
                row.Children.Add(inner);
                string sid = id;
                var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 10), MinHeight = 48, Background = Ui.Brush(Ui.Surface2), CornerRadius = new CornerRadius(10) };
                ToolTip.SetTip(b, (st.Attempts == 0 ? "Not tried yet" : st.Attempts + " answers so far") + "  |  " + Problems.Info(id)?.Std);
                b.Click += (s, e) => GamePage.Start("practice", sid);
                skills.Add(b);
            }
            var sg = Ui.Tiles(skills, 250, 8); sg.Margin = new Thickness(-4, 0, -4, 0);
            body.Children.Add(sg);
        }
    }

    void UpdateInvite()
    {
        invite.Content = null;
        var p = Lg.Me; if (p == null) return;
        string text = null, button = null; Action act = null;
        if (Family.Mode == NetMode.Client)
        {
            text = "You're in " + Family.HostName + "'s " + Family.Session.ToLowerInvariant() + ". They can see your answers and send you hints.";
            button = "Leave"; act = () => { Family.Stop(); UpdateInvite(); };
        }
        else if (Family.Mode == NetMode.None)
        {
            var room = Family.Hosts.FirstOrDefault(h => h.Session == "Family room");
            if (room != null)
            {
                text = room.Name + " opened the family room. Join so they can help you and start games for everyone!";
                button = "Join";
                act = () =>
                {
                    string err = Family.Join(p, room.Address, room.Port);
                    if (err.Length > 0) MainView.Current.Message("", err);
                    UpdateInvite();
                };
            }
        }
        if (text == null) return;
        var b = Ui.Button(button, true); b.MinWidth = 100; b.VerticalAlignment = VerticalAlignment.Center;
        b.Click += (s, e) => act();
        var d = new DockPanel();
        DockPanel.SetDock(b, Dock.Right);
        d.Children.Add(b);
        var t = Ui.Text(text, 15); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 12, 0);
        d.Children.Add(t);
        var card = Ui.Card(d, Ui.AccentSoft, 16); card.BorderBrush = Ui.Brush(Ui.Accent); card.Margin = new Thickness(0, 10, 0, 0);
        invite.Content = card;
    }

    static void OpenTile(string tag)
    {
        if (tag == "rewards") { MainView.Current.Push(new RewardsPage()); return; }
        if (tag == "longdiv") { MainView.Current.Push(new LongDivPage()); return; }
        if (tag.StartsWith("practice:")) { GamePage.Start("practice", tag.Substring(9)); return; }
        GamePage.Start(tag, null);
    }

    /// <summary>A progress ring's arc (0..1) in a box of centre c and radius r.</summary>
    public static Geometry Arc(double fraction, double c, double r)
    {
        if (fraction <= 0) return null;
        if (fraction >= 0.999) return new EllipseGeometry(new Rect(c - r, c - r, 2 * r, 2 * r));
        double a = 2 * Math.PI * fraction - Math.PI / 2;
        var end = new Point(c + r * Math.Cos(a), c + r * Math.Sin(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment { Point = end, Size = new Size(r, r), IsLargeArc = fraction > 0.5, SweepDirection = SweepDirection.Clockwise });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }
}

// ================================================================ results after a math game
public class ResultsPage : IPage
{
    readonly GameState G;
    public string Title => "Math";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    public int Stars { get; }
    readonly TextBlock netPlaces = Ui.Text("", 16), netCount = Ui.Text("", 18, "#FDE68A", true);

    public ResultsPage(GameState g, string title, string text)
    {
        G = g;
        var me = Lg.Me;
        int total = G.Right + G.Wrong;
        double acc = total > 0 ? G.Right / (double)total : 0;
        Stars = G.Spectator ? 0 : acc >= 0.9 && (total >= 5 || G.Kind == "longdiv") ? 3 : acc >= 0.7 ? 2 : G.Right > 0 ? 1 : 0;
        if (me != null && me.Grade > 0)
        {
            me.Stars += Stars;
            if (G.Kind != "practice" && G.Score > me.Best(G.Kind)) me.SetBest(G.Kind, G.Score);
        }
        if (me != null) Profiles.Save();

        var sp = new StackPanel();
        var t = Ui.Text(title, 30, Ui.TextC, true); t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center;
        sp.Children.Add(t);
        var stars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) };
        for (int i = 1; i <= 3; i++) stars.Children.Add(new TextBlock { Text = "★", FontSize = 54, Margin = new Thickness(6, 0), Foreground = Ui.Brush(i <= Stars ? Ui.Yellow : Ui.BorderStrong) });
        sp.Children.Add(stars);
        string line = G.Spectator ? text : text + "\n" + G.Right + " right out of " + total + (G.BestStreak > 2 ? "  |  best streak " + G.BestStreak : "") + "  |  score " + G.Score;
        var tt = Ui.Text(line, 17); tt.TextAlignment = TextAlignment.Center; tt.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(tt);
        if (G.LevelUps.Count > 0) { var lu = Ui.Text("Level up: " + string.Join(", ", G.LevelUps.Distinct()) + "!", 15, Ui.AccentText); lu.TextAlignment = TextAlignment.Center; lu.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(lu); }
        netPlaces.TextAlignment = TextAlignment.Center; netPlaces.Margin = new Thickness(0, 12, 0, 0); netPlaces.IsVisible = false;
        netCount.TextAlignment = TextAlignment.Center; netCount.Margin = new Thickness(0, 8, 0, 0); netCount.IsVisible = false;
        sp.Children.Add(netPlaces); sp.Children.Add(netCount);
        var reward = RewardCard(G);
        if (reward != null) sp.Children.Add(reward);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        if (!G.Net)
        {
            var again = Ui.Button("Play again", true, 17); again.Margin = new Thickness(0, 0, 10, 0);
            again.Click += (s, e) => { if (G.Kind == "longdiv") MainView.Current.Replace(new LongDivPage()); else { MainView.Current.Back(); GamePage.Start(G.Kind, G.Skill); } };
            buttons.Children.Add(again);
        }
        var home = Ui.Button("Home", false, 17);
        home.Click += (s, e) => GoHome();
        buttons.Children.Add(home);
        sp.Children.Add(buttons);
        var card = Ui.Card(sp, Ui.Surface, 26);
        card.MaxWidth = 900;
        View = Ui.Page(card, 920);
        if (Stars >= 2) Snd.Win();
        UpdateNet();
        View.AttachedToVisualTree += (s, e) => { Family.Changed += UpdateNet; Family.GoNext += GoNext; };
        View.DetachedFromVisualTree += (s, e) => { Family.Changed -= UpdateNet; Family.GoNext -= GoNext; };
    }

    void UpdateNet()
    {
        var r = Family.Round;
        if (r == null) { netPlaces.IsVisible = false; netCount.IsVisible = Family.Mode == NetMode.None && G.Net && netCount.Text.Length > 0; if (G.Net && Family.Mode == NetMode.None) netCount.Text = "The game on the network has ended."; return; }
        netPlaces.Text = r.PlacesText(Family.SelfId.ToString());
        netCount.Text = r.CountdownText;
        netPlaces.IsVisible = true; netCount.IsVisible = true;
    }

    /// <summary>Everyone back to the lobby together.</summary>
    void GoNext()
    {
        if (MainView.Current.Page != this || Family.Mode == NetMode.None) return;
        if (Family.Mode == NetMode.Host && Lg.Me.Grade == 0) { MainView.Current.BackTo<ParentPage>(); return; }
        if (Family.Mode == NetMode.Client && Family.Session == "Family room") { MainView.Current.BackTo<MathHomePage>(); return; }
        MainView.Current.BackTo<LobbyPage>();
        if (MainView.Current.Page is not LobbyPage) MainView.Current.Push(new LobbyPage());
    }

    static void GoHome()
    {
        if (Lg.Me != null && Lg.Me.Grade == 0 && Family.Mode == NetMode.Host) { MainView.Current.BackTo<ParentPage>(); return; }
        var mv = MainView.Current;
        if (mv.Has<MathHomePage>()) mv.BackTo<MathHomePage>();
        else if (mv.Has<HubPage>()) mv.BackTo<HubPage>();
        else mv.Back();   // the Workshop opened from the home page's Math: back there
    }

    /// <summary>The build or lab card under the results (players with a grade only).</summary>
    static Control RewardCard(GameState g)
    {
        var me = Lg.Me;
        if (me == null || me.Grade <= 0 || g.Spectator) return null;
        int grade = me.Grade, now = me.RewardPoints;
        string msg = Rewards.Unlocked(grade, g.PointsStart, now);
        if (string.IsNullOrEmpty(msg)) msg = Rewards.Builds(grade) ? "Every right answer places a block on your build. Keep going!" : "Every few right answers unlock a new lab sample. Keep going!";
        var sp = new StackPanel();
        var m = Ui.Text(msg, 17, "#FDE68A", true); m.TextAlignment = TextAlignment.Center; sp.Children.Add(m);
        var pic = Rewards.Picture(grade, now, false);
        if (pic != null) { pic.HorizontalAlignment = HorizontalAlignment.Center; pic.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(new Viewbox { Child = pic, StretchDirection = StretchDirection.DownOnly, MaxHeight = 260 }); }
        var st = Ui.Text(Rewards.Status(grade, now), 13, Ui.Muted); st.TextAlignment = TextAlignment.Center; st.Margin = new Thickness(0, 8, 0, 0); sp.Children.Add(st);
        return new Border { Child = sp, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16, 12), CornerRadius = new CornerRadius(14), Background = Ui.Brush("#14181F"), BorderBrush = Ui.Brush("#2F3644"), BorderThickness = new Thickness(1) };
    }
}

// ================================================================ My Build / My Lab
public class RewardsPage : IPage
{
    public string Title => "Math";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();

    public RewardsPage()
    {
        var me = Lg.Me;
        int grade = me.Grade, pts = me.RewardPoints;
        bool builds = Rewards.Builds(grade);
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(me.Name + (builds ? "'s build" : "'s lab"), 30, Ui.TextC, true));
        var sub = Ui.Text(Rewards.Status(grade, pts) + (builds ? "  |  Every right answer in math places one block; 5 in a row gives 2 bonus blocks." : "  |  Every " + Rewards.PointsPerSample + " right answers in math unlock a sample; 5 in a row gives a bonus."), 15, Ui.Muted);
        sub.Margin = new Thickness(0, 4, 0, 18); sp.Children.Add(sub);
        var pic = Rewards.Picture(grade, pts, true);
        var picBox = Ui.Card(new Viewbox { Child = pic, StretchDirection = StretchDirection.DownOnly }, Ui.Surface, 20);
        sp.Children.Add(picBox);
        if (!builds)
        {
            int n = Rewards.LabUnlocked(pts);
            var h = Ui.Text("Sample cards", 18, Ui.TextC, true); h.Margin = new Thickness(0, 16, 0, 8); sp.Children.Add(h);
            var cards = new List<Control>();
            int i = 0;
            foreach (var it in Rewards.Lab)
            {
                var c = new StackPanel();
                if (i < n)
                {
                    c.Children.Add(Ui.Text(it.Kind == "element" ? it.Name + " (" + it.Symbol + ", atomic number " + it.Z + ")" : it.Name + " (terrarium)", 15, Ui.TextC, true));
                    c.Children.Add(Ui.Text(it.Fact, 13, Ui.Muted));
                }
                else c.Children.Add(Ui.Text("Locked: keep answering to unlock it", 14, Ui.Dim));
                var b = new Border { Child = c, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush("#262B36"), BorderThickness = new Thickness(1) };
                cards.Add(b);
                i++;
            }
            sp.Children.Add(Ui.Tiles(cards, 320, 8));
        }
        else
        {
            var t = Ui.Text("Builds: " + string.Join(", then ", Rewards.Plans.Select(pl => pl.Name + " (" + pl.Count + " blocks)")) + ".", 14, Ui.Muted);
            t.Margin = new Thickness(0, 16, 0, 0); sp.Children.Add(t);
        }
        View = Ui.Page(sp);
    }
}

// ================================================================ the math progress report
public class ProgressPage : IPage
{
    public string Title => "Math progress";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel report = new();
    readonly StackPanel kidsList = new() { Spacing = 4 };
    string selected;

    public ProgressPage(bool everyone)
    {
        var kids = everyone ? Profiles.All.Where(p => p.Grade > 0).ToList() : new List<Player> { Lg.Me };
        foreach (var k in kids)
        {
            var kk = k;
            var b = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Ellipse { Width = 12, Height = 12, Fill = Ui.Brush(k.Color), VerticalAlignment = VerticalAlignment.Center }, Ui.Text(k.Name + "  (" + Profiles.GradeName(k.Grade) + ")", 15) } },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8), MinHeight = 44, Background = Ui.Brush(Ui.Surface2), CornerRadius = new CornerRadius(8), Tag = k.Id,
            };
            b.Click += (s, e) => Show(kk.Id);
            kidsList.Children.Add(b);
        }
        var root = new StackPanel();
        if (kids.Count > 1) { root.Children.Add(Lg.H2("Players")); kidsList.Margin = new Thickness(0, 0, 0, 14); root.Children.Add(kidsList); }
        root.Children.Add(report);
        View = Ui.Page(root);
        if (kids.Count > 0) Show(kids[0].Id);
        else report.Children.Add(Ui.Text("No children yet: add a player with an age under 18.", 16, Ui.Muted));
    }

    public string Selected => selected;

    StackPanel Card(string title)
    {
        var sp = new StackPanel();
        if (!string.IsNullOrEmpty(title)) { var h = Ui.Text(title, 18, Ui.TextC, true); h.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(h); }
        var c = Ui.Card(sp, Ui.Surface, 16); c.Margin = new Thickness(0, 0, 0, 12);
        report.Children.Add(c);
        return sp;
    }

    public void Show(string id)
    {
        selected = id;
        foreach (var b in kidsList.Children.OfType<Button>()) b.BorderBrush = Ui.Brush((string)b.Tag == id ? Ui.Accent : Ui.BorderC);
        report.Children.Clear();
        var p = Profiles.Find(id); if (p == null) return;
        int grade = p.Grade;
        var ids = Problems.SkillsFor(grade).ToList();
        int week = 0, weekRight = 0, daysOn = 0;
        for (int i = 0; i < 7; i++)
        {
            var d = p.Day(DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            if (d.Done > 0) { daysOn++; week += d.Done; weekRight += d.Right; }
        }
        var top = Card("");
        top.Children.Add(Ui.Text(p.Name + "  -  " + Profiles.GradeName(grade) + " math", 24, Ui.TextC, true));
        var s1 = Ui.Text("Last 7 days: " + week + " answers on " + daysOn + " day" + (daysOn != 1 ? "s" : "") + (week > 0 ? ", " + 100 * weekRight / week + "% right" : "") + "  |  day streak " + p.DayStreak + "  |  " + p.Stars + " stars", 14, Ui.Muted);
        s1.Margin = new Thickness(0, 6, 0, 0); top.Children.Add(s1);
        var s2 = Ui.Text("Reward: " + Rewards.Status(grade, p.RewardPoints), 14, "#FDE68A"); s2.Margin = new Thickness(0, 4, 0, 0); top.Children.Add(s2);
        int tried = ids.Count(x => (p.SkillIfAny(x)?.Attempts ?? 0) > 0);
        int mastered = ids.Count(x => SkillStat.State(p.SkillIfAny(x)) == "mastered");
        var s3 = Ui.Text("Skills tried: " + tried + " of " + ids.Count + "  |  mastered: " + mastered, 14, Ui.Muted); s3.Margin = new Thickness(0, 4, 0, 0); top.Children.Add(s3);

        var needs = ids.Where(x => SkillStat.State(p.SkillIfAny(x)) == "needs work").OrderBy(x => p.SkillIfAny(x).Accuracy).ToList();
        var good = ids.Where(x => SkillStat.State(p.SkillIfAny(x)) == "mastered").ToList();
        var none = ids.Where(x => SkillStat.State(p.SkillIfAny(x)) == "not tried").ToList();
        var c = Card("Needs some help");
        if (needs.Count > 0)
            foreach (var x in needs) { var st = p.SkillIfAny(x); var t = Ui.Text(Profiles.SkillName(x) + ": " + (int)(100 * st.Accuracy) + "% right lately (level " + st.Level + ")  -  " + Problems.Info(x)?.Std, 14, Ui.Yellow); t.Margin = new Thickness(0, 2); c.Children.Add(t); }
        else c.Children.Add(Ui.Text("Nothing stands out right now. (A skill shows here after 5 answers with fewer than 7 in 10 right.)", 14, Ui.Muted));
        if (good.Count > 0) { var t = Ui.Text("Going well: " + string.Join(", ", good.Select(Profiles.SkillName)), 14, Ui.Green); t.Margin = new Thickness(0, 10, 0, 0); c.Children.Add(t); }
        if (none.Count > 0) { var t = Ui.Text("Not tried yet: " + string.Join(", ", none.Select(Profiles.SkillName)), 13, Ui.Muted); t.Margin = new Thickness(0, 10, 0, 0); c.Children.Add(t); }

        c = Card("Every skill");
        foreach (var area in Problems.AreasFor(grade))
        {
            var h = Ui.Text(area, 14, Ui.AccentText, true); h.Margin = new Thickness(0, 10, 0, 4); c.Children.Add(h);
            foreach (var x in Problems.SkillsIn(grade, area))
            {
                var st = p.SkillIfAny(x);
                string state = SkillStat.State(st);
                string color = state switch { "mastered" => Ui.Green, "needs work" => Ui.Yellow, "not tried" => Ui.Dim, _ => Ui.TextC };
                var row = new WrapPanel { Margin = new Thickness(0, 2) };
                var nm = Ui.Text(Profiles.SkillName(x), 13, color, true); nm.Margin = new Thickness(0, 0, 10, 0); nm.TextWrapping = TextWrapping.NoWrap; row.Children.Add(nm);
                var parts = new List<string> { Problems.Info(x)?.Std ?? "" };
                if (st != null && st.Attempts > 0) { parts.Add("level " + st.Level); parts.Add(st.Attempts + " tried"); parts.Add((int)(100 * st.Accuracy) + "% lately"); }
                parts.Add(st != null && st.Last.Length > 0 ? "last " + st.Last : state);
                var rest = Ui.Text(string.Join("   ", parts.Where(z => z.Length > 0)), 13, Ui.Muted); rest.TextWrapping = TextWrapping.Wrap;
                row.Children.Add(rest);
                c.Children.Add(row);
            }
        }

        var mine = p.Mistakes.Where(m => ids.Contains(m.Skill)).TakeLast(8).ToList();
        if (mine.Count > 0)
        {
            c = Card("Recent math mistakes");
            foreach (var m in mine.OrderByDescending(m => m.When, StringComparer.Ordinal))
            {
                c.Children.Add(Ui.Text(m.When + "  " + Profiles.SkillName(m.Skill) + ": " + m.Text, 13));
                var a = Ui.Text("answered " + m.Given + ", the answer was " + m.Answer, 13, Ui.Yellow); a.Margin = new Thickness(0, 0, 0, 6); c.Children.Add(a);
            }
        }
        if (View is ScrollViewer sv) sv.Offset = new Vector(0, 0);
    }
}
