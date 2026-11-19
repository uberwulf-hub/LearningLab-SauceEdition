// Emulator Lab - the machine's monitor: the engine's Display draws 640 x 400 pixels (the framebuffer doubled and the
// 80 x 25 text console), and ScreenView shows them in a WriteableBitmap scaled to fit (crisp pixels when it is shown
// bigger, smooth when a phone shows it smaller). ScreenFont draws the console's 8 x 16 letters once per process with a
// monospaced font of the device (App Hub drew them with Consolas the same way).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmulatorLab;

namespace LearningLab.EmulatorLab;

public static class ScreenFont
{
    static bool made;
    public static readonly FontFamily Mono = new("Consolas, Cascadia Mono, Courier New, Droid Sans Mono, monospace");

    /// <summary>Gives the engine its letters (once; the UI thread).</summary>
    public static void Ensure()
    {
        if (made) return;
        made = true;
        try { Display.SetFont(Build()); }
        catch (Exception) { /* the block characters still show; letters are missing (never seen so far) */ }
    }

    static byte[][] Build()
    {
        var face = new Typeface(Mono, FontStyle.Normal, FontWeight.Bold);
        var rtb = new RenderTargetBitmap(new PixelSize(8 * 16, 16 * 16), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        {
            dc.FillRectangle(Brushes.Black, new Rect(0, 0, 128, 256));
            for (int c = 33; c < 256; c++)
            {
                string s = Display.CharFor(c);
                if (s == null) continue;
                var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 13.0, Brushes.White);
                double x = (c % 16) * 8 + (8 - ft.WidthIncludingTrailingWhitespace) / 2;
                double y = (c / 16) * 16 + (16 - ft.Height) / 2;
                using (dc.PushClip(new Rect((c % 16) * 8, (c / 16) * 16, 8, 16)))
                    dc.DrawText(ft, new Point(x, y));
            }
        }
        var px = new int[128 * 256];
        var h = GCHandle.Alloc(px, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, 128, 256), h.AddrOfPinnedObject(), px.Length * 4, 128 * 4); }
        finally { h.Free(); }
        var g = new byte[256][];
        for (int c = 0; c < 256; c++)
        {
            var b = new byte[128];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 8; x++)
                {
                    int p = px[((c / 16) * 16 + y) * 128 + (c % 16) * 8 + x];
                    int v = (p >> 8) & 0xff;                     // green channel = coverage
                    b[y * 8 + x] = (byte)(v > 200 ? 255 : v < 40 ? 0 : v);
                }
            g[c] = b;
        }
        return g;
    }
}

/// <summary>The picture of the monitor (UI thread only). Present copies the engine's pixels into the bitmap.</summary>
public sealed class ScreenView : Image
{
    public readonly Display Display = new();
    readonly WriteableBitmap bitmap = new(new PixelSize(Display.W, Display.H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    readonly int[] buffer = new int[Display.W * Display.H];

    public ScreenView()
    {
        ScreenFont.Ensure();
        Source = bitmap;
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        SizeChanged += (s, e) =>
            RenderOptions.SetBitmapInterpolationMode(this, e.NewSize.Width >= Display.W - 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
    }

    public void Present()
    {
        var src = Display.Pixels;
        for (int i = 0; i < src.Length; i++) buffer[i] = src[i] | unchecked((int)0xFF000000);
        using (var fb = bitmap.Lock())
        {
            if (fb.RowBytes == Display.W * 4) Marshal.Copy(buffer, 0, fb.Address, buffer.Length);
            else for (int y = 0; y < Display.H; y++) Marshal.Copy(buffer, y * Display.W, fb.Address + y * fb.RowBytes, Display.W);
        }
        InvalidateVisual();
    }
}
