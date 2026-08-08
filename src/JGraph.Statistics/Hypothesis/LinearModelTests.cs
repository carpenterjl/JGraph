using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// The tests that take a fitted linear model as their subject rather than a sample: whether its
/// residuals are correlated with each other, whether a stated linear combination of its coefficients
/// is zero, and how many dimensions a set of variables really needs.
/// </summary>
public static class LinearModelTests
{
    /// <summary>The outcome of the Durbin–Watson test.</summary>
    /// <param name="P">The tail probability.</param>
    /// <param name="D">The statistic itself, near 2 when the residuals are uncorrelated.</param>
    /// <param name="Exact">Whether the probability came from the exact distribution.</param>
    public readonly record struct SerialCorrelation(double P, double D, bool Exact);

    /// <summary>The outcome of a linear hypothesis test.</summary>
    /// <param name="P">The tail probability.</param>
    /// <param name="F">The statistic, an F or — with no residual degrees of freedom — a chi-square over its rank.</param>
    /// <param name="Rank">How many independent restrictions were actually tested.</param>
    public readonly record struct LinearHypothesis(double P, double F, int Rank);

    /// <summary>The outcome of Bartlett's dimensionality test, one line per candidate dimension.</summary>
    /// <param name="Dimension">The smallest dimension the data is consistent with.</param>
    /// <param name="P">The probability at each candidate dimension.</param>
    /// <param name="ChiSquare">The statistic at each.</param>
    public readonly record struct Dimensionality(int Dimension, double[] P, double[] ChiSquare);

    /// <summary>
    /// <c>dwtest</c>: whether consecutive residuals of a fitted model are correlated. Values near 2 say
    /// they are not; small values say each residual resembles the one before it.
    /// </summary>
    /// <param name="residuals">The residuals, in the order the observations were taken.</param>
    /// <param name="design">The model's design matrix, one row per observation.</param>
    /// <param name="exact">
    /// Whether to invert the exact null distribution rather than approximate it by a normal. The exact
    /// distribution depends on the design matrix, not only on the sample size, which is why it needs
    /// the design at all.
    /// </param>
    /// <param name="tail">
    /// <see cref="Tail.Right"/> looks for positive correlation, which makes the statistic small.
    /// </param>
    public static SerialCorrelation DurbinWatson(
        double[] residuals, double[,] design, bool exact, Tail tail)
    {
        ArgumentNullException.ThrowIfNull(residuals);
        ArgumentNullException.ThrowIfNull(design);

        int n = residuals.Length;
        int k = design.GetLength(1);
        if (design.GetLength(0) != n)
        {
            throw new ArgumentException("the design matrix must have one row per residual.");
        }

        if (n - k < 2)
        {
            throw new ArgumentException("the Durbin–Watson test needs more observations than the model has terms.");
        }

        double numerator = 0;
        double denominator = 0;
        for (int i = 0; i < n; i++)
        {
            denominator += residuals[i] * residuals[i];
            if (i > 0)
            {
                double step = residuals[i] - residuals[i - 1];
                numerator += step * step;
            }
        }

        if (!(denominator > 0))
        {
            throw new ArgumentException("the residuals are all zero, so there is nothing to test.");
        }

        double d = numerator / denominator;

        // The statistic is a ratio of quadratic forms in the residuals, and the residuals live in the
        // subspace orthogonal to the design. Restricting to an orthonormal basis of that subspace turns
        // the ratio into a weighted sum of independent squares, whose weights are the eigenvalues below
        // and whose distribution the exact path inverts.
        double[,] basis = OrthogonalComplement(design, n, k);
        int m = basis.GetLength(1);
        double[,] projected = Projected(basis, m, n);
        Eigen eigen = Eigen.Factor(projected);

        var weights = new double[m];
        for (int i = 0; i < m; i++)
        {
            weights[i] = eigen.Values[i].Real;
        }

        double cumulative;
        if (exact)
        {
            var shifted = new double[m];
            for (int i = 0; i < m; i++)
            {
                shifted[i] = weights[i] - d;
            }

            cumulative = Imhof.BelowZero(shifted);
        }
        else
        {
            // The normal approximation matches the first two moments of the same ratio, which are the
            // first two power sums of the eigenvalues.
            double sum = 0;
            double squares = 0;
            foreach (double weight in weights)
            {
                sum += weight;
                squares += weight * weight;
            }

            double mean = sum / m;
            double variance = 2 * (squares - (m * mean * mean)) / (m * (m + 2.0));
            cumulative = variance > 0
                ? ContinuousDistributions.NormalCdf((d - mean) / Math.Sqrt(variance), 0, 1)
                : double.NaN;
        }

        double p = tail switch
        {
            Tail.Right => cumulative,
            Tail.Left => 1 - cumulative,
            _ => Math.Min(1, 2 * Math.Min(cumulative, 1 - cumulative)),
        };

        return new SerialCorrelation(Math.Clamp(p, 0, 1), d, exact);
    }

    /// <summary>
    /// <c>linhyptest</c>: whether <c>H·β = c</c>, given the coefficients and their covariance. Only as
    /// many restrictions as <c>H</c> has independent rows are really being tested, and that rank is
    /// what the statistic is divided by.
    /// </summary>
    /// <param name="beta">The fitted coefficients.</param>
    /// <param name="covariance">Their covariance matrix.</param>
    /// <param name="c">What the combinations are hypothesized to equal.</param>
    /// <param name="h">The combinations, one restriction per row.</param>
    /// <param name="errorDf">
    /// The model's residual degrees of freedom. Infinite — or unknown — makes this a chi-square test,
    /// because there is then nothing estimated for the denominator to account for.
    /// </param>
    public static LinearHypothesis Linear(
        double[] beta, double[,] covariance, double[] c, double[,] h, double errorDf)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(covariance);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(h);

        int restrictions = h.GetLength(0);
        int terms = beta.Length;
        if (h.GetLength(1) != terms || covariance.GetLength(0) != terms || covariance.GetLength(1) != terms)
        {
            throw new ArgumentException(
                "the restrictions and the covariance must both be written in terms of every coefficient.");
        }

        if (c.Length != restrictions)
        {
            throw new ArgumentException("there must be one hypothesized value per restriction.");
        }

        var gap = new double[restrictions];
        for (int r = 0; r < restrictions; r++)
        {
            double value = -c[r];
            for (int j = 0; j < terms; j++)
            {
                value += h[r, j] * beta[j];
            }

            gap[r] = value;
        }

        var middle = new double[restrictions, restrictions];
        for (int a = 0; a < restrictions; a++)
        {
            for (int b = 0; b < restrictions; b++)
            {
                double sum = 0;
                for (int i = 0; i < terms; i++)
                {
                    for (int j = 0; j < terms; j++)
                    {
                        sum += h[a, i] * covariance[i, j] * h[b, j];
                    }
                }

                middle[a, b] = sum;
            }
        }

        // A redundant restriction — one implied by the others — makes the middle matrix singular, so
        // the inverse has to be the pseudo-inverse and the divisor the rank rather than the row count.
        Svd svd = Svd.Factor(middle);
        double largest = svd.Values.Length > 0 ? svd.Values[0] : 0;
        double tolerance = restrictions * 2.220446049250313e-16 * largest;
        int rank = 0;
        double quadratic = 0;
        for (int i = 0; i < svd.Values.Length; i++)
        {
            if (svd.Values[i] <= tolerance)
            {
                continue;
            }

            rank++;
            double projection = 0;
            for (int j = 0; j < restrictions; j++)
            {
                projection += svd.U[j, i] * gap[j];
            }

            quadratic += projection * projection / svd.Values[i];
        }

        if (rank == 0)
        {
            throw new ArgumentException("the restrictions carry no information, so there is nothing to test.");
        }

        double f = quadratic / rank;
        double p = double.IsFinite(errorDf) && errorDf > 0
            ? 1 - ContinuousDistributions.FCdf(f, rank, errorDf)
            : 1 - ContinuousDistributions.Chi2Cdf(quadratic, rank);
        return new LinearHypothesis(Math.Clamp(p, 0, 1), f, rank);
    }

    /// <summary>
    /// <c>barttest</c>: how many principal components a set of variables really needs. Each candidate
    /// dimension is tested by asking whether the variance left over beyond it is spread equally in
    /// every remaining direction, which is what "no further structure" means.
    /// </summary>
    /// <param name="observations">One row per observation, one column per variable.</param>
    /// <param name="alpha">The level at which a dimension is accepted.</param>
    public static Dimensionality Bartlett(double[,] observations, double alpha)
    {
        ArgumentNullException.ThrowIfNull(observations);
        int n = observations.GetLength(0);
        int p = observations.GetLength(1);
        if (p < 2 || n < p + 2)
        {
            throw new ArgumentException(
                "Bartlett's dimensionality test needs at least two variables and more observations than variables.");
        }

        var means = new double[p];
        for (int j = 0; j < p; j++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += observations[i, j];
            }

            means[j] = sum / n;
        }

        var covariance = new double[p, p];
        for (int a = 0; a < p; a++)
        {
            for (int b = 0; b < p; b++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    sum += (observations[i, a] - means[a]) * (observations[i, b] - means[b]);
                }

                covariance[a, b] = sum / (n - 1);
            }
        }

        Eigen eigen = Eigen.Factor(covariance);
        var values = new double[p];
        for (int i = 0; i < p; i++)
        {
            values[i] = eigen.Values[i].Real;
        }

        Array.Sort(values);
        Array.Reverse(values);

        var probabilities = new double[p - 1];
        var statistics = new double[p - 1];
        int dimension = p - 1;

        for (int k = 0; k < p - 1; k++)
        {
            int remaining = p - k;
            double sum = 0;
            double logs = 0;
            bool degenerate = false;
            for (int i = k; i < p; i++)
            {
                if (!(values[i] > 0))
                {
                    degenerate = true;
                    break;
                }

                sum += values[i];
                logs += Math.Log(values[i]);
            }

            if (degenerate)
            {
                statistics[k] = double.NaN;
                probabilities[k] = double.NaN;
                continue;
            }

            double statistic = (n - 1 - (((2.0 * p) + 11) / 6))
                * ((remaining * Math.Log(sum / remaining)) - logs);
            double df = (remaining - 1) * (remaining + 2) / 2.0;
            statistics[k] = statistic;
            probabilities[k] = 1 - ContinuousDistributions.Chi2Cdf(statistic, df);

            if (probabilities[k] > alpha && dimension == p - 1)
            {
                dimension = k;
            }
        }

        return new Dimensionality(dimension, probabilities, statistics);
    }

    /// <summary>An orthonormal basis of the directions the design matrix does not reach.</summary>
    private static double[,] OrthogonalComplement(double[,] design, int n, int k)
    {
        double[,] q = QrDecomposition.Factor(design).FullQ;
        int width = n - k;
        var basis = new double[n, width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < n; r++)
            {
                basis[r, c] = q[r, k + c];
            }
        }

        return basis;
    }

    /// <summary>
    /// The second-difference quadratic form expressed in the given basis: <c>Qᵀ·A·Q</c>, where A is
    /// the tridiagonal matrix that turns a vector into its sum of squared consecutive differences.
    /// </summary>
    private static double[,] Projected(double[,] basis, int width, int n)
    {
        // A·q, column by column, without ever forming A: its action is the negated second difference,
        // with the two ends carrying one neighbour instead of two.
        var applied = new double[n, width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < n; r++)
            {
                double value = basis[r, c] * (r == 0 || r == n - 1 ? 1 : 2);
                if (r > 0)
                {
                    value -= basis[r - 1, c];
                }

                if (r < n - 1)
                {
                    value -= basis[r + 1, c];
                }

                applied[r, c] = value;
            }
        }

        var projected = new double[width, width];
        for (int a = 0; a < width; a++)
        {
            for (int b = a; b < width; b++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    sum += basis[i, a] * applied[i, b];
                }

                projected[a, b] = sum;
                projected[b, a] = sum;
            }
        }

        return projected;
    }
}

/// <summary>
/// Imhof's inversion: the probability that a weighted sum of independent squared normals is below
/// zero, computed by integrating the imaginary part of its characteristic function.
/// </summary>
/// <remarks>
/// A ratio of quadratic forms — which is what the Durbin–Watson statistic is — has no distribution
/// with a name, but the event "the ratio is below <c>d</c>" is exactly the event "a particular
/// weighted sum of squares is below zero", and that has an exact integral. The integrand oscillates
/// and decays algebraically, so the infinite range is mapped onto the unit interval and given the
/// same smoothing substitution the multivariate probabilities use, which is what stops the edge of the
/// interval costing several digits.
/// </remarks>
public static class Imhof
{
    private const int Nodes = 128;
    private const int Panels = 24;

    /// <summary>The probability that <c>Σ weightsᵢ · χ²₁</c> is below zero.</summary>
    public static double BelowZero(double[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length == 0)
        {
            throw new ArgumentException("a weighted sum of squares needs at least one weight.");
        }

        double integral = GaussLegendre.Integrate(
            t =>
            {
                // The smoothstep flattens the integrand against both ends of the interval before the
                // map to (0, ∞) stretches it, which is what makes a fixed rule converge here.
                double smooth = t * t * t * (((6 * t) - 15) * t + 10);
                double slope = 30 * t * t * (1 - t) * (1 - t);
                if (smooth <= 0 || smooth >= 1)
                {
                    return 0;
                }

                double u = smooth / (1 - smooth);
                double jacobian = slope / ((1 - smooth) * (1 - smooth));

                double angle = 0;
                double logModulus = 0;
                foreach (double weight in weights)
                {
                    double scaled = weight * u;
                    angle += Math.Atan(scaled);
                    logModulus += double.LogP1(scaled * scaled);
                }

                angle /= 2;
                logModulus /= 4;
                return Math.Sin(angle) / u * Math.Exp(-logModulus) * jacobian;
            },
            0,
            1,
            Nodes,
            Panels);

        return Math.Clamp(0.5 - (integral / Math.PI), 0, 1);
    }
}
