using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>What a synchrosqueezed transform gives back.</summary>
public sealed class SqueezedAnswer
{
    /// <summary>The transform, indexed frame then frequency.</summary>
    public Complex[][] Values { get; set; } = [];

    /// <summary>The frequency of every row.</summary>
    public double[] Frequencies { get; set; } = [];

    /// <summary>The time of every frame — one per sample of the signal.</summary>
    public double[] Times { get; set; } = [];
}

/// <summary>
/// The Fourier synchrosqueezed transform and the ridges read off it: MATLAB's <c>fsst</c>,
/// <c>ifsst</c> and <c>tfridge</c>.
/// </summary>
/// <remarks>
/// The synchrosqueezed transform is a short-time transform whose estimates have been moved along
/// the frequency axis to their centre of gravity and left where they were in time. That single
/// difference from <c>spectrogram</c>'s reassignment — time is not touched — is what makes it
/// invertible: every column still holds all of one instant's energy, so summing a column, or a
/// band of one, gives the signal back.
/// </remarks>
public static class Synchrosqueezing
{
    /// <summary>MATLAB's <c>fsst</c>.</summary>
    /// <remarks>
    /// The window slides one sample at a time and the signal is padded so that the first and last
    /// frames are centred on the first and last samples; there is therefore one column per sample.
    /// </remarks>
    public static SqueezedAnswer Squeeze(Complex[] x, double[] window, double fs, bool normalised)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(window);

        int n = x.Length;
        int nwin = window.Length;
        int nfft = nwin;
        bool odd = (nwin % 2) != 0;
        int front = odd ? (nwin - 1) / 2 : nwin / 2;
        int back = odd ? (nwin - 1) / 2 : (nwin - 2) / 2;
        var padded = new Complex[front + n + back];
        Array.Copy(x, 0, padded, front, n);

        var times = new double[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = normalised ? i + 1 : i / fs;
        }

        (Complex[][] frames, _) = ShortTimeTransforms.Columns(padded, nwin, nwin - 1, fs);
        double[] grid = SpectralEstimation.FrequencyGrid(nfft, fs);
        double[] derivative = SpectralEstimation.WindowDerivative(window, fs);
        int m = nwin / 2;

        var values = new Complex[frames.Length][];
        var centres = new double[frames.Length][];
        for (int c = 0; c < frames.Length; c++)
        {
            Complex[] y = Windowed(frames[c], window, nfft);
            Complex[] yc = Windowed(frames[c], derivative, nfft);
            var row = new double[nfft];
            for (int i = 0; i < nfft; i++)
            {
                double shift = -(yc[i] / y[i]).Imaginary;
                row[i] = grid[i] + (double.IsFinite(shift) ? shift : 0);

                // The linear phase ramp puts the transform's reference back at the frame's centre,
                // which is where the estimate is about to be moved to. MATLAB writes it as
                // `exp(-1i*2*pi*m*inds/nfft)'` — and that trailing quote is a conjugate transpose,
                // so the ramp actually runs the other way. An even-length window hides the
                // difference, because m is then half the transform and the ramp is plus or minus
                // one whichever way it turns; an odd-length one does not.
                double angle = 2 * System.Math.PI * m * i / nfft;
                y[i] *= new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            values[c] = y;
            centres[c] = row;
        }

        Complex[][] moved = MoveInFrequency(values, grid, centres);
        bool real = IsReal(x);
        if (real)
        {
            int keep = SpectralEstimation.OneSidedLength(nfft);
            for (int c = 0; c < moved.Length; c++)
            {
                moved[c] = moved[c][..keep];
            }

            return new SqueezedAnswer { Values = moved, Frequencies = grid[..keep], Times = times };
        }

        for (int c = 0; c < moved.Length; c++)
        {
            moved[c] = ShortTimeTransforms.CentreRows(moved[c]);
        }

        return new SqueezedAnswer
        {
            Values = moved,
            Frequencies = ShortTimeTransforms.CentreFrequencies(grid),
            Times = times,
        };
    }

    /// <summary>
    /// MATLAB's <c>ifsst</c>: the sum of a column, or of the rows a mask keeps, scaled by the
    /// window's middle sample and the frequency step.
    /// </summary>
    /// <remarks>
    /// <paramref name="keep"/> null takes the whole column. A real signal's transform holds only
    /// one side, so the negative frequencies are added back as the conjugate of rows two upward.
    /// </remarks>
    public static Complex[] Invert(
        Complex[][] sst,
        double[] window,
        bool real,
        int nfft,
        Func<int, int, bool>? keep)
    {
        ArgumentNullException.ThrowIfNull(sst);
        ArgumentNullException.ThrowIfNull(window);

        double g0 = window[(int)System.Math.Round(window.Length / 2.0, MidpointRounding.AwayFromZero) - 1];
        double scale = 1.0 / nfft / g0;
        int rows = sst.Length == 0 ? 0 : sst[0].Length;
        int last = (nfft % 2) != 0 ? rows : rows - 1;
        var x = new Complex[sst.Length];
        for (int c = 0; c < sst.Length; c++)
        {
            Complex whole = 0;
            Complex mirrored = 0;
            for (int i = 0; i < rows; i++)
            {
                if (keep is not null && !keep(i, c))
                {
                    continue;
                }

                whole += sst[c][i];
                if (real && i >= 1 && i < last)
                {
                    mirrored += sst[c][i];
                }
            }

            x[c] = real ? scale * (whole + Complex.Conjugate(mirrored)).Real : scale * whole;
        }

        return x;
    }

    /// <summary>
    /// MATLAB's <c>tfridge</c>: the paths of least negative-log energy through a time–frequency
    /// map, one at a time, each blanked out before the next is looked for.
    /// </summary>
    public static int[][] Ridges(Complex[][] tfm, double penalty, int count, int bins)
    {
        ArgumentNullException.ThrowIfNull(tfm);
        int columns = tfm.Length;
        int rows = columns == 0 ? 0 : tfm[0].Length;
        double total = 0;
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                total += tfm[c][i].Magnitude;
            }
        }

        var energy = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            energy[c] = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                energy[c][i] = -System.Math.Log(tfm[c][i].Magnitude + (1e-8 * total))
                    + System.Math.Log(total);
            }
        }

        // A ridge already taken is buried under the least energy a double can carry.
        double blank = -System.Math.Log(2.2250738585072014e-308);
        var ridges = new int[count][];
        for (int r = 0; r < count; r++)
        {
            ridges[r] = Curve(energy, penalty);
            if (r == count - 1)
            {
                continue;
            }

            for (int c = 0; c < columns; c++)
            {
                for (int d = 0; d <= bins; d++)
                {
                    int lower = System.Math.Max(0, ridges[r][c] - d);
                    int upper = System.Math.Min(rows - 1, ridges[r][c] + d);
                    energy[c][lower] = blank;
                    if (d > 0)
                    {
                        energy[c][upper] = blank;
                    }
                }
            }
        }

        return ridges;
    }

    /// <summary>
    /// MATLAB's <c>extractCurve</c>: one pass of dynamic programming forward and a traceback, with
    /// a move from bin <c>k</c> to bin <c>j</c> costing the penalty times the square of the step.
    /// </summary>
    private static int[] Curve(double[][] energy, double penalty)
    {
        int columns = energy.Length;
        int rows = columns == 0 ? 0 : energy[0].Length;
        var cost = new double[columns][];
        var back = new int[columns][];
        for (int c = 0; c < columns; c++)
        {
            cost[c] = new double[rows];
            back[c] = new int[rows];
        }

        for (int i = 0; i < rows; i++)
        {
            cost[0][i] = energy[0][i];
            back[0][i] = i;
        }

        for (int c = 1; c < columns; c++)
        {
            for (int j = 0; j < rows; j++)
            {
                double best = double.PositiveInfinity;
                int at = 0;
                for (int k = 0; k < rows; k++)
                {
                    double step = j - k;
                    double candidate = cost[c - 1][k] + (penalty * step * step);
                    if (best > candidate)
                    {
                        best = candidate;
                        at = k;
                    }
                }

                cost[c][j] = best + energy[c][j];
                back[c][j] = at;
            }
        }

        var curve = new int[columns];
        if (columns == 0)
        {
            return curve;
        }

        double least = cost[columns - 1][0];
        curve[columns - 1] = 0;
        if (columns > 1)
        {
            curve[columns - 2] = back[columns - 1][0];
        }

        for (int j = 1; j < rows; j++)
        {
            if (cost[columns - 1][j] < least)
            {
                least = cost[columns - 1][j];
                curve[columns - 1] = j;
                if (columns > 1)
                {
                    curve[columns - 2] = back[columns - 1][j];
                }
            }
        }

        for (int c = columns - 3; c >= 0; c--)
        {
            curve[c] = back[c + 1][curve[c + 1]];
        }

        return curve;
    }

    /// <summary>
    /// The squeeze itself: every estimate is added into the row nearest its centre of gravity,
    /// cyclically, and none of them changes column.
    /// </summary>
    private static Complex[][] MoveInFrequency(Complex[][] values, double[] grid, double[][] centres)
    {
        int n = grid.Length;
        double first = grid[0];
        double span = grid[n - 1] - first;
        var moved = new Complex[values.Length][];
        for (int c = 0; c < values.Length; c++)
        {
            moved[c] = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double position = span == 0 ? 0 : (centres[c][i] - first) * (n - 1) / span;
                int row = (int)System.Math.Round(position, MidpointRounding.AwayFromZero) % n;
                if (row < 0)
                {
                    row += n;
                }

                moved[c][row] += values[c][i];
            }
        }

        return moved;
    }

    private static Complex[] Windowed(Complex[] frame, double[] window, int nfft)
    {
        var windowed = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            windowed[i] = frame[i] * window[i];
        }

        return SpectralEstimation.Transform(windowed, nfft);
    }

    private static bool IsReal(Complex[] x)
    {
        foreach (Complex value in x)
        {
            if (value.Imaginary != 0)
            {
                return false;
            }
        }

        return true;
    }
}
