using Meili.Analytics;

namespace Meili.Analytics.Tests;

public class ImpliedVolatilityTests
{
    private const double S = 100, R = 0.045, Q = 0.012;

    [Fact]
    public void Round_trip_across_moneyness_maturity_and_vol()
    {
        int solved = 0;
        foreach (var right in new[] { OptionRight.Call, OptionRight.Put })
        foreach (var k in new[] { 50.0, 80, 95, 100, 105, 120, 200 })
        foreach (var t in new[] { 1.0 / 365, 7.0 / 365, 0.25, 1, 3 })
        foreach (var sigma in new[] { 0.02, 0.08, 0.3, 1.0, 3.0 })
        {
            double price = BlackScholes.Price(right, S, k, t, R, Q, sigma);
            double intrinsicFwd = Math.Max(right == OptionRight.Call ? S * Math.Exp(-Q * t) - k * Math.Exp(-R * t) : k * Math.Exp(-R * t) - S * Math.Exp(-Q * t), 0);
            double timeValue = price - intrinsicFwd;
            var iv = ImpliedVolatility.Solve(right, price, S, k, t, R, Q);
            if (timeValue < 1e-9) { continue; } // no information left in the price (deep ITM/OTM at tiny vol)
            Assert.True(iv.HasValue, $"{right} K={k} T={t} sigma={sigma} price={price}");
            double repriced = BlackScholes.Price(right, S, k, t, R, Q, iv!.Value);
            Assert.True(Math.Abs(repriced - price) <= 1e-9 * Math.Max(1, price), $"reprice {right} K={k} T={t} sigma={sigma}");
            if (BlackScholes.VegaPerUnit(S, k, t, R, Q, sigma) > 1e-2)
                Assert.True(Math.Abs(iv.Value - sigma) < 1e-7, $"iv {iv} vs {sigma} ({right} K={k} T={t})");
            solved++;
        }
        Assert.True(solved > 250, $"only {solved} cases solved");
    }

    [Fact]
    public void Hull_example_implied_vol()
    {
        // Price the Hull example at 20% and recover it.
        double c = BlackScholes.Price(OptionRight.Call, 42, 40, 0.5, 0.10, 0, 0.20);
        Assert.Equal(0.20, ImpliedVolatility.Solve(OptionRight.Call, c, 42, 40, 0.5, 0.10, 0)!.Value, 1e-10);
    }

    [Theory]
    [InlineData(19.0, 100, 80, 0.5)]   // below intrinsic
    [InlineData(100.0, 100, 80, 0.5)]  // at/above upper bound (S e^-qT)
    [InlineData(5.0, 100, 100, 0.0)]   // expired
    [InlineData(0.0, 100, 100, 0.5)]   // zero price
    [InlineData(double.NaN, 100, 100, 0.5)]
    [InlineData(5.0, 0, 100, 0.5)]     // invalid spot
    public void Unsolvable_inputs_return_null(double price, double spot, double strike, double years)
        => Assert.Null(ImpliedVolatility.Solve(OptionRight.Call, price, spot, strike, years, 0.0, 0.0));

    [Fact]
    public void Quote_based_solving_and_resolution()
    {
        double t = 30.0 / 365;
        double mid = BlackScholes.Price(OptionRight.Put, S, 95, t, R, Q, 0.35);
        Assert.Null(ImpliedVolatility.SolveFromQuote(OptionRight.Put, 0, mid, S, 95, t, R, Q));
        Assert.Null(ImpliedVolatility.SolveFromQuote(OptionRight.Put, mid + 0.1, mid - 0.1, S, 95, t, R, Q));
        Assert.Null(ImpliedVolatility.SolveFromQuote(OptionRight.Put, null, mid, S, 95, t, R, Q));
        Assert.Equal(0.35, ImpliedVolatility.SolveFromQuote(OptionRight.Put, mid - 0.05, mid + 0.05, S, 95, t, R, Q)!.Value, 1e-9);

        // Yahoo placeholder 1e-5 is rejected and replaced by the mid-implied value.
        var fromMid = ImpliedVolatility.Resolve(OptionRight.Put, 1e-5, mid - 0.05, mid + 0.05, null, S, 95, t, R, Q);
        Assert.Equal(IvOrigin.SolvedFromMid, fromMid!.Value.Origin);
        Assert.Equal(0.35, fromMid.Value.Value, 1e-9);

        var provider = ImpliedVolatility.Resolve(OptionRight.Put, 0.31, mid - 0.05, mid + 0.05, null, S, 95, t, R, Q);
        Assert.Equal(new ResolvedIv(0.31, IvOrigin.Provider), provider);

        var fromLast = ImpliedVolatility.Resolve(OptionRight.Put, null, 0, 0, mid, S, 95, t, R, Q);
        Assert.Equal(IvOrigin.SolvedFromLast, fromLast!.Value.Origin);
        Assert.Equal(0.35, fromLast.Value.Value, 1e-9);

        Assert.Null(ImpliedVolatility.Resolve(OptionRight.Put, double.NaN, null, null, null, S, 95, t, R, Q));
    }

    [Fact]
    public void Iv_filter()
    {
        Assert.False(IvFilter.IsPlausible(1e-5));
        Assert.False(IvFilter.IsPlausible(0));
        Assert.False(IvFilter.IsPlausible(double.PositiveInfinity));
        Assert.False(IvFilter.IsPlausible(null));
        Assert.True(IvFilter.IsPlausible(0.25));
        Assert.Equal(0.253, IvFilter.FromPercent(25.3)!.Value, 1e-15);
        Assert.Null(IvFilter.FromPercent(double.NaN));
    }
}

public class OptionTimeTests
{
    [Fact]
    public void Expiry_instant_is_dst_aware()
    {
        Assert.Equal(new DateTimeOffset(2026, 1, 16, 21, 0, 0, TimeSpan.Zero), OptionTime.ExpiryInstant(new DateOnly(2026, 1, 16)));
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 20, 0, 0, TimeSpan.Zero), OptionTime.ExpiryInstant(new DateOnly(2026, 7, 17)));
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 13, 30, 0, TimeSpan.Zero), OptionTime.ExpiryInstant(new DateOnly(2026, 7, 17), OptionTime.MarketOpen));
    }

    [Fact]
    public void Years_and_days_to_expiry()
    {
        var expiry = new DateOnly(2026, 1, 16);
        var dayBefore = new DateTimeOffset(2026, 1, 15, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(1.0 / 365, OptionTime.YearsToExpiry(expiry, dayBefore), 1e-15);
        Assert.Equal(0.0, OptionTime.YearsToExpiry(expiry, dayBefore.AddDays(2)));
        Assert.Equal(1, OptionTime.DaysToExpiry(expiry, dayBefore));
        Assert.Equal(0, OptionTime.DaysToExpiry(expiry, new DateTimeOffset(2026, 1, 16, 15, 0, 0, TimeSpan.Zero)));
        Assert.Equal(-1, OptionTime.DaysToExpiry(expiry, new DateTimeOffset(2026, 1, 17, 15, 0, 0, TimeSpan.Zero)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OptionTime.YearsBetween(dayBefore, dayBefore, 0));
    }

    [Fact]
    public void Spring_forward_gap_moves_forward()
    {
        // 2026-03-08 02:30 does not exist in New York; it maps to 03:30 EDT = 07:30Z.
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), UsEastern.ToInstant(new DateOnly(2026, 3, 8), new TimeOnly(2, 30)));
    }
}