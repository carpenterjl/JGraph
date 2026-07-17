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
    public RenderState(ICoordinateMapper mapper, Rect2D plotArea, Color seriesColor, double devicePixelRatio = 1.0)
    {
        Mapper = mapper;
        PlotArea = plotArea;
        SeriesColor = seriesColor;
        DevicePixelRatio = devicePixelRatio;
    }

    /// <summary>Maps between data space and device space for the owning axes.</summary>
    public ICoordinateMapper Mapper { get; }

    /// <summary>The device-space rectangle plot content is clipped to.</summary>
    public Rect2D PlotArea { get; }

    /// <summary>The color assigned to this series from the axes' color order, for plots without an explicit color.</summary>
    public Color SeriesColor { get; }

    /// <summary>Physical pixels per device-independent unit (1.0 at 96 DPI).</summary>
    public double DevicePixelRatio { get; }
}
