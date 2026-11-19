// Network Lab in Learning Lab: the small pieces the engine took from WPF and from the console view (App Hub's
// ConsoleView.cs and System.Windows.Rect), so the engine builds without a UI.
using System;
using System.Collections.Generic;

namespace NetworkLab
{
    /// <summary>What Tab can complete: candidates and where the word starts.</summary>
    public class Completion
    {
        public int Start;
        public List<string> Words = new List<string>();
        /// <summary>Words that don't get a space after them (folders, interface types waiting for a number).</summary>
        public readonly HashSet<string> NoSpace = new HashSet<string>();
    }

    /// <summary>A rectangle, as WPF's System.Windows.Rect behaves for the drawings (Union grows it in place).</summary>
    public struct Rect
    {
        public double X, Y, Width, Height;
        public Rect(double x, double y, double w, double h) { X = x; Y = y; Width = Math.Max(0, w); Height = Math.Max(0, h); }
        public double Left { get { return X; } }
        public double Top { get { return Y; } }
        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }
        public void Union(Rect r)
        {
            double l = Math.Min(Left, r.Left), t = Math.Min(Top, r.Top), rr = Math.Max(Right, r.Right), b = Math.Max(Bottom, r.Bottom);
            X = l; Y = t; Width = rr - l; Height = b - t;
        }
        public bool Contains(double x, double y) { return x >= X && x <= Right && y >= Y && y <= Bottom; }
    }

    /// <summary>Colour helpers the engine needs (App Hub's DrawArt.Lighter / IsColor, without WPF). Colours are #RGB, #RRGGBB or #AARRGGBB.</summary>
    public static class DrawColor
    {
        public static bool TryParse(string hex, out byte a, out byte r, out byte g, out byte b)
        {
            a = 255; r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return false;
            string h = hex.Substring(1);
            if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            if (h.Length != 6 && h.Length != 8) return false;
            uint v;
            if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            if (h.Length == 8) a = (byte)(v >> 24);
            r = (byte)(v >> 16); g = (byte)(v >> 8); b = (byte)v;
            return true;
        }
        public static string Lighter(string hex, double t)
        {
            byte a, r, g, b;
            if (!TryParse(hex, out a, out r, out g, out b)) { r = g = b = 128; }
            Func<byte, int> L = v => (int)Math.Round(v + (255 - v) * t);
            return string.Format("#{0:X2}{1:X2}{2:X2}", L(r), L(g), L(b));
        }
        public static bool IsColor(string c)
        {
            if (c == "none") return true;
            byte a, r, g, b;
            return TryParse(c, out a, out r, out g, out b);
        }
    }
}
