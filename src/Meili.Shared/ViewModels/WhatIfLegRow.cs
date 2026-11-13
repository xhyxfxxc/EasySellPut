using CommunityToolkit.Mvvm.ComponentModel;

namespace Meili.Shared.ViewModels;

/// <summary>One row in the live what-if panel (updated in place while the ruler is dragged).</summary>
public partial class WhatIfLegRow : ObservableObject
{
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial string PriceText { get; set; } = "";
    [ObservableProperty] public partial string PnlText { get; set; } = "";
    [ObservableProperty] public partial bool IsProfit { get; set; }
    [ObservableProperty] public partial bool IsLoss { get; set; }
    [ObservableProperty] public partial string Reason { get; set; } = "";
}
