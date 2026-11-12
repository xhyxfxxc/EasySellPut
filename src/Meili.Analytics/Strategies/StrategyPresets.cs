namespace Meili.Analytics;

/// <summary>Strike, per-share premium and (optional) implied volatility of one option from the chain.</summary>
public readonly record struct LegQuote(double Strike, double Premium, double ImpliedVolatility = double.NaN);

/// <summary>
/// Builders for the common strategies. Quantities are in contracts (each leg; butterflies sell 2x the body); stock legs in
/// covered call / protective put are <c>contracts x 100</c> shares. Strikes must be given in the documented order.
/// </summary>
public static class StrategyPresets
{
    private const PositionSide L = PositionSide.Long, S = PositionSide.Short;

    public static OptionStrategy LongCall(LegQuote call, DateOnly expiry, int quantity = 1)
        => Build(StrategyKind.LongCall, Call(L, call, expiry, quantity));

    public static OptionStrategy LongPut(LegQuote put, DateOnly expiry, int quantity = 1)
        => Build(StrategyKind.LongPut, Put(L, put, expiry, quantity));

    /// <summary>Long <c>contracts x 100</c> shares at <paramref name="stockPrice"/> + short <paramref name="contracts"/> calls.</summary>
    public static OptionStrategy CoveredCall(double stockPrice, LegQuote call, DateOnly expiry, int contracts = 1)
        => Build(StrategyKind.CoveredCall, Shares(stockPrice, contracts), Call(S, call, expiry, contracts));

    /// <summary>Long <c>contracts x 100</c> shares at <paramref name="stockPrice"/> + long <paramref name="contracts"/> puts.</summary>
    public static OptionStrategy ProtectivePut(double stockPrice, LegQuote put, DateOnly expiry, int contracts = 1)
        => Build(StrategyKind.ProtectivePut, Shares(stockPrice, contracts), Put(L, put, expiry, contracts));

    /// <summary>Debit call spread: long the lower strike, short the upper strike.</summary>
    public static OptionStrategy BullCallSpread(LegQuote lowerCall, LegQuote upperCall, DateOnly expiry, int quantity = 1)
    {
        Ascending(lowerCall, upperCall);
        return Build(StrategyKind.BullCallSpread, Call(L, lowerCall, expiry, quantity), Call(S, upperCall, expiry, quantity));
    }

    /// <summary>Credit call spread: short the lower strike, long the upper strike.</summary>
    public static OptionStrategy BearCallSpread(LegQuote lowerCall, LegQuote upperCall, DateOnly expiry, int quantity = 1)
    {
        Ascending(lowerCall, upperCall);
        return Build(StrategyKind.BearCallSpread, Call(S, lowerCall, expiry, quantity), Call(L, upperCall, expiry, quantity));
    }

    /// <summary>Credit put spread: long the lower strike, short the upper strike.</summary>
    public static OptionStrategy BullPutSpread(LegQuote lowerPut, LegQuote upperPut, DateOnly expiry, int quantity = 1)
    {
        Ascending(lowerPut, upperPut);
        return Build(StrategyKind.BullPutSpread, Put(L, lowerPut, expiry, quantity), Put(S, upperPut, expiry, quantity));
    }

    /// <summary>Debit put spread: short the lower strike, long the upper strike.</summary>
    public static OptionStrategy BearPutSpread(LegQuote lowerPut, LegQuote upperPut, DateOnly expiry, int quantity = 1)
    {
        Ascending(lowerPut, upperPut);
        return Build(StrategyKind.BearPutSpread, Put(S, lowerPut, expiry, quantity), Put(L, upperPut, expiry, quantity));
    }

    /// <summary>Call + put at the same strike; long (default) or short.</summary>
    public static OptionStrategy Straddle(LegQuote call, LegQuote put, DateOnly expiry, PositionSide side = PositionSide.Long, int quantity = 1)
    {
        if (call.Strike != put.Strike) throw new ArgumentException("A straddle needs the call and put at the same strike.", nameof(put));
        return Build(side == L ? StrategyKind.LongStraddle : StrategyKind.ShortStraddle,
            Call(side, call, expiry, quantity), Put(side, put, expiry, quantity));
    }

    /// <summary>OTM put (lower strike) + OTM call (higher strike); long (default) or short.</summary>
    public static OptionStrategy Strangle(LegQuote put, LegQuote call, DateOnly expiry, PositionSide side = PositionSide.Long, int quantity = 1)
    {
        Ascending(put, call);
        return Build(side == L ? StrategyKind.LongStrangle : StrategyKind.ShortStrangle,
            Put(side, put, expiry, quantity), Call(side, call, expiry, quantity));
    }

    /// <summary>
    /// Short iron condor (net credit): long put (lowest), short put, short call, long call (highest). Strikes strictly ascending.
    /// </summary>
    public static OptionStrategy IronCondor(LegQuote longPut, LegQuote shortPut, LegQuote shortCall, LegQuote longCall, DateOnly expiry, int quantity = 1)
    {
        Ascending(longPut, shortPut, shortCall, longCall);
        return Build(StrategyKind.IronCondor,
            Put(L, longPut, expiry, quantity), Put(S, shortPut, expiry, quantity),
            Call(S, shortCall, expiry, quantity), Call(L, longCall, expiry, quantity));
    }

    /// <summary>Long butterfly: long 1 lower, short 2 middle, long 1 upper (all calls or all puts). Unequal wings are allowed.</summary>
    public static OptionStrategy Butterfly(OptionRight right, LegQuote lower, LegQuote middle, LegQuote upper, DateOnly expiry, int quantity = 1)
    {
        Ascending(lower, middle, upper);
        Func<PositionSide, LegQuote, DateOnly, int, StrategyLeg> leg = right == OptionRight.Call ? Call : Put;
        return Build(right == OptionRight.Call ? StrategyKind.LongCallButterfly : StrategyKind.LongPutButterfly,
            leg(L, lower, expiry, quantity), leg(S, middle, expiry, checked(2 * quantity)), leg(L, upper, expiry, quantity));
    }

    private static OptionStrategy Build(StrategyKind kind, params StrategyLeg[] legs) => new(legs, kind.ToString(), kind);

    private static StrategyLeg Call(PositionSide side, LegQuote q, DateOnly expiry, int quantity)
        => StrategyLeg.Call(side, q.Strike, q.Premium, expiry, quantity, q.ImpliedVolatility);

    private static StrategyLeg Put(PositionSide side, LegQuote q, DateOnly expiry, int quantity)
        => StrategyLeg.Put(side, q.Strike, q.Premium, expiry, quantity, q.ImpliedVolatility);

    private static StrategyLeg Shares(double stockPrice, int contracts)
    {
        if (contracts <= 0) throw new ArgumentOutOfRangeException(nameof(contracts), contracts, "Contracts must be positive.");
        return StrategyLeg.Stock(L, checked(contracts * StrategyLeg.DefaultMultiplier), stockPrice);
    }

    private static void Ascending(params LegQuote[] quotes)
    {
        for (int i = 1; i < quotes.Length; i++)
            if (!(quotes[i].Strike > quotes[i - 1].Strike))
                throw new ArgumentException("Strikes must be strictly ascending in the documented leg order.", nameof(quotes));
    }
}