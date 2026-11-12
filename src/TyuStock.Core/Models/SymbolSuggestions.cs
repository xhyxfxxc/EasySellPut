namespace TyuStock.Core.Models;

/// <summary>One line in a symbol drop-down.</summary>
public sealed record SymbolSuggestion(string Symbol, string? Name, string? Exchange, bool FromWatchlist);

/// <summary>
/// D3: merges watch-list symbols and search hits for the shared symbol box. Watch-list entries come first
/// (prefix matches before contains matches), then search hits; duplicates are dropped case-insensitively.
/// </summary>
public static class SymbolSuggestions
{
    public const int DefaultMax = 12;

    public static IReadOnlyList<SymbolSuggestion> Build(string? query, IEnumerable<string> watch, IEnumerable<SymbolMatch>? hits, int max = DefaultMax)
    {
        var q = (query ?? "").Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<SymbolSuggestion>();
        var w = watch.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        IEnumerable<string> watchMatches = q.Length == 0
            ? w
            : w.Where(s => s.StartsWith(q, StringComparison.OrdinalIgnoreCase))
               .Concat(w.Where(s => !s.StartsWith(q, StringComparison.OrdinalIgnoreCase) && s.Contains(q, StringComparison.OrdinalIgnoreCase)));
        var names = (hits ?? []).GroupBy(h => h.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var s in watchMatches)
        {
            if (list.Count >= max) break;
            if (!seen.Add(s)) continue;
            names.TryGetValue(s, out var h);
            list.Add(new SymbolSuggestion(s, h?.Name, h?.Exchange, true));
        }
        if (q.Length == 0) return list;
        foreach (var h in hits ?? [])
        {
            if (list.Count >= max) break;
            if (string.IsNullOrWhiteSpace(h.Symbol) || !seen.Add(h.Symbol)) continue;
            list.Add(new SymbolSuggestion(h.Symbol, h.Name, h.Exchange, false));
        }
        return list;
    }
}
