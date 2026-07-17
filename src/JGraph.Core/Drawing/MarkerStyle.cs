namespace JGraph.Core.Drawing;

/// <summary>
/// An immutable description of how point markers are drawn. A null <see cref="Fill"/> yields an
/// open (unfilled) marker whose interior shows the background through it.
/// </summary>
public readonly struct MarkerStyle
{
    public MarkerStyle(
        MarkerType type,
        double size = 6.0,
        Color? fill = null,
        Color? edge = null,
        double edgeWidth = 1.0)
    {
        Type = type;
        Size = size;
        Fill = fill;
        Edge = edge;
        EdgeWidth = edgeWidth;
    }

    public MarkerType Type { get; }

    /// <summary>Marker diameter in device-independent units.</summary>
    public double Size { get; }

    /// <summary>Interior fill color, or null for an open marker.</summary>
    public Color? Fill { get; }

    /// <summary>Outline color, or null to use the series color at render time.</summary>
    public Color? Edge { get; }

    public double EdgeWidth { get; }

    public bool IsVisible => Type != MarkerType.None && Size > 0;

    public static MarkerStyle None => new(MarkerType.None);

    public MarkerStyle WithType(MarkerType type) => new(type, Size, Fill, Edge, EdgeWidth);

    public MarkerStyle WithSize(double size) => new(Type, size, Fill, Edge, EdgeWidth);

    public MarkerStyle WithFill(Color? fill) => new(Type, Size, fill, Edge, EdgeWidth);

    public MarkerStyle WithEdge(Color? edge) => new(Type, Size, Fill, edge, EdgeWidth);
}
