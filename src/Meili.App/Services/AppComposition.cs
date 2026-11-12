using Microsoft.Extensions.DependencyInjection;
using Meili.Core.Abstractions;
using Meili.Core.Settings;
using Meili.Data;

namespace Meili.App.Services;

/// <summary>DI registrations for the Windows app: the shared ones plus the WPF theme, notifications and UI platform.</summary>
public static class AppComposition
{
    public static void Register(IServiceCollection s, DataDirectoryInfo dataDir)
    {
        Ui.Platform = new WpfUiPlatform();
        SharedComposition.Register(s, dataDir, WpfStrings.Attach(new LocalizationService()));
        s.AddSingleton<ThemeService>();
        s.AddSingleton<IThemeController>(sp => sp.GetRequiredService<ThemeService>());
        s.AddSingleton<NotificationService>();
        s.AddSingleton<IAlertNotifier>(sp => sp.GetRequiredService<NotificationService>());
    }

    public static void OnStarted(IServiceProvider sp) => SharedComposition.OnStarted(sp);
}
