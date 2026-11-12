using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;
using TyuStock.Core.Alerts;
using TyuStock.Core.Settings;

namespace TyuStock.App.Services;

/// <summary>Tray icon plus Windows toast notifications. Failures are written to the log folder, never shown as dialogs.</summary>
public sealed class NotificationService : IDisposable
{
    public const string AppUserModelId = "MeiliDaogu.SellerTerminal";

    private readonly LocalizationService _loc;
    private readonly AppLaunchOptions _options;
    private readonly DataDirectoryInfo _dataDir;
    private readonly NavigationHub _nav;
    private TaskbarIcon? _tray;
    private bool _disposed;

    public event EventHandler? ExitRequested;

    public NotificationService(LocalizationService loc, AppLaunchOptions options, DataDirectoryInfo dataDir, NavigationHub nav)
    {
        _loc = loc;
        _options = options;
        _dataDir = dataDir;
        _nav = nav;
        _loc.LanguageChanged += (_, _) => UpdateTooltip();
        try { ToastNotificationManagerCompat.OnActivated += OnToastActivated; } catch (Exception ex) { Log(ex); }
    }

    public void Attach(Window window)
    {
        if (_options.NoAlerts || _tray is not null) return;
        try
        {
            TrySetAppUserModelId();
            _tray = new TaskbarIcon
            {
                ToolTipText = _loc["App_Title"],
                IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
                ContextMenu = BuildMenu(),
                NoLeftClickDelay = true,
            };
            _tray.TrayLeftMouseUp += (_, _) => Restore(window);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void UpdateTooltip()
    {
        if (_tray is null) return;
        try
        {
            _tray.ToolTipText = _loc["App_Title"];
            if (_tray.ContextMenu is not null)
                _tray.ContextMenu = BuildMenu();
        }
        catch (Exception ex) { Log(ex); }
    }

    public void ShowAlertToast(PriceAlert alert, AlertEvalResult result)
    {
        if (_options.NoAlerts || !result.Fired) return;
        try
        {
            var title = string.Format(_loc["Al_FiredToastTitle"], alert.Symbol);
            var body = FormatBody(alert, result);
            if (result.UsedExtended) body += " " + _loc["Al_UsedExtended"];
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddArgument("action", "open")
                .AddArgument("symbol", alert.Symbol)
                .Show();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void Restore(Window? window = null)
    {
        window ??= Application.Current?.MainWindow;
        if (window is null) return;
        window.Dispatcher.Invoke(() =>
        {
            window.Show();
            window.ShowInTaskbar = true;
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        });
    }

    public string FormatBody(PriceAlert alert, AlertEvalResult result)
    {
        var value = FormatValue(alert.Kind, result.Value ?? 0);
        var key = alert.Kind switch
        {
            AlertKind.PriceAbove => "Al_FiredPriceAbove",
            AlertKind.PriceBelow => "Al_FiredPriceBelow",
            AlertKind.PercentUp => "Al_FiredPercentUp",
            AlertKind.PercentDown => "Al_FiredPercentDown",
            AlertKind.VolumeAbove => "Al_FiredVolumeAbove",
            _ => "Al_FiredPriceAbove",
        };
        return string.Format(_loc[key], value, FormatValue(alert.Kind, alert.Threshold));
    }

    public static string FormatValue(AlertKind kind, double value) => kind switch
    {
        AlertKind.PercentUp or AlertKind.PercentDown => value.ToString("0.##"),
        AlertKind.VolumeAbove => value.ToString("N0"),
        _ => value.ToString("0.##"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ToastNotificationManagerCompat.OnActivated -= OnToastActivated; } catch { }
        try { _tray?.Dispose(); } catch { }
        _tray = null;
    }

    private ContextMenu BuildMenu()
    {
        var show = new MenuItem { Header = _loc["Tray_Show"], FontSize = 14 };
        show.Click += (_, _) => Restore();
        var exit = new MenuItem { Header = _loc["Tray_Exit"], FontSize = 14 };
        exit.Click += (_, _) =>
        {
            if (ExitRequested is not null) ExitRequested.Invoke(this, EventArgs.Empty);
            else Application.Current?.Shutdown();
        };
        return new ContextMenu { Items = { show, exit }, FontSize = 14 };
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            var symbol = args.TryGetValue("symbol", out var s) ? s : null;
            var disp = Application.Current?.Dispatcher;
            if (disp is null) return;
            disp.BeginInvoke(() =>
            {
                Restore();
                if (!string.IsNullOrWhiteSpace(symbol))
                    _nav.Go(typeof(Views.Pages.ChartsPage), symbol);
            });
        }
        catch (Exception ex) { Log(ex); }
    }

    private void TrySetAppUserModelId()
    {
        try { _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch (Exception ex) { Log(ex); }
    }

    private void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(_dataDir.Path, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "alerts.log"), $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}