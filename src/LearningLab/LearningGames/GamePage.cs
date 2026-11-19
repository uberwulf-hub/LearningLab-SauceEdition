// Learning Games - the math games: Asteroid Blaster, Number Line Frog, Pizza Shop, Math Race, Boss Battle and practising
// one skill. One page: the question and score at the top, the play playField, and the answer area (choice buttons, or a
// typed answer with an on-screen number pad that works by touch). About 30 frames a second (Tick). Ported from App
// Hub's LearningGames.ps1 with the same rules, timings, scores and pictures.
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningGames;
using LearningLab.Views;
using Snd = LearningGames.Sound;

namespace LearningLab.LearningGames;

/// <summary>A game in progress.</summary>
public sealed class GameState
{
    public string Kind = "", Skill;
    public int Round, Rounds = 12, Score, Streak, BestStreak, Lives = 3, Right, Wrong, HintIdx, WrongThis, Frame, Grade, PointsStart;
    public Problem Problem;
    public bool Busy, Net, Spectator, Over;
    public readonly List<string> LevelUps = new();
    public Action After;
    public DateTime NextAt = DateTime.MaxValue;
    public List<NetPlayer> NetPlayers;
}

public class GamePage : IPage, IKeyPage, INetGame
{
    public GameState G { get; }
    public string Title => "Math";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();

    // HUD
    readonly TextBlock lblQuestion = Ui.Text("", 28, Ui.TextC, true), lblRound = Ui.Text("", 13, Ui.Muted), lblLives = Ui.Text("", 20, Ui.Red),
        lblScore = Ui.Text("0", 22, Ui.TextC, true), lblStreak = Ui.Text("0", 22, Ui.Yellow, true);
    // field
    readonly Canvas playField = new() { ClipToBounds = true, Background = Brushes.Transparent };
    readonly Border toast;
    readonly TextBlock lblToast = Ui.Text("", 22, Ui.TextC, true);
    readonly TextBlock lblBig = new() { FontSize = 120, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, IsHitTestVisible = false };
    readonly Border hintCard;
    readonly TextBlock lblHint = Ui.Text("", 17, "#FDE68A");
    // answers
    readonly WrapPanel choices = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Panel typed;
    readonly TextBox answer = new() { Width = 200, FontSize = 26, FontWeight = FontWeight.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 6) };
    readonly TextBlock lblUnit = Ui.Text("", 15, Ui.Muted);
    readonly UniformGrid pad = new() { Columns = 8 };
    readonly Button btnHint, btnQuit;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    DateTime toastUntil = DateTime.MinValue, bigUntil = DateTime.MaxValue;
    Control visual;
    bool narrow;
    StackPanel side;

    public static void Start(string kind, string skill, List<NetPlayer> netPlayers = null)
    {
        var page = new GamePage(kind, skill, netPlayers);
        MainView.Current.Push(page);
        page.Begin();
    }

    GamePage(string kind, string skill, List<NetPlayer> netPlayers)
    {
        var me = Lg.Me;
        G = new GameState { Kind = kind, Skill = skill, Net = netPlayers != null, NetPlayers = netPlayers };
        G.Spectator = (me == null || me.Grade == 0) && !Family.ParentPlays && G.Net;
        if (me == null || (me.Grade == 0 && !G.Net)) G.Spectator = me == null;
        G.Grade = G.Spectator || me == null || me.Grade == 0 ? 6 : me.Grade;
        G.PointsStart = me != null && me.Grade > 0 ? me.RewardPoints : 0;

        // ---- HUD
        var say = Ui.IconButton("\U0001F50A", "Read it to me"); say.Margin = new Thickness(0, 0, 8, 0);
        say.Click += (s, e) => { if (!string.IsNullOrEmpty(lblQuestion.Text)) Lg.Speak(Lg.SayText(lblQuestion.Text)); };
        var scoreBox = new StackPanel { Margin = new Thickness(0, 0, 14, 0), Children = { Ui.Text("Score", 12, Ui.Muted), lblScore } };
        var streakBox = new StackPanel { Children = { Ui.Text("Streak", 12, Ui.Muted), lblStreak } };
        lblLives.VerticalAlignment = VerticalAlignment.Center; lblLives.Margin = new Thickness(0, 0, 14, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { lblLives, scoreBox, streakBox } };
        var hud = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        hud.Children.Add(right);
        lblRound.VerticalAlignment = VerticalAlignment.Center; lblRound.Margin = new Thickness(0, 0, 12, 0);
        lblQuestion.VerticalAlignment = VerticalAlignment.Center;
        var q = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(say, Dock.Left); DockPanel.SetDock(lblRound, Dock.Left);
        q.Children.Add(say); q.Children.Add(lblRound); q.Children.Add(lblQuestion);
        hud.Children.Add(q);
        var hudBorder = new Border { Child = hud, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 10) };

        // ---- playField, toast, big countdown, hint card
        toast = new Border
        {
            Child = lblToast, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 20, 12, 0), Padding = new Thickness(22, 12),
            CornerRadius = new CornerRadius(16), Background = Ui.Brush("#13161D", 224), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1), IsVisible = false, IsHitTestVisible = false,
        };
        lblToast.TextAlignment = TextAlignment.Center; lblToast.MaxWidth = 700;
        lblHint.MaxWidth = 680;
        hintCard = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "\U0001F4A1", FontSize = 20, Margin = new Thickness(0, 2, 10, 0) }, lblHint } },
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 0, 12, 14), Padding = new Thickness(16, 10), MaxWidth = 760,
            CornerRadius = new CornerRadius(14), Background = Ui.Brush("#33280E"), BorderBrush = Ui.Brush(Ui.Yellow), BorderThickness = new Thickness(1), IsVisible = false,
        };
        var fieldHost = new Panel { Children = { playField, toast, lblBig, hintCard } };

        // ---- answers: choices, or a typed answer with a pad
        answer.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(answer.Text ?? ""); } };
        var go = Ui.Button("Go", true, 20); go.Padding = new Thickness(22, 8); go.Margin = new Thickness(10, 0, 14, 0);
        go.Click += (s, e) => Submit(answer.Text ?? "");
        lblUnit.VerticalAlignment = VerticalAlignment.Center; lblUnit.Margin = new Thickness(8, 0, 0, 0);
        var entry = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { answer, lblUnit, go } };
        pad.Width = 460;
        typed = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false, Children = { entry, pad } };
        btnHint = Ui.Button("\U0001F4A1 Hint", false, 15); btnHint.Margin = new Thickness(8, 4, 0, 4);
        btnHint.Click += (s, e) => ShowHint();
        btnQuit = Ui.Button("Stop", false, 15); btnQuit.Margin = new Thickness(8, 4, 0, 4);
        btnQuit.Click += (s, e) => Quit();
        side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { btnHint, btnQuit } };
        var answers = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        answers.Children.Add(side);
        answers.Children.Add(new Panel { Children = { choices, typed } });
        var answerBorder = new Border { Child = answers, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8) };

        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        g.Children.Add(hudBorder);
        Grid.SetRow(fieldHost, 1); g.Children.Add(fieldHost);
        Grid.SetRow(answerBorder, 2); g.Children.Add(answerBorder);
        View = g;
        View.SizeChanged += (s, e) => Layout(e.NewSize.Width);
        timer.Tick += (s, e) => Tick();
        btnHint.IsVisible = !G.Spectator;
    }

    void Layout(double w)
    {
        bool n = w < 640;
        if (n == narrow && pad.Children.Count > 0) return;
        narrow = n;
        pad.Columns = n ? 4 : 8;
        pad.Width = n ? Math.Min(w - 24, 340) : 460;
        DockPanel.SetDock(side, n ? Dock.Top : Dock.Right);
        side.HorizontalAlignment = n ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        lblQuestion.FontSize = n ? Math.Min(lblQuestion.FontSize, 22) : lblQuestion.FontSize;
        btnHint.Content = n ? "\U0001F4A1" : "\U0001F4A1 Hint";
    }

    double FieldW => Math.Max(280, playField.Bounds.Width > 0 ? playField.Bounds.Width : (View.Bounds.Width > 0 ? View.Bounds.Width : 1180));
    double FieldH => Math.Max(240, playField.Bounds.Height > 0 ? playField.Bounds.Height : (View.Bounds.Height > 0 ? View.Bounds.Height - 190 : 520));

    void Begin()
    {
        if (G.Net) Family.Game = this;
        Layout(View.Bounds.Width > 0 ? View.Bounds.Width : 1180);
        // the field gets its size on the first layout: start the game then
        Dispatcher.UIThread.Post(() =>
        {
            switch (G.Kind)
            {
                case "asteroid": InitAsteroid(); break;
                case "frog": InitFrog(); break;
                case "pizza": InitPizza(); break;
                case "race": InitRace(); break;
                case "boss": InitBoss(); break;
                default: InitPractice(); break;
            }
            timer.Start();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>For the checks: starts the game now (no waiting for layout).</summary>
    public void BeginNowForTest()
    {
        Dispatcher.UIThread.RunJobs();
    }

    public void Closed()
    {
        timer.Stop();
        if (Family.Game == this) Family.Game = null;
        if (!G.Over && G.Net && Family.Mode == NetMode.Host) Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "end", ["text"] = Lg.Me?.Name + " ended the game." });
        G.Over = true;
        Lg.StopSpeaking();
        Profiles.Save();
    }

    void Quit() { MainView.Current.Back(); }

    static void Put(Control c, double x, double y) { Canvas.SetLeft(c, x); Canvas.SetTop(c, y); }
    static IBrush B(string hex, byte a = 255) => Ui.Brush(hex, a);
    static TextBlock T(string text, double size, string color, bool bold = false) { var t = new TextBlock { Text = text, FontSize = size, Foreground = B(color) }; if (bold) t.FontWeight = FontWeight.Bold; return t; }
    static Size Measure(Control c) { c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return c.DesiredSize; }

    void SetLives() { lblLives.Text = G.Lives > 0 ? new string('♥', G.Lives) : ""; }
    void Toast(string text, string color = Ui.TextC, double seconds = 1.2) { lblToast.Text = text; lblToast.Foreground = B(color); toast.IsVisible = true; toastUntil = DateTime.Now.AddSeconds(seconds); }
    void Later(int ms, Action a) { G.Busy = true; G.NextAt = DateTime.Now.AddMilliseconds(ms); G.After = a; }
    void Hud() { lblScore.Text = G.Score.ToString(); lblStreak.Text = G.Streak.ToString(); }

    public string QuestionText => lblQuestion.Text;
    public Canvas Field => playField;

    // ---------------------------------------------------------------- problems and answers
    Problem NextProblem(bool quick)
    {
        var me = Lg.Me;
        string skill = G.Skill ?? Lg.PickSkill(me, G.Grade, quick);
        var st = me.Skill(skill);
        var p = Problems.Make(skill, st.Level);
        G.Problem = p; G.HintIdx = 0; G.WrongThis = 0; hintCard.IsVisible = false;
        G.Round++;
        lblRound.Text = G.Rounds > 0 ? G.Round + " / " + G.Rounds : "";
        lblQuestion.Text = p.Text;
        // word problems are long: a smaller font so they fit
        double size = p.Text.Length <= 60 ? 28 : p.Text.Length <= 130 ? 21 : 18;
        lblQuestion.FontSize = narrow ? Math.Min(size, p.Text.Length <= 60 ? 22 : 16) : size;
        DrawVisual(p);
        // 2nd grade stories are read aloud (the speaker button reads any question again)
        if (me.Grade > 0 && me.Grade <= 2 && p.Text.Length > 60 && G.Kind == "practice") Lg.Speak(Lg.SayText(p.Text));
        return p;
    }

    void ShowAnswers(Problem p, bool typedAnswer)
    {
        choices.Children.Clear();
        if (typedAnswer && p.Kind != "choice")
        {
            typed.IsVisible = true; choices.IsVisible = false;
            answer.Text = ""; lblUnit.Text = p.Unit;
            BuildPad(p);
            if (!Services.Platform.Touch) answer.Focus();
        }
        else
        {
            typed.IsVisible = false; choices.IsVisible = true;
            int i = 0;
            foreach (var c in p.Choices)
            {
                i++;
                var b = ChoiceButton(c);
                ToolTip.SetTip(b, "Key " + i);
                string cc = c;
                b.Click += (s, e) => Submit(cc);
                choices.Children.Add(b);
            }
        }
    }

    Button ChoiceButton(string text)
        => new()
        {
            Content = text, FontSize = narrow ? 20 : 24, FontWeight = FontWeight.SemiBold, MinWidth = narrow ? 80 : 120, MinHeight = narrow ? 52 : 62, Margin = new Thickness(6),
            Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(10), Background = B(Ui.Surface3), BorderBrush = B(Ui.BorderStrong), BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };

    void BuildPad(Problem p)
    {
        pad.Children.Clear();
        // two keys change with the answer: a space for mixed numbers, R for remainders
        string k1 = p.Kind == "mixed" || p.Kind == "fraction" || p.Kind == "simplest" ? " " : ":";
        string k2 = p.Kind == "qr" ? " R " : "$";
        foreach (var k in new[] { "7", "8", "9", "/", "4", "5", "6", ".", "1", "2", "3", "-", "0", k1, k2, "⌫" })
        {
            string label = k == " " ? "␣" : k.Trim();
            var b = new Button { Content = label, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(3), Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false, CornerRadius = new CornerRadius(8), Background = B(Ui.Surface3) };
            if (k == " ") ToolTip.SetTip(b, "Space (for mixed numbers like 1 3/4)");
            if (k == "⌫") ToolTip.SetTip(b, "Delete");
            string kk = k;
            b.Click += (s, e) =>
            {
                string t = answer.Text ?? "";
                if (kk == "⌫") { if (t.Length > 0) answer.Text = t.Substring(0, t.Length - 1); }
                else answer.Text = t + kk;
                answer.CaretIndex = (answer.Text ?? "").Length;
                if (!Services.Platform.Touch) answer.Focus();
            };
            pad.Children.Add(b);
        }
    }

    void ShowHint()
    {
        var p = G.Problem;
        if (p == null || p.Hints.Length == 0) return;
        lblHint.Text = p.Hints[Math.Min(G.HintIdx, p.Hints.Length - 1)];
        if (G.HintIdx < p.Hints.Length - 1) G.HintIdx++;
        hintCard.IsVisible = true;
    }

    string Record(string skill, bool ok, string text, string answerText, string given)
    {
        var me = Lg.Me;
        string change = me.RecordAnswer(skill, ok, text, answerText, given);
        Family.SendActivity(me, skill, text, answerText, given, ok);
        return change;
    }

    /// <summary>One answer from the player: checks it, scores, adapts the level, tells the game.</summary>
    public void Submit(string given)
    {
        if (G.Busy || G.Problem == null || G.Over || G.Spectator) return;
        var p = G.Problem;
        bool ok = p.IsCorrect(given);
        string change = Record(p.Skill, ok, p.Text, p.Answer, given);
        if (change == "up") G.LevelUps.Add(Profiles.SkillName(p.Skill));
        if (ok)
        {
            G.Right++; G.Streak++; G.BestStreak = Math.Max(G.BestStreak, G.Streak);
            G.Score += 10 + 5 * Math.Min(5, G.Streak - 1) + (G.WrongThis == 0 && G.HintIdx == 0 ? 5 : 0);
            if (G.Streak >= 5 && G.Streak % 5 == 0 && Lg.Me.Grade > 0) Lg.Me.AddRewardPoints(2);   // a streak bonus (Praise says so)
            Snd.Correct();
        }
        else
        {
            G.Wrong++; G.Streak = 0; G.WrongThis++;
            Snd.Wrong();
            ShowHint();   // a wrong answer always brings the next hint step
        }
        Hud();
        MainView.Current.RefreshBar();
        switch (G.Kind)
        {
            case "asteroid": AnswerAsteroid(given, ok); break;
            case "race": AnswerRace(ok); break;
            case "boss": AnswerBoss(ok); break;
            case "pizza": AnswerPizza(ok); break;
            case "practice": AnswerPractice(ok); break;
        }
    }

    void Finish(string title, string text)
    {
        if (G.Over) return;
        G.Over = true;
        timer.Stop();
        if (Family.Game == this) Family.Game = null;
        Lg.StopSpeaking();
        MainView.Current.Replace(new ResultsPage(G, title, text));
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (G.Over || G.Problem == null) return false;
        int i = QuizPage.KeyNum(e.Key);
        if (i < 0) return false;
        if (G.Kind == "asteroid" || (choices.IsVisible && choices.Children.Count > 0 && G.Kind != "pizza" && G.Kind != "frog"))
        {
            if (i < G.Problem.Choices.Length) Submit(G.Problem.Choices[i]);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- the clock
    public void Tick()
    {
        if (G.Over) return;
        G.Frame++;
        switch (G.Kind)
        {
            case "asteroid": UpdateAsteroid(); break;
            case "frog": UpdateFrog(); break;
            case "race": UpdateRace(); break;
            case "boss": UpdateBoss(); break;
        }
        UpdateFx();
        if (G.Over) return;
        if (DateTime.Now >= G.NextAt) { G.NextAt = DateTime.MaxValue; var a = G.After; G.After = null; a?.Invoke(); }
        if (toast.IsVisible && DateTime.Now > toastUntil) toast.IsVisible = false;
        if (DateTime.Now > bigUntil) { lblBig.IsVisible = false; bigUntil = DateTime.MaxValue; }
    }

    /// <summary>For the checks: runs whatever was waiting (the next problem after a right answer) now.</summary>
    public void RunPendingForTest() { G.NextAt = DateTime.Now.AddMilliseconds(-1); Tick(); }

    // ---------------------------------------------------------------- pictures that go with a problem
    void DrawVisual(Problem p)
    {
        if (visual != null) { playField.Children.Remove(visual); visual = null; }
        if (string.IsNullOrEmpty(p.Visual) || G.Kind == "asteroid" || G.Kind == "frog" || G.Kind == "race" || G.Kind == "boss") return;
        var c = OldVisual(p);
        if (c != null)
        {
            visual = c;
            double scale = Math.Min(1, (FieldW - 24) / 380);
            if (scale < 1) c.RenderTransform = new ScaleTransform(scale, scale);
            if (scale < 1) c.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            Put(c, 12, 12);
            playField.Children.Add(c);
            return;
        }
        // the newer pictures (Visuals.cs): centred at the top of the field
        var pic = Visuals.Draw(p.Visual);
        if (pic == null) return;
        visual = pic;
        double w = double.IsNaN(pic.Width) ? Measure(pic).Width : pic.Width;
        double sc = Math.Min(1, (FieldW - 24) / Math.Max(1, w));
        if (sc < 1) { pic.RenderTransform = new ScaleTransform(sc, sc); pic.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative); }
        Put(pic, Math.Max(12, (FieldW - w * sc) / 2), 14);
        playField.Children.Add(pic);
    }

    /// <summary>The picture for a problem (the older ones drawn here, the newer ones by Visuals), or null.</summary>
    public static Control PictureFor(Problem p)
    {
        if (string.IsNullOrEmpty(p.Visual)) return null;
        return (Control)OldVisual(p) ?? Visuals.Draw(p.Visual);
    }

    /// <summary>The older pictures (array, coins, clock, place value, shapes, number line, box, ruler).</summary>
    static Canvas OldVisual(Problem p)
    {
        string[] kv = p.Visual.Split(':', 2);
        string kind = kv[0], arg = kv.Length > 1 ? kv[1] : "";
        var c = new Canvas { Width = 360, Height = 230 };
        int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        switch (kind)
        {
            case "array":
            {
                var rk = arg.Split('x'); int r = I(rk[0]), k = I(rk[1]);
                for (int i = 0; i < r; i++) for (int j = 0; j < k; j++) { var e = new Ellipse { Width = 26, Height = 26, Fill = B("#60A5FA") }; Put(e, 20 + j * 38, 20 + i * 38); c.Children.Add(e); }
                return c;
            }
            case "coins":
            {
                double x = 10, y = 20;
                foreach (var vs in arg.Split(','))
                {
                    int v = I(vs);
                    double sz = v switch { 1 => 38, 5 => 44, 10 => 34, 25 => 52, _ => 0 };
                    Shape e; string lbl;
                    if (v == 100) { e = new Rectangle { Width = 80, Height = 40, RadiusX = 4, RadiusY = 4, Fill = B("#86EFAC") }; lbl = "$1"; }
                    else { e = new Ellipse { Width = sz, Height = sz, Fill = B(v == 1 ? "#D97706" : "#CBD5E1") }; lbl = v + "¢"; }
                    e.Stroke = B(Ui.Bg); e.StrokeThickness = 2;
                    Put(e, x, y); c.Children.Add(e);
                    var t = T(lbl, 13, Ui.Bg, true); var ts = Measure(t);
                    Put(t, x + (e.Width - ts.Width) / 2, y + (e.Height - ts.Height) / 2); c.Children.Add(t);
                    x += e.Width + 10; if (x > 300) { x = 10; y += 64; }
                }
                return c;
            }
            case "clock":
            {
                var hm = arg.Split(':'); int h = I(hm[0]), m = I(hm[1]);
                double cx = 120, cy = 115, rad = 100;
                var face = new Ellipse { Width = 2 * rad, Height = 2 * rad, Fill = B("#F8FAFC"), Stroke = B("#3B82F6"), StrokeThickness = 6 };
                Put(face, cx - rad, cy - rad); c.Children.Add(face);
                for (int n = 1; n <= 12; n++)
                {
                    double a = n * Math.PI / 6 - Math.PI / 2;
                    var t = T(n.ToString(), 17, Ui.Bg); t.FontWeight = FontWeight.SemiBold; var ts = Measure(t);
                    Put(t, cx + 78 * Math.Cos(a) - ts.Width / 2, cy + 78 * Math.Sin(a) - ts.Height / 2); c.Children.Add(t);
                }
                foreach (var (ang, len, wd) in new[] { ((h % 12 + m / 60.0) * Math.PI / 6, 52.0, 7.0), (m * Math.PI / 30, 80.0, 4.0) })
                    c.Children.Add(new Line { StartPoint = new Point(cx, cy), EndPoint = new Point(cx + len * Math.Sin(ang), cy - len * Math.Cos(ang)), Stroke = B(Ui.Bg), StrokeThickness = wd, StrokeLineCap = PenLineCap.Round });
                return c;
            }
            case "place":
            {
                string digits = I(arg).ToString(CultureInfo.InvariantCulture);
                string[] names = digits.Length == 3 ? new[] { "hundreds", "tens", "ones" } : new[] { "tens", "ones" };
                for (int i = 0; i < digits.Length && i < names.Length; i++)
                {
                    var d = T(digits[i].ToString(), 44, Ui.TextC, true); d.HorizontalAlignment = HorizontalAlignment.Center;
                    var l = T(names[i], 13, Ui.AccentText); l.HorizontalAlignment = HorizontalAlignment.Center;
                    var b = new Border { Width = 96, Height = 110, CornerRadius = new CornerRadius(12), Background = B("#1B2D50"), BorderBrush = B("#3B82F6"), BorderThickness = new Thickness(1.5), Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { d, l } } };
                    Put(b, 10 + i * 108, 30); c.Children.Add(b);
                }
                return c;
            }
            case "rect": case "tri": case "para":
            {
                var wh = arg.Split('x'); int bw = I(wh[0]), bh = I(wh[1]);
                double sc = Math.Min(220.0 / bw, 150.0 / bh), w = bw * sc, hh = bh * sc, x0 = 40, y0 = 30;
                Point[] pts = kind == "rect" ? new[] { new Point(x0, y0), new Point(x0 + w, y0), new Point(x0 + w, y0 + hh), new Point(x0, y0 + hh) }
                    : kind == "tri" ? new[] { new Point(x0 + w * 0.35, y0), new Point(x0 + w, y0 + hh), new Point(x0, y0 + hh) }
                    : new[] { new Point(x0 + 40, y0), new Point(x0 + w + 40, y0), new Point(x0 + w, y0 + hh), new Point(x0, y0 + hh) };
                c.Children.Add(new Polygon { Points = new AvaloniaList<Point>(pts), Fill = B("#3B82F6", 60), Stroke = B("#60A5FA"), StrokeThickness = 2.5 });
                var tb = T("base " + bw, 14, Ui.AccentText); Put(tb, x0 + w / 2 - 25, y0 + hh + 6); c.Children.Add(tb);
                if (kind != "rect")
                {
                    double hx = kind == "tri" ? x0 + w * 0.35 : x0 + 40;
                    c.Children.Add(new Line { StartPoint = new Point(hx, y0), EndPoint = new Point(hx, y0 + hh), Stroke = B(Ui.Yellow), StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 4, 3 } });
                }
                var th = T("height " + bh, 14, Ui.Yellow); Put(th, x0 + w + (kind == "para" ? 48 : 8), y0 + hh / 2 - 10); c.Children.Add(th);
                return c;
            }
            case "line":
            {
                // a number line from lo to hi; the numbers in the question that sit on it get a dot (only the start for + and -)
                var lh = arg.Split(','); int lo = I(lh[0]), hi = I(lh[1]);
                int range = hi - lo; if (range <= 0) return null;
                double lx0 = 20, lx1 = 340, ly = 120;
                int tickEvery = range <= 24 ? 1 : 2, labelEvery = range <= 24 ? 4 : 8;
                c.Children.Add(new Line { StartPoint = new Point(lx0 - 8, ly), EndPoint = new Point(lx1 + 8, ly), Stroke = B(Ui.TextC), StrokeThickness = 2.5 });
                for (int v = lo; v <= hi; v += tickEvery)
                {
                    double tx = lx0 + (lx1 - lx0) * (v - lo) / range; bool big = v % labelEvery == 0;
                    c.Children.Add(new Line { StartPoint = new Point(tx, ly - (big ? 9 : 5)), EndPoint = new Point(tx, ly + (big ? 9 : 5)), Stroke = B(v == 0 ? Ui.Yellow : Ui.TextC), StrokeThickness = v == 0 ? 2.5 : 1.5 });
                    if (big) { var t = T(v.ToString(CultureInfo.InvariantCulture), 12, v == 0 ? Ui.Yellow : Ui.Muted); var ts = Measure(t); Put(t, tx - ts.Width / 2, ly + 12); c.Children.Add(t); }
                }
                string text = p.Text.Replace('−', '-');
                var nums = Regex.Matches(text, @"-?\d+").Select(mm => int.Parse(mm.Value, CultureInfo.InvariantCulture)).ToList();
                var marks = Regex.IsMatch(p.Text, "\\d\\s*[+−]\\s*\\(?-?\\d") ? nums.Take(1).ToList() : nums.Distinct().ToList();
                foreach (int v in marks)
                {
                    if (v < lo || v > hi) continue;
                    double tx = lx0 + (lx1 - lx0) * (v - lo) / range;
                    var dot = new Ellipse { Width = 14, Height = 14, Fill = B("#60A5FA"), Stroke = B(Ui.Bg), StrokeThickness = 2 }; Put(dot, tx - 7, ly - 7); c.Children.Add(dot);
                    var t = T(v.ToString(CultureInfo.InvariantCulture), 16, Ui.AccentText, true); var ts = Measure(t); Put(t, tx - ts.Width / 2, ly - 38); c.Children.Add(t);
                }
                return c;
            }
            case "box":
            {
                // a box in a simple 3-D view with its length, width and height written on the edges
                var lwh = arg.Split('x'); int bl = I(lwh[0]), bw = I(lwh[1]), bh = I(lwh[2]);
                double k = 0.5, sc = Math.Min(210.0 / (bl + bw * k), 150.0 / (bh + bw * k));
                double fw = bl * sc, fh = bh * sc, dx = bw * sc * k, dy = bw * sc * k, bx = 76, by = 30 + dy;
                var faces = new (string F, byte A, Point[] P)[]
                {
                    ("#3B82F6", 70, new[] { new Point(bx, by), new Point(bx + fw, by), new Point(bx + fw, by + fh), new Point(bx, by + fh) }),
                    ("#60A5FA", 90, new[] { new Point(bx, by), new Point(bx + dx, by - dy), new Point(bx + fw + dx, by - dy), new Point(bx + fw, by) }),
                    ("#1D4ED8", 90, new[] { new Point(bx + fw, by), new Point(bx + fw + dx, by - dy), new Point(bx + fw + dx, by + fh - dy), new Point(bx + fw, by + fh) }),
                };
                foreach (var f in faces) c.Children.Add(new Polygon { Points = new AvaloniaList<Point>(f.P), Fill = B(f.F, f.A), Stroke = B(Ui.AccentText), StrokeThickness = 2, StrokeJoin = PenLineJoin.Round });
                var tl = T("length " + bl, 14, Ui.AccentText); var tls = Measure(tl); Put(tl, bx + fw / 2 - tls.Width / 2, by + fh + 6); c.Children.Add(tl);
                var th = T("height " + bh, 14, Ui.Yellow); var ths = Measure(th);
                if (bx - ths.Width - 6 < 0) Put(th, bx + fw + dx + 8, by + fh / 2 - dy / 2 - 10); else Put(th, bx - ths.Width - 6, by + fh / 2 - 10);
                c.Children.Add(th);
                var twd = T("width " + bw, 14, Ui.Green); var tws = Measure(twd); Put(twd, Math.Max(0, bx + dx / 2 - tws.Width - 4), by - dy / 2 - 20); c.Children.Add(twd);
                return c;
            }
            case "ruler":
            {
                // a ruler from 0 to max (inches or centimeters) with a strip lying on it from 0 to len
                var lmu = arg.Split(','); int len = I(lmu[0]), max = I(lmu[1]); string unit = lmu.Length > 2 ? lmu[2] : "in";
                double rx0 = 20, unitW = 320.0 / max, ry = 110;
                var strip = new Rectangle { Width = len * unitW, Height = 26, RadiusX = 6, RadiusY = 6, Fill = B("#F97316"), Stroke = B(Ui.Bg), StrokeThickness = 2 };
                Put(strip, rx0, ry - 40); c.Children.Add(strip);
                var body = new Rectangle { Width = 336, Height = 54, RadiusX = 4, RadiusY = 4, Fill = B("#FDE68A"), Stroke = B("#B45309"), StrokeThickness = 2 };
                Put(body, rx0 - 8, ry); c.Children.Add(body);
                for (int v = 0; v <= max; v++)
                {
                    double tx = rx0 + v * unitW;
                    c.Children.Add(new Line { StartPoint = new Point(tx, ry), EndPoint = new Point(tx, ry + 16), Stroke = B(Ui.Bg), StrokeThickness = 2 });
                    if (unit == "in" && v < max) c.Children.Add(new Line { StartPoint = new Point(tx + unitW / 2, ry), EndPoint = new Point(tx + unitW / 2, ry + 9), Stroke = B(Ui.Bg), StrokeThickness = 1 });
                    var t = T(v.ToString(CultureInfo.InvariantCulture), 12, Ui.Bg); t.FontWeight = FontWeight.SemiBold; var ts = Measure(t); Put(t, tx - ts.Width / 2, ry + 18); c.Children.Add(t);
                }
                var ul = T(unit == "cm" ? "centimeters" : "inches", 12, Ui.AccentText); Put(ul, rx0, ry + 60); c.Children.Add(ul);
                return c;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- practice (one skill, typed answers)
    void InitPractice()
    {
        G.Rounds = 10; G.Lives = 0;
        var p = NextProblem(false); ShowAnswers(p, true);
        lblLives.Text = "";
    }

    void AnswerPractice(bool ok)
    {
        if (ok)
        {
            Toast(Lg.Praise(G.Streak), Ui.Green);
            Later(700, PracticeNext);
        }
        else if (G.WrongThis >= 3)
        {
            Toast("The answer is " + G.Problem.Answer, Ui.Yellow, 2.5);
            Later(2200, PracticeNext);
        }
        else { Toast("Not quite. Look at the hint and try again!", Ui.Yellow); answer.SelectAll(); if (!Services.Platform.Touch) answer.Focus(); }
    }

    void PracticeNext()
    {
        if (G.Round >= G.Rounds) { Finish("Practice done!", Profiles.SkillName(G.Skill)); return; }
        var p = NextProblem(false); ShowAnswers(p, true); G.Busy = false;
    }

    // ---------------------------------------------------------------- effects: fading lasers, sparks, fireballs
    sealed class Fx { public Control E; public int Life; public string Kind = ""; public double X, Y, VX, VY, TX, TY; }
    readonly List<Fx> fx = new();

    void UpdateFx()
    {
        foreach (var f in fx.ToList())
        {
            f.Life--;
            switch (f.Kind)
            {
                case "fade": f.E.Opacity = f.Life / 8.0; break;
                case "spark": f.X += f.VX; f.Y += f.VY; f.VY += 0.25; Put(f.E, f.X, f.Y); f.E.Opacity = Math.Max(0, f.Life / 22.0); break;
                case "shot":
                    double t = 1 - f.Life / 18.0; Put(f.E, f.X + (f.TX - f.X) * t, f.Y + (f.TY - f.Y) * t);
                    if (f.Life == 0) { Explode(f.TX, f.TY, "#F97316"); Snd.Boom(); }
                    break;
            }
            if (f.Life <= 0) { playField.Children.Remove(f.E); fx.Remove(f); }
        }
    }

    void Explode(double x, double y, string color)
    {
        for (int i = 0; i < 14; i++)
        {
            double s = 8 + Lg.Rand.Next(8);
            var e = new Ellipse { Width = s, Height = s, Fill = B(color), IsHitTestVisible = false };
            Put(e, x, y); playField.Children.Add(e);
            double a = Lg.Rand.NextDouble() * 2 * Math.PI, sp = 3 + Lg.Rand.NextDouble() * 6;
            fx.Add(new Fx { E = e, Life = 22, Kind = "spark", X = x, Y = y, VX = sp * Math.Cos(a), VY = sp * Math.Sin(a) });
        }
    }

    // ---------------------------------------------------------------- Asteroid Blaster
    sealed class Rock { public Grid E; public TextBlock Label; public double X, Y, V; public string Value = ""; public int Spin; public bool Dead; }
    sealed class Star { public Ellipse E; public double Y, V; }
    readonly List<Rock> rocks = new();
    readonly List<Star> stars = new();
    Path ship;

    void InitAsteroid()
    {
        G.Rounds = 15; G.Lives = 3; SetLives();
        double w = FieldW, h = FieldH;
        for (int i = 0; i < 70; i++)
        {
            double s = Lg.Rand.NextDouble() * 2.2 + 0.6;
            var e = new Ellipse { Width = s, Height = s, Fill = B("#CBD5E1", (byte)(60 + Lg.Rand.Next(150))), IsHitTestVisible = false };
            double y = Lg.Rand.NextDouble() * h;
            Put(e, Lg.Rand.NextDouble() * w, y); playField.Children.Add(e);
            stars.Add(new Star { E = e, Y = y, V = s * 0.35 });
        }
        ship = new Path { Data = Geometry.Parse("M 30,0 L 60,52 L 30,40 L 0,52 Z"), Fill = B("#60A5FA"), Stroke = B(Ui.AccentText), StrokeThickness = 2 };
        Put(ship, w / 2 - 30, h - 70); playField.Children.Add(ship);
        NextWave();
    }

    void NextWave()
    {
        foreach (var r in rocks) playField.Children.Remove(r.E);
        rocks.Clear();
        if (G.Round >= G.Rounds || G.Lives <= 0) { Finish(G.Lives > 0 ? "Space is safe!" : "Out of shields!", "Asteroid Blaster"); return; }
        var p = NextProblem(true);
        choices.Children.Clear(); typed.IsVisible = false; choices.IsVisible = true;
        var tip = Ui.Text(Services.Platform.Touch ? "Tap the rock with the right answer" : "Click the rock with the right answer (or press 1 to 4)", 15, Ui.Muted); tip.Margin = new Thickness(0, 12);
        choices.Children.Add(tip);
        double w = FieldW; int n = p.Choices.Length;
        var st = Lg.Me.Skill(p.Skill);
        double speed = FieldH / ((14 - 1.6 * st.Level) * 30);   // pixels per frame: 12 s to fall at level 1, about 6 s at level 5
        double sz = Math.Min(112, w / Math.Max(1, n) - 8);
        for (int i = 0; i < n; i++)
        {
            var box = new Grid { Width = sz, Height = sz, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            var pts = new List<string>();
            for (int k = 0; k < 10; k++) { double a = k * Math.PI / 5, rr = sz * 0.446 + Lg.Rand.Next(-7, 7) * sz / 112; pts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", sz / 2 + rr * Math.Cos(a), sz / 2 + rr * Math.Sin(a))); }
            var rb = new RadialGradientBrush();
            rb.GradientStops.Add(new GradientStop(Color.Parse("#64748B"), 0));
            rb.GradientStops.Add(new GradientStop(Color.Parse("#334155"), 1));
            var rock = new Path { Data = Geometry.Parse("M " + pts[0] + " L " + string.Join(" ", pts.Skip(1)) + " Z"), Fill = rb, Stroke = B("#94A3B8"), StrokeThickness = 2 };
            box.Children.Add(rock);
            var t = new TextBlock { Text = p.Choices[i], FontSize = p.Choices[i].Length > 5 ? 18 : 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            box.Children.Add(t);
            if (!Services.Platform.Touch) box.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 12, Foreground = B("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) });
            double x = (w / n) * i + (w / n - sz) / 2 + Lg.Rand.Next(-20, 20) * Math.Min(1, sz / 112);
            double y = -sz * 0.6 - Lg.Rand.Next(0, 50);
            Put(box, x, y);
            string value = p.Choices[i];
            box.PointerPressed += (s, e) => { e.Handled = true; Submit(value); };
            playField.Children.Add(box);
            rocks.Add(new Rock { E = box, Label = t, X = x, Y = y, V = speed * (0.9 + Lg.Rand.NextDouble() * 0.25), Value = value, Spin = Lg.Rand.Next(-2, 3) });
        }
        G.Busy = false;
    }

    void AnswerAsteroid(string given, bool ok)
    {
        var rock = rocks.FirstOrDefault(r => r.Value == given && !r.Dead);
        double half = rock != null ? rock.E.Width / 2 : 56;
        if (rock != null) ShootLaser(rock.X + half, rock.Y + half, ok ? "#60A5FA" : Ui.Red);
        if (ok)
        {
            if (rock != null) { Explode(rock.X + half, rock.Y + half, Ui.Yellow); rock.Dead = true; playField.Children.Remove(rock.E); }
            Toast(Lg.Praise(G.Streak), Ui.Green, 0.8);
            foreach (var r in rocks) r.V *= 3.5;   // the others fly off
            Later(800, NextWave);
        }
        else
        {
            if (rock != null) { rock.Dead = true; Explode(rock.X + half, rock.Y + half, Ui.Red); playField.Children.Remove(rock.E); }
            G.Lives--; SetLives();
            if (G.Lives <= 0) Later(900, () => Finish("Out of shields!", "The answer was " + G.Problem.Answer + "."));
        }
    }

    void ShootLaser(double x, double y, string color)
    {
        double sx = Canvas.GetLeft(ship) + 30, sy = Canvas.GetTop(ship);
        var l = new Line { StartPoint = new Point(sx, sy), EndPoint = new Point(x, y), Stroke = B(color), StrokeThickness = 4, StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false };
        playField.Children.Add(l);
        fx.Add(new Fx { E = l, Life = 8, Kind = "fade" });
        Snd.Pop();
    }

    void UpdateAsteroid()
    {
        double h = FieldH;
        foreach (var s in stars) { s.Y += s.V; if (s.Y > h) s.Y = -3; Canvas.SetTop(s.E, s.Y); }
        if (rocks.Count == 0) return;
        Rock lost = null;
        foreach (var r in rocks)
        {
            if (r.Dead) continue;
            r.Y += r.V;
            Canvas.SetTop(r.E, r.Y);
            r.E.RenderTransform = new RotateTransform(G.Frame * r.Spin * 0.4);
            r.Label.RenderTransform = new RotateTransform(-G.Frame * r.Spin * 0.4);
            if (r.Y > h - 60 && !G.Busy && G.Problem.IsCorrect(r.Value)) lost = r;
        }
        if (lost != null)
        {
            lost.Dead = true; playField.Children.Remove(lost.E);
            Explode(lost.X + lost.E.Width / 2, h - 40, Ui.Red); Snd.Boom();
            G.Lives--; SetLives(); G.Streak = 0; lblStreak.Text = "0";
            Record(G.Problem.Skill, false, G.Problem.Text, G.Problem.Answer, "(too slow)"); G.Wrong++;
            Toast("Too slow! It was " + G.Problem.Answer, Ui.Yellow, 1.6);
            Later(1500, NextWave);
        }
    }

    // ---------------------------------------------------------------- Number Line Frog
    sealed class Pad { public Ellipse E; public double X, Value; }
    readonly List<Pad> pads = new();
    Problems.LinePuzzle line;
    Canvas frog;
    double frogX, frogY;
    (double X0, double X1, int T, int N, bool Ok, int Index)? hop;

    void InitFrog()
    {
        G.Rounds = 10; G.Lives = 0;
        choices.Children.Clear(); typed.IsVisible = false; choices.IsVisible = true;
        var tip = Ui.Text(Services.Platform.Touch ? "Tap the lily pad where the number belongs" : "Click the lily pad where the number belongs", 15, Ui.Muted); tip.Margin = new Thickness(0, 12);
        choices.Children.Add(tip);
        NextFrog();
    }

    void NextFrog()
    {
        if (G.Round >= G.Rounds) { Finish("The frog made it home!", "Number Line Frog"); return; }
        playField.Children.Clear(); pads.Clear(); fx.Clear();
        var st = Lg.Me.Skill("line");
        var q = Problems.Line(G.Grade, st.Level);
        line = q; G.Round++; G.WrongThis = 0; G.HintIdx = 0; hintCard.IsVisible = false;
        lblRound.Text = G.Round + " / " + G.Rounds; lblQuestion.Text = q.Text;
        // a problem for hints and the family room, like the other games
        G.Problem = new Problem { Skill = "line", Text = q.Text, Answer = q.Label };
        int steps = (int)Math.Round((q.Hi - q.Lo) / q.Step);
        G.Problem.Hints = new[]
        {
            "The line goes from " + Answers.Num(q.Lo) + " to " + Answers.Num(q.Hi) + ". Each small jump is " + (q.Step < 1 ? "1/" + Math.Round(1 / q.Step) : Answers.Num(q.Step)) + ".",
            "Start at a number you know and count the jumps to " + q.Label + ".",
        };
        double w = FieldW, h = FieldH;
        var water = new Rectangle { Width = w, Height = h * 0.45, Fill = B("#0C4A6E", 120), IsHitTestVisible = false };
        Put(water, 0, h * 0.55); playField.Children.Add(water);
        double x0 = Math.Min(70, w * 0.08), x1 = w - x0, y = h * 0.6;
        playField.Children.Add(new Line { StartPoint = new Point(x0, y), EndPoint = new Point(x1, y), Stroke = B(Ui.TextC), StrokeThickness = 3 });
        for (int i = 0; i <= steps; i++)
        {
            double v = q.Lo + i * q.Step, x = x0 + (x1 - x0) * i / steps;
            bool whole = Math.Abs(v - Math.Round(v)) < 1e-9;
            bool labelIt = i == 0 || i == steps || (q.Step < 1 && whole) || (q.Step >= 1 && steps <= 20 && i % 5 == 0) || (G.Grade == 6 && q.Step == 1 && v == 0);
            playField.Children.Add(new Line { StartPoint = new Point(x, y - (labelIt ? 14 : 8)), EndPoint = new Point(x, y + (labelIt ? 14 : 8)), Stroke = B(Ui.TextC), StrokeThickness = 2 });
            if (labelIt) { var t = T(Answers.Num(v), 17, Ui.TextC); t.FontWeight = FontWeight.SemiBold; var ts = Measure(t); Put(t, x - ts.Width / 2, y + 18); playField.Children.Add(t); }
            double pw = Math.Min(46, (x1 - x0) / steps * 0.9);
            var pad = new Ellipse { Width = pw, Height = pw * 0.55, Fill = B("#22C55E", 200), Stroke = B("#86EFAC"), StrokeThickness = 1.5, Cursor = new Cursor(StandardCursorType.Hand) };
            Put(pad, x - pw / 2, y - 44 - pad.Height / 2);
            int idx = i;
            pad.PointerPressed += (s, e) => { e.Handled = true; JumpFrog(idx); };
            // a bigger invisible target around each pad, for fingers
            var target = new Rectangle { Width = Math.Max(pw, Math.Min(44, (x1 - x0) / steps)), Height = 56, Fill = Brushes.Transparent };
            Put(target, x - target.Width / 2, y - 72);
            target.PointerPressed += (s, e) => { e.Handled = true; JumpFrog(idx); };
            playField.Children.Add(target);
            playField.Children.Add(pad);
            pads.Add(new Pad { E = pad, X = x, Value = v });
        }
        frog = NewFrog(); frogX = x0; frogY = y - 70;
        Put(frog, frogX - 26, frogY); playField.Children.Add(frog);
        G.Busy = false;
    }

    static Canvas NewFrog()
    {
        var c = new Canvas { Width = 52, Height = 44, IsHitTestVisible = false };
        var b = new Ellipse { Width = 52, Height = 36, Fill = B("#16A34A"), Stroke = B("#14532D"), StrokeThickness = 2 }; Put(b, 0, 8); c.Children.Add(b);
        foreach (double ex in new[] { 6.0, 30.0 })
        {
            var e = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, Stroke = B("#14532D"), StrokeThickness = 2 }; Put(e, ex, 0); c.Children.Add(e);
            var p = new Ellipse { Width = 6, Height = 6, Fill = B(Ui.Bg) }; Put(p, ex + 6, 5); c.Children.Add(p);
        }
        return c;
    }

    public void JumpFrog(int index)
    {
        if (G.Busy || G.Over || G.Spectator) return;
        var pad = pads[index];
        bool ok = Math.Abs(pad.Value - line.Target) < 1e-9;
        G.Busy = true;
        hop = (frogX, pad.X, 0, 14, ok, index);
        Snd.Pop();
        string change = Record("line", ok, line.Text, line.Label, Answers.Num(pad.Value));
        if (change == "up") G.LevelUps.Add("Number line");
        if (ok) { G.Right++; G.Streak++; G.BestStreak = Math.Max(G.BestStreak, G.Streak); G.Score += 10 + 5 * Math.Min(5, G.Streak - 1); }
        else { G.Wrong++; G.Streak = 0; G.WrongThis++; }
        Hud();
        MainView.Current.RefreshBar();
    }

    /// <summary>For the checks: the pad index where the number belongs.</summary>
    public int RightPadForTest() => pads.FindIndex(p => Math.Abs(p.Value - line.Target) < 1e-9);

    void UpdateFrog()
    {
        if (hop == null) return;
        var hp = hop.Value;
        hp.T++;
        double t = hp.T / (double)hp.N;
        double x = hp.X0 + (hp.X1 - hp.X0) * t, yy = frogY - Math.Sin(Math.PI * t) * 90;
        Put(frog, x - 26, yy);
        hop = hp;
        if (hp.T < hp.N) return;
        frogX = hp.X1; hop = null;
        if (hp.Ok)
        {
            Snd.Correct(); Toast(Lg.Praise(G.Streak), Ui.Green, 0.9);
            pads[hp.Index].E.Fill = B(Ui.Yellow);
            Later(900, NextFrog);
        }
        else
        {
            Snd.Wrong();
            pads[hp.Index].E.Fill = B(Ui.Red, 200);
            if (G.WrongThis >= 2)
            {
                var right = pads.FirstOrDefault(p => Math.Abs(p.Value - line.Target) < 1e-9);
                if (right != null) { right.E.Fill = B(Ui.Yellow); right.E.StrokeThickness = 4; }
                Toast(line.Label + " is here", Ui.Yellow, 1.6);
                Later(1700, NextFrog);
            }
            else { ShowHint(); Toast("Splash! Try again.", Ui.Yellow); G.Busy = false; }
        }
    }

    // ---------------------------------------------------------------- Pizza Shop
    sealed class Slice { public Path E; public bool On; public List<Ellipse> Dots = new(); }
    readonly List<Slice> slices = new();
    readonly List<Ellipse> coins = new();
    string pizzaMode = "";
    int target, paid;
    TextBlock paidLabel;

    void InitPizza() { G.Rounds = 10; G.Lives = 0; NextPizza(); }

    void NextPizza()
    {
        if (G.Round >= G.Rounds) { Finish("The shop is closed. Great work!", "Pizza Shop"); return; }
        playField.Children.Clear(); slices.Clear(); coins.Clear(); fx.Clear(); visual = null;
        G.Round++; G.WrongThis = 0; G.HintIdx = 0; hintCard.IsVisible = false; lblRound.Text = G.Round + " / " + G.Rounds;
        string mode = G.Grade <= 2 ? (G.Round % 3 == 0 ? "money" : "slices") : (G.Round % 3 == 0 ? "ratio" : "percent");
        pizzaMode = mode;
        var p = new Problem();
        var me = Lg.Me;
        switch (mode)
        {
            case "slices":
            {
                int lvl = me.Skill("fractions2").Level;
                int d = new[] { 2, 3, 4 }[Lg.Rand.Next(0, Math.Min(3, 1 + lvl))];   // 2.G.3: halves, thirds and fourths only
                int n = lvl <= 1 ? 1 : Lg.Rand.Next(1, d);
                p.Skill = "fractions2"; p.Text = "Put pepperoni on " + n + "/" + d + " of the pizza"; p.Answer = n.ToString();
                p.Hints = new[] { "The pizza has " + d + " equal slices. Each slice is 1/" + d + ".", n + "/" + d + " means " + n + " of the " + d + " slices." };
                DrawPizza(d); target = n;
                break;
            }
            case "percent":
            {
                int d = new[] { 4, 5, 10, 8 }[Lg.Rand.Next(0, 4)], n = Lg.Rand.Next(1, d);
                double pct = Math.Round(100.0 * n / d, 1);
                p.Skill = "percent"; p.Text = "Put pepperoni on " + Answers.Num(pct) + "% of the pizza"; p.Answer = n.ToString();
                p.Hints = new[] { "The pizza has " + d + " slices. 100% is all " + d + ".", "Each slice is 100 ÷ " + d + " = " + Answers.Num(100.0 / d) + "% of the pizza.", Answers.Num(pct) + " ÷ " + Answers.Num(100.0 / d) + " = ? slices" };
                DrawPizza(d); target = n;
                break;
            }
            case "money":
            {
                int lvl = me.Skill("money").Level;
                int price = 5 * Lg.Rand.Next(2, 6 + 3 * lvl) + (lvl >= 3 ? Lg.Rand.Next(0, 5) : 0);
                p.Skill = "money"; p.Text = "A slice costs " + Answers.Money(price) + ". Pay exactly that."; p.Answer = price.ToString(); p.Kind = "money";
                p.Hints = new[] { "Use the biggest coins first: quarters (25¢), then dimes (10¢), nickels (5¢), pennies (1¢).", "Count up as you add each coin." };
                target = price; paid = 0;
                DrawPizza(8);
                break;
            }
            default:
            {
                var st = me.Skill("ratio");
                p = Problems.Make(Lg.Rand.Next(2) == 0 ? "ratio" : "rate", st.Level);
                DrawPizza(8);
                break;
            }
        }
        G.Problem = p;
        lblQuestion.Text = p.Text;
        choices.Children.Clear(); typed.IsVisible = false; choices.IsVisible = true;
        if (mode == "ratio") ShowAnswers(p, true);
        else if (mode == "money")
        {
            foreach (int c in new[] { 25, 10, 5, 1 }) { var b = ChoiceButton(c + "¢"); int cc = c; b.Click += (s, e) => AddCoin(cc); choices.Children.Add(b); }
            var clr = ChoiceButton("Start over"); clr.FontSize = 16;
            clr.Click += (s, e) => { paid = 0; foreach (var c in coins) playField.Children.Remove(c); coins.Clear(); UpdatePaid(); };
            choices.Children.Add(clr);
            var pay = ChoiceButton("Pay"); pay.Background = B(Ui.Accent);
            pay.Click += (s, e) => Submit(paid.ToString());
            choices.Children.Add(pay);
            paidLabel = T("Paid: 0¢", 26, Ui.Yellow); paidLabel.FontWeight = FontWeight.SemiBold;
            Put(paidLabel, CoinX(), 60); playField.Children.Add(paidLabel);
        }
        else
        {
            var serve = ChoiceButton("Serve it!");
            serve.Click += (s, e) => Submit(slices.Count(x => x.On).ToString());
            choices.Children.Add(serve);
            var tip = Ui.Text(Services.Platform.Touch ? "Tap slices to add or remove pepperoni" : "Click slices to add or remove pepperoni", 15, Ui.Muted); tip.Margin = new Thickness(12, 20, 0, 0);
            choices.Children.Add(tip);
        }
        G.Busy = false;
    }

    double CoinX() => narrow ? 12 : FieldW / 2 + 160;

    void DrawPizza(int count)
    {
        double w = FieldW, h = FieldH;
        double r = Math.Min(180, Math.Min(h * 0.4, w * 0.4));
        double cx = w / 2 - (pizzaMode == "money" && !narrow ? 120 : 0), cy = h / 2 + 10 + (pizzaMode == "money" && narrow ? 40 : 0);
        var crust = new Ellipse { Width = 2 * r + 24, Height = 2 * r + 24, Fill = B("#D97706"), IsHitTestVisible = false };
        Put(crust, cx - r - 12, cy - r - 12); playField.Children.Add(crust);
        for (int i = 0; i < count; i++)
        {
            double a0 = 2 * Math.PI * i / count - Math.PI / 2, a1 = 2 * Math.PI * (i + 1) / count - Math.PI / 2;
            var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            fig.Segments.Add(new LineSegment { Point = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)) });
            fig.Segments.Add(new ArcSegment { Point = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            var sl = new Path { Data = geo, Fill = B("#FDE68A"), Stroke = B("#B45309"), StrokeThickness = 3, Cursor = new Cursor(StandardCursorType.Hand) };
            int idx = i;
            sl.PointerPressed += (s, e) => { e.Handled = true; ToggleSlice(idx); };
            playField.Children.Add(sl);
            double am = (a0 + a1) / 2;
            var slice = new Slice { E = sl };
            foreach (double f in new[] { 0.38, 0.68 })
                foreach (double da in new[] { -0.25, 0.25 })
                {
                    double aa = am + da * (2 * Math.PI / count) * (f < 0.5 ? 0.5 : 1);
                    double ds = Math.Max(10, r * 0.13);
                    var d = new Ellipse { Width = ds, Height = ds, Fill = B("#DC2626"), IsHitTestVisible = false, IsVisible = false };
                    Put(d, cx + r * f * Math.Cos(aa) - ds / 2, cy + r * f * Math.Sin(aa) - ds / 2);
                    playField.Children.Add(d);
                    slice.Dots.Add(d);
                }
            slices.Add(slice);
        }
    }

    public void ToggleSlice(int i)
    {
        if (G.Busy || pizzaMode == "money" || pizzaMode == "ratio") return;
        var s = slices[i]; s.On = !s.On;
        foreach (var d in s.Dots) d.IsVisible = s.On;
        Snd.Pop();
    }

    public void AddCoin(int v)
    {
        if (G.Busy) return;
        paid += v;
        double sz = v switch { 1 => 34, 5 => 40, 10 => 30, _ => 48 };
        var e = new Ellipse { Width = sz, Height = sz, Fill = B(v == 1 ? "#D97706" : "#CBD5E1"), Stroke = B(Ui.Bg), StrokeThickness = 2, IsHitTestVisible = false };
        int n = coins.Count, per = narrow ? 6 : 8;
        Put(e, CoinX() + (n % per) * 44, 120 + Math.Floor(n / (double)per) * 52);
        playField.Children.Add(e); coins.Add(e);
        Snd.Tick(); UpdatePaid();
    }

    void UpdatePaid() { if (paidLabel != null) paidLabel.Text = "Paid: " + Answers.Money(paid); }

    void AnswerPizza(bool ok)
    {
        if (ok) { Toast(Lg.Praise(G.Streak) + "  The customer is happy!", Ui.Green, 1.1); Later(1100, NextPizza); }
        else if (G.WrongThis >= 3)
        {
            Toast(pizzaMode == "money" ? "It costs " + Answers.Money(target) + "." : pizzaMode == "ratio" ? "The answer is " + G.Problem.Answer + "." : "That needs " + target + " slices.", Ui.Yellow, 2);
            Later(2000, NextPizza);
        }
        else Toast(pizzaMode == "money" ? (paid > target ? "That is too much. Start over." : "Not enough yet!") : "Not quite. Check the hint!", Ui.Yellow, 1.4);
    }

    // ---------------------------------------------------------------- Math Race (alone against two robots, or on the network)
    public const int RaceLen = 10;
    const double RaceStepGap = 0.8;
    sealed class Racer { public string Id = "", Name = "", Color = ""; public double Pos, Shown, Y, Every; public int Credit; public DateTime LastStep = DateTime.MinValue, Next; public bool Bot, Me; public Canvas Car; }
    readonly List<Racer> racers = new();
    int countdown;
    DateTime countAt;
    double trackX0, trackX1;
    bool raceDone;
    Racer winner;
    readonly Dictionary<string, int> hits = new();
    readonly Dictionary<int, DateTime> lastHit = new();

    void InitRace()
    {
        G.Rounds = 0; G.Lives = 0;
        if (G.Net)
            foreach (var np in G.NetPlayers) racers.Add(new Racer { Id = np.Id.ToString(), Name = np.Name, Color = np.Color, Me = np.Id == Family.SelfId });
        else
        {
            var me = Lg.Me;
            racers.Add(new Racer { Id = "me", Name = me.Name, Color = me.Color, Me = true });
            double avg = Problems.SkillsFor(me.MathGrade).Select(s => (double)me.Skill(s).Level).DefaultIfEmpty(1).Average();
            foreach (var (n, c, s) in new[] { ("Robo", "#94A3B8", 7.5 - avg * 0.6), ("Zippy", "#A855F7", 9.0 - avg * 0.6) })
                racers.Add(new Racer { Id = n, Name = n, Color = c, Bot = true, Every = Math.Max(3.0, s), Next = DateTime.Now.AddSeconds(4 + s) });
        }
        DrawTrack();
        countdown = 3; countAt = DateTime.Now;
        lblBig.Text = "3"; lblBig.IsVisible = true; Snd.Tick();
        G.Busy = true;
        lblQuestion.Text = "Get ready...";
    }

    void DrawTrack()
    {
        playField.Children.Clear();
        double w = FieldW, h = FieldH; int n = Math.Max(1, racers.Count);
        double lane = Math.Min(110, (h - 40) / n), top = (h - lane * n) / 2;
        trackX0 = narrow ? 60 : 90; trackX1 = w - (narrow ? 80 : 110);
        for (int i = 0; i < n; i++)
        {
            var r = racers[i]; double y = top + i * lane;
            var bg = new Rectangle { Width = w - 40, Height = lane - 10, RadiusX = 14, RadiusY = 14, Fill = B(r.Me ? "#1B2D50" : Ui.Surface) }; Put(bg, 20, y); playField.Children.Add(bg);
            playField.Children.Add(new Line { StartPoint = new Point(trackX0, y + (lane - 10) / 2), EndPoint = new Point(trackX1 + 60, y + (lane - 10) / 2), Stroke = B(Ui.BorderStrong), StrokeThickness = 3, StrokeDashArray = new AvaloniaList<double> { 4, 4 } });
            var nm = T(r.Name, 14, Ui.TextC); nm.FontWeight = FontWeight.SemiBold; Put(nm, 30, y + 6); playField.Children.Add(nm);
            var car = NewCar(r.Color); Put(car, trackX0, y + (lane - 10) / 2 - 22); playField.Children.Add(car);
            r.Car = car; r.Y = y + (lane - 10) / 2 - 22;
        }
        // checkered finish line
        for (int k = 0; k < Math.Ceiling(lane * n / 14); k++)
            for (int j = 0; j < 2; j++) { var sq = new Rectangle { Width = 14, Height = 14, Fill = B((k + j) % 2 == 1 ? "#FFFFFF" : Ui.Bg) }; Put(sq, trackX1 + 62 + j * 14, top + k * 14); playField.Children.Add(sq); }
    }

    static Canvas NewCar(string color)
    {
        var c = new Canvas { Width = 70, Height = 44 };
        var body = new Rectangle { Width = 66, Height = 22, RadiusX = 9, RadiusY = 9, Fill = B(color) }; Put(body, 2, 12); c.Children.Add(body);
        var cab = new Rectangle { Width = 30, Height = 16, RadiusX = 6, RadiusY = 6, Fill = B("#BAE6FD") }; Put(cab, 20, 2); c.Children.Add(cab);
        foreach (double wx in new[] { 10.0, 46.0 }) { var wh = new Ellipse { Width = 16, Height = 16, Fill = B(Ui.Bg), Stroke = B("#94A3B8"), StrokeThickness = 2 }; Put(wh, wx, 28); c.Children.Add(wh); }
        return c;
    }

    void RaceProblem() { if (G.Spectator) return; var p = NextProblem(true); ShowAnswers(p, true); G.Busy = false; }

    void AnswerRace(bool ok)
    {
        var mine = racers.FirstOrDefault(r => r.Me);
        if (ok)
        {
            if (mine != null)
            {
                mine.Pos = Math.Min(RaceLen, mine.Pos + 1);
                if (G.Net) Family.Send(new System.Text.Json.Nodes.JsonObject { ["t"] = "pos", ["pos"] = mine.Pos });
            }
            Toast(Lg.Praise(G.Streak), Ui.Green, 0.6);
            if (!G.Net && mine != null && mine.Pos >= RaceLen) { RaceWon(mine); return; }
            RaceProblem();
        }
        else if (G.WrongThis >= 3) { Toast("It was " + G.Problem.Answer, Ui.Yellow, 1.6); Later(1500, RaceProblem); }
        else { Toast("Try again!", Ui.Yellow, 0.8); answer.SelectAll(); if (!Services.Platform.Touch) answer.Focus(); }
    }

    void RaceWon(Racer r)
    {
        if (raceDone) return;   // one winner only
        raceDone = true; G.Busy = true;
        lblBig.IsVisible = false; lblQuestion.Text = r.Name + " wins!";
        winner = r;
        DeclareRound(true);
        Later(1600, () => Finish(r.Me ? "You won the race!" : r.Name + " won the race", r.Me ? "First across the line!" : "So close! Race again?"));
        G.Busy = true;
    }

    void ApplyRaceCredit()
    {
        bool changed = false; Racer first = null;
        foreach (var r in racers)
            if (r.Credit > 0 && r.Pos < RaceLen && (DateTime.Now - r.LastStep).TotalSeconds >= RaceStepGap)
            {
                r.Credit--; r.Pos = Math.Min(RaceLen, r.Pos + 1); r.LastStep = DateTime.Now; changed = true;
                if (r.Pos >= RaceLen && first == null) first = r;
            }
        if (!changed) return;
        BroadcastRace();
        if (first != null) { Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "won", ["id"] = first.Id, ["name"] = first.Name }); RaceWon(first); }
    }

    void BroadcastRace()
    {
        var pos = new System.Text.Json.Nodes.JsonArray();
        foreach (var r in racers) pos.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = int.TryParse(r.Id, out int id) ? id : 0, ["pos"] = r.Pos });
        Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "race", ["pos"] = pos });
    }

    void UpdateRace()
    {
        if (countdown > 0)
        {
            if ((DateTime.Now - countAt).TotalSeconds >= 1)
            {
                countdown--; countAt = DateTime.Now;
                if (countdown > 0) { lblBig.Text = countdown.ToString(); Snd.Tick(); }
                else
                {
                    lblBig.Text = "GO!"; Snd.Win(); bigUntil = DateTime.Now.AddMilliseconds(700);
                    RaceProblem();
                    if (G.Spectator) lblQuestion.Text = "Watching the race";
                }
            }
            return;
        }
        if (Family.Mode == NetMode.Host && G.Net && !raceDone) ApplyRaceCredit();
        foreach (var r in racers)
        {
            if (r.Bot && !G.Over && DateTime.Now >= r.Next)
            {
                r.Pos = Math.Min(RaceLen, r.Pos + 1);
                r.Next = DateTime.Now.AddSeconds(r.Every * (0.7 + Lg.Rand.NextDouble() * 0.6));
                if (r.Pos >= RaceLen) RaceWon(r);
            }
            r.Shown += (r.Pos - r.Shown) * 0.15;
            Put(r.Car, trackX0 + (trackX1 - trackX0) * r.Shown / RaceLen, r.Y);
        }
    }

    /// <summary>For the checks: the countdown is over now.</summary>
    public void SkipCountdownForTest() { countdown = 1; countAt = DateTime.Now.AddSeconds(-2); Tick(); }

    // ---------------------------------------------------------------- Boss Battle (alone or as a team on the network)
    int bossHp, bossMax, shake;
    double bossX, bossY;
    DateTime nextAttack;
    int attackEvery;
    Canvas boss;
    Border hpFill;
    TextBlock hpText;
    bool bossDone;
    double hpWidth = 320;

    void InitBoss()
    {
        G.Rounds = 0; G.Lives = 5; SetLives();
        bossMax = G.Net ? 8 * Math.Max(1, G.NetPlayers.Count) : 12;
        bossHp = bossMax;
        attackEvery = G.Net ? 14 : 16; nextAttack = DateTime.Now.AddSeconds(attackEvery + 3);
        double w = FieldW;
        boss = new Canvas { Width = 240, Height = 220 };
        boss.Children.Add(new Path { Data = Geometry.Parse("M 20,200 C 0,120 30,40 120,40 C 210,40 240,120 220,200 Q 200,180 180,200 Q 160,180 140,200 Q 120,180 100,200 Q 80,180 60,200 Q 40,180 20,200 Z"), Fill = B("#7C3AED"), Stroke = B("#C4B5FD"), StrokeThickness = 4 });
        foreach (int hx in new[] { 55, 165 }) boss.Children.Add(new Path { Data = Geometry.Parse("M " + hx + ",58 L " + (hx + 12) + ",8 L " + (hx + 26) + ",52 Z"), Fill = B("#FDE68A") });
        foreach (int ex in new[] { 75, 135 })
        {
            var e = new Ellipse { Width = 34, Height = 34, Fill = Brushes.White }; Put(e, ex, 90); boss.Children.Add(e);
            var p = new Ellipse { Width = 14, Height = 14, Fill = B(Ui.Bg) }; Put(p, ex + 12, 102); boss.Children.Add(p);
        }
        boss.Children.Add(new Path { Data = Geometry.Parse("M 85,150 Q 125,180 165,150"), Stroke = B(Ui.Bg), StrokeThickness = 6, StrokeLineCap = PenLineCap.Round });
        double scale = Math.Min(1, (FieldH - 80) / 260);
        if (scale < 1) { boss.RenderTransform = new ScaleTransform(scale, scale); boss.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative); }
        bossX = w / 2 - 120; bossY = 30;
        Put(boss, bossX, bossY); playField.Children.Add(boss);
        hpWidth = Math.Min(320, w - 120);
        var bar = new Grid { Width = hpWidth, Height = 18 };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = B(Ui.BorderStrong) });
        hpFill = new Border { CornerRadius = new CornerRadius(9), Background = B("#EC4899"), HorizontalAlignment = HorizontalAlignment.Left, Width = hpWidth };
        bar.Children.Add(hpFill);
        double barY = bossY + 236 * scale;
        Put(bar, w / 2 - hpWidth / 2, barY); playField.Children.Add(bar);
        hpText = T(bossHp + " / " + bossMax, 14, Ui.TextC); hpText.FontWeight = FontWeight.SemiBold; Put(hpText, w / 2 + hpWidth / 2 + 10, barY - 2); playField.Children.Add(hpText);
        if (!G.Spectator) { var p = NextProblem(true); ShowAnswers(p, true); } else lblQuestion.Text = "Watching the battle";
        G.Busy = false;
    }

    void UpdateBossBar() { hpFill.Width = hpWidth * Math.Max(0, bossHp) / (double)bossMax; hpText.Text = Math.Max(0, bossHp) + " / " + bossMax; SetLives(); }

    void AnswerBoss(bool ok)
    {
        if (ok)
        {
            Fireball();
            if (G.Net) Family.Send(new System.Text.Json.Nodes.JsonObject { ["t"] = "hit" });
            else { bossHp--; UpdateBossBar(); if (bossHp <= 0) { BossOver(true); return; } }
            var p = NextProblem(true); ShowAnswers(p, true);
        }
        else if (G.WrongThis >= 3) { Toast("It was " + G.Problem.Answer, Ui.Yellow, 1.6); Later(1500, () => { var p = NextProblem(true); ShowAnswers(p, true); G.Busy = false; }); }
        else { Toast("Try again!", Ui.Yellow, 0.8); answer.SelectAll(); if (!Services.Platform.Touch) answer.Focus(); }
    }

    void Fireball()
    {
        double w = FieldW, h = FieldH;
        var e = new Ellipse { Width = 30, Height = 30, Fill = B(Ui.Yellow), Stroke = B("#F97316"), StrokeThickness = 4, IsHitTestVisible = false };
        Put(e, w / 2 - 15, h - 40); playField.Children.Add(e);
        fx.Add(new Fx { E = e, Life = 18, Kind = "shot", X = w / 2 - 15, Y = h - 40, TX = bossX + 105, TY = bossY + 100 });
        Snd.Pop();
    }

    /// <summary>The monster hits the team: one heart less (or, on a player's device, the hearts the host says are left).</summary>
    void BossAttack(int livesLeft = -1)
    {
        if (livesLeft >= 0) G.Lives = livesLeft; else G.Lives--;
        UpdateBossBar(); Snd.Boom();
        shake = 12; Toast("The monster attacks! Answer faster!", Ui.Red, 1.2);
        if (G.Lives <= 0) BossOver(false);
    }

    void BossOver(bool won)
    {
        if (bossDone) return;
        bossDone = true;
        DeclareRound(won);
        if (won) { boss.Opacity = 0.3; Snd.Win(); Later(1300, () => Finish("You beat the monster!", "Teamwork and math!")); }
        else Later(1300, () => Finish("The monster got away!", "Try again, you can do it!"));
    }

    void UpdateBoss()
    {
        if (!G.Net && !G.Over && !G.Busy && DateTime.Now >= nextAttack) { nextAttack = DateTime.Now.AddSeconds(attackEvery); BossAttack(); }
        if (Family.Mode == NetMode.Host && G.Net && !G.Over && !bossDone && DateTime.Now >= nextAttack)
        {
            // the host keeps the team's hearts: one heart less per attack, then everyone is told how many are left
            nextAttack = DateTime.Now.AddSeconds(attackEvery);
            if (!G.Spectator) BossAttack(); else { G.Lives--; UpdateBossBar(); Snd.Boom(); shake = 12; if (G.Lives <= 0) BossOver(false); }
            Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "boss", ["hp"] = bossHp, ["max"] = bossMax, ["lives"] = Math.Max(0, G.Lives), ["attack"] = true });
        }
        double bob = Math.Sin(G.Frame / 8.0) * 6;
        double sx = shake > 0 ? Lg.Rand.Next(-8, 9) : 0;
        if (shake > 0) shake--;
        Put(boss, bossX + sx, bossY + bob);
    }

    /// <summary>For the checks: the monster attacks now.</summary>
    public void AttackNowForTest() { nextAttack = DateTime.Now.AddSeconds(-1); G.Busy = false; Tick(); }
    public int BossHp => bossHp;
    public double RacePos(string id) => racers.FirstOrDefault(r => r.Id == id)?.Pos ?? -1;

    // ---------------------------------------------------------------- the network
    void DeclareRound(bool won)
    {
        if (Family.Mode != NetMode.Host || !G.Net || Family.Round != null) return;
        int n = Family.NextRoundNumber();
        NetRound r;
        if (G.Kind == "race")
            r = NetRound.Race(n, racers.Select(x => x.Id).ToList(), racers.Select(x => x.Name).ToList(), racers.Select(x => x.Color).ToList(), racers.Select(x => x.Pos).ToList(), RaceLen, winner?.Id ?? "");
        else
        {
            var ps = G.NetPlayers;
            r = NetRound.Boss(n, won, ps.Select(p => p.Id.ToString()).ToList(), ps.Select(p => p.Name).ToList(), ps.Select(p => p.Color).ToList(), ps.Select(p => hits.TryGetValue(p.Id.ToString(), out int h) ? h : 0).ToList());
        }
        Family.Declare(r);
    }

    public void RacePositions(Dictionary<string, double> pos)
    {
        if (G.Kind != "race") return;
        foreach (var r in racers) if (!r.Me && pos.TryGetValue(r.Id, out double v)) r.Pos = v;
    }

    public void RaceWon(string id)
    {
        if (G.Kind != "race" || !G.Net || G.Over) return;
        var r = racers.FirstOrDefault(x => x.Id == id);
        if (r != null) { r.Pos = RaceLen; RaceWon(r); }
    }

    public void BossState(int hp, int max, int lives, bool attack, int by)
    {
        if (G.Kind != "boss" || !G.Net || G.Over || bossDone) return;
        if (by >= 0 && by != Family.SelfId) Fireball();
        bossHp = hp; bossMax = Math.Max(1, max);
        int left = lives >= 0 ? lives : G.Lives;
        if (attack) BossAttack(left); else { G.Lives = left; UpdateBossBar(); }
        if (bossHp <= 0) BossOver(true); else if (G.Lives <= 0) BossOver(false);
    }

    public void HostPos(int from, double claim)
    {
        if (G.Kind != "race" || !G.Net || G.Over || raceDone) return;
        var r = racers.FirstOrDefault(x => x.Id == from.ToString());
        if (r == null) return;
        claim = Math.Min(RaceLen, claim);
        if (from == 0)
        {
            // the host's own car: its answers are checked right here
            if (claim > r.Pos) r.Pos = Math.Min(claim, r.Pos + 1);
            BroadcastRace();
            if (r.Pos >= RaceLen) { Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "won", ["id"] = r.Id, ["name"] = r.Name }); RaceWon(r); }
        }
        else
        {
            if (claim - r.Pos - r.Credit >= 1) r.Credit++;
            ApplyRaceCredit();
        }
    }

    public void HostHit(int from)
    {
        if (G.Kind != "boss" || !G.Net || G.Over || bossDone) return;
        if (from != 0)
        {
            // at most one hit every 0.6 s from each player
            if (lastHit.TryGetValue(from, out var last) && (DateTime.Now - last).TotalSeconds < 0.6) return;
            if (!Family.Players.ContainsKey(from)) return;
            lastHit[from] = DateTime.Now;
        }
        bossHp--;
        string key = from.ToString();
        hits[key] = (hits.TryGetValue(key, out int h) ? h : 0) + 1;
        if (from != 0) Fireball();
        UpdateBossBar();
        Family.HostBroadcast(new System.Text.Json.Nodes.JsonObject { ["t"] = "boss", ["hp"] = bossHp, ["max"] = bossMax, ["lives"] = G.Lives, ["by"] = from });
        if (bossHp <= 0) BossOver(true);
    }

    public void Ended(string why)
    {
        if (!G.Net || G.Over) return;
        G.After = null; G.NextAt = DateTime.MaxValue;
        lblBig.IsVisible = false;
        Finish("Game over", why);
    }

    public void Hint(string from, string text)
    {
        Toast(from + ": " + text, Ui.AccentText, 6);
        if (text.Length > 0) { lblHint.Text = from + " says: " + text; hintCard.IsVisible = true; }
    }
}
