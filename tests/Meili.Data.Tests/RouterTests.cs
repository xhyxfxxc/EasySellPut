using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Data.Http;
using Meili.Data.Routing;

namespace Meili.Data.Tests;

public class RouterTests
{
    private sealed class FakeQuotes(string id, Func<IReadOnlyList<string>, IReadOnlyList<Quote>> f) : IQuoteProvider, IBarsProvider
    {
        public int Calls;
        public string Id => id;
        public string DisplayName => id;
        public DataCapability Capabilities => DataCapability.Quote | DataCapability.DailyBars;
        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
        { Calls++; return Task.FromResult(f(symbols)); }
        public bool Supports(BarInterval i) => i == BarInterval.Day1;
        public Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default) => Task.FromResult(new BarSeries
        {
            Symbol = r.Symbol, Interval = r.Interval, Source = id,
            Bars = Enumerable.Range(0, 10).Select(i => new Bar(new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero).AddDays(i), 1, 2 + i, 0.5, 1 + i, 100)).ToList(),
        });
    }

    private static Quote Q(string s, string src) => new() { Symbol = s, Price = 10, Source = src };
    private static IReadOnlyList<Quote> Fail(IReadOnlyList<string> _) => throw new DataSourceException("x", DataErrorKind.Network, "SSL UNEXPECTED_EOF");

    private static FailoverRouter Router(params IDataProvider[] p) =>
        new(p, new HealthTracker(), new Dictionary<DataCapability, string[]>
        {
            [DataCapability.Quote] = p.Select(x => x.Id).ToArray(), [DataCapability.DailyBars] = p.Select(x => x.Id).ToArray(),
        });

    [Fact]
    public async Task Fails_over_to_next_source_and_records_health()
    {
        var a = new FakeQuotes("a", Fail);
        var b = new FakeQuotes("b", s => s.Select(x => Q(x, "b")).ToList());
        var r = Router(a, b);
        var q = await r.GetQuotesAsync(["AAPL"]);
        Assert.Equal("b", Assert.Single(q).Source);
        Assert.Equal(1, r.Health.Get("a")!.Failures);
        Assert.Equal(1, r.Health.Get("b")!.Successes);
        Assert.Equal(HealthState.Down, r.Health.Get("a")!.State(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Fills_symbols_missing_from_primary_and_keeps_requested_order()
    {
        var a = new FakeQuotes("a", s => s.Where(x => x != "BRK.B").Select(x => Q(x, "a")).ToList());
        var b = new FakeQuotes("b", s => s.Select(x => Q(x, "b")).ToList());
        var q = await Router(a, b).GetQuotesAsync(["brk.b", "AAPL", "aapl"]);
        Assert.Equal([("BRK.B", "b"), ("AAPL", "a")], q.Select(x => (x.Symbol, x.Source)));
    }

    [Fact]
    public async Task Opens_circuit_after_repeated_failures_and_skips_source()
    {
        var a = new FakeQuotes("a", Fail);
        var b = new FakeQuotes("b", s => s.Select(x => Q(x, "b")).ToList());
        var r = Router(a, b);
        for (var i = 0; i < HealthTracker.FailuresToOpen; i++) await r.GetQuotesAsync(["X"]);
        Assert.False(r.Health.IsAvailable("a"));
        await r.GetQuotesAsync(["X"]);
        Assert.Equal(HealthTracker.FailuresToOpen, a.Calls); // skipped while open
        Assert.NotNull(r.Health.Get("a")!.CircuitOpenUntil);
    }

    [Fact]
    public async Task Empty_results_do_not_trip_the_breaker()
    {
        var a = new FakeQuotes("a", _ => []);
        var b = new FakeQuotes("b", s => s.Select(x => Q(x, "b")).ToList());
        var r = Router(a, b);
        for (var i = 0; i < 5; i++) await r.GetQuotesAsync(["X"]);
        Assert.True(r.Health.IsAvailable("a"));
        Assert.Equal(5, a.Calls);
    }

    [Fact]
    public async Task Throws_with_every_source_error_when_all_fail()
    {
        var ex = await Assert.ThrowsAsync<AllSourcesFailedException>(() => Router(new FakeQuotes("a", Fail), new FakeQuotes("b", Fail)).GetQuotesAsync(["X"]));
        Assert.Equal(["a", "b"], ex.Errors.Select(e => e.Source));
    }

    [Fact]
    public async Task Aggregates_weekly_bars_locally_from_daily()
    {
        var s = await Router(new FakeQuotes("a", Fail)).GetBarsAsync(new BarRequest("AAPL", BarInterval.Week1));
        Assert.Equal(BarInterval.Week1, s.Interval);
        Assert.Equal(2, s.Bars.Count); // Mon 2 Nov .. Wed 11 Nov 2026 → two ISO weeks
        Assert.Equal((1.0, 8.0, 7.0, 700.0), (s.Bars[0].Open, s.Bars[0].High, s.Bars[0].Close, s.Bars[0].Volume)); // 2–8 Nov
    }
}