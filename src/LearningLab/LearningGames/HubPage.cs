// Learning Games - the hub: hello, the day's streak, a tile per subject (and the parent view for grown-ups), a nudge
// when questions are due for review, and on Windows the block games (Andrew's web games, opened in their own window).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.LearningGames;

public class HubPage : IPage
{
    public string Title => "Learning Games";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel body = new();

    public HubPage()
    {
        View = Ui.Page(body);
        Fill();
        View.AttachedToVisualTree += (s, e) => Fill();
    }

    public void Closed() { Family.Stop(); }

    void Fill()
    {
        var p = Lg.Me;
        if (p == null) return;
        body.Children.Clear();
        var t = p.TodayCount;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        int ds = p.DayStreak;
        var streak = Ui.Card(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                Ui.Text("\U0001F525", 24, "#F97316"),
                new StackPanel { Children = { Ui.Text(ds > 0 ? ds + " day" + (ds != 1 ? "s" : "") + " in a row" : "Start a streak today", 16, Ui.TextC, true), Ui.Text(Math.Min(t.Done, Profiles.DailyGoal) + " of " + Profiles.DailyGoal + " answers today  |  " + p.Stars + " stars", 13, Ui.Muted) } },
            },
        }, Ui.Surface, 12);
        streak.VerticalAlignment = VerticalAlignment.Center;
        streak.Margin = new Thickness(0, 6, 0, 0);
        var hello = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        hello.Children.Add(Ui.Text("Hi, " + p.Name + "!", 30, Ui.TextC, true));
        hello.Children.Add(Ui.Text(t.Done == 0 ? "What do you want to learn today?" : t.Done < Profiles.DailyGoal ? "You've answered " + t.Done + " today. Keep it up!" : "Daily goal done. You are on fire!", 15, Ui.Muted));
        var top = new WrapPanel();
        top.Children.Add(hello);
        top.Children.Add(streak);
        body.Children.Add(top);

        var tiles = new List<Control>();
        foreach (var sub in Bank.Subjects)
        {
            string note = "", noteColor = Ui.AccentText;
            bool have = true;
            if (sub.Id != "math")
            {
                have = Bank.Get(sub.Id) != null;
                var st = p.Subject(sub.Id);
                int due = st.DueCount;
                note = Bank.LevelNames[st.Level] + (due > 0 ? "  |  " + due + " to review" : "");
                if (due > 0) noteColor = Ui.Yellow;
            }
            string id = sub.Id;
            var b = Lg.Tile(sub.Glyph, sub.Color, sub.Name, sub.Text, note, noteColor, () => Open(id), 176);
            if (!have) { b.IsEnabled = false; ToolTip.SetTip(b, "Questions for this subject are not installed yet."); }
            tiles.Add(b);
        }
        if (p.Grade == 0) tiles.Add(Lg.Tile("☺", "#06B6D4", "Parent view", "Everyone's progress and family games", "", "", () => MainView.Current.Push(new ParentPage()), 176));
        var grid = Ui.Tiles(tiles, 250);
        grid.Margin = new Thickness(-6, 12, -6, 0);
        body.Children.Add(grid);

        // review reminder: the subject with the most cards due
        Bank.SubjectInfo best = null; int most = 0;
        foreach (var sub in Bank.Subjects) if (sub.Id != "math") { int n = p.Subject(sub.Id).DueCount; if (n > most) { most = n; best = sub; } }
        if (best != null && most >= 3)
        {
            var go = Ui.Button("Review now", true);
            string bid = best.Id;
            go.Click += (s, e) => { var sp = new SubjectPage(bid); MainView.Current.Push(sp); sp.Start("cards", -1); };
            go.VerticalAlignment = VerticalAlignment.Center;
            var d = new DockPanel();
            DockPanel.SetDock(go, Dock.Right);
            d.Children.Add(go);
            var txt = new StackPanel { Margin = new Thickness(0, 0, 12, 0), Children = { Ui.Text("Time to review", 16, Ui.TextC, true), Ui.Text(most + " " + best.Name + " question" + (most != 1 ? "s are" : " is") + " ready for review. Reviewing at the right time is the best way to remember for good.", 13, Ui.Muted) } };
            d.Children.Add(txt);
            var card = Ui.Card(d, Ui.Surface, 16);
            card.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(card);
        }

        if (WebGames.Available)
        {
            var h = Lg.H2("Block games"); h.Margin = new Thickness(0, 26, 0, 2); body.Children.Add(h);
            body.Children.Add(Ui.Text("Minecraft- and Roblox-style games that open in their own window. Each one keeps its own progress on this device.", 13.5, Ui.Muted));
            var wt = new List<Control>();
            foreach (var g in WebGames.All) wt.Add(WebGames.Tile(g, p));
            var wg = Ui.Tiles(wt, 260);
            wg.Margin = new Thickness(-6, 8, -6, 0);
            body.Children.Add(wg);
        }
    }

    static void Open(string id)
    {
        if (id == "math") { MainView.Current.Push(new MathHomePage()); return; }
        MainView.Current.Push(new SubjectPage(id));
    }
}

/// <summary>The three block games (Content\LearningGames\Web\&lt;game&gt;\index.html): Windows opens them in a Microsoft
/// Edge app window with its own profile, so their progress stays apart from everyday browsing.</summary>
public static class WebGames
{
    public record Info(string Id, string Name, string Color, string Ages, string Subjects, string Text, string[] Art, Dictionary<char, string> Map);

    public static readonly Info[] All =
    {
        new("block-quest", "Block Quest", "#F2C230", "Ages 6-9", "Math, reading, science, social studies",
            "Mine blocks by answering questions, craft tools and dig deeper. Watch out for the creeper!",
            new[] { "GGGGGGGG", "GHGGGHGG", "HDHGDHDH", "DDDEDDDD", "DEDDDDED", "DDDDEDDD", "DDEDDDDE", "DDDDDDDD" },
            new() { ['G'] = "#5FA33A", ['H'] = "#4F8F2E", ['D'] = "#7B4B25", ['E'] = "#5E3719" }),
        new("mob-siege", "Mob Siege", "#FF5A5A", "Ages 6-9", "Math facts, reading",
            "Every right answer fires an arrow at the mobs marching on your base. Build towers and survive 12 waves.",
            new[] { ".GGGGGG.", ".GGGGGG.", ".KKGGKK.", ".KKGGKK.", "..GKKG..", "..KKKK..", "..KGGK..", ".GGGGGG.", ".GGGGGG.", ".GG..GG." },
            new() { ['G'] = "#4FB84A", ['K'] = "#0B0D12" }),
        new("word-obby", "Word Obby", "#FF4FA3", "Ages 6-9", "Reading stories, word work, math",
            "Jump from block to block over the lava. Read short stories, then answer to make each jump.",
            new[] { ".YYYYYY.", ".YYYYYY.", ".YKYYKY.", ".YYYYYY.", ".YYKKYY.", "..BBBB..", ".BBBBBB.", "GBBBBBBG", "G.BBBB.G", "..GGGG..", "..GG.GG.", "..GG.GG." },
            new() { ['Y'] = "#F9D34A", ['K'] = "#0B0D12", ['B'] = "#2F6FE0", ['G'] = "#3DDC5A" }),
    };

    /// <summary>Opens a game (the platform decides how); null on platforms that can't yet.</summary>
    public static Func<string, string, string> Opener { get; set; }
    /// <summary>The folder holding Web\&lt;game&gt;\index.html, or null.</summary>
    public static string Folder { get; set; }
    public static bool Available => Opener != null && Folder != null;

    public static Control Art(string[] rows, Dictionary<char, string> map, double px)
    {
        var c = new Canvas { Width = rows[0].Length * px, Height = rows.Length * px };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x];
                if (ch == '.' || !map.TryGetValue(ch, out var col)) continue;
                var r = new Rectangle { Width = px, Height = px, Fill = Ui.Brush(col) };
                Canvas.SetLeft(r, x * px); Canvas.SetTop(r, y * px);
                c.Children.Add(r);
            }
        return c;
    }

    public static Control Tile(Info g, Player p)
    {
        var sp = new StackPanel();
        var top = new DockPanel();
        double px = Math.Floor(44.0 / Math.Max(g.Art[0].Length, g.Art.Length));
        var art = Art(g.Art, g.Map, px); art.HorizontalAlignment = HorizontalAlignment.Center; art.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(8), Background = Ui.Brush(g.Color, 40), BorderBrush = Ui.Brush(g.Color), BorderThickness = new Thickness(2), Child = art };
        var age = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3), Background = Ui.Brush(g.Color, 40), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Child = Ui.Text(g.Ages, 12, g.Color, true) };
        DockPanel.SetDock(badge, Dock.Left);
        top.Children.Add(badge); top.Children.Add(age);
        sp.Children.Add(top);
        var n = Ui.Text(g.Name, 18, Ui.TextC, true); n.Margin = new Thickness(0, 12, 0, 4); sp.Children.Add(n);
        sp.Children.Add(Ui.Text(g.Text, 12.5, Ui.Muted));
        var s = Ui.Text(g.Subjects, 12, Ui.AccentText); s.Margin = new Thickness(0, 8, 0, 0); sp.Children.Add(s);
        var b = Ui.Tile(sp, Ui.BorderStrong, () => Open(g, p));
        b.MinHeight = 210; b.CornerRadius = new CornerRadius(18);
        ToolTip.SetTip(b, "Opens " + g.Name + " in its own window");
        return b;
    }

    static void Open(Info g, Player p)
    {
        string file = System.IO.Path.Combine(Folder, g.Id, "index.html");
        if (!File.Exists(file)) { MainView.Current.Message("", g.Name + " is missing from this copy of Learning Lab:\n" + file); return; }
        string url = new Uri(file).AbsoluteUri;
        if (p != null && !string.IsNullOrEmpty(p.Name)) url += "#player=" + Uri.EscapeDataString(p.Name);
        string err = Opener(url, g.Name);
        if (!string.IsNullOrEmpty(err)) MainView.Current.Message("", err);
    }
}
