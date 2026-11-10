namespace TyuStockApp.Analytics;

public static partial class Indicators
{
    // ---------------------------------------------------------------- Bollinger

    /// <summary>
    /// Bollinger Bands: middle = SMA(period); upper/lower = middle ± multiplier × population standard deviation
    /// (the standard definition, same as TA-Lib/Skender). First value at index <c>period - 1</c>.
    /// </summary>
    public static void Bollinger(ReadOnlySpan<double> source, Span<double> middle, Span<double> upper, Span<double> lower,
        int period = 20, double multiplier = 2.0)
    {
        int n = source.Length;
        Validate(n, period, middle.Length, nameof(middle));
        if (upper.Length < n || lower.Length < n) throw new ArgumentException("Destination is shorter than the source.");
        int lastBad = -1;
        bool rolling = false;
        double shift = 0, sum = 0, sumSq = 0; // values are shifted by the window's first value for numerical stability
        for (int i = 0; i < n; i++)
        {
            double x = source[i];
            if (!double.IsFinite(x)) { lastBad = i; rolling = false; middle[i] = upper[i] = lower[i] = double.NaN; continue; }
            if (i - lastBad < period) { middle[i] = upper[i] = lower[i] = double.NaN; continue; }
            if (!rolling)
            {
                shift = source[i - period + 1];
                sum = 0; sumSq = 0;
                for (int j = i - period + 1; j <= i; j++) { double v = source[j] - shift; sum += v; sumSq += v * v; }
                rolling = true;
            }
            else
            {
                double vin = x - shift, vout = source[i - period] - shift;
                sum += vin - vout;
                sumSq += vin * vin - vout * vout;
            }
            double mean = sum / period;
            double variance = sumSq / period - mean * mean;
            double sd = variance > 0 ? Math.Sqrt(variance) : 0;
            double mid = mean + shift;
            middle[i] = mid;
            upper[i] = mid + multiplier * sd;
            lower[i] = mid - multiplier * sd;
        }
    }

    public static BollingerSeries Bollinger(ReadOnlySpan<double> source, int period = 20, double multiplier = 2.0)
    {
        var m = new double[source.Length]; var u = new double[source.Length]; var l = new double[source.Length];
        Bollinger(source, m, u, l, period, multiplier);
        return new BollingerSeries(m, u, l);
    }

    // ---------------------------------------------------------------- True range / ATR

    /// <summary>
    /// True range: max(H − L, |H − C₋₁|, |L − C₋₁|). Index 0 (no previous close) uses H − L.
    /// </summary>
    public static void TrueRange(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, Span<double> destination)
    {
        ValidateSameLength(high.Length, low.Length, close.Length);
        Validate(close.Length, 1, destination.Length);
        double prevClose = double.NaN;
        for (int i = 0; i < close.Length; i++)
        {
            double h = high[i], l = low[i], c = close[i];
            if (!double.IsFinite(h) || !double.IsFinite(l)) { destination[i] = double.NaN; }
            else if (double.IsNaN(prevClose)) { destination[i] = h - l; }
            else { destination[i] = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose))); }
            if (double.IsFinite(c)) prevClose = c;
        }
    }

    public static double[] TrueRange(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close)
    {
        var r = new double[close.Length];
        TrueRange(high, low, close, r);
        return r;
    }

    /// <summary>
    /// Average True Range (Wilder). Like TA-Lib, the seed is the mean of the true ranges of bars 1..period
    /// (bar 0 has no previous close), so the first value is at index <c>period</c>; afterwards ATR = (ATR₋₁·(p − 1) + TR) / p.
    /// </summary>
    public static void Atr(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period, Span<double> destination)
    {
        ValidateSameLength(high.Length, low.Length, close.Length);
        Validate(close.Length, period, destination.Length);
        double prevClose = double.NaN, atr = 0;
        int count = 0;
        bool seeded = false;
        for (int i = 0; i < close.Length; i++)
        {
            double h = high[i], l = low[i], c = close[i];
            if (!double.IsFinite(h) || !double.IsFinite(l) || double.IsNaN(prevClose))
            {
                destination[i] = double.NaN;
                if (double.IsFinite(c)) prevClose = c;
                continue;
            }
            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));
            if (double.IsFinite(c)) prevClose = c;
            if (!seeded)
            {
                atr += tr;
                if (++count == period) { atr /= period; seeded = true; destination[i] = atr; }
                else destination[i] = double.NaN;
            }
            else
            {
                atr = (atr * (period - 1) + tr) / period;
                destination[i] = atr;
            }
        }
    }

    public static double[] Atr(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period = 14)
    {
        var r = new double[close.Length];
        Atr(high, low, close, period, r);
        return r;
    }
}