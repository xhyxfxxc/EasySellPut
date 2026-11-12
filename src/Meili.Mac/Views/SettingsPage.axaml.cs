using Avalonia.Controls;

namespace Meili.Mac.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<SettingsViewModel>();
        DataContext = vm;

    }
}
