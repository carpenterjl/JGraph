using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// The abstract drawing surface that all JGraph rendering targets a specific backend through. A
/// concrete implementation exists per engine (SkiaSharp today; SVG, PDF, or a GPU surface later).
/// Coordinates are in device space with Y growing downward. This is the single seam that keeps the
/// object model and the figure renderer independent of any particular graphics library.
/// </summary>
public interface IRenderContext
{
    /// <summary>The size of the drawable surface in device-independent units.</summary>
    Size2D Size { get; }

    /// <summary>Fills the entire surface with a color.</summary>
    void Clear(Color color);

    /// <summary>Restricts subsequent drawing to <paramref name="rect"/> until the matching <see cref="PopClip"/>.</summary>
    void PushClip(Rect2D rect);

    /// <summary>Removes the most recently pushed clip rectangle.</summary>
    void PopClip();

    /// <summary>Draws a single straight line segment.</summary>
    void DrawLine(Point2D a, Point2D b, LineStyle style);

    /// <summary>Draws a connected polyline through the given points.</summary>
    void DrawPolyline(ReadOnlySpan<Point2D> points, LineStyle style);

    /// <summary>Draws a rectangle with an optional stroke and/or fill.</summary>
    void DrawRectangle(Rect2D rect, LineStyle? stroke, Color? fill);

    /// <summary>Draws a closed polygon with an optional stroke and/or fill.</summary>
    void DrawPolygon(ReadOnlySpan<Point2D> points, LineStyle? stroke, Color? fill);

    /// <summary>
    /// Draws a marker glyph centered at each point. <paramref name="seriesColor"/> supplies the edge
    /// (and fill, for filled markers) color when the marker style leaves them unset.
    /// </summary>
    void DrawMarkers(ReadOnlySpan<Point2D> points, MarkerStyle style, Color seriesColor);

    /// <summary>
    /// Draws a rectangular raster image scaled to fill <paramref name="destination"/>. Pixels are
    /// row-major, top row first, each packed as 0xAARRGGBB (straight, non-premultiplied alpha). This
    /// is the single primitive image/heatmap plots need; vector backends embed it as a raster tile.
    /// </summary>
    /// <param name="pixelsArgb">The image pixels (length must be at least <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>).</param>
    /// <param name="pixelWidth">The image width in pixels.</param>
    /// <param name="pixelHeight">The image height in pixels.</param>
    /// <param name="destination">The device-space rectangle to draw the image into.</param>
    /// <param name="interpolate">When true, samples the image bilinearly (smooth); when false, nearest-neighbor (crisp cells).</param>
    void DrawImage(
        ReadOnlySpan<uint> pixelsArgb,
        int pixelWidth,
        int pixelHeight,
        Rect2D destination,
        bool interpolate = false);

    /// <summary>Draws a text run anchored at <paramref name="position"/> with the given alignment and rotation.</summary>
    void DrawText(
        string text,
        Point2D position,
        TextStyle style,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Baseline,
        double rotationDegrees = 0);

    /// <summary>Measures the pixel size of a text run in the given style.</summary>
    Size2D MeasureText(string text, TextStyle style);
}
