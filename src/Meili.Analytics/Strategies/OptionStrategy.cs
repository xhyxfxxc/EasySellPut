namespace Meili.Analytics;

/// <summary>Known strategy shapes (for display / localisation keys); <see cref="Custom"/> for anything user-built.</summary>
public enum StrategyKind
{
    Custom,
    LongCall,
    LongPut,
    CoveredCall,
    ProtectivePut,
    BullCallSpread,
    BearCallSpread,
    BullPutSpread,
    BearPutSpread,
    LongStraddle,
    ShortStraddle,
    LongStrangle,
    ShortStrangle,
    IronCondor,
    LongCallButterfly,
    LongPutButterfly,
    Collar,
    IronButterfly,
    CalendarCall,
    CalendarPut,
    DiagonalCall,
}

/// <summary>
/// A multi-leg option/stock position with expiry payoff, pre-expiry (Black-Scholes-Merton) P/L, position Greeks,
/// breakevens and max profit/loss. Immutable and thread-safe.
/// <para>"At expiry" means every option is settled at intrinsic value. For strategies whose legs expire on different dates
/// (calendars/diagonals) use <see cref="PnlAt"/> / <see cref="AnalyzeAt"/> with a valuation at <see cref="EarliestExpiry"/>.</para>
/// </summary>
public sealed partial class OptionStrategy
{
    private readonly StrategyLeg[] _legs;

    public OptionStrategy(IEnumerable<StrategyLeg> legs, string? name = null, StrategyKind kind = StrategyKind.Custom)
    {
        ArgumentNullException.ThrowIfNull(legs);
        _legs = legs.ToArray();
        if (_legs.Length == 0) throw new ArgumentException("A strategy needs at least one leg.", nameof(legs));
        foreach (var leg in _legs) ArgumentNullException.ThrowIfNull(leg, nameof(legs));
        Kind = kind;
        Name = name ?? kind.ToString();
        Strikes = _legs.Where(l => l.IsOption).Select(l => l.Strike).Distinct().Order().ToArray();
        var expiries = _legs.Where(l => l.Expiry.HasValue).Select(l => l.Expiry!.Value).ToArray();
        EarliestExpiry = expiries.Length > 0 ? expiries.Min() : null;
        LatestExpiry = expiries.Length > 0 ? expiries.Max() : null;
        NetCost = _legs.Sum(l => l.Cost);
    }

    public string Name { get; }
    public StrategyKind Kind { get; }
    public IReadOnlyList<StrategyLeg> Legs => _legs;

    /// <summary>Distinct option strikes, ascending.</summary>
    public IReadOnlyList<double> Strikes { get; }

    public DateOnly? EarliestExpiry { get; }
    public DateOnly? LatestExpiry { get; }
    public bool HasMixedExpiries => EarliestExpiry != LatestExpiry;

    /// <summary>Cash paid to open the whole position: positive = net debit, negative = net credit.</summary>
    public double NetCost { get; }

    /// <summary>True when every option leg has its own implied volatility.</summary>
    public bool HasAllImpliedVolatilities => _legs.All(l => !l.IsOption || l.HasImpliedVolatility);

    public OptionStrategy WithLegs(IEnumerable<StrategyLeg> legs) => new(legs, Name, StrategyKind.Custom);

    // ------------------------------------------------------------------ point evaluation

    /// <summary>Position P/L with every option settled at intrinsic value.</summary>
    public double PnlAtExpiry(double underlying)
    {
        double pnl = 0;
        foreach (var leg in _legs) pnl += leg.PnlAtExpiry(underlying);
        return pnl;
    }

    /// <summary>Theoretical market value of the whole position (money). NaN if a live option leg has no volatility.</summary>
    public double ValueAt(double underlying, StrategyValuation valuation) => GreeksAt(underlying, valuation).Value;

    /// <summary>Theoretical P/L (value minus <see cref="NetCost"/>) at <paramref name="valuation"/>. NaN if a live option leg has no volatility.</summary>
    public double PnlAt(double underlying, StrategyValuation valuation) => GreeksAt(underlying, valuation).Pnl;

    /// <summary>Position value, P/L and Greeks at <paramref name="underlying"/> under <paramref name="valuation"/>.</summary>
    public StrategyGreeks GreeksAt(double underlying, StrategyValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        double value = 0, delta = 0, gamma = 0, vega = 0, theta = 0, rho = 0;
        foreach (var leg in _legs)
        {
            var g = LegGreeks(leg, underlying, valuation);
            double u = leg.Units;
            value += u * g.Price; delta += u * g.Delta; gamma += u * g.Gamma; vega += u * g.Vega; theta += u * g.Theta; rho += u * g.Rho;
        }
        return new StrategyGreeks(value, value - NetCost, delta, gamma, vega, theta, rho);
    }

    /// <summary>Per-share price and Greeks of one leg (stock: price = underlying, delta = 1).</summary>
    public static OptionGreeks LegGreeks(StrategyLeg leg, double underlying, StrategyValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(valuation);
        if (!double.IsFinite(underlying) || underlying < 0) return OptionGreeks.NaN;
        if (!leg.IsOption) return new OptionGreeks(underlying, 1, 0, 0, 0, 0);

        var right = leg.Right!.Value;
        double years = valuation.YearsTo(leg.Expiry!.Value);
        double r = valuation.Rate, q = valuation.DividendYield;
        if (years <= 0)
        {
            if (underlying > 0) return BlackScholes.Greeks(right, underlying, leg.Strike, 0, r, q, 0);
            return right == OptionRight.Call ? new OptionGreeks(0, 0, 0, 0, 0, 0) : new OptionGreeks(leg.Strike, -1, 0, 0, 0, 0);
        }
        double vol = valuation.VolatilityFor(leg);
        if (double.IsNaN(vol)) return OptionGreeks.NaN;
        if (underlying > 0) return BlackScholes.Greeks(right, underlying, leg.Strike, years, r, q, vol);

        // S = 0 (limit): the call is worthless; the put is a discounted strike.
        if (right == OptionRight.Call) return new OptionGreeks(0, 0, 0, 0, 0, 0);
        double dr = Math.Exp(-r * years), dq = Math.Exp(-q * years);
        return new OptionGreeks(leg.Strike * dr, -dq, 0, 0, r * leg.Strike * dr / OptionTime.DaysPerYear, -leg.Strike * years * dr / 100.0);
    }

    // ------------------------------------------------------------------ curves

    /// <summary>
    /// Samples P/L on [<paramref name="low"/>, <paramref name="high"/>]: <paramref name="points"/> evenly spaced prices plus every
    /// strike inside the range (so expiry kinks are drawn exactly). Adds the pre-expiry curve when <paramref name="valuation"/> is given.
    /// </summary>
    public PayoffCurve Curve(double low, double high, int points = 201, StrategyValuation? valuation = null)
    {
        var prices = BuildGrid(low, high, points);
        var expiry = new double[prices.Length];
        double[]? at = valuation is null ? null : new double[prices.Length];
        for (int i = 0; i < prices.Length; i++)
        {
            expiry[i] = PnlAtExpiry(prices[i]);
            if (at is not null) at[i] = PnlAt(prices[i], valuation!);
        }
        return new PayoffCurve(prices, expiry, at);
    }

    /// <summary>
    /// A sensible chart range around <paramref name="spot"/>: covers every strike, stock entry and expiry breakeven,
    /// padded by <paramref name="padding"/> (0.3 = 30%) on each side, never below 0.
    /// </summary>
    public (double Low, double High) SuggestPriceRange(double spot, double padding = 0.3)
    {
        if (!(padding >= 0) || !double.IsFinite(padding)) throw new ArgumentOutOfRangeException(nameof(padding));
        var pts = new List<double>(Strikes);
        if (double.IsFinite(spot) && spot > 0) pts.Add(spot);
        pts.AddRange(_legs.Where(l => !l.IsOption && l.Premium > 0).Select(l => l.Premium));
        pts.AddRange(AnalyzeAtExpiry().Breakevens.Where(b => b > 0));
        if (pts.Count == 0) throw new ArgumentException("Need a positive spot or at least one strike.", nameof(spot));
        double lo = pts.Min() * (1 - padding), hi = pts.Max() * (1 + padding);
        if (hi <= lo) hi = lo + 1;
        return (Math.Max(0, lo), hi);
    }

    internal static double[] BuildGrid(double low, double high, int points, IReadOnlyList<double>? extra = null)
    {
        if (!double.IsFinite(low) || !double.IsFinite(high) || low < 0 || high <= low)
            throw new ArgumentException("Need 0 <= low < high (finite).");
        if (points < 2) throw new ArgumentOutOfRangeException(nameof(points), points, "Need at least 2 points.");
        var list = new List<double>(points + 8);
        double step = (high - low) / (points - 1);
        for (int i = 0; i < points; i++) list.Add(i == points - 1 ? high : low + i * step);
        if (extra is not null) list.AddRange(extra.Where(x => x > low && x < high));
        list.Sort();
        var result = new List<double>(list.Count);
        double eps = 1e-12 * Math.Max(1.0, high);
        foreach (var x in list) if (result.Count == 0 || x - result[^1] > eps) result.Add(x);
        return result.ToArray();
    }

    private double[] BuildGrid(double low, double high, int points) => BuildGrid(low, high, points, Strikes);
}
