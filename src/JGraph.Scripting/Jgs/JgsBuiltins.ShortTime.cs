using System.Numerics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The short-time Fourier transform and the names built on it (M137): <c>spectrogram</c>,
/// <c>xspectrogram</c>, <c>stft</c>, <c>istft</c> and <c>iscola</c>.
/// </summary>
/// <remarks>
/// <c>spectrogram</c> and <c>stft</c> compute the same transform and disagree about almost
/// everything around it. <c>spectrogram</c> reads its arguments positionally through the same
/// parser as <c>pwelch</c>, defaults to eight Hamming segments and a one-sided estimate, and
/// returns a power spectrogram beside the transform. <c>stft</c> reads name–value pairs, defaults
/// to a hundred and twenty-eight periodic Hann samples at three-quarters overlap and a centred
/// two-sided range, and returns the transform alone. Both are here so that neither has to pretend
/// to be the other.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the short-time transform names.</summary>
    internal static void RegisterShortTimeBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("spectrogram", ShortTimeSpectrogram);
        Many("xspectrogram", ShortTimeSpectrogram);
        Many("stft", ForwardShortTime);
        Many("istft", InverseShortTime);
        Many("iscola", ColaCheck);
        Many("stftmag2sig", MagnitudeToSignal);
    }

    /// <summary>The first output, or nothing when a name drew instead of answering.</summary>
    private static JgsValue First(JgsValue[] outputs) => outputs.Length > 0 ? outputs[0] : JgsValue.Null;

    // --- spectrogram and xspectrogram ---------------------------------------------------------------

    /// <summary><c>[s, f, t, p, fc, tc] = spectrogram(x, window, noverlap, nfft, fs, ...)</c>.</summary>
    private static JgsValue[] ShortTimeSpectrogram(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        bool cross = name == "xspectrogram";
        ArityRange(name, args, cross ? 2 : 1, 13, line, col);
        (Complex[][] xc, bool realX, _) = SpectralChannels(name, args[0], line, col);
        if (xc.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a vector signal.");
        }

        Complex[] x = xc[0];
        Complex[] y = [];
        bool real = realX;
        int start = 1;
        if (cross)
        {
            (Complex[][] yc, bool realY, _) = SpectralChannels(name, args[1], line, col);
            if (yc.Length != 1)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs vector signals.");
            }

            y = yc[0];
            real = realX && realY;
            start = 2;
        }

        (List<JgsValue> rest, double threshold, bool downRows) = StripSpectrogramWords(name, args, start, line, col);
        (double[] window, int overlap, int at) = ReadFrames(name, rest, 0, x.Length, line, col);
        SpectralWords words = ReadSpectralWords(name, rest, at, line, col);
        int defaultNfft = System.Math.Max(256, Fft.NextPowerOfTwo(window.Length));
        SpectralRequest shared = SpectralRequestOf(name, words, real, defaultNfft, line, col);
        shared.Window = window;
        shared.Overlap = overlap;

        if (cross)
        {
            return CrossSpectrogram(name, x, y, real, shared, threshold, wanted, downRows, line);
        }

        var request = new ShortTimeRequest
        {
            Window = window,
            Overlap = overlap,
            Nfft = shared.Nfft,
            Frequencies = shared.Frequencies,
            SampleRate = shared.SampleRate,
            // 'centered' asks for the whole spectrum and then rotates it, so the fold never happens.
            Range = shared.CenterDc ? SpectralRange.TwoSided : shared.Range,
            CenterDc = shared.CenterDc,
            Scaling = shared.Scaling,
            Reassign = words.Reassign,
            Threshold = threshold,
            WantPower = wanted == 0 || wanted > 3,
            WantFrequencyCentres = wanted > 4,
            WantTimeCentres = wanted > 5,
        };
        if (window.Length > x.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window no longer than the signal.");
        }

        ShortTimeAnswer answer = ShortTimeTransforms.Transform(x, request);
        if (wanted == 0)
        {
            DrawSpectrogram(answer, words.SampleRate is not null);
            return [];
        }

        var outputs = new List<JgsValue>
        {
            FrameMatrix(answer.Values, downRows),
            ColumnOfDoubles(answer.Frequencies),
            downRows ? ColumnOfDoubles(answer.Times) : RowOfDoubles(answer.Times),
        };
        if (wanted > 3)
        {
            outputs.Add(FrameMatrix(answer.Power!, downRows));
        }

        if (wanted > 4)
        {
            outputs.Add(RealFrameMatrix(answer.FrequencyCentres!, downRows));
        }

        if (wanted > 5)
        {
            outputs.Add(RealFrameMatrix(answer.TimeCentres!, downRows));
        }

        return [.. outputs.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary>
    /// <c>xspectrogram</c>, which MATLAB computes by handing the frames to <c>cpsd</c> one segment
    /// at a time — so it inherits <c>cpsd</c>'s scaling, folding and centring rather than the
    /// spectrogram's.
    /// </summary>
    private static JgsValue[] CrossSpectrogram(
        string name,
        Complex[] x,
        Complex[] y,
        bool real,
        SpectralRequest shared,
        double threshold,
        int wanted,
        bool downRows,
        int line)
    {
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, 0, $"{name} needs two signals of the same length.");
        }

        double fs = shared.SampleRate;
        (Complex[][] xin, double[] times) = ShortTimeTransforms.Columns(x, shared.Window.Length, shared.Overlap, fs);
        (Complex[][] yin, _) = ShortTimeTransforms.Columns(y, shared.Window.Length, shared.Overlap, fs);
        if (xin.Length == 0)
        {
            throw new JgsRuntimeException(line, 0, $"{name} needs a window no longer than the signals.");
        }

        var request = new SpectralRequest
        {
            Frequencies = shared.Frequencies,
            Nfft = shared.Nfft,
            SampleRate = fs,
            SampleRateGiven = shared.SampleRateGiven,
            Range = shared.Range,
            CenterDc = shared.CenterDc,
            Scaling = SpectralScaling.Psd,
            Window = shared.Window,
            Overlap = 0,
        };
        SpectralAnswer answer = SpectralEstimators.Welch(xin, yin, real, request);

        Complex[][] cross = answer.Values;
        if (shared.Scaling == SpectralScaling.Power)
        {
            double enbw = SpectralMeasurements.EquivalentNoiseBandwidth(shared.Window, fs);
            foreach (Complex[] column in cross)
            {
                for (int i = 0; i < column.Length; i++)
                {
                    column[i] *= enbw;
                }
            }
        }

        var magnitude = new double[cross.Length][];
        for (int c = 0; c < cross.Length; c++)
        {
            magnitude[c] = new double[cross[c].Length];
            for (int i = 0; i < cross[c].Length; i++)
            {
                double m = cross[c][i].Magnitude;
                magnitude[c][i] = threshold > 0 && m < threshold ? 0 : m;
            }
        }

        var outputs = new List<JgsValue>
        {
            RealFrameMatrix(magnitude, downRows),
            ColumnOfDoubles(answer.Frequencies),
            downRows ? ColumnOfDoubles(times) : RowOfDoubles(times),
        };
        if (wanted > 3)
        {
            outputs.Add(FrameMatrix(cross, downRows));
        }

        return [.. outputs.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary>
    /// Takes out the words <c>pwelch</c>'s parser would not know — the threshold, the output
    /// orientation, the axis location and the parent — and hands back what is left.
    /// </summary>
    private static (List<JgsValue> Remaining, double Threshold, bool DownRows) StripSpectrogramWords(
        string name, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        var rest = new List<JgsValue>();
        double threshold = 0;
        bool downRows = false;
        for (int i = start; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                rest.Add(args[i]);
                continue;
            }

            string word = StrOf(name, args[i], line, col).ToLowerInvariant();
            if (Matches(word, "minthreshold"))
            {
                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a value after 'MinThreshold'.");
                }

                threshold = System.Math.Pow(10, 0.1 * Num(name, args, i + 1, line, col));
                i++;
                continue;
            }

            if (Matches(word, "outputtimedimension"))
            {
                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a value after 'OutputTimeDimension'.");
                }

                string given = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                downRows = Matches(given, "downrows");
                if (!downRows && !Matches(given, "acrosscolumns"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'acrosscolumns' or 'downrows' as its output time dimension.");
                }

                i++;
                continue;
            }

            if (Matches(word, "parent"))
            {
                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a value after 'Parent'.");
                }

                i++;
                continue;
            }

            if (Matches(word, "xaxis") || Matches(word, "yaxis"))
            {
                continue;
            }

            rest.Add(args[i]);
        }

        return (rest, threshold, downRows);
    }

    /// <summary>The image <c>spectrogram</c> draws when nothing asked for its numbers.</summary>
    private static void DrawSpectrogram(ShortTimeAnswer answer, bool inHertz)
    {
        Complex[][] power = answer.Power!;
        int nt = power.Length;
        int nf = nt == 0 ? 0 : power[0].Length;
        var image = new double[nf, nt];
        for (int c = 0; c < nt; c++)
        {
            for (int i = 0; i < nf; i++)
            {
                image[i, c] = 10 * System.Math.Log10(System.Math.Max(power[c][i].Real, 1e-300));
            }
        }

        AxesModel axes = JG.Gca();
        if (nf > 0 && nt > 0)
        {
            axes.AddImage(
                image,
                new DataRange(answer.Times[0], answer.Times[nt - 1]),
                new DataRange(answer.Frequencies[0], answer.Frequencies[nf - 1]));
        }

        axes.PrimaryXAxis.Label = "Time (s)";
        axes.PrimaryYAxis.Label = inHertz ? "Frequency (Hz)" : "Normalized frequency (rad/sample)";
    }

    // --- stft, istft and iscola ---------------------------------------------------------------------

    /// <summary>What <c>stft</c> and <c>istft</c> read from their name–value pairs.</summary>
    private sealed class ShortTimeWords
    {
        public double[] Window { get; set; } = SignalWindows.Hann(128, periodic: true);

        public int? Overlap { get; set; }

        public int? Nfft { get; set; }

        public SpectralRange Range { get; set; } = SpectralRange.TwoSided;

        public bool Centred { get; set; } = true;

        public bool RangeNamed { get; set; }

        public bool CentredNamed { get; set; }

        public bool DownRows { get; set; }

        public OverlapAddMethod Method { get; set; } = OverlapAddMethod.Wola;

        public bool ConjugateSymmetric { get; set; }

        public double? SampleRate { get; set; }
    }

    /// <summary>
    /// The parser <c>stft</c> and <c>istft</c> share: an optional sample rate first, then pairs.
    /// </summary>
    private static ShortTimeWords ReadShortTimeWords(
        string name, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        var words = new ShortTimeWords();
        int i = start;
        if (i < args.Count && !IsTextScalar(args[i]))
        {
            if (ElementCount(args[i]) > 0)
            {
                double fs = Num(name, args, i, line, col);
                if (!(fs > 0) || !double.IsFinite(fs))
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a positive sample rate.");
                }

                words.SampleRate = fs;
            }

            i++;
        }

        for (; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, $"{name} takes only one value-only argument.");
            }

            string word = StrOf(name, args[i], line, col).ToLowerInvariant();
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            JgsValue value = args[i + 1];
            if (Matches(word, "window"))
            {
                words.Window = ToDoubles(name, value, line, col);
                if (words.Window.Length < 2)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a window longer than one sample.");
                }
            }
            else if (Matches(word, "overlaplength"))
            {
                words.Overlap = ElementCount(value) == 0 ? null : (int)Num(name, args, i + 1, line, col);
            }
            else if (Matches(word, "fftlength"))
            {
                words.Nfft = ElementCount(value) == 0 ? null : (int)Num(name, args, i + 1, line, col);
            }
            else if (Matches(word, "frequencyrange"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                if (Matches(given, "centered"))
                {
                    words.Range = SpectralRange.TwoSided;
                    words.Centred = true;
                }
                else if (Matches(given, "twosided"))
                {
                    words.Range = SpectralRange.TwoSided;
                    words.Centred = false;
                }
                else if (Matches(given, "onesided"))
                {
                    words.Range = SpectralRange.OneSided;
                    words.Centred = false;
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'onesided', 'twosided' or 'centered' as its frequency range.");
                }

                words.RangeNamed = true;
            }
            else if (Matches(word, "centered"))
            {
                bool centred = Num(name, args, i + 1, line, col) != 0;
                words.Range = SpectralRange.TwoSided;
                words.Centred = centred;
                words.CentredNamed = true;
            }
            else if (Matches(word, "outputtimedimension") || Matches(word, "inputtimedimension"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                words.DownRows = Matches(given, "downrows");
                if (!words.DownRows && !Matches(given, "acrosscolumns"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'acrosscolumns' or 'downrows' as its time dimension.");
                }
            }
            else if (Matches(word, "method"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                words.Method = Matches(given, "ola") ? OverlapAddMethod.Ola : OverlapAddMethod.Wola;
                if (!Matches(given, "ola") && !Matches(given, "wola"))
                {
                    throw new JgsRuntimeException(line, col, $"{name} takes 'ola' or 'wola' as its method.");
                }
            }
            else if (Matches(word, "conjugatesymmetric"))
            {
                words.ConjugateSymmetric = Num(name, args, i + 1, line, col) != 0;
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        if (words.RangeNamed && words.CentredNamed)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes either 'FrequencyRange' or 'Centered', not both.");
        }

        int nwin = words.Window.Length;
        words.Overlap ??= (int)System.Math.Floor(nwin * 0.75);
        words.Nfft ??= nwin;
        if (words.Overlap < 0 || words.Overlap >= nwin)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than the window.");
        }

        if (words.Nfft < nwin)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a transform at least as long as the window.");
        }

        return words;
    }

    /// <summary><c>[s, f, t] = stft(x, fs, ...)</c>.</summary>
    private static JgsValue[] ForwardShortTime(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 12, line, col);
        (Complex[][] channels, bool real, _) = SpectralChannels(name, args[0], line, col);
        ShortTimeWords words = ReadShortTimeWords(name, args, 1, line, col);
        if (words.Range == SpectralRange.OneSided && !real)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} has no one-sided transform for a complex signal.");
        }

        if (words.Window.Length > channels[0].Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window no longer than the signal.");
        }

        bool normalised = words.SampleRate is null;
        double fs = words.SampleRate ?? 2;
        var request = new ShortTimeRequest
        {
            Window = words.Window,
            Overlap = words.Overlap!.Value,
            Nfft = words.Nfft!.Value,
            SampleRate = fs,
            Range = words.Range,
            CenterDc = words.Centred,
            Scaling = SpectralScaling.Psd,
        };

        var maps = new Complex[channels.Length][][];
        double[] frequencies = [];
        double[] times = [];
        for (int c = 0; c < channels.Length; c++)
        {
            ShortTimeAnswer answer = ShortTimeTransforms.Transform(channels[c], request);
            maps[c] = answer.Values;
            frequencies = answer.Frequencies;
            times = answer.Times;
        }

        if (normalised)
        {
            // Normalized frequency is reported in radians per sample and time in samples.
            frequencies = TimesConstant(frequencies, System.Math.PI);
            times = TimesConstant(times, fs);
        }

        JgsValue map = channels.Length == 1
            ? FrameMatrix(maps[0], words.DownRows)
            : FramePages(maps, words.DownRows);
        if (wanted <= 1)
        {
            return [map];
        }

        if (wanted == 2)
        {
            return [map, ColumnOfDoubles(frequencies)];
        }

        return [map, ColumnOfDoubles(frequencies), ColumnOfDoubles(times)];
    }

    /// <summary><c>[x, t] = istft(s, fs, ...)</c>.</summary>
    private static JgsValue[] InverseShortTime(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 16, line, col);
        ShortTimeWords words = ReadShortTimeWords(name, args, 1, line, col);
        int nfft = words.Nfft!.Value;
        int keep = words.Range == SpectralRange.OneSided
            ? SpectralEstimation.OneSidedLength(nfft)
            : nfft;

        Complex[][] map = ShortTimeMap(name, args[0], words.DownRows, line, col);
        if (map.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one frame.");
        }

        if (map[0].Length != keep)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs {keep} frequency rows for a transform of {nfft} points.");
        }

        double fs = words.SampleRate ?? 2;
        int nwin = words.Window.Length;
        var frames = new Complex[map.Length][];
        for (int c = 0; c < map.Length; c++)
        {
            Complex[] column = Twosided(map[c], words, nfft, keep);
            bool symmetric = words.ConjugateSymmetric || ConjugateSymmetric(column);
            Fft.Transform(column, inverse: true);
            if (symmetric)
            {
                // An exactly conjugate-symmetric column has an exactly real inverse, and MATLAB's
                // kernel returns one; this build's transform leaves a rounding residue behind.
                for (int i = 0; i < column.Length; i++)
                {
                    column[i] = column[i].Real;
                }
            }

            frames[c] = column[..System.Math.Min(nwin, column.Length)];
        }

        (Complex[] signal, double[] times) = ShortTimeTransforms.OverlapAdd(
            frames, words.Window, words.Overlap!.Value, words.Method, fs);
        if (words.SampleRate is null)
        {
            times = TimesConstant(times, fs);
        }

        JgsValue x = ComplexShaped(signal, [signal.Length, 1]);
        return wanted <= 1 ? [x] : [x, ColumnOfDoubles(times)];
    }

    /// <summary><c>[tf, m, maxdev] = iscola(window, noverlap, method)</c>.</summary>
    private static JgsValue[] ColaCheck(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        double[] window = ToDoubles(name, args[0], line, col);
        if (window.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-empty window.");
        }

        int overlap = (int)Num(name, args, 1, line, col);
        if (overlap < 0 || overlap >= window.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than the window.");
        }

        OverlapAddMethod method = OverlapAddMethod.Wola;
        if (args.Count > 2)
        {
            string given = StrOf(name, args[2], line, col).ToLowerInvariant();
            method = Matches(given, "ola") ? OverlapAddMethod.Ola : OverlapAddMethod.Wola;
            if (!Matches(given, "ola") && !Matches(given, "wola"))
            {
                throw new JgsRuntimeException(line, col, $"{name} takes 'ola' or 'wola' as its method.");
            }
        }

        (bool constant, double[] sums, double deviation) =
            ShortTimeTransforms.ConstantOverlapAdd(window, overlap, method);
        if (wanted <= 1)
        {
            return [JgsValue.Bool(constant)];
        }

        double[] sorted = (double[])sums.Clone();
        Array.Sort(sorted);
        double median = (sorted.Length % 2) != 0
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
        return wanted == 2
            ? [JgsValue.Bool(constant), JgsValue.Number(median)]
            : [JgsValue.Bool(constant), JgsValue.Number(median), JgsValue.Number(deviation)];
    }

    // --- Shapes -------------------------------------------------------------------------------------

    /// <summary>
    /// MATLAB's <c>formatISTFTInput</c>: the two-sided column an inverse transform needs, rebuilt
    /// from whichever half or rotation the caller handed in.
    /// </summary>
    private static Complex[] Twosided(Complex[] column, ShortTimeWords words, int nfft, int keep)
    {
        if (words.Range == SpectralRange.OneSided)
        {
            var whole = new Complex[nfft];
            for (int i = 0; i < keep; i++)
            {
                whole[i] = column[i];
            }

            bool even = (nfft % 2) == 0;
            int last = even ? keep - 2 : keep - 1;
            for (int i = 1; i <= last; i++)
            {
                whole[nfft - i] = Complex.Conjugate(column[i]);
            }

            return whole;
        }

        if (!words.Centred)
        {
            return (Complex[])column.Clone();
        }

        var moved = new Complex[nfft];
        int shift = (nfft % 2) == 0 ? -((nfft / 2) - 1) : -(nfft / 2);
        for (int i = 0; i < nfft; i++)
        {
            int to = ((i + shift) % nfft + nfft) % nfft;
            moved[to] = column[i];
        }

        return moved;
    }

    /// <summary>Whether a column is its own conjugate reflection, exactly.</summary>
    private static bool ConjugateSymmetric(Complex[] column)
    {
        int n = column.Length;
        if (column[0].Imaginary != 0)
        {
            return false;
        }

        for (int i = 1; i <= n / 2; i++)
        {
            Complex mirror = column[n - i];
            if (column[i].Real != mirror.Real || column[i].Imaginary != -mirror.Imaginary)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a time–frequency map as one array per frame.</summary>
    private static Complex[][] ShortTimeMap(
        string name, JgsValue value, bool downRows, int line, int col)
    {
        Complex[] flat = ComplexArrayOf(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        int frames = downRows ? rows : columns;
        int bins = downRows ? columns : rows;
        var map = new Complex[frames][];
        for (int c = 0; c < frames; c++)
        {
            map[c] = new Complex[bins];
            for (int i = 0; i < bins; i++)
            {
                map[c][i] = downRows ? flat[c + (i * rows)] : flat[i + (c * rows)];
            }
        }

        return map;
    }

    /// <summary>Frames as a matrix: frequency down the rows unless the caller asked otherwise.</summary>
    private static JgsValue FrameMatrix(Complex[][] frames, bool downRows)
    {
        int nt = frames.Length;
        int nf = nt == 0 ? 0 : frames[0].Length;
        var flat = new Complex[nf * nt];
        for (int c = 0; c < nt; c++)
        {
            for (int i = 0; i < nf; i++)
            {
                flat[downRows ? c + (i * nt) : i + (c * nf)] = frames[c][i];
            }
        }

        return ComplexShaped(flat, downRows ? [nt, nf] : [nf, nt]);
    }

    /// <summary>The same, for a map whose values are real.</summary>
    private static JgsValue RealFrameMatrix(double[][] frames, bool downRows)
    {
        int nt = frames.Length;
        int nf = nt == 0 ? 0 : frames[0].Length;
        var flat = new double[nf * nt];
        for (int c = 0; c < nt; c++)
        {
            for (int i = 0; i < nf; i++)
            {
                flat[downRows ? c + (i * nt) : i + (c * nf)] = frames[c][i];
            }
        }

        return JgsMatrix.FromColumnMajor(flat, downRows ? nt : nf, downRows ? nf : nt);
    }

    /// <summary>The same, for a complex map already carried as magnitudes.</summary>
    private static JgsValue RealFrameMatrix(Complex[][] frames, bool downRows)
    {
        var real = new double[frames.Length][];
        for (int c = 0; c < frames.Length; c++)
        {
            real[c] = SpectralEstimators.RealPart(frames[c]);
        }

        return RealFrameMatrix(real, downRows);
    }

    /// <summary>One page per channel, which is how <c>stft</c> returns a multichannel signal.</summary>
    private static JgsValue FramePages(Complex[][][] maps, bool downRows)
    {
        int pages = maps.Length;
        int nt = maps[0].Length;
        int nf = nt == 0 ? 0 : maps[0][0].Length;
        int rows = downRows ? nt : nf;
        int columns = downRows ? nf : nt;
        var flat = new Complex[rows * columns * pages];
        for (int p = 0; p < pages; p++)
        {
            int offset = p * rows * columns;
            for (int c = 0; c < nt; c++)
            {
                for (int i = 0; i < nf; i++)
                {
                    flat[offset + (downRows ? c + (i * nt) : i + (c * nf))] = maps[p][c][i];
                }
            }
        }

        return ComplexShaped(flat, [rows, columns, pages]);
    }

    /// <summary>
    /// The window and overlap MATLAB's <c>welchparse</c> reads positionally, and which the
    /// spectrogram shares with <c>pwelch</c>: an integer window length means a Hamming window, an
    /// absent window means eight segments over the signal, and an absent overlap means half of one.
    /// </summary>
    private static (double[] Window, int Overlap, int Next) ReadFrames(
        string name, IReadOnlyList<JgsValue> args, int start, int n, int line, int col)
    {
        double[]? window = null;
        int? overlap = null;
        int at = start;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            if (ElementCount(args[at]) > 0)
            {
                double[] given = ToDoubles(name, args[at], line, col);
                if (given.Length == 1)
                {
                    if (given[0] <= 1 || given[0] != System.Math.Floor(given[0]))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name} needs a segment longer than one sample.");
                    }

                    window = SignalWindows.Hamming((int)given[0]);
                }
                else
                {
                    window = given;
                }
            }

            at++;
            if (args.Count > at && !IsTextScalar(args[at]))
            {
                if (ElementCount(args[at]) > 0)
                {
                    overlap = (int)Num(name, args, at, line, col);
                }

                at++;
            }
        }

        if (window is null)
        {
            int segment = overlap is null ? (int)(n / 4.5) : (int)((n + (7.0 * overlap.Value)) / 8);
            if (segment < 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs a signal long enough for its default segments.");
            }

            window = SignalWindows.Hamming(segment);
        }

        overlap ??= window.Length / 2;
        if (overlap.Value >= window.Length || overlap.Value < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than the segment.");
        }

        return (window, overlap.Value, at);
    }

    /// <summary>
    /// <c>[x, t, info] = stftmag2sig(S, nfft, fs, ...)</c>: the signal whose short-time transform
    /// has the given magnitudes, found by alternating between the two domains.
    /// </summary>
    /// <remarks>
    /// Griffin and Lim's iteration keeps the magnitudes the caller gave and takes the phase from
    /// whatever the last synthesis-and-analysis round trip produced. The fast variant adds a
    /// fraction of the previous step's inconsistency before taking that phase, which is momentum.
    /// </remarks>
    private static JgsValue[] MagnitudeToSignal(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 27, line, col);
        int nfft = (int)Num(name, args, 1, line, col);
        int at = 2;
        double? fs = null;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            if (ElementCount(args[at]) > 0)
            {
                fs = Num(name, args, at, line, col);
            }

            at++;
        }

        double[] window = SignalWindows.Hann(128, periodic: true);
        int? overlap = null;
        var range = SpectralRange.TwoSided;
        bool centred = true;
        bool fast = false;
        bool downRows = false;
        int maximum = 100;
        double tolerance = 1e-4;
        double momentum = 0.99;
        for (; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            JgsValue value = args[at + 1];
            if (Matches(word, "window"))
            {
                window = ToDoubles(name, value, line, col);
            }
            else if (Matches(word, "overlaplength"))
            {
                overlap = ElementCount(value) == 0 ? null : (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "frequencyrange"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                if (Matches(given, "onesided"))
                {
                    range = SpectralRange.OneSided;
                    centred = false;
                }
                else if (Matches(given, "twosided"))
                {
                    range = SpectralRange.TwoSided;
                    centred = false;
                }
                else if (Matches(given, "centered"))
                {
                    range = SpectralRange.TwoSided;
                    centred = true;
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'onesided', 'twosided' or 'centered' as its frequency range.");
                }
            }
            else if (Matches(word, "method"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                if (Matches(given, "fgla"))
                {
                    fast = true;
                }
                else if (!Matches(given, "gla"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build reconstructs by 'gla' or 'fgla'; " +
                        "'legla' and 'gd' are not written.");
                }
            }
            else if (Matches(word, "maxiterations"))
            {
                maximum = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "inconsistencytolerance"))
            {
                tolerance = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "updateparameter"))
            {
                momentum = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "inputtimedimension"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                downRows = Matches(given, "downrows");
            }
            else if (Matches(word, "initializephasemethod"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                if (!Matches(given, "zeros"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build starts from zero phase only.");
                }
            }
            else if (Matches(word, "display") || Matches(word, "initialphase")
                || Matches(word, "truncationorder"))
            {
                continue;
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        overlap ??= (int)System.Math.Floor(window.Length * 0.75);
        Complex[][] target = ShortTimeMap(name, args[0], downRows, line, col);
        int rows = target.Length == 0 ? 0 : target[0].Length;
        int expected = range == SpectralRange.OneSided
            ? SpectralEstimation.OneSidedLength(nfft)
            : nfft;
        if (rows != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs {expected} magnitude rows for a transform of {nfft} points.");
        }

        foreach (Complex[] column in target)
        {
            foreach (Complex value in column)
            {
                if (value.Real < 0 || value.Imaginary != 0)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs non-negative magnitudes.");
                }
            }
        }

        var estimate = new Complex[target.Length][];
        var step = new Complex[target.Length][];
        double energy = 0;
        for (int c = 0; c < target.Length; c++)
        {
            estimate[c] = new Complex[rows];
            step[c] = new Complex[rows];
            for (int i = 0; i < rows; i++)
            {
                estimate[c][i] = target[c][i].Real;
                energy += target[c][i].Real * target[c][i].Real;
            }
        }

        int cells = System.Math.Max(target.Length * rows, 1);
        double norm = System.Math.Sqrt(energy / cells);
        double inconsistency = double.PositiveInfinity;
        int iterations = 0;
        while (iterations < maximum && inconsistency > tolerance)
        {
            Complex[] signal = ShortTimeTransforms.Synthesise(
                estimate, window, overlap.Value, nfft, range, centred, OverlapAddMethod.Wola);
            for (int i = 0; i < signal.Length; i++)
            {
                signal[i] = signal[i].Real;
            }

            Complex[][] fresh = ShortTimeTransforms.Analyse(
                signal, window, overlap.Value, nfft, range, centred);
            double sum = 0;
            for (int c = 0; c < estimate.Length; c++)
            {
                for (int i = 0; i < rows; i++)
                {
                    Complex seen = c < fresh.Length ? fresh[c][i] : Complex.Zero;
                    Complex guide = fast ? seen + (step[c][i] * momentum) : seen;
                    double size = System.Math.Max(2.220446049250313e-16, guide.Magnitude);
                    Complex updated = target[c][i].Real * guide / size;
                    Complex delta = updated - seen;
                    step[c][i] = delta;
                    estimate[c][i] = updated;
                    sum += (delta.Real * delta.Real) + (delta.Imaginary * delta.Imaginary);
                }
            }

            inconsistency = System.Math.Sqrt(sum / cells) / norm;
            iterations++;
        }

        Complex[] answer = ShortTimeTransforms.Synthesise(
            estimate, window, overlap.Value, nfft, range, centred, OverlapAddMethod.Wola);
        var x = new double[answer.Length];
        var times = new double[answer.Length];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = answer[i].Real;
            times[i] = fs is null ? i : i / fs.Value;
        }

        if (wanted <= 1)
        {
            return [ColumnOfDoubles(x)];
        }

        return [ColumnOfDoubles(x), ColumnOfDoubles(times)];
    }

    /// <summary>A row vector of doubles, which is how the spectrogram's times come back.</summary>
    private static JgsValue RowOfDoubles(double[] values) =>
        JgsMatrix.FromColumnMajor(values, 1, values.Length);

    private static double[] TimesConstant(double[] values, double by)
    {
        var scaled = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            scaled[i] = values[i] * by;
        }

        return scaled;
    }

    /// <summary>Whether an option word is a prefix of the one it is being tested against.</summary>
    private static bool Matches(string given, string candidate) =>
        given.Length > 0 && given.Length <= candidate.Length
        && candidate.StartsWith(given, StringComparison.Ordinal);
}
