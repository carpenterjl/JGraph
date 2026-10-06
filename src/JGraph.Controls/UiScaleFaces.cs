using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Core.Model;

namespace JGraph.Controls;

/// <summary>
/// What the drawn faces of U9 share (app-building plan): a font, a foreground, the rectangle the
/// component's own <c>Position</c> is inside the element (the rest is its labels), and a frozen
/// palette. Each face fills its component's outer rectangle and draws the component in the inner one.
/// </summary>
public abstract class UiDrawnFace : FrameworkElement
{
    /// <summary>R2025b's control grey, 7D7D7D, for ticks and rims.</summary>
    protected static readonly Pen RimPen = FrozenPen(Color.FromRgb(0x7D, 0x7D, 0x7D), 1);

    protected static readonly Pen TickPen = FrozenPen(Color.FromRgb(0x7D, 0x7D, 0x7D), 1);

    protected static readonly Brush DialBrush = Frozen(Color.FromRgb(0xF0, 0xF0, 0xF0));

    protected static readonly Brush AccentBrush = Frozen(Color.FromRgb(0x00, 0x78, 0xD7));

    protected static readonly Brush NeedleBrush = Frozen(Color.FromRgb(0xC8, 0x36, 0x36));

    protected static readonly Brush TrackBrush = Frozen(Color.FromRgb(0xC8, 0xC8, 0xC8));

    /// <summary>How far the component's own rectangle stands in from the element's edges.</summary>
    public Thickness Inset { get; set; }

    public Brush Foreground { get; set; } = Brushes.Black;

    public Typeface Face { get; set; } = new("Arial");

    public double FontSize { get; set; } = 12;

    /// <summary>The component's own rectangle, in the element's coordinates.</summary>
    protected Rect Inner => new(
        Inset.Left, Inset.Top,
        System.Math.Max(0, ActualWidth - Inset.Left - Inset.Right),
        System.Math.Max(0, ActualHeight - Inset.Top - Inset.Bottom));

    protected static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    protected FormattedText Label(string text, double pixelsPerDip) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, System.Math.Max(1, FontSize), Foreground, pixelsPerDip);

    /// <summary>A label drawn with its centre at a point.</summary>
    protected void DrawCentred(DrawingContext context, string text, Point centre, double pixelsPerDip)
    {
        FormattedText label = Label(text, pixelsPerDip);
        context.DrawText(label, new Point(centre.X - (label.Width / 2), centre.Y - (label.Height / 2)));
    }

    /// <summary>A point on a circle: <paramref name="degrees"/> clockwise from twelve o'clock.</summary>
    protected static Point OnCircle(Point centre, double radius, double degrees)
    {
        double radians = degrees * System.Math.PI / 180;
        return new Point(centre.X + (radius * System.Math.Sin(radians)), centre.Y - (radius * System.Math.Cos(radians)));
    }

    /// <summary>An arc from one bearing to another, clockwise, as a geometry to stroke.</summary>
    protected static PathGeometry Arc(Point centre, double radius, double fromDegrees, double toDegrees)
    {
        var figure = new PathFigure { StartPoint = OnCircle(centre, radius, fromDegrees), IsClosed = false };
        double sweep = toDegrees - fromDegrees;
        figure.Segments.Add(new ArcSegment(
            OnCircle(centre, radius, toDegrees), new Size(radius, radius), 0, System.Math.Abs(sweep) > 180,
            sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

/// <summary>
/// A <c>uiknob</c> as the window draws it (U9): a dial with a pointer, turned through 270 degrees
/// from seven o'clock to five o'clock, its ticks and labels round the outside. Dragging reports the
/// value on its way through <see cref="Changing"/> and, when the mouse is let go, through
/// <see cref="Changed"/>; the arrow keys step it.
/// </summary>
public sealed class UiKnobFace : UiDrawnFace
{
    private const double Sweep = 270;
    private const double Start = -135;
    private bool _dragging;

    public event Action<double>? Changing;

    public event Action<double>? Changed;

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 100;

    public double Value { get; set; }

    public IReadOnlyList<double> MajorTicks { get; set; } = [];

    public IReadOnlyList<double> MinorTicks { get; set; } = [];

    public IReadOnlyList<string> TickLabels { get; set; } = [];

    public UiKnobFace()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private double Span => Maximum > Minimum ? Maximum - Minimum : 1;

    private double AngleOf(double value) => Start + (Sweep * System.Math.Clamp((value - Minimum) / Span, 0, 1));

    private Point Centre => new(Inner.X + (Inner.Width / 2), Inner.Y + (Inner.Height / 2));

    private double Radius => System.Math.Max(2, System.Math.Min(Inner.Width, Inner.Height) / 2);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Point centre = Centre;
        double radius = Radius;
        drawingContext.DrawEllipse(IsEnabled ? DialBrush : TrackBrush, RimPen, centre, radius, radius);

        double ring = radius + UiKnobModel.TickGap;
        foreach (double tick in MinorTicks)
        {
            double angle = AngleOf(tick);
            drawingContext.DrawLine(TickPen, OnCircle(centre, ring, angle), OnCircle(centre, ring + 3, angle));
        }

        double labelRadius = ring + UiKnobModel.MajorTickLength + UiKnobModel.TickGap + (FontSize * 0.45);
        for (int i = 0; i < MajorTicks.Count; i++)
        {
            double angle = AngleOf(MajorTicks[i]);
            drawingContext.DrawLine(TickPen, OnCircle(centre, ring, angle), OnCircle(centre, ring + UiKnobModel.MajorTickLength, angle));
            if (i < TickLabels.Count)
            {
                FormattedText label = Label(TickLabels[i], pixelsPerDip);
                Point at = OnCircle(centre, labelRadius + (label.Width / 2 * System.Math.Abs(System.Math.Sin(angle * System.Math.PI / 180))), angle);
                drawingContext.DrawText(label, new Point(at.X - (label.Width / 2), at.Y - (label.Height / 2)));
            }
        }

        // The pointer: a line from near the centre to near the rim, with a dot where the hand rests.
        double pointer = AngleOf(Value);
        var pen = new Pen(IsEnabled ? AccentBrush : TrackBrush, System.Math.Max(2, radius / 10)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawLine(pen, OnCircle(centre, radius * 0.15, pointer), OnCircle(centre, radius * 0.8, pointer));
    }

    private double ValueAt(Point point)
    {
        Point centre = Centre;
        double degrees = System.Math.Atan2(point.X - centre.X, centre.Y - point.Y) * 180 / System.Math.PI;
        if (degrees < Start)
        {
            degrees = degrees < -180 + (Sweep / 2 - 45) ? Start + Sweep : Start;
        }

        return Minimum + (System.Math.Clamp((degrees - Start) / Sweep, 0, 1) * Span);
    }

    private void Move(double value, bool settle)
    {
        Value = System.Math.Clamp(value, Minimum, Maximum);
        InvalidateVisual();
        if (settle)
        {
            Changed?.Invoke(Value);
        }
        else
        {
            Changing?.Invoke(Value);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled)
        {
            return;
        }

        Focus();
        _dragging = true;
        CaptureMouse();
        Move(ValueAt(e.GetPosition(this)), settle: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_dragging && IsMouseCaptured)
        {
            Move(ValueAt(e.GetPosition(this)), settle: false);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            Move(ValueAt(e.GetPosition(this)), settle: true);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        double step = Span / 100;
        double? moved = e.Key switch
        {
            Key.Right or Key.Up => Value + step,
            Key.Left or Key.Down => Value - step,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (moved is { } to && IsEnabled)
        {
            Move(to, settle: true);
            e.Handled = true;
        }
    }

    private sealed class Peer(UiKnobFace owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private UiKnobFace Knob => (UiKnobFace)Owner;

        public bool IsReadOnly => !Knob.IsEnabled;

        public double LargeChange => Knob.Span / 10;

        public double Maximum => Knob.Maximum;

        public double Minimum => Knob.Minimum;

        public double SmallChange => Knob.Span / 100;

        public double Value => Knob.Value;

        public void SetValue(double value)
        {
            if (!Knob.IsEnabled)
            {
                throw new System.Windows.Automation.ElementNotEnabledException();
            }

            Knob.Move(value, settle: true);
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => "UiKnob";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}

/// <summary>
/// A <c>uiknob(…, 'discrete')</c> as the window draws it (U9): a dial with a position for each
/// item, the pointer at the selected one. A click or a drag takes the nearest position and reports
/// its index through <see cref="Changed"/>.
/// </summary>
public sealed class UiDiscreteKnobFace : UiDrawnFace
{
    private const double Sweep = 270;
    private const double Start = -135;

    public event Action<int>? Changed;

    public IReadOnlyList<string> Items { get; set; } = [];

    public int Selected { get; set; }

    public UiDiscreteKnobFace()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private double AngleOf(int index) => Items.Count <= 1 ? Start : Start + (Sweep * index / (Items.Count - 1));

    private Point Centre => new(Inner.X + (Inner.Width / 2), Inner.Y + (Inner.Height / 2));

    private double Radius => System.Math.Max(2, System.Math.Min(Inner.Width, Inner.Height) / 2);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Point centre = Centre;
        double radius = Radius;
        drawingContext.DrawEllipse(IsEnabled ? DialBrush : TrackBrush, RimPen, centre, radius, radius);
        double ring = radius + UiKnobModel.TickGap;
        double labelRadius = ring + UiKnobModel.MajorTickLength + UiKnobModel.TickGap + (FontSize * 0.45);
        for (int i = 0; i < Items.Count; i++)
        {
            double angle = AngleOf(i);
            drawingContext.DrawLine(TickPen, OnCircle(centre, ring, angle), OnCircle(centre, ring + UiKnobModel.MajorTickLength, angle));
            FormattedText label = Label(Items[i], pixelsPerDip);
            Point at = OnCircle(centre, labelRadius + (label.Width / 2 * System.Math.Abs(System.Math.Sin(angle * System.Math.PI / 180))), angle);
            drawingContext.DrawText(label, new Point(at.X - (label.Width / 2), at.Y - (label.Height / 2)));
        }

        double pointer = AngleOf(System.Math.Clamp(Selected, 0, System.Math.Max(0, Items.Count - 1)));
        var pen = new Pen(IsEnabled ? AccentBrush : TrackBrush, System.Math.Max(2, radius / 10)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawLine(pen, OnCircle(centre, radius * 0.15, pointer), OnCircle(centre, radius * 0.8, pointer));
    }

    private int NearestAt(Point point)
    {
        if (Items.Count == 0)
        {
            return 0;
        }

        Point centre = Centre;
        double degrees = System.Math.Atan2(point.X - centre.X, centre.Y - point.Y) * 180 / System.Math.PI;
        if (degrees < Start)
        {
            degrees = degrees < -180 + (Sweep / 2 - 45) ? Start + Sweep : Start;
        }

        return (int)System.Math.Round(System.Math.Clamp((degrees - Start) / Sweep, 0, 1) * (Items.Count - 1));
    }

    private void Pick(int index)
    {
        if (Items.Count == 0)
        {
            return;
        }

        Selected = System.Math.Clamp(index, 0, Items.Count - 1);
        InvalidateVisual();
        Changed?.Invoke(Selected);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (IsEnabled)
        {
            Focus();
            Pick(NearestAt(e.GetPosition(this)));
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        int? moved = e.Key switch
        {
            Key.Right or Key.Up => Selected + 1,
            Key.Left or Key.Down => Selected - 1,
            _ => null,
        };
        if (moved is { } to && IsEnabled)
        {
            Pick(to);
            e.Handled = true;
        }
    }

    private sealed class Peer(UiDiscreteKnobFace owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        private UiDiscreteKnobFace Dial => (UiDiscreteKnobFace)Owner;

        public bool IsReadOnly => !Dial.IsEnabled;

        public string Value => Dial.Selected >= 0 && Dial.Selected < Dial.Items.Count ? Dial.Items[Dial.Selected] : string.Empty;

        public void SetValue(string value)
        {
            int at = Dial.Items.ToList().IndexOf(value);
            if (at >= 0)
            {
                Dial.Pick(at);
            }
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Spinner;

        protected override string GetClassNameCore() => "UiDiscreteKnob";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}

/// <summary>
/// A <c>uigauge</c> as the window draws it (U9), in any of its four shapes: a scale with ticks and
/// labels, bands of colour along it, and a needle at the value. It takes no input.
/// </summary>
public sealed class UiGaugeFace : UiDrawnFace
{
    public UiGaugeStyle GaugeStyle { get; set; }

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 100;

    public double Value { get; set; }

    public IReadOnlyList<double> MajorTicks { get; set; } = [];

    public IReadOnlyList<double> MinorTicks { get; set; } = [];

    public IReadOnlyList<string> TickLabels { get; set; } = [];

    public IReadOnlyList<UiColor> Colors { get; set; } = [];

    public IReadOnlyList<double> ColorLimits { get; set; } = [];

    public bool Clockwise { get; set; } = true;

    public string Orientation { get; set; } = string.Empty;

    public Brush? Background { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private double Span => Maximum > Minimum ? Maximum - Minimum : 1;

    private double Fraction(double value) => System.Math.Clamp((value - Minimum) / Span, 0, 1);

    /// <summary>The arc a round gauge's scale lies on: its centre, radius, and the bearings of its two ends.</summary>
    private (Point Centre, double Radius, double From, double To) Dial()
    {
        Rect box = Inner;
        switch (GaugeStyle)
        {
            case UiGaugeStyle.Semicircular:
            {
                double radius = System.Math.Max(2, System.Math.Min(box.Width / 2, box.Height) - (FontSize * 1.3));
                (Point centre, double from, double to) = Orientation switch
                {
                    "south" => (new Point(box.X + (box.Width / 2), box.Y + 2), 90.0, 270.0),
                    "east" => (new Point(box.X + 2, box.Y + (box.Height / 2)), 0.0, 180.0),
                    "west" => (new Point(box.Right - 2, box.Y + (box.Height / 2)), 180.0, 360.0),
                    _ => (new Point(box.X + (box.Width / 2), box.Bottom - 2), 270.0, 450.0),
                };
                if (Orientation is "east" or "west")
                {
                    radius = System.Math.Max(2, System.Math.Min(box.Width, box.Height / 2) - (FontSize * 1.3));
                }

                return (centre, radius, from, to);
            }

            case UiGaugeStyle.NinetyDegree:
            {
                double radius = System.Math.Max(2, System.Math.Min(box.Width, box.Height) - (FontSize * 1.5));
                return Orientation switch
                {
                    "northeast" => (new Point(box.X + 2, box.Bottom - 2), radius, 0.0, 90.0),
                    "southwest" => (new Point(box.Right - 2, box.Y + 2), radius, 180.0, 270.0),
                    "southeast" => (new Point(box.X + 2, box.Y + 2), radius, 90.0, 180.0),
                    _ => (new Point(box.Right - 2, box.Bottom - 2), radius, 270.0, 360.0),
                };
            }

            default:
            {
                double radius = System.Math.Max(2, (System.Math.Min(box.Width, box.Height) / 2) - 1);
                return (new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2)), radius, -135.0, 135.0);
            }
        }
    }

    private double BearingOf(double value, double from, double to)
    {
        double fraction = Fraction(value);
        return Clockwise ? from + ((to - from) * fraction) : to - ((to - from) * fraction);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);
        Rect box = Inner;
        drawingContext.DrawRectangle(Background ?? Brushes.Transparent, null, box);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (GaugeStyle == UiGaugeStyle.Linear)
        {
            RenderLinear(drawingContext, box, pixelsPerDip);
            return;
        }

        (Point centre, double radius, double from, double to) = Dial();
        double rim = radius - (FontSize * 1.3) - 8;
        if (GaugeStyle == UiGaugeStyle.Circular)
        {
            rim = radius - (FontSize * 1.3) - 8;
        }

        if (rim < 4)
        {
            rim = System.Math.Max(2, radius * 0.6);
        }

        // The bands, as thick arcs just inside the rim.
        for (int i = 0; i + 1 < ColorLimits.Count && i / 2 < Colors.Count; i += 2)
        {
            var pen = new Pen(new SolidColorBrush(ToColor(Colors[i / 2])), System.Math.Max(3, radius * 0.08));
            double a = BearingOf(ColorLimits[i], from, to);
            double b = BearingOf(ColorLimits[i + 1], from, to);
            drawingContext.DrawGeometry(null, pen, Arc(centre, rim - (pen.Thickness / 2) - 1, System.Math.Min(a, b), System.Math.Max(a, b)));
        }

        drawingContext.DrawGeometry(null, RimPen, Arc(centre, rim, from, to));
        foreach (double tick in MinorTicks)
        {
            double angle = BearingOf(tick, from, to);
            drawingContext.DrawLine(TickPen, OnCircle(centre, rim, angle), OnCircle(centre, rim + 3, angle));
        }

        for (int i = 0; i < MajorTicks.Count; i++)
        {
            double angle = BearingOf(MajorTicks[i], from, to);
            drawingContext.DrawLine(TickPen, OnCircle(centre, rim, angle), OnCircle(centre, rim + 6, angle));
            if (i < TickLabels.Count)
            {
                DrawCentred(drawingContext, TickLabels[i], OnCircle(centre, rim + 8 + (FontSize * 0.65), angle), pixelsPerDip);
            }
        }

        double needle = BearingOf(Value, from, to);
        var needlePen = new Pen(NeedleBrush, 2) { EndLineCap = PenLineCap.Round };
        drawingContext.DrawLine(needlePen, centre, OnCircle(centre, rim - 4, needle));
        drawingContext.DrawEllipse(NeedleBrush, null, centre, 3, 3);
    }

    private void RenderLinear(DrawingContext drawingContext, Rect box, double pixelsPerDip)
    {
        bool vertical = Orientation == "vertical";
        const double lead = 8;
        double length = System.Math.Max(1, (vertical ? box.Height : box.Width) - (2 * lead));
        Point Along(double value, double offset)
        {
            double f = Fraction(value);
            return vertical
                ? new Point(box.X + offset, box.Bottom - lead - (f * length))
                : new Point(box.X + lead + (f * length), box.Y + offset);
        }

        double trackAt = vertical ? box.Width * 0.3 : box.Height * 0.3;
        var track = new Pen(TrackBrush, 4);
        drawingContext.DrawLine(track, Along(Minimum, trackAt), Along(Maximum, trackAt));
        for (int i = 0; i + 1 < ColorLimits.Count && i / 2 < Colors.Count; i += 2)
        {
            var pen = new Pen(new SolidColorBrush(ToColor(Colors[i / 2])), 4);
            drawingContext.DrawLine(pen, Along(ColorLimits[i], trackAt), Along(ColorLimits[i + 1], trackAt));
        }

        foreach (double tick in MinorTicks)
        {
            Point at = Along(tick, trackAt + 4);
            drawingContext.DrawLine(TickPen, at, vertical ? new Point(at.X + 3, at.Y) : new Point(at.X, at.Y + 3));
        }

        for (int i = 0; i < MajorTicks.Count; i++)
        {
            Point at = Along(MajorTicks[i], trackAt + 4);
            drawingContext.DrawLine(TickPen, at, vertical ? new Point(at.X + 6, at.Y) : new Point(at.X, at.Y + 6));
            if (i < TickLabels.Count)
            {
                FormattedText label = Label(TickLabels[i], pixelsPerDip);
                drawingContext.DrawText(label, vertical
                    ? new Point(at.X + 9, at.Y - (label.Height / 2))
                    : new Point(at.X - (label.Width / 2), at.Y + 8));
            }
        }

        // The needle: a small triangle pointing at the track from the far side.
        Point tip = Along(Value, trackAt - 3);
        var needle = new StreamGeometry();
        using (StreamGeometryContext g = needle.Open())
        {
            if (vertical)
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(new Point(tip.X - 8, tip.Y - 5), false, false);
                g.LineTo(new Point(tip.X - 8, tip.Y + 5), false, false);
            }
            else
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(new Point(tip.X - 5, tip.Y - 8), false, false);
                g.LineTo(new Point(tip.X + 5, tip.Y - 8), false, false);
            }
        }

        drawingContext.DrawGeometry(NeedleBrush, null, needle);
    }

    private static Color ToColor(UiColor colour) =>
        Color.FromRgb((byte)System.Math.Round(colour.R * 255), (byte)System.Math.Round(colour.G * 255), (byte)System.Math.Round(colour.B * 255));

    private sealed class Peer(UiGaugeFace owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private UiGaugeFace Gauge => (UiGaugeFace)Owner;

        public bool IsReadOnly => true;

        public double LargeChange => 0;

        public double Maximum => Gauge.Maximum;

        public double Minimum => Gauge.Minimum;

        public double SmallChange => 0;

        public double Value => Gauge.Value;

        public void SetValue(double value) => throw new System.Windows.Automation.ElementNotEnabledException();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

        protected override string GetClassNameCore() => "UiGauge";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}

/// <summary>
/// A <c>uiswitch</c> as the window draws it (U9): a two-position control with its two items as
/// labels at its ends, as a slider, a rocker or a toggle. A click, Space or Enter turns it and
/// reports the new position through <see cref="Changed"/>.
/// </summary>
public sealed class UiSwitchFace : UiDrawnFace
{
    public event Action<int>? Changed;

    public UiSwitchStyle SwitchStyle { get; set; }

    public bool IsOn { get; set; }

    public bool Upright { get; set; }

    public IReadOnlyList<string> Items { get; set; } = [];

    public UiSwitchFace()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Rect box = Inner;
        Brush onBrush = IsEnabled ? AccentBrush : TrackBrush;
        switch (SwitchStyle)
        {
            case UiSwitchStyle.Rocker:
            {
                drawingContext.DrawRoundedRectangle(DialBrush, RimPen, box, 3, 3);
                Rect pressed = Upright
                    ? (IsOn ? new Rect(box.X, box.Y, box.Width, box.Height / 2) : new Rect(box.X, box.Y + (box.Height / 2), box.Width, box.Height / 2))
                    : (IsOn ? new Rect(box.X + (box.Width / 2), box.Y, box.Width / 2, box.Height) : new Rect(box.X, box.Y, box.Width / 2, box.Height));
                drawingContext.DrawRoundedRectangle(onBrush, null, pressed, 3, 3);
                break;
            }

            case UiSwitchStyle.Toggle:
            {
                // A lever from the middle to the end that is on.
                double across = System.Math.Min(box.Width, box.Height) * 0.4;
                Point centre = new(box.X + (box.Width / 2), box.Y + (box.Height / 2));
                drawingContext.DrawEllipse(DialBrush, RimPen, centre, across, across);
                Point end = Upright
                    ? new Point(centre.X, IsOn ? box.Y + 3 : box.Bottom - 3)
                    : new Point(IsOn ? box.Right - 3 : box.X + 3, centre.Y);
                var lever = new Pen(onBrush, System.Math.Max(3, across * 0.6)) { EndLineCap = PenLineCap.Round, StartLineCap = PenLineCap.Round };
                drawingContext.DrawLine(lever, centre, end);
                break;
            }

            default:
            {
                double thickness = Upright ? box.Width : box.Height;
                double radius = thickness / 2;
                drawingContext.DrawRoundedRectangle(IsOn ? onBrush : TrackBrush, null, box, radius, radius);
                Point thumb = Upright
                    ? new Point(box.X + radius, IsOn ? box.Y + radius : box.Bottom - radius)
                    : new Point(IsOn ? box.Right - radius : box.X + radius, box.Y + radius);
                drawingContext.DrawEllipse(Brushes.White, RimPen, thumb, radius - 2, radius - 2);
                break;
            }
        }

        if (Items.Count >= 2)
        {
            FormattedText first = Label(Items[0], pixelsPerDip);
            FormattedText second = Label(Items[1], pixelsPerDip);
            if (Upright)
            {
                drawingContext.DrawText(first, new Point(box.X + (box.Width / 2) - (first.Width / 2), box.Bottom + UiSwitchModel.LabelGap));
                drawingContext.DrawText(second, new Point(box.X + (box.Width / 2) - (second.Width / 2), box.Y - UiSwitchModel.LabelGap - second.Height));
            }
            else
            {
                drawingContext.DrawText(first, new Point(box.X - UiSwitchModel.LabelGap - first.Width, box.Y + (box.Height / 2) - (first.Height / 2)));
                drawingContext.DrawText(second, new Point(box.Right + UiSwitchModel.LabelGap, box.Y + (box.Height / 2) - (second.Height / 2)));
            }
        }
    }

    private void Turn()
    {
        if (!IsEnabled)
        {
            return;
        }

        IsOn = !IsOn;
        InvalidateVisual();
        Changed?.Invoke(IsOn ? 1 : 0);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (IsEnabled)
        {
            Focus();
            Turn();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            Turn();
            e.Handled = true;
        }
    }

    private sealed class Peer(UiSwitchFace owner) : FrameworkElementAutomationPeer(owner), IToggleProvider
    {
        private UiSwitchFace Switch => (UiSwitchFace)Owner;

        public System.Windows.Automation.ToggleState ToggleState =>
            Switch.IsOn ? System.Windows.Automation.ToggleState.On : System.Windows.Automation.ToggleState.Off;

        public void Toggle()
        {
            if (!Switch.IsEnabled)
            {
                throw new System.Windows.Automation.ElementNotEnabledException();
            }

            Switch.Turn();
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => "UiSwitch";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}

/// <summary>A <c>uilamp</c> as the window draws it (U9): a circle of one colour, lit from above.</summary>
public sealed class UiLampFace : UiDrawnFace
{
    public Color Color { get; set; } = System.Windows.Media.Colors.Lime;

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);
        Rect box = Inner;
        double radius = System.Math.Max(1, System.Math.Min(box.Width, box.Height) / 2);
        Point centre = new(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        var fill = new RadialGradientBrush(Lighter(Color), Color) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.5, 0.5) };
        drawingContext.DrawEllipse(IsEnabled ? fill : TrackBrush, RimPen, centre, radius - 0.5, radius - 0.5);
    }

    private static Color Lighter(Color color) => Color.FromRgb(
        (byte)System.Math.Min(255, color.R + ((255 - color.R) * 0.6)),
        (byte)System.Math.Min(255, color.G + ((255 - color.G) * 0.6)),
        (byte)System.Math.Min(255, color.B + ((255 - color.B) * 0.6)));
}
