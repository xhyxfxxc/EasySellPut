using System.Text.Json;
using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Data.Http;
using Meili.Data.Json;

namespace Meili.Data.Providers.Cnbc;

/// <summary>CNBC quote web service: real-time batch quotes with extended-hours prices and a compact fundamentals block.</summary>
public sealed class CnbcProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IQuoteProvider, IFundamentalsProvider
{
    private const string Url = "https://quote.cnbc.com/quote-html-webservice/restQuote/symbolType/symbol?symbols={0}&requestMethod=itv&noform=1&partnerId=2&fund=1&exthrs=1&output=json&events=1";

    public override string Id => "cnbc";
    public override string DisplayName => "CNBC";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.Fundamentals;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(250);

    internal static string ToCnbc(string symbol) => Norm(symbol) switch
    {
        "^GSPC" or "^SPX" => ".SPX", "^DJI" => ".DJI", "^IXIC" => ".IXIC", "^NDX" => ".NDX", "^VIX" => ".VIX", "^RUT" => ".RUT",
        var s => s.Replace('-', '.'),
    };

    private async Task<IReadOnlyList<JsonElement>> FetchAsync(IReadOnlyList<string> symbols, CancellationToken ct, Dictionary<string, string> back)
    {
        foreach (var s in symbols) back.TryAdd(ToCnbc(s), Norm(s));
        using var doc = await GetJsonAsync(string.Format(Url, Enc(string.Join("|", back.Keys))), ct).ConfigureAwait(false);
        return Rows(doc.RootElement).Select(e => e.Clone()).ToList();
    }

    internal static IEnumerable<JsonElement> Rows(JsonElement root)
    {
        var fq = root.Path("FormattedQuoteResult", "FormattedQuote");
        if (fq is { ValueKind: JsonValueKind.Object } single) return [single];
        return fq.Items().Where(q => (q.Num("code") ?? 0) == 0 && q.Str("symbol") is not null);
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        if (symbols.Count == 0) return [];
        var back = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = await FetchAsync(symbols, ct, back).ConfigureAwait(false);
        return rows.Select(r => ParseQuote(r, back)).OfType<Quote>().ToList();
    }

    internal static Quote? ParseQuote(JsonElement q, IReadOnlyDictionary<string, string>? back = null)
    {
        if (q.Num("last") is not { } price || price <= 0 || q.Str("symbol") is not { } sym) return null;
        var state = (q.Str("curmktstatus") ?? "") switch
        {
            "PRE_MKT" => MarketState.PreMarket, "REG_MKT" => MarketState.Regular, "POST_MKT" => MarketState.AfterHours, _ => MarketState.Closed,
        };
        var ext = q.Prop("ExtendedMktQuote");
        var realtime = q.Str("realTime") == "true";
        return new Quote
        {
            Symbol = back is not null && back.TryGetValue(sym, out var o) ? o : sym,
            Name = q.Str("name"), Exchange = q.Str("exchange"), Currency = q.Str("currencyCode") ?? "USD",
            Price = price, Change = q.Num("change"), ChangePercent = q.Num("change_pct"), PreviousClose = q.Num("previous_day_closing"),
            Open = q.Num("open"), High = q.Num("high"), Low = q.Num("low"), Volume = q.Num("volume"),
            Bid = q.Num("bid") is > 0 ? q.Num("bid") : null, Ask = q.Num("ask") is > 0 ? q.Num("ask") : null,
            FiftyTwoWeekHigh = q.Num("yrhiprice"), FiftyTwoWeekLow = q.Num("yrloprice"), MarketCap = q.Num("mktcapView"),
            MarketState = state,
            ExtendedPrice = ext?.Num("last"), ExtendedChange = ext?.Num("change"), ExtendedChangePercent = ext?.Num("change_pct"),
            ExtendedTime = Eastern.ParseWithOffset(ext?.Str("last_time")),
            AsOf = Eastern.ParseWithOffset(q.Str("last_time")),
            Source = "cnbc", IsRealtime = realtime, DelayMinutes = realtime ? 0 : null, // F6: "realTime=false" does not state how late
        };
    }

    public async Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default)
    {
        var sym = ToCnbc(symbol);
        return await Cache.GetOrAddAsync($"cnbc:fund:{sym}", TimeSpan.FromMinutes(30), async c =>
        {
            var rows = await FetchAsync([symbol], c, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
            return rows.Count > 0 ? ParseFundamentals(rows[0], Norm(symbol)) : throw NoData($"unknown symbol {symbol}");
        }, ct).ConfigureAwait(false);
    }

    internal static Fundamentals ParseFundamentals(JsonElement q, string symbol) => new()
    {
        Symbol = symbol, Name = q.Str("name"), Exchange = q.Str("exchange"), QuoteType = q.Str("type"),
        MarketCap = q.Num("mktcapView"), SharesOutstanding = q.Num("sharesout"), TrailingPE = q.Num("pe"), ForwardPE = q.Num("fpe"),
        Eps = q.Num("eps"), Beta = q.Num("beta"), DividendRate = q.Num("dividend"),
        DividendYield = q.Num("dividendyield") is { } y ? y / 100 : null,
        FiftyTwoWeekHigh = q.Num("yrhiprice"), FiftyTwoWeekLow = q.Num("yrloprice"), AverageVolume = q.Num("tendayavgvol"),
        ProfitMargin = q.Num("NETPROFTTM") is { } m ? m / 100 : null, Revenue = q.Num("revenuettm"),
        Country = q.Str("countryCode"), Source = "cnbc",
    };
}