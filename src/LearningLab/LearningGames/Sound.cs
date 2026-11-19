// Learning Games - little sound effects made on the fly (no sound files): short tone sequences turned into in-memory
// WAVs and played by the platform (Services.Platform.PlayWav). The same tones as App Hub's Learning Games.
using System.Globalization;
using LearningLab.Services;

namespace LearningGames
{
    public static class Sound
    {
        public static bool Enabled = true;

        /// <summary>notes: "freqHz:ms" separated by spaces, 0 Hz = silence. Plays in the background.</summary>
        public static void Play(string notes, double volume)
        {
            if (!Enabled) return;
            byte[] wav;
            try { wav = Make(notes, volume); } catch (Exception) { return; }
            try { Platform.PlayWav(wav); } catch (Exception) { }
        }

        public static void Correct() { Play("784:70 1047:110", 0.35); }
        public static void Wrong() { Play("220:120 196:160", 0.3); }
        public static void Win() { Play("523:100 659:100 784:100 1047:260", 0.35); }
        public static void Pop() { Play("660:40", 0.25); }
        public static void Boom() { Play("110:60 90:90", 0.4); }
        public static void Tick() { Play("880:60", 0.25); }

        public const int Rate = 22050;

        public static byte[] Make(string notes, double volume)
        {
            var samples = new List<short>();
            foreach (string n in notes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = n.Split(':');
                double f = double.Parse(p[0], CultureInfo.InvariantCulture);
                int ms = int.Parse(p[1], CultureInfo.InvariantCulture);
                int count = Rate * ms / 1000;
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / Rate;
                    double env = Math.Min(1, Math.Min(i / (Rate * 0.005), (count - i) / (Rate * 0.02)));   // soft attack / release
                    double v = f <= 0 ? 0 : (Math.Sin(2 * Math.PI * f * t) * 0.8 + Math.Sin(4 * Math.PI * f * t) * 0.2) * env * volume;
                    samples.Add((short)(v * short.MaxValue));
                }
            }
            using var ms2 = new MemoryStream();
            using var w = new BinaryWriter(ms2);
            int data = samples.Count * 2;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(data);
            foreach (short s in samples) w.Write(s);
            w.Flush();
            return ms2.ToArray();
        }
    }
}
