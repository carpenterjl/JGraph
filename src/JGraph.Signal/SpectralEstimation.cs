using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Signal;

/// <summary>Which part of the Nyquist interval an estimate is reported over.</summary>
public enum SpectralRange
{
    /// <summary>[0, Fs/2] for a real signal, with the interior bins doubled.</summary>
    OneSided,

    /// <summary>[0, Fs), the whole grid the transform produces.</summary>
    TwoSided,
}

/// <summary>How a window's gain is divided out of an estimate.</summary>
public enum SpectralScaling
{
    /// <summary>Power per hertz: the window's squared norm comes out, and the result is divided by Fs.</summary>
    Psd,

    /// <summary>Power: the square of the window's sum comes out, so a sinusoid's peak reads its own power.</summary>
    Power,
}

/// <summary>
/// The arithmetic every spectral estimator in the toolbox shares: the frequency grid, the windowed
/// transform, the periodogram, the one-sided fold, the average over segments, and the chi-squared
/// confidence interval.
/// </summary>
/// <remarks>
/// This is a transcription of MATLAB's <c>psdfreqvec</c>, <c>computeDFT</c>,
/// <c>computeperiodogram</c>, <c>computepsd</c>, <c>psdcenterdc</c>, <c>confInterval</c> and
/// <c>welch</c>, kept in that shape because every name in the family is one of them with different
/// arguments. A signal is a list of channels, each a column; a two-signal estimate pairs the
/// channels of one with the channels of the other.
/// </remarks>
public static class SpectralEstimation
{
    /// <summary>The confidence level a caller gets when it asks for an interval without naming one.</summary>
    public const double DefaultConfidence = 0.95;

    /// <summary>
    /// MATLAB's <c>psdfreqvec('npts', n, 'Fs', fs)</c>: the whole grid, spaced <c>fs/n</c>, with
    /// the points either side of Nyquist written exactly rather than accumulated.
    /// </summary>
    public static double[] FrequencyGrid(int npts, double fs)
    {
        var w = new double[npts];
        double resolution = fs / npts;
        for (int i = 0; i < npts; i++)
        {
            w[i] = resolution * i;
        }

        double nyquist = fs / 2;
        double halfResolution = resolution / 2;
        bool odd = (npts % 2) != 0;
        int half = odd ? (npts + 1) / 2 : (npts / 2) + 1;
        if (odd)
        {
            if (half <= npts)
            {
                w[half - 1] = nyquist - halfResolution;
            }

            if (half < npts)
            {
                w[half] = nyquist + halfResolution;
            }
        }
        else if (half <= npts)
        {
            w[half - 1] = nyquist;
        }

        w[npts - 1] = fs - resolution;
        return w;
    }

    /// <summary>The number of points a one-sided estimate keeps out of <paramref name="nfft"/>.</summary>
    public static int OneSidedLength(int nfft) => (nfft % 2) != 0 ? (nfft + 1) / 2 : (nfft / 2) + 1;

    /// <summary>
    /// The transform of one already-windowed column at <paramref name="nfft"/> points, wrapping the
    /// column first when it is longer than the transform (MATLAB's <c>computeDFT</c>).
    /// </summary>
    public static Complex[] Transform(ReadOnlySpan<Complex> column, int nfft)
    {
        var padded = new Complex[nfft];
        if (column.Length > nfft)
        {
            for (int i = 0; i < column.Length; i++)
            {
                padded[i % nfft] += column[i];
            }
        }
        else
        {
            for (int i = 0; i < column.Length; i++)
            {
                padded[i] = column[i];
            }
        }

        Fft.Transform(padded, inverse: false);
        return padded;
    }

    /// <summary>
    /// The transform of one already-windowed column at the frequencies in <paramref name="f"/>,
    /// which MATLAB reaches by the chirp-z transform when they are evenly spaced and by Goertzel
    /// when they are not.
    /// </summary>
    public static Complex[] Transform(ReadOnlySpan<Complex> column, double[] f, double fs)
    {
        int m = f.Length;
        if (m == 0)
        {
            return [];
        }

        (double first, double last, double error) = UniformApproximation(f);
        bool uniform = error < 3 * 2.220446049250313e-16;
        bool large = m > 80 * System.Math.Log2(Fft.NextPowerOfTwo(m + column.Length - 1));
        if (uniform && large && m > 1)
        {
            Complex start = Complex.Exp(new Complex(0, 2 * System.Math.PI * first / fs));
            Complex step = Complex.Exp(new Complex(0, 2 * System.Math.PI * (first - last) / ((m - 1) * fs)));
            return SignalTransforms.ChirpZ(column, m, step, start);
        }

        var indices = new double[m];
        for (int i = 0; i < m; i++)
        {
            double wrapped = f[i] % fs;
            if (wrapped < 0)
            {
                wrapped += fs;
            }

            indices[i] = wrapped / fs * column.Length;
        }

        return SignalTransforms.Goertzel(column, indices);
    }

    /// <summary>
    /// The endpoints of the best uniform grid through <paramref name="f"/> and the largest relative
    /// departure from it (MATLAB's <c>getUniformApprox</c>).
    /// </summary>
    private static (double First, double Last, double Error) UniformApproximation(double[] f)
    {
        int m = f.Length;
        if (m < 2)
        {
            return (m == 0 ? 0 : f[0], m == 0 ? 0 : f[0], 0);
        }

        double first = f[0];
        double last = f[m - 1];
        double step = (last - first) / (m - 1);
        double largest = 0;
        double deviation = 0;
        for (int i = 0; i < m; i++)
        {
            double expected = first + (i * step);
            deviation = System.Math.Max(deviation, System.Math.Abs(f[i] - expected));
            largest = System.Math.Max(largest, System.Math.Abs(f[i]));
        }

        return (first, last, largest == 0 ? 0 : deviation / largest);
    }

    /// <summary>The derivative of a window with respect to time, MATLAB's <c>dtwin</c>.</summary>
    public static double[] WindowDerivative(double[] window, double fs)
    {
        int n = window.Length;
        var index = new double[n];
        for (int i = 0; i < n; i++)
        {
            index[i] = i + 1;
        }

        double[] slopes = Interpolation.SplineSlopes(index, window);
        var derivative = new double[n];
        double scale = fs / (2 * System.Math.PI);
        for (int i = 0; i < n; i++)
        {
            derivative[i] = slopes[i] * scale;
        }

        return derivative;
    }

    /// <summary>The window normalisation a scaling asks for: MATLAB's <c>U</c>.</summary>
    /// <remarks>
    /// A power spectrum divides by the square of the window's sum, because the window is convolved
    /// with every peak and the peak is meant to keep its height. Reassignment is the exception: it
    /// spreads the estimate rather than leaving it under the peak, so the normalisation there is the
    /// window's energy times the number of points. <paramref name="reassignPoints"/> is zero when
    /// reassignment is off.
    /// </remarks>
    public static double WindowNormalisation(double[] window, SpectralScaling scaling, int reassignPoints = 0)
    {
        double energy = 0;
        double sum = 0;
        foreach (double w in window)
        {
            energy += w * w;
            sum += w;
        }

        if (scaling != SpectralScaling.Power)
        {
            return energy;
        }

        return reassignPoints > 0 ? reassignPoints * energy : sum * sum;
    }

    /// <summary>One channel's raw periodogram over the whole Nyquist interval, before any folding.</summary>
    /// <remarks>
    /// <paramref name="y"/> null makes this the auto spectrum; a second channel makes it the cross
    /// spectrum <c>Xx·conj(Yy)/U</c>, which is what every two-signal estimator averages.
    /// </remarks>
    public static Complex[] Periodogram(
        ReadOnlySpan<Complex> x,
        ReadOnlySpan<Complex> y,
        double[] window,
        int nfft,
        double[]? frequencies,
        double fs,
        SpectralScaling scaling,
        int reassignPoints = 0)
    {
        var xw = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            xw[i] = x[i] * window[i];
        }

        Complex[] xx = frequencies is null ? Transform(xw, nfft) : Transform(xw, frequencies, fs);
        double u = WindowNormalisation(window, scaling, reassignPoints);
        var result = new Complex[xx.Length];
        if (y.Length == 0)
        {
            for (int i = 0; i < xx.Length; i++)
            {
                result[i] = new Complex((xx[i] * Complex.Conjugate(xx[i])).Real / u, 0);
            }

            return result;
        }

        var yw = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            yw[i] = y[i] * window[i];
        }

        Complex[] yy = frequencies is null ? Transform(yw, nfft) : Transform(yw, frequencies, fs);
        for (int i = 0; i < xx.Length; i++)
        {
            result[i] = xx[i] * Complex.Conjugate(yy[i]) / u;
        }

        return result;
    }

    /// <summary>
    /// The centre-of-gravity frequency of every bin of one column, which reassignment moves each
    /// estimate to. Returns null when the window's transform vanishes there.
    /// </summary>
    public static double[] CentreFrequencies(
        ReadOnlySpan<Complex> x,
        double[] window,
        int nfft,
        double[] grid,
        double fs)
    {
        var xw = new Complex[window.Length];
        var xtw = new Complex[window.Length];
        double[] derivative = WindowDerivative(window, fs);
        for (int i = 0; i < window.Length; i++)
        {
            xw[i] = x[i] * window[i];
            xtw[i] = x[i] * derivative[i];
        }

        Complex[] xx = Transform(xw, nfft);
        Complex[] xc = Transform(xtw, nfft);
        var centres = new double[xx.Length];
        for (int i = 0; i < xx.Length; i++)
        {
            Complex ratio = xc[i] / xx[i];
            double shift = -ratio.Imaginary;
            centres[i] = grid[i] + (double.IsFinite(shift) ? shift : 0);
        }

        return centres;
    }

    /// <summary>
    /// MATLAB's <c>computepsd</c>: fold a two-sided estimate onto one side when asked, then divide
    /// by the sample rate unless the caller wanted power rather than density.
    /// </summary>
    public static (Complex[] Values, double[] Frequencies) ToPsd(
        Complex[] sxx,
        double[] w,
        SpectralRange range,
        bool nfftIsScalar,
        double fs,
        SpectralScaling scaling)
    {
        Complex[] values;
        double[] frequencies;
        if (range == SpectralRange.OneSided && nfftIsScalar)
        {
            int nfft = sxx.Length;
            int keep = OneSidedLength(nfft);
            values = new Complex[keep];
            frequencies = new double[keep];
            bool odd = (nfft % 2) != 0;
            for (int i = 0; i < keep; i++)
            {
                bool doubled = i > 0 && (odd || i < keep - 1);
                values[i] = doubled ? 2 * sxx[i] : sxx[i];
                frequencies[i] = w[i];
            }
        }
        else
        {
            values = (Complex[])sxx.Clone();
            frequencies = (double[])w.Clone();
        }

        if (scaling == SpectralScaling.Power)
        {
            return (values, frequencies);
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] /= fs;
        }

        return (values, frequencies);
    }

    /// <summary>
    /// MATLAB's <c>psdcenterdc</c>: rotate an estimate so that zero frequency sits in the middle,
    /// undoing the one-sided doubling on the way when the estimate carries it.
    /// </summary>
    public static (Complex[] Values, double[] Frequencies) CenterDc(
        Complex[] values,
        double[] frequencies,
        SpectralRange range,
        int nfft,
        double fs,
        bool scaled)
    {
        int n = frequencies.Length;
        if (n == 0)
        {
            return (values, frequencies);
        }

        bool even = (nfft % 2) == 0;
        var work = (Complex[])values.Clone();
        int[] index;
        if (range == SpectralRange.OneSided)
        {
            if (scaled)
            {
                int last = even ? n - 1 : n;
                for (int i = 1; i < last; i++)
                {
                    work[i] /= 2;
                }
            }

            int mirror = even ? n - 2 : n - 1;
            index = new int[mirror + n];
            int at = 0;
            for (int i = mirror; i >= 1; i--)
            {
                index[at++] = i;
            }

            for (int i = 0; i < n; i++)
            {
                index[at++] = i;
            }
        }
        else
        {
            index = new int[n];
            int at = 0;
            int start = even ? (n / 2) + 1 : ((n + 1) / 2);
            for (int i = start; i < n; i++)
            {
                index[at++] = i;
            }

            for (int i = 0; i < start; i++)
            {
                index[at++] = i;
            }
        }

        var outValues = new Complex[index.Length];
        var outFrequencies = new double[index.Length];
        for (int i = 0; i < index.Length; i++)
        {
            outValues[i] = work[index[i]];
            outFrequencies[i] = frequencies[index[i]];
        }

        int negatives = index.Length - n;
        if (range == SpectralRange.OneSided)
        {
            for (int i = 0; i < negatives; i++)
            {
                outFrequencies[i] = -outFrequencies[i];
            }
        }
        else
        {
            int count = even ? (n / 2) - 1 : (n - 1) / 2;
            for (int i = 0; i < count; i++)
            {
                outFrequencies[i] -= fs;
            }
        }

        return (outValues, outFrequencies);
    }

    /// <summary>
    /// The two chi-squared multipliers a confidence level asks for at <paramref name="k"/> degrees
    /// (MATLAB's <c>chi2conf</c>).
    /// </summary>
    public static (double Lower, double Upper) Chi2Confidence(double confidence, double k)
    {
        double v = 2 * k;
        double alpha = 1 - confidence;
        double lower = v / (2 * SpecialFunctions.GammaInverse(v / 2, 1 - (alpha / 2)));
        double upper = v / (2 * SpecialFunctions.GammaInverse(v / 2, alpha / 2));
        return (lower, upper);
    }

    /// <summary>
    /// MATLAB's <c>confInterval</c>: the interval around every estimate, with the real signal's
    /// zero and Nyquist bins given half the degrees of freedom the rest have.
    /// </summary>
    public static (double[] Lower, double[] Upper) ConfidenceInterval(
        double confidence,
        double[] pxx,
        double[] frequencies,
        bool realInput,
        double fs,
        double k)
    {
        double whole = System.Math.Truncate(k);
        (double lower, double upper) = Chi2Confidence(confidence, whole);
        var low = new double[pxx.Length];
        var high = new double[pxx.Length];
        for (int i = 0; i < pxx.Length; i++)
        {
            low[i] = pxx[i] * lower;
            high[i] = pxx[i] * upper;
        }

        if (!realInput)
        {
            return (low, high);
        }

        (double halfLower, double halfUpper) = Chi2Confidence(confidence, whole / 2);
        double nyquist = fs / 2;
        for (int i = 0; i < pxx.Length; i++)
        {
            if (frequencies[i] == 0 || frequencies[i] == nyquist)
            {
                low[i] = pxx[i] * halfLower;
                high[i] = pxx[i] * halfUpper;
            }
        }

        return (low, high);
    }

    /// <summary>The segment starts Welch's average runs over, MATLAB's <c>xStart</c>.</summary>
    public static int[] SegmentStarts(int length, int segment, int overlap)
    {
        int advance = segment - overlap;
        int k = advance <= 0 ? 0 : (length - overlap) / advance;
        var starts = new int[System.Math.Max(k, 0)];
        for (int i = 0; i < starts.Length; i++)
        {
            starts[i] = i * advance;
        }

        return starts;
    }
}
