using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering.Layout;

/// <summary>
/// Computes device-space geometry for a figure's axes. The math is pure and backend-independent: it
/// places each axes cell from its normalized bounds and reserves decoration margins for titles, axis
/// labels, and tick labels. In this milestone the decoration margins are estimated; the Skia-backed
/// renderer will refine them using measured text.
/// </summary>
public static class LayoutEngine
{
    /// <summary>Maps an axes' normalized [0, 1] bounds to a device-space rectangle within the figure.</summary>
    public static Rect2D ComputeOuterBounds(Size2D figureSize, Rect2D normalizedBounds)
    {
        double x = normalizedBounds.X * figureSize.Width;
        double y = normalizedBounds.Y * figureSize.Height;
        double w = normalizedBounds.Width * figureSize.Width;
        double h = normalizedBounds.Height * figureSize.Height;
        return new Rect2D(x, y, w, h);
    }

    /// <summary>
    /// Computes an axes layout from its outer cell and the decoration margins to reserve. The plot
    /// area is the outer cell deflated by the margins, clamped so it never becomes negative.
    /// </summary>
    public static AxesLayout Compute(AxesModel axes, Rect2D outerBounds, Thickness decorations)
    {
        Rect2D plotArea = outerBounds.Deflate(decorations);
        return new AxesLayout(axes, outerBounds, plotArea);
    }

    /// <summary>Convenience overload that derives the outer cell from the figure size and normalized bounds.</summary>
    public static AxesLayout Compute(AxesModel axes, Size2D figureSize, Thickness decorations)
    {
        Rect2D outer = ComputeOuterBounds(figureSize, axes.NormalizedBounds);
        return Compute(axes, outer, decorations);
    }

    /// <summary>
    /// Produces a reasonable decoration-margin estimate based on which labels/titles are present.
    /// Used as a starting point and by tests; the Skia renderer measures text for exact margins.
    /// </summary>
    public static Thickness EstimateDecorations(AxesModel axes)
    {
        bool hasTitle = !string.IsNullOrEmpty(axes.Title);
        bool xLabels = axes.PrimaryXAxis.ShowTickLabels;
        bool xTitle = !string.IsNullOrEmpty(axes.PrimaryXAxis.Label);
        bool yLabels = axes.PrimaryYAxis.ShowTickLabels;
        bool yTitle = !string.IsNullOrEmpty(axes.PrimaryYAxis.Label);

        double left = 12 + (yLabels ? 34 : 0) + (yTitle ? 18 : 0);
        double bottom = 10 + (xLabels ? 20 : 0) + (xTitle ? 18 : 0);
        double top = 10 + (hasTitle ? 24 : 0);
        double right = 12;

        return new Thickness(left, top, right, bottom);
    }
}
