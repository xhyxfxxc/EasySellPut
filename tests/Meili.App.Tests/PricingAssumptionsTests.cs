using Meili.App.Services;

namespace Meili.App.Tests;

/// <summary>H1: the options and strategy pages price with the same shared rate and dividend yield.</summary>
public class PricingAssumptionsTests
{
    [Fact]
    public void Options_and_strategy_share_one_set_of_pricing_inputs()
    {
        WpfHost.Run(async () =>
        {
            var pricing = AppServices.Get<PricingAssumptions>();
            var inputs = await pricing.ForAsync("MSFT", "test", CancellationToken.None);
            Assert.Equal(0.01, inputs.DividendYield);                   // from the (fake) fundamentals
            var e = inputs.Enrichment(DateTimeOffset.UnixEpoch);
            Assert.Equal((inputs.Rate.Rate, 0.01, DateTimeOffset.UnixEpoch), (e.RiskFreeRate, e.DividendYield, e.Now));

            var options = AppServices.Get<OptionsViewModel>();
            var strategy = AppServices.Get<StrategyViewModel>();
            try
            {
                options.SymbolText = strategy.SymbolText = "MSFT";
                await options.LoadAsync();
                await strategy.LoadAsync();
                Assert.Equal(PricingText.Model(inputs.Rate, AppServices.Get<LocalizationService>()), options.ModelText);
                Assert.Equal(options.ModelText, strategy.ModelText);
            }
            finally
            {
                options.Deactivate();
                strategy.Deactivate();
            }
        });
    }
}
