// Learning Lab for Android - recognising speech for "Say it" exercises, on the phone only. Android 12 (API 31) and later
// have an on-device recognizer (SpeechRecognizer.createOnDeviceSpeechRecognizer: audio stays on the phone); older
// phones only offer recognizers that may send audio to a server, so there Available is false and lessons ask people to
// mark themselves. EXTRA_PREFER_OFFLINE is set as well. The microphone permission (RECORD_AUDIO) is asked only when a
// person taps "Tap and speak", after the page has said why.
using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using LearningLab.Services;

namespace LearningLab.Android;

sealed class AndroidRecognizer : IRecognizer
{
    readonly Context context;
    static TaskCompletionSource<bool> asking;
    const int RequestCode = 4711;

    AndroidRecognizer(Context c) { context = c.ApplicationContext; }

    public static void Install(Context c) { PlatformAudio.Recognizer = new AndroidRecognizer(c); }

    public bool Available(string lang)
    {
        try { return OperatingSystem.IsAndroidVersionAtLeast(31) && SpeechRecognizer.IsOnDeviceRecognitionAvailable(context); }
        catch (Exception) { return false; }
    }

    public bool NeedsPermission => context.CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted;

    public Task<bool> AskPermission()
    {
        if (!NeedsPermission) return Task.FromResult(true);
        var a = MainActivity.Current;
        if (a == null) return Task.FromResult(false);
        asking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.RequestPermissions(new[] { Manifest.Permission.RecordAudio }, RequestCode);
        return asking.Task;
    }

    /// <summary>MainActivity passes Android's answer here.</summary>
    public static void PermissionResult(int requestCode, Permission[] results)
    {
        if (requestCode != RequestCode) return;
        asking?.TrySetResult(results.Length > 0 && results[0] == Permission.Granted);
        asking = null;
    }

    public Task<string> Listen(string lang, double seconds, IReadOnlyList<string> expected) => Run(lang, seconds, 0);

    /// <summary>Spelling Bee: letters said one by one, with longer pauses allowed between them.</summary>
    public Task<string> ListenLetters(string lang, double seconds) => Run(lang, seconds, 2500);

    Task<string> Run(string lang, double seconds, long silenceMs)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || !Available(lang) || NeedsPermission) { done.SetResult(null); return done.Task; }
        var main = new Handler(Looper.MainLooper);
        main.Post(() =>
        {
            SpeechRecognizer r = null;
            try
            {
                r = SpeechRecognizer.CreateOnDeviceSpeechRecognizer(context);
                var listener = new Listener(text => { done.TrySetResult(text); main.Post(() => { try { r?.Destroy(); } catch (Exception) { } }); });
                r.SetRecognitionListener(listener);
                var i = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                i.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                i.PutExtra(RecognizerIntent.ExtraLanguage, lang);
                i.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
                i.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
                if (silenceMs > 0)
                {
                    i.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, silenceMs);
                    i.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, silenceMs);
                }
                r.StartListening(i);
                main.PostDelayed(() => { try { r.StopListening(); } catch (Exception) { } }, (long)(Math.Clamp(seconds, 2, 10) * 1000));
                main.PostDelayed(() => { if (done.TrySetResult(null)) { try { r.Destroy(); } catch (Exception) { } } }, (long)(Math.Clamp(seconds, 2, 10) * 1000) + 4000);
            }
            catch (Exception) { done.TrySetResult(null); try { r?.Destroy(); } catch (Exception) { } }
        });
        return done.Task;
    }

    sealed class Listener : Java.Lang.Object, IRecognitionListener
    {
        readonly Action<string> finish;
        public Listener(Action<string> f) { finish = f; }
        public void OnResults(Bundle results)
        {
            var list = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            finish(list != null && list.Count > 0 ? list[0] : null);
        }
        public void OnError([GeneratedEnum] SpeechRecognizerError error) => finish(null);
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle @params) { }
        public void OnPartialResults(Bundle partialResults) { }
        public void OnReadyForSpeech(Bundle @params) { }
        public void OnRmsChanged(float rmsdB) { }
    }
}
