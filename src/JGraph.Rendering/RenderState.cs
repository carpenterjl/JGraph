using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// Per-object rendering context passed to <see cref="IDrawable.Render"/>. It carries everything a
/// plot object needs to place itself: the data-to-device coordinate mapper, the clipped plot
/// rectangle, the resolved series color (used when a plot leaves its color unset), and the device
/// pixel ratio for crisp hairlines on high-DPI displays.
/// </summary>
public sealed class RenderState
{
    public RenderState(
        ICoordinateMapper mapper,
        Rect2D plotArea,
        Color seriesColor,
        double devicePixelRatio = 1.0,
        bool depthSort = true)
    {
        Mapper = mapper;
        PlotArea = plotArea;
        SeriesColor = seriesColor;
        DevicePixelRatio = devicePixelRatio;
        DepthSort = depthSort;
    }

    /// <summary>Maps between data space and device space for the owning axes.</summary>
    public ICoordinateMapper Mapper { get; }

    /// <summary>The device-space rectangle plot content is clipped to.</summary>
    public Rect2D PlotArea { get; }

    /// <summary>The color assigned to this series from the axes' color order, for plots without an explicit color.</summary>
    public Color SeriesColor { get; }

    /// <summary>Physical pixels per device-independent unit (1.0 at 96 DPI).</summary>
    public double DevicePixelRatio { get; }

    /// <summary>
    /// Whether a 3D object should sort its own faces back to front (MATLAB <c>SortMethod</c>
    /// <c>'depth'</c>) or paint them in the order it holds them (<c>'childorder'</c>). Depth is what
    /// every 3D plot has always done, so an object that ignores this draws as it always did.
    /// </summary>
    public bool DepthSort { get; }
}
