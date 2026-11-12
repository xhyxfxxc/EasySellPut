using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Settings;

namespace TyuStock.App.Tests;

/// <summary>F5: the options page states the pricing model and where the rate came from.</summary>
public class PricingLabelTests
{
    [Fact]
    public void Options_label_follows_the_rate_setting()
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var settings = AppServices.Get<SettingsStore>();
            var vm = AppServices.Get<OptionsViewModel>();
            try
            {
                settings.Update(s => { s.Pricing.RateMode = RiskFreeRateMode.Fixed; s.Pricing.FixedRatePercent = 3.25; });
                vm.SymbolText = "MSFT";
                await vm.LoadAsync();
                Assert.Contains("3.25%", vm.ModelText);
                Assert.Contains(loc["Px_RateFixed"], vm.ModelText);
                Assert.Contains("BSM", vm.ModelText);
            }
            finally
            {
                settings.Update(s => { s.Pricing.RateMode = RiskFreeRateMode.Treasury13Week; s.Pricing.FixedRatePercent = 4.0; });
                vm.Deactivate();
            }
        });
    }

    [Fact]
    public void Settings_page_validates_the_fixed_rate()
    {
        WpfHost.Run(() =>
        {
            var vm = AppServices.Get<SettingsViewModel>();
            var store = AppServices.Get<SettingsStore>();
            vm.FixedRateText = "abc";
            Assert.NotEqual("", vm.FixedRateError);
            vm.FixedRateText = "4.5";
            Assert.Equal("", vm.FixedRateError);
            Assert.Equal(4.5, store.Current.Pricing.FixedRatePercent);
            vm.FixedRateText = "4";
        });
    }
}
