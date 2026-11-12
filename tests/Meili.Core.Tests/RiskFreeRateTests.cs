using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Core.Pricing;
using Meili.Core.Settings;

namespace Meili.Core.Tests;

public class RiskFreeRateTests
{
    private sealed class Data : IMarketDataService
    {
        public int Calls;
        public Func<Quote?> Make { get; set; } = () => new Quote { Symbol = "^IRX", Price = 4.12, Source = "yahoo" };
        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
        {
            Calls++;
            var q = Make();
            return Task.FromResult<IReadOnlyList<Quote>>(q is null ? [] : [q]);
        }
        public Task<BarSeries> GetBarsAsync(BarRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Treasury_yield_is_used_and_cached()
    {
        var d = new Data();
        var s = new PricingSettings();
        var now = DateTimeOffset.UtcNow;
        var p = new RiskFreeRateProvider(d, () => s) { Now = () => now };
        var r = await p.GetAsync();
        Assert.Equal((0.0412, RateOrigin.Treasury13Week), (Math.Round(r.Rate, 6), r.Origin));
        await p.GetAsync();
        Assert.Equal(1, d.Calls);
        now = now.AddHours(7);
        await p.GetAsync();
        Assert.Equal(2, d.Calls);
    }

    [Fact]
    public async Task Failure_falls_back_to_fixed_and_backs_off()
    {
        var d = new Data { Make = () => throw new HttpRequestException("down") };
        var s = new PricingSettings { FixedRatePercent = 3.5 };
        var now = DateTimeOffset.UtcNow;
        var p = new RiskFreeRateProvider(d, () => s) { Now = () => now };
        var r = await p.GetAsync();
        Assert.Equal((0.035, RateOrigin.FixedFallback), (r.Rate, r.Origin));
        await p.GetAsync();
        Assert.Equal(1, d.Calls);                          // no retry storm
        d.Make = () => new Quote { Symbol = "^IRX", Price = 99, Source = "x" };
        now = now.AddMinutes(20);
        Assert.Equal(RateOrigin.FixedFallback, (await p.GetAsync()).Origin);   // nonsense rejected
    }

    [Fact]
    public async Task Fixed_mode_never_fetches()
    {
        var d = new Data();
        var s = new PricingSettings { RateMode = RiskFreeRateMode.Fixed, FixedRatePercent = 5 };
        var p = new RiskFreeRateProvider(d, () => s);
        var r = await p.GetAsync();
        Assert.Equal((0.05, RateOrigin.Fixed), (r.Rate, r.Origin));
        Assert.Equal(0, d.Calls);
        Assert.Equal(RateOrigin.Fixed, p.Current.Origin);
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var a = new AppSettings();
        a.Pricing.FixedRatePercent = double.NaN;
        a.Normalize();
        Assert.Equal(4.0, a.Pricing.FixedRatePercent);
        a.Pricing.FixedRatePercent = 80;
        a.Normalize();
        Assert.Equal(20, a.Pricing.FixedRatePercent);
    }
}
