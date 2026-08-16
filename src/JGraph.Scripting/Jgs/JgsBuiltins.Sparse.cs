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
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

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
                {
                    double[] i = ToDoubles("sparse", args[0], line, col);
                    double[] j = ToDoubles("sparse", args[1], line, col);
                    double[] v = ToDoubles("sparse", args[2], line, col);
                    if (i.Length != j.Length || i.Length != v.Length)
                    {
                        throw new JgsRuntimeException(line, col,
                            "sparse(i, j, v) needs i, j, and v to be the same length.");
                    }

                    int rows = args.Count == 5 ? Count("sparse", args, 3, line, col) : (int)i.Max();
                    int cols = args.Count == 5 ? Count("sparse", args, 4, line, col) : (int)j.Max();
                    var triplets = new (int, int, double)[i.Length];
                    for (int t = 0; t < i.Length; t++)
                    {
                        triplets[t] = ((int)i[t] - 1, (int)j[t] - 1, v[t]);
                    }

                    return JgsValue.Sparse(CscMatrix.FromTriplets(rows, cols, triplets));
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

    /// <summary>How many elements a sparse subscript may materialize before it is refused by name.</summary>
    /// <remarks>
    /// A scalar subscript never materializes anything. Anything else goes through the dense value, and
    /// a sparse matrix is exactly the shape that has no business becoming dense: a 10⁶-by-10⁶ pattern
    /// is a few megabytes sparse and a petabyte dense. The limit is where an accident stops being
    /// affordable, and the message names <c>find</c>, which answers the same question without it.
    /// </remarks>
    private const long SparseSubscriptLimit = 4_000_000;

    /// <summary>
    /// Whether a subscripted name is a sparse matrix being indexed rather than a function being
    /// called — the same question a struct array and a keyed collection each had to answer, for the
    /// same reason: a name followed by parentheses parses as a call until something says otherwise.
    /// </summary>
    internal static bool IsSparseSubscript(JgsValue callee) => callee.Type == JgsType.Sparse;

    /// <summary>
    /// One reading out of a sparse matrix. A pair of scalar subscripts is answered from the stored
    /// columns; anything wider goes through the dense value and comes back sparse, so
    /// <c>issparse</c> still says what it should.
    /// </summary>
    internal static JgsValue SparseSubscript(
        JgsValue value, IReadOnlyList<JgsValue> subscripts, JgsDialect dialect, int line, int col)
    {
        CscMatrix matrix = value.AsSparse;
        if (subscripts.Count == 2 && IsScalarSubscript(subscripts[0]) && IsScalarSubscript(subscripts[1]))
        {
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
            long index = (long)subscripts[0].AsNumber - dialect.IndexBase;
            long total = (long)matrix.Rows * matrix.Cols;
            if (index < 0 || index >= total)
            {
                throw new JgsRuntimeException(line, col,
                    $"Index {subscripts[0].AsNumber} is outside a {matrix.Rows}x{matrix.Cols} matrix.");
            }

            return JgsValue.Number(matrix.At((int)(index % matrix.Rows), (int)(index / matrix.Rows)));
        }

        if ((long)matrix.Rows * matrix.Cols > SparseSubscriptLimit)
        {
            throw new JgsRuntimeException(line, col,
                $"A subscript wider than one element reads a {matrix.Rows}x{matrix.Cols} sparse matrix as a " +
                "dense one, which is too large to hold; use find() to reach its entries.");
        }

        return JgsValue.Sparse(CscFromDense("subscript", SparseAsDense(matrix), line, col));
    }

    private static bool IsScalarSubscript(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool;

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
