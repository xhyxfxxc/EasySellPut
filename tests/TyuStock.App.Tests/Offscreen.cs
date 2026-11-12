using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TyuStock.App.Tests;

/// <summary>
/// E4/C5/C6: hosts a page in a hidden HwndSource (no WS_VISIBLE, tool window, no-activate, parked at -32000) so
/// Loaded fires and the page activates like in the app — but nothing ever appears on the desktop, in the taskbar
/// or in Alt+Tab. Images come from <see cref="RenderTargetBitmap"/> of the visual itself, never a screen capture.
/// </summary>
public sealed class OffscreenHost : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    private readonly HwndSource _source;

    public FrameworkElement Root { get; }
    public double Width { get; }
    public double Height { get; }

    public OffscreenHost(UIElement content, double width, double height)
    {
        Width = width; Height = height;
        // A Page may only be parented by a Window or Frame, so it becomes the root visual itself.
        if (content is Page page)
        {
            page.Width = width; page.Height = height;
            page.SetResourceReference(Page.BackgroundProperty, "ApplicationBackgroundBrush");
            page.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            Root = page;
        }
        else
        {
            var b = new Border { Width = width, Height = height, Child = content };
            b.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
            b.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            Root = b;
        }
        var p = new HwndSourceParameters("meili-offscreen", (int)width, (int)height)
        {
            WindowStyle = WsPopup,                    // no WS_VISIBLE: the window is never shown
            ExtendedWindowStyle = WsExToolWindow | WsExNoActivate,
            PositionX = -32000, PositionY = -32000,
        };
        _source = new HwndSource(p) { RootVisual = Root };
        Layout();
    }

    public void Layout()
    {
        Root.Measure(new Size(Width, Height));
        Root.Arrange(new Rect(0, 0, Width, Height));
        Root.UpdateLayout();
    }

    /// <summary>PNG of the hosted visual at <paramref name="scale"/> (1.5 = 150 % display scaling).</summary>
    public void SavePng(string path, double scale = 1.0)
    {
        Layout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public void Dispose()
    {
        _source.RootVisual = null;                    // raises Unloaded → view-models deactivate (timers stop)
        _source.Dispose();
    }
}

/// <summary>E4: automatic checks over a laid-out page — clipped text, text below 12 px, low contrast.</summary>
public static class UiAudit
{
    public sealed record Finding(string Kind, string Text, string Detail);

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in Descendants<T>(c)) yield return x;
        }
    }

    private static bool Shown(FrameworkElement e)
    {
        for (DependencyObject? d = e; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: 0 }) return false;
        return e.ActualWidth > 0 && e.ActualHeight > 0;
    }

    /// <summary>Icon glyphs (Segoe Fluent / MDL2 private-use code points) are not text and are exempt from the size rule.</summary>
    private static bool IsGlyph(string text) => text.Trim().All(c => c is >= '\uE000' and <= '\uF8FF');

    private static double Natural(TextBlock tb, string text) =>
        new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, tb.FlowDirection,
            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

    public static List<Finding> Run(FrameworkElement root)
    {
        var list = new List<Finding>();
        foreach (var tb in Descendants<TextBlock>(root).ToList())
        {
            var text = tb.Text;
            if (string.IsNullOrWhiteSpace(text) || !Shown(tb)) continue;
            var glyph = IsGlyph(text);
            if (!glyph && tb.FontSize < 12 - 0.01) list.Add(new("small-font", text, $"{tb.FontSize:0.#}px"));
            if (!glyph && tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None)
            {
                var natural = Natural(tb, text);
                var avail = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
                if (natural > avail + 1.5) list.Add(new("clipped", text, $"needs {natural:0}px, has {avail:0}px"));
            }
            // Disabled controls are exempt from the contrast rule (WCAG 1.4.3).
            if (tb.IsEnabled && tb.Foreground is SolidColorBrush fg && Background(tb) is { } bg)
            {
                var f = Blend(fg.Color, bg);
                var ratio = Contrast(f, bg);
                var large = tb.FontSize >= 24 || (tb.FontSize >= 18.66 && tb.FontWeight.ToOpenTypeWeight() >= 700);
                if (ratio < (large ? 3.0 : 4.5)) list.Add(new(glyph ? "contrast-icon" : "contrast", text, $"{ratio:0.00}:1 fg {fg.Color} bg {bg}"));
            }
        }
        // Column headers clip their content at the column width (the TextBlock itself still measures full width).
        foreach (var h in Descendants<GridViewColumnHeader>(root).ToList())
        {
            if (!Shown(h) || h.Column is null) continue;
            var tb = Descendants<TextBlock>(h).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text));
            if (tb is null) continue;
            var need = Natural(tb, tb.Text) + h.Padding.Left + h.Padding.Right;
            if (need > h.ActualWidth + 1.5) list.Add(new("clipped-header", tb.Text, $"needs {need:0}px, column {h.ActualWidth:0}px"));
        }
        return list;
    }

    /// <summary>Effective background: translucent fills are composited over the nearest opaque ancestor fill.</summary>
    private static Color? Background(DependencyObject d)
    {
        var layers = new List<Color>();
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            // A toggle's Background only fills its check box, not the area behind its label.
            Brush? b = p switch { Border x => x.Background, Page x => x.Background, Panel x => x.Background, System.Windows.Controls.Primitives.ToggleButton => null, Control x => x.Background, _ => null };
            if (b is not SolidColorBrush s || s.Color.A == 0 || s.Opacity <= 0) continue;
            var c = s.Color;
            if (s.Opacity < 1) c = Color.FromArgb((byte)(c.A * s.Opacity), c.R, c.G, c.B);
            if (c.A >= 250)
            {
                var result = Color.FromRgb(c.R, c.G, c.B);
                for (var i = layers.Count - 1; i >= 0; i--) result = Blend(layers[i], result);
                return result;
            }
            layers.Add(c);
        }
        return null;
    }

    private static Color Blend(Color fg, Color bg)
    {
        var a = fg.A / 255.0;
        byte M(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromRgb(M(fg.R, bg.R), M(fg.G, bg.G), M(fg.B, bg.B));
    }

    public static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (l1, l2) = (L(a), L(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
