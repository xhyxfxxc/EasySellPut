using System.IO;
using System.Xml.Linq;

namespace TyuStock.App.Tests;

/// <summary>Every UI string must exist in both languages, so a missing translation fails the build instead of showing a raw key.</summary>
public class StringResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Dictionary<string, string> Load(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "TyuStockApp.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var path = Path.Combine(dir!, "src", "TyuStock.App", "Resources", "Strings", $"Strings.{lang}.xaml");
        return XDocument.Load(path).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value);
    }

    [Fact]
    public void Chinese_and_English_have_the_same_keys()
    {
        var zh = Load("zh-CN");
        var en = Load("en-US");
        Assert.Empty(zh.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(zh.Keys));
    }

    [Fact]
    public void No_string_is_empty()
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
            Assert.All(Load(lang), kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{lang}:{kv.Key} is empty"));
    }

    [Fact]
    public void No_visible_string_mentions_tyu()
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
            Assert.All(Load(lang), kv => Assert.DoesNotContain("tyu", kv.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>D5: no "coming in a later release" text for features that shipped (alerts, export) or never will.</summary>
    [Fact]
    public void No_stale_coming_soon_strings()
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
            Assert.All(Load(lang), kv =>
            {
                Assert.False(kv.Key.EndsWith("Soon", StringComparison.Ordinal) || kv.Key.StartsWith("Placeholder_", StringComparison.Ordinal), kv.Key);
                Assert.DoesNotMatch("(?i)coming soon|later release|后续版本|即将推出", kv.Value);
            });
    }
}

public class HostSafetyTests
{
    [Fact]
    public void Test_host_never_creates_the_real_App_or_a_window()
    {
        WpfHost.Run(() =>
        {
            Assert.IsNotType<App>(System.Windows.Application.Current);
            Assert.Empty(System.Windows.Application.Current.Windows);
            Assert.IsType<FakeMarketDataService>(TyuStock.App.Services.AppServices.Get<TyuStock.Core.Abstractions.IMarketDataService>());
        });
    }
}
