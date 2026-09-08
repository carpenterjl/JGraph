using System;
using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The linear-prediction conversions and fits of M137: the two Levinson steps and everything
/// MATLAB builds out of them, the Schur recursion, the line spectral frequencies, and the two
/// time-domain fits <c>prony</c> and <c>stmcb</c>.
/// </summary>
/// <remarks>
/// A prediction polynomial, a set of reflection coefficients and an autocorrelation sequence are
/// three descriptions of one all-pole model, and MATLAB's dozen conversion names are the edges of
/// the triangle between them. All of them are one of two recursions: a step up, which adds a
/// reflection coefficient to a polynomial, and a step down, which takes the last one off again.
/// </remarks>
public static class LinearPrediction
{
    /// <summary>
    /// MATLAB's <c>levup</c>: the order-<c>m+1</c> polynomial that a reflection coefficient makes
    /// out of the order-<c>m</c> one.
    /// </summary>
    public static (Complex[] Polynomial, double Error) StepUp(Complex[] a, Complex k, double error)
    {
        ArgumentNullException.ThrowIfNull(a);
        int m = a.Length - 1;
        var next = new Complex[m + 2];
        next[0] = 1;
        for (int i = 1; i <= m + 1; i++)
        {
            Complex lower = i <= m ? a[i] : Complex.Zero;
            Complex mirror = i == m + 1 ? Complex.One : Complex.Conjugate(a[m + 1 - i]);
            next[i] = lower + (k * mirror);
        }

        double magnitude = k.Magnitude;
        return (next, (1 - (magnitude * magnitude)) * error);
    }

    /// <summary>
    /// MATLAB's <c>levdown</c>: the order-<c>m-1</c> polynomial under an order-<c>m</c> one, and
    /// the reflection coefficient that was taken off.
    /// </summary>
    public static (Complex[] Polynomial, double Error, Complex Reflection) StepDown(
        Complex[] a, double error)
    {
        ArgumentNullException.ThrowIfNull(a);
        int m = a.Length - 1;
        Complex k = a[m];
        double magnitude = k.Magnitude;
        double scale = 1 - (magnitude * magnitude);
        var lower = new Complex[m];
        lower[0] = 1;
        for (int i = 1; i < m; i++)
        {
            lower[i] = (a[i] - (k * Complex.Conjugate(a[m - i]))) / scale;
        }

        return (lower, error / scale, k);
    }

    /// <summary>
    /// MATLAB's <c>rlevinson</c>: the autocorrelation sequence a prediction polynomial came from,
    /// together with the triangle of lower-order polynomials, the reflection coefficients and the
    /// prediction errors.
    /// </summary>
    public static (Complex[] Autocorrelation, Complex[,] Polynomials, Complex[] Reflection, double[] Errors)
        ReverseLevinson(Complex[] a, double efinal)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.Length < 2)
        {
            throw new ArgumentException(
                "rlevinson needs a polynomial of at least first order.", nameof(a));
        }

        var normalised = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            normalised[i] = a[i] / a[0];
        }

        int size = normalised.Length;
        int p = size - 1;
        var u = new Complex[size, size];
        for (int i = 0; i < size; i++)
        {
            u[i, p] = Complex.Conjugate(normalised[size - 1 - i]);
        }

        var errors = new double[p];
        errors[p - 1] = efinal;
        Complex[] current = normalised;
        for (int k = p - 1; k >= 1; k--)
        {
            (current, errors[k - 1], _) = StepDown(current, errors[k]);
            for (int i = 0; i < current.Length; i++)
            {
                u[i, k] = Complex.Conjugate(current[current.Length - 1 - i]);
            }
        }

        double first = errors[0] / (1 - (current[1] * current[1]).Magnitude);
        u[0, 0] = 1;
        var reflection = new Complex[p];
        for (int k = 1; k <= p; k++)
        {
            reflection[k - 1] = Complex.Conjugate(u[0, k]);
        }

        var r = new Complex[p + 1];
        r[0] = first;
        r[1] = -Complex.Conjugate(u[0, 1]) * first;
        for (int k = 2; k <= p; k++)
        {
            Complex sum = 0;
            for (int i = k - 1; i >= 1; i--)
            {
                sum += Complex.Conjugate(u[i - 1, k - 1]) * r[i];
            }

            r[k] = -sum - (reflection[k - 1] * errors[k - 2]);
        }

        return (r, u, reflection, errors);
    }

    /// <summary>
    /// MATLAB's <c>schurrc</c>: the reflection coefficients read straight off the autocorrelation
    /// by the Schur recursion, without ever forming the prediction polynomial.
    /// </summary>
    public static (double[] Reflection, double Error) SchurReflection(double[] r)
    {
        ArgumentNullException.ThrowIfNull(r);
        int n = r.Length;
        var upper = new double[n];
        var lower = new double[n];
        for (int i = 1; i < n; i++)
        {
            upper[i] = r[i];
        }

        for (int i = 0; i < n; i++)
        {
            lower[i] = r[i];
        }

        var k = new double[System.Math.Max(n - 1, 0)];
        for (int m = 1; m < n; m++)
        {
            for (int i = n - 1; i >= 1; i--)
            {
                lower[i] = lower[i - 1];
            }

            lower[0] = 0;
            k[m - 1] = -upper[m] / lower[m];
            for (int i = 0; i < n; i++)
            {
                double a = upper[i];
                double b = lower[i];
                upper[i] = a + (k[m - 1] * b);
                lower[i] = (k[m - 1] * a) + b;
            }
        }

        return (k, lower[n - 1]);
    }

    /// <summary>
    /// MATLAB's <c>poly2lsf</c>: the angles at which the sum and difference filters of a prediction
    /// polynomial have their roots, interleaved and sorted.
    /// </summary>
    public static double[] PolynomialToLineSpectral(double[] a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var normalised = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            normalised[i] = a[i] / a[0];
        }

        int p = normalised.Length - 1;
        var forward = new double[p + 2];
        Array.Copy(normalised, forward, p + 1);
        var difference = new double[p + 2];
        var sum = new double[p + 2];
        for (int i = 0; i < p + 2; i++)
        {
            difference[i] = forward[i] - forward[p + 1 - i];
            sum[i] = forward[i] + forward[p + 1 - i];
        }

        double[] pp;
        double[] qq;
        if ((p % 2) != 0)
        {
            pp = Deconvolve(difference, [1, 0, -1]);
            qq = sum;
        }
        else
        {
            pp = Deconvolve(difference, [1, -1]);
            qq = Deconvolve(sum, [1, 1]);
        }

        var angles = new List<double>();
        foreach (Complex root in Polynomials.Roots(Boxed(pp)))
        {
            if (root.Phase >= 0)
            {
                angles.Add(root.Phase);
                angles.Add(root.Phase);
            }
        }

        foreach (Complex root in Polynomials.Roots(Boxed(qq)))
        {
            if (root.Phase >= 0)
            {
                angles.Add(root.Phase);
                angles.Add(root.Phase);
            }
        }

        angles.Sort();
        var lsf = new double[angles.Count / 2];
        for (int i = 0; i < lsf.Length; i++)
        {
            lsf[i] = angles[2 * i];
        }

        return lsf;
    }

    /// <summary>
    /// MATLAB's <c>lsf2poly</c>: the prediction polynomial whose sum and difference filters have
    /// their roots at the given angles.
    /// </summary>
    public static double[] LineSpectralToPolynomial(double[] lsf)
    {
        ArgumentNullException.ThrowIfNull(lsf);
        int p = lsf.Length;
        var qRoots = new List<Complex>();
        var pRoots = new List<Complex>();
        for (int i = 0; i < p; i += 2)
        {
            qRoots.Add(Complex.Exp(new Complex(0, lsf[i])));
        }

        for (int i = 1; i < p; i += 2)
        {
            pRoots.Add(Complex.Exp(new Complex(0, lsf[i])));
        }

        int qHalf = qRoots.Count;
        for (int i = 0; i < qHalf; i++)
        {
            qRoots.Add(Complex.Conjugate(qRoots[i]));
        }

        int pHalf = pRoots.Count;
        for (int i = 0; i < pHalf; i++)
        {
            pRoots.Add(Complex.Conjugate(pRoots[i]));
        }

        double[] q = RealPolynomial(Polynomials.FromRoots([.. qRoots]));
        double[] pp = RealPolynomial(Polynomials.FromRoots([.. pRoots]));
        double[] difference;
        double[] sum;
        if ((p % 2) != 0)
        {
            difference = Convolve(pp, [1, 0, -1]);
            sum = q;
        }
        else
        {
            difference = Convolve(pp, [1, -1]);
            sum = Convolve(q, [1, 1]);
        }

        var a = new double[System.Math.Max(difference.Length, sum.Length) - 1];
        for (int i = 0; i < a.Length; i++)
        {
            double d = i < difference.Length ? difference[i] : 0;
            double s = i < sum.Length ? sum[i] : 0;
            a[i] = 0.5 * (d + s);
        }

        return a;
    }

    /// <summary>
    /// MATLAB's <c>prony</c>: the rational filter of given orders whose impulse response starts
    /// like the given one, found by solving the linear part first and then reading off the rest.
    /// </summary>
    public static (Complex[] Numerator, Complex[] Denominator) Prony(Complex[] h, int nb, int na)
    {
        ArgumentNullException.ThrowIfNull(h);
        int k = h.Length - 1;
        int largest = System.Math.Max(nb, na);
        Complex[] padded = h;
        if (k <= largest)
        {
            padded = new Complex[largest + 1];
            Array.Copy(h, padded, h.Length);
            k = largest;
        }

        Complex c = padded[0];
        if (c == Complex.Zero)
        {
            c = 1;
        }

        var normalised = new Complex[padded.Length];
        for (int i = 0; i < padded.Length; i++)
        {
            normalised[i] = padded[i] / c;
        }

        Complex Entry(int row, int column) => row >= column ? normalised[row - column] : Complex.Zero;

        var denominator = new Complex[na + 1];
        denominator[0] = 1;
        if (na > 0 && k > nb)
        {
            var left = new Complex[k - nb, na];
            var right = new Complex[k - nb, 1];
            for (int r = 0; r < k - nb; r++)
            {
                right[r, 0] = Entry(nb + 1 + r, 0);
                for (int cc = 0; cc < na; cc++)
                {
                    left[r, cc] = Entry(nb + 1 + r, cc + 1);
                }
            }

            Complex[,] solution = HouseholderQr.BasicSolution(left, right, -1, out _);
            for (int i = 0; i < na; i++)
            {
                denominator[i + 1] = -solution[i, 0];
            }
        }

        var numerator = new Complex[nb + 1];
        for (int r = 0; r <= nb; r++)
        {
            Complex sum = 0;
            for (int cc = 0; cc <= na; cc++)
            {
                sum += denominator[cc] * Entry(r, cc);
            }

            numerator[r] = c * sum;
        }

        return (numerator, denominator);
    }

    /// <summary>
    /// MATLAB's <c>stmcb</c>: Steiglitz and McBride's iteration, which turns the nonlinear fit into
    /// a sequence of linear ones by pre-filtering both signals with the last denominator found.
    /// </summary>
    public static (Complex[] Numerator, Complex[] Denominator) SteiglitzMcBride(
        Complex[] x, Complex[] input, int nb, int na, int iterations, Complex[] start)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(start);

        int n = x.Length;
        Complex[] denominator = start;
        var numerator = new Complex[nb + 1];
        for (int i = 0; i < iterations; i++)
        {
            Complex[] u = AllPole(denominator, x);
            Complex[] v = AllPole(denominator, input);
            var system = new Complex[n, na + nb + 1];
            var rhs = new Complex[n, 1];
            for (int r = 0; r < n; r++)
            {
                rhs[r, 0] = u[r];
                for (int cc = 0; cc < na; cc++)
                {
                    system[r, cc] = r - cc - 1 >= 0 ? -u[r - cc - 1] : Complex.Zero;
                }

                for (int cc = 0; cc <= nb; cc++)
                {
                    system[r, na + cc] = r - cc >= 0 ? v[r - cc] : Complex.Zero;
                }
            }

            Complex[,] solution = HouseholderQr.BasicSolution(system, rhs, -1, out _);
            denominator = new Complex[na + 1];
            denominator[0] = 1;
            for (int cc = 0; cc < na; cc++)
            {
                denominator[cc + 1] = solution[cc, 0];
            }

            numerator = new Complex[nb + 1];
            for (int cc = 0; cc <= nb; cc++)
            {
                numerator[cc] = solution[na + cc, 0];
            }
        }

        return (numerator, denominator);
    }

    /// <summary>The biased autocorrelation <c>lpc</c> takes, through the transform rather than the sum.</summary>
    public static Complex[] TransformAutocorrelation(Complex[] x, int lags)
    {
        ArgumentNullException.ThrowIfNull(x);
        int m = x.Length;
        int n = Fft.NextPowerOfTwo((2 * m) - 1);
        var padded = new Complex[n];
        Array.Copy(x, padded, m);
        Fft.Transform(padded, inverse: false);
        for (int i = 0; i < n; i++)
        {
            double magnitude = padded[i].Magnitude;
            padded[i] = magnitude * magnitude;
        }

        Fft.Transform(padded, inverse: true);
        var r = new Complex[System.Math.Min(lags + 1, n)];
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = padded[i] / m;
        }

        return r;
    }

    /// <summary>The response of an all-pole filter, which is the pre-filter both fits use.</summary>
    private static Complex[] AllPole(Complex[] a, Complex[] x)
    {
        var y = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            Complex sum = x[i];
            for (int k = 1; k < a.Length && k <= i; k++)
            {
                sum -= a[k] * y[i - k];
            }

            y[i] = sum / a[0];
        }

        return y;
    }

    /// <summary>Polynomial division with no remainder kept, which is all <c>deconv</c> is used for.</summary>
    private static double[] Deconvolve(double[] numerator, double[] denominator)
    {
        int length = numerator.Length - denominator.Length + 1;
        if (length <= 0)
        {
            return [0];
        }

        var quotient = new double[length];
        var work = (double[])numerator.Clone();
        for (int i = 0; i < length; i++)
        {
            quotient[i] = work[i] / denominator[0];
            for (int k = 0; k < denominator.Length; k++)
            {
                work[i + k] -= quotient[i] * denominator[k];
            }
        }

        return quotient;
    }

    private static double[] Convolve(double[] a, double[] b)
    {
        var product = new double[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            for (int k = 0; k < b.Length; k++)
            {
                product[i + k] += a[i] * b[k];
            }
        }

        return product;
    }

    private static double[] RealPolynomial(Complex[] coefficients)
    {
        var real = new double[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
        {
            real[i] = coefficients[i].Real;
        }

        return real;
    }

    private static Complex[] Boxed(double[] values)
    {
        var boxed = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values[i];
        }

        return boxed;
    }
}
