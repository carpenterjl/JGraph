namespace JGraph.Signal;

/// <summary>Which height a peak's width is measured at.</summary>
public enum WidthReference
{
    /// <summary>Half way between the peak and its prominence base.</summary>
    HalfProminence,

    /// <summary>Half the peak's own height, with the saddles as the search bounds.</summary>
    HalfHeight,
}

/// <summary>How a list of peaks is ordered before the count is capped.</summary>
public enum PeakOrder
{
    /// <summary>In the order they occur.</summary>
    None,

    /// <summary>Tallest first.</summary>
    Descending,

    /// <summary>Shortest first.</summary>
    Ascending,
}

/// <summary>
/// <c>findpeaks</c>: the local maxima of a signal, with the prominence and width of each.
/// </summary>
/// <remarks>
/// This is a transcription rather than a reimplementation, and deliberately so — the outputs are a
/// list whose order and membership are load-bearing, and every criterion is applied in a stated
/// order with a stated tie-break. The part that most repays reading MATLAB's own code is the
/// prominence base: it is found by one left-to-right pass with a stack of peaks and the lowest
/// valley below each, which is what makes a peak's base the far side of the nearest higher peak
/// rather than simply the nearest minimum.
/// </remarks>
public static class PeakFinding
{
    /// <summary>What a caller asks of a peak.</summary>
    public sealed class Criteria
    {
        /// <summary>The height a peak must exceed.</summary>
        public double MinHeight { get; set; } = double.NegativeInfinity;

        /// <summary>The prominence a peak must reach.</summary>
        public double MinProminence { get; set; }

        /// <summary>The narrowest a peak may be.</summary>
        public double MinWidth { get; set; }

        /// <summary>The widest a peak may be.</summary>
        public double MaxWidth { get; set; } = double.PositiveInfinity;

        /// <summary>How far apart in the location vector two peaks must lie.</summary>
        public double MinDistance { get; set; }

        /// <summary>How far a peak must stand above its immediate neighbours.</summary>
        public double Threshold { get; set; }

        /// <summary>How many peaks to keep.</summary>
        public int MaxCount { get; set; } = int.MaxValue;

        /// <summary>How the kept peaks are ordered.</summary>
        public PeakOrder Order { get; set; } = PeakOrder.None;

        /// <summary>Which height the width is measured at.</summary>
        public WidthReference Reference { get; set; } = WidthReference.HalfProminence;
    }

    /// <summary>The peaks found, one entry per peak.</summary>
    public readonly record struct Peaks(
        int[] Indices, double[] Heights, double[] Locations, double[] Widths, double[] Prominences);

    /// <summary>Finds the peaks of <paramref name="y"/> located at <paramref name="x"/>.</summary>
    public static Peaks Find(double[] y, double[] x, Criteria criteria)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(criteria);

        (int[] finite, int[] infinite, int[] inflect) = AllPeaks(y);
        List<int> kept = [];
        foreach (int i in finite)
        {
            if (!(y[i] > criteria.MinHeight))
            {
                continue;
            }

            double baseline = System.Math.Max(y[i - 1], y[i + 1]);
            if (y[i] - baseline >= criteria.Threshold)
            {
                kept.Add(i);
            }
        }

        // The extents are always computed: the prominence and the width are outputs in their own
        // right, and both criteria that filter on them need them anyway.
        (int[] peaks, double[] bases, double[,] widthBounds) = Extents(
            y, x, [.. kept], infinite, inflect, criteria);

        int[] order = SeparateAndOrder(y, x, peaks, criteria);
        int count = System.Math.Min(order.Length, criteria.MaxCount);
        var indices = new int[count];
        var heights = new double[count];
        var locations = new double[count];
        var widths = new double[count];
        var prominences = new double[count];
        for (int i = 0; i < count; i++)
        {
            int at = order[i];
            indices[i] = peaks[at];
            heights[i] = y[peaks[at]];
            locations[i] = x[peaks[at]];
            widths[i] = widthBounds[at, 1] - widthBounds[at, 0];
            prominences[i] = y[peaks[at]] - bases[at];
        }

        return new Peaks(indices, heights, locations, widths, prominences);
    }

    /// <summary>Every local maximum, every positive infinity, and every inflection between them.</summary>
    private static (int[] Peaks, int[] Infinite, int[] Inflect) AllPeaks(double[] y)
    {
        var infinite = new List<int>();
        var work = new double[y.Length + 2];
        work[0] = double.NaN;
        work[^1] = double.NaN;
        for (int i = 0; i < y.Length; i++)
        {
            if (double.IsPositiveInfinity(y[i]))
            {
                infinite.Add(i);
                work[i + 1] = double.NaN;
            }
            else
            {
                work[i + 1] = y[i];
            }
        }

        // The indices at which the padded signal changes value, which is what collapses a plateau
        // to a single sample before the sign of the difference is taken.
        var changed = new List<int> { 0 };
        for (int i = 0; i < work.Length - 1; i++)
        {
            bool differs = work[i] != work[i + 1];
            bool anyFinite = !double.IsNaN(work[i]) || !double.IsNaN(work[i + 1]);
            if (differs && anyFinite)
            {
                changed.Add(i + 1);
            }
        }

        // The signs are doubles rather than integers because a difference across the padding is
        // not a number, and MATLAB's sign carries that through: it makes the neighbouring pair
        // count as an inflection and never as a peak.
        var signs = new double[System.Math.Max(changed.Count - 1, 0)];
        for (int i = 0; i < signs.Length; i++)
        {
            double difference = work[changed[i + 1]] - work[changed[i]];
            signs[i] = double.IsNaN(difference) ? double.NaN : System.Math.Sign(difference);
        }

        var peaks = new List<int>();
        var inflect = new List<int>();
        for (int i = 0; i + 1 < signs.Length; i++)
        {
            if (signs[i + 1] - signs[i] < 0)
            {
                peaks.Add(changed[i + 1] - 1);
            }

            if (signs[i] != signs[i + 1])
            {
                inflect.Add(changed[i + 1] - 1);
            }
        }

        return ([.. peaks], [.. infinite], [.. inflect]);
    }

    /// <summary>The prominence base, the width, and the criteria that depend on them.</summary>
    private static (int[] Peaks, double[] Bases, double[,] Bounds) Extents(
        double[] y, double[] x, int[] peaks, int[] infinite, int[] inflect, Criteria criteria)
    {
        var finite = new double[y.Length];
        Array.Copy(y, finite, y.Length);
        foreach (int i in infinite)
        {
            finite[i] = double.NaN;
        }

        int[] allFinite = [.. AllPeaks(y).Peaks];
        (double[] bases, int[] leftSaddle, int[] rightSaddle) = PeakBases(finite, peaks, allFinite, inflect);

        var keep = new List<int>();
        for (int i = 0; i < peaks.Length; i++)
        {
            if (finite[peaks[i]] - bases[i] >= criteria.MinProminence)
            {
                keep.Add(i);
            }
        }

        var chosen = new int[keep.Count];
        var chosenBases = new double[keep.Count];
        var chosenLeft = new int[keep.Count];
        var chosenRight = new int[keep.Count];
        for (int i = 0; i < keep.Count; i++)
        {
            chosen[i] = peaks[keep[i]];
            chosenBases[i] = bases[keep[i]];
            chosenLeft[i] = leftSaddle[keep[i]];
            chosenRight[i] = rightSaddle[keep[i]];
        }

        double[,] bounds = Widths(finite, x, chosen, chosenBases, chosenLeft, chosenRight, criteria.Reference);

        var within = new List<int>();
        for (int i = 0; i < chosen.Length; i++)
        {
            double width = bounds[i, 1] - bounds[i, 0];
            if (criteria.MinWidth <= width && width <= criteria.MaxWidth)
            {
                within.Add(i);
            }
        }

        var outPeaks = new int[within.Count];
        var outBases = new double[within.Count];
        var outBounds = new double[within.Count, 2];
        for (int i = 0; i < within.Count; i++)
        {
            outPeaks[i] = chosen[within[i]];
            outBases[i] = chosenBases[within[i]];
            outBounds[i, 0] = bounds[within[i], 0];
            outBounds[i, 1] = bounds[within[i], 1];
        }

        return (outPeaks, outBases, outBounds);
    }

    /// <summary>
    /// The prominence base of every peak, by MATLAB's two passes of one stack walk: the left base
    /// is found going forwards and the right base by the same walk over the reversed signal.
    /// </summary>
    private static (double[] Bases, int[] LeftSaddle, int[] RightSaddle) PeakBases(
        double[] y, int[] peaks, int[] finite, int[] inflect)
    {
        (int[] leftBase, int[] leftSaddle) = LeftBase(y, peaks, finite, inflect);
        int[] reversedPeaks = Reverse(peaks);
        int[] reversedFinite = Reverse(finite);
        int[] reversedInflect = Reverse(inflect);
        (int[] rightBase, int[] rightSaddle) = LeftBase(y, reversedPeaks, reversedFinite, reversedInflect);
        Array.Reverse(rightBase);
        Array.Reverse(rightSaddle);

        var bases = new double[peaks.Length];
        for (int i = 0; i < peaks.Length; i++)
        {
            bases[i] = System.Math.Max(y[leftBase[i]], y[rightBase[i]]);
        }

        return (bases, leftSaddle, rightSaddle);
    }

    private static int[] Reverse(int[] values)
    {
        var copy = (int[])values.Clone();
        Array.Reverse(copy);
        return copy;
    }

    /// <summary>MATLAB's <c>getLeftBase</c>: one pass with a stack of peaks and the valley under each.</summary>
    private static (int[] Base, int[] Saddle) LeftBase(double[] y, int[] peaks, int[] finite, int[] inflect)
    {
        var baseIndex = new int[peaks.Length];
        var saddleIndex = new int[peaks.Length];
        var peakStack = new double[finite.Length + 1];
        var valleyStack = new double[finite.Length + 1];
        var valleyIndex = new int[finite.Length + 1];
        int n = 0;
        int i = 0;
        int j = 0;
        int k = 0;
        double valley = double.NaN;
        int at = 0;
        while (k < peaks.Length && i < inflect.Length)
        {
            while (i < inflect.Length && j < finite.Length && inflect[i] != finite[j])
            {
                valley = y[inflect[i]];
                at = inflect[i];
                if (double.IsNaN(valley))
                {
                    n = 0;
                }
                else
                {
                    while (n > 0 && valleyStack[n - 1] > valley)
                    {
                        n--;
                    }
                }

                i++;
            }

            if (i >= inflect.Length)
            {
                break;
            }

            double peak = y[inflect[i]];
            while (n > 0 && peakStack[n - 1] < peak)
            {
                if (valleyStack[n - 1] < valley)
                {
                    valley = valleyStack[n - 1];
                    at = valleyIndex[n - 1];
                }

                n--;
            }

            int saddle = at;
            while (n > 0 && peakStack[n - 1] <= peak)
            {
                if (valleyStack[n - 1] < valley)
                {
                    valley = valleyStack[n - 1];
                    at = valleyIndex[n - 1];
                }

                n--;
            }

            peakStack[n] = peak;
            valleyStack[n] = valley;
            valleyIndex[n] = at;
            n++;
            if (inflect[i] == peaks[k])
            {
                baseIndex[k] = at;
                saddleIndex[k] = saddle;
                k++;
            }

            i++;
            j++;
        }

        return (baseIndex, saddleIndex);
    }

    /// <summary>The two half-height crossings either side of each peak.</summary>
    private static double[,] Widths(
        double[] y, double[] x, int[] peaks, double[] bases, int[] left, int[] right, WidthReference reference)
    {
        int n = peaks.Length;
        var bounds = new double[n, 2];
        var heights = new double[n];
        var leftBound = new int[n];
        var rightBound = new int[n];
        if (reference == WidthReference.HalfHeight)
        {
            Array.Copy(left, leftBound, n);
            Array.Copy(right, rightBound, n);
            for (int i = 1; i < n; i++)
            {
                leftBound[i] = System.Math.Max(left[i], right[i - 1]);
            }

            for (int i = 0; i < n - 1; i++)
            {
                rightBound[i] = System.Math.Min(right[i], left[i + 1]);
            }

            for (int i = 0; i < n; i++)
            {
                if (leftBound[i] > peaks[i])
                {
                    leftBound[i] = left[i];
                }

                if (rightBound[i] < peaks[i])
                {
                    rightBound[i] = right[i];
                }
            }
        }
        else
        {
            Array.Copy(left, leftBound, n);
            Array.Copy(right, rightBound, n);
            Array.Copy(bases, heights, n);
        }

        for (int i = 0; i < n; i++)
        {
            double reference2 = (y[peaks[i]] + heights[i]) / 2;
            int index = peaks[i];
            while (index >= leftBound[i] && y[index] > reference2)
            {
                index--;
            }

            bounds[i, 0] = index < leftBound[i]
                ? x[leftBound[i]]
                : Cross(x[index], x[index + 1], y[index], y[index + 1], y[peaks[i]], heights[i]);

            index = peaks[i];
            while (index <= rightBound[i] && y[index] > reference2)
            {
                index++;
            }

            bounds[i, 1] = index > rightBound[i]
                ? x[rightBound[i]]
                : Cross(x[index], x[index - 1], y[index], y[index - 1], y[peaks[i]], heights[i]);
        }

        return bounds;
    }

    private static double Cross(double xa, double xb, double ya, double yb, double yc, double bc)
    {
        double value = xa + ((xb - xa) * ((0.5 * (yc + bc)) - ya) / (yb - ya));
        if (double.IsNaN(value))
        {
            return double.IsInfinity(bc) ? 0.5 * (xa + xb) : xb;
        }

        return value;
    }

    /// <summary>
    /// Drops peaks that stand too close to a taller one, then puts what is left in the asked-for
    /// order. The rejection walks the peaks tallest first, which is what makes it deterministic.
    /// </summary>
    private static int[] SeparateAndOrder(double[] y, double[] x, int[] peaks, Criteria criteria)
    {
        if (peaks.Length == 0 || criteria.MinDistance == 0)
        {
            var all = new int[peaks.Length];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = i;
            }

            return Order(y, peaks, all, criteria.Order);
        }

        var sorted = new int[peaks.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = i;
        }

        Array.Sort(sorted, (a, b) => y[peaks[b]].CompareTo(y[peaks[a]]));
        var locations = new double[peaks.Length];
        for (int i = 0; i < peaks.Length; i++)
        {
            locations[i] = x[peaks[sorted[i]]];
        }

        var deleted = new bool[peaks.Length];
        const double epsilon = 2.220446049250313e-16;
        for (int i = 0; i < deleted.Length; i++)
        {
            if (deleted[i])
            {
                continue;
            }

            for (int j = 0; j < deleted.Length; j++)
            {
                if (locations[j] - (locations[i] - criteria.MinDistance) > -epsilon
                    && locations[j] - (locations[i] + criteria.MinDistance) < epsilon)
                {
                    deleted[j] = true;
                }
            }

            deleted[i] = false;
        }

        var survivors = new List<int>();
        for (int i = 0; i < deleted.Length; i++)
        {
            if (!deleted[i])
            {
                survivors.Add(sorted[i]);
            }
        }

        int[] result = [.. survivors];
        if (criteria.Order == PeakOrder.None)
        {
            Array.Sort(result);
        }
        else if (criteria.Order == PeakOrder.Ascending)
        {
            Array.Reverse(result);
        }

        return result;
    }

    private static int[] Order(double[] y, int[] peaks, int[] index, PeakOrder order)
    {
        if (order == PeakOrder.None || index.Length == 0)
        {
            return index;
        }

        var sorted = (int[])index.Clone();
        var keys = new double[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            keys[i] = y[peaks[sorted[i]]];
        }

        int[] positions = new int[sorted.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = i;
        }

        Array.Sort(positions, (a, b) => order == PeakOrder.Descending
            ? keys[b].CompareTo(keys[a])
            : keys[a].CompareTo(keys[b]));
        var result = new int[sorted.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = sorted[positions[i]];
        }

        return result;
    }
}
