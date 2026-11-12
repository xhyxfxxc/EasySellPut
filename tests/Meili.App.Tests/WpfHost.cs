using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Meili.App.Services;
using Meili.Core.Abstractions;
using Meili.Core.Settings;

namespace Meili.App.Tests;

/// <summary>
/// One STA thread with a running Dispatcher and a single <see cref="App"/> instance for the whole test run
/// (WPF allows only one Application per process). Nothing is ever shown on screen: pages are measured and
/// arranged off-tree, which applies templates, styles and bindings without creating a window.
/// </summary>
public static class WpfHost
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);
    public static FakeMarketDataService Data { get; } = new();
    public static FakeIvSource Iv { get; } = new();
    public static FakeNetwork Network { get; } = new();
    public static string DataDir { get; } = Path.Combine(Path.GetTempPath(), "meili-app-tests-" + Environment.ProcessId);

    public static Dispatcher Dispatcher => Ui.Value;

    private static Dispatcher Start()
    {
        Dispatcher? d = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            try
            {
                d = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(d));
                // Never instantiate App here: its constructor queues OnStartup, which would build the real
                // composition and show MainWindow on the user's desktop. A plain Application with the same
                // merged dictionaries as App.xaml is enough for pages to resolve their resources.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var md = app.Resources.MergedDictionaries;
                md.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                md.Add(new Wpf.Ui.Markup.ControlsDictionary());
                md.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/{typeof(App).Assembly.GetName().Name};component/Resources/Styles.xaml", UriKind.Absolute) });
                Compose();
            }
            catch (Exception ex) { failure = ex; }
            finally { ready.Set(); }
            if (failure is null) Dispatcher.Run();
        }) { IsBackground = true, Name = "wpf-test-ui" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait();
        if (failure is not null) throw new InvalidOperationException("WPF test host failed to start", failure);
        return d!;
    }

    private static void Compose()
    {
        Directory.CreateDirectory(DataDir);
        var dataDir = new DataDirectoryInfo(DataDir, DataDirectorySource.CommandLine);
        AppServices.DataDirectory = dataDir;
        var services = new ServiceCollection();
        // NoAlerts keeps the test process away from the toast/COM registration and the tray icon.
        services.AddSingleton(new AppLaunchOptions { NoAlerts = true, ExitOnClose = true });
        AppComposition.Register(services, dataDir);
        services.AddSingleton<IMarketDataService>(Data);
        services.AddSingleton<IIvSource>(Iv);
        services.AddSingleton<INetworkController>(Network);
        AppServices.Provider = services.BuildServiceProvider();
        var settings = AppServices.Get<SettingsStore>().Load();
        AppServices.Get<LocalizationService>().Apply(settings.Language);
        ThemeService.ApplyUpDownColors(settings.UpDownColors);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits, pumping async continuations.</summary>
    public static void Run(Func<Task> action)
    {
        var op = Dispatcher.InvokeAsync(action);
        var task = op.Task.Unwrap();
        if (!task.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("UI test action timed out");
        task.GetAwaiter().GetResult();
    }

    public static void Run(Action action) => Run(() => { action(); return Task.CompletedTask; });

    /// <summary>Lets queued dispatcher work (bindings, BeginInvoke) run.</summary>
    public static async Task Settle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
}
