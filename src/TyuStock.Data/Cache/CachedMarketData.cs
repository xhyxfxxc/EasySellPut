using System.Collections.Concurrent;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Diagnostics;
using TyuStock.Core.Models;

namespace TyuStock.Data.Cache;

/// <summary>
/// B2 + C2: wraps the failover router and keeps K-line history in <see cref="MarketDb"/>.
/// <list type="bullet">
/// <item>First request: full download, saved.</item>
/// <item>Later requests: only a short tail (a few bars of overlap) is downloaded and merged, so refreshes and restarts
/// are cheap and less likely to hit rate limits.</item>
/// <item>A different source, re-adjusted history (overlap disagrees) or a gap: full download replaces the cache.</item>
/// <item>Network down: the cached bars are returned with <see cref="BarSeries.OfflineCache"/> set.</item>
/// </list>
/// Requests with an explicit end (<see cref="BarRequest.To"/>) bypass the cache. Everything else passes through.
/// </summary>
public sealed class CachedMarketData : IMarketDataService
{
    public const int OverlapBars = 5;
    /// <summary>Tail merges append forever; keep the newest bars only (≈ 5 trading days of 1-minute bars with extended hours).</summary>
    public const int MaxBars = 6000;
    private readonly IMarketDataService _inner;
    private readonly MarketDb _db;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CachedMarketData(IMarketDataService inner, MarketDb db)
    {
        _inner = inner;
        _db = db;
    }

    public MarketDb Db => _db;
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Counters for tests and diagnostics.</summary>
    public int FullFetches { get; private set; }
    public int TailFetches { get; private set; }

    public async Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default)
    {
        if (request.To is not null) return await _inner.GetBarsAsync(request, ct);
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var gate = _locks.GetOrAdd($"{symbol}|{request.Interval}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await GetCoreAsync(request with { Symbol = symbol }, ct); }
        finally { gate.Release(); }
    }

    private async Task<BarSeries> GetCoreAsync(BarRequest request, CancellationToken ct)
    {
        (IReadOnlyList<Bar> Bars, BarCacheMeta? Meta) cached;
        try { cached = _db.LoadBars(request.Symbol, request.Interval); }
        catch (Exception ex)
        {
            AppLog.Warn("cache", $"read failed, bypassing: {ex.Message}");
            return await _inner.GetBarsAsync(request, ct);
        }

        // The cache can serve this request only if it was filled from at least as far back as asked for.
        var usable = cached.Meta is { } meta && cached.Bars.Count > OverlapBars
            && (meta.CoveredFrom is null || (request.From is { } rf && rf >= meta.CoveredFrom));
        if (!usable) return await FullAsync(request, cached, ct);

        BarSeries tail;
        try
        {
            TailFetches++;
            tail = await _inner.GetBarsAsync(request with { From = BarMerge.TailStart(cached.Bars, OverlapBars) }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn("cache", $"{request.Symbol} {request.Interval}: network failed, serving cache ({ex.Message})");
            return Offline(request, cached.Bars, cached.Meta!);
        }

        if (!string.Equals(tail.Source, cached.Meta!.Source, StringComparison.OrdinalIgnoreCase))
            return await FullAsync(request, cached, ct, $"source changed {cached.Meta.Source}→{tail.Source}");
        var (merged, outcome) = BarMerge.Merge(cached.Bars, tail.Bars);
        if (outcome is BarMergeOutcome.Diverged or BarMergeOutcome.Gap)
            return await FullAsync(request, cached, ct, outcome.ToString());

        if (merged.Count > MaxBars) merged = merged.Skip(merged.Count - MaxBars).ToList();
        Save(request, merged, tail, cached.Meta.CoveredFrom);
        return Shape(request, tail, merged);
    }

    private async Task<BarSeries> FullAsync(BarRequest request, (IReadOnlyList<Bar> Bars, BarCacheMeta? Meta) cached, CancellationToken ct, string? why = null)
    {
        if (why is not null) AppLog.Info("cache", $"{request.Symbol} {request.Interval}: full reload ({why})");
        BarSeries full;
        try
        {
            FullFetches++;
            full = await _inner.GetBarsAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch when (cached.Meta is not null && cached.Bars.Count > 0)
        {
            return Offline(request, cached.Bars, cached.Meta);
        }
        Save(request, full.Bars, full, request.From);
        return full;
    }

    private void Save(BarRequest request, IReadOnlyList<Bar> bars, BarSeries from, DateTimeOffset? coveredFrom)
    {
        try
        {
            _db.SaveBars(request.Symbol, request.Interval, bars,
                new BarCacheMeta(from.Source ?? "", from.FetchedAt ?? Now(), from.AsOf, from.DelayMinutes, from.IsRealtime, coveredFrom));
        }
        catch (Exception ex) { AppLog.Warn("cache", $"write failed: {ex.Message}"); }
    }

    private static BarSeries Shape(BarRequest request, BarSeries meta, IReadOnlyList<Bar> bars)
    {
        var list = request.From is { } f ? bars.Where(b => b.Time >= f).ToList() : bars;
        return meta with { Symbol = request.Symbol, Interval = request.Interval, Bars = list };
    }

    private static BarSeries Offline(BarRequest request, IReadOnlyList<Bar> bars, BarCacheMeta meta) =>
        new()
        {
            Symbol = request.Symbol, Interval = request.Interval,
            Bars = request.From is { } f ? bars.Where(b => b.Time >= f).ToList() : bars,
            Source = meta.Source, AsOf = meta.AsOf, FetchedAt = meta.FetchedAt, DelayMinutes = meta.DelayMinutes,
            IsRealtime = false, OfflineCache = true,
        };

    // ---------------- pass-through ----------------
    public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default) => _inner.GetQuotesAsync(symbols, ct);
    public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) => _inner.GetExpirationsAsync(symbol, ct);
    public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default) => _inner.GetChainAsync(symbol, expiration, ct);
    public Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default) => _inner.GetFundamentalsAsync(symbol, ct);
    public Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default) => _inner.GetFinancialsAsync(symbol, ct);
    public Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default) => _inner.GetNewsAsync(symbol, count, ct);
    public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => _inner.SearchAsync(query, limit, ct);
}
