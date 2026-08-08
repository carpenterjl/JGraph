namespace JGraph.Statistics.Cluster;

/// <summary>Which distance between two clusters an agglomeration step minimizes.</summary>
public enum LinkageMethod
{
    /// <summary>The closest pair of members, one from each cluster.</summary>
    Single,

    /// <summary>The farthest pair of members.</summary>
    Complete,

    /// <summary>The mean distance over every cross pair.</summary>
    Average,

    /// <summary>The mean distance, each cluster counted once however many members it has.</summary>
    Weighted,

    /// <summary>The distance between the two centroids.</summary>
    Centroid,

    /// <summary>The distance between the two medians, each cluster counted once.</summary>
    Median,

    /// <summary>The increase in the within-cluster sum of squares the merge would cause.</summary>
    Ward,
}

/// <summary>
/// Agglomerative clustering: the tree, the cuts through it, and the two ways of asking how faithful
/// it is.
/// </summary>
/// <remarks>
/// <para>
/// One routine builds every tree, because all seven methods are the same greedy merge under a
/// different update rule — the Lance-Williams recurrence, which says what the distance from a freshly
/// merged cluster to every other one is in terms of the three distances that already existed. Writing
/// them separately would be seven chances to get the same loop wrong.
/// </para>
/// <para>
/// Four of the methods — centroid, median, Ward, and their shared arithmetic — are recurrences in the
/// <em>squared</em> distance, so the tree is built in squared distances throughout and the heights are
/// square-rooted at the end. Feeding one of those methods a distance vector that was not Euclidean is
/// therefore meaningless rather than merely inadvisable, and MathWorks warns about exactly that.
/// </para>
/// </remarks>
public static class Hierarchical
{
    /// <summary>
    /// The tree: one row per merge, holding the two things merged and the height they merged at.
    /// </summary>
    /// <remarks>
    /// The things merged are numbered the way MathWorks numbers them, but from zero: an observation is
    /// its own index, and the cluster formed by merge <c>k</c> is <c>n + k</c>. The script layer adds
    /// the one that turns this into MATLAB's numbering.
    /// </remarks>
    /// <param name="Left">The lower-numbered of the two merged, one per step.</param>
    /// <param name="Right">The higher-numbered of the two merged.</param>
    /// <param name="Height">The distance the merge happened at, ascending.</param>
    public readonly record struct Tree(int[] Left, int[] Right, double[] Height);

    /// <summary>Builds the tree from a condensed distance vector.</summary>
    /// <param name="condensed">Every pair of observations, in <see cref="Distances.Pairwise"/> order.</param>
    /// <param name="method">Which cluster distance to minimize.</param>
    public static Tree Link(IReadOnlyList<double> condensed, LinkageMethod method)
    {
        ArgumentNullException.ThrowIfNull(condensed);
        int n = Distances.SideOf(condensed.Count);
        if (n < 2)
        {
            throw new ArgumentException("A tree needs at least two observations.", nameof(condensed));
        }

        bool squared = IsGeometric(method);
        int slots = (2 * n) - 1;

        // The working table holds a distance for every pair of live clusters, indexed by the numbering
        // above, so a merged cluster simply takes the next free row rather than displacing anyone.
        var distance = new double[slots, slots];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double d = condensed[Position(n, i, j)];
                double stored = squared ? d * d : d;
                distance[i, j] = stored;
                distance[j, i] = stored;
            }
        }

        var live = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            live.Add(i);
        }

        var size = new int[slots];
        for (int i = 0; i < n; i++)
        {
            size[i] = 1;
        }

        var left = new int[n - 1];
        var right = new int[n - 1];
        var height = new double[n - 1];

        for (int step = 0; step < n - 1; step++)
        {
            int bestA = 0;
            int bestB = 1;
            double best = double.PositiveInfinity;
            for (int a = 0; a < live.Count; a++)
            {
                for (int b = a + 1; b < live.Count; b++)
                {
                    double d = distance[live[a], live[b]];
                    if (d < best)
                    {
                        best = d;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            int first = live[bestA];
            int second = live[bestB];
            int merged = n + step;
            left[step] = Math.Min(first, second);
            right[step] = Math.Max(first, second);
            height[step] = squared ? Math.Sqrt(Math.Max(best, 0)) : best;
            size[merged] = size[first] + size[second];

            foreach (int other in live)
            {
                if (other == first || other == second)
                {
                    continue;
                }

                double updated = Combine(
                    method,
                    distance[first, other],
                    distance[second, other],
                    distance[first, second],
                    size[first],
                    size[second],
                    size[other]);
                distance[merged, other] = updated;
                distance[other, merged] = updated;
            }

            live.RemoveAt(bestB);
            live.RemoveAt(bestA);
            live.Add(merged);
        }

        // The greedy merge takes the smallest distance available at each step, which for the centroid
        // and median methods is not necessarily increasing — a merged centroid can sit closer to a
        // third cluster than either parent did. MathWorks leaves those inversions in the tree rather
        // than hiding them, and so does this, because the inversion is the finding.
        return new Tree(left, right, height);
    }

    /// <summary>Which cluster each observation falls in when the tree is cut.</summary>
    /// <param name="tree">The tree.</param>
    /// <param name="count">How many clusters to cut into, or null to cut by height.</param>
    /// <param name="cutoff">The height to cut at, or the inconsistency threshold.</param>
    /// <param name="byInconsistency">Whether <paramref name="cutoff"/> is an inconsistency rather than a height.</param>
    /// <param name="depth">How many levels down the inconsistency looks.</param>
    /// <returns>A cluster number per observation, from one, numbered in the order they first appear.</returns>
    public static int[] Cut(Tree tree, int? count, double cutoff, bool byInconsistency, int depth)
    {
        int merges = tree.Height.Length;
        int n = merges + 1;
        var alive = new bool[merges];

        if (count is { } wanted)
        {
            if (wanted < 1 || wanted > n)
            {
                throw new ArgumentException(
                    "The number of clusters must be between one and the number of observations.", nameof(count));
            }

            // Cutting into k clusters means undoing the last k − 1 merges — but only when the tree's
            // heights increase, which for the centroid and median methods they need not. Ordering the
            // merges by height first is what makes the cut mean "the k tightest groups" either way.
            int[] order = ByHeight(tree);
            for (int i = 0; i < merges; i++)
            {
                alive[i] = true;
            }

            for (int i = 0; i < wanted - 1; i++)
            {
                alive[order[merges - 1 - i]] = false;
            }
        }
        else
        {
            double[] criterion = byInconsistency ? Inconsistent(tree, depth).Ratio : tree.Height;
            for (int i = 0; i < merges; i++)
            {
                alive[i] = criterion[i] <= cutoff;
            }
        }

        // A merge only counts if everything below it counts too: cutting at a height keeps a subtree
        // whole, it does not keep a high merge whose children were cut apart.
        var whole = new bool[merges];
        for (int i = 0; i < merges; i++)
        {
            whole[i] = alive[i] && ChildIsWhole(tree, whole, tree.Left[i], n) && ChildIsWhole(tree, whole, tree.Right[i], n);
        }

        var labels = new int[n];
        Array.Fill(labels, -1);
        int next = 0;

        // Walking the merges from the top down and claiming the largest whole subtree first gives each
        // observation the cluster it belongs to, and numbers the clusters in observation order below.
        for (int i = merges - 1; i >= 0; i--)
        {
            if (!whole[i])
            {
                continue;
            }

            if (Claim(tree, labels, n + i, n, next))
            {
                next++;
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (labels[i] < 0)
            {
                labels[i] = next++;
            }
        }

        return Renumber(labels);
    }

    /// <summary>How far each merge stands out from the merges below it.</summary>
    /// <param name="Mean">The mean height over the merge and the ones within <c>depth</c> of it.</param>
    /// <param name="Deviation">The standard deviation of those heights.</param>
    /// <param name="Count">How many heights went into each.</param>
    /// <param name="Ratio">The merge's own height, standardized by those two.</param>
    public readonly record struct Inconsistency(double[] Mean, double[] Deviation, double[] Count, double[] Ratio);

    /// <summary>The inconsistency of every merge, looking <paramref name="depth"/> levels down.</summary>
    public static Inconsistency Inconsistent(Tree tree, int depth)
    {
        if (depth < 1)
        {
            throw new ArgumentException("The inconsistency depth must be at least one.", nameof(depth));
        }

        int merges = tree.Height.Length;
        int n = merges + 1;
        var mean = new double[merges];
        var deviation = new double[merges];
        var count = new double[merges];
        var ratio = new double[merges];

        for (int i = 0; i < merges; i++)
        {
            var heights = new List<double>();
            Gather(tree, n + i, n, depth, heights);
            count[i] = heights.Count;
            mean[i] = DescriptiveStatistics.Mean(heights);
            deviation[i] = heights.Count > 1
                ? DescriptiveStatistics.StandardDeviation(heights, population: false)
                : 0;

            // A merge with nothing below it, or with nothing below it that varies, has no scale to be
            // measured against; MathWorks reports zero rather than a division by nothing.
            ratio[i] = deviation[i] > 0 ? (tree.Height[i] - mean[i]) / deviation[i] : 0;
        }

        return new Inconsistency(mean, deviation, count, ratio);
    }

    /// <summary>
    /// How well the tree reproduces the distances it was built from — the correlation between the two,
    /// and the height at which each pair of observations first shares a cluster.
    /// </summary>
    public static (double Correlation, double[] Heights) Cophenetic(Tree tree, IReadOnlyList<double> condensed)
    {
        ArgumentNullException.ThrowIfNull(condensed);
        int merges = tree.Height.Length;
        int n = merges + 1;
        if (condensed.Count != n * (n - 1) / 2)
        {
            throw new ArgumentException(
                "The distances and the tree describe different numbers of observations.", nameof(condensed));
        }

        var members = new List<int>[(2 * n) - 1];
        for (int i = 0; i < n; i++)
        {
            members[i] = [i];
        }

        var cophenetic = new double[condensed.Count];
        for (int step = 0; step < merges; step++)
        {
            List<int> a = members[tree.Left[step]];
            List<int> b = members[tree.Right[step]];
            foreach (int i in a)
            {
                foreach (int j in b)
                {
                    cophenetic[Position(n, Math.Min(i, j), Math.Max(i, j))] = tree.Height[step];
                }
            }

            var joined = new List<int>(a.Count + b.Count);
            joined.AddRange(a);
            joined.AddRange(b);
            members[n + step] = joined;
        }

        return (Correlation.Pearson(cophenetic, condensed), cophenetic);
    }

    /// <summary>
    /// The order to draw the leaves in so that adjacent leaves are as close together as the tree allows.
    /// </summary>
    /// <remarks>
    /// This is the Bar-Joseph ordering: at every merge, choose which subtree goes left and which of
    /// their end leaves meet in the middle, so as to minimize the sum of the distances between
    /// neighbours. It is exact rather than greedy, which is the whole point of the name — a greedy
    /// choice at the root cannot see what it costs at the leaves.
    /// </remarks>
    /// <returns>The observation indices, from zero, in drawing order.</returns>
    public static int[] OptimalLeafOrder(Tree tree, IReadOnlyList<double> condensed)
    {
        ArgumentNullException.ThrowIfNull(condensed);
        int merges = tree.Height.Length;
        int n = merges + 1;
        if (condensed.Count != n * (n - 1) / 2)
        {
            throw new ArgumentException(
                "The distances and the tree describe different numbers of observations.", nameof(condensed));
        }

        double Gap(int i, int j) => i == j ? 0 : condensed[Position(n, Math.Min(i, j), Math.Max(i, j))];

        var leaves = new List<int>[(2 * n) - 1];
        for (int i = 0; i < n; i++)
        {
            leaves[i] = [i];
        }

        for (int step = 0; step < merges; step++)
        {
            var joined = new List<int>(leaves[tree.Left[step]]);
            joined.AddRange(leaves[tree.Right[step]]);
            leaves[n + step] = joined;
        }

        // cost[node][(u, v)] is the least total neighbour distance for that subtree laid out with u at
        // its left end and v at its right. A leaf has one layout costing nothing; a merge tries every
        // way of pairing its children's ends.
        var cost = new Dictionary<(int, int), double>[(2 * n) - 1];
        var split = new Dictionary<(int, int), (bool LeftFirst, int InnerLeft, int InnerRight)>[(2 * n) - 1];
        for (int i = 0; i < n; i++)
        {
            cost[i] = new Dictionary<(int, int), double> { [(i, i)] = 0 };
            split[i] = [];
        }

        for (int step = 0; step < merges; step++)
        {
            int node = n + step;
            int a = tree.Left[step];
            int b = tree.Right[step];
            cost[node] = [];
            split[node] = [];

            foreach (bool leftFirst in (bool[])[true, false])
            {
                int outerNode = leftFirst ? a : b;
                int innerNode = leftFirst ? b : a;
                foreach (((int u, int m) outer, double outerCost) in cost[outerNode])
                {
                    foreach (((int mm, int v) inner, double innerCost) in cost[innerNode])
                    {
                        double total = outerCost + innerCost + Gap(outer.m, inner.mm);
                        (int, int) ends = (outer.u, inner.v);
                        if (!cost[node].TryGetValue(ends, out double existing) || total < existing)
                        {
                            cost[node][ends] = total;
                            split[node][ends] = (leftFirst, outer.m, inner.mm);
                        }
                    }
                }
            }
        }

        int root = (2 * n) - 2;
        (int, int) bestEnds = default;
        double bestCost = double.PositiveInfinity;
        foreach (((int, int) ends, double value) in cost[root])
        {
            if (value < bestCost)
            {
                bestCost = value;
                bestEnds = ends;
            }
        }

        var order = new List<int>(n);
        Lay(root, bestEnds);
        return [.. order];

        void Lay(int node, (int Left, int Right) ends)
        {
            if (node < n)
            {
                order.Add(node);
                return;
            }

            int step = node - n;
            (bool leftFirst, int innerLeft, int innerRight) = split[node][ends];
            int outerNode = leftFirst ? tree.Left[step] : tree.Right[step];
            int innerNode = leftFirst ? tree.Right[step] : tree.Left[step];
            Lay(outerNode, (ends.Left, innerLeft));
            Lay(innerNode, (innerRight, ends.Right));
        }
    }

    /// <summary>Where the pair (i, j) sits in a condensed distance vector over n observations.</summary>
    public static int Position(int n, int i, int j) => (i * ((2 * n) - i - 3) / 2) + j - 1;

    private static bool IsGeometric(LinkageMethod method) =>
        method is LinkageMethod.Centroid or LinkageMethod.Median or LinkageMethod.Ward;

    /// <summary>
    /// The Lance-Williams update: the distance from the cluster just merged to some third one.
    /// </summary>
    private static double Combine(
        LinkageMethod method, double toFirst, double toSecond, double between, int first, int second, int other) =>
        method switch
        {
            LinkageMethod.Single => Math.Min(toFirst, toSecond),
            LinkageMethod.Complete => Math.Max(toFirst, toSecond),
            LinkageMethod.Average => ((first * toFirst) + (second * toSecond)) / (first + second),
            LinkageMethod.Weighted => (toFirst + toSecond) / 2,
            LinkageMethod.Centroid =>
                ((first * toFirst) + (second * toSecond)) / (first + second)
                - (first * (double)second * between / ((first + second) * (double)(first + second))),
            LinkageMethod.Median => ((toFirst + toSecond) / 2) - (between / 4),
            LinkageMethod.Ward =>
                (((first + other) * toFirst) + ((second + other) * toSecond) - (other * between))
                / (first + second + other),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

    private static int[] ByHeight(Tree tree)
    {
        var order = new int[tree.Height.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byHeight = tree.Height[a].CompareTo(tree.Height[b]);
            return byHeight != 0 ? byHeight : a.CompareTo(b);
        });

        return order;
    }

    private static bool ChildIsWhole(Tree tree, bool[] whole, int child, int n) => child < n || whole[child - n];

    private static bool Claim(Tree tree, int[] labels, int node, int n, int label)
    {
        if (node < n)
        {
            if (labels[node] >= 0)
            {
                return false;
            }

            labels[node] = label;
            return true;
        }

        int step = node - n;
        bool claimedLeft = Claim(tree, labels, tree.Left[step], n, label);
        bool claimedRight = Claim(tree, labels, tree.Right[step], n, label);
        return claimedLeft || claimedRight;
    }

    private static void Gather(Tree tree, int node, int n, int depth, List<double> heights)
    {
        if (node < n || depth < 1)
        {
            return;
        }

        int step = node - n;
        heights.Add(tree.Height[step]);
        Gather(tree, tree.Left[step], n, depth - 1, heights);
        Gather(tree, tree.Right[step], n, depth - 1, heights);
    }

    private static int[] Renumber(int[] labels)
    {
        // The clusters were numbered as they were claimed, top down; MathWorks numbers them by the
        // first observation that falls in each, so a relabelling pass over the observations in order
        // is the last thing that happens.
        var mapping = new Dictionary<int, int>();
        var final = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            if (!mapping.TryGetValue(labels[i], out int number))
            {
                number = mapping.Count + 1;
                mapping[labels[i]] = number;
            }

            final[i] = number;
        }

        return final;
    }
}
