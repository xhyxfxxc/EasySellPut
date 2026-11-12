using Meili.Analytics;

namespace Meili.Analytics.Tests;

public class StrategyExpiryTests
{
    internal static readonly DateOnly E = new(2026, 12, 18);
    private const double Tol = 1e-9;

    private static void AssertAnalysis(OptionStrategy s, double[] breakevens, double maxProfit, double maxLoss)
    {
        var a = s.AnalyzeAtExpiry();
        Assert.True(a.IsExact);
        Assert.Equal(breakevens.Length, a.Breakevens.Count);
        for (int i = 0; i < breakevens.Length; i++) Assert.Equal(breakevens[i], a.Breakevens[i], Tol);
        Assert.Equal(maxProfit, a.MaxProfit, Tol);
        Assert.Equal(maxLoss, a.MaxLoss, Tol);
        foreach (var be in a.Breakevens) Assert.Equal(0.0, s.PnlAtExpiry(be), 1e-7);
        if (!a.IsMaxProfitUnbounded) Assert.Equal(a.MaxProfit, s.PnlAtExpiry(a.MaxProfitPrice!.Value), Tol);
        if (!a.IsMaxLossUnbounded) Assert.Equal(a.MaxLoss, s.PnlAtExpiry(a.MaxLossPrice!.Value), Tol);
        // No sampled price beats the reported extremes.
        for (double x = 0; x <= 400; x += 0.25)
        {
            double p = s.PnlAtExpiry(x);
            Assert.True(p <= a.MaxProfit + 1e-7 && p >= a.MaxLoss - 1e-7, $"{s.Name} at {x}: {p}");
        }
    }

    [Fact]
    public void Long_call()
    {
        var s = StrategyPresets.LongCall(new(100, 5), E);
        Assert.Equal(500, s.NetCost);
        Assert.Equal(1500, s.PnlAtExpiry(120), Tol);
        AssertAnalysis(s, new[] { 105.0 }, double.PositiveInfinity, -500);
        Assert.True(s.AnalyzeAtExpiry().IsMaxProfitUnbounded);
        Assert.Equal(0.0, s.AnalyzeAtExpiry().MaxLossPrice); // ties report the lowest price
    }

    [Fact]
    public void Long_put() => AssertAnalysis(StrategyPresets.LongPut(new(100, 4), E), new[] { 96.0 }, 9600, -400);

    [Fact]
    public void Covered_call() => AssertAnalysis(StrategyPresets.CoveredCall(100, new(105, 2), E), new[] { 98.0 }, 700, -9800);

    [Fact]
    public void Protective_put() => AssertAnalysis(StrategyPresets.ProtectivePut(100, new(95, 3), E), new[] { 103.0 }, double.PositiveInfinity, -800);

    [Fact]
    public void Vertical_spreads()
    {
        AssertAnalysis(StrategyPresets.BullCallSpread(new(100, 6), new(110, 2), E), new[] { 104.0 }, 600, -400);
        AssertAnalysis(StrategyPresets.BearCallSpread(new(100, 6), new(110, 2), E), new[] { 104.0 }, 400, -600);
        AssertAnalysis(StrategyPresets.BullPutSpread(new(90, 2), new(100, 6), E), new[] { 96.0 }, 400, -600);
        AssertAnalysis(StrategyPresets.BearPutSpread(new(90, 2), new(100, 6), E), new[] { 96.0 }, 600, -400);
        Assert.Equal(-400, StrategyPresets.BearCallSpread(new(100, 6), new(110, 2), E).NetCost, Tol);
    }

    [Fact]
    public void Straddles_and_strangles()
    {
        AssertAnalysis(StrategyPresets.Straddle(new(100, 5), new(100, 4), E), new[] { 91.0, 109.0 }, double.PositiveInfinity, -900);
        var shortStraddle = StrategyPresets.Straddle(new(100, 5), new(100, 4), E, PositionSide.Short);
        Assert.Equal(StrategyKind.ShortStraddle, shortStraddle.Kind);
        AssertAnalysis(shortStraddle, new[] { 91.0, 109.0 }, 900, double.NegativeInfinity);
        AssertAnalysis(StrategyPresets.Strangle(new(95, 2), new(105, 3), E), new[] { 90.0, 110.0 }, double.PositiveInfinity, -500);
        AssertAnalysis(StrategyPresets.Strangle(new(95, 2), new(105, 3), E, PositionSide.Short, 2), new[] { 90.0, 110.0 }, 1000, double.NegativeInfinity);
    }

    [Fact]
    public void Iron_condor()
    {
        var s = StrategyPresets.IronCondor(new(90, 1), new(95, 2.5), new(105, 2.5), new(110, 1), E);
        Assert.Equal(-300, s.NetCost, Tol);
        AssertAnalysis(s, new[] { 92.0, 108.0 }, 300, -200);
        Assert.Equal(1.5, s.AnalyzeAtExpiry().RewardToRisk, Tol); // 300 / 200
    }

    [Fact]
    public void Butterflies()
    {
        var call = StrategyPresets.Butterfly(OptionRight.Call, new(95, 7), new(100, 4), new(105, 2), E);
        Assert.Equal(2, call.Legs[1].Quantity);
        AssertAnalysis(call, new[] { 96.0, 104.0 }, 400, -100);
        Assert.Equal(100.0, call.AnalyzeAtExpiry().MaxProfitPrice);
        var put = StrategyPresets.Butterfly(OptionRight.Put, new(95, 1), new(100, 3), new(105, 6), E);
        AssertAnalysis(put, new[] { 96.0, 104.0 }, 400, -100);
    }

    [Fact]
    public void Payoff_identities()
    {
        // Long + short the same call nets to zero everywhere.
        var flat = new OptionStrategy(new[] { StrategyLeg.Call(PositionSide.Long, 100, 5, E), StrategyLeg.Call(PositionSide.Short, 100, 5, E) });
        var a = flat.AnalyzeAtExpiry();
        Assert.Equal(0, a.MaxProfit); Assert.Equal(0, a.MaxLoss);
        Assert.False(a.IsMaxProfitUnbounded || a.IsMaxLossUnbounded);

        // Synthetic long stock: long call + short put = 100 x (S - K) - net premium.
        var synth = new OptionStrategy(new[] { StrategyLeg.Call(PositionSide.Long, 100, 6, E), StrategyLeg.Put(PositionSide.Short, 100, 4, E) });
        var stock = new OptionStrategy(new[] { StrategyLeg.Stock(PositionSide.Long, 100, 102) });
        for (double x = 0; x <= 300; x += 7.5) Assert.Equal(stock.PnlAtExpiry(x), synth.PnlAtExpiry(x), Tol);
        AssertAnalysis(synth, new[] { 102.0 }, double.PositiveInfinity, -10200);

        // Covered call = short put (same strike) + constant.
        var cc = StrategyPresets.CoveredCall(100, new(105, 2), E);
        var shortPut = new OptionStrategy(new[] { StrategyLeg.Put(PositionSide.Short, 105, 0, E) });
        for (double x = 0; x <= 300; x += 5) Assert.Equal(700, cc.PnlAtExpiry(x) - shortPut.PnlAtExpiry(x), Tol);

        // Iron condor = bull put spread + bear call spread.
        var ic = StrategyPresets.IronCondor(new(90, 1), new(95, 2.5), new(105, 2.5), new(110, 1), E);
        var bps = StrategyPresets.BullPutSpread(new(90, 1), new(95, 2.5), E);
        var bcs = StrategyPresets.BearCallSpread(new(105, 2.5), new(110, 1), E);
        for (double x = 0; x <= 300; x += 2.5) Assert.Equal(bps.PnlAtExpiry(x) + bcs.PnlAtExpiry(x), ic.PnlAtExpiry(x), Tol);
    }

    [Fact]
    public void Quantity_and_multiplier_scale_pnl()
    {
        var one = StrategyPresets.BullCallSpread(new(100, 6), new(110, 2), E);
        var three = StrategyPresets.BullCallSpread(new(100, 6), new(110, 2), E, 3);
        Assert.Equal(3 * one.PnlAtExpiry(107), three.PnlAtExpiry(107), Tol);
        var mini = new OptionStrategy(new[] { StrategyLeg.Call(PositionSide.Long, 100, 5, E, multiplier: 10) });
        Assert.Equal(150, mini.PnlAtExpiry(120), Tol);
    }

    [Fact]
    public void Validation()
    {
        Assert.Throws<ArgumentException>(() => StrategyPresets.BullCallSpread(new(110, 2), new(100, 6), E));
        Assert.Throws<ArgumentException>(() => StrategyPresets.IronCondor(new(90, 1), new(105, 2.5), new(95, 2.5), new(110, 1), E));
        Assert.Throws<ArgumentException>(() => StrategyPresets.Straddle(new(100, 5), new(95, 4), E));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategyLeg.Call(PositionSide.Long, 100, 5, E, quantity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategyLeg.Put(PositionSide.Long, 100, -1, E));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategyLeg.Put(PositionSide.Long, 0, 1, E));
        Assert.Throws<ArgumentException>(() => new StrategyLeg(LegType.Call, PositionSide.Long, 1, 100, 5));
        Assert.Throws<ArgumentException>(() => new OptionStrategy(Array.Empty<StrategyLeg>()));
        var stock = StrategyLeg.Stock(PositionSide.Short, 50, 20);
        Assert.Equal(1, stock.Multiplier); Assert.Equal(-50, stock.Units); Assert.Null(stock.Expiry);
    }

    [Fact]
    public void Curve_includes_strikes_and_range_covers_breakevens()
    {
        var s = StrategyPresets.IronCondor(new(90, 1), new(95, 2.5), new(105, 2.5), new(110, 1), E);
        var (lo, hi) = s.SuggestPriceRange(100);
        Assert.True(lo < 90 && hi > 110 && lo >= 0);
        var c = s.Curve(lo, hi, 50);
        foreach (var k in s.Strikes) Assert.Contains(k, c.Prices);
        Assert.Null(c.ValuationPnl);
        Assert.True(c.Prices.Zip(c.Prices.Skip(1)).All(p => p.Second > p.First));
        for (int i = 0; i < c.Prices.Length; i++) Assert.Equal(s.PnlAtExpiry(c.Prices[i]), c.ExpiryPnl[i]);
        Assert.Throws<ArgumentException>(() => s.Curve(10, 5));
    }

    [Fact]
    public void Expiry_metadata()
    {
        var cal = new OptionStrategy(new[]
        {
            StrategyLeg.Call(PositionSide.Short, 100, 2, E),
            StrategyLeg.Call(PositionSide.Long, 100, 4, E.AddDays(28)),
        }, "Calendar");
        Assert.True(cal.HasMixedExpiries);
        Assert.Equal(E, cal.EarliestExpiry);
        Assert.Equal(E.AddDays(28), cal.LatestExpiry);
        Assert.False(StrategyPresets.LongCall(new(100, 5), E).HasMixedExpiries);
        Assert.Equal(new[] { 90.0, 95, 105, 110 }, StrategyPresets.IronCondor(new(90, 1), new(95, 2.5), new(105, 2.5), new(110, 1), E).Strikes);
    }
}