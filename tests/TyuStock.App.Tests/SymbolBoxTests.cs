using TyuStock.App.Services;
using CommunityToolkit.Mvvm.Input;
using TyuStock.App.Views.Controls;

namespace TyuStock.App.Tests;

/// <summary>D3: shared symbol box — text sync, suggestions from watch list + search, commit. Never shown on screen.</summary>
public class SymbolBoxTests
{
    [Fact]
    public void Suggests_watchlist_then_search_hits_and_commits_the_pick()
    {
        WpfHost.Run(async () =>
        {
            var commits = 0;
            var box = new SymbolBox
            {
                WatchSymbols = new[] { "AAPL", "MSFT", "AMZN" },
                CommitCommand = new RelayCommand(() => commits++),
            };
            box.Text = "a";
            await box.RefreshAsync(showAll: false, open: true);
            Assert.Equal("AAPL", box.Items[0].Symbol);
            Assert.True(box.Items[0].FromWatchlist);
            Assert.Contains(box.Items, i => i.Symbol == "A" && !i.FromWatchlist);   // the fake search echoes the query
            Assert.False(box.NoMatch);

            await box.RefreshAsync(showAll: true, open: true);
            Assert.Equal(["AAPL", "MSFT", "AMZN"], box.Items.Select(i => i.Symbol));

            box.Pick(box.Items[1]);
            Assert.Equal("MSFT", box.Text);
            Assert.Equal(1, commits);
        });
    }

    [Fact]
    public void Pages_bind_their_symbol_through_the_shared_box()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<TyuStock.App.ViewModels.OptionsViewModel>();
            var page = new TyuStock.App.Views.Pages.OptionsPage();
            PageLoadTests.Layout(page);
            await WpfHost.Settle();
            var box = (SymbolBox)page.FindName("SymbolInput");
            vm.SymbolText = "NVDA";
            Assert.Equal("NVDA", box.Text);
            box.Text = "AMD";
            Assert.Equal("AMD", vm.SymbolText);
            vm.SymbolText = "";
        });
    }
}
