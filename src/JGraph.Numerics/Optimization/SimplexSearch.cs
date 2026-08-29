namespace JGraph.Numerics.Optimization;

/// <summary>
/// The Nelder-Mead simplex search as MATLAB's <c>fminsearch</c> runs it: a derivative-free local
/// minimizer that keeps n+1 points and moves the worst one through the face the others span.
/// </summary>
/// <remarks>
/// <para>
/// The method is Nelder and Mead's, with the reflection, expansion, contraction and shrink
/// coefficients (1, 2, one half, one half) and the convergence test of Lagarias, Reeds, Wright and
/// Wright, <em>Convergence Properties of the Nelder-Mead Simplex Method in Low Dimensions</em>,
/// SIAM J. Optim. 9(1) 112-147, 1998 - the paper <c>fminsearch</c> itself cites.
/// </para>
/// <para>
/// <c>JGraph.Statistics.Optimize.NelderMead</c> is the same method and is deliberately not reused.
/// That one serves the distribution fitters and differs from MATLAB in six ways that are invisible
/// to a fitter and visible to a script: it folds NaN to positive infinity, its tolerances are
/// relative where MATLAB's are absolute, it tests convergence before the first iteration, it accepts
/// an outside contraction on a strict inequality, it re-orders by insertion rather than re-sorting
/// the whole simplex, and its counting convention differs. <c>output.iterations</c> and
/// <c>output.funcCount</c> are answers a script reads, so moving that class would move three fitters
/// to fix one caller.
/// </para>
/// </remarks>
public static class SimplexSearch
{
    /// <summary>The fraction a non-zero starting coordinate is stretched by to make its vertex.</summary>
    private const double UsualDelta = 0.05;

    /// <summary>The displacement used instead for a starting coordinate that is exactly zero.</summary>
    private const double ZeroTermDelta = 0.00025;

    /// <summary>The step name the search reports once the initial simplex is built.</summary>
    private const string InitialSimplex = "initial simplex";

    /// <summary>The step name that means every vertex was pulled towards the best one.</summary>
    private const string Shrink = "shrink";

    /// <summary>How the search is allowed to run and when it should stop.</summary>
    /// <param name="MaxIterations">Iterations allowed, or zero for 200 per free parameter.</param>
    /// <param name="MaxFunctionEvaluations">Evaluations allowed, or zero for 200 per free parameter.</param>
    /// <param name="ToleranceX">How close every vertex must be to the best one; zero for 1e-4.</param>
    /// <param name="ToleranceFunction">How close every value must be to the best; zero for 1e-4.</param>
    public readonly record struct Settings(
        int MaxIterations = 0,
        int MaxFunctionEvaluations = 0,
        double ToleranceX = 0,
        double ToleranceFunction = 0);

    /// <summary>What the search found and why it stopped.</summary>
    /// <param name="Solution">The best point seen.</param>
    /// <param name="Value">The objective there.</param>
    /// <param name="ExitFlag">One of <see cref="SearchExit"/>.</param>
    /// <param name="Iterations">Iterations taken.</param>
    /// <param name="FunctionCount">Objective evaluations spent.</param>
    public readonly record struct Result(
        double[] Solution, double Value, int ExitFlag, int Iterations, int FunctionCount);

    /// <summary>Minimizes <paramref name="objective"/> from <paramref name="start"/>.</summary>
    /// <param name="objective">
    /// The function to minimize, over a flat copy of the starting point. It may answer NaN, which is
    /// carried rather than folded: every comparison against a NaN is false, so a NaN vertex sinks to
    /// the end of the simplex and is the next one reflected away, which is what MATLAB does.
    /// </param>
    /// <param name="start">The starting point; its length is the number of free parameters.</param>
    /// <param name="settings">Budget and tolerances.</param>
    /// <param name="watcher">Optional; asked before the first step and after every iteration.</param>
    public static Result Minimize(
        Func<double[], double> objective,
        double[] start,
        Settings settings = default,
        SearchWatcher? watcher = null)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(start);
        if (start.Length == 0)
        {
            throw new ArgumentException("The starting point needs at least one coordinate.", nameof(start));
        }

        int n = start.Length;
        int np1 = n + 1;
        int maxIterations = settings.MaxIterations > 0 ? settings.MaxIterations : 200 * n;
        int maxEvaluations = settings.MaxFunctionEvaluations > 0 ? settings.MaxFunctionEvaluations : 200 * n;
        double toleranceX = settings.ToleranceX > 0 ? settings.ToleranceX : 1e-4;
        double toleranceF = settings.ToleranceFunction > 0 ? settings.ToleranceFunction : 1e-4;

        int evaluations = 0;
        double Evaluate(double[] point)
        {
            evaluations++;
            return objective(point);
        }

        var simplex = new double[np1][];
        var values = new double[np1];
        simplex[0] = (double[])start.Clone();
        values[0] = Evaluate(simplex[0]);
        int iteration = 0;
        string how = string.Empty;

        // MATLAB reports twice before the simplex is built: once to open the watcher and once as the
        // zeroth iteration. A stop from either is honoured.
        if (Report(watcher, SearchPhase.Init, iteration, evaluations, values[0], how, simplex[0])
            is { } atInit)
        {
            return atInit;
        }

        if (Report(watcher, SearchPhase.Iterate, iteration, evaluations, values[0], how, simplex[0])
            is { } atZeroth)
        {
            return atZeroth;
        }

        // One vertex per coordinate, that coordinate alone displaced. Stretching zero moves nothing,
        // so a zero coordinate takes a small fixed step instead.
        for (int j = 0; j < n; j++)
        {
            double[] vertex = (double[])start.Clone();
            vertex[j] = vertex[j] != 0 ? (1 + UsualDelta) * vertex[j] : ZeroTermDelta;
            simplex[j + 1] = vertex;
            values[j + 1] = Evaluate(vertex);
        }

        SortSimplex(simplex, values);
        how = InitialSimplex;
        iteration = 1;

        if (Report(watcher, SearchPhase.Iterate, iteration, evaluations, values[0], how, simplex[0])
            is { } afterSimplex)
        {
            return afterSimplex;
        }

        var centroid = new double[n];
        while (evaluations < maxEvaluations && iteration < maxIterations)
        {
            if (IsSmallEnough(simplex, values, toleranceX, toleranceF))
            {
                break;
            }

            // The centroid of the n best vertices: the face the worst one is pushed through.
            Array.Clear(centroid);
            for (int v = 0; v < n; v++)
            {
                for (int i = 0; i < n; i++)
                {
                    centroid[i] += simplex[v][i];
                }
            }

            for (int i = 0; i < n; i++)
            {
                centroid[i] /= n;
            }

            double[] worst = simplex[n];
            double[] reflected = Along(centroid, worst, 1.0);
            double reflectedValue = Evaluate(reflected);

            if (reflectedValue < values[0])
            {
                // Better than anything seen: try twice as far in the same direction.
                double[] expanded = Along(centroid, worst, 2.0);
                double expandedValue = Evaluate(expanded);
                if (expandedValue < reflectedValue)
                {
                    simplex[n] = expanded;
                    values[n] = expandedValue;
                    how = "expand";
                }
                else
                {
                    simplex[n] = reflected;
                    values[n] = reflectedValue;
                    how = "reflect";
                }
            }
            else if (reflectedValue < values[n - 1])
            {
                // Better than the second worst: an ordinary reflection.
                simplex[n] = reflected;
                values[n] = reflectedValue;
                how = "reflect";
            }
            else if (reflectedValue < values[n])
            {
                // The reflection improved on the worst but not enough: contract towards it. The
                // acceptance test here is non-strict where the inside contraction's is strict, and
                // the asymmetry is MATLAB's rather than an oversight.
                double[] contracted = Along(centroid, worst, 0.5);
                double contractedValue = Evaluate(contracted);
                if (contractedValue <= reflectedValue)
                {
                    simplex[n] = contracted;
                    values[n] = contractedValue;
                    how = "contract outside";
                }
                else
                {
                    how = Shrink;
                }
            }
            else
            {
                // The reflection was no better than the worst: contract towards the worst instead.
                double[] contracted = Along(centroid, worst, -0.5);
                double contractedValue = Evaluate(contracted);
                if (contractedValue < values[n])
                {
                    simplex[n] = contracted;
                    values[n] = contractedValue;
                    how = "contract inside";
                }
                else
                {
                    how = Shrink;
                }
            }

            if (how == Shrink)
            {
                double[] best = simplex[0];
                for (int v = 1; v <= n; v++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        simplex[v][i] = best[i] + (0.5 * (simplex[v][i] - best[i]));
                    }

                    values[v] = Evaluate(simplex[v]);
                }
            }

            SortSimplex(simplex, values);
            iteration++;

            if (Report(watcher, SearchPhase.Iterate, iteration, evaluations, values[0], how, simplex[0])
                is { } midway)
            {
                return midway;
            }
        }

        watcher?.Invoke(new SearchStep(
            SearchPhase.Done, iteration, evaluations, values[0], how, simplex[0]));

        int exit = evaluations >= maxEvaluations || iteration >= maxIterations
            ? SearchExit.BudgetExhausted
            : SearchExit.Converged;
        return new Result(simplex[0], values[0], exit, iteration, evaluations);
    }

    /// <summary>
    /// Hands a step to <paramref name="watcher"/> and, when it asks to stop, the result the search
    /// should give back: the best point at that report, which is what the watcher was just shown.
    /// </summary>
    private static Result? Report(
        SearchWatcher? watcher, SearchPhase phase, int iteration, int evaluations,
        double value, string how, double[] point)
    {
        if (watcher is null || !watcher(new SearchStep(phase, iteration, evaluations, value, how, point)))
        {
            return null;
        }

        return new Result(point, value, SearchExit.StoppedByWatcher, iteration, evaluations);
    }

    /// <summary>
    /// The point <paramref name="factor"/> of the way from <paramref name="worst"/> past
    /// <paramref name="centroid"/>: 1 reflects, 2 expands, one half contracts outwards and minus one
    /// half contracts inwards.
    /// </summary>
    private static double[] Along(double[] centroid, double[] worst, double factor)
    {
        var point = new double[centroid.Length];
        for (int i = 0; i < point.Length; i++)
        {
            point[i] = centroid[i] + (factor * (centroid[i] - worst[i]));
        }

        return point;
    }

    /// <summary>
    /// Orders the vertices so index 0 is the best and index n the worst, stably and with any NaN
    /// last.
    /// </summary>
    /// <remarks>
    /// Both properties are load-bearing rather than incidental. MATLAB re-sorts the whole simplex
    /// each pass with a stable sort, so vertices of equal value keep the order they arrived in and
    /// the one at the end, the one about to be reflected away, is a determined choice rather than
    /// whichever the sort happened to leave there. And MATLAB sorts NaN to the end, which is how a
    /// point outside the objective's domain gets discarded instead of mistaken for a minimum.
    /// </remarks>
    private static void SortSimplex(double[][] simplex, double[] values)
    {
        for (int i = 1; i < values.Length; i++)
        {
            double value = values[i];
            double[] vertex = simplex[i];
            int j = i - 1;
            while (j >= 0 && Precedes(value, values[j]))
            {
                values[j + 1] = values[j];
                simplex[j + 1] = simplex[j];
                j--;
            }

            values[j + 1] = value;
            simplex[j + 1] = vertex;
        }
    }

    /// <summary>Whether <paramref name="candidate"/> sorts strictly before <paramref name="held"/>.</summary>
    private static bool Precedes(double candidate, double held) =>
        !double.IsNaN(candidate) && (double.IsNaN(held) || candidate < held);

    /// <summary>
    /// Whether every vertex is within the tolerance of the best one and every value within the
    /// tolerance of the best value. Both tests must pass: either alone accepts a simplex that has
    /// collapsed along one direction while still sliding down a valley.
    /// </summary>
    /// <remarks>
    /// The tolerances are absolute, floored at ten ulps of the quantity being compared, which is
    /// MATLAB's <c>max(TolFun, 10*eps(fv(1)))</c> and <c>max(TolX, 10*eps(max(v(:,1))))</c>. The
    /// floor is what stops a search whose values are of order 1e20 from asking for a precision no
    /// double can express, and it is measured from the best vertex's largest <em>signed</em>
    /// coordinate rather than its largest magnitude, because that is the quantity MATLAB takes the
    /// ulp of.
    /// </remarks>
    private static bool IsSmallEnough(
        double[][] simplex, double[] values, double toleranceX, double toleranceF)
    {
        int n = simplex[0].Length;
        double[] best = simplex[0];

        double largest = best[0];
        for (int i = 1; i < n; i++)
        {
            if (best[i] > largest)
            {
                largest = best[i];
            }
        }

        double allowedValue = Math.Max(toleranceF, 10 * Ulp(values[0]));
        double allowedPoint = Math.Max(toleranceX, 10 * Ulp(largest));

        for (int v = 1; v <= n; v++)
        {
            // Written as a negated comparison so that a NaN difference fails the test rather than
            // passing it: a simplex holding a NaN has not converged.
            if (!(Math.Abs(values[0] - values[v]) <= allowedValue))
            {
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (!(Math.Abs(simplex[v][i] - best[i]) <= allowedPoint))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The spacing between <paramref name="x"/> and the next larger double in magnitude, which is
    /// MATLAB's <c>eps(x)</c> and so answers the same for x and minus x.
    /// </summary>
    private static double Ulp(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        double magnitude = Math.Abs(x);
        if (double.IsPositiveInfinity(magnitude))
        {
            return double.PositiveInfinity;
        }

        // eps(0) is the smallest denormal rather than the spacing at 1: the distance from zero to
        // the next representable number.
        return magnitude == 0 ? double.Epsilon : Math.BitIncrement(magnitude) - magnitude;
    }
}
