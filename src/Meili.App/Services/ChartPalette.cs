using System.Windows;
using System.Windows.Media;
using Color = ScottPlot.Color;

namespace Meili.App.Services;

/// <summary>
/// E5: every colour a ScottPlot chart uses, for the active theme. Price/text/background colours come from the current
/// resource brushes (re-toned by <see cref="ThemeService"/>); series colours are fixed per theme so lines keep at least
/// 3:1 contrast on the plot background in both.
/// </summary>
public sealed record ChartPalette(
    bool Dark, Color Up, Color Down, Color Text, Color Muted, Color Grid, Color Background, Color DataBackground,
    Color Series1, Color Series2, Color Series3, Color Series4, Color Series5, Color Series6, Color Marker)
{
    public static ChartPalette Current(bool dark) => new(
        dark,
        Brush("UpBrush"), Brush("DownBrush"), Brush("TextFillColorPrimaryBrush"), Brush("TextFillColorSecondaryBrush"),
        dark ? new Color(255, 255, 255, 0x33) : new Color(0, 0, 0, 0x33),   // ScottPlot hex is RRGGBBAA
        Brush("ApplicationBackgroundBrush"),
        dark ? Color.FromHex("#2A2A2A") : Color.FromHex("#FFFFFF"),
        Series1: dark ? Color.FromHex("#60A5FA") : Color.FromHex("#2563EB"),   // blue
        Series2: dark ? Color.FromHex("#F2B35B") : Color.FromHex("#B45309"),   // amber
        Series3: dark ? Color.FromHex("#C4B5FD") : Color.FromHex("#7C3AED"),   // violet
        Series4: dark ? Color.FromHex("#2DD4BF") : Color.FromHex("#0F766E"),   // teal
        Series5: dark ? Color.FromHex("#F0ABFC") : Color.FromHex("#A21CAF"),   // magenta (drawings)
        Series6: dark ? Color.FromHex("#F87171") : Color.FromHex("#B91C1C"),   // red (KDJ J)
        Marker: dark ? Color.FromHex("#4ADE80") : Color.FromHex("#15803D"));

    private static Color Brush(string key)
    {
        var c = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return new Color(c.R, c.G, c.B, c.A);
    }
}
