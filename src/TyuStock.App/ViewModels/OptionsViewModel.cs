using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TyuStock.Analytics;
using TyuStock.App.Services;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;
using TyuStock.Data.Routing;
using ARight = TyuStock.Analytics.OptionRight;
using CRight = TyuStock.Core.Models.OptionRight;

namespace TyuStock.App.ViewModels;

public enum StrikeWindowKind { Near5 = 5, Near10 = 10, Near20 = 20, All = 0 }

/// <summary>Options page: expiration picker, T-quote chain, local IV/Greeks fill-in.</summary>
public partial class OptionsViewModel : ObservableObject
{
    public const double DefaultRate = 0.04;
    private readonly WatchlistStore _watchlist;
    private readonly IMarketDataService _data;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(45) };
    private CancellationTokenSource? _cts;
    private bool _active;
    private string? _lastError;
    private IReadOnlyList<OptionStrikeRow> _allRows = [];
    private bool _suppressExpChange;

    public ObservableCollection<string> WatchSymbols { get; } = [];
    public ObservableCollection<ExpirationChoice> Expirations { get; } = [];
    public ObservableCollection<OptionStrikeRow> Rows { get; } = [];
    public List<IvSmilePoint> SmilePoints { get; private set; } = [];

    [ObservableProperty] public partial string SymbolText { get; set; } = "";
    [ObservableProperty] public partial ExpirationChoice? SelectedExpiration { get; set; }
    [ObservableProperty] public partial StrikeWindowKind Window { get; set; } = StrikeWindowKind.Near10;
    [ObservableProperty] public partial bool ShowVolume { get; set; } = true;
    [ObservableProperty] public partial bool ShowOi { get; set; } = true;
    [ObservableProperty] public partial bool ShowIv { get; set; } = true;
    [ObservableProperty] public partial bool ShowDelta { get; set; } = true;
    [ObservableProperty] public partial bool ShowGamma { get; set; } = true;
    [ObservableProperty] public partial bool ShowTheta { get; set; } = true;
    [ObservableProperty] public partial bool ShowVega { get; set; } = true;
    [ObservableProperty] public partial bool ShowSmile { get; set; } = true;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string Headline { get; set; } = "";
    [ObservableProperty] public partial string DelayText { get; set; } = "";
    [ObservableProperty] public partial bool HasDelay { get; set; }
    [ObservableProperty] public partial string LocalNote { get; set; } = "";
    [ObservableProperty] public partial double? Spot { get; set; }
    [ObservableProperty] public partial bool HasRows { get; set; }
    [ObservableProperty] public partial OptionStrikeRow? AtmRow { get; set; }

    public event EventHandler? TableChanged;

    public OptionsViewModel(WatchlistStore watchlist, IMarketDataService data, LocalizationService loc)
    {
        _watchlist = watchlist; _data = data; _loc = loc;
        SymbolText = watchlist.AllSymbols.Contains("AAPL", StringComparer.OrdinalIgnoreCase)
            ? "AAPL"
            : (watchlist.AllSymbols.FirstOrDefault() ?? "AAPL");
        RebuildWatchSymbols();
        _timer.Tick += async (_, _) => await LoadAsync(refreshOnly: true);
        _watchlist.Changed += (_, _) => RebuildWatchSymbols();
        _loc.LanguageChanged += (_, _) =>
        {
            RelabelExpirations();
            UpdateStatus();
            TableChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public async Task ActivateAsync()
    {
        _active = true;
        if (Rows.Count == 0) await LoadAsync();
        else _timer.Start();
    }

    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
        _cts?.Cancel();
    }

    private void RebuildWatchSymbols()
    {
        WatchSymbols.Clear();
        foreach (var s in _watchlist.AllSymbols) WatchSymbols.Add(s);
    }

    [RelayCommand]
    private Task LoadFromBoxAsync() => LoadAsync();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(refreshOnly: true);

    [RelayCommand]
    private void SelectWindow(StrikeWindowKind kind)
    {
        Window = kind;
        ApplyWindow();
        TableChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedExpirationChanged(ExpirationChoice? value)
    {
        if (_suppressExpChange || value is null || !_active) return;
        _ = LoadAsync(refreshOnly: true);
    }

    partial void OnShowVolumeChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowOiChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowIvChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowDeltaChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowGammaChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowThetaChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowVegaChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowSmileChanged(bool value) => TableChanged?.Invoke(this, EventArgs.Empty);

    public async Task LoadAsync(bool refreshOnly = false)
    {
        if (!WatchlistStore.TryNormalizeSymbol(SymbolText, out var symbol, out _))
        {
            if (!refreshOnly)
            {
                _lastError = _loc["Op_InvalidSymbol"];
                ClearTable();
                UpdateStatus();
            }
            return;
        }
        SymbolText = symbol;
        IsBusy = true;
        if (!refreshOnly || Rows.Count == 0) StatusText = _loc["Op_Loading"];
        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _cts.Token;
        try
        {
            var exp = SelectedExpiration?.Date;
            var chainTask = _data.GetChainAsync(symbol, exp, ct);
            var quoteTask = SafeQuotes(symbol, ct);
            var fundTask = SafeYield(symbol, ct);
            var chain = await chainTask;
            var quotes = await quoteTask;
            var yield = await fundTask;
            var q = quotes.FirstOrDefault(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            var spot = chain.UnderlyingPrice ?? q?.Price ?? 0;
            if (!(spot > 0) && q is { PreviousClose: > 0 }) spot = q.PreviousClose.Value;
            Spot = spot > 0 ? spot : null;

            _suppressExpChange = true;
            try
            {
                var keep = chain.Expiration ?? exp;
                Expirations.Clear();
                foreach (var d in chain.Expirations)
                    Expirations.Add(new ExpirationChoice(d, FormatExp(d)));
                SelectedExpiration = Expirations.FirstOrDefault(e => e.Date == keep) ?? Expirations.FirstOrDefault();
            }
            finally { _suppressExpChange = false; }

            var settings = new OptionEnrichmentSettings
            {
                RiskFreeRate = DefaultRate,
                DividendYield = yield,
                Now = DateTimeOffset.Now,
            };
            var usedLocal = false;
            var calls = new Dictionary<double, EnrichedSide>();
            var puts = new Dictionary<double, EnrichedSide>();
            foreach (var c in chain.Contracts)
            {
                if (SelectedExpiration is { } se && c.Expiration != se.Date) continue;
                var e = OptionEnricher.Enrich(
                    c.Right == CRight.Call ? ARight.Call : ARight.Put,
                    spot, c.Strike, c.Expiration,
                    c.ImpliedVolatility, c.Bid, c.Ask, c.Last,
                    c.Delta, c.Gamma, c.Theta, c.Vega, c.Rho, c.TheoreticalPrice,
                    c.InTheMoney, settings);
                if (e.GreeksComputedLocally || e.IvOrigin is IvOrigin.SolvedFromMid or IvOrigin.SolvedFromLast)
                    usedLocal = true;
                var side = EnrichedSide.From(c, e, _loc);
                var bag = c.Right == CRight.Call ? calls : puts;
                bag[c.Strike] = side;
            }

            var strikes = calls.Keys.Concat(puts.Keys).Distinct().OrderBy(x => x).ToList();
            var atm = spot > 0 && strikes.Count > 0
                ? strikes.MinBy(k => Math.Abs(k - spot))
                : (double?)null;
            var rows = new List<OptionStrikeRow>(strikes.Count);
            foreach (var k in strikes)
            {
                calls.TryGetValue(k, out var call);
                puts.TryGetValue(k, out var put);
                rows.Add(new OptionStrikeRow(k, call, put, isAtm: atm is { } a && k == a, spot: Spot));
            }
            _allRows = rows;
            ApplyWindow();
            BuildSmile(rows);
            AtmRow = Rows.FirstOrDefault(r => r.IsAtm) ?? _allRows.FirstOrDefault(r => r.IsAtm);

            var src = SourceNames.Display(chain.Source, _loc);
            _lastError = Rows.Count == 0 ? _loc["Op_Empty"] : null;
            Headline = Spot is { } p
                ? $"{symbol}   {string.Format(_loc["Op_Spot"], p.ToString("0.00", CultureInfo.InvariantCulture))}"
                : symbol;
            DelayText = chain.DelayMinutes > 0 ? string.Format(_loc["Op_Delayed"], chain.DelayMinutes) : "";
            HasDelay = DelayText.Length > 0;
            LocalNote = usedLocal ? string.Format(_loc["Op_LocalGreeks"], DefaultRate) : "";
            if (_lastError is null)
                StatusText = string.Format(_loc["Op_Source"], src, Rows.Count);
        }
        catch (AllSourcesFailedException) { Fail(_loc["Op_AllFailed"], refreshOnly); }
        catch (OperationCanceledException) when (_active && !refreshOnly) { Fail(_loc["Op_Timeout"], refreshOnly); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail(ex.Message, refreshOnly); }
        finally
        {
            IsBusy = false;
            if (_active) _timer.Start();
            UpdateStatus();
            TableChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Fail(string message, bool refreshOnly)
    {
        _lastError = message;
        if (!refreshOnly || Rows.Count == 0) ClearTable();
    }

    private void ClearTable()
    {
        _allRows = [];
        Rows.Clear();
        SmilePoints = [];
        HasRows = false;
        AtmRow = null;
        Headline = "";
        DelayText = "";
        HasDelay = false;
        LocalNote = "";
    }

    private void ApplyWindow()
    {
        IEnumerable<OptionStrikeRow> src = _allRows;
        if (Window != StrikeWindowKind.All && _allRows.Count > 0)
        {
            var atmIndex = _allRows.ToList().FindIndex(r => r.IsAtm);
            if (atmIndex < 0) atmIndex = _allRows.Count / 2;
            var n = (int)Window;
            var lo = Math.Max(0, atmIndex - n);
            var hi = Math.Min(_allRows.Count - 1, atmIndex + n);
            src = _allRows.Skip(lo).Take(hi - lo + 1);
        }
        Rows.Clear();
        foreach (var r in src) Rows.Add(r);
        HasRows = Rows.Count > 0;
        AtmRow = Rows.FirstOrDefault(r => r.IsAtm) ?? _allRows.FirstOrDefault(r => r.IsAtm);
    }

    private void BuildSmile(IReadOnlyList<OptionStrikeRow> rows)
    {
        var pts = new List<IvSmilePoint>();
        foreach (var r in rows)
        {
            if (r.Call?.IvValue is { } civ) pts.Add(new IvSmilePoint(r.Strike, civ, true));
            if (r.Put?.IvValue is { } piv) pts.Add(new IvSmilePoint(r.Strike, piv, false));
        }
        SmilePoints = pts;
    }

    private void RelabelExpirations()
    {
        var keep = SelectedExpiration?.Date;
        _suppressExpChange = true;
        try
        {
            var dates = Expirations.Select(e => e.Date).ToList();
            Expirations.Clear();
            foreach (var d in dates) Expirations.Add(new ExpirationChoice(d, FormatExp(d)));
            SelectedExpiration = Expirations.FirstOrDefault(e => e.Date == keep) ?? Expirations.FirstOrDefault();
        }
        finally { _suppressExpChange = false; }
    }

    private string FormatExp(DateOnly d)
    {
        var dte = OptionTime.DaysToExpiry(d, DateTimeOffset.Now);
        var days = string.Format(_loc["Op_Dte"], dte);
        return $"{d:yyyy-MM-dd}  ·  {days}";
    }

    private void UpdateStatus()
    {
        if (_lastError is not null) StatusText = _lastError;
    }

    private async Task<IReadOnlyList<Quote>> SafeQuotes(string symbol, CancellationToken ct)
    {
        try { return await _data.GetQuotesAsync([symbol], ct); }
        catch { return []; }
    }

    private async Task<double> SafeYield(string symbol, CancellationToken ct)
    {
        try
        {
            var f = await _data.GetFundamentalsAsync(symbol, ct);
            return f.DividendYield is { } y && y >= 0 && y < 1 ? y : 0;
        }
        catch { return 0; }
    }
}

public sealed record ExpirationChoice(DateOnly Date, string Label)
{
    public override string ToString() => Label;
}

public sealed record IvSmilePoint(double Strike, double Iv, bool IsCall);

public sealed class OptionStrikeRow
{
    public double Strike { get; }
    public string StrikeText { get; }
    public EnrichedSide? Call { get; }
    public EnrichedSide? Put { get; }
    public bool IsAtm { get; }
    public bool CallItm { get; }
    public bool PutItm { get; }

    public OptionStrikeRow(double strike, EnrichedSide? call, EnrichedSide? put, bool isAtm, double? spot)
    {
        Strike = strike;
        StrikeText = strike.ToString(strike == Math.Truncate(strike) ? "0" : "0.00", CultureInfo.InvariantCulture);
        Call = call;
        Put = put;
        IsAtm = isAtm;
        CallItm = call?.InTheMoney ?? (spot is { } s && s > strike);
        PutItm = put?.InTheMoney ?? (spot is { } s2 && s2 < strike);
    }
}

public sealed class EnrichedSide
{
    public string Bid { get; init; } = "—";
    public string Ask { get; init; } = "—";
    public string Last { get; init; } = "—";
    public string Volume { get; init; } = "—";
    public string Oi { get; init; } = "—";
    public string Iv { get; init; } = "—";
    public string Delta { get; init; } = "—";
    public string Gamma { get; init; } = "—";
    public string Theta { get; init; } = "—";
    public string Vega { get; init; } = "—";
    public double? IvValue { get; init; }
    public bool InTheMoney { get; init; }
    public bool IvLocal { get; init; }
    public string IvHint { get; init; } = "";

    public static EnrichedSide From(OptionContract c, EnrichedOption e, LocalizationService loc)
    {
        var ivLocal = e.IvOrigin is IvOrigin.SolvedFromMid or IvOrigin.SolvedFromLast;
        return new EnrichedSide
        {
            Bid = Px(c.Bid), Ask = Px(c.Ask), Last = Px(c.Last),
            Volume = Qty(c.Volume), Oi = Qty(c.OpenInterest),
            Iv = e.ImpliedVolatility is { } iv ? (iv * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—",
            Delta = G(e.Delta, "0.000"), Gamma = G(e.Gamma, "0.0000"),
            Theta = G(e.Theta, "0.00"), Vega = G(e.Vega, "0.00"),
            IvValue = e.ImpliedVolatility, InTheMoney = e.InTheMoney, IvLocal = ivLocal,
            IvHint = ivLocal ? loc["Op_IvLocal"] : "",
        };
    }

    private static string Px(double? v) => v is { } x && x > 0 ? x.ToString("0.00", CultureInfo.InvariantCulture) : "—";
    private static string Qty(double? v) => v is { } x && x > 0 ? x.ToString("N0", CultureInfo.InvariantCulture) : "—";
    private static string G(double? v, string fmt) => v is { } x && double.IsFinite(x) ? x.ToString(fmt, CultureInfo.InvariantCulture) : "—";
}