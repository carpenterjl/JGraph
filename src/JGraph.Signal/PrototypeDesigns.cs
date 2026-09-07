using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The filter designs the multirate names need before M134 is here to provide them: a least-squares
/// FIR, the window method built on it, and the Chebyshev type I lowpass (M133).
/// </summary>
/// <remarks>
/// <para>
/// <c>interp</c>, <c>decimate</c> and <c>resample</c> each design their own anti-imaging or
/// anti-aliasing filter before they touch a sample, and the filter they design is the whole of their
/// parity. Two implementations of polyphase resampling that agree on the algorithm still disagree on
/// every output sample unless they agree on the filter's coefficients to the last bit.
/// </para>
/// <para>
/// So the designs are here, at M133, even though the names <c>firls</c>, <c>fir1</c>, <c>cheby1</c>,
/// <c>cheb1ap</c>, <c>lp2lp</c>, <c>bilinear</c> and <c>grpdelay</c> are all M134's — the same
/// borrowing M132 made when <c>demod</c> needed the zero-phase pass. What is written here is exactly
/// the paths the multirate names take: the linear-phase least-squares design of even symmetry, the
/// window method for a lowpass, and the Chebyshev lowpass through a state-space prototype. The
/// Hilbert and differentiator types, the other three band types and the analogue forms stay M134's.
/// </para>
/// </remarks>
public static class PrototypeDesigns
{
    /// <summary>
    /// <c>firls</c> for a linear-phase filter of even symmetry: the taps that minimise the squared
    /// error against a piecewise-linear desired response.
    /// </summary>
    /// <param name="order">The filter's order; the answer has one more tap than that.</param>
    /// <param name="frequencies">Band edges in pairs, from zero to one.</param>
    /// <param name="amplitudes">The desired response at each band edge.</param>
    /// <param name="weights">One weight per band, or empty for equal weights.</param>
    public static double[] LeastSquares(
        int order, ReadOnlySpan<double> frequencies, ReadOnlySpan<double> amplitudes, ReadOnlySpan<double> weights)
    {
        int bands = frequencies.Length / 2;
        if (frequencies.Length % 2 != 0 || frequencies.Length != amplitudes.Length)
        {
            throw new ArgumentException(
                "A least-squares design needs an even number of band edges and one amplitude each.",
                nameof(frequencies));
        }

        var f = new double[frequencies.Length];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = frequencies[i] / 2;
        }

        var wt = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            wt[i] = System.Math.Sqrt(System.Math.Abs(i < weights.Length ? weights[i] : 1.0));
        }

        int taps = order + 1;
        bool odd = taps % 2 == 1;
        double half = (taps - 1) / 2.0;
        int count = odd ? (int)half + 1 : taps / 2;

        var m = new double[count];
        for (int i = 0; i < count; i++)
        {
            m[i] = odd ? i : i + 0.5;
        }

        bool fullband = true;
        if (f.Length - 1 > 1)
        {
            for (int i = 1; i < f.Length - 1; i += 2)
            {
                if (f[i + 1] - f[i] != 0)
                {
                    fullband = false;
                    break;
                }
            }
        }
        else
        {
            fullband = false;
        }

        bool constantWeights = true;
        for (int i = 0; i < bands; i++)
        {
            if (wt[i] != wt[0])
            {
                constantWeights = false;
                break;
            }
        }

        bool needMatrix = !fullband || !constantWeights;

        double[,]? g = null;
        if (needMatrix)
        {
            g = new double[count, count];
        }

        // The unknowns are the cosine coefficients; index zero is handled apart when the tap count
        // is odd, because its integral has no 1/k in it.
        int start = odd ? 1 : 0;
        var b = new double[count - start];
        double b0 = 0;

        for (int s = 0; s < f.Length; s += 2)
        {
            double slope = (amplitudes[s + 1] - amplitudes[s]) / (f[s + 1] - f[s]);
            double intercept = amplitudes[s] - (slope * f[s]);
            double weight = wt[s / 2] * wt[s / 2];

            if (odd)
            {
                b0 += ((intercept * (f[s + 1] - f[s]))
                    + (slope / 2 * ((f[s + 1] * f[s + 1]) - (f[s] * f[s])))) * weight;
            }

            for (int i = start; i < count; i++)
            {
                double k = m[i];
                double value = slope / (4 * System.Math.PI * System.Math.PI)
                    * (System.Math.Cos(2 * System.Math.PI * k * f[s + 1]) - System.Math.Cos(2 * System.Math.PI * k * f[s]))
                    / (k * k);
                value += (f[s + 1] * ((slope * f[s + 1]) + intercept) * Sinc(2 * k * f[s + 1]))
                    - (f[s] * ((slope * f[s]) + intercept) * Sinc(2 * k * f[s]));
                b[i - start] += value * weight;
            }

            if (g is not null)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < count; j++)
                    {
                        double sum = m[i] + m[j];
                        double difference = m[i] - m[j];
                        g[i, j] += ((0.5 * f[s + 1] * (Sinc(2 * sum * f[s + 1]) + Sinc(2 * difference * f[s + 1])))
                            - (0.5 * f[s] * (Sinc(2 * sum * f[s]) + Sinc(2 * difference * f[s])))) * weight;
                    }
                }
            }
        }

        var rhs = new double[count];
        if (odd)
        {
            rhs[0] = b0;
            Array.Copy(b, 0, rhs, 1, b.Length);
        }
        else
        {
            Array.Copy(b, rhs, b.Length);
        }

        double[] a;
        if (g is not null)
        {
            var column = new double[count, 1];
            for (int i = 0; i < count; i++)
            {
                column[i, 0] = rhs[i];
            }

            double[,] solved = Linear.Solve(g, column);
            a = new double[count];
            for (int i = 0; i < count; i++)
            {
                a[i] = solved[i, 0];
            }
        }
        else
        {
            a = new double[count];
            for (int i = 0; i < count; i++)
            {
                a[i] = wt[0] * wt[0] * 4 * rhs[i];
            }

            if (odd)
            {
                a[0] /= 2;
            }
        }

        var h = new double[taps];
        if (odd)
        {
            int l = (int)half;
            for (int i = 0; i < l; i++)
            {
                h[i] = a[l - i] / 2;
            }

            h[l] = a[0];
            for (int i = 1; i <= l; i++)
            {
                h[l + i] = a[i] / 2;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                h[i] = 0.5 * a[count - 1 - i];
                h[count + i] = 0.5 * a[i];
            }
        }

        return h;
    }

    /// <summary>
    /// <c>fir1</c> for a lowpass: the least-squares design of an ideal brick wall, tapered by a
    /// Hamming window and scaled to unity at zero frequency.
    /// </summary>
    public static double[] WindowedLowpass(int order, double cutoff)
    {
        int taps = order + 1;
        if (taps % 2 == 0 && cutoff >= 1)
        {
            // MATLAB's own correction: an even tap count cannot pass the Nyquist frequency.
            taps++;
        }

        double[] h = LeastSquares(taps - 1, [0, cutoff, cutoff, 1], [1, 1, 0, 0], []);
        double[] window = SignalWindows.Hamming(taps);
        double sum = 0;
        for (int i = 0; i < taps; i++)
        {
            h[i] *= window[i];
            sum += h[i];
        }

        for (int i = 0; i < taps; i++)
        {
            h[i] /= sum;
        }

        return h;
    }

    /// <summary>
    /// <c>cheby1</c> for a digital lowpass: the analogue prototype through a state-space
    /// realisation, frequency-shifted, and mapped to the unit circle by the bilinear transform.
    /// </summary>
    /// <remarks>
    /// The route matters. Going through state space rather than through the polynomial coefficients
    /// is what keeps a tenth-order design's poles where they belong, and it is what MATLAB does, so
    /// a design that took the algebraically equivalent shortcut would agree to about six figures and
    /// no more.
    /// </remarks>
    public static (double[] B, double[] A) ChebyshevLowpass(int order, double rippleDb, double cutoff)
    {
        double warped = 4 * System.Math.Tan(System.Math.PI * cutoff / 2);

        (Complex[] poles, double gain) = ChebyshevPrototype(order, rippleDb);
        FilterCoefficients.StateSpace ss = FilterCoefficients.ZpToSs([], poles, gain);

        int n = ss.A.GetLength(0);
        var a = new double[n, n];
        var b = new double[n, 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = warped * ss.A[i, j];
            }

            b[i, 0] = warped * ss.B[i, 0];
        }

        double[,] c = ss.C;
        double d = ss.D[0, 0];

        // The bilinear transform, in the state-space form MATLAB uses: two shifted copies of the
        // state matrix and one solve, rather than a substitution into the coefficients.
        const double t = 0.5;
        var t1 = new double[n, n];
        var t2 = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double scaled = a[i, j] * t / 2;
                t1[i, j] = (i == j ? 1 : 0) + scaled;
                t2[i, j] = (i == j ? 1 : 0) - scaled;
            }
        }

        double[,] ad = Linear.Solve(t2, t1);
        Complex[] digitalPoles = Eigen.Factor(ad).Values;

        double g = order % 2 == 0 ? System.Math.Pow(10, -rippleDb / 20) : 1;
        Complex product = Complex.One;
        foreach (Complex p in digitalPoles)
        {
            product *= 1 - p;
        }

        double k = g * product.Real / System.Math.Pow(2, order);

        double[] den = FilterCoefficients.RealPolynomial(digitalPoles);
        var zeros = new Complex[order];
        for (int i = 0; i < order; i++)
        {
            zeros[i] = -1;
        }

        double[] zeroPoly = FilterCoefficients.RealPolynomial(zeros);
        var num = new double[den.Length];
        for (int i = 0; i < zeroPoly.Length; i++)
        {
            num[den.Length - zeroPoly.Length + i] = k * zeroPoly[i];
        }

        _ = b;
        _ = c;
        _ = d;
        return (num, den);
    }

    /// <summary><c>cheb1ap</c>: the analogue prototype's poles and gain.</summary>
    private static (Complex[] Poles, double Gain) ChebyshevPrototype(int order, double rippleDb)
    {
        double epsilon = System.Math.Sqrt(System.Math.Pow(10, 0.1 * rippleDb) - 1);
        double mu = System.Math.Asinh(1 / epsilon) / order;

        var raw = new Complex[order];
        for (int i = 0; i < order; i++)
        {
            double angle = (System.Math.PI * ((2 * i) + 1) / (2 * order)) + (System.Math.PI / 2);
            raw[i] = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
        }

        // The real parts are averaged against their mirror image and the imaginary parts
        // antisymmetrised, which is what makes each conjugate pair exact rather than nearly so.
        var poles = new Complex[order];
        double sinhMu = System.Math.Sinh(mu);
        double coshMu = System.Math.Cosh(mu);
        for (int i = 0; i < order; i++)
        {
            double real = (raw[i].Real + raw[order - 1 - i].Real) / 2;
            double imaginary = (raw[i].Imaginary - raw[order - 1 - i].Imaginary) / 2;
            poles[i] = new Complex(sinhMu * real, coshMu * imaginary);
        }

        Complex product = Complex.One;
        foreach (Complex p in poles)
        {
            product *= -p;
        }

        double k = product.Real;
        if (order % 2 == 0)
        {
            k /= System.Math.Sqrt(1 + (epsilon * epsilon));
        }

        return (poles, k);
    }

    /// <summary>
    /// A feed-forward filter's group delay at zero frequency, which is all <c>decimate</c> takes
    /// from <c>grpdelay</c>.
    /// </summary>
    public static double GroupDelayAtZero(ReadOnlySpan<double> b)
    {
        double weighted = 0;
        double total = 0;
        for (int i = 0; i < b.Length; i++)
        {
            weighted += i * b[i];
            total += b[i];
        }

        return total == 0 ? 0 : weighted / total;
    }

    /// <summary>The normalised sinc, which is one at zero rather than undefined.</summary>
    private static double Sinc(double x) =>
        x == 0 ? 1 : System.Math.Sin(System.Math.PI * x) / (System.Math.PI * x);
}
