// Learning Lab - the home page: the school subjects (Language Arts, Math, Science, Social Studies, World Languages,
// Computers & Technology). Each opens a CategoryPage with what's inside as tiles; things not moved over from App Hub
// yet say "Coming soon". Learning Games' pages need a player: Lg.OpenWithPlayer asks "Who's playing?" first when
// nobody is. Learning Games as a whole (players, family games, the parent view) stays one tap away at the bottom.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.LearningGames;

namespace LearningLab.Views;

public class HomePage : IPage
{
    public string Title => "Learning Lab";
    public Control View { get; }

    /// <summary>Something inside a subject. Open is null until it has moved over. Player: the page needs a Learning Games
    /// player (asked for first); its value is the question asked ("Who's playing?").</summary>
    public record Entry(string Id, string Name, string Glyph, string Accent, string Blurb, Func<IPage> Open, string Player = null);
    public record Category(string Id, string Name, string Glyph, string Accent, string Blurb, Entry[] Entries);

    const string Who = "Who's playing?";

    /// <summary>The subjects, in the order they appear.</summary>
    public static readonly Category[] Categories =
    {
        new("language-arts", "Language Arts", "✎", "#22C55E", "English: Spelling Bee practice, words, grammar and great books.", new Entry[]
        {
            new("english", "English", "✎", "#22C55E", "Spelling Bee practice, then letters, words, grammar and great books: quizzes, flash cards and games.", () => new SubjectPage("english"), Who),
        }),
        new("math", "Math", "➕", "#3B82F6", "A skill map for 2nd and 6th grade with a mastery score for every skill, practice that explains each mistake step by step, math games and the Long Division Workshop.", new Entry[]
        {
            new("skill-map", "Skill map and practice", "◎", "#3B82F6", "Every skill with your score from Getting started to Mastered. Practice one, and learn from every mistake step by step.", () => new SkillMapPage(), Who),
            new("math-games", "Math games", "➕", "#3B82F6", "Asteroid Blaster, Number Line Frog, Pizza Shop, races and boss battles, and practice for every skill.", () => new MathHomePage(), Who),
            new("long-division", "Long Division Workshop", "÷", "#22C55E", "Divide, multiply, subtract, bring down: step by step, with help when you need it.", () => new LongDivPage(), Who),
        }),
        new("science", "Science", "⚛", "#14B8A6", "Animals, plants, weather, matter and space.", new Entry[]
        {
            new("science", "Science questions", "⚛", "#14B8A6", "Animals, plants, weather, matter and space: quizzes, flash cards and games.", () => new SubjectPage("science"), Who),
        }),
        new("social-studies", "Social Studies", "⏳", "#F59E0B", "History, and how the United States is governed.", new Entry[]
        {
            new("history", "History", "⏳", "#F59E0B", "People, places and what happened when.", () => new SubjectPage("history"), Who),
            new("civics", "US Citizenship", "⚖", "#EF4444", "The Constitution, government and the citizenship test.", () => new SubjectPage("civics"), Who),
        }),
        new("world-languages", "World Languages", "Ñ", "#EC4899", "Spanish: everyday phrases, lessons for the car, and quizzes.", new Entry[]
        {
            new("languages", "Spanish lessons", "Ñ", "#F59E0B", "Phrases for every day, lessons, stories, verbs, and spoken lessons to listen to in the car.", () => new Languages.LanguagesPage()),
            new("spanish", "Spanish quizzes", "¡", "#EC4899", "Words, phrases and grammar en español: quizzes, flash cards and games.", () => new SubjectPage("spanish"), Who),
        }),
        new("computers", "Computers & Technology", "</>", "#A855F7", "Coding, how computers work inside, networks, and IT certifications.", new Entry[]
        {
            new("coding", "Coding questions", "</>", "#A855F7", "Think like a programmer, then write real code.", () => new SubjectPage("coding"), Who),
            new("emulator", "Emulator Lab", "⚙", "#EAB308", "A small computer to look inside: its own processor, operating system and programs you can change.", () => new EmulatorLab.EmulatorPage()),
            new("network", "Network Lab", "⬡", "#38BDF8", "Build networks with routers, switches, PCs and Wi-Fi, type real-style commands, and try the labs.", () => new global::LearningLab.NetworkLab.NetLabPage()),
            new("cert", "Cert Lab", "✔", "#A855F7", "Study for RHCSA, Security+, MD-102, AZ-800 and PowerShell: lessons, practice machines, labs, flash cards and practice exams.", () => new global::LearningLab.CertLab.CertHomePage()),
            new("range", "Cyber Range", "⚑", "#EF4444", "Capture-the-flag practice on simulated networks: attack, defend and solve puzzles, from beginner to expert.", () => new global::LearningLab.CyberRange.RangeHomePage()),
        }),
    };

    public HomePage()
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Ui.Text("What would you like to learn today?", 26, Ui.TextC, true));
        body.Children.Add(Ui.Text("Pick a subject. Everything here works offline, and nothing you do leaves this device unless you play with family on your home network.", 15, Ui.Muted));
        var tiles = new List<Control>();
        foreach (var c in Categories) tiles.Add(MakeTile(c));
        var grid = Ui.Tiles(tiles, 300);
        grid.Margin = new Thickness(-6, 8, -6, 0);
        body.Children.Add(grid);

        var all = new Button
        {
            Content = new TextBlock { Text = "All of Learning Games: players, family games and the parent view  ›", TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ui.Brush(Ui.AccentText) },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 10), MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        all.Click += (s, e) => MainView.Current.Push(new ProfilesPage());
        AllGames = all;
        body.Children.Add(all);
        body.Children.Add(Ui.Text("Part of Learning Lab (Sauce Edition) " + AppVersion.Text, 12, Ui.Dim));
        View = Ui.Page(body);
    }

    /// <summary>The "All of Learning Games" link (the checks press it).</summary>
    public Button AllGames { get; }

    static Control MakeTile(Category c)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Badge(c.Glyph, c.Accent, 52), new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(c.Name, 20, Ui.TextC, true) } } } };
        var inside = Ui.Text(string.Join("  ·  ", c.Entries.Select(x => x.Name)), 12.5, Ui.AccentText);
        var stack = new StackPanel { Spacing = 10, Children = { head, Ui.Text(c.Blurb, 14, Ui.Muted), inside } };
        var tile = Ui.Tile(stack, c.Accent, () => MainView.Current.Push(new CategoryPage(c)));
        tile.MinHeight = 150;
        return tile;
    }

    public static Border Badge(string glyph, string accent, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.23),
        Background = Ui.Brush(accent, 40),
        Child = new TextBlock { Text = glyph, FontSize = size * 0.46, Foreground = Ui.Brush(accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>Opens an entry (asking for a player first when it needs one).</summary>
    public static void Open(Entry e)
    {
        if (e.Open == null) return;
        if (e.Player != null) Lg.OpenWithPlayer(e.Name, e.Open, e.Player);
        else MainView.Current.Push(e.Open());
    }

    /// <summary>The Spelling Bee: inside Language Arts > English (Andrew, 2026-11-12), opened from the English page.</summary>
    public static readonly Entry SpellingBee = new("spelling-bee", "Spelling Bee", "\U0001F41D", "#FBBF24", "Hear a word, spell it out loud or type it, and practice the tricky ones until they stick.", () => new LearningLab.SpellingBee.BeeHomePage(), "Who's practicing?");

    public static Entry Find(string id) => id == SpellingBee.Id ? SpellingBee : Categories.SelectMany(c => c.Entries).FirstOrDefault(e => e.Id == id);
    /// <summary>The subject an entry is in (the Spelling Bee counts as in Language Arts, through English).</summary>
    public static Category CategoryOf(string entryId) => Categories.FirstOrDefault(c => c.Entries.Any(e => e.Id == (entryId == SpellingBee.Id ? "english" : entryId)));
}

/// <summary>One school subject: what's inside, as tiles.</summary>
public class CategoryPage : IPage
{
    public HomePage.Category Category { get; }
    public string Title => Category.Name;
    public Control View { get; }

    public CategoryPage(HomePage.Category c)
    {
        Category = c;
        var body = new StackPanel { Spacing = 8 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(HomePage.Badge(c.Glyph, c.Accent, 56));
        var t = Ui.Text(c.Name, 26, Ui.TextC, true); t.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(t);
        body.Children.Add(head);
        body.Children.Add(Ui.Text(c.Blurb, 15, Ui.Muted));
        var tiles = new List<Control>();
        foreach (var e in c.Entries) tiles.Add(MakeTile(e));
        var grid = Ui.Tiles(tiles, 280);
        grid.Margin = new Thickness(-6, 8, -6, 0);
        body.Children.Add(grid);
        // Math: what the player who is playing should practise next
        if (c.Id == "math" && Lg.Me != null && Profiles.All.Contains(Lg.Me))
        {
            var next = MasteryUi.UpNextPanel(Lg.Me, MasteryUi.GradeFor(Lg.Me));
            if (next != null) { next.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(next); }
        }
        View = Ui.Page(body);
    }

    static Control MakeTile(HomePage.Entry e)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { HomePage.Badge(e.Glyph, e.Accent, 48), new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(e.Name, 19, Ui.TextC, true) } } } };
        if (e.Open == null) ((StackPanel)head.Children[1]).Children.Add(Ui.Text("Coming soon", 12, Ui.Yellow));
        var stack = new StackPanel { Spacing = 10, Children = { head, Ui.Text(e.Blurb, 14, Ui.Muted) } };
        var tile = Ui.Tile(stack, e.Accent, () => HomePage.Open(e));
        tile.MinHeight = 130;
        tile.IsEnabled = e.Open != null;
        tile.Opacity = e.Open != null ? 1 : 0.6;
        return tile;
    }
}

public static class AppVersion
{
    public static string Text
    {
        get
        {
            var v = typeof(AppVersion).Assembly.GetName().Version;
            return v == null ? "" : "v" + v.Major + "." + v.Minor + "." + v.Build;
        }
    }
}
