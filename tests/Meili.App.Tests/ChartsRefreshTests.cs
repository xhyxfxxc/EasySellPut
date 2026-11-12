using Meili.App.Services;
using Meili.Core.Models;

namespace Meili.App.Tests;

/// <summary>C2: refreshes that bring nothing new do not recompute or redraw; new data keeps the user's view.</summary>
public class ChartsRefreshTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_data_detection()
    {
        BarSeries S(double lastClose, string src = "yahoo") => new()
        {
            Symbol = "AAPL", Interval = BarInterval.Day1, Source = src,
            Bars = [Bar.FromSource(T0, 1, 2, 0.5, 1.5, 10), Bar.FromSource(T0.AddDays(1), 1.5, 2, 1, lastClose, 10)],
        };
        Assert.True(ChartsViewModel.SameData(S(1.8), S(1.8)));
        Assert.False(ChartsViewModel.SameData(S(1.8), S(1.9)));
        Assert.False(ChartsViewModel.SameData(S(1.8), S(1.8, "cboe")));
        Assert.False(ChartsViewModel.SameData(S(1.8), S(1.8) with { OfflineCache = true }));
    }

    [Fact]
    public void Unchanged_refresh_raises_no_redraw_and_full_load_fits_the_view()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<ChartsViewModel>();
            vm.SymbolText = "AAPL";
            var events = new List<bool>();
            void Handler(object? s, EventArgs e) => events.Add(vm.PreserveView);
            vm.PlotChanged += Handler;
            try
            {
                await vm.LoadAsync();
                Assert.Equal([false], events);                 // full load: fit to data
                var before = vm.UnchangedRefreshes;
                var series = vm.Series;
                await vm.LoadAsync(refreshOnly: true);         // fake returns identical bars
                Assert.Equal(before + 1, vm.UnchangedRefreshes);
                Assert.Same(series, vm.Series);
                Assert.Single(events);
            }
            finally
            {
                vm.PlotChanged -= Handler;
                vm.Deactivate();
            }
        });
    }
}

/// <summary>G3: editing an indicator parameter recomputes in place, relabels, and is saved.</summary>
public class IndicatorParamsTests
{
    [Fact]
    public void Editing_a_period_recomputes_relabels_and_persists()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<ChartsViewModel>();
            var store = AppServices.Get<Meili.Core.Settings.SettingsStore>();
            try
            {
                vm.SymbolText = "AAPL";
                await vm.LoadAsync();
                var requests = WpfHost.Data.BarRequests.Count;
                vm.MaPeriod = 5;
                vm.MacdFast = 40;   // above slow: normalised so fast < slow
                Assert.Equal(5, vm.Snapshot!.Options.SmaPeriod);
                Assert.Equal("MA(5)", vm.MaLabel);
                Assert.True(vm.MacdSlow > vm.MacdFast);
                Assert.Equal(5, store.Current.Charts.Indicators.MaPeriod);
                Assert.Equal(requests, WpfHost.Data.BarRequests.Count);   // no reload
            }
            finally
            {
                vm.ResetIndicatorsCommand.Execute(null);
                vm.Deactivate();
            }
            Assert.Equal(new Meili.Core.Settings.IndicatorSettings(), store.Current.Charts.Indicators);
        });
    }
}

/// <summary>B5: drawings are placed by clicks, saved per symbol, and a comparison symbol is rebased onto the main close.</summary>
public class DrawingAndCompareTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rebase_starts_the_comparison_at_the_main_close_on_shared_bars()
    {
        BarSeries S(string sym, params double[] closes) => new()
        {
            Symbol = sym, Interval = BarInterval.Day1, Source = "x",
            Bars = closes.Select((c, i) => Bar.FromSource(T0.AddDays(i), c, c, c, c, 1)).ToList(),
        };
        var main = S("AAPL", 200, 210, 220);
        var other = S("QQQ", 400, 440, 420) with { Bars = S("QQQ", 400, 440, 420).Bars.Skip(1).ToList() };   // starts one bar later
        var r = ChartsViewModel.Rebase(main, other)!.Value;
        Assert.Equal([T0.AddDays(1), T0.AddDays(2)], r.Time);
        Assert.Equal(210, r.Close[0], 6);
        Assert.Equal(210 * 420.0 / 440, r.Close[1], 6);
        Assert.Null(ChartsViewModel.Rebase(main, S("X") with { Bars = [] }));
    }

    [Fact]
    public void Clicks_place_lines_that_are_saved_per_symbol()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<ChartsViewModel>();
            var store = AppServices.Get<Meili.Core.Settings.ChartDrawingStore>();
            try
            {
                vm.SymbolText = "AAPL";
                await vm.LoadAsync();
                var bars = vm.Series!.Bars;
                Assert.False(vm.AddDrawingPoint(bars[0].Time, 100));                 // no tool active: ignored
                vm.StartDrawingCommand.Execute(Meili.Core.Settings.DrawingKind.HorizontalLine);
                Assert.True(vm.AddDrawingPoint(bars[0].Time, 101.5));
                vm.StartDrawingCommand.Execute(Meili.Core.Settings.DrawingKind.TrendLine);
                Assert.True(vm.AddDrawingPoint(bars[1].Time, 100));
                Assert.Single(vm.Drawings);                                          // trend line waits for its second point
                Assert.True(vm.AddDrawingPoint(bars[5].Time, 110));
                Assert.Equal(2, vm.Drawings.Count);
                Assert.Null(vm.DrawMode);
                var reloaded = new Meili.Core.Settings.ChartDrawingStore(System.IO.Path.GetDirectoryName(store.FilePath)!);
                Assert.Equal(vm.Drawings, reloaded.For("aapl"));

                vm.CompareText = "qqq";
                await vm.ApplyCompareCommand.ExecuteAsync(null);
                Assert.Equal("QQQ", vm.CompareText);
                Assert.NotNull(vm.CompareLine);
                Assert.Contains("QQQ", vm.CompareLabel);
            }
            finally
            {
                vm.ClearDrawingsCommand.Execute(null);
                vm.CompareText = "";
                await vm.ApplyCompareCommand.ExecuteAsync(null);
                vm.Deactivate();
            }
            Assert.Empty(store.For("AAPL"));
        });
    }
}
