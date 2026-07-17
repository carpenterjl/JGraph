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
    /// <summary>Above this many samples, markers are suppressed to avoid clutter and cost.</summary>
    public const int MaxMarkerCount = 5000;

    public static void DrawLine(
        IRenderContext context,
        RenderState state,
        IDataSeries data,
        LineStyle line,
        ref Point2D[] dataBuffer,
        ref Point2D[] pixelBuffer)
    {
        if (data.Count < 2 || !line.IsVisible)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        Rect2D area = state.PlotArea;
        DataRange visibleX = VisibleXRange(mapper, area);
        int columns = System.Math.Max(1, (int)area.Width);

        bool canDecimate = data.IsXAscending
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

            DrawWithGaps(context, pixelBuffer, n, line);
        }
    }

    public static void DrawMarkers(
        IRenderContext context,
        RenderState state,
        IDataSeries data,
        MarkerStyle marker,
        Color seriesColor,
        ref Point2D[] pixelBuffer)
    {
        if (!marker.IsVisible || data.Count == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        EnsureCapacity(ref pixelBuffer, data.Count);
        int m = 0;
        for (int i = 0; i < data.Count; i++)
        {
            double x = data.GetX(i);
            double y = data.GetY(i);
            if (double.IsFinite(x) && double.IsFinite(y))
            {
                pixelBuffer[m++] = mapper.DataToPixel(x, y);
            }
        }

        context.DrawMarkers(pixelBuffer.AsSpan(0, m), marker, seriesColor);
    }

    public static DataRange VisibleXRange(ICoordinateMapper mapper, Rect2D area)
    {
        double xa = mapper.PixelToData(area.Left, area.Bottom).X;
        double xb = mapper.PixelToData(area.Right, area.Bottom).X;
        return new DataRange(System.Math.Min(xa, xb), System.Math.Max(xa, xb));
    }

    private static void DrawWithGaps(IRenderContext context, Point2D[] pixels, int count, LineStyle line)
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

    private static void EnsureCapacity(ref Point2D[] buffer, int required)
    {
        if (buffer.Length < required)
        {
            buffer = new Point2D[System.Math.Max(required, buffer.Length * 2)];
        }
    }
}
