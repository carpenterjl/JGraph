using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// Equiripple linear-phase FIR design by the Parks–McClellan (Remez exchange) algorithm, in
/// MATLAB's <c>firpm(n, f, a)</c> conventions: band edges normalized to Nyquist (0..1), desired
/// amplitude interpolated linearly across each band, transition bands unconstrained. Supports the
/// symmetric (Type I/II) designs used for ordinary frequency-selective filters.
/// </summary>
public static class FirDesign
{
    private const int GridDensity = 16;
    private const int MaxIterations = 40;
    private const double ConvergenceTolerance = 1e-6;

    /// <summary>
    /// Designs an order-<paramref name="order"/> (length order+1) symmetric FIR filter.
    /// <paramref name="bandEdges"/> is an even-length ascending list of normalized band edges
    /// (starting at 0, ending at 1); <paramref name="desired"/> gives the target amplitude at each
    /// edge. Returns the impulse response, exactly even-symmetric. When the exchange fails to
    /// converge the best iterate is returned and <paramref name="converged"/> reports false.
    /// </summary>
    public static double[] Remez(int order, ReadOnlySpan<double> bandEdges, ReadOnlySpan<double> desired, out bool converged)
    {
        if (order < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "The filter order must be at least 3.");
        }

        if (bandEdges.Length < 4 || bandEdges.Length % 2 != 0 || bandEdges.Length != desired.Length)
        {
            throw new ArgumentException("firpm needs matching even-length band-edge and amplitude lists (at least two bands).");
        }

        for (int i = 1; i < bandEdges.Length; i++)
        {
            if (bandEdges[i] < bandEdges[i - 1])
            {
                throw new ArgumentException("Band edges must be non-decreasing.");
            }
        }

        if (bandEdges[0] != 0 || System.Math.Abs(bandEdges[^1] - 1) > 1e-12)
        {
            throw new ArgumentException("Band edges must start at 0 and end at 1 (Nyquist).");
        }

        int taps = order + 1;
        bool typeTwo = taps % 2 == 0;           // even length: A(π) is forced to 0
        int r = typeTwo ? taps / 2 : (taps + 1) / 2; // cosine-series terms

        // Dense frequency grid over the bands (transition bands excluded), with the desired
        // amplitude and weight at each point. Type II designs on the transformed problem
        // D' = D / cos(ω/2), W' = W · cos(ω/2), keeping clear of ω = π where the factor vanishes.
        BuildGrid(bandEdges, desired, r, typeTwo, out double[] gridOmega, out double[] gridDesired, out double[] gridWeight);
        int gridSize = gridOmega.Length;

        // Initial extremals: r+1 grid points spread evenly.
        var extremals = new int[r + 1];
        for (int k = 0; k <= r; k++)
        {
            extremals[k] = (int)((long)k * (gridSize - 1) / r);
        }

        var x = new double[r + 1];      // cos(ω) at the extremals
        var gamma = new double[r + 1];  // barycentric weights
        var c = new double[r + 1];      // interpolation values
        double[] error = new double[gridSize];
        double delta = 0;
        double[]? best = null;
        double bestOvershoot = double.PositiveInfinity;
        converged = false;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            for (int k = 0; k <= r; k++)
            {
                x[k] = System.Math.Cos(gridOmega[extremals[k]]);
            }

            ComputeBarycentricWeights(x, gamma);

            // δ = Σ γk·Dk / Σ (−1)^k·γk/Wk.
            double numerator = 0;
            double denominator = 0;
            for (int k = 0; k <= r; k++)
            {
                numerator += gamma[k] * gridDesired[extremals[k]];
                denominator += (k % 2 == 0 ? 1.0 : -1.0) * gamma[k] / gridWeight[extremals[k]];
            }

            delta = numerator / denominator;

            for (int k = 0; k <= r; k++)
            {
                c[k] = gridDesired[extremals[k]] - ((k % 2 == 0 ? 1.0 : -1.0) * delta / gridWeight[extremals[k]]);
            }

            // Weighted error over the whole grid via the second barycentric form.
            double maxError = 0;
            for (int i = 0; i < gridSize; i++)
            {
                double a = Interpolate(System.Math.Cos(gridOmega[i]), x, gamma, c, extremals, i);
                error[i] = gridWeight[i] * (gridDesired[i] - a);
                maxError = System.Math.Max(maxError, System.Math.Abs(error[i]));
            }

            double overshoot = (maxError - System.Math.Abs(delta)) / System.Math.Abs(delta);
            if (overshoot < bestOvershoot)
            {
                bestOvershoot = overshoot;
                best = ToImpulseResponse(taps, typeTwo, x, gamma, c, gridOmega, extremals);
            }

            if (overshoot < ConvergenceTolerance)
            {
                converged = true;
                break;
            }

            if (!ExchangeExtremals(error, extremals))
            {
                break; // could not find r+1 alternations — keep the best iterate
            }
        }

        return best ?? throw new InvalidOperationException("Remez produced no iterate.");
    }

    private static void BuildGrid(ReadOnlySpan<double> bandEdges, ReadOnlySpan<double> desired, int r, bool typeTwo,
        out double[] gridOmega, out double[] gridDesired, out double[] gridWeight)
    {
        int bands = bandEdges.Length / 2;
        double totalWidth = 0;
        for (int b = 0; b < bands; b++)
        {
            totalWidth += bandEdges[(2 * b) + 1] - bandEdges[2 * b];
        }

        int targetPoints = System.Math.Max(GridDensity * (r + 1), 128);
        var omega = new List<double>(targetPoints + (2 * bands));
        var target = new List<double>(omega.Capacity);
        var weight = new List<double>(omega.Capacity);

        double omegaCap = typeTwo ? System.Math.PI * (1 - (1e-4 / 2)) : System.Math.PI;
        for (int b = 0; b < bands; b++)
        {
            double f1 = bandEdges[2 * b];
            double f2 = bandEdges[(2 * b) + 1];
            double a1 = desired[2 * b];
            double a2 = desired[(2 * b) + 1];
            int points = System.Math.Max(2, (int)System.Math.Round(targetPoints * (f2 - f1) / totalWidth));
            for (int i = 0; i < points; i++)
            {
                double t = (double)i / (points - 1);
                double f = f1 + ((f2 - f1) * t);
                double w = System.Math.Min(f * System.Math.PI, omegaCap);
                double d = a1 + ((a2 - a1) * t);
                double wt = 1.0;
                if (typeTwo)
                {
                    double factor = System.Math.Cos(w / 2);
                    d /= factor;
                    wt *= factor;
                }

                omega.Add(w);
                target.Add(d);
                weight.Add(wt);
            }
        }

        gridOmega = omega.ToArray();
        gridDesired = target.ToArray();
        gridWeight = weight.ToArray();
    }

    private static void ComputeBarycentricWeights(double[] x, double[] gamma)
    {
        for (int k = 0; k < x.Length; k++)
        {
            double product = 1;
            for (int j = 0; j < x.Length; j++)
            {
                if (j != k)
                {
                    product *= 2 * (x[k] - x[j]); // the 2s keep magnitudes near 1 for many nodes
                }
            }

            gamma[k] = 1.0 / product;
        }
    }

    /// <summary>Second barycentric form through the extremal nodes; exact at the nodes themselves.</summary>
    private static double Interpolate(double xi, double[] x, double[] gamma, double[] c, int[] extremals, int gridIndex)
    {
        double numerator = 0;
        double denominator = 0;
        for (int k = 0; k < x.Length; k++)
        {
            if (extremals[k] == gridIndex)
            {
                return c[k];
            }

            double difference = xi - x[k];
            if (System.Math.Abs(difference) < 1e-14)
            {
                return c[k];
            }

            double term = gamma[k] / difference;
            numerator += term * c[k];
            denominator += term;
        }

        return numerator / denominator;
    }

    /// <summary>
    /// Picks the next extremal set: local maxima of |E| with enforced sign alternation, trimmed to
    /// r+1 by dropping the smaller end candidate. Returns false when too few alternations exist.
    /// </summary>
    private static bool ExchangeExtremals(double[] error, int[] extremals)
    {
        int needed = extremals.Length;
        var candidates = new List<int>(needed * 2);

        for (int i = 0; i < error.Length; i++)
        {
            bool leftRise = i == 0 || System.Math.Abs(error[i]) >= System.Math.Abs(error[i - 1]);
            bool rightFall = i == error.Length - 1 || System.Math.Abs(error[i]) >= System.Math.Abs(error[i + 1]);
            if (leftRise && rightFall && error[i] != 0)
            {
                candidates.Add(i);
            }
        }

        // The current extremals stay candidates: their errors alternate ±δ by construction, so even
        // when δ is still tiny (early iterations) the alternation skeleton survives, and any larger
        // same-sign bump found above simply displaces its neighbor in the merge below.
        candidates.AddRange(extremals.Where(i => error[i] != 0));
        candidates.Sort();
        candidates = candidates.Distinct().ToList();

        // Merge consecutive same-sign candidates, keeping the largest of each run.
        var alternating = new List<int>(candidates.Count);
        foreach (int i in candidates)
        {
            if (alternating.Count > 0 && System.Math.Sign(error[i]) == System.Math.Sign(error[alternating[^1]]))
            {
                if (System.Math.Abs(error[i]) > System.Math.Abs(error[alternating[^1]]))
                {
                    alternating[^1] = i;
                }
            }
            else
            {
                alternating.Add(i);
            }
        }

        if (alternating.Count < needed)
        {
            return false;
        }

        // Trim to exactly r+1, dropping whichever end has the smaller error.
        while (alternating.Count > needed)
        {
            if (System.Math.Abs(error[alternating[0]]) < System.Math.Abs(error[alternating[^1]]))
            {
                alternating.RemoveAt(0);
            }
            else
            {
                alternating.RemoveAt(alternating.Count - 1);
            }
        }

        for (int k = 0; k < needed; k++)
        {
            extremals[k] = alternating[k];
        }

        return true;
    }

    /// <summary>
    /// Recovers the impulse response by frequency sampling: the designed amplitude A(ω) (with the
    /// Type II cos(ω/2) factor restored) times the linear phase e^{−jω(N−1)/2}, sampled on a
    /// power-of-two grid and inverse-transformed. Even symmetry is then enforced exactly.
    /// </summary>
    private static double[] ToImpulseResponse(int taps, bool typeTwo, double[] x, double[] gamma, double[] c,
        double[] gridOmega, int[] extremals)
    {
        int k = Fft.NextPowerOfTwo(System.Math.Max(taps * 4, 512));
        var spectrum = new Complex[k];
        double groupDelay = (taps - 1) / 2.0;
        for (int m = 0; m < k; m++)
        {
            double omega = 2 * System.Math.PI * m / k;
            double amplitude = Interpolate(System.Math.Cos(omega), x, gamma, c, extremals, gridIndex: -1);
            if (typeTwo)
            {
                amplitude *= System.Math.Cos(omega / 2); // negative past π — the correct anti-symmetry
            }

            double phase = -omega * groupDelay;
            spectrum[m] = amplitude * new Complex(System.Math.Cos(phase), System.Math.Sin(phase));
        }

        Complex[] impulse = Fft.Inverse(spectrum);
        var h = new double[taps];
        for (int n = 0; n < taps; n++)
        {
            h[n] = impulse[n].Real;
        }

        for (int n = 0; n < taps / 2; n++)
        {
            double average = (h[n] + h[taps - 1 - n]) / 2;
            h[n] = h[taps - 1 - n] = average;
        }

        return h;
    }
}
