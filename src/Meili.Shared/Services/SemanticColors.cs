namespace Meili.Shared.Services;

/// <summary>
/// Price-direction and health tones from <see cref="ThemePalette"/>. Kept so existing call sites
/// (<c>SemanticColors.Light.Red</c>, <see cref="UpDown"/>) keep compiling; new code should read the palette.
/// </summary>
public sealed record SemanticColors(uint Red, uint Green, uint Healthy, uint Degraded, uint DownState, uint Unknown, uint RowHighlight,
    uint ItmTint, uint Placeholder)
{
    public static SemanticColors Light { get; } = From(ThemePalette.Light);
    public static SemanticColors Dark { get; } = From(ThemePalette.Dark);

    /// <summary>Badge fill behind <see cref="BadgeText"/> (the stale-quote tag); the same in both themes.</summary>
    public static uint Badge => ThemePalette.Light.Badge;

    /// <summary>Text on <see cref="Badge"/>.</summary>
    public static uint BadgeText => ThemePalette.Light.BadgeText;

    /// <summary>Alpha of the up/down tag fills behind up/down text.</summary>
    public const byte TintAlpha = ThemePalette.TintAlpha;

    public static SemanticColors For(bool dark) => dark ? Dark : Light;

    /// <summary>The up and down colours for a convention (red-up for China, green-up for the US).</summary>
    public (uint Up, uint Down) UpDown(Meili.Core.Settings.UpDownColorMode mode) =>
        mode == Meili.Core.Settings.UpDownColorMode.RedUp ? (Red, Green) : (Green, Red);

    public static uint Tint(uint color) => ThemePalette.Tint(color);

    private static SemanticColors From(ThemePalette p) =>
        new(p.Up, p.Down, p.Healthy, p.Degraded, p.DownState, p.Unknown, p.RowHighlight, p.ItmTint, p.Placeholder);
}
