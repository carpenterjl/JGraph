using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Regression;

/// <summary>
/// The one weighted least-squares solve every regression in this folder is built out of.
/// </summary>
/// <remarks>
/// <para>
/// It factors the design matrix rather than forming and inverting the cross-product, so a design whose
/// columns are collinear — two ways of writing the same predictor, a dummy-variable set that already
/// carries an intercept — answers a fit rather than a division by nearly nothing. Which coefficients a
/// rank-deficient design gets is then the minimum-norm choice, not MATLAB's basic solution; that is
/// recorded as a divergence, and the fitted values, residuals and every statistic derived from them
/// agree either way because they depend only on the space the columns span.
/// </para>
/// <para>
/// Weights enter by scaling each row by the square root of its weight, which is the same arithmetic
/// as solving the weighted normal equations and is what lets iteratively reweighted least squares —
/// robust regression, the generalized linear models, the multinomial fit — reuse this without knowing
/// anything about how the weights were arrived at.
/// </para>
/// </remarks>
public static class LeastSquares
{
    /// <summary>What a solve produced.</summary>
    /// <param name="Coefficients">One per column of the design.</param>
    /// <param name="Fitted">The design times the coefficients.</param>
    /// <param name="Residuals">Observed minus fitted, unweighted.</param>
    /// <param name="Covariance">The coefficients' covariance, the mean squared error times <paramref name="CrossInverse"/>.</param>
    /// <param name="CrossInverse">The pseudo-inverse of Xᵀ·W·X.</param>
    /// <param name="Leverage">The diagonal of the hat matrix, one per observation.</param>
    /// <param name="ResidualSumOfSquares">The weighted sum of squared residuals.</param>
    /// <param name="MeanSquaredError">That sum over the residual degrees of freedom.</param>
    /// <param name="Rank">How many of the design's columns are independent.</param>
    /// <param name="Df">Observations less rank.</param>
    public readonly record struct Fit(
        double[] Coefficients,
        double[] Fitted,
        double[] Residuals,
        double[,] Covariance,
        double[,] CrossInverse,
        double[] Leverage,
        double ResidualSumOfSquares,
        double MeanSquaredError,
        int Rank,
        int Df);

    /// <summary>Fits <c>y = X·b</c> in the least-squares sense.</summary>
    public static Fit Solve(double[,] design, double[] y) => Solve(design, y, null);

    /// <summary>Fits <c>y = X·b</c> weighting each observation by its own weight.</summary>
    /// <param name="design">One row per observation, one column per coefficient.</param>
    /// <param name="y">The response, one per row of the design.</param>
    /// <param name="weights">One per observation, or null for equal weights.</param>
    public static Fit Solve(double[,] design, double[] y, double[]? weights)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(y);

        int n = design.GetLength(0);
        int k = design.GetLength(1);
        if (y.Length != n)
        {
            throw new ArgumentException(
                $"the response has {y.Length} values but the design has {n} rows.", nameof(y));
        }

        if (weights is not null && weights.Length != n)
        {
            throw new ArgumentException(
                $"there are {weights.Length} weights for {n} observations.", nameof(weights));
        }

        if (n < k)
        {
            throw new ArgumentException(
                $"a model with {k} coefficients needs at least that many observations, and there are {n}.");
        }

        var scaled = new double[n, k];
        var response = new double[n];
        for (int r = 0; r < n; r++)
        {
            double root = weights is null ? 1 : Math.Sqrt(Math.Max(0, weights[r]));
            response[r] = root * y[r];
            for (int c = 0; c < k; c++)
            {
                scaled[r, c] = root * design[r, c];
            }
        }

        Svd svd = Svd.Factor(scaled);
        double largest = svd.Values.Length > 0 ? svd.Values[0] : 0;
        double tolerance = Math.Max(n, k) * 2.220446049250313e-16 * largest;

        var coefficients = new double[k];
        var crossInverse = new double[k, k];
        var leverage = new double[n];
        int rank = 0;
        for (int j = 0; j < svd.Values.Length; j++)
        {
            if (svd.Values[j] <= tolerance)
            {
                continue;
            }

            rank++;
            double projection = 0;
            for (int r = 0; r < n; r++)
            {
                projection += svd.U[r, j] * response[r];
            }

            projection /= svd.Values[j];
            for (int c = 0; c < k; c++)
            {
                coefficients[c] += projection * svd.V[c, j];
            }

            double inverseSquare = 1 / (svd.Values[j] * svd.Values[j]);
            for (int a = 0; a < k; a++)
            {
                for (int b = 0; b < k; b++)
                {
                    crossInverse[a, b] += inverseSquare * svd.V[a, j] * svd.V[b, j];
                }
            }

            for (int r = 0; r < n; r++)
            {
                leverage[r] += svd.U[r, j] * svd.U[r, j];
            }
        }

        var fitted = new double[n];
        var residuals = new double[n];
        double sum = 0;
        for (int r = 0; r < n; r++)
        {
            double value = 0;
            for (int c = 0; c < k; c++)
            {
                value += design[r, c] * coefficients[c];
            }

            fitted[r] = value;
            residuals[r] = y[r] - value;
            sum += (weights is null ? 1 : weights[r]) * residuals[r] * residuals[r];
        }

        int df = n - rank;
        double mse = df > 0 ? sum / df : 0;
        var covariance = new double[k, k];
        for (int a = 0; a < k; a++)
        {
            for (int b = 0; b < k; b++)
            {
                covariance[a, b] = mse * crossInverse[a, b];
            }
        }

        return new Fit(
            coefficients, fitted, residuals, covariance, crossInverse, leverage, sum, mse, rank, df);
    }

    /// <summary>The product of a matrix and a vector.</summary>
    public static double[] Apply(double[,] matrix, double[] vector)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(vector);

        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        if (vector.Length != columns)
        {
            throw new ArgumentException(
                $"a matrix of {columns} columns cannot be applied to {vector.Length} values.");
        }

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

    /// <summary>The variance of one prediction: <c>xᵀ·C·x</c>, for a row <c>x</c> and covariance <c>C</c>.</summary>
    public static double PredictionVariance(double[,] covariance, double[] row)
    {
        ArgumentNullException.ThrowIfNull(covariance);
        ArgumentNullException.ThrowIfNull(row);

        double total = 0;
        for (int a = 0; a < row.Length; a++)
        {
            for (int b = 0; b < row.Length; b++)
            {
                total += row[a] * covariance[a, b] * row[b];
            }
        }

        return Math.Max(0, total);
    }

    /// <summary>One row of a matrix, as a vector.</summary>
    public static double[] Row(double[,] matrix, int index)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        int columns = matrix.GetLength(1);
        var row = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            row[c] = matrix[index, c];
        }

        return row;
    }

    /// <summary>A design matrix with a column of ones written in front of the predictors.</summary>
    public static double[,] WithIntercept(double[,] predictors)
    {
        ArgumentNullException.ThrowIfNull(predictors);

        int n = predictors.GetLength(0);
        int k = predictors.GetLength(1);
        var design = new double[n, k + 1];
        for (int r = 0; r < n; r++)
        {
            design[r, 0] = 1;
            for (int c = 0; c < k; c++)
            {
                design[r, c + 1] = predictors[r, c];
            }
        }

        return design;
    }

    /// <summary>Whether the design already carries a column that never varies.</summary>
    public static bool HasConstantColumn(double[,] design)
    {
        ArgumentNullException.ThrowIfNull(design);

        int n = design.GetLength(0);
        int k = design.GetLength(1);
        for (int c = 0; c < k; c++)
        {
            bool constant = true;
            for (int r = 1; r < n && constant; r++)
            {
                constant = Math.Abs(design[r, c] - design[0, c]) <= 1e-12 * (1 + Math.Abs(design[0, c]));
            }

            if (constant && Math.Abs(design[0, c]) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
