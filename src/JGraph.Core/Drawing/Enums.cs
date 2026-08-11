namespace JGraph.Core.Drawing;

/// <summary>Dash pattern for stroked lines.</summary>
public enum DashStyle
{
    Solid,
    Dash,
    Dot,
    DashDot,
    DashDotDot,

    /// <summary>The line is not drawn at all (used to render markers-only series).</summary>
    None,
}

/// <summary>Marker glyph drawn at data points.</summary>
public enum MarkerType
{
    None,
    Circle,
    Square,
    Diamond,
    TriangleUp,
    TriangleDown,
    Plus,
    Cross,
    Star,
    Point,
}

/// <summary>
/// How a line joins its samples: straight across, or as a stairstep whose tread sits after, before,
/// or centered on each sample. Stepping changes only the path drawn between samples — the samples
/// themselves, and so the markers and the data bounds, are unmoved.
/// </summary>
public enum StepMode
{
    /// <summary>Straight segments from sample to sample.</summary>
    None,

    /// <summary>The value is held forward, changing at the next sample (MATLAB <c>stairs</c>).</summary>
    Post,

    /// <summary>The value is held back, changing at this sample.</summary>
    Pre,

    /// <summary>The value changes halfway between neighbouring samples.</summary>
    Mid,
}

/// <summary>Line cap style for stroke endpoints.</summary>
public enum LineCap
{
    Butt,
    Round,
    Square,
}

/// <summary>Line join style for polyline vertices.</summary>
public enum LineJoin
{
    Miter,
    Round,
    Bevel,
}

/// <summary>Horizontal text/anchor alignment.</summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Vertical text/anchor alignment.</summary>
public enum VerticalAlignment
{
    Top,
    Middle,
    Bottom,

    /// <summary>Aligns to the text baseline rather than the cell top/bottom.</summary>
    Baseline,
}
