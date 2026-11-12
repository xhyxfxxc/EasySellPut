using Meili.Core.Models;

namespace Meili.Core.Tests;

/// <summary>G1: price precision follows the price level.</summary>
public class PriceFormatTests
{
    [Theory]
    [InlineData(0.1234, "0.1234")]
    [InlineData(0.05, "0.05")]
    [InlineData(0.00125, "0.0013")]
    [InlineData(0.5, "0.50")]
    [InlineData(5.125, "5.125")]
    [InlineData(5.1, "5.10")]
    [InlineData(123.456, "123.46")]
    [InlineData(1234.5, "1,234.50")]
    [InlineData(-0.4321, "-0.4321")]
    public void Price_precision_by_level(double v, string expected) => Assert.Equal(expected, NumberDisplay.Price(v));

    [Fact]
    public void Missing_prices_show_a_dash() => Assert.Equal("\u2014", NumberDisplay.Price(double.NaN));

    [Theory]
    [InlineData(0.0125, 0.41, "+0.0125")]
    [InlineData(-0.0125, 0.41, "-0.0125")]
    [InlineData(-1.5, 180.2, "-1.50")]
    [InlineData(0, 180.2, "0.00")]
    [InlineData(12.3, 2345, "+12.30")]
    public void Change_uses_the_precision_of_its_price(double change, double price, string expected) =>
        Assert.Equal(expected, NumberDisplay.SignedChange(change, price));
}
