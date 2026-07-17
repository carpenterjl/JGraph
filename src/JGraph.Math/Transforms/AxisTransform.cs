using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Transforms;

/// <summary>
/// Maps data-space coordinates to device/pixel space for a specific pair of X and Y axes within a
/// plot rectangle, accounting for each axis' scale, range, and inversion. This is the concrete
/// <see cref="ICoordinateMapper"/> used by rendering, interaction, and hit-testing.
/// </summary>
/// <remarks>
/// Device space follows the screen convention: X grows rightward and Y grows downward, so the
/// data-space minimum of a (non-inverted) Y axis maps to the bottom of the plot rectangle.
/// </remarks>
public sealed class AxisTransform : ICoordinateMapper
{
    private readonly IScaleTransform _xScale;
    private readonly IScaleTransform _yScale;
    private readonly bool _xInverted;
    private readonly bool _yInverted;

    // Precomputed linear-space bounds of each axis range.
    private readonly double _fxMin;
    private readonly double _fxSpan;
    private readonly double _fyMin;
    private readonly double _fySpan;

    public AxisTransform(
        Rect2D plotArea,
        IScaleTransform xScale,
        DataRange xRange,
        bool xInverted,
        IScaleTransform yScale,
        DataRange yRange,
        bool yInverted)
    {
        PlotArea = plotArea;
        _xScale = xScale;
        _yScale = yScale;
        _xInverted = xInverted;
        _yInverted = yInverted;

        double fxMin = xScale.Forward(xRange.Min);
        double fxMax = xScale.Forward(xRange.Max);
        _fxMin = fxMin;
        _fxSpan = NonZeroSpan(fxMax - fxMin);

        double fyMin = yScale.Forward(yRange.Min);
        double fyMax = yScale.Forward(yRange.Max);
        _fyMin = fyMin;
        _fySpan = NonZeroSpan(fyMax - fyMin);
    }

    /// <inheritdoc />
    public Rect2D PlotArea { get; }

    /// <summary>Builds a transform for a plot rectangle from an X/Y axis pair.</summary>
    public static AxisTransform Create(Rect2D plotArea, AxisModel xAxis, AxisModel yAxis) => new(
        plotArea,
        ScaleTransforms.For(xAxis.Scale),
        xAxis.Range,
        xAxis.Inverted,
        ScaleTransforms.For(yAxis.Scale),
        yAxis.Range,
        yAxis.Inverted);

    /// <inheritdoc />
    public Point2D DataToPixel(double x, double y) => new(DataToPixelX(x), DataToPixelY(y));

    /// <inheritdoc />
    public Point2D PixelToData(double px, double py) => new(PixelToDataX(px), PixelToDataY(py));

    /// <summary>Maps a single data X value to a device X coordinate.</summary>
    public double DataToPixelX(double x)
    {
        double n = (_xScale.Forward(x) - _fxMin) / _fxSpan;
        if (_xInverted)
        {
            n = 1 - n;
        }

        return PlotArea.Left + (n * PlotArea.Width);
    }

    /// <summary>Maps a single data Y value to a device Y coordinate (Y grows downward).</summary>
    public double DataToPixelY(double y)
    {
        double n = (_yScale.Forward(y) - _fyMin) / _fySpan;
        if (_yInverted)
        {
            n = 1 - n;
        }

        // Data minimum at the bottom of the rectangle, maximum at the top.
        return PlotArea.Bottom - (n * PlotArea.Height);
    }

    /// <summary>Maps a device X coordinate back to a data X value.</summary>
    public double PixelToDataX(double px)
    {
        double n = (px - PlotArea.Left) / PlotArea.Width;
        if (_xInverted)
        {
            n = 1 - n;
        }

        return _xScale.Inverse(_fxMin + (n * _fxSpan));
    }

    /// <summary>Maps a device Y coordinate back to a data Y value.</summary>
    public double PixelToDataY(double py)
    {
        double n = (PlotArea.Bottom - py) / PlotArea.Height;
        if (_yInverted)
        {
            n = 1 - n;
        }

        return _yScale.Inverse(_fyMin + (n * _fySpan));
    }

    private static double NonZeroSpan(double span) =>
        System.Math.Abs(span) < double.Epsilon ? 1.0 : span;
}
