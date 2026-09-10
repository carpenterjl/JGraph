using System.Numerics;
using JGraph.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The waveform generators: <c>chirp</c>, <c>sinc</c>, <c>diric</c>, the three periodic waves and
/// the five single pulses, plus <c>pulstran</c>, which repeats any of them, and <c>vco</c> (M132).
/// </summary>
/// <remarks>
/// <para>
/// Nine of these are elementwise and keep whatever shape they were handed, which is the whole of
/// their grammar. The two that are not are the interesting ones. <c>pulstran</c> takes a pulse — a
/// function to call or a prototype to interpolate — and a list of delays, and adds up one copy per
/// delay; with a two-column delay list the second column scales each copy. It is the one generator
/// here that calls back into the script, and the one that has to decide whether its third argument
/// is a name, a handle or a vector of samples.
/// </para>
/// <para>
/// <c>chirp</c> is the other, and its awkwardness is in its options rather than its arithmetic: the
/// sweep law, the starting phase in degrees, an analytic output, and — for the quadratic law only —
/// a word forcing the sweep to bend against its endpoints, which MATLAB implements by running the
/// same arithmetic backwards along a reversed time axis.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the eleven generator names.</summary>
    internal static void RegisterSignalGeneratorBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("sinc", (args, line, col) =>
        {
            Arity("sinc", args, 1, line, col);
            return SignalShaped(args[0], WaveformGenerators.Sinc(SignalSamples("sinc", args[0], line, col)));
        });

        Define("diric", (args, line, col) =>
        {
            Arity("diric", args, 2, line, col);
            int n = Count("diric", args, 1, line, col);
            return SignalShaped(
                args[0], WaveformGenerators.Dirichlet(SignalSamples("diric", args[0], line, col), n));
        });

        Define("sawtooth", (args, line, col) =>
        {
            ArityRange("sawtooth", args, 1, 2, line, col);
            double width = args.Count == 2 ? Num("sawtooth", args, 1, line, col) : 1.0;
            return SignalShaped(
                args[0], WaveformGenerators.Sawtooth(SignalSamples("sawtooth", args[0], line, col), width));
        });

        Define("square", (args, line, col) =>
        {
            ArityRange("square", args, 1, 2, line, col);
            double duty = args.Count == 2 ? Num("square", args, 1, line, col) : 50.0;
            return SignalShaped(
                args[0], WaveformGenerators.Square(SignalSamples("square", args[0], line, col), duty));
        });

        Define("rectpuls", (args, line, col) =>
        {
            ArityRange("rectpuls", args, 1, 2, line, col);
            double width = args.Count == 2 ? Num("rectpuls", args, 1, line, col) : 1.0;
            return SignalShaped(
                args[0], WaveformGenerators.RectangularPulse(SignalSamples("rectpuls", args[0], line, col), width));
        });

        Define("tripuls", (args, line, col) =>
        {
            ArityRange("tripuls", args, 1, 3, line, col);
            double width = args.Count >= 2 && !IsEmptyValue(args[1]) ? Num("tripuls", args, 1, line, col) : 1.0;
            double skew = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("tripuls", args, 2, line, col) : 0.0;
            return SignalShaped(
                args[0], WaveformGenerators.TriangularPulse(SignalSamples("tripuls", args[0], line, col), width, skew));
        });

        Define("gmonopuls", (args, line, col) => Monopulse(args, line, col));
        Define("gauspuls", (args, line, col) => GaussianPulse(args, 1, line, col)[0],
            (args, wanted, line, col) => GaussianPulse(args, wanted, line, col));
        Define("chirp", Chirp);
        Define("pulstran", (args, line, col) => PulseTrain(env, args, line, col));
        Define("vco", VoltageControlled);
    }

    /// <summary>The samples of an argument that a generator reads elementwise.</summary>
    private static double[] SignalSamples(string name, JgsValue value, int line, int col) =>
        ToDoubles(name, value, line, col);

    /// <summary>Gives an elementwise answer the shape of the argument it came from.</summary>
    private static JgsValue SignalShaped(JgsValue source, double[] values)
    {
        int[] dims = SizeDims(source);
        return dims.Length >= 2
            ? JgsMatrix.FromColumnMajorDims(values, dims)
            : JgsMatrix.FromColumnMajor(values, 1, values.Length);
    }

    /// <summary>The rows and columns an argument has, for the two generators that care.</summary>
    private static (int Rows, int Columns) SignalShapeOf(JgsValue value)
    {
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        return (rows, columns);
    }

    /// <summary><c>gmonopuls(t, fc)</c> and <c>gmonopuls('cutoff', fc)</c>.</summary>
    private static JgsValue Monopulse(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("gmonopuls", args, 1, 2, line, col);
        double centre = args.Count >= 2 && !IsEmptyValue(args[1]) ? Num("gmonopuls", args, 1, line, col) : 1e3;
        if (args[0].Type == JgsType.String)
        {
            RequireCutoffWord("gmonopuls", args[0].AsString, line, col);
            return JgsValue.Number(WaveformGenerators.GaussianMonopulseCutoff(centre));
        }

        return SignalShaped(
            args[0], WaveformGenerators.GaussianMonopulse(SignalSamples("gmonopuls", args[0], line, col), centre));
    }

    /// <summary><c>gauspuls</c> in both its forms, with its three outputs.</summary>
    private static JgsValue[] GaussianPulse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("gauspuls", args, 1, 5, line, col);
        double centre = args.Count >= 2 && !IsEmptyValue(args[1]) ? Num("gauspuls", args, 1, line, col) : 1e3;
        double bandwidth = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("gauspuls", args, 2, line, col) : 0.5;
        double reference = args.Count >= 4 && !IsEmptyValue(args[3]) ? Num("gauspuls", args, 3, line, col) : -6.0;
        try
        {
            if (args[0].Type == JgsType.String)
            {
                RequireCutoffWord("gauspuls", args[0].AsString, line, col);
                double trailing = args.Count >= 5 && !IsEmptyValue(args[4])
                    ? Num("gauspuls", args, 4, line, col)
                    : -60.0;
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "gauspuls's cutoff form has one output.");
                }

                return [JgsValue.Number(
                    WaveformGenerators.GaussianPulseCutoff(centre, bandwidth, reference, trailing))];
            }

            if (args.Count > 4)
            {
                throw new JgsRuntimeException(line, col,
                    "gauspuls takes a trailing level only when it is asked for a cutoff time.");
            }

            (double[] inPhase, double[] quadrature, double[] envelope) = WaveformGenerators.GaussianPulse(
                SignalSamples("gauspuls", args[0], line, col), centre, bandwidth, reference);
            var outputs = new List<JgsValue> { SignalShaped(args[0], inPhase) };
            if (wanted >= 2)
            {
                outputs.Add(SignalShaped(args[0], quadrature));
            }

            if (wanted >= 3)
            {
                outputs.Add(SignalShaped(args[0], envelope));
            }

            return [.. outputs];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"gauspuls: {ex.Message}");
        }
    }

    /// <summary>Refuses anything but the word the two cutoff forms take.</summary>
    private static void RequireCutoffWord(string name, string word, int line, int col)
    {
        if (word.Length == 0 || !"cutoff".StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the only word this takes in place of a time is 'cutoff'.");
        }
    }

    /// <summary><c>chirp(t, f0, t1, f1, method, phi, shape)</c>.</summary>
    private static JgsValue Chirp(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("chirp", args, 1, 8, line, col);
        double[] t = SignalSamples("chirp", args[0], line, col);
        double f0 = args.Count >= 2 && !IsEmptyValue(args[1]) ? Num("chirp", args, 1, line, col) : 0.0;
        double t1 = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("chirp", args, 2, line, col) : 1.0;
        double f1 = args.Count >= 4 && !IsEmptyValue(args[3]) ? Num("chirp", args, 3, line, col) : 100.0;

        var sweep = WaveformGenerators.Sweep.Linear;
        var bend = WaveformGenerators.Bend.Default;
        bool complex = false;
        double phase = 0;
        for (int i = 4; i < args.Count; i++)
        {
            if (IsEmptyValue(args[i]))
            {
                continue;
            }

            if (args[i].Type != JgsType.String)
            {
                phase = Num("chirp", args, i, line, col);
                continue;
            }

            string word = args[i].AsString.ToLowerInvariant();
            switch (word)
            {
                case "linear":
                    sweep = WaveformGenerators.Sweep.Linear;
                    break;
                case "quadratic":
                    sweep = WaveformGenerators.Sweep.Quadratic;
                    break;
                case "logarithmic":
                    sweep = WaveformGenerators.Sweep.Logarithmic;
                    break;
                case "convex":
                    bend = WaveformGenerators.Bend.Convex;
                    break;
                case "concave":
                    bend = WaveformGenerators.Bend.Concave;
                    break;
                case "complex":
                    complex = true;
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"chirp does not know the option '{word}'.");
            }
        }

        (int rows, int columns) = SignalShapeOf(args[0]);
        try
        {
            Complex[] y = WaveformGenerators.Chirp(t, f0, t1, f1, phase, sweep, bend, complex, rows, columns);
            int[] dims = SizeDims(args[0]);
            JgsValue value = ComplexShaped(y, dims.Length >= 2 ? dims : [1, y.Length]);
            return value;
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"chirp: {ex.Message}");
        }
    }

    /// <summary><c>pulstran(t, d, func, ...)</c> and <c>pulstran(t, d, p, fs, method)</c>.</summary>
    private static JgsValue PulseTrain(JgsEnvironment env, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("pulstran", args, 3, 8, line, col);
        double[] t = SignalSamples("pulstran", args[0], line, col);
        (double[] delays, double[]? amplitudes) = PulseDelays(args[1], line, col);
        var y = new double[t.Length];

        bool sampled = args[2].Type is not JgsType.String and not JgsType.Function;
        if (sampled)
        {
            double rate = 1.0;
            string method = "linear";
            for (int i = 3; i < args.Count; i++)
            {
                if (args[i].Type == JgsType.String)
                {
                    method = args[i].AsString.ToLowerInvariant();
                }
                else
                {
                    rate = Num("pulstran", args, i, line, col);
                }
            }

            double[] prototype = SignalSamples("pulstran", args[2], line, col);
            var padded = new double[prototype.Length + 6];
            prototype.CopyTo(padded, 3);
            var grid = new double[padded.Length];
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = (i - 3) / rate;
            }

            var shifted = new double[t.Length];
            for (int d = 0; d < delays.Length; d++)
            {
                for (int i = 0; i < t.Length; i++)
                {
                    shifted[i] = t[i] - delays[d];
                }

                double[] sample = SampleCurve(grid, padded, shifted, method, line, col);
                double gain = amplitudes is null ? 1.0 : amplitudes[d];
                for (int i = 0; i < t.Length; i++)
                {
                    y[i] += gain * sample[i];
                }
            }

            return SignalShaped(args[0], y);
        }

        IJgsCallable pulse = args[2].Type == JgsType.Function
            ? args[2].AsCallable
            : SignalNamedFunction(env, args[2].AsString, line, col);
        var extra = new List<JgsValue>();
        for (int i = 3; i < args.Count; i++)
        {
            extra.Add(args[i]);
        }

        for (int d = 0; d < delays.Length; d++)
        {
            var shifted = new double[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                shifted[i] = t[i] - delays[d];
            }

            var call = new List<JgsValue> { SignalShaped(args[0], shifted) };
            call.AddRange(extra);
            double[] sample = SignalSamples("pulstran", pulse.Call(call, line, col), line, col);
            if (sample.Length != t.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "pulstran: the pulse answered a different number of samples than it was asked about.");
            }

            double gain = amplitudes is null ? 1.0 : amplitudes[d];
            for (int i = 0; i < t.Length; i++)
            {
                y[i] += gain * sample[i];
            }
        }

        return SignalShaped(args[0], y);
    }

    /// <summary>The delays and, if a second column is present, the amplitude of each copy.</summary>
    private static (double[] Delays, double[]? Amplitudes) PulseDelays(JgsValue value, int line, int col)
    {
        int[] dims = SizeDims(value);
        double[] flat = ToDoubles("pulstran", value, line, col);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        if (rows == 1 || columns == 1)
        {
            return (flat, null);
        }

        if (columns != 2)
        {
            throw new JgsRuntimeException(line, col,
                "pulstran's delays are a vector, or a matrix of two columns holding a delay and an amplitude.");
        }

        var delays = new double[rows];
        var amplitudes = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            delays[i] = flat[i];
            amplitudes[i] = flat[rows + i];
        }

        return (delays, amplitudes);
    }

    /// <summary>Reads a sampled pulse at arbitrary times, answering zero beyond its ends.</summary>
    private static double[] SampleCurve(
        double[] grid, double[] values, double[] at, string method, int line, int col)
    {
        var y = new double[at.Length];
        double[]? slopes = method switch
        {
            "linear" or "nearest" => null,
            "spline" => Interpolation.SplineSlopes(grid, values),
            "pchip" or "cubic" => Interpolation.PchipSlopes(grid, values),
            _ => throw new JgsRuntimeException(line, col,
                $"pulstran does not know the interpolation method '{method}'."),
        };

        for (int i = 0; i < at.Length; i++)
        {
            double x = at[i];
            if (x < grid[0] || x > grid[^1])
            {
                y[i] = 0;
                continue;
            }

            int k = Array.BinarySearch(grid, x);
            if (k < 0)
            {
                k = ~k - 1;
            }

            k = System.Math.Clamp(k, 0, grid.Length - 2);
            double s = (x - grid[k]) / (grid[k + 1] - grid[k]);
            y[i] = method switch
            {
                "linear" => values[k] + (s * (values[k + 1] - values[k])),
                "nearest" => s < 0.5 ? values[k] : values[k + 1],
                _ => Interpolation.Hermite(
                    grid[k], grid[k + 1], values[k], values[k + 1], slopes![k], slopes[k + 1], x),
            };
        }

        return y;
    }

    /// <summary>Resolves a pulse named as text into something that can be called.</summary>
    private static IJgsCallable SignalNamedFunction(JgsEnvironment env, string name, int line, int col)
    {
        if (env.TryGetFunction(name, out JgsValue value) && value.Type == JgsType.Function)
        {
            return value.AsCallable;
        }

        throw new JgsRuntimeException(line, col, $"pulstran: there is no function called '{name}'.");
    }

    /// <summary><c>vco(x, fc, fs)</c> and <c>vco(x, [fmin fmax], fs)</c>.</summary>
    private static JgsValue VoltageControlled(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("vco", args, 1, 3, line, col);
        double rate = args.Count >= 3 ? Num("vco", args, 2, line, col) : 1.0;
        double[] range = args.Count >= 2
            ? NumericVector("vco", args, 1, line, col)
            : [rate / 4];
        double[] x = SignalSamples("vco", args[0], line, col);
        (int rows, int columns) = SignalShapeOf(args[0]);
        bool isRow = rows == 1 && columns > 1;
        try
        {
            double[] y = SignalModulation.VoltageControlled(
                x, isRow ? columns : rows, isRow ? 1 : columns, range, rate);
            return SignalShaped(args[0], y);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"vco: {ex.Message}");
        }
    }
}
