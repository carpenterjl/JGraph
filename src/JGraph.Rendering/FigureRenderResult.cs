using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;

namespace JGraph.Rendering;

/// <summary>Where one legend row was drawn, and which series it names.</summary>
public readonly record struct LegendRowBounds(PlotObject Plot, Rect2D Bounds);

/// <summary>Where a legend landed in a paint: its whole box, and each row inside it.</summary>
public sealed class LegendLayout
{
    public LegendLayout(Rect2D box, IReadOnlyList<LegendRowBounds> rows)
    {
        Box = box;
        Rows = rows;
    }

    public Rect2D Box { get; }

    /// <summary>The rows, top to bottom, each spanning the box's width.</summary>
    public IReadOnlyList<LegendRowBounds> Rows { get; }

    /// <summary>The series whose row contains a device-space point, or null.</summary>
    public PlotObject? HitTest(Point2D pixel)
    {
        foreach (LegendRowBounds row in Rows)
        {
            if (row.Bounds.Contains(pixel))
            {
                return row.Plot;
            }
        }

        return null;
    }
}

/// <summary>The device-space geometry produced for one axes during a paint.</summary>
public sealed class AxesRenderInfo
{
    public AxesRenderInfo(
        AxesModel axes, Rect2D plotArea, AxisTransform transform, LegendLayout? legend = null)
    {
        Axes = axes;
        PlotArea = plotArea;
        Transform = transform;
        Legend = legend;
    }

    public AxesModel Axes { get; }

    /// <summary>The device-space rectangle data was drawn into.</summary>
    public Rect2D PlotArea { get; }

    /// <summary>The data↔device mapper for this axes' primary axes.</summary>
    public AxisTransform Transform { get; }

    /// <summary>
    /// Where the legend was drawn, or null when it is hidden or empty. Published so the interaction
    /// layer can hit-test, drag, and click through the legend without re-running layout.
    /// </summary>
    public LegendLayout? Legend { get; }

    /// <summary>The box the legend was drawn in, or null when it is hidden or empty.</summary>
    public Rect2D? LegendBounds => Legend?.Box;
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
