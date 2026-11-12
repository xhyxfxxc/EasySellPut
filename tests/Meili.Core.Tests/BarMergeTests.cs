using Meili.Core.Models;

namespace Meili.Core.Tests;

public class BarMergeTests
{
    private static readonly DateTimeOffset D0 = new(2026, 11, 2, 21, 0, 0, TimeSpan.Zero);
    private static Bar B(int day, double c) => Bar.FromSource(D0.AddDays(day), c, c + 1, c - 1, c, 100);
    private static List<Bar> Series(int from, int to, Func<int, double> close) =>
        Enumerable.Range(from, to - from + 1).Select(d => B(d, close(d))).ToList();

    [Fact]
    public void Tail_updates_last_bar_and_appends_new_ones()
    {
        var cached = Series(0, 9, d => 100 + d);
        cached[^1] = B(9, 108.5);                          // last bar captured mid-session
        var tail = Series(4, 11, d => 100 + d);            // overlap 4..9, new 10..11
        var (bars, outcome) = BarMerge.Merge(cached, tail);
        Assert.Equal(BarMergeOutcome.Merged, outcome);
        Assert.Equal(12, bars.Count);
        Assert.Equal(109, bars[9].Close);                  // in-progress bar replaced
        Assert.Equal(111, bars[^1].Close);
        Assert.True(bars.Zip(bars.Skip(1)).All(p => p.First.Time < p.Second.Time));
        Assert.Same(cached[4], bars[4]);                   // first tail bar is only used for the check
    }

    [Fact]
    public void Readjusted_history_is_detected()
    {
        var cached = Series(0, 9, d => 100 + d);
        var tail = Series(4, 11, d => (100 + d) / 2.0);    // 2:1 split re-adjusted everything
        Assert.Equal(BarMergeOutcome.Diverged, BarMerge.Merge(cached, tail).Outcome);
    }

    [Fact]
    public void Small_rounding_differences_are_tolerated()
    {
        var cached = Series(0, 9, d => 100 + d);
        var tail = Series(4, 10, d => 100 + d + 0.05);
        Assert.Equal(BarMergeOutcome.Merged, BarMerge.Merge(cached, tail).Outcome);
    }

    [Fact]
    public void Gap_and_empty_cases()
    {
        var cached = Series(0, 5, d => 100 + d);
        Assert.Equal(BarMergeOutcome.Gap, BarMerge.Merge(cached, Series(8, 9, d => 100 + d)).Outcome);
        Assert.Equal(BarMergeOutcome.Initial, BarMerge.Merge([], cached).Outcome);
        Assert.Same(cached, BarMerge.Merge(cached, []).Bars);
        Assert.Equal(D0.AddDays(0), BarMerge.TailStart(cached, 5));
        Assert.Equal(D0.AddDays(3), BarMerge.TailStart(cached, 2));
    }
}
