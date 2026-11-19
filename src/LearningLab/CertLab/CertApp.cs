// Cert Lab (Learning Lab) - what App Hub's MainView held for every page: the courses, who is learning (profiles.json),
// the current course and its progress, the practice machines and the exam on screen, the search index, the status line,
// reading aloud (ReadAloud over Platform.Speech: PlatformSpeaker), and keeping work when the app closes (resume.json:
// an exam in progress, or the practice machines / a lab / a try-it, offered back the next time Cert Lab opens).
// One CertApp lives while Cert Lab is open; Quiet = the checks and pictures (in-memory clipboard, FakeSpeaker, seed 1).
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class CertApp
{
    public static CertApp Current { get; private set; }

    public readonly string DataDir;
    public readonly bool Quiet;
    public List<CourseInfo> Courses = new();
    public CourseInfo Course;
    public ProfileStore Store;
    public Profile Me;
    /// <summary>The practice machines, lab or try-it on screen (kept for resume), or null.</summary>
    public SimPage Practice;
    /// <summary>The exam being taken, or null.</summary>
    public ExamRunPage ExamRun;
    public SpeechSettings Speech;
    public ReadAloud Reader;
    ISpeaker speaker;
    readonly Dictionary<string, Pronounce> pronounce = new();
    SearchIndex searchIndex;

    /// <summary>The last sentence for the person (the status line of the page on show).</summary>
    public string StatusText { get; private set; } = "";
    public event Action<string> StatusChanged;

    /// <summary>Opens Cert Lab's data. quiet: checks and pictures (nothing reaches the device).</summary>
    public static CertApp Open(bool quiet)
    {
        if (Current != null) return Current;
        Current = new CertApp(CertLabData.Dir, quiet);
        return Current;
    }
    /// <summary>The checks: a fresh one (after deleting files).</summary>
    public static CertApp Fresh(bool quiet) { Current = new CertApp(CertLabData.Dir, quiet); return Current; }

    CertApp(string dataDir, bool quiet)
    {
        DataDir = dataDir; Quiet = quiet;
        if (quiet) ClipText.UseMemory();
        else
        {
            if (!(ClipText.Current is AppClipboard)) ClipText.Current = new AppClipboard(() => Avalonia.Controls.TopLevel.GetTopLevel(MainView.Current));
            try { CertLabData.BringAcrossFromAppHub(); } catch (Exception) { }
        }
        CertTerminal.QuietRun = quiet;
        CertUi.Copied = Status;
        CertUi.OnError = Status;
        ReadDoc.ParentOf = o => (o as Avalonia.Controls.Control)?.Parent;
        string root = CertLabContent.Root;
        Courses = CourseLoader.LoadAll(CertLabContent.CoursesDir);
        Man.Dir = Path.Combine(root, "Courses", "rhcsa", "man");
        PsHelp.Dir = Path.Combine(root, "Courses", "powershell", "help");
        Store = ProfileStore.Load(CertLabData.ProfilesFile, false);
        Course = Courses.FirstOrDefault(c => c.Id == Store.LastCourse) ?? Courses.FirstOrDefault();
        Me = Store.Get(Store.LastProfile) ?? Store.Profiles.FirstOrDefault();
        Speech = SpeechSettings.Load(CertLabData.SpeechFile, false);
        if (!quiet) MainView.AppClosing += () => { try { KeepWork(); } catch (Exception) { } };
        if (Store.LoadProblem != "") Status(Store.LoadProblem);
    }

    public void Status(string s) { StatusText = s ?? ""; StatusChanged?.Invoke(StatusText); }

    public CourseProgress Progress => Me == null || Course == null ? new CourseProgress() : Me.For(Course.Id);
    public CourseProgress ProgressFor(CourseInfo c) => Me == null || c == null ? new CourseProgress() : Me.For(c.Id);
    public void SaveProgress() { Store.Save(); if (Store.SaveProblem != "") Status(Store.SaveProblem); }

    public void Choose(Profile p) { Me = p; Store.LastProfile = p.Name; SaveProgress(); }

    public Profile AddPerson(string name, out string problem)
    {
        problem = "";
        name = (name ?? "").Trim();
        if (name.Length == 0) { problem = "Type a name first."; return null; }
        if (Store.Get(name) != null) { problem = name + " is already here."; return null; }
        var p = new Profile { Name = name, Created = DateTime.Now.Ticks };
        Store.Profiles.Add(p);
        Choose(p);
        return p;
    }

    public void SetCourse(CourseInfo c)
    {
        if (c == null || c == Course) return;
        Course = c;
        Store.LastCourse = c.Id;
        Practice = null;
        SaveProgress();
    }

    /// <summary>Makes the course a lab, exam or PBQ set belongs to the current one (search, progress).</summary>
    public void EnsureCourseOf(TaskSet set)
    {
        bool Has(CourseInfo x) => x != null && (x.Exams.Contains(set) || x.Labs.Contains(set) || x.Practice.Contains(set));
        if (set == null || Has(Course)) return;
        var c = Courses.FirstOrDefault(Has);
        if (c != null) { var keep = ExamRun; SetCourse(c); ExamRun = keep; }
    }

    // ------------------------------------------------------------ search
    public SearchIndex SearchIdx
    {
        get
        {
            if (Course == null) return null;
            if (searchIndex == null || searchIndex.CourseId != Course.Id) searchIndex = SearchIndex.Build(Course);
            return searchIndex;
        }
    }
    public HashSet<string> TakenExams => new(Progress.Exams.Select(a => a.Exam));

    // ------------------------------------------------------------ reading aloud
    /// <summary>The checks: the speaker that would have spoken (never plays anything).</summary>
    public FakeSpeaker FakeVoice => speaker as FakeSpeaker;

    public ReadAloud EnsureReader()
    {
        if (Reader != null) return Reader;
        speaker = Quiet || Services.Platform.Speech == null ? new FakeSpeaker() : new PlatformSpeaker();
        if (!Quiet && Services.Platform.Speech == null) Status("This device has no voice to read with.");
        Reader = new ReadAloud(speaker) { Rate = Speech.Rate };
        return Reader;
    }
    public void SetRate(int rate) { Speech.Rate = Math.Max(-5, Math.Min(5, rate)); if (Reader != null) Reader.Rate = Speech.Rate; Speech.Save(); }
    public void SetReadCode(bool on) { Speech.ReadCode = on; Speech.Save(); }

    /// <summary>How the current course says its tech words (Courses\<course>\speech.txt, plus its acronym decks).</summary>
    public Pronounce PronounceFor(CourseInfo c)
    {
        string id = c == null ? "" : c.Id;
        if (pronounce.TryGetValue(id, out var p)) return p;
        p = c == null ? new Pronounce() : Pronounce.Load(Path.Combine(c.Dir, "speech.txt"));
        if (c != null) p.AddAcronyms(c.Decks.Where(d => d.Id.StartsWith("acronyms", StringComparison.OrdinalIgnoreCase)).SelectMany(d => d.Cards));
        pronounce[id] = p;
        return p;
    }

    // ------------------------------------------------------------ keeping work (resume.json)
    public string ResumeFile => CertResume.FileIn(DataDir);
    ExamRunPage ActiveExam => ExamRun != null && !ExamRun.IsFinished ? ExamRun : null;

    static string KeepMachines(ISim s)
    {
        if (s == null || !s.CanSave) return "";
        try { return s.Save() ?? ""; } catch (Exception) { return ""; }
    }

    /// <summary>Keeps where the person is in resume.json (the app closing, or leaving an exam): an exam in progress, or
    /// the practice machines / lab / try-it on screen. Returns what was kept ("exam", "lab", ..., "" for nothing).</summary>
    public string KeepWork()
    {
        CertResume r = null;
        var ex = ActiveExam;
        if (ex != null)
        {
            r = new CertResume { Kind = "exam", Set = ex.Exam.Id, Title = ex.Exam.Title, Seed = ex.Seed, Minutes = ex.Minutes };
            r.SecondsLeft = Math.Max(0, (long)(ex.Ends - DateTime.Now).TotalSeconds);
            foreach (var kv in ex.Answers) r.Answers[kv.Key] = kv.Value;
            foreach (var kv in ex.More) r.More[kv.Key] = kv.Value == null ? new List<string>() : new List<string>(kv.Value);
            r.Flags.AddRange(ex.Flags);
            r.CurrentItem = ex.CurrentItem;
            r.Machines = KeepMachines(ex.Sim);
        }
        else if (Practice != null && Practice.Open)
        {
            var p = Practice;
            r = new CertResume { Kind = p.Mode ?? "practice", Seed = p.InstanceSeed, MachineCount = p.MachineCount };
            if (r.Kind == "try") { r.Set = p.FromLesson?.Id ?? ""; r.Title = p.FromLesson?.Title ?? ""; }
            else if (p.Tasks != null) { r.Set = p.Tasks.Id; r.Title = p.Tasks.Title; }
            r.Machines = KeepMachines(p.Sim);
            if (r.Kind == "practice" && r.Machines.Length == 0) r = null;
            if (r != null && r.Kind == "try" && r.Set.Length == 0) r = null;
        }
        Store?.Save();
        if (r == null) return "";
        r.Profile = Me?.Name ?? "";
        r.Course = Course?.Id ?? "";
        r.Saved = DateTime.Now.Ticks;
        r.Save(ResumeFile);
        return r.Kind;
    }

    CertResume PendingResume(out CourseInfo course, out string problem)
    {
        course = null;
        var r = CertResume.Load(ResumeFile, out problem);
        if (r == null || !r.Pending) return null;
        string cid = r.Course;
        course = Courses.FirstOrDefault(c => c.Id == cid);
        if (course == null) return null;
        string set = r.Set;
        if (r.Kind == "exam" && !course.Exams.Any(e => e.Id == set)) return null;
        if (r.Kind == "lab" && !course.Labs.Any(e => e.Id == set) && !course.Practice.Any(e => e.Id == set)) return null;
        if (r.Kind == "try") { var l = course.Lesson(set); if (l == null || l.Try == null) return null; }
        if (r.Kind != "exam" && r.Kind != "lab" && r.Kind != "try" && r.Kind != "practice") return null;
        return r;
    }

    /// <summary>The banner's sentence when there is something to carry on with, else "".</summary>
    public string ResumeOffer()
    {
        if (ActiveExam != null || (Practice != null && Practice.Open)) return "";   // still open: nothing to bring back
        var r = PendingResume(out var c, out var problem);
        if (problem.Length > 0) Status(problem);
        if (r == null) return "";
        string title = r.Title.Length > 0 ? " (" + r.Title + ")" : "";
        bool kept = r.Machines.Length > 0;
        if (r.Kind == "exam")
        {
            var t = c.Exams.First(e => e.Id == r.Set);
            bool handsOn = t.Items.Any(i => !i.IsQuestion);
            return "You were taking a practice exam" + title + ". Your answers and the time that was left (" + CertResume.Clock(r.SecondsLeft) + ") are kept, and the clock waits until you carry on." +
                (!handsOn ? "" : kept ? " The machines are kept too." : " The practice machines start again from the beginning, so do the hands-on tasks again.");
        }
        string what = r.Kind == "lab" ? "doing a lab" + title : r.Kind == "try" ? "trying something from a lesson" + title : "using the practice machines";
        return "Last time you were " + what + "." + (kept ? " The machines are kept as they were, so you can carry on." : " You can open it again; the machines start again from the beginning.");
    }

    /// <summary>"Carry on where you left off": opens what KeepWork kept (pushing its page). A sentence for the status line.</summary>
    public string ResumeNow(MainView mv)
    {
        var r = PendingResume(out var c, out var problem);
        if (r == null) return problem.Length > 0 ? problem : "There is nothing to carry on with.";
        var who = Store.Get(r.Profile);
        if (who != null && who != Me) Choose(who);
        if (Me == null) return "Choose who is learning first.";
        SetCourse(c);
        ISim sim = null;
        bool lost = false;
        if (r.Machines.Length > 0)
        {
            try { var s = RhelSim.Load(r.Machines); if (s.MachineNames.Count > 0) sim = s; else { s.Dispose(); lost = true; } }
            catch (Exception) { sim = null; lost = true; }
        }
        string said;
        if (r.Kind == "exam")
        {
            var t = c.Exams.First(e => e.Id == r.Set);
            var page = new ExamRunPage(this, t, r.Seed, r.Minutes > 0 ? r.Minutes : c.ExamMinutes, sim);
            page.RestoreAnswers(r.Answers, r.More, r.Flags, r.CurrentItem);
            page.Ends = DateTime.Now.AddSeconds(r.SecondsLeft);
            ExamRun = page;
            mv.Push(page);
            said = "Your exam is back with your answers. The clock goes on from " + CertResume.Clock(r.SecondsLeft) + ".";
        }
        else
        {
            SimPage sp;
            if (r.Kind == "lab")
            {
                var set = c.Labs.FirstOrDefault(e => e.Id == r.Set) ?? c.Practice.First(e => e.Id == r.Set);
                sp = new SimPage(this, set.Instance(r.Seed), "lab") { InstanceSeed = r.Seed };
            }
            else if (r.Kind == "try")
            {
                var l = c.Lesson(r.Set);
                sp = new SimPage(this, l.Try.Instance(1), "try") { FromLesson = l };
            }
            else sp = new SimPage(this, null, "practice");
            mv.Push(sp);
            if (sim != null) sp.Adopt(sim);
            said = sim != null ? "The machines are back as they were. The consoles start at a fresh login." : "It is open again; the machines start from the beginning.";
        }
        if (lost) said += " The machines from before couldn't be brought back, so they start again from the beginning.";
        r.Used = true;
        try { r.Save(ResumeFile); } catch (Exception) { }
        Status(said);
        return said;
    }

    /// <summary>"Not now" on the banner: the offer goes away (the file is kept, marked used).</summary>
    public void DismissResume()
    {
        var r = CertResume.Load(ResumeFile, out _);
        if (r == null || !r.Pending) return;
        r.Used = true;
        try { r.Save(ResumeFile); } catch (Exception) { }
    }
}

/// <summary>ReadAloud's speaker on the device's voice (Platform.Speech). The device doesn't say when it finished, so
/// each sentence is given the time it takes at that speed (about 14 letters a second), then the next one starts.</summary>
public sealed class PlatformSpeaker : ISpeaker
{
    readonly Avalonia.Threading.DispatcherTimer timer = new();
    int current;
    string currentText = "";
    DateTime ends; TimeSpan left;
    public event Action<int> Done;
    public int Rate { get; set; }
    public string Voice { get; set; } = "";
    public List<string> Voices() => new() { "The device's voice" };

    public PlatformSpeaker()
    {
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (s, e) =>
        {
            if (current == 0 || DateTime.Now < ends) return;
            int id = current; current = 0; timer.Stop();
            Done?.Invoke(id);
        };
    }
    double Speed => Math.Max(0.5, 1 + Rate * 0.1);
    public void Speak(int id, string text)
    {
        current = id; currentText = text ?? "";
        TimeSpan len;
        if (ReadAloud.IsPause(currentText, out int ms)) len = TimeSpan.FromMilliseconds(ms);
        else
        {
            try { Services.Platform.Speech?.Speak(currentText, "en", Speed); } catch (Exception) { }
            len = TimeSpan.FromSeconds(0.4 + currentText.Length / (14.0 * Speed));
        }
        ends = DateTime.Now + len;
        timer.Start();
    }
    public void Pause()
    {
        if (current == 0) return;
        left = ends - DateTime.Now; timer.Stop();
        try { Services.Platform.Speech?.Stop(); } catch (Exception) { }
    }
    public void Resume()
    {
        if (current == 0) return;
        // the device can't go on mid-sentence: the sentence is said again
        Speak(current, currentText);
    }
    public void Stop()
    {
        current = 0; timer.Stop();
        try { Services.Platform.Speech?.Stop(); } catch (Exception) { }
    }
}
