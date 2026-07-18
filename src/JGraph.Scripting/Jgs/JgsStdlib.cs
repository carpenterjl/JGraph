namespace JGraph.Scripting.Jgs;

/// <summary>
/// The numeric and array algorithms behind the JGS data-analysis builtins (<c>std</c>, <c>median</c>,
/// <c>unique</c>, <c>sort</c>, …). Pure functions over plain doubles and <see cref="JgsValue"/> arrays;
/// argument checking and registration live in <see cref="JgsBuiltins"/>. NaN propagates through every
/// statistic — scripts clean data first with <c>isnan</c> and a mask.
/// </summary>
internal static class JgsStdlib
{
    /// <summary>Sample variance (n − 1 denominator) of at least two values.</summary>
    public static double Variance(double[] values)
    {
        double mean = 0;
        foreach (double v in values)
        {
            mean += v;
        }

        mean /= values.Length;

        double sumSquares = 0;
        foreach (double v in values)
        {
            double d = v - mean;
            sumSquares += d * d;
        }

        return sumSquares / (values.Length - 1);
    }

    /// <summary>Median of a non-empty array (mean of the middle two for even counts).</summary>
    public static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Most frequent value of a non-empty array; the smallest wins a tie (MATLAB).</summary>
    public static double Mode(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);

        double best = sorted[0];
        int bestCount = 0;
        int i = 0;
        while (i < sorted.Length)
        {
            int runStart = i;
            while (i < sorted.Length && sorted[i].Equals(sorted[runStart]))
            {
                i++;
            }

            // Sorted ascending, so on ties the first (smallest) run is kept.
            if (i - runStart > bestCount)
            {
                bestCount = i - runStart;
                best = sorted[runStart];
            }
        }

        return best;
    }

    /// <summary>The p-th percentile (0–100) of a non-empty array, by linear interpolation.</summary>
    public static double Percentile(double[] values, double p)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);

        double rank = p / 100.0 * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double t = rank - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * t);
    }

    /// <summary>Running sums: result[i] = values[0] + … + values[i].</summary>
    public static double[] CumulativeSum(double[] values)
    {
        var result = new double[values.Length];
        double acc = 0;
        for (int i = 0; i < values.Length; i++)
        {
            acc += values[i];
            result[i] = acc;
        }

        return result;
    }

    /// <summary>Running products: result[i] = values[0] × … × values[i].</summary>
    public static double[] CumulativeProduct(double[] values)
    {
        var result = new double[values.Length];
        double acc = 1;
        for (int i = 0; i < values.Length; i++)
        {
            acc *= values[i];
            result[i] = acc;
        }

        return result;
    }

    /// <summary>Adjacent differences: result[i] = values[i + 1] − values[i] (length n − 1).</summary>
    public static double[] Differences(double[] values)
    {
        var result = new double[Math.Max(0, values.Length - 1)];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = values[i + 1] - values[i];
        }

        return result;
    }

    /// <summary>
    /// A sorted copy of a homogeneous numeric or string array; <paramref name="descending"/> flips the
    /// order. Returns null when the array mixes kinds (the caller raises the error).
    /// </summary>
    public static JgsValue[]? Sort(JgsValue[] elements, bool descending)
    {
        JgsValue[]? sorted = SortAscending(elements);
        if (sorted is not null && descending)
        {
            Array.Reverse(sorted);
        }

        return sorted;
    }

    /// <summary>
    /// The sorted distinct values of a homogeneous numeric or string array; null when mixed.
    /// </summary>
    public static JgsValue[]? Unique(JgsValue[] elements)
    {
        JgsValue[]? sorted = SortAscending(elements);
        if (sorted is null || sorted.Length == 0)
        {
            return sorted;
        }

        var distinct = new List<JgsValue> { sorted[0] };
        for (int i = 1; i < sorted.Length; i++)
        {
            if (!JgsValue.AreEqual(sorted[i], sorted[i - 1]))
            {
                distinct.Add(sorted[i]);
            }
        }

        return distinct.ToArray();
    }

    private static JgsValue[]? SortAscending(JgsValue[] elements)
    {
        var copy = (JgsValue[])elements.Clone();
        if (Array.TrueForAll(copy, static v => v.Type is JgsType.Number or JgsType.Bool))
        {
            Array.Sort(copy, static (a, b) => a.AsNumber.CompareTo(b.AsNumber));
            return copy;
        }

        if (Array.TrueForAll(copy, static v => v.Type == JgsType.String))
        {
            Array.Sort(copy, static (a, b) => string.CompareOrdinal(a.AsString, b.AsString));
            return copy;
        }

        return null;
    }

    /// <summary>Deep equality: arrays element-by-element (recursively), scalars by value.</summary>
    public static bool DeepEquals(JgsValue left, JgsValue right)
    {
        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.BoxedElements();
            JgsValue[] b = right.BoxedElements();
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!DeepEquals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return JgsValue.AreEqual(left, right);
    }
}
