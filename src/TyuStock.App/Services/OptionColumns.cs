namespace TyuStock.App.Services;

/// <summary>How a T-table cell is drawn.</summary>
public enum OptionCellKind
{
    Text,
    /// <summary>G4: grey when the bid/ask spread is wide.</summary>
    Spread,
    /// <summary>G4: open interest with a data bar growing from the strike side.</summary>
    OiBar,
}

/// <summary>Which optional columns are on (mirrors the page's column check boxes).</summary>
public sealed record OptionColumnSet(bool Volume = true, bool Oi = true, bool Iv = true, bool Delta = true, bool Gamma = true,
    bool Theta = true, bool Vega = true, bool Spread = true, bool Distance = true, bool Compact = false);

/// <param name="Path">Binding path relative to the side (e.g. "Bid" → "Call.Bid"), or to the row for the strike block.</param>
/// <param name="Width">Normal width (px); <paramref name="CompactWidth"/> applies in the compact preset (E6).</param>
public sealed record OptionColumnDef(string Key, string HeaderKey, string Path, double Width, double CompactWidth,
    OptionCellKind Kind = OptionCellKind.Text);

/// <summary>
/// H1/G4: the T-table's columns as data (header, binding, width, cell kind) instead of hand-written view code.
/// Calls run outside → in (… bid ask last | strike), puts mirror them (strike | last bid ask …).
/// </summary>
public static class OptionColumns
{
    private static readonly OptionColumnDef Volume = new("Volume", "Op_Volume", "Volume", 72, 60);
    private static readonly OptionColumnDef Oi = new("Oi", "Op_OI", "Oi", 80, 64, OptionCellKind.OiBar);
    private static readonly OptionColumnDef Iv = new("Iv", "Op_IV", "Iv", 64, 56);
    private static readonly OptionColumnDef Delta = new("Delta", "Op_Delta", "Delta", 64, 54);
    private static readonly OptionColumnDef Gamma = new("Gamma", "Op_Gamma", "Gamma", 64, 58);
    private static readonly OptionColumnDef Theta = new("Theta", "Op_Theta", "Theta", 64, 52);
    private static readonly OptionColumnDef Vega = new("Vega", "Op_Vega", "Vega", 58, 50);
    private static readonly OptionColumnDef SpreadPct = new("SpreadPct", "Op_SpreadPct", "SpreadPct", 64, 54, OptionCellKind.Spread);
    private static readonly OptionColumnDef Bid = new("Bid", "Op_Bid", "Bid", 58, 54, OptionCellKind.Spread);
    private static readonly OptionColumnDef Ask = new("Ask", "Op_Ask", "Ask", 58, 54, OptionCellKind.Spread);
    private static readonly OptionColumnDef Last = new("Last", "Op_Last", "Last", 58, 54);

    public static readonly OptionColumnDef Strike = new("Strike", "Op_Strike", "StrikeText", 80, 64);
    public static readonly OptionColumnDef Distance = new("Distance", "Op_Distance", "DistanceText", 72, 58);

    /// <summary>One side's columns, outermost first for calls and innermost (next to the strike) first for puts.</summary>
    public static IReadOnlyList<OptionColumnDef> Side(bool call, OptionColumnSet set)
    {
        var outerToInner = new List<OptionColumnDef>();
        if (set.Volume) outerToInner.Add(Volume);
        if (set.Oi) outerToInner.Add(Oi);
        if (set.Iv) outerToInner.Add(Iv);
        if (set.Delta) outerToInner.Add(Delta);
        if (set.Gamma) outerToInner.Add(Gamma);
        if (set.Theta) outerToInner.Add(Theta);
        if (set.Vega) outerToInner.Add(Vega);
        if (set.Spread) outerToInner.Add(SpreadPct);
        outerToInner.Add(Bid);
        outerToInner.Add(Ask);
        outerToInner.Add(Last);
        if (!call) outerToInner.Reverse();
        return outerToInner;
    }

    /// <summary>Middle block: strike, then distance to spot when on.</summary>
    public static IReadOnlyList<OptionColumnDef> Middle(OptionColumnSet set) => set.Distance ? [Strike, Distance] : [Strike];

    public static double WidthOf(OptionColumnDef c, OptionColumnSet set) => set.Compact ? c.CompactWidth : c.Width;

    /// <summary>Total table width for a column set (used by the 150% scaling check, E6).</summary>
    public static double TotalWidth(OptionColumnSet set) =>
        Side(true, set).Concat(Middle(set)).Concat(Side(false, set)).Sum(c => WidthOf(c, set));
}
