using System.Numerics;

namespace JGraph.Signal;

/// <summary>Which correlation matrix <c>corrmtx</c> builds, and which model each estimator fits.</summary>
public enum CorrelationShape
{
    /// <summary>The full matrix, pre- and post-windowed.</summary>
    Autocorrelation,

    /// <summary>Only the rows in which every sample of the signal is present.</summary>
    Covariance,

    /// <summary>The covariance matrix stacked on its conjugate reverse, scaled by root two.</summary>
    Modified,

    /// <summary>The covariance matrix with the leading partial rows in front of it.</summary>
    Prewindowed,

    /// <summary>The covariance matrix with the trailing partial rows behind it.</summary>
    Postwindowed,
}

/// <summary>
/// The autoregressive models the parametric spectral estimates are fitted with: Burg's lattice, the
/// Yule–Walker normal equations, and the two least-squares fits over a correlation matrix.
/// </summary>
/// <remarks>
/// These are the arithmetic under <c>pburg</c>, <c>pyulear</c>, <c>pcov</c> and <c>pmcov</c>, and
/// under <c>arburg</c>, <c>aryule</c>, <c>arcov</c> and <c>armcov</c>, which are M137's names for
/// the same four fits. The names go where the plan puts them; the arithmetic goes where it is first
/// needed, which is here.
/// </remarks>
public static class AutoRegressiveModels
{
    /// <summary>A fitted model: its polynomial, the residual variance, and its reflection coefficients.</summary>
    public readonly record struct Model(Complex[] A, double Variance, Complex[] Reflection);

    /// <summary>Burg's method: the lattice recursion that minimises the two prediction errors together.</summary>
    public static Model Burg(Complex[] x, int order)
    {
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        var a = new Complex[order + 1];
        a[0] = 1;
        var reflection = new Complex[order];
        double energy = 0;
        foreach (Complex z in x)
        {
            energy += (z * Complex.Conjugate(z)).Real;
        }

        double e = energy / n;
        var forward = new Complex[System.Math.Max(n - 1, 0)];
        var backward = new Complex[System.Math.Max(n - 1, 0)];
        for (int i = 0; i < n - 1; i++)
        {
            forward[i] = x[i + 1];
            backward[i] = x[i];
        }

        for (int m = 1; m <= order; m++)
        {
            int count = n - m;
            Complex top = 0;
            double bottom = 0;
            for (int i = 0; i < count; i++)
            {
                top += Complex.Conjugate(backward[i]) * forward[i];
                bottom += (forward[i] * Complex.Conjugate(forward[i])).Real
                    + (backward[i] * Complex.Conjugate(backward[i])).Real;
            }

            Complex k = bottom == 0 ? 0 : -2 * top / bottom;
            reflection[m - 1] = k;
            var nextForward = new Complex[System.Math.Max(count - 1, 0)];
            for (int i = 0; i < count - 1; i++)
            {
                nextForward[i] = forward[i + 1] + (k * backward[i + 1]);
            }

            for (int i = 0; i < count - 1; i++)
            {
                backward[i] += Complex.Conjugate(k) * forward[i];
            }

            for (int i = 0; i < count - 1; i++)
            {
                forward[i] = nextForward[i];
            }

            var updated = new Complex[order + 1];
            Array.Copy(a, updated, order + 1);
            for (int i = 1; i <= m; i++)
            {
                updated[i] = a[i] + (k * Complex.Conjugate(a[m - i]));
            }

            a = updated;
            double magnitude = k.Magnitude;
            e = (1 - (magnitude * magnitude)) * e;
        }

        return new Model(a, e, reflection);
    }

    /// <summary>
    /// The Levinson–Durbin recursion: the model whose autocorrelation is <paramref name="r"/>, in
    /// <paramref name="order"/> steps.
    /// </summary>
    public static Model Levinson(Complex[] r, int order)
    {
        ArgumentNullException.ThrowIfNull(r);
        order = System.Math.Min(order, r.Length - 1);
        var a = new Complex[order + 1];
        a[0] = 1;
        var reflection = new Complex[System.Math.Max(order, 0)];
        double e = r[0].Real;
        for (int m = 1; m <= order; m++)
        {
            Complex sum = r[m];
            for (int i = 1; i < m; i++)
            {
                sum += a[i] * r[m - i];
            }

            Complex k = e == 0 ? 0 : -sum / e;
            reflection[m - 1] = k;
            var updated = new Complex[order + 1];
            Array.Copy(a, updated, order + 1);
            for (int i = 1; i <= m; i++)
            {
                updated[i] = a[i] + (k * Complex.Conjugate(a[m - i]));
            }

            a = updated;
            double magnitude = k.Magnitude;
            e *= 1 - (magnitude * magnitude);
        }

        return new Model(a, e, reflection);
    }

    /// <summary>The Yule–Walker fit: Levinson over the biased autocorrelation estimate.</summary>
    public static Model YuleWalker(Complex[] x, int order)
    {
        Complex[] r = BiasedAutocorrelation(x, order);
        if (order == 0)
        {
            return new Model([Complex.One], r[0].Real, []);
        }

        return Levinson(r, order);
    }

    /// <summary>The biased autocorrelation at lags 0 to <paramref name="lags"/>, which is <c>xcorr</c>'s.</summary>
    public static Complex[] BiasedAutocorrelation(Complex[] x, int lags)
    {
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        var r = new Complex[lags + 1];
        for (int lag = 0; lag <= lags; lag++)
        {
            Complex sum = 0;
            for (int i = 0; i + lag < n; i++)
            {
                sum += x[i + lag] * Complex.Conjugate(x[i]);
            }

            r[lag] = sum / n;
        }

        return r;
    }

    /// <summary>
    /// MATLAB's <c>corrmtx</c>: the data matrix whose Gram matrix is one of the five correlation
    /// estimates, built out of the signal's own sliding windows.
    /// </summary>
    public static Complex[,] CorrelationMatrix(Complex[] x, int m, CorrelationShape shape)
    {
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        if (m < 0 || m >= n)
        {
            throw new ArgumentOutOfRangeException(nameof(m), "corrmtx needs an order below the signal's length.");
        }

        // The unscaled covariance block: row i is x(i+m) … x(i), newest first.
        int rows = n - m;
        var covariance = new Complex[rows, m + 1];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                covariance[i, j] = x[i + m - j];
            }
        }

        List<Complex[]> stack = [];
        double scale = System.Math.Sqrt(n - m);
        if (shape is CorrelationShape.Prewindowed or CorrelationShape.Autocorrelation)
        {
            for (int i = 0; i < m; i++)
            {
                var row = new Complex[m + 1];
                for (int j = 0; j <= m; j++)
                {
                    int at = i - j;
                    row[j] = at >= 0 ? x[at] : 0;
                }

                stack.Add(row);
            }
        }

        for (int i = 0; i < rows; i++)
        {
            var row = new Complex[m + 1];
            for (int j = 0; j <= m; j++)
            {
                row[j] = covariance[i, j];
            }

            stack.Add(row);
        }

        if (shape is CorrelationShape.Postwindowed or CorrelationShape.Autocorrelation)
        {
            for (int i = 0; i < m; i++)
            {
                var row = new Complex[m + 1];
                for (int j = 0; j <= m; j++)
                {
                    int at = n - m + i + m - j;
                    row[j] = at < n ? x[at] : 0;
                }

                stack.Add(row);
            }
        }

        if (shape is CorrelationShape.Prewindowed or CorrelationShape.Postwindowed
            or CorrelationShape.Autocorrelation)
        {
            scale = System.Math.Sqrt(n);
        }

        if (shape == CorrelationShape.Modified)
        {
            int half = stack.Count;
            for (int i = 0; i < half; i++)
            {
                var row = new Complex[m + 1];
                for (int j = 0; j <= m; j++)
                {
                    row[j] = Complex.Conjugate(stack[i][m - j]);
                }

                stack.Add(row);
            }

            scale *= System.Math.Sqrt(2);
        }

        var matrix = new Complex[stack.Count, m + 1];
        for (int i = 0; i < stack.Count; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                matrix[i, j] = stack[i][j] / scale;
            }
        }

        return matrix;
    }

    /// <summary>
    /// The covariance and modified-covariance fits: a least-squares solve against the correlation
    /// matrix's leading column (MATLAB's <c>arparest</c>).
    /// </summary>
    public static Model ParametricFit(Complex[] x, int order, CorrelationShape shape)
    {
        Complex[,] matrix = CorrelationMatrix(x, order, shape);
        int rows = matrix.GetLength(0);
        var first = new Complex[rows];
        var rest = new Complex[rows, order];
        for (int i = 0; i < rows; i++)
        {
            first[i] = matrix[i, 0];
            for (int j = 0; j < order; j++)
            {
                rest[i, j] = matrix[i, j + 1];
            }
        }

        Complex[] solution = LeastSquares(rest, first);
        var a = new Complex[order + 1];
        a[0] = 1;
        for (int i = 0; i < order; i++)
        {
            a[i + 1] = -solution[i];
        }

        Complex variance = 0;
        for (int i = 0; i < rows; i++)
        {
            variance += Complex.Conjugate(first[i]) * first[i];
        }

        for (int j = 0; j < order; j++)
        {
            Complex column = 0;
            for (int i = 0; i < rows; i++)
            {
                column += Complex.Conjugate(first[i]) * rest[i, j];
            }

            variance += column * a[j + 1];
        }

        return new Model(a, System.Math.Abs(variance.Real), []);
    }

    /// <summary>
    /// The least-squares solution of an over-determined system, by the normal equations with a
    /// Cholesky-like elimination. The matrices here are small — an order and a signal's length.
    /// </summary>
    private static Complex[] LeastSquares(Complex[,] matrix, Complex[] rhs)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var normal = new Complex[columns, columns + 1];
        for (int i = 0; i < columns; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                Complex sum = 0;
                for (int r = 0; r < rows; r++)
                {
                    sum += Complex.Conjugate(matrix[r, i]) * matrix[r, j];
                }

                normal[i, j] = sum;
            }

            Complex target = 0;
            for (int r = 0; r < rows; r++)
            {
                target += Complex.Conjugate(matrix[r, i]) * rhs[r];
            }

            normal[i, columns] = target;
        }

        for (int pivot = 0; pivot < columns; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < columns; r++)
            {
                if (normal[r, pivot].Magnitude > normal[best, pivot].Magnitude)
                {
                    best = r;
                }
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= columns; c++)
                {
                    (normal[pivot, c], normal[best, c]) = (normal[best, c], normal[pivot, c]);
                }
            }

            Complex head = normal[pivot, pivot];
            if (head == Complex.Zero)
            {
                continue;
            }

            for (int r = pivot + 1; r < columns; r++)
            {
                Complex factor = normal[r, pivot] / head;
                for (int c = pivot; c <= columns; c++)
                {
                    normal[r, c] -= factor * normal[pivot, c];
                }
            }
        }

        var solution = new Complex[columns];
        for (int i = columns - 1; i >= 0; i--)
        {
            Complex sum = normal[i, columns];
            for (int j = i + 1; j < columns; j++)
            {
                sum -= normal[i, j] * solution[j];
            }

            solution[i] = normal[i, i] == Complex.Zero ? 0 : sum / normal[i, i];
        }

        return solution;
    }
}
