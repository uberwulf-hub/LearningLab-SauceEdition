// Cyber Range - the screens, drawn off screen with Avalonia's headless platform (group "range-screens"): the home entry,
// home (new and with progress), the tour, badges and ranks, the glossary page and its search, every category and level
// page, a briefing with Explain it simply and a glossary word tapped, then driven by button presses: a Red Team
// scenario to every flag through the terminal and the answer box, a SOC Analyst scenario's questions, a Blue Team
// scenario's questions and fixes, a CTF puzzle and a Linux Ladder step through the terminal, hints revealed one at a
// time with their cost, Learning mode, the Defender tab, giving up, the report (score, rank, badges, the walkthrough
// only after the run), progress saved and read back, a run kept when the app closes and brought back, Back asking
// before leaving a run, and every page phone-sized (buttons at least 44 px, nothing wider than the screen). Also App
// Hub's range-ui and range-look checks that apply here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CertLab;
using LearningLab.CertLab;
using LearningLab.CyberRange;
using LearningLab.Views;

namespace LearningLab.Tests;

static class RangeScreens
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("range screens: " + what, ok, detail);
    static Window win;
    static MainView mv;
    static RangeApp A;
    static bool phone;
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, (phone ? "range-phone-" : "range-") + name);
    static string Tail(string s, int n = 400) => s == null ? "" : s.Substring(Math.Max(0, s.Length - n));

    public static void Run()
    {
        string data = Data.For("CertLab");
        foreach (var f in Directory.GetFiles(data, "*", SearchOption.AllDirectories)) File.Delete(f);
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        mv = MainView.Current;
        A = RangeApp.Fresh(true);
        try
        {
            Section("look", Look);
            Section("home", Home);
            Section("tour, badges, glossary", Pages);
            Section("categories and levels", Categories);
            Section("briefing and glossary words", Briefing);
            Section("red team run", RedRun);
            Section("soc analyst run", SocRun);
            Section("blue team run", BlueRun);
            Section("ctf puzzle and hints", Puzzle);
            Section("linux ladder", Ladder);
            Section("defender", Defender);
            Section("give up and the report", GiveUp);
            Section("progress saved", Saved);
            Section("resume", Resume);
            Section("back during a run", BackDuringRun);
            phone = true;
            win.Width = 400; win.Height = 840; Pump();
            Section("phone", Phone);
        }
        finally { phone = false; win.Width = 1180; win.Height = 800; A.EndRun(); win.Close(); }
    }

    static void Section(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(name + ": ran without an error", false, e.ToString()); }
        A.EndRun();
        mv.PopTo(1); Pump();
    }

    static IEnumerable<T> Find<T>(Control root) where T : class => CertUi.Find<T>(root);
    static RangePage Page => mv.Page as RangePage;

    /// <summary>Every button on the page is at least 44 px high, and nothing reaches past the window's right edge
    /// (except inside parts that scroll sideways: the tab row, terminal blocks).</summary>
    static void Fits(string page)
    {
        Pump();
        var root = mv.Page.View;
        var small = Find<Button>(root).Where(b => b.IsEffectivelyVisible && b.Bounds.Height > 0 && b.Bounds.Height < 43.5).Select(b => CertUi.ButtonText(b) + " " + b.Bounds.Height).ToList();
        Check(page + ": every button is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(5)));
        var wide = new List<string>();
        void Walk(Visual v)
        {
            if (v is ScrollViewer sv && sv.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled) return;
            if (v is Control c && c.IsEffectivelyVisible && c.Bounds.Width > 0)
            {
                var p = c.TranslatePoint(new Point(c.Bounds.Width, 0), win);
                if (p != null && p.Value.X > win.Width + 1.5) wide.Add(c.GetType().Name + " " + (int)p.Value.X);
            }
            foreach (var ch in v.GetVisualChildren()) Walk(ch);
        }
        Walk(root);
        Check(page + ": nothing is wider than the window (" + win.Width + " px)", wide.Count == 0, string.Join(", ", wide.Take(6)));
    }

    static bool PressCard(Control root, string tag)
    {
        var c = Find<RangeCard>(root).FirstOrDefault(x => (x.Tag as string) == tag);
        if (c == null) return false;
        c.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        return true;
    }
    static bool Press(string label) { bool ok = CertUi.Press(mv.Page.View, label); Pump(); return ok; }
    static bool PressStarting(string start)
    {
        var b = Find<Button>(mv.Page.View).FirstOrDefault(x => CertUi.ButtonText(x).StartsWith(start, StringComparison.Ordinal) && x.IsEffectivelyEnabled);
        if (b == null) return false;
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        return true;
    }
    static string AllText(Control root) => string.Join("\n", Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<TextBlock>().Select(t => t.Inlines != null && t.Inlines.Count > 0 ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text ?? ""));

    /// <summary>Goes home -> category -> level -> the challenge by taps (a scenario stops at its briefing).</summary>
    static void OpenByTaps(RangeItem it)
    {
        mv.PopTo(1);
        mv.Push(new RangeHomePage(A)); Pump();
        Check(it.Id + ": the category card opens", PressCard(mv.Page.View, "category:" + it.Category) && mv.Page is RangeCategoryPage);
        Check(it.Id + ": the level card opens", PressCard(mv.Page.View, "level:" + it.Level) && mv.Page is RangeLevelPage);
        Check(it.Id + ": the challenge card opens", PressCard(mv.Page.View, "item:" + it.Id) && (it.Puzzle ? mv.Page is RangeRunPage : mv.Page is RangeBriefingPage), mv.Page?.GetType().Name);
    }
    static RangeItem Item(string id) => A.Items.First(i => i.Id == id);
    static RangeItem FirstOf(string cat, string level = "Beginner") => A.Items.Where(i => i.Category == cat && i.Level == level).OrderBy(i => i.Order).ThenBy(i => i.Id, StringComparer.Ordinal).First();

    /// <summary>Types a solution into the run page's terminal (special lines, #key / #wait..., through the simulator).</summary>
    static string TypeSolution(RangeRunPage rp, IEnumerable<string> lines)
    {
        var run = rp.Run;
        foreach (var raw in lines)
        {
            string l = RangeData.Fill(raw, run.Vars, null);
            if (l.StartsWith('#') && !l.StartsWith("#!", StringComparison.Ordinal)) { run.Sim.Type(run.StartMachine, l); rp.Term.Poll(); }
            else rp.Term.TypeLine(l);
        }
        rp.Refresh(); Pump();
        return rp.Term.Text;
    }

    // ------------------------------------------------------------ App Hub's range-look checks (the engine's look data, the walkthrough)
    static void Look()
    {
        Check("the scenarios and puzzles load", A.Scenarios.Count >= 8 && A.Puzzles.Count >= 20 && A.LoadProblems.Count == 0, string.Join("; ", A.LoadProblems));
        Check("no animations in the checks", !RangeFx.On);
        Check("every category has an icon the kit draws", RangeData.Categories.All(c => RangeLook.HasIcon(RangeLook.For(c).Icon)));
        var w = RangeWalk.Render("1. Look around. nmap -sn 10.0.0.0/24 finds it.\n\n    cat /etc/passwd\n\nHow to fix it: change the password.");
        Check("a walkthrough is laid out as a step, a code box and a lesson", w.Children.Count == 3 && w.Children[1] is Border);
        var prose = new SelectableTextBlock { Inlines = new Avalonia.Controls.Documents.InlineCollection() };
        RangeWalk.Prose(prose.Inlines, "Run sudo -l then read /etc/shadow on 10.0.0.5.", 14);
        Check("commands, paths and addresses in the prose are code", prose.Inlines.OfType<Avalonia.Controls.Documents.Run>().Count(r => r.FontFamily == Rg.Mono) >= 2, string.Join("|", prose.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        var entry = HomePage.Categories.SelectMany(c => c.Entries).First(e => e.Id == "range");
        Check("the home page's Cyber Range entry opens Cyber Range", entry.Open != null && entry.Open() is RangeHomePage && RangeApp.Current == A);
    }

    // ------------------------------------------------------------ home
    static void Home()
    {
        var home = new RangeHomePage(A);
        mv.Push(home); Pump(); Snap("home-new");
        string t = AllText(home.View);
        Check("home: the hero says Recruit and offers the first challenge and the tour", t.Contains("Recruit") && Find<Button>(home.View).Any(b => CertUi.ButtonText(b) == "Start your first challenge") && Find<Button>(home.View).Any(b => CertUi.ButtonText(b) == "New here? Take the tour"));
        int cats = RangeData.Categories.Count(c => A.ItemsIn(c.Id).Any());
        Check("home: a card for every category with challenges", Find<RangeCard>(home.View).Count(c => (c.Tag as string ?? "").StartsWith("category:", StringComparison.Ordinal)) == cats, cats.ToString());
        Check("home: the categories' rings are drawn", Find<RangeRing>(home.View).Count() >= cats);
        Check("home: the ethics note is there", t.Contains("Only ever test computers you own"));
        Check("the rank chip in the top bar opens Badges and ranks", home.Extras is Button);
        (home.Extras as Button)?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        Check("...and does", mv.Page is RangeBadgesPage);
        mv.Back(); Pump();
        Fits("home");
    }

    static void Pages()
    {
        mv.Push(new RangeHomePage(A)); Pump();
        Check("the tour button opens the tour", Press("New here? Take the tour") && mv.Page is RangeTourPage);
        Pump(); Snap("tour");
        string t = AllText(mv.Page.View);
        Check("the tour has its six steps and the one rule", t.Contains("You get your own computer") && t.Contains("Stuck? Ask for a hint") && t.Contains("Only ever test computers you own or have clear permission"));
        Check("seeing the tour is remembered", A.Store.TourSeen);
        Fits("tour");
        Check("the tour's Words to know opens the glossary", Press("Words to know") && mv.Page is RangeGlossaryPage);
        var gp = (RangeGlossaryPage)mv.Page; Pump(); Snap("glossary");
        int all = gp.Shown;
        Check("the glossary lists every word", all == RangeGlossary.Terms.Count && all > 20, all + " of " + RangeGlossary.Terms.Count);
        gp.Search.Text = "port"; Pump();
        Check("searching narrows the list", gp.Shown > 0 && gp.Shown < all, gp.Shown.ToString());
        gp.Search.Text = "zzzqqq-nothing"; Pump();
        Check("no match says so", gp.Shown == 0 && AllText(gp.View).Contains("No word matches that."));
        gp.Search.Text = "";
        Fits("glossary");
        mv.Push(new RangeBadgesPage(A)); Pump(); Snap("badges");
        string b = AllText(mv.Page.View);
        Check("badges and ranks: every rank and badge", RangeProgress.Ranks.All(r => b.Contains(r.Name)) && RangeProgress.Badges.All(x => b.Contains(x.Name)) && b.Contains("You are here"));
        Fits("badges");
    }

    static void Categories()
    {
        foreach (var c in RangeData.Categories.Where(c => A.ItemsIn(c.Id).Any()))
        {
            var cp = new RangeCategoryPage(A, c); mv.Push(cp); Pump();
            if (c.Id == "red" || c.Id == "ctf") Snap("category-" + c.Id);
            var levels = RangeData.Levels.Where(l => A.ItemsIn(c.Id, l).Any()).ToList();
            Check(c.Id + ": a step for every level with challenges, never locked", Find<RangeCard>(cp.View).Count(x => (x.Tag as string ?? "").StartsWith("level:", StringComparison.Ordinal)) == levels.Count);
            Check(c.Id + ": the category's plain words are shown", AllText(cp.View).Contains(RangeLook.For(c).Simple));
            if (c.Id == "red") Fits("category " + c.Id);
            foreach (var l in levels)
            {
                var lp = new RangeLevelPage(A, c, l); mv.Push(lp); Pump();
                if (c.Id == "ctf" && l == "Intermediate" || c.Id == "red" && l == "Beginner") Snap("level-" + c.Id + "-" + l);
                var items = A.ItemsIn(c.Id, l).ToList();
                Check(c.Id + " " + l + ": a card for every challenge", Find<RangeCard>(lp.View).Count(x => (x.Tag as string ?? "").StartsWith("item:", StringComparison.Ordinal)) == items.Count);
                if (c.Id == "ctf" && l == "Intermediate") Fits("level " + c.Id + " " + l);
                mv.Back();
            }
            mv.Back(); Pump();
        }
    }

    // ------------------------------------------------------------ a briefing, Explain it simply, a glossary word
    static void Briefing()
    {
        var it = Item("open-door");
        OpenByTaps(it);
        var bp = (RangeBriefingPage)mv.Page;
        Snap("briefing");
        var sw = Find<RangeSwitch>(bp.View).FirstOrDefault(s => s.Label == "Explain it simply");
        Check("a Beginner briefing has Explain it simply", sw != null);
        if (sw != null) { sw.Toggle(); Pump(); }
        Check("Explain it simply shows the plain words and the words to know", A.ExplainOn && AllText(bp.View).Contains("In plain words"));
        Snap("briefing-explain");
        Check("the glossary's words are dotted (tappable) on Beginner", Find<GlossText>(bp.View).Any(g => g.Hits.Count > 0));
        Check("a tap on a dotted word shows its card", bp.TapTerm("port") || bp.TapTerm(null));
        Pump();
        Check("...over the page", bp.TermShown && bp.TermOnShow != null);
        Snap("briefing-glossary");
        Check("Back closes the card first", mv.Back() && !bp.TermShown && mv.Page == bp);
        var chip = Find<Button>(bp.View).FirstOrDefault(b => b.Parent is WrapPanel && RangeGlossary.Find(CertUi.ButtonText(b)) != null);
        Check("Words to know are 44 px buttons that open the card", chip != null);
        if (chip != null) { chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); Check("...and do", bp.TermShown); Check("Close closes it", Press("Close") && !bp.TermShown); }
        Fits("briefing");
        A.ExplainOn = false;
        var s = A.Scenarios.First(x => RangeData.LevelIndex(x.Level) > 0 && x.Category == "red");
        var adv = new RangeBriefingPage(A, s); mv.Push(adv); Pump();
        Check("above Beginner there are no dotted words and no Explain switch", !Find<GlossText>(adv.View).Any() && !Find<RangeSwitch>(adv.View).Any());
    }

    // ------------------------------------------------------------ a red team run, by taps and the terminal
    static void RedRun()
    {
        var it = Item("open-door");
        OpenByTaps(it);
        Check("Start the mission opens the run in the briefing's place", Press("Start the mission") && mv.Page is RangeRunPage && !mv.Has<RangeBriefingPage>());
        var rp = (RangeRunPage)mv.Page;
        var run = rp.Run;
        Check("the run's console is the terminal on the page", rp.Term.S == run.Console);
        Check("the run page's tabs: Brief, Map, Notes, Loot, Hints", new[] { "Brief", "Map", "Notes", "Loot", "Hints" }.All(n => rp.Tabs.Names.Contains(n)) && rp.Tabs.SelectedName == "Map");
        string walk = run.Walkthrough;
        string firstStep = (walk ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 30) ?? "";
        Check("the walkthrough is not on the run page", firstStep.Length > 0 && !AllText(rp.View).Contains(firstStep.Substring(0, 30)), firstStep);
        var s = run.Sc;
        var ip = s.Machines.First(m => m.Name == s.Start).FirstIp.Split('.');
        rp.Term.TypeLine("nmap -sn " + ip[0] + "." + ip[1] + "." + ip[2] + ".0/24");
        rp.Refresh(); Pump();
        Check("the map fills in after a scan", run.Net.Seen.Count >= 1 && rp.MapHosts >= 2, rp.MapHosts.ToString());
        Snap("run-map");
        string all = TypeSolution(rp, s.Solution);
        Check("the model solution typed into the terminal shows every flag", s.Flags.All(f => all.Contains(run.Flags[f.Id])), Tail(all));
        Snap("run-terminal");
        rp.NotesBox.Text = "found the share";
        Pump();
        Check("notes are kept for the challenge", A.Store.For(false, s.Id).Notes == "found the share");
        rp.FlagBox.Text = "FLAG{not-it}";
        Press("Submit");
        Check("a wrong flag is refused and said so", run.Found.Count == 0 && rp.Said.Length > 0 && mv.Page == rp, rp.Said);
        for (int i = 0; i < s.Flags.Count; i++)
        {
            rp.FlagBox.Text = run.Flags[s.Flags[i].Id];
            Press("Submit");
            if (i < s.Flags.Count - 1) { Check("flag " + (i + 1) + " captured, the meter moves", run.Found.Count == i + 1 && mv.Page == rp); Snap("run-flag-captured"); }
        }
        Check("the last flag shows the report at once in the checks (1.6 s on a screen)", mv.Page is RangeResultsPage);
        var res = mv.Page as RangeResultsPage;
        if (res == null) return;
        Snap("results");
        Check("the report: full score", res.TotalText == s.MaxPoints + " / " + s.MaxPoints, res.TotalText);
        Check("the report: the first flag and no-hints badges, points gained", res.Outcome.NewBadges.Contains("first-flag") && res.Outcome.NewBadges.Contains("no-hints") && res.Outcome.PointsAfter > res.Outcome.PointsBefore, string.Join(",", res.Outcome.NewBadges));
        Check("the report shows the walkthrough now", AllText(res.View).Contains(firstStep.Substring(0, 30)) || Find<StackPanel>(res.View).Any(x => (x.Tag as string) == "walkthrough" && x.Children.Count > 0));
        Check("the run is recorded as done", A.IsDone(it) && A.Store.For(false, s.Id).BestScore == s.MaxPoints);
        Fits("results");
        Check("Play again starts a new run in the report's place", Press("Play again") && mv.Page is RangeRunPage && A.Run != null && A.Run != run);
        A.EndRun();
    }

    // ------------------------------------------------------------ SOC analyst: questions answered in the Findings tab
    static void SocRun()
    {
        var it = FirstOf("soc");
        OpenByTaps(it);
        Press(A.Store.For(false, it.Id).Attempts > 0 ? "Start again" : "Start the mission");
        var rp = mv.Page as RangeRunPage;
        Check("soc: the run starts on the Findings tab", rp != null && rp.Tabs.SelectedName == "Findings" && !rp.Tabs.Names.Contains("Loot"), rp?.Tabs.SelectedName);
        if (rp == null) return;
        var run = rp.Run;
        string all = TypeSolution(rp, run.Sc.Solution);
        Check("soc: the siem answers in the terminal", all.Contains("siem") || all.Length > 200, Tail(all));
        Snap("run-soc");
        foreach (var q in run.Sc.Questions.ToList())
        {
            if (mv.Page != rp) break;
            if (!rp.AnswerBoxes.TryGetValue(q.Id, out var box)) { Check("soc: question " + q.Id + " has an answer box", false); continue; }
            box.Text = RangeData.Fill(q.Answer, run.Vars, run.Flags);
            var btn = Find<Button>(box.Parent as Control).First(b => CertUi.ButtonText(b) == "Answer");
            btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("soc: question " + q.Id + " answered by its model answer", run.Answered.ContainsKey(q.Id) && !run.Missed.Contains(q.Id), rp.Said);
        }
        foreach (var t in run.Sc.Tasks.ToList()) { if (mv.Page != rp) break; var b = Find<Button>(rp.View).First(x => (x.Tag as string) == "check:" + t.Id); b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); }
        Check("soc: everything answered -> the report with full marks", mv.Page is RangeResultsPage r && r.TotalText == run.MaxPoints + " / " + run.MaxPoints, (mv.Page as RangeResultsPage)?.TotalText);
        Snap("results-soc");
    }

    // ------------------------------------------------------------ blue team: questions and fixes
    static void BlueRun()
    {
        var it = FirstOf("blue");
        var page = new RangeBriefingPage(A, it.Sc); mv.Push(page); Pump();
        Press("Start the mission");
        var rp = mv.Page as RangeRunPage;
        if (rp == null) { Check("blue: the run starts", false); return; }
        var run = rp.Run;
        Snap("run-blue");
        TypeSolution(rp, run.Sc.Solution);
        foreach (var t in run.Sc.Tasks.ToList())
        {
            var b = Find<Button>(rp.View).FirstOrDefault(x => (x.Tag as string) == "check:" + t.Id);
            Check("blue: fix " + t.Id + " has a Check button", b != null);
            b?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("blue: fix " + t.Id + " passes after the solution", run.TasksDone.Contains(t.Id), rp.Said);
        }
        foreach (var q in run.Sc.Questions.ToList())
        {
            if (mv.Page != rp || !rp.AnswerBoxes.TryGetValue(q.Id, out var box)) continue;
            box.Text = RangeData.Fill(q.Answer, run.Vars, run.Flags);
            Find<Button>(box.Parent as Control).First(b => CertUi.ButtonText(b) == "Answer").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        }
        if (mv.Page == rp && run.Sc.Flags.Count > 0)
            foreach (var f in run.Sc.Flags) { if (mv.Page != rp) break; rp.FlagBox.Text = run.Flags[f.Id]; Press("Submit"); }
        Check("blue: every question and fix -> the report with full marks", mv.Page is RangeResultsPage r && r.TotalText == run.MaxPoints + " / " + run.MaxPoints, (mv.Page as RangeResultsPage)?.TotalText + " " + run.HintCost);
        if (mv.Page is RangeResultsPage) { Snap("results-blue"); Check("blue: the report has the attacker's timeline", AllText(mv.Page.View).Contains("contained the attacker") || AllText(mv.Page.View).Contains("reached its goal")); }
    }

    // ------------------------------------------------------------ a CTF puzzle, with hints
    static void Puzzle()
    {
        var it = Item("b01-base64");
        OpenByTaps(it);
        var rp = mv.Page as RangeRunPage;
        if (rp == null) return;
        var run = rp.Run;
        Check("a puzzle opens on its Brief with Explain it simply (Beginner)", rp.Tabs.SelectedName == "Brief" && Find<RangeSwitch>(rp.View).Any(s => s.Label == "Explain it simply"));
        rp.Tabs.Select("Hints"); Pump();
        Check("hints are hidden until asked", rp.HintsShown == 0 && run.HintsUsed.Count == 0);
        Snap("run-hints-none");
        int max = run.MaxPoints;
        string cost1 = "(-" + run.Hints[0].Cost + " points)";
        Check("the first hint's button says what it costs; later ones are locked", PressStarting("Show hint 1 of " + run.Hints.Count) && rp.HintsShown == 1 && run.HintCost == run.Hints[0].Cost && !Find<Button>(rp.View).Any(b => CertUi.ButtonText(b).StartsWith("Show hint 3", StringComparison.Ordinal)), cost1 + " " + run.HintCost);
        Check("the hint's text is shown", AllText(rp.View).Contains(run.Hints[0].Text.Substring(0, Math.Min(20, run.Hints[0].Text.Length))));
        var learn = Find<RangeSwitch>(rp.View).FirstOrDefault(s => s.Label.StartsWith("Learning mode", StringComparison.Ordinal));
        Check("Beginner has Learning mode", learn != null);
        learn?.Toggle(); Pump();
        Check("Learning mode makes hints free", run.LearningMode && run.HintCost == 0 && PressStarting("Show hint 2") && run.HintCost == 0 && rp.HintsShown == 2);
        Snap("run-hints");
        rp.Term.TypeLine("help"); Pump();
        var said = rp.Term.Text;
        Check("help also names the everyday commands and the range's tools on the box, with man", said.Contains("Everyday commands on this box") && said.Contains("base64") && said.Contains("The range's tools on this box") && said.Contains("`man base64'") && !said.Contains("xfs_repair"), Tail(said));
        rp.Term.TypeLine("help all"); Pump();
        Check("help all lists every program", rp.Term.Text.Contains("Every program on this box") && rp.Term.Text.Contains("xfs_repair"), Tail(rp.Term.Text));
        rp.Tabs.Select("Brief"); Pump();
        TypeSolution(rp, run.Pz.Solution);
        Snap("run-puzzle");
        string answer = run.Pz.Answer == "flag" ? run.Flags[""] : RangeData.Fill(run.Pz.Answer, run.Vars, run.Flags);
        Check("the puzzle's answer shows in the terminal", rp.Term.Text.Contains(answer), Tail(rp.Term.Text));
        rp.FlagBox.Text = answer;
        Press("Submit");
        Check("the answer box solves the puzzle -> the report", mv.Page is RangeResultsPage);
        if (mv.Page is RangeResultsPage r) Check("the report lists the hints revealed", AllText(r.View).Contains("Hints you revealed"));
    }

    // ------------------------------------------------------------ a Linux Ladder step
    static void Ladder()
    {
        var it = FirstOf("ladder");
        OpenByTaps(it);
        var rp = mv.Page as RangeRunPage;
        if (rp == null) return;
        var run = rp.Run;
        Check("ladder: the console signs in as the step's account", rp.Term.Text.Contains(run.Pz.Login) || rp.Term.S.Prompt.Contains(run.Pz.Login), rp.Term.S.Prompt);
        TypeSolution(rp, run.Pz.Solution);
        Snap("run-ladder");
        string answer = RangeData.Fill(run.Pz.Answer, run.Vars, run.Flags);
        Check("ladder: the step's password shows in the terminal", rp.Term.Text.Contains(answer), Tail(rp.Term.Text));
        rp.FlagBox.Text = answer;
        rp.DoSubmit(); Pump();
        Check("ladder: the password climbs the step -> the report", mv.Page is RangeResultsPage && A.IsDone(it));
    }

    // ------------------------------------------------------------ red vs defender: the Defender tab
    static void Defender()
    {
        var s = A.Scenarios.First(x => x.Category == "defender" && x.NoisySolution.Count > 0);
        var page = new RangeBriefingPage(A, s); mv.Push(page); Pump();
        Check("defender: the briefing warns that a defender is watching", AllText(page.View).Contains("A defender is watching"));
        Press("Start the mission");
        var rp = mv.Page as RangeRunPage;
        if (rp == null) { Check("defender: the run starts", false); return; }
        Check("defender: the run has a Defender tab", rp.Tabs.Names.Contains("Defender"));
        TypeSolution(rp, rp.Run.Sc.NoisySolution);
        for (int i = 0; i < 20 && rp.Run.Defender != null && rp.Run.Defender.Blocks == 0; i++) rp.Run.Tick(60);
        rp.Refresh(); rp.Tabs.Select("Defender"); Pump();
        Check("defender: noise is noticed and the tab says so", rp.Run.Defender != null && rp.Run.Defender.Alerts > 0 && AllText(rp.View).Contains("WHAT IT NOTICED"), rp.Run.Defender == null ? "no defender" : rp.Run.Defender.Alerts + " alerts, page " + mv.Page?.GetType().Name);
        Snap("run-defender");
    }

    // ------------------------------------------------------------ giving up: the report and its walkthrough
    static void GiveUp()
    {
        var it = Item("b02-rot13");
        A.StartPuzzle(it.Pz); Pump();
        var rp = (RangeRunPage)mv.Page;
        Check("Give up ends the run (at once in the checks) and shows the report", Press("Give up") && mv.Page is RangeResultsPage);
        var r = (RangeResultsPage)mv.Page;
        Snap("results-gave-up");
        Check("a run given up: not done, the way is shown", !A.IsDone(it) && AllText(r.View).Contains("Not everything this time") && Find<StackPanel>(r.View).Any(x => (x.Tag as string) == "walkthrough" && x.Children.Count > 0));
        Check("More like this goes to the level", Press("More like this") && mv.Page is RangeLevelPage lp && lp.Level == it.Level);
    }

    // ------------------------------------------------------------ progress saved and read back
    static void Saved()
    {
        string file = Path.Combine(CertLabData.Dir, "range", "progress.json");
        Check("progress is saved (range\\progress.json)", File.Exists(file));
        int pts = RangeProgress.TotalPoints(A.Store), badges = A.Store.Badges.Count;
        var again = RangeApp.Fresh(true);
        Check("progress reads back: points, badges, done, notes", RangeProgress.TotalPoints(again.Store) == pts && again.Store.Badges.Count == badges && again.IsDone(again.Items.First(i => i.Id == "open-door")) && again.Store.For(false, "open-door").Notes == "found the share", RangeProgress.TotalPoints(again.Store) + " vs " + pts);
        A = again;
        var home = new RangeHomePage(A); mv.Push(home); Pump(); Snap("home");
        Check("home shows the progress (rank points, a Continue card)", AllText(home.View).Contains(Rg.N(pts) + " points") && Find<RangeCard>(home.View).Any(c => Avalonia.Automation.AutomationProperties.GetName(c).StartsWith("Continue", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------ keeping a run when the app closes, and bringing it back
    static void Resume()
    {
        var it = Item("b03-reversed");
        A.StartPuzzle(it.Pz); Pump();
        var rp = (RangeRunPage)mv.Page;
        rp.Tabs.Select("Hints"); PressStarting("Show hint 1");
        rp.NotesBox.Text = "reversed text"; Pump();
        rp.Run.Seconds = 95;
        Check("the app closing keeps the run", A.KeepWork() == "run" && File.Exists(A.ResumeFile));
        int seed = rp.Run.Seed;
        A.EndRun(); mv.PopTo(1);
        A = RangeApp.Fresh(true);
        var home = new RangeHomePage(A); mv.Push(home); Pump();
        Check("home offers to carry on", home.Offer.Contains(it.Title), home.Offer);
        Snap("home-resume");
        Check("Carry on brings the run back with its hint, time and notes", Press("Carry on where you left off") && mv.Page is RangeRunPage r2 && r2.Run.Id == it.Id && r2.Run.Seed == seed && r2.Run.HintsUsed.Contains(0) && r2.Run.Seconds == 95 && r2.NotesBox.Text == "reversed text");
        A.EndRun(); mv.PopTo(1);
        var h2 = new RangeHomePage(A); mv.Push(h2); Pump();
        Check("...and is offered only once", h2.Offer.Length == 0);
        // an unreadable resume file: a sentence, never a crash, the file kept
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(A.ResumeFile), "resume.json*")) File.WriteAllText(f, "{ this is not json");
        mv.PopTo(1);
        var h3 = new RangeHomePage(A); mv.Push(h3); Pump();
        Check("an unreadable resume file: no offer, a sentence, the file kept aside", h3.Offer.Length == 0 && A.StatusText.Contains("couldn't be opened") && Directory.GetFiles(Path.GetDirectoryName(A.ResumeFile)).Any(f => f.Contains("unreadable", StringComparison.Ordinal)), A.StatusText);
    }

    static void BackDuringRun()
    {
        var it = Item("b01-base64");
        mv.Push(new RangeLevelPage(A, RangeData.CategoryById("ctf"), "Beginner")); Pump();
        A.StartPuzzle(it.Pz); Pump();
        var rp = (RangeRunPage)mv.Page;
        Check("Back during a run asks first", mv.Back() && mv.MessageOpen && mv.Page == rp);
        mv.PressMessage("Stay"); Pump();
        Check("Stay keeps the run", mv.Page == rp && A.Run != null);
        mv.Back(); mv.PressMessage("Leave"); Pump();
        Check("Leave ends the run (no score) and goes back", mv.Page is RangeLevelPage && A.Run == null);
    }

    // ------------------------------------------------------------ phone-sized
    static void Phone()
    {
        var home = new RangeHomePage(A); mv.Push(home); Pump(); Snap("home");
        Fits("phone home");
        mv.Push(new RangeTourPage(A)); Pump(); Snap("tour"); Fits("phone tour"); mv.Back();
        mv.Push(new RangeBadgesPage(A)); Pump(); Snap("badges"); Fits("phone badges"); mv.Back();
        mv.Push(new RangeGlossaryPage(A)); Pump(); Snap("glossary"); Fits("phone glossary"); mv.Back();
        foreach (var c in RangeData.Categories.Where(c => A.ItemsIn(c.Id).Any()))
        {
            mv.Push(new RangeCategoryPage(A, c)); Pump();
            if (c.Id == "red" || c.Id == "soc") Snap("category-" + c.Id);
            Fits("phone category " + c.Id);
            var lvl = RangeData.Levels.First(l => A.ItemsIn(c.Id, l).Any());
            mv.Push(new RangeLevelPage(A, c, lvl)); Pump();
            if (c.Id == "ctf" || c.Id == "red") Snap("level-" + c.Id);
            Fits("phone level " + c.Id);
            mv.Back(); mv.Back();
        }
        var s = A.Scenarios.First(x => x.Id == "open-door");
        A.ExplainOn = true;
        var bp = new RangeBriefingPage(A, s); mv.Push(bp); Pump(); Snap("briefing");
        Fits("phone briefing");
        bp.TapTerm(null); Pump(); Snap("briefing-glossary");
        Check("phone: the glossary card fits the screen", bp.TermShown && Find<Border>(bp.View).Where(b => b.Parent is Canvas).All(b => b.Bounds.Width <= 400 - 32 + 1));
        mv.Back();
        A.ExplainOn = false;
        Check("phone: the briefing's start button", PressStarting("Start"));
        var rp = mv.Page as RangeRunPage;
        Check("phone: the run shows the terminal, the panel waits behind Mission", rp != null && rp.Narrow && !rp.PanelOpen && rp.Term.IsEffectivelyVisible, rp == null ? mv.Page?.GetType().Name : rp.Narrow + " " + rp.PanelOpen + " " + rp.Term.IsEffectivelyVisible);
        if (rp != null)
        {
            rp.Term.TypeLine("ls -la"); Pump(); Snap("run");
            Fits("phone run");
            Check("Mission opens the card and tabs over the terminal", Press("Mission") && rp.PanelOpen && !rp.Term.IsEffectivelyVisible);
            Pump(); Snap("run-mission");
            Fits("phone run panel");
            rp.Tabs.Select("Hints"); Pump(); Snap("run-hints");
            Check("Back closes the panel first", mv.Back() && !rp.PanelOpen && mv.Page == rp);
            var run = rp.Run;
            TypeSolution(rp, run.Sc.Solution);
            foreach (var f in run.Sc.Flags) { rp.FlagBox.Text = run.Flags[f.Id]; rp.DoSubmit(); Pump(); }
            Check("phone: the flags (through the panel's box) end in the report", mv.Page is RangeResultsPage);
            Pump(); Snap("results");
            Fits("phone results");
        }
        A.EndRun(); mv.PopTo(1);
        var soc = A.Items.First(i => i.Category == "soc" && i.Level == "Beginner");
        A.StartScenario(soc.Sc); Pump();
        if (mv.Page is RangeRunPage sp) { sp.SetPanel(true); Pump(); Snap("run-soc-findings"); Fits("phone soc findings"); }
    }
}
