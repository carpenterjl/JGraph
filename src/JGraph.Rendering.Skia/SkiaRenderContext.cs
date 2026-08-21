using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using SkiaSharp;

namespace JGraph.Rendering.Skia;

/// <summary>
/// SkiaSharp implementation of <see cref="IRenderContext"/>. It wraps an <see cref="SKCanvas"/> and
/// translates JGraph's engine-independent primitives into Skia draw calls. Reusable paints, fonts,
/// typefaces, and a point buffer are cached for the lifetime of the context to keep per-frame
/// allocations low. Instances are cheap and intended to be created per paint pass.
/// </summary>
public sealed class SkiaRenderContext : IRenderContext, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly SKPaint _stroke;
    private readonly SKPaint _fill;
    private readonly SKPaint _text;
    private readonly SKPaint _mesh;
    private readonly bool _flattenDashes;
    private readonly bool _supportsMeshes;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaces = new();

    private SKPoint[] _pointBuffer = new SKPoint[256];

    // DrawVertices takes exactly-sized arrays in SkiaSharp 2.88, so an oversized buffer cannot be
    // reused and batch-to-batch size changes would allocate on every call. Batches are therefore
    // padded up to a power-of-two triangle count and one buffer pair is kept per size class; a
    // surface cycles through only a handful of classes, so after the first frame nothing allocates.
    private readonly SKPoint[]?[] _meshPoints = new SKPoint[24][];
    private readonly SKColor[]?[] _meshColors = new SKColor[24][];

    // Reused across DrawPolyline and DrawPolygon: rebuilding the geometry is unavoidable, but
    // reallocating a native SKPath per call per frame is not (a figure redraws every polyline on
    // every pan, and a 3D surface draws one polygon per grid cell). Neither method reenters the
    // other, and DrawDashFlattened builds its own path, so a single scratch path is safe.
    private readonly SKPath _scratchPath = new();

    /// <param name="canvas">The Skia canvas to draw onto (raster, SVG, or PDF page).</param>
    /// <param name="size">The drawable size in device-independent units.</param>
    /// <param name="devicePixelRatio">Physical pixels per device-independent unit.</param>
    /// <param name="flattenDashes">
    /// Converts dashed strokes into explicit segment geometry instead of a Skia dash path effect.
    /// Skia's SVG backend drops dash path effects (drawing them solid), so the SVG exporter enables
    /// this; raster and PDF targets keep the faster path effect.
    /// </param>
    /// <param name="supportsMeshes">
    /// Whether the canvas can rasterize <see cref="DrawTriangles"/> as a vertex mesh. Skia's SVG and
    /// PDF backends drop <c>DrawVertices</c> silently — the geometry simply never appears in the
    /// file — so those exporters pass false and get one filled path per triangle instead.
    /// </param>
    public SkiaRenderContext(
        SKCanvas canvas,
        Size2D size,
        double devicePixelRatio = 1.0,
        bool flattenDashes = false,
        bool supportsMeshes = true)
    {
        _canvas = canvas;
        Size = size;
        DevicePixelRatio = devicePixelRatio;
        _flattenDashes = flattenDashes;
        _supportsMeshes = supportsMeshes;

        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _text = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, SubpixelText = true };

        // Its own paint, not _fill: DrawRectangle/DrawPolygon/DrawMarkers overwrite that one's color
        // and path effect, and a mesh draw must not inherit either.
        _mesh = new SKPaint { Style = SKPaintStyle.Fill };
    }

    /// <inheritdoc />
    public Size2D Size { get; }

    /// <summary>Physical pixels per device-independent unit for this surface.</summary>
    public double DevicePixelRatio { get; }

    /// <inheritdoc />
    public void Clear(Color color) => _canvas.Clear(ToSk(color));

    /// <inheritdoc />
    public void PushClip(Rect2D rect)
    {
        _canvas.Save();
        _canvas.ClipRect(ToSk(rect));
    }

    /// <inheritdoc />
    public void PopClip() => _canvas.Restore();

    /// <inheritdoc />
    public void DrawLine(Point2D a, Point2D b, LineStyle style)
    {
        if (!style.IsVisible)
        {
            return;
        }

        if (NeedsDashFlattening(style))
        {
            using var path = new SKPath();
            path.MoveTo((float)a.X, (float)a.Y);
            path.LineTo((float)b.X, (float)b.Y);
            DrawDashFlattened(path, style);
            return;
        }

        ConfigureStroke(style, out SKPathEffect? dash);
        _canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, _stroke);
        dash?.Dispose();
    }

    /// <inheritdoc />
    public void DrawPolyline(ReadOnlySpan<Point2D> points, LineStyle style)
    {
        if (points.Length < 2 || !style.IsVisible)
        {
            return;
        }

        int count = CopyToBuffer(points);
        SKPath path = _scratchPath;
        path.Rewind(); // keeps the native allocation, unlike Reset
        path.MoveTo(_pointBuffer[0]);
        for (int i = 1; i < count; i++)
        {
            path.LineTo(_pointBuffer[i]);
        }

        if (NeedsDashFlattening(style))
        {
            DrawDashFlattened(path, style);
            return;
        }

        ConfigureStroke(style, out SKPathEffect? dash);
        _canvas.DrawPath(path, _stroke);
        dash?.Dispose();
    }

    /// <inheritdoc />
    public void DrawRectangle(Rect2D rect, LineStyle? stroke, Color? fill)
    {
        SKRect skRect = ToSk(rect);
        if (fill is { } fillColor && !fillColor.IsTransparent)
        {
            _fill.Color = ToSk(fillColor);
            _canvas.DrawRect(skRect, _fill);
        }

        if (stroke is { } strokeStyle && strokeStyle.IsVisible)
        {
            if (NeedsDashFlattening(strokeStyle))
            {
                using var path = new SKPath();
                path.AddRect(skRect);
                DrawDashFlattened(path, strokeStyle);
                return;
            }

            ConfigureStroke(strokeStyle, out SKPathEffect? dash);
            _canvas.DrawRect(skRect, _stroke);
            dash?.Dispose();
        }
    }

    /// <inheritdoc />
    public void DrawPolygon(ReadOnlySpan<Point2D> points, LineStyle? stroke, Color? fill)
    {
        if (points.Length < 2)
        {
            return;
        }

        int count = CopyToBuffer(points);
        SKPath path = _scratchPath;
        path.Rewind();
        path.MoveTo(_pointBuffer[0]);
        for (int i = 1; i < count; i++)
        {
            path.LineTo(_pointBuffer[i]);
        }

        path.Close();

        if (fill is { } fillColor && !fillColor.IsTransparent)
        {
            _fill.Color = ToSk(fillColor);
            _canvas.DrawPath(path, _fill);
        }

        if (stroke is { } strokeStyle && strokeStyle.IsVisible)
        {
            if (NeedsDashFlattening(strokeStyle))
            {
                DrawDashFlattened(path, strokeStyle);
                return;
            }

            ConfigureStroke(strokeStyle, out SKPathEffect? dash);
            _canvas.DrawPath(path, _stroke);
            dash?.Dispose();
        }
    }

    /// <inheritdoc />
    public void DrawMarkers(ReadOnlySpan<Point2D> points, MarkerStyle style, Color seriesColor)
    {
        if (!style.IsVisible)
        {
            return;
        }

        float radius = (float)(style.Size / 2.0);
        Color edgeColor = style.Edge ?? seriesColor;
        Color? fillColor = style.Fill;

        _fill.Style = SKPaintStyle.Fill;
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.Color = ToSk(edgeColor);
        _stroke.StrokeWidth = (float)style.EdgeWidth;
        _stroke.PathEffect = null;
        if (fillColor is { } fc)
        {
            _fill.Color = ToSk(fc);
        }

        foreach (Point2D p in points)
        {
            DrawMarker(style.Type, (float)p.X, (float)p.Y, radius, fillColor.HasValue);
        }
    }

    /// <inheritdoc />
    public void DrawTriangles(ReadOnlySpan<Point2D> vertices, ReadOnlySpan<uint> colorsArgb)
    {
        int count = vertices.Length - (vertices.Length % 3);
        if (count == 0 || colorsArgb.Length < count)
        {
            return;
        }

        // The size-class table tops out well above any batch JGraph issues; a caller that somehow
        // exceeds it still draws, just through the slower per-triangle path.
        if (!_supportsMeshes || count / 3 > 1 << (_meshPoints.Length - 1))
        {
            DrawTrianglesAsPaths(vertices, colorsArgb, count);
            return;
        }

        (SKPoint[] points, SKColor[] colors) = MeshBuffers(count / 3);
        for (int i = 0; i < count; i++)
        {
            points[i] = new SKPoint((float)vertices[i].X, (float)vertices[i].Y);
            colors[i] = ToSk(colorsArgb[i]);
        }

        // The buffer is a whole size class, so the tail is padded with triangles that collapse to a
        // single transparent point: zero area and zero alpha, so Skia rasterizes nothing for them.
        for (int i = count; i < points.Length; i++)
        {
            points[i] = default;
            colors[i] = SKColor.Empty;
        }

        _canvas.DrawVertices(SKVertexMode.Triangles, points, colors, _mesh);
    }

    /// <inheritdoc />
    public void DrawPaths(
        ReadOnlySpan<Point2D> vertices,
        ReadOnlySpan<int> starts,
        bool closed,
        LineStyle? stroke,
        Color? fill)
    {
        if (starts.Length == 0 || vertices.Length < 2)
        {
            return;
        }

        int count = CopyToBuffer(vertices);
        SKPath path = _scratchPath;
        path.Rewind();

        // Fill type is sticky state on a reused path, and even-odd would cancel adjacent sub-paths
        // into holes instead of tiling them.
        path.FillType = SKPathFillType.Winding;

        for (int s = 0; s < starts.Length; s++)
        {
            int begin = starts[s];
            int end = s + 1 < starts.Length ? starts[s + 1] : count;
            if (begin < 0 || end > count || end - begin < 2)
            {
                continue;
            }

            path.MoveTo(_pointBuffer[begin]);
            for (int i = begin + 1; i < end; i++)
            {
                path.LineTo(_pointBuffer[i]);
            }

            if (closed)
            {
                path.Close();
            }
        }

        if (path.IsEmpty)
        {
            return;
        }

        if (fill is { } fillColor && !fillColor.IsTransparent)
        {
            _fill.Color = ToSk(fillColor);
            _canvas.DrawPath(path, _fill);
        }

        if (stroke is { } strokeStyle && strokeStyle.IsVisible)
        {
            if (NeedsDashFlattening(strokeStyle))
            {
                DrawDashFlattened(path, strokeStyle);
                return;
            }

            ConfigureStroke(strokeStyle, out SKPathEffect? dash);
            _canvas.DrawPath(path, _stroke);
            dash?.Dispose();
        }
    }

    /// <inheritdoc />
    public void DrawImage(
        ReadOnlySpan<uint> pixelsArgb,
        int pixelWidth,
        int pixelHeight,
        Rect2D destination,
        bool interpolate = false)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || pixelsArgb.Length < pixelWidth * pixelHeight)
        {
            return;
        }

        // Repack 0xAARRGGBB source pixels into Skia's BGRA byte order.
        int count = pixelWidth * pixelHeight;
        var bgra = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            uint c = pixelsArgb[i];
            int o = i * 4;
            bgra[o + 0] = (byte)c;          // B
            bgra[o + 1] = (byte)(c >> 8);   // G
            bgra[o + 2] = (byte)(c >> 16);  // R
            bgra[o + 3] = (byte)(c >> 24);  // A
        }

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKImage image = SKImage.FromPixelCopy(info, bgra);
        using var paint = new SKPaint
        {
            FilterQuality = interpolate ? SKFilterQuality.Medium : SKFilterQuality.None,
        };
        _canvas.DrawImage(image, ToSk(destination), paint);
    }

    /// <inheritdoc />
    public void DrawText(
        string text,
        Point2D position,
        TextStyle style,
        HorizontalAlignment horizontal = HorizontalAlignment.Left,
        VerticalAlignment vertical = VerticalAlignment.Baseline,
        double rotationDegrees = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Every text in a figure arrives here, which is why the markup is read here: a title, a tick
        // label, a legend entry and a text object are one call each, and one call each is what the
        // interpreter has to reach for a label written with \sigma to read as one wherever it sits.
        text = TexMarkup.Render(text, style.Interpreter);

        ConfigureFont(style);
        _text.Color = ToSk(style.Color);

        float width = _text.MeasureText(text);
        SKFontMetrics metrics = _text.FontMetrics;

        float dx = horizontal switch
        {
            HorizontalAlignment.Center => -width / 2f,
            HorizontalAlignment.Right => -width,
            _ => 0f,
        };

        float dy = vertical switch
        {
            VerticalAlignment.Top => -metrics.Ascent,
            VerticalAlignment.Middle => -(metrics.Ascent + metrics.Descent) / 2f,
            VerticalAlignment.Bottom => -metrics.Descent,
            _ => 0f, // Baseline
        };

        bool rotated = System.Math.Abs(rotationDegrees) > 1e-6;
        if (rotated)
        {
            _canvas.Save();
            _canvas.Translate((float)position.X, (float)position.Y);
            _canvas.RotateDegrees((float)rotationDegrees);
            _canvas.DrawText(text, dx, dy, _text);
            _canvas.Restore();
        }
        else
        {
            _canvas.DrawText(text, (float)position.X + dx, (float)position.Y + dy, _text);
        }
    }

    /// <inheritdoc />
    public Size2D MeasureText(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size2D.Empty;
        }

        // Measured as it will be drawn, or every layout that reserves room for a label would reserve
        // room for the markup instead of for the symbols.
        text = TexMarkup.Render(text, style.Interpreter);

        ConfigureFont(style);
        float width = _text.MeasureText(text);
        SKFontMetrics metrics = _text.FontMetrics;
        return new Size2D(width, metrics.Descent - metrics.Ascent);
    }

    public void Dispose()
    {
        _scratchPath.Dispose();
        _stroke.Dispose();
        _fill.Dispose();
        _text.Dispose();
        _mesh.Dispose();
        foreach (SKTypeface typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _typefaces.Clear();
    }

    /// <summary>
    /// The vertex/color buffer pair for a batch of <paramref name="triangles"/>, rounded up to a
    /// power-of-two size class so repeated batches of similar size share one allocation.
    /// </summary>
    private (SKPoint[] Points, SKColor[] Colors) MeshBuffers(int triangles)
    {
        int slot = 0;
        int capacity = 1;
        while (capacity < triangles)
        {
            capacity <<= 1;
            slot++;
        }

        SKPoint[]? points = _meshPoints[slot];
        if (points is null)
        {
            points = new SKPoint[capacity * 3];
            _meshPoints[slot] = points;
            _meshColors[slot] = new SKColor[capacity * 3];
        }

        return (points, _meshColors[slot]!);
    }

    /// <summary>
    /// The vector-backend path for <see cref="DrawTriangles"/>: one filled path per triangle, in the
    /// mean of its vertex colors. Exact for flat-shaded facets, an approximation for interpolated
    /// ones, and the only option on a canvas whose backend discards vertex meshes.
    /// </summary>
    private void DrawTrianglesAsPaths(ReadOnlySpan<Point2D> vertices, ReadOnlySpan<uint> colorsArgb, int count)
    {
        SKPath path = _scratchPath;
        _fill.PathEffect = null;
        for (int i = 0; i < count; i += 3)
        {
            uint a = colorsArgb[i];
            uint b = colorsArgb[i + 1];
            uint c = colorsArgb[i + 2];
            byte alpha = (byte)((((a >> 24) & 0xFF) + ((b >> 24) & 0xFF) + ((c >> 24) & 0xFF)) / 3);
            if (alpha == 0)
            {
                continue;
            }

            path.Rewind();
            path.MoveTo((float)vertices[i].X, (float)vertices[i].Y);
            path.LineTo((float)vertices[i + 1].X, (float)vertices[i + 1].Y);
            path.LineTo((float)vertices[i + 2].X, (float)vertices[i + 2].Y);
            path.Close();

            _fill.Color = new SKColor(
                (byte)((((a >> 16) & 0xFF) + ((b >> 16) & 0xFF) + ((c >> 16) & 0xFF)) / 3),
                (byte)((((a >> 8) & 0xFF) + ((b >> 8) & 0xFF) + ((c >> 8) & 0xFF)) / 3),
                (byte)(((a & 0xFF) + (b & 0xFF) + (c & 0xFF)) / 3),
                alpha);
            _canvas.DrawPath(path, _fill);
        }
    }

    private void DrawMarker(MarkerType type, float cx, float cy, float r, bool hasFill)
    {
        switch (type)
        {
            case MarkerType.Circle:
                if (hasFill)
                {
                    _canvas.DrawCircle(cx, cy, r, _fill);
                }

                _canvas.DrawCircle(cx, cy, r, _stroke);
                break;

            case MarkerType.Square:
                DrawShapeRect(new SKRect(cx - r, cy - r, cx + r, cy + r), hasFill);
                break;

            case MarkerType.Diamond:
                DrawShapePath(hasFill, (cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy));
                break;

            case MarkerType.TriangleUp:
                DrawShapePath(hasFill, (cx, cy - r), (cx + r, cy + r), (cx - r, cy + r));
                break;

            case MarkerType.TriangleDown:
                DrawShapePath(hasFill, (cx, cy + r), (cx + r, cy - r), (cx - r, cy - r));
                break;

            case MarkerType.Plus:
                _canvas.DrawLine(cx - r, cy, cx + r, cy, _stroke);
                _canvas.DrawLine(cx, cy - r, cx, cy + r, _stroke);
                break;

            case MarkerType.Cross:
                _canvas.DrawLine(cx - r, cy - r, cx + r, cy + r, _stroke);
                _canvas.DrawLine(cx - r, cy + r, cx + r, cy - r, _stroke);
                break;

            case MarkerType.Star:
                _canvas.DrawLine(cx - r, cy, cx + r, cy, _stroke);
                _canvas.DrawLine(cx, cy - r, cx, cy + r, _stroke);
                _canvas.DrawLine(cx - r, cy - r, cx + r, cy + r, _stroke);
                _canvas.DrawLine(cx - r, cy + r, cx + r, cy - r, _stroke);
                break;

            case MarkerType.Point:
                _canvas.DrawCircle(cx, cy, System.Math.Max(1f, r / 3f), _fill);
                break;
        }
    }

    private void DrawShapeRect(SKRect rect, bool hasFill)
    {
        if (hasFill)
        {
            _canvas.DrawRect(rect, _fill);
        }

        _canvas.DrawRect(rect, _stroke);
    }

    private void DrawShapePath(bool hasFill, params (float X, float Y)[] vertices)
    {
        using var path = new SKPath();
        path.MoveTo(vertices[0].X, vertices[0].Y);
        for (int i = 1; i < vertices.Length; i++)
        {
            path.LineTo(vertices[i].X, vertices[i].Y);
        }

        path.Close();

        if (hasFill)
        {
            _canvas.DrawPath(path, _fill);
        }

        _canvas.DrawPath(path, _stroke);
    }

    private void ConfigureStroke(LineStyle style, out SKPathEffect? dash)
    {
        ConfigureStrokeBase(style);

        ReadOnlySpan<float> pattern = style.GetDashPattern();
        if (pattern.IsEmpty)
        {
            dash = null;
        }
        else
        {
            dash = SKPathEffect.CreateDash(ScaledDashIntervals(style), 0);
            _stroke.PathEffect = dash;
        }
    }

    private void ConfigureStrokeBase(LineStyle style)
    {
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.Color = ToSk(style.Color);
        _stroke.StrokeWidth = (float)style.Width;
        _stroke.PathEffect = null;
        _stroke.StrokeCap = style.Cap switch
        {
            LineCap.Round => SKStrokeCap.Round,
            LineCap.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt,
        };
        _stroke.StrokeJoin = style.Join switch
        {
            LineJoin.Round => SKStrokeJoin.Round,
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter,
        };
    }

    private bool NeedsDashFlattening(LineStyle style) =>
        _flattenDashes && !style.GetDashPattern().IsEmpty;

    /// <summary>Draws a dashed stroke as explicit solid segments (see the constructor remarks).</summary>
    private void DrawDashFlattened(SKPath geometry, LineStyle style)
    {
        ConfigureStrokeBase(style);
        using SKPath flattened = FlattenDash(geometry, ScaledDashIntervals(style));
        _canvas.DrawPath(flattened, _stroke);
    }

    /// <summary>Chops a path into its visible dash segments using the on/off interval pattern.</summary>
    private static SKPath FlattenDash(SKPath source, float[] intervals)
    {
        var result = new SKPath();
        using var measure = new SKPathMeasure(source, forceClosed: false);
        using var segment = new SKPath();
        do
        {
            float length = measure.Length;
            float distance = 0;
            int index = 0;
            while (distance < length)
            {
                float interval = intervals[index % intervals.Length];
                if (interval <= 0)
                {
                    break;
                }

                float end = System.Math.Min(distance + interval, length);
                if (index % 2 == 0)
                {
                    segment.Reset();
                    if (measure.GetSegment(distance, end, segment, startWithMoveTo: true))
                    {
                        result.AddPath(segment);
                    }
                }

                distance = end;
                index++;
            }
        }
        while (measure.NextContour());

        return result;
    }

    private static float[] ScaledDashIntervals(LineStyle style)
    {
        ReadOnlySpan<float> pattern = style.GetDashPattern();
        float width = System.Math.Max(1f, (float)style.Width);
        var intervals = new float[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            intervals[i] = pattern[i] * width;
        }

        return intervals;
    }

    private void ConfigureFont(TextStyle style)
    {
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (!_typefaces.TryGetValue(key, out SKTypeface? typeface))
        {
            SKFontStyleWeight weight = style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            SKFontStyleSlant slant = style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            typeface = SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, slant)
                       ?? SKTypeface.Default;
            _typefaces[key] = typeface;
        }

        _text.Typeface = typeface;
        _text.TextSize = (float)style.FontSize;
    }

    private int CopyToBuffer(ReadOnlySpan<Point2D> points)
    {
        if (_pointBuffer.Length < points.Length)
        {
            _pointBuffer = new SKPoint[System.Math.Max(points.Length, _pointBuffer.Length * 2)];
        }

        for (int i = 0; i < points.Length; i++)
        {
            _pointBuffer[i] = new SKPoint((float)points[i].X, (float)points[i].Y);
        }

        return points.Length;
    }

    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    private static SKColor ToSk(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
