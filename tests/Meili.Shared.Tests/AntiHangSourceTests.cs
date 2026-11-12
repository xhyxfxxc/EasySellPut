using System.Text.RegularExpressions;

namespace Meili.Shared.Tests;

/// <summary>
/// Beachball / deadlock guard (docs/06): blocking on a task from UI code (.Result, .Wait(), GetAwaiter().GetResult())
/// is the classic way to freeze the main thread, so no source file under src/ may do it.
/// </summary>
public class AntiHangSourceTests
{
    private static readonly Regex SyncOverAsync = new(@"\.Wait\(\)|\.GetAwaiter\(\)\.GetResult\(\)|\.Result\b(?!\s*=)", RegexOptions.Compiled);

    [Fact]
    public void No_sync_over_async_in_app_sources()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && SyncOverAsync.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.Empty(hits);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "MeiliTerminal.slnx"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }
}
