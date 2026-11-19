// Learning Lab - Languages: one lesson, step by step. Meet the new phrases (each can be heard), the conversation line by
// line (heard as it appears, the English on a tap), the exercises one at a time (all eight kinds, kind feedback, the
// right answer said aloud; the ones answered wrong come back once at the end), the tip, and a recap with the points.
// LessonRun (engine) keeps the score; Finish marks and grades the phrases and saves.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class LessonPage : IPage, IKeyPage
{
    public enum Step { Intro, Words, Dialogue, Exercise, Tip, Recap }

    readonly LangCourse c;
    readonly LangContent content;
    readonly LangLesson lesson;
    readonly List<LangItem> newItems;
    readonly Random rnd;
    readonly StackPanel body = new() { Spacing = 12 };
    readonly HashSet<int> englishShown = new();

    public string Title => lesson.Title;
    public Control View { get; }
    public Step Now { get; private set; } = Step.Intro;
    public LessonRun Run { get; }
    public int LinesShown { get; private set; }
    /// <summary>The last feedback sentence (what the person sees after answering).</summary>
    public string Feedback { get; private set; }
    /// <summary>What was said aloud last (the checks read it).</summary>
    public string LastSaid { get; private set; }
    public bool Finished { get; private set; }

    // the current exercise's state
    List<string> choices = new();
    string picked;
    List<string> tiles = new();
    readonly List<int> chosenTiles = new();
    string typed;
    AnswerResult typedResult;
    readonly HashSet<string> matchedEs = new();
    string matchEs, matchEn;
    int matchMistakes;
    List<string> matchEnglish = new();
    bool sayRevealed;
    List<SaidWord> heard;
    string sayNote;
    TextBox typeBox;

    public LessonPage(LangCourse c, LangContent content, LangLesson lesson, int seed = -1)
    {
        this.c = c; this.content = content; this.lesson = lesson;
        rnd = seed < 0 ? new Random() : new Random(seed);
        newItems = LangContent.NewItems(c, lesson);
        Run = new LessonRun(lesson);
        View = Ui.Page(body, 760);
        Show();
    }

    LangExercise Ex => Run.Current;

    void Say(string es) { if (string.IsNullOrEmpty(es)) return; LastSaid = es; Lx.Say(c, es); }

    // ---------------------------------------------------------------- moving through the steps
    public void Start() { if (Now == Step.Intro) { Now = Step.Words; Run.MeetNew(c, Lx.Progress); Lx.Save(); Show(); } }

    public void ToDialogue() { if (Now == Step.Words) { Now = lesson.Dialogue.Count > 0 ? Step.Dialogue : Step.Exercise; LinesShown = 0; if (Now == Step.Dialogue) NextLine(); else NewExercise(); Show(); } }

    public void NextLine()
    {
        if (Now != Step.Dialogue) return;
        if (LinesShown < lesson.Dialogue.Count) { LinesShown++; Say(lesson.Dialogue[LinesShown - 1].Target); }
        Show();
    }

    public void ToggleEnglish(int line) { if (!englishShown.Remove(line)) englishShown.Add(line); Show(); }
    public bool EnglishShown(int line) => englishShown.Contains(line);

    public void ToExercises()
    {
        if (Now != Step.Dialogue) return;
        Now = Run.Done ? Step.Tip : Step.Exercise;
        NewExercise();
        Show();
    }

    /// <summary>After the feedback: the next exercise, then the tip, then the recap.</summary>
    public void Continue()
    {
        if (Now == Step.Exercise)
        {
            if (!Run.Answered) return;
            Run.Next();
            if (Run.Done) Now = lesson.Tip != null ? Step.Tip : Step.Recap;
            else NewExercise();
            if (Now == Step.Recap) Finish();
            Show();
        }
        else if (Now == Step.Tip) { Now = Step.Recap; Finish(); Show(); }
    }

    void Finish()
    {
        if (Finished) return;
        Finished = true;
        Services.Feel.Done();
        Run.Finish(c, Lx.Progress);
        Lx.Save();
    }

    void NewExercise()
    {
        picked = null; typed = null; typedResult = null; Feedback = null;
        chosenTiles.Clear(); matchedEs.Clear(); matchEs = matchEn = null; matchMistakes = 0;
        sayRevealed = false; heard = null; sayNote = null;
        var e = Ex;
        if (e == null) return;
        choices = e.Kind is "choose" or "listen" or "gap" or "reply" ? e.Choices(rnd) : new List<string>();
        tiles = e.Kind == "build" ? e.Tiles(rnd) : new List<string>();
        matchEnglish = e.Kind == "match" ? e.Pairs.Select(p => p[1]).OrderBy(_ => rnd.Next()).ToList() : new List<string>();
        if (e.Kind == "listen") Say(e.Target);
    }

    // ---------------------------------------------------------------- answering (the checks call these too)
    public IReadOnlyList<string> Choices => choices;
    public IReadOnlyList<string> Tiles => tiles;

    void Answered(bool right, string feedback)
    {
        Record(right);
        Feedback = feedback;
        Sound(right);
        Say(Ex.SayAfter);
        Show();
    }

    static void Sound(bool right) => Services.Feel.Right(right);

    /// <summary>choose, listen, gap, reply: picks a choice.</summary>
    public void Pick(string choice)
    {
        var e = Ex;
        if (e == null || Run.Answered || !(e.Kind is "choose" or "listen" or "gap" or "reply")) return;
        picked = choice;
        bool ok = Answers.Same(choice, e.Answer);
        Answered(ok, ok ? Praise() : "Not quite. The answer is: " + (e.Kind == "gap" ? e.Filled : e.Answer));
    }

    /// <summary>build: taps a tile in the pool (by its place) or, when it is already chosen, takes it back.</summary>
    public void TapTile(int index)
    {
        if (Ex == null || Run.Answered || index < 0 || index >= tiles.Count) return;
        if (!chosenTiles.Remove(index)) chosenTiles.Add(index);
        Show();
    }

    /// <summary>build: taps the first unused tile with that word.</summary>
    public bool TapWord(string word)
    {
        for (int k = 0; k < tiles.Count; k++) if (tiles[k] == word && !chosenTiles.Contains(k)) { TapTile(k); return true; }
        return false;
    }

    public IReadOnlyList<string> Built => chosenTiles.Select(k => tiles[k]).ToList();

    /// <summary>build and type: checks what was put together or typed.</summary>
    public void Check()
    {
        var e = Ex;
        if (e == null || Run.Answered) return;
        if (e.Kind == "build")
        {
            if (chosenTiles.Count == 0) return;
            bool ok = Answers.CheckBuild(Built, e.Target);
            Answered(ok, ok ? Praise() : "Not quite. It goes: " + e.Target);
        }
        else if (e.Kind == "type") Type(typeBox?.Text ?? typed ?? "");
    }

    /// <summary>type: checks a typed answer.</summary>
    public void Type(string text)
    {
        var e = Ex;
        if (e == null || Run.Answered || e.Kind != "type" || string.IsNullOrWhiteSpace(text)) return;
        typed = text;
        typedResult = Answers.CheckTyped(text, e.Target, e.Accept);
        Answered(typedResult.Right, typedResult.Right ? Praise() + (typedResult.Note != null ? " " + typedResult.Note : "") : "Not quite. The answer is: " + e.Target);
    }

    /// <summary>match: picks a Spanish or English side; two picked are checked as a pair.</summary>
    public void MatchPick(string es, string en)
    {
        var e = Ex;
        if (e == null || Run.Answered || e.Kind != "match") return;
        if (es != null) matchEs = es;
        if (en != null) matchEn = en;
        sayNote = null;
        if (matchEs != null && matchEn != null)
        {
            bool ok = e.Pairs.Any(p => p[0] == matchEs && p[1] == matchEn);
            if (ok) { matchedEs.Add(matchEs); Say(matchEs); }
            else { matchMistakes++; sayNote = "Not a pair. Try again."; }
            matchEs = matchEn = null;
            if (matchedEs.Count == e.Pairs.Count)
            {
                bool right = matchMistakes == 0;
                Record(right);
                Feedback = right ? Praise() + " All pairs matched." : "All matched, with " + matchMistakes + (matchMistakes == 1 ? " slip" : " slips") + (Run.Redo ? ". Keep practising these." : ". It comes back at the end.");
                Sound(right);
            }
        }
        Show();
    }

    public bool MatchDone(string es) => matchedEs.Contains(es);

    /// <summary>say: hears the answer (when nothing listens, to compare by ear).</summary>
    public void Reveal() { if (Ex?.Kind != "say") return; sayRevealed = true; Say(Ex.Target); Show(); }

    /// <summary>say: marks yourself after hearing it.</summary>
    public void SelfMark(bool right)
    {
        if (Ex?.Kind != "say" || Run.Answered) return;
        sayRevealed = true;
        Record(right);
        Feedback = right ? Praise() : "That's fine: say it a few more times." + (Run.Redo ? "" : " It comes back at the end.");
        Sound(right);
        Show();
    }

    public bool CanListen => PlatformAudio.Recognizer != null && PlatformAudio.Recognizer.Available(c.Voice ?? c.Language);

    /// <summary>say: listens with the device's own speech recognition and compares word by word.</summary>
    public async Task Listen()
    {
        var e = Ex;
        var r = PlatformAudio.Recognizer;
        if (e?.Kind != "say" || Run.Answered || r == null) return;
        if (r.NeedsPermission)
        {
            var ask = new TaskCompletionSource<bool>();
            MainView.Current.Message("Use the microphone?",
                "To check what you say, Learning Lab listens through the microphone. It is recognised on this " + (Platform.Touch ? "phone" : "computer") + " only: nothing is recorded or sent anywhere. " + (Platform.Touch ? "Android asks you next." : ""),
                ("Allow", () => ask.TrySetResult(true)), ("Not now", () => ask.TrySetResult(false)));
            if (!await ask.Task || !await r.AskPermission()) { sayNote = "No microphone: say it out loud, then listen and mark yourself."; Show(); return; }
        }
        sayNote = "Listening... say it now.";
        Show();
        var others = lesson.Exercises.Where(x => x != e).SelectMany(x => x.SpanishTexts()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(6).ToList();
        string text = null;
        try { text = await r.Listen(c.Voice ?? c.Language, 6, new[] { e.Target }.Concat(others).ToList()); }
        catch (Exception) { }
        if (Ex != e || Run.Answered) return;
        if (string.IsNullOrWhiteSpace(text)) { sayNote = "I didn't catch that. Try again, or say it and mark yourself."; Show(); return; }
        heard = Answers.CheckSaid(text, e.Target);
        bool ok = Answers.SaidRight(heard);
        sayNote = "I heard: \"" + text + "\"";
        Record(ok);
        Feedback = ok ? Praise() : "Nearly. Listen and try the words in amber again.";
        Sound(ok);
        Say(e.Target);
        Show();
    }

    string Praise() => new[] { "Right!", "Yes, that's it!", "Well done!", "Exactly!", "Nice!" }[rnd.Next(5)];

    // ---------------------------------------------------------------- drawing
    void Show()
    {
        body.Children.Clear();
        int total = 2 + lesson.Exercises.Count + 1;
        double done = Now switch { Step.Intro => 0, Step.Words => 1, Step.Dialogue => 2, Step.Exercise => 2 + Math.Min(Run.Index, lesson.Exercises.Count), Step.Tip => total - 1, _ => total };
        body.Children.Add(LUi.Bar(done / total, LUi.Amber, 6));
        switch (Now)
        {
            case Step.Intro: Intro(); break;
            case Step.Words: Words(); break;
            case Step.Dialogue: Dialogue(); break;
            case Step.Exercise: Exercise(); break;
            case Step.Tip: Tip(); break;
            default: Recap(); break;
        }
    }

    Button Next(string text, Action act)
    {
        var b = LUi.Primary(text, 17);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.Click += (s, e) => act();
        return b;
    }

    void Intro()
    {
        var u = c.Unit(lesson.Unit);
        body.Children.Add(Ui.Text("Unit " + u.Number + ": " + u.Title + "  ·  lesson " + lesson.Number, 13.5, Ui.Muted));
        body.Children.Add(LUi.Head(lesson.Title, lesson.Goal));
        int best = Lx.Progress.LessonBest(c.Language, lesson.Id);
        string info = newItems.Count + " new phrases  ·  " + lesson.Exercises.Count + " exercises  ·  about " + (lesson.Minutes > 0 ? lesson.Minutes : 10) + " minutes" + (Lx.Progress.LessonDone(c.Language, lesson.Id) ? "  ·  best so far " + best + "%" : "");
        body.Children.Add(LUi.Wrap(Ui.Text(info, 14, Ui.Muted)));
        LUi.Warn(body, c);
        body.Children.Add(Next("Start", Start));
    }

    void Words()
    {
        body.Children.Add(LUi.Head("New phrases", "Tap the speaker to hear each one. Say them out loud."));
        foreach (var it in newItems)
        {
            var d = new DockPanel();
            var say = LUi.Speaker(() => Say(it.Target));
            say.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(say, Dock.Left);
            d.Children.Add(say);
            var sp = new StackPanel { Spacing = 2, Margin = new Thickness(6, 2, 0, 0) };
            sp.Children.Add(LUi.Wrap(Ui.Text(it.Target, 18, LUi.AmberText, true)));
            sp.Children.Add(LUi.Wrap(Ui.Text(it.English, 14, Ui.TextC)));
            if (!string.IsNullOrEmpty(it.Note)) sp.Children.Add(LUi.Wrap(Ui.Text(it.Note, 12.5, Ui.Muted)));
            d.Children.Add(sp);
            body.Children.Add(Ui.Card(d, Ui.Surface, 10));
        }
        body.Children.Add(Next(lesson.Dialogue.Count > 0 ? "Hear them in a conversation" : "Practise", ToDialogue));
    }

    void Dialogue()
    {
        body.Children.Add(LUi.Head("The conversation", "Each line is read out as it appears. Tap \"English\" to see what it means."));
        var people = lesson.Dialogue.Select(d => d.Who).Distinct().ToList();
        for (int k = 0; k < LinesShown; k++)
        {
            var line = lesson.Dialogue[k];
            bool right = people.IndexOf(line.Who) % 2 == 1;
            var sp = new StackPanel { Spacing = 4 };
            sp.Children.Add(Ui.Text(line.Who, 12.5, right ? Ui.AccentText : LUi.AmberText, true));
            var row = new DockPanel();
            var say = LUi.Speaker(() => Say(line.Target));
            DockPanel.SetDock(say, Dock.Right);
            row.Children.Add(say);
            row.Children.Add(LUi.Wrap(Ui.Text(line.Target, 17.5, Ui.TextC, true)));
            sp.Children.Add(row);
            int kk = k;
            if (englishShown.Contains(k)) sp.Children.Add(LUi.Wrap(Ui.Text(line.English, 14, Ui.Muted)));
            var en = Ui.Button(englishShown.Contains(k) ? "Hide English" : "English", false, 13);
            en.MinHeight = 36; en.Padding = new Thickness(12, 4); en.HorizontalAlignment = HorizontalAlignment.Left;
            en.Click += (s, e) => ToggleEnglish(kk);
            sp.Children.Add(en);
            var card = Ui.Card(sp, right ? Ui.Surface2 : Ui.Surface, 12);
            card.Margin = new Thickness(right ? 40 : 0, 0, right ? 0 : 40, 0);
            body.Children.Add(card);
        }
        if (LinesShown < lesson.Dialogue.Count) body.Children.Add(Next("Next line", NextLine));
        else body.Children.Add(Next("Practise", ToExercises));
    }

    void Exercise()
    {
        var e = Ex;
        if (e == null) return;
        string head = Run.Redo ? "Once more: " + (Run.Index - lesson.Exercises.Count + 1) + " of " + (Run.Count - lesson.Exercises.Count) : "Exercise " + (Run.Index + 1) + " of " + lesson.Exercises.Count;
        body.Children.Add(Ui.Text(head, 13.5, Ui.Muted));
        switch (e.Kind)
        {
            case "choose": Ask(e.Prompt); ChoiceList(false); break;
            case "listen":
                Ask("What does this mean?");
                var hear = Ui.Button("\U0001F50A  Hear it again", false, 17);
                hear.HorizontalAlignment = HorizontalAlignment.Left;
                hear.Click += (s, ev) => Say(e.Target);
                body.Children.Add(hear);
                if (Run.Answered || Lx.Target(c) == null) body.Children.Add(LUi.Wrap(Ui.Text(e.Target, 20, LUi.AmberText, true)));
                ChoiceList(false); break;
            case "gap":
                Ask("Fill the gap");
                body.Children.Add(LUi.Wrap(Ui.Text(Run.Answered ? e.Filled : e.Target.Replace("___", "_____"), 22, Ui.TextC, true)));
                if (!string.IsNullOrEmpty(e.English)) body.Children.Add(LUi.Wrap(Ui.Text(e.English, 14, Ui.Muted)));
                ChoiceList(true); break;
            case "reply":
                Ask("What's a good answer?");
                var row = new DockPanel();
                var sp = LUi.Speaker(() => Say(e.Target));
                DockPanel.SetDock(sp, Dock.Left);
                row.Children.Add(sp);
                row.Children.Add(LUi.Wrap(Ui.Text(e.Target, 20, LUi.AmberText, true)));
                body.Children.Add(Ui.Card(row, Ui.Surface, 12));
                ChoiceList(false); break;
            case "build": Build(e); break;
            case "type": TypeIt(e); break;
            case "match": Match(e); break;
            case "say": SayIt(e); break;
        }
        if (Run.Answered)
        {
            bool ok = lastRight;
            var fb = new StackPanel { Spacing = 6 };
            fb.Children.Add(LUi.Wrap(Ui.Text(Feedback ?? "", 16, ok ? Ui.Green : LUi.AmberText, true)));
            if (!ok && e.SayAfter != null && e.Kind is "listen" or "reply") fb.Children.Add(LUi.Wrap(Ui.Text(e.Kind == "listen" ? e.Target + " = " + e.Answer : e.Answer, 14, Ui.TextC)));
            var card = Ui.Card(fb, Ui.Surface2, 12);
            card.BorderBrush = Ui.Brush(ok ? Ui.Green : LUi.Amber, 140);
            body.Children.Add(card);
            body.Children.Add(Next("Continue", Continue));
        }
    }

    bool lastRight;
    void Record(bool right) { lastRight = right; Run.Answer(right); }

    void Ask(string text) { body.Children.Add(LUi.Wrap(Ui.Text(text, 20, Ui.TextC, true))); }

    void ChoiceList(bool wrapRow)
    {
        var e = Ex;
        Panel list = wrapRow ? new WrapPanel() : new StackPanel { Spacing = 8 };
        for (int k = 0; k < choices.Count; k++)
        {
            string ch = choices[k];
            var b = wrapRow ? Ui.Button(ch, false, 17) : LUi.Choice((Platform.Touch ? "" : (k + 1) + "   ") + ch, 16.5);
            if (wrapRow) b.Margin = new Thickness(0, 0, 8, 8);
            if (Run.Answered)
            {
                if (Answers.Same(ch, e.Answer)) LUi.Mark(b, true);
                else if (ch == picked) LUi.Mark(b, false);
            }
            b.Click += (s, ev) => Pick(ch);
            list.Children.Add(b);
        }
        body.Children.Add(list);
    }

    void Build(LangExercise e)
    {
        Ask("Put it in order");
        body.Children.Add(LUi.Wrap(Ui.Text(e.English, 17, Ui.Muted)));
        var line = new WrapPanel { MinHeight = 52 };
        foreach (int k in chosenTiles.ToList())
        {
            var b = Ui.Button(tiles[k], false, 17);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Background = Ui.Brush(LUi.Amber, 50); b.BorderBrush = Ui.Brush(LUi.Amber);
            int kk = k;
            b.Click += (s, ev) => TapTile(kk);
            line.Children.Add(b);
        }
        var holder = new Panel { Children = { line } };
        if (chosenTiles.Count == 0) holder.Children.Add(new TextBlock { Text = "Tap the words below", FontSize = 14, Foreground = Ui.Brush(Ui.Dim), VerticalAlignment = VerticalAlignment.Center });
        var area = Ui.Card(holder, Ui.Surface, 10);
        area.MinHeight = 64;
        body.Children.Add(area);
        var pool = new WrapPanel();
        for (int k = 0; k < tiles.Count; k++)
        {
            var b = Ui.Button(tiles[k], false, 17);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.IsEnabled = !chosenTiles.Contains(k) && !Run.Answered;
            if (chosenTiles.Contains(k)) b.Opacity = 0.25;
            int kk = k;
            b.Click += (s, ev) => TapTile(kk);
            pool.Children.Add(b);
        }
        body.Children.Add(pool);
        if (Run.Answered) body.Children.Add(LUi.Wrap(Ui.Text(e.Target, 18, LUi.AmberText, true)));
        else
        {
            var check = Next("Check", Check);
            check.IsEnabled = chosenTiles.Count > 0;
            body.Children.Add(check);
        }
    }

    void TypeIt(LangExercise e)
    {
        Ask("Write it in " + c.Name);
        body.Children.Add(LUi.Wrap(Ui.Text(e.English, 20, LUi.AmberText, true)));
        typeBox = new TextBox { FontSize = 18, PlaceholderText = "Type it in " + c.Name, Text = typed ?? "", IsEnabled = !Run.Answered, MinHeight = 44 };
        typeBox.KeyDown += (s, ev) => { if (ev.Key == Key.Enter) { ev.Handled = true; Type(typeBox.Text); } };
        body.Children.Add(typeBox);
        if (!Run.Answered)
        {
            var keys = new WrapPanel();
            foreach (var ch in new[] { "á", "é", "í", "ó", "ú", "ñ", "ü", "¿", "¡" })
            {
                var b = Ui.Button(ch, false, 16);
                b.MinWidth = 44; b.Padding = new Thickness(8, 4); b.Margin = new Thickness(0, 0, 6, 6);
                b.Click += (s, ev) => { int at = typeBox.CaretIndex; typeBox.Text = (typeBox.Text ?? "").Insert(Math.Min(at, (typeBox.Text ?? "").Length), ch); typeBox.CaretIndex = at + 1; typeBox.Focus(); };
                keys.Children.Add(b);
            }
            body.Children.Add(keys);
            body.Children.Add(Next("Check", Check));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => typeBox?.Focus());
        }
        else if (typedResult != null && !typedResult.Right) body.Children.Add(LUi.Wrap(Ui.Text("You wrote: " + typed, 14, Ui.Muted)));
    }

    void Match(LangExercise e)
    {
        Ask("Match the pairs");
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        var left = new StackPanel { Spacing = 8 };
        var right = new StackPanel { Spacing = 8 };
        Grid.SetColumn(right, 2);
        g.Children.Add(left); g.Children.Add(right);
        var matchedEn = new HashSet<string>(e.Pairs.Where(p => matchedEs.Contains(p[0])).Select(p => p[1]));
        foreach (var p in e.Pairs)
        {
            string es = p[0];
            if (matchedEs.Contains(es)) { left.Children.Add(Matched(es)); continue; }
            var b = LUi.Choice(es, 15.5);
            if (matchEs == es) LUi.Selected(b, true);
            b.Click += (s, ev) => MatchPick(es, null);
            left.Children.Add(b);
        }
        foreach (var en in matchEnglish)
        {
            string x = en;
            if (matchedEn.Contains(en)) { right.Children.Add(Matched(en)); continue; }
            var b = LUi.Choice(en, 15.5);
            if (matchEn == en) LUi.Selected(b, true);
            b.Click += (s, ev) => MatchPick(null, x);
            right.Children.Add(b);
        }
        body.Children.Add(g);
        if (sayNote != null && !Run.Answered) body.Children.Add(Ui.Text(sayNote, 14, LUi.AmberText));
    }

    /// <summary>A matched side: green, no longer a button.</summary>
    static Control Matched(string text) => new Border
    {
        MinHeight = 48, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 10), BorderThickness = new Thickness(1),
        Background = Ui.Brush(Ui.Green, 40), BorderBrush = Ui.Brush(Ui.Green),
        Child = new TextBlock { Text = "\u2713 " + text, FontSize = 15.5, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(Ui.TextC), VerticalAlignment = VerticalAlignment.Center },
    };

    void SayIt(LangExercise e)
    {
        Ask("Say it in " + c.Name);
        body.Children.Add(LUi.Wrap(Ui.Text(e.English.StartsWith("Say", StringComparison.OrdinalIgnoreCase) ? e.English : "Say: " + e.English, 19, LUi.AmberText, true)));
        if (heard != null)
        {
            var w = new WrapPanel();
            foreach (var x in heard)
            {
                var t = Ui.Text(x.Word, 20, x.Heard ? Ui.Green : LUi.AmberText, true);
                t.Margin = new Thickness(0, 0, 8, 4);
                w.Children.Add(t);
            }
            body.Children.Add(Ui.Card(w, Ui.Surface, 12));
        }
        else if (sayRevealed) body.Children.Add(LUi.Wrap(Ui.Text(e.Target, 20, Ui.TextC, true)));
        if (sayNote != null) body.Children.Add(LUi.Wrap(Ui.Text(sayNote, 14, Ui.Muted)));
        if (Run.Answered) return;
        var acts = new WrapPanel();
        void Add(Button b) { b.Margin = new Thickness(0, 0, 8, 8); acts.Children.Add(b); }
        if (CanListen)
        {
            var mic = LUi.Primary("\U0001F3A4  Tap and speak", 16);
            mic.Click += async (s, ev) => await Listen();
            Add(mic);
        }
        if (!sayRevealed)
        {
            body.Children.Add(LUi.Wrap(Ui.Text(CanListen ? "Or say it out loud, then listen." : "Say it out loud, then listen.", 14, Ui.Muted)));
            var hear = Ui.Button("\U0001F50A  Now listen", !CanListen, 16);
            hear.Click += (s, ev) => Reveal();
            Add(hear);
        }
        else
        {
            var again = Ui.Button("\U0001F50A  Again", false, 16);
            again.Click += (s, ev) => Say(e.Target);
            Add(again);
            var yes = LUi.Primary("I said it right", 16);
            yes.Click += (s, ev) => SelfMark(true);
            var no = Ui.Button("Not quite yet", false, 16);
            no.Click += (s, ev) => SelfMark(false);
            Add(yes); Add(no);
        }
        body.Children.Add(acts);
    }

    void Tip()
    {
        var t = lesson.Tip;
        body.Children.Add(Ui.Text("Good to know", 14, LUi.AmberText, true));
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(LUi.Wrap(Ui.Text(t.Title, 21, Ui.TextC, true)));
        sp.Children.Add(LUi.Wrap(Ui.Text(t.Text, 15.5, Ui.TextC)));
        body.Children.Add(Ui.Card(sp, Ui.Surface2, 18));
        body.Children.Add(Next("Finish the lesson", Continue));
    }

    void Recap()
    {
        int score = Run.Score;
        string praise = score == 100 ? "Perfect!" : score >= 80 ? "Great work!" : score >= 50 ? "Good going!" : "Lesson done. Keep at it!";
        body.Children.Add(LUi.Head(praise, Run.FirstRight + " of " + lesson.Exercises.Count + " right the first time. +" + (Run.FirstRight + LessonRun.FinishPoints) + " points."));
        body.Children.Add(LUi.GoalCard(c));
        if (newItems.Count > 0)
        {
            body.Children.Add(Ui.Text("You met", 15, Ui.TextC, true));
            var list = new StackPanel { Spacing = 4 };
            foreach (var it in newItems) list.Children.Add(LUi.Wrap(Ui.Text(it.Target + "  —  " + it.English, 14, Ui.Muted)));
            body.Children.Add(list);
        }
        var next = LUi.NextLesson(c, content);
        var row = new WrapPanel();
        if (next != null)
        {
            var b = LUi.Primary("Next lesson: " + next.Title, 16);
            b.Margin = new Thickness(0, 0, 10, 10);
            b.Click += (s, e) => MainView.Current.Replace(new LessonPage(c, content, next));
            row.Children.Add(b);
        }
        var back = Ui.Button("Back to " + c.Name, false, 16);
        back.Margin = new Thickness(0, 0, 10, 10);
        back.Click += (s, e) => MainView.Current.Back();
        row.Children.Add(back);
        body.Children.Add(row);
    }

    public bool OnKey(KeyEventArgs e)
    {
        if (Now == Step.Exercise && !Run.Answered && choices.Count > 0)
        {
            int k = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
            if (k >= 0 && k < choices.Count) { Pick(choices[k]); return true; }
        }
        if (e.Key == Key.Enter && !(e.Source is TextBox))
        {
            switch (Now)
            {
                case Step.Intro: Start(); return true;
                case Step.Words: ToDialogue(); return true;
                case Step.Dialogue: if (LinesShown < lesson.Dialogue.Count) NextLine(); else ToExercises(); return true;
                case Step.Exercise: if (Run.Answered) { Continue(); return true; } break;
                case Step.Tip: Continue(); return true;
            }
        }
        return false;
    }

    public void Closed() { LessonAudio.StopSaying(); }
}
