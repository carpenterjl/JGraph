namespace JGraph.Numerics;

/// <summary>
/// What a numerically differenced Jacobian needs to know, and what it remembers between calls.
/// </summary>
/// <remarks>
/// <see cref="Increments"/> is the working storage MATLAB's <c>numjac</c> calls <c>fac</c>: one
/// number per state component recording how large a perturbation last produced a difference worth
/// believing. It is carried from step to step, which is why a Jacobian formed at step nine costs
/// what it does rather than what the first one cost, and why it must not be reset between calls.
/// </remarks>
internal sealed class OdeJacobianOptions
{
    /// <summary>The size below which a component's exact value does not matter — <c>AbsTol</c>, per component.</summary>
    public required double[] Threshold { get; init; }

    /// <summary>Salane's <c>fac</c>, null until the first call fills it in.</summary>
    public double[]? Increments { get; set; }

    /// <summary>Which entries of the Jacobian can be nonzero, or null for a full one.</summary>
    public bool[,]? Pattern { get; init; }

    /// <summary>The column grouping of <see cref="Pattern"/>, 1-based; 0 means a column of zeros.</summary>
    public int[]? Groups { get; init; }
}

/// <summary>
/// MATLAB's <c>numjac</c>: the Jacobian of a function by forward differences, with Salane's rule
/// for choosing each column's increment and for deciding afterwards that a column was mostly
/// rounding and is worth one more evaluation.
/// </summary>
/// <remarks>
/// <para>
/// A stiff solver's cost is the Jacobian, not the formula: the iteration matrix is refreshed only
/// when the Newton iteration slows down, and each refresh costs one derivative evaluation per
/// column. That is the whole reason <c>JPattern</c> exists — columns that share no row of the
/// pattern can be perturbed together, and a banded problem of four hundred equations then costs
/// three evaluations instead of four hundred.
/// </para>
/// <para>
/// The increment is not <c>sqrt(eps)·y</c>. It is <c>(y + fac·yscale) − y</c> — formed and then
/// subtracted, so that the increment is exactly representable and the difference quotient divides
/// by the step that was actually taken. Where the state is smaller than its own tolerance the
/// threshold stands in for it, and where the difference came out at the level of rounding the
/// column is taken again with a larger increment and kept only if it says more.
/// </para>
/// </remarks>
internal static class OdeNumericalJacobian
{
    private const double Epsilon = 2.220446049250313e-16;

    /// <summary>
    /// The Jacobian of <paramref name="f"/> at <paramref name="y"/>, where
    /// <paramref name="value"/> is <c>f(y)</c>.
    /// </summary>
    /// <param name="f">The function, with everything but the differentiation variable bound.</param>
    /// <param name="vectorized">
    /// The same function over several states at once, when the caller said <c>Vectorized</c>; null
    /// otherwise. It changes how many times the function is called and not how many values are
    /// computed, which is what the statistics count.
    /// </param>
    /// <param name="y">Where to differentiate.</param>
    /// <param name="value"><c>f(y)</c>, already evaluated.</param>
    /// <param name="options">The thresholds, the remembered increments and the sparsity pattern.</param>
    /// <param name="evaluations">How many values of <paramref name="f"/> the difference cost.</param>
    public static double[,] Compute(Func<double[], double[]> f, Func<double[][], double[][]>? vectorized,
        double[] y, double[] value, OdeJacobianOptions options, out int evaluations)
    {
        int ny = y.Length;
        int nf = value.Length;
        double[] threshold = options.Threshold;
        double br = Math.Pow(Epsilon, 0.875);
        double bl = Math.Pow(Epsilon, 0.75);
        double bu = Math.Pow(Epsilon, 0.25);
        const double FactorCeiling = 0.1;
        double factorFloor = Math.Pow(Epsilon, 0.78);

        double[] fac = options.Increments ?? Filled(ny, Math.Sqrt(Epsilon));
        options.Increments = fac;

        var yscale = new double[ny];
        var del = new double[ny];
        for (int j = 0; j < ny; j++)
        {
            yscale[j] = Math.Max(Math.Abs(y[j]), threshold[j]);
            del[j] = (y[j] + (fac[j] * yscale[j])) - y[j];
            while (del[j] == 0)
            {
                if (fac[j] < FactorCeiling)
                {
                    fac[j] = Math.Min(100 * fac[j], FactorCeiling);
                    del[j] = (y[j] + (fac[j] * yscale[j])) - y[j];
                }
                else
                {
                    del[j] = threshold[j];
                }
            }
        }

        // A square Jacobian keeps each increment pointing the way the function is already going,
        // so that the difference is taken inside the region the solution lives in.
        if (nf == ny)
        {
            for (int j = 0; j < ny; j++)
            {
                del[j] = (value[j] >= 0 ? 1 : -1) * Math.Abs(del[j]);
            }
        }

        bool[,]? pattern = options.Pattern;
        int[]? groups = options.Groups;
        var jacobian = new double[nf, ny];
        var difference = new double[nf, ny];   // Fdel - Fvalue, laid out by column of the Jacobian
        var largest = new double[ny];
        var largestRow = new int[ny];
        var perturbedAtRow = new double[ny];

        if (pattern is null || groups is null)
        {
            double[][] columns;
            if (vectorized is not null)
            {
                var states = new double[ny][];
                for (int j = 0; j < ny; j++)
                {
                    states[j] = (double[])y.Clone();
                    states[j][j] += del[j];
                }

                columns = vectorized(states);
            }
            else
            {
                columns = new double[ny][];
                var moved = (double[])y.Clone();
                for (int j = 0; j < ny; j++)
                {
                    double kept = moved[j];
                    moved[j] = kept + del[j];
                    columns[j] = f(moved);
                    moved[j] = kept;
                }
            }

            evaluations = ny;
            for (int j = 0; j < ny; j++)
            {
                double best = -1;
                int bestRow = 0;
                for (int i = 0; i < nf; i++)
                {
                    double d = columns[j][i] - value[i];
                    difference[i, j] = d;
                    jacobian[i, j] = d / del[j];
                    double magnitude = Math.Abs(d);
                    if (magnitude > best)
                    {
                        best = magnitude;
                        bestRow = i;
                    }
                }

                largest[j] = best;
                largestRow[j] = bestRow;
                perturbedAtRow[j] = Math.Abs(columns[j][bestRow]);
            }
        }
        else
        {
            int groupCount = 0;
            foreach (int g in groups)
            {
                groupCount = Math.Max(groupCount, g);
            }

            if (groupCount == 0)
            {
                evaluations = 0;
                return jacobian;
            }

            var states = new double[groupCount][];
            for (int g = 0; g < groupCount; g++)
            {
                states[g] = (double[])y.Clone();
            }

            for (int j = 0; j < ny; j++)
            {
                if (groups[j] > 0)
                {
                    states[groups[j] - 1][j] += del[j];
                }
            }

            double[][] columns = vectorized is not null
                ? vectorized(states)
                : Map(states, f);
            evaluations = groupCount;

            for (int j = 0; j < ny; j++)
            {
                double best = 0;
                int bestRow = 0;
                if (groups[j] > 0)
                {
                    double[] perturbed = columns[groups[j] - 1];
                    for (int i = 0; i < nf; i++)
                    {
                        if (!pattern[i, j])
                        {
                            continue;
                        }

                        double d = perturbed[i] - value[i];
                        difference[i, j] = d;
                        jacobian[i, j] = d / del[j];
                    }

                    // The largest entry of the column as the sparse matrix holds it: the pattern's
                    // own rows, and implicit zeros everywhere else.
                    for (int i = 0; i < nf; i++)
                    {
                        double magnitude = Math.Abs(difference[i, j]);
                        if (magnitude > best)
                        {
                            best = magnitude;
                            bestRow = i;
                        }
                    }

                    perturbedAtRow[j] = Math.Abs(perturbed[bestRow]);
                }

                largest[j] = best;
                largestRow[j] = bestRow;
            }
        }

        // A column whose difference sits at the level of rounding is taken again with a larger
        // increment, and kept only when the second reading is the more significant one.
        for (int k = 0; k < ny; k++)
        {
            double atPerturbed = perturbedAtRow[k];
            double atValue = Math.Abs(value[largestRow[k]]);
            double difmax = largest[k];
            if (!((atPerturbed != 0 && atValue != 0) || difmax == 0))
            {
                continue;
            }

            if (pattern is not null && groups is not null && groups[k] <= 0)
            {
                continue;
            }

            double scale = Math.Max(atPerturbed, atValue);
            double current = fac[k];
            if (difmax <= br * scale)
            {
                double tried = Math.Min(Math.Sqrt(current), FactorCeiling);
                double step = (y[k] + (tried * yscale[k])) - y[k];
                if (tried != current && step != 0)
                {
                    if (nf == ny)
                    {
                        step = (value[k] >= 0 ? 1 : -1) * Math.Abs(step);
                    }

                    var moved = (double[])y.Clone();
                    moved[k] = y[k] + step;
                    double[] again = f(moved);
                    evaluations++;

                    var quotient = new double[nf];
                    double retryMax = 0;
                    int retryRow = 0;
                    double quotientNorm = 0;
                    for (int i = 0; i < nf; i++)
                    {
                        double d = again[i] - value[i];
                        quotient[i] = d / step;
                        quotientNorm = Math.Max(quotientNorm, Math.Abs(quotient[i]));
                        if (Math.Abs(d) > retryMax)
                        {
                            retryMax = Math.Abs(d);
                            retryRow = i;
                        }
                    }

                    double columnNorm = 0;
                    for (int i = 0; i < nf; i++)
                    {
                        columnNorm = Math.Max(columnNorm, Math.Abs(jacobian[i, k]));
                    }

                    if (tried * quotientNorm >= columnNorm)
                    {
                        if (quotientNorm > 0)
                        {
                            for (int i = 0; i < nf; i++)
                            {
                                if (pattern is null || pattern[i, k])
                                {
                                    jacobian[i, k] = quotient[i];
                                }
                            }
                        }

                        double retryScale = Math.Max(Math.Abs(again[retryRow]), Math.Abs(value[retryRow]));
                        fac[k] = retryMax <= bl * retryScale ? Math.Min(10 * tried, FactorCeiling)
                            : retryMax > bu * retryScale ? Math.Max(0.1 * tried, factorFloor)
                            : tried;
                    }
                }
            }
            else if (difmax <= bl * scale)
            {
                fac[k] = Math.Min(10 * current, FactorCeiling);
            }

            if (difmax > bu * scale)
            {
                fac[k] = Math.Max(0.1 * fac[k], factorFloor);
            }
        }

        return jacobian;
    }

    /// <summary>
    /// MATLAB's <c>colgroup</c>, first-fit: the columns of <paramref name="pattern"/> gathered into
    /// groups no two members of which share a row, so that one evaluation differences them all.
    /// Answers a 1-based group per column, and 0 for a column that is all zeros.
    /// </summary>
    public static int[] ColumnGroups(bool[,] pattern)
    {
        int rows = pattern.GetLength(0);
        int n = pattern.GetLength(1);
        var groups = new int[n];

        // A row that touches every column forces every column into a group of its own.
        for (int i = 0; i < rows; i++)
        {
            bool full = true;
            for (int j = 0; j < n && full; j++)
            {
                full = pattern[i, j];
            }

            if (full)
            {
                for (int j = 0; j < n; j++)
                {
                    groups[j] = j + 1;
                }

                return groups;
            }
        }

        // overlap[i, j] for i >= j: how many rows columns i and j share. Only the lower triangle
        // is formed, which is what makes the packing below read each pair once.
        var overlap = new int[n, n];
        for (int j = 0; j < n; j++)
        {
            for (int i = j; i < n; i++)
            {
                int shared = 0;
                for (int r = 0; r < rows; r++)
                {
                    if (pattern[r, i] && pattern[r, j])
                    {
                        shared++;
                    }
                }

                overlap[i, j] = shared;
            }
        }

        var pending = new List<int>(n);
        for (int j = 0; j < n; j++)
        {
            pending.Add(j);
        }

        var reach = new int[n];
        int groupNumber = 0;
        while (pending.Count > 0)
        {
            groupNumber++;
            int first = pending[0];
            groups[first] = groupNumber;
            Array.Clear(reach);
            for (int i = 0; i < n; i++)
            {
                reach[i] = overlap[i, first];
            }

            foreach (int k in pending)
            {
                if (reach[k] != 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    reach[i] += overlap[i, k];
                }

                groups[k] = groupNumber;
            }

            pending.RemoveAll(j => groups[j] != 0);
        }

        for (int j = 0; j < n; j++)
        {
            bool any = false;
            for (int r = 0; r < rows && !any; r++)
            {
                any = pattern[r, j];
            }

            if (!any)
            {
                groups[j] = 0;
            }
        }

        return groups;
    }

    private static double[][] Map(double[][] states, Func<double[], double[]> f)
    {
        var answers = new double[states.Length][];
        for (int i = 0; i < states.Length; i++)
        {
            answers[i] = f(states[i]);
        }

        return answers;
    }

    private static double[] Filled(int length, double value)
    {
        var filled = new double[length];
        Array.Fill(filled, value);
        return filled;
    }
}
