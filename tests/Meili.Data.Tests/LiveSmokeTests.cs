using Meili.Core.Models;
using Meili.Data.Http;
using Meili.Data.Providers.Yahoo;

namespace Meili.Data.Tests;

/// <summary>Real network calls (run with MEILI_LIVE=1). They prove the providers still match the live APIs.</summary>
[Trait("Category", "Live")]
public class YahooLiveTests
{
    private static readonly YahooProvider Y = new(TestHttp.Live(), new ResponseCache());

    [LiveFact] public async Task Quotes() => Assert.True((await Y.GetQuotesAsync(["AAPL", "SPY"])).All(q => q.Price > 0));
    [LiveFact] public async Task Intraday_bars() => Assert.True((await Y.GetBarsAsync(new BarRequest("AAPL", BarInterval.Minute5))).Bars.Count > 50);
    [LiveFact] public async Task Daily_bars() => Assert.True((await Y.GetBarsAsync(new BarRequest("SPY", BarInterval.Day1))).Bars.Count > 200);
    [LiveFact] public async Task Options() => Assert.NotEmpty((await Y.GetChainAsync("AAPL")).Contracts);
    [LiveFact] public async Task Fundamentals() => Assert.Equal("Apple Inc.", (await Y.GetFundamentalsAsync("AAPL")).Name);
    [LiveFact] public async Task News() => Assert.NotEmpty(await Y.GetNewsAsync("AAPL"));
    [LiveFact] public async Task Search() => Assert.Contains(await Y.SearchAsync("apple"), m => m.Symbol == "AAPL");
}