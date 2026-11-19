// Cert Lab (Learning Lab) - reading a page aloud (App Hub's UiRead.cs): the bar under a lesson, a flash card or a cheat
// sheet with Read / Pause, back and forward one sentence, Stop, slower / faster and whether code blocks are read. The
// page says what it reads (IReadable: units with the element that shows each); the unit being read is lit up and the
// sentence being read is shown in the bar. The voice is the device's (PlatformSpeaker over Platform.Speech); the checks
// use FakeSpeaker, which never makes a sound.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class ReadBar : Border
{
    readonly CertApp A;
    readonly IReadable what;
    readonly TextBlock said;
    readonly Button play, codeBtn;
    readonly TextBlock speedText;
    public ReadDoc Doc { get; private set; }
    Control lit; IBrush litOld;

    public ReadBar(CertApp app, IReadable page)
    {
        A = app; what = page;
        Background = Ui.Brush(Ui.Surface); BorderBrush = Ui.Brush(Ui.BorderC); BorderThickness = new Thickness(0, 1, 0, 0); Padding = new Thickness(10, 6, 10, 0);
        var sp = new StackPanel();
        said = Ui.Text("", 13, Ui.Muted); said.MaxHeight = 40; said.TextTrimming = TextTrimming.CharacterEllipsis; said.Margin = new Thickness(2, 0, 2, 4);
        sp.Children.Add(said);
        var row = new WrapPanel();
        Button B(string t, string tip, Action a)
        {
            var b = Ui.Button(t, false, 14); b.Padding = new Thickness(12, 6); b.Margin = new Thickness(0, 0, 6, 6); ToolTip.SetTip(b, tip);
            b.Click += (s, e) => { CertUi.Safe(a); Refresh(); };
            row.Children.Add(b);
            return b;
        }
        play = B("▶ Read aloud", "Read this page aloud, or pause", PlayPause);
        B("⏮", "Back one sentence", () => A.EnsureReader().Previous());
        B("⏭", "Forward one sentence", () => A.EnsureReader().Next());
        B("■", "Stop", Stop);
        B("Slower", "Read more slowly", () => A.SetRate(A.Speech.Rate - 1));
        speedText = Ui.Text("", 13, Ui.Muted); speedText.VerticalAlignment = VerticalAlignment.Center; speedText.Margin = new Thickness(0, 0, 6, 6);
        row.Children.Add(speedText);
        B("Faster", "Read faster", () => A.SetRate(A.Speech.Rate + 1));
        codeBtn = B("", "Code blocks in lessons: skip them, or read them", () => A.SetReadCode(!A.Speech.ReadCode));
        sp.Children.Add(row);
        Child = sp;
        Refresh();
    }

    /// <summary>Read / pause / go on (the first press reads the page from the top).</summary>
    public void PlayPause()
    {
        var r = A.EnsureReader();
        if (r.Playing && !r.Paused) r.Pause();
        else if (r.Paused) r.Resume();
        else ReadFrom(0);
        Refresh();
    }

    /// <summary>Reads the page from a unit.</summary>
    public void ReadFrom(int unit)
    {
        var r = A.EnsureReader();
        r.Stop();
        Unlight();
        Doc = what.ReadContent(A.Speech.ReadCode) ?? new ReadDoc();
        if (Doc.Units.Count == 0) { A.Status("There is nothing to read on this page."); return; }
        r.Rate = A.Speech.Rate;
        r.Transform = A.PronounceFor(A.Course).Say;
        r.Position -= OnPosition; r.Position += OnPosition;
        r.Finished -= OnFinished; r.Finished += OnFinished;
        r.Load("page", Doc.Texts, Math.Max(0, Math.Min(Doc.Units.Count - 1, unit)), 0);
        r.Play();
        Refresh();
    }

    public void Stop() { A.Reader?.Stop(); Unlight(); Refresh(); }

    void OnFinished() { Unlight(); Refresh(); }
    void OnPosition(string key, int unit, int sentence)
    {
        if (Doc == null || unit < 0 || unit >= Doc.Units.Count) return;
        var u = Doc.Units[unit];
        if (u.Kind == "back" && what is CardsPage cp && !cp.Revealed) cp.Reveal();
        Light(u.Element as Control);
        Refresh();
    }

    void Light(Control el)
    {
        if (el == lit) return;
        Unlight();
        if (el == null) return;
        var hi = Ui.Brush(Ui.AccentSoft);
        switch (el)
        {
            case Border b: litOld = b.Background; b.Background = hi; break;
            case Panel p: litOld = p.Background; p.Background = hi; break;
            case TextBlock t: litOld = t.Background; t.Background = hi; break;
            default: return;
        }
        lit = el;
        if (!A.Quiet) el.BringIntoView();
    }
    void Unlight()
    {
        switch (lit)
        {
            case Border b: b.Background = litOld; break;
            case Panel p: p.Background = litOld; break;
            case TextBlock t: t.Background = litOld; break;
        }
        lit = null; litOld = null;
    }
    /// <summary>The checks: the element lit up now.</summary>
    public Control Lit => lit;

    public void Refresh()
    {
        var r = A.Reader;
        bool on = r != null && r.Playing && !r.Paused;
        play.Content = on ? "⏸ Pause" : r != null && r.Paused ? "▶ Go on" : "▶ Read aloud";
        speedText.Text = "Speed " + (A.Speech.Rate > 0 ? "+" : "") + A.Speech.Rate;
        codeBtn.Content = A.Speech.ReadCode ? "Code: read" : "Code: skipped";
        if (r == null || Doc == null || (!r.Playing && !r.Paused))
        {
            said.Inlines = null;
            said.Text = "Read aloud: hear this page in the device's voice.";
            return;
        }
        var ss = r.SentencesOf(r.Unit);
        var inl = new InlineCollection { new Run(r.Paused ? "Paused: " : "Reading: ") };
        for (int i = Math.Max(0, Math.Min(r.Sentence, ss.Count - 1)); i < ss.Count; i++)
        {
            if (ReadAloud.IsPause(ss[i], out _)) { inl.Add(new Run("(a moment to think)") { FontStyle = FontStyle.Italic }); continue; }
            var run = new Run((i > r.Sentence ? " " : "") + ss[i]);
            if (i == r.Sentence) { run.Foreground = Ui.Brush(Ui.TextC); run.FontWeight = FontWeight.SemiBold; }
            inl.Add(run);
        }
        said.Inlines = inl;
    }

    /// <summary>The checks: the sentence the bar marks.</summary>
    public string MarkedSentence => said.Inlines?.OfType<Run>().FirstOrDefault(x => x.FontWeight == FontWeight.SemiBold)?.Text?.Trim() ?? "";
}
