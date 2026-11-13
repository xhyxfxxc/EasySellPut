using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Meili.Core.Settings;

namespace Meili.Mac.Platform;

/// <summary>
/// Light / dark / follow-system (the default) plus the red-up / green-up convention. Surfaces and semantic
/// brushes are written from <see cref="ThemePalette"/> into one swapped dictionary, so a change costs one
/// resource notification and always matches the shared tokens (App.axaml ThemeDictionaries hold the same
/// values for design-time, before this runs).
/// </summary>
public sealed class MacTheme : IThemeController
{
    private readonly ResourceDictionary _host = new();
    private ResourceDictionary? _current;
    private UpDownColorMode _upDown = UpDownColorMode.RedUp;

    /// <summary>Raised after the brushes were replaced; view-models holding one fetch it again.</summary>
    public static event EventHandler? ColorsChanged;

    public MacTheme()
    {
        var app = Application.Current!;
        app.Resources.MergedDictionaries.Add(_host);
        // Follows the system appearance (System Settings › Appearance, including Auto) without polling.
        app.ActualThemeVariantChanged += (_, _) => ApplySemanticColors();
        ApplySemanticColors();
    }

    public bool IsDark => Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

    public void Apply(AppThemeMode mode)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = mode switch { AppThemeMode.Light => ThemeVariant.Light, AppThemeMode.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }

    public void ApplyUpDownColors(UpDownColorMode mode)
    {
        _upDown = mode;
        ApplySemanticColors();
    }

    private void ApplySemanticColors()
    {
        var p = ThemePalette.For(IsDark);
        var (up, down) = p.UpDown(_upDown);
        var dict = new ResourceDictionary();
        foreach (var (key, argb) in p.ResourceBrushes())
            dict[key] = Brush(argb);
        dict["UpBrush"] = Brush(up);
        dict["DownBrush"] = Brush(down);
        dict["UpBackgroundBrush"] = Brush(ThemePalette.Tint(up));
        dict["DownBackgroundBrush"] = Brush(ThemePalette.Tint(down));
        dict["HealthyBrush"] = Brush(p.Healthy);
        dict["DegradedBrush"] = Brush(p.Degraded);
        dict["DownStateBrush"] = Brush(p.DownState);
        dict["UnknownBrush"] = Brush(p.Unknown);
        dict["RowHighlightBrush"] = Brush(p.RowHighlight);
        dict["ItmBrush"] = Brush(p.ItmTint);
        if (_current is not null) _host.MergedDictionaries.Remove(_current);
        _host.MergedDictionaries.Add(dict);
        _current = dict;
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Immutable brushes: shareable between controls and cheap for the renderer.</summary>
    private static ImmutableSolidColorBrush Brush(uint argb) => new(Color.FromUInt32(argb));
}
