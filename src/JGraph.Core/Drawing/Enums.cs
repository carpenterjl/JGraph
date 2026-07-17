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
