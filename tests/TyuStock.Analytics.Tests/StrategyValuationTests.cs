using TyuStock.Analytics;

namespace TyuStock.Analytics.Tests;

public class StrategyValuationTests
{
    private static readonly DateOnly E = StrategyExpiryTests.E;
    private const double R = 0.04, Q = 0.01, Spot = 100;
    private static readonly StrategyValuation Entry = StrategyValuation.AtDate(E.AddDays(-45), R, Q);

    private static LegQuote Quote(OptionRight right, double k, double iv)
        => new(k, BlackScholes.Price(right, Spot, k, Entry.YearsTo(E), R, Q, iv), iv);

    private static OptionStrategy Condor() => StrategyPresets.IronCondor(
        Quote(OptionRight.Put, 90, 0.30), Quote(OptionRight.Put, 95, 0.27), Quote(OptionRight.Call, 105, 0.24), Quote(OptionRight.Call, 110, 0.23), E);

    [Fact]
    public void Fair_premiums_give_zero_pnl_at_entry()
    {
        Assert.Equal(0.0, Condor().PnlAt(Spot, Entry), 1e-9);
        var straddle = StrategyPresets.Straddle(Quote(OptionRight.Call, 100, 0.25), Quote(OptionRight.Put, 100, 0.25), E);
        Assert.Equal(0.0, straddle.PnlAt(Spot, Entry), 1e-9);
    }

    [Fact]
    public void Valuation_at_expiry_close_equals_expiry_payoff()
    {
        var s = Condor();
        var atExpiry = StrategyValuation.AtDate(E, R, Q);
        Assert.Equal(0.0, atExpiry.YearsTo(E));
        for (double x = 0; x <= 200; x += 2.5) Assert.Equal(s.PnlAtExpiry(x), s.PnlAt(x, atExpiry), 1e-9);
        // One day earlier the profile is smooth but already close to the payoff far from the strikes.
        var dayBefore = StrategyValuation.AtDate(E.AddDays(-1), R, Q);
        Assert.Equal(s.PnlAtExpiry(50), s.PnlAt(50, dayBefore), 1.0);
    }

    [Fact]
    public void Position_greeks_are_sums_of_leg_greeks_and_match_finite_differences()
    {
        var s = Condor();
        var g = s.GreeksAt(103, Entry);
        double h = 1e-3;
        Assert.Equal((s.PnlAt(103 + h, Entry) - s.PnlAt(103 - h, Entry)) / (2 * h), g.Delta, 1e-5);
        Assert.Equal((s.PnlAt(103 + h, Entry) - 2 * s.PnlAt(103, Entry) + s.PnlAt(103 - h, Entry)) / (h * h), g.Gamma, 1e-3);
        var up = Entry with { VolatilityShift = 0.0001 }; var dn = Entry with { VolatilityShift = -0.0001 };
        Assert.Equal((s.PnlAt(103, up) - s.PnlAt(103, dn)) / 0.0002 / 100, g.Vega, 1e-5);
        double vegaSum = s.Legs.Sum(l => l.Units * StrategyLeg_greeks(l).Vega);
        Assert.Equal(vegaSum, g.Vega, 1e-12);
        Assert.Equal(g.Value - s.NetCost, g.Pnl, 1e-12);
        Assert.True(g.Theta > 0, "a short condor near the money earns time decay");

        static OptionGreeks StrategyLeg_greeks(StrategyLeg l) => OptionStrategy.LegGreeks(l, 103, Entry);
    }

    [Fact]
    public void Stock_legs_value_at_spot_with_unit_delta()
    {
        var cc = StrategyPresets.CoveredCall(Spot, Quote(OptionRight.Call, 105, 0.25), E);
        var g = cc.GreeksAt(Spot, Entry);
        var callDelta = BlackScholes.Greeks(OptionRight.Call, Spot, 105, Entry.YearsTo(E), R, Q, 0.25).Delta;
        Assert.Equal(100 * (1 - callDelta), g.Delta, 1e-9);
        Assert.Equal(0.0, g.Pnl, 1e-9);
    }

    [Fact]
    public void Missing_iv_gives_nan_until_a_default_is_supplied()
    {
        var s = StrategyPresets.LongCall(new(100, 5), E);
        Assert.False(s.HasAllImpliedVolatilities);
        Assert.True(double.IsNaN(s.PnlAt(Spot, Entry)));
        Assert.Throws<InvalidOperationException>(() => s.AnalyzeAt(Entry, 50, 150));
        var withDefault = Entry with { DefaultVolatility = 0.3 };
        double expected = 100 * (BlackScholes.Price(OptionRight.Call, Spot, 100, Entry.YearsTo(E), R, Q, 0.3) - 5);
        Assert.Equal(expected, s.PnlAt(Spot, withDefault), 1e-9);
        // Expired legs need no IV.
        Assert.Equal(s.PnlAtExpiry(120), s.PnlAt(120, StrategyValuation.AtDate(E.AddDays(1), R, Q)), 1e-12);
    }

    [Fact]
    public void Volatility_shift_moves_value_in_the_direction_of_vega()
    {
        var straddle = StrategyPresets.Straddle(Quote(OptionRight.Call, 100, 0.25), Quote(OptionRight.Put, 100, 0.25), E);
        Assert.True(straddle.PnlAt(Spot, Entry with { VolatilityShift = 0.05 }) > 0);
        Assert.True(straddle.PnlAt(Spot, Entry with { VolatilityShift = -0.05 }) < 0);
        Assert.True(double.IsFinite(straddle.PnlAt(Spot, Entry with { VolatilityShift = -1 }))); // floored at 0 vol
    }

    [Fact]
    public void Zero_underlying_is_handled_as_a_limit()
    {
        var put = StrategyPresets.LongPut(Quote(OptionRight.Put, 100, 0.3), E);
        double t = Entry.YearsTo(E);
        Assert.Equal(100 * (100 * Math.Exp(-R * t) - put.Legs[0].Premium), put.PnlAt(0, Entry), 1e-9);
        Assert.True(double.IsNaN(put.PnlAt(-1, Entry)));
    }

    [Fact]
    public void Pre_expiry_analysis_of_long_straddle()
    {
        var s = StrategyPresets.Straddle(Quote(OptionRight.Call, 100, 0.25), Quote(OptionRight.Put, 100, 0.25), E);
        var mid = StrategyValuation.AtDate(E.AddDays(-20), R, Q);
        var a = s.AnalyzeAt(mid, 50, 150);
        Assert.False(a.IsExact);
        Assert.True(a.IsMaxProfitUnbounded);
        Assert.False(a.IsMaxLossUnbounded);
        Assert.Equal(2, a.Breakevens.Count);
        foreach (var be in a.Breakevens) Assert.Equal(0.0, s.PnlAt(be, mid), 1e-6);
        var exp = s.AnalyzeAtExpiry();
        Assert.True(a.Breakevens[0] > exp.Breakevens[0] && a.Breakevens[1] < exp.Breakevens[1], "time value narrows the breakevens");
        Assert.True(a.MaxLoss > exp.MaxLoss && a.MaxLoss < 0);
        // Golden-section refinement: no grid point is lower than the reported minimum.
        for (double x = 50; x <= 150; x += 0.01) Assert.True(s.PnlAt(x, mid) >= a.MaxLoss - 1e-7);
        Assert.Equal(a.MaxLoss, s.PnlAt(a.MaxLossPrice!.Value, mid), 1e-9);
    }

    [Fact]
    public void Pre_expiry_analysis_of_short_call_detects_unbounded_loss()
    {
        var s = new OptionStrategy(new[] { StrategyLeg.Call(PositionSide.Short, 100, 5, E, impliedVolatility: 0.3) });
        var a = s.AnalyzeAt(Entry, 60, 160);
        Assert.True(a.IsMaxLossUnbounded);
        Assert.False(a.IsMaxProfitUnbounded);
        Assert.Single(a.Breakevens);
        Assert.Equal(60, a.RangeLow); Assert.Equal(160, a.RangeHigh);
    }

    [Fact]
    public void Curve_with_valuation_series()
    {
        var s = Condor();
        var c = s.Curve(70, 130, 121, Entry);
        Assert.NotNull(c.ValuationPnl);
        Assert.Equal(c.Prices.Length, c.ValuationPnl!.Length);
        for (int i = 0; i < c.Prices.Length; i += 10) Assert.Equal(s.PnlAt(c.Prices[i], Entry), c.ValuationPnl[i], 1e-12);
    }
}