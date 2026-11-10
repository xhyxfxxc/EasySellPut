namespace TyuStock.Core.Settings;

public enum DataDirectorySource { CommandLine, Portable, LocalAppData }

public sealed record DataDirectoryInfo(string Path, DataDirectorySource Source);

/// <summary>
/// Decides where the app keeps settings, caches and logs:
/// 1. <c>--data-dir &lt;path&gt;</c> or <c>--data-dir=&lt;path&gt;</c> on the command line;
/// 2. portable mode: a file named <c>portable.flag</c> next to the exe → <c>&lt;exe&gt;\data</c>;
/// 3. otherwise <c>%LOCALAPPDATA%\TyuStockApp</c>.
/// </summary>
public static class DataDirectoryResolver
{
    public const string PortableFlagFile = "portable.flag";
    public const string AppFolderName = "TyuStockApp";

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
        return new(Path.Combine(localAppData, AppFolderName), DataDirectorySource.LocalAppData);
    }

    private static string Full(string path, string baseDir) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
}
