using System.Windows;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.App.Views.Pages;
using TyuStock.Core.Settings;
using Wpf.Ui.Controls;

namespace TyuStock.App;

public partial class MainWindow : FluentWindow
{
    private readonly SettingsStore _settings = AppServices.Get<SettingsStore>();
    private readonly ThemeService _theme = AppServices.Get<ThemeService>();
    private readonly LocalizationService _loc = AppServices.Get<LocalizationService>();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppServices.Get<ShellViewModel>();
        RestorePlacement();
        RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(StartPageFromArgs() ?? typeof(DashboardPage));
        Loaded += (_, _) =>
        {
            // System theme watching needs a loaded window handle.
            if (_theme.Mode == AppThemeMode.System) _theme.Apply(AppThemeMode.System, this);
            UpdateThemeIcon();
        };
        _theme.ThemeChanged += (_, _) => UpdateThemeIcon();
        Closing += (_, _) => SavePlacement();
    }

    /// <summary>Optional <c>--page &lt;name&gt;</c> start page (used for screenshots and deep links).</summary>
    private static Type? StartPageFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.FindIndex(args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return null;
        return typeof(DashboardPage).Assembly.GetType($"TyuStock.App.Views.Pages.{args[i + 1]}Page", false, true);
    }

    private void OnToggleLanguage(object sender, RoutedEventArgs e)
    {
        var next = _loc.Language == "zh-CN" ? "en-US" : "zh-CN";
        _loc.Apply(next);
        _settings.Update(s => s.Language = next);
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        var next = _theme.Toggle();
        _theme.Apply(next, this);
        _settings.Update(s => s.Theme = next);
    }

    private void UpdateThemeIcon() =>
        ThemeIcon.Symbol = _theme.IsDark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;

    private void RestorePlacement()
    {
        var p = _settings.Current.Window;
        if (p is null || p.Width < 600 || p.Height < 400) return;
        var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                          SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!vs.IntersectsWith(new Rect(p.Left, p.Top, p.Width, p.Height))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left; Top = p.Top; Width = p.Width; Height = p.Height;
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Update(s => s.Window = new WindowPlacement
        {
            Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height,
            Maximized = WindowState == WindowState.Maximized,
        });
    }
}

