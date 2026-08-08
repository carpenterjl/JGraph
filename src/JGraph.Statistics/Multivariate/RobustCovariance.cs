namespace JGraph.Statistics.Multivariate;

/// <summary>Which estimator to reach the robust covariance by.</summary>
public enum RobustCovarianceMethod
{
    /// <summary>The minimum covariance determinant, found by concentration from random starts.</summary>
    MinimumDeterminant,

    /// <summary>The orthogonalized Gnanadesikan-Kettenring pairwise estimator.</summary>
    Orthogonalized,

    /// <summary>A concentration started from the orthogonalized estimator rather than at random.</summary>
    OliveHawkins,
}

/// <summary>
/// A covariance that a handful of outliers cannot move, and the distances measured against it.
/// </summary>
/// <remarks>
/// <para>
/// The ordinary covariance has a breakdown point of nothing: a single observation taken far enough
/// away drags both the centre and the shape with it, which is exactly the observation one is usually
/// trying to find. Every estimator here answers the same question instead — which half of the data,
/// taken together, is tightest — and reports the rest as the outliers they are.
/// </para>
/// <para>
/// The concentration step is what does the work: from any subset, compute its centre and covariance,
/// measure every observation against them, and keep the closest h. That step never increases the
/// determinant, so iterating it converges, and starting it from many random subsets is what turns a
/// local answer into a global one. The random starts are drawn from the stream <c>rng</c> seeds, so a
/// seeded script repeats itself.
/// </para>
/// </remarks>
public static class RobustCovariance
{
    /// <summary>What a robust fit produced.</summary>
    /// <param name="Covariance">The robust covariance, consistency-corrected.</param>
    /// <param name="Centre">The robust centre.</param>
    /// <param name="Distances">Each observation's robust Mahalanobis distance.</param>
    /// <param name="Outliers">Which observations lie beyond the cut-off.</param>
    /// <param name="Cutoff">The distance an observation must exceed to be called one.</param>
    /// <param name="Subset">Which observations the estimate was computed from.</param>
    public readonly record struct Estimate(
        double[,] Covariance, double[] Centre, double[] Distances, bool[] Outliers, double Cutoff, int[] Subset);

    /// <summary>Estimates the covariance robustly.</summary>
    /// <param name="data">The observations, one per row.</param>
    /// <param name="method">Which estimator.</param>
    /// <param name="random">The stream the random starts are drawn from.</param>
    /// <param name="outlierFraction">The fraction of the data the estimator is allowed to discard, between 0 and 0.5.</param>
    /// <param name="starts">How many random subsets to concentrate from.</param>
    /// <param name="alpha">The tail probability the outlier cut-off is taken at.</param>
    public static Estimate Fit(
        double[,] data,
        RobustCovarianceMethod method,
        Random random,
        double outlierFraction = 0.5,
        int starts = 500,
        double alpha = 0.025)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(random);
        int n = data.GetLength(0);
        int p = data.GetLength(1);
        if (n <= p)
        {
            throw new ArgumentException(
                "A robust covariance needs more observations than variables.", nameof(data));
        }

        if (!(outlierFraction >= 0) || outlierFraction > 0.5)
        {
            throw new ArgumentException(
                "The outlier fraction must be between zero and one half.", nameof(outlierFraction));
        }

        int h = Math.Max((int)Math.Ceiling(n * (1 - outlierFraction)), (n + p + 1) / 2);
        h = Math.Min(h, n);

        var rows = new double[n][];
        for (int r = 0; r < n; r++)
        {
            rows[r] = new double[p];
            for (int c = 0; c < p; c++)
            {
                rows[r][c] = data[r, c];
            }
        }

        int[] subset = method switch
        {
            RobustCovarianceMethod.Orthogonalized => Closest(rows, PairwiseDistances(rows), h),
            RobustCovarianceMethod.OliveHawkins => Concentrate(rows, Closest(rows, PairwiseDistances(rows), h), h).Subset,
            _ => BestOfRandomStarts(rows, h, starts, random),
        };

        if (method != RobustCovarianceMethod.Orthogonalized)
        {
            subset = Concentrate(rows, subset, h).Subset;
        }

        (double[] centre, double[,] covariance) = MomentsOf(rows, subset, p);

        // A covariance computed from the tightest half is too small by a known factor, because the half
        // was chosen for being tight. The correction is the chi-squared quantile at h/n over the
        // expected value of the same, which brings the estimate back to the right scale on clean data.
        double coverage = (double)subset.Length / n;
        double quantile = Distributions.ContinuousDistributions.Chi2Inv(coverage, p);
        double consistency = coverage / Distributions.ContinuousDistributions.Chi2Cdf(quantile, p + 2);
        if (double.IsFinite(consistency) && consistency > 0)
        {
            for (int i = 0; i < p; i++)
            {
                for (int j = 0; j < p; j++)
                {
                    covariance[i, j] *= consistency;
                }
            }
        }

        double[] distances = Mahalanobis(rows, centre, covariance);
        double cutoff = Math.Sqrt(Distributions.ContinuousDistributions.Chi2Inv(1 - alpha, p));
        var outliers = new bool[n];
        for (int r = 0; r < n; r++)
        {
            outliers[r] = distances[r] > cutoff;
        }

        return new Estimate(covariance, centre, distances, outliers, cutoff, subset);
    }

    private static int[] BestOfRandomStarts(double[][] rows, int h, int starts, Random random)
    {
        int n = rows.Length;
        int p = rows[0].Length;
        int[] best = [];
        double least = double.PositiveInfinity;

        for (int attempt = 0; attempt < Math.Max(1, starts); attempt++)
        {
            // A start of p + 1 observations is the smallest that can have a covariance at all, and
            // starting small is what makes it likely that at least one start misses every outlier.
            var chosen = new List<int>(p + 1);
            while (chosen.Count < Math.Min(p + 1, n))
            {
                int candidate = random.Next(n);
                if (!chosen.Contains(candidate))
                {
                    chosen.Add(candidate);
                }
            }

            (int[] subset, double determinant) = Concentrate(rows, [.. chosen], h, passes: 2);
            if (determinant < least)
            {
                least = determinant;
                best = subset;
            }
        }

        return best.Length > 0 ? best : Closest(rows, PairwiseDistances(rows), h);
    }

    private static (int[] Subset, double Determinant) Concentrate(
        double[][] rows, int[] start, int h, int passes = 50)
    {
        int p = rows[0].Length;
        int[] subset = start;
        double determinant = double.PositiveInfinity;

        for (int pass = 0; pass < passes; pass++)
        {
            (double[] centre, double[,] covariance) = MomentsOf(rows, subset, p);
            double next = Determinant(covariance);
            if (!(next < determinant) && pass > 0)
            {
                break;
            }

            determinant = next;
            double[] distances = Mahalanobis(rows, centre, covariance);
            int[] closest = Closest(rows, distances, h);
            if (closest.SequenceEqual(subset))
            {
                subset = closest;
                break;
            }

            subset = closest;
        }

        (double[] finalCentre, double[,] finalCovariance) = MomentsOf(rows, subset, p);
        _ = finalCentre;
        return (subset, Determinant(finalCovariance));
    }

    /// <summary>
    /// The Gnanadesikan-Kettenring estimator: every covariance built out of two robust spreads rather
    /// than from products, then made positive-definite by rebuilding it in its own eigenbasis.
    /// </summary>
    private static double[] PairwiseDistances(double[][] rows)
    {
        int n = rows.Length;
        int p = rows[0].Length;
        var centre = new double[p];
        var spread = new double[p];
        for (int c = 0; c < p; c++)
        {
            var column = new double[n];
            for (int r = 0; r < n; r++)
            {
                column[r] = rows[r][c];
            }

            centre[c] = DescriptiveStatistics.Median(column);
            spread[c] = 1.4826 * DescriptiveStatistics.AbsoluteDeviation(column, aroundMedian: true);
            if (!(spread[c] > 0))
            {
                spread[c] = 1;
            }
        }

        var covariance = new double[p, p];
        for (int i = 0; i < p; i++)
        {
            for (int j = i; j < p; j++)
            {
                if (i == j)
                {
                    covariance[i, j] = spread[i] * spread[i];
                    continue;
                }

                // The identity 4·cov = var(u + v) − var(u − v) turns a covariance into two spreads,
                // and a robust spread in place of the variance turns it into a robust covariance.
                var sum = new double[n];
                var difference = new double[n];
                for (int r = 0; r < n; r++)
                {
                    double u = (rows[r][i] - centre[i]) / spread[i];
                    double v = (rows[r][j] - centre[j]) / spread[j];
                    sum[r] = u + v;
                    difference[r] = u - v;
                }

                double a = 1.4826 * DescriptiveStatistics.AbsoluteDeviation(sum, aroundMedian: true);
                double b = 1.4826 * DescriptiveStatistics.AbsoluteDeviation(difference, aroundMedian: true);
                double value = ((a * a) - (b * b)) / 4 * spread[i] * spread[j];
                covariance[i, j] = value;
                covariance[j, i] = value;
            }
        }

        return Mahalanobis(rows, centre, covariance);
    }

    private static int[] Closest(double[][] rows, double[] distances, int h)
    {
        var order = new int[rows.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byDistance = distances[a].CompareTo(distances[b]);
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        });

        int[] kept = order[..Math.Min(h, order.Length)];
        Array.Sort(kept);
        return kept;
    }

    private static (double[] Centre, double[,] Covariance) MomentsOf(double[][] rows, int[] subset, int p)
    {
        var centre = new double[p];
        foreach (int i in subset)
        {
            for (int c = 0; c < p; c++)
            {
                centre[c] += rows[i][c];
            }
        }

        for (int c = 0; c < p; c++)
        {
            centre[c] /= subset.Length;
        }

        var covariance = new double[p, p];
        foreach (int i in subset)
        {
            for (int a = 0; a < p; a++)
            {
                for (int b = 0; b < p; b++)
                {
                    covariance[a, b] += (rows[i][a] - centre[a]) * (rows[i][b] - centre[b]);
                }
            }
        }

        double df = Math.Max(subset.Length - 1, 1);
        for (int a = 0; a < p; a++)
        {
            for (int b = 0; b < p; b++)
            {
                covariance[a, b] /= df;
            }
        }

        return (centre, covariance);
    }

    private static double[] Mahalanobis(double[][] rows, double[] centre, double[,] covariance)
    {
        int p = centre.Length;
        double[,] inverse;
        try
        {
            inverse = PrincipalComponents.Inverse(covariance);
        }
        catch (ArgumentException)
        {
            // A subset that happens to lie in a lower-dimensional plane has a singular covariance;
            // nudging the diagonal keeps the concentration moving rather than stopping the search on
            // an accident of which rows were drawn.
            var ridged = (double[,])covariance.Clone();
            double trace = 0;
            for (int i = 0; i < p; i++)
            {
                trace += ridged[i, i];
            }

            double nudge = Math.Max(trace / p, 1) * 1e-10;
            for (int i = 0; i < p; i++)
            {
                ridged[i, i] += nudge;
            }

            inverse = PrincipalComponents.Inverse(ridged);
        }

        var distances = new double[rows.Length];
        for (int r = 0; r < rows.Length; r++)
        {
            var gap = new double[p];
            for (int c = 0; c < p; c++)
            {
                gap[c] = rows[r][c] - centre[c];
            }

            double total = 0;
            for (int a = 0; a < p; a++)
            {
                double partial = 0;
                for (int b = 0; b < p; b++)
                {
                    partial += inverse[a, b] * gap[b];
                }

                total += gap[a] * partial;
            }

            distances[r] = Math.Sqrt(Math.Max(total, 0));
        }

        return distances;
    }

    private static double Determinant(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var work = (double[,])matrix.Clone();
        double determinant = 1;
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(work[r, c]) > Math.Abs(work[pivot, c]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(work[pivot, c]) < 1e-300)
            {
                return 0;
            }

            if (pivot != c)
            {
                for (int k = 0; k < n; k++)
                {
                    (work[c, k], work[pivot, k]) = (work[pivot, k], work[c, k]);
                }

                determinant = -determinant;
            }

            determinant *= work[c, c];
            for (int r = c + 1; r < n; r++)
            {
                double factor = work[r, c] / work[c, c];
                for (int k = c; k < n; k++)
                {
                    work[r, k] -= factor * work[c, k];
                }
            }
        }

        return Math.Abs(determinant);
    }
}
