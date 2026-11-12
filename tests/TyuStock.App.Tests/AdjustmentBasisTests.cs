using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;

namespace TyuStock.App.Tests;

/// <summary>F7: the K-line page labels the adjustment basis, offers adjclose, and warns about basis changes.</summary>
public class AdjustmentBasisTests
{
    [Fact]
    public void Basis_label_toggle_and_warnings()
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var fake = (FakeMarketDataService)AppServices.Get<IMarketDataService>();
            var settings = AppServices.Get<SettingsStore>();
            var vm = AppServices.Get<ChartsViewModel>();
            try
            {
                vm.SymbolText = "AAPL";
                await vm.LoadAsync();
                Assert.Equal(loc["Ch_AdjSplit"], vm.AdjustText);
                Assert.Equal("", vm.AdjustWarning);
                Assert.Equal(AdjustMode.Split, fake.BarRequests[^1].Adjust);

                vm.DividendAdjusted = true;                       // persisted, reloads with adjclose
                await vm.LoadAsync();
                Assert.True(settings.Current.Charts.DividendAdjusted);
                Assert.Equal(AdjustMode.Total, fake.BarRequests[^1].Adjust);
                Assert.Equal(loc["Ch_AdjTotal"], vm.AdjustText);

                // Failover to a source that only gives split-adjusted bars while the user asked for dividends.
                fake.BarMeta = _ => ("nasdaq", PriceAdjustment.Split);
                await vm.LoadAsync();
                Assert.Contains(SourceNames.Display("nasdaq", loc), vm.AdjustWarning);
                Assert.Contains(loc["Ch_AdjSplit"], vm.AdjustWarning);

                // Back to split mode; a source switch that changes the basis is called out.
                fake.BarMeta = null;
                vm.DividendAdjusted = false;
                await vm.LoadAsync();
                fake.BarMeta = _ => ("tencent", PriceAdjustment.SplitDividend);
                await vm.LoadAsync();
                Assert.Equal(string.Format(loc["Ch_AdjChanged"], SourceNames.Display("yahoo", loc), SourceNames.Display("tencent", loc),
                    loc["Ch_AdjSplit"], loc["Ch_AdjTotal"]), vm.AdjustWarning);
            }
            finally
            {
                fake.BarMeta = null;
                vm.DividendAdjusted = false;
                vm.Deactivate();
            }
        });
    }
}
