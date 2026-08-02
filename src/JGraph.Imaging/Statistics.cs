namespace JGraph.Imaging;

/// <summary>
/// Whole-picture summaries: the mean and spread of every sample, the correlation between two
/// pictures, and the entropy of one.
/// </summary>
/// <remarks>
/// MATLAB gives these their own names — <c>mean2</c>, <c>std2</c>, <c>corr2</c> — rather than letting a
/// script write <c>mean(A(:))</c>, because a picture is the one kind of matrix nobody wants summarized
/// column by column. Entropy is the odd one out: it is not a moment of the samples but of their
/// histogram, so it depends on how finely the range is divided, and MATLAB fixes that at 256 levels
/// rather than leaving it to the caller.
/// </remarks>
public static class Statistics
{
    /// <summary>The mean of every sample (MATLAB <c>mean2</c>).</summary>
    public static double Mean(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            return double.NaN;
        }

        double total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                total += values[r, c];
            }
        }

        return total / (rows * cols);
    }

    /// <summary>
    /// The standard deviation of every sample (MATLAB <c>std2</c>), normalized by <c>n - 1</c>.
    /// </summary>
    /// <remarks>
    /// The <c>n - 1</c> is MATLAB's own default for <c>std</c>, and <c>std2</c> inherits it. It reads
    /// oddly for a picture — nobody thinks of a photograph as a sample drawn from a population of
    /// photographs — but a function that quietly differed from <c>std(A(:))</c> would be worse.
    /// </remarks>
    public static double StandardDeviation(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        long count = (long)rows * cols;
        if (count < 2)
        {
            return 0;
        }

        double mean = Mean(values);
        double sum = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double difference = values[r, c] - mean;
                sum += difference * difference;
            }
        }

        return Math.Sqrt(sum / (count - 1));
    }

    /// <summary>The correlation coefficient between two same-size pictures (MATLAB <c>corr2</c>).</summary>
    /// <remarks>
    /// This is the cosine of the angle between the two pictures once each has had its own mean taken
    /// out, so it answers "do these vary together" and says nothing about brightness or contrast: a
    /// picture correlates perfectly with twice itself. When either picture is flat the angle is not
    /// defined and the answer is NaN, which is what MATLAB returns and is more honest than zero.
    /// </remarks>
    public static double Correlation(double[,] a, double[,] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireSameSize(a, b);

        double meanA = Mean(a);
        double meanB = Mean(b);
        double cross = 0;
        double squaresA = 0;
        double squaresB = 0;
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double da = a[r, c] - meanA;
                double db = b[r, c] - meanB;
                cross += da * db;
                squaresA += da * da;
                squaresB += db * db;
            }
        }

        double denominator = Math.Sqrt(squaresA * squaresB);
        return denominator == 0 ? double.NaN : cross / denominator;
    }

    /// <summary>
    /// The entropy of a picture in bits (MATLAB <c>entropy</c>): the histogram's Shannon entropy over
    /// <paramref name="bins"/> equally spaced levels covering <c>[0, 1]</c>.
    /// </summary>
    /// <remarks>
    /// Entropy answers how many bits a sample is worth on average, so it measures how much a picture
    /// has to say rather than how bright it is — a flat field is zero however bright, and a field of
    /// noise is near the maximum however dull. Because it is a property of the histogram and not of
    /// the samples, the bin count is part of the definition: MATLAB converts everything but a mask to
    /// eight bits first, which is the 256 here, and reads a mask on two bins.
    /// </remarks>
    public static double Entropy(double[,] values, int bins = 256)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);

        var counts = new long[bins];
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        long total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double sample = values[r, c];
                if (double.IsNaN(sample))
                {
                    continue;
                }

                int bin = (int)Math.Round(Math.Clamp(sample, 0, 1) * (bins - 1));
                counts[bin]++;
                total++;
            }
        }

        if (total == 0)
        {
            return 0;
        }

        double entropy = 0;
        foreach (long count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            double p = (double)count / total;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>Refuses a pair of pictures that are not the same size.</summary>
    internal static void RequireSameSize(double[,] a, double[,] b)
    {
        if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
        {
            throw new ArgumentException(
                $"the two pictures must be the same size, but one is {a.GetLength(0)}-by-{a.GetLength(1)} " +
                $"and the other is {b.GetLength(0)}-by-{b.GetLength(1)}.");
        }
    }
}
