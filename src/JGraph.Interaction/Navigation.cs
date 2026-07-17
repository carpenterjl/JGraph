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
    public static void ZoomAboutPixel(AxesModel axes, ICoordinateMapper mapper, Point2D focusPixel, double factor)
    {
        Point2D focusData = mapper.PixelToData(focusPixel.X, focusPixel.Y);
        ZoomAxis(axes.PrimaryXAxis, focusData.X, factor);
        ZoomAxis(axes.PrimaryYAxis, focusData.Y, factor);
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
        Point2D currentPixel)
    {
        PanAxis(axes.PrimaryXAxis, startX, ForwardShift(axes.PrimaryXAxis, startMapper, startPixel, currentPixel, horizontal: true));
        PanAxis(axes.PrimaryYAxis, startY, ForwardShift(axes.PrimaryYAxis, startMapper, startPixel, currentPixel, horizontal: false));
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
