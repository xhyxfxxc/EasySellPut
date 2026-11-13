using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Meili.App.Views.Controls;

/// <summary>Vertical underlying-price ruler: price increases upward, the thumb snaps to ticks, and drag reports the snapped price.</summary>
public sealed class PriceRuler : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(PriceRuler), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(PriceRuler), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(PriceRuler), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(PriceRuler), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TicksProperty = DependencyProperty.Register(nameof(Ticks), typeof(IReadOnlyList<double>), typeof(PriceRuler), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IReadOnlyList<double>? Ticks { get => (IReadOnlyList<double>?)GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    public event EventHandler<double>? ValueDragged;

    private bool _dragging;

    public PriceRuler()
    {
        MinWidth = 64;
        Width = 68;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;
        Cursor = Cursors.SizeNS;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        _dragging = true;
        ApplyY(e.GetPosition(this).Y);
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) ApplyY(e.GetPosition(this).Y);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            ApplyY(e.GetPosition(this).Y);
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    private void ApplyY(double y)
    {
        double price = YToPrice(y);
        if (Math.Abs(price - Value) < 1e-12) return;
        Value = price;
        ValueDragged?.Invoke(this, price);
    }

    private double Range => Maximum - Minimum;

    private double PriceToY(double price)
    {
        if (!(Range > 0) || ActualHeight <= 0) return ActualHeight / 2;
        double t = (price - Minimum) / Range;
        return (1 - t) * ActualHeight;
    }

    private double YToPrice(double y)
    {
        if (!(Range > 0) || ActualHeight <= 0) return Value;
        double t = 1 - Math.Clamp(y / ActualHeight, 0, 1);
        return Minimum + t * Range;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var textBrush = Brush("TextFillColorSecondaryBrush");
        var thumbBrush = Brush("AccentFillColorDefaultBrush");
        var trackBrush = Brush("ControlStrokeColorDefaultBrush");
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double trackX = 10;
        dc.DrawLine(new Pen(trackBrush, 2), new Point(trackX, 4), new Point(trackX, h - 4));

        var ticks = Ticks;
        if (ticks is { Count: > 0 } && Range > 0)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            foreach (var tick in ticks)
            {
                double y = PriceToY(tick);
                dc.DrawLine(new Pen(trackBrush, 1), new Point(trackX - 4, y), new Point(trackX + 5, y));
                var ft = new FormattedText(
                    Format(tick), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 12, textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double ty = Math.Clamp(y - ft.Height / 2, 0, Math.Max(0, h - ft.Height));
                dc.DrawText(ft, new Point(trackX + 8, ty));
            }
        }

        if (Range > 0)
        {
            double y = PriceToY(Value);
            dc.DrawEllipse(thumbBrush, null, new Point(trackX, y), 6, 6);
        }
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.Gray;

    private string Format(double v)
        => v == Math.Truncate(v) && Math.Abs(v) < 10000 ? v.ToString("0") : v.ToString("0.00");
}
