// Cyber Range - small puzzle tools (each has a manual page in Courses\rhcsa\man): base32 (the coreutils program, for
// every machine), and on the range's own boxes xorcat (single-byte XOR over hex text), vigenere (the classic letter
// cipher) and morse (dots and dashes). They work only on text inside the simulator. RangeCodes holds the same
// transforms for the challenge files ({{B32:NAME}}, {{XOR:KEY:NAME}}, {{VIG:KEY:NAME}}, {{MORSE:NAME}},
// {{CAESAR:N:NAME}}).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class RangeCodes
    {
        const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Base32(byte[] data)
        {
            var sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b; bits += 8;
                while (bits >= 5) { sb.Append(B32[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(B32[(buffer << (5 - bits)) & 31]);
            while (sb.Length % 8 != 0) sb.Append('=');
            return sb.ToString();
        }

        /// <summary>Base32 text back to bytes (null when it isn't base32).</summary>
        public static byte[] FromBase32(string text)
        {
            var t = Regex.Replace(text ?? "", "\\s", "").TrimEnd('=').ToUpperInvariant();
            var l = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in t)
            {
                int v = B32.IndexOf(c);
                if (v < 0) return null;
                buffer = (buffer << 5) | v; bits += 5;
                if (bits >= 8) { l.Add((byte)((buffer >> (bits - 8)) & 255)); bits -= 8; }
            }
            return l.ToArray();
        }

        public static int Key(string s)
        {
            s = (s ?? "").Trim();
            int k;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out k)) return k;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) return k;
            return -1;
        }

        /// <summary>Text XORed with one byte, written as hex digits.</summary>
        public static string XorHex(string text, int key)
        {
            return string.Concat(Encoding.UTF8.GetBytes(text ?? "").Select(b => ((byte)(b ^ key)).ToString("x2")).ToArray());
        }

        public static byte[] FromHex(string text)
        {
            var t = Regex.Replace(text ?? "", "\\s|0x|\\\\x", "");
            if (t.Length % 2 != 0 || !Regex.IsMatch(t, "^[0-9a-fA-F]*$")) return null;
            var b = new byte[t.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(t.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return b;
        }

        /// <summary>The Vigenere cipher: each letter moves on by the key's next letter (a = 0); other characters stay as
        /// they are and don't use up a key letter. decrypt moves back.</summary>
        public static string Vigenere(string text, string key, bool decrypt)
        {
            var k = (key ?? "").ToLowerInvariant().Where(c => c >= 'a' && c <= 'z').Select(c => c - 'a').ToList();
            if (k.Count == 0) return text;
            var sb = new StringBuilder(); int i = 0;
            foreach (char c in text ?? "")
            {
                bool lo = c >= 'a' && c <= 'z', up = c >= 'A' && c <= 'Z';
                if (!lo && !up) { sb.Append(c); continue; }
                int s = k[i++ % k.Count]; if (decrypt) s = 26 - s;
                char b = lo ? 'a' : 'A';
                sb.Append((char)(b + (c - b + s) % 26));
            }
            return sb.ToString();
        }

        public static string Caesar(string text, int shift)
        {
            shift = ((shift % 26) + 26) % 26;
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c >= 'a' && c <= 'z') sb.Append((char)('a' + (c - 'a' + shift) % 26));
                else if (c >= 'A' && c <= 'Z') sb.Append((char)('A' + (c - 'A' + shift) % 26));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static readonly string[] MorseAlpha = {
            "A.-", "B-...", "C-.-.", "D-..", "E.", "F..-.", "G--.", "H....", "I..", "J.---", "K-.-", "L.-..", "M--", "N-.", "O---", "P.--.", "Q--.-", "R.-.", "S...", "T-",
            "U..-", "V...-", "W.--", "X-..-", "Y-.--", "Z--..", "0-----", "1.----", "2..---", "3...--", "4....-", "5.....", "6-....", "7--...", "8---..", "9----.",
            "{-.--.", "}-.--.-", "_..--.-", "--....-", "..-.-.-", ",--..--", "?..--..", "!-.-.--", "/-..-.", "=-...-", ":---...", "'.----.", "@.--.-." };
        static Dictionary<char, string> toMorse; static Dictionary<string, char> fromMorse;
        static void MorseTables()
        {
            if (toMorse != null) return;
            toMorse = new Dictionary<char, string>(); fromMorse = new Dictionary<string, char>(StringComparer.Ordinal);
            foreach (var e in MorseAlpha) { toMorse[e[0]] = e.Substring(1); if (!fromMorse.ContainsKey(e.Substring(1))) fromMorse[e.Substring(1)] = e[0]; }
        }

        /// <summary>Text as Morse code: letters separated by a space, words by " / ".</summary>
        public static string Morse(string text)
        {
            MorseTables();
            var words = new List<string>();
            foreach (var w in Regex.Split((text ?? "").Trim().ToUpperInvariant(), "\\s+"))
            {
                if (w.Length == 0) continue;
                words.Add(string.Join(" ", w.Where(c => toMorse.ContainsKey(c)).Select(c => toMorse[c]).ToArray()));
            }
            return string.Join(" / ", words.ToArray());
        }

        public static string FromMorse(string code, out bool ok)
        {
            MorseTables(); ok = true;
            var sb = new StringBuilder();
            foreach (var w in Regex.Split((code ?? "").Trim(), "\\s*/\\s*|\\s{3,}"))
            {
                if (sb.Length > 0) sb.Append(' ');
                foreach (var l in Regex.Split(w.Trim(), "\\s+"))
                {
                    if (l.Length == 0) continue;
                    char c; if (fromMorse.TryGetValue(l, out c)) sb.Append(c); else { sb.Append('?'); ok = false; }
                }
            }
            return sb.ToString();
        }
    }

    public static partial class Cmd
    {
        static string InputText(Sh sh, Opts o, Io io, string prog, ref int st)
        {
            return string.Join("", Inputs(sh, o.Args, io, prog, ref st).Select(x => x.Value).ToArray());
        }

        // ------------------------------------------------------------ base32 (coreutils)
        static int Base32(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dw:i", "decode|d,wrap=|w,ignore-garbage|i", "base32");
            if (o.Error != null) { io.E(o.Error); return 1; }
            int st = 0;
            string t = InputText(sh, o, io, "base32", ref st);
            if (o.Has("d"))
            {
                var b = RangeCodes.FromBase32(o.Has("i") ? Regex.Replace(t, "[^A-Za-z2-7=]", "") : t);
                if (b == null) { io.E("base32: invalid input"); return 1; }
                io.P(Encoding.UTF8.GetString(b));
                return st;
            }
            string enc = RangeCodes.Base32(Encoding.UTF8.GetBytes(t));
            int w = o.Has("w") ? int.Parse(o.Get("w"), CultureInfo.InvariantCulture) : 76;
            if (w == 0) io.L(enc); else for (int i = 0; i < enc.Length; i += w) io.L(enc.Substring(i, Math.Min(w, enc.Length - i)));
            return st;
        }

        static string Printable(byte[] b)
        {
            var sb = new StringBuilder();
            foreach (var x in b) sb.Append(x >= 32 && x < 127 || x == 10 || x == 9 ? (char)x : '.');
            return sb.ToString();
        }

        // ------------------------------------------------------------ xorcat (Cyber Range)
        static int XorCat(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "k:eb", "key=|k,encode|e,brute|b", "xorcat");
            if (o.Error != null) { io.E(o.Error); return 1; }
            int st = 0;
            string t = InputText(sh, o, io, "xorcat", ref st);
            if (o.Has("e"))
            {
                int k = RangeCodes.Key(o.Get("k"));
                if (k < 0 || k > 255) { io.E("xorcat: -e needs a key from 0 to 255 (-k 42 or -k 0x2a)"); return 1; }
                io.L(RangeCodes.XorHex(t.TrimEnd('\n'), k));
                return st;
            }
            var data = RangeCodes.FromHex(t);
            if (data == null) { io.E("xorcat: the input is not hex digits (pairs like 4f 2a ...); use -e to turn text into XORed hex"); return 1; }
            if (o.Has("b"))
            {
                int shown = 0;
                for (int k = 1; k < 256; k++)
                {
                    var x = data.Select(b => (byte)(b ^ k)).ToArray();
                    if (x.Any(b => (b < 32 || b > 126) && b != 10 && b != 9)) continue;
                    io.L("key 0x" + k.ToString("x2") + " (" + k.ToString(CultureInfo.InvariantCulture) + "): " + Encoding.ASCII.GetString(x));
                    shown++;
                }
                if (shown == 0) io.L("xorcat: no single-byte key turns this into plain text");
                return st;
            }
            int key = RangeCodes.Key(o.Get("k"));
            if (key < 0 || key > 255) { io.E("xorcat: give a key from 0 to 255 with -k (or try every key with --brute)"); return 1; }
            io.L(Printable(data.Select(b => (byte)(b ^ key)).ToArray()));
            return st;
        }

        // ------------------------------------------------------------ vigenere (Cyber Range)
        static int VigenereCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "k:de", "key=|k,decrypt|d,encrypt|e", "vigenere");
            if (o.Error != null) { io.E(o.Error); return 1; }
            string key = o.Get("k");
            if (string.IsNullOrEmpty(key) || !key.Any(char.IsLetter)) { io.E("vigenere: give the key word with -k WORD"); return 1; }
            int st = 0;
            string t = InputText(sh, o, io, "vigenere", ref st);
            io.P(RangeCodes.Vigenere(t, key, o.Has("d")));
            if (!t.EndsWith("\n", StringComparison.Ordinal)) io.P("\n");
            return st;
        }

        // ------------------------------------------------------------ morse (Cyber Range)
        static int MorseCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "d", "decode|d", "morse");
            if (o.Error != null) { io.E(o.Error); return 1; }
            int st = 0;
            string t = InputText(sh, o, io, "morse", ref st);
            if (o.Has("d"))
            {
                bool ok; string r = RangeCodes.FromMorse(t, out ok);
                io.L(r);
                if (!ok) { io.E("morse: some groups are not Morse letters (shown as ?)"); return 1; }
                return st;
            }
            io.L(RangeCodes.Morse(t));
            return st;
        }
    }
}
