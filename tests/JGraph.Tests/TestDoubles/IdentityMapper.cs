using JGraph.Core.Primitives;

namespace JGraph.Tests.TestDoubles;

/// <summary>A coordinate mapper that treats data coordinates as device pixels (Y not flipped).</summary>
internal sealed class IdentityMapper : ICoordinateMapper
{
    public IdentityMapper(Rect2D area) => PlotArea = area;

    public IdentityMapper()
        : this(new Rect2D(0, 0, 100, 100))
    {
    }

    public Rect2D PlotArea { get; }

    public Point2D DataToPixel(double x, double y) => new(x, y);

    public Point2D PixelToData(double px, double py) => new(px, py);
}
