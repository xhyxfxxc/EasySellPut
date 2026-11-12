using Meili.App.Services;
using Meili.Core.Settings;

namespace Meili.App.Tests;

/// <summary>D6: one symbol in several groups; removing it only touches the open group.</summary>
public class WatchlistGroupTests
{
    [Fact]
    public void Symbol_can_join_a_second_group_and_is_removed_from_the_open_group_only()
    {
        WpfHost.Run(async () =>
        {
            var store = AppServices.Get<WatchlistStore>();
            var vm = AppServices.Get<WatchlistViewModel>();
            var loc = AppServices.Get<LocalizationService>();
            Assert.Equal(WatchlistError.None, store.AddGroup("D6", out var g));
            try
            {
                vm.SelectedGroupId = g!.Id;
                await vm.AddSymbolAsync("aapl");
                Assert.Contains(string.Format(loc["Wl_AddedAlsoIn"], "AAPL", loc["Wl_DefaultGroup"]), vm.StatusText);
                Assert.Equal(2, store.GroupsContaining("AAPL").Count);

                vm.SelectedRow = vm.Rows.Single(r => r.Symbol == "AAPL");
                vm.RemoveSelectedCommand.Execute(null);
                Assert.Empty(vm.Rows);
                Assert.Contains("AAPL", store.FindGroup(WatchlistGroup.DefaultId)!.Symbols);
            }
            finally
            {
                vm.SelectedGroupId = WatchlistGroup.DefaultId;
                store.RemoveGroup(g!.Id);
            }
        });
    }
}
