using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;

namespace JGraph.Controls;

/// <summary>
/// A <c>uislider</c> as the window draws it (app-building plan, U5): a thin track with a thumb — two
/// for a range slider — and the major ticks with their labels beside it. It fills the slider's outer
/// rectangle; the track lies where MATLAB's <c>Position</c> says, 7 pixels in and 6 down. Dragging a
/// thumb reports the value on its way through <see cref="Changing"/> and, when the thumb is let go,
/// through <see cref="Changed"/>.
/// </summary>
public sealed class UiSliderFace : FrameworkElement
{
    private const double Lead = 7;
    private const double Above = 6;
    private const double Thickness = 3;
    private const double ThumbRadius = 6;

    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private static readonly Brush FillBrush = Frozen(Color.FromRgb(0x00, 0x78, 0xD7));
    private static readonly Brush ThumbBrush = Frozen(Colors.White);
    private static readonly Pen ThumbPen = FrozenPen(Color.FromRgb(0x00, 0x78, 0xD7), 1.5);
    private static readonly Pen TickPen = FrozenPen(Color.FromRgb(0x7D, 0x7D, 0x7D), 1);

    private int _dragging = -1;

    /// <summary>Raised while a thumb is dragged: the value so far, or the two of a range.</summary>
    public event Action<double[]>? Changing;

    /// <summary>Raised when a thumb is let go, a key moves it, or an accessibility client sets it.</summary>
    public event Action<double[]>? Changed;

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 100;

    /// <summary>The value; for a range, the lower one.</summary>
    public double Low { get; set; }

    /// <summary>A range slider's upper value.</summary>
    public double High { get; set; } = 100;

    public bool IsRange { get; set; }

    public bool Upright { get; set; }

    public double Step { get; set; } = 1;

    public IReadOnlyList<double> MajorTicks { get; set; } = [];

    public IReadOnlyList<double> MinorTicks { get; set; } = [];

    public IReadOnlyList<string> TickLabels { get; set; } = [];

    public Brush Foreground { get; set; } = Brushes.Black;

    public Typeface Face { get; set; } = new("Arial");

    public double FontSize { get; set; } = 12;

    public UiSliderFace()
    {
        Focusable = true;
        Cursor = Cursors.Arrow;
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private double Span => Maximum > Minimum ? Maximum - Minimum : 1;

    private double TrackLength => System.Math.Max(1, (Upright ? ActualHeight : ActualWidth) - 16);

    /// <summary>Where a value lies along the track, in the element's own coordinates.</summary>
    private double Along(double value)
    {
        double fraction = System.Math.Clamp((value - Minimum) / Span, 0, 1);
        return Upright ? 8 + ((1 - fraction) * TrackLength) : Lead + (fraction * TrackLength);
    }

    private double ValueAt(Point point)
    {
        double fraction = Upright
            ? 1 - ((point.Y - 8) / TrackLength)
            : (point.X - Lead) / TrackLength;
        return Minimum + (System.Math.Clamp(fraction, 0, 1) * Span);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);

        // The whole element takes the mouse, so a press beside the track still lands on it.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double low = Along(IsRange ? Low : Minimum);
        double high = Along(IsRange ? High : Low);
        if (Upright)
        {
            const double x = Lead;
            drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(x, 8, Thickness, TrackLength), 1.5, 1.5);
            drawingContext.DrawRoundedRectangle(FillBrush, null,
                new Rect(x, System.Math.Min(low, high), Thickness, System.Math.Abs(low - high)), 1.5, 1.5);
            for (int i = 0; i < MinorTicks.Count; i++)
            {
                double y = Along(MinorTicks[i]);
                drawingContext.DrawLine(TickPen, new Point(x + 9, y), new Point(x + 12, y));
            }

            for (int i = 0; i < MajorTicks.Count; i++)
            {
                double y = Along(MajorTicks[i]);
                drawingContext.DrawLine(TickPen, new Point(x + 9, y), new Point(x + 15, y));
                if (i < TickLabels.Count)
                {
                    FormattedText text = Label(TickLabels[i], pixelsPerDip);
                    drawingContext.DrawText(text, new Point(x + 18, y - (text.Height / 2)));
                }
            }

            DrawThumb(drawingContext, new Point(x + (Thickness / 2), Along(Low)));
            if (IsRange)
            {
                DrawThumb(drawingContext, new Point(x + (Thickness / 2), Along(High)));
            }

            return;
        }

        const double top = Above;
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(Lead, top, TrackLength, Thickness), 1.5, 1.5);
        drawingContext.DrawRoundedRectangle(FillBrush, null,
            new Rect(System.Math.Min(low, high), top, System.Math.Abs(high - low), Thickness), 1.5, 1.5);
        for (int i = 0; i < MinorTicks.Count; i++)
        {
            double x = Along(MinorTicks[i]);
            drawingContext.DrawLine(TickPen, new Point(x, top + 9), new Point(x, top + 12));
        }

        for (int i = 0; i < MajorTicks.Count; i++)
        {
            double x = Along(MajorTicks[i]);
            drawingContext.DrawLine(TickPen, new Point(x, top + 9), new Point(x, top + 15));
            if (i < TickLabels.Count)
            {
                FormattedText text = Label(TickLabels[i], pixelsPerDip);
                drawingContext.DrawText(text, new Point(x - (text.Width / 2), top + 17));
            }
        }

        DrawThumb(drawingContext, new Point(Along(Low), top + (Thickness / 2)));
        if (IsRange)
        {
            DrawThumb(drawingContext, new Point(Along(High), top + (Thickness / 2)));
        }
    }

    private FormattedText Label(string text, double pixelsPerDip) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, System.Math.Max(1, FontSize), Foreground, pixelsPerDip);

    private void DrawThumb(DrawingContext drawingContext, Point centre) =>
        drawingContext.DrawEllipse(IsEnabled ? ThumbBrush : TrackBrush, ThumbPen, centre, ThumbRadius, ThumbRadius);

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled)
        {
            return;
        }

        Focus();
        double value = ValueAt(e.GetPosition(this));

        // A press takes the nearer thumb, and the thumb goes to the press at once.
        _dragging = IsRange && System.Math.Abs(value - High) < System.Math.Abs(value - Low) ? 1 : 0;
        CaptureMouse();
        Move(value);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_dragging >= 0 && IsMouseCaptured)
        {
            Move(ValueAt(e.GetPosition(this)));
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragging < 0)
        {
            return;
        }

        _dragging = -1;
        ReleaseMouseCapture();
        Changed?.Invoke(Values());
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        double nudge = e.Key switch
        {
            Key.Right or Key.Up => 1,
            Key.Left or Key.Down => -1,
            Key.PageUp => 10,
            Key.PageDown => -10,
            _ => 0,
        };
        if (nudge == 0 || !IsEnabled || IsRange)
        {
            return;
        }

        double step = Step > 0 ? System.Math.Max(Step, Span / 100) : Span / 100;
        Low = System.Math.Clamp(Low + (nudge * step), Minimum, Maximum);
        InvalidateVisual();
        Changed?.Invoke(Values());
        e.Handled = true;
    }

    private void Move(double value)
    {
        if (_dragging == 1)
        {
            High = System.Math.Max(value, Low);
        }
        else
        {
            Low = IsRange ? System.Math.Min(value, High) : value;
        }

        InvalidateVisual();
        Changing?.Invoke(Values());
    }

    private double[] Values() => IsRange ? [Low, High] : [Low];

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double width)
    {
        var pen = new Pen(Frozen(color), width);
        pen.Freeze();
        return pen;
    }

    /// <summary>What an accessibility client sees: a slider whose value it can read and set.</summary>
    private sealed class Peer(UiSliderFace owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private UiSliderFace Face => (UiSliderFace)Owner;

        public bool IsReadOnly => !Face.IsEnabled;

        public double LargeChange => (Face.Maximum - Face.Minimum) / 10;

        public double Maximum => Face.Maximum;

        public double Minimum => Face.Minimum;

        public double SmallChange => Face.Step;

        public double Value => Face.Low;

        public void SetValue(double value)
        {
            if (!Face.IsEnabled)
            {
                throw new System.Windows.Automation.ElementNotEnabledException();
            }

            Face.Low = System.Math.Clamp(value, Face.Minimum, Face.IsRange ? Face.High : Face.Maximum);
            Face.InvalidateVisual();
            Face.Changed?.Invoke(Face.Values());
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => "UiSlider";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
