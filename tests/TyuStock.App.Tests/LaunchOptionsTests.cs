using System.IO;
using TyuStock.App.Services;

namespace TyuStock.App.Tests;

/// <summary>B3: self-check flags.</summary>
public class LaunchOptionsTests
{
    [Fact]
    public void Smoke_implies_no_alerts_and_exit_on_close()
    {
        var o = AppLaunchOptions.Parse(["--smoke", "--smoke-report", @"C:\x\r.json"]);
        Assert.True(o.Smoke);
        Assert.True(o.NoAlerts);
        Assert.True(o.ExitOnClose);
        Assert.False(o.SmokeOffline);
        Assert.Equal(@"C:\x\r.json", o.SmokeReport);
    }

    [Fact]
    public void Offline_smoke_and_inline_report_path()
    {
        var o = AppLaunchOptions.Parse(["--smoke-offline", "--smoke-report=\"D:\\r.json\""]);
        Assert.True(o.Smoke);
        Assert.True(o.SmokeOffline);
        Assert.Equal(@"D:\r.json", o.SmokeReport);
    }

    [Fact]
    public void Normal_start_has_no_smoke()
    {
        var o = AppLaunchOptions.Parse(["--data-dir", "x"]);
        Assert.False(o.Smoke);
        Assert.False(o.NoAlerts);
        Assert.Null(o.SmokeReport);
    }

    /// <summary>C6: --perf records a timeline with monotonic times and writes logs\perf.json.</summary>
    [Fact]
    public void Perf_flag_and_timeline()
    {
        Assert.True(AppLaunchOptions.Parse(["--smoke-offline", "--perf"]).Perf);
        Assert.False(AppLaunchOptions.Parse(["--smoke-offline"]).Perf);

        var dir = Path.Combine(Path.GetTempPath(), "meili-perf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var perf = new StartupPerf(dir);
            perf.Mark("a");
            perf.Mark("b");
            Assert.Equal(["a", "b"], perf.Marks.Select(m => m.Name));
            Assert.True(perf.Marks[1].SinceStartMs >= perf.Marks[0].SinceStartMs);
            Assert.All(perf.Marks, m => Assert.True(m.WorkingSetMb > 0 && m.ManagedMb > 0));
            var json = File.ReadAllText(Path.Combine(dir, "logs", "perf.json"));
            Assert.Contains("\"SinceStartMs\"", json);
        }
        finally { Directory.Delete(dir, true); }
    }
}
