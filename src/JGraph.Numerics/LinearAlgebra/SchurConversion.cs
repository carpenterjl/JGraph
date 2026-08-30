using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The two conversions between a real factorization that keeps a conjugate pair in a two-by-two
/// block and the complex factorization that spells the pair out: <c>rsf2csf</c> going one way over
/// a Schur form, and <c>cdf2rdf</c> going the other over an eigendecomposition.
/// </summary>
/// <remarks>
/// A real matrix has real factors, and the price of that is a two-by-two block wherever a pair of
/// eigenvalues is complex. Neither of these routines computes anything new — the spectrum is
/// already in the block — so both are rearrangements, and both are exactly invertible in principle.
/// What they are for is that some algorithms want a strictly triangular T and some want to stay in
/// real arithmetic, and only one factorization can be handed to both.
/// </remarks>
public static class SchurConversion
{
    /// <summary>
    /// Turns a real Schur form into a complex one: T becomes upper triangular with the conjugate
    /// pairs on its diagonal, and U stays unitary with <c>U·T·Uᴴ</c> unchanged.
    /// </summary>
    /// <remarks>
    /// One rotation per two-by-two block, taken from the bottom up so that a rotation never
    /// disturbs a block it has already dealt with. The subdiagonal entry is set to nought outright
    /// afterwards rather than left as whatever the rotation made of it, which is what makes the
    /// answer triangular by construction and not merely triangular to within rounding.
    /// </remarks>
    public static (Complex[,] U, Complex[,] T) RealToComplex(Complex[,] u, Complex[,] t)
    {
        int n = t.GetLength(1);
        var uu = (Complex[,])u.Clone();
        var tt = (Complex[,])t.Clone();

        for (int m = n - 1; m >= 1; m--)
        {
            if (tt[m, m - 1] == Complex.Zero)
            {
                continue;
            }

            // The block's own eigenvalue with the positive imaginary part, which is the one MATLAB's
            // eig reports first and so the one the rotation below is built from.
            Complex mu = FirstEigenvalue(tt[m - 1, m - 1], tt[m - 1, m], tt[m, m - 1], tt[m, m]) - tt[m, m];
            double r = double.Hypot(mu.Magnitude, tt[m, m - 1].Magnitude);
            if (r == 0)
            {
                tt[m, m - 1] = Complex.Zero;
                continue;
            }

            Complex c = new(mu.Real / r, mu.Imaginary / r);
            Complex s = new(tt[m, m - 1].Real / r, tt[m, m - 1].Imaginary / r);
            Complex[,] g = { { Complex.Conjugate(c), s }, { -s, c } };

            for (int j = m - 1; j < n; j++)
            {
                Complex a = tt[m - 1, j];
                Complex b = tt[m, j];
                tt[m - 1, j] = (g[0, 0] * a) + (g[0, 1] * b);
                tt[m, j] = (g[1, 0] * a) + (g[1, 1] * b);
            }

            for (int i = 0; i <= m; i++)
            {
                Complex a = tt[i, m - 1];
                Complex b = tt[i, m];
                tt[i, m - 1] = (a * Complex.Conjugate(g[0, 0])) + (b * Complex.Conjugate(g[0, 1]));
                tt[i, m] = (a * Complex.Conjugate(g[1, 0])) + (b * Complex.Conjugate(g[1, 1]));
            }

            for (int i = 0; i < uu.GetLength(0); i++)
            {
                Complex a = uu[i, m - 1];
                Complex b = uu[i, m];
                uu[i, m - 1] = (a * Complex.Conjugate(g[0, 0])) + (b * Complex.Conjugate(g[0, 1]));
                uu[i, m] = (a * Complex.Conjugate(g[1, 0])) + (b * Complex.Conjugate(g[1, 1]));
            }

            tt[m, m - 1] = Complex.Zero;
        }

        return (uu, tt);
    }

    /// <summary>
    /// The eigenvalue of a two-by-two block that carries the positive imaginary part, or the first
    /// root of its characteristic quadratic when both are real.
    /// </summary>
    public static Complex FirstEigenvalue(Complex a, Complex b, Complex c, Complex d)
    {
        Complex half = (a + d) / 2.0;
        Complex gap = ((a - d) / 2.0 * ((a - d) / 2.0)) + (b * c);
        Complex root = Complex.Sqrt(gap);
        return root.Imaginary >= 0 ? half + root : half - root;
    }

    /// <summary>
    /// Turns a complex eigendecomposition back into a real one: each conjugate pair of columns
    /// becomes their real and imaginary parts, and the pair of eigenvalues becomes a two-by-two
    /// block carrying the imaginary part off the diagonal.
    /// </summary>
    /// <returns>
    /// The rearranged V and D, or <c>null</c> when D's diagonal is not a set of real values and
    /// adjacent conjugate pairs — the one shape from which a real form can be recovered.
    /// </returns>
    public static (Complex[,] V, Complex[,] D)? ComplexToReal(Complex[,] v, Complex[,] d)
    {
        int n = d.GetLength(0);
        var complexAt = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (d[i, i].Imaginary != 0)
            {
                complexAt.Add(i);
            }
        }

        var vv = (Complex[,])v.Clone();
        var dd = (Complex[,])d.Clone();
        if (complexAt.Count == 0)
        {
            return (vv, dd);
        }

        // Every second one, because a pair is two entries and the first of them names it. The
        // conjugate has to sit immediately below, and there has to be room for it.
        for (int at = 0; at < complexAt.Count; at += 2)
        {
            int i = complexAt[at];
            if (i + 1 >= n || Complex.Conjugate(d[i, i]) != d[i + 1, i + 1])
            {
                return null;
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < dd.GetLength(1); j++)
            {
                dd[i, j] = new Complex(dd[i, j].Real, 0.0);
            }
        }

        double sqrt2 = Math.Sqrt(2.0);
        for (int at = 0; at < complexAt.Count; at += 2)
        {
            int i = complexAt[at];
            for (int row = 0; row < n; row++)
            {
                Complex left = v[row, i];
                Complex right = v[row, i + 1];
                vv[row, i] = (left + right) / sqrt2;

                // Dividing by i is a quarter turn the other way, written out rather than performed
                // as a complex division so that a real part of nought stays nought.
                Complex over = (left - right) / sqrt2;
                vv[row, i + 1] = new Complex(over.Imaginary, -over.Real);
            }

            dd[i, i + 1] = new Complex(d[i, i].Imaginary, 0.0);
            dd[i + 1, i] = new Complex(d[i + 1, i + 1].Imaginary, 0.0);
        }

        return (vv, dd);
    }
}
