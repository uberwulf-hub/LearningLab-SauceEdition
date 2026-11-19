// Learning Lab - soft, subtle feedback (Andrew, 2026-11-13: "haptic feedback and sounds", "soft/subtle sounds"): a
// gentle bell for a right answer, a soft low note for a wrong one, a faint tick for pressing on, each with a short buzz
// on phones (Platform.Haptic; Windows has none). The sounds are made here, quieter and rounder than the games' beeps
// (Learning Games keeps its own), and follow the same on / off switch (LearningGames.Sound.Enabled).
using System.Globalization;

namespace LearningLab.Services;

public static class Feel
{
    /// <summary>A faint tick and a light tap (pressing Next, Continue, a choice).</summary>
    public static void Tap() { Haptic("tap"); Play(Tone((1500, 0, 22)), 0.05); }

    /// <summary>A soft rising bell (two notes) and a short confirm buzz.</summary>
    public static void Right() { Haptic("right"); Play(Tone((880, 0, 260), (1175, 90, 340)), 0.13); }

    /// <summary>A soft, low, falling note and a gentle double buzz.</summary>
    public static void Wrong() { Haptic("wrong"); Play(Tone((330, 0, 240), (262, 110, 300)), 0.11); }

    /// <summary>Finished something: three soft rising notes.</summary>
    public static void Done() { Haptic("right"); Play(Tone((784, 0, 300), (988, 110, 320), (1319, 220, 420)), 0.12); }

    public static void Right(bool right) { if (right) Right(); else Wrong(); }

    /// <summary>The sounds made last (the checks look; nothing is played in them).</summary>
    public static string Last { get; private set; } = "";

    static void Haptic(string kind)
    {
        Last = kind;
        try { Platform.Haptic(kind); } catch (Exception) { }
    }

    static void Play(float[] samples, double volume)
    {
        if (!global::LearningGames.Sound.Enabled) return;
        try { Platform.PlayWav(Wav(samples, volume)); } catch (Exception) { }
    }

    const int Rate = 22050;

    /// <summary>Notes (Hz, start ms, length ms) as soft bells: a quick fade in, then a gentle fall away, sine only.</summary>
    static float[] Tone(params (double Hz, int At, int Ms)[] notes)
    {
        int len = notes.Max(n => (n.At + n.Ms) * Rate / 1000) + Rate / 50;
        var s = new float[len];
        foreach (var n in notes)
        {
            int start = n.At * Rate / 1000, count = n.Ms * Rate / 1000;
            double attack = Rate * 0.008;
            for (int i = 0; i < count && start + i < len; i++)
            {
                double env = Math.Min(1, i / attack) * Math.Exp(-4.0 * i / count) * Math.Min(1, (count - i) / (Rate * 0.01));
                s[start + i] += (float)(Math.Sin(2 * Math.PI * n.Hz * i / Rate) * env);
            }
        }
        return s;
    }

    static byte[] Wav(float[] f, double volume)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = f.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data);
        foreach (float x in f) w.Write((short)Math.Round(Math.Clamp(x * volume, -1, 1) * 32767));
        w.Flush();
        return ms.ToArray();
    }
}
