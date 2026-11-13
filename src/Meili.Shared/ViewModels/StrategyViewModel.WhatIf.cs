using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meili.Analytics;
using Meili.Core.Models;

namespace Meili.Shared.ViewModels;

public partial class StrategyViewModel
{
    private OptionStrategy? _compiled;
    private StrategyValuation? _whatIfValuation;
    private bool _suppressWhatIf;
    private TickDensity _tickDensity = TickDensity.Medium;

    [ObservableProperty] public partial bool ShowHint { get; set; }
    [ObservableProperty] public partial bool IsExpirationToday { get; set; }
    [ObservableProperty] public partial bool NeedsFarExpiration { get; set; }
    [ObservableProperty] public partial ExpirationChoice? SelectedFarExpiration { get; set; }
    [ObservableProperty] public partial string SourceText { get; set; } = "";
    [ObservableProperty] public partial string StrategySummaryText { get; set; } = "";
    [ObservableProperty] public partial string MaxProfitConditionText { get; set; } = "";
    [ObservableProperty] public partial string MaxLossConditionText { get; set; } = "";
    [ObservableProperty] public partial string BreakevenConditionText { get; set; } = "";
    [ObservableProperty] public partial string CapitalText { get; set; } = "";
    [ObservableProperty] public partial string CapitalDetailText { get; set; } = "";
    [ObservableProperty] public partial bool HasStockCapital { get; set; }
    [ObservableProperty] public partial string WhatIfPriceText { get; set; } = "";
    [ObservableProperty] public partial string WhatIfPercentText { get; set; } = "";
    [ObservableProperty] public partial string WhatIfValueText { get; set; } = "";
    [ObservableProperty] public partial string WhatIfPnlText { get; set; } = "";
    [ObservableProperty] public partial bool WhatIfPnlIsProfit { get; set; }
    [ObservableProperty] public partial bool WhatIfPnlIsLoss { get; set; }
    [ObservableProperty] public partial bool HasWhatIf { get; set; }
    [ObservableProperty] public partial bool IsTickSparse { get; set; }
    [ObservableProperty] public partial bool IsTickMedium { get; set; } = true;
    [ObservableProperty] public partial bool IsTickDense { get; set; }

    public ObservableCollection<ExpirationChoice> FarExpirations { get; } = [];
    public ObservableCollection<WhatIfLegRow> WhatIfLegs { get; } = [];

    public double? WhatIfPrice { get; private set; }
    public double ChartLow { get; private set; }
    public double ChartHigh { get; private set; }
    public double TickStep { get; private set; } = 1;
    public IReadOnlyList<double> RulerTicks { get; private set; } = [];
    public TickDensity TickDensity => _tickDensity;

    /// <summary>Fired when the what-if price or its panel values change; the chart should move the marker, not rebuild the curve.</summary>
    public event EventHandler? WhatIfChanged;

    [RelayCommand]
    private void DismissHint()
    {
        ShowHint = false;
        _settings.Update(s => s.StrategyHintDismissed = true);
    }

    [RelayCommand]
    private void SelectTickDensity(int density)
    {
        var value = density switch { 0 => TickDensity.Sparse, 2 => TickDensity.Dense, _ => TickDensity.Medium };
        if (value == _tickDensity) return;
        _tickDensity = value;
        IsTickSparse = value == TickDensity.Sparse;
        IsTickMedium = value == TickDensity.Medium;
        IsTickDense = value == TickDensity.Dense;
        RebuildRuler(snapPrice: true);
        ApplyWhatIf();
        WhatIfChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Set the assumed underlying price (from the ruler, the chart, or the text field). Snaps to the current tick grid unless <paramref name="snap"/> is false.</summary>
    public void SetWhatIfPrice(double price, bool snap = true)
    {
        if (!(ChartHigh > ChartLow)) return;
        double next = snap ? PriceTicks.Snap(price, TickStep, ChartLow, ChartHigh) : Math.Clamp(price, ChartLow, ChartHigh);
        if (WhatIfPrice is { } cur && Math.Abs(cur - next) < 1e-9) return;
        WhatIfPrice = next;
        ApplyWhatIf();
        WhatIfChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnWhatIfPriceTextChanged(string value)
    {
        if (_suppressWhatIf) return;
        if (TryParseMoney(value, out var px) && px >= 0)
            SetWhatIfPrice(px, snap: false);
    }

    partial void OnSelectedFarExpirationChanged(ExpirationChoice? value)
    {
        if (_suppress || !_active || value is null || !NeedsFarExpiration) return;
        if (_manualLegs) return;
        BuildFromPreset(force: true);
    }

    private void SyncFarExpirations()
    {
        var near = SelectedExpiration?.Date;
        bool held = _suppress;
        _suppress = true;
        try
        {
            var keep = SelectedFarExpiration?.Date;
            FarExpirations.Clear();
            foreach (var e in Expirations)
                if (near is not { } n || e.Date > n) FarExpirations.Add(e);
            NeedsFarExpiration = IsFarKind(SelectedPreset?.Kind ?? StrategyKind.Custom);
            if (!NeedsFarExpiration)
            {
                SelectedFarExpiration = null;
                return;
            }
            SelectedFarExpiration = FarExpirations.FirstOrDefault(e => e.Date == keep)
                ?? FarExpirations.FirstOrDefault();
        }
        finally { _suppress = held; }
    }

    internal static bool IsFarKind(StrategyKind kind)
        => kind is StrategyKind.CalendarCall or StrategyKind.CalendarPut or StrategyKind.DiagonalCall;

    private void RebuildRuler(bool snapPrice)
    {
        if (!(ChartHigh > ChartLow))
        {
            RulerTicks = [];
            OnPropertyChanged(nameof(RulerTicks));
            return;
        }
        TickStep = PriceTicks.Step(ChartLow, ChartHigh, _tickDensity);
        RulerTicks = PriceTicks.Marks(ChartLow, ChartHigh, TickStep);
        OnPropertyChanged(nameof(TickStep));
        OnPropertyChanged(nameof(RulerTicks));
        if (snapPrice && WhatIfPrice is { } px)
            WhatIfPrice = PriceTicks.Snap(px, TickStep, ChartLow, ChartHigh);
    }

    private void ResetWhatIfAfterRecompute()
    {
        if (_compiled is null || !(ChartHigh > ChartLow))
        {
            HasWhatIf = false;
            WhatIfPrice = null;
            ClearWhatIfPanel();
            return;
        }
        double seed = WhatIfPrice is { } px && px >= ChartLow && px <= ChartHigh
            ? px
            : (Spot ?? (ChartLow + ChartHigh) / 2);
        WhatIfPrice = PriceTicks.Snap(seed, TickStep, ChartLow, ChartHigh);
        ApplyWhatIf();
    }

    private void ApplyWhatIf()
    {
        if (_compiled is null || WhatIfPrice is not { } px)
        {
            HasWhatIf = false;
            ClearWhatIfPanel();
            return;
        }
        var snap = WhatIf.Capture(_compiled, px, _whatIfValuation);
        var culture = Culture();
        _suppressWhatIf = true;
        try { WhatIfPriceText = NumberDisplay.Price(px); }
        finally { _suppressWhatIf = false; }
        if (Spot is { } spot && spot > 0)
        {
            double pct = (px - spot) / spot;
            string signed = (pct >= 0 ? "+" : "") + (pct * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
            WhatIfPercentText = string.Format(_loc["St_WhatIfVsSpot"], signed);
        }
        else WhatIfPercentText = "";
        WhatIfValueText = snap.CombinationValue.ToString("N2", culture);
        WhatIfPnlText = (snap.CombinationPnl >= 0 ? "+" : "") + snap.CombinationPnl.ToString("N2", culture);
        WhatIfPnlIsProfit = snap.CombinationPnl > 0.005;
        WhatIfPnlIsLoss = snap.CombinationPnl < -0.005;
        bool mixed = _compiled.HasMixedExpiries;
        SyncWhatIfRows(snap, mixed);
        HasWhatIf = true;
    }

    private void SyncWhatIfRows(WhatIfSnapshot snap, bool mixed)
    {
        while (WhatIfLegs.Count > snap.Legs.Count) WhatIfLegs.RemoveAt(WhatIfLegs.Count - 1);
        while (WhatIfLegs.Count < snap.Legs.Count) WhatIfLegs.Add(new WhatIfLegRow());
        var culture = Culture();
        for (int i = 0; i < snap.Legs.Count; i++)
        {
            var leg = snap.Legs[i];
            var row = WhatIfLegs[i];
            row.Label = WhatIfExplainer.LegLabel(leg.Leg, k => _loc[k], mixed);
            row.PriceText = NumberDisplay.Price(leg.TheoreticalPrice);
            row.PnlText = (leg.Pnl >= 0 ? "+" : "") + leg.Pnl.ToString("N2", culture);
            row.IsProfit = leg.Pnl > 0.005;
            row.IsLoss = leg.Pnl < -0.005;
            row.Reason = WhatIfExplainer.Format(leg, k => _loc[k]);
        }
    }

    private void ClearWhatIfPanel()
    {
        WhatIfPercentText = WhatIfValueText = WhatIfPnlText = "";
        WhatIfPnlIsProfit = WhatIfPnlIsLoss = false;
        WhatIfLegs.Clear();
    }

    private void PublishResults(OptionStrategy strategy, PayoffAnalysis? analysis)
    {
        StrategySummaryText = BuildSummary(strategy);
        if (analysis is null)
        {
            MaxProfitConditionText = MaxLossConditionText = BreakevenConditionText = "";
            PublishCapital(strategy, analysis);
            return;
        }
        MaxProfitConditionText = ConditionFor(strategy, analysis.MaxProfitPrice, analysis.IsMaxProfitUnbounded, profit: true);
        MaxLossConditionText = ConditionFor(strategy, analysis.MaxLossPrice, analysis.IsMaxLossUnbounded, profit: false);
        BreakevenConditionText = analysis.Breakevens.Count == 0
            ? ""
            : string.Join("  ·  ", analysis.Breakevens.Select(b => string.Format(_loc["St_Cond_Breakeven"], NumberDisplay.Price(b))));
        PublishCapital(strategy, analysis);
    }

    private void PublishCapital(OptionStrategy strategy, PayoffAnalysis? analysis)
    {
        double stock = 0, opt = 0;
        bool hasStock = false;
        foreach (var leg in strategy.Legs)
        {
            if (leg.IsOption) opt += leg.Cost;
            else { stock += leg.Cost; hasStock = true; }
        }
        HasStockCapital = hasStock && stock > 0;
        double net = stock + opt;
        var culture = Culture();
        if (HasStockCapital)
        {
            string stockText = stock.ToString("N0", culture);
            string premText = Math.Abs(opt).ToString("N0", culture);
            string netText = net.ToString("N0", culture);
            CapitalText = netText;
            CapitalDetailText = opt <= 0
                ? string.Format(_loc["St_CapitalStock"], stockText, premText, netText)
                : string.Format(_loc["St_CapitalStockDebit"], stockText, premText, netText);
            if (analysis is { IsMaxProfitUnbounded: false } a && net > 0)
                RewardText = string.Format(_loc["St_RewardOnCapital"], a.MaxProfit / net);
        }
        else
        {
            CapitalText = FormatCost(net);
            CapitalDetailText = "";
        }
    }

    private string ConditionFor(OptionStrategy strategy, double? at, bool unbounded, bool profit)
    {
        if (unbounded) return _loc[profit ? "St_Cond_UnlimitedUp" : "St_Cond_UnlimitedDown"];
        if (at is not { } p) return "";
        if (p <= 1e-9) return _loc["St_Cond_ToZero"];
        double mid = strategy.PnlAtExpiry(p);
        double up = strategy.PnlAtExpiry(p + Math.Max(0.5, p * 0.02));
        double down = strategy.PnlAtExpiry(Math.Max(0, p - Math.Max(0.5, p * 0.02)));
        double tol = Math.Max(0.05, Math.Abs(mid) * 1e-4);
        bool flatUp = Math.Abs(up - mid) <= tol;
        bool flatDown = Math.Abs(down - mid) <= tol;
        var px = NumberDisplay.Price(p);
        if (flatUp && !flatDown) return string.Format(_loc["St_Cond_AtOrAbove"], px);
        if (flatDown && !flatUp) return string.Format(_loc["St_Cond_AtOrBelow"], px);
        return string.Format(_loc["St_Cond_At"], px);
    }

    private string BuildSummary(OptionStrategy strategy)
    {
        string legs = string.Join(_loc.IsChinese ? "；" : "; ", strategy.Legs.Select(FormatSummaryLeg));
        string intro = strategy.Kind switch
        {
            StrategyKind.CoveredCall => CoverIntro(strategy, "St_Sum_CoveredCall", LegType.Call),
            StrategyKind.ProtectivePut => CoverIntro(strategy, "St_Sum_ProtectivePut", LegType.Put),
            StrategyKind.Collar => _loc["St_Sum_Collar"],
            StrategyKind.CalendarCall => _loc["St_Sum_CalendarCall"],
            StrategyKind.CalendarPut => _loc["St_Sum_CalendarPut"],
            StrategyKind.IronButterfly => _loc["St_Sum_IronButterfly"],
            StrategyKind.DiagonalCall => _loc["St_Sum_DiagonalCall"],
            _ => KindLabel(strategy.Kind) + (_loc.IsChinese ? "。" : ". "),
        };
        return string.IsNullOrWhiteSpace(legs) ? intro : intro + (_loc.IsChinese ? "" : " ") + legs + (_loc.IsChinese ? "。" : ".");
    }

    private string CoverIntro(OptionStrategy strategy, string key, LegType option)
    {
        var stock = strategy.Legs.FirstOrDefault(l => l.Type == LegType.Stock);
        var opt = strategy.Legs.FirstOrDefault(l => l.Type == option);
        int shares = stock?.Quantity ?? 0;
        int qty = opt?.Quantity ?? 0;
        string strike = opt is { } o ? NumberDisplay.Price(o.Strike) : "";
        return string.Format(_loc[key], shares.ToString(CultureInfo.InvariantCulture), qty.ToString(CultureInfo.InvariantCulture), strike);
    }

    private string FormatSummaryLeg(StrategyLeg leg)
    {
        string side = _loc[leg.Side == PositionSide.Short ? "St_Sell" : "St_Buy"];
        if (!leg.IsOption)
            return string.Format(_loc["St_Sum_LegStock"], side, leg.Quantity.ToString(CultureInfo.InvariantCulture), NumberDisplay.Price(leg.Premium));
        string right = _loc[leg.Type == LegType.Call ? "St_Call" : "St_Put"];
        string exp = leg.Expiry is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        return string.Format(_loc["St_Sum_LegOption"], side, leg.Quantity.ToString(CultureInfo.InvariantCulture),
            NumberDisplay.Price(leg.Strike), right, exp, NumberDisplay.Price(leg.Premium));
    }

    private static bool TryParseMoney(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
