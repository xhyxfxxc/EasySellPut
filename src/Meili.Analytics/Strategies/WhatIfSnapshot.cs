namespace Meili.Analytics;

/// <summary>Combination and per-leg value / P/L at one assumed underlying price.</summary>
public sealed record WhatIfSnapshot(
    double Underlying,
    double CombinationValue,
    double CombinationPnl,
    IReadOnlyList<WhatIfLegResult> Legs);

/// <summary>
/// One leg at the what-if price. <see cref="TheoreticalPrice"/> is the per-share (or per-stock) mark;
/// <see cref="Pnl"/> is the position's money P/L (quantity and multiplier applied).
/// </summary>
public sealed record WhatIfLegResult(
    StrategyLeg Leg,
    double TheoreticalPrice,
    double Pnl,
    WhatIfFacts Facts);

/// <summary>Facts the UI uses to build a plain-language reason. Positive <see cref="Moneyness"/> means in-the-money.</summary>
public readonly record struct WhatIfFacts(
    bool AtExpiry,
    bool IsStock,
    bool IsCall,
    bool IsShort,
    double Strike,
    double EntryPrice,
    double Underlying,
    double Moneyness,
    double Intrinsic,
    double TimeValue);

/// <summary>Evaluates an <see cref="OptionStrategy"/> at a single underlying price. Does not re-price the curve.</summary>
public static class WhatIf
{
    /// <summary>
    /// At expiry (or when <paramref name="valuation"/> is null) every option is settled at intrinsic value;
    /// otherwise existing <see cref="OptionStrategy.LegGreeks"/> / BSM prices are used.
    /// </summary>
    public static WhatIfSnapshot Capture(OptionStrategy strategy, double underlying, StrategyValuation? valuation)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var legs = new WhatIfLegResult[strategy.Legs.Count];
        double value = 0, pnl = 0;
        for (int i = 0; i < strategy.Legs.Count; i++)
        {
            legs[i] = Evaluate(strategy.Legs[i], underlying, valuation);
            value += strategy.Legs[i].Units * legs[i].TheoreticalPrice;
            pnl += legs[i].Pnl;
        }
        return new WhatIfSnapshot(underlying, value, pnl, legs);
    }

    public static WhatIfLegResult Evaluate(StrategyLeg leg, double underlying, StrategyValuation? valuation)
    {
        ArgumentNullException.ThrowIfNull(leg);
        bool expiry = valuation is null || (leg.IsOption && valuation.YearsTo(leg.Expiry!.Value) <= 0);
        double price, moneyness = 0, intrinsic = 0, timeValue = 0;
        if (!leg.IsOption)
        {
            price = underlying;
        }
        else if (expiry || !double.IsFinite(underlying) || underlying < 0)
        {
            price = leg.ValueAtExpiry(double.IsFinite(underlying) && underlying >= 0 ? underlying : 0);
            intrinsic = price;
        }
        else
        {
            var g = OptionStrategy.LegGreeks(leg, underlying, valuation!);
            price = g.Price;
            intrinsic = leg.ValueAtExpiry(underlying);
            timeValue = price - intrinsic;
        }
        if (leg.IsOption)
            moneyness = leg.Type == LegType.Call ? underlying - leg.Strike : leg.Strike - underlying;
        var facts = new WhatIfFacts(expiry, !leg.IsOption, leg.Type == LegType.Call, leg.Side == PositionSide.Short,
            leg.Strike, leg.Premium, underlying, moneyness, intrinsic, timeValue);
        return new WhatIfLegResult(leg, price, leg.Units * (price - leg.Premium), facts);
    }
}

/// <summary>
/// Picks a localization key and format arguments for one what-if leg. The UI formats
/// <c>string.Format(loc[key], args)</c>. Keys live in the St_Why_* resource strings.
/// </summary>
public static class WhatIfExplainer
{
    public static (string Key, object[] Args) Describe(WhatIfLegResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var f = result.Facts;
        var px = Px(f.Underlying);
        var entry = Px(f.EntryPrice);
        var k = Px(f.Strike);
        var itm = Px(Math.Abs(f.Moneyness));
        var mark = Px(result.TheoreticalPrice);
        var money = Money(Math.Abs(result.Pnl));
        if (f.IsStock)
        {
            if (result.Pnl >= 0)
                return ("St_Why_StockGain", [px, entry, money]);
            return ("St_Why_StockLoss", [px, entry, money]);
        }
        string right = f.IsCall ? "St_Call" : "St_Put";
        bool itmNow = f.Moneyness > 1e-8;
        if (f.AtExpiry)
        {
            if (f.IsShort && itmNow)
                return (f.IsCall ? "St_Why_ShortCallItmExpiry" : "St_Why_ShortPutItmExpiry", [k, itm]);
            if (f.IsShort)
                return ("St_Why_ShortOtmExpiry", []);
            if (itmNow)
                return ("St_Why_LongItmExpiry", [k, right, itm, mark]);
            return ("St_Why_LongOtmExpiry", []);
        }
        if (f.IsShort && itmNow) return ("St_Why_ShortItmLive", [k, right, itm, mark]);
        if (f.IsShort) return ("St_Why_ShortOtmLive", [k, right, mark]);
        if (itmNow) return ("St_Why_LongItmLive", [k, right, itm, mark]);
        return ("St_Why_LongOtmLive", [k, right, mark]);
    }

    /// <summary>A readable label such as "卖出 63 认购". <paramref name="name"/> resolves St_Buy / St_Sell / St_Call / St_Put / St_Stock. When <paramref name="showExpiry"/> is set, an option label appends its date.</summary>
    public static string LegLabel(StrategyLeg leg, Func<string, string> name, bool showExpiry = false)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(name);
        string side = name(leg.Side == PositionSide.Short ? "St_Sell" : "St_Buy");
        if (!leg.IsOption) return $"{side} {name("St_Stock")}";
        string right = name(leg.Type == LegType.Call ? "St_Call" : "St_Put");
        string body = $"{side} {Px(leg.Strike)} {right}";
        return showExpiry && leg.Expiry is { } d ? $"{body} · {d:yyyy-MM-dd}" : body;
    }

    public static string Format(WhatIfLegResult result, Func<string, string> loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var (key, args) = Describe(result);
        // Right-type placeholders are themselves resource keys (St_Call / St_Put).
        for (int i = 0; i < args.Length; i++)
            if (args[i] is string s && s is "St_Call" or "St_Put") args[i] = loc(s);
        return args.Length == 0 ? loc(key) : string.Format(loc(key), args);
    }

    private static string Px(double v) => v == Math.Truncate(v) && Math.Abs(v) < 10000
        ? v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
        : v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string Money(double v) => v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}
