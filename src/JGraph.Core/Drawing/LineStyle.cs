namespace JGraph.Core.Drawing;

/// <summary>
/// An immutable description of how a stroked line is drawn. Assembled on demand by plot objects
/// from their individual editable properties and passed to the rendering backend.
/// </summary>
public readonly struct LineStyle
{
    public LineStyle(
        Color color,
        double width = 1.0,
        DashStyle dash = DashStyle.Solid,
        LineCap cap = LineCap.Butt,
        LineJoin join = LineJoin.Miter)
    {
        Color = color;
        Width = width;
        Dash = dash;
        Cap = cap;
        Join = join;
    }

    public Color Color { get; }

    public double Width { get; }

    public DashStyle Dash { get; }

    public LineCap Cap { get; }

    public LineJoin Join { get; }

    /// <summary>True when this style would produce visible output.</summary>
    public bool IsVisible => Dash != DashStyle.None && Width > 0 && !Color.IsTransparent;

    public static LineStyle Default => new(Colors.Black);

    public LineStyle WithColor(Color color) => new(color, Width, Dash, Cap, Join);

    public LineStyle WithWidth(double width) => new(Color, width, Dash, Cap, Join);

    public LineStyle WithDash(DashStyle dash) => new(Color, Width, dash, Cap, Join);

    /// <summary>
    /// Returns the on/off dash segment lengths (in multiples of line width) for this dash style,
    /// or an empty span for a solid line. Rendering backends scale these by <see cref="Width"/>.
    /// </summary>
    public ReadOnlySpan<float> GetDashPattern() => Dash switch
    {
        DashStyle.Dash => new[] { 4f, 3f },
        DashStyle.Dot => new[] { 1f, 2f },
        DashStyle.DashDot => new[] { 4f, 2f, 1f, 2f },
        DashStyle.DashDotDot => new[] { 4f, 2f, 1f, 2f, 1f, 2f },
        _ => ReadOnlySpan<float>.Empty,
    };
}
