using TyuStock.Core.Settings;

namespace TyuStock.Core.Tests;

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
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        store.Update(s => { s.Language = "en-US"; s.Theme = AppThemeMode.Dark; s.Network.ProxyMode = ProxyMode.Manual; s.Network.ManualProxy = "http://127.0.0.1:7890"; s.Watchlist = ["aapl", " msft ", "AAPL"]; });
        var again = new SettingsStore(_dir).Load();
        Assert.Equal("en-US", again.Language);
        Assert.Equal(AppThemeMode.Dark, again.Theme);
        Assert.Equal(ProxyMode.Manual, again.Network.ProxyMode);
        Assert.Equal("http://127.0.0.1:7890", again.Network.ManualProxy);
        Assert.Equal(["AAPL", "MSFT"], again.Watchlist);
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
    public void Default_UsesLocalAppData()
    {
        var r = DataDirectoryResolver.Resolve([], _exe, "/lad");
        Assert.Equal(Path.Combine("/lad", "TyuStockApp"), r.Path);
    }
}
