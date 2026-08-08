namespace JGraph.Statistics.Optimize;

/// <summary>
/// The Nelder–Mead simplex search, the minimizer behind every distribution fit that has no closed
/// form. It needs only the objective's value, never its derivative, which is what makes it usable
/// against a censored log-likelihood whose gradient nobody has written down.
/// </summary>
/// <remarks>
/// <para>
/// The construction is MATLAB's <c>fminsearch</c>: the initial simplex is the starting point plus one
/// vertex per coordinate, that coordinate stretched by five percent (or displaced by a small constant
/// where it is zero, since stretching zero moves nothing), and the standard reflection, expansion,
/// contraction and shrink coefficients of 1, 2, ½ and ½.
/// </para>
/// <para>
/// It is a local search on a shape that can stall, so the fitters that use it supply a starting point
/// from a moment estimate rather than a guess, and check the answer they get back. Convergence is
/// declared only when the simplex is small in <em>both</em> senses — every vertex close to the best
/// one and every value close to the best value — because either test alone accepts a simplex that has
/// collapsed along one direction while still sliding down a valley.
/// </para>
/// </remarks>
public static class NelderMead
{
    /// <summary>How the search was allowed to run and when it should stop.</summary>
    /// <param name="MaxIterations">
    /// Iterations allowed, or zero for the default of two hundred per free parameter.
    /// </param>
    /// <param name="MaxEvaluations">
    /// Objective evaluations allowed, or zero for the default of two hundred per free parameter.
    /// </param>
    /// <param name="ToleranceX">How close every vertex must be to the best one.</param>
    /// <param name="ToleranceFunction">How close every value must be to the best value.</param>
    public readonly record struct Settings(
        int MaxIterations = 0,
        int MaxEvaluations = 0,
        double ToleranceX = 1e-8,
        double ToleranceFunction = 1e-8);

    /// <summary>What the search found and whether it got there.</summary>
    /// <param name="Solution">The best point seen.</param>
    /// <param name="Value">The objective there.</param>
    /// <param name="Converged">Whether both tolerances were met before the budget ran out.</param>
    /// <param name="Iterations">Iterations actually taken.</param>
    /// <param name="Evaluations">Objective evaluations actually taken.</param>
    public readonly record struct Result(
        double[] Solution,
        double Value,
        bool Converged,
        int Iterations,
        int Evaluations);

    /// <summary>The displacement used for a starting coordinate that is exactly zero.</summary>
    private const double ZeroStep = 0.00025;

    /// <summary>The fraction a non-zero starting coordinate is stretched by to make its vertex.</summary>
    private const double RelativeStep = 0.05;

    /// <summary>
    /// Minimizes <paramref name="objective"/> from <paramref name="start"/>.
    /// </summary>
    /// <param name="objective">
    /// The function to minimize. It may return <see cref="double.PositiveInfinity"/> for a point
    /// outside its domain — that is how the fitters keep a shape parameter positive without needing a
    /// constrained solver — but it must be finite at <paramref name="start"/>.
    /// </param>
    /// <param name="start">The starting point; its length is the number of free parameters.</param>
    /// <param name="settings">Budget and tolerances.</param>
    public static Result Minimize(
        Func<double[], double> objective, double[] start, Settings settings = default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(start);
        if (start.Length == 0)
        {
            throw new ArgumentException("The starting point needs at least one coordinate.", nameof(start));
        }

        int n = start.Length;
        int maxIterations = settings.MaxIterations > 0 ? settings.MaxIterations : 200 * n;
        int maxEvaluations = settings.MaxEvaluations > 0 ? settings.MaxEvaluations : 200 * n;
        double toleranceX = settings.ToleranceX > 0 ? settings.ToleranceX : 1e-8;
        double toleranceF = settings.ToleranceFunction > 0 ? settings.ToleranceFunction : 1e-8;

        int evaluations = 0;
        double Evaluate(double[] point)
        {
            evaluations++;
            double value = objective(point);
            return double.IsNaN(value) ? double.PositiveInfinity : value;
        }

        // Vertex 0 is the starting point; vertex i+1 moves coordinate i and nothing else.
        var simplex = new double[n + 1][];
        var values = new double[n + 1];
        simplex[0] = (double[])start.Clone();
        values[0] = Evaluate(simplex[0]);

        for (int i = 0; i < n; i++)
        {
            double[] vertex = (double[])start.Clone();
            vertex[i] = vertex[i] != 0 ? vertex[i] * (1 + RelativeStep) : ZeroStep;
            simplex[i + 1] = vertex;
            values[i + 1] = Evaluate(vertex);
        }

        Order(simplex, values);

        int iteration = 0;
        bool converged = false;
        while (iteration < maxIterations && evaluations < maxEvaluations)
        {
            if (IsSmallEnough(simplex, values, toleranceX, toleranceF))
            {
                converged = true;
                break;
            }

            iteration++;

            // The centroid of everything but the worst vertex — the direction the worst one is pushed
            // through.
            var centroid = new double[n];
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
                Replace(simplex, values, n, expandedValue < reflectedValue ? expanded : reflected,
                    Math.Min(expandedValue, reflectedValue));
            }
            else if (reflectedValue < values[n - 1])
            {
                // Better than the second worst: an ordinary reflection.
                Replace(simplex, values, n, reflected, reflectedValue);
            }
            else
            {
                // Reflection failed. Contract towards whichever side is better and, if that fails too,
                // shrink everything towards the best vertex.
                bool outside = reflectedValue < values[n];
                double[] contracted = outside ? Along(centroid, worst, 0.5) : Along(centroid, worst, -0.5);
                double contractedValue = Evaluate(contracted);
                double target = outside ? reflectedValue : values[n];

                if (contractedValue < target)
                {
                    Replace(simplex, values, n, contracted, contractedValue);
                }
                else
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
            }

            Order(simplex, values);
        }

        if (!converged && IsSmallEnough(simplex, values, toleranceX, toleranceF))
        {
            converged = true;
        }

        return new Result(simplex[0], values[0], converged, iteration, evaluations);
    }

    /// <summary>
    /// The point <paramref name="factor"/> of the way from <paramref name="worst"/> past
    /// <paramref name="centroid"/>. A factor of 1 reflects, 2 expands, ½ contracts outwards and −½
    /// contracts inwards.
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

    /// <summary>Puts <paramref name="point"/> at <paramref name="slot"/>.</summary>
    private static void Replace(
        double[][] simplex, double[] values, int slot, double[] point, double value)
    {
        simplex[slot] = point;
        values[slot] = value;
    }

    /// <summary>Sorts the vertices so index 0 is the best and index n the worst.</summary>
    private static void Order(double[][] simplex, double[] values)
    {
        // Insertion sort: the array is tiny and all but one entry is already in place after an
        // ordinary iteration, so this is the cheapest thing that also stays stable.
        for (int i = 1; i < values.Length; i++)
        {
            double value = values[i];
            double[] vertex = simplex[i];
            int j = i - 1;
            while (j >= 0 && values[j] > value)
            {
                values[j + 1] = values[j];
                simplex[j + 1] = simplex[j];
                j--;
            }

            values[j + 1] = value;
            simplex[j + 1] = vertex;
        }
    }

    /// <summary>
    /// Whether every vertex is within <paramref name="toleranceX"/> of the best one and every value
    /// within <paramref name="toleranceF"/> of the best value. Both are relative to the best vertex's
    /// own size, so a fit whose parameters are in the thousands is not asked for absolute precision it
    /// cannot have.
    /// </summary>
    private static bool IsSmallEnough(
        double[][] simplex, double[] values, double toleranceX, double toleranceF)
    {
        if (double.IsInfinity(values[0]))
        {
            return false;
        }

        double[] best = simplex[0];
        int n = best.Length;

        for (int v = 1; v <= n; v++)
        {
            for (int i = 0; i < n; i++)
            {
                double allowed = toleranceX * Math.Max(1, Math.Abs(best[i]));
                if (Math.Abs(simplex[v][i] - best[i]) > allowed)
                {
                    return false;
                }
            }

            double allowedValue = toleranceF * Math.Max(1, Math.Abs(values[0]));
            if (!(Math.Abs(values[v] - values[0]) <= allowedValue))
            {
                return false;
            }
        }

        return true;
    }
}
