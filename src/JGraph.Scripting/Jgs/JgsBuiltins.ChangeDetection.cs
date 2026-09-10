using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Detecting when a signal changes (M136): the cumulative-sum control chart, which reports the first
/// sample at which a drift has accumulated past a limit, and the change-point search, which finds the
/// division of a record that best explains it.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the change-detection names.</summary>
    internal static void RegisterChangeDetectionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("cusum",
            (args, line, col) => CumulativeSum(args, 1, line, col)[0],
            (args, wanted, line, col) => CumulativeSum(args, wanted, line, col));
        Define("findchangepts",
            (args, line, col) => ChangePointsOf(args, 1, line, col)[0],
            (args, wanted, line, col) => ChangePointsOf(args, wanted, line, col));
    }

    /// <summary><c>[iupper, ilower, uppersum, lowersum] = cusum(x, climit, mshift, tmean, tdev, 'all')</c>.</summary>
    private static JgsValue[] CumulativeSum(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "cusum";
        ArityRange(name, args, 1, 6, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        bool wasRow = !(dims.Length > 0 && dims[0] > 1);
        bool all = false;
        var numeric = new List<JgsValue>();
        for (int i = 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                if (!"all".StartsWith(word, StringComparison.Ordinal) || word.Length == 0)
                {
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                all = true;
                continue;
            }

            numeric.Add(args[i]);
        }

        // The target mean and deviation are taken from the first twenty-five samples, which is the
        // stretch cusum assumes is in control.
        int head = System.Math.Min(25, x.Length);
        double headMean = 0;
        for (int i = 0; i < head; i++)
        {
            headMean += x[i];
        }

        headMean /= head;
        double headVariance = 0;
        for (int i = 0; i < head; i++)
        {
            headVariance += (x[i] - headMean) * (x[i] - headMean);
        }

        double headDeviation = head > 1
            ? System.Math.Sqrt(headVariance / (head - 1))
            : 0;

        double limit = numeric.Count > 0 ? Num(name, numeric, 0, line, col) : 5;
        double shift = numeric.Count > 1 ? Num(name, numeric, 1, line, col) : 1;
        double target = numeric.Count > 2 ? Num(name, numeric, 2, line, col) : headMean;
        double deviation = numeric.Count > 3
            ? Num(name, numeric, 3, line, col)
            : System.Math.Max(2.220446049250313e-16, headDeviation);

        var upper = new double[x.Length];
        var lower = new double[x.Length];
        for (int i = 1; i < x.Length; i++)
        {
            upper[i] = System.Math.Max(0, upper[i - 1] + x[i] - target - (shift * deviation / 2));
            lower[i] = System.Math.Min(0, lower[i - 1] + x[i] - target + (shift * deviation / 2));
        }

        var upperHits = new List<double>();
        var lowerHits = new List<double>();
        for (int i = 0; i < x.Length; i++)
        {
            if (upper[i] > limit * deviation)
            {
                upperHits.Add(i + 1);
                if (!all)
                {
                    break;
                }
            }
        }

        for (int i = 0; i < x.Length; i++)
        {
            if (lower[i] < -limit * deviation)
            {
                lowerHits.Add(i + 1);
                if (!all)
                {
                    break;
                }
            }
        }

        JgsValue Shaped(double[] values) => wasRow
            ? JgsMatrix.FromColumnMajor(values, 1, values.Length)
            : JgsMatrix.FromColumnMajor(values, values.Length, 1);

        return wanted switch
        {
            <= 1 => [Shaped([.. upperHits])],
            2 => [Shaped([.. upperHits]), Shaped([.. lowerHits])],
            3 => [Shaped([.. upperHits]), Shaped([.. lowerHits]), Shaped(upper)],
            _ => [Shaped([.. upperHits]), Shaped([.. lowerHits]), Shaped(upper), Shaped(lower)],
        };
    }

    /// <summary><c>[ipt, residual] = findchangepts(x, 'Statistic', s, 'MaxNumChanges', k, ...)</c>.</summary>
    private static JgsValue[] ChangePointsOf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "findchangepts";
        ArityRange(name, args, 1, 7, line, col);
        double[] flat = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        bool wasColumn = rows > 1 && columns == 1;
        if (wasColumn)
        {
            (rows, columns) = (1, rows);
        }

        var y = new double[rows, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                y[r, c] = flat[(c * rows) + r];
            }
        }

        var statistic = ChangeStatistic.Mean;
        int? most = null;
        int? distance = null;
        double? threshold = null;
        for (int i = 1; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after every option name.");
            }

            string option = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "statistic":
                    statistic = StrOf(name, args[i + 1], line, col).ToLowerInvariant() switch
                    {
                        "mean" => ChangeStatistic.Mean,
                        "rms" => ChangeStatistic.RootMeanSquare,
                        "std" => ChangeStatistic.StandardDeviation,
                        "linear" => ChangeStatistic.Linear,
                        _ => throw new JgsRuntimeException(line, col,
                            $"{name} knows the statistics 'mean', 'rms', 'std' and 'linear'."),
                    };
                    break;
                case "maxnumchanges":
                    most = (int)Num(name, args, i + 1, line, col);
                    break;
                case "mindistance":
                    distance = (int)Num(name, args, i + 1, line, col);
                    break;
                case "minthreshold":
                    threshold = Num(name, args, i + 1, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{option}'.");
            }
        }

        if (most is not null && threshold is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a maximum number of changes or a threshold, not both.");
        }

        int minimum = distance ?? (statistic == ChangeStatistic.Mean ? 1 : 2);
        if (statistic != ChangeStatistic.Mean && minimum < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a minimum distance of at least two for that statistic.");
        }

        int[] changes;
        double residual;
        if (columns == 0 || columns < minimum)
        {
            changes = [];
            residual = ChangePointSearch.NoChange(y, statistic);
        }
        else if (threshold is double level)
        {
            (changes, residual) = ChangePointSearch.AboveThreshold(y, statistic, minimum, level);
        }
        else if (most is null || most.Value == 1)
        {
            (changes, residual) = ChangePointSearch.Single(y, statistic, minimum);
            if (changes.Length == 0)
            {
                residual = ChangePointSearch.NoChange(y, statistic);
            }
        }
        else
        {
            (changes, residual) = ChangePointSearch.AtMost(y, statistic, minimum, most.Value);
        }

        var points = new double[changes.Length];
        for (int i = 0; i < changes.Length; i++)
        {
            points[i] = changes[i] + 1;
        }

        JgsValue first = wasColumn
            ? JgsMatrix.FromColumnMajor(points, points.Length, 1)
            : JgsMatrix.FromColumnMajor(points, 1, points.Length);
        return wanted <= 1 ? [first] : [first, JgsValue.Number(residual)];
    }
}
