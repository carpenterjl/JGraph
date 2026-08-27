using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The packed fast path under <c>filter(b, a, x)</c> for the half of the family that has no
/// feedback: when the denominator names nothing past <c>a(1)</c>, every output is its own sum of
/// taps and the answer comes from <see cref="FilterKernels"/> reading the storage where it lies.
/// A real denominator is a recurrence, one long dependency chain, and goes back to the boxed road
/// untouched — along with every other shape, class and error message.
/// </summary>
/// <remarks>
/// The boxed road copied the signal out flat, cut it into one array per slice, filtered each
/// through the transposed recurrence a sample at a time, and joined the pieces back — and the
/// recurrence spent half its multiplies on coefficients it had been told were zero. Ten million
/// samples through a sixty-four-tap smoother was most of a second of that.
/// </remarks>
internal static class PackedFilterOps
{
    /// <summary>
    /// The filtered signal, and — when <paramref name="wanted"/> asks — the delay line each slice
    /// finishes on, together with the shapes the boxed assembly would have given them. False leaves
    /// the call to that assembly untouched.
    /// </summary>
    public static bool TryFilter(
        double[] numerator, double[] denominator, double[]? initial, JgsValue signal,
        int[] dims, int dim, int wanted, out JgsValue[] results, out int[][] shapes)
    {
        results = [];
        shapes = [];
        if (!IsFilterable(numerator, denominator, signal, dim))
        {
            return false;
        }

        int order = Math.Max(numerator.Length, denominator.Length);
        int delays = order - 1;
        if (wanted >= 2 && delays == 0)
        {
            return false; // a filter with no delay line at all; the boxed join shapes that emptiness
        }

        ReduceKernels.Split split = PackedReduceOps.SplitAlong(dims, dim);
        if (split.Total != signal.ArrayLength || split.Count <= 0)
        {
            return false;
        }

        double a0 = denominator[0];
        var taps = new double[order];
        for (int i = 0; i < numerator.Length; i++)
        {
            taps[i] = numerator[i] / a0;
        }

        var carried = new double[Math.Max(delays, 1)];
        initial?.CopyTo(carried, 0);

        NumericBuffer source = signal.AsBuffer;
        NumericBuffer answer = JgsPacking.Allocate(signal.ArrayLength);
        NumericBuffer? finals = wanted >= 2
            ? JgsPacking.Allocate((long)split.Inner * delays * split.Outer)
            : null;
        FilterKernels.FeedForwardAlong(source, answer, finals, split, taps, carried);
        GC.KeepAlive(signal);

        results = finals is null
            ? [JgsValue.Packed(answer)]
            : [JgsValue.Packed(answer), JgsValue.Packed(finals)];
        shapes = finals is null
            ? [JgsMatrix.ShapeAlong(dims, dim, split.Count)]
            : [JgsMatrix.ShapeAlong(dims, dim, split.Count), JgsMatrix.ShapeAlong(dims, dim, delays)];
        return true;
    }

    /// <summary>
    /// Whether this call takes the fast path: packing on, a non-empty packed signal of plain
    /// doubles, a dimension that names itself, coefficients the kernel can normalise, and a
    /// denominator with no feedback in it.
    /// </summary>
    private static bool IsFilterable(
        double[] numerator, double[] denominator, JgsValue signal, int dim) =>
        JgsPacking.Enabled
        && dim >= 1
        && numerator.Length > 0
        && denominator.Length > 0
        && denominator[0] != 0
        && FilterKernels.IsFeedForward(denominator)
        && signal.Type == JgsType.Array
        && signal.IsPacked
        && signal.PackedKind == JgsPackedKind.Number
        && signal.NumericClass == JgsNumericClass.Double
        && signal.ArrayLength > 0;
}
