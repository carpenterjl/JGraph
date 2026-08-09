using JGraph.Statistics.Cluster;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave H, part one: how far apart observations are, and the groups that follow from it — the
/// pairwise distances and the two neighbourhood searches, the agglomerative tree with its cuts and
/// its diagnostics, and the four partitional methods with the silhouette that judges any of them.
/// </summary>
/// <remarks>
/// <para>
/// Everything here reads one observation per row, the same as wave E, and everything that measures a
/// distance takes the same twelve metric words with the same three extra arguments — the Minkowski
/// exponent, the standardizing scale, the Mahalanobis covariance — which is why the metric is parsed
/// once, in <see cref="ClusterMetric"/>, rather than in each name that accepts one.
/// </para>
/// <para>
/// A set of pairwise distances travels between these names as MathWorks' condensed row vector, and
/// the ones that accept either form — <c>linkage</c>, <c>cluster</c>, <c>silhouette</c> — tell the two
/// apart by shape rather than by a word, which is what lets <c>linkage(pdist(X))</c> and
/// <c>linkage(X)</c> both mean what they read like.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec NearestNeighbourOptions = new(
        "knnsearch", [], ["k", "distance", "p", "scale", "cov", "includeties", "sortindices", "nsmethod", "bucketsize"]);

    private static readonly OptionSpec RadiusSearchOptions = new(
        "rangesearch", [], ["distance", "p", "scale", "cov", "sortindices", "nsmethod", "bucketsize"]);

    private static readonly OptionSpec KMeansOptions = new(
        "kmeans", [], ["distance", "start", "replicates", "maxiter", "emptyaction", "options", "onlinephase", "display"]);

    private static readonly OptionSpec KMedoidsOptions = new(
        "kmedoids", [], ["distance", "start", "replicates", "maxiter", "algorithm", "options", "onlinephase", "percentneighbors", "p", "scale", "cov"]);

    private static readonly OptionSpec DbscanOptions = new(
        "dbscan", [], ["distance", "p", "scale", "cov"], StringPositionals: 3);

    private static readonly OptionSpec SpectralOptions = new(
        "spectralcluster",
        [],
        ["distance", "p", "scale", "cov", "kernelscale", "laplaciannormalization", "similaritygraph", "numneighbors", "replicates", "clustermethod"]);

    private static readonly OptionSpec ClusterDataOptions = new(
        "clusterdata",
        [],
        ["criterion", "cutoff", "depth", "distance", "linkage", "maxclust", "savememory", "p"]);

    /// <summary>Registers the distance, hierarchical and partitional clustering builtins.</summary>
    private static void RegisterClusterBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        void DefineSeeded(string name, Func<Random, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            DefineBoth(name, (args, wanted, line, col) => both(random, args, wanted, line, col));

        Define("pdist", PairwiseDistances);
        Define("pdist2", CrossDistances);
        Define("squareform", SquareForm);
        Define("mahal", MahalanobisDistances);
        DefineBoth("knnsearch", NearestNeighbours);
        DefineBoth("rangesearch", NeighboursWithin);

        Define("linkage", BuildTree);
        Define("cluster", CutTree);
        Define("clusterdata", ClusterFromData);
        DefineBoth("cophenet", CopheneticCorrelation);
        Define("inconsistent", TreeInconsistency);
        Define("optimalleaforder", LeafOrder);

        DefineSeeded("kmeans", MeansPartition);
        DefineSeeded("kmedoids", MedoidsPartition);
        DefineBoth("dbscan", DensityPartition);
        DefineSeeded("spectralcluster", SpectralPartition);
        DefineBoth("silhouette", SilhouetteValues);
    }

    // --- pdist, pdist2, squareform, mahal -------------------------------------------------------------

    /// <summary><c>D = pdist(X, metric, …)</c>: the distance between every pair of rows.</summary>
    private static JgsValue PairwiseDistances(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("pdist", args, 1, 4, line, col);
        (double[][] rows, _) = Observations("pdist", args[0], line, col);
        DistanceMeasure measure = ClusterMetric("pdist", args, 1, rows, line, col);
        double[] condensed = Guarded("pdist", () => Distances.Pairwise(rows, measure), line, col);
        return RowVector(condensed);
    }

    /// <summary><c>D = pdist2(X, Y, metric, …)</c>: every row of one set against every row of another.</summary>
    private static JgsValue CrossDistances(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("pdist2", args, 2, 5, line, col);
        (double[][] left, int width) = Observations("pdist2", args[0], line, col);
        (double[][] right, int otherWidth) = Observations("pdist2", args[1], line, col);
        if (width != otherWidth)
        {
            throw new JgsRuntimeException(line, col,
                "pdist2: both sets must describe the same variables, so they need the same number of columns.");
        }

        // The scale and the covariance a metric needs come from both sets together, because the
        // question "how far apart are these two clouds" has no answer in a metric defined by one of
        // them alone.
        var stacked = new List<double[]>(left.Length + right.Length);
        stacked.AddRange(left);
        stacked.AddRange(right);
        DistanceMeasure measure = ClusterMetric("pdist2", args, 2, stacked, line, col);
        return Rectangle(Guarded("pdist2", () => Distances.Between(left, right, measure), line, col));
    }

    /// <summary><c>squareform(D)</c>: the condensed distances as a matrix, or a matrix condensed.</summary>
    private static JgsValue SquareForm(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("squareform", args, 1, 2, line, col);
        (double[] flat, int rows, int columns) = DenseMatrix("squareform", args[0], line, col);
        string forced = args.Count > 1
            ? ClusterWord("squareform", args[1], line, col).ToLowerInvariant()
            : string.Empty;

        if (forced.Length > 0 && forced is not ("tovector" or "tomatrix"))
        {
            throw new JgsRuntimeException(line, col,
                "squareform: the second argument is 'tovector' or 'tomatrix'.");
        }

        // A one-by-one input is ambiguous — it is both a square matrix and a vector of one distance —
        // so MathWorks reads it as the vector, giving a two-by-two matrix of zeros off the diagonal.
        bool square = rows == columns && rows > 1;
        bool toVector = forced == "tovector" || (forced.Length == 0 && square);
        if (toVector)
        {
            if (rows != columns)
            {
                throw new JgsRuntimeException(line, col,
                    "squareform: condensing needs a square matrix.");
            }

            var matrix = new double[rows, columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    matrix[r, c] = flat[r + (c * rows)];
                }
            }

            return RowVector(Distances.CondensedForm(matrix));
        }

        double[] condensed = flat;
        return Rectangle(Guarded("squareform", () => Distances.SquareForm(condensed), line, col));
    }

    /// <summary><c>d = mahal(Y, X)</c>: the squared Mahalanobis distance in <c>X</c>'s own metric.</summary>
    private static JgsValue MahalanobisDistances(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("mahal", args, 2, line, col);
        (double[][] points, int width) = Observations("mahal", args[0], line, col);
        (double[][] reference, int referenceWidth) = Observations("mahal", args[1], line, col);
        if (width != referenceWidth)
        {
            throw new JgsRuntimeException(line, col,
                "mahal: the points and the reference sample must describe the same variables.");
        }

        return ColumnOfAnswers(Guarded("mahal", () => Distances.Mahalanobis(points, reference), line, col));
    }

    // --- knnsearch, rangesearch -----------------------------------------------------------------------

    /// <summary><c>[idx, d] = knnsearch(X, Y, …)</c>: the nearest members of X to each row of Y.</summary>
    private static JgsValue[] NearestNeighbours(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = NearestNeighbourOptions.Parse(ExpandSearcher(args), 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "knnsearch takes the data and the query points.");
        }

        (double[][] data, int width) = Observations("knnsearch", parsed.Positional[0], line, col);
        (double[][] queries, int queryWidth) = Observations("knnsearch", parsed.Positional[1], line, col);
        if (width != queryWidth)
        {
            throw new JgsRuntimeException(line, col,
                "knnsearch: the queries must describe the same variables as the data.");
        }

        RefuseSearcherOptions("knnsearch", parsed, line, col);
        DistanceMeasure measure = NamedMetric("knnsearch", parsed, data, line, col);
        int k = parsed.Whole("k", 1);
        bool ties = parsed.Flag("includeties", false);
        if (ties)
        {
            throw new JgsRuntimeException(line, col,
                "knnsearch: 'IncludeTies' returns a cell of variable-length neighbourhoods and is not supported; "
                + "use rangesearch, which returns exactly that.");
        }

        (int[][] indices, double[][] distances) =
            Guarded("knnsearch", () => Distances.Nearest(data, queries, k, measure), line, col);

        // The answers come back one row per query and k columns wide, which needs every row to be the
        // same length — it is, because a fixed k is the whole difference between this and rangesearch.
        int held = indices.Length > 0 ? indices[0].Length : 0;
        var indexMatrix = new double[indices.Length, held];
        var distanceMatrix = new double[indices.Length, held];
        for (int r = 0; r < indices.Length; r++)
        {
            for (int c = 0; c < held; c++)
            {
                indexMatrix[r, c] = indices[r][c] + 1;
                distanceMatrix[r, c] = distances[r][c];
            }
        }

        return Outputs(wanted, Rectangle(indexMatrix), Rectangle(distanceMatrix));
    }

    /// <summary><c>[idx, d] = rangesearch(X, Y, r, …)</c>: every member of X within r of each query.</summary>
    private static JgsValue[] NeighboursWithin(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RadiusSearchOptions.Parse(ExpandSearcher(args), 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "rangesearch takes the data, the query points and the radius.");
        }

        (double[][] data, int width) = Observations("rangesearch", parsed.Positional[0], line, col);
        (double[][] queries, int queryWidth) = Observations("rangesearch", parsed.Positional[1], line, col);
        if (width != queryWidth)
        {
            throw new JgsRuntimeException(line, col,
                "rangesearch: the queries must describe the same variables as the data.");
        }

        RefuseSearcherOptions("rangesearch", parsed, line, col);
        double radius = ClusterNumber("rangesearch", parsed.Positional[2], line, col);
        DistanceMeasure measure = NamedMetric("rangesearch", parsed, data, line, col);
        (int[][] indices, double[][] distances) =
            Guarded("rangesearch", () => Distances.Within(data, queries, radius, measure), line, col);

        var indexCells = new JgsValue[indices.Length];
        var distanceCells = new JgsValue[indices.Length];
        for (int r = 0; r < indices.Length; r++)
        {
            var found = new double[indices[r].Length];
            for (int c = 0; c < found.Length; c++)
            {
                found[c] = indices[r][c] + 1;
            }

            indexCells[r] = RowVector(found);
            distanceCells[r] = RowVector(distances[r]);
        }

        JgsValue indexCell = JgsValue.Cell(indexCells);
        indexCell.ReshapeDims([indices.Length, 1]);
        JgsValue distanceCell = JgsValue.Cell(distanceCells);
        distanceCell.ReshapeDims([indices.Length, 1]);
        return Outputs(wanted, indexCell, distanceCell);
    }

    // --- linkage, cluster, clusterdata, cophenet, inconsistent, optimalleaforder ----------------------

    /// <summary><c>Z = linkage(X, method, metric)</c>: the agglomerative tree.</summary>
    private static JgsValue BuildTree(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("linkage", args, 1, 4, line, col);
        (double[] condensed, _) = DistancesArgument("linkage", args[0], line, col, args, 2);
        LinkageMethod method = args.Count > 1
            ? LinkageWord("linkage", ClusterWord("linkage", args[1], line, col), line, col)
            : LinkageMethod.Single;

        Hierarchical.Tree tree = Guarded("linkage", () => Hierarchical.Link(condensed, method), line, col);
        return TreeMatrix(tree);
    }

    /// <summary><c>T = cluster(Z, 'cutoff', c)</c> and <c>cluster(Z, 'maxclust', n)</c>.</summary>
    private static JgsValue CutTree(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cluster", args, 3, 7, line, col);
        Hierarchical.Tree tree = TreeArgument("cluster", args[0], line, col);
        var options = new OptionSpec("cluster", [], ["cutoff", "maxclust", "criterion", "depth"]);
        ParsedArgs parsed = options.Parse(args, 1, line, col);

        int? count = parsed.Named("maxclust") is { } maxClust
            ? (int)Math.Round(ClusterNumber("cluster", maxClust, line, col))
            : null;
        double cutoff = parsed.Scalar("cutoff", double.NaN);
        if (count is null && double.IsNaN(cutoff))
        {
            throw new JgsRuntimeException(line, col,
                "cluster: give either 'cutoff' or 'maxclust'.");
        }

        string criterion = parsed.Word("criterion", "inconsistent", "inconsistent", "distance");
        int depth = parsed.Whole("depth", 2);
        int[] labels = Guarded("cluster",
            () => Hierarchical.Cut(tree, count, cutoff, criterion == "inconsistent", depth), line, col);
        return ColumnOfWholes(labels);
    }

    /// <summary><c>T = clusterdata(X, cutoff)</c>: linkage and cut in one step.</summary>
    private static JgsValue ClusterFromData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = ClusterDataOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "clusterdata takes the data and how to cut it.");
        }

        (double[][] rows, _) = Observations("clusterdata", parsed.Positional[0], line, col);
        DistanceMeasure measure = NamedMetric("clusterdata", parsed, rows, line, col);
        double[] condensed = Guarded("clusterdata", () => Distances.Pairwise(rows, measure), line, col);

        LinkageMethod method = LinkageWord(
            "clusterdata", parsed.Text("linkage") ?? "single", line, col);
        Hierarchical.Tree tree = Guarded("clusterdata", () => Hierarchical.Link(condensed, method), line, col);

        // The short form clusterdata(X, c) means "cutoff" when c is not a whole number and "maxclust"
        // when it is — MathWorks' own rule, and the reason the second argument cannot simply be read
        // as one or the other.
        int? count = parsed.Named("maxclust") is { } maxClust
            ? (int)Math.Round(ClusterNumber("clusterdata", maxClust, line, col))
            : null;
        double cutoff = parsed.Scalar("cutoff", double.NaN);
        if (count is null && double.IsNaN(cutoff) && parsed.Positional.Count > 1)
        {
            double given = ClusterNumber("clusterdata", parsed.Positional[1], line, col);
            if (given == Math.Floor(given) && given >= 1)
            {
                count = (int)given;
            }
            else
            {
                cutoff = given;
            }
        }

        if (count is null && double.IsNaN(cutoff))
        {
            throw new JgsRuntimeException(line, col,
                "clusterdata: give a cutoff, or 'maxclust' and how many clusters to end with.");
        }

        string criterion = parsed.Word("criterion", "inconsistent", "inconsistent", "distance");
        int depth = parsed.Whole("depth", 2);
        int[] labels = Guarded("clusterdata",
            () => Hierarchical.Cut(tree, count, cutoff, criterion == "inconsistent", depth), line, col);
        return ColumnOfWholes(labels);
    }

    /// <summary><c>[c, d] = cophenet(Z, Y)</c>: how faithfully the tree reproduces the distances.</summary>
    private static JgsValue[] CopheneticCorrelation(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("cophenet", args, 2, line, col);
        Hierarchical.Tree tree = TreeArgument("cophenet", args[0], line, col);
        (double[] condensed, _) = DistancesArgument("cophenet", args[1], line, col, args, -1);
        (double correlation, double[] heights) =
            Guarded("cophenet", () => Hierarchical.Cophenetic(tree, condensed), line, col);
        return Outputs(wanted, JgsValue.Number(correlation), RowVector(heights));
    }

    /// <summary><c>Y = inconsistent(Z, d)</c>: each merge's height against the merges below it.</summary>
    private static JgsValue TreeInconsistency(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("inconsistent", args, 1, 2, line, col);
        Hierarchical.Tree tree = TreeArgument("inconsistent", args[0], line, col);
        int depth = args.Count > 1 ? (int)Math.Round(ClusterNumber("inconsistent", args[1], line, col)) : 2;
        Hierarchical.Inconsistency measured =
            Guarded("inconsistent", () => Hierarchical.Inconsistent(tree, depth), line, col);

        int merges = measured.Mean.Length;
        var matrix = new double[merges, 4];
        for (int i = 0; i < merges; i++)
        {
            matrix[i, 0] = measured.Mean[i];
            matrix[i, 1] = measured.Deviation[i];
            matrix[i, 2] = measured.Count[i];
            matrix[i, 3] = measured.Ratio[i];
        }

        return Rectangle(matrix);
    }

    /// <summary><c>order = optimalleaforder(Z, D)</c>: the leaf order that keeps neighbours close.</summary>
    private static JgsValue LeafOrder(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("optimalleaforder", args, 2, 4, line, col);
        Hierarchical.Tree tree = TreeArgument("optimalleaforder", args[0], line, col);
        (double[] condensed, _) = DistancesArgument("optimalleaforder", args[1], line, col, args, -1);

        if (args.Count > 2)
        {
            string criterion = ClusterWord("optimalleaforder", args[2], line, col).ToLowerInvariant();
            if (criterion == "criteria" && args.Count > 3)
            {
                criterion = ClusterWord("optimalleaforder", args[3], line, col).ToLowerInvariant();
            }

            if (criterion is not ("adjacent" or "criteria"))
            {
                throw new JgsRuntimeException(line, col,
                    "optimalleaforder: the criterion is 'adjacent'; 'group' minimizes a different sum and is not "
                    + "supported.");
            }
        }

        int[] order = Guarded("optimalleaforder",
            () => Hierarchical.OptimalLeafOrder(tree, condensed), line, col);
        var oneBased = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            oneBased[i] = order[i] + 1;
        }

        return RowVector(oneBased);
    }

    // --- kmeans, kmedoids, dbscan, spectralcluster, silhouette ---------------------------------------

    /// <summary><c>[idx, C, sumd, D] = kmeans(X, k, …)</c>.</summary>
    private static JgsValue[] MeansPartition(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = KMeansOptions.Parse(args, 2, line, col);
        (double[][] rows, int width) = PartitionData("kmeans", parsed, line, col);
        int k = PartitionCount("kmeans", parsed, line, col);

        string metric = parsed.Word("distance", "sqeuclidean", "sqeuclidean", "cityblock", "cosine", "correlation", "hamming");
        if (metric != "sqeuclidean")
        {
            throw new JgsRuntimeException(line, col,
                "kmeans: only the squared Euclidean distance is supported, because the other distances need a "
                + "different centre — the median for cityblock, the normalized mean for cosine — and a mean that "
                + "does not match the distance would report a total nothing minimizes. Use kmedoids, which takes "
                + "every metric.");
        }

        Partitional.Plan plan = PartitionPlan("kmeans", parsed, rows, k, width, line, col);
        Partitional.Partition partition = Guarded("kmeans",
            () => Partitional.KMeans(rows, k, plan, random), line, col);
        return PartitionOutputs(wanted, partition);
    }

    /// <summary><c>[idx, C, sumd, D, midx, info] = kmedoids(X, k, …)</c>.</summary>
    private static JgsValue[] MedoidsPartition(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = KMedoidsOptions.Parse(args, 2, line, col);
        (double[][] rows, int width) = PartitionData("kmedoids", parsed, line, col);
        int k = PartitionCount("kmedoids", parsed, line, col);
        DistanceMeasure measure = NamedMetric("kmedoids", parsed, rows, line, col);
        Partitional.Plan plan = PartitionPlan("kmedoids", parsed, rows, k, width, line, col);

        Partitional.Partition partition = Guarded("kmedoids",
            () => Partitional.KMedoids(rows, k, plan, random, measure), line, col);

        // The medoid is one of the observations, so unlike k-means there is an index to report — found
        // by matching each centre back to the row it was copied from.
        var medoids = new double[partition.Centres.GetLength(0)];
        for (int c = 0; c < medoids.Length; c++)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                bool same = true;
                for (int j = 0; j < width && same; j++)
                {
                    same = rows[r][j] == partition.Centres[c, j];
                }

                if (same)
                {
                    medoids[c] = r + 1;
                    break;
                }
            }
        }

        JgsValue info = Structure(
            ("algorithm", JgsValue.Str("pam")),
            ("start", JgsValue.Str(parsed.Text("start")?.ToLowerInvariant() ?? "plus")),
            ("distance", JgsValue.Str(parsed.Text("distance")?.ToLowerInvariant() ?? "sqeuclidean")),
            ("iterations", JgsValue.Number(partition.Iterations)),
            ("converged", JgsValue.Bool(partition.Converged)));

        JgsValue[] first = PartitionOutputs(Math.Min(wanted, 4), partition);
        return Outputs(wanted, [.. first, ColumnOfAnswers(medoids), info]);
    }

    /// <summary><c>[idx, corepts] = dbscan(X, epsilon, minpts, …)</c>.</summary>
    private static JgsValue[] DensityPartition(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = DbscanOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "dbscan takes the data, the neighbourhood radius and the smallest neighbourhood that counts.");
        }

        double epsilon = ClusterNumber("dbscan", parsed.Positional[1], line, col);
        int minimum = (int)Math.Round(ClusterNumber("dbscan", parsed.Positional[2], line, col));

        // The first argument is either the observations or a full distance matrix; MathWorks tells them
        // apart by the metric word 'precomputed', which is the only reading under which a square matrix
        // of distances is not simply n observations of n variables.
        bool precomputed = string.Equals(parsed.Text("distance"), "precomputed", StringComparison.OrdinalIgnoreCase);
        double[][] rows;
        DistanceMeasure measure;
        if (precomputed)
        {
            (double[] flat, int n, int columns) = DenseMatrix("dbscan", parsed.Positional[0], line, col);
            if (n != columns)
            {
                throw new JgsRuntimeException(line, col,
                    "dbscan: a precomputed distance matrix must be square.");
            }

            rows = new double[n][];
            for (int r = 0; r < n; r++)
            {
                rows[r] = new double[n];
                for (int c = 0; c < n; c++)
                {
                    rows[r][c] = flat[r + (c * n)];
                }
            }

            measure = DistanceMeasure.Precomputed(rows);
        }
        else
        {
            (rows, _) = Observations("dbscan", parsed.Positional[0], line, col);
            measure = NamedMetric("dbscan", parsed, rows, line, col);
        }

        (int[] labels, bool[] core) = Guarded("dbscan",
            () => Partitional.Dbscan(rows, epsilon, minimum, measure), line, col);
        return Outputs(wanted, ColumnOfWholes(labels), ColumnOfFlags(core));
    }

    /// <summary><c>[idx, V, D] = spectralcluster(X, k, …)</c>.</summary>
    private static JgsValue[] SpectralPartition(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = SpectralOptions.Parse(args, 2, line, col);
        (double[][] rows, int width) = PartitionData("spectralcluster", parsed, line, col);
        int k = PartitionCount("spectralcluster", parsed, line, col);
        DistanceMeasure measure = NamedMetric("spectralcluster", parsed, rows, line, col);

        string normalization = parsed.Word("laplaciannormalization", "symmetric", "symmetric", "randomwalk", "none");
        if (normalization != "symmetric")
        {
            throw new JgsRuntimeException(line, col,
                "spectralcluster: only the symmetric Laplacian normalization is supported.");
        }

        string graph = parsed.Word("similaritygraph", "epsilon-neighborhood", "epsilon-neighborhood", "knn");
        if (graph == "knn")
        {
            throw new JgsRuntimeException(line, col,
                "spectralcluster: the nearest-neighbour similarity graph is not supported; the Gaussian kernel "
                + "graph is, and its width is 'KernelScale'.");
        }

        double scale = parsed.Scalar("kernelscale", double.NaN);
        if (double.IsNaN(scale))
        {
            // MathWorks' automatic width is the median pairwise distance, which puts a typical pair at
            // an affinity of about 0.6 — close enough to matter, far enough not to connect everything.
            double[] condensed = Distances.Pairwise(rows, measure);
            scale = condensed.Length > 0
                ? JGraph.Statistics.DescriptiveStatistics.Median(condensed)
                : 1;
            if (!(scale > 0))
            {
                scale = 1;
            }
        }

        Partitional.Plan plan = PartitionPlan("spectralcluster", parsed, rows, k, width, line, col);
        (int[] labels, double[,] vectors, double[] values) = Guarded("spectralcluster",
            () => Partitional.Spectral(rows, k, scale, measure, plan, random), line, col);
        return Outputs(wanted, ColumnOfWholes(labels), Rectangle(vectors), ColumnOfAnswers(values));
    }

    /// <summary><c>s = silhouette(X, clust, metric)</c>: how well each observation fits its cluster.</summary>
    private static JgsValue[] SilhouetteValues(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("silhouette", args, 2, 4, line, col);
        (double[][] rows, _) = Observations("silhouette", args[0], line, col);
        (int[] labels, string[] names) = GroupIndex("silhouette", args[1], line, col);
        if (labels.Length != rows.Length)
        {
            throw new JgsRuntimeException(line, col,
                "silhouette: there must be one cluster number for each observation.");
        }

        DistanceMeasure measure = ClusterMetric("silhouette", args, 2, rows, line, col);
        double[] values = Guarded("silhouette", () => Partitional.Silhouette(rows, labels, measure), line, col);

        // MathWorks draws the silhouette when nothing catches the answer. There is no plot verb in this
        // wave, so the values are the answer either way, and the console echoes them — the same choice
        // tabulate made in wave B.
        var cells = new JgsValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            cells[i] = JgsValue.Str(names[i]);
        }

        JgsValue nameCell = JgsValue.Cell(cells);
        nameCell.ReshapeDims([names.Length, 1]);
        return Outputs(wanted, ColumnOfAnswers(values), nameCell);
    }

    // --- shared helpers -------------------------------------------------------------------------------

    /// <summary>Reads a metric from a positional slot, with its exponent, scale or covariance after it.</summary>
    private static DistanceMeasure ClusterMetric(
        string name, IReadOnlyList<JgsValue> args, int slot, IReadOnlyList<double[]> data, int line, int col)
    {
        if (args.Count <= slot)
        {
            return Guarded(name, () => DistanceMeasure.Create(DistanceMetric.Euclidean, data), line, col);
        }

        if (args[slot].Type == JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a distance written as a function handle is not supported; the twelve documented metric "
                + "names are.");
        }

        DistanceMetric metric = MetricWord(name, ClusterWord(name, args[slot], line, col), line, col);
        double? exponent = null;
        double[]? scale = null;
        double[,]? covariance = null;

        if (args.Count > slot + 1)
        {
            switch (metric)
            {
                case DistanceMetric.Minkowski:
                    exponent = ClusterNumber(name, args[slot + 1], line, col);
                    break;

                case DistanceMetric.StandardizedEuclidean:
                    scale = FlattenColumnMajor(name, args[slot + 1], line, col);
                    break;

                case DistanceMetric.Mahalanobis:
                {
                    (double[] flat, int order) = SquareMatrix(name, args[slot + 1], line, col);
                    covariance = Square(flat, order);
                    break;
                }

                default:
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the '{ClusterWord(name, args[slot], line, col)}' distance takes no further argument.");
            }
        }

        return Guarded(name, () => DistanceMeasure.Create(metric, data, exponent, scale, covariance), line, col);
    }

    /// <summary>Reads a metric from the named options the searchers and the partitioners take.</summary>
    private static DistanceMeasure NamedMetric(
        string name, ParsedArgs parsed, IReadOnlyList<double[]> data, int line, int col)
    {
        string word = parsed.Text("distance") ?? "euclidean";
        if (string.Equals(word, "sqeuclidean", StringComparison.OrdinalIgnoreCase))
        {
            word = "squaredeuclidean";
        }

        DistanceMetric metric = MetricWord(name, word, line, col);
        double? exponent = parsed.Named("p") is { } p ? ClusterNumber(name, p, line, col) : null;
        double[]? scale = parsed.Vector("scale");
        double[,]? covariance = null;
        if (parsed.Named("cov") is { } given)
        {
            (double[] flat, int order) = SquareMatrix(name, given, line, col);
            covariance = Square(flat, order);
        }

        return Guarded(name, () => DistanceMeasure.Create(metric, data, exponent, scale, covariance), line, col);
    }

    /// <summary>The twelve metric names, refusing a misspelling by listing them.</summary>
    private static DistanceMetric MetricWord(string name, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "euclidean" => DistanceMetric.Euclidean,
            "squaredeuclidean" or "sqeuclidean" => DistanceMetric.SquaredEuclidean,
            "seuclidean" => DistanceMetric.StandardizedEuclidean,
            "mahalanobis" => DistanceMetric.Mahalanobis,
            "cityblock" => DistanceMetric.CityBlock,
            "minkowski" => DistanceMetric.Minkowski,
            "chebychev" or "chebyshev" => DistanceMetric.Chebychev,
            "cosine" => DistanceMetric.Cosine,
            "correlation" => DistanceMetric.Correlation,
            "spearman" => DistanceMetric.Spearman,
            "hamming" => DistanceMetric.Hamming,
            "jaccard" => DistanceMetric.Jaccard,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{word}' is not a distance. The metrics are euclidean, squaredeuclidean, seuclidean, "
                + "mahalanobis, cityblock, minkowski, chebychev, cosine, correlation, spearman, hamming and jaccard."),
        };

    /// <summary>The seven linkage methods, refusing a misspelling by listing them.</summary>
    private static LinkageMethod LinkageWord(string name, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "single" or "nearest" => LinkageMethod.Single,
            "complete" or "farthest" => LinkageMethod.Complete,
            "average" => LinkageMethod.Average,
            "weighted" => LinkageMethod.Weighted,
            "centroid" => LinkageMethod.Centroid,
            "median" => LinkageMethod.Median,
            "ward" => LinkageMethod.Ward,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{word}' is not a linkage method. They are single, complete, average, weighted, "
                + "centroid, median and ward."),
        };

    /// <summary>
    /// An argument that is either a condensed distance vector or the observations themselves.
    /// </summary>
    private static (double[] Condensed, int Count) DistancesArgument(
        string name, JgsValue value, int line, int col, IReadOnlyList<JgsValue> args, int metricSlot)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);

        // A row or a column of numbers is a condensed distance vector when its length is one that a
        // whole number of observations could produce, and observations of one variable otherwise. The
        // test is exact, so a length like five — which no set of observations gives — is the error it
        // should be rather than a silent reinterpretation.
        if (rows == 1 || columns == 1)
        {
            int length = flat.Length;
            int side = (int)Math.Round((1 + Math.Sqrt(1 + (8.0 * length))) / 2);
            if (side * (side - 1) / 2 == length && length > 0)
            {
                return (flat, side);
            }
        }

        var observations = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            observations[r] = new double[columns];
            for (int c = 0; c < columns; c++)
            {
                observations[r][c] = flat[r + (c * rows)];
            }
        }

        DistanceMeasure measure = metricSlot >= 0
            ? ClusterMetric(name, args, metricSlot, observations, line, col)
            : Guarded(name, () => DistanceMeasure.Create(DistanceMetric.Euclidean, observations), line, col);
        return (Guarded(name, () => Distances.Pairwise(observations, measure), line, col), rows);
    }

    /// <summary>A linkage matrix read back into a tree.</summary>
    private static Hierarchical.Tree TreeArgument(string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);
        if (columns != 3 || rows < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a linkage matrix has three columns — the two things merged and the height.");
        }

        var left = new int[rows];
        var right = new int[rows];
        var height = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            left[r] = (int)Math.Round(flat[r]) - 1;
            right[r] = (int)Math.Round(flat[r + rows]) - 1;
            height[r] = flat[r + (2 * rows)];
            if (left[r] < 0 || right[r] < 0 || left[r] >= rows + 1 + r || right[r] >= rows + 1 + r)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a merge in the linkage matrix names something that does not exist yet.");
            }
        }

        return new Hierarchical.Tree(left, right, height);
    }

    /// <summary>A tree written out as MATLAB's three-column linkage matrix, numbered from one.</summary>
    private static JgsValue TreeMatrix(Hierarchical.Tree tree)
    {
        int merges = tree.Height.Length;
        var matrix = new double[merges, 3];
        for (int r = 0; r < merges; r++)
        {
            matrix[r, 0] = tree.Left[r] + 1;
            matrix[r, 1] = tree.Right[r] + 1;
            matrix[r, 2] = tree.Height[r];
        }

        return Rectangle(matrix);
    }

    private static (double[][] Rows, int Width) PartitionData(string name, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} takes the data and how many clusters to find.");
        }

        return Observations(name, parsed.Positional[0], line, col);
    }

    private static int PartitionCount(string name, ParsedArgs parsed, int line, int col)
    {
        double k = ClusterNumber(name, parsed.Positional[1], line, col);
        if (k != Math.Floor(k) || k < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the number of clusters must be a whole number of at least one.");
        }

        return (int)k;
    }

    private static Partitional.Plan PartitionPlan(
        string name, ParsedArgs parsed, IReadOnlyList<double[]> rows, int k, int width, int line, int col)
    {
        StartRule start = StartRule.Plus;
        double[,]? given = null;
        if (parsed.Named("start") is { } startValue)
        {
            if (startValue.Type == JgsType.String)
            {
                start = startValue.AsString.ToLowerInvariant() switch
                {
                    "plus" or "kmeans++" => StartRule.Plus,
                    "sample" => StartRule.Sample,
                    "uniform" => StartRule.Uniform,
                    "cluster" => throw new JgsRuntimeException(line, col,
                        $"{name}: the 'cluster' start pre-clusters a tenth of the data and is not supported; "
                        + "'plus', 'sample' and 'uniform' are."),
                    _ => throw new JgsRuntimeException(line, col,
                        $"{name}: the start is 'plus', 'sample', 'uniform', or the starting centres themselves."),
                };
            }
            else
            {
                (double[] flat, int centres, int columns) = DenseMatrix(name, startValue, line, col);
                if (centres != k || columns != width)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the starting centres must be one row per cluster and one column per variable.");
                }

                given = new double[centres, columns];
                for (int r = 0; r < centres; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        given[r, c] = flat[r + (c * centres)];
                    }
                }

                start = StartRule.Given;
            }
        }

        EmptyClusterRule onEmpty = parsed.Word("emptyaction", "singleton", "singleton", "drop", "error") switch
        {
            "drop" => EmptyClusterRule.Drop,
            "error" => EmptyClusterRule.Error,
            _ => EmptyClusterRule.Singleton,
        };

        int replicates = parsed.Whole("replicates", 1);
        if (start == StartRule.Given && replicates > 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: starting from given centres more than once would repeat the same run.");
        }

        _ = rows;
        return new Partitional.Plan(
            replicates, parsed.Whole("maxiter", 100), start, given, onEmpty);
    }

    private static JgsValue[] PartitionOutputs(int wanted, Partitional.Partition partition) =>
        Outputs(wanted,
            ColumnOfWholes(partition.Labels),
            Rectangle(partition.Centres),
            ColumnOfAnswers(partition.WithinSums),
            Rectangle(partition.ToCentres));

    /// <summary>A word argument, refused by name when something else was given.</summary>
    private static string ClusterWord(string name, JgsValue value, int line, int col) =>
        value.Type == JgsType.String
            ? value.AsString
            : throw new JgsRuntimeException(line, col, $"{name} expects a word there, not a {value.TypeName}.");

    /// <summary>A single number, however it was written.</summary>
    private static double ClusterNumber(string name, JgsValue value, int line, int col) =>
        value.Type is JgsType.Number or JgsType.Bool
            ? value.AsNumber
            : throw new JgsRuntimeException(line, col, $"{name} expects a number there, not a {value.TypeName}.");

    private static JgsValue ColumnOfWholes(int[] values)
    {
        var numbers = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            numbers[i] = values[i];
        }

        return ColumnOfAnswers(numbers);
    }

    private static JgsValue ColumnOfFlags(bool[] flags)
    {
        var elements = new JgsValue[flags.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            elements[i] = JgsValue.Bool(flags[i]);
        }

        JgsValue column = JgsValue.Array(elements);
        column.ReshapeDims([flags.Length, 1]);
        return column;
    }

    private static void RefuseSearcherOptions(string name, ParsedArgs parsed, int line, int col)
    {
        // 'NSMethod' and 'BucketSize' choose between search structures, and the two structures answer
        // the same question exactly. Both words are therefore accepted and neither changes the answer;
        // what changes is how long it takes, and the search here is exhaustive either way.
        if (parsed.Named("nsmethod") is not null)
        {
            parsed.Word("nsmethod", "exhaustive", "exhaustive", "kdtree");
        }

        if (parsed.Named("sortindices") is { } sorted && !sorted.IsTruthy)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'SortIndices' cannot be turned off; the neighbours always come back nearest first.");
        }
    }
}
