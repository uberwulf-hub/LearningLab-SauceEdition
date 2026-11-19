// Learning Lab - Languages: cartoon conversations (Cartoons.cs has the format and the rules). CartoonsPage lists them
// by level, each opening once its unit is reached (as stories do). CartoonPage plays one: the stage (CartoonStage)
// follows the cartoon's own audio (each character in its own voice: the device's Spanish voice, a second one when the
// device has it, and the character's pitch), with Watch again, Slower and captions (off at first: the point is to
// work out what's going on by ear and by what they do). Then "What's going on?" and a few more questions, and the
// script with every word tappable. Without a Spanish voice it still plays, silently, with Spanish captions.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class CartoonsPage : IPage
{
    readonly LangCourse c;
    readonly LangContent content;
    readonly StackPanel body = new() { Spacing = 8 };
    public string Title => "Cartoons";
    public Control View { get; }

    public CartoonsPage(LangCourse c, LangContent content)
    {
        this.c = c; this.content = content;
        View = Ui.Page(body, 1100);
        View.AttachedToVisualTree += (s, e) => Fill();
        Fill();
    }

    public bool Open(LangCartoon x) => LUi.Reached(c, content, x.After);

    void Fill()
    {
        body.Children.Clear();
        body.Children.Add(LUi.Head("Cartoons", "Short, silly cartoons in Spanish, about half a minute each. Watch, listen, and work out what's going on. Captions are there if you need them."));
        foreach (var level in content.Cartoons.Select(s => s.Level).Distinct().OrderBy(x => x))
        {
            var h = Ui.Text(LangCourse.LevelName(level), 18, LUi.AmberText, true);
            h.Margin = new Thickness(0, 12, 0, 2);
            body.Children.Add(h);
            var g = Ui.Tiles(content.Cartoons.Where(x => x.Level == level).Select(Tile).ToList(), 280, 10);
            g.Margin = new Thickness(-5, 0, -5, 0);
            body.Children.Add(g);
        }
    }

    Control Tile(LangCartoon x)
    {
        bool open = Open(x);
        var p = Lx.Progress;
        var sp = new StackPanel { Spacing = 4 };
        var still = new CartoonStage(x, CartoonTimeline.From(x, null)) { Time = 1.1, Opacity = open ? 1 : 0.35 };
        sp.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Viewbox { Child = still, Stretch = Stretch.Uniform, Height = 140 } });
        sp.Children.Add(LUi.Wrap(Ui.Text(x.Title, 17, open ? Ui.TextC : Ui.Dim, true)));
        sp.Children.Add(LUi.Wrap(Ui.Text(x.TitleEn, 13.5, open ? Ui.Muted : Ui.Dim)));
        int times = p.CartoonTimes(c.Language, x.Id);
        var u = c.Unit(x.After);
        int qs = x.AllQuestions().Count;
        string state = !open ? "Opens with unit " + (u?.Number ?? 0) + ": " + (u?.Title ?? x.After)
            : times > 0 ? "Watched  ·  " + p.CartoonBest(c.Language, x.Id) + " of " + qs + " questions right"
            : "About " + Math.Round(x.EstimatedSeconds() / 5) * 5 + " seconds  ·  " + qs + " questions";
        sp.Children.Add(LUi.Wrap(Ui.Text((times > 0 ? "✓  " : !open ? "\U0001F512  " : "") + state, 12.5, times > 0 ? Ui.Green : Ui.Muted)));
        var t = Ui.Tile(sp, open ? LUi.Amber : Ui.BorderC, () => { if (open) MainView.Current.Push(new CartoonPage(c, x)); });
        t.IsEnabled = open;
        t.Padding = new Thickness(12);
        return t;
    }
}

public class CartoonPage : IPage, IKeyPage
{
    readonly LangCourse c;
    readonly LangCartoon cartoon;
    readonly Random rnd;
    readonly StackPanel body = new() { Spacing = 10 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch clock = new();
    readonly CartoonStage stage;
    public string Title => cartoon.Title;
    public Control View { get; }

    public enum Part { Watch, Questions, Done }
    public Part Now { get; private set; } = Part.Watch;
    /// <summary>0 no captions, 1 Spanish, 2 Spanish and English.</summary>
    public int Captions { get => stage.Captions; set { stage.Captions = Math.Clamp(value, 0, 2); stage.InvalidateVisual(); } }
    public bool Slow { get; private set; }
    public bool Playing { get; private set; }
    public bool Watched { get; private set; }
    public bool Silent { get; private set; }
    public string Status { get; private set; } = "";
    public double Time => stage.Time;
    public CartoonTimeline Timeline => stage.Timeline;
    public LessonFile Audio => file;
    public int Question { get; private set; }
    public int Right { get; private set; }
    public bool Answered { get; private set; }
    public List<string> Choices { get; private set; } = new();
    public string Word { get; private set; }
    public string WordMeaning { get; private set; }
    readonly List<StoryQuestion> questions;
    readonly LangStory asStory;
    string picked;
    int wordLine = -1;

    IPlayer player;
    LessonFile file;
    bool making;
    double clockFrom;

    public CartoonPage(LangCourse c, LangCartoon cartoon, int seed = -1)
    {
        this.c = c; this.cartoon = cartoon;
        rnd = seed < 0 ? new Random() : new Random(seed);
        questions = cartoon.AllQuestions();
        asStory = cartoon.AsStory();
        stage = new CartoonStage(cartoon, CartoonTimeline.From(cartoon, null));
        timer.Tick += (s, e) => Tick();
        View = Ui.Page(body, 960);
        Show();
    }

    /// <summary>The second Spanish voice for a cartoon's other characters, when the device has one.</summary>
    internal static VoiceInfo SecondVoice(LangCourse c)
    {
        var first = Lx.Target(c);
        if (first == null) return null;
        var others = Lx.VoicesFor(c.Language).Where(v => v.Id != first.Id).ToList();
        return others.FirstOrDefault(v => string.Equals(v.Lang, first.Lang, StringComparison.OrdinalIgnoreCase)) ?? others.FirstOrDefault();
    }

    /// <summary>Makes the cartoon's audio (or finds it made) and lines the animation up with it.</summary>
    public async Task Prepare()
    {
        if (file != null || making || Silent) return;
        making = true;
        Status = "Getting the cartoon ready...";
        Show();
        try
        {
            var l = Lesson.BuildCartoon(cartoon, c.Language, false);
            file = await LessonAudio.Make(l, Lx.Target(c), Lx.English(), Lx.Rate * (Slow ? 0.8 : 1), null, CancellationToken.None, SecondVoice(c));
            stage.Timeline = CartoonTimeline.FromMarks(cartoon, file.ItemStarts, file.Duration);
            Status = "";
        }
        catch (InvalidOperationException ex)
        {
            Silent = true;
            if (Captions == 0) Captions = 1;
            Status = ex.Message + " Until then it plays without sound, with captions.";
        }
        catch (Exception ex) { Silent = true; if (Captions == 0) Captions = 1; Status = "The voices couldn't be made (" + ex.Message + "). It plays without sound, with captions."; }
        finally { making = false; }
        Show();
    }

    /// <summary>Play / pause.</summary>
    public async Task Play()
    {
        if (Playing) { Pause(); return; }
        await Prepare();
        if (Time >= Timeline.Duration - 0.05) Seek(0);
        if (!Silent && file != null)
        {
            if (player == null)
            {
                player = PlatformAudio.NewPlayer();
                if (player == null || !player.Open(file.File)) { player?.Dispose(); player = null; Silent = true; if (Captions == 0) Captions = 1; Status = "This device couldn't play the sound. It plays with captions."; }
                else player.Ended += () => Dispatcher.UIThread.Post(Ended);
            }
            if (player != null) { player.Position = Time; player.Play(); }
        }
        clockFrom = Time; clock.Restart();
        Playing = true;
        timer.Start();
        Show();
    }

    public void Pause()
    {
        if (!Playing) return;
        player?.Pause();
        clock.Stop(); timer.Stop();
        Playing = false;
        Show();
    }

    /// <summary>Jumps to a moment (the checks use it for pictures; Watch again uses 0).</summary>
    public void Seek(double seconds)
    {
        stage.Time = Math.Clamp(seconds, 0, Timeline.Duration);
        if (player != null) player.Position = stage.Time;
        clockFrom = stage.Time; if (Playing) clock.Restart(); else clock.Reset();
        stage.InvalidateVisual();
    }

    public async Task WatchAgain() { Pause(); Seek(0); await Play(); }

    public async Task ToggleSlow()
    {
        bool was = Playing;
        Pause();
        StopPlayer();
        Slow = !Slow;
        file = null;
        if (!Silent) await Prepare();
        Seek(0);
        if (was) await Play(); else Show();
    }

    public void CycleCaptions() { Captions = (Captions + 1) % 3; Show(); }

    void Tick()
    {
        double t = player != null && !Silent ? player.Position : clockFrom + clock.Elapsed.TotalSeconds;
        // a player that reports no position (or stops moving) is overtaken by the clock
        double byClock = clockFrom + clock.Elapsed.TotalSeconds;
        if (player != null && Math.Abs(t - byClock) > 1.5 && t < 0.01) t = byClock;
        stage.Time = t;
        stage.InvalidateVisual();
        if (t >= Timeline.Duration) Ended();
    }

    /// <summary>The checks: moves the cartoon on as if that many seconds played.</summary>
    public void Advance(double seconds)
    {
        stage.Time = Math.Min(Timeline.Duration, stage.Time + seconds);
        if (player != null) player.Position = stage.Time;
        stage.InvalidateVisual();
        if (stage.Time >= Timeline.Duration) Ended();
    }

    void Ended()
    {
        if (!Playing && Watched && stage.Time >= Timeline.Duration) return;
        player?.Pause();
        clock.Stop(); timer.Stop();
        Playing = false;
        stage.Time = Timeline.Duration;
        if (!Watched) Feel.Tap();
        Watched = true;
        Show();
    }

    void StopPlayer() { timer.Stop(); clock.Stop(); try { player?.Dispose(); } catch (Exception) { } player = null; Playing = false; }

    // ---------------------------------------------------------------- questions
    public void StartQuestions()
    {
        Pause();
        Now = Part.Questions; Question = 0; Right = 0; NewQuestion(); Show();
    }

    void NewQuestion() { Answered = false; picked = null; Choices = questions[Question].Choices(rnd); }

    public StoryQuestion CurrentQuestion => Now == Part.Questions && Question < questions.Count ? questions[Question] : null;

    public void Answer(string choice)
    {
        var q = CurrentQuestion;
        if (q == null || Answered) return;
        picked = choice; Answered = true;
        bool ok = Answers.Same(choice, q.Answer);
        if (ok) Right++;
        Feel.Right(ok);
        Show();
    }

    public void Continue()
    {
        if (Now != Part.Questions || !Answered) return;
        Question++;
        if (Question >= questions.Count) Finish();
        else NewQuestion();
        Show();
    }

    void Finish()
    {
        Now = Part.Done;
        Lx.Progress.FinishCartoon(c.Language, cartoon.Id, Right);
        Lx.Progress.AddPoints(c.Language, 5 + Right);
        Lx.Save();
        Feel.Done();
        Show();
    }

    /// <summary>Back to the cartoon with Spanish captions (from the end).</summary>
    public void WatchWithCaptions()
    {
        Now = Part.Watch;
        Captions = Math.Max(Captions, 1);
        Seek(0);
        Show();
    }

    public void TapWord(int line, int word)
    {
        var words = LangStory.LineWords(cartoon.Lines[line].Target);
        WordMeaning = asStory.Meaning(c, words, word, out var shown);
        Word = shown; wordLine = line;
        if (!string.IsNullOrEmpty(shown)) Lx.Say(c, shown);
        Show();
    }

    // ---------------------------------------------------------------- the page
    void Show()
    {
        body.Children.Clear();
        switch (Now)
        {
            case Part.Questions: Questions(); break;
            case Part.Done: Recap(); break;
            default: WatchPart(); break;
        }
    }

    void WatchPart()
    {
        body.Children.Add(LUi.Head(cartoon.Title, cartoon.TitleEn + "  ·  " + LangCourse.LevelName(cartoon.Level)));
        LUi.Warn(body, c);
        var frame = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, BorderThickness = new Thickness(2), BorderBrush = Ui.Brush(LUi.Amber, 120) };
        stage.Parent?.GetType();   // (the stage is moved from the last Show's frame)
        if (stage.Parent is Viewbox old) old.Child = null;
        frame.Child = new Viewbox { Child = stage, Stretch = Stretch.Uniform };
        body.Children.Add(frame);
        if (!string.IsNullOrEmpty(Status)) body.Children.Add(LUi.Wrap(Ui.Text(Status, 13.5, Ui.Muted)));

        var row = new WrapPanel();
        Button B(string text, Func<Task> act, bool primary = false)
        {
            var b = primary ? LUi.Primary(text, 16) : Ui.Button(text, false, 15);
            b.Margin = new Thickness(0, 0, 8, 8); b.MinHeight = 44;
            b.Click += async (s, e) => await act();
            row.Children.Add(b);
            return b;
        }
        B(Playing ? "⏸  Pause" : Watched ? "▶  Play" : "▶  Play the cartoon", Play, !Watched);
        if (Watched || Time > 0) B("↺  Watch again", WatchAgain);
        B(Slow ? "Normal speed" : "\U0001F422  Slower", ToggleSlow);
        B(Captions == 0 ? "Captions: off" : Captions == 1 ? "Captions: Spanish" : "Captions: Spanish + English", () => { CycleCaptions(); return Task.CompletedTask; });
        body.Children.Add(row);

        body.Children.Add(LUi.Wrap(Ui.Text(Watched
            ? "Got the idea? Watch it again as many times as you like, then answer."
            : "Watch and listen first. Try it without captions: their faces and what they do are clues.", 13.5, Ui.Dim)));
        var go = LUi.Primary("What's going on?", 17);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Opacity = Watched ? 1 : 0.85;
        go.Click += (s, e) => StartQuestions();
        body.Children.Add(go);
    }

    void Questions()
    {
        var q = CurrentQuestion;
        body.Children.Add(Ui.Text(cartoon.Title + "  ·  question " + (Question + 1) + " of " + questions.Count, 13.5, Ui.Muted));
        body.Children.Add(LUi.Bar(Question / (double)questions.Count, LUi.Amber, 6));
        body.Children.Add(LUi.Wrap(Ui.Text(q.Question, 20, Ui.TextC, true)));
        var list = new StackPanel { Spacing = 8 };
        for (int k = 0; k < Choices.Count; k++)
        {
            string ch = Choices[k];
            var b = LUi.Choice((Platform.Touch ? "" : (k + 1) + "   ") + ch, 16.5);
            if (Answered) { if (Answers.Same(ch, q.Answer)) LUi.Mark(b, true); else if (ch == picked) LUi.Mark(b, false); }
            b.Click += (s, e) => Answer(ch);
            list.Children.Add(b);
        }
        body.Children.Add(list);
        if (Answered)
        {
            bool ok = Answers.Same(picked, q.Answer);
            body.Children.Add(LUi.Wrap(Ui.Text(ok ? "Right!" : "Not quite. It's: " + q.Answer, 16, ok ? Ui.Green : LUi.AmberText, true)));
            var next = LUi.Primary(Question + 1 < questions.Count ? "Next" : "Finish", 17);
            next.HorizontalAlignment = HorizontalAlignment.Stretch;
            next.Click += (s, e) => Continue();
            body.Children.Add(next);
        }
        else
        {
            var back = Ui.Button("↺  Watch it again first", false, 14);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Click += (s, e) => { Now = Part.Watch; Seek(0); Show(); };
            body.Children.Add(back);
        }
    }

    void Recap()
    {
        body.Children.Add(LUi.Head("Cartoon finished!", Right + " of " + questions.Count + " questions right. +" + (5 + Right) + " points."));
        body.Children.Add(LUi.GoalCard(c));
        var row = new WrapPanel();
        var again = Ui.Button("Watch with captions", false, 16); again.Margin = new Thickness(0, 0, 10, 10);
        again.Click += (s, e) => WatchWithCaptions();
        var back = LUi.Primary("Back to the cartoons", 16); back.Margin = new Thickness(0, 0, 10, 10);
        back.Click += (s, e) => MainView.Current.Back();
        row.Children.Add(back); row.Children.Add(again);
        body.Children.Add(row);
        var h = Ui.Text("What they said", 18, LUi.AmberText, true);
        h.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(h);
        body.Children.Add(LUi.Wrap(Ui.Text(Platform.Touch ? "Tap a word for its meaning." : "Click a word for its meaning.", 13, Ui.Dim)));
        for (int k = 0; k < cartoon.Lines.Count; k++)
        {
            body.Children.Add(ScriptLine(k));
            if (Word != null && k == wordLine) body.Children.Add(WordCard());
        }
    }

    Control ScriptLine(int k)
    {
        var line = cartoon.Lines[k];
        var who = cartoon.Actor(line.Who);
        var sp = new StackPanel { Spacing = 2 };
        sp.Children.Add(Ui.Text(who?.Name ?? line.Who, 12.5, who?.Color ?? LUi.AmberText, true));
        var words = new WrapPanel();
        var list = LangStory.LineWords(line.Target);
        for (int w = 0; w < list.Count; w++)
        {
            int kw = w;
            var b = new Button
            {
                Content = list[w], FontSize = 17, Padding = new Thickness(3, 2), Margin = new Thickness(0, 0, 4, 2), MinHeight = 34,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush(Ui.BorderStrong),
                Foreground = Ui.Brush(Ui.TextC), Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.Click += (s, e) => TapWord(k, kw);
            words.Children.Add(b);
        }
        var r = new DockPanel();
        var say = LUi.Speaker(() => Lx.Say(c, line.Target));
        say.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(say, Dock.Left);
        r.Children.Add(say);
        r.Children.Add(words);
        sp.Children.Add(r);
        var en = LUi.Wrap(Ui.Text(line.English, 13.5, Ui.Muted)); en.Margin = new Thickness(46, 0, 0, 0);
        sp.Children.Add(en);
        return Ui.Card(sp, Ui.Surface, 8);
    }

    Control WordCard()
    {
        var sp = new StackPanel { Spacing = 4 };
        var head = new DockPanel();
        var close = Ui.IconButton("✕", "Close");
        close.Click += (s, e) => { Word = null; WordMeaning = null; Show(); };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = Word, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(LUi.AmberText), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(head);
        sp.Children.Add(LUi.Wrap(Ui.Text(WordMeaning ?? "This word isn't in the word list yet. The English under the line helps.", 15, WordMeaning != null ? Ui.TextC : Ui.Muted)));
        var card = Ui.Card(sp, Ui.Surface2, 12);
        card.BorderBrush = Ui.Brush(LUi.Amber, 140);
        return card;
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (Now == Part.Questions && !Answered)
        {
            int k = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
            if (k >= 0 && k < Choices.Count) { Answer(Choices[k]); return true; }
        }
        if (e.Key == Key.Enter && Now == Part.Questions && Answered) { Continue(); return true; }
        if (e.Key == Key.Space && Now == Part.Watch) { _ = Play(); return true; }
        return false;
    }

    public void Closed() { StopPlayer(); LessonAudio.StopSaying(); }
}
