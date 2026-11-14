using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// The axes' mapper seen through an hgtransform's matrix: a member of a transform keeps its own data
/// and is drawn where the matrix puts it, as in R2025b (open item 296, ADR 0360). In a flat axes a
/// point is its (x, y) at height zero, so only the matrix's x and y rows and its x, y and offset
/// columns take part.
/// </summary>
internal sealed class GroupMatrixMapper : ICoordinateMapper
{
    private readonly ICoordinateMapper _inner;
    private readonly double[,] _m;

    public GroupMatrixMapper(ICoordinateMapper inner, double[,] matrix)
    {
        _inner = inner;
        _m = matrix;
    }

    public Rect2D PlotArea => _inner.PlotArea;

    /// <summary>
    /// A column still stands for a range of x only while the matrix keeps x apart from y and does not
    /// turn it round; a rotation or a mirror makes the reduction before mapping wrong.
    /// </summary>
    public bool ColumnsAreXRanges =>
        _inner.ColumnsAreXRanges && _m[0, 1] == 0 && _m[1, 0] == 0 && _m[0, 0] > 0
        && _m[3, 0] == 0 && _m[3, 1] == 0 && _m[3, 2] == 0;

    public Point2D DataToPixel(double x, double y)
    {
        (double mx, double my, _) = GraphicsGroupModel.Apply(_m, x, y, 0);
        return _inner.DataToPixel(mx, my);
    }

    public Point2D PixelToData(double px, double py)
    {
        // The matrix's flat part undone: a 2-by-2 and an offset. A matrix that flattens the plane has
        // no inverse, and the point is read back unmoved.
        Point2D moved = _inner.PixelToData(px, py);
        double a = _m[0, 0], b = _m[0, 1], c = _m[1, 0], d = _m[1, 1];
        double det = (a * d) - (b * c);
        if (det == 0 || double.IsNaN(det))
        {
            return moved;
        }

        double u = moved.X - _m[0, 3];
        double v = moved.Y - _m[1, 3];
        return new Point2D(((d * u) - (b * v)) / det, ((a * v) - (c * u)) / det);
    }
}
