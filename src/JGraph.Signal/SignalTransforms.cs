namespace JGraph.Signal;

using System.Numerics;
using JGraph.Numerics;

/// <summary>
/// The transforms of the Signal Processing Toolbox that are not the Fourier transform itself: the
/// analytic signal, the chirp z-transform, one bin by recurrence, the Walsh–Hadamard pair, the three
/// cepstra, and the two digit-reversal permutations (M132).
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is built on the same complex transform the interpreter already has, which is
/// the point: they are not new kernels, they are new arrangements. The analytic signal is a spectrum
/// with its negative half deleted and its positive half doubled. The chirp z-transform is a
/// convolution against a chirp, which is a multiplication in the transform domain — Bluestein's
/// trick, and the reason a transform of an arbitrary length costs no more than a padded one. The
/// cepstrum is the transform of the logarithm of a spectrum, and its only difficulty is the
/// logarithm's branch.
/// </para>
/// <para>
/// That branch is where the complex cepstrum earns its second output. A phase read off an
/// <c>atan2</c> is confined to one turn, and taking the logarithm of a spectrum needs the phase that
/// runs continuously across the whole band, so the phase is unwrapped first. A linear term survives
/// the unwrapping — a delay in the signal is a slope in the phase — and it is removed and reported
/// as the delay <c>nd</c>, because without it the cepstrum of a delayed signal would be a different
/// shape rather than the same shape shifted.
/// </para>
/// </remarks>
public static class SignalTransforms
{
    /// <summary>The orderings <c>fwht</c> can produce its rows in.</summary>
    public enum WalshOrdering
    {
        /// <summary>By number of sign changes, which is the closest thing to frequency.</summary>
        Sequency,

        /// <summary>The natural order of the Hadamard matrix.</summary>
        Hadamard,

        /// <summary>Gray-code order.</summary>
        Dyadic,
    }

    /// <summary>The analytic signal of one real column, transformed at length <paramref name="n"/>.</summary>
    public static Complex[] Hilbert(ReadOnlySpan<double> x, int n)
    {
        if (n <= 0)
        {
            return [];
        }

        var re = new double[n];
        var im = new double[n];
        int copy = System.Math.Min(n, x.Length);
        for (int i = 0; i < copy; i++)
        {
            re[i] = x[i];
        }

        FftKernels.Transform(re, im, n, inverse: false);

        // Half the spectrum is deleted and the other half doubled, which leaves a signal whose real
        // part is the original and whose imaginary part is its quadrature.
        int half = n / 2;
        int lastToDouble = (n % 2 == 0) ? half : half + 1;
        for (int i = 1; i < n; i++)
        {
            if (i < lastToDouble)
            {
                re[i] *= 2;
                im[i] *= 2;
            }
            else if (i >= half + 1)
            {
                re[i] = 0;
                im[i] = 0;
            }
        }

        FftKernels.Transform(re, im, n, inverse: true);
        var y = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            y[i] = new Complex(re[i], im[i]);
        }

        return y;
    }

    /// <summary>
    /// The chirp z-transform: <paramref name="k"/> points along the spiral that starts at
    /// <paramref name="a"/> and steps by <paramref name="w"/>.
    /// </summary>
    public static Complex[] ChirpZ(ReadOnlySpan<Complex> x, int k, Complex w, Complex a)
    {
        int m = x.Length;
        if (k <= 0 || m == 0)
        {
            return new Complex[System.Math.Max(0, k)];
        }

        // nextpow2 answers the exponent, not the length: the transform runs at two to that power.
        int nfft = 1 << (int)Polynomials.NextPowerOfTwo(m + k - 1);
        int span = m + System.Math.Max(k - 1, m - 1);
        var ww = new Complex[span];
        for (int i = 0; i < span; i++)
        {
            double kk = -m + 1 + i;
            ww[i] = Complex.Pow(w, kk * kk / 2.0);
        }

        var y = new Complex[nfft];
        for (int n = 0; n < m; n++)
        {
            Complex aa = Complex.Pow(a, -n) * ww[m - 1 + n];
            y[n] = x[n] * aa;
        }

        var fv = new Complex[nfft];
        for (int i = 0; i < k - 1 + m; i++)
        {
            fv[i] = Complex.One / ww[i];
        }

        Transform(y, inverse: false);
        Transform(fv, inverse: false);
        for (int i = 0; i < nfft; i++)
        {
            y[i] *= fv[i];
        }

        Transform(y, inverse: true);

        var g = new Complex[k];
        for (int i = 0; i < k; i++)
        {
            g[i] = y[m - 1 + i] * ww[m - 1 + i];
        }

        return g;
    }

    /// <summary>One column's transform at the frequency bins named by <paramref name="indices"/>.</summary>
    /// <remarks>
    /// Goertzel's recurrence is a second-order filter run once per wanted bin. It beats a whole
    /// transform when the wanted bins are few, and it is exact in the same sense: the same sum, in a
    /// different order.
    /// </remarks>
    public static Complex[] Goertzel(ReadOnlySpan<Complex> x, ReadOnlySpan<double> indices)
    {
        int len = x.Length;
        var y = new Complex[indices.Length];
        for (int f = 0; f < indices.Length; f++)
        {
            double twiddle = 2.0 * System.Math.PI * indices[f] / len;
            double twoCos = 2.0 * System.Math.Cos(twiddle);
            Complex constant = Complex.Exp(-Complex.ImaginaryOne * twiddle);
            Complex correction = Complex.Exp(-Complex.ImaginaryOne * twiddle * (len - 1));

            Complex s1 = Complex.Zero;
            Complex s2 = Complex.Zero;
            for (int i = 0; i < len - 1; i++)
            {
                Complex s0 = x[i] + (twoCos * s1) - s2;
                s2 = s1;
                s1 = s0;
            }

            Complex last = x[len - 1] + (twoCos * s1) - s2;
            y[f] = (last - (s1 * constant)) * correction;
        }

        return y;
    }

    /// <summary>The fast Walsh–Hadamard transform of one column, padded or cut to <paramref name="n"/>.</summary>
    public static double[] Walsh(ReadOnlySpan<double> x, int n, WalshOrdering ordering)
    {
        var v = new double[n];
        int copy = System.Math.Min(n, x.Length);
        for (int i = 0; i < copy; i++)
        {
            v[i] = x[i];
        }

        if (ordering == WalshOrdering.Hadamard)
        {
            int[] order = DigitReverse(n, 2);
            var permuted = new double[n];
            for (int i = 0; i < n; i++)
            {
                permuted[i] = v[order[i]];
            }

            v = permuted;
        }

        for (int i = 0; i + 1 < n; i += 2)
        {
            v[i] += v[i + 1];
            v[i + 1] = v[i] - (2 * v[i + 1]);
        }

        var next = new double[n];
        int stages = Log2(n);
        for (int stage = 1; stage < stages; stage++)
        {
            int m = 1 << stage;
            int j0 = 0;
            int at = 0;
            while (at < n)
            {
                for (int j = j0; j <= j0 + m - 2; j += 2)
                {
                    next[at] = v[j] + v[j + m];
                    next[at + 1] = v[j] - v[j + m];
                    if (ordering == WalshOrdering.Sequency)
                    {
                        next[at + 2] = v[j + 1] - v[j + 1 + m];
                        next[at + 3] = v[j + 1] + v[j + 1 + m];
                    }
                    else
                    {
                        next[at + 2] = v[j + 1] + v[j + 1 + m];
                        next[at + 3] = v[j + 1] - v[j + 1 + m];
                    }

                    at += 4;
                }

                j0 += 2 * m;
            }

            (v, next) = (next, v);
        }

        for (int i = 0; i < n; i++)
        {
            v[i] /= n;
        }

        return v;
    }

    /// <summary>How many times <paramref name="n"/> halves before it reaches one.</summary>
    private static int Log2(int n)
    {
        int bits = 0;
        while ((1 << bits) < n)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>The complex cepstrum of one column, with the linear phase term it removed.</summary>
    public static (double[] Cepstrum, int Delay) ComplexCepstrum(ReadOnlySpan<double> x, int n)
    {
        var re = new double[n];
        var im = new double[n];
        int copy = System.Math.Min(n, x.Length);
        for (int i = 0; i < copy; i++)
        {
            re[i] = x[i];
        }

        FftKernels.Transform(re, im, n, inverse: false);

        var phase = new double[n];
        var magnitude = new double[n];
        for (int i = 0; i < n; i++)
        {
            phase[i] = System.Math.Atan2(im[i], re[i]);
            magnitude[i] = System.Math.Log(Complex.Abs(new Complex(re[i], im[i])));
        }

        PhaseSequences.Unwrap(phase, System.Math.PI);

        // A delay in the signal is a straight line in the unwrapped phase. Taking it out here is
        // what makes the cepstrum of a delayed signal the cepstrum of the signal, and the slope is
        // handed back so that the inverse can put the delay where it was.
        int nh = (n + 1) / 2;
        int idx = n == 1 ? 0 : nh;
        int nd = (int)System.Math.Round(phase[idx] / System.Math.PI, MidpointRounding.AwayFromZero);
        for (int i = 0; i < n; i++)
        {
            phase[i] -= System.Math.PI * nd * i / nh;
        }

        for (int i = 0; i < n; i++)
        {
            re[i] = magnitude[i];
            im[i] = phase[i];
        }

        FftKernels.Transform(re, im, n, inverse: true);
        var answer = new double[n];
        Array.Copy(re, answer, n);
        return (answer, nd);
    }

    /// <summary>The signal a complex cepstrum came from, with its delay put back.</summary>
    public static double[] InverseComplexCepstrum(ReadOnlySpan<double> cepstrum, int delay)
    {
        int n = cepstrum.Length;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = cepstrum[i];
        }

        FftKernels.Transform(re, im, n, inverse: false);

        int nh = (n + 1) / 2;
        for (int i = 0; i < n; i++)
        {
            double lagged = im[i] + (System.Math.PI * delay * i / nh);
            var h = Complex.Exp(new Complex(re[i], lagged));
            re[i] = h.Real;
            im[i] = h.Imaginary;
        }

        FftKernels.Transform(re, im, n, inverse: true);
        var answer = new double[n];
        Array.Copy(re, answer, n);
        return answer;
    }

    /// <summary>The real cepstrum of one column, and the minimum-phase signal that shares it.</summary>
    public static (double[] Cepstrum, double[] MinimumPhase) RealCepstrum(ReadOnlySpan<double> x, bool wantMinimumPhase)
    {
        int n = x.Length;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = x[i];
        }

        FftKernels.Transform(re, im, n, inverse: false);
        for (int i = 0; i < n; i++)
        {
            double magnitude = Complex.Abs(new Complex(re[i], im[i]));
            if (magnitude == 0)
            {
                throw new InvalidOperationException(
                    "rceps needs a signal whose transform has no zero, because it takes the logarithm of it.");
            }

            re[i] = System.Math.Log(magnitude);
            im[i] = 0;
        }

        FftKernels.Transform(re, im, n, inverse: true);
        var cepstrum = new double[n];
        Array.Copy(re, cepstrum, n);
        if (!wantMinimumPhase)
        {
            return (cepstrum, []);
        }

        // Folding the cepstrum onto its causal half and transforming back builds the one signal with
        // this magnitude spectrum whose energy arrives as early as it can.
        int odd = n % 2;
        var weighted = new double[n];
        var weights = new double[n];
        weights[0] = 1;
        for (int i = 1; i < ((n + odd) / 2); i++)
        {
            weights[i] = 2;
        }

        if (odd == 0 && n / 2 < n)
        {
            weights[n / 2] = 1;
        }

        for (int i = 0; i < n; i++)
        {
            weighted[i] = weights[i] * cepstrum[i];
        }

        var wr = new double[n];
        var wi = new double[n];
        Array.Copy(weighted, wr, n);
        FftKernels.Transform(wr, wi, n, inverse: false);
        for (int i = 0; i < n; i++)
        {
            var e = Complex.Exp(new Complex(wr[i], wi[i]));
            wr[i] = e.Real;
            wi[i] = e.Imaginary;
        }

        FftKernels.Transform(wr, wi, n, inverse: true);
        var minimumPhase = new double[n];
        Array.Copy(wr, minimumPhase, n);
        return (cepstrum, minimumPhase);
    }

    /// <summary>The n-by-n discrete Fourier transform matrix, column-major.</summary>
    public static Complex[] TransformMatrix(int n)
    {
        if (n < 0 || (long)n * n > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "dftmtx needs a size that fits in memory.");
        }

        var d = new Complex[n * n];
        var re = new double[n];
        var im = new double[n];
        for (int column = 0; column < n; column++)
        {
            Array.Clear(re);
            Array.Clear(im);
            re[column] = 1.0;
            FftKernels.Transform(re, im, n, inverse: false);
            for (int row = 0; row < n; row++)
            {
                d[(column * n) + row] = new Complex(re[row], im[row]);
            }
        }

        return d;
    }

    /// <summary>
    /// The permutation that reverses the digits of every index of <paramref name="n"/> written in
    /// base <paramref name="radix"/>, as zero-based positions.
    /// </summary>
    public static int[] DigitReverse(int n, int radix)
    {
        if (radix is < 2 or > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), "A radix lies between 2 and 36.");
        }

        int digits = 0;
        long size = 1;
        while (size < n)
        {
            size *= radix;
            digits++;
        }

        if (size != n)
        {
            throw new ArgumentOutOfRangeException(
                nameof(n), $"digitrevorder needs a length that is a power of {radix}.");
        }

        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            int value = i;
            int reversed = 0;
            for (int d = 0; d < digits; d++)
            {
                reversed = (reversed * radix) + (value % radix);
                value /= radix;
            }

            order[i] = reversed;
        }

        return order;
    }

    /// <summary>One in-place transform over a complex array.</summary>
    private static void Transform(Complex[] v, bool inverse)
    {
        int n = v.Length;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = v[i].Real;
            im[i] = v[i].Imaginary;
        }

        FftKernels.Transform(re, im, n, inverse);
        for (int i = 0; i < n; i++)
        {
            v[i] = new Complex(re[i], im[i]);
        }
    }
}
