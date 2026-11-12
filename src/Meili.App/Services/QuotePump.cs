using System.Windows.Threading;
using Meili.Core.Diagnostics;
using Meili.Core.Quotes;
using Meili.Core.Time;

namespace Meili.App.Services;

/// <summary>
/// C1: the only quote timer in the app. Ticks at the market-session cadence (fast in regular hours, slow when closed)
/// and asks <see cref="QuoteHub"/> for one combined request; pages and the alert monitor just listen. Keeps running
/// while the window is hidden in the tray, because alerts depend on it.
/// </summary>
public sealed class QuotePump : IDisposable
{
    private readonly QuoteHub _hub;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private int _busy;

    public QuotePump(QuoteHub hub)
    {
        _hub = hub;
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public void Start()
    {
        _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose() => Stop();

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { await _hub.PollAsync(); }
        catch (Exception ex) { AppLog.Warn("quotes", "poll failed: " + ex.Message); }
        finally
        {
            _timer.Interval = MarketClock.QuoteRefreshInterval(DateTimeOffset.Now);
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
