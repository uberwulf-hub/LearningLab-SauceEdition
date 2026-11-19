// Cert Lab - copy and paste (the WPF-free part of App Hub's Clip.cs): the clipboard behind an interface. The app
// sets ClipText.Current to the platform's clipboard; until then, and always in the checks, it is an in-memory one,
// so nothing reaches the real clipboard. SelText, CodeCopy and the text box routing stay with the screens.
using System;

namespace CertLab
{
    public interface IClipboard
    {
        string GetText();          // null when the clipboard has no text
        void SetText(string text);
    }

    /// <summary>The clipboard of -Snapshot and -Test runs: nothing reaches Windows.</summary>
    public class MemoryClipboard : IClipboard
    {
        public string Text;
        public int Sets;
        public string GetText() { return Text; }
        public void SetText(string text) { Text = text; Sets++; }
    }

    public static class ClipText
    {
        public static IClipboard Current = new MemoryClipboard();
        /// <summary>-Snapshot and -Test: an in-memory clipboard from here on.</summary>
        public static MemoryClipboard UseMemory()
        {
            var m = Current as MemoryClipboard;
            if (m == null) { m = new MemoryClipboard(); Current = m; }
            return m;
        }
        /// <summary>Puts text on the clipboard (Windows line ends); false if the clipboard stayed busy.</summary>
        public static bool SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try { Current.SetText(Lf(text).Replace("\n", "\r\n")); return true; }
            catch (Exception) { return false; }
        }
        /// <summary>The clipboard's text with LF line ends, or null.</summary>
        public static string GetText()
        {
            string t;
            try { t = Current.GetText(); } catch (Exception) { return null; }
            return t == null ? null : Lf(t);
        }
        public static string Lf(string s) { return (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n"); }
    }
}
