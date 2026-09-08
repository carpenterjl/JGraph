using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// A general function of a matrix, by the Schur-Parlett method of Davies and Higham: triangularize,
/// gather eigenvalues that are close to one another into blocks, evaluate the function on each
/// block by its own Taylor series, and fill in everything above the diagonal by a recurrence.
/// </summary>
/// <remarks>
/// <para>
/// The recurrence is what makes the blocking necessary. Parlett's formula recovers an off-diagonal
/// entry of f(T) by dividing by the difference of two eigenvalues, which is exact when they are far
/// apart and worthless when they are close. So eigenvalues within a tolerance of each other are
/// gathered into one block, the division is never performed inside a block, and the block's own
/// value is found instead from a Taylor series about its mean — where being clustered is a virtue,
/// because it makes the nilpotent part small.
/// </para>
/// <para>
/// The price is that the caller must supply not just the function but its derivatives: the series
/// needs f⁽ᵏ⁾ at the block's centre, and the stopping test needs the largest derivative over the
/// block. A function that cannot say what its own derivatives are cannot be applied to a matrix
/// with repeated eigenvalues, which is why MATLAB's <c>funm</c> takes a two-argument handle and not
/// an ordinary one.
/// </para>
/// </remarks>
public static class MatrixFunction
{
    /// <summary>The spacing of one at double precision.</summary>
    private const double Spacing = 2.220446049250313e-16;

    /// <summary>A function together with its derivatives: <c>f(x, k)</c> is f⁽ᵏ⁾ at each x.</summary>
    public delegate Complex[] Derivative(Complex[] x, int order);

    /// <summary>Which of the three the caller asked for, since two of them have closed forms.</summary>
    public enum Kind
    {
        /// <summary>An arbitrary function, evaluated by its Taylor series.</summary>
        General,

        /// <summary>The exponential, which has a closed form on a two-by-two block.</summary>
        Exponential,

        /// <summary>The logarithm, which has a closed form and its own scaling algorithm.</summary>
        Logarithm,
    }

    /// <summary>What the caller may set, and what MATLAB's own options struct carries.</summary>
    /// <param name="BlockTolerance">How close two eigenvalues must be to share a block.</param>
    /// <param name="SeriesTolerance">When a block's Taylor series has converged.</param>
    /// <param name="MaxTerms">How many terms that series may take.</param>
    /// <param name="MaxSquareRoots">How many square roots the logarithm's scaling may take.</param>
    /// <param name="Order">A blocking supplied outright, overriding the one that would be chosen.</param>
    public sealed record Options(
        double BlockTolerance = 0.1,
        double SeriesTolerance = Spacing,
        int MaxTerms = 250,
        int MaxSquareRoots = 100,
        int[]? Order = null);

    /// <summary>The answer, and everything MATLAB's third output reports about how it was reached.</summary>
    /// <param name="F">The function of the matrix.</param>
    /// <param name="Terms">How many series terms, or square roots, each block took.</param>
    /// <param name="Blocks">Which diagonal positions each block covers, after reordering.</param>
    /// <param name="Order">The block each original diagonal position was assigned to.</param>
    /// <param name="T">The reordered triangular form the evaluation ran on.</param>
    /// <param name="Stalled">Whether some block's series ran out of terms.</param>
    /// <param name="TooManyRoots">Whether the logarithm's scaling ran out of square roots.</param>
    public sealed record Result(
        Complex[,] F, int[] Terms, int[][] Blocks, int[] Order, Complex[,] T, bool Stalled, bool TooManyRoots);

    /// <summary>Evaluates the function over a Schur factorization that is already in hand.</summary>
    public static Result Evaluate(Complex[,] u, Complex[,] t, Derivative fun, Kind kind, Options options)
    {
        int n = t.GetLength(0);
        var diagonal = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            diagonal[i] = t[i, i];
        }

        if (IsDiagonal(t, n))
        {
            Complex[] values = fun(diagonal, 0);
            var d = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                d[i, i] = values[i];
            }

            var identityBlocks = new int[n][];
            var ones = new int[n];
            for (int i = 0; i < n; i++)
            {
                identityBlocks[i] = [i];
                ones[i] = 1;
            }

            return new Result(Conjugated(u, d), ones, identityBlocks, Ascending(n), t, false, false);
        }

        int[] clusters = options.Order ?? Blocking(diagonal, options.BlockTolerance);
        (int[] labels, int[][] blocks) = Swapping(clusters);

        var uu = (Complex[,])u.Clone();
        var tt = (Complex[,])t.Clone();
        Reorder(uu, tt, labels);

        int count = blocks.Length;
        var f = new Complex[n, n];
        var terms = new int[count];
        bool stalled = false;
        bool tooManyRoots = false;

        for (int column = 0; column < count; column++)
        {
            int[] j = blocks[column];
            Complex[,] block = Sub(tt, j[0], j.Length, j[0], j.Length);
            Complex[,] value;
            switch (kind)
            {
                case Kind.Logarithm:
                    (value, terms[column]) = TriangularLogarithm(block, options.MaxSquareRoots);
                    tooManyRoots |= terms[column] == options.MaxSquareRoots;
                    break;
                case Kind.Exponential when j.Length <= 2:
                    // No series was taken, so no count is reported — which is what MATLAB's own
                    // table shows for an exponential block: the zero it was initialised to.
                    value = TriangularExponential(block);
                    terms[column] = 0;
                    break;
                default:
                    (value, terms[column]) = Atom(block, fun, options.SeriesTolerance, options.MaxTerms);
                    stalled |= terms[column] < 0;
                    break;
            }

            Place(f, value, j[0], j[0]);

            for (int row = column - 1; row >= 0; row--)
            {
                int[] i = blocks[row];
                var middle = new List<int>();
                for (int between = row + 1; between < column; between++)
                {
                    middle.AddRange(blocks[between]);
                }

                Complex[,] rhs = ParlettRight(f, tt, i, j, middle);
                Complex[,] answer;
                if (i.Length == 1 && j.Length == 1)
                {
                    answer = new Complex[1, 1]
                    {
                        { rhs[0, 0] / (tt[i[0], i[0]] - tt[j[0], j[0]]) },
                    };
                }
                else
                {
                    Complex[,] left = Sub(tt, i[0], i.Length, i[0], i.Length);
                    Complex[,] right = Sub(tt, j[0], j.Length, j[0], j.Length);
                    Negate(right);
                    answer = SylvesterEquation.SolveTriangular(left, right, rhs);
                }

                Place(f, answer, i[0], j[0]);
            }
        }

        return new Result(Conjugated(uu, f), terms, blocks, labels, tt, stalled, tooManyRoots);
    }

    /// <summary>
    /// The right-hand side of Parlett's recurrence for one off-diagonal block: what the blocks
    /// already computed contribute, once the diagonal blocks either side are taken across.
    /// </summary>
    private static Complex[,] ParlettRight(
        Complex[,] f, Complex[,] t, int[] i, int[] j, List<int> middle)
    {
        int rows = i.Length;
        int cols = j.Length;
        var rhs = new Complex[rows, cols];
        for (int a = 0; a < rows; a++)
        {
            for (int b = 0; b < cols; b++)
            {
                Complex sum = Complex.Zero;
                if (rows == 1 && cols == 1)
                {
                    sum = t[i[0], j[0]] * (f[i[0], i[0]] - f[j[0], j[0]]);
                }
                else
                {
                    for (int k = 0; k < rows; k++)
                    {
                        sum += f[i[a], i[k]] * t[i[k], j[b]];
                    }

                    for (int k = 0; k < cols; k++)
                    {
                        sum -= t[i[a], j[k]] * f[j[k], j[b]];
                    }
                }

                foreach (int k in middle)
                {
                    sum += (f[i[a], k] * t[k, j[b]]) - (t[i[a], k] * f[k, j[b]]);
                }

                rhs[a, b] = sum;
            }
        }

        return rhs;
    }

    /// <summary>
    /// One block's value, by the Taylor series of the function about the block's mean eigenvalue.
    /// </summary>
    /// <remarks>
    /// The stopping test is not simply that the terms have stopped changing the answer. It is a
    /// bound on the whole remaining tail, built from the largest derivative anywhere on the block's
    /// diagonal and from how far from nilpotent the block is; the cheap test can pass while the tail
    /// is still large, and does, for a function whose derivatives grow before they shrink.
    /// </remarks>
    private static (Complex[,] Value, int Terms) Atom(
        Complex[,] t, Derivative fun, double tolerance, int maxTerms)
    {
        int n = t.GetLength(0);
        if (n == 1)
        {
            return (new Complex[1, 1] { { fun([t[0, 0]], 0)[0] } }, 1);
        }

        Complex mean = Complex.Zero;
        var diagonal = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            diagonal[i] = t[i, i];
            mean += t[i, i];
        }

        mean /= n;
        Complex first = fun([mean], 0)[0];
        var f = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            f[i, i] = first;
        }

        var nilpotent = (Complex[,])t.Clone();
        for (int i = 0; i < n; i++)
        {
            nilpotent[i, i] -= mean;
        }

        double mu = NilpotencyBound(t, n);
        var power = (Complex[,])nilpotent.Clone();
        var derivatives = new double[maxTerms + n];
        int measured = 1;

        for (int k = 1; k <= maxTerms; k++)
        {
            Complex coefficient = fun([mean], k)[0];
            if (double.IsInfinity(coefficient.Real) || double.IsInfinity(coefficient.Imaginary))
            {
                throw new ArgumentException("Infinite derivative.");
            }

            Complex[,] previous = (Complex[,])f.Clone();
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    f[r, c] += power[r, c] * coefficient;
                }
            }

            double change = InfinityNorm(Difference(f, previous)) / (tolerance + InfinityNorm(previous));
            power = Scaled(Multiply(power, nilpotent), k + 1);

            if (change > tolerance)
            {
                continue;
            }

            for (int j = measured; j <= k + n - 1; j++)
            {
                derivatives[j] = LargestMagnitude(fun(diagonal, j));
            }

            measured = k + n;
            double omega = 0.0;
            double factorial = 1.0;
            for (int j = 0; j < n; j++)
            {
                if (j > 0)
                {
                    factorial *= j;
                }

                omega = Math.Max(omega, derivatives[k + j] / factorial);
            }

            if (InfinityNorm(power) * mu * omega <= tolerance * InfinityNorm(f))
            {
                return (f, k + 1);
            }
        }

        return (f, -1);
    }

    /// <summary>
    /// How far the block is from being nilpotent, as the infinity norm of the solution of
    /// <c>(I − |strictly upper T|)·x = 1</c> — the bound Davies and Higham's truncation test uses.
    /// </summary>
    private static double NilpotencyBound(Complex[,] t, int n)
    {
        var m = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            m[r, r] = Complex.One;
            for (int c = r + 1; c < n; c++)
            {
                m[r, c] = new Complex(-t[r, c].Magnitude, 0.0);
            }
        }

        var ones = new Complex[n, 1];
        for (int i = 0; i < n; i++)
        {
            ones[i, 0] = Complex.One;
        }

        HouseholderQr.SolveUpper(m, n, ones);
        double worst = 0.0;
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, ones[i, 0].Magnitude);
        }

        return worst;
    }

    /// <summary>The exponential of a block of one or two, where it is a formula rather than a series.</summary>
    private static Complex[,] TriangularExponential(Complex[,] t)
    {
        if (t.GetLength(0) == 1)
        {
            return new Complex[1, 1] { { Complex.Exp(t[0, 0]) } };
        }

        Complex a = t[0, 0];
        Complex b = t[1, 1];
        Complex half = (b - a) / 2.0;

        // The difference of two exponentials divided by the difference of their arguments, written
        // so that it stays accurate when the two arguments are nearly equal — which for a block
        // gathered by closeness is the ordinary case, not the exceptional one.
        Complex ratio = half == Complex.Zero ? Complex.One : ComplexSinh(half) / half;
        return new Complex[2, 2]
        {
            { Complex.Exp(a), t[0, 1] * Complex.Exp((a + b) / 2.0) * ratio },
            { Complex.Zero, Complex.Exp(b) },
        };
    }

    /// <summary>The logarithm of a triangular block, and how many square roots it took.</summary>
    private static (Complex[,] Value, int Roots) TriangularLogarithm(Complex[,] t, int maxRoots)
    {
        int n = t.GetLength(0);
        if (n == 1)
        {
            return (new Complex[1, 1] { { Complex.Log(t[0, 0]) } }, 0);
        }

        if (n == 2)
        {
            Complex a = t[0, 0];
            Complex b = t[1, 1];
            Complex la = Complex.Log(a);
            Complex lb = Complex.Log(b);
            var x = new Complex[2, 2];
            x[0, 0] = la;
            x[1, 1] = lb;
            if (a == b)
            {
                x[0, 1] = t[0, 1] / a;
            }
            else if (a.Magnitude < 0.5 * b.Magnitude || b.Magnitude < 0.5 * a.Magnitude)
            {
                x[0, 1] = t[0, 1] * (lb - la) / (b - a);
            }
            else
            {
                // Close but unequal: the difference of the two logarithms loses its digits, and the
                // inverse hyperbolic tangent of their relative gap does not.
                Complex gap = (b - a) / (b + a);
                Complex turn = new Complex(0.0, 2 * Math.PI) * Unwinding(lb - la);
                x[0, 1] = t[0, 1] * ((2 * Atanh(gap)) + turn) / (b - a);
            }

            return (x, 0);
        }

        double[] limits =
        [
            1.6206284795015669e-002,
            5.3873532631381268e-002,
            1.1352802267628663e-001,
            1.8662860613541296e-001,
            2.6429608311114350e-001,
        ];

        var work = (Complex[,])t.Clone();
        int roots = 0;
        int degree;
        int passes = 0;
        while (true)
        {
            double distance = OneNormFromIdentity(work, n);
            if (distance <= limits[^1])
            {
                passes++;
                int first = Position(limits, distance) + 3;
                int second = Position(limits, distance / 2) + 3;
                if (first - second < 2 || passes == 2)
                {
                    degree = first;
                    break;
                }
            }

            if (roots == maxRoots)
            {
                degree = 16;
                break;
            }

            work = TriangularSquareRoot(work, n);
            roots++;
        }

        Complex[,] value = PadeLogarithm(work, n, degree);
        double scale = Math.Pow(2.0, roots);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                value[r, c] *= scale;
            }
        }

        return (value, roots);
    }

    /// <summary>The square root of an upper triangular matrix, by the recurrence on its entries.</summary>
    private static Complex[,] TriangularSquareRoot(Complex[,] t, int n)
    {
        var r = new Complex[n, n];
        for (int j = 0; j < n; j++)
        {
            r[j, j] = Complex.Sqrt(t[j, j]);
            for (int i = j - 1; i >= 0; i--)
            {
                Complex sum = t[i, j];
                for (int k = i + 1; k < j; k++)
                {
                    sum -= r[i, k] * r[k, j];
                }

                r[i, j] = sum / (r[i, i] + r[j, j]);
            }
        }

        return r;
    }

    /// <summary>
    /// The Gauss-Legendre quadrature form of the Padé approximant to <c>log(I + A)</c> — the integral
    /// of <c>A·(I + sA)⁻¹</c> over the unit interval, evaluated exactly for a polynomial of the
    /// approximant's degree.
    /// </summary>
    private static Complex[,] PadeLogarithm(Complex[,] work, int n, int degree)
    {
        var a = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                a[r, c] = work[r, c] - (r == c ? Complex.One : Complex.Zero);
            }
        }

        (double[] nodes, double[] weights) = GaussLegendre(degree);
        var s = new Complex[n, n];
        for (int j = 0; j < degree; j++)
        {
            double node = (nodes[j] + 1) / 2;
            double weight = weights[j] / 2;
            var denominator = new Complex[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    denominator[r, c] = (r == c ? Complex.One : Complex.Zero) + (node * a[r, c]);
                }
            }

            // A·(I + sA)⁻¹ is a division from the right, and the divisor is triangular because the
            // matrix this whole routine works on is, so it is a forward substitution over columns
            // rather than a factorization.
            var solved = new Complex[n, n];
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    Complex sum = a[r, c];
                    for (int k = 0; k < c; k++)
                    {
                        sum -= solved[r, k] * denominator[k, c];
                    }

                    solved[r, c] = sum / denominator[c, c];
                }
            }

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    s[r, c] += weight * solved[r, c];
                }
            }
        }

        return s;
    }

    /// <summary>The Gauss-Legendre nodes and weights of a given degree, by Golub and Welsch.</summary>
    private static (double[] Nodes, double[] Weights) GaussLegendre(int n)
    {
        var jacobi = new double[n, n];
        for (int i = 1; i < n; i++)
        {
            double off = i / Math.Sqrt(((2.0 * i) * (2.0 * i)) - 1);
            jacobi[i, i - 1] = off;
            jacobi[i - 1, i] = off;
        }

        Schur factored = Schur.Factor(jacobi);
        var nodes = new double[n];
        var weights = new double[n];
        for (int i = 0; i < n; i++)
        {
            nodes[i] = factored.T[i, i];
            weights[i] = 2 * factored.U[0, i] * factored.U[0, i];
        }

        return (nodes, weights);
    }

    /// <summary>
    /// Which diagonal positions belong together: everything within <paramref name="delta"/> of
    /// something already in a set joins that set, and two sets that meet are merged.
    /// </summary>
    public static int[] Blocking(Complex[] a, double delta)
    {
        int n = a.Length;
        var m = new int[n];
        int most = 0;
        for (int i = 0; i < n; i++)
        {
            if (m[i] == 0)
            {
                m[i] = ++most;
            }

            for (int j = i + 1; j < n; j++)
            {
                if (m[i] == m[j] || (a[i] - a[j]).Magnitude > delta)
                {
                    continue;
                }

                if (m[j] == 0)
                {
                    m[j] = m[i];
                    continue;
                }

                int high = Math.Max(m[i], m[j]);
                int low = Math.Min(m[i], m[j]);
                for (int k = 0; k < n; k++)
                {
                    if (m[k] == high)
                    {
                        m[k] = low;
                    }
                    else if (m[k] > high)
                    {
                        m[k]--;
                    }
                }

                most--;
            }
        }

        return m;
    }

    /// <summary>
    /// Which order the blocks should be put in, and which positions each ends up covering: a block
    /// whose members already sit near the top of the diagonal is moved the least by being put first.
    /// </summary>
    public static (int[] Labels, int[][] Blocks) Swapping(int[] m)
    {
        int most = 0;
        foreach (int value in m)
        {
            most = Math.Max(most, value);
        }

        var counts = new int[most];
        var centres = new double[most];
        for (int i = 0; i < most; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < m.Length; j++)
            {
                if (m[j] == i + 1)
                {
                    counts[i]++;
                    sum += j + 1;
                }
            }

            centres[i] = sum / counts[i];
        }

        var order = new int[most];
        for (int i = 0; i < most; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
            centres[a] != centres[b] ? centres[a].CompareTo(centres[b]) : a.CompareTo(b));

        var labels = new int[m.Length];
        var blocks = new int[most][];
        int at = 0;
        for (int i = 0; i < most; i++)
        {
            for (int j = 0; j < m.Length; j++)
            {
                if (m[j] == order[i] + 1)
                {
                    labels[j] = i + 1;
                }
            }

            blocks[i] = new int[counts[order[i]]];
            for (int k = 0; k < blocks[i].Length; k++)
            {
                blocks[i][k] = at++;
            }
        }

        return (labels, blocks);
    }

    /// <summary>
    /// Moves the diagonal of an upper triangular factorization so that the positions labelled 1 come
    /// first, then those labelled 2, and so on, by adjacent swaps that preserve the factorization.
    /// </summary>
    public static void Reorder(Complex[,] u, Complex[,] t, int[] labels)
    {
        int n = t.GetLength(0);
        var current = (int[])labels.Clone();
        var wanted = (int[])labels.Clone();
        Array.Sort(wanted);

        for (int p = 0; p < n; p++)
        {
            if (current[p] == wanted[p])
            {
                continue;
            }

            int q = p;
            while (q < n && current[q] != wanted[p])
            {
                q++;
            }

            for (int k = q; k > p; k--)
            {
                Swap(u, t, k - 1, n);
                (current[k - 1], current[k]) = (current[k], current[k - 1]);
            }
        }
    }

    /// <summary>Exchanges the diagonal entries at <paramref name="j"/> and <paramref name="j"/>+1.</summary>
    private static void Swap(Complex[,] u, Complex[,] t, int j, int n)
    {
        Complex t11 = t[j, j];
        Complex t22 = t[j + 1, j + 1];
        (double cosine, Complex sine) = Rotation(t[j, j + 1], t22 - t11);

        for (int c = j + 2; c < n; c++)
        {
            Complex a = t[j, c];
            Complex b = t[j + 1, c];
            t[j, c] = (cosine * a) + (sine * b);
            t[j + 1, c] = (cosine * b) - (Complex.Conjugate(sine) * a);
        }

        Complex turn = Complex.Conjugate(sine);
        for (int r = 0; r < j; r++)
        {
            Complex a = t[r, j];
            Complex b = t[r, j + 1];
            t[r, j] = (cosine * a) + (turn * b);
            t[r, j + 1] = (cosine * b) - (Complex.Conjugate(turn) * a);
        }

        for (int r = 0; r < u.GetLength(0); r++)
        {
            Complex a = u[r, j];
            Complex b = u[r, j + 1];
            u[r, j] = (cosine * a) + (turn * b);
            u[r, j + 1] = (cosine * b) - (Complex.Conjugate(turn) * a);
        }

        t[j, j] = t22;
        t[j + 1, j + 1] = t11;
    }

    /// <summary>The rotation with a real cosine that sends (f, g) to (r, 0).</summary>
    private static (double Cosine, Complex Sine) Rotation(Complex f, Complex g)
    {
        double gsize = g.Magnitude;
        if (gsize == 0)
        {
            return (1.0, Complex.Zero);
        }

        double fsize = f.Magnitude;
        if (fsize == 0)
        {
            return (0.0, Complex.Conjugate(g) / gsize);
        }

        double length = double.Hypot(fsize, gsize);
        return (fsize / length, f / fsize * Complex.Conjugate(g) / length);
    }

    private static bool IsDiagonal(Complex[,] t, int n)
    {
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (r != c && t[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int[] Ascending(int n)
    {
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i + 1;
        }

        return order;
    }

    /// <summary>U·F·Uᴴ, which takes the answer back out of the Schur basis.</summary>
    private static Complex[,] Conjugated(Complex[,] u, Complex[,] f)
    {
        Complex[,] left = NormEstimators.Product(u, f, conjugateTranspose: false);
        int n = u.GetLength(0);
        var answer = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < left.GetLength(1); k++)
                {
                    sum += left[r, k] * Complex.Conjugate(u[c, k]);
                }

                answer[r, c] = sum;
            }
        }

        return answer;
    }

    private static Complex[,] Sub(Complex[,] a, int row, int rows, int col, int cols)
    {
        var cut = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                cut[r, c] = a[row + r, col + c];
            }
        }

        return cut;
    }

    private static void Place(Complex[,] a, Complex[,] block, int row, int col)
    {
        for (int c = 0; c < block.GetLength(1); c++)
        {
            for (int r = 0; r < block.GetLength(0); r++)
            {
                a[row + r, col + c] = block[r, c];
            }
        }
    }

    private static void Negate(Complex[,] a)
    {
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                a[r, c] = -a[r, c];
            }
        }
    }

    private static Complex[,] Multiply(Complex[,] a, Complex[,] b) =>
        NormEstimators.Product(a, b, conjugateTranspose: false);

    private static Complex[,] Scaled(Complex[,] a, double by)
    {
        var s = new Complex[a.GetLength(0), a.GetLength(1)];
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                s[r, c] = new Complex(a[r, c].Real / by, a[r, c].Imaginary / by);
            }
        }

        return s;
    }

    private static Complex[,] Difference(Complex[,] a, Complex[,] b)
    {
        var d = new Complex[a.GetLength(0), a.GetLength(1)];
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                d[r, c] = a[r, c] - b[r, c];
            }
        }

        return d;
    }

    private static double InfinityNorm(Complex[,] a)
    {
        double worst = 0.0;
        for (int r = 0; r < a.GetLength(0); r++)
        {
            double sum = 0.0;
            for (int c = 0; c < a.GetLength(1); c++)
            {
                sum += a[r, c].Magnitude;
            }

            worst = Math.Max(worst, sum);
        }

        return worst;
    }

    private static double OneNormFromIdentity(Complex[,] a, int n)
    {
        double worst = 0.0;
        for (int c = 0; c < n; c++)
        {
            double sum = 0.0;
            for (int r = 0; r < n; r++)
            {
                sum += (a[r, c] - (r == c ? Complex.One : Complex.Zero)).Magnitude;
            }

            worst = Math.Max(worst, sum);
        }

        return worst;
    }

    private static double LargestMagnitude(Complex[] values)
    {
        double worst = 0.0;
        foreach (Complex value in values)
        {
            worst = Math.Max(worst, value.Magnitude);
        }

        return worst;
    }

    private static int Position(double[] limits, double value)
    {
        for (int i = 0; i < limits.Length; i++)
        {
            if (value <= limits[i])
            {
                return i;
            }
        }

        return limits.Length - 1;
    }

    private static Complex ComplexSinh(Complex z) => (Complex.Exp(z) - Complex.Exp(-z)) / 2.0;

    private static Complex Atanh(Complex z) => (Complex.Log(1 + z) - Complex.Log(1 - z)) / 2.0;

    /// <summary>How many turns the principal logarithm dropped — <c>ceil((imag(z) − π) / 2π)</c>.</summary>
    private static double Unwinding(Complex z) => Math.Ceiling((z.Imaginary - Math.PI) / (2 * Math.PI));
}
