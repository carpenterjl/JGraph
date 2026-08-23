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

/// <summary>
/// How a marker chart spreads points that would otherwise sit on top of one another (MATLAB's
/// <c>XJitter</c>/<c>YJitter</c>). The spread is a drawing offset only: the data a point carries, and
/// so what a script reads back out of it, is the position it was given.
/// </summary>
public enum JitterStyle
{
    /// <summary>No spread — every point is drawn where its data puts it.</summary>
    None,

    /// <summary>
    /// Spread by how many neighbours share the value, so a crowd fans out into a shape whose width
    /// is its own histogram (MATLAB <c>swarmchart</c>'s default).
    /// </summary>
    Density,

    /// <summary>Spread evenly at random across the jitter width.</summary>
    Rand,

    /// <summary>Spread at random about the centre, thinning out toward the edges of the width.</summary>
    Randn,
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

/// <summary>
/// How per-element transparency numbers are read: MATLAB's <c>AlphaDataMapping</c>. Scaled stretches
/// them over the axes' alpha limits and looks them up in its alphamap; direct indexes the map with
/// them as they stand; none takes them as opacities already, between 0 and 1.
/// </summary>
public enum AlphaMapping
{
    /// <summary>The numbers are opacities in [0, 1] and nothing is looked up.</summary>
    None,

    /// <summary>Stretched over ALim and looked up in the alphamap — MATLAB's default.</summary>
    Scaled,

    /// <summary>Used as indices into the alphamap without scaling.</summary>
    Direct,
}

/// <summary>
/// How the numbers behind a colour-mapped chart are read: MATLAB's <c>CDataMapping</c>. Scaled
/// stretches them over the axes' colour limits; direct indexes the colormap with them as they stand,
/// counting from one.
/// </summary>
public enum ColorMapping
{
    /// <summary>Stretched over CLim and looked up in the colormap — MATLAB's default for a surface.</summary>
    Scaled,

    /// <summary>Used as indices into the colormap without scaling, counting from one.</summary>
    Direct,
}

/// <summary>Which of a surface's mesh lines are drawn: MATLAB's <c>MeshStyle</c>.</summary>
public enum SurfaceMeshStyle
{
    /// <summary>Both the lines along the rows and the lines down the columns.</summary>
    Both,

    /// <summary>Only the lines running along each row.</summary>
    Row,

    /// <summary>Only the lines running down each column.</summary>
    Column,
}

/// <summary>
/// How a face turned away from the viewer is lit: MATLAB's <c>BackFaceLighting</c>. It only shows on
/// a surface you can see the inside of — a translucent one, or one that folds over itself.
/// </summary>
public enum BackFaceLighting
{
    /// <summary>The normal is flipped so the far face is lit as if it faced you — MATLAB's default.</summary>
    ReverseLit,

    /// <summary>The far face takes only its ambient colour.</summary>
    Unlit,

    /// <summary>The normal is used as it stands, so a far face is lit from behind and reads dark.</summary>
    Lit,
}
