using System.Numerics;

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
            return IsMatrixValue(args[0])
                ? FlipColumns("fliplr", args[0], line, col)
                : ReversedVector("fliplr", args[0], line, col);
        });

        Define("flipud", (args, line, col) =>
        {
            Arity("flipud", args, 1, line, col);

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

        Redefine("zeros", (args, line, col) => NdConstructorValue("zeros", args, line, col, static () => 0.0));
        Redefine("ones", (args, line, col) => NdConstructorValue("ones", args, line, col, static () => 1.0));
        Redefine("rand", (args, line, col) =>
            NdConstructorValue("rand", args, line, col, random.NextDouble));

        double NextGaussian()
        {
            // Box-Muller: two uniforms in, one standard normal out.
            double u1 = 1.0 - random.NextDouble(); // in (0, 1], so Log is finite
            double u2 = random.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2);
        }

        Redefine("randn", (args, line, col) =>
            NdConstructorValue("randn", args, line, col, NextGaussian));
    }

    /// <summary>
    /// The MATLAB constructor shapes: () scalar, (n) n-by-n, (r, c, …) or a size vector as written —
    /// any number of dimensions, empty ones included (<c>zeros(5, 0, 2)</c>).
    /// </summary>
    private static JgsValue NdConstructorValue(
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
        foreach (string name in new[] { "sum", "prod", "mean", "median", "std", "variance", "mode", "any", "all" })
        {
            WrapColumnwise(env, name, keepShape: false);
        }

        foreach (string name in new[] { "cumsum", "cumprod", "diff", "sort" })
        {
            WrapColumnwise(env, name, keepShape: true);
        }

        WrapExtreme(env, "max", dialect, takeMin: false);
        WrapExtreme(env, "min", dialect, takeMin: true);
    }

    private static void WrapColumnwise(JgsEnvironment env, string name, bool keepShape)
    {
        if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        IJgsCallable inner = existing.AsCallable;

        // A numeric second argument is the dimension; anything else ('descend', a bin count) is the
        // inner builtin's own business and rides along on every per-slice call.
        (JgsValue Subject, int? Dim, JgsValue[] Extra, bool All) Split(IReadOnlyList<JgsValue> args, int line, int col)
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least one argument.");
            }

            if (args.Count >= 2 && args[1].Type == JgsType.String
                && args[1].AsString.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return (args[0], null, args.Skip(2).ToArray(), All: true);
            }

            if (args.Count >= 2 && args[1].Type == JgsType.Number)
            {
                return (args[0], (int)args[1].AsNumber, args.Skip(2).ToArray(), All: false);
            }

            return (args[0], null, args.Skip(1).ToArray(), All: false);
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

        JgsValue Single(IReadOnlyList<JgsValue> args, int line, int col)
        {
            (JgsValue subject, int? dim, JgsValue[] extra, bool all) = Split(args, line, col);
            if (all)
            {
                return inner.Call(SliceArgs(FlattenColumnMajor(name, subject, line, col), extra), line, col);
            }

            // A vector — row or column — reduces to a scalar, not a per-column array of one; a
            // column carries its orientation through the shape-keeping reductions (cumsum, sort).
            if (!IsMatrixValue(subject) || ReducesAsVector(subject))
            {
                bool column = subject.Type == JgsType.Array
                    && JgsMatrix.ColCount(subject) == 1 && JgsMatrix.RowCount(subject) > 1;

                // Reducing along a vector's singleton dimension changes nothing; a recognized dim
                // argument must never reach the inner builtin as a value to reduce.
                if (dim == (column ? 2 : 1))
                {
                    return subject;
                }

                var direct = new JgsValue[extra.Length + 1];
                direct[0] = subject;
                System.Array.Copy(extra, 0, direct, 1, extra.Length);
                JgsValue reduced = inner.Call(direct, line, col);
                if (keepShape && column && reduced.Type == JgsType.Array && reduced.ArrayLength > 1)
                {
                    reduced.Reshape(reduced.ArrayLength, 1);
                }

                return reduced;
            }

            double[][] rows = RowsOfMatrix(name, subject, line, col);
            bool byColumn = (dim ?? 1) == 1;
            double[][] slices = byColumn ? TransposeRows(rows) : rows;
            var results = new JgsValue[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                results[i] = inner.Call(SliceArgs(slices[i], extra), line, col);
            }

            return AssembleSliceResults(name, results, byColumn, keepShape, line, col);
        }

        JgsValue[] Multi(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            (JgsValue subject, int? dim, JgsValue[] extra, bool all) = Split(args, line, col);
            if (all || !IsMatrixValue(subject) || ReducesAsVector(subject))
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

            double[][] rows = RowsOfMatrix(name, subject, line, col);
            bool byColumn = (dim ?? 1) == 1;
            double[][] slices = byColumn ? TransposeRows(rows) : rows;
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
                var column = new JgsValue[slices.Length];
                for (int i = 0; i < slices.Length; i++)
                {
                    column[i] = perSlice[i][o];
                }

                outputs[o] = AssembleSliceResults(name, column, byColumn, keepShape, line, col);
            }

            return outputs;
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, Single) { MultiOutput = Multi }));
    }

    /// <summary>
    /// Reassembles per-slice results: scalar results become a row vector (by column) or an n-by-1
    /// column (by row); vector results become the matrix they slice back into.
    /// </summary>
    private static JgsValue AssembleSliceResults(
        string name, JgsValue[] results, bool byColumn, bool keepShape, int line, int col)
    {
        if (!keepShape)
        {
            // One value per slice. Reducing columns yields a row vector; reducing rows a column.
            return byColumn
                ? JgsValue.Array(results)
                : JgsValue.Array(results.Select(static v => JgsValue.Array([v])).ToArray());
        }

        var sliceRows = new double[results.Length][];
        for (int i = 0; i < results.Length; i++)
        {
            sliceRows[i] = ToDoubles(name, results[i], line, col);
        }

        double[][] shaped = byColumn ? TransposeRows(sliceRows) : sliceRows;
        return MatrixFromRows(shaped);
    }

    /// <summary>
    /// MATLAB max/min: over a matrix, per-column extremes (and per-column indices for the two-output
    /// form); with two array arguments, the elementwise extreme with scalar broadcast.
    /// </summary>
    private static void WrapExtreme(JgsEnvironment env, string name, JgsDialect dialect, bool takeMin)
    {
        if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        IJgsCallable inner = existing.AsCallable;
        double Pick(double a, double b) => takeMin ? System.Math.Min(a, b) : System.Math.Max(a, b);

        // max(A, [], dim): the [] placeholder says "one input, reduce along dim".
        bool IsDimForm(IReadOnlyList<JgsValue> args) =>
            args.Count == 3 && args[1].Type == JgsType.Array && args[1].ArrayLength == 0;

        (JgsValue[] Extremes, JgsValue[] Indices) ReduceSlices(double[][] slices, JgsDialect within, int line, int col)
        {
            var extremes = new JgsValue[slices.Length];
            var indices = new JgsValue[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                double best = ExtremeOf(name, slices[i], takeMin, line, col);
                extremes[i] = JgsValue.Number(best);
                indices[i] = JgsValue.Number(System.Array.IndexOf(slices[i], best) + within.IndexBase);
            }

            return (extremes, indices);
        }

        JgsValue Single(IReadOnlyList<JgsValue> args, int line, int col)
        {
            if (IsDimForm(args))
            {
                int dim = Count(name, args, 2, line, col);
                if (!IsMatrixValue(args[0]) || ReducesAsVector(args[0]))
                {
                    bool column = args[0].Type == JgsType.Array
                        && JgsMatrix.ColCount(args[0]) == 1 && JgsMatrix.RowCount(args[0]) > 1;
                    return dim == (column ? 2 : 1) ? args[0] : inner.Call([args[0]], line, col);
                }

                double[][] rows = RowsOfMatrix(name, args[0], line, col);
                double[][] slices = dim == 1 ? TransposeRows(rows) : rows;
                (JgsValue[] extremes, _) = ReduceSlices(slices, dialect, line, col);
                return AssembleSliceResults(name, extremes, byColumn: dim == 1, keepShape: false, line, col);
            }

            // A vector — either orientation — has a scalar extreme, and the inner builtin already
            // says so; only genuine matrices reduce per column.
            if (args.Count == 1 && ReducesAsVector(args[0]))
            {
                return inner.Call(args, line, col);
            }

            if (args.Count == 1 && IsMatrixValue(args[0]))
            {
                double[][] columns = TransposeRows(RowsOfMatrix(name, args[0], line, col));
                var extremes = new double[columns.Length];
                for (int c = 0; c < columns.Length; c++)
                {
                    extremes[c] = ExtremeOf(name, columns[c], takeMin, line, col);
                }

                return Numbers(extremes);
            }

            if (args.Count == 2 && (args[0].Type == JgsType.Array || args[1].Type == JgsType.Array))
            {
                return ElementwiseExtreme(name, args[0], args[1], Pick, line, col);
            }

            return inner.Call(args, line, col);
        }

        JgsValue[] Multi(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (IsDimForm(args) && IsMatrixValue(args[0]))
            {
                int dim = Count(name, args, 2, line, col);
                double[][] rows = RowsOfMatrix(name, args[0], line, col);
                double[][] slices = dim == 1 ? TransposeRows(rows) : rows;
                (JgsValue[] extremes, JgsValue[] indices) = ReduceSlices(slices, dialect, line, col);
                return
                [
                    AssembleSliceResults(name, extremes, byColumn: dim == 1, keepShape: false, line, col),
                    AssembleSliceResults(name, indices, byColumn: dim == 1, keepShape: false, line, col),
                ];
            }

            if (args.Count == 1 && IsMatrixValue(args[0]) && !ReducesAsVector(args[0]))
            {
                double[][] columns = TransposeRows(RowsOfMatrix(name, args[0], line, col));
                (JgsValue[] extremes, JgsValue[] indices) = ReduceSlices(columns, dialect, line, col);
                return [JgsValue.Array(extremes), JgsValue.Array(indices)];
            }

            return inner is IJgsMultiCallable multi
                ? multi.CallMultiple(args, wanted, line, col)
                : [Single(args, line, col)];
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, Single) { MultiOutput = Multi }));
    }

    /// <summary>
    /// Whether a value reduces the way a vector does — one significant dimension, so its reduction
    /// is a scalar rather than a per-column array of one. N-D arrays never qualify.
    /// </summary>
    private static bool ReducesAsVector(JgsValue value) =>
        value.Type == JgsType.Array && !value.IsNd
        && (JgsMatrix.RowCount(value) == 1 || JgsMatrix.ColCount(value) == 1);

    private static double ExtremeOf(string name, double[] values, bool takeMin, int line, int col)
    {
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
        }

        double best = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            best = takeMin ? System.Math.Min(best, values[i]) : System.Math.Max(best, values[i]);
        }

        return best;
    }

    private static JgsValue ElementwiseExtreme(
        string name, JgsValue left, JgsValue right, Func<double, double, double> pick, int line, int col)
    {
        JgsValue Map(JgsValue shaped, Func<double, double> f)
        {
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
    private static double[] FlattenColumnMajor(string name, JgsValue value, int line, int col)
    {
        if (!IsMatrixValue(value))
        {
            if (value.Type is JgsType.Number or JgsType.Bool)
            {
                return [value.AsNumber];
            }

            return ToDoubles(name, value, line, col);
        }

        double[][] rows = RowsOfMatrix(name, value, line, col);
        var flat = new double[rows.Length * rows[0].Length];
        int at = 0;
        for (int c = 0; c < rows[0].Length; c++)
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
        if (parts.Length == 0)
        {
            return JgsValue.Array([]);
        }

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
        if (parts.Count == 0)
        {
            return JgsValue.Array([]);
        }

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
        if (parts.Count == 0)
        {
            return JgsValue.Array([]);
        }

        double[][][] blocks = parts.Select(p => AsJaggedRows(name, p, line, col)).ToArray();
        int width = blocks[0][0].Length;
        if (blocks.Any(b => b.Any(row => row.Length != width)))
        {
            throw new JgsRuntimeException(line, col, $"{name}: every piece must have the same number of columns.");
        }

        double[][] stacked = blocks.SelectMany(static b => b).ToArray();
        return MatrixFromRows(stacked);
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
