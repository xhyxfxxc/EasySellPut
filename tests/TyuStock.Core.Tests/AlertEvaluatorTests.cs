using TyuStock.Core.Alerts;
using TyuStock.Core.Models;

namespace TyuStock.Core.Tests;

public class AlertEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(-5));

    private static Quote Q(double price, MarketState state = MarketState.Regular,
        double? ext = null, double? changePct = null, double? extPct = null, double? volume = null) => new()
    {
        Symbol = "AAPL",
        Price = price,
        ChangePercent = changePct,
        Volume = volume,
        MarketState = state,
        ExtendedPrice = ext,
        ExtendedChangePercent = extPct,
        Source = "yahoo",
    };

    private static PriceAlert A(AlertKind kind, double threshold, bool seen = true, double? last = null,
        AlertRepeatMode repeat = AlertRepeatMode.Once, bool enabled = true, bool ext = true,
        DateTimeOffset? fired = null, int cooldown = 15) => new()
    {
        Id = "t1",
        Symbol = "AAPL",
        Kind = kind,
        Threshold = threshold,
        Enabled = enabled,
        Repeat = repeat,
        CooldownMinutes = cooldown,
        IncludeExtendedHours = ext,
        HasSeenQuote = seen,
        LastSeenPrice = kind is AlertKind.PriceAbove or AlertKind.PriceBelow ? last : null,
        LastSeenChangePercent = kind is AlertKind.PercentUp or AlertKind.PercentDown ? last : null,
        LastSeenVolume = kind is AlertKind.VolumeAbove ? last : null,
        LastFiredAt = fired,
    };

    [Fact]
    public void FirstSample_DoesNotFire_EvenIfAlreadyAbove()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, seen: false), Q(190), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.WaitingForSample, r.Status);
        Assert.True(r.Next.HasSeenQuote);
        Assert.Equal(190, r.Next.LastSeenPrice);
        Assert.True(r.Next.Enabled);
    }

    [Fact]
    public void PriceAbove_FiresOnCross_AndDisablesOneShot()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, last: 179.5), Q(180), T0);
        Assert.True(r.Fired);
        Assert.Equal(AlertEvalStatus.Fired, r.Status);
        Assert.Equal(180, r.Value);
        Assert.False(r.Next.Enabled);
        Assert.Equal(T0, r.Next.LastFiredAt);
        Assert.Equal(180, r.Next.LastSeenPrice);
    }

    [Fact]
    public void PriceAbove_NoFire_WhenAlreadyAbove()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, last: 181), Q(182), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.Armed, r.Status);
        Assert.True(r.Next.Enabled);
    }

    [Fact]
    public void PriceAbove_NoFire_WhileStillBelow()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, last: 170), Q(179.99), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.Armed, r.Status);
        Assert.Equal(179.99, r.Next.LastSeenPrice);
    }

    [Fact]
    public void PriceBelow_FiresOnCross()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceBelow, 170, last: 170.01), Q(170), T0);
        Assert.True(r.Fired);
        Assert.Equal(170, r.Value);
        Assert.False(r.Next.Enabled);
    }

    [Fact]
    public void PriceBelow_NoFire_WhenAlreadyBelow()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceBelow, 170, last: 169), Q(168), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.Armed, r.Status);
    }

    [Fact]
    public void Disabled_NeverFires()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, last: 179, enabled: false), Q(190), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.Disabled, r.Status);
        Assert.False(r.Next.Enabled);
    }

    [Fact]
    public void InvalidQuote_ZeroPrice()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 180, last: 179), Q(0), T0);
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.InvalidQuote, r.Status);
        Assert.Equal(179, r.Next.LastSeenPrice);
    }

    [Fact]
    public void Repeat_FiresThenRequiresRecrossAndCooldown()
    {
        var alert = A(AlertKind.PriceAbove, 180, last: 179, repeat: AlertRepeatMode.Repeat, cooldown: 15);
        var fire = AlertEvaluator.Evaluate(alert, Q(181), T0);
        Assert.True(fire.Fired);
        Assert.True(fire.Next.Enabled);

        // Still above, same side — no recross.
        var stay = AlertEvaluator.Evaluate(fire.Next, Q(182), T0.AddMinutes(1));
        Assert.False(stay.Fired);
        Assert.Equal(AlertEvalStatus.Armed, stay.Status);

        // Drops below, then recrosses inside the cooldown window.
        var reset = AlertEvaluator.Evaluate(stay.Next, Q(179), T0.AddMinutes(2));
        Assert.False(reset.Fired);
        var recrossEarly = AlertEvaluator.Evaluate(reset.Next, Q(181), T0.AddMinutes(3));
        Assert.False(recrossEarly.Fired);
        Assert.Equal(AlertEvalStatus.Cooling, recrossEarly.Status);

        // Recross after cooldown.
        var afterCd = AlertEvaluator.Evaluate(reset.Next, Q(181), T0.AddMinutes(16));
        Assert.True(afterCd.Fired);
        Assert.True(afterCd.Next.Enabled);
        Assert.Equal(T0.AddMinutes(16), afterCd.Next.LastFiredAt);
    }

    [Fact]
    public void ExtendedHours_UsesExtendedPrice_InPremarket()
    {
        var q = Q(100, MarketState.PreMarket, ext: 205);
        var price = AlertEvaluator.EffectiveLastPrice(q, includeExtended: true, out var used);
        Assert.True(used);
        Assert.Equal(205, price);

        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 200, last: 199), q, T0);
        Assert.True(r.Fired);
        Assert.True(r.UsedExtended);
        Assert.Equal(205, r.Value);
    }

    [Fact]
    public void ExtendedHours_IgnoredInRegularSession()
    {
        var q = Q(100, MarketState.Regular, ext: 205);
        var price = AlertEvaluator.EffectiveLastPrice(q, includeExtended: true, out var used);
        Assert.False(used);
        Assert.Equal(100, price);
    }

    [Fact]
    public void ExtendedHours_OptOut_UsesRegularPriceInPremarket()
    {
        var q = Q(100, MarketState.PreMarket, ext: 205);
        var r = AlertEvaluator.Evaluate(A(AlertKind.PriceAbove, 200, last: 99, ext: false), q, T0);
        Assert.False(r.Fired);
        Assert.False(r.UsedExtended);
        Assert.Equal(100, r.Value);
    }

    [Fact]
    public void ExtendedHours_FallbackWhenRegularPriceMissing()
    {
        var q = Q(0, MarketState.Closed, ext: 150);
        var price = AlertEvaluator.EffectiveLastPrice(q, includeExtended: true, out var used);
        Assert.True(used);
        Assert.Equal(150, price);
    }

    [Fact]
    public void PercentUp_FiresOnCross()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PercentUp, 5, last: 4.9), Q(100, changePct: 5.0), T0);
        Assert.True(r.Fired);
        Assert.Equal(5.0, r.Value);
    }

    [Fact]
    public void PercentDown_UsesNegativeThreshold()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.PercentDown, 5, last: -4.9), Q(100, changePct: -5.0), T0);
        Assert.True(r.Fired);
        Assert.Equal(-5.0, r.Value);
        Assert.False(AlertEvaluator.IsCross(AlertKind.PercentDown, 5, -5.1, -6));
    }

    [Fact]
    public void PercentUp_UsesExtendedChange_InAfterHours()
    {
        var q = Q(100, MarketState.AfterHours, changePct: 1, extPct: 6);
        var r = AlertEvaluator.Evaluate(A(AlertKind.PercentUp, 5, last: 4), q, T0);
        Assert.True(r.Fired);
        Assert.True(r.UsedExtended);
        Assert.Equal(6, r.Value);
    }

    [Fact]
    public void VolumeAbove_FiresOnCross()
    {
        var r = AlertEvaluator.Evaluate(A(AlertKind.VolumeAbove, 1_000_000, last: 900_000), Q(100, volume: 1_000_000), T0);
        Assert.True(r.Fired);
        Assert.Equal(1_000_000, r.Value);
    }

    [Fact]
    public void DoesNotMutateInput()
    {
        var alert = A(AlertKind.PriceAbove, 180, last: 179);
        var r = AlertEvaluator.Evaluate(alert, Q(181), T0);
        Assert.True(r.Fired);
        Assert.True(alert.Enabled);
        Assert.Null(alert.LastFiredAt);
        Assert.Equal(179, alert.LastSeenPrice);
    }
}