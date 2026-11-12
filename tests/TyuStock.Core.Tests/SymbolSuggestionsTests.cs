using TyuStock.Core.Models;

namespace TyuStock.Core.Tests;

/// <summary>D3: merge of watch-list symbols and search hits for the shared symbol box.</summary>
public class SymbolSuggestionsTests
{
    private static SymbolMatch H(string s, string n = "") => new(s, n == "" ? s + " Inc." : n, "NMS", "EQUITY", "yahoo");

    [Fact]
    public void Watchlist_prefix_matches_come_first_then_contains_then_search_hits()
    {
        var r = SymbolSuggestions.Build("ms", ["AAPL", "AMSC", "MSFT", "MSTR"], [H("MSFT", "Microsoft"), H("MS", "Morgan Stanley")]);
        Assert.Equal(["MSFT", "MSTR", "AMSC", "MS"], r.Select(x => x.Symbol));
        Assert.True(r[0].FromWatchlist);
        Assert.Equal("Microsoft", r[0].Name);              // name filled in from the search hit
        Assert.False(r[3].FromWatchlist);
    }

    [Fact]
    public void Empty_query_lists_the_whole_watchlist_without_hits()
    {
        var r = SymbolSuggestions.Build("  ", ["AAPL", "aapl", "TSLA", ""], [H("X")]);
        Assert.Equal(["AAPL", "TSLA"], r.Select(x => x.Symbol));
    }

    [Fact]
    public void Duplicates_are_dropped_and_the_list_is_capped()
    {
        var hits = Enumerable.Range(0, 30).Select(i => H("Q" + i)).Prepend(H("q0")).ToList();
        var r = SymbolSuggestions.Build("q", [], hits, max: 5);
        Assert.Equal(5, r.Count);
        Assert.Single(r, x => x.Symbol.Equals("Q0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Failed_search_still_returns_watchlist_matches()
    {
        var r = SymbolSuggestions.Build("TS", ["TSLA", "AAPL"], null);
        Assert.Equal(["TSLA"], r.Select(x => x.Symbol));
    }
}
