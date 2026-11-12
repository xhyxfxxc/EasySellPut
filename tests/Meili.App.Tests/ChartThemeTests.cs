using System.IO;
using System.Windows.Controls;
using ScottPlot.WPF;
using Meili.App.Services;
using Meili.App.Views.Pages;
using Meili.Core.Settings;
using MColor = System.Windows.Media.Color;

namespace Meili.App.Tests;

/// <summary>E5: ScottPlot charts repaint with the new theme and up/down colours, without reopening the page.</summary>
public class ChartThemeTests
{
    private static MColor M(ScottPlot.Color c) => MColor.FromRgb(c.R, c.G, c.B);

    [Fact]
    public void Series_colours_keep_3_to_1_contrast_on_the_plot_background_in_both_themes()
    {
        WpfHost.Run(() =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                {
                    ScreenRenderTests.Apply("zh-CN", dark);
                    var c = ChartPalette.Current(dark);
                    foreach (var s in new[] { c.Series1, c.Series2, c.Series3, c.Series4, c.Series5, c.Series6, c.Marker, c.Up, c.Down })
                        Assert.True(UiAudit.Contrast(M(s), M(c.DataBackground)) >= 3, $"{(dark ? "dark" : "light")} {s.ToHex()}");
                    Assert.True(UiAudit.Contrast(M(c.Text), M(c.Background)) >= 4.5);
                }
            }
            finally { ScreenRenderTests.Apply("zh-CN", true); }
        });
    }

    [Theory]
    [InlineData(typeof(ChartsPage))]
    [InlineData(typeof(OptionsPage))]
    [InlineData(typeof(StrategyPage))]
    public void Open_chart_follows_theme_and_up_down_switches(Type pageType)
    {
        var dir = Environment.GetEnvironmentVariable("MEILI_RENDER_DIR");
        WpfHost.Run(async () =>
        {
            var name = pageType.Name.Replace("Page", "").ToLowerInvariant();
            try
            {
                ScreenRenderTests.Apply("zh-CN", true);
                var page = (Page)Activator.CreateInstance(pageType)!;
                using var host = new OffscreenHost(page, 1280, 800);
                await ScreenRenderTests.Settle();
                var plot = UiAudit.Descendants<WpfPlot>(page).First(p => p.IsVisible);
                Assert.Equal(ChartPalette.Current(true).Background, plot.Plot.FigureBackground.Color);
                if (dir is { Length: > 0 }) host.SavePng(Path.Combine(dir, $"e5-{name}-1-dark.png"));

                ScreenRenderTests.Apply("zh-CN", false);                        // switch while the page is open
                await ScreenRenderTests.Settle();
                var light = ChartPalette.Current(false);
                Assert.Equal(light.Background, plot.Plot.FigureBackground.Color);
                Assert.Equal(light.DataBackground, plot.Plot.DataBackground.Color);
                if (dir is { Length: > 0 }) host.SavePng(Path.Combine(dir, $"e5-{name}-2-light.png"));

                if (pageType == typeof(ChartsPage))
                {
                    ThemeService.ApplyUpDownColors(UpDownColorMode.GreenUp);
                    await ScreenRenderTests.Settle();
                    var candles = plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.CandlestickPlot>().Single();
                    Assert.Equal(ChartPalette.Current(false).Up, candles.RisingFillStyle.Color);
                    Assert.Equal(light.Down, candles.RisingFillStyle.Color);                // green-up: rising uses the former "down" green
                    if (dir is { Length: > 0 }) host.SavePng(Path.Combine(dir, $"e5-{name}-3-light-greenup.png"));
                }
            }
            finally
            {
                ThemeService.ApplyUpDownColors(AppServices.Get<SettingsStore>().Current.UpDownColors);
                ScreenRenderTests.Apply("zh-CN", true);
            }
        });
    }
}
