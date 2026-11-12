using Microsoft.Extensions.DependencyInjection;
using TyuStock.App.ViewModels;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Settings;
using TyuStock.Data;

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
        s.AddSingleton<WatchlistStore>();
        s.AddSingleton<ShellViewModel>();
        s.AddSingleton(sp => new DataServices(sp.GetRequiredService<SettingsStore>().Current.Network));
        s.AddSingleton(sp => sp.GetRequiredService<DataServices>().Router);
        s.AddSingleton<IMarketDataService>(sp => sp.GetRequiredService<DataServices>().Router);
        s.AddSingleton<NetworkController>();
        s.AddSingleton<INetworkController>(sp => sp.GetRequiredService<NetworkController>());
        s.AddTransient<SettingsViewModel>();
        s.AddTransient<DashboardViewModel>();
        s.AddSingleton<WatchlistViewModel>();
        s.AddSingleton<ChartsViewModel>();
        s.AddSingleton<OptionsViewModel>();
        s.AddSingleton<StrategyViewModel>();
        s.AddSingleton<DataSourcesViewModel>();
    }

    /// <summary>After the window is shown: detect the proxy (Clash etc.) in the background without blocking startup.</summary>
    public static void OnStarted(IServiceProvider sp) => _ = sp.GetRequiredService<NetworkController>().ReconfigureAsync();
}