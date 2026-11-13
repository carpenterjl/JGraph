using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The packed fast path under <c>filter(b, a, x)</c> for the half of the family that has no
/// feedback: when the denominator names nothing past <c>a(1)</c>, every output is its own sum of
/// taps and the answer comes from <see cref="FilterKernels"/> reading the storage where it lies.
/// A real denominator is a recurrence, one long dependency chain per slice; since ADR 0334 it is
/// walked here too, by <see cref="FilterKernels.RecurrenceAlong"/>, in the boxed loop's operation
/// order. Every other shape, class and error message keeps the boxed road.
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
    /// Whether a denominator with feedback takes the packed recurrence too (<c>JGRAPH_IIR_FAST</c>,
    /// on unless it is <c>0</c>; ADR 0334). Off, it keeps the boxed road, which is the
    /// measurement's off condition.
    /// </summary>
    internal static bool RecurrenceEnabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_IIR_FAST") != "0";

    /// <summary>
    /// The filtered signal, and — when <paramref name="wanted"/> asks — the delay line each slice
    /// finishes on, together with the shapes the boxed assembly would have given them. False leaves
    /// the call to that assembly untouched. <paramref name="initial"/> is one delay line for every
    /// slice, or — when <paramref name="initialPerSlice"/> — one per slice in <c>zf</c>'s own
    /// layout (<see cref="FinalConditionsShape"/>).
    /// </summary>
    public static bool TryFilter(
        double[] numerator, double[] denominator, double[]? initial, bool initialPerSlice,
        JgsValue signal, int[] dims, int dim, int wanted, out JgsValue[] results, out int[][] shapes)
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

        double[] carried;
        int stride = 0;
        if (initialPerSlice && initial is not null && delays > 0)
        {
            carried = initial;
            stride = delays;
        }
        else
        {
            carried = new double[Math.Max(delays, 1)];
            initial?.CopyTo(carried, 0);
        }

        NumericBuffer source = signal.AsBuffer;
        NumericBuffer answer = JgsPacking.AllocateForOverwrite(signal.ArrayLength);
        NumericBuffer? finals = wanted >= 2
            ? JgsPacking.Allocate((long)split.Inner * delays * split.Outer)
            : null;
        if (FilterKernels.IsFeedForward(denominator))
        {
            FilterKernels.FeedForwardAlong(source, answer, finals, split, taps, carried, stride);
        }
        else
        {
            // A real denominator: the recurrence, a chain per slice, reading and writing the
            // packed storage where it lies (ADR 0334). The feedback coefficients are normalised
            // and padded exactly as DigitalFilter.Filter does it.
            var feedback = new double[order];
            for (int i = 0; i < denominator.Length; i++)
            {
                feedback[i] = denominator[i] / a0;
            }

            FilterKernels.RecurrenceAlong(source, answer, finals, split, taps, feedback, carried, stride);
        }

        GC.KeepAlive(signal);

        results = finals is null
            ? [JgsValue.Packed(answer)]
            : [JgsValue.Packed(answer), JgsValue.Packed(finals)];
        shapes = finals is null
            ? [JgsMatrix.ShapeAlong(dims, dim, split.Count)]
            : [JgsMatrix.ShapeAlong(dims, dim, split.Count), FinalConditionsShape(dims, dim, delays)];
        return true;
    }

    /// <summary>
    /// The dimensions of the signal other than the one filtered, in order, with trailing
    /// singletons dropped: the slices' own shape, which <c>zi</c> and <c>zf</c> carry after the
    /// delays. A dimension past the signal's last counts as a singleton taken out.
    /// </summary>
    internal static int[] OtherDimensions(IReadOnlyList<int> dims, int dim)
    {
        int[] full = JgsMatrix.ShapeAlong(dims, dim, 1);
        var others = new List<int>(full.Length);
        for (int i = 0; i < full.Length; i++)
        {
            if (i != dim - 1)
            {
                others.Add(full[i]);
            }
        }

        while (others.Count > 0 && others[^1] == 1)
        {
            others.RemoveAt(others.Count - 1);
        }

        return [.. others];
    }

    /// <summary>
    /// R2025b's shape for <c>zf</c>: the delays first, then the other dimensions of the signal
    /// (<see cref="OtherDimensions"/>) — <c>[L 1]</c> for any vector, <c>[L 4]</c> for a 4-by-5
    /// filtered along its rows (ADR 0345). Slice <c>s</c>'s line is the run at <c>s · L</c>.
    /// </summary>
    internal static int[] FinalConditionsShape(IReadOnlyList<int> dims, int dim, int delays)
    {
        int[] others = OtherDimensions(dims, dim);
        if (others.Length == 0)
        {
            return [delays, 1];
        }

        var shape = new int[others.Length + 1];
        shape[0] = delays;
        others.CopyTo(shape, 1);
        return shape;
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
        && (RecurrenceEnabled || FilterKernels.IsFeedForward(denominator))
        && signal.Type == JgsType.Array
        && signal.IsPacked
        && signal.PackedKind == JgsPackedKind.Number
        && signal.NumericClass == JgsNumericClass.Double
        && signal.ArrayLength > 0;
}
