// Cert Lab (Learning Lab) - the pages: App Hub's MainView had a rail (Home, Course, Practice machines, Labs, Practice
// exams, PBQ practice, Flash cards, Cheat sheets, About) and a page beside it; here each is a page on Learning Lab's
// stack (Back = the phone's Back button). CertPage is their frame: the content, the status line under it (the last
// sentence for the person) and, on reading pages, the read-aloud bar. CertHomePage = who is learning, the five courses
// with progress, and "Carry on where you left off"; CourseHubPage = one course's sections as tiles and its search box.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public abstract class CertPage : IPage
{
    protected readonly CertApp A;
    readonly DockPanel root = new();
    readonly ContentControl body = new();
    readonly TextBlock status;
    readonly Border statusBar;
    ReadBar readBar;

    protected CertPage(CertApp app)
    {
        A = app;
        status = Ui.Text("", 13, Ui.Muted);
        status.TextWrapping = TextWrapping.Wrap;
        statusBar = new Border { Child = status, Padding = new Thickness(14, 6), Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(0, 1, 0, 0), IsVisible = false };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);
        root.Children.Add(body);
        root.AttachedToVisualTree += (s, e) => { A.StatusChanged += OnStatus; };
        root.DetachedFromVisualTree += (s, e) => { A.StatusChanged -= OnStatus; };
    }

    void OnStatus(string s) { status.Text = s; statusBar.IsVisible = s.Length > 0; }
    /// <summary>The status line's text (checks).</summary>
    public string StatusText => status.Text ?? "";

    public abstract string Title { get; }
    public Control View => root;
    protected Control Body { get => body.Content as Control; set => body.Content = value; }
    public virtual void Closed() { if (readBar != null) A.Reader?.Stop(); }
    public virtual bool OnBack() => false;

    /// <summary>A page that can be read aloud adds the bar (above the status line).</summary>
    protected void AddReadBar(IReadable what)
    {
        readBar = new ReadBar(A, what);
        DockPanel.SetDock(readBar, Dock.Bottom);
        root.Children.Insert(1, readBar);
    }
    public ReadBar ReadBarView => readBar;

    protected static MainView Mv => MainView.Current;
}

// ------------------------------------------------------------ Cert Lab's home
public sealed class CertHomePage : CertPage
{
    public override string Title => "Cert Lab";
    public string Offer { get; private set; } = "";

    public CertHomePage() : this(CertApp.Open(false)) { }
    public CertHomePage(CertApp app) : base(app) { Build(); }

    void Build()
    {
        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(CertUi.H1(A.Me == null ? "Welcome to Cert Lab" : "Welcome back, " + A.Me.Name));
        root.Children.Add(CertUi.Muted("Courses, flash cards, cheat sheets, labs and practice exams for IT certifications, with practice machines to type on. The machines are simulated by Learning Lab itself: nothing is downloaded and nothing reaches a real network."));

        Offer = A.ResumeOffer();
        if (Offer.Length > 0)
        {
            var sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(Ui.Text("Carry on where you left off?", 17, Ui.TextC, true));
            sp.Children.Add(CertUi.P(Offer));
            sp.Children.Add(CertUi.Row(CertUi.Accent("Carry on where you left off", () => { string s = A.ResumeNow(Mv); A.Status(s); Build(); }),
                CertUi.Btn("Not now", () => { A.DismissResume(); Build(); })));
            var pane = CertUi.Pane(sp, 16);
            pane.BorderBrush = Ui.Brush(Ui.Accent);
            root.Children.Add(pane);
        }

        // who is learning
        var who = new StackPanel();
        who.Children.Add(CertUi.H2("Who is learning?"));
        var row = new WrapPanel();
        foreach (var p in A.Store.Profiles)
        {
            var pp = p;
            var b = CertUi.Btn(p.Name, () => { A.Choose(pp); Build(); }, A.Me == p);
            row.Children.Add(b);
        }
        var nameBox = new TextBox { Width = 200, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), PlaceholderText = "A new person's name", VerticalContentAlignment = VerticalAlignment.Center };
        void Add() { var p = A.AddPerson(nameBox.Text, out var problem); if (p == null) A.Status(problem); else Build(); }
        nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        row.Children.Add(nameBox);
        row.Children.Add(CertUi.Btn("Add a person", Add));
        who.Children.Add(row);
        root.Children.Add(CertUi.Pane(who, 16));

        // the courses
        root.Children.Add(CertUi.H2("Courses"));
        var tiles = new List<Control>();
        foreach (var c in A.Courses.OrderBy(c => c.Order)) tiles.Add(CourseCard(c));
        var grid = Ui.Tiles(tiles, 420);
        grid.Margin = new Thickness(-6, 0, -6, 0);
        root.Children.Add(grid);
        root.Children.Add(CertUi.Btn("About Cert Lab", () => Mv.Push(new CertAboutPage(A))));
        Body = Ui.Page(root, 1100);
    }

    Control CourseCard(CourseInfo c)
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(Ui.Text(c.Title, 18, Ui.TextC, true));
        sp.Children.Add(CertUi.Muted(c.Description));
        if (A.Me != null)
        {
            var pr = A.ProgressFor(c);
            int lessons = c.AllLessons.Count(), done = c.AllLessons.Count(l => pr.Lessons.Contains(l.Id));
            int labs = c.Labs.Count, labsDone = c.Labs.Count(l => pr.Labs.TryGetValue(l.Id, out int v) && v >= 100);
            var best = pr.Exams.OrderByDescending(x => x.Scaled).FirstOrDefault();
            int due = Leitner.Session(c.Decks.SelectMany(d => d.Cards), pr, Leitner.Today, 0).Count;
            sp.Children.Add(Stat("Lessons read", done + " of " + lessons, lessons == 0 ? 0 : done / (double)lessons));
            sp.Children.Add(Stat("Labs passed", labsDone + " of " + labs, labs == 0 ? 0 : labsDone / (double)labs));
            sp.Children.Add(Stat("Best practice exam", best == null ? "not taken yet" : best.Scaled + " of " + c.ExamPoints + (best.Passed ? " (pass)" : ""), best == null ? 0 : best.Scaled / (double)c.ExamPoints));
            sp.Children.Add(CertUi.Muted("Flash cards due today: " + due));
        }
        var open = CertUi.Accent(A.Me == null ? "Add a person first" : "Open " + c.Short, () => { A.SetCourse(c); Mv.Push(new CourseHubPage(A)); });
        open.IsEnabled = A.Me != null;
        open.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(open);
        foreach (var pr in c.Problems.Take(3)) sp.Children.Add(CertUi.Muted("Course file problem: " + pr));
        return Ui.Card(sp, Ui.Surface, 16);
    }

    static Control Stat(string label, string value, double frac)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        g.Children.Add(Ui.Text(label, 13, Ui.Muted));
        var v = Ui.Text(value, 13, Ui.TextC, true); v.TextWrapping = TextWrapping.NoWrap; Grid.SetColumn(v, 1); g.Children.Add(v);
        return new StackPanel { Children = { g, CertUi.Bar(frac) } };
    }

    public void Refresh() => Build();
}

// ------------------------------------------------------------ one course
public sealed class CourseHubPage : CertPage
{
    readonly CourseInfo course;
    public override string Title => course?.Short ?? "Course";
    TextBox search;

    public CourseHubPage(CertApp app) : base(app) { course = app.Course; Build(); }

    void Build()
    {
        var c = course;
        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(CertUi.H1(c.Title));
        root.Children.Add(CertUi.Muted(c.Exam.Length > 0 ? "Exam: " + c.Exam + (A.Me != null ? "  -  learning as " + A.Me.Name : "") : ""));
        search = new TextBox { MinHeight = 44, PlaceholderText = "Search the course (lessons, labs, cards, cheat sheets, manual pages)", Margin = new Thickness(0, 6, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
        search.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; OpenSearch(search.Text); } };
        var sgo = CertUi.Btn("Search", () => OpenSearch(search.Text));
        var srow = new DockPanel();
        DockPanel.SetDock(sgo, Dock.Right); sgo.Margin = new Thickness(8, 6, 0, 10);
        srow.Children.Add(sgo); srow.Children.Add(search);
        root.Children.Add(srow);
        var pr = A.Progress;
        int lessons = c.AllLessons.Count(), done = c.AllLessons.Count(l => pr.Lessons.Contains(l.Id));
        var tiles = new List<Control>
        {
            Tile("Lessons", "▤", done + " of " + lessons + " read. Each has examples, a try-it on the practice machines and a quick quiz.", () => new LessonsPage(A)),
            Tile(c.Simulator == "endpoint" ? "Practice tenant" : c.Simulator == "winserver" ? "Practice network" : "Practice machines", "▣", "Type on the simulated machines as much as you like. Reset them any time.", () => new SimPage(A, null, "practice")),
            Tile("Labs", "⚑", c.Labs.Count + " labs: tasks on the machines, checked when you say so, with hints and the model solution.", () => new LabsPage(A)),
            Tile("Practice exams", "⏱", c.Exams.Count + " exams with a timer, graded like the real one (the details change each attempt).", () => new ExamsPage(A)),
        };
        if (c.Practice.Count > 0) tiles.Add(Tile("PBQ practice", "◈", "Performance-based questions: firewall rules, logs, matching, placing devices, certificates, risk.", () => new PbqListPage(A)));
        tiles.Add(Tile("Flash cards", "❐", c.Decks.Sum(d => d.Cards.Count) + " cards in boxes: the ones you know come back later and later.", () => new CardsPage(A)));
        tiles.Add(Tile("Cheat sheets", "☰", c.Sheets.Count + " sheets, one per objective, searchable.", () => new CheatPage(A)));
        tiles.Add(Tile("About this course", "ⓘ", "What the practice machines are, and who made the course.", () => new CertAboutPage(A)));
        var grid = Ui.Tiles(tiles, 300);
        grid.Margin = new Thickness(-6, 0, -6, 0);
        root.Children.Add(grid);
        Body = Ui.Page(root, 1100);
    }

    void OpenSearch(string q)
    {
        if ((q ?? "").Trim().Length < 2) { A.Status("Type at least two letters to search."); return; }
        Mv.Push(new SearchPage(A, q));
    }

    Control Tile(string name, string glyph, string blurb, Func<IPage> open)
    {
        var sp = new StackPanel { Spacing = 4 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(Ui.Text(glyph, 22, Ui.AccentText));
        var t = Ui.Text(name, 17, Ui.TextC, true); t.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(t);
        sp.Children.Add(head);
        sp.Children.Add(Ui.Text(blurb, 13.5, Ui.Muted));
        return Ui.Tile(sp, Ui.Accent, () => CertUi.Safe(() => Mv.Push(open())));
    }
}

// ------------------------------------------------------------ About
public sealed class CertAboutPage : CertPage
{
    public override string Title => "About Cert Lab";
    public CertAboutPage(CertApp app) : base(app)
    {
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("About Cert Lab"));
        root.Children.Add(CertUi.P("Cert Lab teaches for IT certifications with lessons, flash cards, cheat sheets, labs and practice exams, and gives you practice machines to type on. The machines are simulated by Learning Lab itself: no virtual machine, no download, no network traffic, and nothing signs in anywhere."));
        foreach (var c in app.Courses.OrderBy(c => c.Order))
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(c.Title, 16, Ui.TextC, true));
            if (c.Disclaimer.Length > 0) sp.Children.Add(CertUi.P(c.Disclaimer));
            if (c.AboutMachines.Length > 0) foreach (var para in c.AboutMachines.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) sp.Children.Add(CertUi.P(para));
            root.Children.Add(CertUi.Pane(sp, 14));
        }
        root.Children.Add(CertUi.H2("Your data"));
        root.Children.Add(CertUi.P("Progress, flash card boxes and saved machines are kept on this device, in " + app.DataDir + ". An exam or lab in progress is kept when Learning Lab closes and offered back next time."));
        root.Children.Add(CertUi.H2("The terminal"));
        root.Children.Add(CertUi.P("Type in the line at the bottom; the key row gives Tab (finish a word, twice to list), Ctrl+C (stop a command), Ctrl+D (end input), the command history, Copy, Paste and Select all. With a keyboard: Up / Down bring back earlier commands, Ctrl+L clears, Page Up / Page Down scroll, Ctrl+Shift+C copies and Ctrl+V pastes. On the boot menu and in vi, the key row has the arrows, Esc and Enter, and the Type here box sends letters straight to the screen."));
        Body = Ui.Page(root, 900);
    }
}
