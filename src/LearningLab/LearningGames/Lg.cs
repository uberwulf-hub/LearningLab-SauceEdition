// Learning Games - what the pages share: who is playing, the top bar's chips, reading aloud, picking the next skill,
// and the game tiles.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.LearningGames;

public static class Lg
{
    /// <summary>The player using the app now (null on the "Who's playing?" page).</summary>
    public static Player Me { get; set; }
    public static Random Rand { get; set; } = new();
    static bool loaded;

    /// <summary>Loads the players once (the first time Learning Games opens).</summary>
    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Profiles.Load();
        // a game started for everyone (a family room or a lobby) opens on any page; hints show wherever the child is
        Family.GameStarted += (game, list) => { if (MainView.Current.Page is GamePage gp && !gp.G.Over) return; GamePage.Start(game, null, list); };
        Family.HintArrived += (from, text) => { if (MainView.Current.Page is not GamePage) MainView.Current.Message(from + " says", text); };
    }

    /// <summary>For the checks: forget the players so the next EnsureLoaded reads them again.</summary>
    public static void Reset() { loaded = false; Me = null; Family.Stop(); }

    public record GameInfo(string Id, string Name, string Glyph, string Color, string Text);
    public static readonly GameInfo[] Games =
    {
        new("asteroid", "Asteroid Blaster", "☄", "#60A5FA", "Blast the rock with the right answer"),
        new("frog", "Number Line Frog", "→", "#22C55E", "Hop to the right spot on the line"),
        new("pizza", "Pizza Shop", "◔", "#F97316", "Fractions, money and orders"),
        new("race", "Math Race", "⚑", "#EAB308", "Answer fast to win the race"),
        new("boss", "Boss Battle", "⚔", "#EC4899", "Beat the monster with math"),
    };

    /// <summary>The extra tiles for a grade: Block World / Block Trader / My Build, or Long Division / Science Lab / My Lab.</summary>
    public static GameInfo[] GradeGames(int grade)
    {
        if (grade <= 0) return Array.Empty<GameInfo>();
        if (grade <= 4) return new GameInfo[]
        {
            new("practice:blockwords", "Block World", "▦", "#22C55E", "Mine, build and trade: story problems in a blocky world"),
            new("practice:regroup", "Block Trader", "⇄", "#F97316", "Trade 10 ones for a ten: adding and subtracting with blocks"),
            new("rewards", "My Build", "⌂", "#A855F7", "Every right answer places a block. See your house grow!"),
        };
        return new GameInfo[]
        {
            new("longdiv", "Long Division", "÷", "#22C55E", "The workshop: divide, multiply, subtract, bring down, step by step"),
            new("practice:sci6", "Science Lab", "⚗", "#14B8A6", "Uranium, terrariums and space mining story problems"),
            new("rewards", "My Lab", "⚛", "#A855F7", "Unlock element samples and build a terrarium"),
        };
    }

    // ---------------------------------------------------------------- opening a page that needs a player
    static int chainDepth = -1;
    static Func<IPage> chainOpen;
    static string chainTitle, chainHeading;

    /// <summary>Opens a page that needs a player (from the home page's subjects): straight away when someone is
    /// playing, else "Who's playing?" first, which then puts the page in its place.</summary>
    public static void OpenWithPlayer(string title, Func<IPage> open, string heading = "Who's playing?")
    {
        EnsureLoaded();
        var mv = MainView.Current;
        chainDepth = mv.Depth; chainOpen = open; chainTitle = title; chainHeading = heading;
        if (Me != null && Profiles.All.Contains(Me)) mv.Push(open());
        else mv.Push(new ProfilesPage(title, heading, open));
    }

    /// <summary>The player chip: back to "Who's playing?" (Learning Games' own, or the one before this page).</summary>
    public static void ChangePlayer()
    {
        var mv = MainView.Current;
        if (mv.Has<ProfilesPage>()) { mv.BackTo<ProfilesPage>(); return; }
        if (chainOpen != null && chainDepth >= 1 && chainDepth < mv.Depth)
        {
            mv.PopTo(chainDepth);
            mv.Push(new ProfilesPage(chainTitle, chainHeading, chainOpen));
            return;
        }
        mv.Push(new ProfilesPage());
    }

    public static string GameName(string id) => Games.FirstOrDefault(g => g.Id == id)?.Name ?? id;

    // ---------------------------------------------------------------- choosing what to practise
    /// <summary>The next skill: mostly the ones that need work, sometimes a review of a mastered one.</summary>
    public static string PickSkill(Player p, int grade, bool quick, string[] allowed = null)
    {
        var pool = quick ? global::LearningGames.Problems.QuickSkillsFor(grade) : global::LearningGames.Problems.SkillsFor(grade);
        var ids = pool.Where(id => allowed == null || allowed.Length == 0 || allowed.Contains(id)).ToList();
        if (ids.Count == 0) ids = pool.ToList();
        var weights = ids.Select(id =>
        {
            var st = p.Skill(id);
            double w = 1.0;
            if (st.Mastered) w = 0.35;
            else if (st.Recent.Count >= 5) w = 1.6 - st.Accuracy;
            return Math.Max(0.2, w);
        }).ToList();
        double r = Rand.NextDouble() * weights.Sum();
        for (int i = 0; i < ids.Count; i++) { r -= weights[i]; if (r <= 0) return ids[i]; }
        return ids[^1];
    }

    static readonly string[] praise = { "Great job!", "Awesome!", "You got it!", "Super!", "Nice work!", "Correct!", "Brilliant!", "Way to go!" };
    public static string Praise(int streak)
    {
        if (streak >= 5 && streak % 5 == 0 && Me != null && Me.Grade > 0)
            return streak + " in a row! Bonus: +2 " + (global::LearningGames.Rewards.Builds(Me.Grade) ? "blocks" : "lab points");
        return praise[Rand.Next(praise.Length)];
    }

    // ---------------------------------------------------------------- reading aloud
    public static void Speak(string text, string lang = "")
    {
        if (!global::LearningGames.Sound.Enabled || string.IsNullOrWhiteSpace(text)) return;
        try { Platform.Speech?.Speak(text, lang); } catch (Exception) { }
    }
    public static void StopSpeaking() { try { Platform.Speech?.Stop(); } catch (Exception) { } }

    /// <summary>A question as a voice should read it (symbols as words).</summary>
    public static string SayText(string text)
    {
        string t = text.Replace("−", " minus ").Replace("×", " times ").Replace("÷", " divided by ").Replace("¢", " cents");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\?\s*$", "?");
        t = t.Replace("= ?", "equals what?");
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
    }

    // ---------------------------------------------------------------- pieces of pages
    public static Control Avatar(string name, string color, double size)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Fill = Ui.Brush(color) });
        g.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
            FontSize = size * 0.48, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    public static Border Chip(Control content, string bg = Ui.Surface2, string border = Ui.BorderC)
        => new() { Child = content, Background = Ui.Brush(bg), BorderBrush = Ui.Brush(border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4), Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The top bar's right side: family room, stars, the player (tap to change) and the sound switch.</summary>
    public static Control TopExtras(bool playerChip = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (Family.Mode != NetMode.None)
            row.Children.Add(Chip(Ui.Text(Family.Mode == NetMode.Host ? "Hosting: " + Family.Players.Count + " joined" : "With " + Family.HostName, 13, Ui.AccentText), Ui.AccentSoft, Ui.Accent));
        if (Me != null && playerChip)
        {
            if (Me.Grade > 0)
                row.Children.Add(Chip(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Ui.Text("★", 14, Ui.Yellow), Ui.Text(Me.Stars.ToString(), 14, Ui.TextC, true) } }));
            var who = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Ellipse { Width = 12, Height = 12, Fill = Ui.Brush(Me.Color), VerticalAlignment = VerticalAlignment.Center }, Ui.Text(Me.Name, 14, Ui.TextC, true) } },
                Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4), Margin = new Thickness(4, 0), MinHeight = 32,
            };
            ToolTip.SetTip(who, "Change player");
            who.Click += (s, e) => { Family.Stop(); ChangePlayer(); };
            row.Children.Add(who);
        }
        var sound = Ui.IconButton(global::LearningGames.Sound.Enabled ? "\U0001F50A" : "\U0001F507", "Sound on / off");
        sound.FontSize = 16;
        sound.Click += (s, e) =>
        {
            global::LearningGames.Sound.Enabled = !global::LearningGames.Sound.Enabled;
            if (!global::LearningGames.Sound.Enabled) StopSpeaking();
            sound.Content = global::LearningGames.Sound.Enabled ? "\U0001F50A" : "\U0001F507";
        };
        row.Children.Add(sound);
        return row;
    }

    /// <summary>A coloured square with a symbol, for tiles.</summary>
    public static Border Badge(string glyph, string color, double size)
        => new()
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = Ui.Brush(color, 48), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = glyph, FontSize = size * 0.46, Foreground = Ui.Brush(color), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };

    /// <summary>A big friendly tile: badge, name, a line of text, and an optional coloured note under it.</summary>
    public static Button Tile(string glyph, string color, string name, string text, string note, string noteColor, Action click, double height = 170)
    {
        var sp = new StackPanel();
        sp.Children.Add(Badge(glyph, color, 54));
        var n = Ui.Text(name, 17, Ui.TextC, true); n.Margin = new Thickness(0, 12, 0, 4); sp.Children.Add(n);
        sp.Children.Add(Ui.Text(text, 13, Ui.Muted));
        if (!string.IsNullOrEmpty(note)) { var t = Ui.Text(note, 12.5, noteColor); t.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(t); }
        var b = Ui.Tile(sp, Ui.BorderStrong, click);
        b.MinHeight = height;
        b.CornerRadius = new CornerRadius(18);
        return b;
    }

    /// <summary>A thin progress bar (0..1).</summary>
    public static Control Bar(double fraction, string color, double height = 5)
    {
        var g = new Grid { Height = height, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(0.0001, fraction).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{Math.Max(0.0001, 1 - fraction).ToString(System.Globalization.CultureInfo.InvariantCulture)}*") };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Ui.Brush(Ui.Surface3), [Grid.ColumnSpanProperty] = 2 });
        if (fraction > 0) g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Ui.Brush(color), MinWidth = 3 });
        return g;
    }

    public static TextBlock H2(string text) { var t = Ui.Text(text, 18, Ui.TextC, true); t.Margin = new Thickness(0, 0, 0, 10); return t; }
}
