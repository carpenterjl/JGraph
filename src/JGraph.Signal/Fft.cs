using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The discrete Fourier transform. Power-of-two lengths use an in-place iterative radix-2
/// Cooley–Tukey FFT (O(n log n)); other lengths fall back to a direct O(n²) DFT so any length is
/// correct (the spectrum and spectrogram helpers keep their frame sizes powers of two to stay on the
/// fast path). The transform is engine- and model-independent: it operates on
/// <see cref="System.Numerics.Complex"/> data only.
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
            Radix2(buffer, inverse);
        }
        else
        {
            DirectDft(buffer, inverse);
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                buffer[i] /= n;
            }
        }
    }

    private static void Radix2(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;

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

        double sign = inverse ? 1.0 : -1.0;
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = sign * 2.0 * System.Math.PI / len;
            var wLen = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                int half = len >> 1;
                for (int k = 0; k < half; k++)
                {
                    Complex u = buffer[i + k];
                    Complex v = buffer[i + k + half] * w;
                    buffer[i + k] = u + v;
                    buffer[i + k + half] = u - v;
                    w *= wLen;
                }
            }
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
