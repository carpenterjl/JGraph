namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// The tests that replace the observations by their ranks and so assume nothing about the shape of
/// the distribution: Wilcoxon's rank sum and signed rank, the sign test, and Ansari and Bradley's
/// test of dispersion.
/// </summary>
/// <remarks>
/// <para>
/// Each has an exact null distribution — a count of how many of the equally likely rearrangements give
/// a statistic at least as extreme — and a normal approximation to it. The exact count is used on
/// small samples, where the approximation is worst and the count is cheap; above the cut-off the
/// approximation takes over, with the half-step continuity correction and, where there are ties, the
/// variance correction they call for.
/// </para>
/// <para>
/// Ties make the exact distribution wrong rather than slow: it counts rearrangements of the integers
/// 1…N, and tied observations do not have integer ranks. So an exact test asked for over tied data is
/// refused by name rather than answered with a number that is not what was asked for.
/// </para>
/// </remarks>
public static class RankTests
{
    /// <summary>The outcome of a rank test.</summary>
    /// <param name="P">The tail probability.</param>
    /// <param name="Statistic">The rank statistic itself.</param>
    /// <param name="Z">The normal statistic, or NaN when the answer was counted exactly.</param>
    /// <param name="Exact">Whether the probability was counted rather than approximated.</param>
    public readonly record struct RankOutcome(double P, double Statistic, double Z, bool Exact);

    /// <summary>Whether a rank test counts its null distribution or approximates it.</summary>
    public enum Method
    {
        /// <summary>Counted exactly on a small sample, approximated above the cut-off.</summary>
        Automatic,

        /// <summary>Counted exactly, whatever the sample size.</summary>
        Exact,

        /// <summary>Approximated by a normal, whatever the sample size.</summary>
        Approximate,
    }

    /// <summary>
    /// <c>ranksum</c>: whether two independent samples come from distributions with the same median,
    /// judged by where the first sample's observations fall in the pooled ranking.
    /// </summary>
    public static RankOutcome RankSum(
        IReadOnlyList<double> x, IReadOnlyList<double> y, Tail tail, Method method)
    {
        double[] first = TestSupport.Clean(x);
        double[] second = TestSupport.Clean(y);
        int nx = first.Length;
        int ny = second.Length;
        if (nx < 1 || ny < 1)
        {
            throw new ArgumentException("a rank sum test needs observations in both samples.");
        }

        var pooled = new double[nx + ny];
        Array.Copy(first, pooled, nx);
        Array.Copy(second, 0, pooled, nx, ny);

        double[] ranks = TestSupport.Ranks(pooled);
        double statistic = 0;
        for (int i = 0; i < nx; i++)
        {
            statistic += ranks[i];
        }

        var sorted = new double[pooled.Length];
        Array.Copy(pooled, sorted, pooled.Length);
        Array.Sort(sorted);
        double ties = TestSupport.TieAdjustment(sorted);

        int n = nx + ny;

        // MathWorks' own cut-off: a small enough pair of samples with no ties is counted, and
        // everything else is approximated.
        bool exact = Chooses(method, ties == 0 && Math.Min(nx, ny) < 10 && n < 20, ties, "ranksum");
        double mean = nx * (n + 1.0) / 2;
        double variance = ((double)nx * ny / 12 * (n + 1)) - ((double)nx * ny * ties / (12.0 * n * (n - 1)));
        double z = variance > 0
            ? (statistic - mean - (0.5 * Math.Sign(statistic - mean))) / Math.Sqrt(variance)
            : double.NaN;

        if (!exact)
        {
            return new RankOutcome(TestSupport.NormalTail(z, tail), statistic, z, false);
        }

        // Count over the smaller sample: the distribution of the larger sample's rank sum is the
        // mirror of it, and counting the smaller one is the cheaper half of the same table.
        bool mirrored = ny < nx;
        int size = mirrored ? ny : nx;
        double counted = mirrored ? (n * (n + 1.0) / 2) - statistic : statistic;
        double[] distribution = SubsetSums(n, size);
        double p = ExactTail(distribution, counted, size * (size + 1) / 2.0, MirrorTail(tail, mirrored));
        return new RankOutcome(p, statistic, z, true);
    }

    /// <summary>
    /// <c>signrank</c>: whether the differences of paired observations are centred on zero, judged by
    /// the ranks of their sizes with their signs put back.
    /// </summary>
    public static RankOutcome SignedRank(IReadOnlyList<double> differences, Tail tail, Method method)
    {
        ArgumentNullException.ThrowIfNull(differences);
        var kept = new List<double>(differences.Count);
        int zeros = 0;
        foreach (double difference in differences)
        {
            if (double.IsNaN(difference))
            {
                continue;
            }

            if (difference == 0)
            {
                // A difference of exactly zero has no sign to rank, so it leaves the test rather than
                // being given one arbitrarily.
                zeros++;
                continue;
            }

            kept.Add(difference);
        }

        int n = kept.Count;
        if (n < 1)
        {
            throw new ArgumentException("a signed rank test needs at least one non-zero difference.");
        }

        var sizes = new double[n];
        for (int i = 0; i < n; i++)
        {
            sizes[i] = Math.Abs(kept[i]);
        }

        double[] ranks = TestSupport.Ranks(sizes);
        double statistic = 0;
        for (int i = 0; i < n; i++)
        {
            if (kept[i] > 0)
            {
                statistic += ranks[i];
            }
        }

        var sorted = new double[n];
        Array.Copy(sizes, sorted, n);
        Array.Sort(sorted);
        double ties = TestSupport.TieAdjustment(sorted);

        bool exact = Chooses(method, ties == 0 && zeros == 0 && n <= 15, ties, "signrank");
        double mean = n * (n + 1.0) / 4;
        double variance = ((n * (n + 1.0) * ((2.0 * n) + 1)) - (ties / 2)) / 24;
        double z = variance > 0
            ? (statistic - mean - (0.5 * Math.Sign(statistic - mean))) / Math.Sqrt(variance)
            : double.NaN;

        if (!exact)
        {
            return new RankOutcome(TestSupport.NormalTail(z, tail), statistic, z, false);
        }

        double[] distribution = SignedRankSums(n);
        return new RankOutcome(ExactTail(distribution, statistic, 0, tail), statistic, z, true);
    }

    /// <summary>
    /// <c>signtest</c>: the same question with the sizes thrown away as well, leaving only how many
    /// differences are positive — a coin toss under the null.
    /// </summary>
    public static RankOutcome Sign(IReadOnlyList<double> differences, Tail tail, Method method)
    {
        ArgumentNullException.ThrowIfNull(differences);
        int positive = 0;
        int negative = 0;
        foreach (double difference in differences)
        {
            if (difference > 0)
            {
                positive++;
            }
            else if (difference < 0)
            {
                negative++;
            }
        }

        int n = positive + negative;
        if (n < 1)
        {
            throw new ArgumentException("a sign test needs at least one non-zero difference.");
        }

        bool exact = method switch
        {
            Method.Exact => true,
            Method.Approximate => false,
            _ => n <= 100,
        };

        double z = ((positive - (n / 2.0)) - (0.5 * Math.Sign(positive - (n / 2.0)))) / Math.Sqrt(n / 4.0);
        if (!exact)
        {
            return new RankOutcome(TestSupport.NormalTail(z, tail), positive, z, false);
        }

        double p = tail switch
        {
            Tail.Right => BinomialTail(positive, n, atMost: false),
            Tail.Left => BinomialTail(positive, n, atMost: true),
            _ => Math.Min(1, 2 * Math.Min(BinomialTail(positive, n, true), BinomialTail(positive, n, false))),
        };

        return new RankOutcome(p, positive, z, true);
    }

    /// <summary>
    /// <c>ansaribradley</c>: whether two samples are equally dispersed, judged by ranking the pooled
    /// observations inward from both ends at once. A sample concentrated in the middle collects the
    /// large scores, so a <em>large</em> statistic means a <em>small</em> spread — which is why the
    /// right-hand alternative reads the lower tail.
    /// </summary>
    public static RankOutcome AnsariBradley(
        IReadOnlyList<double> x, IReadOnlyList<double> y, Tail tail, Method method)
    {
        double[] first = TestSupport.Clean(x);
        double[] second = TestSupport.Clean(y);
        int nx = first.Length;
        int ny = second.Length;
        if (nx < 1 || ny < 1)
        {
            throw new ArgumentException("Ansari and Bradley's test needs observations in both samples.");
        }

        int n = nx + ny;
        var pooled = new double[n];
        Array.Copy(first, pooled, nx);
        Array.Copy(second, 0, pooled, nx, ny);

        double[] ranks = TestSupport.Ranks(pooled);
        var scores = new double[n];
        double statistic = 0;
        for (int i = 0; i < n; i++)
        {
            scores[i] = Math.Min(ranks[i], n + 1 - ranks[i]);
            if (i < nx)
            {
                statistic += scores[i];
            }
        }

        var sorted = new double[n];
        Array.Copy(pooled, sorted, n);
        Array.Sort(sorted);
        double ties = TestSupport.TieAdjustment(sorted);

        bool even = n % 2 == 0;
        double mean = even ? nx * (n + 2.0) / 4 : nx * (n + 1.0) * (n + 1.0) / (4.0 * n);
        double variance = even
            ? (double)nx * ny * (n + 2.0) * (n - 2.0) / (48.0 * (n - 1))
            : (double)nx * ny * (n + 1.0) * (3.0 + ((double)n * n)) / (48.0 * n * n);

        double z = variance > 0 ? (statistic - mean) / Math.Sqrt(variance) : double.NaN;
        bool exact = Chooses(method, ties == 0 && n <= 25, ties, "ansaribradley");

        // A large statistic means the first sample is the tighter one, so the alternative that its
        // variance is the greater looks at small statistics — the opposite tail from the one the
        // statistic's own sign would suggest.
        Tail statisticTail = tail switch
        {
            Tail.Right => Tail.Left,
            Tail.Left => Tail.Right,
            _ => Tail.Both,
        };

        if (!exact)
        {
            return new RankOutcome(TestSupport.NormalTail(z, statisticTail), statistic, z, false);
        }

        double[] distribution = ScoreSums(scores, nx);
        return new RankOutcome(ExactTail(distribution, statistic, 0, statisticTail), statistic, z, true);
    }

    private static bool Chooses(Method method, bool automatic, double ties, string name) => method switch
    {
        Method.Exact when ties > 0 => throw new ArgumentException(
            $"{name}: the exact distribution counts rearrangements of distinct ranks, "
            + "so it cannot be used on data with ties; ask for the approximation instead."),
        Method.Exact => true,
        Method.Approximate => false,
        _ => automatic,
    };

    private static Tail MirrorTail(Tail tail, bool mirrored) => !mirrored
        ? tail
        : tail switch
        {
            Tail.Right => Tail.Left,
            Tail.Left => Tail.Right,
            _ => Tail.Both,
        };

    /// <summary>
    /// The probability of a statistic at least as extreme as the one seen, read off a counted
    /// distribution whose index <c>i</c> stands for the value <c>offset + i</c>.
    /// </summary>
    private static double ExactTail(double[] distribution, double statistic, double offset, Tail tail)
    {
        double total = 0;
        foreach (double count in distribution)
        {
            total += count;
        }

        double atMost = 0;
        double atLeast = 0;
        for (int i = 0; i < distribution.Length; i++)
        {
            double value = offset + i;
            if (value <= statistic + 1e-9)
            {
                atMost += distribution[i];
            }

            if (value >= statistic - 1e-9)
            {
                atLeast += distribution[i];
            }
        }

        atMost /= total;
        atLeast /= total;
        return Math.Clamp(tail switch
        {
            Tail.Right => atLeast,
            Tail.Left => atMost,
            _ => 2 * Math.Min(atMost, atLeast),
        }, 0, 1);
    }

    /// <summary>
    /// How many subsets of <paramref name="size"/> of the ranks 1…<paramref name="n"/> have each
    /// possible sum, indexed from the smallest sum a subset of that size can have.
    /// </summary>
    private static double[] SubsetSums(int n, int size)
    {
        int smallest = size * (size + 1) / 2;
        int largest = size * ((2 * n) - size + 1) / 2;
        int span = largest - smallest + 1;

        // counts[k, s] — how many subsets of k ranks sum to s. Filled rank by rank so each rank is
        // offered to every subset size once, which is what keeps this a table rather than a search.
        var counts = new double[size + 1, largest + 1];
        counts[0, 0] = 1;
        for (int rank = 1; rank <= n; rank++)
        {
            for (int k = Math.Min(size, rank); k >= 1; k--)
            {
                for (int s = largest; s >= rank; s--)
                {
                    counts[k, s] += counts[k - 1, s - rank];
                }
            }
        }

        var distribution = new double[span];
        for (int i = 0; i < span; i++)
        {
            distribution[i] = counts[size, smallest + i];
        }

        return distribution;
    }

    /// <summary>How many sign patterns of <paramref name="n"/> ranks give each signed-rank sum.</summary>
    private static double[] SignedRankSums(int n)
    {
        int largest = n * (n + 1) / 2;
        var counts = new double[largest + 1];
        counts[0] = 1;
        for (int rank = 1; rank <= n; rank++)
        {
            for (int s = largest; s >= rank; s--)
            {
                counts[s] += counts[s - rank];
            }
        }

        return counts;
    }

    /// <summary>
    /// How many ways of choosing <paramref name="size"/> of the scores give each total. The scores are
    /// not distinct — that is the point of the Ansari–Bradley scoring — so the table is built over the
    /// items themselves rather than over a range of integers.
    /// </summary>
    private static double[] ScoreSums(double[] scores, int size)
    {
        int largest = 0;
        foreach (double score in scores)
        {
            largest += (int)Math.Round(score);
        }

        var counts = new double[size + 1, largest + 1];
        counts[0, 0] = 1;
        int used = 0;
        foreach (double score in scores)
        {
            int value = (int)Math.Round(score);
            used++;
            for (int k = Math.Min(size, used); k >= 1; k--)
            {
                for (int s = largest; s >= value; s--)
                {
                    counts[k, s] += counts[k - 1, s - value];
                }
            }
        }

        var distribution = new double[largest + 1];
        for (int s = 0; s <= largest; s++)
        {
            distribution[s] = counts[size, s];
        }

        return distribution;
    }

    private static double BinomialTail(int successes, int trials, bool atMost)
    {
        double sum = 0;
        double logTotal = trials * Math.Log(0.5);
        int from = atMost ? 0 : successes;
        int to = atMost ? successes : trials;
        for (int k = from; k <= to; k++)
        {
            sum += Math.Exp(TestSupport.LogChoose(trials, k) + logTotal);
        }

        return Math.Clamp(sum, 0, 1);
    }
}
