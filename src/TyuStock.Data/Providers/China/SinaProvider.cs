using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Data.Http;
using TyuStock.Data.Json;

namespace TyuStock.Data.Providers.China;

/// <summary>新浪财经 US quotes (GBK, positional CSV, needs Referer) and full daily history. Reachable from mainland China without a proxy.</summary>
public sealed partial class SinaProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IQuoteProvider, IBarsProvider
{
    private static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string> { ["Referer"] = "https://finance.sina.com.cn/" };
    internal static readonly TimeSpan Beijing = TimeSpan.FromHours(8);

    public override string Id => "sina";
    public override string DisplayName => "新浪财经 Sina";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.DailyBars;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(300);
    protected override IReadOnlyDictionary<string, string>? DefaultHeaders => Headers;

    internal static string ToSina(string symbol) => Norm(symbol).ToLowerInvariant().Replace('.', '$').Replace('-', '$');

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        if (symbols.Count == 0) return [];
        var back = new Dictionary<string, string>();
        foreach (var s in symbols) back.TryAdd(ToSina(s), Norm(s));
        var text = await GetTextAsync($"https://hq.sinajs.cn/list={string.Join(",", back.Keys.Select(k => "gb_" + k))}", ct, Encoding.GetEncoding(936)).ConfigureAwait(false);
        return ParseQuotes(text, back);
    }

    [GeneratedRegex("var hq_str_gb_([^=]+)=\"([^\"]*)\"")]
    private static partial Regex QuoteLine();

    internal static IReadOnlyList<Quote> ParseQuotes(string text, IReadOnlyDictionary<string, string>? back = null)
    {
        var result = new List<Quote>();
        foreach (Match m in QuoteLine().Matches(text))
        {
            var f = m.Groups[2].Value.Split(',');
            if (f.Length < 27 || JsonX.ParseNumber(f[1]) is not { } price || price <= 0) continue;
            double? N(int i) => i < f.Length ? JsonX.ParseNumber(f[i]) : null;
            var state = Eastern.SessionNow();
            var ext = N(21) is > 0 && state != MarketState.Regular ? N(21) : null;
            result.Add(new Quote
            {
                Symbol = back is not null && back.TryGetValue(m.Groups[1].Value, out var o) ? o : m.Groups[1].Value.ToUpperInvariant().Replace('$', '.'),
                Name = f[0], Price = price, ChangePercent = N(2), Change = N(4), Open = N(5), High = N(6), Low = N(7),
                FiftyTwoWeekHigh = N(8), FiftyTwoWeekLow = N(9), Volume = N(10), MarketCap = N(12), PreviousClose = N(26),
                MarketState = state, ExtendedPrice = ext, ExtendedChangePercent = ext is null ? null : N(22), ExtendedChange = ext is null ? null : N(23),
                AsOf = DateTime.TryParseExact(f[3], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? new DateTimeOffset(t, Beijing) : null,
                Source = "sina", IsRealtime = false, DelayMinutes = null, // F1: delay not declared by the source; UI measures it from AsOf
            });
        }
        return result;
    }

    public bool Supports(BarInterval interval) => interval == BarInterval.Day1;

    public async Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
    {
        if (!Supports(r.Interval)) throw Unsupported($"interval {r.Interval}");
        var sym = ToSina(r.Symbol);
        var all = await Cache.GetOrAddAsync($"sina:day:{sym}", TimeSpan.FromMinutes(10), async c =>
        {
            var text = await GetTextAsync($"https://stock.finance.sina.com.cn/usstock/api/jsonp.php/var%20_{Enc(sym)}=/US_MinKService.getDailyK?symbol={Enc(sym)}&_=1", c).ConfigureAwait(false);
            return ParseDaily(text, Norm(r.Symbol));
        }, ct).ConfigureAwait(false);
        var from = r.From ?? DateTimeOffset.UtcNow.AddYears(-2);
        var bars = all.Bars.Where(b => b.Time >= from && b.Time <= (r.To ?? DateTimeOffset.MaxValue)).ToList();
        return bars.Count > 0 ? all with { Bars = bars } : throw NoData($"no bars for {r.Symbol}");
    }

    internal static BarSeries ParseDaily(string jsonp, string symbol)
    {
        var start = jsonp.IndexOf("([", StringComparison.Ordinal);
        var end = jsonp.LastIndexOf("])", StringComparison.Ordinal);
        if (start < 0 || end < start) throw new DataSourceException("sina", DataErrorKind.Parse, "unexpected daily-K payload");
        using var doc = JsonDocument.Parse(jsonp.AsMemory(start + 1, end - start));
        var bars = doc.RootElement.EnumerateArray()
            .Select(x => (t: Eastern.Parse(x.Str("d")), c: x.Num("c"), x))
            .Where(p => p.t is not null && p.c is > 0)
            .Select(p => Bar.FromSource(p.t!.Value, p.x.Num("o"), p.x.Num("h"), p.x.Num("l"), p.c!.Value, p.x.Num("v")))
            .ToList();
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Day1, Bars = bars, Source = "sina", AsOf = bars.LastOrDefault()?.Time };
    }
}