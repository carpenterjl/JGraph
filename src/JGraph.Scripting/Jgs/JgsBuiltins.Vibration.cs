using System.Numerics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Maths;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The vibration names of M137 that do not need a tachometer's order map: <c>rainflow</c>,
/// <c>envspectrum</c>, <c>tsa</c> and <c>strips</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the vibration names.</summary>
    internal static void RegisterVibrationBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("rainflow", RainflowCount);
        Many("envspectrum", EnvelopeSpectrumOf);
        Many("tsa", TimeSynchronousAverage);
        Many("strips", StripPlot);
    }

    /// <summary><c>[c, rm, rmr, rmm, idx] = rainflow(x, fs|t, 'ext')</c>.</summary>
    private static JgsValue[] RainflowCount(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        bool extrema = false;
        double[]? times = null;
        for (int at = 1; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = StrOf(name, args[at], line, col).ToLowerInvariant();
                if (!Matches(word, "ext"))
                {
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                extrema = true;
                continue;
            }

            double[] given = ToDoubles(name, args[at], line, col);
            if (given.Length == 1)
            {
                times = new double[x.Length];
                for (int i = 0; i < x.Length; i++)
                {
                    times[i] = i / given[0];
                }
            }
            else
            {
                times = given;
            }
        }

        if (x.Length < 3)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least three samples.");
        }

        // Without a sample rate the reversals are reported at their sample numbers, which MATLAB
        // counts from one rather than from zero.
        times ??= OneBased(Ramp(x.Length, 0));
        int[] turning = extrema ? Ramp(x.Length, 0) : RainflowCounting.Extrema(x);
        var reversals = new double[turning.Length];
        for (int i = 0; i < turning.Length; i++)
        {
            reversals[i] = x[turning[i]];
        }

        LoadCycle[] cycles = RainflowCounting.Cycles(reversals);
        var table = new double[cycles.Length * 5];
        for (int i = 0; i < cycles.Length; i++)
        {
            table[i] = cycles[i].Count;
            table[i + cycles.Length] = cycles[i].Range;
            table[i + (2 * cycles.Length)] = cycles[i].Mean;
            table[i + (3 * cycles.Length)] = times[turning[cycles[i].Start]];
            table[i + (4 * cycles.Length)] = times[turning[cycles[i].End]];
        }

        JgsValue counted = JgsMatrix.FromColumnMajor(table, cycles.Length, 5);
        if (wanted <= 1)
        {
            return [counted];
        }

        // A whole cycle is counted twice in the histogram and the counts halved, so that a half
        // cycle contributes a half.
        var ranges = new List<double>();
        var means = new List<double>();
        foreach (LoadCycle cycle in cycles)
        {
            ranges.Add(cycle.Range);
            means.Add(cycle.Mean);
            if (cycle.Count == 1)
            {
                ranges.Add(cycle.Range);
                means.Add(cycle.Mean);
            }
        }

        double[] rangeEdges = Binning.EdgesFor(ranges, null, null, null, "auto");
        double[] meanEdges = Binning.EdgesFor(means, null, null, null, "auto");
        var matrix = new double[(rangeEdges.Length - 1) * (meanEdges.Length - 1)];
        for (int i = 0; i < ranges.Count; i++)
        {
            int r = Binning.BinOf(ranges[i], rangeEdges);
            int m = Binning.BinOf(means[i], meanEdges);
            if (r >= 0 && m >= 0)
            {
                matrix[r + (m * (rangeEdges.Length - 1))] += 0.5;
            }
        }

        var outputs = new List<JgsValue>
        {
            counted,
            JgsMatrix.FromColumnMajor(matrix, rangeEdges.Length - 1, meanEdges.Length - 1),
            ColumnOfDoubles(rangeEdges),
            ColumnOfDoubles(meanEdges),
            ColumnOfDoubles(OneBased(turning)),
        };
        return [.. outputs.Take(wanted)];
    }

    /// <summary><c>[spec, f, env, t] = envspectrum(x, fs, ...)</c>.</summary>
    private static JgsValue[] EnvelopeSpectrumOf(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 8, line, col);
        (Complex[][] channels, _, bool wasVector) = SpectralChannels(name, args[0], line, col);
        double fs = Num(name, args, 1, line, col);
        double[] band = [fs / 4, 3.0 / 8 * fs];
        bool hilbert = false;
        int order = 50;
        for (int at = 2; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "band"))
            {
                band = ToDoubles(name, args[at + 1], line, col);
                if (band.Length != 2 || !(band[0] < band[1]) || band[1] >= fs / 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs an increasing band below the Nyquist frequency.");
                }
            }
            else if (Matches(word, "method"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                hilbert = Matches(kind, "hilbert");
                if (!hilbert && !Matches(kind, "demod"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'hilbert' or 'demod' as its method.");
                }
            }
            else if (Matches(word, "filterorder"))
            {
                order = (int)Num(name, args, at + 1, line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        var spectra = new double[channels.Length][];
        var envelopes = new double[channels.Length][];
        double[] frequencies = [];
        for (int c = 0; c < channels.Length; c++)
        {
            (spectra[c], frequencies, envelopes[c]) = EnvelopeSpectrum.Compute(
                SpectralEstimators.RealPart(channels[c]), fs, band, hilbert, order);
        }

        _ = wasVector;
        var results = new List<JgsValue>
        {
            Columns(spectra),
            ColumnOfDoubles(frequencies),
            Columns(envelopes),
            ColumnOfDoubles(Divided(Ramp(channels[0].Length), fs)),
        };
        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary><c>[ta, t, p, rpm] = tsa(x, fs, tp, ...)</c>.</summary>
    private static JgsValue[] TimeSynchronousAverage(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 11, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double[] time = ToDoubles(name, args[1], line, col);
        double fs;
        double[] t;
        if (time.Length == 1)
        {
            fs = time[0];
            t = Divided(Ramp(x.Length), fs);
        }
        else
        {
            t = time;
            fs = (t.Length - 1) / (t[^1] - t[0]);
        }

        double[] pulses = ToDoubles(name, args[2], line, col);
        if (pulses.Length == 1)
        {
            var stepped = new List<double>();
            for (double at = t[0]; at <= t[^1] + (1 / fs); at += pulses[0])
            {
                stepped.Add(at);
            }

            pulses = [.. stepped];
        }

        var inside = new List<double>();
        foreach (double pulse in pulses)
        {
            if (pulse >= t[0] && pulse <= t[^1] + (1 / fs))
            {
                inside.Add(pulse);
            }
        }

        pulses = [.. inside];
        double perRotation = 1;
        int rotations = 1;
        int resample = 1;
        var method = SynchronousMethod.Linear;
        for (int at = 3; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "pulsesperrotation"))
            {
                perRotation = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "numrotations"))
            {
                rotations = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "resamplefactor"))
            {
                resample = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "method"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                method = Matches(kind, "linear") ? SynchronousMethod.Linear
                    : Matches(kind, "spline") ? SynchronousMethod.Spline
                    : Matches(kind, "pchip") ? SynchronousMethod.Pchip
                    : Matches(kind, "fft") ? SynchronousMethod.Fft
                    : throw new JgsRuntimeException(line, col,
                        $"{name} takes 'linear', 'spline', 'pchip' or 'fft' as its method.");
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        if (perRotation * rotations >= pulses.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs more than {rotations} rotations' worth of pulse times.");
        }

        SynchronousAnswer answer = SynchronousAverage.Compute(
            x, t, fs, pulses, perRotation, rotations, resample, method);
        var results = new List<JgsValue>
        {
            ColumnOfDoubles(answer.Average),
            ColumnOfDoubles(answer.Times),
            ColumnOfDoubles(TimesConstant(answer.Times, answer.Rate)),
            JgsValue.Number(answer.Rate * 60),
        };
        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary><c>strips(x, sd, fs, scale)</c>: the signal drawn as a stack of equal-length strips.</summary>
    private static JgsValue[] StripPlot(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 4, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        if (x.Length == 0)
        {
            return [];
        }

        double duration = args.Count > 1 ? Num(name, args, 1, line, col) : 250;
        double fs = args.Count > 2 ? Num(name, args, 2, line, col) : 1;
        double scale = args.Count > 3 ? Num(name, args, 3, line, col) : 1;
        int perStrip = (int)System.Math.Ceiling(duration * fs);
        if (perStrip < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a strip of at least one sample.");
        }

        int length = x.Length;
        if (length % perStrip == 1)
        {
            length--;
        }

        int strips = (int)System.Math.Ceiling((double)length / perStrip);
        double highest = double.NegativeInfinity;
        double lowest = double.PositiveInfinity;
        for (int i = 0; i < length; i++)
        {
            if (!double.IsNaN(x[i]))
            {
                highest = System.Math.Max(highest, x[i]);
                lowest = System.Math.Min(lowest, x[i]);
            }
        }

        double middle = 0.5 * (lowest + highest);
        double separation = (highest - lowest) * 1.25;
        if (separation == 0)
        {
            separation = 1;
        }

        AxesModel axes = JG.Gca();
        for (int s = 0; s < strips; s++)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            for (int i = 0; i < perStrip; i++)
            {
                int at = (s * perStrip) + i;
                if (at >= length)
                {
                    break;
                }

                xs.Add(i / fs);
                ys.Add((scale * x[at]) - middle + ((strips - 1 - s) * separation));
            }

            axes.AddLine([.. xs], [.. ys]);
        }

        axes.PrimaryXAxis.Label = "Time (s)";
        _ = wanted;
        return [];
    }

    // --- Shapes -------------------------------------------------------------------------------------

    /// <summary>One column per channel.</summary>
    private static JgsValue Columns(double[][] channels)
    {
        int rows = channels.Length == 0 ? 0 : channels[0].Length;
        var flat = new double[rows * channels.Length];
        for (int c = 0; c < channels.Length; c++)
        {
            Array.Copy(channels[c], 0, flat, c * rows, rows);
        }

        return JgsMatrix.FromColumnMajor(flat, rows, channels.Length);
    }

    private static double[] Ramp(int n)
    {
        var ramp = new double[n];
        for (int i = 0; i < n; i++)
        {
            ramp[i] = i;
        }

        return ramp;
    }

    private static int[] Ramp(int n, int from)
    {
        var ramp = new int[n];
        for (int i = 0; i < n; i++)
        {
            ramp[i] = from + i;
        }

        return ramp;
    }

    private static double[] Divided(double[] values, double by)
    {
        var divided = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            divided[i] = values[i] / by;
        }

        return divided;
    }

    private static double[] OneBased(int[] index)
    {
        var shifted = new double[index.Length];
        for (int i = 0; i < index.Length; i++)
        {
            shifted[i] = index[i] + 1;
        }

        return shifted;
    }
}
