// Cert Lab (Learning Lab) - practice exams (App Hub's UiExam.cs, UiExamItems.cs, UiExamSeq.cs): the list with the time
// to allow, the exam itself (a timer, the tasks and questions, the consoles), and the results after the grader has
// rebooted the machines (score on the course's scale, by objective, each item with what was checked and the model
// solution). Question kinds: one answer (choice), several (multi), put in order (order: tap actions into the answer
// area, then move them up and down: touch first instead of dragging), Yes / No statements (yesno), case studies (a
// scenario in sections that several questions refer to) and performance-based questions (PbqView). An exam of
// questions only (Security+) runs one item at a time with a numbered navigator, flags and a review screen. Leaving an
// exam keeps it (answers, flags, the time left) for "Carry on where you left off".
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

/// <summary>The marks of reviews and flags.</summary>
public static class Mark
{
    public const string Yes = "✔ ", Half = "◑ ", No = "✘ ", Flag = "⚑";
}

public sealed class ExamsPage : CertPage
{
    public override string Title => "Practice exams";
    public ComboBox Minutes { get; }

    public ExamsPage(CertApp app) : base(app)
    {
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("Practice exams"));
        var c = A.Course;
        if (c.ExamNote.Length > 0) root.Children.Add(CertUi.P(c.ExamNote + " The passing mark is " + c.PassMark + " of " + c.ExamPoints + " points."));
        else root.Children.Add(CertUi.P("Like the real " + c.Exam + ": hands-on tasks on the machines, a time limit, and everything must survive a reboot: when you finish, the grader reboots the machines and then checks them. The passing mark is " + c.PassMark + " of " + c.ExamPoints + " points. Some details (names, numbers) change from one attempt to the next."));
        var pr = A.Progress;
        int minutes = pr.ExamMinutes > 0 ? pr.ExamMinutes : c.ExamMinutes;
        var timeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        var tl = Ui.Text("Time allowed: ", 15, Ui.TextC); tl.VerticalAlignment = VerticalAlignment.Center; tl.Margin = new Thickness(0, 0, 8, 0);
        timeRow.Children.Add(tl);
        Minutes = new ComboBox { MinWidth = 200, MinHeight = 44 };
        foreach (var mm in new[] { 30, 60, 90, 120, 150, 180, 240, c.ExamMinutes }.Distinct().OrderBy(x => x)) Minutes.Items.Add(new ComboBoxItem { Content = mm == c.ExamMinutes ? mm + " minutes (real exam)" : mm + " minutes", Tag = mm, MinHeight = 44 });
        foreach (ComboBoxItem it in Minutes.Items) if ((int)it.Tag == minutes) Minutes.SelectedItem = it;
        Minutes.SelectionChanged += (s, e) => { if (Minutes.SelectedItem is ComboBoxItem it) { pr.ExamMinutes = (int)it.Tag; A.SaveProgress(); } };
        timeRow.Children.Add(Minutes);
        root.Children.Add(timeRow);
        foreach (var ex in c.Exams)
        {
            var ex0 = ex;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(ex.Title, 17, Ui.TextC, true));
            int nQ = ex.Items.Count(i => i.IsQuestion);
            string what = nQ == 0 ? ex.Items.Count + " tasks on " + string.Join(" and ", ex.Items.Select(i => i.Machine).Distinct().OrderBy(x => x, StringComparer.Ordinal)) : (ex.Items.Count - nQ) + " hands-on tasks and " + nQ + " questions";
            if (ex.Cases.Count > 0) what += ", " + ex.Cases.Count + (ex.Cases.Count == 1 ? " case study" : " case studies");
            var objs = ex.Items.Select(i => i.Objective).Distinct().ToList();
            sp.Children.Add(CertUi.Muted(what + "  -  " + string.Join(", ", objs.Take(6)) + (objs.Count > 6 ? "..." : "")));
            var tries = pr.Exams.Where(a => a.Exam == ex.Id).OrderByDescending(a => a.When).ToList();
            if (tries.Count > 0) sp.Children.Add(CertUi.Muted("Last: " + tries[0].Scaled + " of " + c.ExamPoints + (tries[0].Passed ? " (pass)" : " (not yet)") + "   Best: " + tries.Max(a => a.Scaled) + "   Attempts: " + tries.Count));
            sp.Children.Add(CertUi.Accent("Start this exam", () => Start(ex0)));
            root.Children.Add(CertUi.Pane(sp, 16));
        }
        Body = Ui.Page(root, 1000);
    }

    public ExamRunPage Start(TaskSet ex)
    {
        int m = Minutes.SelectedItem is ComboBoxItem it ? (int)it.Tag : A.Course.ExamMinutes;
        int seed = A.Quiet ? 1 : Environment.TickCount & 0xFFFF;
        var page = new ExamRunPage(A, ex, seed, m, null);
        A.ExamRun = page;
        Mv.Push(page);
        return page;
    }
}

public sealed class ExamRunPage : CertPage
{
    public readonly TaskSet Exam;
    public ISim Sim;
    public Consoles Consoles { get; private set; }
    public Control Workspace { get; private set; }
    readonly TextBlock timer;
    public DateTime Ends;
    public int Seed { get; }
    public int Minutes { get; }
    bool finished;
    public bool IsFinished => finished;
    public readonly Dictionary<string, int> Answers = new();
    public readonly Dictionary<string, List<string>> More = new();
    public readonly Dictionary<string, Action<List<string>>> Setters = new();
    public readonly Dictionary<string, Control> ItemViews = new();
    public readonly List<RadioButton> ChoiceButtons = new();
    public readonly HashSet<string> Flags = new();
    public bool Sequential { get; private set; }
    public int CurrentItem;
    public bool ReviewShown;
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    Border left, right;
    Button swap;
    bool showMachines, narrow;

    public override string Title => Exam.Title;

    public ExamRunPage(CertApp app, TaskSet exam, int seed, int mins, ISim restored) : base(app)
    {
        Minutes = mins; Seed = seed;
        A.EnsureCourseOf(exam);
        Exam = exam.Instance(seed);
        var dock = new DockPanel();
        var bar = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        var end = CertUi.Accent("Finish and grade", () => { if (Sequential) ShowReview(); else AskFinish(); });
        DockPanel.SetDock(end, Dock.Right);
        bar.Children.Add(end);
        timer = Ui.Text("", 18, Ui.TextC, true); timer.VerticalAlignment = VerticalAlignment.Center; timer.Margin = new Thickness(0, 0, 12, 8); timer.TextWrapping = TextWrapping.NoWrap;
        DockPanel.SetDock(timer, Dock.Right);
        bar.Children.Add(timer);
        swap = CertUi.Btn("Machines", () => SetMachines(!showMachines));
        DockPanel.SetDock(swap, Dock.Left);
        bar.Children.Add(swap);
        bar.Children.Add(new Border());
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(layout);
        Body = dock;
        Ends = DateTime.Now.AddMinutes(mins);
        if (Exam.Items.Count > 0 && Exam.Items.All(i => i.IsQuestion))
        {
            end.Content = "Review and submit";
            swap.IsVisible = false;
            BuildSequential();
        }
        else BuildTasks(restored);
        UpdateTimer();
        ticker.Tick += (s, e) => CertUi.Safe(Tick);
        View.AttachedToVisualTree += (s, e) => { if (!A.Quiet) ticker.Start(); };
        View.DetachedFromVisualTree += (s, e) => ticker.Stop();
    }

    // ------------------------------------------------------------ tasks and questions beside the machines
    void BuildTasks(ISim restored)
    {
        var tasks = new StackPanel();
        var crs = A.Course;
        if (Exam.Intro.Length > 0) tasks.Children.Add(CertUi.P(Exam.Intro));
        tasks.Children.Add(CertUi.Muted(crs != null && crs.ExamHint.Length > 0 ? crs.ExamHint : "Root password: redhat, unless the exam says otherwise. Everything you do must still work after a reboot: the grader restarts the machines first. Tasks are worth the points shown."));
        int n = 0;
        string lastCase = "";
        foreach (var it in Exam.Items)
        {
            n++;
            var sp = new StackPanel();
            if (it.Case.Length > 0 && it.Case != lastCase)
            {
                var cs = Exam.CaseOf(it);
                if (cs != null) { var cv = CertUi.Pane(ExamItems.CaseView(cs), 12); cv.BorderBrush = Ui.Brush(Ui.Accent); tasks.Children.Add(cv); }
            }
            lastCase = it.Case;
            if (it.IsQuestion)
            {
                sp.Children.Add(CertUi.Muted("Question " + n + "  -  " + ExamItems.KindLabel(it) + "  -  " + it.Points + (it.Points == 1 ? " point" : " points") + (it.Case.Length > 0 ? "  -  case study" : "")));
                sp.Children.Add(ExamItems.Build(it, Answers, More, ChoiceButtons, Setters));
            }
            else
            {
                sp.Children.Add(CertUi.Muted("Task " + n + "  -  " + TaskRunner.MachineLabel(it.Machine) + "  -  " + it.Points + " points"));
                sp.Children.Add(CertUi.Rich(it.Text));
                sp.Children.Add(new CheckBox { Content = "Done (just for you; the grader checks the machines)", Foreground = Ui.Brush(Ui.Muted), FontSize = 13, MinHeight = 44, Margin = new Thickness(0, 6, 0, 0) });
            }
            var pane = CertUi.Pane(sp, 12);
            ItemViews[it.Id] = pane;
            tasks.Children.Add(pane);
        }
        if (restored != null) Sim = restored;
        else
        {
            Sim = RhelSim.Create(crs == null ? "rhel" : crs.Simulator, crs == null ? 2 : Math.Max(1, crs.Machines), Seed);
            TaskRunner.Prepare(Sim, Exam);
        }
        Sim.StartConsoles(true, Exam.NoLogin);
        left = new Border { Child = new ScrollViewer { Content = new Border { Child = tasks, Padding = new Thickness(10, 4, 10, 10) }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Workspace = CertWorkspaces.Create(Sim, true);
        Control machines;
        if (Workspace != null) machines = Workspace;
        else
        {
            string note = Sim is EpSim || Sim is WsSim ? "The practice portal and consoles come to Learning Lab later: until then, the machines' PowerShell consoles." : null;
            Consoles = new Consoles(Sim, true, note);
            machines = Consoles;
        }
        right = new Border { Child = machines, Margin = new Thickness(6, 0, 10, 10) };
        Grid.SetColumn(right, 1);
        layout.Children.Add(left); layout.Children.Add(right);
        layout.SizeChanged += (s, e) => Fit(e.NewSize.Width);
        Fit(1000);
    }

    void Fit(double w)
    {
        if (left == null || right == null) return;
        narrow = w < 900;
        if (narrow)
        {
            Grid.SetColumn(left, 0); Grid.SetColumnSpan(left, 2); Grid.SetColumn(right, 0); Grid.SetColumnSpan(right, 2);
            left.Width = double.NaN;
            left.IsVisible = !showMachines; right.IsVisible = showMachines;
        }
        else
        {
            Grid.SetColumnSpan(left, 1); Grid.SetColumn(right, 1); Grid.SetColumnSpan(right, 1);
            left.Width = Math.Min(460, w * 0.38);
            left.IsVisible = right.IsVisible = true;
        }
        swap.IsVisible = narrow;
        swap.Content = showMachines ? "Questions" : "Machines";
    }
    /// <summary>On a phone: the machines (true) or the tasks and questions (false).</summary>
    public void SetMachines(bool on) { showMachines = on; Fit(layout.Bounds.Width > 0 ? layout.Bounds.Width : 1000); }

    public IEnumerable<CertTerminal> Terms => Consoles != null ? Consoles.Terms : (Workspace != null && CertWorkspaces.Terminals != null ? CertWorkspaces.Terminals(Workspace) ?? Enumerable.Empty<CertTerminal>() : Enumerable.Empty<CertTerminal>());

    void UpdateTimer()
    {
        var leftT = Ends - DateTime.Now;
        if (leftT < TimeSpan.Zero) leftT = TimeSpan.Zero;
        timer.Text = ((int)leftT.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + leftT.Minutes.ToString("00") + ":" + leftT.Seconds.ToString("00");
        timer.Foreground = Ui.Brush(leftT.TotalMinutes < 10 ? Ui.Yellow : Ui.TextC);
    }

    public void Tick()
    {
        if (finished) return;
        if (Sim != null && Sim.Idle) Sim.Advance(1);
        Consoles?.Tick();
        if (Workspace != null) CertWorkspaces.Tick?.Invoke(Workspace);
        UpdateTimer();
        if (DateTime.Now >= Ends) Finish(true);
    }

    void AskFinish()
    {
        if (A.Quiet) { Finish(false); return; }
        Mv.Message("Finish the exam?", "The grader " + (Exam.RebootFirst ? "restarts the machines, then " : "") + "checks your work. You can't change anything afterwards.", ("Finish and grade", () => Finish(false)), ("Keep working", null));
    }

    public override bool OnBack()
    {
        if (finished) return false;
        if (CertWorkspaces.Back(Workspace)) return true;
        Mv.Message("Leave the exam?", "Your answers and the time that is left are kept: Cert Lab offers to carry on next time.", ("Keep it and leave", () => { A.KeepWork(); kept = finished = true; Mv.Back(); }), ("Stay", null));
        return true;
    }

    bool kept;
    public override void Closed()
    {
        base.Closed();
        ticker.Stop();
        if (A.ExamRun == this) A.ExamRun = null;
        if (!kept && A == CertApp.Current) A.DismissResume();   // kept only while the app was in the background: it is done with now
        Consoles?.DisposeTerms();
        try { Sim?.Dispose(); } catch (Exception) { }
    }

    public GradeReport Finish(bool timeUp)
    {
        if (finished) return null;
        finished = true;
        ticker.Stop();
        var rep = TaskRunner.Grade(Sim, Exam, Exam.RebootFirst, Answers, More);
        var c = A.Course;
        var att = new ExamAttempt { Exam = Exam.Id, Seed = Seed, When = DateTime.Now.Ticks, Score = rep.Score, Max = rep.Max, Scaled = c.Scale(rep.Score, rep.Max), Pass = c.PassMark };
        att.Passed = att.Scaled >= att.Pass;
        foreach (var kv in rep.PerObjective) att.PerObjective[kv.Key] = new List<int> { kv.Value[0], kv.Value[1] };
        att.Failed = rep.Items.Where(i => !i.Pass).Select(i => i.Item.Id).ToList();
        if (A.Me != null) { A.Progress.Exams.Add(att); A.SaveProgress(); }
        A.DismissResume();
        if (A.ExamRun == this) A.ExamRun = null;
        Mv.Replace(new ExamResultsPage(A, Exam, rep, att, timeUp, Answers, More));
        return rep;
    }

    /// <summary>Puts kept answers back in the controls (items the exam no longer has are left out).</summary>
    public void RestoreAnswers(Dictionary<string, int> answers, Dictionary<string, List<string>> more, List<string> flags, int current)
    {
        foreach (var kv in answers ?? new Dictionary<string, int>())
        {
            var it = Exam.Items.FirstOrDefault(i => i.Id == kv.Key);
            if (it == null || kv.Value < 0 || kv.Value >= it.Choices.Count) continue;
            int k = ChoiceButtons.FindIndex(b => b.GroupName == "q" + it.Id);
            if (k >= 0 && k + kv.Value < ChoiceButtons.Count) ChoiceButtons[k + kv.Value].IsChecked = true;
            Answers[it.Id] = kv.Value;
        }
        foreach (var kv in more ?? new Dictionary<string, List<string>>())
        {
            if (!Exam.Items.Any(i => i.Id == kv.Key)) continue;
            var l = kv.Value == null ? new List<string>() : new List<string>(kv.Value);
            if (Setters.TryGetValue(kv.Key, out var set)) { try { set(new List<string>(l)); } catch (Exception) { } }
            More[kv.Key] = l;
        }
        foreach (var f in flags ?? new List<string>()) if (Exam.Items.Any(i => i.Id == f)) Flags.Add(f);
        if (Sequential) Go(Math.Max(0, Math.Min(Exam.Items.Count - 1, current)));
    }

    /// <summary>The checks: puts an answer into an item's controls (as a person would).</summary>
    public void SetAnswer(TaskItem it, List<string> answer)
    {
        if (it.Kind == "choice") { int k = ChoiceButtons.FindIndex(b => b.GroupName == "q" + it.Id); int.TryParse(answer[0], out int pick); ChoiceButtons[k + pick].IsChecked = true; return; }
        Setters[it.Id](answer);
    }

    // ------------------------------------------------------------ one item at a time (questions only)
    WrapPanel nav;
    Border stage;
    TextBlock where;
    Button flagBtn;
    readonly List<Button> navButtons = new();

    void BuildSequential()
    {
        Sequential = true;
        var ordered = Exam.Items.Where(i => i.Pbq != null).Concat(Exam.Items.Where(i => i.Pbq == null)).ToList();
        Exam.Items.Clear(); Exam.Items.AddRange(ordered);
        var main = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
        nav = new WrapPanel();
        for (int i = 0; i < Exam.Items.Count; i++)
        {
            int k = i;
            var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = (i + 1).ToString(CultureInfo.InvariantCulture), MinWidth = 44, MinHeight = 44, Margin = new Thickness(0, 0, 4, 4), FontSize = 13, Padding = new Thickness(4) };
            b.Click += (o, e) => CertUi.Safe(() => Go(k));
            navButtons.Add(b); nav.Children.Add(b);
        }
        var navScroll = new ScrollViewer { Content = nav, MaxHeight = 104, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(navScroll, Dock.Top);
        main.Children.Add(navScroll);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(CertUi.Btn("Previous", () => Go(CurrentItem - 1)));
        flagBtn = CertUi.Btn("Flag for review", ToggleFlag);
        buttons.Children.Add(flagBtn);
        buttons.Children.Add(CertUi.Btn("Review all", ShowReview));
        buttons.Children.Add(CertUi.Accent("Next", () => { if (CurrentItem + 1 < Exam.Items.Count) Go(CurrentItem + 1); else ShowReview(); }));
        DockPanel.SetDock(buttons, Dock.Bottom);
        main.Children.Add(buttons);
        where = Ui.Text("", 13, Ui.Muted); where.Margin = new Thickness(0, 6, 0, 6);
        DockPanel.SetDock(where, Dock.Top);
        main.Children.Add(where);
        stage = new Border();
        main.Children.Add(new ScrollViewer { Content = stage, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetColumnSpan(main, 2);
        layout.Children.Add(main);
        int n = 0;
        foreach (var it in Exam.Items)
        {
            n++;
            var sp = new StackPanel();
            if (n == 1 && Exam.Intro.Length > 0) sp.Children.Add(CertUi.Muted(Exam.Intro));
            var cs = Exam.CaseOf(it);
            if (cs != null) { var cv = CertUi.Pane(ExamItems.CaseView(cs), 12); cv.BorderBrush = Ui.Brush(Ui.Accent); sp.Children.Add(cv); }
            sp.Children.Add(CertUi.Muted("Question " + n + " of " + Exam.Items.Count + "  -  " + ExamItems.KindLabel(it) + "  -  " + it.Points + (it.Points == 1 ? " point" : " points")));
            sp.Children.Add(ExamItems.Build(it, Answers, More, ChoiceButtons, Setters, PaintNav));
            ItemViews[it.Id] = CertUi.Pane(sp, 14);
        }
        Go(0);
    }

    public bool Answered(TaskItem it)
    {
        if (it.Kind == "choice") return Answers.ContainsKey(it.Id);
        return More.TryGetValue(it.Id, out var a) && a != null && a.Any(x => !string.IsNullOrEmpty(x) && !x.EndsWith('='));
    }

    public void Go(int i)
    {
        if (!Sequential || i < 0 || i >= Exam.Items.Count) return;
        CurrentItem = i; ReviewShown = false;
        var it = Exam.Items[i];
        stage.Child = ItemViews[it.Id];
        where.Text = "Item " + (i + 1) + " of " + Exam.Items.Count + "  -  " + it.Objective + (Flags.Contains(it.Id) ? "  -  flagged for review" : "");
        flagBtn.Content = Flags.Contains(it.Id) ? Mark.Flag + " Unflag" : "Flag for review";
        flagBtn.IsEnabled = true;
        PaintNav();
    }

    public void ToggleFlag()
    {
        if (ReviewShown) return;
        var id = Exam.Items[CurrentItem].Id;
        if (!Flags.Remove(id)) Flags.Add(id);
        Go(CurrentItem);
    }

    void PaintNav()
    {
        if (!Sequential) return;
        for (int i = 0; i < navButtons.Count; i++)
        {
            var it = Exam.Items[i];
            var b = navButtons[i];
            bool cur = i == CurrentItem && !ReviewShown, ans = Answered(it), fl = Flags.Contains(it.Id);
            b.Content = (i + 1).ToString(CultureInfo.InvariantCulture) + (fl ? " " + Mark.Flag : "");
            b.Background = Ui.Brush(cur ? Ui.Accent : ans ? Ui.Surface3 : Ui.Surface);
            b.Foreground = Ui.Brush(cur ? "#FFFFFF" : fl ? Ui.Yellow : ans ? Ui.TextC : Ui.Muted);
            b.BorderBrush = Ui.Brush(fl ? Ui.Yellow : Ui.BorderC);
        }
    }

    /// <summary>The review screen: every item with its state; Submit grades the exam.</summary>
    public void ShowReview()
    {
        if (!Sequential) { AskFinish(); return; }
        ReviewShown = true;
        flagBtn.IsEnabled = false;
        int unanswered = Exam.Items.Count(i => !Answered(i));
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Review your answers", 20, Ui.TextC, true));
        sp.Children.Add(CertUi.P(Exam.Items.Count - unanswered + " of " + Exam.Items.Count + " items answered" + (unanswered > 0 ? ", " + unanswered + " not answered yet" : "") + ", " + Flags.Count + " flagged for review. Tap an item to go back to it. An unanswered item scores nothing, so a guess is better than a blank."));
        var g = new WrapPanel();
        for (int i = 0; i < Exam.Items.Count; i++)
        {
            int k = i; var it = Exam.Items[i];
            bool ans = Answered(it), fl = Flags.Contains(it.Id);
            var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = Ui.Text((i + 1) + ". " + (ans ? "answered" : "not answered") + (fl ? "  " + Mark.Flag : ""), 13, !ans ? Ui.Red : fl ? Ui.Yellow : Ui.Muted), MinWidth = 150, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 4) };
            b.Click += (o, e) => CertUi.Safe(() => Go(k));
            g.Children.Add(b);
        }
        sp.Children.Add(g);
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (Flags.Count > 0) row.Children.Add(CertUi.Btn("Go to the first flagged item", () => Go(Exam.Items.FindIndex(x => Flags.Contains(x.Id)))));
        if (unanswered > 0) row.Children.Add(CertUi.Btn("Go to the first unanswered item", () => Go(Exam.Items.FindIndex(x => !Answered(x)))));
        row.Children.Add(CertUi.Btn("Back to the questions", () => Go(CurrentItem)));
        var confirm = new WrapPanel { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        var ct = Ui.Text("Submit now? Your answers can't be changed afterwards.", 14, Ui.TextC); ct.Margin = new Thickness(0, 0, 10, 8); ct.VerticalAlignment = VerticalAlignment.Center;
        confirm.Children.Add(ct);
        confirm.Children.Add(CertUi.Accent("Yes, submit the exam", () => Finish(false)));
        row.Children.Add(CertUi.Accent("Submit the exam", () => confirm.IsVisible = true));
        sp.Children.Add(row);
        sp.Children.Add(confirm);
        stage.Child = CertUi.Pane(sp, 16);
        where.Text = "Review";
        PaintNav();
    }
}

public sealed class ExamResultsPage : CertPage
{
    public override string Title => "Results";
    public readonly ExamAttempt Attempt;
    public readonly GradeReport Report;

    public ExamResultsPage(CertApp app, TaskSet exam, GradeReport rep, ExamAttempt att, bool timeUp, Dictionary<string, int> answers, Dictionary<string, List<string>> more) : base(app)
    {
        Attempt = att; Report = rep;
        var c = A.Course;
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("Results: " + exam.Title));
        if (timeUp) root.Children.Add(CertUi.Muted("Time was up, so the exam ended by itself."));
        if (c.Simulator == "winserver") root.Children.Add(CertUi.Muted("The grader restarted every server first (the domain controllers replicated, computers refreshed Group Policy), then checked the network as you left it. The score is shown from 1 to 1000 like Microsoft's reports; how Microsoft really scores is not public, so this is an approximation."));
        else if (c.Simulator == "endpoint") root.Children.Add(CertUi.Muted("Hands-on tasks were checked in the practice tenant and on the devices as you left them. The score is shown from 1 to 1000 like Microsoft's reports; how Microsoft really scores is not public, so this is an approximation."));
        else if (exam.Items.All(i => i.IsQuestion) && c.ScoreMin >= 100) root.Children.Add(CertUi.Muted("The score is shown from " + c.ScoreMin + " to " + c.ExamPoints + " like " + (c.Id == "secplus" ? "CompTIA" : "the real") + " score reports, with " + c.PassMark + " to pass. How the real exam scales scores is not public, so this is an approximation: " + c.ScoreMin + " plus your share of the points times " + (c.ExamPoints - c.ScoreMin) + ". Performance-based questions gave partial credit."));
        else root.Children.Add(CertUi.Muted("The grader rebooted the machines first, then checked them."));
        var head = new StackPanel();
        head.Children.Add(Ui.Text(att.Scaled + " of " + c.ExamPoints, 34, att.Passed ? Ui.Green : Ui.Yellow, true));
        head.Children.Add(Ui.Text(att.Passed ? "A pass: the passing mark is " + att.Pass + "." : "Not a pass yet: the passing mark is " + att.Pass + ".", 15, Ui.TextC));
        head.Children.Add(CertUi.Bar(att.Scaled / (double)c.ExamPoints, att.Passed ? Ui.Green : Ui.Yellow, 10));
        bool anyQ = rep.Items.Any(i => i.Item.IsQuestion);
        head.Children.Add(CertUi.Muted(anyQ ? rep.Items.Count(i => i.Pass) + " of " + rep.Items.Count + " tasks and questions right (" + rep.Score + " of " + rep.Max + " points)." : rep.Items.Count(i => i.Pass) + " of " + rep.Items.Count + " tasks done (" + rep.Score + " of " + rep.Max + " task points)."));
        root.Children.Add(CertUi.Pane(head, 18));
        root.Children.Add(CertUi.H2("By objective"));
        var obj = new StackPanel();
        foreach (var kv in rep.PerObjective.OrderBy(k => c.Objectives.IndexOf(k.Key) < 0 ? 99 : c.Objectives.IndexOf(k.Key)).ThenBy(k => k.Key, StringComparer.Ordinal))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            g.Children.Add(Ui.Text(kv.Key, 14, Ui.TextC));
            var pct = Ui.Text((kv.Value[1] == 0 ? 0 : kv.Value[0] * 100 / kv.Value[1]) + "%", 14, Ui.Muted); Grid.SetColumn(pct, 1); g.Children.Add(pct);
            obj.Children.Add(g);
            obj.Children.Add(CertUi.Bar(kv.Value[1] == 0 ? 0 : kv.Value[0] / (double)kv.Value[1]));
        }
        root.Children.Add(CertUi.Pane(obj, 14));
        root.Children.Add(CertUi.H2("Item by item"));
        int n = 0;
        foreach (var ir in rep.Items)
        {
            n++;
            var sp = new StackPanel();
            string pts = ir.Item.IsQuestion && ir.Points != ir.Item.Points && ir.Points > 0 ? ir.Points + " of " + ir.Item.Points + " points" : ir.Item.Points + " points";
            sp.Children.Add(Ui.Text((ir.Pass ? Mark.Yes : ir.Points > 0 ? Mark.Half : Mark.No) + (ir.Item.IsQuestion ? "Question " + n + " (" + ExamItems.KindLabel(ir.Item) + ", " : "Task " + n + " (" + TaskRunner.MachineLabel(ir.Item.Machine) + ", ") + pts + ")  -  " + ir.Item.Objective, 15, ir.Pass ? Ui.Green : ir.Points > 0 ? Ui.Yellow : Ui.Red, true));
            var cs = exam.CaseOf(ir.Item);
            if (ir.Item.IsQuestion)
            {
                if (cs != null) sp.Children.Add(CertUi.Muted("Case study: " + cs.Title));
                if (ir.Pbq == null) sp.Children.Add(CertUi.QuestionView(ir.Item.Text));
                if (ir.Item.Kind == "choice")
                {
                    bool answered = answers != null && answers.TryGetValue(ir.Item.Id, out int mine0) && mine0 >= 0 && mine0 < ir.Item.Choices.Count;
                    int mine = answered ? answers[ir.Item.Id] : -1;
                    sp.Children.Add(CertUi.Muted("Your answer: " + (answered ? ir.Item.Choices[mine].Replace("`", "") : "(none)")));
                    sp.Children.Add(CertUi.Muted("Right answer: " + (ir.Item.Answer >= 0 && ir.Item.Answer < ir.Item.Choices.Count ? ir.Item.Choices[ir.Item.Answer].Replace("`", "") : "")));
                }
                else
                {
                    List<string> mine = null; more?.TryGetValue(ir.Item.Id, out mine);
                    sp.Children.Add(ir.Pbq != null ? PbqView.Review(ir.Item, ir.Pbq) : ExamReview.Answer(ir.Item, mine));
                }
                if (ir.Item.Explain.Length > 0) sp.Children.Add(CertUi.P(ir.Item.Explain));
                root.Children.Add(CertUi.Pane(sp, 14));
                continue;
            }
            sp.Children.Add(CertUi.Rich(ir.Item.Text));
            sp.Children.Add(CertUi.Muted("What the grader checked:"));
            foreach (var ch in ir.Checks) sp.Children.Add(Ui.Text("   " + (ch.Pass ? Mark.Yes : Mark.No) + ch.Desc + (ch.Machine.Length > 0 ? " (" + ch.Machine + ")" : ""), 14, ch.Pass ? Ui.Muted : Ui.Red));
            var es = new StackPanel();
            foreach (var g in ir.Item.Solution.GroupBy(x => x.Machine)) { es.Children.Add(CertUi.Muted(TaskRunner.Where(g.Key))); es.Children.Add(CertUi.TermBlock(TaskRunner.SolutionBlock(g))); }
            if (ir.Item.Explain.Length > 0) es.Children.Add(CertUi.P(ir.Item.Explain));
            sp.Children.Add(new Expander { Header = "How to do it", IsExpanded = !ir.Pass, Content = es, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 });
            root.Children.Add(CertUi.Pane(sp, 14));
        }
        root.Children.Add(CertUi.Accent("Back to the exams", () => Mv.Back()));
        Body = Ui.Page(root, 1000);
    }
}

/// <summary>The review of a multiple-answer, order or Yes / No question: your answer next to the right one.</summary>
public static class ExamReview
{
    static string Plain(string s) => (s ?? "").Replace("`", "");
    public static Control Answer(TaskItem it, List<string> mine)
    {
        var sp = new StackPanel();
        bool showMine = mine != null;
        if (it.Pbq != null) { sp.Children.Add(PbqView.Review(it, PbqGrade.Grade(it.Pbq, it.Points, mine ?? PbqGrade.ModelAnswer(it.Pbq)))); return sp; }
        mine ??= new List<string>();
        switch (it.Kind)
        {
            case "multi":
                {
                    var picked = mine.Select(x => int.TryParse(x, out int i) && i >= 0 && i < it.Choices.Count ? Plain(it.Choices[i]) : null).Where(x => x != null).ToList();
                    if (showMine) sp.Children.Add(CertUi.Muted("Your answers: " + (picked.Count == 0 ? "(none)" : string.Join("; ", picked))));
                    sp.Children.Add(CertUi.Muted("Right answers: " + string.Join("; ", it.Answers.Select(a => Plain(it.Choices[a])))));
                    break;
                }
            case "order":
                if (showMine) sp.Children.Add(CertUi.Muted("Your order:" + (mine.Count == 0 ? " (none)" : "")));
                for (int i = 0; i < mine.Count; i++) sp.Children.Add(CertUi.Muted("   " + (i + 1) + ". " + Plain(mine[i])));
                sp.Children.Add(CertUi.Muted("The right order:"));
                for (int i = 0; i < it.OrderAnswer.Count; i++) sp.Children.Add(CertUi.Muted("   " + (i + 1) + ". " + Plain(it.OrderAnswer[i])));
                var extra = it.Choices.Where(c => !it.OrderAnswer.Contains(c)).ToList();
                if (extra.Count > 0) sp.Children.Add(CertUi.Muted("Not needed: " + string.Join("; ", extra.Select(Plain))));
                break;
            case "yesno":
                for (int i = 0; i < it.Choices.Count; i++)
                {
                    string you = i < mine.Count && mine[i].Length > 0 ? (mine[i] == "Y" ? "Yes" : "No") : "(none)";
                    string right = it.Truth[i] ? "Yes" : "No";
                    sp.Children.Add(Ui.Text((!showMine ? "" : you == right ? Mark.Yes : Mark.No) + Plain(it.Choices[i]) + (showMine ? "  -  you: " + you + ", right: " + right : "  -  " + right), 14, !showMine || you == right ? Ui.Muted : Ui.Red));
                }
                break;
        }
        return sp;
    }
}

public static class ExamItems
{
    public static string KindLabel(TaskItem it)
    {
        if (PbqSpec.IsPbq(it.Kind)) return PbqView.KindLabel(it.Kind);
        return it.Kind switch { "multi" => "Multiple answers", "order" => "Put in order", "yesno" => "Yes / No", "choice" => "One answer", _ => "Hands-on task" };
    }

    /// <summary>A case study's scenario: a button per section (Overview, Requirements...) and the section shown.</summary>
    public static Control CaseView(CaseStudy cs)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Case study: " + cs.Title, 17, Ui.AccentText, true));
        sp.Children.Add(CertUi.Muted("Read the scenario, then answer the questions after it. Each question is about this case."));
        var tabs = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var body = new ContentControl();
        var buttons = new List<Button>();
        void ShowSec(int k)
        {
            var b = new StackPanel();
            foreach (var para in cs.Sections[k].Value.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) { var r = CertUi.Rich(para.Trim()); r.Margin = new Thickness(0, 0, 0, 8); b.Children.Add(r); }
            body.Content = new ScrollViewer { Content = b, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            for (int i = 0; i < buttons.Count; i++) { buttons[i].Background = Ui.Brush(i == k ? Ui.Accent : Ui.Surface3); buttons[i].Foreground = Ui.Brush(i == k ? "#FFFFFF" : Ui.TextC); }
        }
        for (int i = 0; i < cs.Sections.Count; i++)
        {
            int k = i;
            var b = CertUi.Btn(cs.Sections[i].Key.Length > 0 ? cs.Sections[i].Key : "Overview", () => ShowSec(k));
            buttons.Add(b); tabs.Children.Add(b);
        }
        sp.Children.Add(tabs);
        sp.Children.Add(body);
        if (cs.Sections.Count > 0) ShowSec(0);
        return sp;
    }

    /// <summary>The controls for one question; they write the answer into choice (one answer) or more (the rest).</summary>
    public static Control Build(TaskItem it, Dictionary<string, int> choice, Dictionary<string, List<string>> more, List<RadioButton> radios, Dictionary<string, Action<List<string>>> setters, Action changed = null)
    {
        var sp = new StackPanel();
        string id = it.Id;
        if (it.Pbq != null)
        {
            more.TryGetValue(id, out var have);
            var list = have != null ? new List<string>(have) : new List<string>();
            var pv = new PbqView(it, list, () => { more[id] = new List<string>(list); changed?.Invoke(); });
            setters[id] = l => pv.Load(l);
            sp.Children.Add(pv);
            return sp;
        }
        sp.Children.Add(CertUi.QuestionView(it.Text));
        switch (it.Kind)
        {
            case "choice":
                for (int ci = 0; ci < it.Choices.Count; ci++)
                {
                    int pick = ci;
                    var rb = new RadioButton { Content = CertUi.PlainRich(it.Choices[ci]), VerticalContentAlignment = VerticalAlignment.Center, GroupName = "q" + id, MinHeight = 44, Margin = new Thickness(0, 2, 0, 0) };
                    rb.IsCheckedChanged += (o, e) => { if (rb.IsChecked == true) { choice[id] = pick; changed?.Invoke(); } };
                    sp.Children.Add(rb);
                    radios.Add(rb);
                }
                break;
            case "multi":
                {
                    sp.Children.Add(CertUi.Muted("Pick " + it.Answers.Count + "."));
                    var boxes = new List<CheckBox>();
                    void Update() { more[id] = boxes.Select((b, i) => b.IsChecked == true ? i.ToString(CultureInfo.InvariantCulture) : null).Where(x => x != null).ToList(); changed?.Invoke(); }
                    for (int ci = 0; ci < it.Choices.Count; ci++)
                    {
                        var cb = new CheckBox { Content = CertUi.PlainRich(it.Choices[ci]), MinHeight = 44, Margin = new Thickness(0, 2, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
                        cb.IsCheckedChanged += (o, e) => Update();
                        boxes.Add(cb); sp.Children.Add(cb);
                    }
                    setters[id] = l => { for (int i = 0; i < boxes.Count; i++) boxes[i].IsChecked = l.Contains(i.ToString(CultureInfo.InvariantCulture)); };
                    break;
                }
            case "order": sp.Children.Add(OrderView(it, more, setters, changed)); break;
            case "yesno":
                {
                    var picks = Enumerable.Repeat("", it.Choices.Count).ToList();
                    var rows = new List<RadioButton[]>();
                    for (int si = 0; si < it.Choices.Count; si++)
                    {
                        int k = si;
                        var box = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
                        box.Children.Add(CertUi.Rich(it.Choices[si]));
                        var y = new RadioButton { Content = "Yes", GroupName = "yn" + id + "-" + si, MinHeight = 44, MinWidth = 90 };
                        var n = new RadioButton { Content = "No", GroupName = "yn" + id + "-" + si, MinHeight = 44, MinWidth = 90 };
                        y.IsCheckedChanged += (o, e) => { if (y.IsChecked == true) { picks[k] = "Y"; more[id] = new List<string>(picks); changed?.Invoke(); } };
                        n.IsCheckedChanged += (o, e) => { if (n.IsChecked == true) { picks[k] = "N"; more[id] = new List<string>(picks); changed?.Invoke(); } };
                        box.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { y, n } });
                        rows.Add(new[] { y, n });
                        sp.Children.Add(box);
                    }
                    setters[id] = l => { for (int i = 0; i < rows.Count && i < l.Count; i++) { if (l[i] == "Y") rows[i][0].IsChecked = true; else if (l[i] == "N") rows[i][1].IsChecked = true; } };
                    break;
                }
        }
        return sp;
    }

    /// <summary>Put in order: tap an action to add it to the answer area; in the answer area, Up / Down / Remove.</summary>
    static Control OrderView(TaskItem it, Dictionary<string, List<string>> more, Dictionary<string, Action<List<string>>> setters, Action changed)
    {
        string id = it.Id;
        var answer = new List<string>();
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var src = new WrapPanel();
        var dst = new StackPanel();
        void Save() { more[id] = new List<string>(answer); changed?.Invoke(); }
        void Render()
        {
            src.Children.Clear(); dst.Children.Clear();
            foreach (var c in it.Choices.Where(c => !answer.Contains(c)))
            {
                var c0 = c;
                var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = new TextBlock { Text = "+ " + c.Replace("`", ""), TextWrapping = TextWrapping.Wrap }, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 6), Background = Ui.Brush(Ui.Surface3) };
                b.Click += (o, e) => { answer.Add(c0); Save(); Render(); };
                src.Children.Add(b);
            }
            if (src.Children.Count == 0) src.Children.Add(CertUi.Muted("Every action is in the answer area."));
            for (int i = 0; i < answer.Count; i++)
            {
                int k = i;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                Button T(string t, string tip, Action a) { var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = t, MinWidth = 44, MinHeight = 44, Margin = new Thickness(4, 0, 0, 0) }; ToolTip.SetTip(b, tip); b.Click += (o, e) => { a(); Save(); Render(); }; tools.Children.Add(b); return b; }
                T("↑", "Move up", () => { if (k > 0) (answer[k - 1], answer[k]) = (answer[k], answer[k - 1]); });
                T("↓", "Move down", () => { if (k + 1 < answer.Count) (answer[k + 1], answer[k]) = (answer[k], answer[k + 1]); });
                T("✕", "Take it out", () => answer.RemoveAt(k));
                DockPanel.SetDock(tools, Dock.Right);
                row.Children.Add(tools);
                var t = Ui.Text((i + 1) + ". " + answer[i].Replace("`", ""), 14, Ui.TextC); t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                dst.Children.Add(new Border { Child = row, Background = Ui.Brush(Ui.Surface2), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 0, 4) });
            }
            if (answer.Count == 0) dst.Children.Add(CertUi.Muted("Tap the actions above in the order you would do them."));
        }
        sp.Children.Add(CertUi.Muted("Actions (some may not be needed):"));
        sp.Children.Add(src);
        sp.Children.Add(CertUi.Muted("Answer area, in order:"));
        sp.Children.Add(new Border { Child = dst, BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), MinHeight = 60 });
        setters[id] = l => { answer.Clear(); foreach (var s in l) if (it.Choices.Contains(s) && !answer.Contains(s)) answer.Add(s); Save(); Render(); };
        Render();
        return sp;
    }
}
