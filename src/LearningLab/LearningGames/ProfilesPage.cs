// Learning Games - "Who's playing?" and adding or changing a player.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;

namespace LearningLab.LearningGames;

public class ProfilesPage : IPage
{
    readonly string title;
    public string Title => title;
    public Control View { get; }
    public Control Extras => Lg.TopExtras(false);
    readonly WrapPanel list = new() { HorizontalAlignment = HorizontalAlignment.Center };
    bool warned;
    /// <summary>The page to open in this one's place once a player is picked (null: Learning Games' hub).</summary>
    public Func<IPage> Then { get; }

    public ProfilesPage() : this("Learning Games", "Who's playing?", null) { }

    /// <summary>"Who's playing?" before a page that needs a player (a subject opened from the home page).</summary>
    public ProfilesPage(string title, string heading, Func<IPage> then)
    {
        this.title = title;
        Then = then;
        Lg.EnsureLoaded();
        Lg.Me = null;
        Family.StartPump();
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var h = Ui.Text(heading, 32, Ui.TextC, true); h.HorizontalAlignment = HorizontalAlignment.Center; h.TextAlignment = TextAlignment.Center;
        var sub = Ui.Text("Pick your name. Grown-ups can watch and help from their own profile.", 15, Ui.Muted); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.TextAlignment = TextAlignment.Center; sub.Margin = new Thickness(0, 6, 0, 22);
        body.Children.Add(h); body.Children.Add(sub); body.Children.Add(list);
        View = Ui.Page(body);
        Fill(1000);
        View.SizeChanged += (s, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Fill(e.NewSize.Width); };
        View.AttachedToVisualTree += (s, e) => { Lg.Me = null; Fill(View.Bounds.Width > 0 ? View.Bounds.Width : 1000); WarnOnce(); };
    }

    void WarnOnce()
    {
        if (warned) return;
        warned = true;
        if (Profiles.State == ProfilesState.Locked)
            MainView.Current.Message("Saved players can't be read", Profiles.Message + "\n\nLearning Games could not read the saved players (or any of their backups). To keep that file safe for repair, nothing will be saved over it.");
        else if (Profiles.Message.Length > 0)
            MainView.Current.Message("Players brought back from a backup", Profiles.Message + "\n\nEveryone's progress up to then is back; anything since that copy was made is in the file that was kept aside.");
    }

    public void Fill(double width)
    {
        list.Children.Clear();
        double w = width < 520 ? 145 : 190;
        foreach (var p in Profiles.All)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var av = Lg.Avatar(p.Name, p.Color, w * 0.46); av.Margin = new Thickness(0, 4, 0, 10); sp.Children.Add(av);
            var n = Ui.Text(p.Name, w < 160 ? 17 : 20, Ui.TextC, true); n.HorizontalAlignment = HorizontalAlignment.Center; n.TextAlignment = TextAlignment.Center; sp.Children.Add(n);
            string sub = p.Age > 0 ? "Age " + p.Age + (p.Grade == 0 ? "  |  grown-up" : "") + "  |  " + p.Stars + " stars" : p.Stars + " stars";
            var g = Ui.Text(sub, 12.5, Ui.Muted); g.HorizontalAlignment = HorizontalAlignment.Center; g.TextAlignment = TextAlignment.Center; sp.Children.Add(g);
            var player = p;
            var edit = new Button { Content = "Change", FontSize = 12, Padding = new Thickness(12, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Ui.Muted), CornerRadius = new CornerRadius(10) };
            edit.Click += (s, e) => { e.Handled = true; MainView.Current.Push(new EditPlayerPage(player)); };
            sp.Children.Add(edit);
            var tile = Ui.Tile(sp, Ui.BorderStrong, () => Choose(player));
            tile.Width = w; tile.MinHeight = w + 40; tile.Margin = new Thickness(7); tile.CornerRadius = new CornerRadius(18);
            ToolTip.SetTip(tile, "Right-click (or Change) to change or delete");
            tile.PointerReleased += (s, e) => { if (e.InitialPressMouseButton == MouseButton.Right) MainView.Current.Push(new EditPlayerPage(player)); };
            list.Children.Add(tile);
        }
        var add = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plus = Ui.Text("+", 48, Ui.AccentHover, true); plus.HorizontalAlignment = HorizontalAlignment.Center; add.Children.Add(plus);
        var at = Ui.Text("Add a player", 16, Ui.AccentText, true); at.HorizontalAlignment = HorizontalAlignment.Center; add.Children.Add(at);
        var addTile = Ui.Tile(add, Ui.BorderStrong, () => MainView.Current.Push(new EditPlayerPage(null)));
        addTile.Width = w; addTile.MinHeight = w + 40; addTile.Margin = new Thickness(7); addTile.CornerRadius = new CornerRadius(18);
        list.Children.Add(addTile);
    }

    /// <summary>A player picked on this page: the page it was opened for takes its place, or Learning Games' hub.</summary>
    public void Choose(Player p)
    {
        if (Then == null) { Select(p); return; }
        Lg.Me = p;
        MainView.Current.Replace(Then());
    }

    public static void Select(Player p)
    {
        Lg.Me = p;
        MainView.Current.Push(new HubPage());
    }
}

public class EditPlayerPage : IPage
{
    readonly Player player;
    public string Title => player == null ? "New player" : "Change " + player.Name;
    public Control View { get; }
    readonly TextBox name = new() { FontSize = 18, MaxLength = 20 };
    readonly TextBox age = new() { FontSize = 18, Width = 90, MaxLength = 3, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly TextBlock levelNote = Ui.Text("", 14, Ui.Muted);
    readonly CheckBox grownUp = new() { Content = "Grown-up: can also open the parent view (everyone's progress, family games)", FontSize = 14, Margin = new Thickness(0, 14, 0, 0) };
    readonly WrapPanel colors = new();
    string color;

    public EditPlayerPage(Player p)
    {
        player = p;
        name.Text = p?.Name ?? "";
        age.Text = p != null && p.Age > 0 ? p.Age.ToString() : "";
        grownUp.IsChecked = p != null && p.Grade == 0;
        color = p?.Color ?? Profiles.Colors[Profiles.All.Count % Profiles.Colors.Length];
        age.TextChanged += (s, e) => UpdateLevel();
        UpdateLevel();
        FillColors();

        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(Ui.Text("Name", 13, Ui.Muted));
        body.Children.Add(name);
        var al = Ui.Text("Age", 13, Ui.Muted); al.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(al);
        levelNote.VerticalAlignment = VerticalAlignment.Center; levelNote.Margin = new Thickness(14, 0, 0, 0);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { age, levelNote } });
        body.Children.Add(Ui.Text("The age picks where each subject starts; it moves up or down with how you do.", 13, Ui.Muted));
        body.Children.Add(grownUp);
        var cl = Ui.Text("Colour", 13, Ui.Muted); cl.Margin = new Thickness(0, 14, 0, 6); body.Children.Add(cl);
        body.Children.Add(colors);
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        if (p != null)
        {
            var del = Ui.Button("Delete player");
            del.Foreground = Ui.Brush(Ui.Red); del.BorderBrush = Ui.Brush(Ui.Red, 120); del.Margin = new Thickness(0, 4, 16, 4);
            del.Click += (s, e) => MainView.Current.Message("Delete " + p.Name + "?", "This deletes " + p.Name + " and all their progress.",
                ("Delete", () => { Profiles.Remove(p); Profiles.Save(); Done(); }), ("Keep", null));
            buttons.Children.Add(del);
        }
        var cancel = Ui.Button("Cancel"); cancel.Margin = new Thickness(0, 4, 8, 4); cancel.Click += (s, e) => MainView.Current.Back();
        var save = Ui.Button("Save", true); save.MinWidth = 110; save.Margin = new Thickness(0, 4); save.Click += (s, e) => Save();
        save.IsDefault = true;
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        body.Children.Add(buttons);
        var card = Ui.Card(body, Ui.Surface, 24);
        card.MaxWidth = 560;
        View = Ui.Page(card, 600);
    }

    public void SetForTest(string n, string a, bool grown) { name.Text = n; age.Text = a; grownUp.IsChecked = grown; }
    public void SaveForTest() => Save();

    void UpdateLevel()
    {
        levelNote.Text = int.TryParse(age.Text?.Trim(), out int a) && a >= 2 ? "Starts at: " + Bank.LevelNames[Profiles.AgeLevel(a)] : "";
    }

    void FillColors()
    {
        colors.Children.Clear();
        foreach (var c in Profiles.Colors)
        {
            var g = new Grid { Width = 40, Height = 40 };
            g.Children.Add(new Ellipse { Fill = Ui.Brush(c) });
            if (c == color) g.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 3 });
            var b = new Button { Content = g, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 8), Cursor = new Cursor(StandardCursorType.Hand) };
            string cc = c;
            b.Click += (s, e) => { color = cc; FillColors(); };
            colors.Children.Add(b);
        }
    }

    void Save()
    {
        string n = (name.Text ?? "").Trim();
        if (n.Length == 0) { MainView.Current.Message("", "Type a name."); return; }
        if (!int.TryParse((age.Text ?? "").Trim(), out int a) || a < 2 || a > 120) { MainView.Current.Message("", "Type an age (a number, like 7 or 45)."); return; }
        // math has 2nd- and 6th-grade games: the age picks one; a grown-up keeps the parent view (and plays 6th-grade math)
        int grade = grownUp.IsChecked == true ? 0 : a <= 9 ? 2 : 6;
        if (player != null)
        {
            bool newAge = player.Age != a;
            player.Name = n; player.Grade = grade; player.Age = a; player.Color = color;
            if (newAge) player.ResetSubjectLevels();   // a new age restarts the levels there
        }
        else Profiles.Add(n, grade, a, color);
        if (!Profiles.Save() && Profiles.LastSaveError.Length > 0) MainView.Current.Message("Not saved", Profiles.LastSaveError);
        Done();
    }

    static void Done()
    {
        MainView.Current.BackTo<ProfilesPage>();
        if (MainView.Current.Page is ProfilesPage pp) pp.Fill(pp.View.Bounds.Width > 0 ? pp.View.Bounds.Width : 1000);
    }
}
