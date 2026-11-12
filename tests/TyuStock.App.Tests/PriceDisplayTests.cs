using TyuStock.App.Services;
using TyuStock.Core.Models;
using TyuStock.Core.Alerts;

namespace TyuStock.App.Tests;

public class PriceDisplayTests
{
    [Fact]
    public void Alert_thresholds_keep_low_price_precision_and_ignore_culture()
    {
        Assert.Equal("0.1234", NotificationService.FormatValue(AlertKind.PriceAbove, 0.1234));
        Assert.Equal("1,234.50", NotificationService.FormatValue(AlertKind.PriceBelow, 1234.5));
        Assert.Equal("2.5", NotificationService.FormatValue(AlertKind.PercentUp, 2.5));
    }
}
