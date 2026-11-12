using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TyuStock.App.Views.Pages;

namespace TyuStock.App.Tests;

/// <summary>E2: digits are tabular everywhere and the watchlist's numeric columns are right-aligned.</summary>
public class TabularNumberTests
{
    [Theory]
    [MemberData(nameof(PageLoadTests.Pages), MemberType = typeof(PageLoadTests))]
    public void Every_page_uses_tabular_digits(Type pageType)
    {
        WpfHost.Run(() =>
        {
            var page = (Page)Activator.CreateInstance(pageType)!;
            Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(page));
        });
    }

    [Fact]
    public void Watchlist_numeric_headers_are_right_aligned()
    {
        WpfHost.Run(() =>
        {
            var page = new WatchlistPage();
            PageLoadTests.Layout(page);
            var list = (ListView)page.FindName("QuoteList");
            var grid = (GridView)list.View;
            var right = grid.Columns.Count(c => c.HeaderContainerStyle?.Setters.OfType<Setter>()
                .Any(s => s.Property == Control.HorizontalContentAlignmentProperty && Equals(s.Value, HorizontalAlignment.Right)) == true);
            Assert.True(right >= 4, $"expected price/change/%/volume right-aligned, got {right}");
        });
    }
}
