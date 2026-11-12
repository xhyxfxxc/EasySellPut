using System.Globalization;
using TyuStock.Core.Models;

namespace TyuStock.Core.Export;

/// <summary>Turns live market objects into <see cref="ExportTable"/>s. Headers are supplied by the caller so they can be localized.</summary>
public static class MarketExport
{
    public static ExportTable Quotes(string name, IReadOnlyList<string> headers, IEnumerable<Quote> quotes) =>
        new()
        {
            Name = name,
            Headers = headers,
            Rows = quotes.Select(q => (IReadOnlyList<string>)
            [
                q.Symbol,
                q.Name ?? "",
                Num(q.Price),
                Num(q.Change),
                Num(q.ChangePercent),
                Num(q.Volume, "0"),
                Stamp(q.AsOf),
                q.Source,
                Num(q.Bid),
                Num(q.Ask),
                Num(q.ExtendedPrice),
                Num(q.ExtendedChangePercent),
                q.MarketState.ToString(),
            ]).ToList(),
        };

    public static ExportTable Bars(string name, IReadOnlyList<string> headers, BarSeries series) =>
        new()
        {
            Name = name,
            Headers = headers,
            Rows = series.Bars.Select(b => (IReadOnlyList<string>)
            [
                Stamp(b.Time),
                Num(b.Open),
                Num(b.High),
                Num(b.Low),
                Num(b.Close),
                Num(b.Volume, "0"),
            ]).ToList(),
        };

    public static ExportTable OptionChain(string name, IReadOnlyList<string> headers, OptionChain chain) =>
        new()
        {
            Name = name,
            Headers = headers,
            Rows = chain.Contracts.Select(c => (IReadOnlyList<string>)
            [
                c.ContractSymbol,
                c.Right.ToString(),
                Num(c.Strike),
                c.Expiration.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Num(c.Bid),
                Num(c.Ask),
                Num(c.Last),
                Num(c.Volume, "0"),
                Num(c.OpenInterest, "0"),
                Num(c.ImpliedVolatility),
                Num(c.Delta),
                Num(c.Gamma),
                Num(c.Theta),
                Num(c.Vega),
            ]).ToList(),
        };

    public static ExportTable Fundamentals(string name, IReadOnlyList<string> fieldHeaders, Fundamentals f,
        IReadOnlyList<(string Label, string Value)> rows) =>
        new()
        {
            Name = name,
            Headers = fieldHeaders,
            Rows = rows.Select(r => (IReadOnlyList<string>)[r.Label, r.Value]).ToList(),
        };

    public static ExportTable Statement(string name, StatementTable table, IReadOnlyList<string>? localizedItems = null)
    {
        var headers = new List<string> { name };
        headers.AddRange(table.Periods.Select(p => p.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var rows = new List<IReadOnlyList<string>>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var label = localizedItems is not null && i < localizedItems.Count ? localizedItems[i] : row.Item;
            var cells = new List<string> { label };
            cells.AddRange(row.Values.Select(v => Num(v)));
            rows.Add(cells);
        }
        return new ExportTable { Name = name, Headers = headers, Rows = rows };
    }

    public static ExportTable News(string name, IReadOnlyList<string> headers, IEnumerable<NewsItem> items) =>
        new()
        {
            Name = name,
            Headers = headers,
            Rows = items.Select(n => (IReadOnlyList<string>)
            [
                n.Title,
                n.Publisher ?? "",
                Stamp(n.Published),
                n.Url,
                n.Source,
            ]).ToList(),
        };

    public static string Num(double value, string format = "0.########") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    public static string Num(double? value, string format = "0.########") =>
        value is double v ? v.ToString(format, CultureInfo.InvariantCulture) : "";

    public static string Stamp(DateTimeOffset? value) =>
        value is DateTimeOffset t ? t.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) : "";
}