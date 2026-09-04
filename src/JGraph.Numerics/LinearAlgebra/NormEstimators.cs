using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Something a norm can be estimated of without ever being written down: it knows its size, and it
/// can multiply a block of vectors on either side. A matrix is one of these; so is "the inverse of
/// this matrix", which is the whole reason the abstraction is here.
/// </summary>
public interface INormOperand
{
    /// <summary>The order of the square operator.</summary>
    int Dimension { get; }

    /// <summary>Whether the operator maps real vectors to real ones.</summary>
    bool IsReal { get; }

    /// <summary>The operator applied to each column of <paramref name="x"/>.</summary>
    Complex[,] Apply(Complex[,] x);

    /// <summary>The conjugate-transposed operator applied to each column of <paramref name="x"/>.</summary>
    Complex[,] ApplyConjugateTranspose(Complex[,] x);
}

/// <summary>
/// The two norm estimators: a power iteration for the two-norm, and Higham and Tisseur's block
/// algorithm for the one-norm of something that can only be applied, never seen.
/// </summary>
/// <remarks>
/// <para>
/// Both are estimators and neither promises the answer. That is not a weakness to be apologised
/// for: the one-norm estimator exists so that the condition number of a matrix can be had for the
/// price of a few solves instead of a full inverse, and the two-norm estimator so that the largest
/// singular value of a large sparse matrix can be had without a factorization. An estimate that is
/// occasionally a few per cent low is the price, and it is the price MATLAB pays too.
/// </para>
/// <para>
/// The one-norm estimator starts from a block of random sign vectors, so two runs of it need not
/// agree with each other, let alone with another engine's. Below five columns, and whenever the
/// block is as wide as the matrix, it does not iterate at all — it reads the answer off directly —
/// and there the estimate is exact and reproducible.
/// </para>
/// </remarks>
public static class NormEstimators
{
    /// <summary>
    /// The two-norm of a matrix by power iteration on <c>AᴴA</c>, started from the column of column
    /// sums of magnitudes.
    /// </summary>
    /// <returns>The estimate, the number of iterations, and whether the iteration limit was hit.</returns>
    public static (double Estimate, int Count, bool Stalled) TwoNorm(
        Complex[,] s, double tolerance, Random random)
    {
        int m = s.GetLength(0);
        int n = s.GetLength(1);

        var x = new Complex[n];
        for (int c = 0; c < n; c++)
        {
            double sum = 0.0;
            for (int i = 0; i < m; i++)
            {
                sum += s[i, c].Magnitude;
            }

            x[c] = new Complex(sum, 0.0);
        }

        double estimate = VectorNorm(x);
        if (estimate == 0)
        {
            return (0.0, 0, false);
        }

        Scale(x, estimate);
        double previous = 0.0;
        int count = 0;
        while (Math.Abs(estimate - previous) > tolerance * estimate)
        {
            previous = estimate;
            Complex[] sx = Multiply(s, x);

            // An iterate that the matrix annihilates carries no information about its norm, so the
            // walk restarts from somewhere arbitrary rather than dividing by nought.
            if (AllZero(sx))
            {
                for (int i = 0; i < sx.Length; i++)
                {
                    sx[i] = new Complex(random.NextDouble(), 0.0);
                }
            }

            x = MultiplyConjugateTranspose(s, sx);
            double length = VectorNorm(x);
            estimate = length / VectorNorm(sx);
            Scale(x, length);
            count++;
            if (count > 100)
            {
                return (estimate, count, true);
            }
        }

        return (estimate, count, false);
    }

    /// <summary>
    /// The one-norm of an operator, by Higham and Tisseur's block generalization of Hager's
    /// estimator.
    /// </summary>
    /// <param name="operand">What is being measured.</param>
    /// <param name="t">How many columns the search carries at once.</param>
    /// <param name="random">The source of the starting block's signs.</param>
    /// <returns>
    /// The estimate, a unit vector <c>v</c> and its image <c>w</c> with <c>‖w‖₁ = est·‖v‖₁</c>, and
    /// the iteration and matrix-product counts.
    /// </returns>
    public static (double Estimate, Complex[] V, Complex[] W, int Iterations, int Products) OneNorm(
        INormOperand operand, int t, Random random)
    {
        int n = operand.Dimension;
        if (t == n || n <= 4)
        {
            // Small enough to read the answer off: apply the operator to every unit vector at once
            // and take the largest column sum. No iteration, and no randomness either.
            var identity = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                identity[i, i] = Complex.One;
            }

            Complex[,] y = operand.Apply(identity);
            int at = LargestColumn(y);
            var exactly = new Complex[n];
            exactly[at] = Complex.One;
            return (ColumnSum(y, at), exactly, ColumnOf(y, at), 0, 1);

        }

        var x = new Complex[n, t];
        for (int i = 0; i < n; i++)
        {
            x[i, 0] = Complex.One;
        }

        for (int c = 1; c < t; c++)
        {
            for (int i = 0; i < n; i++)
            {
                x[i, c] = new Complex(Sign((2 * random.NextDouble()) - 1), 0.0);
            }
        }

        Unduplicate(x, null, random);
        for (int c = 0; c < t; c++)
        {
            for (int i = 0; i < n; i++)
            {
                x[i, c] /= n;
            }
        }

        var index = new int[t];
        var history = new List<int>();
        Complex[,]? signs = null;
        double estimate = 0.0;
        double best = 0.0;
        int bestAt = 0;
        Complex[] w = new Complex[n];
        int iterations = 0;
        int products = 0;

        while (true)
        {
            iterations++;
            Complex[,] y = operand.Apply(x);
            products++;

            int[] order = DescendingByColumnSum(y);
            estimate = ColumnSum(y, order[0]);
            if (estimate > best || iterations == 2)
            {
                bestAt = index[order[0]];
                w = ColumnOf(y, order[0]);
            }

            if (iterations >= 2 && estimate <= best)
            {
                estimate = best;
                break;
            }

            best = estimate;
            if (iterations > 5)
            {
                iterations = 5;
                break;
            }

            Complex[,] previous = signs ?? new Complex[n, t];
            signs = SignsOf(y);
            if (operand.IsReal)
            {
                // Every column of the new sign block already lying in the old block's span means
                // the search has nowhere left to go.
                int parallel = 0;
                for (int c = 0; c < t; c++)
                {
                    double most = 0.0;
                    for (int d = 0; d < t; d++)
                    {
                        double dot = 0.0;
                        for (int i = 0; i < n; i++)
                        {
                            dot += (Complex.Conjugate(previous[i, d]) * signs[i, c]).Real;
                        }

                        most = Math.Max(most, Math.Abs(dot));
                    }

                    if (most == n)
                    {
                        parallel++;
                    }
                }

                if (parallel == t)
                {
                    break;
                }

                Unduplicate(signs, previous, random);
            }

            Complex[,] z = operand.ApplyConjugateTranspose(signs);
            products++;

            var largest = new double[n];
            for (int i = 0; i < n; i++)
            {
                double most = 0.0;
                for (int c = 0; c < t; c++)
                {
                    most = Math.Max(most, z[i, c].Magnitude);
                }

                largest[i] = most;
            }

            if (iterations >= 2)
            {
                double overall = 0.0;
                foreach (double value in largest)
                {
                    overall = Math.Max(overall, value);
                }

                if (overall == largest[bestAt])
                {
                    break;
                }
            }

            int[] ranked = DescendingOrder(largest);
            int fresh = t;
            if (iterations == 1)
            {
                for (int i = 0; i < t; i++)
                {
                    index[i] = ranked[i];
                    history.Add(ranked[i]);
                }
            }
            else
            {
                int repeats = 0;
                for (int i = 0; i < t; i++)
                {
                    if (history.Contains(ranked[i]))
                    {
                        repeats++;
                    }
                }

                if (repeats == t)
                {
                    break;
                }

                int j = 0;
                for (int i = 0; i < t; i++)
                {
                    if (j >= n)
                    {
                        fresh = i;
                        break;
                    }

                    while (history.Contains(ranked[j]))
                    {
                        j++;
                        if (j >= n)
                        {
                            break;
                        }
                    }

                    if (j >= n)
                    {
                        fresh = i;
                        break;
                    }

                    index[i] = ranked[j];
                    j++;
                }

                for (int i = 0; i < fresh; i++)
                {
                    history.Add(index[i]);
                }
            }

            x = new Complex[n, t];
            for (int j = 0; j < fresh; j++)
            {
                x[index[j], j] = Complex.One;
            }
        }

        var v = new Complex[n];
        v[bestAt] = Complex.One;
        return (estimate, v, w, iterations, products);
    }

    /// <summary>An operand backed by an explicit matrix.</summary>
    public sealed class MatrixOperand(Complex[,] matrix) : INormOperand
    {
        private readonly Complex[,] _matrix = matrix;

        /// <inheritdoc/>
        public int Dimension => Math.Max(_matrix.GetLength(0), _matrix.GetLength(1));

        /// <inheritdoc/>
        public bool IsReal
        {
            get
            {
                foreach (Complex value in _matrix)
                {
                    if (value.Imaginary != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <inheritdoc/>
        public Complex[,] Apply(Complex[,] x) => Product(_matrix, x, conjugateTranspose: false);

        /// <inheritdoc/>
        public Complex[,] ApplyConjugateTranspose(Complex[,] x) => Product(_matrix, x, conjugateTranspose: true);
    }

    /// <summary>The matrix product, optionally with the left operand conjugate-transposed.</summary>
    public static Complex[,] Product(Complex[,] a, Complex[,] x, bool conjugateTranspose)
    {
        int rows = conjugateTranspose ? a.GetLength(1) : a.GetLength(0);
        int inner = conjugateTranspose ? a.GetLength(0) : a.GetLength(1);
        int cols = x.GetLength(1);
        var y = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < inner; k++)
                {
                    sum += conjugateTranspose ? Complex.Conjugate(a[k, i]) * x[k, c] : a[i, k] * x[k, c];
                }

                y[i, c] = sum;
            }
        }

        return y;
    }

    /// <summary>
    /// The Euclidean length of a vector, computed to the last bit: every entry is scaled by a power
    /// of two so nothing overflows, each square is taken exactly, the sum of them keeps its own
    /// rounding error, and one Newton step over the whole residual corrects the square root.
    /// </summary>
    /// <remarks>
    /// The scaled running sum LAPACK's <c>dnrm2</c> was written with is the obvious way to do this
    /// and it is not accurate enough here: it disagrees with a correctly rounded length in over a
    /// third of measured pairs, where MATLAB's <c>norm</c> is correctly rounded, and
    /// <c>normest(magic(6))</c> came back as 110.99999999999999 rather than as 111 because of it.
    /// </remarks>
    public static double VectorNorm(Complex[] x)
    {
        // A complex vector is a real vector of twice the length, and its length is the same sum.
        var parts = new double[2 * x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            parts[2 * i] = x[i].Real;
            parts[(2 * i) + 1] = x[i].Imaginary;
        }

        return VectorNorm(parts);
    }

    /// <summary>The same correctly rounded length, of a real vector.</summary>
    public static double VectorNorm(ReadOnlySpan<double> x)
    {
        double biggest = 0.0;
        foreach (double value in x)
        {
            if (double.IsNaN(value))
            {
                return double.NaN;
            }

            if (double.IsInfinity(value))
            {
                return double.PositiveInfinity;
            }

            biggest = Math.Max(biggest, Math.Abs(value));
        }

        if (biggest == 0)
        {
            return 0.0;
        }

        int shift = Math.ILogB(biggest);
        double scale = Math.ScaleB(1.0, -shift);
        double sum = 0.0;
        double dropped = 0.0;
        foreach (double value in x)
        {
            double part = value * scale;
            double square = part * part;
            dropped += Math.FusedMultiplyAdd(part, part, -square);

            // Neumaier's compensation, which unlike Kahan's is right when the addend is the
            // larger of the two — which it often is here, the sum having started at nought.
            double total = sum + square;
            dropped += Math.Abs(sum) >= square ? (sum - total) + square : (square - total) + sum;
            sum = total;
        }

        double root = Math.Sqrt(sum);
        if (root == 0)
        {
            return 0.0;
        }

        dropped += Math.FusedMultiplyAdd(-root, root, sum);
        return Math.ScaleB(root + (dropped / (2 * root)), shift);
    }

    /// <summary>The largest absolute column sum of a matrix.</summary>
    public static double OneNormOf(Complex[,] a)
    {
        double worst = 0.0;
        for (int c = 0; c < a.GetLength(1); c++)
        {
            worst = Math.Max(worst, ColumnSum(a, c));
        }

        return worst;
    }

    private static double Sign(double value) => value < 0 ? -1.0 : 1.0;

    private static Complex[,] SignsOf(Complex[,] y)
    {
        int rows = y.GetLength(0);
        int cols = y.GetLength(1);
        var s = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                Complex value = y[i, c];
                s[i, c] = value == Complex.Zero
                    ? Complex.One
                    : value.Imaginary == 0
                        ? new Complex(Sign(value.Real), 0.0)
                        : value / value.Magnitude;
            }
        }

        return s;
    }

    /// <summary>Replaces any column that repeats one already present with a fresh block of signs.</summary>
    private static void Unduplicate(Complex[,] s, Complex[,]? previous, Random random)
    {
        int n = s.GetLength(0);
        int t = s.GetLength(1);
        if (t == 1)
        {
            return;
        }

        var seen = new List<Complex[]>();
        int from = 0;
        if (previous is null)
        {
            seen.Add(ColumnOf(s, 0));
            from = 1;
        }
        else
        {
            for (int c = 0; c < t; c++)
            {
                seen.Add(ColumnOf(previous, c));
            }
        }

        for (int c = from; c < t; c++)
        {
            int tries = 0;
            while (RepeatsAny(s, c, seen, n))
            {
                tries++;
                for (int i = 0; i < n; i++)
                {
                    s[i, c] = new Complex(Sign((2 * random.NextDouble()) - 1), 0.0);
                }

                if (tries > (double)n / t)
                {
                    break;
                }
            }

            if (c < t - 1)
            {
                seen.Add(ColumnOf(s, c));
            }
        }
    }

    private static bool RepeatsAny(Complex[,] s, int c, List<Complex[]> seen, int n)
    {
        double most = 0.0;
        foreach (Complex[] column in seen)
        {
            double dot = 0.0;
            for (int i = 0; i < n; i++)
            {
                dot += (Complex.Conjugate(s[i, c]) * column[i]).Real;
            }

            most = Math.Max(most, Math.Abs(dot));
        }

        return most == n;
    }

    private static Complex[] ColumnOf(Complex[,] m, int c)
    {
        var column = new Complex[m.GetLength(0)];
        for (int i = 0; i < column.Length; i++)
        {
            column[i] = m[i, c];
        }

        return column;
    }

    private static double ColumnSum(Complex[,] m, int c)
    {
        double sum = 0.0;
        for (int i = 0; i < m.GetLength(0); i++)
        {
            sum += m[i, c].Magnitude;
        }

        return sum;
    }

    /// <summary>The column with the largest sum of magnitudes; a tie goes to the later column.</summary>
    private static int LargestColumn(Complex[,] m)
    {
        int at = 0;
        double best = double.NegativeInfinity;
        for (int c = 0; c < m.GetLength(1); c++)
        {
            double sum = ColumnSum(m, c);
            if (sum >= best)
            {
                best = sum;
                at = c;
            }
        }

        return at;
    }

    private static int[] DescendingByColumnSum(Complex[,] y)
    {
        int cols = y.GetLength(1);
        var sums = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            sums[c] = ColumnSum(y, c);
        }

        return DescendingOrder(sums);
    }

    /// <summary>
    /// The indices that would sort ascending, reversed — which puts the largest first and, among
    /// equals, the later index first, exactly as sorting and then flipping does.
    /// </summary>
    private static int[] DescendingOrder(double[] values)
    {
        var order = new int[values.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => values[a] != values[b] ? values[a].CompareTo(values[b]) : a.CompareTo(b));
        Array.Reverse(order);
        return order;
    }

    private static Complex[] Multiply(Complex[,] s, Complex[] x)
    {
        int m = s.GetLength(0);
        int n = s.GetLength(1);
        var y = new Complex[m];
        for (int i = 0; i < m; i++)
        {
            Complex sum = Complex.Zero;
            for (int c = 0; c < n; c++)
            {
                sum += s[i, c] * x[c];
            }

            y[i] = sum;
        }

        return y;
    }

    private static Complex[] MultiplyConjugateTranspose(Complex[,] s, Complex[] x)
    {
        int m = s.GetLength(0);
        int n = s.GetLength(1);
        var y = new Complex[n];
        for (int c = 0; c < n; c++)
        {
            Complex sum = Complex.Zero;
            for (int i = 0; i < m; i++)
            {
                sum += Complex.Conjugate(s[i, c]) * x[i];
            }

            y[c] = sum;
        }

        return y;
    }

    private static void Scale(Complex[] x, double by)
    {
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = new Complex(x[i].Real / by, x[i].Imaginary / by);
        }
    }

    private static bool AllZero(Complex[] x)
    {
        foreach (Complex value in x)
        {
            if (value != Complex.Zero)
            {
                return false;
            }
        }

        return true;
    }
}
