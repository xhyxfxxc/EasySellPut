namespace Meili.Shared.Services;

/// <summary>
/// E4: theme-specific tones (0xAARRGGBB) for price direction and health states, all ≥ 4.5:1 on the theme's page and
/// card backgrounds. One table for both front ends; WPF and Avalonia turn them into their own brushes.
/// </summary>
public sealed record SemanticColors(uint Red, uint Green, uint Healthy, uint Degraded, uint DownState, uint Unknown, uint RowHighlight,
    uint ItmTint, uint Placeholder)
{
    public static readonly SemanticColors Light = new(0xFFB8282E, 0xFF0F6B45, 0xFF2563B8, 0xFF8F5E00, 0xFF5E6875, 0xFF6B6B6B, 0xFFEADCF2,
        0x0F000000, 0xA8000000);
    public static readonly SemanticColors Dark = new(0xFFF89CA0, 0xFF45C98E, 0xFF6AA6EC, 0xFFD99A1E, 0xFF9AA4B2, 0xFFA0A0A0, 0xFF3E2F48,
        0x17FFFFFF, 0xB8FFFFFF);

    /// <summary>Badge fill behind dark (#1A1A1A) text, e.g. the "stale" tag; the same in both themes.</summary>
    public const uint Badge = 0xFFD99A1E;

    /// <summary>Alpha of the up/down tag fills behind up/down text.</summary>
    public const byte TintAlpha = 0x1A;

    public static SemanticColors For(bool dark) => dark ? Dark : Light;

    /// <summary>The up and down colours for a convention (red-up for China, green-up for the US).</summary>
    public (uint Up, uint Down) UpDown(Meili.Core.Settings.UpDownColorMode mode) =>
        mode == Meili.Core.Settings.UpDownColorMode.RedUp ? (Red, Green) : (Green, Red);

    public static uint Tint(uint color) => ((uint)TintAlpha << 24) | (color & 0x00FFFFFF);
}
