using System.Globalization;
using System.Text.Json;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Time;
using TyuStock.Data.Http;
using TyuStock.Data.Json;

namespace TyuStock.Data.Providers.Cboe;

/// <summary>
/// CBOE delayed-quotes CDN (no account, 15-minute delay). Its option file contains the WHOLE chain with IV and all Greeks,
/// so it is the primary options source; it also serves quotes (with IV30), today's 1-minute bars and full daily history.
/// </summary>
public sealed class CboeProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IQuoteProvider, IBarsProvider, IOptionsProvider
{
    private const string Base = "https://cdn.cboe.com/api/global/delayed_quotes";
    private static readonly HashSet<string> Indices = ["SPX", "NDX", "VIX", "RUT", "XSP", "DJX", "OEX", "MXEF", "MXEA"];

    public override string Id => "cboe";
    public override string DisplayName => "CBOE";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.IntradayBars | DataCapability.DailyBars | DataCapability.Options;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(250);

    /// <summary>Indices use a leading underscore: SPX/^SPX/^GSPC → _SPX.</summary>
    internal static string ToCboe(string symbol)
    {
        var s = Norm(symbol).TrimStart('^').Replace('-', '.');
        if (s == "GSPC") s = "SPX";
        return Indices.Contains(s) ? "_" + s : s;
    }

    // ---------------- quotes ----------------

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        var tasks = symbols.Distinct(StringComparer.OrdinalIgnoreCase).Select(async s =>
        {
            try
            {
                using var doc = await GetJsonAsync($"{Base}/quotes/{Enc(ToCboe(s))}.json", ct).ConfigureAwait(false);
                return ParseQuote(doc.RootElement, Norm(s));
            }
            catch (DataSourceException ex) when (ex.StatusCode is 403 or 404) { return null; } // unknown symbol (CDN answers 403)
        });
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<Quote>().ToList();
    }

    internal static Quote? ParseQuote(JsonElement root, string symbol)
    {
        if (root.Prop("data") is not { } d || d.Num("current_price") is not { } price || price <= 0) return null;
        return new Quote
        {
            Symbol = symbol, Price = price, Exchange = "CBOE",
            Change = d.Num("price_change"), ChangePercent = d.Num("price_change_percent"),
            PreviousClose = d.Num("prev_day_close"), Open = d.Num("open"), High = d.Num("high"), Low = d.Num("low"),
            Volume = d.Num("volume"), Bid = Pos(d.Num("bid")), Ask = Pos(d.Num("ask")), BidSize = d.Num("bid_size"), AskSize = d.Num("ask_size"),
            MarketState = Eastern.SessionNow(),
            ImpliedVolatility30 = ParseIv30(root),
            AsOf = Eastern.Parse(d.Str("last_trade_time")) ?? Utc(root.Str("timestamp")),
            Source = "cboe", IsRealtime = false, DelayMinutes = 15,
        };
    }

    /// <summary>CBOE's 30-day implied volatility index for the underlying, as a fraction.</summary>
    internal static double? ParseIv30(JsonElement root) => root.Path("data", "iv30").Num() is { } v and > 0 ? v / 100 : null;

    // ---------------- bars ----------------

    public bool Supports(BarInterval interval) => interval is BarInterval.Minute1 or BarInterval.Day1;

    public async Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
    {
        if (!Supports(r.Interval)) throw Unsupported($"interval {r.Interval}");
        var sym = ToCboe(r.Symbol);
        var kind = r.Interval == BarInterval.Day1 ? "historical" : "intraday";
        var series = await Cache.GetOrAddAsync($"cboe:bars:{kind}:{sym}", TimeSpan.FromSeconds(r.Interval == BarInterval.Day1 ? 600 : 60), async c =>
        {
            using var doc = await GetJsonAsync($"{Base}/charts/{kind}/{Enc(sym)}.json", c, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return r.Interval == BarInterval.Day1 ? ParseHistorical(doc.RootElement, Norm(r.Symbol)) : ParseIntraday(doc.RootElement, Norm(r.Symbol));
        }, ct).ConfigureAwait(false);
        var from = r.From ?? (r.Interval == BarInterval.Day1 ? DateTimeOffset.UtcNow.AddYears(-2) : DateTimeOffset.MinValue);
        var to = r.To ?? DateTimeOffset.MaxValue;
        var bars = series.Bars.Where(b => b.Time >= from && b.Time <= to).ToList();
        if (bars.Count == 0) throw NoData($"no bars for {r.Symbol}");
        return series with { Bars = bars };
    }

    internal static BarSeries ParseHistorical(JsonElement root, string symbol)
    {
        var bars = root.Prop("data").Items()
            .Select(x => (t: Eastern.Parse(x.Str("date")), x))
            .Where(p => p.t is not null && p.x.Num("close") is > 0)
            .Select(p => Bar.FromSource(p.t!.Value, p.x.Num("open"), p.x.Num("high"), p.x.Num("low"), p.x.Num("close")!.Value, p.x.Num("volume")))
            .OrderBy(b => b.Time).ToList();
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Day1, Bars = bars, Source = "cboe", AsOf = Utc(root.Str("timestamp")), DelayMinutes = 15 };
    }

    internal static BarSeries ParseIntraday(JsonElement root, string symbol)
    {
        var bars = new List<Bar>();
        foreach (var x in root.Prop("data").Items())
        {
            if (Eastern.Parse(x.Str("datetime")) is not { } t || x.Prop("price") is not { } p || p.Num("close") is not > 0) continue;
            var close = p.Num("close")!.Value;
            // CBOE stamps the bar END (09:31 = 09:30–09:31); normalize to bar start like the other sources.
            bars.Add(Bar.FromSource(t.AddMinutes(-1), p.Num("open"), p.Num("high"), p.Num("low"), close,
                x.Path("volume", "stock_volume").Num()));
        }
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Minute1, Bars = bars, Source = "cboe", AsOf = Utc(root.Str("timestamp")), DelayMinutes = 15, Adjustment = PriceAdjustment.Split };
    }

    // ---------------- options: one request returns the whole chain ----------------

    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) =>
        (await GetFullChainAsync(symbol, ct).ConfigureAwait(false)).Expirations;

    public async Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        var full = await GetFullChainAsync(symbol, ct).ConfigureAwait(false);
        var exp = expiration ?? full.Expirations.FirstOrDefault();
        var contracts = full.Contracts.Where(c => c.Expiration == exp).ToList();
        if (contracts.Count == 0) throw NoData($"no contracts for {symbol} {exp}");
        return full with { Contracts = contracts, Expiration = exp };
    }

    private Task<OptionChain> GetFullChainAsync(string symbol, CancellationToken ct)
    {
        var sym = ToCboe(symbol);
        return Cache.GetOrAddAsync($"cboe:opt:{sym}", RefreshCadence.OptionChainCache, async c =>
        {
            var payload = await GetAsync($"{Base}/options/{Enc(sym)}.json", c, timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(false);
            // Large (AAPL ~1.5 MB, SPX ~14 MB): parse off the UI thread.
            var chain = await Task.Run(() => { using var doc = ParseJson(payload.Body); return ParseOptions(doc.RootElement, Norm(symbol), Eastern.Today); }, c).ConfigureAwait(false);
            if (chain.Contracts.Count == 0) throw NoData($"no options for {symbol}");
            return chain;
        }, ct);
    }

    internal static OptionChain ParseOptions(JsonElement root, string underlying, DateOnly today)
    {
        var d = root.Prop("data");
        var list = new List<OptionContract>();
        foreach (var o in d.Items("options"))
        {
            if (!OccSymbol.TryParse(o.Str("option"), out _, out var exp, out var right, out var strike) || exp < today) continue;
            var iv = o.Num("iv");
            var hasIv = iv is > 0.0001 and < 20;
            list.Add(new OptionContract
            {
                ContractSymbol = o.Str("option")!, Underlying = underlying, Right = right, Strike = strike, Expiration = exp,
                Bid = Pos(o.Num("bid")), Ask = Pos(o.Num("ask")), BidSize = o.Num("bid_size"), AskSize = o.Num("ask_size"),
                Last = Pos(o.Num("last_trade_price")), Change = o.Num("change"), ChangePercent = o.Num("percent_change"),
                Volume = o.Num("volume"), OpenInterest = o.Num("open_interest"),
                ImpliedVolatility = hasIv ? iv : null,
                Delta = hasIv ? o.Num("delta") : null, Gamma = hasIv ? o.Num("gamma") : null, Theta = hasIv ? o.Num("theta") : null,
                Vega = hasIv ? o.Num("vega") : null, Rho = hasIv ? o.Num("rho") : null,
                TheoreticalPrice = Pos(o.Num("theo")), LastTradeTime = Eastern.Parse(o.Str("last_trade_time")),
            });
        }
        var spot = d?.Num("current_price");
        if (spot is > 0)
            for (var i = 0; i < list.Count; i++)
                list[i] = list[i] with { InTheMoney = list[i].Right == OptionRight.Call ? spot > list[i].Strike : spot < list[i].Strike };
        return new OptionChain
        {
            Underlying = underlying, UnderlyingPrice = spot, Contracts = list,
            Expirations = list.Select(c => c.Expiration).Distinct().Order().ToList(),
            Source = "cboe", DelayMinutes = 15, AsOf = Utc(root.Str("timestamp")),
        };
    }

    private static double? Pos(double? v) => v is > 0 ? v : null;

    private static DateTimeOffset? Utc(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero) : null;
}