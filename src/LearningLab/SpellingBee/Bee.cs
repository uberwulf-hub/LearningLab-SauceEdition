// Learning Lab - Spelling Bee: what the pages share: the word lists (Learning Lab's own lists in
// Content\SpellingBee\*.js, and the lists imported on this device), each player's settings, tricky words and history
// (<data>\SpellingBee\players.json, by Learning Games player name), importing lists, and the voice.
using Avalonia.Platform;
using LearningLab.LearningGames;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.SpellingBee;

public static class Bee
{
    public const string Honey = "#FBBF24", HoneyDark = "#D97706", HoneySoft = "#3A2E12";
    public const string TrickyId = "tricky";

    // ---------------------------------------------------------------- lists
    static List<BeeList> builtIn;

    /// <summary>The lists that come with the app (each Content\SpellingBee\*.js file, read like an imported page).</summary>
    public static List<BeeList> BuiltIn
    {
        get
        {
            if (builtIn != null) return builtIn;
            var l = new List<BeeList>();
            try
            {
                foreach (var uri in AssetLoader.GetAssets(new Uri("avares://LearningLab/Content/SpellingBee/"), null).OrderBy(u => u.AbsolutePath, StringComparer.Ordinal))
                {
                    if (!uri.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = AssetLoader.Open(uri);
                    using var r = new StreamReader(s);
                    foreach (var x in BeeImport.ParseHtml(r.ReadToEnd())) { x.BuiltIn = true; l.Add(x); }
                }
            }
            catch (Exception) { }
            return builtIn = l;
        }
    }

    static string folder;
    static BeePlayers players;
    static BeeLists store;
    static List<BeeList> mine;

    public static string Folder => folder ?? Data.For("SpellingBee");

    /// <summary>The checks: another folder (and everything read again).</summary>
    public static void UseFolder(string f) { folder = f; players = null; store = null; mine = null; }

    public static BeePlayers Players => players ??= BeePlayers.Load(Path.Combine(Folder, "players.json"));
    public static BeeLists Store => store ??= new BeeLists(Path.Combine(Folder, "lists"));
    /// <summary>The lists imported on this device.</summary>
    public static List<BeeList> Mine => mine ??= Store.Load();
    public static void ReloadMine() { mine = null; }

    /// <summary>Every list: the player's Tricky Words (when there are any), the bundled ones, then this device's.</summary>
    public static List<BeeList> ListsFor(BeePlayer p)
    {
        var all = new List<BeeList>();
        var tricky = p.TrickyWords;
        if (tricky.Count > 0) all.Add(new BeeList { Id = TrickyId, Name = "My Tricky Words", Emoji = "⭐", Note = "Words you missed before", Words = tricky, BuiltIn = true });
        all.AddRange(BuiltIn);
        all.AddRange(Mine);
        return all;
    }

    // ---------------------------------------------------------------- the player
    public static string PlayerName => Lg.Me?.Name ?? "Player";
    public static BeePlayer Me => Players.For(PlayerName);

    /// <summary>The chosen lists (the first bundled list the very first time).</summary>
    public static HashSet<string> Selected(BeePlayer p)
    {
        var l = p.Lists;
        if (l == null) l = BuiltIn.Take(1).Select(x => x.Id).ToList();
        return new HashSet<string>(l);
    }

    public static void Save()
    {
        try { Players.Save(); }
        catch (Exception ex) { MainView.Current?.Message("Not saved", "Your spelling practice couldn't be saved: " + ex.Message); }
    }

    // ---------------------------------------------------------------- importing
    /// <summary>Reads a file's text as word lists: a page like spelling-bee.html (its LISTS), else words.</summary>
    public static List<BeeList> ReadFile(string fileName, string text)
    {
        var lists = BeeImport.ParseHtml(text);
        if (lists.Count == 0)
        {
            string ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
            if (ext is ".html" or ".htm") text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", " ");
            string name = Path.GetFileNameWithoutExtension(fileName ?? "");
            lists = BeeImport.ParseText(text, string.IsNullOrWhiteSpace(name) ? "My words" : name.Replace('_', ' ').Replace('-', ' ').Trim());
        }
        return lists;
    }

    /// <summary>Saves new lists (fresh ids) and chooses them for this player. Returns a sentence for the person.</summary>
    public static string Add(List<BeeList> lists)
    {
        if (lists.Count == 0) return "No words were found there. Put one word on each line, or separate them with spaces or commas.";
        var me = Me;
        var sel = Selected(me);
        var said = new List<string>();
        foreach (var l in lists)
        {
            l.Id = BeeImport.NewId();
            l.BuiltIn = false; l.Json = null; l.SavedFile = null;
            if (string.IsNullOrWhiteSpace(l.Emoji)) l.Emoji = "\U0001F4DD";
            if (string.IsNullOrWhiteSpace(l.Note)) l.Note = "Added " + DateTime.Now.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var w in l.Words) w.ListId = l.Id;
            Store.Save(l);
            sel.Add(l.Id);
            said.Add(l.Name + " (" + l.Words.Count + " word" + (l.Words.Count == 1 ? "" : "s") + ")");
        }
        me.Lists = sel.ToList();
        Save();
        ReloadMine();
        return (lists.Count == 1 ? "Added the list " : "Added " + lists.Count + " lists: ") + string.Join(", ", said) + ". " + (lists.Count == 1 ? "It's" : "They're") + " ticked, ready to practice.";
    }

    // ---------------------------------------------------------------- the voice
    /// <summary>The checks: everything that would have been said, in order.</summary>
    public static readonly List<string> Said = new();
    public static string LastSaid => Said.Count > 0 ? Said[^1] : null;

    /// <summary>Says English text through the device's voice (the Languages English voice when there is one).</summary>
    public static void Say(string text, bool slow = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Said.Add(text);
        if (Said.Count > 200) Said.RemoveAt(0);
        try { Platform.Speech?.Speak(text, "en", slow ? 0.6 : 0.9); } catch (Exception) { }
    }

    public static void Stop() { try { Platform.Speech?.Stop(); } catch (Exception) { } }

    /// <summary>"cabin. C, A, B, I, N. cabin." (Spell it for me).</summary>
    public static string SpellItText(BeeWord w) => w.Word + ". " + BeeCheck.SpellOut(w.Word) + ". " + w.Word + ".";
}
