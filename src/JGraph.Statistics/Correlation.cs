using JGraph.Numerics;

namespace JGraph.Statistics;

/// <summary>
/// How strongly two variables move together, in the three senses MATLAB's <c>corr</c> offers: Pearson's
/// product moment, Spearman's correlation of the ranks, and Kendall's concordance of the pairs. Each
/// comes with the chance of seeing a coefficient that large from unrelated data. Also here: the
/// conversions and repairs that operate on a whole covariance or correlation matrix.
/// </summary>
public static class Correlation
{
    /// <summary>Which correlation is being asked for.</summary>
    public enum Kind
    {
        /// <summary>The product-moment correlation of the values themselves.</summary>
        Pearson,

        /// <summary>Pearson's correlation of the ranks, which measures any monotone relationship.</summary>
        Spearman,

        /// <summary>Kendall's tau-b: how much more often pairs agree than disagree, ties allowed for.</summary>
        Kendall,
    }

    /// <summary>Which side of the null distribution the p-value is measured on.</summary>
    public enum Tail
    {
        /// <summary>A correlation of either sign counts against the null.</summary>
        Both,

        /// <summary>Only a positive correlation counts.</summary>
        Right,

        /// <summary>Only a negative correlation counts.</summary>
        Left,
    }

    /// <summary>
    /// The correlation between two equal-length samples and the chance of seeing one at least that
    /// extreme from unrelated data.
    /// </summary>
    public static (double Coefficient, double PValue) Between(
        IReadOnlyList<double> left, IReadOnlyList<double> right, Kind kind, Tail tail)
    {
        int n = Math.Min(left.Count, right.Count);
        if (n < 2)
        {
            return (double.NaN, double.NaN);
        }

        return kind switch
        {
            Kind.Spearman => SpearmanBetween(left, right, n, tail),
            Kind.Kendall => KendallBetween(left, right, n, tail),
            _ => PearsonWithP(Pearson(left, right), n, 2, tail),
        };
    }

    /// <summary>Pearson's correlation of two samples, without a significance test.</summary>
    public static double Pearson(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        int n = Math.Min(left.Count, right.Count);
        if (n < 2)
        {
            return double.NaN;
        }

        double meanLeft = DescriptiveStatistics.Mean(Take(left, n));
        double meanRight = DescriptiveStatistics.Mean(Take(right, n));
        double together = 0;
        double spreadLeft = 0;
        double spreadRight = 0;
        for (int i = 0; i < n; i++)
        {
            double a = left[i] - meanLeft;
            double b = right[i] - meanRight;
            together += a * b;
            spreadLeft += a * a;
            spreadRight += b * b;
        }

        double scale = Math.Sqrt(spreadLeft * spreadRight);
        return scale == 0 ? double.NaN : together / scale;
    }

    /// <summary>
    /// The chance of seeing a Pearson correlation at least this extreme when the variables are
    /// unrelated. The statistic is Student's t on n − <paramref name="lost"/> degrees of freedom, so
    /// the same routine serves the partial correlations, which lose one degree of freedom per variable
    /// they hold fixed.
    /// </summary>
    /// <param name="r">The correlation.</param>
    /// <param name="n">How many observations it came from.</param>
    /// <param name="lost">Degrees of freedom already spent (2 for a plain correlation).</param>
    /// <param name="tail">Which side of the null distribution to measure.</param>
    public static (double Coefficient, double PValue) PearsonWithP(double r, int n, int lost, Tail tail)
    {
        double df = n - lost;
        if (df < 1 || double.IsNaN(r))
        {
            return (r, double.NaN);
        }

        if (Math.Abs(r) >= 1)
        {
            // A perfect correlation leaves no room for chance in the direction it points, and none of
            // the null distribution on the other side of it either.
            double perfect = tail switch
            {
                Tail.Right => r > 0 ? 0 : 1,
                Tail.Left => r < 0 ? 0 : 1,
                _ => 0,
            };
            return (r, perfect);
        }

        double t = r * Math.Sqrt(df / (1 - (r * r)));
        return (r, StudentTail(t, df, tail));
    }

    /// <summary>The tail area of Student's t distribution, on whichever side was asked for.</summary>
    public static double StudentTail(double t, double df, Tail tail)
    {
        // The two-sided area is exactly the regularized incomplete beta; the one-sided areas are half
        // of it, taken on the side the statistic actually landed.
        double twoSided = SpecialFunctions.BetaRegularized(df / (df + (t * t)), df / 2, 0.5);
        return tail switch
        {
            Tail.Right => t >= 0 ? twoSided / 2 : 1 - (twoSided / 2),
            Tail.Left => t <= 0 ? twoSided / 2 : 1 - (twoSided / 2),
            _ => twoSided,
        };
    }

    /// <summary>The tail area of the standard normal distribution, on whichever side was asked for.</summary>
    public static double NormalTail(double z, Tail tail)
    {
        double upper = 0.5 * SpecialFunctions.Erfc(z / Math.Sqrt(2));
        return tail switch
        {
            Tail.Right => upper,
            Tail.Left => 1 - upper,
            _ => 2 * Math.Min(upper, 1 - upper),
        };
    }

    private static (double Coefficient, double PValue) SpearmanBetween(
        IReadOnlyList<double> left, IReadOnlyList<double> right, int n, Tail tail)
    {
        (double[] rankLeft, _) = DescriptiveStatistics.TiedRanks(
            Take(left, n), DescriptiveStatistics.TieAdjustment.RankSumOfCubes);
        (double[] rankRight, _) = DescriptiveStatistics.TiedRanks(
            Take(right, n), DescriptiveStatistics.TieAdjustment.RankSumOfCubes);

        // Spearman's coefficient is Pearson's applied to the ranks, ties averaged — which is the
        // definition that stays correct when the sample has repeats, unlike the 6Σd² shortcut.
        return PearsonWithP(Pearson(rankLeft, rankRight), n, 2, tail);
    }

    private static (double Coefficient, double PValue) KendallBetween(
        IReadOnlyList<double> left, IReadOnlyList<double> right, int n, Tail tail)
    {
        long concordant = 0;
        long discordant = 0;
        long tiedLeft = 0;
        long tiedRight = 0;
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                int a = Math.Sign(left[j] - left[i]);
                int b = Math.Sign(right[j] - right[i]);
                if (a == 0 && b == 0)
                {
                    tiedLeft++;
                    tiedRight++;
                    continue;
                }

                if (a == 0)
                {
                    tiedLeft++;
                    continue;
                }

                if (b == 0)
                {
                    tiedRight++;
                    continue;
                }

                if (a == b)
                {
                    concordant++;
                }
                else
                {
                    discordant++;
                }
            }
        }

        double pairs = (double)n * (n - 1) / 2;
        double scale = Math.Sqrt((pairs - tiedLeft) * (pairs - tiedRight));
        double tau = scale == 0 ? double.NaN : (concordant - discordant) / scale;

        // The exact null distribution of tau is a permutation count. The normal approximation to its
        // variance is what MATLAB itself falls back on past a small sample, and is used throughout
        // here — recorded as a divergence rather than a silent approximation.
        double variance = (double)n * (n - 1) * ((2 * n) + 5) / 18.0;
        double z = variance <= 0 ? double.NaN : (concordant - discordant) / Math.Sqrt(variance);
        return (tau, double.IsNaN(z) ? double.NaN : NormalTail(z, tail));
    }

    /// <summary>
    /// The correlation matrix a covariance matrix implies, and the standard deviations that scale
    /// between them. A variable with no variance has no correlation with anything, which is the NaN
    /// row and column MATLAB reports too.
    /// </summary>
    public static (double[] Correlations, double[] Deviations) FromCovariance(double[] covariance, int n)
    {
        var deviations = new double[n];
        for (int i = 0; i < n; i++)
        {
            deviations[i] = Math.Sqrt(covariance[i + (i * n)]);
        }

        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                double scale = deviations[r] * deviations[c];
                result[r + (c * n)] = scale == 0 ? double.NaN : covariance[r + (c * n)] / scale;
            }
        }

        // The diagonal is one by definition, and saying so avoids the rounding that dividing a number
        // by its own square root twice would otherwise leave.
        for (int i = 0; i < n; i++)
        {
            if (deviations[i] != 0)
            {
                result[i + (i * n)] = 1;
            }
        }

        return (result, deviations);
    }

    /// <summary>
    /// The nearest correlation matrix to a symmetric matrix that is not quite one — the usual result of
    /// estimating correlations pairwise, which can leave the matrix indefinite. Found by Higham's
    /// alternating projections: project onto the positive semidefinite matrices, then onto the ones
    /// with a unit diagonal, and repeat.
    /// </summary>
    /// <param name="matrix">The starting matrix, column-major, symmetric.</param>
    /// <param name="n">Its order.</param>
    /// <param name="tolerance">How small a step must get before the answer is taken as settled.</param>
    /// <param name="maxIterations">The most projections to make before giving up.</param>
    public static double[] NearestCorrelation(
        double[] matrix, int n, double tolerance, int maxIterations)
    {
        var current = (double[])matrix.Clone();
        var correction = new double[n * n];
        var previous = new double[n * n];

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            Array.Copy(current, previous, current.Length);

            // Dykstra's correction: the residual from the last unit-diagonal step is removed before
            // projecting again, which is what makes the pair of projections converge to the nearest
            // matrix rather than merely to some matrix in the intersection.
            var adjusted = new double[n * n];
            for (int i = 0; i < adjusted.Length; i++)
            {
                adjusted[i] = current[i] - correction[i];
            }

            double[] positive = NearestPositiveSemidefinite(adjusted, n);
            for (int i = 0; i < correction.Length; i++)
            {
                correction[i] = positive[i] - adjusted[i];
            }

            current = positive;
            for (int i = 0; i < n; i++)
            {
                current[i + (i * n)] = 1;
            }

            double step = 0;
            for (int i = 0; i < current.Length; i++)
            {
                step = Math.Max(step, Math.Abs(current[i] - previous[i]));
            }

            if (step <= tolerance)
            {
                break;
            }
        }

        // Symmetry can drift by a rounding error over many projections; averaging with the transpose
        // costs nothing and makes the answer exactly symmetric, which is what a correlation matrix is.
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < c; r++)
            {
                double average = (current[r + (c * n)] + current[c + (r * n)]) / 2;
                current[r + (c * n)] = average;
                current[c + (r * n)] = average;
            }
        }

        return current;
    }

    /// <summary>
    /// The nearest positive semidefinite matrix, by clipping the negative eigenvalues of a symmetric
    /// matrix to zero and rebuilding it.
    /// </summary>
    private static double[] NearestPositiveSemidefinite(double[] matrix, int n)
    {
        (double[] values, double[] vectors) = SymmetricEigen(matrix, n);
        var result = new double[n * n];
        for (int k = 0; k < n; k++)
        {
            double weight = Math.Max(values[k], 0);
            if (weight == 0)
            {
                continue;
            }

            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    result[r + (c * n)] += weight * vectors[r + (k * n)] * vectors[c + (k * n)];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The eigenvalues and eigenvectors of a symmetric matrix, by the cyclic Jacobi rotations. Small
    /// and self-contained on purpose: a correlation matrix is rarely large, and Jacobi's accuracy on
    /// the small eigenvalues is exactly what deciding whether one is negative depends on.
    /// </summary>
    private static (double[] Values, double[] Vectors) SymmetricEigen(double[] matrix, int n)
    {
        var a = (double[])matrix.Clone();
        var v = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            v[i + (i * n)] = 1;
        }

        for (int sweep = 0; sweep < 100; sweep++)
        {
            double offDiagonal = 0;
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < c; r++)
                {
                    offDiagonal += a[r + (c * n)] * a[r + (c * n)];
                }
            }

            if (offDiagonal <= 1e-30)
            {
                break;
            }

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p + (q * n)];
                    if (Math.Abs(apq) < 1e-300)
                    {
                        continue;
                    }

                    double theta = (a[q + (q * n)] - a[p + (p * n)]) / (2 * apq);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    if (theta == 0)
                    {
                        t = 1;
                    }

                    double cos = 1 / Math.Sqrt((t * t) + 1);
                    double sin = t * cos;

                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k + (p * n)];
                        double akq = a[k + (q * n)];
                        a[k + (p * n)] = (cos * akp) - (sin * akq);
                        a[k + (q * n)] = (sin * akp) + (cos * akq);
                    }

                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p + (k * n)];
                        double aqk = a[q + (k * n)];
                        a[p + (k * n)] = (cos * apk) - (sin * aqk);
                        a[q + (k * n)] = (sin * apk) + (cos * aqk);
                    }

                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k + (p * n)];
                        double vkq = v[k + (q * n)];
                        v[k + (p * n)] = (cos * vkp) - (sin * vkq);
                        v[k + (q * n)] = (sin * vkp) + (cos * vkq);
                    }
                }
            }
        }

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = a[i + (i * n)];
        }

        return (values, v);
    }

    private static double[] Take(IReadOnlyList<double> values, int n)
    {
        var taken = new double[n];
        for (int i = 0; i < n; i++)
        {
            taken[i] = values[i];
        }

        return taken;
    }
}
