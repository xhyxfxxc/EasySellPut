using Avalonia.Controls;

namespace Meili.Mac.Views;

public partial class NewsPage : UserControl
{
    public NewsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<NewsViewModel>();
        DataContext = vm;
        AnnualChip.CommandParameter = true;      // the command takes a bool, not the XAML string
        QuarterlyChip.CommandParameter = false;
        PageLife.Bind(this, vm.ActivateAsync, vm.Deactivate);
        NewsList.DoubleTapped += (_, _) =>
        {
            if (NewsList.SelectedItem is NewsEntry item) vm.OpenArticleCommand.Execute(item);
        };
    }
}
