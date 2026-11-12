using System.Text.Json;
using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Data.Http;
using Meili.Data.Json;

namespace Meili.Data.Providers.Sec;

/// <summary>
/// SEC EDGAR (official, free, no key): ticker→CIK table (also an offline-capable symbol search) and XBRL company facts
/// for annual financial statements. SEC requires a descriptive User-Agent and allows at most 10 requests/second.
/// </summary>
public sealed class SecProvider(HttpGateway http, ResponseCache cache) : ProviderBase(http, cache), IFinancialsProvider, ISymbolSearchProvider
{
    public const string UserAgent = "MeiliTerminal/0.3 (personal, non-commercial) contact@example.com";
    private static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string> { ["User-Agent"] = UserAgent };

    public override string Id => "sec";
    public override string DisplayName => "SEC EDGAR";
    public override DataCapability Capabilities => DataCapability.Financials | DataCapability.Search;
    public override TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(150);
    protected override IReadOnlyDictionary<string, string>? DefaultHeaders => Headers;

    public sealed record TickerEntry(string Ticker, long Cik, string Title);

    private Task<IReadOnlyList<TickerEntry>> TickersAsync(CancellationToken ct) =>
        Cache.GetOrAddAsync("sec:tickers", TimeSpan.FromHours(24), async c =>
        {
            using var doc = await GetJsonAsync("https://www.sec.gov/files/company_tickers.json", c, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return ParseTickers(doc.RootElement);
        }, ct);

    internal static IReadOnlyList<TickerEntry> ParseTickers(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object ? [] : root.EnumerateObject()
            .Select(p => (t: p.Value.Str("ticker"), c: p.Value.Num("cik_str"), n: p.Value.Str("title")))
            .Where(x => x.t is not null && x.c is > 0)
            .Select(x => new TickerEntry(x.t!.ToUpperInvariant(), (long)x.c!.Value, x.n ?? x.t!)).ToList();

    public async Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) =>
        Search(await TickersAsync(ct).ConfigureAwait(false), query, limit);

    /// <summary>Exact ticker, then ticker prefix, then company-name match.</summary>
    internal static IReadOnlyList<SymbolMatch> Search(IReadOnlyList<TickerEntry> all, string query, int limit)
    {
        var q = query.Trim().ToUpperInvariant().Replace('.', '-');
        if (q.Length == 0) return [];
        return all.Select(e => (e, rank: e.Ticker == q ? 0 : e.Ticker.StartsWith(q, StringComparison.Ordinal) ? 1
                    : e.Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 2 : 9))
            .Where(x => x.rank < 9).OrderBy(x => x.rank).ThenBy(x => x.e.Ticker.Length).Take(limit)
            .Select(x => new SymbolMatch(x.e.Ticker.Replace('-', '.'), x.e.Title, null, "EQUITY", "sec")).ToList();
    }

    // ---------------- financials ----------------

    private static readonly (string Concept, FinancialStatementKind Kind, string Item, bool Duration, int Sign)[] Concepts =
    [
        ("RevenueFromContractWithCustomerExcludingAssessedTax", FinancialStatementKind.Income, FinancialItems.TotalRevenue, true, 1),
        ("Revenues", FinancialStatementKind.Income, FinancialItems.TotalRevenue, true, 1),
        ("GrossProfit", FinancialStatementKind.Income, FinancialItems.GrossProfit, true, 1),
        ("OperatingIncomeLoss", FinancialStatementKind.Income, FinancialItems.OperatingIncome, true, 1),
        ("NetIncomeLoss", FinancialStatementKind.Income, FinancialItems.NetIncome, true, 1),
        ("EarningsPerShareDiluted", FinancialStatementKind.Income, FinancialItems.DilutedEps, true, 1),
        ("Assets", FinancialStatementKind.Balance, FinancialItems.TotalAssets, false, 1),
        ("Liabilities", FinancialStatementKind.Balance, FinancialItems.TotalLiabilities, false, 1),
        ("StockholdersEquity", FinancialStatementKind.Balance, FinancialItems.StockholdersEquity, false, 1),
        ("NetCashProvidedByUsedInOperatingActivities", FinancialStatementKind.CashFlow, FinancialItems.OperatingCashFlow, true, 1),
        ("PaymentsToAcquirePropertyPlantAndEquipment", FinancialStatementKind.CashFlow, FinancialItems.CapitalExpenditure, true, -1),
    ];

    public async Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default)
    {
        var sym = Norm(symbol).Replace('.', '-');
        return await Cache.GetOrAddAsync($"sec:fin:{sym}", TimeSpan.FromHours(12), async c =>
        {
            var entry = (await TickersAsync(c).ConfigureAwait(false)).FirstOrDefault(e => e.Ticker == sym) ?? throw NoData($"{symbol} is not an SEC registrant");
            var payload = await GetAsync($"https://data.sec.gov/api/xbrl/companyfacts/CIK{entry.Cik:D10}.json", c, timeout: TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            var fs = await Task.Run(() => { using var doc = ParseJson(payload.Body); return ParseCompanyFacts(doc.RootElement, Norm(symbol)); }, c).ConfigureAwait(false);
            return fs.Lines.Count > 0 ? fs : throw NoData($"no annual facts for {symbol}");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Annual (10-K, full fiscal year) values; when a period is restated, the most recently filed value wins.</summary>
    internal static FinancialStatements ParseCompanyFacts(JsonElement root, string symbol, int years = 6)
    {
        var gaap = root.Path("facts", "us-gaap");
        var lines = new Dictionary<(string Item, DateOnly End), (FinancialLine Line, string Filed, int Priority)>();
        for (var pi = 0; pi < Concepts.Length; pi++)
        {
            var (concept, kind, item, duration, sign) = Concepts[pi];
            if (gaap?.Prop(concept)?.Prop("units") is not { } units) continue;
            foreach (var unit in units.EnumerateObject())
                foreach (var f in unit.Value.EnumerateArray())
                {
                    if (f.Str("form") is not ("10-K" or "10-K/A") || f.Num("val") is not { } val) continue;
                    if (Eastern.Date(f.Str("end")) is not { } end) continue;
                    if (duration)
                    {
                        if (Eastern.Date(f.Str("start")) is not { } start) continue;
                        var days = end.DayNumber - start.DayNumber;
                        if (days is < 340 or > 380) continue;
                    }
                    var filed = f.Str("filed") ?? "";
                    var key = (item, end);
                    var line = new FinancialLine(kind, item, end, val * sign);
                    if (!lines.TryGetValue(key, out var cur) || pi < cur.Priority || (pi == cur.Priority && string.CompareOrdinal(filed, cur.Filed) > 0))
                        lines[key] = (line, filed, pi);
                }
        }
        var all = lines.Values.Select(v => v.Line).ToList();
        var cutoff = all.Count == 0 ? default : all.Max(l => l.PeriodEnd).AddYears(-years);
        all = all.Where(l => l.PeriodEnd > cutoff).OrderBy(l => l.Item).ThenBy(l => l.PeriodEnd).ToList();
        foreach (var ocf in all.Where(l => l.Item == FinancialItems.OperatingCashFlow).ToList())
            if (all.FirstOrDefault(l => l.Item == FinancialItems.CapitalExpenditure && l.PeriodEnd == ocf.PeriodEnd) is { } capex)
                all.Add(new FinancialLine(FinancialStatementKind.CashFlow, FinancialItems.FreeCashFlow, ocf.PeriodEnd, ocf.Value + capex.Value));
        return new FinancialStatements { Symbol = symbol, Lines = all, Source = "sec" };
    }
}