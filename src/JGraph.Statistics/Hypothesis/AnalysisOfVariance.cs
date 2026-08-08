using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// Analysis of variance in its four shapes: one grouping variable, two crossed ones, any number of
/// them, and the rank-based tests that ask the same questions without assuming normality.
/// </summary>
/// <remarks>
/// <para>
/// One-way and two-way analysis have closed forms and use them. The general case does not: with
/// several factors, unequal group sizes and interactions, "the sum of squares for a term" only means
/// something once it is said which other terms are already in the model, which is what the three
/// sum-of-squares types are. So <see cref="NWayFrom"/> builds a design matrix and answers every term's
/// sum of squares as the difference between two model fits, which is one description that covers all
/// three types instead of three sets of algebra.
/// </para>
/// <para>
/// The coding is the sum-to-zero one: a factor with L levels contributes L − 1 columns, each marking
/// one level with a 1 and the last level with a −1. That is what makes a main effect mean the same
/// thing whether or not its interactions are in the model, and so what makes type III sums of squares
/// the ones MATLAB reports by default.
/// </para>
/// </remarks>
public static class AnalysisOfVariance
{
    private const double RankTolerance = 1e-10;

    /// <summary>A one-way analysis: one grouping variable, any number of levels, any group sizes.</summary>
    public readonly record struct OneWay(
        int[] Counts,
        double[] Means,
        double BetweenSS,
        double WithinSS,
        double BetweenDf,
        double WithinDf,
        double F,
        double P)
    {
        /// <summary>The total sum of squares — every observation's departure from the overall mean.</summary>
        public double TotalSS => BetweenSS + WithinSS;

        /// <summary>The total degrees of freedom.</summary>
        public double TotalDf => BetweenDf + WithinDf;

        /// <summary>The between-groups mean square.</summary>
        public double BetweenMS => BetweenSS / BetweenDf;

        /// <summary>The within-groups mean square, which estimates the common variance.</summary>
        public double WithinMS => WithinSS / WithinDf;
    }

    /// <summary>A two-way analysis of a balanced grid, with or without replication.</summary>
    public readonly record struct TwoWay(
        double ColumnSS,
        double RowSS,
        double InteractionSS,
        double ErrorSS,
        double ColumnDf,
        double RowDf,
        double InteractionDf,
        double ErrorDf,
        double[] ColumnMeans,
        double[] RowMeans,
        int Replicates)
    {
        /// <summary>Whether the grid was replicated, and so whether an interaction term exists.</summary>
        public bool HasInteraction => Replicates > 1;
    }

    /// <summary>One term of a general analysis: which factors it crosses and what it explains.</summary>
    public readonly record struct NWayTerm(int[] Factors, string Name, double SS, double Df, double F, double P)
    {
        /// <summary>The term's mean square.</summary>
        public double MS => Df > 0 ? SS / Df : double.NaN;
    }

    /// <summary>A general analysis: the terms, what is left over, and the fitted model behind them.</summary>
    public readonly record struct NWay(
        NWayTerm[] Terms,
        double ErrorSS,
        double ErrorDf,
        double TotalSS,
        double TotalDf,
        double[] Coefficients,
        double[] Residuals,
        double[] Fitted)
    {
        /// <summary>The residual mean square, which every F in the table is measured against.</summary>
        public double ErrorMS => ErrorDf > 0 ? ErrorSS / ErrorDf : double.NaN;
    }

    /// <summary>Which sums of squares a general analysis reports.</summary>
    public enum SumOfSquares
    {
        /// <summary>Sequential: each term is credited with what it adds to the terms before it.</summary>
        Sequential = 1,

        /// <summary>Hierarchical: each term is measured against every term that does not contain it.</summary>
        Hierarchical = 2,

        /// <summary>Marginal: each term is measured against the whole model without it. The default.</summary>
        Marginal = 3,
    }

    /// <summary>A rank-based analysis: the Kruskal–Wallis and Friedman statistics share this shape.</summary>
    public readonly record struct RankAnalysis(
        int[] Counts, double[] MeanRanks, double Statistic, double Df, double P, double TieAdjustment, double Sigma);

    /// <summary>A multivariate analysis: the two scatter matrices and the dimension the group means span.</summary>
    public readonly record struct Manova(
        double[,] Within,
        double[,] Between,
        double WithinDf,
        double BetweenDf,
        double[] Lambda,
        double[] ChiSquare,
        double[] ChiSquareDf,
        double[] P,
        int Dimension,
        double[] EigenValues,
        double[,] EigenVectors,
        double[,] Canonical,
        double[] Distances,
        double[,] GroupDistances);

    // --- One way ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>anova1</c>: whether several groups share a mean. The groups need not be the same size and a
    /// group of one contributes to the between-groups sum of squares but nothing to the within.
    /// </summary>
    public static OneWay OneWayFrom(IReadOnlyList<double[]> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var counts = new int[groups.Count];
        var means = new double[groups.Count];
        int total = 0;
        double sum = 0;

        for (int g = 0; g < groups.Count; g++)
        {
            double[] group = groups[g];
            counts[g] = group.Length;
            means[g] = group.Length == 0 ? double.NaN : DescriptiveStatistics.Mean(group);
            total += group.Length;
            foreach (double value in group)
            {
                sum += value;
            }
        }

        int present = 0;
        foreach (int count in counts)
        {
            if (count > 0)
            {
                present++;
            }
        }

        if (present < 2 || total <= present)
        {
            throw new ArgumentException(
                "an analysis of variance needs at least two non-empty groups and more observations than groups.");
        }

        double grand = sum / total;
        double between = 0;
        double within = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            if (counts[g] == 0)
            {
                continue;
            }

            double gap = means[g] - grand;
            between += counts[g] * gap * gap;
            foreach (double value in groups[g])
            {
                double residual = value - means[g];
                within += residual * residual;
            }
        }

        double betweenDf = present - 1;
        double withinDf = total - present;
        double f = (between / betweenDf) / (within / withinDf);
        double p = double.IsNaN(f) ? double.NaN : 1 - ContinuousDistributions.FCdf(f, betweenDf, withinDf);
        return new OneWay(counts, means, between, within, betweenDf, withinDf, f, p);
    }

    // --- Two way ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>anova2</c>: a balanced grid whose columns are the levels of one factor and whose rows are the
    /// levels of another, each cell holding <paramref name="replicates"/> observations.
    /// </summary>
    /// <param name="data">The grid, row-major, with the replicates of a row level stacked together.</param>
    /// <param name="rows">How many rows the grid has, replicates included.</param>
    /// <param name="columns">How many columns.</param>
    /// <param name="replicates">How many observations sit in each cell.</param>
    public static TwoWay TwoWayFrom(double[] data, int rows, int columns, int replicates)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (replicates < 1 || rows % replicates != 0)
        {
            throw new ArgumentException(
                "a two-way analysis needs the same number of replicates in every cell, so the row count must divide by it.");
        }

        int levels = rows / replicates;
        if (levels < 2 || columns < 2)
        {
            throw new ArgumentException("a two-way analysis needs at least two levels of each factor.");
        }

        foreach (double value in data)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException(
                    "a two-way analysis needs a complete grid; a missing value would unbalance it.");
            }
        }

        double Cell(int level, int replicate, int column) =>
            data[(((level * replicates) + replicate) * columns) + column];

        double grand = 0;
        foreach (double value in data)
        {
            grand += value;
        }

        int n = rows * columns;
        grand /= n;

        var rowMeans = new double[levels];
        var columnMeans = new double[columns];
        var cellMeans = new double[levels, columns];

        for (int i = 0; i < levels; i++)
        {
            for (int c = 0; c < columns; c++)
            {
                double cell = 0;
                for (int r = 0; r < replicates; r++)
                {
                    cell += Cell(i, r, c);
                }

                cellMeans[i, c] = cell / replicates;
                rowMeans[i] += cell;
                columnMeans[c] += cell;
            }

            rowMeans[i] /= replicates * columns;
        }

        for (int c = 0; c < columns; c++)
        {
            columnMeans[c] /= replicates * levels;
        }

        double rowSS = 0;
        for (int i = 0; i < levels; i++)
        {
            double gap = rowMeans[i] - grand;
            rowSS += replicates * columns * gap * gap;
        }

        double columnSS = 0;
        for (int c = 0; c < columns; c++)
        {
            double gap = columnMeans[c] - grand;
            columnSS += replicates * levels * gap * gap;
        }

        double interactionSS = 0;
        double errorSS = 0;
        for (int i = 0; i < levels; i++)
        {
            for (int c = 0; c < columns; c++)
            {
                double gap = cellMeans[i, c] - rowMeans[i] - columnMeans[c] + grand;
                interactionSS += replicates * gap * gap;
                for (int r = 0; r < replicates; r++)
                {
                    double residual = Cell(i, r, c) - cellMeans[i, c];
                    errorSS += residual * residual;
                }
            }
        }

        double rowDf = levels - 1;
        double columnDf = columns - 1;
        double interactionDf = rowDf * columnDf;
        double errorDf = levels * columns * (replicates - 1);

        // Without replication there is nothing left over once the interaction is taken out, so the
        // interaction is what the two main effects are tested against — the additive model.
        if (replicates == 1)
        {
            errorSS = interactionSS;
            errorDf = interactionDf;
            interactionSS = 0;
            interactionDf = 0;
        }

        return new TwoWay(
            columnSS, rowSS, interactionSS, errorSS,
            columnDf, rowDf, interactionDf, errorDf,
            columnMeans, rowMeans, replicates);
    }

    /// <summary>The F statistic and its probability for one line of a two-way table.</summary>
    public static (double F, double P) Ratio(double ss, double df, double errorMS, double errorDf)
    {
        if (!(df > 0) || !(errorDf > 0) || !(errorMS > 0))
        {
            return (double.NaN, double.NaN);
        }

        double f = ss / df / errorMS;
        return (f, 1 - ContinuousDistributions.FCdf(f, df, errorDf));
    }

    // --- Any number of factors ----------------------------------------------------------------------

    /// <summary>
    /// <c>anovan</c>: how much of the response each term of a factorial model explains, once the other
    /// terms named by <paramref name="type"/> are already in it.
    /// </summary>
    /// <param name="y">The response, one value per observation.</param>
    /// <param name="factors">One grouping index per factor, each holding a level number per observation.</param>
    /// <param name="levels">How many levels each factor has.</param>
    /// <param name="terms">The model: each term names the factors it crosses.</param>
    /// <param name="names">What to call each term in the table.</param>
    /// <param name="type">Which sums of squares to report.</param>
    public static NWay NWayFrom(
        double[] y,
        IReadOnlyList<int[]> factors,
        IReadOnlyList<int> levels,
        IReadOnlyList<int[]> terms,
        IReadOnlyList<string> names,
        SumOfSquares type)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(factors);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(names);

        int n = y.Length;
        if (terms.Count == 0)
        {
            throw new ArgumentException("a model needs at least one term.");
        }

        // Each term owns a block of design columns; the blocks are laid side by side after the
        // intercept, so a subset of terms is a subset of columns and nothing has to be rebuilt.
        var blocks = new List<double[][]>(terms.Count);
        foreach (int[] term in terms)
        {
            blocks.Add(TermColumns(term, factors, levels, n));
        }

        double[] full = FitColumns(y, blocks, AllOf(terms.Count), out int fullRank);
        double totalSS = CentredSumOfSquares(y);
        double errorSS = full[0];
        double errorDf = n - fullRank;
        double errorMS = errorDf > 0 ? errorSS / errorDf : double.NaN;

        var reported = new NWayTerm[terms.Count];
        for (int t = 0; t < terms.Count; t++)
        {
            (double ss, double df) = TermSumOfSquares(y, blocks, terms, t, type, errorSS, fullRank);
            double f = double.NaN;
            double p = double.NaN;
            if (df > 0 && errorDf > 0 && errorMS > 0)
            {
                f = ss / df / errorMS;
                p = 1 - ContinuousDistributions.FCdf(f, df, errorDf);
            }

            reported[t] = new NWayTerm(terms[t], names.Count > t ? names[t] : $"X{t + 1}", ss, df, f, p);
        }

        (double[] coefficients, double[] fitted) = Coefficients(y, blocks, AllOf(terms.Count));
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            residuals[i] = y[i] - fitted[i];
        }

        return new NWay(reported, errorSS, errorDf, totalSS, n - 1, coefficients, residuals, fitted);
    }

    /// <summary>
    /// Every term of a factorial model up to <paramref name="order"/> interactions, in the order MATLAB
    /// lists them: all main effects, then all pairs, and so on.
    /// </summary>
    public static List<int[]> FactorialTerms(int factors, int order)
    {
        var terms = new List<int[]>();
        for (int size = 1; size <= Math.Min(order, factors); size++)
        {
            Walk([], 0, size);
        }

        return terms;

        void Walk(List<int> current, int start, int size)
        {
            if (current.Count == size)
            {
                terms.Add([.. current]);
                return;
            }

            for (int f = start; f < factors; f++)
            {
                current.Add(f);
                Walk(current, f + 1, size);
                current.RemoveAt(current.Count - 1);
            }
        }
    }

    private static (double SS, double Df) TermSumOfSquares(
        double[] y,
        List<double[][]> blocks,
        IReadOnlyList<int[]> terms,
        int subject,
        SumOfSquares type,
        double fullErrorSS,
        int fullRank)
    {
        List<int> with;
        List<int> without;

        switch (type)
        {
            case SumOfSquares.Sequential:
                with = [];
                for (int t = 0; t <= subject; t++)
                {
                    with.Add(t);
                }

                without = with.GetRange(0, subject);
                break;

            case SumOfSquares.Hierarchical:
                // Every term that does not contain this one is already in the model; this term's
                // credit is what it adds on top of them.
                without = [];
                for (int t = 0; t < terms.Count; t++)
                {
                    if (t != subject && !Contains(terms[t], terms[subject]))
                    {
                        without.Add(t);
                    }
                }

                with = [.. without, subject];
                break;

            default:
                with = AllOf(terms.Count);
                without = [];
                foreach (int t in with)
                {
                    if (t != subject)
                    {
                        without.Add(t);
                    }
                }

                break;
        }

        double[] reduced = FitColumns(y, blocks, without, out int reducedRank);

        double enlargedSS;
        int enlargedRank;
        if (type == SumOfSquares.Marginal)
        {
            // Marginal sums of squares measure every term against the whole model, which has already
            // been fitted once — refitting it per term would give the same two numbers back.
            enlargedSS = fullErrorSS;
            enlargedRank = fullRank;
        }
        else
        {
            enlargedSS = FitColumns(y, blocks, with, out enlargedRank)[0];
        }

        return (Math.Max(0, reduced[0] - enlargedSS), enlargedRank - reducedRank);
    }

    /// <summary>Whether <paramref name="outer"/> crosses every factor <paramref name="inner"/> does.</summary>
    private static bool Contains(int[] outer, int[] inner)
    {
        foreach (int factor in inner)
        {
            if (Array.IndexOf(outer, factor) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static List<int> AllOf(int count)
    {
        var all = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            all.Add(i);
        }

        return all;
    }

    /// <summary>
    /// The sum-to-zero columns of one term: the elementwise product of its factors' main-effect
    /// columns, which for a single factor is just those columns.
    /// </summary>
    private static double[][] TermColumns(
        int[] term, IReadOnlyList<int[]> factors, IReadOnlyList<int> levels, int n)
    {
        double[][] columns = [];
        foreach (int factor in term)
        {
            double[][] main = MainEffect(factors[factor], levels[factor], n);
            if (columns.Length == 0)
            {
                columns = main;
                continue;
            }

            var crossed = new List<double[]>(columns.Length * main.Length);
            foreach (double[] left in columns)
            {
                foreach (double[] right in main)
                {
                    var product = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        product[i] = left[i] * right[i];
                    }

                    crossed.Add(product);
                }
            }

            columns = [.. crossed];
        }

        return columns;
    }

    private static double[][] MainEffect(int[] index, int levels, int n)
    {
        var columns = new double[Math.Max(0, levels - 1)][];
        for (int c = 0; c < columns.Length; c++)
        {
            columns[c] = new double[n];
            for (int i = 0; i < n; i++)
            {
                columns[c][i] = index[i] == c ? 1 : index[i] == levels - 1 ? -1 : 0;
            }
        }

        return columns;
    }

    /// <summary>
    /// The residual sum of squares and the rank of the design formed from the intercept plus the named
    /// blocks. Rank rather than column count, because an empty cell makes a column redundant and
    /// crediting a term with a degree of freedom it has no data for would inflate every F below it.
    /// </summary>
    private static double[] FitColumns(double[] y, List<double[][]> blocks, List<int> chosen, out int rank)
    {
        double[,] design = Design(blocks, chosen, y.Length);
        Svd svd = Svd.Factor(design);
        rank = 0;
        double largest = svd.Values.Length > 0 ? svd.Values[0] : 0;
        foreach (double value in svd.Values)
        {
            if (value > RankTolerance * Math.Max(1, largest))
            {
                rank++;
            }
        }

        double total = 0;
        foreach (double value in y)
        {
            total += value * value;
        }

        double explained = 0;
        for (int c = 0; c < rank; c++)
        {
            double projection = 0;
            for (int i = 0; i < y.Length; i++)
            {
                projection += svd.U[i, c] * y[i];
            }

            explained += projection * projection;
        }

        return [Math.Max(0, total - explained), rank];
    }

    private static (double[] Coefficients, double[] Fitted) Coefficients(
        double[] y, List<double[][]> blocks, List<int> chosen)
    {
        double[,] design = Design(blocks, chosen, y.Length);
        int columns = design.GetLength(1);
        Svd svd = Svd.Factor(design);
        double largest = svd.Values.Length > 0 ? svd.Values[0] : 0;

        var coefficients = new double[columns];
        for (int c = 0; c < svd.Values.Length; c++)
        {
            if (svd.Values[c] <= RankTolerance * Math.Max(1, largest))
            {
                continue;
            }

            double projection = 0;
            for (int i = 0; i < y.Length; i++)
            {
                projection += svd.U[i, c] * y[i];
            }

            double scaled = projection / svd.Values[c];
            for (int j = 0; j < columns; j++)
            {
                coefficients[j] += svd.V[j, c] * scaled;
            }
        }

        var fitted = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            double value = 0;
            for (int j = 0; j < columns; j++)
            {
                value += design[i, j] * coefficients[j];
            }

            fitted[i] = value;
        }

        return (coefficients, fitted);
    }

    private static double[,] Design(List<double[][]> blocks, List<int> chosen, int n)
    {
        int width = 1;
        foreach (int block in chosen)
        {
            width += blocks[block].Length;
        }

        var design = new double[n, width];
        for (int i = 0; i < n; i++)
        {
            design[i, 0] = 1;
        }

        int column = 1;
        foreach (int block in chosen)
        {
            foreach (double[] values in blocks[block])
            {
                for (int i = 0; i < n; i++)
                {
                    design[i, column] = values[i];
                }

                column++;
            }
        }

        return design;
    }

    private static double CentredSumOfSquares(double[] y)
    {
        double mean = DescriptiveStatistics.Mean(y);
        double total = 0;
        foreach (double value in y)
        {
            double gap = value - mean;
            total += gap * gap;
        }

        return total;
    }

    // --- Rank-based analyses -------------------------------------------------------------------------

    /// <summary>
    /// <c>kruskalwallis</c>: the one-way question asked of the ranks. The statistic is the between-groups
    /// sum of squares of the ranks on the scale that makes it a chi-square, divided by the correction
    /// that ties call for.
    /// </summary>
    public static RankAnalysis KruskalWallisFrom(IReadOnlyList<double[]> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var pooled = new List<double>();
        var counts = new int[groups.Count];
        for (int g = 0; g < groups.Count; g++)
        {
            counts[g] = groups[g].Length;
            pooled.AddRange(groups[g]);
        }

        int n = pooled.Count;
        int present = 0;
        foreach (int count in counts)
        {
            if (count > 0)
            {
                present++;
            }
        }

        if (present < 2 || n < 3)
        {
            throw new ArgumentException("a Kruskal–Wallis test needs at least two non-empty groups.");
        }

        double[] ranks = TestSupport.Ranks(pooled);
        var meanRanks = new double[groups.Count];
        double statistic = 0;
        int position = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            double sum = 0;
            for (int i = 0; i < counts[g]; i++)
            {
                sum += ranks[position + i];
            }

            position += counts[g];
            meanRanks[g] = counts[g] == 0 ? double.NaN : sum / counts[g];
            if (counts[g] > 0)
            {
                statistic += sum * sum / counts[g];
            }
        }

        statistic = (12.0 / (n * (n + 1.0)) * statistic) - (3 * (n + 1.0));

        var sorted = new List<double>(pooled);
        sorted.Sort();
        double ties = TestSupport.TieAdjustment(sorted);
        double correction = 1 - (ties / (((double)n * n * n) - n));
        if (correction > 0)
        {
            statistic /= correction;
        }

        double df = present - 1;
        return new RankAnalysis(
            counts, meanRanks, statistic, df, 1 - ContinuousDistributions.Chi2Cdf(statistic, df), ties,
            Math.Sqrt(n * (n + 1.0) / 12));
    }

    /// <summary>
    /// <c>friedman</c>: the two-way question asked of ranks taken within each block, so that a block's
    /// own level has no effect on the comparison between columns.
    /// </summary>
    /// <param name="data">The grid, row-major, with each block's replicates stacked together.</param>
    /// <param name="rows">How many rows, replicates included.</param>
    /// <param name="columns">How many treatments.</param>
    /// <param name="replicates">How many rows make up one block.</param>
    public static RankAnalysis FriedmanFrom(double[] data, int rows, int columns, int replicates)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (replicates < 1 || rows % replicates != 0)
        {
            throw new ArgumentException(
                "Friedman's test needs the same number of replicates in every block, so the row count must divide by it.");
        }

        int blocks = rows / replicates;
        if (blocks < 2 || columns < 2)
        {
            throw new ArgumentException("Friedman's test needs at least two blocks and two treatments.");
        }

        int perBlock = replicates * columns;
        var columnSums = new double[columns];
        double ties = 0;

        for (int b = 0; b < blocks; b++)
        {
            var block = new double[perBlock];
            for (int r = 0; r < replicates; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    block[(r * columns) + c] = data[(((b * replicates) + r) * columns) + c];
                }
            }

            double[] ranks = TestSupport.Ranks(block);
            var sorted = new double[perBlock];
            Array.Copy(block, sorted, perBlock);
            Array.Sort(sorted);
            ties += TestSupport.TieAdjustment(sorted);

            for (int r = 0; r < replicates; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    columnSums[c] += ranks[(r * columns) + c];
                }
            }
        }

        double expected = blocks * replicates * (perBlock + 1) / 2.0;
        double scale = blocks * replicates * (double)replicates * columns * (perBlock + 1) / 12;
        double statistic = 0;
        var meanRanks = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            double gap = columnSums[c] - expected;
            statistic += gap * gap;
            meanRanks[c] = columnSums[c] / (blocks * replicates);
        }

        statistic /= scale;

        double correction = 1 - (ties / (blocks * (((double)perBlock * perBlock * perBlock) - perBlock)));
        if (correction > 0)
        {
            statistic /= correction;
        }

        double df = columns - 1;
        var counts = new int[columns];
        Array.Fill(counts, blocks * replicates);
        return new RankAnalysis(
            counts, meanRanks, statistic, df, 1 - ContinuousDistributions.Chi2Cdf(statistic, df), ties,
            Math.Sqrt(scale / (blocks * replicates)));
    }

    // --- Several responses at once --------------------------------------------------------------------

    /// <summary>
    /// <c>manova1</c>: whether several groups share a mean *vector*. The answer is a dimension — how
    /// many directions the group means need before what is left of their spread is indistinguishable
    /// from noise — reached by testing Wilks' lambda one dimension at a time.
    /// </summary>
    /// <param name="observations">One row per observation, one column per response variable.</param>
    /// <param name="group">Which group each observation belongs to.</param>
    /// <param name="groups">How many groups there are.</param>
    /// <param name="alpha">The level each dimension is tested at.</param>
    public static Manova ManovaFrom(double[,] observations, int[] group, int groups, double alpha)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(group);

        int n = observations.GetLength(0);
        int p = observations.GetLength(1);
        if (groups < 2 || n <= groups + p)
        {
            throw new ArgumentException(
                "a multivariate analysis needs at least two groups and more observations than groups plus variables.");
        }

        var counts = new int[groups];
        var groupMeans = new double[groups, p];
        var grand = new double[p];

        for (int i = 0; i < n; i++)
        {
            counts[group[i]]++;
            for (int j = 0; j < p; j++)
            {
                groupMeans[group[i], j] += observations[i, j];
                grand[j] += observations[i, j];
            }
        }

        for (int j = 0; j < p; j++)
        {
            grand[j] /= n;
        }

        for (int g = 0; g < groups; g++)
        {
            if (counts[g] == 0)
            {
                throw new ArgumentException("a multivariate analysis needs every group to hold an observation.");
            }

            for (int j = 0; j < p; j++)
            {
                groupMeans[g, j] /= counts[g];
            }
        }

        var within = new double[p, p];
        var between = new double[p, p];
        for (int i = 0; i < n; i++)
        {
            for (int a = 0; a < p; a++)
            {
                double da = observations[i, a] - groupMeans[group[i], a];
                for (int b = 0; b < p; b++)
                {
                    within[a, b] += da * (observations[i, b] - groupMeans[group[i], b]);
                }
            }
        }

        for (int g = 0; g < groups; g++)
        {
            for (int a = 0; a < p; a++)
            {
                double da = groupMeans[g, a] - grand[a];
                for (int b = 0; b < p; b++)
                {
                    between[a, b] += counts[g] * da * (groupMeans[g, b] - grand[b]);
                }
            }
        }

        // The canonical directions are the eigenvectors of W⁻¹B, which is not symmetric — but
        // W^(−1/2) B W^(−1/2) is, and has the same eigenvalues, so the symmetric path is taken and the
        // vectors transformed back. That keeps the eigenvalues real, which they are.
        double[,] whitening = InverseSquareRoot(within);
        double[,] scaled = Product(Product(whitening, between), whitening);
        Eigen eigen = Eigen.Factor(Symmetrized(scaled));

        int available = Math.Min(p, groups - 1);
        var order = new int[p];
        for (int i = 0; i < p; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => eigen.Values[b].Real.CompareTo(eigen.Values[a].Real));

        var eigenValues = new double[p];
        var eigenVectors = new double[p, p];
        for (int c = 0; c < p; c++)
        {
            eigenValues[c] = Math.Max(0, eigen.Values[order[c]].Real);
            for (int r = 0; r < p; r++)
            {
                double sum = 0;
                for (int k = 0; k < p; k++)
                {
                    sum += whitening[r, k] * eigen.Vectors[k, order[c]].Real;
                }

                eigenVectors[r, c] = sum;
            }
        }

        // Scale each direction so the within-group variance along it is one, which is what makes the
        // canonical variables comparable and the Mahalanobis distances below read in the usual units.
        double withinDf = n - groups;
        for (int c = 0; c < p; c++)
        {
            double variance = 0;
            for (int a = 0; a < p; a++)
            {
                for (int b = 0; b < p; b++)
                {
                    variance += eigenVectors[a, c] * within[a, b] * eigenVectors[b, c];
                }
            }

            variance /= withinDf;
            double scale = variance > 0 ? 1 / Math.Sqrt(variance) : 1;
            for (int r = 0; r < p; r++)
            {
                eigenVectors[r, c] *= scale;
            }
        }

        var lambda = new double[available];
        var chiSquare = new double[available];
        var chiSquareDf = new double[available];
        var probabilities = new double[available];
        int dimension = available;

        for (int s = 0; s < available; s++)
        {
            double product = 1;
            for (int i = s; i < available; i++)
            {
                product /= 1 + eigenValues[i];
            }

            lambda[s] = product;
            chiSquare[s] = -(n - 1 - ((p + groups) / 2.0)) * Math.Log(product);
            chiSquareDf[s] = (p - s) * (groups - 1.0 - s);
            probabilities[s] = chiSquareDf[s] > 0
                ? 1 - ContinuousDistributions.Chi2Cdf(chiSquare[s], chiSquareDf[s])
                : double.NaN;

            if (probabilities[s] > alpha && dimension == available)
            {
                dimension = s;
            }
        }

        var canonical = new double[n, p];
        var distances = new double[n];
        for (int i = 0; i < n; i++)
        {
            double own = 0;
            for (int c = 0; c < p; c++)
            {
                double centred = 0;
                double fromOwnGroup = 0;
                for (int j = 0; j < p; j++)
                {
                    centred += (observations[i, j] - grand[j]) * eigenVectors[j, c];
                    fromOwnGroup += (observations[i, j] - groupMeans[group[i], j]) * eigenVectors[j, c];
                }

                canonical[i, c] = centred;

                // The distance reported is to the observation's own group mean, in the same
                // within-group metric the canonical directions are scaled by.
                own += fromOwnGroup * fromOwnGroup;
            }

            distances[i] = own;
        }

        var groupDistances = new double[groups, groups];
        for (int a = 0; a < groups; a++)
        {
            for (int b = 0; b < groups; b++)
            {
                double squared = 0;
                for (int c = 0; c < p; c++)
                {
                    double value = 0;
                    for (int j = 0; j < p; j++)
                    {
                        value += (groupMeans[a, j] - groupMeans[b, j]) * eigenVectors[j, c];
                    }

                    squared += value * value;
                }

                groupDistances[a, b] = squared;
            }
        }

        return new Manova(
            within, between, withinDf, groups - 1.0, lambda, chiSquare, chiSquareDf, probabilities,
            dimension, eigenValues, eigenVectors, canonical, distances, groupDistances);
    }

    private static double[,] Symmetrized(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var symmetric = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                symmetric[r, c] = (matrix[r, c] + matrix[c, r]) / 2;
            }
        }

        return symmetric;
    }

    private static double[,] Product(double[,] left, double[,] right)
    {
        int n = left.GetLength(0);
        int inner = left.GetLength(1);
        int m = right.GetLength(1);
        var product = new double[n, m];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < m; c++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    sum += left[r, k] * right[k, c];
                }

                product[r, c] = sum;
            }
        }

        return product;
    }

    private static double[,] InverseSquareRoot(double[,] matrix)
    {
        Eigen eigen = Eigen.Factor(Symmetrized(matrix));
        int n = matrix.GetLength(0);
        double largest = 0;
        for (int i = 0; i < n; i++)
        {
            largest = Math.Max(largest, Math.Abs(eigen.Values[i].Real));
        }

        var root = new double[n, n];
        for (int k = 0; k < n; k++)
        {
            double value = eigen.Values[k].Real;
            if (value <= RankTolerance * Math.Max(1, largest))
            {
                throw new ArgumentException(
                    "the within-group scatter is singular, so the groups cannot be compared in every direction.");
            }

            double scale = 1 / Math.Sqrt(value);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    root[r, c] += scale * eigen.Vectors[r, k].Real * eigen.Vectors[c, k].Real;
                }
            }
        }

        return root;
    }
}
