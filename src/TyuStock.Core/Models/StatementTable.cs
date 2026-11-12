using System.Globalization;

namespace TyuStock.Core.Models;

/// <summary>One line of a pivoted financial statement (period values are aligned with <see cref="StatementTable.Periods"/>).</summary>
public sealed record StatementRow(string Item, IReadOnlyList<double?> Values);

/// <summary>
/// Pivots <see cref="FinancialLine"/> rows into a compact table: newest period first, a fixed item order,
/// and at most <c>maxPeriods</c> columns. Items with no values in the selected window are omitted.
/// </summary>
public sealed record StatementTable(
    FinancialStatementKind Kind,
    bool Annual,
    IReadOnlyList<DateOnly> Periods,
    IReadOnlyList<StatementRow> Rows)
{
    public bool IsEmpty => Periods.Count == 0 || Rows.Count == 0;

    public static IReadOnlyList<string> ItemsFor(FinancialStatementKind kind) => kind switch
    {
        FinancialStatementKind.Income =>
        [
            FinancialItems.TotalRevenue, FinancialItems.GrossProfit, FinancialItems.OperatingIncome,
            FinancialItems.NetIncome, FinancialItems.DilutedEps,
        ],
        FinancialStatementKind.Balance =>
        [
            FinancialItems.TotalAssets, FinancialItems.TotalLiabilities, FinancialItems.StockholdersEquity,
        ],
        FinancialStatementKind.CashFlow =>
        [
            FinancialItems.OperatingCashFlow, FinancialItems.CapitalExpenditure, FinancialItems.FreeCashFlow,
        ],
        _ => [],
    };

    public static StatementTable Build(
        IEnumerable<FinancialLine> lines,
        FinancialStatementKind kind,
        bool annual = true,
        int maxPeriods = 4)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (maxPeriods <= 0) throw new ArgumentOutOfRangeException(nameof(maxPeriods), maxPeriods, "Need at least one period.");

        var set = lines.Where(l => l.Statement == kind && l.Annual == annual).ToList();
        var periods = set.Select(l => l.PeriodEnd).Distinct().OrderByDescending(d => d).Take(maxPeriods).ToList();
        var rows = new List<StatementRow>();
        foreach (var item in ItemsFor(kind))
        {
            var vals = new double?[periods.Count];
            bool any = false;
            for (int i = 0; i < periods.Count; i++)
            {
                var hit = set.FirstOrDefault(l => l.Item == item && l.PeriodEnd == periods[i]);
                if (hit is null) continue;
                vals[i] = hit.Value;
                any = true;
            }
            if (any) rows.Add(new StatementRow(item, vals));
        }
        return new StatementTable(kind, annual, periods, rows);
    }
}

/// <summary>Culture-invariant compact number and percent formatting for quotes, fundamentals and financials.</summary>
public static class NumberDisplay
{
    public const string Empty = "\u2014";

    /// <summary>
    /// G2: one format for volumes, open interest, market caps and statement amounts. Chinese uses 万/亿/万亿 (10⁴/10⁸/10¹²),
    /// English K/M/B/T; always two decimals once scaled so columns line up, plain thousands-separated integers below
    /// 10,000, and two decimals for small fractional values.
    /// </summary>
    public static string Amount(double? value, bool chinese, string empty = Empty)
    {
        if (value is not { } x || !double.IsFinite(x)) return empty;
        var inv = CultureInfo.InvariantCulture;
        var sign = x < 0 ? "-" : "";
        var a = Math.Abs(x);
        if (chinese)
        {
            if (a >= 1e12) return sign + (a / 1e12).ToString("0.00", inv) + "万亿";
            if (a >= 1e8) return sign + (a / 1e8).ToString("0.00", inv) + "亿";
            if (a >= 1e4) return sign + (a / 1e4).ToString("0.00", inv) + "万";
        }
        else
        {
            if (a >= 1e12) return sign + (a / 1e12).ToString("0.00", inv) + "T";
            if (a >= 1e9) return sign + (a / 1e9).ToString("0.00", inv) + "B";
            if (a >= 1e6) return sign + (a / 1e6).ToString("0.00", inv) + "M";
            if (a >= 1e4) return sign + (a / 1e3).ToString("0.00", inv) + "K";
        }
        return Math.Abs(a - Math.Round(a)) < 1e-9 ? sign + Math.Round(a).ToString("N0", inv) : x.ToString("0.00", inv);
    }

    public static string Compact(double? value, string empty = Empty)
    {
        if (value is not { } x || !double.IsFinite(x)) return empty;
        string sign = x < 0 ? "-" : "";
        double a = Math.Abs(x);
        if (a >= 1e12) return sign + (a / 1e12).ToString("0.00", CultureInfo.InvariantCulture) + "T";
        if (a >= 1e9) return sign + (a / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + "B";
        if (a >= 1e6) return sign + (a / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M";
        if (a >= 1000) return sign + a.ToString("N0", CultureInfo.InvariantCulture);
        return x.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// G1: precision follows the price level — below 1 up to 4 decimals, 1–10 up to 3, otherwise 2 (thousands
    /// separators from 1,000). Trailing extra zeros are not padded, so 0.05 stays "0.05" but 0.1234 keeps all four.
    /// </summary>
    public static string PriceFormat(double reference)
    {
        var a = Math.Abs(reference);
        return a < 1 ? "0.00##" : a < 10 ? "0.00#" : a < 1000 ? "0.00" : "#,##0.00";
    }

    public static string Price(double? value, string empty = Empty)
        => value is { } x && double.IsFinite(x) ? x.ToString(PriceFormat(x), CultureInfo.InvariantCulture) : empty;

    /// <summary>A signed price change shown with the precision of the price it belongs to (e.g. +0.0125 on a $0.41 stock).</summary>
    public static string SignedChange(double? change, double reference, string empty = Empty)
    {
        if (change is not { } c || !double.IsFinite(c)) return empty;
        var body = Math.Abs(c).ToString(PriceFormat(reference), CultureInfo.InvariantCulture);
        return (c > 0 ? "+" : c < 0 ? "-" : "") + body;
    }

    public static string Percent(double? decimalFraction, string empty = Empty)
        => decimalFraction is { } x && double.IsFinite(x)
            ? (x * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%"
            : empty;

    public static string Integer(double? value, string empty = Empty)
        => value is { } x && double.IsFinite(x) ? Math.Round(x).ToString("N0", CultureInfo.InvariantCulture) : empty;

    public static string Ratio(double? value, string empty = Empty)
        => value is { } x && double.IsFinite(x) ? x.ToString("0.00", CultureInfo.InvariantCulture) : empty;

    public static string Range(double? low, double? high, string empty = Empty)
    {
        if (low is not { } a || high is not { } b || !double.IsFinite(a) || !double.IsFinite(b)) return empty;
        return Price(a) + " \u2013 " + Price(b);
    }
}