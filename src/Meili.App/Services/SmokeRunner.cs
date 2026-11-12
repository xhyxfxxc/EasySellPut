using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Meili.Core.Abstractions;
using Meili.Core.Alerts;
using Meili.Core.Models;
using Meili.Core.Pricing;
using Meili.Core.Quotes;
using Meili.Core.Settings;
using Meili.Data.Cache;

namespace Meili.App.Services;

public enum SmokeStatus { Pass, Warn, Fail }

public sealed record SmokeCheck(string Name, SmokeStatus Status, string Detail, long Milliseconds);

/// <summary>
/// B3: <c>--smoke</c> self-check. Runs inside the real app (real composition, real window kept off-screen), walks every
/// page, exercises local storage (settings, SQLite cache incl. the native library), the string dictionaries and — unless
/// <c>--smoke-offline</c> — one live quote, K-line, option chain, IV and rate fetch. Writes a JSON report and exits
/// with 0 (no failures) or 1. Network problems are warnings, not failures: the machine may simply be offline.
/// </summary>
public sealed class SmokeRunner
{
    private readonly List<SmokeCheck> _checks = [];
    private static readonly List<string> Errors = [];

    /// <summary>Unhandled exceptions seen while smoke-testing (recorded instead of showing a dialog).</summary>
    public static void RecordError(Exception ex)
    {
        lock (Errors) Errors.Add(ex.GetType().Name + ": " + ex.Message);
    }

    public IReadOnlyList<SmokeCheck> Checks => _checks;

    public async Task<int> RunAsync(MainWindow window, AppLaunchOptions options)
    {
        var sw = Stopwatch.StartNew();
        await CheckAsync("data-dir", () =>
        {
            var dir = AppServices.DataDirectory.Path;
            var probe = Path.Combine(dir, ".smoke-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult($"{dir} ({AppServices.DataDirectory.Source})");
        });
        await CheckAsync("settings", () =>
        {
            var store = AppServices.Get<SettingsStore>();
            store.Update(_ => { });
            var again = new SettingsStore(AppServices.DataDirectory.Path).Load();
            return Task.FromResult($"language={again.Language}, close={again.CloseAction}");
        });
        await CheckAsync("sqlite-cache", () =>
        {
            var db = AppServices.Get<MarketDb>();
            var t = DateTimeOffset.UtcNow.Date;
            var bars = new[] { Bar.FromSource(new DateTimeOffset(t.AddDays(-1), TimeSpan.Zero), 1, 2, 0.5, 1.5, 100) };
            db.SaveBars("__SMOKE__", BarInterval.Day1, bars, new BarCacheMeta("smoke", DateTimeOffset.UtcNow, null, null, false));
            var (loaded, meta) = db.LoadBars("__SMOKE__", BarInterval.Day1);
            if (loaded.Count != 1 || meta?.Source != "smoke") throw new InvalidOperationException("bar round-trip mismatch");
            db.AddIvSample("__SMOKE__", DateTimeOffset.UtcNow, "Iv30", 0.3, "smoke");
            if (db.LoadIvSamples("__SMOKE__", DateTimeOffset.UtcNow.AddMinutes(-5)).Count == 0) throw new InvalidOperationException("iv sample round-trip failed");
            return Task.FromResult($"{db.FilePath}, schema v{db.ReadSchemaVersion()}");
        });
        await CheckAsync("strings", () =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var current = loc.Language;
            loc.Apply("en-US");
            var en = loc["Nav_Dashboard"];
            loc.Apply("zh-CN");
            var zh = loc["Nav_Dashboard"];
            loc.Apply(current);
            if (en == "Nav_Dashboard" || zh == "Nav_Dashboard" || en == zh) throw new InvalidOperationException($"dictionaries not loaded (en={en}, zh={zh})");
            return Task.FromResult($"en={en}, zh={zh}");
        });
        foreach (var page in KeyboardShortcuts.PageOrder)
        {
            await CheckAsync("page:" + page.Name.Replace("Page", ""), async () =>
            {
                int before;
                lock (Errors) before = Errors.Count;
                window.NavigateForSmoke(page);
                await Task.Delay(1500);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                string[] newErrors;
                lock (Errors) newErrors = Errors.Skip(before).ToArray();
                if (newErrors.Length > 0) throw new InvalidOperationException(string.Join(" | ", newErrors));
                if (window.CurrentPageType != page) throw new InvalidOperationException($"navigation ended on {window.CurrentPageType?.Name ?? "none"}");
                return "loaded";
            });
        }
        await CheckAsync("keyboard-wizard", () =>
        {
            window.OpenWizardForSmoke("A");
            var ok = window.WizardVisible;
            window.CloseWizardForSmoke();
            if (!ok) throw new InvalidOperationException("wizard did not open");
            return Task.FromResult("opens and closes");
        });

        if (!options.SmokeOffline)
        {
            var data = AppServices.Get<IMarketDataService>();
            await NetAsync("net:quote", async ct =>
            {
                var q = (await AppServices.Get<QuoteHub>().GetQuotesAsync(["AAPL"], ct)).FirstOrDefault();
                if (q is null || q.Price <= 0) throw new InvalidOperationException("no AAPL quote");
                return $"AAPL {NumberDisplay.Price(q.Price)} from {q.Source}, {DataFreshness.Evaluate(q, DateTimeOffset.Now).Kind}";
            });
            await NetAsync("net:bars", async ct =>
            {
                var s = await data.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1, DateTimeOffset.UtcNow.AddMonths(-3)), ct);
                return $"{s.Bars.Count} daily bars from {s.Source}{(s.OfflineCache ? " (offline cache)" : "")}";
            });
            await NetAsync("net:options", async ct =>
            {
                var exps = await data.GetExpirationsAsync("AAPL", ct);
                var chain = await data.GetChainAsync("AAPL", exps.FirstOrDefault(), ct);
                return $"{exps.Count} expirations, {chain.Contracts.Count} contracts from {chain.Source}";
            });
            await NetAsync("net:iv", async ct =>
            {
                var s = await AppServices.Get<IIvSource>().GetAsync("AAPL", DateTimeOffset.Now, ct) ?? throw new InvalidOperationException("no IV sample");
                return $"{s.Percent:0.0}% ({s.Basis}, {s.Source})";
            });
            await NetAsync("net:rate", async ct =>
            {
                var r = await AppServices.Get<RiskFreeRateProvider>().GetAsync(ct);
                if (r.Origin == RateOrigin.FixedFallback) throw new InvalidOperationException($"^IRX unavailable, fallback {r.Percent:0.00}%");
                return $"{r.Percent:0.00}% ({r.Origin})";
            });
        }
        else _checks.Add(new SmokeCheck("net:*", SmokeStatus.Warn, "skipped (--smoke-offline)", 0));

        var fails = _checks.Count(c => c.Status == SmokeStatus.Fail);
        WriteReport(options, sw.Elapsed, fails);
        return fails == 0 ? 0 : 1;
    }

    private async Task CheckAsync(string name, Func<Task<string>> body)
    {
        var sw = Stopwatch.StartNew();
        try { _checks.Add(new SmokeCheck(name, SmokeStatus.Pass, await body(), sw.ElapsedMilliseconds)); }
        catch (Exception ex) { _checks.Add(new SmokeCheck(name, SmokeStatus.Fail, ex.GetType().Name + ": " + ex.Message, sw.ElapsedMilliseconds)); }
    }

    private async Task NetAsync(string name, Func<CancellationToken, Task<string>> body)
    {
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { _checks.Add(new SmokeCheck(name, SmokeStatus.Pass, await body(cts.Token), sw.ElapsedMilliseconds)); }
        catch (Exception ex) { _checks.Add(new SmokeCheck(name, SmokeStatus.Warn, ex.GetType().Name + ": " + ex.Message, sw.ElapsedMilliseconds)); }
    }

    private void WriteReport(AppLaunchOptions options, TimeSpan elapsed, int fails)
    {
        var path = options.SmokeReport ?? Path.Combine(AppServices.DataDirectory.Path, "logs", "smoke.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string[] errors;
        lock (Errors) errors = [.. Errors];
        var report = new
        {
            Version = typeof(SmokeRunner).Assembly.GetName().Version?.ToString(),
            Process = Environment.ProcessPath,
            StartedAt = DateTimeOffset.Now - elapsed,
            ElapsedSeconds = Math.Round(elapsed.TotalSeconds, 1),
            Failures = fails,
            Warnings = _checks.Count(c => c.Status == SmokeStatus.Warn),
            Checks = _checks.Select(c => new { c.Name, Status = c.Status.ToString(), c.Detail, c.Milliseconds }),
            UnhandledErrors = errors,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Meili.Core.Diagnostics.AppLog.Info("smoke", $"done: {fails} failures, report {path}");
    }
}
