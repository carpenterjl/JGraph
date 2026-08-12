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
