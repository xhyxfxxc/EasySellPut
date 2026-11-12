namespace Meili.Core.Settings;

public enum DataDirectorySource { CommandLine, Portable, LocalAppData }

public sealed record DataDirectoryInfo(string Path, DataDirectorySource Source);

/// <summary>
/// Decides where the app keeps settings, caches and logs:
/// 1. <c>--data-dir &lt;path&gt;</c> or <c>--data-dir=&lt;path&gt;</c> on the command line;
/// 2. portable mode: a file named <c>portable.flag</c> next to the exe → <c>&lt;exe&gt;\data</c>;
/// 3. otherwise <c>%LOCALAPPDATA%\美丽稻谷终端</c> (a leftover <c>TyuStockApp</c> folder is moved here when possible).
/// </summary>
public static class DataDirectoryResolver
{
    public const string PortableFlagFile = "portable.flag";
    /// <summary>Visible folder name under LocalAppData. Must not contain "tyu".</summary>
    public const string AppFolderName = "美丽稻谷终端";
    /// <summary>Folder used by builds before the product was renamed. Migrated automatically when possible.</summary>
    public const string LegacyAppFolderName = "TyuStockApp";

    public static DataDirectoryInfo Resolve(IReadOnlyList<string> args, string exeDirectory, string localAppData)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
                return new(Full(a["--data-dir=".Length..].Trim('"'), exeDirectory), DataDirectorySource.CommandLine);
            if (a.Equals("--data-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return new(Full(args[i + 1].Trim('"'), exeDirectory), DataDirectorySource.CommandLine);
        }
        if (File.Exists(Path.Combine(exeDirectory, PortableFlagFile)))
            return new(Path.Combine(exeDirectory, "data"), DataDirectorySource.Portable);

        var modern = Path.Combine(localAppData, AppFolderName);
        var legacy = Path.Combine(localAppData, LegacyAppFolderName);
        if (!Directory.Exists(modern) && Directory.Exists(legacy))
        {
            try
            {
                Directory.CreateDirectory(localAppData);
                Directory.Move(legacy, modern);
            }
            catch // expected: runs before logging exists; keep using the old folder rather than lose data
            {
                return new(legacy, DataDirectorySource.LocalAppData);
            }
        }
        return new(modern, DataDirectorySource.LocalAppData);
    }

    private static string Full(string path, string baseDir) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
}