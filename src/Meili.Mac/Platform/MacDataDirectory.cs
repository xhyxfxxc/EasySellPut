using Meili.Core.Settings;

namespace Meili.Mac.Platform;

/// <summary>
/// Where the Mac app keeps its data: <c>--data-dir</c>; else <c>data/</c> next to the .app when a <c>portable.flag</c>
/// sits beside it; else <c>~/Library/Application Support/美丽稻谷终端</c>.
/// </summary>
public static class MacDataDirectory
{
    public static DataDirectoryInfo Resolve(IReadOnlyList<string> args, string exeDirectory, string home)
    {
        var appSupport = Path.Combine(home, "Library", "Application Support");
        var portableBase = BundleParent(exeDirectory);
        // Gatekeeper "App Translocation" runs a quarantined app from a random read-only copy; a portable data folder
        // there would be lost, so translocated launches use Application Support (使用说明 explains how to avoid it).
        if (portableBase.Contains("/AppTranslocation/", StringComparison.Ordinal))
            portableBase = exeDirectory;
        return DataDirectoryResolver.Resolve(args, portableBase, appSupport);
    }

    /// <summary>The folder holding <c>X.app</c> when running from a bundle (…/X.app/Contents/MacOS/), else the exe folder.</summary>
    public static string BundleParent(string exeDirectory)
    {
        var dir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(exeDirectory));
        if (dir is { Name: "MacOS", Parent: { Name: "Contents", Parent: { } app } } && app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return app.Parent!.FullName;
        return dir.FullName;
    }
}
