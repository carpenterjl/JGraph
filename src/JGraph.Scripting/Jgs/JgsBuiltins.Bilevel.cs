using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The bilevel waveform measurements (M136): the two states a pulse waveform holds, and the twelve
/// things measured about the transitions between them.
/// </summary>
/// <remarks>
/// They share an argument shape as well as their arithmetic. After the signal comes an optional
/// sample rate or time vector — a scalar is a rate, a vector is the times — and for
/// <c>settlingtime</c> a second number, the seek duration. Everything after that is name–value:
/// <c>'Tolerance'</c>, <c>'StateLevels'</c>, <c>'MidPercentReferenceLevel'</c>,
/// <c>'PercentReferenceLevels'</c>, <c>'Polarity'</c>, <c>'Region'</c> and <c>'SeekFactor'</c>.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the bilevel names.</summary>
    internal static void RegisterBilevelBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Measure(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Measure("statelevels", StateLevelsOf);
        Measure("midcross", MidCrossOf);
        Measure("risetime", TransitionDuration);
        Measure("falltime", TransitionDuration);
        Measure("slewrate", TransitionDuration);
        Measure("pulsewidth", PulseMeasure);
        Measure("pulseperiod", PulseMeasure);
        Measure("pulsesep", PulseMeasure);
        Measure("dutycycle", PulseMeasure);
        Measure("overshoot", ShootMeasure);
        Measure("undershoot", ShootMeasure);
        Measure("settlingtime", SettlingMeasure);
    }

    /// <summary>What the shared reading yields: the signal, its times, and the named options.</summary>
    private sealed class BilevelOptions
    {
        public double[] Signal { get; set; } = [];

        public double[] Times { get; set; } = [];

        public bool WasRow { get; set; }

        public double Tolerance { get; set; } = 2;

        public double MidPercent { get; set; } = 50;

        public double[] Percents { get; set; } = [10, 90];

        public double[] StateLevels { get; set; } = [];

        public int Polarity { get; set; } = 1;

        public string Region { get; set; } = "postshoot";

        public double SeekFactor { get; set; } = 3;

        public double Seek { get; set; }
    }

    /// <summary>Reads the signal, the time information and the name–value options every name shares.</summary>
    private static BilevelOptions ReadBilevel(
        string name, IReadOnlyList<JgsValue> args, bool needsSeek, int line, int col)
    {
        double[] x = ToDoubles(name, args[0], line, col);
        if (x.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least two samples.");
        }

        int[] dims = SizeDims(args[0]);
        var options = new BilevelOptions
        {
            Signal = x,
            WasRow = dims.Length > 1 && dims[0] == 1 && dims[1] > 1,
        };

        int at = 1;
        var leading = new List<double[]>();
        while (at < args.Count && !IsTextScalar(args[at]))
        {
            leading.Add(ToDoubles(name, args[at], line, col));
            at++;
        }

        int timeArgs = leading.Count - (needsSeek ? 1 : 0);
        if (needsSeek)
        {
            if (leading.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a seek duration.");
            }

            options.Seek = leading[^1][0];
        }

        if (timeArgs > 0 && leading[0].Length == 1)
        {
            double fs = leading[0][0];
            options.Times = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                options.Times[i] = i / fs;
            }
        }
        else if (timeArgs > 0)
        {
            if (leading[0].Length != x.Length)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a time for every sample.");
            }

            options.Times = leading[0];
        }
        else
        {
            options.Times = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                options.Times[i] = i + 1;
            }
        }

        for (int i = at; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after every option name.");
            }

            string option = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "tolerance":
                    options.Tolerance = Num(name, args, i + 1, line, col);
                    break;
                case "midpercentreferencelevel":
                case "midpct":
                    options.MidPercent = Num(name, args, i + 1, line, col);
                    break;
                case "percentreferencelevels":
                case "pctreflevels":
                    options.Percents = ToDoubles(name, args[i + 1], line, col);
                    break;
                case "statelevels":
                    options.StateLevels = ToDoubles(name, args[i + 1], line, col);
                    break;
                case "polarity":
                    options.Polarity = StrOf(name, args[i + 1], line, col)
                        .StartsWith("n", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                    break;
                case "region":
                    options.Region = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                    break;
                case "seekfactor":
                    options.SeekFactor = Num(name, args, i + 1, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{option}'.");
            }
        }

        if (options.StateLevels.Length == 0)
        {
            options.StateLevels = DefaultStateLevels(x);
        }

        return options;
    }

    /// <summary>The state levels a waveform gets when the caller did not name them.</summary>
    private static double[] DefaultStateLevels(double[] x)
    {
        double lower = x.Min();
        double upper = x.Max();
        (double[] counts, _) = BilevelWaveforms.Histogram(x, 100, lower, upper);
        return BilevelWaveforms.StateLevels(counts, lower, upper, BilevelWaveforms.LevelMethod.Mode);
    }

    private static JgsValue BilevelColumn(double[] values, bool wasRow) => wasRow
        ? JgsMatrix.FromColumnMajor(values, 1, values.Length)
        : JgsMatrix.FromColumnMajor(values, values.Length, 1);

    /// <summary><c>[levels, histogram, bins] = statelevels(x, nbins, method, bounds)</c>.</summary>
    private static JgsValue[] StateLevelsOf(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 4, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        bool wasRow = dims.Length > 1 && dims[0] == 1 && dims[1] > 1;
        int bins = args.Count > 1 ? (int)Num(name, args, 1, line, col) : 100;
        var method = BilevelWaveforms.LevelMethod.Mode;
        int at = 2;
        if (args.Count > 2 && IsTextScalar(args[2]))
        {
            method = StrOf(name, args[2], line, col).ToLowerInvariant() switch
            {
                "mean" => BilevelWaveforms.LevelMethod.Mean,
                "mode" => BilevelWaveforms.LevelMethod.Mode,
                _ => throw new JgsRuntimeException(line, col, $"{name} knows the methods 'mean' and 'mode'."),
            };
            at = 3;
        }

        double lower = x.Min();
        double upper = x.Max();
        if (args.Count > at)
        {
            double[] bounds = ToDoubles(name, args[at], line, col);
            lower = bounds[0];
            upper = bounds[1];
        }

        (double[] counts, double[] centres) = BilevelWaveforms.Histogram(x, bins, lower, upper);
        double[] levels = BilevelWaveforms.StateLevels(counts, lower, upper, method);
        JgsValue result = JgsMatrix.FromColumnMajor(levels, 1, 2);
        return wanted switch
        {
            <= 1 => [result],
            2 => [result, BilevelColumn(counts, wasRow)],
            _ => [result, BilevelColumn(counts, wasRow), BilevelColumn(centres, wasRow)],
        };
    }

    /// <summary><c>[c, midlev] = midcross(x, fs, ...)</c>.</summary>
    private static JgsValue[] MidCrossOf(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        BilevelOptions options = ReadBilevel(name, args, needsSeek: false, line, col);
        double lower = BilevelWaveforms.Reference(options.StateLevels, options.Tolerance);
        double upper = BilevelWaveforms.Reference(options.StateLevels, 100 - options.Tolerance);
        double mid = BilevelWaveforms.Reference(options.StateLevels, options.MidPercent);
        BilevelWaveforms.Crossings crossings = BilevelWaveforms.MidCrossings(
            options.Signal, options.Times, upper, lower, mid);
        JgsValue times = BilevelColumn(crossings.Times, options.WasRow);
        return wanted <= 1 ? [times] : [times, JgsValue.Number(mid)];
    }

    /// <summary><c>risetime</c>, <c>falltime</c> and <c>slewrate</c>.</summary>
    private static JgsValue[] TransitionDuration(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        BilevelOptions options = ReadBilevel(name, args, needsSeek: false, line, col);
        double meanPercent = (options.Percents[0] + options.Percents[1]) / 2;
        double lowerBound = BilevelWaveforms.Reference(options.StateLevels, options.Tolerance);
        double upperBound = BilevelWaveforms.Reference(options.StateLevels, 100 - options.Tolerance);
        double lowerRef = BilevelWaveforms.Reference(options.StateLevels, options.Percents[0]);
        double midRef = BilevelWaveforms.Reference(options.StateLevels, meanPercent);
        double upperRef = BilevelWaveforms.Reference(options.StateLevels, options.Percents[1]);
        BilevelWaveforms.Transitions transitions = BilevelWaveforms.FindTransitions(
            options.Signal, options.Times, upperBound, lowerBound, upperRef, midRef, lowerRef);

        var chosen = new List<int>();
        for (int i = 0; i < transitions.Polarity.Length; i++)
        {
            bool keep = name switch
            {
                "risetime" => transitions.Polarity[i] > 0,
                "falltime" => transitions.Polarity[i] < 0,
                _ => true,
            };
            if (keep)
            {
                chosen.Add(i);
            }
        }

        var first = new double[chosen.Count];
        var lowerCross = new double[chosen.Count];
        var upperCross = new double[chosen.Count];
        for (int i = 0; i < chosen.Count; i++)
        {
            lowerCross[i] = transitions.LowerCross[chosen[i]];
            upperCross[i] = transitions.UpperCross[chosen[i]];
            first[i] = name == "slewrate"
                ? (upperRef - lowerRef) / (upperCross[i] - lowerCross[i])
                : transitions.Duration[chosen[i]];
        }

        return wanted switch
        {
            <= 1 => [BilevelColumn(first, options.WasRow)],
            2 => [BilevelColumn(first, options.WasRow), BilevelColumn(lowerCross, options.WasRow)],
            3 =>
            [
                BilevelColumn(first, options.WasRow), BilevelColumn(lowerCross, options.WasRow),
                BilevelColumn(upperCross, options.WasRow),
            ],
            4 =>
            [
                BilevelColumn(first, options.WasRow), BilevelColumn(lowerCross, options.WasRow),
                BilevelColumn(upperCross, options.WasRow), JgsValue.Number(lowerRef),
            ],
            _ =>
            [
                BilevelColumn(first, options.WasRow), BilevelColumn(lowerCross, options.WasRow),
                BilevelColumn(upperCross, options.WasRow), JgsValue.Number(lowerRef),
                JgsValue.Number(upperRef),
            ],
        };
    }

    /// <summary><c>pulsewidth</c>, <c>pulseperiod</c>, <c>pulsesep</c> and <c>dutycycle</c>.</summary>
    private static JgsValue[] PulseMeasure(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        // dutycycle has a second reading entirely: a pulse width and a repetition frequency.
        if (name == "dutycycle" && args.Count == 2 && ElementCount(args[0]) == 1)
        {
            double tau = Num(name, args, 0, line, col);
            double[] prf = ToDoubles(name, args[1], line, col);
            var cycles = new double[prf.Length];
            for (int i = 0; i < prf.Length; i++)
            {
                cycles[i] = tau * prf[i];
                if (cycles[i] > 1)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a width and a repetition frequency whose product is at most one.");
                }
            }

            return [cycles.Length == 1 ? JgsValue.Number(cycles[0]) : JgsMatrix.FromColumnMajor(cycles, 1, cycles.Length)];
        }

        BilevelOptions options = ReadBilevel(name, args, needsSeek: false, line, col);
        double lower = BilevelWaveforms.Reference(options.StateLevels, options.Tolerance);
        double upper = BilevelWaveforms.Reference(options.StateLevels, 100 - options.Tolerance);
        double mid = BilevelWaveforms.Reference(options.StateLevels, options.MidPercent);
        BilevelWaveforms.Crossings crossings = BilevelWaveforms.MidCrossings(
            options.Signal, options.Times, upper, lower, mid);

        int start = -1;
        for (int i = 0; i < crossings.Polarity.Length; i++)
        {
            if (crossings.Polarity[i] == options.Polarity)
            {
                start = i;
                break;
            }
        }

        bool needsNext = name is not "pulsewidth";
        var init = new List<double>();
        var final = new List<double>();
        var next = new List<double>();
        if (start >= 0 && crossings.Times.Length > 1)
        {
            int count = crossings.Times.Length - start;
            if (needsNext)
            {
                for (int k = 0; k + 2 < count; k += 2)
                {
                    init.Add(crossings.Times[start + k]);
                    final.Add(crossings.Times[start + k + 1]);
                    next.Add(crossings.Times[start + k + 2]);
                }
            }
            else
            {
                for (int k = 0; k + 1 < count; k += 2)
                {
                    init.Add(crossings.Times[start + k]);
                    final.Add(crossings.Times[start + k + 1]);
                }
            }
        }

        var measure = new double[init.Count];
        for (int i = 0; i < init.Count; i++)
        {
            measure[i] = name switch
            {
                "pulsewidth" => final[i] - init[i],
                "pulseperiod" => next[i] - init[i],
                "pulsesep" => next[i] - final[i],
                _ => (final[i] - init[i]) / (next[i] - init[i]),
            };
        }

        JgsValue First() => BilevelColumn(measure, options.WasRow);
        JgsValue Init() => BilevelColumn([.. init], options.WasRow);
        JgsValue Final() => BilevelColumn([.. final], options.WasRow);
        JgsValue Next() => BilevelColumn([.. next], options.WasRow);

        if (name == "pulsewidth")
        {
            return wanted switch
            {
                <= 1 => [First()],
                2 => [First(), Init()],
                3 => [First(), Init(), Final()],
                _ => [First(), Init(), Final(), JgsValue.Number(mid)],
            };
        }

        return wanted switch
        {
            <= 1 => [First()],
            2 => [First(), Init()],
            3 => [First(), Init(), Final()],
            4 => [First(), Init(), Final(), Next()],
            _ => [First(), Init(), Final(), Next(), JgsValue.Number(mid)],
        };
    }

    /// <summary><c>overshoot</c> and <c>undershoot</c>.</summary>
    private static JgsValue[] ShootMeasure(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        BilevelOptions options = ReadBilevel(name, args, needsSeek: false, line, col);
        double meanPercent = (options.Percents[0] + options.Percents[1]) / 2;
        double lowerBound = BilevelWaveforms.Reference(options.StateLevels, options.Tolerance);
        double upperBound = BilevelWaveforms.Reference(options.StateLevels, 100 - options.Tolerance);
        double lowerRef = BilevelWaveforms.Reference(options.StateLevels, options.Percents[0]);
        double midRef = BilevelWaveforms.Reference(options.StateLevels, meanPercent);
        double upperRef = BilevelWaveforms.Reference(options.StateLevels, options.Percents[1]);
        BilevelWaveforms.Transitions transitions = BilevelWaveforms.FindTransitions(
            options.Signal, options.Times, upperBound, lowerBound, upperRef, midRef, lowerRef);
        BilevelWaveforms.Shoots shoots = options.Region.StartsWith("pre", StringComparison.Ordinal)
            ? BilevelWaveforms.Preshoots(
                options.Signal, options.Times, options.StateLevels[1], options.StateLevels[0],
                upperBound, lowerBound, options.SeekFactor, transitions)
            : BilevelWaveforms.Postshoots(
                options.Signal, options.Times, options.StateLevels[1], options.StateLevels[0],
                upperBound, lowerBound, options.SeekFactor, transitions);

        double[] value = name == "overshoot" ? shoots.Overshoot : shoots.Undershoot;
        double[] level = name == "overshoot" ? shoots.OvershootLevel : shoots.UndershootLevel;
        double[] instant = name == "overshoot" ? shoots.OvershootInstant : shoots.UndershootInstant;
        return wanted switch
        {
            <= 1 => [BilevelColumn(value, options.WasRow)],
            2 => [BilevelColumn(value, options.WasRow), BilevelColumn(level, options.WasRow)],
            _ =>
            [
                BilevelColumn(value, options.WasRow), BilevelColumn(level, options.WasRow),
                BilevelColumn(instant, options.WasRow),
            ],
        };
    }

    /// <summary><c>[s, slev, sinst] = settlingtime(x, fs, d, ...)</c>.</summary>
    private static JgsValue[] SettlingMeasure(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        BilevelOptions options = ReadBilevel(name, args, needsSeek: true, line, col);
        double lowerBound = BilevelWaveforms.Reference(options.StateLevels, options.Tolerance);
        double upperBound = BilevelWaveforms.Reference(options.StateLevels, 100 - options.Tolerance);
        double midRef = BilevelWaveforms.Reference(options.StateLevels, options.MidPercent);
        BilevelWaveforms.Crossings crossings = BilevelWaveforms.MidCrossings(
            options.Signal, options.Times, upperBound, lowerBound, midRef);
        var transitions = new BilevelWaveforms.Transitions
        {
            Polarity = crossings.Polarity,
            MiddleCross = crossings.Times,
            Pre = crossings.Pre,
            Post = crossings.Post,
        };
        (double[] duration, double[] level, double[] instant) = BilevelWaveforms.Settling(
            options.Signal, options.Times, options.StateLevels[1], options.StateLevels[0],
            options.Tolerance, options.Seek, transitions);
        return wanted switch
        {
            <= 1 => [BilevelColumn(duration, options.WasRow)],
            2 => [BilevelColumn(duration, options.WasRow), BilevelColumn(level, options.WasRow)],
            _ =>
            [
                BilevelColumn(duration, options.WasRow), BilevelColumn(level, options.WasRow),
                BilevelColumn(instant, options.WasRow),
            ],
        };
    }
}
