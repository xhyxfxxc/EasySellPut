using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Meili.Mac.Views.Controls;

/// <summary>Vertical underlying-price ruler: price increases upward, the thumb snaps to ticks, and drag reports the snapped price.</summary>
public sealed class PriceRuler : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<PriceRuler, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<PriceRuler, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<PriceRuler, double>(nameof(Step), 1);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<PriceRuler, double>(nameof(Value));
    public static readonly StyledProperty<IReadOnlyList<double>?> TicksProperty = AvaloniaProperty.Register<PriceRuler, IReadOnlyList<double>?>(nameof(Ticks));

    static PriceRuler()
    {
        AffectsRender<PriceRuler>(MinimumProperty, MaximumProperty, StepProperty, ValueProperty, TicksProperty);
        MinWidthProperty.OverrideDefaultValue<PriceRuler>(64);
    }

    public PriceRuler()
    {
        Width = 68;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IReadOnlyList<double>? Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    public event EventHandler<double>? ValueDragged;

    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }
        e.Pointer.Capture(this);
        _dragging = true;
        ApplyY(e.GetPosition(this).Y);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging) ApplyY(e.GetPosition(this).Y);
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            ApplyY(e.GetPosition(this).Y);
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _dragging = false;
        base.OnPointerCaptureLost(e);
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
        if (!(Range > 0) || Bounds.Height <= 0) return Bounds.Height / 2;
        double t = (price - Minimum) / Range;
        return (1 - t) * Bounds.Height;
    }

    private double YToPrice(double y)
    {
        if (!(Range > 0) || Bounds.Height <= 0) return Value;
        double t = 1 - Math.Clamp(y / Bounds.Height, 0, 1);
        return Minimum + t * Range;
    }

    public override void Render(DrawingContext context)
    {
        var textBrush = Brush("TextFillColorSecondaryBrush");
        var thumbBrush = Brush("AccentFillColorDefaultBrush");
        var trackBrush = Brush("ControlStrokeColorDefaultBrush");
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        const double trackX = 10;
        var trackPen = new Pen(trackBrush, 2);
        context.DrawLine(trackPen, new Point(trackX, 4), new Point(trackX, h - 4));
        var ticks = Ticks;
        if (ticks is { Count: > 0 } && Range > 0)
        {
            var tickPen = new Pen(trackBrush, 1);
            var typeface = new Typeface("Inter");
            foreach (var tick in ticks)
            {
                double y = PriceToY(tick);
                context.DrawLine(tickPen, new Point(trackX - 4, y), new Point(trackX + 5, y));
                var ft = new FormattedText(Format(tick), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, textBrush);
                double ty = Math.Clamp(y - ft.Height / 2, 0, Math.Max(0, h - ft.Height));
                context.DrawText(ft, new Point(trackX + 8, ty));
            }
        }
        if (Range > 0)
            context.DrawEllipse(thumbBrush, null, new Point(trackX, PriceToY(Value)), 6, 6);
    }

    private IBrush Brush(string key) => this.FindResource(key) as IBrush ?? Brushes.Gray;

    private static string Format(double v)
        => v == Math.Truncate(v) && Math.Abs(v) < 10000 ? v.ToString("0") : v.ToString("0.00");
}
