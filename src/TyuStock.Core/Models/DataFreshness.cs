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
        ArgumentNullException.ThrowIfNull(data);
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
