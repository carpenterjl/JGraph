using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;

namespace JGraph.Interaction;

/// <summary>
/// Pure navigation math for the primary axes of an <see cref="AxesModel"/>. All operations work in the
/// axis' scale (forward) space, so they behave correctly for both linear and logarithmic axes and for
/// inverted axes. Each operation disables auto-scaling on the axes it moves.
/// </summary>
public static class Navigation
{
    /// <summary>Zooms both primary axes about a focus pixel by <paramref name="factor"/> (&lt;1 zooms in).</summary>
    public static void ZoomAboutPixel(AxesModel axes, ICoordinateMapper mapper, Point2D focusPixel, double factor) =>
        ZoomAboutPixel(axes, mapper, focusPixel, factor, InteractionDimensions.XY);

    /// <summary>
    /// The same zoom, held to one direction when the axes' zoom interaction names one (M80). The
    /// direction it does not move keeps its range and its auto-scale untouched, which is what makes
    /// <c>Dimensions</c> a setting rather than a label.
    /// </summary>
    public static void ZoomAboutPixel(
        AxesModel axes,
        ICoordinateMapper mapper,
        Point2D focusPixel,
        double factor,
        InteractionDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(mapper);

        Point2D focusData = mapper.PixelToData(focusPixel.X, focusPixel.Y);

        // A polar axes navigates the rulers it is drawn through (M83). Its θ and r are stored as the
        // plots' X and Y data, so moving the primary pair below would be perfectly well-formed
        // arithmetic on ranges nothing draws from — which is exactly what a wheel over a polar chart
        // used to do, silently and to no visible effect.
        if (axes.IsPolar && mapper is PolarTransform)
        {
            ZoomPolar(axes, focusData, factor, dimensions);
            return;
        }

        if (dimensions != InteractionDimensions.Y)
        {
            ZoomAxis(axes.PrimaryXAxis, focusData.X, factor);
        }

        if (dimensions != InteractionDimensions.X)
        {
            ZoomAxis(axes.PrimaryYAxis, focusData.Y, factor);
        }
    }

    /// <summary>
    /// Zooms a polar axes about the radius and angle under the pointer.
    /// </summary>
    /// <remarks>
    /// <c>Dimensions</c> maps onto the two rulers a polar axes has: <c>X</c> is θ and <c>Y</c> is r.
    /// <c>XY</c> — the default — scales r alone rather than both, because zooming a polar chart means
    /// changing how much of the radius is shown, and a default wheel that also narrowed the wedge
    /// would be a surprise nobody asked for. Recorded as a divergence: MATLAB does not define
    /// <c>Dimensions</c> for a polar axes at all.
    /// </remarks>
    private static void ZoomPolar(
        AxesModel axes, Point2D focusData, double factor, InteractionDimensions dimensions)
    {
        if (dimensions == InteractionDimensions.X)
        {
            double focusDegrees = focusData.X * 180 / System.Math.PI;
            ZoomAxis(axes.ThetaAxis, focusDegrees, factor);
            return;
        }

        ZoomAxis(axes.RAxis, focusData.Y, factor);
    }

    /// <summary>
    /// Drags a polar axes: the radial part of the movement slides the visible radii, and the
    /// tangential part turns the chart (M83).
    /// </summary>
    /// <remarks>
    /// One gesture with two components rather than two modes, so a drag simply takes the chart with
    /// it. The turn is a change of <see cref="AxesModel.ThetaZeroOffset"/> and not of
    /// <c>ThetaLim</c>: the visible turn decides which angles are drawn, and shifting it rotates
    /// nothing at all.
    /// </remarks>
    public static void PanPolar(
        AxesModel axes,
        PolarTransform mapper,
        DataRange startR,
        double startOffsetDegrees,
        Point2D startPixel,
        Point2D currentPixel,
        InteractionDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(mapper);

        if (dimensions != InteractionDimensions.X)
        {
            IScaleTransform scale = ScaleTransforms.For(axes.RAxis.Scale);
            double rStart = mapper.PixelToData(startPixel.X, startPixel.Y).Y;
            double rNow = mapper.PixelToData(currentPixel.X, currentPixel.Y).Y;
            PanAxis(axes.RAxis, startR, scale.Forward(rStart) - scale.Forward(rNow));
        }

        if (dimensions != InteractionDimensions.Y)
        {
            double degrees = mapper.AngleDeltaBetween(startPixel, currentPixel) * 180 / System.Math.PI;
            axes.ThetaZeroOffset = startOffsetDegrees + degrees;
        }
    }

    /// <summary>Zooms a single axis about a data focus value by <paramref name="factor"/>.</summary>
    public static void ZoomAxis(AxisModel axis, double focusData, double factor)
    {
        IScaleTransform scale = ScaleTransforms.For(axis.Scale);
        double fFocus = scale.Forward(focusData);
        double fMin = scale.Forward(axis.Range.Min);
        double fMax = scale.Forward(axis.Range.Max);

        double newMin = fFocus - ((fFocus - fMin) * factor);
        double newMax = fFocus + ((fMax - fFocus) * factor);

        axis.AutoScale = false;
        axis.Range = new DataRange(scale.Inverse(newMin), scale.Inverse(newMax));
    }

    /// <summary>
    /// Pans both primary axes so the data point under <paramref name="startPixel"/> at gesture start
    /// moves to <paramref name="currentPixel"/>. The ranges captured at gesture start keep panning
    /// stable across many move events.
    /// </summary>
    public static void Pan(
        AxesModel axes,
        ICoordinateMapper startMapper,
        DataRange startX,
        DataRange startY,
        Point2D startPixel,
        Point2D currentPixel) =>
        Pan(axes, startMapper, startX, startY, startPixel, currentPixel, InteractionDimensions.XY);

    /// <summary>
    /// The same pan, held to one direction when the axes' pan interaction names one (M80). A drag
    /// across a chart whose pan is aimed along X slides it sideways and leaves the other range alone.
    /// </summary>
    public static void Pan(
        AxesModel axes,
        ICoordinateMapper startMapper,
        DataRange startX,
        DataRange startY,
        Point2D startPixel,
        Point2D currentPixel,
        InteractionDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(axes);

        if (dimensions != InteractionDimensions.Y)
        {
            PanAxis(axes.PrimaryXAxis, startX, ForwardShift(axes.PrimaryXAxis, startMapper, startPixel, currentPixel, horizontal: true));
        }

        if (dimensions != InteractionDimensions.X)
        {
            PanAxis(axes.PrimaryYAxis, startY, ForwardShift(axes.PrimaryYAxis, startMapper, startPixel, currentPixel, horizontal: false));
        }
    }

    /// <summary>Sets both primary axes' ranges to the data bounds of a device-space rectangle.</summary>
    public static void ZoomToRect(AxesModel axes, ICoordinateMapper mapper, Rect2D pixelRect)
    {
        Point2D a = mapper.PixelToData(pixelRect.Left, pixelRect.Bottom);
        Point2D b = mapper.PixelToData(pixelRect.Right, pixelRect.Top);

        var xRange = new DataRange(System.Math.Min(a.X, b.X), System.Math.Max(a.X, b.X));
        var yRange = new DataRange(System.Math.Min(a.Y, b.Y), System.Math.Max(a.Y, b.Y));

        if (xRange.IsValid)
        {
            axes.PrimaryXAxis.AutoScale = false;
            axes.PrimaryXAxis.Range = xRange;
        }

        if (yRange.IsValid)
        {
            axes.PrimaryYAxis.AutoScale = false;
            axes.PrimaryYAxis.Range = yRange;
        }
    }

    /// <summary>Re-enables auto-scaling on both primary axes, fitting them to the data.</summary>
    public static void ResetView(AxesModel axes)
    {
        axes.PrimaryXAxis.AutoScale = true;
        axes.PrimaryYAxis.AutoScale = true;

        // A polar axes has two more rulers and a rotation, and none of them is reached by resetting
        // the Cartesian pair (M83). Restoring the view has to restore what the gesture moved.
        if (axes.IsPolar)
        {
            axes.RAxis.AutoScale = true;
            axes.ThetaAxis.Range = new DataRange(0, 360);
            axes.ThetaZeroOffset = 0;
        }

        axes.RecomputeDataBounds();
    }

    private static double ForwardShift(
        AxisModel axis,
        ICoordinateMapper startMapper,
        Point2D startPixel,
        Point2D currentPixel,
        bool horizontal)
    {
        IScaleTransform scale = ScaleTransforms.For(axis.Scale);
        Point2D startData = startMapper.PixelToData(startPixel.X, startPixel.Y);
        Point2D currentData = startMapper.PixelToData(currentPixel.X, currentPixel.Y);

        double fStart = horizontal ? scale.Forward(startData.X) : scale.Forward(startData.Y);
        double fCurrent = horizontal ? scale.Forward(currentData.X) : scale.Forward(currentData.Y);
        return fStart - fCurrent;
    }

    private static void PanAxis(AxisModel axis, DataRange startRange, double forwardShift)
    {
        IScaleTransform scale = ScaleTransforms.For(axis.Scale);
        double fMin = scale.Forward(startRange.Min) + forwardShift;
        double fMax = scale.Forward(startRange.Max) + forwardShift;

        axis.AutoScale = false;
        axis.Range = new DataRange(scale.Inverse(fMin), scale.Inverse(fMax));
    }
}
