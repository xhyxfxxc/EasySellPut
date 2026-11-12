using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Tests;

/// <summary>G4 + H1: data-driven T-table columns, spread, OI bars, distance to spot.</summary>
public class OptionTableTests
{
    [Fact]
    public void Columns_mirror_around_the_strike_and_follow_the_toggles()
    {
        var all = new OptionColumnSet();
        var calls = OptionColumns.Side(true, all).Select(c => c.Key).ToList();
        var puts = OptionColumns.Side(false, all).Select(c => c.Key).ToList();
        Assert.Equal(["Volume", "Oi", "Iv", "Delta", "Gamma", "Theta", "Vega", "SpreadPct", "Bid", "Ask", "Last"], calls);
        Assert.Equal(Enumerable.Reverse(calls), puts);
        Assert.Equal(["Strike", "Distance"], OptionColumns.Middle(all).Select(c => c.Key));

        var lean = new OptionColumnSet(Volume: false, Gamma: false, Spread: false, Distance: false);
        Assert.DoesNotContain("Volume", OptionColumns.Side(true, lean).Select(c => c.Key));
        Assert.DoesNotContain("SpreadPct", OptionColumns.Side(false, lean).Select(c => c.Key));
        Assert.Equal(["Strike"], OptionColumns.Middle(lean).Select(c => c.Key));
        Assert.True(OptionColumns.TotalWidth(all with { Compact = true }) < OptionColumns.TotalWidth(all));
    }

    [Theory]
    [InlineData(2.00, 2.20, false)]   // 9.5 % → fine
    [InlineData(0.10, 0.20, true)]    // 67 % and 0.10 wide
    [InlineData(0.01, 0.03, false)]   // huge %, but only 0.02 wide
    public void Wide_spread_rule(double bid, double ask, bool wide)
    {
        var (spread, pct, isWide) = EnrichedSide.SpreadOf(bid, ask);
        Assert.Equal(ask - bid, spread!.Value, 9);
        Assert.Equal((ask - bid) / ((ask + bid) / 2), pct!.Value, 9);
        Assert.Equal(wide, isWide);
        Assert.Equal((null, null, false), EnrichedSide.SpreadOf(null, 1));
    }

    [Fact]
    public void Distance_to_spot_and_oi_ratios()
    {
        var row = new OptionStrikeRow(105, null, null, false, 100);
        Assert.Equal("+5.0%", row.DistanceText);
        Assert.Equal("-10.0%", new OptionStrikeRow(90, null, null, false, 100).DistanceText);
        Assert.Equal("—", new OptionStrikeRow(90, null, null, false, null).DistanceText);

        var rows = new[]
        {
            new OptionStrikeRow(100, new EnrichedSide { OiValue = 500 }, new EnrichedSide { OiValue = 40 }, false, 100),
            new OptionStrikeRow(105, new EnrichedSide { OiValue = 1000 }, new EnrichedSide { OiValue = null }, false, 100),
        };
        OptionStrikeRow.SetOiRatios(rows);
        Assert.Equal(0.5, rows[0].Call!.OiRatio);
        Assert.Equal(1.0, rows[1].Call!.OiRatio);
        Assert.Equal(1.0, rows[0].Put!.OiRatio);
        Assert.Equal(0.0, rows[1].Put!.OiRatio);
    }

    [Fact]
    public void Loaded_chain_has_spreads_and_distances()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<OptionsViewModel>();
            try
            {
                vm.SymbolText = "MSFT";
                await vm.LoadAsync();
                var atm = vm.Rows.First(r => r.Strike == 125);
                Assert.Equal("0.20", atm.Call!.Spread);
                Assert.NotEqual("—", atm.Call.SpreadPct);
                Assert.Equal("+1.3%", atm.DistanceText);                 // (125 − 123.45) / 123.45
                Assert.All(vm.Rows, r => Assert.Equal(1.0, r.Call!.OiRatio)); // fake chain: equal OI everywhere
            }
            finally { vm.Deactivate(); }
        });
    }
}
