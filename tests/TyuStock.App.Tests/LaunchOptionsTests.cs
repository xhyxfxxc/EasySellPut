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
}
