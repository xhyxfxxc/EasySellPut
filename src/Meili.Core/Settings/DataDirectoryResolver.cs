namespace Meili.Core.Settings;

public enum DataDirectorySource { CommandLine, Portable, LocalAppData }

public sealed record DataDirectoryInfo(string Path, DataDirectorySource Source);

/// <summary>
/// Decides where the app keeps settings, caches and logs:
/// 1. <c>--data-dir &lt;path&gt;</c> or <c>--data-dir=&lt;path&gt;</c> on the command line;
/// 2. portable mode: a file named <c>portable.flag</c> next to the exe → <c>&lt;exe&gt;\data</c>;
/// 3. otherwise <c>%LOCALAPPDATA%\EasySellPut</c> (leftover <c>美丽稻谷终端</c> or <c>TyuStockApp</c>
///    folders are moved here when possible).
/// </summary>
public static class DataDirectoryResolver
{
    public const string PortableFlagFile = "portable.flag";
    /// <summary>Visible folder name under LocalAppData. Must not contain "tyu".</summary>
    public const string AppFolderName = "EasySellPut";
    /// <summary>Folder used after the first rename and before EasySellPut. Migrated automatically when possible.</summary>
    public const string PreviousAppFolderName = "美丽稻谷终端";
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
        foreach (var oldName in new[] { PreviousAppFolderName, LegacyAppFolderName })
        {
            var old = Path.Combine(localAppData, oldName);
            if (!Directory.Exists(modern) && Directory.Exists(old))
            {
                try
                {
                    Directory.CreateDirectory(localAppData);
                    Directory.Move(old, modern);
                }
                catch // expected: runs before logging exists; keep using the old folder rather than lose data
                {
                    return new(old, DataDirectorySource.LocalAppData);
                }
                break;
            }
        }
        return new(modern, DataDirectorySource.LocalAppData);
    }

    private static string Full(string path, string baseDir) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
}
