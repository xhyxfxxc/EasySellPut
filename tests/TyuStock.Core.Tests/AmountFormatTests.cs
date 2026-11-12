using TyuStock.Core.Models;

namespace TyuStock.Core.Tests;

/// <summary>G2: one volume/amount format — 万/亿 in Chinese, K/M/B in English.</summary>
public class AmountFormatTests
{
    [Theory]
    [InlineData(9_999, "9,999", "9,999")]
    [InlineData(12_345, "1.23万", "12.35K")]
    [InlineData(45_678_901, "4567.89万", "45.68M")]
    [InlineData(123_456_789, "1.23亿", "123.46M")]
    [InlineData(2_500_000_000, "25.00亿", "2.50B")]
    [InlineData(3.4e12, "3.40万亿", "3.40T")]
    [InlineData(-12_500_000, "-1250.00万", "-12.50M")]
    [InlineData(12.34, "12.34", "12.34")]
    [InlineData(0, "0", "0")]
    public void Formats_by_language(double v, string zh, string en)
    {
        Assert.Equal(zh, NumberDisplay.Amount(v, chinese: true));
        Assert.Equal(en, NumberDisplay.Amount(v, chinese: false));
    }

    [Fact]
    public void Missing_values_use_the_placeholder()
    {
        Assert.Equal(NumberDisplay.Empty, NumberDisplay.Amount(null, true));
        Assert.Equal("-", NumberDisplay.Amount(double.NaN, false, "-"));
    }
}
