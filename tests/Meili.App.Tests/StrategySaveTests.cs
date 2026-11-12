using System.IO;
using Meili.Analytics;
using Meili.App.Services;

namespace Meili.App.Tests;

/// <summary>B7: POP and Reg-T margin on the strategy page; saving to and loading from strategies.json.</summary>
public class StrategySaveTests
{
    [Fact]
    public void Strategy_shows_pop_and_margin_and_round_trips_through_the_store()
    {
        WpfHost.Run(async () =>
        {
            var vm = AppServices.Get<StrategyViewModel>();
            var store = AppServices.Get<StrategyStore>();
            try
            {
                vm.SymbolText = "MSFT";
                await vm.LoadAsync();
                vm.SelectedPreset = vm.Presets.First(p => p.Kind == StrategyKind.BullPutSpread);
                vm.ApplyPresetCommand.Execute(null);
                var spread = vm.Legs.Select(r => (r.Type, r.Side, r.StrikeText)).ToList();
                Assert.Equal(2, spread.Count);
                Assert.Matches(@"^\d{1,3}\.\d %$", vm.PopText);
                Assert.Equal(AppServices.Get<LocalizationService>()["St_MarginDefinedTip"], vm.MarginTip);
                Assert.NotEqual("", vm.MarginText);

                vm.SaveName = "put spread";
                vm.SaveStrategyCommand.Execute(null);
                Assert.Equal("put spread", vm.Saved[0].Name);
                Assert.Equal("put spread", new StrategyStore(Path.GetDirectoryName(store.FilePath)!).All[0].Name);

                vm.SelectedPreset = vm.Presets.First(p => p.Kind == StrategyKind.LongCall);
                vm.ApplyPresetCommand.Execute(null);
                Assert.Single(vm.Legs);

                vm.SelectedSaved = vm.Saved[0];
                await vm.LoadSavedCommand.ExecuteAsync(null);
                Assert.Equal(spread, vm.Legs.Select(r => (r.Type, r.Side, r.StrikeText)).ToList());
                Assert.Equal(StrategyKind.Custom, vm.SelectedPreset!.Kind);

                vm.DeleteSavedCommand.Execute(null);
                Assert.Empty(vm.Saved);
                Assert.Empty(new StrategyStore(Path.GetDirectoryName(store.FilePath)!).All);
            }
            finally
            {
                vm.Deactivate();
            }
        });
    }
}
