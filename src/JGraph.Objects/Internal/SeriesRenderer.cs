using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Maths.Decimation;
using JGraph.Rendering;

namespace JGraph.Objects.Internal;

/// <summary>
/// Shared helpers for turning an <see cref="IDataSeries"/> into device-space draw calls: windowed
/// min/max decimation for large ascending series, per-point mapping otherwise, and splitting a
/// polyline at non-finite samples so gaps in the data become gaps in the line.
/// </summary>
internal static class SeriesRenderer
{
    /// <summary>
    /// Above this many samples, markers are placed one per device pixel rather than one per sample.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until M121 this was the count above which markers were <em>suppressed</em>, and the comment
    /// said so: "to avoid clutter and cost". The cost was real and the clutter was not — a marker
    /// per sample is what MATLAB draws, however many there are. What the rule actually did was
    /// leave an axes completely blank whenever the series had no line to fall back on, which is
    /// every <c>plot(x, y, '.')</c> over more than five thousand points: the limits were computed
    /// from the data, the ticks were drawn, and nothing was inside them.
    /// </para>
    /// <para>
    /// The count is kept as the threshold for the collapse below rather than deleted, so that every
    /// series that drew markers before draws exactly the same ones now — overlapping marks are only
    /// merged past the point where none were being drawn at all.
    /// </para>
    /// </remarks>
    public const int MaxMarkerCount = 5000;

    public static void DrawLine(
        IRenderContext context,
        RenderState state,
        IDataSeries data,
        LineStyle line,
        ref Point2D[] dataBuffer,
        ref Point2D[] pixelBuffer,
        bool alignVertexCenters = false)
    {
        if (data.Count < 2 || !line.IsVisible)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        Rect2D area = state.PlotArea;
        DataRange visibleX = VisibleXRange(mapper, area);
        int columns = System.Math.Max(1, (int)area.Width);

        // The reduction below buckets samples by data x into device columns, which is a reduction
        // only where a column IS a range of x. On an angular mapper it is not, and the visible range
        // read back through the plot area's corners is a wedge rather than the whole turn — so the
        // curve would be cut down to whatever angles those corners happen to name.
        bool canDecimate = mapper.ColumnsAreXRanges
            && data.IsXAscending
            && data.Count > columns * 2
            && data.TryGetSpans(out ReadOnlySpan<double> xs, out ReadOnlySpan<double> ys);

        if (canDecimate)
        {
            data.TryGetSpans(out ReadOnlySpan<double> xs2, out ReadOnlySpan<double> ys2);
            EnsureCapacity(ref dataBuffer, MinMaxDecimator.RequiredBufferSize(columns));
            int n = MinMaxDecimator.Decimate(xs2, ys2, visibleX, columns, dataBuffer);

            EnsureCapacity(ref pixelBuffer, n);
            for (int i = 0; i < n; i++)
            {
                pixelBuffer[i] = mapper.DataToPixel(dataBuffer[i].X, dataBuffer[i].Y);
            }

            SnapToPixelCentres(pixelBuffer, n, alignVertexCenters);
            DrawWithGaps(context, pixelBuffer, n, line);
        }
        else
        {
            int n = data.Count;
            EnsureCapacity(ref pixelBuffer, n);
            for (int i = 0; i < n; i++)
            {
                double x = data.GetX(i);
                double y = data.GetY(i);
                pixelBuffer[i] = double.IsFinite(x) && double.IsFinite(y)
                    ? mapper.DataToPixel(x, y)
                    : Point2D.NaN;
            }

            SnapToPixelCentres(pixelBuffer, n, alignVertexCenters);
            DrawWithGaps(context, pixelBuffer, n, line);
        }
    }

    /// <summary>
    /// Puts every vertex on a pixel centre. A one-pixel line between two half-pixel positions is
    /// spread across two rows and reads as grey; on the centres it lands in one and reads as the
    /// line it is. This is MATLAB's <c>AlignVertexCenters</c>, and it does nothing unless asked.
    /// </summary>
    private static void SnapToPixelCentres(Point2D[] pixels, int count, bool snap)
    {
        if (!snap)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (pixels[i].IsFinite)
            {
                pixels[i] = new Point2D(
                    System.Math.Floor(pixels[i].X) + 0.5,
                    System.Math.Floor(pixels[i].Y) + 0.5);
            }
        }
    }

    public static void DrawMarkers(
        IRenderContext context,
        RenderState state,
        IDataSeries data,
        MarkerStyle marker,
        Color seriesColor,
        ref Point2D[] pixelBuffer,
        int[]? indices = null)
    {
        if (!marker.IsVisible || data.Count == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;

        // A marker per sample unless the caller named the ones it wants — MATLAB's MarkerIndices,
        // which is how a line of ten thousand points is given a dozen readable markers.
        int wanted = indices?.Length ?? data.Count;
        EnsureCapacity(ref pixelBuffer, wanted);

        // Past the threshold, samples that land on the same device pixel are drawn once. Two
        // hundred thousand markers over a plot area of half a million pixels is at most half a
        // million marks and usually far fewer, so the work stops growing with the data and starts
        // being bounded by the picture — and the picture is the same one, because a second opaque
        // mark on a pixel already marked adds no ink.
        HashSet<long>? drawn = wanted > MaxMarkerCount ? new HashSet<long>(MaxMarkerCount) : null;
        int m = 0;
        for (int k = 0; k < wanted; k++)
        {
            int i = indices is null ? k : indices[k];
            if (i < 0 || i >= data.Count)
            {
                continue;
            }

            double x = data.GetX(i);
            double y = data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            Point2D at = mapper.DataToPixel(x, y);
            if (drawn is not null && !drawn.Add(PixelKey(at)))
            {
                continue;
            }

            pixelBuffer[m++] = at;
        }

        context.DrawMarkers(pixelBuffer.AsSpan(0, m), marker, seriesColor);
    }

    /// <summary>
    /// One device pixel, as a single number two marks can be compared by. Non-finite coordinates
    /// never reach here, and the clamp keeps a point far outside the axes from wrapping onto one
    /// inside it.
    /// </summary>
    private static long PixelKey(Point2D at)
    {
        long column = (long)System.Math.Clamp(System.Math.Round(at.X), -1_000_000.0, 1_000_000.0);
        long row = (long)System.Math.Clamp(System.Math.Round(at.Y), -1_000_000.0, 1_000_000.0);
        return (column << 22) ^ row;
    }

    public static DataRange VisibleXRange(ICoordinateMapper mapper, Rect2D area)
    {
        double xa = mapper.PixelToData(area.Left, area.Bottom).X;
        double xb = mapper.PixelToData(area.Right, area.Bottom).X;
        return new DataRange(System.Math.Min(xa, xb), System.Math.Max(xa, xb));
    }

    /// <summary>
    /// Draws a run of projected points as one polyline per unbroken stretch, so a non-finite point is
    /// a break in the line rather than a jump through the origin. Public within this assembly because
    /// the 3D line plot projects its own points but wants exactly this treatment of gaps.
    /// </summary>
    public static void DrawWithGaps(IRenderContext context, Point2D[] pixels, int count, LineStyle line)
    {
        int start = -1;
        for (int i = 0; i < count; i++)
        {
            bool finite = pixels[i].IsFinite;
            if (finite)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                if (i - start >= 2)
                {
                    context.DrawPolyline(pixels.AsSpan(start, i - start), line);
                }

                start = -1;
            }
        }

        if (start >= 0 && count - start >= 2)
        {
            context.DrawPolyline(pixels.AsSpan(start, count - start), line);
        }
    }

    public static void EnsureCapacity(ref Point2D[] buffer, int required)
    {
        if (buffer.Length < required)
        {
            buffer = new Point2D[System.Math.Max(required, buffer.Length * 2)];
        }
    }
}
