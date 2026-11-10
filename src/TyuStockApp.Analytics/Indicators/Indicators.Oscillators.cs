using System.Buffers;

namespace TyuStockApp.Analytics;

public static partial class Indicators
{
    // ---------------------------------------------------------------- RSI (Wilder)

    /// <summary>
    /// Relative Strength Index with Wilder smoothing. The first average gain/loss is the simple mean of the first
    /// <paramref name="period"/> changes, so the first value is at index <c>firstFinite + period</c> (matches TA-Lib/Skender).
    /// A completely flat window yields 50; no losses yields 100.
    /// </summary>
    public static void Rsi(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        double prev = double.NaN, avgGain = 0, avgLoss = 0;
        int count = 0;
        bool seeded = false;
        for (int i = 0; i < source.Length; i++)
        {
            double x = source[i];
            if (!double.IsFinite(x)) { destination[i] = double.NaN; continue; }
            if (double.IsNaN(prev)) { prev = x; destination[i] = double.NaN; continue; }
            double change = x - prev;
            prev = x;
            double gain = change > 0 ? change : 0, loss = change < 0 ? -change : 0;
            if (!seeded)
            {
                avgGain += gain; avgLoss += loss;
                if (++count == period)
                {
                    avgGain /= period; avgLoss /= period; seeded = true;
                    destination[i] = RsiValue(avgGain, avgLoss);
                }
                else destination[i] = double.NaN;
            }
            else
            {
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
                destination[i] = RsiValue(avgGain, avgLoss);
            }
        }
    }

    private static double RsiValue(double avgGain, double avgLoss)
    {
        double total = avgGain + avgLoss;
        return total > 0 ? 100.0 * avgGain / total : 50.0;
    }

    public static double[] Rsi(ReadOnlySpan<double> source, int period = 14)
    {
        var r = new double[source.Length];
        Rsi(source, period, r);
        return r;
    }

    // ---------------------------------------------------------------- Highest / Lowest

    /// <summary>Rolling maximum over <paramref name="period"/> values (O(n) monotonic deque). First value at index <c>period - 1</c>.</summary>
    public static void Highest(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        RollingExtreme(source, period, destination, max: true);
    }

    /// <summary>Rolling minimum over <paramref name="period"/> values. First value at index <c>period - 1</c>.</summary>
    public static void Lowest(ReadOnlySpan<double> source, int period, Span<double> destination)
    {
        Validate(source.Length, period, destination.Length);
        RollingExtreme(source, period, destination, max: false);
    }

    public static double[] Highest(ReadOnlySpan<double> source, int period) { var r = new double[source.Length]; Highest(source, period, r); return r; }

    public static double[] Lowest(ReadOnlySpan<double> source, int period) { var r = new double[source.Length]; Lowest(source, period, r); return r; }

    private static void RollingExtreme(ReadOnlySpan<double> source, int period, Span<double> destination, bool max)
    {
        int[]? rented = null;
        Span<int> dq = period <= 256 ? stackalloc int[period] : (rented = ArrayPool<int>.Shared.Rent(period)).AsSpan(0, period);
        try
        {
            int head = 0, count = 0, lastBad = -1;
            for (int i = 0; i < source.Length; i++)
            {
                double x = source[i];
                if (!double.IsFinite(x)) { lastBad = i; head = 0; count = 0; destination[i] = double.NaN; continue; }
                if (count > 0 && dq[head] <= i - period) { head = (head + 1) % period; count--; }
                while (count > 0)
                {
                    double back = source[dq[(head + count - 1) % period]];
                    if (max ? back <= x : back >= x) count--; else break;
                }
                dq[(head + count) % period] = i;
                count++;
                destination[i] = i - lastBad >= period ? source[dq[head]] : double.NaN;
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
        }
    }

    // ---------------------------------------------------------------- Stochastic

    /// <summary>
    /// Stochastic oscillator. Fast %K = 100 × (C − LL) / (HH − LL) over <paramref name="kPeriod"/> bars (50 when HH = LL);
    /// %K = SMA(fast %K, <paramref name="kSmoothing"/>) (use 1 for the "fast" stochastic); %D = SMA(%K, <paramref name="dPeriod"/>).
    /// </summary>
    public static void Stochastic(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close,
        Span<double> k, Span<double> d, int kPeriod = 14, int kSmoothing = 3, int dPeriod = 3)
    {
        ValidateSameLength(high.Length, low.Length, close.Length);
        Validate(close.Length, kPeriod, k.Length, nameof(k));
        Validate(close.Length, dPeriod, d.Length, nameof(d));
        if (kSmoothing < 1) throw new ArgumentOutOfRangeException(nameof(kSmoothing));
        int n = close.Length;
        double[] buf = ArrayPool<double>.Shared.Rent(n * 2);
        try
        {
            var raw = buf.AsSpan(0, n);
            var tmp = buf.AsSpan(n, n);
            RawStochastic(high, low, close, kPeriod, raw, tmp, k[..n]);
            if (kSmoothing == 1) raw.CopyTo(k);
            else Sma(raw, kSmoothing, k);
            Sma(k[..n], dPeriod, d);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(buf);
        }
    }

    public static StochasticSeries Stochastic(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close,
        int kPeriod = 14, int kSmoothing = 3, int dPeriod = 3)
    {
        var k = new double[close.Length]; var d = new double[close.Length];
        Stochastic(high, low, close, k, d, kPeriod, kSmoothing, dPeriod);
        return new StochasticSeries(k, d);
    }

    /// <summary>Writes the raw stochastic value (RSV, 0..100) into <paramref name="rsv"/>; uses two scratch spans.</summary>
    private static void RawStochastic(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period,
        Span<double> rsv, Span<double> scratchHigh, Span<double> scratchLow)
    {
        RollingExtreme(high, period, scratchHigh, max: true);
        RollingExtreme(low, period, scratchLow, max: false);
        for (int i = 0; i < close.Length; i++)
        {
            double hh = scratchHigh[i], ll = scratchLow[i], c = close[i];
            if (!double.IsFinite(hh) || !double.IsFinite(ll) || !double.IsFinite(c)) { rsv[i] = double.NaN; continue; }
            double range = hh - ll;
            rsv[i] = range > 0 ? 100.0 * (c - ll) / range : 50.0;
        }
    }

    // ---------------------------------------------------------------- KDJ

    /// <summary>
    /// KDJ as used by Chinese charting software (通达信/同花顺): RSV over <paramref name="period"/> bars;
    /// K = ((m1 − 1)·K₋₁ + RSV) / m1, D = ((m2 − 1)·D₋₁ + K) / m2, both seeded with 50; J = 3K − 2D.
    /// Defaults KDJ(9, 3, 3). First value at index <c>period - 1</c>.
    /// </summary>
    public static void Kdj(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close,
        Span<double> k, Span<double> d, Span<double> j, int period = 9, int m1 = 3, int m2 = 3)
    {
        ValidateSameLength(high.Length, low.Length, close.Length);
        int n = close.Length;
        Validate(n, period, k.Length, nameof(k));
        if (d.Length < n || j.Length < n) throw new ArgumentException("Destination is shorter than the source.");
        if (m1 < 1 || m2 < 1) throw new ArgumentOutOfRangeException(nameof(m1), "Smoothing factors must be >= 1.");

        RawStochastic(high, low, close, period, j[..n], k[..n], d[..n]); // RSV -> j (k, d used as scratch)
        double kPrev = 50, dPrev = 50;
        for (int i = 0; i < n; i++)
        {
            double rsv = j[i];
            if (double.IsNaN(rsv)) { k[i] = d[i] = j[i] = double.NaN; continue; }
            kPrev = ((m1 - 1) * kPrev + rsv) / m1;
            dPrev = ((m2 - 1) * dPrev + kPrev) / m2;
            k[i] = kPrev; d[i] = dPrev; j[i] = 3 * kPrev - 2 * dPrev;
        }
    }

    public static KdjSeries Kdj(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period = 9, int m1 = 3, int m2 = 3)
    {
        var k = new double[close.Length]; var d = new double[close.Length]; var j = new double[close.Length];
        Kdj(high, low, close, k, d, j, period, m1, m2);
        return new KdjSeries(k, d, j);
    }
}