using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>plomb</c> (M136): the spectrum of a signal whose samples are not evenly spaced, or which has
/// gaps where samples are missing.
/// </summary>
/// <remarks>
/// Its argument list is positional but every position is optional, and each is told apart by what it
/// is rather than by where it sits: a scalar second argument is a sample rate and a vector one is a
/// list of times; a scalar third is the highest frequency wanted and a vector one is the list of
/// frequencies; a fourth is the oversampling factor. The words <c>'psd'</c>, <c>'power'</c> and
/// <c>'normalized'</c> may appear anywhere, and <c>'Pd'</c> takes a value after it.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>plomb</c>.</summary>
    internal static void RegisterLombScargleBuiltins(JgsEnvironment env)
    {
        env.Builtins.Register("plomb", JgsValue.Function(new BuiltinFunction(
            "plomb", (args, line, col) => LombPeriodogram(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => LombPeriodogram(args, wanted, line, col),
        }));
    }

    /// <summary><c>[pxx, f, pth] = plomb(x, t, f, ofac, spectrumtype, 'Pd', p)</c>.</summary>
    private static JgsValue[] LombPeriodogram(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "plomb";
        ArityRange(name, args, 1, 7, line, col);
        double[] flat = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        if (rows == 1)
        {
            (rows, columns) = (columns, 1);
        }

        LombScargle.Scaling scaling = LombScargle.Scaling.Psd;
        double[]? detection = null;
        var positional = new List<JgsValue>();
        for (int i = 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                if (word == "pd" && i + 1 < args.Count)
                {
                    detection = ToDoubles(name, args[i + 1], line, col);
                    i++;
                    continue;
                }

                if ("psd".StartsWith(word, StringComparison.Ordinal) && word.Length > 0)
                {
                    scaling = LombScargle.Scaling.Psd;
                }
                else if ("power".StartsWith(word, StringComparison.Ordinal) && word.Length > 0)
                {
                    scaling = LombScargle.Scaling.Power;
                }
                else if ("normalized".StartsWith(word, StringComparison.Ordinal) && word.Length > 0)
                {
                    scaling = LombScargle.Scaling.Normalized;
                }
                else
                {
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                continue;
            }

            positional.Add(args[i]);
        }

        double[]? times = null;
        double? sampleRate = null;
        double[]? frequencies = null;
        double? maximum = null;
        double? oversample = null;
        int at = 0;
        if (positional.Count > at && ElementCount(positional[at]) > 0)
        {
            double[] given = ToDoubles(name, positional[at], line, col);
            if (given.Length == 1)
            {
                sampleRate = given[0];
            }
            else
            {
                times = given;
                if (times.Length != rows)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a time for every sample of the signal.");
                }
            }
        }

        at++;
        if (positional.Count > at && ElementCount(positional[at]) > 0)
        {
            double[] given = ToDoubles(name, positional[at], line, col);
            if (given.Length == 1)
            {
                maximum = given[0];
            }
            else
            {
                frequencies = (double[])given.Clone();
                Array.Sort(frequencies);
            }
        }

        at++;
        if (positional.Count > at && ElementCount(positional[at]) > 0)
        {
            oversample = Num(name, positional, at, line, col);
        }

        if (frequencies is not null && oversample is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} cannot take both a list of frequencies and an oversampling factor.");
        }

        bool inHertz = times is not null || sampleRate is not null;
        times ??= EvenTimes(rows, sampleRate);
        double factor = oversample ?? 4;

        // The sample rate the answer is reported against is the one the times imply, not the one
        // that was handed in: a signal with gaps has a different effective rate.
        double[] finite = times.Where(static v => !double.IsNaN(v)).ToArray();
        double fs = (finite.Length - 1) / (finite[^1] - finite[0]);
        if (frequencies is not null && !inHertz)
        {
            for (int i = 0; i < frequencies.Length; i++)
            {
                frequencies[i] *= fs / (2 * System.Math.PI);
            }
        }

        if (maximum is double top && !inHertz)
        {
            maximum = top * fs / (2 * System.Math.PI);
        }

        var spectra = new double[columns][];
        double[] grid = [];
        var counts = new int[columns];
        var variances = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            var signal = new List<double>();
            var stamps = new List<double>();
            var seen = new HashSet<double>();
            for (int r = 0; r < rows; r++)
            {
                double value = flat[(c * rows) + r];
                if (double.IsNaN(value) || double.IsNaN(times[r]) || !seen.Add(times[r]))
                {
                    continue;
                }

                signal.Add(value);
                stamps.Add(times[r]);
            }

            if (signal.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least one usable sample.");
            }

            counts[c] = signal.Count;
            double mean = signal.Average();
            double variance = 0;
            foreach (double value in signal)
            {
                variance += (value - mean) * (value - mean);
            }

            variance = signal.Count > 1 ? variance / (signal.Count - 1) : 0;
            variances[c] = variance;
            var centred = new double[signal.Count];
            for (int i = 0; i < signal.Count; i++)
            {
                centred[i] = signal[i] - mean;
            }

            if (frequencies is not null)
            {
                double[] values = LombScargle.Direct(centred, frequencies, [.. stamps]);
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (frequencies[i] == 0)
                    {
                        values[i] = 0;
                    }
                }

                spectra[c] = values;
                grid = frequencies;
                continue;
            }

            (double[] fast, double[] where) = LombScargle.Fast(centred, [.. stamps], factor, maximum);
            spectra[c] = fast;
            grid = where;
        }

        var reported = (double[])grid.Clone();
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < spectra[c].Length; i++)
            {
                spectra[c][i] = scaling switch
                {
                    LombScargle.Scaling.Power => spectra[c][i] / counts[c],
                    LombScargle.Scaling.Normalized => spectra[c][i] / (2 * variances[c]),
                    _ => inHertz ? spectra[c][i] / fs : spectra[c][i] / (2 * System.Math.PI),
                };
            }
        }

        if (!inHertz)
        {
            for (int i = 0; i < reported.Length; i++)
            {
                reported[i] = reported[i] * 2 * System.Math.PI / fs;
            }
        }

        JgsValue spectrum = ColumnsOfDoubles(spectra);
        JgsValue f = ColumnOfDoubles(reported);
        if (wanted <= 1)
        {
            return [spectrum];
        }

        if (wanted == 2)
        {
            return [spectrum, f];
        }

        if (detection is null)
        {
            return [spectrum, f, JgsMatrix.FromColumnMajor([], 0, 0)];
        }

        double m = 2.0 * reported.Length / factor;
        var thresholds = new double[detection.Length * columns];
        for (int c = 0; c < columns; c++)
        {
            double alpha = scaling switch
            {
                LombScargle.Scaling.Power => counts[c] / (2 * variances[c]),
                LombScargle.Scaling.Normalized => 1,
                _ => inHertz ? fs / (2 * variances[c]) : System.Math.PI / variances[c],
            };

            for (int i = 0; i < detection.Length; i++)
            {
                double a = 1 - detection[i];
                thresholds[(c * detection.Length) + i] =
                    -System.Math.Log(1 - System.Math.Pow(1 - a, 1 / m)) / alpha;
            }
        }

        return [spectrum, f, JgsMatrix.FromColumnMajor(thresholds, detection.Length, columns)];
    }

    /// <summary>The sample times a signal has when none were given.</summary>
    private static double[] EvenTimes(int count, double? sampleRate)
    {
        var t = new double[count];
        double step = sampleRate is double fs ? 1 / fs : 1;
        for (int i = 0; i < count; i++)
        {
            t[i] = i * step;
        }

        return t;
    }

    /// <summary>Several equal-length columns laid out as one matrix.</summary>
    private static JgsValue ColumnsOfDoubles(double[][] columns)
    {
        int rows = columns.Length == 0 ? 0 : columns[0].Length;
        var flat = new double[rows * columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            Array.Copy(columns[c], 0, flat, c * rows, rows);
        }

        return JgsMatrix.FromColumnMajor(flat, rows, columns.Length);
    }
}
