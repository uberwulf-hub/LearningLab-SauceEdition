// Learning Lab - Languages: what the pages share: the courses (Content\Languages\<id>.json), this device's progress,
// the chosen voices and speed.
using Avalonia.Platform;
using LearningLab.Services;

namespace LearningLab.Languages;

public static class Lx
{
    public record LanguageInfo(string Id, string Name, string Native, string Flag, string Accent, string Blurb);

    /// <summary>The languages, in order. A language appears once its course file is in Content\Languages.</summary>
    public static readonly LanguageInfo[] Languages =
    {
        new("spanish", "Spanish", "Español", "ES", "#F59E0B", "Latin American Spanish as spoken in Mexico and the US Southwest: from hello to short conversations, with lessons to listen to in the car."),
    };

    static readonly Dictionary<string, LangCourse> courses = new();
    static LangProgress progress;
    static string progressFile;

    public static LangCourse Course(string id)
    {
        if (courses.TryGetValue(id, out var c)) return c;
        var uri = new Uri("avares://LearningLab/Content/Languages/" + id + ".json");
        if (!AssetLoader.Exists(uri)) return null;
        using var s = AssetLoader.Open(uri);
        using var r = new StreamReader(s);
        c = LangCourse.Parse(r.ReadToEnd());
        courses[id] = c;
        return c;
    }

    static readonly Dictionary<string, LangContent> contents = new();
    static string contentFolder;

    /// <summary>The course's lessons, stories and verbs (any may be missing: then the lists are empty).</summary>
    public static LangContent Content(string id)
    {
        if (contents.TryGetValue(id, out var x)) return x;
        var files = new List<KeyValuePair<string, string>>();
        try
        {
            if (contentFolder != null)
            {
                if (Directory.Exists(contentFolder))
                    foreach (var f in Directory.GetFiles(contentFolder, id + "-*.json"))
                        files.Add(new(Path.GetFileName(f), File.ReadAllText(f)));
            }
            else
            {
                foreach (var uri in AssetLoader.GetAssets(new Uri("avares://LearningLab/Content/Languages/"), null))
                {
                    string name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
                    if (!name.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = AssetLoader.Open(uri);
                    using var r = new StreamReader(s);
                    files.Add(new(name, r.ReadToEnd()));
                }
            }
        }
        catch (Exception) { }
        x = LangContent.Load(id, files, Course(id));
        contents[id] = x;
        return x;
    }

    /// <summary>The checks: read lessons, stories and verbs from this folder instead of the app's own (null: the app's).</summary>
    public static void UseContent(string folder) { contentFolder = folder; contents.Clear(); }

    /// <summary>The course id of a loaded course.</summary>
    public static string IdOf(LangCourse c) => courses.FirstOrDefault(kv => kv.Value == c).Key ?? "spanish";

    public static LangProgress Progress
    {
        get
        {
            progress ??= LangProgress.Load(progressFile ?? Path.Combine(Data.For("Languages"), "progress.json"));
            return progress;
        }
    }

    /// <summary>The checks: a progress file of their own.</summary>
    public static void UseFile(string file) { progressFile = file; progress = null; }

    public static void Save()
    {
        try { Progress.Save(); }
        catch (Exception ex) { Views.MainView.Current?.Message("Couldn't save", "Your language progress couldn't be saved: " + ex.Message); }
    }

    // ---------------------------------------------------------------- voices
    public static IReadOnlyList<VoiceInfo> VoicesFor(string lang)
    {
        var all = PlatformAudio.Voices?.All ?? Array.Empty<VoiceInfo>();
        string two = lang.Length >= 2 ? lang.Substring(0, 2) : lang;
        return all.Where(v => v.Lang != null && v.Lang.StartsWith(two, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The voice for a language: the one chosen in Voices if it is still there, else the best match
    /// (the course's own region first, then US, then any; better quality first).</summary>
    public static VoiceInfo Voice(string lang, string preferRegion)
    {
        var list = VoicesFor(lang);
        if (list.Count == 0) return null;
        string chosen = Progress.Setting("voice-" + lang.Substring(0, 2));
        var c = list.FirstOrDefault(v => v.Id == chosen);
        if (c != null) return c;
        int Score(VoiceInfo v)
        {
            int s = 0;
            if (preferRegion != null && string.Equals(v.Lang, preferRegion, StringComparison.OrdinalIgnoreCase)) s += 100;
            else if (v.Lang.EndsWith("US", StringComparison.OrdinalIgnoreCase)) s += 60;
            else if (v.Lang.EndsWith("MX", StringComparison.OrdinalIgnoreCase) || v.Lang.EndsWith("419")) s += 50;
            if (v.Quality == "high" || v.Quality == "natural") s += 20;
            return s;
        }
        return list.OrderByDescending(Score).First();
    }

    public static VoiceInfo Target(LangCourse c) => Voice(c.Voice ?? c.Language, c.Voice);
    public static VoiceInfo English() => Voice("en", "en-US");
    public static double Rate => Progress.Rate;

    public static void Say(LangCourse c, string text) => LessonAudio.Say(text, Target(c), Rate);
    public static void SayEnglish(string text) => LessonAudio.Say(text, English(), Rate);
}
