using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Thomson's multitaper estimate (M136): the average of the periodograms a family of orthogonal
/// tapers produces, weighted so that each taper counts for as much as its own leakage allows.
/// </summary>
/// <remarks>
/// The tapers are the Slepian sequences by default — <c>dpss</c>, which M132 already writes — and
/// the sine tapers when asked for. The weighting is the interesting part: the adaptive one is a
/// fixed-point iteration in which each taper's weight depends on the spectrum the weights produce,
/// run until the estimate settles.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>pmtm</c>.</summary>
    internal static void RegisterMultitaperBuiltins(JgsEnvironment env)
    {
        env.Builtins.Register("pmtm", JgsValue.Function(new BuiltinFunction(
            "pmtm", (args, line, col) => MultitaperSpectrum(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => MultitaperSpectrum(args, wanted, line, col),
        }));
    }

    /// <summary><c>[pxx, f, pxxc] = pmtm(x, nw, nfft, fs, ...)</c>.</summary>
    private static JgsValue[] MultitaperSpectrum(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "pmtm";
        ArityRange(name, args, 1, 13, line, col);
        (Complex[][] channels, bool real, bool wasVector) = SpectralChannels(name, args[0], line, col);
        int n = channels[0].Length;

        // The three name-value options are pulled out first, exactly as MATLAB pulls them out,
        // because what is left after them is a positional list.
        var rest = new List<JgsValue>();
        string taperType = "slepian";
        bool dropLast = true;
        double? confidence = null;
        for (int i = 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                if (i + 1 < args.Count && "tapers".StartsWith(word, StringComparison.Ordinal) && word.Length >= 3)
                {
                    taperType = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                    i++;
                    continue;
                }

                if (i + 1 < args.Count && "droplasttaper".StartsWith(word, StringComparison.Ordinal) && word.Length >= 4)
                {
                    dropLast = Num(name, args, i + 1, line, col) != 0;
                    i++;
                    continue;
                }

                if (i + 1 < args.Count && "confidencelevel".StartsWith(word, StringComparison.Ordinal)
                    && word.Length >= 4)
                {
                    confidence = Num(name, args, i + 1, line, col);
                    i++;
                    continue;
                }
            }

            rest.Add(args[i]);
        }

        if (taperType is not ("slepian" or "sine"))
        {
            throw new JgsRuntimeException(line, col, $"{name} knows the tapers 'slepian' and 'sine'.");
        }

        int at = 0;
        double[,] tapers;
        double[] weights;
        if (taperType == "sine")
        {
            int count = 7;
            double[]? given = null;
            if (rest.Count > 0 && !IsTextScalar(rest[0]))
            {
                if (ElementCount(rest[0]) > 0)
                {
                    double[] value = ToDoubles(name, rest[0], line, col);
                    if (value.Length == 1)
                    {
                        count = (int)value[0];
                    }
                    else
                    {
                        given = value;
                        count = value.Length;
                    }
                }

                at = 1;
            }

            if (count < 2)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least two sine tapers.");
            }

            tapers = SineTapers(n, count);
            weights = given ?? Uniform(count, 1.0 / count);
        }
        else
        {
            double nw = 4;
            if (rest.Count > 0 && !IsTextScalar(rest[0]))
            {
                if (ElementCount(rest[0]) > 0)
                {
                    double[] value = ToDoubles(name, rest[0], line, col);
                    if (value.Length == 1)
                    {
                        nw = value[0];
                        at = 1;
                    }
                    else
                    {
                        // A matrix of tapers with its concentrations beside it.
                        int[] dims = SizeDims(rest[0]);
                        int rows = dims.Length > 0 ? dims[0] : value.Length;
                        int columns = value.Length / System.Math.Max(rows, 1);
                        if (rest.Count < 2)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{name} needs the concentrations beside a matrix of tapers.");
                        }

                        double[] given = ToDoubles(name, rest[1], line, col);
                        (tapers, weights) = TrimTapers(value, rows, columns, given, dropLast, line, col);
                        return Multitaper(
                            name, channels, real, wasVector, tapers, weights, rest, 2,
                            confidence, taperType, wanted, line, col);
                    }
                }
                else
                {
                    at = 1;
                }
            }

            (double[,] sequences, double[] concentrations) = DiscreteProlate.Compute(n, nw, 1, (int)System.Math.Round(2 * nw));
            int total = concentrations.Length;
            var flat = new double[n * total];
            for (int c = 0; c < total; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    flat[(c * n) + r] = sequences[r, c];
                }
            }

            (tapers, weights) = TrimTapers(flat, n, total, concentrations, dropLast, line, col);
        }

        return Multitaper(
            name, channels, real, wasVector, tapers, weights, rest, at,
            confidence, taperType, wanted, line, col);
    }

    /// <summary>Drops the last taper unless told not to, which is MATLAB's <c>trimEV</c>.</summary>
    private static (double[,] Tapers, double[] Weights) TrimTapers(
        double[] flat, int rows, int columns, double[] concentrations, bool dropLast, int line, int col)
    {
        int keep = dropLast ? columns - 1 : columns;
        if (keep < 2)
        {
            throw new JgsRuntimeException(line, col,
                dropLast
                    ? "pmtm needs at least three tapers when it drops the last one."
                    : "pmtm needs at least two tapers.");
        }

        var tapers = new double[rows, keep];
        var weights = new double[keep];
        for (int c = 0; c < keep; c++)
        {
            weights[c] = concentrations[c];
            for (int r = 0; r < rows; r++)
            {
                tapers[r, c] = flat[(c * rows) + r];
            }
        }

        return (tapers, weights);
    }

    private static double[] Uniform(int count, double value)
    {
        var weights = new double[count];
        Array.Fill(weights, value);
        return weights;
    }

    /// <summary>MATLAB's <c>sinetapers</c>.</summary>
    private static double[,] SineTapers(int n, int count)
    {
        var tapers = new double[n, count];
        double scale = System.Math.Sqrt(2.0 / (n + 1));
        for (int k = 1; k <= count; k++)
        {
            for (int t = 1; t <= n; t++)
            {
                tapers[t - 1, k - 1] = scale * System.Math.Sin((double)t * k * System.Math.PI / (n + 1));
            }
        }

        return tapers;
    }

    /// <summary>The estimate itself, once the tapers are in hand.</summary>
    private static JgsValue[] Multitaper(
        string name,
        Complex[][] channels,
        bool real,
        bool wasVector,
        double[,] tapers,
        double[] weights,
        List<JgsValue> rest,
        int at,
        double? confidence,
        string taperType,
        int wanted,
        int line,
        int col)
    {
        int n = channels[0].Length;
        if (tapers.GetLength(0) != n)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs tapers as long as the signal.");
        }

        var trailing = new List<JgsValue>();
        for (int i = at; i < rest.Count; i++)
        {
            trailing.Add(rest[i]);
        }

        string method = taperType == "sine" ? "eigen" : "adapt";
        var numbers = new List<JgsValue>();
        var words = new List<JgsValue>();
        foreach (JgsValue value in trailing)
        {
            if (!IsTextScalar(value))
            {
                numbers.Add(value);
                continue;
            }

            string word = StrOf(name, value, line, col).ToLowerInvariant();
            if (word is "adapt" or "unity" or "eigen")
            {
                method = word;
                continue;
            }

            words.Add(value);
        }

        if (numbers.Count > 2)
        {
            confidence ??= Num(name, numbers, 2, line, col);
            numbers.RemoveAt(2);
        }

        var forwarded = new List<JgsValue>();
        forwarded.AddRange(numbers);
        forwarded.AddRange(words);
        SpectralWords parsed = ReadSpectralWords(name, forwarded, 0, line, col);
        SpectralRequest request = SpectralRequestOf(
            name, parsed, real, System.Math.Max(256, Fft.NextPowerOfTwo(n)), line, col);
        bool scalarNfft = parsed.Frequencies is null;
        int nfft = scalarNfft ? request.Nfft : parsed.Frequencies!.Length;
        double fs = request.SampleRate;
        double[] grid = scalarNfft ? SpectralEstimation.FrequencyGrid(nfft, fs) : parsed.Frequencies!;

        int k = weights.Length;
        var values = new Complex[channels.Length][];
        double[] frequencies = grid;
        for (int c = 0; c < channels.Length; c++)
        {
            var eigenspectra = new double[k][];
            for (int j = 0; j < k; j++)
            {
                var tapered = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    tapered[i] = channels[c][i] * tapers[i, j];
                }

                Complex[] spectrum = scalarNfft
                    ? SpectralEstimation.Transform(tapered, nfft)
                    : SpectralEstimation.Transform(tapered, parsed.Frequencies!, fs);
                eigenspectra[j] = new double[spectrum.Length];
                for (int i = 0; i < spectrum.Length; i++)
                {
                    double magnitude = spectrum[i].Magnitude;
                    eigenspectra[j][i] = magnitude * magnitude;
                }
            }

            double[] combined = Combine(eigenspectra, weights, channels[c], method, taperType, nfft);
            var raw = new Complex[combined.Length];
            for (int i = 0; i < combined.Length; i++)
            {
                raw[i] = combined[i];
            }

            (Complex[] pxx, double[] w) = SpectralEstimation.ToPsd(
                raw, grid, request.Range, scalarNfft, fs, SpectralScaling.Psd);
            values[c] = pxx;
            frequencies = w;
        }

        var answer = new SpectralAnswer { Values = values, Frequencies = frequencies, Segments = k };
        if (confidence is double level || wanted > 2)
        {
            level = confidence ?? SpectralEstimation.DefaultConfidence;
            (double lower, double upper) = SpectralEstimation.Chi2Confidence(level, k);
            var low = new double[values.Length][];
            var high = new double[values.Length][];
            for (int c = 0; c < values.Length; c++)
            {
                low[c] = new double[values[c].Length];
                high[c] = new double[values[c].Length];
                for (int i = 0; i < values[c].Length; i++)
                {
                    low[c][i] = values[c][i].Real * lower;
                    high[c][i] = values[c][i].Real * upper;
                }
            }

            answer.ConfidenceLower = low;
            answer.ConfidenceUpper = high;
        }

        if (request.CenterDc && scalarNfft)
        {
            CenterParametric(answer, request.Range, nfft, fs);
        }

        JgsValue[] packed = PackEstimate(answer, request, wanted, wasVector, !scalarNfft);
        if (wanted == 3 && confidence is null)
        {
            // The legacy signature: without a named confidence level the interval comes second and
            // the frequencies third, which is the order pmtm had before the option existed.
            return [packed[0], packed[2], packed[1]];
        }

        return packed;
    }

    /// <summary>The three weightings: uniform, by concentration, and the adaptive fixed point.</summary>
    private static double[] Combine(
        double[][] eigenspectra, double[] weights, Complex[] x, string method, string taperType, int nfft)
    {
        int points = eigenspectra[0].Length;
        int k = eigenspectra.Length;
        var combined = new double[points];
        if (method != "adapt")
        {
            for (int i = 0; i < points; i++)
            {
                double sum = 0;
                for (int j = 0; j < k; j++)
                {
                    sum += eigenspectra[j][i] * (method == "eigen" ? weights[j] : 1);
                }

                combined[i] = taperType == "sine" ? sum : sum / k;
            }

            return combined;
        }

        double power = 0;
        foreach (Complex z in x)
        {
            power += (z * Complex.Conjugate(z)).Real;
        }

        power /= x.Length;
        var current = new double[points];
        for (int i = 0; i < points; i++)
        {
            current[i] = (eigenspectra[0][i] + eigenspectra[1][i]) / 2;
        }

        var previous = new double[points];
        double tolerance = 0.0005 * power / points;
        var offset = new double[k];
        for (int j = 0; j < k; j++)
        {
            offset[j] = power * (1 - weights[j]);
        }

        while (true)
        {
            double change = 0;
            for (int i = 0; i < points; i++)
            {
                change += System.Math.Abs(current[i] - previous[i]) / points;
            }

            if (change <= tolerance)
            {
                break;
            }

            var next = new double[points];
            for (int i = 0; i < points; i++)
            {
                double top = 0;
                double bottom = 0;
                for (int j = 0; j < k; j++)
                {
                    double b = current[i] / ((current[i] * weights[j]) + offset[j]);
                    double wk = b * b * weights[j];
                    top += wk * eigenspectra[j][i];
                    bottom += wk;
                }

                next[i] = top / bottom;
            }

            previous = current;
            current = next;
        }

        return current;
    }
}
