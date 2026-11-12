using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Meili.App.Services;

/// <summary>WPF side of <see cref="IUiPlatform"/>.</summary>
public sealed class WpfUiPlatform : IUiPlatform
{
    public WpfUiPlatform() => ThemeService.ColorsChanged += (s, e) => ColorsChanged?.Invoke(s, e);

    public event EventHandler? ColorsChanged;

    public void Run(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    public IUiTimer CreateTimer(TimeSpan interval) => new Timer(interval);

    public object? Resource(string key) => Application.Current?.TryFindResource(key);

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK);

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public Task<string?> PickSavePathAsync(string title, string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedName,
            Filter = LocalizationService.Current["Ex_Filter"],
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return Task.FromResult(dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileName : null);
    }

    /// <summary>Background priority: ticks never preempt input or rendering.</summary>
    private sealed class Timer(TimeSpan interval) : IUiTimer
    {
        private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = interval };

        public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
        public bool IsEnabled => _timer.IsEnabled;
        public event EventHandler? Tick { add => _timer.Tick += value; remove => _timer.Tick -= value; }
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}

/// <summary>Keeps the WPF string dictionary (DynamicResource text) in step with <see cref="LocalizationService"/>.</summary>
public static class WpfStrings
{
    private static readonly string AssemblyName = typeof(WpfStrings).Assembly.GetName().Name!;

    public static LocalizationService Attach(LocalizationService loc)
    {
        ResourceDictionary? current = null;
        loc.Applying += language =>
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{AssemblyName};component/Resources/Strings/Strings.{language}.xaml", UriKind.Absolute),
            };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (current is not null) merged.Remove(current);
            merged.Add(dict);
            current = dict;
        };
        return loc;
    }
}
