namespace TyuStock.Analytics.Tests;

public class KlineSnapshotTests
{
    [Fact]
    public void Compute_AlignsEverySeries_AndWarmupIsNaN()
    {
        var bars = Rising(40, start: 100, step: 1);
        var snap = KlineSnapshot.Compute(bars, new KlineIndicatorOptions { SmaPeriod = 5, EmaPeriod = 5, BollingerPeriod = 5, RsiPeriod = 5 });
        Assert.Equal(40, snap.Columns.Count);
        Assert.Equal(40, snap.Sma.Length);
        Assert.Equal(40, snap.Ema.Length);
        Assert.Equal(40, snap.Rsi.Length);
        Assert.Equal(40, snap.Bollinger.Middle.Length);
        Assert.Equal(40, snap.Macd.Macd.Length);
        Assert.Equal(40, snap.Kdj.K.Length);
        Assert.True(double.IsNaN(snap.Sma[3]));
        Assert.True(double.IsFinite(snap.Sma[4]));
        // SMA(5) of 100..104 = 102
        Assert.Equal(102, snap.Sma[4], 12);
        Assert.Equal(Indicators.Sma(snap.Columns.Close, 5)[20], snap.Sma[20], 12);
        Assert.Equal(Indicators.Ema(snap.Columns.Close, 5)[20], snap.Ema[20], 12);
        Assert.Equal(Indicators.Rsi(snap.Columns.Close, 5)[20], snap.Rsi[20], 12);
    }

    [Fact]
    public void Compute_Empty_ReturnsEmptyAlignedSeries()
    {
        var snap = KlineSnapshot.Compute(Array.Empty<OhlcvBar>());
        Assert.Equal(0, snap.Columns.Count);
        Assert.Empty(snap.Sma);
        Assert.Empty(snap.Rsi);
        Assert.Empty(snap.Macd.Histogram);
    }

    [Fact]
    public void CopyDefined_DropsNaN_KeepsOrder()
    {
        var time = Enumerable.Range(0, 5).Select(i => new DateTimeOffset(2026, 1, 1 + i, 0, 0, 0, TimeSpan.Zero)).ToArray();
        var values = new[] { double.NaN, 1, double.NaN, 2, 3 };
        var xs = new List<double>();
        var ys = new List<double>();
        KlineSnapshot.CopyDefined(time, values, xs, ys);
        Assert.Equal([1, 2, 3], ys);
        Assert.Equal(3, xs.Count);
        Assert.True(xs[0] < xs[1] && xs[1] < xs[2]);
    }

    private static OhlcvBar[] Rising(int n, double start, double step)
    {
        var t0 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero); // Friday
        var bars = new OhlcvBar[n];
        for (int i = 0; i < n; i++)
        {
            var c = start + i * step;
            bars[i] = new OhlcvBar(t0.AddDays(i), c - 0.4, c + 0.5, c - 0.6, c, 1000 + i);
        }
        return bars;
    }
}