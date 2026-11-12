using TyuStock.Analytics;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Abstractions;

namespace TyuStock.App.Tests;

/// <summary>D1: one shared symbol across pages, and option-chain → strategy hand-off.</summary>
public class SymbolContextTests
{
    [Fact]
    public void Context_normalizes_and_only_raises_on_change()
    {
        var ctx = new SymbolContext();
        var raised = new List<string>();
        ctx.Changed += (_, s) => raised.Add(s);
        Assert.True(ctx.Set(" msft "));
        Assert.False(ctx.Set("MSFT"));
        Assert.False(ctx.Set("!!"));
        Assert.Equal(["MSFT"], raised);
        Assert.True(ctx.ShouldAdopt("AAPL"));
        Assert.False(ctx.ShouldAdopt("msft"));
        Assert.False(new SymbolContext().ShouldAdopt("AAPL"));
    }

    [Fact]
    public void Hidden_pages_adopt_the_shared_symbol_when_shown()
    {
        WpfHost.Run(async () =>
        {
            var ctx = AppServices.Get<SymbolContext>();
            var charts = AppServices.Get<ChartsViewModel>();
            charts.SymbolText = "AAPL";
            await charts.LoadAsync();                       // sets context to AAPL
            Assert.Equal("AAPL", ctx.Current);
            ctx.Set("NVDA");                                // e.g. watchlist "option chain" on NVDA
            var news = AppServices.Get<NewsViewModel>();
            await news.ActivateAsync();
            Assert.Equal("NVDA", news.SymbolText);
            news.Deactivate();
            await charts.ActivateAsync();
            Assert.Equal("NVDA", charts.SymbolText);
            Assert.Equal("NVDA", charts.Series!.Symbol);
            charts.Deactivate();
        });
    }

    [Fact]
    public void Switching_symbol_on_options_does_not_reuse_the_old_expiration()
    {
        WpfHost.Run(async () =>
        {
            var fake = (FakeMarketDataService)AppServices.Get<IMarketDataService>();
            var vm = AppServices.Get<OptionsViewModel>();
            vm.SymbolText = "AAPL";
            await vm.LoadAsync();
            vm.SelectedExpiration = vm.Expirations[^1];     // user picks the later expiry
            await vm.LoadAsync();
            vm.SymbolText = "TSLA";
            lock (fake.ChainRequests) fake.ChainRequests.Clear();
            await vm.LoadAsync();
            lock (fake.ChainRequests) Assert.Equal(("TSLA", (DateOnly?)null), fake.ChainRequests[0]);
            vm.Deactivate();
        });
    }

    [Fact]
    public void Option_chain_row_seeds_a_custom_strategy_leg()
    {
        WpfHost.Run(async () =>
        {
            var options = AppServices.Get<OptionsViewModel>();
            options.SymbolText = "AMZN";
            await options.LoadAsync();
            Assert.Null(options.BuildSeed("SP"));           // nothing selected
            options.SelectedRow = options.Rows.First(r => r.Strike == 120);
            var seed = options.BuildSeed("SP")!;
            Assert.Equal(("AMZN", TyuStock.Core.Models.OptionRight.Put, PositionSide.Short, 120.0), (seed.Symbol, seed.Right, seed.Side, seed.Strike));
            Assert.Null(options.BuildSeed("XX"));
            options.Deactivate();

            var strategy = AppServices.Get<StrategyViewModel>();
            strategy.Seed(seed);
            await strategy.ActivateAsync();
            var leg = Assert.Single(strategy.Legs);
            Assert.Equal(LegType.Put, leg.Type);
            Assert.Equal(PositionSide.Short, leg.Side);
            Assert.Equal("120", leg.StrikeText);
            Assert.NotEqual("0", leg.PremiumText);          // filled from the chain
            Assert.Equal(StrategyKind.Custom, strategy.SelectedPreset!.Kind);
            Assert.Equal(seed.Expiration, strategy.SelectedExpiration!.Date);
            strategy.Deactivate();
        });
    }
}
