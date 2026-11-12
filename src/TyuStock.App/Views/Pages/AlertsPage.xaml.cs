using System.Windows.Controls;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;

namespace TyuStock.App.Views.Pages;

public partial class AlertsPage : Page
{
    public AlertsPage()
    {
        InitializeComponent();
        DataContext = AppServices.Get<AlertsViewModel>();
    }
}