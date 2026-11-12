namespace Meili.Analytics;

/// <summary>B6: where today's IV sits in its own one-year history.</summary>
/// <param name="Rank">(current − low) / (high − low) × 100.</param>
/// <param name="Percentile">Share of past days with a lower IV, × 100.</param>
/// <param name="Low">Lowest IV of the period (including today).</param>
/// <param name="High">Highest IV of the period (including today).</param>
/// <param name="Days">Number of past days with a reading.</param>
public sealed record IvRankResult(double Rank, double Percentile, double Low, double High, int Days);

public static class IvRank
{
    /// <summary>Fewer recorded days than this give no rank (a few days of history would make any value look extreme).</summary>
    public const int MinDays = 20;

    /// <summary>
    /// <paramref name="daily"/> holds one IV per day (the caller keeps the day's last reading); the current value is compared
    /// with the days before today. Returns null with fewer than <see cref="MinDays"/> past days.
    /// </summary>
    public static IvRankResult? Compute(IReadOnlyList<(DateOnly Day, double Iv)> daily, DateOnly today, double current)
    {
        var past = daily.Where(d => d.Day < today && d.Day >= today.AddYears(-1)).Select(d => d.Iv).ToList();
        if (past.Count < MinDays) return null;
        var low = Math.Min(past.Min(), current);
        var high = Math.Max(past.Max(), current);
        var rank = high > low ? (current - low) / (high - low) * 100 : 50;
        var pct = past.Count(v => v < current) * 100.0 / past.Count;
        return new IvRankResult(rank, pct, low, high, past.Count);
    }

    /// <summary>The last reading of each day (samples must be in time order).</summary>
    public static List<(DateOnly Day, double Iv)> LastPerDay(IEnumerable<(DateOnly Day, double Iv)> samples)
    {
        var map = new SortedDictionary<DateOnly, double>();
        foreach (var (day, iv) in samples) map[day] = iv;
        return map.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
