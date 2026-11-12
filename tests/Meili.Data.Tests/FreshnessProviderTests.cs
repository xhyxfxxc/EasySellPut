using System.Text;
using Meili.Core.Models;
using Meili.Core.Time;
using Meili.Data.Providers;
using Meili.Data.Providers.China;
using Meili.Data.Providers.Nasdaq;
using Meili.Data.Providers.Yahoo;
using Meili.Data.Routing;
using Meili.Core.Abstractions;

namespace Meili.Data.Tests;

/// <summary>F1 / F4 / F6: providers state only what the source says about timeliness.</summary>
public class FreshnessProviderTests
{
    static FreshnessProviderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private static string Gbk(string name) => Encoding.GetEncoding(936).GetString(Fx.Bytes(name));

    [Fact]
    public void China_sources_declare_no_delay_but_keep_their_timestamp()
    {
        var sina = SinaProvider.ParseQuotes(Gbk("sina_quote.txt"), new Dictionary<string, string> { ["aapl"] = "AAPL" });
        var tencent = TencentProvider.ParseQuotes(Gbk("tencent_quote.txt"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usAAPL"] = "AAPL" }, out _);
        foreach (var q in new[] { sina[0], tencent[0] })
        {
            Assert.False(q.IsRealtime);
            Assert.Null(q.DelayMinutes);
            Assert.NotNull(q.AsOf);
        }
        using var em = Fx.Json("eastmoney_quote.json");
        var e = EastmoneyProvider.ParseQuote(em.RootElement, "AAPL")!;
        Assert.False(e.IsRealtime);
        Assert.Null(e.DelayMinutes);
    }

    [Fact]
    public void Nasdaq_realtime_comes_only_from_its_own_flag_and_a_bare_date_is_not_a_timestamp()
    {
        using var s = Fx.Json("nasdaq_info_stock.json");
        var q = NasdaqProvider.ParseInfo(s.RootElement, "AAPL")!;
        Assert.False(q.IsRealtime);          // fixture: isRealTime=false
        Assert.Null(q.DelayMinutes);
        Assert.Null(q.AsOf);                 // "Nov 11, 2026" has no time of day
    }

    [Theory]
    [InlineData("Nov 12, 2026 10:31 AM ET", 2026, 11, 12, 10, 31)]
    [InlineData("Nov 2, 2026 3:05 PM", 2026, 11, 2, 15, 5)]
    [InlineData("2026-11-12 09:30:00", 2026, 11, 12, 9, 30)]
    public void Eastern_timestamps_parse_with_time_of_day(string s, int y, int mo, int d, int h, int mi) =>
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), MarketClock.ToEastern(Eastern.ParseTimestamp(s)!.Value));

    [Theory]
    [InlineData("Nov 11, 2026")]
    [InlineData("")]
    [InlineData(null)]
    public void Bare_dates_are_not_timestamps(string? s) => Assert.Null(Eastern.ParseTimestamp(s));

    [Fact]
    public void Option_chain_and_fundamentals_never_use_download_time_as_market_time()
    {
        using var d = Fx.Json("yahoo_options.json");
        var c = YahooProvider.ParseOptions(d.RootElement, "AAPL");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794258001), c.AsOf); // underlying quote time in the response
        Assert.Null(c.FetchedAt);
        using var f = Fx.Json("yahoo_quotesummary.json");
        Assert.Null(YahooProvider.ParseQuoteSummary(f.RootElement, "AAPL")!.AsOf);
        using var n = Fx.Json("nasdaq_option_chain.json");
        Assert.Null(NasdaqProvider.ParseOptionChain(n.RootElement, "AAPL", new DateOnly(2026, 11, 11)).AsOf);
    }

    private sealed class OneQuote(string id) : IQuoteProvider, IOptionsProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public DataCapability Capabilities => DataCapability.Quote | DataCapability.Options;
        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> s, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quote>>(s.Select(x => new Quote { Symbol = x, Price = 1, Source = id }).ToList());
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateOnly>>([new DateOnly(2026, 12, 18)]);
        public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default) =>
            Task.FromResult(new OptionChain
            {
                Underlying = symbol, Expirations = [new DateOnly(2026, 12, 18)], Source = id,
                Contracts = [new OptionContract { ContractSymbol = "X", Underlying = symbol, Right = OptionRight.Call, Strike = 1, Expiration = new DateOnly(2026, 12, 18) }],
            });
    }

    [Fact]
    public async Task Router_stamps_fetch_time_without_touching_market_time()
    {
        var at = new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
        var p = new OneQuote("a");
        var router = new FailoverRouter([p], new HealthTracker(), new Dictionary<DataCapability, string[]>
        {
            [DataCapability.Quote] = ["a"], [DataCapability.Options] = ["a"],
        }) { Now = () => at };
        var q = (await router.GetQuotesAsync(["AAPL"]))[0];
        Assert.Equal(at, q.FetchedAt);
        Assert.Null(q.AsOf);
        var chain = await router.GetChainAsync("AAPL");
        Assert.Equal(at, chain.FetchedAt);
        Assert.Null(chain.AsOf);
    }
}
