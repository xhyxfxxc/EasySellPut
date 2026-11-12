using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScottPlot;
using ScottPlot.Avalonia;
using Meili.Mac.Platform;

namespace Meili.Mac.Views;

public partial class ChartsPage : UserControl
{
    private readonly ChartPainter _painter = new();
    private readonly MacTheme _theme = AppServices.Get<MacTheme>();
    private bool _userView;   // C2: the user zoomed or panned since the last fit

    public ChartsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<ChartsViewModel>();
        DataContext = vm;
        // Redraw only while on screen: a hidden page costs nothing when quotes, theme or colours change.
        EventHandler onPlot = (_, _) => Dispatcher.UIThread.Post(Render);
        EventHandler onColors = (_, _) => Dispatcher.UIThread.Post(Render);
        AttachedToVisualTree += async (_, _) =>
        {
            vm.PlotChanged += onPlot;
            Ui.Platform.ColorsChanged += onColors;
            await vm.ActivateAsync();
            Render();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            vm.PlotChanged -= onPlot;
            Ui.Platform.ColorsChanged -= onColors;
            vm.Deactivate();
        };
        CompareBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { vm.ApplyCompareCommand.Execute(null); e.Handled = true; } };
        PricePlot.PointerReleased += OnPriceClick;
        foreach (var plot in AllPlots())
        {
            plot.PointerMoved += OnPlotPointerMoved;
            plot.PointerWheelChanged += (_, _) => _userView = true;
            plot.PointerPressed += (_, _) => _userView = true;
        }
    }

    private ChartsViewModel Vm => (ChartsViewModel)DataContext!;

    private IEnumerable<AvaPlot> AllPlots() => [PricePlot, VolumePlot, MacdPlot, RsiPlot, KdjPlot];
    private IEnumerable<AvaPlot> Lower() => new[] { VolumePlot, MacdPlot, RsiPlot, KdjPlot }.Where(p => p.IsVisible);

    // B5: while a drawing tool is active, a click on the price chart places a point (snapped to the nearest bar's time).
    private void OnPriceClick(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm.DrawMode is null || Vm.Series is not { } series) return;
        var pos = e.GetPosition(PricePlot);
        var c = PricePlot.Plot.GetCoordinates((float)pos.X, (float)pos.Y);
        var i = ChartPainter.IndexFromX(series, c.X);
        if (i >= 0 && Vm.AddDrawingPoint(series.Bars[i].Time, c.Y)) e.Handled = true;
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm.Series is null || sender is not AvaPlot plot) return;
        var pos = e.GetPosition(plot);
        var coords = plot.Plot.GetCoordinates((float)pos.X, (float)pos.Y);
        Vm.UpdateCrosshair(ChartPainter.IndexFromX(Vm.Series, coords.X));
        if (e.GetCurrentPoint(plot).Properties.IsLeftButtonPressed) _painter.SyncX(PricePlot, Lower());   // panning keeps panes aligned
    }

    internal void Render()
    {
        if (!IsAttachedToVisualTree()) return;
        var vm = Vm;
        SetRow(1, VolumePlot, vm.ShowVolume, 0.55);
        SetRow(2, MacdPlot, vm.ShowMacd, 0.7);
        SetRow(3, RsiPlot, vm.ShowRsi, 0.55);
        SetRow(4, KdjPlot, vm.ShowKdj, 0.55);
        var palette = MacChartPalette.Current(_theme.IsDark);   // E5: every render paints with the current theme
        foreach (var p in AllPlots()) _painter.ApplyTheme(p, palette);

        if (vm.Series is null || vm.Snapshot is null || vm.Series.Bars.Count == 0)
        {
            foreach (var p in AllPlots()) { p.Plot.Clear(); p.Refresh(); }
            return;
        }

        var keep = vm.PreserveView && _userView ? PricePlot.Plot.Axes.GetLimits() : (AxisLimits?)null;
        _painter.DrawPrice(PricePlot, vm.Series, vm.Snapshot, vm);
        if (vm.ShowVolume) _painter.DrawVolume(VolumePlot, vm.Series);
        if (vm.ShowMacd) _painter.DrawMacd(MacdPlot, vm.Snapshot, vm.MacdLabel);
        if (vm.ShowRsi) _painter.DrawRsi(RsiPlot, vm.Snapshot, vm.RsiLabel);
        if (vm.ShowKdj) _painter.DrawKdj(KdjPlot, vm.Snapshot, vm.KdjLabel);

        var last = AllPlots().Last(p => p.IsVisible);
        foreach (var p in AllPlots().Where(p => p.IsVisible))
            _painter.FinishAxis(p, p == last ? LocalizationService.Current["Ch_TimeAxis"] : "", p != last);
        if (keep is { } limits)
        {
            PricePlot.Plot.Axes.SetLimitsX(limits.Left, limits.Right);
            PricePlot.Plot.Axes.SetLimitsY(limits.Bottom, limits.Top);
        }
        else
        {
            foreach (var p in AllPlots().Where(p => p.IsVisible)) p.Plot.Axes.AutoScale();
            _userView = false;
        }
        _painter.SyncX(PricePlot, Lower());
        foreach (var p in AllPlots().Where(p => p.IsVisible)) p.Refresh();
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    private void SetRow(int row, Control plot, bool visible, double star)
    {
        plot.IsVisible = visible;
        PlotGrid.RowDefinitions[row].Height = visible ? new GridLength(star, GridUnitType.Star) : new GridLength(0);
    }
}
