using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.WPF;
using Meili.App.Services;
using Meili.App.ViewModels;
using Color = ScottPlot.Color;

namespace Meili.App.Views.Pages;

public partial class ChartsPage : Page
{
    private readonly ChartPainter _painter = new();
    private readonly ThemeService _theme = AppServices.Get<ThemeService>();
    private bool _wired;
    private bool _userView;   // C2: the user zoomed or panned since the last fit

    public ChartsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<ChartsViewModel>();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            WireOnce();
            await vm.ActivateAsync();
            Render();
        };
        Unloaded += (_, _) => vm.Deactivate();
        vm.PlotChanged += (_, _) => Dispatcher.Invoke(Render);
        // E5: theme switch and red-up/green-up change both re-tone the brushes and raise ColorsChanged.
        ThemeService.ColorsChanged += (_, _) => Dispatcher.Invoke(Render);
    }

    private ChartsViewModel Vm => (ChartsViewModel)DataContext;

    private void WireOnce()
    {
        if (_wired) return;
        _wired = true;
        PricePlot.PreviewMouseLeftButtonUp += OnPriceClick;
        foreach (var plot in AllPlots())
        {
            plot.MouseMove += OnPlotMouseMove;
            plot.PreviewMouseWheel += (_, _) => _userView = true;
            plot.PreviewMouseDown += (_, _) => _userView = true;
            plot.Plot.RenderManager.RenderFinished += (_, _) =>
            {
                if (PricePlot.IsVisible)
                    _painter.SyncX(PricePlot, VolumePlot, MacdPlot, RsiPlot, KdjPlot);
            };
        }
    }

    // B5: while a drawing tool is active, a click on the price chart places a point (snapped to the nearest bar's time).
    private void OnPriceClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.DrawMode is null || Vm.Series is not { } series) return;
        var pos = e.GetPosition(PricePlot);
        var c = PricePlot.Plot.GetCoordinates((float)pos.X, (float)pos.Y);
        var i = ChartPainter.IndexFromX(series, c.X);
        if (i >= 0 && Vm.AddDrawingPoint(series.Bars[i].Time, c.Y)) e.Handled = true;
    }

    private IEnumerable<WpfPlot> AllPlots() => [PricePlot, VolumePlot, MacdPlot, RsiPlot, KdjPlot];


    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (Vm.Series is null || sender is not WpfPlot plot) return;
        var pos = e.GetPosition(plot);
        var coords = plot.Plot.GetCoordinates((float)pos.X, (float)pos.Y);
        var i = ChartPainter.IndexFromX(Vm.Series, coords.X);
        Vm.UpdateCrosshair(i);
    }

    private void Render()
    {
        var vm = Vm;
        var showVol = vm.ShowVolume;
        var showMacd = vm.ShowMacd;
        var showRsi = vm.ShowRsi;
        var showKdj = vm.ShowKdj;
        SetRow(VolumeRow, VolumePlot, showVol, 0.55);
        SetRow(MacdRow, MacdPlot, showMacd, 0.7);
        SetRow(RsiRow, RsiPlot, showRsi, 0.55);
        SetRow(KdjRow, KdjPlot, showKdj, 0.55);
        ApplyAllThemes();   // E5: every render paints with the current theme, empty plots included

        if (vm.Series is null || vm.Snapshot is null || vm.Series.Bars.Count == 0)
        {
            foreach (var p in AllPlots())
            {
                p.Plot.Clear();
                p.Refresh();
            }
            return;
        }

        // C2: keep the user's zoom/pan across refreshes; fit the data after a new symbol or range.
        var keep = vm.PreserveView && _userView ? PricePlot.Plot.Axes.GetLimits() : (AxisLimits?)null;

        _painter.DrawPrice(PricePlot, vm.Series, vm.Snapshot, vm);
        if (showVol) _painter.DrawVolume(VolumePlot, vm.Series);
        if (showMacd) _painter.DrawMacd(MacdPlot, vm.Snapshot, vm.MacdLabel);
        if (showRsi) _painter.DrawRsi(RsiPlot, vm.Snapshot, vm.RsiLabel);
        if (showKdj) _painter.DrawKdj(KdjPlot, vm.Snapshot, vm.KdjLabel);

        var bottomLabel = Find("Ch_TimeAxis");
        WpfPlot last = PricePlot;
        if (showVol) last = VolumePlot;
        if (showMacd) last = MacdPlot;
        if (showRsi) last = RsiPlot;
        if (showKdj) last = KdjPlot;
        foreach (var p in AllPlots())
        {
            if (!p.IsVisible) continue;
            _painter.FinishAxis(p, p == last ? bottomLabel : "", p != last);
        }
        if (keep is { } limits)
        {
            PricePlot.Plot.Axes.SetLimitsX(limits.Left, limits.Right);
            PricePlot.Plot.Axes.SetLimitsY(limits.Bottom, limits.Top);
        }
        else
        {
            foreach (var p in AllPlots()) if (p.IsVisible) p.Plot.Axes.AutoScale();
            _userView = false;
        }
        foreach (var p in AllPlots()) if (p.IsVisible) p.Refresh();
        _painter.SyncX(PricePlot, VolumePlot, MacdPlot, RsiPlot, KdjPlot);
    }

    private void ApplyAllThemes()
    {
        var palette = ChartPalette.Current(_theme.IsDark);
        foreach (var p in AllPlots()) _painter.ApplyTheme(p, palette);
    }

    private static void SetRow(RowDefinition row, UIElement plot, bool visible, double star)
    {
        plot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        row.Height = visible ? new GridLength(star, GridUnitType.Star) : new GridLength(0);
    }

    private static string Find(string key) => Application.Current.TryFindResource(key) as string ?? key;
}