// Learning Lab for Windows - recognising speech for "Say it" exercises, offline only. Windows.Media.SpeechRecognition's
// free-form dictation and topic constraints use Microsoft's online service, so they are never used here: only a list
// constraint (the answer and a few other phrases of the lesson), which Windows checks on this PC with an installed
// speech recognizer for that language (Settings > Time & language > Speech / Language: Spanish with "Speech
// recognition"). Without one, Available is false and lessons ask people to mark themselves.
using LearningLab.Services;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace LearningLab.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
sealed class DesktopRecognizer : IRecognizer
{
    readonly Dictionary<string, Language> found = new(StringComparer.OrdinalIgnoreCase);
    List<Language> installed;

    public static void Install()
    {
        try { PlatformAudio.Recognizer = new DesktopRecognizer(); }
        catch (Exception) { }
    }

    List<Language> Installed()
    {
        if (installed != null) return installed;
        try { installed = SpeechRecognizer.SupportedGrammarLanguages.ToList(); }
        catch (Exception) { installed = new List<Language>(); }
        return installed;
    }

    Language Find(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return null;
        if (found.TryGetValue(lang, out var l)) return l;
        var list = Installed();
        string two = lang.Length >= 2 ? lang.Substring(0, 2) : lang;
        l = list.FirstOrDefault(x => string.Equals(x.LanguageTag, lang, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => x.LanguageTag.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase) && x.LanguageTag.EndsWith("MX", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => x.LanguageTag.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase) || string.Equals(x.LanguageTag, two, StringComparison.OrdinalIgnoreCase));
        found[lang] = l;
        return l;
    }

    public bool Available(string lang) => Find(lang) != null;
    public bool NeedsPermission => false;
    public Task<bool> AskPermission() => Task.FromResult(true);

    public async Task<string> Listen(string lang, double seconds, IReadOnlyList<string> expected)
    {
        var l = Find(lang);
        if (l == null || expected == null || expected.Count == 0) return null;
        try
        {
            using var r = new SpeechRecognizer(l);
            r.Constraints.Add(new SpeechRecognitionListConstraint(expected.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(), "answers"));
            var comp = await r.CompileConstraintsAsync();
            if (comp.Status != SpeechRecognitionResultStatus.Success) return null;
            r.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 10));
            r.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(0.8);
            var res = await r.RecognizeAsync();
            if (res.Status != SpeechRecognitionResultStatus.Success || res.Confidence == SpeechRecognitionConfidence.Rejected) return null;
            return string.IsNullOrWhiteSpace(res.Text) ? null : res.Text;
        }
        catch (Exception) { return null; }   // (no microphone, microphone access off in Windows' privacy settings)
    }

    /// <summary>Spelling Bee: a word spelled letter by letter. A grammar (SRGS, checked on this PC like the list
    /// constraint, never online) of up to 40 letters, "double" + a letter, "capital" + a letter, dash, space and
    /// apostrophe; the text comes back as the letters said ("c a b i n"), which BeeCheck.ParseSpoken reads.</summary>
    public async Task<string> ListenLetters(string lang, double seconds)
    {
        var l = Find(lang);
        if (l == null) return null;
        try
        {
            string tag = l.LanguageTag;
            var letters = string.Concat("abcdefghijklmnopqrstuvwxyz".Select(c => "<item>" + c + "</item>"));
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<grammar version=\"1.0\" xml:lang=\"" + tag + "\" root=\"spelling\" xmlns=\"http://www.w3.org/2001/06/grammar\">"
                + "<rule id=\"spelling\" scope=\"public\"><item repeat=\"1-40\"><ruleref uri=\"#piece\"/></item></rule>"
                + "<rule id=\"piece\"><one-of><item><ruleref uri=\"#letter\"/></item><item>double <ruleref uri=\"#letter\"/></item>"
                + "<item>capital <ruleref uri=\"#letter\"/></item><item>dash</item><item>space</item><item>apostrophe</item></one-of></rule>"
                + "<rule id=\"letter\"><one-of>" + letters + "</one-of></rule></grammar>";
            string file = System.IO.Path.Combine(LearningLab.Data.Cache("SpellingBee"), "letters-" + tag + ".grxml");
            if (!System.IO.File.Exists(file) || System.IO.File.ReadAllText(file) != xml) System.IO.File.WriteAllText(file, xml);
            var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(file);
            using var r = new SpeechRecognizer(l);
            r.Constraints.Add(new SpeechRecognitionGrammarFileConstraint(sf, "letters"));
            var comp = await r.CompileConstraintsAsync();
            if (comp.Status != SpeechRecognitionResultStatus.Success) return null;
            r.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 10));
            r.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.6);
            r.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(20);
            var res = await r.RecognizeAsync();
            if (res.Status != SpeechRecognitionResultStatus.Success || res.Confidence == SpeechRecognitionConfidence.Rejected) return null;
            return string.IsNullOrWhiteSpace(res.Text) ? null : res.Text;
        }
        catch (Exception) { return null; }
    }
}
