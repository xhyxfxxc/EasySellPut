using Avalonia.Media;

namespace Meili.Mac.Platform;

/// <summary>The chart palette from the current theme brushes (re-toned by <see cref="MacTheme"/>).</summary>
public static class MacChartPalette
{
    public static ChartPalette Current(bool dark) => ChartPalette.Create(dark,
        Brush("UpBrush"), Brush("DownBrush"), Brush("TextFillColorPrimaryBrush"), Brush("TextFillColorSecondaryBrush"),
        Brush("ApplicationBackgroundBrush"));

    private static ScottPlot.Color Brush(string key)
    {
        var c = ((ISolidColorBrush)Ui.Platform.Resource(key)!).Color;
        return new ScottPlot.Color(c.R, c.G, c.B, c.A);
    }
}
