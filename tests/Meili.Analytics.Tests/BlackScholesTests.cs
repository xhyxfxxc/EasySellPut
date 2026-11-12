using Meili.Analytics;

namespace Meili.Analytics.Tests;

public class NormalDistributionTests
{
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 0.8413447460685428)]
    [InlineData(-1.96, 0.024997895148220435)]
    [InlineData(3.0, 0.9986501019683699)]
    public void Cdf_matches_reference(double x, double expected) => Assert.Equal(expected, NormalDistribution.Cdf(x), 1e-14);

    [Theory]
    [InlineData(-10.0, 7.619853024160595e-24)]
    [InlineData(-30.0, 4.9067139271487634e-198)]
    // Hart/West: absolute error < 1e-14 everywhere; in the far tail the relative error is ~3e-9 (measured), ample for option values.
    public void Cdf_far_tail_has_small_relative_error(double x, double expected)
        => Assert.True(Math.Abs(NormalDistribution.Cdf(x) / expected - 1) < 1e-8);

    [Fact]
    public void Cdf_is_symmetric_and_pdf_peaks_at_zero()
    {
        for (double x = -8; x <= 8; x += 0.37) Assert.Equal(1.0, NormalDistribution.Cdf(x) + NormalDistribution.Cdf(-x), 1e-15);
        Assert.Equal(0.3989422804014327, NormalDistribution.Pdf(0), 1e-16);
        Assert.True(double.IsNaN(NormalDistribution.Cdf(double.NaN)));
    }
}

public class BlackScholesTests
{
    // Hull, Options Futures & Other Derivatives, Example 15.6: S=42 K=40 r=10% sigma=20% T=0.5 -> c=4.76, p=0.81.
    [Fact]
    public void Hull_textbook_prices()
    {
        Assert.Equal(4.759422392871528, BlackScholes.Price(OptionRight.Call, 42, 40, 0.5, 0.10, 0, 0.20), 1e-12);
        Assert.Equal(0.8085993729000958, BlackScholes.Price(OptionRight.Put, 42, 40, 0.5, 0.10, 0, 0.20), 1e-12);
    }

    // Haug, Complete Guide to Option Pricing Formulas (generalized BSM): S=100 K=95 T=0.5 r=10% q=5% sigma=20% -> put 2.4648.
    [Fact]
    public void Haug_dividend_yield_put() => Assert.Equal(2.4647876467558234, BlackScholes.Price(OptionRight.Put, 100, 95, 0.5, 0.10, 0.05, 0.20), 1e-12);

    // Hull Greeks example: S=49 K=50 r=5% sigma=20% T=20 weeks. delta 0.522, gamma 0.066, theta -4.31/yr, vega 12.1, rho 8.91 (per 1.00).
    [Fact]
    public void Hull_textbook_greeks()
    {
        var g = BlackScholes.Greeks(OptionRight.Call, 49, 50, 20.0 / 52, 0.05, 0, 0.20);
        Assert.Equal(2.4005, g.Price, 3e-4);
        Assert.Equal(0.522, g.Delta, 1e-3);
        Assert.Equal(0.066, g.Gamma, 1e-3);
        Assert.Equal(-4.31 / 365, g.Theta, 1e-4);
        Assert.Equal(0.121, g.Vega, 1e-3);
        Assert.Equal(0.0891, g.Rho, 1e-4);
    }

    public static TheoryData<double, double, double, double, double, double> Grid()
    {
        var d = new TheoryData<double, double, double, double, double, double>();
        foreach (var k in new[] { 50.0, 95, 100, 105, 200 })
            foreach (var t in new[] { 1.0 / 365, 0.25, 2.0 })
                foreach (var v in new[] { 0.05, 0.3, 1.5 })
                    d.Add(100, k, t, 0.045, 0.015, v);
        return d;
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Put_call_parity(double s, double k, double t, double r, double q, double v)
    {
        double c = BlackScholes.Price(OptionRight.Call, s, k, t, r, q, v);
        double p = BlackScholes.Price(OptionRight.Put, s, k, t, r, q, v);
        Assert.Equal(s * Math.Exp(-q * t) - k * Math.Exp(-r * t), c - p, 1e-10);
        Assert.True(c >= 0 && p >= 0);
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void Greeks_match_finite_differences(double s, double k, double t, double r, double q, double v)
    {
        foreach (var right in new[] { OptionRight.Call, OptionRight.Put })
        {
            double P(double ss, double tt, double rr, double vv) => BlackScholes.Price(right, ss, k, tt, rr, q, vv);
            var g = BlackScholes.Greeks(right, s, k, t, r, q, v);
            double hd = s * 1e-6, hs = s * 1e-4, hv = 1e-5, hr = 1e-6, ht = Math.Min(1e-6, t / 10);
            Assert.Equal(P(s, t, r, v), g.Price, 1e-12);
            Assert.Equal((P(s + hd, t, r, v) - P(s - hd, t, r, v)) / (2 * hd), g.Delta, 1e-6);
            Assert.Equal((P(s + hs, t, r, v) - 2 * P(s, t, r, v) + P(s - hs, t, r, v)) / (hs * hs), g.Gamma, 2e-4 + 1e-3 * Math.Abs(g.Gamma));
            Assert.Equal((P(s, t, r, v + hv) - P(s, t, r, v - hv)) / (2 * hv) / 100, g.Vega, 1e-6);
            Assert.Equal((P(s, t, r + hr, v) - P(s, t, r - hr, v)) / (2 * hr) / 100, g.Rho, 1e-5);
            Assert.Equal(-(P(s, t + ht, r, v) - P(s, t - ht, r, v)) / (2 * ht) / 365, g.Theta, 1e-5 + 1e-4 * Math.Abs(g.Theta));
        }
    }

    [Fact]
    public void Degenerate_inputs()
    {
        Assert.Equal(5.0, BlackScholes.Price(OptionRight.Call, 105, 100, 0, 0.05, 0, 0.3));
        Assert.Equal(0.0, BlackScholes.Price(OptionRight.Put, 105, 100, -1, 0.05, 0, 0.3));
        Assert.Equal(105 - 100 * Math.Exp(-0.05), BlackScholes.Price(OptionRight.Call, 105, 100, 1, 0.05, 0, 0), 1e-12);
        Assert.True(double.IsNaN(BlackScholes.Price(OptionRight.Call, 0, 100, 1, 0.05, 0, 0.3)));
        Assert.True(double.IsNaN(BlackScholes.Price(OptionRight.Call, 100, double.NaN, 1, 0.05, 0, 0.3)));
        Assert.True(double.IsNaN(BlackScholes.Greeks(OptionRight.Put, -1, 100, 1, 0.05, 0, 0.3).Delta));
        var expired = BlackScholes.Greeks(OptionRight.Put, 90, 100, 0, 0.05, 0, 0.3);
        Assert.Equal(10, expired.Price);
        Assert.Equal(-1, expired.Delta);
    }
}