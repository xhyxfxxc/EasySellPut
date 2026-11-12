using Meili.Core.Settings;

namespace Meili.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tyu-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Load_WithoutFile_ReturnsDefaults()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("zh-CN", s.Language);
        Assert.Equal(AppThemeMode.System, s.Theme);
        Assert.Equal(ProxyMode.AutoDetect, s.Network.ProxyMode);
        Assert.Contains("SPY", s.Watchlist);
        Assert.Equal(WatchlistGroup.DefaultId, s.WatchlistGroups.Single().Id);
        Assert.Equal(s.Watchlist, s.WatchlistGroups.Single().Symbols);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        store.Update(s => { s.Language = "en-US"; s.Theme = AppThemeMode.Dark; s.Network.ProxyMode = ProxyMode.Manual; s.Network.ManualProxy = "http://127.0.0.1:7890"; s.Watchlist = ["aapl", " msft ", "AAPL"]; s.WatchlistGroups = []; });
        var again = new SettingsStore(_dir).Load();
        Assert.Equal("en-US", again.Language);
        Assert.Equal(AppThemeMode.Dark, again.Theme);
        Assert.Equal(ProxyMode.Manual, again.Network.ProxyMode);
        Assert.Equal("http://127.0.0.1:7890", again.Network.ManualProxy);
        Assert.Equal(["AAPL", "MSFT"], again.Watchlist);
        Assert.Equal(["AAPL", "MSFT"], again.WatchlistGroups.Single().Symbols);
        Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults_AndKeepsBackup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("zh-CN", s.Language);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Normalize_ClampsBadValues()
    {
        var s = new AppSettings { Language = "fr", Network = new NetworkSettings { RequestTimeoutSeconds = 0, MaxRetries = 99 } }.Normalize();
        Assert.Equal("zh-CN", s.Language);
        Assert.Equal(3, s.Network.RequestTimeoutSeconds);
        Assert.Equal(5, s.Network.MaxRetries);
    }
    [Fact]
    public void Load_LegacyFlatWatchlist_MigratesToDefaultGroup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "version": 1, "watchlist": ["IBM", "ge"] }""");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(["IBM", "GE"], s.Watchlist);
        Assert.Equal(["IBM", "GE"], s.WatchlistGroups.Single().Symbols);
        Assert.Equal(WatchlistGroup.DefaultId, s.WatchlistGroups.Single().Id);
    }
}
public class DataDirectoryResolverTests : IDisposable
{
    private readonly string _exe = Path.Combine(Path.GetTempPath(), "tyu-exe-" + Guid.NewGuid().ToString("N"));
    public DataDirectoryResolverTests() => Directory.CreateDirectory(_exe);
    public void Dispose() { try { Directory.Delete(_exe, true); } catch { } }

    [Fact]
    public void CommandLine_SeparateArgument()
    {
        var target = Path.Combine(Path.GetTempPath(), "x1");
        var r = DataDirectoryResolver.Resolve(["--data-dir", target], _exe, "/lad");
        Assert.Equal(DataDirectorySource.CommandLine, r.Source);
        Assert.Equal(Path.GetFullPath(target), r.Path);
    }

    [Fact]
    public void CommandLine_EqualsForm_RelativeToExe()
    {
        var r = DataDirectoryResolver.Resolve(["--data-dir=mydata"], _exe, "/lad");
        Assert.Equal(Path.Combine(_exe, "mydata"), r.Path);
    }

    [Fact]
    public void PortableFlag_UsesExeDataFolder()
    {
        File.WriteAllText(Path.Combine(_exe, DataDirectoryResolver.PortableFlagFile), "");
        var r = DataDirectoryResolver.Resolve([], _exe, "/lad");
        Assert.Equal(DataDirectorySource.Portable, r.Source);
        Assert.Equal(Path.Combine(_exe, "data"), r.Path);
    }

    [Fact]
    public void Default_UsesLocalAppData_WithoutTyuInFolderName()
    {
        var lad = Path.Combine(Path.GetTempPath(), "tyu-lad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lad);
        try
        {
            var r = DataDirectoryResolver.Resolve([], _exe, lad);
            Assert.Equal(DataDirectorySource.LocalAppData, r.Source);
            Assert.Equal(Path.Combine(lad, DataDirectoryResolver.AppFolderName), r.Path);
            Assert.DoesNotContain("tyu", DataDirectoryResolver.AppFolderName, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(lad, true); } catch { } }
    }

    [Fact]
    public void MigratesLegacyTyuStockAppFolderWhenPossible()
    {
        var lad = Path.Combine(Path.GetTempPath(), "tyu-lad-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(lad, DataDirectoryResolver.LegacyAppFolderName);
        var modern = Path.Combine(lad, DataDirectoryResolver.AppFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{ }");
        try
        {
            var r = DataDirectoryResolver.Resolve([], _exe, lad);
            Assert.Equal(modern, r.Path);
            Assert.True(File.Exists(Path.Combine(modern, "settings.json")));
            Assert.False(Directory.Exists(legacy));
        }
        finally { try { Directory.Delete(lad, true); } catch { } }
    }

    [Fact]
    public void PrefersModernFolderIfBothExist()
    {
        var lad = Path.Combine(Path.GetTempPath(), "tyu-lad-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(lad, DataDirectoryResolver.LegacyAppFolderName);
        var modern = Path.Combine(lad, DataDirectoryResolver.AppFolderName);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(modern);
        File.WriteAllText(Path.Combine(modern, "marker.txt"), "new");
        try
        {
            var r = DataDirectoryResolver.Resolve([], _exe, lad);
            Assert.Equal(modern, r.Path);
            Assert.True(Directory.Exists(legacy));
        }
        finally { try { Directory.Delete(lad, true); } catch { } }
    }
}

/// <summary>G3: typed indicator parameters are clamped into usable ranges and survive a save/load.</summary>
public class IndicatorSettingsTests
{
    [Fact]
    public void Normalized_clamps_and_keeps_macd_fast_below_slow()
    {
        var n = new Meili.Core.Settings.IndicatorSettings { MaPeriod = 0, BollPeriod = 1, BollWidth = 9, MacdFast = 30, MacdSlow = 26, RsiPeriod = 999 }.Normalized();
        Assert.Equal(1, n.MaPeriod);
        Assert.Equal(2, n.BollPeriod);
        Assert.Equal(5, n.BollWidth);
        Assert.Equal(30, n.MacdFast);
        Assert.Equal(31, n.MacdSlow);
        Assert.Equal(250, n.RsiPeriod);
        Assert.Equal(new Meili.Core.Settings.IndicatorSettings(), new Meili.Core.Settings.IndicatorSettings().Normalized());
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var s = new Meili.Core.Settings.AppSettings();
        s.Charts.Indicators = new Meili.Core.Settings.IndicatorSettings { MaPeriod = 60, KdjM2 = 5 };
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<Meili.Core.Settings.AppSettings>(json)!;
        Assert.Equal(s.Charts.Indicators, back.Charts.Indicators);
    }
}
