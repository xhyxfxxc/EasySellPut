using Avalonia.Controls;

namespace Meili.Mac.Views;

public partial class AlertsPage : UserControl
{
    public AlertsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<AlertsViewModel>();
        DataContext = vm;

    }
}
