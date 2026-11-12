using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Tests;

/// <summary>G2: watchlist volume follows the interface language's units.</summary>
public class AmountDisplayTests
{
    [Fact]
    public void Watchlist_volume_uses_wan_in_chinese_and_m_in_english()
    {
        WpfHost.Run(() =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var row = new WatchlistRowViewModel("AAPL");
            var q = FakeMarketDataService.MakeQuote("AAPL") with { Volume = 45_678_901 };
            var lang = loc.Language;
            try
            {
                loc.Apply("zh-CN");
                row.Update(q, loc);
                Assert.Equal("4567.89万", row.VolumeText);
                loc.Apply("en-US");
                row.Relabel(loc);
                Assert.Equal("45.68M", row.VolumeText);
            }
            finally { loc.Apply(lang); }
        });
    }
}
