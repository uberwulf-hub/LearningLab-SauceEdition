// Learning Lab - Languages: lessons to listen to (in the car). The page makes the lesson into one audio file
// (LessonAudio) and plays it with big buttons; on a phone it keeps playing with the screen off. What was heard counts
// as met (it comes back in Review), and the minutes add up.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class CarPage : IPage
{
    readonly LangCourse c;
    readonly StackPanel body = new() { Spacing = 12 };
    readonly ComboBox what = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 15 };
    readonly ComboBox style = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 15 };
    readonly ComboBox speed = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 15 };
    readonly ComboBox pauses = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 15 };
    readonly TextBlock status = new() { FontSize = 14, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock bigTarget = new() { FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Ui.Brush("#FBBF24"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    readonly TextBlock bigEnglish = new() { FontSize = 20, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    readonly TextBlock time = new() { FontSize = 13, Foreground = Ui.Brush(Ui.Muted) };
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 1, Height = 8, CornerRadius = new CornerRadius(4) };
    readonly Button play, prev, next, save;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly List<(string Label, Func<List<LangItem>> Items, string Unit, string Style, LangStory Story)> choices = new();
    readonly TextBlock styleText = new() { FontSize = 13, Foreground = Ui.Brush(Ui.Dim), TextWrapping = TextWrapping.Wrap };
    readonly LangContent content;
    /// <summary>Choices that are cartoons (by index in choices): heard only, with "What's going on?" at the end.</summary>
    readonly Dictionary<int, LangCartoon> cartoonAt = new();

    CancellationTokenSource making;
    Lesson lesson;
    LessonFile file;
    IPlayer player;
    int lastItem = -1;
    double counted;   // seconds of this lesson already added to "minutes listened"
    bool building;

    public string Title => "In the car";
    public Control View { get; }
    public LessonFile File => file;
    public Lesson Current => lesson;
    public int ItemNow => lastItem;
    public string StatusText => status.Text;

    /// <summary>Picks a lesson by its label's start ("1. Hello", "Story: El ...") and a style (the checks).</summary>
    public bool Choose(string labelStart, string styleId)
    {
        int k = choices.FindIndex(x => x.Label.StartsWith(labelStart, StringComparison.Ordinal));
        if (k < 0) return false;
        what.SelectedIndex = k;
        if (styleId != null && Array.IndexOf(Lesson.Styles, styleId) >= 0) style.SelectedIndex = Array.IndexOf(Lesson.Styles, styleId);
        Reset();
        return true;
    }

    public IEnumerable<string> Labels => choices.Select(x => x.Label);

    public CarPage(LangCourse c, LangUnit unit)
    {
        this.c = c;
        content = Lx.Content(Lx.IdOf(c));
        var p = Lx.Progress;
        int due = p.DueItems(c).Count;
        bool anyMet = c.AllItems.Any(i => p.Seen(c.Language, i.Id));
        if (anyMet) choices.Add(("Review: a mix of what you've met" + (due > 0 ? " (" + due + " due)" : ""), () => Lesson.ReviewMix(c, p, 25, new Random()), null, "recall", null));
        foreach (var u in c.Units)
        {
            var uu = u;
            choices.Add((u.Number + ". " + u.Title, () => uu.Items.ToList(), u.Id, p.UnitState(c.Language, u).Met >= u.Items.Count ? "recall" : "learn", null));
        }
        foreach (var st in content.Stories.Where(s => LUi.Reached(c, content, s.After)).OrderBy(s => s.Level))
            choices.Add(("Story: " + st.Title + " (" + st.TitleEn + ")", () => new List<LangItem>(), null, "story", st));
        foreach (var ct in content.Cartoons.Where(x => LUi.Reached(c, content, x.After)).OrderBy(x => x.Level))
        {
            cartoonAt[choices.Count] = ct;
            choices.Add(("Cartoon: " + ct.Title + " (" + ct.TitleEn + ")", () => new List<LangItem>(), null, "cartoon", null));
        }
        foreach (var ch in choices) what.Items.Add(ch.Label);
        int pick;
        if (unit != null) pick = choices.FindIndex(x => x.Unit == unit.Id);
        else
        {
            var nextUnit = c.Units.FirstOrDefault(u => p.UnitState(c.Language, u).Learned < u.Items.Count * 0.8) ?? c.Units[0];
            pick = due >= 10 && anyMet ? 0 : choices.FindIndex(x => x.Unit == nextUnit.Id);
        }
        what.SelectedIndex = Math.Max(0, pick);
        foreach (var s in Lesson.Styles) style.Items.Add(Lesson.StyleName(s));
        style.SelectedIndex = Math.Max(0, Array.IndexOf(Lesson.Styles, choices[what.SelectedIndex].Style));
        foreach (var s in new[] { "Slower", "Normal", "Faster" }) speed.Items.Add(s);
        speed.SelectedIndex = p.Rate < 0.85 ? 0 : p.Rate > 0.95 ? 2 : 1;
        foreach (var s in new[] { "Normal pauses", "Longer pauses", "Much longer pauses" }) pauses.Items.Add(s);
        pauses.SelectedIndex = p.Setting("pauses") switch { "1.5" => 1, "2" => 2, _ => 0 };

        what.SelectionChanged += (s, e) => { if (what.SelectedIndex >= 0) { var st = choices[what.SelectedIndex].Style; if (st != "story") style.SelectedIndex = Math.Max(0, Array.IndexOf(Lesson.Styles, st)); Reset(); } };
        style.SelectionChanged += (s, e) => Reset();
        speed.SelectionChanged += (s, e) => { p.Rate = new[] { 0.75, 0.9, 1.05 }[Math.Max(0, speed.SelectedIndex)]; Lx.Save(); Reset(); };
        pauses.SelectionChanged += (s, e) => { p.SetSetting("pauses", new[] { "1", "1.5", "2" }[Math.Max(0, pauses.SelectedIndex)]); Lx.Save(); Reset(); };

        body.Children.Add(LUi.Head("Listen and learn", "Pick a lesson and press play before you set off. " + (Platform.Touch ? "It keeps playing with the screen off, through the car's speakers when the phone is connected." : "It plays through this computer's speakers or headphones.")));
        LUi.Warn(body, c);
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        void Field(string label, Control box, int col, int row, int span = 1)
        {
            var sp = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, col == 0 && span == 1 ? 8 : 0, 10), Children = { Ui.Text(label, 12.5, Ui.Muted), box } };
            Grid.SetColumn(sp, col); Grid.SetRow(sp, row); Grid.SetColumnSpan(sp, span);
            form.Children.Add(sp);
        }
        Field("Lesson", what, 0, 0, 2);
        Field("How it goes", style, 0, 1, 2);
        Field("Speed", speed, 0, 2);
        Field("Time to repeat", pauses, 1, 2);
        body.Children.Add(form);

        var face = new StackPanel { Spacing = 8, MinHeight = 130 };
        face.Children.Add(bigTarget);
        face.Children.Add(bigEnglish);
        body.Children.Add(Ui.Card(face, Ui.Surface, 20));
        var tl = new DockPanel();
        DockPanel.SetDock(time, Dock.Right);
        time.Margin = new Thickness(10, 0, 0, 0);
        tl.Children.Add(time);
        tl.Children.Add(bar);
        body.Children.Add(tl);

        prev = Round("prev", 64, "Back one phrase");
        play = Round("play", 92, "Play");
        next = Round("next", 64, "Next phrase");
        prev.Click += (s, e) => Skip(-1);
        next.Click += (s, e) => Skip(+1);
        play.Click += (s, e) => Toggle();
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center, Children = { prev, play, next } });
        body.Children.Add(status);

        save = Ui.Button("Save this lesson as an audio file");
        save.HorizontalAlignment = HorizontalAlignment.Center;
        save.IsVisible = PlatformAudio.SaveAudio != null;
        save.Click += async (s, e) => await SaveAudio();
        body.Children.Add(save);
        body.Children.Add(styleText);

        timer.Tick += (s, e) => Tick();
        View = Ui.Page(body, 760);
        Reset();
    }

    /// <summary>A conversation drill from a unit's lessons: their new phrases in order, with each lesson's tip spoken
    /// first ("Think it through"); a unit without lessons uses its phrases.</summary>
    Lesson Drill(LangUnit u, string label)
    {
        var items = new List<LangItem>();
        var think = new Dictionary<int, string>();
        foreach (var l in content.LessonsOf(u.Id))
        {
            var news = LangContent.NewItems(c, l).Where(i => !items.Contains(i)).ToList();
            if (news.Count == 0) continue;
            if (l.Tip != null && !string.IsNullOrWhiteSpace(l.Tip.Text)) think[items.Count] = "Think it through. " + l.Tip.Text;
            items.AddRange(news);
        }
        if (items.Count == 0) items.AddRange(u.Items);
        return Lesson.BuildDrill(label, c.Language, items, PauseScale, c.Name + ". Conversation drill. " + u.Title + ".", think);
    }

    /// <summary>Drawn player icons (the symbol characters show as emoji on some devices).</summary>
    static Control Icon(string kind, double size)
    {
        string data = kind switch
        {
            "pause" => "M0,0 H6 V20 H0 Z M11,0 H17 V20 H11 Z",
            "prev" => "M0,0 H3 V20 H0 Z M18,0 L4,10 L18,20 Z",
            "next" => "M0,0 L14,10 L0,20 Z M15,0 H18 V20 H15 Z",
            _ => "M2,0 L19,10 L2,20 Z",
        };
        double s = size * 0.36;
        return new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Fill = Ui.Brush(size > 80 ? "#111111" : Ui.TextC), Stretch = Stretch.Uniform, Width = s, Height = s };
    }

    static Button Round(string glyph, double size, string tip)
    {
        var b = new Button
        {
            Content = Icon(glyph, size), Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = Ui.Brush(size > 80 ? "#F59E0B" : Ui.Surface3), Foreground = Ui.Brush(size > 80 ? "#111111" : Ui.TextC),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    double PauseScale => new[] { 1.0, 1.5, 2.0 }[Math.Max(0, pauses.SelectedIndex)];

    /// <summary>A setting changed: stop, and make the new lesson when play is pressed.</summary>
    void Reset()
    {
        if (play == null) return;
        StopPlayer();
        making?.Cancel();
        building = false;
        var ch = choices[Math.Max(0, what.SelectedIndex)];
        string unitTitle = ch.Unit != null ? c.Unit(ch.Unit).Title : "Review";
        string sty = Lesson.Styles[Math.Max(0, style.SelectedIndex)];
        cartoonAt.TryGetValue(Math.Max(0, what.SelectedIndex), out var cartoon);
        style.IsEnabled = ch.Story == null && cartoon == null;
        if (cartoon != null) lesson = Lesson.BuildCartoon(cartoon, c.Language, true, PauseScale);
        else if (ch.Story != null) lesson = Lesson.BuildStory(ch.Story, c.Language, PauseScale);
        else if (sty == "drill" && ch.Unit != null) lesson = Drill(c.Unit(ch.Unit), ch.Label);
        else lesson = Lesson.Build(ch.Label, c.Language, ch.Items(), sty, true, PauseScale, c.Name + ". " + unitTitle + ".");
        styleText.Text = Lesson.StyleText(lesson.Style) + (lesson.Style == "story" || lesson.Style == "cartoon" ? "" : " Repeat out loud: speaking is how it sticks.");
        file = null;
        lastItem = -1; counted = 0;
        bigTarget.Text = ""; int distinct = lesson.Items.Distinct().Count();
        bigEnglish.Text = (lesson.Style == "story" || lesson.Style == "cartoon" ? lesson.Items.Count + " lines" : distinct + " phrases" + (distinct < lesson.Items.Count ? ", each asked " + (1 + Lesson.DrillGaps.Length) + " times" : "")) + ". Press play.";
        bar.Value = 0; time.Text = "";
        play.Content = Icon("play", 92);
        status.Text = Lx.Target(c) == null ? "Add a " + c.Name + " voice first (Voices)." : "";
    }

    /// <summary>Makes the lesson's audio (the checks call it directly). True when it is ready.</summary>
    public async Task<bool> Prepare()
    {
        if (file != null) return true;
        if (building) return false;
        building = true;
        making = new CancellationTokenSource();
        var ct = making.Token;
        var l = lesson;
        try
        {
            var prog = new Progress<(int Done, int Total)>(x => { if (l == lesson) status.Text = "Getting the lesson ready... " + x.Done + " of " + x.Total; });
            var f = await LessonAudio.Make(l, Lx.Target(c), Lx.English(), Lx.Rate, prog, ct, l.Style == "cartoon" ? CartoonPage.SecondVoice(c) : null);
            if (l != lesson || ct.IsCancellationRequested) return false;
            file = f;
            status.Text = "Ready: " + Clock(f.Duration) + " long.";
            time.Text = "0:00 / " + Clock(f.Duration);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (InvalidOperationException ex) { status.Text = ex.Message; return false; }
        catch (Exception ex) { status.Text = "The lesson couldn't be made: " + ex.Message; return false; }
        finally { if (l == lesson) building = false; }
    }

    async void Toggle()
    {
        if (player != null && player.Playing) { player.Pause(); play.Content = Icon("play", 92); timer.Stop(); Count(); return; }
        if (player == null)
        {
            if (!await Prepare()) return;
            player = PlatformAudio.NewPlayer();
            if (player == null || !player.Open(file.File)) { status.Text = "This device couldn't play the lesson."; player?.Dispose(); player = null; return; }
            player.Ended += () => Dispatcher.UIThread.Post(Finished);
        }
        player.Play();
        play.Content = Icon("pause", 92);
        timer.Start();
        status.Text = "";
    }

    void Skip(int by)
    {
        if (player == null || file == null || file.ItemStarts.Length == 0) return;
        int k = file.ItemAt(player.Position);
        // "back" during the first two seconds of a phrase goes to the one before; later it starts this one again
        if (by < 0 && k >= 0 && player.Position - file.ItemStarts[k] > 2) by = 0;
        k = Math.Clamp(k + by, 0, file.ItemStarts.Length - 1);
        player.Position = file.ItemStarts[k];
        Tick();
    }

    void Tick()
    {
        if (player == null || file == null) return;
        double pos = player.Position;
        bar.Value = file.Duration > 0 ? pos / file.Duration : 0;
        time.Text = Clock(pos) + " / " + Clock(file.Duration);
        int k = file.ItemAt(pos);
        if (k != lastItem && k >= 0 && k < lesson.Items.Count)
        {
            lastItem = k;
            var it = lesson.Items[k];
            bigTarget.Text = it.Target;
            bigEnglish.Text = it.English;
            if (c.Item(it.Id) != null) Lx.Progress.Meet(c.Language, it.Id);
        }
        if (pos - counted > 30) Count();
    }

    void Count()
    {
        if (player == null) return;
        double pos = player.Position;
        if (pos > counted) { Lx.Progress.AddListened(c.Language, (pos - counted) / 60.0); counted = pos; Lx.Save(); }
    }

    void Finished()
    {
        if (player == null) return;
        timer.Stop();
        Lx.Progress.AddListened(c.Language, Math.Max(0, file.Duration - counted) / 60.0);
        counted = file.Duration;
        var unit = choices[Math.Max(0, what.SelectedIndex)].Unit;
        if (unit != null) Lx.Progress.Heard(c.Language, unit);
        Lx.Progress.AddPoints(c.Language, 5);
        Lx.Save();
        play.Content = Icon("play", 92);
        bar.Value = 1;
        status.Text = "Lesson finished. " + (unit != null ? "Next time try \"" + Lesson.StyleName("recall") + "\" to see what stuck." : "Well done!");
        StopPlayer();
    }

    /// <summary>The checks: plays the whole lesson at once (marks every item met, as listening does).</summary>
    public void FinishForTest()
    {
        if (file == null) return;
        foreach (var it in lesson.Items) if (c.Item(it.Id) != null) Lx.Progress.Meet(c.Language, it.Id);
        player ??= new NoPlayer();
        Finished();
    }

    sealed class NoPlayer : IPlayer
    {
        public bool Open(string f) => true; public void Play() { } public void Pause() { } public bool Playing => false;
        public double Position { get; set; } public double Duration => 0; public event Action Ended { add { } remove { } } public void Dispose() { }
    }

    async Task SaveAudio()
    {
        if (!await Prepare()) return;
        string name = c.Name + " - " + lesson.Title.Replace(":", " -") + " (" + Lesson.StyleName(lesson.Style) + ")";
        string said;
        try { said = PlatformAudio.SaveAudio(file.File, name); }
        catch (Exception ex) { said = "It couldn't be saved: " + ex.Message; }
        MainView.Current.Message("Save audio", said);
    }

    void StopPlayer()
    {
        timer.Stop();
        Count();
        try { player?.Dispose(); } catch (Exception) { }
        player = null;
    }

    static string Clock(double s) => ((int)s / 60) + ":" + ((int)s % 60).ToString("00");

    public void Closed() { making?.Cancel(); StopPlayer(); LessonAudio.StopSaying(); }
}
