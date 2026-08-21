namespace JGraph.Core.Drawing;

/// <summary>
/// One entry of an axes' line-style order (MATLAB <c>LineStyleOrder</c>): the dash pattern and
/// marker a series takes when its slot in the cycle comes up. The cycle advances only after the
/// color order wraps, so with seven colors the eighth auto-styled line reuses color one with the
/// second entry here.
/// </summary>
public readonly record struct SeriesLineStyle(DashStyle Dash, MarkerType Marker)
{
    /// <summary>The default single-entry order: a solid, markerless line.</summary>
    public static SeriesLineStyle Solid => new(DashStyle.Solid, MarkerType.None);
}
