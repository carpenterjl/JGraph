using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The real Schur decomposition A = U·T·Uᵀ, with U orthogonal and T quasi-upper-triangular: 1×1
/// blocks on the diagonal for real eigenvalues and 2×2 blocks for conjugate pairs.
/// </summary>
/// <remarks>
/// <para>
/// The reduction is the Francis double-shift QR iteration run on the Hessenberg form
/// <see cref="Hessenberg"/> already produces. Two shifts are taken implicitly from the trailing 2×2
/// block, so a conjugate pair is handled without ever leaving real arithmetic — which is the whole
/// reason the real form has 2×2 blocks in it.
/// </para>
/// <para>
/// <see cref="Reorder"/> then moves chosen eigenvalues to the top by repeatedly exchanging adjacent
/// diagonal blocks. Each exchange solves the small Sylvester equation that names the invariant
/// subspace of the second block and rotates onto it, which works uniformly for 1×1 and 2×2 blocks
/// instead of needing a case for each of the four pairings.
/// </para>
/// </remarks>
public sealed class Schur
{
    /// <summary>The relative floor a subdiagonal entry has to fall below to count as deflated.</summary>
    private const double Epsilon = 2.220446049250313e-16;

    /// <summary>QR steps allowed per eigenvalue before the iteration is declared stuck.</summary>
    private const int MaxStepsPerEigenvalue = 120;

    private Schur(double[,] t, double[,] u)
    {
        T = t;
        U = u;
    }

    /// <summary>The quasi-upper-triangular factor.</summary>
    public double[,] T { get; }

    /// <summary>The orthogonal factor, so that U·T·Uᵀ reproduces the matrix.</summary>
    public double[,] U { get; }

    /// <summary>The eigenvalues, read off the diagonal blocks in the order they appear.</summary>
    public Complex[] Eigenvalues => EigenvaluesOf(T);

    /// <summary>Factors a square matrix into its real Schur form, through the active backend.</summary>
    public static Schur Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        if (n != matrix.GetLength(1))
        {
            throw new ArgumentException("The Schur decomposition needs a square matrix.", nameof(matrix));
        }

        if (n == 0)
        {
            return new Schur(new double[0, 0], new double[0, 0]);
        }

        double[] work = Flatten(matrix, n);
        var u = new double[(long)n * n];

        var real = new double[n];
        var imaginary = new double[n];
        if (LinalgProvider.Current.Gees(vectors: true, n, work, n, real, imaginary, u, n) != 0)
        {
            throw new InvalidOperationException("The Schur iteration did not converge.");
        }

        return new Schur(Rebuild(work, n), Rebuild(u, n));
    }

    /// <summary>
    /// The managed kernel behind <see cref="Factor"/>: Hessenberg reduction and the Francis
    /// double-shift iteration. <see cref="ManagedLinalg"/> reaches it directly — the public door
    /// routes through the provider, and this one is what the provider's managed lane answers with.
    /// </summary>
    internal static Schur FactorManaged(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n == 0)
        {
            return new Schur(new double[0, 0], new double[0, 0]);
        }

        Hessenberg reduction = Hessenberg.Reduce(matrix);
        double[,] h = reduction.H;
        double[,] u = reduction.Q;

        Iterate(h, u, n);

        // Below the subdiagonal the iteration leaves rounding dust that the block structure says is
        // exactly zero; a caller reading the blocks should not have to filter it.
        for (int i = 2; i < n; i++)
        {
            for (int j = 0; j < i - 1; j++)
            {
                h[i, j] = 0;
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (h[i + 1, i] != 0)
            {
                Standardize(h, u, n, i);
                i++;
            }
        }

        return new Schur(h, u);
    }

    /// <summary>
    /// The eigenvalues of a quasi-triangular matrix, in the order its diagonal blocks appear —
    /// which is what makes a reordering visible to a caller.
    /// </summary>
    public static Complex[] EigenvaluesOf(double[,] t)
    {
        ArgumentNullException.ThrowIfNull(t);
        int n = t.GetLength(0);
        var values = new Complex[n];

        for (int i = 0; i < n;)
        {
            if (i + 1 < n && t[i + 1, i] != 0)
            {
                double half = 0.5 * (t[i, i] + t[i + 1, i + 1]);
                double discriminant = 0.25 * (t[i, i] - t[i + 1, i + 1]) * (t[i, i] - t[i + 1, i + 1])
                    + t[i, i + 1] * t[i + 1, i];

                if (discriminant < 0)
                {
                    double imaginary = Math.Sqrt(-discriminant);
                    values[i] = new Complex(half, imaginary);
                    values[i + 1] = new Complex(half, -imaginary);
                }
                else
                {
                    // A 2×2 block that turns out to have real eigenvalues after all; standardizing
                    // should have split it, but reading it correctly costs nothing.
                    double root = Math.Sqrt(discriminant);
                    values[i] = new Complex(half + root, 0);
                    values[i + 1] = new Complex(half - root, 0);
                }

                i += 2;
            }
            else
            {
                values[i] = new Complex(t[i, i], 0);
                i++;
            }
        }

        return values;
    }

    /// <summary>
    /// Reorders a real Schur form so the eigenvalues marked in <paramref name="select"/> come first,
    /// keeping U·T·Uᵀ equal to the matrix it came from. The selection is per eigenvalue in
    /// <see cref="EigenvaluesOf"/> order; a 2×2 block moves if either of its pair is selected,
    /// because a conjugate pair cannot be separated inside a real form.
    /// </summary>
    public static Schur Reorder(double[,] t, double[,] u, bool[] select)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(select);

        int n = t.GetLength(0);
        if (select.Length != n)
        {
            throw new ArgumentException($"The selection needs one entry per eigenvalue ({n}).", nameof(select));
        }

        if (n == 0)
        {
            return new Schur(new double[0, 0], new double[0, 0]);
        }

        double[] tFlat = Flatten(t, n);
        double[] uFlat = Flatten(u, n);
        var real = new double[n];
        var imaginary = new double[n];
        if (LinalgProvider.Current.Trsen(select, n, tFlat, n, uFlat, n, real, imaginary) != 0)
        {
            throw new InvalidOperationException("Reordering the Schur form failed.");
        }

        return new Schur(Rebuild(tFlat, n), Rebuild(uFlat, n));
    }

    /// <summary>The managed block-exchange reorder behind <see cref="Reorder"/>, one swap at a time.</summary>
    internal static Schur ReorderManaged(double[,] t, double[,] u, bool[] select)
    {
        int n = t.GetLength(0);
        double[,] tt = (double[,])t.Clone();
        double[,] uu = (double[,])u.Clone();

        // The flags travel with their blocks, so a block that has already been moved up is not
        // considered again when the scan reaches the position it used to occupy.
        bool[] wanted = (bool[])select.Clone();

        int destination = 0;
        for (int k = 0; k < n;)
        {
            int size = k + 1 < n && tt[k + 1, k] != 0 ? 2 : 1;
            bool selected = wanted[k] || (size == 2 && wanted[k + 1]);

            if (selected)
            {
                int here = k;
                while (here > destination)
                {
                    int previousStart = here - (here >= 2 && tt[here - 1, here - 2] != 0 ? 2 : 1);
                    Exchange(tt, uu, wanted, n, previousStart, here - previousStart, size);
                    here = previousStart;
                }

                destination = here + size;
            }

            k += size;
        }

        return new Schur(tt, uu);
    }

    private static double[] Flatten(double[,] source, int n)
    {
        var flat = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                flat[(c * n) + r] = source[r, c];
            }
        }

        return flat;
    }

    private static double[,] Rebuild(double[] flat, int n)
    {
        var rect = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                rect[r, c] = flat[(c * n) + r];
            }
        }

        return rect;
    }

    // --- The Francis iteration ------------------------------------------------------------------------

    /// <summary>
    /// Drives the Hessenberg matrix to quasi-triangular form, accumulating every rotation into
    /// <paramref name="u"/>.
    /// </summary>
    private static void Iterate(double[,] h, double[,] u, int n)
    {
        double norm = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = Math.Max(i - 1, 0); j < n; j++)
            {
                norm += Math.Abs(h[i, j]);
            }
        }

        int high = n - 1;
        int steps = 0;

        while (high >= 0)
        {
            // Walk up the subdiagonal looking for an entry small enough to call zero; that splits
            // the problem and is the only way the iteration ever finishes.
            int low = high;
            while (low > 0)
            {
                double scale = Math.Abs(h[low - 1, low - 1]) + Math.Abs(h[low, low]);
                if (scale == 0)
                {
                    scale = norm;
                }

                if (Math.Abs(h[low, low - 1]) <= Epsilon * scale)
                {
                    h[low, low - 1] = 0;
                    break;
                }

                low--;
            }

            if (low == high)
            {
                high--;
                steps = 0;
                continue;
            }

            if (low == high - 1)
            {
                high -= 2;
                steps = 0;
                continue;
            }

            if (++steps > MaxStepsPerEigenvalue)
            {
                throw new InvalidOperationException(
                    "The Schur iteration did not converge; the matrix may contain infinities or NaNs.");
            }

            double x = h[high, high];
            double y = h[high - 1, high - 1];
            double w = h[high, high - 1] * h[high - 1, high];

            if (steps % 12 == 0)
            {
                // An exceptional shift, to break the rare cycle where the trailing block's own
                // eigenvalues keep reproducing themselves. The constants are the classical ones.
                double magnitude = Math.Abs(h[high, high - 1]) + Math.Abs(h[high - 1, high - 2]);
                x = y = 0.75 * magnitude;
                w = -0.4375 * magnitude * magnitude;
            }

            Step(h, u, n, low, high, x, y, w);
        }
    }

    /// <summary>
    /// One implicit double-shift step: build the bulge from the shifts, then chase it down the
    /// subdiagonal with three-element reflectors.
    /// </summary>
    private static void Step(double[,] h, double[,] u, int n, int low, int high, double x, double y, double w)
    {
        // Find where the bulge can start. Beginning as far down as possible is what keeps the step
        // to O(n) work per column rather than restarting from the top of the block.
        double p = 0, q = 0, r = 0;
        int start = low;

        for (int m = high - 2; m >= low; m--)
        {
            double z = h[m, m];
            double rr = x - z;
            double ss = y - z;
            p = (rr * ss - w) / h[m + 1, m] + h[m, m + 1];
            q = h[m + 1, m + 1] - z - rr - ss;
            r = h[m + 2, m + 1];

            double scale = Math.Abs(p) + Math.Abs(q) + Math.Abs(r);
            p /= scale;
            q /= scale;
            r /= scale;

            if (m == low)
            {
                start = m;
                break;
            }

            double left = Math.Abs(h[m, m - 1]) * (Math.Abs(q) + Math.Abs(r));
            double right = Math.Abs(p) * (Math.Abs(h[m - 1, m - 1]) + Math.Abs(z) + Math.Abs(h[m + 1, m + 1]));
            if (left <= Epsilon * right)
            {
                start = m;
                break;
            }

            start = m;
        }

        // The previous chase left the entries the bulge passed through holding values that are
        // mathematically zero but were never written back, because the reflector that annihilated
        // them was only applied from the column it started at. Clearing them here is what keeps the
        // matrix Hessenberg between steps — leaving them turns the next step's reflectors into
        // nonsense and the iteration never converges.
        for (int i = start + 2; i <= high; i++)
        {
            h[i, i - 2] = 0;
            if (i > start + 2)
            {
                h[i, i - 3] = 0;
            }
        }

        for (int k = start; k <= high - 1; k++)
        {
            bool pair = k == high - 1;
            double scale = 0;

            if (k != start)
            {
                p = h[k, k - 1];
                q = h[k + 1, k - 1];
                r = pair ? 0 : h[k + 2, k - 1];
                scale = Math.Abs(p) + Math.Abs(q) + Math.Abs(r);
                if (scale == 0)
                {
                    continue;
                }

                p /= scale;
                q /= scale;
                r /= scale;
            }

            double s = Math.Sqrt(p * p + q * q + r * r);
            if (p < 0)
            {
                s = -s;
            }

            if (s == 0)
            {
                continue;
            }

            if (k == start)
            {
                if (start != low)
                {
                    h[k, k - 1] = -h[k, k - 1];
                }
            }
            else
            {
                // The reflector puts the whole column onto its first entry, whose magnitude is the
                // scale that was divided out.
                h[k, k - 1] = -s * scale;
            }

            p += s;
            double px = p / s;
            double py = q / s;
            double pz = r / s;
            q /= p;
            r /= p;

            // Rows: the reflector from the left, over the whole trailing row so that U stays the
            // similarity for the entire matrix rather than just the active block.
            for (int j = k; j < n; j++)
            {
                double sum = h[k, j] + q * h[k + 1, j] + (pair ? 0 : r * h[k + 2, j]);
                h[k, j] -= sum * px;
                h[k + 1, j] -= sum * py;
                if (!pair)
                {
                    h[k + 2, j] -= sum * pz;
                }
            }

            // Columns: the same reflector from the right, over every row it can reach. Below the
            // bulge the entries are still zero, so there is nothing there to update.
            int last = Math.Min(pair ? high : k + 3, high);
            for (int i = 0; i <= last; i++)
            {
                double sum = px * h[i, k] + py * h[i, k + 1] + (pair ? 0 : pz * h[i, k + 2]);
                h[i, k] -= sum;
                h[i, k + 1] -= sum * q;
                if (!pair)
                {
                    h[i, k + 2] -= sum * r;
                }
            }

            for (int i = 0; i < n; i++)
            {
                double sum = px * u[i, k] + py * u[i, k + 1] + (pair ? 0 : pz * u[i, k + 2]);
                u[i, k] -= sum;
                u[i, k + 1] -= sum * q;
                if (!pair)
                {
                    u[i, k + 2] -= sum * r;
                }
            }
        }
    }

    /// <summary>
    /// Puts the 2×2 block at <paramref name="i"/> into standard form — LAPACK's <c>dlanv2</c>:
    /// split into two 1×1 blocks if its eigenvalues turn out to be real, and otherwise rotated
    /// until its diagonal entries are equal, which is what lets the pair be read off as a ± ib.
    /// </summary>
    /// <remarks>
    /// The block's own four entries are computed in closed form and written; only the rest of its
    /// two rows and two columns is rotated. Rotating the block too and reading its diagonal
    /// afterwards puts a rounding error into each eigenvalue that the closed form does not have:
    /// the companion matrix of <c>s² + 3s + 2</c> came out as −1.9999999999999996 and
    /// −0.99999999999999978 that way, where LAPACK answers −2 and −1 exactly, and <c>residue</c>
    /// then reported 2.2e-16 for a residue that is 0. The rescaling <c>dlanv2</c> does for entries
    /// near overflow or underflow is left out.
    /// </remarks>
    private static void Standardize(double[,] h, double[,] u, int n, int i)
    {
        double a = h[i, i];
        double b = h[i, i + 1];
        double c = h[i + 1, i];
        double d = h[i + 1, i + 1];
        double cos = 1;
        double sin = 0;

        if (c == 0)
        {
            // Already split.
        }
        else if (b == 0)
        {
            // Swap rows and columns: a quarter turn puts the zero below the diagonal.
            cos = 0;
            sin = 1;
            (a, d) = (d, a);
            b = -c;
            c = 0;
        }
        else if (a - d == 0 && SignOf(b) != SignOf(c))
        {
            // Already standard: an equal diagonal over off-diagonals of opposite sign.
        }
        else
        {
            double temp = a - d;
            double p = 0.5 * temp;
            double bcMax = Math.Max(Math.Abs(b), Math.Abs(c));
            double bcMis = Math.Min(Math.Abs(b), Math.Abs(c)) * SignOf(b) * SignOf(c);
            double scale = Math.Max(Math.Abs(p), bcMax);
            double z = ((p / scale) * p) + ((bcMax / scale) * bcMis);

            if (z >= 4 * Epsilon)
            {
                // Real eigenvalues. The diagonal becomes the pair itself, formed from the
                // discriminant and the product rather than read off a rotated block.
                z = p + Math.CopySign(Math.Sqrt(scale) * Math.Sqrt(z), p);
                a = d + z;
                d -= (bcMax / z) * bcMis;
                double tau = Math.Sqrt((c * c) + (z * z));
                cos = z / tau;
                sin = c / tau;
                b -= c;
                c = 0;
            }
            else
            {
                // A conjugate pair, or a real pair too close to tell apart: make the diagonal
                // entries equal.
                double sigma = b + c;
                double tau = Math.Sqrt((sigma * sigma) + (temp * temp));
                cos = Math.Sqrt(0.5 * (1 + (Math.Abs(sigma) / tau)));
                sin = -(p / (tau * cos)) * SignOf(sigma);

                // [a b; c d]·[cos -sin; sin cos], and then [cos sin; -sin cos] on the left of that.
                double aa = (a * cos) + (b * sin);
                double bb = (-a * sin) + (b * cos);
                double cc = (c * cos) + (d * sin);
                double dd = (-c * sin) + (d * cos);
                a = (aa * cos) + (cc * sin);
                b = (bb * cos) + (dd * sin);
                c = (-aa * sin) + (cc * cos);
                d = (-bb * sin) + (dd * cos);
                temp = 0.5 * (a + d);
                a = temp;
                d = temp;

                if (c != 0)
                {
                    if (b != 0)
                    {
                        if (SignOf(b) == SignOf(c))
                        {
                            // Real eigenvalues after all: one more rotation makes it triangular.
                            double sab = Math.Sqrt(Math.Abs(b));
                            double sac = Math.Sqrt(Math.Abs(c));
                            p = Math.CopySign(sab * sac, c);
                            tau = 1 / Math.Sqrt(Math.Abs(b + c));
                            a = temp + p;
                            d = temp - p;
                            b -= c;
                            c = 0;
                            double cos1 = sab * tau;
                            double sin1 = sac * tau;
                            temp = (cos * cos1) - (sin * sin1);
                            sin = (cos * sin1) + (sin * cos1);
                            cos = temp;
                        }
                    }
                    else
                    {
                        b = -c;
                        c = 0;
                        temp = cos;
                        cos = -sin;
                        sin = temp;
                    }
                }
            }
        }

        // Gᵀ·H·G with G = [cos -sin; sin cos] over the rest of the two rows, the rest of the two
        // columns, and the accumulated U; the block itself is written below.
        for (int j = i + 2; j < n; j++)
        {
            double top = h[i, j];
            double bottom = h[i + 1, j];
            h[i, j] = (cos * top) + (sin * bottom);
            h[i + 1, j] = (cos * bottom) - (sin * top);
        }

        for (int j = 0; j < i; j++)
        {
            double left = h[j, i];
            double right = h[j, i + 1];
            h[j, i] = (cos * left) + (sin * right);
            h[j, i + 1] = (cos * right) - (sin * left);
        }

        for (int j = 0; j < n; j++)
        {
            double left = u[j, i];
            double right = u[j, i + 1];
            u[j, i] = (cos * left) + (sin * right);
            u[j, i + 1] = (cos * right) - (sin * left);
        }

        h[i, i] = a;
        h[i, i + 1] = b;
        h[i + 1, i] = c;
        h[i + 1, i + 1] = d;
    }

    /// <summary>Fortran's <c>SIGN(1, x)</c>: −1 for a negative x and 1 otherwise, zero included.</summary>
    private static double SignOf(double x) => x < 0 ? -1 : 1;

    // --- Exchanging adjacent diagonal blocks -----------------------------------------------------------

    /// <summary>
    /// Swaps the adjacent diagonal blocks of sizes <paramref name="p"/> and <paramref name="q"/>
    /// starting at <paramref name="s"/>, carrying U and the selection flags with them.
    /// </summary>
    /// <remarks>
    /// The columns of [X; I] span the invariant subspace belonging to the second block, where X
    /// solves T₁₁X − XT₂₂ = −T₁₂. Orthonormalizing that basis gives the similarity that brings the
    /// second block to the front, and the same three lines work for every combination of block
    /// sizes — which is why this is written as a Sylvester solve rather than four special cases.
    /// </remarks>
    private static void Exchange(double[,] t, double[,] u, bool[] wanted, int n, int s, int p, int q)
    {
        int m = p + q;
        var sylvester = new double[p * q, p * q];
        var rhs = new double[p * q, 1];

        for (int i = 0; i < p; i++)
        {
            for (int j = 0; j < q; j++)
            {
                int row = (i * q) + j;
                for (int k = 0; k < p; k++)
                {
                    sylvester[row, (k * q) + j] += t[s + i, s + k];
                }

                for (int k = 0; k < q; k++)
                {
                    sylvester[row, (i * q) + k] -= t[s + p + k, s + p + j];
                }

                rhs[row, 0] = -t[s + i, s + p + j];
            }
        }

        double[,] solution;
        try
        {
            solution = Linear.Solve(sylvester, rhs);
        }
        catch (InvalidOperationException)
        {
            // A singular Sylvester system means the two blocks share an eigenvalue, and swapping
            // equal eigenvalues changes nothing in T. Only the flags have to notice the exchange.
            RotateFlags(wanted, s, p, q);
            return;
        }

        // The basis [X; I], then a Householder QR of it to get an orthonormal one.
        var basis = new double[m, q];
        for (int i = 0; i < p; i++)
        {
            for (int j = 0; j < q; j++)
            {
                basis[i, j] = solution[(i * q) + j, 0];
            }
        }

        for (int j = 0; j < q; j++)
        {
            basis[p + j, j] = 1;
        }

        double[,] rotation = OrthonormalBasis(basis, m, q);

        // Zᵀ·T·Z over the full rows and columns, and U·Z alongside.
        var rows = new double[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < m; k++)
                {
                    sum += rotation[k, i] * t[s + k, j];
                }

                rows[i, j] = sum;
            }
        }

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                t[s + i, j] = rows[i, j];
            }
        }

        var columns = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double sum = 0;
                for (int k = 0; k < m; k++)
                {
                    sum += t[i, s + k] * rotation[k, j];
                }

                columns[i, j] = sum;
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                t[i, s + j] = columns[i, j];
            }
        }

        var accumulated = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double sum = 0;
                for (int k = 0; k < m; k++)
                {
                    sum += u[i, s + k] * rotation[k, j];
                }

                accumulated[i, j] = sum;
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                u[i, s + j] = accumulated[i, j];
            }
        }

        // The coupling below the new leading block is zero by construction; saying so keeps the
        // block structure readable rather than leaving rounding dust that looks like a 2×2.
        for (int i = q; i < m; i++)
        {
            for (int j = 0; j < q; j++)
            {
                t[s + i, s + j] = 0;
            }
        }

        if (q == 2)
        {
            Standardize(t, u, n, s);
        }

        if (p == 2)
        {
            Standardize(t, u, n, s + q);
        }

        RotateFlags(wanted, s, p, q);
    }

    /// <summary>The selection flags follow their blocks: the trailing q entries move to the front.</summary>
    private static void RotateFlags(bool[] wanted, int s, int p, int q)
    {
        bool[] window = new bool[p + q];
        for (int i = 0; i < q; i++)
        {
            window[i] = wanted[s + p + i];
        }

        for (int i = 0; i < p; i++)
        {
            window[q + i] = wanted[s + i];
        }

        for (int i = 0; i < p + q; i++)
        {
            wanted[s + i] = window[i];
        }
    }

    /// <summary>
    /// The full orthogonal factor of a tall thin matrix's QR, by Householder. The matrices here are
    /// at most 4×2, so the plain accumulation is both clearer and faster than reusing the general
    /// decomposition.
    /// </summary>
    private static double[,] OrthonormalBasis(double[,] a, int rows, int columns)
    {
        var q = new double[rows, rows];
        for (int i = 0; i < rows; i++)
        {
            q[i, i] = 1;
        }

        for (int k = 0; k < columns; k++)
        {
            double norm = 0;
            for (int i = k; i < rows; i++)
            {
                norm += a[i, k] * a[i, k];
            }

            norm = Math.Sqrt(norm);
            if (norm == 0)
            {
                continue;
            }

            if (a[k, k] > 0)
            {
                norm = -norm;
            }

            var v = new double[rows];
            for (int i = k; i < rows; i++)
            {
                v[i] = a[i, k];
            }

            v[k] -= norm;

            double vv = 0;
            for (int i = k; i < rows; i++)
            {
                vv += v[i] * v[i];
            }

            if (vv == 0)
            {
                continue;
            }

            for (int j = k; j < columns; j++)
            {
                double dot = 0;
                for (int i = k; i < rows; i++)
                {
                    dot += v[i] * a[i, j];
                }

                dot = 2 * dot / vv;
                for (int i = k; i < rows; i++)
                {
                    a[i, j] -= dot * v[i];
                }
            }

            for (int j = 0; j < rows; j++)
            {
                double dot = 0;
                for (int i = k; i < rows; i++)
                {
                    dot += v[i] * q[j, i];
                }

                dot = 2 * dot / vv;
                for (int i = k; i < rows; i++)
                {
                    q[j, i] -= dot * v[i];
                }
            }
        }

        return q;
    }
}
