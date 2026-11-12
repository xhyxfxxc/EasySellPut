using Meili.Core.Abstractions;
using Meili.Core.Diagnostics;
using Meili.Core.Models;

namespace Meili.App.Services;

/// <summary>
/// H1/H2: the "best effort" side fetches shared by the options, strategy and news pages (spot quote, dividend yield).
/// Failures fall back to an empty result as before, but are now logged instead of swallowed.
/// </summary>
public static class SafeData
{
    public static async Task<IReadOnlyList<Quote>> QuotesAsync(IMarketDataService data, string symbol, string area, CancellationToken ct)
    {
        try { return await data.GetQuotesAsync([symbol], ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn(area, $"quote for {symbol} unavailable, continuing without it", ex);
            return [];
        }
    }

    /// <summary>Dividend yield as a fraction; 0 (with a log line) when the source has none or fails.</summary>
    public static async Task<double> DividendYieldAsync(IMarketDataService data, string symbol, string area, CancellationToken ct)
    {
        try
        {
            var f = await data.GetFundamentalsAsync(symbol, ct);
            return f.DividendYield is { } y && y >= 0 && y < 1 ? y : 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn(area, $"dividend yield for {symbol} unavailable, assuming 0", ex);
            return 0;
        }
    }
}
