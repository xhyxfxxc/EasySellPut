using System.Windows.Controls;
using Meili.App.Services;
using Meili.App.ViewModels;

namespace Meili.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = AppServices.Get<SettingsViewModel>();
    }
}
