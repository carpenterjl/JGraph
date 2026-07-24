using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Tests.TestDoubles;

/// <summary>
/// An <see cref="IRenderContext"/> that records call counts instead of drawing, with deterministic
/// text measurement. It lets the figure renderer and plot objects be exercised headlessly (no Skia,
/// no window) in unit tests.
/// </summary>
internal sealed class RecordingRenderContext : IRenderContext
{
    public RecordingRenderContext(Size2D size) => Size = size;

    public Size2D Size { get; }

    public int ClearCount { get; private set; }

    public int PolylineCount { get; private set; }

    public int RectangleCount { get; private set; }

    public int PolygonCount { get; private set; }

    public int LineCount { get; private set; }

    public int TextCount { get; private set; }

    public int MarkerBatchCount { get; private set; }

    public int TotalMarkerPoints { get; private set; }

    public int ImageCount { get; private set; }

    public Rect2D LastImageDestination { get; private set; }

    public int ClipDepth { get; private set; }

    public int MaxClipDepth { get; private set; }

    public void Clear(Color color) => ClearCount++;

    public void PushClip(Rect2D rect)
    {
        ClipDepth++;
        MaxClipDepth = System.Math.Max(MaxClipDepth, ClipDepth);
    }

    public void PopClip() => ClipDepth--;

    public void DrawLine(Point2D a, Point2D b, LineStyle style)
    {
        LineCount++;
        LineColors.Add(style.Color);
    }

    /// <summary>The stroke color of every line drawn — lets tests check legend swatch colors.</summary>
    public List<Color> LineColors { get; } = new();

    public void DrawPolyline(ReadOnlySpan<Point2D> points, LineStyle style) => PolylineCount++;

    public void DrawRectangle(Rect2D rect, LineStyle? stroke, Color? fill) => RectangleCount++;

    public void DrawPolygon(ReadOnlySpan<Point2D> points, LineStyle? stroke, Color? fill) => PolygonCount++;

    public void DrawMarkers(ReadOnlySpan<Point2D> points, MarkerStyle style, Color seriesColor)
    {
        MarkerBatchCount++;
        TotalMarkerPoints += points.Length;
    }

    public void DrawImage(
        ReadOnlySpan<uint> pixelsArgb,
        int pixelWidth,
        int pixelHeight,
        Rect2D destination,
        bool interpolate = false)
    {
        ImageCount++;
        LastImageDestination = destination;
    }

    public void DrawText(
        string text,
        Point2D position,
        TextStyle style,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Baseline,
        double rotationDegrees = 0)
    {
        TextCount++;
        Texts.Add(text);
    }

    /// <summary>Every string drawn, in draw order — lets tests assert on legend rows and labels.</summary>
    public List<string> Texts { get; } = new();

    public Size2D MeasureText(string text, TextStyle style) =>
        new(text.Length * style.FontSize * 0.5, style.FontSize * 1.2);
}
