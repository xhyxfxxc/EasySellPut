using System.Globalization;
using Meili.Core.Models;

namespace Meili.Data.Providers;

/// <summary>OCC option symbol: ROOT + YYMMDD + C/P + strike×1000 (8 digits), e.g. AAPL270115P00420000.</summary>
public static class OccSymbol
{
    public static bool TryParse(string? s, out string root, out DateOnly expiration, out OptionRight right, out double strike)
    {
        root = ""; expiration = default; right = default; strike = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().Replace(" ", "").ToUpperInvariant();
        if (s.Length < 16) return false;
        var tail = s[^15..];
        root = s[..^15];
        if (!DateOnly.TryParseExact(tail[..6], "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out expiration)) return false;
        right = tail[6] switch { 'C' => OptionRight.Call, 'P' => OptionRight.Put, _ => (OptionRight)(-1) };
        if ((int)right < 0) return false;
        if (!long.TryParse(tail[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var k)) return false;
        strike = k / 1000.0;
        return root.Length > 0;
    }

    public static string Format(string root, DateOnly expiration, OptionRight right, double strike) =>
        $"{root.ToUpperInvariant()}{expiration:yyMMdd}{(right == OptionRight.Call ? 'C' : 'P')}{(long)Math.Round(strike * 1000):D8}";
}
