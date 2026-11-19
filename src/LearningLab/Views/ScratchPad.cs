// Learning Lab - scratch paper (Andrew, 2026-11-13: "some sort of scratch paper ... to work through the problem"): a
// dark board with faint squares (columns of digits line up) to write on with a finger, a pen or the mouse. Pen colours
// white / yellow / blue, an eraser, Undo and Clear. Nothing is saved; Clear() is called for every new problem.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace LearningLab.Views;

public sealed class ScratchPad : Border
{
    sealed class Stroke { public readonly List<Point> Points = new(); public string Color; public double Width; }

    sealed class Surface : Control
    {
        public readonly List<Stroke> Strokes = new();
        public Stroke Drawing;
        static readonly IPen Grid = new Pen(Ui.Brush("#FFFFFF", 14), 1);
        public Surface() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Cross); }
        public override void Render(DrawingContext dc)
        {
            var b = new Rect(Bounds.Size);
            dc.FillRectangle(Ui.Brush("#0E1A14"), b);
            for (double x = 28; x < b.Width; x += 28) dc.DrawLine(Grid, new Point(x, 0), new Point(x, b.Height));
            for (double y = 28; y < b.Height; y += 28) dc.DrawLine(Grid, new Point(0, y), new Point(b.Width, y));
            foreach (var s in Strokes)
            {
                var pen = new Pen(Ui.Brush(s.Color), s.Width) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                if (s.Points.Count == 1) { dc.DrawEllipse(Ui.Brush(s.Color), null, s.Points[0], s.Width / 2, s.Width / 2); continue; }
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(s.Points[0], false);
                    for (int i = 1; i < s.Points.Count; i++) c.LineTo(s.Points[i]);
                    c.EndFigure(false);
                }
                dc.DrawGeometry(null, pen, g);
            }
        }
    }

    readonly Surface surface = new();
    string color = "#F1F5F9";
    bool eraser;
    readonly List<Button> toolButtons = new();

    /// <summary>How many strokes are on the paper (the checks look).</summary>
    public int Strokes => surface.Strokes.Count;

    public ScratchPad(double height = 300)
    {
        surface.Height = height;
        surface.PointerPressed += (s, e) =>
        {
            var p = e.GetCurrentPoint(surface);
            if (!p.Properties.IsLeftButtonPressed && e.Pointer.Type == PointerType.Mouse) return;
            e.Pointer.Capture(surface);
            Begin(p.Position);
            e.Handled = true;
        };
        surface.PointerMoved += (s, e) => { if (surface.Drawing == null) return; Extend(e.GetPosition(surface)); e.Handled = true; };
        surface.PointerReleased += (s, e) => { End(); e.Pointer.Capture(null); e.Handled = true; };
        surface.PointerCaptureLost += (s, e) => End();

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = Ui.Text("✏  Scratch paper", 14, Ui.Muted, true);
        title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 14, 6);
        bar.Children.Add(title);
        Button Tool(string text, string tip, Action act)
        {
            var b = Ui.Button(text, false, 14);
            b.MinHeight = 44; b.MinWidth = 44; b.Padding = new Thickness(12, 4); b.Margin = new Thickness(0, 0, 6, 6);
            ToolTip.SetTip(b, tip);
            b.Click += (s, e) => act();
            bar.Children.Add(b);
            return b;
        }
        foreach (var (name, hex) in new[] { ("White", "#F1F5F9"), ("Yellow", "#FDE047"), ("Blue", "#7DD3FC") })
        {
            var b = Tool("●", name + " pen", () => { color = hex; eraser = false; Mark(); });
            b.Foreground = Ui.Brush(hex); b.Tag = hex; b.FontSize = 20;
            toolButtons.Add(b);
        }
        var er = Tool("Eraser", "Rub out what you draw over", () => { eraser = true; Mark(); });
        er.Tag = "eraser"; toolButtons.Add(er);
        Tool("↶  Undo", "Take back the last line", Undo);
        Tool("Clear", "Wipe the paper", Clear);
        Mark();

        Child = new StackPanel { Children = { bar, new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = surface } } };
        Padding = new Thickness(14, 10, 14, 14);
        CornerRadius = new CornerRadius(14);
        Background = Ui.Brush(Ui.Surface);
        BorderBrush = Ui.Brush(Ui.BorderC);
        BorderThickness = new Thickness(1);
    }

    void Mark()
    {
        foreach (var b in toolButtons)
        {
            bool on = eraser ? (string)b.Tag == "eraser" : (string)b.Tag == color;
            b.BorderBrush = Ui.Brush(on ? Ui.Accent : Ui.BorderC);
            b.BorderThickness = new Thickness(on ? 2 : 1);
        }
    }

    // ---- drawing (public so the checks can draw without a pointer)
    public void Begin(Point p)
    {
        if (eraser) { EraseAt(p); surface.Drawing = new Stroke { Color = "", Width = 0 }; return; }
        surface.Drawing = new Stroke { Color = color, Width = 3.5 };
        surface.Drawing.Points.Add(p);
        surface.Strokes.Add(surface.Drawing);
        surface.InvalidateVisual();
    }

    public void Extend(Point p)
    {
        var d = surface.Drawing;
        if (d == null) return;
        if (eraser) { EraseAt(p); return; }
        var last = d.Points[^1];
        if (Math.Abs(last.X - p.X) + Math.Abs(last.Y - p.Y) < 1.5) return;
        d.Points.Add(p);
        surface.InvalidateVisual();
    }

    public void End() { surface.Drawing = null; }

    void EraseAt(Point p)
    {
        int n = surface.Strokes.RemoveAll(s => s.Points.Any(q => Math.Abs(q.X - p.X) < 12 && Math.Abs(q.Y - p.Y) < 12));
        if (n > 0) surface.InvalidateVisual();
    }

    public void UseEraser(bool on) { eraser = on; Mark(); }

    public void Undo()
    {
        if (surface.Strokes.Count == 0) return;
        surface.Strokes.RemoveAt(surface.Strokes.Count - 1);
        surface.InvalidateVisual();
    }

    public void Clear() { surface.Strokes.Clear(); surface.Drawing = null; surface.InvalidateVisual(); }
}
