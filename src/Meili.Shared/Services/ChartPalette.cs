using Color = ScottPlot.Color;

namespace Meili.Shared.Services;

/// <summary>
/// Every colour a ScottPlot chart uses, for the active theme. Price and text colours come from the front
/// end's current theme resources (so they follow the up/down convention); series, grid and plot background
/// come from <see cref="ThemePalette"/>.
/// </summary>
public sealed record ChartPalette(
    bool Dark, Color Up, Color Down, Color Text, Color Muted, Color Grid, Color Background, Color DataBackground,
    Color Series1, Color Series2, Color Series3, Color Series4, Color Series5, Color Series6, Color Marker)
{
    public static ChartPalette Create(bool dark, Color up, Color down, Color text, Color muted, ThemePalette p)
    {
        Color Argb(uint v) => new((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24));
        var chart = Argb(p.Chart);
        var grid = new Color((byte)(p.PrimaryText >> 16), (byte)(p.PrimaryText >> 8), (byte)p.PrimaryText, p.GridAlpha);
        return new(dark, up, down, text, muted, grid, chart, chart,
            Argb(p.Series1), Argb(p.Series2), Argb(p.Series3), Argb(p.Series4), Argb(p.Series5), Argb(p.Series6),
            Argb(p.Series2));
    }

    // Older call sites passed a window-background colour as the last argument; the plot now always uses the palette's Chart.
    public static ChartPalette Create(bool dark, Color up, Color down, Color text, Color muted, Color _) =>
        Create(dark, up, down, text, muted, ThemePalette.For(dark));
}
