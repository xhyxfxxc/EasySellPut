using System.Text.RegularExpressions;
using TyuStock.Core.Diagnostics;

namespace TyuStock.Core.Tests;

/// <summary>
/// H2 regression net: every broad <c>catch</c> (bare, or <c>catch (Exception …)</c>) in src must log, rethrow, surface the
/// failure (health/fail/check records) or carry an <c>// expected:</c> comment explaining why silence is correct.
/// </summary>
[Collection("AppLog")]
public class NoSilentCatchTests
{
    private static readonly string[] Handled = ["AppLog", "throw", "Log(", "RecordError", "expected:", "Health.", "Fail(", "_checks."];

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "TyuStockApp.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    internal static IEnumerable<string> Violations(string source, string file)
    {
        foreach (Match m in Regex.Matches(source, @"\bcatch\b"))
        {
            var open = source.IndexOf('{', m.Index);
            if (open < 0) continue;
            var header = source[m.Index..open];
            var broad = Regex.IsMatch(header, @"^catch\s*$") || Regex.IsMatch(header, @"^catch\s*\(\s*(System\.)?Exception\b");
            if (!broad) continue;
            int depth = 0, end = open;
            for (; end < source.Length; end++)
            {
                if (source[end] == '{') depth++;
                else if (source[end] == '}' && --depth == 0) break;
            }
            var lineStart = source.LastIndexOf('\n', m.Index) + 1;
            var lineEnd = source.IndexOf('\n', m.Index);
            var context = source[open..Math.Min(end + 1, source.Length)] + source[lineStart..(lineEnd < 0 ? source.Length : lineEnd)];
            var closeLineEnd = source.IndexOf('\n', end);
            context += source[end..(closeLineEnd < 0 ? source.Length : closeLineEnd)];
            if (!Handled.Any(context.Contains))
                yield return $"{file}:{source[..m.Index].Count(c => c == '\n') + 1}";
        }
    }

    [Fact]
    public void No_broad_catch_swallows_silently()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var bad = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Violations(File.ReadAllText(f), Path.GetRelativePath(src, f)))
            .ToList();
        Assert.True(bad.Count == 0, "silent catch blocks:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Scanner_flags_silent_and_accepts_handled_blocks()
    {
        Assert.Single(Violations("try { } catch { }", "x"));
        Assert.Single(Violations("try { } catch (Exception) { return 0; }", "x"));
        Assert.Empty(Violations("try { } catch { } // expected: probe", "x"));
        Assert.Empty(Violations("try { } catch (Exception ex) { AppLog.Warn(\"a\", \"b\", ex); }", "x"));
        Assert.Empty(Violations("try { } catch (IOException) { }", "x"));          // narrow, deliberate
    }

    [Fact]
    public void Errors_keep_the_stack_and_warnings_keep_the_root_cause()
    {
        var dir = Path.Combine(Path.GetTempPath(), "h2log-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.Initialize(dir);
            Exception Boom() { try { throw new InvalidOperationException("outer", new TimeoutException("root")); } catch (Exception e) { return e; } }
            AppLog.Error("h2test", "err", Boom());
            AppLog.Warn("h2test", "warn", Boom());
            var file = Directory.GetFiles(Path.Combine(dir, "logs"), "app-*.log").Single();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var text = new StreamReader(fs).ReadToEnd();
            Assert.Contains("at TyuStock.Core.Tests.NoSilentCatchTests", text);   // stack trace for errors
            Assert.Contains("<- TimeoutException: root", text);                       // root cause for warnings
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
