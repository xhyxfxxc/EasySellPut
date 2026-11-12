using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ScottPlot;
using Meili.Mac.Platform;

namespace Meili.Mac.Views;

public partial class OptionsPage : UserControl
{
    private readonly MacTheme _theme = AppServices.Get<MacTheme>();
    private readonly ChartPainter _themePaint = new();
    private OptionColumnSet? _builtFor;

    /// <summary>Avalonia's column header reserves room for the sort glyph, which WPF's widths don't account for.</summary>
    private const double HeaderChrome = 14;

    public OptionsPage()
    {
        InitializeComponent();
        var vm = AppServices.Get<OptionsViewModel>();
        DataContext = vm;
        EventHandler onTable = (_, _) => Dispatcher.UIThread.Post(Refresh);
        EventHandler onColors = (_, _) => Dispatcher.UIThread.Post(RenderSmile);
        AttachedToVisualTree += async (_, _) =>
        {
            vm.TableChanged += onTable;
            Ui.Platform.ColorsChanged += onColors;
            Refresh();
            await vm.ActivateAsync();
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            vm.TableChanged -= onTable;
            Ui.Platform.ColorsChanged -= onColors;
            vm.Deactivate();
        };
        // The ATM strike row is highlighted; rows are recycled, so the class follows each row's data.
        ChainList.LoadingRow += (_, e) => e.Row.Classes.Set("Atm", e.Row.DataContext is OptionStrikeRow { IsAtm: true });
    }

    private OptionsViewModel Vm => (OptionsViewModel)DataContext!;

    private void Refresh()
    {
        RebuildColumns();
        foreach (var (b, kind) in new[] { (Near5, StrikeWindowKind.Near5), (Near10, StrikeWindowKind.Near10), (Near20, StrikeWindowKind.Near20), (All, StrikeWindowKind.All) })
            b.Classes.Set("Selected", Vm.Window == kind);
        RenderSmile();
        // After the grid has laid out the new rows; scrolling before that is a no-op.
        if (Vm.AtmRow is { } row) Dispatcher.UIThread.Post(() => ChainList.ScrollIntoView(row, null), DispatcherPriority.Background);
    }

    /// <summary>H1/G4: columns come from <see cref="OptionColumns"/>; only rebuilt when the column set changes.</summary>
    private void RebuildColumns()
    {
        var set = Vm.ColumnSet;
        if (set == _builtFor) return;
        _builtFor = set;
        ChainList.Columns.Clear();
        foreach (var c in OptionColumns.Side(call: true, set)) ChainList.Columns.Add(Column(c, set, side: true));
        foreach (var c in OptionColumns.Middle(set)) ChainList.Columns.Add(Column(c, set, side: null));
        foreach (var c in OptionColumns.Side(call: false, set)) ChainList.Columns.Add(Column(c, set, side: false));
    }

    /// <summary>
    /// One chain column. Rows are immutable snapshots, so each cell is filled from its row when the (recycled) cell gets
    /// it — no per-cell bindings. Colours come from style classes resolved through DynamicResource, so a theme switch
    /// re-tones the visible cells in place.
    /// </summary>
    private static DataGridTemplateColumn Column(OptionColumnDef def, OptionColumnSet set, bool? side)
    {
        var strike = def.Key == OptionColumns.Strike.Key;
        var sideProp = side is null ? null : typeof(OptionStrikeRow).GetProperty(side.Value ? "Call" : "Put")!;
        var valueProp = (sideProp is null ? typeof(OptionStrikeRow) : typeof(EnrichedSide)).GetProperty(def.Path)!;
        var wideCheck = def.Kind == OptionCellKind.Spread && side is not null;
        var oiBar = def.Kind == OptionCellKind.OiBar && side is not null;
        var tip = def.Key == "SpreadPct" ? "Op_SpreadTip" : def.Key == OptionColumns.Distance.Key ? "Op_DistanceTip" : null;

        var template = new FuncDataTemplate<OptionStrikeRow>((_, _) =>
        {
            var text = new TextBlock
            {
                Classes = { "Num" }, FontSize = strike ? 14 : 12, FontWeight = strike ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
                TextAlignment = side is null ? TextAlignment.Center : TextAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            if (tip is not null) ToolTip.SetTip(text, LocalizationService.Current[tip]);
            var grid = new Grid();
            Rectangle? bar = null;
            if (oiBar)
            {
                bar = new Rectangle { Opacity = 0.28, Margin = new Thickness(-2, 1), RenderTransformOrigin = new RelativePoint(side == true ? 1 : 0, 0.5, RelativeUnit.Relative) };
                bar[!Shape.FillProperty] = bar.GetResourceObservable("AccentFillColorDefaultBrush").ToBinding();
                grid.Children.Add(bar);
            }
            grid.Children.Add(text);
            var cell = new Border { Classes = { "OptCell" }, Child = grid };
            cell.Classes.Set("Compact", set.Compact);
            cell.DataContextChanged += (_, _) =>
            {
                if (cell.DataContext is not OptionStrikeRow row) return;
                var owner = sideProp is null ? row : sideProp.GetValue(row);
                text.Text = owner is null ? "" : valueProp.GetValue(owner)?.ToString();
                cell.Classes.Set("Itm", side == true ? row.CallItm : side == false && row.PutItm);
                text.Classes.Set("Wide", wideCheck && owner is EnrichedSide { IsWideSpread: true });
                if (bar is not null) bar.RenderTransform = new ScaleTransform(owner is EnrichedSide s ? s.OiRatio : 0, 1);
            };
            return cell;
        }, supportsRecycling: true);

        var column = new DataGridTemplateColumn { CellTemplate = template, Width = new DataGridLength(OptionColumns.WidthOf(def, set) + HeaderChrome) };
        Loc.SetHeader(column, def.HeaderKey);
        return column;
    }

    /// <summary>B6: ATM IV by days to expiry (same plot as the smile, switched by the term-structure box).</summary>
    private void RenderTerm(Plot plot, ChartPalette c)
    {
        var loc = LocalizationService.Current;
        var pts = Vm.TermPoints;
        if (pts.Count > 0)
        {
            var s = plot.Add.Scatter(pts.Select(p => (double)p.DaysToExpiry).ToArray(), pts.Select(p => p.Iv * 100).ToArray());
            s.MarkerSize = 6;
            s.LineWidth = 1.6f;
            s.Color = c.Series1;
            s.LegendText = loc["Op_TermLegend"];
            plot.Legend.IsVisible = true;
            plot.Legend.Alignment = Alignment.UpperRight;
        }
        plot.Axes.Left.Label.Text = loc["Op_IV"] + " %";
        plot.Axes.Bottom.Label.Text = loc["Op_TermAxis"];
        plot.Title(loc["Op_TermTitle"]);
        plot.Axes.AutoScale();
        SmilePlot.Refresh();
    }

    private void RenderSmile()
    {
        if (!Vm.ShowSmile) return;
        var loc = LocalizationService.Current;
        var plot = SmilePlot.Plot;
        plot.Clear();
        var c = MacChartPalette.Current(_theme.IsDark);
        _themePaint.ApplyTheme(SmilePlot, c);
        if (Vm.ShowTerm)
        {
            RenderTerm(plot, c);
            return;
        }
        var pts = Vm.SmilePoints;
        if (pts.Count == 0)
        {
            SmilePlot.Refresh();
            return;
        }
        foreach (var (isCall, color, key) in new[] { (true, c.Series1, "Op_Calls"), (false, c.Series2, "Op_Puts") })
        {
            var side = pts.Where(p => p.IsCall == isCall).OrderBy(p => p.Strike).ToList();
            if (side.Count == 0) continue;
            var s = plot.Add.Scatter(side.Select(p => p.Strike).ToArray(), side.Select(p => p.Iv * 100).ToArray());
            s.LegendText = loc[key];
            s.MarkerSize = 5;
            s.LineWidth = 1.4f;
            s.Color = color;
        }
        if (Vm.Spot is { } spot)
        {
            var v = plot.Add.VerticalLine(spot);
            v.LineColor = c.Muted;
            v.LineWidth = 1;
            v.LinePattern = LinePattern.Dashed;
        }
        plot.Axes.Left.Label.Text = loc["Op_IV"] + " %";
        plot.Axes.Bottom.Label.Text = loc["Op_Strike"];
        plot.Title(loc["Op_Smile"]);
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Axes.AutoScale();   // E4: limits from an earlier render would otherwise stick
        SmilePlot.Refresh();
    }
}
