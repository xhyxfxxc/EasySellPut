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

        var options = AppLaunchOptions.Parse(e.Args);
        if (options.ExitOnClose)
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        var services = new ServiceCollection();
        services.AddSingleton(options);
        AppComposition.Register(services, dataDir);
        AppServices.Provider = services.BuildServiceProvider();

        var settings = AppServices.Get<SettingsStore>().Load();
        AppServices.Get<LocalizationService>().Apply(settings.Language);
        ThemeService.ApplyUpDownColors(settings.UpDownColors);

        var window = new MainWindow();
        MainWindow = window;
        AppServices.Get<ThemeService>().Apply(settings.Theme, window);
        window.Show();
        AppComposition.OnStarted(AppServices.Provider);
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