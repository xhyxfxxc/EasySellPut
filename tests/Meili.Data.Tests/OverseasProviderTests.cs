using Meili.Core.Models;
using Meili.Core.Time;
using Meili.Data.Providers.Cboe;
using Meili.Data.Providers.Cnbc;
using Meili.Data.Providers.Nasdaq;

namespace Meili.Data.Tests;

public class CboeTests
{
    [Fact]
    public void Parses_quote()
    {
        using var d = Fx.Json("cboe_quote.json");
        var q = CboeProvider.ParseQuote(d.RootElement, "AAPL")!;
        Assert.Equal(356.3473, q.Price, 4);
        Assert.Equal(348.51, q.PreviousClose!.Value, 2);
        Assert.Equal(15, q.DelayMinutes);
        Assert.Equal(0.21772, CboeProvider.ParseIv30(d.RootElement)!.Value, 5);
        Assert.Equal(0.21772, q.ImpliedVolatility30!.Value, 5); // B1: carried on the quote
        Assert.Equal(new DateTime(2026, 11, 11, 15, 59, 59), MarketClock.ToEastern(q.AsOf!.Value));
    }

    [Fact]
    public void Parses_whole_option_chain_with_greeks()
    {
        using var d = Fx.Json("cboe_options.json");
        var c = CboeProvider.ParseOptions(d.RootElement, "AAPL", new DateOnly(2026, 11, 1));
        Assert.Equal(120, c.Contracts.Count);
        Assert.Equal(2, c.Expirations.Count);
        Assert.Contains(c.Contracts, x => x.Delta is not null && x.Gamma is not null && x.Vega is not null && x.ImpliedVolatility is > 0);
        Assert.All(c.Contracts, x => Assert.True(x.ImpliedVolatility is null || x.Delta is not null));
        Assert.All(c.Contracts.Where(x => x.ImpliedVolatility is null), x => Assert.Null(x.Gamma));
        var call = c.Contracts.First(x => x.ContractSymbol == "AAPL261111C00270000");
        Assert.Equal((OptionRight.Call, 270.0, true), (call.Right, call.Strike, call.InTheMoney!.Value));
        // expired series are dropped
        Assert.DoesNotContain(CboeProvider.ParseOptions(d.RootElement, "AAPL", new DateOnly(2027, 1, 1)).Contracts, x => x.Expiration < new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Parses_history_and_intraday()
    {
        using var h = Fx.Json("cboe_historical.json");
        var daily = CboeProvider.ParseHistorical(h.RootElement, "AAPL");
        Assert.Equal(60, daily.Bars.Count);
        using var i = Fx.Json("cboe_intraday.json");
        var intra = CboeProvider.ParseIntraday(i.RootElement, "AAPL");
        Assert.Equal(60, intra.Bars.Count);
        Assert.Equal(new TimeSpan(9, 30, 0), MarketClock.ToEastern(intra.Bars[0].Time).TimeOfDay);
        Assert.True(intra.Bars[0].Volume > 0);
    }

    [Theory]
    [InlineData("^SPX", "_SPX")]
    [InlineData("^GSPC", "_SPX")]
    [InlineData("spx", "_SPX")]
    [InlineData("AAPL", "AAPL")]
    [InlineData("BRK-B", "BRK.B")]
    public void Maps_symbols(string s, string expected) => Assert.Equal(expected, CboeProvider.ToCboe(s));
}

public class CnbcTests
{
    [Fact]
    public void Parses_batch_quotes_with_extended_hours()
    {
        using var d = Fx.Json("cnbc_quote.json");
        var rows = CnbcProvider.Rows(d.RootElement).ToList();
        Assert.Equal(2, rows.Count);
        var q = CnbcProvider.ParseQuote(rows[0])!;
        Assert.Equal(("AAPL", 355.63, 348.51), (q.Symbol, q.Price, q.PreviousClose!.Value));
        Assert.Equal(MarketState.AfterHours, q.MarketState);
        Assert.Equal(356.35, q.ExtendedPrice!.Value, 2);
        Assert.True(q.IsRealtime);
        Assert.InRange(q.MarketCap!.Value, 5.16e12, 5.17e12);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 21, 0, 0, TimeSpan.Zero), q.AsOf);
    }

    [Fact]
    public void Parses_fundamentals()
    {
        using var d = Fx.Json("cnbc_quote.json");
        var f = CnbcProvider.ParseFundamentals(CnbcProvider.Rows(d.RootElement).First(), "AAPL");
        Assert.Equal(39.63, f.TrailingPE!.Value, 2);
        Assert.Equal(0.003, f.DividendYield!.Value, 4);
        Assert.InRange(f.SharesOutstanding!.Value, 1.45e10, 1.46e10);
    }

    [Fact]
    public void Maps_index_symbols() => Assert.Equal((".SPX", "BRK.B"), (CnbcProvider.ToCnbc("^GSPC"), CnbcProvider.ToCnbc("brk-b")));
}

public class NasdaqTests
{
    [Fact]
    public void Parses_info_quote_for_stock_and_etf()
    {
        using var s = Fx.Json("nasdaq_info_stock.json");
        var q = NasdaqProvider.ParseInfo(s.RootElement, "AAPL")!;
        Assert.Equal((355.63, 7.12, "Apple Inc."), (q.Price, q.Change!.Value, q.Name));
        Assert.Equal(348.51, q.PreviousClose!.Value, 2);
        Assert.Equal(355.38, q.FiftyTwoWeekHigh!.Value, 2);
        Assert.Equal(MarketState.Closed, q.MarketState);
        Assert.Null(q.Bid); // "N/A"
        using var e = Fx.Json("nasdaq_info_etf.json");
        Assert.Equal(759.84, NasdaqProvider.ParseInfo(e.RootElement, "SPY")!.Price, 2);
    }

    [Fact]
    public void Parses_intraday_points_as_new_york_time()
    {
        using var d = Fx.Json("nasdaq_chart.json");
        var s = NasdaqProvider.ParseChart(d.RootElement, "AAPL");
        Assert.Equal(80, s.Bars.Count);
        Assert.Equal(new DateTime(2026, 11, 11, 4, 0, 0), MarketClock.ToEastern(s.Bars[0].Time));
    }

    [Fact]
    public void Parses_history_in_ascending_order()
    {
        using var d = Fx.Json("nasdaq_historical.json");
        var s = NasdaqProvider.ParseHistorical(d.RootElement, "AAPL");
        Assert.Equal(43, s.Bars.Count);
        Assert.True(s.Bars[0].Time < s.Bars[^1].Time);
        Assert.Equal(355.63, s.Bars[^1].Close, 2);
    }

    [Fact]
    public void Parses_option_chain_rows_into_calls_and_puts()
    {
        using var d = Fx.Json("nasdaq_option_chain.json");
        var c = NasdaqProvider.ParseOptionChain(d.RootElement, "AAPL", new DateOnly(2026, 11, 1));
        Assert.NotEmpty(c.Contracts);
        Assert.Equal(c.Contracts.Count(x => x.Right == OptionRight.Call), c.Contracts.Count(x => x.Right == OptionRight.Put));
        var call = c.Contracts.First(x => x.Right == OptionRight.Call && x.Strike == 270);
        Assert.Equal(("AAPL261111C00270000", 83.90, 5.0), (call.ContractSymbol, call.Bid!.Value, call.OpenInterest!.Value));
        Assert.Equal(new DateOnly(2026, 11, 11), call.Expiration);
        Assert.Equal(355.63, c.UnderlyingPrice!.Value, 2);
    }

    [Fact]
    public void Parses_summary_financials_and_news()
    {
        using var s = Fx.Json("nasdaq_summary.json");
        var f = NasdaqProvider.ParseSummary(s.RootElement, "AAPL");
        Assert.Equal(("Technology", 350.0), (f.Sector, f.TargetPrice!.Value));
        Assert.Equal(5168079884660, f.MarketCap!.Value, 0);
        using var fin = Fx.Json("nasdaq_financials.json");
        var fs = NasdaqProvider.ParseFinancials(fin.RootElement, "AAPL");
        Assert.Contains(fs.Lines, l => l.Item == FinancialItems.TotalRevenue && l.PeriodEnd == new DateOnly(2026, 9, 26) && l.Value == 478_834_000_000);
        Assert.Contains(fs.Lines, l => l.Item == FinancialItems.FreeCashFlow);
        using var n = Fx.Json("nasdaq_news.json");
        var news = NasdaqProvider.ParseNews(n.RootElement);
        Assert.Equal(10, news.Count);
        Assert.All(news, x => Assert.StartsWith("https://www.nasdaq.com/", x.Url));
    }
}
/// <summary>C3: chain refresh cadence and the .NET parse cost of an AAPL-sized CBOE chain.</summary>
public class CboeChainCostTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Cache_expires_just_before_each_refresh_tick()
    {
        Assert.InRange(RefreshCadence.OptionChain.TotalSeconds, 60, 120);
        Assert.True(RefreshCadence.OptionChainCache < RefreshCadence.OptionChain);
        Assert.True(RefreshCadence.OptionChainCache > RefreshCadence.OptionChain / 2);
    }

    [Fact]
    public void Parses_an_aapl_sized_chain_quickly()
    {
        // 3,492 contracts ≈ 1.5 MB, the size measured for AAPL on 2026-11-12.
        var sb = new System.Text.StringBuilder("{\"timestamp\":\"2026-11-12 20:00:00\",\"data\":{\"current_price\":230.5,\"options\":[");
        var n = 0;
        for (var e = 0; e < 18 && n < 3492; e++)
            for (var k = 0; k < 97 && n < 3492; k++)
                foreach (var r in "CP")
                {
                    if (n > 0) sb.Append(',');
                    var exp = new DateOnly(2026, 11, 20).AddDays(7 * e);
                    var strike = 150 + k * 2.5;
                    sb.Append($"{{\"option\":\"AAPL{exp:yyMMdd}{r}{(int)(strike * 1000):00000000}\",\"bid\":1.25,\"bid_size\":10,\"ask\":1.35,\"ask_size\":12,\"iv\":0.2834,\"open_interest\":1520,\"volume\":340,\"delta\":0.4512,\"gamma\":0.0213,\"vega\":0.1834,\"theta\":-0.0712,\"rho\":0.0341,\"theo\":1.3,\"change\":0.05,\"percent_change\":3.85,\"last_trade_price\":1.31,\"last_trade_time\":\"2026-11-12T15:59:58\",\"tick\":\"up\",\"prev_day_close\":1.26,\"high\":1.4,\"low\":1.1,\"open\":1.2}}");
                    n++;
                }
        sb.Append("]}}");
        var json = sb.ToString();
        CboeProvider.ParseOptions(System.Text.Json.JsonDocument.Parse(json).RootElement, "AAPL", new DateOnly(2026, 11, 12)); // warm-up (JIT)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var chain = CboeProvider.ParseOptions(doc.RootElement, "AAPL", new DateOnly(2026, 11, 12));
        var ms = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"C3 baseline: {json.Length:N0} chars, {chain.Contracts.Count} contracts, parse {ms:0} ms");
        Assert.Equal(3492, chain.Contracts.Count);
        Assert.True(ms < 1500, $"parse took {ms:0} ms");
    }
}
