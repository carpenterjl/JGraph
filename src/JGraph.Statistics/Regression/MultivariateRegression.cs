using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Regression;

/// <summary>
/// <c>mvregress</c> and <c>mvregresslike</c>: several responses fitted together, so that what they
/// leave over is allowed to be correlated.
/// </summary>
/// <remarks>
/// <para>
/// Fitting each response on its own throws away the fact that the errors move together. Here the
/// coefficients and the error covariance are estimated in turn — coefficients by generalized least
/// squares given the covariance, covariance from the residuals given the coefficients — until neither
/// moves. When every response has the same design matrix that loop converges immediately to the same
/// answer as fitting them separately, which is a useful thing to know and a useful thing to test.
/// </para>
/// <para>
/// One design per observation is the general form, and it is the only one implemented: a single design
/// shared by all the responses is expressed as that form with each observation's design built once,
/// which is why there is one estimator here rather than two.
/// </para>
/// </remarks>
public static class MultivariateRegression
{
    /// <summary>What a multivariate fit produced.</summary>
    /// <param name="Coefficients">The fitted coefficients.</param>
    /// <param name="Covariance">The error covariance across responses.</param>
    /// <param name="Residuals">One row per observation, one column per response.</param>
    /// <param name="CoefficientCovariance">The coefficients' own covariance.</param>
    /// <param name="LogLikelihood">The log-likelihood at the answer.</param>
    /// <param name="Iterations">Iterations taken.</param>
    /// <param name="Converged">Whether it settled before the budget ran out.</param>
    public readonly record struct MultivariateFit(
        double[] Coefficients,
        double[,] Covariance,
        double[,] Residuals,
        double[,] CoefficientCovariance,
        double LogLikelihood,
        int Iterations,
        bool Converged);

    /// <summary>Builds the per-observation designs a single shared design matrix stands for.</summary>
    /// <param name="design">One row per observation, one column per predictor.</param>
    /// <param name="responses">How many responses each observation carries.</param>
    /// <returns>
    /// One design per observation, each with a row per response and a column per coefficient. The
    /// coefficients run response by response, so reshaping them into a matrix puts one response in
    /// each column.
    /// </returns>
    public static double[][,] Expand(double[,] design, int responses)
    {
        ArgumentNullException.ThrowIfNull(design);

        int n = design.GetLength(0);
        int p = design.GetLength(1);
        var expanded = new double[n][,];
        for (int i = 0; i < n; i++)
        {
            var block = new double[responses, p * responses];
            for (int d = 0; d < responses; d++)
            {
                for (int c = 0; c < p; c++)
                {
                    block[d, (d * p) + c] = design[i, c];
                }
            }

            expanded[i] = block;
        }

        return expanded;
    }

    /// <summary>Fits the model by alternating generalized least squares with a covariance estimate.</summary>
    /// <param name="designs">One design per observation, each with a row per response.</param>
    /// <param name="responses">One row per observation, one column per response.</param>
    /// <param name="maxIterations">Iterations allowed, or zero for a hundred.</param>
    /// <param name="tolerance">How small a coefficient move counts as settled.</param>
    public static MultivariateFit Fit(
        IReadOnlyList<double[,]> designs, double[,] responses, int maxIterations, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(designs);
        ArgumentNullException.ThrowIfNull(responses);

        int n = designs.Count;
        if (responses.GetLength(0) != n)
        {
            throw new ArgumentException(
                $"there are {designs.Count} designs for {responses.GetLength(0)} observations.", nameof(designs));
        }

        int d = responses.GetLength(1);
        int k = designs[0].GetLength(1);
        foreach (double[,] block in designs)
        {
            if (block.GetLength(0) != d || block.GetLength(1) != k)
            {
                throw new ArgumentException(
                    $"every design must have {d} rows and {k} columns.", nameof(designs));
            }
        }

        int budget = maxIterations > 0 ? maxIterations : 100;
        double settled = tolerance > 0 ? tolerance : 1e-8;

        var sigma = new double[d, d];
        for (int a = 0; a < d; a++)
        {
            sigma[a, a] = 1;
        }

        var beta = new double[k];
        var residuals = new double[n, d];
        double[,] information = new double[k, k];
        bool converged = false;
        int iteration = 0;
        for (iteration = 1; iteration <= budget; iteration++)
        {
            double[,] precision = LuDecomposition.Factor(sigma).Inverse();
            information = new double[k, k];
            var right = new double[k];
            for (int i = 0; i < n; i++)
            {
                double[,] block = designs[i];
                for (int a = 0; a < k; a++)
                {
                    for (int row = 0; row < d; row++)
                    {
                        double weighted = 0;
                        for (int c = 0; c < d; c++)
                        {
                            weighted += precision[row, c] * responses[i, c];
                        }

                        right[a] += block[row, a] * weighted;
                    }

                    for (int b = 0; b < k; b++)
                    {
                        double value = 0;
                        for (int row = 0; row < d; row++)
                        {
                            for (int c = 0; c < d; c++)
                            {
                                value += block[row, a] * precision[row, c] * block[c, b];
                            }
                        }

                        information[a, b] += value;
                    }
                }
            }

            double[] next = LuDecomposition.Factor(information).Solve(right);
            double movement = 0, size = 0;
            for (int a = 0; a < k; a++)
            {
                movement = Math.Max(movement, Math.Abs(next[a] - beta[a]));
                size = Math.Max(size, Math.Abs(next[a]));
            }

            beta = next;
            var updated = new double[d, d];
            for (int i = 0; i < n; i++)
            {
                double[,] block = designs[i];
                for (int row = 0; row < d; row++)
                {
                    double fitted = 0;
                    for (int a = 0; a < k; a++)
                    {
                        fitted += block[row, a] * beta[a];
                    }

                    residuals[i, row] = responses[i, row] - fitted;
                }

                for (int a = 0; a < d; a++)
                {
                    for (int b = 0; b < d; b++)
                    {
                        updated[a, b] += residuals[i, a] * residuals[i, b] / n;
                    }
                }
            }

            sigma = updated;
            if (movement <= settled * (1 + size))
            {
                converged = true;
                break;
            }
        }

        double logLikelihood = LogLikelihood(designs, responses, beta, sigma);
        double[,] coefficientCovariance = LuDecomposition.Factor(information).Inverse();
        return new MultivariateFit(
            beta, sigma, residuals, coefficientCovariance, logLikelihood, Math.Min(iteration, budget),
            converged);
    }

    /// <summary><c>mvregresslike</c>: the negative log-likelihood at stated parameters.</summary>
    public static double LogLikelihood(
        IReadOnlyList<double[,]> designs, double[,] responses, double[] beta, double[,] sigma)
    {
        ArgumentNullException.ThrowIfNull(designs);
        ArgumentNullException.ThrowIfNull(responses);
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(sigma);

        int n = designs.Count;
        int d = sigma.GetLength(0);
        LuDecomposition factored = LuDecomposition.Factor(sigma);
        double determinant = factored.Determinant;
        if (determinant <= 0)
        {
            return double.NegativeInfinity;
        }

        double[,] precision = factored.Inverse();
        double total = -0.5 * n * d * Math.Log(2 * Math.PI);
        total -= 0.5 * n * Math.Log(determinant);
        for (int i = 0; i < n; i++)
        {
            double[,] block = designs[i];
            var residual = new double[d];
            for (int row = 0; row < d; row++)
            {
                double fitted = 0;
                for (int a = 0; a < beta.Length; a++)
                {
                    fitted += block[row, a] * beta[a];
                }

                residual[row] = responses[i, row] - fitted;
            }

            for (int a = 0; a < d; a++)
            {
                for (int b = 0; b < d; b++)
                {
                    total -= 0.5 * residual[a] * precision[a, b] * residual[b];
                }
            }
        }

        return total;
    }

    /// <summary>The coefficients' covariance at stated parameters, from the information matrix.</summary>
    public static double[,] CoefficientCovariance(
        IReadOnlyList<double[,]> designs, double[,] sigma, int coefficients)
    {
        ArgumentNullException.ThrowIfNull(designs);
        ArgumentNullException.ThrowIfNull(sigma);

        int d = sigma.GetLength(0);
        double[,] precision = LuDecomposition.Factor(sigma).Inverse();
        var information = new double[coefficients, coefficients];
        foreach (double[,] block in designs)
        {
            for (int a = 0; a < coefficients; a++)
            {
                for (int b = 0; b < coefficients; b++)
                {
                    double value = 0;
                    for (int row = 0; row < d; row++)
                    {
                        for (int c = 0; c < d; c++)
                        {
                            value += block[row, a] * precision[row, c] * block[c, b];
                        }
                    }

                    information[a, b] += value;
                }
            }
        }

        return LuDecomposition.Factor(information).Inverse();
    }

    /// <summary>
    /// The covariance of the error covariance's own free elements, which the full variance format
    /// appends below the coefficients' block.
    /// </summary>
    /// <remarks>
    /// The coefficients and the covariance are asymptotically independent for a normal model, so the
    /// full matrix really is block diagonal rather than merely treated as such.
    /// </remarks>
    public static double[,] CovarianceOfCovariance(double[,] sigma, int observations)
    {
        ArgumentNullException.ThrowIfNull(sigma);

        int d = sigma.GetLength(0);
        var pairs = new List<(int A, int B)>();
        for (int b = 0; b < d; b++)
        {
            for (int a = b; a < d; a++)
            {
                pairs.Add((a, b));
            }
        }

        var covariance = new double[pairs.Count, pairs.Count];
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int j = 0; j < pairs.Count; j++)
            {
                (int a, int b) = pairs[i];
                (int c, int e) = pairs[j];
                covariance[i, j] =
                    ((sigma[a, c] * sigma[b, e]) + (sigma[a, e] * sigma[b, c])) / observations;
            }
        }

        return covariance;
    }
}
