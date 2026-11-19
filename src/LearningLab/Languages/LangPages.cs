// Learning Lab - Languages: the pages. LanguagesPage (the languages) -> CoursePage (CoursePage.cs: the path of units
// and lessons, the daily goal) -> UnitPage (the phrases, flashcards, quiz, listen); LessonPage, VerbPage, StoryPages,
// CarPage, VoicesPage and LangSettingsPage have their own files.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

static partial class LUi
{
    public static Control Bar(double share, string color, double height = 8)
    {
        var g = new Grid { Height = height, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(0.0001, share).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{Math.Max(0.0001, 1 - share).ToString(System.Globalization.CultureInfo.InvariantCulture)}*") };
        var bg = new Border { CornerRadius = new CornerRadius(height / 2), Background = Ui.Brush(Ui.Surface3) };
        Grid.SetColumnSpan(bg, 2);
        g.Children.Add(bg);
        if (share > 0) g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Ui.Brush(color) });
        return g;
    }

    public static Button Speaker(Action say, string tip = "Hear it")
    {
        var b = Ui.IconButton("\U0001F50A", tip);
        b.Click += (s, e) => say();
        return b;
    }

    public static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; return t; }

    public static Control Head(string title, string sub)
    {
        var sp = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(Wrap(Ui.Text(title, 26, Ui.TextC, true)));
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(Wrap(Ui.Text(sub, 14.5, Ui.Muted)));
        return sp;
    }

    public static void Warn(StackPanel body, LangCourse c)
    {
        var p = Lx.Progress;
        if (!string.IsNullOrEmpty(p.Message))
        {
            var sp = new StackPanel { Spacing = 8 };
            sp.Children.Add(Wrap(Ui.Text(p.Message, 14, Ui.Yellow)));
            if (p.Blocked)
            {
                var fresh = Ui.Button("Start fresh");
                fresh.HorizontalAlignment = HorizontalAlignment.Left;
                fresh.Click += (s, e) => MainView.Current.Message("Start fresh?", "Your old progress file stays where it is (it is kept aside, never deleted). Learning Lab starts a new one from here.",
                    ("Start fresh", () => { p.StartFresh(); Lx.Save(); MainView.Current.Back(); }), ("Not now", null));
                sp.Children.Add(fresh);
            }
            var card = Ui.Card(sp, Ui.Surface2, 12);
            card.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(card);
        }
        if (c != null && Lx.Target(c) == null)
        {
            var sp = new StackPanel { Spacing = 8 };
            sp.Children.Add(Ui.Text("No " + c.Name + " voice on this device yet", 15, Ui.TextC, true));
            sp.Children.Add(Wrap(Ui.Text("Everything works without one except hearing the words. A free voice takes a minute to add.", 13.5, Ui.Muted)));
            var go = Ui.Button("Voices", true);
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Click += (s, e) => MainView.Current.Push(new VoicesPage(c));
            sp.Children.Add(go);
            var card = Ui.Card(sp, Ui.Surface2, 12);
            card.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(card);
        }
    }
}

// ==================================================================== the languages
public class LanguagesPage : IPage
{
    public string Title => "Languages";
    public Control View { get; }

    public LanguagesPage()
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(LUi.Head("Languages", "Learn to understand and speak, a few minutes a day. Lessons talk you through it, so you can learn in the car with your eyes on the road."));
        var tiles = new List<Control>();
        foreach (var l in Lx.Languages)
        {
            var c = Lx.Course(l.Id);
            if (c == null) continue;
            var (learned, _, total) = Totals(c);
            var badge = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Background = Ui.Brush(l.Accent, 40), Child = new TextBlock { Text = l.Flag, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Ui.Brush(l.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(l.Name, 20, Ui.TextC, true), Ui.Text(l.Native, 13, Ui.Muted) } };
            var sp = new StackPanel { Spacing = 10 };
            sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { badge, names } });
            sp.Children.Add(LUi.Wrap(Ui.Text(l.Blurb, 13.5, Ui.Muted)));
            sp.Children.Add(LUi.Bar(total == 0 ? 0 : learned / (double)total, l.Accent));
            sp.Children.Add(Ui.Text(learned + " of " + total + " phrases learned", 12.5, Ui.Dim));
            string id = l.Id;
            var t = Ui.Tile(sp, l.Accent, () => MainView.Current.Push(new CoursePage(id)));
            tiles.Add(t);
        }
        var grid = Ui.Tiles(tiles, 300);
        grid.Margin = new Thickness(-6, 4, -6, 0);
        body.Children.Add(grid);
        body.Children.Add(LUi.Wrap(Ui.Text("More languages can be added later.", 13, Ui.Dim)));
        View = Ui.Page(body, 900);
    }

    public static (int Learned, int Met, int Total) Totals(LangCourse c)
    {
        int l = 0, m = 0, t = 0;
        foreach (var u in c.Units) { var s = Lx.Progress.UnitState(c.Language, u); l += s.Learned; m += s.Met; t += s.Total; }
        return (l, m, t);
    }
}

// ==================================================================== one unit
public class UnitPage : IPage
{
    readonly LangCourse c;
    readonly LangUnit u;
    public string Title => u.Title;
    public Control View { get; }

    public UnitPage(LangCourse c, LangUnit u)
    {
        this.c = c; this.u = u;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(LUi.Head(u.Number + ". " + u.Title, u.Intro));
        var acts = new WrapPanel();
        var listen = Ui.Button("\U0001F697  Listen", true);
        listen.Click += (s, e) => MainView.Current.Push(new CarPage(c, u));
        var cards = Ui.Button("Flashcards");
        cards.Click += (s, e) => MainView.Current.Push(new CardsPage(c, u.Items.ToList(), u.Title));
        var quiz = Ui.Button("Quiz");
        quiz.Click += (s, e) => MainView.Current.Push(new QuizPage(c, u));
        foreach (var b in new[] { listen, cards, quiz }) { b.Margin = new Thickness(0, 0, 10, 10); acts.Children.Add(b); }
        body.Children.Add(acts);

        if (u.Tips.Count > 0)
        {
            var tips = new StackPanel { Spacing = 6 };
            tips.Children.Add(Ui.Text("Good to know", 14, Ui.AccentText, true));
            foreach (var t in u.Tips) tips.Children.Add(LUi.Wrap(Ui.Text("• " + t, 13.5, Ui.TextC)));
            var card = Ui.Card(tips, Ui.Surface2, 12);
            card.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(card);
        }

        var list = new StackPanel { Spacing = 6 };
        foreach (var it in u.Items) list.Children.Add(Row(it));
        body.Children.Add(list);
        View = Ui.Page(body, 860);
    }

    Control Row(LangItem it)
    {
        var p = Lx.Progress;
        var d = new DockPanel();
        var say = LUi.Speaker(() => Lx.Say(c, it.Target));
        say.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(say, Dock.Left);
        d.Children.Add(say);
        if (p.Learned(c.Language, it.Id))
        {
            var ok = Ui.Text("✓", 16, Ui.Green, true);
            ok.VerticalAlignment = VerticalAlignment.Center; ok.Margin = new Thickness(8, 0, 4, 0);
            ToolTip.SetTip(ok, "Learned");
            DockPanel.SetDock(ok, Dock.Right);
            d.Children.Add(ok);
        }
        var sp = new StackPanel { Spacing = 2, Margin = new Thickness(6, 2, 0, 0) };
        sp.Children.Add(LUi.Wrap(Ui.Text(it.Target, 17, Ui.TextC, true)));
        sp.Children.Add(LUi.Wrap(Ui.Text(it.English + (string.IsNullOrEmpty(it.Literal) ? "" : "  (word for word: " + it.Literal + ")"), 14, Ui.Muted)));
        if (!string.IsNullOrEmpty(it.Note)) sp.Children.Add(LUi.Wrap(Ui.Text(it.Note, 12.5, Ui.AccentText)));
        d.Children.Add(sp);
        return Ui.Card(d, Ui.Surface, 8);
    }
}

// ==================================================================== flashcards
public class CardsPage : IPage, IKeyPage
{
    readonly LangCourse c;
    readonly List<LangItem> items;
    readonly string what;
    readonly StackPanel body = new() { Spacing = 12 };
    int at, right;
    bool shown;
    public bool EnglishFirst = true;
    public string Title => "Flashcards";
    public Control View { get; }
    public int At => at;
    public bool Shown => shown;

    public CardsPage(LangCourse c, List<LangItem> items, string what)
    {
        this.c = c; this.items = items; this.what = what;
        View = Ui.Page(body, 700);
        Show();
    }

    void Show()
    {
        body.Children.Clear();
        if (items.Count == 0) { body.Children.Add(LUi.Head("Nothing to review yet", "Listen to a unit or go through its flashcards first; what you meet comes back here when it is due.")); return; }
        if (at >= items.Count) { Done(); return; }
        var it = items[at];
        var dir = new ToggleSwitch { IsChecked = !EnglishFirst, OnContent = c.Name + " first", OffContent = "English first", FontSize = 13 };
        dir.IsCheckedChanged += (s, e) => { EnglishFirst = dir.IsChecked != true; shown = false; Show(); };
        var head = new DockPanel();
        DockPanel.SetDock(dir, Dock.Right);
        head.Children.Add(dir);
        head.Children.Add(Ui.Text(what + "  |  " + (at + 1) + " of " + items.Count, 14, Ui.Muted));
        body.Children.Add(head);
        body.Children.Add(LUi.Bar(at / (double)items.Count, "#F59E0B", 6));

        var face = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        string front = EnglishFirst ? it.English : it.Target;
        var ft = LUi.Wrap(Ui.Text(front, 30, Ui.TextC, true)); ft.TextAlignment = TextAlignment.Center;
        face.Children.Add(ft);
        if (!EnglishFirst) { var sp = LUi.Speaker(() => Lx.Say(c, it.Target)); sp.HorizontalAlignment = HorizontalAlignment.Center; face.Children.Add(sp); }
        if (shown)
        {
            var back = LUi.Wrap(Ui.Text(EnglishFirst ? it.Target : it.English, 26, "#FBBF24", true)); back.TextAlignment = TextAlignment.Center;
            face.Children.Add(back);
            if (EnglishFirst) { var sp = LUi.Speaker(() => Lx.Say(c, it.Target)); sp.HorizontalAlignment = HorizontalAlignment.Center; face.Children.Add(sp); }
            if (!string.IsNullOrEmpty(it.Note)) { var n = LUi.Wrap(Ui.Text(it.Note, 13.5, Ui.AccentText)); n.TextAlignment = TextAlignment.Center; face.Children.Add(n); }
        }
        var card = Ui.Card(face, Ui.Surface, 28);
        card.MinHeight = 220;
        body.Children.Add(card);

        var acts = new UniformGrid { Columns = shown ? 2 : 1 };
        if (!shown)
        {
            var flip = Ui.Button("Show the answer", true, 17);
            flip.HorizontalAlignment = HorizontalAlignment.Stretch;
            flip.Click += (s, e) => Flip();
            acts.Children.Add(flip);
        }
        else
        {
            var no = Ui.Button("Not yet", false, 17); no.Margin = new Thickness(0, 0, 6, 0); no.HorizontalAlignment = HorizontalAlignment.Stretch;
            no.Click += (s, e) => Grade(false);
            var yes = Ui.Button("I knew it", true, 17); yes.Margin = new Thickness(6, 0, 0, 0); yes.HorizontalAlignment = HorizontalAlignment.Stretch;
            yes.Click += (s, e) => Grade(true);
            acts.Children.Add(no); acts.Children.Add(yes);
        }
        body.Children.Add(acts);
        if (!Platform.Touch) body.Children.Add(Ui.Text(shown ? "Keys: 1 not yet, 2 I knew it" : "Key: Space shows the answer", 12, Ui.Dim));
        if (!EnglishFirst && !shown) Lx.Say(c, it.Target);
    }

    public void Flip()
    {
        if (shown || at >= items.Count) return;
        shown = true;
        if (EnglishFirst) Lx.Say(c, items[at].Target);
        Show();
    }

    public void Grade(bool knew)
    {
        if (!shown || at >= items.Count) return;
        Lx.Progress.Grade(c.Language, items[at].Id, knew);
        if (knew) { right++; Lx.Progress.AddPoints(c.Language, 1); }
        else if (items.Count(x => x == items[at]) < 3) items.Add(items[at]);   // comes back at the end, at most twice more
        at++; shown = false;
        Lx.Save();
        Show();
    }

    void Done()
    {
        body.Children.Add(LUi.Head("All done!", right + " right out of " + at + ". The ones you didn't know yet come back sooner; the ones you knew come back later, so they stay."));
        var again = Ui.Button("Back to " + c.Name, true);
        again.HorizontalAlignment = HorizontalAlignment.Left;
        again.Click += (s, e) => MainView.Current.Back();
        body.Children.Add(again);
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter) { if (!shown) { Flip(); return true; } }
        if (shown && (e.Key == Key.D1 || e.Key == Key.NumPad1)) { Grade(false); return true; }
        if (shown && (e.Key == Key.D2 || e.Key == Key.NumPad2)) { Grade(true); return true; }
        return false;
    }

    public void Closed() { LessonAudio.StopSaying(); }
}

// ==================================================================== quiz
public class QuizPage : IPage, IKeyPage
{
    readonly LangCourse c;
    readonly LangUnit u;
    readonly Random rnd;
    readonly StackPanel body = new() { Spacing = 12 };
    readonly List<LangItem> order;
    int at, score;
    int answered = -1;
    List<LangItem> choices = new();
    public const int Count = 10;
    public string Title => "Quiz";
    public Control View { get; }
    public int Score => score;
    public bool Finished => at >= order.Count;
    /// <summary>Odd questions are "hear it": the Spanish is said and the choices are English.</summary>
    public bool Hearing => at % 2 == 1;
    public LangItem Current => at < order.Count ? order[at] : null;
    public IReadOnlyList<LangItem> Choices => choices;

    public QuizPage(LangCourse c, LangUnit u, int seed = -1)
    {
        this.c = c; this.u = u;
        rnd = seed < 0 ? new Random() : new Random(seed);
        order = u.Items.OrderBy(_ => rnd.Next()).Take(Count).ToList();
        View = Ui.Page(body, 700);
        Show();
    }

    void Show()
    {
        body.Children.Clear();
        if (Finished) { Done(); return; }
        var it = order[at];
        if (answered < 0)
        {
            choices = u.Items.Where(x => x != it && x.English != it.English && x.Target != it.Target).OrderBy(_ => rnd.Next()).Take(3).Append(it).OrderBy(_ => rnd.Next()).ToList();
        }
        body.Children.Add(Ui.Text(u.Title + "  |  question " + (at + 1) + " of " + order.Count + "  |  " + score + " right", 14, Ui.Muted));
        body.Children.Add(LUi.Bar(at / (double)order.Count, "#F59E0B", 6));
        var q = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        if (Hearing)
        {
            q.Children.Add(Ui.Text("What does this mean?", 15, Ui.Muted));
            var again = Ui.Button("\U0001F50A  Hear it again", false, 17);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            again.Click += (s, e) => Lx.Say(c, it.Target);
            q.Children.Add(again);
            if (answered >= 0 || Lx.Target(c) == null) { var t = LUi.Wrap(Ui.Text(it.Target, 26, Ui.TextC, true)); t.TextAlignment = TextAlignment.Center; q.Children.Add(t); }
        }
        else
        {
            q.Children.Add(Ui.Text("How do you say it in " + c.Name + "?", 15, Ui.Muted));
            var t = LUi.Wrap(Ui.Text(it.English, 26, Ui.TextC, true)); t.TextAlignment = TextAlignment.Center; q.Children.Add(t);
        }
        body.Children.Add(Ui.Card(q, Ui.Surface, 20));

        var opts = new StackPanel { Spacing = 8 };
        for (int k = 0; k < choices.Count; k++)
        {
            var ch = choices[k];
            string text = Hearing ? ch.English : ch.Target;
            var b = Ui.Button((Platform.Touch ? "" : (k + 1) + "   ") + text, false, 17);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (answered >= 0)
            {
                if (ch == it) { b.Background = Ui.Brush(Ui.Green, 60); b.BorderBrush = Ui.Brush(Ui.Green); }
                else if (k == answered) { b.Background = Ui.Brush(Ui.Red, 60); b.BorderBrush = Ui.Brush(Ui.Red); }
            }
            int kk = k;
            b.Click += (s, e) => Answer(kk);
            opts.Children.Add(b);
        }
        body.Children.Add(opts);
        if (answered >= 0)
        {
            var next = Ui.Button(at + 1 < order.Count ? "Next" : "See how you did", true, 17);
            next.HorizontalAlignment = HorizontalAlignment.Right;
            next.Click += (s, e) => Next();
            body.Children.Add(next);
        }
        else if (Hearing) Lx.Say(c, it.Target);
    }

    public void Answer(int k)
    {
        if (answered >= 0 || Finished || k < 0 || k >= choices.Count) return;
        answered = k;
        var it = order[at];
        bool ok = choices[k] == it;
        if (ok) { score++; Sound(true); Lx.Progress.AddPoints(c.Language, 1); } else Sound(false);
        Lx.Progress.Grade(c.Language, it.Id, ok);
        if (!Hearing || !ok) Lx.Say(c, it.Target);
        Show();
    }

    static void Sound(bool right) => Services.Feel.Right(right);

    public void Next()
    {
        if (answered < 0) return;
        answered = -1; at++;
        if (Finished)
        {
            Lx.Progress.SetQuiz(c.Language, u.Id, score * 10 / Math.Max(1, order.Count));
            Lx.Save();
        }
        Show();
    }

    void Done()
    {
        string praise = score == order.Count ? "Perfect!" : score >= order.Count * 0.8 ? "Great job!" : score >= order.Count / 2 ? "Good going!" : "Keep practising!";
        body.Children.Add(LUi.Head(praise, score + " right out of " + order.Count + "."));
        var row = new WrapPanel();
        var again = Ui.Button("Try again", true); again.Margin = new Thickness(0, 0, 10, 0);
        again.Click += (s, e) => MainView.Current.Replace(new QuizPage(c, u));
        var back = Ui.Button("Back to the unit");
        back.Click += (s, e) => MainView.Current.Back();
        row.Children.Add(again); row.Children.Add(back);
        body.Children.Add(row);
    }

    public bool OnKey(KeyEventArgs e)
    {
        int k = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
        if (k >= 0 && answered < 0) { Answer(k); return true; }
        if ((e.Key == Key.Enter || e.Key == Key.Space) && answered >= 0) { Next(); return true; }
        return false;
    }

    public void Closed() { LessonAudio.StopSaying(); }
}
