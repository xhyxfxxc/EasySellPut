using System.Globalization;
using TyuStock.Core.Pricing;

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
