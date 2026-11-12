using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Quotes;

namespace TyuStock.Core.Tests;

/// <summary>C1: one fetch for all subscribers, short TTL, single-flight, health counters.</summary>
public class QuoteHubTests
{
    private sealed class Counting : IMarketDataService
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Exception? Fail { get; set; }
        public TaskCompletionSource? Gate { get; set; }

        public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
        {
            Calls.Add(symbols.ToList());
            if (Gate is { } g) await g.Task;
            if (Fail is { } f) throw f;
            return symbols.Select(s => new Quote { Symbol = s, Price = 100, Source = "x" }).ToList();
        }
        public Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static (QuoteHub Hub, Counting Data, Func<DateTimeOffset> Clock, Action<double> Advance) Make()
    {
        var data = new Counting();
        var now = new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
        var hub = new QuoteHub(data) { Now = () => now };
        return (hub, data, () => now, s => now = now.AddSeconds(s));
    }

    [Fact]
    public async Task Poll_fetches_the_union_of_all_subscribers_once_and_broadcasts()
    {
        var (hub, data, _, _) = Make();
        hub.Subscribe("dashboard", ["AAPL", "msft"]);
        hub.Subscribe("watchlist", ["MSFT", "NVDA"]);
        hub.Subscribe("alerts", ["aapl", "TSLA"]);
        QuoteBatch? seen = null;
        hub.Updated += (_, b) => seen = b;
        await hub.PollAsync();
        var call = Assert.Single(data.Calls);
        Assert.Equal(["AAPL", "MSFT", "NVDA", "TSLA"], call.Order(StringComparer.Ordinal));
        Assert.NotNull(seen);
        Assert.True(seen!.Covers("nvda"));
        Assert.Equal(100, seen.Find("tsla")!.Price);
    }

    [Fact]
    public async Task Poll_without_subscribers_does_nothing_and_unsubscribe_removes_symbols()
    {
        var (hub, data, _, _) = Make();
        Assert.Null(await hub.PollAsync());
        hub.Subscribe("a", ["AAPL"]);
        hub.Subscribe("b", ["MSFT"]);
        hub.Unsubscribe("a");
        await hub.PollAsync();
        Assert.Equal(["MSFT"], data.Calls.Single());
    }

    [Fact]
    public async Task Reads_within_ttl_come_from_memory_and_stale_subscribers_piggyback()
    {
        var (hub, data, _, advance) = Make();
        hub.Subscribe("alerts", ["TSLA"]);
        await hub.GetQuotesAsync(["AAPL"]);
        Assert.Equal(["AAPL", "TSLA"], data.Calls[0]);          // TSLA rode along
        advance(1);
        var q = await hub.GetQuotesAsync(["aapl", "TSLA"]);
        Assert.Equal(2, q.Count);
        Assert.Single(data.Calls);                              // both fresh: no request
        advance(2);
        await hub.GetQuotesAsync(["AAPL"]);
        Assert.Equal(2, data.Calls.Count);
    }

    [Fact]
    public async Task Concurrent_identical_reads_share_one_request()
    {
        var (hub, data, _, _) = Make();
        data.Gate = new TaskCompletionSource();
        var a = hub.GetQuotesAsync(["AAPL", "MSFT"]);
        var b = hub.GetQuotesAsync(["MSFT"]);
        data.Gate.SetResult();
        await Task.WhenAll(a, b);
        Assert.Single(data.Calls);
        Assert.Single(await b);
    }

    [Fact]
    public async Task Failures_are_counted_broadcast_and_reset_on_success()
    {
        var (hub, data, _, advance) = Make();
        hub.Subscribe("alerts", ["AAPL"]);
        data.Fail = new InvalidOperationException("down");
        var errors = 0;
        hub.Updated += (_, b) => { if (b.Error is not null) errors++; };
        await hub.PollAsync();
        await hub.PollAsync();
        Assert.Equal(2, hub.ConsecutiveFailures);
        Assert.Equal(2, errors);
        Assert.Equal("down", hub.LastError!.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.GetQuotesAsync(["MSFT"]));
        Assert.Equal(3, hub.ConsecutiveFailures);
        data.Fail = null;
        advance(10);
        await hub.PollAsync();
        Assert.Equal(0, hub.ConsecutiveFailures);
        Assert.NotNull(hub.LastSuccess);
        Assert.Null(hub.LastError);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_source_failure()
    {
        var (hub, data, _, _) = Make();
        data.Fail = new OperationCanceledException();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        data.Fail = null;
        data.Gate = new TaskCompletionSource();
        var t = hub.GetQuotesAsync(["AAPL"], cts.Token);
        data.Gate.SetException(new OperationCanceledException(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);
        Assert.Equal(0, hub.ConsecutiveFailures);
    }
}
