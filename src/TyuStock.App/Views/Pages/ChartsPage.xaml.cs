using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.WPF;
using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using Color = ScottPlot.Color;

namespace TyuStock.App.Views.Pages;

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
            ApplyAllThemes();
            await vm.ActivateAsync();
            Render();
        };
        Unloaded += (_, _) => vm.Deactivate();
        vm.PlotChanged += (_, _) => Dispatcher.Invoke(Render);
        _theme.ThemeChanged += (_, _) => Dispatcher.Invoke(() => { ApplyAllThemes(); Render(); });
    }

    private ChartsViewModel Vm => (ChartsViewModel)DataContext;

    private void WireOnce()
    {
        if (_wired) return;
        _wired = true;
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
        var (up, down, text, muted, grid, bg, dataBg) = Colors();
        foreach (var p in AllPlots()) _painter.ApplyTheme(p, _theme.IsDark, up, down, text, muted, grid, bg, dataBg);

        _painter.DrawPrice(PricePlot, vm.Series, vm.Snapshot, vm, up, down, text);
        if (showVol) _painter.DrawVolume(VolumePlot, vm.Series, up, down);
        if (showMacd) _painter.DrawMacd(MacdPlot, vm.Snapshot, up, down, text, vm.MacdLabel);
        if (showRsi) _painter.DrawRsi(RsiPlot, vm.Snapshot, muted, vm.RsiLabel);
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
        var (up, down, text, muted, grid, bg, dataBg) = Colors();
        foreach (var p in AllPlots()) _painter.ApplyTheme(p, _theme.IsDark, up, down, text, muted, grid, bg, dataBg);
    }

    private static void SetRow(RowDefinition row, UIElement plot, bool visible, double star)
    {
        plot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        row.Height = visible ? new GridLength(star, GridUnitType.Star) : new GridLength(0);
    }

    private (Color up, Color down, Color text, Color muted, Color grid, Color bg, Color dataBg) Colors()
    {
        var up = BrushColor("UpBrush", System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));
        var down = BrushColor("DownBrush", System.Windows.Media.Color.FromRgb(0x22, 0xA0, 0x6B));
        var text = BrushColor("TextFillColorPrimaryBrush", System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
        var muted = BrushColor("TextFillColorSecondaryBrush", System.Windows.Media.Color.FromRgb(0x9A, 0x9A, 0x9A));
        var bg = BrushColor("ApplicationBackgroundBrush", _theme.IsDark ? System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20) : System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3));
        var dataBg = _theme.IsDark ? Color.FromHex("#2A2A2A") : Color.FromHex("#FFFFFF");
        var grid = _theme.IsDark ? new Color(255, 255, 255, 0x33) : new Color(0, 0, 0, 0x33); // E4: ScottPlot hex is RRGGBBAA, "#33FFFFFF" was opaque cyan
        return (Sp(up), Sp(down), Sp(text), Sp(muted), grid, Sp(bg), dataBg);
    }

    private static System.Windows.Media.Color BrushColor(string key, System.Windows.Media.Color fallback)
    {
        if (Application.Current.TryFindResource(key) is SolidColorBrush b) return b.Color;
        return fallback;
    }

    private static Color Sp(System.Windows.Media.Color c) => new(c.R, c.G, c.B, c.A);
    private static string Find(string key) => Application.Current.TryFindResource(key) as string ?? key;
}