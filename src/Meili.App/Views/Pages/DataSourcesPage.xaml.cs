using System.Windows.Controls;
using Meili.App.Services;
using Meili.App.ViewModels;

namespace Meili.App.Views.Pages;

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