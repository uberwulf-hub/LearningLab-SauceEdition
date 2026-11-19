// Learning Lab for Android - voices for spoken lessons (the phone's text-to-speech, offline voices only, spoken into
// WAV files with synthesizeToFile), a player for lesson files (MediaPlayer with a partial wake lock, so a lesson keeps
// playing with the screen off) and saving a lesson to Music/Learning Lab (Android 10 and later).
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Speech.Tts;
using LearningLab.Services;

namespace LearningLab.Android;

static class AndroidAudio
{
    public static void Install(Context context)
    {
        var app = context.ApplicationContext;
        PlatformAudio.Voices = new PhoneVoices(app);
        PlatformAudio.NewPlayer = () => new PhonePlayer(app);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) PlatformAudio.SaveAudio = (file, name) => SaveToMusic(app, file, name);
    }

    sealed class PhoneVoices : UtteranceProgressListener, IVoices, TextToSpeech.IOnInitListener
    {
        readonly Context context;
        readonly TextToSpeech tts;
        readonly Dictionary<string, TaskCompletionSource<bool>> waiting = new();
        volatile bool ready;
        IReadOnlyList<VoiceInfo> all;
        int n;

        public PhoneVoices(Context c) { context = c; tts = new TextToSpeech(c, this); }

        public void OnInit(OperationResult status)
        {
            ready = status == OperationResult.Success;
            if (ready) tts.SetOnUtteranceProgressListener(this);
        }

        public IReadOnlyList<VoiceInfo> All
        {
            get
            {
                if (!ready) return Array.Empty<VoiceInfo>();
                if (all != null && all.Count > 0) return all;
                var list = new List<VoiceInfo>();
                try
                {
                    foreach (var v in tts.Voices ?? new List<Voice>())
                    {
                        if (v.IsNetworkConnectionRequired) continue;
                        if (v.Features != null && v.Features.Contains("notInstalled")) continue;
                        var loc = v.Locale;
                        string tag = loc.ToLanguageTag();
                        string q = v.Quality >= VoiceQuality.VeryHigh ? "very high" : v.Quality >= VoiceQuality.High ? "high" : "";
                        list.Add(new VoiceInfo(v.Name, loc.DisplayName + " (" + Short(v.Name) + ")", tag, q));
                    }
                }
                catch (Exception) { }
                return all = list;
            }
        }

        static string Short(string name)
        {
            // "es-us-x-esd-local" -> "esd"
            var parts = name.Split('-');
            int x = Array.IndexOf(parts, "x");
            return x >= 0 && x + 1 < parts.Length ? parts[x + 1] : name;
        }

        public async Task<byte[]> MakeWav(string text, string voiceId, double rate)
        {
            if (!ready) return null;
            var voice = tts.Voices?.FirstOrDefault(v => v.Name == voiceId);
            if (voice == null) return null;
            string id = "ll" + Interlocked.Increment(ref n);
            var file = new Java.IO.File(context.CacheDir, id + ".wav");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (waiting) waiting[id] = tcs;
            try
            {
                tts.SetVoice(voice);
                tts.SetSpeechRate((float)Math.Clamp(rate, 0.5, 2));
                if (tts.SynthesizeToFile(text, new Bundle(), file, id) != OperationResult.Success) return null;
                var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                if (done != tcs.Task || !tcs.Task.Result) return null;
                return File.ReadAllBytes(file.AbsolutePath);
            }
            finally
            {
                lock (waiting) waiting.Remove(id);
                try { file.Delete(); } catch (Exception) { }
            }
        }

        void Finish(string id, bool ok)
        {
            TaskCompletionSource<bool> t;
            lock (waiting) waiting.TryGetValue(id ?? "", out t);
            t?.TrySetResult(ok);
        }

        public override void OnStart(string utteranceId) { }
        public override void OnDone(string utteranceId) => Finish(utteranceId, true);
        [Obsolete] public override void OnError(string utteranceId) => Finish(utteranceId, false);
        public override void OnError(string utteranceId, TextToSpeechError errorCode) => Finish(utteranceId, false);

        public string HowToGetVoices =>
            "Your phone's own voices are free and work offline. Open voice settings below, make sure the preferred engine is " +
            "\"Speech Services by Google\", tap the gear beside it, then \"Install voice data\". Choose Spanish (United States) " +
            "or Spanish (Mexico), download it, and pick the voice you like. Then come back here and choose it.";

        public bool OpenVoiceSettings()
        {
            try
            {
                var i = new Intent("com.android.settings.TTS_SETTINGS");
                i.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(i);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    sealed class PhonePlayer : IPlayer
    {
        readonly Context context;
        MediaPlayer p;
        public event Action Ended;
        public PhonePlayer(Context c) { context = c; }

        public bool Open(string file)
        {
            try
            {
                p = new MediaPlayer();
                p.SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media).SetContentType(AudioContentType.Speech).Build());
                p.SetWakeMode(context, WakeLockFlags.Partial);
                p.SetDataSource(file);
                p.Prepare();
                p.Completion += (s, e) => Ended?.Invoke();
                return true;
            }
            catch (Exception) { Dispose(); return false; }
        }

        public void Play() { try { p?.Start(); } catch (Exception) { } }
        public void Pause() { try { if (p != null && p.IsPlaying) p.Pause(); } catch (Exception) { } }
        public bool Playing { get { try { return p != null && p.IsPlaying; } catch (Exception) { return false; } } }
        public double Position
        {
            get { try { return p == null ? 0 : p.CurrentPosition / 1000.0; } catch (Exception) { return 0; } }
            set { try { p?.SeekTo((int)(Math.Max(0, value) * 1000)); } catch (Exception) { } }
        }
        public double Duration { get { try { return p == null ? 0 : p.Duration / 1000.0; } catch (Exception) { return 0; } } }
        public void Dispose()
        {
            var x = p; p = null;
            if (x == null) return;
            try { if (x.IsPlaying) x.Stop(); } catch (Exception) { }
            try { x.Release(); } catch (Exception) { }
        }
    }

    static string SaveToMusic(Context c, string file, string name)
    {
        foreach (char ch in "/\\:*?\"<>|") name = name.Replace(ch, '-');
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name + ".wav");
        values.Put(MediaStore.IMediaColumns.MimeType, "audio/wav");
        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryMusic + "/Learning Lab");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        var resolver = c.ContentResolver;
        var uri = resolver.Insert(MediaStore.Audio.Media.ExternalContentUri, values);
        if (uri == null) return "It couldn't be saved on this phone.";
        using (var outp = resolver.OpenOutputStream(uri))
        using (var inp = File.OpenRead(file))
            inp.CopyTo(outp);
        values.Clear();
        values.Put(MediaStore.IMediaColumns.IsPending, 0);
        resolver.Update(uri, values, null, null);
        return "Saved in Music > Learning Lab as \"" + name + ".wav\". Any music app can play it, so it also works through the car when Learning Lab is closed.";
    }
}
