using Meili.Core.Models;

namespace Meili.Core.Abstractions;

/// <summary>What the UI talks to: every call is routed across the free sources with automatic failover.</summary>
public interface IMarketDataService
{
    /// <summary>Quotes for as many symbols as any source can supply (missing ones are filled from the next source).</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default);
    Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default);
    Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default);
    Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default);
    Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default);
    Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
}