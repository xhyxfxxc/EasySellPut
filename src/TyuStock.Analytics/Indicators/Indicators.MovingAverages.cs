namespace TyuStock.Analytics;

/// <summary>
/// Technical indicators over price/volume series.
/// <para>Conventions (all indicators):</para>
/// <list type="bullet">
/// <item>Output has the same length as the input and is index-aligned; positions without enough history (warm-up) are <see cref="double.NaN"/>.</item>
/// <item>Span overloads write into a caller-provided destination and allocate nothing (except a small pooled buffer for
/// rolling-extreme indicators with very long periods); array overloads allocate only the result.</item>
/// <item>Non-finite inputs (NaN/±Inf, e.g. missing bars) never poison the whole series: window-based indicators
/// (SMA/WMA/Bollinger/highest/lowest) output NaN while the bad value is inside the window and recover afterwards;
/// recursive indicators (EMA/RSI/ATR/KDJ/OBV/VWAP) output NaN at the bad point and carry their state across it.
/// Leading NaNs simply delay the warm-up.</item>
/// </list>
/// </summary>
public static partial class Indicators
{
    private static void Validate(int srcLength, int period, int dstLength, string dstName = "destination")
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be >= 1.");
        if (dstLength < srcLength) throw new ArgumentException("Destination is shorter than the source.", dstName);
    }

    private static void ValidateSameLength(int a, int b, int c)
    {
        if (a != b || a != c) throw new ArgumentException("Input series must have the same length.");
    }

    // ---------------------------------------------------------------- SMA

    /// <summary>Simple moving average. First value at index <c>period - 1</c>.</summary>
    public static void Sma(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        int lastBad = -1;
        bool rolling = false;
        double sum = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double x = source[i];
            if (!double.IsFinite(x)) { lastBad = i; rolling = false; destination[i] = double.NaN; continue; }
            if (i - lastBad < period) { destination[i] = double.NaN; continue; }
            if (!rolling)
            {
                sum = 0;
                for (int j = i - period + 1; j <= i; j++) sum += source[j];
                rolling = true;
            }
            else
            {
                sum += x - source[i - period];
            }
            destination[i] = sum / period;
        }
    }

    public static double[] Sma(ReadOnlySpan<double> source, int period)
    {
        var r = new double[source.Length];
        Sma(source, period, r);
        return r;
    }

    /// <summary>Moving average of volume (alias of <see cref="Sma(ReadOnlySpan{double}, int, Span{double})"/>, e.g. MAVOL5/10).</summary>
    public static void VolumeMa(ReadOnlySpan<double> volume, int period, Span<double> destination) => Sma(volume, period, destination);

    public static double[] VolumeMa(ReadOnlySpan<double> volume, int period) => Sma(volume, period);

    // ---------------------------------------------------------------- WMA

    /// <summary>Linearly weighted moving average (weights 1..period, newest heaviest). First value at index <c>period - 1</c>.</summary>
    public static void Wma(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        double denom = period * (period + 1) / 2.0;
        int lastBad = -1;
        bool rolling = false;
        double sum = 0, weighted = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double x = source[i];
            if (!double.IsFinite(x)) { lastBad = i; rolling = false; destination[i] = double.NaN; continue; }
            if (i - lastBad < period) { destination[i] = double.NaN; continue; }
            if (!rolling)
            {
                sum = 0; weighted = 0;
                int start = i - period + 1;
                for (int j = 0; j < period; j++)
                {
                    double v = source[start + j];
                    sum += v;
                    weighted += (j + 1) * v;
                }
                rolling = true;
            }
            else
            {
                // Shift the window by one: every old weight drops by 1 (subtract old sum), newest gets weight = period.
                weighted += period * x - sum;
                sum += x - source[i - period];
            }
            destination[i] = weighted / denom;
        }
    }

    public static double[] Wma(ReadOnlySpan<double> source, int period)
    {
        var r = new double[source.Length];
        Wma(source, period, r);
        return r;
    }

    // ---------------------------------------------------------------- EMA / Wilder

    /// <summary>
    /// Exponential moving average, alpha = 2 / (period + 1), seeded with the SMA of the first <paramref name="period"/>
    /// finite values (TA-Lib / Skender convention). First value at index <c>firstFinite + period - 1</c>.
    /// </summary>
    public static void Ema(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        ExponentialSmooth(source, period, 2.0 / (period + 1), destination);
    }

    public static double[] Ema(ReadOnlySpan<double> source, int period)
    {
        var r = new double[source.Length];
        Ema(source, period, r);
        return r;
    }

    /// <summary>
    /// Wilder's smoothed moving average (RMA / SMMA), alpha = 1 / period, SMA-seeded. Used by RSI and ATR.
    /// </summary>
    public static void WilderMa(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        ExponentialSmooth(source, period, 1.0 / period, destination);
    }

    public static double[] WilderMa(ReadOnlySpan<double> source, int period)
    {
        var r = new double[source.Length];
        WilderMa(source, period, r);
        return r;
    }

    private static void ExponentialSmooth(ReadOnlySpan<double> source, int period, double alpha, Span<double> destination)
    {
        int count = 0;
        double sum = 0, value = double.NaN;
        bool seeded = false;
        for (int i = 0; i < source.Length; i++)
        {
            double x = source[i];
            if (!double.IsFinite(x))
            {
                destination[i] = double.NaN;
                if (!seeded) { count = 0; sum = 0; } // seed needs `period` consecutive finite values
                continue;
            }
            if (!seeded)
            {
                sum += x;
                if (++count == period) { value = sum / period; seeded = true; destination[i] = value; }
                else destination[i] = double.NaN;
            }
            else
            {
                value += alpha * (x - value);
                destination[i] = value;
            }
        }
    }

    // ---------------------------------------------------------------- MACD

    /// <summary>
    /// MACD: line = EMA(fast) - EMA(slow); signal = EMA(signalPeriod) of the line; histogram = (line - signal) * <paramref name="histogramMultiplier"/>.
    /// Use <paramref name="histogramMultiplier"/> = 2 for the Chinese-software convention (MACD柱 = 2 × (DIF − DEA)).
    /// The line starts at index <c>slow - 1</c>, signal/histogram at <c>slow + signal - 2</c>.
    /// </summary>
    public static void Macd(ReadOnlySpan<double> source, Span<double> macd, Span<double> signal, Span<double> histogram,
        int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, double histogramMultiplier = 1.0)
    {
        if (fastPeriod < 1 || slowPeriod < 1 || signalPeriod < 1) throw new ArgumentOutOfRangeException(nameof(fastPeriod), "Periods must be >= 1.");
        if (fastPeriod >= slowPeriod) throw new ArgumentException("fastPeriod must be smaller than slowPeriod.", nameof(fastPeriod));
        int n = source.Length;
        if (macd.Length < n || signal.Length < n || histogram.Length < n) throw new ArgumentException("Destination is shorter than the source.");

        Ema(source, fastPeriod, macd);
        Ema(source, slowPeriod, histogram); // histogram used as scratch for the slow EMA
        for (int i = 0; i < n; i++) macd[i] -= histogram[i]; // NaN propagates through warm-up
        Ema(macd[..n], signalPeriod, signal);
        for (int i = 0; i < n; i++) histogram[i] = (macd[i] - signal[i]) * histogramMultiplier;
    }

    public static MacdSeries Macd(ReadOnlySpan<double> source, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, double histogramMultiplier = 1.0)
    {
        var m = new double[source.Length]; var s = new double[source.Length]; var h = new double[source.Length];
        Macd(source, m, s, h, fastPeriod, slowPeriod, signalPeriod, histogramMultiplier);
        return new MacdSeries(m, s, h);
    }
}