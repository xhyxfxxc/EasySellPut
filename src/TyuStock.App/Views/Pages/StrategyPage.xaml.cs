using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using Color = ScottPlot.Color;

namespace TyuStock.App.Views.Pages;

public partial class StrategyPage : Page
{
    private readonly ThemeService _theme = AppServices.Get<ThemeService>();
    private readonly ChartPainter _themePaint = new();

    public StrategyPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<StrategyViewModel>();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            HighlightHorizonButtons();
            await vm.ActivateAsync();
            RenderChart();
        };
        Unloaded += (_, _) => vm.Deactivate();
        vm.ChartChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            HighlightHorizonButtons();
            RenderChart();
        });
        _theme.ThemeChanged += (_, _) => Dispatcher.Invoke(RenderChart);
    }

    private StrategyViewModel Vm => (StrategyViewModel)DataContext;

    private void OnSymbolKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = Vm.LoadFromBoxCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnHorizonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            Vm.SelectHorizonCommand.Execute(days);
    }

    private void HighlightHorizonButtons()
    {
        foreach (var child in FindButtons(this))
        {
            if (child.Tag is not string tag || !int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                continue;
            var on = days == Vm.HorizonDays;
            child.SetResourceReference(Control.BackgroundProperty, on ? "AccentFillColorDefaultBrush" : "ControlFillColorSecondaryBrush");
            child.SetResourceReference(Control.ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Button b) yield return b;
            foreach (var x in FindButtons(c)) yield return x;
        }
    }

    private void RenderChart()
    {
        var plot = PayoffPlot.Plot;
        plot.Clear();
        var dark = _theme.IsDark;
        var text = Sp(BrushColor("TextFillColorPrimaryBrush", MediaColor(0xE8, 0xE8, 0xE8)));
        var muted = Sp(BrushColor("TextFillColorSecondaryBrush", MediaColor(0x9A, 0x9A, 0x9A)));
        var bg = Sp(BrushColor("ApplicationBackgroundBrush", dark ? MediaColor(0x20, 0x20, 0x20) : MediaColor(0xF3, 0xF3, 0xF3)));
        var dataBg = dark ? Color.FromHex("#2A2A2A") : Color.FromHex("#FFFFFF");
        var grid = dark ? Color.FromHex("#33FFFFFF") : Color.FromHex("#33000000");
        _themePaint.ApplyTheme(PayoffPlot, dark, Color.FromHex("#3B82F6"), Color.FromHex("#E2A03F"), text, muted, grid, bg, dataBg);

        var curve = Vm.Curve;
        if (curve is null || curve.Prices.Length < 2)
        {
            PayoffPlot.Refresh();
            return;
        }

        var expiry = plot.Add.Scatter(curve.Prices, curve.ExpiryPnl);
        expiry.LegendText = Find("St_ExpiryCurve");
        expiry.LineWidth = 2.2f;
        expiry.MarkerSize = 0;
        expiry.Color = Color.FromHex("#3B82F6");

        if (curve.ValuationPnl is { } tn && tn.Length == curve.Prices.Length)
        {
            var s = plot.Add.Scatter(curve.Prices, tn);
            s.LegendText = string.IsNullOrWhiteSpace(Vm.HorizonLabel) ? Find("St_CurrentCurve") : Vm.HorizonLabel;
            s.LineWidth = 1.8f;
            s.LinePattern = LinePattern.Dashed;
            s.MarkerSize = 0;
            s.Color = Color.FromHex("#E2A03F");
        }

        var zero = plot.Add.HorizontalLine(0);
        zero.LineColor = muted;
        zero.LineWidth = 1;

        if (Vm.Spot is { } spot)
        {
            var v = plot.Add.VerticalLine(spot);
            v.LineColor = muted;
            v.LineWidth = 1;
            v.LinePattern = LinePattern.Dashed;
        }

        if (Vm.ExpiryAnalysis is { Breakevens.Count: > 0 } analysis)
        {
            var xs = analysis.Breakevens.ToArray();
            var ys = Enumerable.Repeat(0.0, xs.Length).ToArray();
            var be = plot.Add.Scatter(xs, ys);
            be.LegendText = Find("St_Breakeven");
            be.LineWidth = 0;
            be.MarkerSize = 9;
            be.MarkerShape = MarkerShape.FilledCircle;
            be.Color = Color.FromHex("#22C55E");
        }

        plot.Axes.Bottom.Label.Text = Find("St_PriceAxis");
        plot.Axes.Left.Label.Text = Find("St_PnlAxis");
        plot.Title(Find("St_Chart"));
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        PayoffPlot.Refresh();
    }

    private static System.Windows.Media.Color MediaColor(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);
    private static System.Windows.Media.Color BrushColor(string key, System.Windows.Media.Color fallback)
        => Application.Current.TryFindResource(key) is SolidColorBrush br ? br.Color : fallback;
    private static Color Sp(System.Windows.Media.Color c) => new(c.R, c.G, c.B, c.A);
    private static string Find(string key) => Application.Current.TryFindResource(key) as string ?? key;
}