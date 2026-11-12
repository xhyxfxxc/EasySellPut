using Meili.Core.Models;

namespace Meili.Core.Tests;

/// <summary>D8: when a price gets the "stale" marker.</summary>
public class QuoteStalenessTests
{
    // Wednesday 2026-11-11 15:00 ET (regular session) and Saturday (closed).
    private static readonly DateTimeOffset Regular = new(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Weekend = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static Quote Q(DateTimeOffset? fetched, DateTimeOffset? asOf, bool realtime = false, int? delay = null) => new()
    {
        Symbol = "AAPL", Price = 1, Source = "x", FetchedAt = fetched, AsOf = asOf, IsRealtime = realtime, DelayMinutes = delay,
    };

    [Fact]
    public void Fresh_quote_is_not_stale()
        => Assert.Equal(StaleReason.None, QuoteStaleness.Check(Q(Regular.AddSeconds(-5), Regular.AddSeconds(-6)), Regular));

    [Fact]
    public void Quote_not_refreshed_for_several_cycles_is_stale()
    {
        Assert.Equal(StaleReason.NotUpdated, QuoteStaleness.Check(Q(Regular.AddMinutes(-3), Regular.AddMinutes(-3), realtime: true), Regular));
        Assert.Equal(StaleReason.None, QuoteStaleness.Check(Q(Regular.AddSeconds(-90), Regular.AddSeconds(-90), realtime: true), Regular));
    }

    [Fact]
    public void Declared_delay_is_expected_not_stale()
        => Assert.Equal(StaleReason.None, QuoteStaleness.Check(Q(Regular.AddSeconds(-5), Regular.AddMinutes(-15), delay: 15), Regular));

    [Fact]
    public void Large_measured_lag_in_session_is_stale_but_not_after_hours()
    {
        Assert.Equal(StaleReason.SourceLagging, QuoteStaleness.Check(Q(Regular.AddSeconds(-5), Regular.AddMinutes(-45)), Regular));
        Assert.Equal(StaleReason.None, QuoteStaleness.Check(Q(Weekend.AddSeconds(-5), Weekend.AddDays(-1)), Weekend));
    }

    [Fact]
    public void Max_age_is_three_cycles_with_a_two_minute_floor()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), QuoteStaleness.MaxAge(Regular));   // 3 × 5 s → floor
        Assert.Equal(TimeSpan.FromMinutes(3), QuoteStaleness.MaxAge(Weekend));   // 3 × 60 s
    }
}
