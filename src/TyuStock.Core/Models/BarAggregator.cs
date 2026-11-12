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
                cur = cur with { High = Math.Max(cur.High, b.High), Low = Math.Min(cur.Low, b.Low), Close = b.Close, Volume = cur.Volume + b.Volume };
            }
        }
        if (cur is not null) result.Add(cur);
        return result;
    }

    private static DateTime Bucket(DateTime et, BarInterval target) => target switch
    {
        BarInterval.Week1 => et.Date.AddDays(-(((int)et.DayOfWeek + 6) % 7)),
        BarInterval.Month1 => new DateTime(et.Year, et.Month, 1),
        BarInterval.Day1 => et.Date,
        _ => et.Date.AddMinutes(Math.Floor(et.TimeOfDay.TotalMinutes / target.Duration().TotalMinutes) * target.Duration().TotalMinutes),
    };
}