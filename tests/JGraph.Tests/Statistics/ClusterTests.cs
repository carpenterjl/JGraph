using JGraph.Statistics.Cluster;
using JGraph.Statistics.Multivariate;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// The clustering and multivariate kernels of M53 wave H. The distances are written out longhand
/// because each has a closed form; everything above them is pinned by an identity a wrong
/// implementation would break — a tree over two obvious groups merges within them first, a
/// factorization at full rank reproduces its matrix, principal components reconstruct the data they
/// came from, a configuration matched against itself needs no moving, and a hidden Markov model
/// trained on a sequence it generated recovers the matrices that generated it.
/// </summary>
public class ClusterTests
{
    /// <summary>Two tight groups a long way apart, which every method here should find.</summary>
    private static readonly double[][] TwoGroups =
    [
        [1, 1], [1, 2], [2, 1], [20, 20], [20, 21], [21, 20],
    ];

    private static DistanceMeasure Euclidean(IReadOnlyList<double[]> data) =>
        DistanceMeasure.Create(DistanceMetric.Euclidean, data);

    // --- Distances ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DistanceMetric.Euclidean, 5.0)]
    [InlineData(DistanceMetric.SquaredEuclidean, 25.0)]
    [InlineData(DistanceMetric.CityBlock, 7.0)]
    [InlineData(DistanceMetric.Chebychev, 4.0)]
    [InlineData(DistanceMetric.Hamming, 1.0)]
    public void Distance_MatchesTheClosedForm(DistanceMetric metric, double expected)
    {
        double[][] points = [[0, 0], [3, 4]];
        DistanceMeasure measure = DistanceMeasure.Create(metric, points);
        Assert.Equal(expected, measure.Distance(points[0], points[1]), 12);
    }

    [Fact]
    public void MinkowskiDistance_TakesItsExponent()
    {
        double[][] points = [[0, 0], [3, 4]];
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.Minkowski, points, exponent: 3);
        Assert.Equal(Math.Cbrt(27 + 64), measure.Distance(points[0], points[1]), 12);
    }

    [Fact]
    public void CosineDistance_IsZeroForCollinearObservationsAndOneForOrthogonalOnes()
    {
        double[][] points = [[1, 2, 3], [2, 4, 6], [1, -1, 0]];
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.Cosine, points);
        Assert.Equal(0, measure.Distance(points[0], points[1]), 12);
        Assert.Equal(1, measure.Distance(points[1], [-1, 2, -1]), 12);
    }

    [Fact]
    public void SpearmanDistance_IsTwoWhenTheRanksAreExactlyReversed()
    {
        double[][] points = [[1, 2, 3], [3, 2, 1]];
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.Spearman, points);
        Assert.Equal(2, measure.Distance(points[0], points[1]), 12);
    }

    [Fact]
    public void JaccardDistance_IgnoresTheCoordinatesWhereBothAreZero()
    {
        double[][] points = [[1, 0, 0, 1], [1, 1, 0, 0]];
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.Jaccard, points);

        // Three coordinates have something in them and two of those three disagree; the fourth, where
        // both are zero, is not counted at all — which is the whole difference from Hamming.
        Assert.Equal(2.0 / 3, measure.Distance(points[0], points[1]), 12);
    }

    [Fact]
    public void StandardizedEuclidean_DividesEachVariableByItsOwnSpread()
    {
        double[][] points = [[0, 0], [1, 10]];
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.StandardizedEuclidean, points);

        // Each variable's spread over the two points is its own gap over the square root of two, so
        // once standardized both coordinates differ by exactly the square root of two and the distance
        // is two — the same answer whatever the units the second variable happened to be measured in.
        Assert.Equal(2, measure.Distance(points[0], points[1]), 12);
    }

    [Fact]
    public void PairwiseDistances_AreTheUpperTriangleReadAcrossEachRow()
    {
        double[][] points = [[0, 0], [3, 0], [0, 4]];
        double[] condensed = Distances.Pairwise(points, Euclidean(points));

        Assert.Equal([3, 4, 5], condensed);
        Assert.Equal(3, Distances.SideOf(condensed.Length));
    }

    [Fact]
    public void SquareForm_RoundTripsThroughTheMatrixAndBack()
    {
        double[][] points = [[0, 0], [3, 0], [0, 4], [1, 1]];
        double[] condensed = Distances.Pairwise(points, Euclidean(points));
        double[,] square = Distances.SquareForm(condensed);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0, square[i, i]);
            for (int j = 0; j < 4; j++)
            {
                Assert.Equal(square[i, j], square[j, i], 12);
            }
        }

        Assert.Equal(condensed, Distances.CondensedForm(square));
    }

    [Fact]
    public void SideOf_RefusesALengthNoNumberOfObservationsCouldProduce() =>
        Assert.Throws<ArgumentException>(() => Distances.SideOf(5));

    [Fact]
    public void Nearest_ReportsTheNeighboursInIncreasingDistance()
    {
        (int[][] index, double[][] distance) = Distances.Nearest(
            TwoGroups, [[0, 0]], 3, Euclidean(TwoGroups));

        Assert.Equal([0, 1, 2], index[0].OrderBy(i => i).ToArray());
        Assert.True(distance[0][0] <= distance[0][1]);
        Assert.True(distance[0][1] <= distance[0][2]);
    }

    [Fact]
    public void Within_KeepsOnlyWhatIsInsideTheRadius()
    {
        (int[][] index, double[][] distance) = Distances.Within(
            TwoGroups, [[1, 1]], 1.5, Euclidean(TwoGroups));

        Assert.Equal(3, index[0].Length);
        Assert.All(distance[0], d => Assert.True(d <= 1.5));
    }

    [Fact]
    public void Precomputed_ReadsItsAnswersOutOfTheMatrixItWasGiven()
    {
        double[][] matrix = [[0, 2, 9], [2, 0, 3], [9, 3, 0]];
        DistanceMeasure measure = DistanceMeasure.Precomputed(matrix);

        Assert.Equal(2, measure.Distance(matrix[0], matrix[1]), 12);
        Assert.Equal(9, measure.Distance(matrix[0], matrix[2]), 12);
        Assert.Equal(0, measure.Distance(matrix[1], matrix[1]), 12);
    }

    [Fact]
    public void Mahalanobis_IsTheSquaredDistanceInTheSamplesOwnMetric()
    {
        double[][] reference = [[0, 0], [2, 0], [0, 2], [2, 2], [1, 1]];

        // The reference is centred on (1, 1) with unit variance in each direction and no correlation,
        // so the squared distance from (2, 2) is just two.
        double[] squared = Distances.Mahalanobis([[2, 2]], reference);
        Assert.Equal(2, squared[0], 10);
    }

    // --- Hierarchical ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(LinkageMethod.Single)]
    [InlineData(LinkageMethod.Complete)]
    [InlineData(LinkageMethod.Average)]
    [InlineData(LinkageMethod.Weighted)]
    [InlineData(LinkageMethod.Centroid)]
    [InlineData(LinkageMethod.Median)]
    [InlineData(LinkageMethod.Ward)]
    public void Link_MergesWithinTheTwoGroupsBeforeItJoinsThem(LinkageMethod method)
    {
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree tree = Hierarchical.Link(condensed, method);

        Assert.Equal(5, tree.Height.Length);

        // Whatever the method, the last merge is the one that spans the gap, and it is far larger than
        // any before it — which is the property every one of the seven should agree on.
        Assert.True(tree.Height[4] > 10 * tree.Height[0]);
        int[] labels = Hierarchical.Cut(tree, 2, double.NaN, byInconsistency: false, depth: 2);
        Assert.Equal([1, 1, 1, 2, 2, 2], labels);
    }

    [Fact]
    public void Link_ChoosesDifferentHeightsForSingleAndCompleteLinkage()
    {
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree single = Hierarchical.Link(condensed, LinkageMethod.Single);
        Hierarchical.Tree complete = Hierarchical.Link(condensed, LinkageMethod.Complete);

        Assert.True(complete.Height[4] > single.Height[4]);
    }

    [Fact]
    public void Cut_ByHeightKeepsWholeSubtrees()
    {
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree tree = Hierarchical.Link(condensed, LinkageMethod.Single);

        int[] every = Hierarchical.Cut(tree, count: null, cutoff: 100, byInconsistency: false, depth: 2);
        Assert.All(every, label => Assert.Equal(1, label));

        int[] none = Hierarchical.Cut(tree, count: null, cutoff: 0, byInconsistency: false, depth: 2);
        Assert.Equal([1, 2, 3, 4, 5, 6], none);
    }

    [Fact]
    public void Cophenetic_IsHighWhereTheDataReallyIsNested()
    {
        // Two tight groups a long way apart is the shape a tree describes best, so the heights it
        // assigns track the distances closely; a chain of equally spaced points, which has no nesting
        // at all, is the case the same measure should score lower — and does.
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree tree = Hierarchical.Link(condensed, LinkageMethod.Single);
        (double correlation, double[] heights) = Hierarchical.Cophenetic(tree, condensed);

        Assert.Equal(condensed.Length, heights.Length);
        Assert.True(correlation > 0.9, $"the correlation was {correlation}.");
        Assert.All(heights, h => Assert.True(h > 0));

        double[][] chain = [[0], [1], [2], [3]];
        double[] spaced = Distances.Pairwise(chain, Euclidean(chain));
        Hierarchical.Tree flat = Hierarchical.Link(spaced, LinkageMethod.Complete);
        Assert.True(Hierarchical.Cophenetic(flat, spaced).Correlation < correlation);
    }

    [Fact]
    public void Inconsistent_ReportsZeroWhereThereIsNothingBelowToCompareAgainst()
    {
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree tree = Hierarchical.Link(condensed, LinkageMethod.Single);
        Hierarchical.Inconsistency measured = Hierarchical.Inconsistent(tree, depth: 2);

        Assert.Equal(5, measured.Ratio.Length);
        Assert.Equal(1, measured.Count[0]);
        Assert.Equal(0, measured.Deviation[0]);
        Assert.Equal(0, measured.Ratio[0]);
        Assert.True(measured.Ratio[4] > 0);
    }

    [Fact]
    public void OptimalLeafOrder_PutsEachGroupsMembersTogether()
    {
        double[] condensed = Distances.Pairwise(TwoGroups, Euclidean(TwoGroups));
        Hierarchical.Tree tree = Hierarchical.Link(condensed, LinkageMethod.Single);
        int[] order = Hierarchical.OptimalLeafOrder(tree, condensed);

        Assert.Equal(6, order.Distinct().Count());
        bool firstHalfIsOneGroup = order[..3].All(i => i < 3) || order[..3].All(i => i >= 3);
        Assert.True(firstHalfIsOneGroup, "the two groups should not be interleaved.");
    }

    // --- Partitional ------------------------------------------------------------------------------------

    [Fact]
    public void KMeans_FindsTheTwoGroupsAndCentresThemOnTheirMeans()
    {
        Partitional.Partition partition = Partitional.KMeans(TwoGroups, 2, new Partitional.Plan(), new Random(7));

        Assert.True(partition.Labels[0] == partition.Labels[1] && partition.Labels[1] == partition.Labels[2]);
        Assert.NotEqual(partition.Labels[0], partition.Labels[3]);
        Assert.Equal(2, partition.Centres.GetLength(0));

        double[] centres = [partition.Centres[0, 0], partition.Centres[1, 0]];
        Array.Sort(centres);
        Assert.Equal(4.0 / 3, centres[0], 10);
        Assert.Equal(61.0 / 3, centres[1], 10);
    }

    [Fact]
    public void KMeans_RepeatsItselfUnderTheSameSeedAndDiffersFromAnother()
    {
        Partitional.Partition first = Partitional.KMeans(TwoGroups, 2, new Partitional.Plan(), new Random(3));
        Partitional.Partition again = Partitional.KMeans(TwoGroups, 2, new Partitional.Plan(), new Random(3));

        Assert.Equal(first.Labels, again.Labels);
    }

    [Fact]
    public void KMeans_StartsFromTheCentresItWasGiven()
    {
        var given = new double[,] { { 1, 1 }, { 20, 20 } };
        var plan = new Partitional.Plan(Start: StartRule.Given, Given: given);
        Partitional.Partition partition = Partitional.KMeans(TwoGroups, 2, plan, new Random(1));

        Assert.Equal([1, 1, 1, 2, 2, 2], partition.Labels);
    }

    [Fact]
    public void KMedoids_ChoosesCentresThatAreThemselvesObservations()
    {
        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.CityBlock, TwoGroups);
        Partitional.Partition partition = Partitional.KMedoids(
            TwoGroups, 2, new Partitional.Plan(), new Random(5), measure);

        for (int c = 0; c < 2; c++)
        {
            double[] centre = [partition.Centres[c, 0], partition.Centres[c, 1]];
            Assert.Contains(TwoGroups, row => row[0] == centre[0] && row[1] == centre[1]);
        }
    }

    [Fact]
    public void Dbscan_LeavesAPointWithNoNeighboursInNoClusterAtAll()
    {
        double[][] withAnOutlier = [.. TwoGroups, [200, 200]];
        DistanceMeasure measure = Euclidean(withAnOutlier);
        (int[] labels, bool[] core) = Partitional.Dbscan(withAnOutlier, 2, 2, measure);

        Assert.Equal(labels[0], labels[2]);
        Assert.NotEqual(labels[0], labels[3]);
        Assert.Equal(-1, labels[6]);
        Assert.False(core[6]);
    }

    [Fact]
    public void Spectral_SeparatesTheTwoGroups()
    {
        (int[] labels, double[,] vectors, double[] values) = Partitional.Spectral(
            TwoGroups, 2, 5, Euclidean(TwoGroups), new Partitional.Plan(), new Random(2));

        Assert.Equal(labels[0], labels[2]);
        Assert.NotEqual(labels[0], labels[3]);
        Assert.Equal(6, vectors.GetLength(0));
        Assert.Equal(2, values.Length);

        // The Laplacian's smallest eigenvalue is zero for any graph, and a second one near zero is the
        // signature of a second connected component — which is exactly the structure being looked for.
        Assert.True(Math.Abs(values[0]) < 1e-8);
    }

    [Fact]
    public void Silhouette_IsNearOneForWellSeparatedGroupsAndZeroForALoneObservation()
    {
        int[] labels = [1, 1, 1, 2, 2, 2];
        double[] values = Partitional.Silhouette(TwoGroups, labels, Euclidean(TwoGroups));
        Assert.All(values, v => Assert.True(v > 0.9));

        int[] withALoner = [1, 1, 1, 2, 2, 3];
        double[] lonely = Partitional.Silhouette(TwoGroups, withALoner, Euclidean(TwoGroups));
        Assert.Equal(0, lonely[5], 12);
    }

    // --- Principal components ------------------------------------------------------------------------

    /// <summary>Three variables whose variation lies almost entirely along one direction.</summary>
    private static readonly double[,] Cloud = new double[,]
    {
        { 1, 2, 3.1 }, { 2, 4, 5.5 }, { 3, 6, 9.5 }, { 4, 8, 11 },
        { 5, 10, 14 }, { 6, 12, 17.5 }, { 7, 14, 19 }, { 8, 16, 23 },
    };

    [Fact]
    public void Components_ReconstructTheDataTheyCameFrom()
    {
        PrincipalComponents.Analysis analysis = PrincipalComponents.Analyse(Cloud);

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                double fitted = analysis.Centre[c];
                for (int k = 0; k < analysis.Latent.Length; k++)
                {
                    fitted += analysis.Scores[r, k] * analysis.Coefficients[c, k];
                }

                Assert.Equal(Cloud[r, c], fitted, 10);
            }
        }
    }

    [Fact]
    public void Components_AccountForTheWholeVarianceAndAreOrthonormal()
    {
        PrincipalComponents.Analysis analysis = PrincipalComponents.Analyse(Cloud);

        double total = 0;
        for (int c = 0; c < 3; c++)
        {
            double mean = 0;
            for (int r = 0; r < 8; r++)
            {
                mean += Cloud[r, c];
            }

            mean /= 8;
            double sum = 0;
            for (int r = 0; r < 8; r++)
            {
                sum += (Cloud[r, c] - mean) * (Cloud[r, c] - mean);
            }

            total += sum / 7;
        }

        Assert.Equal(total, analysis.Latent.Sum(), 10);
        Assert.Equal(100, analysis.Explained.Sum(), 10);

        for (int i = 0; i < analysis.Latent.Length; i++)
        {
            for (int j = 0; j < analysis.Latent.Length; j++)
            {
                double dot = 0;
                for (int c = 0; c < 3; c++)
                {
                    dot += analysis.Coefficients[c, i] * analysis.Coefficients[c, j];
                }

                Assert.Equal(i == j ? 1 : 0, dot, 10);
            }
        }
    }

    [Fact]
    public void Components_DescendInVariance()
    {
        PrincipalComponents.Analysis analysis = PrincipalComponents.Analyse(Cloud);
        for (int i = 1; i < analysis.Latent.Length; i++)
        {
            Assert.True(analysis.Latent[i] <= analysis.Latent[i - 1] + 1e-12);
        }
    }

    [Fact]
    public void ComponentsOfACovariance_AgreeWithTheComponentsOfTheDataItCameFrom()
    {
        PrincipalComponents.Analysis analysis = PrincipalComponents.Analyse(Cloud);

        var covariance = new double[3, 3];
        var means = new double[3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 8; r++)
            {
                means[c] += Cloud[r, c];
            }

            means[c] /= 8;
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                for (int r = 0; r < 8; r++)
                {
                    covariance[i, j] += (Cloud[r, i] - means[i]) * (Cloud[r, j] - means[j]);
                }

                covariance[i, j] /= 7;
            }
        }

        (double[,] coefficients, double[] latent, _) = PrincipalComponents.FromCovariance(covariance);
        Assert.Equal(analysis.Latent[0], latent[0], 8);
        Assert.Equal(Math.Abs(analysis.Coefficients[0, 0]), Math.Abs(coefficients[0, 0]), 8);
    }

    [Fact]
    public void Residuals_VanishWhenEveryComponentIsKept()
    {
        (double[,] residuals, double[,] reconstructed) = PrincipalComponents.Residuals(Cloud, 3);

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(0, residuals[r, c], 10);
                Assert.Equal(Cloud[r, c], reconstructed[r, c], 10);
            }
        }
    }

    [Fact]
    public void Probabilistically_RecoversAValueThatWasNotThere()
    {
        // Column two is exactly twice column one, so the missing entry is determined by the model even
        // though nothing observed it — which is the whole claim the probabilistic fit makes.
        var withAGap = (double[,])Cloud.Clone();
        withAGap[2, 1] = double.NaN;

        PrincipalComponents.Probabilistic fit = PrincipalComponents.Probabilistically(withAGap, 2);
        Assert.True(fit.Converged);
        Assert.Equal(9, fit.Centre[1], 4);
    }

    [Fact]
    public void NonNegativeFactors_ReproduceAMatrixThatHasAnExactFactorization()
    {
        // Every row is a non-negative combination of [1 2 3] and [1 1 1], so a rank-two factorization
        // with no negative entries fits exactly.
        var a = new double[,] { { 1, 2, 3 }, { 2, 4, 6 }, { 3, 6, 9 }, { 1, 1, 1 } };
        (double[,] w, double[,] h, double residual) = PrincipalComponents.NonNegativeFactors(
            a, 2, new Random(9), replicates: 10, maxIterations: 2000, tolerance: 1e-12);

        Assert.True(residual < 1e-6, $"the residual was {residual}.");
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.True(w[r, 0] >= 0 && h[0, c] >= 0);
            }
        }
    }

    [Fact]
    public void NonNegativeFactors_RefuseAMatrixWithANegativeEntry() =>
        Assert.Throws<ArgumentException>(() => PrincipalComponents.NonNegativeFactors(
            new double[,] { { 1, -1 }, { 1, 1 } }, 1, new Random(1)));

    [Fact]
    public void Rotate_KeepsTheSpaceTheLoadingsSpanAndSharpensThem()
    {
        var loadings = new double[,] { { 0.8, 0.4 }, { 0.7, 0.5 }, { 0.4, 0.8 }, { 0.5, 0.7 } };
        (double[,] rotated, double[,] transform) = PrincipalComponents.Rotate(
            loadings, PrincipalComponents.Rotation.Varimax);

        // An orthogonal rotation leaves each variable's total loading alone; only how it is divided
        // between the components changes.
        for (int r = 0; r < 4; r++)
        {
            double before = (loadings[r, 0] * loadings[r, 0]) + (loadings[r, 1] * loadings[r, 1]);
            double after = (rotated[r, 0] * rotated[r, 0]) + (rotated[r, 1] * rotated[r, 1]);
            Assert.Equal(before, after, 8);
        }

        double product = (transform[0, 0] * transform[0, 1]) + (transform[1, 0] * transform[1, 1]);
        Assert.Equal(0, product, 8);
    }

    // --- Scaling ----------------------------------------------------------------------------------------

    [Fact]
    public void ClassicalScaling_ReproducesTheDistancesItWasGiven()
    {
        double[][] points = [[0, 0], [3, 0], [0, 4], [3, 4], [1, 2]];
        double[] condensed = Distances.Pairwise(points, Euclidean(points));
        (double[,] coordinates, double[] values) = Scaling.Classical(Distances.SquareForm(condensed));

        Assert.Equal(2, coordinates.GetLength(1));
        var recovered = new double[5][];
        for (int r = 0; r < 5; r++)
        {
            recovered[r] = [coordinates[r, 0], coordinates[r, 1]];
        }

        double[] again = Distances.Pairwise(recovered, Euclidean(recovered));
        for (int i = 0; i < condensed.Length; i++)
        {
            Assert.Equal(condensed[i], again[i], 8);
        }

        Assert.True(values[0] >= values[1]);
    }

    [Fact]
    public void Procrustes_NeedsNoMovementToMatchAConfigurationWithItself()
    {
        (double dissimilarity, double[,] transformed, Scaling.Transformation transform) =
            Scaling.Procrustes(Cloud, Cloud);

        Assert.Equal(0, dissimilarity, 10);
        Assert.Equal(1, transform.Scale, 8);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(Cloud[r, c], transformed[r, c], 8);
            }
        }
    }

    [Fact]
    public void Procrustes_UndoesARotationExactly()
    {
        var turned = new double[8, 3];
        for (int r = 0; r < 8; r++)
        {
            turned[r, 0] = -Cloud[r, 1] + 5;
            turned[r, 1] = Cloud[r, 0] + 5;
            turned[r, 2] = Cloud[r, 2] + 5;
        }

        (double dissimilarity, double[,] transformed, _) = Scaling.Procrustes(Cloud, turned);
        Assert.Equal(0, dissimilarity, 10);
        for (int r = 0; r < 8; r++)
        {
            Assert.Equal(Cloud[r, 0], transformed[r, 0], 8);
        }
    }

    [Fact]
    public void CanonicalCorrelation_IsOneWhereTheTwoSetsShareAVariable()
    {
        var left = new double[,] { { 1, 5 }, { 2, 3 }, { 3, 8 }, { 4, 2 }, { 5, 9 }, { 6, 4 }, { 7, 7 }, { 8, 1 } };
        var right = new double[,] { { 2, 1 }, { 4, 4 }, { 6, 2 }, { 8, 7 }, { 10, 3 }, { 12, 8 }, { 14, 5 }, { 16, 9 } };

        Scaling.Canonical fit = Scaling.CanonicalCorrelation(left, right);

        // The first column of the second set is twice the first column of the first, so a pair of
        // combinations correlates perfectly and the leading canonical correlation is one.
        Assert.Equal(1, fit.R[0], 8);
        Assert.True(fit.R[1] <= fit.R[0]);
        Assert.All(fit.R, r => Assert.InRange(r, 0, 1 + 1e-12));
        Assert.Equal(8, fit.U.GetLength(0));
    }

    // --- Robust covariance --------------------------------------------------------------------------------

    [Fact]
    public void RobustCovariance_IgnoresAPointThatWouldDominateTheOrdinaryOne()
    {
        var contaminated = new double[13, 2];
        for (int r = 0; r < 12; r++)
        {
            contaminated[r, 0] = r % 4;
            contaminated[r, 1] = r / 4;
        }

        contaminated[12, 0] = 500;
        contaminated[12, 1] = 500;

        RobustCovariance.Estimate estimate = RobustCovariance.Fit(
            contaminated, RobustCovarianceMethod.MinimumDeterminant, new Random(4), starts: 50);

        Assert.True(estimate.Outliers[12], "the point at five hundred should be flagged.");
        Assert.True(estimate.Centre[0] < 10, $"the centre was dragged to {estimate.Centre[0]}.");
        Assert.True(estimate.Distances[12] > estimate.Cutoff);
    }

    [Theory]
    [InlineData(RobustCovarianceMethod.MinimumDeterminant)]
    [InlineData(RobustCovarianceMethod.Orthogonalized)]
    [InlineData(RobustCovarianceMethod.OliveHawkins)]
    public void RobustCovariance_AnswersASymmetricMatrixWhicheverEstimatorIsAsked(RobustCovarianceMethod method)
    {
        RobustCovariance.Estimate estimate = RobustCovariance.Fit(Cloud, method, new Random(1), starts: 25);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(estimate.Covariance[i, i] >= 0);
            for (int j = 0; j < 3; j++)
            {
                Assert.Equal(estimate.Covariance[i, j], estimate.Covariance[j, i], 10);
            }
        }
    }

    // --- Hidden Markov -------------------------------------------------------------------------------------

    private static readonly double[,] Transition = new double[,] { { 0.9, 0.1 }, { 0.2, 0.8 } };
    private static readonly double[,] Emission = new double[,] { { 0.85, 0.15 }, { 0.2, 0.8 } };

    [Fact]
    public void Generate_DrawsSymbolsAndStatesTheModelAllows()
    {
        (int[] sequence, int[] states) = HiddenMarkov.Generate(500, Transition, Emission, new Random(11));

        Assert.Equal(500, sequence.Length);
        Assert.All(sequence, s => Assert.InRange(s, 0, 1));
        Assert.All(states, s => Assert.InRange(s, 0, 1));
    }

    [Fact]
    public void Decode_AnswersProbabilitiesThatSumToOneAtEveryStep()
    {
        (int[] sequence, _) = HiddenMarkov.Generate(200, Transition, Emission, new Random(12));
        HiddenMarkov.Decoding decoded = HiddenMarkov.Decode(sequence, Transition, Emission);

        Assert.Equal(201, decoded.Probabilities.GetLength(1));
        for (int t = 0; t <= 200; t++)
        {
            double total = decoded.Probabilities[0, t] + decoded.Probabilities[1, t];
            Assert.Equal(1, total, 8);
        }

        Assert.True(decoded.LogLikelihood < 0);
    }

    [Fact]
    public void Viterbi_FindsAPathAtLeastAsLikelyAsTheOneThatGeneratedTheSequence()
    {
        (int[] sequence, int[] truth) = HiddenMarkov.Generate(200, Transition, Emission, new Random(13));
        (int[] path, double logProbability) = HiddenMarkov.Viterbi(sequence, Transition, Emission);

        Assert.Equal(200, path.Length);
        Assert.True(logProbability >= PathLikelihood(sequence, truth) - 1e-9);
    }

    [Fact]
    public void EstimateFromStates_RecoversTheMatricesThatGeneratedALongSequence()
    {
        (int[] sequence, int[] states) = HiddenMarkov.Generate(20000, Transition, Emission, new Random(14));
        (double[,] transition, double[,] emission) =
            HiddenMarkov.EstimateFromStates(sequence, states, 2, 2);

        Assert.Equal(0.9, transition[0, 0], 1);
        Assert.Equal(0.8, emission[1, 1], 1);
        Assert.Equal(1, transition[0, 0] + transition[0, 1], 10);
    }

    [Fact]
    public void Train_ImprovesTheLikelihoodOfTheGuessItStartedFrom()
    {
        (int[] sequence, _) = HiddenMarkov.Generate(500, Transition, Emission, new Random(15));
        var guessTransition = new double[,] { { 0.6, 0.4 }, { 0.4, 0.6 } };
        var guessEmission = new double[,] { { 0.6, 0.4 }, { 0.4, 0.6 } };

        double before = HiddenMarkov.Decode(sequence, guessTransition, guessEmission).LogLikelihood;
        (double[,] transition, double[,] emission, double after, _, bool converged) =
            HiddenMarkov.Train([sequence], guessTransition, guessEmission);

        Assert.True(converged);
        Assert.True(after > before, $"the likelihood went from {before} to {after}.");
        Assert.Equal(1, transition[0, 0] + transition[0, 1], 8);
        Assert.Equal(1, emission[0, 0] + emission[0, 1], 8);
    }

    [Fact]
    public void Decode_RefusesAModelWhoseRowsDoNotSumToOne() =>
        Assert.Throws<ArgumentException>(() => HiddenMarkov.Decode(
            [0, 1], new double[,] { { 0.5, 0.4 }, { 0.2, 0.8 } }, Emission));

    private static double PathLikelihood(int[] sequence, int[] states)
    {
        double total = Math.Log(Transition[0, states[0]]) + Math.Log(Emission[states[0], sequence[0]]);
        for (int t = 1; t < sequence.Length; t++)
        {
            total += Math.Log(Transition[states[t - 1], states[t]]) + Math.Log(Emission[states[t], sequence[t]]);
        }

        return total;
    }
}
