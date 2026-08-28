using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Matrix generation and shape builtins (<c>eye</c>, <c>diag</c>, <c>magic</c>, <c>reshape</c>,
/// <c>cat</c>, <c>flip</c>, …) plus the MATLAB dialect's column-wise reduction semantics, where
/// <c>sum(A)</c> over a matrix reduces each column. Registered for both dialects; only the
/// dialect-conditional pieces (square <c>zeros(n)</c>, column-wise reductions) check which one runs.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the generation/shape builtins into <paramref name="env"/>.</summary>
    private static void RegisterShapeBuiltins(JgsEnvironment env, Random random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // --- Matrix generation ------------------------------------------------------------------
        Define("eye", (args, line, col) =>
        {
            ArityRange("eye", args, 0, 2, line, col);
            int[] dims = SquareDims("eye", args, line, col);
            if (dims.Length != 2)
            {
                throw new JgsRuntimeException(line, col, "eye builds 2-D identity matrices only.");
            }

            return BuildMatrix(dims[0], dims[1], static (r, c) => r == c ? 1.0 : 0.0);
        });

        Define("diag", (args, line, col) =>
        {
            ArityRange("diag", args, 1, 2, line, col);
            int offset = args.Count == 2 ? Count("diag", args, 1, line, col) : 0;

            // Neither reading of an empty has anything to answer, and the matrix reading below walks
            // off the end of a matrix with no rows in it (M96b). A vector builds a square of its own
            // length — 0-by-0 — and so does the shapeless empty; extracting a diagonal from a real
            // but empty matrix answers the column it would have filled, which is 0-by-1.
            if (JgsEmpty.IsEmptyArray(args[0]))
            {
                int diagRows = JgsMatrix.RowCount(args[0]);
                int diagCols = JgsMatrix.ColCount(args[0]);
                bool vector = diagRows == 1 || diagCols == 1 || (diagRows == 0 && diagCols == 0);
                return JgsEmpty.Shaped(0, vector ? 0 : 1);
            }

            if (IsMatrixValue(args[0]))
            {
                // Matrix in: extract the k-th diagonal as a vector.
                double[][] rows = RowsOfMatrix("diag", args[0], line, col);
                int height = rows.Length;
                int width = rows[0].Length;
                var extracted = new List<double>();
                for (int r = 0; r < height; r++)
                {
                    int c = r + offset;
                    if (c >= 0 && c < width)
                    {
                        extracted.Add(rows[r][c]);
                    }
                }

                return Numbers(extracted.ToArray());
            }

            // Vector in: build a matrix with the vector on the k-th diagonal.
            double[] values = ToDoubles("diag", args[0], line, col);
            int n = values.Length + System.Math.Abs(offset);
            return BuildMatrix(n, n, (r, c) =>
                c - r == offset && (offset >= 0 ? r : c) < values.Length
                    ? values[offset >= 0 ? r : c]
                    : 0.0);
        });

        Define("magic", (args, line, col) =>
        {
            Arity("magic", args, 1, line, col);
            int n = Count("magic", args, 0, line, col);
            if (n < 1)
            {
                throw new JgsRuntimeException(line, col, "magic needs a positive size.");
            }

            double[][] square = MagicSquare(n);
            return n == 1 ? JgsValue.Number(1) : MatrixFromRows(square);
        });

        Define("logspace", (args, line, col) =>
        {
            ArityRange("logspace", args, 2, 3, line, col);
            double start = Num("logspace", args, 0, line, col);
            double stop = Num("logspace", args, 1, line, col);
            int count = args.Count == 3 ? Count("logspace", args, 2, line, col) : 50;
            if (count < 1)
            {
                throw new JgsRuntimeException(line, col, "logspace needs at least one point.");
            }

            // MATLAB's special case: logspace(a, pi) ends exactly at pi, for digital signal work.
            if (stop == System.Math.PI)
            {
                stop = System.Math.Log10(System.Math.PI);
            }

            var points = new double[count];
            double step = count == 1 ? 0 : (stop - start) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                points[i] = System.Math.Pow(10, start + (step * i));
            }

            if (count > 1)
            {
                points[^1] = System.Math.Pow(10, stop); // land exactly on the endpoint
            }

            return Numbers(points);
        });

        // --- Inspection and shape ---------------------------------------------------------------
        Define("ndims", (args, line, col) =>
        {
            Arity("ndims", args, 1, line, col);

            // Everything in MATLAB is at least 2-D; N-D arrays and multi-channel images say more.
            return JgsValue.Number(args[0].Type switch
            {
                JgsType.Array => args[0].DimCount,
                JgsType.Image when args[0].AsImage.Channels > 1 => 3,
                _ => 2,
            });
        });

        Define("reshape", (args, line, col) => Reshape(args, line, col));

        Define("cat", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "cat expects a dimension and at least one value.");
            }

            int dim = Count("cat", args, 0, line, col);
            JgsValue[] parts = args.Skip(1).ToArray();
            return dim switch
            {
                1 => ConcatVertical("cat", parts, line, col),
                2 => ConcatHorizontal("cat", parts, line, col),
                _ => ConcatAlongDimension("cat", dim, parts, line, col),
            };
        });

        Define("horzcat", (args, line, col) => ConcatHorizontal("horzcat", args, line, col));
        Define("vertcat", (args, line, col) => ConcatVertical("vertcat", args, line, col));

        Define("flip", (args, line, col) =>
        {
            ArityRange("flip", args, 1, 2, line, col);
            int? dim = args.Count == 2 ? Count("flip", args, 1, line, col) : null;

            // Reversing nothing leaves it as it was, shape and all: flip(zeros(0, 3)) is 0-by-3,
            // where the paths below would have rebuilt it as a bare 1-by-0 row (M96b).
            if (JgsEmpty.IsEmptyArray(args[0]))
            {
                return args[0];
            }

            if (IsMatrixValue(args[0]))
            {
                // The default dimension is the first non-singleton one: rows, for a matrix.
                return (dim ?? 1) == 1
                    ? FlipRows("flip", args[0], line, col)
                    : FlipColumns("flip", args[0], line, col);
            }

            // A vector is a row here, so its non-singleton dimension is 2; flip(v, 1) is a no-op.
            return dim == 1 ? args[0] : ReversedVector("flip", args[0], line, col);
        });

        Define("fliplr", (args, line, col) =>
        {
            Arity("fliplr", args, 1, line, col);
            if (JgsEmpty.IsEmptyArray(args[0]))
            {
                return args[0]; // see flip: an empty reversed is the same empty
            }

            return IsMatrixValue(args[0])
                ? FlipColumns("fliplr", args[0], line, col)
                : ReversedVector("fliplr", args[0], line, col);
        });

        Define("flipud", (args, line, col) =>
        {
            Arity("flipud", args, 1, line, col);
            if (JgsEmpty.IsEmptyArray(args[0]))
            {
                return args[0]; // see flip: an empty reversed is the same empty
            }

            // A vector is a row, and flipping a single row upside down changes nothing.
            return IsMatrixValue(args[0]) ? FlipRows("flipud", args[0], line, col) : args[0];
        });

        Define("squeeze", (args, line, col) =>
        {
            Arity("squeeze", args, 1, line, col);
            if (args[0].Type != JgsType.Array || !args[0].IsNd)
            {
                return args[0]; // 2-D values keep their shape, exactly as in MATLAB
            }

            // Drop every singleton dimension; storage order is untouched, so this is a flat copy
            // with a new shape stamp.
            int[] dims = args[0].Dims;
            var kept = new List<int>(dims.Length);
            foreach (int dim in dims)
            {
                if (dim != 1)
                {
                    kept.Add(dim);
                }
            }

            while (kept.Count < 2)
            {
                kept.Add(1);
            }

            double[] flat = FlattenColumnMajor("squeeze", args[0], line, col);
            return JgsMatrix.FromColumnMajorDims(flat, kept);
        });

        Define("permute", (args, line, col) =>
        {
            Arity("permute", args, 2, line, col);
            double[] orderRaw = ToDoubles("permute", args[1], line, col);
            if (args[0].Type != JgsType.Array)
            {
                return args[0]; // a scalar is 1-by-1 whichever way its dimensions are ordered
            }

            int[] source = JgsMatrix.DimsOf(args[0]);
            if (orderRaw.Length < source.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"permute needs an order covering all {source.Length} dimensions.");
            }

            var order = new int[orderRaw.Length];
            var seen = new bool[orderRaw.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = (int)orderRaw[i] - 1;
                if (order[i] < 0 || order[i] >= order.Length || seen[order[i]])
                {
                    throw new JgsRuntimeException(line, col, "permute's order must use each dimension exactly once.");
                }

                seen[order[i]] = true;
            }

            // Pad the source with singletons up to the order's length, then gather column-major.
            var padded = new int[order.Length];
            for (int i = 0; i < padded.Length; i++)
            {
                padded[i] = i < source.Length ? source[i] : 1;
            }

            double[] flat = FlattenColumnMajor("permute", args[0], line, col);
            var strides = new long[padded.Length];
            long stride = 1;
            for (int i = 0; i < padded.Length; i++)
            {
                strides[i] = stride;
                stride *= padded[i];
            }

            var resultDims = new int[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                resultDims[i] = padded[order[i]];
            }

            var result = new double[flat.Length];
            var counter = new int[order.Length]; // odometer over the RESULT dims, column-major
            for (int i = 0; i < result.Length; i++)
            {
                long sourceIndex = 0;
                for (int d = 0; d < order.Length; d++)
                {
                    sourceIndex += counter[d] * strides[order[d]];
                }

                result[i] = flat[sourceIndex];
                for (int d = 0; d < order.Length; d++)
                {
                    if (++counter[d] < resultDims[d])
                    {
                        break;
                    }

                    counter[d] = 0;
                }
            }

            return JgsMatrix.FromColumnMajorDims(result, resultDims);
        });

        Define("transpose", (args, line, col) =>
        {
            Arity("transpose", args, 1, line, col);
            return TransposeValue("transpose", args[0], line, col);
        });

        Define("ctranspose", (args, line, col) =>
        {
            Arity("ctranspose", args, 1, line, col);
            return TransposeValue("ctranspose", args[0], line, col, conjugate: true);
        });

        // --- Reductions and search --------------------------------------------------------------
        Define("prod", (args, line, col) => Reduce("prod", args, line, col, static (a, b) => a * b, 1.0));

        Define("ismember", (args, line, col) =>
        {
            Arity("ismember", args, 2, line, col);
            return Ismember(args[0], args[1], line, col);
        });

        Define("dot", (args, line, col) =>
        {
            Arity("dot", args, 2, line, col);

            // Two packed real vectors are the overwhelming case, and boxing them into Complex[] to
            // multiply by a zero imaginary part is most of what it used to cost (M92). Conjugating a
            // real number is a no-op and (a,0)·(b,0) is (a·b, 0), so the running total below is the
            // real part of the complex one, term for term — the same left fold, the same rounding.
            if (args[0].IsPacked && args[1].IsPacked)
            {
                NumericBuffer left = args[0].AsBuffer;
                NumericBuffer right = args[1].AsBuffer;
                if (left.Length != right.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"dot needs vectors of equal length, but got {left.Length} and {right.Length}.");
                }

                double total = 0;
                ReadOnlySpan<double> xs = left.AsSpan();
                ReadOnlySpan<double> ys = right.AsSpan();
                for (int i = 0; i < xs.Length; i++)
                {
                    total += xs[i] * ys[i];
                }

                GC.KeepAlive(left);
                GC.KeepAlive(right);
                return JgsValue.Number(total);
            }

            Complex[] a = ComplexArray("dot", args, 0, line, col);
            Complex[] b = ComplexArray("dot", args, 1, line, col);
            if (a.Length != b.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"dot needs vectors of equal length, but got {a.Length} and {b.Length}.");
            }

            // MATLAB conjugates the first operand, so dot of a complex vector with itself is real.
            Complex sum = Complex.Zero;
            for (int i = 0; i < a.Length; i++)
            {
                sum += Complex.Conjugate(a[i]) * b[i];
            }

            return sum.Imaginary == 0 ? JgsValue.Number(sum.Real) : JgsValue.ComplexNum(sum);
        });

        // --- MATLAB shapes for the random and filled constructors -------------------------------
        if (dialect.IsMatlab)
        {
            RegisterMatlabConstructorShapes(env, random);
        }
    }

    /// <summary>
    /// In MATLAB, <c>zeros(n)</c>/<c>ones(n)</c>/<c>rand(n)</c>/<c>randn(n)</c> build n-by-n matrices,
    /// a single size never a length. JGS keeps its documented flat forms, so these re-registrations
    /// only happen for the MATLAB dialect.
    /// </summary>
    private static void RegisterMatlabConstructorShapes(JgsEnvironment env, Random random)
    {
        void Redefine(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // rand and randn are questions as well as constructors: MATLAB's `x = rand` is one number,
        // and `@(t) t + randn` is how a proposal distribution is written. Without the bare call the
        // name evaluates to the function itself and the addition fails, which is how stess_25 found
        // this. zeros and ones stay as they are — nobody writes a bare `zeros`.
        void RedefineAutoCalling(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        Redefine("zeros", (args, line, col) => NdConstructorValue("zeros", args, line, col, static () => 0.0));
        Redefine("ones", (args, line, col) => NdConstructorValue("ones", args, line, col, static () => 1.0));
        RedefineAutoCalling("rand", (args, line, col) =>
            NdConstructorValue("rand", args, line, col, random.NextDouble));

        double NextGaussian()
        {
            // Box-Muller: two uniforms in, one standard normal out.
            double u1 = 1.0 - random.NextDouble(); // in (0, 1], so Log is finite
            double u2 = random.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2);
        }

        RedefineAutoCalling("randn", (args, line, col) =>
            NdConstructorValue("randn", args, line, col, NextGaussian));
    }

    /// <summary>
    /// The MATLAB constructor shapes: () scalar, (n) n-by-n, (r, c, …) or a size vector as written —
    /// any number of dimensions, empty ones included (<c>zeros(5, 0, 2)</c>).
    /// </summary>
    private static JgsValue NdConstructorValue(
        string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double> next)
    {
        (args, JgsNumericClass? asked) = ClassSuffix(name, args, line, col);
        JgsValue built = NdConstructorOfDoubles(name, args, line, col, next);
        return asked is { } numericClass
            ? ToNumericClass(name, numericClass, built, line, col)
            : built;
    }

    /// <summary>
    /// The trailing class of a constructor call: <c>zeros(2, 'uint8')</c> and
    /// <c>zeros(2, 'like', x)</c>. Both are read off the end and removed, so the shape logic below
    /// never learns that a class exists — the class is applied to whatever it built.
    /// </summary>
    /// <remarks>
    /// <c>'like'</c> takes the class from a prototype value rather than from a word, which is how a
    /// function writes "same kind of array as the one I was handed" without knowing what kind that is.
    /// </remarks>
    private static (IReadOnlyList<JgsValue> Shape, JgsNumericClass? Class) ClassSuffix(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count >= 2 && IsTextScalar(args[^2]) && string.Equals(TextOf(args[^2]), "like", StringComparison.OrdinalIgnoreCase))
        {
            JgsNumericClass? prototype = JgsNumericClasses.Parse(ClassOf(args[^1], JgsDialect.Matlab));
            if (prototype is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'like' copies a numeric class, and a {args[^1].TypeName} has none.");
            }

            return (args.Take(args.Count - 2).ToList(), prototype);
        }

        if (args.Count >= 1 && IsTextScalar(args[^1]))
        {
            string word = TextOf(args[^1]);
            if (JgsNumericClasses.Parse(word) is { } named)
            {
                return (args.Take(args.Count - 1).ToList(), named);
            }

            throw new JgsRuntimeException(line, col, $"{name}: JGraph has no '{word}' class.");
        }

        return (args, null);
    }

    private static JgsValue NdConstructorOfDoubles(
        string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double> next)
    {
        int[] dims = SquareDims(name, args, line, col);
        bool scalar = true;
        long count = 1;
        foreach (int dim in dims)
        {
            scalar &= dim == 1;
            count *= dim;
        }

        if (scalar)
        {
            return JgsValue.Number(next());
        }

        if (count > int.MaxValue)
        {
            throw new JgsRuntimeException(line, col, $"{name}: a {string.Join("x", dims)} array is too large.");
        }

        var flat = new double[count];
        for (int i = 0; i < flat.Length; i++)
        {
            flat[i] = next();
        }

        return JgsMatrix.FromColumnMajorDims(flat, dims);
    }

    /// <summary>
    /// The dimensions a square-defaulting constructor was asked for: () is 1-by-1, (n) is n-by-n,
    /// (d1, d2, …) and size vectors are as written ([n] alone is n-by-n). Negative sizes clamp to
    /// zero, as MATLAB's do.
    /// </summary>
    private static int[] SquareDims(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        static int Dim(double raw) => raw > 0 ? (int)raw : 0;

        switch (args.Count)
        {
            case 0:
                return [1, 1];
            case 1 when args[0].Type == JgsType.Array:
                double[] size = ToDoubles(name, args[0], line, col);
                return size.Length switch
                {
                    0 => throw new JgsRuntimeException(line, col, $"{name}: a size vector needs at least one dimension."),
                    1 => [Dim(size[0]), Dim(size[0])],
                    _ => Array.ConvertAll(size, Dim),
                };
            case 1:
                int n = Dim(Count(name, args, 0, line, col));
                return [n, n];
            default:
                var dims = new int[args.Count];
                for (int i = 0; i < dims.Length; i++)
                {
                    dims[i] = Dim(Count(name, args, i, line, col));
                }

                return dims;
        }
    }

    /// <summary>
    /// reshape(A, d1, d2, …), reshape(A, [d1 d2 …]) — any number of dimensions, with one dimension
    /// allowed to be [] ("whatever makes the count work"). MATLAB reads and fills column by column,
    /// which is exactly the storage order, so a reshape is a shape stamp on a flat copy.
    /// </summary>
    private static JgsValue Reshape(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("reshape", args, 2, int.MaxValue, line, col);
        double[] flat = FlattenColumnMajor("reshape", args[0], line, col);

        // Collect the requested size; -1 marks the (at most one) [] wildcard.
        var dims = new List<int>();
        int wild = -1;
        if (args.Count == 2 && args[1].Type == JgsType.Array && args[1].ArrayLength > 0)
        {
            double[] size = ToDoubles("reshape", args[1], line, col);
            if (size.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "reshape expects a size with at least two dimensions.");
            }

            foreach (double dim in size)
            {
                dims.Add((int)dim);
            }
        }
        else
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i].Type == JgsType.Array && args[i].ArrayLength == 0)
                {
                    if (wild >= 0)
                    {
                        throw new JgsRuntimeException(line, col, "reshape can infer at most one dimension from [].");
                    }

                    wild = dims.Count;
                    dims.Add(1);
                }
                else
                {
                    dims.Add(Count("reshape", args, i, line, col));
                }
            }

            if (dims.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "reshape expects a size with at least two dimensions.");
            }
        }

        if (wild >= 0)
        {
            long known = 1;
            for (int i = 0; i < dims.Count; i++)
            {
                if (i != wild)
                {
                    known *= dims[i];
                }
            }

            if (known == 0 || flat.Length % known != 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"reshape cannot infer the [] dimension: {flat.Length} element(s) do not divide by {known}.");
            }

            dims[wild] = (int)(flat.Length / known);
        }

        long product = 1;
        foreach (int dim in dims)
        {
            product *= dim;
        }

        if (product != flat.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"reshape must keep the element count: {flat.Length} element(s) do not fill {string.Join("x", dims)}.");
        }

        return product == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajorDims(flat, dims);
    }

    /// <summary>Elementwise set membership: each element of <paramref name="subject"/> against the set.</summary>
    private static JgsValue Ismember(JgsValue subject, JgsValue set, int line, int col)
    {
        bool Contains(JgsValue candidate)
        {
            if (set.Type is JgsType.Array or JgsType.Cell)
            {
                IEnumerable<JgsValue> members = set.Type == JgsType.Cell
                    ? set.AsCell
                    : EnumerateElements(set);
                foreach (JgsValue member in members)
                {
                    if (SameScalar(candidate, member))
                    {
                        return true;
                    }
                }

                return false;
            }

            return SameScalar(candidate, set);
        }

        if (subject.Type == JgsType.Array && !IsMatrixValue(subject))
        {
            var mask = new JgsValue[subject.ArrayLength];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = JgsValue.Bool(Contains(subject.ElementAt(i)));
            }

            return JgsValue.Array(mask);
        }

        if (IsMatrixValue(subject))
        {
            return JgsMatrix.BuildValues(
                JgsMatrix.RowCount(subject),
                JgsMatrix.ColCount(subject),
                (r, c) => JgsValue.Bool(Contains(JgsMatrix.At(subject, r, c))));
        }

        return JgsValue.Bool(Contains(subject));
    }

    private static bool SameScalar(JgsValue a, JgsValue b)
    {
        if (a.Type == JgsType.String || b.Type == JgsType.String)
        {
            return a.Type == JgsType.String && b.Type == JgsType.String
                && string.Equals(a.AsString, b.AsString, StringComparison.Ordinal);
        }

        return a.Type is JgsType.Number or JgsType.Bool && b.Type is JgsType.Number or JgsType.Bool
            && a.AsNumber.Equals(b.AsNumber);
    }

    // --- Column-wise reduction semantics (MATLAB dialect) --------------------------------------

    /// <summary>
    /// Re-registers the reductions with MATLAB's matrix semantics: <c>sum(A)</c> reduces each column
    /// to a row vector, <c>sum(A, 2)</c> each row to a column, <c>sum(A, 'all')</c> everything to one
    /// value. Vectors behave exactly as before, so the JGS dialect (which never calls this) and every
    /// existing vector script are untouched.
    /// </summary>
    private static void RegisterMatlabReductions(JgsEnvironment env, JgsDialect dialect)
    {
        // One row per name, and every difference between one reduction and another lives here rather
        // than in the wrapper's body. Before M52 the only such difference was a bool for diff, and
        // std(x, 1) paid for it: the weight landed in the slot the wrapper reads as the dimension, so
        // asking for the population standard deviation silently reduced along dimension 1 instead.
        WrapColumnwise(env, "sum",
            new(Words: TailWords.Nan | TailWords.Outtype, Identity: 0, Vecdim: true));
        WrapColumnwise(env, "prod",
            new(Words: TailWords.Nan | TailWords.Outtype, Identity: 1, Vecdim: true));
        WrapColumnwise(env, "mean", new(Words: TailWords.Nan | TailWords.Outtype));
        WrapColumnwise(env, "median", new(Words: TailWords.Nan));
        WrapColumnwise(env, "mode", new());
        WrapColumnwise(env, "rms", new(Words: TailWords.Nan));

        // std(X, w, dim) and var(X, w, dim): the weight sits where every other reduction keeps the
        // dimension, so the dimension moves along one.
        foreach (string spread in new[] { "std", "variance", "var" })
        {
            WrapColumnwise(env, spread, new(LeadingArgs: 1, Words: TailWords.Nan));
        }

        // any and all take no 'omitnan': MATLAB counts NaN as nonzero, so there is nothing to omit.
        WrapColumnwise(env, "any", new(Vecdim: true));
        WrapColumnwise(env, "all", new(Vecdim: true));

        WrapColumnwise(env, "cumsum", new(KeepShape: true, Words: TailWords.Nan | TailWords.Reverse, Identity: 0));
        WrapColumnwise(env, "cumprod", new(KeepShape: true, Words: TailWords.Nan | TailWords.Reverse, Identity: 1));
        WrapColumnwise(env, "sort", new(KeepShape: true), dialect);

        // cummax and cummin are cumulative reductions like the two above them, and until M70 they
        // were neither: the body underneath flattens whatever it is handed, so cummax of a matrix
        // ran one sequence through the whole of it rather than one down each column. Wrapping gives
        // them the column, the dimension argument and 'reverse' in a single row. The identity is
        // what a NaN is replaced by while the running value passes over it, which is the losing end
        // of the comparison; MATLAB ignores NaN here by default, unlike cumsum.
        WrapColumnwise(env, "cummax", new(
            KeepShape: true, Words: TailWords.Nan | TailWords.Reverse,
            Identity: double.NegativeInfinity, OmitsNanByDefault: true));
        WrapColumnwise(env, "cummin", new(
            KeepShape: true, Words: TailWords.Nan | TailWords.Reverse,
            Identity: double.PositiveInfinity, OmitsNanByDefault: true));

        // vecnorm(A, p, dim): p sits where the dimension does for everything else, so the dimension
        // moves along one — the same shape std and var have, and the same reason.
        WrapColumnwise(env, "vecnorm", new(LeadingArgs: 1));

        // diff(X, n, dim): n is how many times to difference, not the dimension.
        WrapColumnwise(env, "diff", new(KeepShape: true, LeadingArgs: 1, RepeatsInner: true));

        WrapExtreme(env, "max", dialect, takeMin: false);
        WrapExtreme(env, "min", dialect, takeMin: true);
    }

    /// <summary>
    /// The trailing option words a reduction answers itself rather than handing to the builtin
    /// underneath. Anything not listed rides along on every per-slice call instead, which is how
    /// <c>sort(A, 'descend')</c> still reaches the sort that knows what to do with it.
    /// </summary>
    [Flags]
    private enum TailWords
    {
        None = 0,

        /// <summary><c>'omitnan'</c> / <c>'includenan'</c>.</summary>
        Nan = 1,

        /// <summary><c>'reverse'</c> / <c>'forward'</c> — the cumulative reductions.</summary>
        Reverse = 2,

        /// <summary><c>'default'</c> / <c>'double'</c> / <c>'native'</c> — MATLAB's output class.</summary>
        Outtype = 4,
    }

    /// <summary>
    /// What one reduction name puts in each argument slot after the array, and which option words it
    /// answers itself. The defaults are the plain <c>sum(A, dim)</c> shape, so a name registered with
    /// <c>new()</c> behaves exactly as it did before these roles existed.
    /// </summary>
    /// <param name="KeepShape">
    /// Whether a slice reduces to a whole vector rather than one value — <c>cumsum</c>, <c>sort</c>,
    /// <c>diff</c> — which is what decides how the results are scattered back.
    /// </param>
    /// <param name="LeadingArgs">
    /// How many arguments sit between the array and the dimension. They ride along to every per-slice
    /// call untouched, so the builtin underneath is the one that has to understand them.
    /// </param>
    /// <param name="RepeatsInner">
    /// Whether the leading argument is a repetition count the wrapper consumes rather than passes on:
    /// differencing n times along a dimension is the base builtin applied n times to each slice.
    /// </param>
    /// <param name="Words">The option words this name answers itself.</param>
    /// <param name="Vecdim">
    /// Whether a vector of dimensions may stand where the dimension does — MATLAB's <c>vecdim</c>,
    /// as in <c>sum(A, [1 2])</c>. Only a reduction that gives the same answer applied one dimension
    /// at a time earns this: the sum of the column sums is the whole sum, but the median of the
    /// medians is not the median, which is why <c>median</c> and <c>mode</c> are absent below.
    /// </param>
    /// <param name="OmitsNanByDefault">
    /// Whether this name steps over NaN unless told otherwise. MATLAB splits the cumulative
    /// reductions down the middle here — <c>cumsum</c> keeps NaN, <c>cummax</c> ignores it — so the
    /// default belongs to the name rather than to the family.
    /// </param>
    /// <param name="Identity">
    /// What a slice reduces to once <c>'omitnan'</c> has emptied it — 0 for a sum, 1 for a product,
    /// NaN for a mean, which has nothing left to average. The cumulative reductions put it in the
    /// NaN's place instead, because their result has to stay the length of their input.
    /// </param>
    private readonly record struct ReductionSpec(
        bool KeepShape = false,
        int LeadingArgs = 0,
        bool RepeatsInner = false,
        TailWords Words = TailWords.None,
        double Identity = double.NaN,
        bool Vecdim = false,
        bool OmitsNanByDefault = false);

    /// <remarks>
    /// <paramref name="dialect"/> is passed by the one name here that has a second output: sort
    /// reports the position each value came from, and a position is written in the dialect's own
    /// index base. Every other reduction answers values alone and has no use for it.
    /// </remarks>
    private static void WrapColumnwise(
        JgsEnvironment env, string name, ReductionSpec spec, JgsDialect? dialect = null)
    {
        int indexBase = dialect?.IndexBase ?? 1;
        if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        bool keepShape = spec.KeepShape;
        IJgsCallable inner = existing.AsCallable;

        static bool IsPlaceholder(JgsValue value) =>
            value.Type == JgsType.Array && value.ArrayLength == 0;

        // The option words this name answers itself, pulled out wherever they sit. MATLAB only ever
        // writes them after the positional arguments, so taking them first leaves the slots below
        // unambiguous — and a word this name does not claim stays put, so sort still sees 'descend'.
        (bool OmitNan, bool Reverse, JgsValue[] Remaining) TakeWords(
            IReadOnlyList<JgsValue> args, int line, int col)
        {
            bool omitNan = spec.OmitsNanByDefault;
            bool reverse = false;
            var rest = new List<JgsValue> { args[0] };
            for (int i = 1; i < args.Count; i++)
            {
                string word = args[i].Type == JgsType.String ? args[i].AsString.ToLowerInvariant() : string.Empty;
                bool nan = spec.Words.HasFlag(TailWords.Nan);
                bool order = spec.Words.HasFlag(TailWords.Reverse);
                bool outtype = spec.Words.HasFlag(TailWords.Outtype);

                if (nan && word is "omitnan" or "includenan")
                {
                    omitNan = word == "omitnan";
                }
                else if (order && word is "reverse" or "forward")
                {
                    reverse = word == "reverse";
                }
                else if (outtype && word is "default" or "double")
                {
                    // Every number in here is already a double, so the two spellings that ask for one
                    // are the same no-op.
                }
                else if (outtype && word == "native")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: 'native' asks for the answer in the input's own class, which this reduction does not do — it always answers in double.");
                }
                else
                {
                    rest.Add(args[i]);
                }
            }

            return (omitNan, reverse, rest.ToArray());
        }

        // What is left after the words: the array, then this name's leading arguments, then the
        // dimension. A numeric argument in the dimension's slot is the dimension; anything else
        // ('descend', a bin count) is the inner builtin's own business and rides along per slice.
        (JgsValue Subject, int? Dim, int[]? Vecdim, JgsValue[] Extra, bool All, int Order,
            bool OmitNan, bool Reverse) Split(IReadOnlyList<JgsValue> args, int line, int col)
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least one argument.");
            }

            (bool omitNan, bool reverse, JgsValue[] rest) = TakeWords(args, line, col);
            int next = 1;
            int order = 1;
            var extra = new List<JgsValue>();
            for (int taken = 0; taken < spec.LeadingArgs && next < rest.Length; taken++, next++)
            {
                if (!spec.RepeatsInner)
                {
                    extra.Add(rest[next]); // the builtin underneath is the one that reads it
                    continue;
                }

                // [] in the count's place asks for the default of a single difference.
                order = IsPlaceholder(rest[next]) ? 1 : Whole(rest[next], "number of differences", line, col);
                if (order < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the number of differences must be zero or more, but was {order}.");
                }
            }

            bool all = next < rest.Length && rest[next].Type == JgsType.String
                && rest[next].AsString.Equals("all", StringComparison.OrdinalIgnoreCase);
            int? dim = null;
            int[]? vecdim = null;
            if (all)
            {
                next++;
            }
            else if (next < rest.Length && rest[next].Type == JgsType.Number)
            {
                dim = (int)rest[next].AsNumber;
                next++;
            }
            else if (spec.Vecdim && next < rest.Length
                && IsNumericArray(rest[next]) && rest[next].ArrayLength > 0)
            {
                vecdim = ReadVecdim(name, rest[next], line, col);
                next++;
            }

            extra.AddRange(rest.Skip(next));

            // A name whose leading argument the wrapper consumes passes nothing else down, so anything
            // past the dimension is a mistake rather than an argument the builtin underneath wants.
            if (spec.RepeatsInner && extra.Count > 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} takes at most three arguments (the array, how many times to difference, and the dimension), but got {args.Count}.");
            }

            return (rest[0], dim, vecdim, extra.ToArray(), all, order, omitNan, reverse);
        }

        int Whole(JgsValue value, string what, int line, int col)
        {
            if (value.Type != JgsType.Number || value.AsNumber != System.Math.Floor(value.AsNumber))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the {what} must be a whole number.");
            }

            return (int)value.AsNumber;
        }

        // Differencing n times along one dimension is the base builtin applied n times to each slice,
        // because the slices are walked independently of each other. Every other reduction here has an
        // order of 1, so this is one call for them.
        JgsValue Reduce(JgsValue[] callArgs, int order, int line, int col)
        {
            JgsValue result = inner.Call(callArgs, line, col);
            for (int again = 1; again < order; again++)
            {
                result = inner.Call([result], line, col);
            }

            return result;
        }

        JgsValue[] SliceArgs(double[] slice, JgsValue[] rest)
        {
            var sliceArgs = new JgsValue[rest.Length + 1];
            sliceArgs[0] = Numbers(slice);
            for (int i = 0; i < rest.Length; i++)
            {
                sliceArgs[i + 1] = rest[i];
            }

            return sliceArgs;
        }

        // One slice, through the option words this name answers, and into the builtin underneath.
        // Omitting NaN is deletion for a reduction — a mean's denominator has to shrink with it — and
        // replacement by the identity for a cumulative one, whose answer stays the length of its input.
        JgsValue ReduceSlice(
            double[] slice, JgsValue[] extra, int order, bool omitNan, bool reverse, int line, int col)
        {
            double[] prepared = slice;
            if (omitNan && keepShape)
            {
                prepared = (double[])prepared.Clone();
                for (int i = 0; i < prepared.Length; i++)
                {
                    if (double.IsNaN(prepared[i]))
                    {
                        prepared[i] = spec.Identity;
                    }
                }
            }
            else if (omitNan)
            {
                prepared = System.Array.FindAll(prepared, static v => !double.IsNaN(v));
                if (prepared.Length == 0)
                {
                    // Every value was NaN. What is left is the reduction of nothing: 0 for a sum,
                    // 1 for a product, NaN for a mean, which has nothing to average.
                    return JgsValue.Number(spec.Identity);
                }
            }

            if (!reverse)
            {
                return Reduce(SliceArgs(prepared, extra), order, line, col);
            }

            // 'reverse' runs the slice backwards, so a running total accumulates from the far end. The
            // answer comes back in the same order it went in.
            prepared = (double[])prepared.Clone();
            System.Array.Reverse(prepared);
            JgsValue reversed = Reduce(SliceArgs(prepared, extra), order, line, col);
            double[] back = ToDoubles(name, reversed, line, col);
            System.Array.Reverse(back);
            return back.Length == 1 ? JgsValue.Number(back[0]) : Numbers(back);
        }

        // Only a numeric array is sliced here; a string array, a complex array, an image and a
        // scalar keep the answer the builtin already gave them. An empty one is sliced too (M96b):
        // the shape a reduction of nothing takes is decided by the dimension it ran along, so
        // sum(zeros(0, 3)) is a 1-by-3 of zeros and sum(zeros(3, 0)) is a 1-by-0 — neither of which
        // the builtin underneath, which sees a flat list with nothing in it, could have known.
        static bool Reduces(JgsValue subject) => IsNumericArray(subject);

        // Everything else, exactly as before: reducing along a vector's singleton dimension changes
        // nothing, and a column carries its orientation through the shape-keeping reductions.
        JgsValue Defer(JgsValue subject, int? dim, JgsValue[] extra, int order, int line, int col)
        {
            bool column = subject.Type == JgsType.Array
                && JgsMatrix.ColCount(subject) == 1 && JgsMatrix.RowCount(subject) > 1;
            if (dim == (column ? 2 : 1))
            {
                return subject;
            }

            var direct = new JgsValue[extra.Length + 1];
            direct[0] = subject;
            System.Array.Copy(extra, 0, direct, 1, extra.Length);
            JgsValue reduced = Reduce(direct, order, line, col);
            if (keepShape && column && reduced.Type == JgsType.Array && reduced.ArrayLength > 1)
            {
                reduced.Reshape(reduced.ArrayLength, 1);
            }

            return reduced;
        }

        // The slices to reduce, in the order the result stores them, plus the shape one value per
        // slice takes. MATLAB's default is the first non-singleton dimension.
        (double[][] Slices, int[] Reduced, int[] Dims, int Dim) Cut(
            JgsValue subject, int? named, int line, int col)
        {
            int[] dims = JgsMatrix.DimsOf(subject);
            int dim = named ?? JgsMatrix.DefaultDim(dims);
            if (dim < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the dimension must be a positive whole number, but was {dim}.");
            }

            double[] flat = FlattenColumnMajor(name, subject, line, col);
            (double[][] slices, int[] reduced) = JgsMatrix.SlicesAlong(flat, dims, dim);
            return (slices, reduced, dims, dim);
        }

        // One value per slice lands in the reduced shape; a whole vector per slice is scattered back
        // along the dimension it came from, which is what keeps cumsum and sort the size of the input.
        JgsValue Assemble(
            JgsValue[] results, int[] reduced, int[] dims, int dim,
            JgsValue[] extra, int order, bool omitNan, bool reverse, int line, int col)
        {
            if (!keepShape)
            {
                return JgsMatrix.FromElementsDims(results, reduced);
            }

            var vectors = new double[results.Length][];
            for (int i = 0; i < results.Length; i++)
            {
                vectors[i] = ToDoubles(name, results[i], line, col);
                if (vectors[i].Length != vectors[0].Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} gave slices of different lengths ({vectors[0].Length} and {vectors[i].Length}), so the result has no shape.");
                }
            }

            if (results.Length == 0)
            {
                // There was no slice to reduce, so the join has none to measure either — and the
                // reduced dimension's length is exactly what a slice's answer would have been. Ask
                // for one (M96b): sort and cumsum answer as long as they were asked and diff one
                // shorter, which is why zeros(3, 0) sorted down its columns is 3-by-0 where the
                // empty join on its own would have said 0-by-0.
                int span = dim - 1 < dims.Length ? dims[dim - 1] : 1;
                double[] measured = ToDoubles(
                    name, ReduceSlice(new double[span], extra, order, omitNan, reverse, line, col),
                    line, col);
                return JgsMatrix.FromColumnMajorDims([], JgsMatrix.ShapeAlong(dims, dim, measured.Length));
            }

            (double[] joined, int[] shape) = JgsMatrix.JoinAlong(vectors, dims, dim);
            if (joined.Length == 0)
            {
                return JgsMatrix.FromColumnMajorDims(joined, shape);
            }

            // One value left is a number rather than a one-element array — the same rule the
            // scalar-per-slice path follows, and what makes diff([1 4 9 16], 3) answer 0.
            return joined.Length == 1
                ? JgsValue.Number(joined[0])
                : JgsMatrix.FromColumnMajorDims(joined, shape);
        }

        // The reduction along one dimension, lifted out of Single so a vector of dimensions can walk
        // it once per dimension. Each pass leaves the dimension it reduced a singleton rather than
        // dropping it, so the next dimension still names what it named in the original array — which
        // is what makes the order of a vecdim not matter, in this build as in MATLAB.
        JgsValue Along(
            JgsValue subject, int? dim, JgsValue[] extra, int order, bool omitNan, bool reverse,
            int line, int col)
        {
            if (!Reduces(subject))
            {
                return Defer(subject, dim, extra, order, line, col);
            }

            // sum([]) is 0, prod([]) is 1, mean([]) is NaN. MATLAB does not pick a dimension for the
            // shapeless 0-by-0 — it reduces the whole of it, which is one empty slice, and answers a
            // scalar (M96b). Only 0-by-0 does this and only when no dimension was named: zeros(0, 3)
            // reduces down its columns like any other array, and sum([], 1) is the 1-by-0 that
            // reducing no rows of no columns makes. A shape-keeping reduction has no scalar to
            // answer with and stays out of it: sort([]) is [].
            if (dim is null && !keepShape && subject.ArrayLength == 0
                && JgsMatrix.RowCount(subject) == 0 && JgsMatrix.ColCount(subject) == 0)
            {
                return ReduceSlice([], extra, order, omitNan, reverse, line, col);
            }

            (double[][] slices, int[] reduced, int[] dims, int along) = Cut(subject, dim, line, col);
            var results = new JgsValue[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                results[i] = ReduceSlice(slices[i], extra, order, omitNan, reverse, line, col);
            }

            return Assemble(results, reduced, dims, along, extra, order, omitNan, reverse, line, col);
        }

        JgsValue Single(IReadOnlyList<JgsValue> args, int line, int col)
        {
            (JgsValue subject, int? dim, int[]? vecdim, JgsValue[] extra, bool all, int order,
                bool omitNan, bool reverse) = Split(args, line, col);
            if (order == 0)
            {
                // diff(X, 0) is MATLAB's "difference it no times", which is X itself.
                return subject;
            }

            // A packed double array with arguments the kernels understand reduces in place, without
            // the flatten and the boxed vector per slice this wrapper otherwise pays for (M94). The
            // kernels replicate every fold to the bit, so this is a shortcut, never a different road.
            if (PackedReduceOps.TryColumnwise(
                name, subject, dim, vecdim, all, extra, order, omitNan, reverse, out JgsValue direct))
            {
                return direct;
            }

            // sort is not a fold, so the reduction kernels above pass it by; its own kernels put
            // the storage in order where it lies rather than boxing a comparison per element (M95).
            if (PackedSortOps.TryOrder(name, subject, dim, all, extra, 1, indexBase, out JgsValue[] put))
            {
                return put[0];
            }

            if (all)
            {
                return ReduceSlice(
                    FlattenColumnMajor(name, subject, line, col), extra, order, omitNan, reverse, line, col);
            }

            if (vecdim is not null)
            {
                JgsValue running = subject;
                foreach (int one in vecdim)
                {
                    running = Along(running, one, extra, order, omitNan, reverse, line, col);
                }

                return running;
            }

            return Along(subject, dim, extra, order, omitNan, reverse, line, col);
        }

        JgsValue[] Multi(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            // diff has no second output, and its repeated form is a chain of single-output calls, so
            // the whole decision stays in Single rather than being mirrored here.
            if (spec.RepeatsInner)
            {
                return [Single(args, line, col)];
            }

            // The words are discarded rather than applied: the only name here with a second output is
            // sort, which claims none of them. A name that gained both would have to say what its
            // index output means once values have been dropped, and none does yet.
            (JgsValue subject, int? dim, int[]? vecdim, JgsValue[] extra, bool all, int order, bool _,
                bool _) = Split(args, line, col);

            // MATLAB refuses a second output for a vector of dimensions, and for a good reason: a
            // position within a slice means nothing once several dimensions have been collapsed one
            // after another. The value output is still well defined, so it is the one answered here.
            if (vecdim is not null)
            {
                return [Single(args, line, col)];
            }

            // Both of sort's outputs from one pass. The boxed second output recovered the
            // positions afterwards, by searching the input for each sorted value in turn — which
            // is quadratic, and was the slowest thing left in the engine (M95).
            if (PackedSortOps.TryOrder(name, subject, dim, all, extra, wanted, indexBase, out JgsValue[] put))
            {
                return put;
            }

            if (all || !Reduces(subject))
            {
                if (all || dim is not null)
                {
                    return [Single(args, line, col)];
                }

                return inner is IJgsMultiCallable multi
                    ? multi.CallMultiple(args, wanted, line, col)
                    : [Single(args, line, col)];
            }

            if (inner is not IJgsMultiCallable multiInner)
            {
                return [Single(args, line, col)];
            }

            (double[][] slices, int[] reduced, int[] dims, int along) = Cut(subject, dim, line, col);
            var perSlice = new JgsValue[slices.Length][];
            int produced = int.MaxValue;
            for (int i = 0; i < slices.Length; i++)
            {
                perSlice[i] = multiInner.CallMultiple(SliceArgs(slices[i], extra), wanted, line, col);
                produced = System.Math.Min(produced, perSlice[i].Length);
            }

            var outputs = new JgsValue[System.Math.Min(produced, wanted)];
            for (int o = 0; o < outputs.Length; o++)
            {
                var perOutput = new JgsValue[slices.Length];
                for (int i = 0; i < slices.Length; i++)
                {
                    perOutput[i] = perSlice[i][o];
                }

                // omitNan and reverse stay discarded here for the reason above; they reach Assemble
                // only as the settings a measuring slice would be reduced under, and sort claims
                // neither word.
                outputs[o] = Assemble(
                    perOutput, reduced, dims, along, extra, order,
                    omitNan: false, reverse: false, line, col);
            }

            return outputs;
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, Single) { MultiOutput = Multi }));
    }

    /// <summary>
    /// The dimensions a <c>vecdim</c> names, checked and put in order. Reducing along a dimension
    /// twice is not the same as reducing along it once — the second pass would collapse a singleton
    /// and lose nothing, but MATLAB refuses the call rather than quietly doing nothing, so this does
    /// too. The order is ascending because the answer does not depend on it, and a fixed order is
    /// one less thing for a reader to hold.
    /// </summary>
    private static int[] ReadVecdim(string name, JgsValue value, int line, int col)
    {
        var dims = new List<int>();
        for (int i = 0; i < value.ArrayLength; i++)
        {
            JgsValue element = value.ElementAt(i);
            double raw = element.Type == JgsType.Bool ? (element.IsTruthy ? 1 : 0) : element.AsNumber;
            if (raw != System.Math.Floor(raw) || raw < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: every dimension must be a positive whole number, but one was {raw}.");
            }

            if (dims.Contains((int)raw))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: dimension {(int)raw} is named twice, and a dimension can only be reduced once.");
            }

            dims.Add((int)raw);
        }

        dims.Sort();
        return [.. dims];
    }

    /// <summary>
    /// Whether a value is an array of plain numbers — the only thing the dimension reductions can
    /// slice. A cell, a string array and a complex array all answer false, so they stay with the
    /// builtin that already knows what to do with them.
    /// </summary>
    private static bool IsNumericArray(JgsValue value)
    {
        if (value.Type != JgsType.Array)
        {
            return false;
        }

        if (value.IsPacked)
        {
            return true;
        }

        if (value.IsPackedComplex)
        {
            return false;
        }

        for (int i = 0; i < value.ArrayLength; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type is JgsType.Number or JgsType.Bool)
            {
                continue;
            }

            // The pre-shape representation is an array of row arrays, and is still what a MAT-file
            // load or a workspace restore can produce.
            if (element.Type == JgsType.Array && IsNumericArray(element))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// MATLAB max/min: <c>max(A)</c> reduces along the first non-singleton dimension, <c>max(A, [],
    /// dim)</c> along the one named, <c>max(A, [], 'all')</c> to a single value, and every form has a
    /// second output giving the position the extreme came from. Two array arguments are the
    /// elementwise extreme with scalar broadcast instead.
    /// </summary>
    /// <remarks>
    /// The reduction reads its slices straight out of column-major storage
    /// (<see cref="JgsMatrix.SlicesAlong"/>) rather than folding the value into rows first. Folding is
    /// what limited this to two dimensions: an N-D array read as rows is its pages laid side by side,
    /// so <c>max(A, [], 3)</c> quietly reduced along the fold instead of along the pages.
    /// </remarks>
    private static void WrapExtreme(JgsEnvironment env, string name, JgsDialect dialect, bool takeMin)
    {
        if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        IJgsCallable inner = existing.AsCallable;

        // MATLAB's default is 'omitnan': a NaN in the data is a reading that is missing, not one that
        // beats everything else. Math.Max and Math.Min propagate it instead, so before M52 a single
        // NaN anywhere made max of the whole column NaN — the wrong answer, and a quiet one.
        double Pick(double a, double b, bool omitNan)
        {
            if (omitNan && double.IsNaN(a))
            {
                return b;
            }

            if (omitNan && double.IsNaN(b))
            {
                return a;
            }

            return takeMin ? System.Math.Min(a, b) : System.Math.Max(a, b);
        }

        // The trailing option words, taken off the end wherever the call stops being positional.
        // 'all' is not among them: it sits in the dimension's slot and means something there.
        (bool OmitNan, bool Linear, IReadOnlyList<JgsValue> Remaining) TakeWords(
            IReadOnlyList<JgsValue> args, int line, int col)
        {
            bool omitNan = true;
            bool linear = false;
            var rest = new List<JgsValue>(args);
            while (rest.Count > 1 && rest[^1].Type == JgsType.String)
            {
                switch (rest[^1].AsString.ToLowerInvariant())
                {
                    case "omitnan":
                        omitNan = true;
                        break;
                    case "includenan":
                        omitNan = false;
                        break;
                    case "linear":
                        linear = true;
                        break;
                    default:
                        return (omitNan, linear, rest);
                }

                rest.RemoveAt(rest.Count - 1);
            }

            if (linear && rest.Count < 3)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'linear' asks for an index into the whole array, so it needs the reducing form — {name}(A, [], dim, 'linear').");
            }

            return (omitNan, linear, rest);
        }

        // max(A, [], dim) and max(A, [], 'all'): the [] placeholder says "one input, reduce it".
        static bool IsDimForm(IReadOnlyList<JgsValue> args) =>
            args.Count is 2 or 3 && args[1].Type == JgsType.Array && args[1].ArrayLength == 0;

        static bool IsAll(JgsValue value) =>
            value.Type == JgsType.String && value.AsString.Equals("all", StringComparison.OrdinalIgnoreCase);

        // A one-element result is a scalar, not a one-element array — which is what makes max of a
        // vector a number, and keeps every caller that expected one working.
        static JgsValue Shaped(double[] values, IReadOnlyList<int> dims) =>
            values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajorDims(values, dims);

        // The extreme of nothing is nothing, and MATLAB gives that nothing a shape (M96b). A slice
        // with no elements answers no value, which leaves the reduced dimension zero long; where
        // there was no slice at all to reduce, the dimension collapses to one the way any
        // reduction's does. So max(zeros(0, 3)) is 0-by-3 — reducing down columns that each hold
        // nothing — and max(zeros(3, 0)) is 1-by-0, there being no column to reduce. The shape is
        // empty either way, because the zero that made the input empty is still in it.
        static int[] ExtremeEmptyShape(int[] dims, int dim) =>
            JgsMatrix.ShapeAlong(dims, dim, dim - 1 < dims.Length && dims[dim - 1] == 0 ? 0 : 1);

        static JgsValue[] ExtremeEmpty(int[] shape) =>
            [JgsMatrix.FromColumnMajorDims([], shape), JgsMatrix.FromColumnMajorDims([], shape)];

        // The extreme of every slice along one dimension, paired with the position it came from. Ties
        // go to the first, as MATLAB's do. 'linear' asks for that position as an index into the whole
        // array instead of into its slice, which is what makes A(i) round-trip back to the extreme.
        JgsValue[] ReduceAlong(JgsValue subject, int? named, bool omitNan, bool linear, int line, int col)
        {
            // A packed double array scans where it lies (M94): same extreme, same tie, same NaN
            // rule, and the position already in the dialect's base — but no flat copy first.
            if (PackedReduceOps.TryExtremeAlong(
                subject, named, takeMin, omitNan, linear, dialect.IndexBase, out JgsValue[] direct))
            {
                return direct;
            }

            double[] flat = FlattenColumnMajor(name, subject, line, col);
            int[] dims = JgsMatrix.DimsOf(subject);
            int dim = named ?? JgsMatrix.DefaultDim(dims);
            if (dim < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the dimension must be a positive whole number, but was {dim}.");
            }

            if (flat.Length == 0)
            {
                return ExtremeEmpty(ExtremeEmptyShape(dims, dim));
            }

            (double[][] slices, int[] reduced) = JgsMatrix.SlicesAlong(flat, dims, dim);

            // Where each slice's elements came from, read by cutting 0, 1, 2, ... the same way the
            // data was cut. Asking the layout rather than re-deriving it means the two cannot disagree.
            double[][]? origins = null;
            if (linear)
            {
                var positions = new double[flat.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = i;
                }

                (origins, _) = JgsMatrix.SlicesAlong(positions, dims, dim);
            }

            var extremes = new double[slices.Length];
            var indices = new double[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                (double best, int at) = ExtremeOf(name, slices[i], takeMin, omitNan, line, col);
                extremes[i] = best;
                indices[i] = (origins is null ? at : origins[i][at]) + dialect.IndexBase;
            }

            return [Shaped(extremes, reduced), Shaped(indices, reduced)];
        }

        // 'all' reduces everything at once, so the index it reports is a linear one over the whole
        // array — the same number A(k) would take, which is why 'linear' adds nothing here.
        JgsValue[] ReduceAll(JgsValue subject, bool omitNan, int line, int col)
        {
            if (PackedReduceOps.TryExtremeAll(
                subject, takeMin, omitNan, dialect.IndexBase, out JgsValue[] direct))
            {
                return direct;
            }

            double[] flat = FlattenColumnMajor(name, subject, line, col);
            if (flat.Length == 0)
            {
                // 'all' is every dimension in turn, and over an empty each pass leaves a shape for
                // the next one to reduce: max(zeros(0, 3), [], 'all') is 0-by-3 collapsed to 0-by-1.
                int[] shape = JgsMatrix.DimsOf(subject);
                for (int d = 1; d <= shape.Length; d++)
                {
                    shape = ExtremeEmptyShape(shape, d);
                }

                return ExtremeEmpty(shape);
            }

            (double best, int at) = ExtremeOf(name, flat, takeMin, omitNan, line, col);
            return [JgsValue.Number(best), JgsValue.Number(at + dialect.IndexBase)];
        }

        // A vector of dimensions, collapsed one at a time. Each pass leaves the dimension it
        // reduced a singleton, so the next one still names what it named to begin with. There is no
        // second output: a position inside a slice has no meaning once several dimensions have been
        // collapsed in turn, which is why MATLAB refuses one here too.
        JgsValue[] ReduceVecdim(JgsValue subject, int[] dims, bool omitNan, int line, int col)
        {
            JgsValue running = subject;
            foreach (int one in dims)
            {
                if (running.Type != JgsType.Array)
                {
                    break; // already down to one value; the remaining dimensions are singletons
                }

                running = ReduceAlong(running, one, omitNan, linear: false, line, col)[0];
            }

            return [running];
        }

        JgsValue[] Both(IReadOnlyList<JgsValue> args, bool omitNan, bool linear, int line, int col)
        {
            if (!IsDimForm(args) || args.Count == 2)
            {
                return ReduceAlong(args[0], null, omitNan, linear, line, col);
            }

            if (IsAll(args[2]))
            {
                return ReduceAll(args[0], omitNan, line, col);
            }

            if (args[2].Type == JgsType.Array && IsNumericArray(args[2]) && args[2].ArrayLength > 0)
            {
                return ReduceVecdim(args[0], ReadVecdim(name, args[2], line, col), omitNan, line, col);
            }

            return ReduceAlong(args[0], Count(name, args, 2, line, col), omitNan, linear, line, col);
        }

        // Only a numeric array reduces here. An image, a scalar, the elementwise two-argument form and
        // anything else stay with the builtin that already knows them.
        static bool Reduces(IReadOnlyList<JgsValue> args) =>
            args.Count > 0 && args[0].Type == JgsType.Array && (args.Count == 1 || IsDimForm(args));

        JgsValue Single(IReadOnlyList<JgsValue> args, int line, int col)
        {
            (bool omitNan, bool linear, IReadOnlyList<JgsValue> rest) = TakeWords(args, line, col);
            if (Reduces(rest))
            {
                return Both(rest, omitNan, linear, line, col)[0];
            }

            if (rest.Count == 2 && (rest[0].Type == JgsType.Array || rest[1].Type == JgsType.Array))
            {
                return ElementwiseExtreme(name, rest[0], rest[1], (a, b) => Pick(a, b, omitNan), line, col);
            }

            return inner.Call(rest, line, col);
        }

        JgsValue[] Multi(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            (bool omitNan, bool linear, IReadOnlyList<JgsValue> rest) = TakeWords(args, line, col);
            if (Reduces(rest))
            {
                return Both(rest, omitNan, linear, line, col);
            }

            return inner is IJgsMultiCallable multi
                ? multi.CallMultiple(rest, wanted, line, col)
                : [Single(args, line, col)];
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, Single) { MultiOutput = Multi }));
    }

    /// <summary>
    /// The extreme value and the position it came from. Under <c>'omitnan'</c> — MATLAB's default —
    /// a NaN never wins, so an all-NaN run answers NaN at the first position rather than nothing.
    /// </summary>
    private static (double Value, int At) ExtremeOf(
        string name, double[] values, bool takeMin, bool omitNan, int line, int col)
    {
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
        }

        double best = values[0];
        int at = 0;
        for (int i = 1; i < values.Length; i++)
        {
            double candidate = values[i];

            // Under 'includenan' a NaN swallows the answer the moment one turns up, and the position
            // reported is where it turned up. Comparison alone can never do that: NaN loses every
            // < and > it takes part in, so without this the flag would quietly mean nothing.
            if (!omitNan && double.IsNaN(candidate))
            {
                return (double.NaN, i);
            }

            bool wins = double.IsNaN(best)
                ? !double.IsNaN(candidate)
                : takeMin ? candidate < best : candidate > best;
            if (wins)
            {
                best = candidate;
                at = i;
            }
        }

        return (best, at);
    }

    private static JgsValue ElementwiseExtreme(
        string name, JgsValue left, JgsValue right, Func<double, double, double> pick, int line, int col)
    {
        JgsValue Map(JgsValue shaped, Func<double, double> f)
        {
            // Nothing to map, and the shape is the answer: max(zeros(0, 3), 5) is 0-by-3 (M96b).
            if (JgsEmpty.IsEmptyArray(shaped))
            {
                return shaped;
            }

            if (IsMatrixValue(shaped))
            {
                double[][] rows = RowsOfMatrix(name, shaped, line, col);
                return MatrixFromRows(rows.Select(r => r.Select(f).ToArray()).ToArray());
            }

            double[] values = ToDoubles(name, shaped, line, col);
            return Numbers(values.Select(f).ToArray());
        }

        // Scalar against array: broadcast the scalar.
        if (left.Type is JgsType.Number or JgsType.Bool)
        {
            double scalar = left.AsNumber;
            return Map(right, v => pick(scalar, v));
        }

        if (right.Type is JgsType.Number or JgsType.Bool)
        {
            double scalar = right.AsNumber;
            return Map(left, v => pick(v, scalar));
        }

        if (JgsEmpty.IsEmptyArray(left) && JgsEmpty.IsEmptyArray(right))
        {
            return left; // two empties of one shape pick between no elements at all
        }

        if (IsMatrixValue(left) != IsMatrixValue(right))
        {
            throw new JgsRuntimeException(line, col, $"{name}: both sides must have the same shape.");
        }

        if (IsMatrixValue(left))
        {
            double[][] a = RowsOfMatrix(name, left, line, col);
            double[][] b = RowsOfMatrix(name, right, line, col);
            if (a.Length != b.Length || a[0].Length != b[0].Length)
            {
                throw new JgsRuntimeException(line, col, $"{name}: both matrices must have the same size.");
            }

            return MatrixFromRows(a.Select((row, r) => row.Select((v, c) => pick(v, b[r][c])).ToArray()).ToArray());
        }

        double[] x = ToDoubles(name, left, line, col);
        double[] y = ToDoubles(name, right, line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name}: both vectors must have the same length.");
        }

        return Numbers(x.Select((v, i) => pick(v, y[i])).ToArray());
    }

    // --- Shared matrix helpers ------------------------------------------------------------------

    /// <summary>Whether a value is a matrix — see <see cref="JgsMatrix"/> for what that means now.</summary>
    private static bool IsMatrixValue(JgsValue value) => JgsMatrix.IsMatrix(value);

    /// <summary>A matrix value as rectangular jagged rows of doubles.</summary>
    private static double[][] RowsOfMatrix(string name, JgsValue value, int line, int col) =>
        JgsMatrix.ToRows(name, value, line, col);

    /// <summary>Wraps jagged rows as a matrix value; a single row collapses to a flat vector.</summary>
    private static JgsValue MatrixFromRows(double[][] rows) => JgsMatrix.FromRows(rows);

    /// <summary>Builds a rows-by-cols matrix from a per-cell function; one row collapses to a vector.</summary>
    private static JgsValue BuildMatrix(int rows, int cols, Func<int, int, double> cell) =>
        JgsMatrix.Build(rows, cols, cell);

    private static double[][] TransposeRows(double[][] rows)
    {
        int height = rows.Length;
        int width = height == 0 ? 0 : rows[0].Length;
        var transposed = new double[width][];
        for (int c = 0; c < width; c++)
        {
            transposed[c] = new double[height];
            for (int r = 0; r < height; r++)
            {
                transposed[c][r] = rows[r][c];
            }
        }

        return transposed;
    }

    /// <summary>A value's elements in MATLAB's linear order: down each column, column by column.</summary>
    /// <remarks>
    /// A packed array is <em>already</em> stored in that order, whatever its shape, so the answer is
    /// a copy of its buffer. Without this a shaped one took the jagged-rows road below — which reads
    /// a value per element and, for the commonest shape of all, a column, allocates one row array
    /// per element on the way. <c>min(A(:))</c> over four million numbers spent a second and a half
    /// there and now spends four milliseconds. A vector never did, because vectors are not matrix
    /// values and <see cref="ToDoubles(string, JgsValue, int, int)"/> has had this fast path since M22;
    /// the shaped case is the one that was missing it.
    /// </remarks>
    private static double[] FlattenColumnMajor(string name, JgsValue value, int line, int col)
    {
        if (value.IsPacked)
        {
            NumericBuffer buffer = value.AsBuffer;
            double[] stored = buffer.AsSpan().ToArray();
            GC.KeepAlive(buffer);
            return stored;
        }

        if (!IsMatrixValue(value))
        {
            if (value.Type is JgsType.Number or JgsType.Bool)
            {
                return [value.AsNumber];
            }

            return ToDoubles(name, value, line, col);
        }

        // A matrix with no rows has no first row to measure, and reading one walked off the end of
        // the list outright (M96b) — which is what filter, vecnorm and every other caller of this
        // did the moment [] started arriving as a 0-by-0 rather than a 1-by-0 row.
        double[][] rows = RowsOfMatrix(name, value, line, col);
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        var flat = new double[rows.Length * width];
        int at = 0;
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                flat[at++] = rows[r][c];
            }
        }

        return flat;
    }

    private static JgsValue FlipRows(string name, JgsValue matrix, int line, int col)
    {
        double[][] rows = RowsOfMatrix(name, matrix, line, col);
        System.Array.Reverse(rows);
        return MatrixFromRows(rows);
    }

    private static JgsValue FlipColumns(string name, JgsValue matrix, int line, int col)
    {
        double[][] rows = RowsOfMatrix(name, matrix, line, col);
        foreach (double[] row in rows)
        {
            System.Array.Reverse(row);
        }

        return MatrixFromRows(rows);
    }

    private static JgsValue ReversedVector(string name, JgsValue vector, int line, int col)
    {
        double[] values = ToDoubles(name, vector, line, col);
        System.Array.Reverse(values);
        return Numbers(values);
    }

    /// <summary>MATLAB's transpose for scalars, vectors, and matrices (optionally conjugating).</summary>
    private static JgsValue TransposeValue(string name, JgsValue value, int line, int col, bool conjugate = false)
    {
        if (value.Type == JgsType.Complex)
        {
            return conjugate ? JgsValue.ComplexNum(Complex.Conjugate(value.AsComplex)) : value;
        }

        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.String)
        {
            return value;
        }

        if (!IsMatrixValue(value))
        {
            // Vectors here have no orientation, so their transpose is themselves — matching the
            // interpreter's own ' operator (see EvaluateTranspose).
            return value;
        }

        double[][] rows = RowsOfMatrix(name, value, line, col);
        return MatrixFromRows(TransposeRows(rows));
    }

    private static IEnumerable<JgsValue> EnumerateElements(JgsValue array)
    {
        for (int i = 0; i < array.ArrayLength; i++)
        {
            yield return array.ElementAt(i);
        }
    }

    /// <summary>
    /// <c>cat(dim, …)</c> past the second dimension — the form that stacks planes.
    /// </summary>
    /// <remarks>
    /// Absent until M46 wave L, which is a gap the imaging work had been walking around: <c>cat(3,
    /// R, G, B)</c> is how MATLAB documents building a colour picture out of its planes, and how
    /// wave K's own error message tells a script to build a volume. Storage is already column-major
    /// with real dimensions (M41), so the copy is one contiguous run per piece per trailing position:
    /// everything before the joined dimension moves together, and everything after it repeats.
    /// </remarks>
    private static JgsValue ConcatAlongDimension(string name, int dim, JgsValue[] parts, int line, int col)
    {
        // An empty is omitted from a join past the second dimension too, and cat(3, [], []) is the
        // 0-by-0 empty rather than anything with a third dimension to it (M96b).
        if (TryJoinEmpties(parts, across: true, out JgsValue empty, out IReadOnlyList<JgsValue> joinable))
        {
            return empty;
        }

        parts = [.. joinable];

        var flats = new double[parts.Length][];
        var sizes = new int[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
        {
            JgsValue part = parts[i];
            if (part.Type is JgsType.Number or JgsType.Bool)
            {
                flats[i] = [part.AsNumber];
                sizes[i] = Extend([1, 1], dim);
                continue;
            }

            if (part.Type != JgsType.Array || JgsMatrix.IsNested(part))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} along dimension {dim} joins numeric arrays, but piece {i + 1} is a {part.TypeName}.");
            }

            flats[i] = ToDoubles(name, part, line, col);
            sizes[i] = Extend(JgsMatrix.DimsOf(part), dim);
        }

        // Every dimension but the joined one has to agree, exactly as for rows and columns.
        for (int d = 0; d < dim; d++)
        {
            if (d == dim - 1)
            {
                continue;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                if (sizes[i][d] != sizes[0][d])
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} along dimension {dim}: piece {i + 1} is {sizes[i][d]} long in dimension " +
                        $"{d + 1} where the first is {sizes[0][d]}.");
                }
            }
        }

        int inner = 1;
        for (int d = 0; d < dim - 1; d++)
        {
            inner *= sizes[0][d];
        }

        int outer = 1;
        for (int d = dim; d < sizes[0].Length; d++)
        {
            outer *= sizes[0][d];
        }

        int joined = sizes.Sum(s => s[dim - 1]);
        var result = new double[(long)inner * joined * outer];
        int at = 0;
        for (int o = 0; o < outer; o++)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                int run = inner * sizes[i][dim - 1];
                Array.Copy(flats[i], o * run, result, at, run);
                at += run;
            }
        }

        int[] dims = (int[])sizes[0].Clone();
        dims[dim - 1] = joined;
        return JgsMatrix.FromColumnMajorDims(result, dims);
    }

    /// <summary>Pads a size vector with trailing ones, so a matrix has a third dimension of 1.</summary>
    private static int[] Extend(IReadOnlyList<int> dims, int length)
    {
        var padded = new int[Math.Max(dims.Count, length)];
        for (int i = 0; i < padded.Length; i++)
        {
            padded[i] = i < dims.Count ? dims[i] : 1;
        }

        return padded;
    }

    private static JgsValue ConcatHorizontal(string name, IReadOnlyList<JgsValue> parts, int line, int col)
    {
        if (TryJoinEmpties(parts, across: true, out JgsValue empty, out IReadOnlyList<JgsValue> joinable))
        {
            return empty;
        }

        parts = joinable;

        if (parts.All(static p => p.Type == JgsType.String))
        {
            return JgsValue.Str(string.Concat(parts.Select(static p => p.AsString)));
        }

        if (parts.Any(p => IsMatrixValue(p)))
        {
            double[][][] blocks = parts.Select(p => AsJaggedRows(name, p, line, col)).ToArray();
            int height = blocks[0].Length;
            if (blocks.Any(b => b.Length != height))
            {
                throw new JgsRuntimeException(line, col, $"{name}: every piece must have the same number of rows.");
            }

            var rows = new double[height][];
            for (int r = 0; r < height; r++)
            {
                rows[r] = blocks.SelectMany(b => b[r]).ToArray();
            }

            return MatrixFromRows(rows);
        }

        // Vectors and scalars: one longer vector.
        var flat = new List<double>();
        foreach (JgsValue part in parts)
        {
            if (part.Type is JgsType.Number or JgsType.Bool)
            {
                flat.Add(part.AsNumber);
            }
            else
            {
                flat.AddRange(ToDoubles(name, part, line, col));
            }
        }

        return Numbers(flat.ToArray());
    }

    private static JgsValue ConcatVertical(string name, IReadOnlyList<JgsValue> parts, int line, int col)
    {
        if (TryJoinEmpties(parts, across: false, out JgsValue empty, out IReadOnlyList<JgsValue> joinable))
        {
            return empty;
        }

        parts = joinable;

        double[][][] blocks = parts.Select(p => AsJaggedRows(name, p, line, col)).ToArray();
        int width = blocks[0][0].Length;
        if (blocks.Any(b => b.Any(row => row.Length != width)))
        {
            throw new JgsRuntimeException(line, col, $"{name}: every piece must have the same number of columns.");
        }

        double[][] stacked = blocks.SelectMany(static b => b).ToArray();
        return MatrixFromRows(stacked);
    }

    /// <summary>
    /// Applies MATLAB's rule that an empty array is omitted from a concatenation (M96b), which is
    /// what makes <c>vertcat([], [1 2])</c> a 1-by-2 rather than a shape error and what a script
    /// growing a result from <c>out = []</c> depends on. Answers true — with the whole result in
    /// <paramref name="empty"/> — when there was nothing but empties to join and the shape is
    /// therefore <see cref="JgsEmpty"/>'s to settle; otherwise hands back the pieces that remain.
    /// </summary>
    private static bool TryJoinEmpties(
        IReadOnlyList<JgsValue> parts,
        bool across,
        out JgsValue empty,
        out IReadOnlyList<JgsValue> joinable)
    {
        joinable = JgsEmpty.WithoutEmpties(parts);
        bool allEmpty = true;
        foreach (JgsValue part in joinable)
        {
            allEmpty &= JgsEmpty.IsEmptyArray(part);
        }

        if (!allEmpty)
        {
            empty = JgsValue.Null;
            return false;
        }

        List<(int Rows, int Cols)> shapes = JgsEmpty.ShapesOf(joinable);
        (int rows, int cols) = across ? JgsEmpty.JoinAcross(shapes) : JgsEmpty.JoinDown(shapes);
        empty = JgsEmpty.Shaped(rows, cols);
        return true;
    }

    /// <summary>A scalar, vector, or matrix as jagged rows (scalar and vector become one row).</summary>
    private static double[][] AsJaggedRows(string name, JgsValue value, int line, int col)
    {
        if (IsMatrixValue(value))
        {
            return RowsOfMatrix(name, value, line, col);
        }

        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return [[value.AsNumber]];
        }

        return [ToDoubles(name, value, line, col)];
    }

    /// <summary>MATLAB's magic square, matching its three constructions exactly.</summary>
    private static double[][] MagicSquare(int n)
    {
        var m = new double[n][];
        for (int r = 0; r < n; r++)
        {
            m[r] = new double[n];
        }

        if (n % 2 == 1)
        {
            // Odd: de la Loubère — start above the middle, march up-right, drop on collision.
            int row = 0;
            int column = (n - 1) / 2;
            for (int v = 1; v <= n * n; v++)
            {
                m[row][column] = v;
                int nextRow = (row - 1 + n) % n;
                int nextColumn = (column + 1) % n;
                if (m[nextRow][nextColumn] != 0)
                {
                    row = (row + 1) % n;
                }
                else
                {
                    row = nextRow;
                    column = nextColumn;
                }
            }

            return m;
        }

        if (n % 4 == 0)
        {
            // Doubly even: fill in reading order, then reflect the diagonal 4x4 sub-pattern.
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int v = (r * n) + c + 1;
                    bool invert = r % 4 == c % 4 || (r % 4) + (c % 4) == 3;
                    m[r][c] = invert ? (n * n) + 1 - v : v;
                }
            }

            return m;
        }

        // Singly even: four shifted copies of the odd square, then two column swaps.
        int p = n / 2;
        double[][] a = MagicSquare(p);
        for (int r = 0; r < p; r++)
        {
            for (int c = 0; c < p; c++)
            {
                m[r][c] = a[r][c];
                m[r][c + p] = a[r][c] + (2 * p * p);
                m[r + p][c] = a[r][c] + (3 * p * p);
                m[r + p][c + p] = a[r][c] + (p * p);
            }
        }

        int k = (n - 2) / 4;
        for (int r = 0; r < p; r++)
        {
            // Left k columns and right k-1 columns swap between the top and bottom halves.
            for (int c = 0; c < k; c++)
            {
                (m[r][c], m[r + p][c]) = (m[r + p][c], m[r][c]);
            }

            for (int c = n - k + 1; c < n; c++)
            {
                (m[r][c], m[r + p][c]) = (m[r + p][c], m[r][c]);
            }
        }

        // The middle row's first and center cells swap with their bottom-half partners.
        int mid = k;
        foreach (int c in new[] { 0, k })
        {
            (m[mid][c], m[mid + p][c]) = (m[mid + p][c], m[mid][c]);
        }

        return m;
    }
}
