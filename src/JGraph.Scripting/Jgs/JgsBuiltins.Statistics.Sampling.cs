using JGraph.Statistics.Distributions;
using JGraph.Statistics.Sampling;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave E, part two: choosing points rather than computing probabilities — weighted draws from a
/// population, gamma variates, Latin hypercube designs, and the three resampling verbs.
/// </summary>
/// <remarks>
/// <para>
/// Everything here draws from the one stream <c>rng</c> seeds, so a seeded script repeats itself
/// exactly. It does not repeat MATLAB: the generator is a different one, recorded as a divergence since
/// M52.
/// </para>
/// <para>
/// The resampling three — <c>bootstrp</c>, <c>bootci</c> and <c>jackknife</c> — are the first builtins
/// in the statistics surface that call back into the script, because their subject is a function the
/// caller wrote. Each resample selects <em>rows</em>, and every data argument is re-indexed by the same
/// row numbers, so a statistic of several parallel variables stays paired up.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec DataSampleOptions = new(
        "datasample", [], ["Replace", "Weights"]);

    private static readonly OptionSpec LatinDesignOptions = new(
        "lhsdesign", [], ["criterion", "iterations", "smooth"]);

    private static readonly OptionSpec BootstrapOptions = new(
        "bootstrp", [], ["Weights", "Options"]);

    private static readonly OptionSpec BootstrapIntervalOptions = new(
        "bootci", [], ["alpha", "type", "Weights", "Options", "nbootstd", "stderr"]);

    /// <summary>Registers the sampling and resampling builtins.</summary>
    private static void RegisterSamplingBuiltins(JgsEnvironment env, Random random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("randsample", (args, line, col) => RandomSample(random, dialect, args, line, col));
        Define("datasample",
            (args, line, col) => DataSample(random, dialect, args, 1, line, col)[0],
            (args, wanted, line, col) => DataSample(random, dialect, args, wanted, line, col));
        Define("randg", (args, line, col) => GammaDraw(random, args, line, col));

        Define("lhsdesign", (args, line, col) => LatinDesign(random, args, line, col));
        Define("lhsnorm",
            (args, line, col) => LatinNormal(random, args, 1, line, col)[0],
            (args, wanted, line, col) => LatinNormal(random, args, wanted, line, col));

        Define("bootstrp",
            (args, line, col) => BootstrapReplicates(random, dialect, args, 1, line, col)[0],
            (args, wanted, line, col) => BootstrapReplicates(random, dialect, args, wanted, line, col));
        Define("bootci", (args, line, col) => BootstrapInterval(random, args, line, col));
        Define("jackknife", (args, line, col) => JackknifeReplicates(args, line, col));

        Define("combnk", (args, line, col) => Combinations(args, line, col));
    }

    // --- Draws from a population -------------------------------------------------------------------

    /// <summary>
    /// <c>y = randsample(population, k, replace, w)</c>: k values from a population, which is either a
    /// vector of values or a count standing for the integers up to it.
    /// </summary>
    private static JgsValue RandomSample(
        Random random, JgsDialect dialect, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("randsample", args, 2, 4, line, col);

        bool fromCount = args[0].Type is JgsType.Number or JgsType.Bool;
        double[] population = fromCount
            ? []
            : ToDoubles("randsample", args[0], line, col);
        int size = fromCount ? Count("randsample", args, 0, line, col) : population.Length;
        int wanted = Count("randsample", args, 1, line, col);

        if (size <= 0)
        {
            throw new JgsRuntimeException(line, col, "randsample: the population must not be empty.");
        }

        bool replacement = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? Truthy("randsample", args[2], line, col)
            : false;
        double[]? weights = args.Count > 3 && !IsPlaceholderValue(args[3])
            ? ToDoubles("randsample", args[3], line, col)
            : null;

        if (weights is not null && weights.Length != size)
        {
            throw new JgsRuntimeException(line, col,
                $"randsample: {weights.Length} weights for a population of {size}.");
        }

        int[] picks = Sampled("randsample", random, size, weights, wanted, replacement, line, col);
        var values = new double[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            values[i] = fromCount ? picks[i] + dialect.IndexBase : population[picks[i]];
        }

        // A population written as a row answers as a row; the integer form and a column population
        // answer as a column, which is what MATLAB's own indexing of the population would produce.
        bool row = !fromCount && IsRowShaped(args[0]);
        return Oriented(values, row);
    }

    /// <summary>
    /// <c>[y, idx] = datasample(data, k, dim)</c>: k rows (or columns, or elements) of the data, drawn
    /// with replacement unless told otherwise.
    /// </summary>
    private static JgsValue[] DataSample(
        Random random, JgsDialect dialect, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = DataSampleOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count is not (2 or 3))
        {
            throw new JgsRuntimeException(line, col,
                "datasample(data, k) or datasample(data, k, dim) takes k observations from the data.");
        }

        JgsValue data = parsed.Positional[0];
        int asked = Count("datasample", parsed.Positional, 1, line, col);
        (double[] flat, int rows, int columns) = DenseMatrix("datasample", data, line, col);

        int dim;
        if (parsed.Positional.Count == 3)
        {
            dim = Count("datasample", parsed.Positional, 2, line, col);
            if (dim is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col, "datasample: the dimension must be 1 or 2.");
            }
        }
        else
        {
            // A vector is sampled along the direction it runs in; a matrix along its rows.
            dim = rows == 1 && columns > 1 ? 2 : 1;
        }

        int population = dim == 1 ? rows : columns;
        bool replacement = parsed.Flag("Replace", true);
        double[]? weights = parsed.Vector("Weights");
        if (weights is not null && weights.Length != population)
        {
            throw new JgsRuntimeException(line, col,
                $"datasample: {weights.Length} weights for {population} observations along dimension {dim}.");
        }

        int[] picks = Sampled("datasample", random, population, weights, asked, replacement, line, col);

        int outRows = dim == 1 ? picks.Length : rows;
        int outColumns = dim == 1 ? columns : picks.Length;
        var taken = new double[outRows * outColumns];
        for (int i = 0; i < picks.Length; i++)
        {
            if (dim == 1)
            {
                for (int c = 0; c < columns; c++)
                {
                    taken[i + (c * outRows)] = flat[picks[i] + (c * rows)];
                }
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    taken[r + (i * rows)] = flat[r + (picks[i] * rows)];
                }
            }
        }

        JgsValue answer = outRows == 1 || outColumns == 1
            ? Oriented(taken, outRows == 1)
            : JgsMatrix.FromColumnMajor(taken, outRows, outColumns);

        if (wanted <= 1)
        {
            return [answer];
        }

        var indices = new double[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            indices[i] = picks[i] + dialect.IndexBase;
        }

        return [answer, Oriented(indices, dim == 2)];
    }

    /// <summary><c>randg(A, m, n)</c>: gamma variates of shape A and unit scale.</summary>
    private static JgsValue GammaDraw(Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] shapes = args.Count == 0 ? [1] : ToDoubles("randg", args[0], line, col);
        foreach (double shape in shapes)
        {
            if (!(shape > 0))
            {
                throw new JgsRuntimeException(line, col, "randg: the shape must be above zero.");
            }
        }

        int[] dims;
        if (args.Count > 1)
        {
            dims = SquareDims("randg", args.Skip(1).ToList(), line, col);
            long total = 1;
            foreach (int dim in dims)
            {
                total *= dim;
            }

            if (shapes.Length != 1 && shapes.Length != total)
            {
                throw new JgsRuntimeException(line, col,
                    $"randg: {shapes.Length} shapes cannot fill {total} values.");
            }
        }
        else
        {
            dims = args.Count == 0 ? [1, 1] : JgsMatrix.DimsOf(args[0]);
            long recorded = 1;
            foreach (int dim in dims)
            {
                recorded *= dim;
            }

            if (recorded != shapes.Length)
            {
                dims = [1, shapes.Length];
            }
        }

        long count = 1;
        foreach (int dim in dims)
        {
            count *= dim;
        }

        var draws = new double[count];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = ContinuousDistributions.SampleGamma(
                random, shapes.Length == 1 ? shapes[0] : shapes[i], 1);
        }

        return draws.Length == 1 ? JgsValue.Number(draws[0]) : JgsMatrix.FromColumnMajorDims(draws, dims);
    }

    // --- Latin hypercube designs -------------------------------------------------------------------

    /// <summary><c>X = lhsdesign(n, p)</c>: n points, one in every stratum of each of p variables.</summary>
    private static JgsValue LatinDesign(Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = LatinDesignOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "lhsdesign(n, p) makes n points in p variables.");
        }

        int n = Count("lhsdesign", parsed.Positional, 0, line, col);
        int p = Count("lhsdesign", parsed.Positional, 1, line, col);
        bool smooth = !string.Equals(
            parsed.Word("smooth", "on", "on", "off"), "off", StringComparison.OrdinalIgnoreCase);
        int iterations = parsed.Whole("iterations", 5);
        SamplePlans.LatinCriterion criterion =
            parsed.Word("criterion", "none", "none", "maximin", "correlation").ToLowerInvariant() switch
            {
                "maximin" => SamplePlans.LatinCriterion.Maximin,
                "correlation" => SamplePlans.LatinCriterion.Correlation,
                _ => SamplePlans.LatinCriterion.None,
            };

        double[,] design = SamplePlans.LatinHypercube(
            random, Math.Max(0, n), Math.Max(0, p), smooth, criterion, iterations);
        return FromDense(design);
    }

    /// <summary>
    /// <c>[X, Z] = lhsnorm(mu, sigma, n)</c>: a multivariate normal sample whose every marginal is
    /// stratified.
    /// </summary>
    /// <remarks>
    /// The sample is an ordinary multivariate normal draw whose values are then moved, in rank order,
    /// onto the stratum midpoints of the same marginal. Ranks are what carry the correlation, so the
    /// covariance survives the substitution while every variable gains one point per stratum.
    /// </remarks>
    private static JgsValue[] LatinNormal(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("lhsnorm", args, 3, 4, line, col);
        (double[][] meanRows, int width) = Observations("lhsnorm", args[0], line, col);
        if (meanRows.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "lhsnorm: mu must be one row of means.");
        }

        double[,] sigma = CovarianceArgument("lhsnorm", args, 1, width, line, col);
        int n = Math.Max(0, Count("lhsnorm", args, 2, line, col));
        bool smooth = args.Count <= 3
            || args[3].Type != JgsType.String
            || !string.Equals(args[3].AsString, "off", StringComparison.OrdinalIgnoreCase);

        (double[,] Factor, int Rank)? factored = Multivariate.CovarianceFactor(sigma);
        if (factored is null)
        {
            throw new JgsRuntimeException(line, col, "lhsnorm: sigma must be a covariance matrix.");
        }

        var raw = new double[n, width];
        for (int i = 0; i < n; i++)
        {
            double[] draw = Multivariate.NormalSample(random, meanRows[0], factored.Value.Factor);
            for (int v = 0; v < width; v++)
            {
                raw[i, v] = draw[v];
            }
        }

        var stratified = new double[n, width];
        var order = new int[n];
        for (int v = 0; v < width; v++)
        {
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }

            int variable = v;
            Array.Sort(order, (a, b) => raw[a, variable].CompareTo(raw[b, variable]));

            double sd = Math.Sqrt(sigma[v, v]);
            for (int rank = 0; rank < n; rank++)
            {
                double offset = smooth ? random.NextDouble() : 0.5;
                stratified[order[rank], v] =
                    ContinuousDistributions.NormalInv((rank + offset) / n, meanRows[0][v], sd);
            }
        }

        JgsValue answer = FromDense(stratified);
        return wanted <= 1 ? [answer] : [answer, FromDense(raw)];
    }

    // --- Resampling ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[bootstat, bootsam] = bootstrp(nboot, bootfun, d1, …)</c>: the statistic recomputed on nboot
    /// resamples of the rows.
    /// </summary>
    private static JgsValue[] BootstrapReplicates(
        Random random, JgsDialect dialect, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = BootstrapOptions.Parse(args, int.MaxValue, line, col);
        if (parsed.Positional.Count < 3 || parsed.Positional[1].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                "bootstrp(nboot, bootfun, d1, …) needs a resample count, a function handle and the data.");
        }

        int replicates = Count("bootstrp", parsed.Positional, 0, line, col);
        IJgsCallable statistic = parsed.Positional[1].AsCallable;
        List<JgsValue> data = parsed.Positional.Skip(2).ToList();
        int n = SharedRowCount("bootstrp", data, line, col);
        double[]? weights = ObservationWeights("bootstrp", parsed.Vector("Weights"), n, line, col);

        var rows = new List<double[]>(replicates);
        var samples = new double[Math.Max(0, replicates) * n];
        for (int b = 0; b < replicates; b++)
        {
            int[] picks = SamplePlans.WeightedSample(random, n, weights, n, replacement: true);
            rows.Add(StatisticOf("bootstrp", statistic, data, picks, line, col));
            for (int i = 0; i < n; i++)
            {
                samples[i + (b * n)] = picks[i] + dialect.IndexBase;
            }
        }

        JgsValue answer = Stacked(rows);
        return wanted <= 1
            ? [answer]
            : [answer, JgsMatrix.FromColumnMajor(samples, n, Math.Max(0, replicates))];
    }

    /// <summary>
    /// <c>ci = bootci(nboot, bootfun, d1, …)</c>: a confidence interval for whatever the statistic is,
    /// two rows — the lower limits and the upper.
    /// </summary>
    private static JgsValue BootstrapInterval(
        Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = BootstrapIntervalOptions.Parse(args, int.MaxValue, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "bootci(nboot, bootfun, d1, …) or bootci(nboot, {bootfun, d1, …}) needs a statistic and data.");
        }

        int replicates = Count("bootci", parsed.Positional, 0, line, col);

        // MathWorks gives the statistic and its data two spellings: laid out as arguments, or gathered
        // into one cell so the option tail is unambiguous. Both mean the same call.
        IJgsCallable statistic;
        List<JgsValue> data;
        if (parsed.Positional[1].Type == JgsType.Cell)
        {
            JgsValue[] cell = parsed.Positional[1].AsCell;
            if (cell.Length < 2 || cell[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col,
                    "bootci: the cell must hold the function handle followed by its data.");
            }

            statistic = cell[0].AsCallable;
            data = cell.Skip(1).ToList();
        }
        else if (parsed.Positional[1].Type == JgsType.Function)
        {
            statistic = parsed.Positional[1].AsCallable;
            data = parsed.Positional.Skip(2).ToList();
        }
        else
        {
            throw new JgsRuntimeException(line, col, "bootci: the second argument is the statistic to bootstrap.");
        }

        if (data.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "bootci: the statistic needs data to resample.");
        }

        double alpha = parsed.Scalar("alpha", 0.05);
        if (!(alpha > 0) || !(alpha < 1))
        {
            throw new JgsRuntimeException(line, col, "bootci: alpha must be one number between 0 and 1.");
        }

        string type = parsed.Word(
            "type", "bca", "bca", "norm", "normal", "per", "percentile", "cper", "corrected percentile",
            "stud", "student");
        if (type is "stud" or "student")
        {
            throw new JgsRuntimeException(line, col,
                "bootci: the studentized interval needs a bootstrap inside every bootstrap, which is "
                + "not computed here — use 'bca', 'per', 'cper' or 'norm'.");
        }

        Bootstrap.IntervalMethod method = type switch
        {
            "norm" or "normal" => Bootstrap.IntervalMethod.Normal,
            "per" or "percentile" => Bootstrap.IntervalMethod.Percentile,
            "cper" or "corrected percentile" => Bootstrap.IntervalMethod.BiasCorrected,
            _ => Bootstrap.IntervalMethod.Accelerated,
        };

        int n = SharedRowCount("bootci", data, line, col);
        double[]? weights = ObservationWeights("bootci", parsed.Vector("Weights"), n, line, col);

        int[] everything = Enumerable.Range(0, n).ToArray();
        double[] observed = StatisticOf("bootci", statistic, data, everything, line, col);
        int width = observed.Length;

        var replicated = new double[width][];
        for (int k = 0; k < width; k++)
        {
            replicated[k] = new double[replicates];
        }

        for (int b = 0; b < replicates; b++)
        {
            int[] picks = SamplePlans.WeightedSample(random, n, weights, n, replacement: true);
            double[] value = StatisticOf("bootci", statistic, data, picks, line, col);
            EnsureWidth("bootci", value.Length, width, line, col);
            for (int k = 0; k < width; k++)
            {
                replicated[k][b] = value[k];
            }
        }

        double[][]? jackknife = null;
        if (method == Bootstrap.IntervalMethod.Accelerated)
        {
            jackknife = new double[width][];
            for (int k = 0; k < width; k++)
            {
                jackknife[k] = new double[n];
            }

            for (int leave = 0; leave < n; leave++)
            {
                int[] picks = everything.Where(i => i != leave).ToArray();
                double[] value = StatisticOf("bootci", statistic, data, picks, line, col);
                EnsureWidth("bootci", value.Length, width, line, col);
                for (int k = 0; k < width; k++)
                {
                    jackknife[k][leave] = value[k];
                }
            }
        }

        var limits = new double[2 * width];
        for (int k = 0; k < width; k++)
        {
            (double lower, double upper) = Bootstrap.Interval(
                method, replicated[k], observed[k], jackknife?[k], alpha);
            limits[k * 2] = lower;
            limits[(k * 2) + 1] = upper;
        }

        return JgsMatrix.FromColumnMajor(limits, 2, width);
    }

    /// <summary>
    /// <c>jackstat = jackknife(jackfun, X, …)</c>: the statistic recomputed with each observation left
    /// out, one row per observation.
    /// </summary>
    private static JgsValue JackknifeReplicates(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                "jackknife(jackfun, X, …) needs a function handle and the data.");
        }

        IJgsCallable statistic = args[0].AsCallable;
        List<JgsValue> data = args.Skip(1).Where(value => value.Type != JgsType.Struct).ToList();
        int n = SharedRowCount("jackknife", data, line, col);

        var rows = new List<double[]>(n);
        for (int leave = 0; leave < n; leave++)
        {
            int[] picks = new int[n - 1];
            for (int i = 0, k = 0; i < n; i++)
            {
                if (i != leave)
                {
                    picks[k++] = i;
                }
            }

            rows.Add(StatisticOf("jackknife", statistic, data, picks, line, col));
        }

        return Stacked(rows);
    }

    /// <summary><c>combnk(v, k)</c>: every way of choosing k of the values, one combination per row.</summary>
    private static JgsValue Combinations(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("combnk", args, 2, line, col);
        int k = Count("combnk", args, 1, line, col);

        // A char row is a population of letters, which is MathWorks' own documented example.
        bool text = args[0].Type == JgsType.String;
        double[] values = text
            ? args[0].AsString.Select(character => (double)character).ToArray()
            : ToDoubles("combnk", args[0], line, col);

        List<int[]> chosen = SamplePlans.Combinations(values.Length, k);
        if (chosen.Count == 0)
        {
            return JgsValue.Array([]);
        }

        var flat = new double[chosen.Count * k];
        for (int r = 0; r < chosen.Count; r++)
        {
            for (int c = 0; c < k; c++)
            {
                flat[r + (c * chosen.Count)] = values[chosen[r][c]];
            }
        }

        if (!text)
        {
            return JgsMatrix.FromColumnMajor(flat, chosen.Count, k);
        }

        var words = new JgsValue[chosen.Count];
        for (int r = 0; r < chosen.Count; r++)
        {
            words[r] = JgsValue.Str(new string(chosen[r].Select(i => (char)values[i]).ToArray()));
        }

        return JgsValue.Cell(words);
    }

    // --- Shared resampling machinery ------------------------------------------------------------------

    /// <summary>Draws indices, turning the sampler's refusals into script-level errors.</summary>
    private static int[] Sampled(
        string name, Random random, int population, double[]? weights, int wanted, bool replacement,
        int line, int col)
    {
        if (wanted < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: cannot take a negative number of observations.");
        }

        try
        {
            return SamplePlans.WeightedSample(random, population, weights, wanted, replacement);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>How many observations every data argument has, refusing a set that disagrees.</summary>
    private static int SharedRowCount(string name, IReadOnlyList<JgsValue> data, int line, int col)
    {
        if (data.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: there is no data to resample.");
        }

        int n = -1;
        for (int i = 0; i < data.Count; i++)
        {
            (_, int rows, int columns) = DenseMatrix(name, data[i], line, col);

            // A row of numbers standing alone is a set of observations, not one observation of many
            // variables — nothing can be resampled from a single row.
            int observations = rows == 1 && columns > 1 ? columns : rows;

            if (n < 0)
            {
                n = observations;
            }
            else if (observations != n)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: argument {i + 1} has {observations} observations where the first has {n}.");
            }
        }

        if (n <= 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: there is no data to resample.");
        }

        return n;
    }

    /// <summary>The statistic on the chosen rows of every data argument, flattened to one row.</summary>
    private static double[] StatisticOf(
        string name, IJgsCallable statistic, IReadOnlyList<JgsValue> data, int[] picks, int line, int col)
    {
        var call = new JgsValue[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            call[i] = RowSubset(name, data[i], picks, line, col);
        }

        JgsValue result = statistic.Call(call, line, col);
        return result.Type == JgsType.Number
            ? [result.AsNumber]
            : ToDoubles(name, result, line, col);
    }

    /// <summary>The chosen observations of one data argument, keeping its orientation.</summary>
    private static JgsValue RowSubset(string name, JgsValue value, int[] picks, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);
        bool alongRow = rows == 1 && columns > 1;
        if (alongRow)
        {
            var taken = new double[picks.Length];
            for (int i = 0; i < picks.Length; i++)
            {
                taken[i] = flat[picks[i]];
            }

            return JgsMatrix.FromColumnMajor(taken, 1, picks.Length);
        }

        var block = new double[picks.Length * columns];
        for (int i = 0; i < picks.Length; i++)
        {
            for (int c = 0; c < columns; c++)
            {
                block[i + (c * picks.Length)] = flat[picks[i] + (c * rows)];
            }
        }

        return JgsMatrix.FromColumnMajor(block, picks.Length, columns);
    }

    /// <summary>Observation weights, checked against the data before any resampling happens.</summary>
    private static double[]? ObservationWeights(string name, double[]? weights, int n, int line, int col)
    {
        if (weights is null)
        {
            return null;
        }

        if (weights.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {weights.Length} weights for {n} observations.");
        }

        return weights;
    }

    private static void EnsureWidth(string name, int given, int expected, int line, int col)
    {
        if (given != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the statistic answered {given} values on one resample and {expected} on another.");
        }
    }

    /// <summary>A list of equal-length rows as a matrix, one replicate per row.</summary>
    private static JgsValue Stacked(List<double[]> rows)
    {
        if (rows.Count == 0)
        {
            return JgsValue.Array([]);
        }

        int width = rows[0].Length;
        var flat = new double[rows.Count * width];
        for (int r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length != width)
            {
                throw new InvalidOperationException("the statistic changed shape between resamples.");
            }

            for (int c = 0; c < width; c++)
            {
                flat[r + (c * rows.Count)] = rows[r][c];
            }
        }

        return width == 1
            ? JgsMatrix.FromColumnMajor(flat, rows.Count, 1)
            : JgsMatrix.FromColumnMajor(flat, rows.Count, width);
    }

    /// <summary>Whether the value was written as a row, which is what a result's orientation follows.</summary>
    private static bool IsRowShaped(JgsValue value)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        return dims.Length >= 2 && dims[0] == 1 && dims[1] != 1;
    }

    private static JgsValue Oriented(double[] values, bool row) =>
        row
            ? JgsMatrix.FromColumnMajor(values, 1, values.Length)
            : JgsMatrix.FromColumnMajor(values, values.Length, 1);

    /// <summary>Reads a replacement flag written as true, false, 1 or 0.</summary>
    private static bool Truthy(string name, JgsValue value, int line, int col) => value.Type switch
    {
        JgsType.Bool => value.AsBool,
        JgsType.Number => value.AsNumber != 0,
        _ => throw new JgsRuntimeException(line, col, $"{name}: replacement is true or false."),
    };
}
