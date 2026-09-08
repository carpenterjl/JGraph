using System;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Signal;

/// <summary>How <c>tsa</c> resamples a rotation onto a common grid before averaging it.</summary>
public enum SynchronousMethod
{
    /// <summary>Straight lines between samples.</summary>
    Linear,

    /// <summary>A cubic spline through the samples.</summary>
    Spline,

    /// <summary>A shape-preserving cubic.</summary>
    Pchip,

    /// <summary>No interpolation: the segments are averaged in the frequency domain instead.</summary>
    Fft,
}

/// <summary>What a synchronous average gives back.</summary>
public sealed class SynchronousAnswer
{
    /// <summary>The averaged signal, one rotation long.</summary>
    public double[] Average { get; set; } = [];

    /// <summary>The time of every sample of it.</summary>
    public double[] Times { get; set; } = [];

    /// <summary>The segments the average was taken over, one per column.</summary>
    public double[][] Segments { get; set; } = [];

    /// <summary>The rotation rate the average is at, in revolutions per second.</summary>
    public double Rate { get; set; }
}

/// <summary>
/// MATLAB's <c>tsa</c>: the average of a signal over many rotations of a shaft, lined up by the
/// shaft's own tachometer rather than by the clock.
/// </summary>
/// <remarks>
/// Averaging in shaft angle rather than in time is what separates a gear's own vibration from
/// everything else in the machine: anything locked to the shaft survives the average and anything
/// that is not averages away. The two methods differ in how a rotation of varying length is put on
/// a common grid — by interpolating the samples, or by truncating every segment's spectrum to the
/// shortest one's length.
/// </remarks>
public static class SynchronousAverage
{
    /// <summary>The synchronous average of one channel.</summary>
    public static SynchronousAnswer Compute(
        double[] x,
        double[] t,
        double fs,
        double[] pulses,
        double pulsesPerRotation,
        int rotations,
        int resample,
        SynchronousMethod method)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(pulses);

        double[] up = x;
        double[] upTimes = t;
        if (resample > 1)
        {
            up = Upsample(x, resample);
            upTimes = new double[up.Length];
            for (int i = 0; i < up.Length; i++)
            {
                double position = 1 + ((double)i / resample);
                upTimes[i] = Extrapolated(t, position);
            }
        }

        double[] starts = Starts(pulses, pulsesPerRotation);
        return method == SynchronousMethod.Fft
            ? Frequency(up, upTimes, fs, starts, rotations, resample)
            : Time(up, upTimes, fs, starts, rotations, method);
    }

    /// <summary>The pulse times a rotation starts at, which need not fall on a pulse.</summary>
    private static double[] Starts(double[] pulses, double pulsesPerRotation)
    {
        var starts = new List<double>();
        for (double position = 1; position <= pulses.Length; position += pulsesPerRotation)
        {
            int left = (int)System.Math.Floor(position) - 1;
            if (left >= pulses.Length - 1)
            {
                starts.Add(pulses[^1]);
                continue;
            }

            double fraction = position - (left + 1);
            starts.Add(pulses[left] + (fraction * (pulses[left + 1] - pulses[left])));
        }

        return [.. starts];
    }

    /// <summary>Averaging by interpolating every rotation onto the same number of points.</summary>
    private static SynchronousAnswer Time(
        double[] x, double[] t, double fs, double[] starts, int rotations, SynchronousMethod method)
    {
        int segments = starts.Length - 1;
        double shortest = double.PositiveInfinity;
        for (int i = 0; i < segments; i++)
        {
            shortest = System.Math.Min(shortest, starts[i + 1] - starts[i]);
        }

        int n = (int)System.Math.Round(shortest * fs, MidpointRounding.AwayFromZero);
        double rate = 1 / shortest;
        double outputRate = n * rate;

        double[] slopes = method switch
        {
            SynchronousMethod.Spline => Interpolation.SplineSlopes(t, x),
            SynchronousMethod.Pchip => Interpolation.PchipSlopes(t, x),
            _ => [],
        };

        var columns = new double[segments][];
        for (int c = 0; c < segments; c++)
        {
            double width = (starts[c + 1] - starts[c]) / n;
            columns[c] = new double[n];
            for (int i = 0; i < n; i++)
            {
                columns[c][i] = Sample(t, x, slopes, starts[c] + (i * width));
            }
        }

        double[][] grouped = Group(columns, n, rotations);
        int height = grouped.Length == 0 ? 0 : grouped[0].Length;
        var average = new double[height];
        foreach (double[] column in grouped)
        {
            for (int i = 0; i < height; i++)
            {
                average[i] += column[i] / grouped.Length;
            }
        }

        return Finish(average, grouped, outputRate, rate);
    }

    /// <summary>Averaging the segments' spectra, truncated to the shortest segment's length.</summary>
    private static SynchronousAnswer Frequency(
        double[] x, double[] t, double fs, double[] starts, int rotations, int resample)
    {
        int usable = starts.Length - ((starts.Length - 1) % rotations);
        var kept = new List<double>();
        for (int i = 0; i < usable; i += rotations)
        {
            kept.Add(starts[i]);
        }

        double[] edges = [.. kept];
        int segments = edges.Length - 1;
        var first = new int[segments];
        var last = new int[segments];
        for (int i = 0; i < segments; i++)
        {
            first[i] = (int)System.Math.Round(Position(t, edges[i]), MidpointRounding.AwayFromZero);
            last[i] = (int)System.Math.Round(Position(t, edges[i + 1]), MidpointRounding.AwayFromZero) - 1;
        }

        int n = int.MaxValue;
        double effective = 0;
        for (int i = 0; i < segments; i++)
        {
            n = System.Math.Min(n, last[i] - first[i] + 1);
            effective += (last[i] - first[i] + 1.0) / segments;
        }

        double outputRate = n / effective * fs;
        double rate = fs * resample / (effective / rotations);
        bool odd = (n % 2) != 0;
        int half = odd ? (n + 1) / 2 : (n / 2) + 1;

        var averaged = new Complex[half];
        var columns = new Complex[segments][];
        for (int i = 0; i < segments; i++)
        {
            int length = last[i] - first[i] + 1;
            var segment = new Complex[length];
            for (int k = 0; k < length; k++)
            {
                segment[k] = x[first[i] - 1 + k];
            }

            Fft.Transform(segment, inverse: false);
            columns[i] = new Complex[half];
            for (int k = 0; k < half; k++)
            {
                columns[i][k] = segment[k] * n / length;
                averaged[k] += columns[i][k] / segments;
            }
        }

        double[] average = Rebuild(averaged, n, odd, resample);
        var grouped = new double[segments][];
        for (int i = 0; i < segments; i++)
        {
            grouped[i] = Rebuild(columns[i], n, odd, resample);
        }

        return Finish(average, grouped, outputRate, rate);
    }

    private static SynchronousAnswer Finish(
        double[] average, double[][] segments, double outputRate, double rate)
    {
        var times = new double[average.Length];
        for (int i = 0; i < average.Length; i++)
        {
            times[i] = i / outputRate;
        }

        return new SynchronousAnswer
        {
            Average = average,
            Times = times,
            Segments = segments,
            Rate = rate,
        };
    }

    /// <summary>The signal a half spectrum stands for, decimated back to the original rate.</summary>
    private static double[] Rebuild(Complex[] half, int n, bool odd, int resample)
    {
        var whole = new Complex[n];
        for (int i = 0; i < half.Length; i++)
        {
            whole[i] = half[i];
        }

        int last = odd ? half.Length - 1 : half.Length - 2;
        for (int i = 1; i <= last; i++)
        {
            whole[n - i] = Complex.Conjugate(half[i]);
        }

        Fft.Transform(whole, inverse: true);
        var real = new double[(n + resample - 1) / resample];
        for (int i = 0; i < real.Length; i++)
        {
            real[i] = whole[i * resample].Real;
        }

        return real;
    }

    /// <summary>Several rotations stacked into one column, when the caller asked for more than one.</summary>
    private static double[][] Group(double[][] columns, int n, int rotations)
    {
        if (rotations == 1)
        {
            return columns;
        }

        int wide = columns.Length - (columns.Length % rotations);
        int count = wide / rotations;
        var grouped = new double[count][];
        for (int c = 0; c < count; c++)
        {
            grouped[c] = new double[n * rotations];
            for (int r = 0; r < rotations; r++)
            {
                Array.Copy(columns[(c * rotations) + r], 0, grouped[c], r * n, n);
            }
        }

        return grouped;
    }

    /// <summary>The signal interpolated to a whole multiple of its rate by zero-padding its spectrum.</summary>
    private static double[] Upsample(double[] x, int factor)
    {
        int n = x.Length;
        var transform = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            transform[i] = x[i];
        }

        Fft.Transform(transform, inverse: false);
        int half = (n % 2) != 0 ? (n + 1) / 2 : (n / 2) + 1;
        var padded = new Complex[factor * n];
        for (int i = 0; i < half; i++)
        {
            padded[i] = transform[i] * factor;
        }

        for (int i = half; i < n; i++)
        {
            padded[padded.Length - n + i] = transform[i] * factor;
        }

        Fft.Transform(padded, inverse: true);
        var up = new double[padded.Length];
        for (int i = 0; i < up.Length; i++)
        {
            up[i] = padded[i].Real;
        }

        return up;
    }

    /// <summary>One interpolated sample, extrapolating from the end segments when asked outside.</summary>
    private static double Sample(double[] t, double[] x, double[] slopes, double at)
    {
        int n = t.Length;
        int i = Bracket(t, at);
        double h = t[i + 1] - t[i];
        double s = at - t[i];
        if (slopes.Length == 0)
        {
            return x[i] + (s * (x[i + 1] - x[i]) / h);
        }

        double delta = (x[i + 1] - x[i]) / h;
        double c = ((3 * delta) - (2 * slopes[i]) - slopes[i + 1]) / h;
        double d = (slopes[i] + slopes[i + 1] - (2 * delta)) / (h * h);
        _ = n;
        return x[i] + (s * (slopes[i] + (s * (c + (s * d)))));
    }

    /// <summary>The fractional index a time falls at, MATLAB's <c>interp1(t, 1:n, at)</c>.</summary>
    private static double Position(double[] t, double at)
    {
        int i = Bracket(t, at);
        return i + 1 + ((at - t[i]) / (t[i + 1] - t[i]));
    }

    /// <summary>A time read at a fractional index, MATLAB's <c>interp1(1:n, t, at)</c>.</summary>
    private static double Extrapolated(double[] t, double position)
    {
        int i = System.Math.Clamp((int)System.Math.Floor(position) - 1, 0, t.Length - 2);
        return t[i] + ((position - (i + 1)) * (t[i + 1] - t[i]));
    }

    private static int Bracket(double[] t, double at)
    {
        int low = 0;
        int high = t.Length - 2;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (t[middle] <= at)
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
