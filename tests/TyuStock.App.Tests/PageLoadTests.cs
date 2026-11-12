using System.Windows;
using System.Windows.Controls;
using TyuStock.App.Services;
using TyuStock.App.Views.Pages;

namespace TyuStock.App.Tests;

/// <summary>
/// Every page must construct (XAML parse + StaticResource lookup) and lay out (templates, styles, bindings)
/// in both languages and both themes. This is the regression net for M7-style XAML crashes.
/// </summary>
public class PageLoadTests
{
    public static TheoryData<Type> Pages => new()
    {
        typeof(DashboardPage), typeof(WatchlistPage), typeof(ChartsPage), typeof(OptionsPage), typeof(StrategyPage),
        typeof(NewsPage), typeof(AlertsPage), typeof(DataSourcesPage), typeof(SettingsPage),
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void Page_loads_and_lays_out_in_every_language_and_theme(Type pageType)
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            foreach (var lang in new[] { "zh-CN", "en-US" })
            {
                loc.Apply(lang);
                foreach (var dark in new[] { true, false })
                {
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None, false);
                    var page = (Page)Activator.CreateInstance(pageType)!;
                    Layout(page);
                    await WpfHost.Settle();
                    Layout(page);
                }
            }
            loc.Apply("zh-CN");
        });
    }

    internal static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(1280, 800));
        e.Arrange(new Rect(0, 0, 1280, 800));
        e.UpdateLayout();
    }
}
