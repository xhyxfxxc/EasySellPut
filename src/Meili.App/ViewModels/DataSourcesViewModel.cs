using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meili.App.Services;
using Meili.Core.Models;
using Meili.Data;
using Meili.Data.Routing;

namespace Meili.App.ViewModels;

/// <summary>Data-source status page: live health, latency, counters and last error of each source, plus the failover order.</summary>
public partial class DataSourcesViewModel : ObservableObject
{
    private static readonly HashSet<string> Domestic = ["sina", "tencent", "eastmoney"];
    private readonly DataServices _data;
    private readonly LocalizationService _loc;
    private readonly INetworkController _network;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private bool _autoTested;

    public ObservableCollection<SourceRowViewModel> Sources { get; } = [];
    public ObservableCollection<FailoverRowViewModel> Failover { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TestButtonText))] public partial bool IsTesting { get; set; }
    [ObservableProperty] public partial string NetworkText { get; set; } = "";
    [ObservableProperty] public partial string SummaryText { get; set; } = "";
    public string TestButtonText => _loc[IsTesting ? "Ds_Testing" : "Ds_TestAll"];

    public DataSourcesViewModel(DataServices data, LocalizationService loc, INetworkController network)
    {
        _data = data; _loc = loc; _network = network;
        foreach (var h in data.Health.All) Sources.Add(new SourceRowViewModel(h, Domestic.Contains(h.Id)));
        _timer.Tick += (_, _) => Refresh();
        _loc.LanguageChanged += (_, _) => { BuildFailover(); OnPropertyChanged(nameof(TestButtonText)); Refresh(); };
        BuildFailover();
        Refresh();
    }

    public async Task ActivateAsync()
    {
        _timer.Start();
        Refresh();
        if (!_autoTested && _data.Health.All.All(h => h.State(_data.Health.Now) == HealthState.Unknown))
        {
            _autoTested = true;
            await TestAllAsync();
        }
    }

    public void Deactivate() => _timer.Stop();

    [RelayCommand]
    private async Task TestAllAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Task.WhenAll(_data.Providers.Select(p => Task.Run(() => _data.Router.ProbeAsync(p, cts.Token))));
        }
        catch (OperationCanceledException) { }
        finally { IsTesting = false; Refresh(); }
    }

    private void BuildFailover()
    {
        Failover.Clear();
        foreach (var cap in DataServices.DefaultOrder.Keys)
        {
            var chain = _data.Router.Chain(cap);
            if (chain.Count == 0) continue;
            Failover.Add(new FailoverRowViewModel(_loc[$"Cap_{cap}"], string.Join("  →  ", chain.Select(p => SourceNames.Display(p.Id, _loc)))));
        }
    }

    private void Refresh()
    {
        var now = _data.Health.Now;
        foreach (var s in Sources) s.Refresh(now, _loc);
        NetworkText = $"{_loc["Status_Proxy"]}: {_network.EffectiveDescription}";
        var healthy = Sources.Count(s => s.State == HealthState.Healthy);
        var down = Sources.Count(s => s.State == HealthState.Down);
        SummaryText = string.Format(_loc["Ds_Summary"], Sources.Count, healthy, down);
    }
}

public sealed record FailoverRowViewModel(string Capability, string Chain);

public partial class SourceRowViewModel(SourceHealth health, bool domestic) : ObservableObject
{
    public string Id => health.Id;
    [ObservableProperty] public partial string DisplayName { get; set; } = health.DisplayName;
    public bool IsDomestic { get; } = domestic;
    public HealthState State { get; private set; }
    [ObservableProperty] public partial string StateText { get; set; } = "";
    [ObservableProperty] public partial Brush? StateBrush { get; set; }
    [ObservableProperty] public partial string CapabilitiesText { get; set; } = "";
    [ObservableProperty] public partial string LatencyText { get; set; } = "—";
    [ObservableProperty] public partial string SuccessText { get; set; } = "0";
    [ObservableProperty] public partial string FailureText { get; set; } = "0";
    [ObservableProperty] public partial string LastUsedText { get; set; } = "—";
    [ObservableProperty] public partial string LastErrorText { get; set; } = "";
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string RegionText { get; set; } = "";
    [ObservableProperty] public partial string CircuitText { get; set; } = "";
    [ObservableProperty] public partial bool IsCircuitOpen { get; set; }
    /// <summary>E1: the state is told by icon shape too, not by colour alone.</summary>
    [ObservableProperty] public partial Wpf.Ui.Controls.SymbolRegular StateIcon { get; set; } = Wpf.Ui.Controls.SymbolRegular.QuestionCircle24;

    public void Refresh(DateTimeOffset now, LocalizationService loc)
    {
        State = health.State(now);
        DisplayName = loc.Get($"SrcFull_{health.Id}") is var dn && dn != $"SrcFull_{health.Id}" ? dn : health.DisplayName;
        StateText = loc[$"Ds_{State switch { HealthState.Healthy => "Healthy", HealthState.Degraded => "Degraded", HealthState.Down => "Down", _ => "Unknown" }}"];
        StateBrush = Application.Current.TryFindResource(State switch
        {
            HealthState.Healthy => "HealthyBrush", HealthState.Degraded => "DegradedBrush", HealthState.Down => "DownStateBrush", _ => "UnknownBrush",
        }) as Brush;
        StateIcon = HealthVisuals.Icon(State);
        CapabilitiesText = string.Join("  ·  ", Enum.GetValues<DataCapability>()
            .Where(c => c != DataCapability.None && health.Capabilities.HasFlag(c)).Select(c => loc[$"Cap_{c}"]));
        var lat = health.AverageLatency ?? health.LastLatency;
        LatencyText = lat is { } l ? $"{l.TotalMilliseconds:0} ms" : loc["Ds_Never"];
        SuccessText = health.Successes.ToString();
        FailureText = health.Failures.ToString();
        LastUsedText = health.LastUsedAt is { } u ? u.ToLocalTime().ToString("HH:mm:ss") : loc["Ds_Never"];
        var recentError = health.LastError is not null && (health.LastSuccessAt is null || health.LastErrorAt > health.LastSuccessAt);
        HasError = recentError;
        LastErrorText = recentError ? $"{health.LastErrorAt?.ToLocalTime():HH:mm:ss}  {health.LastError}" : "";
        RegionText = loc[IsDomestic ? "Ds_Domestic" : "Ds_Overseas"];
        IsCircuitOpen = health.CircuitOpenUntil is { } c && c > now;
        CircuitText = IsCircuitOpen ? string.Format(loc["Ds_Circuit"], (int)Math.Ceiling((health.CircuitOpenUntil!.Value - now).TotalSeconds)) : "";
    }
}
/// <summary>E1: icon per health state.</summary>
public static class HealthVisuals
{
    public static Wpf.Ui.Controls.SymbolRegular Icon(HealthState state) => state switch
    {
        HealthState.Healthy => Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24,
        HealthState.Degraded => Wpf.Ui.Controls.SymbolRegular.Warning24,
        HealthState.Down => Wpf.Ui.Controls.SymbolRegular.ErrorCircle24,
        _ => Wpf.Ui.Controls.SymbolRegular.QuestionCircle24,
    };
}
