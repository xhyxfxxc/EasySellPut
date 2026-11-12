using System.Text.Json;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Data.Http;
using TyuStock.Data.Json;

namespace TyuStock.Data.Providers.China;

/// <summary>东方财富 push2 quotes (integer-scaled prices) and day/week/month K. Needs a market id: 105 Nasdaq, 106 NYSE, 107 NYSE Arca/AMEX.</summary>
public sealed class EastmoneyProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IQuoteProvider, IBarsProvider
{
    private static readonly int[] Markets = [105, 106, 107];
    private const string Fields = "f43,f44,f45,f46,f47,f48,f57,f58,f59,f60,f86,f116,f169,f170";

    public override string Id => "eastmoney";
    public override string DisplayName => "东方财富 Eastmoney";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.DailyBars;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(300);

    internal static string ToEastmoney(string symbol) => Norm(symbol).Replace('.', '_').Replace('-', '_');

    /// <summary>Finds (and remembers) which market a symbol trades on.</summary>
    private async Task<(string SecId, JsonDocument? Quote)> ResolveAsync(string symbol, CancellationToken ct)
    {
        var code = ToEastmoney(symbol);
        if (Cache.TryGet<string>($"em:secid:{code}", out var known)) return (known, null);
        foreach (var m in Markets)
        {
            var doc = await GetJsonAsync($"https://push2.eastmoney.com/api/qt/stock/get?secid={m}.{Enc(code)}&fields={Fields}", ct).ConfigureAwait(false);
            if (doc.RootElement.Prop("data") is { ValueKind: JsonValueKind.Object })
            {
                Cache.Set($"em:secid:{code}", $"{m}.{code}", TimeSpan.FromDays(30));
                return ($"{m}.{code}", doc);
            }
            doc.Dispose();
        }
        throw NoData($"unknown symbol {symbol}");
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        var result = new List<Quote>();
        foreach (var s in symbols.Select(Norm).Distinct())
        {
            try
            {
                var (secid, doc) = await ResolveAsync(s, ct).ConfigureAwait(false);
                doc ??= await GetJsonAsync($"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&fields={Fields}", ct).ConfigureAwait(false);
                using (doc) if (ParseQuote(doc.RootElement, s) is { } q) result.Add(q);
            }
            catch (DataSourceException ex) when (ex.Kind == DataErrorKind.NoData) { }
        }
        return result;
    }

    /// <summary>Prices are integers scaled by 10^f59; f170 (change %) is scaled by 100.</summary>
    internal static Quote? ParseQuote(JsonElement root, string symbol)
    {
        if (root.Prop("data") is not { } d) return null;
        var scale = Math.Pow(10, d.Num("f59") ?? 2);
        double? P(string f) => d.Num(f) is { } v && v != 0 ? v / scale : null;
        if (P("f43") is not { } price || price <= 0) return null;
        return new Quote
        {
            Symbol = symbol, Name = d.Str("f58"), Price = price, High = P("f44"), Low = P("f45"), Open = P("f46"), PreviousClose = P("f60"),
            Volume = d.Num("f47"), MarketCap = d.Num("f116"), Change = d.Num("f169") is { } c ? c / scale : null,
            ChangePercent = d.Num("f170") is { } pc ? pc / 100 : null, AsOf = d.UnixSeconds("f86"),
            MarketState = Eastern.SessionNow(), Source = "eastmoney", IsRealtime = false, DelayMinutes = null, // F1: delay not declared by the source; UI measures it from AsOf
        };
    }

    public bool Supports(BarInterval interval) => interval is BarInterval.Day1 or BarInterval.Week1 or BarInterval.Month1;

    public async Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
    {
        if (!Supports(r.Interval)) throw Unsupported($"interval {r.Interval}");
        var (secid, q) = await ResolveAsync(r.Symbol, ct).ConfigureAwait(false);
        q?.Dispose();
        var klt = r.Interval switch { BarInterval.Week1 => 102, BarInterval.Month1 => 103, _ => 101 };
        using var doc = await GetJsonAsync($"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={secid}&klt={klt}&fqt=1&lmt=600&end=20500101&fields1=f1,f2,f3&fields2=f51,f52,f53,f54,f55,f56,f57", ct).ConfigureAwait(false);
        var s = ParseKline(doc.RootElement, Norm(r.Symbol), r.Interval);
        var from = r.From ?? DateTimeOffset.MinValue;
        return s with { Bars = s.Bars.Where(b => b.Time >= from && b.Time <= (r.To ?? DateTimeOffset.MaxValue)).ToList() };
    }

    /// <summary>"date,open,close,high,low,volume,amount" strings.</summary>
    internal static BarSeries ParseKline(JsonElement root, string symbol, BarInterval interval)
    {
        var bars = root.Path("data", "klines").Items().Select(k => (k.Str() ?? "").Split(','))
            .Where(f => f.Length >= 6 && Eastern.Parse(f[0]) is not null && JsonX.ParseNumber(f[2]) is > 0)
            .Select(f => Bar.FromSource(Eastern.Parse(f[0])!.Value, JsonX.ParseNumber(f[1]), JsonX.ParseNumber(f[3]),
                JsonX.ParseNumber(f[4]), JsonX.ParseNumber(f[2])!.Value, JsonX.ParseNumber(f[5])))
            .ToList();
        return new BarSeries { Symbol = symbol, Interval = interval, Bars = bars, Source = "eastmoney", AsOf = bars.LastOrDefault()?.Time };
    }
}