// Learning Lab - what differs between Windows and Android: playing a sound, reading aloud, opening a file or web
// address. Each platform's project fills these in at start (LearningLab.Desktop\DesktopPlatform.cs,
// LearningLab.Android\AndroidPlatform.cs); the checks leave the quiet defaults, so they never play audio or open anything.
namespace LearningLab.Services;

public interface ISpeech
{
    /// <summary>Reads the text aloud, stopping anything it was reading. lang: "" or "es" (a Spanish voice when there is one).</summary>
    void Speak(string text, string lang);
    /// <summary>Reads the text aloud at a speed (1 = normal, 0.6 = slowly; Spelling Bee). lang "en" asks for an English voice.</summary>
    void Speak(string text, string lang, double rate) => Speak(text, lang);
    void Stop();
}

public static class Platform
{
    /// <summary>Plays a short 16-bit mono WAV in the background (sound effects made on the fly).</summary>
    public static Action<byte[]> PlayWav { get; set; } = _ => { };

    /// <summary>A short buzz on phones: "tap", "right" or "wrong" (Windows: nothing).</summary>
    public static Action<string> Haptic { get; set; } = _ => { };

    /// <summary>Reading aloud, or null when this device has no voice.</summary>
    public static ISpeech Speech { get; set; }

    /// <summary>Opens a web address or a local file in the device's browser; false when it couldn't.</summary>
    public static Func<string, bool> Open { get; set; } = _ => false;

    /// <summary>True on a phone or tablet (touch first: bigger buttons, no keyboard hints).</summary>
    public static bool Touch { get; set; }

    /// <summary>A folder where files that ship with the app (the block games) are unpacked, or null.</summary>
    public static string FilesFolder { get; set; }
}

/// <summary>A voice on this device. Lang is a tag such as "es-MX" or "en-US".</summary>
public sealed record VoiceInfo(string Id, string Name, string Lang, string Quality);

/// <summary>Making speech into audio (for spoken lessons that also play with the screen off and can be saved).</summary>
public interface IVoices
{
    /// <summary>The voices that work offline (may be empty for a moment while the device's speech engine starts).</summary>
    IReadOnlyList<VoiceInfo> All { get; }
    /// <summary>Speaks the text with that voice into a WAV file's bytes; null when it couldn't.</summary>
    Task<byte[]> MakeWav(string text, string voiceId, double rate);
    /// <summary>Plain words: how to add free voices on this kind of device.</summary>
    string HowToGetVoices { get; }
    /// <summary>Opens the device's own voice settings, when it can (false when it can't).</summary>
    bool OpenVoiceSettings();
}

/// <summary>Plays one audio file with pause and seek; keeps playing with the screen off where the device allows.</summary>
public interface IPlayer : IDisposable
{
    bool Open(string file);
    void Play();
    void Pause();
    bool Playing { get; }
    /// <summary>Seconds.</summary>
    double Position { get; set; }
    double Duration { get; }
    /// <summary>Raised (on any thread) when the file played to its end.</summary>
    event Action Ended;
}

public static partial class PlatformAudio
{
    /// <summary>Voices for lessons, or null on a device without speech.</summary>
    public static IVoices Voices { get; set; }
    /// <summary>Makes a player (null: no audio, as in the checks).</summary>
    public static Func<IPlayer> NewPlayer { get; set; } = () => null;
    /// <summary>Saves a lesson's audio where music players find it; returns a sentence saying where, or why not.</summary>
    public static Func<string, string, string> SaveAudio { get; set; }
}

/// <summary>Recognising speech for "Say it" exercises, on the device only (nothing is sent anywhere). A platform offers
/// it only where it can recognise that language offline; everywhere else Available is false and the lesson asks people
/// to say it out loud and mark themselves.</summary>
public interface IRecognizer
{
    /// <summary>True when this device can recognise speech in that language (e.g. "es-MX") without the network.</summary>
    bool Available(string lang);
    /// <summary>True when the device must ask for the microphone first (Android's permission).</summary>
    bool NeedsPermission { get; }
    /// <summary>Asks for the microphone (the page explains why first); true when allowed.</summary>
    Task<bool> AskPermission();
    /// <summary>Listens for up to that many seconds; what was heard, or null (nothing heard, or it couldn't).
    /// expected: the answer and a few other phrases, for recognisers that pick from a list.</summary>
    Task<string> Listen(string lang, double seconds, IReadOnlyList<string> expected);
    /// <summary>Listens to someone spelling a word letter by letter (Spelling Bee): what was heard, as the recognizer
    /// wrote it ("c a b i n", "see ay bee"), or null. A recognizer that picks from a list gets the letters' names.</summary>
    Task<string> ListenLetters(string lang, double seconds) => Listen(lang, seconds, LearningLab.SpellingBee.BeeCheck.SpokenVocabulary());
}

public static partial class PlatformAudio
{
    /// <summary>Speech recognition on the device, or null (the checks put in a pretend one).</summary>
    public static IRecognizer Recognizer { get; set; }
}
