using System.Windows;
using System.Windows.Media;
using Meili.App.Services;
using Meili.Core.Settings;
using Meili.Data.Routing;

namespace Meili.App.Tests;

/// <summary>E1: health colours never coincide with up/down colours, and each state has its own icon.</summary>
public class HealthColorTests
{
    [Fact]
    public void Health_brushes_differ_from_both_up_down_conventions()
    {
        WpfHost.Run(() =>
        {
            Color C(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            var health = new[] { "HealthyBrush", "DegradedBrush", "DownStateBrush", "UnknownBrush" }.Select(C).ToList();
            Assert.Equal(4, health.Distinct().Count());
            try
            {
                foreach (var mode in new[] { UpDownColorMode.RedUp, UpDownColorMode.GreenUp })
                {
                    ThemeService.ApplyUpDownColors(mode);
                    Assert.DoesNotContain(C("UpBrush"), health);
                    Assert.DoesNotContain(C("DownBrush"), health);
                }
            }
            finally { ThemeService.ApplyUpDownColors(AppServices.Get<SettingsStore>().Current.EffectiveUpDownColors()); }
        });
    }

    [Fact]
    public void Every_state_has_a_distinct_icon()
    {
        var icons = Enum.GetValues<HealthState>().Select(Meili.App.Services.HealthIconConverter.Icon).ToList();
        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Fact]
    public void Settings_choice_marks_colours_as_chosen()
    {
        WpfHost.Run(() =>
        {
            var vm = AppServices.Get<SettingsViewModel>();
            var store = AppServices.Get<SettingsStore>();
            var before = store.Current.UpDownColors;
            vm.UpDownIndex = vm.UpDownIndex == 0 ? 1 : 0;
            Assert.True(store.Current.UpDownColorsChosen);
            store.Update(s => { s.UpDownColors = before; s.UpDownColorsChosen = false; });
            ThemeService.ApplyUpDownColors(store.Current.EffectiveUpDownColors());
        });
    }
}

/// <summary>E4: every semantic tone keeps ≥ 4.5:1 text contrast on its theme's page and card backgrounds.</summary>
public class SemanticPaletteTests
{
    private static readonly Color[] LightBacks = [Colors.White, Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0xFA, 0xFA, 0xFA)];
    private static readonly Color[] DarkBacks = [Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0x2B, 0x2B, 0x2B)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tones_meet_text_contrast(bool dark)
    {
        var p = SemanticPalette.For(dark);
        foreach (var c in new[] { p.Red, p.Green, p.Healthy, p.Degraded, p.DownState, p.Unknown })
            foreach (var bg in dark ? DarkBacks : LightBacks)
                Assert.True(UiAudit.Contrast(c, bg) >= 4.5, $"{c} on {bg}: {UiAudit.Contrast(c, bg):0.00}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Up_down_text_reads_on_its_tinted_tag(bool dark)
    {
        var p = SemanticPalette.For(dark);
        // Darkest-contrast cards each theme puts tags on (dashboard card in dark is #363636).
        var cards = dark ? new[] { Color.FromRgb(0x2B, 0x2B, 0x2B), Color.FromRgb(0x36, 0x36, 0x36) } : new[] { Colors.White, Color.FromRgb(0xF3, 0xF3, 0xF3) };
        foreach (var card in cards)
        foreach (var c in new[] { p.Red, p.Green })
        {
            var a = SemanticPalette.TintAlpha / 255.0;
            byte M(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
            var tag = Color.FromRgb(M(c.R, card.R), M(c.G, card.G), M(c.B, card.B));
            Assert.True(UiAudit.Contrast(c, tag) >= 4.5, $"{c} on its tag: {UiAudit.Contrast(c, tag):0.00}");
        }
        var text = dark ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1C);
        Assert.True(UiAudit.Contrast(text, p.RowHighlight) >= 7);
    }

    private static Color Over(Color fg, Color bg)
    {
        var a = fg.A / 255.0;
        byte M(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromRgb(M(fg.R, bg.R), M(fg.G, bg.G), M(fg.B, bg.B));
    }

    /// <summary>E4 final pass: the ITM shade is visible on plain and highlighted rows; placeholders keep 4.5:1.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Itm_shade_and_placeholder_read_in_both_themes(bool dark)
    {
        var p = SemanticPalette.For(dark);
        var text = dark ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1C);
        foreach (var row in (dark ? DarkBacks : LightBacks).Append(p.RowHighlight))
        {
            var shaded = Over(p.ItmTint, row);
            Assert.True(UiAudit.Contrast(shaded, row) >= 1.05, $"ITM shade invisible on {row}");
            Assert.True(UiAudit.Contrast(text, shaded) >= 7);
        }
        // Inputs: page backgrounds plus the hovered dark input (#4D4D4D) the audit flagged.
        foreach (var bg in dark ? DarkBacks.Append(Color.FromRgb(0x4D, 0x4D, 0x4D)) : LightBacks)
            Assert.True(UiAudit.Contrast(Over(p.Placeholder, bg), bg) >= 4.5, $"placeholder on {bg}");
    }

    [Fact]
    public void Badge_fill_reads_with_dark_text()
        => Assert.True(UiAudit.Contrast(Color.FromRgb(0x1A, 0x1A, 0x1A), SemanticPalette.Badge) >= 4.5);

    [Fact]
    public void Theme_switch_retones_brushes()
    {
        WpfHost.Run(() =>
        {
            Color C(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            var theme = AppServices.Get<ThemeService>();
            var raised = 0;
            EventHandler h = (_, _) => raised++;
            ThemeService.ColorsChanged += h;
            try
            {
                theme.Apply(AppThemeMode.Light);
                Assert.Equal(SemanticPalette.Light.Healthy, C("HealthyBrush"));
                theme.Apply(AppThemeMode.Dark);
                Assert.Equal(SemanticPalette.Dark.Healthy, C("HealthyBrush"));
                Assert.Contains(C("UpBrush"), new[] { SemanticPalette.Dark.Red, SemanticPalette.Dark.Green });
                Assert.Equal(2, raised);   // view models holding a brush are told to re-fetch it
            }
            finally { ThemeService.ColorsChanged -= h; theme.Apply(AppThemeMode.Dark); }
        });
    }
}
