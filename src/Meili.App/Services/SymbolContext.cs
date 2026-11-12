using Meili.Core.Settings;

namespace Meili.App.Services;

/// <summary>
/// D1: the one "current symbol" shared by K-line, options, strategy and news pages. Loading a symbol on any of them
/// sets it; the others adopt it the next time they are shown (or immediately if visible). No WPF dependency, so it is
/// unit-tested directly.
/// </summary>
public sealed class SymbolContext
{
    public string? Current { get; private set; }

    /// <summary>Raised with the normalized symbol whenever <see cref="Current"/> changes.</summary>
    public event EventHandler<string>? Changed;

    /// <returns>True when the context changed.</returns>
    public bool Set(string? symbol)
    {
        if (!WatchlistStore.TryNormalizeSymbol(symbol, out var s, out _)) return false;
        if (string.Equals(Current, s, StringComparison.OrdinalIgnoreCase)) return false;
        Current = s;
        Changed?.Invoke(this, s);
        return true;
    }

    /// <summary>True when the page showing <paramref name="pageSymbol"/> should switch to the shared symbol.</summary>
    public bool ShouldAdopt(string? pageSymbol) =>
        Current is not null && !string.Equals(Current, pageSymbol?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>"Build a strategy from this contract" request carried from the option chain to the strategy page.</summary>
public sealed record StrategySeed(string Symbol, DateOnly Expiration, Meili.Core.Models.OptionRight Right,
    Meili.Analytics.PositionSide Side, double Strike);
