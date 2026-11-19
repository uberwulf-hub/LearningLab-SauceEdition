// Learning Lab - Languages: turning a lesson into one audio file. Every phrase is spoken once by the device's voice
// (PlatformAudio.Voices) and kept in the cache (<cache>\Languages\voice\<hash>.wav, 24 kHz mono, trimmed), so a lesson
// heard again, or with another style, is made in moments. The lesson's WAV goes to <cache>\Languages\lessons\ with a
// small .marks file (where each item starts, in seconds). Waits are silence; "echo" waits are as long as the phrase
// just said times a factor plus a little, so there is time to repeat it.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LearningLab.Audio;
using LearningLab.Services;

namespace LearningLab.Languages;

public sealed class LessonFile
{
    public string File;
    public double Duration;
    public double[] ItemStarts = Array.Empty<double>();

    /// <summary>Which item is playing at that time (-1 before the first).</summary>
    public int ItemAt(double seconds)
    {
        int k = -1;
        for (int i = 0; i < ItemStarts.Length; i++) if (ItemStarts[i] <= seconds + 0.05) k = i;
        return k;
    }
}

public static class LessonAudio
{
    static readonly SemaphoreSlim gate = new(1, 1);

    static string Hash(string s)
    {
        var h = MD5.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(h, 0, 10).ToLowerInvariant();
    }

    static string PieceFile(string voiceId, double rate, string text)
        => Path.Combine(Data.Cache("Languages"), "voice", Hash(voiceId + "|" + rate.ToString("0.00", CultureInfo.InvariantCulture) + "|" + text) + ".wav");

    /// <summary>One phrase spoken by that voice, as samples (cached). Null when the voice couldn't.</summary>
    public static async Task<short[]> Piece(string text, VoiceInfo voice, double rate)
    {
        var v = PlatformAudio.Voices;
        if (v == null || voice == null) return null;
        string f = PieceFile(voice.Id, rate, text);
        if (File.Exists(f))
        {
            try { var s = Wav.ReadMono(File.ReadAllBytes(f)); if (s != null) return s; } catch (IOException) { }
        }
        byte[] wav;
        await gate.WaitAsync().ConfigureAwait(false);
        try { wav = await v.MakeWav(text, voice.Id, rate).ConfigureAwait(false); }
        finally { gate.Release(); }
        var samples = Wav.Trim(Wav.ReadMono(wav));
        if (samples == null || samples.Length == 0) return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllBytes(f, Wav.Write(samples));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return samples;
    }

    static short[] chime;
    static short[] Chime()
    {
        if (chime != null) return chime;
        var a = Wav.ReadMono(Wav.Tone(659.25, 160, Wav.Rate, 0.18));
        var b = Wav.ReadMono(Wav.Tone(880, 260, Wav.Rate, 0.18));
        var all = new List<short>(a); all.AddRange(new short[Wav.Samples(40)]); all.AddRange(b);
        return chime = all.ToArray();
    }

    /// <summary>Makes (or finds) the lesson's audio. progress gets (done, total) phrases. Throws
    /// <see cref="InvalidOperationException"/> with a plain sentence when a voice is missing or fails.</summary>
    public static async Task<LessonFile> Make(Lesson l, VoiceInfo target, VoiceInfo english, double rate, IProgress<(int Done, int Total)> progress, CancellationToken ct, VoiceInfo second = null)
    {
        if (target == null) throw new InvalidOperationException("There is no Spanish voice on this device yet. Open Voices to add a free one.");
        var key = new StringBuilder();
        key.Append(target.Id).Append('|').Append(english?.Id).Append('|').Append(second?.Id).Append('|').Append(rate.ToString("0.00", CultureInfo.InvariantCulture)).Append('|');
        foreach (var s in l.Steps) key.Append(s).Append('\n');
        string dir = Path.Combine(Data.Cache("Languages"), "lessons");
        Directory.CreateDirectory(dir);
        string h = Hash(key.ToString());
        string file = Path.Combine(dir, h + ".wav"), marks = Path.Combine(dir, h + ".marks");
        var known = Read(file, marks);
        if (known != null) return known;

        var says = l.Steps.Where(s => s.Kind == StepKind.Say).ToList();
        int done = 0, total = says.Count;
        progress?.Report((0, total));
        var outp = new List<short>(Wav.Rate * 60 * 5);
        var starts = new double[l.Items.Count];
        int last = 0;   // samples in the last phrase in the course's language
        foreach (var s in l.Steps)
        {
            ct.ThrowIfCancellationRequested();
            switch (s.Kind)
            {
                case StepKind.Chime: outp.AddRange(Chime()); outp.AddRange(new short[Wav.Samples(500)]); break;
                case StepKind.Item: if (s.Index < starts.Length) starts[s.Index] = Wav.Seconds(outp.Count); break;
                case StepKind.Wait: outp.AddRange(new short[Wav.Samples(s.Ms)]); break;
                case StepKind.Echo: outp.AddRange(new short[(int)(last * s.Factor) + Wav.Samples(s.Ms)]); break;
                case StepKind.Say:
                    {
                        bool en = s.Lang == "en";
                        var voice = en ? english : s.Voice == 1 && second != null ? second : target;
                        short[] p = null;
                        if (voice != null) p = await Piece(s.Text, voice, Math.Round(rate * s.Rate, 2)).ConfigureAwait(false);
                        if (p == null && !en) throw new InvalidOperationException("The voice " + target.Name + " couldn't say \"" + s.Text + "\". Try another voice in Voices.");
                        if (p != null && s.Pitch != 1) p = Wav.PitchShift(p, s.Pitch);
                        if (p != null) { outp.AddRange(p); if (!en) last = p.Length; }
                        progress?.Report((++done, total));
                        break;
                    }
            }
        }
        var bytes = Wav.Write(outp);
        await File.WriteAllBytesAsync(file + ".tmp", bytes, ct).ConfigureAwait(false);
        File.Move(file + ".tmp", file, true);
        File.WriteAllText(marks, Wav.Seconds(outp.Count).ToString("R", CultureInfo.InvariantCulture) + "\n" + string.Join("\n", starts.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
        Prune(dir);
        return new LessonFile { File = file, Duration = Wav.Seconds(outp.Count), ItemStarts = starts };
    }

    static LessonFile Read(string file, string marks)
    {
        try
        {
            if (!File.Exists(file) || !File.Exists(marks)) return null;
            var lines = File.ReadAllLines(marks);
            var nums = lines.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
            return new LessonFile { File = file, Duration = nums[0], ItemStarts = nums.Skip(1).ToArray() };
        }
        catch (Exception) { return null; }
    }

    /// <summary>Keeps the lessons folder under about 300 MB (oldest used first).</summary>
    static void Prune(string dir)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("*.wav").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long sum = 0;
            foreach (var f in files)
            {
                sum += f.Length;
                if (sum > 300L * 1024 * 1024) { f.Delete(); File.Delete(Path.ChangeExtension(f.FullName, ".marks")); }
            }
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------- saying one phrase (the speaker buttons)
    static IPlayer tap;
    static int tapNo;

    /// <summary>Says one phrase with the chosen voice (quietly does nothing without one).</summary>
    public static async void Say(string text, VoiceInfo voice, double rate)
    {
        int me = ++tapNo;
        try
        {
            var p = await Piece(text, voice, Math.Round(rate, 2));
            if (p == null || me != tapNo) return;
            tap?.Dispose();
            tap = null;
            // a new file for every phrase: a player that still holds an older one open (Windows releases them late) can
            // never stop the next phrase from being written; older ones are cleared when they are free
            string dir = Path.Combine(Data.Cache("Languages"), "say");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.wav")) { try { File.Delete(old); } catch (Exception) { } }
            string f = Path.Combine(dir, "say-" + me + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
            File.WriteAllBytes(f, Wav.Write(p));
            tap = PlatformAudio.NewPlayer();
            if (tap != null && tap.Open(f)) tap.Play();
        }
        catch (Exception) { }
    }

    public static void StopSaying() { tapNo++; try { tap?.Dispose(); } catch (Exception) { } tap = null; }
}

/// <summary>Reading aloud through the lesson voices (the voices chosen in Languages > Voices), falling back to another
/// reader for a language with no lesson voice. Windows uses it so Learning Games' Spanish finds the newer voices.</summary>
public sealed class VoiceSpeech : ISpeech
{
    readonly ISpeech fallback;
    public VoiceSpeech(ISpeech fallback) { this.fallback = fallback; }

    public void Speak(string text, string lang) => Speak(text, lang, 1.0);

    public void Speak(string text, string lang, double rate)
    {
        string l = string.IsNullOrEmpty(lang) ? "en" : lang;
        var v = Lx.Voice(l, l == "es" ? "es-MX" : "en-US");
        if (v != null) { fallback?.Stop(); LessonAudio.Say(text, v, rate); }
        else { LessonAudio.StopSaying(); fallback?.Speak(text, lang, rate); }
    }

    public void Stop() { LessonAudio.StopSaying(); fallback?.Stop(); }
}
