using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Models;

namespace TyuStock.App.Tests;

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
}
