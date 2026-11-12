namespace Meili.Analytics;

/// <summary>One contract of a single expiration, as needed for an at-the-money IV estimate.</summary>
public readonly record struct AtmCandidate(OptionRight Right, double Strike, double? ProviderIv, double? Bid, double? Ask, double? Last);

/// <summary>
/// B1 fallback when the source has no 30-day IV index: at-the-money implied volatility of the listed expiration
/// closest to 30 days. Call and put IV at the strike nearest the spot are averaged (put-call parity makes them close;
/// averaging cancels most of the early-exercise and dividend bias of either side).
/// </summary>
public static class AtmVolatility
{
    public const int TargetDays = 30;
    /// <summary>Expirations shorter than this are skipped: their IV is dominated by event and gamma noise.</summary>
    public const int MinDays = 7;
    /// <summary>How many strikes nearest the spot are tried before giving up.</summary>
    public const int MaxStrikesTried = 3;

    /// <summary>Expiration closest to <see cref="TargetDays"/> with at least <see cref="MinDays"/> to go; ties prefer the later one.</summary>
    public static DateOnly? PickExpiration(IEnumerable<DateOnly> expirations, DateTimeOffset now)
    {
        DateOnly? best = null;
        var bestDistance = int.MaxValue;
        foreach (var e in expirations.Distinct().OrderBy(e => e))
        {
            var days = OptionTime.DaysToExpiry(e, now);
            if (days < MinDays) continue;
            var d = Math.Abs(days - TargetDays);
            if (d <= bestDistance) { best = e; bestDistance = d; }
            if (days > TargetDays && d > bestDistance) break;
        }
        return best;
    }

    /// <summary>ATM IV as a fraction, or null when no strike near the spot has a usable IV or price.</summary>
    public static AtmEstimate? Estimate(IReadOnlyList<AtmCandidate> contracts, double spot, DateOnly expiration, DateTimeOffset now,
        double rate, double dividendYield = 0)
    {
        if (!(spot > 0) || !double.IsFinite(spot) || contracts.Count == 0) return null;
        var years = OptionTime.YearsToExpiry(expiration, now);
        if (!(years > 0)) return null;
        var strikes = contracts.Where(c => c.Strike > 0 && double.IsFinite(c.Strike)).Select(c => c.Strike).Distinct()
            .OrderBy(k => Math.Abs(k - spot)).ThenBy(k => k).Take(MaxStrikesTried);
        foreach (var k in strikes)
        {
            var ivs = new List<double>(2);
            foreach (var c in contracts.Where(c => c.Strike == k))
            {
                var r = ImpliedVolatility.Resolve(c.Right, c.ProviderIv, c.Bid, c.Ask, c.Last, spot, k, years, rate, dividendYield);
                if (r is { } v) ivs.Add(v.Value);
            }
            if (ivs.Count > 0) return new AtmEstimate(ivs.Average(), k, ivs.Count);
        }
        return null;
    }
}

/// <param name="Iv">Fraction.</param><param name="Strike">Strike used.</param><param name="Legs">1 or 2 (call and/or put).</param>
public readonly record struct AtmEstimate(double Iv, double Strike, int Legs);
