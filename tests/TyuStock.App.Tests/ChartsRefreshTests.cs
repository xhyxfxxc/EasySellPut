using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Models;

namespace TyuStock.App.Tests;

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
            var store = AppServices.Get<TyuStock.Core.Settings.SettingsStore>();
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
            Assert.Equal(new TyuStock.Core.Settings.IndicatorSettings(), store.Current.Charts.Indicators);
        });
    }
}
