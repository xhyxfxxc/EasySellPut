namespace TyuStock.Analytics.Tests;

/// <summary>B1 fallback: ATM IV of the expiration nearest 30 days.</summary>
public class AtmVolatilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private static DateOnly D(int days) => DateOnly.FromDateTime(Now.UtcDateTime).AddDays(days);

    [Fact]
    public void Picks_the_expiry_closest_to_30_days_skipping_the_very_short_ones()
    {
        Assert.Equal(D(28), AtmVolatility.PickExpiration([D(1), D(3), D(28), D(35), D(63)], Now));
        Assert.Equal(D(35), AtmVolatility.PickExpiration([D(2), D(35), D(91)], Now));
        Assert.Equal(D(33), AtmVolatility.PickExpiration([D(27), D(33)], Now));   // tie → later
        Assert.Null(AtmVolatility.PickExpiration([D(1), D(5)], Now));
        Assert.Null(AtmVolatility.PickExpiration([], Now));
    }

    [Fact]
    public void Averages_call_and_put_provider_iv_at_the_strike_nearest_spot()
    {
        var c = new[]
        {
            new AtmCandidate(OptionRight.Call, 95, 0.40, null, null, null),
            new AtmCandidate(OptionRight.Call, 100, 0.30, null, null, null),
            new AtmCandidate(OptionRight.Put, 100, 0.34, null, null, null),
            new AtmCandidate(OptionRight.Put, 105, 0.20, null, null, null),
        };
        var e = AtmVolatility.Estimate(c, 101, D(30), Now, 0.04)!.Value;
        Assert.Equal(100, e.Strike);
        Assert.Equal(0.32, e.Iv, 10);
        Assert.Equal(2, e.Legs);
    }

    [Fact]
    public void Solves_from_prices_when_the_source_has_no_iv_and_skips_unusable_strikes()
    {
        var exp = D(30);
        var years = OptionTime.YearsToExpiry(exp, Now);
        var call = BlackScholes.Price(OptionRight.Call, 100, 102.5, years, 0.04, 0, 0.27);
        var put = BlackScholes.Price(OptionRight.Put, 100, 102.5, years, 0.04, 0, 0.27);
        var c = new[]
        {
            new AtmCandidate(OptionRight.Call, 100, null, null, null, null),          // nearest, but nothing usable
            new AtmCandidate(OptionRight.Call, 102.5, null, call - 0.01, call + 0.01, null),
            new AtmCandidate(OptionRight.Put, 102.5, 50.0, put - 0.01, put + 0.01, null), // implausible provider IV is ignored
        };
        var e = AtmVolatility.Estimate(c, 100, exp, Now, 0.04)!.Value;
        Assert.Equal(102.5, e.Strike);
        Assert.Equal(0.27, e.Iv, 3);
    }

    [Fact]
    public void Returns_null_without_spot_or_contracts()
    {
        Assert.Null(AtmVolatility.Estimate([], 100, D(30), Now, 0.04));
        Assert.Null(AtmVolatility.Estimate([new AtmCandidate(OptionRight.Call, 100, 0.3, null, null, null)], 0, D(30), Now, 0.04));
        Assert.Null(AtmVolatility.Estimate([new AtmCandidate(OptionRight.Call, 100, 0.3, null, null, null)], 100, D(-1), Now, 0.04));
    }
}
