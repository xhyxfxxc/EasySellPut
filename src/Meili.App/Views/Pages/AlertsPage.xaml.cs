using System.Windows.Controls;
using Meili.App.Services;
using Meili.App.ViewModels;

namespace Meili.App.Views.Pages;

public partial class AlertsPage : Page
{
    public AlertsPage()
    {
        InitializeComponent();
        DataContext = AppServices.Get<AlertsViewModel>();
    }
}