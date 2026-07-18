using JGraph.Core.Data;
using JGraph.Core.Primitives;

namespace JGraph.Objects.Internal;

/// <summary>
/// Nearest-point hit-testing for point series, shared by the line/scatter/stem plots. For ascending,
/// span-backed data (the common case — every scripted or API-built series) it binary-searches the
/// data-x window that could possibly contain a hit and scans only those candidates, so Pointer-mode
/// hover over a million-point line costs a handful of comparisons per mouse move instead of a full
/// scan. Non-ascending or non-span data takes the exact legacy full scan.
/// </summary>
/// <remarks>
/// Exact parity with the legacy scan, by construction: any point within <c>tol</c> pixels of the
/// cursor has a pixel-x offset of at most <c>tol</c>, so its data x lies inside
/// [PixelToData(px−tol), PixelToData(px+tol)] (ordered to absorb axis inversion, widened one index
/// each side against rounding at the edges — log scales live inside the mapper, which only needs to
/// be monotonic in x). The global argmin, whenever it is a hit, is therefore inside the window, and
/// a point outside the window can never tie it (its distance necessarily exceeds <c>tol</c>) — so
/// the windowed scan returns the identical index, first-wins tie-breaking included.
/// </remarks>
internal static class SeriesHitTester
{
    /// <summary>
    /// The nearest finite point within <paramref name="tolerancePixels"/> of
    /// <paramref name="pixelPoint"/>, or null when nothing is close enough.
    /// </summary>
    public static (int Index, double Distance)? FindNearest(
        IDataSeries data, ICoordinateMapper mapper, Point2D pixelPoint, double tolerancePixels)
    {
        if (data.Count == 0)
        {
            return null;
        }

        int start = 0;
        int end = data.Count - 1;
        if (data.IsXAscending && data.TryGetSpans(out System.ReadOnlySpan<double> xs, out _))
        {
            double a = mapper.PixelToData(pixelPoint.X - tolerancePixels, pixelPoint.Y).X;
            double b = mapper.PixelToData(pixelPoint.X + tolerancePixels, pixelPoint.Y).X;
            (double lo, double hi) = a <= b ? (a, b) : (b, a);
            if (!double.IsNaN(lo) && !double.IsNaN(hi))
            {
                start = System.Math.Max(0, LowerBound(xs, lo) - 1);
                end = System.Math.Min(data.Count - 1, UpperBound(xs, hi) + 1);
                if (start > end)
                {
                    return null;
                }
            }
        }

        double bestDistance = double.MaxValue;
        int bestIndex = -1;
        for (int i = start; i <= end; i++)
        {
            double x = data.GetX(i);
            double y = data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            double distance = mapper.DataToPixel(x, y).DistanceTo(pixelPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex >= 0 && bestDistance <= tolerancePixels ? (bestIndex, bestDistance) : null;
    }

    /// <summary>The first index whose value is at least <paramref name="value"/> (or length).</summary>
    private static int LowerBound(System.ReadOnlySpan<double> values, double value)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (values[mid] < value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>The last index whose value is at most <paramref name="value"/> (or -1).</summary>
    private static int UpperBound(System.ReadOnlySpan<double> values, double value)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (values[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low - 1;
    }
}
