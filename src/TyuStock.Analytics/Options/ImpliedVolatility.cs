namespace TyuStock.Analytics;

/// <summary>Search bounds and tolerances for <see cref="ImpliedVolatility"/>.</summary>
public sealed record ImpliedVolatilitySettings
{
    public static readonly ImpliedVolatilitySettings Default = new();

    /// <summary>Lowest volatility searched (decimal). Prices implying less are reported as unsolvable.</summary>
    public double MinVolatility { get; init; } = 1e-4;

    /// <summary>Highest volatility searched (decimal, 10 = 1000%).</summary>
    public double MaxVolatility { get; init; } = 10.0;

    /// <summary>Relative price tolerance (fraction of the option's time value).</summary>
    public double RelativePriceTolerance { get; init; } = 1e-10;

    /// <summary>Absolute volatility tolerance.</summary>
    public double VolatilityTolerance { get; init; } = 1e-10;

    public int MaxIterations { get; init; } = 100;
}

/// <summary>Where a resolved implied volatility came from.</summary>
public enum IvOrigin
{
    /// <summary>The data provider's IV passed the plausibility filter.</summary>
    Provider,
    /// <summary>Solved locally from the bid/ask mid.</summary>
    SolvedFromMid,
    /// <summary>Solved locally from the last trade price.</summary>
    SolvedFromLast,
}

public readonly record struct ResolvedIv(double Value, IvOrigin Origin);

/// <summary>
/// Implied-volatility solver for Black-Scholes-Merton prices.
/// <para>Method: the price is checked against no-arbitrage bounds, then converted (via put-call parity) to the
/// out-of-the-money option with the same strike, whose price equals the time value — this keeps deep-ITM quotes well
/// conditioned. A safeguarded Newton iteration starts from the Manaster-Koehler point (maximum vega) and falls back to
/// bisection whenever a step leaves the bracket; if it has not converged, Brent's method finishes on the bracket.</para>
/// <para>Returns <c>null</c> whenever the IV is undefined or unreliable: non-positive/non-finite inputs, expired options,
/// prices at/below intrinsic or above the upper bound, or IV outside [MinVolatility, MaxVolatility].</para>
/// </summary>
public static class ImpliedVolatility
{
    public static double? Solve(OptionRight right, double price, double spot, double strike, double years, double rate, double dividendYield,
        ImpliedVolatilitySettings? settings = null)
    {
        var s = settings ?? ImpliedVolatilitySettings.Default;
        if (!double.IsFinite(price) || !double.IsFinite(spot) || !double.IsFinite(strike) || !double.IsFinite(years)
            || !double.IsFinite(rate) || !double.IsFinite(dividendYield))
            return null;
        if (price <= 0 || spot <= 0 || strike <= 0 || years <= 0) return null;

        double fwdSpot = spot * Math.Exp(-dividendYield * years);   // S·e^(−qT)
        double pvStrike = strike * Math.Exp(-rate * years);         // K·e^(−rT)
        double upper = right == OptionRight.Call ? fwdSpot : pvStrike;
        if (price >= upper) return null;

        // Time value = price − lower bound; equals the price of the OTM option of the same strike (put-call parity).
        double parity = fwdSpot - pvStrike; // C − P
        double lower = right == OptionRight.Call ? Math.Max(parity, 0) : Math.Max(-parity, 0);
        double timeValue = price - lower;
        if (!(timeValue > 1e-12 * Math.Max(1.0, Math.Min(spot, strike)))) return null;
        var otm = parity >= 0 ? OptionRight.Put : OptionRight.Call;

        double lo = s.MinVolatility, hi = s.MaxVolatility;
        double fLo = BlackScholes.Price(otm, spot, strike, years, rate, dividendYield, lo) - timeValue;
        double fHi = BlackScholes.Price(otm, spot, strike, years, rate, dividendYield, hi) - timeValue;
        if (double.IsNaN(fLo) || double.IsNaN(fHi)) return null;
        double tol = Math.Max(s.RelativePriceTolerance * timeValue, 1e-300);
        if (fLo > tol || fHi < -tol) return null; // IV outside the search range
        if (Math.Abs(fLo) <= tol) return lo;
        if (Math.Abs(fHi) <= tol) return hi;

        // Manaster-Koehler starting point: σ0 = sqrt(2·|ln(F/K)| / T), where vega is maximal.
        double logMoneyness = Math.Abs(Math.Log(fwdSpot / pvStrike));
        double sigma = logMoneyness > 1e-12 ? Math.Sqrt(2.0 * logMoneyness / years)
            : timeValue / spot * Math.Sqrt(2 * Math.PI / years); // Brenner-Subrahmanyam (ATM)
        if (!(sigma > lo && sigma < hi)) sigma = 0.5 > lo && 0.5 < hi ? 0.5 : 0.5 * (lo + hi);

        for (int i = 0; i < s.MaxIterations; i++)
        {
            double f = BlackScholes.Price(otm, spot, strike, years, rate, dividendYield, sigma) - timeValue;
            if (Math.Abs(f) <= tol) return sigma;
            if (f > 0) hi = sigma; else lo = sigma;
            if (hi - lo <= s.VolatilityTolerance) return 0.5 * (lo + hi);
            double vega = BlackScholes.VegaPerUnit(spot, strike, years, rate, dividendYield, sigma);
            double next = vega > 1e-300 ? sigma - f / vega : double.NaN;
            sigma = next > lo && next < hi ? next : 0.5 * (lo + hi);
        }
        return Brent(otm, timeValue, spot, strike, years, rate, dividendYield, lo, hi, tol, s);
    }

    /// <summary>Solves IV from the bid/ask mid. Returns null for missing, zero or crossed quotes.</summary>
    public static double? SolveFromQuote(OptionRight right, double? bid, double? ask, double spot, double strike, double years, double rate,
        double dividendYield, ImpliedVolatilitySettings? settings = null)
    {
        if (bid is not { } b || ask is not { } a || !(b > 0) || !(a >= b) || !double.IsFinite(a)) return null;
        return Solve(right, 0.5 * (b + a), spot, strike, years, rate, dividendYield, settings);
    }

    /// <summary>
    /// Picks a usable IV for display/Greeks: the provider value if plausible (see <see cref="IvFilter"/>), otherwise solves
    /// from the bid/ask mid, otherwise from the last price. Returns null if nothing usable exists.
    /// </summary>
    public static ResolvedIv? Resolve(OptionRight right, double? providerIv, double? bid, double? ask, double? last,
        double spot, double strike, double years, double rate, double dividendYield, ImpliedVolatilitySettings? settings = null)
    {
        if (IvFilter.Clean(providerIv) is { } p) return new ResolvedIv(p, IvOrigin.Provider);
        if (IvFilter.Clean(SolveFromQuote(right, bid, ask, spot, strike, years, rate, dividendYield, settings)) is { } m)
            return new ResolvedIv(m, IvOrigin.SolvedFromMid);
        if (last is { } l && l > 0 && IvFilter.Clean(Solve(right, l, spot, strike, years, rate, dividendYield, settings)) is { } x)
            return new ResolvedIv(x, IvOrigin.SolvedFromLast);
        return null;
    }

    private static double? Brent(OptionRight right, double target, double spot, double strike, double years, double rate, double q,
        double a, double b, double tol, ImpliedVolatilitySettings s)
    {
        double F(double v) => BlackScholes.Price(right, spot, strike, years, rate, q, v) - target;
        double fa = F(a), fb = F(b);
        if (fa * fb > 0) return null;
        double c = a, fc = fa, d = b - a, e = d;
        for (int i = 0; i < 4 * s.MaxIterations; i++)
        {
            if (fb * fc > 0) { c = a; fc = fa; d = b - a; e = d; }
            if (Math.Abs(fc) < Math.Abs(fb)) { a = b; b = c; c = a; fa = fb; fb = fc; fc = fa; }
            double tol1 = 2 * double.Epsilon + 0.5 * s.VolatilityTolerance;
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol1 || Math.Abs(fb) <= tol) return b;
            if (Math.Abs(e) >= tol1 && Math.Abs(fa) > Math.Abs(fb))
            {
                double p, qq, r2, s2 = fb / fa;
                if (a == c) { p = 2 * m * s2; qq = 1 - s2; }
                else
                {
                    qq = fa / fc; r2 = fb / fc;
                    p = s2 * (2 * m * qq * (qq - r2) - (b - a) * (r2 - 1));
                    qq = (qq - 1) * (r2 - 1) * (s2 - 1);
                }
                if (p > 0) qq = -qq; else p = -p;
                if (2 * p < Math.Min(3 * m * qq - Math.Abs(tol1 * qq), Math.Abs(e * qq))) { e = d; d = p / qq; }
                else { d = m; e = m; }
            }
            else { d = m; e = m; }
            a = b; fa = fb;
            b += Math.Abs(d) > tol1 ? d : (m > 0 ? tol1 : -tol1);
            fb = F(b);
        }
        return Math.Abs(fb) <= Math.Max(tol, 1e-8 * target) ? b : null;
    }
}

/// <summary>
/// Plausibility filter for provider-supplied IVs. Free feeds emit placeholders such as 1e-5 (Yahoo pre-market when
/// bid/ask are 0) or 0; those must be discarded and recomputed rather than shown or used for Greeks.
/// </summary>
public static class IvFilter
{
    /// <summary>Lowest IV considered real (0.5%).</summary>
    public const double DefaultMin = 0.005;

    /// <summary>Highest IV considered real (1000%; near-expiry meme names can legitimately exceed 500%).</summary>
    public const double DefaultMax = 10.0;

    public static bool IsPlausible(double? iv, double min = DefaultMin, double max = DefaultMax)
        => iv is { } v && double.IsFinite(v) && v >= min && v <= max;

    /// <summary>Returns <paramref name="iv"/> if plausible, otherwise null.</summary>
    public static double? Clean(double? iv, double min = DefaultMin, double max = DefaultMax)
        => IsPlausible(iv, min, max) ? iv : null;

    /// <summary>Converts a percentage IV (25.3 meaning 25.3%) to a decimal (0.253). Use only when the source is known to quote percent.</summary>
    public static double? FromPercent(double? percent) => percent is { } p && double.IsFinite(p) ? p / 100.0 : null;
}