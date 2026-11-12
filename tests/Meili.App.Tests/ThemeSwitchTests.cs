using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Meili.App.Services;
using Meili.Core.Settings;

namespace Meili.App.Tests;

/// <summary>
/// 0.4.1: a runtime theme switch must look exactly like a fresh start in that theme. Each page is rendered off-screen
/// (hidden HwndSource + RenderTargetBitmap) once after starting in the target theme and once after starting in the
/// opposite theme and switching; the two bitmaps and the brushes the page resolves must match. Up/down convention and
/// language switches are checked for the same class of bug (a brush or string resolved once and never again).
/// </summary>
public class ThemeSwitchTests
{
    public static TheoryData<Type, bool> Cases()
    {
        var d = new TheoryData<Type, bool>();
        foreach (var t in ScreenRenderTests.PageTypes) { d.Add(t, true); d.Add(t, false); }
        return d;
    }

    /// <summary>Brushes every page draws with: WPF-UI surfaces/text plus the app's semantic tones.</summary>
    private static readonly string[] Keys =
    [
        "ApplicationBackgroundBrush", "CardBackgroundFillColorDefaultBrush", "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
        "ControlFillColorDefaultBrush", "ControlFillColorSecondaryBrush", "ControlStrokeColorDefaultBrush", "LayerFillColorDefaultBrush",
        "SolidBackgroundFillColorBaseBrush", "AccentFillColorDefaultBrush",
        "UpBrush", "DownBrush", "UpBackgroundBrush", "DownBackgroundBrush", "RowHighlightBrush", "ItmBrush",
        "HealthyBrush", "DegradedBrush", "DownStateBrush", "UnknownBrush", "TextControlPlaceholderForeground",
    ];

    private static void Theme(bool dark) => AppServices.Get<ThemeService>().Apply(dark ? AppThemeMode.Dark : AppThemeMode.Light);

    private static async Task<(byte[] Pixels, Dictionary<string, Color> Brushes)> Capture(Page page, OffscreenHost host)
    {
        await ScreenRenderTests.Settle();
        host.Layout();
        var rtb = new RenderTargetBitmap((int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host.Root);
        var px = new byte[(int)host.Width * (int)host.Height * 4];
        rtb.CopyPixels(px, (int)host.Width * 4, 0);
        var deepest = Descendants(page).OfType<FrameworkElement>().LastOrDefault() ?? page;
        var brushes = Keys.ToDictionary(k => k, k => ((SolidColorBrush)deepest.FindResource(k)).Color);
        return (px, brushes);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Runtime_theme_switch_matches_a_fresh_start(Type pageType, bool toDark)
    {
        WpfHost.Run(async () =>
        {
            try
            {
                Theme(toDark);
                var freshPage = (Page)Activator.CreateInstance(pageType)!;
                (byte[] Pixels, Dictionary<string, Color> Brushes) fresh;
                using (var host = new OffscreenHost(freshPage, 1280, 800, asMainWindow: true)) fresh = await Capture(freshPage, host);

                Theme(!toDark);
                var page = (Page)Activator.CreateInstance(pageType)!;
                using var switched = new OffscreenHost(page, 1280, 800, asMainWindow: true);
                await ScreenRenderTests.Settle();
                Theme(toDark);
                var after = await Capture(page, switched);

                foreach (var k in Keys)
                    Assert.True(fresh.Brushes[k] == after.Brushes[k], $"{pageType.Name} → {(toDark ? "dark" : "light")}: {k} is {after.Brushes[k]}, fresh start has {fresh.Brushes[k]}");
                AssertNoShadowedResources(switched.Root);

                var share = DifferingShare(fresh.Pixels, after.Pixels);
                if (share > 0.01) Dump(pageType, toDark, fresh.Pixels, after.Pixels);
                Assert.True(share <= 0.01, $"{pageType.Name} → {(toDark ? "dark" : "light")}: {share:P1} of pixels differ from a fresh start");
            }
            finally { Theme(true); }
        });
    }

    [Fact]
    public void Up_down_convention_switch_retones_resolved_brushes_and_view_models()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<DashboardViewModel>();
            var page = new Views.Pages.DashboardPage();
            using var host = new OffscreenHost(page, 1280, 800, asMainWindow: true);
            Theme(true);   // like App.OnStartup: the theme is applied once the main window exists
            await ScreenRenderTests.Settle();
            try
            {
                foreach (var mode in new[] { UpDownColorMode.GreenUp, UpDownColorMode.RedUp })
                {
                    ThemeService.ApplyUpDownColors(mode);
                    await ScreenRenderTests.Settle();
                    var app = (SolidColorBrush)Application.Current.FindResource("UpBrush");
                    Assert.Equal(app.Color, ((SolidColorBrush)page.FindResource("UpBrush")).Color);
                    Assert.All(vm.Quotes.Where(q => q.ChangeBrush is not null), q =>
                        Assert.True(ReferenceEquals(q.ChangeBrush, app) || ReferenceEquals(q.ChangeBrush, Application.Current.FindResource("DownBrush")),
                            $"{q.Symbol} keeps a brush from before the {mode} switch"));
                }
            }
            finally { ThemeService.ApplyUpDownColors(UpDownColorMode.RedUp); }
        });
    }

    [Theory]
    [MemberData(nameof(PageLoadTests.Pages), MemberType = typeof(PageLoadTests))]
    public void Language_switch_leaves_no_text_of_the_previous_language(Type pageType)
    {
        WpfHost.Run(async () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            try
            {
                foreach (var (from, to) in new[] { ("zh-CN", "en-US"), ("en-US", "zh-CN") })
                {
                    loc.Apply(from);
                    var old = Strings(loc);
                    var page = (Page)Activator.CreateInstance(pageType)!;
                    using var host = new OffscreenHost(page, 1280, 800, asMainWindow: true);
                    Theme(true);   // like App.OnStartup: the theme is applied once the main window exists
                    await ScreenRenderTests.Settle();
                    loc.Apply(to);
                    await ScreenRenderTests.Settle();
                    host.Layout();
                    var current = Strings(loc).Values.ToHashSet();
                    var stale = old.Values.Where(v => !current.Contains(v)).ToHashSet();
                    var left = Descendants(page).OfType<TextBlock>().Where(t => t.IsVisible && stale.Contains(t.Text)).Select(t => t.Text).ToList();
                    Assert.True(left.Count == 0, $"{pageType.Name} {from}→{to} still shows: {string.Join(" | ", left)}");
                    AssertNoShadowedResources(host.Root);
                }
            }
            finally { loc.Apply("zh-CN"); }
        });
    }

    private static Dictionary<string, string> Strings(LocalizationService loc) =>
        loc.Strings.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>No element may carry a copy of an application resource: a local copy would never be re-toned.</summary>
    private static void AssertNoShadowedResources(DependencyObject root)
    {
        foreach (var fe in Descendants(root).Prepend(root).OfType<FrameworkElement>())
        {
            var shadow = fe.Resources.Keys.Cast<object>().Where(k => k is string s && Keys.Contains(s)).ToList();
            Assert.True(shadow.Count == 0, $"{fe.GetType().Name} holds local copies of {string.Join(", ", shadow)}");
            Assert.DoesNotContain(fe.Resources.MergedDictionaries, d => d.Source?.ToString().Contains("Wpf.Ui") == true);
        }
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>Share of pixels where any channel differs by more than 24 (anti-aliasing and caret blink stay below).</summary>
    internal static double DifferingShare(byte[] a, byte[] b)
    {
        var diff = 0;
        for (var i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) > 24 || Math.Abs(a[i + 1] - b[i + 1]) > 24 || Math.Abs(a[i + 2] - b[i + 2]) > 24) diff++;
        return diff / (a.Length / 4.0);
    }

    private static void Dump(Type page, bool dark, byte[] fresh, byte[] after)
    {
        if (Environment.GetEnvironmentVariable("MEILI_RENDER_DIR") is not { Length: > 0 } dir) return;
        foreach (var (name, px) in new[] { ("fresh", fresh), ("switched", after) })
        {
            var bmp = BitmapSource.Create(1280, 800, 96, 96, PixelFormats.Pbgra32, null, px, 1280 * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(dir);
            using var fs = File.Create(Path.Combine(dir, $"themeswitch-{page.Name}-{(dark ? "dark" : "light")}-{name}.png"));
            enc.Save(fs);
        }
    }
}
