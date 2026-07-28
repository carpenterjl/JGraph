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
            (int rows, int cols) = SquareShape("eye", args, line, col);
            return BuildMatrix(rows, cols, static (r, c) => r == c ? 1.0 : 0.0);
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

            // Everything in MATLAB is at least 2-D; only a multi-channel image adds a third.
            return JgsValue.Number(
                args[0].Type == JgsType.Image && args[0].AsImage.Channels > 1 ? 3 : 2);
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
                _ => throw new JgsRuntimeException(line, col, "cat supports dimensions 1 and 2."),
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
            return args[0]; // nothing here has singleton dimensions beyond 2-D to remove
        });

        Define("permute", (args, line, col) =>
        {
            Arity("permute", args, 2, line, col);
            double[] order = ToDoubles("permute", args[1], line, col);
            if (order.Length != 2 || order.Min() != 1 || order.Max() != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "permute supports 2-D orders only: [1 2] (unchanged) or [2 1] (transpose).");
            }

            return order[0] == 2 ? TransposeValue("permute", args[0], line, col) : args[0];
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

        void Filled(string name, IJgsCallable inner) =>
            Redefine(name, (args, line, col) =>
                args.Count == 1 && args[0].Type is JgsType.Number or JgsType.Bool && Count(name, args, 0, line, col) > 1
                    ? inner.Call([args[0], args[0]], line, col)
                    : inner.Call(args, line, col));

        if (env.TryGet("zeros", out JgsValue zeros) && zeros.Type == JgsType.Function)
        {
            Filled("zeros", zeros.AsCallable);
        }

        if (env.TryGet("ones", out JgsValue ones) && ones.Type == JgsType.Function)
        {
            Filled("ones", ones.AsCallable);
        }

        Redefine("rand", (args, line, col) =>
            RandomValue("rand", args, line, col, random.NextDouble));

        double NextGaussian()
        {
            // Box-Muller: two uniforms in, one standard normal out.
            double u1 = 1.0 - random.NextDouble(); // in (0, 1], so Log is finite
            double u2 = random.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2);
        }

        Redefine("randn", (args, line, col) =>
            RandomValue("randn", args, line, col, NextGaussian));
    }

    /// <summary>MATLAB random shapes: () scalar, (n) n-by-n, (r, c) or ([r c]) a matrix.</summary>
    private static JgsValue RandomValue(
        string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double> next)
    {
        ArityRange(name, args, 0, 2, line, col);
        (int rows, int cols) = SquareShape(name, args, line, col);
        if (rows == 1 && cols == 1)
        {
            return JgsValue.Number(next());
        }

        if (rows == 1)
        {
            var flat = new double[cols];
            for (int i = 0; i < cols; i++)
            {
                flat[i] = next();
            }

            return Numbers(flat);
        }

        return BuildMatrix(rows, cols, (_, _) => next());
    }

    /// <summary>
    /// The (rows, cols) a square-defaulting constructor was asked for: () is 1-by-1, (n) is n-by-n,
    /// (r, c) and ([r c]) are as written, ([n]) is n-by-n.
    /// </summary>
    private static (int Rows, int Cols) SquareShape(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        switch (args.Count)
        {
            case 0:
                return (1, 1);
            case 1 when args[0].Type == JgsType.Array:
                double[] dims = ToDoubles(name, args[0], line, col);
                return dims.Length switch
                {
                    1 => ((int)dims[0], (int)dims[0]),
                    2 => ((int)dims[0], (int)dims[1]),
                    _ => throw new JgsRuntimeException(line, col, $"{name} supports at most 2 dimensions."),
                };
            case 1:
                int n = Count(name, args, 0, line, col);
                return (n, n);
            default:
                return (Count(name, args, 0, line, col), Count(name, args, 1, line, col));
        }
    }

    /// <summary>reshape(A, r, c), reshape(A, [r c]), with one dimension allowed to be [].</summary>
    private static JgsValue Reshape(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("reshape", args, 2, 3, line, col);
        double[] flat = FlattenColumnMajor("reshape", args[0], line, col);

        int rows, cols;
        if (args.Count == 2)
        {
            double[] dims = ToDoubles("reshape", args[1], line, col);
            if (dims.Length != 2)
            {
                throw new JgsRuntimeException(line, col, "reshape expects a two-element size, like reshape(A, [r c]).");
            }

            rows = (int)dims[0];
            cols = (int)dims[1];
        }
        else
        {
            // An empty [] dimension means "whatever makes the count work".
            bool rowsWild = args[1].Type == JgsType.Array && args[1].ArrayLength == 0;
            bool colsWild = args[2].Type == JgsType.Array && args[2].ArrayLength == 0;
            if (rowsWild && colsWild)
            {
                throw new JgsRuntimeException(line, col, "reshape can infer at most one dimension from [].");
            }

            cols = colsWild ? 0 : Count("reshape", args, 2, line, col);
            rows = rowsWild ? 0 : Count("reshape", args, 1, line, col);
            if (rowsWild)
            {
                if (cols == 0 || flat.Length % cols != 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"reshape cannot split {flat.Length} element(s) into columns of {cols}.");
                }

                rows = flat.Length / cols;
            }
            else if (colsWild)
            {
                if (rows == 0 || flat.Length % rows != 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"reshape cannot split {flat.Length} element(s) into rows of {rows}.");
                }

                cols = flat.Length / rows;
            }
        }

        if (rows * cols != flat.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"reshape must keep the element count: {flat.Length} element(s) do not fill {rows}x{cols}.");
        }

        // MATLAB reads and fills column by column.
        return BuildMatrix(rows, cols, (r, c) => flat[(c * rows) + r]);
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
            var rows = new JgsValue[subject.ArrayLength];
            for (int r = 0; r < rows.Length; r++)
            {
                JgsValue row = subject.ElementAt(r);
                var mask = new JgsValue[row.ArrayLength];
                for (int c = 0; c < mask.Length; c++)
                {
                    mask[c] = JgsValue.Bool(Contains(row.ElementAt(c)));
                }

                rows[r] = JgsValue.Array(mask);
            }

            return JgsValue.Array(rows);
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

            if (!IsMatrixValue(subject))
            {
                // A vector's first dimension is the singleton one, so reducing along it changes
                // nothing; otherwise the inner builtin runs on the vector alone — a recognized dim
                // argument must never reach it as a value to reduce.
                if (dim == 1)
                {
                    return subject;
                }

                var direct = new JgsValue[extra.Length + 1];
                direct[0] = subject;
                System.Array.Copy(extra, 0, direct, 1, extra.Length);
                return inner.Call(direct, line, col);
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
            if (all || !IsMatrixValue(subject))
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
                if (!IsMatrixValue(args[0]))
                {
                    return dim == 1 ? args[0] : inner.Call([args[0]], line, col);
                }

                double[][] rows = RowsOfMatrix(name, args[0], line, col);
                double[][] slices = dim == 1 ? TransposeRows(rows) : rows;
                (JgsValue[] extremes, _) = ReduceSlices(slices, dialect, line, col);
                return AssembleSliceResults(name, extremes, byColumn: dim == 1, keepShape: false, line, col);
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

            if (args.Count == 1 && IsMatrixValue(args[0]))
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

    /// <summary>Whether a value is a matrix in this model: an array whose elements are arrays.</summary>
    private static bool IsMatrixValue(JgsValue value) =>
        value.Type == JgsType.Array && !value.IsPacked && !value.IsPackedComplex
        && value.ArrayLength > 0 && value.ElementAt(0).Type == JgsType.Array;

    /// <summary>A matrix value as rectangular jagged rows of doubles.</summary>
    private static double[][] RowsOfMatrix(string name, JgsValue value, int line, int col)
    {
        var rows = new double[value.ArrayLength][];
        for (int r = 0; r < rows.Length; r++)
        {
            JgsValue row = value.ElementAt(r);
            if (row.Type != JgsType.Array)
            {
                throw new JgsRuntimeException(line, col, $"{name}: matrix row {r} is a {row.TypeName}, not an array.");
            }

            rows[r] = ToDoubles(name, row, line, col);
            if (rows[r].Length != rows[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: matrix rows must all be the same length (row 0 has {rows[0].Length}, row {r} has {rows[r].Length}).");
            }
        }

        return rows;
    }

    /// <summary>Wraps jagged rows as a matrix value; a single row collapses to a flat vector.</summary>
    private static JgsValue MatrixFromRows(double[][] rows)
    {
        if (rows.Length == 1)
        {
            return Numbers(rows[0]);
        }

        var wrapped = new JgsValue[rows.Length];
        for (int r = 0; r < rows.Length; r++)
        {
            wrapped[r] = Numbers(rows[r]);
        }

        return JgsValue.Array(wrapped);
    }

    /// <summary>Builds a rows-by-cols matrix from a per-cell function; one row collapses to a vector.</summary>
    private static JgsValue BuildMatrix(int rows, int cols, Func<int, int, double> cell)
    {
        if (rows < 0 || cols < 0)
        {
            rows = System.Math.Max(rows, 0);
            cols = System.Math.Max(cols, 0);
        }

        if (rows == 1 && cols == 1)
        {
            return JgsValue.Number(cell(0, 0));
        }

        var jagged = new double[System.Math.Max(rows, 1)][];
        for (int r = 0; r < rows; r++)
        {
            jagged[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                jagged[r][c] = cell(r, c);
            }
        }

        return rows == 0 ? JgsValue.Array([]) : MatrixFromRows(jagged[..rows]);
    }

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
