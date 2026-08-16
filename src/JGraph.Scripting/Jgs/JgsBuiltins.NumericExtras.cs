namespace JGraph.Scripting.Jgs;

/// <summary>
/// The core numeric names M52 left on its deferred list (M66 wave B): <c>kron</c>, <c>perms</c>,
/// <c>factor</c>, <c>idivide</c> and <c>interp2</c>.
/// </summary>
/// <remarks>
/// Nothing here is deep — these are five separate small holes rather than one missing idea — but each
/// of them is a name a ported script writes without thinking, and a name that is missing stops a
/// script at the line that uses it rather than degrading what it computes.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec Interp2Options = new(
        "interp2",
        Flags: ["linear", "nearest", "cubic", "spline", "makima"],
        Names: []);

    private static void RegisterNumericExtraBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        Define("kron", KroneckerProduct);
        Define("perms", Permutations);
        Define("factor", PrimeFactors);
        Define("idivide", IntegerDivide);
        Define("interp2", Interpolate2D);
    }

    // --- kron ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>kron(A, B)</c>: every element of A replaced by that element times the whole of B, giving an
    /// <c>(m·p)</c>-by-<c>(n·q)</c> matrix. The block structure is the definition, and writing it as
    /// blocks rather than as an index formula is what keeps it readable.
    /// </summary>
    private static JgsValue KroneckerProduct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("kron", args, 2, line, col);
        double[] a = FlattenColumnMajor("kron", args[0], line, col);
        double[] b = FlattenColumnMajor("kron", args[1], line, col);
        int[] da = SizeDims(args[0]);
        int[] db = SizeDims(args[1]);
        if (da.Length > 2 || db.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "kron works on matrices, not on N-D arrays.");
        }

        int m = da[0];
        int n = da.Length > 1 ? da[1] : 1;
        int p = db[0];
        int q = db.Length > 1 ? db[1] : 1;

        int rows = m * p;
        int cols = n * q;
        var result = new double[rows * cols];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < m; i++)
            {
                double scale = a[i + (j * m)];
                for (int l = 0; l < q; l++)
                {
                    for (int k = 0; k < p; k++)
                    {
                        int row = (i * p) + k;
                        int column = (j * q) + l;
                        result[row + (column * rows)] = scale * b[k + (l * p)];
                    }
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(result, [rows, cols]);
    }

    // --- perms --------------------------------------------------------------------------------

    /// <summary>
    /// <c>perms(v)</c>: every arrangement of the values, one per row, in reverse lexicographic order
    /// of the positions they came from — which is the order MATLAB produces and the reason
    /// <c>perms([1 2 3])</c> starts at <c>3 2 1</c> rather than at <c>1 2 3</c>.
    /// </summary>
    private static JgsValue Permutations(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("perms", args, 1, line, col);
        double[] values = FlattenColumnMajor("perms", args[0], line, col);
        int n = values.Length;

        // 11! rows would be forty million; the answer is a memory exhaustion rather than a result, so
        // it is refused with the size it would have been.
        if (n > 10)
        {
            throw new JgsRuntimeException(line, col,
                $"perms: {n} values have more arrangements than fit in memory; perms goes up to 10.");
        }

        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        var rowsOut = new List<int[]>();
        Permute(order, 0, rowsOut);

        // Lexicographic by position, then reversed: MATLAB's own order.
        rowsOut.Reverse();

        var flat = new double[rowsOut.Count * n];
        for (int r = 0; r < rowsOut.Count; r++)
        {
            for (int c = 0; c < n; c++)
            {
                flat[r + (c * rowsOut.Count)] = values[rowsOut[r][c]];
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [rowsOut.Count, n]);
    }

    /// <summary>Every arrangement of <paramref name="order"/> from <paramref name="at"/> onwards, in order.</summary>
    private static void Permute(int[] order, int at, List<int[]> into)
    {
        if (at == order.Length)
        {
            into.Add((int[])order.Clone());
            return;
        }

        // The rotate-back at the end of each step keeps the tail sorted, which is what makes the
        // whole enumeration come out in lexicographic order rather than in swap order.
        for (int i = at; i < order.Length; i++)
        {
            int held = order[i];
            for (int j = i; j > at; j--)
            {
                order[j] = order[j - 1];
            }

            order[at] = held;
            Permute(order, at + 1, into);

            for (int j = at; j < i; j++)
            {
                order[j] = order[j + 1];
            }

            order[i] = held;
        }
    }

    // --- factor -------------------------------------------------------------------------------

    /// <summary>
    /// <c>factor(n)</c>: the prime factors of a positive whole number, smallest first and repeated as
    /// often as they divide. <c>factor(1)</c> is empty, because one is a product of no primes.
    /// </summary>
    private static JgsValue PrimeFactors(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("factor", args, 1, line, col);
        double raw = Num("factor", args, 0, line, col);
        if (raw < 1 || raw != Math.Floor(raw) || !double.IsFinite(raw))
        {
            throw new JgsRuntimeException(line, col,
                $"factor takes a positive whole number, but got {raw}.");
        }

        // Above 2^53 a double no longer counts by ones, so a factorization would be of whatever the
        // nearest representable number happened to be rather than of what was asked.
        if (raw > 9007199254740992d)
        {
            throw new JgsRuntimeException(line, col,
                "factor: numbers this large are not exactly representable, so their factors would not be theirs.");
        }

        long n = (long)raw;
        var factors = new List<double>();
        for (long p = 2; p * p <= n; p += p == 2 ? 1 : 2)
        {
            while (n % p == 0)
            {
                factors.Add(p);
                n /= p;
            }
        }

        if (n > 1)
        {
            factors.Add(n);
        }

        return Numbers([.. factors]);
    }

    // --- idivide ------------------------------------------------------------------------------

    /// <summary>
    /// <c>idivide(a, b, opt)</c>: integer division that says out loud what it does with the remainder,
    /// rather than leaving it to <c>./</c>'s implicit rounding. The default is <c>'fix'</c> — towards
    /// zero — which is the one case where <c>idivide</c> and <c>a ./ b</c> disagree for an integer
    /// class, and the reason the name exists.
    /// </summary>
    private static JgsValue IntegerDivide(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("idivide", args, 2, 3, line, col);
        string how = args.Count == 3
            ? OneWord("idivide", args, 2, line, col, "fix", "floor", "ceil", "round")
            : "fix";

        // The class of the answer is the class of whichever operand carries one, exactly as MATLAB's
        // integer arithmetic decides it.
        JgsNumericClass? target = JgsNumericClasses.Parse(ClassOf(args[0], JgsDialect.Matlab)) is
            { } left and not JgsNumericClass.Double and not JgsNumericClass.Single
            ? left
            : JgsNumericClasses.Parse(ClassOf(args[1], JgsDialect.Matlab)) is
                { } right and not JgsNumericClass.Double and not JgsNumericClass.Single
                ? right
                : null;

        JgsValue quotient = Zip("idivide", args[0], args[1], (a, b) =>
        {
            if (b == 0)
            {
                return 0; // integer division by zero saturates at zero, as MATLAB's does
            }

            double exact = a / b;
            return how switch
            {
                "floor" => Math.Floor(exact),
                "ceil" => Math.Ceiling(exact),
                "round" => Math.Round(exact, MidpointRounding.AwayFromZero),
                _ => Math.Truncate(exact),
            };
        }, line, col);

        return target is { } numericClass
            ? ToNumericClass("idivide", numericClass, quotient, line, col)
            : quotient;
    }

    // --- interp2 ------------------------------------------------------------------------------

    /// <summary>
    /// <c>interp2(V, Xq, Yq)</c> and <c>interp2(X, Y, V, Xq, Yq)</c>: values read off a grid at places
    /// between its samples. The grid must be plaid — the same x down every row and the same y across
    /// every column — which is what lets the lookup be two one-dimensional searches instead of a
    /// triangulation.
    /// </summary>
    private static JgsValue Interpolate2D(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = Interp2Options.Parse(args, 6, line, col);
        string method = parsed.OneOf("linear", "linear", "nearest", "cubic", "spline", "makima");
        if (method is "cubic" or "spline" or "makima")
        {
            throw new JgsRuntimeException(line, col,
                $"interp2: '{method}' fits a surface through the neighbouring samples, which JGraph does not do — " +
                "'linear' and 'nearest' read off the grid it was given.");
        }

        IReadOnlyList<JgsValue> positional = parsed.Positional;
        double[] xs;
        double[] ys;
        JgsValue grid;
        JgsValue queryX;
        JgsValue queryY;

        switch (positional.Count)
        {
            case 3:
                grid = positional[0];
                (xs, ys) = DefaultGrid(grid, line, col);
                queryX = positional[1];
                queryY = positional[2];
                break;

            case 5:
                grid = positional[2];
                (xs, ys) = GivenGrid(positional[0], positional[1], grid, line, col);
                queryX = positional[3];
                queryY = positional[4];
                break;

            default:
                throw new JgsRuntimeException(line, col,
                    "interp2 takes (V, Xq, Yq) or (X, Y, V, Xq, Yq), and a method word after them.");
        }

        // A query outside the grid has no sample to read, and NaN is what MATLAB puts there by
        // default. An extrapolation value is not accepted, because continuing a surface past its data
        // is a guess about the surface rather than a reading of it.
        const double extrapolation = double.NaN;
        int[] gridDims = SizeDims(grid);
        if (gridDims.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "interp2 reads a matrix of samples.");
        }

        double[] values = FlattenColumnMajor("interp2", grid, line, col);
        double[] askedX = FlattenColumnMajor("interp2", queryX, line, col);
        double[] askedY = FlattenColumnMajor("interp2", queryY, line, col);

        // MATLAB lets the query points be a pair of vectors as well as a pair of matrices; a row of x
        // against a column of y means the full grid of their combinations, and the orientation is the
        // whole signal — two rows of the same length are a list of points, a row and a column are a
        // grid.
        int[] queryDims = SizeDims(queryX);
        if (askedX.Length != askedY.Length || CrossedOrientations(queryX, queryY))
        {
            (askedX, askedY, queryDims) = MeshOfQueries(askedX, askedY);
        }

        var answer = new double[askedX.Length];
        for (int i = 0; i < answer.Length; i++)
        {
            answer[i] = SampleGrid(
                values, gridDims[0], gridDims[1], xs, ys, askedX[i], askedY[i], method, extrapolation);
        }

        return JgsMatrix.FromColumnMajorDims(answer, queryDims);
    }

    /// <summary>The implicit grid of a bare matrix: 1..cols across and 1..rows down.</summary>
    private static (double[] Xs, double[] Ys) DefaultGrid(JgsValue grid, int line, int col)
    {
        int[] dims = SizeDims(grid);
        if (dims.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "interp2 reads a matrix of samples.");
        }

        var xs = new double[dims[1]];
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] = i + 1;
        }

        var ys = new double[dims[0]];
        for (int i = 0; i < ys.Length; i++)
        {
            ys[i] = i + 1;
        }

        return (xs, ys);
    }

    /// <summary>
    /// The grid a call named, whether as vectors or as the matrices <c>meshgrid</c> hands back. A
    /// meshgrid X repeats its row, so its first row is the x coordinates; a meshgrid Y repeats its
    /// column, so its first column is the y coordinates.
    /// </summary>
    private static (double[] Xs, double[] Ys) GivenGrid(
        JgsValue x, JgsValue y, JgsValue grid, int line, int col)
    {
        int[] dims = SizeDims(grid);
        double[] flatX = FlattenColumnMajor("interp2", x, line, col);
        double[] flatY = FlattenColumnMajor("interp2", y, line, col);
        int[] dimX = SizeDims(x);
        int[] dimY = SizeDims(y);

        double[] xs = dimX.Length == 2 && dimX[0] > 1 && dimX[1] > 1
            ? Row(flatX, dimX[0], dimX[1], 0)
            : flatX;
        double[] ys = dimY.Length == 2 && dimY[0] > 1 && dimY[1] > 1
            ? Column(flatY, dimY[0], 0)
            : flatY;

        if (dims.Length == 2 && (xs.Length != dims[1] || ys.Length != dims[0]))
        {
            throw new JgsRuntimeException(line, col,
                $"interp2: a {dims[0]}-by-{dims[1]} grid of samples needs {dims[1]} x values and {dims[0]} y values, " +
                $"but got {xs.Length} and {ys.Length}.");
        }

        return (xs, ys);
    }

    private static double[] Row(double[] flat, int rows, int cols, int row)
    {
        var picked = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            picked[c] = flat[row + (c * rows)];
        }

        return picked;
    }

    /// <summary>Whether the query points are a row of x against a column of y, which names a grid.</summary>
    private static bool CrossedOrientations(JgsValue x, JgsValue y)
    {
        int[] dx = SizeDims(x);
        int[] dy = SizeDims(y);
        if (dx.Length != 2 || dy.Length != 2)
        {
            return false;
        }

        return dx[0] == 1 && dx[1] > 1 && dy[1] == 1 && dy[0] > 1;
    }

    /// <summary>A row of query x against a column of query y, expanded into the grid they describe.</summary>
    private static (double[] X, double[] Y, int[] Dims) MeshOfQueries(double[] xs, double[] ys)
    {
        var x = new double[xs.Length * ys.Length];
        var y = new double[xs.Length * ys.Length];
        for (int c = 0; c < xs.Length; c++)
        {
            for (int r = 0; r < ys.Length; r++)
            {
                x[r + (c * ys.Length)] = xs[c];
                y[r + (c * ys.Length)] = ys[r];
            }
        }

        return (x, y, [ys.Length, xs.Length]);
    }

    /// <summary>One reading off the grid, or the extrapolation value when the point is outside it.</summary>
    private static double SampleGrid(
        double[] values, int rows, int cols, double[] xs, double[] ys,
        double atX, double atY, string method, double outside)
    {
        int cx = BracketOrOutside(xs, atX);
        int cy = BracketOrOutside(ys, atY);
        if (cx < 0 || cy < 0)
        {
            return outside;
        }

        double tx = xs.Length == 1 ? 0 : (atX - xs[cx]) / (xs[cx + 1] - xs[cx]);
        double ty = ys.Length == 1 ? 0 : (atY - ys[cy]) / (ys[cy + 1] - ys[cy]);

        if (method == "nearest")
        {
            int nx = Math.Min(cols - 1, cx + (tx >= 0.5 ? 1 : 0));
            int ny = Math.Min(rows - 1, cy + (ty >= 0.5 ? 1 : 0));
            return values[ny + (nx * rows)];
        }

        int x1 = Math.Min(cols - 1, cx + 1);
        int y1 = Math.Min(rows - 1, cy + 1);
        double topLeft = values[cy + (cx * rows)];
        double topRight = values[cy + (x1 * rows)];
        double bottomLeft = values[y1 + (cx * rows)];
        double bottomRight = values[y1 + (x1 * rows)];

        double top = topLeft + (tx * (topRight - topLeft));
        double bottom = bottomLeft + (tx * (bottomRight - bottomLeft));
        return top + (ty * (bottom - top));
    }

    /// <summary>Which interval of an increasing grid holds a value, or −1 when it lies outside.</summary>
    private static int BracketOrOutside(double[] grid, double at)
    {
        if (double.IsNaN(at) || grid.Length == 0 || at < grid[0] || at > grid[^1])
        {
            return -1;
        }

        if (grid.Length == 1)
        {
            return 0;
        }

        int low = 0;
        int high = grid.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (at < grid[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }
}
