namespace TyuStock.Core.Alerts;

/// <summary>What an IV sample measures.</summary>
public enum IvBasis
{
    /// <summary>The source's 30-day implied volatility index (CBOE "iv30").</summary>
    Iv30,
    /// <summary>Fallback: at-the-money IV of the listed expiration closest to 30 days.</summary>
    AtmNearest30,
}

/// <summary>B1: one implied-volatility reading for an underlying.</summary>
/// <param name="Iv">Fraction (0.32 = 32%).</param>
/// <param name="AsOf">Market time of the underlying data when the source states it.</param>
public sealed record IvSample(string Symbol, double Iv, IvBasis Basis, DateTimeOffset? AsOf, string Source,
    DateOnly? Expiration = null, int? DaysToExpiry = null)
{
    public double Percent => Iv * 100;
}

/// <summary>B1: whether an IV sample is fresh enough to trigger alerts (same limit as quotes, F3).</summary>
public static class IvSampleGate
{
    public static AlertQuoteUse Check(IvSample sample, DateTimeOffset now, int maxDelayMinutes)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.AsOf is not { } asOf) return AlertQuoteUse.UsableDelayUnknown;
        return now - asOf > TimeSpan.FromMinutes(Math.Max(1, maxDelayMinutes)) ? AlertQuoteUse.TooStale : AlertQuoteUse.Usable;
    }
}
