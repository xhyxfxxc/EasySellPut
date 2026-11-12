using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Meili.Core.Settings;

namespace Meili.Mac.Platform;

/// <summary>
/// Light / dark / follow-system (the default) plus the red-up / green-up convention. The semantic brushes live in one
/// merged dictionary that is swapped whole, so a change costs one resource notification, not one per brush.
/// </summary>
public sealed class MacTheme : IThemeController
{
    private readonly ResourceDictionary _host = new();
    private ResourceDictionary? _current;
    private UpDownColorMode _upDown = UpDownColorMode.RedUp;

    /// <summary>Raised after the semantic brushes were replaced; view-models holding one fetch it again.</summary>
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
        var c = SemanticColors.For(IsDark);
        var (up, down) = c.UpDown(_upDown);
        var dict = new ResourceDictionary
        {
            ["UpBrush"] = Brush(up),
            ["DownBrush"] = Brush(down),
            ["UpBackgroundBrush"] = Brush(SemanticColors.Tint(up)),
            ["DownBackgroundBrush"] = Brush(SemanticColors.Tint(down)),
            ["HealthyBrush"] = Brush(c.Healthy),
            ["DegradedBrush"] = Brush(c.Degraded),
            ["DownStateBrush"] = Brush(c.DownState),
            ["UnknownBrush"] = Brush(c.Unknown),
            ["RowHighlightBrush"] = Brush(c.RowHighlight),
            ["ItmBrush"] = Brush(c.ItmTint),
            ["StaleBadgeBrush"] = Brush(SemanticColors.Badge),
        };
        if (_current is not null) _host.MergedDictionaries.Remove(_current);
        _host.MergedDictionaries.Add(dict);
        _current = dict;
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Immutable brushes: shareable between controls and cheap for the renderer.</summary>
    private static ImmutableSolidColorBrush Brush(uint argb) => new(Color.FromUInt32(argb));
}
