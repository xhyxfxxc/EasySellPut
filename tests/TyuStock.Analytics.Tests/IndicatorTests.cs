using TyuStock.Analytics;

namespace TyuStock.Analytics.Tests;

public class IndicatorTests
{
    // Same deterministic series as reference/reference_values.py (pure-Python reference implementation used to produce the expected values).
    private static readonly double[] X = Enumerable.Range(0, 60).Select(i => 100 + 10 * Math.Sin(i / 5.0) + 0.3 * i).ToArray();
    private static readonly double[] H = X.Select((v, i) => v + 1 + Math.Abs(Math.Sin(i))).ToArray();
    private static readonly double[] L = X.Select((v, i) => v - 1 - Math.Abs(Math.Cos(i))).ToArray();
    private const double Tol = 1e-9;

    [Fact]
    public void Ema_matches_python()
    {
        var e = Indicators.Ema(X, 12);
        Assert.True(double.IsNaN(e[10]));
        Assert.Equal(108.58355091893901, e[11], Tol);
        Assert.Equal(101.76995478199215, e[30], Tol);
        Assert.Equal(109.63118927188644, e[59], Tol);
    }

    [Fact]
    public void Rsi_matches_python()
    {
        var r = Indicators.Rsi(X, 14);
        Assert.True(double.IsNaN(r[13]));
        Assert.Equal(71.78637535164641, r[14], Tol);
        Assert.Equal(66.81660886167468, r[30], Tol);
        Assert.Equal(51.35917226541754, r[59], Tol);
    }

    [Fact]
    public void Macd_matches_python()
    {
        var m = Indicators.Macd(X);
        Assert.True(double.IsNaN(m.Macd[24]));
        Assert.Equal(-4.697796685081215, m.Macd[25], Tol);
        Assert.Equal(4.06917778501527, m.Macd[40], Tol);
        Assert.Equal(-1.149125800218826, m.Macd[59], Tol);
        Assert.True(double.IsNaN(m.Signal[32]));
        Assert.Equal(-2.8005204646077066, m.Signal[33], Tol);
        Assert.Equal(-0.630963601250321, m.Signal[59], Tol);
        Assert.Equal(m.Macd[59] - m.Signal[59], m.Histogram[59], 1e-12);
        var cn = Indicators.Macd(X, histogramMultiplier: 2);
        Assert.Equal(2 * (m.Macd[59] - m.Signal[59]), cn.Histogram[59], 1e-12);
    }

    [Fact]
    public void Atr_matches_python()
    {
        var a = Indicators.Atr(H, L, X, 14);
        Assert.True(double.IsNaN(a[13]));
        Assert.Equal(3.41812860833074, a[14], Tol);
        Assert.Equal(3.3616105836368844, a[59], Tol);
    }

    [Fact]
    public void Kdj_matches_python()
    {
        var k = Indicators.Kdj(H, L, X);
        Assert.True(double.IsNaN(k.K[7]));
        Assert.Equal(62.61957725522274, k.K[8], Tol); Assert.Equal(54.20652575174091, k.D[8], Tol); Assert.Equal(79.44568026218639, k.J[8], Tol);
        Assert.Equal(13.916051528234, k.K[20], Tol); Assert.Equal(20.813850933564936, k.D[20], Tol); Assert.Equal(0.12045271757212817, k.J[20], Tol);
        Assert.Equal(53.13260384749731, k.K[59], Tol); Assert.Equal(35.273488812158554, k.D[59], Tol); Assert.Equal(88.85083391817481, k.J[59], Tol);
    }

    [Fact]
    public void Bollinger_matches_python_and_brute_force()
    {
        var b = Indicators.Bollinger(X);
        Assert.Equal(112.76634792805866, b.Middle[59], Tol);
        Assert.Equal(123.89463630613166, b.Upper[59], Tol);
        Assert.Equal(101.63805954998567, b.Lower[59], Tol);
        for (int i = 19; i < X.Length; i++)
        {
            var w = X[(i - 19)..(i + 1)];
            double mu = w.Average(), sd = Math.Sqrt(w.Sum(v => (v - mu) * (v - mu)) / 20);
            Assert.Equal(mu, b.Middle[i], 1e-9); Assert.Equal(mu + 2 * sd, b.Upper[i], 1e-9); Assert.Equal(mu - 2 * sd, b.Lower[i], 1e-9);
        }
        var pb = b.PercentB(X);
        Assert.Equal((X[59] - b.Lower[59]) / (b.Upper[59] - b.Lower[59]), pb[59], 1e-12);
        Assert.True(double.IsNaN(pb[5]));
        Assert.Equal((b.Upper[59] - b.Lower[59]) / b.Middle[59], b.Bandwidth()[59], 1e-12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void Window_indicators_match_brute_force(int p)
    {
        var sma = Indicators.Sma(X, p); var wma = Indicators.Wma(X, p);
        var hi = Indicators.Highest(H, p); var lo = Indicators.Lowest(L, p);
        for (int i = 0; i < X.Length; i++)
        {
            if (i < p - 1) { Assert.True(double.IsNaN(sma[i]) && double.IsNaN(wma[i]) && double.IsNaN(hi[i]) && double.IsNaN(lo[i])); continue; }
            var w = X[(i - p + 1)..(i + 1)];
            Assert.Equal(w.Average(), sma[i], 1e-9);
            Assert.Equal(w.Select((v, j) => (j + 1) * v).Sum() / (p * (p + 1) / 2.0), wma[i], 1e-9);
            Assert.Equal(H[(i - p + 1)..(i + 1)].Max(), hi[i]);
            Assert.Equal(L[(i - p + 1)..(i + 1)].Min(), lo[i]);
        }
    }

    [Fact]
    public void Long_period_rolling_extreme_uses_pooled_buffer_correctly()
    {
        var rnd = new Random(7);
        var s = Enumerable.Range(0, 1500).Select(_ => rnd.NextDouble() * 100).ToArray();
        var hi = Indicators.Highest(s, 300);
        for (int i = 299; i < s.Length; i += 37) Assert.Equal(s[(i - 299)..(i + 1)].Max(), hi[i]);
    }

    [Fact]
    public void Stochastic_fast_and_slow_match_brute_force()
    {
        var fast = Indicators.Stochastic(H, L, X, 14, 1, 3);
        var slow = Indicators.Stochastic(H, L, X, 14, 3, 3);
        double Raw(int i) { double hh = H[(i - 13)..(i + 1)].Max(), ll = L[(i - 13)..(i + 1)].Min(); return 100 * (X[i] - ll) / (hh - ll); }
        for (int i = 13; i < X.Length; i++) Assert.Equal(Raw(i), fast.K[i], 1e-9);
        for (int i = 15; i < X.Length; i++) Assert.Equal((Raw(i) + Raw(i - 1) + Raw(i - 2)) / 3, slow.K[i], 1e-9);
        Assert.Equal((slow.K[59] + slow.K[58] + slow.K[57]) / 3, slow.D[59], 1e-9);
        Assert.True(double.IsNaN(slow.D[16]));
        Assert.False(double.IsNaN(slow.D[17]));
    }

    [Fact]
    public void Wilder_ma_equals_rsi_style_smoothing()
    {
        var w = Indicators.WilderMa(X, 10);
        double v = X[..10].Average();
        Assert.Equal(v, w[9], 1e-12);
        for (int i = 10; i < X.Length; i++) { v = (v * 9 + X[i]) / 10; Assert.Equal(v, w[i], 1e-9); }
    }

    [Fact]
    public void Missing_values_do_not_poison_series()
    {
        var s = X.ToArray(); s[30] = double.NaN;
        var sma = Indicators.Sma(s, 5);
        for (int i = 30; i <= 34; i++) Assert.True(double.IsNaN(sma[i]));
        Assert.Equal(s[31..36].Average(), sma[35], 1e-9);

        var ema = Indicators.Ema(s, 5);
        Assert.True(double.IsNaN(ema[30]));
        Assert.Equal(ema[29] + (2.0 / 6) * (s[31] - ema[29]), ema[31], 1e-12); // state carried across the gap

        var rsi = Indicators.Rsi(s, 14);
        Assert.True(double.IsNaN(rsi[30]));
        Assert.False(double.IsNaN(rsi[31]));
        Assert.False(double.IsNaN(Indicators.Bollinger(s, 5).Middle[35]));
    }

    [Fact]
    public void Rsi_edge_cases()
    {
        Assert.Equal(50.0, Indicators.Rsi(Enumerable.Repeat(10.0, 20).ToArray(), 14)[19]);
        Assert.Equal(100.0, Indicators.Rsi(Enumerable.Range(0, 20).Select(i => (double)i).ToArray(), 14)[19]);
        Assert.Equal(0.0, Indicators.Rsi(Enumerable.Range(0, 20).Select(i => -(double)i).ToArray(), 14)[19]);
    }

    [Fact]
    public void Obv_and_true_range_hand_values()
    {
        double[] c = { 10, 11, 11, 10, 12 }, v = { 100, 200, 300, 400, 500 };
        Assert.Equal(new double[] { 0, 200, 200, -200, 300 }, Indicators.Obv(c, v));
        double[] h = { 11, 12, 13 }, l = { 9, 10.5, 12.5 }, cl = { 10, 12, 13 };
        Assert.Equal(new double[] { 2, 2, 1 }, Indicators.TrueRange(h, l, cl));
    }

    [Fact]
    public void Vwap_resets_each_new_york_day()
    {
        var t0 = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero); // 09:30 ET
        DateTimeOffset[] time = { t0, t0.AddMinutes(1), t0.AddDays(1), t0.AddDays(1).AddMinutes(1) };
        double[] h = { 11, 13, 21, 23 }, l = { 9, 11, 19, 21 }, c = { 10, 12, 20, 22 }, vol = { 100, 300, 50, 0 };
        var vw = Indicators.Vwap(time, h, l, c, vol);
        Assert.Equal(10, vw[0], 1e-12);
        Assert.Equal((10 * 100 + 12 * 300) / 400.0, vw[1], 1e-12);
        Assert.Equal(20, vw[2], 1e-12);
        Assert.Equal(20, vw[3], 1e-12); // zero volume keeps the session VWAP
    }

    [Fact]
    public void Columns_and_argument_validation()
    {
        var bars = X.Select((v, i) => new OhlcvBar(DateTimeOffset.UnixEpoch.AddDays(i), v, H[i], L[i], v, 1000)).ToList();
        var cols = OhlcvColumns.From(bars);
        Assert.Equal(60, cols.Count);
        Assert.Equal((H[3] + L[3] + X[3]) / 3, OhlcvColumns.Extract(bars.ToArray(), PriceField.Hlc3)[3], 1e-12);
        Assert.Throws<ArgumentOutOfRangeException>(() => Indicators.Sma(X, 0));
        Assert.Throws<ArgumentException>(() => Indicators.Macd(X, 26, 12));
        Assert.Throws<ArgumentException>(() => Indicators.Sma(X, 5, new double[3]));
    }
}