namespace Meili.Shared.Tests;

[Collection(nameof(UiPlatformCollection))] // shares the static LocalizationService.Current
public class LocalizationTests
{
    [Fact]
    public void Both_languages_are_embedded_with_the_same_keys()
    {
        var zh = LocalizationService.Load("zh-CN");
        var en = LocalizationService.Load("en-US");
        Assert.True(zh.Count > 500);
        Assert.Empty(zh.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(zh.Keys));
        Assert.All(zh, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void Apply_switches_text_and_raises_applying_before_changed()
    {
        var loc = new LocalizationService();
        var order = new List<string>();
        loc.Applying += lang => order.Add("applying:" + lang + ":" + loc["Net_FromMac"]);
        loc.LanguageChanged += (_, _) => order.Add("changed");

        loc.Apply("en-US");

        Assert.Equal(["applying:en-US:from macOS proxy setting", "changed"], order);
        Assert.False(loc.IsChinese);
        Assert.Same(loc, LocalizationService.Current);
    }

    [Fact]
    public void Unknown_language_falls_back_to_chinese_and_unknown_key_to_itself()
    {
        var loc = new LocalizationService();
        loc.Apply("fr-FR");
        Assert.Equal("zh-CN", loc.Language);
        Assert.Equal("来自 macOS 代理设置", loc["Net_FromMac"]);
        Assert.Equal("No_Such_Key", loc["No_Such_Key"]);
    }
}
