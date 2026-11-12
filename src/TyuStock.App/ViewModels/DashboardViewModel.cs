using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TyuStock.App.Services;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;
using TyuStock.Data.Routing;

namespace TyuStock.App.ViewModels;

/// <summary>Overview page: live quote cards for the watchlist, refreshed adaptively by market session.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly WatchlistStore _watchlist;
    private readonly IMarketDataService _data;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _lastUpdate;
    private string? _lastError;

    public ObservableCollection<QuoteCardViewModel> Quotes { get; } = [];
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public DashboardViewModel(WatchlistStore watchlist, IMarketDataService data, LocalizationService loc)
    {
        _watchlist = watchlist; _data = data; _loc = loc;
        RebuildCards();
        _timer.Tick += async (_, _) => await RefreshAsync();
        _loc.LanguageChanged += (_, _) => { foreach (var q in Quotes) q.Relabel(_loc); UpdateStatus(); };
        _watchlist.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(() => { RebuildCards(); _ = RefreshAsync(); });
    }

    private void RebuildCards()
    {
        var wanted = _watchlist.AllSymbols.ToList();
        var map = Quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        Quotes.Clear();
        foreach (var s in wanted)
        {
            if (!map.TryGetValue(s, out var card)) card = new QuoteCardViewModel(s);
            Quotes.Add(card);
        }
    }

    public async Task ActivateAsync()
    {
        _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
        _timer.Start();
        await RefreshAsync();
    }

    public void Deactivate() { _timer.Stop(); _cts?.Cancel(); }
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy || Quotes.Count == 0) return;
        IsBusy = true;
        if (_lastUpdate is null) StatusText = _loc["Dash_Loading"];
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var quotes = await _data.GetQuotesAsync(Quotes.Select(q => q.Symbol).ToList(), _cts.Token);
            var map = quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
            foreach (var card in Quotes)
                if (map.TryGetValue(card.Symbol, out var q)) card.Update(q, _loc);
            _lastUpdate = DateTimeOffset.Now; _lastError = null;
        }
        catch (AllSourcesFailedException) { _lastError = _loc["Dash_AllFailed"]; }
        catch (OperationCanceledException) { _lastError = _loc["Dash_Timeout"]; }
        catch (Exception ex) { _lastError = ex.Message; }
        finally
        {
            IsBusy = false;
            _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
            UpdateStatus();
        }
    }

    private void UpdateStatus() => StatusText = _lastError is not null
        ? _lastError
        : _lastUpdate is { } t ? $"{_loc["Dash_LastUpdated"]} {t:HH:mm:ss}" : "";
}

public partial class QuoteCardViewModel(string symbol) : ObservableObject
{
    private Quote? _quote;
    public string Symbol { get; } = symbol;
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string PriceText { get; set; } = "—";
    [ObservableProperty] public partial string ChangeText { get; set; } = "";
    [ObservableProperty] public partial string ExtText { get; set; } = "";
    [ObservableProperty] public partial string SourceText { get; set; } = "";
    [ObservableProperty] public partial Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial Brush? ChangeBackground { get; set; }

    public void Update(Quote q, LocalizationService loc) { _quote = q; Relabel(loc); }

    public void Relabel(LocalizationService loc)
    {
        if (_quote is not { } q) return;
        var inv = CultureInfo.InvariantCulture;
        Name = q.Name ?? "";
        PriceText = q.Price.ToString(q.Price >= 1000 ? "N2" : "0.00", inv);
        var chg = q.Change ?? (q.PreviousClose is { } pc ? q.Price - pc : 0);
        var pct = q.ChangePercent ?? (q.PreviousClose is > 0 ? chg / q.PreviousClose.Value * 100 : 0);
        ChangeText = $"{chg.ToString("+0.00;-0.00;0.00", inv)}  {pct.ToString("+0.00;-0.00;0.00", inv)}%";
        var up = chg >= 0;
        ChangeBrush = Application.Current.TryFindResource(up ? "UpBrush" : "DownBrush") as Brush;
        ChangeBackground = Application.Current.TryFindResource(up ? "UpBackgroundBrush" : "DownBackgroundBrush") as Brush;
        ExtText = q.ExtendedPrice is { } ep
            ? $"{loc[q.MarketState == MarketState.PreMarket ? "Quote_Pre" : "Quote_Post"]}  {ep.ToString("0.00", inv)}  {(q.ExtendedChangePercent ?? 0).ToString("+0.00;-0.00;0.00", inv)}%"
            : "";
        var timing = q.IsRealtime ? loc["Quote_Realtime"] : q.DelayMinutes > 0 ? string.Format(loc["Quote_DelayedN"], q.DelayMinutes) : loc["Quote_NearRealtime"];
        SourceText = $"{SourceNames.Display(q.Source, loc)} · {timing}";
    }
}

public static class SourceNames
{
    /// <summary>Short localized source name (resource key <c>Src_{id}</c>).</summary>
    public static string Display(string id, LocalizationService loc) => loc.Get($"Src_{id}") is var s && s != $"Src_{id}" ? s : id;
}