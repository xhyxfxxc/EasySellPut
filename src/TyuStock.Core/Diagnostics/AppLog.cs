using System.Text;

namespace TyuStock.Core.Diagnostics;

public enum LogLevel { Info, Warn, Error }

/// <summary>
/// Minimal thread-safe rolling text log: <c>&lt;data&gt;\logs\app-yyyyMMdd.log</c>, one file per day, at most
/// <see cref="MaxBytesPerDay"/> per file, files older than <see cref="KeepDays"/> are deleted at start-up.
/// Never throws. H2: this stays the single logging facade (Serilog was evaluated and not added: same features here, one dependency less in the single-file exe).
/// </summary>
public static class AppLog
{
    public const long MaxBytesPerDay = 5 * 1024 * 1024;
    public const int KeepDays = 14;
    private static readonly object Gate = new();
    private static string? _dir;
    /// <summary>Test hook: replaces the clock.</summary>
    public static Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;
    /// <summary>Raised after each write (UI can surface warnings; tests can observe).</summary>
    public static event Action<LogLevel, string, string>? Written;

    public static string? Directory => _dir;

    public static void Initialize(string dataDirectory)
    {
        try
        {
            var dir = Path.Combine(dataDirectory, "logs");
            System.IO.Directory.CreateDirectory(dir);
            lock (Gate) _dir = dir;
            var cutoff = Now().AddDays(-KeepDays).DateTime;
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "app-*.log"))
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
        }
        catch { } // expected: logging must never take the app down
    }

    public static void Info(string area, string message) => Write(LogLevel.Info, area, message, null);
    public static void Warn(string area, string message, Exception? ex = null) => Write(LogLevel.Warn, area, message, ex);
    public static void Error(string area, string message, Exception? ex = null) => Write(LogLevel.Error, area, message, ex);

    public static void Write(LogLevel level, string area, string message, Exception? ex)
    {
        try
        {
            var now = Now();
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append(' ')
                .Append(level.ToString().ToUpperInvariant().PadRight(5)).Append(' ')
                .Append('[').Append(area).Append("] ").Append(message);
            if (ex is not null)
            {
                // H2: errors keep the full stack; warnings keep type, message and the innermost cause.
                if (level == LogLevel.Error) line.AppendLine().Append(ex);
                else
                {
                    line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    var inner = ex.GetBaseException();
                    if (!ReferenceEquals(inner, ex)) line.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
                }
            }
            line.AppendLine();
            lock (Gate)
            {
                if (_dir is null) return;
                var file = Path.Combine(_dir, $"app-{now:yyyyMMdd}.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytesPerDay) return;
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
            Written?.Invoke(level, area, message);
        }
        catch { } // expected: logging must never take the app down
    }
}
