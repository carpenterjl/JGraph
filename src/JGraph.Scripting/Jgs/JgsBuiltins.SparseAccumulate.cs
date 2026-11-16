using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>ismember</c> against a one-element set and <c>accumarray</c>'s sparse output, as R2025b
/// answers them for a sparse argument (open item 345, ADR 0381; probes p8b_a, p8b_e).
/// </summary>
/// <remarks>
/// R2025b's <c>ismember.m</c> answers a one-element set with <c>a == b</c>, so a sparse operand
/// on either side makes the answer sparse, and <c>locb</c> is <c>double(lia)</c>; a larger set reads
/// both full and answers dense. <c>accumarray</c> answers sparse when its values are sparse or its
/// sixth argument asks, and then refuses what a sparse matrix cannot hold.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registered at the end of <see cref="WrapSparseLeftovers"/>.</summary>
    private static void WrapSparseMembership(JgsEnvironment env)
    {
        WrapOverSparse(env, "ismember", (inner, args, wanted, line, col) =>
        {
            if (args.Count != 2 || !MembershipOperand(args[0]) || !MembershipOperand(args[1])
                || (MemberElementCount(args[0]) != 0 && MemberElementCount(args[1]) != 1))
            {
                return null;
            }

            // ismember.m's class rule comes first: two classes, neither double, is refused.
            string classA = MemberClassName(args[0]), classB = MemberClassName(args[1]);
            if (classA != classB && classA != "double" && classB != "double")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ISMEMBER:InvalidInputsDataType",
                    $"Input A of class {classA} and input B of class {classB} must be the same class, unless one is double.");
            }

            int[] dims = args[0].Type == JgsType.Sparse ? [args[0].SparseExtent.Rows, args[0].SparseExtent.Cols] : SizeDims(args[0]);
            if (MemberElementCount(args[0]) == 0)
            {
                // An empty A answers false(size(A)) and zeros(size(A)), dense (i_numa0).
                JgsValue none = EmptyLogical(dims[0], dims[1]);
                return wanted >= 2 ? [none, JgsMatrix.FromColumnMajorDims([], dims)] : [none];
            }

            // A one-element set is a == b (NaN never equal), sparse with a sparse operand.
            Complex[] a = MemberValues(args[0], line, col);
            Complex b = MemberValues(args[1], line, col)[0];
            var hits = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                hits[i] = a[i] == b ? 1 : 0;
            }

            CscMatrix found = CscMatrix.FromColumnMajor(hits, dims[0], dims[1]);
            return wanted >= 2 ? [LogicalSparseOf(found), JgsValue.Sparse(found)] : [LogicalSparseOf(found)];
        });

        if (!env.Builtins.TryGet("accumarray", out JgsValue existing) || existing.Type != JgsType.Function
            || existing.AsCallable is not BuiltinFunction accumulate)
        {
            return;
        }

        env.Builtins.Register("accumarray", JgsValue.Function(new BuiltinFunction("accumarray", (args, line, col) =>
            AnySparseArgument(args) || args.Count == 6 ? SparseAccumArray(accumulate, args, line, col) : accumulate.Call(args, line, col))
        {
            MultiOutput = accumulate.MultiOutput,
            KeepsStringArguments = accumulate.KeepsStringArguments,
            BindsAnsAsStatement = accumulate.BindsAnsAsStatement,
            AutoCallsBare = accumulate.AutoCallsBare,
            KnowsWhenDiscarded = accumulate.KnowsWhenDiscarded,
            TakesOutputCount = accumulate.TakesOutputCount,
        }));
    }

    private static bool MembershipOperand(JgsValue value) =>
        value.Type is JgsType.Sparse or JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String
        || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsTime && value.Type != JgsType.Cell
            && SizeDims(value).Length == 2);

    private static string MemberClassName(JgsValue value) =>
        value.Type == JgsType.String || value.IsCharMatrix ? "char"
        : IsLogicalValue(value) ? "logical"
        : value.NumericClass == JgsNumericClass.Single ? "single"
        : value.NumericClass.IsInteger() ? value.NumericClass.ToString().ToLowerInvariant()
        : "double";

    /// <summary>An operand's elements column-major as complex numbers, a char's as its codes.</summary>
    private static Complex[] MemberValues(JgsValue value, int line, int col) =>
        value.Type == JgsType.Sparse ? SparseColumnMajor(value)
        : value.Type == JgsType.String ? value.AsString.Select(static c => new Complex(c, 0)).ToArray()
        : value.Type is JgsType.Number or JgsType.Bool ? [new Complex(value.AsNumber, 0)]
        : ColumnComplex("ismember", value, line, col);

    private static int MemberElementCount(JgsValue value) => value.Type switch
    {
        JgsType.Sparse => value.SparseExtent.Rows * value.SparseExtent.Cols,
        JgsType.Array => value.ArrayLength,
        _ => 1,
    };

    /// <summary>
    /// <c>accumarray</c> with a sparse argument or a sixth one (probes p8b_a, p8b_e). The answer is
    /// sparse when the values are or <c>issparse</c> asks; a sparse value list beside an explicit
    /// false is <c>badClassValInput</c>, sparse subscripts <c>badIndInput</c>. For a sparse answer the
    /// subscripts may have at most two columns, the values must be double or single, the fill zero,
    /// and a column of subscripts takes a size <c>[N 1]</c>. Without a function the bins are summed
    /// in arrival order, in single for single values; a function's answers must be double or single.
    /// </summary>
    private static JgsValue SparseAccumArray(BuiltinFunction inner, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("accumarray", args, 2, 6, line, col);
        if (args[0].Type == JgsType.Sparse)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:accumarray:badIndInput",
                "First input must be a real, full, numeric matrix or a cell vector.");
        }

        bool sparseValues = args[1].Type == JgsType.Sparse;
        bool? asked = null;
        if (args.Count == 6)
        {
            JgsValue flag = args[5];
            bool scalarFlag = flag.Type is JgsType.Number or JgsType.Bool
                || (flag.Type == JgsType.Array && flag.ArrayLength == 1 && !flag.IsCharMatrix && !flag.IsStringArray);
            if (!scalarFlag)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:accumarray:badIsSparseInput", "Sixth input must be a logical scalar.");
            }

            asked = (flag.Type == JgsType.Array ? flag.ElementAt(0) : flag).AsNumber != 0;
            if (sparseValues && asked == false)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:accumarray:badClassValInput",
                    "Second input must be a full numeric, logical, or char vector or scalar.");
            }
        }

        JgsValue values = args[1].Type == JgsType.Sparse
            ? (args[1].SparseExtent is (1, 1) && !args[1].IsComplexSparse ? SparseDenseScalar(args[1]) : SparseDense(args[1]))
            : args[1];
        JgsValue[] dense = [args[0], values, .. args.Skip(2).Take(3)];
        if (!(sparseValues || asked == true))
        {
            return inner.Call(dense, line, col);
        }

        // --- a sparse answer ---
        (int[] rowsAt, int[]? colsAt) = AccumSubscripts(args[0], line, col);
        if (rowsAt.Length == 0 && colsAt is null && args[0].Type != JgsType.Cell && SubscriptColumns(args[0]) > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:accumarray:nonmatrixSparseOutput",
                "First input may not have more than 2 columns for sparse output.");
        }

        bool floatValues = !IsLogicalValue(values) && !values.IsCharMatrix && !values.IsStringArray && values.Type != JgsType.String
            && values.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single;
        if (!floatValues)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:accumarray:nonfloatValues",
                "Input values for sparse accumulation must be double or single.");
        }

        if (args.Count >= 5 && !(args[4].Type == JgsType.Array && args[4].ArrayLength == 0)
            && (ColumnComplex("accumarray", args[4], line, col) is not [var fill] || fill != Complex.Zero))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:accumarray:nonzeroFillvalSparseOutput",
                "Fifth input must be the real double value zero for sparse output.");
        }

        if (colsAt is null && args.Count >= 3 && !(args[2].Type == JgsType.Array && args[2].ArrayLength == 0))
        {
            double[] size = Array.ConvertAll(ColumnComplex("accumarray", args[2], line, col), static z => z.Real);
            if (size.Length != 2 || size[1] != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:accumarray:badSzInputVecInd",
                    "If the first input is a column vector, then the third input must be of the form [N 1].");
            }
        }

        if (rowsAt.Length == 0)
        {
            // Whatever the subscripts' reader left to the dense road, in its checks and its words.
            return DenseAsSparse(inner.Call(dense.Take(Math.Min(dense.Length, 5)).ToArray(), line, col), "accumarray", line, col);
        }

        // One value a subscript row, or one for them all; a matrix of values is refused (a_sp_matrix_v).
        int count = rowsAt.Length;
        Complex[] given = values.Type is JgsType.Number or JgsType.Bool ? [new Complex(values.AsNumber, 0)] : ColumnComplex("accumarray", values, line, col);
        int[] valueDims = values.Type == JgsType.Array ? SizeDims(values) : [1, 1];
        bool vector = valueDims.Length == 2 && (valueDims[0] == 1 || valueDims[1] == 1);
        if (given.Length != 1 && (!vector || given.Length != count))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:accumarray:badSizeValInputMatInd",
                "Second input must be a vector with one element for each row in the first input, or a scalar.");
        }

        int rows = 0, cols = 1;
        for (int k = 0; k < count; k++)
        {
            rows = Math.Max(rows, rowsAt[k]);
            if (colsAt is not null)
            {
                cols = Math.Max(cols, colsAt[k]);
            }
        }

        if (args.Count >= 3 && !(args[2].Type == JgsType.Array && args[2].ArrayLength == 0))
        {
            double[] size = Array.ConvertAll(ColumnComplex("accumarray", args[2], line, col), static z => z.Real);
            rows = Math.Max(rows, (int)size[0]);
            if (size.Length > 1)
            {
                cols = Math.Max(cols, (int)size[1]);
            }
        }

        bool single = values.NumericClass == JgsNumericClass.Single;
        bool complexValues = values.IsComplexSparse || IsComplexDense(values);
        var bins = new Dictionary<(int, int), Complex>();
        if (args.Count >= 4 && args[3].Type == JgsType.Function)
        {
            // Each bin's values, in arrival order, handed to the function as a column of the values'
            // class; its answers must be one double or single number each (a_sp_fun_*).
            IJgsCallable reducer = args[3].AsCallable;
            var groups = new SortedDictionary<(int, int), List<Complex>>(Comparer<(int, int)>.Create(
                static (x, y) => x.Item2 != y.Item2 ? x.Item2.CompareTo(y.Item2) : x.Item1.CompareTo(y.Item1)));
            for (int k = 0; k < count; k++)
            {
                (int, int) key = (rowsAt[k] - 1, colsAt is null ? 0 : colsAt[k] - 1);
                if (!groups.TryGetValue(key, out List<Complex>? members))
                {
                    groups[key] = members = [];
                }

                members.Add(given.Length == 1 ? given[0] : given[k]);
            }

            foreach (((int, int) key, List<Complex> members) in groups)
            {
                Complex[] group = [.. members];
                JgsValue column = !complexValues
                    ? JgsMatrix.FromColumnMajor(Array.ConvertAll(group, static z => z.Real), group.Length, 1)
                    : ComplexStorage(group, [group.Length, 1], preserveComplex: true);
                if (single)
                {
                    column = JgsNumericClasses.Stamp(column, JgsNumericClass.Single);
                }

                JgsValue reduced = reducer.Call([column], line, col);
                if (reduced.Type is JgsType.Cell or JgsType.String or JgsType.Struct || IsLogicalValue(reduced) || reduced.IsCharMatrix
                    || reduced.IsStringArray || !(reduced.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:sparsfcn:nondoubleFunctionOutput",
                        "Function output values for sparse accumulation must be double.");
                }

                Complex[] one = reduced.Type is JgsType.Number ? [new Complex(reduced.AsNumber, 0)] : ColumnComplex("accumarray", reduced, line, col);
                if (one.Length != 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:sparsfcn:nonScalarOutput",
                        $"Output from '{SourceTextOf("accumarray", args[3], line, col, matlab: true)}' is non-scalar.");
                }

                single |= reduced.NumericClass == JgsNumericClass.Single;
                bins[key] = one[0];
            }
        }
        else
        {
            for (int k = 0; k < count; k++)
            {
                (int, int) key = (rowsAt[k] - 1, colsAt is null ? 0 : colsAt[k] - 1);
                Complex v = given.Length == 1 ? given[0] : given[k];
                bins[key] = bins.TryGetValue(key, out Complex held)
                    ? single ? new Complex(RoundToSingle((float)held.Real + (float)v.Real), RoundToSingle((float)held.Imaginary + (float)v.Imaginary)) : held + v
                    : v;
            }
        }

        var stored = JgsComplexSparse.FromTriplets(rows, cols, bins.Select(static b => (b.Key.Item1, b.Key.Item2, b.Value)));
        return single ? SingleSparseOf(stored, keepFlag: false) : SparseOfComplex(stored, keepFlag: complexValues && stored.AnyImaginary);
    }

    private static int SubscriptColumns(JgsValue subs) =>
        subs.Type == JgsType.Array ? JgsMatrix.ColCount(subs) : 1;

    /// <summary>
    /// The subscripts as row and column lists (columns null for a column of subscripts): a numeric
    /// matrix of one or two columns, or a cell of one or two vectors. Empty lists hand the call back
    /// (more columns, or whatever the dense road refuses in its own words).
    /// </summary>
    private static (int[] Rows, int[]? Cols) AccumSubscripts(JgsValue subs, int line, int col)
    {
        static int[]? Whole(Complex[] values)
        {
            var whole = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i].Real;
                if (values[i].Imaginary != 0 || v < 1 || v != Math.Floor(v) || v > int.MaxValue)
                {
                    return null;
                }

                whole[i] = (int)v;
            }

            return whole;
        }

        if (subs.Type == JgsType.Cell)
        {
            JgsValue[] parts = subs.AsCell;
            if (parts.Length is < 1 or > 2 || Array.Exists(parts, static p => p.Type is not (JgsType.Array or JgsType.Number) || p.IsCharMatrix))
            {
                return ([], null);
            }

            int[]? first = Whole(ColumnComplex("accumarray", parts[0], line, col));
            int[]? second = parts.Length == 2 ? Whole(ColumnComplex("accumarray", parts[1], line, col)) : null;
            if (first is null || (parts.Length == 2 && (second is null || second.Length != first.Length)))
            {
                return ([], null);
            }

            return (first, second);
        }

        if (subs.Type is not (JgsType.Array or JgsType.Number) || subs.IsCharMatrix || subs.IsStringArray || subs.IsNd)
        {
            return ([], null);
        }

        int columns = SubscriptColumns(subs);
        if (columns > 2)
        {
            return ([], null);
        }

        int[]? flat = Whole(subs.Type == JgsType.Number ? [new Complex(subs.AsNumber, 0)] : ColumnComplex("accumarray", subs, line, col));
        if (flat is null || flat.Length == 0)
        {
            return ([], null);
        }

        if (columns == 1)
        {
            return (flat, null);
        }

        int n = flat.Length / 2;
        return (flat[..n], flat[n..]);
    }
}
