using System.Windows.Controls;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Views.Pages;

public partial class DataSourcesPage : Page
{
    public DataSourcesPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<DataSourcesViewModel>();
        DataContext = vm;
        Loaded += async (_, _) => await vm.ActivateAsync();
        Unloaded += (_, _) => vm.Deactivate();
    }
}