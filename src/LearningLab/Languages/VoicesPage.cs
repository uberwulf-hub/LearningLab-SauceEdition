// Learning Lab - Languages: choosing the voices (the language's and the English one) and how to add free ones.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class VoicesPage : IPage
{
    readonly LangCourse c;
    readonly StackPanel body = new() { Spacing = 10 };
    readonly DispatcherTimer wait = new() { Interval = TimeSpan.FromSeconds(1) };
    int waits;
    public string Title => "Voices";
    public Control View { get; }

    public VoicesPage(LangCourse c)
    {
        this.c = c;
        View = Ui.Page(body, 760);
        // a phone's speech engine lists its voices a moment after start
        wait.Tick += (s, e) => { if (++waits > 8 || (PlatformAudio.Voices?.All.Count ?? 0) > 0) { wait.Stop(); Fill(); } };
        if (PlatformAudio.Voices != null && PlatformAudio.Voices.All.Count == 0) wait.Start();
        View.AttachedToVisualTree += (s, e) => Fill();   // back from the device's settings: new voices may be there
        Fill();
    }

    void Fill()
    {
        body.Children.Clear();
        body.Children.Add(LUi.Head("Voices", "Lessons are read by voices that come with your " + (Platform.Touch ? "phone" : "computer") + ". They are free and work offline."));
        var v = PlatformAudio.Voices;
        if (v == null) { body.Children.Add(LUi.Wrap(Ui.Text("This device can't read aloud.", 15, Ui.Yellow))); return; }
        Section(c.Name + " voice", c.Voice ?? c.Language, "Hola. ¿Cómo estás? Me llamo Sofía y vivo en Arizona.");
        Section("English voice", "en", "Hello! This is the voice for the English in your lessons.");

        var help = new StackPanel { Spacing = 8 };
        help.Children.Add(Ui.Text("Getting better voices", 16, Ui.TextC, true));
        help.Children.Add(LUi.Wrap(Ui.Text(v.HowToGetVoices, 13.5, Ui.Muted)));
        var open = Ui.Button("Open voice settings");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (s, e) => { if (!v.OpenVoiceSettings()) MainView.Current.Message("", "The settings couldn't be opened from here. Follow the steps above."); };
        help.Children.Add(open);
        var card = Ui.Card(help, Ui.Surface2, 14);
        card.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(card);
    }

    void Section(string title, string lang, string sample)
    {
        string key = "voice-" + lang.Substring(0, 2);
        body.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Ui.Brush(Ui.AccentText), Margin = new Thickness(0, 10, 0, 0) });
        var list = Lx.VoicesFor(lang);
        if (list.Count == 0) { body.Children.Add(LUi.Wrap(Ui.Text("None on this device yet. See \"Getting better voices\" below.", 14, Ui.Yellow))); return; }
        var current = Lx.Voice(lang, lang.Length > 2 ? lang : null);
        foreach (var voice in list.OrderBy(x => x.Lang).ThenBy(x => x.Name))
        {
            var vv = voice;
            var rb = new RadioButton { GroupName = key, IsChecked = current != null && voice.Id == current.Id, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            rb.Content = new StackPanel { Children = { Ui.Text(voice.Name, 15, Ui.TextC, true), Ui.Text(Region(voice.Lang) + (string.IsNullOrEmpty(voice.Quality) ? "" : "  |  " + voice.Quality + " quality"), 12.5, Ui.Muted) } };
            rb.IsCheckedChanged += (s, e) => { if (rb.IsChecked == true) { Lx.Progress.SetSetting(key, vv.Id); Lx.Save(); } };
            var tryIt = Ui.Button("Try");
            tryIt.Click += (s, e) => LessonAudio.Say(sample, vv, Lx.Rate);
            var row = new DockPanel();
            DockPanel.SetDock(tryIt, Dock.Right);
            row.Children.Add(tryIt);
            row.Children.Add(rb);
            body.Children.Add(Ui.Card(row, Ui.Surface, 8));
        }
    }

    static string Region(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return "";
        try { return new System.Globalization.CultureInfo(tag).DisplayName; }
        catch (System.Globalization.CultureNotFoundException) { return tag; }
    }

    public void Closed() { wait.Stop(); LessonAudio.StopSaying(); }
}
