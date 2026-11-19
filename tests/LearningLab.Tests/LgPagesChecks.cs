// Learning Lab - Learning Games' pages, played through off screen: every past profiles.json format loads and keeps
// fields it doesn't know, a damaged file is never saved over, a player is added, each math game is played (right and
// wrong answers), practice, the Workshop, results with rewards, a subject's quiz / cards / lightning / match, the parent
// view and the lobby. Group "games-pages"; -Snapshot saves each page wide and phone-sized.
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using LearningLab.LearningGames;
using LearningLab.Views;

namespace LearningLab.Tests;

static class LgPagesChecks
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LearningGames", name);
    static void Check(string what, bool ok, string detail = null) => Program.Check("games-pages: " + what, ok, detail);

    static string Copy(string fixture)
    {
        string dir = Path.Combine(Data.Root, "lg-" + Path.GetFileNameWithoutExtension(fixture));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "profiles.json");
        File.Copy(Fixture(fixture), f);
        return f;
    }

    public static void Run()
    {
        global::LearningGames.Sound.Enabled = false;
        Saves();
        Pages();
    }

    static void Saves()
    {
        foreach (var (fx, count, name) in new[] { ("amath-2026-10-13.json", 0, "Mia"), ("alearn-2026-10-14.json", 0, "Ava"), ("learninggames-2026-10-24.json", 2, "Theo"), ("future-version.json", 1, "Grandpa") })
        {
            string f = Copy(fx);
            Profiles.UseFile(f);
            Profiles.Load();
            Check(fx + " loads", Profiles.State == ProfilesState.Ok && Profiles.All.Count >= 1 && Profiles.All.Any(p => p.Name == name), Profiles.State + " " + Profiles.Message);
            if (count > 0) Check(fx + ": every player", Profiles.All.Count == count, Profiles.All.Count.ToString());
            foreach (var p in Profiles.All) Check(fx + ": " + p.Name + " has an age and (children) reward points", p.Age > 0 && (p.Grade == 0 || p.RewardPoints >= 0));
            Check(fx + ": saves", Profiles.Save());
            Profiles.Load();
            Check(fx + ": reads back the same players", Profiles.All.Any(p => p.Name == name));
        }
        // a later version's fields are kept
        string fut = Copy("future-version.json");
        Profiles.UseFile(fut); Profiles.Load();
        Profiles.All[0].Stars += 1; Profiles.Save();
        var back = JsonNode.Parse(File.ReadAllText(fut)) as JsonArray;
        Check("fields this version doesn't know are written back", back?[0]?["Avatar"]?["Kind"]?.ToString() == "owl" && back?[0]?["Badges"] is JsonArray);
        // the oldest player gets reward points for the math already right (Correct summed)
        string am = Copy("amath-2026-10-13.json");
        Profiles.UseFile(am); Profiles.Load();
        var mia = Profiles.All.First(p => p.Name == "Mia");
        Check("an AMath player gets reward points for past right answers", mia.RewardPoints > 0, mia.RewardPoints.ToString());
        Check("an AMath player gets an age for her grade", mia.Age == 7, mia.Age.ToString());
        // a damaged file: the backup is used; nothing readable: never saved over
        string dmg = Copy("learninggames-2026-10-24.json");
        Profiles.UseFile(dmg); Profiles.Load(); Profiles.Save(); Profiles.Save();
        File.WriteAllText(dmg, "{ not json");
        Profiles.UseFile(dmg); Profiles.Load();
        Check("a damaged file: the newest backup is used and the person is told", Profiles.State == ProfilesState.Backup && Profiles.All.Count == 2 && Profiles.Message.Length > 0, Profiles.State + " " + Profiles.Message);
        Check("a damaged file is copied aside", Directory.GetFiles(Path.GetDirectoryName(dmg)).Any(x => x.Contains(".unreadable-")));
        string bad = Path.Combine(Data.Root, "lg-bad", "profiles.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bad));
        File.WriteAllText(bad, "garbage");
        Profiles.UseFile(bad); Profiles.Load();
        Check("nothing readable: locked", Profiles.State == ProfilesState.Locked);
        Check("locked: saving refuses", !Profiles.Save() && File.ReadAllText(bad) == "garbage");
        // spaced review ids match App Hub's (MD5 of question | code | answer)
        var q = new Question { Q = "What is 2 + 2?", Choices = new() { "4", "5" }, Answer = 0 };
        Check("review ids are 12 hex characters", q.Id.Length == 12 && q.Id.All(Uri.IsHexDigit), q.Id);
        Check("every subject bank opens", Bank.Subjects.Where(s => s.Id != "math").All(s => Bank.Get(s.Id) != null && Bank.LevelsOf(s.Id).Length == 7));
    }

    static Window win;
    static void Snap(string name) { LearningLab.Tests.Snap.Save(win, name); }
    static void Pump() { Dispatcher.UIThread.RunJobs(); }

    static void Pages()
    {
        string f = Copy("learninggames-2026-10-24.json");
        Lg.Reset();
        Profiles.UseFile(f);
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        mv.Push(new ProfilesPage()); Pump();
        Check("Who's playing? lists the players", Profiles.All.Count == 2 && mv.Page is ProfilesPage);
        Snap("lg-profiles");
        // add a player
        var edit = new EditPlayerPage(null); mv.Push(edit); Pump();
        edit.SetForTest("", "7", false); edit.SaveForTest();
        Check("a player needs a name", mv.MessageOpen && Profiles.All.Count == 2);
        mv.PressMessage("OK");
        edit.SetForTest("Sam", "7", false); Snap("lg-newplayer"); edit.SaveForTest();
        Check("a new player is saved", Profiles.All.Any(p => p.Name == "Sam" && p.Grade == 2) && mv.Page is ProfilesPage && File.ReadAllText(f).Contains("\"Sam\""));
        var theo = Profiles.All.First(p => p.Name == "Theo");
        ProfilesPage.Select(theo); Pump();
        Check("the hub opens", mv.Page is HubPage);
        Snap("lg-hub");
        mv.Push(new MathHomePage()); Pump();
        Snap("lg-math");

        // practice: right, wrong, then right
        GamePage.Start("practice", "pctwhole"); Pump(); Pump();
        var gp = mv.Page as GamePage;
        Check("practice starts with a problem", gp?.G.Problem != null, gp?.QuestionText);
        Snap("lg-practice");
        int before = theo.Skill("pctwhole").Attempts;
        gp.Submit(gp.G.Problem.Answer);
        Check("a right answer scores", gp.G.Right == 1 && gp.G.Score > 0 && theo.Skill("pctwhole").Attempts == before + 1);
        gp.RunPendingForTest();
        gp.Submit("999999");
        Check("a wrong answer brings a hint and is kept as a mistake", gp.G.Wrong == 1 && theo.Mistakes.Last().Given == "999999");
        Snap("lg-practice-hint");
        for (int i = 0; i < 12 && mv.Page == gp; i++) { gp.Submit(gp.G.Problem.Answer); gp.RunPendingForTest(); }
        Check("ten problems end with the results", mv.Page is ResultsPage, mv.Page?.GetType().Name);
        Snap("lg-results");
        mv.BackTo<MathHomePage>();

        // each game: right answers move it on
        foreach (var kind in new[] { "asteroid", "pizza", "boss", "race", "frog" })
        {
            GamePage.Start(kind, null); Pump(); Pump();
            gp = mv.Page as GamePage;
            Check(kind + " starts", gp != null && (gp.G.Problem != null || kind == "race"), kind);
            if (kind == "race") gp.SkipCountdownForTest();
            for (int k = 0; k < 3; k++) gp.Tick();
            if (kind == "frog") { int right = gp.RightPadForTest(); gp.JumpFrog(right); for (int k = 0; k < 16; k++) gp.Tick(); Check("frog: the right pad scores", gp.G.Right == 1); }
            else if (kind == "pizza") { int t = int.Parse(gp.G.Problem.Answer); for (int s = 0; s < t; s++) gp.ToggleSlice(s); gp.Submit(t.ToString()); Check("pizza: the right slices serve", gp.G.Right == 1, gp.QuestionText); }
            else if (kind == "boss") { int hp = gp.BossHp; gp.Submit(gp.G.Problem.Answer); Check("boss: a right answer hits the monster", gp.BossHp == hp - 1); gp.AttackNowForTest(); Check("boss: the monster attacks", gp.G.Lives == 4); }
            else if (kind == "race") { gp.Submit(gp.G.Problem.Answer); Check("race: a right answer moves the car", gp.RacePos("me") == 1); }
            else { gp.Submit(gp.G.Problem.Answer); Check(kind + ": a right answer scores", gp.G.Right == 1); }
            for (int k = 0; k < 10; k++) gp.Tick();
            Snap("lg-game-" + kind);
            mv.Back(); Pump();
            Check(kind + ": Stop goes back", mv.Page is MathHomePage);
        }
        // a whole asteroid game played right reaches the results
        GamePage.Start("asteroid", null); Pump(); Pump();
        gp = mv.Page as GamePage;
        for (int i = 0; i < 20 && mv.Page == gp; i++) { gp.Submit(gp.G.Problem.Answer); gp.RunPendingForTest(); }
        Check("asteroid: fifteen right answers win", mv.Page is ResultsPage rp && rp.Stars == 3, mv.Page?.GetType().Name);
        mv.BackTo<MathHomePage>();

        // the Workshop
        var ld = new LongDivPage(); mv.Push(ld); Pump();
        Snap("lg-longdiv");
        for (int i = 0; i < 3 && mv.Page == ld; i++) ld.SolveForTest();
        Check("the Workshop's three problems end with the results", mv.Page is ResultsPage);
        Snap("lg-longdiv-done");
        mv.BackTo<MathHomePage>();
        mv.Push(new RewardsPage()); Pump(); Snap("lg-mylab"); mv.Back();
        mv.Push(new ProgressPage(false)); Pump(); Snap("lg-progress"); mv.Back();

        // a subject
        mv.BackTo<HubPage>();
        var sp = new SubjectPage("science"); mv.Push(sp); Pump();
        Snap("lg-subject");
        int stars = theo.Stars;
        sp.Start("quiz", -1); Pump();
        var qp = mv.Page as QuizPage;
        Check("Quiz Quest starts", qp != null);
        Snap("lg-quiz");
        var item = typeof(QuizPage).GetField("R", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(qp) as SubjectRound;
        for (int i = 0; i < 10 && mv.Page == qp; i++) { qp.Answer(item.Items[item.I].Q.Answer); if (i == 0) Snap("lg-quiz-answered"); qp.Next(); }
        Check("ten right answers end the quiz with three stars", mv.Page is DonePage dp && dp.Stars == 3 && theo.Stars >= stars + 10);
        Snap("lg-done");
        mv.Back();
        sp.Start("cards", -1); Pump();
        var cp = mv.Page as CardsPage; cp.Flip(); Snap("lg-cards"); cp.Grade(true);
        mv.Back();
        sp.Start("match", -1); Pump();
        var mp = mv.Page as MatchPage; Snap("lg-match"); mp.SolveForTest();
        Check("Memory Match: every pair found ends the round", mv.Page is DonePage);
        mv.Back();
        sp.Start("lightning", -1); Pump();
        var lp = mv.Page as QuizPage; lp.EndNowForTest();
        Check("Lightning Round ends when the clock runs out", mv.Page is DonePage);
        mv.Back();
        string due = Profiles.Find(theo.Id).Subject("science").Cards.Count.ToString();
        Check("answers are in the review boxes", int.Parse(due) >= 10, due);

        // the parent view and the lobby (nothing goes on the network until asked)
        var grandpa = Profiles.Add("Grandpa", 0, 70, "#A855F7");
        mv.BackTo<ProfilesPage>();
        ProfilesPage.Select(grandpa); Pump();
        var pp = new ParentPage(); mv.Push(pp); Pump();
        pp.ShowKid("local:" + theo.Id); Pump();
        Snap("lg-parent");
        mv.BackTo<ProfilesPage>();
        ProfilesPage.Select(theo); mv.Push(new MathHomePage());
        global::LearningGames.GameHost.LoopbackOnly = true;
        mv.Push(new LobbyPage()); Pump(); Snap("lg-lobby"); mv.Back();
        Check("the players were saved along the way", File.ReadAllText(f).Contains("pctwhole") && Profiles.State == ProfilesState.Ok);

        // phone-sized pictures of the main pages
        win.Width = 400; win.Height = 840;
        mv.BackTo<ProfilesPage>(); Pump(); Snap("lg-phone-profiles");
        ProfilesPage.Select(theo); Pump(); Snap("lg-phone-hub");
        mv.Push(new MathHomePage()); Pump(); Snap("lg-phone-math");
        GamePage.Start("practice", "pctwhole"); Pump(); Pump(); Snap("lg-phone-practice");
        mv.Back();
        GamePage.Start("asteroid", null); Pump(); Pump(); for (int k = 0; k < 30; k++) (mv.Page as GamePage).Tick(); Snap("lg-phone-asteroid");
        mv.Back();
        GamePage.Start("pizza", null); Pump(); Pump(); Snap("lg-phone-pizza");
        mv.Back();
        var ld2 = new LongDivPage(); mv.Push(ld2); Pump(); Snap("lg-phone-longdiv"); mv.Back();
        mv.BackTo<HubPage>();
        var sp2 = new SubjectPage("history"); mv.Push(sp2); Pump(); Snap("lg-phone-subject");
        sp2.Start("quiz", -1); Pump(); Snap("lg-phone-quiz");
        win.Close();
        Family.Stop();
        Lg.Reset();
    }
}
