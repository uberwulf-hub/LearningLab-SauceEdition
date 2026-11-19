// Learning Games - the Long Division Workshop page, the parent view (family room, each child's skills and answers,
// hints) and the lobby where a child hosts or joins a race or battle on the home network.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using LearningGames;
using LearningLab.Views;

namespace LearningLab.LearningGames;

// ================================================================ Long Division Workshop (6th grade)
public class LongDivPage : IPage
{
    public string Title => "Long Division Workshop";
    public Control View { get; }
    public Control Extras { get; }
    public GameState G { get; } = new() { Kind = "longdiv", Skill = "longdiv", Rounds = 3, Lives = 0 };
    public LongDivisionView Sheet { get; } = new();
    readonly TextBlock count = Ui.Text("", 14, Ui.Muted);
    readonly Button guidedBtn = Ui.Button("Guided"), practiceBtn = Ui.Button("Practice");
    bool guided = true, recorded;
    LongDivision current;

    public LongDivPage()
    {
        var me = Lg.Me;
        G.PointsStart = me != null && me.Grade > 0 ? me.RewardPoints : 0;
        ToolTip.SetTip(guidedBtn, "Shows which digit to work on and explains every step");
        ToolTip.SetTip(practiceBtn, "Just names the step: you do the thinking");
        guidedBtn.Click += (s, e) => SetMode(true);
        practiceBtn.Click += (s, e) => SetMode(false);
        practiceBtn.Margin = new Thickness(6, 0, 0, 0);
        Extras = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { guidedBtn, practiceBtn } };
        var head = new Border { Child = count, Padding = new Thickness(16, 8), Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1) };
        var d = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        d.Children.Add(head);
        d.Children.Add(Sheet);
        View = d;
        if (Services.Platform.Touch) Sheet.ShowKeypad = true;
        Sheet.Answered += (s, e) => { if (e.Done) Complete(); };
        Sheet.Finished += (s, e) => Next();
        SetMode(true);
        Next();
    }

    public void Closed() { G.Over = true; Profiles.Save(); }

    void SetMode(bool g)
    {
        guided = g;
        guidedBtn.Background = Ui.Brush(g ? Ui.Accent : Ui.Surface3);
        practiceBtn.Background = Ui.Brush(!g ? Ui.Accent : Ui.Surface3);
        if (Sheet.Ld != null) Sheet.Load(Sheet.Ld, g);
    }

    void Next()
    {
        if (G.Round >= G.Rounds) { G.Over = true; MainView.Current.Replace(new ResultsPage(G, "Workshop done!", "Long Division Workshop")); return; }
        G.Round++;
        var st = Lg.Me.Skill("longdiv");
        bool story = G.Round == 2 || (st.Level >= 3 && G.Round == 3 && Lg.Rand.Next(2) == 0);   // the second problem is a story
        current = global::LearningGames.Problems.MakeLongDivision(st.Level, story);
        recorded = false;
        count.Text = "Problem " + G.Round + " of " + G.Rounds + "  |  level " + st.Level;
        Sheet.Load(current, guided);
    }

    /// <summary>A finished problem counts as right with at most one slip; every one earns 2 more reward points.</summary>
    void Complete()
    {
        var ld = current;
        if (ld == null || recorded) return;
        recorded = true;
        bool ok = ld.Mistakes <= 1;
        var me = Lg.Me;
        string change = me.RecordAnswer("longdiv", ok, ld.Dividend + " ÷ " + ld.Divisor + " (Workshop)", ld.Summary(), ok ? ld.Summary() : ld.Mistakes + " slips");
        Family.SendActivity(me, "longdiv", ld.Dividend + " ÷ " + ld.Divisor + " (Workshop)", ld.Summary(), ok ? ld.Summary() : ld.Mistakes + " slips", ok);
        if (change == "up") G.LevelUps.Add("Long division");
        if (me.Grade > 0) me.AddRewardPoints(2);
        if (ok) { G.Right++; G.Streak++; G.BestStreak = Math.Max(G.BestStreak, G.Streak); G.Score += 20 + (ld.Mistakes == 0 ? 10 : 0); }
        else { G.Wrong++; G.Streak = 0; G.Score += 5; }
    }

    /// <summary>For the checks: answers the whole problem right and moves on.</summary>
    public void SolveForTest() { Sheet.Advance(Sheet.Ld.StepCount); Next(); }
}

// ================================================================ the parent view
public class ParentPage : IPage
{
    public string Title => "Parent view";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel kids = new() { Spacing = 4 };
    readonly TextBlock roomText = Ui.Text("", 13, Ui.Muted);
    readonly Button roomBtn = Ui.Button("Open the family room", true);
    readonly StackPanel roomGames = new() { Margin = new Thickness(0, 12, 0, 0) };
    readonly CheckBox plays = new() { Content = "I play too (6th-grade problems)", Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock kidName = Ui.Text("Select a child", 22, Ui.TextC, true), kidSummary = Ui.Text("", 13, Ui.Muted), kidLive = Ui.Text("", 13, Ui.Muted);
    readonly StackPanel skills = new(), feed = new();
    readonly TextBlock feedTitle = Lg.H2("Recent mistakes");
    readonly TextBox hint = new() { FontSize = 15, PlaceholderText = "A hint or message; it pops up on the child's screen" };
    readonly Border sendBox;
    string selected = "";

    public ParentPage()
    {
        roomBtn.HorizontalAlignment = HorizontalAlignment.Stretch; roomBtn.Margin = new Thickness(0, 12, 0, 0);
        roomBtn.Click += (s, e) =>
        {
            if (Family.Mode == NetMode.Host) Family.Stop();
            else { string err = Family.StartHosting(Lg.Me, "Family room"); if (err.Length > 0) MainView.Current.Message("", err); }
            Update();
        };
        var report = Ui.Button("Math progress report..."); report.HorizontalAlignment = HorizontalAlignment.Stretch; report.Margin = new Thickness(0, 8, 0, 0);
        report.Click += (s, e) => MainView.Current.Push(new ProgressPage(true));
        var race = Ui.Button("Math Race"); race.HorizontalAlignment = HorizontalAlignment.Stretch; race.Margin = new Thickness(0, 0, 4, 0);
        var bossB = Ui.Button("Boss Battle"); bossB.HorizontalAlignment = HorizontalAlignment.Stretch; bossB.Margin = new Thickness(4, 0, 0, 0);
        race.Click += (s, e) => StartForAll("race");
        bossB.Click += (s, e) => StartForAll("boss");
        plays.IsChecked = Family.ParentPlays;
        plays.IsCheckedChanged += (s, e) => Family.SetParentPlays(Lg.Me, plays.IsChecked == true);
        roomGames.Children.Add(Ui.Text("Start a game for everyone", 13, Ui.Muted));
        var two = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0), Children = { race, bossB } };
        roomGames.Children.Add(two);
        roomGames.Children.Add(plays);
        var room = Ui.Card(new StackPanel { Children = { Lg.H2("Family room"), roomText, roomBtn, report, roomGames } });
        room.Margin = new Thickness(0, 0, 0, 12);
        var left = new StackPanel { Children = { room, Lg.H2("Kids"), kids } };

        var who = new DockPanel();
        DockPanel.SetDock(kidLive, Dock.Right); kidLive.VerticalAlignment = VerticalAlignment.Center;
        who.Children.Add(kidLive);
        kidSummary.Margin = new Thickness(0, 4, 0, 0);
        who.Children.Add(new StackPanel { Children = { kidName, kidSummary } });
        var whoCard = Ui.Card(who); whoCard.Margin = new Thickness(0, 0, 0, 12);
        var skillCard = Ui.Card(new StackPanel { Children = { Lg.H2("Skills"), skills } }); skillCard.Margin = new Thickness(0, 0, 0, 12);
        var feedCard = Ui.Card(new StackPanel { Children = { feedTitle, feed } }); feedCard.Margin = new Thickness(0, 0, 0, 12);
        var send = Ui.Button("Send hint", true); send.Margin = new Thickness(8, 4, 0, 4);
        send.Click += (s, e) => { Send(hint.Text); hint.Text = ""; };
        hint.KeyDown += (s, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Send(hint.Text); hint.Text = ""; } };
        var cheer = Ui.Button("Great job!"); cheer.Margin = new Thickness(8, 4, 0, 4); cheer.Click += (s, e) => Send("Great job! I'm proud of you!");
        var cheer2 = Ui.Button("Keep going!"); cheer2.Margin = new Thickness(8, 4, 0, 4); cheer2.Click += (s, e) => Send("Keep going, you've got this!");
        sendBox = Ui.Card(new StackPanel { Children = { hint, new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { send, cheer, cheer2 } } } });
        sendBox.IsEnabled = false;
        var right = new StackPanel { Children = { whoCard, sendBox, skillCard, feedCard } };
        sendBox.Margin = new Thickness(0, 0, 0, 12);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1); right.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(right);
        View = Ui.Page(grid, 1300);
        View.SizeChanged += (s, e) =>
        {
            bool n = e.NewSize.Width < 760;
            grid.ColumnDefinitions = new ColumnDefinitions(n ? "*" : "300,*");
            grid.RowDefinitions = new RowDefinitions(n ? "Auto,Auto" : "Auto");
            Grid.SetColumn(right, n ? 0 : 1); Grid.SetRow(right, n ? 1 : 0);
            right.Margin = new Thickness(n ? 0 : 14, n ? 12 : 0, 0, 0);
        };
        View.AttachedToVisualTree += (s, e) => { Family.Changed += Update; Update(); };
        View.DetachedFromVisualTree += (s, e) => Family.Changed -= Update;
        Update();
    }

    void StartForAll(string game)
    {
        if (!Family.StartGameForAll(game)) MainView.Current.Message("", "Nobody has joined yet.");
    }

    void Send(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || Family.Mode != NetMode.Host || !selected.StartsWith("net:")) return;
        Family.SendHint(int.Parse(selected.Substring(4)), text, Lg.Me.Name);
        MainView.Current.Message("", "Sent.");
    }

    void Update()
    {
        bool host = Family.Mode == NetMode.Host;
        roomBtn.Content = host ? "Close the family room" : "Open the family room";
        roomGames.IsVisible = host;
        roomText.Text = host ? "Open on " + Lan.LocalAddresses() + ". The kids see an invitation on their home screen." : "Open the family room so the kids' devices can join. You'll see what they're working on and can send hints.";
        kids.Children.Clear();
        // kids connected now, then kids with a profile on this device
        foreach (var p in Family.Players.Values)
        {
            if (p.Id == 0) continue;
            kids.Children.Add(KidButton("net:" + p.Id, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Ellipse { Width = 10, Height = 10, Fill = Ui.Brush(Ui.Green), VerticalAlignment = VerticalAlignment.Center }, Ui.Text(p.Name + "  (online, " + Profiles.GradeName(p.Grade) + ")", 15) } }));
        }
        foreach (var p in Profiles.All.Where(x => x.Grade > 0))
        {
            if (Family.Players.Values.Any(x => x.Id != 0 && x.Name == p.Name)) continue;   // online now
            kids.Children.Add(KidButton("local:" + p.Id, Ui.Text(p.Name + "  (" + Profiles.GradeName(p.Grade) + ", this device)", 15, Ui.Muted)));
        }
        if (selected.Length > 0) ShowKid(selected);
        MainView.Current?.RefreshBar();
    }

    Button KidButton(string key, Control content)
    {
        var b = new Button { Content = content, Tag = key, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8), MinHeight = 44, CornerRadius = new CornerRadius(8), Background = Ui.Brush(key == selected ? Ui.AccentSoft : Ui.Surface2) };
        b.Click += (s, e) => { selected = key; Update(); };
        return b;
    }

    public void ShowKid(string key)
    {
        selected = key;
        skills.Children.Clear(); feed.Children.Clear();
        string[] kv = key.Split(':', 2);
        System.Text.Json.Nodes.JsonObject sum; int grade;
        if (kv[0] == "net")
        {
            if (!Family.Players.TryGetValue(int.Parse(kv[1]), out var p)) return;
            sum = p.Summary; grade = p.Grade;
            kidName.Text = p.Name; kidLive.Text = p.Current.Length > 0 ? "Now: " + p.Current : "";
            sendBox.IsEnabled = true;
            feedTitle.Text = "Live answers";
            foreach (var f in p.Feed.TakeLast(40).Reverse()) Feed(f.Text, f.Given, f.Answer, f.Ok, f.When);
            kidSummary.Text = sum != null ? Profiles.GradeName(grade) + "  |  " + Player.Int(sum["stars"]) + " stars  |  " + Player.Int(sum["today"]) + " answers today  |  " + Player.Int(sum["streak"]) + " day streak" : "";
        }
        else
        {
            var pr = Profiles.Find(kv[1]); if (pr == null) return;
            kidName.Text = pr.Name; kidLive.Text = ""; sendBox.IsEnabled = false;
            feedTitle.Text = "Recent mistakes";
            foreach (var m in pr.Mistakes.TakeLast(25).OrderByDescending(m => m.When, StringComparer.Ordinal)) Feed(m.Text, m.Given, m.Answer, false, m.When);
            sum = Family.Summary(pr); grade = pr.Grade;
            kidSummary.Text = Profiles.GradeName(grade) + "  |  " + pr.Stars + " stars  |  " + Player.Int(sum["today"]) + " answers today  |  " + Player.Int(sum["streak"]) + " day streak";
        }
        var sk = sum?["skills"] as System.Text.Json.Nodes.JsonObject;
        var ids = global::LearningGames.Problems.SkillsFor(grade).Concat(new[] { "line" }).Concat(grade <= 2 ? new[] { "fractions2" } : Array.Empty<string>());
        foreach (var sid in ids)
        {
            var s = sk?[sid] as System.Text.Json.Nodes.JsonObject;
            int att = s == null ? 0 : Player.Int(s["Attempts"]);
            bool mastered = s != null && s["Mastered"] is System.Text.Json.Nodes.JsonValue mv && mv.TryGetValue(out bool mb) && mb;
            double acc = s == null ? 0 : Player.Dbl(s["Acc"]);
            string state = att == 0 ? "not tried" : mastered ? "mastered" : "level " + Player.Int(s["Level"], 1) + ", " + (int)(100 * acc) + "% right";
            var lv = Ui.Text(state, 12.5, mastered ? Ui.Green : att > 0 && acc < 0.6 ? Ui.Yellow : Ui.Muted);
            var row = new DockPanel { Margin = new Thickness(0, 4) };
            DockPanel.SetDock(lv, Dock.Right); lv.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(lv); row.Children.Add(Ui.Text(Profiles.SkillName(sid), 14));
            skills.Children.Add(row);
        }
    }

    void Feed(string text, string given, string answer, bool ok, string when)
    {
        var top = new DockPanel();
        var w = Ui.Text(when, 11.5, Ui.Dim); DockPanel.SetDock(w, Dock.Right); w.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(w); top.Children.Add(Ui.Text(text, 14));
        feed.Children.Add(new StackPanel { Margin = new Thickness(0, 3), Children = { top, Ui.Text(ok ? "✓ " + given : "answered " + given + ", correct: " + answer, 13, ok ? Ui.Green : Ui.Yellow) } });
    }
}

// ================================================================ the lobby (a child hosting or joining a race / battle)
public class LobbyPage : IPage
{
    public string Title => "Play with family";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel hosts = new() { Spacing = 4 };
    readonly TextBox addr = new() { PlaceholderText = "Or type the other device's address (e.g. 192.168.1.20)" };
    readonly TextBlock status = Ui.Text("", 14, Ui.AccentText);
    readonly StackPanel players = new();
    readonly Button start = Ui.Button("Start!", true, 18);
    FoundHost picked;

    public LobbyPage()
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(Ui.Text("Everyone needs Learning Games open on a device on the same home network (Learning Lab, or App Hub's Learning Games on a PC). One person hosts, the others join. This device: " + Lan.LocalAddresses(), 14, Ui.Muted));
        var look = Ui.Button("Look again"); look.Click += (s, e) => Family.Discover();
        var hh = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(look, Dock.Right); hh.Children.Add(look);
        var gl = Ui.Text("Games on your home network", 13, Ui.Muted); gl.VerticalAlignment = VerticalAlignment.Center; hh.Children.Add(gl);
        sp.Children.Add(hh);
        sp.Children.Add(hosts);
        var join = Ui.Button("Join", true); join.MinWidth = 100; join.Margin = new Thickness(8, 0, 0, 0);
        join.Click += (s, e) => Join();
        var jr = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(join, Dock.Right); jr.Children.Add(join); jr.Children.Add(addr);
        sp.Children.Add(jr);
        sp.Children.Add(new Border { Height = 1, Background = Ui.Brush(Ui.BorderC), Margin = new Thickness(0, 14) });
        sp.Children.Add(Ui.Text("Or host a game here and let others join", 13, Ui.Muted));
        var hr = Ui.Button("Host Math Race"); hr.Margin = new Thickness(0, 0, 8, 6);
        var hb = Ui.Button("Host Boss Battle"); hb.Margin = new Thickness(0, 0, 8, 6);
        hr.Click += (s, e) => Host("Math Race", "race");
        hb.Click += (s, e) => Host("Boss Battle", "boss");
        sp.Children.Add(new WrapPanel { Children = { hr, hb } });
        status.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(status);
        sp.Children.Add(players);
        start.HorizontalAlignment = HorizontalAlignment.Right; start.IsVisible = false;
        start.Click += (s, e) => { if (!Family.StartGameForAll(Family.HostGame)) MainView.Current.Message("", "Nobody has joined yet."); };
        sp.Children.Add(start);
        var card = Ui.Card(sp, Ui.Surface, 22); card.MaxWidth = 660;
        View = Ui.Page(card, 700);
        View.AttachedToVisualTree += (s, e) => { Family.Changed += Update; Family.Discover(); Update(); };
        View.DetachedFromVisualTree += (s, e) => Family.Changed -= Update;
        Update();
    }

    public void Closed() { if (Family.Mode == NetMode.Host) Family.Stop(); }


    void Host(string session, string game)
    {
        string err = Family.StartHosting(Lg.Me, session);
        if (err.Length > 0 && Family.Mode != NetMode.Host) { MainView.Current.Message("", err); return; }
        Family.HostGame = game;
        Update();
    }

    void Join()
    {
        string a = (addr.Text ?? "").Trim(); int port = Lan.GamePort;
        if (a.Length == 0 && picked != null) { a = picked.Address; port = picked.Port; }
        if (a.Length == 0) { MainView.Current.Message("", "Pick a game from the list, or type the address of the device hosting it."); return; }
        status.Text = "Connecting to " + a + "...";
        string err = Family.Join(Lg.Me, a, port);
        if (err.Length > 0) { status.Text = ""; MainView.Current.Message("", err); }
        Update();
    }

    void Update()
    {
        hosts.Children.Clear();
        foreach (var h in Family.Hosts)
        {
            var hh = h;
            var b = new Button { Content = Ui.Text(h.Name + "  -  " + h.Session + "  (" + h.Players + " playing)  " + h.Address, 15), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 44, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(8), Background = Ui.Brush(picked == h ? Ui.AccentSoft : Ui.Surface2) };
            b.Click += (s, e) => { picked = hh; Update(); };
            hosts.Children.Add(b);
        }
        if (Family.Hosts.Length == 0) hosts.Children.Add(Ui.Text("No games found yet.", 14, Ui.Dim));
        players.Children.Clear();
        foreach (var p in Family.Players.Values) players.Children.Add(Ui.Text("   " + p.Name + (p.Grade > 0 ? "  (" + Profiles.GradeName(p.Grade) + ")" : ""), 16, p.Color, true));
        if (Family.Mode == NetMode.Host) { status.Text = "Hosting " + Family.Session + ". Waiting for players... (" + Family.Players.Count + " ready)"; start.IsVisible = Family.Players.Count >= 2; }
        else if (Family.Mode == NetMode.Client) { status.Text = "Joined " + Family.HostName + "'s " + Family.Session + ". Waiting for " + Family.HostName + " to start..."; start.IsVisible = false; }
        else start.IsVisible = false;
        MainView.Current?.RefreshBar();
    }
}
