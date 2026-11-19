// Cert Lab (Learning Lab) - the course's lessons (App Hub's CoursePage, as two pages for a phone: the units and their
// lessons, then a lesson), a lesson with its example terminals, try-it and quick quiz (read aloud through the bar), and
// the labs page.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class LessonsPage : CertPage
{
    public override string Title => "Lessons";
    public LessonsPage(CertApp app) : base(app) { Build(); }
    public void Build()
    {
        var c = A.Course;
        var pr = A.Progress;
        var root = new StackPanel();
        root.Children.Add(CertUi.H1(c.Title));
        var next = c.Lesson(pr.LastLesson) ?? c.AllLessons.FirstOrDefault(l => !pr.Lessons.Contains(l.Id)) ?? c.AllLessons.FirstOrDefault();
        if (next != null) root.Children.Add(CertUi.Accent("Continue: " + next.Title, () => Mv.Push(new LessonPage(A, next))));
        foreach (var u in c.Units)
        {
            root.Children.Add(CertUi.H2(u.Title));
            if (u.Objective.Length > 0) root.Children.Add(CertUi.Muted("Exam objective: " + u.Objective));
            foreach (var l in u.Lessons)
            {
                var ll = l;
                bool done = pr.Lessons.Contains(l.Id);
                var row = new DockPanel();
                var mark = Ui.Text(done ? "✔" : "•", 16, done ? Ui.Green : Ui.Muted); mark.Width = 24; mark.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(mark, Dock.Left);
                row.Children.Add(mark);
                row.Children.Add(Ui.Text(l.Title, 15, Ui.TextC));
                var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = row, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Ui.Brush(Ui.Surface), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6) };
                b.Click += (s, e) => Mv.Push(new LessonPage(A, ll));
                root.Children.Add(b);
            }
        }
        Body = Ui.Page(root, 900);
    }
}

public sealed class LessonPage : CertPage, IReadable
{
    public readonly Lesson L;
    public override string Title => L.Title;
    readonly List<Control> blockViews = new(), quizQs = new();
    Control tryView;
    public readonly List<List<RadioButton>> QuizChoices = new();
    public TextBlock QuizScore { get; private set; }
    readonly List<(Control el, string text, string kind)> reads = new();

    public LessonPage(CertApp app, Lesson l) : base(app)
    {
        L = l;
        var pr = A.Progress;
        pr.LastLesson = l.Id;
        var root = new StackPanel();
        var u = A.Course.Unit(l.UnitId);
        if (u != null) root.Children.Add(CertUi.Muted(u.Title + (u.Objective.Length > 0 ? "  -  exam objective: " + u.Objective : "")));
        var h = CertUi.H1(l.Title);
        root.Children.Add(h);
        reads.Add((h, l.Title, "h"));
        foreach (var b in l.Blocks)
        {
            var bv = new Border { Child = BlockView(b, out string readText, out string kind), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2), Margin = new Thickness(-6, -2, -6, -2) };
            blockViews.Add(bv);
            root.Children.Add(bv);
            reads.Add((bv, readText, kind));
        }
        if (l.Try != null)
        {
            var t = new StackPanel();
            t.Children.Add(Ui.Text("Try it on the practice machines", 17, Ui.AccentText, true));
            foreach (var it in l.Try.Items) { var tt = CertUi.Rich(it.Text); tt.Margin = new Thickness(0, 6, 0, 6); t.Children.Add(tt); reads.Add((tt, CertUi.Plain(it.Text), "p")); }
            if (pr.TriesDone.Contains(l.Id)) t.Children.Add(CertUi.Muted("✔ You did this one."));
            t.Children.Add(CertUi.Accent("Open the practice machines for this", () => OpenTry()));
            tryView = CertUi.Pane(t, 16);
            root.Children.Add(tryView);
        }
        if (l.Quiz.Count > 0) root.Children.Add(QuizView());
        var all = A.Course.AllLessons.ToList();
        int i = all.IndexOf(l);
        var nav = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        nav.Children.Add(CertUi.Accent(pr.Lessons.Contains(l.Id) ? "Next lesson" : "Mark as read", () =>
        {
            if (!pr.Lessons.Contains(l.Id)) pr.Lessons.Add(l.Id);
            A.SaveProgress();
            if (i + 1 < all.Count) Mv.Replace(new LessonPage(A, all[i + 1])); else Mv.Back();
        }));
        if (i > 0) nav.Children.Add(CertUi.Btn("Previous lesson", () => Mv.Replace(new LessonPage(A, all[i - 1]))));
        if (i + 1 < all.Count) nav.Children.Add(CertUi.Btn("Skip to the next", () => Mv.Replace(new LessonPage(A, all[i + 1]))));
        root.Children.Add(nav);
        Scroller = Ui.Page(root, 900);
        Body = Scroller;
        AddReadBar(this);
        A.SaveProgress();
    }
    public ScrollViewer Scroller { get; }

    public SimPage OpenTry()
    {
        var sp = new SimPage(A, L.Try.Instance(1), "try") { FromLesson = L };
        Mv.Push(sp);
        return sp;
    }

    Control BlockView(Block b, out string read, out string kind)
    {
        kind = b.Kind == "h" ? "h" : b.Kind == "terminal" || b.Kind == "file" ? "code" : "p";
        read = CertUi.Plain(b.Kind == "ul" ? string.Join(". ", b.Items) : b.Text);
        switch (b.Kind)
        {
            case "h": return CertUi.H2(b.Text);
            case "terminal":
                {
                    string shown = string.Join("\n", b.Text.Replace("\r", "").Split('\n').Where(x => !x.StartsWith("#setup ", StringComparison.Ordinal) && !x.StartsWith("#machine ", StringComparison.Ordinal)));
                    read = "Code: " + shown;
                    return CertUi.TermBlock(shown);
                }
            case "file": read = "Code: " + b.Text; return CertUi.TermBlock(b.Text);
            case "note":
                return new Border { Child = CertUi.Rich(b.Text), Background = Ui.Brush(Ui.AccentSoft), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8), Margin = new Thickness(0, 4, 0, 12) };
            case "ul":
                {
                    var sp = new StackPanel { Margin = new Thickness(6, 0, 0, 10) };
                    foreach (var it in b.Items)
                    {
                        var g = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                        var dot = Ui.Text("•", 15, Ui.AccentText); dot.Width = 16;
                        DockPanel.SetDock(dot, Dock.Left);
                        g.Children.Add(dot);
                        g.Children.Add(CertUi.Rich(it));
                        sp.Children.Add(g);
                    }
                    return sp;
                }
        }
        var p = CertUi.Rich(b.Text);
        p.Margin = new Thickness(0, 0, 0, 10);
        return p;
    }

    Control QuizView()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Quick quiz", 17, Ui.AccentText, true));
        var feedback = new List<TextBlock>();
        int qn = 0;
        foreach (var q in L.Quiz)
        {
            qn++;
            var qt = CertUi.RichText(qn + ". " + q.Q); qt.FontWeight = FontWeight.SemiBold; qt.Margin = new Thickness(0, 10, 0, 4);
            quizQs.Add(qt); sp.Children.Add(qt);
            reads.Add((qt, CertUi.Plain(q.Q), "question"));
            var g = new List<RadioButton>();
            foreach (var ch in q.Choices)
            {
                var rb = new RadioButton { Content = CertUi.PlainRich(ch), GroupName = L.Id + "-" + qn, Margin = new Thickness(6, 2, 0, 2), MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
                g.Add(rb); sp.Children.Add(rb);
                reads.Add((rb, CertUi.Plain(ch), "choice"));
            }
            QuizChoices.Add(g);
            var fb = Ui.Text("", 14, Ui.TextC); fb.Margin = new Thickness(6, 4, 0, 4);
            feedback.Add(fb); sp.Children.Add(fb);
        }
        QuizScore = Ui.Text("", 15, Ui.TextC, true);
        sp.Children.Add(CertUi.Accent("Check my answers", () =>
        {
            int right = 0;
            for (int i = 0; i < L.Quiz.Count; i++)
            {
                int pick = QuizChoices[i].FindIndex(r => r.IsChecked == true);
                bool ok = pick == L.Quiz[i].Answer;
                if (ok) right++;
                feedback[i].Text = (pick < 0 ? "Not answered. " : ok ? "✔ Right. " : "✘ Not quite: the answer is \"" + L.Quiz[i].Choices[L.Quiz[i].Answer].Replace("`", "") + "\". ") + L.Quiz[i].Why;
                feedback[i].Foreground = Ui.Brush(ok ? Ui.Green : Ui.Yellow);
            }
            QuizScore.Text = right + " of " + L.Quiz.Count + " right.";
            var pr = A.Progress;
            int pct = right * 100 / Math.Max(1, L.Quiz.Count);
            if (!pr.Quiz.TryGetValue(L.Id, out int old) || pct > old) pr.Quiz[L.Id] = pct;
            A.SaveProgress();
        }));
        sp.Children.Add(QuizScore);
        return CertUi.Pane(sp, 16);
    }

    /// <summary>Search opens a lesson at a block ("block"), the try-it ("try") or a quiz question ("quiz") and lights it up.</summary>
    public Control Reveal(string part, int index)
    {
        Control el = null;
        if (part == "block" && index >= 0 && index < blockViews.Count) el = blockViews[index];
        else if (part == "try") el = tryView;
        else if (part == "quiz" && index >= 0 && index < quizQs.Count) el = quizQs[index];
        if (el != null) RevealUi.Reveal(el, A.Quiet);
        return el;
    }

    public ReadDoc ReadContent(bool readCode)
    {
        var d = new ReadDoc { Title = L.Title };
        foreach (var r in reads)
        {
            if (r.kind == "code" && !readCode) continue;
            d.Add(r.text, r.el, r.kind);
        }
        return d;
    }
}

/// <summary>Scrolling to an element and lighting it up (search results open at their spot this way).</summary>
public static class RevealUi
{
    /// <summary>The element lit up last (checks).</summary>
    public static Control LastRevealed;
    public static void Reveal(Control el, bool quiet)
    {
        if (el == null) return;
        LastRevealed = el;
        var hi = Ui.Brush("#2A4A80");
        if (el is Border b) b.Background = hi;
        else if (el is Panel p) p.Background = hi;
        else if (el is TextBlock t) t.Background = hi;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => el.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        if (!quiet)
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (el is Border b2) b2.Background = Brushes.Transparent;
                else if (el is Panel p2) p2.Background = Brushes.Transparent;
                else if (el is TextBlock t2) t2.Background = Brushes.Transparent;
            }, TimeSpan.FromSeconds(2.5));
    }
}

public sealed class LabsPage : CertPage
{
    public override string Title => "Labs";
    public LabsPage(CertApp app) : base(app)
    {
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("Labs"));
        root.Children.Add(CertUi.Muted("Each lab prepares the practice machines, gives you a few tasks and checks your work. Hints and the model solution are there when you need them."));
        var c = A.Course;
        var pr = A.Progress;
        foreach (var u in c.Units)
        {
            var labs = c.Labs.Where(l => l.Unit == u.Id).ToList();
            if (labs.Count == 0) continue;
            root.Children.Add(CertUi.H2(u.Title));
            foreach (var lab in labs) root.Children.Add(Row(lab, pr));
        }
        var other = c.Labs.Where(l => c.Unit(l.Unit) == null).ToList();
        if (other.Count > 0) { root.Children.Add(CertUi.H2("More labs")); foreach (var lab in other) root.Children.Add(Row(lab, pr)); }
        Body = Ui.Page(root, 1000);
    }

    Control Row(TaskSet lab, CourseProgress pr)
    {
        bool tried = pr.Labs.TryGetValue(lab.Id, out int best);
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(lab.Title, 16, Ui.TextC, true));
        sp.Children.Add(CertUi.Muted(lab.Items.Count + " task" + (lab.Items.Count == 1 ? "" : "s") + ", about " + lab.Minutes + " minutes, " + lab.Machines + " machine" + (lab.Machines == 1 ? "" : "s") + (tried ? "  -  best: " + best + "%" : "")));
        if (lab.Intro.Length > 0) sp.Children.Add(CertUi.Muted(lab.Intro));
        sp.Children.Add(CertUi.Accent(tried && best >= 100 ? "Do it again" : "Start", () => StartLab(A, lab)));
        return CertUi.Pane(sp, 14);
    }

    public static SimPage StartLab(CertApp a, TaskSet lab)
    {
        int iseed = a.Quiet ? 1 : Environment.TickCount & 0x7FFFFFFF;
        var sp = new SimPage(a, lab.Instance(iseed), "lab") { InstanceSeed = iseed };
        MainView.Current.Push(sp);
        return sp;
    }
}
