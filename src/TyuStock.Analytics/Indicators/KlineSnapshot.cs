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

    public static KlineSnapshot Compute(IReadOnlyList<OhlcvBar> bars, KlineIndicatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var opt = options ?? new KlineIndicatorOptions();
        var cols = OhlcvColumns.From(bars);
        return Compute(cols, opt);
    }

    public static KlineSnapshot Compute(OhlcvColumns cols, KlineIndicatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cols);
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
            Kdj = Indicators.Kdj(cols.High, cols.Low, close, opt.KdjPeriod, opt.KdjM1, opt.KdjM2),
        };
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