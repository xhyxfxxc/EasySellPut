using System.Text;
using TyuStock.Core.Models;
using TyuStock.Core.Time;
using TyuStock.Data.Providers.China;
using TyuStock.Data.Providers.Sec;

namespace TyuStock.Data.Tests;

public class ChinaProviderTests
{
    static ChinaProviderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private static string Gbk(string name) => Encoding.GetEncoding(936).GetString(Fx.Bytes(name));

    [Fact]
    public void Sina_parses_gbk_quotes_including_share_classes()
    {
        var back = new Dictionary<string, string> { ["aapl"] = "AAPL", ["spy"] = "SPY", ["brk$b"] = "BRK.B" };
        var q = SinaProvider.ParseQuotes(Gbk("sina_quote.txt"), back);
        Assert.Equal(["AAPL", "SPY", "BRK.B"], q.Select(x => x.Symbol));
        Assert.Equal(("苹果", 355.63, 348.51, 7.12), (q[0].Name, q[0].Price, q[0].PreviousClose!.Value, q[0].Change!.Value));
        Assert.Equal(2.04, q[0].ChangePercent!.Value, 2);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 9, 16, 15, TimeSpan.FromHours(8)), q[0].AsOf);
        Assert.Equal("brk$b", SinaProvider.ToSina("BRK.B"));
    }

    [Fact]
    public void Sina_parses_jsonp_daily_history()
    {
        var s = SinaProvider.ParseDaily(Fx.Text("sina_dailyk.txt"), "AAPL");
        Assert.Equal(80, s.Bars.Count);
        Assert.Equal(new DateTime(2026, 7, 22), MarketClock.ToEastern(s.Bars[0].Time).Date);
        Assert.Equal(325.89, s.Bars[0].Close, 2);
    }

    [Fact]
    public void Tencent_parses_quotes_and_learns_exchange_suffix()
    {
        var back = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usAAPL"] = "AAPL", ["usSPY"] = "SPY" };
        var q = TencentProvider.ParseQuotes(Gbk("tencent_quote.txt"), back, out var codes);
        Assert.Equal(2, q.Count);
        Assert.Equal(("AAPL", "Apple Inc.", 355.63, 348.51), (q[0].Symbol, q[0].Name, q[0].Price, q[0].PreviousClose!.Value));
        Assert.Equal((356.13, 356.40), (q[0].Bid!.Value, q[0].Ask!.Value));
        Assert.InRange(q[0].MarketCap!.Value, 5.16e12, 5.17e12);
        Assert.Equal("usAAPL.OQ", codes["AAPL"]);
        Assert.Equal("usSPY.AM", codes["SPY"]);
        Assert.Equal(new DateTime(2026, 11, 11, 16, 0, 1), MarketClock.ToEastern(q[0].AsOf!.Value));
    }

    [Fact]
    public void Tencent_parses_kline_in_open_close_high_low_order_and_minutes()
    {
        using var k = Fx.Json("tencent_kline.json");
        var s = TencentProvider.ParseKline(k.RootElement, "usAAPL.OQ", "AAPL", BarInterval.Day1);
        Assert.Equal(60, s.Bars.Count);
        var last = s.Bars[^1];
        Assert.Equal((349.55, 355.91, 349.33, 355.63), (last.Open, last.High, last.Low, last.Close));
        using var m = Fx.Json("tencent_minute.json");
        var mins = TencentProvider.ParseMinute(m.RootElement, "usAAPL.OQ", "AAPL");
        Assert.Equal(391, mins.Bars.Count);
        Assert.Equal(new DateTime(2026, 11, 11, 9, 30, 0), MarketClock.ToEastern(mins.Bars[0].Time));
        Assert.Equal(34453477, mins.Bars.Sum(b => b.Volume), 0);
    }

    [Fact]
    public void Eastmoney_descales_integer_prices()
    {
        using var d = Fx.Json("eastmoney_quote.json");
        var q = EastmoneyProvider.ParseQuote(d.RootElement, "AAPL")!;
        Assert.Equal((355.63, 355.91, 349.325, 348.51, 7.12, 2.04), (q.Price, q.High!.Value, q.Low!.Value, q.PreviousClose!.Value, q.Change!.Value, q.ChangePercent!.Value));
        Assert.Equal("苹果", q.Name);
        using var k = Fx.Json("eastmoney_kline.json");
        var s = EastmoneyProvider.ParseKline(k.RootElement, "AAPL", BarInterval.Day1);
        Assert.Equal(60, s.Bars.Count);
        Assert.Equal((310.14, 319.28, 309.6, 316.83), (s.Bars[0].Open, s.Bars[0].High, s.Bars[0].Low, s.Bars[0].Close));
        Assert.Equal("BRK_B", EastmoneyProvider.ToEastmoney("brk.b"));
    }
}

public class SecTests
{
    [Fact]
    public void Searches_ticker_table()
    {
        using var d = Fx.Json("sec_tickers.json");
        var all = SecProvider.ParseTickers(d.RootElement);
        Assert.Equal(8, all.Count);
        Assert.Equal("AAPL", SecProvider.Search(all, "aapl", 5)[0].Symbol);
        Assert.Equal("BRK.B", SecProvider.Search(all, "BRK.B", 5)[0].Symbol);
        Assert.Contains(SecProvider.Search(all, "nvidia", 5), m => m.Symbol == "NVDA");
    }

    [Fact]
    public void Extracts_annual_statements_from_xbrl_facts()
    {
        using var d = Fx.Json("sec_companyfacts.json");
        var fs = SecProvider.ParseCompanyFacts(d.RootElement, "AAPL");
        var revenue = fs.Lines.Where(l => l.Item == FinancialItems.TotalRevenue).ToList();
        Assert.InRange(revenue.Count, 3, 6);
        Assert.All(revenue, l => Assert.True(l.Value > 2e11));
        Assert.Equal(revenue.Count, revenue.Select(l => l.PeriodEnd).Distinct().Count());
        Assert.All(fs.Lines.Where(l => l.Item == FinancialItems.CapitalExpenditure), l => Assert.True(l.Value < 0));
        Assert.Contains(fs.Lines, l => l.Item == FinancialItems.FreeCashFlow);
        Assert.Contains(fs.Lines, l => l.Item == FinancialItems.TotalAssets);
    }
}