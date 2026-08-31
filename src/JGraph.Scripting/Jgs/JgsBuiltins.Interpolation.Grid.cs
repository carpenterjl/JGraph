using System;
using System.Collections.Generic;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The one grid reader behind <c>interp2</c>, <c>interp3</c> and <c>interpn</c> (M101).
/// </summary>
/// <remarks>
/// <para>
/// The three names differ in exactly two ways and in nothing else. They have two, three and any
/// number of directions; and the first two name their directions the way <c>meshgrid</c> does — x
/// across the columns, y down the rows — where <c>interpn</c> names them the way the array is
/// indexed. Both differences are a permutation applied to the arguments before anything else
/// happens, which is why one function serves all three and why <c>interp2</c> and <c>interp3</c>
/// gained four documented forms each here without gaining an implementation.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>interp2</c>, <c>interp3</c> and <c>interpn</c> in every documented form.
    /// </summary>
    /// <param name="name">Which of the three was called, for diagnostics and for the permutation.</param>
    /// <param name="args">The call's arguments.</param>
    /// <param name="rank">How many directions the name has, or zero to read it off the samples.</param>
    /// <param name="host">Where a warning about a method that had to be changed is written.</param>
    /// <param name="line">The line the call was made on.</param>
    /// <param name="col">The column the call was made at.</param>
    private static JgsValue SampleGridded(
        string name, IReadOnlyList<JgsValue> args, int rank, JGraphScriptGlobals host, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least the samples to read.");
        }

        // A word is where the data stop: everything before it is positional, and at most one value
        // may follow it, which is what to answer outside the grid.
        int wordAt = args.Count;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                wordAt = i;
                break;
            }
        }

        string method = wordAt < args.Count
            ? GridMethodWord(name, args, wordAt, line, col)
            : "linear";
        bool hasFill = wordAt + 1 < args.Count;
        double fill = hasFill ? Num(name, args, wordAt + 1, line, col) : double.NaN;
        if (wordAt + 2 < args.Count)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes at most a method and a value to use outside the grid after its data.");
        }

        var positional = new List<JgsValue>();
        for (int i = 0; i < wordAt; i++)
        {
            positional.Add(args[i]);
        }

        if (method == "makima")
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'makima' over more than one direction is not available here; its cross terms are "
                + "not the ones MATLAB uses, and answering with a different surface would be wrong quietly. "
                + "'linear', 'nearest', 'cubic' and 'spline' are the methods that are.");
        }

        (JgsValue samples, JgsValue[] axisArguments, JgsValue[] queryArguments, int times) =
            SplitGridForm(name, positional, ref rank, line, col);

        int[] dims = GridDimensions(samples, rank, line, col);
        double[] values = FlattenColumnMajor(name, samples, line, col);
        double[][] axes = ReadAxes(name, axisArguments, dims, rank, line, col);

        method = SettleMethod(method, axes, host);
        bool extrapolate = !hasFill && method == "spline";

        (double[][] points, int[] answerDims) = times >= 0
            ? RefinedPoints(axes, times)
            : ReadQueryPoints(name, queryArguments, rank, line, col);

        if (times >= 0 && rank == 1)
        {
            // A refinement answers in the shape it was given, and a vector of samples has an
            // orientation the one direction it runs along cannot record.
            int[] shape = SizeDims(samples);
            answerDims = shape.Length == 2 && shape[0] == 1 ? [1, answerDims[0]] : [answerDims[0], 1];
        }

        var sampler = new GridSampler(axes, values, dims, MethodOf(method));
        int count = points.Length == 0 ? 0 : points[0].Length;
        var answer = new double[count];

        // A grain gets a sampler of its own because the one above keeps its working indices and
        // weights in fields; what it does not get is a second copy of the grid or of the spline
        // slopes, which is the expensive half and is read only (M120).
        ParallelKernels.For(count, ParallelKernels.ComputeBoundThreshold, null, (start, length) =>
        {
            GridSampler mine = sampler.ForAnotherThread();
            Span<double> point = stackalloc double[rank];
            for (int q = start; q < start + length; q++)
            {
                for (int d = 0; d < rank; d++)
                {
                    point[d] = points[d][q];
                }

                answer[q] = mine.Sample(point, extrapolate, fill);
            }
        });

        return ShapedNumbers(answer, answerDims);
    }

    /// <summary>
    /// The method word, refused with MathWorks' identifier when it names nothing at all. The
    /// message is this repository's rather than MATLAB's, because MATLAB's lists four methods a
    /// grid reader here does not take and telling a caller to try one of those would be worse than
    /// saying nothing; the identifier is what a script branches on and that is MathWorks'.
    /// </summary>
    private static string GridMethodWord(
        string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        string word = Str(name, args, at, line, col);
        foreach (string candidate in GridMethods)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:BadInterpTypeErrId",
            $"'{word}' is not an interpolation method here; the methods are "
            + "'linear', 'nearest', 'cubic', 'spline' and 'makima'.");
    }

    /// <summary>
    /// Which of the four shapes a call has: the samples alone, the samples and a refinement count,
    /// the samples and the points to read them at, or the grid as well.
    /// </summary>
    /// <returns>
    /// The samples, the arguments naming the grid (empty when it is implied), the arguments naming
    /// the points (empty when the call is a refinement), and how many times to refine, or −1.
    /// </returns>
    private static (JgsValue Samples, JgsValue[] Axes, JgsValue[] Queries, int Times) SplitGridForm(
        string name, List<JgsValue> positional, ref int rank, int line, int col)
    {
        int p = positional.Count;
        if (p <= 2)
        {
            // interp2(V), interp2(V, k) and their siblings: the grid is 1..n along each direction
            // and the points are every place a run of refinements puts one.
            if (rank == 0)
            {
                rank = RankOf(positional[0]);
            }

            int times = 1;
            if (p == 2)
            {
                double asked = ScalarOf(name, positional[1], line, col);
                times = (int)Math.Floor(asked);
                if (times < 0 || double.IsNaN(asked))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the number of refinements must not be negative.");
                }
            }

            return (positional[0], [], [], times);
        }

        if (rank == 0)
        {
            // interpn works its number of directions out from what it was handed. The samples come
            // first when the grid is implied and in the middle when it is given, and the two are
            // told apart by which reading makes the samples' own shape add up.
            int implied = p - 1;
            if (RankOf(positional[0]) == implied)
            {
                rank = implied;
            }
            else if (p % 2 == 1 && RankOf(positional[(p - 1) / 2]) == (p - 1) / 2)
            {
                rank = (p - 1) / 2;
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "interpn takes interpn(V, Xq1, ..., Xqn) or interpn(X1, ..., Xn, V, Xq1, ..., Xqn).");
            }
        }

        if (p == rank + 1)
        {
            return (positional[0], [], Permuted(name, positional.GetRange(1, rank), rank), -1);
        }

        if (p == (2 * rank) + 1)
        {
            return (
                positional[rank],
                Permuted(name, positional.GetRange(0, rank), rank),
                Permuted(name, positional.GetRange(rank + 1, rank), rank),
                -1);
        }

        throw name == "interp3"
            ? new JgsRuntimeException(line, col, "MATLAB:interp3:nargin",
                "Wrong number of input arguments.")
            : new JgsRuntimeException(line, col,
                $"{name} takes {rank + 1} or {(2 * rank) + 1} data arguments, but got {p}.");
    }

    /// <summary>
    /// The arguments in the order the array's own directions run. <c>meshgrid</c> puts x across the
    /// columns and y down the rows, so the first two are swapped for <c>interp2</c> and
    /// <c>interp3</c>; <c>ndgrid</c> already agrees with the array, so <c>interpn</c> is left alone.
    /// </summary>
    private static JgsValue[] Permuted(string name, List<JgsValue> given, int rank)
    {
        var ordered = given.ToArray();
        if (name != "interpn" && rank >= 2)
        {
            (ordered[0], ordered[1]) = (ordered[1], ordered[0]);
        }

        return ordered;
    }

    /// <summary>How many directions a value has, counting a vector as one however it is oriented.</summary>
    private static int RankOf(JgsValue value)
    {
        int[] dims = SizeDims(value);
        if (dims.Length <= 2 && (dims.Length < 2 || dims[0] == 1 || dims[1] == 1))
        {
            return 1;
        }

        int rank = dims.Length;
        while (rank > 2 && dims[rank - 1] == 1)
        {
            rank--;
        }

        return rank;
    }

    /// <summary>The grid's size along each direction, padded with ones for a vector of samples.</summary>
    private static int[] GridDimensions(JgsValue samples, int rank, int line, int col)
    {
        int[] shape = SizeDims(samples);
        var dims = new int[rank];
        if (RankOf(samples) == 1 && rank == 1)
        {
            dims[0] = ElementCount(samples);
        }
        else
        {
            for (int d = 0; d < rank; d++)
            {
                dims[d] = d < shape.Length ? shape[d] : 1;
            }
        }

        foreach (int size in dims)
        {
            if (size < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:DegenerateGridErrId",
                    "Interpolation requires at least two sample points for each grid dimension.");
            }
        }

        return dims;
    }

    /// <summary>
    /// The coordinates along each direction. An argument may be the vector of them or the whole
    /// grid <c>meshgrid</c> or <c>ndgrid</c> builds, in which case the coordinates are the line
    /// through it along that direction — every other one repeats.
    /// </summary>
    private static double[][] ReadAxes(
        string name, JgsValue[] given, int[] dims, int rank, int line, int col)
    {
        var axes = new double[rank][];
        for (int d = 0; d < rank; d++)
        {
            if (given.Length == 0)
            {
                var implied = new double[dims[d]];
                for (int i = 0; i < implied.Length; i++)
                {
                    implied[i] = i + 1;
                }

                axes[d] = implied;
                continue;
            }

            JgsValue argument = given[d];
            double[] flat = FlattenColumnMajor(name, argument, line, col);
            if (RankOf(argument) == 1)
            {
                axes[d] = flat;
            }
            else
            {
                int[] shape = SizeDims(argument);
                int stride = 1;
                for (int i = 0; i < d && i < shape.Length; i++)
                {
                    stride *= shape[i];
                }

                int length = d < shape.Length ? shape[d] : 1;
                var picked = new double[length];
                for (int i = 0; i < length; i++)
                {
                    picked[i] = flat[i * stride];
                }

                axes[d] = picked;
            }

            if (axes[d].Length != dims[d])
            {
                throw new JgsRuntimeException(line, col,
                    "MATLAB:griddedInterpolant:CompVecValueMismatchErrId",
                    $"Sample points vector corresponding to grid dimension {d + 1} must contain "
                    + $"{dims[d]} elements.");
            }
        }

        return axes;
    }

    /// <summary>
    /// The points a call named: one list per direction, all the same length. Query arrays of the
    /// same shape are read point by point; a set of vectors each lying along its own direction
    /// names the whole grid of their combinations instead, which is what lets a row of x against a
    /// column of y describe a surface.
    /// </summary>
    /// <remarks>
    /// Which of the two a call meant is decided by orientation and not by size. Two rows of
    /// different lengths are neither — they are not the same shape and they do not lie along
    /// different directions — and MATLAB refuses them, which is the case that settles that the rule
    /// is about orientation.
    /// </remarks>
    private static (double[][] Points, int[] Dims) ReadQueryPoints(
        string name, JgsValue[] queries, int rank, int line, int col)
    {
        var flat = new double[rank][];
        for (int d = 0; d < rank; d++)
        {
            flat[d] = FlattenColumnMajor(name, queries[d], line, col);
        }

        bool sameShape = true;
        int[] first = SizeDims(queries[0]);
        for (int d = 1; d < rank && sameShape; d++)
        {
            sameShape = SameShape(first, SizeDims(queries[d]));
        }

        if (sameShape)
        {
            return (flat, first);
        }

        for (int d = 0; d < rank; d++)
        {
            if (!LiesAlong(queries[d], d))
            {
                throw new JgsRuntimeException(line, col,
                    "MATLAB:griddedInterpolant:InputMixSizeErrId",
                    "Query coordinates input arrays must have the same size.");
            }
        }

        return Grid(flat);
    }

    /// <summary>Whether a value is a single number, or a vector running along direction <paramref name="d"/>.</summary>
    private static bool LiesAlong(JgsValue value, int d)
    {
        int[] dims = SizeDims(value);
        int count = 1;
        foreach (int size in dims)
        {
            count *= size;
        }

        if (count == 1)
        {
            return true;
        }

        for (int i = 0; i < dims.Length; i++)
        {
            if (dims[i] != (i == d ? count : 1))
            {
                return false;
            }
        }

        return d < dims.Length;
    }

    /// <summary>Every combination of one coordinate from each direction, in the array's own order.</summary>
    private static (double[][] Points, int[] Dims) Grid(double[][] axes)
    {
        var dims = new int[axes.Length];
        int total = 1;
        for (int d = 0; d < axes.Length; d++)
        {
            dims[d] = axes[d].Length;
            total *= dims[d];
        }

        var points = new double[axes.Length][];
        int stride = 1;
        for (int d = 0; d < axes.Length; d++)
        {
            var lane = new double[total];
            for (int i = 0; i < total; i++)
            {
                lane[i] = axes[d][(i / stride) % dims[d]];
            }

            points[d] = lane;
            stride *= dims[d];
        }

        return (points, dims);
    }

    /// <summary>
    /// The grid a refinement asks for: each direction halved <paramref name="times"/> over, which
    /// puts <c>2^times · (n − 1) + 1</c> points where there were <c>n</c>.
    /// </summary>
    private static (double[][] Points, int[] Dims) RefinedPoints(double[][] axes, int times)
    {
        var refined = new double[axes.Length][];
        for (int d = 0; d < axes.Length; d++)
        {
            double[] axis = axes[d];
            int steps = 1 << times;
            var finer = new double[((axis.Length - 1) * steps) + 1];
            for (int i = 0; i < axis.Length - 1; i++)
            {
                for (int k = 0; k < steps; k++)
                {
                    finer[(i * steps) + k] = axis[i] + ((axis[i + 1] - axis[i]) * k / steps);
                }
            }

            finer[^1] = axis[^1];
            refined[d] = finer;
        }

        return Grid(refined);
    }

    /// <summary>
    /// The method a call can actually be answered with. Cubic convolution is written in cell widths
    /// rather than positions and reads two samples either side, so a grid that is uneven or short
    /// cannot carry it; MATLAB says so and changes the method, and so does this.
    /// </summary>
    private static string SettleMethod(string method, double[][] axes, JGraphScriptGlobals host)
    {
        if (method != "cubic")
        {
            return method;
        }

        foreach (double[] axis in axes)
        {
            if (axis.Length < 3)
            {
                host.WriteErr(
                    $"Warning: The 'cubic' method requires at least 3 points in each dimension.\n"
                    + "Reverting to the default 'linear' method because this condition is not met.\n");
                return "linear";
            }
        }

        foreach (double[] axis in axes)
        {
            if (!IsEvenlySpaced(axis))
            {
                host.WriteErr(
                    $"Warning: The 'cubic' method requires the grid to have a uniform spacing.\n"
                    + "Switching the method from 'cubic' to 'spline' because this condition is not met.\n");
                return "spline";
            }
        }

        return method;
    }

    private static bool IsEvenlySpaced(double[] axis)
    {
        double step = (axis[^1] - axis[0]) / (axis.Length - 1);
        double tolerance = Math.Abs(step) * 1e-10;
        for (int i = 1; i < axis.Length; i++)
        {
            if (Math.Abs(axis[i] - axis[i - 1] - step) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static GridMethod MethodOf(string method) => method switch
    {
        "nearest" => GridMethod.Nearest,
        "cubic" => GridMethod.Cubic,
        "spline" => GridMethod.Spline,
        _ => GridMethod.Linear,
    };
}
