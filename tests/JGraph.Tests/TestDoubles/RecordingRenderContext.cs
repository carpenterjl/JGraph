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
        Lines.Add((a, b, style));
    }

    /// <summary>Every straight line drawn, endpoints and all — how a span across the axes is checked.</summary>
    public List<(Point2D From, Point2D To, LineStyle Style)> Lines { get; } = new();

    /// <summary>The stroke color of every line drawn — lets tests check legend swatch colors.</summary>
    public List<Color> LineColors { get; } = new();

    public void DrawPolyline(ReadOnlySpan<Point2D> points, LineStyle style)
    {
        PolylineCount++;
        PolylineColors.Add(style.Color);
    }

    /// <summary>The stroke color of each polyline, in draw order — which is how a series' color is checked.</summary>
    public List<Color> PolylineColors { get; } = new();

    public void DrawRectangle(Rect2D rect, LineStyle? stroke, Color? fill) => RectangleCount++;

    public void DrawPolygon(ReadOnlySpan<Point2D> points, LineStyle? stroke, Color? fill)
    {
        PolygonCount++;
        PolygonFills.Add(fill);
        PolygonStrokes.Add(stroke);

        double sum = 0;
        foreach (Point2D p in points)
        {
            sum += p.Y;
        }

        PolygonMeanY.Add(points.Length == 0 ? 0 : sum / points.Length);
    }

    /// <summary>The fill of every polygon drawn, in draw order — lets tests assert painter ordering.</summary>
    public List<Color?> PolygonFills { get; } = new();

    /// <summary>The stroke of every polygon drawn, in draw order.</summary>
    public List<LineStyle?> PolygonStrokes { get; } = new();

    /// <summary>
    /// The mean device Y of every polygon, in draw order. Y grows downward and an elevated camera puts
    /// far geometry higher on screen, so this reads directly on back-to-front ordering.
    /// </summary>
    public List<double> PolygonMeanY { get; } = new();

    public void DrawMarkers(ReadOnlySpan<Point2D> points, MarkerStyle style, Color seriesColor)
    {
        MarkerBatchCount++;
        TotalMarkerPoints += points.Length;
        MarkerPoints.AddRange(points.ToArray());
        MarkerStyles.Add(style);
    }

    /// <summary>Every marker position, in draw order — which is painter order for a 3D scatter.</summary>
    public List<Point2D> MarkerPoints { get; } = new();

    /// <summary>The style of each marker batch, in draw order.</summary>
    public List<MarkerStyle> MarkerStyles { get; } = new();

    /// <summary>How many <see cref="DrawTriangles"/> batches were issued — the surface's draw-call count.</summary>
    public int TriangleBatchCount { get; private set; }

    /// <summary>Total vertices across every triangle batch: six per surface cell.</summary>
    public int TotalTriangleVertices { get; private set; }

    /// <summary>
    /// The mean device Y of every triangle batch, in draw order. Y grows downward and an elevated
    /// camera puts the far side of a surface higher up the screen, so this is a direct read on
    /// whether the batches really are ordered back to front.
    /// </summary>
    public List<double> TriangleBatchMeanY { get; } = new();

    public void DrawTriangles(ReadOnlySpan<Point2D> vertices, ReadOnlySpan<uint> colorsArgb)
    {
        TriangleBatchCount++;
        TotalTriangleVertices += vertices.Length;
        TriangleColors.AddRange(colorsArgb.ToArray());

        double sum = 0;
        foreach (Point2D p in vertices)
        {
            sum += p.Y;
        }

        TriangleBatchMeanY.Add(vertices.Length == 0 ? 0 : sum / vertices.Length);
    }

    /// <summary>Every vertex color drawn, in draw order.</summary>
    public List<uint> TriangleColors { get; } = new();

    /// <summary>How many <see cref="DrawPaths"/> batches were issued.</summary>
    public int PathBatchCount { get; private set; }

    /// <summary>Total sub-paths across every batch — grid edges, contour bands, and so on.</summary>
    public int TotalSubpaths { get; private set; }

    /// <summary>Total vertices across every path batch.</summary>
    public int TotalPathVertices { get; private set; }

    /// <summary>The stroke handed to the most recent <see cref="DrawPaths"/> call, if any.</summary>
    public LineStyle? LastPathStroke { get; private set; }

    public void DrawPaths(
        ReadOnlySpan<Point2D> vertices,
        ReadOnlySpan<int> starts,
        bool closed,
        LineStyle? stroke,
        Color? fill)
    {
        PathBatchCount++;
        TotalSubpaths += starts.Length;
        TotalPathVertices += vertices.Length;
        LastPathStroke = stroke;
        PathFills.Add(fill);

        double sum = 0;
        foreach (Point2D p in vertices)
        {
            sum += p.Y;
        }

        PathMeanY.Add(vertices.Length == 0 ? 0 : sum / vertices.Length);
    }

    /// <summary>The fill of every path batch, in draw order.</summary>
    public List<Color?> PathFills { get; } = new();

    /// <summary>
    /// The mean Y of every path batch, in draw order — enough to tell one batch's screen height from
    /// another's, which is how a contour lifted to its own level is distinguished from a flat one.
    /// </summary>
    public List<double> PathMeanY { get; } = new();

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
        TextPositions.Add(position);
        TextStyles.Add(style);
    }

    /// <summary>Every string drawn, in draw order — lets tests assert on legend rows and labels.</summary>
    public List<string> Texts { get; } = new();

    /// <summary>Where each string was anchored, parallel to <see cref="Texts"/>.</summary>
    public List<Point2D> TextPositions { get; } = new();

    /// <summary>The style of each string, parallel to <see cref="Texts"/> — how a dimmed label is told apart.</summary>
    public List<TextStyle> TextStyles { get; } = new();

    public Size2D MeasureText(string text, TextStyle style) =>
        new(text.Length * style.FontSize * 0.5, style.FontSize * 1.2);
}
