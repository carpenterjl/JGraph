namespace JGraph.Core.Primitives;

/// <summary>
/// Maps normalized [0, 1] coordinates onto a device-space rectangle: (0, 0) is the rectangle's
/// top-left corner and (1, 1) its bottom-right, matching the convention of
/// <c>AxesModel.NormalizedBounds</c>. Used for figure-space annotations, which are positioned as
/// fractions of the figure so they stay put when axes zoom or pan.
/// </summary>
public sealed class NormalizedCoordinateMapper : ICoordinateMapper
{
    private readonly Rect2D _rect;

    public NormalizedCoordinateMapper(Rect2D pixelRect) => _rect = pixelRect;

    /// <inheritdoc />
    public Rect2D PlotArea => _rect;

    /// <inheritdoc />
    public Point2D DataToPixel(double x, double y) =>
        new(_rect.X + (x * _rect.Width), _rect.Y + (y * _rect.Height));

    /// <inheritdoc />
    public Point2D PixelToData(double px, double py) => new(
        _rect.Width > 0 ? (px - _rect.X) / _rect.Width : 0,
        _rect.Height > 0 ? (py - _rect.Y) / _rect.Height : 0);
}
