// Learning Lab - the one view on every platform: a stack of pages with a top bar (back, title). On Windows it fills the
// window; on Android it is the activity's content, and the phone's Back button goes back a page.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;

namespace LearningLab.Views;

public interface IPage
{
    string Title { get; }
    Control View { get; }
    /// <summary>Called when the page is left for good (Back), so it can save and stop its timers.</summary>
    void Closed() { }
    /// <summary>Back pressed on this page: return true to stay (the page handled it, for example closing a panel).</summary>
    bool OnBack() => false;
    /// <summary>Shown at the right of the top bar while the page is open (a player chip, a score), or null.</summary>
    Control Extras => null;
}

/// <summary>A page that takes keys (1-4 to answer, Space to flip...), before text boxes see them.</summary>
public interface IKeyPage { bool OnKey(KeyEventArgs e); }

public class MainView : UserControl
{
    readonly List<IPage> stack = new();
    readonly ContentControl host = new();
    readonly Button back;
    readonly TextBlock title;
    readonly Border bar;
    readonly ContentControl extras = new() { VerticalAlignment = VerticalAlignment.Center };

    public static MainView Current { get; private set; }

    /// <summary>The app is closing (the window) or going to the background (Android): apps keep their work now
    /// (Cert Lab keeps an exam or lab in progress for "Carry on where you left off").</summary>
    public static event Action AppClosing;
    public static void RaiseAppClosing() { try { AppClosing?.Invoke(); } catch (Exception) { } }

    public MainView()
    {
        Current = this;
        Background = Ui.Brush("#0B0D12");
        back = Ui.IconButton("←", "Back");
        back.Click += (s, e) => Back();
        title = Ui.Text("", 20, Ui.TextC, true);
        title.VerticalAlignment = VerticalAlignment.Center;
        var top = new DockPanel { Margin = new Thickness(12, 8) };
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(extras, Dock.Right);
        top.Children.Add(extras);
        top.Children.Add(back);
        title.Margin = new Thickness(10, 0, 0, 0);
        top.Children.Add(title);
        bar = new Border { Child = top, Background = Ui.Brush("#13161D"), BorderBrush = Ui.Brush("#252A35"), BorderThickness = new Thickness(0, 0, 0, 1) };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(host);
        overlay = new Panel { IsVisible = false, Background = Ui.Brush("#000000", 150) };
        Content = new Panel { Children = { root, overlay } };
        Push(new HomePage());
        AddHandler(KeyDownEvent, (object s, KeyEventArgs e) => { if (!overlay.IsVisible && Page is IKeyPage kp && kp.OnKey(e)) e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && overlay.IsVisible) { CloseMessage(); e.Handled = true; return; }
            if (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) { if (Back()) e.Handled = true; }
        };
    }

    readonly Panel overlay;
    Action onCancel;

    /// <summary>A message over the page with buttons (the first is the main one); null action = just close.
    /// Used instead of message boxes, which phones don't have.</summary>
    public void Message(string title, string text, params (string Label, Action Act)[] buttons)
    {
        var body = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(title)) body.Children.Add(Ui.Text(title, 20, Ui.TextC, true));
        body.Children.Add(Ui.Text(text, 15, Ui.Muted));
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        if (buttons.Length == 0) buttons = new (string, Action)[] { ("OK", null) };
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            var (label, act) = buttons[i];
            var b = Ui.Button(label, i == 0);
            b.Margin = new Thickness(8, 4, 0, 0);
            b.Click += (s, e) => { CloseMessage(); act?.Invoke(); };
            row.Children.Add(b);
        }
        onCancel = buttons.Length > 1 ? buttons[^1].Act : buttons[0].Act;
        body.Children.Add(row);
        var card = Ui.Card(body, Ui.Surface, 22);
        card.MaxWidth = 520; card.Margin = new Thickness(16);
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
        overlay.Children.Clear();
        overlay.Children.Add(card);
        overlay.IsVisible = true;
    }

    public bool MessageOpen => overlay.IsVisible;

    void CloseMessage() { overlay.IsVisible = false; overlay.Children.Clear(); }

    /// <summary>For the checks: presses the message's button with this label.</summary>
    public bool PressMessage(string label)
    {
        foreach (var b in overlay.GetLogicalDescendants().OfType<Button>())
            if (b.Content as string == label) { b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return true; }
        return false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var top = TopLevel.GetTopLevel(this);
        if (top != null) top.BackRequested += (s, a) => { if (Back()) a.Handled = true; };
    }

    public IPage Page => stack.Count > 0 ? stack[^1] : null;
    public int Depth => stack.Count;

    public void Push(IPage p)
    {
        stack.Add(p);
        Show();
    }

    /// <summary>Puts a page in place of the current one (a game's results replacing the game).</summary>
    public void Replace(IPage p)
    {
        if (stack.Count > 1) { stack[^1].Closed(); stack.RemoveAt(stack.Count - 1); }
        Push(p);
    }

    /// <summary>One page back; false when already home (Android then leaves the app).</summary>
    public bool Back()
    {
        if (overlay.IsVisible) { var c = onCancel; CloseMessage(); return true; }
        if (Page != null && Page.OnBack()) return true;
        if (stack.Count <= 1) return false;
        stack[^1].Closed();
        stack.RemoveAt(stack.Count - 1);
        Show();
        return true;
    }

    /// <summary>Back to a page of the given type (or home).</summary>
    public void BackTo<T>() where T : IPage
    {
        while (stack.Count > 1 && stack[^1] is not T) { stack[^1].Closed(); stack.RemoveAt(stack.Count - 1); }
        Show();
    }

    /// <summary>True when a page of that type is somewhere in the stack.</summary>
    public bool Has<T>() where T : IPage => stack.Any(p => p is T);

    /// <summary>Back until only that many pages are left (at least the home page).</summary>
    public void PopTo(int depth)
    {
        while (stack.Count > Math.Max(1, depth)) { stack[^1].Closed(); stack.RemoveAt(stack.Count - 1); }
        Show();
    }

    void Show()
    {
        var p = stack[^1];
        title.Text = p.Title;
        back.IsVisible = stack.Count > 1;
        host.Content = p.View;
        extras.Content = p.Extras;
    }

    /// <summary>The top bar's title, for pages that change it (a game's score).</summary>
    public void SetTitle(string t) { title.Text = t; }

    /// <summary>Asks the page for its top-bar extras again (after the stars changed).</summary>
    public void RefreshBar() { if (Page != null) { title.Text = Page.Title; extras.Content = Page.Extras; } }
}
