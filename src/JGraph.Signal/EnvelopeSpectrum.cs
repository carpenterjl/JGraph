using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// MATLAB's <c>envspectrum</c>: the spectrum of a signal's envelope, which is where the repetition
/// rate of an impulsive fault shows up even though the impulses themselves sit at a resonance.
/// </summary>
/// <remarks>
/// There are two ways to the envelope and MATLAB defaults to the second. The Hilbert method
/// band-passes the signal and takes the magnitude of its analytic signal; the demodulation method
/// shifts the band down to zero, low-passes it at half the bandwidth and doubles it. The two agree
/// when the band is narrow and the second is cheaper, because the low-pass filter is shorter.
/// </remarks>
public static class EnvelopeSpectrum
{
    /// <summary>The envelope of one channel and the one-sided spectrum of that envelope.</summary>
    public static (double[] Spectrum, double[] Frequencies, double[] Envelope) Compute(
        double[] x, double fs, double[] band, bool hilbert, int filterOrder)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(band);

        int n = x.Length;
        double mean = 0;
        foreach (double value in x)
        {
            mean += value;
        }

        mean /= n;
        var centred = new double[n];
        for (int i = 0; i < n; i++)
        {
            centred[i] = x[i] - mean;
        }

        double bandwidth = band[1] - band[0];
        double[] taps = hilbert
            ? FirWindowDesign.Windowed(
                filterOrder,
                [band[0] / (fs / 2), band[1] / (fs / 2)],
                WindowBandType.BandPass,
                SignalWindows.Hamming(filterOrder + 1),
                scale: true,
                hilbert: false,
                out _)
            : FirWindowDesign.Windowed(
                filterOrder,
                [bandwidth / 2 / (fs / 2)],
                WindowBandType.Low,
                SignalWindows.Hamming(filterOrder + 1),
                scale: true,
                hilbert: false,
                out _);

        var envelope = new double[n];
        if (hilbert)
        {
            double[] filtered = CentralConvolution(centred, taps);
            Complex[] analytic = SignalTransforms.Hilbert(filtered, n);
            for (int i = 0; i < n; i++)
            {
                envelope[i] = analytic[i].Magnitude;
            }
        }
        else
        {
            double centre = (band[0] + band[1]) / 2;
            var shifted = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double angle = -2 * System.Math.PI * centre * i / fs;
                shifted[i] = centred[i] * new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            Complex[] filtered = CentralConvolution(shifted, taps);
            for (int i = 0; i < n; i++)
            {
                envelope[i] = (2 * filtered[i]).Magnitude;
            }
        }

        double envelopeMean = 0;
        foreach (double value in envelope)
        {
            envelopeMean += value;
        }

        envelopeMean /= n;
        var transform = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            envelope[i] -= envelopeMean;
            transform[i] = envelope[i];
        }

        Fft.Transform(transform, inverse: false);
        bool odd = (n % 2) != 0;
        int keep = odd ? (n + 1) / 2 : (n / 2) + 1;
        var spectrum = new double[keep];
        var frequencies = new double[keep];
        for (int i = 0; i < keep; i++)
        {
            double magnitude = transform[i].Magnitude / n;
            bool doubled = i > 0 && (odd || i < keep - 1);
            spectrum[i] = doubled ? 2 * magnitude : magnitude;
            frequencies[i] = (double)i / n * fs;
        }

        return (spectrum, frequencies, envelope);
    }

    /// <summary>MATLAB's <c>conv2(x, b, 'same')</c>: the middle of the convolution.</summary>
    private static double[] CentralConvolution(double[] x, double[] b)
    {
        var full = new double[x.Length + b.Length - 1];
        for (int i = 0; i < x.Length; i++)
        {
            for (int k = 0; k < b.Length; k++)
            {
                full[i + k] += x[i] * b[k];
            }
        }

        var kept = new double[x.Length];
        Array.Copy(full, b.Length / 2, kept, 0, x.Length);
        return kept;
    }

    private static Complex[] CentralConvolution(Complex[] x, double[] b)
    {
        var full = new Complex[x.Length + b.Length - 1];
        for (int i = 0; i < x.Length; i++)
        {
            for (int k = 0; k < b.Length; k++)
            {
                full[i + k] += x[i] * b[k];
            }
        }

        var kept = new Complex[x.Length];
        Array.Copy(full, b.Length / 2, kept, 0, x.Length);
        return kept;
    }
}
