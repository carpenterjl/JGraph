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
    private readonly bool _flattenDashes;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaces = new();

    private SKPoint[] _pointBuffer = new SKPoint[256];

    // Reused across DrawPolyline calls: rebuilding the geometry is unavoidable, but reallocating
    // a native SKPath per polyline per frame is not (a figure redraws every polyline on every pan).
    private readonly SKPath _polylinePath = new();

    /// <param name="canvas">The Skia canvas to draw onto (raster, SVG, or PDF page).</param>
    /// <param name="size">The drawable size in device-independent units.</param>
    /// <param name="devicePixelRatio">Physical pixels per device-independent unit.</param>
    /// <param name="flattenDashes">
    /// Converts dashed strokes into explicit segment geometry instead of a Skia dash path effect.
    /// Skia's SVG backend drops dash path effects (drawing them solid), so the SVG exporter enables
    /// this; raster and PDF targets keep the faster path effect.
    /// </param>
    public SkiaRenderContext(SKCanvas canvas, Size2D size, double devicePixelRatio = 1.0, bool flattenDashes = false)
    {
        _canvas = canvas;
        Size = size;
        DevicePixelRatio = devicePixelRatio;
        _flattenDashes = flattenDashes;

        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _text = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, SubpixelText = true };
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
        SKPath path = _polylinePath;
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
        using var path = new SKPath();
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

        ConfigureFont(style);
        float width = _text.MeasureText(text);
        SKFontMetrics metrics = _text.FontMetrics;
        return new Size2D(width, metrics.Descent - metrics.Ascent);
    }

    public void Dispose()
    {
        _polylinePath.Dispose();
        _stroke.Dispose();
        _fill.Dispose();
        _text.Dispose();
        foreach (SKTypeface typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _typefaces.Clear();
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

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
