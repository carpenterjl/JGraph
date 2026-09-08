using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>Whether a rotation map is drawn against frequency or against order.</summary>
public enum RotationAxis
{
    /// <summary>Hertz: the signal is analysed in time.</summary>
    Frequency,

    /// <summary>Orders of the shaft: the signal is first resampled to constant samples per cycle.</summary>
    Order,
}

/// <summary>How a map's magnitudes are reported.</summary>
public enum MapAmplitude
{
    /// <summary>Root mean square.</summary>
    Rms,

    /// <summary>Peak amplitude, which is the root mean square times the square root of two.</summary>
    Peak,

    /// <summary>Power, which is the square of the root mean square.</summary>
    Power,
}

/// <summary>What a rotation map gives back.</summary>
public sealed class RotationMap
{
    /// <summary>The map, indexed frame then bin.</summary>
    public double[][] Values { get; set; } = [];

    /// <summary>The frequency or order of every bin.</summary>
    public double[] Bins { get; set; } = [];

    /// <summary>The rotation rate of every frame.</summary>
    public double[] Rpm { get; set; } = [];

    /// <summary>The time of every frame.</summary>
    public double[] Times { get; set; } = [];

    /// <summary>The resolution the window achieved.</summary>
    public double Resolution { get; set; }
}

/// <summary>
/// MATLAB's <c>rpmfreqmap</c> and <c>rpmordermap</c>: a spectrogram of a run-up or run-down, read
/// against the shaft's speed rather than against the clock.
/// </summary>
/// <remarks>
/// The two differ in one step. A frequency map is a spectrogram of the signal as recorded, so a
/// component locked to the shaft sweeps across it. An order map first resamples the signal to a
/// constant number of samples per revolution, which freezes every shaft-locked component onto a
/// vertical line — its order — whatever the speed was doing.
/// </remarks>
public static class RotationMaps
{
    /// <summary>The map, its axes and the resolution its window achieved.</summary>
    public static RotationMap Compute(
        double[] x,
        double fs,
        double[] rpm,
        RotationAxis axis,
        double? resolution,
        double overlapPercent,
        MapAmplitude amplitude,
        bool decibels,
        Func<int, double[]> windowOf)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(rpm);
        ArgumentNullException.ThrowIfNull(windowOf);

        var time = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            time[i] = i / fs;
        }

        double[] resampled;
        double rate;
        double[] phaseUp = [];
        double[] rpmUp = [];
        double[] timeUp = [];
        if (axis == RotationAxis.Order)
        {
            (resampled, rate, phaseUp, rpmUp, timeUp) = ConstantSamplesPerCycle(x, fs, rpm, time);
        }
        else
        {
            resampled = x;
            rate = fs;
        }

        int shortest = 4;
        int longest = resampled.Length;
        double wanted = resolution ?? (axis == RotationAxis.Order ? rate / 256 : rate / 128);
        double smallest = EquivalentBandwidth(windowOf(longest)) * rate / longest;
        double largest = EquivalentBandwidth(windowOf(shortest)) * rate / shortest;
        int length = wanted <= smallest ? longest
            : wanted >= largest ? shortest
            : WindowLengthFor(wanted, rate, windowOf);

        double[] taps = windowOf(length);
        double sum = 0;
        foreach (double w in taps)
        {
            sum += w;
        }

        var scaled = new double[taps.Length];
        for (int i = 0; i < taps.Length; i++)
        {
            scaled[i] = taps[i] / sum;
        }

        int overlap = System.Math.Min(
            (int)System.Math.Ceiling(overlapPercent / 100 * scaled.Length), scaled.Length - 1);
        double achieved = SpectralMeasurements.EquivalentNoiseBandwidth(scaled, null)
            * rate / scaled.Length;
        int nfft = System.Math.Max(256, scaled.Length);

        var boxed = new Complex[resampled.Length];
        for (int i = 0; i < resampled.Length; i++)
        {
            boxed[i] = resampled[i];
        }

        ShortTimeAnswer answer = ShortTimeTransforms.Transform(boxed, new ShortTimeRequest
        {
            Window = scaled,
            Overlap = overlap,
            Nfft = nfft,
            SampleRate = rate,
            Range = SpectralRange.OneSided,
        });

        double[][] map = Scale(answer.Values, amplitude, decibels, nfft);
        double[] bins = answer.Frequencies;
        double[] frames = answer.Times;

        if (axis == RotationAxis.Order)
        {
            double fastest = 0;
            foreach (double r in rpm)
            {
                fastest = System.Math.Max(fastest, r / 60);
            }

            double ceiling = fs / (2 * fastest);
            int keep = 0;
            while (keep < bins.Length && bins[keep] <= ceiling)
            {
                keep++;
            }

            if (keep < bins.Length)
            {
                keep++;
            }

            bins = bins[..keep];
            for (int c = 0; c < map.Length; c++)
            {
                map[c] = map[c][..keep];
            }

            return new RotationMap
            {
                Values = map,
                Bins = bins,
                Rpm = Interpolate(phaseUp, rpmUp, frames),
                Times = Interpolate(phaseUp, timeUp, frames),
                Resolution = achieved,
            };
        }

        return new RotationMap
        {
            Values = map,
            Bins = bins,
            Rpm = Interpolate(time, rpm, frames),
            Times = frames,
            Resolution = achieved,
        };
    }

    /// <summary>
    /// MATLAB's <c>toConstantSamplesPerCycle</c>: the signal resampled onto an evenly spaced phase
    /// axis, so that a shaft order becomes a fixed frequency.
    /// </summary>
    public static (double[] Signal, double Rate, double[] Phase, double[] Rpm, double[] Time)
        ConstantSamplesPerCycle(double[] x, double fs, double[] rpm, double[] time)
    {
        double fastest = 0;
        foreach (double r in rpm)
        {
            fastest = System.Math.Max(fastest, r / 60);
        }

        double maximumOrder = fs / (2 * fastest);
        double rate = 4 * (2 * maximumOrder);
        const int Factor = 15;
        double[] filter = Multirate.ResampleFilter(Factor, 1, 10, 5);
        double[] up = Multirate.Resample(x, Factor, 1, filter, filter.Length);

        var upTime = new double[up.Length];
        for (int i = 0; i < up.Length; i++)
        {
            upTime[i] = (double)i / (Factor * fs);
        }

        double[] upRpm = Interpolate(time, rpm, upTime);
        var phase = new double[up.Length];
        for (int i = 1; i < up.Length; i++)
        {
            phase[i] = phase[i - 1]
                + ((upRpm[i] + upRpm[i - 1]) / 2 / (60.0 * Factor * fs));
        }

        var keptPhase = new List<double>();
        var keptRpm = new List<double>();
        var keptTime = new List<double>();
        var keptSignal = new List<double>();
        for (int i = 0; i < up.Length; i++)
        {
            if (i == 0 || phase[i] > phase[i - 1])
            {
                keptPhase.Add(phase[i]);
                keptRpm.Add(upRpm[i]);
                keptTime.Add(upTime[i]);
                keptSignal.Add(up[i]);
            }
        }

        var even = new List<double>();
        for (double p = phase[0]; p <= phase[^1]; p += 1 / rate)
        {
            even.Add(p);
        }

        double[] resampled = Interpolate([.. keptPhase], [.. keptSignal], [.. even]);
        return (resampled, rate, [.. keptPhase], [.. keptRpm], [.. keptTime]);
    }

    /// <summary>MATLAB's <c>mapAmplitudeScale</c>: the fold, the scaling and the decibels.</summary>
    private static double[][] Scale(Complex[][] frames, MapAmplitude amplitude, bool decibels, int nfft)
    {
        bool odd = (nfft % 2) != 0;
        var map = new double[frames.Length][];
        for (int c = 0; c < frames.Length; c++)
        {
            int bins = frames[c].Length;
            map[c] = new double[bins];
            for (int i = 0; i < bins; i++)
            {
                double magnitude = frames[c][i].Magnitude;
                bool doubled = i > 0 && (odd || i < bins - 1);
                if (doubled)
                {
                    magnitude *= System.Math.Sqrt(2);
                }

                if (amplitude == MapAmplitude.Peak && i > 0)
                {
                    magnitude *= System.Math.Sqrt(2);
                }

                map[c][i] = amplitude == MapAmplitude.Power ? magnitude * magnitude : magnitude;
            }

            if (decibels)
            {
                for (int i = 0; i < bins; i++)
                {
                    map[c][i] = amplitude == MapAmplitude.Power
                        ? 10 * System.Math.Log10(map[c][i])
                        : 20 * System.Math.Log10(map[c][i]);
                }
            }
        }

        return map;
    }

    /// <summary>
    /// MATLAB's <c>getWinDurationForAGivenRBW</c>: the window length whose equivalent noise
    /// bandwidth comes nearest the resolution asked for, found by a fixed-point iteration.
    /// </summary>
    private static int WindowLengthFor(double resolution, double fs, Func<int, double[]> windowOf)
    {
        int length = (int)System.Math.Ceiling(EquivalentBandwidth(windowOf(1000)) * fs / resolution);
        var seen = new List<int> { length };
        for (int step = 0; step < 100; step++)
        {
            int next = (int)System.Math.Ceiling(
                EquivalentBandwidth(windowOf(length)) * fs / resolution);
            if (next == length)
            {
                return length;
            }

            if (seen.Contains(next))
            {
                // The sequence has begun to cycle; take whichever of the lengths it visited comes
                // nearest the resolution that was asked for.
                double best = double.PositiveInfinity;
                int at = length;
                foreach (int candidate in seen)
                {
                    double error = System.Math.Abs(
                        resolution - (EquivalentBandwidth(windowOf(candidate)) * fs / candidate));
                    if (error < best)
                    {
                        best = error;
                        at = candidate;
                    }
                }

                return at;
            }

            seen.Add(next);
            length = next;
        }

        return length;
    }

    /// <summary>The equivalent noise bandwidth of a window, in bins.</summary>
    private static double EquivalentBandwidth(double[] window)
    {
        double energy = 0;
        double sum = 0;
        foreach (double w in window)
        {
            energy += w * w;
            sum += w;
        }

        return energy / (sum * sum) * window.Length;
    }

    /// <summary>Linear interpolation with straight-line extrapolation past either end.</summary>
    public static double[] Interpolate(double[] x, double[] y, double[] at)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(at);

        var values = new double[at.Length];
        for (int k = 0; k < at.Length; k++)
        {
            int i = Bracket(x, at[k]);
            values[k] = y[i] + ((at[k] - x[i]) * (y[i + 1] - y[i]) / (x[i + 1] - x[i]));
        }

        return values;
    }

    private static int Bracket(double[] x, double at)
    {
        int low = 0;
        int high = x.Length - 2;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (x[middle] <= at)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
