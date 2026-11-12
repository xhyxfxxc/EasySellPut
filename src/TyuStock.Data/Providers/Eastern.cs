using System.Globalization;
using TyuStock.Core.Time;

namespace TyuStock.Data.Providers;

/// <summary>Parsing helpers for the many ways free APIs write New York wall-clock times.</summary>
internal static class Eastern
{
    private static readonly string[] Formats =
        ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MMM d, yyyy", "MMM dd, yyyy"];

    /// <summary>Parses a New York local time string into an absolute time.</summary>
    public static DateTimeOffset? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(s.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? MarketClock.FromEastern(d) : null;
    }

    public static DateOnly? Date(string? s) =>
        !string.IsNullOrWhiteSpace(s) && DateTime.TryParseExact(s.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? DateOnly.FromDateTime(d) : null;

    /// <summary>"2026-11-11T16:00:00.000-0500" (offset without colon) or any ISO-8601 time with offset.</summary>
    public static DateTimeOffset? ParseWithOffset(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Length > 5 && (s[^5] is '+' or '-') && char.IsDigit(s[^1]) && s[^3] != ':') s = s[..^2] + ":" + s[^2..];
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>Today's date in New York.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(MarketClock.ToEastern(DateTimeOffset.UtcNow));

    public static Core.Models.MarketState SessionNow() => MarketClock.GetSession(DateTimeOffset.UtcNow) switch
    {
        MarketSession.PreMarket => Core.Models.MarketState.PreMarket,
        MarketSession.Regular => Core.Models.MarketState.Regular,
        MarketSession.AfterHours => Core.Models.MarketState.AfterHours,
        _ => Core.Models.MarketState.Closed,
    };
}