using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;

namespace JGraph.Rendering;

/// <summary>The device-space geometry produced for one axes during a paint.</summary>
public sealed class AxesRenderInfo
{
    public AxesRenderInfo(AxesModel axes, Rect2D plotArea, AxisTransform transform, Rect2D? legendBounds = null)
    {
        Axes = axes;
        PlotArea = plotArea;
        Transform = transform;
        LegendBounds = legendBounds;
    }

    public AxesModel Axes { get; }

    /// <summary>The device-space rectangle data was drawn into.</summary>
    public Rect2D PlotArea { get; }

    /// <summary>The data↔device mapper for this axes' primary axes.</summary>
    public AxisTransform Transform { get; }

    /// <summary>
    /// The box the legend was drawn in, or null when it is hidden or empty. Published so the
    /// interaction layer can hit-test and drag the legend without re-running layout.
    /// </summary>
    public Rect2D? LegendBounds { get; }
}

/// <summary>
/// The result of rendering a figure: the per-axes geometry from this paint. The host keeps it so the
/// interaction layer can map pointer positions to data and hit-test without re-running layout.
/// </summary>
public sealed class FigureRenderResult
{
    public static readonly FigureRenderResult Empty = new(Array.Empty<AxesRenderInfo>(), null);

    public FigureRenderResult(IReadOnlyList<AxesRenderInfo> axes, ICoordinateMapper? figureMapper = null)
    {
        Axes = axes;
        FigureMapper = figureMapper;
    }

    public IReadOnlyList<AxesRenderInfo> Axes { get; }

    /// <summary>
    /// Maps normalized [0, 1] figure coordinates to device space for this paint (used by figure-space
    /// annotations), or null if the figure has not been rendered.
    /// </summary>
    public ICoordinateMapper? FigureMapper { get; }

    /// <summary>Finds the axes whose plot area contains the given device-space point.</summary>
    public AxesRenderInfo? HitTest(Point2D pixel)
    {
        // Search topmost-first so overlapping axes resolve to the last drawn.
        for (int i = Axes.Count - 1; i >= 0; i--)
        {
            if (Axes[i].PlotArea.Contains(pixel))
            {
                return Axes[i];
            }
        }

        return null;
    }
}
