using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering.Layout;

/// <summary>
/// The computed device-space geometry for one axes: the whole cell it occupies within the figure
/// (<see cref="OuterBounds"/>) and the inner rectangle its data is drawn into (<see cref="PlotArea"/>),
/// after reserving room for the title, axis labels, and tick labels.
/// </summary>
public readonly struct AxesLayout
{
    public AxesLayout(AxesModel axes, Rect2D outerBounds, Rect2D plotArea)
    {
        Axes = axes;
        OuterBounds = outerBounds;
        PlotArea = plotArea;
    }

    /// <summary>The axes this layout was computed for.</summary>
    public AxesModel Axes { get; }

    /// <summary>The device-space rectangle of the whole axes cell.</summary>
    public Rect2D OuterBounds { get; }

    /// <summary>The device-space rectangle data is plotted into.</summary>
    public Rect2D PlotArea { get; }
}
