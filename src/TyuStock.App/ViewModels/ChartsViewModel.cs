using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TyuStock.Analytics;
using TyuStock.App.Services;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Export;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;
using TyuStock.Data.Routing;

namespace TyuStock.App.ViewModels;

public enum ChartRangeKind { Session, Minute1, Minute5, Minute15, Hour1, Day1, Week1, Month1 }

public partial class ChartRangeTab : ObservableObject
{
    public ChartRangeKind Kind { get; }
    public string Key { get; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }

    public ChartRangeTab(ChartRangeKind kind, string key, string title, bool selected)
    {
        Kind = kind; Key = key; Title = title; IsSelected = selected;
    }
}

/// <summary>K-line page: symbol + interval, live-enough bar refresh, indicator toggles, snapshot for the plotter.</summary>
public partial class ChartsViewModel : ObservableObject
{
    private readonly WatchlistStore _watchlist;
    private readonly IMarketDataService _data;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private CancellationTokenSource? _cts;
    private bool _active;

    public ObservableCollection<string> WatchSymbols { get; } = [];
    public ObservableCollection<ChartRangeTab> Ranges { get; } = [];

    [ObservableProperty] public partial string SymbolText { get; set; } = "";
    [ObservableProperty] public partial ChartRangeKind Range { get; set; } = ChartRangeKind.Day1;
    [ObservableProperty] public partial bool ShowMa { get; set; } = true;
    [ObservableProperty] public partial bool ShowEma { get; set; } = true;
    [ObservableProperty] public partial bool ShowBoll { get; set; }
    [ObservableProperty] public partial bool ShowVolume { get; set; } = true;
    [ObservableProperty] public partial bool ShowMacd { get; set; }
    [ObservableProperty] public partial bool ShowRsi { get; set; }
    [ObservableProperty] public partial bool ShowKdj { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string CrosshairText { get; set; } = "";
    [ObservableProperty] public partial string Headline { get; set; } = "";
    [ObservableProperty] public partial BarSeries? Series { get; set; }
    [ObservableProperty] public partial KlineSnapshot? Snapshot { get; set; }

    public event EventHandler? PlotChanged;

    public ChartsViewModel(WatchlistStore watchlist, IMarketDataService data, LocalizationService loc)
    {
        _watchlist = watchlist; _data = data; _loc = loc;
        SymbolText = watchlist.AllSymbols.FirstOrDefault() ?? "AAPL";
        RebuildWatchSymbols();
        RebuildRanges();
        _timer.Tick += async (_, _) => await LoadAsync(refreshOnly: true);
        _watchlist.Changed += (_, _) => RebuildWatchSymbols();
        _loc.LanguageChanged += (_, _) =>
        {
            RebuildRanges();
            UpdateStatus();
            PlotChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public async Task ActivateAsync()
    {
        _active = true;
        if (Series is null) await LoadAsync();
        else
        {
            ArmTimer();
            _timer.Start();
        }
    }

    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
        _cts?.Cancel();
    }

    public Task OpenSymbolAsync(string symbol)
    {
        if (WatchlistStore.TryNormalizeSymbol(symbol, out var s, out _))
            SymbolText = s;
        else if (!string.IsNullOrWhiteSpace(symbol))
            SymbolText = symbol.Trim().ToUpperInvariant();
        return LoadAsync();
    }

    private void RebuildWatchSymbols()
    {
        WatchSymbols.Clear();
        foreach (var s in _watchlist.AllSymbols) WatchSymbols.Add(s);
    }

    private void RebuildRanges()
    {
        var keep = Range;
        Ranges.Clear();
        foreach (var (kind, key) in AllRanges())
            Ranges.Add(new ChartRangeTab(kind, key, _loc[key], kind == keep));
    }

    private static (ChartRangeKind kind, string key)[] AllRanges() =>
    [
        (ChartRangeKind.Session, "Ch_Session"),
        (ChartRangeKind.Minute1, "Ch_1m"),
        (ChartRangeKind.Minute5, "Ch_5m"),
        (ChartRangeKind.Minute15, "Ch_15m"),
        (ChartRangeKind.Hour1, "Ch_1h"),
        (ChartRangeKind.Day1, "Ch_1d"),
        (ChartRangeKind.Week1, "Ch_1w"),
        (ChartRangeKind.Month1, "Ch_1mo"),
    ];

    [RelayCommand]
    private void SelectRange(ChartRangeKind kind)
    {
        if (Range == kind) return;
        Range = kind;
        foreach (var r in Ranges) r.IsSelected = r.Kind == kind;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task LoadFromBoxAsync() => LoadAsync();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(refreshOnly: true);

    [RelayCommand]
    private void Export()
    {
        if (Series is null || Series.Bars.Count == 0)
        {
            StatusText = _loc["Ex_NoData"];
            return;
        }
        var headers = new[] { _loc["Ex_Time"], "Open", "High", "Low", "Close", _loc["Ch_Volume"] };
        var table = MarketExport.Bars($"{SymbolText}-{Range}", headers, Series);
        var msg = ExportUi.Save($"{SymbolText}-{Range}", [table], _loc);
        if (msg is not null) StatusText = msg;
    }

    partial void OnShowMaChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowEmaChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowBollChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowVolumeChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMacdChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowRsiChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowKdjChanged(bool value) => PlotChanged?.Invoke(this, EventArgs.Empty);

    public async Task LoadAsync(bool refreshOnly = false)
    {
        if (!WatchlistStore.TryNormalizeSymbol(SymbolText, out var symbol, out _))
        {
            if (!refreshOnly)
            {
                StatusText = _loc["Ch_InvalidSymbol"];
                Series = null;
                Snapshot = null;
                PlotChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        SymbolText = symbol;
        if (IsBusy) { _cts?.Cancel(); }
        IsBusy = true;
        if (!refreshOnly || Series is null) StatusText = _loc["Ch_Loading"];
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var request = BuildRequest(symbol, Range);
            var series = await _data.GetBarsAsync(request, _cts.Token);
            var bars = series.Bars.Select(b => new OhlcvBar(b.Time, b.Open, b.High, b.Low, b.Close, b.Volume)).ToArray();
            Series = series;
            Snapshot = bars.Length == 0 ? null : KlineSnapshot.Compute(bars);
            CrosshairText = "";
            Headline = string.IsNullOrWhiteSpace(symbol) ? "" : symbol;
            _lastError = series.Bars.Count == 0 ? _loc["Ch_Empty"] : null;
        }
        catch (AllSourcesFailedException) { if (!refreshOnly || Series is null) Fail(_loc["Ch_AllFailed"]); }
        catch (OperationCanceledException) when (_active && !refreshOnly) { Fail(_loc["Ch_Timeout"]); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail(ex.Message); }
        finally
        {
            IsBusy = false;
            ArmTimer();
            if (_active) _timer.Start();
            UpdateStatus();
            PlotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? _lastError;

    private void Fail(string message)
    {
        _lastError = message;
        if (Series is null)
        {
            Snapshot = null;
            PlotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static BarRequest BuildRequest(string symbol, ChartRangeKind range)
    {
        return range switch
        {
            ChartRangeKind.Session => new BarRequest(symbol, BarInterval.Minute1, SessionStart(), null),
            ChartRangeKind.Minute1 => new BarRequest(symbol, BarInterval.Minute1),
            ChartRangeKind.Minute5 => new BarRequest(symbol, BarInterval.Minute5),
            ChartRangeKind.Minute15 => new BarRequest(symbol, BarInterval.Minute15),
            ChartRangeKind.Hour1 => new BarRequest(symbol, BarInterval.Hour1),
            ChartRangeKind.Day1 => new BarRequest(symbol, BarInterval.Day1),
            ChartRangeKind.Week1 => new BarRequest(symbol, BarInterval.Week1),
            _ => new BarRequest(symbol, BarInterval.Month1),
        };
    }

    private static DateTimeOffset SessionStart()
    {
        var now = DateTimeOffset.Now;
        var et = MarketClock.ToEastern(now);
        var d = DateOnly.FromDateTime(et);
        if (et.TimeOfDay < new TimeSpan(4, 0, 0) || !MarketClock.IsTradingDay(d))
        {
            d = d.AddDays(-1);
            while (!MarketClock.IsTradingDay(d)) d = d.AddDays(-1);
        }
        return MarketClock.FromEastern(d.ToDateTime(new TimeOnly(4, 0)));
    }

    private void ArmTimer()
    {
        _timer.Interval = Range switch
        {
            ChartRangeKind.Session or ChartRangeKind.Minute1 or ChartRangeKind.Minute5 => TimeSpan.FromSeconds(15),
            ChartRangeKind.Minute15 or ChartRangeKind.Hour1 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(120),
        };
    }

    private void UpdateStatus()
    {
        if (_lastError is not null) { StatusText = _lastError; return; }
        if (Series is { } s)
        {
            var src = SourceNames.Display(s.Source, _loc);
            StatusText = string.Format(_loc["Ch_Source"], src, s.Bars.Count);
        }
        else StatusText = "";
    }

    public void UpdateCrosshair(int index)
    {
        if (Series is null || Snapshot is null || index < 0 || index >= Series.Bars.Count)
        {
            CrosshairText = "";
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var b = Series.Bars[index];
        var et = MarketClock.ToEastern(b.Time);
        var head = string.Format(_loc["Ch_Crosshair"],
            F(b.Open), F(b.High), F(b.Low), F(b.Close), Vol(b.Volume));
        var ma = Def(Snapshot.Sma[index]);
        var ema = Def(Snapshot.Ema[index]);
        CrosshairText = $"{et:yyyy-MM-dd HH:mm}   " + string.Format(_loc["Ch_CrosshairMore"], head, ma, ema);

        string F(double x) => x.ToString(x >= 1000 ? "N2" : "0.00", inv);
        string Def(double x) => double.IsFinite(x) ? F(x) : "—";
        static string Vol(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M" : v.ToString("N0", CultureInfo.InvariantCulture);
    }
}