using TyuStock.Core.Time;

namespace TyuStock.Core.Models;

public enum FreshnessKind
{
    /// <summary>The source declares the data real-time.</summary>
    Realtime,
    /// <summary>The source declares a fixed delay (e.g. 15 minutes).</summary>
    Delayed,
    /// <summary>The source declares nothing, but its own timestamp lets us measure the lag during regular hours.</summary>
    Measured,
    /// <summary>Neither declared nor measurable.</summary>
    Unknown,
}

/// <summary>How old a piece of data is, and how we know.</summary>
/// <param name="Lag">Declared delay (Delayed), measured lag (Measured), zero (Realtime) or null (Unknown).</param>
public sealed record Freshness(FreshnessKind Kind, TimeSpan? Lag, DateTimeOffset? AsOf)
{
    /// <summary>Best estimate of the delay in minutes for threshold checks; null when unknown.</summary>
    public double? LagMinutes => Lag?.TotalMinutes;
}

/// <summary>
/// One place that decides what to tell the user about data timeliness. Rules (F1/F6):
/// only a source's own real-time flag makes data "real-time"; a declared delay is shown as such;
/// otherwise the lag is measured from the source's timestamp, but only while the regular session is open
/// (outside it, an old last-trade time is expected and says nothing about delay; inside it, a timestamp from
/// a previous session correctly shows up as a very large lag); everything else is "unknown".
/// </summary>
public static class DataFreshness
{
    public static Freshness Evaluate(SourcedData data, DateTimeOffset now)
    {
        if (data.IsRealtime) return new Freshness(FreshnessKind.Realtime, TimeSpan.Zero, data.AsOf);
        if (data.DelayMinutes is > 0 and var d) return new Freshness(FreshnessKind.Delayed, TimeSpan.FromMinutes(d), data.AsOf);
        if (data.DelayMinutes == 0) return new Freshness(FreshnessKind.Realtime, TimeSpan.Zero, data.AsOf);
        if (data.AsOf is { } asOf && MarketClock.GetSession(now) == MarketSession.Regular)
        {
            var lag = now - asOf;
            if (lag < TimeSpan.Zero) lag = TimeSpan.Zero; // clock skew between us and the source
            return new Freshness(FreshnessKind.Measured, lag, asOf);
        }
        return new Freshness(FreshnessKind.Unknown, null, data.AsOf);
    }
}

public enum StaleReason
{
    None,
    /// <summary>We have not managed to refresh this quote for several refresh cycles (sources failing / offline).</summary>
    NotUpdated,
    /// <summary>Refreshed fine, but the source's own timestamp lags far behind during regular hours.</summary>
    SourceLagging,
}

/// <summary>
/// D8: when a displayed price should carry a "stale" marker. Builds on <see cref="DataFreshness"/>: a declared delay
/// (e.g. 15 minutes) is expected and never "stale"; only a quote we failed to refresh, or a measured lag far beyond
/// normal, is.
/// </summary>
public static class QuoteStaleness
{
    /// <summary>Measured lag above this during the regular session counts as stale.</summary>
    public static readonly TimeSpan MaxMeasuredLag = TimeSpan.FromMinutes(20);

    /// <summary>A quote not refreshed for this long (three refresh cycles, at least two minutes) is stale.</summary>
    public static TimeSpan MaxAge(DateTimeOffset now)
    {
        var three = Time.MarketClock.QuoteRefreshInterval(now) * 3;
        return three < TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : three;
    }

    public static StaleReason Check(SourcedData data, DateTimeOffset now)
    {
        if (data.FetchedAt is { } fetched && now - fetched > MaxAge(now)) return StaleReason.NotUpdated;
        var f = DataFreshness.Evaluate(data, now);
        if (f.Kind == FreshnessKind.Measured && f.Lag > MaxMeasuredLag) return StaleReason.SourceLagging;
        return StaleReason.None;
    }
}
