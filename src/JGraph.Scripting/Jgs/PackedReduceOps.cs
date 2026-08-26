using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The packed fast path under the MATLAB dimension reductions: when the subject is a packed double
/// array and the call is one the kernels understand, the answer comes straight from
/// <see cref="ReduceKernels"/> reading the storage where it lies — no flatten, no per-slice copies,
/// no boxed vector per column. Anything else returns false untouched, and the boxed wrapper above
/// runs exactly as before, so every error message and every odd argument shape keeps its one home.
/// </summary>
/// <remarks>
/// There is no threshold and no approximation here: every kernel replicates its boxed fold to the
/// bit (M92's Tier E), so the fast path is taken whenever it applies. The wrapper has already parsed
/// the words and slots — this class receives the parsed call, decides only whether the kernels
/// cover it, and mints the results the same shapes the boxed assembly would: a lone value is a
/// scalar, an empty join is an empty array, everything else is a packed array in the reduced shape.
/// </remarks>
internal static class PackedReduceOps
{
    /// <summary>
    /// The single-output reductions <c>WrapColumnwise</c> registers. <paramref name="order"/> is
    /// diff's repetition count (1 for everything else); <paramref name="all"/>, <paramref name="dim"/>
    /// and <paramref name="vecdim"/> are mutually exclusive, exactly as the wrapper parses them.
    /// </summary>
    public static bool TryColumnwise(
        string name, JgsValue subject, int? dim, int[]? vecdim, bool all, JgsValue[] extra,
        int order, bool omitNan, bool reverse, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!IsReducible(subject))
        {
            return false;
        }

        if (!TryConfigure(name, extra, out Family family, out double p, out bool population))
        {
            return false;
        }

        if (all)
        {
            // 'all' flattens to a single slice. The running reductions answer a bare vector there,
            // whose shape rules differ from the joined form's — boxed keeps that edge.
            if (family is Family.CumSum or Family.CumProd or Family.CumMax or Family.CumMin
                or Family.Diff)
            {
                return false;
            }

            NumericBuffer whole = subject.AsBuffer;
            result = ReduceOne(name, family, whole,
                new ReduceKernels.Split(1, whole.Length, 1), [1, 1], 1, order, omitNan, reverse,
                p, population);
            return true;
        }

        if (vecdim is not null)
        {
            // One pass per dimension, each leaving a singleton behind, exactly as the boxed loop
            // walks it. If a pass collapses the value to a scalar before the list runs out, the
            // remaining passes are the boxed Defer edge cases — hand the whole call back rather
            // than mimic them.
            JgsValue running = subject;
            for (int i = 0; i < vecdim.Length; i++)
            {
                if (!IsReducible(running))
                {
                    return false;
                }

                running = AlongOne(name, family, running, vecdim[i], order, omitNan, reverse,
                    p, population);
            }

            result = running;
            return true;
        }

        int[] dims = JgsMatrix.DimsOf(subject);
        int along = dim ?? JgsMatrix.DefaultDim(dims);
        if (along < 1)
        {
            return false; // the boxed path throws the dimension error in the same words
        }

        result = AlongOne(name, family, subject, along, order, omitNan, reverse, p, population);
        return true;
    }

    /// <summary>
    /// The reducing forms of <c>max</c>/<c>min</c> along one dimension: values and fold positions
    /// for every slice, positions already carrying the dialect's index base — linear into the whole
    /// array under <c>'linear'</c>, into the slice otherwise.
    /// </summary>
    public static bool TryExtremeAlong(
        JgsValue subject, int? named, bool takeMin, bool omitNan, bool linear, int indexBase,
        out JgsValue[] results)
    {
        results = [];
        if (!IsReducible(subject))
        {
            return false;
        }

        int[] dims = JgsMatrix.DimsOf(subject);
        int along = named ?? JgsMatrix.DefaultDim(dims);
        if (along < 1)
        {
            return false;
        }

        var split = SplitAlong(dims, along);
        NumericBuffer source = subject.AsBuffer;
        NumericBuffer values = JgsPacking.Allocate(split.Slices);
        NumericBuffer indices = JgsPacking.Allocate(split.Slices);
        ReduceKernels.Extreme(source, values, indices, split, takeMin, omitNan);

        // The kernel reports 0-based fold positions; the boxed path answers them shifted by the
        // dialect's base, or as positions in the flat array — slice s = o·inner + i keeps its j-th
        // element at o·inner·n + j·inner + i, the same arithmetic the storage was cut by.
        Span<double> at = indices.AsSpan();
        for (int s = 0; s < at.Length; s++)
        {
            if (linear)
            {
                long o = s / split.Inner;
                long i = s % split.Inner;
                at[s] = (o * split.Inner * split.Count) + i + ((long)at[s] * split.Inner) + indexBase;
            }
            else
            {
                at[s] += indexBase;
            }
        }

        int[] reduced = JgsMatrix.ShapeAlong(dims, along, 1);
        results =
        [
            MintScalars(values, reduced, JgsPackedKind.Number),
            MintScalars(indices, reduced, JgsPackedKind.Number),
        ];
        return true;
    }

    /// <summary>The <c>'all'</c> form: one extreme over the whole array and its linear position,
    /// already base-shifted — without the flat copy the boxed path starts from.</summary>
    public static bool TryExtremeAll(
        JgsValue subject, bool takeMin, bool omitNan, int indexBase, out JgsValue[] results)
    {
        results = [];
        if (!IsReducible(subject))
        {
            return false;
        }

        (double value, int at) = ReduceKernels.ExtremeFlat(subject.AsBuffer, takeMin, omitNan);
        results = [JgsValue.Number(value), JgsValue.Number(at + indexBase)];
        return true;
    }

    // --- One pass along one dimension -----------------------------------------------------------

    private enum Family
    {
        Sum, Prod, Mean, Rms, Variance, Any, All, Norm, CumSum, CumProd, CumMax, CumMin, Diff,
    }

    /// <summary>
    /// Whether the kernels cover this name with these extra arguments, and what the extras mean.
    /// A refusal here is not an error — a weight vector, a misplaced word, an unknown name all fall
    /// back to the boxed wrapper, which owns their behavior and their complaints.
    /// </summary>
    private static bool TryConfigure(
        string name, JgsValue[] extra, out Family family, out double p, out bool population)
    {
        p = 2;
        population = false;
        family = default;
        Family? known = name switch
        {
            "sum" => Family.Sum,
            "prod" => Family.Prod,
            "mean" => Family.Mean,
            "rms" => Family.Rms,
            "std" or "var" or "variance" => Family.Variance,
            "any" => Family.Any,
            "all" => Family.All,
            "vecnorm" => Family.Norm,
            "cumsum" => Family.CumSum,
            "cumprod" => Family.CumProd,
            "cummax" => Family.CumMax,
            "cummin" => Family.CumMin,
            "diff" => Family.Diff,
            _ => null, // median, mode, sort — the boxed builtins keep them whole
        };

        if (known is null)
        {
            return false;
        }

        family = known.Value;
        switch (family)
        {
            case Family.Variance:
                // The weight slot: absent or [] or 0 divides by n−1, 1 by n; a vector of weights
                // (or anything else) is the boxed SampleVariance's business.
                if (extra.Length == 0)
                {
                    return true;
                }

                if (extra.Length > 1)
                {
                    return false;
                }

                if (extra[0].Type == JgsType.Array && extra[0].ArrayLength == 0)
                {
                    return true;
                }

                if (extra[0].Type is JgsType.Number or JgsType.Bool)
                {
                    double weight = extra[0].AsNumber;
                    population = weight == 1;
                    return weight is 0 or 1;
                }

                return false;

            case Family.Norm:
                // The p slot; a placeholder or anything non-numeric falls back to the builtin's
                // own argument checking.
                if (extra.Length == 0)
                {
                    return true;
                }

                if (extra.Length == 1 && extra[0].Type == JgsType.Number)
                {
                    p = extra[0].AsNumber;
                    return true;
                }

                return false;

            default:
                // Everything else takes nothing between the array and the dimension; a stray
                // argument is the inner builtin's to refuse.
                return extra.Length == 0;
        }
    }

    private static JgsValue AlongOne(
        string name, Family family, JgsValue subject, int along, int order, bool omitNan,
        bool reverse, double p, bool population)
    {
        int[] dims = JgsMatrix.DimsOf(subject);
        var split = SplitAlong(dims, along);
        return ReduceOne(name, family, subject.AsBuffer, split, dims, along, order, omitNan,
            reverse, p, population);
    }

    private static JgsValue ReduceOne(
        string name, Family family, NumericBuffer source, ReduceKernels.Split split, int[] dims,
        int along, int order, bool omitNan, bool reverse, double p, bool population)
    {
        switch (family)
        {
            case Family.CumSum:
            case Family.CumProd:
            case Family.CumMax:
            case Family.CumMin:
            {
                NumericBuffer dest = JgsPacking.Allocate(split.Total);
                switch (family)
                {
                    case Family.CumSum:
                        ReduceKernels.CumulativeSum(source, dest, split, omitNan, reverse);
                        break;
                    case Family.CumProd:
                        ReduceKernels.CumulativeProduct(source, dest, split, omitNan, reverse);
                        break;
                    default:
                        ReduceKernels.CumulativeExtreme(
                            source, dest, split, family == Family.CumMin, omitNan, reverse);
                        break;
                }

                return MintShaped(dest, JgsMatrix.ShapeAlong(dims, along, split.Count));
            }

            case Family.Diff:
                return DifferenceTimes(source, split, dims, along, order);

            default:
            {
                NumericBuffer dest = JgsPacking.Allocate(split.Slices);
                switch (family)
                {
                    case Family.Sum:
                        ReduceKernels.Sum(source, dest, split, omitNan);
                        break;
                    case Family.Prod:
                        ReduceKernels.Product(source, dest, split, omitNan);
                        break;
                    case Family.Mean:
                        ReduceKernels.Mean(source, dest, split, omitNan);
                        break;
                    case Family.Rms:
                        ReduceKernels.RootMeanSquare(source, dest, split, omitNan);
                        break;
                    case Family.Variance:
                        ReduceKernels.Variance(source, dest, split, omitNan, population,
                            takeRoot: name == "std");
                        break;
                    case Family.Any:
                        ReduceKernels.Any(source, dest, split);
                        break;
                    case Family.All:
                        ReduceKernels.All(source, dest, split);
                        break;
                    default:
                        ReduceKernels.Norm(source, dest, split, p);
                        break;
                }

                JgsPackedKind kind = family is Family.Any or Family.All
                    ? JgsPackedKind.Bool
                    : JgsPackedKind.Number;
                return MintScalars(dest, JgsMatrix.ShapeAlong(dims, along, 1), kind);
            }
        }
    }

    /// <summary>Differencing applied <paramref name="order"/> times, each pass one shorter; a pass
    /// that would empty the slices ends it, because differencing nothing stays nothing.</summary>
    private static JgsValue DifferenceTimes(
        NumericBuffer source, ReduceKernels.Split split, int[] dims, int along, int order)
    {
        NumericBuffer current = source;
        bool owned = false;
        int length = split.Count;
        for (int pass = 0; pass < order; pass++)
        {
            if (length <= 1)
            {
                length = 0;
                break;
            }

            NumericBuffer next = JgsPacking.Allocate((long)split.Inner * (length - 1) * split.Outer);
            ReduceKernels.Differences(current, next, split with { Count = length });
            if (owned)
            {
                current.Dispose();
            }

            current = next;
            owned = true;
            length--;
        }

        if (length == 0)
        {
            if (owned)
            {
                current.Dispose();
            }

            return JgsValue.Array([]);
        }

        return MintShaped(current, JgsMatrix.ShapeAlong(dims, along, length));
    }

    // --- Shapes and minting ---------------------------------------------------------------------

    /// <summary>
    /// Whether this value takes the fast path at all: packing on, a non-empty packed array of plain
    /// doubles. The numeric-class gate keeps the sized integer classes on the boxed road, where
    /// their saturation and output-class rules live.
    /// </summary>
    private static bool IsReducible(JgsValue value) =>
        JgsPacking.Enabled
        && value.Type == JgsType.Array
        && value.IsPacked
        && value.NumericClass == JgsNumericClass.Double
        && value.ArrayLength > 0;

    /// <summary>The <c>(inner, n, outer)</c> decomposition of a reduction along <paramref name="along"/>
    /// — the same arithmetic <c>JgsMatrix.SlicesAlong</c> cuts by.</summary>
    private static ReduceKernels.Split SplitAlong(int[] dims, int along)
    {
        int inner = 1;
        for (int i = 0; i < along - 1 && i < dims.Length; i++)
        {
            inner *= dims[i];
        }

        int count = along <= dims.Length ? dims[along - 1] : 1;
        int outer = 1;
        for (int i = along; i < dims.Length; i++)
        {
            outer *= dims[i];
        }

        return new ReduceKernels.Split(inner, count, outer);
    }

    /// <summary>One value per slice, shaped as the boxed assembly shapes it: a lone value is a
    /// scalar (a Bool one for the truth reductions), anything more a packed array.</summary>
    private static JgsValue MintScalars(NumericBuffer dest, int[] reduced, JgsPackedKind kind)
    {
        if (dest.Length == 1)
        {
            double value = dest.AsSpan()[0];
            dest.Dispose();
            return kind == JgsPackedKind.Bool ? JgsValue.Bool(value != 0) : JgsValue.Number(value);
        }

        JgsValue packed = JgsValue.Packed(dest, kind);
        packed.ReshapeDims(reduced);
        return packed;
    }

    /// <summary>A whole vector per slice, shaped as the boxed join shapes it: empty stays an empty
    /// array, a lone value is a scalar, anything more a packed array along the original dimension.</summary>
    private static JgsValue MintShaped(NumericBuffer dest, int[] shape)
    {
        if (dest.Length == 0)
        {
            dest.Dispose();
            return JgsValue.Array([]);
        }

        if (dest.Length == 1)
        {
            double value = dest.AsSpan()[0];
            dest.Dispose();
            return JgsValue.Number(value);
        }

        JgsValue packed = JgsValue.Packed(dest);
        packed.ReshapeDims(shape);
        return packed;
    }
}
