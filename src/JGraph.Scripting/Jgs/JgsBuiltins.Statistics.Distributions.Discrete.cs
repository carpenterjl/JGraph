using JGraph.Statistics.Distributions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave D: the discrete probability distributions. Six families and the multinomial.
/// </summary>
/// <remarks>
/// <para>
/// The elementwise five — <c>binopdf</c>, <c>binocdf</c>, <c>binoinv</c>, <c>binornd</c>,
/// <c>binostat</c> and their thirty siblings — are not written here at all. A discrete family is the
/// same <see cref="DistributionFamily"/> record a continuous one is, so the wave C registrar builds
/// every one of those names from the record without knowing the difference, and the generic
/// <c>pdf('Poisson', …)</c> forms come along with them.
/// </para>
/// <para>
/// What is written here is what a discrete family genuinely does differently: the three fitters, whose
/// intervals are exact rather than asymptotic, and the multinomial, which is the one distribution in
/// the toolbox whose observation is a whole row rather than a number and so fits no part of the
/// elementwise machinery.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the discrete names that are not built from a family record.</summary>
    private static void RegisterDiscreteDistributionBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("binofit",
            (args, line, col) => BinomialFit(args, 1, line, col)[0],
            (args, wanted, line, col) => BinomialFit(args, wanted, line, col));

        Define("mnpdf", (args, line, col) => MultinomialDensity(args, line, col));
        Define("mnrnd", (args, line, col) => MultinomialDraw(random, args, line, col));
    }

    /// <summary>
    /// Maximum likelihood for a discrete family. Only three of the six have a documented fitter: a
    /// hypergeometric is described by population counts that are known rather than estimated, and a
    /// discrete uniform by the largest value it can take, which is not something a likelihood is
    /// informative about.
    /// </summary>
    private static DistributionFitting.FitOutcome FitDiscrete(
        DistributionFamily family, in DistributionFitting.Sample sample, double alpha)
    {
        if (sample.HasCensoring)
        {
            throw new ArgumentException(
                "a discrete fit does not take censored observations.", nameof(sample));
        }

        switch (family.Prefix)
        {
            case "poiss":
            {
                (double estimate, double lower, double upper) =
                    DiscreteFitting.PoissonRate(DiscreteFitting.WeightedTotal(sample), sample.Count, alpha);
                return new DistributionFitting.FitOutcome([estimate], [lower], [upper]);
            }

            case "nbin":
                return DiscreteFitting.NegativeBinomial(sample, alpha);

            case "geo":
                return DiscreteFitting.Geometric(sample, alpha);

            default:
                throw new ArgumentException(
                    $"there is no maximum likelihood fit for the {family.Name} distribution.", nameof(family));
        }
    }

    /// <summary>
    /// The <c>mle</c> route into a discrete fit. The binomial is the family MathWorks makes reachable
    /// here and nowhere else: it has no <c>*fit</c> of the usual shape because its trial count is not
    /// part of the data, so <c>mle</c> takes the count as <c>'ntrials'</c> instead.
    /// </summary>
    private static DistributionFitting.FitOutcome FitDiscreteForLikelihood(
        string name, DistributionFamily family, in DistributionFitting.Sample sample,
        double alpha, JgsValue? trials, int line, int col)
    {
        if (family.Prefix == "bino")
        {
            if (trials is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: fitting a Binomial needs 'ntrials' — the number of trials each observation counts successes out of.");
            }

            double[] given = ToDoubles(name, trials, line, col);
            if (given.Length != 1 || !(given[0] >= 1) || given[0] != Math.Floor(given[0]))
            {
                throw new JgsRuntimeException(line, col, $"{name}: 'ntrials' must be one whole number of trials.");
            }

            double count = given[0] * sample.Count;
            (double estimate, double lower, double upper) =
                DiscreteFitting.BinomialProportion(DiscreteFitting.WeightedTotal(sample), count, alpha);
            // Only the probability is estimated: the trial count came in as an option, so reporting it
            // back as a fitted parameter would claim the data said something about it.
            return new DistributionFitting.FitOutcome([estimate], [lower], [upper]);
        }

        if (trials is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'ntrials' belongs to the Binomial, not to the {family.Name} distribution.");
        }

        try
        {
            return FitDiscrete(family, sample, alpha);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>[phat, pci] = binofit(x, n, alpha)</c>. This one fitter takes its trial count as a second
    /// argument beside the data rather than as an option after it, and answers one interval per
    /// experiment as a row, so it is written out rather than driven from the shared table.
    /// </summary>
    private static JgsValue[] BinomialFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string Name = "binofit";
        if (args.Count is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                "binofit(x, n, alpha) estimates the probability of success from x successes in n trials.");
        }

        double alpha = OptionalAlpha(Name, args, 2, line, col);
        (double[][] columns, int[] dims) = AlignArguments(Name, [args[0], args[1]], line, col);

        int length = columns[0].Length;
        var estimates = new double[length];
        var limits = new double[2 * length];
        for (int i = 0; i < length; i++)
        {
            (double estimate, double lower, double upper) =
                DiscreteFitting.BinomialProportion(columns[0][i], columns[1][i], alpha);
            estimates[i] = estimate;

            // Column-major storage of a length-by-2 matrix: lower limits fill the first column and
            // upper the second, which is the one-row-per-experiment shape MathWorks documents.
            limits[i] = lower;
            limits[length + i] = upper;
        }

        JgsValue estimate0 = ShapedResult(estimates, dims);
        return wanted <= 1
            ? [estimate0]
            : [estimate0, JgsMatrix.FromColumnMajor(limits, length, 2)];
    }

    // --- The multinomial ------------------------------------------------------------------------------

    /// <summary><c>y = mnpdf(x, p)</c>: the probability of each row of counts.</summary>
    private static JgsValue MultinomialDensity(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string Name = "mnpdf";
        if (args.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "mnpdf(x, p) takes a row of counts, or one row per trial, and the category probabilities.");
        }

        (int countRows, int categories, double[] counts) = RowsAndColumns(Name, args[0], line, col);
        (int probabilityRows, int probabilityColumns, double[] probabilities) =
            RowsAndColumns(Name, args[1], line, col);

        if (probabilityColumns != categories)
        {
            throw new JgsRuntimeException(line, col,
                $"{Name}: the counts have {categories} categories and the probabilities have {probabilityColumns}.");
        }

        if (probabilityRows != 1 && probabilityRows != countRows)
        {
            throw new JgsRuntimeException(line, col,
                $"{Name}: there are {countRows} row(s) of counts and {probabilityRows} row(s) of probabilities.");
        }

        var answer = new double[countRows];
        var row = new double[categories];
        var mass = new double[categories];
        for (int r = 0; r < countRows; r++)
        {
            int which = probabilityRows == 1 ? 0 : r;
            for (int c = 0; c < categories; c++)
            {
                row[c] = counts[(c * countRows) + r];
                mass[c] = probabilities[(c * probabilityRows) + which];
            }

            answer[r] = DiscreteDistributions.MultinomialPdf(row, mass);
        }

        return countRows == 1 ? JgsValue.Number(answer[0]) : JgsMatrix.FromColumnMajor(answer, countRows, 1);
    }

    /// <summary><c>r = mnrnd(n, p, m)</c>: how many of n trials land in each category.</summary>
    private static JgsValue MultinomialDraw(Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string Name = "mnrnd";
        if (args.Count is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                "mnrnd(n, p) draws one set of category counts; mnrnd(n, p, m) draws m of them.");
        }

        double[] trials = ToDoubles(Name, args[0], line, col);
        (int probabilityRows, int categories, double[] probabilities) =
            RowsAndColumns(Name, args[1], line, col);

        int rows = Math.Max(trials.Length, probabilityRows);
        if (args.Count == 3)
        {
            double[] wanted = ToDoubles(Name, args[2], line, col);
            if (wanted.Length != 1 || wanted[0] < 0 || wanted[0] != Math.Floor(wanted[0]))
            {
                throw new JgsRuntimeException(line, col, $"{Name}: m must be one whole number of draws.");
            }

            if (rows != 1 && rows != (int)wanted[0])
            {
                throw new JgsRuntimeException(line, col,
                    $"{Name}: {rows} row(s) of parameters do not fill {(int)wanted[0]} draw(s).");
            }

            rows = (int)wanted[0];
        }

        if (trials.Length != 1 && trials.Length != rows)
        {
            throw new JgsRuntimeException(line, col,
                $"{Name}: there are {trials.Length} trial count(s) and {rows} row(s) to draw.");
        }

        var drawn = new double[rows * categories];
        var mass = new double[categories];
        for (int r = 0; r < rows; r++)
        {
            int which = probabilityRows == 1 ? 0 : Math.Min(r, probabilityRows - 1);
            for (int c = 0; c < categories; c++)
            {
                mass[c] = probabilities[(c * probabilityRows) + which];
            }

            double[] counts = DiscreteDistributions.MultinomialSample(
                random, trials[trials.Length == 1 ? 0 : r], mass);
            for (int c = 0; c < categories; c++)
            {
                drawn[(c * rows) + r] = counts[c];
            }
        }

        return JgsMatrix.FromColumnMajor(drawn, rows, categories);
    }

    /// <summary>
    /// Reads a value as a table of rows, which is what the multinomial's arguments are. A plain vector
    /// is one row of categories rather than a column of one-category rows, because that is the only
    /// reading under which <c>mnpdf([1 2 3], p)</c> means anything.
    /// </summary>
    private static (int Rows, int Columns, double[] Flat) RowsAndColumns(
        string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = JgsMatrix.DimsOf(value);

        long recorded = 1;
        foreach (int dim in dims)
        {
            recorded *= dim;
        }

        if (dims.Length != 2 || recorded != flat.Length)
        {
            return (1, flat.Length, flat);
        }

        if (dims[0] == 1 || dims[1] == 1)
        {
            return (1, flat.Length, flat);
        }

        return (dims[0], dims[1], flat);
    }
}
