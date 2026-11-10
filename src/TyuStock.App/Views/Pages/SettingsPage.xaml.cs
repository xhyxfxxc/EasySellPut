using System.Windows.Controls;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = AppServices.Get<SettingsViewModel>();
    }
}
