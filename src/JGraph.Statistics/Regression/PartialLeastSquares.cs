using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Regression;

/// <summary>
/// <c>plsregress</c>: regression through a small number of directions chosen to explain the response
/// rather than only the predictors.
/// </summary>
/// <remarks>
/// <para>
/// Principal components find the directions in which the predictors themselves vary most, which is no
/// use if the response happens to depend on a direction they barely move in. Partial least squares
/// instead takes, at each step, the direction of the predictors most strongly covarying with the
/// response, projects it out of both, and repeats. With as many components as predictors it reproduces
/// ordinary least squares exactly; with fewer it is a regression that works where there are more
/// predictors than observations.
/// </para>
/// <para>
/// The implementation is de Jong's SIMPLS, which extracts each direction from the cross-product of the
/// centred predictors and response directly instead of deflating the data matrices themselves. That is
/// why one singular vector per component is the whole cost.
/// </para>
/// </remarks>
public static class PartialLeastSquares
{
    /// <summary>What a partial least squares fit produced.</summary>
    /// <param name="XLoadings">How each predictor loads on each component.</param>
    /// <param name="YLoadings">How each response loads on each component.</param>
    /// <param name="XScores">Each observation's position along each component.</param>
    /// <param name="YScores">The response's projection onto each component.</param>
    /// <param name="Weights">The combination of predictors each component is.</param>
    /// <param name="Beta">The regression coefficients, intercept first, one column per response.</param>
    /// <param name="ExplainedX">The fraction of predictor variance each component accounts for.</param>
    /// <param name="ExplainedY">The fraction of response variance each accounts for.</param>
    /// <param name="XMeanSquaredError">The predictors' error using none, one, two … components.</param>
    /// <param name="YMeanSquaredError">The response's error, likewise.</param>
    /// <param name="XResiduals">What the components leave of the centred predictors.</param>
    /// <param name="YResiduals">What they leave of the centred response.</param>
    /// <param name="T2">Each observation's distance from the centre in the component space.</param>
    public readonly record struct PlsFit(
        double[,] XLoadings,
        double[,] YLoadings,
        double[,] XScores,
        double[,] YScores,
        double[,] Weights,
        double[,] Beta,
        double[] ExplainedX,
        double[] ExplainedY,
        double[] XMeanSquaredError,
        double[] YMeanSquaredError,
        double[,] XResiduals,
        double[,] YResiduals,
        double[] T2);

    /// <summary>Fits <paramref name="components"/> components relating the predictors to the responses.</summary>
    /// <param name="x">One row per observation, one column per predictor.</param>
    /// <param name="y">One row per observation, one column per response.</param>
    /// <param name="components">How many components to extract.</param>
    public static PlsFit Fit(double[,] x, double[,] y, int components)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        int n = x.GetLength(0);
        int p = x.GetLength(1);
        int m = y.GetLength(1);
        if (y.GetLength(0) != n)
        {
            throw new ArgumentException(
                $"the response has {y.GetLength(0)} rows but the predictors have {n}.", nameof(y));
        }

        int most = Math.Min(n - 1, p);
        if (components < 1 || components > most)
        {
            throw new ArgumentException(
                $"the number of components must be between 1 and {most}, and {components} is not.",
                nameof(components));
        }

        (double[,] x0, double[] xMeans) = Centre(x);
        (double[,] y0, double[] yMeans) = Centre(y);

        double[,] cross = CrossProduct(x0, y0);
        var weights = new double[p, components];
        var xLoadings = new double[p, components];
        var yLoadings = new double[m, components];
        var xScores = new double[n, components];
        var yScores = new double[n, components];
        var basis = new double[p, components];

        for (int a = 0; a < components; a++)
        {
            // The direction of the predictors that covaries most with the response is the leading left
            // singular vector of their cross-product.
            double[] direction = Leading(cross);
            double[] score = Multiply(x0, direction);
            double length = Norm(score);
            if (length <= 0)
            {
                throw new ArgumentException(
                    $"component {a + 1} carries no variation; ask for fewer components.");
            }

            for (int i = 0; i < n; i++)
            {
                score[i] /= length;
            }

            for (int j = 0; j < p; j++)
            {
                direction[j] /= length;
                weights[j, a] = direction[j];
            }

            for (int i = 0; i < n; i++)
            {
                xScores[i, a] = score[i];
            }

            double[] loading = MultiplyTranspose(x0, score);
            double[] responseLoading = MultiplyTranspose(y0, score);
            for (int j = 0; j < p; j++)
            {
                xLoadings[j, a] = loading[j];
            }

            for (int j = 0; j < m; j++)
            {
                yLoadings[j, a] = responseLoading[j];
            }

            double[] responseScore = Multiply(y0, responseLoading);

            // Both the loading and the response's score are made orthogonal to everything already
            // taken, so each component says something the earlier ones did not.
            var orthogonal = (double[])loading.Clone();
            for (int earlier = 0; earlier < a; earlier++)
            {
                double projection = 0;
                for (int j = 0; j < p; j++)
                {
                    projection += basis[j, earlier] * loading[j];
                }

                for (int j = 0; j < p; j++)
                {
                    orthogonal[j] -= projection * basis[j, earlier];
                }

                double along = 0;
                for (int i = 0; i < n; i++)
                {
                    along += xScores[i, earlier] * responseScore[i];
                }

                for (int i = 0; i < n; i++)
                {
                    responseScore[i] -= along * xScores[i, earlier];
                }
            }

            for (int i = 0; i < n; i++)
            {
                yScores[i, a] = responseScore[i];
            }

            double basisLength = Norm(orthogonal);
            if (basisLength <= 0)
            {
                throw new ArgumentException(
                    $"component {a + 1} repeats an earlier one; ask for fewer components.");
            }

            for (int j = 0; j < p; j++)
            {
                basis[j, a] = orthogonal[j] / basisLength;
            }

            // Deflate the cross-product rather than the data: remove from it everything reachable
            // along the direction just taken.
            for (int c = 0; c < m; c++)
            {
                double along = 0;
                for (int j = 0; j < p; j++)
                {
                    along += basis[j, a] * cross[j, c];
                }

                for (int j = 0; j < p; j++)
                {
                    cross[j, c] -= along * basis[j, a];
                }
            }
        }

        var beta = new double[p + 1, m];
        for (int j = 0; j < p; j++)
        {
            for (int c = 0; c < m; c++)
            {
                double value = 0;
                for (int a = 0; a < components; a++)
                {
                    value += weights[j, a] * yLoadings[c, a];
                }

                beta[j + 1, c] = value;
            }
        }

        for (int c = 0; c < m; c++)
        {
            double intercept = yMeans[c];
            for (int j = 0; j < p; j++)
            {
                intercept -= xMeans[j] * beta[j + 1, c];
            }

            beta[0, c] = intercept;
        }

        double xTotal = SumOfSquares(x0);
        double yTotal = SumOfSquares(y0);
        var explainedX = new double[components];
        var explainedY = new double[components];
        for (int a = 0; a < components; a++)
        {
            double xPart = 0, yPart = 0;
            for (int j = 0; j < p; j++)
            {
                xPart += xLoadings[j, a] * xLoadings[j, a];
            }

            for (int c = 0; c < m; c++)
            {
                yPart += yLoadings[c, a] * yLoadings[c, a];
            }

            explainedX[a] = xTotal > 0 ? xPart / xTotal : 0;
            explainedY[a] = yTotal > 0 ? yPart / yTotal : 0;
        }

        // The error using nought, one, two … components. Every entry is the same reconstruction with a
        // different number of columns kept, so one loop over the prefixes covers the whole table.
        var xError = new double[components + 1];
        var yError = new double[components + 1];
        double[,] xResiduals = (double[,])x0.Clone();
        double[,] yResiduals = (double[,])y0.Clone();
        for (int used = 0; used <= components; used++)
        {
            var xLeft = (double[,])x0.Clone();
            var yLeft = (double[,])y0.Clone();
            for (int a = 0; a < used; a++)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < p; j++)
                    {
                        xLeft[i, j] -= xScores[i, a] * xLoadings[j, a];
                    }

                    for (int c = 0; c < m; c++)
                    {
                        yLeft[i, c] -= xScores[i, a] * yLoadings[c, a];
                    }
                }
            }

            xError[used] = SumOfSquares(xLeft) / (n * p);
            yError[used] = SumOfSquares(yLeft) / (n * m);
            if (used == components)
            {
                xResiduals = xLeft;
                yResiduals = yLeft;
            }
        }

        var t2 = new double[n];
        for (int a = 0; a < components; a++)
        {
            double spread = 0;
            for (int i = 0; i < n; i++)
            {
                spread += xScores[i, a] * xScores[i, a];
            }

            spread /= n - 1;
            if (spread <= 0)
            {
                continue;
            }

            for (int i = 0; i < n; i++)
            {
                t2[i] += xScores[i, a] * xScores[i, a] / spread;
            }
        }

        return new PlsFit(
            xLoadings, yLoadings, xScores, yScores, weights, beta, explainedX, explainedY,
            xError, yError, xResiduals, yResiduals, t2);
    }

    /// <summary>The leading left singular vector of a matrix.</summary>
    private static double[] Leading(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        Svd svd = Svd.Factor(matrix);
        var direction = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            direction[r] = svd.U[r, 0];
        }

        return direction;
    }

    private static (double[,] Centred, double[] Means) Centre(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        int k = matrix.GetLength(1);
        var means = new double[k];
        var centred = new double[n, k];
        for (int c = 0; c < k; c++)
        {
            double mean = 0;
            for (int r = 0; r < n; r++)
            {
                mean += matrix[r, c];
            }

            means[c] = mean / n;
            for (int r = 0; r < n; r++)
            {
                centred[r, c] = matrix[r, c] - means[c];
            }
        }

        return (centred, means);
    }

    private static double[,] CrossProduct(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int p = a.GetLength(1);
        int m = b.GetLength(1);
        var cross = new double[p, m];
        for (int j = 0; j < p; j++)
        {
            for (int c = 0; c < m; c++)
            {
                double value = 0;
                for (int i = 0; i < n; i++)
                {
                    value += a[i, j] * b[i, c];
                }

                cross[j, c] = value;
            }
        }

        return cross;
    }

    private static double[] Multiply(double[,] matrix, double[] vector)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var product = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            double value = 0;
            for (int c = 0; c < columns; c++)
            {
                value += matrix[r, c] * vector[c];
            }

            product[r] = value;
        }

        return product;
    }

    private static double[] MultiplyTranspose(double[,] matrix, double[] vector)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var product = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            double value = 0;
            for (int r = 0; r < rows; r++)
            {
                value += matrix[r, c] * vector[r];
            }

            product[c] = value;
        }

        return product;
    }

    private static double Norm(double[] vector)
    {
        double total = 0;
        foreach (double value in vector)
        {
            total += value * value;
        }

        return Math.Sqrt(total);
    }

    private static double SumOfSquares(double[,] matrix)
    {
        double total = 0;
        for (int r = 0; r < matrix.GetLength(0); r++)
        {
            for (int c = 0; c < matrix.GetLength(1); c++)
            {
                total += matrix[r, c] * matrix[r, c];
            }
        }

        return total;
    }
}
