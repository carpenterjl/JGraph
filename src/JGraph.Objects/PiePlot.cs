using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
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
/// <para>
/// The wedges themselves are a real <see cref="PatchPlot"/> (M79), rebuilt from the values whenever
/// they change and drawn by the patch renderer. That is what makes MATLAB's patch properties — the
/// mesh, its colours, its markers, its lighting — mean something on a pie rather than merely answer:
/// a pie <em>is</em> a patch in MATLAB, and now it is one here too. The geometry is derived, so it is
/// read and never written: the shape of a pie comes from its values.
/// </para>
/// </summary>
public sealed class PiePlot : PlotObject, IDrawable, I3DDrawable, ILegendItem
{
    private readonly PatchPlot _patch = new([0], [0], [0], [[0]]);

    private double[] _values;
    private double[]? _explode;
    private string[]? _labels;
    private Colormap _colormap = Colormap.Parula;
    private double _startAngle = 90;
    private bool _clockwise;
    private bool _showLabels = true;
    private double _labelRadius = 1.2;
    private TextStyle? _labelStyle;
    private bool _wedgesStale = true;

    public PiePlot(double[] values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        Name = "Pie";

        // The patch is the pie's own, not the axes' — it never joins the figure tree, so it is told
        // which chart to find the lights and the colour scale through.
        _patch.Host = this;
        _patch.EdgeColor = Colors.White;
        _patch.EdgeWidth = 1.0;
        Adopt(_patch);
    }

    /// <summary>
    /// The wedges as the patch they are drawn as. Everything MATLAB documents on a pie's patch is
    /// read and written here; the vertices and faces are worked out from the values, so replacing
    /// them is refused and setting a value redraws them.
    /// </summary>
    [Browsable(false)]
    public PatchPlot Patch
    {
        get
        {
            EnsureWedges();
            return _patch;
        }
    }

    /// <summary>The value behind each wedge, exactly as the caller gave them.</summary>
    [Browsable(false)]
    public double[] Values
    {
        get => _values;
        set
        {
            SetProperty(ref _values, value ?? [], InvalidationKind.Layout);
            _wedgesStale = true;
        }
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
        set
        {
            SetProperty(ref _explode, value, InvalidationKind.Layout);
            _wedgesStale = true;
        }
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
        set
        {
            SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render);
            _wedgesStale = true;
        }
    }

    /// <summary>
    /// The colour the wedges are outlined in, or null for no outline. One number with two spellings:
    /// this is the patch's own <see cref="PatchPlot.EdgeColor"/>, so a script that reaches it through
    /// either name is setting the same thing.
    /// </summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _patch.EdgeColor;
        set => _patch.EdgeColor = value;
    }

    /// <inheritdoc cref="PatchPlot.EdgeWidth" />
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _patch.EdgeWidth;
        set => _patch.EdgeWidth = value;
    }

    /// <summary>How opaque the wedge faces are, in [0, 1]. The outlines are unaffected.</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _patch.FaceAlpha;
        set => _patch.FaceAlpha = value;
    }

    /// <summary>Where the first wedge begins, in degrees counter-clockwise from due east.</summary>
    [Category("Appearance"), DisplayName("Start angle")]
    public double StartAngle
    {
        get => _startAngle;
        set
        {
            SetProperty(ref _startAngle, value, InvalidationKind.Layout);
            _wedgesStale = true;
        }
    }

    /// <summary>Whether the wedges run clockwise instead of counter-clockwise.</summary>
    [Category("Appearance")]
    public bool Clockwise
    {
        get => _clockwise;
        set
        {
            SetProperty(ref _clockwise, value, InvalidationKind.Layout);
            _wedgesStale = true;
        }
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
    public IReadOnlyList<PieSlice> Slices() =>
        PieGeometry.Slices(_values, _explode, _startAngle, _clockwise);

    /// <summary>What is written beside wedge <paramref name="i"/>: its label, or its share.</summary>
    public string LabelOf(int i, double fraction) => PieGeometry.LabelOf(_labels, i, fraction);

    public override DataRange GetXDataBounds() => Reach();

    public override DataRange GetYDataBounds() => Reach();

    /// <summary>Flat: a pie lies in the plane its axes is drawn on, whatever the view.</summary>
    public DataRange GetZDataBounds() => new(0, 0);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Wedges().Render(context, state);
        if (_showLabels)
        {
            DrawLabels(context, state.Mapper, Slices());
        }
    }

    /// <summary>
    /// The same wedges under a camera. A pie is flat, so turning the view is the only thing this
    /// changes — and it is also what puts the wedges in front of the axes' lights, which is where a
    /// patch is shaded.
    /// </summary>
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(projection);
        Wedges().Render3D(context, projection, state);
    }

    /// <summary>The patch, rebuilt if the values moved and told the pie's own transparency.</summary>
    private PatchPlot Wedges()
    {
        EnsureWedges();
        _patch.Opacity = Opacity;
        return _patch;
    }

    /// <summary>
    /// Cuts the values into wedges and writes them onto the patch: one face per value, a fan of
    /// vertices per wedge, and the colour the value's place in the order gives it.
    /// <para>
    /// A wedge with no share of the circle still takes its face, holding a single vertex — nothing is
    /// drawn from it, and every face stays at the index of the value behind it, which is what lets
    /// <c>CData</c> and the alpha data be written one entry per value.
    /// </para>
    /// </summary>
    private void EnsureWedges()
    {
        if (!_wedgesStale)
        {
            return;
        }

        _wedgesStale = false;
        IReadOnlyList<PieSlice> slices = Slices();
        Color[] colors = _colormap.Resample(System.Math.Max(slices.Count, 1));

        var xs = new List<double>();
        var ys = new List<double>();
        var faces = new int[slices.Count][];
        var faceColors = new Color[slices.Count];

        for (int i = 0; i < slices.Count; i++)
        {
            PieSlice slice = slices[i];
            (double cx, double cy) = CenterOf(slice);
            faceColors[i] = colors[slice.Index % colors.Length];

            if (slice.Fraction <= 0)
            {
                faces[i] = [xs.Count];
                xs.Add(cx);
                ys.Add(cy);
                continue;
            }

            var face = new List<int>();

            // A whole circle has no point in the middle to close through; anything less is a wedge,
            // and the middle is its first vertex.
            if (slice.Fraction < 1)
            {
                face.Add(xs.Count);
                xs.Add(cx);
                ys.Add(cy);
            }

            int steps = PieGeometry.StepsFor(slice.Sweep);
            for (int step = 0; step <= steps; step++)
            {
                double angle = slice.Start + (slice.Sweep * step / steps);
                face.Add(xs.Count);
                xs.Add(cx + System.Math.Cos(angle));
                ys.Add(cy + System.Math.Sin(angle));
            }

            faces[i] = [.. face];
        }

        // The colours are set after the geometry: a per-face list has to match the face count, and
        // the patch checks that when it is handed one.
        _patch.SetData([.. xs], [.. ys], new double[xs.Count], faces);
        _patch.FaceColors = faceColors;
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
    private static (double X, double Y) CenterOf(PieSlice slice) => PieGeometry.CenterOf(slice);

    private double OffsetOf(int i) => PieGeometry.OffsetOf(_explode, i);

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
