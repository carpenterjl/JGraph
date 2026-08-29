using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's matrix builders and the shape verbs that go with them (M102): <c>toeplitz</c>,
/// <c>hankel</c>, <c>blkdiag</c>, <c>compan</c>, <c>vander</c>, <c>hadamard</c>, <c>pascal</c>,
/// <c>rosser</c>, <c>wilkinson</c>, <c>invhilb</c> and <c>gallery</c>, together with
/// <c>repelem</c>, <c>shiftdim</c>, <c>ipermute</c> and <c>flipdim</c>.
/// </summary>
/// <remarks>
/// <para>
/// A named matrix is a rule about a pair of indices, and every builder here is written as one: no
/// row of rows is assembled and nothing is transposed on the way out, because
/// <see cref="TestMatrices"/> and <see cref="GalleryMatrices"/> write straight into the
/// column-major storage the rest of the engine already reads.
/// </para>
/// <para>
/// The four shape verbs share the observation that all of them are permutations of one array's
/// storage. <c>ipermute</c> is <c>permute</c> with the order read the other way round,
/// <c>shiftdim</c> is <c>permute</c> by a rotation, and <c>flipdim</c> is a reversal along one
/// direction — which <c>flip</c> now shares, having answered a matrix's columns for a flip along
/// any direction past the second.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The numeric classes <c>invhilb</c> is willing to answer in.</summary>
    private static readonly string[] FloatClasses = ["double", "single"];

    /// <summary>Registers the matrix-builder and shape builtins into <paramref name="env"/>.</summary>
    /// <param name="env">The scope to declare into.</param>
    /// <param name="host">Where a diagonal-conflict warning is written.</param>
    internal static void RegisterMatrixBuilderBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        // --- the named matrices -----------------------------------------------------------------
        Define("toeplitz", (args, line, col) =>
        {
            ArityRange("toeplitz", args, 1, 2, line, col);
            Complex[] column = ComplexElements("toeplitz", args[0], line, col);
            Complex[] row;
            if (args.Count == 2)
            {
                row = ComplexElements("toeplitz", args[1], line, col);
                if (column.Length > 0 && row.Length > 0 && column[0] != row[0])
                {
                    host.WriteErr("Warning: First element of input column does not match first element"
                        + " of input row.\n         Column wins diagonal conflict.\n");
                }
            }
            else
            {
                // One argument is a Hermitian Toeplitz: the argument is the row, and the column is
                // its conjugate, so a real vector gives the symmetric matrix everyone expects.
                row = column;
                column = new Complex[row.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    column[i] = Complex.Conjugate(row[i]);
                }
            }

            if (column.Length == 0 || row.Length == 0)
            {
                return JgsEmpty.Shaped(column.Length, row.Length);
            }

            var result = new Complex[column.Length * row.Length];
            for (int c = 0; c < row.Length; c++)
            {
                for (int r = 0; r < column.Length; r++)
                {
                    result[(c * column.Length) + r] = r >= c ? column[r - c] : row[c - r];
                }
            }

            // The (1,1) entry is the column's, whichever the row claims.
            result[0] = column[0];
            return ShapedComplex(result, column.Length, row.Length);
        });

        Define("hankel", (args, line, col) =>
        {
            ArityRange("hankel", args, 1, 2, line, col);
            Complex[] column = ComplexElements("hankel", args[0], line, col);
            Complex[] row;
            if (args.Count == 2)
            {
                row = ComplexElements("hankel", args[1], line, col);
                if (column.Length > 0 && row.Length > 0 && column[^1] != row[0])
                {
                    host.WriteErr("Warning: Last element of input column does not match first element"
                        + " of input row.\n         Column wins anti-diagonal conflict.\n");
                }
            }
            else
            {
                // One argument fills the anti-diagonals from the column and zero past it.
                row = new Complex[column.Length];
                if (column.Length > 0)
                {
                    row[0] = column[^1];
                }
            }

            if (column.Length == 0 || row.Length == 0)
            {
                return JgsEmpty.Shaped(column.Length, row.Length);
            }

            var result = new Complex[column.Length * row.Length];
            for (int c = 0; c < row.Length; c++)
            {
                for (int r = 0; r < column.Length; r++)
                {
                    int along = r + c;
                    result[(c * column.Length) + r] =
                        along < column.Length ? column[along] : row[along - column.Length + 1];
                }
            }

            return ShapedComplex(result, column.Length, row.Length);
        });

        Define("blkdiag", BlockDiagonal);

        Define("compan", (args, line, col) =>
        {
            Arity("compan", args, 1, line, col);
            if (!IsVectorShaped(args[0]) || JgsEmpty.IsEmptyArray(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:compan:NeedVectorInput",
                    "Input argument must be a vector.");
            }

            double[] coefficients = ToDoubles("compan", args[0], line, col);
            double[] flat = TestMatrices.Companion(coefficients, out int order);
            return ShapedReal(flat, order, order);
        });

        Define("vander", (args, line, col) =>
        {
            Arity("vander", args, 1, line, col);
            double[] points = NumericVector("vander", args, 0, line, col);
            return ShapedReal(TestMatrices.Vandermonde(points), points.Length, points.Length);
        });

        Define("hadamard", (args, line, col) =>
        {
            (IReadOnlyList<JgsValue> rest, JgsNumericClass? numericClass) =
                ClassNameTail("hadamard", args, null, line, col);
            Arity("hadamard", rest, 1, line, col);
            int n = Count("hadamard", rest, 0, line, col);
            if (!TestMatrices.IsHadamardOrder(n, out _, out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:hadamard:InvalidInput",
                    "n must be an integer and n, n/12 or n/20 must be a power of 2.");
            }

            return Classed(ShapedReal(TestMatrices.Hadamard(n), n, n), numericClass);
        });

        Define("pascal", (args, line, col) =>
        {
            (IReadOnlyList<JgsValue> rest, JgsNumericClass? numericClass) =
                ClassNameTail("pascal", args, null, line, col);
            ArityRange("pascal", rest, 1, 2, line, col);
            int n = Count("pascal", rest, 0, line, col);
            int kind = rest.Count == 2 ? Count("pascal", rest, 1, line, col) : 0;
            if (kind is < 0 or > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:pascal:InvalidArg2",
                    "Second argument must be 0, 1, or 2.");
            }

            return Classed(ShapedReal(TestMatrices.Pascal(Math.Max(n, 0), kind), n, n), numericClass);
        });

        // Answers bare: `rosser` on its own line is the matrix, not the handle (M84's lesson).
        env.Declare("rosser", JgsValue.Function(new BuiltinFunction("rosser", (args, line, col) =>
        {
            (IReadOnlyList<JgsValue> rest, JgsNumericClass? numericClass) =
                ClassNameTail("rosser", args, null, line, col);
            Arity("rosser", rest, 0, line, col);
            return Classed(ShapedReal(TestMatrices.Rosser(), 8, 8), numericClass);
        })
        { AutoCallsBare = true }));

        Define("wilkinson", (args, line, col) =>
        {
            (IReadOnlyList<JgsValue> rest, JgsNumericClass? numericClass) =
                ClassNameTail("wilkinson", args, null, line, col);
            Arity("wilkinson", rest, 1, line, col);
            int n = Math.Max(Count("wilkinson", rest, 0, line, col), 0);
            return Classed(ShapedReal(TestMatrices.Wilkinson(n), n, n), numericClass);
        });

        Define("invhilb", (args, line, col) =>
        {
            (IReadOnlyList<JgsValue> rest, JgsNumericClass? numericClass) =
                ClassNameTail("invhilb", args, FloatClasses, line, col);
            Arity("invhilb", rest, 1, line, col);
            int n = Math.Max(Count("invhilb", rest, 0, line, col), 0);
            return Classed(ShapedReal(TestMatrices.InverseHilbert(n), n, n), numericClass);
        });

        Define("gallery", (args, line, col) => TestMatrix(args, 1, line, col)[0],
            (args, wanted, line, col) => TestMatrix(args, wanted, line, col));

        // --- the shape verbs --------------------------------------------------------------------
        Define("repelem", RepeatElements);

        Define("shiftdim", (args, line, col) => ShiftDimensions(args, 1, line, col)[0],
            (args, wanted, line, col) => ShiftDimensions(args, wanted, line, col));

        Define("ipermute", (args, line, col) =>
        {
            Arity("ipermute", args, 2, line, col);
            double[] order = ToDoubles("ipermute", args[1], line, col);

            // permute's order says where each of the source's dimensions went; ipermute's says where
            // each of the result's came from, which is the same list read backwards.
            var inverse = new double[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int target = (int)order[i];
                if (target < 1 || target > order.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        "ipermute's order must use each dimension exactly once.");
                }

                inverse[target - 1] = i + 1;
            }

            return Permuted("ipermute", args[0], inverse, line, col);
        });

        Define("flipdim", (args, line, col) =>
        {
            Arity("flipdim", args, 2, line, col);
            return FlipAlong("flipdim", args[0], Count("flipdim", args, 1, line, col), line, col);
        });
    }

    /// <summary>
    /// The elements of <paramref name="value"/> as complex numbers, column-major, so one builder
    /// serves the real and the complex reading of the same construction.
    /// </summary>
    private static Complex[] ComplexElements(string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return [new Complex(value.AsNumber, 0)];
        }

        if (value.Type == JgsType.Complex)
        {
            return [value.AsComplex];
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a numeric vector.");
        }

        var result = new Complex[value.ArrayLength];
        for (int i = 0; i < result.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            result[i] = element.Type switch
            {
                JgsType.Complex => element.AsComplex,
                JgsType.Number or JgsType.Bool => new Complex(element.AsNumber, 0),
                _ => throw new JgsRuntimeException(line, col, $"{name} expects a numeric vector."),
            };
        }

        return result;
    }

    /// <summary>Shapes real column-major storage, keeping a one-by-one a plain number.</summary>
    private static JgsValue ShapedReal(double[] flat, int rows, int cols) =>
        flat.Length == 0 ? JgsEmpty.Shaped(Math.Max(rows, 0), Math.Max(cols, 0))
        : flat.Length == 1 ? JgsValue.Number(flat[0])
        : JgsMatrix.FromColumnMajor(flat, rows, cols);

    /// <summary>
    /// Shapes complex column-major storage, dropping to real storage when nothing in it is
    /// imaginary — which is what keeps <c>toeplitz([1 2 3])</c> a plain matrix.
    /// </summary>
    private static JgsValue ShapedComplex(Complex[] values, int rows, int cols)
    {
        bool imaginary = false;
        foreach (Complex value in values)
        {
            if (value.Imaginary != 0)
            {
                imaginary = true;
                break;
            }
        }

        if (!imaginary)
        {
            var reals = new double[values.Length];
            for (int i = 0; i < reals.Length; i++)
            {
                reals[i] = values[i].Real;
            }

            return ShapedReal(reals, rows, cols);
        }

        if (values.Length == 1)
        {
            return JgsValue.ComplexNum(values[0]);
        }

        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = JgsValue.ComplexNum(values[i]);
        }

        return JgsMatrix.FromElementsDims(boxed, [rows, cols]);
    }

    /// <summary>Whether <paramref name="value"/> has at most one row or at most one column.</summary>
    private static bool IsVectorShaped(JgsValue value)
    {
        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
        {
            return true;
        }

        int[] dims = SizeDims(value);
        if (dims.Length > 2)
        {
            return false;
        }

        return dims[0] == 1 || dims[1] == 1;
    }

    /// <summary>
    /// Splits a trailing class name off <paramref name="args"/>. Unlike the constructors'
    /// <c>ClassSuffix</c> there is no <c>'like'</c> form here — MATLAB's builders do not take one —
    /// and <paramref name="allowed"/> narrows the answer to the classes a family can hold.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Remaining, JgsNumericClass? Class) ClassNameTail(
        string name, IReadOnlyList<JgsValue> args, string[]? allowed, int line, int col)
    {
        if (args.Count == 0 || !IsTextScalar(args[^1]))
        {
            return (args, null);
        }

        string word = TextOf(args[^1]);
        if (allowed is not null && Array.IndexOf(allowed, word) < 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:notSupportedClass",
                "CLASSNAME must be 'double' or 'single'.");
        }

        if (JgsNumericClasses.Parse(word) is not { } named)
        {
            throw new JgsRuntimeException(line, col, $"{name}: JGraph has no '{word}' class.");
        }

        var rest = new List<JgsValue>(args.Count - 1);
        for (int i = 0; i < args.Count - 1; i++)
        {
            rest.Add(args[i]);
        }

        return (rest, named);
    }

    /// <summary>Stamps a numeric class onto a built matrix, when one was asked for.</summary>
    private static JgsValue Classed(JgsValue value, JgsNumericClass? numericClass) =>
        numericClass is { } named ? JgsNumericClasses.Stamp(value, named) : value;

    /// <summary>
    /// <c>blkdiag(A1, …, AN)</c>: the blocks laid corner to corner down a matrix of zeros. An empty
    /// block contributes neither a row nor a column, so it drops out entirely.
    /// </summary>
    private static JgsValue BlockDiagonal(IReadOnlyList<JgsValue> args, int line, int col)
    {
        int rows = 0;
        int cols = 0;
        var shapes = new (int Rows, int Cols)[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            int[] dims = SizeDims(args[i]);
            if (dims.Length > 2)
            {
                throw new JgsRuntimeException(line, col, "blkdiag takes 2-D blocks.");
            }

            shapes[i] = (dims[0], dims[1]);
            rows += dims[0];
            cols += dims[1];
        }

        if (rows == 0 || cols == 0)
        {
            return JgsEmpty.Shaped(rows, cols);
        }

        var elements = new JgsValue[rows * cols];
        JgsValue zero = JgsValue.Number(0);
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = zero;
        }

        int atRow = 0;
        int atCol = 0;
        for (int i = 0; i < args.Count; i++)
        {
            (int blockRows, int blockCols) = shapes[i];
            for (int c = 0; c < blockCols; c++)
            {
                for (int r = 0; r < blockRows; r++)
                {
                    elements[((atCol + c) * rows) + atRow + r] = ElementOf(args[i], r, c, blockRows);
                }
            }

            atRow += blockRows;
            atCol += blockCols;
        }

        return JgsMatrix.FromElements(elements, rows, cols);
    }

    /// <summary>One element of a value that may be a scalar, a vector or a matrix.</summary>
    private static JgsValue ElementOf(JgsValue value, int row, int column, int rows) =>
        value.Type == JgsType.Array ? value.ElementAt((column * rows) + row) : value;

    /// <summary>
    /// <c>repelem(v, n)</c> and <c>repelem(A, r1, …, rN)</c>: each element repeated in place, as
    /// many times as its own position asks for.
    /// </summary>
    /// <remarks>
    /// A repeat count is a scalar or one count per index along that direction, and the two read the
    /// same way once each direction is turned into a list saying which source index every output
    /// index came from. After that the copy is one walk over the output, whatever the rank.
    /// </remarks>
    private static JgsValue RepeatElements(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "repelem needs an array and a repeat count.");
        }

        int[] dims = SizeDims(args[0]);
        int given = args.Count - 1;
        if (given == 1)
        {
            if (dims.Length > 2 || (dims[0] != 1 && dims[1] != 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:repelem:twoInputNonVector",
                    "With two inputs, the first argument must be a vector."
                    + " Use three-input syntax for matrices.");
            }
        }

        // Trailing directions nobody named repeat once, which leaves them as they are.
        int rank = Math.Max(dims.Length, given);
        var padded = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            padded[d] = d < dims.Length ? dims[d] : 1;
        }

        var sources = new int[rank][];
        var grown = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            // The two-input form counts along the vector's own direction and leaves the other alone.
            int at = given == 1 ? (padded[0] == 1 && rank == 2 ? 1 : 0) : d;
            if (given == 1 ? d != at : d >= given)
            {
                sources[d] = Identity(padded[d]);
                grown[d] = padded[d];
                continue;
            }

            double[] counts = NumericVector("repelem", args, given == 1 ? 1 : d + 1, line, col);
            if (counts.Length != 1 && counts.Length != padded[d])
            {
                throw new JgsRuntimeException(line, col,
                    $"repelem: a repeat count for dimension {d + 1} must be a scalar or have"
                    + $" {padded[d]} elements.");
            }

            var map = new List<int>(padded[d]);
            for (int i = 0; i < padded[d]; i++)
            {
                double raw = counts.Length == 1 ? counts[0] : counts[i];
                int times = (int)Math.Round(raw);
                if (times < 0 || raw != Math.Floor(raw))
                {
                    throw new JgsRuntimeException(line, col,
                        "repelem: repeat counts must be non-negative integers.");
                }

                for (int k = 0; k < times; k++)
                {
                    map.Add(i);
                }
            }

            sources[d] = map.ToArray();
            grown[d] = map.Count;
        }

        long total = 1;
        foreach (int size in grown)
        {
            total *= size;
        }

        if (total == 0)
        {
            return args[0].Type == JgsType.Cell
                ? Celled([], grown)
                : JgsEmpty.Shaped(grown[0], rank == 2 ? grown[1] : 0);
        }

        var strides = new long[rank];
        long stride = 1;
        for (int d = 0; d < rank; d++)
        {
            strides[d] = stride;
            stride *= padded[d];
        }

        var elements = new JgsValue[total];
        var counter = new int[rank];
        for (long i = 0; i < total; i++)
        {
            long from = 0;
            for (int d = 0; d < rank; d++)
            {
                from += sources[d][counter[d]] * strides[d];
            }

            elements[i] = args[0].Type == JgsType.Array || args[0].Type == JgsType.Cell
                ? args[0].ElementAt((int)from)
                : args[0];
            for (int d = 0; d < rank; d++)
            {
                if (++counter[d] < grown[d])
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        return args[0].Type == JgsType.Cell
            ? Celled(elements, grown)
            : JgsMatrix.FromElementsDims(elements, grown);

        static int[] Identity(int length)
        {
            var map = new int[length];
            for (int i = 0; i < length; i++)
            {
                map[i] = i;
            }

            return map;
        }
    }

    /// <summary>A cell array in the given shape.</summary>
    private static JgsValue Celled(JgsValue[] elements, IReadOnlyList<int> dims)
    {
        JgsValue built = JgsValue.Cell(elements);
        built.ReshapeDims(dims);
        return built;
    }

    /// <summary>
    /// <c>shiftdim(A, n)</c> rotates the dimensions, <c>shiftdim(A)</c> strips the leading
    /// singletons and says how many it removed.
    /// </summary>
    private static JgsValue[] ShiftDimensions(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("shiftdim", args, 1, 2, line, col);
        int[] dims = SizeDims(args[0]);

        if (args.Count == 2)
        {
            int by = Count("shiftdim", args, 1, line, col);
            if (by == 0)
            {
                return Outputs(wanted, args[0], JgsValue.Number(0));
            }

            if (by < 0)
            {
                // Negative shifts put singleton dimensions in front instead of taking them away.
                var grown = new int[dims.Length - by];
                for (int d = 0; d < -by; d++)
                {
                    grown[d] = 1;
                }

                Array.Copy(dims, 0, grown, -by, dims.Length);
                return Outputs(wanted, Reshaped(args[0], grown, line, col), JgsValue.Number(by));
            }

            // A shift past the rank comes round again, which is what makes shiftdim(A, ndims(A)) a
            // no-op rather than an error.
            int steps = by % dims.Length;
            if (steps == 0)
            {
                return Outputs(wanted, args[0], JgsValue.Number(by));
            }

            var order = new double[dims.Length];
            for (int d = 0; d < dims.Length; d++)
            {
                order[d] = ((d + steps) % dims.Length) + 1;
            }

            return Outputs(
                wanted, Permuted("shiftdim", args[0], order, line, col), JgsValue.Number(by));
        }

        int leading = 0;
        while (leading < dims.Length && dims[leading] == 1)
        {
            leading++;
        }

        // A scalar is all singletons and stays a scalar: there is nothing under the ones to promote.
        if (leading >= dims.Length)
        {
            return Outputs(wanted, args[0], JgsValue.Number(0));
        }

        if (leading == 0)
        {
            return Outputs(wanted, args[0], JgsValue.Number(0));
        }

        var kept = new List<int>(dims.Length - leading);
        for (int d = leading; d < dims.Length; d++)
        {
            kept.Add(dims[d]);
        }

        while (kept.Count < 2)
        {
            kept.Add(1);
        }

        return Outputs(wanted, Reshaped(args[0], kept, line, col), JgsValue.Number(leading));
    }

    /// <summary>The same storage under a new shape.</summary>
    private static JgsValue Reshaped(
        JgsValue value, IReadOnlyList<int> dims, int line, int col)
    {
        if (value.Type != JgsType.Array && value.Type != JgsType.Cell)
        {
            return value;
        }

        if (value.Type == JgsType.Cell)
        {
            return Celled(value.BoxedElements(), dims);
        }

        double[] flat = FlattenColumnMajor("shiftdim", value, line, col);
        return JgsMatrix.FromColumnMajorDims(flat, dims);
    }

    /// <summary>
    /// <c>permute</c> by an order given as plain numbers, so <c>ipermute</c> and <c>shiftdim</c>
    /// can reach it without building an argument list to hand back to the interpreter.
    /// </summary>
    private static JgsValue Permuted(
        string name, JgsValue value, double[] order, int line, int col) =>
        PermuteDimensions(name, value, Numbers(order), line, col);

    /// <summary>
    /// A reversal along one direction, for any rank. <c>flip</c>, <c>flipdim</c> and the two
    /// two-dimensional spellings all end here.
    /// </summary>
    private static JgsValue FlipAlong(string name, JgsValue value, int dim, int line, int col)
    {
        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}: a dimension is at least 1.");
        }

        if (JgsEmpty.IsEmptyArray(value) || value.Type is not (JgsType.Array or JgsType.Cell))
        {
            return value;
        }

        int[] dims = SizeDims(value);
        if (dim > dims.Length || dims[dim - 1] == 1)
        {
            return value; // reversing a direction with one place along it changes nothing
        }

        long inner = 1;
        for (int d = 0; d < dim - 1; d++)
        {
            inner *= dims[d];
        }

        int length = dims[dim - 1];
        long outer = 1;
        for (int d = dim; d < dims.Length; d++)
        {
            outer *= dims[d];
        }

        // The reversal is a swap of two contiguous runs of `inner` elements, repeated for every
        // position past the direction: nothing is indexed element by element.
        var elements = new JgsValue[inner * length * outer];
        for (long o = 0; o < outer; o++)
        {
            long block = o * inner * length;
            for (int k = 0; k < length; k++)
            {
                long from = block + ((length - 1 - k) * inner);
                long to = block + (k * inner);
                for (long i = 0; i < inner; i++)
                {
                    elements[to + i] = value.ElementAt((int)(from + i));
                }
            }
        }

        return value.Type == JgsType.Cell
            ? Celled(elements, dims)
            : JgsMatrix.FromElementsDims(elements, dims);
    }
}
