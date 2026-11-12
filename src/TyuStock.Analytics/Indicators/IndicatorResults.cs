namespace TyuStock.Analytics;

// Multi-output indicator results (array-returning convenience overloads).
// Every array has the same length as the input; warm-up positions are NaN.

public sealed record MacdSeries(double[] Macd, double[] Signal, double[] Histogram);

public sealed record BollingerSeries(double[] Middle, double[] Upper, double[] Lower)
{
    /// <summary>%B = (price - lower) / (upper - lower); NaN where bands are undefined or have zero width.</summary>
    public double[] PercentB(ReadOnlySpan<double> price)
    {
        var r = new double[Middle.Length];
        for (int i = 0; i < r.Length; i++)
        {
            double w = Upper[i] - Lower[i];
            r[i] = i < price.Length && w > 0 ? (price[i] - Lower[i]) / w : double.NaN;
        }
        return r;
    }

    /// <summary>Bandwidth = (upper - lower) / middle.</summary>
    public double[] Bandwidth()
    {
        var r = new double[Middle.Length];
        for (int i = 0; i < r.Length; i++) r[i] = Middle[i] != 0 ? (Upper[i] - Lower[i]) / Middle[i] : double.NaN;
        return r;
    }
}

public sealed record StochasticSeries(double[] K, double[] D);

public sealed record KdjSeries(double[] K, double[] D, double[] J);