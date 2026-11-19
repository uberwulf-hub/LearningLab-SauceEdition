// Learning Games - the math skill map and mastery practice (2026-11-11, docs\MATH.md steps 1-2). The skill map shows a
// grade's skills by area with the standard, the player's mastery score ring and its zone, and Up next. Practice asks
// one skill's questions at the difficulty the score calls for (LearningGames.Mastery.LevelFor); a right answer moves
// on, a wrong one shows "Not quite", the answer and a worked explanation of exactly that question (LearningGames.Worked),
// and "Got it" carries on. Reaching 100 celebrates (sound, reward points). Our own look: the dark theme, Math blue.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LearningLab.Views;
using Problem = LearningGames.Problem;
using Problems = LearningGames.Problems;
using M = LearningGames.Mastery;
using Snd = LearningGames.Sound;

namespace LearningLab.LearningGames;

/// <summary>Pieces the mastery pages share: the score ring, its colours, a skill tile, Up next.</summary>
public static class MasteryUi
{
    public const string MathBlue = "#3B82F6";
    /// <summary>The grades on the map's tabs (the ones with skills come first in use; the rest say "coming").</summary>
    public static readonly int[] Grades = { 1, 2, 3, 4, 5, 6, 7 };

    public static string ZoneColor(int score) => M.ZoneOf(score) switch
    {
        M.Zone.Mastered => Ui.Green,
        M.Zone.Challenge => Ui.Yellow,
        M.Zone.Strong => "#2DD4BF",
        M.Zone.Building => MathBlue,
        _ => "#93C5FD",
    };

    public static bool HasSkills(int grade) => Problems.SkillsFor(grade).Length > 0;

    /// <summary>The grade the map opens on: the player's, or the nearest grade that has skills.</summary>
    public static int GradeFor(Player p)
    {
        int g = p?.MathGrade ?? 6;
        if (HasSkills(g)) return g;
        return Grades.Where(HasSkills).OrderBy(x => Math.Abs(x - g)).ThenBy(x => x).FirstOrDefault();
    }

    /// <summary>A ring 0-100 with the score in the middle (a star when Mastered).</summary>
    public static Control Ring(int score, double size, bool started = true)
    {
        double stroke = Math.Max(4, size / 9), r = (size - stroke) / 2, c = size / 2;
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Stroke = Ui.Brush(Ui.Surface3), StrokeThickness = stroke, Margin = new Thickness(stroke / 2) });
        string col = ZoneColor(score);
        if (score > 0) g.Children.Add(new Path { Data = MathHomePage.Arc(score / 100.0, c, r), Stroke = Ui.Brush(col), StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round });
        string mid = score >= M.Max ? "★" : started ? score.ToString() : "–";
        g.Children.Add(new TextBlock
        {
            Text = mid, FontSize = score >= M.Max ? size * 0.42 : size * 0.3, FontWeight = FontWeight.Bold,
            Foreground = Ui.Brush(score >= M.Max ? Ui.Green : started ? Ui.TextC : Ui.Dim),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    /// <summary>A skill as a tile: ring, name, standard, zone and answers.</summary>
    public static Button SkillTile(Player p, string id, Action open)
    {
        var info = Problems.Info(id);
        var m = p.MasteryIfAny(id);
        int score = m?.Score ?? 0, answered = m?.Answered ?? 0;
        var row = new DockPanel();
        var ring = Ring(score, 50, answered > 0); ring.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(ring, Dock.Left); row.Children.Add(ring);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Ui.Text(info?.Name ?? id, 15, Ui.TextC, true));
        if (!string.IsNullOrEmpty(info?.Std)) text.Children.Add(Ui.Text(info.Std, 12, Ui.Dim));
        text.Children.Add(Ui.Text(answered == 0 ? "Not started" : M.ZoneName(score) + "  ·  " + answered + " answered", 12.5, answered == 0 ? Ui.Muted : ZoneColor(score)));
        row.Children.Add(text);
        var b = Ui.Tile(row, score >= M.Max ? Ui.Green : Ui.BorderStrong, open);
        b.Padding = new Thickness(12, 10);
        b.MinHeight = 76;
        b.Tag = id;
        return b;
    }

    /// <summary>The player's Up next skills for a grade (MasteryUi.GradeFor).</summary>
    public static List<string> UpNext(Player p, int grade, int max)
        => M.UpNext(Problems.SkillsFor(grade), p.MasteryScore, p.MasteryAnswered, max);

    /// <summary>"Up next" with up to three tiles, or null when everything is mastered.</summary>
    public static Control UpNextPanel(Player p, int grade, int max = 3)
    {
        var ids = UpNext(p, grade, max);
        if (ids.Count == 0) return null;
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(Ui.Text("Up next for " + p.Name, 18, Ui.TextC, true));
        sp.Children.Add(Ui.Text(ids.Any(id => p.MasteryAnswered(id) > 0) ? "Finish what you started, then try something new." : "Start here: these come first for " + Profiles.GradeName(grade) + ".", 13, Ui.Muted));
        var tiles = ids.Select(id => (Control)SkillTile(p, id, () => MasteryPracticePage.Open(id))).ToList();
        var grid = Ui.Tiles(tiles, 250, 10); grid.Margin = new Thickness(-5, 6, -5, 0);
        sp.Children.Add(grid);
        return sp;
    }
}

// ================================================================ the skill map
public class SkillMapPage : IPage
{
    public string Title => "Skill map";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel body = new() { Spacing = 10 };
    readonly WrapPanel tabs = new();
    readonly StackPanel gradeBox = new() { Spacing = 6 };
    readonly ContentControl upNext = new();
    public int Grade { get; private set; }

    public SkillMapPage(int grade = 0)
    {
        var p = Lg.Me;
        Grade = grade > 0 ? grade : MasteryUi.GradeFor(p);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(HomePage.Badge("◎", MasteryUi.MathBlue, 52));
        var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ht.Children.Add(Ui.Text("Math skill map", 26, Ui.TextC, true));
        head.Children.Add(ht);
        body.Children.Add(head);
        body.Children.Add(Ui.Text("Pick a skill to practice. Your score goes up as you answer: Getting started, Building, Strong at 70, the Challenge zone at 90, and Mastered at 100. Miss one and you'll see how to solve it, step by step.", 14, Ui.Muted));
        upNext.Margin = new Thickness(0, 6, 0, 4);
        body.Children.Add(upNext);
        var th = Ui.Text("Grades", 13, Ui.Muted); th.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(th);
        body.Children.Add(tabs);
        body.Children.Add(gradeBox);
        View = Ui.Page(body);
        Fill();
        View.AttachedToVisualTree += (s, e) => Fill();   // back from practice: new scores
    }

    void Fill()
    {
        var p = Lg.Me; if (p == null) return;
        upNext.Content = MasteryUi.UpNextPanel(p, MasteryUi.HasSkills(Grade) ? Grade : MasteryUi.GradeFor(p));
        tabs.Children.Clear();
        foreach (int g in MasteryUi.Grades)
        {
            bool has = MasteryUi.HasSkills(g), on = g == Grade;
            var content = new StackPanel { Spacing = 0 };
            content.Children.Add(new TextBlock { Text = "Grade " + g, FontSize = 15, FontWeight = on ? FontWeight.Bold : FontWeight.Normal, Foreground = Ui.Brush(on ? "#FFFFFF" : has ? Ui.TextC : Ui.Dim), HorizontalAlignment = HorizontalAlignment.Center });
            if (!has) content.Children.Add(new TextBlock { Text = "coming", FontSize = 11, Foreground = Ui.Brush(Ui.Dim), HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button
            {
                Content = content, MinHeight = 44, MinWidth = 84, Padding = new Thickness(12, 4), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10),
                Background = Ui.Brush(on ? MasteryUi.MathBlue : Ui.Surface2), BorderBrush = Ui.Brush(on ? MasteryUi.MathBlue : Ui.BorderC), BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = g,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            int gg = g;
            b.Click += (s, e) => ShowGrade(gg);
            tabs.Children.Add(b);
        }
        FillGrade();
    }

    public void ShowGrade(int grade) { Grade = grade; Fill(); }

    void FillGrade()
    {
        var p = Lg.Me;
        gradeBox.Children.Clear();
        var ids = Problems.SkillsFor(Grade);
        if (ids.Length == 0)
        {
            var c = Ui.Card(new StackPanel { Spacing = 6, Children = { Ui.Text(Profiles.GradeName(Grade) + " skills are coming", 18, Ui.TextC, true), Ui.Text("For now, 2nd and 6th grade have skills to practice. More grades are on the way.", 14, Ui.Muted) } }, Ui.Surface, 18);
            gradeBox.Children.Add(c);
            return;
        }
        int mastered = ids.Count(id => p.MasteryScore(id) >= M.Max), started = ids.Count(id => p.MasteryAnswered(id) > 0);
        var sum = new StackPanel { Spacing = 6 };
        sum.Children.Add(Ui.Text(Profiles.GradeName(Grade) + ": " + mastered + " of " + ids.Length + " skills mastered" + (started > mastered ? "  ·  " + (started - mastered) + " started" : ""), 16, Ui.TextC, true));
        sum.Children.Add(Lg.Bar(ids.Length == 0 ? 0 : mastered / (double)ids.Length, Ui.Green, 7));
        var sc = Ui.Card(sum, Ui.Surface, 14); sc.Margin = new Thickness(0, 4, 0, 4);
        gradeBox.Children.Add(sc);
        foreach (var area in Problems.AreasFor(Grade))
        {
            var inArea = Problems.SkillsIn(Grade, area);
            int am = inArea.Count(id => p.MasteryScore(id) >= M.Max);
            var ah = new WrapPanel { Margin = new Thickness(2, 14, 0, 0) };
            var an = Ui.Text(area, 17, Ui.AccentText, true); an.Margin = new Thickness(0, 0, 10, 0); ah.Children.Add(an);
            var ac = Ui.Text(am + " of " + inArea.Length + " mastered", 13, am == inArea.Length ? Ui.Green : Ui.Muted); ac.VerticalAlignment = VerticalAlignment.Center; ah.Children.Add(ac);
            gradeBox.Children.Add(ah);
            var tiles = inArea.Select(id => (Control)MasteryUi.SkillTile(p, id, () => MasteryPracticePage.Open(id))).ToList();
            var grid = Ui.Tiles(tiles, 260, 10); grid.Margin = new Thickness(-5, 0, -5, 0);
            gradeBox.Children.Add(grid);
        }
    }

    /// <summary>For the checks: the skill tiles now shown (Up next first).</summary>
    public List<Button> SkillTiles => body.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string).ToList();
}

// ================================================================ practising one skill
public class MasteryPracticePage : IPage, IKeyPage
{
    public string Skill { get; }
    public string Title => "Math practice";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();

    readonly Player me;
    readonly ContentControl ringHost = new();
    readonly TextBlock lblZone = Ui.Text("", 13, Ui.Muted, true), lblStats = Ui.Text("", 13, Ui.Muted), lblQuestion = Ui.Text("", 24, Ui.TextC, true), lblUnit = Ui.Text("", 15, Ui.Muted), lblFeedback = Ui.Text("", 16, Ui.Green, true);
    readonly ContentControl picture = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly StackPanel answerArea = new() { Spacing = 10 };
    readonly WrapPanel choices = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly StackPanel typed = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBox answer = new() { Width = 200, FontSize = 24, FontWeight = FontWeight.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 6) };
    readonly UniformGrid pad = new() { Columns = 8, Width = 460 };
    readonly Border learn, mastered, questionCard;
    readonly StackPanel learnBody = new() { Spacing = 8 };
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DateTime opened = DateTime.Now;
    DateTime shownAt = DateTime.Now;
    int answered, right;

    /// <summary>Scratch paper under the answer (open unless hidden; remembered while the app runs).</summary>
    public ScratchPad Scratch { get; } = new();
    static bool scratchHidden;
    readonly Button scratchToggle = Ui.Button("", false, 14);

    public Problem Current { get; private set; }
    /// <summary>"Not quite": the worked explanation is showing.</summary>
    public bool Learning => learn.IsVisible;
    public bool ShowingMastered => mastered.IsVisible;
    public string[] ShownSteps { get; private set; } = Array.Empty<string>();
    public int Answered => answered;

    public static void Open(string skill) => MainView.Current.Push(new MasteryPracticePage(skill));

    public MasteryPracticePage(string skill)
    {
        Skill = skill;
        me = Lg.Me;
        var info = Problems.Info(skill);

        // ---- the top: skill, standard, counters; the score ring on the right
        var top = new DockPanel();
        var ringBox = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { ringHost, lblZone } };
        lblZone.TextAlignment = TextAlignment.Center; lblZone.MaxWidth = 120;
        ringBox.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(ringBox, Dock.Right); top.Children.Add(ringBox);
        var names = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(info?.Name ?? skill, 22, Ui.TextC, true));
        names.Children.Add(Ui.Text((info != null ? Profiles.GradeName(info.Grade) + "  ·  " + info.Area : "") + (string.IsNullOrEmpty(info?.Std) ? "" : "  ·  " + info.Std), 13, Ui.Dim));
        names.Children.Add(lblStats);
        top.Children.Add(names);
        var topCard = Ui.Card(top, Ui.Surface, 16);

        // ---- the question and its picture
        lblFeedback.IsVisible = false;
        var q = new StackPanel { Spacing = 14, Children = { lblFeedback, lblQuestion, picture } };
        questionCard = Ui.Card(q, Ui.Surface, 18);

        // ---- answers: typed (with a number pad) or choices
        answer.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(answer.Text ?? ""); } };
        var go = Ui.Button("Check", true, 18); go.Margin = new Thickness(10, 0, 0, 0);
        go.Click += (s, e) => Submit(answer.Text ?? "");
        lblUnit.VerticalAlignment = VerticalAlignment.Center; lblUnit.Margin = new Thickness(8, 0, 0, 0);
        var entry = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { answer, lblUnit, go } };
        typed.Children.Add(entry);
        typed.Children.Add(pad);
        answerArea.Children.Add(choices);
        answerArea.Children.Add(typed);

        // ---- "Not quite": the answer and the worked steps
        var gotIt = Ui.Button("Got it", true, 17); gotIt.MinWidth = 140; gotIt.HorizontalAlignment = HorizontalAlignment.Left; gotIt.Margin = new Thickness(0, 6, 0, 0);
        gotIt.Click += (s, e) => GotIt();
        learn = new Border
        {
            Child = new StackPanel { Spacing = 8, Children = { learnBody, gotIt } }, Padding = new Thickness(18, 14), CornerRadius = new CornerRadius(14),
            Background = Ui.Brush("#2A2110"), BorderBrush = Ui.Brush(Ui.Yellow, 160), BorderThickness = new Thickness(1.5), IsVisible = false,
        };

        // ---- Mastered!
        var keep = Ui.Button("Keep practicing", false, 16); keep.Margin = new Thickness(0, 0, 10, 8);
        keep.Click += (s, e) => KeepPracticing();
        var map = Ui.Button("Back to the skill map", true, 16); map.Margin = new Thickness(0, 0, 0, 8);
        map.Click += (s, e) => BackToMap();
        var mtitle = Ui.Text("★  Mastered!", 32, Ui.Green, true); mtitle.TextAlignment = TextAlignment.Center;
        var mtext = Ui.Text("You reached 100 in " + (info?.Name ?? skill) + ". Amazing work!", 17, Ui.TextC); mtext.TextAlignment = TextAlignment.Center;
        mastered = new Border
        {
            Child = new StackPanel { Spacing = 10, Children = { mtitle, mtext, MasteredReward(), new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { keep, map } } } },
            Padding = new Thickness(20, 18), CornerRadius = new CornerRadius(16), Background = Ui.Brush("#0F2A22"), BorderBrush = Ui.Brush(Ui.Green, 180), BorderThickness = new Thickness(2), IsVisible = false,
        };

        scratchToggle.HorizontalAlignment = HorizontalAlignment.Left; scratchToggle.MinHeight = 44;
        scratchToggle.Click += (s, e) => { scratchHidden = !scratchHidden; ShowScratch(); };
        ShowScratch();
        var body = new StackPanel { Spacing = 12, Children = { topCard, mastered, questionCard, learn, answerArea, scratchToggle, Scratch } };
        View = Ui.Page(body, 980);
        View.SizeChanged += (s, e) => Layout(e.NewSize.Width);
        clock.Tick += (s, e) => UpdateStats();
        View.AttachedToVisualTree += (s, e) => clock.Start();
        View.DetachedFromVisualTree += (s, e) => clock.Stop();
        UpdateRing();
        Next();
    }

    Control MasteredReward()
    {
        if (me == null || me.Grade <= 0) return new Panel();
        var t = Ui.Text("+" + MasteryReward + " bonus " + (global::LearningGames.Rewards.Builds(me.Grade) ? "blocks for your build" : "points for your lab"), 15, "#FDE68A", true);
        t.TextAlignment = TextAlignment.Center;
        return t;
    }

    /// <summary>Reward points for mastering a skill (on top of the point for each right answer).</summary>
    public const int MasteryReward = 5;

    bool narrow;
    void Layout(double w)
    {
        narrow = w < 640;
        pad.Columns = narrow ? 4 : 8;
        pad.Width = narrow ? Math.Min(w - 40, 340) : 460;
        lblQuestion.FontSize = narrow ? 20 : 24;
        answer.Width = narrow ? 150 : 200;
    }

    void UpdateRing()
    {
        var m = me?.MasteryIfAny(Skill);
        int score = m?.Score ?? 0;
        ringHost.Content = MasteryUi.Ring(score, 84, (m?.Answered ?? 0) > 0);
        lblZone.Text = M.ZoneName(score);
        lblZone.Foreground = Ui.Brush(MasteryUi.ZoneColor(score));
        UpdateStats();
    }

    void UpdateStats()
    {
        var t = DateTime.Now - opened;
        int total = me?.MasteryAnswered(Skill) ?? 0;
        lblStats.Text = "Questions answered: " + answered + (total > answered ? " (" + total + " in all)" : "") + "  ·  Time " + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
    }

    int Score => me?.MasteryScore(Skill) ?? 0;

    void ShowScratch()
    {
        Scratch.IsVisible = !scratchHidden;
        scratchToggle.Content = scratchHidden ? "✏  Show scratch paper" : "Hide scratch paper";
    }

    void Next()
    {
        Scratch.Clear();
        Current = Problems.Make(Skill, M.LevelFor(Score));
        lblQuestion.Text = Current.Text;
        var pic = GamePage.PictureFor(Current);
        picture.Content = pic == null ? null : new Viewbox { Child = pic, StretchDirection = StretchDirection.DownOnly, MaxHeight = 300 };
        picture.IsVisible = pic != null;
        choices.Children.Clear();
        bool choice = Current.Kind == "choice";
        choices.IsVisible = choice; typed.IsVisible = !choice;
        if (choice)
            foreach (var c in Current.Choices)
            {
                var b = new Button
                {
                    Content = new TextBlock { Text = c, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 380 },
                    FontSize = c.Length > 14 ? 16 : 22, FontWeight = FontWeight.SemiBold, MinWidth = 90, MinHeight = 56, Margin = new Thickness(6), Padding = new Thickness(16, 8),
                    CornerRadius = new CornerRadius(10), Background = Ui.Brush(Ui.Surface3), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1),
                    HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = c,
                };
                string cc = c;
                b.Click += (s, e) => Submit(cc);
                choices.Children.Add(b);
            }
        else
        {
            answer.Text = ""; lblUnit.Text = Current.Unit; lblUnit.IsVisible = !string.IsNullOrEmpty(Current.Unit);
            BuildPad();
            if (!Services.Platform.Touch) Dispatcher.UIThread.Post(() => answer.Focus());
        }
        answerArea.IsVisible = true;
        shownAt = DateTime.Now;
    }

    void BuildPad()
    {
        pad.Children.Clear();
        // two keys change with the answer (as in the games): a space for mixed numbers, R for remainders
        string k1 = Current.Kind == "mixed" || Current.Kind == "fraction" || Current.Kind == "simplest" ? " " : ":";
        string k2 = Current.Kind == "qr" ? " R " : "$";
        foreach (var k in new[] { "7", "8", "9", "/", "4", "5", "6", ".", "1", "2", "3", "-", "0", k1, k2, "⌫" })
        {
            string label = k == " " ? "␣" : k.Trim();
            var b = new Button
            {
                Content = label, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(3), Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false, CornerRadius = new CornerRadius(8), Background = Ui.Brush(Ui.Surface3),
            };
            if (k == " ") ToolTip.SetTip(b, "Space (for mixed numbers like 1 3/4)");
            if (k == "⌫") ToolTip.SetTip(b, "Delete");
            string kk = k;
            b.Click += (s, e) =>
            {
                string t = answer.Text ?? "";
                if (kk == "⌫") { if (t.Length > 0) answer.Text = t.Substring(0, t.Length - 1); }
                else answer.Text = t + kk;
                answer.CaretIndex = (answer.Text ?? "").Length;
            };
            pad.Children.Add(b);
        }
    }

    /// <summary>One answer: right moves on (or celebrates 100); wrong shows how to solve this exact question.</summary>
    public void Submit(string given)
    {
        if (Current == null || Learning || ShowingMastered || me == null) return;
        if (string.IsNullOrWhiteSpace(given)) return;
        bool ok = Current.IsCorrect(given);
        int secs = (int)Math.Round((DateTime.Now - shownAt).TotalSeconds);
        var (before, after) = me.RecordMastery(Skill, ok, secs, Current.Text, Current.Answer, given);
        Family.SendActivity(me, Skill, Current.Text, Current.Answer, given, ok);
        answered++;
        if (ok) right++;
        UpdateRing();
        MainView.Current.RefreshBar();
        if (ok)
        {
            if (after >= M.Max && before < M.Max)
            {
                if (me.Grade > 0) me.AddRewardPoints(MasteryReward);
                Profiles.Save();
                Snd.Win();
                lblFeedback.IsVisible = false;
                questionCard.IsVisible = false; answerArea.IsVisible = false;
                mastered.IsVisible = true;
                return;
            }
            Snd.Correct();
            lblFeedback.Text = Lg.Praise(right) + "  " + (after > before ? "+" + (after - before) + "  →  " + after : "Score " + after);
            lblFeedback.Foreground = Ui.Brush(Ui.Green);
            lblFeedback.IsVisible = true;
            Next();
            return;
        }
        Snd.Wrong();
        ShowLearn(given, before, after);
    }

    void ShowLearn(string given, int before, int after)
    {
        lblFeedback.IsVisible = false;
        answerArea.IsVisible = false;
        learnBody.Children.Clear();
        learnBody.Children.Add(Ui.Text("Not quite.", 24, Ui.Yellow, true));
        var ans = new WrapPanel();
        ans.Children.Add(Ui.Text("You answered " + given.Trim() + ".  ", 16, Ui.Muted));
        ans.Children.Add(Ui.Text("The answer is " + global::LearningGames.Worked.AnswerText(Current) + ".", 16, Ui.TextC, true));
        learnBody.Children.Add(ans);
        if (before > after) learnBody.Children.Add(Ui.Text("Score " + before + " → " + after + ". " + (M.ZoneOf(before) == M.Zone.Challenge ? "In the Challenge zone a miss costs more, but you'll get it back." : "No worries: a few right answers win it back."), 13, Ui.Muted));
        var h = Ui.Text("Learn from it", 18, Ui.AccentText, true); h.Margin = new Thickness(0, 8, 0, 2);
        learnBody.Children.Add(h);
        ShownSteps = global::LearningGames.Worked.Steps(Current);
        int i = 0;
        foreach (string step in ShownSteps)
        {
            i++;
            bool last = i == ShownSteps.Length;
            var num = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Brush(last ? Ui.Green : MasteryUi.MathBlue, 60), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = i.ToString(), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Ui.Brush(last ? Ui.Green : Ui.AccentText), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var row = new DockPanel();
            DockPanel.SetDock(num, Dock.Left); row.Children.Add(num);
            var t = Ui.Text(step, 16, last ? Ui.Green : Ui.TextC, last); t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            learnBody.Children.Add(row);
        }
        learn.IsVisible = true;
        Dispatcher.UIThread.Post(() => learn.BringIntoView(), DispatcherPriority.Loaded);
    }

    /// <summary>"Got it": the next question.</summary>
    public void GotIt()
    {
        if (!Learning) return;
        learn.IsVisible = false;
        Next();
    }

    public void KeepPracticing()
    {
        mastered.IsVisible = false;
        questionCard.IsVisible = true;
        Next();
    }

    void BackToMap()
    {
        var mv = MainView.Current;
        if (mv.Has<SkillMapPage>()) mv.BackTo<SkillMapPage>();
        else mv.Replace(new SkillMapPage());
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (Learning && (e.Key == Key.Enter || e.Key == Key.Space)) { GotIt(); return true; }
        if (Current == null || Current.Kind != "choice" || Learning || ShowingMastered) return false;
        int i = QuizPage.KeyNum(e.Key);
        if (i >= 0 && i < Current.Choices.Length) { Submit(Current.Choices[i]); return true; }
        return false;
    }

    public void Closed()
    {
        clock.Stop();
        Lg.StopSpeaking();
        Profiles.Save();
    }
}
