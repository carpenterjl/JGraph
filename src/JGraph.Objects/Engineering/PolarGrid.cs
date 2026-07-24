using System.ComponentModel;
using System.Globalization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Engineering;

/// <summary>
/// The circular grid of a polar axes: concentric radius rings, angular spokes, and their labels. It
/// is an ordinary drawable that maps its geometry through the (equal-aspect) coordinate mapper, so no
/// polar-specific rendering path is needed. Polar data series are ordinary line/scatter plots whose
/// (θ, r) samples are converted to Cartesian (x, y) before plotting.
/// </summary>
public sealed class PolarGrid : PlotObject, IDrawable
{
    private double _maxRadius = 1.0;
    private int _radialDivisions = 5;
    private int _angularDivisions = 12;
    private Color _gridColor = Colors.LightGray;
    private TextStyle _labelStyle = new(Colors.DimGray, 10);
    private bool _showLabels = true;

    public PolarGrid()
    {
        Name = "PolarGrid";
        HitTestVisible = false;
    }

    /// <summary>The outer radius of the grid in data units.</summary>
    [Category("Appearance"), DisplayName("Max radius")]
    public double MaxRadius
    {
        get => _maxRadius;
        set => SetProperty(ref _maxRadius, System.Math.Max(double.Epsilon, value), InvalidationKind.Layout);
    }

    /// <summary>The number of concentric radius rings drawn out to <see cref="MaxRadius"/>.</summary>
    [Category("Appearance"), DisplayName("Radial divisions")]
    public int RadialDivisions
    {
        get => _radialDivisions;
        set => SetProperty(ref _radialDivisions, System.Math.Max(1, value), InvalidationKind.Render);
    }

    /// <summary>The number of angular spokes (12 → every 30°).</summary>
    [Category("Appearance"), DisplayName("Angular divisions")]
    public int AngularDivisions
    {
        get => _angularDivisions;
        set => SetProperty(ref _angularDivisions, System.Math.Max(1, value), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Grid color")]
    public Color GridColor
    {
        get => _gridColor;
        set => SetProperty(ref _gridColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Label style")]
    public TextStyle LabelStyle
    {
        get => _labelStyle;
        set => SetProperty(ref _labelStyle, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Show labels")]
    public bool ShowLabels
    {
        get => _showLabels;
        set => SetProperty(ref _showLabels, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => new(-_maxRadius, _maxRadius);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => new(-_maxRadius, _maxRadius);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ICoordinateMapper mapper = state.Mapper;
        var gridStyle = new LineStyle(_gridColor, 1);
        Point2D center = mapper.DataToPixel(0, 0);

        // Concentric radius rings.
        for (int k = 1; k <= _radialDivisions; k++)
        {
            double r = _maxRadius * k / _radialDivisions;
            CircleRenderer.DrawCircle(context, mapper, 0, 0, r, gridStyle);
        }

        // Angular spokes out to the rim.
        for (int j = 0; j < _angularDivisions; j++)
        {
            double angle = 2.0 * System.Math.PI * j / _angularDivisions;
            Point2D rim = mapper.DataToPixel(_maxRadius * System.Math.Cos(angle), _maxRadius * System.Math.Sin(angle));
            context.DrawLine(center, rim, gridStyle);
        }

        if (!_showLabels)
        {
            return;
        }

        // Angle labels just outside the rim.
        for (int j = 0; j < _angularDivisions; j++)
        {
            double angle = 2.0 * System.Math.PI * j / _angularDivisions;
            int degrees = (int)System.Math.Round(angle * 180.0 / System.Math.PI) % 360;
            Point2D at = mapper.DataToPixel(_maxRadius * 1.08 * System.Math.Cos(angle), _maxRadius * 1.08 * System.Math.Sin(angle));
            context.DrawText(
                degrees.ToString(CultureInfo.CurrentCulture) + "°",
                at,
                _labelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Middle);
        }

        // Radius tick labels along the east (0°) axis.
        for (int k = 1; k <= _radialDivisions; k++)
        {
            double r = _maxRadius * k / _radialDivisions;
            Point2D at = mapper.DataToPixel(r, 0);
            context.DrawText(
                FormatRadius(r),
                new Point2D(at.X, at.Y - 2),
                _labelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom);
        }
    }

    private static string FormatRadius(double r) => r.ToString("0.###", CultureInfo.CurrentCulture);
}
