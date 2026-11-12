using TyuStock.Core.Alerts;
using TyuStock.Core.Diagnostics;
using TyuStock.Core.Models;
using TyuStock.Core.Quotes;
using TyuStock.Core.Settings;

namespace TyuStock.App.Services;

/// <summary>
/// Evaluates enabled alerts against quotes from the shared <see cref="QuoteHub"/> (C1): it subscribes the alert symbols
/// and reacts to every batch the app-level <see cref="QuotePump"/> fetches, so it keeps working while the window is
/// hidden in the tray without a timer or request of its own.
/// </summary>
public sealed class AlertMonitor : IDisposable
{
    private const string Owner = "alerts";
    private readonly AlertStore _store;
    private readonly QuoteHub _hub;
    private readonly NotificationService _notify;
    private readonly AppLaunchOptions _options;
    private bool _started;
    private int _batches;

    public event EventHandler? Updated;

    public AlertMonitor(AlertStore store, QuoteHub hub, NotificationService notify, AppLaunchOptions options)
    {
        _store = store;
        _hub = hub;
        _notify = notify;
        _options = options;
    }

    public void Start()
    {
        if (_options.NoAlerts || _started) return;
        _started = true;
        _store.Changed += OnStoreChanged;
        _hub.Updated += OnBatch;
        Resubscribe();
        _ = PollAsync();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _store.Changed -= OnStoreChanged;
        _hub.Updated -= OnBatch;
        _hub.Unsubscribe(Owner);
    }

    public void Dispose() => Stop();

    private void OnStoreChanged(object? sender, EventArgs e) => Resubscribe();

    private void Resubscribe() => _hub.Subscribe(Owner, _store.EnabledSymbols);

    /// <summary>Immediate check (start-up, or after alerts change). Errors are reported through the hub's batch event.</summary>
    public async Task PollAsync()
    {
        if (_options.NoAlerts) return;
        var symbols = _store.EnabledSymbols;
        if (symbols.Count == 0) return;
        var before = _batches;
        try
        {
            var quotes = await _hub.GetQuotesAsync(symbols);
            // Served from the hub's cache (no batch event fired): evaluate here so a manual re-check always runs.
            if (_batches == before) Evaluate(quotes, DateTimeOffset.Now);
        }
        catch (Exception ex) { AppLog.Warn("alerts", "quote fetch failed: " + ex.Message); }
    }

    private void OnBatch(object? sender, QuoteBatch batch)
    {
        _batches++;
        if (!_started || batch.Error is not null || batch.Quotes.Count == 0) return;
        Evaluate(batch.Quotes, DateTimeOffset.Now);
    }

    internal void Evaluate(IReadOnlyList<Quote> quotes, DateTimeOffset now)
    {
        var bySymbol = quotes
            .Where(q => !string.IsNullOrWhiteSpace(q.Symbol))
            .GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (bySymbol.Count == 0) return;

        var applied = new List<(PriceAlert Next, AlertHistoryEntry? History)>();
        foreach (var alert in _store.Alerts)
        {
            if (!alert.Enabled) continue;
            if (!bySymbol.TryGetValue(alert.Symbol, out var quote)) continue;
            var result = AlertEvaluator.Evaluate(alert, quote, now);
            AlertHistoryEntry? history = null;
            if (result.Fired)
            {
                history = AlertEvaluator.HistoryFrom(alert, result, quote, now);
                try { _notify.ShowAlertToast(alert, result); }
                catch (Exception ex) { AppLog.Warn("alerts", "toast failed: " + ex.Message); }
            }
            if (result.Fired || result.Next.HasSeenQuote != alert.HasSeenQuote
                || result.Next.LastSeenPrice != alert.LastSeenPrice
                || result.Next.LastSeenChangePercent != alert.LastSeenChangePercent
                || result.Next.LastSeenVolume != alert.LastSeenVolume
                || result.Next.Enabled != alert.Enabled
                || result.Next.LastFiredAt != alert.LastFiredAt)
                applied.Add((result.Next, history));
        }

        if (applied.Count > 0)
            _store.ApplyResults(applied);
        Updated?.Invoke(this, EventArgs.Empty);
    }
}
