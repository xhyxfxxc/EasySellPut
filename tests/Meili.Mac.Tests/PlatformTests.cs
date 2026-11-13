using Meili.Core.Settings;
using Meili.Mac.Platform;

namespace Meili.Mac.Tests;

public class PlatformTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meili-mac-dd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Bundle_parent_is_the_folder_holding_the_app()
    {
        var exe = Path.Combine(_root, "EasySellPut.app", "Contents", "MacOS") + "/";
        Assert.Equal(_root, MacDataDirectory.BundleParent(exe));
        Assert.Equal(Path.Combine(_root, "bin"), MacDataDirectory.BundleParent(Path.Combine(_root, "bin")));
    }

    [Fact]
    public void Data_goes_to_application_support_unless_portable_flag_sits_next_to_the_app()
    {
        var exe = Path.Combine(_root, "X.app", "Contents", "MacOS");
        Directory.CreateDirectory(exe);
        var home = Path.Combine(_root, "home");
        var normal = MacDataDirectory.Resolve([], exe, home);
        Assert.Equal(Path.Combine(home, "Library", "Application Support", "EasySellPut"), normal.Path);
        Assert.Equal(DataDirectorySource.LocalAppData, normal.Source);

        File.WriteAllText(Path.Combine(_root, DataDirectoryResolver.PortableFlagFile), "");
        var portable = MacDataDirectory.Resolve([], exe, home);
        Assert.Equal(Path.Combine(_root, "data"), portable.Path);
        Assert.Equal(DataDirectorySource.Portable, portable.Source);
    }

    [Fact]
    public void Translocated_app_never_uses_a_portable_folder()
    {
        // Translocation paths are macOS paths; on Windows DirectoryInfo rewrites them with backslashes.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "macOS path semantics");
        var bundleParent = Path.Combine(_root, "private", "var", "folders", "AppTranslocation", "ABC", "d");
        var exe = Path.Combine(bundleParent, "X.app", "Contents", "MacOS");
        Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(bundleParent, DataDirectoryResolver.PortableFlagFile), "");
        var info = MacDataDirectory.Resolve([], exe, Path.Combine(_root, "home"));
        Assert.Equal(DataDirectorySource.LocalAppData, info.Source);
    }

    [Fact]
    public void Notification_script_quotes_user_text()
    {
        var script = MacNotifier.Script("AAPL \"提醒\"", "a\\b\nc");
        Assert.Equal("display notification \"a\\\\b\nc\" with title \"AAPL \\\"提醒\\\"\"", script);
    }
}
