using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>num2cell</c>, <c>mode</c> and the complex <c>median</c> in MATLAB code, read off R2025b's own
/// num2cell.m, mode.m and median.m (open item 346, ADR 0386).
/// </summary>
/// <remarks>
/// <para>
/// <c>num2cell(A)</c> keeps each element's class (<c>num2cell(single([1 2]))</c> holds singles, a
/// logical holds logicals) and wraps each element of a cell in a cell; <c>num2cell(A, dims)</c>
/// permutes the named dimensions to the front and cuts the array into blocks of their size, as
/// num2cell.m does, with its refusals (<c>MATLAB:badsubscript</c>, <c>MATLAB:permute:*</c>).
/// </para>
/// <para>
/// <c>[M, F, C] = mode(X, dim)</c>: the most frequent value (the smallest on a tie, a NaN never equal to
/// another), its count as a double, and a cell of every value with that count, a sorted column each;
/// an empty answers NaN (0 for an integer or logical), a count of 0 and empties.
/// </para>
/// <para>
/// <c>median</c> of a complex dense array sorts by magnitude and then angle and averages the middle
/// two as median.m's <c>meanof</c> does (<c>a + (b - a)/2</c>, or <c>(a + b)/2</c> where the signs
/// differ or a side is infinite).
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Wraps <c>num2cell</c>, <c>mode</c>, <c>median</c>, <c>unique</c> and <c>imag</c> for MATLAB code;
    /// registered after the bracket verbs and before R2025b's refusals.
    /// </summary>
    internal static void RegisterSlicesAndModes(JgsEnvironment env)
    {
        WrapMatlabVerb(env, "num2cell", static (args, wanted, line, col) => MatlabNum2Cell(args, line, col) is { } c ? [c] : null);
        WrapMatlabVerb(env, "mode", static (args, wanted, line, col) => MatlabMode(args, wanted, line, col));
        WrapMatlabVerb(env, "median", static (args, wanted, line, col) => MatlabComplexMedian(args, line, col) is { } m ? [m] : null);
        WrapMatlabVerb(env, "unique", static (args, wanted, line, col) => MatlabUniqueComplexRows(args, wanted));

        // imag of a flagged array reads its plane, so complex(1, -0) answers -0 as R2025b's does (open item 349, ADR 0386).
        WrapMatlabVerb(env, "imag", static (args, wanted, line, col) =>
        {
            if (args.Count != 1 || args[0].Type != JgsType.Array || !args[0].IsPackedComplex)
            {
                return null;
            }

            JgsPackedComplex planes = args[0].AsPackedComplex;
            ReadOnlySpan<double> im = planes.Im.AsSpan(0, args[0].ArrayLength);
            // Only a flagged array (every imaginary part zero, complex(x, -0)): an array with a nonzero
            // imaginary part keeps the verb's road, whose zeros read the same in both storage lanes (the
            // boxed lane's elements do not keep a -0; MatlabPackedFftM96Tests caught the split at the gate).
            bool negativeZero = false;
            foreach (double v in im)
            {
                if (v != 0)
                {
                    return null;
                }

                negativeZero |= double.IsNegative(v);
            }

            if (!negativeZero)
            {
                return null;
            }

            double[] copy = im.ToArray();
            GC.KeepAlive(planes);
            return [MatlabArrayValue(MatlabCatKind.Numeric, args[0].NumericClass, SizeDims(args[0]), copy, null)];
        });
    }

    // --- unique(z, 'rows') of a complex matrix -----------------------------------------------------

    /// <summary>
    /// <c>unique(z, 'rows')</c> of a matrix with an imaginary part (open item 349, ADR 0386): rows in
    /// the complex order column by column (magnitude, then angle with -0 read as 0, NaN last), equal
    /// rows (a NaN equal to nothing) kept once; <c>'stable'</c> keeps them in order of first appearance,
    /// <c>'last'</c> indexes each by its last occurrence. Without <c>'rows'</c> each element is a row of one,
    /// the answer a row when the argument is a row vector (the verb's road refused a complex array).
    /// Null for any other form.
    /// </summary>
    private static JgsValue[]? MatlabUniqueComplexRows(IReadOnlyList<JgsValue> args, int wanted)
    {
        if (args.Count < 1 || args[0].Type is not (JgsType.Array or JgsType.Complex) || ReadPlain(args[0]) is not { HasImaginary: true } z
            || z.Kind != MatlabCatKind.Numeric || z.Class.IsInteger())
        {
            return null;
        }

        bool rows = false, stable = false, last = false;
        for (int i = 1; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                return null;
            }

            switch (TextOf(args[i]).ToLowerInvariant())
            {
                case "rows": rows = true; break;
                case "stable": stable = true; break;
                case "sorted" or "first": break;
                case "last": last = true; break;
                default: return null;
            }
        }

        // Without 'rows' every element is a row of one, and the answer lies as a row vector does
        // (unique of a complex array, which the verb refused as boxed elements, open item 349).
        bool rowVector = !rows && z.Dims.Length == 2 && z.Dims[0] == 1;
        if (rows && (z.Dims.Length > 2 || args[0].Type != JgsType.Array))
        {
            return null;
        }

        int height = rows ? z.Dims[0] : z.Re.Length, width = rows ? z.Dims[1] : 1;
        int Compare(int p, int q)
        {
            for (int c = 0; c < width; c++)
            {
                int i = p + (c * height), j = q + (c * height);
                double ai = z.Im![i] + 0.0, aj = z.Im[j] + 0.0;
                bool ni = double.IsNaN(z.Re[i]) || double.IsNaN(ai), nj = double.IsNaN(z.Re[j]) || double.IsNaN(aj);
                if (ni || nj)
                {
                    if (ni != nj)
                    {
                        return ni ? 1 : -1;
                    }

                    continue;
                }

                double mi = Complex.Abs(new Complex(z.Re[i], ai)), mj = Complex.Abs(new Complex(z.Re[j], aj));
                if (mi != mj)
                {
                    return mi < mj ? -1 : 1;
                }

                double ti = Math.Atan2(ai, z.Re[i] + 0.0), tj = Math.Atan2(aj, z.Re[j] + 0.0);
                if (ti != tj)
                {
                    return ti < tj ? -1 : 1;
                }
            }

            return 0;
        }

        bool Same(int p, int q)
        {
            for (int c = 0; c < width; c++)
            {
                int i = p + (c * height), j = q + (c * height);
                if (!(z.Re[i] == z.Re[j] && z.Im![i] == z.Im[j]))
                {
                    return false;
                }
            }

            return true;
        }

        int[] order = Enumerable.Range(0, height).ToArray();
        Array.Sort(order, (p, q) => Compare(p, q) is var c && c != 0 ? c : p.CompareTo(q));
        var groupOf = new int[height];
        var groups = new List<List<int>>();
        for (int k = 0; k < height; k++)
        {
            if (k == 0 || !Same(order[k - 1], order[k]))
            {
                groups.Add([]);
            }

            groups[^1].Add(order[k]);
        }

        if (stable)
        {
            groups.Sort((a, b) => a.Min().CompareTo(b.Min()));
        }

        for (int g = 0; g < groups.Count; g++)
        {
            foreach (int r in groups[g])
            {
                groupOf[r] = g;
            }
        }

        int n = groups.Count;
        var picks = groups.ConvertAll(g => last ? g.Max() : g.Min());
        var re = new double[n * width];
        var im = new double[n * width];
        for (int g = 0; g < n; g++)
        {
            for (int c = 0; c < width; c++)
            {
                re[g + (c * n)] = z.Re[picks[g] + (c * height)];
                im[g + (c * n)] = z.Im![picks[g] + (c * height)];
            }
        }

        JgsValue u = MatlabArrayValue(MatlabCatKind.Numeric, z.Class, rowVector ? [1, n] : [n, width], re, im);
        JgsValue first = MatlabArrayValue(MatlabCatKind.Numeric, JgsNumericClass.Double, [n, 1], picks.ConvertAll(static p => (double)(p + 1)).ToArray(), null);
        JgsValue map = MatlabArrayValue(MatlabCatKind.Numeric, JgsNumericClass.Double, [height, 1], Array.ConvertAll(groupOf, static g => (double)(g + 1)), null);
        return wanted <= 1 ? [u] : wanted == 2 ? [u, first] : [u, first, map];
    }

    /// <summary>Re-registers a builtin so MATLAB code asks <paramref name="answer"/> first; null hands the call on.</summary>
    private static void WrapMatlabVerb(JgsEnvironment env, string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> answer)
    {
        if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function || existing.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            JgsRunningDialect.ThreadIsMatlab && answer(args, 1, line, col) is { } mine ? mine[0] : inner.Call(args, line, col))
        {
            KeepsStringArguments = inner.KeepsStringArguments,
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (JgsRunningDialect.ThreadIsMatlab && answer(args, wanted, line, col) is { } mine)
                {
                    return mine;
                }

                return inner.MultiOutput is { } multi ? multi(args, wanted, line, col) : [inner.Call(args, line, col)];
            },
        }));
    }

    /// <summary>A plain array as a value, complex when an imaginary part is non-zero.</summary>
    private static JgsValue MatlabArrayValue(MatlabCatKind kind, JgsNumericClass numericClass, int[] dims, double[] re, double[]? im)
    {
        bool imaginary = im is not null && Array.Exists(im, static v => v != 0);
        var piece = new MatlabCatPiece { Kind = kind, Class = numericClass, Dims = dims, Re = re, Im = imaginary ? im : null, HasImaginary = imaginary };
        return MatlabCatValue(piece, new MatlabCatResult(kind, numericClass, imaginary, false));
    }

    private static MatlabCatPiece? ReadPlain(JgsValue v) =>
        TryMatlabCatSignature(v, out MatlabCatKind kind, out JgsNumericClass numericClass, out byte complexity, out _, out _)
            ? ReadMatlabCatPiece(v, kind, numericClass, complexity)
            : null;

    // --- num2cell --------------------------------------------------------------------------------

    private static JgsValue? MatlabNum2Cell(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue a = args[0];
        bool isCell = a.Type == JgsType.Cell;
        bool isStrings = a.IsStringArray && a.Type == JgsType.Array;
        bool isStructs = a.Type == JgsType.Struct && a.ClassName is null && args.Count == 2; // one argument keeps its road
        bool container = isCell || isStrings || isStructs;
        MatlabCatPiece? plain = container ? null : ReadPlain(a);
        if (plain is null && !container)
        {
            return null;
        }

        int[] size = container ? SizeDims(a) : plain!.Dims;
        int count = isCell ? a.AsCell.Length : isStrings ? a.ArrayLength : isStructs ? a.AsStructArray.Elements.Length : plain!.Re.Length;
        JgsValue Slice(int[] picks, int[] shape)
        {
            if (isCell)
            {
                JgsValue[] source = a.AsCell;
                JgsValue inner = JgsValue.Cell(Array.ConvertAll(picks, i => JgsValue.Share(source[i])));
                inner.ReshapeDims(shape);
                return inner;
            }

            if (isStrings)
            {
                JgsValue strings = JgsValue.StringArray(Array.ConvertAll(picks, i => a.ElementAt(i)));
                strings.ReshapeDims(shape);
                return strings;
            }

            if (isStructs)
            {
                Dictionary<string, JgsValue>[] source = a.AsStructArray.Elements;
                JgsValue structs = JgsValue.StructArray(Array.ConvertAll(picks, i => new Dictionary<string, JgsValue>(source[i], StringComparer.Ordinal)));
                structs.ReshapeDims(shape);
                return structs;
            }

            double[] re = Array.ConvertAll(picks, i => plain!.Re[i]);
            double[]? im = plain!.Im is { } planes ? Array.ConvertAll(picks, i => planes[i]) : null;
            return MatlabArrayValue(plain.Kind, plain.Class, TrimTrailingOnes(shape), re, im);
        }

        if (args.Count == 1)
        {
            var each = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                each[i] = Slice([i], [1, 1]);
            }

            JgsValue cells = JgsValue.Cell(each);
            cells.ReshapeDims(size);
            return cells;
        }

        int[] dims = Num2CellDims(args[1], line, col);
        int rank = Math.Max(size.Length, dims.Max());
        int[] siz = Extend(size, rank);
        var rest = Enumerable.Range(1, rank).Where(d => Array.IndexOf(dims, d) < 0).ToArray();
        int[] perm = [.. dims, .. rest];
        var bsize = new int[rank];
        int[] sorted = [.. dims.OrderBy(static d => d)];
        for (int i = 0; i < dims.Length; i++)
        {
            bsize[sorted[i] - 1] = siz[dims[i] - 1];
        }

        foreach (int d in rest)
        {
            bsize[d - 1] = 1;
        }

        int[] csize = (int[])siz.Clone();
        foreach (int d in dims)
        {
            csize[d - 1] = 1;
        }

        // The permuted array's elements as indices into the original, in its column-major order.
        int[] strides = new int[rank];
        for (int d = 0, s = 1; d < rank; d++)
        {
            strides[d] = s;
            s *= siz[d];
        }

        int[] permutedSize = Array.ConvertAll(perm, d => siz[d - 1]);
        var order = new int[count];
        var sub = new int[rank];
        for (int k = 0; k < count; k++)
        {
            int at = 0;
            for (int d = 0; d < rank; d++)
            {
                at += sub[d] * strides[perm[d] - 1];
            }

            order[k] = at;
            for (int d = 0; d < rank && ++sub[d] == permutedSize[d]; d++)
            {
                sub[d] = 0;
            }
        }

        int offset = bsize.Aggregate(1, static (p, v) => p * v);
        int cellCount = csize.Aggregate(1, static (p, v) => p * v);
        var slices = new JgsValue[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            slices[i] = Slice(order[(i * offset)..((i + 1) * offset)], bsize);
        }

        JgsValue result = JgsValue.Cell(slices);
        result.ReshapeDims(csize);
        return result;
    }

    /// <summary>num2cell's dimension list, refused as R2025b's num2cell.m refuses it.</summary>
    private static int[] Num2CellDims(JgsValue dims, int line, int col)
    {
        if (dims.Type == JgsType.String || dims.IsCharMatrix || dims.IsStringArray)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:permute:badIndexType", "ORDER must be a numeric array.");
        }

        double[] values = dims.Type is JgsType.Number or JgsType.Bool ? [dims.AsNumber] : FlattenColumnMajor("num2cell", dims, line, col);
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:permute:tooFewIndices", "ORDER must have at least N elements for an N-D array.");
        }

        foreach (double v in values)
        {
            if (!(v >= 1) || v != Math.Floor(v) || v > int.MaxValue)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:badsubscript", "Array indices must be positive integers or logical values.");
            }
        }

        int[] result = Array.ConvertAll(values, static v => (int)v);
        if (result.Distinct().Count() != result.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:permute:repeatedIndex", "ORDER cannot contain repeated permutation indices.");
        }

        return result;
    }

    // --- mode ------------------------------------------------------------------------------------

    /// <summary>R2025b's sort order: magnitude, then angle, for a complex array; NaN last.</summary>
    private static int CompareMatlabSort(MatlabCatPiece x, int i, int j)
    {
        bool complex = x.Im is not null;
        double ai = complex ? x.Im![i] : 0, aj = complex ? x.Im![j] : 0;
        bool ni = double.IsNaN(x.Re[i]) || double.IsNaN(ai);
        bool nj = double.IsNaN(x.Re[j]) || double.IsNaN(aj);
        if (ni || nj)
        {
            return ni == nj ? 0 : ni ? 1 : -1;
        }

        if (!complex)
        {
            return x.Re[i].CompareTo(x.Re[j]) is var c && x.Re[i] == x.Re[j] ? 0 : c;
        }

        double mi = Complex.Abs(new Complex(x.Re[i], ai)), mj = Complex.Abs(new Complex(x.Re[j], aj));
        if (mi != mj)
        {
            return mi < mj ? -1 : 1;
        }

        double ti = Math.Atan2(ai, x.Re[i] + 0.0), tj = Math.Atan2(aj, x.Re[j] + 0.0);
        return ti == tj ? 0 : ti < tj ? -1 : 1;
    }

    private static bool MatlabElementsEqual(MatlabCatPiece x, int i, int j) =>
        x.Re[i] == x.Re[j] && (x.Im is null || x.Im[i] == x.Im[j]);

    /// <summary>The groups of linear indices a reduction over <paramref name="reduced"/> (1-based, ascending) reads, one per output element.</summary>
    private static int[][] ReductionGroups(int[] size, int[] reduced, out int[] outSize)
    {
        int rank = size.Length;
        outSize = (int[])size.Clone();
        foreach (int d in reduced)
        {
            outSize[d - 1] = 1;
        }

        var strides = new int[rank];
        for (int d = 0, s = 1; d < rank; d++)
        {
            strides[d] = s;
            s *= size[d];
        }

        int outer = outSize.Aggregate(1, static (p, v) => p * v);
        int inner = reduced.Aggregate(1, (p, d) => p * size[d - 1]);
        var groups = new int[outer][];
        var subOut = new int[rank];
        for (int o = 0; o < outer; o++)
        {
            int baseAt = 0;
            for (int d = 0; d < rank; d++)
            {
                baseAt += subOut[d] * strides[d];
            }

            var group = new int[inner];
            var subIn = new int[reduced.Length];
            for (int r = 0; r < inner; r++)
            {
                int at = baseAt;
                for (int k = 0; k < reduced.Length; k++)
                {
                    at += subIn[k] * strides[reduced[k] - 1];
                }

                group[r] = at;
                for (int k = 0; k < reduced.Length && ++subIn[k] == size[reduced[k] - 1]; k++)
                {
                    subIn[k] = 0;
                }
            }

            groups[o] = group;
            for (int d = 0; d < rank && ++subOut[d] == outSize[d]; d++)
            {
                subOut[d] = 0;
            }
        }

        return groups;
    }

    private static JgsValue[]? MatlabMode(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count is < 1 or > 2 || ReadPlain(args[0]) is not { } x)
        {
            return null;
        }

        if (wanted > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        bool integerLike = x.Kind == MatlabCatKind.Logical || x.Class.IsInteger();
        JgsValue Empties(int[] shape) => integerLike
            ? MatlabArrayValue(x.Kind, x.Class, shape, new double[shape.Aggregate(1, static (p, v) => p * v)], null)
            : MatlabArrayValue(x.Kind, x.Class, shape, Enumerable.Repeat(double.NaN, shape.Aggregate(1, static (p, v) => p * v)).ToArray(), null);
        JgsValue EmptyOfClass(int rows, int cols) => MatlabArrayValue(x.Kind, x.Class, [rows, cols], [], null);

        int[] dims;
        int[] size = x.Dims;
        if (args.Count < 2)
        {
            if (size is [0, 0])
            {
                JgsValue cell0 = JgsValue.Cell([EmptyOfClass(0, 1)]);
                return [Empties([1, 1]), JgsValue.Number(0), cell0];
            }

            int first = Array.FindIndex(size, static d => d != 1);
            dims = [first < 0 ? 1 : first + 1];
        }
        else
        {
            JgsValue d = args[1];
            if (IsTextScalar(d))
            {
                string word = TextOf(d);
                if (word.Length == 0 || !"all".StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:mode:BadDim", "Dimension argument must be a positive integer scalar, a vector of unique positive integers, or 'all'.");
                }

                x = new MatlabCatPiece { Kind = x.Kind, Class = x.Class, Dims = [x.Re.Length, 1], Re = x.Re, Im = x.Im, HasImaginary = x.HasImaginary };
                size = x.Dims;
                dims = [1];
            }
            else
            {
                double[] values = d.Type is JgsType.Number or JgsType.Bool ? [d.AsNumber]
                    : d.Type == JgsType.Array && !HasComplexElements(d) ? FlattenColumnMajor("mode", d, line, col)
                    : throw new JgsRuntimeException(line, col, "MATLAB:mode:BadDim", "Dimension argument must be a positive integer scalar, a vector of unique positive integers, or 'all'.");
                if (values.Length == 0 || Array.Exists(values, static v => !(v >= 1) || v != Math.Floor(v) || double.IsInfinity(v)))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:mode:BadDim", "Dimension argument must be a positive integer scalar, a vector of unique positive integers, or 'all'.");
                }

                if (values.Distinct().Count() != values.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:mode:vecDimsMustBeUniquePositiveIntegers", "Dimension argument must be a positive integer scalar or a vector of unique positive integers.");
                }

                dims = Array.ConvertAll(values, v => (int)Math.Min(v, size.Length + 1));
            }
        }

        int rank = Math.Max(size.Length, dims.Max());
        int[] sizex = Extend(size, rank);
        int[] reduced = [.. dims.Distinct().OrderBy(static v => v)];
        int[][] groups = ReductionGroups(sizex, reduced, out int[] sizem);
        int outCount = groups.Length;
        int[] shapeM = TrimTrailingOnes(sizem);

        if (x.Re.Length == 0)
        {
            var cells = new JgsValue[outCount];
            for (int i = 0; i < outCount; i++)
            {
                cells[i] = EmptyOfClass(1, 0);
            }

            JgsValue cellE = JgsValue.Cell(cells);
            cellE.ReshapeDims(shapeM);
            return [Empties(shapeM), MatlabArrayValue(MatlabCatKind.Numeric, JgsNumericClass.Double, shapeM, new double[outCount], null), cellE];
        }

        var mRe = new double[outCount];
        double[]? mIm = x.Im is null ? null : new double[outCount];
        var freqs = new double[outCount];
        var modes = new JgsValue[outCount];
        for (int o = 0; o < outCount; o++)
        {
            int[] group = groups[o];
            int[] sortedAt = [.. group];
            Array.Sort(sortedAt, (i, j) => CompareMatlabSort(x, i, j) is var c && c != 0 ? c : i.CompareTo(j));
            int bestStart = 0, bestCount = 0;
            var runs = new List<(int Start, int Count)>();
            for (int s = 0; s < sortedAt.Length;)
            {
                int e = s + 1;
                while (e < sortedAt.Length && MatlabElementsEqual(x, sortedAt[e - 1], sortedAt[e]))
                {
                    e++;
                }

                runs.Add((s, e - s));
                if (e - s > bestCount)
                {
                    bestCount = e - s;
                    bestStart = s;
                }

                s = e;
            }

            mRe[o] = x.Re[sortedAt[bestStart]];
            if (mIm is not null)
            {
                mIm[o] = x.Im![sortedAt[bestStart]];
            }

            freqs[o] = bestCount;
            if (wanted >= 3)
            {
                int[] picks = [.. runs.Where(r => r.Count == bestCount).Select(r => sortedAt[r.Start])];
                double[] re = Array.ConvertAll(picks, i => x.Re[i]);
                double[]? im = x.Im is null ? null : Array.ConvertAll(picks, i => x.Im[i]);
                modes[o] = MatlabArrayValue(x.Kind, x.Class, [picks.Length, 1], re, im);
            }
        }

        JgsValue m = MatlabArrayValue(x.Kind, x.Class, shapeM, mRe, mIm);
        JgsValue f = MatlabArrayValue(MatlabCatKind.Numeric, JgsNumericClass.Double, shapeM, freqs, null);
        if (wanted < 3)
        {
            return [m, f];
        }

        JgsValue c = JgsValue.Cell(modes);
        c.ReshapeDims(shapeM);
        return [m, f, c];
    }

    // --- median of a complex array ----------------------------------------------------------------

    private static JgsValue? MatlabComplexMedian(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 3 || !HoldsComplexStorage(args[0]) || ReadPlain(args[0]) is not { } x
            || x.Kind != MatlabCatKind.Numeric || x.Class.IsInteger())
        {
            return null;
        }

        bool omitNan = false, all = false;
        int[]? dims = null;
        for (int i = 1; i < args.Count; i++)
        {
            JgsValue a = args[i];
            if (IsTextScalar(a))
            {
                string word = TextOf(a).ToLowerInvariant();
                if (word.Length > 0 && "all".StartsWith(word, StringComparison.Ordinal) && i == 1)
                {
                    all = true;
                }
                else if (word.Length > 0 && ("omitnan".StartsWith(word, StringComparison.Ordinal) || "omitmissing".StartsWith(word, StringComparison.Ordinal)))
                {
                    omitNan = true;
                }
                else if (word.Length > 0 && ("includenan".StartsWith(word, StringComparison.Ordinal) || "includemissing".StartsWith(word, StringComparison.Ordinal)))
                {
                    omitNan = false;
                }
                else
                {
                    return null; // weights and anything else keep the old road
                }
            }
            else if (i == 1)
            {
                double[] values = a.Type is JgsType.Number or JgsType.Bool ? [a.AsNumber] : FlattenColumnMajor("median", a, line, col);
                if (values.Length == 0 || Array.Exists(values, static v => !(v >= 1) || v != Math.Floor(v) || double.IsInfinity(v)))
                {
                    return null;
                }

                dims = Array.ConvertAll(values, static v => (int)Math.Min(v, int.MaxValue));
            }
            else
            {
                return null;
            }
        }

        int[] size = x.Dims;
        if (all)
        {
            size = [x.Re.Length, 1];
            x = new MatlabCatPiece { Kind = x.Kind, Class = x.Class, Dims = size, Re = x.Re, Im = x.Im, HasImaginary = x.HasImaginary };
            dims = [1];
        }

        if (x.Re.Length == 0)
        {
            if (dims is null && size is [0, 0])
            {
                return MatlabFlaggedNaN([1, 1], x.Class);
            }

            int[] reducedE = dims ?? [Array.FindIndex(size, static d => d != 1) + 1 is var f && f > 0 ? f : 1];
            int[] sz = Extend(size, Math.Max(size.Length, reducedE.Max()));
            foreach (int d in reducedE)
            {
                sz[d - 1] = 1;
            }

            return MatlabFlaggedNaN(TrimTrailingOnes(sz), x.Class);
        }

        if (dims is not null && dims.All(d => d > size.Length))
        {
            return args[0];
        }

        int[] reduced = dims is null
            ? [Array.FindIndex(size, static d => d != 1) is var first && first >= 0 ? first + 1 : 1]
            : [.. dims.Select(d => Math.Min(d, size.Length + 1)).Distinct().OrderBy(static d => d)];
        int rank = Math.Max(size.Length, reduced.Max());
        int[][] groups = ReductionGroups(Extend(size, rank), reduced, out int[] sizey);
        var re = new double[groups.Length];
        var im = new double[groups.Length];
        for (int o = 0; o < groups.Length; o++)
        {
            int[] sorted = [.. groups[o]];
            Array.Sort(sorted, (i, j) => CompareMatlabSort(x, i, j) is var c && c != 0 ? c : i.CompareTo(j));
            int n = sorted.Length;
            bool lastNaN = double.IsNaN(x.Re[sorted[n - 1]]) || double.IsNaN(x.Im![sorted[n - 1]]);
            if (lastNaN && omitNan)
            {
                n = Array.FindLastIndex(sorted, i => !(double.IsNaN(x.Re[i]) || double.IsNaN(x.Im![i]))) + 1;
            }

            if (n == 0 || (lastNaN && !omitNan))
            {
                re[o] = double.NaN;
                im[o] = 0;
                continue;
            }

            int half = n / 2;
            Complex y = new(x.Re[sorted[half]], x.Im![sorted[half]]);
            if (2 * half == n)
            {
                y = MedianMeanOf(new Complex(x.Re[sorted[half - 1]], x.Im[sorted[half - 1]]), y);
            }

            re[o] = y.Real;
            im[o] = y.Imaginary;
        }

        return MatlabArrayValue(MatlabCatKind.Numeric, x.Class, TrimTrailingOnes(sizey), re, im);
    }

    /// <summary>median.m's <c>meanof</c> for two complex numbers.</summary>
    private static Complex MedianMeanOf(Complex a, Complex b)
    {
        static Complex Sign(Complex z) => z == Complex.Zero ? Complex.Zero : z / Complex.Abs(z);
        bool infinite = double.IsInfinity(a.Real) || double.IsInfinity(a.Imaginary) || double.IsInfinity(b.Real) || double.IsInfinity(b.Imaginary);
        Complex sa = Sign(a), sb = Sign(b);
        if (sa != sb || infinite)
        {
            return new Complex((a.Real + b.Real) / 2, (a.Imaginary + b.Imaginary) / 2);
        }

        return new Complex(a.Real + ((b.Real - a.Real) / 2), a.Imaginary + ((b.Imaginary - a.Imaginary) / 2));
    }

    /// <summary>NaN in the given shape, complex with its flag (median.m's <c>nan(sz, "like", x)</c> of a complex x).</summary>
    private static JgsValue MatlabFlaggedNaN(int[] dims, JgsNumericClass numericClass)
    {
        int count = dims.Aggregate(1, static (p, v) => p * v);
        var values = Enumerable.Repeat(new Complex(double.NaN, 0), count).ToArray();
        JgsValue flagged = ComplexStorage(values, dims, preserveComplex: true);
        if (numericClass != JgsNumericClass.Double)
        {
            flagged.SetNumericClass(numericClass);
        }

        return flagged;
    }

    /// <summary>Whether a value has complex storage: an imaginary part, or the zero-imaginary flag.</summary>
    private static bool HoldsComplexStorage(JgsValue v) =>
        v.Type == JgsType.Complex || (v.Type == JgsType.Array && (v.IsPackedComplex || (!v.IsPacked && HasComplexElements(v))));
}
