// Learning Lab - Languages: short graded stories. StoriesPage lists them by level; a story opens once the unit named
// in its "after" is reached. StoryPage reads it line by line (each line heard as it appears, "Read it all" plays the
// whole story), every word can be tapped for its meaning, the English of a line shows on a tap, and a few questions
// follow.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class StoriesPage : IPage
{
    readonly LangCourse c;
    readonly LangContent content;
    readonly StackPanel body = new() { Spacing = 8 };
    public string Title => "Stories";
    public Control View { get; }

    public StoriesPage(LangCourse c, LangContent content)
    {
        this.c = c; this.content = content;
        View = Ui.Page(body, 1000);
        View.AttachedToVisualTree += (s, e) => Fill();
        Fill();
    }

    public bool Open(LangStory s) => LUi.Reached(c, content, s.After);

    void Fill()
    {
        body.Children.Clear();
        body.Children.Add(LUi.Head("Stories", "Short stories to read and hear. Tap any word for its meaning. New ones open as you go through the units."));
        foreach (var level in content.Stories.Select(s => s.Level).Distinct().OrderBy(x => x))
        {
            var h = Ui.Text(LangCourse.LevelName(level), 18, LUi.AmberText, true);
            h.Margin = new Thickness(0, 12, 0, 2);
            body.Children.Add(h);
            var tiles = new List<Control>();
            foreach (var s in content.Stories.Where(x => x.Level == level)) tiles.Add(Tile(s));
            var g = Ui.Tiles(tiles, 280, 10);
            g.Margin = new Thickness(-5, 0, -5, 0);
            body.Children.Add(g);
        }
    }

    Control Tile(LangStory s)
    {
        bool open = Open(s);
        var p = Lx.Progress;
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(LUi.Wrap(Ui.Text(s.Title, 17, open ? Ui.TextC : Ui.Dim, true)));
        sp.Children.Add(LUi.Wrap(Ui.Text(s.TitleEn, 13.5, open ? Ui.Muted : Ui.Dim)));
        int times = p.StoryTimes(c.Language, s.Id);
        var u = c.Unit(s.After);
        string state = !open ? "Opens with unit " + (u?.Number ?? 0) + ": " + (u?.Title ?? s.After)
            : times > 0 ? "Read  ·  " + p.StoryBest(c.Language, s.Id) + " of " + s.Questions.Count + " questions right"
            : s.Lines.Count + " lines  ·  " + s.Questions.Count + " questions";
        sp.Children.Add(LUi.Wrap(Ui.Text((times > 0 ? "✓  " : !open ? "\U0001F512  " : "") + state, 12.5, times > 0 ? Ui.Green : Ui.Muted)));
        var t = Ui.Tile(sp, open ? LUi.Amber : Ui.BorderC, () => { if (open) MainView.Current.Push(new StoryPage(c, s)); });
        t.IsEnabled = open;
        t.Padding = new Thickness(14);
        return t;
    }
}

public class StoryPage : IPage, IKeyPage
{
    readonly LangCourse c;
    readonly LangStory story;
    readonly Random rnd;
    readonly StackPanel body = new() { Spacing = 10 };
    readonly HashSet<int> english = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    public string Title => story.Title;
    public Control View { get; }

    public int LinesShown { get; private set; } = 1;
    public bool InQuestions { get; private set; }
    public bool Done { get; private set; }
    public int Question { get; private set; }
    public int Right { get; private set; }
    public bool Answered { get; private set; }
    public List<string> Choices { get; private set; } = new();
    /// <summary>The tapped word and its meaning (null when nothing is tapped).</summary>
    public string Word { get; private set; }
    public string WordMeaning { get; private set; }
    public int Playing { get; private set; } = -1;
    public string Status { get; private set; } = "";
    string picked;
    int wordLine = -1;

    IPlayer player;
    LessonFile file;
    bool making;

    public StoryPage(LangCourse c, LangStory story, int seed = -1)
    {
        this.c = c; this.story = story;
        rnd = seed < 0 ? new Random() : new Random(seed);
        timer.Tick += (s, e) => Tick();
        View = Ui.Page(body, 760);
        Show();
        if (story.Lines.Count > 0) Lx.Say(c, story.Lines[0].Target);
    }

    public void NextLine()
    {
        if (InQuestions || LinesShown >= story.Lines.Count) return;
        LinesShown++;
        Lx.Say(c, story.Lines[LinesShown - 1].Target);
        Show();
    }

    public void ShowAll() { LinesShown = story.Lines.Count; Show(); }

    public void ToggleEnglish(int line) { if (!english.Remove(line)) english.Add(line); Show(); }
    public bool EnglishShown(int line) => english.Contains(line);

    /// <summary>Taps a word of a line: shows its meaning.</summary>
    public void TapWord(int line, int word)
    {
        var words = LangStory.LineWords(story.Lines[line].Target);
        WordMeaning = story.Meaning(c, words, word, out var shown);
        wordLine = line;
        Word = shown;
        if (!string.IsNullOrEmpty(shown)) Lx.Say(c, shown);
        Show();
    }

    /// <summary>"Read it all": makes the story's audio (Spanish only) and plays it, following the lines.</summary>
    public async Task ReadAll()
    {
        if (player != null) { if (player.Playing) { player.Pause(); timer.Stop(); } else { player.Play(); timer.Start(); } Show(); return; }
        if (making) return;
        making = true;
        Status = "Getting the story ready...";
        Show();
        try
        {
            var l = Lesson.BuildStory(story, c.Language, 1, false);
            file = await LessonAudio.Make(l, Lx.Target(c), Lx.English(), Lx.Rate, null, CancellationToken.None);
            ShowAll();
            player = PlatformAudio.NewPlayer();
            if (player == null || !player.Open(file.File)) { Status = "This device couldn't play the story."; player?.Dispose(); player = null; Show(); return; }
            player.Ended += () => Dispatcher.UIThread.Post(() => { StopPlayer(); Status = ""; Show(); });
            player.Play();
            timer.Start();
            Status = "";
        }
        catch (InvalidOperationException ex) { Status = ex.Message; }
        catch (Exception ex) { Status = "The story couldn't be read aloud: " + ex.Message; }
        finally { making = false; }
        Show();
    }

    /// <summary>The audio's length and line marks once Read it all made it (the checks look).</summary>
    public LessonFile Audio => file;

    void Tick()
    {
        if (player == null || file == null) return;
        int k = file.ItemAt(player.Position);
        if (k != Playing) { Playing = k; Show(); }
    }

    void StopPlayer() { timer.Stop(); try { player?.Dispose(); } catch (Exception) { } player = null; Playing = -1; }

    public void StartQuestions()
    {
        StopPlayer();
        if (story.Questions.Count == 0) { Finish(); return; }
        InQuestions = true; Question = 0; Right = 0; NewQuestion(); Show();
    }

    void NewQuestion() { Answered = false; picked = null; Choices = story.Questions[Question].Choices(rnd); }

    public StoryQuestion CurrentQuestion => InQuestions && Question < story.Questions.Count ? story.Questions[Question] : null;

    public void Answer(string choice)
    {
        var q = CurrentQuestion;
        if (q == null || Answered || Done) return;
        picked = choice; Answered = true;
        bool ok = Answers.Same(choice, q.Answer);
        if (ok) Right++;
        Services.Feel.Right(ok);
        Show();
    }

    public void Continue()
    {
        if (!InQuestions || !Answered || Done) return;
        Question++;
        if (Question >= story.Questions.Count) Finish();
        else NewQuestion();
        Show();
    }

    void Finish()
    {
        Done = true; InQuestions = false;
        Lx.Progress.FinishStory(c.Language, story.Id, Right);
        Lx.Progress.AddPoints(c.Language, 5 + Right);
        Lx.Save();
        Show();
    }

    void Show()
    {
        body.Children.Clear();
        if (Done) { Recap(); return; }
        if (InQuestions) { Questions(); return; }
        body.Children.Add(LUi.Head(story.Title, story.TitleEn + "  ·  " + LangCourse.LevelName(story.Level)));
        LUi.Warn(body, c);
        var top = new WrapPanel();
        var all = Ui.Button(player != null && player.Playing ? "Pause" : "\U0001F50A  Read it all", false, 15);
        all.Margin = new Thickness(0, 0, 8, 8);
        all.Click += async (s, e) => await ReadAll();
        top.Children.Add(all);
        if (LinesShown < story.Lines.Count)
        {
            var allText = Ui.Button("Show every line", false, 15);
            allText.Margin = new Thickness(0, 0, 8, 8);
            allText.Click += (s, e) => ShowAll();
            top.Children.Add(allText);
        }
        body.Children.Add(top);
        if (!string.IsNullOrEmpty(Status)) body.Children.Add(LUi.Wrap(Ui.Text(Status, 13.5, Ui.Muted)));
        body.Children.Add(LUi.Wrap(Ui.Text(Platform.Touch ? "Tap a word for its meaning." : "Click a word for its meaning.", 13, Ui.Dim)));
        for (int k = 0; k < LinesShown && k < story.Lines.Count; k++)
        {
            body.Children.Add(Line(k));
            if (Word != null && k == wordLine) body.Children.Add(WordCard());   // right under the line, so it is in view
        }
        if (LinesShown < story.Lines.Count)
        {
            var next = LUi.Primary("Next line", 17);
            next.HorizontalAlignment = HorizontalAlignment.Stretch;
            next.Click += (s, e) => NextLine();
            body.Children.Add(next);
        }
        else
        {
            var q = LUi.Primary(story.Questions.Count > 0 ? "Questions about the story" : "Finish", 17);
            q.HorizontalAlignment = HorizontalAlignment.Stretch;
            q.Click += (s, e) => StartQuestions();
            body.Children.Add(q);
        }
    }

    Control Line(int k)
    {
        var line = story.Lines[k];
        var sp = new StackPanel { Spacing = 4 };
        var words = new WrapPanel();
        var list = LangStory.LineWords(line.Target);
        for (int w = 0; w < list.Count; w++)
        {
            int kw = w;
            var b = new Button
            {
                Content = list[w], FontSize = 19, Padding = new Thickness(3, 2), Margin = new Thickness(0, 0, 4, 2), MinHeight = 36,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush(Ui.BorderStrong),
                Foreground = Ui.Brush(k == Playing ? LUi.AmberText : Ui.TextC), Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.Click += (s, e) => TapWord(k, kw);
            words.Children.Add(b);
        }
        var row = new DockPanel();
        var say = LUi.Speaker(() => Lx.Say(c, line.Target));
        say.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(say, Dock.Left);
        row.Children.Add(say);
        var en = Ui.Button(english.Contains(k) ? "Hide" : "English", false, 12.5);
        en.MinHeight = 36; en.Padding = new Thickness(10, 2); en.VerticalAlignment = VerticalAlignment.Top;
        en.Click += (s, e) => ToggleEnglish(k);
        DockPanel.SetDock(en, Dock.Right);
        row.Children.Add(en);
        row.Children.Add(words);
        sp.Children.Add(row);
        if (english.Contains(k)) { var t = LUi.Wrap(Ui.Text(line.English, 14, Ui.Muted)); t.Margin = new Thickness(46, 0, 0, 0); sp.Children.Add(t); }
        var card = Ui.Card(sp, k == Playing ? Ui.Surface2 : Ui.Surface, 8);
        if (k == Playing) card.BorderBrush = Ui.Brush(LUi.Amber, 140);
        return card;
    }

    Control WordCard()
    {
        var sp = new StackPanel { Spacing = 4 };
        var head = new DockPanel();
        var close = Ui.IconButton("✕", "Close");
        close.Click += (s, e) => { Word = null; WordMeaning = null; Show(); };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var say = LUi.Speaker(() => Lx.Say(c, Word));
        DockPanel.SetDock(say, Dock.Left);
        head.Children.Add(say);
        head.Children.Add(new TextBlock { Text = Word, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(LUi.AmberText), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(head);
        sp.Children.Add(LUi.Wrap(Ui.Text(WordMeaning ?? "This word isn't in the word list yet. The English of the whole line helps.", 15, WordMeaning != null ? Ui.TextC : Ui.Muted)));
        var card = Ui.Card(sp, Ui.Surface2, 12);
        card.BorderBrush = Ui.Brush(LUi.Amber, 140);
        return card;
    }

    void Questions()
    {
        var q = CurrentQuestion;
        body.Children.Add(Ui.Text(story.Title + "  ·  question " + (Question + 1) + " of " + story.Questions.Count, 13.5, Ui.Muted));
        body.Children.Add(LUi.Bar(Question / (double)story.Questions.Count, LUi.Amber, 6));
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
            var next = LUi.Primary(Question + 1 < story.Questions.Count ? "Next" : "Finish", 17);
            next.HorizontalAlignment = HorizontalAlignment.Stretch;
            next.Click += (s, e) => Continue();
            body.Children.Add(next);
        }
    }

    void Recap()
    {
        body.Children.Add(LUi.Head("Story finished!", Right + " of " + story.Questions.Count + " questions right. +" + (5 + Right) + " points."));
        body.Children.Add(LUi.GoalCard(c));
        var row = new WrapPanel();
        var again = Ui.Button("Read it again", false, 16); again.Margin = new Thickness(0, 0, 10, 10);
        again.Click += (s, e) => MainView.Current.Replace(new StoryPage(c, story));
        var back = LUi.Primary("Back to the stories", 16); back.Margin = new Thickness(0, 0, 10, 10);
        back.Click += (s, e) => MainView.Current.Back();
        row.Children.Add(back); row.Children.Add(again);
        body.Children.Add(row);
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (InQuestions && !Answered)
        {
            int k = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
            if (k >= 0 && k < Choices.Count) { Answer(Choices[k]); return true; }
        }
        if (e.Key == Key.Enter)
        {
            if (InQuestions && Answered) { Continue(); return true; }
            if (!InQuestions && !Done) { if (LinesShown < story.Lines.Count) NextLine(); else StartQuestions(); return true; }
        }
        return false;
    }

    public void Closed() { StopPlayer(); LessonAudio.StopSaying(); }
}
