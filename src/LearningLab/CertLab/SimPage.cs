// Cert Lab (Learning Lab) - the practice machines page (App Hub's UiSim.cs): the consoles of the machines (Consoles: side
// by side when two fit, else one at a time with a tab per machine), the machines' controls (reset, save, open saved,
// skip time) and, for a lesson's try-it or a lab, the tasks with Check my work, hints and the model solution (beside the
// consoles on a wide screen; on a phone the "Your tasks" button shows them over the consoles and Back closes them).
// The MD-102 portal and the AZ-800 consoles are controls of their own (CertLab\Portal, written separately): a page asks
// CertWorkspaces.Make for one; until that is registered, those courses show their machines' PowerShell consoles.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CertLab;
using LearningLab.Saves;
using LearningLab.Views;

namespace LearningLab.CertLab;

/// <summary>The hook for the MD-102 practice portal (EpSim) and the AZ-800 practice consoles (WsSim): the screens in
/// CertLab\Portal register here (at start), and SimPage / ExamRunPage host what Make returns.</summary>
public static class CertWorkspaces
{
    /// <summary>(the simulator, inside an exam) -> its workspace control, or null for "none" (terminals are shown).</summary>
    public static Func<ISim, bool, Control> Make;
    /// <summary>Called every second while the page is open (and after Skip time), with the control Make returned.</summary>
    public static Action<Control> Tick;
    /// <summary>The terminals inside a workspace, so the page polls them and the checks find them (may be null).</summary>
    public static Func<Control, IEnumerable<CertTerminal>> Terminals;

    // The portal and consoles (CertLab\Portal) are registered here, and their PowerShell windows are Cert Lab's terminal.
    static CertWorkspaces()
    {
        Make = (sim, exam) => global::LearningLab.CertLab.Portal.PortalWorkspaces.Make(sim, exam);
        Tick = w => global::LearningLab.CertLab.Portal.PortalWorkspaces.Tick(w);
        Terminals = w => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(w).OfType<CertTerminal>();
        global::LearningLab.CertLab.Portal.PortalWorkspaces.TerminalMaker ??= t => new CertTerminal(t);
        global::LearningLab.CertLab.Portal.PortalWorkspaces.TerminalPoll ??= c => (c as CertTerminal)?.Poll();
    }

    /// <summary>Back inside a workspace (closes its menu, goes back a page); false when there is nothing to undo.</summary>
    public static bool Back(Control w) => w != null && global::LearningLab.CertLab.Portal.PortalWorkspaces.OnBack(w);

    internal static Control Create(ISim sim, bool exam)
    {
        if (!(sim is EpSim) && !(sim is WsSim)) return null;
        try { return Make?.Invoke(sim, exam); } catch (Exception) { return null; }
    }
}

/// <summary>The consoles of the machines of a simulator, each in a CertTerminal with a head (name, state, Reboot /
/// Power or Restart).</summary>
public sealed class Consoles : DockPanel
{
    readonly ISim sim;
    readonly bool exam;
    public readonly List<CertTerminal> Terms = new();
    readonly List<string> names;
    readonly List<TextBlock> heads = new();
    readonly List<Control> panes = new();
    readonly WrapPanel tabs = new() { Margin = new Thickness(0, 0, 0, 6) };
    readonly Grid area = new();
    int shown;
    bool sideBySide;

    public Consoles(ISim s, bool inExam, string note = null)
    {
        sim = s; exam = inExam;
        names = sim.MachineNames;
        DockPanel.SetDock(tabs, Dock.Top);
        Children.Add(tabs);
        if (!string.IsNullOrEmpty(note)) { var n = CertUi.Muted(note); DockPanel.SetDock(n, Dock.Top); Children.Insert(0, n); }
        Children.Add(area);
        foreach (var name in names)
        {
            var t = sim.Term(name);
            if (t == null) continue;
            if (!t.Waiting && !t.Finished) t.Start();
            var tv = new CertTerminal(t) { Title = name };
            var head = new DockPanel { Background = Ui.Brush(Ui.Surface2) };
            string n0 = name;
            Button Small(string text, string tip, Action a)
            {
                var b = new Button { Content = text, FontSize = 13, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 4), Margin = new Thickness(4, 0, 0, 0), Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Ui.TextC) };
                ToolTip.SetTip(b, tip);
                b.Click += (o, e) => CertUi.Safe(() => { a(); tv.Poll(); tv.FocusInput(); });
                DockPanel.SetDock(b, Dock.Right);
                head.Children.Add(b);
                return b;
            }
            if (exam) Small("Restart", "Restart this machine (like the reset button). You see its boot menu.", () => sim.Reboot(n0));
            else { Small("Reboot", "Restart this machine: you see its boot menu", () => sim.Reboot(n0)); Small("Power", "Switch off (like pulling the plug) or on", () => sim.Power(n0)); }
            var ht = Ui.Text(name, 13.5, Ui.TextC); ht.Margin = new Thickness(10, 4); ht.VerticalAlignment = VerticalAlignment.Center; ht.TextTrimming = TextTrimming.CharacterEllipsis; ht.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(ht);
            heads.Add(ht);
            var dock = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            dock.Children.Add(new Border { Child = tv, BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1) });
            panes.Add(dock);
            Terms.Add(tv);
        }
        SizeChanged += (s, e) => Layout(e.NewSize.Width);
        Layout(800);
        UpdateHeads();
    }

    void Layout(double w)
    {
        bool side = Terms.Count == 2 && w >= 1000;
        if (side == sideBySide && area.Children.Count > 0) return;
        sideBySide = side;
        area.Children.Clear(); area.ColumnDefinitions.Clear();
        tabs.Children.Clear();
        if (side)
        {
            area.ColumnDefinitions = new ColumnDefinitions("*,8,*");
            for (int i = 0; i < 2; i++) { Grid.SetColumn(panes[i], i * 2); area.Children.Add(panes[i]); }
            tabs.IsVisible = false;
            return;
        }
        tabs.IsVisible = Terms.Count > 1;
        for (int i = 0; i < Terms.Count; i++)
        {
            int k = i;
            var b = CertUi.Btn(Terms[i].Title, () => Show(k), i == shown);
            b.Margin = new Thickness(0, 0, 6, 0);
            tabs.Children.Add(b);
        }
        foreach (var p in panes) Grid.SetColumn(p, 0);
        if (Terms.Count > 0) area.Children.Add(panes[Math.Min(shown, panes.Count - 1)]);
    }

    /// <summary>Shows one machine's console (one at a time on a phone).</summary>
    public void Show(int i)
    {
        if (i < 0 || i >= Terms.Count) return;
        shown = i;
        if (!sideBySide) { area.Children.Clear(); area.Children.Add(panes[i]); }
        for (int k = 0; k < tabs.Children.Count; k++) if (tabs.Children[k] is Button b) { b.Background = Ui.Brush(k == i ? Ui.Accent : Ui.Surface3); b.Foreground = Ui.Brush(k == i ? "#FFFFFF" : Ui.TextC); }
        Terms[i].FocusInput();
    }
    public CertTerminal Term(string machine) => Terms.FirstOrDefault(t => t.Title == machine);
    public bool SideBySide => sideBySide;

    public void Tick()
    {
        foreach (var t in Terms) t.Poll();
        UpdateHeads();
    }
    void UpdateHeads()
    {
        for (int i = 0; i < heads.Count && i < Terms.Count; i++)
        {
            string st = sim.Status(Terms[i].Title);
            heads[i].Text = string.IsNullOrEmpty(st) ? Terms[i].Title : st;
        }
    }
    public void DisposeTerms() { foreach (var t in Terms) t.S.Dispose(); }
}

public sealed class SimPage : CertPage
{
    public ISim Sim;
    public TaskSet Tasks;
    public string Mode;           // practice, try, lab
    public Lesson FromLesson;
    /// <summary>The seed of the lab or try-it's details (names, numbers), kept with the work.</summary>
    public int InstanceSeed = 1;
    public Consoles Consoles { get; private set; }
    /// <summary>The MD-102 / AZ-800 workspace (from CertWorkspaces.Make), or null.</summary>
    public Control Workspace { get; private set; }
    public bool Open { get; private set; } = true;
    int machineCount = 2;
    public int MachineCount => machineCount;
    readonly Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    readonly Border center = new() { Margin = new Thickness(10, 0, 10, 10) };
    readonly TextBlock clock;
    Border side;
    StackPanel results;
    Button tasksBtn;
    bool panelOpen, narrow;
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly bool ep, ws;

    public override string Title => Mode == "practice" ? (ep ? "Practice tenant" : ws ? "Practice network" : "Practice machines") : Mode == "lab" ? "Lab: " + Tasks.Title : "Try it: " + Tasks.Title;

    public SimPage(CertApp app, TaskSet tasks, string mode) : base(app)
    {
        Tasks = tasks; Mode = mode ?? "practice";
        ep = A.Course != null && A.Course.Simulator == "endpoint";
        ws = A.Course != null && A.Course.Simulator == "winserver";
        A.Practice = this;
        var dock = new DockPanel();
        var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 2) };
        if (tasks != null) { tasksBtn = CertUi.Accent(Mode == "lab" ? "Your tasks" : "Your task", () => SetPanel(!panelOpen)); bar.Children.Add(tasksBtn); }
        if (Mode == "practice" && !ep && !ws) { bar.Children.Add(CertUi.Btn("One machine", () => Rebuild(1))); bar.Children.Add(CertUi.Btn("Two machines", () => Rebuild(2))); }
        bar.Children.Add(CertUi.Btn(ep ? "Reset the tenant" : "Reset machines", () => { Rebuild(machineCount); A.Status(ep ? "The tenant and its devices are as new again." : "The machines are as new again."); }));
        if (Mode == "practice" && (A.Course == null || A.Course.Simulator == "rhel")) { bar.Children.Add(CertUi.Btn("Save", SaveMachines)); bar.Children.Add(CertUi.Btn("Open saved", LoadMachines)); }
        var skip = new StackPanel { Spacing = 6 };
        void SkipBtn(string t, long secs) { var b = CertUi.Btn(t, () => Skip(secs)); b.HorizontalAlignment = HorizontalAlignment.Stretch; skip.Children.Add(b); }
        SkipBtn("1 minute", 60); SkipBtn("1 hour", 3600);
        if (ep) SkipBtn("8 hours", 28800);
        if (ws) SkipBtn("15 minutes", 900);
        SkipBtn("1 day", 86400);
        var skipBtn = CertUi.Btn("Skip time ▾", null);
        skipBtn.Flyout = new Flyout { Content = skip };
        bar.Children.Add(skipBtn);
        clock = Ui.Text("", 13, Ui.Muted); clock.VerticalAlignment = VerticalAlignment.Center; clock.Margin = new Thickness(4, 0, 0, 8);
        bar.Children.Add(clock);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        layout.Children.Add(center);
        dock.Children.Add(layout);
        if (tasks != null) BuildSide();
        layout.SizeChanged += (s, e) => Fit(e.NewSize.Width);
        Body = dock;
        Rebuild(tasks != null ? Math.Max(1, tasks.Machines) : 2);
        ticker.Tick += (s, e) => CertUi.Safe(Tick);
        View.AttachedToVisualTree += (s, e) => { if (!A.Quiet) ticker.Start(); };
        View.DetachedFromVisualTree += (s, e) => ticker.Stop();
        Fit(1000);
    }

    void Fit(double w)
    {
        if (side == null) return;
        narrow = w < 820;
        if (narrow)
        {
            Grid.SetColumn(side, 0); Grid.SetColumnSpan(side, 2);
            side.Width = double.NaN;
            side.IsVisible = panelOpen;
            center.IsVisible = !panelOpen;
        }
        else
        {
            Grid.SetColumn(side, 1); Grid.SetColumnSpan(side, 1);
            side.Width = Math.Min(380, w * 0.36);
            side.IsVisible = true;
            center.IsVisible = true;
        }
        if (tasksBtn != null) tasksBtn.IsVisible = narrow;
    }

    /// <summary>On a phone: the tasks over the consoles (true) or the consoles (false).</summary>
    public void SetPanel(bool open)
    {
        panelOpen = open;
        if (tasksBtn != null) tasksBtn.Content = open ? "Back to the machines" : Mode == "lab" ? "Your tasks" : "Your task";
        Fit(layout.Bounds.Width > 0 ? layout.Bounds.Width : 1000);
    }
    public bool PanelOpen => panelOpen && narrow;
    public override bool OnBack() { if (PanelOpen) { SetPanel(false); return true; } return CertWorkspaces.Back(Workspace); }

    public override void Closed()
    {
        base.Closed();
        Open = false;
        ticker.Stop();
        if (A.Practice == this) A.Practice = null;
        if (A == CertApp.Current) A.DismissResume();
        Consoles?.DisposeTerms();
        try { Sim?.Dispose(); } catch (Exception) { }
    }

    public void Rebuild(int machines)
    {
        machineCount = machines;
        Consoles?.DisposeTerms();
        Sim?.Dispose();
        int seed = A.Quiet ? 1 : (int)(DateTime.Now.Ticks % 100000) + 1;
        Sim = RhelSim.Create(A.Course == null ? "rhel" : A.Course.Simulator, machines, seed);
        if (Tasks != null) TaskRunner.Prepare(Sim, Tasks);
        Sim.StartConsoles(true, Tasks?.NoLogin);
        Attach();
    }

    /// <summary>Puts these machines in place of the ones on screen (Open saved, or machines kept from last time).</summary>
    public void Adopt(ISim loaded)
    {
        Consoles?.DisposeTerms();
        Sim?.Dispose();
        Sim = loaded;
        machineCount = Sim.MachineNames.Count;
        Sim.StartConsoles(true, Tasks?.NoLogin);
        Attach();
    }

    void Attach()
    {
        Workspace = CertWorkspaces.Create(Sim, false);
        if (Workspace != null) { Consoles = null; center.Child = Workspace; UpdateClock(); return; }
        string note = Sim is EpSim ? "The practice portal comes to Learning Lab later: until then, here are the devices' PowerShell consoles." : Sim is WsSim ? "The practice consoles (Users and Computers, Group Policy, DNS...) come to Learning Lab later: until then, here are the servers' PowerShell consoles." : null;
        Consoles = new Consoles(Sim, false, note);
        center.Child = Consoles;
        UpdateClock();
    }

    public IEnumerable<CertTerminal> Terms => Consoles != null ? Consoles.Terms : (Workspace != null && CertWorkspaces.Terminals != null ? CertWorkspaces.Terminals(Workspace) ?? Enumerable.Empty<CertTerminal>() : Enumerable.Empty<CertTerminal>());
    public CertTerminal Term(string machine) => Terms.FirstOrDefault(t => t.Title == machine) ?? Terms.FirstOrDefault();

    void UpdateClock() { clock.Text = "Time on the machines: " + Sim.Clock; }

    public void Tick()
    {
        if (Sim == null) return;
        if (Sim.Idle) Sim.Advance(1);
        PollAll();
    }
    void PollAll()
    {
        Consoles?.Tick();
        foreach (var t in Terms) if (Consoles == null) t.Poll();
        if (Workspace != null) CertWorkspaces.Tick?.Invoke(Workspace);
        UpdateClock();
    }

    public void Skip(long secs)
    {
        if (!Sim.Idle) { A.Status("Wait until the command finishes."); return; }
        Sim.Advance(secs);
        PollAll();
        string span = secs == 900 ? "15 minutes" : secs == 60 ? "1 minute" : secs == 3600 ? "1 hour" : secs == 28800 ? "8 hours" : "1 day";
        if (Sim is WsSim) A.Status("The clock moved on " + span + ": replication, Group Policy refresh, leases and sync cycles ran on the way.");
        else if (Sim is EpSim) A.Status("The clock moved on " + span + ": devices checked in when they were due, updates and timers ran on the way.");
        else A.Status("The machines' clocks moved on " + span + ": cron, at and timers ran on the way.");
    }

    string SaveFile => CertLabData.MachinesFile(A.DataDir, A.Me?.Name);

    public void SaveMachines()
    {
        if (!Sim.Idle) { A.Status("Wait until the command finishes."); return; }
        SafeFile.WriteText(SaveFile, Sim.Save());
        A.Status("Saved the machines. Open saved brings them back as they are now.");
    }
    public void LoadMachines()
    {
        RhelSim loaded; SaveLoad info;
        try { loaded = CertLabData.OpenMachines(SaveFile, out info); }
        catch (FileNotFoundException) { A.Status("Nothing saved yet: press Save first."); return; }
        catch (SaveUnreadableException ex)
        {
            A.Status("The saved machines couldn't be opened (they may be from a different version). They are kept as " + Path.GetFileName(ex.Quarantined) + "; these machines stay as they are.");
            return;
        }
        Adopt(loaded);
        A.Status(info.Message != "" ? info.Message : "Opened the saved machines. The terminals start at a fresh root login.");
    }

    // ------------------------------------------------------------ the task panel
    void BuildSide()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(Mode == "lab" ? "Your tasks" : "Your task", 17, Ui.AccentText, true));
        if (Tasks.Intro.Length > 0) sp.Children.Add(CertUi.P(Tasks.Intro));
        int n = 0;
        foreach (var it in Tasks.Items)
        {
            n++;
            var box = new StackPanel();
            string head = (Tasks.Items.Count > 1 ? n + ". " : "") + (it.Machine != null && Tasks.Machines > 1 ? "(" + it.Machine + ") " : "");
            if (head.Length > 0) box.Children.Add(CertUi.Muted(head));
            box.Children.Add(CertUi.Rich(it.Text));
            var it0 = it;
            var hints = new StackPanel();
            box.Children.Add(hints);
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (it.Hints.Count > 0)
            {
                int shown = 0;
                Button hb = null;
                hb = CertUi.Btn("Show a hint", () => { if (shown < it0.Hints.Count) { hints.Children.Add(CertUi.Muted("Hint: " + it0.Hints[shown])); shown++; } if (shown >= it0.Hints.Count) hb.IsEnabled = false; });
                row.Children.Add(hb);
            }
            var solBox = new StackPanel();
            Button sol = null;
            sol = CertUi.Btn("Show the solution", () =>
            {
                solBox.Children.Clear();
                foreach (var g in it0.Solution.GroupBy(x => x.Machine)) { if (Tasks.Machines > 1 || g.Key == EpSim.Portal) solBox.Children.Add(CertUi.Muted(TaskRunner.Where(g.Key))); solBox.Children.Add(CertUi.TermBlock(TaskRunner.SolutionBlock(g))); }
                if (it0.Explain.Length > 0) solBox.Children.Add(CertUi.P(it0.Explain));
                sol.IsEnabled = false;
            });
            row.Children.Add(sol);
            box.Children.Add(row);
            box.Children.Add(solBox);
            sp.Children.Add(CertUi.Pane(box, 12));
        }
        sp.Children.Add(CertUi.Accent("Check my work", () => CheckWork()));
        results = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(results);
        side = new Border { Child = new ScrollViewer { Content = new Border { Child = sp, Padding = new Thickness(12) }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1, 0, 0, 0) };
        Grid.SetColumn(side, 1);
        layout.Children.Add(side);
    }

    /// <summary>Check my work: grades the tasks on the machines as they are now and shows each check.</summary>
    public GradeReport CheckWork()
    {
        if (!Sim.Idle) { A.Status("Wait until the command finishes."); return null; }
        var rep = TaskRunner.Grade(Sim, Tasks, false, null);
        results.Children.Clear();
        bool all = rep.Items.All(i => i.Pass);
        var head = Ui.Text(all ? "Well done: everything checks out." : "Not yet: " + rep.Items.Count(i => !i.Pass) + " of " + rep.Items.Count + " still to do.", 15, all ? Ui.Green : Ui.Yellow, true);
        head.Margin = new Thickness(0, 0, 0, 6);
        results.Children.Add(head);
        foreach (var ir in rep.Items)
            foreach (var c in ir.Checks)
                results.Children.Add(Ui.Text((c.Pass ? "✔  " : "✘  ") + c.Desc, 14, c.Pass ? Ui.Green : Ui.Red));
        if (A.Me != null)
        {
            var pr = A.Progress;
            if (Mode == "lab") { int pct = rep.Percent; if (!pr.Labs.TryGetValue(Tasks.Id, out int old) || pct > old) pr.Labs[Tasks.Id] = pct; }
            if (Mode == "try" && all && FromLesson != null && !pr.TriesDone.Contains(FromLesson.Id)) pr.TriesDone.Add(FromLesson.Id);
            A.SaveProgress();
        }
        if (narrow && !panelOpen) SetPanel(true);
        return rep;
    }
}
