using TyuStock.Core.Time;
using TyuStock.Core.Diagnostics;
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
using TyuStock.Data.Routing;
using ARight = TyuStock.Analytics.OptionRight;
using CRight = TyuStock.Core.Models.OptionRight;

namespace TyuStock.App.ViewModels;

/// <summary>Strategy P/L page: presets or custom legs, expiry and T+n curves, live chain fill-in.</summary>
public partial class StrategyViewModel : ObservableObject
{
    public const double DefaultVolatility = 0.30;

    private static readonly StrategyKind[] PresetOrder =
    [
        StrategyKind.CoveredCall, StrategyKind.ProtectivePut,
        StrategyKind.BullCallSpread, StrategyKind.BearCallSpread,
        StrategyKind.BullPutSpread, StrategyKind.BearPutSpread,
        StrategyKind.LongStraddle, StrategyKind.ShortStraddle,
        StrategyKind.LongStrangle, StrategyKind.ShortStrangle,
        StrategyKind.IronCondor, StrategyKind.LongCallButterfly, StrategyKind.LongPutButterfly,
        StrategyKind.LongCall, StrategyKind.LongPut, StrategyKind.Custom,
    ];

    private readonly WatchlistStore _watchlist;
    private readonly IMarketDataService _data;
    private readonly LocalizationService _loc;
    private readonly SymbolContext _context;
    private readonly TyuStock.Core.Pricing.RiskFreeRateProvider _rates;
    private TyuStock.Core.Pricing.RiskFreeRate _rate;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = RefreshCadence.OptionChain }; // C3
    private CancellationTokenSource? _cts;
    private bool _active, _manualLegs, _suppress, _hasChain;
    private string? _lastError;
    private IReadOnlyList<ListedQuote> _listed = [];
    private double _yield;
    private string? _loadedSymbol;
    private StrategySeed? _pendingSeed;

    public ObservableCollection<string> WatchSymbols { get; } = [];
    public ObservableCollection<ExpirationChoice> Expirations { get; } = [];
    public ObservableCollection<PresetChoice> Presets { get; } = [];
    public ObservableCollection<StrategyLegRow> Legs { get; } = [];
    public ObservableCollection<EnumChoice<LegType>> TypeChoices { get; } = [];
    public ObservableCollection<EnumChoice<PositionSide>> SideChoices { get; } = [];

    [ObservableProperty] public partial string SymbolText { get; set; } = "";
    [ObservableProperty] public partial ExpirationChoice? SelectedExpiration { get; set; }
    [ObservableProperty] public partial PresetChoice? SelectedPreset { get; set; }
    [ObservableProperty] public partial int Quantity { get; set; } = 1;
    [ObservableProperty] public partial int HorizonDays { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string Headline { get; set; } = "";
    /// <summary>F8: latest stock price beside the chain's (delayed, in-sync) underlying price.</summary>
    [ObservableProperty] public partial string LivePriceText { get; set; } = "";
    [ObservableProperty] public partial string DelayText { get; set; } = "";
    /// <summary>F5: "BSM (European) · r = x %" with the rate's origin.</summary>
    [ObservableProperty] public partial string ModelText { get; set; } = "";
    [ObservableProperty] public partial bool HasDelay { get; set; }
    [ObservableProperty] public partial string LocalNote { get; set; } = "";
    [ObservableProperty] public partial string CostText { get; set; } = "";
    [ObservableProperty] public partial string MaxProfitText { get; set; } = "";
    [ObservableProperty] public partial string MaxLossText { get; set; } = "";
    [ObservableProperty] public partial string BreakevenText { get; set; } = "";
    [ObservableProperty] public partial string GreeksText { get; set; } = "";
    [ObservableProperty] public partial string RewardText { get; set; } = "";
    [ObservableProperty] public partial double? Spot { get; set; }
    [ObservableProperty] public partial StrategyLegRow? SelectedLeg { get; set; }
    [ObservableProperty] public partial bool HasCurve { get; set; }

    public PayoffCurve? Curve { get; private set; }
    public PayoffAnalysis? ExpiryAnalysis { get; private set; }
    public string HorizonLabel { get; private set; } = "";
    public event EventHandler? ChartChanged;

    public StrategyViewModel(WatchlistStore watchlist, IMarketDataService data, LocalizationService loc, SymbolContext context,
        TyuStock.Core.Pricing.RiskFreeRateProvider rates)
    {
        _watchlist = watchlist; _data = data; _loc = loc; _context = context; _rates = rates;
        _rate = rates.Current;
        ModelText = PricingText.Model(_rate, loc);
        // D1: follow the shared symbol while visible; when hidden, ActivateAsync adopts it on the next visit.
        _context.Changed += (_, sym) => { if (_active && _context.ShouldAdopt(SymbolText)) _ = AdoptContextAsync(); };
        SymbolText = watchlist.AllSymbols.Contains("AAPL", StringComparer.OrdinalIgnoreCase)
            ? "AAPL"
            : (watchlist.AllSymbols.FirstOrDefault() ?? "AAPL");
        RebuildWatchSymbols();
        RebuildChoices();
        _timer.Tick += async (_, _) => await LoadAsync(refreshOnly: true);
        _watchlist.Changed += (_, _) => RebuildWatchSymbols();
        _loc.LanguageChanged += (_, _) =>
        {
            ModelText = PricingText.Model(_rate, _loc);
            RelabelExpirations();
            RebuildChoices();
            RelabelLegs();
            RelabelHeader();
            Recompute();
        };
    }

    public async Task ActivateAsync()
    {
        _active = true;
        if (_context.ShouldAdopt(SymbolText)) { await AdoptContextAsync(); return; }
        if (Legs.Count == 0 || _pendingSeed is not null) await LoadAsync();
        else _timer.Start();
    }

    /// <summary>D1: switch to the shared symbol (set by another page, the watchlist, or a toast) and load it.</summary>
    private Task AdoptContextAsync()
    {
        SymbolText = _context.Current!;
        return LoadAsync();
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

    private void RebuildChoices()
    {
        _suppress = true;
        try
        {
            var keep = SelectedPreset?.Kind ?? StrategyKind.CoveredCall;
            Presets.Clear();
            foreach (var k in PresetOrder)
                Presets.Add(new PresetChoice(k, KindLabel(k)));
            SelectedPreset = Presets.FirstOrDefault(p => p.Kind == keep) ?? Presets.FirstOrDefault();

            TypeChoices.Clear();
            TypeChoices.Add(new EnumChoice<LegType>(LegType.Call, _loc["St_Call"]));
            TypeChoices.Add(new EnumChoice<LegType>(LegType.Put, _loc["St_Put"]));
            TypeChoices.Add(new EnumChoice<LegType>(LegType.Stock, _loc["St_Stock"]));
            SideChoices.Clear();
            SideChoices.Add(new EnumChoice<PositionSide>(PositionSide.Long, _loc["St_Buy"]));
            SideChoices.Add(new EnumChoice<PositionSide>(PositionSide.Short, _loc["St_Sell"]));
        }
        finally { _suppress = false; }
    }

    private string KindLabel(StrategyKind k) => _loc[$"St_Kind_{k}"];

    [RelayCommand]
    private Task LoadFromBoxAsync() => LoadAsync();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(refreshOnly: true);

    [RelayCommand]
    private void ApplyPreset() => BuildFromPreset(force: true);

    [RelayCommand]
    private void SelectHorizon(int days)
    {
        HorizonDays = days;
        Recompute();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddCall() => AddBlank(LegType.Call);

    [RelayCommand]
    private void AddPut() => AddBlank(LegType.Put);

    [RelayCommand]
    private void AddStock() => AddBlank(LegType.Stock);

    [RelayCommand]
    private void RemoveLeg()
    {
        if (SelectedLeg is null) return;
        Legs.Remove(SelectedLeg);
        SelectedLeg = null;
        OnLegEdited();
    }

    partial void OnSelectedExpirationChanged(ExpirationChoice? value)
    {
        if (_suppress || !_active || value is null) return;
        _ = LoadAsync(refreshOnly: true);
    }

    partial void OnSelectedPresetChanged(PresetChoice? value)
    {
        if (_suppress || value is null) return;
        if (value.Kind == StrategyKind.Custom)
        {
            _manualLegs = true;
            return;
        }
        BuildFromPreset(force: true);
    }

    partial void OnQuantityChanged(int value)
    {
        if (_suppress || value <= 0 || _manualLegs) return;
        BuildFromPreset(force: true);
    }

    public async Task LoadAsync(bool refreshOnly = false)
    {
        if (!WatchlistStore.TryNormalizeSymbol(SymbolText, out var symbol, out _))
        {
            if (!refreshOnly)
            {
                _lastError = _loc["St_InvalidSymbol"];
                UpdateStatus();
            }
            return;
        }
        SymbolText = symbol;
        _context.Set(symbol);
        IsBusy = true;
        if (!refreshOnly || Legs.Count == 0) StatusText = _loc["St_Loading"];
        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _cts.Token;
        try
        {
            var seed = _pendingSeed is { } ps && ps.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) ? ps : null;
            var exp = seed?.Expiration
                ?? (string.Equals(symbol, _loadedSymbol, StringComparison.OrdinalIgnoreCase) ? SelectedExpiration?.Date : null);
            OptionChain? chain = null;
            string? chainError = null;
            try { chain = await _data.GetChainAsync(symbol, exp, ct); }
            catch (AllSourcesFailedException) { chainError = _loc["St_AllFailed"]; }
            catch (OperationCanceledException) when (_active && !refreshOnly) { chainError = _loc["St_Timeout"]; throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Warn("strategy", "chain load failed", ex); chainError = ex.Message; }

            if (chain is not null) _loadedSymbol = symbol;
            var quotes = await SafeQuotes(symbol, ct);
            _yield = await SafeYield(symbol, ct);
            var q = quotes.FirstOrDefault(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            // F8: the chain's own underlying price (same moment as the option quotes) drives IV/Greeks.
            var underlying = UnderlyingText.Describe(chain, q, _loc, DateTimeOffset.Now);
            var spot = underlying.Spot ?? 0;
            Spot = underlying.Spot;

            _suppress = true;
            try
            {
                if (chain is not null)
                {
                    var keep = chain.Expiration ?? exp;
                    Expirations.Clear();
                    foreach (var d in chain.Expirations)
                        Expirations.Add(new ExpirationChoice(d, FormatExp(d)));
                    SelectedExpiration = Expirations.FirstOrDefault(e => e.Date == keep) ?? Expirations.FirstOrDefault();
                }
            }
            finally { _suppress = false; }

            _rate = await _rates.GetAsync(ct);
            ModelText = PricingText.Model(_rate, _loc);
            _listed = [];
            _hasChain = false;
            if (chain is not null)
            {
                var settings = new OptionEnrichmentSettings
                {
                    RiskFreeRate = _rate.Rate,
                    DividendYield = _yield,
                    Now = DateTimeOffset.Now,
                };
                var expDate = SelectedExpiration?.Date ?? chain.Expiration;
                var list = new List<ListedQuote>();
                foreach (var c in chain.Contracts)
                {
                    if (expDate is { } se && c.Expiration != se) continue;
                    list.Add(ToListed(c, spot, settings));
                }
                _listed = list;
                _hasChain = list.Count > 0;
                _lastError = _hasChain ? null : (chainError ?? _loc["St_NoChain"]);
            }
            else
            {
                _lastError = chainError ?? _loc["St_NoChain"];
            }

            (_hdrSymbol, _hdrQuote, _hdrChain) = (symbol, q, chain);
            RelabelHeader();

            if (seed is not null)
            {
                _pendingSeed = null;
                ApplySeed(seed);
            }
            else if (_hasChain && !_manualLegs) BuildFromPreset(force: false);
            else if (_hasChain && _manualLegs) RefreshLegQuotes();
            Recompute();
        }
        catch (OperationCanceledException) when (_active && !refreshOnly)
        {
            _lastError = _loc["St_Timeout"];
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Warn("strategy", "load failed", ex); _lastError = ex.Message; }
        finally
        {
            IsBusy = false;
            if (_active) _timer.Start();
            UpdateStatus();
            ChartChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ListedQuote ToListed(OptionContract c, double spot, OptionEnrichmentSettings settings)
    {
        var right = c.Right == CRight.Call ? ARight.Call : ARight.Put;
        var e = OptionEnricher.Enrich(
            right, spot, c.Strike, c.Expiration,
            c.ImpliedVolatility, c.Bid, c.Ask, c.Last,
            c.Delta, c.Gamma, c.Theta, c.Vega, c.Rho, c.TheoreticalPrice,
            c.InTheMoney, settings);
        double px = c.Mid ?? (c.Last is > 0 ? c.Last.Value : 0);
        if (!(px > 0) && e.TheoreticalPrice is { } th && th > 0) px = th;
        if (!(px > 0) && c.Bid is > 0) px = c.Bid.Value;
        if (!(px > 0) && c.Ask is > 0) px = c.Ask.Value;
        var iv = e.ImpliedVolatility ?? double.NaN;
        return new ListedQuote(right, c.Strike, px, iv);
    }

    /// <summary>
    /// D1: "build a strategy from this contract" (option chain right-click). Starts a custom strategy with that one leg;
    /// the user then adds legs. Takes effect on the next load (immediately if the page is visible).
    /// </summary>
    public void Seed(StrategySeed seed)
    {
        _pendingSeed = seed;
        SymbolText = seed.Symbol;
        _context.Set(seed.Symbol);
        if (_active) _ = LoadAsync();
    }

    private void ApplySeed(StrategySeed seed)
    {
        var right = seed.Right == CRight.Call ? ARight.Call : ARight.Put;
        var q = _listed.FirstOrDefault(x => x.Right == right && Math.Abs(x.Strike - seed.Strike) < 1e-8);
        var exp = SelectedExpiration?.Date ?? seed.Expiration;
        var premium = q.Strike > 0 ? q.Premium : 0;
        var iv = q.Strike > 0 && double.IsFinite(q.ImpliedVolatility) ? q.ImpliedVolatility : DefaultVolatility;
        var qty = Math.Max(1, Quantity);
        var leg = seed.Right == CRight.Call
            ? StrategyLeg.Call(seed.Side, seed.Strike, premium, exp, qty, iv)
            : StrategyLeg.Put(seed.Side, seed.Strike, premium, exp, qty, iv);
        _manualLegs = true;
        _suppress = true;
        try { SelectedPreset = Presets.FirstOrDefault(p => p.Kind == StrategyKind.Custom) ?? SelectedPreset; }
        finally { _suppress = false; }
        ReplaceLegs([leg]);
        if (q.Strike <= 0) _lastError = _loc["St_SeedNoQuote"];
    }

    private void BuildFromPreset(bool force)
    {
        var kind = SelectedPreset?.Kind ?? StrategyKind.CoveredCall;
        if (kind == StrategyKind.Custom) { _manualLegs = true; return; }
        if (!_hasChain || SelectedExpiration is null || Spot is not { } spot)
        {
            if (force) _lastError = _loc["St_NoChain"];
            UpdateStatus();
            return;
        }
        var qty = Math.Max(1, Quantity);
        var built = StrategyFromQuotes.TryBuild(kind, spot, SelectedExpiration.Date, _listed, qty);
        if (built is null)
        {
            _lastError = _loc["St_BuildFailed"];
            UpdateStatus();
            return;
        }
        _manualLegs = false;
        ReplaceLegs(built.Legs);
        Recompute();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLegQuotes()
    {
        foreach (var row in Legs)
        {
            if (row.Type == LegType.Stock) continue;
            if (!row.TryParseStrike(out var k)) continue;
            var right = row.Type == LegType.Call ? ARight.Call : ARight.Put;
            var q = _listed.FirstOrDefault(x => x.Right == right && Math.Abs(x.Strike - k) < 1e-8);
            if (q.Strike <= 0) continue;
            row.SetQuotes(q.Premium, q.ImpliedVolatility);
        }
    }

    private void ReplaceLegs(IReadOnlyList<StrategyLeg> legs)
    {
        _suppress = true;
        try
        {
            Legs.Clear();
            foreach (var l in legs)
                Legs.Add(StrategyLegRow.From(l, this));
            SelectedLeg = Legs.FirstOrDefault();
        }
        finally { _suppress = false; }
    }

    private void AddBlank(LegType type)
    {
        var exp = SelectedExpiration?.Date ?? DateOnly.FromDateTime(UsEastern.ToEastern(DateTimeOffset.Now).DateTime).AddDays(30);
        var strike = Spot is { } s ? Math.Round(s / 5) * 5 : 100;
        var premium = type == LegType.Stock ? (Spot ?? 0) : 0;
        var qty = type == LegType.Stock ? Math.Max(1, Quantity) * StrategyLeg.DefaultMultiplier : Math.Max(1, Quantity);
        var iv = type == LegType.Stock ? double.NaN : DefaultVolatility;
        var leg = type switch
        {
            LegType.Call => StrategyLeg.Call(PositionSide.Long, Math.Max(strike, 1), premium, exp, qty, iv),
            LegType.Put => StrategyLeg.Put(PositionSide.Long, Math.Max(strike, 1), premium, exp, qty, iv),
            _ => StrategyLeg.Stock(PositionSide.Long, Math.Max(1, qty), Math.Max(premium, 0)),
        };
        // Prefer a live quote when adding an option.
        if (type != LegType.Stock && _listed.Count > 0)
        {
            var right = type == LegType.Call ? ARight.Call : ARight.Put;
            var q = _listed.Where(x => x.Right == right).OrderBy(x => Math.Abs(x.Strike - (Spot ?? x.Strike))).FirstOrDefault();
            if (q.Strike > 0)
                leg = type == LegType.Call
                    ? StrategyLeg.Call(PositionSide.Long, q.Strike, q.Premium, exp, Math.Max(1, Quantity), q.ImpliedVolatility)
                    : StrategyLeg.Put(PositionSide.Long, q.Strike, q.Premium, exp, Math.Max(1, Quantity), q.ImpliedVolatility);
        }
        Legs.Add(StrategyLegRow.From(leg, this));
        SelectedLeg = Legs.Last();
        OnLegEdited();
    }

    internal void OnLegEdited()
    {
        if (_suppress) return;
        _manualLegs = true;
        if (SelectedPreset?.Kind != StrategyKind.Custom)
        {
            _suppress = true;
            try { SelectedPreset = Presets.FirstOrDefault(p => p.Kind == StrategyKind.Custom) ?? SelectedPreset; }
            finally { _suppress = false; }
        }
        Recompute();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Recompute()
    {
        LocalNote = "";
        RewardText = "";
        GreeksText = "";
        HorizonLabel = HorizonDays <= 0 ? _loc["St_CurrentCurve"] : string.Format(_loc["St_TnCurve"], HorizonDays);
        if (!TryCompile(out var strategy, out var err))
        {
            if (strategy is null && Legs.Count == 0)
            {
                Curve = null; ExpiryAnalysis = null; HasCurve = false;
                CostText = MaxProfitText = MaxLossText = BreakevenText = "";
                if (_lastError is null) StatusText = err ?? _loc["St_NoLegs"];
            }
            else if (err is not null) StatusText = err;
            return;
        }

        try
        {
            var analysis = strategy!.AnalyzeAtExpiry();
            ExpiryAnalysis = analysis;
            var spot = Spot ?? (strategy.Strikes.Count > 0 ? strategy.Strikes[strategy.Strikes.Count / 2] : 100);
            var (lo, hi) = strategy.SuggestPriceRange(spot);
            var usedDefaultIv = strategy.Legs.Any(l => l.IsOption && !l.HasImpliedVolatility);
            var nowVal = new StrategyValuation(DateTimeOffset.Now, _rate.Rate, _yield) { DefaultVolatility = DefaultVolatility };
            StrategyValuation? overlay = nowVal;
            if (HorizonDays > 0)
            {
                var etToday = DateOnly.FromDateTime(UsEastern.ToEastern(DateTimeOffset.Now).DateTime);
                var target = etToday.AddDays(HorizonDays);
                if (strategy.EarliestExpiry is { } e && target >= e) overlay = null;
                else overlay = StrategyValuation.AtDate(target, _rate.Rate, _yield) with { DefaultVolatility = DefaultVolatility };
            }
            Curve = strategy.Curve(lo, hi, 251, overlay);
            HasCurve = Curve.Prices.Length > 1;
            CostText = FormatCost(strategy.NetCost);
            MaxProfitText = FormatBound(analysis.MaxProfit, analysis.MaxProfitPrice, unbounded: analysis.IsMaxProfitUnbounded);
            MaxLossText = FormatBound(analysis.MaxLoss, analysis.MaxLossPrice, unbounded: analysis.IsMaxLossUnbounded);
            BreakevenText = analysis.Breakevens.Count == 0
                ? "—"
                : string.Join("  ·  ", analysis.Breakevens.Select(b => NumberDisplay.Price(b)));
            if (double.IsFinite(analysis.RewardToRisk))
                RewardText = string.Format(_loc["St_RewardRisk"], analysis.RewardToRisk);
            if (Spot is { } s && overlay is { } val)
            {
                var g = strategy.GreeksAt(s, val);
                if (double.IsFinite(g.Delta))
                    GreeksText = string.Format(_loc["St_Delta"], g.Delta.ToString("0.00", CultureInfo.InvariantCulture));
            }
            if (usedDefaultIv && overlay is not null)
                LocalNote = string.Format(_loc["St_UsedDefaultIv"], DefaultVolatility);
            if (_lastError is null && _hasChain == false && Legs.Count > 0)
                StatusText = _loc["St_NoChain"];
        }
        catch (Exception ex)
        {
            AppLog.Warn("strategy", "valuation failed", ex);
            HasCurve = false;
            StatusText = ex.Message;
        }
    }

    private bool TryCompile(out OptionStrategy? strategy, out string? error)
    {
        strategy = null; error = null;
        if (Legs.Count == 0)
        {
            error = _loc["St_NoLegs"];
            return false;
        }
        var list = new List<StrategyLeg>(Legs.Count);
        for (int i = 0; i < Legs.Count; i++)
        {
            if (!Legs[i].TryToLeg(out var leg, out var err))
            {
                error = string.Format(_loc["St_BadLeg"], i + 1, err);
                return false;
            }
            list.Add(leg!);
        }
        var kind = _manualLegs ? StrategyKind.Custom : (SelectedPreset?.Kind ?? StrategyKind.Custom);
        strategy = new OptionStrategy(list, KindLabel(kind), kind);
        return true;
    }

    private string FormatCost(double net)
    {
        var px = Math.Abs(net).ToString("N2", Culture());
        if (Math.Abs(net) < 0.005) return _loc["St_FlatCost"];
        return net > 0 ? string.Format(_loc["St_Debit"], px) : string.Format(_loc["St_Credit"], px);
    }

    private string FormatBound(double value, double? at, bool unbounded)
    {
        if (unbounded) return _loc["St_Unbounded"];
        var body = value.ToString("N2", Culture());
        if (at is { } p && p > 0) return $"{body}  ({string.Format(_loc["St_AtPrice"], NumberDisplay.Price(p))})";
        return body;
    }

    private CultureInfo Culture() => CultureInfo.GetCultureInfo(_loc.Language);

    private void RelabelExpirations()
    {
        var keep = SelectedExpiration?.Date;
        _suppress = true;
        try
        {
            var dates = Expirations.Select(e => e.Date).ToList();
            Expirations.Clear();
            foreach (var d in dates) Expirations.Add(new ExpirationChoice(d, FormatExp(d)));
            SelectedExpiration = Expirations.FirstOrDefault(e => e.Date == keep) ?? Expirations.FirstOrDefault();
        }
        finally { _suppress = false; }
    }

    private void RelabelLegs()
    {
        foreach (var row in Legs) row.RefreshLabels();
    }

    private string FormatExp(DateOnly d)
    {
        var dte = OptionTime.DaysToExpiry(d, DateTimeOffset.Now);
        return $"{d:yyyy-MM-dd}  ·  {string.Format(_loc["Op_Dte"], dte)}";
    }

    // E4: inputs of the header texts, so a language switch can relabel them without a reload.
    private string? _hdrSymbol;
    private Quote? _hdrQuote;
    private OptionChain? _hdrChain;

    /// <summary>E4: header/status texts from the last load, rebuilt in the current language.</summary>
    private void RelabelHeader()
    {
        if (_hdrSymbol is not { } symbol) return;
        var now = DateTimeOffset.Now;
        var u = UnderlyingText.Describe(_hdrChain, _hdrQuote, _loc, now);
        Headline = u.Main.Length > 0 ? $"{symbol}   {u.Main}" : symbol;
        LivePriceText = u.Live;
        DelayText = _hdrChain is { } c ? FreshnessText.ChainTiming(c, _loc, now) : "";
        HasDelay = DelayText.Length > 0;
        if (_hasChain && _lastError is null && _hdrChain is { } ch)
            StatusText = string.Format(_loc["St_Source"], SourceNames.Display(ch.Source, _loc), _listed.Count);
    }

    private void UpdateStatus()
    {
        if (_lastError is not null) StatusText = _lastError;
    }

    private Task<IReadOnlyList<Quote>> SafeQuotes(string symbol, CancellationToken ct) => SafeData.QuotesAsync(_data, symbol, "strategy", ct);

    private Task<double> SafeYield(string symbol, CancellationToken ct) => SafeData.DividendYieldAsync(_data, symbol, "strategy", ct);
}

public sealed record PresetChoice(StrategyKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record EnumChoice<T>(T Value, string Label) where T : struct, Enum
{
    public override string ToString() => Label;
}

public partial class StrategyLegRow : ObservableObject
{
    private readonly StrategyViewModel _owner;
    private bool _suppress;

    [ObservableProperty] public partial LegType Type { get; set; } = LegType.Call;
    [ObservableProperty] public partial PositionSide Side { get; set; } = PositionSide.Long;
    [ObservableProperty] public partial string StrikeText { get; set; } = "";
    [ObservableProperty] public partial string PremiumText { get; set; } = "";
    [ObservableProperty] public partial string IvText { get; set; } = "";
    [ObservableProperty] public partial string QtyText { get; set; } = "1";
    [ObservableProperty] public partial string ExpiryText { get; set; } = "";
    [ObservableProperty] public partial string TypeLabel { get; set; } = "";
    [ObservableProperty] public partial string SideLabel { get; set; } = "";

    public StrategyLegRow(StrategyViewModel owner) => _owner = owner;

    public static StrategyLegRow From(StrategyLeg leg, StrategyViewModel owner)
    {
        var row = new StrategyLegRow(owner) { _suppress = true };
        row.Type = leg.Type;
        row.Side = leg.Side;
        row.StrikeText = leg.IsOption ? F(leg.Strike) : "";
        row.PremiumText = F(leg.Premium);
        row.IvText = leg.HasImpliedVolatility ? (leg.ImpliedVolatility * 100).ToString("0.0", CultureInfo.InvariantCulture) : "";
        row.QtyText = leg.Quantity.ToString(CultureInfo.InvariantCulture);
        row.ExpiryText = leg.Expiry is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        row._suppress = false;
        row.RefreshLabels();
        return row;
    }

    public void SetQuotes(double premium, double iv)
    {
        _suppress = true;
        try
        {
            PremiumText = F(premium);
            IvText = double.IsFinite(iv) ? (iv * 100).ToString("0.0", CultureInfo.InvariantCulture) : IvText;
        }
        finally { _suppress = false; }
    }

    public void RefreshLabels()
    {
        TypeLabel = Type switch { LegType.Put => L("St_Put"), LegType.Stock => L("St_Stock"), _ => L("St_Call") };
        SideLabel = Side == PositionSide.Short ? L("St_Sell") : L("St_Buy");
    }

    public bool TryParseStrike(out double strike)
        => double.TryParse(StrikeText, NumberStyles.Float, CultureInfo.InvariantCulture, out strike) && strike > 0
           || double.TryParse(StrikeText, NumberStyles.Float, CultureInfo.CurrentCulture, out strike) && strike > 0;

    public bool TryToLeg(out StrategyLeg? leg, out string error)
    {
        leg = null; error = "";
        if (!int.TryParse(QtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)
            && !int.TryParse(QtyText, NumberStyles.Integer, CultureInfo.CurrentCulture, out qty) || qty <= 0)
        { error = L("St_Qty"); return false; }
        if (!TryParseMoney(PremiumText, out var premium) || premium < 0)
        { error = L("St_Premium"); return false; }
        double iv = double.NaN;
        if (!string.IsNullOrWhiteSpace(IvText))
        {
            if (!TryParseMoney(IvText, out var ivPct) || ivPct < 0) { error = L("St_IV"); return false; }
            iv = ivPct / 100.0;
        }
        try
        {
            if (Type == LegType.Stock)
            {
                leg = StrategyLeg.Stock(Side, qty, premium);
                return true;
            }
            if (!TryParseStrike(out var strike)) { error = L("St_Strike"); return false; }
            if (!DateOnly.TryParse(ExpiryText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)
                && !DateOnly.TryParse(ExpiryText, CultureInfo.CurrentCulture, DateTimeStyles.None, out exp))
            { error = L("St_Expiry"); return false; }
            leg = Type == LegType.Call
                ? StrategyLeg.Call(Side, strike, premium, exp, qty, iv)
                : StrategyLeg.Put(Side, strike, premium, exp, qty, iv);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("strategy", "leg parse failed", ex);
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseMoney(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string F(double v)
        => v == Math.Truncate(v) ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.00", CultureInfo.InvariantCulture);

    private string L(string key) => System.Windows.Application.Current?.TryFindResource(key) as string ?? key;

    partial void OnTypeChanged(LegType value) { RefreshLabels(); Notify(); }
    partial void OnSideChanged(PositionSide value) { RefreshLabels(); Notify(); }
    partial void OnStrikeTextChanged(string value) => Notify();
    partial void OnPremiumTextChanged(string value) => Notify();
    partial void OnIvTextChanged(string value) => Notify();
    partial void OnQtyTextChanged(string value) => Notify();
    partial void OnExpiryTextChanged(string value) => Notify();

    private void Notify()
    {
        if (!_suppress) _owner.OnLegEdited();
    }
}