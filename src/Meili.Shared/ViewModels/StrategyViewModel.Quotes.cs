using Meili.Analytics;
using Meili.Core.Diagnostics;
using Meili.Core.Models;
using Meili.Core.Settings;
using Meili.Data.Routing;
using ARight = Meili.Analytics.OptionRight;

namespace Meili.Shared.ViewModels;

public partial class StrategyViewModel
{
    private readonly Dictionary<DateOnly, IReadOnlyList<ListedQuote>> _quotesByExpiry = [];

    private void RememberQuotes(DateOnly? expiry, IReadOnlyList<ListedQuote> list)
    {
        _listed = list;
        if (expiry is { } d) _quotesByExpiry[d] = list;
    }

    private void ClearQuoteCache()
    {
        _quotesByExpiry.Clear();
        _listed = [];
    }

    private IReadOnlyList<ListedQuote> QuotesFor(DateOnly expiry)
        => _quotesByExpiry.TryGetValue(expiry, out var list) ? list : _listed;

    private async Task EnsureQuotesAsync(DateOnly expiry, CancellationToken ct)
    {
        if (_quotesByExpiry.ContainsKey(expiry)) return;
        if (!WatchlistStore.TryNormalizeSymbol(SymbolText, out var symbol, out _)) return;
        OptionChain? chain = null;
        try { chain = await _data.GetChainAsync(symbol, expiry, ct); }
        catch (AllSourcesFailedException) { return; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLog.Warn("strategy", "far-expiry chain failed", ex); return; }
        if (chain is null || !symbol.Equals(SymbolText, StringComparison.OrdinalIgnoreCase)) return;
        var settings = _inputs.Enrichment(DateTimeOffset.Now);
        var spot = Spot ?? 0;
        var list = new List<ListedQuote>();
        foreach (var c in chain.Contracts)
        {
            if (c.Expiration != expiry) continue;
            list.Add(ToListed(c, spot, settings));
        }
        if (list.Count > 0) _quotesByExpiry[expiry] = list;
    }

    internal async Task FillLegQuotesAsync()
    {
        var needed = new List<DateOnly>();
        foreach (var row in Legs)
        {
            if (row.Type == LegType.Stock) continue;
            if (!DateOnly.TryParse(row.ExpiryText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exp)
                && !DateOnly.TryParse(row.ExpiryText, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out exp))
                continue;
            if (!_quotesByExpiry.ContainsKey(exp) && (SelectedExpiration is null || exp != SelectedExpiration.Date || _listed.Count == 0))
                needed.Add(exp);
        }
        if (needed.Count > 0)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            foreach (var exp in needed.Distinct())
                await EnsureQuotesAsync(exp, cts.Token);
        }
        RefreshLegQuotes();
    }

    private ListedQuote FindQuote(LegType type, double strike, DateOnly? expiry)
    {
        var right = type == LegType.Call ? ARight.Call : ARight.Put;
        IReadOnlyList<ListedQuote> list = expiry is { } d ? QuotesFor(d) : _listed;
        return list.FirstOrDefault(x => x.Right == right && Math.Abs(x.Strike - strike) < 1e-8);
    }
}
