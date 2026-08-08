using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// The multivariate normal and multivariate t distributions: their densities, their rectangle
/// probabilities, and draws from them.
/// </summary>
/// <remarks>
/// <para>
/// A multivariate distribution function is an integral with no closed form above one dimension, so the
/// question is not which formula to use but which quadrature. Two dimensions have an exact reduction —
/// differentiating the bivariate normal probability with respect to the correlation collapses the
/// double integral to a single one over an angle — and that is used wherever it applies, because it is
/// worth several digits over anything numerical.
/// </para>
/// <para>
/// Above two dimensions the integral is put through Genz's transformation, which turns the correlated
/// orthant into a smooth, bounded integrand over the unit cube of one fewer dimension, and that cube is
/// integrated with a tensor Gauss–Legendre rule. Two consequences are visible from a script: the answer
/// is deterministic — the same arguments give the same digits every time, which a Monte Carlo rule
/// would not — and the cost grows as a power of the dimension, which is why the dimension is capped
/// rather than allowed to run into an hour-long call.
/// </para>
/// </remarks>
public static class Multivariate
{
    /// <summary>The most variables a normal rectangle probability is integrated over.</summary>
    public const int MaximumNormalDimension = 5;

    /// <summary>
    /// The most variables a t rectangle probability is integrated over. One lower than the normal's,
    /// because the scaling variable that turns a normal into a t is one more dimension of quadrature.
    /// </summary>
    public const int MaximumTDimension = 4;

    // Nodes per dimension, indexed by how many dimensions are left to integrate. A smooth integrand
    // over a box needs far fewer points per dimension than a rough one, and these counts were chosen
    // against the cases whose answers are known in closed form — the equicorrelated orthants.
    private static readonly int[] NodesForDimensions = [0, 96, 48, 28, 20];

    /// <summary>
    /// A factor <c>T</c> of a covariance matrix with <c>Tᵀ·T = Σ</c>, which is what MATLAB's
    /// <c>cholcov</c> answers: the Cholesky factor where the matrix is positive definite, and a
    /// shorter factor read off the eigendecomposition where it is only semi-definite.
    /// </summary>
    /// <param name="sigma">The covariance, read as symmetric.</param>
    /// <returns>
    /// The factor and its number of rows, which is the rank; or null where the matrix has a negative
    /// eigenvalue and so is no covariance at all.
    /// </returns>
    public static (double[,] Factor, int Rank)? CovarianceFactor(double[,] sigma)
    {
        ArgumentNullException.ThrowIfNull(sigma);
        int n = sigma.GetLength(0);

        Cholesky cholesky = Cholesky.Factor(Symmetrized(sigma));
        if (cholesky.IsPositiveDefinite)
        {
            // Tᵀ·T = Σ wants the upper triangle, and Cholesky.Lower holds L with L·Lᵀ = Σ.
            var upper = new double[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    upper[r, c] = cholesky.Lower[c, r];
                }
            }

            return (upper, n);
        }

        // Not positive definite. It may still be a covariance — a singular one — and the eigenvalues
        // are what tell the two apart, so the tolerance is scaled by the largest of them the way a
        // rank test always is.
        Eigen eigen = Eigen.Factor(Symmetrized(sigma));
        var values = new double[n];
        double largest = 0;
        for (int i = 0; i < n; i++)
        {
            values[i] = eigen.Values[i].Real;
            largest = Math.Max(largest, Math.Abs(values[i]));
        }

        double tolerance = 10 * n * largest * 2.220446049250313e-16;
        var rows = new List<double[]>();
        for (int i = 0; i < n; i++)
        {
            if (values[i] < -tolerance)
            {
                return null;
            }

            if (values[i] <= tolerance)
            {
                continue;
            }

            double scale = Math.Sqrt(values[i]);
            var row = new double[n];
            for (int c = 0; c < n; c++)
            {
                row[c] = scale * eigen.Vectors[c, i].Real;
            }

            rows.Add(row);
        }

        var factor = new double[rows.Count, n];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < n; c++)
            {
                factor[r, c] = rows[r][c];
            }
        }

        return (factor, rows.Count);
    }

    /// <summary>The multivariate normal density at <paramref name="x"/>.</summary>
    /// <param name="x">The point, centred by the caller or not — <paramref name="mu"/> is subtracted here.</param>
    /// <param name="mu">The mean.</param>
    /// <param name="sigma">The covariance, which must be positive definite.</param>
    /// <exception cref="ArgumentException">The covariance is singular or not positive definite.</exception>
    public static double NormalPdf(double[] x, double[] mu, double[,] sigma)
    {
        int n = x.Length;
        Cholesky cholesky = Cholesky.Factor(Symmetrized(sigma));
        if (!cholesky.IsPositiveDefinite)
        {
            throw new ArgumentException("the covariance matrix must be positive definite.");
        }

        // z = L⁻¹(x − mu) by forward substitution: the quadratic form is zᵀz and the determinant is
        // the square of the product of the diagonal, so neither needs an inverse.
        var z = new double[n];
        double logDeterminant = 0;
        for (int i = 0; i < n; i++)
        {
            double sum = x[i] - mu[i];
            for (int j = 0; j < i; j++)
            {
                sum -= cholesky.Lower[i, j] * z[j];
            }

            z[i] = sum / cholesky.Lower[i, i];
            logDeterminant += 2 * Math.Log(cholesky.Lower[i, i]);
        }

        double quadratic = 0;
        foreach (double value in z)
        {
            quadratic += value * value;
        }

        return Math.Exp(-0.5 * (quadratic + logDeterminant + (n * Math.Log(2 * Math.PI))));
    }

    /// <summary>
    /// The multivariate t density at <paramref name="x"/> with correlation
    /// <paramref name="correlation"/> and <paramref name="df"/> degrees of freedom.
    /// </summary>
    /// <exception cref="ArgumentException">The correlation matrix is not positive definite.</exception>
    public static double TPdf(double[] x, double[,] correlation, double df)
    {
        int n = x.Length;
        Cholesky cholesky = Cholesky.Factor(Symmetrized(correlation));
        if (!cholesky.IsPositiveDefinite)
        {
            throw new ArgumentException(
                "the correlation matrix must be positive definite.");
        }

        var z = new double[n];
        double logDeterminant = 0;
        for (int i = 0; i < n; i++)
        {
            double sum = x[i];
            for (int j = 0; j < i; j++)
            {
                sum -= cholesky.Lower[i, j] * z[j];
            }

            z[i] = sum / cholesky.Lower[i, i];
            logDeterminant += 2 * Math.Log(cholesky.Lower[i, i]);
        }

        double quadratic = 0;
        foreach (double value in z)
        {
            quadratic += value * value;
        }

        double logDensity = SpecialFunctions.LogGamma((df + n) / 2)
            - SpecialFunctions.LogGamma(df / 2)
            - (n / 2.0 * Math.Log(df * Math.PI))
            - (0.5 * logDeterminant)
            - ((df + n) / 2 * double.LogP1(quadratic / df));

        return Math.Exp(logDensity);
    }

    /// <summary>
    /// One draw from the multivariate normal with mean <paramref name="mu"/> and the covariance whose
    /// factor is <paramref name="factor"/> — a matrix with <c>Tᵀ·T = Σ</c>, as
    /// <see cref="CovarianceFactor"/> answers.
    /// </summary>
    /// <remarks>
    /// The factor is taken rather than the covariance because a caller drawing a thousand rows should
    /// factor once; and because a singular covariance draws perfectly well from a short factor, which
    /// is the whole reason <c>cholcov</c> exists.
    /// </remarks>
    public static double[] NormalSample(Random random, double[] mu, double[,] factor)
    {
        int rank = factor.GetLength(0);
        int n = factor.GetLength(1);
        var normals = new double[rank];
        for (int i = 0; i < rank; i++)
        {
            normals[i] = ContinuousDistributions.StandardNormal(random);
        }

        var draw = new double[n];
        for (int c = 0; c < n; c++)
        {
            double sum = mu[c];
            for (int r = 0; r < rank; r++)
            {
                sum += normals[r] * factor[r, c];
            }

            draw[c] = sum;
        }

        return draw;
    }

    /// <summary>
    /// One draw from the multivariate t with the given correlation factor and degrees of freedom: a
    /// centred normal draw divided by the square root of a scaled chi-square.
    /// </summary>
    public static double[] TSample(Random random, double[,] factor, double df)
    {
        int n = factor.GetLength(1);
        double[] normal = NormalSample(random, new double[n], factor);
        double scale = Math.Sqrt(df / ContinuousDistributions.SampleGamma(random, df / 2, 2));
        for (int i = 0; i < n; i++)
        {
            normal[i] *= scale;
        }

        return normal;
    }

    /// <summary>
    /// One draw from the Wishart distribution with <paramref name="df"/> degrees of freedom and the
    /// covariance whose factor is <paramref name="factor"/>, so the draw has mean <c>df·Σ</c>.
    /// </summary>
    /// <remarks>
    /// Bartlett's decomposition: a lower triangular matrix with chi-square roots down the diagonal and
    /// standard normals below it has exactly the distribution of the Cholesky factor of a standard
    /// Wishart, so one d² -element draw replaces the df sums of outer products the definition asks for
    /// — and it works for a fractional degree of freedom, which the definition cannot.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The degrees of freedom are too few for the number of variables, and not a whole number either.
    /// </exception>
    public static double[,] WishartSample(Random random, double[,] factor, double df)
    {
        ArgumentNullException.ThrowIfNull(random);
        int n = factor.GetLength(1);
        var square = new double[n, n];

        if (df > n - 1)
        {
            var bartlett = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                bartlett[i, i] = Math.Sqrt(ContinuousDistributions.SampleGamma(random, (df - i) / 2, 2));
                for (int j = 0; j < i; j++)
                {
                    bartlett[i, j] = ContinuousDistributions.StandardNormal(random);
                }
            }

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c <= r; c++)
                {
                    double sum = 0;
                    for (int k = 0; k <= Math.Min(r, c); k++)
                    {
                        sum += bartlett[r, k] * bartlett[c, k];
                    }

                    square[r, c] = sum;
                    square[c, r] = sum;
                }
            }
        }
        else if (df == Math.Floor(df) && df > 0)
        {
            // Too few degrees of freedom for Bartlett's diagonal to exist, but a whole number of them
            // still has the definition to fall back on: the draw is singular, and correctly so.
            var zeros = new double[n];
            for (int draw = 0; draw < (int)df; draw++)
            {
                double[] normal = NormalSample(random, zeros, IdentityFactor(n));
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        square[r, c] += normal[r] * normal[c];
                    }
                }
            }
        }
        else
        {
            throw new ArgumentException(
                $"a Wishart draw over {n} variables needs more than {n - 1} degrees of freedom, "
                + "or a whole number of them.");
        }

        // W = Tᵀ·(A·Aᵀ)·T, which has mean df·Tᵀ·T = df·Σ.
        var scaled = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int a = 0; a < factor.GetLength(0); a++)
                {
                    for (int b = 0; b < factor.GetLength(0); b++)
                    {
                        sum += factor[a, r] * square[a, b] * factor[b, c];
                    }
                }

                scaled[r, c] = sum;
            }
        }

        return scaled;
    }

    /// <summary>
    /// The inverse of a symmetric positive definite matrix, through its Cholesky factor.
    /// </summary>
    /// <exception cref="ArgumentException">The matrix is singular or not positive definite.</exception>
    public static double[,] SymmetricInverse(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        Cholesky cholesky = Cholesky.Factor(Symmetrized(matrix));
        if (!cholesky.IsPositiveDefinite)
        {
            throw new ArgumentException("the matrix must be positive definite to be inverted here.");
        }

        var inverse = new double[n, n];
        var column = new double[n];
        for (int e = 0; e < n; e++)
        {
            // Forward substitution on L, then back substitution on Lᵀ, one unit vector at a time.
            for (int i = 0; i < n; i++)
            {
                double sum = i == e ? 1 : 0;
                for (int j = 0; j < i; j++)
                {
                    sum -= cholesky.Lower[i, j] * column[j];
                }

                column[i] = sum / cholesky.Lower[i, i];
            }

            for (int i = n - 1; i >= 0; i--)
            {
                double sum = column[i];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= cholesky.Lower[j, i] * inverse[j, e];
                }

                inverse[i, e] = sum / cholesky.Lower[i, i];
            }
        }

        return inverse;
    }

    private static double[,] IdentityFactor(int n)
    {
        var identity = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = 1;
        }

        return identity;
    }

    /// <summary>
    /// The probability that a centred multivariate normal with covariance <paramref name="sigma"/>
    /// falls in the box between <paramref name="lower"/> and <paramref name="upper"/>, either of which
    /// may hold infinities.
    /// </summary>
    /// <returns>The probability and an estimate of how far it may be out.</returns>
    /// <exception cref="ArgumentException">
    /// The covariance is not positive definite, or there are more variables than
    /// <see cref="MaximumNormalDimension"/>.
    /// </exception>
    public static (double Probability, double Error) NormalCdf(double[] lower, double[] upper, double[,] sigma)
    {
        int n = lower.Length;
        if (n > MaximumNormalDimension)
        {
            throw new ArgumentException(
                $"a normal probability over more than {MaximumNormalDimension} variables is not computed here.");
        }

        for (int i = 0; i < n; i++)
        {
            if (lower[i] >= upper[i])
            {
                return (0, 0);
            }
        }

        if (n == 1)
        {
            double sd = Math.Sqrt(sigma[0, 0]);
            double p = ContinuousDistributions.NormalCdf(upper[0], 0, sd)
                - ContinuousDistributions.NormalCdf(lower[0], 0, sd);
            return (Math.Clamp(p, 0, 1), 0);
        }

        if (n == 2)
        {
            double s1 = Math.Sqrt(sigma[0, 0]);
            double s2 = Math.Sqrt(sigma[1, 1]);
            double r = sigma[0, 1] / (s1 * s2);
            double p = BivariateRectangle(
                lower[0] / s1, upper[0] / s1, lower[1] / s2, upper[1] / s2, r);
            return (Math.Clamp(p, 0, 1), 0);
        }

        Cholesky cholesky = Cholesky.Factor(Symmetrized(sigma));
        if (!cholesky.IsPositiveDefinite)
        {
            throw new ArgumentException(
                "the covariance matrix must be positive definite.");
        }

        return Refined(nodes => TransformedIntegral(lower, upper, cholesky.Lower, df: null, nodes), n - 1);
    }

    /// <summary>
    /// The probability that a centred multivariate t with correlation <paramref name="correlation"/>
    /// and <paramref name="df"/> degrees of freedom falls in the box between <paramref name="lower"/>
    /// and <paramref name="upper"/>.
    /// </summary>
    /// <returns>The probability and an estimate of how far it may be out.</returns>
    /// <exception cref="ArgumentException">
    /// The correlation is not positive definite, or there are more variables than
    /// <see cref="MaximumTDimension"/>.
    /// </exception>
    public static (double Probability, double Error) TCdf(
        double[] lower, double[] upper, double[,] correlation, double df)
    {
        int n = lower.Length;
        if (n > MaximumTDimension)
        {
            throw new ArgumentException(
                $"a t probability over more than {MaximumTDimension} variables is not computed here.");
        }

        for (int i = 0; i < n; i++)
        {
            if (lower[i] >= upper[i])
            {
                return (0, 0);
            }
        }

        if (n == 1)
        {
            double p = ContinuousDistributions.TCdf(upper[0], df) - ContinuousDistributions.TCdf(lower[0], df);
            return (Math.Clamp(p, 0, 1), 0);
        }

        if (n == 2)
        {
            // The scaling variable is one integral and the inner probability is exact, which is a far
            // better use of the same work than putting both dimensions through the same rule.
            double r = correlation[0, 1];
            double Inner(double u)
            {
                double scale = ScaleFor(u, df);
                return double.IsInfinity(scale)
                    ? 0
                    : BivariateRectangle(
                        lower[0] * scale, upper[0] * scale, lower[1] * scale, upper[1] * scale, r);
            }

            double fine = SmoothIntegral(Inner, 96);
            double coarse = SmoothIntegral(Inner, 48);
            return (Math.Clamp(fine, 0, 1), Math.Abs(fine - coarse));
        }

        Cholesky cholesky = Cholesky.Factor(Symmetrized(correlation));
        if (!cholesky.IsPositiveDefinite)
        {
            throw new ArgumentException(
                "the correlation matrix must be positive definite.");
        }

        return Refined(nodes => TransformedIntegral(lower, upper, cholesky.Lower, df, nodes), n);
    }

    /// <summary>
    /// <c>P(X ≤ h, Y ≤ k)</c> for a standard bivariate normal with correlation <paramref name="r"/>.
    /// </summary>
    /// <remarks>
    /// Differentiating the probability with respect to the correlation gives the bivariate density, so
    /// the probability is the independent product plus the integral of that density from zero
    /// correlation up to this one. Substituting the sine of an angle for the correlation clears the
    /// square root, leaving one smooth integral over a finite range — and at the origin the integrand
    /// is exactly one, which is why this reproduces the orthant formula ¼ + asin(r)/2π to the last
    /// digit.
    /// </remarks>
    public static double BivariateNormalCdf(double h, double k, double r)
    {
        if (double.IsNegativeInfinity(h) || double.IsNegativeInfinity(k))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(h) && double.IsPositiveInfinity(k))
        {
            return 1;
        }

        if (double.IsPositiveInfinity(h))
        {
            return ContinuousDistributions.NormalCdf(k, 0, 1);
        }

        if (double.IsPositiveInfinity(k))
        {
            return ContinuousDistributions.NormalCdf(h, 0, 1);
        }

        r = Math.Clamp(r, -1, 1);
        double independent = ContinuousDistributions.NormalCdf(h, 0, 1)
            * ContinuousDistributions.NormalCdf(k, 0, 1);
        if (r == 0)
        {
            return independent;
        }

        double hs = ((h * h) + (k * k)) / 2;
        double hk = h * k;

        double Integrand(double theta)
        {
            double sine = Math.Sin(theta);
            double cosineSquared = 1 - (sine * sine);
            if (cosineSquared <= 0)
            {
                return 0;
            }

            return Math.Exp((hk * sine - hs) / cosineSquared);
        }

        // Panels rather than a longer single rule: as the correlation approaches one the integrand
        // piles up against the far end of the range, and equal panels resolve that where extra nodes
        // spread over the whole range would not.
        double integral = GaussLegendre.Integrate(Integrand, 0, Math.Asin(r), 48, panels: 4);
        return Math.Clamp(independent + (integral / (2 * Math.PI)), 0, 1);
    }

    private static double BivariateRectangle(double a1, double b1, double a2, double b2, double r) =>
        BivariateNormalCdf(b1, b2, r)
        - BivariateNormalCdf(a1, b2, r)
        - BivariateNormalCdf(b1, a2, r)
        + BivariateNormalCdf(a1, a2, r);

    /// <summary>
    /// Integrates over the unit interval after the same edge-flattening substitution the cube uses, so
    /// a limit at infinity does not cost the rule its accuracy.
    /// </summary>
    private static double SmoothIntegral(Func<double, double> f, int nodes) =>
        GaussLegendre.Integrate(
            t => f(t * t * t * (((6 * t) - 15) * t + 10)) * 30 * t * t * (1 - t) * (1 - t), 0, 1, nodes);

    /// <summary>
    /// Runs <paramref name="integrate"/> at the chosen order and again at half of it, and reports the
    /// finer answer with the gap between them as the error. Two rules of different order agreeing is
    /// the only evidence a deterministic quadrature can offer about its own accuracy.
    /// </summary>
    private static (double Probability, double Error) Refined(Func<int, double> integrate, int dimensions)
    {
        int nodes = NodesForDimensions[dimensions];
        double fine = integrate(nodes);
        double coarse = integrate(Math.Max(4, nodes / 2));
        return (Math.Clamp(fine, 0, 1), Math.Abs(fine - coarse));
    }

    /// <summary>
    /// Genz's transformation, integrated by a tensor Gauss–Legendre rule.
    /// </summary>
    /// <remarks>
    /// Each variable in turn is conditioned on the ones before it, so the correlated box becomes a
    /// product of one-dimensional normal probabilities whose limits depend on the earlier variables.
    /// The remaining integral is over the unit cube, its integrand is bounded by one, and it is smooth
    /// wherever the limits are finite. Passing a degree of freedom prepends one more variable: the
    /// scale that turns the normal into a t.
    /// </remarks>
    private static double TransformedIntegral(
        double[] lower, double[] upper, double[,] cholesky, double? df, int nodesPerDimension)
    {
        int n = lower.Length;
        int cube = df is null ? n - 1 : n;
        (double[] nodes, double[] weights) = GaussLegendre.Rule(nodesPerDimension);

        var index = new int[cube];
        var point = new double[cube];
        var conditioned = new double[n];
        double total = 0;

        while (true)
        {
            double weight = 1;
            for (int d = 0; d < cube; d++)
            {
                // Gauss–Legendre lives on (−1, 1) and the cube is (0, 1); the second substitution is
                // what makes the rule converge. A limit at infinity leaves the transformed integrand
                // with an unbounded slope at the edge of the cube, which costs several digits;
                // reparameterizing so that the first two derivatives vanish at both edges buys them
                // back, and does nothing to a case that was already smooth.
                double t = 0.5 * (nodes[index[d]] + 1);
                point[d] = t * t * t * (((6 * t) - 15) * t + 10);
                weight *= weights[index[d]] / 2 * 30 * t * t * (1 - t) * (1 - t);
            }

            total += weight * Integrand(point);

            int carry = 0;
            while (carry < cube && ++index[carry] == nodesPerDimension)
            {
                index[carry] = 0;
                carry++;
            }

            if (carry == cube)
            {
                break;
            }
        }

        return total;

        double Integrand(double[] w)
        {
            double scale = 1;
            int offset = 0;
            if (df is double freedom)
            {
                scale = ScaleFor(w[0], freedom);
                if (double.IsInfinity(scale) || scale == 0)
                {
                    return 0;
                }

                offset = 1;
            }

            double product = 1;
            for (int i = 0; i < n; i++)
            {
                double shift = 0;
                for (int j = 0; j < i; j++)
                {
                    shift += cholesky[i, j] * conditioned[j];
                }

                double diagonal = cholesky[i, i];
                double e = double.IsNegativeInfinity(lower[i])
                    ? 0
                    : ContinuousDistributions.NormalCdf(((lower[i] * scale) - shift) / diagonal, 0, 1);
                double f = double.IsPositiveInfinity(upper[i])
                    ? 1
                    : ContinuousDistributions.NormalCdf(((upper[i] * scale) - shift) / diagonal, 0, 1);

                product *= f - e;
                if (product <= 0)
                {
                    return 0;
                }

                if (i < n - 1)
                {
                    double u = Math.Clamp(e + (w[i + offset] * (f - e)), 1e-300, 1 - 1e-16);
                    conditioned[i] = ContinuousDistributions.NormalInv(u, 0, 1);
                }
            }

            return product;
        }
    }

    /// <summary>
    /// The multiplier that turns a t limit into the normal limit it is equivalent to, at the
    /// <paramref name="u"/>-th quantile of the scaling chi-square.
    /// </summary>
    private static double ScaleFor(double u, double df)
    {
        double chiSquare = ContinuousDistributions.GammaInv(Math.Clamp(u, 1e-300, 1 - 1e-16), df / 2, 2);
        return Math.Sqrt(chiSquare / df);
    }

    /// <summary>A copy with the two triangles averaged, so a caller's rounding cannot make it asymmetric.</summary>
    private static double[,] Symmetrized(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var copy = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                copy[r, c] = (matrix[r, c] + matrix[c, r]) / 2;
            }
        }

        return copy;
    }
}
