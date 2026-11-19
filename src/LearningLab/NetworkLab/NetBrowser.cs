// Network Lab - the pretend web browser (App Hub's HtmlView.cs): a very small page renderer (headings, paragraphs,
// lists, bold and italic, links, line breaks, a rule and simple forms with text boxes and a button) and MiniBrowser
// (Back, Reload, the address and Go, the page). Pages come only from the simulated servers (Request.Http), never the
// real network.
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

public static class HtmlView
{
    /// <summary>A page drawn as controls, with its plain text and links (for the checks).</summary>
    public sealed class Page
    {
        public Control View;
        public string Text = "";
        public readonly List<(string Text, Action Open)> Links = new();
        public readonly Dictionary<string, TextBox> Fields = new();
        public readonly List<(string Label, Action Send)> Buttons = new();
    }

    /// <summary>Turns HTML into controls. onLink(url) for links, onSubmit(action, method, fields) for forms.</summary>
    public static Page Render(string html, bool light, Action<string> onLink, Action<string, string, Dictionary<string, string>> onSubmit)
    {
        var page = new Page();
        var fg = Ui.Brush(light ? "#111827" : "#E5E7EB");
        var linkBrush = Ui.Brush(light ? "#1D4ED8" : "#93C5FD");
        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 24) };
        var text = new StringBuilder();
        var tokens = Regex.Split(html ?? "", "(<[^>]+>)");
        // a paragraph is gathered as pieces, then drawn: as one TextBlock of runs, or (when it has links, which are
        // tapped) as a row of words that wraps, since Avalonia drops the runs before an inline control
        Border para = null; double paraSize = 15; bool paraHead = false;
        var segs = new List<(string Text, bool Bold, bool Ital, Action Link, bool Break)>();
        StackPanel list = null;
        bool ordered = false; int itemNo = 0;
        int bold = 0, ital = 0;
        string linkHref = null; StringBuilder linkText = null; Action linkOpen = null;
        string formAction = null, formMethod = "get";
        Dictionary<string, TextBox> formFields = null;
        WrapPanel formRow = null;
        WrapPanel Row() { if (formRow == null) { EndBlock(); formRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; root.Children.Add(formRow); } return formRow; }

        Border NewPara(double size = 15, bool heading = false)
        {
            var b = new Border { Margin = new Thickness(0, heading ? 6 : 0, 0, heading ? 8 : 10) };
            paraSize = size; paraHead = heading; segs.Clear();
            if (list != null)
            {
                itemNo++;
                var marker = new TextBlock { Text = ordered ? itemNo + "." : "\u2022", Foreground = fg, FontSize = size, Width = 26, Margin = new Thickness(6, 0, 0, 0) };
                var row = new DockPanel();
                DockPanel.SetDock(marker, Dock.Left);
                row.Children.Add(marker); row.Children.Add(b);
                b.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(row);
                text.Append(ordered ? itemNo + ". " : "* ");
            }
            else root.Children.Add(b);
            return b;
        }
        Border P() => para ??= NewPara();
        void Draw()
        {
            var weight = paraHead ? FontWeight.SemiBold : FontWeight.Normal;
            if (!segs.Any(x => x.Link != null))
            {
                var t = new TextBlock { FontSize = paraSize, Foreground = fg, TextWrapping = TextWrapping.Wrap, LineHeight = paraSize * 1.45, FontWeight = weight, Inlines = new InlineCollection() };
                foreach (var x in segs)
                {
                    if (x.Break) { t.Inlines.Add(new LineBreak()); continue; }
                    var run = new Run(x.Text) { Foreground = fg };
                    if (x.Bold || paraHead) run.FontWeight = x.Bold ? FontWeight.Bold : weight;
                    if (x.Ital) run.FontStyle = FontStyle.Italic;
                    t.Inlines.Add(run);
                }
                para.Child = t;
                return;
            }
            var lines = new StackPanel();
            var w = new WrapPanel();
            lines.Children.Add(w);
            foreach (var x in segs)
            {
                if (x.Break) { w = new WrapPanel(); lines.Children.Add(w); continue; }
                var words = Regex.Split(x.Text, @"(?<= )");
                if (x.Link != null) words = new[] { x.Text.Trim() };   // a link stays one piece to tap
                foreach (var word in words)
                {
                    if (word.Length == 0) continue;
                    var t = new TextBlock { Text = word, FontSize = paraSize, FontWeight = x.Bold ? FontWeight.Bold : weight, VerticalAlignment = VerticalAlignment.Center };
                    if (x.Ital) t.FontStyle = FontStyle.Italic;
                    if (x.Link != null)
                    {
                        t.Foreground = linkBrush; t.TextDecorations = TextDecorations.Underline; t.Padding = new Thickness(2, 12); t.Margin = new Thickness(0, 0, 4, 0);
                        t.Cursor = new Cursor(StandardCursorType.Hand); t.Background = Brushes.Transparent;
                        var open = x.Link;
                        t.Tapped += (s, e) => open();
                    }
                    else t.Foreground = fg;
                    w.Children.Add(t);
                }
            }
            para.Child = lines;
        }
        void EndBlock() { if (para != null) { Draw(); text.Append('\n'); } para = null; }

        foreach (var tok in tokens)
        {
            if (tok.Length == 0) continue;
            if (!tok.StartsWith("<"))
            {
                string s = System.Net.WebUtility.HtmlDecode(Regex.Replace(tok, @"\s+", " "));
                if (s.Trim().Length == 0 && para == null) continue;
                formRow = null;
                P();
                segs.Add((s, bold > 0, ital > 0, linkOpen, false));
                text.Append(s);
                linkText?.Append(s);
                continue;
            }
            string tag = tok.Trim('<', '>', '/').Split(' ')[0].ToLowerInvariant();
            bool close = tok.StartsWith("</");
            switch (tag)
            {
                case "h1": case "h2": case "h3":
                    EndBlock();
                    if (!close) para = NewPara(tag == "h1" ? 24 : tag == "h2" ? 19 : 16, true);
                    break;
                case "p": case "div": EndBlock(); break;
                case "br": P(); segs.Add(("", false, false, null, true)); text.Append('\n'); break;
                case "hr": EndBlock(); root.Children.Add(new Border { Height = 1, Background = Ui.Brush("#9CA3AF"), Margin = new Thickness(0, 6) }); break;
                case "b": case "strong": bold += close ? -1 : 1; break;
                case "i": case "em": ital += close ? -1 : 1; break;
                case "ul": case "ol":
                    EndBlock();
                    if (!close) { list = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; ordered = tag == "ol"; itemNo = 0; root.Children.Add(list); }
                    else list = null;
                    break;
                case "li": EndBlock(); if (!close) P(); break;
                case "a":
                    if (!close)
                    {
                        var m = Regex.Match(tok, "href\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        linkHref = m.Success ? m.Groups[1].Value : "";
                        string target = linkHref;
                        linkOpen = () => { if (onLink != null) Lt.Safe(() => onLink(target)); };
                        linkText = new StringBuilder();
                    }
                    else if (linkOpen != null)
                    {
                        page.Links.Add((linkText.ToString().Trim(), linkOpen));
                        linkOpen = null; linkHref = null; linkText = null;
                    }
                    break;
                case "form":
                    if (!close)
                    {
                        var ma = Regex.Match(tok, "action\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        var mm = Regex.Match(tok, "method\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        formAction = ma.Success ? ma.Groups[1].Value : "/";
                        formMethod = mm.Success ? mm.Groups[1].Value.ToLowerInvariant() : "get";
                        formFields = new Dictionary<string, TextBox>();
                    }
                    else formFields = null;
                    formRow = null;
                    break;
                case "input":
                    {
                        var mt = Regex.Match(tok, "type\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        var mn = Regex.Match(tok, "name\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        var mv = Regex.Match(tok, "value\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        string type = mt.Success ? mt.Groups[1].Value.ToLowerInvariant() : "text";
                        string value = mv.Success ? System.Net.WebUtility.HtmlDecode(mv.Groups[1].Value) : "";
                        if (type == "submit")
                        {
                            var fields = formFields; string action = formAction, method = formMethod;
                            Action send = () =>
                            {
                                var d = new Dictionary<string, string>();
                                if (fields != null) foreach (var kv in fields) d[kv.Key] = kv.Value.Text ?? "";
                                onSubmit?.Invoke(action, method, d);
                            };
                            string label = value.Length > 0 ? value : "Submit";
                            var b = light ? Lt.Primary(label, send, "#2563EB") : Ui.Button(label, true, 14);
                            if (!light) b.Click += (s, e) => Lt.Safe(send);
                            b.Margin = new Thickness(0, 4);
                            Row().Children.Add(b);
                            page.Buttons.Add((label, send));
                        }
                        else
                        {
                            var tb = light ? Lt.Box(value, 200) : new TextBox { Text = value, Width = 200, MinHeight = 40 };
                            if (type == "password") tb.PasswordChar = '*';
                            tb.Margin = new Thickness(0, 4, 8, 4);
                            if (mn.Success) { formFields?.TryAdd(mn.Groups[1].Value, tb); page.Fields[mn.Groups[1].Value] = tb; }
                            Row().Children.Add(tb);
                        }
                        break;
                    }
            }
        }
        EndBlock();
        page.View = root;
        page.Text = Regex.Replace(text.ToString(), "\n{2,}", "\n").Trim();
        return page;
    }

    public static Page Message(string title, string text, bool light) =>
        Render("<h2>" + System.Net.WebUtility.HtmlEncode(title) + "</h2><p>" + System.Net.WebUtility.HtmlEncode(text) + "</p>", light, null, null);
}

/// <summary>A pretend browser (Back, Reload, the address bar, Go and the page) wired to one device.</summary>
public sealed class MiniBrowser : DockPanel
{
    readonly Device D;
    readonly TextBox address;
    readonly ScrollViewer view;
    readonly List<string> backList = new();
    readonly bool light;
    public string Current { get; private set; } = "";
    public HtmlView.Page Shown { get; private set; }
    public string PageText => Shown?.Text ?? "";
    public event Action<string> Navigated;

    public MiniBrowser(Device d, bool lightPage, string home)
    {
        D = d; light = lightPage;
        Background = Ui.Brush(light ? "#FFFFFF" : Ui.Surface);
        var bar = new DockPanel { Margin = new Thickness(6), LastChildFill = true };
        Button B(string t, string tip, Action a)
        {
            var b = light ? Lt.Btn(t, a) : Ui.Button(t, false, 14);
            if (!light) b.Click += (s, e) => Lt.Safe(a);
            b.MinWidth = 44; b.Padding = new Thickness(10, 6); b.Margin = new Thickness(0, 0, 4, 0);
            ToolTip.SetTip(b, tip);
            return b;
        }
        var back = B("<", "Back", () => { if (backList.Count > 1) { backList.RemoveAt(backList.Count - 1); Go(backList[^1], false); } });
        var reload = B("Reload", "Load the page again", () => Go(address.Text, false));
        var go = B("Go", "Open the address", () => Go(address.Text, true));
        go.Margin = new Thickness(4, 0, 0, 0);
        address = light ? Lt.Box(home ?? "", 100) : new TextBox { Text = home ?? "", MinHeight = 40 };
        address.Width = double.NaN; address.HorizontalAlignment = HorizontalAlignment.Stretch; address.VerticalContentAlignment = VerticalAlignment.Center;
        address.Watermark = "http://192.168.1.10/ or www.example.com";
        ToolTip.SetTip(address, "A web address: http://192.168.1.10/ or a name like www.example.com");
        address.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Lt.Safe(() => Go(address.Text, true)); } };
        SetDock(back, Dock.Left); SetDock(reload, Dock.Left); SetDock(go, Dock.Right);
        bar.Children.Add(back); bar.Children.Add(reload); bar.Children.Add(go); bar.Children.Add(address);
        var barBox = new Border { Child = bar, Background = Ui.Brush(light ? "#F3F4F6" : Ui.Surface2) };
        SetDock(barBox, Dock.Top);
        Children.Add(barBox);
        view = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(view);
        Show(HtmlView.Message("Web browser", "Type an address and press Go. Pages come from the servers in your network (try a server's IP address, or www.example.com if this device can reach the internet box).", light));
    }

    void Show(HtmlView.Page p) { Shown = p; view.Content = p.View; view.Offset = default; }

    public void Go(string url, bool remember)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return;
        if (!url.Contains("://")) url = "http://" + url;
        address.Text = url;
        Current = url;
        if (remember) backList.Add(url);
        Show(HtmlView.Message("Loading...", "Asking " + url, light));
        Request.Http(D, url, "GET", null, (m, err) => Arrived(url, m, err));
        Navigated?.Invoke(url);
    }

    void Arrived(string url, TextMsg m, string err)
    {
        if (m != null) { Show(HtmlView.Render(m.Body, light, l => Link(url, l), (action, method, fields) => Submit(url, action, method, fields))); return; }
        Request.ParseUrl(url, out string host, out _, out _);
        string text;
        if (err.StartsWith("name")) text = "The name " + host + " couldn't be found. Is a DNS server set (and reachable), and does it know this name?";
        else if (err == "refused") text = host + " refused to connect: it's there, but no web server is running on it (or a firewall said no).";
        else if (err == "https") text = "This pretend network doesn't do secure (https) pages: use http://";
        else if (err == "badurl") text = "That isn't a web address.";
        else if (err == "no route" || err == "no address") text = "This device can't send anything there: check its IP address and default gateway.";
        else text = host + " took too long to answer. Check the cables, addresses, gateways, and any firewall or access list on the way.";
        Show(HtmlView.Message("This page can't be reached", text, light));
    }

    void Link(string from, string l)
    {
        if (l.StartsWith("/")) { Request.ParseUrl(from, out string host, out int port, out _); l = "http://" + host + (port != 80 ? ":" + port : "") + l; }
        Go(l, true);
    }

    void Submit(string from, string action, string method, Dictionary<string, string> fields)
    {
        Request.ParseUrl(from, out string host, out int port, out _);
        string url = action.StartsWith("http") ? action : "http://" + host + (port != 80 ? ":" + port : "") + (action.StartsWith("/") ? action : "/" + action);
        Show(HtmlView.Message("Sending...", url, light));
        Request.Http(D, url, method == "post" ? "POST" : "GET", fields, (m, err) => Arrived(url, m, err));
    }
}
