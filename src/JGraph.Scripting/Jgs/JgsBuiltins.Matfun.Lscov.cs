using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>lscov</c>: least squares when the observations are not all equally trustworthy, and the
/// standard errors that come with the fit.
/// </summary>
/// <remarks>
/// <para>
/// Two algorithms, and which one runs is decided by the covariance rather than by the caller. A
/// covariance that is positive definite can be Cholesky-factored and divided out, which turns the
/// whole problem back into an ordinary least-squares fit of a rescaled design — that is the
/// <c>'chol'</c> path and it is the fast one. A covariance that is merely positive semidefinite has
/// directions of no variance at all, and along those the fit is not a fit but a constraint: the
/// residual there must be exactly nought or there is no solution. That is the <c>'orth'</c> path,
/// and most of its length is deciding whether the right-hand side is consistent with those
/// constraints and saying so plainly when it is not.
/// </para>
/// <para>
/// Both paths rank the design by column-pivoted QR rather than by singular values. That is not the
/// better rank test and it is the one MATLAB uses; more to the point it is the factorization the
/// solve itself runs on, so the rank the answer was computed at and the rank the warning reports
/// cannot disagree.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>x = lscov(A, b)</c>, <c>lscov(A, b, w)</c>, <c>lscov(A, b, V)</c>,
    /// <c>lscov(A, b, V, alg)</c> and the three extra outputs <c>[x, stdx, mse, S]</c>.
    /// </summary>
    private static JgsValue[] CovarianceLeastSquares(
        IReadOnlyList<JgsValue> args, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        ArityRange("lscov", args, 2, 4, line, col);
        Complex[,] a = MatBlock("lscov", args[0], line, col);
        Complex[,] b = MatBlock("lscov", args[1], line, col);
        int observations = a.GetLength(0);
        int variables = a.GetLength(1);
        int rhs = b.GetLength(1);

        if (b.GetLength(0) != observations)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lscov:InputSizeMismatch",
                "B must have the same number of rows as A.");
        }

        if (wanted > 3 && rhs > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lscov:CantReturnCov",
                "Cannot return S when B contains multiple right-hand sides.");
        }

        bool algorithmGiven = args.Count >= 4;
        bool useCholesky = true;
        if (algorithmGiven)
        {
            string word = Str("lscov", args, 3, line, col);
            if (word.Length > 0 && "chol".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                useCholesky = true;
            }
            else if (word.Length > 0 && "orth".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                useCholesky = false;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lscov:InvalidAlgArg",
                    "ALG must be 'chol' or 'orth'.");
            }
        }

        Complex[,]? covariance = args.Count >= 3 && args[2].Type != JgsType.Null
            ? MatBlock("lscov", args[2], line, col)
            : null;
        if (covariance is not null && covariance.Length == 0)
        {
            covariance = null;
        }

        Complex[,]? spread = null;
        double[]? weights = null;
        if (covariance is null)
        {
            useCholesky = true;
        }
        else if (IsWeightVector(covariance, observations, out double[] w))
        {
            weights = w;
            useCholesky = true;
        }
        else if (covariance.GetLength(0) == observations && covariance.GetLength(1) == observations)
        {
            if (!IsHermitian(covariance))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lscov:InvalidCovMatSymV",
                    "V must be symmetric.");
            }

            (spread, useCholesky) = FactorCovariance(covariance, algorithmGiven, useCholesky, host, line, col);
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lscov:InvalidCovMat",
                $"V must be a {observations}-by-{observations} covariance matrix or a "
                + $"{observations}-by-1 weight vector.");
        }

        return useCholesky
            ? CholeskyFit(a, b, weights, spread, covariance, observations, variables, rhs, wanted, host, line, col)
            : OrthogonalFit(a, b, spread!, observations, variables, rhs, wanted, host, line, col);
    }

    /// <summary>The rescale-and-solve path: the covariance is divided out and an ordinary fit remains.</summary>
    private static JgsValue[] CholeskyFit(
        Complex[,] a, Complex[,] b, double[]? weights, Complex[,]? spread, Complex[,]? covariance,
        int observations, int variables, int rhs, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        if (weights is not null)
        {
            // A weight is a variance's reciprocal, so its square root scales the row it belongs to.
            for (int row = 0; row < observations; row++)
            {
                double scale = Math.Sqrt(weights[row]);
                for (int c = 0; c < variables; c++)
                {
                    a[row, c] *= scale;
                }

                for (int c = 0; c < rhs; c++)
                {
                    b[row, c] *= scale;
                }
            }
        }
        else if (spread is not null && covariance is not null)
        {
            SolveLowerBlock(spread, a);
            SolveLowerBlock(spread, b);
        }

        HouseholderQr qr = HouseholderQr.Factor(a, pivot: true);
        Complex[,] q = qr.Q(full: false);
        Complex[,] r = qr.R(full: false);
        int[] perm = qr.Pivot;
        Complex[,] z = Head(qr.ApplyConjugateTranspose(b), Math.Min(observations, variables));

        double[] diagonal = qr.DiagonalMagnitudes;
        double largest = 0.0;
        foreach (double value in diagonal)
        {
            largest = Math.Max(largest, value);
        }

        int rank = 0;
        double cut = largest * Math.Max(observations, variables) * DoubleSpacing;
        foreach (double value in diagonal)
        {
            if (value > cut)
            {
                rank++;
            }
        }

        if (rank < variables)
        {
            host.WriteErr("Warning: A is rank deficient to within machine precision.");
        }

        Complex[,] rr = Corner(r, rank, rank);
        Complex[,] zz = Head(z, rank);
        var solved = (Complex[,])zz.Clone();
        HouseholderQr.SolveUpper(rr, rank, solved);

        var x = new Complex[variables, rhs];
        for (int i = 0; i < rank; i++)
        {
            for (int c = 0; c < rhs; c++)
            {
                x[perm[i], c] = solved[i, c];
            }
        }

        if (wanted <= 1)
        {
            return [MatValue(x)];
        }

        var mse = new double[rhs];
        int freedom = observations - rank;
        if (freedom > 0)
        {
            Complex[,] fitted = NormEstimators.Product(Columns(q, rank), zz, conjugateTranspose: false);
            for (int c = 0; c < rhs; c++)
            {
                double sum = 0.0;
                for (int i = 0; i < observations; i++)
                {
                    Complex residual = b[i, c] - fitted[i, c];
                    sum += (residual.Real * residual.Real) + (residual.Imaginary * residual.Imaginary);
                }

                mse[c] = sum / freedom;
            }
        }

        Complex[,] inverse = InvertUpper(rr, rank);
        if (wanted > 3)
        {
            var s = new Complex[variables, variables];
            for (int i = 0; i < rank; i++)
            {
                for (int j = 0; j < rank; j++)
                {
                    Complex sum = Complex.Zero;
                    for (int k = 0; k < rank; k++)
                    {
                        sum += inverse[i, k] * Complex.Conjugate(inverse[j, k]);
                    }

                    s[perm[i], perm[j]] = sum * mse[0];
                }
            }

            var deviations = new double[variables, 1];
            for (int i = 0; i < variables; i++)
            {
                deviations[i, 0] = Math.Sqrt(s[i, i].Real);
            }

            return [MatValue(x), MatValue(Widen(deviations)), ShapedNumbers(mse, [1, rhs]), MatValue(s)];
        }

        var stdx = new Complex[variables, rhs];
        for (int i = 0; i < rank; i++)
        {
            double length = 0.0;
            for (int k = 0; k < rank; k++)
            {
                length += (inverse[i, k].Real * inverse[i, k].Real)
                    + (inverse[i, k].Imaginary * inverse[i, k].Imaginary);
            }

            length = Math.Sqrt(length);
            for (int c = 0; c < rhs; c++)
            {
                stdx[perm[i], c] = length * Math.Sqrt(mse[c]);
            }
        }

        return Outputs(wanted, MatValue(x), MatValue(stdx), ShapedNumbers(mse, [1, rhs]));
    }

    /// <summary>
    /// The path a singular covariance forces: what the design cannot explain has to be explained by
    /// the covariance's own range, and where the covariance has no range the fit becomes a
    /// constraint that the right-hand side either satisfies or does not.
    /// </summary>
    private static JgsValue[] OrthogonalFit(
        Complex[,] a, Complex[,] b, Complex[,] spread,
        int observations, int variables, int rhs, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        HouseholderQr qr = HouseholderQr.Factor(a, pivot: true);
        Complex[,] fullQ = qr.Q(full: true);
        Complex[,] r = Head(qr.R(full: true), Math.Min(variables, observations));
        int[] perm = qr.Pivot;

        double[] diagonal = qr.DiagonalMagnitudes;
        double largest = 0.0;
        foreach (double value in diagonal)
        {
            largest = Math.Max(largest, value);
        }

        int rank = 0;
        double cut = largest * Math.Max(observations, variables) * DoubleSpacing;
        foreach (double value in diagonal)
        {
            if (value > cut)
            {
                rank++;
            }
        }

        if (rank < variables)
        {
            host.WriteErr("Warning: A is rank deficient to within machine precision.");
        }

        Complex[,] rr = Corner(r, rank, rank);
        Complex[,] range = Columns(fullQ, rank);
        Complex[,] nullSpace = TrailingColumns(fullQ, rank);
        int spreadRank = spread.GetLength(1);

        Complex[,] projected = NormEstimators.Product(nullSpace, spread, conjugateTranspose: true);
        Complex[,] shortfall = NormEstimators.Product(nullSpace, b, conjugateTranspose: true);

        bool zeroCorrection;
        bool feasible;
        Complex[,] correction = new Complex[spreadRank, rhs];
        Complex[,] basis = new Complex[spreadRank, 0];
        var mse = new double[rhs];

        if (rank == observations)
        {
            zeroCorrection = true;
            feasible = true;
        }
        else if (NormEstimators.OneNormOf(projected)
            < NormEstimators.OneNormOf(spread) * Math.Pow(Math.Max(observations, spreadRank), 2) * DoubleSpacing)
        {
            host.WriteErr("Warning: T is orthogonal to the null space of A.");
            zeroCorrection = true;
            feasible = NormEstimators.OneNormOf(shortfall)
                < NormEstimators.OneNormOf(b) * observations * (double)observations * DoubleSpacing;
        }
        else
        {
            zeroCorrection = false;
            feasible = true;
            (correction, basis, double[] residuals) =
                SmallestCorrection(projected, shortfall, spreadRank, rhs, host);
            int freedom = basis.GetLength(1);
            for (int c = 0; c < rhs; c++)
            {
                double length = 0.0;
                for (int i = 0; i < spreadRank; i++)
                {
                    length += (correction[i, c].Real * correction[i, c].Real)
                        + (correction[i, c].Imaginary * correction[i, c].Imaginary);
                }

                mse[c] = freedom > 0 ? length / freedom : 0.0;
                feasible &= residuals[c] < Math.Sqrt(length) * spreadRank * DoubleSpacing;
            }
        }

        if (!feasible)
        {
            throw rhs == 1
                ? new JgsRuntimeException(line, col, "MATLAB:lscov:InfeasibleRHS",
                    "B is not consistent with A and V.")
                : new JgsRuntimeException(line, col, "MATLAB:lscov:InfeasibleRHScol",
                    "One or more columns of B are not consistent with A and V.");
        }

        Complex[,] adjusted = (Complex[,])b.Clone();
        if (!zeroCorrection)
        {
            Complex[,] explained = NormEstimators.Product(spread, correction, conjugateTranspose: false);
            for (int i = 0; i < observations; i++)
            {
                for (int c = 0; c < rhs; c++)
                {
                    adjusted[i, c] -= explained[i, c];
                }
            }
        }

        Complex[,] z = NormEstimators.Product(range, adjusted, conjugateTranspose: true);
        var solved = (Complex[,])z.Clone();
        HouseholderQr.SolveUpper(rr, rank, solved);

        var x = new Complex[variables, rhs];
        for (int i = 0; i < rank; i++)
        {
            for (int c = 0; c < rhs; c++)
            {
                x[perm[i], c] = solved[i, c];
            }
        }

        if (wanted <= 1)
        {
            return [MatValue(x)];
        }

        // The covariance of the fit is what the design lets through of the part of the covariance's
        // range that the correction did not already account for.
        var complement = new Complex[spreadRank, spreadRank];
        for (int i = 0; i < spreadRank; i++)
        {
            complement[i, i] = Complex.One;
            for (int j = 0; j < spreadRank; j++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < basis.GetLength(1); k++)
                {
                    sum += basis[i, k] * Complex.Conjugate(basis[j, k]);
                }

                complement[i, j] -= sum;
            }
        }

        Complex[,] carried = NormEstimators.Product(
            NormEstimators.Product(range, spread, conjugateTranspose: true), complement, conjugateTranspose: false);
        var c2 = (Complex[,])carried.Clone();
        HouseholderQr.SolveUpper(rr, rank, c2);

        if (wanted > 3)
        {
            var s = new Complex[variables, variables];
            for (int i = 0; i < rank; i++)
            {
                for (int j = 0; j < rank; j++)
                {
                    Complex sum = Complex.Zero;
                    for (int k = 0; k < c2.GetLength(1); k++)
                    {
                        sum += c2[i, k] * Complex.Conjugate(c2[j, k]);
                    }

                    s[perm[i], perm[j]] = sum * mse[0];
                }
            }

            var deviations = new double[variables, 1];
            for (int i = 0; i < variables; i++)
            {
                deviations[i, 0] = Math.Sqrt(s[i, i].Real);
            }

            return [MatValue(x), MatValue(Widen(deviations)), ShapedNumbers(mse, [1, rhs]), MatValue(s)];
        }

        var stdx = new Complex[variables, rhs];
        for (int i = 0; i < rank; i++)
        {
            double length = 0.0;
            for (int k = 0; k < c2.GetLength(1); k++)
            {
                length += (c2[i, k].Real * c2[i, k].Real) + (c2[i, k].Imaginary * c2[i, k].Imaginary);
            }

            length = Math.Sqrt(length);
            for (int c = 0; c < rhs; c++)
            {
                stdx[perm[i], c] = length * Math.Sqrt(mse[c]);
            }
        }

        return Outputs(wanted, MatValue(x), MatValue(stdx), ShapedNumbers(mse, [1, rhs]));
    }

    /// <summary>
    /// The smallest correction that reconciles the constrained part of the fit, its own null-space
    /// basis, and how much residual each right-hand side is left with.
    /// </summary>
    private static (Complex[,] Correction, Complex[,] Basis, double[] Residuals) SmallestCorrection(
        Complex[,] t0, Complex[,] b0, int spreadRank, int rhs, JGraphScriptGlobals host)
    {
        int m = t0.GetLength(0);
        int n = t0.GetLength(1);
        HouseholderQr qr = HouseholderQr.Factor(t0, pivot: true);
        double[] diagonal = qr.DiagonalMagnitudes;
        double largest = 0.0;
        foreach (double value in diagonal)
        {
            largest = Math.Max(largest, value);
        }

        double cut = largest * Math.Pow(Math.Max(m, n), 2) * DoubleSpacing;
        int rank = 0;
        foreach (double value in diagonal)
        {
            if (value > cut)
            {
                rank++;
            }
        }

        if (rank < Math.Min(m, n))
        {
            host.WriteErr("Warning: Some columns of T are orthogonal to the null space of A.");
        }

        Complex[,] correction = HouseholderQr.MinimumNormSolution(t0, b0, cut, out _);

        // The directions the correction was free to move along: an orthonormal basis of the row
        // space, put back into the original column order. It is the row space and not its
        // complement — what the covariance of the fit needs is the part of the correction the data
        // determined, and the projector below subtracts it.
        var basis = new Complex[n, rank];
        if (rank > 0)
        {
            var transposed = new Complex[n, rank];
            Complex[,] r = qr.R(full: false);
            int[] perm = qr.Pivot;
            for (int i = 0; i < rank; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    transposed[perm[j], i] = Complex.Conjugate(r[i, j]);
                }
            }

            basis = HouseholderQr.Factor(transposed, pivot: false).Q(full: false);
        }

        var residuals = new double[rhs];
        Complex[,] fitted = NormEstimators.Product(t0, correction, conjugateTranspose: false);
        for (int c = 0; c < rhs; c++)
        {
            double sum = 0.0;
            for (int i = 0; i < m; i++)
            {
                Complex left = fitted[i, c] - b0[i, c];
                sum += (left.Real * left.Real) + (left.Imaginary * left.Imaginary);
            }

            residuals[c] = m > rank ? sum / (m - rank) : 0.0;
        }

        _ = spreadRank;
        return (correction, basis, residuals);
    }

    /// <summary>
    /// The factor T with <c>T·Tᴴ = V</c>: a Cholesky when the covariance is definite, and the
    /// positive part of its eigendecomposition when it is only semidefinite.
    /// </summary>
    private static (Complex[,] Spread, bool UseCholesky) FactorCovariance(
        Complex[,] v, bool algorithmGiven, bool useCholesky, JGraphScriptGlobals host, int line, int col)
    {
        if (useCholesky && TryCholesky(v, out Complex[,] lower))
        {
            return (lower, true);
        }

        int n = v.GetLength(0);
        JgsValue[] factors = SingleEigen(MatValue(v), 2, asVector: true, line, col);
        Complex[,] vectors = MatBlock("lscov", factors[0], line, col);
        Complex[] values = ComplexElements("lscov", factors[1], line, col);

        double biggest = 0.0;
        foreach (Complex value in values)
        {
            biggest = Math.Max(biggest, value.Magnitude);
        }

        double tolerance = biggest * n * DoubleSpacing;
        var kept = new List<int>();
        foreach (Complex value in values)
        {
            if (value.Real < -tolerance)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lscov:InvalidCovMatPosV",
                    "V must be positive definite or positive semidefinite.");
            }
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Real > tolerance)
            {
                kept.Add(i);
            }
        }

        var spread = new Complex[n, kept.Count];
        for (int c = 0; c < kept.Count; c++)
        {
            double scale = Math.Sqrt(values[kept[c]].Real);
            for (int r = 0; r < n; r++)
            {
                spread[r, c] = vectors[r, kept[c]] * scale;
            }
        }

        if (algorithmGiven && useCholesky)
        {
            host.WriteErr("Warning: V is rank deficient to within machine precision "
                + "- switching to orthogonal algorithm.");
        }

        return (spread, false);
    }

    /// <summary>The lower Cholesky factor, or nothing when the matrix is not positive definite.</summary>
    private static bool TryCholesky(Complex[,] v, out Complex[,] lower)
    {
        int n = v.GetLength(0);
        lower = new Complex[n, n];
        for (int j = 0; j < n; j++)
        {
            Complex diagonal = v[j, j];
            for (int k = 0; k < j; k++)
            {
                diagonal -= lower[j, k] * Complex.Conjugate(lower[j, k]);
            }

            if (diagonal.Real <= 0)
            {
                return false;
            }

            double root = Math.Sqrt(diagonal.Real);
            lower[j, j] = new Complex(root, 0.0);
            for (int i = j + 1; i < n; i++)
            {
                Complex sum = v[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= lower[i, k] * Complex.Conjugate(lower[j, k]);
                }

                lower[i, j] = new Complex(sum.Real / root, sum.Imaginary / root);
            }
        }

        return true;
    }

    /// <summary>Whether V is a vector of as many non-negative weights as there are observations.</summary>
    private static bool IsWeightVector(Complex[,] v, int observations, out double[] weights)
    {
        weights = [];
        int rows = v.GetLength(0);
        int cols = v.GetLength(1);
        if ((rows != 1 && cols != 1) || rows * cols != observations)
        {
            return false;
        }

        var found = new double[observations];
        int at = 0;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (v[r, c].Imaginary != 0 || !(v[r, c].Real >= 0))
                {
                    return false;
                }

                found[at++] = v[r, c].Real;
            }
        }

        weights = found;
        return true;
    }

    /// <summary>Whether a square block equals its own conjugate transpose.</summary>
    private static bool IsHermitian(Complex[,] v)
    {
        int n = v.GetLength(0);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (v[r, c] != Complex.Conjugate(v[c, r]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Solves <c>L·X = B</c> in place for a lower triangular L.</summary>
    private static void SolveLowerBlock(Complex[,] l, Complex[,] b)
    {
        int n = l.GetLength(0);
        int rhs = b.GetLength(1);
        for (int c = 0; c < rhs; c++)
        {
            for (int i = 0; i < n; i++)
            {
                Complex sum = b[i, c];
                for (int j = 0; j < i; j++)
                {
                    sum -= l[i, j] * b[j, c];
                }

                b[i, c] = sum / l[i, i];
            }
        }
    }

    /// <summary>The inverse of an upper triangular leading block of order n.</summary>
    private static Complex[,] InvertUpper(Complex[,] r, int n)
    {
        var inverse = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            inverse[i, i] = Complex.One;
        }

        HouseholderQr.SolveUpper(r, n, inverse);
        return inverse;
    }

    /// <summary>The first <paramref name="rows"/> rows of a block.</summary>
    private static Complex[,] Head(Complex[,] a, int rows)
    {
        int cols = a.GetLength(1);
        var cut = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows && r < a.GetLength(0); r++)
            {
                cut[r, c] = a[r, c];
            }
        }

        return cut;
    }

    /// <summary>The leading rows-by-cols corner of a block.</summary>
    private static Complex[,] Corner(Complex[,] a, int rows, int cols)
    {
        var cut = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                cut[r, c] = a[r, c];
            }
        }

        return cut;
    }

    /// <summary>The first <paramref name="count"/> columns of a block.</summary>
    private static Complex[,] Columns(Complex[,] a, int count)
    {
        int rows = a.GetLength(0);
        var cut = new Complex[rows, count];
        for (int c = 0; c < count; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                cut[r, c] = a[r, c];
            }
        }

        return cut;
    }

    /// <summary>Every column of a block from <paramref name="from"/> onward.</summary>
    private static Complex[,] TrailingColumns(Complex[,] a, int from)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var cut = new Complex[rows, Math.Max(0, cols - from)];
        for (int c = from; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                cut[r, c - from] = a[r, c];
            }
        }

        return cut;
    }
}
