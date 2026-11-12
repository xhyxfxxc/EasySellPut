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

    private static string L(string key) => (string)System.Windows.Application.Current.FindResource(key);

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
