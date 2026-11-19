// Learning Lab - small WAV helpers for spoken lessons: read what a voice made (any PCM 8/16/24/32-bit or float, mono
// or stereo, any rate), turn it into 24 kHz mono 16-bit, add silence, join pieces and write one WAV file. Everything
// in memory; no codec, no device.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LearningLab.Audio
{
    public static class Wav
    {
        /// <summary>The rate every lesson is made at (mono, 16-bit).</summary>
        public const int Rate = 24000;

        /// <summary>Reads a RIFF WAV into mono samples at <see cref="Rate"/>; null when it isn't a WAV it understands.</summary>
        public static short[] ReadMono(byte[] data)
        {
            if (data == null || data.Length < 44 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE") return null;
            int fmt = 0, channels = 0, rate = 0, bits = 0, pos = 12, dataAt = -1, dataLen = 0;
            while (pos + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4);
                int len = BitConverter.ToInt32(data, pos + 4);
                int body = pos + 8;
                if (id == "fmt " && len >= 16 && body + 16 <= data.Length)
                {
                    fmt = BitConverter.ToUInt16(data, body);
                    channels = BitConverter.ToUInt16(data, body + 2);
                    rate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToUInt16(data, body + 14);
                    if (fmt == 0xFFFE && len >= 26) fmt = BitConverter.ToUInt16(data, body + 24);   // WAVE_FORMAT_EXTENSIBLE: the sub format
                }
                else if (id == "data")
                {
                    dataAt = body;
                    // some voices write the length as 0 or -1 while streaming: take what is there
                    dataLen = len <= 0 || body + (long)len > data.Length ? data.Length - body : len;
                    break;
                }
                if (len < 0) return null;
                pos = body + len + (len & 1);
            }
            if (dataAt < 0 || channels < 1 || rate < 4000 || rate > 192000) return null;
            if (!((fmt == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) || (fmt == 3 && bits == 32))) return null;
            int bytesPer = bits / 8, frame = bytesPer * channels;
            int frames = dataLen / frame;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + i * frame + c * bytesPer;
                    float v;
                    if (fmt == 3) v = BitConverter.ToSingle(data, at);
                    else if (bits == 8) v = (data[at] - 128) / 128f;
                    else if (bits == 16) v = BitConverter.ToInt16(data, at) / 32768f;
                    else if (bits == 24) v = ((data[at] << 8 | data[at + 1] << 16 | data[at + 2] << 24) >> 8) / 8388608f;
                    else v = BitConverter.ToInt32(data, at) / 2147483648f;
                    sum += v;
                }
                mono[i] = sum / channels;
            }
            return ToShorts(Resample(mono, rate, Rate));
        }

        /// <summary>Linear resampling (speech only, so this is plenty).</summary>
        public static float[] Resample(float[] src, int from, int to)
        {
            if (from == to || src.Length == 0) return src;
            int n = (int)((long)src.Length * to / from);
            var dst = new float[n];
            double step = (double)from / to;
            for (int i = 0; i < n; i++)
            {
                double p = i * step;
                int a = (int)p;
                double f = p - a;
                float x = src[Math.Min(a, src.Length - 1)], y = src[Math.Min(a + 1, src.Length - 1)];
                dst[i] = (float)(x + (y - x) * f);
            }
            return dst;
        }

        /// <summary>A cartoon voice: plays the samples faster (factor above 1: higher and quicker) or slower (lower and
        /// slower), like a tape sped up. Factor 1 gives the same samples back.</summary>
        public static short[] PitchShift(short[] s, double factor)
        {
            if (s == null || s.Length == 0 || Math.Abs(factor - 1) < 0.001) return s;
            int n = (int)(s.Length / factor);
            var dst = new short[n];
            for (int i = 0; i < n; i++)
            {
                double p = i * factor;
                int a = (int)p;
                double f = p - a;
                int x = s[Math.Min(a, s.Length - 1)], y = s[Math.Min(a + 1, s.Length - 1)];
                dst[i] = (short)Math.Round(x + (y - x) * f);
            }
            return dst;
        }

        static short[] ToShorts(float[] f)
        {
            var s = new short[f.Length];
            for (int i = 0; i < f.Length; i++) s[i] = (short)Math.Round(Math.Clamp(f[i], -1f, 1f) * 32767f);
            return s;
        }

        /// <summary>Cuts leading and trailing near-silence (voices pad differently), keeping a short fade margin.</summary>
        public static short[] Trim(short[] s, int threshold = 300, int keepMs = 40)
        {
            if (s == null || s.Length == 0) return s;
            int a = 0, b = s.Length - 1;
            while (a < s.Length && Math.Abs((int)s[a]) < threshold) a++;
            while (b > a && Math.Abs((int)s[b]) < threshold) b--;
            if (a >= s.Length) return new short[0];
            int keep = Rate * keepMs / 1000;
            a = Math.Max(0, a - keep); b = Math.Min(s.Length - 1, b + keep);
            var r = new short[b - a + 1];
            Array.Copy(s, a, r, 0, r.Length);
            return r;
        }

        public static int Samples(int ms) { return (int)((long)Rate * ms / 1000); }
        public static double Seconds(int samples) { return samples / (double)Rate; }

        /// <summary>Writes mono 16-bit samples at <see cref="Rate"/> as a WAV file's bytes.</summary>
        public static byte[] Write(IList<short> samples)
        {
            int n = samples.Count;
            var ms = new MemoryStream(44 + n * 2);
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            var buf = new byte[n * 2];
            for (int i = 0; i < n; i++) { short v = samples[i]; buf[2 * i] = (byte)v; buf[2 * i + 1] = (byte)(v >> 8); }
            w.Write(buf);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>A WAV of a plain tone (the checks' pretend voice, and a gentle chime between units).</summary>
        public static byte[] Tone(double hz, int ms, int rate = 22050, double gain = 0.3)
        {
            int n = rate * ms / 1000;
            var s = new short[n];
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1, Math.Min(i, n - i) / (rate * 0.01));
                s[i] = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * gain * env * 32767);
            }
            var ms2 = new MemoryStream();
            var w = new BinaryWriter(ms2);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            foreach (var v in s) w.Write(v);
            w.Flush();
            return ms2.ToArray();
        }
    }
}
