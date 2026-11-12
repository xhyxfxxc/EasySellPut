using System.Windows;
using System.Windows.Media;
using Meili.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Meili.App.Services;

/// <summary>Light/dark/system theme via WPF-UI, plus the up/down price colour brushes.</summary>
public sealed class ThemeService : IThemeController
{
    void IThemeController.Apply(AppThemeMode mode) => Apply(mode);
    void IThemeController.ApplyUpDownColors(UpDownColorMode mode) => ApplyUpDownColors(mode);

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
        ApplySemanticColors(IsDark);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Quick toggle from the title bar: flips to the opposite explicit theme.</summary>
    public AppThemeMode Toggle() => IsDark ? AppThemeMode.Light : AppThemeMode.Dark;

    private static UpDownColorMode _upDown = UpDownColorMode.RedUp;

    /// <summary>Up/down colours for the chosen convention, in the tones of the active theme (E4 contrast).</summary>
    public static void ApplyUpDownColors(UpDownColorMode mode)
    {
        _upDown = mode;
        ApplySemanticColors(ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark);
    }

    /// <summary>Raised after the up/down and health brushes were replaced; view models holding one re-fetch it.</summary>
    public static event EventHandler? ColorsChanged;

    /// <summary>E4: price and health colours are re-toned per theme so text keeps a 4.5:1 contrast on both.</summary>
    public static void ApplySemanticColors(bool dark)
    {
        var p = SemanticPalette.For(dark);
        var up = _upDown == UpDownColorMode.RedUp ? p.Red : p.Green;
        var down = _upDown == UpDownColorMode.RedUp ? p.Green : p.Red;
        Set("UpBrush", up);
        Set("DownBrush", down);
        Set("UpBackgroundBrush", Color.FromArgb(SemanticPalette.TintAlpha, up.R, up.G, up.B));
        Set("DownBackgroundBrush", Color.FromArgb(SemanticPalette.TintAlpha, down.R, down.G, down.B));
        Set("HealthyBrush", p.Healthy);
        Set("DegradedBrush", p.Degraded);
        Set("DownStateBrush", p.DownState);
        Set("UnknownBrush", p.Unknown);
        Set("RowHighlightBrush", p.RowHighlight);
        // E4 final pass: a translucent ITM shade shows on plain and highlighted rows in both themes (the WPF-UI
        // secondary fill is near-white in light mode), and placeholders keep 4.5:1 even on hovered dark inputs.
        Set("ItmBrush", p.ItmTint);
        Set("TextControlPlaceholderForeground", p.Placeholder);
        ColorsChanged?.Invoke(null, EventArgs.Empty);
    }

    // WPF freezes brushes held in application resources, so each change installs a new brush.
    private static void Set(string key, Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        Application.Current.Resources[key] = b;
    }
}

/// <summary>E4: theme-specific tones for price direction and health states (all ≥ 4.5:1 on the theme's page and card backgrounds).</summary>
public sealed record SemanticPalette(Color Red, Color Green, Color Healthy, Color Degraded, Color DownState, Color Unknown, Color RowHighlight,
    Color ItmTint, Color Placeholder)
{
    private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static readonly SemanticPalette Light = new(C(0xB8282E), C(0x0F6B45), C(0x2563B8), C(0x8F5E00), C(0x5E6875), C(0x6B6B6B), C(0xEADCF2),
        Color.FromArgb(0x0F, 0, 0, 0), Color.FromArgb(0xA8, 0, 0, 0));
    public static readonly SemanticPalette Dark = new(C(0xF89CA0), C(0x45C98E), C(0x6AA6EC), C(0xD99A1E), C(0x9AA4B2), C(0xA0A0A0), C(0x3E2F48),
        Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));

    /// <summary>Badge fill behind dark (#1A1A1A) text, e.g. the "stale" tag; the same in both themes.</summary>
    public static readonly Color Badge = C(0xD99A1E);

    /// <summary>Alpha of the up/down tag fills behind up/down text.</summary>
    public const byte TintAlpha = 0x1A;

    public static SemanticPalette For(bool dark) => dark ? Dark : Light;
}
