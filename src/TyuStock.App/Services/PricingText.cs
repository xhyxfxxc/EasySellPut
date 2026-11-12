using System.Globalization;
using TyuStock.Core.Models;
using TyuStock.Core.Pricing;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Services;

/// <summary>F5: the "model / rate" label shown next to locally priced options and strategy curves.</summary>
public static class PricingText
{
    public static string Model(RiskFreeRate rate, LocalizationService loc)
    {
        var pct = rate.Percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        var origin = rate.Origin switch
        {
            RateOrigin.Treasury13Week => loc["Px_RateTreasury"],
            RateOrigin.Fixed => loc["Px_RateFixed"],
            _ => loc["Px_RateFallback"],
        };
        return string.Format(loc["Px_Model"], pct, origin);
    }
}

/// <summary>
/// F8: which underlying price the option maths uses, and the latest stock price beside it. The chain's own underlying
/// price shares the option quotes' delay (e.g. 15 minutes) and is what IV/Greeks are computed from; mixing in a
/// real-time stock price would misprice moneyness, so it is shown only for comparison.
/// </summary>
public static class UnderlyingText
{
    public sealed record Result(double? Spot, string Main, string Live);

    public static Result Describe(OptionChain? chain, Quote? quote, LocalizationService loc, DateTimeOffset now)
    {
        if (chain?.UnderlyingPrice is > 0 and var cs)
        {
            var f = DataFreshness.Evaluate(chain, now);
            var main = f.Kind == FreshnessKind.Delayed
                ? string.Format(loc["Op_SpotSynced"], NumberDisplay.Price(cs), (int)Math.Round(f.Lag!.Value.TotalMinutes))
                : string.Format(loc["Op_SpotSyncedNoDelay"], NumberDisplay.Price(cs));
            var live = quote is { Price: > 0 } q
                ? string.Format(loc["Op_LivePrice"], NumberDisplay.Price(q.Price), FreshnessText.SourceAndTiming(q, loc, now))
                : "";
            return new Result(cs, main, live);
        }
        if (quote is { Price: > 0 } qq)
            return new Result(qq.Price, string.Format(loc["Op_SpotFromQuote"], NumberDisplay.Price(qq.Price)), "");
        if (quote is { PreviousClose: > 0 and var pc })
            return new Result(pc, string.Format(loc["Op_SpotPrevClose"], NumberDisplay.Price(pc)), "");
        return new Result(null, "", "");
    }
}
