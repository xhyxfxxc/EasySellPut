using System.Windows.Input;
using Meili.App.Services;
using Meili.App.Views.Pages;

namespace Meili.App.Tests;

/// <summary>D2: keyboard map.</summary>
public class KeyboardShortcutTests
{
    [Fact]
    public void Ctrl_digits_map_to_pages_in_navigation_order()
    {
        Assert.Equal(typeof(DashboardPage), KeyboardShortcuts.PageFor(Key.D1, ModifierKeys.Control));
        Assert.Equal(typeof(ChartsPage), KeyboardShortcuts.PageFor(Key.NumPad3, ModifierKeys.Control));
        Assert.Equal(typeof(SettingsPage), KeyboardShortcuts.PageFor(Key.D9, ModifierKeys.Control));
        Assert.Null(KeyboardShortcuts.PageFor(Key.D0, ModifierKeys.Control));
        Assert.Null(KeyboardShortcuts.PageFor(Key.D1, ModifierKeys.None));
        Assert.Null(KeyboardShortcuts.PageFor(Key.D1, ModifierKeys.Control | ModifierKeys.Alt));
        Assert.Equal(9, KeyboardShortcuts.PageOrder.Distinct().Count());
    }

    [Fact]
    public void Typing_outside_text_boxes_starts_the_wizard()
    {
        Assert.True(KeyboardShortcuts.StartsWizard("a", false));
        Assert.True(KeyboardShortcuts.StartsWizard("6", false));
        Assert.True(KeyboardShortcuts.StartsWizard("^", false));
        Assert.False(KeyboardShortcuts.StartsWizard("a", true));
        Assert.False(KeyboardShortcuts.StartsWizard(" ", false));
        Assert.False(KeyboardShortcuts.StartsWizard("\r", false));
        Assert.False(KeyboardShortcuts.StartsWizard("中", false));
        Assert.False(KeyboardShortcuts.StartsWizard("", false));
    }

    [Fact]
    public void F5_finds_each_page_refresh_command()
    {
        WpfHost.Run(() =>
        {
            Assert.NotNull(KeyboardShortcuts.RefreshCommandOf(AppServices.Get<ChartsViewModel>()));
            Assert.NotNull(KeyboardShortcuts.RefreshCommandOf(AppServices.Get<WatchlistViewModel>()));
            Assert.NotNull(KeyboardShortcuts.RefreshCommandOf(AppServices.Get<AlertsViewModel>()));
            Assert.Null(KeyboardShortcuts.RefreshCommandOf(new object()));
            Assert.Null(KeyboardShortcuts.RefreshCommandOf(null));
        });
    }
}
