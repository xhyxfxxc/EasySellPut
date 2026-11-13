namespace Meili.Analytics;

/// <summary>A listed option used to fill a preset: strike, per-share premium and optional IV (decimal, or NaN if unknown).</summary>
public readonly record struct ListedQuote(OptionRight Right, double Strike, double Premium, double ImpliedVolatility = double.NaN);

/// <summary>
/// Picks strikes from a single-expiry option list and builds a <see cref="StrategyPresets"/> strategy around the spot.
/// Wing width is counted in listed strike steps (default 2). Returns <see langword="null"/> when the chain is too thin
/// or the spot is not usable.
/// </summary>
public static class StrategyFromQuotes
{
    /// <summary>Default distance, in listed strikes, between the body and each wing.</summary>
    public const int DefaultWingSteps = 2;

    /// <summary>
    /// Builds <paramref name="kind"/> at <paramref name="expiry"/> using mid-style <paramref name="quotes"/>.
    /// <paramref name="spot"/> is also the stock entry for covered-call, protective-put and collar presets.
    /// Calendar and diagonal kinds need <paramref name="farExpiry"/> and <paramref name="farQuotes"/> (a later chain).
    /// </summary>
    public static OptionStrategy? TryBuild(
        StrategyKind kind,
        double spot,
        DateOnly expiry,
        IReadOnlyList<ListedQuote> quotes,
        int quantity = 1,
        int wingSteps = DefaultWingSteps,
        DateOnly? farExpiry = null,
        IReadOnlyList<ListedQuote>? farQuotes = null)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        if (wingSteps <= 0) throw new ArgumentOutOfRangeException(nameof(wingSteps), wingSteps, "Wing steps must be positive.");
        if (!(spot > 0) || !double.IsFinite(spot)) return null;
        if (kind == StrategyKind.Custom) return null;

        var calls = Unique(quotes, OptionRight.Call);
        var puts = Unique(quotes, OptionRight.Put);

        return kind switch
        {
            StrategyKind.LongCall => One(calls, Atm(calls, spot), q => StrategyPresets.LongCall(Q(q), expiry, quantity)),
            StrategyKind.LongPut => One(puts, Atm(puts, spot), q => StrategyPresets.LongPut(Q(q), expiry, quantity)),
            StrategyKind.CoveredCall => One(calls, FirstAbove(calls, spot) ?? FirstAtOrAbove(calls, spot) ?? Atm(calls, spot), q => StrategyPresets.CoveredCall(spot, Q(q), expiry, quantity)),
            StrategyKind.ProtectivePut => One(puts, LastBelow(puts, spot) ?? LastAtOrBelow(puts, spot) ?? Atm(puts, spot), q => StrategyPresets.ProtectivePut(spot, Q(q), expiry, quantity)),
            StrategyKind.BullCallSpread => Spread(calls, Atm(calls, spot), +wingSteps, (lo, hi) => StrategyPresets.BullCallSpread(Q(lo), Q(hi), expiry, quantity)),
            StrategyKind.BearCallSpread => Spread(calls, Atm(calls, spot), +wingSteps, (lo, hi) => StrategyPresets.BearCallSpread(Q(lo), Q(hi), expiry, quantity)),
            StrategyKind.BullPutSpread => Spread(puts, Atm(puts, spot), -wingSteps, (lo, hi) => StrategyPresets.BullPutSpread(Q(lo), Q(hi), expiry, quantity)),
            StrategyKind.BearPutSpread => Spread(puts, Atm(puts, spot), -wingSteps, (lo, hi) => StrategyPresets.BearPutSpread(Q(lo), Q(hi), expiry, quantity)),
            StrategyKind.LongStraddle => Straddle(calls, puts, spot, expiry, PositionSide.Long, quantity),
            StrategyKind.ShortStraddle => Straddle(calls, puts, spot, expiry, PositionSide.Short, quantity),
            StrategyKind.LongStrangle => Strangle(calls, puts, spot, wingSteps, expiry, PositionSide.Long, quantity),
            StrategyKind.ShortStrangle => Strangle(calls, puts, spot, wingSteps, expiry, PositionSide.Short, quantity),
            StrategyKind.IronCondor => Iron(calls, puts, spot, wingSteps, expiry, quantity),
            StrategyKind.LongCallButterfly => Fly(calls, OptionRight.Call, spot, wingSteps, expiry, quantity),
            StrategyKind.LongPutButterfly => Fly(puts, OptionRight.Put, spot, wingSteps, expiry, quantity),
            StrategyKind.Collar => Collar(calls, puts, spot, expiry, quantity),
            StrategyKind.IronButterfly => IronFly(calls, puts, spot, wingSteps, expiry, quantity),
            StrategyKind.CalendarCall => Calendar(OptionRight.Call, calls, Unique(farQuotes ?? [], OptionRight.Call), spot, expiry, farExpiry, quantity),
            StrategyKind.CalendarPut => Calendar(OptionRight.Put, puts, Unique(farQuotes ?? [], OptionRight.Put), spot, expiry, farExpiry, quantity),
            StrategyKind.DiagonalCall => Diagonal(calls, Unique(farQuotes ?? [], OptionRight.Call), spot, wingSteps, expiry, farExpiry, quantity),
            _ => null,
        };
    }

    private static List<ListedQuote> Unique(IReadOnlyList<ListedQuote> quotes, OptionRight right)
        => quotes
            .Where(q => q.Right == right && double.IsFinite(q.Strike) && q.Strike > 0 && double.IsFinite(q.Premium) && q.Premium >= 0)
            .GroupBy(q => q.Strike)
            .Select(g => g.First())
            .OrderBy(q => q.Strike)
            .ToList();

    private static LegQuote Q(ListedQuote q) => new(q.Strike, q.Premium, q.ImpliedVolatility);

    private static int? Atm(IReadOnlyList<ListedQuote> list, double spot)
    {
        if (list.Count == 0) return null;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            double d = Math.Abs(list[i].Strike - spot);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }


    private static int? FirstAbove(IReadOnlyList<ListedQuote> list, double spot)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Strike > spot) return i;
        return null;
    }

    private static int? LastBelow(IReadOnlyList<ListedQuote> list, double spot)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Strike < spot) return i;
        return null;
    }
    private static int? FirstAtOrAbove(IReadOnlyList<ListedQuote> list, double spot)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Strike >= spot) return i;
        return null;
    }

    private static int? LastAtOrBelow(IReadOnlyList<ListedQuote> list, double spot)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Strike <= spot) return i;
        return null;
    }

    private static OptionStrategy? One(IReadOnlyList<ListedQuote> list, int? index, Func<ListedQuote, OptionStrategy> build)
        => index is { } i && i >= 0 && i < list.Count ? build(list[i]) : null;

    /// <summary>A two-leg vertical: the ATM (or nearest) contract plus a wing <paramref name="delta"/> strikes away.</summary>
    private static OptionStrategy? Spread(
        IReadOnlyList<ListedQuote> list, int? atm, int delta, Func<ListedQuote, ListedQuote, OptionStrategy> build)
    {
        if (atm is not { } i) return null;
        int j = i + delta;
        if (j < 0 || j >= list.Count || j == i) return null;
        var a = list[Math.Min(i, j)];
        var b = list[Math.Max(i, j)];
        if (!(b.Strike > a.Strike)) return null;
        return build(a, b);
    }

    private static OptionStrategy? Straddle(
        IReadOnlyList<ListedQuote> calls, IReadOnlyList<ListedQuote> puts, double spot, DateOnly expiry, PositionSide side, int quantity)
    {
        if (Atm(calls, spot) is not { } ci) return null;
        var call = calls[ci];
        var put = puts.FirstOrDefault(p => p.Strike == call.Strike);
        if (put.Strike != call.Strike)
        {
            if (Atm(puts, spot) is not { } pi) return null;
            put = puts[pi];
            // Prefer a matching strike on the other side when the two ATMs disagree.
            var match = calls.FirstOrDefault(c => c.Strike == put.Strike);
            if (match.Strike == put.Strike) call = match;
            if (call.Strike != put.Strike) return null;
        }
        return StrategyPresets.Straddle(Q(call), Q(put), expiry, side, quantity);
    }

    private static OptionStrategy? Strangle(
        IReadOnlyList<ListedQuote> calls, IReadOnlyList<ListedQuote> puts, double spot, int wing, DateOnly expiry, PositionSide side, int quantity)
    {
        if (LastAtOrBelow(puts, spot) is not { } pi) return null;
        if (FirstAtOrAbove(calls, spot) is not { } ci) return null;
        int puti = pi - wing;
        int calli = ci + wing;
        // If ATM put/call sit on the same strike, step at least one strike off it so it stays a strangle.
        if (puti < 0 || calli >= calls.Count) return null;
        if (puts[puti].Strike >= calls[calli].Strike)
        {
            puti = pi - Math.Max(wing, 1);
            calli = ci + Math.Max(wing, 1);
            if (puti < 0 || calli >= calls.Count || puts[puti].Strike >= calls[calli].Strike) return null;
        }
        return StrategyPresets.Strangle(Q(puts[puti]), Q(calls[calli]), expiry, side, quantity);
    }

    private static OptionStrategy? Iron(
        IReadOnlyList<ListedQuote> calls, IReadOnlyList<ListedQuote> puts, double spot, int wing, DateOnly expiry, int quantity)
    {
        if (LastAtOrBelow(puts, spot) is not { } pi) return null;
        if (FirstAtOrAbove(calls, spot) is not { } ci) return null;
        int shortPut = pi;
        int shortCall = ci;
        if (puts[shortPut].Strike >= calls[shortCall].Strike)
        {
            shortPut = pi - 1;
            shortCall = ci + 1;
        }
        int longPut = shortPut - wing;
        int longCall = shortCall + wing;
        if (longPut < 0 || longCall >= calls.Count || shortPut < 0 || shortCall >= calls.Count) return null;
        var lp = puts[longPut]; var sp = puts[shortPut]; var sc = calls[shortCall]; var lc = calls[longCall];
        if (!(lp.Strike < sp.Strike && sp.Strike < sc.Strike && sc.Strike < lc.Strike)) return null;
        return StrategyPresets.IronCondor(Q(lp), Q(sp), Q(sc), Q(lc), expiry, quantity);
    }

    private static OptionStrategy? Fly(
        IReadOnlyList<ListedQuote> list, OptionRight right, double spot, int wing, DateOnly expiry, int quantity)
    {
        if (Atm(list, spot) is not { } mid) return null;
        int lo = mid - wing, hi = mid + wing;
        if (lo < 0 || hi >= list.Count) return null;
        if (!(list[lo].Strike < list[mid].Strike && list[mid].Strike < list[hi].Strike)) return null;
        return StrategyPresets.Butterfly(right, Q(list[lo]), Q(list[mid]), Q(list[hi]), expiry, quantity);
    }

    private static OptionStrategy? Collar(
        IReadOnlyList<ListedQuote> calls, IReadOnlyList<ListedQuote> puts, double spot, DateOnly expiry, int quantity)
    {
        int? calli = FirstAbove(calls, spot) ?? FirstAtOrAbove(calls, spot);
        int? puti = LastBelow(puts, spot) ?? LastAtOrBelow(puts, spot);
        if (calli is not { } ci || puti is not { } pi) return null;
        if (!(puts[pi].Strike < calls[ci].Strike)) return null;
        return StrategyPresets.Collar(spot, Q(calls[ci]), Q(puts[pi]), expiry, quantity);
    }

    private static OptionStrategy? IronFly(
        IReadOnlyList<ListedQuote> calls, IReadOnlyList<ListedQuote> puts, double spot, int wing, DateOnly expiry, int quantity)
    {
        if (Atm(calls, spot) is not { } ci) return null;
        var bodyCall = calls[ci];
        var bodyPut = puts.FirstOrDefault(p => p.Strike == bodyCall.Strike);
        if (bodyPut.Strike != bodyCall.Strike)
        {
            if (Atm(puts, spot) is not { } pi) return null;
            bodyPut = puts[pi];
            var match = calls.FirstOrDefault(c => c.Strike == bodyPut.Strike);
            if (match.Strike == bodyPut.Strike) bodyCall = match;
            if (bodyCall.Strike != bodyPut.Strike) return null;
        }
        int callBody = IndexOfStrike(calls, bodyCall.Strike);
        int putBody = IndexOfStrike(puts, bodyPut.Strike);
        int longPut = putBody - wing, longCall = callBody + wing;
        if (longPut < 0 || longCall >= calls.Count) return null;
        if (!(puts[longPut].Strike < bodyPut.Strike && bodyCall.Strike < calls[longCall].Strike)) return null;
        return StrategyPresets.IronButterfly(Q(puts[longPut]), Q(bodyPut), Q(bodyCall), Q(calls[longCall]), expiry, quantity);
    }

    private static OptionStrategy? Calendar(
        OptionRight right, IReadOnlyList<ListedQuote> near, IReadOnlyList<ListedQuote> far, double spot, DateOnly nearExp, DateOnly? farExp, int quantity)
    {
        if (farExp is not { } farDate || farDate <= nearExp || far.Count == 0) return null;
        if (Atm(near, spot) is not { } ni) return null;
        var n = near[ni];
        var f = far.FirstOrDefault(q => q.Strike == n.Strike);
        if (f.Strike != n.Strike)
        {
            if (Atm(far, spot) is not { } fi) return null;
            f = far[fi];
            var match = near.FirstOrDefault(q => q.Strike == f.Strike);
            if (match.Strike == f.Strike) n = match;
            if (n.Strike != f.Strike) return null;
        }
        return right == OptionRight.Call
            ? StrategyPresets.CalendarCall(Q(n), nearExp, Q(f), farDate, quantity)
            : StrategyPresets.CalendarPut(Q(n), nearExp, Q(f), farDate, quantity);
    }

    private static OptionStrategy? Diagonal(
        IReadOnlyList<ListedQuote> near, IReadOnlyList<ListedQuote> far, double spot, int wing, DateOnly nearExp, DateOnly? farExp, int quantity)
    {
        if (farExp is not { } farDate || farDate <= nearExp || far.Count == 0) return null;
        // Short the nearer ATM (or first above) call; long a farther call one wing higher (poor-man's covered-call shape).
        int ni = FirstAbove(near, spot) ?? Atm(near, spot) ?? -1;
        if (ni < 0) return null;
        var n = near[ni];
        int fi = (Atm(far, spot) ?? 0) + wing;
        if (fi >= far.Count) fi = far.Count - 1;
        var f = far[fi];
        if (f.Strike == n.Strike)
        {
            if (fi + 1 < far.Count) f = far[fi + 1];
            else if (fi - 1 >= 0) f = far[fi - 1];
        }
        if (f.Strike == n.Strike) return null;
        return StrategyPresets.DiagonalCall(Q(n), nearExp, Q(f), farDate, quantity);
    }

    private static int IndexOfStrike(IReadOnlyList<ListedQuote> list, double strike)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Strike == strike) return i;
        return -1;
    }
}