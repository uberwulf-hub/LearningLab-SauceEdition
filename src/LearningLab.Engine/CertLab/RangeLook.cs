// Cyber Range - the look as data (the WPF-free part of App Hub's RangeStyle.cs): a style per category (accent colour,
// icon, a plain-words explanation) with sensible defaults for categories added later, the four levels, and the icons' path
// data. The drawing kit (RangeGfx, RangeCard, RangeRing, RangeFx...) stays with the screens and reads these.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    /// <summary>How one category looks. Known ids have hand-picked values; any other id gets an icon from words in its
    /// id or title and a colour from a palette, so a new category looks right without code changes.</summary>
    public class RangeCatStyle
    {
        public string Accent = "#60A5FA", Accent2 = "#818CF8", Icon = "hex", Simple = "";
    }

    public class RangeLevelStyle
    {
        public string Name = "", Color = "#34D399", Purpose = "", Kid = "";
    }

    public static class RangeLook
    {
        static readonly Dictionary<string, RangeCatStyle> known = new Dictionary<string, RangeCatStyle>(StringComparer.OrdinalIgnoreCase)
        {
            { "red", new RangeCatStyle { Accent = "#FB7185", Accent2 = "#F97316", Icon = "target",
                Simple = "You play a security tester who is allowed to look for weak spots before real attackers find them. You start on your own pretend computer, look around the pretend network, find a way in, and collect the flags that prove you got there." } },
            { "defender", new RangeCatStyle { Accent = "#FBBF24", Accent2 = "#F97316", Icon = "eye",
                Simple = "The same kind of break-in as Red Team, but a pretend guard is watching. If you make a lot of noise, like knocking on every door at once, the guard notices and locks you out. Move carefully and quietly." } },
            { "blue", new RangeCatStyle { Accent = "#38BDF8", Accent2 = "#6366F1", Icon = "shield",
                Simple = "This time you are the guard. Someone has been sneaking around a pretend computer. Read its diary (the logs) to work out what they did, answer the questions, and close the doors they left open." } },
            { "web", new RangeCatStyle { Accent = "#F472B6", Accent2 = "#A855F7", Icon = "globe",
                Simple = "A pretend online shop, Copperleaf Market, has a mistake in how its website works. Use the site like a curious visitor - change a number in the address, look at what the page sends - and find what it gives away by accident." } },
            { "soc", new RangeCatStyle { Accent = "#FB923C", Accent2 = "#EAB308", Icon = "bell",
                Simple = "You sit at the security team's desk. Alarms have gone off, and the logs from every computer are collected in one place. Some alarms are real trouble, some are harmless (a person who typed the wrong password, a backup doing its job). Decide which is which, then say what happened." } },
            { "ladder", new RangeCatStyle { Accent = "#4ADE80", Accent2 = "#22D3EE", Icon = "terminal",
                Simple = "Twenty small steps on a pretend Linux computer, for complete beginners. Each step teaches one command. Find the password hidden on that step - it opens the next one - and climb all the way to the top." } },
            { "ctf", new RangeCatStyle { Accent = "#A78BFA", Accent2 = "#EC4899", Icon = "flag",
                Simple = "A short brain-teaser. Something is hidden or scrambled on a pretend computer. Find it or unscramble it, then type the answer into the box." } },
        };

        // icons picked from words in a new category's id or title, so content added later looks right
        static readonly string[][] iconWords =
        {
            new[] { "globe", "web", "site", "http", "browser" },
            new[] { "terminal", "linux", "ladder", "shell", "bash" },
            new[] { "bell", "soc", "alert", "triage", "monitor", "siem" },
            new[] { "cloud", "cloud", "azure", "aws", "bucket" },
            new[] { "key", "crypt", "cipher", "password" },
            new[] { "shield", "defen", "blue", "harden" },
            new[] { "target", "red", "attack", "offens" },
            new[] { "flag", "ctf", "puzzle" },
        };
        static readonly string[][] palette =
        {
            new[] { "#2DD4BF", "#22C55E" }, new[] { "#F472B6", "#A855F7" }, new[] { "#4ADE80", "#14B8A6" },
            new[] { "#FB923C", "#EF4444" }, new[] { "#22D3EE", "#3B82F6" }, new[] { "#E879F9", "#6366F1" },
        };
        static readonly Dictionary<string, string[]> iconColours = new Dictionary<string, string[]>
        {
            { "globe", new[] { "#F472B6", "#A855F7" } }, { "terminal", new[] { "#4ADE80", "#14B8A6" } },
            { "bell", new[] { "#FB923C", "#EF4444" } }, { "cloud", new[] { "#22D3EE", "#3B82F6" } },
        };
        static readonly Dictionary<string, string> iconSimple = new Dictionary<string, string>
        {
            { "globe", "A pretend website has a mistake in it. Poke at it like a curious visitor would and find what it gives away by accident." },
            { "terminal", "Small Linux challenges, one after another. Each one teaches a command or two and hands you what you need for the next." },
            { "bell", "You sit in a pretend security office where alarms keep going off. Decide which alerts are real trouble and which are false alarms, and say what to do." },
            { "cloud", "A pretend company keeps things on rented computers in the cloud and left a setting wrong. Find what is open that should be closed." },
        };

        public static RangeCatStyle For(string id, string title)
        {
            RangeCatStyle s;
            if (known.TryGetValue(id ?? "", out s)) return s;
            string words = ((id ?? "") + " " + (title ?? "")).ToLowerInvariant();
            string icon = "hex";
            foreach (var row in iconWords) if (row.Skip(1).Any(w => words.Contains(w))) { icon = row[0]; break; }
            string[] cols;
            if (!iconColours.TryGetValue(icon, out cols)) cols = palette[(int)((uint)(id ?? "").GetHashCodeStable() % (uint)palette.Length)];
            string simple;
            if (!iconSimple.TryGetValue(icon, out simple)) simple = "Look around the pretend computer, follow the clues and type what you find into the answer box. Nothing here is real, so you can't break anything.";
            return new RangeCatStyle { Accent = cols[0], Accent2 = cols[1], Icon = icon, Simple = simple };
        }
        public static RangeCatStyle For(RangeCategory c) { return c == null ? For("", "") : For(c.Id, c.Title); }
        public static RangeCatStyle ForId(string id) { var c = RangeData.CategoryById(id); return c != null ? For(c) : For(id, id); }

        public static readonly RangeLevelStyle[] Levels =
        {
            new RangeLevelStyle { Name = "Beginner", Color = "#34D399", Purpose = "Learn the basics, one clear step at a time.", Kid = "Start here. Everything is explained." },
            new RangeLevelStyle { Name = "Intermediate", Color = "#38BDF8", Purpose = "Put two or three ideas together.", Kid = "A few steps in a row." },
            new RangeLevelStyle { Name = "Advanced", Color = "#FBBF24", Purpose = "Longer chains and less hand-holding.", Kid = "For when the basics feel easy." },
            new RangeLevelStyle { Name = "Expert", Color = "#F87171", Purpose = "Real-world tricky. Short nudges only.", Kid = "The hardest ones." },
        };
        public static RangeLevelStyle Level(string name) { return Levels[RangeData.LevelIndex(name)]; }

        // ---------------------------------------------------------------- icons (24 x 24 path data, drawn with lines by the screens)
        public static readonly Dictionary<string, string> IconPaths = new Dictionary<string, string>
        {
            { "target", "M12,3 A9,9 0 1 1 11.99,3 Z M12,7.5 A4.5,4.5 0 1 1 11.99,7.5 Z M12,0.5 V5 M12,19 V23.5 M0.5,12 H5 M19,12 H23.5" },
            { "eye", "M1.5,12 C4.5,6 19.5,6 22.5,12 C19.5,18 4.5,18 1.5,12 Z M12,8.6 A3.4,3.4 0 1 1 11.99,8.6 Z" },
            { "shield", "M12,2 L20,5 V11 C20,16.5 16.5,20.5 12,22 C7.5,20.5 4,16.5 4,11 V5 Z M8.3,12 L11,14.6 L15.8,9.4" },
            { "flag", "M5,22 V3 M5,4 H18 L15,8.5 L18,13 H5" },
            { "globe", "M12,2 A10,10 0 1 1 11.99,2 Z M2,12 H22 M12,2 C15.5,5.5 15.5,18.5 12,22 C8.5,18.5 8.5,5.5 12,2 Z" },
            { "terminal", "M3,4.5 H21 V19.5 H3 Z M7,9 L10,12 L7,15 M12.5,15 H17" },
            { "bell", "M6,16.5 V11 C6,7.6 8.7,5 12,5 C15.3,5 18,7.6 18,11 V16.5 L20,18.5 H4 Z M10,21.5 H14 M12,2.5 V5" },
            { "cloud", "M7,19 H17.5 C20,19 22,17 22,14.5 C22,12 20,10.2 17.6,10.3 C16.8,7.3 14.2,5.5 11.3,5.8 C8.3,6.1 6,8.6 6,11.6 C3.8,11.9 2,13.6 2,15.8 C2,17.6 3.6,19 5.5,19 Z" },
            { "key", "M8,10 A5,5 0 1 1 7.99,10 Z M12.5,13 H22 M18.5,13 V16.5 M21.5,13 V15.5" },
            { "hex", "M12,2 L21,7 V17 L12,22 L3,17 V7 Z M12,8 L16,10.3 V14.7 L12,17 L8,14.7 V10.3 Z" },
            { "star", "M12,2.5 L14.9,8.6 L21.5,9.4 L16.6,14 L17.9,20.6 L12,17.3 L6.1,20.6 L7.4,14 L2.5,9.4 L9.1,8.6 Z" },
            { "ghost", "M5,21 V10.5 C5,6.4 8.1,3 12,3 C15.9,3 19,6.4 19,10.5 V21 L16.7,19 L14.3,21 L12,19 L9.7,21 L7.3,19 Z M9.5,10 V11.5 M14.5,10 V11.5" },
            { "layers", "M12,3 L22,8 L12,13 L2,8 Z M2,12 L12,17 L22,12 M2,16 L12,21 L22,16" },
            { "crown", "M3,18 L4.5,7 L9,11 L12,4.5 L15,11 L19.5,7 L21,18 Z M3,21 H21" },
            { "flame", "M12,22 C7.6,22 5,19.2 5,15.6 C5,11.5 8.6,9.6 9,5 C11.6,6.6 12.8,8.8 12.6,11.2 C13.7,10.2 14.3,8.7 14.2,7 C17.2,9 19,12.2 19,15.6 C19,19.2 16.4,22 12,22 Z" },
            { "mountain", "M2,20 L9,7 L13,14.5 L15.5,10.5 L22,20 Z M7.2,10.4 L9,12 L10.6,10.4" },
            { "check", "M5,12.5 L10,17.5 L19.5,7" },
            { "clock", "M12,3 A9,9 0 1 1 11.99,3 Z M12,7 V12 L15.5,14" },
            { "bulb", "M9,18 H15 M10,21 H14 M12,3 C8.7,3 6,5.6 6,8.9 C6,11.2 7.3,12.6 8.5,13.8 C9.2,14.5 9.5,15.2 9.5,16 H14.5 C14.5,15.2 14.8,14.5 15.5,13.8 C16.7,12.6 18,11.2 18,8.9 C18,5.6 15.3,3 12,3 Z" },
            { "lock", "M5,11 H19 V21 H5 Z M8,11 V7.5 C8,5.3 9.8,3.5 12,3.5 C14.2,3.5 16,5.3 16,7.5 V11" },
            { "unlock", "M5,11 H19 V21 H5 Z M8,11 V7.5 C8,5.3 9.8,3.5 12,3.5 C13.8,3.5 15.3,4.7 15.8,6.3" },
            { "trophy", "M7,3.5 H17 V9 C17,11.8 14.8,14 12,14 C9.2,14 7,11.8 7,9 Z M7,5.5 H3.5 C3.5,8.5 5,10 7.3,10.3 M17,5.5 H20.5 C20.5,8.5 19,10 16.7,10.3 M12,14 V18 M8,21 H16 M9,18 H15 V21 H9 Z" },
            { "book", "M4,4.5 C6.5,3.5 9.5,3.5 12,5 C14.5,3.5 17.5,3.5 20,4.5 V19.5 C17.5,18.5 14.5,18.5 12,20 C9.5,18.5 6.5,18.5 4,19.5 Z M12,5 V20" },
            { "map", "M3,6 L9,3.5 L15,6 L21,3.5 V18 L15,20.5 L9,18 L3,20.5 Z M9,3.5 V18 M15,6 V20.5" },
            { "bolt", "M13,2 L4,14 H11 L10,22 L20,9.5 H13 Z" },
            { "server", "M4,4 H20 V10 H4 Z M4,14 H20 V20 H4 Z M7.5,7 H8.5 M7.5,17 H8.5" },
            { "door", "M6,21 V3.5 H18 V21 M3.5,21 H20.5 M14.5,12 H15.5" },
            { "search", "M10.5,3.5 A7,7 0 1 1 10.49,3.5 Z M15.5,15.5 L21,21" },
        };
        public static bool HasIcon(string key) { return key != null && IconPaths.ContainsKey(key); }

        /// <summary>Joins lines a text file wrapped by hand into flowing paragraphs (blank lines and indented lines stay).</summary>
        public static string Unwrap(string text)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (i > 0)
                {
                    string prev = lines[i - 1];
                    bool join = prev.Trim().Length > 0 && l.Trim().Length > 0 && !l.StartsWith(" ", StringComparison.Ordinal) && !l.StartsWith("\t", StringComparison.Ordinal) && !prev.StartsWith(" ", StringComparison.Ordinal) && !l.TrimStart().StartsWith("-", StringComparison.Ordinal) && !l.TrimStart().StartsWith("*", StringComparison.Ordinal);
                    sb.Append(join ? " " : "\n");
                }
                sb.Append(l);
            }
            return sb.ToString();
        }

        /// <summary>Number with thousands separators ("1,230").</summary>
        public static string N(int n) { return n.ToString("#,0", CultureInfo.InvariantCulture); }
    }
}
