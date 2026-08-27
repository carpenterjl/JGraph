using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The packed fast path under MATLAB's <c>sort</c>: when the subject is a packed double array and
/// the options are ones the kernels understand, the answer comes from <see cref="SortKernels"/>
/// reading the storage where it lies. Anything else returns false untouched and the boxed wrapper
/// runs exactly as before, so the odd forms and every error message keep their one home.
/// </summary>
/// <remarks>
/// <para>
/// The boxed road cost more than a sort. Every slice was flattened, copied into its own array,
/// boxed one <see cref="JgsValue"/> per element, sorted through a comparison delegate over those
/// boxes, and joined back — and its second output was worse than that: the positions were recovered
/// afterwards by searching the input for each sorted value in turn, which is quadratic, so
/// <c>[B, I] = sort(x)</c> on a hundred thousand elements was already minutes of work.
/// </para>
/// <para>
/// What the kernels have to match is not a fold order but a tie rule, and the boxed one is
/// MATLAB's: values compared with <c>&lt;</c> so that <c>-0</c> and <c>+0</c> tie, equal values
/// left in the order they arrived, NaN lifted out and put back at the end
/// <c>MissingPlacement</c> names. <c>'abs'</c> and anything else the options can say fall through.
/// </para>
/// </remarks>
internal static class PackedSortOps
{
    /// <summary>
    /// <c>sort</c> along one dimension, values and — when <paramref name="wanted"/> asks for it —
    /// the position in its own slice that each value came from, already carrying
    /// <paramref name="indexBase"/>. False leaves the call to the boxed wrapper untouched.
    /// </summary>
    public static bool TryOrder(
        string name, JgsValue subject, int? dim, bool all, JgsValue[] extra, int wanted,
        int indexBase, out JgsValue[] results)
    {
        results = [];
        if (!string.Equals(name, "sort", StringComparison.Ordinal) || all || !IsSortable(subject))
        {
            return false;
        }

        if (!TryOptions(extra, out bool descending, out bool missingFirst))
        {
            return false;
        }

        int[] dims = JgsMatrix.DimsOf(subject);
        int along = dim ?? JgsMatrix.DefaultDim(dims);
        if (along < 1)
        {
            return false; // the boxed path throws the dimension error in the same words
        }

        ReduceKernels.Split split = PackedReduceOps.SplitAlong(dims, along);
        if (split.Total != subject.ArrayLength)
        {
            return false; // a shape the wrapper and the storage do not agree on is the boxed road's
        }

        NumericBuffer source = subject.AsBuffer;
        NumericBuffer values = JgsPacking.Allocate(subject.ArrayLength);
        NumericBuffer? positions = wanted >= 2 ? JgsPacking.Allocate(subject.ArrayLength) : null;
        SortKernels.SortAlong(source, values, positions, split, descending, missingFirst, indexBase);
        GC.KeepAlive(subject);

        results = positions is null
            ? [Mint(values, dims)]
            : [Mint(values, dims), Mint(positions, dims)];
        return true;
    }

    /// <summary>
    /// The option words this path answers, read the way the boxed <c>sort</c> reads them. A word it
    /// does not know — <c>'abs'</c>, a weight, a second direction — sends the whole call back, so
    /// the refusal and its wording stay in one place.
    /// </summary>
    private static bool TryOptions(JgsValue[] extra, out bool descending, out bool missingFirst)
    {
        descending = false;
        missingFirst = false;
        string placement = "auto";
        bool directed = false;
        for (int i = 0; i < extra.Length; i++)
        {
            if (extra[i].Type != JgsType.String)
            {
                return false;
            }

            string word = extra[i].AsString.ToLowerInvariant();
            if (word is "ascend" or "asc" or "descend" or "desc")
            {
                if (directed)
                {
                    return false;
                }

                directed = true;
                descending = word is "descend" or "desc";
                continue;
            }

            if (word is not ("missingplacement" or "comparisonmethod"))
            {
                return false;
            }

            if (i + 1 >= extra.Length || extra[i + 1].Type != JgsType.String)
            {
                return false;
            }

            string value = extra[++i].AsString.ToLowerInvariant();
            if (word == "missingplacement")
            {
                if (value is not ("auto" or "first" or "last"))
                {
                    return false;
                }

                placement = value;
            }
            else if (value is not ("auto" or "real"))
            {
                // 'abs' orders by magnitude and settles ties by angle — a comparison, not a sort
                // over doubles, and the boxed path is where it lives.
                return false;
            }
        }

        // 'auto' is last in reading order either way round, which puts it first when descending.
        missingFirst = placement == "first" || (placement == "auto" && descending);
        return true;
    }

    /// <summary>
    /// Whether this value takes the fast path: packing on, a non-empty packed array of plain
    /// doubles. A logical array is left to the boxed road, which has its own rules about what class
    /// comes back out.
    /// </summary>
    private static bool IsSortable(JgsValue value) =>
        JgsPacking.Enabled
        && value.Type == JgsType.Array
        && value.IsPacked
        && value.PackedKind == JgsPackedKind.Number
        && value.NumericClass == JgsNumericClass.Double
        && value.ArrayLength > 0;

    /// <summary>
    /// The result, shaped as the boxed join shapes it: a lone value is a scalar, everything else the
    /// shape it went in as, since a sort keeps every slice the length it found it.
    /// </summary>
    private static JgsValue Mint(NumericBuffer buffer, int[] dims)
    {
        if (buffer.Length == 1)
        {
            double lone = buffer.AsSpan()[0];
            buffer.Dispose();
            return JgsValue.Number(lone);
        }

        JgsValue packed = JgsValue.Packed(buffer);
        packed.ReshapeDims(dims);
        return packed;
    }
}
