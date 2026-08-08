using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The clustering and multivariate surface as a script sees it (M53 wave H): every documented output
/// in MathWorks' order, every option word taking effect, and every refusal naming what was wrong.
/// </summary>
/// <remarks>
/// The pins are identities rather than copied constants wherever one exists — squareform round-trips,
/// principal components reconstruct their data, a Procrustes fit of a configuration against itself
/// needs no moving, a hidden Markov posterior sums to one at every step — because those are the
/// checks a plausible-looking wrong implementation fails.
/// </remarks>
[Collection("JG facade")]
public class MatlabClusterTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabClusterTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "the call was expected to be refused.");
        return result.Message + _output.ErrorText;
    }

    /// <summary>Two tight groups a long way apart.</summary>
    private const string Groups = """
        X = [1 1; 1 2; 2 1; 20 20; 20 21; 21 20];
        """;

    /// <summary>Three variables whose variation lies almost entirely along one direction.</summary>
    private const string Cloud = """
        C = [1 2 3.1; 2 4 5.5; 3 6 9.5; 4 8 11; 5 10 14; 6 12 17.5; 7 14 19; 8 16 23];
        """;

    // --- pdist, pdist2, squareform ---------------------------------------------------------------------

    [Fact]
    public Task Pdist_AnswersOneRowOfEveryPair() => RunAsserting(Groups + """
        D = pdist(X);
        assert(numel(D) == 15);
        assert(size(D, 1) == 1);
        assert(abs(D(1) - 1) < 1e-12);
        """);

    [Theory]
    [InlineData("'euclidean'", 5)]
    [InlineData("'squaredeuclidean'", 25)]
    [InlineData("'cityblock'", 7)]
    [InlineData("'chebychev'", 4)]
    public Task Pdist_TakesEveryDocumentedMetricWord(string metric, double expected) => RunAsserting($"""
        d = pdist([0 0; 3 4], {metric});
        assert(abs(d - {expected}) < 1e-12);
        """);

    [Fact]
    public Task Pdist_TakesTheMinkowskiExponentAfterTheMetric() => RunAsserting("""
        d = pdist([0 0; 3 4], 'minkowski', 3);
        assert(abs(d - (27 + 64)^(1/3)) < 1e-10);
        """);

    [Fact]
    public Task Pdist_TakesAMahalanobisCovarianceAfterTheMetric() => RunAsserting("""
        X = [0 0; 2 0; 0 2; 2 2; 1 1];
        d = pdist(X, 'mahalanobis', eye(2));
        e = pdist(X, 'euclidean');
        assert(max(abs(d - e)) < 1e-10);
        """);

    [Fact]
    public Task Squareform_RoundTripsBothWays() => RunAsserting(Groups + """
        D = pdist(X);
        S = squareform(D);
        assert(size(S, 1) == 6 && size(S, 2) == 6);
        assert(abs(S(1, 1)) < 1e-15);
        assert(abs(S(1, 2) - S(2, 1)) < 1e-15);
        assert(max(abs(squareform(S) - D)) < 1e-12);
        assert(max(abs(squareform(D, 'tomatrix')(1, :) - S(1, :))) < 1e-12);
        """);

    [Fact]
    public Task Pdist2_AnswersOneRowPerLeftObservation() => RunAsserting(Groups + """
        P = pdist2(X, [0 0; 20 20]);
        assert(size(P, 1) == 6 && size(P, 2) == 2);
        assert(abs(P(1, 1) - sqrt(2)) < 1e-12);
        assert(abs(P(4, 2)) < 1e-12);
        """);

    [Fact]
    public async Task Pdist2_RefusesTwoSetsOfDifferentWidths() =>
        Assert.Contains("same number of columns", await RunExpectingFailure("pdist2([1 2], [1 2 3]);"));

    [Fact]
    public Task Mahal_IsTheSquaredDistanceInTheReferencesOwnMetric() => RunAsserting("""
        X = [0 0; 2 0; 0 2; 2 2; 1 1];
        d = mahal([2 2], X);
        assert(abs(d - 2) < 1e-8);
        """);

    // --- knnsearch, rangesearch ---------------------------------------------------------------------------

    [Fact]
    public Task Knnsearch_AnswersTheNearestNeighboursNearestFirst() => RunAsserting(Groups + """
        [idx, d] = knnsearch(X, [0 0; 20 20], 'K', 3);
        assert(size(idx, 1) == 2 && size(idx, 2) == 3);
        assert(all(sort(idx(1, :)) == [1 2 3]));
        assert(idx(2, 1) == 4);
        assert(d(1, 1) <= d(1, 2) && d(1, 2) <= d(1, 3));
        """);

    [Fact]
    public Task Knnsearch_TakesADistanceWordAndItsExponent() => RunAsserting(Groups + """
        a = knnsearch(X, [0 0], 'K', 1, 'Distance', 'cityblock');
        b = knnsearch(X, [0 0], 'K', 1, 'Distance', 'minkowski', 'P', 3);
        assert(a == 1 && b == 1);
        """);

    [Fact]
    public Task Rangesearch_AnswersACellOfVariableLengthNeighbourhoods() => RunAsserting(Groups + """
        [idx, d] = rangesearch(X, [1 1; 100 100], 1.5);
        assert(iscell(idx));
        assert(numel(idx{1}) == 3);
        assert(isempty(idx{2}));
        assert(all(d{1} <= 1.5));
        """);

    [Fact]
    public async Task Knnsearch_RefusesTheSearchStructureOptionsItCannotHonour() =>
        Assert.Contains("exhaustive", await RunExpectingFailure(
            "knnsearch([1 1; 2 2], [1 1], 'NSMethod', 'kdtree');"));

    // --- linkage, cluster, clusterdata, cophenet, inconsistent, optimalleaforder --------------------------

    [Fact]
    public Task Linkage_AnswersMathWorksThreeColumnMatrix() => RunAsserting(Groups + """
        Z = linkage(X);
        assert(size(Z, 1) == 5 && size(Z, 2) == 3);
        assert(all(Z(:, 1) >= 1) && all(Z(:, 2) >= 1));
        assert(Z(end, 3) > 10 * Z(1, 3));
        """);

    [Fact]
    public Task Linkage_ReadsEitherTheDataOrDistancesAlreadyComputed() => RunAsserting(Groups + """
        a = linkage(X, 'average');
        b = linkage(pdist(X), 'average');
        assert(max(max(abs(a - b))) < 1e-12);
        """);

    [Theory]
    [InlineData("single")]
    [InlineData("complete")]
    [InlineData("average")]
    [InlineData("weighted")]
    [InlineData("centroid")]
    [InlineData("median")]
    [InlineData("ward")]
    public Task Linkage_TakesEveryDocumentedMethod(string method) => RunAsserting(Groups + $"""
        Z = linkage(X, '{method}');
        T = cluster(Z, 'maxclust', 2);
        assert(T(1) == T(2) && T(2) == T(3));
        assert(T(1) ~= T(4));
        """);

    [Fact]
    public Task Cluster_CutsByHeightAsWellAsByCount() => RunAsserting(Groups + """
        Z = linkage(X);
        byCount = cluster(Z, 'maxclust', 2);
        byHeight = cluster(Z, 'cutoff', 5, 'criterion', 'distance');
        assert(isequal(byCount, byHeight));
        assert(max(cluster(Z, 'cutoff', 1000, 'criterion', 'distance')) == 1);
        """);

    [Fact]
    public Task Clusterdata_ReadsAWholeSecondArgumentAsAClusterCount() => RunAsserting(Groups + """
        a = clusterdata(X, 2);
        b = clusterdata(X, 'maxclust', 2);
        assert(isequal(a, b));
        assert(max(a) == 2);
        """);

    [Fact]
    public Task Cophenet_AnswersTheCorrelationAndTheHeights() => RunAsserting(Groups + """
        D = pdist(X);
        Z = linkage(D);
        [c, h] = cophenet(Z, D);
        assert(c > 0.9 && c <= 1);
        assert(numel(h) == numel(D));
        """);

    [Fact]
    public Task Inconsistent_AnswersFourColumnsAndTakesItsDepth() => RunAsserting(Groups + """
        Z = linkage(X);
        Y = inconsistent(Z);
        Y3 = inconsistent(Z, 3);
        assert(size(Y, 1) == 5 && size(Y, 2) == 4);
        assert(abs(Y(1, 4)) < 1e-15);
        assert(size(Y3, 2) == 4);
        """);

    [Fact]
    public Task Optimalleaforder_AnswersEveryLeafOnce() => RunAsserting(Groups + """
        D = pdist(X);
        Z = linkage(D);
        order = optimalleaforder(Z, D);
        assert(numel(order) == 6);
        assert(numel(unique(order)) == 6);
        """);

    // --- kmeans, kmedoids, dbscan, spectralcluster, silhouette --------------------------------------------

    [Fact]
    public Task Kmeans_AnswersFourOutputsInTheDocumentedOrder() => RunAsserting(Groups + """
        rng(7);
        [idx, C, sumd, D] = kmeans(X, 2);
        assert(numel(idx) == 6);
        assert(size(C, 1) == 2 && size(C, 2) == 2);
        assert(numel(sumd) == 2);
        assert(size(D, 1) == 6 && size(D, 2) == 2);
        assert(idx(1) == idx(2) && idx(2) == idx(3));
        assert(idx(1) ~= idx(4));
        """);

    [Fact]
    public Task Kmeans_RepeatsItselfUnderTheSameSeed() => RunAsserting(Groups + """
        rng(21); a = kmeans(X, 2);
        rng(21); b = kmeans(X, 2);
        assert(isequal(a, b));
        """);

    [Fact]
    public Task Kmeans_TakesEveryStartRuleAndTheReplicateCount() => RunAsserting(Groups + """
        rng(1); a = kmeans(X, 2, 'Start', 'plus');
        rng(1); b = kmeans(X, 2, 'Start', 'sample', 'Replicates', 3);
        rng(1); c = kmeans(X, 2, 'Start', 'uniform', 'MaxIter', 20);
        d = kmeans(X, 2, 'Start', [1 1; 20 20]);
        assert(max(a) == 2 && max(b) == 2 && max(c) == 2);
        assert(isequal(d, [1;1;1;2;2;2]));
        """);

    [Fact]
    public Task Kmedoids_CentresOnObservationsAndReportsWhichOnes() => RunAsserting(Groups + """
        rng(4);
        [idx, C, sumd, D, midx, info] = kmedoids(X, 2, 'Distance', 'cityblock');
        assert(numel(midx) == 2);
        assert(all(midx >= 1 & midx <= 6));
        assert(isequal(C(1, :), X(midx(1), :)));
        assert(strcmp(info.distance, 'cityblock'));
        """);

    [Fact]
    public Task Dbscan_LeavesAnIsolatedPointOutOfEveryCluster() => RunAsserting("""
        X = [1 1; 1 2; 2 1; 20 20; 20 21; 21 20; 200 200];
        [idx, core] = dbscan(X, 2, 2);
        assert(idx(1) == idx(3));
        assert(idx(1) ~= idx(4));
        assert(idx(7) == -1);
        assert(core(7) == false);
        """);

    [Fact]
    public Task Dbscan_TakesADistanceMatrixItWasGiven() => RunAsserting(Groups + """
        D = squareform(pdist(X));
        a = dbscan(D, 2, 2, 'Distance', 'precomputed');
        b = dbscan(X, 2, 2);
        assert(isequal(a, b));
        """);

    [Fact]
    public Task Spectralcluster_SeparatesTheTwoGroups() => RunAsserting(Groups + """
        rng(6);
        [idx, V, D] = spectralcluster(X, 2);
        assert(idx(1) == idx(3));
        assert(idx(1) ~= idx(4));
        assert(size(V, 1) == 6 && size(V, 2) == 2);
        assert(numel(D) == 2);
        """);

    [Fact]
    public Task Silhouette_ScoresWellSeparatedGroupsNearOne() => RunAsserting(Groups + """
        s = silhouette(X, [1;1;1;2;2;2]);
        assert(numel(s) == 6);
        assert(min(s) > 0.9);
        c = silhouette(X, [1;1;1;2;2;2], 'cityblock');
        assert(min(c) > 0.9);
        """);

    // --- pca and friends -----------------------------------------------------------------------------------

    [Fact]
    public Task Pca_AnswersSixOutputsInTheDocumentedOrder() => RunAsserting(Cloud + """
        [coeff, score, latent, tsq, explained, mu] = pca(C);
        assert(size(coeff, 1) == 3);
        assert(size(score, 1) == 8);
        assert(numel(latent) == 3);
        assert(numel(tsq) == 8);
        assert(abs(sum(explained) - 100) < 1e-8);
        assert(abs(mu(1) - 4.5) < 1e-12);
        """);

    [Fact]
    public Task Pca_ReconstructsTheDataItCameFrom() => RunAsserting(Cloud + """
        [coeff, score, ~, ~, ~, mu] = pca(C);
        R = score * coeff' + repmat(mu, 8, 1);
        assert(max(max(abs(R - C))) < 1e-10);
        """);

    [Fact]
    public Task Pca_KeepsOnlyTheComponentsAskedFor() => RunAsserting(Cloud + """
        [coeff, score] = pca(C, 'NumComponents', 2);
        assert(size(coeff, 2) == 2);
        assert(size(score, 2) == 2);
        """);

    [Fact]
    public Task Pca_TakesWeightsAndTheCentringFlag() => RunAsserting(Cloud + """
        a = pca(C, 'Centered', false);
        b = pca(C, 'Weights', ones(8, 1));
        c = pca(C, 'VariableWeights', [1 1 1]);
        assert(size(a, 1) == 3 && size(b, 1) == 3 && size(c, 1) == 3);
        """);

    [Fact]
    public Task Pca_DropsAnIncompleteRowByDefault() => RunAsserting(Cloud + """
        C(3, 2) = NaN;
        [~, score] = pca(C);
        assert(size(score, 1) == 7);
        """);

    [Fact]
    public Task Pcacov_AgreesWithTheAnalysisOfTheDataItsCovarianceCameFrom() => RunAsserting(Cloud + """
        [~, ~, latent] = pca(C);
        [coeff, l2, explained] = pcacov(cov(C));
        assert(abs(l2(1) - latent(1)) < 1e-8);
        assert(abs(sum(explained) - 100) < 1e-8);
        assert(size(coeff, 1) == 3);
        """);

    [Fact]
    public Task Pcares_VanishesWhenEveryComponentIsKept() => RunAsserting(Cloud + """
        [res, recon] = pcares(C, 3);
        assert(max(max(abs(res))) < 1e-9);
        assert(max(max(abs(recon - C))) < 1e-9);
        assert(norm(pcares(C, 1)) > 1e-3);
        """);

    [Fact]
    public Task Ppca_FitsThroughAGapAndReportsItsDiagnostics() => RunAsserting("""
        C = [1 2 3.1; 2 4 5.5; 3 6 9.5; 4 8 11; 5 10 14; 6 12 17.5; 7 14 19; 8 16 23];
        C(3, 2) = NaN;
        rng(4);
        [coeff, score, pcvar, mu, v, S] = ppca(C, 2);
        assert(size(coeff, 2) == 2);
        assert(size(score, 1) == 8);
        assert(numel(pcvar) == 2);
        assert(abs(mu(2) - 9) < 1e-4);
        assert(v > 0);
        assert(S.NumIter >= 1);
        """);

    [Fact]
    public Task Nnmf_FactorsIntoPartsThatOnlyAdd() => RunAsserting("""
        A = [1 2 3; 2 4 6; 3 6 9; 1 1 1];
        rng(9);
        [W, H, d] = nnmf(A, 2, 'Replicates', 10, 'MaxIter', 2000, 'TolFun', 1e-12);
        assert(size(W, 1) == 4 && size(W, 2) == 2);
        assert(size(H, 1) == 2 && size(H, 2) == 3);
        assert(min(min(W)) >= 0 && min(min(H)) >= 0);
        assert(d < 1e-4);
        """);

    [Fact]
    public Task Nnmf_TakesTheMultiplicativeAlgorithmToo() => RunAsserting("""
        A = [1 2 3; 2 4 6; 3 6 9; 1 1 1];
        rng(9);
        d = 0;
        [W, H, d] = nnmf(A, 2, 'Algorithm', 'mult', 'MaxIter', 3000, 'TolFun', 1e-12);
        assert(d < 1e-3);
        """);

    [Fact]
    public Task Rotatefactors_KeepsEachVariablesTotalLoading() => RunAsserting("""
        L = [0.8 0.4; 0.7 0.5; 0.4 0.8; 0.5 0.7];
        [B, T] = rotatefactors(L, 'Method', 'varimax');
        before = sum(L .* L, 2);
        after = sum(B .* B, 2);
        assert(max(abs(before - after)) < 1e-8);
        assert(max(max(abs(T' * T - eye(2)))) < 1e-8);
        """);

    [Theory]
    [InlineData("varimax")]
    [InlineData("quartimax")]
    [InlineData("equamax")]
    [InlineData("parsimax")]
    [InlineData("promax")]
    public Task Rotatefactors_TakesEveryDocumentedMethod(string method) => RunAsserting($"""
        L = [0.8 0.4; 0.7 0.5; 0.4 0.8; 0.5 0.7];
        B = rotatefactors(L, 'Method', '{method}');
        assert(size(B, 1) == 4 && size(B, 2) == 2);
        """);

    // --- cmdscale, procrustes, canoncorr, robustcov --------------------------------------------------------

    [Fact]
    public Task Cmdscale_ReproducesTheDistancesItWasGiven() => RunAsserting("""
        P = [0 0; 3 0; 0 4; 3 4; 1 2];
        D = pdist(P);
        [Y, e] = cmdscale(squareform(D));
        assert(size(Y, 1) == 5);
        assert(max(abs(pdist(Y) - D)) < 1e-8);
        assert(e(1) >= e(2));
        """);

    [Fact]
    public Task Cmdscale_KeepsOnlyTheDimensionsAskedFor() => RunAsserting("""
        P = [0 0; 3 0; 0 4; 3 4; 1 2];
        Y = cmdscale(squareform(pdist(P)), 1);
        assert(size(Y, 2) == 1);
        """);

    [Fact]
    public Task Procrustes_NeedsNoMovementToMatchAConfigurationWithItself() => RunAsserting(Cloud + """
        [d, Z, tr] = procrustes(C, C);
        assert(d < 1e-10);
        assert(abs(tr.b - 1) < 1e-8);
        assert(max(max(abs(Z - C))) < 1e-8);
        assert(size(tr.T, 1) == 3);
        """);

    [Fact]
    public Task Procrustes_TakesTheScalingAndReflectionOptions() => RunAsserting(Cloud + """
        a = procrustes(C, C, 'Scaling', false);
        b = procrustes(C, C, 'Reflection', 'best');
        c = procrustes(C, C, 'Reflection', false);
        assert(a < 1e-8 && b < 1e-8 && c < 1e-8);
        """);

    [Fact]
    public Task Canoncorr_AnswersSixOutputsAndBoundedCorrelations() => RunAsserting("""
        Q = [1 5; 2 3; 3 8; 4 2; 5 9; 6 4; 7 7; 8 1];
        R = [2 1; 4 4; 6 2; 8 7; 10 3; 12 8; 14 5; 16 9];
        [A, B, r, U, V, stats] = canoncorr(Q, R);
        assert(numel(r) == 2);
        assert(all(r >= 0 & r <= 1 + 1e-12));
        assert(r(1) >= r(2));
        assert(size(U, 1) == 8 && size(V, 1) == 8);
        assert(numel(stats.Wilks) == 2);
        assert(numel(stats.pChisq) == 2);
        """);

    [Fact]
    public Task Robustcov_FlagsThePointThatWouldOtherwiseDominate() => RunAsserting("""
        X = [0 0; 1 0; 0 1; 1 1; 2 0; 0 2; 2 1; 1 2; 2 2; 0.5 0.5; 1.5 1.5; 0.5 1.5; 500 500];
        rng(4);
        [sig, mu, mah, outliers, s] = robustcov(X, 'NumTrials', 50);
        assert(size(sig, 1) == 2 && size(sig, 2) == 2);
        assert(mu(1) < 10);
        assert(outliers(13) == true);
        assert(mah(13) > s.cutoff);
        """);

    [Theory]
    [InlineData("fmcd")]
    [InlineData("ogk")]
    [InlineData("olivehawkins")]
    public Task Robustcov_TakesEveryDocumentedMethod(string method) => RunAsserting(Cloud + $"""
        rng(2);
        sig = robustcov(C, 'Method', '{method}', 'NumTrials', 25);
        assert(size(sig, 1) == 3);
        assert(max(max(abs(sig - sig'))) < 1e-10);
        """);

    // --- grp2idx, confusionmat, onehotencode, onehotdecode -------------------------------------------------

    [Fact]
    public Task Grp2idx_NumbersTheLevelsAndNamesThem() => RunAsserting("""
        [g, gn] = grp2idx({'b', 'a', 'b', 'c'});
        assert(isequal(g, [2;1;2;3]));
        assert(strcmp(gn{1}, 'a'));
        [h, hn] = grp2idx([10 20 10 30]);
        assert(isequal(h, [1;2;1;3]));
        assert(strcmp(hn{3}, '30'));
        """);

    [Fact]
    public Task Confusionmat_CountsEveryKnownClassAgainstEveryPredictedOne() => RunAsserting("""
        [Cm, order] = confusionmat({'a','b','a','c'}, {'a','b','b','c'});
        assert(size(Cm, 1) == 3 && size(Cm, 2) == 3);
        assert(Cm(1, 1) == 1);
        assert(Cm(1, 2) == 1);
        assert(sum(sum(Cm)) == 4);
        assert(strcmp(order{2}, 'b'));
        """);

    [Fact]
    public Task Confusionmat_TakesTheClassOrderItWasGiven() => RunAsserting("""
        Cm = confusionmat({'a','b'}, {'a','b'}, 'Order', {'b','a'});
        assert(Cm(1, 1) == 1 && Cm(2, 2) == 1);
        """);

    [Fact]
    public Task Onehot_RoundTripsThroughEncodeAndDecode() => RunAsserting("""
        E = onehotencode({'a','b','a'});
        assert(size(E, 1) == 3 && size(E, 2) == 2);
        assert(E(1, 1) == 1 && E(1, 2) == 0);
        back = onehotdecode(E, {'a','b'});
        assert(strcmp(back{1}, 'a') && strcmp(back{2}, 'b'));
        T = onehotencode({'a','b','a'}, 1);
        assert(size(T, 1) == 2 && size(T, 2) == 3);
        """);

    // --- the hidden Markov family --------------------------------------------------------------------------

    private const string Model = """
        TR = [0.9 0.1; 0.2 0.8];
        EM = [0.85 0.15; 0.2 0.8];
        """;

    [Fact]
    public Task Hmmgenerate_DrawsSymbolsAndStatesTheModelAllows() => RunAsserting(Model + """
        rng(11);
        [seq, states] = hmmgenerate(300, TR, EM);
        assert(numel(seq) == 300);
        assert(all(seq >= 1 & seq <= 2));
        assert(all(states >= 1 & states <= 2));
        """);

    [Fact]
    public Task Hmmgenerate_TakesNamesForItsSymbolsAndStates() => RunAsserting(Model + """
        rng(12);
        [seq, states] = hmmgenerate(20, TR, EM, 'Symbols', {'x','y'}, 'Statenames', {'lo','hi'});
        assert(iscell(seq) && numel(seq) == 20);
        assert(strcmp(seq{1}, 'x') || strcmp(seq{1}, 'y'));
        assert(strcmp(states{1}, 'lo') || strcmp(states{1}, 'hi'));
        """);

    [Fact]
    public Task Hmmdecode_AnswersProbabilitiesThatSumToOne() => RunAsserting(Model + """
        rng(13);
        seq = hmmgenerate(50, TR, EM);
        [pstates, logp] = hmmdecode(seq, TR, EM);
        assert(size(pstates, 1) == 2);
        assert(size(pstates, 2) == 51);
        assert(abs(sum(pstates(:, 10)) - 1) < 1e-10);
        assert(logp < 0);
        """);

    [Fact]
    public Task Hmmviterbi_AnswersAPathTheModelCouldHaveTaken() => RunAsserting(Model + """
        rng(14);
        seq = hmmgenerate(50, TR, EM);
        [states, logp] = hmmviterbi(seq, TR, EM);
        assert(numel(states) == 50);
        assert(all(states >= 1 & states <= 2));
        assert(logp < 0);
        """);

    [Fact]
    public Task Hmmestimate_RecoversTheMatricesFromALongKnownPath() => RunAsserting(Model + """
        rng(15);
        [seq, states] = hmmgenerate(20000, TR, EM);
        [eT, eE] = hmmestimate(seq, states);
        assert(abs(eT(1, 1) - 0.9) < 0.05);
        assert(abs(eE(2, 2) - 0.8) < 0.05);
        assert(abs(sum(eT(1, :)) - 1) < 1e-12);
        """);

    [Fact]
    public Task Hmmtrain_ImprovesOnTheGuessItStartedFrom() => RunAsserting(Model + """
        rng(16);
        seq = hmmgenerate(400, TR, EM);
        g = [0.6 0.4; 0.4 0.6];
        [~, before] = hmmdecode(seq, g, g);
        [tT, tE, after, iters, converged] = hmmtrain(seq, g, g);
        assert(after > before);
        assert(converged == true);
        assert(iters >= 1);
        assert(abs(sum(tT(1, :)) - 1) < 1e-10);
        assert(abs(sum(tE(1, :)) - 1) < 1e-10);
        """);

    // --- refusals ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("pdist([1 2; 3 4], 'euclidian');", "is not a distance")]
    [InlineData("pdist([1 2; 3 4], 'euclidean', 3);", "takes no further argument")]
    [InlineData("linkage([1 2; 3 4], 'wards');", "is not a linkage method")]
    [InlineData("squareform([1 2 3 4 5]);", "do not describe every pair")]
    [InlineData("cluster(linkage([1 1; 2 2; 5 5]));", "cluster expects")]
    [InlineData("kmeans([1 1; 2 2], 5);", "fewer observations than clusters")]
    [InlineData("kmeans([1 1; 2 2; 9 9], 2, 'Distance', 'cityblock');", "kmedoids")]
    [InlineData("kmeans([1 1; 2 2; 9 9], 2, 'Start', 'clusters');", "the start is")]
    [InlineData("dbscan([1 1; 2 2], 0, 2);", "must be positive")]
    [InlineData("spectralcluster([1 1; 2 2; 9 9], 2, 'SimilarityGraph', 'knn');", "KernelScale")]
    [InlineData("pca([1 2; 3 4], 'Algorithm', 'als');", "ppca")]
    [InlineData("pca([1 2; 3 4], 'Rows', 'pairwise');", "need not be a covariance")]
    [InlineData("pca([1 2; 3 4], 'Economy', false);", "NumComponents")]
    [InlineData("nnmf([1 -1; 1 1], 1);", "no negative or missing")]
    [InlineData("rotatefactors([0.8 0.4; 0.4 0.8], 'Method', 'procrustes');", "needs a 'Target'")]
    [InlineData("procrustes([1 2; 3 4], [1 2 3; 4 5 6]);", "more dimensions than the target")]
    [InlineData("procrustes([1 2; 3 4], [1 2; 3 4], 'Reflection', 'maybe');", "true, false, or 'best'")]
    [InlineData("hmmdecode([1 2], [0.5 0.4; 0.2 0.8], [0.5 0.5; 0.5 0.5]);", "sum to one")]
    [InlineData("hmmdecode([1 5], [0.5 0.5; 0.5 0.5], [0.5 0.5; 0.5 0.5]);", "cannot emit")]
    [InlineData("hmmtrain([1 2], [0.5 0.5; 0.5 0.5], [0.5 0.5; 0.5 0.5], 'Algorithm', 'viterbi');", "Baum-Welch")]
    [InlineData("hmmgenerate(5, [0.5 0.5; 0.5 0.5], [0.5 0.5; 0.5 0.5], 'Tolerance', 1e-6);", "hmmtrain")]
    [InlineData("onehotdecode([1 0; 0 1], {'a','b','c'});", "class names")]
    public async Task RefusalsNameWhatWasWrong(string code, string expected) =>
        Assert.Contains(expected, await RunExpectingFailure(code), StringComparison.OrdinalIgnoreCase);

    // --- the JGS dialect ------------------------------------------------------------------------------------

    [Fact]
    public void TheSameNamesAreReachableFromJgsAsWell()
    {
        var context = new ScriptContext(_output, (_, _) => { });
        ScriptRunResult result = JgsRunner.Run(
            """
            let X = [[1, 1], [1, 2], [20, 20], [20, 21]]
            let D = pdist(X)
            print(numel(D))
            let Z = linkage(D, 'single')
            let T = cluster(Z, 'maxclust', 2)
            print(T[0] == T[1])
            print(T[0] == T[2])
            """,
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);

        // Four observations make six pairs; the two near ones share a cluster and the far one does not.
        Assert.Contains("6", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("true", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("false", _output.NormalText, StringComparison.Ordinal);
    }
}
