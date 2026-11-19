// Learning Lab - Languages: the cartoon stage. Draws a cartoon at a moment in time (Time, seconds) on a 640 x 360
// stage: the scene, the characters (drawn in code: cat, dog, frog, chicken, robot, cactus) bobbing, blinking and
// looking at whoever talks, the speaker's mouth flapping, and what each line's "act" makes them do (jump, spin, fall,
// laugh, cry...). Everything follows Time, so the same moment always looks the same (the checks take pictures of it),
// and the page only moves Time along with the audio. Captions (none, Spanish, Spanish and English) are drawn at the
// bottom of the stage.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LearningLab.Views;

namespace LearningLab.Languages;

public sealed class CartoonStage : Control
{
    public const double W = 640, H = 360, Ground = 292;
    readonly LangCartoon c;
    public CartoonTimeline Timeline { get; set; }
    /// <summary>Seconds into the cartoon.</summary>
    public double Time { get; set; }
    /// <summary>0 no captions, 1 Spanish, 2 Spanish and English.</summary>
    public int Captions { get; set; }

    public CartoonStage(LangCartoon c, CartoonTimeline t)
    {
        this.c = c; Timeline = t;
        Width = W; Height = H;
        ClipToBounds = true;
    }

    static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    static readonly Dictionary<string, IBrush> brushes = new();
    static IBrush B(string hex, byte a = 255)
    {
        string k = hex + "/" + a;
        if (!brushes.TryGetValue(k, out var b)) brushes[k] = b = Ui.Brush(hex, a);
        return b;
    }
    static Pen P(string hex, double w, byte a = 255) => new(B(hex, a), w) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    static readonly Pen Outline = P("#1F2937", 3);

    /// <summary>Where each character stands (x of its feet).</summary>
    public double HomeX(int i)
    {
        int n = c.Cast.Count;
        if (n <= 1) return W / 2;
        double gap = n == 2 ? 250 : 190;
        return W / 2 + (i - (n - 1) / 2.0) * gap;
    }

    public override void Render(DrawingContext dc)
    {
        Scene(dc, c.Scene, Time);
        int speaking = Timeline?.Speaking(Time) ?? -1;
        int last = Timeline?.LastStarted(Time) ?? -1;
        string speaker = speaking >= 0 ? c.Lines[speaking].Who : null;
        for (int i = 0; i < c.Cast.Count; i++)
        {
            var a = c.Cast[i];
            // the act of this character's most recent line, while it is the latest line said
            string act = null; double u = 0;
            if (last >= 0 && c.Lines[last].Who == a.Id) { act = c.Lines[last].Act ?? "talk"; u = Time - Timeline.Start[last]; }
            int lookAt = speaker != null && speaker != a.Id ? c.Cast.FindIndex(x => x.Id == speaker) : -1;
            double lookDir = lookAt < 0 ? 0 : Math.Sign(HomeX(lookAt) - HomeX(i));
            if (lookAt < 0 && speaker == a.Id) { int other = i == 0 ? 1 : 0; lookDir = Math.Sign(HomeX(other) - HomeX(i)); }
            Actor(dc, a, i, act, u, speaker == a.Id, lookDir);
        }
        if (Captions > 0 && last >= 0 && Time < Timeline.End[last] + 1.2) Caption(dc, c.Lines[last]);
    }

    // ---------------------------------------------------------------- the scenes
    static void Scene(DrawingContext dc, string scene, double t)
    {
        switch (scene)
        {
            case "kitchen":
                dc.FillRectangle(B("#FDE7B0"), new Rect(0, 0, W, H));
                for (int x = 0; x < W; x += 40) dc.FillRectangle(B("#F9D98A"), new Rect(x, 0, 20, Ground));
                dc.DrawRectangle(B("#BAE6FD"), P("#92400E", 6), new Rect(420, 50, 150, 110), 6, 6);
                dc.DrawLine(P("#92400E", 4), new Point(495, 50), new Point(495, 160));
                dc.DrawEllipse(B("#FCD34D"), null, new Point(530 + Math.Sin(t * 0.3) * 6, 85), 18, 18);
                dc.DrawRectangle(B("#A16207"), Outline, new Rect(30, 190, 210, 102), 6, 6);
                dc.FillRectangle(B("#78716C"), new Rect(20, 180, 230, 14));
                dc.DrawEllipse(B("#9CA3AF"), Outline, new Point(90, 168), 30, 14);
                Steam(dc, 90, 150, t);
                Floor(dc, "#E7C9A9", "#D6B38F");
                break;
            case "park":
                Sky(dc, "#7DD3FC", "#E0F2FE");
                Sun(dc, 560, 60, t);
                Cloud(dc, 120 + (t * 8) % 700 - 60, 60);
                dc.DrawEllipse(B("#86EFAC"), null, new Point(150, Ground + 40), 360, 90);
                dc.FillRectangle(B("#4ADE80"), new Rect(0, Ground, W, H - Ground));
                dc.DrawRectangle(B("#92400E"), Outline, new Rect(70, 150, 22, 145), 4, 4);
                dc.DrawEllipse(B("#16A34A"), Outline, new Point(80, 130), 60, 50);
                dc.DrawEllipse(B("#22C55E"), null, new Point(65, 115), 26, 20);
                Flowers(dc);
                break;
            case "beach":
                Sky(dc, "#38BDF8", "#BAE6FD");
                Sun(dc, 90, 70, t);
                dc.FillRectangle(B("#0EA5E9"), new Rect(0, 200, W, 60));
                for (int x = 0; x < W; x += 40) dc.DrawEllipse(B("#E0F2FE", 200), null, new Point(x + (t * 20) % 40, 202), 14, 4);
                dc.FillRectangle(B("#FDE68A"), new Rect(0, 250, W, H - 250));
                dc.DrawLine(P("#78350F", 5), new Point(560, 300), new Point(540, 170));
                var umb = new StreamGeometry();
                using (var g = umb.Open()) { g.BeginFigure(new Point(470, 190), true); g.QuadraticBezierTo(new Point(540, 110), new Point(610, 150)); g.LineTo(new Point(470, 190)); g.EndFigure(true); }
                dc.DrawGeometry(B("#F43F5E"), Outline, umb);
                break;
            case "space":
                dc.FillRectangle(B("#0B1026"), new Rect(0, 0, W, H));
                var rnd = new Random(7);
                for (int k = 0; k < 70; k++)
                {
                    double x = rnd.NextDouble() * W, y = rnd.NextDouble() * 240, tw = 0.5 + 0.5 * Math.Sin(t * 3 + k);
                    dc.DrawEllipse(B("#FFFFFF", (byte)(120 + 135 * tw)), null, new Point(x, y), 1.6, 1.6);
                }
                dc.DrawEllipse(B("#A78BFA"), Outline, new Point(520, 90), 46, 46);
                dc.DrawEllipse(null, P("#FDE68A", 5), new Point(520, 90), 72, 16);
                dc.DrawEllipse(B("#9CA3AF"), null, new Point(W / 2, Ground + 260), 520, 280);
                for (int k = 0; k < 6; k++) dc.DrawEllipse(B("#6B7280"), null, new Point(60 + k * 110, Ground + 30 + (k % 2) * 18), 22, 7);
                break;
            case "market":
                dc.FillRectangle(B("#FECACA"), new Rect(0, 0, W, H));
                for (int x = 0; x < W; x += 64) dc.FillRectangle(B(x / 64 % 2 == 0 ? "#EF4444" : "#FFFFFF"), new Rect(x, 30, 64, 46));
                for (int x = 0; x < W; x += 64) dc.DrawEllipse(B(x / 64 % 2 == 0 ? "#EF4444" : "#FFFFFF"), null, new Point(x + 32, 76), 32, 12);
                dc.DrawRectangle(B("#92400E"), Outline, new Rect(30, 190, 580, 26), 4, 4);
                string[] fruit = { "#F97316", "#FACC15", "#22C55E", "#EF4444", "#A855F7" };
                for (int k = 0; k < 16; k++) dc.DrawEllipse(B(fruit[k % 5]), P("#1F2937", 2), new Point(55 + k * 36, 180 - (k % 3) * 6), 14, 14);
                Floor(dc, "#D6D3D1", "#A8A29E");
                break;
            case "classroom":
                dc.FillRectangle(B("#FEF3C7"), new Rect(0, 0, W, H));
                dc.DrawRectangle(B("#166534"), P("#92400E", 8), new Rect(120, 40, 400, 150), 4, 4);
                var ft = new FormattedText("1 + 1 = 2   ¡Hola!", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 28, B("#F0FDF4", 230));
                dc.DrawText(ft, new Point(320 - ft.Width / 2, 95));
                dc.DrawEllipse(B("#FFFFFF"), Outline, new Point(580, 70), 30, 30);
                dc.DrawLine(P("#1F2937", 3), new Point(580, 70), new Point(580 + 18 * Math.Sin(t), 70 - 18 * Math.Cos(t)));
                dc.DrawLine(P("#1F2937", 3), new Point(580, 70), new Point(592, 70));
                Floor(dc, "#D9B38C", "#C49A6C");
                break;
            default: // desert
                Sky(dc, "#FB923C", "#FDE68A");
                Sun(dc, 520, 80, t);
                var hills = new StreamGeometry();
                using (var g = hills.Open())
                {
                    g.BeginFigure(new Point(0, 230), true);
                    g.LineTo(new Point(90, 170)); g.LineTo(new Point(170, 210)); g.LineTo(new Point(280, 150)); g.LineTo(new Point(400, 215)); g.LineTo(new Point(520, 175)); g.LineTo(new Point(W, 220));
                    g.LineTo(new Point(W, 260)); g.LineTo(new Point(0, 260)); g.EndFigure(true);
                }
                dc.DrawGeometry(B("#C2410C"), null, hills);
                dc.FillRectangle(B("#FCD34D"), new Rect(0, 250, W, H - 250));
                Saguaro(dc, 50, 255, 0.9); Saguaro(dc, 600, 250, 0.7);
                break;
        }
    }

    static void Sky(DrawingContext dc, string top, string bottom)
    {
        var g = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        g.GradientStops.Add(new GradientStop(Color.Parse(top), 0)); g.GradientStops.Add(new GradientStop(Color.Parse(bottom), 1));
        dc.FillRectangle(g, new Rect(0, 0, W, H));
    }
    static void Sun(DrawingContext dc, double x, double y, double t)
    {
        for (int k = 0; k < 10; k++)
        {
            double a = k * Math.PI / 5 + t * 0.4;
            dc.DrawLine(P("#FBBF24", 5), new Point(x + Math.Cos(a) * 38, y + Math.Sin(a) * 38), new Point(x + Math.Cos(a) * 52, y + Math.Sin(a) * 52));
        }
        dc.DrawEllipse(B("#FDE047"), P("#F59E0B", 3), new Point(x, y), 30, 30);
    }
    static void Cloud(DrawingContext dc, double x, double y)
    {
        foreach (var (dx, dy, r) in new[] { (0.0, 0.0, 22.0), (24, -10, 26), (52, 0, 22), (26, 8, 22) }) dc.DrawEllipse(B("#FFFFFF"), null, new Point(x + dx, y + dy), r * 1.2, r);
    }
    static void Floor(DrawingContext dc, string a, string b)
    {
        dc.FillRectangle(B(a), new Rect(0, Ground, W, H - Ground));
        for (int x = 0; x < W; x += 80) dc.FillRectangle(B(b), new Rect(x, Ground, 40, H - Ground));
        dc.DrawLine(P("#1F2937", 2, 90), new Point(0, Ground), new Point(W, Ground));
    }
    static void Steam(DrawingContext dc, double x, double y, double t)
    {
        for (int k = 0; k < 3; k++)
        {
            double p = (t * 0.8 + k / 3.0) % 1;
            dc.DrawEllipse(B("#FFFFFF", (byte)(180 * (1 - p))), null, new Point(x + Math.Sin(p * 6 + k) * 8, y - p * 60), 8 + p * 8, 6 + p * 6);
        }
    }
    static void Flowers(DrawingContext dc)
    {
        string[] cols = { "#F472B6", "#FDE047", "#FFFFFF", "#C084FC" };
        for (int k = 0; k < 9; k++)
        {
            double x = 160 + k * 52 + (k % 2) * 14, y = Ground + 18 + (k % 3) * 14;
            dc.DrawLine(P("#15803D", 2), new Point(x, y), new Point(x, y + 12));
            dc.DrawEllipse(B(cols[k % 4]), null, new Point(x, y), 6, 6);
            dc.DrawEllipse(B("#F59E0B"), null, new Point(x, y), 2.5, 2.5);
        }
    }
    static void Saguaro(DrawingContext dc, double x, double y, double s)
    {
        var p = P("#14532D", 3);
        dc.DrawRectangle(B("#16A34A"), p, new Rect(x - 12 * s, y - 130 * s, 24 * s, 130 * s), 12 * s, 12 * s);
        dc.DrawRectangle(B("#16A34A"), p, new Rect(x - 40 * s, y - 95 * s, 16 * s, 50 * s), 8 * s, 8 * s);
        dc.DrawRectangle(B("#16A34A"), p, new Rect(x - 40 * s, y - 55 * s, 34 * s, 14 * s), 7 * s, 7 * s);
        dc.DrawRectangle(B("#16A34A"), p, new Rect(x + 24 * s, y - 110 * s, 16 * s, 55 * s), 8 * s, 8 * s);
        dc.DrawRectangle(B("#16A34A"), p, new Rect(x + 6 * s, y - 68 * s, 34 * s, 14 * s), 7 * s, 7 * s);
    }

    // ---------------------------------------------------------------- the characters
    /// <summary>Draws one character with its feet at (HomeX, Ground), moved by its act. u = seconds since its line began.</summary>
    void Actor(DrawingContext dc, CartoonActor a, int i, string act, double u, bool talking, double lookDir)
    {
        double phase = i * 1.7;
        double x = HomeX(i), y = Ground;
        double rot = 0, sx = 1, sy = 1;
        double bob = Math.Sin(Time * 2.2 + phase) * 3;
        y += bob;
        bool eyesShut = Blink(Time + phase), bigEyes = false, lying = false;
        switch (act)
        {
            case "jump": if (u < 1.4) y -= Math.Abs(Math.Sin(u * Math.PI * 2.2)) * 55; break;
            case "spin": if (u < 1.0) sx = Math.Cos(u * Math.PI * 4); break;
            case "shake": if (u < 1.2) x += Math.Sin(u * 45) * 7; break;
            case "laugh": if (u < 2.0) { y -= Math.Abs(Math.Sin(u * 14)) * 8; rot = Math.Sin(u * 7) * 6; eyesShut = true; } break;
            case "surprise": if (u < 1.6) { double k = u < 0.15 ? u / 0.15 : 1; sy = 1 + 0.18 * k; sx = 1 - 0.06 * k; bigEyes = true; } break;
            case "dance": rot = Math.Sin(u * 6) * 14; y -= Math.Abs(Math.Sin(u * 6)) * 10; break;
            case "fall":
                if (u < 2.4) { double k = Math.Min(1, u / 0.3); rot = 90 * k * (i % 2 == 0 ? -1 : 1); lying = u > 0.3; y += 6 * k; eyesShut = lying; }
                else if (u < 2.8) { rot = 90 * (1 - (u - 2.4) / 0.4) * (i % 2 == 0 ? -1 : 1); }
                break;
            case "run": if (u < 2.2) { x += Math.Sin(u * 3) * 90; y -= Math.Abs(Math.Sin(u * 18)) * 6; } break;
            case "sleep": eyesShut = true; rot = 8; break;
        }
        var m = Matrix.CreateScale(sx, sy) * Matrix.CreateRotation(rot * Math.PI / 180) * Matrix.CreateTranslation(x, y);
        using (dc.PushTransform(m))
        {
            double look = lookDir * 3;
            switch (a.Kind)
            {
                case "dog": Dog(dc, a.Color, talking, look, eyesShut, bigEyes); break;
                case "frog": Frog(dc, a.Color, talking, look, eyesShut, bigEyes); break;
                case "chicken": Chicken(dc, a.Color, talking, look, eyesShut, bigEyes, u, act); break;
                case "robot": Robot(dc, a.Color, talking, look, eyesShut, bigEyes); break;
                case "cactus": Cactus(dc, a.Color, talking, look, eyesShut, bigEyes); break;
                default: Cat(dc, a.Color, talking, look, eyesShut, bigEyes); break;
            }
            Arm(dc, a, act, u, i, lookDir);
        }
        // things around the character (not turned with it)
        double headY = y - 140;
        switch (act)
        {
            case "laugh": if (u < 2.0) Word(dc, "¡ja ja!", x + 40, headY - 10 - u * 10, "#B45309", 1 - u / 2.0); break;
            case "surprise": if (u < 1.6) Word(dc, "!", x, headY - 20, "#DC2626", 1); break;
            case "think": Bubble(dc, x + 45, headY - 20, "?"); break;
            case "sleep": for (int k = 0; k < 3; k++) { double p = (u * 0.6 + k / 3.0) % 1; Word(dc, "z", x + 30 + p * 30, headY - p * 50, "#1E3A8A", 1 - p); } break;
            case "cry": if (u < 2.5) for (int k = 0; k < 4; k++) { double p = (u * 1.5 + k / 4.0) % 1; dc.DrawEllipse(B("#38BDF8", (byte)(255 * (1 - p))), null, new Point(x + (k % 2 == 0 ? -16 : 16), headY + 50 + p * 70), 4, 6); } break;
            case "fall": if (u > 0.3 && u < 2.4) for (int k = 0; k < 3; k++) { double ang = u * 5 + k * 2.1; Word(dc, "★", x + (i % 2 == 0 ? -70 : 70) + Math.Cos(ang) * 26, Ground - 40 + Math.Sin(ang) * 8, "#FACC15", 1); } break;
            case "run": if (u < 2.2) for (int k = 0; k < 3; k++) dc.DrawLine(P("#FFFFFF", 3, 180), new Point(x - 70, Ground - 30 - k * 18), new Point(x - 100, Ground - 30 - k * 18)); break;
            case "eat":
                if (u < 2.2)
                {
                    double r = Math.Max(0, 14 * (1 - u / 2.0));
                    dc.DrawEllipse(B("#F97316"), Outline, new Point(x + 34, headY + 64), r, r);
                    if (r < 12) Word(dc, "ñam", x + 50, headY + 20 - u * 6, "#7C2D12", 1);
                }
                break;
        }
    }

    static bool Blink(double t) => (t % 3.7) < 0.12;

    static void Eyes(DrawingContext dc, double y, double gap, double r, double look, bool shut, bool big)
    {
        if (big) r *= 1.35;
        foreach (double ex in new[] { -gap, gap })
        {
            if (shut) { dc.DrawLine(P("#1F2937", 3), new Point(ex - r * 0.8, y), new Point(ex + r * 0.8, y)); continue; }
            dc.DrawEllipse(B("#FFFFFF"), P("#1F2937", 2.5), new Point(ex, y), r, r);
            dc.DrawEllipse(B("#111827"), null, new Point(ex + look * r * 0.3, y + 1), r * 0.45, r * 0.45);
            dc.DrawEllipse(B("#FFFFFF"), null, new Point(ex + look * r * 0.3 - r * 0.15, y - r * 0.15), r * 0.14, r * 0.14);
        }
    }

    void Mouth(DrawingContext dc, double y, double w, bool talking)
    {
        if (talking)
        {
            double open = 2 + Math.Abs(Math.Sin(Time * 17)) * 9;
            dc.DrawEllipse(B("#7F1D1D"), P("#1F2937", 2.5), new Point(0, y + open / 2), w * 0.45, open);
        }
        else
        {
            var g = new StreamGeometry();
            using (var s = g.Open()) { s.BeginFigure(new Point(-w / 2, y), false); s.QuadraticBezierTo(new Point(0, y + 9), new Point(w / 2, y)); s.EndFigure(false); }
            dc.DrawGeometry(null, P("#1F2937", 3), g);
        }
    }

    void Cat(DrawingContext dc, string col, bool talking, double look, bool shut, bool big)
    {
        var tail = new StreamGeometry();
        using (var s = tail.Open()) { s.BeginFigure(new Point(28, -20), false); s.QuadraticBezierTo(new Point(75, -30 + Math.Sin(Time * 3) * 10), new Point(60, -80)); s.EndFigure(false); }
        dc.DrawGeometry(null, P("#1F2937", 13), tail); dc.DrawGeometry(null, P(col, 8), tail);
        dc.DrawEllipse(B(col), Outline, new Point(0, -38), 36, 38);
        foreach (int sx in new[] { -1, 1 })
        {
            var ear = new StreamGeometry();
            using (var s = ear.Open()) { s.BeginFigure(new Point(sx * 14, -132), true); s.LineTo(new Point(sx * 38, -150)); s.LineTo(new Point(sx * 36, -112)); s.EndFigure(true); }
            dc.DrawGeometry(B(col), Outline, ear);
        }
        dc.DrawEllipse(B(col), Outline, new Point(0, -105), 40, 34);
        Eyes(dc, -112, 15, 9, look, shut, big);
        dc.DrawEllipse(B("#F472B6"), null, new Point(0, -98), 4, 3);
        foreach (int sx in new[] { -1, 1 }) for (int k = -1; k <= 1; k++) dc.DrawLine(P("#1F2937", 1.5), new Point(sx * 14, -96 + k * 4), new Point(sx * 40, -98 + k * 7));
        Mouth(dc, -90, 18, talking);
    }

    void Dog(DrawingContext dc, string col, bool talking, double look, bool shut, bool big)
    {
        dc.DrawEllipse(B(col), Outline, new Point(0, -38), 40, 38);
        dc.DrawEllipse(B("#FFFFFF", 160), null, new Point(0, -32), 20, 22);
        dc.DrawEllipse(B(col), Outline, new Point(0, -105), 42, 36);
        foreach (int sx in new[] { -1, 1 }) dc.DrawEllipse(B(Darker(col)), Outline, new Point(sx * 42, -100 + Math.Sin(Time * 4 + sx) * 3), 13, 28);
        Eyes(dc, -115, 15, 8.5, look, shut, big);
        dc.DrawEllipse(B("#FFFFFF"), Outline, new Point(0, -92), 20, 13);
        dc.DrawEllipse(B("#111827"), null, new Point(0, -100), 7, 5);
        Mouth(dc, -88, 18, talking);
    }

    void Frog(DrawingContext dc, string col, bool talking, double look, bool shut, bool big)
    {
        dc.DrawEllipse(B(col), Outline, new Point(0, -34), 52, 34);
        foreach (int sx in new[] { -1, 1 }) dc.DrawEllipse(B(col), Outline, new Point(sx * 46, -6), 18, 8);
        dc.DrawEllipse(B(col), Outline, new Point(0, -82), 50, 32);
        foreach (int sx in new[] { -1, 1 }) dc.DrawEllipse(B(col), Outline, new Point(sx * 24, -112), 17, 17);
        Eyes(dc, -113, 24, 11, look, shut, big);
        if (talking) Mouth(dc, -78, 34, true);
        else
        {
            var g = new StreamGeometry();
            using (var s = g.Open()) { s.BeginFigure(new Point(-34, -80), false); s.QuadraticBezierTo(new Point(0, -64), new Point(34, -80)); s.EndFigure(false); }
            dc.DrawGeometry(null, P("#1F2937", 3), g);
        }
    }

    void Chicken(DrawingContext dc, string col, bool talking, double look, bool shut, bool big, double u, string act)
    {
        foreach (int sx in new[] { -1, 1 }) dc.DrawLine(P("#F59E0B", 4), new Point(sx * 10, -20), new Point(sx * 12, 0));
        dc.DrawEllipse(B(col), Outline, new Point(0, -52), 40, 36);
        double flap = act == "surprise" || act == "jump" ? Math.Sin(u * 30) * 20 : Math.Sin(Time * 2) * 4;
        foreach (int sx in new[] { -1, 1 })
        {
            using (dc.PushTransform(Matrix.CreateRotation(sx * flap * Math.PI / 180) * Matrix.CreateTranslation(sx * 36, -56)))
                dc.DrawEllipse(B(Darker(col)), Outline, new Point(sx * 6, 4), 12, 20);
        }
        dc.DrawEllipse(B(col), Outline, new Point(0, -108), 28, 28);
        foreach (int k in new[] { -10, 0, 10 }) dc.DrawEllipse(B("#EF4444"), P("#1F2937", 2), new Point(k, -138 - (k == 0 ? 4 : 0)), 8, 9);
        Eyes(dc, -114, 11, 7, look, shut, big);
        var beak = new StreamGeometry();
        double open = talking ? Math.Abs(Math.Sin(Time * 17)) * 8 : 0;
        using (var s = beak.Open()) { s.BeginFigure(new Point(-10, -102), true); s.LineTo(new Point(0, -88 + open)); s.LineTo(new Point(10, -102)); s.EndFigure(true); }
        dc.DrawGeometry(B("#F59E0B"), P("#1F2937", 2), beak);
        dc.DrawEllipse(B("#EF4444"), null, new Point(0, -84 + open), 4, 6);
    }

    void Robot(DrawingContext dc, string col, bool talking, double look, bool shut, bool big)
    {
        foreach (int sx in new[] { -1, 1 }) dc.DrawRectangle(B("#6B7280"), Outline, new Rect(sx * 18 - 7, -20, 14, 20), 3, 3);
        dc.DrawRectangle(B(col), Outline, new Rect(-38, -80, 76, 62), 12, 12);
        for (int k = 0; k < 3; k++) dc.DrawEllipse(B(k == (int)(Time * 3) % 3 ? "#F87171" : "#FDE68A"), P("#1F2937", 1.5), new Point(-16 + k * 16, -48), 5, 5);
        dc.DrawLine(P("#1F2937", 3), new Point(0, -150), new Point(0, -168));
        dc.DrawEllipse(B("#F43F5E"), Outline, new Point(0, -172), 7, 7);
        dc.DrawRectangle(B(col), Outline, new Rect(-42, -152, 84, 66), 14, 14);
        dc.DrawRectangle(B("#0F172A"), P("#1F2937", 2), new Rect(-32, -142, 64, 46), 8, 8);
        if (shut) { dc.DrawLine(P("#5EEAD4", 3), new Point(-20, -124), new Point(-8, -124)); dc.DrawLine(P("#5EEAD4", 3), new Point(8, -124), new Point(20, -124)); }
        else foreach (int sx in new[] { -1, 1 }) dc.DrawEllipse(B("#5EEAD4"), null, new Point(sx * 14 + look * 2, -125), big ? 8 : 6, big ? 8 : 6);
        double open = talking ? 2 + Math.Abs(Math.Sin(Time * 17)) * 6 : 2;
        dc.DrawRectangle(B("#5EEAD4"), null, new Rect(-14, -108 - open / 2, 28, open), 1, 1);
    }

    void Cactus(DrawingContext dc, string col, bool talking, double look, bool shut, bool big)
    {
        dc.DrawRectangle(B("#C2410C"), Outline, new Rect(-34, -34, 68, 34), 6, 6);
        dc.DrawRectangle(B(col), Outline, new Rect(-30, -150, 60, 120), 28, 28);
        dc.DrawRectangle(B(col), Outline, new Rect(-62, -118, 22, 54), 11, 11);
        dc.DrawRectangle(B(col), Outline, new Rect(-62, -78, 40, 18), 9, 9);
        dc.DrawRectangle(B(col), Outline, new Rect(40, -126, 22, 48), 11, 11);
        dc.DrawRectangle(B(col), Outline, new Rect(22, -92, 40, 18), 9, 9);
        for (int k = 0; k < 8; k++) dc.DrawLine(P("#1F2937", 1.5), new Point(-20 + (k % 4) * 13, -60 + (k / 4) * 16), new Point(-23 + (k % 4) * 13, -64 + (k / 4) * 16));
        dc.DrawEllipse(B("#F472B6"), P("#1F2937", 2), new Point(0, -152), 10, 7);
        Eyes(dc, -118, 12, 8, look, shut, big);
        Mouth(dc, -98, 16, talking);
    }

    /// <summary>The arm that waves or points (characters keep their arms tucked otherwise).</summary>
    void Arm(DrawingContext dc, CartoonActor a, string act, double u, int i, double lookDir)
    {
        if (act != "wave" && act != "point") return;
        if (u > 2.0) return;
        double side = act == "point" ? (lookDir == 0 ? 1 : lookDir) : 1;
        var sh = new Point(side * 30, -62);
        double ang = act == "wave" ? -100 + Math.Sin(u * 12) * 25 : -10;
        double r = ang * Math.PI / 180;
        var hand = new Point(sh.X + side * Math.Cos(r) * 42, sh.Y + Math.Sin(r) * 42);
        dc.DrawLine(P("#1F2937", 11), sh, hand);
        dc.DrawLine(P(a.Color, 7), sh, hand);
        dc.DrawEllipse(B(a.Color), Outline, hand, 7, 7);
    }

    static string Darker(string hex)
    {
        var c = Color.Parse(hex);
        return "#" + ((byte)(c.R * 0.75)).ToString("X2") + ((byte)(c.G * 0.75)).ToString("X2") + ((byte)(c.B * 0.75)).ToString("X2");
    }

    static void Word(DrawingContext dc, string text, double x, double y, string col, double alpha)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 24, B(col, (byte)(255 * Math.Clamp(alpha, 0, 1))));
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }

    static void Bubble(DrawingContext dc, double x, double y, string text)
    {
        dc.DrawEllipse(B("#FFFFFF"), Outline, new Point(x - 22, y + 30), 5, 5);
        dc.DrawEllipse(B("#FFFFFF"), Outline, new Point(x - 12, y + 18), 8, 8);
        dc.DrawEllipse(B("#FFFFFF"), Outline, new Point(x + 12, y - 6), 24, 20);
        Word(dc, text, x + 12, y - 6, "#1F2937", 1);
    }

    void Caption(DrawingContext dc, CartoonLine l)
    {
        var who = c.Actor(l.Who);
        string es = (who?.Name ?? "") + ": " + l.Target;
        var t1 = new FormattedText(es, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 19, B("#FFFFFF")) { MaxTextWidth = W - 40 };
        FormattedText t2 = Captions == 2 ? new FormattedText(l.English, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 15, B("#E5E7EB")) { MaxTextWidth = W - 40 } : null;
        double h = t1.Height + (t2?.Height ?? 0) + 14;
        dc.DrawRectangle(B("#000000", 170), null, new Rect(10, H - h - 8, W - 20, h), 8, 8);
        dc.DrawText(t1, new Point(20, H - h - 1));
        if (t2 != null) dc.DrawText(t2, new Point(20, H - h - 1 + t1.Height + 2));
    }
}
