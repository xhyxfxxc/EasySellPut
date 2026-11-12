namespace Meili.Analytics.Tests;

public class OptionEnricherTests
{
    private static readonly DateOnly Exp = new(2026, 12, 18);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 16, 0, 0, TimeSpan.FromHours(-5));
    private static readonly OptionEnrichmentSettings Settings = new() { RiskFreeRate = 0.04, DividendYield = 0, Now = Now };

    [Fact]
    public void KeepsPlausibleProviderIvAndGreeks()
    {
        var e = OptionEnricher.Enrich(OptionRight.Call, 200, 200, Exp, providerIv: 0.25, bid: 6, ask: 6.2, last: 6.1,
            delta: 0.52, gamma: 0.02, theta: -0.05, vega: 0.30, settings: Settings);
        Assert.Equal(0.25, e.ImpliedVolatility);
        Assert.Equal(IvOrigin.Provider, e.IvOrigin);
        Assert.Equal(0.52, e.Delta);
        Assert.Equal(0.02, e.Gamma);
        Assert.Equal(-0.05, e.Theta);
        Assert.Equal(0.30, e.Vega);
        Assert.False(e.GreeksComputedLocally);
        Assert.False(e.InTheMoney);
    }

    [Fact]
    public void DropsBogusPreMarketIv_AndSolvesFromMid()
    {
        const double spot = 100, strike = 100, mid = 4.0;
        var years = OptionTime.YearsToExpiry(Exp, Now);
        var expected = ImpliedVolatility.Solve(OptionRight.Call, mid, spot, strike, years, 0.04, 0);
        Assert.NotNull(expected);

        var e = OptionEnricher.Enrich(OptionRight.Call, spot, strike, Exp, providerIv: 1e-5, bid: 3.9, ask: 4.1, last: 0,
            settings: Settings);
        Assert.Equal(IvOrigin.SolvedFromMid, e.IvOrigin);
        Assert.NotNull(e.ImpliedVolatility);
        Assert.Equal(expected!.Value, e.ImpliedVolatility!.Value, 6);
        Assert.True(e.GreeksComputedLocally);
        Assert.True(e.Delta is > 0.4 and < 0.7);
        var g = BlackScholes.Greeks(OptionRight.Call, spot, strike, years, 0.04, 0, expected.Value);
        Assert.Equal(g.Delta, e.Delta!.Value, 8);
        Assert.Equal(g.Gamma, e.Gamma!.Value, 8);
        Assert.Equal(g.Theta, e.Theta!.Value, 8);
        Assert.Equal(g.Vega, e.Vega!.Value, 8);
    }

    [Fact]
    public void FillsMissingGreeksFromProviderIv()
    {
        var e = OptionEnricher.Enrich(OptionRight.Put, 100, 110, Exp, providerIv: 0.30, bid: 11, ask: 11.4, last: 11.2,
            settings: Settings);
        Assert.Equal(IvOrigin.Provider, e.IvOrigin);
        Assert.Equal(0.30, e.ImpliedVolatility);
        Assert.True(e.GreeksComputedLocally);
        Assert.True(e.Delta is < 0);
        Assert.True(e.InTheMoney);
        var years = OptionTime.YearsToExpiry(Exp, Now);
        var g = BlackScholes.Greeks(OptionRight.Put, 100, 110, years, 0.04, 0, 0.30);
        Assert.Equal(g.Delta, e.Delta!.Value, 8);
        // theoretical price comes from BlackScholes.Price
        Assert.Equal(g.Price, e.TheoreticalPrice!.Value, 8);
    }

    [Fact]
    public void NoMarketNoIv_LeavesGreeksEmpty_ButSetsItm()
    {
        var e = OptionEnricher.Enrich(OptionRight.Call, 50, 40, Exp, providerIv: 1e-5, bid: 0, ask: 0, last: 0, settings: Settings);
        Assert.Null(e.ImpliedVolatility);
        Assert.Null(e.Delta);
        Assert.False(e.GreeksComputedLocally);
        Assert.True(e.InTheMoney);
    }
}