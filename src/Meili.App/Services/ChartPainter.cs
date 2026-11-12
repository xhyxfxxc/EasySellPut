using System.Windows.Media;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using Meili.Analytics;
using Meili.App.ViewModels;
using Meili.Core.Models;
using BarSeries = Meili.Core.Models.BarSeries;
using Meili.Core.Time;
using Meili.Core.Settings;
using Color = ScottPlot.Color;

namespace Meili.App.Services;

/// <summary>Paints a <see cref="KlineSnapshot"/> onto stacked ScottPlot controls and keeps their X axes in sync.</summary>
public sealed class ChartPainter
{
    private const float FontSize = 12f;
    private const string FontName = "Microsoft YaHei UI";
    private bool _syncing;

    /// <summary>E5: colours of the last <see cref="ApplyTheme"/>; the Draw methods paint with them.</summary>
    public ChartPalette Palette { get; private set; } = null!;

    public void ApplyTheme(WpfPlot plot, ChartPalette palette)
    {
        Palette = palette;
        var (text, grid, bg, dataBg) = (palette.Text, palette.Grid, palette.Background, palette.DataBackground);
        var p = plot.Plot;
        p.FigureBackground.Color = bg;
        p.DataBackground.Color = dataBg;
        p.Axes.Color(text);
        p.Axes.Bottom.TickLabelStyle.FontName = FontName;
        p.Axes.Bottom.TickLabelStyle.FontSize = FontSize;
        p.Axes.Left.TickLabelStyle.FontName = FontName;
        p.Axes.Left.TickLabelStyle.FontSize = FontSize;
        p.Axes.Bottom.Label.FontName = FontName;
        p.Axes.Bottom.Label.FontSize = FontSize;
        p.Axes.Left.Label.FontName = FontName;
        p.Axes.Left.Label.FontSize = FontSize;
        p.Axes.Title.Label.FontName = FontName; // E4: the default title font has no CJK glyphs ("IV 微笑" drew as boxes)
        p.Axes.Title.Label.FontSize = FontSize + 1;
        p.Legend.FontName = FontName;
        p.Legend.FontSize = FontSize;
        p.Legend.BackgroundColor = dataBg;
        p.Legend.FontColor = text;
        p.Legend.OutlineColor = grid;
        p.Grid.MajorLineColor = grid;
        p.Grid.MinorLineColor = grid.WithAlpha(40);
        plot.Menu?.Clear();
    }

    public void DrawPrice(WpfPlot plot, BarSeries series, KlineSnapshot snap, ChartsViewModel vm)
    {
        var (up, down, text) = (Palette.Up, Palette.Down, Palette.Text);
        var p = plot.Plot;
        p.Clear();
        var span = SpanOf(vm.Range);
        var plan = PricePlan.Build(series.Bars);
        if (plan.LineOnly)
        {
            // Every bar is close-only (e.g. an intraday tick series): a price line, not fake flat candles.
            var line = p.Add.Scatter(plan.CloseX, plan.CloseY);
            line.Color = text.WithAlpha(220);
            line.LineWidth = 1.5f;
            line.MarkerSize = 0;
        }
        else
        {
            if (plan.Candles.Count > 0)
            {
                var ohlcs = plan.Candles.Select(b => new OHLC(b.Open, b.High, b.Low, b.Close, MarketClock.ToEastern(b.Time), span)).ToList();
                var candles = p.Add.Candlestick(ohlcs);
                candles.Sequential = false;
                candles.RisingColor = up;
                candles.FallingColor = down;
            }
            if (plan.CloseX.Count > 0)
            {
                // Incomplete bars: only the close is trustworthy — draw it as a hollow-looking point.
                var dots = p.Add.Scatter(plan.CloseX, plan.CloseY);
                dots.LineWidth = 0;
                dots.MarkerSize = 5;
                dots.MarkerShape = MarkerShape.OpenCircle;
                dots.Color = text.WithAlpha(200);
            }
        }
        var time = snap.Columns.Time;
        if (vm.ShowMa) AddLine(p, time, snap.Sma, Palette.Series1, vm.MaLabel);
        if (vm.ShowEma) AddLine(p, time, snap.Ema, Palette.Series2, vm.EmaLabel);
        if (vm.ShowBoll)
        {
            var boll = Palette.Series3;
            AddLine(p, time, snap.Bollinger.Middle, boll, vm.BollLabel);
            AddLine(p, time, snap.Bollinger.Upper, boll.WithAlpha(160), null);
            AddLine(p, time, snap.Bollinger.Lower, boll.WithAlpha(160), null);
        }
        // B5: rebased comparison symbol and the user's drawings.
        if (vm.CompareLine is { } cmp)
        {
            var line = p.Add.Scatter(cmp.Time.Select(ToX).ToArray(), cmp.Close);
            line.Color = Palette.Series4;
            line.LineWidth = 1.6f;
            line.MarkerSize = 0;
            line.LegendText = vm.CompareLabel;
        }
        var ink = Palette.Series5;
        foreach (var d in vm.Drawings)
        {
            if (d.Kind == DrawingKind.HorizontalLine)
            {
                var h = p.Add.HorizontalLine(d.Price1);
                h.LineColor = ink;
                h.LineWidth = 1.2f;
                h.LabelText = NumberDisplay.Price(d.Price1);
            }
            else if (d.Time1 is { } t1 && d.Time2 is { } t2)
            {
                var l = p.Add.Line(ToX(t1), d.Price1, ToX(t2), d.Price2);
                l.LineColor = ink;
                l.LineWidth = 1.4f;
            }
        }
        p.Axes.DateTimeTicksBottom();
        p.Axes.Left.Label.Text = Label(vm, "Ch_Price");
        p.Axes.Bottom.Label.Text = "";
        p.Legend.IsVisible = vm.ShowMa || vm.ShowEma || vm.ShowBoll || vm.CompareLine is not null;
        p.Legend.Alignment = Alignment.UpperLeft;
        p.Title(string.Empty);
        plot.Refresh();
    }

    public void DrawVolume(WpfPlot plot, BarSeries series)
    {
        var (up, down) = (Palette.Up, Palette.Down);
        var p = plot.Plot;
        p.Clear();
        var bars = new List<ScottPlot.Bar>(series.Bars.Count);
        double? prevClose = null;
        foreach (var b in series.Bars)
        {
            var x = MarketClock.ToEastern(b.Time).ToOADate();
            var rising = double.IsFinite(b.Open) ? b.Close >= b.Open : prevClose is not double pc || b.Close >= pc;
            prevClose = b.Close;
            bars.Add(new ScottPlot.Bar
            {
                Position = x,
                Value = b.Volume,
                FillColor = (rising ? up : down).WithAlpha(180),
                LineColor = (rising ? up : down).WithAlpha(0),
                Size = Math.Max(spanFraction(series) * 0.7, 0.0003),
            });
        }
        p.Add.Bars(bars);
        p.Axes.DateTimeTicksBottom();
        p.Axes.Bottom.Label.Text = "";
        plot.Refresh();
    }

    public void DrawMacd(WpfPlot plot, KlineSnapshot snap, string title)
    {
        var (up, down, text) = (Palette.Up, Palette.Down, Palette.Text);
        var p = plot.Plot;
        p.Clear();
        var time = snap.Columns.Time;
        AddLine(p, time, snap.Macd.Macd, Palette.Series1, "DIF");
        AddLine(p, time, snap.Macd.Signal, Palette.Series2, "DEA");
        var histBars = new List<ScottPlot.Bar>();
        for (int i = 0; i < snap.Macd.Histogram.Length; i++)
        {
            var v = snap.Macd.Histogram[i];
            if (!double.IsFinite(v)) continue;
            histBars.Add(new ScottPlot.Bar
            {
                Position = ToX(time[i]),
                Value = v,
                FillColor = (v >= 0 ? up : down).WithAlpha(180),
                LineColor = new Color(0,0,0,0),
                Size = Math.Max(spanFraction(time) * 0.7, 0.0003),
            });
        }
        if (histBars.Count > 0) p.Add.Bars(histBars);
        p.Axes.DateTimeTicksBottom();
        p.Axes.Left.Label.Text = title;
        p.Legend.IsVisible = true;
        p.Legend.Alignment = Alignment.UpperLeft;
        p.Axes.Bottom.Label.Text = "";
        plot.Refresh();
    }

    public void DrawRsi(WpfPlot plot, KlineSnapshot snap, string title)
    {
        var muted = Palette.Muted;
        var p = plot.Plot;
        p.Clear();
        AddLine(p, snap.Columns.Time, snap.Rsi, Palette.Series2, title);
        var h = p.Add.HorizontalLine(70, color: muted, width: 1);
        h.LinePattern = LinePattern.Dashed;
        var l = p.Add.HorizontalLine(30, color: muted, width: 1);
        l.LinePattern = LinePattern.Dashed;
        p.Axes.DateTimeTicksBottom();
        p.Axes.Left.Label.Text = title;
        p.Axes.Left.Min = 0;
        p.Axes.Left.Max = 100;
        p.Axes.Bottom.Label.Text = "";
        p.Legend.IsVisible = false;
        plot.Refresh();
    }

    public void DrawKdj(WpfPlot plot, KlineSnapshot snap, string title)
    {
        var p = plot.Plot;
        p.Clear();
        var time = snap.Columns.Time;
        AddLine(p, time, snap.Kdj.K, Palette.Series1, "K");
        AddLine(p, time, snap.Kdj.D, Palette.Series2, "D");
        AddLine(p, time, snap.Kdj.J, Palette.Series6, "J");
        p.Axes.DateTimeTicksBottom();
        p.Axes.Left.Label.Text = title;
        p.Legend.IsVisible = true;
        p.Legend.Alignment = Alignment.UpperLeft;
        p.Axes.Bottom.Label.Text = "";
        plot.Refresh();
    }

    public void FinishAxis(WpfPlot plot, string? bottomLabel, bool hideBottomTicks)
    {
        var p = plot.Plot;
        p.Axes.Bottom.Label.Text = bottomLabel ?? "";
        p.Axes.Bottom.TickLabelStyle.IsVisible = !hideBottomTicks;
        plot.Refresh();
    }

    public void SyncX(WpfPlot source, params WpfPlot[] others)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var min = source.Plot.Axes.Bottom.Min;
            var max = source.Plot.Axes.Bottom.Max;
            foreach (var o in others)
            {
                if (!o.IsVisible) continue;
                if (Math.Abs(o.Plot.Axes.Bottom.Min - min) < 1e-9 && Math.Abs(o.Plot.Axes.Bottom.Max - max) < 1e-9) continue;
                o.Plot.Axes.Bottom.Min = min;
                o.Plot.Axes.Bottom.Max = max;
                o.Refresh();
            }
        }
        finally { _syncing = false; }
    }

    public static int IndexFromX(BarSeries series, double oaDate)
    {
        var bars = series.Bars;
        if (bars.Count == 0) return -1;
        // Binary-search nearest Eastern OADate.
        int lo = 0, hi = bars.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (ToX(bars[mid].Time) < oaDate) lo = mid + 1;
            else hi = mid;
        }
        int i = lo;
        if (i > 0 && Math.Abs(ToX(bars[i - 1].Time) - oaDate) < Math.Abs(ToX(bars[i].Time) - oaDate)) i--;
        return i;
    }

    public static double ToX(DateTimeOffset t) => MarketClock.ToEastern(t).ToOADate();

    private static void AddLine(Plot plot, DateTimeOffset[] time, double[] values, Color color, string? legend)
    {
        var xs = new List<double>(values.Length);
        var ys = new List<double>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            xs.Add(ToX(time[i]));
            ys.Add(values[i]);
        }
        if (xs.Count == 0) return;
        var sig = plot.Add.Scatter(xs, ys);
        sig.Color = color;
        sig.LineWidth = 1.5f;
        sig.MarkerSize = 0;
        sig.LegendText = legend ?? "";
        sig.IsVisible = true;
        if (string.IsNullOrEmpty(legend))
        {
            // keep in plot, hide from legend if API allows; empty legend text usually hides it
        }
    }

    private static string Label(ChartsViewModel vm, string key)
    {
        // Localization is applied through resources; the VM already uses the same keys for chips.
        // We read from Application resources so the plot labels follow the UI language.
        return System.Windows.Application.Current.TryFindResource(key) as string ?? key;
    }

    private static TimeSpan SpanOf(ChartRangeKind range) => range switch
    {
        ChartRangeKind.Session or ChartRangeKind.Minute1 => TimeSpan.FromMinutes(1),
        ChartRangeKind.Minute5 => TimeSpan.FromMinutes(5),
        ChartRangeKind.Minute15 => TimeSpan.FromMinutes(15),
        ChartRangeKind.Hour1 => TimeSpan.FromHours(1),
        ChartRangeKind.Week1 => TimeSpan.FromDays(7),
        ChartRangeKind.Month1 => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(1),
    };

    private static double spanFraction(BarSeries series)
    {
        if (series.Bars.Count < 2) return 0.8;
        return Math.Abs(ToX(series.Bars[1].Time) - ToX(series.Bars[0].Time));
    }

    private static double spanFraction(DateTimeOffset[] time)
    {
        if (time.Length < 2) return 0.8;
        return Math.Abs(ToX(time[1]) - ToX(time[0]));
    }
}

/// <summary>Splits bars into drawable candles and close-only points (F2). Pure, so it is unit-tested without a plot.</summary>
public sealed class PricePlan
{
    public List<Meili.Core.Models.Bar> Candles { get; } = [];
    public List<double> CloseX { get; } = [];
    public List<double> CloseY { get; } = [];
    public bool LineOnly { get; private set; }

    public static PricePlan Build(IReadOnlyList<Meili.Core.Models.Bar> bars)
    {
        var plan = new PricePlan();
        foreach (var b in bars)
        {
            // Re-check consistency even if the provider did: ScottPlot throws on Low > Open/Close.
            if (!b.Incomplete && b.IsConsistent()) plan.Candles.Add(b);
            else { plan.CloseX.Add(MarketClock.ToEastern(b.Time).ToOADate()); plan.CloseY.Add(b.Close); }
        }
        plan.LineOnly = plan.Candles.Count == 0 && plan.CloseX.Count > 0;
        return plan;
    }
}
