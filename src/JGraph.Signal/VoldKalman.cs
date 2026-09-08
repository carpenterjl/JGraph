using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// MATLAB's Vold–Kalman order filter: the waveforms of named orders extracted from a signal by
/// least squares rather than by filtering.
/// </summary>
/// <remarks>
/// Each order is written as a slowly varying complex envelope multiplied by a carrier whose phase
/// follows the shaft. Finding the envelopes is then a linear problem: fit the signal while
/// penalising how fast each envelope changes, with the penalty weight setting the bandwidth. Doing
/// several orders together — MATLAB calls it decoupling — lets the fit separate orders that cross,
/// which no fixed filter can. The system has one unknown per sample per order and is solved
/// without ever being assembled, by preconditioned conjugate gradients.
/// </remarks>
public static class VoldKalman
{
    /// <summary>The extracted waveforms and their complex envelopes, one column per order.</summary>
    public static (double[][] Waveforms, Complex[][] Envelopes) Extract(
        double[] x,
        double fs,
        double[][] frequencies,
        double[] weights,
        int filterOrder,
        int segment)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(weights);

        int n = x.Length;
        int orders = frequencies.Length;
        if (segment >= n)
        {
            return Solve(x, fs, frequencies, weights, filterOrder);
        }

        // A long record is fitted a window at a time and the windows are added back under a Hann
        // taper, which is cheaper than one system over the whole signal and gives the same answer
        // wherever the envelopes vary slowly compared with the window.
        int width = segment;
        if ((3 * width) + 1 > 2 * n)
        {
            width = ((2 * n) - 1) / 3;
        }

        if (width % 2 == 0)
        {
            width--;
        }

        int hop = (width + 1) / 2;
        var waveforms = new double[orders][];
        var envelopes = new Complex[orders][];
        for (int k = 0; k < orders; k++)
        {
            waveforms[k] = new double[n];
            envelopes[k] = new Complex[n];
        }

        double[] taper = SignalWindows.Hann(width);
        var starts = new List<int>();
        for (int start = 0; start + width <= n; start += hop)
        {
            starts.Add(start);
        }

        for (int s = 0; s < starts.Count; s++)
        {
            int start = starts[s];
            int length = s == starts.Count - 1 ? n - start : width;
            var piece = new double[length];
            Array.Copy(x, start, piece, 0, length);
            var pieceFrequencies = new double[orders][];
            for (int k = 0; k < orders; k++)
            {
                pieceFrequencies[k] = new double[length];
                Array.Copy(frequencies[k], start, pieceFrequencies[k], 0, length);
            }

            (double[][] partial, Complex[][] partialEnvelope) =
                Solve(piece, fs, pieceFrequencies, weights, filterOrder);
            for (int k = 0; k < orders; k++)
            {
                for (int i = 0; i < length; i++)
                {
                    double weight = s == 0 && i < hop ? 1
                        : s == starts.Count - 1 ? (i < hop ? taper[i] : 1)
                        : taper[System.Math.Min(i, width - 1)];
                    waveforms[k][start + i] += partial[k][i] * weight;
                    envelopes[k][start + i] += partialEnvelope[k][i].Magnitude * weight;
                }
            }
        }

        return (waveforms, envelopes);
    }

    /// <summary>One system, solved whole.</summary>
    private static (double[][] Waveforms, Complex[][] Envelopes) Solve(
        double[] x, double fs, double[][] frequencies, double[] weights, int filterOrder)
    {
        int n = x.Length;
        int orders = frequencies.Length;
        var carrier = new Complex[orders][];
        for (int k = 0; k < orders; k++)
        {
            carrier[k] = new Complex[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                phase += frequencies[k][i];
                double angle = 2 * System.Math.PI * phase / fs;
                carrier[k][i] = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }
        }

        var b = new Complex[orders * n];
        for (int k = 0; k < orders; k++)
        {
            for (int i = 0; i < n; i++)
            {
                b[(k * n) + i] = Complex.Conjugate(carrier[k][i]) * x[i];
            }
        }

        double mean = 0;
        foreach (double value in x)
        {
            mean += value;
        }

        mean /= n;
        double variance = 0;
        foreach (double value in x)
        {
            variance += (value - mean) * (value - mean);
        }

        double deviation = n > 1 ? System.Math.Sqrt(variance / (n - 1)) : 0;
        var start = new Complex[orders * n];
        Array.Fill(start, new Complex(deviation / System.Math.Sqrt(2), 0));

        Complex[] solution = ConjugateGradients(
            v => Apply(v, carrier, weights, filterOrder, n, orders),
            v => Precondition(v, filterOrder),
            b,
            start,
            1e-3,
            50000);

        var waveforms = new double[orders][];
        var envelopes = new Complex[orders][];
        for (int k = 0; k < orders; k++)
        {
            waveforms[k] = new double[n];
            envelopes[k] = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                Complex envelope = 2 * solution[(k * n) + i];
                envelopes[k][i] = envelope;
                waveforms[k][i] = (envelope * carrier[k][i]).Real;
            }
        }

        return (waveforms, envelopes);
    }

    /// <summary>
    /// The system's action on a vector, never assembled: a smoothness penalty down each order's
    /// own block, an identity, and one term per pair of orders that couples them.
    /// </summary>
    private static Complex[] Apply(
        Complex[] v, Complex[][] carrier, double[] weights, int filterOrder, int n, int orders)
    {
        var result = new Complex[v.Length];
        for (int k = 0; k < orders; k++)
        {
            Complex[] block = Smoothness(v, k * n, n, filterOrder);
            double weight = weights[k] * weights[k];
            for (int i = 0; i < n; i++)
            {
                result[(k * n) + i] = (weight * block[i]) + v[(k * n) + i];
            }
        }

        for (int i = 0; i < orders; i++)
        {
            for (int j = i + 1; j < orders; j++)
            {
                for (int s = 0; s < n; s++)
                {
                    Complex coupling = Complex.Conjugate(carrier[i][s]) * carrier[j][s];
                    result[(i * n) + s] += coupling * v[(j * n) + s];
                    result[(j * n) + s] += Complex.Conjugate(coupling) * v[(i * n) + s];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// <c>S'S·v</c> for the difference operator of the chosen order, applied as two passes of a
    /// short convolution rather than as a matrix.
    /// </summary>
    private static Complex[] Smoothness(Complex[] v, int offset, int n, int filterOrder)
    {
        double[] taps = filterOrder == 1 ? [1, -2, 1] : [1, -3, 3, -1];
        int rows = n - taps.Length + 1;
        var forward = new Complex[System.Math.Max(rows, 0)];
        for (int r = 0; r < rows; r++)
        {
            Complex sum = 0;
            for (int t = 0; t < taps.Length; t++)
            {
                sum += taps[t] * v[offset + r + t];
            }

            forward[r] = sum;
        }

        var back = new Complex[n];
        for (int r = 0; r < rows; r++)
        {
            for (int t = 0; t < taps.Length; t++)
            {
                back[r + t] += taps[t] * forward[r];
            }
        }

        return back;
    }

    /// <summary>
    /// MATLAB's preconditioner: two or three passes of a forward cumulative sum followed by a
    /// reverse one, which is a cheap stand-in for inverting the smoothness penalty.
    /// </summary>
    private static Complex[] Precondition(Complex[] v, int filterOrder)
    {
        Complex[] work = (Complex[])v.Clone();
        for (int pass = 0; pass < filterOrder + 1; pass++)
        {
            for (int i = 1; i < work.Length; i++)
            {
                work[i] += work[i - 1];
            }

            for (int i = work.Length - 2; i >= 0; i--)
            {
                work[i] += work[i + 1];
            }
        }

        return work;
    }

    /// <summary>Preconditioned conjugate gradients over a complex Hermitian system.</summary>
    private static Complex[] ConjugateGradients(
        Func<Complex[], Complex[]> apply,
        Func<Complex[], Complex[]> precondition,
        Complex[] b,
        Complex[] start,
        double tolerance,
        int most)
    {
        int n = b.Length;
        var x = (Complex[])start.Clone();
        Complex[] product = apply(x);
        var r = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            r[i] = b[i] - product[i];
        }

        double target = tolerance * Norm(b);
        Complex[] p = [];
        Complex previous = 0;
        for (int step = 0; step < most; step++)
        {
            if (Norm(r) <= target)
            {
                break;
            }

            Complex[] y = precondition(r);
            Complex rho = 0;
            for (int i = 0; i < n; i++)
            {
                rho += Complex.Conjugate(r[i]) * y[i];
            }

            if (step == 0)
            {
                p = y;
            }
            else
            {
                Complex beta = rho / previous;
                for (int i = 0; i < n; i++)
                {
                    p[i] = y[i] + (beta * p[i]);
                }
            }

            Complex[] q = apply(p);
            Complex denominator = 0;
            for (int i = 0; i < n; i++)
            {
                denominator += Complex.Conjugate(p[i]) * q[i];
            }

            if (denominator == Complex.Zero)
            {
                break;
            }

            Complex alpha = rho / denominator;
            for (int i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * q[i];
            }

            previous = rho;
        }

        return x;
    }

    private static double Norm(Complex[] v)
    {
        double sum = 0;
        foreach (Complex value in v)
        {
            sum += (value.Real * value.Real) + (value.Imaginary * value.Imaginary);
        }

        return System.Math.Sqrt(sum);
    }
}
