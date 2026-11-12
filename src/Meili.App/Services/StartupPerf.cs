using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Meili.Core.Diagnostics;

namespace Meili.App.Services;

/// <summary>C6: one timing/memory sample; times are milliseconds since the process started.</summary>
public sealed record PerfMark(string Name, double SinceStartMs, double WorkingSetMb, double PrivateMb, double ManagedMb);

/// <summary>
/// C6: <c>--perf</c> startup timeline. Marks are taken at fixed points (app start, services built, window shown, first
/// render, idle, and after the self-check when combined with <c>--smoke</c>) and written to <c>logs\perf.json</c> and the log.
/// </summary>
public sealed class StartupPerf
{
    private readonly DateTime _processStart = Process.GetCurrentProcess().StartTime;
    private readonly List<PerfMark> _marks = [];
    private readonly string _path;

    public StartupPerf(string dataDirectory) => _path = Path.Combine(dataDirectory, "logs", "perf.json");

    public IReadOnlyList<PerfMark> Marks => _marks;

    public void Mark(string name)
    {
        using var p = Process.GetCurrentProcess();
        const double Mb = 1024 * 1024;
        var m = new PerfMark(name, Math.Round((DateTime.Now - _processStart).TotalMilliseconds, 1),
            Math.Round(p.WorkingSet64 / Mb, 1), Math.Round(p.PrivateMemorySize64 / Mb, 1), Math.Round(GC.GetTotalMemory(false) / Mb, 1));
        _marks.Add(m);
        AppLog.Info("perf", $"{m.Name}: {m.SinceStartMs} ms, working set {m.WorkingSetMb} MB, private {m.PrivateMb} MB, managed {m.ManagedMb} MB");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_marks, new JsonSerializerOptions { WriteIndented = true }));
    }
}
