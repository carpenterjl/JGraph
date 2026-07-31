using JGraph.Core.Drawing;

namespace JGraph.Serialization.Dto;

/// <summary>A 2D point in the document format.</summary>
public sealed record PointDto(double X, double Y);

/// <summary>A 3D point or direction in the document format.</summary>
public sealed record Point3Dto(double X, double Y, double Z);

/// <summary>A closed numeric interval [Min, Max].</summary>
public sealed record RangeDto(double Min, double Max);

/// <summary>A rectangle by top-left corner and size (used for normalized axes bounds).</summary>
public sealed record RectDto(double X, double Y, double Width, double Height);

/// <summary>A width/height pair (used for the figure size).</summary>
public sealed record SizeDto(double Width, double Height);

/// <summary>A stroked-line style.</summary>
public sealed record LineStyleDto(Color Color, double Width, DashStyle Dash, LineCap Cap, LineJoin Join);

/// <summary>A text style.</summary>
public sealed record TextStyleDto(Color Color, double FontSize, string FontFamily, bool Bold, bool Italic);

/// <summary>
/// A 2D data series. Small series store parallel X/Y arrays as readable JSON numbers; large series
/// (format v4+) store the raw little-endian IEEE-754 doubles as base64 blocks instead — roughly 4x
/// smaller than digit lists and decoded with one bulk copy. Readers accept either shape (packed
/// wins when both are present, which writers never produce).
/// </summary>
public sealed record SeriesDto(
    double[]? Xs,
    double[]? Ys,
    byte[]? XsPacked = null,
    byte[]? YsPacked = null,
    int Count = 0);

/// <summary>
/// A named colormap defined by its evenly spaced color stops. <paramref name="Discrete"/> marks a
/// palette whose stops are used unblended, one bin each, rather than interpolated; it is optional so
/// that documents written before it existed still read back.
/// </summary>
public sealed record ColormapDto(string Name, Color[] Stops, bool Discrete = false);
