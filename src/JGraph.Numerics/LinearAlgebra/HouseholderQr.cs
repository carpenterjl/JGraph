using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// A Householder QR factorization over complex arithmetic, with optional column pivoting, and the
/// complete orthogonal decomposition built on top of it.
/// </summary>
/// <remarks>
/// <para>
/// The real case already has a factorization here, backed by LAPACK and much the faster of the two.
/// This one exists for the complex case and for the routines that need the pivoting order and the
/// reflectors themselves rather than a finished Q — and, once it existed, for the real case too
/// wherever both have to agree with each other. A rank decision taken from one factorization and a
/// solve taken from another is the kind of disagreement that shows up as a wrong answer rather than
/// as a small one.
/// </para>
/// <para>
/// Pivoting keeps a running estimate of each remaining column's length and downdates it as the
/// reflectors are applied, which is what makes the pivoted factorization cost what the unpivoted one
/// costs. A downdated length loses digits as it shrinks, so a column whose estimate has fallen far
/// below where it started is measured again from scratch.
/// </para>
/// </remarks>
public sealed class HouseholderQr
{
    private readonly Complex[,] _qr;
    private readonly Complex[] _tau;
    private readonly int[] _pivot;
    private readonly int _m;
    private readonly int _n;
    private readonly int _p;

    private HouseholderQr(Complex[,] qr, Complex[] tau, int[] pivot, int m, int n)
    {
        _qr = qr;
        _tau = tau;
        _pivot = pivot;
        _m = m;
        _n = n;
        _p = Math.Min(m, n);
    }

    /// <summary>Column j of the factorization was column <c>Pivot[j]</c> of the input, zero-based.</summary>
    public int[] Pivot => (int[])_pivot.Clone();

    /// <summary>The magnitudes of R's diagonal, in factorization order.</summary>
    public double[] DiagonalMagnitudes
    {
        get
        {
            var d = new double[_p];
            for (int i = 0; i < _p; i++)
            {
                d[i] = _qr[i, i].Magnitude;
            }

            return d;
        }
    }

    /// <summary>Factors <paramref name="a"/>, which is not modified.</summary>
    public static HouseholderQr Factor(Complex[,] a, bool pivot)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        var qr = (Complex[,])a.Clone();
        int p = Math.Min(m, n);
        var tau = new Complex[p];
        var order = new int[n];
        var lengths = new double[n];
        var original = new double[n];
        for (int c = 0; c < n; c++)
        {
            order[c] = c;
            lengths[c] = ColumnLength(qr, 0, c, m);
            original[c] = lengths[c];
        }

        for (int k = 0; k < p; k++)
        {
            if (pivot)
            {
                int best = k;
                for (int c = k + 1; c < n; c++)
                {
                    if (lengths[c] > lengths[best])
                    {
                        best = c;
                    }
                }

                if (best != k)
                {
                    for (int i = 0; i < m; i++)
                    {
                        (qr[i, k], qr[i, best]) = (qr[i, best], qr[i, k]);
                    }

                    (order[k], order[best]) = (order[best], order[k]);
                    (lengths[k], lengths[best]) = (lengths[best], lengths[k]);
                    (original[k], original[best]) = (original[best], original[k]);
                }
            }

            tau[k] = Reflect(qr, k, m);

            // The reflector is applied conjugated here and in the transposed application below, and
            // unconjugated only where Q itself is formed. For real data the two are the same scalar,
            // which is why getting this wrong showed up as nothing at all until a complex matrix
            // reached it and lsqminnorm of a complex diagonal answered its reciprocal unconjugated.
            Complex applied = Complex.Conjugate(tau[k]);
            for (int c = k + 1; c < n; c++)
            {
                ApplyReflector(qr, k, m, applied, qr, c);
            }

            if (!pivot)
            {
                continue;
            }

            for (int c = k + 1; c < n; c++)
            {
                if (lengths[c] == 0)
                {
                    continue;
                }

                double ratio = qr[k, c].Magnitude / lengths[c];
                double shrunk = 1.0 - (ratio * ratio);
                lengths[c] = shrunk <= 0 ? 0.0 : lengths[c] * Math.Sqrt(shrunk);

                // The downdate loses a digit every time the column halves; once it has lost most of
                // them the estimate is worthless and the length is taken again.
                if (lengths[c] <= original[c] * 1e-8)
                {
                    lengths[c] = ColumnLength(qr, k + 1, c, m);
                    original[c] = lengths[c];
                }
            }
        }

        return new HouseholderQr(qr, tau, order, m, n);
    }

    /// <summary>R, either the full m-by-n trapezoid or its leading min(m, n) rows.</summary>
    public Complex[,] R(bool full)
    {
        int rows = full ? _m : _p;
        var r = new Complex[rows, _n];
        for (int c = 0; c < _n; c++)
        {
            for (int i = 0; i <= Math.Min(c, rows - 1); i++)
            {
                r[i, c] = _qr[i, c];
            }
        }

        return r;
    }

    /// <summary>Q, either the full m-by-m unitary or its first min(m, n) columns.</summary>
    public Complex[,] Q(bool full)
    {
        int cols = full ? _m : _p;
        var q = new Complex[_m, cols];
        for (int c = 0; c < cols; c++)
        {
            q[c, c] = Complex.One;
        }

        for (int k = _p - 1; k >= 0; k--)
        {
            for (int c = k; c < cols; c++)
            {
                ApplyReflector(_qr, k, _m, _tau[k], q, c);
            }
        }

        return q;
    }

    /// <summary>Qᴴ·b, computed by walking the reflectors rather than by forming Q.</summary>
    public Complex[,] ApplyConjugateTranspose(Complex[,] b)
    {
        var y = (Complex[,])b.Clone();
        for (int k = 0; k < _p; k++)
        {
            Complex applied = Complex.Conjugate(_tau[k]);
            for (int c = 0; c < y.GetLength(1); c++)
            {
                ApplyReflector(_qr, k, _m, applied, y, c);
            }
        }

        return y;
    }

    /// <summary>
    /// How many of R's diagonal entries exceed <paramref name="tolerance"/> — the rank the
    /// factorization ascribes to the matrix.
    /// </summary>
    public int RankAbove(double tolerance)
    {
        int rank = 0;
        for (int i = 0; i < _p; i++)
        {
            if (_qr[i, i].Magnitude > tolerance)
            {
                rank++;
            }
        }

        return rank;
    }

    /// <summary>
    /// The tolerance a rank decision is taken at when none was given: the larger dimension times
    /// the spacing of one times the largest diagonal entry, which pivoting has put first.
    /// </summary>
    public double DefaultRankTolerance() =>
        Math.Max(_m, _n) * 2.220446049250313e-16 * (_p == 0 ? 0.0 : _qr[0, 0].Magnitude);

    /// <summary>
    /// The minimum-norm least-squares solution of <c>A·X = B</c>, by completing the pivoted
    /// factorization to a complete orthogonal one when the matrix is rank deficient.
    /// </summary>
    /// <remarks>
    /// The pivoted factorization leaves <c>A·P = Q·[R₁₁ R₁₂; 0 0]</c>, whose top block is k rows of
    /// n columns and is generally not square, so it cannot simply be solved. A second factorization
    /// of that block's conjugate transpose squares it off — <c>[R₁₁ R₁₂] = [T 0]·Zᴴ</c> with T lower
    /// triangular — and then the solution with the smallest length is the one whose last n−k
    /// coordinates in Z's basis are nought, because Z is unitary and so those coordinates cost
    /// length without changing the residual.
    /// </remarks>
    public static Complex[,] MinimumNormSolution(Complex[,] a, Complex[,] b, double tolerance, out int rank)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        int rhs = b.GetLength(1);
        HouseholderQr qr = Factor(a, pivot: true);
        double cut = tolerance >= 0 ? tolerance : qr.DefaultRankTolerance();
        rank = qr.RankAbove(cut);

        var x = new Complex[n, rhs];
        if (rank == 0)
        {
            return x;
        }

        Complex[,] c = qr.ApplyConjugateTranspose(b);
        Complex[,] r = qr.R(full: false);

        if (rank == Math.Min(m, n) && n <= m)
        {
            // Full column rank: the leading block is square and triangular, and the pivoting is the
            // only thing left to undo.
            var y = new Complex[rank, rhs];
            for (int col = 0; col < rhs; col++)
            {
                for (int i = 0; i < rank; i++)
                {
                    y[i, col] = c[i, col];
                }
            }

            SolveUpper(r, rank, y);
            for (int col = 0; col < rhs; col++)
            {
                for (int i = 0; i < rank; i++)
                {
                    x[qr._pivot[i], col] = y[i, col];
                }
            }

            return x;
        }

        var wide = new Complex[n, rank];
        for (int i = 0; i < rank; i++)
        {
            for (int j = 0; j < n; j++)
            {
                wide[j, i] = Complex.Conjugate(r[i, j]);
            }
        }

        HouseholderQr second = Factor(wide, pivot: false);
        Complex[,] zq = second.Q(full: false);
        Complex[,] tt = second.R(full: false);

        // T is the conjugate transpose of that second R, so a lower-triangular solve here is an
        // upper-triangular one over there, done by conjugating both sides.
        var head = new Complex[rank, rhs];
        for (int col = 0; col < rhs; col++)
        {
            for (int i = 0; i < rank; i++)
            {
                head[i, col] = Complex.Conjugate(c[i, col]);
            }
        }

        SolveUpperTransposed(tt, rank, head);
        for (int col = 0; col < rhs; col++)
        {
            for (int i = 0; i < rank; i++)
            {
                head[i, col] = Complex.Conjugate(head[i, col]);
            }
        }

        for (int col = 0; col < rhs; col++)
        {
            for (int j = 0; j < n; j++)
            {
                Complex sum = Complex.Zero;
                for (int i = 0; i < rank; i++)
                {
                    sum += zq[j, i] * head[i, col];
                }

                x[qr._pivot[j], col] = sum;
            }
        }

        return x;
    }

    /// <summary>Solves <c>R·X = B</c> in place for an upper triangular leading block of order n.</summary>
    public static void SolveUpper(Complex[,] r, int n, Complex[,] b)
    {
        int rhs = b.GetLength(1);
        for (int col = 0; col < rhs; col++)
        {
            for (int i = n - 1; i >= 0; i--)
            {
                Complex sum = b[i, col];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= r[i, j] * b[j, col];
                }

                b[i, col] = sum / r[i, i];
            }
        }
    }

    /// <summary>Solves <c>Rᵀ·X = B</c> in place, where R is upper triangular — a forward substitution.</summary>
    private static void SolveUpperTransposed(Complex[,] r, int n, Complex[,] b)
    {
        int rhs = b.GetLength(1);
        for (int col = 0; col < rhs; col++)
        {
            for (int i = 0; i < n; i++)
            {
                Complex sum = b[i, col];
                for (int j = 0; j < i; j++)
                {
                    sum -= r[j, i] * b[j, col];
                }

                b[i, col] = sum / r[i, i];
            }
        }
    }

    /// <summary>
    /// Builds the reflector that puts column k on its axis, storing it below the diagonal and
    /// answering the scalar that finishes it.
    /// </summary>
    private static Complex Reflect(Complex[,] qr, int k, int m)
    {
        Complex alpha = qr[k, k];
        double tail = ColumnLength(qr, k + 1, k, m);
        if (tail == 0 && alpha.Imaginary == 0)
        {
            return Complex.Zero;
        }

        double size = double.Hypot(alpha.Magnitude, tail);
        double beta = alpha.Real >= 0 ? -size : size;
        Complex divisor = alpha - beta;
        for (int i = k + 1; i < m; i++)
        {
            qr[i, k] /= divisor;
        }

        qr[k, k] = new Complex(beta, 0.0);
        return (beta - alpha) / beta;
    }

    /// <summary>Applies the reflector stored in column k of <paramref name="qr"/> to one column of a target.</summary>
    private static void ApplyReflector(Complex[,] qr, int k, int m, Complex tau, Complex[,] target, int col)
    {
        if (tau == Complex.Zero)
        {
            return;
        }

        Complex dot = target[k, col];
        for (int i = k + 1; i < m; i++)
        {
            dot += Complex.Conjugate(qr[i, k]) * target[i, col];
        }

        Complex scaled = tau * dot;
        target[k, col] -= scaled;
        for (int i = k + 1; i < m; i++)
        {
            target[i, col] -= qr[i, k] * scaled;
        }
    }

    private static double ColumnLength(Complex[,] a, int from, int col, int m)
    {
        double scale = 0.0;
        double sum = 1.0;
        for (int i = from; i < m; i++)
        {
            foreach (double part in new[] { a[i, col].Real, a[i, col].Imaginary })
            {
                if (part == 0)
                {
                    continue;
                }

                double size = Math.Abs(part);
                if (scale < size)
                {
                    double ratio = scale / size;
                    sum = 1.0 + (sum * ratio * ratio);
                    scale = size;
                }
                else
                {
                    double ratio = size / scale;
                    sum += ratio * ratio;
                }
            }
        }

        return scale == 0 ? 0.0 : scale * Math.Sqrt(sum);
    }
}
