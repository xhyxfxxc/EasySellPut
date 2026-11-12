using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using Meili.App.Services;
using Meili.App.ViewModels;
using Meili.App.Views.Pages;
using Meili.Core.Settings;

namespace Meili.App.Tests;

/// <summary>
/// E4 visual walkthrough + C5 render baseline. Pages are rendered off-screen (hidden HwndSource +
/// RenderTargetBitmap) to PNG only when MEILI_RENDER_DIR is set, e.g.
/// <c>$env:MEILI_RENDER_DIR='C:\tyuStockApp\scratch\screens\run1'; dotnet test --filter Category=Render</c>.
/// Without it the render tests return immediately; the audit and virtualization checks always run.
/// </summary>
[Trait("Category", "Render")]
public class ScreenRenderTests
{
    private static string? OutDir => Environment.GetEnvironmentVariable("MEILI_RENDER_DIR") is { Length: > 0 } d ? d : null;

    public static TheoryData<string, bool> Variants => new() { { "zh-CN", true }, { "zh-CN", false }, { "en-US", true }, { "en-US", false } };

    internal static readonly Type[] PageTypes =
    [
        typeof(DashboardPage), typeof(WatchlistPage), typeof(ChartsPage), typeof(OptionsPage), typeof(StrategyPage),
        typeof(NewsPage), typeof(AlertsPage), typeof(DataSourcesPage), typeof(SettingsPage),
    ];

    internal static async Task Settle(int rounds = 8)
    {
        for (var i = 0; i < rounds; i++) { await WpfHost.Settle(); await Task.Delay(25); }
    }

    internal static void Apply(string lang, bool dark)
    {
        AppServices.Get<LocalizationService>().Apply(lang);
        AppServices.Get<ThemeService>().Apply(dark ? AppThemeMode.Dark : AppThemeMode.Light);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Render_every_page(string lang, bool dark)
    {
        if (OutDir is not { } dir) return;
        WpfHost.Run(async () =>
        {
            var report = new StringBuilder();
            try
            {
                Apply(lang, dark);
                foreach (var type in PageTypes)
                {
                    var page = (Page)Activator.CreateInstance(type)!;
                    using var host = new OffscreenHost(page, 1280, 800);
                    await Settle();
                    var name = $"{type.Name.Replace("Page", "").ToLowerInvariant()}-{lang}-{(dark ? "dark" : "light")}";
                    host.SavePng(Path.Combine(dir, name + ".png"));
                    // Audit after rendering, so pending theme/resource invalidations have been applied.
                    foreach (var f in UiAudit.Run(page))
                        report.AppendLine($"{name}\t{f.Kind}\t{f.Text.Replace('\n', ' ')}\t{f.Detail}");
                }
            }
            finally { Apply("zh-CN", true); }
            File.AppendAllText(Path.Combine(dir, "audit.tsv"), report.ToString(), Encoding.UTF8);
        });
    }

    /// <summary>C5: a 400-strike chain with the "all strikes" window must stay virtualized and render quickly.</summary>
    [Fact]
    public void Full_chain_renders_virtualized()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<OptionsViewModel>();
            WpfHost.Data.ChainStrikes = Enumerable.Range(0, 400).Select(i => 50 + i * 0.5).ToArray();
            try
            {
                vm.SymbolText = "SPY";
                var page = new OptionsPage();
                using var host = new OffscreenHost(page, 1280, 800);
                await Settle(2);
                vm.SelectWindowCommand.Execute(StrikeWindowKind.All);
                var sw = Stopwatch.StartNew();
                await vm.LoadAsync();
                host.Layout();
                var loadMs = sw.Elapsed.TotalMilliseconds;
                await Settle(2);
                sw.Restart();
                if (OutDir is { } dir) host.SavePng(Path.Combine(dir, "perf-options-400.png"));
                else host.Layout();
                var renderMs = sw.Elapsed.TotalMilliseconds;
                var realized = UiAudit.Descendants<ListViewItem>(page).Count();
                Assert.Equal(400, vm.Rows.Count);
                Assert.True(realized < 120, $"rows are not virtualized: {realized} containers for 400 rows");
                Assert.True(loadMs < 8000, $"load + layout took {loadMs:0} ms");
                if (OutDir is { } d)
                    File.AppendAllText(Path.Combine(d, "perf.tsv"),
                        $"options-400\tload+layout {loadMs:0} ms\trender {renderMs:0} ms\trealized rows {realized}\n");
            }
            finally
            {
                WpfHost.Data.ChainStrikes = null;
                vm.SelectWindowCommand.Execute(StrikeWindowKind.Near10);
                vm.Deactivate();
            }
        });
    }
}
