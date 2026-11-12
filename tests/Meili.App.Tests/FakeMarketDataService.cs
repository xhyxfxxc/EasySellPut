using Meili.Core.Abstractions;
using Meili.Core.Models;

namespace Meili.App.Tests;

/// <summary>Deterministic in-memory market data so view-models and pages can be exercised without the network.</summary>
public sealed class FakeMarketDataService : IMarketDataService
{
    public int QuoteCalls;
    public List<IReadOnlyList<string>> QuoteRequests { get; } = [];
    /// <summary>When set, quote calls throw this exception (to test error states).</summary>
    public Exception? QuoteFailure { get; set; }
    public Func<string, Quote>? QuoteFactory { get; set; }

    public static Quote MakeQuote(string symbol, double price = 123.45) => new()
    {
        Symbol = symbol, Name = symbol + " Inc.", Price = price, Change = 1.23, ChangePercent = 1.0, PreviousClose = price - 1.23,
        Volume = 12_345_678, Source = "yahoo", IsRealtime = true, DelayMinutes = 0, AsOf = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow,
        MarketState = MarketState.Regular,
    };

    public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        Interlocked.Increment(ref QuoteCalls);
        lock (QuoteRequests) QuoteRequests.Add(symbols.ToList());
        if (QuoteFailure is { } ex) return Task.FromException<IReadOnlyList<Quote>>(ex);
        IReadOnlyList<Quote> r = symbols.Select(s => QuoteFactory?.Invoke(s) ?? MakeQuote(s)).ToList();
        return Task.FromResult(r);
    }

    /// <summary>F7: source and adjustment basis of returned bars (default: yahoo, honouring the requested mode).</summary>
    public Func<BarRequest, (string Source, PriceAdjustment Basis)>? BarMeta { get; set; }
    public List<BarRequest> BarRequests { get; } = [];
    /// <summary>C5: override the chain's strikes (e.g. 400 strikes for the render-time baseline).</summary>
    public double[]? ChainStrikes { get; set; }

    public Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default)
    {
        lock (BarRequests) BarRequests.Add(request);
        var (src, basis) = BarMeta?.Invoke(request)
            ?? ("yahoo", request.Adjust == AdjustMode.Total && !request.Interval.IsIntraday() ? PriceAdjustment.SplitDividend : PriceAdjustment.Split);
        var bars = new List<Bar>();
        var t0 = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var step = request.Interval.IsIntraday() ? request.Interval.Duration() : TimeSpan.FromDays(1);
        var px = 100.0;
        for (var i = 0; i < 120; i++)
        {
            var o = px; var c = px + Math.Sin(i / 5.0) * 2; var h = Math.Max(o, c) + 1; var l = Math.Min(o, c) - 1;
            bars.Add(new Bar(t0 + step * i, o, h, l, c, 1_000_000 + i * 1000));
            px = c;
        }
        return Task.FromResult(new BarSeries
        {
            Symbol = request.Symbol, Interval = request.Interval, Bars = bars, Source = src, IsRealtime = true,
            AsOf = bars[^1].Time, FetchedAt = DateTimeOffset.UtcNow, Adjustment = basis,
        });
    }

    public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DateOnly>>([Exp(30), Exp(60)]);

    private static DateOnly Exp(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    public List<(string Symbol, DateOnly? Expiration)> ChainRequests { get; } = [];

    public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        lock (ChainRequests) ChainRequests.Add((symbol, expiration));
        var exp = expiration ?? Exp(30);
        var contracts = new List<OptionContract>();
        foreach (var k in ChainStrikes ?? [110.0, 115, 120, 125, 130, 135])
        {
            foreach (var right in new[] { OptionRight.Call, OptionRight.Put })
            {
                var intrinsic = right == OptionRight.Call ? Math.Max(0, 123.45 - k) : Math.Max(0, k - 123.45);
                contracts.Add(new OptionContract
                {
                    ContractSymbol = $"{symbol}{exp:yyMMdd}{(right == OptionRight.Call ? 'C' : 'P')}{k * 1000:00000000}",
                    Underlying = symbol, Right = right, Strike = k, Expiration = exp,
                    Bid = intrinsic + 2, Ask = intrinsic + 2.2, Last = intrinsic + 2.1, Volume = 100, OpenInterest = 1000,
                    ImpliedVolatility = 0.25, Delta = right == OptionRight.Call ? 0.5 : -0.5, Gamma = 0.02, Theta = -0.05, Vega = 0.1,
                });
            }
        }
        return Task.FromResult(new OptionChain
        {
            Underlying = symbol, UnderlyingPrice = 123.45, Expirations = [Exp(30), Exp(60)], Contracts = contracts, Expiration = exp,
            Source = "cboe", DelayMinutes = 15, FetchedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default) =>
        Task.FromResult(new Fundamentals { Symbol = symbol, Name = symbol + " Inc.", MarketCap = 1e12, DividendYield = 0.01, Source = "yahoo" });

    public Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default) =>
        Task.FromResult(new FinancialStatements
        {
            Symbol = symbol, Source = "sec",
            Lines = [new FinancialLine(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new DateOnly(2025, 12, 31), 1e11)],
        });

    public Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NewsItem>>(
            [new NewsItem { Title = "Test headline", Url = "https://example.com/a", Publisher = "Test", Published = DateTimeOffset.UtcNow.AddHours(-1), Source = "yahoo" }]);

    public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SymbolMatch>>([new SymbolMatch(query.ToUpperInvariant(), query.ToUpperInvariant() + " Inc.", "NMS", "EQUITY", "yahoo")]);
}

/// <summary>Offline IV readings for the app tests (the real source would reach CBOE over the network).</summary>
public sealed class FakeIvSource : Meili.Shared.Services.IIvSource
{
    public double Iv { get; set; } = 0.25;
    public Task<Meili.Core.Alerts.IvSample?> GetAsync(string symbol, DateTimeOffset now, CancellationToken ct = default) =>
        Task.FromResult<Meili.Core.Alerts.IvSample?>(new(symbol, Iv, Meili.Core.Alerts.IvBasis.Iv30, now, "fake"));
}

/// <summary>D7: proxy detection result under test control; never touches the real network stack.</summary>
public sealed class FakeNetwork : Meili.Shared.Services.INetworkController
{
    public bool NoProxyDetected { get; set; }
    public int Reconfigured { get; private set; }
    public string EffectiveDescription => NoProxyDetected ? "direct" : "127.0.0.1:7890";

    public Task<string> ReconfigureAsync()
    {
        Reconfigured++;
        return Task.FromResult(EffectiveDescription);
    }
}
