using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TyuStock.Core.Settings;

namespace TyuStock.App.ViewModels;

/// <summary>Overview page. M0: shows the watchlist symbols; quote loading is wired in once the data layer exists.</summary>
public partial class DashboardViewModel : ObservableObject
{
    protected readonly SettingsStore Store;
    public ObservableCollection<QuoteCardViewModel> Quotes { get; } = [];
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    public DashboardViewModel(SettingsStore store)
    {
        Store = store;
        foreach (var s in store.Current.Watchlist) Quotes.Add(new QuoteCardViewModel(s));
    }

    public virtual Task ActivateAsync() => Task.CompletedTask;
    public virtual void Deactivate() { }

    [RelayCommand]
    protected virtual Task RefreshAsync() => Task.CompletedTask;
}

public partial class QuoteCardViewModel(string symbol) : ObservableObject
{
    public string Symbol { get; } = symbol;
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string PriceText { get; set; } = "—";
    [ObservableProperty] public partial string ChangeText { get; set; } = "";
    [ObservableProperty] public partial string ExtText { get; set; } = "";
    [ObservableProperty] public partial string SourceText { get; set; } = "";
    [ObservableProperty] public partial System.Windows.Media.Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial System.Windows.Media.Brush? ChangeBackground { get; set; }
}
