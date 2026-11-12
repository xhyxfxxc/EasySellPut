using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TyuStock.App.Views.Pages;

/// <summary>Code-built "coming soon" page for features scheduled in later milestones.</summary>
public abstract class PlaceholderPage : Page
{
    protected PlaceholderPage(string titleKey, SymbolRegular symbol, string milestone)
    {
        var title = new System.Windows.Controls.TextBlock { Style = (Style)FindResource("PageTitle") };
        title.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, titleKey);

        var icon = new SymbolIcon { Symbol = symbol, FontSize = 56, Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Left };
        icon.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");

        var heading = new System.Windows.Controls.TextBlock { Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "Placeholder_Title");
        var body = new System.Windows.Controls.TextBlock { Style = (Style)FindResource("Secondary") };
        body.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "Placeholder_Body");
        var tag = new System.Windows.Controls.TextBlock { Style = (Style)FindResource("Caption"), Text = milestone, Margin = new Thickness(0, 10, 0, 0) };

        var card = new Card
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(28),
            Content = new StackPanel { Children = { icon, heading, body, tag } },
        };
        Content = new ScrollViewer
        {
            Style = (Style)FindResource("PageScroller"),
            Content = new StackPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { title, card } },
        };
    }
}

public sealed class AlertsPage() : PlaceholderPage("Nav_Alerts", SymbolRegular.Alert24, "M8");
