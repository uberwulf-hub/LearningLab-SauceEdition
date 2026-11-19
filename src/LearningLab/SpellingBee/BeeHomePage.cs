// Learning Lab - Spelling Bee's start page: pick the word lists (ticks, word counts; My Tricky Words first), how many
// words, mix up the order, say each word automatically, then Start. Also: add a word list (a file or pasted words),
// change or delete an added one, how it works, and the player's last scores.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.LearningGames;
using LearningLab.Views;

namespace LearningLab.SpellingBee;

public class BeeHomePage : IPage
{
    public string Title => "Spelling Bee";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel body = new() { Spacing = 14 };
    bool warned;

    public BeeHomePage()
    {
        View = Ui.Page(body, 760);
        Fill();
        View.AttachedToVisualTree += (s, e) => { Fill(); Warn(); };
    }

    void Warn()
    {
        if (warned) return;
        warned = true;
        var ps = Bee.Players;
        if (ps.Blocked)
            MainView.Current.Message("Saved spelling practice can't be read", ps.Message + "\n\nYou can still practice; tricky words and scores won't be saved until you choose Start fresh.",
                ("Keep it as it is", null), ("Start fresh", () => { ps.StartFresh(); Bee.Save(); Fill(); }));
        else if (!string.IsNullOrEmpty(ps.Message)) MainView.Current.Message("Brought back from a backup", ps.Message);
        else if (Bee.Store.Problems.Count > 0) MainView.Current.Message("A word list couldn't be read", string.Join("\n", Bee.Store.Problems));
    }

    BeePlayer me;
    HashSet<string> selected;
    List<BeeList> lists;

    public IReadOnlyList<BeeList> Lists => lists;
    public IReadOnlyCollection<string> SelectedIds => selected;

    public void Fill()
    {
        _ = Bee.Mine;
        me = Bee.Me;
        lists = Bee.ListsFor(me);
        selected = Bee.Selected(me);
        selected.RemoveWhere(id => !lists.Any(l => l.Id == id));
        body.Children.Clear();

        // header
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(HomePage.Badge("\U0001F41D", Bee.Honey, 58));
        head.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text("Spelling Bee practice", 26, Ui.TextC, true), Ui.Text("Hear it, spell it, check it.", 15, Ui.Muted) } });
        body.Children.Add(head);

        // 1: lists
        var c1 = new StackPanel { Spacing = 10 };
        c1.Children.Add(Step("1", "Pick your word lists"));
        foreach (var l in lists) c1.Children.Add(ListButton(l));
        var add = Ui.Button("+  Add a word list");
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Foreground = Ui.Brush(Ui.AccentText);
        add.Click += (s, e) => MainView.Current.Push(new BeeAddListPage());
        c1.Children.Add(add);
        body.Children.Add(Ui.Card(c1, Ui.Surface, 18));

        // 2: how many, order, voice
        var c2 = new StackPanel { Spacing = 12 };
        c2.Children.Add(Step("2", "How many words?"));
        var seg = new UniformGrid { Columns = 4 };
        foreach (var n in new[] { 10, 25, 50, 0 })
        {
            int nn = n;
            var b = Ui.Button(n == 0 ? "All" : n.ToString(), me.HowMany == n, 17);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (me.HowMany == n) { b.Background = Ui.Brush(Bee.Honey); b.BorderBrush = Ui.Brush(Bee.Honey); b.Foreground = Ui.Brush("#1A1408"); }
            b.Click += (s, e) => SetHowMany(nn);
            seg.Children.Add(b);
        }
        c2.Children.Add(seg);
        c2.Children.Add(Switch("Mix up the order", me.Shuffle, v => { me.Shuffle = v; Bee.Save(); }));
        c2.Children.Add(Switch("Say each word automatically", me.AutoSay, v => { me.AutoSay = v; Bee.Save(); }));
        body.Children.Add(Ui.Card(c2, Ui.Surface, 18));

        // start
        int count = Pool().Select(w => w.Word).Distinct().Count();
        int take = me.HowMany > 0 ? Math.Min(me.HowMany, count) : count;
        var start = new Button
        {
            Content = new TextBlock { Text = "\U0001F41D  Start practicing", FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush("#1A1408"), HorizontalAlignment = HorizontalAlignment.Center },
            Background = Ui.Brush(Bee.Honey), BorderBrush = Ui.Brush(Bee.HoneyDark), BorderThickness = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 16), MinHeight = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = count > 0,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        start.Click += (s, e) => Start();
        StartButton = start;
        body.Children.Add(start);
        var hint = Ui.Text(count == 0 ? "Tick a word list to start." : take + " word" + (take == 1 ? "" : "s") + " from " + string.Join(", ", lists.Where(l => selected.Contains(l.Id)).Select(l => l.Name)) + ".", 13.5, Ui.Muted);
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, -6, 0, 0);
        body.Children.Add(hint);

        // how it works
        var how = new StackPanel { Spacing = 8 };
        how.Children.Add(Ui.Text("How it works", 17, Ui.TextC, true));
        foreach (var (g, t) in new[]
        {
            ("\U0001F50A", "Tap the big speaker to hear the word. Use \U0001F422 to hear it slowly."),
            ("\U0001F3A4", "Spell it out loud, saying each letter like \"C, A, B, I, N\", or just type it."),
            ("✅", "You find out right away if you got it, and hear the right spelling if you didn't."),
            ("⭐", "Words you miss go into My Tricky Words, so you can practice them again. Spell one right the first time and it leaves the list."),
        })
        {
            var row = new DockPanel();
            var gl = Ui.Text(g, 18); gl.Width = 34; DockPanel.SetDock(gl, Dock.Left);
            row.Children.Add(gl); row.Children.Add(Ui.Text(t, 14, Ui.Muted));
            how.Children.Add(row);
        }
        body.Children.Add(Ui.Card(how, Ui.Surface, 18));

        // last scores
        var hist = me.History;
        if (hist.Count > 0)
        {
            var h = new StackPanel { Spacing = 6 };
            h.Children.Add(Ui.Text(Bee.PlayerName + "'s last practices", 17, Ui.TextC, true));
            foreach (var x in Enumerable.Reverse(hist).Take(6))
            {
                var row = new DockPanel();
                var sc = Ui.Text(x.Right + " / " + x.Total, 15, x.Total > 0 && x.Right == x.Total ? Ui.Green : Ui.TextC, true);
                DockPanel.SetDock(sc, Dock.Right); sc.Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(sc);
                var t = Ui.Text(x.When + "  ·  " + x.Lists, 13.5, Ui.Muted); t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap;
                row.Children.Add(t);
                h.Children.Add(row);
            }
            body.Children.Add(Ui.Card(h, Ui.Surface, 18));
        }
    }

    public Button StartButton { get; private set; }

    static Control Step(string n, string text)
    {
        var num = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Brush(Bee.Honey), Child = new TextBlock { Text = n, FontWeight = FontWeight.Bold, Foreground = Ui.Brush("#1A1408"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var t = Ui.Text(text, 18, Ui.TextC, true); t.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { num, t } };
    }

    static Control Switch(string label, bool on, Action<bool> changed)
    {
        var t = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        t.IsCheckedChanged += (s, e) => changed(t.IsChecked == true);
        var l = Ui.Text(label, 15.5); l.VerticalAlignment = VerticalAlignment.Center;
        var d = new DockPanel { MinHeight = 44 };
        DockPanel.SetDock(t, Dock.Right);
        d.Children.Add(t); d.Children.Add(l);
        return d;
    }

    Control ListButton(BeeList l)
    {
        bool on = selected.Contains(l.Id);
        var em = Ui.Text(string.IsNullOrEmpty(l.Emoji) ? "\U0001F4DD" : l.Emoji, 22); em.Width = 58; em.TextWrapping = TextWrapping.NoWrap; em.TextAlignment = TextAlignment.Center; em.VerticalAlignment = VerticalAlignment.Center;
        var chk = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center,
            Background = on ? Ui.Brush(Bee.Honey) : Brushes.Transparent, BorderBrush = Ui.Brush(on ? Bee.Honey : Ui.BorderStrong), BorderThickness = new Thickness(2),
            Child = new TextBlock { Text = on ? "✓" : "", FontWeight = FontWeight.Bold, Foreground = Ui.Brush("#1A1408"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(l.Name, 16.5, Ui.TextC, true), Ui.Text(l.Words.Count + " word" + (l.Words.Count == 1 ? "" : "s") + (string.IsNullOrEmpty(l.Note) ? "" : "  ·  " + l.Note), 13, Ui.Muted) } };
        var row = new DockPanel();
        DockPanel.SetDock(em, Dock.Left); DockPanel.SetDock(chk, Dock.Right);
        row.Children.Add(em); row.Children.Add(chk);
        if (!l.BuiltIn)
        {
            var change = new Button { Content = "Change", FontSize = 13, Padding = new Thickness(12, 6), MinHeight = 36, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center, Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Ui.Muted), CornerRadius = new CornerRadius(10) };
            var list = l;
            change.Click += (s, e) => { e.Handled = true; MainView.Current.Push(new BeeListPage(list)); };
            DockPanel.SetDock(change, Dock.Right);
            row.Children.Add(change);
        }
        row.Children.Add(text);
        var b = new Button
        {
            Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 10), MinHeight = 60, CornerRadius = new CornerRadius(14),
            Background = Ui.Brush(on ? Bee.HoneySoft : Ui.Surface2), BorderBrush = Ui.Brush(on ? Bee.Honey : Ui.BorderC), BorderThickness = new Thickness(1.5),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        string id = l.Id;
        b.Click += (s, e) => Toggle(id);
        return b;
    }

    // ---------------------------------------------------------------- actions (the checks call these too)
    public void Toggle(string id)
    {
        if (!selected.Remove(id)) selected.Add(id);
        me.Lists = selected.ToList();
        Bee.Save();
        Fill();
    }

    public void SetHowMany(int n) { me.HowMany = n; Bee.Save(); Fill(); }

    IEnumerable<BeeWord> Pool() => lists.Where(l => selected.Contains(l.Id)).SelectMany(l => l.Words);

    /// <summary>Starts a run; false when no list with words is ticked.</summary>
    public bool Start()
    {
        var words = BeeRun.Pick(Pool(), me.Shuffle, me.HowMany, Lg.Rand);
        if (words.Count == 0) { MainView.Current.Message("", "Tick a word list first."); return false; }
        string names = string.Join(", ", lists.Where(l => selected.Contains(l.Id)).Select(l => l.Name));
        MainView.Current.Push(new BeePracticePage(new BeeRun(words), names));
        return true;
    }
}
