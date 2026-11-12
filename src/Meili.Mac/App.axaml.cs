using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Meili.Core.Diagnostics;
using Meili.Core.Settings;
using Meili.Mac.Platform;

namespace Meili.Mac;

public partial class App : Application
{
    private IntPtr _napActivity;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Start(desktop, desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>After the services are built (also used by the headless tests): language, theme and colours.</summary>
    public static void Boot()
    {
        var settings = AppServices.Get<SettingsStore>().Load();
        var loc = AppServices.Get<LocalizationService>();
        loc.Apply(settings.Language);
        var theme = AppServices.Get<MacTheme>();
        theme.ApplyUpDownColors(settings.EffectiveUpDownColors());
        // E1: until the user picks colours, they follow the interface language.
        loc.LanguageChanged += (_, _) => theme.ApplyUpDownColors(AppServices.Get<SettingsStore>().Current.EffectiveUpDownColors(loc.Language));
        theme.Apply(settings.Theme);
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        var dataDir = MacDataDirectory.Resolve(args, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Directory.CreateDirectory(dataDir.Path);
        AppServices.DataDirectory = dataDir;
        AppLog.Initialize(dataDir.Path);
        AppLog.Info("app", $"start mac {string.Join(' ', args)} data={dataDir.Path} ({dataDir.Source}) os={Environment.OSVersion.VersionString}");
        Dispatcher.UIThread.UnhandledException += OnUiException;

        var options = AppLaunchOptions.Parse(args);
        var services = new ServiceCollection();
        services.AddSingleton(options);
        MacComposition.Register(services, dataDir);
        AppServices.Provider = services.BuildServiceProvider();
        Boot();

        var window = new MainWindow();
        desktop.MainWindow = window;
        MacMenu.Install(this, window, desktop);
        desktop.ShutdownRequested += (_, _) => window.AllowClose();
        desktop.Exit += (_, e) => OnExit(e.ApplicationExitCode);
        // Dock icon click with no visible window: bring the hidden main window back (Mac convention).
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) window.ShowFromDock(); };
        window.Show();
        SharedComposition.OnStarted(AppServices.Provider);
        // App Nap would delay alert polling by minutes while the window is hidden or covered.
        if (!options.NoAlerts && OperatingSystem.IsMacOS())
            _napActivity = MacNative.BeginActivity("Price alert monitoring");
    }

    private static void OnUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handled first and no modal dialog: the in-app banner cannot re-enter this handler or pump a nested loop.
        e.Handled = true;
        try
        {
            var log = Path.Combine(AppServices.DataDirectory?.Path ?? AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(log);
            File.AppendAllText(Path.Combine(log, "crash.log"), $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
            AppLog.Error("app", "unhandled UI exception (see crash.log)", e.Exception);
            AppServices.Get<MacNotifier>().ShowError(e.Exception.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error("app", "could not record an unhandled UI exception", ex);
        }
    }

    private void OnExit(int code)
    {
        if (_napActivity != IntPtr.Zero) MacNative.EndActivity(_napActivity);
        AppServices.Get<AlertMonitor>().Dispose();   // only stops its subscription; nothing to fail
        try { AppServices.Get<IvAlertSampler>().Dispose(); AppServices.Get<AlertStore>().Flush(); }
        catch (Exception ex) { AppLog.Warn("app", "alert state flush failed", ex); }
        AppLog.Info("app", $"exit code {code}");
        (AppServices.Provider as IDisposable)?.Dispose();
    }
}
