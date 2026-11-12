using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TyuStock.App.Services;
using TyuStock.Core.Settings;

namespace TyuStock.App;

public partial class App : Application
{
    private bool _handlingUnhandled;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var exeDir = AppContext.BaseDirectory;
        var dataDir = DataDirectoryResolver.Resolve(e.Args, exeDir,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Directory.CreateDirectory(dataDir.Path);
        AppServices.DataDirectory = dataDir;
        TyuStock.Core.Diagnostics.AppLog.Initialize(dataDir.Path);
        TyuStock.Core.Diagnostics.AppLog.Info("app", $"start {string.Join(' ', e.Args)} data={dataDir.Path} ({dataDir.Source})");

        var options = AppLaunchOptions.Parse(e.Args);
        if (options.ExitOnClose)
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        var services = new ServiceCollection();
        services.AddSingleton(options);
        AppComposition.Register(services, dataDir);
        AppServices.Provider = services.BuildServiceProvider();

        var settings = AppServices.Get<SettingsStore>().Load();
        AppServices.Get<LocalizationService>().Apply(settings.Language);
        ThemeService.ApplyUpDownColors(settings.EffectiveUpDownColors());
        // E1: until the user picks colours, they follow the interface language.
        var loc = AppServices.Get<LocalizationService>();
        loc.LanguageChanged += (_, _) => ThemeService.ApplyUpDownColors(AppServices.Get<SettingsStore>().Current.EffectiveUpDownColors(loc.Language));

        var window = new MainWindow();
        MainWindow = window;
        AppServices.Get<ThemeService>().Apply(settings.Theme, window);
        if (options.Smoke)
        {
            // B3: real window, but off-screen, not activated and not in the taskbar, so the self-check never disturbs the desktop.
            window.PrepareForSmoke();
            TaskScheduler.UnobservedTaskException += (_, ev) => SmokeRunner.RecordError(ev.Exception);
        }
        window.Show();
        AppComposition.OnStarted(AppServices.Provider);
        if (options.Smoke)
            Dispatcher.BeginInvoke(async () =>
            {
                int code;
                try { code = await new SmokeRunner().RunAsync(window, options); }
                catch (Exception ex) { SmokeRunner.RecordError(ex); code = 2; }
                Shutdown(code);
            }, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Get<NotificationService>().Dispose(); } catch { }
        try { AppServices.Get<AlertMonitor>().Dispose(); } catch { }
        (AppServices.Provider as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Always mark handled first so a layout-time XAML error cannot re-enter this
        // handler (MessageBox would pump a new dispatcher frame and overflow the stack).
        e.Handled = true;
        if (AppServices.Provider is not null && AppServices.Get<AppLaunchOptions>().Smoke)
        {
            SmokeRunner.RecordError(e.Exception);   // no dialog during a self-check
            return;
        }
        if (_handlingUnhandled) return;
        _handlingUnhandled = true;
        try
        {
            var log = Path.Combine(AppServices.DataDirectory?.Path ?? AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(log);
            File.AppendAllText(Path.Combine(log, "crash.log"), $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
            var title = TryFindResource("App_Title") as string ?? "美丽稻谷卖方绿泡群专用终端";
            var message = e.Exception.Message;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try { System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning); }
                catch { }
            });
        }
        catch { }
    }
}