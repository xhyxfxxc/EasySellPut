namespace Meili.Analytics;

/// <summary>How tightly the underlying-price ruler is marked.</summary>
public enum TickDensity
{
    /// <summary>疏 — fewer, wider steps.</summary>
    Sparse,
    /// <summary>中 — default, a comfortable step for the price magnitude.</summary>
    Medium,
    /// <summary>密 — tighter steps.</summary>
    Dense,
}

/// <summary>
/// Nice-number tick steps for the strategy price ruler. The step is chosen from the familiar
/// 0.5 / 1 / 2 / 5 × 10^k series so that a typical chart range yields about 8, 16 or 32 ticks
/// (疏 / 中 / 密). Dragging snaps to those ticks.
/// </summary>
public static class PriceTicks
{
    /// <summary>Target tick counts for 疏 / 中 / 密 across the visible range.</summary>
    public const int SparseTarget = 8, MediumTarget = 16, DenseTarget = 32;

    /// <summary>A sensible step for <paramref name="density"/> so that [<paramref name="low"/>, <paramref name="high"/>] has about the target number of ticks.</summary>
    public static double Step(double low, double high, TickDensity density)
    {
        if (!double.IsFinite(low) || !double.IsFinite(high) || high <= low)
            throw new ArgumentException("Need a finite range with low < high.");
        int target = density switch { TickDensity.Sparse => SparseTarget, TickDensity.Dense => DenseTarget, _ => MediumTarget };
        return Nice((high - low) / target);
    }

    /// <summary>The nearest nice step (0.5 / 1 / 2 / 5 × 10^k) at or above <paramref name="raw"/> (raw must be finite and &gt; 0).</summary>
    public static double Nice(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0)
            throw new ArgumentOutOfRangeException(nameof(raw), raw, "Need a finite positive raw step.");
        double exp = Math.Floor(Math.Log10(raw));
        double pow = Math.Pow(10, exp);
        double n = raw / pow;
        double nice = n <= 0.5 ? 0.5 : n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
        return nice * pow;
    }

    /// <summary>Snap <paramref name="price"/> to the nearest tick inside [<paramref name="low"/>, <paramref name="high"/>].</summary>
    public static double Snap(double price, double step, double low, double high)
    {
        if (!(step > 0) || !double.IsFinite(step)) throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be finite and > 0.");
        if (!double.IsFinite(low) || !double.IsFinite(high) || high < low)
            throw new ArgumentException("Need a finite range with low <= high.");
        if (!double.IsFinite(price)) return low;
        double snapped = Math.Round(price / step) * step;
        // Round-trip through the step can leave 1 ulp of dust (e.g. 0.3 on a 0.1 grid).
        snapped = Math.Round(snapped / step) * step;
        if (snapped < low) snapped = Math.Ceiling(low / step) * step;
        if (snapped > high) snapped = Math.Floor(high / step) * step;
        return Math.Clamp(snapped, low, high);
    }

    /// <summary>Inclusive ticks from <paramref name="low"/> to <paramref name="high"/> at <paramref name="step"/>, always including both ends when they fall on the grid (ends are added even if off-grid so the ruler covers the chart).</summary>
    public static double[] Marks(double low, double high, double step)
    {
        if (!(step > 0) || !double.IsFinite(step)) throw new ArgumentOutOfRangeException(nameof(step), step, "Step must be finite and > 0.");
        if (!double.IsFinite(low) || !double.IsFinite(high) || high < low)
            throw new ArgumentException("Need a finite range with low <= high.");
        var list = new List<double>();
        double first = Math.Ceiling(low / step) * step;
        // Correct ceiling dust so 0.3000000002 doesn't skip the first visible tick.
        if (first - step >= low - step * 1e-12) first -= step;
        first = Math.Max(first, Math.Ceiling((low - step * 1e-9) / step) * step);
        for (double x = first; x <= high + step * 1e-9; x += step)
        {
            double v = Math.Round(x / step) * step;
            if (v < low - step * 1e-9 || v > high + step * 1e-9) continue;
            if (list.Count == 0 || v - list[^1] > step * 1e-6) list.Add(v);
        }
        if (list.Count == 0 || list[0] - low > step * 1e-6) list.Insert(0, low);
        if (high - list[^1] > step * 1e-6) list.Add(high);
        return list.ToArray();
    }
}
