using System.Globalization;
using Meili.Core.Models;
using Meili.Core.Time;
using Meili.App.ViewModels;

namespace Meili.App.Services;

/// <summary>Localized wording for <see cref="DataFreshness"/> (F1/F4): never claims "real-time" unless the source does.</summary>
public static class FreshnessText
{
    public static string Timing(Freshness f, LocalizationService loc) => f.Kind switch
    {
        FreshnessKind.Realtime => loc["Quote_Realtime"],
        FreshnessKind.Delayed => string.Format(loc["Quote_DelayedN"], (int)Math.Round(f.Lag!.Value.TotalMinutes)),
        FreshnessKind.Measured => string.Format(loc["Quote_LagMeasured"], Lag(f.Lag!.Value, loc)),
        _ => loc["Quote_DelayUnknown"],
    };

    public static string Lag(TimeSpan lag, LocalizationService loc)
    {
        if (lag < TimeSpan.FromMinutes(1)) return loc["Quote_LagUnder1"];
        if (lag < TimeSpan.FromHours(2)) return string.Format(loc["Quote_LagMin"], (int)lag.TotalMinutes);
        return string.Format(loc["Quote_LagHours"], lag.TotalHours.ToString("0.#", CultureInfo.InvariantCulture));
    }

    /// <summary>"行情时间 11-12 15:59:59（美东）" or "行情时间未知".</summary>
    public static string AsOf(DateTimeOffset? asOf, LocalizationService loc) => asOf is { } t
        ? string.Format(loc["Quote_AsOfEt"], MarketClock.ToEastern(t).ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        : loc["Quote_AsOfUnknown"];

    /// <summary>"抓取于 20:31:05" in the user's local time.</summary>
    public static string Fetched(DateTimeOffset? fetched, LocalizationService loc) => fetched is { } t
        ? string.Format(loc["Quote_FetchedAt"], t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
        : "";

    /// <summary>Source · timing, e.g. "新浪 · 实测滞后约 3 分钟".</summary>
    public static string SourceAndTiming(SourcedData d, LocalizationService loc, DateTimeOffset now) =>
        $"{SourceNames.Display(d.Source, loc)} · {Timing(DataFreshness.Evaluate(d, now), loc)}";

    /// <summary>
    /// Option-chain badge (F4): declared delay plus the market time when the source states one; otherwise the fetch time,
    /// clearly labelled as such so it is never read as the quote time.
    /// </summary>
    public static string ChainTiming(OptionChain chain, LocalizationService loc, DateTimeOffset now)
    {
        var parts = new List<string> { Timing(DataFreshness.Evaluate(chain, now), loc) };
        if (chain.AsOf is not null) parts.Add(AsOf(chain.AsOf, loc));
        else
        {
            parts.Add(loc["Quote_AsOfUnknown"]);
            var f = Fetched(chain.FetchedAt, loc);
            if (f.Length > 0) parts.Add(f);
        }
        return string.Join(" · ", parts);
    }

    /// <summary>F7: localized adjustment basis, e.g. "拆股调整 · 不含分红".</summary>
    public static string Adjustment(PriceAdjustment a, LocalizationService loc) => a switch
    {
        PriceAdjustment.Split => loc["Ch_AdjSplit"],
        PriceAdjustment.SplitDividend => loc["Ch_AdjTotal"],
        PriceAdjustment.None => loc["Ch_AdjNone"],
        _ => loc["Ch_AdjUnknown"],
    };

    /// <summary>D8: "stale" explanation for a price, or null when the price is current.</summary>
    public static string? Stale(SourcedData d, LocalizationService loc, DateTimeOffset now) => QuoteStaleness.Check(d, now) switch
    {
        StaleReason.NotUpdated => string.Format(loc["Quote_StaleNotUpdated"], Lag(now - d.FetchedAt!.Value, loc)),
        StaleReason.SourceLagging => string.Format(loc["Quote_StaleLagging"], Lag(DataFreshness.Evaluate(d, now).Lag!.Value, loc)),
        _ => null,
    };

    /// <summary>D8: short time shown beside a price: market time (ET) when the source gives one, else the fetch time.</summary>
    public static string PriceTime(SourcedData d) => d.AsOf is { } t
        ? MarketClock.ToEastern(t).ToString("HH:mm", CultureInfo.InvariantCulture) + " ET"
        : d.FetchedAt is { } f ? f.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "";

    /// <summary>Multi-line tooltip: source, timing, market time, fetch time.</summary>
    public static string Tooltip(SourcedData d, LocalizationService loc, DateTimeOffset now)
    {
        var lines = new List<string> { SourceAndTiming(d, loc, now), AsOf(d.AsOf, loc) };
        var f = Fetched(d.FetchedAt, loc);
        if (f.Length > 0) lines.Add(f);
        return string.Join(Environment.NewLine, lines);
    }
}
