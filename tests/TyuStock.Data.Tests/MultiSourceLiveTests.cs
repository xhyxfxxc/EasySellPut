using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;

namespace TyuStock.Data.Tests;

/// <summary>Each provider against the real endpoint (TYU_LIVE=1). Bypasses failover so a broken source is visible.</summary>
[Trait("Category", "Live")]
public class MultiSourceLiveTests
{
    private static readonly Lazy<DataServices> D = new(() => new DataServices(new NetworkSettings(), gateway: TestHttp.Live()));
    private static T P<T>(string id) => (T)(object)D.Value.Providers.Single(p => p.Id == id);

    [LiveFact] public async Task Cboe_quote_chain_bars()
    {
        var p = P<IOptionsProvider>("cboe");
        var c = await p.GetChainAsync("AAPL");
        Assert.Contains(c.Contracts, x => x.Delta is not null);
        Assert.NotEmpty(await ((IQuoteProvider)p).GetQuotesAsync(["SPY"]));
        Assert.True((await ((IBarsProvider)p).GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1))).Bars.Count > 200);
        Assert.NotEmpty((await p.GetChainAsync("^SPX")).Contracts);
    }

    [LiveFact] public async Task Nasdaq_quote_history_chain_news()
    {
        var p = P<IQuoteProvider>("nasdaq");
        Assert.Equal(2, (await p.GetQuotesAsync(["AAPL", "SPY"])).Count);
        Assert.NotEmpty((await ((IBarsProvider)p).GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1))).Bars);
        Assert.NotEmpty((await ((IOptionsProvider)p).GetChainAsync("AAPL")).Contracts);
        Assert.NotEmpty(await ((INewsProvider)p).GetNewsAsync("AAPL"));
        Assert.NotEmpty((await ((IFinancialsProvider)p).GetFinancialsAsync("AAPL")).Lines);
    }

    [LiveFact] public async Task Cnbc_quotes() => Assert.Equal(2, (await P<IQuoteProvider>("cnbc").GetQuotesAsync(["AAPL", "SPY"])).Count);

    [LiveFact] public async Task Sec_search_and_financials()
    {
        Assert.Contains(await P<ISymbolSearchProvider>("sec").SearchAsync("MSFT"), m => m.Symbol == "MSFT");
        Assert.NotEmpty((await P<IFinancialsProvider>("sec").GetFinancialsAsync("MSFT")).Lines);
    }

    [LiveFact] public async Task Sina_quotes_and_daily()
    {
        Assert.Equal(3, (await P<IQuoteProvider>("sina").GetQuotesAsync(["AAPL", "SPY", "BRK.B"])).Count);
        Assert.True((await P<IBarsProvider>("sina").GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1))).Bars.Count > 200);
    }

    [LiveFact] public async Task Tencent_quotes_kline_minute()
    {
        Assert.Equal(2, (await P<IQuoteProvider>("tencent").GetQuotesAsync(["AAPL", "SPY"])).Count);
        Assert.NotEmpty((await P<IBarsProvider>("tencent").GetBarsAsync(new BarRequest("SPY", BarInterval.Day1))).Bars);
        Assert.NotEmpty((await P<IBarsProvider>("tencent").GetBarsAsync(new BarRequest("AAPL", BarInterval.Minute1))).Bars);
    }

    [LiveFact] public async Task Eastmoney_quotes_across_exchanges()
    {
        var q = await P<IQuoteProvider>("eastmoney").GetQuotesAsync(["AAPL", "JPM", "SPY"]); // Nasdaq, NYSE, Arca
        Assert.Equal(3, q.Count);
        Assert.NotEmpty((await P<IBarsProvider>("eastmoney").GetBarsAsync(new BarRequest("JPM", BarInterval.Week1))).Bars);
    }

    [LiveFact] public async Task Router_end_to_end()
    {
        var r = D.Value.Router;
        Assert.Equal(3, (await r.GetQuotesAsync(["AAPL", "SPY", "QQQ"])).Count);
        Assert.NotEmpty((await r.GetBarsAsync(new BarRequest("AAPL", BarInterval.Minute15))).Bars);
        Assert.NotEmpty((await r.GetChainAsync("SPY")).Contracts);
    }
}