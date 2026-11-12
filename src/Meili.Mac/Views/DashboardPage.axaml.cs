using Avalonia.Controls;

namespace Meili.Mac.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<DashboardViewModel>();
        DataContext = vm;
        PageLife.Bind(this, vm.ActivateAsync, vm.Deactivate);
    }
}
