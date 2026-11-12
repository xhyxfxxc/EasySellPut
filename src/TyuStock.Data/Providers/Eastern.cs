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

    private static readonly string[] TimestampFormats =
        ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "MMM d, yyyy h:mm tt", "MMM dd, yyyy h:mm tt",
         "MMM d, yyyy hh:mm tt", "MMM d, yyyy h:mm:ss tt", "MM/dd/yyyy h:mm tt", "MM/dd/yyyy HH:mm:ss"];

    /// <summary>
    /// Like <see cref="Parse"/> but only accepts strings that carry a time of day ("Nov 12, 2026 10:31 AM ET").
    /// A bare date ("Nov 11, 2026") is not a timestamp and returns null, so it can never be mistaken for a quote time.
    /// </summary>
    public static DateTimeOffset? ParseTimestamp(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        foreach (var suffix in new[] { " ET", " EST", " EDT" })
            if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { t = t[..^suffix.Length].TrimEnd(); break; }
        return DateTime.TryParseExact(t, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
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