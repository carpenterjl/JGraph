using JGraph.Statistics.Distributions;

namespace JGraph.Statistics;

/// <summary>
/// What a measured process says about the specification it has to meet: how much of it falls outside
/// the limits, and how much of the spread the measurement system is responsible for rather than the
/// parts.
/// </summary>
/// <remarks>
/// Both answers here are numbers rather than charts, which is why they are in the working set while
/// <c>controlchart</c> is not: a control chart is a figure with rule annotations hanging off it, and a
/// capability index is arithmetic over a mean and a standard deviation.
/// </remarks>
public static class ProcessCapability
{
    /// <summary>The capability of a process against a pair of specification limits.</summary>
    /// <param name="Mean">The sample mean.</param>
    /// <param name="Deviation">The sample standard deviation.</param>
    /// <param name="Outside">The probability of falling outside either limit.</param>
    /// <param name="BelowLower">The probability of falling below the lower limit.</param>
    /// <param name="AboveUpper">The probability of falling above the upper limit.</param>
    /// <param name="Cp">The spread the limits allow, over the spread the process has.</param>
    /// <param name="Cpl">The same, measured only downward from the mean.</param>
    /// <param name="Cpu">The same, measured only upward.</param>
    /// <param name="Cpk">The worse of the two one-sided indices.</param>
    /// <param name="Cpm">The index that also charges the process for being off target.</param>
    public readonly record struct Capability(
        double Mean,
        double Deviation,
        double Outside,
        double BelowLower,
        double AboveUpper,
        double Cp,
        double Cpl,
        double Cpu,
        double Cpk,
        double Cpm);

    /// <summary>
    /// The capability indices of a sample against limits, either of which may be infinite when the
    /// specification is one-sided.
    /// </summary>
    public static Capability Capable(IReadOnlyList<double> values, double lower, double upper)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] clean = DescriptiveStatistics.WithoutNaN(values);
        if (clean.Length < 2)
        {
            throw new ArgumentException("Capability needs at least two observations.", nameof(values));
        }

        if (!(upper > lower))
        {
            throw new ArgumentException("The upper specification limit must sit above the lower one.", nameof(upper));
        }

        double mean = DescriptiveStatistics.Mean(clean);
        double deviation = DescriptiveStatistics.StandardDeviation(clean, population: false);

        double below = double.IsNegativeInfinity(lower)
            ? 0
            : ContinuousDistributions.NormalCdf(lower, mean, deviation);
        double above = double.IsPositiveInfinity(upper)
            ? 0
            : 1 - ContinuousDistributions.NormalCdf(upper, mean, deviation);

        double cp = (upper - lower) / (6 * deviation);
        double cpl = (mean - lower) / (3 * deviation);
        double cpu = (upper - mean) / (3 * deviation);

        // The target sits halfway between two finite limits, and at the finite one when only one limit
        // was given — which is what makes Cpm one-sided rather than undefined there.
        double target = double.IsInfinity(lower) ? upper : double.IsInfinity(upper) ? lower : (lower + upper) / 2;
        double offset = mean - target;
        double cpm = cp / Math.Sqrt(1 + ((offset * offset) / (deviation * deviation)));

        return new Capability(mean, deviation, below + above, below, above, cp, cpl, cpu, Math.Min(cpl, cpu), cpm);
    }

    /// <summary>Which effects a gage study charges the measurement system for.</summary>
    public enum GageModel
    {
        /// <summary>Part and operator, with no interaction between them.</summary>
        Linear,

        /// <summary>Part, operator, and the interaction of the two.</summary>
        Interaction,

        /// <summary>Operator, and part nested inside operator — each operator measured its own parts.</summary>
        Nested,
    }

    /// <summary>One row of a gage study: a source of variation and how much of it there is.</summary>
    /// <param name="Source">What the row is about.</param>
    /// <param name="Variance">The variance component.</param>
    /// <param name="PercentVariance">That component as a percentage of the total.</param>
    /// <param name="Sigma">The study spread, which is 5.15 standard deviations.</param>
    /// <param name="PercentSigma">The study spread as a percentage of the total study spread.</param>
    /// <param name="PercentTolerance">The study spread as a percentage of the tolerance, when one was given.</param>
    public readonly record struct GageRow(
        string Source,
        double Variance,
        double PercentVariance,
        double Sigma,
        double PercentSigma,
        double PercentTolerance);

    /// <summary>The answer of a gage repeatability and reproducibility study.</summary>
    /// <param name="Rows">The variance decomposition, one row per source.</param>
    /// <param name="GageDeviation">The measurement system's own standard deviation.</param>
    /// <param name="DistinctCategories">How many distinct levels of part the system can tell apart.</param>
    public readonly record struct GageStudy(
        IReadOnlyList<GageRow> Rows, double GageDeviation, int DistinctCategories);

    /// <summary>
    /// A gage repeatability and reproducibility study: how much of the spread in a set of measurements
    /// belongs to the parts, and how much to the act of measuring them.
    /// </summary>
    /// <remarks>
    /// The decomposition is an analysis of variance over the two grouping factors, with the mean squares
    /// turned back into variance components. Repeatability is the residual — the same operator measuring
    /// the same part twice — and reproducibility is everything the operator contributed, which under the
    /// interaction model includes the operator's disagreement with themselves from part to part. A
    /// component the arithmetic makes negative is reported as zero, which is what every published
    /// treatment does and what keeps the percentages from turning meaningless.
    /// </remarks>
    /// <param name="measurements">One measurement per reading.</param>
    /// <param name="part">Which part each reading is of.</param>
    /// <param name="operators">Which operator took each reading; empty for a study with one operator.</param>
    /// <param name="model">Which effects to charge the measurement system for.</param>
    /// <param name="tolerance">The width of the specification, or zero for none.</param>
    /// <param name="deviations">How many standard deviations the study spread is; the convention is 5.15.</param>
    public static GageStudy Gage(
        IReadOnlyList<double> measurements,
        IReadOnlyList<int> part,
        IReadOnlyList<int> operators,
        GageModel model,
        double tolerance,
        double deviations)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(operators);
        if (measurements.Count != part.Count || (operators.Count != 0 && operators.Count != measurements.Count))
        {
            throw new ArgumentException("A gage study needs one part, and one operator, per measurement.", nameof(part));
        }

        int[] parts = Renumber(part, out int partCount);
        int[] people = operators.Count == 0
            ? new int[measurements.Count]
            : Renumber(operators, out _);
        int peopleCount = operators.Count == 0 ? 1 : people.Max() + 1;

        int total = measurements.Count;
        if (total <= partCount * peopleCount)
        {
            throw new ArgumentException(
                "A gage study needs more than one measurement of some part by some operator.", nameof(measurements));
        }

        double grand = 0;
        foreach (double value in measurements)
        {
            grand += value;
        }

        grand /= total;

        // The cell means are the whole calculation: every sum of squares below is a weighted spread of
        // one set of means about the grand mean, and the residual is what the cell means do not explain.
        var cellSum = new double[partCount, peopleCount];
        var cellCount = new int[partCount, peopleCount];
        for (int i = 0; i < total; i++)
        {
            cellSum[parts[i], people[i]] += measurements[i];
            cellCount[parts[i], people[i]]++;
        }

        double replicates = (double)total / (partCount * peopleCount);

        double squaresPart = 0;
        for (int p = 0; p < partCount; p++)
        {
            (double sum, int count) = RowTotals(cellSum, cellCount, p, peopleCount);
            if (count > 0)
            {
                squaresPart += count * Square((sum / count) - grand);
            }
        }

        double squaresPerson = 0;
        for (int o = 0; o < peopleCount; o++)
        {
            (double sum, int count) = ColumnTotals(cellSum, cellCount, o, partCount);
            if (count > 0)
            {
                squaresPerson += count * Square((sum / count) - grand);
            }
        }

        double squaresCells = 0;
        for (int p = 0; p < partCount; p++)
        {
            for (int o = 0; o < peopleCount; o++)
            {
                if (cellCount[p, o] > 0)
                {
                    squaresCells += cellCount[p, o] * Square((cellSum[p, o] / cellCount[p, o]) - grand);
                }
            }
        }

        double squaresTotal = 0;
        foreach (double value in measurements)
        {
            squaresTotal += Square(value - grand);
        }

        double squaresError = squaresTotal - squaresCells;
        double squaresInteraction = squaresCells - squaresPart - squaresPerson;

        int freedomPart = partCount - 1;
        int freedomPerson = Math.Max(peopleCount - 1, 0);
        int freedomInteraction = freedomPart * freedomPerson;
        int freedomError = total - (partCount * peopleCount);

        double meanError = freedomError > 0 ? squaresError / freedomError : 0;
        double meanPart = freedomPart > 0 ? squaresPart / freedomPart : 0;
        double meanPerson = freedomPerson > 0 ? squaresPerson / freedomPerson : 0;
        double meanInteraction = freedomInteraction > 0 ? squaresInteraction / freedomInteraction : 0;

        double repeatability = Math.Max(meanError, 0);
        double interaction;
        double person;
        double partVariance;

        if (model == GageModel.Interaction && freedomInteraction > 0 && freedomError > 0)
        {
            interaction = Math.Max((meanInteraction - meanError) / replicates, 0);
            person = Math.Max((meanPerson - meanInteraction) / (partCount * replicates), 0);
            partVariance = Math.Max((meanPart - meanInteraction) / (peopleCount * replicates), 0);
        }
        else if (model == GageModel.Nested)
        {
            // Nested: each operator measured their own parts, so there is no part effect to separate
            // from the interaction, and the part term is read against the residual directly.
            interaction = 0;
            person = Math.Max((meanPerson - meanPart) / (partCount * replicates), 0);
            partVariance = Math.Max((meanPart - meanError) / replicates, 0);
        }
        else
        {
            interaction = 0;
            person = Math.Max((meanPerson - meanError) / (partCount * replicates), 0);
            partVariance = Math.Max((meanPart - meanError) / (peopleCount * replicates), 0);
        }

        double reproducibility = person + interaction;
        double gage = repeatability + reproducibility;
        double all = gage + partVariance;

        var rows = new List<GageRow>();
        void Add(string source, double variance) =>
            rows.Add(new GageRow(
                source,
                variance,
                all > 0 ? 100 * variance / all : 0,
                deviations * Math.Sqrt(variance),
                all > 0 ? 100 * Math.Sqrt(variance / all) : 0,
                tolerance > 0 ? 100 * deviations * Math.Sqrt(variance) / tolerance : double.NaN));

        Add("Gage R&R", gage);
        Add("Repeatability", repeatability);
        Add("Reproducibility", reproducibility);
        if (peopleCount > 1)
        {
            Add("Operator", person);
            if (model == GageModel.Interaction && freedomInteraction > 0)
            {
                Add("Part*Operator", interaction);
            }
        }

        Add("Part", partVariance);
        Add("Total", all);

        // The number of distinct categories is how many part widths fit inside the measurement noise,
        // scaled by the constant that turns two standard deviations into a confidence interval width.
        int categories = gage > 0
            ? (int)Math.Truncate(1.41 * Math.Sqrt(partVariance / gage))
            : 0;

        return new GageStudy(rows, Math.Sqrt(gage), Math.Max(categories, 0));
    }

    private static (double Sum, int Count) RowTotals(double[,] sums, int[,] counts, int row, int columns)
    {
        double sum = 0;
        int count = 0;
        for (int c = 0; c < columns; c++)
        {
            sum += sums[row, c];
            count += counts[row, c];
        }

        return (sum, count);
    }

    private static (double Sum, int Count) ColumnTotals(double[,] sums, int[,] counts, int column, int rows)
    {
        double sum = 0;
        int count = 0;
        for (int r = 0; r < rows; r++)
        {
            sum += sums[r, column];
            count += counts[r, column];
        }

        return (sum, count);
    }

    private static int[] Renumber(IReadOnlyList<int> labels, out int distinct)
    {
        var seen = new Dictionary<int, int>();
        var index = new int[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            if (!seen.TryGetValue(labels[i], out int number))
            {
                number = seen.Count;
                seen[labels[i]] = number;
            }

            index[i] = number;
        }

        distinct = seen.Count;
        return index;
    }

    private static double Square(double value) => value * value;
}
