using TyuStock.Core.Time;

namespace TyuStock.Core.Models;

/// <summary>Builds coarser bars locally (1m → 5m/15m/30m/1h, 1d → 1w/1mo) for sources that only offer the fine interval.</summary>
public static class BarAggregator
{
    public static IReadOnlyList<Bar> Aggregate(IReadOnlyList<Bar> bars, BarInterval target)
    {
        if (bars.Count == 0) return bars;
        var result = new List<Bar>();
        DateTime? key = null;
        Bar? cur = null;
        foreach (var b in bars)
        {
            var k = Bucket(MarketClock.ToEastern(b.Time), target);
            if (key != k || cur is null)
            {
                if (cur is not null) result.Add(cur);
                key = k;
                cur = b with { Time = MarketClock.FromEastern(k) };
            }
            else
            {
                cur = cur with
                {
                    High = MaxKnown(cur.High, b.High),
                    Low = MinKnown(cur.Low, b.Low),
                    Close = b.Close,
                    Volume = cur.Volume + b.Volume,
                    Incomplete = cur.Incomplete || b.Incomplete,
                };
            }
        }
        if (cur is not null) result.Add(cur);
        return result;
    }

    // Math.Max/Min propagate NaN, which would wipe out a whole bucket because of one close-only point. Ignore NaN
    // here; the bucket is still flagged Incomplete (its true extreme may be outside what we saw).
    private static double MaxKnown(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
    private static double MinKnown(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);

    private static DateTime Bucket(DateTime et, BarInterval target) => target switch
    {
        BarInterval.Week1 => et.Date.AddDays(-(((int)et.DayOfWeek + 6) % 7)),
        BarInterval.Month1 => new DateTime(et.Year, et.Month, 1),
        BarInterval.Day1 => et.Date,
        _ => et.Date.AddMinutes(Math.Floor(et.TimeOfDay.TotalMinutes / target.Duration().TotalMinutes) * target.Duration().TotalMinutes),
    };
}