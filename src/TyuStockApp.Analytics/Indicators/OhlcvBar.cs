namespace TyuStockApp.Analytics;

/// <summary>One OHLCV bar. <see cref="Time"/> is the bar's open time.</summary>
public readonly record struct OhlcvBar(DateTimeOffset Time, double Open, double High, double Low, double Close, double Volume);

/// <summary>Which value of a bar to feed into a single-series indicator.</summary>
public enum PriceField
{
    Open,
    High,
    Low,
    Close,
    Volume,
    /// <summary>(High + Low) / 2</summary>
    Hl2,
    /// <summary>(High + Low + Close) / 3 (typical price)</summary>
    Hlc3,
    /// <summary>(Open + High + Low + Close) / 4</summary>
    Ohlc4,
}

/// <summary>
/// Column-oriented (struct-of-arrays) view of a bar series. Indicators work on spans of doubles,
/// so converting once and reusing the columns avoids repeated per-indicator extraction.
/// </summary>
public sealed class OhlcvColumns
{
    public OhlcvColumns(DateTimeOffset[] time, double[] open, double[] high, double[] low, double[] close, double[] volume)
    {
        ArgumentNullException.ThrowIfNull(time);
        int n = time.Length;
        if (open.Length != n || high.Length != n || low.Length != n || close.Length != n || volume.Length != n)
            throw new ArgumentException("All OHLCV columns must have the same length.");
        Time = time; Open = open; High = high; Low = low; Close = close; Volume = volume;
    }

    public DateTimeOffset[] Time { get; }
    public double[] Open { get; }
    public double[] High { get; }
    public double[] Low { get; }
    public double[] Close { get; }
    public double[] Volume { get; }
    public int Count => Time.Length;

    public static OhlcvColumns From(ReadOnlySpan<OhlcvBar> bars)
    {
        int n = bars.Length;
        var t = new DateTimeOffset[n];
        var o = new double[n]; var h = new double[n]; var l = new double[n]; var c = new double[n]; var v = new double[n];
        for (int i = 0; i < n; i++)
        {
            ref readonly var b = ref bars[i];
            t[i] = b.Time; o[i] = b.Open; h[i] = b.High; l[i] = b.Low; c[i] = b.Close; v[i] = b.Volume;
        }
        return new OhlcvColumns(t, o, h, l, c, v);
    }

    public static OhlcvColumns From(IReadOnlyList<OhlcvBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars is OhlcvBar[] arr) return From((ReadOnlySpan<OhlcvBar>)arr);
        if (bars is List<OhlcvBar> list) return From(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        var copy = new OhlcvBar[bars.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = bars[i];
        return From((ReadOnlySpan<OhlcvBar>)copy);
    }

    /// <summary>Extracts one <see cref="PriceField"/> from bars into <paramref name="destination"/>.</summary>
    public static void Extract(ReadOnlySpan<OhlcvBar> bars, PriceField field, Span<double> destination)
    {
        if (destination.Length < bars.Length) throw new ArgumentException("Destination is shorter than the source.", nameof(destination));
        for (int i = 0; i < bars.Length; i++)
        {
            ref readonly var b = ref bars[i];
            destination[i] = field switch
            {
                PriceField.Open => b.Open,
                PriceField.High => b.High,
                PriceField.Low => b.Low,
                PriceField.Close => b.Close,
                PriceField.Volume => b.Volume,
                PriceField.Hl2 => (b.High + b.Low) / 2.0,
                PriceField.Hlc3 => (b.High + b.Low + b.Close) / 3.0,
                PriceField.Ohlc4 => (b.Open + b.High + b.Low + b.Close) / 4.0,
                _ => throw new ArgumentOutOfRangeException(nameof(field)),
            };
        }
    }

    public static double[] Extract(ReadOnlySpan<OhlcvBar> bars, PriceField field)
    {
        var r = new double[bars.Length];
        Extract(bars, field, r);
        return r;
    }
}