using System.Windows;
using System.Windows.Media;
using Color = ScottPlot.Color;

namespace Meili.App.Services;

/// <summary>The chart palette from the current WPF resource brushes, with series and plot background from <see cref="ThemePalette"/>.</summary>
public static class WpfChartPalette
{
    public static ChartPalette Current(bool dark) => ChartPalette.Create(dark,
        Brush("UpBrush"), Brush("DownBrush"), Brush("TextFillColorPrimaryBrush"), Brush("TextFillColorSecondaryBrush"),
        ThemePalette.For(dark));

    private static Color Brush(string key)
    {
        var c = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return new Color(c.R, c.G, c.B, c.A);
    }
}
