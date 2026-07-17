using JGraph.Core.Drawing;

namespace JGraph.Serialization.Dto;

/// <summary>A 2D point in the document format.</summary>
public sealed record PointDto(double X, double Y);

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

/// <summary>A 2D data series as parallel X/Y arrays.</summary>
public sealed record SeriesDto(double[] Xs, double[] Ys);

/// <summary>A named colormap defined by its evenly spaced color stops.</summary>
public sealed record ColormapDto(string Name, Color[] Stops);
