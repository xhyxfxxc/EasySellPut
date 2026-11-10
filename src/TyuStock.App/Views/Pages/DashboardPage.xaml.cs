using System.Windows.Controls;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Views.Pages;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<DashboardViewModel>();
        DataContext = vm;
        Loaded += async (_, _) => await vm.ActivateAsync();
        Unloaded += (_, _) => vm.Deactivate();
    }
}
