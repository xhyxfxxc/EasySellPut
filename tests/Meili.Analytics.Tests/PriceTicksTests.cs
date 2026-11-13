namespace Meili.Analytics.Tests;

public class PriceTicksTests
{
    [Theory]
    [InlineData(0.4, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.6, 1)]
    [InlineData(1, 1)]
    [InlineData(1.1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 10)]
    [InlineData(12, 20)]
    [InlineData(0.05, 0.05)]
    public void Nice_uses_half_one_two_five_series(double raw, double expected)
        => Assert.Equal(expected, PriceTicks.Nice(raw), 12);

    [Fact]
    public void Step_widens_for_sparse_and_tightens_for_dense()
    {
        double sparse = PriceTicks.Step(40, 80, TickDensity.Sparse);
        double medium = PriceTicks.Step(40, 80, TickDensity.Medium);
        double dense = PriceTicks.Step(40, 80, TickDensity.Dense);
        Assert.True(sparse >= medium);
        Assert.True(medium >= dense);
        Assert.Contains(medium, new[] { 0.5, 1.0, 2.0, 5.0, 10.0 });
        // A $40 window around typical equity prices should land on 0.5 / 1 / 2 / 5, not 0.01 or 50.
        Assert.InRange(medium, 0.5, 5);
    }

    [Fact]
    public void Snap_lands_on_the_grid_and_stays_inside_the_range()
    {
        Assert.Equal(63, PriceTicks.Snap(63.2, 1, 40, 80));
        Assert.Equal(63.5, PriceTicks.Snap(63.4, 0.5, 40, 80));
        Assert.Equal(40, PriceTicks.Snap(10, 1, 40, 80));
        Assert.Equal(80, PriceTicks.Snap(99, 1, 40, 80));
        Assert.Equal(62, PriceTicks.Snap(62, 1, 40, 80));
    }

    [Fact]
    public void Marks_include_the_range_and_step_on_the_grid()
    {
        var marks = PriceTicks.Marks(40.2, 47.8, 1);
        Assert.Equal(40.2, marks[0]);
        Assert.Equal(47.8, marks[^1]);
        Assert.Contains(41.0, marks);
        Assert.Contains(47.0, marks);
        for (int i = 1; i < marks.Length - 1; i++)
            Assert.Equal(0, marks[i] % 1, 9);
    }
}
