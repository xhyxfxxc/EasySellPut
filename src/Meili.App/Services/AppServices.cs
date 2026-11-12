using Microsoft.Extensions.DependencyInjection;
using Meili.Core.Settings;

namespace Meili.App.Services;

/// <summary>Composition root access for XAML-created pages (WPF-UI instantiates pages with their parameterless constructor).</summary>
public static class AppServices
{
    public static IServiceProvider Provider { get; set; } = null!;
    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
    public static DataDirectoryInfo DataDirectory { get; set; } = null!;
}
