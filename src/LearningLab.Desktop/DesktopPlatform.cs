// Learning Lab for Windows - sounds (winmm PlaySound from memory), reading aloud (Windows' own voices through SAPI),
// opening files and addresses, and the block games in a Microsoft Edge app window with its own profile.
using System.Diagnostics;
using System.Runtime.InteropServices;
using LearningLab.LearningGames;
using LearningLab.Services;
using Microsoft.Win32;

namespace LearningLab.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
static class DesktopPlatform
{
    [DllImport("winmm.dll", SetLastError = true)]
    static extern bool PlaySound(byte[] sound, IntPtr mod, uint flags);
    const uint SND_SYNC = 0x0000, SND_MEMORY = 0x0004, SND_NODEFAULT = 0x0002;

    public static void Install()
    {
        Platform.PlayWav = wav => ThreadPool.QueueUserWorkItem(_ => { try { PlaySound(wav, IntPtr.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT); } catch (Exception) { } });
        DesktopAudio.Install();
        DesktopRecognizer.Install();
        Platform.Speech = PlatformAudio.Voices != null ? new LearningLab.Languages.VoiceSpeech(new SapiSpeech()) : new SapiSpeech();
        Platform.Open = target => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; } catch (Exception) { return false; } };
        Platform.Touch = false;
        string web = Path.Combine(AppContext.BaseDirectory, "Web");
        if (Directory.Exists(web)) { WebGames.Folder = web; WebGames.Opener = OpenWebGame; }
    }

    /// <summary>Opens a block game in an Edge app window (no tabs or address bar), else the default browser.</summary>
    static string OpenWebGame(string url, string name)
    {
        string edge = EdgePath();
        if (edge == null)
            return Platform.Open(url) ? "" : "Could not open " + name + ". It plays best in Microsoft Edge, which is not on this computer.";
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LearningLabSE", "WebGames");
        try { Directory.CreateDirectory(profile); } catch (Exception) { }
        try
        {
            var psi = new ProcessStartInfo(edge) { UseShellExecute = false };
            psi.ArgumentList.Add("--app=" + url);
            psi.ArgumentList.Add("--user-data-dir=" + profile);
            psi.ArgumentList.Add("--window-size=1280,860");
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            Process.Start(psi);
            return "";
        }
        catch (Exception e) { return "Could not open " + name + ": " + e.Message; }
    }

    static string EdgePath()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
                if (k?.GetValue(null) is string p && File.Exists(p)) return p;
            }
            catch (Exception) { }
        }
        foreach (var p in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        {
            string f = Path.Combine(p, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    /// <summary>Windows' voices (SAPI 5), made on first use; a Spanish voice for "es" when one is installed.</summary>
    sealed class SapiSpeech : ISpeech
    {
        dynamic voice;
        object defaultVoice;
        bool failed;

        dynamic Voice()
        {
            if (voice != null || failed) return voice;
            try
            {
                var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t == null) { failed = true; return null; }
                voice = Activator.CreateInstance(t);
                voice.Rate = -1;
                defaultVoice = voice.Voice;
            }
            catch (Exception) { failed = true; voice = null; }
            return voice;
        }

        public void Speak(string text, string lang) => Speak(text, lang, 1.0);

        public void Speak(string text, string lang, double rate)
        {
            var v = Voice(); if (v == null) return;
            try
            {
                v.Rate = rate < 0.8 ? -5 : -1;   // SAPI: -10 .. 10

                object pick = defaultVoice;
                if (lang == "es")
                {
                    var list = v.GetVoices("Language=c0a;Language=40a;Language=80a", "");
                    if (list.Count == 0) list = v.GetVoices("Language=80a", "");
                    if (list.Count > 0) pick = list.Item(0);
                }
                v.Voice = pick;
                v.Speak(text, 1 | 2);   // asynchronously, stopping what it was saying
            }
            catch (Exception) { }
        }

        public void Stop()
        {
            try { voice?.Speak("", 1 | 2); } catch (Exception) { }
        }
    }
}
