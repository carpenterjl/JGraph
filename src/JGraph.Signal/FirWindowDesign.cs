using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>Which symmetry a least-squares FIR design is asked for.</summary>
public enum LinearPhaseType
{
    /// <summary>An even-symmetric response: types I and II, the ordinary frequency-selective filters.</summary>
    Symmetric,

    /// <summary>An odd-symmetric response: types III and IV, a Hilbert transformer.</summary>
    Hilbert,

    /// <summary>Odd-symmetric with the bands weighted by one over frequency squared: a differentiator.</summary>
    Differentiator,
}

/// <summary>Which bands a window-method design passes (MATLAB's <c>fir1</c> type strings).</summary>
public enum WindowBandType
{
    Low,
    High,
    BandPass,
    Stop,
    DcZero,
    DcOne,
}

/// <summary>
/// The FIR designs that are not an exchange algorithm: least squares, the window method, frequency
/// sampling, the raised cosines, the Gaussians, and the two order estimates (M134).
/// </summary>
/// <remarks>
/// <para>
/// <c>firls</c> is the root of most of this file. It minimises the integrated squared error against
/// a piecewise-linear response, and it does it in closed form: every entry of the normal equations
/// is an integral of a product of two cosines over a band, which is a pair of sinc functions. When
/// the bands tile the whole axis and the weights are equal, the normal matrix is the identity and
/// there is nothing to solve — MATLAB tests for exactly that and takes the cheap path, and the two
/// paths do not agree to the last bit, so the test has to be copied rather than skipped.
/// </para>
/// <para>
/// <c>fir1</c> is <c>firls</c> against an ideal brick wall, tapered by a window and scaled at one
/// frequency. <c>intfilt</c> is <c>firls</c> against a Nyquist band. <c>fir2</c> and
/// <c>yulewalk</c> instead sample the response on a fine grid and transform it back, which is why
/// both take a grid size and an interpolation width and neither is a least-squares design at all.
/// </para>
/// </remarks>
public static class FirWindowDesign
{
    /// <summary>
    /// <c>firls</c>: the taps minimising the squared error against a piecewise-linear response.
    /// </summary>
    /// <param name="order">The design's order; the answer has one more tap.</param>
    /// <param name="frequencies">Band edges in pairs, from zero to one.</param>
    /// <param name="amplitudes">The desired response at each band edge.</param>
    /// <param name="weights">One weight per band, or empty for equal weights.</param>
    /// <param name="type">Which symmetry to design for.</param>
    /// <param name="orderRaised">Set when an even-length design was asked to pass Nyquist and grew by one.</param>
    public static double[] LeastSquares(
        int order,
        ReadOnlySpan<double> frequencies,
        ReadOnlySpan<double> amplitudes,
        ReadOnlySpan<double> weights,
        LinearPhaseType type,
        out bool orderRaised)
    {
        if (frequencies.Length % 2 != 0)
        {
            throw new ArgumentException("firls needs an even number of band edges.", nameof(frequencies));
        }

        if (frequencies.Length != amplitudes.Length)
        {
            throw new ArgumentException("firls needs one amplitude per band edge.", nameof(amplitudes));
        }

        foreach (double edge in frequencies)
        {
            if (edge < 0 || edge > 1)
            {
                throw new ArgumentException("firls band edges must lie between 0 and 1.", nameof(frequencies));
            }
        }

        int bands = frequencies.Length / 2;
        if (weights.Length != 0 && weights.Length != bands)
        {
            throw new ArgumentException("firls needs one weight per band.", nameof(weights));
        }

        bool odd = type is LinearPhaseType.Symmetric;
        order = CheckOrder(order, frequencies[^1], amplitudes, exception: !odd, out orderRaised);

        int taps = order + 1;
        var f = new double[frequencies.Length];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = frequencies[i] / 2;
        }

        var wt = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            wt[i] = System.Math.Sqrt(System.Math.Abs(weights.Length == 0 ? 1 : weights[i]));
        }

        bool fullband = frequencies.Length - 1 > 1;
        if (fullband)
        {
            for (int i = 1; i < frequencies.Length - 1; i += 2)
            {
                if (f[i + 1] - f[i] != 0)
                {
                    fullband = false;
                    break;
                }
            }
        }

        bool constantWeights = true;
        for (int i = 0; i < bands; i++)
        {
            if (wt[i] != wt[0])
            {
                constantWeights = false;
                break;
            }
        }

        double half = (taps - 1) / 2.0;
        int l = (int)half;
        bool tapsOdd = taps % 2 == 1;

        return type == LinearPhaseType.Symmetric
            ? EvenSymmetry(taps, l, tapsOdd, f, amplitudes, wt, fullband, constantWeights)
            : OddSymmetry(taps, l, tapsOdd, f, amplitudes, wt, fullband, constantWeights,
                type == LinearPhaseType.Differentiator);
    }

    /// <summary>Types I and II: the basis is the cosines, and index zero has no <c>1/k</c> in it.</summary>
    private static double[] EvenSymmetry(
        int taps, int l, bool tapsOdd, double[] f, ReadOnlySpan<double> a, double[] wt,
        bool fullband, bool constantWeights)
    {
        int count = tapsOdd ? l + 1 : taps / 2;
        var m = new double[count];
        for (int i = 0; i < count; i++)
        {
            m[i] = tapsOdd ? i : i + 0.5;
        }

        bool needMatrix = !fullband || !constantWeights;
        double[,]? g = needMatrix ? new double[count, count] : null;

        int start = tapsOdd ? 1 : 0;
        var b = new double[count - start];
        double b0 = 0;

        for (int s = 0; s < f.Length; s += 2)
        {
            double slope = (a[s + 1] - a[s]) / (f[s + 1] - f[s]);
            double intercept = a[s] - (slope * f[s]);
            double weight = wt[s / 2] * wt[s / 2];

            if (tapsOdd)
            {
                b0 += ((intercept * (f[s + 1] - f[s]))
                    + (slope / 2 * ((f[s + 1] * f[s + 1]) - (f[s] * f[s])))) * weight;
            }

            for (int i = start; i < count; i++)
            {
                double k = m[i];
                double value = slope / (4 * System.Math.PI * System.Math.PI)
                    * (System.Math.Cos(2 * System.Math.PI * k * f[s + 1]) - System.Math.Cos(2 * System.Math.PI * k * f[s]))
                    / (k * k);
                value += (f[s + 1] * ((slope * f[s + 1]) + intercept) * Sinc(2 * k * f[s + 1]))
                    - (f[s] * ((slope * f[s]) + intercept) * Sinc(2 * k * f[s]));
                b[i - start] += value * weight;
            }

            if (g is not null)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < count; j++)
                    {
                        double sum = m[i] + m[j];
                        double difference = m[i] - m[j];
                        g[i, j] += ((0.5 * f[s + 1] * (Sinc(2 * sum * f[s + 1]) + Sinc(2 * difference * f[s + 1])))
                            - (0.5 * f[s] * (Sinc(2 * sum * f[s]) + Sinc(2 * difference * f[s])))) * weight;
                    }
                }
            }
        }

        var rhs = new double[count];
        if (tapsOdd)
        {
            rhs[0] = b0;
            Array.Copy(b, 0, rhs, 1, b.Length);
        }
        else
        {
            Array.Copy(b, rhs, b.Length);
        }

        double[] solved;
        if (g is not null)
        {
            solved = SolveSquare(g, rhs);
        }
        else
        {
            solved = new double[count];
            for (int i = 0; i < count; i++)
            {
                solved[i] = wt[0] * wt[0] * 4 * rhs[i];
            }

            if (tapsOdd)
            {
                solved[0] /= 2;
            }
        }

        var h = new double[taps];
        if (tapsOdd)
        {
            for (int i = 0; i < l; i++)
            {
                h[i] = solved[l - i] / 2;
            }

            h[l] = solved[0];
            for (int i = 1; i <= l; i++)
            {
                h[l + i] = solved[i] / 2;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                h[i] = 0.5 * solved[count - 1 - i];
                h[count + i] = 0.5 * solved[i];
            }
        }

        return h;
    }

    /// <summary>
    /// Types III and IV: the basis is the sines, and a differentiator's bands carry an extra
    /// <c>1/f²</c> weight, whose integrals are sine integrals rather than sincs.
    /// </summary>
    private static double[] OddSymmetry(
        int taps, int l, bool tapsOdd, double[] f, ReadOnlySpan<double> a, double[] wt,
        bool fullband, bool constantWeights, bool differentiator)
    {
        int bands = f.Length / 2;
        var doWeight = new bool[bands];
        bool anyWeighted = false;
        if (differentiator)
        {
            for (int i = 0; i < bands; i++)
            {
                doWeight[i] = System.Math.Abs(a[2 * i]) + System.Math.Abs(a[(2 * i) + 1]) > 0;
                anyWeighted |= doWeight[i];
            }
        }

        int count = tapsOdd ? l : (taps / 2);
        var m = new double[count];
        for (int i = 0; i < count; i++)
        {
            m[i] = tapsOdd ? i + 1 : i + 0.5;
        }

        bool needMatrix = !fullband || anyWeighted || !constantWeights;
        double[,]? g = needMatrix ? new double[count, count] : null;
        var b = new double[count];

        for (int s = 0; s < f.Length; s += 2)
        {
            double weight = wt[s / 2] * wt[s / 2];

            if (doWeight[s / 2])
            {
                if (f[s] == 0)
                {
                    f[s] = 1e-5; // the 1/f² weight has a pole at the origin, so the band starts past it
                }

                double slope = (a[s + 1] - a[s]) / (f[s + 1] - f[s]);
                double intercept = a[s] - (slope * f[s]);

                for (int i = 0; i < count; i++)
                {
                    double k = m[i];
                    double sine = SineIntegral(2 * System.Math.PI * k * f[s + 1])
                        - SineIntegral(2 * System.Math.PI * k * f[s]);
                    double cosine = CosineIntegralPair(2 * System.Math.PI * k * f[s + 1])
                        - CosineIntegralPair(2 * System.Math.PI * k * f[s]);
                    b[i] += ((slope * sine)
                        + (intercept * 2 * System.Math.PI * k
                            * (-Sinc(2 * k * f[s + 1]) + Sinc(2 * k * f[s]) + cosine))) * weight;
                }

                if (g is not null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            double i1 = m[i] + m[j];
                            double i2 = m[i] - m[j];
                            g[i, j] -= (WeightedEntry(f[s + 1], i1, i2) - WeightedEntry(f[s], i1, i2)) * weight;
                        }
                    }
                }
            }
            else
            {
                double slope = (a[s + 1] - a[s]) / (f[s + 1] - f[s]);
                double intercept = a[s] - (slope * f[s]);

                for (int i = 0; i < count; i++)
                {
                    double k = m[i];
                    double value = slope / (4 * System.Math.PI * System.Math.PI)
                        * (System.Math.Sin(2 * System.Math.PI * k * f[s + 1]) - System.Math.Sin(2 * System.Math.PI * k * f[s]))
                        / (k * k);
                    value += (((slope * f[s]) + intercept) * System.Math.Cos(2 * System.Math.PI * k * f[s])
                        - (((slope * f[s + 1]) + intercept) * System.Math.Cos(2 * System.Math.PI * k * f[s + 1])))
                        / (2 * System.Math.PI * k);
                    b[i] += value * weight;
                }

                if (g is not null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            double sum = m[i] + m[j];
                            double difference = m[i] - m[j];
                            g[i, j] += ((0.5 * f[s + 1] * (Sinc(2 * sum * f[s + 1]) - Sinc(2 * difference * f[s + 1])))
                                - (0.5 * f[s] * (Sinc(2 * sum * f[s]) - Sinc(2 * difference * f[s])))) * weight;
                        }
                    }
                }
            }
        }

        double[] solved;
        if (g is not null)
        {
            solved = SolveSquare(g, b);
        }
        else
        {
            solved = new double[count];
            for (int i = 0; i < count; i++)
            {
                solved[i] = -4 * b[i] * wt[0] * wt[0];
            }
        }

        var h = new double[taps];
        int at = 0;
        for (int i = count - 1; i >= 0; i--)
        {
            h[at++] = 0.5 * solved[i];
        }

        if (tapsOdd)
        {
            h[at++] = 0;
        }

        for (int i = 0; i < count; i++)
        {
            h[at++] = -0.5 * solved[i];
        }

        if (differentiator)
        {
            for (int i = 0; i < h.Length; i++)
            {
                h[i] = -h[i];
            }
        }

        return h;
    }

    /// <summary>One entry of the differentiator's normal matrix, which is a pair of sine integrals.</summary>
    private static double WeightedEntry(double f, double i1, double i2)
    {
        double first = -0.5 * ((System.Math.Cos(2 * System.Math.PI * f * -i2) / f)
            - (2 * SineIntegral(2 * System.Math.PI * f * -i2) * System.Math.PI * i2)
            - (System.Math.Cos(2 * System.Math.PI * f * i1) / f)
            - (2 * SineIntegral(2 * System.Math.PI * f * i1) * System.Math.PI * i1));
        return first;
    }

    /// <summary>The sine integral Si(x), by way of the exponential integral.</summary>
    private static double SineIntegral(double x)
    {
        if (x == 0)
        {
            return 0;
        }

        if (x < 0)
        {
            return -SineIntegral(-x);
        }

        Complex[] e = ExponentialIntegral.E1([new Complex(0, x)]);
        return e[0].Imaginary + (System.Math.PI / 2);
    }

    /// <summary>The symmetric combination of exponential integrals the differentiator's bias needs.</summary>
    private static double CosineIntegralPair(double x)
    {
        if (x == 0)
        {
            return 0;
        }

        Complex[] e = ExponentialIntegral.E1([new Complex(0, x), new Complex(0, -x)]);
        return -0.5 * (e[0] + e[1]).Real;
    }

    // --- The window method ---------------------------------------------------------------------

    /// <summary>
    /// <c>fir1</c>: the least-squares design of an ideal brick wall, tapered by a window and scaled
    /// so that one point of the first passband has unit gain.
    /// </summary>
    public static double[] Windowed(
        int order,
        ReadOnlySpan<double> cutoffs,
        WindowBandType? band,
        ReadOnlySpan<double> window,
        bool scale,
        bool hilbert,
        out bool orderRaised)
    {
        foreach (double w in cutoffs)
        {
            if (w <= 0 || w >= 1)
            {
                throw new ArgumentException(
                    "fir1 cutoff frequencies must lie strictly between 0 and 1.", nameof(cutoffs));
            }
        }

        for (int i = 1; i < cutoffs.Length; i++)
        {
            if (cutoffs[i] < cutoffs[i - 1])
            {
                throw new ArgumentException("fir1 cutoff frequencies must increase.", nameof(cutoffs));
            }
        }

        WindowBandType type = band ?? cutoffs.Length switch
        {
            1 => WindowBandType.Low,
            2 => WindowBandType.BandPass,
            _ => WindowBandType.DcZero,
        };

        int bands = cutoffs.Length + 1;
        if (bands > 2 && type == WindowBandType.BandPass)
        {
            type = WindowBandType.DcZero;
        }

        var freq = new double[(2 * cutoffs.Length) + 2];
        freq[0] = 0;
        for (int i = 0; i < cutoffs.Length; i++)
        {
            freq[(2 * i) + 1] = cutoffs[i];
            freq[(2 * i) + 2] = cutoffs[i];
        }

        freq[^1] = 1;

        bool firstBand = type is not (WindowBandType.DcZero or WindowBandType.High);
        var magnitude = new double[2 * bands];
        for (int i = 0; i < bands; i++)
        {
            double value = ((firstBand ? 1 : 0) + i) % 2;
            magnitude[2 * i] = value;
            magnitude[(2 * i) + 1] = value;
        }

        order = CheckOrder(order, freq[^1], magnitude, hilbert, out orderRaised);
        int taps = order + 1;

        double[] taper;
        if (window.Length > 0)
        {
            if (window.Length != taps)
            {
                throw new ArgumentException(
                    "fir1's window must be as long as the filter it tapers.", nameof(window));
            }

            taper = window.ToArray();
        }
        else
        {
            taper = SignalWindows.Hamming(taps);
        }

        double[] h = LeastSquares(
            taps - 1, freq, magnitude, [],
            hilbert ? LinearPhaseType.Hilbert : LinearPhaseType.Symmetric, out _);

        for (int i = 0; i < taps; i++)
        {
            h[i] *= taper[i];
        }

        if (!scale)
        {
            return h;
        }

        if (firstBand)
        {
            double sum = 0;
            foreach (double value in h)
            {
                sum += value;
            }

            for (int i = 0; i < taps; i++)
            {
                h[i] /= sum;
            }

            return h;
        }

        // The scaling point is the middle of the first passband, or Nyquist when the passband
        // reaches it — a highpass has no gain at zero to normalise against.
        double f0 = freq[3] == 1 ? 1 : (freq[2] + freq[3]) / 2;
        Complex response = Complex.Zero;
        for (int i = 0; i < taps; i++)
        {
            response += h[i] * Complex.Exp(new Complex(0, -2 * System.Math.PI * i * f0 / 2));
        }

        double magnitudeAt = Complex.Abs(response);
        for (int i = 0; i < taps; i++)
        {
            h[i] /= magnitudeAt;
        }

        return h;
    }

    // --- Frequency sampling --------------------------------------------------------------------

    /// <summary>
    /// <c>fir2</c>: the response drawn on a fine grid, transformed back, and windowed. A repeated
    /// frequency in the specification is a step, and the grid spreads it over
    /// <paramref name="lap"/> points rather than letting it be a discontinuity.
    /// </summary>
    public static double[] FrequencySampled(
        int order, ReadOnlySpan<double> f, ReadOnlySpan<double> a, int points, int lap,
        ReadOnlySpan<double> window, out bool orderRaised)
    {
        order = CheckOrder(order, f[^1], a, exception: false, out orderRaised);
        int taps = order + 1;

        if (f.Length != a.Length)
        {
            throw new ArgumentException("fir2 needs one amplitude per frequency.", nameof(a));
        }

        if (System.Math.Abs(f[0]) > 2.220446049250313e-16 || System.Math.Abs(f[^1] - 1) > 2.220446049250313e-16)
        {
            throw new ArgumentException("fir2's frequency vector must run from 0 to 1.", nameof(f));
        }

        double[] taper = window.Length > 0 ? window.ToArray() : SignalWindows.Hamming(taps);
        if (taper.Length != taps)
        {
            throw new ArgumentException("fir2's window must be as long as the filter.", nameof(window));
        }

        if (taps > 2 * points)
        {
            throw new ArgumentException(
                $"fir2 needs at least {(int)System.Math.Ceiling(taps / 2.0)} grid points for an order of {order}.",
                nameof(points));
        }

        double[] grid = SampleResponse(f, a, points, lap, out int gridPoints);

        // The design's own delay is applied as a linear phase before the transform back, which is
        // what makes the answer causal and symmetric rather than centred on zero.
        double delay = 0.5 * (taps - 1);
        var full = new Complex[2 * (gridPoints - 1)];
        for (int i = 0; i < gridPoints; i++)
        {
            double phase = -delay * System.Math.PI * i / (gridPoints - 1);
            full[i] = grid[i] * Complex.Exp(new Complex(0, phase));
        }

        for (int i = gridPoints - 2; i >= 1; i--)
        {
            full[(2 * (gridPoints - 1)) - i] = Complex.Conjugate(full[i]);
        }

        Complex[] time = Fft.Inverse(full);
        var h = new double[taps];
        for (int i = 0; i < taps; i++)
        {
            h[i] = time[i].Real * taper[i];
        }

        return h;
    }

    /// <summary>
    /// The piecewise-linear response <c>fir2</c> and <c>yulewalk</c> both draw before transforming,
    /// on a grid of <c>points + 1</c> samples from zero to Nyquist.
    /// </summary>
    private static double[] SampleResponse(
        ReadOnlySpan<double> f, ReadOnlySpan<double> a, int points, int lap, out int gridPoints)
    {
        gridPoints = points + 1;
        var h = new double[gridPoints];
        h[0] = a[0];

        int nb = 1;
        for (int i = 0; i < f.Length - 1; i++)
        {
            int ne;
            if (f[i + 1] - f[i] == 0)
            {
                nb = (int)System.Math.Ceiling(nb - (lap / 2.0));
                ne = nb + lap;
            }
            else
            {
                ne = (int)(f[i + 1] * gridPoints);
            }

            if (nb < 0 || ne > gridPoints)
            {
                throw new ArgumentException("The frequency specification does not fit the grid.", nameof(f));
            }

            for (int j = nb; j <= ne; j++)
            {
                double increment = ne == nb ? 0 : (double)(j - nb) / (ne - nb);
                h[j - 1] = (increment * a[i + 1]) + ((1 - increment) * a[i]);
            }

            nb = ne + 1;
        }

        return h;
    }

    /// <summary>
    /// <c>yulewalk</c>: a recursive filter fitted to a sampled magnitude response, by solving the
    /// modified Yule–Walker equations for the denominator and a spectral factorisation for the
    /// numerator.
    /// </summary>
    /// <remarks>
    /// The denominator comes from the autocorrelation of the response, tapered by a Hamming window
    /// over four times the order; the numerator comes from the cepstrum of the resulting spectrum,
    /// which is what turns a magnitude with no phase into a minimum-phase numerator.
    /// </remarks>
    public static (double[] B, double[] A) YuleWalk(
        int order, ReadOnlySpan<double> f, ReadOnlySpan<double> m, int points, int lap)
    {
        if (f.Length != m.Length)
        {
            throw new ArgumentException("yulewalk needs one amplitude per frequency.", nameof(m));
        }

        if (System.Math.Abs(f[0]) > 2.220446049250313e-16 || System.Math.Abs(f[^1] - 1) > 2.220446049250313e-16)
        {
            throw new ArgumentException("yulewalk's frequency vector must run from 0 to 1.", nameof(f));
        }

        double[] half = SampleResponse(f, m, points, lap, out int gridPoints);
        int n = (2 * gridPoints) - 2;
        var full = new Complex[n];
        for (int i = 0; i < gridPoints; i++)
        {
            full[i] = half[i];
        }

        for (int i = 1; i <= gridPoints - 2; i++)
        {
            full[n - i] = half[i];
        }

        int middle = (n + 1) / 2;
        int correlations = 4 * order;

        var squared = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            squared[i] = full[i] * full[i];
        }

        Complex[] autocorrelation = Fft.Inverse(squared);
        var r = new double[correlations];
        for (int i = 0; i < correlations; i++)
        {
            r[i] = autocorrelation[i].Real
                * (0.54 + (0.46 * System.Math.Cos(System.Math.PI * i / (correlations - 1))));
        }

        double[] a = FilterCoefficients.PolyStabilise(Denominator(r, order), out _);

        var shifted = new double[correlations];
        shifted[0] = r[0] / 2;
        Array.Copy(r, 1, shifted, 1, correlations - 1);
        double[] qh = Numerator(shifted, a, order);

        // The additive decomposition's spectrum, then its cepstrum, then the minimum-phase part.
        var spectrum = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            double omega = 2 * System.Math.PI * i / n;
            Complex z = Complex.Exp(new Complex(0, -omega));
            Complex num = Complex.Zero;
            foreach (double c in qh)
            {
                num = (num * z) + c;
            }

            Complex den = Complex.Zero;
            foreach (double c in a)
            {
                den = (den * z) + c;
            }

            spectrum[i] = 2 * (num / den).Real;
        }

        var logged = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            logged[i] = Complex.Log(spectrum[i]);
        }

        Complex[] cepstrum = Fft.Inverse(logged);
        for (int i = 0; i < n; i++)
        {
            double weight = i == 0 ? 0.5 : i < middle ? 1 : 0;
            cepstrum[i] *= weight;
        }

        Complex[] folded = Fft.Forward(cepstrum);
        for (int i = 0; i < n; i++)
        {
            folded[i] = Complex.Exp(folded[i]);
        }

        Complex[] impulse = Fft.Inverse(folded);
        var head = new double[correlations];
        for (int i = 0; i < correlations; i++)
        {
            head[i] = impulse[i].Real;
        }

        return (Numerator(head, a, order), a);
    }

    /// <summary>The modified Yule–Walker denominator: a Toeplitz least-squares fit to the tail.</summary>
    private static double[] Denominator(double[] r, int order)
    {
        int nr = r.Length;
        int rows = nr - 1 - order;
        var matrix = new double[rows * order];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < order; j++)
            {
                // Column j of a Toeplitz matrix whose first column is R(na+1..nr-1) and whose first
                // row is R(na+1:-1:2), transposed for the right division.
                int index = order + i - j;
                matrix[(j * rows) + i] = r[index];
            }
        }

        var rhs = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            rhs[i] = -r[order + 1 + i];
        }

        double[] solved = Linear.Solve(matrix, rows, order, rhs, 1);
        var a = new double[order + 1];
        a[0] = 1;
        Array.Copy(solved, 0, a, 1, order);
        return a;
    }

    /// <summary>The numerator that best matches an impulse response through a known denominator.</summary>
    private static double[] Numerator(double[] h, double[] a, int order)
    {
        int nh = h.Length;
        var impulse = new double[nh];
        impulse[0] = 1;
        double[] response = DigitalFilter.Filter([1], a, impulse);

        var matrix = new double[nh * (order + 1)];
        for (int j = 0; j <= order; j++)
        {
            for (int i = 0; i < nh; i++)
            {
                matrix[(j * nh) + i] = i >= j ? response[i - j] : 0;
            }
        }

        double[] rhs = (double[])h.Clone();
        return Linear.Solve(matrix, nh, order + 1, rhs, 1);
    }

    // --- Interpolation filters -----------------------------------------------------------------

    /// <summary>
    /// <c>intfilt</c>: the filter that fills in <paramref name="rate"/> − 1 samples between every
    /// pair, either as a bandlimited least-squares design or as a Lagrange interpolator.
    /// </summary>
    public static double[] Interpolation(int rate, int span, double bandwidth, bool bandlimited)
    {
        if (rate < 1 || span < 0)
        {
            throw new ArgumentException("intfilt needs a positive rate and a nonnegative span.", nameof(rate));
        }

        if (bandlimited)
        {
            int taps = (2 * rate * span) - 1;
            double[] f;
            double[] m;
            if (bandwidth == 1)
            {
                m = [rate, rate, 0, 0];
                f = [0, 1.0 / (2 * rate), 1.0 / (2 * rate), 0.5];
            }
            else
            {
                var edges = new List<double> { 0, bandwidth / 2 / rate };
                var mags = new List<double> { rate, rate };
                for (double band = 1.0 / rate; band <= 0.5 + 1e-12; band += 1.0 / rate)
                {
                    edges.Add(band - (bandwidth / 2 / rate));
                    edges.Add(band + (bandwidth / 2 / rate));
                    mags.Add(0);
                    mags.Add(0);
                }

                if (edges[^1] > 0.5)
                {
                    edges[^1] = 0.5;
                }

                f = [.. edges];
                m = [.. mags];
            }

            var doubled = new double[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                doubled[i] = f[i] * 2;
            }

            return LeastSquares(taps - 1, doubled, m, [], LinearPhaseType.Symmetric, out _);
        }

        if (span == 0)
        {
            var flat = new double[rate];
            Array.Fill(flat, 1);
            return flat;
        }

        // The Lagrange form: one polynomial through span + 1 points, sampled at every offset the
        // rate change asks for.
        int n = span;
        int length = (n * rate) + 2;
        var basis = new double[n + 1, length];
        for (int i = 0; i <= n; i++)
        {
            for (int t = 0; t < length; t++)
            {
                basis[i, t] = 1;
            }

            for (int j = 0; j <= n; j++)
            {
                if (j == i)
                {
                    continue;
                }

                for (int t = 0; t < length; t++)
                {
                    basis[i, t] *= ((double)t / rate - j) / (i - j);
                }
            }
        }

        var h = new double[(n + 1) * rate];
        for (int i = 0; i < rate; i++)
        {
            for (int j = 0; j <= n; j++)
            {
                int at = (int)System.Math.Round((n - 1) / 2.0 * rate) + i;
                h[(j * rate) + i] = basis[n - j, at];
            }
        }

        return h[0] == 0 ? h[1..] : h;
    }

    // --- The raised cosines and the Gaussians --------------------------------------------------

    /// <summary>
    /// <c>rcosdesign</c>: the pulse whose spectrum rolls off as a raised cosine, or its square root
    /// — the shape a matched pair of transmitter and receiver each use.
    /// </summary>
    public static double[] RaisedCosine(double beta, double span, int samplesPerSymbol, bool squareRoot)
    {
        if (beta < 0 || beta > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(beta), "rcosdesign's roll-off must lie between 0 and 1.");
        }

        if (beta == 0)
        {
            beta = double.Epsilon;
        }

        double exact = samplesPerSymbol * span;
        if (System.Math.Abs(exact - System.Math.Round(exact)) > 1e-9)
        {
            throw new ArgumentException(
                "rcosdesign needs span times the sample rate to be a whole number.", nameof(span));
        }

        int filterOrder = (int)System.Math.Round(exact);
        double delay = filterOrder / 2.0;
        var b = new double[filterOrder + 1];
        double root = System.Math.Sqrt(2.220446049250313e-16);

        for (int i = 0; i <= filterOrder; i++)
        {
            double t = (i - delay) / samplesPerSymbol;
            if (!squareRoot)
            {
                double denominator = 1 - System.Math.Pow(2 * beta * t, 2);
                b[i] = System.Math.Abs(denominator) > root
                    ? Sinc(t) * System.Math.Cos(System.Math.PI * beta * t) / denominator / samplesPerSymbol
                    : beta * System.Math.Sin(System.Math.PI / (2 * beta)) / (2 * samplesPerSymbol);
                continue;
            }

            if (t == 0)
            {
                b[i] = -1 / (System.Math.PI * samplesPerSymbol) * ((System.Math.PI * (beta - 1)) - (4 * beta));
            }
            else if (System.Math.Abs(System.Math.Abs(4 * beta * t) - 1) < root)
            {
                b[i] = 1 / (2 * System.Math.PI * samplesPerSymbol)
                    * ((System.Math.PI * (beta + 1) * System.Math.Sin(System.Math.PI * (beta + 1) / (4 * beta)))
                        - (4 * beta * System.Math.Sin(System.Math.PI * (beta - 1) / (4 * beta)))
                        + (System.Math.PI * (beta - 1) * System.Math.Cos(System.Math.PI * (beta - 1) / (4 * beta))));
            }
            else
            {
                b[i] = -4 * beta / samplesPerSymbol
                    * (System.Math.Cos((1 + beta) * System.Math.PI * t)
                        + (System.Math.Sin((1 - beta) * System.Math.PI * t) / (4 * beta * t)))
                    / (System.Math.PI * (System.Math.Pow(4 * beta * t, 2) - 1));
            }
        }

        double energy = 0;
        foreach (double value in b)
        {
            energy += value * value;
        }

        double norm = System.Math.Sqrt(energy);
        for (int i = 0; i < b.Length; i++)
        {
            b[i] /= norm;
        }

        return b;
    }

    /// <summary>The Gaussian pulse both <c>gaussdesign</c> and <c>gaussfir</c> answer, over a given span.</summary>
    public static double[] Gaussian(double bandwidthTime, double halfSpan, int length)
    {
        double alpha = System.Math.Sqrt(System.Math.Log(2) / 2) / bandwidthTime;
        var h = new double[length];
        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            double t = length == 1 ? 0 : -halfSpan + (2 * halfSpan * i / (length - 1.0));
            h[i] = System.Math.Sqrt(System.Math.PI) / alpha
                * System.Math.Exp(-System.Math.Pow(t * System.Math.PI / alpha, 2));
            sum += h[i];
        }

        for (int i = 0; i < length; i++)
        {
            h[i] /= sum;
        }

        return h;
    }

    /// <summary>
    /// <c>firgauss</c>: a moving average applied <paramref name="cascades"/> times, which is a
    /// Gaussian by the central limit theorem and costs nothing but additions.
    /// </summary>
    public static double[] GaussianCascade(int cascades, int length)
    {
        if (cascades < 1 || length < 1)
        {
            throw new ArgumentException("firgauss needs a positive cascade count and length.", nameof(cascades));
        }

        var h = new double[length];
        Array.Fill(h, 1);
        double[] result = h;
        for (int i = 1; i < cascades; i++)
        {
            result = FilterCoefficients.Convolve(result, h);
        }

        return result;
    }

    /// <summary>The length <c>firgauss</c> picks when it is asked for the smallest one for a variance.</summary>
    public static int GaussianCascadeLength(int cascades, double variance)
    {
        double sigma = System.Math.Sqrt(variance);
        if (cascades < 4)
        {
            return (int)System.Math.Round(System.Math.Sqrt((12 * variance / cascades) + 1));
        }

        double alpha = sigma >= 2 && sigma <= 400 && cascades <= 8 ? 0.005 : 0;
        double sum = 0;
        for (int p = 0; p <= (cascades - 1) / 2; p++)
        {
            sum += -System.Math.Pow(1, p) / Factorial(cascades - 1)
                * Choose(cascades, p) * System.Math.Pow((cascades / 2.0) - p, cascades - 1);
        }

        return (int)System.Math.Abs(System.Math.Ceiling(((System.Math.Sqrt(2 * System.Math.PI) * sum) + alpha) * sigma));
    }

    /// <summary>
    /// <c>firrcos</c>: the older raised-cosine design, which takes a cutoff and a roll-off in
    /// absolute frequency rather than a symbol rate.
    /// </summary>
    public static double[] RaisedCosineLegacy(
        int order, double cutoff, double rolloff, double sampleRate, double delay, bool squareRoot)
    {
        int taps = order + 1;
        if (rolloff == 0)
        {
            rolloff = double.Epsilon;
        }

        var b = new double[taps];
        double root = System.Math.Sqrt(2.220446049250313e-16);
        for (int i = 0; i < taps; i++)
        {
            double n = (i - delay) / sampleRate;
            if (!squareRoot)
            {
                b[i] = System.Math.Abs(System.Math.Abs(4 * rolloff * cutoff * n) - 1) > root
                    ? Sinc(2 * cutoff * n) / sampleRate
                        * System.Math.Cos(2 * System.Math.PI * rolloff * cutoff * n)
                        / (1 - System.Math.Pow(4 * rolloff * cutoff * n, 2))
                    : rolloff / (2 * sampleRate) * System.Math.Sin(System.Math.PI / (2 * rolloff));
                b[i] *= 2 * cutoff;
                continue;
            }

            if (n == 0)
            {
                b[i] = -System.Math.Sqrt(2 * cutoff) / (System.Math.PI * sampleRate)
                    * ((System.Math.PI * (rolloff - 1)) - (4 * rolloff));
            }
            else if (System.Math.Abs(System.Math.Abs(8 * rolloff * cutoff * n) - 1) < root)
            {
                b[i] = System.Math.Sqrt(2 * cutoff) / (2 * System.Math.PI * sampleRate)
                    * ((System.Math.PI * (rolloff + 1) * System.Math.Sin(System.Math.PI * (rolloff + 1) / (4 * rolloff)))
                        - (4 * rolloff * System.Math.Sin(System.Math.PI * (rolloff - 1) / (4 * rolloff)))
                        + (System.Math.PI * (rolloff - 1) * System.Math.Cos(System.Math.PI * (rolloff - 1) / (4 * rolloff))));
            }
            else
            {
                b[i] = -4 * rolloff / sampleRate
                    * (System.Math.Cos((1 + rolloff) * 2 * System.Math.PI * cutoff * n)
                        + (System.Math.Sin((1 - rolloff) * 2 * System.Math.PI * cutoff * n)
                            / (8 * rolloff * cutoff * n)))
                    / (System.Math.PI * System.Math.Sqrt(1 / (2 * cutoff))
                        * (System.Math.Pow(8 * rolloff * cutoff * n, 2) - 1));
            }

            b[i] *= squareRoot ? 1 : 1;
        }

        if (squareRoot)
        {
            for (int i = 0; i < taps; i++)
            {
                b[i] *= System.Math.Sqrt(2 * cutoff);
            }
        }

        return b;
    }

    // --- Order estimates -----------------------------------------------------------------------

    /// <summary>What a Kaiser-window order estimate answers.</summary>
    public readonly record struct KaiserEstimate(int Order, double[] Cutoffs, double Beta, WindowBandType Type);

    /// <summary>
    /// <c>kaiserord</c>: the order a Kaiser-windowed design needs to hold the given deviations, from
    /// Kaiser's own empirical formula.
    /// </summary>
    public static KaiserEstimate KaiserOrder(
        ReadOnlySpan<double> edges, ReadOnlySpan<double> magnitudes, ReadOnlySpan<double> deviations, double sampleRate)
    {
        int bands = magnitudes.Length;
        if (edges.Length != 2 * (bands - 1))
        {
            throw new ArgumentException(
                "kaiserord needs two band edges per transition, so f must be two shorter than twice a.", nameof(edges));
        }

        if (deviations.Length != bands)
        {
            throw new ArgumentException("kaiserord needs one deviation per band.", nameof(deviations));
        }

        var cuts = new double[edges.Length];
        for (int i = 0; i < edges.Length; i++)
        {
            cuts[i] = edges[i] / sampleRate;
        }

        var relative = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            relative[i] = deviations[i] / (magnitudes[i] == 0 ? 1 : magnitudes[i]);
        }

        int transitions = edges.Length / 2;
        var f1 = new double[transitions];
        var f2 = new double[transitions];
        int narrowest = 0;
        for (int i = 0; i < transitions; i++)
        {
            f1[i] = cuts[2 * i];
            f2[i] = cuts[(2 * i) + 1];
            if (f2[i] - f1[i] < f2[narrowest] - f1[narrowest])
            {
                narrowest = i;
            }
        }

        double length = 0;
        double beta = 0;
        if (bands == 2)
        {
            (length, beta) = KaiserLowpassOrder(f1[narrowest], f2[narrowest], relative[0], relative[1]);
        }
        else
        {
            for (int i = 1; i < bands - 1; i++)
            {
                (double l1, double b1) = KaiserLowpassOrder(f1[i - 1], f2[i - 1], relative[i], relative[i - 1]);
                (double l2, double b2) = KaiserLowpassOrder(f1[i], f2[i], relative[i], relative[i + 1]);
                if (l1 > length)
                {
                    length = l1;
                    beta = b1;
                }

                if (l2 > length)
                {
                    length = l2;
                    beta = b2;
                }
            }
        }

        int order = (int)System.Math.Ceiling(length) - 1;
        var wn = new double[transitions];
        for (int i = 0; i < transitions; i++)
        {
            wn[i] = 2 * (f1[i] + f2[i]) / 2;
        }

        WindowBandType type = WindowBandType.Low;
        if (bands == 2 && magnitudes[0] == 0)
        {
            type = WindowBandType.High;
        }
        else if (bands == 3 && magnitudes[1] == 0)
        {
            type = WindowBandType.Stop;
        }
        else if (bands >= 3 && magnitudes[0] == 0)
        {
            type = WindowBandType.DcZero;
        }
        else if (bands >= 3 && magnitudes[0] == 1)
        {
            type = WindowBandType.DcOne;
        }

        if (order % 2 == 1 && magnitudes[^1] != 0)
        {
            order++;
        }

        return new KaiserEstimate(order, wn, beta, type);
    }

    /// <summary>Kaiser's length and shape for one transition band.</summary>
    private static (double Length, double Beta) KaiserLowpassOrder(double f1, double f2, double d1, double d2)
    {
        double delta = System.Math.Min(d1, d2);
        double attenuation = -20 * System.Math.Log10(delta);
        double d = (attenuation - 7.95) / (2 * System.Math.PI * 2.285);
        double width = System.Math.Abs(f2 - f1);
        return ((d / width) + 1, KaiserBeta(attenuation));
    }

    /// <summary>The Kaiser window shape a given stopband attenuation asks for.</summary>
    public static double KaiserBeta(double attenuation)
    {
        if (attenuation > 50)
        {
            return 0.1102 * (attenuation - 8.7);
        }

        return attenuation >= 21
            ? (0.5842 * System.Math.Pow(attenuation - 21, 0.4)) + (0.07886 * (attenuation - 21))
            : 0;
    }

    /// <summary>What an equiripple order estimate answers.</summary>
    public readonly record struct RemezEstimate(int Order, double[] Frequencies, double[] Amplitudes, double[] Weights);

    /// <summary>
    /// <c>firpmord</c>: the order an equiripple design needs, from Herrmann's empirical fit rather
    /// than from a design — which is why the estimate is only an estimate and can be one short.
    /// </summary>
    public static RemezEstimate RemezOrder(
        ReadOnlySpan<double> edges, ReadOnlySpan<double> magnitudes, ReadOnlySpan<double> deviations, double sampleRate)
    {
        int bands = magnitudes.Length;
        if (magnitudes.Length != deviations.Length)
        {
            throw new ArgumentException("firpmord needs one deviation per band.", nameof(deviations));
        }

        if (edges.Length != 2 * (bands - 1))
        {
            throw new ArgumentException("firpmord needs f to be two shorter than twice a.", nameof(edges));
        }

        var cuts = new double[edges.Length];
        for (int i = 0; i < edges.Length; i++)
        {
            cuts[i] = edges[i] / sampleRate;
            if (cuts[i] > 0.5)
            {
                throw new ArgumentException("firpmord's band edges must not exceed half the sample rate.", nameof(edges));
            }

            if (cuts[i] < 0)
            {
                throw new ArgumentException("firpmord's band edges must be positive.", nameof(edges));
            }
        }

        var relative = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            relative[i] = deviations[i] / (magnitudes[i] == 0 ? 1 : magnitudes[i]);
        }

        int transitions = edges.Length / 2;
        var f1 = new double[transitions];
        var f2 = new double[transitions];
        int narrowest = 0;
        for (int i = 0; i < transitions; i++)
        {
            f1[i] = cuts[2 * i];
            f2[i] = cuts[(2 * i) + 1];
            if (f2[i] - f1[i] < f2[narrowest] - f1[narrowest])
            {
                narrowest = i;
            }
        }

        double length;
        if (bands == 2)
        {
            length = RemezLowpassOrder(f1[narrowest], f2[narrowest], relative[0], relative[1]);
        }
        else
        {
            length = 0;
            for (int i = 1; i < bands - 1; i++)
            {
                length = System.Math.Max(length, RemezLowpassOrder(f1[i - 1], f2[i - 1], relative[i], relative[i - 1]));
                length = System.Math.Max(length, RemezLowpassOrder(f1[i], f2[i], relative[i], relative[i + 1]));
            }
        }

        int order = (int)System.Math.Ceiling(length) - 1;

        var ff = new double[edges.Length + 2];
        ff[0] = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            ff[i + 1] = 2 * cuts[i];
        }

        ff[^1] = 1;

        var aa = new double[2 * bands];
        for (int i = 0; i < bands; i++)
        {
            aa[2 * i] = magnitudes[i];
            aa[(2 * i) + 1] = magnitudes[i];
        }

        double worst = 0;
        foreach (double d in relative)
        {
            worst = System.Math.Max(worst, d);
        }

        var wts = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            wts[i] = worst / relative[i];
        }

        if (aa[^1] != 0 && order % 2 == 1)
        {
            order++;
        }

        return new RemezEstimate(order, ff, aa, wts);
    }

    /// <summary>Herrmann's empirical length for one transition band.</summary>
    private static double RemezLowpassOrder(double f1, double f2, double d1, double d2)
    {
        double[,] coefficients =
        {
            { -4.278e-01, -4.761e-01, 0 },
            { -5.941e-01, 7.114e-02, 0 },
            { -2.660e-03, 5.309e-03, 0 },
        };

        double a = System.Math.Log10(d1);
        double b = System.Math.Log10(d2);
        double[] left = [1, a, a * a];
        double[] right = [1, b, b * b];

        double d = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                d += left[i] * coefficients[i, j] * right[j];
            }
        }

        double correction = 11.01217 + (0.51244 * (a - b));
        double width = System.Math.Abs(f2 - f1);
        return (d / width) - (correction * width) + 1;
    }

    // --- Small shared pieces -------------------------------------------------------------------

    /// <summary>
    /// <c>firchk</c>: an even-length filter cannot pass Nyquist, so a design that asks for one grows
    /// by an order — which is a warning rather than an error, and one of the few places MATLAB
    /// silently changes what it was asked for.
    /// </summary>
    internal static int CheckOrder(int order, double lastEdge, ReadOnlySpan<double> amplitudes, bool exception, out bool raised)
    {
        raised = false;
        if (order <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "The filter order must be a positive whole number.");
        }

        if (amplitudes.Length > 0 && amplitudes[^1] != 0 && lastEdge == 1 && order % 2 == 1 && !exception)
        {
            raised = true;
            return order + 1;
        }

        return order;
    }

    /// <summary>The normalised sinc, which is one at zero rather than undefined.</summary>
    internal static double Sinc(double x) =>
        x == 0 ? 1 : System.Math.Sin(System.Math.PI * x) / (System.Math.PI * x);

    private static double[] SolveSquare(double[,] g, double[] rhs)
    {
        int n = rhs.Length;
        var column = new double[n, 1];
        for (int i = 0; i < n; i++)
        {
            column[i, 0] = rhs[i];
        }

        double[,] solved = Linear.Solve(g, column);
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = solved[i, 0];
        }

        return result;
    }

    private static double Factorial(int n)
    {
        double result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }

        return result;
    }

    private static double Choose(int n, int k)
    {
        double result = 1;
        for (int i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return System.Math.Round(result);
    }
}
