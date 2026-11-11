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

    /// <summary>The bitmap the canvas paints into, when its maker handed it over; see <see cref="DrawMarkerCloud"/>.</summary>
    private readonly SKBitmap? _raster;

    /// <summary>Every clip pushed and not yet popped, in whole device pixels, innermost last.</summary>
    private readonly Stack<SKRectI> _deviceClips = new();
    private readonly SKPaint _stroke;
    private readonly SKPaint _fill;
    private readonly SKPaint _text;
    private readonly SKPaint _mesh;
    private readonly bool _smoothing;
    private readonly bool _flattenDashes;
    private readonly bool _supportsMeshes;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaces = new();

    /// <summary>
    /// The face that can draw a character the chosen one cannot, remembered per (face, character).
    /// </summary>
    /// <remarks>
    /// A figure asks for the same missing glyph on every redraw — the same axis label, the same
    /// legend entry — so the system font list is walked once per character and never again.
    /// </remarks>
    private readonly Dictionary<(SKTypeface Face, int Character), SKTypeface?> _fallbacks = new();

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
    /// <param name="smoothing">
    /// Whether edges are drawn smoothed (MATLAB's figure <c>GraphicsSmoothing</c>). Off gives the
    /// hard staircase of unaliased geometry, which is exactly what turning it off is asking for.
    /// </param>
    /// <param name="supportsMeshes">
    /// Whether the canvas can rasterize <see cref="DrawTriangles"/> as a vertex mesh. Skia's SVG and
    /// PDF backends drop <c>DrawVertices</c> silently — the geometry simply never appears in the
    /// file — so those exporters pass false and get one filled path per triangle instead.
    /// </param>
    /// <param name="raster">
    /// The bitmap <paramref name="canvas"/> paints into, when there is one and its maker can hand it
    /// over: with it a cloud of own-coloured markers is stamped into the pixels instead of drawn a
    /// call at a time (<see cref="DrawMarkerCloud"/>). Null for a vector canvas or a screen.
    /// </param>
    public SkiaRenderContext(
        SKCanvas canvas,
        Size2D size,
        double devicePixelRatio = 1.0,
        bool flattenDashes = false,
        bool supportsMeshes = true,
        bool smoothing = true,
        SKBitmap? raster = null)
    {
        _canvas = canvas;
        _raster = raster;
        Size = size;
        DevicePixelRatio = devicePixelRatio;
        _flattenDashes = flattenDashes;
        _supportsMeshes = supportsMeshes;
        _smoothing = smoothing;

        _stroke = new SKPaint { IsAntialias = smoothing, Style = SKPaintStyle.Stroke };
        _fill = new SKPaint { IsAntialias = smoothing, Style = SKPaintStyle.Fill };
        _text = new SKPaint { IsAntialias = smoothing, Style = SKPaintStyle.Fill, SubpixelText = true };

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

        // The same clip in whole device pixels, as Skia's unsmoothed clip keeps it (each edge to
        // the nearest pixel), for the one painter here that writes pixels itself.
        SKRect device = _canvas.TotalMatrix.MapRect(ToSk(rect));
        var whole = new SKRectI(
            (int)System.Math.Floor(device.Left + 0.5),
            (int)System.Math.Floor(device.Top + 0.5),
            (int)System.Math.Floor(device.Right + 0.5),
            (int)System.Math.Floor(device.Bottom + 0.5));
        if (_deviceClips.Count > 0)
        {
            whole.Intersect(_deviceClips.Peek());
        }

        _deviceClips.Push(whole);
    }

    /// <inheritdoc />
    public void PopClip()
    {
        _canvas.Restore();
        if (_deviceClips.Count > 0)
        {
            _deviceClips.Pop();
        }
    }

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

        // Every other glyph is an outline that a fill sits inside, so one with no face colour simply
        // skips the fill. A point has no outline — it IS its fill — so it is drawn in the edge colour
        // when no face colour was asked for, which is the one colour a '.' series always carries.
        // Leaving the fill at whatever the last caller set paints the point in a stale colour, and a
        // stale colour is usually no colour at all.
        _fill.Color = ToSk(fillColor ?? edgeColor);

        foreach (Point2D p in points)
        {
            DrawMarker(style.Type, (float)p.X, (float)p.Y, radius, fillColor.HasValue);
        }
    }

    /// <summary>
    /// Whether a cloud of own-coloured markers is stamped from masks (<c>JGRAPH_MARKER_SPRITES</c>,
    /// on unless it is <c>0</c>); off, every marker is its own pair of Skia calls, as before ADR 0331.
    /// </summary>
    public static bool MarkerSprites { get; set; } =
        Environment.GetEnvironmentVariable("JGRAPH_MARKER_SPRITES") != "0";

    /// <summary>Fewest markers worth making the masks for.</summary>
    private const int SpriteWorthwhile = 256;

    /// <summary>Positions within a pixel, each way, a mask is made for.</summary>
    private const int SpritePhases = 32;

    /// <summary>Widest mask, in device pixels; a larger marker is drawn by Skia one at a time.</summary>
    private const int SpriteCellLimit = 64;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A scatter coloured point by point is two Skia calls a marker (a fill and an outline), and at
    /// two hundred thousand markers those calls are the whole export. When the canvas paints into a
    /// bitmap this context was handed, through a plain scale and shift and a rectangular clip, and
    /// every colour is opaque, the markers are stamped instead: Skia draws the glyph once for each
    /// of <see cref="SpritePhases"/> by <see cref="SpritePhases"/> positions within a pixel, in
    /// white onto a coverage-only bitmap (fill, then outline when there is one, as the
    /// one-at-a-time road composes them), and each marker is then its colour blended through the
    /// mask nearest its own position.
    /// </para>
    /// <para>
    /// The picture differs from the one-at-a-time road's only in that a marker's centre is read to
    /// an eighth of a pixel and the blend is rounded once instead of twice. Anything else (a PDF
    /// or SVG canvas, a rotated one, a see-through colour, a large marker, a short run) takes the
    /// one-at-a-time road unchanged.
    /// </para>
    /// </remarks>
    public void DrawMarkerCloud(ReadOnlySpan<Point2D> points, ReadOnlySpan<uint> colorsArgb, MarkerStyle style)
    {
        int count = System.Math.Min(points.Length, colorsArgb.Length);
        if (!style.IsVisible || count == 0)
        {
            return;
        }

        if (count >= SpriteWorthwhile && MarkerSprites
            && TryStampMarkers(points[..count], colorsArgb[..count], style))
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            uint argb = colorsArgb[i];
            Color own = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            Color edge = style.Edge is { A: 0 } none ? none : own;
            DrawMarkers(
                points.Slice(i, 1),
                new MarkerStyle(style.Type, style.Size, style.Fill is null ? null : own, edge, style.EdgeWidth),
                edge);
        }
    }

    private bool TryStampMarkers(ReadOnlySpan<Point2D> points, ReadOnlySpan<uint> colors, MarkerStyle style)
    {
        SKBitmap? raster = _raster;
        if (raster is null
            || raster.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888)
            || raster.AlphaType == SKAlphaType.Unpremul)
        {
            return false;
        }

        SKMatrix matrix = _canvas.TotalMatrix;
        if (matrix.SkewX != 0 || matrix.SkewY != 0 || matrix.Persp0 != 0 || matrix.Persp1 != 0
            || matrix.Persp2 != 1 || !(matrix.ScaleX > 0) || !(matrix.ScaleY > 0) || !_canvas.IsClipRect)
        {
            return false;
        }

        foreach (uint argb in colors)
        {
            if (argb >> 24 != 0xFF)
            {
                return false;
            }
        }

        double sx = matrix.ScaleX;
        double sy = matrix.ScaleY;
        double edgeWidth = System.Math.Max(style.EdgeWidth, 0);

        // Room for the widest glyph: a square's corner is the radius times root two out, and a
        // mitred outline reaches up to twice its width past that.
        int pad = (int)System.Math.Ceiling(((style.Size * 0.75) + (edgeWidth * 2.5)) * System.Math.Max(sx, sy)) + 2;
        int cell = (2 * pad) + 1;
        if (cell > SpriteCellLimit)
        {
            return false;
        }

        const int Phases = SpritePhases;
        using var atlas = new SKBitmap(new SKImageInfo(Phases * cell, Phases * cell, SKColorType.Alpha8, SKAlphaType.Premul));
        atlas.Erase(SKColors.Transparent);
        using (var maskCanvas = new SKCanvas(atlas))
        using (var fill = new SKPaint { IsAntialias = _smoothing, Style = SKPaintStyle.Fill, Color = SKColors.White })
        using (var stroke = new SKPaint
        {
            IsAntialias = _smoothing,
            Style = SKPaintStyle.Stroke,

            // No outline is an outline in a colour that marks nothing, as it is one at a time.
            Color = style.Edge is { A: 0 } ? SKColors.Transparent : SKColors.White,
            StrokeWidth = (float)style.EdgeWidth,
            StrokeCap = _stroke.StrokeCap,
            StrokeJoin = _stroke.StrokeJoin,
            StrokeMiter = _stroke.StrokeMiter,
        })
        {
            float radius = (float)(style.Size / 2.0);
            for (int j = 0; j < Phases; j++)
            {
                for (int i = 0; i < Phases; i++)
                {
                    maskCanvas.Save();
                    maskCanvas.ClipRect(SKRect.Create(i * cell, j * cell, cell, cell));
                    maskCanvas.Translate((i * cell) + pad + (i / (float)Phases), (j * cell) + pad + (j / (float)Phases));
                    maskCanvas.Scale((float)sx, (float)sy);
                    DrawMarkerOn(maskCanvas, fill, stroke, style.Type, 0, 0, radius, style.Fill.HasValue);
                    maskCanvas.Restore();
                }
            }

            maskCanvas.Flush();
        }

        int maskStride = atlas.RowBytes;
        byte[] mask = atlas.GetPixelSpan().ToArray();

        // The part of a cell any phase's glyph touches, so a stamp reads no more than that.
        int x0 = cell;
        int x1 = -1;
        int y0 = cell;
        int y1 = -1;
        for (int row = 0; row < Phases * cell; row++)
        {
            int y = row % cell;
            int at = row * maskStride;
            for (int column = 0; column < Phases * cell; column++)
            {
                if (mask[at + column] != 0)
                {
                    int x = column % cell;
                    x0 = System.Math.Min(x0, x);
                    x1 = System.Math.Max(x1, x);
                    y0 = System.Math.Min(y0, y);
                    y1 = System.Math.Max(y1, y);
                }
            }
        }

        if (x1 < 0)
        {
            return true; // a glyph that marks no pixel marks none anywhere
        }

        // The canvas reports its clip rounded outwards; the clips pushed here are kept as Skia
        // applies them, so the stamp marks no pixel the one-at-a-time road is kept out of.
        SKRectI clip = _canvas.DeviceClipBounds;
        if (_deviceClips.Count > 0)
        {
            clip.Intersect(_deviceClips.Peek());
        }
        int left = System.Math.Max(clip.Left, 0);
        int top = System.Math.Max(clip.Top, 0);
        int right = System.Math.Min(clip.Right, raster.Width);
        int bottom = System.Math.Min(clip.Bottom, raster.Height);
        if (left >= right || top >= bottom)
        {
            return true;
        }

        ReadOnlySpan<byte> stored = raster.GetPixelSpan();
        Span<byte> pixels = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(stored), stored.Length);
        int stride = raster.RowBytes;
        bool blueFirst = raster.ColorType == SKColorType.Bgra8888;
        double tx = matrix.TransX;
        double ty = matrix.TransY;
        for (int k = 0; k < points.Length; k++)
        {
            double dx = (sx * points[k].X) + tx;
            double dy = (sy * points[k].Y) + ty;
            if (!(dx > -1e6 && dx < 1e6 && dy > -1e6 && dy < 1e6))
            {
                continue; // far outside, or not a number: Skia draws nothing for it either
            }

            double wholeX = System.Math.Floor(dx);
            double wholeY = System.Math.Floor(dy);
            int ix = (int)wholeX;
            int iy = (int)wholeY;
            int qx = (int)System.Math.Round((dx - wholeX) * Phases);
            int qy = (int)System.Math.Round((dy - wholeY) * Phases);
            if (qx == Phases)
            {
                qx = 0;
                ix++;
            }

            if (qy == Phases)
            {
                qy = 0;
                iy++;
            }

            int originX = ix - pad;
            int originY = iy - pad;
            int xa = System.Math.Max(x0, left - originX);
            int xb = System.Math.Min(x1, right - 1 - originX);
            int ya = System.Math.Max(y0, top - originY);
            int yb = System.Math.Min(y1, bottom - 1 - originY);
            if (xa > xb || ya > yb)
            {
                continue;
            }

            uint argb = colors[k];
            int red = (int)((argb >> 16) & 0xFF);
            int green = (int)((argb >> 8) & 0xFF);
            int blue = (int)(argb & 0xFF);
            int c0 = blueFirst ? blue : red;
            int c2 = blueFirst ? red : blue;
            int cellBase = (qy * cell * maskStride) + (qx * cell);
            for (int y = ya; y <= yb; y++)
            {
                int maskAt = cellBase + (y * maskStride) + xa;
                int at = ((originY + y) * stride) + ((originX + xa) * 4);
                for (int x = xa; x <= xb; x++, maskAt++, at += 4)
                {
                    int a = mask[maskAt];
                    if (a == 0)
                    {
                        continue;
                    }

                    if (a == 255)
                    {
                        pixels[at] = (byte)c0;
                        pixels[at + 1] = (byte)green;
                        pixels[at + 2] = (byte)c2;
                        pixels[at + 3] = 255;
                        continue;
                    }

                    int rest = 255 - a;
                    pixels[at] = Over255((c0 * a) + (pixels[at] * rest));
                    pixels[at + 1] = Over255((green * a) + (pixels[at + 1] * rest));
                    pixels[at + 2] = Over255((c2 * a) + (pixels[at + 2] * rest));
                    pixels[at + 3] = Over255((255 * a) + (pixels[at + 3] * rest));
                }
            }
        }

        raster.NotifyPixelsChanged();
        return true;
    }

    /// <summary>A product of two bytes brought back to a byte, rounded to nearest.</summary>
    private static byte Over255(int product)
    {
        int t = product + 128;
        return (byte)((t + (t >> 8)) >> 8);
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

        if (text.Contains('\n'))
        {
            string[] lines = text.Replace("\r", "").Split('\n');
            double height = MeasureText("Mg",style).Height * 1.2;
            double shift = vertical switch { VerticalAlignment.Bottom => -(lines.Length-1)*height,
                VerticalAlignment.Middle => -(lines.Length-1)*height/2, _ => 0 };
            _canvas.Save();
            _canvas.Translate((float)position.X,(float)position.Y);
            _canvas.RotateDegrees((float)rotationDegrees);
            for (int i=0;i<lines.Length;i++) DrawText(lines[i],new Point2D(0,shift+i*height),style,horizontal,vertical);
            _canvas.Restore();
            return;
        }

        // Every text in a figure arrives here, which is why the markup is read here: a title, a tick
        // label, a legend entry and a text object are one call each, and one call each is what the
        // interpreter has to reach for a label written with \sigma to read as one wherever it sits.
        text = TexMarkup.Render(text, style.Interpreter);

        ConfigureFont(style);
        _text.Color = ToSk(style.Color);
        _text.IsAntialias = style.Antialias && _smoothing;

        float width = RunWidth(text);
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
            DrawRuns(text, dx, dy);
            _canvas.Restore();
        }
        else
        {
            DrawRuns(text, (float)position.X + dx, (float)position.Y + dy);
        }
    }

    /// <summary>
    /// Draws <paramref name="text"/> from <paramref name="x"/>, changing face wherever the chosen one
    /// has no glyph for what comes next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skia draws one string with one face and puts a box where that face has nothing. That is what
    /// made <c>title('t \in [0, 60]')</c> come out as <em>t □ [0, 60]</em>: the markup had already
    /// turned <c>\in</c> into <c>∈</c> correctly, and then the figure's font — a plain UI sans — was
    /// asked to draw a character it does not carry. The label was right and the picture was wrong,
    /// which is why nothing in the console ever mentioned it.
    /// </para>
    /// <para>
    /// A string of ASCII takes the single-call road it always took, so nothing that already drew can
    /// move: every face carries ASCII, so a split would produce one run and one call anyway.
    /// </para>
    /// </remarks>
    private void DrawRuns(string text, float x, float y)
    {
        if (IsPlain(text))
        {
            _canvas.DrawText(text, x, y, _text);
            return;
        }

        SKTypeface chosen = _text.Typeface;
        foreach ((string piece, SKTypeface face) in Runs(text, chosen))
        {
            _text.Typeface = face;
            _canvas.DrawText(piece, x, y, _text);
            x += _text.MeasureText(piece);
        }

        _text.Typeface = chosen;
    }

    /// <summary>The width of <paramref name="text"/> once every run is measured in its own face.</summary>
    private float RunWidth(string text)
    {
        if (IsPlain(text))
        {
            return _text.MeasureText(text);
        }

        SKTypeface chosen = _text.Typeface;
        float width = 0;
        foreach ((string piece, SKTypeface face) in Runs(text, chosen))
        {
            _text.Typeface = face;
            width += _text.MeasureText(piece);
        }

        _text.Typeface = chosen;
        return width;
    }

    /// <summary>Whether every character is one no font is short of, so no split is needed.</summary>
    private static bool IsPlain(string text)
    {
        foreach (char c in text)
        {
            if (c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <paramref name="text"/> cut into stretches, each with a face that can draw all of it.
    /// </summary>
    private List<(string Text, SKTypeface Face)> Runs(string text, SKTypeface primary)
    {
        var runs = new List<(string, SKTypeface)>();
        var run = new System.Text.StringBuilder();
        SKTypeface current = primary;

        for (int i = 0; i < text.Length;)
        {
            int size = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            int point = size == 2 ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            SKTypeface face = FaceFor(primary, point);
            if (run.Length > 0 && !ReferenceEquals(face, current))
            {
                runs.Add((run.ToString(), current));
                run.Clear();
            }

            current = face;
            run.Append(text, i, size);
            i += size;
        }

        if (run.Length > 0)
        {
            runs.Add((run.ToString(), current));
        }

        return runs;
    }

    /// <summary>
    /// The face to draw one character in: the chosen one when it has the glyph, otherwise whatever
    /// the system offers, and the chosen one again when nothing does.
    /// </summary>
    /// <remarks>
    /// Falling back to the chosen face for a character nobody has is deliberate: the box it draws is
    /// the honest answer when the machine has no font for the character, and it keeps the run list
    /// from growing a face per unknown glyph.
    /// </remarks>
    private SKTypeface FaceFor(SKTypeface primary, int character)
    {
        if (primary.ContainsGlyph(character))
        {
            return primary;
        }

        var key = (primary, character);
        if (!_fallbacks.TryGetValue(key, out SKTypeface? found))
        {
            found = SKFontManager.Default.MatchCharacter(
                primary.FamilyName, primary.FontStyle, null, character);
            _fallbacks[key] = found;
        }

        return found ?? primary;
    }

    /// <inheritdoc />
    public Size2D MeasureText(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size2D.Empty;
        }

        if (text.Contains('\n'))
        {
            var lines=text.Replace("\r", "").Split('\n');
            double height=MeasureText("Mg",style).Height;
            return new Size2D(lines.Max(l=>MeasureText(l,style).Width),height*(1+(lines.Length-1)*1.2));
        }
        // Measured as it will be drawn, or every layout that reserves room for a label would reserve
        // room for the markup instead of for the symbols.
        text = TexMarkup.Render(text, style.Interpreter);

        ConfigureFont(style);
        float width = RunWidth(text);
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
        foreach (SKTypeface? fallback in _fallbacks.Values)
        {
            fallback?.Dispose();
        }

        _fallbacks.Clear();

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

    private void DrawMarker(MarkerType type, float cx, float cy, float r, bool hasFill) =>
        DrawMarkerOn(_canvas, _fill, _stroke, type, cx, cy, r, hasFill);

    /// <summary>One marker glyph on a canvas, with the paints that fill and outline it.</summary>
    private static void DrawMarkerOn(
        SKCanvas canvas, SKPaint fill, SKPaint stroke, MarkerType type, float cx, float cy, float r, bool hasFill)
    {
        switch (type)
        {
            case MarkerType.Circle:
                if (hasFill)
                {
                    canvas.DrawCircle(cx, cy, r, fill);
                }

                canvas.DrawCircle(cx, cy, r, stroke);
                break;

            case MarkerType.Square:
                DrawShapeRect(canvas, fill, stroke, new SKRect(cx - r, cy - r, cx + r, cy + r), hasFill);
                break;

            case MarkerType.Diamond:
                DrawShapePath(canvas, fill, stroke, hasFill, (cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy));
                break;

            case MarkerType.TriangleUp:
                DrawShapePath(canvas, fill, stroke, hasFill, (cx, cy - r), (cx + r, cy + r), (cx - r, cy + r));
                break;

            case MarkerType.TriangleDown:
                DrawShapePath(canvas, fill, stroke, hasFill, (cx, cy + r), (cx + r, cy - r), (cx - r, cy - r));
                break;

            case MarkerType.Plus:
                canvas.DrawLine(cx - r, cy, cx + r, cy, stroke);
                canvas.DrawLine(cx, cy - r, cx, cy + r, stroke);
                break;

            case MarkerType.Cross:
                canvas.DrawLine(cx - r, cy - r, cx + r, cy + r, stroke);
                canvas.DrawLine(cx - r, cy + r, cx + r, cy - r, stroke);
                break;

            case MarkerType.Star:
                canvas.DrawLine(cx - r, cy, cx + r, cy, stroke);
                canvas.DrawLine(cx, cy - r, cx, cy + r, stroke);
                canvas.DrawLine(cx - r, cy - r, cx + r, cy + r, stroke);
                canvas.DrawLine(cx - r, cy + r, cx + r, cy - r, stroke);
                break;

            case MarkerType.Point:
                // A third of the radius, because MATLAB's '.' is the one marker drawn smaller than
                // the size it is given. The fill is always a real colour here — see DrawMarkers.
                canvas.DrawCircle(cx, cy, System.Math.Max(1f, r / 3f), fill);
                break;
        }
    }

    private static void DrawShapeRect(SKCanvas canvas, SKPaint fill, SKPaint stroke, SKRect rect, bool hasFill)
    {
        if (hasFill)
        {
            canvas.DrawRect(rect, fill);
        }

        canvas.DrawRect(rect, stroke);
    }

    private static void DrawShapePath(
        SKCanvas canvas, SKPaint fill, SKPaint stroke, bool hasFill, params (float X, float Y)[] vertices)
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
            canvas.DrawPath(path, fill);
        }

        canvas.DrawPath(path, stroke);
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
