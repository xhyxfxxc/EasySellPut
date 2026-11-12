using Meili.Core.Settings;

namespace Meili.Core.Tests;

/// <summary>D4: close-button behaviour.</summary>
public class CloseDecisionTests
{
    [Theory]
    [InlineData(CloseAction.Ask, false, false, CloseOutcome.Ask)]
    [InlineData(CloseAction.MinimizeToTray, false, false, CloseOutcome.HideToTray)]
    [InlineData(CloseAction.Exit, false, false, CloseOutcome.Exit)]
    [InlineData(CloseAction.Ask, true, false, CloseOutcome.Exit)]
    [InlineData(CloseAction.MinimizeToTray, true, false, CloseOutcome.Exit)]
    [InlineData(CloseAction.MinimizeToTray, false, true, CloseOutcome.Exit)]
    public void Decides(CloseAction setting, bool exitRequested, bool exitOnClose, CloseOutcome expected) =>
        Assert.Equal(expected, CloseDecision.Decide(setting, exitRequested, exitOnClose));

    [Fact]
    public void Default_is_ask_and_bad_values_are_reset()
    {
        Assert.Equal(CloseAction.Ask, new AppSettings().CloseAction);
        var s = new AppSettings { CloseAction = (CloseAction)42 }.Normalize();
        Assert.Equal(CloseAction.Ask, s.CloseAction);
    }

    [Fact]
    public void Choice_round_trips_through_the_settings_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            store.Load();
            store.Update(x => x.CloseAction = CloseAction.Exit);
            var again = new SettingsStore(dir);
            Assert.Equal(CloseAction.Exit, again.Load().CloseAction);
        }
        finally { Directory.Delete(dir, true); }
    }
}
