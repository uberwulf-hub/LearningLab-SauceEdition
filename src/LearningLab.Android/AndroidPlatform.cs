// Learning Lab for Android - sounds (AudioTrack from the WAV's samples), reading aloud (the phone's text-to-speech) and
// opening web addresses. The block games (web pages on Windows) aren't offered on the phone yet.
using Android.Content;
using Android.Media;
using Android.Speech.Tts;
using LearningLab.Services;

namespace LearningLab.Android;

static class AndroidPlatform
{
    public static void Install(Context context)
    {
        Platform.Touch = true;
        Platform.PlayWav = PlayWav;
        Platform.Haptic = Haptic;
        Platform.Speech = new PhoneSpeech(context.ApplicationContext);
        AndroidAudio.Install(context);
        AndroidRecognizer.Install(context);
        Platform.Open = url =>
        {
            try
            {
                var i = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
                i.AddFlags(ActivityFlags.NewTask);
                context.ApplicationContext.StartActivity(i);
                return true;
            }
            catch (Exception) { return false; }
        };
    }

    /// <summary>A short buzz through the screen's own haptics (no permission needed; follows the phone's touch
    /// feedback setting): tap = a key tap, right = confirm, wrong = reject (older Androids: a long press).</summary>
    static void Haptic(string kind)
    {
        try
        {
            var view = MainActivity.Current?.Window?.DecorView;
            if (view == null) return;
            bool r = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R;
            var c = kind switch
            {
                "right" => r ? global::Android.Views.FeedbackConstants.Confirm : global::Android.Views.FeedbackConstants.ContextClick,
                "wrong" => r ? global::Android.Views.FeedbackConstants.Reject : global::Android.Views.FeedbackConstants.LongPress,
                _ => global::Android.Views.FeedbackConstants.KeyboardTap,
            };
            view.PerformHapticFeedback(c);
        }
        catch (Exception) { }
    }

    static void PlayWav(byte[] wav)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                const int header = 44;
                int rate = BitConverter.ToInt32(wav, 24);
                int bytes = wav.Length - header;
                if (bytes <= 0) return;
                var pcm = new byte[bytes];
                Buffer.BlockCopy(wav, header, pcm, 0, bytes);
                var attrs = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game).SetContentType(AudioContentType.Sonification).Build();
                var format = new AudioFormat.Builder().SetSampleRate(rate).SetEncoding(Encoding.Pcm16bit).SetChannelMask(ChannelOut.Mono).Build();
                var track = new AudioTrack(attrs, format, bytes, AudioTrackMode.Static, AudioManager.AudioSessionIdGenerate);
                track.Write(pcm, 0, bytes);
                track.Play();
                Thread.Sleep(bytes * 1000 / (rate * 2) + 100);
                track.Release();
            }
            catch (Exception) { }
        });
    }

    sealed class PhoneSpeech : Java.Lang.Object, ISpeech, TextToSpeech.IOnInitListener
    {
        readonly TextToSpeech tts;
        bool ready;
        public PhoneSpeech(Context c) { tts = new TextToSpeech(c, this); }
        public void OnInit(OperationResult status) { ready = status == OperationResult.Success; if (ready) tts.SetSpeechRate(0.9f); }

        public void Speak(string text, string lang) => Speak(text, lang, 1.0);

        public void Speak(string text, string lang, double rate)
        {
            if (!ready || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                tts.SetSpeechRate((float)(0.9 * rate));
                tts.SetLanguage(lang == "es" ? new Java.Util.Locale("es") : lang == "en" ? Java.Util.Locale.Us : Java.Util.Locale.Default);
                tts.Speak(text, QueueMode.Flush, null, "ll");
            }
            catch (Exception) { }
        }

        public void Stop() { try { if (ready) tts.Stop(); } catch (Exception) { } }
    }
}
