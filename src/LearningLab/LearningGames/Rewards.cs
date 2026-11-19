// Learning Games - rewards for right answers in math. Younger players (up to 4th grade) build with blocks: every right
// answer places the next block of a house, then a lookout tower, then a castle. Older players fill a science lab:
// every few right answers unlock an element sample for the shelf or a piece of a terrarium. Everything is worked out
// from one number, the player's reward points (Rewards.Points in profiles.json), so nothing else needs saving.
// The facts on the sample cards are listed in App Hub's docs\math-facts-to-check.md.
// Ported from App Hub's Learning Games (Engine\Rewards.cs, WPF) to Avalonia: the plans, the lab list, Status and
// Unlocked are plain C#; only the pictures (Picture, BuildPicture, LabPicture) use Avalonia and look as in App Hub.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace LearningGames
{
    public sealed class BuildPlan
    {
        public string Name = "";
        public string[] Rows = new string[0];
        /// <summary>The block cells in the order they are placed: bottom row first, left to right.</summary>
        public List<int[]> Order = new List<int[]>();
        public int Count { get { return Order.Count; } }
    }

    public sealed class LabItem
    {
        public string Symbol = "", Name = "", Fact = "", Kind = "";   // Kind: "element" or "terrarium"
        public int Z;
    }

    public static class Rewards
    {
        public static bool Builds(int grade) { return grade > 0 && grade <= 4; }

        // ---------------------------------------------------------------- blocks
        static readonly Dictionary<char, string> BlockColors = new Dictionary<char, string> {
            { 'G', "#5FA33A" }, { 'D', "#7B4B25" }, { 'S', "#8A8F98" }, { 'P', "#C8955A" }, { 'L', "#6B4423" }, { 'W', "#A5D8FF" },
            { 'R', "#B5452F" }, { 'O', "#5A3418" }, { 'F', "#F472B6" }, { 'T', "#2F7D32" }, { 'Y', "#FBBF24" }, { 'B', "#4B5563" }, { 'Q', "#60A5FA" } };
        public static readonly BuildPlan[] Plans = {
            Plan("a cozy house", new[] {
                "................",
                "......RRRR......",
                ".....RRRRRR.....",
                "....RRRRRRRR....",
                "...RRRRRRRRRR...",
                "..RRRRRRRRRRRR..",
                "...LPPPPPPPPL...",
                "...LPWWPPWWPL...",
                "...LPWWPPWWPL...",
                "...LPPPOOPPPL..F",
                "F..LPPPOOPPPL.FF",
                "GGGGGGGGGGGGGGGG" }),
            Plan("a lookout tower", new[] {
                "....S.S.S.S.....",
                "....SSSSSSS.....",
                ".....SSSSS......",
                ".....SWSWS....T.",
                ".....SSSSS...TTT",
                ".....SYSYS...TTT",
                ".....SSSSS....L.",
                ".....SWSWS....L.",
                ".....SSSSS....L.",
                ".....SSOSS....L.",
                ".....SSOSS..F.L.",
                "GGGGGGGGGGGGGGGG" }),
            Plan("a castle", new[] {
                "S.S..........S.S",
                "SSS..Y....Y..SSS",
                "SWS.S.S.S.S.SSWS",
                "SSS.SSSSSSSSSSSS",
                "SSS.SWSSSSSWS.SS",
                "SWSSSSSSSSSSSSWS",
                "SSSSSSSSSSSSSSSS",
                "SSSSSWSSSSWSSSSS",
                "SSSSSSSOOSSSSSSS",
                "SSSSSSSOOSSSSSSS",
                "QQQQQQQOOQQQQQQQ",
                "GGGGGGGGGGGGGGGG" }),
        };
        static BuildPlan Plan(string name, string[] rows)
        {
            BuildPlan p = new BuildPlan(); p.Name = name; p.Rows = rows;
            for (int y = rows.Length - 1; y >= 0; y--)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] != '.') p.Order.Add(new[] { x, y });
            return p;
        }
        public static int TotalBlocks { get { return Plans.Sum(p => p.Count); } }
        /// <summary>Which plan is being built and how many of its blocks are placed (after all plans: the last one, full).</summary>
        public static void BuildState(int points, out int plan, out int placed)
        {
            int left = Math.Max(0, points);
            for (int i = 0; i < Plans.Length; i++)
            {
                if (left < Plans[i].Count) { plan = i; placed = left; return; }
                left -= Plans[i].Count;
            }
            plan = Plans.Length - 1; placed = Plans[plan].Count;
        }

        // ---------------------------------------------------------------- the lab
        public const int PointsPerSample = 6;
        public static readonly LabItem[] Lab = {
            E("H", "Hydrogen", 1, "The lightest element. Stars like the Sun are mostly hydrogen."),
            T("Gravel", "The bottom layer of a terrarium: extra water drains into it so roots don't get soggy."),
            E("He", "Helium", 2, "Lighter than air, so helium balloons float."),
            E("C", "Carbon", 6, "Found in every living thing. Diamonds and pencil 'lead' (graphite) are both carbon."),
            T("Charcoal", "A thin layer of activated charcoal can help keep a closed terrarium smelling fresh."),
            E("N", "Nitrogen", 7, "About 78% of the air you breathe is nitrogen."),
            E("O", "Oxygen", 8, "About 21% of the air. Plants give it off when they make food from sunlight."),
            T("Soil", "Plants take in water and nutrients through their roots in the soil."),
            E("Ne", "Neon", 10, "Glows red-orange in neon signs."),
            E("Na", "Sodium", 11, "Table salt is sodium and chlorine joined together."),
            T("Moss", "Moss has no roots or flowers; it soaks up water like a sponge."),
            E("Al", "Aluminum", 13, "Light and doesn't rust: used for cans and foil."),
            E("Si", "Silicon", 14, "Sand is mostly silicon and oxygen. Computer chips are made from silicon."),
            T("Fern", "Ferns grow from tiny spores instead of seeds."),
            E("Ca", "Calcium", 20, "Helps build strong bones and teeth."),
            E("Fe", "Iron", 26, "Earth's core is mostly iron. Iron in your blood carries oxygen."),
            T("Springtails", "Tiny helpers that eat mold, keeping a terrarium clean."),
            E("Cu", "Copper", 29, "Carries electricity well, so it is used for wires."),
            E("Ag", "Silver", 47, "Conducts electricity better than any other metal."),
            T("Isopods", "Little crustaceans (cousins of crabs!) that recycle dead leaves."),
            E("Au", "Gold", 79, "Doesn't rust or tarnish, so old gold coins still shine."),
            E("Pb", "Lead", 82, "Very dense: lead aprons block X-rays at the dentist."),
            T("Glass lid", "With the lid on, water evaporates, collects on the glass and drips back down: a tiny water cycle."),
            E("U", "Uranium", 92, "The heaviest element found in nature in large amounts. Power plants use it as fuel. It is radioactive, so it is kept in labelled containers."),
        };
        static LabItem E(string s, string n, int z, string fact) { return new LabItem { Symbol = s, Name = n, Z = z, Fact = fact, Kind = "element" }; }
        static LabItem T(string n, string fact) { return new LabItem { Symbol = "", Name = n, Fact = fact, Kind = "terrarium" }; }
        public static int LabUnlocked(int points) { return Math.Min(Lab.Length, Math.Max(0, points) / PointsPerSample); }

        // ---------------------------------------------------------------- words
        /// <summary>One line about where the player is: "Building a cozy house: 37 of 120 blocks".</summary>
        public static string Status(int grade, int points)
        {
            if (Builds(grade))
            {
                int plan, placed; BuildState(points, out plan, out placed);
                if (plan == Plans.Length - 1 && placed == Plans[plan].Count) return "Master builder! All " + Plans.Length + " builds are finished.";
                return "Building " + Plans[plan].Name + ": " + placed + " of " + Plans[plan].Count + " blocks";
            }
            int n = LabUnlocked(points);
            if (n >= Lab.Length) return "Your lab is complete: all " + Lab.Length + " samples collected!";
            int need = (n + 1) * PointsPerSample - Math.Max(0, points);
            return n + " of " + Lab.Length + " lab samples. Next sample in " + need + " right answer" + (need == 1 ? "" : "s") + ".";
        }
        /// <summary>What the answers between two point totals unlocked, in words ("" for nothing).</summary>
        public static string Unlocked(int grade, int before, int after)
        {
            if (after <= before) return "";
            if (Builds(grade))
            {
                int p0, b0, p1, b1; BuildState(before, out p0, out b0); BuildState(after, out p1, out b1);
                int blocks = Math.Min(after, TotalBlocks) - Math.Min(before, TotalBlocks);
                if (blocks <= 0) return "";
                string s = "You placed " + blocks + " new block" + (blocks == 1 ? "" : "s") + "!";
                if (p1 > p0) s += " You finished " + Plans[p0].Name + "! Now building " + Plans[p1].Name + ".";
                else if (p1 == Plans.Length - 1 && b1 == Plans[p1].Count && b0 < b1) s += " You finished " + Plans[p1].Name + "!";
                return s;
            }
            int n0 = LabUnlocked(before), n1 = LabUnlocked(after);
            if (n1 <= n0) return "";
            var names = Lab.Skip(n0).Take(n1 - n0).Select(i => i.Kind == "element" ? i.Name + " (" + i.Symbol + ", " + i.Z + ")" : i.Name + " for the terrarium").ToArray();
            return "New in your lab: " + string.Join(", ", names) + "!";
        }

        // ---------------------------------------------------------------- pictures
        /// <summary>The build or the lab for a point total. Big = the collection page; small = the results page.</summary>
        public static Control Picture(int grade, int points, bool big)
        {
            return Builds(grade) ? BuildPicture(points, big ? 22 : 15) : LabPicture(points, big);
        }
        public static Canvas BuildPicture(int points, double px)
        {
            int plan, placed; BuildState(points, out plan, out placed);
            Canvas c = new Canvas();
            // finished builds in a row on the left (small), the current one large
            double x = 0;
            for (int i = 0; i < plan; i++) { Canvas done = PlanPicture(Plans[i], Plans[i].Count, px * 0.45); Canvas.SetLeft(done, x); Canvas.SetTop(done, 12 * px - done.Height); c.Children.Add(done); x += done.Width + 14; }
            Canvas cur = PlanPicture(Plans[plan], placed, px);
            Canvas.SetLeft(cur, x); Canvas.SetTop(cur, 0); c.Children.Add(cur);
            c.Width = x + cur.Width; c.Height = cur.Height;
            return c;
        }
        static Canvas PlanPicture(BuildPlan p, int placed, double px)
        {
            Canvas c = new Canvas(); c.Width = 16 * px; c.Height = p.Rows.Length * px;
            for (int i = 0; i < p.Order.Count; i++)
            {
                int bx = p.Order[i][0], by = p.Order[i][1]; char ch = p.Rows[by][bx];
                Rectangle r = new Rectangle(); r.Width = px; r.Height = px;
                if (i < placed)
                {
                    r.Fill = Visuals.B(BlockColors[ch]); r.Stroke = Visuals.B("#0B0D12", 120); r.StrokeThickness = Math.Max(0.6, px / 14);
                    if (px >= 12) { Rectangle shine = new Rectangle(); shine.Width = px * 0.35; shine.Height = px * 0.18; shine.Fill = Visuals.B("#FFFFFF", 60); Canvas.SetLeft(shine, bx * px + px * 0.12); Canvas.SetTop(shine, by * px + px * 0.12); c.Children.Add(r); Canvas.SetLeft(r, bx * px); Canvas.SetTop(r, by * px); c.Children.Add(shine); continue; }
                }
                else { r.Fill = Visuals.B("#FFFFFF", 10); r.Stroke = Visuals.B("#FFFFFF", 40); r.StrokeThickness = 0.8; r.StrokeDashArray = new AvaloniaList<double> { 2, 2 }; }
                Canvas.SetLeft(r, bx * px); Canvas.SetTop(r, by * px); c.Children.Add(r);
            }
            return c;
        }
        public static Canvas LabPicture(int points, bool big)
        {
            int n = LabUnlocked(points);
            double tile = big ? 76 : 44, gap = big ? 8 : 6;
            var elements = Lab.Select((it, i) => new { it, i }).Where(a => a.it.Kind == "element").ToList();
            int perRow = big ? 9 : 9;
            Canvas c = new Canvas();
            // the element shelf
            for (int k = 0; k < elements.Count; k++)
            {
                var e = elements[k]; bool have = e.i < n;
                double x = (k % perRow) * (tile + gap), y = (k / perRow) * (tile + gap);
                Border b = new Border(); b.Width = tile; b.Height = tile; b.CornerRadius = new CornerRadius(8); b.BorderThickness = new Thickness(2);
                b.Background = Visuals.B(have ? (e.it.Z > 88 ? "#3B2667" : e.it.Z > 20 ? "#1E3A5F" : "#14432F") : "#1B1F28");
                b.BorderBrush = Visuals.B(have ? (e.it.Z > 88 ? "#C4B5FD" : e.it.Z > 20 ? "#93C5FD" : "#86EFAC") : "#353C4B");
                Grid g = new Grid();
                if (have)
                {
                    TextBlock z = Visuals.T(e.it.Z.ToString(), tile * 0.17, Visuals.TextC, true); z.Margin = new Thickness(5, 2, 0, 0); g.Children.Add(z);
                    TextBlock s = Visuals.T(e.it.Symbol, tile * 0.38, "#FFFFFF", true); s.HorizontalAlignment = HorizontalAlignment.Center; s.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(s);
                    TextBlock nm = Visuals.T(e.it.Name, tile * 0.14, Visuals.TextC, false); nm.HorizontalAlignment = HorizontalAlignment.Center; nm.VerticalAlignment = VerticalAlignment.Bottom; nm.Margin = new Thickness(0, 0, 0, 3); g.Children.Add(nm);
                    ToolTip.SetTip(b, e.it.Name + ": " + e.it.Fact);   // extra only: the page lists every fact as a card too
                }
                else { TextBlock q = Visuals.T("?", tile * 0.4, "#353C4B", true); q.HorizontalAlignment = HorizontalAlignment.Center; q.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(q); }
                b.Child = g;
                Canvas.SetLeft(b, x); Canvas.SetTop(b, y); c.Children.Add(b);
            }
            double shelfW = perRow * (tile + gap), shelfH = Math.Ceiling(elements.Count / (double)perRow) * (tile + gap);
            // the terrarium jar, filled with what has been unlocked
            var parts = Lab.Select((it, i) => new { it, i }).Where(a => a.it.Kind == "terrarium").ToList();
            Func<string, bool> has = name => parts.Any(p => p.it.Name == name && p.i < n);
            double jw = big ? 220 : 110, jh = big ? 230 : 110, jx = shelfW + (big ? 20 : 12), jy = 0;
            Canvas jar = new Canvas(); jar.Width = jw; jar.Height = jh;
            double floor = jh - 10, layer = jh * 0.08;
            double yy = floor;
            if (has("Gravel")) { yy -= layer; Visuals.Box(jar, 6, yy, jw - 12, layer, "#9CA3AF", 255, null, 0); }
            if (has("Charcoal")) { yy -= layer * 0.5; Visuals.Box(jar, 6, yy, jw - 12, layer * 0.5, "#1F2937", 255, null, 0); }
            if (has("Soil")) { yy -= layer * 1.4; Visuals.Box(jar, 6, yy, jw - 12, layer * 1.4, "#5A3418", 255, null, 0); }
            if (has("Moss")) { for (int k = 0; k < 6; k++) { Ellipse m = new Ellipse(); m.Width = jw * 0.2; m.Height = layer * 0.9; m.Fill = Visuals.B("#4D7C0F"); Canvas.SetLeft(m, 8 + k * (jw - 16) / 6.5); Canvas.SetTop(m, yy - layer * 0.5); jar.Children.Add(m); } }
            if (has("Fern"))
            {
                double fx = jw * 0.62, fy = yy - layer * 0.3;
                for (int k = -2; k <= 2; k++) { Line f = Visuals.Ln(jar, fx, fy, fx + k * jw * 0.08, fy - jh * (0.42 - Math.Abs(k) * 0.07), "#16A34A", 4); }
            }
            if (has("Springtails")) for (int k = 0; k < 5; k++) Visuals.Dot2(jar, 20 + k * 11, yy + 4, 1.6, "#E5E7EB");
            if (has("Isopods")) for (int k = 0; k < 2; k++) { Ellipse iso = new Ellipse(); iso.Width = 12; iso.Height = 7; iso.Fill = Visuals.B("#9CA3AF"); Canvas.SetLeft(iso, jw * 0.3 + k * 18); Canvas.SetTop(iso, yy - 6); jar.Children.Add(iso); }
            // the glass
            Rectangle glass = new Rectangle(); glass.Width = jw; glass.Height = jh - 10; glass.RadiusX = 18; glass.RadiusY = 18; glass.Stroke = Visuals.B("#BAE6FD"); glass.StrokeThickness = 3; glass.Fill = Visuals.B("#BAE6FD", 18);
            Canvas.SetTop(glass, 10); jar.Children.Add(glass);
            if (has("Glass lid")) { Visuals.Box(jar, -4, 2, jw + 8, 10, "#BAE6FD", 200, "#E0F2FE", 1.5); for (int k = 0; k < 4; k++) Visuals.Dot2(jar, 30 + k * jw / 4.5, 26 + (k % 2) * 8, 2.5, "#BAE6FD"); }
            TextBlock jl = Visuals.T("Terrarium", 13, Visuals.Muted, true); Canvas.SetLeft(jl, 4); Canvas.SetTop(jl, jh + 2); jar.Children.Add(jl);
            Canvas.SetLeft(jar, jx); Canvas.SetTop(jar, jy); c.Children.Add(jar);
            c.Width = jx + jw; c.Height = Math.Max(shelfH, jh + 20);
            return c;
        }
    }
}
