using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// MATLAB's <c>A\B</c> for a square dense complex <c>A</c>, answering where the matrix is singular
/// rather than refusing, and saying how well it went — the complex half of
/// <see cref="SquareSystems"/> (open item 321, ADR 0365).
/// </summary>
/// <remarks>
/// <para>
/// R2025b warns and answers here as it does for a real matrix (measured): <c>[1i 2i; 2i 4i] \ b</c>
/// is NaN under <c>MATLAB:singularMatrix</c>, a diagonal <c>[1 0; 0 1e-17i]</c> answers under
/// <c>MATLAB:nearlySingularMatrix</c> with RCOND in the text, a NaN anywhere is
/// <c>MATLAB:illConditionedMatrix</c>. The roads are the real ones: a triangular matrix is
/// substituted, lower before upper, and RCOND is its diagonal's smallest magnitude over its largest;
/// anything else takes the partial-pivot LU, whose singular factors are substituted in managed code
/// so the Inf and NaN fall where R2025b's do on both backends.
/// </para>
/// <para>
/// One division rule decides where they fall: a divisor with a zero imaginary part divides each
/// part on its own, so <c>(1 + 0i) / (0 + 0i)</c> is <c>Inf + NaN·i</c> as R2025b's substitutions
/// give it (the textbook formula would give <c>NaN + NaN·i</c>); any other divisor takes Smith's
/// algorithm.
/// </para>
/// <para>
/// RCOND of a nonsingular LU is LAPACK's <c>zgecon</c> estimate — Higham's <c>zlacn2</c> iteration
/// over solves with the factors, O(n²) per step — so the condition is had for a few solves, not an
/// inverse. A real <c>A</c> with a complex <c>B</c> never reaches here: R2025b solves it as two real
/// systems (the scripting layer sends it to <see cref="SquareSystems"/>).
/// </para>
/// </remarks>
public static class ComplexSquareSystems
{
    /// <summary>The spacing of one, MATLAB's eps: the RCOND below which a system is nearly singular.</summary>
    private const double Eps = 2.220446049250313e-16;

    /// <summary>The spacing of one in single precision: a single system's threshold.</summary>
    private const double SingleEps = 1.1920928955078125e-07;

    /// <summary>The backend a system holding a NaN is factored on, as <see cref="SquareSystems"/> does.</summary>
    private static readonly ManagedLinalg NaNThrough = new();

    /// <summary>
    /// Solves A·X = B for a square <paramref name="a"/> and an n-by-k <paramref name="b"/>; neither is
    /// modified. A <paramref name="single"/> system is nearly singular below single's eps.
    /// </summary>
    /// <remarks>A diagonal matrix is substituted as an upper triangle when <paramref name="diagonalAsUpper"/>,
    /// as R2025b's <c>b / D</c> does (open item 341, ADR 0377).</remarks>
    public static SquareCondition Solve(Complex[,] a, Complex[,] b, bool single, out Complex[,] x, out double rcond,
        bool diagonalAsUpper = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int n = a.GetLength(0);
        int nrhs = b.GetLength(1);
        rcond = double.PositiveInfinity;
        if (n == 0)
        {
            x = new Complex[0, nrhs];
            return SquareCondition.Fine;
        }

        Complex[] lu = Flatten(a);
        Complex[] rhs = Flatten(b);
        bool lower = IsLowerTriangular(lu, n);
        bool upper = IsUpperTriangular(lu, n);
        if (lower || upper)
        {
            lower &= !(diagonalAsUpper && upper);
            bool zeroDiagonal = HasZeroDiagonal(lu, n);
            rcond = zeroDiagonal ? 0 : DiagonalRatio(lu, n);
            for (int c = 0; c < nrhs; c++)
            {
                Span<Complex> column = rhs.AsSpan(c * n, n);
                if (lower)
                {
                    ForwardSubstitute(lu, n, column, unitDiagonal: false);
                }
                else
                {
                    BackSubstitute(lu, n, column);
                }
            }

            x = Unflatten(rhs, n, nrhs);
            return Classify(zeroDiagonal, 0, rcond, single);
        }

        double anorm = OneNorm(lu, n);
        var ipiv = new int[n];
        DenseLinalg provider = HasNaN(lu) || HasNaN(rhs) ? NaNThrough : LinalgProvider.Current;
        provider.Zgetrf(n, n, lu, n, ipiv);
        if (HasZeroDiagonal(lu, n))
        {
            rcond = 0;
            for (int c = 0; c < nrhs; c++)
            {
                Span<Complex> column = rhs.AsSpan(c * n, n);
                Interchange(ipiv, n, column);
                ForwardSubstitute(lu, n, column, unitDiagonal: true);
                BackSubstitute(lu, n, column);
            }

            x = Unflatten(rhs, n, nrhs);
            return SquareCondition.Singular;
        }

        rcond = ReciprocalCondition(lu, ipiv, n, anorm);
        provider.Zgetrs(n, nrhs, lu, n, ipiv, rhs, n);
        x = Unflatten(rhs, n, nrhs);
        return Classify(false, anorm, rcond, single);
    }

    /// <summary>
    /// RCOND of a square matrix, as <c>rcond</c> of MATLAB's LU decomposition object answers it: the
    /// <c>zgecon</c> estimate, zero for a zero pivot, NaN for a NaN anywhere.
    /// </summary>
    public static double ReciprocalCondition(Complex[,] a)
    {
        ArgumentNullException.ThrowIfNull(a);
        int n = a.GetLength(0);
        if (n == 0)
        {
            return double.PositiveInfinity;
        }

        Complex[] lu = Flatten(a);
        double anorm = OneNorm(lu, n);
        var ipiv = new int[n];
        DenseLinalg provider = HasNaN(lu) ? NaNThrough : LinalgProvider.Current;
        provider.Zgetrf(n, n, lu, n, ipiv);
        return HasZeroDiagonal(lu, n) ? 0 : ReciprocalCondition(lu, ipiv, n, anorm);
    }

    /// <summary>
    /// One complex number over another as R2025b's substitutions divide: a real divisor divides each
    /// part on its own, anything else takes Smith's algorithm (<see cref="Complex.op_Division(Complex, Complex)"/>).
    /// </summary>
    internal static Complex Divide(Complex n, Complex d) =>
        d.Imaginary == 0 ? new Complex(n.Real / d.Real, n.Imaginary / d.Real) : n / d;

    private static SquareCondition Classify(bool zeroPivot, double anorm, double rcond, bool single)
    {
        if (zeroPivot)
        {
            return SquareCondition.Singular;
        }

        if (double.IsNaN(anorm) || double.IsNaN(rcond))
        {
            return SquareCondition.IllConditioned;
        }

        if (rcond == 0)
        {
            return SquareCondition.Singular;
        }

        return rcond < (single ? SingleEps : Eps) ? SquareCondition.NearlySingular : SquareCondition.Fine;
    }

    /// <summary>RCOND from an LU with no zero pivot: zero for an infinite norm, NaN for a NaN one.</summary>
    private static double ReciprocalCondition(Complex[] lu, int[] ipiv, int n, double anorm)
    {
        if (double.IsNaN(anorm))
        {
            return double.NaN;
        }

        if (double.IsInfinity(anorm))
        {
            return 0;
        }

        foreach (Complex value in lu)
        {
            if (double.IsNaN(value.Real) || double.IsNaN(value.Imaginary))
            {
                return double.NaN;
            }
        }

        if (anorm == 0)
        {
            return 0;
        }

        double inverseNorm = InverseOneNormEstimate(lu, ipiv, n);
        return inverseNorm == 0 ? 0 : (1.0 / inverseNorm) / anorm;
    }

    /// <summary>
    /// LAPACK's <c>zlacn2</c> estimate of ‖A⁻¹‖₁ from A's LU factors (Higham, 1988): a few solves
    /// with A and with Aᴴ, then the alternating-sign test vector as a lower bound.
    /// </summary>
    private static double InverseOneNormEstimate(Complex[] lu, int[] ipiv, int n)
    {
        const int MaxIterations = 5;
        const double SafeMinimum = 2.2250738585072014e-308;
        var x = new Complex[n];
        Array.Fill(x, new Complex(1.0 / n, 0));
        SolveWithLu(lu, ipiv, n, x);
        if (n == 1)
        {
            return x[0].Magnitude;
        }

        double estimate = SumOfMagnitudes(x);
        Signs(x, SafeMinimum);
        SolveWithLuConjugateTranspose(lu, ipiv, n, x);
        int j = LargestMagnitude(x);
        int iteration = 2;
        while (true)
        {
            Array.Clear(x);
            x[j] = Complex.One;
            SolveWithLu(lu, ipiv, n, x);
            double previous = estimate;
            estimate = SumOfMagnitudes(x);
            if (estimate <= previous)
            {
                break; // cycling; zlacn2 keeps the new figure and goes to the alternating test
            }

            Signs(x, SafeMinimum);
            SolveWithLuConjugateTranspose(lu, ipiv, n, x);
            int last = j;
            j = LargestMagnitude(x);
            if (x[last].Magnitude == x[j].Magnitude || iteration >= MaxIterations)
            {
                break;
            }

            iteration++;
        }

        // The alternating-sign vector catches the matrices the iteration underestimates.
        double alternating = 1;
        for (int i = 0; i < n; i++)
        {
            x[i] = new Complex(alternating * (1.0 + ((double)i / (n - 1))), 0);
            alternating = -alternating;
        }

        SolveWithLu(lu, ipiv, n, x);
        double bound = 2.0 * (SumOfMagnitudes(x) / (3.0 * n));
        return Math.Max(estimate, bound);
    }

    private static void Signs(Complex[] x, double safeMinimum)
    {
        for (int i = 0; i < x.Length; i++)
        {
            double magnitude = x[i].Magnitude;
            x[i] = magnitude > safeMinimum ? x[i] / magnitude : Complex.One;
        }
    }

    private static double SumOfMagnitudes(Complex[] x)
    {
        double sum = 0;
        foreach (Complex value in x)
        {
            sum += value.Magnitude;
        }

        return sum;
    }

    private static int LargestMagnitude(Complex[] x)
    {
        int at = 0;
        double best = -1;
        for (int i = 0; i < x.Length; i++)
        {
            double magnitude = x[i].Magnitude;
            if (magnitude > best)
            {
                best = magnitude;
                at = i;
            }
        }

        return at;
    }

    /// <summary>
    /// x := (L·U)⁻¹·x. <c>zgecon</c> never applies the interchanges: they permute the columns of
    /// A⁻¹, which its one-norm does not see, and leaving them out keeps the estimate's sequence of
    /// test vectors LAPACK's.
    /// </summary>
    private static void SolveWithLu(Complex[] lu, int[] ipiv, int n, Span<Complex> x)
    {
        _ = ipiv;
        ForwardSubstitute(lu, n, x, unitDiagonal: true);
        BackSubstitute(lu, n, x);
    }

    /// <summary>x := (L·U)⁻ᴴ·x: Uᴴ forward, then Lᴴ back.</summary>
    private static void SolveWithLuConjugateTranspose(Complex[] lu, int[] ipiv, int n, Span<Complex> x)
    {
        _ = ipiv;
        for (int k = 0; k < n; k++)
        {
            Complex sum = x[k];
            for (int i = 0; i < k; i++)
            {
                sum -= Complex.Conjugate(lu[((long)k * n) + i]) * x[i];
            }

            x[k] = Divide(sum, Complex.Conjugate(lu[((long)k * n) + k]));
        }

        for (int k = n - 1; k >= 0; k--)
        {
            Complex sum = x[k];
            for (int i = k + 1; i < n; i++)
            {
                sum -= Complex.Conjugate(lu[((long)k * n) + i]) * x[i];
            }

            x[k] = sum;
        }
    }

    private static void Interchange(int[] ipiv, int n, Span<Complex> x)
    {
        for (int i = 0; i < n; i++)
        {
            int p = ipiv[i] - 1;
            if (p != i)
            {
                (x[i], x[p]) = (x[p], x[i]);
            }
        }
    }

    /// <summary>L·x = b in place, column by column as the reference <c>ztrsm</c> runs it.</summary>
    private static void ForwardSubstitute(Complex[] a, int n, Span<Complex> x, bool unitDiagonal)
    {
        for (int k = 0; k < n; k++)
        {
            if (!unitDiagonal)
            {
                x[k] = Divide(x[k], a[((long)k * n) + k]);
            }

            Complex xk = x[k];
            for (int i = k + 1; i < n; i++)
            {
                x[i] -= xk * a[((long)k * n) + i];
            }
        }
    }

    /// <summary>U·x = b in place, column by column from the last.</summary>
    private static void BackSubstitute(Complex[] a, int n, Span<Complex> x)
    {
        for (int k = n - 1; k >= 0; k--)
        {
            x[k] = Divide(x[k], a[((long)k * n) + k]);
            Complex xk = x[k];
            for (int i = 0; i < k; i++)
            {
                x[i] -= xk * a[((long)k * n) + i];
            }
        }
    }

    /// <summary>The largest absolute column sum, as <c>zlange('1')</c> measures it.</summary>
    private static double OneNorm(Complex[] a, int n)
    {
        double best = 0;
        for (int c = 0; c < n; c++)
        {
            double sum = 0;
            for (int r = 0; r < n; r++)
            {
                sum += a[((long)c * n) + r].Magnitude;
            }

            if (double.IsNaN(sum))
            {
                return double.NaN;
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    /// <summary>The smallest diagonal magnitude over the largest; NaN when the diagonal holds one.</summary>
    private static double DiagonalRatio(Complex[] a, int n)
    {
        double smallest = double.PositiveInfinity;
        double largest = 0;
        for (int i = 0; i < n; i++)
        {
            double d = a[((long)i * n) + i].Magnitude;
            if (double.IsNaN(d))
            {
                return double.NaN;
            }

            smallest = Math.Min(smallest, d);
            largest = Math.Max(largest, d);
        }

        return smallest / largest;
    }

    private static bool IsLowerTriangular(Complex[] a, int n)
    {
        for (int c = 1; c < n; c++)
        {
            for (int r = 0; r < c; r++)
            {
                if (a[((long)c * n) + r] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsUpperTriangular(Complex[] a, int n)
    {
        for (int c = 0; c < n - 1; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[((long)c * n) + r] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasZeroDiagonal(Complex[] a, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (a[((long)i * n) + i] == Complex.Zero)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNaN(Complex[] values)
    {
        foreach (Complex v in values)
        {
            if (double.IsNaN(v.Real) || double.IsNaN(v.Imaginary))
            {
                return true;
            }
        }

        return false;
    }

    private static Complex[] Flatten(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var flat = new Complex[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * (long)rows) + r] = matrix[r, c];
            }
        }

        return flat;
    }

    private static Complex[,] Unflatten(Complex[] flat, int rows, int cols)
    {
        var matrix = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                matrix[r, c] = flat[(c * (long)rows) + r];
            }
        }

        return matrix;
    }
}
