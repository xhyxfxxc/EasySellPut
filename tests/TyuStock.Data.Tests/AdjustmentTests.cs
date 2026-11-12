using System.Text.Json;
using TyuStock.Core.Models;
using TyuStock.Data.Providers.Yahoo;

namespace TyuStock.Data.Tests;

/// <summary>F7: Yahoo bars are split-adjusted; Total mode rescales OHLC by adjclose/close.</summary>
public class AdjustmentTests
{
    private const string Chart = """
        {"chart":{"result":[{"meta":{"regularMarketTime":1762981200},
          "timestamp":[1762808400,1762894800,1762981200],
          "indicators":{"quote":[{"open":[100,102,104],"high":[101,103,105],"low":[99,101,103],"close":[100,102,104],"volume":[10,20,30]}],
                        "adjclose":[{"adjclose":[98,101,104]}]}}]}}
        """;

    [Fact]
    public void Split_mode_keeps_prices_and_labels_split()
    {
        using var d = JsonDocument.Parse(Chart);
        var s = YahooProvider.ParseChart(d.RootElement, "AAPL", BarInterval.Day1);
        Assert.Equal(PriceAdjustment.Split, s.Adjustment);
        Assert.Equal(100, s.Bars[0].Close);
    }

    [Fact]
    public void Total_mode_scales_every_field_by_adjclose()
    {
        using var d = JsonDocument.Parse(Chart);
        var s = YahooProvider.ParseChart(d.RootElement, "AAPL", BarInterval.Day1, AdjustMode.Total);
        Assert.Equal(PriceAdjustment.SplitDividend, s.Adjustment);
        var b = s.Bars[0];
        Assert.Equal(98, b.Close, 9);
        Assert.Equal(100 * 0.98, b.Open, 9);
        Assert.Equal(101 * 0.98, b.High, 9);
        Assert.Equal(99 * 0.98, b.Low, 9);
        Assert.Equal(10, b.Volume);                       // volume is not price-adjusted
        Assert.Equal(104, s.Bars[2].Close, 9);            // latest bar unchanged
        Assert.True(b.IsConsistent());
    }

    [Fact]
    public void Intraday_ignores_total_mode()
    {
        using var d = JsonDocument.Parse(Chart);
        var s = YahooProvider.ParseChart(d.RootElement, "AAPL", BarInterval.Minute5, AdjustMode.Total);
        Assert.Equal(PriceAdjustment.Split, s.Adjustment);
        Assert.Equal(100, s.Bars[0].Close);
    }

    [Fact]
    public void Cache_keys_keep_pre_F7_entries_for_split_mode()
    {
        Assert.Equal("", AdjustMode.Split.CacheKey());
        Assert.Equal("total", AdjustMode.Total.CacheKey());
    }
}
