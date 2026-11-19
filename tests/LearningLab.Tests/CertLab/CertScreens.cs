// Cert Lab - the screens, drawn off screen with Avalonia's headless platform (group "certlab-screens"): the home page and
// each course, a lesson read aloud and its try-it passed through the terminal, the terminal itself (Tab, history,
// Ctrl+C, copy and paste, pastes that ask, GRUB by keys to rd.break, vi and nano), a lab passed with its solution typed
// into the terminals, practice exams to their results (hands-on tasks, one answer, several, order, yes / no, a case study,
// PBQs; one item at a time with flags and review), search with exam secrets hidden until the exam is taken, flash cards,
// cheat sheets, PBQ practice, keeping an exam and a lab and bringing them back, and the pages phone-sized. Also the
// screen parts of App Hub's clip, speech, search, grub, resume, sec-ui and sec-window checks that apply here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CertLab;
using LearningLab.CertLab;
using LearningLab.Views;

namespace LearningLab.Tests;

static class CertScreens
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("certlab screens: " + what, ok, detail);
    static Window win;
    static MainView mv;
    static CertApp A;
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);
    static string Tail(string s, int n = 300) => s == null ? "" : s.Substring(Math.Max(0, s.Length - n));
    static MemoryClipboard Mem => ClipText.UseMemory();

    public static void Run()
    {
        string data = Data.For("CertLab");
        foreach (var f in Directory.GetFiles(data, "*", SearchOption.AllDirectories)) File.Delete(f);
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        mv = MainView.Current;
        A = CertApp.Fresh(true);
        try
        {
            Section("home", Home);
            Section("courses", Courses);
            Section("lesson and try-it", LessonAndTry);
            Section("terminal", Terminal);
            Section("grub", Grub);
            Section("editors", Editors);
            Section("lab", Lab);
            Section("exam with tasks", ExamTasks);
            Section("exam with every question kind", ExamKinds);
            Section("exam one item at a time", ExamSequential);
            Section("search", Search);
            Section("cards and cheat sheets", CardsAndCheats);
            Section("pbq practice", PbqPractice);
            Section("resume", Resume);
            Section("phone", Phone);
        }
        finally { win.Close(); CertTerminal.ConfirmPaste = null; }
    }

    static void Section(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(name + ": ran without an error", false, e.ToString()); }
        mv.PopTo(1); Pump();
    }

    static CourseInfo Course(string id) { var c = A.Courses.First(x => x.Id == id); A.SetCourse(c); return c; }

    /// <summary>Every button on the page is at least 44 px high (touch first).</summary>
    static void Buttons44(string page)
    {
        Pump();
        var small = CertUi.Find<Button>(mv.Page.View).Where(b => b.IsEffectivelyVisible && b.Bounds.Height > 0 && b.Bounds.Height < 43.5).Select(b => CertUi.ButtonText(b) + " " + b.Bounds.Height).ToList();
        Check(page + ": every button is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(5)));
    }

    /// <summary>Types solution steps into the terminals (lines the GRUB screen or a login needs go through the simulator).</summary>
    static void TypeSteps(ISim sim, Func<string, CertTerminal> term, IEnumerable<SolutionStep> steps)
    {
        foreach (var s in steps)
        {
            var t = term(s.Machine);
            var it = sim.Term(s.Machine);
            bool special = s.Line.StartsWith('#') && !s.Line.StartsWith("#!", StringComparison.Ordinal);
            bool atLogin = it != null && (it.Grub != null || it.Prompt.EndsWith(" login: ", StringComparison.Ordinal) || it.Prompt.StartsWith("Press Enter", StringComparison.Ordinal));
            if (t == null || special || atLogin) { sim.Type(s.Machine, s.Line); t?.Poll(); }
            else t.TypeLine(s.Line);
        }
    }

    // ------------------------------------------------------------ home
    static void Home()
    {
        var hp = new HomePage();
        var entry = HomePage.Categories.SelectMany(c => c.Entries).First(e => e.Id == "cert");
        Check("the home page's Cert Lab entry opens Cert Lab", entry.Open != null && entry.Open() is CertHomePage && CertApp.Current == A);
        var home = new CertHomePage(A);
        mv.Push(home); Pump(); Snap("cert-home-empty");
        Check("the five courses load", A.Courses.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "az800", "md102", "powershell", "rhcsa", "secplus" }));
        Check("nobody yet: Add a person first", CertUi.Find<Button>(home.View).Any(b => CertUi.ButtonText(b) == "Add a person first" && !b.IsEnabled));
        var p = A.AddPerson("Theo", out var problem);
        Check("adding a person", p != null && A.Me == p && File.Exists(CertLabData.ProfilesFile), problem);
        Check("the same name twice is refused", A.AddPerson("Theo", out problem) == null && problem.Length > 0);
        home.Refresh(); Pump(); Snap("cert-home");
        Check("each course has its Open button", A.Courses.All(c => CertUi.Find<Button>(home.View).Any(b => CertUi.ButtonText(b) == "Open " + c.Short)));
        Buttons44("home");
        var about = new CertAboutPage(A); mv.Push(about); Pump(); Snap("cert-about");
        string aboutText = string.Join("\n", CertUi.Find<TextBlock>(about.View).Select(t => t.Text));
        Check("About keeps every course's disclaimer", A.Courses.Where(c => c.Disclaimer.Length > 0).All(c => aboutText.Contains(c.Disclaimer)), aboutText.Length.ToString());
    }

    static void Courses()
    {
        foreach (var c in A.Courses)
        {
            A.SetCourse(c);
            var hub = new CourseHubPage(A); mv.Push(hub); Pump(); Snap("cert-hub-" + c.Id);
            Check(c.Id + ": the course page opens with its sections", CertUi.Find<Button>(hub.View).Count() >= 7);
            var lp = new LessonsPage(A); mv.Push(lp); Pump();
            Check(c.Id + ": the lessons list every lesson", CertUi.Find<Button>(lp.View).Count() >= c.AllLessons.Count());
            mv.Back(); mv.Back(); Pump();
        }
        Course("rhcsa");
    }

    // ------------------------------------------------------------ a lesson, read aloud, and its try-it
    static void LessonAndTry()
    {
        var c = Course("rhcsa");
        var l = c.AllLessons.First(x => x.Try != null && x.Quiz.Count > 0);
        var lp = new LessonPage(A, l); mv.Push(lp); Pump(); Snap("cert-lesson");
        Check("a lesson shows its blocks, try-it and quiz", lp.QuizChoices.Count == l.Quiz.Count);
        Check("code in lessons has Copy chips", CertUi.Find<CodeCopy>(lp.View).Any());
        var chip = CertUi.Find<CodeCopy>(lp.View).First();
        chip.Copy();
        Check("a Copy chip copies its code", ClipText.GetText() == chip.Code, ClipText.GetText());
        Check("terminal blocks have a Copy button", CertUi.Find<Button>(lp.View).Any(b => CertUi.ButtonText(b) == "Copy"));
        // the quiz
        for (int i = 0; i < l.Quiz.Count; i++) lp.QuizChoices[i][l.Quiz[i].Answer].IsChecked = true;
        CertUi.Press(lp.View, "Check my answers"); Pump();
        Check("the quiz marks right answers", lp.QuizScore.Text == l.Quiz.Count + " of " + l.Quiz.Count + " right." && A.Progress.Quiz[l.Id] == 100, lp.QuizScore.Text);
        // read aloud: the bar reads the page sentence by sentence with the device voice (a fake one here)
        var bar = lp.ReadBarView;
        bar.PlayPause(); Pump();
        var fv = A.FakeVoice;
        Check("read aloud starts with the lesson's title", fv != null && fv.Spoken.Count == 1 && fv.Spoken[0].Contains(A.PronounceFor(c).Say(l.Title).Split(' ')[0]), fv == null ? "" : string.Join("|", fv.Spoken));
        fv.Finish(); fv.Finish(); Pump();
        Check("the unit being read is lit up and its sentence marked in the bar", bar.Lit != null && bar.MarkedSentence.Length > 0, bar.MarkedSentence);
        Snap("cert-lesson-reading");
        Check("code blocks are skipped unless asked for", !bar.Doc.Units.Any(u => u.Kind == "code"));
        bar.PlayPause();
        Check("pause", A.Reader.Paused && fv.Pauses > 0);
        bar.Stop();
        Check("stop", !A.Reader.Playing && bar.Lit == null);
        A.SetReadCode(true); bar.ReadFrom(0);
        Check("code blocks read when asked (Code: ...)", bar.Doc.Units.Any(u => u.Kind == "code" && u.Text.StartsWith("Code: ", StringComparison.Ordinal)) || !l.Blocks.Any(b => b.Kind == "terminal" || b.Kind == "file"));
        bar.Stop(); A.SetReadCode(false);
        // the try-it, typed into the terminal
        var sp = lp.OpenTry(); Pump(); Snap("cert-try");
        Check("the try-it opens the practice machines with its task", sp.Mode == "try" && sp.Terms.Any());
        var before = sp.CheckWork(); Pump();
        Check("the try-it fails before you do it", before != null && !before.Items.All(i => i.Pass));
        foreach (var it in sp.Tasks.Items) TypeSteps(sp.Sim, sp.Term, it.Solution);
        Pump(); Snap("cert-try-typed");
        var after = sp.CheckWork(); Pump(); Snap("cert-try-passed");
        Check("the try-it passes with its solution typed into the terminal", after != null && after.Items.All(i => i.Pass), after == null ? "" : string.Join("; ", after.Items.SelectMany(i => i.Checks).Where(x => !x.Pass).Select(x => x.Desc)));
        Check("and counts as done in the lesson", A.Progress.TriesDone.Contains(l.Id));
        Buttons44("try-it");
    }

    // ------------------------------------------------------------ the terminal
    static void Terminal()
    {
        Course("rhcsa");
        var sp = new SimPage(A, null, "practice"); mv.Push(sp); Pump();
        sp.Rebuild(1); Pump();
        var t = sp.Terms.First();
        Check("the practice machine starts at a root prompt", t.S.Prompt.StartsWith("[root@", StringComparison.Ordinal), t.S.Prompt);
        t.TypeLine("echo alpha beta");
        t.TypeLine("printf 'one\\ttwo\\n'");
        Check("typed lines run and print", t.Text.Contains("\nalpha beta\n"), Tail(t.Text));
        // typing through the text box as the keyboard does
        t.SendText("echo keyed"); t.SendKey(Key.Enter);
        Check("keys typed into the line run on Enter", t.Text.Contains("\nkeyed\n"), Tail(t.Text));
        t.SendKey(Key.Up);
        Check("Up brings back the last command", t.Typed == "echo keyed", t.Typed);
        t.Typed = "";
        // Tab
        t.Typed = "systemct"; t.Tab();
        Check("Tab completes a command", t.Typed == "systemctl ", t.Typed);
        t.Typed = "ls /et"; t.Tab();
        Check("Tab completes a path", t.Typed == "ls /etc/", t.Typed);
        t.Typed = "zzzqq"; t.Tab();
        Check("Tab with no match flashes", t.Flashes == 1 && t.Typed == "zzzqq");
        t.Typed = "ls /etc/sys"; t.Tab(); t.Tab();
        Check("Tab twice lists the matches", t.Text.Contains("sysconfig") || t.Typed.Length > 11, Tail(t.Text));
        t.Typed = "";
        // Ctrl+C
        t.TypeLine("echo 'open quote");
        Check("an open quote waits at > ", t.S.Prompt == "> ", t.S.Prompt);
        t.SendKey(Key.C, KeyModifiers.Control);
        Check("Ctrl+C at > drops the command", t.S.Prompt.StartsWith("[root@", StringComparison.Ordinal) && t.Text.Contains("^C"), t.S.Prompt);
        // copy
        t.SelectAll();
        Check("Select all selects the output", t.SelectedText.Contains("alpha beta"));
        bool copied = t.CopySelection();
        Check("Copy puts it on the clipboard", copied && (Mem.Text ?? "").Contains("alpha beta") && !t.HasSelection, copied + " [" + Mem.Text + "] " + t.HasSelection);
        int i0 = t.Output.Text.IndexOf("alpha beta", StringComparison.Ordinal);
        t.Select(i0 + 6, i0 + 10);
        Check("a selection inside a line", t.SelectedText == "beta", t.SelectedText);
        int breaks = t.Text.Split("^C").Length;
        t.SendKey(Key.C, KeyModifiers.Control);
        Check("Ctrl+C with a selection copies (and doesn't interrupt)", Mem.Text == "beta" && t.Text.Split("^C").Length == breaks, Mem.Text);
        t.Select(i0, i0 + 10);
        t.SendKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift);
        Check("Ctrl+Shift+C copies", Mem.Text == "alpha beta", Mem.Text);
        // paste
        t.Typed = "echo ";
        t.PasteText("pasted-word");
        Check("a paste goes into the line (not run yet)", t.Typed == "echo pasted-word" && !t.Text.Contains("\npasted-word\n"), t.Typed);
        t.SendKey(Key.Enter);
        Check("Enter runs the pasted line", t.Text.Contains("\npasted-word\n"), Tail(t.Text));
        t.PasteText("echo L1\r\necho L2\necho L3\necho still");
        Check("three lines run in order, the last piece stays typed", t.Text.IndexOf("\nL1\n", StringComparison.Ordinal) > 0 && t.Text.IndexOf("\nL3\n", StringComparison.Ordinal) > t.Text.IndexOf("\nL2\n", StringComparison.Ordinal) && t.Typed == "echo still", t.Typed);
        t.Typed = "";
        int asked = -1;
        CertTerminal.ConfirmPaste = (tv, n, capped) => { asked = n; return false; };
        t.PasteText("echo Q1\necho Q2\necho Q3\necho Q4\necho Q5\n");
        Check("five lines ask first; No runs nothing", asked == 5 && !t.Text.Contains("\nQ1\n"), asked.ToString());
        CertTerminal.ConfirmPaste = (tv, n, capped) => true;
        t.PasteText("echo Q1\necho Q2\necho Q3\necho Q4\necho Q5\n");
        Check("Yes: all five run", t.Text.Contains("\nQ5\n"), Tail(t.Text));
        CertTerminal.ConfirmPaste = null;
        Check("in the checks a paste that would ask is refused (no question shown)", !t.PasteText("echo z1\necho z2\necho z3\necho z4\n") && !mv.MessageOpen);
        t.PasteText("echo before-vi\nvi /root/pasted.txt\necho after-vi\n");
        Pump();
        Check("a paste stops when an editor opens", t.Mode == "vim" && !t.Text.Contains("\nafter-vi\n") && t.Text.Contains("not typed because an editor opened"), t.Mode);
        t.SendText(":q"); t.SendKey(Key.Enter); Pump();
        Check("vi closes again", t.Mode == "shell", t.Mode);
        t.TypeLine("passwd student");
        t.PasteText("Paste-pass-77\nPaste-pass-77\n");
        Check("pasted lines answer prompts (passwd); the password isn't shown", t.Text.Contains("all authentication tokens updated successfully") && !t.Text.Contains("Paste-pass-77"), Tail(t.Text));
        // the key row
        Check("the shell's key row: Tab, Ctrl+C, Ctrl+D, Copy, Paste", new[] { "Tab", "Ctrl+C", "Ctrl+D", "Copy", "Paste" }.All(k => CertUi.Find<Button>(t).Any(b => CertUi.ButtonText(b) == k)));
        t.TypeLine("cat > /root/eof.txt"); t.TypeLine("line one");
        CertUi.Press(t, "Ctrl+D"); Pump();
        Check("Ctrl+D ends input (cat > file)", t.S.Prompt.StartsWith("[root@", StringComparison.Ordinal) && sp.Sim.Check("server1", "grep -q 'line one' /root/eof.txt", out _), t.S.Prompt);
        Pump(); Snap("cert-terminal");
        // skip time, reset, save and open saved
        string clock = sp.Sim.Clock;
        sp.Skip(86400);
        Check("Skip 1 day moves the machines' clock", sp.Sim.Clock != clock, sp.Sim.Clock);
        sp.SaveMachines();
        Check("Save keeps the machines (machines-Theo.json)", File.Exists(CertLabData.MachinesFile(A.DataDir, "Theo")));
        sp.Terms.First().TypeLine("touch /root/after-save");
        sp.LoadMachines(); Pump();
        Check("Open saved brings them back as saved", sp.Sim.Check("server1", "test -f /root/eof.txt && ! test -f /root/after-save", out var o2), o2);
        Buttons44("practice machines");
    }

    // ------------------------------------------------------------ GRUB by keys to rd.break
    static void Grub()
    {
        Course("rhcsa");
        var sp = new SimPage(A, null, "practice"); mv.Push(sp); Pump();
        sp.Rebuild(1); Pump();
        var t = sp.Terms.First();
        t.TypeLine("systemctl reboot"); Pump();
        Check("a reboot shows the GRUB menu over the terminal", t.GrubShown && t.Mode == "grub", t.Mode);
        Snap("cert-grub");
        Check("GRUB's key row: arrows, e, Ctrl+X, Esc and the type box", new[] { "↑", "↓", "e", "Ctrl+X", "Esc" }.All(k => CertUi.Find<Button>(t).Any(b => CertUi.ButtonText(b) == k)));
        Check("arrows go to the menu", t.SendKey(Key.Down) && t.S.Grub.Sel == 1);
        t.SendKey(Key.Up);
        Check("Esc stays on the GRUB screen (it isn't Back)", t.SendKey(Key.Escape) && mv.Page == sp);
        t.SendText("e");
        for (int i = 0; i < 3; i++) t.SendKey(Key.Down);
        t.SendKey(Key.End);
        t.SendText(" rd.break");
        Pump(); Snap("cert-grub-edit");
        Check("the entry editor shows what was typed", t.S.Grub.Mode == "edit" && t.S.Grub.Lines.Any(x => x.EndsWith(" rd.break", StringComparison.Ordinal)), string.Join(" | ", t.S.Grub.Lines));
        Check("Tab is kept on the GRUB screen", t.SendKey(Key.Tab) && t.S.Grub.Mode == "edit");
        t.SendKey(Key.X, KeyModifiers.Control); Pump();
        var m = ((RhelSim)sp.Sim).M("server1");
        Check("Ctrl+X boots the edited entry into rd.break", m.State == "rdbreak" && !t.GrubShown && m.Cmdline.EndsWith("rd.break", StringComparison.Ordinal), m.State + " " + m.Cmdline);
        Check("the terminal is back at the rd.break prompt", t.S.Prompt.StartsWith("Press Enter for maintenance", StringComparison.Ordinal) && t.Mode == "shell", t.S.Prompt);
        // the type box (a phone's keyboard) and pasting into GRUB
        t.TypeLine(""); t.TypeLine("exit"); Pump();
        sp.Sim.Reboot("server1"); t.Poll(); Pump();
        if (t.GrubShown)
        {
            Check("a paste on the GRUB menu does nothing (letters are commands there)", !t.PasteText("e") && t.S.Grub.Mode == "menu");
            t.TextToScreen("e");
            t.KeyByName("Down"); t.KeyByName("Down"); t.KeyByName("Down"); t.KeyByName("End");
            Check("the key row's keys and the type box drive GRUB", t.S.Grub.Mode == "edit");
            Check("a paste types at the cursor in the entry editor", t.PasteText(" init=/bin/bash") && t.S.Grub.Lines.Any(x => x.EndsWith("init=/bin/bash", StringComparison.Ordinal)));
            t.KeyByName("Esc"); t.KeyByName("Enter"); t.Poll(); Pump();
            Check("Esc throws the edit away, Enter boots normally", !t.GrubShown && m.State == "running" && !m.Cmdline.Contains("init=/bin/bash"), m.State + " " + m.Cmdline);
        }
        else Check("a second reboot shows GRUB", false);
    }

    // ------------------------------------------------------------ vi and nano
    static void Editors()
    {
        Course("rhcsa");
        var sp = new SimPage(A, null, "practice"); mv.Push(sp); Pump();
        sp.Rebuild(1); Pump();
        var t = sp.Terms.First();
        t.TypeLine("vi /root/notes.txt"); Pump();
        Check("vi opens over the terminal", t.Mode == "vim" && t.Vim != null, t.Mode);
        t.SendText("i"); t.SendText("hello from vi");
        Check("Esc goes to vi, not Back", t.SendKey(Key.Escape) && mv.Page == sp && t.Vim.Mode == "normal");
        Pump(); Snap("cert-vim");
        t.SendText(":wq"); t.SendKey(Key.Enter); Pump();
        Check("vi saves (:wq) and closes", t.Mode == "shell" && sp.Sim.Check("server1", "grep -q 'hello from vi' /root/notes.txt", out _), t.Mode);
        t.TypeLine("vi /root/notes.txt"); Pump();
        t.TextToScreen("A"); t.TextToScreen(" and the type box"); t.KeyByName("Esc"); t.TextToScreen(":wq"); t.KeyByName("Enter"); Pump();
        Check("the type box and key row edit in vi (a phone's keyboard)", sp.Sim.Check("server1", "grep -q 'hello from vi and the type box' /root/notes.txt", out var o), o);
        t.TypeLine("vi /root/clip.txt"); Pump();
        t.SendText("i"); t.PasteText("aX\tY\nZ"); t.SendKey(Key.Escape); t.SendText(":wq"); t.SendKey(Key.Enter); Pump();
        Check("a paste in vi's insert mode types at the cursor, lines and tabs kept", sp.Sim.Check("server1", "test \"$(sed -n 1p /root/clip.txt)\" = \"$(printf 'aX\\tY')\" && grep -qx Z /root/clip.txt", out o), o);
        // nano
        t.TypeLine("nano /root/nano.txt"); Pump();
        Check("nano opens over the terminal", t.Mode == "nano" && t.Nano != null, t.Mode);
        t.Nano.Text = "first line\nsecond line";
        Pump(); Snap("cert-nano");
        t.SendKey(Key.O, KeyModifiers.Control);
        t.SendKey(Key.X, KeyModifiers.Control); Pump();
        Check("nano saves with Ctrl+O and leaves with Ctrl+X", t.Mode == "shell" && sp.Sim.Check("server1", "grep -q 'second line' /root/nano.txt", out o), t.Mode + " " + o);
        t.TypeLine("nano /root/nano.txt"); Pump();
        t.Nano.Text = "changed";
        t.SendKey(Key.X, KeyModifiers.Control); Pump();
        Check("Exit with changes asks to save (Yes / No / Cancel buttons)", t.Mode == "nano" && CertUi.Find<Button>(t).Any(b => CertUi.ButtonText(b) == "Yes, save"));
        CertUi.Press(t, "No"); Pump();
        Check("No leaves the file as it was", t.Mode == "shell" && sp.Sim.Check("server1", "grep -q 'second line' /root/nano.txt", out o), o);
    }

    // ------------------------------------------------------------ a lab, typed into the terminals
    static void Lab()
    {
        var c = Course("rhcsa");
        var lab = c.Labs.First(l => l.Items.SelectMany(i => i.Solution).All(s => !s.Line.StartsWith('#')) && l.Machines >= 1);
        var labs = new LabsPage(A); mv.Push(labs); Pump(); Snap("cert-labs");
        var sp = LabsPage.StartLab(A, lab); Pump(); Snap("cert-lab");
        var before = sp.CheckWork();
        Check("the lab fails at the start", before != null && !before.Items.All(i => i.Pass));
        CertUi.Press(sp.View, "Show a hint"); CertUi.Press(sp.View, "Show the solution"); Pump();
        Check("hints and the solution show on request", CertUi.Find<TextBlock>(sp.View).Any(x => (x.Text ?? "").StartsWith("Hint: ", StringComparison.Ordinal)) || lab.Items.All(i => i.Hints.Count == 0));
        foreach (var it in sp.Tasks.Items) TypeSteps(sp.Sim, sp.Term, it.Solution);
        var after = sp.CheckWork(); Pump(); Snap("cert-lab-passed");
        Check("the lab passes with its solution typed into the terminals: " + lab.Id, after != null && after.Items.All(i => i.Pass), after == null ? "" : string.Join("; ", after.Items.SelectMany(i => i.Checks).Where(x => !x.Pass).Select(x => x.Desc)));
        Check("the lab's best score is saved", A.Progress.Labs.TryGetValue(lab.Id, out int best) && best == 100);
    }

    // ------------------------------------------------------------ exams
    static ExamRunPage StartExam(string course, int index)
    {
        var c = Course(course);
        var ep = new ExamsPage(A); mv.Push(ep); Pump();
        return ep.Start(c.Exams[index]);
    }

    static void AnswerAll(ExamRunPage run)
    {
        var choice = new Dictionary<string, int>(); var more = new Dictionary<string, List<string>>();
        TaskRunner.ModelAnswers(run.Exam, choice, more);
        foreach (var it in run.Exam.Items.Where(i => i.IsQuestion))
        {
            if (it.Kind == "choice") run.SetAnswer(it, new List<string> { choice[it.Id].ToString() });
            else run.SetAnswer(it, more[it.Id]);
        }
    }

    static void ExamTasks()
    {
        var run = StartExam("powershell", 0); Pump(); Snap("cert-exam-ps");
        Check("a hands-on exam shows the tasks and the consoles", run.Terms.Any() && !run.Sequential);
        var c = A.Course;
        foreach (var it in run.Exam.Items.Where(i => !i.IsQuestion)) TypeSteps(run.Sim, m => run.Consoles?.Term(m) ?? run.Terms.FirstOrDefault(), it.Solution);
        AnswerAll(run);
        Pump(); Snap("cert-exam-ps-done");
        run.Finish(false); Pump();
        var res = mv.Page as ExamResultsPage;
        Snap("cert-exam-ps-results");
        Check("the PowerShell exam's tasks typed into the consoles score full marks", res != null && res.Report.Score == res.Report.Max, res == null ? "" : res.Report.Score + "/" + res.Report.Max + " " + string.Join("; ", res.Report.Items.Where(i => !i.Pass).Select(i => i.Item.Id)));
        Check("the attempt is saved", A.Progress.Exams.Any(e => e.Exam == c.Exams[0].Id && e.Passed));
    }

    static void ExamKinds()
    {
        var run = StartExam("md102", 0); Pump(); Snap("cert-exam-md102");
        var kinds = run.Exam.Items.Select(i => i.Kind).Distinct().ToList();
        Check("the MD-102 exam has tasks, one answer, several, order, yes / no and a case study", new[] { "task", "choice", "multi", "order", "yesno" }.All(kinds.Contains) && run.Exam.Cases.Count > 0, string.Join(",", kinds));
        Check("the MD-102 exam shows the practice portal", run.Workspace is global::LearningLab.CertLab.Portal.EpWorkspace, run.Workspace?.GetType().Name ?? "none");
        TaskRunner.TypeSolutions(run.Sim, new TaskSet { Items = run.Exam.Items.Where(i => !i.IsQuestion).ToList() });
        // answers by tapping: the order question through its buttons, the rest through the controls
        var order = run.Exam.Items.First(i => i.Kind == "order");
        var ov = run.ItemViews[order.Id];
        foreach (var a in order.OrderAnswer) CertUi.Press(ov, "+ " + a.Replace("`", ""));
        Check("put in order: tapping actions fills the answer area in order", run.More[order.Id].SequenceEqual(order.OrderAnswer), string.Join(" | ", run.More[order.Id]));
        AnswerAll(run);
        var multi = run.Exam.Items.First(i => i.Kind == "multi");
        Check("several answers: the check boxes", run.More[multi.Id].Count == multi.Answers.Count);
        Pump(); Snap("cert-exam-md102-answered");
        var rep = run.Finish(false); Pump(); Snap("cert-exam-md102-results");
        Check("every question kind graded right with the model answers (and the tasks done)", rep != null && rep.Score == rep.Max, rep == null ? "" : rep.Score + "/" + rep.Max + " " + string.Join("; ", rep.Items.Where(i => !i.Pass).Select(i => i.Item.Id + " " + i.Item.Kind)));
        var res = mv.Page as ExamResultsPage;
        Check("the results page: score on the 1000 scale, pass", res != null && res.Attempt.Scaled == 1000 && res.Attempt.Passed);
        Buttons44("results");
    }

    static void ExamSequential()
    {
        var run = StartExam("secplus", 0); Pump(); Snap("cert-exam-seq");
        Check("questions only: one item at a time, PBQs first", run.Sequential && run.Exam.Items[0].Pbq != null);
        Check("a PBQ shows its view", CertUi.Find<PbqView>(run.ItemViews[run.Exam.Items[0].Id]).Any());
        run.ToggleFlag();
        Check("Flag for review", run.Flags.Contains(run.Exam.Items[0].Id));
        AnswerAll(run);
        run.Go(1); Pump();
        Check("answered items count as answered", run.Exam.Items.All(run.Answered), run.Exam.Items.Count(i => !run.Answered(i)) + " not answered");
        var choiceItem = run.Exam.Items.First(i => i.Kind == "choice");
        run.Go(run.Exam.Items.IndexOf(choiceItem)); Pump(); Snap("cert-exam-seq-choice");
        run.ShowReview(); Pump(); Snap("cert-exam-seq-review");
        Check("the review lists every item and the flag", run.ReviewShown);
        CertUi.Press(run.View, "Submit the exam"); CertUi.Press(run.View, "Yes, submit the exam"); Pump();
        var res = mv.Page as ExamResultsPage;
        Snap("cert-exam-seq-results");
        Check("Security+: 900 of 900 with the model answers (PBQs included)", res != null && res.Attempt.Scaled == 900, res == null ? "no results" : res.Attempt.Scaled.ToString());
    }

    // ------------------------------------------------------------ search
    static void Search()
    {
        var c = Course("rhcsa");
        foreach (var e in A.Me.For(c.Id).Exams.ToList()) A.Me.For(c.Id).Exams.Remove(e);
        var page = new SearchPage(A, "rd.break"); mv.Push(page); Pump(); Snap("cert-search");
        Check("search finds rd.break in the course", page.Hits.Count > 0);
        Check("exam secrets stay hidden before the exam is taken", !page.Hits.Any(h => h.Doc.Secret));
        var secret = A.SearchIdx.Docs.FirstOrDefault(d => d.Secret);
        if (secret != null)
        {
            A.Progress.Exams.Add(new ExamAttempt { Exam = secret.SetId, When = DateTime.Now.Ticks });
            var withSecret = A.SearchIdx.Find(secret.Title.Length > 2 ? secret.Title : "rd.break", SearchIndex.Allows(A.TakenExams), 300);
            Check("an exam's explanations show once it is taken", SearchIndex.Allows(A.TakenExams)(secret));
            var review = new TaskSetReviewPage(A, c.Exams.First(x => x.Id == secret.SetId), true, true, "");
            var hidden = new TaskSetReviewPage(A, c.Exams.First(x => x.Id == secret.SetId), true, false, "");
            Check("the exam's review page shows model solutions only when taken", review.AllText.Length > hidden.AllText.Length);
            A.Progress.Exams.RemoveAll(e => e.Exam == secret.SetId);
        }
        var lessonHit = page.Hits.First(h => h.Doc.Kind == "lesson" || h.Doc.Kind == "example");
        RevealUi.LastRevealed = null;
        var opened = page.Open(lessonHit.Doc); Pump(); Snap("cert-search-open");
        Check("opening a result shows the lesson there, lit up", opened is LessonPage && RevealUi.LastRevealed != null);
        mv.Back(); Pump();
        var manHit = page.Hits.FirstOrDefault(h => h.Doc.Kind == "man") ?? A.SearchIdx.Find("fstab", SearchIndex.Allows(A.TakenExams), 300).FirstOrDefault(h => h.Doc.Kind == "man");
        if (manHit != null) { var mp = page.Open(manHit.Doc); Pump(); Snap("cert-man"); Check("a manual page opens at its section", mp is ManViewPage && RevealUi.LastRevealed != null, manHit.Doc.Man + " " + mp); mv.Back(); }
        var cardHit = A.SearchIdx.Find("tar", SearchIndex.Allows(A.TakenExams), 300).FirstOrDefault(h => h.Doc.Kind == "card");
        if (cardHit != null) { var cp = page.Open(cardHit.Doc) as CardsPage; Check("a card result opens the card", cp != null && cp.CurrentCard != null && cp.CurrentCard.Id == cardHit.Doc.CardId); mv.Back(); }
        var cheatHit = A.SearchIdx.Find("mount", SearchIndex.Allows(A.TakenExams), 300).FirstOrDefault(h => h.Doc.Kind == "cheat");
        if (cheatHit != null) { var ch = page.Open(cheatHit.Doc); Check("a cheat sheet result lights its row", ch is CheatPage && RevealUi.LastRevealed != null); mv.Back(); }
        Pump();
    }

    // ------------------------------------------------------------ flash cards and cheat sheets
    static void CardsAndCheats()
    {
        var c = Course("rhcsa");
        var cp = new CardsPage(A); mv.Push(cp); Pump(); Snap("cert-cards");
        var deck = c.Decks.First();
        cp.Start(deck, false); Pump(); Snap("cert-card");
        var card = cp.CurrentCard;
        // read aloud: the front, a pause, then the back (which shows itself)
        cp.ReadBarView.PlayPause();
        var fv = A.FakeVoice;
        int guard = 0;
        while (!cp.Revealed && guard++ < 30) fv.Finish();
        Check("reading a card aloud: the front, a moment, then the back shows", cp.Revealed && fv.Spoken.Any(s => s.StartsWith("[pause", StringComparison.Ordinal)), string.Join(" | ", fv.Spoken.TakeLast(4)));
        cp.ReadBarView.Stop();
        Pump(); Snap("cert-card-back");
        CertUi.Press(cp.View, "I knew it"); Pump();
        Check("I knew it moves the card up a box", A.Progress.Cards.TryGetValue(card.Id, out var st) && st.Box == 2);
        cp.Start(deck, true); Pump();
        var c2 = cp.CurrentCard;
        cp.AnswerBox.Text = c2.Command ?? c2.Back.Replace("`", "");
        cp.Reveal(); Pump(); Snap("cert-card-typed");
        Check("Type it checks the typed answer", cp.Revealed);
        mv.Back(); Pump();
        var sh = new CheatPage(A); mv.Push(sh); Pump(); Snap("cert-cheat");
        Check("the quick reference lists every sheet's rows", sh.Hits > 20, sh.Hits.ToString());
        sh.Search("mount"); Pump(); Snap("cert-cheat-search");
        Check("searching the sheets", sh.Hits > 0 && sh.Hits < 200, sh.Hits.ToString());
        CertUi.Press(sh.View, "Copy");
        Check("Copy puts the sheet on the clipboard as text", (Mem.Text ?? "").Contains("mount"));
        Buttons44("cheat sheet");
    }

    static void PbqPractice()
    {
        var c = Course("secplus");
        var list = new PbqListPage(A); mv.Push(list); Pump(); Snap("cert-pbq-list");
        foreach (var set in c.Practice)
        {
            var pp = new PbqPracticePage(A, set, null); mv.Push(pp); Pump();
            for (int i = 0; i < pp.Inst.Items.Count; i++)
            {
                pp.Go(i); Pump();
                if (i == 0) Snap("cert-pbq-" + set.Id);
                var empty = pp.Check();
                pp.View2.Load(PbqGrade.ModelAnswer(pp.Inst.Items[i].Pbq));
                var full = pp.Check();
                Check("PBQ " + set.Id + " #" + (i + 1) + ": nothing answered scores less, the model answer scores all", empty.Fraction < 1 && full.Fraction >= 0.999, empty.Fraction + " " + full.Fraction);
            }
            mv.Back(); Pump();
        }
        // a firewall table and matching by taps
        var fwSet = c.Practice.FirstOrDefault(s => s.Items.Any(i => i.Kind == "firewall"));
        if (fwSet != null)
        {
            var pp = new PbqPracticePage(A, fwSet, fwSet.Items.First(i => i.Kind == "firewall").Id); mv.Push(pp); Pump(); Snap("cert-pbq-firewall");
            Check("the firewall table: a card of drop-downs per rule", pp.View2.RuleBoxes.Count > 0 && pp.View2.RuleBoxes[0].Length == 5);
            pp.View2.RuleBoxes[0][0].SelectedItem = "DENY";
            Check("a drop-down writes the rule", pp.View2.Ans.Any(a => a.StartsWith("r:0=DENY", StringComparison.Ordinal)), string.Join(",", pp.View2.Ans));
            Buttons44("PBQ firewall");
            mv.Back(); Pump();
        }
        var mSet = c.Practice.FirstOrDefault(s => s.Items.Any(i => i.Kind == "match"));
        if (mSet != null)
        {
            var it = mSet.Items.First(i => i.Kind == "match");
            var pp = new PbqPracticePage(A, mSet, it.Id); mv.Push(pp); Pump();
            var spec = pp.Inst.Items[pp.Index].Pbq;
            var card = CertUi.Find<Button>(pp.View2).First(b => (b.Tag as string) == spec.Targets[0]);
            card.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var slot = CertUi.Find<Button>(pp.View2).First(b => (b.Tag as string) == "slot:0");
            slot.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check("matching: tap a card, then the item", PbqGrade.Get(pp.View2.Ans, "m:0") == spec.Targets[0], string.Join(",", pp.View2.Ans));
            Pump(); Snap("cert-pbq-match");
            mv.Back(); Pump();
        }
    }

    // ------------------------------------------------------------ keeping work and bringing it back
    static void Resume()
    {
        var c = Course("rhcsa");
        var lab = c.Labs.First();
        var sp = LabsPage.StartLab(A, lab); Pump();
        sp.Terms.First().TypeLine("touch /root/kept-for-later");
        Check("the app closing keeps a lab in progress", A.KeepWork() == "lab" && File.Exists(A.ResumeFile));
        // a new start of Cert Lab
        A = CertApp.Fresh(true);
        mv.PopTo(1); Pump();
        var home = new CertHomePage(A); mv.Push(home); Pump(); Snap("cert-resume-offer");
        Check("Cert Lab offers to carry on with the lab", home.Offer.Contains("doing a lab"), home.Offer);
        CertUi.Press(home.View, "Carry on where you left off"); Pump();
        var back = mv.Page as SimPage;
        Check("the lab is back with its machines as they were", back != null && back.Tasks.Id == lab.Id && back.Sim.Check("server1", "test -f /root/kept-for-later", out _));
        mv.PopTo(1); Pump();
        Check("once brought back it isn't offered again", new CertHomePage(A).Offer.Length == 0);
        // an exam: answers, flags, the item shown and the time left
        var run = StartExam("secplus", 0); Pump();
        var q = run.Exam.Items.First(i => i.Kind == "choice");
        run.SetAnswer(q, new List<string> { "1" });
        run.Go(3); run.ToggleFlag();
        run.Ends = DateTime.Now.AddMinutes(42);
        Check("leaving an exam asks, and Keep it keeps it", run.OnBack() && mv.PressMessage("Keep it and leave") && !(mv.Page is ExamRunPage));
        A = CertApp.Fresh(true);
        mv.PopTo(1); Pump();
        home = new CertHomePage(A); mv.Push(home); Pump();
        Check("Cert Lab offers to carry on with the exam", home.Offer.Contains("practice exam"), home.Offer);
        CertUi.Press(home.View, "Carry on where you left off"); Pump();
        var again = mv.Page as ExamRunPage;
        Check("the exam is back: answer, flag, item and time left", again != null && again.Answers.TryGetValue(q.Id, out int a) && a == 1 && again.Flags.Count == 1 && again.CurrentItem == 3 && (again.Ends - DateTime.Now).TotalMinutes > 40, again == null ? "" : again.CurrentItem + " " + again.Flags.Count);
        Pump(); Snap("cert-resume-exam");
        // an unreadable resume file is never written over
        mv.PopTo(1);
        foreach (var b in LearningLab.Saves.SafeFile.Backups(A.ResumeFile)) File.Delete(b);
        File.WriteAllText(A.ResumeFile, "{ not json");
        A = CertApp.Fresh(true);
        home = new CertHomePage(A);
        Check("an unreadable resume file: no offer, a sentence, the file kept aside", home.Offer.Length == 0 && A.StatusText.Length > 0 && LearningLab.Saves.SafeFile.Quarantined(A.ResumeFile).Count > 0, A.StatusText);
    }

    // ------------------------------------------------------------ phone-sized
    static void Phone()
    {
        win.Width = 400; win.Height = 840; Pump();
        try
        {
            mv.Push(new CertHomePage(A)); Pump(); Snap("cert-phone-home");
            Buttons44("phone home");
            var c = Course("rhcsa");
            mv.Push(new CourseHubPage(A)); Pump(); Snap("cert-phone-hub");
            var l = c.AllLessons.First(x => x.Try != null);
            var lp = new LessonPage(A, l); mv.Push(lp); Pump(); Snap("cert-phone-lesson");
            var sp = lp.OpenTry(); Pump(); Snap("cert-phone-try");
            Check("on a phone the tasks are behind Your task; Back closes them", !sp.PanelOpen);
            sp.SetPanel(true); Pump(); Snap("cert-phone-try-tasks");
            Check("Your task opens the panel over the machines", sp.PanelOpen && sp.OnBack() && !sp.PanelOpen);
            var t = sp.Terms.First();
            t.TypeLine("ls -l /etc | head"); Pump(); Snap("cert-phone-terminal");
            Buttons44("phone terminal");
            t.TypeLine("systemctl reboot"); Pump(); Snap("cert-phone-grub");
            Check("GRUB on a phone: the screen scrolls sideways, the key row is there", t.GrubShown);
            sp.Sim.Type("server1", "#key Enter"); t.Poll(); Pump();
            t.TypeLine("vi /root/phone.txt"); Pump(); Snap("cert-phone-vim");
            t.KeyByName("Esc"); t.TextToScreen(":q"); t.KeyByName("Enter"); Pump();
            mv.PopTo(1);
            var lsp = LabsPage.StartLab(A, c.Labs.First(x => x.Machines >= 2)); Pump(); Snap("cert-phone-lab");
            Check("two machines on a phone: one at a time with a tab each", lsp.Consoles != null && !lsp.Consoles.SideBySide);
            lsp.Consoles.Show(1); Pump(); Snap("cert-phone-lab-second");
            mv.PopTo(1);
            var run = StartExam("rhcsa", 0); Pump(); Snap("cert-phone-exam");
            run.SetMachines(true); Pump(); Snap("cert-phone-exam-machines");
            mv.PopTo(1);
            run = StartExam("secplus", 0); Pump(); Snap("cert-phone-exam-pbq");
            Buttons44("phone exam");
            run.Go(run.Exam.Items.FindIndex(i => i.Kind == "choice")); Pump(); Snap("cert-phone-exam-choice");
            mv.PopTo(1);
            run = StartExam("md102", 0); Pump(); Snap("cert-phone-exam-md102");
            mv.PopTo(1);
            Course("secplus");
            var fw = A.Course.Practice.FirstOrDefault(s => s.Items.Any(i => i.Kind == "firewall"));
            if (fw != null) { mv.Push(new PbqPracticePage(A, fw, fw.Items.First(i => i.Kind == "firewall").Id)); Pump(); Snap("cert-phone-pbq-firewall"); Buttons44("phone PBQ"); }
            mv.PopTo(1);
            Course("rhcsa");
            var cards = new CardsPage(A); mv.Push(cards); cards.Start(A.Course.Decks.First(), false); Pump(); Snap("cert-phone-card");
            mv.Push(new CheatPage(A)); Pump(); Snap("cert-phone-cheat");
            mv.Push(new SearchPage(A, "firewall")); Pump(); Snap("cert-phone-search");
            mv.Push(new ExamsPage(A)); Pump(); Snap("cert-phone-exams");
        }
        finally { win.Width = 1180; win.Height = 800; }
    }
}
