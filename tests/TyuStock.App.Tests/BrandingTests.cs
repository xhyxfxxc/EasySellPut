using System.Reflection;
using TyuStock.App.Services;

namespace TyuStock.App.Tests;

/// <summary>B4: nothing the user can see in Explorer, Task Manager or network logs carries the developer prefix.</summary>
public class BrandingTests
{
    [Fact]
    public void App_assembly_and_its_file_attributes_are_neutral()
    {
        var asm = typeof(App).Assembly;
        Assert.Equal("MeiliTerminal", asm.GetName().Name);
        var visible = new[]
        {
            asm.GetName().Name, asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product,
            asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title, asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company,
            NotificationService.AppUserModelId, TyuStock.Data.Providers.Sec.SecProvider.UserAgent,
        };
        Assert.All(visible, v => Assert.DoesNotContain("tyu", v ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void String_dictionaries_load_under_the_new_assembly_name()
    {
        WpfHost.Run(() =>
        {
            var loc = AppServices.Get<LocalizationService>();
            loc.Apply("en-US");
            Assert.Equal("Overview", loc["Nav_Dashboard"]);
            loc.Apply("zh-CN");
            Assert.Equal("概览", loc["Nav_Dashboard"]);
        });
    }
}
