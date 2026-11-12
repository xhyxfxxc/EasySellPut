namespace Meili.Analytics;

public sealed partial class OptionStrategy
{
    /// <summary>
    /// Exact expiry analysis over all underlying prices &gt;= 0. The expiry P/L is piecewise linear with kinks only at strikes,
    /// so it is evaluated at 0 and every strike; above the highest strike its slope is the summed units of calls and stock,
    /// which makes profit (slope &gt; 0) or loss (slope &lt; 0) unbounded. Ties report the lowest price.
    /// </summary>
    public PayoffAnalysis AnalyzeAtExpiry()
    {
        var bp = new List<double>(Strikes.Count + 1) { 0.0 };
        bp.AddRange(Strikes);
        double slope = 0;
        foreach (var leg in _legs) if (leg.Type != LegType.Put) slope += leg.Units;
        double tol = ZeroTolerance();

        var f = new double[bp.Count];
        for (int i = 0; i < f.Length; i++) f[i] = PnlAtExpiry(bp[i]);
        int iMax = 0, iMin = 0;
        for (int i = 1; i < f.Length; i++)
        {
            if (f[i] > f[iMax] + tol) iMax = i;
            if (f[i] < f[iMin] - tol) iMin = i;
        }

        var be = new List<double>();
        for (int i = 0; i < f.Length; i++)
        {
            double a = f[i];
            if (Math.Abs(a) <= tol) { be.Add(bp[i]); continue; }
            if (i + 1 < f.Length)
            {
                double b = f[i + 1];
                if (Math.Abs(b) > tol && Math.Sign(a) != Math.Sign(b)) be.Add(bp[i] + a * (bp[i + 1] - bp[i]) / (a - b));
            }
            else if (slope != 0 && Math.Sign(a) != Math.Sign(slope))
            {
                be.Add(bp[i] - a / slope);
            }
        }

        bool upUnbounded = slope > 0, downUnbounded = slope < 0;
        return new PayoffAnalysis(
            Dedupe(be),
            upUnbounded ? double.PositiveInfinity : f[iMax], upUnbounded ? null : bp[iMax],
            downUnbounded ? double.NegativeInfinity : f[iMin], downUnbounded ? null : bp[iMin],
            IsExact: true, RangeLow: 0, RangeHigh: double.PositiveInfinity);
    }

    /// <summary>
    /// Pre-expiry analysis of <see cref="PnlAt"/> within [<paramref name="low"/>, <paramref name="high"/>]: breakevens are found
    /// on a grid of <paramref name="samples"/> prices (plus strikes) and refined by bisection; max/min are refined by golden-section
    /// search. Unboundedness is structural (sign of the P/L slope as the price goes to infinity), not range-limited.
    /// </summary>
    /// <exception cref="InvalidOperationException">A live option leg has no volatility (set its IV or <see cref="StrategyValuation.DefaultVolatility"/>).</exception>
    public PayoffAnalysis AnalyzeAt(StrategyValuation valuation, double low, double high, int samples = 400)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        double slope = 0, unitSum = 0;
        foreach (var leg in _legs)
        {
            unitSum += Math.Abs(leg.Units);
            if (!leg.IsOption) { slope += leg.Units; continue; }
            double years = valuation.YearsTo(leg.Expiry!.Value);
            if (years > 0 && double.IsNaN(valuation.VolatilityFor(leg)))
                throw new InvalidOperationException($"Leg '{leg}' has no implied volatility; set it or StrategyValuation.DefaultVolatility.");
            if (leg.Type == LegType.Call) slope += leg.Units * Math.Exp(-valuation.DividendYield * years);
        }
        if (!double.IsFinite(slope)) throw new ArgumentException("Rate/dividend yield must be finite.", nameof(valuation));

        double F(double s) => PnlAt(s, valuation);
        var x = BuildGrid(low, high, Math.Max(samples, 2));
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++) y[i] = F(x[i]);
        double tol = ZeroTolerance();

        var be = new List<double>();
        int iMax = 0, iMin = 0;
        for (int i = 0; i < x.Length; i++)
        {
            if (y[i] > y[iMax]) iMax = i;
            if (y[i] < y[iMin]) iMin = i;
            if (Math.Abs(y[i]) <= tol) { be.Add(x[i]); continue; }
            if (i + 1 < x.Length && Math.Abs(y[i + 1]) > tol && Math.Sign(y[i]) != Math.Sign(y[i + 1]))
                be.Add(Bisect(F, x[i], x[i + 1], y[i]));
        }

        var (maxAt, maxVal) = RefineExtreme(F, x, iMax, y[iMax], maximize: true);
        var (minAt, minVal) = RefineExtreme(F, x, iMin, y[iMin], maximize: false);
        double slopeTol = 1e-9 * Math.Max(1.0, unitSum);
        bool upUnbounded = slope > slopeTol, downUnbounded = slope < -slopeTol;
        return new PayoffAnalysis(
            Dedupe(be),
            upUnbounded ? double.PositiveInfinity : maxVal, upUnbounded ? null : maxAt,
            downUnbounded ? double.NegativeInfinity : minVal, downUnbounded ? null : minAt,
            IsExact: false, RangeLow: x[0], RangeHigh: x[^1]);
    }

    /// <summary>Money amount treated as zero P/L: 1e-12 of the position's notional scale.</summary>
    private double ZeroTolerance()
    {
        double scale = 0;
        foreach (var leg in _legs) scale += Math.Abs(leg.Units) * Math.Max(1.0, Math.Max(leg.Strike, leg.Premium));
        return 1e-12 * Math.Max(1.0, scale);
    }

    private static double Bisect(Func<double, double> f, double lo, double hi, double fLo)
    {
        double tol = 1e-12 * Math.Max(1.0, hi);
        for (int i = 0; i < 200 && hi - lo > tol; i++)
        {
            double mid = 0.5 * (lo + hi), fm = f(mid);
            if (fm == 0) return mid;
            if (Math.Sign(fm) == Math.Sign(fLo)) { lo = mid; fLo = fm; } else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Golden-section refinement of a sampled extreme between its neighbours (keeps the sample if it is better).</summary>
    private static (double At, double Value) RefineExtreme(Func<double, double> f, double[] x, int i, double fi, bool maximize)
    {
        if (i == 0 || i == x.Length - 1) return (x[i], fi);
        double sign = maximize ? -1 : 1; // minimise sign*f
        double a = x[i - 1], b = x[i + 1];
        const double InvPhi = 0.6180339887498949;
        double c = b - InvPhi * (b - a), d = a + InvPhi * (b - a);
        double fc = sign * f(c), fd = sign * f(d);
        for (int k = 0; k < 100 && b - a > 1e-10 * Math.Max(1.0, b); k++)
        {
            if (fc < fd) { b = d; d = c; fd = fc; c = b - InvPhi * (b - a); fc = sign * f(c); }
            else { a = c; c = d; fc = fd; d = a + InvPhi * (b - a); fd = sign * f(d); }
        }
        double xm = 0.5 * (a + b), fm = f(xm);
        return (maximize ? fm > fi : fm < fi) ? (xm, fm) : (x[i], fi);
    }

    private static double[] Dedupe(List<double> values)
    {
        values.Sort();
        var r = new List<double>(values.Count);
        foreach (var v in values)
            if (r.Count == 0 || v - r[^1] > 1e-9 * Math.Max(1.0, Math.Abs(v))) r.Add(v);
        return r.ToArray();
    }
}