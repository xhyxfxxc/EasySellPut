using TyuStock.Core.Models;

namespace TyuStock.Core.Tests;

public sealed class StatementTableTests
{
    [Fact]
    public void Pivots_newest_first_and_skips_empty_items()
    {
        var lines = new FinancialLine[]
        {
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2026, 9, 26), 400e9),
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2025, 9, 27), 390e9),
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2024, 9, 28), 380e9),
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2023, 9, 30), 370e9),
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2022, 9, 24), 360e9),
            new(FinancialStatementKind.Income, FinancialItems.NetIncome, new(2026, 9, 26), 90e9),
            new(FinancialStatementKind.Income, FinancialItems.NetIncome, new(2025, 9, 27), 80e9),
            new(FinancialStatementKind.Income, FinancialItems.DilutedEps, new(2026, 9, 26), 6.5),
            new(FinancialStatementKind.Balance, FinancialItems.TotalAssets, new(2026, 9, 26), 1e12),
            new(FinancialStatementKind.Income, FinancialItems.TotalRevenue, new(2026, 6, 27), 90e9, Annual: false),
        };

        var table = StatementTable.Build(lines, FinancialStatementKind.Income, annual: true, maxPeriods: 4);
        Assert.Equal(new[] { new DateOnly(2026, 9, 26), new DateOnly(2025, 9, 27), new DateOnly(2024, 9, 28), new DateOnly(2023, 9, 30) }, table.Periods);
        Assert.DoesNotContain(table.Rows, r => r.Item == FinancialItems.GrossProfit);
        Assert.DoesNotContain(table.Rows, r => r.Item == FinancialItems.OperatingIncome);
        var rev = table.Rows.Single(r => r.Item == FinancialItems.TotalRevenue);
        Assert.Equal(new double?[] { 400e9, 390e9, 380e9, 370e9 }, rev.Values.ToArray());
        var eps = table.Rows.Single(r => r.Item == FinancialItems.DilutedEps);
        Assert.Equal(6.5, eps.Values[0]);
        Assert.Null(eps.Values[1]);
        Assert.False(table.IsEmpty);

        var q = StatementTable.Build(lines, FinancialStatementKind.Income, annual: false, maxPeriods: 4);
        Assert.Equal(new[] { new DateOnly(2026, 6, 27) }, q.Periods);
        Assert.Equal(90e9, q.Rows.Single().Values[0]);

        var empty = StatementTable.Build(lines, FinancialStatementKind.CashFlow);
        Assert.True(empty.IsEmpty);
    }

    [Fact]
    public void Number_display_compacts_and_formats_percents()
    {
        Assert.Equal("1.23T", NumberDisplay.Compact(1.234e12));
        Assert.Equal("478.83B", NumberDisplay.Compact(478_834_000_000));
        Assert.Equal("-12.50M", NumberDisplay.Compact(-12_500_000));
        Assert.Equal("1,234", NumberDisplay.Compact(1234));
        Assert.Equal("12.34", NumberDisplay.Compact(12.34));
        Assert.Equal(NumberDisplay.Empty, NumberDisplay.Compact(null));
        Assert.Equal("0.55%", NumberDisplay.Percent(0.0055));
        Assert.Equal("167.66 – 277.32", NumberDisplay.Range(167.66, 277.32));
        Assert.Equal("12,345", NumberDisplay.Integer(12345.4));
    }
}