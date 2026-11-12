using Meili.Core.Models;

namespace Meili.Core.Abstractions;

/// <summary>A free market-data source. Concrete providers implement one or more capability interfaces below.</summary>
public interface IDataProvider
{
    /// <summary>Stable lowercase id, e.g. "yahoo".</summary>
    string Id { get; }
    string DisplayName { get; }
    DataCapability Capabilities { get; }
}

public interface IQuoteProvider : IDataProvider
{
    /// <summary>Returns quotes for as many of <paramref name="symbols"/> as the source knows; unknown symbols are omitted.</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default);
}

public interface IBarsProvider : IDataProvider
{
    bool Supports(BarInterval interval);
    Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default);
}

public interface IOptionsProvider : IDataProvider
{
    Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default);
    /// <summary>Chain for one expiration (nearest when null).</summary>
    Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default);
}

public interface IFundamentalsProvider : IDataProvider
{
    Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default);
}

public interface IFinancialsProvider : IDataProvider
{
    Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default);
}

public interface INewsProvider : IDataProvider
{
    Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default);
}

public interface ISymbolSearchProvider : IDataProvider
{
    Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
}
