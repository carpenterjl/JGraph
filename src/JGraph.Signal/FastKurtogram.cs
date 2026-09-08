using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>What a kurtogram found: the map, its axes, and the band that stood out.</summary>
public sealed class KurtogramAnswer
{
    /// <summary>The kurtosis of every band at every level, <c>2*level</c> rows.</summary>
    public double[,] Map { get; set; } = new double[0, 0];

    /// <summary>The centre frequency of every column.</summary>
    public double[] Frequencies { get; set; } = [];

    /// <summary>The window length of every row.</summary>
    public double[] Windows { get; set; } = [];

    /// <summary>The level of every row.</summary>
    public double[] Levels { get; set; } = [];

    /// <summary>The centre frequency of the band with the largest kurtosis.</summary>
    public double Centre { get; set; }

    /// <summary>The window length of that band.</summary>
    public double Window { get; set; }

    /// <summary>The bandwidth of that band.</summary>
    public double Bandwidth { get; set; }
}

/// <summary>
/// MATLAB's <c>kurtogram</c>: the fast kurtogram of Antoni, which splits a signal down a tree of
/// complex band-pass filters and reports the spectral kurtosis of every band it reaches.
/// </summary>
/// <remarks>
/// The tree has two kinds of branch. A binary node halves its parent's band with a pair of
/// quarter-band filters and decimates by two; a ternary node thirds it with three filters and
/// decimates by three. Interleaving them gives levels one third of an octave apart rather than a
/// whole octave, which is why the map has two rows per level.
/// </remarks>
public static class FastKurtogram
{
    /// <summary>The kurtogram of a signal, to a given level or to the deepest one it supports.</summary>
    public static KurtogramAnswer Compute(double[] x, double fs, int level)
    {
        ArgumentNullException.ThrowIfNull(x);

        int maximum = System.Math.Max(0, (int)System.Math.Floor(System.Math.Log2(x.Length) - 6));
        if (level < 0 || level > maximum)
        {
            level = maximum;
        }

        double mean = 0;
        foreach (double value in x)
        {
            mean += value;
        }

        mean /= x.Length;
        var root = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            root[i] = x[i] - mean;
        }

        (Complex[][] binary, Complex[][] ternary) = FilterBank();
        int columns = level == 0 ? 3 : 3 * (1 << level);
        var map = new double[System.Math.Max(2 * level, 1), columns];
        double rootKurtosis = Kurtosis(root) - 1;
        for (int c = 0; c < columns; c++)
        {
            map[0, c] = rootKurtosis;
        }

        double bestLevel = 0;
        double bestNumber = 1;
        double best = rootKurtosis;
        if (level > 0)
        {
            Grow(map, root, 0, 1, level, binary, ternary, ref best, ref bestLevel, ref bestNumber);
        }

        return Describe(map, level, fs, bestLevel, bestNumber);
    }

    /// <summary>One step of the tree: two halves, three thirds, and the halves again below.</summary>
    private static void Grow(
        double[,] map,
        Complex[] parent,
        double parentLevel,
        double parentNumber,
        int remaining,
        Complex[][] binary,
        Complex[][] ternary,
        ref double best,
        ref double bestLevel,
        ref double bestNumber)
    {
        int columns = map.GetLength(1);
        var children = new Complex[2][];
        for (int i = 0; i < 2; i++)
        {
            Complex[] coefficients = FilterDown(parent, binary[i], 2);
            if (i == 1)
            {
                // The upper half comes back reflected about the quarter band, so its samples
                // alternate in sign.
                for (int k = 0; k < coefficients.Length; k++)
                {
                    coefficients[k] *= (k % 2) == 0 ? -1 : 1;
                }
            }

            children[i] = coefficients;
            double childLevel = parentLevel + 1;
            double childNumber = (2 * (parentNumber - 1)) + i + 1;
            double kurtosis = Kurtosis(coefficients[System.Math.Min(binary[i].Length - 1, coefficients.Length)..]);
            int step = (int)(columns / System.Math.Pow(2, childLevel));
            for (int c = (int)((childNumber - 1) * step); c < childNumber * step; c++)
            {
                map[(int)(2 * childLevel) - 1, c] = kurtosis;
            }

            if (kurtosis > best)
            {
                best = kurtosis;
                bestLevel = childLevel;
                bestNumber = childNumber;
            }
        }

        if (remaining == 1)
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            Complex[] coefficients = FilterDown(parent, ternary[i], 3);
            double childNumber = (3 * (parentNumber - 1)) + i + 1;
            double kurtosis = Kurtosis(coefficients[System.Math.Min(ternary[i].Length - 1, coefficients.Length)..]);
            int step = (int)(columns / (3 * System.Math.Pow(2, parentLevel)));
            for (int c = (int)((childNumber - 1) * step); c < childNumber * step; c++)
            {
                map[(int)(2 * (parentLevel + 1)), c] = kurtosis;
            }

            if (kurtosis > best)
            {
                best = kurtosis;
                bestLevel = parentLevel + System.Math.Log2(3);
                bestNumber = childNumber;
            }
        }

        Grow(map, children[0], parentLevel + 1, (2 * (parentNumber - 1)) + 1, remaining - 1,
            binary, ternary, ref best, ref bestLevel, ref bestNumber);
        Grow(map, children[1], parentLevel + 1, (2 * (parentNumber - 1)) + 2, remaining - 1,
            binary, ternary, ref best, ref bestLevel, ref bestNumber);
    }

    /// <summary>
    /// The two quarter-band filters and the three sixth-band ones, each a low-pass design shifted
    /// up to the band it is meant to keep.
    /// </summary>
    private static (Complex[][] Binary, Complex[][] Ternary) FilterBank()
    {
        double[] h = FirWindowDesign.Windowed(
            16, [0.4], WindowBandType.Low, SignalWindows.Hamming(17), scale: true, hilbert: false, out _);
        double[] g = FirWindowDesign.Windowed(
            24, [2.0 / 3 * 0.4], WindowBandType.Low, SignalWindows.Hamming(25), scale: true, hilbert: false, out _);
        return (
            [Shifted(h, 1.0 / 4), Shifted(h, 3.0 / 4)],
            [Shifted(g, 1.0 / 6), Shifted(g, 3.0 / 6), Shifted(g, 5.0 / 6)]);
    }

    private static Complex[] Shifted(double[] h, double fraction)
    {
        var shifted = new Complex[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            double angle = fraction * System.Math.PI * i;
            shifted[i] = h[i] * new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
        }

        return shifted;
    }

    /// <summary>Filter, then keep every <paramref name="n"/>th sample from the <paramref name="n"/>th.</summary>
    private static Complex[] FilterDown(Complex[] x, Complex[] h, int n)
    {
        var filtered = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            Complex sum = 0;
            int taps = System.Math.Min(h.Length, i + 1);
            for (int k = 0; k < taps; k++)
            {
                sum += h[k] * x[i - k];
            }

            filtered[i] = sum;
        }

        var kept = new Complex[x.Length / n];
        for (int i = 0; i < kept.Length; i++)
        {
            kept[i] = filtered[((i + 1) * n) - 1];
        }

        return kept;
    }

    /// <summary>
    /// The kurtosis of a complex band: the fourth moment of the magnitude over the square of the
    /// second, less two — which is zero for a circular Gaussian.
    /// </summary>
    private static double Kurtosis(Complex[] x)
    {
        if (x.Length == 0)
        {
            return 0;
        }

        Complex mean = 0;
        foreach (Complex value in x)
        {
            mean += value;
        }

        mean /= x.Length;
        double second = 0;
        double fourth = 0;
        foreach (Complex value in x)
        {
            double magnitude = (value - mean).Magnitude;
            double squared = magnitude * magnitude;
            second += squared;
            fourth += squared * squared;
        }

        second /= x.Length;
        fourth /= x.Length;
        return (fourth / (second * second)) - 2;
    }

    /// <summary>MATLAB's <c>computeOutputParameters</c>: the axes and the winning band.</summary>
    private static KurtogramAnswer Describe(
        double[,] map, int level, double fs, double bestLevel, double bestNumber)
    {
        double smallest = 1.0 / (3 * System.Math.Pow(2, level)) * fs / 2;
        int columns = map.GetLength(1);
        var frequencies = new double[columns];
        for (int i = 0; i < columns; i++)
        {
            frequencies[i] = (smallest / 2) + (i * smallest);
        }

        var levels = new double[System.Math.Max(2 * level, 1)];
        var windows = new double[levels.Length];
        levels[0] = 0;
        for (int i = 1; i < levels.Length; i++)
        {
            // The rows alternate: a whole level, then that level plus a third of an octave.
            int pair = (i - 1) / 2;
            levels[i] = ((i - 1) % 2) == 0 ? pair + 1 : pair + System.Math.Log2(3);
        }

        for (int i = 0; i < levels.Length; i++)
        {
            windows[i] = System.Math.Round(System.Math.Pow(2, levels[i] + 1), MidpointRounding.AwayFromZero);
        }

        double step = 1 / System.Math.Pow(2, bestLevel) * fs / 2;
        return new KurtogramAnswer
        {
            Map = map,
            Frequencies = frequencies,
            Windows = windows,
            Levels = levels,
            Centre = (bestNumber - 0.5) * step,
            Window = System.Math.Pow(2, bestLevel + 1),
            Bandwidth = step,
        };
    }
}
