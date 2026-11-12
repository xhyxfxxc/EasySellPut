namespace Meili.Analytics;

/// <summary>Rate, yield and valuation time used when filling missing IV and Greeks.</summary>
public sealed class OptionEnrichmentSettings
{
    /// <summary>Continuously compounded risk-free rate (0.04 = 4%).</summary>
    public double RiskFreeRate { get; init; } = 0.04;

    /// <summary>Continuously compounded dividend yield (0.005 = 0.5%).</summary>
    public double DividendYield { get; init; } = 0.0;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public TimeOnly? SettlementTime { get; init; }
}

/// <summary>IV/Greeks after provider values have been filtered and any gaps filled with Black-Scholes.</summary>
public readonly record struct EnrichedOption(
    double? ImpliedVolatility,
    IvOrigin? IvOrigin,
    double? Delta,
    double? Gamma,
    double? Theta,
    double? Vega,
    double? Rho,
    double? TheoreticalPrice,
    bool InTheMoney,
    bool GreeksComputedLocally);

/// <summary>
/// Completes an option quote for display: drops implausible provider IVs (see <see cref="IvFilter"/>),
/// solves IV from mid/last when needed, and computes missing Greeks with <see cref="BlackScholes"/>.
/// </summary>
public static class OptionEnricher
{
    public static EnrichedOption Enrich(
        OptionRight right,
        double spot,
        double strike,
        DateOnly expiration,
        double? providerIv,
        double? bid,
        double? ask,
        double? last,
        double? delta = null,
        double? gamma = null,
        double? theta = null,
        double? vega = null,
        double? rho = null,
        double? theoreticalPrice = null,
        bool? inTheMoney = null,
        OptionEnrichmentSettings? settings = null)
    {
        var s = settings ?? new OptionEnrichmentSettings();
        bool itm = inTheMoney ?? (right == OptionRight.Call ? spot > strike : spot < strike);
        if (!(spot > 0) || !(strike > 0) || !double.IsFinite(spot) || !double.IsFinite(strike))
            return new EnrichedOption(IvFilter.Clean(providerIv), IvFilter.IsPlausible(providerIv) ? IvOrigin.Provider : null,
                Finite(delta), Finite(gamma), Finite(theta), Finite(vega), Finite(rho), Finite(theoreticalPrice), itm, false);

        double years = OptionTime.YearsToExpiry(expiration, s.Now, s.SettlementTime);
        var resolved = ImpliedVolatility.Resolve(right, providerIv, bid, ask, last, spot, strike, years, s.RiskFreeRate, s.DividendYield);
        double? iv = resolved?.Value;

        bool haveAll = Finite(delta) is not null && Finite(gamma) is not null && Finite(theta) is not null && Finite(vega) is not null;
        bool keepProviderGreeks = resolved is { Origin: IvOrigin.Provider } && haveAll;
        if (keepProviderGreeks)
            return new EnrichedOption(iv, resolved?.Origin, Finite(delta), Finite(gamma), Finite(theta), Finite(vega), Finite(rho), Finite(theoreticalPrice), itm, false);

        if (iv is not { } sigma)
            return new EnrichedOption(null, resolved?.Origin, Finite(delta), Finite(gamma), Finite(theta), Finite(vega), Finite(rho), Finite(theoreticalPrice), itm, false);

        var g = BlackScholes.Greeks(right, spot, strike, years, s.RiskFreeRate, s.DividendYield, sigma);
        if (double.IsNaN(g.Price))
            return new EnrichedOption(iv, resolved?.Origin, Finite(delta), Finite(gamma), Finite(theta), Finite(vega), Finite(rho), Finite(theoreticalPrice), itm, false);

        return new EnrichedOption(
            iv,
            resolved?.Origin,
            Finite(delta) ?? Finite(g.Delta),
            Finite(gamma) ?? Finite(g.Gamma),
            Finite(theta) ?? Finite(g.Theta),
            Finite(vega) ?? Finite(g.Vega),
            Finite(rho) ?? Finite(g.Rho),
            Finite(theoreticalPrice) ?? Finite(g.Price),
            itm,
            GreeksComputedLocally: Finite(delta) is null || Finite(gamma) is null || Finite(theta) is null || Finite(vega) is null);
    }

    private static double? Finite(double? v) => v is { } x && double.IsFinite(x) ? x : null;
}