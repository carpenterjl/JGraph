using JGraph.Statistics.Hypothesis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave F, part two: analysis of variance in its four shapes, its two rank-based counterparts, its
/// multivariate form, and the pairwise comparison that follows any of them.
/// </summary>
/// <remarks>
/// <para>
/// Each of these answers three things: the probability for each term, the table a report would print,
/// and a structure holding enough of the fit for <c>multcompare</c> to work from. The table is a cell
/// array of headings and numbers rather than anything drawn, because JGraph's console prints a cell
/// array perfectly well and the figure these names open in MATLAB is a display, not a result.
/// </para>
/// <para>
/// <c>multcompare</c> reads the <c>source</c> field to know what it was handed. All five sources reduce
/// to the same four numbers — a set of estimates, a weight for each, a common scale and its degrees of
/// freedom — which is why one comparison routine serves an analysis of variance and a rank test whose
/// scale was never estimated at all.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec ComparisonOptions = new(
        "multcompare", [], ["Alpha", "CType", "Display", "Dimension", "Estimate"]);

    private static readonly OptionSpec GeneralAnovaOptions = new(
        "anovan",
        [],
        ["alpha", "continuous", "display", "model", "nested", "random", "sstype", "varnames"]);

    /// <summary>Registers the analysis-of-variance builtins.</summary>
    private static void RegisterAnovaBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                { MultiOutput = both }));

        DefineBoth("anova1", OneWayAnalysis);
        DefineBoth("anova2", TwoWayAnalysis);
        DefineBoth("anovan", GeneralAnalysis);
        DefineBoth("manova1", MultivariateAnalysis);
        DefineBoth("kruskalwallis", RankAnalysis);
        DefineBoth("friedman", BlockedRankAnalysis);
        DefineBoth("multcompare", PairwiseComparison);
    }

    // --- One way -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[p, tbl, stats] = anova1(X)</c> or <c>anova1(x, group)</c>: whether several groups share a
    /// mean.
    /// </summary>
    private static JgsValue[] OneWayAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("anova1", args, 1, 3, line, col);
        DisplayWord("anova1", args, 2, line, col);

        (double[][] groups, string[] names) = Grouped("anova1", args, line, col);
        AnalysisOfVariance.OneWay outcome = Guarded(
            "anova1", () => AnalysisOfVariance.OneWayFrom(groups), line, col);

        JgsValue table = CellTable([
            [Word("Source"), Word("SS"), Word("df"), Word("MS"), Word("F"), Word("Prob>F")],
            [
                Word("Groups"), JgsValue.Number(outcome.BetweenSS), JgsValue.Number(outcome.BetweenDf),
                JgsValue.Number(outcome.BetweenMS), JgsValue.Number(outcome.F), JgsValue.Number(outcome.P)
            ],
            [
                Word("Error"), JgsValue.Number(outcome.WithinSS), JgsValue.Number(outcome.WithinDf),
                JgsValue.Number(outcome.WithinMS), Nothing(), Nothing()
            ],
            [
                Word("Total"), JgsValue.Number(outcome.TotalSS), JgsValue.Number(outcome.TotalDf),
                Nothing(), Nothing(), Nothing()
            ],
        ]);

        var counts = new double[outcome.Counts.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = outcome.Counts[i];
        }

        JgsValue stats = Structure(
            ("gnames", NameCell(names)),
            ("n", RowVector(counts)),
            ("source", Word("anova1")),
            ("means", RowVector(outcome.Means)),
            ("df", JgsValue.Number(outcome.WithinDf)),
            ("s", JgsValue.Number(Math.Sqrt(outcome.WithinMS))));

        return Outputs(wanted, JgsValue.Number(outcome.P), table, stats);
    }

    // --- Two way -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[p, tbl, stats] = anova2(X, reps)</c>: a balanced grid whose columns are the levels of one
    /// factor and whose rows are the levels of another. The probabilities come back in MathWorks'
    /// order — columns, rows, then the interaction.
    /// </summary>
    private static JgsValue[] TwoWayAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("anova2", args, 1, 3, line, col);
        DisplayWord("anova2", args, 2, line, col);

        (double[] flat, int rows, int columns) = DenseMatrix("anova2", args[0], line, col);
        int replicates = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Count("anova2", args, 1, line, col)
            : 1;

        // DenseMatrix reads column-major; the analysis wants the grid row by row, which is the order
        // the caller wrote it in.
        var byRow = new double[flat.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                byRow[(r * columns) + c] = flat[r + (c * rows)];
            }
        }

        AnalysisOfVariance.TwoWay outcome = Guarded(
            "anova2", () => AnalysisOfVariance.TwoWayFrom(byRow, rows, columns, replicates), line, col);

        double errorMS = outcome.ErrorSS / outcome.ErrorDf;
        (double columnF, double columnP) =
            AnalysisOfVariance.Ratio(outcome.ColumnSS, outcome.ColumnDf, errorMS, outcome.ErrorDf);
        (double rowF, double rowP) =
            AnalysisOfVariance.Ratio(outcome.RowSS, outcome.RowDf, errorMS, outcome.ErrorDf);
        (double interactionF, double interactionP) = outcome.HasInteraction
            ? AnalysisOfVariance.Ratio(outcome.InteractionSS, outcome.InteractionDf, errorMS, outcome.ErrorDf)
            : (double.NaN, double.NaN);

        double totalSS = outcome.ColumnSS + outcome.RowSS + outcome.InteractionSS + outcome.ErrorSS;
        double totalDf = outcome.ColumnDf + outcome.RowDf + outcome.InteractionDf + outcome.ErrorDf;

        List<JgsValue[]> lines =
        [
            [Word("Source"), Word("SS"), Word("df"), Word("MS"), Word("F"), Word("Prob>F")],
            Line("Columns", outcome.ColumnSS, outcome.ColumnDf, columnF, columnP),
            Line("Rows", outcome.RowSS, outcome.RowDf, rowF, rowP),
        ];

        if (outcome.HasInteraction)
        {
            lines.Add(Line("Interaction", outcome.InteractionSS, outcome.InteractionDf, interactionF, interactionP));
        }

        lines.Add([
            Word("Error"), JgsValue.Number(outcome.ErrorSS), JgsValue.Number(outcome.ErrorDf),
            JgsValue.Number(errorMS), Nothing(), Nothing()
        ]);
        lines.Add([
            Word("Total"), JgsValue.Number(totalSS), JgsValue.Number(totalDf), Nothing(), Nothing(), Nothing()
        ]);

        double[] probabilities = outcome.HasInteraction
            ? [columnP, rowP, interactionP]
            : [columnP, rowP];

        int levels = rows / replicates;
        JgsValue stats = Structure(
            ("source", Word("anova2")),
            ("sigmasq", JgsValue.Number(errorMS)),
            ("colmeans", RowVector(outcome.ColumnMeans)),
            ("coln", JgsValue.Number(replicates * levels)),
            ("rowmeans", RowVector(outcome.RowMeans)),
            ("rown", JgsValue.Number(replicates * columns)),
            ("inter", JgsValue.Number(outcome.HasInteraction ? 1 : 0)),
            ("pval", JgsValue.Number(interactionP)),
            ("df", JgsValue.Number(outcome.ErrorDf)));

        return Outputs(wanted, RowVector(probabilities), CellTable(lines), stats);

        static JgsValue[] Line(string name, double ss, double df, double f, double p) =>
        [
            Word(name), JgsValue.Number(ss), JgsValue.Number(df), JgsValue.Number(ss / df),
            JgsValue.Number(f), JgsValue.Number(p)
        ];
    }

    // --- Any number of factors -------------------------------------------------------------------------

    /// <summary>
    /// <c>[p, tbl, stats] = anovan(y, group)</c>: an analysis with any number of crossed factors, any
    /// group sizes, and whichever of the three sums of squares was asked for.
    /// </summary>
    private static JgsValue[] GeneralAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = GeneralAnovaOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "anovan(y, group) needs the response and a cell array holding one grouping variable per factor.");
        }

        foreach (string refused in new[] { "nested", "random", "continuous" })
        {
            if (parsed.Named(refused) is not null)
            {
                throw new JgsRuntimeException(line, col,
                    $"anovan: '{refused}' asks for a model this build does not fit — every factor here is "
                    + "crossed, fixed and categorical.");
            }
        }

        _ = parsed.Scalar("alpha", 0.05);
        _ = parsed.Word("display", "off", "on", "off");

        double[] y = ToDoubles("anovan", parsed.Positional[0], line, col);
        (List<int[]> factors, List<int> levels, List<string[]> levelNames) =
            Factors("anovan", parsed.Positional[1], y.Length, line, col);

        string[] variableNames = VariableNames(parsed.Named("varnames"), factors.Count, line, col);
        List<int[]> terms = Model(parsed.Named("model"), factors.Count, line, col);
        var termNames = new List<string>(terms.Count);
        foreach (int[] term in terms)
        {
            var parts = new List<string>(term.Length);
            foreach (int factor in term)
            {
                parts.Add(variableNames[factor]);
            }

            termNames.Add(string.Join("*", parts));
        }

        AnalysisOfVariance.SumOfSquares type = parsed.Whole("sstype", 3) switch
        {
            1 => AnalysisOfVariance.SumOfSquares.Sequential,
            2 => AnalysisOfVariance.SumOfSquares.Hierarchical,
            3 => AnalysisOfVariance.SumOfSquares.Marginal,
            _ => throw new JgsRuntimeException(line, col, "anovan: 'sstype' is 1, 2 or 3."),
        };

        AnalysisOfVariance.NWay outcome = Guarded(
            "anovan",
            () => AnalysisOfVariance.NWayFrom(y, factors, levels, terms, termNames, type),
            line,
            col);

        List<JgsValue[]> lines =
        [
            [Word("Source"), Word("Sum Sq."), Word("d.f."), Word("Mean Sq."), Word("F"), Word("Prob>F")],
        ];

        var probabilities = new double[outcome.Terms.Length];
        for (int t = 0; t < outcome.Terms.Length; t++)
        {
            AnalysisOfVariance.NWayTerm term = outcome.Terms[t];
            probabilities[t] = term.P;
            lines.Add([
                Word(term.Name), JgsValue.Number(term.SS), JgsValue.Number(term.Df),
                JgsValue.Number(term.MS), JgsValue.Number(term.F), JgsValue.Number(term.P)
            ]);
        }

        lines.Add([
            Word("Error"), JgsValue.Number(outcome.ErrorSS), JgsValue.Number(outcome.ErrorDf),
            JgsValue.Number(outcome.ErrorMS), Nothing(), Nothing()
        ]);
        lines.Add([
            Word("Total"), JgsValue.Number(outcome.TotalSS), JgsValue.Number(outcome.TotalDf),
            Nothing(), Nothing(), Nothing()
        ]);

        // The marginal means and counts per factor are what multcompare works from, so they travel in
        // the stats structure rather than being recomputed from data it would not have.
        var means = new JgsValue[factors.Count];
        var counts = new JgsValue[factors.Count];
        var namesPerFactor = new JgsValue[factors.Count];
        for (int f = 0; f < factors.Count; f++)
        {
            var sums = new double[levels[f]];
            var seen = new double[levels[f]];
            for (int i = 0; i < y.Length; i++)
            {
                sums[factors[f][i]] += y[i];
                seen[factors[f][i]]++;
            }

            for (int level = 0; level < levels[f]; level++)
            {
                sums[level] = seen[level] > 0 ? sums[level] / seen[level] : double.NaN;
            }

            means[f] = RowVector(sums);
            counts[f] = RowVector(seen);
            namesPerFactor[f] = NameCell(levelNames[f]);
        }

        var levelCounts = new double[factors.Count];
        for (int f = 0; f < factors.Count; f++)
        {
            levelCounts[f] = levels[f];
        }

        JgsValue stats = Structure(
            ("source", Word("anovan")),
            ("resid", RowVector(outcome.Residuals)),
            ("coeffs", Column(outcome.Coefficients)),
            ("dfe", JgsValue.Number(outcome.ErrorDf)),
            ("mse", JgsValue.Number(outcome.ErrorMS)),
            ("nlevels", RowVector(levelCounts)),
            ("varnames", NameCell(variableNames)),
            ("grpnames", CellOf(namesPerFactor)),
            ("means", CellOf(means)),
            ("n", CellOf(counts)));

        return Outputs(wanted, Column(probabilities), CellTable(lines), stats);
    }

    // --- Several responses at once ------------------------------------------------------------------------

    /// <summary>
    /// <c>[d, p, stats] = manova1(X, group)</c>: whether several groups share a mean vector, answered as
    /// the number of directions their means need.
    /// </summary>
    private static JgsValue[] MultivariateAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("manova1", args, 2, 3, line, col);
        double alpha = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? Num("manova1", args, 2, line, col)
            : 0.05;

        double[,] observations = AsRectangle("manova1", args[0], line, col);
        (int[] index, string[] names) = GroupIndex("manova1", args[1], line, col);
        if (index.Length != observations.GetLength(0))
        {
            throw new JgsRuntimeException(line, col,
                $"manova1: the grouping variable has {index.Length} entries but the data has "
                + $"{observations.GetLength(0)} rows.");
        }

        foreach (int group in index)
        {
            if (group < 0)
            {
                throw new JgsRuntimeException(line, col,
                    "manova1: an observation with no group cannot be left out of a multivariate analysis; "
                    + "remove it from the data as well.");
            }
        }

        AnalysisOfVariance.Manova outcome = Guarded(
            "manova1",
            () => AnalysisOfVariance.ManovaFrom(observations, index, names.Length, alpha),
            line,
            col);

        JgsValue stats = Structure(
            ("W", FromDense(outcome.Within)),
            ("B", FromDense(outcome.Between)),
            ("T", FromDense(Sum(outcome.Within, outcome.Between))),
            ("dfW", JgsValue.Number(outcome.WithinDf)),
            ("dfB", JgsValue.Number(outcome.BetweenDf)),
            ("dfT", JgsValue.Number(outcome.WithinDf + outcome.BetweenDf)),
            ("lambda", Column(outcome.Lambda)),
            ("chisq", Column(outcome.ChiSquare)),
            ("chisqdf", Column(outcome.ChiSquareDf)),
            ("eigenval", Column(outcome.EigenValues)),
            ("eigenvec", FromDense(outcome.EigenVectors)),
            ("canon", FromDense(outcome.Canonical)),
            ("mdist", RowVector(outcome.Distances)),
            ("gmdist", FromDense(outcome.GroupDistances)),
            ("gnames", NameCell(names)));

        return Outputs(
            wanted, JgsValue.Number(outcome.Dimension), Column(outcome.P), stats);
    }

    // --- Rank-based ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[p, tbl, stats] = kruskalwallis(x, group)</c>: the one-way question asked of the ranks.
    /// </summary>
    private static JgsValue[] RankAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("kruskalwallis", args, 1, 3, line, col);
        DisplayWord("kruskalwallis", args, 2, line, col);

        (double[][] groups, string[] names) = Grouped("kruskalwallis", args, line, col);
        AnalysisOfVariance.RankAnalysis outcome = Guarded(
            "kruskalwallis", () => AnalysisOfVariance.KruskalWallisFrom(groups), line, col);

        int total = 0;
        var counts = new double[outcome.Counts.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = outcome.Counts[i];
            total += outcome.Counts[i];
        }

        // The table reports the ranks' sums of squares, which is what the statistic is built from: the
        // between-groups piece divided by the total mean square is the chi-square itself.
        double totalSS = (double)total * (total + 1) * (total - 1) / 12;
        double betweenSS = outcome.Statistic * totalSS / total;
        double withinSS = totalSS - betweenSS;
        double betweenDf = outcome.Df;
        double withinDf = total - outcome.Counts.Length;

        JgsValue table = CellTable([
            [Word("Source"), Word("SS"), Word("df"), Word("MS"), Word("Chi-sq"), Word("Prob>Chi-sq")],
            [
                Word("Groups"), JgsValue.Number(betweenSS), JgsValue.Number(betweenDf),
                JgsValue.Number(betweenSS / betweenDf), JgsValue.Number(outcome.Statistic),
                JgsValue.Number(outcome.P)
            ],
            [
                Word("Error"), JgsValue.Number(withinSS), JgsValue.Number(withinDf),
                JgsValue.Number(withinDf > 0 ? withinSS / withinDf : double.NaN), Nothing(), Nothing()
            ],
            [
                Word("Total"), JgsValue.Number(totalSS), JgsValue.Number(total - 1.0),
                Nothing(), Nothing(), Nothing()
            ],
        ]);

        JgsValue stats = Structure(
            ("gnames", NameCell(names)),
            ("n", RowVector(counts)),
            ("source", Word("kruskalwallis")),
            ("meanranks", RowVector(outcome.MeanRanks)),
            ("sumt", JgsValue.Number(outcome.TieAdjustment)));

        return Outputs(wanted, JgsValue.Number(outcome.P), table, stats);
    }

    /// <summary>
    /// <c>[p, tbl, stats] = friedman(x, reps)</c>: the two-way question asked of ranks taken inside each
    /// block, so that a block's own level cannot influence the comparison between columns.
    /// </summary>
    private static JgsValue[] BlockedRankAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("friedman", args, 1, 3, line, col);
        DisplayWord("friedman", args, 2, line, col);

        (double[] flat, int rows, int columns) = DenseMatrix("friedman", args[0], line, col);
        int replicates = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Count("friedman", args, 1, line, col)
            : 1;

        var byRow = new double[flat.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                byRow[(r * columns) + c] = flat[r + (c * rows)];
            }
        }

        AnalysisOfVariance.RankAnalysis outcome = Guarded(
            "friedman", () => AnalysisOfVariance.FriedmanFrom(byRow, rows, columns, replicates), line, col);

        JgsValue table = CellTable([
            [Word("Source"), Word("SS"), Word("df"), Word("MS"), Word("Chi-sq"), Word("Prob>Chi-sq")],
            [
                Word("Columns"), JgsValue.Number(outcome.Statistic), JgsValue.Number(outcome.Df),
                JgsValue.Number(outcome.Statistic / outcome.Df), JgsValue.Number(outcome.Statistic),
                JgsValue.Number(outcome.P)
            ],
        ]);

        var counts = new double[outcome.Counts.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = outcome.Counts[i];
        }

        JgsValue stats = Structure(
            ("source", Word("friedman")),
            ("n", RowVector(counts)),
            ("meanranks", RowVector(outcome.MeanRanks)),
            ("sigma", JgsValue.Number(outcome.Sigma)));

        return Outputs(wanted, JgsValue.Number(outcome.P), table, stats);
    }

    // --- Pairwise comparison ------------------------------------------------------------------------------

    /// <summary>
    /// <c>[c, m, h, gnames] = multcompare(stats)</c>: every pair of estimates compared, with the interval
    /// widened by whichever rule keeps the whole family of comparisons at the level asked for.
    /// </summary>
    private static JgsValue[] PairwiseComparison(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ComparisonOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1 || parsed.Positional[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                "multcompare(stats) takes the structure an analysis of variance or a rank test returned.");
        }

        Dictionary<string, JgsValue> stats = parsed.Positional[0].AsStruct;
        double alpha = parsed.Scalar("Alpha", 0.05);
        _ = parsed.Word("Display", "off", "on", "off");

        MultipleComparison.Correction correction = parsed.Word(
            "CType", "tukey-kramer",
            "tukey-kramer", "hsd", "lsd", "bonferroni", "dunn-sidak", "scheffe") switch
        {
            "lsd" => MultipleComparison.Correction.LeastSignificant,
            "bonferroni" => MultipleComparison.Correction.Bonferroni,
            "dunn-sidak" => MultipleComparison.Correction.DunnSidak,
            "scheffe" => MultipleComparison.Correction.Scheffe,
            _ => MultipleComparison.Correction.TukeyKramer,
        };

        (double[] estimates, double[] weights, double scale, double df, string[] names) =
            ComparisonSubject(stats, parsed, line, col);

        MultipleComparison.Comparison[] compared = Guarded(
            "multcompare",
            () => MultipleComparison.Compare(estimates, weights, scale, df, alpha, correction),
            line,
            col);

        var table = new double[compared.Length * 6];
        for (int i = 0; i < compared.Length; i++)
        {
            table[i] = compared[i].First + 1;
            table[i + compared.Length] = compared[i].Second + 1;
            table[i + (2 * compared.Length)] = compared[i].Lower;
            table[i + (3 * compared.Length)] = compared[i].Estimate;
            table[i + (4 * compared.Length)] = compared[i].Upper;
            table[i + (5 * compared.Length)] = compared[i].P;
        }

        var summary = new double[estimates.Length * 2];
        for (int i = 0; i < estimates.Length; i++)
        {
            summary[i] = estimates[i];
            summary[i + estimates.Length] = scale * Math.Sqrt(weights[i]);
        }

        // The third output is the figure MATLAB draws the comparison in. Nothing is drawn here, so it
        // is an empty rather than a handle to a window that does not exist.
        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(table, compared.Length, 6),
            JgsMatrix.FromColumnMajor(summary, estimates.Length, 2),
            Nothing(),
            NameCell(names));
    }

    /// <summary>
    /// What a comparison is actually about, read out of whichever analysis produced the structure: the
    /// estimates, their variance weights, the scale they are measured in, and its degrees of freedom.
    /// </summary>
    private static (double[] Estimates, double[] Weights, double Scale, double Df, string[] Names) ComparisonSubject(
        Dictionary<string, JgsValue> stats, ParsedArgs parsed, int line, int col)
    {
        string source = stats.TryGetValue("source", out JgsValue? word) && word.Type == JgsType.String
            ? word.AsString
            : throw new JgsRuntimeException(line, col,
                "multcompare: the structure does not say which analysis produced it.");

        switch (source)
        {
            case "anova1":
            {
                double[] means = Field(stats, "means", line, col);
                double[] counts = Field(stats, "n", line, col);
                return (means, Reciprocals(counts), Scalar(stats, "s", line, col),
                    Scalar(stats, "df", line, col), Names(stats, "gnames", means.Length));
            }

            case "anova2":
            {
                string which = parsed.Word("Estimate", "column", "column", "row");
                double[] means = Field(stats, which == "row" ? "rowmeans" : "colmeans", line, col);
                double count = Scalar(stats, which == "row" ? "rown" : "coln", line, col);
                var weights = new double[means.Length];
                Array.Fill(weights, 1 / count);
                return (means, weights, Math.Sqrt(Scalar(stats, "sigmasq", line, col)),
                    Scalar(stats, "df", line, col), Labels(which == "row" ? "Row" : "Col", means.Length));
            }

            case "anovan":
            {
                int dimension = parsed.Whole("Dimension", 1);
                JgsValue[] means = CellField(stats, "means", line, col);
                JgsValue[] counts = CellField(stats, "n", line, col);
                if (dimension < 1 || dimension > means.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"multcompare: 'Dimension' is between 1 and {means.Length} for this analysis.");
                }

                double[] estimates = ToDoubles("multcompare", means[dimension - 1], line, col);
                double[] sizes = ToDoubles("multcompare", counts[dimension - 1], line, col);
                JgsValue[] groups = CellField(stats, "grpnames", line, col);
                return (estimates, Reciprocals(sizes),
                    Math.Sqrt(Scalar(stats, "mse", line, col)), Scalar(stats, "dfe", line, col),
                    TextElements("multcompare", groups[dimension - 1], line, col));
            }

            case "kruskalwallis":
            {
                double[] ranks = Field(stats, "meanranks", line, col);
                double[] counts = Field(stats, "n", line, col);
                double total = 0;
                foreach (double count in counts)
                {
                    total += count;
                }

                // Ranks have a known variance under the null, so the scale is not estimated and the
                // comparison has infinite degrees of freedom.
                return (ranks, Reciprocals(counts), Math.Sqrt(total * (total + 1) / 12),
                    double.PositiveInfinity, Names(stats, "gnames", ranks.Length));
            }

            case "friedman":
            {
                double[] ranks = Field(stats, "meanranks", line, col);
                double[] counts = Field(stats, "n", line, col);
                return (ranks, Reciprocals(counts), Scalar(stats, "sigma", line, col),
                    double.PositiveInfinity, Labels("Col", ranks.Length));
            }

            default:
                throw new JgsRuntimeException(line, col,
                    $"multcompare does not compare the output of '{source}'; it takes the structure from "
                    + "anova1, anova2, anovan, kruskalwallis or friedman.");
        }
    }

    // --- Shared reading and shaping --------------------------------------------------------------------------

    /// <summary>
    /// The grouping variables of a general analysis: a cell array holding one per factor, or a matrix
    /// whose columns are them.
    /// </summary>
    private static (List<int[]> Factors, List<int> Levels, List<string[]> Names) Factors(
        string name, JgsValue given, int observations, int line, int col)
    {
        var columns = new List<JgsValue>();
        if (given.Type == JgsType.Cell)
        {
            columns.AddRange(given.AsCell);
        }
        else
        {
            (double[] flat, int rows, int width) = DenseMatrix(name, given, line, col);
            for (int c = 0; c < width; c++)
            {
                var column = new double[rows];
                Array.Copy(flat, c * rows, column, 0, rows);
                columns.Add(JgsMatrix.FromColumnMajor(column, rows, 1));
            }
        }

        if (columns.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: there must be at least one grouping variable.");
        }

        var factors = new List<int[]>(columns.Count);
        var levels = new List<int>(columns.Count);
        var names = new List<string[]>(columns.Count);
        for (int f = 0; f < columns.Count; f++)
        {
            (int[] index, string[] labels) = GroupIndex(name, columns[f], line, col);
            if (index.Length != observations)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: grouping variable {f + 1} has {index.Length} entries but the response has "
                    + $"{observations}.");
            }

            foreach (int level in index)
            {
                if (level < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: grouping variable {f + 1} leaves an observation ungrouped; remove it from "
                        + "the response as well.");
                }
            }

            factors.Add(index);
            levels.Add(labels.Length);
            names.Add(labels);
        }

        return (factors, levels, names);
    }

    /// <summary>The terms of the model, from the word or the order or the term matrix that named them.</summary>
    private static List<int[]> Model(JgsValue? given, int factors, int line, int col)
    {
        if (given is null)
        {
            return AnalysisOfVariance.FactorialTerms(factors, 1);
        }

        if (given.Type == JgsType.String)
        {
            return given.AsString.ToLowerInvariant() switch
            {
                "linear" => AnalysisOfVariance.FactorialTerms(factors, 1),
                "interaction" => AnalysisOfVariance.FactorialTerms(factors, 2),
                "full" => AnalysisOfVariance.FactorialTerms(factors, factors),
                _ => throw new JgsRuntimeException(line, col,
                    $"anovan: '{given.AsString}' is not a model "
                    + "(expected 'linear', 'interaction', 'full', an interaction order, or a term matrix)."),
            };
        }

        if (given.Type == JgsType.Number)
        {
            int order = (int)given.AsNumber;
            if (order < 1 || order > factors)
            {
                throw new JgsRuntimeException(line, col,
                    $"anovan: an interaction order is between 1 and {factors}.");
            }

            return AnalysisOfVariance.FactorialTerms(factors, order);
        }

        // A term matrix: one row per term, a one in every column the term crosses.
        (double[] flat, int rows, int columns) = DenseMatrix("anovan", given, line, col);
        if (columns != factors)
        {
            throw new JgsRuntimeException(line, col,
                $"anovan: a term matrix has one column per factor, so {factors} of them, not {columns}.");
        }

        var terms = new List<int[]>(rows);
        for (int r = 0; r < rows; r++)
        {
            var crossed = new List<int>();
            for (int c = 0; c < columns; c++)
            {
                if (flat[r + (c * rows)] != 0)
                {
                    crossed.Add(c);
                }
            }

            if (crossed.Count > 0)
            {
                terms.Add([.. crossed]);
            }
        }

        if (terms.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "anovan: the term matrix names no terms.");
        }

        return terms;
    }

    private static string[] VariableNames(JgsValue? given, int factors, int line, int col)
    {
        if (given is null)
        {
            var made = new string[factors];
            for (int i = 0; i < factors; i++)
            {
                made[i] = "X" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return made;
        }

        string[] names = TextElements("anovan", given, line, col);
        if (names.Length != factors)
        {
            throw new JgsRuntimeException(line, col,
                $"anovan: {names.Length} variable names for {factors} factors.");
        }

        return names;
    }

    /// <summary>
    /// The display argument these names take in their last positional slot. Nothing is drawn either
    /// way; the word is read so that a script that passes <c>'off'</c> is not told it is unexpected.
    /// </summary>
    private static void DisplayWord(string name, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        if (args.Count <= slot || IsPlaceholderValue(args[slot]))
        {
            return;
        }

        if (args[slot].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the display argument is 'on' or 'off'.");
        }

        string word = args[slot].AsString;
        if (!string.Equals(word, "on", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(word, "off", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the display argument is 'on' or 'off', not '{word}'.");
        }
    }

    private static double[] Reciprocals(double[] counts)
    {
        var weights = new double[counts.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            weights[i] = counts[i] > 0 ? 1 / counts[i] : double.PositiveInfinity;
        }

        return weights;
    }

    private static double[] Field(Dictionary<string, JgsValue> stats, string name, int line, int col) =>
        stats.TryGetValue(name, out JgsValue? value)
            ? ToDoubles("multcompare", value, line, col)
            : throw new JgsRuntimeException(line, col, $"multcompare: the structure has no '{name}'.");

    private static double Scalar(Dictionary<string, JgsValue> stats, string name, int line, int col)
    {
        double[] values = Field(stats, name, line, col);
        return values.Length == 1
            ? values[0]
            : throw new JgsRuntimeException(line, col, $"multcompare: '{name}' should be a single number.");
    }

    private static JgsValue[] CellField(Dictionary<string, JgsValue> stats, string name, int line, int col) =>
        stats.TryGetValue(name, out JgsValue? value) && value.Type == JgsType.Cell
            ? value.AsCell
            : throw new JgsRuntimeException(line, col, $"multcompare: the structure has no cell '{name}'.");

    private static string[] Names(Dictionary<string, JgsValue> stats, string field, int count)
    {
        if (stats.TryGetValue(field, out JgsValue? value) && value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell;
            var names = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                names[i] = cells[i].Type == JgsType.String
                    ? cells[i].AsString
                    : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        return Labels("Group", count);
    }

    private static string[] Labels(string prefix, int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = prefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return names;
    }

    private static double[,] Sum(double[,] left, double[,] right)
    {
        int rows = left.GetLength(0);
        int columns = left.GetLength(1);
        var total = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                total[r, c] = left[r, c] + right[r, c];
            }
        }

        return total;
    }

    private static JgsValue Word(string text) => JgsValue.Str(text);

    private static JgsValue Nothing() => JgsValue.Array([]);

    private static JgsValue NameCell(string[] names)
    {
        var cells = new JgsValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            cells[i] = JgsValue.Str(names[i]);
        }

        JgsValue cell = JgsValue.Cell(cells);
        cell.Reshape(names.Length, 1);
        return cell;
    }

    private static JgsValue CellOf(JgsValue[] values)
    {
        JgsValue cell = JgsValue.Cell(values);
        cell.Reshape(1, values.Length);
        return cell;
    }

    /// <summary>The cell array an analysis-of-variance table is: headings on the first row, one line per source.</summary>
    private static JgsValue CellTable(IReadOnlyList<JgsValue[]> rows)
    {
        int height = rows.Count;
        int width = rows[0].Length;
        var cells = new JgsValue[height * width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                cells[r + (c * height)] = rows[r][c];
            }
        }

        JgsValue table = JgsValue.Cell(cells);
        table.Reshape(height, width);
        return table;
    }
}
