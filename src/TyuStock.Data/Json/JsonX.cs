using System.Globalization;
using System.Text.Json;

namespace TyuStock.Data.Json;

/// <summary>Lenient JSON accessors for messy free APIs: numbers may arrive as numbers, "$1,234.5", "+2.43%", "N/A" or {"raw":..}.</summary>
public static class JsonX
{
    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v : null;

    public static JsonElement? Path(this JsonElement e, params string[] names)
    {
        JsonElement? cur = e;
        foreach (var n in names)
        {
            if (cur is null) return null;
            cur = cur.Value.Prop(n);
        }
        return cur;
    }

    public static JsonElement? At(this JsonElement e, int index) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > index ? e[index] : null;

    public static double? Num(this JsonElement? e) => e is { } v ? Num(v) : null;

    public static double? Num(this JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetDouble(out var d) && double.IsFinite(d) ? d : null,
        JsonValueKind.String => ParseNumber(e.GetString()),
        JsonValueKind.Object => e.Prop("raw") is { } raw ? Num(raw) : null,
        _ => null,
    };

    public static double? Num(this JsonElement e, string name) => e.Prop(name).Num();
    public static long? Long(this JsonElement e, string name) => e.Prop(name).Num() is { } d ? (long)d : null;

    public static string? Str(this JsonElement? e) => e is { } v ? Str(v) : null;
    public static string? Str(this JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => NullIfEmpty(e.GetString()),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object => e.Prop("fmt") is { } f ? Str(f) : null,
        _ => null,
    };
    public static string? Str(this JsonElement e, string name) => e.Prop(name).Str();

    public static bool? Bool(this JsonElement e, string name) => e.Prop(name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.String } s => bool.TryParse(s.GetString(), out var b) ? b : null,
        _ => null,
    };

    public static IEnumerable<JsonElement> Items(this JsonElement? e, string name) => e is { } v ? v.Prop(name).Items() : [];
    public static IEnumerable<JsonElement> Items(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    public static DateTimeOffset? UnixSeconds(this JsonElement e, string name) =>
        e.Num(name) is { } s && s > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)s) : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Parses "$1,234.50", "+2.43%", "-0.05", "1.2K/M/B/T", returning null for "N/A", "--", "".</summary>
    public static double? ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Length > 2 && s[0] == '(' && s[^1] == ')') s = "-" + s[1..^1]; // accounting negative: ($1,234)
        s = s.Replace("$", "").Replace(",", "").Replace("%", "").Replace("+", "").Trim();
        if (s is "" or "N/A" or "NA" or "--" or "-" or "null" or "UNCH") return null;
        double mult = 1;
        if (s.Length > 1)
        {
            switch (char.ToUpperInvariant(s[^1]))
            {
                case 'K': mult = 1e3; s = s[..^1]; break;
                case 'M': mult = 1e6; s = s[..^1]; break;
                case 'B': mult = 1e9; s = s[..^1]; break;
                case 'T': mult = 1e12; s = s[..^1]; break;
            }
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d * mult : null;
    }
}
