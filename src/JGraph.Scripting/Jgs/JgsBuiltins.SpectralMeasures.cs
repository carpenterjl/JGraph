using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The measurements taken off a spectrum (M136): how much power a band holds, where the power sits,
/// how wide it is, and how much of it is distortion rather than signal.
/// </summary>
/// <remarks>
/// These names take their input three ways — a signal, a power spectral density, or a power spectrum
/// with its resolution bandwidth beside it — and MATLAB tells them apart by the shape of the second
/// argument and by a trailing <c>'psd'</c> or <c>'power'</c>. That reading is written once here, as
/// <c>psdparserange</c> writes it once there, because getting it wrong turns a sample rate into a
/// frequency vector without complaint.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the spectral measurements.</summary>
    internal static void RegisterSpectralMeasureBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Measure(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Define("enbw", NoiseBandwidth);
        Define("bandpower", BandPowerOf);
        Measure("meanfreq", BandStatistic);
        Measure("medfreq", BandStatistic);
        Measure("obw", BandStatistic);
        Measure("powerbw", BandStatistic);
        Measure("thd", HarmonicMeasure);
        Measure("snr", HarmonicMeasure);
        Measure("sinad", HarmonicMeasure);
        Measure("sfdr", HarmonicMeasure);
        Measure("toi", HarmonicMeasure);
    }

    /// <summary><c>bw = enbw(window, fs)</c>.</summary>
    private static JgsValue NoiseBandwidth(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("enbw", args, 1, 2, line, col);
        double[] window = ToDoubles("enbw", args[0], line, col);
        double? fs = args.Count > 1 ? Num("enbw", args, 1, line, col) : null;
        return JgsValue.Number(SpectralMeasurements.EquivalentNoiseBandwidth(window, fs));
    }

    /// <summary><c>bandpower(x)</c>, <c>bandpower(x, fs, range)</c> and <c>bandpower(pxx, f, ..., 'psd')</c>.</summary>
    private static JgsValue BandPowerOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string name = "bandpower";
        ArityRange(name, args, 1, 4, line, col);
        bool fromPsd = false;
        var numeric = new List<JgsValue>();
        foreach (JgsValue value in args)
        {
            if (IsTextScalar(value))
            {
                string word = StrOf(name, value, line, col).ToLowerInvariant();
                if (word != "psd")
                {
                    throw new JgsRuntimeException(line, col, $"{name} knows only the flag 'psd'.");
                }

                fromPsd = true;
                continue;
            }

            numeric.Add(value);
        }

        if (fromPsd)
        {
            if (numeric.Count < 2)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a frequency vector with a spectrum.");
            }

            double[][] columns = MeasurementColumns(name, numeric[0], line, col);
            double[] f = ToDoubles(name, numeric[1], line, col);
            double[]? range = numeric.Count > 2 ? ToDoubles(name, numeric[2], line, col) : null;
            var powers = new double[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                powers[c] = SpectralMeasurements.BandPower(columns[c], f, range);
            }

            return powers.Length == 1 ? JgsValue.Number(powers[0]) : JgsMatrix.FromColumnMajor(powers, 1, powers.Length);
        }

        Complex[] flat = ComplexArrayOf(name, numeric[0], line, col);
        int[] dims = SizeDims(numeric[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columnCount = flat.Length / System.Math.Max(rows, 1);
        if (rows == 1)
        {
            (rows, columnCount) = (columnCount, 1);
        }

        if (numeric.Count == 1)
        {
            var mean = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                double sum = 0;
                for (int r = 0; r < rows; r++)
                {
                    Complex z = flat[(c * rows) + r];
                    sum += (Complex.Conjugate(z) * z).Real;
                }

                mean[c] = sum / rows;
            }

            return mean.Length == 1 ? JgsValue.Number(mean[0]) : JgsMatrix.FromColumnMajor(mean, 1, mean.Length);
        }

        if (numeric.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a sample rate and a frequency range together, or neither.");
        }

        double fs = Num(name, numeric, 1, line, col);
        double[] band = ToDoubles(name, numeric[2], line, col);
        bool real = flat.All(static z => z.Imaginary == 0);
        var request = new SpectralRequest
        {
            Nfft = rows,
            SampleRate = fs,
            SampleRateGiven = true,
            Range = real ? SpectralRange.OneSided : SpectralRange.TwoSided,
            CenterDc = !real,
            Window = SignalWindows.Hamming(rows),
        };
        var channels = new Complex[columnCount][];
        for (int c = 0; c < columnCount; c++)
        {
            channels[c] = new Complex[rows];
            Array.Copy(flat, c * rows, channels[c], 0, rows);
        }

        SpectralAnswer answer = SpectralEstimators.Periodogram(channels, real, request);
        var result = new double[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            result[c] = SpectralMeasurements.BandPower(
                SpectralEstimators.RealPart(answer.Values[c]), answer.Frequencies, band);
        }

        return result.Length == 1 ? JgsValue.Number(result[0]) : JgsMatrix.FromColumnMajor(result, 1, result.Length);
    }

    /// <summary>A real matrix read as one column per channel.</summary>
    private static double[][] MeasurementColumns(string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        if (rows == 1)
        {
            (rows, columns) = (columns, 1);
        }

        var result = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            result[c] = new double[rows];
            Array.Copy(flat, c * rows, result[c], 0, rows);
        }

        return result;
    }

    /// <summary>What <c>psdparserange</c> works out: the density, its frequencies, and the band asked for.</summary>
    private sealed record MeasurementInput(
        double[][] Columns, double[] Frequencies, double[]? Range, double Rbw,
        List<JgsValue> Extra, bool FromTime, bool HasNyquist);

    /// <summary>
    /// MATLAB's <c>psdparserange</c>: a second argument that is empty or scalar makes the first a
    /// signal; a scalar third makes the first a power spectrum; anything else makes it a density.
    /// </summary>
    private static MeasurementInput ReadMeasurementInput(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int n = args.Count;
        bool fromTime = n == 1
            || ElementCount(args[1]) == 0
            || (!IsTextScalar(args[1]) && ElementCount(args[1]) == 1);
        var extra = new List<JgsValue>();
        if (fromTime)
        {
            Complex[] flat = ComplexArrayOf(name, args[0], line, col);
            int[] dims = SizeDims(args[0]);
            int rows = dims.Length > 0 ? dims[0] : 1;
            int columns = flat.Length / System.Math.Max(rows, 1);
            if (rows == 1)
            {
                (rows, columns) = (columns, 1);
            }

            double fs = n > 1 && ElementCount(args[1]) > 0 ? Num(name, args, 1, line, col) : 2 * System.Math.PI;
            bool given = n > 1 && ElementCount(args[1]) > 0;
            double[] window = SignalWindows.Kaiser(rows, 0);
            double rbw = SpectralMeasurements.EquivalentNoiseBandwidth(window, fs);
            bool real = flat.All(static z => z.Imaginary == 0);
            var channels = new Complex[columns][];
            for (int c = 0; c < columns; c++)
            {
                channels[c] = new Complex[rows];
                Array.Copy(flat, c * rows, channels[c], 0, rows);
            }

            var request = new SpectralRequest
            {
                Nfft = rows,
                SampleRate = fs,
                SampleRateGiven = given,
                Range = real ? SpectralRange.OneSided : SpectralRange.TwoSided,
                CenterDc = !real,
                Window = window,
            };
            SpectralAnswer answer = SpectralEstimators.Periodogram(channels, real, request);
            var columnsOut = new double[columns][];
            for (int c = 0; c < columns; c++)
            {
                columnsOut[c] = SpectralEstimators.RealPart(answer.Values[c]);
            }

            double[]? range = n > 2 && ElementCount(args[2]) > 0
                ? ToDoubles(name, args[2], line, col)
                : null;
            for (int i = 3; i < n; i++)
            {
                extra.Add(args[i]);
            }

            return new MeasurementInput(
                columnsOut, answer.Frequencies, range, rbw, extra, true, rows % 2 == 0);
        }

        double[][] density = MeasurementColumns(name, args[0], line, col);
        double[] f = ToDoubles(name, args[1], line, col);
        bool isPower = n > 2 && !IsTextScalar(args[2]) && ElementCount(args[2]) == 1;
        double bandwidth = double.NaN;
        int at = 2;
        if (isPower)
        {
            bandwidth = Num(name, args, 2, line, col);
            for (int c = 0; c < density.Length; c++)
            {
                for (int i = 0; i < density[c].Length; i++)
                {
                    density[c][i] /= bandwidth;
                }
            }

            at = 3;
        }

        double[]? band = at < n && ElementCount(args[at]) > 0 && !IsTextScalar(args[at])
            && ElementCount(args[at]) == 2
                ? ToDoubles(name, args[at], line, col)
                : null;
        if (band is not null)
        {
            at++;
        }

        for (int i = at; i < n; i++)
        {
            extra.Add(args[i]);
        }

        return new MeasurementInput(density, f, band, bandwidth, extra, false, false);
    }

    /// <summary><c>meanfreq</c>, <c>medfreq</c>, <c>obw</c> and <c>powerbw</c>.</summary>
    private static JgsValue[] BandStatistic(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 5, line, col);
        MeasurementInput input = ReadMeasurementInput(name, args, line, col);
        double[] f = input.Frequencies;
        double[] range = input.Range ?? [f[0], f[^1]];
        int channels = input.Columns.Length;
        var first = new double[channels];
        var second = new double[channels];
        var third = new double[channels];
        var fourth = new double[channels];

        double extra = name switch
        {
            "obw" => input.Extra.Count > 0 ? Num(name, input.Extra, 0, line, col) : 99,
            "powerbw" => input.Extra.Count > 0
                ? -System.Math.Abs(Num(name, input.Extra, 0, line, col))
                : 10 * System.Math.Log10(0.5),
            _ => 0,
        };

        for (int c = 0; c < channels; c++)
        {
            switch (name)
            {
                case "meanfreq":
                    (first[c], second[c]) = SpectralMeasurements.MeanFrequency(
                        input.Columns[c], f, range[0], range[1]);
                    break;
                case "medfreq":
                    (first[c], second[c]) = SpectralMeasurements.MedianFrequency(
                        input.Columns[c], f, range[0], range[1]);
                    break;
                case "obw":
                    (first[c], second[c], third[c], fourth[c]) = SpectralMeasurements.OccupiedBandwidth(
                        input.Columns[c], f, range[0], range[1], extra);
                    break;
                default:
                    (first[c], second[c], third[c], fourth[c]) = SpectralMeasurements.PowerBandwidth(
                        input.Columns[c], f, input.Range, extra, input.HasNyquist, input.FromTime);
                    break;
            }
        }

        JgsValue Row(double[] values) =>
            values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, 1, values.Length);

        if (name is "meanfreq" or "medfreq")
        {
            return wanted <= 1 ? [Row(first)] : [Row(first), Row(second)];
        }

        return wanted switch
        {
            <= 1 => [Row(first)],
            2 => [Row(first), Row(second)],
            3 => [Row(first), Row(second), Row(third)],
            _ => [Row(first), Row(second), Row(third), Row(fourth)],
        };
    }

    /// <summary><c>thd</c>, <c>snr</c>, <c>sinad</c>, <c>sfdr</c> and <c>toi</c>.</summary>
    private static JgsValue[] HarmonicMeasure(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 6, line, col);
        bool aliased = false;
        string kind = "time";
        var numeric = new List<JgsValue>();
        foreach (JgsValue value in args)
        {
            if (!IsTextScalar(value))
            {
                numeric.Add(value);
                continue;
            }

            string word = StrOf(name, value, line, col).ToLowerInvariant();
            switch (word)
            {
                case "psd":
                case "power":
                case "time":
                    kind = word;
                    break;
                case "aliased":
                    aliased = true;
                    break;
                case "omitaliases":
                    aliased = false;
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        // snr's two-signal form: a second array rather than a sample rate is the noise itself.
        if (name == "snr" && kind == "time" && numeric.Count == 2 && ElementCount(numeric[1]) > 1)
        {
            double[] signal = ToDoubles(name, numeric[0], line, col);
            double[] noise = ToDoubles(name, numeric[1], line, col);
            if (signal.Length != noise.Length)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs two arrays of the same size.");
            }

            double signalPower = signal.Sum(static v => v * v);
            double noisePower = noise.Sum(static v => v * v);
            JgsValue ratio = JgsValue.Number(10 * System.Math.Log10(signalPower / noisePower));
            return wanted <= 1
                ? [ratio]
                : [ratio, JgsValue.Number(10 * System.Math.Log10(noisePower))];
        }

        double[] pxx;
        double[] f;
        double rbw;
        int at;
        if (kind == "time")
        {
            double[] x = ToDoubles(name, numeric[0], line, col);
            double fs = numeric.Count > 1 && ElementCount(numeric[1]) > 0
                ? Num(name, numeric, 1, line, col)
                : 1;
            at = 2;
            int n = x.Length;
            double[] centred = x;
            if (name is "thd" or "toi")
            {
                double mean = x.Average();
                centred = new double[n];
                for (int i = 0; i < n; i++)
                {
                    centred[i] = x[i] - mean;
                }
            }

            double[] window = SignalWindows.Kaiser(n, 38);
            rbw = SpectralMeasurements.EquivalentNoiseBandwidth(window, fs);
            var channels = new Complex[1][];
            channels[0] = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                channels[0][i] = centred[i];
            }

            SpectralAnswer answer = SpectralEstimators.Periodogram(channels, true, new SpectralRequest
            {
                Nfft = n,
                SampleRate = fs,
                SampleRateGiven = true,
                Range = SpectralRange.OneSided,
                Window = window,
            });
            pxx = SpectralEstimators.RealPart(answer.Values[0]);
            f = answer.Frequencies;
        }
        else
        {
            pxx = ToDoubles(name, numeric[0], line, col);
            f = ToDoubles(name, numeric[1], line, col);
            if (f.Length == 0 || f[0] != 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a one-sided spectrum starting at zero.");
            }

            double spacing = (f[^1] - f[0]) / (f.Length - 1);
            if (kind == "power")
            {
                rbw = Num(name, numeric, 2, line, col);
                var scaled = new double[pxx.Length];
                for (int i = 0; i < pxx.Length; i++)
                {
                    scaled[i] = pxx[i] / rbw;
                }

                pxx = scaled;
                at = 3;
            }
            else
            {
                rbw = spacing;
                at = 2;
            }
        }

        double? aliasFs = aliased ? 2 * f[^1] : null;
        if (aliased && kind == "time")
        {
            aliasFs = 2 * f[^1];
        }

        int harmonics = 6;
        double separation = 0;
        if (numeric.Count > at && ElementCount(numeric[at]) > 0)
        {
            double given = Num(name, numeric, at, line, col);
            if (name == "sfdr")
            {
                separation = given;
            }
            else
            {
                harmonics = (int)given;
                if (harmonics <= 1)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs more than one harmonic.");
                }
            }
        }

        switch (name)
        {
            case "thd":
            {
                (double ratio, double[] powers, double[] frequencies) =
                    HarmonicMeasurements.TotalHarmonicDistortion(pxx, f, rbw, harmonics, aliasFs);
                return wanted switch
                {
                    <= 1 => [JgsValue.Number(ratio)],
                    2 => [JgsValue.Number(ratio), ColumnOfDoubles(powers)],
                    _ => [JgsValue.Number(ratio), ColumnOfDoubles(powers), ColumnOfDoubles(frequencies)],
                };
            }

            case "snr":
            {
                (double ratio, double noise) =
                    HarmonicMeasurements.SignalToNoise(pxx, f, rbw, harmonics, aliasFs);
                return wanted <= 1 ? [JgsValue.Number(ratio)] : [JgsValue.Number(ratio), JgsValue.Number(noise)];
            }

            case "sinad":
            {
                (double ratio, double noise) =
                    HarmonicMeasurements.SignalToNoiseAndDistortion(pxx, f, rbw);
                return wanted <= 1 ? [JgsValue.Number(ratio)] : [JgsValue.Number(ratio), JgsValue.Number(noise)];
            }

            case "sfdr":
            {
                (double ratio, double power, double frequency) =
                    HarmonicMeasurements.SpuriousFreeRange(pxx, f, rbw, separation);
                return wanted switch
                {
                    <= 1 => [JgsValue.Number(ratio)],
                    2 => [JgsValue.Number(ratio), JgsValue.Number(power)],
                    _ => [JgsValue.Number(ratio), JgsValue.Number(power), JgsValue.Number(frequency)],
                };
            }

            default:
            {
                (double intercept, double[] fundamentals, double[] frequencies,
                    double[] modulation, double[] modulationFrequencies) =
                    HarmonicMeasurements.ThirdOrderIntercept(pxx, f, rbw);
                JgsValue Row(double[] values) => JgsMatrix.FromColumnMajor(values, 1, values.Length);
                return wanted switch
                {
                    <= 1 => [JgsValue.Number(intercept)],
                    2 => [JgsValue.Number(intercept), Row(fundamentals)],
                    3 => [JgsValue.Number(intercept), Row(fundamentals), Row(frequencies)],
                    4 => [JgsValue.Number(intercept), Row(fundamentals), Row(frequencies), Row(modulation)],
                    _ =>
                    [
                        JgsValue.Number(intercept), Row(fundamentals), Row(frequencies),
                        Row(modulation), Row(modulationFrequencies),
                    ],
                };
            }
        }
    }
}
