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
