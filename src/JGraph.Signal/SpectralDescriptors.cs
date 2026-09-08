using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>The options the six spectral descriptors share.</summary>
public sealed class DescriptorRequest
{
    /// <summary>The window, or null for the default that depends on the sample rate.</summary>
    public double[]? Window { get; set; }

    /// <summary>Samples shared between frames, or null for the default.</summary>
    public int? Overlap { get; set; }

    /// <summary>The transform length, or null for the window's length.</summary>
    public int? Nfft { get; set; }

    /// <summary>Whether the frames are squared or taken as magnitudes.</summary>
    public bool Power { get; set; } = true;

    /// <summary>The band to keep, or null for zero to Nyquist.</summary>
    public double[]? Range { get; set; }
}

/// <summary>The half-sided spectrum of every frame, and the axes it lives on.</summary>
public sealed class DescriptorFrames
{
    /// <summary>One spectrum per frame, and one block of frames per channel.</summary>
    public double[][] Columns { get; set; } = [];

    /// <summary>The frequency of every row.</summary>
    public double[] Frequencies { get; set; } = [];

    /// <summary>The mean time of every frame.</summary>
    public double[] Times { get; set; } = [];

    /// <summary>How many channels the frames were divided among.</summary>
    public int Channels { get; set; } = 1;
}

/// <summary>
/// The six descriptors that summarise a spectrum with one number per frame:
/// <c>spectralCentroid</c>'s family, as MATLAB reaches it through one shared preprocessor.
/// </summary>
/// <remarks>
/// These names take either a signal and a sample rate or a spectrogram and its frequencies, and
/// the whole difference is made by one argument: a scalar second argument is a rate and the signal
/// is framed, a vector is a frequency axis and the input is already a spectrum. The framing is
/// MATLAB's <c>signal.internal.spectraldescriptors.stft</c>, which is not the short-time transform
/// of <c>stft</c>: it is half-sided, scaled by half the window's sum, and halves its own end bins.
/// </remarks>
public static class SpectralDescriptors
{
    /// <summary>
    /// MATLAB's <c>preprocess</c> and its private <c>stft</c>: the half-sided spectrum of every
    /// frame of every channel, laid out channel block by channel block.
    /// </summary>
    public static DescriptorFrames Frames(double[][] channels, double fs, DescriptorRequest request)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(request);

        int length = channels[0].Length;
        double[] window = request.Window ?? DefaultWindow(length, fs, request.Overlap);
        int overlap = request.Overlap ?? DefaultOverlap(window.Length, fs);
        int nfft = request.Nfft ?? window.Length;
        double[] range = request.Range ?? [0, fs > 1 ? System.Math.Floor(fs / 2) : fs / 2];

        int hop = window.Length - overlap;
        int hops = ((length - window.Length) / hop) + 1;
        int high = (int)System.Math.Floor((range[1] * nfft / fs) + 1);
        int low = (int)System.Math.Ceiling((range[0] * nfft / fs) + 1);
        int rows = high - low + 1;

        double sum = 0;
        foreach (double w in window)
        {
            sum += w;
        }

        var columns = new double[hops * channels.Length][];
        for (int c = 0; c < channels.Length; c++)
        {
            for (int h = 0; h < hops; h++)
            {
                var frame = new Complex[window.Length];
                for (int i = 0; i < window.Length; i++)
                {
                    frame[i] = channels[c][(h * hop) + i] * window[i];
                }

                Complex[] y = SpectralEstimation.Transform(frame, nfft);
                var kept = new double[rows];
                for (int i = 0; i < rows; i++)
                {
                    Complex value = y[low - 1 + i];
                    kept[i] = request.Power
                        ? (value * Complex.Conjugate(value)).Real / (0.5 * sum * sum)
                        : value.Magnitude / (0.5 * sum);
                }

                if (low == 1 && rows > 0)
                {
                    kept[0] *= 0.5;
                }

                if (high == (int)System.Math.Floor((nfft / 2.0) + 1) && (nfft % 2) == 0 && rows > 0)
                {
                    kept[rows - 1] *= 0.5;
                }

                columns[h + (c * hops)] = kept;
            }
        }

        var frequencies = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            frequencies[i] = fs / nfft * (low - 1 + i);
        }

        if ((nfft % 2) != 0 && high == (int)System.Math.Floor((nfft / 2.0) + 1) && rows > 0)
        {
            frequencies[rows - 1] = fs * (nfft - 1) / (2.0 * nfft);
        }

        var times = new double[hops];
        for (int h = 0; h < hops; h++)
        {
            double mean = 0;
            for (int i = 0; i < window.Length; i++)
            {
                mean += ((h * hop) + i) / fs;
            }

            times[h] = mean / window.Length;
        }

        return new DescriptorFrames
        {
            Columns = columns,
            Frequencies = frequencies,
            Times = times,
            Channels = channels.Length,
        };
    }

    /// <summary>The centroid, spread, skewness and kurtosis of one frame's spectrum.</summary>
    public static (double Centroid, double Spread, double Skewness, double Kurtosis) Moments(
        double[] column, double[] f)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(f);

        double total = 0;
        double weighted = 0;
        for (int i = 0; i < column.Length; i++)
        {
            total += column[i];
            weighted += column[i] * f[i];
        }

        double centroid = weighted / total;
        double second = 0;
        double third = 0;
        double fourth = 0;
        for (int i = 0; i < column.Length; i++)
        {
            double d = f[i] - centroid;
            double dd = d * d;
            second += dd * column[i];
            third += dd * d * column[i];
            fourth += dd * dd * column[i];
        }

        double spread = System.Math.Sqrt(second / total);
        double skewness = third / (spread * spread * spread * total);
        double kurtosis = fourth / (spread * spread * spread * spread * total);
        return (centroid, spread, skewness, kurtosis);
    }

    /// <summary>The geometric mean over the arithmetic one, which is how flat a spectrum is.</summary>
    public static (double Flatness, double Arithmetic, double Geometric) Flatness(double[] column)
    {
        ArgumentNullException.ThrowIfNull(column);
        double logs = 0;
        double total = 0;
        foreach (double value in column)
        {
            logs += System.Math.Log(value + 2.220446049250313e-16);
            total += value;
        }

        double geometric = System.Math.Exp(logs / column.Length);
        double arithmetic = total / column.Length;
        return (geometric / arithmetic, arithmetic, geometric);
    }

    /// <summary>The largest bin over the mean one, which is how peaked a spectrum is.</summary>
    public static (double Crest, double Peak, double Mean) Crest(double[] column)
    {
        ArgumentNullException.ThrowIfNull(column);
        double total = 0;
        double peak = double.NegativeInfinity;
        foreach (double value in column)
        {
            total += value;
            peak = System.Math.Max(peak, value);
        }

        double mean = total / column.Length;
        return (peak / mean, peak, mean);
    }

    /// <summary>The Shannon entropy of a spectrum read as a distribution, in bits.</summary>
    /// <remarks>
    /// Scaled, the entropy is divided by the entropy of a flat spectrum, so a flat one reads one
    /// and a single tone reads nearly zero whatever the number of bins.
    /// </remarks>
    public static double Entropy(double[] column, bool scaled)
    {
        ArgumentNullException.ThrowIfNull(column);
        double total = 0;
        foreach (double value in column)
        {
            total += value;
        }

        double sum = 0;
        foreach (double value in column)
        {
            double p = value / total;
            double term = p * System.Math.Log2(p);
            if (!double.IsNaN(term))
            {
                sum += term;
            }
        }

        return scaled ? -sum / System.Math.Log2(column.Length) : -sum;
    }

    /// <summary>
    /// The window MATLAB picks when none is given: three hundredths of a second when the rate is
    /// high enough for that to be more than a hundred and twenty samples, and otherwise sixty-four
    /// samples or an eighth of the signal.
    /// </summary>
    private static double[] DefaultWindow(int length, double fs, int? overlap)
    {
        int seconds = (int)System.Math.Round(0.03 * fs, MidpointRounding.AwayFromZero);
        int n = seconds > 120
            ? seconds
            : overlap is null
                ? System.Math.Min(64, length)
                : (int)((length + (7.0 * overlap.Value)) / 8);
        return Rectangular(n);
    }

    /// <summary>Two hundredths of a second when that is more than eighty samples, else half.</summary>
    private static int DefaultOverlap(int n, double fs)
    {
        int seconds = (int)System.Math.Round(0.02 * fs, MidpointRounding.AwayFromZero);
        return seconds > 80 ? seconds : (int)(0.5 * n);
    }

    private static double[] Rectangular(int n)
    {
        var window = new double[n];
        Array.Fill(window, 1);
        return window;
    }
}
