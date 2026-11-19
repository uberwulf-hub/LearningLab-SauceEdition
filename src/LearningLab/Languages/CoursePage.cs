// Learning Lab - Languages: one course as a path. At the top: today's goal (a ring of points against the goal) with the
// streak, and a big Continue for the next lesson. Then the units in order by level, each with its lessons (done, next,
// not started; every lesson can be opened) and its phrases, flashcards and quiz. Without lesson files the units open
// as before.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;

namespace LearningLab.Languages;

static partial class LUi
{
    public const string Amber = "#F59E0B", AmberText = "#FBBF24";

    /// <summary>The Languages main button: amber with dark text.</summary>
    public static Button Primary(string text, double size = 16)
    {
        var b = Ui.Button(text, false, size);
        b.Background = Ui.Brush(Amber); b.BorderBrush = Ui.Brush(Amber); b.Foreground = Ui.Brush("#111111");
        b.FontWeight = FontWeight.SemiBold;
        b.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };   // long titles wrap on a phone
        b.Click += (s, e) => Services.Feel.Tap();
        return b;
    }

    /// <summary>A button whose text wraps (choices, tiles with long phrases).</summary>
    public static Button Choice(string text, double size = 16)
    {
        var b = Ui.Button("", false, size);
        b.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.MinHeight = 48;
        return b;
    }

    public static void Mark(Button b, bool right)
    {
        string c = right ? Ui.Green : Ui.Red;
        b.Background = Ui.Brush(c, 50); b.BorderBrush = Ui.Brush(c);
    }

    public static void Selected(Button b, bool on)
    {
        b.Background = Ui.Brush(on ? Amber : Ui.Surface3, on ? (byte)60 : (byte)255);
        b.BorderBrush = Ui.Brush(on ? Amber : Ui.BorderStrong);
    }

    /// <summary>A row of buttons where one is chosen (settings, the verb trainer).</summary>
    public static WrapPanel Segmented<T>(IList<(string Label, T Value)> options, T current, Action<T> chose)
    {
        var w = new WrapPanel();
        var buttons = new List<Button>();
        foreach (var o in options)
        {
            var b = Ui.Button(o.Label, false, 15);
            b.Margin = new Thickness(0, 0, 8, 8);
            var v = o.Value;
            Selected(b, Equals(v, current));
            b.Click += (s, e) => { foreach (var x in buttons) Selected(x, x == b); chose(v); };
            buttons.Add(b);
            w.Children.Add(b);
        }
        return w;
    }

    /// <summary>A ring: today's points against the goal.</summary>
    public static Control Ring(int points, int goal, double size = 64)
    {
        double frac = goal <= 0 ? 0 : Math.Min(1.0, points / (double)goal);
        double c = size / 2, th = size / 9;
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Stroke = Ui.Brush(Ui.Surface3), StrokeThickness = th, Margin = new Thickness(th / 2) });
        if (frac > 0) g.Children.Add(new Avalonia.Controls.Shapes.Path { Data = LearningGames.MathHomePage.Arc(frac, c, c - th / 2 - th / 2), Stroke = Ui.Brush(frac >= 1 ? Ui.Green : Amber), StrokeThickness = th, StrokeLineCap = PenLineCap.Round });
        g.Children.Add(new TextBlock { Text = frac >= 1 ? "✓" : points.ToString(), FontSize = size / 3.6, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(frac >= 1 ? Ui.Green : Ui.TextC), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return g;
    }

    /// <summary>Today's goal card: ring, points, streak.</summary>
    public static Control GoalCard(LangCourse c)
    {
        var p = Lx.Progress;
        int pts = p.PointsToday(c.Language), goal = p.Goal, streak = p.Streak(c.Language);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        words.Children.Add(Ui.Text("Today", 13, Ui.Muted));
        words.Children.Add(Ui.Text(Math.Min(pts, 9999) + " of " + goal + " points", 17, Ui.TextC, true));
        words.Children.Add(Ui.Text(pts >= goal ? "You're done for today. Nice work!" : streak > 0 ? streak + (streak == 1 ? " day" : " days") + " in a row" : "A lesson is about 15 points.", 13, pts >= goal ? Ui.Green : Ui.Muted));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { Ring(pts, goal), words } };
        return Ui.Card(row, Ui.Surface, 14);
    }

    /// <summary>The first unit with nothing met yet counts as reached too, so the first stories open at once.</summary>
    public static bool Reached(LangCourse c, LangContent content, string unitId)
    {
        var u = c.Unit(unitId);
        if (u == null) return false;
        var p = Lx.Progress;
        int far = 1;
        foreach (var x in c.Units)
            if (x.Items.Any(i => p.Seen(c.Language, i.Id)) || content.LessonsOf(x.Id).Any(l => p.LessonDone(c.Language, l.Id))) far = Math.Max(far, x.Number);
        return u.Number <= far;
    }

    /// <summary>The next lesson: the first not done, in course order (null when there are none or all are done).</summary>
    public static LangLesson NextLesson(LangCourse c, LangContent content)
    {
        return content.Lessons.FirstOrDefault(l => c.Unit(l.Unit) != null && !Lx.Progress.LessonDone(c.Language, l.Id));
    }
}

public class CoursePage : IPage
{
    readonly string id;
    readonly LangCourse c;
    readonly LangContent content;
    readonly StackPanel body = new() { Spacing = 8 };
    public string Title => c.Name;
    public Control View { get; }
    /// <summary>The lesson the Continue button opens (null without lessons).</summary>
    public LangLesson NextLesson { get; private set; }

    public CoursePage(string id)
    {
        this.id = id;
        c = Lx.Course(id);
        content = Lx.Content(id);
        View = Ui.Page(body, 1000);
        View.AttachedToVisualTree += (s, e) => Fill();
        Fill();
    }

    void Fill()
    {
        body.Children.Clear();
        var p = Lx.Progress;
        var (learned, met, total) = LanguagesPage.Totals(c);
        int due = p.DueItems(c).Count;
        body.Children.Add(LUi.Head(c.Name, c.Variant));
        LUi.Warn(body, c);

        // today's goal and Continue
        NextLesson = LUi.NextLesson(c, content);
        var top = new WrapPanel();
        var goal = LUi.GoalCard(c);
        goal.Margin = new Thickness(0, 0, 12, 12);
        top.Children.Add(goal);
        top.Children.Add(ContinueCard());
        body.Children.Add(top);

        var car = Ui.Button("\U0001F697  In the car", false, 16);
        car.Click += (s, e) => MainView.Current.Push(new CarPage(c, null));
        var rev = Ui.Button(due > 0 ? "Review " + due + " due" : "Review", false, 16);
        rev.IsEnabled = met > 0;
        rev.Click += (s, e) => MainView.Current.Push(new CardsPage(c, Lesson.ReviewMix(c, p, 20, new Random()), "Review"));
        var verbs = Ui.Button("Verb trainer", false, 16);
        verbs.IsVisible = content.Verbs.Count > 0;
        verbs.Click += (s, e) => MainView.Current.Push(new VerbPage(c, content));
        var stories = Ui.Button("Stories", false, 16);
        stories.IsVisible = content.Stories.Count > 0;
        stories.Click += (s, e) => MainView.Current.Push(new StoriesPage(c, content));
        var cartoons = Ui.Button("\U0001F3AC  Cartoons", false, 16);
        cartoons.IsVisible = content.Cartoons.Count > 0;
        cartoons.Click += (s, e) => MainView.Current.Push(new CartoonsPage(c, content));
        var voices = Ui.Button("Voices", false, 16);
        voices.Click += (s, e) => MainView.Current.Push(new VoicesPage(c));
        var settings = Ui.Button("Settings", false, 16);
        settings.Click += (s, e) => MainView.Current.Push(new LangSettingsPage(c));
        var acts = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { car, rev, verbs, stories, cartoons, voices, settings }) { b.Margin = new Thickness(0, 0, 8, 8); acts.Children.Add(b); }
        body.Children.Add(acts);

        var stats = new WrapPanel();
        void Stat(string big, string small)
        {
            stats.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 28, 6), Children = { Ui.Text(big, 20, Ui.TextC, true), Ui.Text(small, 12.5, Ui.Muted) } });
        }
        Stat(learned + " / " + total, "phrases learned");
        if (content.Lessons.Count > 0) Stat(content.Lessons.Count(l => p.LessonDone(c.Language, l.Id)) + " / " + content.Lessons.Count, "lessons done");
        Stat(Ui.F(p.Listened(c.Language)), "minutes listened");
        body.Children.Add(stats);

        LangUnit nextUnit = NextLesson != null ? c.Unit(NextLesson.Unit) : c.Units.FirstOrDefault(u => p.UnitState(c.Language, u).Learned < u.Items.Count * 0.8) ?? c.Units[0];
        foreach (var level in c.Units.Select(u => u.Level).Distinct().OrderBy(x => x))
        {
            var h = Ui.Text(LangCourse.LevelName(level), 18, LUi.AmberText, true);
            h.Margin = new Thickness(0, 14, 0, 2);
            body.Children.Add(h);
            var tiles = new List<Control>();
            foreach (var u in c.Units.Where(x => x.Level == level)) tiles.Add(UnitCard(u, u == nextUnit));
            var g = Ui.Tiles(tiles, 300, 10);
            g.Margin = new Thickness(-5, 0, -5, 0);
            body.Children.Add(g);
        }
    }

    Control ContinueCard()
    {
        var sp = new StackPanel { Spacing = 6 };
        Button go;
        if (NextLesson != null)
        {
            var u = c.Unit(NextLesson.Unit);
            sp.Children.Add(Ui.Text("Up next  ·  Unit " + u.Number + ": " + u.Title, 13, Ui.Muted));
            sp.Children.Add(LUi.Wrap(Ui.Text(NextLesson.Title, 19, Ui.TextC, true)));
            if (!string.IsNullOrEmpty(NextLesson.Goal)) sp.Children.Add(LUi.Wrap(Ui.Text(NextLesson.Goal, 13.5, Ui.Muted)));
            go = LUi.Primary(Lx.Progress.LessonDone(c.Language, content.Lessons[0].Id) ? "Continue" : "Start the first lesson", 18);
            var l = NextLesson;
            go.Click += (s, e) => MainView.Current.Push(new LessonPage(c, content, l));
        }
        else if (content.Lessons.Count > 0)
        {
            sp.Children.Add(Ui.Text("Every lesson done!", 19, Ui.Green, true));
            sp.Children.Add(LUi.Wrap(Ui.Text("Keep it fresh: review, stories and the verb trainer.", 13.5, Ui.Muted)));
            go = LUi.Primary("Review", 18);
            go.Click += (s, e) => MainView.Current.Push(new CardsPage(c, Lesson.ReviewMix(c, Lx.Progress, 20, new Random()), "Review"));
        }
        else
        {
            var u = c.Units.FirstOrDefault(x => Lx.Progress.UnitState(c.Language, x).Learned < x.Items.Count * 0.8) ?? c.Units[0];
            sp.Children.Add(Ui.Text("Up next  ·  Unit " + u.Number, 13, Ui.Muted));
            sp.Children.Add(LUi.Wrap(Ui.Text(u.Title, 19, Ui.TextC, true)));
            go = LUi.Primary("Continue", 18);
            go.Click += (s, e) => MainView.Current.Push(new UnitPage(c, u));
        }
        go.Margin = new Thickness(0, 6, 0, 0);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        go.MinWidth = 200;
        sp.Children.Add(go);
        var card = Ui.Card(sp, Ui.Surface2, 16);
        card.BorderBrush = Ui.Brush(LUi.Amber, 120);
        card.MinWidth = 260; card.MaxWidth = 520;
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    Control UnitCard(LangUnit u, bool isNext)
    {
        var p = Lx.Progress;
        var st = p.UnitState(c.Language, u);
        var lessons = content.LessonsOf(u.Id);
        var sp = new StackPanel { Spacing = 6 };
        var top = new DockPanel();
        var num = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Brush(isNext ? LUi.Amber : Ui.Surface3), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = u.Number.ToString(), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(isNext ? "#111111" : Ui.TextC), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        DockPanel.SetDock(num, Dock.Left);
        top.Children.Add(num);
        top.Children.Add(LUi.Wrap(Ui.Text(u.Title, 16.5, Ui.TextC, true)));
        sp.Children.Add(top);
        int done = lessons.Count(l => p.LessonDone(c.Language, l.Id));
        string state = lessons.Count > 0 ? done + " of " + lessons.Count + " lessons  ·  " + st.Learned + " of " + st.Total + " phrases learned"
            : st.Learned >= st.Total ? "All learned" : st.Met == 0 ? (isNext ? "Up next" : "Not started") : st.Learned + " of " + st.Total + " learned";
        sp.Children.Add(LUi.Wrap(Ui.Text(state, 12.5, Ui.Muted)));
        sp.Children.Add(LUi.Bar(st.Total == 0 ? 0 : st.Learned / (double)st.Total, st.Learned >= st.Total ? Ui.Green : LUi.Amber, 6));
        var next = LUi.NextLesson(c, content);
        foreach (var l in lessons)
        {
            bool isDone = p.LessonDone(c.Language, l.Id), isNextL = l == next;
            string mark = isDone ? "✓" : isNextL ? "→" : "○";
            var row = new DockPanel();
            var m = Ui.Text(mark, 15, isDone ? Ui.Green : isNextL ? LUi.Amber : Ui.Dim, true);
            m.Width = 22; m.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(m, Dock.Left);
            row.Children.Add(m);
            if (isDone)
            {
                var best = Ui.Text(p.LessonBest(c.Language, l.Id) + "%", 12.5, Ui.Muted);
                best.VerticalAlignment = VerticalAlignment.Center; best.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(best, Dock.Right);
                row.Children.Add(best);
            }
            row.Children.Add(new TextBlock { Text = l.Title, FontSize = 14.5, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(isNextL ? LUi.AmberText : Ui.TextC), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44, Padding = new Thickness(10, 6),
                Background = Ui.Brush(isNextL ? LUi.Amber : Ui.Surface2, isNextL ? (byte)40 : (byte)255), BorderBrush = Ui.Brush(isNextL ? LUi.Amber : Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            var ll = l;
            b.Click += (s, e) => MainView.Current.Push(new LessonPage(c, content, ll));
            sp.Children.Add(b);
        }
        var phrases = Ui.Button(lessons.Count > 0 ? "Phrases, flashcards, quiz" : "Open", false, 14);
        phrases.HorizontalAlignment = HorizontalAlignment.Stretch;
        phrases.Click += (s, e) => MainView.Current.Push(new UnitPage(c, u));
        sp.Children.Add(phrases);
        var card = Ui.Card(sp, Ui.Surface, 14);
        if (isNext) card.BorderBrush = Ui.Brush(LUi.Amber, 140);
        return card;
    }
}

// ==================================================================== settings
public class LangSettingsPage : IPage
{
    readonly LangCourse c;
    public string Title => "Settings";
    public Control View { get; }

    public LangSettingsPage(LangCourse c)
    {
        this.c = c;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(LUi.Head("Settings", "How much to aim for each day. Points come from lessons, flashcards, quizzes, stories and the verb trainer."));
        body.Children.Add(Ui.Text("Daily goal", 16, Ui.TextC, true));
        var opts = LangProgress.Goals.Select(g => (g + " points" + (g == 10 ? " (relaxed)" : g == 20 ? " (steady)" : g == 30 ? " (keen)" : " (intense)"), g)).ToList();
        body.Children.Add(LUi.Segmented(opts, Lx.Progress.Goal, g => { Lx.Progress.Goal = g; Lx.Save(); }));
        body.Children.Add(LUi.Wrap(Ui.Text("A lesson is about 15 points: one for each exercise right the first time and five for finishing.", 13, Ui.Muted)));
        body.Children.Add(Ui.Text("Speaking", 16, Ui.TextC, true));
        var r = Services.PlatformAudio.Recognizer;
        bool can = r != null && r.Available(c.Voice ?? c.Language);
        body.Children.Add(LUi.Wrap(Ui.Text(can
            ? "This device can listen to your " + c.Name + " on the device itself, so \"Say it\" exercises check what you said. Nothing is sent anywhere."
            : "This device can't recognise " + c.Name + " speech offline, so in \"Say it\" exercises you say it out loud, listen, and mark yourself.", 13.5, Ui.Muted)));
        var voices = Ui.Button("Voices");
        voices.HorizontalAlignment = HorizontalAlignment.Left;
        voices.Click += (s, e) => MainView.Current.Push(new VoicesPage(c));
        body.Children.Add(voices);
        View = Ui.Page(body, 760);
    }
}
