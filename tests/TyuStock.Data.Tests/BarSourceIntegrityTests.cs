using System.Text.Json;
using TyuStock.Core.Models;
using TyuStock.Data.Providers.China;
using TyuStock.Data.Providers.Nasdaq;
using TyuStock.Data.Providers.Yahoo;

namespace TyuStock.Data.Tests;

/// <summary>F2: no provider invents O/H/L from the close.</summary>
public class BarSourceIntegrityTests
{
    [Fact]
    public void Yahoo_missing_open_high_low_stay_missing()
    {
        using var d = JsonDocument.Parse("""
        {"chart":{"result":[{"meta":{"regularMarketTime":1794258001},"timestamp":[1794240000,1794240060],
          "indicators":{"quote":[{"open":[null,10],"high":[null,10.4],"low":[null,10.5],"close":[10.2,10.3],"volume":[null,5]}]}}]}}
        """);
        var s = YahooProvider.ParseChart(d.RootElement, "AAPL", BarInterval.Minute1);
        Assert.Equal(2, s.Bars.Count);
        Assert.True(double.IsNaN(s.Bars[0].Open) && double.IsNaN(s.Bars[0].High) && double.IsNaN(s.Bars[0].Low));
        Assert.True(s.Bars[0].Incomplete);
        Assert.True(s.Bars[1].Incomplete);            // low 10.5 > open 10: contradictory, flagged
        Assert.False(s.IsRealtime);                   // charts carry no realtime declaration
    }

    [Fact]
    public void Intraday_tick_series_are_close_only()
    {
        using var m = Fx.Json("tencent_minute.json");
        var mins = TencentProvider.ParseMinute(m.RootElement, "usAAPL.OQ", "AAPL");
        Assert.All(mins.Bars, b => { Assert.True(b.Incomplete); Assert.True(double.IsNaN(b.High)); });
        using var n = Fx.Json("nasdaq_chart.json");
        var ns = NasdaqProvider.ParseChart(n.RootElement, "AAPL");
        Assert.All(ns.Bars, b => Assert.True(b.Incomplete));
        Assert.False(ns.IsRealtime);
    }

    [Fact]
    public void Real_daily_bars_from_fixtures_stay_complete()
    {
        using var k = Fx.Json("tencent_kline.json");
        var s = TencentProvider.ParseKline(k.RootElement, "usAAPL.OQ", "AAPL", BarInterval.Day1);
        Assert.All(s.Bars, b => Assert.False(b.Incomplete));
        using var y = Fx.Json("yahoo_chart_1d.json");
        Assert.All(YahooProvider.ParseChart(y.RootElement, "SPY", BarInterval.Day1).Bars, b => Assert.Equal(!b.IsConsistent(), b.Incomplete));
    }

    [Fact]
    public void Sina_daily_without_ohl_is_flagged_not_filled()
    {
        var s = SinaProvider.ParseDaily("""x([{"d":"2026-11-10","c":"10.5","v":"100"},{"d":"2026-11-11","o":"10","h":"11","l":"9","c":"10.5","v":"1"}])""", "AAPL");
        Assert.True(s.Bars[0].Incomplete);
        Assert.True(double.IsNaN(s.Bars[0].Open));
        Assert.False(s.Bars[1].Incomplete);
    }
}
