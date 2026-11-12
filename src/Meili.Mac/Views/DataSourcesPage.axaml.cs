using Avalonia.Controls;

namespace Meili.Mac.Views;

public partial class DataSourcesPage : UserControl
{
    public DataSourcesPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<DataSourcesViewModel>();
        DataContext = vm;
        PageLife.Bind(this, vm.ActivateAsync, vm.Deactivate);
    }
}
