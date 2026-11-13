namespace Meili.Shared.Tests;

/// <summary>
/// 0.4.2 palette: body, muted, up/down, health and accent text all keep WCAG AA contrast on every surface
/// the tokens were designed for. Surfaces are not compared to each other; tints (ItmTint, up/down tag fills)
/// are not required to be 4.5:1 against the surface they sit on.
/// </summary>
public class PaletteContrastTests
{
    public static TheoryData<bool> Themes() => new() { false, true };

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_and_status_tones_meet_4_5_on_every_surface(bool dark)
    {
        var p = ThemePalette.For(dark);
        uint[] tones = [p.PrimaryText, p.SecondaryText, p.MutedText, p.Up, p.Down, p.Healthy, p.Degraded, p.Accent];
        uint[] surfaces = [p.Window, p.Card, p.Sidebar, p.Chart];
        foreach (var tone in tones)
        foreach (var surface in surfaces)
        {
            var ratio = Contrast(tone, surface);
            Assert.True(ratio >= 4.5, $"{(dark ? "dark" : "light")} #{tone:X8} on #{surface:X8} is {ratio:0.00}:1");
        }
    }

    /// <summary>Relative-luminance contrast (WCAG 2), ignoring alpha — these tokens are opaque.</summary>
    internal static double Contrast(uint a, uint b)
    {
        double L(uint c)
        {
            static double Lin(byte v)
            {
                var s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin((byte)(c >> 16)) + 0.7152 * Lin((byte)(c >> 8)) + 0.0722 * Lin((byte)c);
        }
        var (lo, hi) = L(a) < L(b) ? (L(a), L(b)) : (L(b), L(a));
        return (hi + 0.05) / (lo + 0.05);
    }
}
