using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Meili.App.Services;
using Meili.Core.Settings;
using Meili.Core.Time;

namespace Meili.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private static readonly TimeZoneInfo Beijing = FindBeijing();
    private readonly LocalizationService _loc;
    private readonly DataDirectoryInfo _dataDir;

    [ObservableProperty] public partial string ClockText { get; set; } = "";
    [ObservableProperty] public partial string SessionText { get; set; } = "";
    [ObservableProperty] public partial Brush? SessionBrush { get; set; }
    [ObservableProperty] public partial string NetworkText { get; set; } = "";
    [ObservableProperty] public partial string DataDirText { get; set; } = "";
    [ObservableProperty] public partial string SourcesText { get; set; } = "";
    [ObservableProperty] public partial Brush? SourcesBrush { get; set; }
    private readonly Meili.Data.Routing.HealthTracker _health;

    public ShellViewModel(LocalizationService loc, DataDirectoryInfo dataDir, Meili.Data.DataServices data)
    {
        _loc = loc;
        _health = data.Health;
        _dataDir = dataDir;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        _loc.LanguageChanged += (_, _) => Tick();
        Tick();
    }

    /// <summary>Set by <see cref="NetworkController"/> to describe the effective proxy.</summary>
    public string NetworkDescription { get => _networkDescription; set { _networkDescription = value; Tick(); } }
    private string _networkDescription = "";

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        var et = MarketClock.ToEastern(now);
        var bj = TimeZoneInfo.ConvertTime(now, Beijing);
        ClockText = $"{_loc["Status_Eastern"]} {et:MM-dd HH:mm:ss}   ·   {_loc["Status_Beijing"]} {bj:HH:mm:ss}";
        var session = MarketClock.GetSession(now);
        SessionText = _loc[$"Session_{session}"];
        SessionBrush = Application.Current.TryFindResource(session switch
        {
            MarketSession.Regular => "HealthyBrush",
            MarketSession.PreMarket or MarketSession.AfterHours => "DegradedBrush",
            _ => "UnknownBrush",
        }) as Brush;
        NetworkText = string.IsNullOrEmpty(NetworkDescription) ? "" : $"{_loc["Status_Proxy"]}: {NetworkDescription}";
        DataDirText = $"{_loc["Status_DataDir"]}: {_dataDir.Path}";
        var hnow = _health.Now;
        var states = _health.All.Select(h => h.State(hnow)).ToList();
        var ok = states.Count(s => s == Meili.Data.Routing.HealthState.Healthy);
        var bad = states.Count(s => s == Meili.Data.Routing.HealthState.Down);
        SourcesText = string.Format(_loc["Status_Sources"], ok, states.Count);
        SourcesBrush = Application.Current.TryFindResource(bad > 0 ? "DegradedBrush" : ok > 0 ? "HealthyBrush" : "UnknownBrush") as Brush;
    }

    private static TimeZoneInfo FindBeijing()
    {
        foreach (var id in new[] { "Asia/Shanghai", "China Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Local;
    }
}
