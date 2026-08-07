using JGraph.Statistics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave B, the correlation half: <c>corr</c> in its three senses, the partial correlations that
/// hold other variables fixed, and the two whole-matrix operations — reading a correlation matrix out
/// of a covariance one, and repairing a matrix that is nearly but not quite a correlation matrix.
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec CorrOptions = new(
        "corr",
        Flags: [],
        Names: ["type", "rows", "tail"]);

    private static readonly OptionSpec NearCorrOptions = new(
        "nearcorr",
        Flags: [],
        Names: ["Tolerance", "MaxIterations", "Method", "Weights"]);

    private static void RegisterCorrelationBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name, (args, line, col) => both(args, 1, line, col)[0])
            { MultiOutput = both }));

        DefineBoth("corr", Correlate);
        DefineBoth("partialcorr", PartialCorrelate);
        DefineBoth("partialcorri", InternalPartialCorrelate);
        DefineBoth("corrcov", CorrelationFromCovariance);
        env.Declare("nearcorr", JgsValue.Function(new BuiltinFunction("nearcorr", NearestCorrelation)));
    }

    /// <summary>
    /// <c>[RHO, PVAL] = corr(X)</c> or <c>corr(X, Y)</c>: the correlation between every pair of
    /// columns, in whichever sense <c>'type'</c> names, with the chance of seeing one that extreme
    /// from unrelated data.
    /// </summary>
    private static JgsValue[] Correlate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "corr needs some data.");
        }

        ParsedArgs parsed = CorrOptions.Parse(args, 2, line, col);
        (Correlation.Kind kind, Correlation.Tail tail, string rows) = CorrelationWords("corr", parsed);

        double[][] left = VariableColumns("corr", parsed.Positional[0], line, col);
        double[][] right = parsed.Positional.Count > 1
            ? VariableColumns("corr", parsed.Positional[1], line, col)
            : left;

        if (parsed.Positional.Count > 1 && left[0].Length != right[0].Length)
        {
            throw new JgsRuntimeException(line, col,
                $"corr: the two sets must have the same number of observations, but got {left[0].Length} and {right[0].Length}.");
        }

        if (rows == "complete")
        {
            (left, right) = WithoutIncompleteRows(left, right, parsed.Positional.Count > 1);
        }

        int n = left.Length;
        int m = right.Length;
        var coefficients = new double[n * m];
        var probabilities = new double[n * m];
        for (int c = 0; c < m; c++)
        {
            for (int r = 0; r < n; r++)
            {
                (double[] a, double[] b) = rows == "pairwise"
                    ? BothPresent(left[r], right[c])
                    : (left[r], right[c]);
                (double coefficient, double p) = Correlation.Between(a, b, kind, tail);
                coefficients[r + (c * n)] = coefficient;
                probabilities[r + (c * n)] = p;
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(coefficients, n, m),
            JgsMatrix.FromColumnMajor(probabilities, n, m));
    }

    /// <summary>
    /// <c>[RHO, PVAL] = partialcorr(X)</c>, <c>partialcorr(X, Z)</c> or <c>partialcorr(X, Y, Z)</c>:
    /// the correlation left between two variables once the effect of the controlling variables has
    /// been regressed out of both.
    /// </summary>
    private static JgsValue[] PartialCorrelate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "partialcorr needs some data.");
        }

        ParsedArgs parsed = PartialOptions("partialcorr").Parse(args, 3, line, col);
        (Correlation.Kind kind, Correlation.Tail tail, string rows) = CorrelationWords("partialcorr", parsed);

        double[][] left;
        double[][] right;
        double[][] controls;
        switch (parsed.Positional.Count)
        {
            case 1:
                // partialcorr(X): every pair of columns, controlling for all the other columns.
                left = VariableColumns("partialcorr", parsed.Positional[0], line, col);
                right = left;
                controls = [];
                break;

            case 2:
                left = VariableColumns("partialcorr", parsed.Positional[0], line, col);
                right = left;
                controls = VariableColumns("partialcorr", parsed.Positional[1], line, col);
                break;

            default:
                left = VariableColumns("partialcorr", parsed.Positional[0], line, col);
                right = VariableColumns("partialcorr", parsed.Positional[1], line, col);
                controls = VariableColumns("partialcorr", parsed.Positional[2], line, col);
                break;
        }

        bool square = parsed.Positional.Count < 3;
        if (rows == "complete")
        {
            (left, right, controls) = WithoutIncompleteRows("partialcorr", left, right, controls, square, line, col);
        }

        int n = left.Length;
        int m = right.Length;
        var coefficients = new double[n * m];
        var probabilities = new double[n * m];

        for (int c = 0; c < m; c++)
        {
            for (int r = 0; r < n; r++)
            {
                // Controlling for "the rest" means every other column of X when only X was given;
                // otherwise it is exactly the controlling set the caller named.
                var held = new List<double[]>(controls);
                if (parsed.Positional.Count == 1)
                {
                    for (int other = 0; other < n; other++)
                    {
                        if (other != r && other != c)
                        {
                            held.Add(left[other]);
                        }
                    }
                }

                (coefficients[r + (c * n)], probabilities[r + (c * n)]) =
                    square && r == c
                        ? (1, 0)
                        : PartialBetween(left[r], right[c], held, kind, tail);
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(coefficients, n, m),
            JgsMatrix.FromColumnMajor(probabilities, n, m));
    }

    /// <summary>
    /// <c>[RHO, PVAL] = partialcorri(Y, X)</c>: how much of each column of Y each column of X explains
    /// once the other columns of X — and any further controlling variables — have been taken out of
    /// both. One row per column of Y, one column per column of X.
    /// </summary>
    private static JgsValue[] InternalPartialCorrelate(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "partialcorri needs a response and some predictors.");
        }

        ParsedArgs parsed = PartialOptions("partialcorri").Parse(args, 3, line, col);
        (Correlation.Kind kind, Correlation.Tail tail, string rows) = CorrelationWords("partialcorri", parsed);

        double[][] responses = VariableColumns("partialcorri", parsed.Positional[0], line, col);
        double[][] predictors = VariableColumns("partialcorri", parsed.Positional[1], line, col);
        double[][] controls = parsed.Positional.Count > 2
            ? VariableColumns("partialcorri", parsed.Positional[2], line, col)
            : [];

        if (rows == "complete")
        {
            (responses, predictors, controls) =
                WithoutIncompleteRows("partialcorri", responses, predictors, controls, square: false, line, col);
        }

        int n = responses.Length;
        int m = predictors.Length;
        var coefficients = new double[n * m];
        var probabilities = new double[n * m];
        for (int c = 0; c < m; c++)
        {
            for (int r = 0; r < n; r++)
            {
                var held = new List<double[]>(controls);
                for (int other = 0; other < m; other++)
                {
                    if (other != c)
                    {
                        held.Add(predictors[other]);
                    }
                }

                (coefficients[r + (c * n)], probabilities[r + (c * n)]) =
                    PartialBetween(responses[r], predictors[c], held, kind, tail);
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(coefficients, n, m),
            JgsMatrix.FromColumnMajor(probabilities, n, m));
    }

    /// <summary>
    /// <c>[R, sigma] = corrcov(C)</c>: the correlation matrix a covariance matrix implies, and the
    /// standard deviations that scale between the two.
    /// </summary>
    private static JgsValue[] CorrelationFromCovariance(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("corrcov", args, 1, line, col);
        (double[] covariance, int n) = SquareMatrix("corrcov", args[0], line, col);
        for (int i = 0; i < n; i++)
        {
            if (covariance[i + (i * n)] < 0)
            {
                throw new JgsRuntimeException(line, col,
                    "corrcov: a covariance matrix has no negative variance on its diagonal.");
            }
        }

        (double[] correlations, double[] deviations) = Correlation.FromCovariance(covariance, n);
        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(correlations, n, n),
            JgsMatrix.FromColumnMajor(deviations, n, 1));
    }

    /// <summary>
    /// <c>nearcorr(A)</c>: the correlation matrix closest to a symmetric matrix that is not quite one —
    /// what pairwise-estimated correlations usually need before anything can be factorized out of them.
    /// </summary>
    private static JgsValue NearestCorrelation(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "nearcorr needs a matrix.");
        }

        ParsedArgs parsed = NearCorrOptions.Parse(args, 1, line, col);
        (double[] matrix, int n) = SquareMatrix("nearcorr", parsed.Positional[0], line, col);

        double tolerance = parsed.Scalar("Tolerance", 1e-10);
        int maxIterations = parsed.Whole("MaxIterations", 200);

        // MATLAB's default method is Newton's; the alternating projections here reach the same nearest
        // matrix by a different path, so both spellings are accepted and the difference is recorded as
        // a divergence rather than a refusal.
        parsed.Word("Method", "newton", "newton", "projection");
        if (parsed.Named("Weights") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "nearcorr: 'Weights' asks for the nearest matrix under a weighted norm, which this "
                + "implementation does not do — it minimizes the plain Frobenius distance.");
        }

        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                if (Math.Abs(matrix[r + (c * n)] - matrix[c + (r * n)]) > 1e-12)
                {
                    throw new JgsRuntimeException(line, col, "nearcorr: the matrix must be symmetric.");
                }
            }
        }

        double[] repaired = Correlation.NearestCorrelation(matrix, n, tolerance, maxIterations);
        return JgsMatrix.FromColumnMajor(repaired, n, n);
    }

    // --- Shared reading ---------------------------------------------------------------------------

    private static OptionSpec PartialOptions(string name) =>
        new(name, Flags: [], Names: ["type", "rows", "tail"]);

    /// <summary>The three option words every correlation shares, read once.</summary>
    private static (Correlation.Kind Kind, Correlation.Tail Tail, string Rows) CorrelationWords(
        string name, ParsedArgs parsed)
    {
        _ = name;
        Correlation.Kind kind = parsed.Word("type", "Pearson", "Pearson", "Kendall", "Spearman") switch
        {
            "Kendall" => Correlation.Kind.Kendall,
            "Spearman" => Correlation.Kind.Spearman,
            _ => Correlation.Kind.Pearson,
        };

        Correlation.Tail tail = parsed.Word("tail", "both", "both", "right", "left") switch
        {
            "right" => Correlation.Tail.Right,
            "left" => Correlation.Tail.Left,
            _ => Correlation.Tail.Both,
        };

        return (kind, tail, parsed.Word("rows", "all", "all", "complete", "pairwise"));
    }

    /// <summary>
    /// The correlation between two variables once the controlling ones have been regressed out of
    /// both. Rank correlations rank first and residualize the ranks, which is what makes the answer a
    /// partial Spearman rather than a Spearman of two residuals that were never monotone.
    /// </summary>
    private static (double Coefficient, double PValue) PartialBetween(
        double[] left,
        double[] right,
        List<double[]> controls,
        Correlation.Kind kind,
        Correlation.Tail tail)
    {
        double[] a = left;
        double[] b = right;
        var held = new List<double[]>(controls);

        if (kind == Correlation.Kind.Spearman)
        {
            a = Ranked(a);
            b = Ranked(b);
            for (int i = 0; i < held.Count; i++)
            {
                held[i] = Ranked(held[i]);
            }
        }

        if (held.Count > 0)
        {
            a = ResidualsAfter(a, held);
            b = ResidualsAfter(b, held);
        }

        int n = Math.Min(a.Length, b.Length);
        if (kind == Correlation.Kind.Kendall)
        {
            return Correlation.Between(a, b, Correlation.Kind.Kendall, tail);
        }

        // Every controlling variable costs a degree of freedom, on top of the two the correlation
        // itself spends.
        return Correlation.PearsonWithP(Correlation.Pearson(a, b), n, 2 + held.Count, tail);
    }

    private static double[] Ranked(double[] values) =>
        DescriptiveStatistics.TiedRanks(values, DescriptiveStatistics.TieAdjustment.RankSumOfCubes).Ranks;

    /// <summary>
    /// What is left of a variable once the controlling variables (and a constant) have explained all
    /// they can — the least-squares residuals, found from the normal equations, which stay small
    /// because a controlling set is a handful of columns rather than a whole design.
    /// </summary>
    private static double[] ResidualsAfter(double[] response, List<double[]> controls)
    {
        int n = response.Length;
        int k = controls.Count + 1;

        // The normal equations XᵀX b = Xᵀy, with the constant column written in rather than stored.
        var xtx = new double[k * k];
        var xty = new double[k];
        for (int i = 0; i < n; i++)
        {
            for (int p = 0; p < k; p++)
            {
                double xp = p == 0 ? 1 : controls[p - 1][i];
                xty[p] += xp * response[i];
                for (int q = 0; q < k; q++)
                {
                    double xq = q == 0 ? 1 : controls[q - 1][i];
                    xtx[p + (q * k)] += xp * xq;
                }
            }
        }

        double[] beta = SolveSmall(xtx, xty, k);
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            double fitted = beta[0];
            for (int p = 1; p < k; p++)
            {
                fitted += beta[p] * controls[p - 1][i];
            }

            residuals[i] = response[i] - fitted;
        }

        return residuals;
    }

    /// <summary>Gaussian elimination with partial pivoting on a small square system.</summary>
    private static double[] SolveSmall(double[] matrix, double[] rightHandSide, int n)
    {
        var a = (double[])matrix.Clone();
        var b = (double[])rightHandSide.Clone();

        for (int step = 0; step < n; step++)
        {
            int pivot = step;
            for (int r = step + 1; r < n; r++)
            {
                if (Math.Abs(a[r + (step * n)]) > Math.Abs(a[pivot + (step * n)]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(a[pivot + (step * n)]) < 1e-12)
            {
                // A controlling set that is collinear explains nothing more; leaving the coefficient
                // at zero is what keeps the residuals defined rather than infinite.
                continue;
            }

            if (pivot != step)
            {
                for (int c = 0; c < n; c++)
                {
                    (a[step + (c * n)], a[pivot + (c * n)]) = (a[pivot + (c * n)], a[step + (c * n)]);
                }

                (b[step], b[pivot]) = (b[pivot], b[step]);
            }

            for (int r = step + 1; r < n; r++)
            {
                double factor = a[r + (step * n)] / a[step + (step * n)];
                if (factor == 0)
                {
                    continue;
                }

                for (int c = step; c < n; c++)
                {
                    a[r + (c * n)] -= factor * a[step + (c * n)];
                }

                b[r] -= factor * b[step];
            }
        }

        var solution = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double total = b[r];
            for (int c = r + 1; c < n; c++)
            {
                total -= a[r + (c * n)] * solution[c];
            }

            solution[r] = Math.Abs(a[r + (r * n)]) < 1e-12 ? 0 : total / a[r + (r * n)];
        }

        return solution;
    }

    /// <summary>The columns of a matrix, or the one column a vector is however it was written.</summary>
    private static double[][] VariableColumns(string name, JgsValue value, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a vector or a matrix, not an array with more than two dimensions.");
        }

        double[] flat = FlattenColumnMajor(name, value, line, col);
        int rows = value.Type == JgsType.Array ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        if (rows == 1 || columns == 1)
        {
            return [flat];
        }

        var variables = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            variables[c] = new double[rows];
            Array.Copy(flat, c * rows, variables[c], 0, rows);
        }

        return variables;
    }

    /// <summary>A square numeric matrix, read column-major, with its order.</summary>
    private static (double[] ColumnMajor, int Order) SquareMatrix(
        string name, JgsValue value, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        double[] flat = FlattenColumnMajor(name, value, line, col);
        int rows = value.Type == JgsType.Array ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        if (dims.Length > 2 || rows != columns || rows == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: expected a square matrix.");
        }

        return (flat, rows);
    }

    /// <summary>Every observation dropped where any variable in either set is missing.</summary>
    private static (double[][] Left, double[][] Right) WithoutIncompleteRows(
        double[][] left, double[][] right, bool separate)
    {
        var all = new List<double[]>(left);
        if (separate)
        {
            all.AddRange(right);
        }

        int[] keep = CompleteRows(all);
        return (Gather(left, keep), separate ? Gather(right, keep) : Gather(left, keep));
    }

    private static (double[][] Left, double[][] Right, double[][] Controls) WithoutIncompleteRows(
        string name,
        double[][] left,
        double[][] right,
        double[][] controls,
        bool square,
        int line,
        int col)
    {
        var all = new List<double[]>(left);
        if (!square)
        {
            all.AddRange(right);
        }

        all.AddRange(controls);
        foreach (double[] variable in all)
        {
            if (variable.Length != all[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: every variable must have the same number of observations.");
            }
        }

        int[] keep = CompleteRows(all);
        double[][] keptLeft = Gather(left, keep);
        return (keptLeft, square ? keptLeft : Gather(right, keep), Gather(controls, keep));
    }

    private static int[] CompleteRows(List<double[]> variables)
    {
        int rows = variables.Count == 0 ? 0 : variables[0].Length;
        var keep = new List<int>(rows);
        for (int r = 0; r < rows; r++)
        {
            bool complete = true;
            foreach (double[] variable in variables)
            {
                if (r >= variable.Length || double.IsNaN(variable[r]))
                {
                    complete = false;
                    break;
                }
            }

            if (complete)
            {
                keep.Add(r);
            }
        }

        return [.. keep];
    }

    private static double[][] Gather(double[][] variables, int[] keep)
    {
        var kept = new double[variables.Length][];
        for (int v = 0; v < variables.Length; v++)
        {
            kept[v] = new double[keep.Length];
            for (int i = 0; i < keep.Length; i++)
            {
                kept[v][i] = variables[v][keep[i]];
            }
        }

        return kept;
    }
}
