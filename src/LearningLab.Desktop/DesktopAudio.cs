// Learning Lab for Windows - voices for spoken lessons (Windows.Media.SpeechSynthesis: every voice Windows has,
// including the free Spanish ones added in Settings > Time & language > Speech, which the older SAPI can't see), a
// player for lesson files (Windows.Media.Playback) and saving a lesson to Music\Learning Lab.
using System.Diagnostics;
using LearningLab.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace LearningLab.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
static class DesktopAudio
{
    public static void Install()
    {
        try
        {
            var v = new WinVoices();
            if (v.All.Count == 0) return;
            PlatformAudio.Voices = v;
            PlatformAudio.NewPlayer = () => new WinPlayer();
            PlatformAudio.SaveAudio = SaveToMusic;
        }
        catch (Exception) { }   // (no speech on this Windows: lessons say so)
    }

    sealed class WinVoices : IVoices
    {
        readonly SpeechSynthesizer synth = new();
        IReadOnlyList<VoiceInfo> all;

        public IReadOnlyList<VoiceInfo> All
        {
            get
            {
                if (all != null) return all;
                var list = new List<VoiceInfo>();
                foreach (var v in SpeechSynthesizer.AllVoices)
                    list.Add(new VoiceInfo(v.Id, v.DisplayName.Replace("Microsoft ", ""), v.Language, ""));
                return all = list;
            }
        }

        public async Task<byte[]> MakeWav(string text, string voiceId, double rate)
        {
            var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.Id == voiceId);
            if (v == null) return null;
            synth.Voice = v;
            synth.Options.SpeakingRate = Math.Clamp(rate, 0.5, 2);
            using var stream = await synth.SynthesizeTextToStreamAsync(text);
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }

        public string HowToGetVoices =>
            "Windows has free voices for many languages. Open voice settings below (Settings > Time & language > Speech), " +
            "choose \"Add voices\", and add Spanish (Mexico) or Spanish (United States). It downloads in a minute or two. " +
            "If that list stays empty or won't add it: Settings > Time & language > Language & region > Add a language > " +
            "Español (México), and on the next page tick only \"Text-to-speech\" (untick the display language and the rest). " +
            "Then close Learning Lab, open it again and pick the voice here. English voices are already there.";

        public bool OpenVoiceSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true }); return true; }
            catch (Exception) { return false; }
        }
    }

    sealed class WinPlayer : IPlayer
    {
        MediaPlayer p;
        MediaSource src;
        public event Action Ended;

        public bool Open(string file)
        {
            try
            {
                p = new MediaPlayer { AutoPlay = false };
                p.CommandManager.IsEnabled = false;
                src = MediaSource.CreateFromUri(new Uri(file));
                p.Source = src;
                p.MediaEnded += (s, e) => Ended?.Invoke();
                return true;
            }
            catch (Exception) { return false; }
        }

        public void Play() { try { p?.Play(); } catch (Exception) { } }
        public void Pause() { try { p?.Pause(); } catch (Exception) { } }
        public bool Playing { get { try { return p != null && p.PlaybackSession.PlaybackState == MediaPlaybackState.Playing; } catch (Exception) { return false; } } }
        public double Position
        {
            get { try { return p?.PlaybackSession.Position.TotalSeconds ?? 0; } catch (Exception) { return 0; } }
            set { try { if (p != null) p.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, value)); } catch (Exception) { } }
        }
        public double Duration { get { try { return p?.PlaybackSession.NaturalDuration.TotalSeconds ?? 0; } catch (Exception) { return 0; } } }
        public void Dispose()
        {
            var x = p; p = null;
            var s = src; src = null;
            // the source holds the file open until it is disposed (a phrase file left open could not be written again)
            if (x != null) { try { x.Pause(); x.Source = null; x.Dispose(); } catch (Exception) { } }
            if (s != null) { try { s.Dispose(); } catch (Exception) { } }
        }
    }

    static string SaveToMusic(string file, string name)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Learning Lab");
        Directory.CreateDirectory(dir);
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
        string dest = Path.Combine(dir, name + ".wav");
        File.Copy(file, dest, true);
        return "Saved in your Music folder, in Learning Lab, as \"" + name + ".wav\". Copy it to a USB stick or your phone to play it in the car.";
    }
}
