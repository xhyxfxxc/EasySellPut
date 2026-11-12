using Meili.Analytics;

namespace Meili.Analytics.Tests;

public class IvRankTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    private static List<(DateOnly, double)> Days(int n, Func<int, double> iv) =>
        Enumerable.Range(1, n).Select(i => (Today.AddDays(-i), iv(i))).ToList();

    [Fact]
    public void Rank_and_percentile_against_the_past_year()
    {
        var hist = Days(100, i => 0.10 + i * 0.002);                         // 0.102 … 0.30
        var r = IvRank.Compute(hist, Today, 0.20)!;
        Assert.Equal(100, r.Days);
        Assert.Equal((0.20 - 0.102) / (0.30 - 0.102) * 100, r.Rank, 6);
        Assert.Equal(49, r.Percentile, 6);                                    // 0.102 … 0.198 are below
        Assert.Equal(100, IvRank.Compute(hist, Today, 0.50)!.Rank, 6);       // new high
    }

    [Fact]
    public void Needs_enough_history_and_ignores_older_than_a_year_and_today()
    {
        Assert.Null(IvRank.Compute(Days(IvRank.MinDays - 1, _ => 0.2), Today, 0.2));
        var old = Enumerable.Range(0, 50).Select(i => (Today.AddYears(-2).AddDays(i), 0.9)).ToList();
        Assert.Null(IvRank.Compute(old, Today, 0.2));
        var withToday = Days(IvRank.MinDays, _ => 0.2).Append((Today, 0.9)).ToList();
        Assert.Equal(IvRank.MinDays, IvRank.Compute(withToday, Today, 0.2)!.Days);
    }

    [Fact]
    public void Last_reading_of_each_day_wins()
    {
        var d = IvRank.LastPerDay([(Today, 0.1), (Today, 0.3), (Today.AddDays(-1), 0.2)]);
        Assert.Equal([(Today.AddDays(-1), 0.2), (Today, 0.3)], d);
    }
}
