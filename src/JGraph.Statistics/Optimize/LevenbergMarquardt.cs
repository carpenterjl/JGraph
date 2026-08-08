namespace JGraph.Statistics.Optimize;

/// <summary>
/// Levenberg–Marquardt least squares: the solver behind <c>nlinfit</c>.
/// </summary>
/// <remarks>
/// <para>
/// A sum of squared residuals has more structure than a general objective, and this exploits it. Near
/// the answer the residuals are almost linear in the parameters, so the Gauss–Newton step — solve the
/// linearized problem, move there — converges quadratically; far from it that step can be wild. The
/// damping term interpolates between the two: with none the step is Gauss–Newton, with a great deal it
/// is a short step straight downhill. The damping is lowered after every step that helps and raised
/// after every step that does not, so the method chooses its own character as it goes.
/// </para>
/// <para>
/// The damping is applied to the scaled problem — each parameter's column is measured against its own
/// curvature rather than all of them against one — which is what stops a model whose parameters differ
/// by orders of magnitude from stalling. That is Marquardt's own refinement and it is why this is not
/// simply "Gauss–Newton with a ridge term".
/// </para>
/// <para>
/// The Jacobian is taken by forward differences, with each parameter's step scaled to its own size.
/// A model given as a script function has no derivative to hand, and asking for one would rule out
/// most of what <c>nlinfit</c> is used for.
/// </para>
/// </remarks>
public static class LevenbergMarquardt
{
    /// <summary>How the search was allowed to run and when it should stop.</summary>
    /// <param name="MaxIterations">Iterations allowed, or zero for two hundred.</param>
    /// <param name="ToleranceX">How small a relative parameter move counts as settled.</param>
    /// <param name="ToleranceFunction">How small a relative improvement counts as settled.</param>
    /// <param name="InitialDamping">The damping to start from, or zero for a hundredth.</param>
    public readonly record struct Settings(
        int MaxIterations = 0,
        double ToleranceX = 1e-8,
        double ToleranceFunction = 1e-8,
        double InitialDamping = 0);

    /// <summary>What the search found.</summary>
    /// <param name="Solution">The parameters at the best point seen.</param>
    /// <param name="Residuals">The residuals there.</param>
    /// <param name="Jacobian">Their derivatives with respect to each parameter, one row per residual.</param>
    /// <param name="SumOfSquares">The objective there.</param>
    /// <param name="Converged">Whether a tolerance was met before the budget ran out.</param>
    /// <param name="Iterations">Iterations actually taken.</param>
    /// <param name="Evaluations">Residual evaluations actually taken, the Jacobian's included.</param>
    public readonly record struct Result(
        double[] Solution,
        double[] Residuals,
        double[,] Jacobian,
        double SumOfSquares,
        bool Converged,
        int Iterations,
        int Evaluations);

    /// <summary>Minimizes the sum of squares of <paramref name="residuals"/> from <paramref name="start"/>.</summary>
    /// <param name="residuals">The residual vector at a set of parameters; its length must not change.</param>
    /// <param name="start">The starting parameters.</param>
    /// <param name="settings">Budget and tolerances.</param>
    public static Result Minimize(
        Func<double[], double[]> residuals, double[] start, Settings settings = default)
    {
        ArgumentNullException.ThrowIfNull(residuals);
        ArgumentNullException.ThrowIfNull(start);
        if (start.Length == 0)
        {
            throw new ArgumentException("the model needs at least one parameter.", nameof(start));
        }

        int maxIterations = settings.MaxIterations > 0 ? settings.MaxIterations : 200;
        double toleranceX = settings.ToleranceX > 0 ? settings.ToleranceX : 1e-8;
        double toleranceF = settings.ToleranceFunction > 0 ? settings.ToleranceFunction : 1e-8;
        double damping = settings.InitialDamping > 0 ? settings.InitialDamping : 1e-2;

        int evaluations = 0;
        double[] Evaluate(double[] point)
        {
            evaluations++;
            double[] values = residuals(point);
            if (values is null)
            {
                throw new ArgumentException("the model answered nothing.");
            }

            return values;
        }

        int k = start.Length;
        var beta = (double[])start.Clone();
        double[] current = Evaluate(beta);
        int n = current.Length;
        if (n < 1)
        {
            throw new ArgumentException("the model answered no residuals at all.");
        }

        double best = SumSquares(current);
        if (!double.IsFinite(best))
        {
            throw new ArgumentException("the model is not finite at the starting parameters.");
        }

        double[,] jacobian = Jacobian(Evaluate, beta, current);
        bool converged = false;
        int iteration = 0;
        for (iteration = 1; iteration <= maxIterations; iteration++)
        {
            // The scaled normal equations: (JᵀJ + λ·diag(JᵀJ))·δ = −Jᵀr. The right-hand side is the
            // downhill direction whichever way round the residual was defined, because J is the
            // Jacobian of the residual itself. Scaling the damping by each column's own curvature is
            // what makes it mean the same thing for every parameter.
            var normal = new double[k, k];
            var gradient = new double[k];
            for (int a = 0; a < k; a++)
            {
                for (int i = 0; i < n; i++)
                {
                    gradient[a] -= jacobian[i, a] * current[i];
                }

                for (int b = 0; b < k; b++)
                {
                    double value = 0;
                    for (int i = 0; i < n; i++)
                    {
                        value += jacobian[i, a] * jacobian[i, b];
                    }

                    normal[a, b] = value;
                }
            }

            bool improved = false;
            for (int attempt = 0; attempt < 40 && !improved; attempt++)
            {
                var damped = new double[k, k];
                for (int a = 0; a < k; a++)
                {
                    for (int b = 0; b < k; b++)
                    {
                        damped[a, b] = normal[a, b];
                    }

                    damped[a, a] += damping * Math.Max(normal[a, a], 1e-12);
                }

                double[]? step = SolveSymmetric(damped, gradient);
                if (step is null)
                {
                    damping *= 10;
                    continue;
                }

                var candidate = new double[k];
                double move = 0, size = 0;
                for (int a = 0; a < k; a++)
                {
                    candidate[a] = beta[a] + step[a];
                    move = Math.Max(move, Math.Abs(step[a]));
                    size = Math.Max(size, Math.Abs(beta[a]));
                }

                double[] trial;
                double value;
                try
                {
                    trial = Evaluate(candidate);
                    value = SumSquares(trial);
                }
                catch (ArithmeticException)
                {
                    damping *= 10;
                    continue;
                }

                if (!double.IsFinite(value) || value >= best)
                {
                    damping *= 10;
                    continue;
                }

                improved = true;
                bool settledX = move <= toleranceX * (1 + size);
                bool settledF = best - value <= toleranceF * (1 + Math.Abs(best));
                beta = candidate;
                current = trial;
                best = value;
                damping = Math.Max(damping / 10, 1e-12);
                jacobian = Jacobian(Evaluate, beta, current);
                if (settledX || settledF)
                {
                    converged = true;
                }
            }

            if (converged || !improved)
            {
                converged = converged || IsStationary(jacobian, current, toleranceF);
                break;
            }
        }

        return new Result(
            beta, current, jacobian, best, converged, Math.Min(iteration, maxIterations), evaluations);
    }

    /// <summary>The residuals' derivatives, by a forward difference scaled to each parameter's size.</summary>
    private static double[,] Jacobian(Func<double[], double[]> residuals, double[] beta, double[] at)
    {
        int k = beta.Length;
        int n = at.Length;
        var jacobian = new double[n, k];
        for (int a = 0; a < k; a++)
        {
            double step = 1.4901161193847656e-08 * Math.Max(Math.Abs(beta[a]), 1e-3);
            var moved = (double[])beta.Clone();
            moved[a] = beta[a] + step;
            double actual = moved[a] - beta[a];
            double[] shifted = residuals(moved);
            for (int i = 0; i < n; i++)
            {
                jacobian[i, a] = (shifted[i] - at[i]) / actual;
            }
        }

        return jacobian;
    }

    /// <summary>Whether the gradient has effectively vanished, which is the other way to be finished.</summary>
    private static bool IsStationary(double[,] jacobian, double[] residuals, double tolerance)
    {
        int n = jacobian.GetLength(0);
        int k = jacobian.GetLength(1);
        double largest = 0;
        for (int a = 0; a < k; a++)
        {
            double value = 0;
            for (int i = 0; i < n; i++)
            {
                value += jacobian[i, a] * residuals[i];
            }

            largest = Math.Max(largest, Math.Abs(value));
        }

        return largest <= tolerance;
    }

    private static double SumSquares(double[] values)
    {
        double total = 0;
        foreach (double value in values)
        {
            total += value * value;
        }

        return total;
    }

    /// <summary>Solves a symmetric positive system by Cholesky, or answers null where it is not one.</summary>
    private static double[]? SolveSymmetric(double[,] matrix, double[] rhs)
    {
        int k = rhs.Length;
        var lower = new double[k, k];
        for (int i = 0; i < k; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = matrix[i, j];
                for (int c = 0; c < j; c++)
                {
                    sum -= lower[i, c] * lower[j, c];
                }

                if (i == j)
                {
                    if (sum <= 0)
                    {
                        return null;
                    }

                    lower[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    lower[i, j] = sum / lower[j, j];
                }
            }
        }

        var forward = new double[k];
        for (int i = 0; i < k; i++)
        {
            double sum = rhs[i];
            for (int c = 0; c < i; c++)
            {
                sum -= lower[i, c] * forward[c];
            }

            forward[i] = sum / lower[i, i];
        }

        var solution = new double[k];
        for (int i = k - 1; i >= 0; i--)
        {
            double sum = forward[i];
            for (int c = i + 1; c < k; c++)
            {
                sum -= lower[c, i] * solution[c];
            }

            solution[i] = sum / lower[i, i];
        }

        return solution;
    }
}
