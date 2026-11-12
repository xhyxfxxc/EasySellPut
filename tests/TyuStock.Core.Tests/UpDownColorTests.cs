using System.Text.Json;
using TyuStock.Core.Settings;

namespace TyuStock.Core.Tests;

/// <summary>E1: up/down colours follow the language until the user chooses.</summary>
public class UpDownColorTests
{
    [Fact]
    public void Unchosen_colours_follow_the_language()
    {
        var s = new AppSettings();
        Assert.Equal(UpDownColorMode.RedUp, s.EffectiveUpDownColors());
        Assert.Equal(UpDownColorMode.GreenUp, s.EffectiveUpDownColors("en-US"));
        s.Language = "en-US";
        Assert.Equal(UpDownColorMode.GreenUp, s.EffectiveUpDownColors());
    }

    [Fact]
    public void A_user_choice_wins_in_every_language()
    {
        var s = new AppSettings { UpDownColors = UpDownColorMode.RedUp, UpDownColorsChosen = true, Language = "en-US" };
        Assert.Equal(UpDownColorMode.RedUp, s.EffectiveUpDownColors());
        s.UpDownColors = UpDownColorMode.GreenUp;
        Assert.Equal(UpDownColorMode.GreenUp, s.EffectiveUpDownColors("zh-CN"));
    }

    [Fact]
    public void Old_files_with_green_up_count_as_a_choice()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"UpDownColors\":1,\"Language\":\"zh-CN\"}")!.Normalize();
        Assert.True(old.UpDownColorsChosen);
        Assert.Equal(UpDownColorMode.GreenUp, old.EffectiveUpDownColors());
        var dflt = JsonSerializer.Deserialize<AppSettings>("{\"UpDownColors\":0}")!.Normalize();
        Assert.False(dflt.UpDownColorsChosen);
    }
}
