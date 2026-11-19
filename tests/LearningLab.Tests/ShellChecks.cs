using Avalonia.Controls;
using Avalonia.Headless;
using LearningLab.Views;

namespace LearningLab.Tests;

static class ShellChecks
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("shell: " + what, ok, detail);
    static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    public static void Run()
    {
        var w = new Window { Width = 1180, Height = 800, Content = new MainView() };
        w.Show();
        var mv = MainView.Current;
        Check("the home page opens", mv.Page is HomePage);
        var names = HomePage.Categories.Select(c => c.Name).ToArray();
        Check("home is by school subject", string.Join("|", names) == "Language Arts|Math|Science|Social Studies|World Languages|Computers & Technology", string.Join("|", names));
        string Inside(string cat) => string.Join("|", HomePage.Categories.First(c => c.Name == cat).Entries.Select(e => e.Id));
        Check("Language Arts: English (with the Spelling Bee inside)", Inside("Language Arts") == "english" && HomePage.Find("spelling-bee") == HomePage.SpellingBee && HomePage.CategoryOf("spelling-bee").Name == "Language Arts");
        Check("Math: the skill map, the math games and the Long Division Workshop", Inside("Math") == "skill-map|math-games|long-division");
        Check("Science", Inside("Science") == "science");
        Check("Social Studies: history and US citizenship", Inside("Social Studies") == "history|civics");
        Check("World Languages: Spanish lessons and the Spanish quizzes", Inside("World Languages") == "languages|spanish");
        Check("Computers & Technology: coding, Emulator Lab, Network Lab, Cert Lab, Cyber Range", Inside("Computers & Technology") == "coding|emulator|network|cert|range");
        Check("every entry opens (Cert Lab and Cyber Range too)", HomePage.Categories.SelectMany(c => c.Entries).All(e => e.Open != null));
        Check("Learning Games' subject pages ask for a player", HomePage.Categories.SelectMany(c => c.Entries).Where(e => e.Player != null).Select(e => e.Id).OrderBy(x => x).SequenceEqual(new[] { "civics", "coding", "english", "history", "long-division", "math-games", "science", "skill-map", "spanish" }));
        Snap.Save(w, "home");
        w.Width = 400; w.Height = 820;
        Snap.Save(w, "home-phone");
        foreach (var c in HomePage.Categories)
        {
            w.Width = 1180; w.Height = 800;
            mv.Push(new CategoryPage(c)); Pump();
            Check(c.Name + ": its page lists what's inside", mv.Page is CategoryPage cp && cp.Category == c);
            Snap.Save(w, "category-" + c.Id);
            w.Width = 400; w.Height = 820;
            Snap.Save(w, "category-" + c.Id + "-phone");
            mv.Back();
        }
        w.Width = 1180; w.Height = 800;

        // a subject that needs a player: "Who's playing?" first, then straight to it
        string dir = Path.Combine(Data.Root, "shell-players");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "profiles.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LearningGames", "learninggames-2026-10-24.json"), f, true);
        LearningLab.LearningGames.Lg.Reset();
        LearningLab.LearningGames.Profiles.UseFile(f);
        mv.Push(new CategoryPage(HomePage.CategoryOf("science"))); Pump();
        HomePage.Open(HomePage.Find("science")); Pump();
        var who = mv.Page as LearningLab.LearningGames.ProfilesPage;
        Check("nobody playing: Who's playing? comes first", who != null && who.Then != null);
        Snap.Save(w, "category-who");
        var theo = LearningLab.LearningGames.Profiles.All.First(p => p.Name == "Theo");
        who.Choose(theo); Pump();
        Check("then the subject itself, in its place (Back goes to Science)", mv.Page is LearningLab.LearningGames.SubjectPage sp && sp.Id == "science" && mv.Depth == 3);
        mv.Back(); Pump();
        HomePage.Open(HomePage.Find("science")); Pump();
        Check("someone playing: straight to the subject", mv.Page is LearningLab.LearningGames.SubjectPage && mv.Depth == 3);
        LearningLab.LearningGames.Lg.ChangePlayer(); Pump();
        Check("the player chip goes back to Who's playing? for this subject", mv.Page is LearningLab.LearningGames.ProfilesPage pp && pp.Then != null && mv.Depth == 3);
        mv.PopTo(1); Pump();
        HomePage.Open(HomePage.Find("long-division")); Pump();
        (mv.Page as LearningLab.LearningGames.ProfilesPage)?.Choose(theo); Pump();
        Check("the Long Division Workshop opens from Math", mv.Page is LearningLab.LearningGames.LongDivPage);
        mv.PopTo(1); Pump();
        HomePage.Open(HomePage.Find("emulator")); Pump();
        Check("Emulator Lab needs no player", mv.Page is LearningLab.EmulatorLab.EmulatorPage);
        mv.PopTo(1); Pump();
        ((HomePage)mv.Page).AllGames.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        Check("All of Learning Games opens its own Who's playing? (then the hub with family games and the parent view)", mv.Page is LearningLab.LearningGames.ProfilesPage p2 && p2.Then == null);
        p2 = (LearningLab.LearningGames.ProfilesPage)mv.Page;
        p2.Choose(theo); Pump();
        Check("... and its hub", mv.Page is LearningLab.LearningGames.HubPage);
        mv.PopTo(1);
        LearningLab.LearningGames.Lg.Reset();
        w.Close();
    }
}

static class Snap
{
    /// <summary>Renders the window and saves it as name.png in the snapshot folder (only with -Snapshot).</summary>
    public static void Save(Window w, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // each frame holds a native bitmap: release it at once (a full -Snapshot run makes several hundred, and letting
        // them pile up ended in an access violation inside Skia's PNG writer, 2026-11-11)
        using var frame = w.CaptureRenderedFrame();
        if (Program.SnapshotDir == null || frame == null) return;
        Directory.CreateDirectory(Program.SnapshotDir);
        frame.Save(Path.Combine(Program.SnapshotDir, name + ".png"));
        if (++saved % 40 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    static int saved;
}
