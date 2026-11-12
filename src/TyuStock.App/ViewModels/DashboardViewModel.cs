using TyuStock.Core.Diagnostics;
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
using TyuStock.Core.Quotes;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;
using TyuStock.Data.Routing;

namespace TyuStock.App.ViewModels;

/// <summary>Overview page: live quote cards for the watchlist, refreshed adaptively by market session.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly WatchlistStore _watchlist;
    private const string Owner = "dashboard";
    private readonly QuoteHub _hub;
    private readonly LocalizationService _loc;
    private bool _active;
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _lastUpdate;
    private string? _lastError;

    public ObservableCollection<QuoteCardViewModel> Quotes { get; } = [];
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public DashboardViewModel(WatchlistStore watchlist, QuoteHub hub, LocalizationService loc)
    {
        _watchlist = watchlist; _hub = hub; _loc = loc;
        RebuildCards();
        _loc.LanguageChanged += (_, _) => { foreach (var q in Quotes) q.Relabel(_loc); UpdateStatus(); };
        _watchlist.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(() => { RebuildCards(); if (_active) { Resubscribe(); _ = RefreshAsync(); } });
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

    /// <summary>C1: listen to the shared quote hub while visible; the app-level pump does the periodic fetch.</summary>
    public async Task ActivateAsync()
    {
        if (!_active)
        {
            _active = true;
            _hub.Updated += OnBatch;
        }
        Resubscribe();
        ShowCached();
        await RefreshAsync();
    }

    public void Deactivate()
    {
        _active = false;
        _hub.Updated -= OnBatch;
        _hub.Unsubscribe(Owner);
        _cts?.Cancel();
    }

    private void Resubscribe() => _hub.Subscribe(Owner, Quotes.Select(q => q.Symbol));

    private void ShowCached()
    {
        foreach (var card in Quotes)
            if (_hub.TryGetCached(card.Symbol, out var q)) card.Update(q, _loc);
    }

    private void OnBatch(object? sender, QuoteBatch batch)
    {
        if (!Quotes.Any(c => batch.Covers(c.Symbol))) return;
        if (batch.Error is not null) { _lastError = ErrorText(batch.Error); UpdateStatus(); return; }
        Apply(batch.Quotes);
    }

    private void Apply(IReadOnlyList<Quote> quotes)
    {
        var map = quotes.GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var card in Quotes)
            if (map.TryGetValue(card.Symbol, out var q)) card.Update(q, _loc);
        if (map.Count > 0) { _lastUpdate = DateTimeOffset.Now; _lastError = null; }
        UpdateStatus();
    }

    private string ErrorText(Exception ex) => ex switch
    {
        AllSourcesFailedException => _loc["Dash_AllFailed"],
        OperationCanceledException => _loc["Dash_Timeout"],
        _ => ex.Message,
    };
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy || Quotes.Count == 0) return;
        IsBusy = true;
        if (_lastUpdate is null) StatusText = _loc["Dash_Loading"];
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            // Served from the hub's short-TTL cache when another page just fetched; otherwise one combined request.
            var quotes = await _hub.GetQuotesAsync(Quotes.Select(q => q.Symbol).ToList(), _cts.Token);
            Apply(quotes);
        }
        catch (OperationCanceledException) when (!_active) { }
        catch (Exception ex) { AppLog.Warn("dashboard", "refresh failed", ex); _lastError = ErrorText(ex); }
        finally
        {
            IsBusy = false;
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
    [ObservableProperty] public partial string AsOfText { get; set; } = "";
    [ObservableProperty] public partial string TimingTip { get; set; } = "";
    [ObservableProperty] public partial Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial Brush? ChangeBackground { get; set; }

    public void Update(Quote q, LocalizationService loc) { _quote = q; Relabel(loc); }

    public void Relabel(LocalizationService loc)
    {
        if (_quote is not { } q) return;
        var inv = CultureInfo.InvariantCulture;
        Name = q.Name ?? "";
        PriceText = NumberDisplay.Price(q.Price);
        var chg = q.Change ?? (q.PreviousClose is { } pc ? q.Price - pc : 0);
        var pct = q.ChangePercent ?? (q.PreviousClose is > 0 ? chg / q.PreviousClose.Value * 100 : 0);
        ChangeText = $"{NumberDisplay.SignedChange(chg, q.Price)}  {pct.ToString("+0.00;-0.00;0.00", inv)}%";
        var up = chg >= 0;
        ChangeBrush = Application.Current.TryFindResource(up ? "UpBrush" : "DownBrush") as Brush;
        ChangeBackground = Application.Current.TryFindResource(up ? "UpBackgroundBrush" : "DownBackgroundBrush") as Brush;
        ExtText = q.ExtendedPrice is { } ep
            ? $"{loc[q.MarketState == MarketState.PreMarket ? "Quote_Pre" : "Quote_Post"]}  {NumberDisplay.Price(ep)}  {(q.ExtendedChangePercent ?? 0).ToString("+0.00;-0.00;0.00", inv)}%"
            : "";
        var now = DateTimeOffset.Now;
        SourceText = FreshnessText.SourceAndTiming(q, loc, now);
        AsOfText = FreshnessText.AsOf(q.AsOf, loc);
        TimingTip = FreshnessText.Tooltip(q, loc, now);
    }
}

public static class SourceNames
{
    /// <summary>Short localized source name (resource key <c>Src_{id}</c>).</summary>
    public static string Display(string id, LocalizationService loc) => loc.Get($"Src_{id}") is var s && s != $"Src_{id}" ? s : id;
}