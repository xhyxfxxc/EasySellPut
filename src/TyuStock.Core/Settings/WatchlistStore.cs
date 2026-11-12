using System.Text.RegularExpressions;

namespace TyuStock.Core.Settings;

public enum WatchlistError
{
    None = 0,
    InvalidSymbol,
    DuplicateInGroup,
    AlreadyOnWatchlist,
    GroupNotFound,
    LastGroup,
    EmptyName,
}

/// <summary>Mutates the grouped watchlist in <see cref="SettingsStore"/> and persists each successful change.</summary>
public sealed class WatchlistStore
{
    /// <summary>US-listed tickers, share classes (BRK.B), a few Yahoo prefixes (^GSPC).</summary>
    private static readonly Regex SymbolRx = new(@"^[A-Z^][A-Z0-9.\-]{0,15}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SettingsStore _settings;

    public WatchlistStore(SettingsStore settings)
    {
        _settings = settings;
        if (_settings.Current.WatchlistGroups.Count == 0)
            _settings.Current.Normalize();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<WatchlistGroup> Groups => _settings.Current.WatchlistGroups;
    public IReadOnlyList<string> AllSymbols => _settings.Current.Watchlist;

    public WatchlistGroup? FindGroup(string id) =>
        Groups.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public (WatchlistGroup Group, int Index)? FindSymbol(string symbol)
    {
        var needle = symbol.Trim().ToUpperInvariant();
        foreach (var g in Groups)
        {
            var i = g.Symbols.FindIndex(s => s.Equals(needle, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return (g, i);
        }
        return null;
    }

    public static bool TryNormalizeSymbol(string? raw, out string symbol, out WatchlistError error)
    {
        symbol = (raw ?? "").Trim().ToUpperInvariant();
        if (symbol.Length is 0 or > 16 || !SymbolRx.IsMatch(symbol))
        {
            symbol = "";
            error = WatchlistError.InvalidSymbol;
            return false;
        }
        error = WatchlistError.None;
        return true;
    }

    public WatchlistError AddSymbol(string groupId, string raw, out string symbol)
    {
        symbol = "";
        if (!TryNormalizeSymbol(raw, out symbol, out var err)) return err;
        var normalized = symbol;
        var g = FindGroup(groupId);
        if (g is null) return WatchlistError.GroupNotFound;
        if (g.Symbols.Any(s => s.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return WatchlistError.DuplicateInGroup;
        if (FindSymbol(normalized) is not null) return WatchlistError.AlreadyOnWatchlist;
        g.Symbols.Add(normalized);
        Persist();
        return WatchlistError.None;
    }

    public bool RemoveSymbol(string symbol)
    {
        if (FindSymbol(symbol) is not { } hit) return false;
        hit.Group.Symbols.RemoveAt(hit.Index);
        Persist();
        return true;
    }

    /// <summary>Move a symbol up (<paramref name="delta"/> = -1) or down (+1) inside its group.</summary>
    public bool MoveSymbol(string symbol, int delta)
    {
        if (delta == 0 || FindSymbol(symbol) is not { } hit) return false;
        var to = hit.Index + delta;
        if (to < 0 || to >= hit.Group.Symbols.Count) return false;
        var item = hit.Group.Symbols[hit.Index];
        hit.Group.Symbols.RemoveAt(hit.Index);
        hit.Group.Symbols.Insert(to, item);
        Persist();
        return true;
    }

    /// <summary>Reorder within a group by index (used by drag-and-drop).</summary>
    public bool Reorder(string groupId, int from, int to)
    {
        var g = FindGroup(groupId);
        if (g is null || from == to) return false;
        if (from < 0 || to < 0 || from >= g.Symbols.Count || to >= g.Symbols.Count) return false;
        var item = g.Symbols[from];
        g.Symbols.RemoveAt(from);
        g.Symbols.Insert(to, item);
        Persist();
        return true;
    }

    public WatchlistError AddGroup(string name, out WatchlistGroup? group)
    {
        group = null;
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return WatchlistError.EmptyName;
        var id = NewGroupId();
        group = new WatchlistGroup { Id = id, Name = trimmed, Symbols = [] };
        _settings.Current.WatchlistGroups.Add(group);
        Persist();
        return WatchlistError.None;
    }

    public WatchlistError RenameGroup(string id, string name)
    {
        var g = FindGroup(id);
        if (g is null) return WatchlistError.GroupNotFound;
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 && !id.Equals(WatchlistGroup.DefaultId, StringComparison.OrdinalIgnoreCase))
            return WatchlistError.EmptyName;
        g.Name = trimmed;
        Persist();
        return WatchlistError.None;
    }

    /// <summary>Removes a group and appends its symbols onto the first remaining group (skipping those already present).</summary>
    public WatchlistError RemoveGroup(string id)
    {
        var g = FindGroup(id);
        if (g is null) return WatchlistError.GroupNotFound;
        if (Groups.Count <= 1) return WatchlistError.LastGroup;
        var target = Groups.First(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        foreach (var s in g.Symbols)
            if (!target.Symbols.Contains(s, StringComparer.OrdinalIgnoreCase))
                target.Symbols.Add(s);
        _settings.Current.WatchlistGroups.Remove(g);
        Persist();
        return WatchlistError.None;
    }

    private string NewGroupId()
    {
        for (var i = 0; i < 8; i++)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (FindGroup(id) is null) return id;
        }
        return Guid.NewGuid().ToString("N");
    }

    private void Persist()
    {
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}