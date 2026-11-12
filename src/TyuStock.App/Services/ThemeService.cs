using System.Windows;
using System.Windows.Media;
using TyuStock.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TyuStock.App.Services;

/// <summary>Light/dark/system theme via WPF-UI, plus the up/down price colour brushes.</summary>
public sealed class ThemeService
{
    private Window? _watched;
    public AppThemeMode Mode { get; private set; } = AppThemeMode.System;
    public event EventHandler? ThemeChanged;

    public bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    public void Apply(AppThemeMode mode, Window? mainWindow = null)
    {
        Mode = mode;
        mainWindow ??= Application.Current.MainWindow;
        if (_watched is not null) { SystemThemeWatcher.UnWatch(_watched); _watched = null; }

        switch (mode)
        {
            case AppThemeMode.Light: ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true); break;
            case AppThemeMode.Dark: ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true); break;
            default:
                ApplicationThemeManager.ApplySystemTheme(true);
                if (mainWindow is not null && mainWindow.IsLoaded)
                {
                    SystemThemeWatcher.Watch(mainWindow, WindowBackdropType.Mica, true);
                    _watched = mainWindow;
                }
                break;
        }
        if (mainWindow is not null) ApplicationThemeManager.Apply(mainWindow);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Quick toggle from the title bar: flips to the opposite explicit theme.</summary>
    public AppThemeMode Toggle() => IsDark ? AppThemeMode.Light : AppThemeMode.Dark;

    public static void ApplyUpDownColors(UpDownColorMode mode)
    {
        var red = (Color)ColorConverter.ConvertFromString("#E5484D");
        var green = (Color)ColorConverter.ConvertFromString("#22A06B");
        var up = mode == UpDownColorMode.RedUp ? red : green;
        var down = mode == UpDownColorMode.RedUp ? green : red;
        var res = Application.Current.Resources;
        res["UpBrush"] = Frozen(new SolidColorBrush(up));
        res["DownBrush"] = Frozen(new SolidColorBrush(down));
        res["UpBackgroundBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x26, up.R, up.G, up.B)));
        res["DownBackgroundBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x26, down.R, down.G, down.B)));
    }

    private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
}
