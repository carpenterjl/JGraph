using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Multivariate;

/// <summary>
/// Three ways of relating one configuration of points to another: recovering coordinates from the
/// distances alone, matching two configurations by moving one onto the other, and finding the
/// directions in which two sets of variables agree.
/// </summary>
public static class Scaling
{
    /// <summary>
    /// Classical multidimensional scaling: coordinates whose pairwise distances reproduce the ones
    /// given.
    /// </summary>
    /// <remarks>
    /// Double-centring the squared distances turns them into a matrix of inner products, whose
    /// eigenvectors are then the coordinates. A distance matrix that no set of points could have
    /// produced makes some of those eigenvalues negative — which is not a failure but the answer: the
    /// negative values are returned alongside the coordinates, and their size is how far from
    /// Euclidean the distances were.
    /// </remarks>
    /// <param name="square">The distances, as a symmetric matrix with a zero diagonal.</param>
    /// <returns>The coordinates, one point per row, and the eigenvalues in descending order.</returns>
    public static (double[,] Coordinates, double[] Values) Classical(double[,] square)
    {
        ArgumentNullException.ThrowIfNull(square);
        int n = square.GetLength(0);
        if (square.GetLength(1) != n)
        {
            throw new ArgumentException("A distance matrix must be square.", nameof(square));
        }

        if (n < 2)
        {
            throw new ArgumentException("Scaling needs at least two points.", nameof(square));
        }

        var squared = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                squared[r, c] = square[r, c] * square[r, c];
            }
        }

        var rowMeans = new double[n];
        double grand = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                rowMeans[r] += squared[r, c];
            }

            rowMeans[r] /= n;
            grand += rowMeans[r];
        }

        grand /= n;

        var inner = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                inner[r, c] = -0.5 * (squared[r, c] - rowMeans[r] - rowMeans[c] + grand);
            }
        }

        Eigen eigen = Eigen.Factor(inner);
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byValue = eigen.Values[b].Real.CompareTo(eigen.Values[a].Real);
            return byValue != 0 ? byValue : a.CompareTo(b);
        });

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = eigen.Values[order[i]].Real;
        }

        // Only the positive eigenvalues give real coordinates; the rest are reported but contribute no
        // column, which is what makes the answer narrower than the input for non-Euclidean distances.
        double tolerance = n * 2.220446049250313e-16 * Math.Max(Math.Abs(values[0]), 1);
        int kept = 0;
        while (kept < n && values[kept] > tolerance)
        {
            kept++;
        }

        var coordinates = new double[n, kept];
        for (int c = 0; c < kept; c++)
        {
            double scale = Math.Sqrt(values[c]);
            int largest = 0;
            for (int r = 0; r < n; r++)
            {
                if (Math.Abs(eigen.Vectors[r, order[c]].Real) > Math.Abs(eigen.Vectors[largest, order[c]].Real))
                {
                    largest = r;
                }
            }

            double sign = eigen.Vectors[largest, order[c]].Real < 0 ? -1 : 1;
            for (int r = 0; r < n; r++)
            {
                coordinates[r, c] = sign * eigen.Vectors[r, order[c]].Real * scale;
            }
        }

        return (coordinates, values);
    }

    /// <summary>The transformation that carries one configuration onto another.</summary>
    /// <param name="Rotation">The orthogonal part.</param>
    /// <param name="Scale">The single factor everything is stretched by.</param>
    /// <param name="Translation">The shift, one value per variable.</param>
    public readonly record struct Transformation(double[,] Rotation, double Scale, double[] Translation);

    /// <summary>
    /// Procrustes analysis: how much of <paramref name="y"/> is left unexplained once it has been
    /// moved, turned and scaled as close to <paramref name="x"/> as it can get.
    /// </summary>
    /// <param name="x">The target configuration, one point per row.</param>
    /// <param name="y">The configuration to move.</param>
    /// <param name="scaling">Whether the fit may stretch as well as turn.</param>
    /// <param name="reflection">Whether a reflection is allowed, or null to take whichever fits better.</param>
    /// <returns>The dissimilarity, the transformed configuration, and the transformation itself.</returns>
    public static (double Dissimilarity, double[,] Transformed, Transformation Transform) Procrustes(
        double[,] x, double[,] y, bool scaling = true, bool? reflection = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        int n = x.GetLength(0);
        int p = x.GetLength(1);
        if (y.GetLength(0) != n)
        {
            throw new ArgumentException("The two configurations must have the same number of points.", nameof(y));
        }

        int q = y.GetLength(1);
        if (q > p)
        {
            throw new ArgumentException(
                "The configuration being moved cannot have more dimensions than the target.", nameof(y));
        }

        // A configuration in fewer dimensions is padded with zeros rather than refused, because the
        // question "how well does this plane figure match that solid one" is a real one and the answer
        // is the same rotation in the larger space.
        double[,] padded = q == p ? y : Pad(y, p);

        (double[,] centredX, double[] centreX) = Centre(x);
        (double[,] centredY, double[] centreY) = Centre(padded);

        double normX = FrobeniusNorm(centredX);
        double normY = FrobeniusNorm(centredY);
        if (!(normX > 0))
        {
            throw new ArgumentException("The target configuration is a single point.", nameof(x));
        }

        Scale(centredX, 1 / normX);
        if (normY > 0)
        {
            Scale(centredY, 1 / normY);
        }

        double[,] cross = PrincipalComponents.Multiply(
            PrincipalComponents.Transpose(centredY), centredX);
        Svd svd = Svd.Factor(cross);
        double[,] rotation = PrincipalComponents.Multiply(
            svd.U, PrincipalComponents.Transpose(svd.V));

        if (reflection is { } wanted && Determinant(rotation) < 0 != !wanted)
        {
            // Forcing the reflection either way means flipping the last singular direction, which is
            // the cheapest column to give up because it is the one that matched least.
            int last = svd.Values.Length - 1;
            var flipped = (double[,])svd.U.Clone();
            for (int r = 0; r < flipped.GetLength(0); r++)
            {
                flipped[r, last] = -flipped[r, last];
            }

            rotation = PrincipalComponents.Multiply(flipped, PrincipalComponents.Transpose(svd.V));
        }

        double trace = 0;
        for (int i = 0; i < svd.Values.Length; i++)
        {
            trace += svd.Values[i];
        }

        if (reflection is { } forced && Determinant(rotation) < 0 != !forced)
        {
            trace -= 2 * svd.Values[svd.Values.Length - 1];
        }

        double scale = scaling ? trace * normX / Math.Max(normY, double.Epsilon) : 1;
        double dissimilarity = scaling
            ? 1 - (trace * trace)
            : 1 + ((normY * normY / (normX * normX)) - (2 * trace * normY / normX));

        var transformed = new double[n, p];
        double[,] turned = PrincipalComponents.Multiply(centredY, rotation);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                transformed[r, c] = (scaling ? trace * normX : normY) * turned[r, c] + centreX[c];
            }
        }

        var translation = new double[p];
        for (int c = 0; c < p; c++)
        {
            double moved = 0;
            for (int i = 0; i < p; i++)
            {
                moved += centreY[i] * rotation[i, c];
            }

            translation[c] = centreX[c] - (scale * moved);
        }

        return (Math.Max(dissimilarity, 0), transformed, new Transformation(rotation, scale, translation));
    }

    /// <summary>What a canonical correlation analysis produced.</summary>
    /// <param name="A">The combinations of the first set's variables.</param>
    /// <param name="B">The combinations of the second set's.</param>
    /// <param name="R">The correlation each pair of combinations achieves, descending.</param>
    /// <param name="U">The first set in canonical coordinates.</param>
    /// <param name="V">The second set in canonical coordinates.</param>
    /// <param name="Wilks">Wilks' lambda for each successive test.</param>
    /// <param name="ChiSquared">Bartlett's statistic for each.</param>
    /// <param name="Df">Its degrees of freedom.</param>
    /// <param name="P">The probability of a statistic that large under independence.</param>
    public readonly record struct Canonical(
        double[,] A,
        double[,] B,
        double[] R,
        double[,] U,
        double[,] V,
        double[] Wilks,
        double[] ChiSquared,
        double[] Df,
        double[] P);

    /// <summary>
    /// The combinations of two sets of variables that correlate most strongly with each other.
    /// </summary>
    /// <remarks>
    /// Both sets are factored first, so the analysis is carried out in an orthonormal basis for each
    /// and a set whose variables are collinear costs its redundant directions rather than the whole
    /// answer. The correlations are then singular values, which is why they come out ordered and
    /// bounded by one without anything having to enforce it.
    /// </remarks>
    public static Canonical CanonicalCorrelation(double[,] x, double[,] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        int n = x.GetLength(0);
        if (y.GetLength(0) != n)
        {
            throw new ArgumentException("Both sets must hold the same observations.", nameof(y));
        }

        int p = x.GetLength(1);
        int q = y.GetLength(1);
        if (n <= 1)
        {
            throw new ArgumentException("A correlation needs more than one observation.", nameof(x));
        }

        (double[,] centredX, double[] meanX) = Centre(x);
        (double[,] centredY, double[] meanY) = Centre(y);

        (double[,] qx, double[,] rx, int rankX) = ThinQr(centredX);
        (double[,] qy, double[,] ry, int rankY) = ThinQr(centredY);
        int d = Math.Min(rankX, rankY);
        if (d < 1)
        {
            throw new ArgumentException("Neither set varies, so no correlation is defined.", nameof(x));
        }

        double[,] cross = PrincipalComponents.Multiply(PrincipalComponents.Transpose(qx), qy);
        Svd svd = Svd.Factor(cross);

        double root = Math.Sqrt(n - 1);
        var a = new double[p, d];
        var b = new double[q, d];
        var r = new double[d];
        for (int c = 0; c < d; c++)
        {
            r[c] = Math.Clamp(svd.Values[c], 0, 1);

            double[] left = SolveUpper(rx, Column(svd.U, c, rankX));
            double[] right = SolveUpper(ry, Column(svd.V, c, rankY));
            for (int i = 0; i < p; i++)
            {
                a[i, c] = i < left.Length ? left[i] * root : 0;
            }

            for (int i = 0; i < q; i++)
            {
                b[i, c] = i < right.Length ? right[i] * root : 0;
            }
        }

        double[,] u = PrincipalComponents.Multiply(centredX, a);
        double[,] v = PrincipalComponents.Multiply(centredY, b);

        // Bartlett's sequence tests "are the remaining correlations all zero", one after another, so
        // the k-th lambda multiplies only the correlations from k on and the degrees of freedom shrink
        // by one row and one column of the cross-covariance each time.
        var wilks = new double[d];
        var chi = new double[d];
        var df = new double[d];
        var pValue = new double[d];
        for (int k = 0; k < d; k++)
        {
            double lambda = 1;
            for (int i = k; i < d; i++)
            {
                lambda *= 1 - (r[i] * r[i]);
            }

            wilks[k] = lambda;
            df[k] = (p - k) * (double)(q - k);
            chi[k] = lambda > 0
                ? -(n - 1 - ((p + q + 1) / 2.0)) * Math.Log(lambda)
                : double.PositiveInfinity;
            pValue[k] = df[k] > 0
                ? 1 - Distributions.ContinuousDistributions.Chi2Cdf(chi[k], df[k])
                : double.NaN;
        }

        _ = meanX;
        _ = meanY;
        return new Canonical(a, b, r, u, v, wilks, chi, df, pValue);
    }

    private static double[,] Pad(double[,] matrix, int width)
    {
        int n = matrix.GetLength(0);
        int q = matrix.GetLength(1);
        var padded = new double[n, width];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < q; c++)
            {
                padded[r, c] = matrix[r, c];
            }
        }

        return padded;
    }

    private static (double[,] Centred, double[] Means) Centre(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        int p = matrix.GetLength(1);
        var means = new double[p];
        for (int c = 0; c < p; c++)
        {
            for (int r = 0; r < n; r++)
            {
                means[c] += matrix[r, c];
            }

            means[c] /= n;
        }

        var centred = new double[n, p];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                centred[r, c] = matrix[r, c] - means[c];
            }
        }

        return (centred, means);
    }

    private static double FrobeniusNorm(double[,] matrix)
    {
        double total = 0;
        for (int r = 0; r < matrix.GetLength(0); r++)
        {
            for (int c = 0; c < matrix.GetLength(1); c++)
            {
                total += matrix[r, c] * matrix[r, c];
            }
        }

        return Math.Sqrt(total);
    }

    private static void Scale(double[,] matrix, double factor)
    {
        for (int r = 0; r < matrix.GetLength(0); r++)
        {
            for (int c = 0; c < matrix.GetLength(1); c++)
            {
                matrix[r, c] *= factor;
            }
        }
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

        return determinant;
    }

    /// <summary>
    /// A thin QR factorization by modified Gram-Schmidt, dropping the columns that add nothing.
    /// </summary>
    private static (double[,] Q, double[,] R, int Rank) ThinQr(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        int p = matrix.GetLength(1);
        var work = (double[,])matrix.Clone();
        var q = new double[n, p];
        var r = new double[p, p];
        int rank = 0;

        double scale = 0;
        for (int c = 0; c < p; c++)
        {
            for (int i = 0; i < n; i++)
            {
                scale = Math.Max(scale, Math.Abs(matrix[i, c]));
            }
        }

        double tolerance = Math.Max(n, p) * 2.220446049250313e-16 * Math.Max(scale, 1);
        for (int c = 0; c < p; c++)
        {
            var column = new double[n];
            for (int i = 0; i < n; i++)
            {
                column[i] = work[i, c];
            }

            for (int k = 0; k < rank; k++)
            {
                double dot = 0;
                for (int i = 0; i < n; i++)
                {
                    dot += q[i, k] * column[i];
                }

                r[k, c] = dot;
                for (int i = 0; i < n; i++)
                {
                    column[i] -= dot * q[i, k];
                }
            }

            double norm = 0;
            for (int i = 0; i < n; i++)
            {
                norm += column[i] * column[i];
            }

            norm = Math.Sqrt(norm);
            if (norm <= tolerance)
            {
                r[rank, c] = 0;
                continue;
            }

            r[rank, c] = norm;
            for (int i = 0; i < n; i++)
            {
                q[i, rank] = column[i] / norm;
            }

            rank++;
        }

        var thinQ = new double[n, rank];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < rank; k++)
            {
                thinQ[i, k] = q[i, k];
            }
        }

        var thinR = new double[rank, p];
        for (int k = 0; k < rank; k++)
        {
            for (int c = 0; c < p; c++)
            {
                thinR[k, c] = r[k, c];
            }
        }

        return (thinQ, thinR, rank);
    }

    private static double[] Column(double[,] matrix, int index, int length)
    {
        var column = new double[length];
        for (int i = 0; i < length && i < matrix.GetLength(0); i++)
        {
            column[i] = matrix[i, index];
        }

        return column;
    }

    /// <summary>Back-substitution through an upper-triangular factor that may be wider than it is tall.</summary>
    private static double[] SolveUpper(double[,] r, double[] rhs)
    {
        int rows = r.GetLength(0);
        int columns = r.GetLength(1);
        var answer = new double[columns];
        for (int i = Math.Min(rows, columns) - 1; i >= 0; i--)
        {
            double value = rhs[i];
            for (int j = i + 1; j < columns; j++)
            {
                value -= r[i, j] * answer[j];
            }

            answer[i] = Math.Abs(r[i, i]) > 1e-300 ? value / r[i, i] : 0;
        }

        return answer;
    }
}
