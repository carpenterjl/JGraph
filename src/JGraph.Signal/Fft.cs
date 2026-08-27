using System.Buffers;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The discrete Fourier transform over <see cref="System.Numerics.Complex"/> data — the boxed door
/// onto <see cref="FftKernels"/>, which does the arithmetic over separate real and imaginary planes
/// so that it can be vectorised, batched and threaded. Power-of-two lengths are walked stage by
/// stage or, above 32K points, factored into two passes of shorter ones; other lengths use
/// Bluestein's chirp-z algorithm, with a direct sum only for tiny inputs. A million-sample audio
/// clip transforms in milliseconds at any length.
/// </summary>
/// <remarks>
/// There is one transform in this build and this is its front door. The scripting layer's
/// <c>fft</c> reads packed storage and calls the same kernels directly rather than boxing a
/// <see cref="System.Numerics.Complex"/> per sample, but it is the same butterflies in the same
/// order, so the two roads cannot disagree about an answer (ADR 0096).
/// </remarks>
public static class Fft
{
    /// <summary>True when <paramref name="n"/> is a positive power of two.</summary>
    public static bool IsPowerOfTwo(int n) => FftKernels.IsPowerOfTwo(n);

    /// <summary>The smallest power of two greater than or equal to <paramref name="n"/> (at least 1).</summary>
    public static int NextPowerOfTwo(int n) => FftKernels.NextPowerOfTwo(n);

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

        var pool = ArrayPool<double>.Shared;
        double[] re = pool.Rent(n);
        double[] im = pool.Rent(n);
        try
        {
            for (int i = 0; i < n; i++)
            {
                re[i] = buffer[i].Real;
                im[i] = buffer[i].Imaginary;
            }

            FftKernels.Transform(re.AsSpan(0, n), im.AsSpan(0, n), n, inverse);

            for (int i = 0; i < n; i++)
            {
                buffer[i] = new Complex(re[i], im[i]);
            }
        }
        finally
        {
            pool.Return(re);
            pool.Return(im);
        }
    }
}
