using Avalonia;
using Meili.Core.Diagnostics;

namespace Meili.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // H2: background-thread and unobserved task failures are logged.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            AppLog.Error("app", "unhandled exception (terminating=" + ev.IsTerminating + ")", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { AppLog.Warn("app", "unobserved task exception", ev.Exception); ev.SetObserved(); };
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Also used by the headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The application menu is built by MacMenu (localized, with Settings… ⌘, and Quit ⌘Q).
            .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
            .LogToTrace();
}
