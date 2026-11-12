using Meili.Core.Abstractions;
using Meili.Core.Models;

namespace Meili.App.Services;

/// <summary>D3: symbol search shared by every symbol box; remembers recent answers so retyping does not refetch.</summary>
public sealed class SymbolSearchService(IMarketDataService data)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<SymbolMatch> Hits)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hits for <paramref name="query"/>, or null when the search failed (the box then shows watch-list matches only).</summary>
    public async Task<IReadOnlyList<SymbolMatch>?> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        lock (_cache)
            if (_cache.TryGetValue(q, out var c) && DateTimeOffset.UtcNow - c.At < Ttl) return c.Hits;
        try
        {
            var hits = await data.SearchAsync(q, 8, ct);
            lock (_cache)
            {
                if (_cache.Count > 200) _cache.Clear();
                _cache[q] = (DateTimeOffset.UtcNow, hits);
            }
            return hits;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Meili.Core.Diagnostics.AppLog.Warn("search", $"symbol search '{q}' failed: {ex.Message}");
            return null;
        }
    }
}
