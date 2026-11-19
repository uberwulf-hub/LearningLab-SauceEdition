// Cyber Range (Learning Lab) - what App Hub's RangeView held: the scenarios and puzzles (RangeData), the glossary, the
// progress (RangeStore: range\progress.json through SafeFile), the run on screen, the status line, and keeping a run in
// progress when the app closes (RangeResume: range\resume.json, offered back the next time Cyber Range opens). App Hub's
// one window with breadcrumbs became pages on Learning Lab's stack: home -> category -> level -> briefing -> run ->
// report (the report replaces the run). One RangeApp lives while Cyber Range is open; Quiet = the checks and pictures
// (in-memory clipboard, seed 1, no animations, no timers, the report at once).
using CertLab;
using LearningLab.CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

/// <summary>What a finished run changed: points before and after, and badges earned (for the report).</summary>
public sealed class RangeOutcome
{
    public int PointsBefore, PointsAfter, BestBefore;
    public List<string> NewBadges = new();
}

public sealed class RangeApp
{
    public static RangeApp Current { get; private set; }

    public readonly bool Quiet;
    public readonly string DataDir;
    public readonly List<RangeScenario> Scenarios = new();
    public readonly List<RangePuzzle> Puzzles = new();
    public readonly List<string> LoadProblems = new();
    public List<RangeItem> Items = new();
    public RangeStore Store;
    public RangeRun Run;
    public RangeRunPage RunPage;
    public RangeOutcome LastOutcome;
    /// <summary>"Explain it simply" stays on or off as the person left it (while the app runs).</summary>
    public bool ExplainOn;

    public string StatusText { get; private set; } = "";
    public event Action<string> StatusChanged;

    public static RangeApp Open(bool quiet)
    {
        if (Current != null) return Current;
        Current = new RangeApp(CertLabData.Dir, quiet);
        return Current;
    }
    /// <summary>The checks: a fresh one (reads the files again).</summary>
    public static RangeApp Fresh(bool quiet) { Current?.EndRun(); Current = new RangeApp(CertLabData.Dir, quiet); return Current; }

    RangeApp(string dataDir, bool quiet)
    {
        DataDir = dataDir; Quiet = quiet;
        RangeFx.On = !quiet;
        CertTerminal.QuietRun = quiet;
        if (quiet) ClipText.UseMemory();
        else
        {
            if (!(ClipText.Current is AppClipboard)) ClipText.Current = new AppClipboard(() => Avalonia.Controls.TopLevel.GetTopLevel(MainView.Current));
            try { CertLabData.BringAcrossFromAppHub(); } catch (Exception) { }
        }
        RangeUi.OnError = Status;
        string root = CertLabContent.Root;
        Man.Dir = Path.Combine(root, "Courses", "rhcsa", "man");
        PsHelp.Dir = Path.Combine(root, "Courses", "powershell", "help");
        RangeData.Load(CertLabContent.RangeDir, Scenarios, Puzzles, LoadProblems);
        RangeGlossary.Load(Path.Combine(CertLabContent.RangeDir, "glossary.txt"));
        Items = RangeItem.All(Scenarios, Puzzles);
        Store = RangeStore.Load(Path.Combine(dataDir, "range", "progress.json"), false);
        // badges the saved progress already shows (an older file, or content added since): saved with the next save
        if (Store.LoadProblem.Length == 0) RangeProgress.Award(Store, Items, null, DateTime.Now);
        if (!quiet) MainView.AppClosing += () => { try { KeepWork(); } catch (Exception) { } };
        if (Store.LoadProblem.Length > 0) Status(Store.LoadProblem);
        else if (LoadProblems.Count > 0) Status(LoadProblems.Count + " challenge file(s) could not be read.");
    }

    public void Status(string s) { StatusText = s ?? ""; StatusChanged?.Invoke(StatusText); }

    // ------------------------------------------------------------ progress helpers
    public static string CatTitle(string id) { var c = RangeData.CategoryById(id); return c == null ? id : c.Title; }
    public IEnumerable<RangeItem> ItemsIn(string cat) => Items.Where(i => i.Category == cat);
    public IEnumerable<RangeItem> ItemsIn(string cat, string level) => Items.Where(i => i.Category == cat && i.Level == level);
    public bool IsDone(RangeItem it) { var d = it.Puzzle ? Store.Puzzles : Store.Scenarios; return d.TryGetValue(it.Id, out var r) && r.Completed; }
    public bool IsTried(RangeItem it) { var d = it.Puzzle ? Store.Puzzles : Store.Scenarios; return d.TryGetValue(it.Id, out var r) && (r.Attempts > 0 || r.LastPlayed > 0); }
    public int DoneInCategory(string cat, out int total) { var l = ItemsIn(cat).ToList(); total = l.Count; return l.Count(IsDone); }
    public int DoneInLevel(string cat, string level, out int total) { var l = ItemsIn(cat, level).ToList(); total = l.Count; return l.Count(IsDone); }
    public RangeItem Next(out bool resuming) => RangeProgress.Next(Store, Items, out resuming);
    public RangeItem ItemOf(RangeRun r) => Items.FirstOrDefault(i => i.Puzzle == (r.Pz != null) && i.Id == r.Id);

    static MainView Mv => MainView.Current;

    // ------------------------------------------------------------ opening challenges
    /// <summary>Opens a challenge from a list: a scenario's briefing, or a puzzle straight away.</summary>
    public void Open(RangeItem it)
    {
        if (it == null) return;
        if (it.Puzzle) StartPuzzle(it.Pz); else Mv.Push(new RangeBriefingPage(this, it.Sc));
    }

    int NewSeed => Quiet ? 1 : (int)(DateTime.Now.Ticks % 2000000000) + 1;

    public RangeRunPage StartScenario(RangeScenario s, int seed = 0, Action<RangeRun> before = null, bool replace = false)
    {
        EndRun();
        Run = RangeRun.ForScenario(s, seed == 0 ? NewSeed : seed);
        before?.Invoke(Run);
        Run.StartConsole();
        Store.Touch(false, s.Id, DateTime.Now);
        return ShowRun(replace);
    }
    public RangeRunPage StartPuzzle(RangePuzzle p, int seed = 0, Action<RangeRun> before = null, bool replace = false)
    {
        EndRun();
        Run = RangeRun.ForPuzzle(p, seed == 0 ? NewSeed : seed);
        before?.Invoke(Run);
        Run.StartConsole();
        Store.Touch(true, p.Id, DateTime.Now);
        return ShowRun(replace);
    }
    RangeRunPage ShowRun(bool replace)
    {
        RunPage = new RangeRunPage(this, Run);
        if (replace || Mv.Page is RangeBriefingPage || Mv.Page is RangeResultsPage) Mv.Replace(RunPage); else Mv.Push(RunPage);
        return RunPage;
    }

    /// <summary>Ends the run on screen without recording it (leaving it, or starting another).</summary>
    public void EndRun()
    {
        RunPage?.Stop();
        if (Run != null) { try { Run.Dispose(); } catch (Exception) { } Run = null; }
        RunPage = null;
    }

    /// <summary>The run is over (everything captured, or given up): records it, awards badges, shows the report in the
    /// run page's place. The walkthrough is shown only from here on.</summary>
    public RangeResultsPage FinishRun(bool gaveUp)
    {
        if (Run == null) return null;
        Run.GaveUp = gaveUp;
        var o = new RangeOutcome { PointsBefore = RangeProgress.TotalPoints(Store), BestBefore = Store.For(Run.Pz != null, Run.Id).BestScore };
        Store.Record(Run, false);
        o.NewBadges = RangeProgress.Award(Store, Items, Run, DateTime.Now);
        o.PointsAfter = RangeProgress.TotalPoints(Store);
        Store.Save();
        LastOutcome = o;
        if (Store.SaveProblem.Length > 0) Status(Store.SaveProblem);
        var done = Run;
        RunPage?.Stop();
        RunPage = null;
        Run = null;
        DismissResume();
        var page = new RangeResultsPage(this, done, o);
        if (Mv.Page is RangeRunPage) Mv.Replace(page); else Mv.Push(page);
        try { done.Dispose(); } catch (Exception) { }
        return page;
    }

    // ------------------------------------------------------------ keeping a run (range\resume.json)
    public string ResumeFile => RangeResume.FileIn(DataDir);
    public bool HasWorkInProgress => Run != null && !Run.Finished && !Run.GaveUp;

    /// <summary>Keeps the progress (notes too) and the run in progress (the app closing). Returns "run" or "".</summary>
    public string KeepWork()
    {
        Store?.Save();
        if (!HasWorkInProgress) return "";
        var run = Run;
        var r = new RangeResume { Puzzle = run.Pz != null, Id = run.Id, Title = run.Title, Seed = run.Seed, LearningMode = run.LearningMode, Breached = run.Breached, Seconds = run.Seconds, Clock = run.ClockElapsed };
        r.Found.AddRange(run.Found);
        r.HintsUsed.AddRange(run.HintsUsed);
        foreach (var kv in run.Answered) r.Answered[kv.Key] = kv.Value;
        r.Missed.AddRange(run.Missed);
        foreach (var kv in run.WrongTries) r.WrongTries[kv.Key] = kv.Value;
        r.AttackerSteps.AddRange(run.AttackerSteps);
        r.AttackerLog.AddRange(run.AttackerLog);
        if (run.Net != null)
        {
            foreach (var s in run.Net.Seen.Values) r.Seen.Add(new SeenHost { Ip = s.Ip, Name = s.Name, Os = s.Os, Owned = s.Owned, Ports = new List<int>(s.Ports), Users = new List<string>(s.Users) });
            foreach (var w in run.Net.Worked) r.Loot.Add(new LootItem { Host = w.Host, User = w.User, Secret = w.Secret, Where = w.Where, Auto = w.Auto });
        }
        r.Notes = Store.For(r.Puzzle, r.Id).Notes ?? "";
        r.Saved = DateTime.Now.Ticks;
        r.Save(ResumeFile);
        return "run";
    }

    RangeResume PendingResume(out string problem)
    {
        var r = RangeResume.Load(ResumeFile, out problem);
        if (problem.Length > 0) problem = "Your challenge from last time couldn't be opened. Nothing was written over it: a copy is kept beside your progress.";
        if (r == null || !r.Pending) return null;
        if (r.Puzzle ? !Puzzles.Any(p => p.Id == r.Id) : !Scenarios.Any(s => s.Id == r.Id)) return null;
        return r;
    }

    /// <summary>The home banner's sentence when a run is waiting, else "".</summary>
    public string ResumeOffer()
    {
        if (HasWorkInProgress) return "";
        var r = PendingResume(out var problem);
        if (problem.Length > 0) Status(problem);
        if (r == null) return "";
        return "Last time you were in a challenge (" + r.Title + "). Carry on to get it back with your notes, flags, " + (r.Answered.Count > 0 ? "answers, " : "") + "hints and time. The pretend machines start again from the beginning, so make any changes on them again.";
    }

    /// <summary>"Carry on where you left off": starts the kept run again from its seed and puts its state back.</summary>
    public string ResumeNow()
    {
        var r = PendingResume(out var problem);
        if (r == null) { string s0 = problem.Length > 0 ? problem : "There is nothing to carry on with."; Status(s0); return s0; }
        var res = Store.For(r.Puzzle, r.Id);
        if (string.IsNullOrEmpty(res.Notes) && r.Notes.Length > 0) res.Notes = r.Notes;
        Action<RangeRun> before = run => RestoreRun(run, r);
        if (r.Puzzle) StartPuzzle(Puzzles.First(p => p.Id == r.Id), r.Seed, before);
        else StartScenario(Scenarios.First(s => s.Id == r.Id), r.Seed, before);
        r.Used = true;
        try { r.Save(ResumeFile); } catch (Exception) { }
        string said = "Your challenge is back: notes, flags, hints and time. The pretend machines started again from the beginning.";
        Status(said);
        return said;
    }

    /// <summary>"Not now": the offer goes away (the file is kept, marked used).</summary>
    public void DismissResume()
    {
        var r = RangeResume.Load(ResumeFile, out _);
        if (r == null || !r.Pending) return;
        r.Used = true;
        try { r.Save(ResumeFile); } catch (Exception) { }
    }

    /// <summary>Puts a kept run's state into a run rebuilt from the same seed: the pretend clock moves on as far as
    /// before (the blue team attacker's steps go as they went), then the scores, hints, map and loot.</summary>
    public static void RestoreRun(RangeRun run, RangeResume r)
    {
        if (run.Sim != null && run.Sim.Idle && r.Clock > 0)
        {
            run.Replay = r.AttackerSteps;
            try { run.Net.Lab.Advance(Math.Min(r.Clock, 86400)); run.RunAttacker(); }
            finally { run.Replay = null; }
        }
        var flagIds = run.Pz != null ? new List<string> { "flag" } : run.Sc.Flags.Select(f => f.Id).ToList();
        run.Found.Clear(); run.Found.AddRange(r.Found.Where(flagIds.Contains).Distinct());
        run.HintsUsed.Clear(); run.HintsUsed.AddRange(r.HintsUsed.Where(i => i >= 0 && i < run.Hints.Count).Distinct());
        run.LearningMode = r.LearningMode;
        run.Seconds = Math.Max(0, r.Seconds);
        if (run.Sc != null)
        {
            var qs = run.Sc.Questions.Select(q => q.Id).ToList();
            run.Answered.Clear(); foreach (var kv in r.Answered) if (qs.Contains(kv.Key)) run.Answered[kv.Key] = kv.Value ?? "";
            run.Missed.Clear(); foreach (var m in r.Missed) if (qs.Contains(m)) run.Missed.Add(m);
            run.WrongTries.Clear(); foreach (var kv in r.WrongTries) if (qs.Contains(kv.Key)) run.WrongTries[kv.Key] = kv.Value;
            if (run.IsBlue) { run.AttackerLog.Clear(); run.AttackerLog.AddRange(r.AttackerLog); run.Breached = r.Breached; }
        }
        if (run.Net != null)
        {
            foreach (var s in r.Seen) run.Net.Seen[s.Ip] = s;
            foreach (var w in r.Loot) if (!run.Net.Worked.Any(x => x.Host == w.Host && x.User == w.User && x.Secret == w.Secret)) run.Net.Worked.Add(w);
        }
    }
}
