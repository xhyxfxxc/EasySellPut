namespace TyuStockApp.Analytics;

public enum OptionRight
{
    Call,
    Put,
}

/// <summary>
/// Price and Greeks of a European option under Black-Scholes-Merton.
/// <para>Units (same as CBOE / most broker UIs):</para>
/// <list type="bullet">
/// <item><see cref="Delta"/>: per $1 move of the underlying.</item>
/// <item><see cref="Gamma"/>: change of delta per $1 move.</item>
/// <item><see cref="Vega"/>: price change per 1 volatility point (σ + 0.01).</item>
/// <item><see cref="Theta"/>: price change per calendar day (annual theta / 365); usually negative.</item>
/// <item><see cref="Rho"/>: price change per 1 percentage point of the risk-free rate (r + 0.01).</item>
/// </list>
/// All values are per share (multiply by the contract multiplier, normally 100, for per-contract values).
/// </summary>
public readonly record struct OptionGreeks(double Price, double Delta, double Gamma, double Vega, double Theta, double Rho)
{
    public static readonly OptionGreeks NaN = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
}

/// <summary>
/// Black-Scholes-Merton for European options with a continuous dividend yield.
/// <para>Parameters: <c>spot</c> S &gt; 0, <c>strike</c> K &gt; 0, <c>years</c> T (see <see cref="OptionTime"/>),
/// <c>rate</c> r and <c>dividendYield</c> q as continuously-compounded decimals (0.04 = 4%), <c>volatility</c> σ as a decimal (0.25 = 25%).</para>
/// <para>Degenerate inputs are handled rather than thrown: T ≤ 0 gives intrinsic value; σ ≤ 0 gives the discounted
/// forward intrinsic value; invalid S/K (≤ 0 or non-finite) give NaN.</para>
/// <para>American-style US equity options are priced as European; for calls without dividends this is exact, otherwise
/// it is the standard approximation used for displaying Greeks/IV (CBOE and Yahoo quote IVs the same way).</para>
/// </summary>
public static class BlackScholes
{
    public static double Price(OptionRight right, double spot, double strike, double years, double rate, double dividendYield, double volatility)
    {
        if (!ValidSpotStrike(spot, strike) || !double.IsFinite(rate) || !double.IsFinite(dividendYield) || double.IsNaN(years) || double.IsNaN(volatility)
            || double.IsPositiveInfinity(years) || double.IsPositiveInfinity(volatility))
            return double.NaN;
        if (years <= 0) return Intrinsic(right, spot, strike);
        double dq = Math.Exp(-dividendYield * years), dr = Math.Exp(-rate * years);
        if (volatility <= 0)
            return Math.Max(right == OptionRight.Call ? spot * dq - strike * dr : strike * dr - spot * dq, 0.0);
        double sqrtT = Math.Sqrt(years);
        double sd = volatility * sqrtT;
        double d1 = (Math.Log(spot / strike) + (rate - dividendYield + 0.5 * volatility * volatility) * years) / sd;
        double d2 = d1 - sd;
        return right == OptionRight.Call
            ? spot * dq * NormalDistribution.Cdf(d1) - strike * dr * NormalDistribution.Cdf(d2)
            : strike * dr * NormalDistribution.Cdf(-d2) - spot * dq * NormalDistribution.Cdf(-d1);
    }

    public static OptionGreeks Greeks(OptionRight right, double spot, double strike, double years, double rate, double dividendYield, double volatility)
    {
        if (!ValidSpotStrike(spot, strike) || !double.IsFinite(rate) || !double.IsFinite(dividendYield) || double.IsNaN(years) || double.IsNaN(volatility)
            || double.IsPositiveInfinity(years) || double.IsPositiveInfinity(volatility))
            return OptionGreeks.NaN;
        bool call = right == OptionRight.Call;

        if (years <= 0)
        {
            double intrinsic = Intrinsic(right, spot, strike);
            double delta = call ? (spot > strike ? 1 : spot == strike ? 0.5 : 0) : (spot < strike ? -1 : spot == strike ? -0.5 : 0);
            return new OptionGreeks(intrinsic, delta, 0, 0, 0, 0);
        }

        double dq = Math.Exp(-dividendYield * years), dr = Math.Exp(-rate * years);
        if (volatility <= 0)
        {
            double fwd = spot * dq - strike * dr; // call payoff in forward terms
            bool itm = call ? fwd > 0 : fwd < 0;
            if (!itm) return new OptionGreeks(0, 0, 0, 0, 0, 0);
            return call
                ? new OptionGreeks(fwd, dq, 0, 0, (dividendYield * spot * dq - rate * strike * dr) / OptionTime.DaysPerYear, strike * years * dr / 100.0)
                : new OptionGreeks(-fwd, -dq, 0, 0, (rate * strike * dr - dividendYield * spot * dq) / OptionTime.DaysPerYear, -strike * years * dr / 100.0);
        }

        double sqrtT = Math.Sqrt(years);
        double sd = volatility * sqrtT;
        double d1 = (Math.Log(spot / strike) + (rate - dividendYield + 0.5 * volatility * volatility) * years) / sd;
        double d2 = d1 - sd;
        double pdf1 = NormalDistribution.Pdf(d1);
        double gamma = dq * pdf1 / (spot * sd);
        double vega = spot * dq * pdf1 * sqrtT / 100.0;
        double decay = -spot * dq * pdf1 * volatility / (2.0 * sqrtT);
        if (call)
        {
            double nd1 = NormalDistribution.Cdf(d1), nd2 = NormalDistribution.Cdf(d2);
            double price = spot * dq * nd1 - strike * dr * nd2;
            double theta = decay - rate * strike * dr * nd2 + dividendYield * spot * dq * nd1;
            return new OptionGreeks(price, dq * nd1, gamma, vega, theta / OptionTime.DaysPerYear, strike * years * dr * nd2 / 100.0);
        }
        else
        {
            double nmd1 = NormalDistribution.Cdf(-d1), nmd2 = NormalDistribution.Cdf(-d2);
            double price = strike * dr * nmd2 - spot * dq * nmd1;
            double theta = decay + rate * strike * dr * nmd2 - dividendYield * spot * dq * nmd1;
            return new OptionGreeks(price, -dq * nmd1, gamma, vega, theta / OptionTime.DaysPerYear, -strike * years * dr * nmd2 / 100.0);
        }
    }

    /// <summary>Intrinsic value max(S − K, 0) for calls, max(K − S, 0) for puts.</summary>
    public static double Intrinsic(OptionRight right, double spot, double strike)
        => right == OptionRight.Call ? Math.Max(spot - strike, 0.0) : Math.Max(strike - spot, 0.0);

    /// <summary>Vega per 1.00 of volatility (not per point) — the derivative used by the IV solver.</summary>
    internal static double VegaPerUnit(double spot, double strike, double years, double rate, double dividendYield, double volatility)
    {
        double sqrtT = Math.Sqrt(years);
        double d1 = (Math.Log(spot / strike) + (rate - dividendYield + 0.5 * volatility * volatility) * years) / (volatility * sqrtT);
        return spot * Math.Exp(-dividendYield * years) * NormalDistribution.Pdf(d1) * sqrtT;
    }

    private static bool ValidSpotStrike(double spot, double strike)
        => double.IsFinite(spot) && double.IsFinite(strike) && spot > 0 && strike > 0;
}