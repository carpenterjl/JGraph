using JGraph.Core.Primitives;

namespace JGraph.Maths.Decimation;

/// <summary>
/// Level-of-detail reducer for large line series. It splits the visible points into roughly one
/// bucket per pixel column and, from each bucket, keeps only the minimum and maximum Y samples. The
/// result has at most ~2 points per column yet preserves the visual envelope of the signal, which is
/// what makes millions of points render in milliseconds.
/// </summary>
/// <remarks>
/// The X samples are assumed to be non-decreasing; callers that cannot guarantee this should render
/// the raw points instead. The decimator first narrows to the visible X window (with one point of
/// padding on each side to preserve line continuity at the edges) via binary search.
/// </remarks>
public static class MinMaxDecimator
{
    /// <summary>The output buffer size required for a given column count.</summary>
    public static int RequiredBufferSize(int columns) => (System.Math.Max(1, columns) * 2) + 4;

    /// <summary>
    /// Decimates <paramref name="xs"/>/<paramref name="ys"/> for the given visible window into
    /// <paramref name="output"/>, returning the number of points written.
    /// </summary>
    /// <param name="xs">Non-decreasing X samples.</param>
    /// <param name="ys">Y samples, same length as <paramref name="xs"/>.</param>
    /// <param name="visibleX">The visible X range; points outside (plus one neighbor each side) are dropped.</param>
    /// <param name="columns">The approximate number of pixel columns (buckets) available.</param>
    /// <param name="output">Destination buffer, at least <see cref="RequiredBufferSize"/> long.</param>
    /// <returns>The number of points written to <paramref name="output"/>.</returns>
    public static int Decimate(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        DataRange visibleX,
        int columns,
        Span<Point2D> output)
    {
        int n = xs.Length;
        if (n == 0 || ys.Length != n)
        {
            return 0;
        }

        // Narrow to the visible window (inclusive of one neighbor on each side for continuity).
        int start = LowerBound(xs, visibleX.Min);
        int end = UpperBound(xs, visibleX.Max);
        if (start > 0)
        {
            start--;
        }

        if (end < n)
        {
            end++;
        }

        int visibleCount = end - start;
        if (visibleCount <= 0)
        {
            return 0;
        }

        int buckets = System.Math.Max(1, columns);

        // When the visible points already fit in the available detail, copy them verbatim.
        if (visibleCount <= buckets * 2)
        {
            int copied = 0;
            for (int i = start; i < end && copied < output.Length; i++)
            {
                output[copied++] = new Point2D(xs[i], ys[i]);
            }

            return copied;
        }

        int written = 0;
        for (int b = 0; b < buckets; b++)
        {
            int bucketStart = start + (int)((long)b * visibleCount / buckets);
            int bucketEnd = start + (int)((long)(b + 1) * visibleCount / buckets);
            if (bucketEnd <= bucketStart)
            {
                continue;
            }

            int minIndex = bucketStart;
            int maxIndex = bucketStart;
            double minY = ys[bucketStart];
            double maxY = ys[bucketStart];

            for (int i = bucketStart + 1; i < bucketEnd; i++)
            {
                double y = ys[i];
                if (y < minY)
                {
                    minY = y;
                    minIndex = i;
                }
                else if (y > maxY)
                {
                    maxY = y;
                    maxIndex = i;
                }
            }

            // Emit the two extrema in index order so the polyline advances monotonically in X.
            int firstIndex = System.Math.Min(minIndex, maxIndex);
            int secondIndex = System.Math.Max(minIndex, maxIndex);

            if (written < output.Length)
            {
                output[written++] = new Point2D(xs[firstIndex], ys[firstIndex]);
            }

            if (secondIndex != firstIndex && written < output.Length)
            {
                output[written++] = new Point2D(xs[secondIndex], ys[secondIndex]);
            }
        }

        return written;
    }

    /// <summary>First index whose X is &gt;= <paramref name="value"/>, assuming ascending <paramref name="xs"/>.</summary>
    internal static int LowerBound(ReadOnlySpan<double> xs, double value)
    {
        int lo = 0;
        int hi = xs.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (xs[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>First index whose X is &gt; <paramref name="value"/>, assuming ascending <paramref name="xs"/>.</summary>
    internal static int UpperBound(ReadOnlySpan<double> xs, double value)
    {
        int lo = 0;
        int hi = xs.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (xs[mid] <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
