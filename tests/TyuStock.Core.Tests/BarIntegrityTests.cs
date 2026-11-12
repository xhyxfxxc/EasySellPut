using TyuStock.Core.Export;
using TyuStock.Core.Models;
using TyuStock.Core.Time;

namespace TyuStock.Core.Tests;

/// <summary>F2: bars are never fabricated; missing/contradictory O/H/L are flagged, not invented.</summary>
public class BarIntegrityTests
{
    private static readonly DateTimeOffset T0 = MarketClock.FromEastern(new DateTime(2026, 11, 11, 9, 30, 0));

    [Fact]
    public void Missing_fields_stay_NaN_and_flag_the_bar()
    {
        var b = Bar.FromSource(T0, null, 11, null, 10.5, null);
        Assert.True(double.IsNaN(b.Open));
        Assert.True(double.IsNaN(b.Low));
        Assert.Equal(11, b.High);
        Assert.Equal(0, b.Volume);
        Assert.True(b.Incomplete);
    }

    [Fact]
    public void Contradictory_bars_are_flagged_but_values_are_kept()
    {
        var b = Bar.FromSource(T0, 10, 11, 10.2, 10.6, 100); // Low above Open
        Assert.True(b.Incomplete);
        Assert.Equal(10.2, b.Low);
        Assert.False(b.IsConsistent());
        Assert.False(Bar.FromSource(T0, 10, 10.4, 9.5, 10.6, 1).IsConsistent()); // High below Close
    }

    [Fact]
    public void Consistent_bars_are_complete_and_zero_or_negative_prices_count_as_missing()
    {
        Assert.False(Bar.FromSource(T0, 10, 11, 9, 10.5, 100).Incomplete);
        var z = Bar.FromSource(T0, 0, 11, -1, 10.5, 100);
        Assert.True(z.Incomplete);
        Assert.True(double.IsNaN(z.Open) && double.IsNaN(z.Low));
    }

    [Fact]
    public void Close_only_points_have_no_ohlc()
    {
        var b = Bar.CloseOnly(T0, 10, 5);
        Assert.True(b.Incomplete);
        Assert.True(double.IsNaN(b.Open) && double.IsNaN(b.High) && double.IsNaN(b.Low));
    }

    [Fact]
    public void Aggregator_ignores_NaN_extremes_and_propagates_the_flag()
    {
        var bars = new[]
        {
            Bar.FromSource(T0, 10, 11, 9.5, 10.5, 100),
            Bar.CloseOnly(T0.AddMinutes(1), 12, 50),
            Bar.FromSource(T0.AddMinutes(2), 10.6, 10.8, 9, 10.7, 10),
        };
        var agg = BarAggregator.Aggregate(bars, BarInterval.Minute5);
        var a = Assert.Single(agg);
        Assert.Equal(10, a.Open);
        Assert.Equal(11, a.High);   // the close-only 12 is not an observed high
        Assert.Equal(9, a.Low);
        Assert.Equal(10.7, a.Close);
        Assert.Equal(160, a.Volume);
        Assert.True(a.Incomplete);

        var clean = BarAggregator.Aggregate([bars[0], bars[2]], BarInterval.Minute5);
        Assert.False(clean[0].Incomplete);
    }

    [Fact]
    public void Export_writes_empty_cells_for_missing_values_and_marks_incomplete_rows()
    {
        var series = new BarSeries
        {
            Symbol = "AAPL", Interval = BarInterval.Minute1,
            Bars = [Bar.CloseOnly(T0, 10.5, 3), Bar.FromSource(T0.AddMinutes(1), 10, 11, 9, 10.5, 1)],
        };
        var t = MarketExport.Bars("K", ["T", "O", "H", "L", "C", "V", "Inc"], series);
        Assert.Equal(["", "", "", "10.5", "3", "1"], t.Rows[0].Skip(1));
        Assert.Equal("", t.Rows[1][6]);
        Assert.Equal(6, MarketExport.Bars("K", ["T", "O", "H", "L", "C", "V"], series).Rows[0].Count);
    }
}
