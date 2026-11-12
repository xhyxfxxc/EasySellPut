using TyuStock.App.Services;
using TyuStock.Core.Models;
using TyuStock.Core.Time;

namespace TyuStock.App.Tests;

public class FreshnessTextTests
{
    [Fact]
    public void Wording_never_says_realtime_without_the_source_saying_so()
    {
        WpfHost.Run(() =>
        {
            var loc = AppServices.Get<LocalizationService>();
            loc.Apply("zh-CN");
            var regular = MarketClock.FromEastern(new DateTime(2026, 11, 12, 10, 30, 0));
            var sina = new Quote { Symbol = "AAPL", Price = 1, Source = "sina", AsOf = regular.AddMinutes(-4) };
            Assert.Contains("实测滞后约 4 分钟", FreshnessText.SourceAndTiming(sina, loc, regular));
            var unknown = sina with { AsOf = null };
            Assert.EndsWith("延迟未知", FreshnessText.SourceAndTiming(unknown, loc, regular));
            Assert.Equal("行情时间未知", FreshnessText.AsOf(null, loc));
            var chain = new OptionChain { Underlying = "AAPL", Expirations = [], Contracts = [], Source = "nasdaq", DelayMinutes = 15, FetchedAt = regular };
            var text = FreshnessText.ChainTiming(chain, loc, regular);
            Assert.Contains("延迟 15 分钟", text);
            Assert.Contains("行情时间未知", text);
            Assert.Contains("抓取于", text);
        });
    }
}
