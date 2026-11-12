using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.WPF;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using Color = ScottPlot.Color;

namespace TyuStock.App.Views.Pages;

public partial class OptionsPage : Page
{
    private readonly ThemeService _theme = AppServices.Get<ThemeService>();
    private readonly ChartPainter _themePaint = new();

    public OptionsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<OptionsViewModel>();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            RebuildColumns();
            HighlightWindowButtons();
            await vm.ActivateAsync();
            RenderSmile();
            ScrollAtm();
        };
        Unloaded += (_, _) => vm.Deactivate();
        vm.TableChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            RebuildColumns();
            HighlightWindowButtons();
            RenderSmile();
            ScrollAtm();
        });
        _theme.ThemeChanged += (_, _) => Dispatcher.Invoke(() => { RenderSmile(); });
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OptionsViewModel.ShowVolume) or nameof(OptionsViewModel.ShowOi)
                or nameof(OptionsViewModel.ShowIv) or nameof(OptionsViewModel.ShowDelta)
                or nameof(OptionsViewModel.ShowGamma) or nameof(OptionsViewModel.ShowTheta)
                or nameof(OptionsViewModel.ShowVega))
                Dispatcher.Invoke(RebuildColumns);
        };
    }

    private OptionsViewModel Vm => (OptionsViewModel)DataContext;

    private void OnSymbolKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = Vm.LoadFromBoxCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnWindowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<StrikeWindowKind>(tag, out var kind))
            Vm.SelectWindowCommand.Execute(kind);
    }

    private void HighlightWindowButtons()
    {
        foreach (var child in FindButtons(this))
        {
            if (child.Tag is not string tag || !Enum.TryParse<StrikeWindowKind>(tag, out var kind)) continue;
            var on = kind == Vm.Window;
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

    private void ScrollAtm()
    {
        if (Vm.AtmRow is { } row)
            ChainList.ScrollIntoView(row);
    }

    private void RebuildColumns()
    {
        var view = new GridView { AllowsColumnReorder = false };
        AddSide(view, call: true);
        view.Columns.Add(Column(Find("Op_Strike"), "StrikeText", 80, center: true, strike: true));
        AddSide(view, call: false);
        ChainList.View = view;
    }

    private void AddSide(GridView view, bool call)
    {
        var p = call ? "Call" : "Put";
        var vm = Vm;
        var cols = new List<(string header, string path, double width, string? fmt)>();
        if (call)
        {
            if (vm.ShowVolume) cols.Add((Find("Op_Volume"), $"{p}.Volume", 72, null));
            if (vm.ShowOi) cols.Add((Find("Op_OI"), $"{p}.Oi", 72, null));
            if (vm.ShowIv) cols.Add((Find("Op_IV"), $"{p}.Iv", 64, null));
            if (vm.ShowDelta) cols.Add((Find("Op_Delta"), $"{p}.Delta", 64, null));
            if (vm.ShowGamma) cols.Add((Find("Op_Gamma"), $"{p}.Gamma", 64, null));
            if (vm.ShowTheta) cols.Add((Find("Op_Theta"), $"{p}.Theta", 64, null));
            if (vm.ShowVega) cols.Add((Find("Op_Vega"), $"{p}.Vega", 58, null));
            cols.Add((Find("Op_Bid"), $"{p}.Bid", 58, null));
            cols.Add((Find("Op_Ask"), $"{p}.Ask", 58, null));
            cols.Add((Find("Op_Last"), $"{p}.Last", 58, null));
        }
        else
        {
            cols.Add((Find("Op_Last"), $"{p}.Last", 58, null));
            cols.Add((Find("Op_Bid"), $"{p}.Bid", 58, null));
            cols.Add((Find("Op_Ask"), $"{p}.Ask", 58, null));
            if (vm.ShowVega) cols.Add((Find("Op_Vega"), $"{p}.Vega", 58, null));
            if (vm.ShowTheta) cols.Add((Find("Op_Theta"), $"{p}.Theta", 64, null));
            if (vm.ShowGamma) cols.Add((Find("Op_Gamma"), $"{p}.Gamma", 64, null));
            if (vm.ShowDelta) cols.Add((Find("Op_Delta"), $"{p}.Delta", 64, null));
            if (vm.ShowIv) cols.Add((Find("Op_IV"), $"{p}.Iv", 64, null));
            if (vm.ShowOi) cols.Add((Find("Op_OI"), $"{p}.Oi", 72, null));
            if (vm.ShowVolume) cols.Add((Find("Op_Volume"), $"{p}.Volume", 72, null));
        }
        foreach (var c in cols)
            view.Columns.Add(Column(c.header, c.path, c.width, itm: call ? "CallItm" : "PutItm"));
    }

    private GridViewColumn Column(string header, string path, double width, bool center = false, bool strike = false, string? itm = null)
    {
        var align = center ? TextAlignment.Center : TextAlignment.Right;
        var factory = new FrameworkElementFactory(typeof(Border));
        if (itm is not null)
        {
            var bind = new Binding(itm);
            var style = new Style(typeof(Border));
            var trigger = new DataTrigger { Binding = bind, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, TryFindResource("ControlFillColorSecondaryBrush")));
            style.Triggers.Add(trigger);
            if (strike)
            {
                var atm = new DataTrigger { Binding = new Binding("IsAtm"), Value = true };
                atm.Setters.Add(new Setter(Border.BackgroundProperty, TryFindResource("AccentFillColorSecondaryBrush")));
                style.Triggers.Add(atm);
            }
            factory.SetValue(FrameworkElement.StyleProperty, style);
        }
        else if (strike)
        {
            var style = new Style(typeof(Border));
            var atm = new DataTrigger { Binding = new Binding("IsAtm"), Value = true };
            atm.Setters.Add(new Setter(Border.BackgroundProperty, TryFindResource("AccentFillColorSecondaryBrush")));
            style.Triggers.Add(atm);
            factory.SetValue(FrameworkElement.StyleProperty, style);
        }
        factory.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(path));
        tb.SetValue(TextBlock.FontSizeProperty, 12.0);
        tb.SetValue(TextBlock.TextAlignmentProperty, align);
        tb.SetValue(TextBlock.FontFamilyProperty, TryFindResource("NumberFont") as FontFamily ?? new FontFamily("Segoe UI"));
        if (strike)
        {
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            tb.SetValue(TextBlock.FontSizeProperty, 14.0);
        }
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        factory.AppendChild(tb);
        var template = new DataTemplate { VisualTree = factory };
        var col = new GridViewColumn
        {
            Header = header,
            Width = width,
            CellTemplate = template,
        };
        return col;
    }

    private void RenderSmile()
    {
        var plot = SmilePlot.Plot;
        plot.Clear();
        var dark = _theme.IsDark;
        var text = Sp(BrushColor("TextFillColorPrimaryBrush", MediaColor(0xE8, 0xE8, 0xE8)));
        var muted = Sp(BrushColor("TextFillColorSecondaryBrush", MediaColor(0x9A, 0x9A, 0x9A)));
        var bg = Sp(BrushColor("ApplicationBackgroundBrush", dark ? MediaColor(0x20, 0x20, 0x20) : MediaColor(0xF3, 0xF3, 0xF3)));
        var dataBg = dark ? Color.FromHex("#2A2A2A") : Color.FromHex("#FFFFFF");
        var grid = dark ? Color.FromHex("#33FFFFFF") : Color.FromHex("#33000000");
        _themePaint.ApplyTheme(SmilePlot, dark, Color.FromHex("#3B82F6"), Color.FromHex("#F59E0B"), text, muted, grid, bg, dataBg);

        var pts = Vm.SmilePoints;
        if (pts.Count == 0)
        {
            SmilePlot.Refresh();
            return;
        }
        var calls = pts.Where(p => p.IsCall).OrderBy(p => p.Strike).ToList();
        var puts = pts.Where(p => !p.IsCall).OrderBy(p => p.Strike).ToList();
        if (calls.Count > 0)
        {
            var s = plot.Add.Scatter(calls.Select(p => p.Strike).ToArray(), calls.Select(p => p.Iv * 100).ToArray());
            s.LegendText = Find("Op_Calls");
            s.MarkerSize = 5;
            s.LineWidth = 1.4f;
            s.Color = Color.FromHex("#3B82F6");
        }
        if (puts.Count > 0)
        {
            var s = plot.Add.Scatter(puts.Select(p => p.Strike).ToArray(), puts.Select(p => p.Iv * 100).ToArray());
            s.LegendText = Find("Op_Puts");
            s.MarkerSize = 5;
            s.LineWidth = 1.4f;
            s.Color = Color.FromHex("#E2A03F");
        }
        if (Vm.Spot is { } spot)
        {
            var v = plot.Add.VerticalLine(spot);
            v.LineColor = muted;
            v.LineWidth = 1;
            v.LinePattern = LinePattern.Dashed;
        }
        plot.Axes.Left.Label.Text = Find("Op_IV") + " %";
        plot.Axes.Bottom.Label.Text = Find("Op_Strike");
        plot.Title(Find("Op_Smile"));
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        SmilePlot.Refresh();
    }

    private static System.Windows.Media.Color MediaColor(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);
    private static System.Windows.Media.Color BrushColor(string key, System.Windows.Media.Color fallback)
        => Application.Current.TryFindResource(key) is SolidColorBrush br ? br.Color : fallback;
    private static Color Sp(System.Windows.Media.Color c) => new(c.R, c.G, c.B, c.A);
    private static string Find(string key) => Application.Current.TryFindResource(key) as string ?? key;
}