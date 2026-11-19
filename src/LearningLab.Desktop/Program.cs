using Avalonia;

namespace LearningLab.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) DesktopPlatform.Install();
        // a developer's check, no window and no sound: lists the voices and writes one spoken phrase to a WAV file
        if (args.Length == 2 && args[0] == "--check-voices") { Environment.Exit(CheckVoices(args[1])); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    static int CheckVoices(string wavFile)
    {
        var v = LearningLab.Services.PlatformAudio.Voices;
        using var log = new StreamWriter(wavFile + ".txt");
        if (v == null) { log.WriteLine("no voices"); return 1; }
        foreach (var x in v.All) log.WriteLine(x.Lang + " | " + x.Name + " | " + x.Id);
        var first = v.All.FirstOrDefault(x => x.Lang.StartsWith("es")) ?? v.All[0];
        var wav = v.MakeWav(first.Lang.StartsWith("es") ? "Hola, ¿cómo estás?" : "Hello, how are you?", first.Id, 0.9).GetAwaiter().GetResult();
        var mono = LearningLab.Audio.Wav.ReadMono(wav);
        log.WriteLine("made " + (wav?.Length ?? 0) + " bytes with " + first.Name + "; read back " + (mono?.Length ?? -1) + " samples at 24 kHz");
        if (wav != null) File.WriteAllBytes(wavFile, wav);
        return mono != null && mono.Length > 0 ? 0 : 2;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
