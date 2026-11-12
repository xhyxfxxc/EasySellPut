using System.Windows.Threading;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Alerts;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;

namespace TyuStock.App.Services;

/// <summary>
/// Background quote polling for enabled alerts. Uses the UI dispatcher timer so it keeps running
/// while the main window is hidden in the tray, and never blocks the UI on network I/O.
/// </summary>
public sealed class AlertMonitor : IDisposable
{
    private readonly AlertStore _store;
    private readonly IMarketDataService _data;
    private readonly NotificationService _notify;
    private readonly AppLaunchOptions _options;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;
    private bool _started;
    private int _polling;

    public event EventHandler? Updated;

    public AlertMonitor(AlertStore store, IMarketDataService data, NotificationService notify, AppLaunchOptions options)
    {
        _store = store;
        _data = data;
        _notify = notify;
        _options = options;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now),
        };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public void Start()
    {
        if (_options.NoAlerts || _started) return;
        _started = true;
        _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
        _timer.Start();
        _ = PollAsync();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
        _cts?.Cancel();
    }

    public void Dispose() => Stop();

    public async Task PollAsync()
    {
        if (_options.NoAlerts) return;
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
            var symbols = _store.EnabledSymbols;
            if (symbols.Count == 0) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            IReadOnlyList<Quote> quotes;
            try
            {
                quotes = await _data.GetQuotesAsync(symbols, _cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                return;
            }

            var bySymbol = quotes
                .Where(q => !string.IsNullOrWhiteSpace(q.Symbol))
                .GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.Now;
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
                    try { _notify.ShowAlertToast(alert, result); } catch { }
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
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }
}