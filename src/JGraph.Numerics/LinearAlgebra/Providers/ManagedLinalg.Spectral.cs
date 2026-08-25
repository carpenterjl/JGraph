using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The managed backend's orthogonal factorizations and eigensolvers: Householder QR, one-sided
/// Jacobi SVD, cyclic Jacobi for a symmetric spectrum, and the real Schur form plus inverse
/// iteration for a general one. They are the kernels JGraph shipped before OpenBLAS arrived,
/// restated in LAPACK's storage and argument conventions so the two backends are interchangeable
/// rather than merely both correct.
/// </summary>
/// <remarks>
/// Two of these are honestly slower than their LAPACK counterparts by more than a constant.
/// <see cref="Gesdd"/> is one-sided Jacobi, which costs O(n³) per sweep rather than O(n³) in total,
/// and <see cref="Geev"/> recovers each eigenvector by inverse iteration — an O(n³) solve per
/// eigenvalue, so O(n⁴) for the matrix. Both are fallbacks: correct everywhere, fast enough for the
/// small matrices a script actually hands them, and never the path taken when the native library
/// loaded.
/// </remarks>
public sealed partial class ManagedLinalg
{
    /// <summary>Machine epsilon for double — the convergence floor the one-sided sweep measures against.</summary>
    private const double Epsilon = 2.220446049250313e-16;

    /// <summary>The sweep cap both Jacobi loops share; convergence is quadratic and never reaches it.</summary>
    private const int MaximumSweeps = 60;

    /// <inheritdoc />
    public override int Geqrf(int m, int n, Span<double> a, int lda, Span<double> tau)
    {
        int p = Math.Min(m, n);
        for (int k = 0; k < p; k++)
        {
            tau[k] = Reflector(a, lda, m, k, k);
            if (k + 1 < n)
            {
                ApplyReflectorLeft(a[((k * lda) + k)..], tau[k],
                    a[(((k + 1) * lda) + k)..], m - k, n - k - 1, lda);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Geqp3(int m, int n, Span<double> a, int lda, Span<int> jpvt, Span<double> tau)
    {
        for (int j = 0; j < n; j++)
        {
            jpvt[j] = j + 1;
        }

        int p = Math.Min(m, n);
        for (int k = 0; k < p; k++)
        {
            SwapInLargestColumn(a, lda, jpvt, m, n, k);
            tau[k] = Reflector(a, lda, m, k, k);
            if (k + 1 < n)
            {
                ApplyReflectorLeft(a[((k * lda) + k)..], tau[k],
                    a[(((k + 1) * lda) + k)..], m - k, n - k - 1, lda);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Orgqr(int m, int n, int k, Span<double> a, int lda, ReadOnlySpan<double> tau)
    {
        // Columns past the reflectors start as columns of the identity; the reflectors then turn
        // them into the rest of Q. They are applied in reverse, which is what turns the stored
        // vectors back into the product they represent.
        for (int j = k; j < n; j++)
        {
            a.Slice(j * lda, m).Clear();
            if (j < m)
            {
                a[(j * lda) + j] = 1;
            }
        }

        for (int i = k - 1; i >= 0; i--)
        {
            if (i + 1 < n)
            {
                ApplyReflectorLeft(a[((i * lda) + i)..], tau[i],
                    a[(((i + 1) * lda) + i)..], m - i, n - i - 1, lda);
            }

            for (int r = i + 1; r < m; r++)
            {
                a[(i * lda) + r] *= -tau[i];
            }

            a[(i * lda) + i] = 1 - tau[i];
            for (int r = 0; r < i; r++)
            {
                a[(i * lda) + r] = 0;
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Ormqr(bool leftSide, bool transpose, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> tau, Span<double> c, int ldc)
    {
        // Q = H₀·H₁·…·H_{k−1}, so multiplying by Q takes the reflectors in reverse and by Qᵀ takes
        // them forward — and the two swap over again when the product is from the right.
        bool forward = leftSide == transpose;
        for (int step = 0; step < k; step++)
        {
            int i = forward ? step : k - 1 - step;
            if (leftSide)
            {
                ApplyReflectorLeft(a[((i * lda) + i)..], tau[i], c[i..], m - i, n, ldc);
            }
            else
            {
                ApplyReflectorRight(a[((i * lda) + i)..], tau[i], c[(i * ldc)..], m, n - i, ldc);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Gesdd(SvdVectors job, int m, int n, Span<double> a, int lda,
        Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        // One-sided Jacobi wants at least as many rows as columns; a wide matrix factors as Aᵀ with
        // the two factors swapped. Either way the rotated columns are the tall side — the one that
        // can come out short of a full basis and need completing — and the accumulated rotations are
        // the other, which is orthogonal by construction because it is a product of plane rotations.
        bool transposed = m < n;
        int rows = transposed ? n : m;
        int order = transposed ? m : n;
        bool complete = job == SvdVectors.All && rows > order;

        double[] rotated = OneSidedJacobi(a, lda, m, n, transposed, rows, order, complete, s, out double[] turned);

        if (job == SvdVectors.None)
        {
            return 0;
        }

        double[] left = transposed ? turned : rotated;
        double[] right = transposed ? rotated : turned;
        int width = complete ? rows : order;

        for (int c = 0; c < (transposed ? order : width); c++)
        {
            new ReadOnlySpan<double>(left, c * m, m).CopyTo(u.Slice(c * ldu, m));
        }

        // V arrives as columns and leaves as rows: the contract's second factor is Vᵀ.
        for (int c = 0; c < (transposed ? width : order); c++)
        {
            for (int r = 0; r < n; r++)
            {
                vt[(r * ldvt) + c] = right[(c * n) + r];
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Gesvd(SvdVectors job, int m, int n, Span<double> a, int lda,
        Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt) =>
        Gesdd(job, m, n, a, lda, s, u, ldu, vt, ldvt);

    /// <inheritdoc />
    public override int Syevd(bool vectors, bool lower, int n, Span<double> a, int lda, Span<double> w)
    {
        if (n == 0)
        {
            return 0;
        }

        // Only the named triangle is the caller's promise, so the other one is mirrored rather than
        // read — which is what makes an almost-symmetric input give exactly the answer LAPACK would.
        var work = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                bool stored = lower ? r >= c : r <= c;
                work[(c * n) + r] = stored ? a[(c * lda) + r] : a[(r * lda) + c];
            }
        }

        double[] v = CyclicJacobi(work, n);

        // MATLAB reports a symmetric matrix's eigenvalues in ascending order, and so does LAPACK.
        var permutation = new int[n];
        for (int i = 0; i < n; i++)
        {
            permutation[i] = i;
        }

        Array.Sort(permutation, (x, y) => work[(x * n) + x].CompareTo(work[(y * n) + y]));

        for (int i = 0; i < n; i++)
        {
            w[i] = work[(permutation[i] * n) + permutation[i]];
        }

        if (vectors)
        {
            for (int i = 0; i < n; i++)
            {
                new ReadOnlySpan<double>(v, permutation[i] * n, n).CopyTo(a.Slice(i * lda, n));
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Geev(bool vectors, int n, Span<double> a, int lda,
        Span<double> wr, Span<double> wi, Span<double> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        // Compacted first, because the balancing works over a matrix whose leading dimension is its
        // own row count and this one's need not be.
        var work = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            a.Slice(c * lda, n).CopyTo(work.AsSpan(c * n, n));
        }

        // Balance next, exactly as LAPACK's own driver does — a badly scaled matrix otherwise spends
        // five of its digits on the scaling rather than on the answer, and the rescaling is by
        // powers of two, so it costs no accuracy of its own.
        double[] scaling = Balancing.InPlace(work, n);

        var matrix = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                matrix[r, c] = work[(c * n) + r];
            }
        }

        // The eigenvalues are read off the real Schur form. Doing it that way rather than running a
        // shifted QR in complex arithmetic is what makes them right: the Schur factor is produced by
        // an orthogonal similarity that is checked by reassembly, so the diagonal blocks carry the
        // matrix's own spectrum — the conjugate pairs come out exactly paired, and the values
        // reproduce the trace and the determinant to the last few digits rather than merely
        // approximately.
        Complex[] values = Schur.Factor(matrix).Eigenvalues;
        for (int i = 0; i < n; i++)
        {
            wr[i] = values[i].Real;
            wi[i] = values[i].Imaginary;
        }

        if (!vectors)
        {
            return 0;
        }

        for (int j = 0; j < n;)
        {
            Complex[] vector = Unbalance(InverseIteration(matrix, values[j]), scaling, n);
            if (j + 1 < n && wi[j] > 0 && wi[j + 1] < 0)
            {
                // A conjugate pair is stored once, as a real column and an imaginary one. The
                // second eigenvector is the first conjugated — computed rather than iterated for,
                // because the packing can only carry it if it is the conjugate exactly.
                for (int r = 0; r < n; r++)
                {
                    vr[(j * ldvr) + r] = vector[r].Real;
                    vr[((j + 1) * ldvr) + r] = vector[r].Imaginary;
                }

                j += 2;
            }
            else
            {
                for (int r = 0; r < n; r++)
                {
                    vr[(j * ldvr) + r] = vector[r].Real;
                }

                j++;
            }
        }

        return 0;
    }

    // --- Householder machinery ------------------------------------------------------------------

    /// <summary>
    /// LAPACK's <c>dlarfg</c> over column <paramref name="col"/> from row <paramref name="row"/>
    /// down: the entries below the diagonal become the reflector vector, whose leading 1 stays
    /// implied, the diagonal becomes R's, and the returned scalar finishes the reflector. A column
    /// already zero below the diagonal returns zero — the identity reflection — which is what
    /// leaves <c>qr</c> of a triangular matrix alone rather than negating it.
    /// </summary>
    private static double Reflector(Span<double> a, int lda, int m, int row, int col)
    {
        int offset = (col * lda) + row;
        double alpha = a[offset];

        double below = 0;
        for (int r = row + 1; r < m; r++)
        {
            below = Math.Sqrt((below * below) + (a[(col * lda) + r] * a[(col * lda) + r]));
        }

        if (below == 0)
        {
            return 0;
        }

        double norm = Math.Sqrt((alpha * alpha) + (below * below));
        double beta = alpha >= 0 ? -norm : norm;
        double scale = 1.0 / (alpha - beta);
        for (int r = row + 1; r < m; r++)
        {
            a[(col * lda) + r] *= scale;
        }

        a[offset] = beta;
        return (beta - alpha) / beta;
    }

    /// <summary>
    /// C := (I − τ·v·vᵀ)·C, where <paramref name="v"/> starts at the reflector's diagonal entry —
    /// whose stored value belongs to R and is ignored, the vector's leading entry being an implied 1.
    /// </summary>
    private static void ApplyReflectorLeft(ReadOnlySpan<double> v, double tau,
        Span<double> c, int rows, int cols, int ldc)
    {
        if (tau == 0)
        {
            return;
        }

        for (int j = 0; j < cols; j++)
        {
            Span<double> column = c.Slice(j * ldc, rows);
            double s = column[0];
            for (int r = 1; r < rows; r++)
            {
                s += v[r] * column[r];
            }

            s *= tau;
            column[0] -= s;
            for (int r = 1; r < rows; r++)
            {
                column[r] -= s * v[r];
            }
        }
    }

    /// <summary>C := C·(I − τ·v·vᵀ), the same reflection applied from the other side.</summary>
    private static void ApplyReflectorRight(ReadOnlySpan<double> v, double tau,
        Span<double> c, int rows, int cols, int ldc)
    {
        if (tau == 0)
        {
            return;
        }

        for (int r = 0; r < rows; r++)
        {
            double s = c[r];
            for (int j = 1; j < cols; j++)
            {
                s += v[j] * c[(j * ldc) + r];
            }

            s *= tau;
            c[r] -= s;
            for (int j = 1; j < cols; j++)
            {
                c[(j * ldc) + r] -= s * v[j];
            }
        }
    }

    /// <summary>
    /// Moves the largest remaining column into position <paramref name="k"/>, measuring each by the
    /// part of it the reflections still have to reach — rows k downward. Recomputed rather than
    /// updated downdate-style: a downdated norm loses the accuracy that is the entire reason for
    /// pivoting.
    /// </summary>
    private static void SwapInLargestColumn(Span<double> a, int lda, Span<int> jpvt, int m, int n, int k)
    {
        int best = k;
        double largest = -1;
        for (int c = k; c < n; c++)
        {
            double sum = 0;
            for (int r = k; r < m; r++)
            {
                sum += a[(c * lda) + r] * a[(c * lda) + r];
            }

            if (sum > largest)
            {
                largest = sum;
                best = c;
            }
        }

        if (best == k)
        {
            return;
        }

        for (int r = 0; r < m; r++)
        {
            (a[(k * lda) + r], a[(best * lda) + r]) = (a[(best * lda) + r], a[(k * lda) + r]);
        }

        (jpvt[k], jpvt[best]) = (jpvt[best], jpvt[k]);
    }

    // --- Jacobi ---------------------------------------------------------------------------------

    /// <summary>
    /// One-sided Jacobi: sweep column pairs, rotating each pair orthogonal until every pair already
    /// is. The rotated columns' norms are then the singular values and their directions the tall
    /// factor's columns, and the accumulated rotations — returned through <paramref name="turned"/>
    /// — are the other factor.
    /// </summary>
    private static double[] OneSidedJacobi(ReadOnlySpan<double> a, int lda, int m, int n,
        bool transposed, int rows, int order, bool complete, Span<double> values, out double[] turned)
    {
        var b = new double[(long)rows * (complete ? rows : order)];
        for (int c = 0; c < order; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                b[(c * rows) + r] = transposed ? a[(r * lda) + c] : a[(c * lda) + r];
            }
        }

        var v = new double[(long)order * order];
        for (int i = 0; i < order; i++)
        {
            v[(i * order) + i] = 1;
        }

        for (int sweep = 0; sweep < MaximumSweeps; sweep++)
        {
            bool rotated = false;
            for (int p = 0; p < order - 1; p++)
            {
                for (int q = p + 1; q < order; q++)
                {
                    double alpha = 0, beta = 0, gamma = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        alpha += b[(p * rows) + r] * b[(p * rows) + r];
                        beta += b[(q * rows) + r] * b[(q * rows) + r];
                        gamma += b[(p * rows) + r] * b[(q * rows) + r];
                    }

                    if (Math.Abs(gamma) <= Epsilon * Math.Sqrt(alpha * beta) || gamma == 0)
                    {
                        continue;
                    }

                    rotated = true;
                    double zeta = (beta - alpha) / (2 * gamma);

                    // A ζ of exactly zero means the two columns have the same norm, and the rotation
                    // that makes them orthogonal is exactly 45°. Math.Sign answers 0 there, which asks
                    // for no rotation at all — so a matrix whose equal-norm columns are parallel, like
                    // a matrix of ones, would never converge and would come out looking full rank.
                    double direction = zeta >= 0 ? 1 : -1;
                    double t = direction / (Math.Abs(zeta) + Math.Sqrt(1 + (zeta * zeta)));
                    double c = 1 / Math.Sqrt(1 + (t * t));
                    double s = c * t;

                    for (int r = 0; r < rows; r++)
                    {
                        double bp = b[(p * rows) + r];
                        b[(p * rows) + r] = (c * bp) - (s * b[(q * rows) + r]);
                        b[(q * rows) + r] = (s * bp) + (c * b[(q * rows) + r]);
                    }

                    for (int r = 0; r < order; r++)
                    {
                        double vp = v[(p * order) + r];
                        v[(p * order) + r] = (c * vp) - (s * v[(q * order) + r]);
                        v[(q * order) + r] = (s * vp) + (c * v[(q * order) + r]);
                    }
                }
            }

            if (!rotated)
            {
                break;
            }
        }

        // Singular values are the rotated columns' norms; the tall factor holds their directions.
        var sigma = new double[order];
        for (int c = 0; c < order; c++)
        {
            double norm = 0;
            for (int r = 0; r < rows; r++)
            {
                norm += b[(c * rows) + r] * b[(c * rows) + r];
            }

            sigma[c] = Math.Sqrt(norm);
        }

        SortDescending(sigma, b, rows, v, order);

        // A column the sweeps annihilated has no direction left to normalize — dividing what
        // remains of it by its own vanishing norm scales rounding error up to unit length, and two
        // such columns are then unit vectors pointing wherever the noise pointed, which is not a
        // basis. Below the cutoff the direction is discarded and replaced by a real one; the value
        // itself is still reported as computed, because it is accurate to within it.
        double cutoff = rows * Epsilon * (order > 0 ? sigma[0] : 0);
        int width = complete ? rows : order;
        var missing = new bool[width];
        for (int c = 0; c < order; c++)
        {
            values[c] = sigma[c];
            if (sigma[c] > cutoff)
            {
                for (int r = 0; r < rows; r++)
                {
                    b[(c * rows) + r] /= sigma[c];
                }
            }
            else
            {
                Array.Clear(b, c * rows, rows);
                missing[c] = true;
            }
        }

        for (int c = order; c < width; c++)
        {
            missing[c] = true;
        }

        CompleteBasis(b, rows, width, missing);

        turned = v;
        return b;
    }

    /// <summary>
    /// Cyclic Jacobi over a symmetric n×n column-major matrix, rotating away one off-diagonal pair
    /// at a time. <paramref name="work"/> is left with the eigenvalues on its diagonal, and the
    /// returned matrix holds the eigenvectors — one per column, in that same order.
    /// </summary>
    private static double[] CyclicJacobi(double[] work, int n)
    {
        var v = new double[(long)n * n];
        for (int i = 0; i < n; i++)
        {
            v[(i * n) + i] = 1;
        }

        for (int sweep = 0; sweep < MaximumSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    off += work[(q * n) + p] * work[(q * n) + p];
                }
            }

            if (off < 1e-30)
            {
                break;
            }

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    if (work[(q * n) + p] == 0)
                    {
                        continue;
                    }

                    double theta = (work[(q * n) + q] - work[(p * n) + p]) / (2 * work[(q * n) + p]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(1 + (theta * theta)));
                    if (theta == 0)
                    {
                        t = 1;
                    }

                    double c = 1 / Math.Sqrt(1 + (t * t));
                    double s = t * c;

                    for (int r = 0; r < n; r++)
                    {
                        double arp = work[(p * n) + r];
                        double arq = work[(q * n) + r];
                        work[(p * n) + r] = (c * arp) - (s * arq);
                        work[(q * n) + r] = (s * arp) + (c * arq);
                    }

                    for (int col = 0; col < n; col++)
                    {
                        double apc = work[(col * n) + p];
                        double aqc = work[(col * n) + q];
                        work[(col * n) + p] = (c * apc) - (s * aqc);
                        work[(col * n) + q] = (s * apc) + (c * aqc);
                    }

                    for (int r = 0; r < n; r++)
                    {
                        double vrp = v[(p * n) + r];
                        double vrq = v[(q * n) + r];
                        v[(p * n) + r] = (c * vrp) - (s * vrq);
                        v[(q * n) + r] = (s * vrp) + (c * vrq);
                    }
                }
            }
        }

        return v;
    }

    private static void SortDescending(double[] sigma, double[] u, int rows, double[] v, int order)
    {
        for (int i = 0; i < order - 1; i++)
        {
            int biggest = i;
            for (int j = i + 1; j < order; j++)
            {
                if (sigma[j] > sigma[biggest])
                {
                    biggest = j;
                }
            }

            if (biggest != i)
            {
                (sigma[i], sigma[biggest]) = (sigma[biggest], sigma[i]);
                SwapColumns(u, rows, i, biggest);
                SwapColumns(v, order, i, biggest);
            }
        }
    }

    private static void SwapColumns(double[] matrix, int rows, int a, int b)
    {
        for (int r = 0; r < rows; r++)
        {
            (matrix[(a * rows) + r], matrix[(b * rows) + r]) =
                (matrix[(b * rows) + r], matrix[(a * rows) + r]);
        }
    }

    /// <summary>
    /// Fills the columns flagged in <paramref name="missing"/> with unit vectors orthogonal to
    /// every other column and to each other, so a factor whose singular values ran out still has a
    /// whole orthonormal basis in it.
    /// </summary>
    /// <remarks>
    /// Each new direction starts from the standard basis vector that sticks out of the span the
    /// furthest, tracked incrementally. Taking the first candidate that merely clears a threshold
    /// is what the earlier version did, and it fails twice over: on a nearly-full span there may be
    /// no such candidate at all — leaving a column of zeros in a matrix that promised orthonormal
    /// ones — and a candidate that barely clears it is mostly inside the span already, so
    /// orthogonalizing it amplifies whatever rounding it carried in.
    /// </remarks>
    private static void CompleteBasis(double[] u, int rows, int columns, bool[] missing)
    {
        var residual = new double[rows];
        Array.Fill(residual, 1.0);
        for (int c = 0; c < columns; c++)
        {
            if (missing[c])
            {
                continue;
            }

            for (int r = 0; r < rows; r++)
            {
                residual[r] -= u[(c * rows) + r] * u[(c * rows) + r];
            }
        }

        var candidate = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            if (!missing[c])
            {
                continue;
            }

            int basis = 0;
            for (int r = 1; r < rows; r++)
            {
                if (residual[r] > residual[basis])
                {
                    basis = r;
                }
            }

            Array.Clear(candidate);
            candidate[basis] = 1;

            // Twice: one Gram-Schmidt sweep leaves a candidate that started close to the span with
            // as much error as answer, and the second sweep takes it back out.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int other = 0; other < columns; other++)
                {
                    if (missing[other])
                    {
                        continue;
                    }

                    double projection = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        projection += candidate[r] * u[(other * rows) + r];
                    }

                    for (int r = 0; r < rows; r++)
                    {
                        candidate[r] -= projection * u[(other * rows) + r];
                    }
                }
            }

            double norm = 0;
            for (int r = 0; r < rows; r++)
            {
                norm += candidate[r] * candidate[r];
            }

            if (norm <= 0)
            {
                residual[basis] = 0; // that direction is spent; try another for the next column
                continue;
            }

            norm = Math.Sqrt(norm);
            for (int r = 0; r < rows; r++)
            {
                double entry = candidate[r] / norm;
                u[(c * rows) + r] = entry;
                residual[r] -= entry * entry;
            }

            missing[c] = false;
        }
    }

    // --- Inverse iteration ----------------------------------------------------------------------

    /// <summary>
    /// LAPACK's <c>dgebak</c>: an eigenvector of the balanced matrix becomes one of the original by
    /// undoing the diagonal scaling, and is renormalized afterwards because that undoing changes its
    /// length — the contract's unit 2-norm has to survive the trip back.
    /// </summary>
    private static Complex[] Unbalance(Complex[] vector, double[] scaling, int n)
    {
        double norm = 0;
        for (int i = 0; i < n; i++)
        {
            vector[i] *= scaling[i];
            norm += vector[i].Magnitude * vector[i].Magnitude;
        }

        norm = Math.Sqrt(norm);
        if (norm == 0 || double.IsNaN(norm) || double.IsInfinity(norm))
        {
            return vector;
        }

        int biggest = 0;
        for (int i = 0; i < n; i++)
        {
            vector[i] /= norm;
            if (vector[i].Magnitude > vector[biggest].Magnitude)
            {
                biggest = i;
            }
        }

        // The phase is free, and fixing it on the largest entry is what makes the answer the same
        // one twice running rather than merely a correct one each time.
        if (vector[biggest].Magnitude > 0)
        {
            Complex phase = vector[biggest] / vector[biggest].Magnitude;
            for (int i = 0; i < n; i++)
            {
                vector[i] /= phase;
            }
        }

        return vector;
    }

    /// <summary>Inverse iteration: a few solves against (A − λ̃I) pull out λ's eigenvector.</summary>
    private static Complex[] InverseIteration(double[,] matrix, Complex value)
    {
        int n = matrix.GetLength(0);
        double scale = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                scale = Math.Max(scale, Math.Abs(matrix[r, c]));
            }
        }

        // Perturb the shift so the system is merely ill-conditioned, not exactly singular —
        // ill-conditioned is exactly what makes inverse iteration converge in one or two solves.
        Complex shift = value + ((scale + 1) * 1e-10);
        var shifted = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                shifted[r, c] = matrix[r, c];
            }

            shifted[r, r] -= shift;
        }

        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = 1.0 / Math.Sqrt(n);
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            Complex[] next = SolveComplex(shifted, x);
            double norm = 0;
            foreach (Complex entry in next)
            {
                norm += entry.Magnitude * entry.Magnitude;
            }

            norm = Math.Sqrt(norm);
            if (norm == 0 || double.IsNaN(norm) || double.IsInfinity(norm))
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                x[i] = next[i] / norm;
            }
        }

        // Fix the free phase: make the largest entry real and positive, so results are stable.
        int biggest = 0;
        for (int i = 1; i < n; i++)
        {
            if (x[i].Magnitude > x[biggest].Magnitude)
            {
                biggest = i;
            }
        }

        if (x[biggest].Magnitude > 0)
        {
            Complex phase = x[biggest] / x[biggest].Magnitude;
            for (int i = 0; i < n; i++)
            {
                x[i] /= phase;
            }
        }

        return x;
    }

    /// <summary>Complex Gaussian elimination with partial pivoting.</summary>
    private static Complex[] SolveComplex(Complex[,] matrix, Complex[] b)
    {
        int n = b.Length;
        var a = (Complex[,])matrix.Clone();
        var x = (Complex[])b.Clone();

        for (int k = 0; k < n; k++)
        {
            int best = k;
            for (int r = k + 1; r < n; r++)
            {
                if (a[r, k].Magnitude > a[best, k].Magnitude)
                {
                    best = r;
                }
            }

            if (best != k)
            {
                for (int c = k; c < n; c++)
                {
                    (a[k, c], a[best, c]) = (a[best, c], a[k, c]);
                }

                (x[k], x[best]) = (x[best], x[k]);
            }

            if (a[k, k] == Complex.Zero)
            {
                a[k, k] = 1e-300; // keep the elimination moving on an exactly singular pivot
            }

            for (int r = k + 1; r < n; r++)
            {
                Complex factor = a[r, k] / a[k, k];
                for (int c = k + 1; c < n; c++)
                {
                    a[r, c] -= factor * a[k, c];
                }

                x[r] -= factor * x[k];
            }
        }

        for (int k = n - 1; k >= 0; k--)
        {
            for (int c = k + 1; c < n; c++)
            {
                x[k] -= a[k, c] * x[c];
            }

            x[k] /= a[k, k];
        }

        return x;
    }
}
