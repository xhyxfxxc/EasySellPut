namespace Meili.Shared.Services;

/// <summary>
/// 0.4.2 colour tokens: softer night and warmer paper. This is the single source both front ends and
/// <see cref="ChartPalette"/> read; values match palette-proposal/tokens-0.4.2.json (0xAARRGGBB).
/// </summary>
public sealed record ThemePalette(
    uint Window, uint Sidebar, uint Card, uint Chart, uint Control, uint Border,
    uint PrimaryText, uint SecondaryText, uint MutedText,
    uint Up, uint Down, uint Healthy, uint Degraded, uint DownState, uint Unknown,
    uint Accent, uint OnAccent, uint RowHighlight, uint ItmTint, uint Placeholder,
    uint Badge, uint BadgeText, uint ErrorBar, uint InfoBar,
    byte GridAlpha,
    uint Series1, uint Series2, uint Series3, uint Series4, uint Series5, uint Series6)
{
    /// <summary>Alpha of the up/down tag fills behind up/down text.</summary>
    public const byte TintAlpha = 0x1A;

    public static readonly ThemePalette Dark = new(
        Window: 0xFF24221F, Sidebar: 0xFF1F1D1B, Card: 0xFF2C2A27, Chart: 0xFF201E1C, Control: 0xFF32302C, Border: 0xFF433F3A,
        PrimaryText: 0xFFC9C6C1, SecondaryText: 0xFFA09B92, MutedText: 0xFF969188,
        Up: 0xFFCC8E8A, Down: 0xFF6EAA8A, Healthy: 0xFF7EA6C0, Degraded: 0xFFC4A46A, DownState: 0xFF9A958C, Unknown: 0xFF9A958C,
        Accent: 0xFFC4B3D4, OnAccent: 0xFF1A1816, RowHighlight: 0xFF35322E, ItmTint: 0x22C4B3D4, Placeholder: 0xFF969188,
        Badge: 0xFFC4A46A, BadgeText: 0xFF1A1816, ErrorBar: 0xFF3A2A28, InfoBar: 0xFF2C2A28,
        GridAlpha: 0x1F,
        Series1: 0xFF7BA8C0, Series2: 0xFFC9A06A, Series3: 0xFFBAA8C6, Series4: 0xFF6AADC0, Series5: 0xFFC4909E, Series6: 0xFFB4A072);

    public static readonly ThemePalette Light = new(
        Window: 0xFFF4EDDF, Sidebar: 0xFFEDE4D4, Card: 0xFFF9F4EA, Chart: 0xFFF6F0E3, Control: 0xFFFBF7F0, Border: 0xFFE0D6C8,
        PrimaryText: 0xFF2F2B26, SecondaryText: 0xFF5F594F, MutedText: 0xFF655E55,
        Up: 0xFFA83E38, Down: 0xFF1A7050, Healthy: 0xFF1A5F96, Degraded: 0xFF7A5500, DownState: 0xFF5F594F, Unknown: 0xFF5F594F,
        Accent: 0xFF6A458E, OnAccent: 0xFFF9F4EA, RowHighlight: 0xFFE8DFD0, ItmTint: 0x156A458E, Placeholder: 0xFF655E55,
        Badge: 0xFFC4A46A, BadgeText: 0xFF1A1816, ErrorBar: 0xFFF4E4DC, InfoBar: 0xFFEDE6D8,
        GridAlpha: 0x1F,
        Series1: 0xFF1C648E, Series2: 0xFF8E4E00, Series3: 0xFF5E3F8A, Series4: 0xFF0C6262, Series5: 0xFF8E3F68, Series6: 0xFF6A4E10);

    public static ThemePalette For(bool dark) => dark ? Dark : Light;

    /// <summary>The up and down colours for a convention (red-up for China, green-up for the US).</summary>
    public (uint Up, uint Down) UpDown(Meili.Core.Settings.UpDownColorMode mode) =>
        mode == Meili.Core.Settings.UpDownColorMode.RedUp ? (Up, Down) : (Down, Up);

    public static uint Tint(uint color) => ((uint)TintAlpha << 24) | (color & 0x00FFFFFF);

    /// <summary>
    /// Resource keys both front ends install after a theme apply (and that macOS ThemeDictionaries mirror).
    /// WPF-UI resets the Fluent keys to #202020/#FFFFFF on every Apply; these must be written afterwards.
    /// </summary>
    public IEnumerable<(string Key, uint Argb)> ResourceBrushes()
    {
        var accentHover = (0xCC000000) | (Accent & 0x00FFFFFF);
        var accentPressed = (0x99000000) | (Accent & 0x00FFFFFF);
        return
        [
            ("ApplicationBackgroundBrush", Window),
            ("WindowBackground", Window),
            ("SidebarBackgroundBrush", Sidebar),
            ("CardBackgroundFillColorDefaultBrush", Card),
            ("CardBackgroundFillColorSecondaryBrush", Card),
            ("SolidBackgroundFillColorBaseBrush", Window),
            ("SolidBackgroundFillColorSecondaryBrush", Card),
            ("SolidBackgroundFillColorTertiaryBrush", Control),
            ("SolidBackgroundFillColorQuarternaryBrush", Control),
            ("SolidBackgroundFillColorBaseAltBrush", Sidebar),
            ("LayerFillColorDefaultBrush", Window),
            ("LayerFillColorAltBrush", Card),
            ("NavigationViewContentBackground", Window),
            ("LoadingScreenBackground", Window),
            ("ControlFillColorDefaultBrush", Control),
            ("ControlFillColorSecondaryBrush", Control),
            ("ControlFillColorTertiaryBrush", Control),
            ("ControlFillColorInputActiveBrush", Control),
            ("ControlSolidFillColorDefaultBrush", Control),
            ("ControlStrokeColorDefaultBrush", Border),
            ("ControlStrokeColorSecondaryBrush", Border),
            ("CardStrokeColorDefaultBrush", Border),
            ("CardStrokeColorDefaultSolidBrush", Border),
            ("DividerStrokeColorDefaultBrush", Border),
            ("NavigationViewContentGridBorderBrush", Border),
            ("TextFillColorPrimaryBrush", PrimaryText),
            ("TextFillColorSecondaryBrush", SecondaryText),
            ("TextFillColorTertiaryBrush", MutedText),
            ("TextControlPlaceholderForeground", Placeholder),
            ("AccentFillColorDefaultBrush", Accent),
            ("AccentFillColorSecondaryBrush", accentHover),
            ("AccentFillColorTertiaryBrush", accentPressed),
            ("SystemAccentColorPrimaryBrush", Accent),
            ("SystemAccentColorSecondaryBrush", accentHover),
            ("SystemAccentColorTertiaryBrush", accentPressed),
            ("SystemAccentColorBrush", Accent),
            ("AccentTextFillColorPrimaryBrush", Accent),
            ("TextOnAccentFillColorPrimaryBrush", OnAccent),
            ("NavigationViewSelectionIndicatorForeground", Accent),
            ("ErrorBarBrush", ErrorBar),
            ("InfoBarBrush", InfoBar),
            ("InfoBarErrorSeverityBackgroundBrush", ErrorBar),
            ("InfoBarInformationalSeverityBackgroundBrush", InfoBar),
            ("SystemFillColorCriticalBackgroundBrush", ErrorBar),
            ("SystemFillColorAttentionBackgroundBrush", InfoBar),
            ("StaleBadgeBrush", Badge),
            ("BadgeTextBrush", BadgeText),
        ];
    }
}
