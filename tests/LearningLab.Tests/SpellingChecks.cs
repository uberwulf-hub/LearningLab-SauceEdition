// Learning Lab - the checks of Spelling Bee (group "spelling"): the bundled lists, importing a practice page
// (a fixture of made-up words in the same shape), .txt and pasted words, judging (other spellings, accents, capitals,
// spaces / dashes / apostrophes), hints, the spell-out, spoken letters, tricky words, saves (round trip, unknown fields
// kept, a damaged file: the backup, or blocked and never written over), the list store, and a whole practice run
// through the pages with snapshots wide and phone-sized. Pretend speech recognition; nothing is played or listened to.
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LearningLab.LearningGames;
using LearningLab.Services;
using LearningLab.SpellingBee;
using LearningLab.Views;

namespace LearningLab.Tests;

static class SpellingChecks
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("spelling: " + what, ok, detail);
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellingBee", name);

    sealed class FakeRecognizer : IRecognizer
    {
        public string Hears = "";
        public int LetterCalls;
        public bool Available(string lang) => lang.StartsWith("en");
        public bool NeedsPermission => false;
        public Task<bool> AskPermission() => Task.FromResult(true);
        public Task<string> Listen(string lang, double seconds, IReadOnlyList<string> expected) => Task.FromResult<string>(null);
        public Task<string> ListenLetters(string lang, double seconds) { LetterCalls++; return Task.FromResult(Hears); }
    }

    public static void Run()
    {
        global::LearningGames.Sound.Enabled = false;
        Lists();
        Judging();
        Spoken();
        Saves();
        Pages();
    }

    // ------------------------------------------------------------------ lists and importing
    static void Lists()
    {
        var b = Bee.BuiltIn;
        Check("Learning Lab's own five lists are bundled", b.Count == 5 && string.Join("|", b.Select(l => l.Name)) == "Easy Words|Medium Words|Hard Words|Harder Words|Hardest Words", string.Join("|", b.Select(l => l.Name)));
        Check("Easy Words is first (chosen the first time) with its emoji and note", b.Count > 0 && b[0].Id == "easy" && b[0].Emoji == "\U0001F331" && b[0].Note == "A good place to start" && b[0].Words[0].Word == "cabin" && b.All(l => l.BuiltIn));
        Check("every bundled list has words, each a real spelling", b.All(l => l.Words.Count >= 20 && l.Words.All(w => w.Word.Any(char.IsLetter) && !w.Word.Contains('_'))), string.Join(",", b.Select(l => l.Words.Count)));
        var adv = b.FirstOrDefault(l => l.Name == "Harder Words")?.Words.FirstOrDefault(w => w.Word == "fertilizer");
        Check("other spellings: fertilizer|fertiliser", adv != null && adv.Alts.Count == 2 && adv.Alts[1] == "fertiliser");
        var exp = b.FirstOrDefault(l => l.Name == "Hardest Words");
        Check("\"_\" is a space (a cappella, Addis Ababa) and accents stay (éclair)", exp != null && exp.Words.Any(w => w.Word == "a cappella") && exp.Words.Any(w => w.Word == "Addis Ababa") && exp.Words.Any(w => w.Word == "éclair"));

        var page = BeeImport.ParseHtml(File.ReadAllText(Fixture("practice-page.html")));
        Check("a practice page: its lists (an empty one left out)", page.Count == 2, page.Count.ToString());
        if (page.Count == 2)
        {
            var g = page[0];
            Check("a practice page: id, name, emoji, note", g.Id == "g1" && g.Name == "Garden Words" && g.Emoji == "\U0001F331" && g.Note == "Week 1");
            Check("a practice page: words across lines, each once", string.Join(",", g.Words.Select(w => w.Word)) == "apple,banana,colour,ice cream,jack-o'-lantern,Zanzibar,pebble,meadow", string.Join(",", g.Words.Select(w => w.Word)));
            Check("a practice page: \"a|b\" and \"_\"", g.Words[2].Alts.SequenceEqual(new[] { "colour", "color" }) && g.Words[3].Word == "ice cream");
            Check("a practice page: a name in single quotes with quotes inside, accents, alternatives with spaces", page[1].Name == "Kitchen \"Words\"" && page[1].Words[1].Alts.SequenceEqual(new[] { "crème brûlée", "creme brulee" }));
            Check("a practice page: words know their list", g.Words.All(w => w.ListId == "g1"));
        }
        Check("a page without LISTS: nothing", BeeImport.ParseHtml("<html><body>const OTHER = [ {words:`a b`} ];</body></html>").Count == 0 && BeeImport.ParseHtml("").Count == 0);

        var txt = BeeImport.ParseText(File.ReadAllText(Fixture("words.txt")), "words");
        Check("a .txt file: \"# Name\" starts a list", txt.Count == 2 && txt[0].Name == "Week 3 words" && txt[1].Name == "Week 4 words", string.Join("|", txt.Select(l => l.Name)));
        if (txt.Count == 2)
        {
            Check("a .txt file: commas keep two-word entries, a word once, alternatives", string.Join(",", txt[0].Words.Select(w => w.Token)) == "lantern,ice cream,x-ray,harbor|harbour", string.Join(",", txt[0].Words.Select(w => w.Token)));
            Check("a .txt file: spaces split, quotes dropped, semicolons split", string.Join(",", txt[1].Words.Select(w => w.Word)) == "puzzle,quiver,zigzag,whistle", string.Join(",", txt[1].Words.Select(w => w.Word)));
        }
        var pasted = BeeImport.ParseText("  cabin carve\r\nearth, half\n\n123 !!", "My words");
        Check("pasted words: one list with the name given, no numbers or marks", pasted.Count == 1 && pasted[0].Name == "My words" && string.Join(",", pasted[0].Words.Select(w => w.Word)) == "cabin,carve,earth,half");
        Check("nothing pasted: no list", BeeImport.ParseText(" \n ", "x").Count == 0);
        Check("a file's name names its list", Bee.ReadFile("Week_6-words.txt", "a b c")[0].Name == "Week 6 words");
        Check("an .html without LISTS: its text's words", Bee.ReadFile("w.html", "<p>apple</p><p>pear</p>")[0].Words.Count == 2);
    }

    // ------------------------------------------------------------------ judging, hints, spell-out
    static BeeWord W(string token) => BeeWord.Parse(token);

    static void Judging()
    {
        var table = new (string Typed, string Word, bool Ok, bool Close, string Matched)[]
        {
            ("cabin", "cabin", true, false, "cabin"),
            ("Cabin", "cabin", true, false, "cabin"),
            ("CABIN ", "cabin", true, false, "cabin"),
            ("tripoli", "Tripoli", true, false, "Tripoli"),
            ("eclair", "éclair", true, false, "éclair"),
            ("ÉCLAIR", "éclair", true, false, "éclair"),
            ("fertiliser", "fertilizer|fertiliser", true, false, "fertiliser"),
            ("fertilizer", "fertilizer|fertiliser", true, false, "fertilizer"),
            ("a cappella", "a_cappella", true, false, "a cappella"),
            ("a  Cappella", "a_cappella", true, false, "a cappella"),
            ("acappella", "a_cappella", true, true, "a cappella"),
            ("jackolantern", "jack-o'-lantern", true, true, "jack-o'-lantern"),
            ("jack-o’-lantern", "jack-o'-lantern", true, false, "jack-o'-lantern"),
            ("xray", "x-ray", true, true, "x-ray"),
            ("cabbin", "cabin", false, false, null),
            ("fertilisor", "fertilizer|fertiliser", false, false, null),
            ("see ay bee eye en", "cabin", true, false, "cabin"),
            ("C A B I N", "cabin", true, false, "cabin"),
        };
        foreach (var t in table)
        {
            var v = BeeCheck.Judge(t.Typed, W(t.Word));
            Check("judge \"" + t.Typed + "\" for " + t.Word, v.Ok == t.Ok && v.Close == t.Close && (t.Matched == null || v.Matched == t.Matched), v.Ok + " " + v.Close + " " + v.Matched);
        }
        Check("a wrong spoken spelling shows the letters heard", BeeCheck.Judge("see ay bee oh en", W("cabin")).Typed == "cabon");
        Check("wrong letters are marked", string.Join("", BeeCheck.Wrong("cabin", "cabon").Select(x => x ? "x" : ".")) == "...x." && string.Join("", BeeCheck.Wrong("cabin", "cab").Select(x => x ? "x" : ".")) == "...xx");
        Check("accents and capitals aren't marked wrong", !BeeCheck.Wrong("Éclair", "eclair").Any(x => x));
        string Marks(string c, string t) => string.Join("", BeeCheck.Wrong(c, t).Select(x => x ? "x" : "."));
        Check("a missing letter marks only that letter", Marks("half", "haf") == "..x." && Marks("friend", "freind") == "..x..." && Marks("riddle", "ridle") == "...x.." && Marks("cabin", "") == "xxxxx", Marks("half", "haf") + " " + Marks("friend", "freind") + " " + Marks("riddle", "ridle"));
        Check("hint 1: blanks and the letter count", BeeCheck.Hint("cabin", 1) == ("_ _ _ _ _", 5));
        Check("hint 2: the first letter", BeeCheck.Hint("cabin", 2).Blanks == "c _ _ _ _");
        Check("hint 3: and the last", BeeCheck.Hint("cabin", 3).Blanks == "c _ _ _ n");
        Check("hints keep spaces and dashes (not counted as letters)", BeeCheck.Hint("x-ray", 1) == ("_ - _ _ _", 4) && BeeCheck.Hint("a cappella", 3) == ("a   _ _ _ _ _ _ _ a", 9), BeeCheck.Hint("a cappella", 3).Blanks + "/" + BeeCheck.Hint("a cappella", 3).Letters);
        Check("spell-out: capital letters with commas", BeeCheck.SpellOut("cabin") == "C, A, B, I, N");
        Check("spell-out: space, dash, accents dropped", BeeCheck.SpellOut("a x-é") == "A, space, X, dash, E", BeeCheck.SpellOut("a x-é"));
        Check("spell-out: apostrophe", BeeCheck.SpellOut("o'clock") == "O, apostrophe, C, L, O, C, K");
        Check("Spell it for me: the word, its letters, the word", Bee.SpellItText(W("cabin")) == "cabin. C, A, B, I, N. cabin.");
        Check("results: the line for each score", BeeCheck.Cheer(10, 10).Message.StartsWith("Perfect") && BeeCheck.Cheer(8, 10).Message.StartsWith("Awesome") && BeeCheck.Cheer(5, 10).Message.StartsWith("Nice") && BeeCheck.Cheer(1, 10).Message.StartsWith("Keep going"));

        var picked = BeeRun.Pick(new[] { W("a"), W("b"), W("a"), W("c"), W("d") }, false, 3, new Random(1));
        Check("a run: each word once, in order, cut to how many", string.Join("", picked.Select(w => w.Word)) == "abc");
        Check("a run: All", BeeRun.Pick(new[] { W("a"), W("b") }, true, 0, new Random(1)).Count == 2);
        var mixed = BeeRun.Pick("abcdefghijklmnop".Select(c => W(c.ToString())), true, 0, new Random(3));
        Check("a run: mixed up", string.Join("", mixed.Select(w => w.Word)) != "abcdefghijklmnop" && mixed.Count == 16);
    }

    static void Spoken()
    {
        var table = new (string Said, string Spelled)[]
        {
            ("see ay bee eye en", "cabin"),
            ("C. A. B. I. N.", "cabin"),
            ("c, a, b, i, n", "cabin"),
            ("bee ee", "be"),
            ("double u ay ex", "wax"),
            ("double you ay ex", "wax"),
            ("doubleu", "w"),
            ("bee ay double ell", "ball"),
            ("bee ay double l", "ball"),
            ("double oh", "oo"),
            ("tee double ee", "tee"),
            ("ex dash are ay why", "x-ray"),
            ("ex hyphen ray", "x-ray"),
            ("capital tee are eye pee oh ell eye", "tripoli"),
            ("um see uh ay bee", "cab"),
            ("the letter zee", "z"),
            ("zed", "z"),
            ("ay space bee", "a b"),
            ("oh apostrophe see", "o'c"),
            ("cabin", "cabin"),
            ("cue you eye ex", "quix"),
            ("aitch ay why", "hay"),
            ("", ""),
        };
        foreach (var t in table)
        {
            string got = BeeCheck.ParseSpoken(t.Said);
            Check("spoken \"" + t.Said + "\" -> \"" + t.Spelled + "\"", got == t.Spelled, got);
        }
        var vocab = BeeCheck.SpokenVocabulary();
        Check("the letter list for recognizers has every letter's names, double and dash", "abcdefghijklmnopqrstuvwxyz".All(c => vocab.Contains(c.ToString())) && vocab.Contains("double") && vocab.Contains("dash") && vocab.Contains("bee"));
    }

    // ------------------------------------------------------------------ saves
    static string Fresh(string name)
    {
        string d = Path.Combine(Data.Root, "bee-" + name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    static void Saves()
    {
        // players.json: the fixture loads, unknown fields are kept
        string d = Fresh("players");
        string f = Path.Combine(d, "players.json");
        File.Copy(Fixture("players-2026-11-11.json"), f);
        var ps = BeePlayers.Load(f);
        var riley = ps.For("riley");
        Check("players.json (2026-11-11) loads", !ps.Blocked && ps.Message == null && riley.Tricky.Count == 2 && riley.HowMany == 10 && !riley.Shuffle && !riley.AutoSay && riley.Lists.SequenceEqual(new[] { "p2", "p3" }));
        Check("the history reads back", riley.History.Count == 1 && riley.History[0].Right == 8 && riley.History[0].Total == 10 && riley.History[0].Lists == "Starter Words");
        Check("tricky words keep their other spellings", riley.TrickyWords[1].Alts.SequenceEqual(new[] { "fertilizer", "fertiliser" }));
        riley.AddTricky(W("cabin")); riley.AddTricky(W("Earth"));
        riley.RemoveTricky(W("fertilizer|fertiliser"));
        Check("tricky words: added once, removed", string.Join(",", riley.Tricky) == "cabin,Earth");
        riley.AddHistory("Two Bee", 5, 6);
        var sam = ps.For("Sam");
        Check("a new player gets the defaults", sam.Lists == null && sam.HowMany == 25 && sam.Shuffle && sam.AutoSay && sam.Tricky.Count == 0);
        sam.HowMany = 50; sam.Lists = new List<string> { "p4" }; sam.AddTricky(W("a_cappella"));
        Check("saves", ps.Save());
        var back = JsonNode.Parse(File.ReadAllText(f));
        Check("fields this version doesn't know are written back", back?["futureTopLevel"]?["kept"]?.GetValue<bool>() == true && back?["players"]?["Riley"]?["badges"]?[0]?.ToString() == "first-bee" && back?["players"]?["Riley"]?["history"]?[0]?["seconds"]?.GetValue<int>() == 300);
        var ps2 = BeePlayers.Load(f);
        Check("round trip", ps2.For("Sam").HowMany == 50 && ps2.For("Sam").TrickyWords[0].Word == "a cappella" && ps2.For("Riley").History.Count == 2 && ps2.For("Riley").History[1].Right == 5);

        // a damaged file: the newest backup; nothing readable: blocked, never written over
        ps2.For("Sam").HowMany = 10; ps2.Save();
        File.WriteAllText(f, "{ broken");
        var ps3 = BeePlayers.Load(f);
        Check("a damaged file: the newest backup is used and the person is told", !ps3.Blocked && ps3.Message != null && ps3.For("Sam").HowMany == 50, ps3.Message);
        Check("a damaged file is copied aside", Directory.GetFiles(d).Any(x => x.Contains(".unreadable-")));
        string d2 = Fresh("blocked");
        string f2 = Path.Combine(d2, "players.json");
        File.WriteAllText(f2, "garbage");
        var ps4 = BeePlayers.Load(f2);
        ps4.For("Sam").AddTricky(W("cabin"));
        Check("nothing readable: blocked with a sentence", ps4.Blocked && ps4.Message != null && ps4.Message.Contains("couldn't be read"));
        Check("blocked: saving refuses and the file stays as it was", !ps4.Save() && File.ReadAllText(f2) == "garbage");
        ps4.StartFresh();
        Check("Start fresh (the person chose it): saves again, the old file kept aside", ps4.Save() && Directory.GetFiles(d2).Any(x => x.Contains(".unreadable-")));

        // the list store
        string ld = Fresh("lists");
        File.Copy(Fixture("my-list.json"), Path.Combine(ld, "my-list.json"));
        var store = new BeeLists(ld);
        var mine = store.Load();
        Check("an added list loads", mine.Count == 1 && mine[0].Name == "Week 5" && mine[0].Words.Count == 3 && mine[0].Words[1].Alts.Count == 2 && !mine[0].BuiltIn);
        mine[0].Name = "Week 5 (Riley)";
        store.Save(mine[0]);
        var again = store.Load();
        Check("renamed in its own file, unknown fields kept", again.Count == 1 && again[0].Name == "Week 5 (Riley)" && JsonNode.Parse(File.ReadAllText(Path.Combine(ld, "my-list.json")))?["color"]?.ToString() == "#22C55E");
        var nl = BeeImport.ParseText("puzzle quiver", "New")[0];
        nl.Id = BeeImport.NewId();
        store.Save(nl);
        Check("a new list gets its own file", store.Load().Count == 2 && File.Exists(Path.Combine(ld, nl.Id + ".json")));
        store.Save(nl); store.Save(nl);
        store.Delete(nl);
        Check("deleting a list removes its file and backups", store.Load().Count == 1 && !Directory.GetFiles(ld).Any(x => Path.GetFileName(x).StartsWith(nl.Id)));
        File.WriteAllText(Path.Combine(ld, "bad.json"), "not json");
        var withBad = store.Load();
        Check("a list file that can't be read: named, left as it is", withBad.Count == 1 && store.Problems.Count == 1 && File.ReadAllText(Path.Combine(ld, "bad.json")) == "not json", string.Join(" / ", store.Problems));
        try { store.Save(new BeeList { Id = "bad", Name = "x", Words = { W("a") } }); Check("a list file that can't be read is never saved over", false); }
        catch (IOException) { Check("a list file that can't be read is never saved over", File.ReadAllText(Path.Combine(ld, "bad.json")) == "not json"); }
    }

    // ------------------------------------------------------------------ the pages
    static Window win;
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Snap(string name)
    {
        win.Width = 1180; win.Height = 800;
        LearningLab.Tests.Snap.Save(win, name);
        win.Width = 400; win.Height = 820;
        LearningLab.Tests.Snap.Save(win, name + "-phone");
        win.Width = 1180; win.Height = 800;
        Pump();
    }
    static KeyEventArgs Enter => new() { Key = Key.Enter };

    static void Pages()
    {
        string data = Fresh("pages");
        Bee.UseFolder(data);
        Bee.Said.Clear();
        string pf = Path.Combine(data, "profiles.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LearningGames", "learninggames-2026-10-24.json"), pf);
        Lg.Reset();
        Profiles.UseFile(pf);
        Lg.EnsureLoaded();
        Profiles.Add("Sam", 2, 8, "#3B82F6");
        Profiles.Add("Riley", 6, 11, "#EC4899");
        var rec = new FakeRecognizer();
        PlatformAudio.Recognizer = rec;

        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        mv.Push(new CategoryPage(HomePage.CategoryOf("spelling-bee"))); Pump();
        Snap("bee-category");
        HomePage.Open(HomePage.Find("spelling-bee")); Pump();
        var who = mv.Page as ProfilesPage;
        Check("Who's practicing? comes first", who != null && who.Then != null);
        Snap("bee-who");
        who.Choose(Profiles.All.First(p => p.Name == "Sam")); Pump();
        var home = mv.Page as BeeHomePage;
        Check("then the Spelling Bee start page", home != null);
        if (home == null) return;
        Check("the first time: Easy Words ticked, with My Tricky Words not shown yet", home.SelectedIds.SequenceEqual(new[] { "easy" }) && home.Lists.All(l => l.Id != Bee.TrickyId));
        Snap("bee-start");
        home.Toggle("medium"); home.Toggle("medium");
        home.SetHowMany(10);
        Bee.Me.Shuffle = false;
        Check("choices are kept for the player", Bee.Me.HowMany == 10 && Bee.Me.Lists.SequenceEqual(new[] { "easy" }) && File.ReadAllText(Path.Combine(data, "players.json")).Contains("\"Sam\""));
        home.Toggle("easy"); Pump();
        Check("nothing ticked: Start can't go", !home.StartButton.IsEnabled && !home.Start() && mv.MessageOpen);
        mv.PressMessage("OK");
        home.Toggle("easy"); Pump();
        Check("Start", home.Start());
        Pump();
        var pp = mv.Page as BeePracticePage;
        Check("the practice page: 10 words from Easy Words in order", pp != null && pp.Run.Words.Count == 10 && pp.Run.Current.Word == "cabin");
        if (pp == null) return;
        Check("the word is said by itself", Bee.LastSaid == "cabin", Bee.LastSaid);
        pp.SayWord(true);
        Check("Say it slowly", Bee.LastSaid == "cabin");
        Snap("bee-practice");
        pp.Hint(); pp.Hint(); pp.Hint(); pp.Hint();
        Check("Hint three times: blanks, the first letter, the last", pp.HintShown == "c _ _ _ n");
        Snap("bee-hint");

        // 1. cabin: right the first time (typed with a capital), Enter goes on
        pp.Type("Cabin"); pp.OnKey(Enter); Pump();
        Check("right the first time: Correct!, counted right", pp.Screen == BeeScreen.Right && pp.Run.Good == 1 && pp.Feedback == "Correct!");
        Snap("bee-right");
        pp.OnKey(Enter); Pump();
        Check("Enter goes on to the next word", pp.Run.Index == 1 && pp.Screen == BeeScreen.Answering && Bee.LastSaid == "carve");

        // 2. carve: wrong, then right
        pp.Type("carv"); pp.Check(); Pump();
        Check("a first miss: Not quite, Try again", pp.Screen == BeeScreen.Retry && Bee.LastSaid == "Not quite. Try again." && pp.Run.Bad == 0);
        Snap("bee-not-quite");
        pp.TryAgain(); Pump();
        Check("Try again says the word again", pp.Screen == BeeScreen.Answering && Bee.LastSaid == "carve");
        pp.Type("carve"); pp.Check(); Pump();
        Check("right the second time: You got it!, counted missed, in Tricky Words", pp.Screen == BeeScreen.Right && pp.Feedback == "You got it!" && pp.Run.Bad == 1 && Bee.Me.Tricky.Contains("carve"));
        pp.Next(); Pump();

        // 3. earth: I don't know
        pp.ShowAnswer(); Pump();
        Check("I don't know: the answer, spelled out loud", pp.Screen == BeeScreen.Shown && Bee.LastSaid == "The word earth is spelled: E, A, R, T, H." && Bee.Me.Tricky.Contains("earth"));
        pp.SpellItForMe();
        Check("Spell it for me", Bee.LastSaid == "earth. E, A, R, T, H. earth.");
        Snap("bee-shown");
        pp.OnKey(Enter); Pump();

        // 4. half: two misses = the answer with the wrong letters marked
        pp.Type("hlaf"); pp.Check(); pp.TryAgain(); pp.Type("haf"); pp.Check(); pp.Check(); Pump();
        Check("a second miss shows the answer", pp.Screen == BeeScreen.Shown && pp.Run.Bad == 3 && pp.Run.Last.Typed == "haf");
        Snap("bee-shown-marked");
        pp.Next(); Pump();

        // 5. fear: spelled out loud
        rec.Hears = "eff ee ay are";
        pp.Listen().Wait(); Pump();
        Check("spelled out loud: the letters heard, then checked", rec.LetterCalls == 1 && pp.Heard == "I heard: F E A R" && pp.Screen == BeeScreen.Right && pp.Run.Good == 2, pp.Heard);
        Snap("bee-spoken");
        pp.Next(); Pump();
        rec.Hears = null;
        pp.Listen().Wait(); Pump();
        Check("nothing heard: a kind note, still answering", pp.Screen == BeeScreen.Answering && pp.Heard.StartsWith("I didn't hear"));
        pp.Submit("   ");
        Check("an empty answer isn't checked", pp.Screen == BeeScreen.Answering && pp.Run.Tries == 0);

        // stop early: the score so far
        Check("Back asks before stopping", mv.Back() && mv.MessageOpen && mv.Page == pp);
        Snap("bee-stop");
        mv.PressMessage("Stop"); Pump();
        var res = mv.Page as BeeResultsPage;
        Check("the results: 2 of 5, the missed words", res != null && res.Run.Good == 2 && res.Run.Total == 5 && string.Join(",", res.Run.Missed.Select(w => w.Word)) == "carve,earth,half", res == null ? "" : string.Join(",", res.Run.Missed));
        Check("the history keeps it", Bee.Me.History.Count == 1 && Bee.Me.History[0].Right == 2 && Bee.Me.History[0].Total == 5 && Bee.Me.History[0].Lists == "Easy Words");
        Snap("bee-results");
        if (res == null) return;

        // practice the missed ones again, all right the first time: they leave Tricky Words
        res.PracticeAgain(); Pump();
        var again = mv.Page as BeePracticePage;
        Check("Practice these again: just the missed words", again != null && again.Run.Words.Count == 3 && again.Run.Words.All(w => w.Word is "carve" or "earth" or "half"));
        while (again != null && !again.Run.Finished)
        {
            again.Type(again.Run.Current.Word); again.Check(); Pump();
            if (again.Run.IsLast) { Snap("bee-last-right"); }
            again.Next(); Pump();
        }
        var res2 = mv.Page as BeeResultsPage;
        Check("all right: a perfect score", res2 != null && res2.Run.Good == 3 && res2.Message.StartsWith("Perfect") && res2.Run.Missed.Count == 0);
        Check("right the first time: out of Tricky Words", Bee.Me.Tricky.Count == 0, string.Join(",", Bee.Me.Tricky));
        Snap("bee-results-perfect");
        mv.BackTo<BeeHomePage>(); Pump();

        // My Tricky Words: a word missed comes back as a list of its own
        Bee.Me.AddTricky(W("fertilizer|fertiliser")); Bee.Save();
        home.Fill(); Pump();
        Check("My Tricky Words is the first list once there are some", home.Lists[0].Id == Bee.TrickyId && home.Lists[0].Words.Count == 1);
        home.Toggle("easy"); home.Toggle(Bee.TrickyId);
        home.Start(); Pump();
        var tp = mv.Page as BeePracticePage;
        tp?.Type("fertiliser"); tp?.Check(); Pump();
        Check("practicing Tricky Words: the other spelling counts", tp != null && tp.Screen == BeeScreen.Right && Bee.Me.Tricky.Count == 0);
        tp?.Next(); Pump();
        mv.BackTo<BeeHomePage>(); Pump();
        home.Toggle("easy");

        // without speech recognition: the phone tip
        PlatformAudio.Recognizer = null;
        Platform.Touch = true;
        var tip = new BeePracticePage(new BeeRun(new[] { W("meadow") }), "Garden Words");
        mv.Push(tip); Pump();
        Snap("bee-practice-no-mic");
        Platform.Touch = false;
        mv.Back(); Pump();

        // adding lists: a practice page, a .txt, pasted words; renaming and deleting
        var add = new BeeAddListPage(); mv.Push(add); Pump();
        Snap("bee-add");
        add.Import("practice-page.html", File.ReadAllText(Fixture("practice-page.html"))); Pump();
        Check("importing a practice page adds its lists for everyone and ticks them", Bee.Mine.Count == 2 && mv.Page is BeeHomePage && mv.MessageOpen && Bee.Me.Lists.Count(id => id.StartsWith("my-")) == 2);
        Check("imported lists are saved in the lists folder", Directory.GetFiles(Path.Combine(data, "lists"), "*.json").Length == 2);
        mv.PressMessage("OK");
        Snap("bee-start-imported");
        var add2 = new BeeAddListPage(); mv.Push(add2); Pump();
        add2.SetPasted("", "a b"); add2.SavePasted();
        Check("pasted words need a list name", mv.MessageOpen && mv.Page == add2 && Bee.Mine.Count == 2);
        mv.PressMessage("OK");
        add2.SetPasted("Week 7", "lantern\nharbor|harbour, ice cream"); add2.SavePasted(); Pump();
        Check("pasted words become a list", Bee.Mine.Any(l => l.Name == "Week 7" && l.Words.Count == 3));
        mv.PressMessage("OK");
        var week7 = Bee.Mine.First(l => l.Name == "Week 7");
        var lp = new BeeListPage(week7); mv.Push(lp); Pump();
        Snap("bee-list");
        lp.Rename("Week 7 words"); Pump();
        Check("renaming a list", Bee.Mine.Any(l => l.Name == "Week 7 words") && mv.Page is BeeHomePage);
        lp = new BeeListPage(Bee.Mine.First(l => l.Name == "Week 7 words")); mv.Push(lp); Pump();
        lp.Delete(); Pump();
        Check("deleting a list", Bee.Mine.Count == 2 && Bee.Mine.All(l => l.Name != "Week 7 words"));
        var add3 = new BeeAddListPage(); mv.Push(add3); Pump();
        add3.Import("empty.txt", "  ");
        Check("a file with no words: says so, nothing added", mv.MessageOpen && Bee.Mine.Count == 2);
        mv.PressMessage("OK");
        mv.BackTo<BeeHomePage>();

        // another player keeps their own choices; a blocked file is shown and never written over
        mv.PopTo(1); Pump();
        Lg.Me = Profiles.All.First(p => p.Name == "Riley");
        HomePage.Open(HomePage.Find("spelling-bee")); Pump();
        Check("a player chosen: straight to Spelling Bee, with their own settings", mv.Page is BeeHomePage rh && rh.SelectedIds.SequenceEqual(new[] { "easy" }) && Bee.Me.HowMany == 25);
        mv.PopTo(1); Pump();
        string bd = Fresh("pages-blocked");
        File.WriteAllText(Path.Combine(bd, "players.json"), "[ not players");
        Bee.UseFolder(bd);
        mv.Push(new BeeHomePage()); Pump();
        Check("an unreadable save: the person is told, practice still works", mv.MessageOpen && Bee.Players.Blocked);
        Snap("bee-blocked");
        mv.PressMessage("Keep it as it is");
        Bee.Me.AddTricky(W("cabin")); Bee.Save();
        Check("... and it is never written over", File.ReadAllText(Path.Combine(bd, "players.json")) == "[ not players");

        PlatformAudio.Recognizer = null;
        Bee.UseFolder(null);
        Lg.Reset();
        win.Close();
    }
}
