using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Meili.Core.Settings;
using Meili.Mac.Platform;

namespace Meili.Mac.Tests;

public class ShellTests
{
    private static MainWindow Open()
    {
        MacHost.EnsureComposed();
        var w = new MainWindow();
        w.Show();
        MacHost.Pump();
        return w;
    }

    [AvaloniaFact]
    public void Every_page_opens_and_nothing_is_smaller_than_12px()
    {
        var w = Open();
        try
        {
            foreach (var page in MainWindow.PageOrder)
            {
                w.Navigate(page);
                MacHost.Pump();
                Assert.Equal(page, w.CurrentPage);
                Assert.NotNull(w.CurrentView);
                Assert.Equal(page + "Page", w.CurrentView!.GetType().Name);
                var small = w.CurrentView.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && t.FontSize < 12)
                    .Select(t => $"{t.Text} ({t.FontSize})").ToList();
                Assert.True(small.Count == 0, page + ": " + string.Join(", ", small));
                MacHost.Save(w, $"page-{page}");
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Language_switch_changes_window_and_menu_text()
    {
        var w = Open();
        var loc = AppServices.Get<LocalizationService>();
        try
        {
            w.ToggleLanguage();
            MacHost.Pump();
            var english = loc.Language == "en-US";
            Assert.Equal(LocalizationService.Load(loc.Language)["App_Title"], w.Title);
            w.ToggleLanguage();
            MacHost.Pump();
            Assert.NotEqual(english, loc.Language == "en-US");
            Assert.Equal(LocalizationService.Load(loc.Language)["App_Title"], w.Title);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Theme_switch_follows_the_choice_and_retones_price_colours()
    {
        var w = Open();
        var theme = AppServices.Get<MacTheme>();
        try
        {
            theme.Apply(AppThemeMode.Dark);
            MacHost.Pump();
            Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
            var darkUp = ((ISolidColorBrush)Ui.Platform.Resource("UpBrush")!).Color;
            theme.Apply(AppThemeMode.Light);
            MacHost.Pump();
            Assert.Equal(ThemeVariant.Light, Application.Current.ActualThemeVariant);
            var lightUp = ((ISolidColorBrush)Ui.Platform.Resource("UpBrush")!).Color;
            Assert.NotEqual(darkUp, lightUp);
            Assert.Equal(Color.FromUInt32(SemanticColors.Light.UpDown(UpDownColorMode.RedUp).Up), lightUp);
            theme.ApplyUpDownColors(UpDownColorMode.GreenUp);
            Assert.Equal(Color.FromUInt32(SemanticColors.Light.Green), ((ISolidColorBrush)Ui.Platform.Resource("UpBrush")!).Color);
            theme.ApplyUpDownColors(UpDownColorMode.RedUp);
            theme.Apply(AppThemeMode.System);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Banner_shows_alert_text_in_the_window()
    {
        var w = Open();
        try
        {
            AppServices.Get<MacNotifier>().ShowError("boom");
            MacHost.Pump();
            Assert.True(w.BannerVisible);
            Assert.Contains("boom", w.BannerText);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Keyboard_wizard_opens_from_typing_and_menu()
    {
        var w = Open();
        try
        {
            w.Navigate(AppPage.Dashboard);
            MacHost.Pump();
            w.Activate();
            MacHost.Pump();
            w.KeyTextInput("M");                       // typing anywhere opens the wizard with that letter
            MacHost.Pump();
            Assert.True(w.WizardVisible);
            w.KeyTextInput("SFT");
            MacHost.Pump();
            w.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            MacHost.Pump();
            Assert.False(w.WizardVisible);
            Assert.Equal(AppPage.Charts, w.CurrentPage);
            Assert.Equal("MSFT", AppServices.Get<ChartsViewModel>().SymbolText);

            w.OpenWizard();                           // ⌘F / menu
            MacHost.Pump();
            Assert.True(w.WizardVisible);
            w.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
            MacHost.Pump();
            Assert.False(w.WizardVisible);
        }
        finally { w.Close(); }
    }
}
