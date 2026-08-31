using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The numeric and array algorithms behind the JGS data-analysis builtins (<c>std</c>, <c>median</c>,
/// <c>cumsum</c>, …). Pure functions over plain doubles and <see cref="JgsValue"/> arrays;
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
        // A sort over doubles puts every NaN in front, so the middle of a run holding one is a
        // real reading rather than the missing answer it should be. There is no median of a run
        // with a hole in it -- stepping over the hole is what 'omitnan' asks for, and the caller
        // that asked has already taken them out. A selection does not order the NaNs anywhere in
        // particular, so the run is asked directly rather than read off a front element (M120).
        foreach (double value in values)
        {
            if (double.IsNaN(value))
            {
                return double.NaN;
            }
        }

        double[] scratch = (double[])values.Clone();
        int mid = scratch.Length / 2;
        if (scratch.Length % 2 == 1)
        {
            Span<int> middle = [mid];
            SelectKernels.PartialSort(scratch, middle);
            return scratch[mid];
        }

        Span<int> pair = [mid - 1, mid];
        SelectKernels.PartialSort(scratch, pair);
        return (scratch[mid - 1] + scratch[mid]) / 2.0;
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
    /// Deep equality: arrays element-by-element (recursively), scalars by value. NaN is unequal to
    /// itself, which is what <c>isequal</c> reports; <paramref name="nanEqual"/> switches to the
    /// <c>isequaln</c> reading, where two NaNs in the same position match.
    /// </summary>
    public static bool DeepEquals(JgsValue left, JgsValue right, bool nanEqual = false)
    {
        static bool IsOneElementArray(JgsValue value) =>
            value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsNd;

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            // Sizes must agree, which is what MATLAB's isequal means by equal — and comparing
            // through JgsMatrix rather than raw element order is what lets a shaped matrix and a
            // value still in the older array-of-rows form compare as the same matrix. N-D shapes
            // must match in full: a 2x3x4 is never equal to the 2x12 that holds the same numbers.
            if (!JgsMatrix.DimsOf(left).AsSpan().SequenceEqual(JgsMatrix.DimsOf(right)))
            {
                return false;
            }

            int rows = JgsMatrix.RowCount(left);
            int cols = JgsMatrix.ColCount(left);

            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (!DeepEquals(JgsMatrix.At(left, r, c), JgsMatrix.At(right, r, c), nanEqual))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // A one-element array and a bare number are both 1-by-1, and size is what isequal compares, so
        // they have to match (M52). They did not, which made every "did this answer what I expected"
        // check on a reduction of a single value fail for a reason the script could not see: size
        // reported 1 1 for both, == answered true, and only isequal disagreed.
        if (IsOneElementArray(left) && right.Type is JgsType.Number or JgsType.Bool)
        {
            return DeepEquals(left.ElementAt(0), right, nanEqual);
        }

        if (IsOneElementArray(right) && left.Type is JgsType.Number or JgsType.Bool)
        {
            return DeepEquals(left, right.ElementAt(0), nanEqual);
        }

        // A cell is equal element by element, the same reading as an array — which is what lets a
        // script check a split, a fieldnames list or a cellfun result against a literal. Without it
        // isequal fell through to reference equality and answered false for two identical cells,
        // so the natural way to assert about text was quietly always wrong (M52).
        if (left.Type == JgsType.Cell && right.Type == JgsType.Cell)
        {
            JgsValue[] ours = left.AsCell;
            JgsValue[] theirs = right.AsCell;
            if (ours.Length != theirs.Length
                || JgsMatrix.RowCount(left) != JgsMatrix.RowCount(right))
            {
                return false;
            }

            for (int i = 0; i < ours.Length; i++)
            {
                if (!DeepEquals(ours[i], theirs[i], nanEqual))
                {
                    return false;
                }
            }

            return true;
        }

        // A struct is equal when it holds the same fields with the same values. MATLAB does not care
        // what order the fields were added in, only that both sides name the same set.
        if (left.Type == JgsType.Struct && right.Type == JgsType.Struct)
        {
            Dictionary<string, JgsValue> mine = left.AsStruct;
            Dictionary<string, JgsValue> yours = right.AsStruct;
            if (mine.Count != yours.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, JgsValue> field in mine)
            {
                if (!yours.TryGetValue(field.Key, out JgsValue? other) || !DeepEquals(field.Value, other, nanEqual))
                {
                    return false;
                }
            }

            return true;
        }

        if (left.Type == JgsType.Image && right.Type == JgsType.Image)
        {
            return ImagesEqual(left.AsImage, right.AsImage);
        }

        // MATLAB's isequal compares logicals and doubles by value: isequal(true, 1) is true,
        // so a mask can be checked against a plain [1 0] literal. NaN is the exception — it matches
        // nothing, itself included, unless the caller asked for the isequaln reading.
        if (left.Type is JgsType.Number or JgsType.Bool && right.Type is JgsType.Number or JgsType.Bool)
        {
            double x = left.AsNumber;
            double y = right.AsNumber;
            return double.IsNaN(x) || double.IsNaN(y) ? nanEqual && double.IsNaN(x) && double.IsNaN(y) : x == y;
        }

        return JgsValue.AreEqual(left, right);
    }

    private static bool ImagesEqual(JGraph.Imaging.ImageBuffer a, JGraph.Imaging.ImageBuffer b)
    {
        if (a.Height != b.Height || a.Width != b.Width || a.Channels != b.Channels)
        {
            return false;
        }

        ReadOnlySpan<double> pa = a.Pixels;
        ReadOnlySpan<double> pb = b.Pixels;
        for (int i = 0; i < pa.Length; i++)
        {
            if (pa[i] != pb[i])
            {
                return false;
            }
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return true;
    }
}
