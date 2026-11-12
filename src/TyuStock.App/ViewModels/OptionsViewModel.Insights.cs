using TyuStock.Core.Diagnostics;
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
using ARight = TyuStock.Analytics.OptionRight;
using CRight = TyuStock.Core.Models.OptionRight;

namespace TyuStock.App.ViewModels;

/// <summary>B6 (split out in H1): IV Rank, ATM term structure and OI change for the options page.</summary>
public partial class OptionsViewModel
{
    // B6: IV Rank (from the locally recorded IV history), ATM term structure, OI change vs. the previous stored day.
    [ObservableProperty] public partial string IvRankText { get; set; } = "";
    [ObservableProperty] public partial string IvRankTip { get; set; } = "";
    [ObservableProperty] public partial bool ShowTerm { get; set; }
    [ObservableProperty] public partial bool ShowOiChange { get; set; } = true;
    public IReadOnlyList<TermPoint> TermPoints { get; private set; } = [];

    /// <summary>B6: stores today's open interest and returns the previous stored day's (empty when there is none).</summary>
    private IReadOnlyDictionary<string, double> OpenInterestBefore(string symbol, OptionChain chain)
    {
        var day = DateOnly.FromDateTime(MarketClock.ToEastern(chain.AsOf ?? DateTimeOffset.Now).Date);
        try { return _insights.SwapOpenInterest(symbol, day, chain.Contracts); }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            AppLog.Warn("options", "open interest history unavailable", ex);
            return new Dictionary<string, double>();
        }
    }

    private async Task LoadInsightsAsync(string symbol, IReadOnlyList<DateOnly> expirations, double spot, CancellationToken ct)
    {
        IvRankText = "";
        IvRankTip = "";
        TermPoints = [];
        try
        {
            var now = DateTimeOffset.Now;
            if (await _insights.RankAsync(symbol, now, ct) is var (sample, rank, days))
            {
                var basis = NotificationService.IvBasisText(sample, _loc);
                var inv = CultureInfo.InvariantCulture;
                if (rank is { } r)
                {
                    IvRankText = string.Format(inv, _loc["Op_IvRank"], r.Rank, r.Percentile);
                    IvRankTip = string.Format(inv, _loc["Op_IvRankTip"], r.Days, basis, r.Low * 100, r.High * 100, sample.Iv * 100);
                }
                else
                {
                    IvRankText = string.Format(inv, _loc["Op_IvRankPending"], days, TyuStock.Analytics.IvRank.MinDays);
                    IvRankTip = _loc["Op_IvRankPendingTip"];
                }
            }
            TermPoints = await _insights.TermStructureAsync(symbol, expirations, spot, now, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is AllSourcesFailedException or Microsoft.Data.Sqlite.SqliteException)
        {
            AppLog.Warn("options", "volatility insights failed", ex);
        }
        if (string.Equals(symbol, _loadedSymbol, StringComparison.OrdinalIgnoreCase)) TableChanged?.Invoke(this, EventArgs.Empty);
    }
}
