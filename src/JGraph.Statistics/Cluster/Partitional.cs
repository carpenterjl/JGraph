using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Cluster;

/// <summary>What to do when a cluster loses its last member.</summary>
public enum EmptyClusterRule
{
    /// <summary>Say so and stop.</summary>
    Error,

    /// <summary>Carry on with one cluster fewer.</summary>
    Drop,

    /// <summary>Give it the point that is farthest from its own centre.</summary>
    Singleton,
}

/// <summary>Where the first centres come from.</summary>
public enum StartRule
{
    /// <summary>Chosen one at a time, each with probability proportional to its squared distance from the nearest centre already chosen.</summary>
    Plus,

    /// <summary>A uniformly random sample of the observations.</summary>
    Sample,

    /// <summary>Uniform draws from the box the data occupies.</summary>
    Uniform,

    /// <summary>Cluster centres given by the caller.</summary>
    Given,
}

/// <summary>
/// Clustering that partitions rather than nests: k-means and its medoid variant, density-based
/// clustering, spectral clustering, and the silhouette that says how well any of them did.
/// </summary>
/// <remarks>
/// <para>
/// k-means and k-medoids share everything but the question of what a cluster's representative is —
/// a mean the data need not contain, or the member with the least total distance to the rest — so
/// they share one loop with that choice handed in. What they do not share with the hierarchical
/// methods is that they start from a guess, so every one of them draws from the stream <c>rng</c>
/// seeds, and a script that seeds it gets the same partition twice.
/// </para>
/// <para>
/// Only k-means is written in the squared Euclidean distance; the other three measure with whatever
/// <see cref="DistanceMeasure"/> they are handed, which is what lets k-medoids cluster by
/// correlation and DBSCAN by city block.
/// </para>
/// </remarks>
public static class Partitional
{
    /// <summary>What a partition came to.</summary>
    /// <param name="Labels">Which cluster each observation fell in, from one.</param>
    /// <param name="Centres">One representative per cluster, a row each.</param>
    /// <param name="WithinSums">The total distance from each cluster's members to its representative.</param>
    /// <param name="ToCentres">Every observation's distance to every representative.</param>
    /// <param name="Iterations">How many passes the loop took.</param>
    /// <param name="Converged">Whether the labels stopped changing before the limit.</param>
    public readonly record struct Partition(
        int[] Labels, double[,] Centres, double[] WithinSums, double[,] ToCentres, int Iterations, bool Converged);

    /// <summary>How a partitional fit is to be run.</summary>
    /// <param name="Replicates">How many times to run it, keeping the best.</param>
    /// <param name="MaxIterations">The most passes any one run may take.</param>
    /// <param name="Start">Where the first representatives come from.</param>
    /// <param name="Given">The starting representatives when <paramref name="Start"/> is <see cref="StartRule.Given"/>.</param>
    /// <param name="OnEmpty">What to do when a cluster empties.</param>
    public readonly record struct Plan(
        int Replicates = 1,
        int MaxIterations = 100,
        StartRule Start = StartRule.Plus,
        double[,]? Given = null,
        EmptyClusterRule OnEmpty = EmptyClusterRule.Singleton);

    /// <summary>Clusters by mean, in the squared Euclidean distance.</summary>
    public static Partition KMeans(IReadOnlyList<double[]> data, int k, Plan plan, Random random) =>
        Cluster(data, k, plan, random, DistanceMeasure.Create(DistanceMetric.SquaredEuclidean, data), byMean: true);

    /// <summary>Clusters by medoid — the member of each cluster closest to the rest of it.</summary>
    public static Partition KMedoids(
        IReadOnlyList<double[]> data, int k, Plan plan, Random random, DistanceMeasure measure) =>
        Cluster(data, k, plan, random, measure, byMean: false);

    private static Partition Cluster(
        IReadOnlyList<double[]> data, int k, Plan plan, Random random, DistanceMeasure measure, bool byMean)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(measure);
        if (k < 1)
        {
            throw new ArgumentException("The number of clusters must be at least one.", nameof(k));
        }

        if (data.Count < k)
        {
            throw new ArgumentException(
                "There are fewer observations than clusters asked for.", nameof(k));
        }

        int replicates = Math.Max(1, plan.Replicates);
        Partition best = default;
        double bestTotal = double.PositiveInfinity;
        for (int attempt = 0; attempt < replicates; attempt++)
        {
            Partition run = Once(data, k, plan, random, measure, byMean);
            double total = 0;
            foreach (double within in run.WithinSums)
            {
                total += within;
            }

            if (total < bestTotal)
            {
                bestTotal = total;
                best = run;
            }
        }

        return best;
    }

    private static Partition Once(
        IReadOnlyList<double[]> data, int k, Plan plan, Random random, DistanceMeasure measure, bool byMean)
    {
        int n = data.Count;
        int width = data[0].Length;
        double[][] centres = FirstCentres(data, k, plan, random, measure, width);

        var labels = new int[n];
        Array.Fill(labels, -1);
        bool converged = false;
        int iterations = 0;
        int limit = Math.Max(1, plan.MaxIterations);

        for (; iterations < limit; iterations++)
        {
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                int nearest = 0;
                double best = double.PositiveInfinity;
                for (int c = 0; c < centres.Length; c++)
                {
                    double d = measure.Distance(data[i], centres[c]);
                    if (d < best)
                    {
                        best = d;
                        nearest = c;
                    }
                }

                if (labels[i] != nearest)
                {
                    labels[i] = nearest;
                    moved = true;
                }
            }

            if (!moved)
            {
                converged = true;
                break;
            }

            centres = Recentre(data, labels, centres, measure, byMean, plan.OnEmpty, width);
        }

        // The loop above stops the moment no observation changed cluster, so on that pass the centres
        // were not recomputed — which is right, because recomputing them from unchanged memberships
        // would give the same centres back.
        double[,] finalCentres = Rows(centres, width);
        var toCentres = new double[n, centres.Length];
        var within = new double[centres.Length];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < centres.Length; c++)
            {
                toCentres[i, c] = measure.Distance(data[i], centres[c]);
            }

            within[labels[i]] += toCentres[i, labels[i]];
        }

        var oneBased = new int[n];
        for (int i = 0; i < n; i++)
        {
            oneBased[i] = labels[i] + 1;
        }

        return new Partition(oneBased, finalCentres, within, toCentres, iterations + 1, converged);
    }

    private static double[][] FirstCentres(
        IReadOnlyList<double[]> data, int k, Plan plan, Random random, DistanceMeasure measure, int width)
    {
        switch (plan.Start)
        {
            case StartRule.Given:
            {
                double[,] given = plan.Given
                    ?? throw new ArgumentException("No starting centres were given.", nameof(plan));
                if (given.GetLength(0) != k || given.GetLength(1) != width)
                {
                    throw new ArgumentException(
                        "The starting centres must be one row per cluster and one column per variable.",
                        nameof(plan));
                }

                var centres = new double[k][];
                for (int c = 0; c < k; c++)
                {
                    centres[c] = new double[width];
                    for (int j = 0; j < width; j++)
                    {
                        centres[c][j] = given[c, j];
                    }
                }

                return centres;
            }

            case StartRule.Uniform:
            {
                var low = new double[width];
                var high = new double[width];
                for (int j = 0; j < width; j++)
                {
                    low[j] = double.PositiveInfinity;
                    high[j] = double.NegativeInfinity;
                }

                foreach (double[] row in data)
                {
                    for (int j = 0; j < width; j++)
                    {
                        low[j] = Math.Min(low[j], row[j]);
                        high[j] = Math.Max(high[j], row[j]);
                    }
                }

                var centres = new double[k][];
                for (int c = 0; c < k; c++)
                {
                    centres[c] = new double[width];
                    for (int j = 0; j < width; j++)
                    {
                        centres[c][j] = low[j] + (random.NextDouble() * (high[j] - low[j]));
                    }
                }

                return centres;
            }

            case StartRule.Sample:
            {
                var chosen = new List<int>(k);
                while (chosen.Count < k)
                {
                    int candidate = random.Next(data.Count);
                    if (!chosen.Contains(candidate))
                    {
                        chosen.Add(candidate);
                    }
                }

                return [.. chosen.Select(i => (double[])data[i].Clone())];
            }

            default:
            {
                // k-means++: after a uniform first choice, every further centre is drawn with
                // probability proportional to how far its point is from the nearest centre so far, which
                // is what stops two centres landing in the same dense region and never separating.
                var centres = new List<double[]> { (double[])data[random.Next(data.Count)].Clone() };
                var nearest = new double[data.Count];
                for (int i = 0; i < data.Count; i++)
                {
                    nearest[i] = measure.Distance(data[i], centres[0]);
                }

                while (centres.Count < k)
                {
                    double total = 0;
                    foreach (double d in nearest)
                    {
                        total += Math.Max(d, 0);
                    }

                    int pick;
                    if (!(total > 0))
                    {
                        pick = random.Next(data.Count);
                    }
                    else
                    {
                        double target = random.NextDouble() * total;
                        pick = data.Count - 1;
                        double running = 0;
                        for (int i = 0; i < data.Count; i++)
                        {
                            running += Math.Max(nearest[i], 0);
                            if (running >= target)
                            {
                                pick = i;
                                break;
                            }
                        }
                    }

                    var added = (double[])data[pick].Clone();
                    centres.Add(added);
                    for (int i = 0; i < data.Count; i++)
                    {
                        nearest[i] = Math.Min(nearest[i], measure.Distance(data[i], added));
                    }
                }

                return [.. centres];
            }
        }
    }

    private static double[][] Recentre(
        IReadOnlyList<double[]> data,
        int[] labels,
        double[][] centres,
        DistanceMeasure measure,
        bool byMean,
        EmptyClusterRule onEmpty,
        int width)
    {
        int k = centres.Length;
        var members = new List<int>[k];
        for (int c = 0; c < k; c++)
        {
            members[c] = [];
        }

        for (int i = 0; i < data.Count; i++)
        {
            members[labels[i]].Add(i);
        }

        var updated = new List<double[]>(k);
        for (int c = 0; c < k; c++)
        {
            if (members[c].Count == 0)
            {
                switch (onEmpty)
                {
                    case EmptyClusterRule.Error:
                        throw new ArgumentException(
                            "A cluster lost every member; ask for fewer clusters or allow empty ones.");

                    case EmptyClusterRule.Drop:
                        continue;

                    default:
                    {
                        // The farthest point from its own centre is the one whose cluster least wants
                        // it, so handing it the empty cluster is the move that lowers the total most.
                        int farthest = 0;
                        double worst = double.NegativeInfinity;
                        for (int i = 0; i < data.Count; i++)
                        {
                            double d = measure.Distance(data[i], centres[labels[i]]);
                            if (d > worst)
                            {
                                worst = d;
                                farthest = i;
                            }
                        }

                        members[labels[farthest]].Remove(farthest);
                        members[c].Add(farthest);
                        labels[farthest] = c;
                        break;
                    }
                }
            }

            updated.Add(byMean
                ? MeanOf(data, members[c], width)
                : (double[])data[Medoid(data, members[c], measure)].Clone());
        }

        // Dropping an empty cluster renumbers everything after it, so the labels are rebuilt from the
        // memberships rather than patched.
        if (updated.Count != k)
        {
            int next = 0;
            for (int c = 0; c < k; c++)
            {
                if (members[c].Count == 0)
                {
                    continue;
                }

                foreach (int i in members[c])
                {
                    labels[i] = next;
                }

                next++;
            }
        }

        return [.. updated];
    }

    private static double[] MeanOf(IReadOnlyList<double[]> data, List<int> members, int width)
    {
        var centre = new double[width];
        foreach (int i in members)
        {
            for (int j = 0; j < width; j++)
            {
                centre[j] += data[i][j];
            }
        }

        for (int j = 0; j < width; j++)
        {
            centre[j] /= members.Count;
        }

        return centre;
    }

    private static int Medoid(IReadOnlyList<double[]> data, List<int> members, DistanceMeasure measure)
    {
        int best = members[0];
        double least = double.PositiveInfinity;
        foreach (int candidate in members)
        {
            double total = 0;
            foreach (int other in members)
            {
                total += measure.Distance(data[candidate], data[other]);
            }

            if (total < least)
            {
                least = total;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Density-based clustering: every point with enough neighbours within <paramref name="epsilon"/>
    /// seeds a cluster, which then grows through its neighbours' neighbours.
    /// </summary>
    /// <returns>
    /// A cluster number per observation, from one, with −1 for a point in no cluster; and which points
    /// were dense enough to grow one.
    /// </returns>
    public static (int[] Labels, bool[] IsCore) Dbscan(
        IReadOnlyList<double[]> data, double epsilon, int minimumPoints, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(measure);
        if (!(epsilon > 0))
        {
            throw new ArgumentException("The neighbourhood radius must be positive.", nameof(epsilon));
        }

        if (minimumPoints < 1)
        {
            throw new ArgumentException("The minimum neighbourhood size must be at least one.", nameof(minimumPoints));
        }

        int n = data.Count;
        var neighbours = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            neighbours[i] = [];
        }

        for (int i = 0; i < n; i++)
        {
            neighbours[i].Add(i);
            for (int j = i + 1; j < n; j++)
            {
                if (measure.Distance(data[i], data[j]) <= epsilon)
                {
                    neighbours[i].Add(j);
                    neighbours[j].Add(i);
                }
            }
        }

        var core = new bool[n];
        for (int i = 0; i < n; i++)
        {
            core[i] = neighbours[i].Count >= minimumPoints;
        }

        var labels = new int[n];
        Array.Fill(labels, -1);
        int cluster = 0;
        for (int i = 0; i < n; i++)
        {
            if (!core[i] || labels[i] >= 0)
            {
                continue;
            }

            cluster++;
            var queue = new Queue<int>();
            queue.Enqueue(i);
            labels[i] = cluster;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();

                // Only a core point spreads the cluster further. A border point joins the first cluster
                // that reaches it and stops there, which is what makes the answer depend on the order
                // the points were given — MathWorks documents the same.
                if (!core[at])
                {
                    continue;
                }

                foreach (int neighbour in neighbours[at])
                {
                    if (labels[neighbour] < 0)
                    {
                        labels[neighbour] = cluster;
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }

        return (labels, core);
    }

    /// <summary>
    /// Spectral clustering: k-means over the leading eigenvectors of the normalized affinity matrix.
    /// </summary>
    /// <param name="data">The observations, one per row.</param>
    /// <param name="k">How many clusters to find.</param>
    /// <param name="scale">The width of the Gaussian kernel that turns a distance into an affinity.</param>
    /// <param name="measure">How to measure the distance between two observations.</param>
    /// <param name="plan">How to run the k-means at the end.</param>
    /// <param name="random">The stream the k-means starts from.</param>
    /// <returns>The labels, and the embedding they were found in.</returns>
    public static (int[] Labels, double[,] Vectors, double[] Values) Spectral(
        IReadOnlyList<double[]> data, int k, double scale, DistanceMeasure measure, Plan plan, Random random)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(measure);
        if (!(scale > 0))
        {
            throw new ArgumentException("The kernel scale must be positive.", nameof(scale));
        }

        int n = data.Count;
        if (n < k)
        {
            throw new ArgumentException("There are fewer observations than clusters asked for.", nameof(k));
        }

        var affinity = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double d = measure.Distance(data[i], data[j]);
                double a = Math.Exp(-(d * d) / (2 * scale * scale));
                affinity[i, j] = a;
                affinity[j, i] = a;
            }
        }

        // The symmetric normalization D^-½·A·D^-½ is what makes a cluster's own density irrelevant:
        // without it a dense region's eigenvector swamps a sparse one and the partition follows the
        // density rather than the gaps.
        var degree = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                degree[i] += affinity[i, j];
            }

            degree[i] = degree[i] > 0 ? 1 / Math.Sqrt(degree[i]) : 0;
        }

        var normalized = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                normalized[i, j] = i == j
                    ? 1 - (degree[i] * affinity[i, j] * degree[j])
                    : -(degree[i] * affinity[i, j] * degree[j]);
            }
        }

        Eigen eigen = Eigen.Factor(normalized);
        int[] order = SmallestFirst(eigen);
        var embedding = new double[n][];
        var values = new double[k];
        for (int i = 0; i < n; i++)
        {
            embedding[i] = new double[k];
        }

        for (int c = 0; c < k; c++)
        {
            values[c] = eigen.Values[order[c]].Real;
            for (int i = 0; i < n; i++)
            {
                embedding[i][c] = eigen.Vectors[i, order[c]].Real;
            }
        }

        // Each row of the embedding is scaled to unit length before the k-means, which is what turns
        // the eigenvector coordinates into directions and is the step the method is named for.
        var vectors = new double[n, k];
        for (int i = 0; i < n; i++)
        {
            double norm = 0;
            foreach (double value in embedding[i])
            {
                norm += value * value;
            }

            norm = Math.Sqrt(norm);
            for (int c = 0; c < k; c++)
            {
                if (norm > 0)
                {
                    embedding[i][c] /= norm;
                }

                vectors[i, c] = embedding[i][c];
            }
        }

        Partition partition = KMeans(embedding, k, plan, random);
        return (partition.Labels, vectors, values);
    }

    /// <summary>
    /// How well each observation sits in its cluster: the gap between its mean distance to the nearest
    /// other cluster and to its own, over whichever of the two is larger.
    /// </summary>
    /// <returns>One value per observation, between −1 and 1.</returns>
    public static double[] Silhouette(IReadOnlyList<double[]> data, int[] labels, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(measure);
        if (labels.Length != data.Count)
        {
            throw new ArgumentException(
                "There must be one cluster number for each observation.", nameof(labels));
        }

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (!groups.TryGetValue(labels[i], out List<int>? members))
            {
                members = [];
                groups[labels[i]] = members;
            }

            members.Add(i);
        }

        var values = new double[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            double own = 0;
            int ownCount = 0;
            double nearest = double.PositiveInfinity;
            foreach ((int label, List<int> members) in groups)
            {
                double total = 0;
                foreach (int j in members)
                {
                    if (j != i)
                    {
                        total += measure.Distance(data[i], data[j]);
                    }
                }

                if (label == labels[i])
                {
                    ownCount = members.Count - 1;
                    own = ownCount > 0 ? total / ownCount : 0;
                }
                else
                {
                    nearest = Math.Min(nearest, total / members.Count);
                }
            }

            // A cluster of one has nothing to be cohesive with. MathWorks scores it zero rather than
            // one, and this follows, because a lone point is not well clustered — it is unclustered.
            values[i] = ownCount == 0 || double.IsPositiveInfinity(nearest)
                ? 0
                : (nearest - own) / Math.Max(own, nearest);
        }

        return values;
    }

    private static int[] SmallestFirst(Eigen eigen)
    {
        var order = new int[eigen.Values.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byValue = eigen.Values[a].Real.CompareTo(eigen.Values[b].Real);
            return byValue != 0 ? byValue : a.CompareTo(b);
        });

        return order;
    }

    private static double[,] Rows(double[][] rows, int width)
    {
        var matrix = new double[rows.Length, width];
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < width; c++)
            {
                matrix[r, c] = rows[r][c];
            }
        }

        return matrix;
    }
}
