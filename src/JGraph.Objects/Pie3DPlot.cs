using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A raised pie chart (MATLAB <c>pie3</c>): the same unit circle divided into the same wedges as
/// <see cref="PiePlot"/>, given a thickness so that each wedge is a solid with a lid, a floor and a
/// skirt round its arc.
///
/// <para>
/// The wedge arithmetic is shared with the flat pie, so a total above one is normalized and a total
/// at or below it is taken as the shares themselves — a pie3 and a pie of the same numbers divide
/// the circle identically, and only the drawing differs.
/// </para>
///
/// <para>
/// Two divergences from MATLAB, both deliberate. MATLAB builds a pie3 out of a surface, two patches
/// and a text object per wedge and hands back all of them; this is one object and one handle,
/// because the faces are painted back to front and a depth sort is only right when it can see the
/// whole chart. And the sides are shaded by a fixed step from the lid rather than lit: there is no
/// light model in this pipeline, and a flat-coloured solid reads as a flat shape.
/// </para>
/// </summary>
public sealed class Pie3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem
{
    /// <summary>How much of a wedge's colour the lid, the floor and the walls each keep.</summary>
    private const double LidShade = 1.0;
    private const double FloorShade = 0.5;
    private const double WallShade = 0.72;

    private double[] _values;
    private double[]? _explode;
    private string[]? _labels;
    private Colormap _colormap = Colormap.Parula;
    private Color? _edgeColor = Colors.White;
    private double _lineWidth = 1.0;
    private double _faceAlpha = 1.0;
    private double _startAngle = 90;
    private bool _clockwise;
    private double _height = 0.3;
    private bool _showLabels = true;
    private double _labelRadius = 1.2;
    private TextStyle? _labelStyle;

    private Point2D[] _face = new Point2D[8];

    public Pie3DPlot(double[] values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        Name = "Pie3D";
    }

    /// <summary>The value behind each wedge, exactly as the caller gave them.</summary>
    [Browsable(false)]
    public double[] Values
    {
        get => _values;
        set => SetProperty(ref _values, value ?? [], InvalidationKind.Layout);
    }

    /// <summary>How far each wedge is pushed out of the middle, as a fraction of the radius.</summary>
    [Browsable(false)]
    public double[]? Explode
    {
        get => _explode;
        set => SetProperty(ref _explode, value, InvalidationKind.Layout);
    }

    /// <summary>What is written beside each wedge, or null to write its share as a percentage.</summary>
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

    /// <summary>The colour the faces are outlined in, or null for no outline.</summary>
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

    /// <summary>How thick the pie is, as a fraction of its radius.</summary>
    [Category("Appearance")]
    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, System.Math.Max(0, value), InvalidationKind.Layout);
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

    /// <summary>The wedges, in the order the values were given.</summary>
    public IReadOnlyList<PieSlice> Slices() =>
        PieGeometry.Slices(_values, _explode, _startAngle, _clockwise);

    /// <summary>What is written beside wedge <paramref name="i"/>: its label, or its share.</summary>
    public string LabelOf(int i, double fraction) => PieGeometry.LabelOf(_labels, i, fraction);

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => Reach();

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => Reach();

    /// <inheritdoc />
    public DataRange GetZDataBounds() =>
        _values.Length == 0 ? DataRange.Empty : new DataRange(0, System.Math.Max(_height, 1e-9));

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);

        IReadOnlyList<PieSlice> slices = Slices();
        Color[] colors = _colormap.Resample(System.Math.Max(slices.Count, 1));
        LineStyle? stroke = _edgeColor is { } edge && _lineWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity), _lineWidth)
            : null;

        List<Face> faces = Faces(slices);
        var depths = new double[faces.Count];
        var order = new int[faces.Count];
        for (int f = 0; f < faces.Count; f++)
        {
            depths[f] = faces[f].Depth(projection);
            order[f] = f;
        }

        Array.Sort(depths, order);

        foreach (int f in order)
        {
            Face face = faces[f];
            if (_face.Length < face.Corners.Count)
            {
                _face = new Point2D[face.Corners.Count];
            }

            for (int v = 0; v < face.Corners.Count; v++)
            {
                (double x, double y, double z) = face.Corners[v];
                _face[v] = projection.ProjectPoint(x, y, z);
            }

            Color color = Shaded(colors[face.Slice % colors.Length], face.Shade);
            context.DrawPolygon(
                _face.AsSpan(0, face.Corners.Count),
                stroke,
                color.WithOpacity(Opacity * _faceAlpha));
        }

        if (_showLabels)
        {
            DrawLabels(context, projection, slices);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: _colormap.Resample(System.Math.Max(_values.Length, 1))[0]);

    /// <summary>One flat polygon of the solid, with the wedge it belongs to and how dark it is.</summary>
    private readonly record struct Face(
        int Slice, double Shade, IReadOnlyList<(double X, double Y, double Z)> Corners)
    {
        /// <summary>How far from the camera the face is, taken at its middle.</summary>
        public double Depth(Projection3D projection)
        {
            double sum = 0;
            foreach ((double x, double y, double z) in Corners)
            {
                sum += projection.Project(x, y, z).Depth;
            }

            return sum / Corners.Count;
        }
    }

    /// <summary>
    /// Every polygon of every wedge: a lid, a floor, the skirt round the arc, and — unless the wedge
    /// is the whole circle and so has no straight side — the two radial walls that close it.
    /// </summary>
    private List<Face> Faces(IReadOnlyList<PieSlice> slices)
    {
        var faces = new List<Face>();
        foreach (PieSlice slice in slices)
        {
            if (slice.Fraction <= 0)
            {
                continue;
            }

            (double cx, double cy) = PieGeometry.CenterOf(slice);
            int steps = PieGeometry.StepsFor(slice.Sweep);
            var arc = new (double X, double Y)[steps + 1];
            for (int step = 0; step <= steps; step++)
            {
                double angle = slice.Start + (slice.Sweep * step / steps);
                arc[step] = (cx + System.Math.Cos(angle), cy + System.Math.Sin(angle));
            }

            bool wedge = slice.Fraction < 1;
            faces.Add(new Face(slice.Index, LidShade, Disc(arc, cx, cy, _height, wedge)));
            faces.Add(new Face(slice.Index, FloorShade, Disc(arc, cx, cy, 0, wedge)));

            for (int step = 0; step < steps; step++)
            {
                faces.Add(new Face(slice.Index, WallShade,
                [
                    (arc[step].X, arc[step].Y, 0),
                    (arc[step + 1].X, arc[step + 1].Y, 0),
                    (arc[step + 1].X, arc[step + 1].Y, _height),
                    (arc[step].X, arc[step].Y, _height),
                ]));
            }

            if (wedge)
            {
                foreach ((double X, double Y) end in new[] { arc[0], arc[steps] })
                {
                    faces.Add(new Face(slice.Index, WallShade,
                    [
                        (cx, cy, 0),
                        (end.X, end.Y, 0),
                        (end.X, end.Y, _height),
                        (cx, cy, _height),
                    ]));
                }
            }
        }

        return faces;
    }

    /// <summary>The lid or the floor of one wedge, at the given height.</summary>
    private static (double X, double Y, double Z)[] Disc(
        (double X, double Y)[] arc, double cx, double cy, double z, bool wedge)
    {
        var corners = new (double X, double Y, double Z)[arc.Length + (wedge ? 1 : 0)];
        int at = 0;
        if (wedge)
        {
            corners[at++] = (cx, cy, z);
        }

        foreach ((double x, double y) in arc)
        {
            corners[at++] = (x, y, z);
        }

        return corners;
    }

    private static Color Shaded(Color color, double keep) =>
        keep >= 1 ? color : Color.Lerp(color, Colors.Black, 1 - keep);

    private void DrawLabels(
        IRenderContext context, Projection3D projection, IReadOnlyList<PieSlice> slices)
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

            (double cx, double cy) = PieGeometry.CenterOf(slice);
            context.DrawText(
                text,
                projection.ProjectPoint(
                    cx + (_labelRadius * System.Math.Cos(slice.Middle)),
                    cy + (_labelRadius * System.Math.Sin(slice.Middle)),
                    _height),
                style,
                HorizontalAlignment.Center,
                VerticalAlignment.Middle);
        }
    }

    /// <summary>
    /// How far the chart reaches from the middle — the radius, plus the furthest a wedge is pushed
    /// out, plus room for the labels, which cannot be measured before there is a surface to measure
    /// them on.
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
            reach = System.Math.Max(reach, 1 + PieGeometry.OffsetOf(_explode, i));
        }

        if (_showLabels)
        {
            reach = System.Math.Max(reach, _labelRadius) + 0.2;
        }

        return new DataRange(-reach, reach);
    }
}
