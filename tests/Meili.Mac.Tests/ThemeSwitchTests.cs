using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Meili.Core.Settings;
using Meili.Mac.Platform;

namespace Meili.Mac.Tests;

/// <summary>
/// The 0.4.1 Windows defect, checked on the Mac UI: after switching dark/light (or red/green, or the language) at
/// runtime, every page must look the same as when the app was started in that state. Each page is checked while
/// visible and while hidden behind another page at the moment of the switch. Off-screen headless renders only.
/// </summary>
public class ThemeSwitchTests
{
    /// <summary>Theme-dependent brushes the pages draw with; resolved at the page so local copies would show up.</summary>
    private static readonly string[] Keys =
    [
        "ApplicationBackgroundBrush", "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "AccentFillColorDefaultBrush",
        "TextOnAccentFillColorPrimaryBrush", "ControlFillColorSecondaryBrush", "CardBackgroundFillColorDefaultBrush",
        "UpBrush", "DownBrush", "UpBackgroundBrush", "DownBackgroundBrush", "HealthyBrush", "DegradedBrush", "DownStateBrush",
        "RowHighlightBrush", "ItmBrush",
    ];

    /// <summary>Per-channel tolerance: low enough for the faint ITM / tag tints (a 6–9% overlay is ~20 levels apart).</summary>
    private const int Tolerance = 8;

    public static TheoryData<AppPage, bool, bool> Cases()
    {
        var d = new TheoryData<AppPage, bool, bool>();
        foreach (var page in MainWindow.PageOrder)
            foreach (var toDark in new[] { true, false })
                foreach (var hidden in new[] { false, true })
                    d.Add(page, toDark, hidden);
        return d;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Runtime_theme_switch_matches_a_fresh_start(AppPage page, bool toDark, bool hiddenDuringSwitch)
    {
        MacHost.EnsureComposed();
        var theme = AppServices.Get<MacTheme>();
        var from = toDark ? AppThemeMode.Light : AppThemeMode.Dark;
        var to = toDark ? AppThemeMode.Dark : AppThemeMode.Light;
        try
        {
            var (switchedPixels, switchedBrushes) = Render(page, from, () => theme.Apply(to), hiddenDuringSwitch);
            Assert.Equal(toDark ? ThemeVariant.Dark : ThemeVariant.Light, Application.Current!.ActualThemeVariant);
            var (freshPixels, freshBrushes) = Render(page, to, null, false);
            AssertSame(page, $"{from}→{to}{(hiddenDuringSwitch ? " (hidden)" : "")}", switchedBrushes, freshBrushes, switchedPixels, freshPixels);
        }
        finally { theme.Apply(AppThemeMode.System); }
    }

    [AvaloniaTheory]
    [InlineData(AppPage.Dashboard)]
    [InlineData(AppPage.Watchlist)]
    [InlineData(AppPage.Charts)]
    [InlineData(AppPage.News)]
    public void Red_green_switch_matches_a_fresh_start(AppPage page)
    {
        MacHost.EnsureComposed();
        var theme = AppServices.Get<MacTheme>();
        try
        {
            var (sp, sb) = Render(page, AppThemeMode.Light, () => theme.ApplyUpDownColors(UpDownColorMode.GreenUp), false);
            var (fp, fb) = Render(page, AppThemeMode.Light, null, false);   // GreenUp is still in effect: a "fresh start" with it
            AssertSame(page, "RedUp→GreenUp", sb, fb, sp, fp);
            Assert.Equal(Color.FromUInt32(SemanticColors.Light.Green), ((ISolidColorBrush)sb["UpBrush"]!).Color);
        }
        finally
        {
            theme.ApplyUpDownColors(UpDownColorMode.RedUp);
            theme.Apply(AppThemeMode.System);
        }
    }

    public static TheoryData<AppPage> Pages()
    {
        var d = new TheoryData<AppPage>();
        foreach (var p in MainWindow.PageOrder) d.Add(p);
        return d;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Pages))]
    public void Language_switch_matches_a_fresh_start(AppPage page)
    {
        MacHost.EnsureComposed();
        var loc = AppServices.Get<LocalizationService>();
        var original = loc.Language;
        var other = original == "zh-CN" ? "en-US" : "zh-CN";
        try
        {
            var (sp, sb) = Render(page, AppThemeMode.Light, () => SetLanguage(other), false);
            var (fp, fb) = Render(page, AppThemeMode.Light, null, false);
            AssertSame(page, original + "→" + other, sb, fb, sp, fp);
        }
        finally
        {
            SetLanguage(original);
            AppServices.Get<MacTheme>().Apply(AppThemeMode.System);
        }
    }

    private static void SetLanguage(string language) => AppServices.Get<LocalizationService>().Apply(language);

    /// <summary>
    /// Opens a window in <paramref name="start"/>, shows <paramref name="page"/>, applies <paramref name="change"/>
    /// (optionally while another page is in front), returns to the page and renders it.
    /// </summary>
    private static (byte[] Pixels, Dictionary<string, object?> Brushes) Render(AppPage page, AppThemeMode start, Action? change, bool hidden)
    {
        MacHost.EnsureComposed();
        AppServices.Get<MacTheme>().Apply(start);
        MacHost.Pump();
        var w = new MainWindow { Width = 1100, Height = 720 };
        try
        {
            w.Show();
            w.Navigate(page);
            MacHost.Pump(10);
            if (change is not null)
            {
                if (hidden) w.Navigate(page == AppPage.Settings ? AppPage.Dashboard : AppPage.Settings);
                MacHost.Pump();
                change();
                MacHost.Pump();
                w.Navigate(page);
            }
            var view = w.CurrentView!;
            Settle(view.DataContext);
            // Row selection is user state that survives re-attachment, not theme state: compare without it.
            foreach (var grid in view.GetVisualDescendants().OfType<DataGrid>()) grid.SelectedItem = null;
            MacHost.Pump(15);
            var brushes = Keys.ToDictionary(k => k, k => view.TryFindResource(k, view.ActualThemeVariant, out var v) ? v : null);
            using var frame = w.CaptureRenderedFrame()!;
            if (Environment.GetEnvironmentVariable("MEILI_RENDER_DIR") is { Length: > 0 } dir)
                frame.Save(Path.Combine(dir, $"mac-theme-{page}-{start}-{(change is null ? "fresh" : hidden ? "switched-hidden" : "switched")}.png"));
            return (Pixels(frame), brushes);
        }
        finally { w.Close(); }
    }

    /// <summary>Waits for the page's own loading / source check to finish so both renders show the same data.</summary>
    private static void Settle(object? vm)
    {
        var busy = new[] { "IsBusy", "IsTesting" }.Select(n => vm?.GetType().GetProperty(n)).Where(p => p is not null).ToList();
        for (var i = 0; i < 3000 && busy.Any(p => p!.GetValue(vm) is true); i++)
        {
            Thread.Sleep(10);
            MacHost.Pump(1);
        }
    }

    private static byte[] Pixels(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void AssertSame(AppPage page, string what, Dictionary<string, object?> switched, Dictionary<string, object?> fresh, byte[] a, byte[] b)
    {
        foreach (var key in Keys)
            Assert.True(Equals(ColorOf(switched[key]), ColorOf(fresh[key])), $"{page} {what}: {key} is {ColorOf(switched[key])}, fresh start has {ColorOf(fresh[key])}");
        Assert.Equal(b.Length, a.Length);
        var differing = 0;
        for (var i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) > Tolerance || Math.Abs(a[i + 1] - b[i + 1]) > Tolerance || Math.Abs(a[i + 2] - b[i + 2]) > Tolerance) differing++;
        var share = differing / (a.Length / 4.0);
        Assert.True(share <= 0.01, $"{page} {what}: {share:P1} of pixels differ from a fresh start");
    }

    private static Color? ColorOf(object? brush) => (brush as ISolidColorBrush)?.Color;
}
