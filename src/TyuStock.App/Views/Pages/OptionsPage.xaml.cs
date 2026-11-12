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
                or nameof(OptionsViewModel.ShowVega) or nameof(OptionsViewModel.ShowSpread) or nameof(OptionsViewModel.ShowDistance))
                Dispatcher.Invoke(RebuildColumns);
        };
    }

    private OptionsViewModel Vm => (OptionsViewModel)DataContext;


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

    /// <summary>H1/G4: columns come from <see cref="OptionColumns"/>; this method only turns definitions into cells.</summary>
    private void RebuildColumns()
    {
        var set = Vm.ColumnSet;
        var view = new GridView { AllowsColumnReorder = false };
        foreach (var c in OptionColumns.Side(call: true, set)) view.Columns.Add(Column(c, set, "Call", "CallItm", call: true));
        foreach (var c in OptionColumns.Middle(set)) view.Columns.Add(Column(c, set, null, null, call: true));
        foreach (var c in OptionColumns.Side(call: false, set)) view.Columns.Add(Column(c, set, "Put", "PutItm", call: false));
        ChainList.View = view;
    }

    private GridViewColumn Column(OptionColumnDef def, OptionColumnSet set, string? side, string? itm, bool call)
    {
        var strike = def.Key == OptionColumns.Strike.Key;
        var path = side is null ? def.Path : $"{side}.{def.Path}";
        var factory = new FrameworkElementFactory(typeof(Border));
        var style = new Style(typeof(Border));
        if (itm is not null)
        {
            var trigger = new DataTrigger { Binding = new Binding(itm), Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, TryFindResource("ControlFillColorSecondaryBrush")));
            style.Triggers.Add(trigger);
        }
        if (side is null)
        {
            var atm = new DataTrigger { Binding = new Binding("IsAtm"), Value = true };
            atm.Setters.Add(new Setter(Border.BackgroundProperty, TryFindResource("AccentFillColorSecondaryBrush")));
            style.Triggers.Add(atm);
        }
        factory.SetValue(FrameworkElement.StyleProperty, style);
        factory.SetValue(Border.PaddingProperty, set.Compact ? new Thickness(4, 3, 4, 3) : new Thickness(6, 4, 6, 4));

        var grid = new FrameworkElementFactory(typeof(Grid));
        if (def.Kind == OptionCellKind.OiBar && side is not null)
        {
            // G4: OI data bar, growing from the strike side (calls → right edge, puts → left edge).
            var bar = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
            bar.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentFillColorDefaultBrush");
            bar.SetValue(UIElement.OpacityProperty, 0.28);
            bar.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
            bar.SetValue(UIElement.RenderTransformOriginProperty, new System.Windows.Point(call ? 1 : 0, 0.5));
            bar.SetValue(FrameworkElement.MarginProperty, new Thickness(-2, 1, -2, 1));
            bar.SetBinding(UIElement.RenderTransformProperty, new Binding($"{side}.OiRatio") { Converter = RatioToScale.Instance });
            grid.AppendChild(bar);
        }
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(path));
        tb.SetValue(TextBlock.FontSizeProperty, strike ? 14.0 : 12.0);
        if (strike) tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        tb.SetValue(TextBlock.TextAlignmentProperty, side is null ? TextAlignment.Center : TextAlignment.Right);
        tb.SetValue(TextBlock.FontFamilyProperty, TryFindResource("NumberFont") as FontFamily ?? new FontFamily("Segoe UI"));
        var tbStyle = new Style(typeof(TextBlock));
        tbStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextFillColorPrimaryBrush")));
        if (def.Kind == OptionCellKind.Spread && side is not null)
        {
            // G4: wide spread → secondary text colour (still ≥ 4.5:1 in both themes), so liquid quotes stand out.
            var wide = new DataTrigger { Binding = new Binding($"{side}.IsWideSpread"), Value = true };
            wide.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextFillColorSecondaryBrush")));
            wide.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            tbStyle.Triggers.Add(wide);
            if (def.Key == "SpreadPct") tb.SetValue(FrameworkElement.ToolTipProperty, Find("Op_SpreadTip"));
        }
        if (def.Key == OptionColumns.Distance.Key) tb.SetValue(FrameworkElement.ToolTipProperty, Find("Op_DistanceTip"));
        tb.SetValue(FrameworkElement.StyleProperty, tbStyle);
        grid.AppendChild(tb);
        factory.AppendChild(grid);
        return new GridViewColumn
        {
            Header = Find(def.HeaderKey),
            Width = OptionColumns.WidthOf(def, set),
            CellTemplate = new DataTemplate { VisualTree = factory },
        };
    }

    /// <summary>G4: OI ratio (0..1) → horizontal scale of the data bar.</summary>
    private sealed class RatioToScale : IValueConverter
    {
        public static readonly RatioToScale Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            new ScaleTransform(value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0, 1);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
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