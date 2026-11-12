namespace TyuStock.Analytics;

/// <summary>Standard overlay/oscillator set for a K-line chart. Every series is aligned to the bar columns (warm-up is NaN).</summary>
public sealed class KlineIndicatorOptions
{
    public int SmaPeriod { get; init; } = 20;
    public int EmaPeriod { get; init; } = 20;
    public int BollingerPeriod { get; init; } = 20;
    public double BollingerMultiplier { get; init; } = 2.0;
    public int MacdFast { get; init; } = 12;
    public int MacdSlow { get; init; } = 26;
    public int MacdSignal { get; init; } = 9;
    public int RsiPeriod { get; init; } = 14;
    public int KdjPeriod { get; init; } = 9;
    public int KdjM1 { get; init; } = 3;
    public int KdjM2 { get; init; } = 3;
}

/// <summary>Computed MA, EMA, Bollinger, MACD, RSI and KDJ series, each the same length as the input bars.</summary>
public sealed class KlineSnapshot
{
    public required OhlcvColumns Columns { get; init; }
    public required double[] Sma { get; init; }
    public required double[] Ema { get; init; }
    public required BollingerSeries Bollinger { get; init; }
    public required MacdSeries Macd { get; init; }
    public required double[] Rsi { get; init; }
    public required KdjSeries Kdj { get; init; }
    public KlineIndicatorOptions Options { get; init; } = new();

    public static KlineSnapshot Compute(IReadOnlyList<OhlcvBar> bars, KlineIndicatorOptions? options = null) =>
        Compute(bars, options, null);

    /// <param name="bars">Bars in time order.</param>
    /// <param name="options">Indicator periods; defaults when null.</param>
    /// <param name="incomplete">
    /// Optional per-bar flag for bars whose High/Low are missing or untrustworthy. Close-based indicators still use
    /// every real close; KDJ (which needs High/Low) is computed over the complete bars only and realigned, with NaN
    /// at incomplete positions — a gap is shown instead of a value built from invented highs and lows.
    /// </param>
    public static KlineSnapshot Compute(IReadOnlyList<OhlcvBar> bars, KlineIndicatorOptions? options, IReadOnlyList<bool>? incomplete)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var cols = OhlcvColumns.From(bars);
        return Compute(cols, options, incomplete);
    }

    public static KlineSnapshot Compute(OhlcvColumns cols, KlineIndicatorOptions? options = null) => Compute(cols, options, null);

    public static KlineSnapshot Compute(OhlcvColumns cols, KlineIndicatorOptions? options, IReadOnlyList<bool>? incomplete)
    {
        ArgumentNullException.ThrowIfNull(cols);
        if (incomplete is not null && incomplete.Count != cols.Count) throw new ArgumentException("Flag count must match bar count.", nameof(incomplete));
        var opt = options ?? new KlineIndicatorOptions();
        var close = cols.Close;
        return new KlineSnapshot
        {
            Columns = cols,
            Options = opt,
            Sma = Indicators.Sma(close, opt.SmaPeriod),
            Ema = Indicators.Ema(close, opt.EmaPeriod),
            Bollinger = Indicators.Bollinger(close, opt.BollingerPeriod, opt.BollingerMultiplier),
            Macd = Indicators.Macd(close, opt.MacdFast, opt.MacdSlow, opt.MacdSignal),
            Rsi = Indicators.Rsi(close, opt.RsiPeriod),
            Kdj = KdjOnComplete(cols, opt, incomplete),
        };
    }

    private static KdjSeries KdjOnComplete(OhlcvColumns cols, KlineIndicatorOptions opt, IReadOnlyList<bool>? incomplete)
    {
        int n = cols.Count;
        var keep = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            double h = cols.High[i], l = cols.Low[i], c = cols.Close[i];
            bool ok = (incomplete is null || !incomplete[i]) && double.IsFinite(h) && double.IsFinite(l) && double.IsFinite(c) && l <= c && c <= h;
            if (ok) keep.Add(i);
        }
        if (keep.Count == n) return Indicators.Kdj(cols.High, cols.Low, cols.Close, opt.KdjPeriod, opt.KdjM1, opt.KdjM2);
        var k = Nan(n); var d = Nan(n); var j = Nan(n);
        if (keep.Count >= opt.KdjPeriod)
        {
            var h = keep.Select(i => cols.High[i]).ToArray();
            var l = keep.Select(i => cols.Low[i]).ToArray();
            var c = keep.Select(i => cols.Close[i]).ToArray();
            var sub = Indicators.Kdj(h, l, c, opt.KdjPeriod, opt.KdjM1, opt.KdjM2);
            for (int x = 0; x < keep.Count; x++) { k[keep[x]] = sub.K[x]; d[keep[x]] = sub.D[x]; j[keep[x]] = sub.J[x]; }
        }
        return new KdjSeries(k, d, j);

        static double[] Nan(int len) { var a = new double[len]; Array.Fill(a, double.NaN); return a; }
    }

    /// <summary>Pairs (time OADate, value) skipping NaN, for a ScottPlot scatter overlay.</summary>
    public static void CopyDefined(DateTimeOffset[] time, double[] values, List<double> xs, List<double> ys)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (time.Length != values.Length) throw new ArgumentException("Time and values must be the same length.");
        xs.Clear(); ys.Clear();
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsFinite(values[i]))
            {
                xs.Add(time[i].LocalDateTime.ToOADate());
                ys.Add(values[i]);
            }
        }
    }
}