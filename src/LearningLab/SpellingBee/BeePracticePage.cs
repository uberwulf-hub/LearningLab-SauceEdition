// Learning Lab - Spelling Bee's practice and results pages. Practice: the progress bar, the right / missed pills, the
// big speaker (and "Say it slowly"), Hint (blanks and the letter count, then the first letter, then the last), spell
// out loud (offline speech recognition where the device has it) or type, Check; "Not quite" once with Try again /
// Show answer; the answer with the wrong letters marked and "Spell it for me"; a calm celebration for a right answer;
// Enter goes on. Results: the score, a line for the speller, the missed words with a speaker each, practice them again.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.LearningGames;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.SpellingBee;

public enum BeeScreen { Answering, Retry, Right, Shown }

public class BeePracticePage : IPage, IKeyPage
{
    public string Title => "Spelling Bee";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    public BeeRun Run { get; }
    public BeeScreen Screen { get; private set; } = BeeScreen.Answering;
    readonly string listNames;
    readonly Dictionary<string, string> listName = new();
    readonly BeePlayer me;

    readonly Grid barHost = new() { Height = 12 };
    readonly TextBlock count = Ui.Text("", 15, Ui.Muted, true);
    readonly TextBlock goodPill = Ui.Text("", 15, Ui.Green, true), badPill = Ui.Text("", 15, Ui.Red, true);
    readonly TextBlock listLabel = Ui.Text("", 13, Ui.Muted, true);
    readonly TextBlock hintText = new() { FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(Ui.TextC), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LetterSpacing = 3 };
    readonly TextBlock hintNote = Ui.Text("", 13, Ui.Muted);
    readonly Button hintBtn;
    readonly StackPanel answerArea = new() { Spacing = 10 };
    readonly StackPanel resultArea = new() { Spacing = 10 };
    readonly TextBox box;
    readonly Button micBtn;
    readonly TextBlock heard = Ui.Text("", 17, Ui.AccentText, true);
    readonly Canvas confetti = new() { IsHitTestVisible = false, ClipToBounds = true };
    bool listening;

    public BeePracticePage(BeeRun run, string listNames)
    {
        Run = run;
        this.listNames = listNames;
        me = Bee.Me;
        foreach (var l in Bee.ListsFor(me)) listName[l.Id] = l.Name;
        run.Missing += w => { me.AddTricky(w); Bee.Save(); };
        run.KnownNow += w => { me.RemoveTricky(w); Bee.Save(); };

        var top = new DockPanel();
        count.MinWidth = 70; count.TextAlignment = TextAlignment.Right; count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(count, Dock.Right);
        top.Children.Add(count);
        barHost.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(barHost);

        var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        pills.Children.Add(Pill(goodPill)); pills.Children.Add(Pill(badPill));

        // the stage
        listLabel.HorizontalAlignment = HorizontalAlignment.Center; listLabel.TextAlignment = TextAlignment.Center; listLabel.LetterSpacing = 1;
        var speaker = new Button
        {
            Width = 132, Height = 132, CornerRadius = new CornerRadius(66), Padding = new Thickness(0),
            Background = Ui.Brush(Bee.Honey), BorderBrush = Ui.Brush(Bee.HoneyDark), BorderThickness = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "\U0001F50A", FontSize = 52 }, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(speaker, "Hear the word");
        speaker.Click += (s, e) => SayWord();
        var slow = Chip("\U0001F422  Say it slowly"); slow.Click += (s, e) => SayWord(true);
        hintBtn = Chip("\U0001F4A1  Hint"); hintBtn.Click += (s, e) => Hint();
        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        chips.Children.Add(slow); chips.Children.Add(hintBtn);
        hintNote.HorizontalAlignment = HorizontalAlignment.Center; hintNote.TextAlignment = TextAlignment.Center;
        var hint = new StackPanel { Children = { hintText, hintNote } };

        // answering
        var rec = PlatformAudio.Recognizer;
        bool canListen = rec != null && rec.Available("en-US");
        micBtn = new Button
        {
            Content = "\U0001F3A4  Spell out loud", FontSize = 18, FontWeight = FontWeight.SemiBold, MinHeight = 56, Padding = new Thickness(16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Ui.Brush(Ui.Accent), Foreground = Brushes.White, BorderBrush = Ui.Brush("#1D4FA8"), BorderThickness = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(16),
            Cursor = new Cursor(StandardCursorType.Hand), IsVisible = canListen,
        };
        micBtn.Click += (s, e) => _ = Listen();
        heard.IsVisible = false;
        heard.PropertyChanged += (s, e) => { if (e.Property == TextBlock.TextProperty) heard.IsVisible = !string.IsNullOrEmpty(heard.Text); };
        heard.HorizontalAlignment = HorizontalAlignment.Center; heard.TextAlignment = TextAlignment.Center; heard.LetterSpacing = 1;
        var or = Ui.Text("or type it", 13, Ui.Muted); or.HorizontalAlignment = HorizontalAlignment.Center; or.IsVisible = canListen;
        box = new TextBox
        {
            FontSize = 24, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, MinHeight = 56, Padding = new Thickness(12, 10),
            PlaceholderText = "type the spelling", CornerRadius = new CornerRadius(14),
            VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 80,
        };
        var check = new Button
        {
            Content = "Check", FontSize = 18, FontWeight = FontWeight.SemiBold, MinHeight = 56, Padding = new Thickness(20, 10), Margin = new Thickness(8, 0, 0, 0),
            Background = Ui.Brush("#16A34A"), Foreground = Brushes.White, BorderBrush = Ui.Brush("#15803D"), BorderThickness = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(14),
            VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
        };
        check.Click += (s, e) => Check();
        var input = new DockPanel();
        DockPanel.SetDock(check, Dock.Right);
        input.Children.Add(check); input.Children.Add(box);
        answerArea.Children.Add(micBtn);
        answerArea.Children.Add(heard);
        answerArea.Children.Add(or);
        answerArea.Children.Add(input);
        if (!canListen)
        {
            string tip = Platform.Touch
                ? "\U0001F4F1 On a phone or tablet? Tap the box above, then tap the \U0001F3A4 microphone on your keyboard and say the letters (\"C, A, B, I, N\"). Then tap Check."
                : "Type the spelling, then press Enter. (To spell out loud, Windows needs its English speech recognition: Settings > Time & language > Speech.)";
            var t = Ui.Card(Ui.Text(tip, 13.5, Ui.AccentText), Ui.AccentSoft, 12);
            answerArea.Children.Add(t);
        }
        var skip = Chip("I don't know, show me"); skip.HorizontalAlignment = HorizontalAlignment.Center; skip.Margin = new Thickness(0, 6, 0, 0);
        skip.Click += (s, e) => ShowAnswer();
        answerArea.Children.Add(skip);

        var stage = new StackPanel { Spacing = 10 };
        stage.Children.Add(listLabel);
        stage.Children.Add(speaker);
        stage.Children.Add(chips);
        stage.Children.Add(hint);
        stage.Children.Add(answerArea);
        stage.Children.Add(resultArea);
        var card = Ui.Card(stage, Ui.Surface, 20);
        var cardHost = new Panel { Children = { card, confetti } };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(top);
        body.Children.Add(pills);
        body.Children.Add(cardHost);
        View = Ui.Page(body, 640);
        Load();
    }

    static Border Pill(TextBlock t) => new() { Child = t, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1) };

    static Button Chip(string text) => new()
    {
        Content = text, FontSize = 15, MinHeight = 44, Padding = new Thickness(16, 8), Margin = new Thickness(4),
        CornerRadius = new CornerRadius(22), Background = Ui.Brush(Ui.Surface3), Foreground = Ui.Brush(Ui.TextC),
        BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
    };

    void Bar()
    {
        barHost.Children.Clear();
        double f = Run.Finished ? 1 : (double)Run.Index / Run.Words.Count;
        barHost.Children.Add(Lg.Bar(f, Bee.Honey, 12));
    }

    void Pills() { goodPill.Text = "✓ " + Run.Good; badPill.Text = "✗ " + Run.Bad; }

    void Load()
    {
        var w = Run.Current;
        Screen = BeeScreen.Answering;
        listLabel.Text = (w.ListId != null && listName.TryGetValue(w.ListId, out var n) ? n : listNames).ToUpperInvariant();
        count.Text = (Run.Index + 1) + " / " + Run.Words.Count;
        Bar(); Pills();
        hintText.Text = ""; hintNote.Text = ""; hintText.IsVisible = false; hintNote.IsVisible = false;
        hintBtn.IsVisible = true;
        heard.Text = ""; box.Text = ""; confetti.Children.Clear();
        answerArea.IsVisible = true; resultArea.IsVisible = false; resultArea.Children.Clear();
        if (me.AutoSay) Dispatcher.UIThread.Post(() => { if (MainView.Current?.Page == this && Run.Current == w) SayWord(); }, DispatcherPriority.Background);
        FocusBox();
    }

    void FocusBox() { if (!Platform.Touch) Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input); }

    // ---------------------------------------------------------------- actions (the checks call these too)
    public void SayWord(bool slow = false) => Bee.Say(Run.Current.Word, slow);

    public string HintShown => hintText.Text;

    public void Hint()
    {
        var (blanks, letters) = Run.NextHint();
        hintText.Text = blanks;
        hintNote.Text = letters + " letters" + (Run.HintLevel < 3 ? "  ·  tap Hint again for more" : "");
        hintText.IsVisible = true; hintNote.IsVisible = true;
    }

    public void Type(string text) { box.Text = text; }

    /// <summary>Checks what's in the box.</summary>
    public void Check() => Submit(box.Text);

    public void Submit(string text)
    {
        if (Screen != BeeScreen.Answering) return;
        var outcome = Run.Submit(text);
        switch (outcome)
        {
            case BeeOutcome.Nothing:
                heard.Text = "Type the spelling first (or tap \"I don't know\").";
                FocusBox();
                break;
            case BeeOutcome.Right: ShowRight(); break;
            case BeeOutcome.Retry: ShowRetry(); break;
            case BeeOutcome.Shown: ShowWord(Run.Last?.Typed); break;
        }
    }

    public void ShowAnswer()
    {
        if (Run.Answered) return;
        string typed = Screen == BeeScreen.Retry ? Run.Last?.Typed : null;
        Run.Show();
        ShowWord(typed);
    }

    public void TryAgain()
    {
        if (Screen != BeeScreen.Retry) return;
        Screen = BeeScreen.Answering;
        resultArea.IsVisible = false; answerArea.IsVisible = true;
        box.Text = ""; heard.Text = "";
        SayWord();
        FocusBox();
    }

    public void SpellItForMe() => Bee.Say(Bee.SpellItText(Run.Current), true);

    public void Next()
    {
        if (!Run.Answered) return;
        Bee.Stop();
        if (Run.Next()) Load();
        else Finish();
    }

    /// <summary>The run ends (the last word, or Stop): the results take this page's place.</summary>
    public void Finish()
    {
        Run.Stop();
        if (Run.Total > 0) { me.AddHistory(listNames, Run.Good, Run.Total); Bee.Save(); }
        if (Run.Total == 0) { MainView.Current.Back(); return; }
        MainView.Current.Replace(new BeeResultsPage(Run, listNames));
    }

    public string Feedback { get; private set; } = "";

    void ShowRetry()
    {
        Screen = BeeScreen.Retry;
        Feedback = "Not quite";
        resultArea.Children.Clear();
        var c = new StackPanel { Spacing = 8 };
        c.Children.Add(Centered(Ui.Text("Not quite \U0001F914", 26, Ui.Red, true)));
        c.Children.Add(Centered(Yours(Run.Last?.Typed)));
        c.Children.Add(Centered(Ui.Text("Listen again and give it one more try!", 14, Ui.Muted)));
        c.Children.Add(Two(Big("\U0001F501  Try again", true, TryAgain), Big("Show answer", false, ShowAnswer)));
        resultArea.Children.Add(Box(c, Ui.Red));
        answerArea.IsVisible = false; resultArea.IsVisible = true;
        Bee.Say("Not quite. Try again.");
    }

    void ShowRight()
    {
        Screen = BeeScreen.Right;
        var w = Run.Current; var v = Run.Last;
        Feedback = Run.FirstTry ? "Correct!" : "You got it!";
        resultArea.Children.Clear();
        var c = new StackPanel { Spacing = 8 };
        c.Children.Add(Centered(Ui.Text(Run.FirstTry ? "Correct! \U0001F389" : "You got it! \U0001F44D", 28, Ui.Green, true)));
        c.Children.Add(Centered(WordText(v?.Matched ?? w.Word, null)));
        if (v != null && v.Close) c.Children.Add(Centered(Ui.Text("✏ Close enough, but watch the spaces, dashes and apostrophes: " + v.Matched, 14, Ui.Yellow)));
        Notes(c, w);
        if (!Run.FirstTry) c.Children.Add(Centered(Ui.Text("It took two tries, so it stays in your ⭐ Tricky Words for now.", 13, Ui.Muted)));
        c.Children.Add(Big(Run.IsLast ? "See my score \U0001F3C1" : "Next word  ➜", true, Next));
        resultArea.Children.Add(Box(c, Ui.Green));
        answerArea.IsVisible = false; resultArea.IsVisible = true; hintBtn.IsVisible = false;
        Pills();
        global::LearningGames.Sound.Correct();
        Celebrate();
    }

    void ShowWord(string typed)
    {
        Screen = BeeScreen.Shown;
        var w = Run.Current;
        Feedback = "Here's how to spell it";
        resultArea.Children.Clear();
        var c = new StackPanel { Spacing = 8 };
        c.Children.Add(Centered(Ui.Text("Here's how to spell it", 24, Ui.TextC, true)));
        c.Children.Add(Centered(WordText(w.Word, string.IsNullOrEmpty(typed) ? null : BeeCheck.Wrong(w.Word, typed))));
        if (!string.IsNullOrEmpty(typed)) c.Children.Add(Centered(Yours(typed)));
        Notes(c, w);
        c.Children.Add(Two(Big("\U0001F50A  Spell it for me", false, SpellItForMe), Big(Run.IsLast ? "Score \U0001F3C1" : "Next  ➜", true, Next)));
        c.Children.Add(Centered(Ui.Text("It's saved to your ⭐ Tricky Words.", 13, Ui.Muted)));
        resultArea.Children.Add(Box(c, Ui.Red));
        answerArea.IsVisible = false; resultArea.IsVisible = true; hintBtn.IsVisible = false;
        Pills();
        Bee.Say("The word " + w.Word + " is spelled: " + BeeCheck.SpellOut(w.Word) + ".");
    }

    static void Notes(StackPanel c, BeeWord w)
    {
        if (w.Alts.Count > 1) c.Children.Add(Centered(Ui.Text("Also right: " + string.Join(", ", w.Alts.Skip(1)) + " (another spelling)", 14, Ui.Muted)));
        if (w.Word.Any(char.IsUpper)) c.Children.Add(Centered(Ui.Text("Capital letter: it's a name or a place.", 14, Ui.Muted)));
    }

    static Control Centered(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; return t; }

    static TextBlock Yours(string typed)
    {
        var t = new TextBlock { FontSize = 15, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new Run("You spelled: "));
        t.Inlines.Add(new Run(typed ?? "") { FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(Ui.TextC) });
        return t;
    }

    /// <summary>The word, big, with the wrong letters red and underlined.</summary>
    static TextBlock WordText(string word, bool[] wrong)
    {
        var t = new TextBlock { FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, LetterSpacing = 2 };
        for (int i = 0; i < word.Length; i++)
        {
            var r = new Run(word[i].ToString());
            if (wrong != null && wrong[i]) { r.Foreground = Ui.Brush(Ui.Red); r.TextDecorations = TextDecorations.Underline; }
            t.Inlines.Add(r);
        }
        return t;
    }

    static Border Box(Control c, string color)
    {
        var b = Ui.Card(c, Ui.Surface2, 16);
        b.BorderBrush = Ui.Brush(color, 140); b.BorderThickness = new Thickness(2); b.CornerRadius = new CornerRadius(16);
        return b;
    }

    static Button Big(string text, bool main, Action act)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Ui.Brush(main ? "#1A1408" : Ui.TextC) },
            MinHeight = 54, Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = Ui.Brush(main ? Bee.Honey : Ui.Surface3), BorderBrush = Ui.Brush(main ? Bee.HoneyDark : Ui.BorderStrong), BorderThickness = new Thickness(0, 0, 0, 3),
            Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(0, 6, 0, 0),
        };
        b.Click += (s, e) => act();
        return b;
    }

    static Control Two(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        Grid.SetColumn(b, 2);
        g.Children.Add(a); g.Children.Add(b);
        return g;
    }

    /// <summary>A few bees and stars drift up and fade (about a second); nothing in the checks, where no time passes.</summary>
    void Celebrate()
    {
        confetti.Children.Clear();
        if (Data.Testing) return;   // (no time passes in the checks: the pictures stay clear)
        var items = new List<(TextBlock T, double X, double Y, double Dx)>();
        string[] em = { "\U0001F41D", "⭐", "✨", "\U0001F33C" };
        double w = confetti.Bounds.Width > 0 ? confetti.Bounds.Width : 500, h = confetti.Bounds.Height > 0 ? confetti.Bounds.Height : 500;
        for (int i = 0; i < 10; i++)
        {
            var t = new TextBlock { Text = em[i % em.Length], FontSize = 22 };
            double x = w * (0.1 + 0.8 * Lg.Rand.NextDouble()), y = h * (0.55 + 0.35 * Lg.Rand.NextDouble());
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            confetti.Children.Add(t);
            items.Add((t, x, y, (Lg.Rand.NextDouble() - 0.5) * 40));
        }
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (s, e) =>
        {
            double k = (DateTime.UtcNow - start).TotalSeconds / 1.2;
            if (k >= 1) { timer.Stop(); confetti.Children.Clear(); return; }
            foreach (var it in items)
            {
                Canvas.SetTop(it.T, it.Y - k * 160);
                Canvas.SetLeft(it.T, it.X + it.Dx * k);
                it.T.Opacity = 1 - k;
            }
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- spelling out loud
    public string Heard => heard.Text;

    public async Task Listen()
    {
        var rec = PlatformAudio.Recognizer;
        if (rec == null || listening || Screen != BeeScreen.Answering) return;
        if (rec.NeedsPermission)
        {
            var ask = new TaskCompletionSource<bool>();
            MainView.Current.Message("Use the microphone?", "To spell out loud, Learning Lab listens to the letters you say. It all stays on this device: nothing is recorded or sent anywhere.",
                ("Allow the microphone", () => ask.TrySetResult(true)), ("Not now", () => ask.TrySetResult(false)));
            if (!await ask.Task) return;
            if (!await rec.AskPermission()) { heard.Text = "The microphone isn't allowed, so type it instead."; return; }
        }
        Bee.Stop();
        listening = true;
        micBtn.Content = "⏺  Listening... say the letters";
        micBtn.Background = Ui.Brush(Ui.Red);
        heard.Text = "Say the letters...";
        string said = null;
        try { said = await rec.ListenLetters("en-US", 10); } catch (Exception) { }
        listening = false;
        micBtn.Content = "\U0001F3A4  Spell out loud";
        micBtn.Background = Ui.Brush(Ui.Accent);
        if (Screen != BeeScreen.Answering) return;
        if (string.IsNullOrWhiteSpace(said)) { heard.Text = "I didn't hear anything. Try again, or type it."; return; }
        string spelled = BeeCheck.ParseSpoken(said);
        heard.Text = "I heard: " + string.Join(" ", spelled.ToUpperInvariant().ToCharArray());
        box.Text = spelled;
        Submit(spelled);
    }

    // ---------------------------------------------------------------- keys and Back
    public bool OnKey(KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return false;
        if (Screen == BeeScreen.Right || Screen == BeeScreen.Shown) { Next(); return true; }
        if (Screen == BeeScreen.Retry) { TryAgain(); return true; }
        Check();
        return true;
    }

    public bool OnBack()
    {
        if (Run.Finished || Run.Total == 0 && !Run.Answered) return false;
        MainView.Current.Message("Stop practicing?", "You'll see your score so far.", ("Keep going", null), ("Stop", Finish));
        return true;
    }

    public void Closed() { Bee.Stop(); }
}

public class BeeResultsPage : IPage
{
    public string Title => "Spelling Bee";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    public BeeRun Run { get; }
    readonly string listNames;

    public BeeResultsPage(BeeRun run, string listNames)
    {
        Run = run; this.listNames = listNames;
        var (emoji, message) = BeeCheck.Cheer(run.Good, run.Total);
        Message = message;
        var body = new StackPanel { Spacing = 14 };
        var top = new StackPanel { Spacing = 4 };
        top.Children.Add(Center(Ui.Text(emoji, 64)));
        top.Children.Add(Center(Ui.Text(run.Good + " / " + run.Total, 48, Ui.TextC, true)));
        top.Children.Add(Center(Ui.Text(message, 16, Ui.Muted)));
        body.Children.Add(Ui.Card(top, Ui.Surface, 20));
        if (run.Missed.Count > 0)
        {
            var m = new StackPanel { Spacing = 8 };
            m.Children.Add(Ui.Text("Words to practice", 18, Ui.TextC, true));
            foreach (var w in run.Missed)
            {
                var row = new DockPanel { MinHeight = 48 };
                var say = Ui.IconButton("\U0001F50A", "Hear it spelled");
                var word = w;
                say.Click += (s, e) => Bee.Say(word.Word + ". " + BeeCheck.SpellOut(word.Word) + ".");
                DockPanel.SetDock(say, Dock.Right);
                row.Children.Add(say);
                var t = Ui.Text(w.Word, 20, Ui.TextC, true); t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                var b = new Border { Child = row, Background = Ui.Brush(Ui.Red, 30), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 4, 6, 4) };
                m.Children.Add(b);
            }
            body.Children.Add(Ui.Card(m, Ui.Surface, 18));
            var again = Big("\U0001F501  Practice these again", true);
            again.Click += (s, e) => PracticeAgain();
            body.Children.Add(again);
        }
        var home = Big("\U0001F3E0  Back to start", false);
        home.Click += (s, e) => MainView.Current.BackTo<BeeHomePage>();
        body.Children.Add(home);
        View = Ui.Page(body, 640);
    }

    public string Message { get; }

    public void PracticeAgain()
    {
        if (Run.Missed.Count == 0) return;
        var words = BeeRun.Pick(Run.Missed, true, 0, Lg.Rand);
        MainView.Current.Replace(new BeePracticePage(new BeeRun(words), listNames.StartsWith("Practicing again: ") ? listNames : "Practicing again: " + listNames));
    }

    static Control Center(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; return t; }

    static Button Big(string text, bool main) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(main ? "#1A1408" : Ui.TextC), HorizontalAlignment = HorizontalAlignment.Center },
        MinHeight = 58, Padding = new Thickness(16, 12), CornerRadius = new CornerRadius(16),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = Ui.Brush(main ? Bee.Honey : Ui.Surface3), BorderBrush = Ui.Brush(main ? Bee.HoneyDark : Ui.BorderStrong), BorderThickness = new Thickness(0, 0, 0, 4),
        Cursor = new Cursor(StandardCursorType.Hand),
    };
}
