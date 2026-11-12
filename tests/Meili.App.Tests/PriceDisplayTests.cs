using Meili.App.Services;
using Meili.Core.Models;
using Meili.Core.Alerts;

namespace Meili.App.Tests;

public class PriceDisplayTests
{
    [Fact]
    public void Alert_thresholds_keep_low_price_precision_and_ignore_culture()
    {
        Assert.Equal("0.1234", AlertText.FormatValue(AlertKind.PriceAbove, 0.1234));
        Assert.Equal("1,234.50", AlertText.FormatValue(AlertKind.PriceBelow, 1234.5));
        Assert.Equal("2.5", AlertText.FormatValue(AlertKind.PercentUp, 2.5));
    }
}
