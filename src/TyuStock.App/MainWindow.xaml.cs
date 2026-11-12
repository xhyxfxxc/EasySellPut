using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
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
    private readonly AppLaunchOptions _options = AppServices.Get<AppLaunchOptions>();
    private readonly NavigationHub _nav = AppServices.Get<NavigationHub>();
    private readonly NotificationService _notify = AppServices.Get<NotificationService>();
    private bool _exitRequested;
    private bool _hidingToTray;
    private FrameworkElement? _currentPage;

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
            _notify.Attach(this);
        };
        _theme.ThemeChanged += (_, _) => UpdateThemeIcon();
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        _nav.Requested += OnNavigateRequested;
        _notify.ExitRequested += (_, _) => RequestExit();
        RootNavigation.Navigated += (_, e) => _currentPage = e.Page as FrameworkElement;
        WizardBox.WatchSymbols = AppServices.Get<WatchlistStore>().AllSymbols;
        WizardBox.CommitCommand = new RelayCommand(CommitWizard);
        PreviewKeyDown += OnShortcutKeyDown;
        PreviewTextInput += OnWindowTextInput;
        Wizard.IsKeyboardFocusWithinChanged += (_, e) => { if (e.NewValue is false) CloseWizard(); };
    }

    // ---------------- D2: keyboard ----------------

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (KeyboardShortcuts.PageFor(key, Keyboard.Modifiers) is { } page)
        {
            CloseWizard();
            RootNavigation.Navigate(page);
            e.Handled = true;
            return;
        }
        if (key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            var cmd = KeyboardShortcuts.RefreshCommandOf(_currentPage?.DataContext);
            if (cmd?.CanExecute(null) == true) cmd.Execute(null);
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && Wizard.Visibility == Visibility.Visible)
        {
            CloseWizard();
            e.Handled = true;
        }
    }

    private void OnWindowTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Wizard.Visibility == Visibility.Visible) return;
        if (!KeyboardShortcuts.StartsWizard(e.Text, IsTextInput(Keyboard.FocusedElement as DependencyObject))) return;
        WatchlistSync();
        Wizard.Visibility = Visibility.Visible;
        WizardBox.Text = e.Text.ToUpperInvariant();
        WizardBox.FocusInput();
        // Put the caret after the first character instead of selecting it, so typing continues naturally.
        Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox tb) { tb.SelectionLength = 0; tb.CaretIndex = tb.Text.Length; }
            _ = WizardBox.RefreshAsync(showAll: false, open: true);
        }, System.Windows.Threading.DispatcherPriority.Input);
        e.Handled = true;
    }

    private void WatchlistSync() => WizardBox.WatchSymbols = AppServices.Get<WatchlistStore>().AllSymbols;

    private static bool IsTextInput(DependencyObject? d)
    {
        for (var x = d; x is not null; x = x is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is TextBoxBase or System.Windows.Controls.PasswordBox || x is ComboBox { IsEditable: true }) return true;
        return false;
    }

    private void CommitWizard()
    {
        var text = WizardBox.Text;
        CloseWizard();
        if (!WatchlistStore.TryNormalizeSymbol(text, out var symbol, out _)) return;
        _nav.Go(typeof(ChartsPage), symbol);
    }

    private void CloseWizard()
    {
        if (Wizard.Visibility != Visibility.Visible) return;
        Wizard.Visibility = Visibility.Collapsed;
        WizardBox.Text = "";
        _currentPage?.Focus();
    }

    public void RequestExit()
    {
        _exitRequested = true;
        SavePlacement();
        Application.Current.Shutdown();
    }

    /// <summary>Optional <c>--page &lt;name&gt;</c> start page (used for screenshots and deep links).</summary>
    private static Type? StartPageFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.FindIndex(args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return null;
        return typeof(DashboardPage).Assembly.GetType($"TyuStock.App.Views.Pages.{args[i + 1]}Page", false, true);
    }

    private void OnNavigateRequested(object? sender, NavigationRequest e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // D1: set the shared symbol first; the target page adopts it when it activates (or at once if it is
            // already showing), so every symbol-aware page is reachable with a symbol — not only K-line and alerts.
            if (!string.IsNullOrWhiteSpace(e.Symbol) && e.PageType != typeof(AlertsPage))
                AppServices.Get<SymbolContext>().Set(e.Symbol);
            RootNavigation.Navigate(e.PageType);
            if (!string.IsNullOrWhiteSpace(e.Symbol) && e.PageType == typeof(AlertsPage))
                AppServices.Get<AlertsViewModel>().PrepareForSymbol(e.Symbol);
        });
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_exitRequested || _options.ExitOnClose || _hidingToTray) return;
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || _options.ExitOnClose)
        {
            SavePlacement();
            return;
        }
        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        if (_hidingToTray) return;
        _hidingToTray = true;
        try
        {
            SavePlacement();
            WindowState = WindowState.Normal;
            ShowInTaskbar = false;
            Hide();
        }
        finally { _hidingToTray = false; }
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