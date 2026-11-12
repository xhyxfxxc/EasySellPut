using System.Diagnostics;
using System.Globalization;
using Meili.Core.Alerts;
using Meili.Core.Diagnostics;

namespace Meili.Mac.Platform;

/// <summary>
/// Alert delivery on macOS: Notification Center via <c>osascript display notification</c> (works for an ad-hoc signed
/// app on macOS 26, where UNUserNotificationCenter wants a Developer ID identity), plus an in-app banner (always
/// visible, even when notifications are off for Script Editor) and a Dock bounce. The child process runs off the UI
/// thread with a time limit, so a stuck notification service can never hang the app.
/// </summary>
public sealed class MacNotifier(LocalizationService loc, AppLaunchOptions options) : IAlertNotifier
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);
    private bool _hiddenHintShown;

    /// <summary>Title and body for the in-app banner (raised on the UI thread).</summary>
    public event Action<string, string>? Banner;

    public void ShowAlert(PriceAlert alert, AlertEvalResult result, Meili.Core.Models.Quote? quote)
    {
        if (options.NoAlerts || !result.Fired) return;
        var body = AlertText.Body(alert, result, loc);
        if (result.UsedExtended) body += " " + loc["Al_UsedExtended"];
        // F3: say where the price came from and how old it may be.
        var timing = quote is null ? null : FreshnessText.SourceAndTiming(quote, loc, DateTimeOffset.Now);
        Post(AlertText.Title(alert, loc), string.IsNullOrEmpty(timing) ? body : body + "\n" + timing, attention: true);
    }

    public void ShowIvAlert(PriceAlert alert, AlertEvalResult result, IvSample sample)
    {
        if (options.NoAlerts || !result.Fired) return;
        Post(AlertText.Title(alert, loc), AlertText.Body(alert, result, loc) + "\n" + AlertText.IvBasisText(sample, loc), attention: true);
    }

    public void ShowMonitorInterrupted(MonitorHealth health)
    {
        if (options.NoAlerts) return;
        Post(loc["Al_MonitorDownTitle"], string.Format(loc["Al_MonitorDownBody"], health.ConsecutiveFailures,
            health.FailingSince?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? ""), attention: false);
    }

    public void ShowMonitorRecovered()
    {
        if (options.NoAlerts) return;
        Post(loc["Al_MonitorUpTitle"], loc["Al_MonitorUpBody"], attention: false);
    }

    /// <summary>Once per session: closing the window does not quit (Mac convention), so say alerts keep running.</summary>
    public void ShowHiddenHint()
    {
        if (_hiddenHintShown || options.NoAlerts) return;
        _hiddenHintShown = true;
        _ = NotifyAsync(loc["Mac_HiddenTitle"], loc["Mac_HiddenBody"]);
    }

    /// <summary>In-app banner for an unexpected error (no modal dialog, so it cannot re-enter or pile up).</summary>
    public void ShowError(string message) => Banner?.Invoke(loc["Mac_ErrorTitle"], message);

    private void Post(string title, string body, bool attention)
    {
        Ui.Platform.Run(() =>
        {
            Banner?.Invoke(title, body);
            if (attention && OperatingSystem.IsMacOS()) MacNative.RequestAttention();
        });
        _ = NotifyAsync(title, body);
    }

    private static async Task NotifyAsync(string title, string body)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(Script(title, body));
            using var p = await Task.Run(() => Process.Start(psi)!);
            using var cts = new CancellationTokenSource(ScriptTimeout);
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                p.Kill();
                AppLog.Warn("notify", "osascript timed out; notification skipped");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("notify", "notification failed", ex);
        }
    }

    /// <summary>AppleScript source with both strings quoted (backslash and quote escaped).</summary>
    internal static string Script(string title, string body) =>
        $"display notification {Quote(body)} with title {Quote(title)}";

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
