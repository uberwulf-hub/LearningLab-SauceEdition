// Cert Lab - what the lab and exam pages call: PortalWorkspaces.Make(sim) gives the MD-102 practice portal
// (EpWorkspace) for an EpSim, the AZ-800 practice consoles (WsWorkspace) for a WsSim, else null. The PowerShell
// consoles inside them are Cert Lab's terminal when the screens set TerminalMaker (and TerminalPoll), else the small
// PortalConsole below, so the portal works and can be checked on its own.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public static class PortalWorkspaces
{
    /// <summary>Makes a terminal control for a console (set by Cert Lab's screens to their terminal view).</summary>
    public static Func<ITerm, Control> TerminalMaker;
    /// <summary>Shows a terminal's new output (called every Tick for terminals made by TerminalMaker).</summary>
    public static Action<Control> TerminalPoll;

    /// <summary>The practice area for a course's simulator: EpWorkspace (MD-102), WsWorkspace (AZ-800), or null.
    /// exam = drawn for an exam's column; status = where messages also go (the page's status line), may be null.</summary>
    public static Control Make(object sim, bool exam = false, Action<string> status = null)
    {
        if (sim is EpSim ep) return new EpWorkspace(ep, exam, status);
        if (sim is WsSim ws) return new WsWorkspace(ws, exam, status);
        return null;
    }

    /// <summary>Moves a practice area on after the clock moved (the page's one-second timer).</summary>
    public static void Tick(Control workspace)
    {
        if (workspace is EpWorkspace e) e.Tick();
        else if (workspace is WsWorkspace w) w.Tick();
    }

    /// <summary>For the host page's OnBack: true when the practice area used the Back press itself.</summary>
    public static bool OnBack(Control workspace)
        => workspace is EpWorkspace e ? e.OnBack() : workspace is WsWorkspace w && w.OnBack();

    internal static Control Terminal(ITerm t)
    {
        if (!t.Waiting && !t.Finished) t.Start();
        var c = TerminalMaker?.Invoke(t);
        return c ?? new PortalConsole(t);
    }

    internal static void Poll(Control terminal)
    {
        if (terminal is PortalConsole pc) pc.Poll();
        else TerminalPoll?.Invoke(terminal);
    }
}

/// <summary>A plain console for a simulated machine: the output, the prompt and a box to type in (Enter or Run).</summary>
public sealed class PortalConsole : Border
{
    public readonly ITerm Term;
    readonly TextBox output;
    readonly TextBlock prompt;
    public readonly TextBox Input;
    readonly List<string> history = new();
    int histPos;

    public PortalConsole(ITerm term)
    {
        Term = term;
        Background = Ui.Brush("#0A0C10");
        BorderBrush = Ui.Brush(Ui.BorderC);
        BorderThickness = new Thickness(1);
        MinHeight = 280;
        output = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Pk.Mono, FontSize = 13, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = Ui.Brush("#D7DCE6"), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Stretch,
        };
        prompt = new TextBlock { FontFamily = Pk.Mono, FontSize = 13, Foreground = Ui.Brush(Ui.AccentText), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        Input = new TextBox { FontFamily = Pk.Mono, FontSize = 14, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = "Type a command" };
        var run = Pk.Accent("Run", () => Submit());
        run.Margin = new Thickness(6, 0, 6, 0);
        Input.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { Submit(); e.Handled = true; }
            else if (e.Key == Key.Up && history.Count > 0) { histPos = Math.Max(0, histPos - 1); Input.Text = history[histPos]; e.Handled = true; }
            else if (e.Key == Key.Down && history.Count > 0) { histPos = Math.Min(history.Count, histPos + 1); Input.Text = histPos < history.Count ? history[histPos] : ""; e.Handled = true; }
        };
        var bottom = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        DockPanel.SetDock(prompt, Dock.Left); DockPanel.SetDock(run, Dock.Right);
        bottom.Children.Add(prompt); bottom.Children.Add(run); bottom.Children.Add(Input);
        var dock = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(new ScrollViewer { Content = output });
        Child = dock;
        Poll();
    }

    public string Text => output.Text ?? "";

    /// <summary>Runs a line as if typed and Enter pressed.</summary>
    public void Submit(string line = null)
    {
        line ??= Input.Text ?? "";
        Input.Text = "";
        if (!Term.Waiting) { Poll(); return; }
        if (line.Trim().Length > 0) { history.Add(line); histPos = history.Count; }
        output.Text = Text + Term.Prompt + line + "\n";
        Term.Execute(line);
        Term.AfterSubmit();
        Poll();
    }

    public void Poll()
    {
        if (Term.ClearRequested) { Term.ClearRequested = false; output.Text = ""; }
        string o = Term.TakeOutput();
        if (!string.IsNullOrEmpty(o))
        {
            string all = Text + o.Replace("\r", "");
            var lines = all.Split('\n');
            if (lines.Length > 3000) all = string.Join("\n", lines.Skip(lines.Length - 3000));
            output.Text = all;
            output.CaretIndex = all.Length;
        }
        prompt.Text = Term.Waiting ? Term.Prompt.TrimEnd() : Term.Finished ? "(closed)" : "...";
    }
}
