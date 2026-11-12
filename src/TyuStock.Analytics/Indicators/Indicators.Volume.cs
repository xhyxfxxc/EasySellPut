namespace TyuStock.Analytics;

public static partial class Indicators
{
    // ---------------------------------------------------------------- OBV

    /// <summary>
    /// On-Balance Volume. Starts at 0 on the first bar with a finite close; then adds the bar's volume when the close
    /// rises, subtracts it when the close falls, and keeps it unchanged when the close is flat.
    /// </summary>
    public static void Obv(ReadOnlySpan<double> close, ReadOnlySpan<double> volume, Span<double> destination)
    {
        if (close.Length != volume.Length) throw new ArgumentException("Input series must have the same length.");
        Validate(close.Length, 1, destination.Length);
        double prev = double.NaN, obv = 0;
        for (int i = 0; i < close.Length; i++)
        {
            double c = close[i], v = volume[i];
            if (!double.IsFinite(c)) { destination[i] = double.NaN; continue; }
            if (double.IsNaN(prev)) { prev = c; destination[i] = obv; continue; }
            if (double.IsFinite(v))
            {
                if (c > prev) obv += v;
                else if (c < prev) obv -= v;
            }
            prev = c;
            destination[i] = obv;
        }
    }

    public static double[] Obv(ReadOnlySpan<double> close, ReadOnlySpan<double> volume)
    {
        var r = new double[close.Length];
        Obv(close, volume, r);
        return r;
    }

    // ---------------------------------------------------------------- VWAP

    /// <summary>
    /// Volume-weighted average price using the typical price (H + L + C) / 3, cumulative within a session and reset
    /// whenever the bar's calendar date in <paramref name="sessionTimeZone"/> changes (default US/Eastern, so pre-market,
    /// regular and after-hours bars of the same New York day form one session). NaN until the session has positive volume.
    /// </summary>
    public static void Vwap(ReadOnlySpan<DateTimeOffset> time, ReadOnlySpan<double> high, ReadOnlySpan<double> low,
        ReadOnlySpan<double> close, ReadOnlySpan<double> volume, Span<double> destination, TimeZoneInfo? sessionTimeZone = null)
    {
        ValidateSameLength(high.Length, low.Length, close.Length);
        if (time.Length != close.Length || volume.Length != close.Length) throw new ArgumentException("Input series must have the same length.");
        Validate(close.Length, 1, destination.Length);
        var tz = sessionTimeZone ?? UsEastern.TimeZone;

        // Cache the current session's [startUtc, endUtc) so the time-zone conversion runs once per session, not per bar.
        DateTimeOffset sessionStart = DateTimeOffset.MaxValue, sessionEnd = DateTimeOffset.MinValue;
        double pv = 0, vol = 0;
        for (int i = 0; i < close.Length; i++)
        {
            var t = time[i];
            if (t < sessionStart || t >= sessionEnd)
            {
                (sessionStart, sessionEnd) = LocalDayBounds(t, tz);
                pv = 0; vol = 0;
            }
            double h = high[i], l = low[i], c = close[i], v = volume[i];
            if (double.IsFinite(h) && double.IsFinite(l) && double.IsFinite(c) && double.IsFinite(v) && v > 0)
            {
                pv += (h + l + c) / 3.0 * v;
                vol += v;
            }
            destination[i] = vol > 0 ? pv / vol : double.NaN;
        }
    }

    public static double[] Vwap(ReadOnlySpan<DateTimeOffset> time, ReadOnlySpan<double> high, ReadOnlySpan<double> low,
        ReadOnlySpan<double> close, ReadOnlySpan<double> volume, TimeZoneInfo? sessionTimeZone = null)
    {
        var r = new double[close.Length];
        Vwap(time, high, low, close, volume, r, sessionTimeZone);
        return r;
    }

    public static double[] Vwap(OhlcvColumns bars, TimeZoneInfo? sessionTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        return Vwap(bars.Time, bars.High, bars.Low, bars.Close, bars.Volume, sessionTimeZone);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) LocalDayBounds(DateTimeOffset t, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(t, tz);
        var date = DateOnly.FromDateTime(local.DateTime);
        return (UsEastern.ToInstant(date, TimeOnly.MinValue, tz), UsEastern.ToInstant(date.AddDays(1), TimeOnly.MinValue, tz));
    }
}