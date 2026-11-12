using TyuStock.App.Services;
using TyuStock.Core.Models;
using TyuStock.Core.Time;

namespace TyuStock.App.Tests;

public class PricePlanTests
{
    private static readonly DateTimeOffset T0 = MarketClock.FromEastern(new DateTime(2026, 11, 11, 9, 30, 0));

    [Fact]
    public void Contradictory_and_missing_bars_become_close_points_never_candles()
    {
        // Low above Open would make ScottPlot throw if it reached the candlestick plot (seen in live Yahoo data).
        var raw = new Bar(T0, 10, 11, 10.2, 10.6, 1); // unflagged on purpose: the painter re-checks
        var plan = PricePlan.Build([raw, Bar.CloseOnly(T0.AddMinutes(1), 10.7, 1), Bar.FromSource(T0.AddMinutes(2), 10, 11, 9, 10.5, 1)]);
        Assert.Single(plan.Candles);
        Assert.Equal([10.6, 10.7], plan.CloseY);
        Assert.False(plan.LineOnly);
    }

    [Fact]
    public void All_close_only_series_draws_as_a_line()
    {
        var plan = PricePlan.Build([Bar.CloseOnly(T0, 1, 0), Bar.CloseOnly(T0.AddMinutes(1), 2, 0)]);
        Assert.True(plan.LineOnly);
        Assert.Empty(plan.Candles);
    }
}
