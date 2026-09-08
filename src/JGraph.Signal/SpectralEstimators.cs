using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>Which trace a Welch average keeps when it has more than one segment.</summary>
public enum SpectralTrace
{
    /// <summary>The mean of the segments' periodograms, which is Welch's estimate.</summary>
    Mean,

    /// <summary>The largest value each bin took over the segments.</summary>
    MaxHold,

    /// <summary>The smallest value each bin took over the segments.</summary>
    MinHold,
}

/// <summary>
/// Everything the spectral names share once their arguments are read: the transform length, the
/// sample rate, the range, and the options that only some of them accept.
/// </summary>
public sealed class SpectralRequest
{
    /// <summary>The frequencies asked for by name, or null when a transform length was given instead.</summary>
    public double[]? Frequencies { get; set; }

    /// <summary>The transform length, used when <see cref="Frequencies"/> is null.</summary>
    public int Nfft { get; set; } = 256;

    /// <summary>The sample rate in hertz, or 2π when the caller did not name one.</summary>
    public double SampleRate { get; set; } = 2 * System.Math.PI;

    /// <summary>False when the estimate is reported against normalised frequency in radians a sample.</summary>
    public bool SampleRateGiven { get; set; }

    /// <summary>Which part of the Nyquist interval the estimate covers.</summary>
    public SpectralRange Range { get; set; } = SpectralRange.OneSided;

    /// <summary>True when zero frequency is moved to the middle of the answer.</summary>
    public bool CenterDc { get; set; }

    /// <summary>The confidence level asked for, or null when none was.</summary>
    public double? ConfidenceLevel { get; set; }

    /// <summary>True when every estimate is moved to its centre of gravity.</summary>
    public bool Reassign { get; set; }

    /// <summary>Whether the window's gain comes out as a density or as a power.</summary>
    public SpectralScaling Scaling { get; set; } = SpectralScaling.Psd;

    /// <summary>The window each segment is multiplied by.</summary>
    public double[] Window { get; set; } = [];

    /// <summary>How many samples one segment shares with the next.</summary>
    public int Overlap { get; set; }

    /// <summary>Which trace a segmented estimate keeps.</summary>
    public SpectralTrace Trace { get; set; } = SpectralTrace.Mean;

    /// <summary>Whether the two-signal estimates pair every input with every output.</summary>
    public bool Mimo { get; set; }

    /// <summary>Whether a transfer estimate divides the output's autospectrum by the cross one.</summary>
    public bool SecondEstimator { get; set; }

    /// <summary>True when the estimate is a coherence, whose one-sided doubling is not undone by centring.</summary>
    public bool Unscaled { get; set; }
}

/// <summary>What a spectral estimator hands back: the estimate and whatever else was asked for.</summary>
public sealed class SpectralAnswer
{
    /// <summary>The estimate, one array per channel.</summary>
    public Complex[][] Values { get; set; } = [];

    /// <summary>The frequency of each bin, in hertz or in radians a sample.</summary>
    public double[] Frequencies { get; set; } = [];

    /// <summary>The lower end of the confidence interval, one array per channel, or null.</summary>
    public double[][]? ConfidenceLower { get; set; }

    /// <summary>The upper end of the confidence interval, one array per channel, or null.</summary>
    public double[][]? ConfidenceUpper { get; set; }

    /// <summary>The estimate before reassignment moved it, or null when reassignment was off.</summary>
    public Complex[][]? Unreassigned { get; set; }

    /// <summary>Each bin's centre-of-gravity frequency, or null when reassignment was off.</summary>
    public double[][]? Centres { get; set; }

    /// <summary>The number of segments the estimate averaged.</summary>
    public int Segments { get; set; } = 1;
}

/// <summary>
/// The periodogram and the Welch average, which between them are every non-parametric spectral
/// estimate in the toolbox. Both are MATLAB's, argument for argument: <c>periodogram</c> is one
/// segment and <c>welch</c> is the mean of many, and the cross, coherence and transfer estimates
/// are the same average with a second signal in it.
/// </summary>
public static class SpectralEstimators
{
    /// <summary>MATLAB's <c>periodogram</c> over one or more channels.</summary>
    public static SpectralAnswer Periodogram(Complex[][] channels, bool realInput, SpectralRequest request)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(request);

        double fs = request.SampleRate;
        bool scalarNfft = request.Frequencies is null;
        int nfft = scalarNfft ? request.Nfft : request.Frequencies!.Length;
        double[] grid = scalarNfft
            ? SpectralEstimation.FrequencyGrid(nfft, fs)
            : request.Frequencies!;
        int reassignPoints = request.Reassign ? (scalarNfft ? request.Nfft : request.Window.Length) : 0;

        var values = new Complex[channels.Length][];
        var reassigned = new Complex[channels.Length][];
        var centres = new double[channels.Length][];
        double[] frequencies = grid;
        for (int c = 0; c < channels.Length; c++)
        {
            Complex[] sxx = SpectralEstimation.Periodogram(
                channels[c], [], request.Window, nfft, request.Frequencies, fs, request.Scaling, reassignPoints);
            if (request.Reassign)
            {
                double[] wc = SpectralEstimation.CentreFrequencies(channels[c], request.Window, nfft, grid, fs);
                Complex[] moved = Reassign(sxx, grid, wc, scalarNfft && request.Range == SpectralRange.TwoSided);
                (Complex[] rp, _) = SpectralEstimation.ToPsd(
                    moved, grid, request.Range, scalarNfft, fs, request.Scaling);
                reassigned[c] = rp;
                centres[c] = OneSidedCentres(wc, request.Range, scalarNfft, nfft, fs, request.CenterDc);
            }

            (Complex[] pxx, double[] w) = SpectralEstimation.ToPsd(
                sxx, grid, request.Range, scalarNfft, fs, request.Scaling);
            values[c] = pxx;
            frequencies = w;
        }

        var answer = new SpectralAnswer { Values = values, Frequencies = frequencies };
        if (request.Reassign)
        {
            answer.Unreassigned = values;
            answer.Values = reassigned;
            answer.Centres = centres;
        }

        Finish(answer, request, realInput, 1, scalarNfft ? request.Nfft : nfft, scalarNfft);
        return answer;
    }

    /// <summary>MATLAB's <c>welch</c>: the mean, largest or smallest of the segments' periodograms.</summary>
    /// <remarks>
    /// <paramref name="second"/> null makes this <c>pwelch</c>. A second signal makes it the cross
    /// spectrum, which <c>cpsd</c> reports directly and which <c>mscohere</c> and <c>tfestimate</c>
    /// divide by one or both of the auto spectra.
    /// </remarks>
    public static SpectralAnswer Welch(
        Complex[][] channels,
        Complex[][]? second,
        bool realInput,
        SpectralRequest request)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(request);

        double fs = request.SampleRate;
        bool scalarNfft = request.Frequencies is null;
        int nfft = scalarNfft ? request.Nfft : request.Frequencies!.Length;
        double[] grid = scalarNfft
            ? SpectralEstimation.FrequencyGrid(nfft, fs)
            : request.Frequencies!;
        int segment = request.Window.Length;
        int[] starts = SpectralEstimation.SegmentStarts(channels[0].Length, segment, request.Overlap);
        int k = starts.Length;
        int lanes = second is null ? channels.Length : System.Math.Max(channels.Length, second.Length);

        var values = new Complex[lanes][];
        double[] frequencies = grid;
        for (int c = 0; c < lanes; c++)
        {
            Complex[] x = channels[c % channels.Length];
            Complex[] y = second is null ? [] : second[c % second.Length];
            Complex[] sum = Accumulate(x, y, starts, request, nfft, fs, k);
            (Complex[] pxx, double[] w) = SpectralEstimation.ToPsd(
                sum, grid, request.Range, scalarNfft, fs, request.Scaling);
            values[c] = pxx;
            frequencies = w;
        }

        var answer = new SpectralAnswer { Values = values, Frequencies = frequencies, Segments = k };
        Finish(answer, request, realInput, k, scalarNfft ? request.Nfft : nfft, scalarNfft);
        return answer;
    }

    /// <summary>
    /// MATLAB's <c>'mimo'</c> transfer estimate: every input paired with every output, and the
    /// matrix of cross spectra divided by the matrix of input autospectra at every frequency.
    /// </summary>
    /// <remarks>
    /// With one input the transfer function is a ratio; with several it is a linear system, because
    /// each output is driven by all of them at once and the inputs are generally correlated. The
    /// division is a matrix one, and a singular input matrix at some frequency means the inputs did
    /// not excite that frequency independently.
    /// </remarks>
    public static SpectralAnswer MimoTransfer(
        Complex[][] x,
        Complex[][] y,
        bool realInput,
        SpectralRequest request)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(request);

        int inputs = x.Length;
        int outputs = y.Length;
        SpectralRequest plain = Plain(request);
        SpectralAnswer autos = Welch(Pick(x, Repeat(inputs, inputs)), Pick(x, Spread(inputs, inputs)),
            realInput, plain);
        SpectralAnswer cross = Welch(Pick(y, Repeat(outputs, inputs)), Pick(x, Spread(inputs, outputs)),
            realInput, plain);

        int bins = cross.Frequencies.Length;
        var values = new Complex[inputs * outputs][];
        for (int c = 0; c < values.Length; c++)
        {
            values[c] = new Complex[bins];
        }

        for (int f = 0; f < bins; f++)
        {
            var gram = new Complex[inputs, inputs];
            for (int r = 0; r < inputs; r++)
            {
                for (int c = 0; c < inputs; c++)
                {
                    gram[r, c] = autos.Values[r + (c * inputs)][f];
                }
            }

            var right = new Complex[inputs, outputs];
            for (int r = 0; r < inputs; r++)
            {
                for (int c = 0; c < outputs; c++)
                {
                    // A right division by the input matrix is a left division of its transpose.
                    right[r, c] = cross.Values[c + (r * outputs)][f];
                }
            }

            Complex[,] solved = HouseholderQr.BasicSolution(Transposed(gram, inputs), right, -1, out _);
            for (int r = 0; r < inputs; r++)
            {
                for (int c = 0; c < outputs; c++)
                {
                    values[c + (r * outputs)][f] = solved[r, c];
                }
            }
        }

        var answer = new SpectralAnswer
        {
            Values = values,
            Frequencies = cross.Frequencies,
            Segments = cross.Segments,
        };
        Finish(answer, request, realInput, cross.Segments, request.Nfft, request.Frequencies is null);
        return answer;
    }

    /// <summary>MATLAB's <c>'mimo'</c> coherence: one number per output, over all inputs at once.</summary>
    public static SpectralAnswer MimoCoherence(
        Complex[][] x,
        Complex[][] y,
        bool realInput,
        SpectralRequest request)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(request);

        int inputs = x.Length;
        int outputs = y.Length;
        SpectralRequest plain = Plain(request);
        SpectralAnswer autos = Welch(Pick(x, Repeat(inputs, inputs)), Pick(x, Spread(inputs, inputs)),
            realInput, plain);
        SpectralAnswer outs = Welch(y, null, realInput, plain);
        SpectralAnswer cross = Welch(Pick(x, Repeat(inputs, outputs)), Pick(y, Spread(outputs, inputs)),
            realInput, plain);

        int bins = cross.Frequencies.Length;
        var values = new Complex[outputs][];
        for (int c = 0; c < outputs; c++)
        {
            values[c] = new Complex[bins];
        }

        for (int f = 0; f < bins; f++)
        {
            var gram = new Complex[inputs, inputs];
            for (int r = 0; r < inputs; r++)
            {
                for (int c = 0; c < inputs; c++)
                {
                    gram[r, c] = autos.Values[r + (c * inputs)][f];
                }
            }

            for (int o = 0; o < outputs; o++)
            {
                var column = new Complex[inputs, 1];
                for (int j = 0; j < inputs; j++)
                {
                    column[j, 0] = cross.Values[(o * inputs) + j][f];
                }

                // The coherence divides on the right of a row vector and on the left of a column
                // one, so the system solved here is the plain matrix, not its transpose.
                Complex[,] solved = HouseholderQr.BasicSolution(gram, column, -1, out _);
                Complex sum = 0;
                for (int j = 0; j < inputs; j++)
                {
                    sum += Complex.Conjugate(cross.Values[(o * inputs) + j][f]) * solved[j, 0];
                }

                values[o][f] = sum.Real / outs.Values[o][f].Real;
            }
        }

        var answer = new SpectralAnswer
        {
            Values = values,
            Frequencies = cross.Frequencies,
            Segments = cross.Segments,
        };
        Finish(answer, request, realInput, cross.Segments, request.Nfft, request.Frequencies is null);
        return answer;
    }

    /// <summary><c>repmat(1:n, 1, times)</c>.</summary>
    private static int[] Repeat(int n, int times)
    {
        var index = new int[n * times];
        for (int i = 0; i < index.Length; i++)
        {
            index[i] = i % n;
        }

        return index;
    }

    /// <summary><c>repelem(1:n, times)</c>, written as MATLAB writes it with a reshape.</summary>
    private static int[] Spread(int n, int times)
    {
        var index = new int[n * times];
        for (int i = 0; i < index.Length; i++)
        {
            index[i] = i / times;
        }

        return index;
    }

    private static Complex[][] Pick(Complex[][] channels, int[] index)
    {
        var picked = new Complex[index.Length][];
        for (int i = 0; i < index.Length; i++)
        {
            picked[i] = channels[index[i]];
        }

        return picked;
    }

    private static Complex[,] Transposed(Complex[,] a, int n)
    {
        var t = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                t[r, c] = a[c, r];
            }
        }

        return t;
    }

    /// <summary>MATLAB's <c>mscohere</c>: the cross spectrum's squared magnitude over the two autos.</summary>
    public static SpectralAnswer Coherence(
        Complex[][] x,
        Complex[][] y,
        bool realInput,
        SpectralRequest request)
    {
        SpectralAnswer pxx = Welch(x, null, realInput, Plain(request));
        SpectralAnswer pyy = Welch(y, null, realInput, Plain(request));
        SpectralAnswer pxy = Welch(x, y, realInput, Plain(request));
        var values = new Complex[pxy.Values.Length][];
        for (int c = 0; c < values.Length; c++)
        {
            Complex[] cross = pxy.Values[c];
            Complex[] a = pxx.Values[c % pxx.Values.Length];
            Complex[] b = pyy.Values[c % pyy.Values.Length];
            values[c] = new Complex[cross.Length];
            for (int i = 0; i < cross.Length; i++)
            {
                double magnitude = cross[i].Magnitude;
                values[c][i] = magnitude * magnitude / (a[i] * b[i]);
            }
        }

        var answer = new SpectralAnswer
        {
            Values = values,
            Frequencies = pxy.Frequencies,
            Segments = pxy.Segments,
        };
        Finish(answer, request, realInput, pxy.Segments, request.Nfft, request.Frequencies is null);
        return answer;
    }

    /// <summary>MATLAB's <c>tfestimate</c>: the cross spectrum over the input's auto spectrum.</summary>
    public static SpectralAnswer TransferEstimate(
        Complex[][] x,
        Complex[][] y,
        bool realInput,
        SpectralRequest request)
    {
        // H1 divides the cross spectrum by the input's autospectrum, which is unbiased when the
        // noise is on the output; H2 divides the output's autospectrum by the cross one, which is
        // unbiased when the noise is on the input.
        SpectralAnswer above = request.SecondEstimator
            ? Welch(y, null, realInput, Plain(request))
            : Welch(y, x, realInput, Plain(request));
        SpectralAnswer below = request.SecondEstimator
            ? Welch(x, y, realInput, Plain(request))
            : Welch(x, null, realInput, Plain(request));
        SpectralAnswer pyx = above;
        var values = new Complex[System.Math.Max(above.Values.Length, below.Values.Length)][];
        for (int c = 0; c < values.Length; c++)
        {
            Complex[] cross = above.Values[c % above.Values.Length];
            Complex[] a = below.Values[c % below.Values.Length];
            values[c] = new Complex[cross.Length];
            for (int i = 0; i < cross.Length; i++)
            {
                values[c][i] = cross[i] / a[i];
            }
        }

        var answer = new SpectralAnswer
        {
            Values = values,
            Frequencies = pyx.Frequencies,
            Segments = pyx.Segments,
        };
        Finish(answer, request, realInput, pyx.Segments, request.Nfft, request.Frequencies is null);
        return answer;
    }

    /// <summary>The same request with centring and confidence off, so a ratio is taken before either.</summary>
    private static SpectralRequest Plain(SpectralRequest request) => new()
    {
        Frequencies = request.Frequencies,
        Nfft = request.Nfft,
        SampleRate = request.SampleRate,
        SampleRateGiven = request.SampleRateGiven,
        Range = request.Range,
        CenterDc = false,
        ConfidenceLevel = null,
        Reassign = false,
        Scaling = request.Scaling,
        Window = request.Window,
        Overlap = request.Overlap,
        Trace = request.Trace,
    };

    private static Complex[] Accumulate(
        Complex[] x,
        Complex[] y,
        int[] starts,
        SpectralRequest request,
        int nfft,
        double fs,
        int k)
    {
        int segment = request.Window.Length;
        Complex[]? sum = null;
        foreach (int start in starts)
        {
            Complex[] one = SpectralEstimation.Periodogram(
                x.AsSpan(start, segment),
                y.Length == 0 ? [] : y.AsSpan(start, segment),
                request.Window,
                nfft,
                request.Frequencies,
                fs,
                request.Scaling);
            if (sum is null)
            {
                sum = one;
                if (request.Trace != SpectralTrace.Mean)
                {
                    for (int i = 0; i < sum.Length; i++)
                    {
                        sum[i] *= k;
                    }
                }

                continue;
            }

            for (int i = 0; i < sum.Length; i++)
            {
                sum[i] = request.Trace switch
                {
                    SpectralTrace.MaxHold => System.Math.Max(sum[i].Real, k * one[i].Real),
                    SpectralTrace.MinHold => System.Math.Min(sum[i].Real, k * one[i].Real),
                    _ => sum[i] + one[i],
                };
            }
        }

        sum ??= new Complex[nfft];
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] /= k;
        }

        return sum;
    }

    /// <summary>Moves each estimate to the bin nearest its centre of gravity and sums what lands there.</summary>
    private static Complex[] Reassign(Complex[] p, double[] f, double[] centres, bool cyclic)
    {
        int n = f.Length;
        var result = new Complex[n];
        double first = f[0];
        double last = f[n - 1];
        double span = last - first;
        for (int i = 0; i < n; i++)
        {
            double position = span == 0 ? 0 : (centres[i] - first) * (n - 1) / span;
            int row = (int)System.Math.Round(position, MidpointRounding.AwayFromZero);
            if (cyclic)
            {
                row %= n;
                if (row < 0)
                {
                    row += n;
                }
            }

            if (row >= 0 && row < n)
            {
                result[row] += p[i];
            }
        }

        return result;
    }

    private static double[] OneSidedCentres(
        double[] wc,
        SpectralRange range,
        bool scalarNfft,
        int nfft,
        double fs,
        bool centerDc)
    {
        double[] kept = wc;
        if (range == SpectralRange.OneSided && scalarNfft)
        {
            kept = wc[..SpectralEstimation.OneSidedLength(nfft)];
        }

        var dealiased = new double[kept.Length];
        for (int i = 0; i < kept.Length; i++)
        {
            double v = kept[i];
            if (centerDc)
            {
                v = Modulo(v + (fs / 2), fs) - (fs / 2);
            }
            else
            {
                v = Modulo(v, fs);
            }

            dealiased[i] = v;
        }

        return dealiased;
    }

    private static double Modulo(double x, double y)
    {
        double r = x - (y * System.Math.Floor(x / y));
        return r;
    }

    private static void Finish(
        SpectralAnswer answer,
        SpectralRequest request,
        bool realInput,
        int k,
        int nfft,
        bool scalarNfft)
    {
        double fs = request.SampleRate;
        if (request.ConfidenceLevel is double level)
        {
            ApplyConfidence(answer, level, realInput, fs, k);
        }

        if (!request.CenterDc || !scalarNfft)
        {
            return;
        }

        // The frequencies move once; every column moves with them, and the un-doubling the
        // one-sided fold applied is undone on the way for every array that carries it.
        bool scaled = !request.Unscaled;
        double[] original = answer.Frequencies;
        double[] moved = original;
        for (int c = 0; c < answer.Values.Length; c++)
        {
            (Complex[] v, double[] f) = SpectralEstimation.CenterDc(
                answer.Values[c], original, request.Range, nfft, fs, scaled);
            answer.Values[c] = v;
            moved = f;
        }

        if (answer.Unreassigned is not null)
        {
            for (int c = 0; c < answer.Unreassigned.Length; c++)
            {
                answer.Unreassigned[c] = SpectralEstimation.CenterDc(
                    answer.Unreassigned[c], original, request.Range, nfft, fs, scaled).Values;
            }
        }

        if (answer.ConfidenceLower is not null && answer.ConfidenceUpper is not null)
        {
            for (int c = 0; c < answer.ConfidenceLower.Length; c++)
            {
                answer.ConfidenceLower[c] = RealPart(SpectralEstimation.CenterDc(
                    Boxed(answer.ConfidenceLower[c]), original, request.Range, nfft, fs, scaled).Values);
                answer.ConfidenceUpper[c] = RealPart(SpectralEstimation.CenterDc(
                    Boxed(answer.ConfidenceUpper[c]), original, request.Range, nfft, fs, scaled).Values);
            }
        }

        answer.Frequencies = moved;
    }

    /// <summary>Attaches the chi-squared interval around every channel's estimate.</summary>
    public static void ApplyConfidence(
        SpectralAnswer answer,
        double level,
        bool realInput,
        double fs,
        int k)
    {
        var lower = new double[answer.Values.Length][];
        var upper = new double[answer.Values.Length][];
        for (int c = 0; c < answer.Values.Length; c++)
        {
            (lower[c], upper[c]) = SpectralEstimation.ConfidenceInterval(
                level, RealPart(answer.Values[c]), answer.Frequencies, realInput, fs, k);
        }

        answer.ConfidenceLower = lower;
        answer.ConfidenceUpper = upper;
    }

    /// <summary>The real part of a complex estimate, which is all a power spectrum ever has.</summary>
    public static double[] RealPart(Complex[] values)
    {
        var real = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            real[i] = values[i].Real;
        }

        return real;
    }

    private static Complex[] Boxed(double[] values)
    {
        var boxed = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values[i];
        }

        return boxed;
    }
}
