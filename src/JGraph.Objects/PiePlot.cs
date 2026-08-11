using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// One wedge of a <see cref="PiePlot"/>, in the geometry the plot draws it from: angles in radians
/// measured the mathematical way (counter-clockwise from the positive X direction) on a unit circle
/// centered at the origin.
/// </summary>
/// <param name="Index">Which value of the plot this wedge came from.</param>
/// <param name="Start">Where the wedge begins.</param>
/// <param name="Sweep">How far it turns — negative when the pie runs clockwise.</param>
/// <param name="Fraction">The share of the whole circle it covers, in [0, 1].</param>
/// <param name="Offset">How far it is pushed out from the center, as a fraction of the radius.</param>
public readonly record struct PieSlice(int Index, double Start, double Sweep, double Fraction, double Offset)
{
    /// <summary>The angle halfway through the wedge, which is the direction it points.</summary>
    public double Middle => Start + (Sweep / 2);
}

/// <summary>
/// A pie chart (MATLAB <c>pie</c>): a unit circle at the origin divided into wedges, one per value,
/// each labelled with its share.
/// <para>
/// The values are read the way MATLAB reads them — a total above one is normalized, and a total at
/// or below one is taken as the shares themselves, which is how a partial pie is asked for. Nothing
/// about that is stored: <see cref="Values"/> keeps answering what the script passed, and the shares
/// are worked out from it whenever the wedges are needed.
/// </para>
/// <para>
/// A pie is drawn in data space on a unit circle, so it needs an equal-aspect axes to come out
/// round — the <c>pie</c> verb arranges that, along with hiding the frame and the rulers, which have
/// nothing to say about a pie. Two divergences from MATLAB are worth stating: a pie here is one
/// object rather than a patch and a text object per wedge, and a zero-valued entry keeps its place
/// in the colour order rather than being dropped from the chart.
/// </para>
/// </summary>
public sealed class PiePlot : PlotObject, IDrawable, ILegendItem
{
    private double[] _values;
    private double[]? _explode;
    private string[]? _labels;
    private Colormap _colormap = Colormap.Parula;
    private Color? _edgeColor = Colors.White;
    private double _lineWidth = 1.0;
    private double _faceAlpha = 1.0;
    private double _startAngle = 90;
    private bool _clockwise;
    private bool _showLabels = true;
    private double _labelRadius = 1.2;
    private TextStyle? _labelStyle;

    public PiePlot(double[] values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        Name = "Pie";
    }

    /// <summary>The value behind each wedge, exactly as the caller gave them.</summary>
    [Browsable(false)]
    public double[] Values
    {
        get => _values;
        set => SetProperty(ref _values, value ?? [], InvalidationKind.Layout);
    }

    /// <summary>
    /// How far each wedge is pushed out of the middle, as a fraction of the radius, or null for a
    /// pie with none pushed out. MATLAB's <c>explode</c> vector is a set of flags; turning a flag
    /// into a distance is the verb's job, so the model can offset a wedge by any amount.
    /// </summary>
    [Browsable(false)]
    public double[]? Explode
    {
        get => _explode;
        set => SetProperty(ref _explode, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// What is written beside each wedge, or null to write its share as a percentage — which is what
    /// MATLAB does when a call names no labels.
    /// </summary>
    [Browsable(false)]
    public string[]? Labels
    {
        get => _labels;
        set => SetProperty(ref _labels, value, InvalidationKind.Render);
    }

    /// <summary>The colormap the wedges are coloured from, spread evenly across them.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render);
    }

    /// <summary>The colour the wedges are outlined in, or null for no outline.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>How opaque the wedge faces are, in [0, 1]. The outlines are unaffected.</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>Where the first wedge begins, in degrees counter-clockwise from due east.</summary>
    [Category("Appearance"), DisplayName("Start angle")]
    public double StartAngle
    {
        get => _startAngle;
        set => SetProperty(ref _startAngle, value, InvalidationKind.Layout);
    }

    /// <summary>Whether the wedges run clockwise instead of counter-clockwise.</summary>
    [Category("Appearance")]
    public bool Clockwise
    {
        get => _clockwise;
        set => SetProperty(ref _clockwise, value, InvalidationKind.Layout);
    }

    /// <summary>Whether the wedge labels are drawn at all.</summary>
    [Category("Appearance"), DisplayName("Show labels")]
    public bool ShowLabels
    {
        get => _showLabels;
        set => SetProperty(ref _showLabels, value, InvalidationKind.Layout);
    }

    /// <summary>How far out the labels sit, as a multiple of the radius.</summary>
    [Category("Appearance"), DisplayName("Label radius")]
    public double LabelRadius
    {
        get => _labelRadius;
        set => SetProperty(ref _labelRadius, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>How the labels are drawn, or null for ten-point text in the default colour.</summary>
    [Category("Appearance"), DisplayName("Label style")]
    public TextStyle? LabelStyle
    {
        get => _labelStyle;
        set => SetProperty(ref _labelStyle, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>
    /// The wedges, in the order the values were given. A value that is not a positive finite number
    /// takes no angle, which is how a zero entry ends up drawing nothing at all.
    /// </summary>
    public IReadOnlyList<PieSlice> Slices()
    {
        double total = 0;
        foreach (double value in _values)
        {
            if (double.IsFinite(value) && value > 0)
            {
                total += value;
            }
        }

        // MATLAB's rule, and the whole of it: a total over one is normalized, and one at or below it
        // is already the shares — which is the only way to ask for a pie with a piece missing.
        double scale = total > 1 ? 1 / total : 1;
        double direction = _clockwise ? -1 : 1;
        double angle = _startAngle * System.Math.PI / 180;

        var slices = new List<PieSlice>(_values.Length);
        for (int i = 0; i < _values.Length; i++)
        {
            double value = _values[i];
            double fraction = double.IsFinite(value) && value > 0 ? value * scale : 0;
            double sweep = fraction * 2 * System.Math.PI * direction;
            slices.Add(new PieSlice(i, angle, sweep, fraction, OffsetOf(i)));
            angle += sweep;
        }

        return slices;
    }

    /// <summary>What is written beside wedge <paramref name="i"/>: its label, or its share.</summary>
    public string LabelOf(int i, double fraction)
    {
        if (_labels is { } labels && i < labels.Length)
        {
            return labels[i] ?? string.Empty;
        }

        // MATLAB rounds to whole percent and says "< 1%" rather than "0%" for a sliver, because a
        // wedge that is drawn should not be labelled as though it were not there.
        double percent = fraction * 100;
        return percent > 0 && percent < 0.5
            ? "< 1%"
            : $"{System.Math.Round(percent, MidpointRounding.AwayFromZero):0}%";
    }

    public override DataRange GetXDataBounds() => Reach();

    public override DataRange GetYDataBounds() => Reach();

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        IReadOnlyList<PieSlice> slices = Slices();
        Color[] colors = _colormap.Resample(System.Math.Max(slices.Count, 1));
        LineStyle? stroke = _edgeColor is { } edge && _lineWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity), _lineWidth)
            : null;
        ICoordinateMapper mapper = state.Mapper;
        var vertices = new List<Point2D>();

        foreach (PieSlice slice in slices)
        {
            if (slice.Fraction <= 0)
            {
                continue;
            }

            vertices.Clear();
            (double cx, double cy) = CenterOf(slice);

            // A whole circle has no point in the middle to close through; anything less is a wedge,
            // and the middle is its first vertex.
            if (slice.Fraction < 1)
            {
                vertices.Add(mapper.DataToPixel(cx, cy));
            }

            int steps = System.Math.Max(2, (int)System.Math.Ceiling(System.Math.Abs(slice.Sweep) / (System.Math.PI / 90)));
            for (int step = 0; step <= steps; step++)
            {
                double angle = slice.Start + (slice.Sweep * step / steps);
                vertices.Add(mapper.DataToPixel(
                    cx + System.Math.Cos(angle), cy + System.Math.Sin(angle)));
            }

            context.DrawPolygon(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                stroke,
                colors[slice.Index % colors.Length].WithOpacity(Opacity * _faceAlpha));
        }

        if (_showLabels)
        {
            DrawLabels(context, mapper, slices);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: _colormap.Resample(System.Math.Max(_values.Length, 1))[0]);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        // A wedge is a filled shape, so the test is whether the point is in it — worked out in data
        // space, where a pie is always a unit circle whatever the axes have been zoomed to.
        Point2D point = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        foreach (PieSlice slice in Slices())
        {
            if (slice.Fraction <= 0)
            {
                continue;
            }

            (double cx, double cy) = CenterOf(slice);
            double dx = point.X - cx;
            double dy = point.Y - cy;
            if (((dx * dx) + (dy * dy)) > 1 || !Covers(slice, System.Math.Atan2(dy, dx)))
            {
                continue;
            }

            return new PlotHitResult(this, new Point2D(slice.Index, _values[slice.Index]), 0, slice.Index);
        }

        return null;
    }

    /// <summary>Whether <paramref name="angle"/> falls inside the wedge, whichever way it turns.</summary>
    private static bool Covers(PieSlice slice, double angle)
    {
        double turn = 2 * System.Math.PI;
        double into = (angle - slice.Start) / (slice.Sweep < 0 ? -1 : 1);
        into -= System.Math.Floor(into / turn) * turn;
        return into <= System.Math.Abs(slice.Sweep);
    }

    private void DrawLabels(IRenderContext context, ICoordinateMapper mapper, IReadOnlyList<PieSlice> slices)
    {
        TextStyle style = _labelStyle ?? new TextStyle(Colors.Black, 10);
        foreach (PieSlice slice in slices)
        {
            if (slice.Fraction <= 0)
            {
                continue;
            }

            string text = LabelOf(slice.Index, slice.Fraction);
            if (text.Length == 0)
            {
                continue;
            }

            (double cx, double cy) = CenterOf(slice);
            context.DrawText(
                text,
                mapper.DataToPixel(
                    cx + (_labelRadius * System.Math.Cos(slice.Middle)),
                    cy + (_labelRadius * System.Math.Sin(slice.Middle))),
                style,
                HorizontalAlignment.Center,
                VerticalAlignment.Middle);
        }
    }

    /// <summary>Where the tip of a wedge sits once it has been pushed out of the middle.</summary>
    private (double X, double Y) CenterOf(PieSlice slice) =>
        slice.Offset == 0
            ? (0, 0)
            : (slice.Offset * System.Math.Cos(slice.Middle), slice.Offset * System.Math.Sin(slice.Middle));

    private double OffsetOf(int i) =>
        _explode is { } explode && i < explode.Length && double.IsFinite(explode[i]) ? explode[i] : 0;

    /// <summary>
    /// How far the chart reaches from the origin — the radius, plus the furthest a wedge is pushed
    /// out, plus room for the labels. The labels are not measured: their width is not known until
    /// there is a surface to measure on, and the bounds are needed before there is one.
    /// </summary>
    private DataRange Reach()
    {
        if (_values.Length == 0)
        {
            return DataRange.Empty;
        }

        double reach = 1;
        for (int i = 0; i < _values.Length; i++)
        {
            reach = System.Math.Max(reach, 1 + OffsetOf(i));
        }

        if (_showLabels)
        {
            reach = System.Math.Max(reach, _labelRadius) + 0.2;
        }

        return new DataRange(-reach, reach);
    }
}
