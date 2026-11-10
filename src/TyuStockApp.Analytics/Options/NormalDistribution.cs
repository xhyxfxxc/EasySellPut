namespace TyuStockApp.Analytics;

/// <summary>Standard normal distribution.</summary>
public static class NormalDistribution
{
    private const double InvSqrt2Pi = 0.39894228040143267794;

    /// <summary>Probability density φ(x).</summary>
    public static double Pdf(double x) => InvSqrt2Pi * Math.Exp(-0.5 * x * x);

    /// <summary>
    /// Cumulative distribution Φ(x), double-precision accurate (Hart 1968 / West 2005, |error| &lt; 1e-14);
    /// the tails are computed directly (no 1 − Φ cancellation), so deep-OTM option values stay accurate.
    /// </summary>
    public static double Cdf(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        double ax = Math.Abs(x);
        double c;
        if (ax > 37.0)
        {
            c = 0.0;
        }
        else
        {
            double e = Math.Exp(-ax * ax / 2.0);
            if (ax < 7.07106781186547)
            {
                double b = 3.52624965998911E-02 * ax + 0.700383064443688;
                b = b * ax + 6.37396220353165;
                b = b * ax + 33.912866078383;
                b = b * ax + 112.079291497871;
                b = b * ax + 221.213596169931;
                b = b * ax + 220.206867912376;
                c = e * b;
                b = 8.83883476483184E-02 * ax + 1.75566716318264;
                b = b * ax + 16.064177579207;
                b = b * ax + 86.7807322029461;
                b = b * ax + 296.564248779674;
                b = b * ax + 637.333633378831;
                b = b * ax + 793.826512519948;
                b = b * ax + 440.413735824752;
                c /= b;
            }
            else
            {
                double b = ax + 0.65;
                b = ax + 4.0 / b;
                b = ax + 3.0 / b;
                b = ax + 2.0 / b;
                b = ax + 1.0 / b;
                c = e / b / 2.506628274631;
            }
        }
        return x > 0 ? 1.0 - c : c;
    }
}