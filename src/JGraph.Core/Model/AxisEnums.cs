namespace JGraph.Core.Model;

/// <summary>The physical orientation of an axis within its axes.</summary>
public enum AxisOrientation
{
    /// <summary>A horizontal axis mapping data to the X (pixel) direction.</summary>
    Horizontal,

    /// <summary>A vertical axis mapping data to the Y (pixel) direction.</summary>
    Vertical,
}

/// <summary>Which edge of the plot region an axis is anchored to.</summary>
public enum AxisPosition
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Which compass point a polar axes puts θ = 0 at (MATLAB <c>ThetaZeroLocation</c>). The default is
/// the right-hand side, which is where the mathematical convention puts it; a wind rose or a bearing
/// chart wants it at the top.
/// </summary>
public enum ThetaZeroLocation
{
    Right,
    Top,
    Left,
    Bottom,
}

/// <summary>
/// Which way θ increases on a polar axes (MATLAB <c>ThetaDirection</c>). Anticlockwise is the
/// mathematical convention and the default; compass bearings run the other way.
/// </summary>
public enum ThetaDirection
{
    CounterClockwise,
    Clockwise,
}

/// <summary>
/// The unit a polar axes reads and writes angles in (MATLAB <c>ThetaAxisUnits</c>). This is about the
/// numbers a script hands over and gets back, not about how the chart is drawn: <c>polarplot</c> takes
/// radians as MATLAB does, while the θ ruler's ticks and limits are spoken in degrees.
/// </summary>
public enum AngleUnits
{
    Degrees,
    Radians,
}

/// <summary>
/// The scale (data-to-linear mapping) applied by an axis. Additional scales (date/time, category,
/// symmetric-log) are planned; the linear and logarithmic transforms are implemented today.
/// </summary>
public enum AxisScaleType
{
    Linear,
    Logarithmic,

    /// <summary>Reserved: maps <see cref="DateTime"/> ticks to a linear axis. Not yet implemented.</summary>
    DateTime,

    /// <summary>Reserved: maps discrete categories to evenly spaced positions. Not yet implemented.</summary>
    Category,
}

/// <summary>
/// Whether the grid (and, with it, the tick marks) is drawn under or over the plotted content
/// (MATLAB <c>Layer</c>). Under is the default everywhere; over is what keeps a grid readable
/// across a filled surface or image.
/// </summary>
public enum AxesLayer
{
    Bottom,
    Top,
}

/// <summary>
/// How much of the 3D coordinate box is outlined (MATLAB <c>BoxStyle</c>): only the three far
/// faces' edges, or the full twelve-edge box.
/// </summary>
public enum Box3DStyle
{
    Back,
    Full,
}

/// <summary>Which side of the axis line the tick marks grow from (MATLAB <c>TickDir</c>).</summary>
public enum TickDirection
{
    In,
    Out,
    Both,
}

/// <summary>How colormap values are spread over the color limits (MATLAB <c>ColorScale</c>).</summary>
public enum ColorScaleType
{
    Linear,
    Log,
}

/// <summary>Where the axes title sits over the plot area (MATLAB <c>TitleHorizontalAlignment</c>).</summary>
public enum TitleHorizontalAlignment
{
    Center,
    Left,
    Right,
}

/// <summary>
/// How an auto-scaling ruler turns its data extent into limits (MATLAB <c>XLimitMethod</c>).
/// Padded — the JGraph default, which every existing figure was fitted under — leaves a margin of
/// <see cref="AxesModel.AutoScalePadding"/> around the data; Tight puts the limits exactly at the
/// data; Tickaligned pushes them outward to round numbers a tick would land on.
/// </summary>
public enum LimitMethod
{
    Padded,
    Tight,
    Tickaligned,
}

/// <summary>
/// Whether the 3D camera projects along parallel rays or through a viewpoint (MATLAB
/// <c>Projection</c>). Orthographic is what every 3D axes drew before M74; perspective divides by
/// the distance from the camera, so near faces grow and parallel edges converge.
/// </summary>
public enum ProjectionType
{
    Orthographic,
    Perspective,
}

/// <summary>
/// How a 3D axes orders the faces it paints (MATLAB <c>SortMethod</c>). Depth sorts each object's
/// own faces back to front, which is what JGraph has always done; ChildOrder paints them in the
/// order they were created, which is faster and lets a script decide the order itself.
/// </summary>
public enum SortMethodType
{
    Depth,
    ChildOrder,
}
