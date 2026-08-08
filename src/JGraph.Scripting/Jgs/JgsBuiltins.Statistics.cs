using JGraph.Statistics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Statistics Toolbox surface (M53). This file registers the whole toolbox and carries wave B —
/// the descriptive and robust statistics: percentiles, the shape moments, the spreads that survive an
/// outlier, ranks, frequency tables and grouped summaries.
/// </summary>
/// <remarks>
/// Registered after the base builtins and before <c>RegisterMatlabReductions</c>. The dimension
/// handling here is not the reduction wrapper's, because several of these names put something other
/// than the dimension in the slot after the array — <c>trimmean(X, 10, 'floor', 2)</c> has a word
/// there — and because they have to work in both dialects, while the wrapper is MATLAB-only. What is
/// shared is the machinery underneath: <see cref="SliceStatistic"/> cuts and rejoins through the same
/// <see cref="JgsMatrix.SlicesAlong"/> and <see cref="JgsMatrix.JoinAlong"/> the reductions use, so
/// there is one description of what a dimension means.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the Statistics Toolbox builtins.</summary>
    private static void RegisterStatisticsBuiltins(JgsEnvironment env, Random random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        Define("prctile", (args, line, col) => Percentiles("prctile", args, scale: 1, line, col));
        Define("quantile", (args, line, col) => Percentiles("quantile", args, scale: 100, line, col));

        Define("skewness", (args, line, col) => ShapeStatistic("skewness", args, DescriptiveStatistics.Skewness, line, col));
        Define("kurtosis", (args, line, col) => ShapeStatistic("kurtosis", args, DescriptiveStatistics.Kurtosis, line, col));
        Define("moment", CentralMoment);
        Define("mad", MeanAbsoluteDeviation);
        Define("trimmean", TrimmedMean);
        Define("geomean", (args, line, col) =>
            SimpleMean("geomean", args, DescriptiveStatistics.GeometricMean, nanFlag: true, line, col));
        Define("harmmean", (args, line, col) =>
            SimpleMean("harmmean", args, DescriptiveStatistics.HarmonicMean, nanFlag: true, line, col));
        DefineBoth("zscore", StandardScores);

        // MATLAB's range(X) is max - min. JGS has meant range(start, stop, step) since M12 and its
        // surface is frozen, so the statistic replaces the sequence builder in the MATLAB dialect
        // only — the one place in the toolbox where the two dialects answer the same call differently.
        if (dialect.IsMatlab)
        {
            Define("range", (args, line, col) =>
                SimpleMean("range", args, DescriptiveStatistics.Range, nanFlag: false, line, col));
        }

        DefineBoth("tiedrank", TiedRanks);
        Define("tabulate", FrequencyTable);
        DefineBoth("crosstab", CrossTabulate);
        DefineBoth("grpstats", GroupStatistics);

        RegisterDistributionBuiltins(env, random);
        RegisterMultivariateBuiltins(env, random);
        RegisterSamplingBuiltins(env, random, dialect);
        RegisterHypothesisTestBuiltins(env);
        RegisterAnovaBuiltins(env);
        RegisterRegressionBuiltins(env);
        RegisterCorrelationBuiltins(env);
        RegisterEmpiricalBuiltins(env);
        RegisterLegacyNanBuiltins(env);
    }

    // --- Percentiles ------------------------------------------------------------------------------

    /// <summary>
    /// <c>prctile(X, p)</c> and <c>quantile(X, p)</c>: where the sample sits at the given cumulative
    /// probabilities. <paramref name="scale"/> is what turns a probability into a percentage, so the
    /// two names share everything but their argument's units.
    /// </summary>
    /// <remarks>
    /// <c>quantile(X, N)</c> with a whole number above one asks for N evenly spaced quantiles rather
    /// than the quantile at N, which is unambiguous because a probability never exceeds one.
    /// </remarks>
    private static JgsValue Percentiles(
        string name, IReadOnlyList<JgsValue> args, double scale, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        JgsValue subject = args[0];
        JgsValue asked = args[1];

        double[] percents;
        int[] shape;
        if (name == "quantile" && asked.Type == JgsType.Number
            && asked.AsNumber > 1 && asked.AsNumber == Math.Floor(asked.AsNumber))
        {
            int wanted = (int)asked.AsNumber;
            percents = new double[wanted];
            for (int i = 0; i < wanted; i++)
            {
                percents[i] = 100.0 * (i + 1) / (wanted + 1);
            }

            shape = [1, wanted];
        }
        else
        {
            percents = NumericVector(name, asked, line, col);
            for (int i = 0; i < percents.Length; i++)
            {
                percents[i] *= scale;
                if (percents[i] < 0 || percents[i] > 100)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: every probability must lie between 0 and {(scale == 1 ? 100 : 1)}.");
                }
            }

            shape = asked.Type == JgsType.Number ? [1, 1] : JgsMatrix.DimsOf(asked);
        }

        (int? dim, bool all) = Dimension(name, args, 2, line, col);

        // A vector answers in the shape the probabilities were asked in, which is how
        // prctile(x, [25 50 75]) comes back as a row and prctile(x, p(:)) as a column.
        if (all || !IsMatrix(subject))
        {
            double[] flat = FlattenColumnMajor(name, subject, line, col);
            double[] answered = DescriptiveStatistics.Percentiles(flat, percents);
            return answered.Length == 1
                ? JgsValue.Number(answered[0])
                : JgsMatrix.FromColumnMajorDims(answered, shape);
        }

        return SliceStatistic(name, subject, dim, slice => DescriptiveStatistics.Percentiles(slice, percents), line, col);
    }

    // --- Shape and spread -------------------------------------------------------------------------

    /// <summary>
    /// <c>skewness(X, flag, dim)</c> and <c>kurtosis(X, flag, dim)</c>: the flag chooses between the
    /// plain ratio (1, the default) and the bias-corrected one (0).
    /// </summary>
    private static JgsValue ShapeStatistic(
        string name, IReadOnlyList<JgsValue> args, Func<double[], bool, double> statistic, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);
        bool bias = FlagArgument(name, args, 1, line, col);
        (int? dim, bool all) = Dimension(name, args, 2, line, col);
        return Reduce(name, args[0], dim, all, slice => statistic(slice, bias), line, col);
    }

    /// <summary><c>moment(X, order, dim)</c>: the order-th central moment.</summary>
    private static JgsValue CentralMoment(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("moment", args, 2, 3, line, col);
        int order = Count("moment", args, 1, line, col);
        if (order < 1)
        {
            throw new JgsRuntimeException(line, col, "moment: the order must be a positive whole number.");
        }

        (int? dim, bool all) = Dimension("moment", args, 2, line, col);
        return Reduce("moment", args[0], dim, all,
            slice => DescriptiveStatistics.CentralMoment(slice, order), line, col);
    }

    /// <summary>
    /// <c>mad(X, flag, dim)</c>: the mean deviation from the mean by default, the median deviation
    /// from the median when the flag is 1 — the version an outlier cannot move.
    /// </summary>
    private static JgsValue MeanAbsoluteDeviation(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mad", args, 1, 3, line, col);
        bool aroundMedian = args.Count > 1 && !IsPlaceholderValue(args[1])
            && Num("mad", args, 1, line, col) != 0;
        (int? dim, bool all) = Dimension("mad", args, 2, line, col);
        return Reduce("mad", args[0], dim, all,
            slice => DescriptiveStatistics.AbsoluteDeviation(slice, aroundMedian), line, col);
    }

    /// <summary>
    /// <c>trimmean(X, percent, flag, dim)</c>: the mean after the given percentage of observations is
    /// removed, half from each tail.
    /// </summary>
    private static JgsValue TrimmedMean(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("trimmean", args, 2, 4, line, col);
        double percent = Num("trimmean", args, 1, line, col);
        if (!(percent >= 0 && percent < 100))
        {
            throw new JgsRuntimeException(line, col,
                "trimmean: the percentage to trim must be at least 0 and below 100.");
        }

        int next = 2;
        DescriptiveStatistics.TrimRule rule = DescriptiveStatistics.TrimRule.Round;
        if (next < args.Count && args[next].Type == JgsType.String)
        {
            rule = OneWord("trimmean", args, next, line, col, "round", "floor", "weighted") switch
            {
                "floor" => DescriptiveStatistics.TrimRule.Floor,
                "weighted" => DescriptiveStatistics.TrimRule.Weighted,
                _ => DescriptiveStatistics.TrimRule.Round,
            };
            next++;
        }

        (int? dim, bool all) = Dimension("trimmean", args, next, line, col);
        return Reduce("trimmean", args[0], dim, all,
            slice => DescriptiveStatistics.TrimmedMean(slice, percent, rule), line, col);
    }

    /// <summary>
    /// A reduction whose only arguments are the data, where to reduce it, and — for the names that
    /// document one — whether to drop the missing values first.
    /// </summary>
    private static JgsValue SimpleMean(
        string name,
        IReadOnlyList<JgsValue> args,
        Func<double[], double> statistic,
        bool nanFlag,
        int line,
        int col)
    {
        ArityRange(name, args, 1, nanFlag ? 3 : 2, line, col);
        (bool omitNan, IReadOnlyList<JgsValue> rest) = nanFlag
            ? TakeNanFlag(name, args, line, col)
            : (false, args);

        (int? dim, bool all) = Dimension(name, rest, 1, line, col);
        Func<double[], double> reduce = omitNan
            ? slice => statistic(DescriptiveStatistics.WithoutNaN(slice))
            : statistic;
        return Reduce(name, rest[0], dim, all, reduce, line, col);
    }

    /// <summary>
    /// Pulls <c>'omitnan'</c> or <c>'includenan'</c> out of the option tail wherever it sits, leaving
    /// the rest of the arguments in place — the same rule the reduction wrapper follows, so a script
    /// can write the word before or after the dimension.
    /// </summary>
    private static (bool OmitNan, IReadOnlyList<JgsValue> Remaining) TakeNanFlag(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool omitNan = false;
        var rest = new List<JgsValue> { args[0] };
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                string word = args[i].AsString;
                if (string.Equals(word, "omitnan", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(word, "includenan", StringComparison.OrdinalIgnoreCase))
                {
                    omitNan = string.Equals(word, "omitnan", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
            }

            rest.Add(args[i]);
        }

        _ = (name, line, col);
        return (omitNan, rest);
    }

    /// <summary>
    /// <c>[Z, mu, sigma] = zscore(X, flag, dim)</c>: how many standard deviations from the mean each
    /// observation sits. The answer keeps the shape of the input, and the centre and spread come back
    /// in the shape a reduction along the same dimension would have.
    /// </summary>
    private static JgsValue[] StandardScores(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("zscore", args, 1, 3, line, col);
        bool population = args.Count > 1 && !IsPlaceholderValue(args[1])
            && Num("zscore", args, 1, line, col) != 0;
        (int? dim, bool all) = Dimension("zscore", args, 2, line, col);

        var centres = new List<double>();
        var spreads = new List<double>();
        double[] Score(double[] slice)
        {
            (double[] scores, double centre, double spread) =
                DescriptiveStatistics.StandardScores(slice, population);
            centres.Add(centre);
            spreads.Add(spread);
            return scores;
        }

        JgsValue z;
        JgsValue mu;
        JgsValue sigma;
        if (all || !IsMatrix(args[0]))
        {
            double[] flat = FlattenColumnMajor("zscore", args[0], line, col);
            z = KeepingShape(args[0], Score(flat));
            mu = JgsValue.Number(centres[0]);
            sigma = JgsValue.Number(spreads[0]);
        }
        else
        {
            z = SliceStatistic("zscore", args[0], dim, Score, line, col);
            (mu, sigma) = (SummaryShape(args[0], dim, centres), SummaryShape(args[0], dim, spreads));
        }

        return Outputs(wanted, z, mu, sigma);
    }

    // --- Ranks, tables and groups -----------------------------------------------------------------

    /// <summary>
    /// <c>[R, TIEADJ] = tiedrank(X, tieflag, bootflag)</c>: ranks from smallest to largest, tied
    /// observations sharing the average of the ranks they cover, plus the tie correction the rank
    /// tests need. The second flag ranks from the outside in, which is what Ansari-Bradley wants.
    /// </summary>
    private static JgsValue[] TiedRanks(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tiedrank", args, 1, 3, line, col);
        bool pairCount = args.Count > 1 && !IsPlaceholderValue(args[1])
            && Num("tiedrank", args, 1, line, col) != 0;
        bool fromOutside = args.Count > 2 && !IsPlaceholderValue(args[2])
            && Num("tiedrank", args, 2, line, col) != 0;

        DescriptiveStatistics.TieAdjustment adjustment = pairCount
            ? DescriptiveStatistics.TieAdjustment.PairCount
            : DescriptiveStatistics.TieAdjustment.RankSumOfCubes;

        var adjustments = new List<double>();
        double[] Rank(double[] slice)
        {
            (double[] ranks, double adjusted) =
                DescriptiveStatistics.TiedRanks(slice, adjustment, fromOutside);
            adjustments.Add(adjusted);
            return ranks;
        }

        if (!IsMatrix(args[0]))
        {
            double[] flat = FlattenColumnMajor("tiedrank", args[0], line, col);
            return Outputs(wanted, KeepingShape(args[0], Rank(flat)), JgsValue.Number(adjustments[0]));
        }

        JgsValue ranked = SliceStatistic("tiedrank", args[0], dim: 1, Rank, line, col);
        return Outputs(wanted, ranked, SummaryShape(args[0], 1, adjustments));
    }

    /// <summary>
    /// <c>tabulate(x)</c>: a row per value with how often it occurred and its share of the sample.
    /// A sample of positive whole numbers gets a row for every integer up to the largest, including
    /// the ones nobody took, so the table lines up with an index.
    /// </summary>
    private static JgsValue FrequencyTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("tabulate", args, 1, line, col);

        // A cell of words or a string array is tabulated by label, and the labels have to come back
        // as labels — so that form answers a cell, exactly as MATLAB's does.
        if (args[0].Type is JgsType.Cell or JgsType.String)
        {
            return LabelledFrequencyTable(args[0], line, col);
        }

        double[] values = FlattenColumnMajor("tabulate", args[0], line, col);
        DescriptiveStatistics.FrequencyRow[] rows = DescriptiveStatistics.Tabulate(values);
        var flat = new double[rows.Length * 3];
        for (int i = 0; i < rows.Length; i++)
        {
            flat[i] = rows[i].Value;
            flat[i + rows.Length] = rows[i].Count;
            flat[i + (2 * rows.Length)] = rows[i].Percent;
        }

        return JgsMatrix.FromColumnMajor(flat, rows.Length, 3);
    }

    private static JgsValue LabelledFrequencyTable(JgsValue subject, int line, int col)
    {
        string[] labels = TextElements("tabulate", subject, line, col);
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string label in labels)
        {
            if (!counts.TryGetValue(label, out int seen))
            {
                order.Add(label);
                seen = 0;
            }

            counts[label] = seen + 1;
        }

        order.Sort(StringComparer.Ordinal);
        int rows = order.Count;
        var cells = new JgsValue[rows * 3];
        for (int i = 0; i < rows; i++)
        {
            int count = counts[order[i]];
            cells[i] = JgsValue.Str(order[i]);
            cells[i + rows] = JgsValue.Number(count);
            cells[i + (2 * rows)] = JgsValue.Number(labels.Length == 0 ? 0 : 100.0 * count / labels.Length);
        }

        JgsValue table = JgsValue.Cell(cells);
        table.Reshape(rows, 3);
        return table;
    }

    /// <summary>
    /// <c>[TBL, CHI2, P, LABELS] = crosstab(x1, x2, ...)</c>: how often each combination of grouping
    /// values occurs, with the chi-square test of independence for the two-variable case.
    /// </summary>
    private static JgsValue[] CrossTabulate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "crosstab needs at least one grouping variable.");
        }

        var groups = new List<(int[] Index, string[] Labels)>();
        int observations = -1;
        foreach (JgsValue argument in args)
        {
            (int[] index, string[] labels) = GroupIndex("crosstab", argument, line, col);
            if (observations >= 0 && index.Length != observations)
            {
                throw new JgsRuntimeException(line, col,
                    "crosstab: every grouping variable must have the same number of observations.");
            }

            observations = index.Length;
            groups.Add((index, labels));
        }

        var dims = new int[groups.Count];
        for (int g = 0; g < groups.Count; g++)
        {
            dims[g] = groups[g].Labels.Length;
        }

        int total = 1;
        foreach (int size in dims)
        {
            total *= size;
        }

        var table = new double[total];
        for (int observation = 0; observation < observations; observation++)
        {
            int offset = 0;
            int stride = 1;
            bool known = true;
            for (int g = 0; g < groups.Count; g++)
            {
                int at = groups[g].Index[observation];
                if (at < 0)
                {
                    known = false; // a missing grouping value takes the observation out of the table
                    break;
                }

                offset += at * stride;
                stride *= dims[g];
            }

            if (known)
            {
                table[offset]++;
            }
        }

        JgsValue counts = dims.Length == 1
            ? JgsMatrix.FromColumnMajor(table, dims[0], 1)
            : JgsMatrix.FromColumnMajorDims(table, dims);

        (double chi2, double p) = groups.Count == 2
            ? IndependenceTest(table, dims[0], dims[1])
            : (double.NaN, double.NaN);

        return Outputs(wanted, counts, JgsValue.Number(chi2), JgsValue.Number(p), GroupLabels(groups));
    }

    /// <summary>
    /// Pearson's chi-square test that two grouping variables are independent: how far the observed
    /// counts sit from the ones the row and column totals alone would predict.
    /// </summary>
    private static (double ChiSquare, double PValue) IndependenceTest(double[] table, int rows, int cols)
    {
        var rowTotal = new double[rows];
        var colTotal = new double[cols];
        double grand = 0;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                double count = table[r + (c * rows)];
                rowTotal[r] += count;
                colTotal[c] += count;
                grand += count;
            }
        }

        if (grand == 0 || rows < 2 || cols < 2)
        {
            return (double.NaN, double.NaN);
        }

        double statistic = 0;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                double expected = rowTotal[r] * colTotal[c] / grand;
                if (expected <= 0)
                {
                    continue;
                }

                double difference = table[r + (c * rows)] - expected;
                statistic += difference * difference / expected;
            }
        }

        // The chi-square distribution's upper tail is the regularized upper incomplete gamma, so the
        // p-value is read straight off it rather than as one minus a cumulative probability that would
        // lose its precision in the far tail.
        double df = (rows - 1.0) * (cols - 1.0);
        return (statistic, JGraph.Numerics.SpecialFunctions.GammaUpper(df / 2, statistic / 2));
    }

    private static JgsValue GroupLabels(List<(int[] Index, string[] Labels)> groups)
    {
        int rows = 0;
        foreach ((int[] _, string[] labels) in groups)
        {
            rows = Math.Max(rows, labels.Length);
        }

        var cells = new JgsValue[rows * groups.Count];
        for (int g = 0; g < groups.Count; g++)
        {
            for (int r = 0; r < rows; r++)
            {
                // Shorter columns are padded with the empty label, because a cell array is rectangular
                // and the variables need not have the same number of groups.
                cells[r + (g * rows)] = JgsValue.Str(
                    r < groups[g].Labels.Length ? groups[g].Labels[r] : string.Empty);
            }
        }

        JgsValue labelCell = JgsValue.Cell(cells);
        labelCell.Reshape(rows, groups.Count);
        return labelCell;
    }

    /// <summary>
    /// <c>[MEANS, SEM, COUNTS, NAMES] = grpstats(X, group)</c>: the mean of each group, the standard
    /// error of that mean, how many observations went into it, and what the group was called. A matrix
    /// is summarized column by column, so each output has one row per group and one column per
    /// variable.
    /// </summary>
    private static JgsValue[] GroupStatistics(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("grpstats", args, 1, 3, line, col);

        double[] flat = FlattenColumnMajor("grpstats", args[0], line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                "grpstats takes a vector or a matrix, not an array with more than two dimensions.");
        }

        int rows = args[0].Type == JgsType.Array ? dims[0] : 1;
        int columns = flat.Length == 0 ? 0 : flat.Length / Math.Max(rows, 1);
        if (rows == 1 && columns > 1)
        {
            // A row vector is one variable observed several times, which is what every other
            // statistic here reads it as.
            (rows, columns) = (columns, 1);
        }

        int[] index;
        string[] names;
        if (args.Count > 1 && !IsPlaceholderValue(args[1]))
        {
            (index, names) = GroupIndex("grpstats", args[1], line, col);
            if (index.Length != rows)
            {
                throw new JgsRuntimeException(line, col,
                    $"grpstats: the grouping variable has {index.Length} entries but the data has {rows} rows.");
            }
        }
        else
        {
            index = new int[rows];
            names = ["1"];
        }

        int groups = names.Length;
        var means = new double[groups * columns];
        var errors = new double[groups * columns];
        var counts = new double[groups * columns];

        for (int c = 0; c < columns; c++)
        {
            var buckets = new List<double>[groups];
            for (int g = 0; g < groups; g++)
            {
                buckets[g] = [];
            }

            for (int r = 0; r < rows; r++)
            {
                if (index[r] >= 0)
                {
                    buckets[index[r]].Add(flat[r + (c * rows)]);
                }
            }

            for (int g = 0; g < groups; g++)
            {
                double[] values = DescriptiveStatistics.WithoutNaN(buckets[g]);
                means[g + (c * groups)] = DescriptiveStatistics.Mean(values);
                counts[g + (c * groups)] = values.Length;
                errors[g + (c * groups)] = values.Length == 0
                    ? double.NaN
                    : DescriptiveStatistics.StandardDeviation(values, population: false) / Math.Sqrt(values.Length);
            }
        }

        var labels = new JgsValue[groups];
        for (int g = 0; g < groups; g++)
        {
            labels[g] = JgsValue.Str(names[g]);
        }

        JgsValue nameCell = JgsValue.Cell(labels);
        nameCell.Reshape(groups, 1);

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(means, groups, columns),
            JgsMatrix.FromColumnMajor(errors, groups, columns),
            JgsMatrix.FromColumnMajor(counts, groups, columns),
            nameCell);
    }

    // --- Shared argument reading and shaping ------------------------------------------------------

    /// <summary>
    /// The dimension a statistic reduces along, read from <paramref name="slot"/> onwards: a number
    /// names one, <c>'all'</c> asks for every value at once, and nothing at all takes MATLAB's default
    /// of the first dimension that is not a singleton.
    /// </summary>
    private static (int? Dim, bool All) Dimension(
        string name, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        if (slot >= args.Count || IsPlaceholderValue(args[slot]))
        {
            return (null, false);
        }

        if (args[slot].Type == JgsType.String)
        {
            OneWord(name, args, slot, line, col, "all");
            return (null, true);
        }

        int dim = Count(name, args, slot, line, col);
        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the dimension must be a positive whole number, but was {dim}.");
        }

        return (dim, false);
    }

    /// <summary>A statistic that answers one number per slice.</summary>
    private static JgsValue Reduce(
        string name, JgsValue subject, int? dim, bool all, Func<double[], double> statistic, int line, int col)
    {
        if (all || !IsMatrix(subject))
        {
            return JgsValue.Number(statistic(FlattenColumnMajor(name, subject, line, col)));
        }

        return SliceStatistic(name, subject, dim, slice => [statistic(slice)], line, col);
    }

    /// <summary>
    /// A statistic applied to every one-dimensional slice along a dimension, the answers scattered
    /// back where the slices came from. Every slice has to answer the same number of values, which is
    /// how one value per slice and a whole vector per slice are the same operation.
    /// </summary>
    private static JgsValue SliceStatistic(
        string name, JgsValue subject, int? dim, Func<double[], double[]> statistic, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(subject);
        int along = dim ?? JgsMatrix.DefaultDim(dims);
        double[] flat = FlattenColumnMajor(name, subject, line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, along);

        var answered = new double[slices.Length][];
        for (int i = 0; i < slices.Length; i++)
        {
            answered[i] = statistic(slices[i]);
            if (answered[i].Length != answered[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} gave slices of different lengths ({answered[0].Length} and {answered[i].Length}), so the result has no shape.");
            }
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(answered, dims, along);
        if (joined.Length == 0)
        {
            return JgsValue.Array([]);
        }

        return joined.Length == 1 ? JgsValue.Number(joined[0]) : JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    /// <summary>One value per slice, in the shape a reduction along that dimension takes.</summary>
    private static JgsValue SummaryShape(JgsValue subject, int? dim, List<double> perSlice)
    {
        if (perSlice.Count == 1)
        {
            return JgsValue.Number(perSlice[0]);
        }

        int[] dims = JgsMatrix.DimsOf(subject);
        int along = dim ?? JgsMatrix.DefaultDim(dims);
        var slices = new double[perSlice.Count][];
        for (int i = 0; i < perSlice.Count; i++)
        {
            slices[i] = [perSlice[i]];
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(slices, dims, along);
        return JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    /// <summary>An answer the length of its input, wearing the input's own shape.</summary>
    private static JgsValue KeepingShape(JgsValue subject, double[] values)
    {
        if (values.Length == 1)
        {
            return JgsValue.Number(values[0]);
        }

        int[] dims = JgsMatrix.DimsOf(subject);
        return JgsMatrix.FromColumnMajorDims(values, dims);
    }

    /// <summary>Whether a value has more than one dimension worth slicing — a matrix or an N-D array.</summary>
    private static bool IsMatrix(JgsValue value)
    {
        if (value.Type != JgsType.Array)
        {
            return false;
        }

        int[] dims = JgsMatrix.DimsOf(value);
        int nonSingleton = 0;
        foreach (int size in dims)
        {
            if (size != 1)
            {
                nonSingleton++;
            }
        }

        return nonSingleton > 1;
    }

    /// <summary>Whether an argument is the <c>[]</c> that asks for a default.</summary>
    private static bool IsPlaceholderValue(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0;

    /// <summary>
    /// A leading flag argument that chooses between two conventions — 1 (the default) for the plain
    /// statistic, 0 for the bias-corrected one. <c>[]</c> asks for the default.
    /// </summary>
    private static bool FlagArgument(string name, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        if (slot >= args.Count || IsPlaceholderValue(args[slot]))
        {
            return true;
        }

        double flag = Num(name, args, slot, line, col);
        if (flag is not (0 or 1))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the flag is 0 or 1, but was {flag}.");
        }

        return flag == 1;
    }

    /// <summary>
    /// A grouping variable read as an index per observation plus the group names, in the order MATLAB
    /// orders them: numbers ascending, words alphabetically. A missing value takes its observation out
    /// of every group, which is the −1 the callers skip.
    /// </summary>
    private static (int[] Index, string[] Names) GroupIndex(
        string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Cell or JgsType.String)
        {
            string[] labels = TextElements(name, value, line, col);
            var distinct = new List<string>();
            foreach (string label in labels)
            {
                if (!distinct.Contains(label, StringComparer.Ordinal))
                {
                    distinct.Add(label);
                }
            }

            distinct.Sort(StringComparer.Ordinal);
            var indices = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                indices[i] = distinct.IndexOf(labels[i]);
            }

            return (indices, [.. distinct]);
        }

        double[] numbers = FlattenColumnMajor(name, value, line, col);
        var levels = new List<double>();
        foreach (double number in numbers)
        {
            if (!double.IsNaN(number) && !levels.Contains(number))
            {
                levels.Add(number);
            }
        }

        levels.Sort();
        var index = new int[numbers.Length];
        var names = new string[levels.Count];
        for (int i = 0; i < levels.Count; i++)
        {
            names[i] = FormatNumber(levels[i]);
        }

        for (int i = 0; i < numbers.Length; i++)
        {
            index[i] = double.IsNaN(numbers[i]) ? -1 : levels.IndexOf(numbers[i]);
        }

        return (index, names);
    }

    /// <summary>The words in a cell of char rows, a string array, or a single string.</summary>
    private static string[] TextElements(string name, JgsValue value, int line, int col)
    {
        switch (value.Type)
        {
            case JgsType.String:
                return [value.AsString];

            case JgsType.Cell:
                JgsValue[] cells = value.AsCell;
                var words = new string[cells.Length];
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].Type != JgsType.String)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name}: a cell of grouping labels holds text, but element {i + 1} does not.");
                    }

                    words[i] = cells[i].AsString;
                }

                return words;

            default:
                throw new JgsRuntimeException(line, col, $"{name}: expected text or a cell of text.");
        }
    }

    /// <summary>A number as MATLAB writes it in a group name: whole numbers without a decimal point.</summary>
    private static string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
