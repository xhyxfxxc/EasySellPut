using TyuStock.Core.Diagnostics;
using TyuStock.Core.Models;
using TyuStock.Core.Time;

namespace TyuStock.Core.Tests;

public class FreshnessTests
{
    // 2026-11-12 is a Thursday; 10:30 ET is regular session, 20:00 ET is closed.
    private static readonly DateTimeOffset RegularNow = MarketClock.FromEastern(new DateTime(2026, 11, 12, 10, 30, 0));
    private static readonly DateTimeOffset ClosedNow = MarketClock.FromEastern(new DateTime(2026, 11, 12, 21, 0, 0));

    private static Quote Q(bool rt = false, int? delay = null, DateTimeOffset? asOf = null) =>
        new() { Symbol = "AAPL", Price = 1, IsRealtime = rt, DelayMinutes = delay, AsOf = asOf };

    [Fact]
    public void Realtime_only_when_the_source_says_so()
    {
        Assert.Equal(FreshnessKind.Realtime, DataFreshness.Evaluate(Q(rt: true), RegularNow).Kind);
        Assert.Equal(FreshnessKind.Realtime, DataFreshness.Evaluate(Q(delay: 0), RegularNow).Kind);
    }

    [Fact]
    public void Declared_delay_is_reported_as_declared()
    {
        var f = DataFreshness.Evaluate(Q(delay: 15, asOf: RegularNow.AddMinutes(-16)), RegularNow);
        Assert.Equal(FreshnessKind.Delayed, f.Kind);
        Assert.Equal(15, f.LagMinutes);
    }

    [Fact]
    public void Undeclared_delay_is_measured_from_the_source_timestamp_in_regular_hours()
    {
        var f = DataFreshness.Evaluate(Q(asOf: RegularNow.AddMinutes(-3)), RegularNow);
        Assert.Equal(FreshnessKind.Measured, f.Kind);
        Assert.Equal(3, f.LagMinutes!.Value, 3);
    }

    [Fact]
    public void A_previous_session_timestamp_during_regular_hours_shows_as_a_large_lag()
    {
        var yesterdayClose = MarketClock.FromEastern(new DateTime(2026, 11, 11, 16, 0, 0));
        var f = DataFreshness.Evaluate(Q(asOf: yesterdayClose), RegularNow);
        Assert.Equal(FreshnessKind.Measured, f.Kind);
        Assert.True(f.LagMinutes > 60 * 18);
    }

    [Fact]
    public void Outside_regular_hours_an_undeclared_delay_is_unknown()
    {
        Assert.Equal(FreshnessKind.Unknown, DataFreshness.Evaluate(Q(asOf: ClosedNow.AddHours(-5)), ClosedNow).Kind);
    }

    [Fact]
    public void Without_timestamp_or_declaration_the_delay_is_unknown()
    {
        var f = DataFreshness.Evaluate(Q(), RegularNow);
        Assert.Equal(FreshnessKind.Unknown, f.Kind);
        Assert.Null(f.LagMinutes);
    }

    [Fact]
    public void Clock_skew_never_gives_a_negative_lag()
    {
        var f = DataFreshness.Evaluate(Q(asOf: RegularNow.AddSeconds(30)), RegularNow);
        Assert.Equal(TimeSpan.Zero, f.Lag);
    }
}

[Collection("AppLog")]
public class AppLogTests
{
    [Fact]
    public void Writes_daily_file_and_raises_event()
    {
        var dir = Path.Combine(Path.GetTempPath(), "applog-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.Initialize(dir);
            string? seen = null;
            // AppLog is process-wide and other tests run in parallel: only look at our own area.
            void H(LogLevel l, string a, string m) { if (a == "test") seen = $"{l}:{a}:{m}"; }
            AppLog.Written += H;
            AppLog.Warn("test", "hello", new InvalidOperationException("boom"));
            AppLog.Written -= H;
            var file = Directory.GetFiles(Path.Combine(dir, "logs"), "app-*.log").Single();
            // Other parallel tests may be appending to the same file: read with shared write access.
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var text = new StreamReader(fs).ReadToEnd();
            Assert.Contains("WARN  [test] hello | InvalidOperationException: boom", text);
            Assert.Equal("Warn:test:hello", seen);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
