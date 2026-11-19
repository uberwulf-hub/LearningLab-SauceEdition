// Emulator Lab - the text console: 80 x 25 characters fed by the serial port, with a few ANSI escape codes
// (clear screen, cursor position, colours), and the screen renderer that combines it with the framebuffer.
// (Learning Lab: no UI here. The app draws the font, EmulatorLab\ScreenFont.cs, and shows Pixels in a WriteableBitmap.)
using System;
using System.Text;

namespace EmulatorLab
{
    public sealed class Terminal
    {
        public const int Cols = 80, Rows = 25;
        public readonly byte[] Chars = new byte[Cols * Rows];
        public readonly byte[] Attr = new byte[Cols * Rows];   // low nibble: text colour, high nibble: background
        public int CursorX, CursorY;
        public byte Color = 0x07;
        public readonly object Sync = new object();
        public volatile bool Dirty = true;
        public bool CursorVisible = true;

        readonly StringBuilder transcript = new StringBuilder();
        int escState;          // 0 = normal, 1 = after ESC, 2 = in ESC [ ... sequence
        readonly StringBuilder escArgs = new StringBuilder();

        public Terminal() { Clear(); }

        public void Clear()
        {
            lock (Sync)
            {
                for (int i = 0; i < Chars.Length; i++) { Chars[i] = 32; Attr[i] = Color; }
                CursorX = 0; CursorY = 0; Dirty = true;
            }
        }
        public void Reset() { lock (Sync) { Color = 0x07; escState = 0; CursorVisible = true; } Clear(); }

        /// <summary>Everything written so far (newest 200 000 characters), for tests.</summary>
        public string Transcript { get { lock (Sync) return transcript.ToString(); } }
        public void ClearTranscript() { lock (Sync) transcript.Length = 0; }

        /// <summary>The visible screen as text, one line per row.</summary>
        public string ScreenText()
        {
            lock (Sync)
            {
                var sb = new StringBuilder();
                for (int r = 0; r < Rows; r++)
                {
                    var line = new StringBuilder();
                    for (int c = 0; c < Cols; c++) { byte b = Chars[r * Cols + c]; line.Append(b >= 32 && b < 127 ? (char)b : ' '); }
                    sb.AppendLine(line.ToString().TrimEnd());
                }
                return sb.ToString();
            }
        }

        void Scroll()
        {
            Array.Copy(Chars, Cols, Chars, 0, Cols * (Rows - 1));
            Array.Copy(Attr, Cols, Attr, 0, Cols * (Rows - 1));
            for (int i = Cols * (Rows - 1); i < Cols * Rows; i++) { Chars[i] = 32; Attr[i] = Color; }
        }

        void NewLine()
        {
            CursorX = 0;
            if (++CursorY >= Rows) { CursorY = Rows - 1; Scroll(); }
        }

        public void Put(byte b)
        {
            lock (Sync)
            {
                Dirty = true;
                if (transcript.Length > 200000) transcript.Remove(0, 100000);
                if (escState == 0 && b != 27 && b != 13) transcript.Append(b >= 32 && b < 127 || b == 10 || b == 8 ? (char)b : '?');
                if (escState == 1)
                {
                    if (b == '[') { escState = 2; escArgs.Length = 0; } else escState = 0;
                    return;
                }
                if (escState == 2)
                {
                    if ((b >= '0' && b <= '9') || b == ';' || b == '?') { if (escArgs.Length < 32) escArgs.Append((char)b); return; }
                    escState = 0;
                    Escape((char)b, escArgs.ToString());
                    return;
                }
                switch (b)
                {
                    case 27: escState = 1; return;
                    case 10: NewLine(); return;
                    case 13: CursorX = 0; return;
                    case 8: if (CursorX > 0) CursorX--; return;
                    case 9: do { PutChar(32); } while ((CursorX & 7) != 0 && CursorX != 0); return;
                    case 12: for (int i = 0; i < Chars.Length; i++) { Chars[i] = 32; Attr[i] = Color; } CursorX = 0; CursorY = 0; return;
                    case 7: return;
                    default: PutChar(b); return;
                }
            }
        }

        void PutChar(byte b)
        {
            if (CursorX >= Cols) NewLine();
            int i = CursorY * Cols + CursorX;
            Chars[i] = b; Attr[i] = Color;
            CursorX++;
        }

        static readonly int[] AnsiToPc = { 0, 4, 2, 6, 1, 5, 3, 7 };   // ANSI colour order -> PC colour order

        void Escape(char cmd, string args)
        {
            string[] parts = args.Split(';');
            Func<int, int, int> arg = (k, def) => { int v; return k < parts.Length && int.TryParse(parts[k], out v) ? v : def; };
            switch (cmd)
            {
                case 'J':
                    if (arg(0, 0) == 2) { for (int i = 0; i < Chars.Length; i++) { Chars[i] = 32; Attr[i] = Color; } CursorX = 0; CursorY = 0; }
                    else if (arg(0, 0) == 0) { for (int i = CursorY * Cols + CursorX; i < Chars.Length; i++) { Chars[i] = 32; Attr[i] = Color; } }
                    break;
                case 'K':
                    for (int c = CursorX; c < Cols; c++) { Chars[CursorY * Cols + c] = 32; Attr[CursorY * Cols + c] = Color; }
                    break;
                case 'H': case 'f':
                    CursorY = Math.Max(0, Math.Min(Rows - 1, arg(0, 1) - 1));
                    CursorX = Math.Max(0, Math.Min(Cols - 1, arg(1, 1) - 1));
                    break;
                case 'A': CursorY = Math.Max(0, CursorY - Math.Max(1, arg(0, 1))); break;
                case 'B': CursorY = Math.Min(Rows - 1, CursorY + Math.Max(1, arg(0, 1))); break;
                case 'C': CursorX = Math.Min(Cols - 1, CursorX + Math.Max(1, arg(0, 1))); break;
                case 'D': CursorX = Math.Max(0, CursorX - Math.Max(1, arg(0, 1))); break;
                case 'h': if (args == "?25") CursorVisible = true; break;
                case 'l': if (args == "?25") CursorVisible = false; break;
                case 'm':
                    for (int k = 0; k < parts.Length; k++)
                    {
                        int v = arg(k, 0);
                        int fg = Color & 15, bg = Color >> 4;
                        if (v == 0) { fg = 7; bg = 0; }
                        else if (v == 1) fg |= 8;
                        else if (v == 7) { int t = fg; fg = bg; bg = t; }
                        else if (v >= 30 && v <= 37) fg = AnsiToPc[v - 30] | (fg & 8);
                        else if (v >= 90 && v <= 97) fg = AnsiToPc[v - 90] | 8;
                        else if (v >= 40 && v <= 47) bg = AnsiToPc[v - 40];
                        else if (v >= 100 && v <= 107) bg = AnsiToPc[v - 100] | 8;
                        else if (v == 39) fg = 7;
                        else if (v == 49) bg = 0;
                        Color = (byte)((bg << 4) | fg);
                    }
                    break;
            }
        }
    }

    /// <summary>Draws the 640 x 400 screen: the framebuffer (each pixel doubled) and/or the text console (8 x 16
    /// character cells), coloured with the video palette, like an old PC's VGA card.</summary>
    public sealed class Display
    {
        public const int W = 640, H = 400;
        public readonly int[] Pixels = new int[W * H];
        static byte[][] glyphs;     // 256 glyphs, 8 x 16 coverage values (0-255)

        /// <summary>The screen mode chosen in the window: -1 = what the machine says, 0 = text, 1 = graphics, 2 = both.</summary>
        public int ModeOverride = -1;
        public bool Scanlines = true;

        public Display()
        {
            if (glyphs == null) glyphs = WithBlocks(null);
        }

        /// <summary>The character a code shows (CP437 above 127), or null for codes with no picture.</summary>
        public static string CharFor(int c)
        {
            if (c < 33 || c == 127) return null;
            return c < 127 ? ((char)c).ToString() : Cp437High[c - 128].ToString();
        }

        /// <summary>Sets the font: 256 glyphs of 8 x 16 coverage values (0-255), drawn by the app from a monospaced
        /// font once per process. The block characters are replaced with exact shapes so they join up.</summary>
        public static void SetFont(byte[][] font) { glyphs = WithBlocks(font); }

        /// <summary>True once the app has given the letters (without them only the block characters show).</summary>
        public static bool HasFont { get { return glyphs != null && Array.Exists(glyphs['A'], b => b != 0); } }

        // CP437 (the original PC character set) for codes 128-255, so programs can draw boxes and blocks
        public const string Cp437High =
            "\u00C7\u00FC\u00E9\u00E2\u00E4\u00E0\u00E5\u00E7\u00EA\u00EB\u00E8\u00EF\u00EE\u00EC\u00C4\u00C5" +
            "\u00C9\u00E6\u00C6\u00F4\u00F6\u00F2\u00FB\u00F9\u00FF\u00D6\u00DC\u00A2\u00A3\u00A5\u20A7\u0192" +
            "\u00E1\u00ED\u00F3\u00FA\u00F1\u00D1\u00AA\u00BA\u00BF\u2310\u00AC\u00BD\u00BC\u00A1\u00AB\u00BB" +
            "\u2591\u2592\u2593\u2502\u2524\u2561\u2562\u2556\u2555\u2563\u2551\u2557\u255D\u255C\u255B\u2510" +
            "\u2514\u2534\u252C\u251C\u2500\u253C\u255E\u255F\u255A\u2554\u2569\u2566\u2560\u2550\u256C\u2567" +
            "\u2568\u2564\u2565\u2559\u2558\u2552\u2553\u256B\u256A\u2518\u250C\u2588\u2584\u258C\u2590\u2580" +
            "\u03B1\u00DF\u0393\u03C0\u03A3\u03C3\u00B5\u03C4\u03A6\u0398\u03A9\u03B4\u221E\u03C6\u03B5\u2229" +
            "\u2261\u00B1\u2265\u2264\u2320\u2321\u00F7\u2248\u00B0\u2219\u00B7\u221A\u207F\u00B2\u25A0\u00A0";

        /// <summary>The app's glyphs (or none) with the block characters drawn exactly.</summary>
        static byte[][] WithBlocks(byte[][] font)
        {
            var g = new byte[256][];
            for (int c = 0; c < 256; c++) g[c] = font != null && font[c] != null && font[c].Length == 128 ? (byte[])font[c].Clone() : new byte[128];
            // exact block characters
            Func<Func<int, int, bool>, byte[]> shape = f => { var b = new byte[128]; for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++) b[y * 8 + x] = f(x, y) ? (byte)255 : (byte)0; return b; };
            g[0xDB] = shape((x, y) => true);
            g[0xDC] = shape((x, y) => y >= 8);
            g[0xDF] = shape((x, y) => y < 8);
            g[0xDD] = shape((x, y) => x < 4);
            g[0xDE] = shape((x, y) => x >= 4);
            g[0xB0] = shape((x, y) => (x + y * 2) % 4 == 0 && y % 2 == 0);
            g[0xB1] = shape((x, y) => (x + y) % 2 == 0);
            g[0xB2] = shape((x, y) => !((x + y * 2) % 4 == 0 && y % 2 == 0));
            g[0xFE] = shape((x, y) => x >= 1 && x <= 6 && y >= 5 && y <= 11);
            g[0xC4] = shape((x, y) => y == 7 || y == 8);
            g[0xB3] = shape((x, y) => x == 3 || x == 4);
            g[0xCD] = shape((x, y) => y == 6 || y == 9);
            g[0xBA] = shape((x, y) => x == 2 || x == 5);
            g[0] = new byte[128]; g[32] = new byte[128];
            return g;
        }

        /// <summary>Which mode is shown: the window's choice, or the machine's.</summary>
        public int EffectiveMode(Machine m) { return ModeOverride >= 0 ? ModeOverride : (int)m.Video.Mode; }

        /// <summary>Draws everything into <see cref="Pixels"/>. Cheap enough for 60 frames a second.</summary>
        public void Render(Machine m, Terminal t, bool cursorBlinkOn, bool poweredOff)
        {
            int[] px = Pixels;
            if (poweredOff)
            {
                Array.Clear(px, 0, px.Length);
                return;
            }
            uint[] pal = m.Video.Palette;
            int mode = EffectiveMode(m);
            bool gfx = mode != 0, text = mode != 1;
            if (gfx)
            {
                byte[] fb = m.Video.Fb;
                for (int y = 0; y < 200; y++)
                {
                    int src = y * 320, dst = y * 2 * W;
                    for (int x = 0; x < 320; x++)
                    {
                        int c = (int)pal[fb[src + x]];
                        px[dst] = c; px[dst + 1] = c;
                        dst += 2;
                    }
                    Array.Copy(px, y * 2 * W, px, (y * 2 + 1) * W, W);
                }
            }
            else Array.Clear(px, 0, px.Length);
            if (text)
            {
                lock (t.Sync)
                {
                    for (int r = 0; r < Terminal.Rows; r++)
                        for (int c = 0; c < Terminal.Cols; c++)
                        {
                            int i = r * Terminal.Cols + c;
                            byte ch = t.Chars[i], at = t.Attr[i];
                            int bgIdx = at >> 4;
                            bool cursor = cursorBlinkOn && t.CursorVisible && r == t.CursorY && c == t.CursorX;
                            if (gfx && bgIdx == 0 && (ch == 32 || ch == 0) && !cursor) continue;   // see-through over graphics
                            DrawCell(px, c * 8, r * 16, glyphs[ch], (int)pal[at & 15], (int)pal[bgIdx], gfx && bgIdx == 0);
                            if (cursor)
                            {
                                int col = (int)pal[at & 15];
                                for (int y = 13; y < 15; y++) for (int x = 0; x < 8; x++) px[(r * 16 + y) * W + c * 8 + x] = col;
                            }
                        }
                }
            }
        }

        static void DrawCell(int[] px, int x0, int y0, byte[] g, int fg, int bg, bool transparentBg)
        {
            int fr = (fg >> 16) & 255, fgc = (fg >> 8) & 255, fb = fg & 255;
            for (int y = 0; y < 16; y++)
            {
                int row = (y0 + y) * W + x0;
                for (int x = 0; x < 8; x++)
                {
                    int a = g[y * 8 + x];
                    int under = transparentBg ? px[row + x] : bg;
                    if (a == 0) { px[row + x] = under; continue; }
                    if (a == 255) { px[row + x] = fg; continue; }
                    int ur = (under >> 16) & 255, ug = (under >> 8) & 255, ub = under & 255;
                    px[row + x] = (((ur * (255 - a) + fr * a) / 255) << 16) | (((ug * (255 - a) + fgc * a) / 255) << 8) | ((ub * (255 - a) + fb * a) / 255);
                }
            }
        }

    }
}
