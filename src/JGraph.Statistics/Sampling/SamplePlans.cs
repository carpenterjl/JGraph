namespace JGraph.Statistics.Sampling;

/// <summary>
/// Ways of choosing points: weighted draws from a population, and Latin hypercube designs.
/// </summary>
/// <remarks>
/// Everything here takes the caller's <see cref="System.Random"/> rather than owning one, so a script
/// that seeded the stream with <c>rng</c> gets the same design twice. That is the only reproducibility
/// promise these functions make: the numbers repeat for a given seed, and they are not MATLAB's.
/// </remarks>
public static class SamplePlans
{
    /// <summary>What a Latin hypercube design is optimized for, once it has been laid out.</summary>
    public enum LatinCriterion
    {
        /// <summary>Nothing — the first design produced is the answer.</summary>
        None,

        /// <summary>The design whose closest pair of points is furthest apart.</summary>
        Maximin,

        /// <summary>The design whose largest absolute pairwise correlation is smallest.</summary>
        Correlation,
    }

    /// <summary>
    /// <paramref name="count"/> indices drawn from <c>0 … population − 1</c>, optionally weighted and
    /// optionally without replacement.
    /// </summary>
    /// <remarks>
    /// Without replacement and with weights, each draw is proportional to what is left — the sequential
    /// scheme, which is what "sampling without replacement with unequal probabilities" ordinarily means
    /// and what makes the first draw's marginal probability exactly its weight.
    /// </remarks>
    /// <exception cref="ArgumentException">More were asked for than the population holds.</exception>
    public static int[] WeightedSample(
        Random random, int population, double[]? weights, int count, bool replacement)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!replacement && count > population)
        {
            throw new ArgumentException(
                "cannot take more than the population without replacement.");
        }

        var chosen = new int[count];

        if (weights is null)
        {
            if (replacement)
            {
                for (int i = 0; i < count; i++)
                {
                    chosen[i] = random.Next(population);
                }

                return chosen;
            }

            // A partial Fisher–Yates shuffle: as many swaps as there are draws, and no array of the
            // whole population is walked more than once.
            var order = new int[population];
            for (int i = 0; i < population; i++)
            {
                order[i] = i;
            }

            for (int i = 0; i < count; i++)
            {
                int pick = i + random.Next(population - i);
                (order[i], order[pick]) = (order[pick], order[i]);
                chosen[i] = order[i];
            }

            return chosen;
        }

        var remaining = (double[])weights.Clone();
        double total = 0;
        foreach (double weight in remaining)
        {
            if (weight < 0 || double.IsNaN(weight))
            {
                throw new ArgumentException("every weight must be zero or more.");
            }

            total += weight;
        }

        if (!(total > 0))
        {
            throw new ArgumentException("the weights must not all be zero.");
        }

        for (int i = 0; i < count; i++)
        {
            double target = random.NextDouble() * total;
            int pick = population - 1;
            double run = 0;
            for (int j = 0; j < population; j++)
            {
                run += remaining[j];
                if (target < run)
                {
                    pick = j;
                    break;
                }
            }

            // A zero-weight index can only be reached by rounding at the very end of the sweep, and
            // choosing it would be choosing something the caller said was impossible.
            while (remaining[pick] == 0 && pick > 0)
            {
                pick--;
            }

            chosen[i] = pick;
            if (!replacement)
            {
                total -= remaining[pick];
                remaining[pick] = 0;
                if (!(total > 0) && i + 1 < count)
                {
                    throw new ArgumentException(
                        "there are fewer values with a positive weight than were asked for.");
                }
            }
        }

        return chosen;
    }

    /// <summary>
    /// A Latin hypercube design: <paramref name="samples"/> points in <paramref name="variables"/>
    /// dimensions, one point in each of the <paramref name="samples"/> equal strata of every variable.
    /// </summary>
    /// <param name="random">The stream the permutations and jitter come from.</param>
    /// <param name="samples">How many points.</param>
    /// <param name="variables">How many dimensions.</param>
    /// <param name="smooth">
    /// Whether a point sits anywhere in its stratum (true) or exactly at the middle of it (false).
    /// </param>
    /// <param name="criterion">What to optimize over repeated attempts.</param>
    /// <param name="iterations">How many attempts the criterion chooses between.</param>
    public static double[,] LatinHypercube(
        Random random, int samples, int variables, bool smooth, LatinCriterion criterion, int iterations)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(variables);

        double[,] best = OneDesign(random, samples, variables, smooth);
        if (criterion == LatinCriterion.None || samples < 2 || variables < 1)
        {
            return best;
        }

        double bestScore = Score(best, criterion);
        for (int attempt = 1; attempt < Math.Max(1, iterations); attempt++)
        {
            double[,] candidate = OneDesign(random, samples, variables, smooth);
            double score = Score(candidate, criterion);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Every way of choosing <paramref name="k"/> of <paramref name="n"/> positions, in ascending
    /// order, each as an array of indices.
    /// </summary>
    public static List<int[]> Combinations(int n, int k)
    {
        var found = new List<int[]>();
        if (k < 0 || k > n)
        {
            return found;
        }

        var current = new int[k];
        Walk(0, 0);
        return found;

        void Walk(int start, int depth)
        {
            if (depth == k)
            {
                found.Add((int[])current.Clone());
                return;
            }

            for (int i = start; i <= n - (k - depth); i++)
            {
                current[depth] = i;
                Walk(i + 1, depth + 1);
            }
        }
    }

    private static double[,] OneDesign(Random random, int samples, int variables, bool smooth)
    {
        var design = new double[samples, variables];
        var order = new int[samples];

        for (int v = 0; v < variables; v++)
        {
            for (int i = 0; i < samples; i++)
            {
                order[i] = i;
            }

            for (int i = samples - 1; i > 0; i--)
            {
                int pick = random.Next(i + 1);
                (order[i], order[pick]) = (order[pick], order[i]);
            }

            for (int i = 0; i < samples; i++)
            {
                double offset = smooth ? random.NextDouble() : 0.5;
                design[i, v] = (order[i] + offset) / samples;
            }
        }

        return design;
    }

    /// <summary>Higher is better, whichever criterion is being scored, so one comparison serves both.</summary>
    private static double Score(double[,] design, LatinCriterion criterion) => criterion switch
    {
        LatinCriterion.Maximin => SmallestDistance(design),
        LatinCriterion.Correlation => -LargestCorrelation(design),
        _ => 0,
    };

    private static double SmallestDistance(double[,] design)
    {
        int n = design.GetLength(0);
        int p = design.GetLength(1);
        double smallest = double.PositiveInfinity;

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double sum = 0;
                for (int v = 0; v < p; v++)
                {
                    double gap = design[i, v] - design[j, v];
                    sum += gap * gap;
                }

                smallest = Math.Min(smallest, sum);
            }
        }

        return Math.Sqrt(smallest);
    }

    private static double LargestCorrelation(double[,] design)
    {
        int n = design.GetLength(0);
        int p = design.GetLength(1);
        var means = new double[p];
        var deviations = new double[p];

        for (int v = 0; v < p; v++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += design[i, v];
            }

            means[v] = sum / n;

            double square = 0;
            for (int i = 0; i < n; i++)
            {
                double gap = design[i, v] - means[v];
                square += gap * gap;
            }

            deviations[v] = Math.Sqrt(square);
        }

        double largest = 0;
        for (int a = 0; a < p; a++)
        {
            for (int b = a + 1; b < p; b++)
            {
                double cross = 0;
                for (int i = 0; i < n; i++)
                {
                    cross += (design[i, a] - means[a]) * (design[i, b] - means[b]);
                }

                double scale = deviations[a] * deviations[b];
                if (scale > 0)
                {
                    largest = Math.Max(largest, Math.Abs(cross / scale));
                }
            }
        }

        return largest;
    }
}
