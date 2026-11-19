// Learning Lab - Spelling Bee: adding a word list (from a file: a page like the spelling-bee practice page, or a .txt /
// .csv; or typed / pasted words with a name), and changing an added list (rename, delete). Added lists are kept on this
// device for everyone (<data>\SpellingBee\lists).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LearningLab.LearningGames;
using LearningLab.Views;

namespace LearningLab.SpellingBee;

public class BeeAddListPage : IPage
{
    public string Title => "Add a word list";
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly TextBox name = new() { FontSize = 17, MaxLength = 60, PlaceholderText = "For example: Week 3 words" };
    readonly TextBox words = new() { FontSize = 17, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160, MaxHeight = 320, PlaceholderText = "cabin\ncarve\nearth\n..." };

    public BeeAddListPage()
    {
        var body = new StackPanel { Spacing = 14 };

        var f = new StackPanel { Spacing = 8 };
        f.Children.Add(Ui.Text("Import from a file", 18, Ui.TextC, true));
        f.Children.Add(Ui.Text("A spelling practice web page (its word lists come in with their names), or a text or spreadsheet file (.txt, .csv) with one word per line, or words separated by spaces or commas. A line starting with # gives the words after it a list name. \"color|colour\" means both spellings are right.", 14, Ui.Muted));
        var pick = Ui.Button("Choose a file...", true);
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Click += async (s, e) => await PickFile();
        f.Children.Add(pick);
        body.Children.Add(Ui.Card(f, Ui.Surface, 18));

        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(Ui.Text("Type or paste words", 18, Ui.TextC, true));
        p.Children.Add(Ui.Text("List name", 13, Ui.Muted));
        p.Children.Add(name);
        p.Children.Add(Ui.Text("Words (one per line, or separated by spaces or commas)", 13, Ui.Muted));
        p.Children.Add(words);
        var save = Ui.Button("Save this list", true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Click += (s, e) => SavePasted();
        p.Children.Add(save);
        body.Children.Add(Ui.Card(p, Ui.Surface, 18));
        View = Ui.Page(body, 760);
    }

    async Task PickFile()
    {
        var top = TopLevel.GetTopLevel(View);
        if (top?.StorageProvider == null || !top.StorageProvider.CanOpen) { MainView.Current.Message("", "This device can't open files here. Paste the words into the box instead."); return; }
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a word list", AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Word lists") { Patterns = new[] { "*.html", "*.htm", "*.txt", "*.csv" }, MimeTypes = new[] { "text/html", "text/plain", "text/csv", "text/comma-separated-values" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files == null || files.Count == 0) return;
            var file = files[0];
            string text;
            await using (var s = await file.OpenReadAsync())
            using (var r = new StreamReader(s))
            {
                if (s.CanSeek && s.Length > 5_000_000) { MainView.Current.Message("", "That file is too big to be a word list."); return; }
                text = await r.ReadToEndAsync();
            }
            Import(file.Name, text);
        }
        catch (Exception ex) { MainView.Current.Message("Couldn't read that file", ex.Message); }
    }

    /// <summary>Adds the lists in a file's text and goes back to the start page (the checks call it with a fixture).</summary>
    public void Import(string fileName, string text)
    {
        var lists = Bee.ReadFile(fileName, text);
        Done(lists);
    }

    public void SetPasted(string listName, string text) { name.Text = listName; words.Text = text; }

    public void SavePasted()
    {
        string n = (name.Text ?? "").Trim();
        if (n.Length == 0) { MainView.Current.Message("", "Give the list a name, like \"Week 3 words\"."); return; }
        var lists = BeeImport.ParseText(words.Text ?? "", n);
        Done(lists);
    }

    static void Done(List<BeeList> lists)
    {
        string said;
        try { said = Bee.Add(lists); }
        catch (Exception ex) { MainView.Current.Message("Not saved", "The list couldn't be saved: " + ex.Message); return; }
        if (lists.Count > 0)
        {
            MainView.Current.BackTo<BeeHomePage>();
            if (MainView.Current.Page is BeeHomePage h) h.Fill();
        }
        MainView.Current.Message(lists.Count > 0 ? "Word list added" : "No words found", said);
    }
}

public class BeeListPage : IPage
{
    readonly BeeList list;
    public string Title => list.Name;
    public Control View { get; }
    readonly TextBox name = new() { FontSize = 17, MaxLength = 60 };

    public BeeListPage(BeeList l)
    {
        list = l;
        name.Text = l.Name;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Ui.Text("List name", 13, Ui.Muted));
        body.Children.Add(name);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var save = Ui.Button("Save the name", true); save.Margin = new Thickness(0, 0, 8, 8); save.Click += (s, e) => Rename(name.Text);
        var del = Ui.Button("Delete this list"); del.Margin = new Thickness(0, 0, 8, 8);
        del.Foreground = Ui.Brush(Ui.Red); del.BorderBrush = Ui.Brush(Ui.Red, 120);
        del.Click += (s, e) => MainView.Current.Message("Delete " + l.Name + "?", "This deletes the list from this device for everyone. Words already in someone's Tricky Words stay there.",
            ("Delete", Delete), ("Keep it", null));
        row.Children.Add(save); row.Children.Add(del);
        body.Children.Add(row);
        body.Children.Add(Ui.Text(l.Words.Count + " word" + (l.Words.Count == 1 ? "" : "s"), 15, Ui.TextC, true));
        body.Children.Add(Ui.Text(string.Join(", ", l.Words.Select(w => w.Alts.Count > 1 ? w.Word + " (or " + string.Join(", ", w.Alts.Skip(1)) + ")" : w.Word)), 14, Ui.Muted));
        var card = Ui.Card(body, Ui.Surface, 20);
        View = Ui.Page(card, 760);
    }

    public void Rename(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length == 0) { MainView.Current.Message("", "Type a name for the list."); return; }
        list.Name = n;
        try { Bee.Store.Save(list); }
        catch (Exception ex) { MainView.Current.Message("Not saved", ex.Message); return; }
        Bee.ReloadMine();
        Back();
    }

    public void Delete()
    {
        try { Bee.Store.Delete(list); }
        catch (Exception ex) { MainView.Current.Message("Not deleted", ex.Message); return; }
        Bee.ReloadMine();
        Back();
    }

    static void Back()
    {
        MainView.Current.BackTo<BeeHomePage>();
        if (MainView.Current.Page is BeeHomePage h) h.Fill();
    }
}
