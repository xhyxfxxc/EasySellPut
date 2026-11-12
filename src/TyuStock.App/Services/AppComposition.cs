using Microsoft.Extensions.DependencyInjection;
using TyuStock.App.ViewModels;
using TyuStock.Core.Settings;

namespace TyuStock.App.Services;

/// <summary>DI registrations for the whole app.</summary>
public static class AppComposition
{
    public static void Register(IServiceCollection s, DataDirectoryInfo dataDir)
    {
        s.AddSingleton(dataDir);
        s.AddSingleton(new SettingsStore(dataDir.Path));
        s.AddSingleton<LocalizationService>();
        s.AddSingleton<ThemeService>();
        s.AddSingleton<ShellViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddTransient<DashboardViewModel>();
    }

    public static void OnStarted(IServiceProvider sp) { }
}
