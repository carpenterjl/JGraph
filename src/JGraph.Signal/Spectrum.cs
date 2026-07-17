using System.Numerics;

namespace JGraph.Signal;

/// <summary>The single-sided amplitude spectrum of a real signal.</summary>
/// <param name="Frequencies">The frequency of each bin in hertz (0 … sampleRate/2).</param>
/// <param name="Magnitude">The estimated peak amplitude of a sinusoid at each frequency.</param>
public sealed record SpectrumResult(double[] Frequencies, double[] Magnitude)
{
    /// <summary>The magnitude in decibels, <c>20·log10(mag/reference)</c>, floored to avoid −∞.</summary>
    public double[] MagnitudeDb(double reference = 1.0)
    {
        var db = new double[Magnitude.Length];
        double refValue = reference <= 0 ? 1.0 : reference;
        for (int i = 0; i < Magnitude.Length; i++)
        {
            double ratio = Magnitude[i] / refValue;
            db[i] = 20.0 * System.Math.Log10(System.Math.Max(ratio, 1e-12));
        }

        return db;
    }
}

/// <summary>
/// Computes the single-sided amplitude spectrum of a real signal via the <see cref="Fft"/>. The
/// signal is windowed (<see cref="WindowType.Hann"/> by default) and normalized by the window's
/// coherent gain, so the peak of a pure sinusoid reads back at roughly its true amplitude.
/// </summary>
public static class Spectrum
{
    /// <summary>Computes the amplitude spectrum of <paramref name="signal"/> sampled at <paramref name="sampleRate"/> Hz.</summary>
    public static SpectrumResult Compute(
        ReadOnlySpan<double> signal,
        double sampleRate,
        WindowType window = WindowType.Hann)
    {
        int n = signal.Length;
        if (n < 2)
        {
            return new SpectrumResult(new double[n], new double[n]);
        }

        double[] w = Window.Create(window, n);
        var buffer = new Complex[n];
        double windowSum = 0;
        for (int i = 0; i < n; i++)
        {
            buffer[i] = new Complex(signal[i] * w[i], 0);
            windowSum += w[i];
        }

        Fft.Transform(buffer, inverse: false);

        int half = (n / 2) + 1;
        var freqs = new double[half];
        var mag = new double[half];
        double scale = 2.0 / (windowSum <= 0 ? n : windowSum);
        for (int k = 0; k < half; k++)
        {
            freqs[k] = k * sampleRate / n;
            double m = buffer[k].Magnitude * scale;
            if (k == 0)
            {
                m *= 0.5; // DC is not mirrored, so it is not doubled.
            }

            mag[k] = m;
        }

        return new SpectrumResult(freqs, mag);
    }
}
