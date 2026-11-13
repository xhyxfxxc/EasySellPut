namespace Meili.Analytics.Tests;

public sealed class StrategyFromQuotesTests
{
    private static readonly DateOnly Exp = new(2026, 12, 18);

    [Fact]
    public void Covered_call_picks_first_call_at_or_above_spot()
    {
        var s = StrategyFromQuotes.TryBuild(StrategyKind.CoveredCall, 100, Exp, Chain(), quantity: 2);
        Assert.NotNull(s);
        Assert.Equal(StrategyKind.CoveredCall, s!.Kind);
        Assert.Equal(2, s.Legs.Count);
        var stock = s.Legs.Single(l => l.Type == LegType.Stock);
        var call = s.Legs.Single(l => l.Type == LegType.Call);
        Assert.Equal(PositionSide.Long, stock.Side);
        Assert.Equal(200, stock.Quantity);
        Assert.Equal(100, stock.Premium);
        Assert.Equal(PositionSide.Short, call.Side);
        Assert.Equal(105, call.Strike);
        Assert.Equal(2.1, call.Premium);
        Assert.Equal(0.22, call.ImpliedVolatility);
        Assert.Equal(2, call.Quantity);
    }

    [Fact]
    public void Protective_put_picks_last_put_at_or_below_spot()
    {
        var s = StrategyFromQuotes.TryBuild(StrategyKind.ProtectivePut, 100, Exp, Chain());
        Assert.NotNull(s);
        var put = s!.Legs.Single(l => l.Type == LegType.Put);
        Assert.Equal(PositionSide.Long, put.Side);
        Assert.Equal(95, put.Strike);
        Assert.Equal(1.6, put.Premium);
    }

    [Fact]
    public void Vertical_spreads_use_atm_and_wing_steps()
    {
        var bull = StrategyFromQuotes.TryBuild(StrategyKind.BullCallSpread, 100, Exp, Chain(), wingSteps: 2);
        Assert.NotNull(bull);
        Assert.Equal(new[] { 100.0, 110.0 }, bull!.Strikes.ToArray());
        Assert.Equal(PositionSide.Long, bull.Legs.Single(l => l.Strike == 100).Side);
        Assert.Equal(PositionSide.Short, bull.Legs.Single(l => l.Strike == 110).Side);

        var bear = StrategyFromQuotes.TryBuild(StrategyKind.BearPutSpread, 100, Exp, Chain(), wingSteps: 2);
        Assert.NotNull(bear);
        Assert.Equal(new[] { 90.0, 100.0 }, bear!.Strikes.ToArray());
        Assert.Equal(PositionSide.Short, bear.Legs.Single(l => l.Strike == 90).Side);
        Assert.Equal(PositionSide.Long, bear.Legs.Single(l => l.Strike == 100).Side);
    }

    [Fact]
    public void Straddle_and_strangle_and_iron_condor()
    {
        var straddle = StrategyFromQuotes.TryBuild(StrategyKind.LongStraddle, 100, Exp, Chain());
        Assert.NotNull(straddle);
        Assert.Equal(new[] { 100.0 }, straddle!.Strikes.ToArray());
        Assert.All(straddle.Legs, l => Assert.Equal(PositionSide.Long, l.Side));

        var strangle = StrategyFromQuotes.TryBuild(StrategyKind.ShortStrangle, 100, Exp, Chain(), wingSteps: 1);
        Assert.NotNull(strangle);
        Assert.Equal(StrategyKind.ShortStrangle, strangle!.Kind);
        Assert.Equal(new[] { 95.0, 105.0 }, strangle.Strikes.ToArray());
        Assert.All(strangle.Legs, l => Assert.Equal(PositionSide.Short, l.Side));

        var ic = StrategyFromQuotes.TryBuild(StrategyKind.IronCondor, 100, Exp, Chain(), wingSteps: 1);
        Assert.NotNull(ic);
        Assert.Equal(new[] { 90.0, 95.0, 105.0, 110.0 }, ic!.Strikes.ToArray());
        Assert.Equal(PositionSide.Long, ic.Legs.Single(l => l.Type == LegType.Put && l.Strike == 90).Side);
        Assert.Equal(PositionSide.Short, ic.Legs.Single(l => l.Type == LegType.Put && l.Strike == 95).Side);
        Assert.Equal(PositionSide.Short, ic.Legs.Single(l => l.Type == LegType.Call && l.Strike == 105).Side);
        Assert.Equal(PositionSide.Long, ic.Legs.Single(l => l.Type == LegType.Call && l.Strike == 110).Side);
    }

    [Fact]
    public void Butterfly_centres_on_atm()
    {
        var s = StrategyFromQuotes.TryBuild(StrategyKind.LongCallButterfly, 100, Exp, Chain(), wingSteps: 2);
        Assert.NotNull(s);
        Assert.Equal(new[] { 90.0, 100.0, 110.0 }, s!.Strikes.ToArray());
        Assert.Equal(2, s.Legs.Single(l => l.Strike == 100).Quantity);
    }

    [Fact]
    public void Thin_chain_or_bad_spot_returns_null()
    {
        var thin = new ListedQuote[]
        {
            new(OptionRight.Call, 100, 4, 0.2),
            new(OptionRight.Put, 100, 3, 0.2),
        };
        Assert.Null(StrategyFromQuotes.TryBuild(StrategyKind.IronCondor, 100, Exp, thin));
        Assert.Null(StrategyFromQuotes.TryBuild(StrategyKind.BullCallSpread, 100, Exp, thin, wingSteps: 2));
        Assert.Null(StrategyFromQuotes.TryBuild(StrategyKind.LongCall, 0, Exp, Chain()));
        Assert.Null(StrategyFromQuotes.TryBuild(StrategyKind.Custom, 100, Exp, Chain()));
        Assert.NotNull(StrategyFromQuotes.TryBuild(StrategyKind.LongCall, 100, Exp, thin));
    }

    [Fact]
    public void Expiry_analysis_of_built_covered_call_matches_preset()
    {
        var built = StrategyFromQuotes.TryBuild(StrategyKind.CoveredCall, 100, Exp, Chain());
        var direct = StrategyPresets.CoveredCall(100, new LegQuote(105, 2.1, 0.22), Exp);
        Assert.NotNull(built);
        var a = built!.AnalyzeAtExpiry();
        var b = direct.AnalyzeAtExpiry();
        Assert.Equal(b.Breakevens, a.Breakevens);
        Assert.Equal(b.MaxProfit, a.MaxProfit);
        Assert.Equal(b.MaxLoss, a.MaxLoss);
    }

    [Fact]
    public void Collar_and_iron_butterfly_build()
    {
        var collar = StrategyFromQuotes.TryBuild(StrategyKind.Collar, 100, Exp, Chain());
        Assert.NotNull(collar);
        Assert.Equal(StrategyKind.Collar, collar!.Kind);
        Assert.Equal(3, collar.Legs.Count);
        Assert.Equal(PositionSide.Long, collar.Legs.Single(l => l.Type == LegType.Stock).Side);
        Assert.Equal(PositionSide.Short, collar.Legs.Single(l => l.Type == LegType.Call).Side);
        Assert.Equal(105, collar.Legs.Single(l => l.Type == LegType.Call).Strike);
        Assert.Equal(PositionSide.Long, collar.Legs.Single(l => l.Type == LegType.Put).Side);
        Assert.Equal(95, collar.Legs.Single(l => l.Type == LegType.Put).Strike);

        var fly = StrategyFromQuotes.TryBuild(StrategyKind.IronButterfly, 100, Exp, Chain(), wingSteps: 2);
        Assert.NotNull(fly);
        Assert.Equal(new[] { 90.0, 100.0, 110.0 }, fly!.Strikes.ToArray());
        Assert.Equal(2, fly.Legs.Count(l => l.Side == PositionSide.Short && l.Strike == 100));
        Assert.Equal(PositionSide.Long, fly.Legs.Single(l => l.Type == LegType.Put && l.Strike == 90).Side);
        Assert.Equal(PositionSide.Long, fly.Legs.Single(l => l.Type == LegType.Call && l.Strike == 110).Side);
    }

    [Fact]
    public void Calendar_and_diagonal_use_the_far_chain()
    {
        var near = Chain();
        var farExp = Exp.AddDays(28);
        var far = new[]
        {
            C(90, 12.0, 0.29), C(95, 8.0, 0.26), C(100, 5.0, 0.24), C(105, 3.0, 0.22), C(110, 1.6, 0.21),
            P(90, 1.1, 0.21), P(95, 2.2, 0.22), P(100, 4.2, 0.24), P(105, 6.8, 0.26), P(110, 10.5, 0.29),
        };
        var cal = StrategyFromQuotes.TryBuild(StrategyKind.CalendarCall, 100, Exp, near, farExpiry: farExp, farQuotes: far);
        Assert.NotNull(cal);
        Assert.Equal(StrategyKind.CalendarCall, cal!.Kind);
        Assert.True(cal.HasMixedExpiries);
        Assert.Equal(Exp, cal.EarliestExpiry);
        Assert.Equal(farExp, cal.LatestExpiry);
        Assert.All(cal.Legs, l => Assert.Equal(100, l.Strike));
        Assert.Equal(PositionSide.Short, cal.Legs.Single(l => l.Expiry == Exp).Side);
        Assert.Equal(PositionSide.Long, cal.Legs.Single(l => l.Expiry == farExp).Side);
        Assert.Equal(5.0, cal.Legs.Single(l => l.Expiry == farExp).Premium);

        var put = StrategyFromQuotes.TryBuild(StrategyKind.CalendarPut, 100, Exp, near, farExpiry: farExp, farQuotes: far);
        Assert.NotNull(put);
        Assert.All(put!.Legs, l => Assert.Equal(LegType.Put, l.Type));

        var diag = StrategyFromQuotes.TryBuild(StrategyKind.DiagonalCall, 100, Exp, near, wingSteps: 1, farExpiry: farExp, farQuotes: far);
        Assert.NotNull(diag);
        Assert.True(diag!.HasMixedExpiries);
        Assert.NotEqual(diag.Legs[0].Strike, diag.Legs[1].Strike);

        Assert.Null(StrategyFromQuotes.TryBuild(StrategyKind.CalendarCall, 100, Exp, near));
    }

    private static ListedQuote[] Chain()
    {
        // 90, 95, 100, 105, 110 — both calls and puts, premiums roughly offset from ATM.
        return
        [
            C(90, 11.0, 0.30), C(95, 7.0, 0.26), C(100, 4.0, 0.24), C(105, 2.1, 0.22), C(110, 1.0, 0.21),
            P(90, 0.8, 0.21), P(95, 1.6, 0.22), P(100, 3.0, 0.24), P(105, 5.5, 0.26), P(110, 9.5, 0.30),
        ];
    }

    private static ListedQuote C(double k, double px, double iv) => new(OptionRight.Call, k, px, iv);
    private static ListedQuote P(double k, double px, double iv) => new(OptionRight.Put, k, px, iv);
}