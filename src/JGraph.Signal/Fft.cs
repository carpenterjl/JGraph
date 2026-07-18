using System.Buffers;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The discrete Fourier transform. Power-of-two lengths use an in-place iterative radix-2
/// Cooley–Tukey FFT (O(n log n)); other lengths use Bluestein's chirp-z algorithm (also
/// O(n log n), built on the radix-2 kernel), with a direct O(n²) DFT only for tiny inputs — so a
/// million-sample audio clip transforms in milliseconds at any length. The transform is engine- and
/// model-independent: it operates on <see cref="System.Numerics.Complex"/> data only.
/// </summary>
public static class Fft
{
    /// <summary>True when <paramref name="n"/> is a positive power of two.</summary>
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>The smallest power of two greater than or equal to <paramref name="n"/> (at least 1).</summary>
    public static int NextPowerOfTwo(int n)
    {
        if (n <= 1)
        {
            return 1;
        }

        int p = 1;
        while (p < n)
        {
            p <<= 1;
        }

        return p;
    }

    /// <summary>Returns the forward transform of complex <paramref name="input"/> as a new array.</summary>
    public static Complex[] Forward(ReadOnlySpan<Complex> input)
    {
        var buffer = input.ToArray();
        Transform(buffer, inverse: false);
        return buffer;
    }

    /// <summary>Returns the forward transform of a real signal as a new array of complex spectra.</summary>
    public static Complex[] Forward(ReadOnlySpan<double> real)
    {
        var buffer = new Complex[real.Length];
        for (int i = 0; i < real.Length; i++)
        {
            buffer[i] = new Complex(real[i], 0);
        }

        Transform(buffer, inverse: false);
        return buffer;
    }

    /// <summary>Returns the inverse transform of complex <paramref name="input"/> as a new array.</summary>
    public static Complex[] Inverse(ReadOnlySpan<Complex> input)
    {
        var buffer = input.ToArray();
        Transform(buffer, inverse: true);
        return buffer;
    }

    /// <summary>
    /// Transforms <paramref name="buffer"/> in place. The forward transform uses the unscaled
    /// convention (sum of x[n]·e^(-2πi kn/N)); the inverse divides by N so that
    /// Inverse(Forward(x)) == x.
    /// </summary>
    public static void Transform(Complex[] buffer, bool inverse)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int n = buffer.Length;
        if (n <= 1)
        {
            return;
        }

        if (IsPowerOfTwo(n))
        {
            Radix2(buffer, n, inverse);
        }
        else if (n <= 32)
        {
            DirectDft(buffer, inverse); // cheaper than Bluestein's three FFTs at tiny sizes
        }
        else
        {
            Bluestein(buffer, inverse);
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                buffer[i] /= n;
            }
        }
    }

    /// <summary>In-place radix-2 over the first <paramref name="n"/> elements (n a power of two).</summary>
    private static void Radix2(Complex[] buffer, int n, bool inverse)
    {
        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        // One pooled table of the n/2 distinct twiddles exp(sign·2πi·k/n), each from a direct
        // sincos. Stage `len` reads it with stride n/len, replacing the old per-butterfly
        // `w *= wLen` recurrence — one complex multiply less per butterfly, and no accumulated
        // rounding drift across a stage.
        int half = n >> 1;
        Complex[] twiddles = ArrayPool<Complex>.Shared.Rent(half);
        try
        {
            double step = (inverse ? 2.0 : -2.0) * System.Math.PI / n;
            for (int k = 0; k < half; k++)
            {
                double angle = step * k;
                twiddles[k] = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                int halfLen = len >> 1;
                int stride = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < halfLen; k++)
                    {
                        Complex u = buffer[i + k];
                        Complex v = buffer[i + k + halfLen] * twiddles[k * stride];
                        buffer[i + k] = u + v;
                        buffer[i + k + halfLen] = u - v;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<Complex>.Shared.Return(twiddles);
        }
    }

    /// <summary>
    /// Bluestein's chirp-z transform: expresses an arbitrary-length DFT as a circular convolution of
    /// length 2n-1 (padded to a power of two), evaluated with the radix-2 kernel. The chirp exponent
    /// k²/2n is reduced modulo 2n in exact integer arithmetic so phases stay accurate for large n.
    /// </summary>
    private static void Bluestein(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        double sign = inverse ? 1.0 : -1.0;
        int m = NextPowerOfTwo((2 * n) - 1);

        // The three large scratch arrays (n + 2m Complex ≈ 5n·32 bytes per call) are pooled;
        // rented arrays hold stale data, so the regions the algorithm assumes are zero-padded
        // are cleared explicitly before use.
        var pool = ArrayPool<Complex>.Shared;
        Complex[] chirpRented = pool.Rent(n);
        Complex[] a = pool.Rent(m);
        Complex[] b = pool.Rent(m);
        try
        {
            Array.Clear(a, 0, m);
            Array.Clear(b, 0, m);
            // c[j] = exp(sign·iπ·j²/n), with j² reduced mod 2n (the exponent's true period).
            long modulus = 2L * n;
            for (int j = 0; j < n; j++)
            {
                long j2 = (long)j * j % modulus;
                double angle = sign * System.Math.PI * j2 / n;
                chirpRented[j] = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            for (int j = 0; j < n; j++)
            {
                a[j] = buffer[j] * chirpRented[j];
            }

            b[0] = Complex.Conjugate(chirpRented[0]);
            for (int j = 1; j < n; j++)
            {
                b[j] = b[m - j] = Complex.Conjugate(chirpRented[j]);
            }

            Radix2(a, m, inverse: false);
            Radix2(b, m, inverse: false);
            for (int j = 0; j < m; j++)
            {
                a[j] *= b[j];
            }

            Radix2(a, m, inverse: true);
            for (int k = 0; k < n; k++)
            {
                buffer[k] = a[k] / m * chirpRented[k]; // the /m completes the radix-2 inverse
            }
        }
        finally
        {
            pool.Return(chirpRented);
            pool.Return(a);
            pool.Return(b);
        }
    }

    private static void DirectDft(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        var result = new Complex[n];
        double sign = inverse ? 1.0 : -1.0;
        double baseAngle = sign * 2.0 * System.Math.PI / n;
        for (int k = 0; k < n; k++)
        {
            Complex sum = Complex.Zero;
            for (int t = 0; t < n; t++)
            {
                double angle = baseAngle * k * t;
                sum += buffer[t] * new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            result[k] = sum;
        }

        Array.Copy(result, buffer, n);
    }
}
