using System.ComponentModel;
using System.Globalization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Engineering;

/// <summary>
/// The grid of a Smith chart: the unit reflection-coefficient circle, constant-resistance circles,
/// and constant-reactance arcs. Impedance data is converted to reflection coefficients
/// Γ = (z − 1)/(z + 1) before plotting, so the data lives in the unit disk and this grid draws through
/// the same equal-aspect mapper as the polar grid — no chart-specific rendering path.
/// </summary>
public sealed class SmithGrid : PlotObject, IDrawable
{
    private static readonly double[] DefaultResistances = { 0.2, 0.5, 1.0, 2.0, 5.0 };
    private static readonly double[] DefaultReactances = { 0.2, 0.5, 1.0, 2.0, 5.0 };

    private Color _gridColor = Colors.LightGray;
    private TextStyle _labelStyle = new(Colors.DimGray, 10);
    private bool _showLabels = true;

    public SmithGrid()
    {
        Name = "SmithGrid";
        HitTestVisible = false;
    }

    [Category("Appearance"), DisplayName("Grid color")]
    public Color GridColor
    {
        get => _gridColor;
        set => SetProperty(ref _gridColor, value, InvalidationKind.Render);
    }

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
    public override DataRange GetXDataBounds() => new(-1, 1);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => new(-1, 1);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ICoordinateMapper mapper = state.Mapper;
        var gridStyle = new LineStyle(_gridColor, 1);

        // The unit circle (Γ boundary) and the real axis.
        CircleRenderer.DrawCircle(context, mapper, 0, 0, 1.0, gridStyle, 120);
        context.DrawLine(mapper.DataToPixel(-1, 0), mapper.DataToPixel(1, 0), gridStyle);

        // Constant-resistance circles: center (r/(1+r), 0), radius 1/(1+r); all inside the unit disk.
        foreach (double r in DefaultResistances)
        {
            double center = r / (1.0 + r);
            double radius = 1.0 / (1.0 + r);
            CircleRenderer.DrawCircle(context, mapper, center, 0, radius, gridStyle, 120);
        }

        // Constant-reactance arcs: center (1, 1/x), radius 1/|x|, clipped to the unit disk.
        foreach (double x in DefaultReactances)
        {
            double radius = 1.0 / x;
            CircleRenderer.DrawCircleClippedToUnitDisk(context, mapper, 1.0, 1.0 / x, radius, 1.0, gridStyle);
            CircleRenderer.DrawCircleClippedToUnitDisk(context, mapper, 1.0, -1.0 / x, radius, 1.0, gridStyle);
        }

        if (!_showLabels)
        {
            return;
        }

        // Resistance labels where each circle crosses the real axis (Γ = (r−1)/(r+1)).
        foreach (double r in DefaultResistances)
        {
            double gx = (r - 1.0) / (r + 1.0);
            Point2D at = mapper.DataToPixel(gx, 0);
            context.DrawText(
                r.ToString("0.#", CultureInfo.CurrentCulture),
                new Point2D(at.X, at.Y + 2),
                _labelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Top);
        }
    }
}
