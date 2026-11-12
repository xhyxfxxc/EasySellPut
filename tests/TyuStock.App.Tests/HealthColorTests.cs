using System.Windows;
using System.Windows.Media;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Settings;
using TyuStock.Data.Routing;

namespace TyuStock.App.Tests;

/// <summary>E1: health colours never coincide with up/down colours, and each state has its own icon.</summary>
public class HealthColorTests
{
    [Fact]
    public void Health_brushes_differ_from_both_up_down_conventions()
    {
        WpfHost.Run(() =>
        {
            Color C(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            var health = new[] { "HealthyBrush", "DegradedBrush", "DownStateBrush", "UnknownBrush" }.Select(C).ToList();
            Assert.Equal(4, health.Distinct().Count());
            try
            {
                foreach (var mode in new[] { UpDownColorMode.RedUp, UpDownColorMode.GreenUp })
                {
                    ThemeService.ApplyUpDownColors(mode);
                    Assert.DoesNotContain(C("UpBrush"), health);
                    Assert.DoesNotContain(C("DownBrush"), health);
                }
            }
            finally { ThemeService.ApplyUpDownColors(AppServices.Get<SettingsStore>().Current.EffectiveUpDownColors()); }
        });
    }

    [Fact]
    public void Every_state_has_a_distinct_icon()
    {
        var icons = Enum.GetValues<HealthState>().Select(HealthVisuals.Icon).ToList();
        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Fact]
    public void Settings_choice_marks_colours_as_chosen()
    {
        WpfHost.Run(() =>
        {
            var vm = AppServices.Get<SettingsViewModel>();
            var store = AppServices.Get<SettingsStore>();
            var before = store.Current.UpDownColors;
            vm.UpDownIndex = vm.UpDownIndex == 0 ? 1 : 0;
            Assert.True(store.Current.UpDownColorsChosen);
            store.Update(s => { s.UpDownColors = before; s.UpDownColorsChosen = false; });
            ThemeService.ApplyUpDownColors(store.Current.EffectiveUpDownColors());
        });
    }
}
