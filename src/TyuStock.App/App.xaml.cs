using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TyuStock.App.Services;
using TyuStock.Core.Settings;

namespace TyuStock.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var exeDir = AppContext.BaseDirectory;
        var dataDir = DataDirectoryResolver.Resolve(e.Args, exeDir,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Directory.CreateDirectory(dataDir.Path);
        AppServices.DataDirectory = dataDir;

        var services = new ServiceCollection();
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
        (AppServices.Provider as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var log = Path.Combine(AppServices.DataDirectory?.Path ?? AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(log);
            File.AppendAllText(Path.Combine(log, "crash.log"), $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch { }
        System.Windows.MessageBox.Show(e.Exception.Message, "tyuStockApp", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
