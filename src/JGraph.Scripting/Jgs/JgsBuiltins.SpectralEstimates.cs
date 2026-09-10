using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The non-parametric spectral estimates of the Signal Processing Toolbox (M136): the periodogram,
/// Welch's average, and the cross, coherence and transfer estimates built on it, together with the
/// four legacy names that are the same estimates under their old spellings.
/// </summary>
/// <remarks>
/// Every one of these names reads its arguments the same way, and MATLAB writes that reading once,
/// in <c>psdoptions</c>. The dance is: an optional window and overlap for the segmented names, then
/// up to two numbers — a transform length or a list of frequencies, and a sample rate — and any
/// number of words among the range, the scaling, the trace and the confidence level. The words may
/// come in any order and may be abbreviated. This file holds that reading once, for the same reason.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the non-parametric spectral names.</summary>
    internal static void RegisterSpectralEstimateBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Estimate(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Estimate("periodogram", PeriodogramEstimate);
        Estimate("pwelch", WelchEstimate);
        Estimate("cpsd", WelchEstimate);
        Estimate("mscohere", WelchEstimate);
        Estimate("tfestimate", WelchEstimate);
    }

    /// <summary>The names that take two signals rather than one.</summary>
    private static bool IsTwoSignalEstimate(string name) =>
        name is "cpsd" or "mscohere" or "tfestimate";

    // --- Reading a signal --------------------------------------------------------------------------

    /// <summary>
    /// A signal read as channels: a vector is one channel however it lies, and anything wider is one
    /// channel per column. This is MATLAB's rule for every name in the family.
    /// </summary>
    private static (Complex[][] Channels, bool Real, bool WasVector) SpectralChannels(
        string name, JgsValue value, int line, int col)
    {
        Complex[] flat = ComplexArrayOf(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        bool real = true;
        foreach (Complex z in flat)
        {
            if (z.Imaginary != 0)
            {
                real = false;
                break;
            }
        }

        if (rows == 1 || columns == 1)
        {
            return ([flat], real, true);
        }

        var channels = new Complex[columns][];
        for (int c = 0; c < columns; c++)
        {
            channels[c] = new Complex[rows];
            Array.Copy(flat, c * rows, channels[c], 0, rows);
        }

        return (channels, real, false);
    }

    /// <summary>A per-channel answer laid out as a matrix, one column a channel.</summary>
    private static JgsValue SpectralMatrix(Complex[][] channels)
    {
        int rows = channels.Length == 0 ? 0 : channels[0].Length;
        var flat = new Complex[rows * channels.Length];
        for (int c = 0; c < channels.Length; c++)
        {
            Array.Copy(channels[c], 0, flat, c * rows, rows);
        }

        return ComplexShaped(flat, [rows, channels.Length]);
    }

    /// <summary>A per-channel real answer laid out as a matrix.</summary>
    private static JgsValue SpectralRealMatrix(Complex[][] channels)
    {
        int rows = channels.Length == 0 ? 0 : channels[0].Length;
        var flat = new double[rows * channels.Length];
        for (int c = 0; c < channels.Length; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                flat[(c * rows) + i] = channels[c][i].Real;
            }
        }

        return JgsMatrix.FromColumnMajor(flat, rows, channels.Length);
    }

    /// <summary>The confidence bounds interleaved the way MATLAB stacks them: lower, upper, per channel.</summary>
    private static JgsValue ConfidenceMatrix(double[][] lower, double[][] upper)
    {
        int rows = lower.Length == 0 ? 0 : lower[0].Length;
        var flat = new double[rows * lower.Length * 2];
        for (int c = 0; c < lower.Length; c++)
        {
            Array.Copy(lower[c], 0, flat, 2 * c * rows, rows);
            Array.Copy(upper[c], 0, flat, ((2 * c) + 1) * rows, rows);
        }

        return JgsMatrix.FromColumnMajor(flat, rows, lower.Length * 2);
    }

    // --- Reading the options -----------------------------------------------------------------------

    /// <summary>What the shared argument dance yields before the defaults are filled in.</summary>
    private sealed class SpectralWords
    {
        public double[]? Frequencies { get; set; }

        public int Nfft { get; set; }

        public bool NfftGiven { get; set; }

        public double? SampleRate { get; set; }

        public bool OneSided { get; set; }

        public bool TwoSided { get; set; }

        public bool Centered { get; set; }

        public bool Reassign { get; set; }

        public double? ConfidenceLevel { get; set; }

        public SpectralScaling Scaling { get; set; } = SpectralScaling.Psd;

        public SpectralTrace Trace { get; set; } = SpectralTrace.Mean;

        public bool Mimo { get; set; }

        public bool SecondEstimator { get; set; }
    }

    /// <summary>
    /// Reads the words and numbers every spectral name shares, from <paramref name="start"/> to the
    /// end of the argument list.
    /// </summary>
    private static SpectralWords ReadSpectralWords(
        string name, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        var words = new SpectralWords();
        int numbers = 0;
        for (int i = start; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = SpectralWord(name, args[i], line, col);
                switch (word)
                {
                    case "onesided":
                    case "half":
                        words.OneSided = true;
                        break;
                    case "twosided":
                    case "whole":
                        words.TwoSided = true;
                        break;
                    case "centered":
                        words.Centered = true;
                        break;
                    case "power":
                    case "ms":
                        words.Scaling = SpectralScaling.Power;
                        break;
                    case "psd":
                        words.Scaling = SpectralScaling.Psd;
                        break;
                    case "reassigned":
                        words.Reassign = true;
                        break;
                    case "mimo":
                        words.Mimo = true;
                        break;
                    case "estimator":
                        if (i + 1 >= args.Count)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{name} needs a value after 'Estimator'.");
                        }

                        string which = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                        words.SecondEstimator = which == "h2";
                        if (which != "h1" && which != "h2")
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{name} takes 'H1' or 'H2' as its estimator.");
                        }

                        i++;
                        break;
                    case "mean":
                        words.Trace = SpectralTrace.Mean;
                        break;
                    case "maxhold":
                        words.Trace = SpectralTrace.MaxHold;
                        break;
                    case "minhold":
                        words.Trace = SpectralTrace.MinHold;
                        break;
                    case "confidencelevel":
                        if (i + 1 >= args.Count)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{name} needs a value after 'ConfidenceLevel'.");
                        }

                        double level = Num(name, args, i + 1, line, col);
                        if (!(level > 0 && level < 1))
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{name} needs a confidence level strictly between 0 and 1.");
                        }

                        words.ConfidenceLevel = level;
                        i++;
                        break;
                    default:
                        throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                continue;
            }

            numbers++;
            if (numbers == 1)
            {
                if (ElementCount(args[i]) == 0)
                {
                    continue;
                }

                double[] given = ToDoubles(name, args[i], line, col);
                if (given.Length == 1)
                {
                    words.Nfft = (int)given[0];
                    words.NfftGiven = true;
                    if (words.Nfft <= 0 || given[0] != System.Math.Floor(given[0]))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name} needs a positive whole number of transform points.");
                    }
                }
                else
                {
                    words.Frequencies = given;
                }

                continue;
            }

            if (numbers == 2)
            {
                if (ElementCount(args[i]) == 0)
                {
                    words.SampleRate = 1;
                    continue;
                }

                double fs = Num(name, args, i, line, col);
                if (!(fs > 0) || double.IsInfinity(fs))
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a positive sample rate.");
                }

                words.SampleRate = fs;
                continue;
            }

            throw new JgsRuntimeException(line, col, $"{name} reads at most a transform length and a sample rate.");
        }

        return words;
    }

    /// <summary>An option word, lower-cased, with the hyphen of 'one-sided' taken out, matched by prefix.</summary>
    private static string SpectralWord(string name, JgsValue value, int line, int col)
    {
        string given = StrOf(name, value, line, col).ToLowerInvariant();
        if (given.Length > 3
            && (given.StartsWith("one", StringComparison.Ordinal) || given.StartsWith("two", StringComparison.Ordinal))
            && (given[3] == ' ' || given[3] == '-'))
        {
            given = given[..3] + given[4..];
        }

        string[] allowed =
        [
            "onesided", "twosided", "centered", "half", "whole", "power", "psd", "ms",
            "reassigned", "mean", "maxhold", "minhold", "confidencelevel", "mimo", "estimator",
        ];
        foreach (string candidate in allowed)
        {
            if (given.Length > 0 && given.Length <= candidate.Length
                && candidate.StartsWith(given, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return given;
    }

    /// <summary>Turns the words into the request the estimators take.</summary>
    private static SpectralRequest SpectralRequestOf(
        string name, SpectralWords words, bool real, int defaultNfft, int line, int col)
    {
        bool scalarNfft = words.Frequencies is null;
        if (words.OneSided && !real)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} has no one-sided estimate for a complex signal.");
        }

        SpectralRange range = words.OneSided
            ? SpectralRange.OneSided
            : words.TwoSided
                ? SpectralRange.TwoSided
                : real && scalarNfft ? SpectralRange.OneSided : SpectralRange.TwoSided;
        if (!scalarNfft)
        {
            range = SpectralRange.TwoSided;
        }

        return new SpectralRequest
        {
            Frequencies = words.Frequencies,
            Nfft = words.NfftGiven ? words.Nfft : defaultNfft,
            SampleRate = words.SampleRate ?? (2 * System.Math.PI),
            SampleRateGiven = words.SampleRate is not null,
            Range = range,
            CenterDc = words.Centered && scalarNfft,
            ConfidenceLevel = words.ConfidenceLevel,
            Reassign = words.Reassign,
            Scaling = words.Scaling,
            Trace = words.Trace,
            Mimo = words.Mimo,
            SecondEstimator = words.SecondEstimator,
        };
    }

    // --- periodogram -------------------------------------------------------------------------------

    /// <summary><c>[pxx, w] = periodogram(x, window, nfft, fs, ...)</c>.</summary>
    private static JgsValue[] PeriodogramEstimate(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 12, line, col);
        (Complex[][] channels, bool real, bool wasVector) = SpectralChannels(name, args[0], line, col);
        int n = channels[0].Length;
        int start = 1;
        double[]? window = null;
        if (args.Count > 1 && !IsTextScalar(args[1]))
        {
            if (ElementCount(args[1]) > 0)
            {
                window = ToDoubles(name, args[1], line, col);
            }

            start = 2;
        }

        SpectralWords words = ReadSpectralWords(name, args, start, line, col);
        SpectralRequest request = SpectralRequestOf(
            name, words, real, System.Math.Max(256, Fft.NextPowerOfTwo(n)), line, col);
        request.Window = window ?? (words.Reassign ? SignalWindows.Kaiser(n, 38) : Ones(n));
        if (request.Window.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a window as long as the signal.");
        }

        if (words.Reassign && words.ConfidenceLevel is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} cannot reassign an estimate and give a confidence interval for it.");
        }

        if (wanted > 2 && !words.Reassign && words.ConfidenceLevel is null)
        {
            request.ConfidenceLevel = SpectralEstimation.DefaultConfidence;
        }

        SpectralAnswer answer = SpectralEstimators.Periodogram(channels, real, request);
        return PackEstimate(answer, request, wanted, wasVector, words.Frequencies is not null);
    }

    /// <summary>A rectangular window, which is what a periodogram uses when none was named.</summary>
    private static double[] RectangularWindow(int n)
    {
        var window = new double[n];
        Array.Fill(window, 1.0);
        return window;
    }

    // --- the Welch family --------------------------------------------------------------------------

    /// <summary><c>pwelch</c>, <c>cpsd</c>, <c>mscohere</c>, <c>tfestimate</c> and their legacy spellings.</summary>
    private static JgsValue[] WelchEstimate(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        string modern = name;
        bool two = IsTwoSignalEstimate(name);
        ArityRange(name, args, two ? 2 : 1, 12, line, col);
        (Complex[][] channels, bool realX, bool wasVector) = SpectralChannels(name, args[0], line, col);
        Complex[][]? second = null;
        bool real = realX;
        int start = 1;
        if (two)
        {
            (Complex[][] other, bool realY, _) = SpectralChannels(name, args[1], line, col);
            if (other[0].Length != channels[0].Length)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs two signals of the same length.");
            }

            second = other;
            real = realX && realY;
            start = 2;
        }

        int n = channels[0].Length;
        double[]? window = null;
        int? overlap = null;
        if (args.Count > start && !IsTextScalar(args[start]))
        {
            if (ElementCount(args[start]) > 0)
            {
                double[] given = ToDoubles(name, args[start], line, col);
                window = given.Length == 1 ? SignalWindows.Hamming((int)given[0]) : given;
                if (given.Length == 1 && (int)given[0] <= 1)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a segment longer than one sample.");
                }
            }

            start++;
            if (args.Count > start && !IsTextScalar(args[start]))
            {
                if (ElementCount(args[start]) > 0)
                {
                    overlap = (int)Num(name, args, start, line, col);
                }

                start++;
            }
        }

        int segment;
        if (window is null)
        {
            segment = overlap is null ? (int)(n / 4.5) : (int)((n + (7.0 * overlap.Value)) / 8);
            if (segment < 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs a signal long enough for its default segments.");
            }

            window = SignalWindows.Hamming(segment);
        }

        segment = window.Length;
        overlap ??= segment / 2;
        if (segment > n)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a segment no longer than the signal.");
        }

        if (overlap.Value >= segment || overlap.Value < 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs an overlap shorter than the segment.");
        }

        SpectralWords words = ReadSpectralWords(name, args, start, line, col);
        SpectralRequest request = SpectralRequestOf(
            name, words, real, System.Math.Max(256, Fft.NextPowerOfTwo(segment)), line, col);
        request.Window = window;
        request.Overlap = overlap.Value;
        request.Unscaled = modern is "mscohere" or "tfestimate";
        if (wanted > 2 && words.ConfidenceLevel is null && modern == "pwelch")
        {
            request.ConfidenceLevel = SpectralEstimation.DefaultConfidence;
        }

        bool mimo = request.Mimo && (channels.Length > 1 || (second is not null && second.Length > 1));
        int[]? pages = null;
        SpectralAnswer answer;
        if (mimo && modern == "cpsd")
        {
            var left = new Complex[channels.Length * second!.Length][];
            var right = new Complex[left.Length][];
            for (int i = 0; i < left.Length; i++)
            {
                left[i] = channels[i % channels.Length];
                right[i] = second[i / channels.Length];
            }

            answer = SpectralEstimators.Welch(left, right, real, request);
            pages = [channels.Length, second.Length];
        }
        else if (mimo && modern == "tfestimate")
        {
            answer = SpectralEstimators.MimoTransfer(channels, second!, real, request);
            pages = [second!.Length, channels.Length];
        }
        else
        {
            answer = modern switch
            {
                "mscohere" => mimo
                    ? SpectralEstimators.MimoCoherence(channels, second!, real, request)
                    : SpectralEstimators.Coherence(channels, second!, real, request),
                "tfestimate" => SpectralEstimators.TransferEstimate(channels, second!, real, request),
                _ => SpectralEstimators.Welch(channels, second, real, request),
            };
        }

        JgsValue[] packed = PackEstimate(answer, request, wanted, wasVector, words.Frequencies is not null);
        if (pages is not null && packed.Length > 0)
        {
            packed[0] = ComplexPages(answer.Values, pages[0], pages[1]);
        }

        return packed;
    }

    /// <summary>
    /// A many-channel estimate as a page per input: MATLAB reports a MIMO transfer function as
    /// frequency down the rows, output across the columns and input across the pages.
    /// </summary>
    private static JgsValue ComplexPages(Complex[][] channels, int first, int second)
    {
        int bins = channels.Length == 0 ? 0 : channels[0].Length;
        var flat = new Complex[bins * first * second];
        for (int c = 0; c < channels.Length; c++)
        {
            for (int i = 0; i < bins; i++)
            {
                flat[i + (c * bins)] = channels[c][i];
            }
        }

        return ComplexShaped(flat, [bins, first, second]);
    }

    // --- Packing the answer ------------------------------------------------------------------------

    /// <summary>
    /// The outputs in MATLAB's order. A frequency vector given as a row makes a vector signal's
    /// answer a row too, which is the one shape rule these names keep from the days before columns.
    /// </summary>
    private static JgsValue[] PackEstimate(
        SpectralAnswer answer, SpectralRequest request, int wanted, bool wasVector, bool freqVector)
    {
        bool transpose = freqVector && wasVector;
        JgsValue values = answer.Values.Length == 1 && transpose
            ? ComplexShaped(answer.Values[0], [1, answer.Values[0].Length])
            : SpectralMatrix(answer.Values);
        JgsValue frequencies = transpose
            ? JgsMatrix.FromColumnMajor(answer.Frequencies, 1, answer.Frequencies.Length)
            : ColumnOfDoubles(answer.Frequencies);

        if (wanted <= 1)
        {
            return [values];
        }

        if (wanted == 2)
        {
            return [values, frequencies];
        }

        if (answer.Unreassigned is not null)
        {
            JgsValue original = SpectralRealMatrix(answer.Unreassigned);
            if (wanted == 3)
            {
                return [values, frequencies, original];
            }

            var centres = new Complex[answer.Centres!.Length][];
            for (int c = 0; c < centres.Length; c++)
            {
                centres[c] = new Complex[answer.Centres[c].Length];
                for (int i = 0; i < centres[c].Length; i++)
                {
                    centres[c][i] = answer.Centres[c][i];
                }
            }

            return [values, frequencies, original, SpectralRealMatrix(centres)];
        }

        JgsValue interval = answer.ConfidenceLower is not null && answer.ConfidenceUpper is not null
            ? ConfidenceMatrix(answer.ConfidenceLower, answer.ConfidenceUpper)
            : JgsMatrix.FromColumnMajor([], 0, 0);
        return [values, frequencies, interval];
    }
}
