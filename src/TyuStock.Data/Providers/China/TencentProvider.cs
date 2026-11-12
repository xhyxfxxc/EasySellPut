using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Data.Http;
using TyuStock.Data.Json;

namespace TyuStock.Data.Providers.China;

/// <summary>腾讯财经: batch quotes ("~"-separated, GBK), forward-adjusted day/week/month K and today's minute line. Reachable from China directly.</summary>
public sealed partial class TencentProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IQuoteProvider, IBarsProvider
{
    public override string Id => "tencent";
    public override string DisplayName => "腾讯财经 Tencent";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.IntradayBars | DataCapability.DailyBars;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(300);

    internal static string ToTencent(string symbol) => "us" + Norm(symbol).Replace('-', '.');

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        if (symbols.Count == 0) return [];
        var back = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symbols) back.TryAdd(ToTencent(s), Norm(s));
        var text = await GetTextAsync($"https://qt.gtimg.cn/q={string.Join(",", back.Keys)}", ct, Encoding.GetEncoding(936)).ConfigureAwait(false);
        var quotes = ParseQuotes(text, back, out var codes);
        foreach (var (sym, code) in codes) Cache.Set($"tencent:code:{sym}", code, TimeSpan.FromDays(7));
        return quotes;
    }

    [GeneratedRegex("v_(us[^=]+)=\"([^\"]*)\"")]
    private static partial Regex QuoteLine();

    /// <param name="codes">Symbol → full exchange-suffixed code (e.g. AAPL → usAAPL.OQ) needed by the K-line endpoints.</param>
    internal static IReadOnlyList<Quote> ParseQuotes(string text, IReadOnlyDictionary<string, string>? back, out Dictionary<string, string> codes)
    {
        codes = [];
        var result = new List<Quote>();
        foreach (Match m in QuoteLine().Matches(text))
        {
            var f = m.Groups[2].Value.Split('~');
            if (f.Length < 50 || JsonX.ParseNumber(f[3]) is not { } price || price <= 0) continue;
            double? N(int i) => i < f.Length ? JsonX.ParseNumber(f[i]) : null;
            var sym = back is not null && back.TryGetValue(m.Groups[1].Value, out var o) ? o : m.Groups[1].Value[2..].ToUpperInvariant();
            if (f[2].Contains('.')) codes[sym] = "us" + f[2];
            result.Add(new Quote
            {
                Symbol = sym, Name = string.IsNullOrWhiteSpace(f[46]) ? f[1] : f[46], Currency = string.IsNullOrEmpty(f[35]) ? "USD" : f[35],
                Price = price, PreviousClose = N(4), Open = N(5), Volume = N(6), Bid = N(9) is > 0 ? N(9) : null, BidSize = N(10),
                Ask = N(19) is > 0 ? N(19) : null, AskSize = N(20), Change = N(31), ChangePercent = N(32), High = N(33), Low = N(34),
                MarketCap = N(45) is { } mc ? mc * 1e8 : null, FiftyTwoWeekHigh = N(48), FiftyTwoWeekLow = N(49),
                MarketState = Eastern.SessionNow(), AsOf = Eastern.Parse(f[30]),
                Source = "tencent", IsRealtime = false, DelayMinutes = 0,
            });
        }
        return result;
    }

    private async Task<string> CodeAsync(string symbol, CancellationToken ct)
    {
        var sym = Norm(symbol);
        if (Cache.TryGet<string>($"tencent:code:{sym}", out var code)) return code;
        await GetQuotesAsync([sym], ct).ConfigureAwait(false);
        return Cache.TryGet<string>($"tencent:code:{sym}", out var c2) ? c2 : throw NoData($"unknown symbol {symbol}");
    }

    public bool Supports(BarInterval interval) => interval is BarInterval.Minute1 or BarInterval.Day1 or BarInterval.Week1 or BarInterval.Month1;

    public async Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
    {
        if (!Supports(r.Interval)) throw Unsupported($"interval {r.Interval}");
        var code = await CodeAsync(r.Symbol, ct).ConfigureAwait(false);
        if (r.Interval == BarInterval.Minute1)
        {
            using var m = await GetJsonAsync($"https://web.ifzq.gtimg.cn/appstock/app/UsMinute/query?code={Enc(code)}", ct).ConfigureAwait(false);
            return ParseMinute(m.RootElement, code, Norm(r.Symbol));
        }
        var period = r.Interval switch { BarInterval.Week1 => "week", BarInterval.Month1 => "month", _ => "day" };
        using var doc = await GetJsonAsync($"https://web.ifzq.gtimg.cn/appstock/app/usfqkline/get?param={Enc(code)},{period},,,640,qfq", ct).ConfigureAwait(false);
        var s = ParseKline(doc.RootElement, code, Norm(r.Symbol), r.Interval);
        var from = r.From ?? DateTimeOffset.MinValue;
        return s with { Bars = s.Bars.Where(b => b.Time >= from && b.Time <= (r.To ?? DateTimeOffset.MaxValue)).ToList() };
    }

    /// <summary>Rows are [date, open, close, high, low, volume] (note the order).</summary>
    internal static BarSeries ParseKline(JsonElement root, string code, string symbol, BarInterval interval)
    {
        var node = root.Path("data", code);
        var key = interval switch { BarInterval.Week1 => "week", BarInterval.Month1 => "month", _ => "day" };
        var rows = node?.Prop("qfq" + key) ?? node?.Prop(key);
        var bars = rows.Items().Select(x => x.EnumerateArray().Select(v => v.Str()).ToArray())
            .Where(a => a.Length >= 6 && Eastern.Parse(a[0]) is not null && JsonX.ParseNumber(a[2]) is > 0)
            .Select(a => new Bar(Eastern.Parse(a[0])!.Value, JsonX.ParseNumber(a[1])!.Value, JsonX.ParseNumber(a[3])!.Value,
                JsonX.ParseNumber(a[4])!.Value, JsonX.ParseNumber(a[2])!.Value, JsonX.ParseNumber(a[5]) ?? 0))
            .ToList();
        return new BarSeries { Symbol = symbol, Interval = interval, Bars = bars, Source = "tencent", AsOf = bars.LastOrDefault()?.Time };
    }

    /// <summary>"HHmm price cumulativeVolume" points for the latest session.</summary>
    internal static BarSeries ParseMinute(JsonElement root, string code, string symbol)
    {
        var d = root.Path("data", code, "data");
        var day = DateTime.TryParseExact(d?.Str("date"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd) ? dd : DateTime.Today;
        var bars = new List<Bar>();
        double prevVol = 0;
        foreach (var p in d.Items("data"))
        {
            var f = (p.Str() ?? "").Split(' ');
            if (f.Length < 3 || f[0].Length != 4 || JsonX.ParseNumber(f[1]) is not { } px || px <= 0) continue;
            var t = day.AddHours(int.Parse(f[0][..2], CultureInfo.InvariantCulture)).AddMinutes(int.Parse(f[0][2..], CultureInfo.InvariantCulture));
            var cum = JsonX.ParseNumber(f[2]) ?? prevVol;
            bars.Add(new Bar(TyuStock.Core.Time.MarketClock.FromEastern(t), px, px, px, px, Math.Max(0, cum - prevVol)));
            prevVol = cum;
        }
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Minute1, Bars = bars, Source = "tencent", AsOf = bars.LastOrDefault()?.Time };
    }
}