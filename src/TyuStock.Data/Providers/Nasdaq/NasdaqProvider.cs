using System.Globalization;
using System.Text.Json;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Data.Http;
using TyuStock.Data.Json;

namespace TyuStock.Data.Providers.Nasdaq;

/// <summary>
/// api.nasdaq.com (needs browser UA + nasdaq.com Origin/Referer). Real-time Nasdaq Last Sale quotes, intraday points, daily history,
/// option chain (no IV/Greeks), summary, financial statements and news. Values are display strings ("$1,234.50"), parsed leniently.
/// It intermittently answers data=null with bCode 3002 ("Error while calling vendor"), which is retried.
/// </summary>
public sealed class NasdaqProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache),
    IQuoteProvider, IBarsProvider, IOptionsProvider, IFundamentalsProvider, IFinancialsProvider, INewsProvider
{
    private const string Api = "https://api.nasdaq.com/api";
    private static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string>
    {
        ["Accept"] = "application/json, text/plain, */*", ["Origin"] = "https://www.nasdaq.com", ["Referer"] = "https://www.nasdaq.com/",
    };

    public override string Id => "nasdaq";
    public override string DisplayName => "Nasdaq";
    public override DataCapability Capabilities => DataCapability.Quote | DataCapability.IntradayBars | DataCapability.DailyBars |
        DataCapability.Options | DataCapability.Fundamentals | DataCapability.Financials | DataCapability.News;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(700);
    protected override IReadOnlyDictionary<string, string>? DefaultHeaders => Headers;

    internal static string ToNasdaq(string symbol) => Norm(symbol).Replace('-', '.');

    /// <summary>GET and return the "data" object; retries vendor errors; NoData when the symbol is unknown for this asset class.</summary>
    private async Task<JsonDocument> DataAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc.RootElement.Prop("data") is { ValueKind: JsonValueKind.Object }) return doc;
            var vendorError = doc.RootElement.Path("status", "bCodeMessage").Items().Any(m => m.Num("code") == 3002);
            doc.Dispose();
            if (!vendorError || attempt >= 2) throw NoData("data=null");
            await Task.Delay(800 * (attempt + 1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Nasdaq needs the asset class (stocks/etf/index); try stocks then etf and remember the answer.</summary>
    private async Task<(JsonDocument Doc, string AssetClass)> InfoAsync(string sym, CancellationToken ct)
    {
        var known = Cache.TryGet<string>($"nasdaq:ac:{sym}", out var ac) ? [ac] : new[] { "stocks", "etf" };
        DataSourceException? last = null;
        foreach (var a in known)
        {
            try
            {
                var doc = await DataAsync($"{Api}/quote/{Enc(sym)}/info?assetclass={a}", ct).ConfigureAwait(false);
                Cache.Set($"nasdaq:ac:{sym}", a, TimeSpan.FromDays(7));
                return (doc, a);
            }
            catch (DataSourceException ex) when (ex.Kind == DataErrorKind.NoData) { last = ex; }
        }
        throw last ?? NoData($"unknown symbol {sym}");
    }

    private async Task<string> AssetClassAsync(string sym, CancellationToken ct)
    {
        if (Cache.TryGet<string>($"nasdaq:ac:{sym}", out var ac)) return ac;
        var (doc, a) = await InfoAsync(sym, ct).ConfigureAwait(false);
        doc.Dispose();
        return a;
    }

    // ---------------- quotes ----------------

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        var result = new List<Quote>();
        DataSourceException? hard = null;
        foreach (var s in symbols.Select(Norm).Distinct())
        {
            try
            {
                var (doc, _) = await InfoAsync(ToNasdaq(s), ct).ConfigureAwait(false);
                using (doc) if (ParseInfo(doc.RootElement, s) is { } q) result.Add(q);
            }
            catch (DataSourceException ex) when (ex.Kind == DataErrorKind.NoData) { }
            catch (DataSourceException ex) { hard = ex; break; }
        }
        if (result.Count == 0 && hard is not null) throw hard;
        return result;
    }

    internal static Quote? ParseInfo(JsonElement root, string symbol)
    {
        if (root.Prop("data") is not { } d) return null;
        var status = d.Str("marketStatus") ?? "";
        var state = status switch
        {
            "Pre-Market" => MarketState.PreMarket, "Market Open" => MarketState.Regular, "After-Hours" => MarketState.AfterHours,
            "Closed" => MarketState.Closed, _ => MarketState.Unknown,
        };
        var primary = d.Prop("primaryData");
        var secondary = d.Prop("secondaryData");
        // In extended hours, primaryData is the pre/after-market trade and secondaryData the regular-session close.
        var extended = state is MarketState.PreMarket or MarketState.AfterHours && secondary is not null;
        var reg = extended ? secondary : primary;
        if (reg is not { } r || r.Num("lastSalePrice") is not { } price || price <= 0) return null;
        var (hi, lo) = Range(d.Path("keyStats", "fiftyTwoWeekHighLow", "value").Str(), " - ");
        var change = r.Num("netChange");
        return new Quote
        {
            Symbol = symbol, Name = CleanName(d.Str("companyName")), Exchange = d.Str("exchange"),
            Price = price, Change = change, ChangePercent = r.Num("percentageChange"),
            PreviousClose = change is { } c ? Math.Round(price - c, 4) : null,
            Volume = r.Num("volume"), Bid = primary?.Num("bidPrice"), Ask = primary?.Num("askPrice"),
            BidSize = primary?.Num("bidSize"), AskSize = primary?.Num("askSize"),
            FiftyTwoWeekHigh = hi, FiftyTwoWeekLow = lo, MarketState = state,
            ExtendedPrice = extended ? primary?.Num("lastSalePrice") : null,
            ExtendedChange = extended ? primary?.Num("netChange") : null,
            ExtendedChangePercent = extended ? primary?.Num("percentageChange") : null,
            // F6: only the source's own isRealTime flag counts; the session state says nothing about delay.
            AsOf = Eastern.ParseTimestamp(primary?.Str("lastTradeTimestamp")),
            Source = "nasdaq", IsRealtime = primary?.Bool("isRealTime") == true,
            DelayMinutes = primary?.Bool("isRealTime") == true ? 0 : null,
        };
    }

    internal static string? CleanName(string? n) => n?.Replace(" Common Stock", "").Replace(" Class A Common Stock", "").Trim();

    private static (double?, double?) Range(string? s, string sep)
    {
        if (s is null) return (null, null);
        var p = s.Split(sep, StringSplitOptions.TrimEntries);
        if (p.Length != 2) return (null, null);
        var a = JsonX.ParseNumber(p[0]); var b = JsonX.ParseNumber(p[1]);
        return a > b ? (a, b) : (b, a);
    }

    // ---------------- bars ----------------

    public bool Supports(BarInterval interval) => interval is BarInterval.Minute1 or BarInterval.Day1;

    public async Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
    {
        if (!Supports(r.Interval)) throw Unsupported($"interval {r.Interval}");
        var sym = ToNasdaq(r.Symbol);
        var ac = await AssetClassAsync(sym, ct).ConfigureAwait(false);
        if (r.Interval == BarInterval.Minute1)
        {
            using var doc = await DataAsync($"{Api}/quote/{Enc(sym)}/chart?assetclass={ac}", ct).ConfigureAwait(false);
            return ParseChart(doc.RootElement, Norm(r.Symbol));
        }
        var from = (r.From ?? DateTimeOffset.UtcNow.AddYears(-2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = (r.To ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var h = await DataAsync($"{Api}/quote/{Enc(sym)}/historical?assetclass={ac}&fromdate={from}&todate={to}&limit=9999", ct).ConfigureAwait(false);
        return ParseHistorical(h.RootElement, Norm(r.Symbol));
    }

    /// <summary>Intraday price points; "x" is New York wall-clock time encoded as a Unix epoch in milliseconds.</summary>
    internal static BarSeries ParseChart(JsonElement root, string symbol)
    {
        var bars = root.Path("data", "chart").Items()
            .Where(p => p.Num("x") is not null && p.Num("y") is > 0)
            .Select(p => Bar.CloseOnly(TyuStock.Core.Time.MarketClock.FromEastern(DateTime.UnixEpoch.AddMilliseconds(p.Num("x")!.Value)),
                p.Num("y")!.Value, 0))
            .ToList();
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Minute1, Bars = bars, Source = "nasdaq", AsOf = bars.LastOrDefault()?.Time, Adjustment = PriceAdjustment.Split }; // F7: split-adjusted history
    }

    internal static BarSeries ParseHistorical(JsonElement root, string symbol)
    {
        var bars = root.Path("data", "tradesTable", "rows").Items()
            .Select(x => (t: Eastern.Parse(x.Str("date")), c: x.Num("close"), x))
            .Where(p => p.t is not null && p.c is > 0)
            .Select(p => Bar.FromSource(p.t!.Value, p.x.Num("open"), p.x.Num("high"), p.x.Num("low"), p.c!.Value, p.x.Num("volume")))
            .OrderBy(b => b.Time).ToList();
        return new BarSeries { Symbol = symbol, Interval = BarInterval.Day1, Bars = bars, Source = "nasdaq", AsOf = bars.LastOrDefault()?.Time, Adjustment = PriceAdjustment.Split }; // F7: split-adjusted history
    }

    // ---------------- options ----------------

    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) =>
        (await GetFullChainAsync(symbol, ct).ConfigureAwait(false)).Expirations;

    public async Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        var full = await GetFullChainAsync(symbol, ct).ConfigureAwait(false);
        var exp = expiration ?? full.Expirations.FirstOrDefault();
        var list = full.Contracts.Where(c => c.Expiration == exp).ToList();
        if (list.Count == 0) throw NoData($"no contracts for {symbol} {exp}");
        return full with { Contracts = list, Expiration = exp };
    }

    private Task<OptionChain> GetFullChainAsync(string symbol, CancellationToken ct)
    {
        var sym = ToNasdaq(symbol);
        return Cache.GetOrAddAsync($"nasdaq:opt:{sym}", TimeSpan.FromSeconds(90), async c =>
        {
            var ac = await AssetClassAsync(sym, c).ConfigureAwait(false);
            using var doc = await DataAsync($"{Api}/quote/{Enc(sym)}/option-chain?assetclass={ac}&limit=5000&fromdate=all&todate=undefined&excode=oprac&callput=callput&money=all&type=all", c).ConfigureAwait(false);
            var chain = ParseOptionChain(doc.RootElement, Norm(symbol), Eastern.Today);
            return chain.Contracts.Count > 0 ? chain : throw NoData($"no options for {symbol}");
        }, ct);
    }

    internal static OptionChain ParseOptionChain(JsonElement root, string underlying, DateOnly today)
    {
        var list = new List<OptionContract>();
        DateOnly? group = null;
        foreach (var row in root.Path("data", "table", "rows").Items())
        {
            if (row.Str("expirygroup") is { } g)
            {
                group = DateTime.TryParse(g, CultureInfo.InvariantCulture, DateTimeStyles.None, out var gd) ? DateOnly.FromDateTime(gd) : null;
                continue;
            }
            if (group is not { } exp || exp < today || row.Num("strike") is not { } k) continue;
            var occ = row.Str("drillDownURL")?.Split('/').LastOrDefault()?.Replace("--", "").ToUpperInvariant();
            var root2 = OccSymbol.TryParse(occ, out var r0, out _, out _, out _) ? r0 : underlying;
            foreach (var (pfx, right) in new[] { ("c_", OptionRight.Call), ("p_", OptionRight.Put) })
            {
                double? N(string f) => row.Num(pfx + f);
                list.Add(new OptionContract
                {
                    ContractSymbol = OccSymbol.Format(root2, exp, right, k), Underlying = underlying, Right = right, Strike = k, Expiration = exp,
                    Last = N("Last"), Change = N("Change"), Bid = N("Bid") is > 0 ? N("Bid") : null, Ask = N("Ask") is > 0 ? N("Ask") : null,
                    Volume = N("Volume"), OpenInterest = N("Openinterest"), InTheMoney = row.Bool(pfx + "colour"),
                });
            }
        }
        var spot = JsonX.ParseNumber(System.Text.RegularExpressions.Regex.Match(root.Path("data", "lastTrade").Str() ?? "", @"\$([\d,\.]+)").Groups[1].Value);
        return new OptionChain
        {
            Underlying = underlying, UnderlyingPrice = spot, Contracts = list, Expirations = list.Select(c => c.Expiration).Distinct().Order().ToList(),
            // F4: the chain carries no snapshot time; download time is FetchedAt (stamped by the router), not AsOf.
            Source = "nasdaq", DelayMinutes = 15,
        };
    }

    // ---------------- fundamentals / financials / news ----------------

    public async Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default)
    {
        var sym = ToNasdaq(symbol);
        return await Cache.GetOrAddAsync($"nasdaq:fund:{sym}", TimeSpan.FromMinutes(30), async c =>
        {
            var ac = await AssetClassAsync(sym, c).ConfigureAwait(false);
            using var doc = await DataAsync($"{Api}/quote/{Enc(sym)}/summary?assetclass={ac}", c).ConfigureAwait(false);
            return ParseSummary(doc.RootElement, Norm(symbol), ac);
        }, ct).ConfigureAwait(false);
    }

    internal static Fundamentals ParseSummary(JsonElement root, string symbol, string assetClass = "stocks")
    {
        var s = root.Path("data", "summaryData");
        string? V(string k) => s?.Path(k, "value").Str();
        double? N(string k) => JsonX.ParseNumber(V(k));
        var (hi, lo) = Range(V("FiftTwoWeekHighLow"), "/");
        return new Fundamentals
        {
            Symbol = symbol, QuoteType = assetClass == "etf" ? "ETF" : "EQUITY",
            Exchange = V("Exchange"), Sector = V("Sector"), Industry = V("Industry"),
            MarketCap = N("MarketCap"), TrailingPE = N("PERatio"), ForwardPE = N("ForwardPE1Yr"), Eps = N("EarningsPerShare"),
            DividendRate = N("AnnualizedDividend"), DividendYield = N("Yield") is { } y ? y / 100 : null,
            AverageVolume = N("AverageVolume"), TargetPrice = N("OneYrTarget"), Beta = N("Beta"),
            FiftyTwoWeekHigh = hi, FiftyTwoWeekLow = lo, ExDividendDate = Eastern.Date(V("ExDividendDate")),
            Source = "nasdaq",
        };
    }

    private static readonly (string Table, string Label, FinancialStatementKind Kind, string Item)[] FinMap =
    [
        ("incomeStatementTable", "Total Revenue", FinancialStatementKind.Income, FinancialItems.TotalRevenue),
        ("incomeStatementTable", "Gross Profit", FinancialStatementKind.Income, FinancialItems.GrossProfit),
        ("incomeStatementTable", "Operating Income", FinancialStatementKind.Income, FinancialItems.OperatingIncome),
        ("incomeStatementTable", "Net Income", FinancialStatementKind.Income, FinancialItems.NetIncome),
        ("balanceSheetTable", "Total Assets", FinancialStatementKind.Balance, FinancialItems.TotalAssets),
        ("balanceSheetTable", "Total Liabilities", FinancialStatementKind.Balance, FinancialItems.TotalLiabilities),
        ("balanceSheetTable", "Total Equity", FinancialStatementKind.Balance, FinancialItems.StockholdersEquity),
        ("cashFlowTable", "Net Cash Flow-Operating", FinancialStatementKind.CashFlow, FinancialItems.OperatingCashFlow),
        ("cashFlowTable", "Capital Expenditures", FinancialStatementKind.CashFlow, FinancialItems.CapitalExpenditure),
    ];

    public async Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default)
    {
        var sym = ToNasdaq(symbol);
        return await Cache.GetOrAddAsync($"nasdaq:fin:{sym}", TimeSpan.FromHours(6), async c =>
        {
            using var doc = await DataAsync($"{Api}/company/{Enc(sym)}/financials?frequency=1", c).ConfigureAwait(false);
            var fs = ParseFinancials(doc.RootElement, Norm(symbol));
            return fs.Lines.Count > 0 ? fs : throw NoData($"no financials for {symbol}");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Annual statements; Nasdaq reports values in thousands of USD.</summary>
    internal static FinancialStatements ParseFinancials(JsonElement root, string symbol)
    {
        var d = root.Prop("data");
        var lines = new List<FinancialLine>();
        foreach (var (table, label, kind, item) in FinMap)
        {
            var t = d?.Prop(table);
            var headers = t?.Prop("headers");
            var row = t?.Prop("rows").Items().FirstOrDefault(r => r.Str("value1") == label);
            if (headers is null || row is not { ValueKind: JsonValueKind.Object } rr) continue;
            for (var i = 2; i <= 6; i++)
            {
                if (Eastern.Date(headers.Value.Str($"value{i}")) is not { } end || rr.Num($"value{i}") is not { } v) continue;
                lines.Add(new FinancialLine(kind, item, end, v * 1000));
            }
        }
        foreach (var g in lines.Where(l => l.Item == FinancialItems.OperatingCashFlow).ToList())
            if (lines.FirstOrDefault(l => l.Item == FinancialItems.CapitalExpenditure && l.PeriodEnd == g.PeriodEnd) is { } capex)
                lines.Add(new FinancialLine(FinancialStatementKind.CashFlow, FinancialItems.FreeCashFlow, g.PeriodEnd, g.Value + capex.Value));
        return new FinancialStatements { Symbol = symbol, Lines = lines, Source = "nasdaq" };
    }

    public async Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default)
    {
        var sym = ToNasdaq(symbol).ToLowerInvariant();
        using var doc = await DataAsync($"{Api}/news/topic/articlebysymbol?q={Enc(sym)}|stocks&offset=0&limit={Math.Clamp(count, 1, 50)}&fallback=true", ct).ConfigureAwait(false);
        var items = ParseNews(doc.RootElement);
        return items.Count > 0 ? items : throw NoData($"no news for {symbol}");
    }

    internal static IReadOnlyList<NewsItem> ParseNews(JsonElement root) =>
        root.Path("data", "rows").Items().Where(r => r.Str("title") is not null && r.Str("url") is not null).Select(r => new NewsItem
        {
            Title = System.Net.WebUtility.HtmlDecode(r.Str("title")!),
            Url = r.Str("url")!.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? r.Str("url")! : "https://www.nasdaq.com" + r.Str("url"),
            Publisher = r.Str("publisher"), Published = Eastern.Parse(r.Str("created")),
            Summary = r.Str("description"), RelatedSymbols = r.Str("primarysymbol") is { } p ? [p.ToUpperInvariant()] : [],
            Source = "nasdaq",
        }).ToList();
}