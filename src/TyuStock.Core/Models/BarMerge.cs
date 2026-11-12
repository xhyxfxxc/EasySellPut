namespace TyuStock.Core.Models;

public enum BarMergeOutcome
{
    /// <summary>Tail appended / last bars updated.</summary>
    Merged,
    /// <summary>Nothing cached: the tail is the whole series.</summary>
    Initial,
    /// <summary>
    /// Overlapping bars disagree (split or dividend re-adjustment, a different source's adjustment, or bad data):
    /// the cache must be replaced by a full download, never patched.
    /// </summary>
    Diverged,
    /// <summary>The tail does not reach back to the cached data (gap): full download needed.</summary>
    Gap,
}

/// <summary>
/// B2/C2: merges a freshly downloaded tail into cached bars. The tail is requested with a few bars of overlap; those
/// overlapping closes must agree with the cache, otherwise the history was re-adjusted and is replaced wholesale.
/// The first tail bar is used only for the check (for locally aggregated intervals it can be a partial bucket).
/// </summary>
public static class BarMerge
{
    /// <summary>Relative close tolerance for "same bar" (sources round differently).</summary>
    public const double Tolerance = 0.002;

    public static (IReadOnlyList<Bar> Bars, BarMergeOutcome Outcome) Merge(IReadOnlyList<Bar> cached, IReadOnlyList<Bar> tail)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(tail);
        if (cached.Count == 0) return (tail, BarMergeOutcome.Initial);
        if (tail.Count == 0) return (cached, BarMergeOutcome.Merged);

        var lastCached = cached[^1].Time;
        var firstTail = tail[0].Time;
        if (firstTail > lastCached) return (cached, BarMergeOutcome.Gap);

        var byTime = new Dictionary<DateTimeOffset, Bar>(cached.Count);
        foreach (var b in cached) byTime[b.Time] = b;
        // Compare closes of bars both sides consider finished: skip the cached last bar (it may have been in progress)
        // and the first tail bar (possibly a partial aggregate).
        for (int i = 1; i < tail.Count; i++)
        {
            var t = tail[i];
            if (t.Time >= lastCached) break;
            if (!byTime.TryGetValue(t.Time, out var c)) continue;
            if (!Same(c.Close, t.Close)) return (cached, BarMergeOutcome.Diverged);
        }

        var result = new List<Bar>(cached.Count + tail.Count);
        bool firstInCache = byTime.ContainsKey(firstTail);
        foreach (var b in cached)
        {
            if (b.Time < firstTail || (firstInCache && b.Time == firstTail)) result.Add(b);
            else break;
        }
        for (int i = firstInCache ? 1 : 0; i < tail.Count; i++) result.Add(tail[i]);
        return (result, BarMergeOutcome.Merged);
    }

    private static bool Same(double a, double b) =>
        Math.Abs(a - b) <= Tolerance * Math.Max(Math.Abs(a), Math.Abs(b));

    /// <summary>Where the tail download should start: <paramref name="overlapBars"/> bars before the last cached bar.</summary>
    public static DateTimeOffset TailStart(IReadOnlyList<Bar> cached, int overlapBars = 5)
    {
        if (cached.Count == 0) throw new ArgumentException("No cached bars.", nameof(cached));
        var i = Math.Max(0, cached.Count - 1 - overlapBars);
        return cached[i].Time;
    }
}
