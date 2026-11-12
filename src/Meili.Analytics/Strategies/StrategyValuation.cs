namespace Meili.Analytics;

/// <summary>
/// Market assumptions for valuing a strategy before expiry with Black-Scholes-Merton.
/// <para><see cref="Rate"/> and <see cref="DividendYield"/> are continuously-compounded decimals. Each option leg uses its own
/// <see cref="StrategyLeg.ImpliedVolatility"/>; legs without one fall back to <see cref="DefaultVolatility"/>.
/// <see cref="VolatilityShift"/> is added to every leg's IV (0.05 = +5 vol points) for "what if IV changes" scenarios.</para>
/// </summary>
public sealed record StrategyValuation(DateTimeOffset ValuationTime, double Rate, double DividendYield = 0)
{
    /// <summary>IV used for option legs whose own IV is not set (NaN). Null = such legs value as NaN.</summary>
    public double? DefaultVolatility { get; init; }

    /// <summary>Added to every leg's IV (decimal); the result is floored at 0.</summary>
    public double VolatilityShift { get; init; }

    /// <summary>Settlement time of day on the expiry date, New York time (null = 16:00).</summary>
    public TimeOnly? SettlementTime { get; init; }

    /// <summary>Valuation at <paramref name="date"/> <paramref name="timeOfDay"/> New York time (default 16:00, i.e. that day's close).</summary>
    public static StrategyValuation AtDate(DateOnly date, double rate, double dividendYield = 0, TimeOnly? timeOfDay = null)
        => new(UsEastern.ToInstant(date, timeOfDay ?? OptionTime.MarketClose), rate, dividendYield);

    /// <summary>Years from <see cref="ValuationTime"/> to the leg's expiry instant (0 once expired).</summary>
    public double YearsTo(DateOnly expiry) => OptionTime.YearsBetween(ValuationTime, OptionTime.ExpiryInstant(expiry, SettlementTime));

    /// <summary>Effective volatility for a leg (own IV or default, plus shift, floored at 0); NaN if none is available.</summary>
    public double VolatilityFor(StrategyLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        double v = leg.HasImpliedVolatility ? leg.ImpliedVolatility : DefaultVolatility ?? double.NaN;
        return double.IsFinite(v) ? Math.Max(v + VolatilityShift, 0.0) : double.NaN;
    }
}

/// <summary>
/// Position-level value and Greeks of a strategy (all legs, all quantities and multipliers applied).
/// <para><see cref="Delta"/>: share-equivalents (P/L per $1 move); <see cref="Gamma"/>: delta change per $1;
/// <see cref="Vega"/>: money per 1 vol point; <see cref="Theta"/>: money per calendar day; <see cref="Rho"/>: money per 1% rate.</para>
/// </summary>
public readonly record struct StrategyGreeks(double Value, double Pnl, double Delta, double Gamma, double Vega, double Theta, double Rho);

/// <summary>Sampled P/L curve for charting. <see cref="ValuationPnl"/> is null when no pre-expiry valuation was requested.</summary>
public sealed record PayoffCurve(double[] Prices, double[] ExpiryPnl, double[]? ValuationPnl);

/// <summary>
/// Key figures of a P/L profile. Money values are position P/L: <see cref="MaxProfit"/> is +Infinity when profit is unbounded,
/// <see cref="MaxLoss"/> is the lowest P/L (a negative number for a loss) and -Infinity when the loss is unbounded.
/// <para>When <see cref="IsExact"/> is true (expiry analysis) the figures are exact over all prices &gt;= 0; otherwise they were
/// found numerically within [<see cref="RangeLow"/>, <see cref="RangeHigh"/>], except unboundedness, which is always structural.</para>
/// <para>Breakevens are sorted; a P/L that is exactly zero over an interval contributes both ends of that interval.</para>
/// </summary>
public sealed record PayoffAnalysis(
    IReadOnlyList<double> Breakevens,
    double MaxProfit,
    double? MaxProfitPrice,
    double MaxLoss,
    double? MaxLossPrice,
    bool IsExact,
    double RangeLow,
    double RangeHigh)
{
    public bool IsMaxProfitUnbounded => double.IsPositiveInfinity(MaxProfit);

    public bool IsMaxLossUnbounded => double.IsNegativeInfinity(MaxLoss);

    /// <summary>MaxProfit / |MaxLoss|; NaN when either side is unbounded or there is no possible loss.</summary>
    public double RewardToRisk => IsMaxProfitUnbounded || IsMaxLossUnbounded || !(MaxLoss < 0) ? double.NaN : MaxProfit / -MaxLoss;
}