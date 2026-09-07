using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics.Sparse;

/// <summary>One singular triplet and how far it still is from being one.</summary>
/// <param name="Value">The singular value.</param>
/// <param name="Left">Its left singular vector, length m.</param>
/// <param name="Right">Its right singular vector, length n.</param>
/// <param name="Residual">The Ritz residual, which is what the convergence test reads.</param>
public readonly record struct SingularTriplet(double Value, double[] Left, double[] Right, double Residual);

/// <summary>
/// A few singular values of a sparse matrix — <c>svds</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two routes, and which one is taken turns on a single question the method has no other answer
/// to: repeated singular values. A Krylov space grown from one starting vector contains one
/// vector per <em>distinct</em> singular value, so a value of multiplicity two is found once and
/// the third answer returned is the fourth singular value. That is not a corner case — a matrix
/// with any symmetry in it has repeated values, and the discrete Laplacian on a square grid, the
/// first thing anyone tries <c>svds</c> on, is full of them. Up to a smaller dimension of 512 the
/// whole decomposition is therefore taken and the wanted end picked off it: it is exact, it never
/// fails to converge, and at that size it costs less than being careful would.
/// </para>
/// <para>
/// Above it the iteration is the only affordable answer, and it is Golub–Kahan–Lanczos
/// bidiagonalization with full reorthogonalization: an orthonormal pair of bases, one in each of
/// the matrix's two spaces, whose projection is upper bidiagonal. The Ritz values are that
/// projection's singular values, and the residual of triplet <c>i</c> is the last off-diagonal
/// times the last component of the corresponding right singular vector — a scalar, so the
/// convergence test costs nothing.
/// </para>
/// <para>
/// Bidiagonalizing rather than running Arnoldi on the augmented matrix <c>[0 A; A' 0]</c> is a
/// choice about accuracy. Both build the same Krylov space and neither squares anything, but the
/// augmented form's spectrum comes in <c>±sigma</c> pairs, and a symmetric solver asked to
/// separate a pair degenerate to working precision hands back vectors mixed between the two. The
/// bidiagonalization never sees the pair.
/// </para>
/// <para>
/// Read against R2025b's <c>svds.m</c> for the option names, the defaults (tolerance 1e-10,
/// subspace <c>max(3k, 15)</c>), the descending order every selection answers in, and the meaning
/// of <c>flag</c>.
/// </para>
/// </remarks>
public static class SparseSingularValues
{
    /// <summary>Which end of the spectrum is wanted.</summary>
    public enum Selection
    {
        /// <summary>The k largest.</summary>
        Largest,

        /// <summary>The k smallest, zeros included.</summary>
        Smallest,

        /// <summary>The k smallest that are not numerically zero.</summary>
        SmallestNonZero,

        /// <summary>The k nearest a given number.</summary>
        Nearest,
    }

    /// <summary>
    /// The k wanted triplets, chosen by <paramref name="selection"/> and handed back largest
    /// first — which is the order <c>svds</c> answers in even when it was asked for the smallest.
    /// </summary>
    /// <param name="a">The matrix.</param>
    /// <param name="k">How many triplets are wanted.</param>
    /// <param name="selection">Which end.</param>
    /// <param name="sigma">The target, for <see cref="Selection.Nearest"/>.</param>
    /// <param name="tolerance">Relative residual to accept a triplet at.</param>
    /// <param name="subspace">The largest Krylov space to build before giving up.</param>
    /// <param name="converged">Whether every returned triplet met the tolerance.</param>
    public static SingularTriplet[] Compute(CscMatrix a, int k, Selection selection, double sigma,
        double tolerance, int? subspace, out bool converged)
    {
        int m = a.Rows;
        int n = a.Cols;
        int rank = Math.Min(m, n);
        k = Math.Min(k, rank);
        if (k <= 0)
        {
            converged = true;
            return [];
        }

        // A Krylov space grown from one vector holds one vector per distinct singular value, so a
        // repeated one is found once however long the iteration runs — and a repeated singular
        // value is not exotic: any matrix with a symmetry has them. Below this size the whole
        // decomposition is cheaper than being careful, and it is the only route that is right, so
        // it is the route taken. Above it the iteration is the only affordable answer and the
        // largest end, which is what a large matrix is asked for, is where it is dependable.
        if (rank <= 512)
        {
            converged = true;
            return Dense(a, k, selection, sigma);
        }

        // The small end and an interior target need a much larger space than the largest end does,
        // and there is a ceiling on how large a space is affordable: past it the run reports that
        // it did not converge rather than allocating a basis the size of the matrix.
        int wanted = subspace ?? Math.Max(3 * k, 15);
        int cap = Math.Min(rank, selection == Selection.Largest ? wanted : (4 * wanted) + 100);

        CscMatrix transposed = a.Transpose();
        var left = new List<double[]>(cap);
        var right = new List<double[]>(cap + 1);
        var alpha = new List<double>(cap);
        var beta = new List<double>(cap);

        double[] v = StartingVector(n);
        right.Add(v);

        SingularTriplet[] answer = [];
        converged = false;
        for (int j = 1; j <= cap; j++)
        {
            double[] u = a.MultiplyVector(right[j - 1]);
            if (j > 1)
            {
                u = Subtract(u, beta[j - 2], left[j - 2]);
            }

            Orthogonalize(u, left);
            double alphaJ = Length(u);
            if (alphaJ <= 0)
            {
                break;
            }

            Divide(u, alphaJ);
            left.Add(u);
            alpha.Add(alphaJ);

            double[] next = Subtract(transposed.MultiplyVector(u), alphaJ, right[j - 1]);
            Orthogonalize(next, right);
            double betaJ = Length(next);
            beta.Add(betaJ);

            // The Ritz problem is tiny and dense, so it goes through the same SVD everything else
            // in JGraph goes through rather than through a bidiagonal solver of its own.
            answer = Ritz(left, right, alpha, beta, j, k, selection, sigma);
            double scale = answer.Length == 0 ? 1 : Math.Max(alpha[0], answer.Max(t => t.Value));
            if (answer.Length == k && answer.All(t => t.Residual <= tolerance * scale))
            {
                converged = true;
                break;
            }

            if (betaJ <= 0 || j == cap)
            {
                break;
            }

            Divide(next, betaJ);
            right.Add(next);
        }

        // A space that spanned the whole of the smaller dimension has produced the exact answer,
        // whatever the residual estimate says about the last digits.
        if (left.Count >= rank)
        {
            converged = true;
        }

        return answer;
    }

    /// <summary>
    /// The whole decomposition, with the wanted end picked off it. Whichever rule chose them, the
    /// triplets come back in descending order of singular value, which is the order <c>svds</c>
    /// answers in even when it was asked for the smallest.
    /// </summary>
    private static SingularTriplet[] Dense(CscMatrix a, int k, Selection selection, double sigma)
    {
        int m = a.Rows;
        int n = a.Cols;
        Svd factored = Svd.Factor(a.ToColumnMajor(), m, n);
        double[] values = factored.Values;
        int[] order = [.. Enumerable.Range(0, values.Length)];
        if (selection == Selection.SmallestNonZero)
        {
            double threshold = (values.Length == 0 ? 0 : values[0]) * Math.Max(m, n)
                * 2.220446049250313e-16;
            order = [.. order.Where(i => values[i] > threshold)];
        }

        Array.Sort(order, (p, q) => selection switch
        {
            Selection.Largest => values[q].CompareTo(values[p]),
            Selection.Nearest => Math.Abs(values[p] - sigma).CompareTo(Math.Abs(values[q] - sigma)),
            _ => values[p].CompareTo(values[q]),
        });

        int[] taken = [.. order.Take(Math.Min(k, order.Length)).OrderByDescending(i => values[i])];
        var answer = new SingularTriplet[taken.Length];
        for (int t = 0; t < taken.Length; t++)
        {
            int c = taken[t];
            var u = new double[m];
            var v = new double[n];
            for (int i = 0; i < m; i++)
            {
                u[i] = factored.UColumnMajor[(c * m) + i];
            }

            for (int i = 0; i < n; i++)
            {
                v[i] = factored.VColumnMajor[(c * n) + i];
            }

            answer[t] = new SingularTriplet(values[c], u, v, 0);
        }

        return answer;
    }

    /// <summary>
    /// A deterministic starting vector. <c>svds</c> seeds its own stream so that repeated calls
    /// agree with each other; the same promise is kept here without touching the session's stream,
    /// which a script is entitled to expect <c>svds</c> not to disturb.
    /// </summary>
    private static double[] StartingVector(int n)
    {
        var v = new double[n];
        var random = new Random(0);
        for (int i = 0; i < n; i++)
        {
            v[i] = random.NextDouble() - 0.5;
        }

        double length = Length(v);
        if (length == 0)
        {
            v[0] = 1;
            return v;
        }

        Divide(v, length);
        return v;
    }

    private static SingularTriplet[] Ritz(List<double[]> left, List<double[]> right,
        List<double> alpha, List<double> beta, int j, int k, Selection selection, double sigma)
    {
        var bidiagonal = new double[j * j];
        for (int i = 0; i < j; i++)
        {
            bidiagonal[(i * j) + i] = alpha[i];
            if (i + 1 < j)
            {
                bidiagonal[((i + 1) * j) + i] = beta[i];
            }
        }

        Svd factored = Svd.Factor(bidiagonal, j, j);
        double[] values = factored.Values;
        int[] order = Enumerable.Range(0, values.Length).ToArray();
        Array.Sort(order, (p, q) => selection switch
        {
            Selection.Largest => values[q].CompareTo(values[p]),
            Selection.Nearest => Math.Abs(values[p] - sigma).CompareTo(Math.Abs(values[q] - sigma)),
            _ => values[p].CompareTo(values[q]),
        });

        if (selection == Selection.SmallestNonZero)
        {
            double threshold = (values.Length == 0 ? 0 : values.Max())
                * Math.Max(left[0].Length, right[0].Length) * 2.220446049250313e-16;
            order = [.. order.Where(i => values[i] > threshold)];
        }

        int take = Math.Min(k, order.Length);

        // Whichever end was asked for, the answer is handed back largest first.
        order = [.. order.Take(take).OrderByDescending(i => values[i])];
        var answer = new SingularTriplet[take];
        double lastBeta = beta[j - 1];
        for (int t = 0; t < take; t++)
        {
            int column = order[t];
            var u = new double[left[0].Length];
            var vv = new double[right[0].Length];
            for (int i = 0; i < j; i++)
            {
                double lw = factored.UColumnMajor[(column * j) + i];
                double rw = factored.VColumnMajor[(column * j) + i];
                for (int p = 0; p < u.Length; p++)
                {
                    u[p] += lw * left[i][p];
                }

                for (int p = 0; p < vv.Length; p++)
                {
                    vv[p] += rw * right[i][p];
                }
            }

            double residual = Math.Abs(lastBeta * factored.VColumnMajor[(column * j) + j - 1]);
            answer[t] = new SingularTriplet(values[column], u, vv, residual);
        }

        return answer;
    }

    /// <summary>
    /// Full reorthogonalization, twice. Once is not enough when the vector being added is nearly in
    /// the span already, which is exactly the situation a converging Lanczos process spends most of
    /// its time in.
    /// </summary>
    private static void Orthogonalize(double[] x, List<double[]> basis)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (double[] q in basis)
            {
                double projection = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    projection += q[i] * x[i];
                }

                for (int i = 0; i < x.Length; i++)
                {
                    x[i] -= projection * q[i];
                }
            }
        }
    }

    private static double[] Subtract(double[] a, double s, double[] b)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            y[i] = a[i] - (s * b[i]);
        }

        return y;
    }

    private static double Length(double[] x) => NormEstimators.VectorNorm(x);

    private static void Divide(double[] x, double s)
    {
        for (int i = 0; i < x.Length; i++)
        {
            x[i] /= s;
        }
    }
}
