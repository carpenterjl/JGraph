using JGraph.Api;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The rest of MATLAB's <c>sparfun</c> (M128): the eleven iterative solvers, <c>svds</c>, the
/// diagonal and pattern verbs <c>spdiags</c>, <c>spfun</c>, <c>spones</c>, <c>spconvert</c>,
/// <c>spaugment</c>, <c>sprank</c> and <c>colperm</c>, the random constructors <c>sprandn</c> and
/// <c>sprandsym</c>, and the graph drawings <c>treelayout</c>, <c>treeplot</c>, <c>etreeplot</c>,
/// <c>gplot</c> and <c>unmesh</c>.
/// </summary>
/// <remarks>
/// <para>
/// This file is an adapter and nothing else: it turns a script's arguments into a
/// <see cref="KrylovOperator"/> and a <see cref="KrylovOptions"/>, hands them to
/// <see cref="KrylovSolver"/>, and turns the answer back into MATLAB's five outputs. The one piece
/// of behaviour that lives here rather than in the numerics is the message a solver prints when
/// nobody asked for its <c>flag</c> — text belongs where the output stream is.
/// </para>
/// <para>
/// A matrix argument may be sparse or dense, and a function-handle argument stands in for either
/// the matrix or a preconditioner. <c>bicg</c>, <c>qmr</c> and <c>lsqr</c> need <c>A'x</c> as well
/// as <c>Ax</c>, and MATLAB's convention for saying so is a second argument to the handle reading
/// <c>'notransp'</c> or <c>'transp'</c>; that convention is honoured here, and a handle that
/// refuses the extra argument is a runtime error naming the solver, as it is there.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the Krylov solvers and the remaining sparse names.</summary>
    /// <param name="env">The scope to declare into.</param>
    /// <param name="host">The session, for warnings and printed messages.</param>
    /// <param name="dialect">The dialect, which decides whether a permutation is 0- or 1-based.</param>
    /// <param name="random">The session's stream, so <c>sprandn</c> answers to <c>rng</c>.</param>
    internal static void RegisterSparseKrylovBuiltins(
        JgsEnvironment env, JGraphScriptGlobals host, JgsDialect dialect, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Solver(string name, KrylovMethod method)
        {
            JgsValue[] Run(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                RunKrylov(env, host, name, method, args, wanted, line, col);

            Define(name, (args, line, col) => Run(args, 1, line, col)[0], Run);
        }

        Solver("pcg", KrylovMethod.Pcg);
        Solver("bicg", KrylovMethod.Bicg);
        Solver("bicgstab", KrylovMethod.Bicgstab);
        Solver("bicgstabl", KrylovMethod.Bicgstabl);
        Solver("cgs", KrylovMethod.Cgs);
        Solver("gmres", KrylovMethod.Gmres);
        Solver("lsqr", KrylovMethod.Lsqr);
        Solver("minres", KrylovMethod.Minres);
        Solver("qmr", KrylovMethod.Qmr);
        Solver("symmlq", KrylovMethod.Symmlq);
        Solver("tfqmr", KrylovMethod.Tfqmr);

        Define("svds", (args, line, col) => SingularValues(args, 1, line, col)[0],
            (args, wanted, line, col) => SingularValues(args, wanted, line, col));

        Define("spdiags", (args, line, col) => Spdiags(args, 1, line, col)[0],
            (args, wanted, line, col) => Spdiags(args, wanted, line, col));

        Define("spfun", (args, line, col) =>
        {
            Arity("spfun", args, 2, line, col);
            IJgsCallable f = OdeFunctionOf(env, "spfun", args[0], line, col);
            CscMatrix s = SparseOperandOf("spfun", args[1], line, col);

            // The function sees the stored values as one column, in the order the matrix holds
            // them, because that is what find gives it and what MATLAB hands to feval.
            var stored = new List<(int Row, int Col, double Value)>(s.NonZeroCount);
            var values = new List<double>(s.NonZeroCount);
            for (int c = 0; c < s.Cols; c++)
            {
                for (int i = s.ColumnStarts[c]; i < s.ColumnStarts[c + 1]; i++)
                {
                    if (s.Values[i] == 0)
                    {
                        continue;
                    }

                    stored.Add((s.RowIndices[i], c, s.Values[i]));
                    values.Add(s.Values[i]);
                }
            }

            JgsValue column = JgsMatrix.FromColumnMajorDims([.. values], [values.Count, 1]);
            double[] applied = ToDoubles("spfun", f.Call([column], line, col), line, col);
            if (applied.Length != values.Count)
            {
                throw new JgsRuntimeException(line, col,
                    "spfun: the function must answer one value for each stored entry.");
            }

            var triplets = new List<(int, int, double)>(applied.Length);
            for (int i = 0; i < applied.Length; i++)
            {
                triplets.Add((stored[i].Row, stored[i].Col, applied[i]));
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(s.Rows, s.Cols, triplets));
        });

        Define("spones", (args, line, col) =>
        {
            CscMatrix s = Sparse("spones", args, line, col);
            var triplets = new List<(int, int, double)>(s.NonZeroCount);
            for (int c = 0; c < s.Cols; c++)
            {
                for (int i = s.ColumnStarts[c]; i < s.ColumnStarts[c + 1]; i++)
                {
                    if (s.Values[i] != 0)
                    {
                        triplets.Add((s.RowIndices[i], c, 1));
                    }
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(s.Rows, s.Cols, triplets));
        });

        Define("spconvert", (args, line, col) =>
        {
            Arity("spconvert", args, 1, line, col);
            if (args[0].Type == JgsType.Sparse)
            {
                return args[0];
            }

            double[,] d = RectOf("spconvert", args[0], line, col);
            int columns = d.GetLength(1);
            if (columns != 3)
            {
                throw new JgsRuntimeException(line, col,
                    columns == 4
                        ? "spconvert: a four-column list describes a complex matrix, which sparse storage here does not hold."
                        : "spconvert needs an N-by-3 list of [row, column, value].");
            }

            int rows = 0;
            int cols = 0;
            var triplets = new List<(int, int, double)>(d.GetLength(0));
            for (int k = 0; k < d.GetLength(0); k++)
            {
                int r = (int)d[k, 0];
                int c = (int)d[k, 1];
                if (r < 1 || c < 1 || r != d[k, 0] || c != d[k, 1])
                {
                    throw new JgsRuntimeException(line, col,
                        "spconvert: the first two columns must be positive integer subscripts.");
                }

                rows = Math.Max(rows, r);
                cols = Math.Max(cols, c);
                triplets.Add((r - 1, c - 1, d[k, 2]));
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(rows, cols, triplets));
        });

        Define("spaugment", (args, line, col) =>
        {
            ArityRange("spaugment", args, 1, 2, line, col);
            CscMatrix a = SparseOperandOf("spaugment", args[0], line, col);
            double c = args.Count == 2
                ? Num("spaugment", args, 1, line, col)
                : LargestMagnitude(a) / 1000;

            // [c*I A; A' 0]: the least-squares problem written as one symmetric indefinite system,
            // which is what the name augments.
            int m = a.Rows;
            int n = a.Cols;
            var triplets = new List<(int, int, double)>(a.NonZeroCount * 2 + m);
            for (int i = 0; i < m; i++)
            {
                triplets.Add((i, i, c));
            }

            for (int j = 0; j < n; j++)
            {
                for (int i = a.ColumnStarts[j]; i < a.ColumnStarts[j + 1]; i++)
                {
                    triplets.Add((a.RowIndices[i], m + j, a.Values[i]));
                    triplets.Add((m + j, a.RowIndices[i], a.Values[i]));
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(m + n, m + n, triplets));
        });

        Define("sprank", (args, line, col) =>
            JgsValue.Number(SparseStructure.StructuralRank(Sparse("sprank", args, line, col))));

        Define("colperm", (args, line, col) =>
        {
            CscMatrix s = Sparse("colperm", args, line, col);

            // Nondecreasing nonzero count, ties left in the order they came — MATLAB's sort is
            // stable and a permutation that reordered equal columns would not be the same answer.
            var counts = new int[s.Cols];
            for (int c = 0; c < s.Cols; c++)
            {
                for (int i = s.ColumnStarts[c]; i < s.ColumnStarts[c + 1]; i++)
                {
                    if (s.Values[i] != 0)
                    {
                        counts[c]++;
                    }
                }
            }

            int[] order = [.. Enumerable.Range(0, s.Cols).OrderBy(c => counts[c])];
            return PermutationValue(order, dialect);
        });

        Define("sprandn", (args, line, col) => RandomSparse(args, random, normal: true, line, col));
        Define("sprandsym", (args, line, col) => RandomSymmetric(args, random, line, col));

        Define("treelayout", (args, line, col) => TreeLayout(args, 1, dialect, line, col)[0],
            (args, wanted, line, col) => TreeLayout(args, wanted, dialect, line, col));

        Define("treeplot", (args, line, col) =>
        {
            ArityRange("treeplot", args, 1, 3, line, col);
            double[] parent = ToDoubles("treeplot", args[0], line, col);
            string? nodeSpec = args.Count >= 2 && IsTextScalar(args[1]) ? TextOf(args[1]) : null;
            string? edgeSpec = args.Count >= 3 && IsTextScalar(args[2]) ? TextOf(args[2]) : null;
            DrawTree(parent, args.Count, nodeSpec, edgeSpec, line, col);
            return JgsValue.Null;
        });

        Define("etreeplot", (args, line, col) =>
        {
            ArityRange("etreeplot", args, 1, 3, line, col);
            CscMatrix a = SparseOperandOf("etreeplot", args[0], line, col);
            RequireSquare("etreeplot", a, line, col);
            int[] parent = EtreeOf(SymmetricPatternMatrix(a));
            var asDoubles = new double[parent.Length];
            for (int i = 0; i < parent.Length; i++)
            {
                asDoubles[i] = parent[i] < 0 ? 0 : parent[i] + 1;
            }

            string? nodeSpec = args.Count >= 2 && IsTextScalar(args[1]) ? TextOf(args[1]) : null;
            string? edgeSpec = args.Count >= 3 && IsTextScalar(args[2]) ? TextOf(args[2]) : null;
            DrawTree(asDoubles, args.Count, nodeSpec, edgeSpec, line, col);
            return JgsValue.Null;
        });

        // gplot is the one name here that behaves differently as a statement than as an
        // expression: asked for nothing it draws, asked for X it hands back the coordinates and
        // draws nothing. It therefore has to be told when its answer was discarded.
        env.DeclareFunction("gplot", JgsValue.Function(new BuiltinFunction("gplot",
            (args, line, col) => GraphPlot(args, 1, line, col)[0])
        {
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => GraphPlot(args, wanted, line, col),
        }));

        Define("unmesh", (args, line, col) => Unmesh(args, 1, line, col)[0],
            (args, wanted, line, col) => Unmesh(args, wanted, line, col));
    }

    /// <summary>Which recurrence a solver name stands for.</summary>
    private enum KrylovMethod
    {
        Pcg,
        Bicg,
        Bicgstab,
        Bicgstabl,
        Cgs,
        Gmres,
        Lsqr,
        Minres,
        Qmr,
        Symmlq,
        Tfqmr,
    }

    /// <summary>The largest magnitude of any stored entry — <c>max(max(abs(A)))</c>.</summary>
    private static double LargestMagnitude(CscMatrix a)
    {
        double largest = 0;
        foreach (double v in a.Values)
        {
            largest = Math.Max(largest, Math.Abs(v));
        }

        return largest;
    }

    /// <summary>One argument as a sparse matrix, whatever storage it arrived in.</summary>
    private static CscMatrix SparseOperandOf(string name, JgsValue value, int line, int col) =>
        value.Type == JgsType.Sparse ? value.AsSparse : CscFromDense(name, value, line, col);

    /// <summary>The pattern of <c>A + A'</c> as a matrix, which is what an elimination tree is of.</summary>
    private static CscMatrix SymmetricPatternMatrix(CscMatrix a)
    {
        var triplets = new List<(int, int, double)>(a.NonZeroCount * 2);
        for (int c = 0; c < a.Cols; c++)
        {
            for (int i = a.ColumnStarts[c]; i < a.ColumnStarts[c + 1]; i++)
            {
                if (a.Values[i] == 0)
                {
                    continue;
                }

                triplets.Add((a.RowIndices[i], c, 1));
                triplets.Add((c, a.RowIndices[i], 1));
            }
        }

        return CscMatrix.FromTriplets(a.Rows, a.Cols, triplets);
    }

    /// <summary>Whether an argument was written as <c>[]</c>, which every optional slot reads as "default".</summary>
    private static bool IsOmitted(JgsValue value) =>
        (value.Type == JgsType.Array && value.ArrayLength == 0) || value.Type == JgsType.Null;

    /// <summary>One argument at <paramref name="index"/>, or null when it was absent or <c>[]</c>.</summary>
    private static JgsValue? Optional(IReadOnlyList<JgsValue> args, int index) =>
        index < args.Count && !IsOmitted(args[index]) ? args[index] : null;

    // --- the eleven solvers -------------------------------------------------------------------

    /// <summary>
    /// <c>[x, flag, relres, iter, resvec] = name(A, b, tol, maxit, M1, M2, x0)</c>, with
    /// <c>gmres</c>'s restart length slotted in at position three and <c>lsqr</c>'s rectangular
    /// matrix allowed through.
    /// </summary>
    private static JgsValue[] RunKrylov(JgsEnvironment env, JGraphScriptGlobals host, string name,
        KrylovMethod method, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        bool isGmres = method == KrylovMethod.Gmres;
        ArityRange(name, args, 2, isGmres ? 8 : 7, line, col);

        double[] b = ColumnOf(name, args[1], line, col);
        int rows = b.Length;
        CscMatrix? matrix = args[0].Type == JgsType.Function ? null
            : SparseOperandOf(name, args[0], line, col);
        if (matrix is not null)
        {
            if (method != KrylovMethod.Lsqr && matrix.Rows != matrix.Cols)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NonSquareMatrix",
                    $"{name}: the coefficient matrix must be square.");
            }

            if (matrix.Rows != rows)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:RSHsizeMatchCoeffMatrix",
                    $"{name}: the right-hand side must be a column vector of length {matrix.Rows}.");
            }
        }

        bool needsTranspose = method is KrylovMethod.Bicg or KrylovMethod.Qmr or KrylovMethod.Lsqr;
        KrylovOperator a = matrix is not null
            ? KrylovOperator.Multiply(matrix)
            : HandleOperator(env, name, args[0], needsTranspose, line, col);

        int shift = isGmres ? 1 : 0;
        int? restart = null;
        if (isGmres && Optional(args, 2) is JgsValue restartValue)
        {
            restart = Count(name, [restartValue], 0, line, col);
        }

        double? tolerance = Optional(args, 2 + shift) is JgsValue tolValue
            ? Num(name, [tolValue], 0, line, col)
            : null;
        int? maxIterations = Optional(args, 3 + shift) is JgsValue maxValue
            ? Count(name, [maxValue], 0, line, col)
            : null;
        KrylovOperator? m1 = Optional(args, 4 + shift) is JgsValue m1Value
            ? PreconditionerOf(name, m1Value, needsTranspose, line, col)
            : null;
        KrylovOperator? m2 = Optional(args, 5 + shift) is JgsValue m2Value
            ? PreconditionerOf(name, m2Value, needsTranspose, line, col)
            : null;
        double[]? start = Optional(args, 6 + shift) is JgsValue startValue
            ? ColumnOf(name, startValue, line, col)
            : null;

        var options = new KrylovOptions
        {
            Tolerance = tolerance,
            MaxIterations = maxIterations,
            Left = m1,
            Right = m2,
            InitialGuess = start,
            Restart = restart,
        };

        KrylovResult result;
        try
        {
            result = method switch
            {
                KrylovMethod.Pcg => KrylovSolver.Pcg(a, b, options),
                KrylovMethod.Bicg => KrylovSolver.Bicg(a, b, options),
                KrylovMethod.Bicgstab => KrylovSolver.Bicgstab(a, b, options),
                KrylovMethod.Bicgstabl => KrylovSolver.Bicgstabl(a, b, options),
                KrylovMethod.Cgs => KrylovSolver.Cgs(a, b, options),
                KrylovMethod.Gmres => KrylovSolver.Gmres(a, b, options),
                KrylovMethod.Lsqr => KrylovSolver.Lsqr(a, b, matrix?.Cols ?? 0, options),
                KrylovMethod.Minres => KrylovSolver.Minres(a, b, options),
                KrylovMethod.Qmr => KrylovSolver.Qmr(a, b, options),
                KrylovMethod.Symmlq => KrylovSolver.Symmlq(a, b, options),
                _ => KrylovSolver.Tfqmr(a, b, options),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        foreach (string warning in result.Warnings)
        {
            Warn(env, host, warning, line, col);
        }

        if (wanted < 2)
        {
            // Unrestarted gmres reports one number, not the pair its fourth output carries.
            double[] reported = isGmres && restart is null && result.Iteration.Length == 2
                ? [result.Iteration[1]]
                : result.Iteration;
            host.print(IterationMessage(name, restart, options.Tolerance ?? 1e-6, result, reported));
        }

        JgsValue solution = JgsMatrix.FromColumnMajorDims(result.Solution, [result.Solution.Length, 1]);
        JgsValue iteration = result.Iteration.Length == 2
            ? JgsMatrix.FromColumnMajorDims([result.Iteration[0], result.Iteration[1]], [1, 2])
            : JgsValue.Number(result.Iteration[0]);
        JgsValue residuals = JgsMatrix.FromColumnMajorDims(
            result.ResidualNorms, [result.ResidualNorms.Length, 1]);
        double[]? sixth = method == KrylovMethod.Lsqr
            ? result.LeastSquaresNorms
            : result.ConjugateGradientNorms;

        return sixth is null
            ? Outputs(wanted, solution, JgsValue.Number(result.Flag),
                JgsValue.Number(result.RelativeResidual), iteration, residuals)
            : Outputs(wanted, solution, JgsValue.Number(result.Flag),
                JgsValue.Number(result.RelativeResidual), iteration, residuals,
                JgsMatrix.FromColumnMajorDims(sixth, [sixth.Length, 1]));
    }

    /// <summary>
    /// A postorder of a tree given by its parent pointers, obtained the way MATLAB's
    /// <c>treelayout</c> obtains one: build the tree's own adjacency and ask <c>etree</c>.
    /// </summary>
    private static int[] PostorderOfTree(int[] parent)
    {
        int n = parent.Length;
        var triplets = new List<(int, int, double)>((3 * n) + 1);
        for (int i = 0; i < n; i++)
        {
            triplets.Add((i, i, 1));
            if (parent[i] > 0)
            {
                triplets.Add((parent[i] - 1, i, 1));
                triplets.Add((i, parent[i] - 1, 1));
            }
        }

        return [.. Postorder(EtreeOf(CscMatrix.FromTriplets(n, n, triplets))).Select(static v => v + 1)];
    }

    /// <summary>A column vector argument, refused when it is a matrix — as every solver refuses it.</summary>
    private static double[] ColumnOf(string name, JgsValue value, int line, int col)
    {
        int[] dims = SizeDims(value.Type == JgsType.Sparse ? SparseAsDense(value.AsSparse) : value);
        if (dims.Length > 2 || (dims[1] != 1 && dims[0] != 1))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:RSHnotColumn",
                $"{name}: the right-hand side and the initial guess must be column vectors.");
        }

        return ToDoubles(name, value.Type == JgsType.Sparse ? SparseAsDense(value.AsSparse) : value, line, col);
    }

    /// <summary>
    /// A function handle standing in for the matrix. MATLAB signals the direction with a trailing
    /// <c>'notransp'</c> or <c>'transp'</c>, and only for the three solvers that need both.
    /// </summary>
    private static KrylovOperator HandleOperator(JgsEnvironment env, string name, JgsValue given,
        bool needsTranspose, int line, int col)
    {
        IJgsCallable handle = OdeFunctionOf(env, name, given, line, col);

        double[] Call(double[] x, string? direction)
        {
            JgsValue column = JgsMatrix.FromColumnMajorDims((double[])x.Clone(), [x.Length, 1]);
            JgsValue[] arguments = direction is null ? [column] : [column, JgsValue.Str(direction)];
            return ToDoubles(name, handle.Call(arguments, line, col), line, col);
        }

        return needsTranspose
            ? KrylovOperator.Function(x => Call(x, "notransp"), x => Call(x, "transp"))
            : KrylovOperator.Function(x => Call(x, null), null);
    }

    /// <summary>A preconditioner: a matrix solved against, or a handle that already answers <c>M\x</c>.</summary>
    private static KrylovOperator PreconditionerOf(string name, JgsValue given, bool needsTranspose,
        int line, int col)
    {
        if (given.Type == JgsType.Function)
        {
            IJgsCallable handle = given.AsCallable;
            double[] Call(double[] x, string? direction)
            {
                JgsValue column = JgsMatrix.FromColumnMajorDims((double[])x.Clone(), [x.Length, 1]);
                JgsValue[] arguments = direction is null ? [column] : [column, JgsValue.Str(direction)];
                return ToDoubles(name, handle.Call(arguments, line, col), line, col);
            }

            // Only the three solvers that ask their preconditioner for a transposed solve pass the
            // direction flag; a handle written for pcg takes the vector and nothing else.
            return needsTranspose
                ? KrylovOperator.Function(x => Call(x, "notransp"), x => Call(x, "transp"))
                : KrylovOperator.Function(x => Call(x, null), null);
        }

        try
        {
            return KrylovOperator.Solve(SparseOperandOf(name, given, line, col));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:WrongPrecondSize", $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The message a solver prints when the caller did not ask for its flag — MATLAB's
    /// <c>private\itermsg.m</c>, assembled here because it is output and output belongs in the
    /// adapter. The two numbers it formats go through <c>%0.2g</c>, so a relative residual of
    /// 8.7e-16 prints as <c>8.7e-16</c> and a tolerance of 1e-14 as <c>1e-14</c>.
    /// </summary>
    private static string IterationMessage(string name, int? restart, double tolerance,
        KrylovResult result, double[] reported)
    {
        string Short(double value) =>
            JgsSprintf.FormatMatlab("%0.2g", [JgsValue.Number(value)]);

        string label = restart is null ? name : $"{name}({restart.Value})";

        string Count(double[] parts, bool verbose)
        {
            if (parts.Length == 2)
            {
                return verbose
                    ? $"outer iteration {(long)parts[0]} (inner iteration {(long)parts[1]})"
                    : $"(number {(long)parts[0]}({(long)parts[1]}))";
            }

            double it = parts[0];
            if (it != Math.Truncate(it))
            {
                string half = JgsSprintf.FormatMatlab("%.1f", [JgsValue.Number(it)]);
                return verbose ? $"iteration {half}" : $"(number {half})";
            }

            return verbose ? $"iteration {(long)it}" : $"(number {(long)it})";
        }

        double[] stopped = result.StoppedAt.Length > 0 ? result.StoppedAt : reported;
        if (result.Flag == 0)
        {
            if (reported.All(static v => v == 0))
            {
                return result.ResidualNorms.Length == 1 && result.ResidualNorms[0] == 0
                    && result.RelativeResidual == 0
                    ? $"The right hand side vector is all zero so {name}\n"
                        + "returned an all zero solution without iterating."
                    : $"The initial guess has relative residual {Short(result.RelativeResidual)} which is within\n"
                        + $"the desired tolerance {Short(tolerance)} so {name} returned it without iterating.";
            }

            return $"{label} converged at {Count(reported, true)} to a solution with relative "
                + $"residual {Short(result.RelativeResidual)}.";
        }

        string because = result.Flag switch
        {
            1 => "because the maximum number of iterations was reached.",
            2 => "because the system involving the preconditioner was ill conditioned.",
            3 => "because the method stagnated.",
            4 => "because a scalar quantity became too small or too large to continue computing.",
            _ => "because the preconditioner is not symmetric positive definite.",
        };

        // "stopped at" is where the loop gave up; "the iterate returned" is where the answer came
        // from, and after a run that kept its best iterate those are not the same place.
        return $"{label} stopped at {Count(stopped, true)} without converging to the desired "
            + $"tolerance {Short(tolerance)}\n{because}\n"
            + $"The iterate returned {Count(reported, false)} has relative "
            + $"residual {Short(result.RelativeResidual)}.";
    }

    // --- svds ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>s = svds(A, k, sigma)</c> and <c>[U, S, V, flag] = svds(...)</c>.
    /// </summary>
    private static JgsValue[] SingularValues(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("svds", args, 1, 3, line, col);
        CscMatrix a = SparseOperandOf("svds", args[0], line, col);
        int k = args.Count >= 2 && !IsOmitted(args[1]) ? Count("svds", args, 1, line, col) : 6;
        var selection = SparseSingularValues.Selection.Largest;
        double sigma = 0;
        if (args.Count >= 3 && !IsOmitted(args[2]))
        {
            if (IsTextScalar(args[2]))
            {
                selection = TextOf(args[2]).ToLowerInvariant() switch
                {
                    "largest" => SparseSingularValues.Selection.Largest,
                    "smallest" => SparseSingularValues.Selection.Smallest,
                    "smallestnz" => SparseSingularValues.Selection.SmallestNonZero,
                    _ => throw new JgsRuntimeException(line, col,
                        "svds: sigma must be 'largest', 'smallest', 'smallestnz', or a number."),
                };
            }
            else
            {
                selection = SparseSingularValues.Selection.Nearest;
                sigma = Num("svds", args, 2, line, col);
            }
        }

        SingularTriplet[] triplets = SparseSingularValues.Compute(
            a, k, selection, sigma, 1e-10, null, out bool converged);

        if (wanted <= 1)
        {
            var values = triplets.Select(static t => t.Value).ToArray();
            return [JgsMatrix.FromColumnMajorDims(values, [values.Length, 1])];
        }

        int found = triplets.Length;
        JgsValue u = JgsMatrix.Build(a.Rows, found, (r, c) => triplets[c].Left[r]);
        JgsValue s = JgsMatrix.Build(found, found, (r, c) => r == c ? triplets[r].Value : 0);
        JgsValue v = JgsMatrix.Build(a.Cols, found, (r, c) => triplets[c].Right[r]);
        return Outputs(wanted, u, s, v, JgsValue.Number(converged ? 0 : 1));
    }

    // --- spdiags ------------------------------------------------------------------------------

    /// <summary>The four forms of <c>spdiags</c>: extract all, extract some, replace, and create.</summary>
    private static JgsValue[] Spdiags(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("spdiags", args, 1, 4, line, col);
        if (args.Count <= 2)
        {
            CscMatrix a = SparseOperandOf("spdiags", args[0], line, col);
            int[] offsets;
            if (args.Count == 2)
            {
                offsets = [.. ToDoubles("spdiags", args[1], line, col).Select(WholeOffset)];
            }
            else
            {
                (_, _, offsets) = SparseStructure.ExtractDiagonals(a);
            }

            double[] compact = SparseStructure.ExtractDiagonals(a, offsets);
            int height = Math.Min(a.Rows, a.Cols);
            return Outputs(wanted,
                JgsMatrix.FromColumnMajorDims(compact, [height, offsets.Length]),
                JgsMatrix.FromColumnMajorDims(
                    [.. offsets.Select(static d => (double)d)], [offsets.Length, 1]));
        }

        JgsValue given = args[0].Type == JgsType.Sparse ? SparseAsDense(args[0].AsSparse) : args[0];
        int[] shape = SizeDims(given);
        double[] b = ToDoubles("spdiags", given, line, col);
        int[] wantedOffsets = [.. ToDoubles("spdiags", args[1], line, col).Select(WholeOffset)];

        CscMatrix? existing = null;
        int m;
        int n;
        if (args.Count == 3)
        {
            existing = SparseOperandOf("spdiags", args[2], line, col);
            m = existing.Rows;
            n = existing.Cols;
        }
        else
        {
            m = Count("spdiags", args, 2, line, col);
            n = Count("spdiags", args, 3, line, col);
        }

        // MATLAB's own size check: a column of B is only long enough if it reaches the last
        // position the diagonal actually occupies, and that position depends on the matrix's shape.
        foreach (int d in wantedOffsets)
        {
            int first = Math.Max(1, 1 - d);
            int last = Math.Min(m, n - d);
            int needed = first > last ? 0 : Math.Max(first, last) + (m >= n ? d : 0);
            if (shape[0] != 1 && needed > shape[0])
            {
                throw new JgsRuntimeException(line, col, "MATLAB:spdiags:InvalidSizeB",
                    "spdiags: B is too small for the diagonals asked for.");
            }
        }

        if (shape[1] != 1 && shape[1] < wantedOffsets.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:spdiags:InvalidSizeB",
                "spdiags: B needs one column per diagonal.");
        }

        return [JgsValue.Sparse(SparseStructure.BuildFromDiagonals(
            b, shape[0], shape[1], wantedOffsets, m, n, existing))];

        int WholeOffset(double d)
        {
            if (!double.IsFinite(d) || d != Math.Truncate(d))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:spdiags:InvalidDiagonal",
                    "spdiags: the diagonals must be integers.");
            }

            return (int)d;
        }
    }

    // --- trees and graphs ---------------------------------------------------------------------

    /// <summary><c>[x, y, h, s] = treelayout(parent, post)</c>.</summary>
    private static JgsValue[] TreeLayout(IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect,
        int line, int col)
    {
        ArityRange("treelayout", args, 1, 2, line, col);
        double[] given = ToDoubles("treelayout", args[0], line, col);
        int n = given.Length;
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(given[i]) || given[i] != Math.Truncate(given[i])
                || given[i] < 0 || given[i] > n)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:treelayout:InvalidParentPointers",
                    "treelayout: the parent pointers must be node numbers, with 0 for a root.");
            }

            parent[i] = (int)given[i];
        }

        int[] postorder;
        int[]? permutation = null;
        if (args.Count == 2)
        {
            postorder = [.. ToDoubles("treelayout", args[1], line, col).Select(static v => (int)v)];
        }
        else
        {
            // A parent vector not in etree order is renumbered first and the answer permuted back,
            // which is what lets treeplot([2 4 2 0 6 4 6]) draw the tree it names.
            if (parent.Where((p, i) => p != 0 && p <= i + 1).Any())
            {
                if (parent.Where((p, i) => p == i + 1).Any())
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:treelayout:InvalidParentPointers",
                        "treelayout: a node cannot be its own parent.");
                }

                (parent, permutation) = SparseStructure.FixParent(parent);
            }

            postorder = PostorderOfTree(parent);
        }

        (double[] x, double[] y, int height, int separator) = SparseStructure.TreeLayout(parent, postorder);
        if (permutation is not null)
        {
            var px = new double[n];
            var py = new double[n];
            for (int i = 0; i < n; i++)
            {
                px[permutation[i] - 1] = x[i];
                py[permutation[i] - 1] = y[i];
            }

            x = px;
            y = py;
        }

        _ = dialect;
        return Outputs(wanted,
            JgsMatrix.FromColumnMajorDims(x, [1, x.Length]),
            JgsMatrix.FromColumnMajorDims(y, [1, y.Length]),
            JgsValue.Number(height),
            JgsValue.Number(separator));
    }

    /// <summary>
    /// The picture <c>treeplot</c> and <c>etreeplot</c> draw: the nodes as markers and the edges as
    /// one NaN-separated polyline, which is how a whole forest becomes a single line object.
    /// </summary>
    private static void DrawTree(double[] parent, int argumentCount, string? nodeSpec, string? edgeSpec,
        int line, int col)
    {
        int n = parent.Length;
        var asInts = new int[n];
        for (int i = 0; i < n; i++)
        {
            asInts[i] = (int)parent[i];
        }

        int[]? permutation = null;
        if (asInts.Where((p, i) => p != 0 && p <= i + 1).Any())
        {
            (asInts, permutation) = SparseStructure.FixParent(asInts);
        }

        (double[] x, double[] y, int height, _) =
            SparseStructure.TreeLayout(asInts, PostorderOfTree(asInts));
        if (permutation is not null)
        {
            var px = new double[n];
            var py = new double[n];
            for (int i = 0; i < n; i++)
            {
                px[permutation[i] - 1] = x[i];
                py[permutation[i] - 1] = y[i];
            }

            x = px;
            y = py;

            // The edges must be drawn between the nodes as the caller numbered them, so the parent
            // pointers go back to the ones that came in.
            for (int i = 0; i < n; i++)
            {
                asInts[i] = (int)parent[i];
            }
        }

        var edgeX = new List<double>();
        var edgeY = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (asInts[i] == 0)
            {
                continue;
            }

            edgeX.Add(x[i]);
            edgeX.Add(x[asInts[i] - 1]);
            edgeX.Add(double.NaN);
            edgeY.Add(y[i]);
            edgeY.Add(y[asInts[i] - 1]);
            edgeY.Add(double.NaN);
        }

        // MATLAB drops the markers above five hundred nodes, where they would be a smear rather
        // than a picture, and keeps the edges.
        string nodes = argumentCount == 1 ? "ro" : nodeSpec ?? string.Empty;
        string edges = argumentCount == 1
            ? "r-"
            : edgeSpec ?? (nodeSpec is { Length: > 1 } ? nodeSpec[..^1] + "-" : "r-");
        bool drawNodes = nodes.Length > 0 && (argumentCount > 1 || n < 500);
        bool drawEdges = edges.Length > 0;

        _ = line;
        _ = col;
        if (drawNodes)
        {
            JG.Plot(x, y, nodes);
        }

        if (drawEdges)
        {
            if (drawNodes)
            {
                JG.Hold(true);
            }

            JG.Plot([.. edgeX], [.. edgeY], edges);
            if (drawNodes)
            {
                JG.Hold(false);
            }
        }

        JG.XLabel($"height = {height}");
        JG.XLim(0, 1);
        JG.YLim(0, 1);
    }

    /// <summary><c>gplot(A, xy)</c> and <c>[X, Y] = gplot(A, xy)</c>.</summary>
    private static JgsValue[] GraphPlot(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("gplot", args, 2, 3, line, col);
        CscMatrix a = SparseOperandOf("gplot", args[0], line, col);
        double[,] xy = RectOf("gplot", args[1], line, col);
        if (xy.GetLength(1) < 2)
        {
            throw new JgsRuntimeException(line, col, "gplot: the coordinates must be an n-by-2 matrix.");
        }

        // The edges come out in order of their larger endpoint, which is what makes the picture
        // reproducible: the stored order of a sparse matrix is by column, and that is not the same
        // thing when the matrix is symmetric.
        var edges = new List<(int Row, int Col)>();
        for (int c = 0; c < a.Cols; c++)
        {
            for (int i = a.ColumnStarts[c]; i < a.ColumnStarts[c + 1]; i++)
            {
                if (a.Values[i] != 0)
                {
                    edges.Add((a.RowIndices[i], c));
                }
            }
        }

        var ordered = edges.OrderBy(e => Math.Max(e.Row, e.Col)).ToList();
        var xs = new List<double>(ordered.Count * 3);
        var ys = new List<double>(ordered.Count * 3);
        foreach ((int r, int c) in ordered)
        {
            if (r >= xy.GetLength(0) || c >= xy.GetLength(0))
            {
                throw new JgsRuntimeException(line, col,
                    "gplot: the coordinate matrix needs one row per node.");
            }

            xs.Add(xy[r, 0]);
            xs.Add(xy[c, 0]);
            xs.Add(double.NaN);
            ys.Add(xy[r, 1]);
            ys.Add(xy[c, 1]);
            ys.Add(double.NaN);
        }

        if (wanted == 0)
        {
            JG.Plot([.. xs], [.. ys], args.Count == 3 && IsTextScalar(args[2]) ? TextOf(args[2]) : null);
            return [];
        }

        return Outputs(wanted,
            JgsMatrix.FromColumnMajorDims([.. xs], [xs.Count, 1]),
            JgsMatrix.FromColumnMajorDims([.. ys], [ys.Count, 1]));
    }

    /// <summary>
    /// <c>[A, xy] = unmesh(E)</c>: an edge list of endpoint coordinates turned into the mesh's
    /// Laplacian and the list of distinct vertices.
    /// </summary>
    private static JgsValue[] Unmesh(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("unmesh", args, 1, line, col);
        double[,] edges = RectOf("unmesh", args[0], line, col);
        if (edges.GetLength(1) != 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unmesh:WrongRowForm",
                "unmesh: each row must be [x1 y1 x2 y2].");
        }

        int m = edges.GetLength(0);

        // Coordinates are quantized onto a grid of eps^(-1/3) steps before they are compared, so
        // that two endpoints written to different last digits are still the same vertex.
        double range = Math.Round(Math.Pow(2.220446049250313e-16, -1.0 / 3));
        var x = new double[2 * m];
        var y = new double[2 * m];
        for (int i = 0; i < m; i++)
        {
            x[i] = edges[i, 0];
            y[i] = edges[i, 1];
            x[m + i] = edges[i, 2];
            y[m + i] = edges[i, 3];
        }

        if (m == 0)
        {
            return Outputs(wanted, JgsValue.Sparse(CscMatrix.FromTriplets(0, 0, [])),
                JgsMatrix.FromColumnMajorDims([], [0, 2]));
        }

        double xmin = x.Min();
        double ymin = y.Min();
        double xScale = (range - 1) / Math.Max(x.Max() - xmin, 1);
        double yScale = (range - 1) / Math.Max(y.Max() - ymin, 1);
        var names = new double[2 * m];
        for (int i = 0; i < 2 * m; i++)
        {
            names[i] = Math.Round((x[i] - xmin) * xScale) + 1
                + (Math.Round((y[i] - ymin) * yScale) / range);
        }

        int[] byName = [.. Enumerable.Range(0, 2 * m).OrderBy(i => names[i])];
        var vertexOf = new int[2 * m];
        var vx = new List<double>();
        var vy = new List<double>();
        int vertices = 0;
        for (int k = 0; k < byName.Length; k++)
        {
            if (k == 0 || names[byName[k]] != names[byName[k - 1]])
            {
                vertices++;
                vx.Add(x[byName[k]]);
                vy.Add(y[byName[k]]);
            }

            vertexOf[byName[k]] = vertices - 1;
        }

        var pattern = new HashSet<(int, int)>();
        for (int i = 0; i < m; i++)
        {
            int p = vertexOf[i];
            int q = vertexOf[m + i];
            if (p != q)
            {
                pattern.Add((Math.Min(p, q), Math.Max(p, q)));
            }
        }

        var degree = new int[vertices];
        var triplets = new List<(int, int, double)>();
        foreach ((int p, int q) in pattern)
        {
            triplets.Add((p, q, -1));
            triplets.Add((q, p, -1));
            degree[p]++;
            degree[q]++;
        }

        for (int i = 0; i < vertices; i++)
        {
            triplets.Add((i, i, degree[i]));
        }

        JgsValue coordinates = JgsMatrix.Build(vertices, 2, (r, c) => c == 0 ? vx[r] : vy[r]);
        return Outputs(wanted,
            JgsValue.Sparse(CscMatrix.FromTriplets(vertices, vertices, triplets)),
            coordinates);
    }

    // --- the random constructors ---------------------------------------------------------------

    /// <summary>One standard normal from the session's stream, by the polar form of Box–Muller.</summary>
    private static double Gaussian(Random random)
    {
        double u;
        double v;
        double s;
        do
        {
            u = (2 * random.NextDouble()) - 1;
            v = (2 * random.NextDouble()) - 1;
            s = (u * u) + (v * v);
        }
        while (s is <= 0 or >= 1);

        return u * Math.Sqrt(-2 * Math.Log(s) / s);
    }

    /// <summary><c>sprandn(S)</c> and <c>sprandn(m, n, density)</c>.</summary>
    private static JgsValue RandomSparse(IReadOnlyList<JgsValue> args, Random random, bool normal,
        int line, int col)
    {
        ArityRange("sprandn", args, 1, 4, line, col);
        if (args.Count == 1)
        {
            // sprandn(S) keeps the pattern and replaces every stored value, which is the form that
            // lets a script keep a matrix's structure and vary only its numbers.
            CscMatrix s = SparseOperandOf("sprandn", args[0], line, col);
            var triplets = new List<(int, int, double)>(s.NonZeroCount);
            for (int c = 0; c < s.Cols; c++)
            {
                for (int i = s.ColumnStarts[c]; i < s.ColumnStarts[c + 1]; i++)
                {
                    if (s.Values[i] != 0)
                    {
                        triplets.Add((s.RowIndices[i], c, normal ? Gaussian(random) : random.NextDouble()));
                    }
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(s.Rows, s.Cols, triplets));
        }

        if (args.Count == 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sprandn:TwoInputs",
                "sprandn takes a matrix, or (m, n, density), or (m, n, density, rc).");
        }

        int rows = Count("sprandn", args, 0, line, col);
        int cols = Count("sprandn", args, 1, line, col);
        double density = Math.Min(Num("sprandn", args, 2, line, col), 1);
        long wantedCount = (long)Math.Round((double)rows * cols * density);
        var chosen = new HashSet<long>();
        var entries = new List<(int, int, double)>();
        for (long t = 0; t < wantedCount; t++)
        {
            int r = random.Next(rows);
            int c = random.Next(cols);

            // MATLAB draws the positions with replacement and then keeps the distinct ones, so the
            // nonzero count is at most the requested one and usually a little under it.
            if (chosen.Add(((long)c * rows) + r))
            {
                entries.Add((r, c, normal ? Gaussian(random) : random.NextDouble()));
            }
        }

        CscMatrix built = CscMatrix.FromTriplets(rows, cols, entries);
        if (args.Count == 4)
        {
            built = ConditionedBy(built, Num("sprandn", args, 3, line, col), random, line, col);
        }

        return JgsValue.Sparse(built);
    }

    /// <summary><c>sprandsym(S)</c>, <c>sprandsym(n, density)</c> and the conditioned forms.</summary>
    private static JgsValue RandomSymmetric(IReadOnlyList<JgsValue> args, Random random, int line, int col)
    {
        ArityRange("sprandsym", args, 1, 4, line, col);
        if (args.Count == 1)
        {
            CscMatrix s = SparseOperandOf("sprandsym", args[0], line, col);
            var triplets = new List<(int, int, double)>(s.NonZeroCount);
            for (int c = 0; c < s.Cols; c++)
            {
                for (int i = s.ColumnStarts[c]; i < s.ColumnStarts[c + 1]; i++)
                {
                    int r = s.RowIndices[i];
                    if (s.Values[i] == 0 || r < c)
                    {
                        continue;
                    }

                    double value = Gaussian(random);
                    triplets.Add((r, c, value));
                    if (r != c)
                    {
                        triplets.Add((c, r, value));
                    }
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(s.Rows, s.Cols, triplets));
        }

        int n = Count("sprandsym", args, 0, line, col);
        double density = Math.Min(Num("sprandsym", args, 1, line, col), 1);
        if (args.Count == 2)
        {
            long wantedCount = (long)Math.Round(n * (n + 1) / 2.0 * density);
            var entries = new List<(int, int, double)>();
            var chosen = new HashSet<long>();
            for (long t = 0; t < wantedCount; t++)
            {
                int r = random.Next(n);
                int c = random.Next(n);
                if (!chosen.Add(((long)Math.Max(r, c) * n) + Math.Min(r, c)))
                {
                    continue;
                }

                double value = Gaussian(random);
                entries.Add((r, c, value));
                if (r != c)
                {
                    entries.Add((c, r, value));
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(n, n, entries));
        }

        // The conditioned forms start from a diagonal whose entries are a geometric sequence with
        // the requested reciprocal condition number, and fill it in with random Jacobi rotations,
        // each of which preserves the whole spectrum.
        double rc = Num("sprandsym", args, 2, line, col);
        if (rc is <= 0 or > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sprandsym:invalidRC",
                "sprandsym: rc must be greater than 0 and at most 1.");
        }

        int kind = args.Count == 4 ? Count("sprandsym", args, 3, line, col) : 0;
        if (args.Count == 4 && kind is not (1 or 2 or 3))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sprandsym:invalidKind",
                "sprandsym: kind must be 1, 2, or 3.");
        }

        double ratio = (args.Count == 4 ? 1 : -1) * Math.Pow(rc, 1.0 / (n - 1));
        var diagonal = new List<(int, int, double)>(n);
        for (int i = 0; i < n; i++)
        {
            diagonal.Add((i, i, Math.Pow(ratio, i)));
        }

        CscMatrix result = CscMatrix.FromTriplets(n, n, diagonal);
        long target = (long)Math.Round(density * n * n);
        int guard = 0;
        while (result.NonZeroCount < 0.95 * target && guard++ < 100 * n)
        {
            result = JacobiRotation(result, random, both: false);
        }

        return JgsValue.Sparse(result);
    }

    /// <summary>
    /// The two-sided rotations that fill a diagonal matrix in without moving its singular values —
    /// what <c>sprandn</c>'s four-argument form uses to reach a requested condition number.
    /// </summary>
    private static CscMatrix ConditionedBy(CscMatrix seed, double rc, Random random, int line, int col)
    {
        if (rc is < 0 or > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sprandn:RcondTooHigh",
                "sprandn: rc must be between 0 and 1.");
        }

        int order = Math.Min(seed.Rows, seed.Cols);
        var diagonal = new List<(int, int, double)>(order);
        double ratio = order > 1 ? Math.Pow(rc, 1.0 / (order - 1)) : 1;
        for (int i = 0; i < order; i++)
        {
            diagonal.Add((i, i, Math.Pow(ratio, i)));
        }

        CscMatrix result = CscMatrix.FromTriplets(seed.Rows, seed.Cols, diagonal);
        long target = seed.NonZeroCount;
        int guard = 0;
        while (result.NonZeroCount < 0.95 * target && guard++ < 100 * Math.Max(order, 1))
        {
            result = JacobiRotation(result, random, both: true);
        }

        return result;
    }

    /// <summary>
    /// One random plane rotation applied to the rows, and — when <paramref name="both"/> — a second
    /// one applied to the columns. A single rotation on both sides preserves eigenvalues and
    /// symmetry; two different ones preserve only the singular values, which is what a rectangular
    /// matrix has to settle for.
    /// </summary>
    private static CscMatrix JacobiRotation(CscMatrix a, Random random, bool both)
    {
        int m = a.Rows;
        int n = a.Cols;
        if ((long)m * n <= 1)
        {
            return a;
        }

        double theta = ((2 * random.NextDouble()) - 1) * Math.PI;
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        int i = random.Next(m);
        int j = i;
        while (j == i)
        {
            j = random.Next(m);
        }

        double[] dense = a.ToColumnMajor();
        for (int k = 0; k < n; k++)
        {
            double top = dense[(k * m) + i];
            double bottom = dense[(k * m) + j];
            dense[(k * m) + i] = (c * top) + (s * bottom);
            dense[(k * m) + j] = (-s * top) + (c * bottom);
        }

        if (both)
        {
            theta = ((2 * random.NextDouble()) - 1) * Math.PI;
            c = Math.Cos(theta);
            s = Math.Sin(theta);
            i = random.Next(n);
            j = i;
            while (j == i)
            {
                j = random.Next(n);
            }
        }

        for (int k = 0; k < m; k++)
        {
            double leftValue = dense[(i * m) + k];
            double rightValue = dense[(j * m) + k];
            dense[(i * m) + k] = (c * leftValue) + (s * rightValue);
            dense[(j * m) + k] = (-s * leftValue) + (c * rightValue);
        }

        return CscMatrix.FromColumnMajor(dense, m, n);
    }
}
