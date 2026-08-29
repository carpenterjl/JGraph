using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics.Optimization;

/// <summary>
/// The Lawson-Hanson active-set solver behind MATLAB's <c>lsqnonneg</c>: the least-squares solution
/// of C x = d over the non-negative orthant.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is Lawson and Hanson's NNLS, <em>Solving Least Squares Problems</em> (1974,
/// Chapter 23). It splits the unknowns into a passive set free to take any value and an active set
/// pinned at zero, solves the unconstrained problem over the passive set alone, and moves one index
/// between the sets each pass: in whenever the gradient says a pinned unknown wants to rise, out
/// whenever a freed one has gone negative. Because each pass strictly reduces the residual and there
/// are finitely many splits, it terminates.
/// </para>
/// <para>
/// The inner solve is an ordinary unconstrained least-squares over the passive columns, so it rides
/// the same LAPACK path as the backslash operator.
/// </para>
/// </remarks>
public static class NonnegativeLeastSquares
{
    /// <summary>What the solve found and whether it got there.</summary>
    /// <param name="Solution">The non-negative minimizer.</param>
    /// <param name="ResidualNormSquared">The squared norm of the residual there.</param>
    /// <param name="Residual">The residual d - C x.</param>
    /// <param name="Dual">
    /// The gradient C' (d - C x). At a solution its entries are zero on the passive set and no
    /// greater than zero on the active set, which is what makes it the Lagrange multiplier vector.
    /// </param>
    /// <param name="ExitFlag">
    /// <see cref="SearchExit.Converged"/>, or <see cref="SearchExit.BudgetExhausted"/> when the
    /// inner loop hit its iteration cap.
    /// </param>
    /// <param name="Iterations">Outer passes taken.</param>
    public readonly record struct Result(
        double[] Solution,
        double ResidualNormSquared,
        double[] Residual,
        double[] Dual,
        int ExitFlag,
        int Iterations);

    /// <summary>Solves min ||C x - d|| subject to x greater than or equal to zero.</summary>
    /// <param name="c">The m-by-n coefficient matrix.</param>
    /// <param name="d">The m-element right-hand side.</param>
    /// <param name="tolerance">
    /// How large a gradient entry must be to be worth freeing, and how near zero a freed unknown
    /// must fall to be pinned again. Zero for MATLAB's default,
    /// <c>10 * eps * norm(C, 1) * length(C)</c>.
    /// </param>
    /// <exception cref="ArgumentException">The shapes disagree.</exception>
    public static Result Solve(double[,] c, double[] d, double tolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);

        int m = c.GetLength(0);
        int n = c.GetLength(1);
        if (d.Length != m)
        {
            throw new ArgumentException(
                "The right-hand side must have one entry per row of the matrix.", nameof(d));
        }

        double tol = tolerance > 0 ? tolerance : DefaultTolerance(c);

        var passive = new bool[n];
        var x = new double[n];
        double[] residual = Residual(c, d, x);
        double[] gradient = TransposeTimes(c, residual);

        int outerIteration = 0;
        int inner = 0;
        int innerCap = 3 * n;
        int exit = SearchExit.Converged;

        while (true)
        {
            // The pinned unknown whose gradient most wants it to rise. Nothing left wanting to rise
            // means the Karush-Kuhn-Tucker conditions hold and the answer is optimal.
            int freed = -1;
            double best = tol;
            for (int i = 0; i < n; i++)
            {
                if (!passive[i] && gradient[i] > best)
                {
                    best = gradient[i];
                    freed = i;
                }
            }

            if (freed < 0)
            {
                break;
            }

            outerIteration++;
            passive[freed] = true;
            double[] candidate = SolveOverPassive(c, d, passive, n);

            // Freeing that index may have driven others negative. Step only as far towards the
            // candidate as keeps every freed unknown non-negative, pin whatever reached zero, and
            // solve again.
            while (AnyNonPositive(candidate, passive))
            {
                inner++;
                if (inner > innerCap)
                {
                    exit = SearchExit.BudgetExhausted;
                    return new Result(
                        candidate,
                        Dot(residual, residual),
                        residual,
                        gradient,
                        exit,
                        outerIteration);
                }

                double alpha = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (passive[i] && candidate[i] <= 0)
                    {
                        double ratio = x[i] / (x[i] - candidate[i]);
                        if (ratio < alpha)
                        {
                            alpha = ratio;
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * (candidate[i] - x[i]);
                }

                for (int i = 0; i < n; i++)
                {
                    if (passive[i] && Math.Abs(x[i]) < tol)
                    {
                        passive[i] = false;
                    }
                }

                candidate = SolveOverPassive(c, d, passive, n);
            }

            x = candidate;
            residual = Residual(c, d, x);
            gradient = TransposeTimes(c, residual);
        }

        return new Result(x, Dot(residual, residual), residual, gradient, exit, outerIteration);
    }

    /// <summary>
    /// MATLAB's default tolerance, <c>10 * eps * norm(C, 1) * length(C)</c>, where <c>length</c> is
    /// the longer of the two dimensions and <c>norm(C, 1)</c> the largest absolute column sum.
    /// </summary>
    private static double DefaultTolerance(double[,] c)
    {
        int m = c.GetLength(0);
        int n = c.GetLength(1);
        double columnSum = 0;
        for (int col = 0; col < n; col++)
        {
            double sum = 0;
            for (int row = 0; row < m; row++)
            {
                sum += Math.Abs(c[row, col]);
            }

            if (sum > columnSum)
            {
                columnSum = sum;
            }
        }

        return 10 * Math.Pow(2, -52) * columnSum * Math.Max(m, n);
    }

    /// <summary>
    /// The unconstrained least-squares solution over the passive columns alone, scattered back into
    /// a full-length vector with zeros in the active positions.
    /// </summary>
    private static double[] SolveOverPassive(double[,] c, double[] d, bool[] passive, int n)
    {
        var answer = new double[n];
        int free = 0;
        foreach (bool included in passive)
        {
            if (included)
            {
                free++;
            }
        }

        if (free == 0)
        {
            return answer;
        }

        int m = c.GetLength(0);
        var sub = new double[m, free];
        var index = new int[free];
        int at = 0;
        for (int col = 0; col < n; col++)
        {
            if (!passive[col])
            {
                continue;
            }

            index[at] = col;
            for (int row = 0; row < m; row++)
            {
                sub[row, at] = c[row, col];
            }

            at++;
        }

        var rhs = new double[m, 1];
        for (int row = 0; row < m; row++)
        {
            rhs[row, 0] = d[row];
        }

        double[,] solved = Linear.Solve(sub, rhs);
        for (int i = 0; i < free; i++)
        {
            answer[index[i]] = solved[i, 0];
        }

        return answer;
    }

    /// <summary>Whether any freed unknown has been driven to zero or below.</summary>
    private static bool AnyNonPositive(double[] candidate, bool[] passive)
    {
        for (int i = 0; i < candidate.Length; i++)
        {
            if (passive[i] && candidate[i] <= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The residual d - C x.</summary>
    private static double[] Residual(double[,] c, double[] d, double[] x)
    {
        int m = c.GetLength(0);
        int n = c.GetLength(1);
        var residual = new double[m];
        for (int row = 0; row < m; row++)
        {
            double sum = 0;
            for (int col = 0; col < n; col++)
            {
                sum += c[row, col] * x[col];
            }

            residual[row] = d[row] - sum;
        }

        return residual;
    }

    /// <summary>The product C' v.</summary>
    private static double[] TransposeTimes(double[,] c, double[] v)
    {
        int m = c.GetLength(0);
        int n = c.GetLength(1);
        var answer = new double[n];
        for (int col = 0; col < n; col++)
        {
            double sum = 0;
            for (int row = 0; row < m; row++)
            {
                sum += c[row, col] * v[row];
            }

            answer[col] = sum;
        }

        return answer;
    }

    /// <summary>The inner product of two vectors of the same length.</summary>
    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
