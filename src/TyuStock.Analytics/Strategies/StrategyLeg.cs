namespace TyuStock.Analytics;

/// <summary>Instrument of one strategy leg.</summary>
public enum LegType
{
    Call,
    Put,
    /// <summary>The underlying itself (shares). Quantity is in shares and the multiplier is always 1.</summary>
    Stock,
}

public enum PositionSide
{
    Long,
    Short,
}

/// <summary>
/// One leg of an option strategy.
/// <para><see cref="Quantity"/> is always positive (contracts for options, shares for stock); direction comes from <see cref="Side"/>.
/// <see cref="Premium"/> is the per-share price paid (long) or received (short); for stock legs it is the entry price.
/// <see cref="ImpliedVolatility"/> (decimal, 0.25 = 25%) is only needed for pre-expiry valuation; NaN means "not set".</para>
/// <para>Signed money amounts follow one convention everywhere: positive = profit / cash paid (debit) for <see cref="Cost"/>.</para>
/// </summary>
public sealed record StrategyLeg
{
    /// <summary>Standard US equity/ETF option contract size.</summary>
    public const int DefaultMultiplier = 100;

    public StrategyLeg(LegType type, PositionSide side, int quantity, double strike, double premium, DateOnly? expiry = null,
        double impliedVolatility = double.NaN, int multiplier = DefaultMultiplier)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive; use Side for short positions.");
        if (!double.IsFinite(premium) || premium < 0) throw new ArgumentOutOfRangeException(nameof(premium), premium, "Premium / entry price must be finite and >= 0.");
        if (type == LegType.Stock)
        {
            strike = 0;
            expiry = null;
            impliedVolatility = double.NaN;
            multiplier = 1;
        }
        else
        {
            if (!double.IsFinite(strike) || strike <= 0) throw new ArgumentOutOfRangeException(nameof(strike), strike, "Strike must be finite and > 0.");
            if (expiry is null) throw new ArgumentException("Option legs need an expiry date.", nameof(expiry));
            if (multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "Multiplier must be positive.");
            if (!double.IsNaN(impliedVolatility) && !(double.IsFinite(impliedVolatility) && impliedVolatility >= 0))
                throw new ArgumentOutOfRangeException(nameof(impliedVolatility), impliedVolatility, "IV must be NaN (unset) or a finite decimal >= 0.");
        }
        Type = type; Side = side; Quantity = quantity; Strike = strike; Premium = premium;
        Expiry = expiry; ImpliedVolatility = impliedVolatility; Multiplier = multiplier;
    }

    public LegType Type { get; }
    public PositionSide Side { get; }
    public int Quantity { get; }
    /// <summary>Strike (0 for stock legs).</summary>
    public double Strike { get; }
    /// <summary>Per-share premium (options) or entry price (stock).</summary>
    public double Premium { get; }
    /// <summary>Expiration date (New York); null for stock legs.</summary>
    public DateOnly? Expiry { get; }
    public double ImpliedVolatility { get; }
    /// <summary>Shares per contract (1 for stock legs).</summary>
    public int Multiplier { get; }

    public bool IsOption => Type != LegType.Stock;
    public OptionRight? Right => Type switch { LegType.Call => OptionRight.Call, LegType.Put => OptionRight.Put, _ => null };
    public bool HasImpliedVolatility => double.IsFinite(ImpliedVolatility);

    /// <summary>+1 for long, -1 for short.</summary>
    public int Sign => Side == PositionSide.Long ? 1 : -1;

    /// <summary>Signed share-equivalent size: Sign x Quantity x Multiplier (e.g. -200 for 2 short contracts).</summary>
    public double Units => (double)Sign * Quantity * Multiplier;

    /// <summary>Cash paid to open the leg (positive = debit, negative = credit).</summary>
    public double Cost => Units * Premium;

    /// <summary>Per-share value at the leg's own expiry for an underlying price: intrinsic value, or the price itself for stock.</summary>
    public double ValueAtExpiry(double underlying) => Type switch
    {
        LegType.Call => Math.Max(underlying - Strike, 0.0),
        LegType.Put => Math.Max(Strike - underlying, 0.0),
        _ => underlying,
    };

    /// <summary>Position P/L (money) when every option is settled at intrinsic value.</summary>
    public double PnlAtExpiry(double underlying) => Units * (ValueAtExpiry(underlying) - Premium);

    public StrategyLeg WithImpliedVolatility(double impliedVolatility)
        => new(Type, Side, Quantity, Strike, Premium, Expiry, impliedVolatility, Multiplier);

    public StrategyLeg WithPremium(double premium)
        => new(Type, Side, Quantity, Strike, premium, Expiry, ImpliedVolatility, Multiplier);

    public StrategyLeg WithQuantity(int quantity)
        => new(Type, Side, quantity, Strike, Premium, Expiry, ImpliedVolatility, Multiplier);

    public static StrategyLeg Call(PositionSide side, double strike, double premium, DateOnly expiry, int quantity = 1,
        double impliedVolatility = double.NaN, int multiplier = DefaultMultiplier)
        => new(LegType.Call, side, quantity, strike, premium, expiry, impliedVolatility, multiplier);

    public static StrategyLeg Put(PositionSide side, double strike, double premium, DateOnly expiry, int quantity = 1,
        double impliedVolatility = double.NaN, int multiplier = DefaultMultiplier)
        => new(LegType.Put, side, quantity, strike, premium, expiry, impliedVolatility, multiplier);

    /// <summary>A stock leg of <paramref name="shares"/> shares opened at <paramref name="entryPrice"/>.</summary>
    public static StrategyLeg Stock(PositionSide side, int shares, double entryPrice)
        => new(LegType.Stock, side, shares, 0, entryPrice);

    public override string ToString() => Type == LegType.Stock
        ? $"{Side} {Quantity} sh @ {Premium:0.####}"
        : $"{Side} {Quantity} {Expiry:yyyy-MM-dd} {Strike:0.####} {Type} @ {Premium:0.####}";
}