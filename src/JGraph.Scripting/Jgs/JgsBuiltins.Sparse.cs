using JGraph.Api;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The sparse-matrix builtins (M42): <c>sparse</c>, <c>sprand</c>, <c>spy</c>, <c>eigs</c>, and the
/// operator dispatch for <see cref="JgsType.Sparse"/> operands. Storage is compressed sparse column
/// (<see cref="CscMatrix"/>, immutable); <c>*</c> between sparse operands is always the matrix
/// product — sparse matrices are a MATLAB feature and carry MATLAB's operator meanings in either
/// dialect. Anything the sparse kernels don't cover errors by name and points at <c>full</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the sparse builtins into <paramref name="env"/>.</summary>
    /// <param name="env">The scope to declare into.</param>
    /// <param name="random">The run's shared stream, so <c>sprand</c> answers to <c>rng</c> like everything else.</param>
    private static void RegisterSparseBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("sparse", (args, line, col) =>
        {
            switch (args.Count)
            {
                case 1 when args[0].Type == JgsType.Sparse:
                    return args[0];
                case 1:
                    return JgsValue.Sparse(CscFromDense("sparse", args[0], line, col));
                case 2:
                    return JgsValue.Sparse(CscMatrix.FromTriplets(
                        Count("sparse", args, 0, line, col), Count("sparse", args, 1, line, col), []));
                case 3:
                case 5:
                case 6:
                {
                    double[] i = ToDoubles("sparse", args[0], line, col);
                    double[] j = ToDoubles("sparse", args[1], line, col);
                    double[] v = ToDoubles("sparse", args[2], line, col);
                    int length = Math.Max(i.Length, Math.Max(j.Length, v.Length));
                    if (i.Length == 1 && length > 1) i = Enumerable.Repeat(i[0], length).ToArray();
                    if (j.Length == 1 && length > 1) j = Enumerable.Repeat(j[0], length).ToArray();
                    if (v.Length == 1 && length > 1) v = Enumerable.Repeat(v[0], length).ToArray();
                    if (i.Length != j.Length || i.Length != v.Length)
                    {
                        throw new JgsRuntimeException(line, col,
                            "sparse(i, j, v) needs i, j, and v to be the same length.");
                    }

                    int rows = args.Count >= 5 ? Count("sparse", args, 3, line, col) : (i.Length == 0 ? 0 : (int)i.Max());
                    int cols = args.Count >= 5 ? Count("sparse", args, 4, line, col) : (j.Length == 0 ? 0 : (int)j.Max());
                    var triplets = new (int, int, double)[i.Length];
                    for (int t = 0; t < i.Length; t++)
                    {
                        if (!double.IsFinite(i[t]) || !double.IsFinite(j[t]) || i[t] != Math.Truncate(i[t])
                            || j[t] != Math.Truncate(j[t]) || i[t] < 1 || j[t] < 1 || i[t] > rows || j[t] > cols)
                            throw new JgsRuntimeException(line, col, "sparse indices must be positive integers within the matrix dimensions.");
                        triplets[t] = ((int)i[t] - 1, (int)j[t] - 1, v[t]);
                    }

                    var matrix = CscMatrix.FromTriplets(rows, cols, triplets);
                    if (args.Count == 6) matrix = matrix.WithReservedCapacity(Count("sparse", args, 5, line, col));
                    return JgsValue.Sparse(matrix);
                }
                default:
                    throw new JgsRuntimeException(line, col,
                        "sparse takes a matrix, (m, n), (i, j, v), or (i, j, v, m, n).");
            }
        });

        Define("sprand", (args, line, col) =>
        {
            Arity("sprand", args, 3, line, col);
            int rows = Count("sprand", args, 0, line, col);
            int cols = Count("sprand", args, 1, line, col);
            double density = Num("sprand", args, 2, line, col);
            if (density is < 0 or > 1)
            {
                throw new JgsRuntimeException(line, col, "sprand needs a density between 0 and 1.");
            }

            long wanted = (long)Math.Round((double)rows * cols * density);
            var positions = new HashSet<long>();
            var triplets = new List<(int, int, double)>((int)wanted);
            while (positions.Count < wanted)
            {
                int r = random.Next(rows);
                int c = random.Next(cols);
                if (positions.Add(((long)c * rows) + r))
                {
                    triplets.Add((r, c, random.NextDouble()));
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(rows, cols, triplets));
        });

        Define("spy", (args, line, col) =>
        {
            Arity("spy", args, 1, line, col);
            CscMatrix sparse = args[0].Type == JgsType.Sparse
                ? args[0].AsSparse
                : CscFromDense("spy", args[0], line, col);

            // One point per nonzero, row 1 plotted at the top (MATLAB's downward row axis).
            var x = new double[sparse.NonZeroCount];
            var y = new double[sparse.NonZeroCount];
            int at = 0;
            for (int c = 0; c < sparse.Cols; c++)
            {
                for (int i = sparse.ColumnStarts[c]; i < sparse.ColumnStarts[c + 1]; i++)
                {
                    x[at] = c + 1;
                    y[at] = sparse.Rows - sparse.RowIndices[i];
                    at++;
                }
            }

            JG.Scatter(x, y).MarkerSize = 2;
            JG.XLabel($"nz = {sparse.NonZeroCount}");
            return JgsValue.Null;
        });

        JgsValue[] Eigenpairs(IReadOnlyList<JgsValue> args, int line, int col)
        {
            Arity("eigs", args, 2, line, col);
            CscMatrix matrix = args[0].Type == JgsType.Sparse
                ? args[0].AsSparse
                : CscFromDense("eigs", args[0], line, col);
            int count = Count("eigs", args, 1, line, col);
            if (count < 1)
            {
                throw new JgsRuntimeException(line, col, "eigs needs at least one eigenvalue.");
            }

            System.Numerics.Complex[] values;
            System.Numerics.Complex[,] vectors;
            try
            {
                (values, vectors) = matrix.LargestEigenpairs(count);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            var diagonal = new System.Numerics.Complex[values.Length, values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                diagonal[i, i] = values[i];
            }

            return [FromComplexRect(vectors), FromComplexRect(diagonal)];
        }

        Define("eigs",
            (args, line, col) =>
            {
                Arity("eigs", args, 2, line, col);
                CscMatrix matrix = args[0].Type == JgsType.Sparse
                    ? args[0].AsSparse
                    : CscFromDense("eigs", args[0], line, col);
                int count = Count("eigs", args, 1, line, col);
                if (count < 1)
                {
                    throw new JgsRuntimeException(line, col, "eigs needs at least one eigenvalue.");
                }

                System.Numerics.Complex[] values;
                try
                {
                    (values, _) = matrix.LargestEigenpairs(count);
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }

                var column = new System.Numerics.Complex[values.Length, 1];
                for (int i = 0; i < values.Length; i++)
                {
                    column[i, 0] = values[i];
                }

                return FromComplexRect(column);
            },
            (args, _, line, col) => Eigenpairs(args, line, col));
    }

    /// <summary>
    /// Whether a subscripted name is a sparse matrix being indexed rather than a function being
    /// called — the same question a struct array and a keyed collection each had to answer, for the
    /// same reason: a name followed by parentheses parses as a call until something says otherwise.
    /// </summary>
    internal static bool IsSparseSubscript(JgsValue callee) => callee.Type == JgsType.Sparse;

    /// <summary>
    /// One reading out of a sparse matrix. A pair of scalar subscripts is answered from the stored
    /// columns; wider selections gather stored entries and stay sparse, so
    /// <c>issparse</c> still says what it should.
    /// </summary>
    internal static JgsValue SparseSubscript(
        JgsValue value, IReadOnlyList<JgsValue> subscripts, JgsDialect dialect, int line, int col)
    {
        CscMatrix matrix = value.AsSparse;
        if (subscripts.Count == 2 && IsScalarSubscript(subscripts[0]) && IsScalarSubscript(subscripts[1]))
        {
            Validate(subscripts[0].AsNumber, matrix.Rows);
            Validate(subscripts[1].AsNumber, matrix.Cols);
            int r = (int)subscripts[0].AsNumber - dialect.IndexBase;
            int c = (int)subscripts[1].AsNumber - dialect.IndexBase;
            if (r < 0 || r >= matrix.Rows || c < 0 || c >= matrix.Cols)
            {
                throw new JgsRuntimeException(line, col,
                    $"Subscript ({subscripts[0].AsNumber}, {subscripts[1].AsNumber}) is outside a " +
                    $"{matrix.Rows}x{matrix.Cols} matrix.");
            }

            return JgsValue.Number(matrix.At(r, c));
        }

        if (subscripts.Count == 1 && IsScalarSubscript(subscripts[0]))
        {
            Validate(subscripts[0].AsNumber, checked(matrix.Rows * matrix.Cols));
            long index = (long)subscripts[0].AsNumber - dialect.IndexBase;
            long total = (long)matrix.Rows * matrix.Cols;
            if (index < 0 || index >= total)
            {
                throw new JgsRuntimeException(line, col,
                    $"Index {subscripts[0].AsNumber} is outside a {matrix.Rows}x{matrix.Cols} matrix.");
            }

            return JgsValue.Number(matrix.At((int)(index % matrix.Rows), (int)(index / matrix.Rows)));
        }

        void Validate(double index, int extent)
        {
            if (!double.IsFinite(index) || index != Math.Truncate(index))
                throw new JgsRuntimeException(line, col, "Sparse index must be an integer within the matrix dimensions.");
        }
        int[] Picks(JgsValue index, int extent)
        {
            if (index.Type == JgsType.Bool) return index.AsNumber == 0 ? [] : extent > 0 ? [0] : throw new JgsRuntimeException(line, col, "Logical sparse index is outside the matrix dimensions.");
            double[] raw = ToDoubles("subscript", index, line, col);
            if (index.Type == JgsType.Array && index.ArrayLength > 0 && (index.IsPacked ? index.PackedKind == JgsPackedKind.Bool : index.AsArray.All(v => v.Type == JgsType.Bool)))
            {
                int[] selected = raw.Select((v, i) => (v, i)).Where(t => t.v != 0).Select(t => t.i).ToArray();
                if (selected.Any(i => i >= extent)) throw new JgsRuntimeException(line, col, "Logical sparse index is outside the matrix dimensions.");
                return selected;
            }
            if (raw.Any(v => !double.IsFinite(v) || v != Math.Truncate(v) || v < dialect.IndexBase || v >= extent + dialect.IndexBase))
                throw new JgsRuntimeException(line, col, "Sparse index is outside the matrix dimensions.");
            return raw.Select(v => (int)v - dialect.IndexBase).ToArray();
        }
        if (subscripts.Count == 1)
        {
            int[] picks = Picks(subscripts[0], checked(matrix.Rows * matrix.Cols));
            var values = picks.Select(k => matrix.At(k % matrix.Rows, k / matrix.Rows)).ToArray();
            int rows = JgsMatrix.RowCount(subscripts[0]);
            return JgsValue.Sparse(CscMatrix.FromColumnMajor(values, rows, rows == 0 ? 0 : values.Length / rows));
        }
        if (subscripts.Count != 2)
            throw new JgsRuntimeException(line, col, "Sparse matrices support one or two subscripts.");
        int[] rp = Picks(subscripts[0], matrix.Rows), cp = Picks(subscripts[1], matrix.Cols);
        var entries = new List<(int, int, double)>();
        var selectedRows = new Dictionary<int, List<int>>();
        for (int r = 0; r < rp.Length; r++)
        {
            if (!selectedRows.TryGetValue(rp[r], out var destinations)) selectedRows[rp[r]] = destinations = [];
            destinations.Add(r);
        }
        for (int c = 0; c < cp.Length; c++)
            for (int k = matrix.ColumnStarts[cp[c]]; k < matrix.ColumnStarts[cp[c] + 1]; k++)
                if (selectedRows.TryGetValue(matrix.RowIndices[k], out var destinations))
                    foreach (int r in destinations) entries.Add((r, c, matrix.Values[k]));
        return JgsValue.Sparse(CscMatrix.FromTriplets(rp.Length, cp.Length, entries));
    }

    private static bool IsScalarSubscript(JgsValue value) =>
        value.Type is JgsType.Number;

    /// <summary>The dense value a sparse matrix stands for — what <c>full</c> hands back.</summary>
    internal static JgsValue SparseAsDense(CscMatrix matrix) =>
        JgsMatrix.FromColumnMajorDims(matrix.ToColumnMajor(), [matrix.Rows, matrix.Cols]);

    /// <summary>
    /// <c>[i, j, v] = find(S)</c> over stored entries, without expanding anything. This is the answer
    /// a sparse matrix is shaped to give: the entries it holds, in the order it holds them.
    /// </summary>
    internal static JgsValue[] SparseFind(
        CscMatrix matrix, int wanted, JgsDialect dialect, int line, int col)
    {
        var rows = new List<double>(matrix.NonZeroCount);
        var cols = new List<double>(matrix.NonZeroCount);
        var values = new List<double>(matrix.NonZeroCount);
        var linear = new List<double>(matrix.NonZeroCount);

        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
            {
                if (matrix.Values[i] == 0)
                {
                    continue; // a stored zero is still a zero, and find reports what is nonzero
                }

                rows.Add(matrix.RowIndices[i] + dialect.IndexBase);
                cols.Add(c + dialect.IndexBase);
                values.Add(matrix.Values[i]);
                linear.Add(((long)c * matrix.Rows) + matrix.RowIndices[i] + dialect.IndexBase);
            }
        }

        // MATLAB answers find with columns, whatever shape the input had.
        JgsValue Column(List<double> from)
        {
            JgsValue built = Numbers([.. from]);
            built.Reshape(from.Count, from.Count == 0 ? 0 : 1);
            return built;
        }

        _ = line;
        _ = col;
        return wanted <= 1
            ? [Column(linear)]
            : wanted == 2
                ? [Column(rows), Column(cols)]
                : [Column(rows), Column(cols), Column(values)];
    }

    /// <summary>Converts a dense numeric value to CSC storage, dropping exact zeros.</summary>
    private static CscMatrix CscFromDense(string name, JgsValue value, int line, int col)
    {
        double[,] rect = RectOf(name, value, line, col);
        int rows = rect.GetLength(0);
        int cols = rect.GetLength(1);
        var flat = new double[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = rect[r, c];
            }
        }

        return CscMatrix.FromColumnMajor(flat, rows, cols);
    }

    /// <summary>
    /// The binary operators over sparse operands — the interpreter routes here before any dense
    /// machinery runs. Sparse×sparse and sparse±sparse stay sparse; sparse×dense produces a dense
    /// result; scaling by a scalar stays sparse. Everything else names itself and points at
    /// <c>full</c> rather than silently densifying a 25-million-element matrix.
    /// </summary>
    internal static JgsValue SparseBinary(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        int line = at.Line;
        int col = at.Column;
        JgsValue other = left.Type == JgsType.Sparse ? right : left;
        if (op is TokenType.Plus or TokenType.Minus && other.Type is JgsType.Number or JgsType.Bool && other.AsNumber == 0)
            return JgsValue.Sparse((left.Type == JgsType.Sparse ? left : right).AsSparse.Scale(op == TokenType.Minus && right.Type == JgsType.Sparse ? -1 : 1));
        if (op is TokenType.Plus or TokenType.Minus && (left.Type != JgsType.Sparse || right.Type != JgsType.Sparse))
        {
            JgsValue a = left.Type == JgsType.Sparse ? SparseAsDense(left.AsSparse) : left;
            JgsValue b = right.Type == JgsType.Sparse ? SparseAsDense(right.AsSparse) : right;
            int[] ad = SizeDims(a), bd = SizeDims(b);
            int rows = ad[0] == 1 ? bd[0] : ad[0], cols = ad[1] == 1 ? bd[1] : ad[1];
            if ((ad[0] != bd[0] && ad[0] != 1 && bd[0] != 1) || (ad[1] != bd[1] && ad[1] != 1 && bd[1] != 1))
                throw new JgsRuntimeException(line, col, "Array dimensions must agree.");
            var av = ColumnComplex("sparse arithmetic", a, line, col);
            var bv = ColumnComplex("sparse arithmetic", b, line, col);
            var output = new System.Numerics.Complex[rows * cols];
            for (int c = 0; c < cols; c++) for (int r = 0; r < rows; r++)
            {
                var x = av[(ad[1] == 1 ? 0 : c) * ad[0] + (ad[0] == 1 ? 0 : r)];
                var y = bv[(bd[1] == 1 ? 0 : c) * bd[0] + (bd[0] == 1 ? 0 : r)];
                output[c * rows + r] = op == TokenType.Plus ? x + y : x - y;
            }
            return output.Any(z => z.Imaginary != 0) ? ComplexStorage(output, [rows, cols]) : JgsMatrix.FromColumnMajor(output.Select(z => z.Real).ToArray(), rows, cols);
        }
        if (left.Type == JgsType.Sparse && right.Type == JgsType.Sparse)
        {
            CscMatrix a = left.AsSparse;
            CscMatrix b = right.AsSparse;
            try
            {
                return op switch
                {
                    TokenType.Plus => JgsValue.Sparse(a.Add(b)),
                    TokenType.Minus => JgsValue.Sparse(a.Add(b, -1)),
                    TokenType.Star => JgsValue.Sparse(a.Multiply(b)),
                    _ => throw new JgsRuntimeException(line, col,
                        $"'{OpName(op)}' is not supported between sparse matrices; use full() first."),
                };
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        // Scalar scaling keeps the pattern: s*A, A*s, A/s, A.*s.
        JgsValue scalarSide = left.Type == JgsType.Sparse ? right : left;
        if (scalarSide.Type is JgsType.Number or JgsType.Bool &&
            op is TokenType.Star or TokenType.DotStar or TokenType.Slash or TokenType.DotSlash)
        {
            CscMatrix matrix = (left.Type == JgsType.Sparse ? left : right).AsSparse;
            double scalar = scalarSide.Type == JgsType.Bool ? (scalarSide.AsBool ? 1 : 0) : scalarSide.AsNumber;
            if (op is TokenType.Slash or TokenType.DotSlash)
            {
                if (left.Type != JgsType.Sparse)
                {
                    throw new JgsRuntimeException(line, col,
                        "dividing a scalar by a sparse matrix makes it dense; use full() first.");
                }

                scalar = 1 / scalar;
            }

            return JgsValue.Sparse(matrix.Scale(scalar));
        }

        // A\b: the sparse factorization solves it in place of densifying the matrix, which is the
        // whole reason a script chose sparse storage in the first place.
        if (op == TokenType.Backslash && left.Type == JgsType.Sparse)
        {
            CscMatrix a = left.AsSparse;
            double[,] rhs = right.Type == JgsType.Sparse
                ? RectOf("\\", SparseAsDense(right.AsSparse), line, col)
                : RectOf("\\", right, line, col);

            // A column vector arrives as a row when it was written as one, and a right-hand side is
            // a column by definition.
            if (rhs.GetLength(0) == 1 && rhs.GetLength(1) == a.Rows && a.Rows != 1)
            {
                var turned = new double[a.Rows, 1];
                for (int r = 0; r < a.Rows; r++)
                {
                    turned[r, 0] = rhs[0, r];
                }

                rhs = turned;
            }

            if (rhs.GetLength(0) != a.Rows)
            {
                throw new JgsRuntimeException(line, col,
                    $"Matrix dimensions do not agree for the division: a {a.Rows}x{a.Cols} matrix needs " +
                    $"{a.Rows} rows on the right, but got {rhs.GetLength(0)}.");
            }

            int columns = rhs.GetLength(1);
            var solution = new double[a.Rows, columns];
            var b = new double[a.Rows];
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < a.Rows; r++)
                {
                    b[r] = rhs[r, c];
                }

                double[] x;
                try
                {
                    x = a.Solve(b);
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }

                for (int r = 0; r < a.Rows; r++)
                {
                    solution[r, c] = x[r];
                }
            }

            return FromRect(solution);
        }

        // Sparse times a dense matrix or vector: dense result, one matvec per column.
        if (op == TokenType.Star && left.Type == JgsType.Sparse && right.Type == JgsType.Array)
        {
            CscMatrix a = left.AsSparse;
            double[,] dense = RectOf("*", right, line, col);
            int innerRows = dense.GetLength(0);
            int denseCols = dense.GetLength(1);
            if (innerRows != a.Cols)
            {
                throw new JgsRuntimeException(line, col,
                    $"Inner dimensions disagree: {a.Rows}x{a.Cols} times {innerRows}x{denseCols}.");
            }

            var result = new double[a.Rows, denseCols];
            var columnIn = new double[innerRows];
            for (int c = 0; c < denseCols; c++)
            {
                for (int r = 0; r < innerRows; r++)
                {
                    columnIn[r] = dense[r, c];
                }

                double[] columnOut = a.MultiplyVector(columnIn);
                for (int r = 0; r < a.Rows; r++)
                {
                    result[r, c] = columnOut[r];
                }
            }

            return FromRect(result);
        }

        // Dense times sparse: walk the sparse columns, accumulate into the dense result.
        if (op == TokenType.Star && left.Type == JgsType.Array && right.Type == JgsType.Sparse)
        {
            double[,] dense = RectOf("*", left, line, col);
            CscMatrix b = right.AsSparse;
            int denseRows = dense.GetLength(0);
            int inner = dense.GetLength(1);
            if (inner != b.Rows)
            {
                throw new JgsRuntimeException(line, col,
                    $"Inner dimensions disagree: {denseRows}x{inner} times {b.Rows}x{b.Cols}.");
            }

            var result = new double[denseRows, b.Cols];
            for (int c = 0; c < b.Cols; c++)
            {
                for (int i = b.ColumnStarts[c]; i < b.ColumnStarts[c + 1]; i++)
                {
                    int k = b.RowIndices[i];
                    double v = b.Values[i];
                    for (int r = 0; r < denseRows; r++)
                    {
                        result[r, c] += dense[r, k] * v;
                    }
                }
            }

            return FromRect(result);
        }

        throw new JgsRuntimeException(line, col,
            $"'{OpName(op)}' between a sparse matrix and a {(left.Type == JgsType.Sparse ? right : left).TypeName} " +
            "is not supported; use full() first.");
    }

    private static JgsValue SparseDiagonal(CscMatrix input, int offset)
    {
        bool vector = input.Rows == 1 || input.Cols == 1;
        int n = vector ? Math.Max(input.Rows, input.Cols) + Math.Abs(offset) : Math.Max(0, Math.Min(input.Rows + Math.Min(offset, 0), input.Cols - Math.Max(offset, 0)));
        var entries = new List<(int Row, int Col, double Value)>();
        for (int c = 0; c < input.Cols; c++) for (int k = input.ColumnStarts[c]; k < input.ColumnStarts[c + 1]; k++)
        {
            int r = input.RowIndices[k];
            if (vector)
            {
                int i = input.Rows == 1 ? c : r;
                entries.Add((i + Math.Max(0, -offset), i + Math.Max(0, offset), input.Values[k]));
            }
            else if (c - r == offset) entries.Add((r - Math.Max(0, -offset), 0, input.Values[k]));
        }
        return JgsValue.Sparse(CscMatrix.FromTriplets(n, vector ? n : 1, entries));
    }

    private static JgsValue SparseSum(CscMatrix input, int? dim, int[]? vecdim, bool all, bool omitNan, int line, int col)
    {
        int[] dimensions = all ? [1, 2] : vecdim ?? [dim ?? (input.Rows != 1 ? 1 : 2)];
        if (dimensions.Any(d => d < 1)) throw new JgsRuntimeException(line, col, "sum: dimension must be positive.");
        bool rowsReduced = dimensions.Contains(1), colsReduced = dimensions.Contains(2);
        if (!rowsReduced && !colsReduced) return JgsValue.Sparse(input.Scale(1));
        int rows = rowsReduced ? 1 : input.Rows, cols = colsReduced ? 1 : input.Cols;
        var sums = new Dictionary<(int Row, int Col), double>();
        for (int c = 0; c < input.Cols; c++) for (int k = input.ColumnStarts[c]; k < input.ColumnStarts[c + 1]; k++)
        {
            double v = input.Values[k];
            if (omitNan && double.IsNaN(v)) continue;
            var key = (rowsReduced ? 0 : input.RowIndices[k], colsReduced ? 0 : c);
            sums[key] = sums.GetValueOrDefault(key) + v;
        }
        return JgsValue.Sparse(CscMatrix.FromTriplets(rows, cols, sums.Select(p => (p.Key.Row, p.Key.Col, p.Value)).ToArray()));
    }

    /// <summary>The user-facing spelling of an operator token, for sparse error messages.</summary>
    private static string OpName(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Backslash => "\\",
        TokenType.Caret => "^",
        TokenType.DotStar => ".*",
        TokenType.DotSlash => "./",
        TokenType.DotBackslash => ".\\",
        TokenType.DotCaret => ".^",
        _ => op.ToString(),
    };
}
