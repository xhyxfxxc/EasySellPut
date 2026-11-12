using System.Windows;
using System.Windows.Media;
using Color = ScottPlot.Color;

namespace Meili.App.Services;

/// <summary>E5: the chart palette from the current WPF resource brushes (re-toned by <see cref="ThemeService"/>).</summary>
public static class WpfChartPalette
{
    public static ChartPalette Current(bool dark) => ChartPalette.Create(dark,
        Brush("UpBrush"), Brush("DownBrush"), Brush("TextFillColorPrimaryBrush"), Brush("TextFillColorSecondaryBrush"),
        Brush("ApplicationBackgroundBrush"));

    private static Color Brush(string key)
    {
        var c = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return new Color(c.R, c.G, c.B, c.A);
    }
}
