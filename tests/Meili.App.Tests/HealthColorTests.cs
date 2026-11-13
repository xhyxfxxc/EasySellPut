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
            // 0.4.2 tokens use one colour for DownState and Unknown; Healthy and Degraded stay distinct, and none equals up/down.
            Assert.Equal(3, health.Distinct().Count());
            Assert.Equal(C("DownStateBrush"), C("UnknownBrush"));
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
    private static Color C32(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    private static Color[] Backs(ThemePalette p) => [C32(p.Window), C32(p.Card), C32(p.Sidebar), C32(p.Chart)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tones_meet_text_contrast(bool dark)
    {
        var p = SemanticPalette.For(dark);
        foreach (var c in new[] { p.Red, p.Green, p.Healthy, p.Degraded, p.DownState, p.Unknown })
            foreach (var bg in Backs(ThemePalette.For(dark)))
                Assert.True(UiAudit.Contrast(c, bg) >= 4.5, $"{c} on {bg}: {UiAudit.Contrast(c, bg):0.00}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Up_down_text_reads_on_its_tinted_tag(bool dark)
    {
        var p = SemanticPalette.For(dark);
        var pal = ThemePalette.For(dark);
        foreach (var card in new[] { C32(pal.Card), C32(pal.Window) })
        foreach (var c in new[] { p.Red, p.Green })
        {
            var a = SemanticPalette.TintAlpha / 255.0;
            byte M(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
            var tag = Color.FromRgb(M(c.R, card.R), M(c.G, card.G), M(c.B, card.B));
            Assert.True(UiAudit.Contrast(c, tag) >= 4.5, $"{c} on its tag: {UiAudit.Contrast(c, tag):0.00}");
        }
        Assert.True(UiAudit.Contrast(C32(pal.PrimaryText), p.RowHighlight) >= 4.5);
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
        var pal = ThemePalette.For(dark);
        var text = C32(pal.PrimaryText);
        foreach (var row in Backs(pal).Append(p.RowHighlight))
        {
            var shaded = Over(p.ItmTint, row);
            Assert.True(UiAudit.Contrast(shaded, row) >= 1.05, $"ITM shade invisible on {row}");
            Assert.True(UiAudit.Contrast(text, shaded) >= 4.5, $"text on ITM shade: {UiAudit.Contrast(text, shaded):0.00}");
        }
        foreach (var bg in Backs(pal))
            Assert.True(UiAudit.Contrast(Over(p.Placeholder, bg), bg) >= 4.5, $"placeholder on {bg}");
    }

    [Fact]
    public void Badge_fill_reads_with_dark_text()
        => Assert.True(UiAudit.Contrast(C32(ThemePalette.Light.BadgeText), SemanticPalette.Badge) >= 4.5);

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
