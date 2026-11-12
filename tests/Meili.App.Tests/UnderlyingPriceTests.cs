using Meili.App.Services;
using Meili.Core.Models;

namespace Meili.App.Tests;

/// <summary>F8: the options maths uses the chain's in-sync underlying price; the latest stock price is shown beside it.</summary>
public class UnderlyingPriceTests
{
    [Fact]
    public void Options_page_labels_the_synced_underlying_and_shows_the_latest_price()
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var vm = AppServices.Get<OptionsViewModel>();
            try
            {
                vm.SymbolText = "MSFT";
                await vm.LoadAsync();
                Assert.Equal(123.45, vm.Spot);                                                   // chain price, not the quote
                Assert.Contains(string.Format(loc["Op_SpotSynced"], "123.45", 15), vm.Headline);
                Assert.StartsWith(string.Format(loc["Op_LivePrice"], "123.45", "").TrimEnd(), vm.LivePriceText);
            }
            finally { vm.Deactivate(); }
        });
    }

    [Fact]
    public void Falls_back_to_the_stock_quote_and_says_so()
    {
        WpfHost.Run(() =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var now = DateTimeOffset.UtcNow;
            var quote = FakeMarketDataService.MakeQuote("AAPL", 200);
            var noPrice = new OptionChain { Underlying = "AAPL", Source = "x", Expirations = [], Contracts = [] };

            var r = UnderlyingText.Describe(noPrice, quote, loc, now);
            Assert.Equal(200, r.Spot);
            Assert.Equal(string.Format(loc["Op_SpotFromQuote"], "200.00"), r.Main);
            Assert.Equal("", r.Live);

            var closed = quote with { Price = 0, PreviousClose = 190 };
            Assert.Equal(190, UnderlyingText.Describe(null, closed, loc, now).Spot);

            var realtimeChain = noPrice with { UnderlyingPrice = 201, IsRealtime = true };
            Assert.Equal(string.Format(loc["Op_SpotSyncedNoDelay"], "201.00"), UnderlyingText.Describe(realtimeChain, quote, loc, now).Main);
            return Task.CompletedTask;
        });
    }

    /// <summary>E4: switching language while the page is hidden must not leave the header in the old language.</summary>
    [Fact]
    public void Header_follows_a_language_switch_without_reloading()
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var options = AppServices.Get<OptionsViewModel>();
            var strategy = AppServices.Get<StrategyViewModel>();
            var charts = AppServices.Get<ChartsViewModel>();
            var before = loc.Language;
            try
            {
                loc.Apply("zh-CN");
                options.SymbolText = "MSFT";
                strategy.SymbolText = "MSFT";
                await options.LoadAsync();
                await strategy.LoadAsync();
                charts.SymbolText = "MSFT";
                await charts.LoadAsync();
                options.Deactivate();
                strategy.Deactivate();
                loc.Apply("en-US");
                foreach (var text in new[] { options.Headline, options.StatusText, options.DelayText, strategy.Headline, strategy.StatusText, charts.AdjustText })
                    Assert.DoesNotMatch(@"\p{IsCJKUnifiedIdeographs}", text);
                Assert.Contains(string.Format(loc["Op_SpotSynced"], "123.45", 15), options.Headline);
                Assert.Equal(FreshnessText.Adjustment(charts.Series!.Adjustment, loc), charts.AdjustText);
            }
            finally
            {
                loc.Apply(before);
                options.Deactivate();
                strategy.Deactivate();
                charts.Deactivate();
            }
        });
    }
}
