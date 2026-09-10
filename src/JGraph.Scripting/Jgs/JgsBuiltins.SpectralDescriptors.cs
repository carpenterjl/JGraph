using JGraph.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The spectral descriptors of M137: <c>spectralKurtosis</c>, <c>spectralSkewness</c>,
/// <c>spectralFlatness</c>, <c>spectralCrest</c> and <c>spectralEntropy</c>.
/// </summary>
/// <remarks>
/// All five read their arguments the same way and differ only in the number they take from each
/// frame's spectrum. The second argument decides everything else: a scalar is a sample rate and
/// the first argument is a signal to be framed; a vector is a frequency axis and the first
/// argument is already a spectrogram.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the spectral descriptor names.</summary>
    internal static void RegisterSpectralDescriptorBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("spectralKurtosis", Descriptor);
        Many("spectralSkewness", Descriptor);
        Many("spectralFlatness", Descriptor);
        Many("spectralCrest", Descriptor);
        Many("spectralEntropy", Descriptor);
        Many("kurtogram", Kurtogram);
    }

    /// <summary>
    /// <c>[kgram, f, w, fc, wc, bw] = kurtogram(x, fs, level)</c>: the fast kurtogram and the band
    /// it says carries the most impulsive content.
    /// </summary>
    private static JgsValue[] Kurtogram(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        if (x.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least two samples.");
        }

        double fs = 1;
        int level = -1;
        int at = 1;
        if (args.Count > at && ElementCount(args[at]) > 0)
        {
            double[] given = ToDoubles(name, args[at], line, col);
            if (given.Length > 1)
            {
                // A time vector rather than a rate: the rate is the reciprocal of its step.
                fs = (given.Length - 1) / (given[^1] - given[0]);
            }
            else
            {
                fs = given[0];
            }

            if (!(fs > 0) || !double.IsFinite(fs))
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a positive sample rate.");
            }

            at++;
        }

        if (args.Count > at && ElementCount(args[at]) > 0)
        {
            double given = Num(name, args, at, line, col);
            if (given < 0 || given != System.Math.Floor(given))
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a non-negative whole level.");
            }

            level = (int)given;
        }

        KurtogramAnswer answer = FastKurtogram.Compute(x, fs, level);
        int rows = answer.Map.GetLength(0);
        int columns = answer.Map.GetLength(1);
        var flat = new double[rows * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[r + (c * rows)] = answer.Map[r, c];
            }
        }

        var outputs = new List<JgsValue>
        {
            JgsMatrix.FromColumnMajor(flat, rows, columns),
            ColumnOfDoubles(answer.Frequencies),
            ColumnOfDoubles(answer.Windows),
            JgsValue.Number(answer.Centre),
            JgsValue.Number(answer.Window),
            JgsValue.Number(answer.Bandwidth),
        };
        return [.. outputs.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary>The five descriptors, which share everything but their last few lines.</summary>
    private static JgsValue[] Descriptor(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 16, line, col);
        var request = new DescriptorRequest();
        bool scaled = true;
        bool instantaneous = true;
        double? confidence = null;
        double[]? times = null;

        int at = 1;
        double[] axis = args.Count > 1 && !IsTextScalar(args[1])
            ? ToDoubles(name, args[1], line, col)
            : [1];
        if (args.Count > 1 && !IsTextScalar(args[1]))
        {
            at = 2;
            if (name == "spectralEntropy" && args.Count > 2 && !IsTextScalar(args[2]))
            {
                times = ToDoubles(name, args[2], line, col);
                at = 3;
            }
        }

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
                request.Window = ElementCount(value) == 0 ? null : ToDoubles(name, value, line, col);
            }
            else if (Matches(word, "overlaplength"))
            {
                request.Overlap = ElementCount(value) == 0 ? null : (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "fftlength"))
            {
                request.Nfft = ElementCount(value) == 0 ? null : (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "spectrumtype"))
            {
                string given = StrOf(name, value, line, col).ToLowerInvariant();
                request.Power = Matches(given, "power");
                if (!request.Power && !Matches(given, "magnitude"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'power' or 'magnitude' as its spectrum type.");
                }
            }
            else if (Matches(word, "range"))
            {
                request.Range = ElementCount(value) == 0 ? null : ToDoubles(name, value, line, col);
            }
            else if (Matches(word, "scaled"))
            {
                scaled = Num(name, args, at + 1, line, col) != 0;
            }
            else if (Matches(word, "instantaneous") && name == "spectralEntropy")
            {
                instantaneous = Num(name, args, at + 1, line, col) != 0;
            }
            else if (Matches(word, "timelimits") && name == "spectralEntropy")
            {
                // The limits narrow the frames a scalar entropy is taken over; the frames
                // themselves are unchanged.
                times ??= [];
            }
            else if (Matches(word, "confidencelevel") && name == "spectralKurtosis")
            {
                confidence = Num(name, args, at + 1, line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        if (confidence is not null && scaled)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a confidence level only when 'Scaled' is false.");
        }

        (double[][] columns, double[] frequencies, double[] frameTimes, int channels) =
            DescriptorInput(name, args[0], axis, request, line, col);
        int frames = channels == 0 ? 0 : columns.Length / channels;

        if (name == "spectralKurtosis" && !scaled)
        {
            return UnscaledKurtosis(columns, frequencies, frames, channels,
                confidence ?? 0.95, wanted);
        }

        var first = new double[columns.Length];
        var second = new double[columns.Length];
        var third = new double[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            switch (name)
            {
                case "spectralKurtosis":
                {
                    (double centroid, double spread, _, double kurtosis) =
                        SpectralDescriptors.Moments(columns[i], frequencies);
                    first[i] = kurtosis;
                    second[i] = spread;
                    third[i] = centroid;
                    break;
                }

                case "spectralSkewness":
                {
                    (double centroid, double spread, double skewness, _) =
                        SpectralDescriptors.Moments(columns[i], frequencies);
                    first[i] = skewness;
                    second[i] = spread;
                    third[i] = centroid;
                    break;
                }

                case "spectralFlatness":
                {
                    (double flatness, double arithmetic, double geometric) =
                        SpectralDescriptors.Flatness(columns[i]);
                    first[i] = flatness;
                    second[i] = arithmetic;
                    third[i] = geometric;
                    break;
                }

                case "spectralCrest":
                {
                    (double crest, double peak, double mean) = SpectralDescriptors.Crest(columns[i]);
                    first[i] = crest;
                    second[i] = peak;
                    third[i] = mean;
                    break;
                }

                default:
                    first[i] = SpectralDescriptors.Entropy(columns[i], scaled);
                    break;
            }
        }

        if (name == "spectralEntropy")
        {
            if (!instantaneous)
            {
                return EntropyOverAll(columns, frames, channels, scaled, wanted, frameTimes, times);
            }

            JgsValue values = JgsMatrix.FromColumnMajor(first, frames, channels);
            return wanted <= 1
                ? [values]
                : [values, RowOfDoubles(times ?? frameTimes)];
        }

        JgsValue a = JgsMatrix.FromColumnMajor(first, frames, channels);
        if (wanted <= 1)
        {
            return [a];
        }

        JgsValue b = JgsMatrix.FromColumnMajor(second, frames, channels);
        return wanted == 2 ? [a, b] : [a, b, JgsMatrix.FromColumnMajor(third, frames, channels)];
    }

    /// <summary>
    /// MATLAB's <c>computeSpectralKurtosis</c>: the fourth moment over the square of the second,
    /// taken along the frames rather than across the spectrum, with the bias of a finite record
    /// taken out and a normal threshold beside it.
    /// </summary>
    private static JgsValue[] UnscaledKurtosis(
        double[][] columns, double[] frequencies, int frames, int channels, double confidence, int wanted)
    {
        int rows = frequencies.Length;
        var kurtosis = new double[rows * channels];
        var spread = new double[rows * channels];
        var centroid = new double[rows * channels];
        for (int c = 0; c < channels; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                double m2 = 0;
                double m4 = 0;
                for (int h = 0; h < frames; h++)
                {
                    double value = columns[h + (c * frames)][i];
                    m2 += value;
                    m4 += value * value;
                }

                m2 /= frames;
                m4 /= frames;
                double correction = frames < 2 ? 1 : (frames + 1.0) / (frames - 1.0);
                kurtosis[i + (c * rows)] = (correction * m4 / (m2 * m2)) - 2;
                centroid[i + (c * rows)] = m2;

                double variance = 0;
                for (int h = 0; h < frames; h++)
                {
                    double d = columns[h + (c * frames)][i] - m2;
                    variance += d * d;
                }

                spread[i + (c * rows)] = frames < 2 ? 0 : System.Math.Sqrt(variance / (frames - 1));
            }
        }

        double alpha = 1 - confidence;
        double threshold = -System.Math.Sqrt(2) * ErrorFunctions.ErfcInverse(2 * (1 - (alpha / 2)))
            * 2 / System.Math.Sqrt(frames);
        var outputs = new List<JgsValue>
        {
            JgsMatrix.FromColumnMajor(kurtosis, rows, channels),
            JgsMatrix.FromColumnMajor(spread, rows, channels),
            JgsMatrix.FromColumnMajor(centroid, rows, channels),
            JgsValue.Number(threshold),
            ColumnOfDoubles(frequencies),
        };
        return [.. outputs.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary>The entropy of the whole record rather than of each frame.</summary>
    private static JgsValue[] EntropyOverAll(
        double[][] columns,
        int frames,
        int channels,
        bool scaled,
        int wanted,
        double[] frameTimes,
        double[]? times)
    {
        int rows = columns.Length == 0 ? 0 : columns[0].Length;
        var values = new double[channels];
        for (int c = 0; c < channels; c++)
        {
            var pooled = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                for (int h = 0; h < frames; h++)
                {
                    pooled[i] += columns[h + (c * frames)][i];
                }
            }

            values[c] = SpectralDescriptors.Entropy(pooled, scaled);
        }

        JgsValue entropy = JgsMatrix.FromColumnMajor(values, 1, channels);
        return wanted <= 1 ? [entropy] : [entropy, RowOfDoubles(times ?? frameTimes)];
    }

    /// <summary>
    /// The frames a descriptor works on: a signal is framed and transformed, and a spectrogram is
    /// taken as it stands.
    /// </summary>
    private static (double[][] Columns, double[] Frequencies, double[] Times, int Channels) DescriptorInput(
        string name, JgsValue value, double[] axis, DescriptorRequest request, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        int pages = 1;
        for (int i = 2; i < dims.Length; i++)
        {
            pages *= dims[i];
        }

        if (axis.Length == 1)
        {
            double fs = axis[0];
            bool vector = rows == 1 || columns == 1;
            int length = vector ? flat.Length : rows;
            int channels = vector ? 1 : columns;
            var signals = new double[channels][];
            for (int c = 0; c < channels; c++)
            {
                signals[c] = new double[length];
                Array.Copy(flat, c * length, signals[c], 0, length);
            }

            if (length < 2)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least two samples.");
            }

            DescriptorFrames framed = SpectralDescriptors.Frames(signals, fs, request);
            return (framed.Columns, framed.Frequencies, framed.Times, channels);
        }

        if (rows != axis.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one frequency for every row of the spectrum.");
        }

        int total = columns * pages;
        var kept = new double[total][];
        for (int i = 0; i < total; i++)
        {
            kept[i] = new double[rows];
            Array.Copy(flat, i * rows, kept[i], 0, rows);
        }

        var frameTimes = new double[columns];
        for (int i = 0; i < columns; i++)
        {
            frameTimes[i] = i;
        }

        return (kept, axis, frameTimes, pages);
    }
}
