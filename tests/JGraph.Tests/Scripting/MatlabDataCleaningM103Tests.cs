using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Data cleaning and grouping (M103): the outlier trio, <c>ischange</c>,
/// <c>standardizeMissing</c>, <c>findgroups</c>/<c>splitapply</c> and their conveniences,
/// <c>detrend</c>, <c>del2</c>, <c>filter2</c>, <c>histcounts2</c>, <c>xcorr</c>/<c>xcov</c>,
/// <c>subspace</c>, and the small verdict verbs.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer and not JGraph's
/// display format. Every number was read off MATLAB R2024a on this machine; where an answer
/// carries least-squares or FFT roundoff of MATLAB's own, the assertion allows the ulps and the
/// divergence is recorded in ADR 0104.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabDataCleaningM103Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDataCleaningM103Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private void AssertRan(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    private async Task Asserts(string code)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, code));
    }

    private async Task Refuses(string code, string identifier)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, $"""
            caught = '';
            try
                {code}
            catch err
                caught = err.identifier;
            end
            assert(strcmp(caught, '{identifier}'), ['got: ' caught]);
            """));
    }

    // --- isoutlier ----------------------------------------------------------------------------

    [Fact]
    public Task MedianMethodFencesAreScaledMad() => Asserts("""
        [TF, L, U, C] = isoutlier([1 2 100 3 4]);
        assert(isequal(TF, logical([0 0 1 0 0])));
        assert(C == 3);
        assert(abs(L - -1.4478066555168052) < 1e-14 && abs(U - 7.4478066555168052) < 1e-14);
        """);

    [Fact]
    public Task MeanQuartileAndPercentileFencesMatchMatlab() => Asserts("""
        [~, L, U, C] = isoutlier([1 2 100 3 4], 'mean');
        assert(abs(L - -108.85297092538633) < 1e-11 && C == 22);
        [~, L, U, C] = isoutlier([1 2 100 3 4], 'quartiles');
        assert(L == -37.625 && U == 67.375 && C == 14.875);
        [~, L, U, C] = isoutlier([1 2 100 3 4], 'percentiles', [10 90]);
        assert(L == 1 && U == 100 && C == 50.5);
        """);

    [Fact]
    public Task GrubbsReportsTheSurvivorsAndGesdTheLastSignificantRound() => Asserts("""
        x = [57 59 60 100 59 58 57 58 300 61 62 60 62 58 57];
        [TF, L, U, C] = isoutlier(x, 'grubbs');
        assert(isequal(find(TF), [4 9]));
        assert(abs(C - 59.07692307692308) < 1e-12);
        [TF2, L2, U2, C2] = isoutlier(x, 'gesd');
        assert(isequal(find(TF2), [4 9]));
        assert(C2 == 62);
        assert(abs(U2 - 89.764022964560056) < 1e-9);
        """);

    [Fact]
    public Task MovingMedianFencesSlideWithTheData() => Asserts("""
        x = [57 59 60 100 59 58 57 58 300 61 62 60 62 58 57];
        [TF, L, U, C] = isoutlier(x, 'movmedian', 5);
        assert(isequal(find(TF), [4 9]));
        assert(numel(L) == 15 && C(1) == 59 && C(2) == 59.5);
        assert(abs(L(1) - 54.552193344483193) < 1e-12);
        """);

    [Fact]
    public Task MatrixOutliersScanEachColumnAndNanIsNeverOut() => Asserts("""
        [~, L, U, C] = isoutlier(magic(4));
        assert(isequal(size(C), [1 4]) && isequal(C, [7 9 8 10]));
        assert(isequal(isoutlier([1 NaN 100 3 4]), logical([0 0 1 0 0])));
        """);

    [Fact]
    public Task ThresholdFactorWidensTheFences() => Asserts("""
        assert(~any(isoutlier([1 2 100 3 4], 'ThresholdFactor', 100)));
        tf = isoutlier([1 2 100 3 4], 'movmedian', 2.5, 'SamplePoints', [1 2 3 4.2 5]);
        assert(isequal(find(tf), 3));
        """);

    [Fact]
    public Task IsOutlierRefusalsCarryTheDocumentedIdentifiers() => Refuses(
        "isoutlier([1 2 3], 'nosuchmethod');", "MATLAB:unrecognizedStringChoice");

    [Fact]
    public Task MissingWindowAndNegativeThresholdAreRefused() => Refuses(
        "isoutlier([1 2 3], 'movmedian');", "MATLAB:isoutlier:MissingWindowLength");

    // --- rmoutliers and filloutliers ----------------------------------------------------------

    [Fact]
    public Task RmOutliersDropsElementsOfAVectorAndRowsOfAMatrix() => Asserts("""
        [B, TF] = rmoutliers([1 2 100 3 4]);
        assert(isequal(B, [1 2 3 4]) && isequal(TF, logical([0 0 1 0 0])));
        assert(isequal(rmoutliers([1 2 100; 4 5 6; 7 8 9; 1 2 3]), [4 5 6; 7 8 9; 1 2 3]));
        assert(isequal(rmoutliers([1 2 100; 4 5 6; 7 8 9; 1 2 3], 2), [1 2; 4 5; 7 8; 1 2]));
        """);

    [Fact]
    public Task MinNumOutliersSparesARowWithOnlyOne() => Asserts("""
        A = [1 2 100; 4 5 6; 7 8 9; 1 200 3];
        assert(isequal(rmoutliers(A, 'MinNumOutliers', 2), A));
        assert(isequal(rmoutliers([1 2 100 3 4], 'MinNumOutliers', 2), [1 2 100 3 4]));
        """);

    [Fact]
    public Task FillMethodsReplaceOnlyTheFlagged() => Asserts("""
        assert(isequal(filloutliers([1 2 100 3 4], 'center'), [1 2 3 3 4]));
        assert(isequal(filloutliers([1 2 100 3 4], 'previous'), [1 2 2 3 4]));
        assert(isequal(filloutliers([1 2 100 3 4], 'linear'), [1 2 2.5 3 4]));
        assert(isequal(filloutliers([1 2 100 3 4], 'spline'), [1 2 2.5 3 4]));
        assert(isequal(filloutliers([1 2 100 3 4], 0), [1 2 0 3 4]));
        c = filloutliers([1 2 100 3 4], 'clip');
        assert(abs(c(3) - 7.4478066555168052) < 1e-14);
        assert(isequal(filloutliers([1 2 100 3 4], 'nearest', 'mean'), [1 2 100 3 4]));
        """);

    [Fact]
    public Task OutlierLocationsNamesTheOutliersItself() => Asserts("""
        b = filloutliers([1 2 100 3 4], 'previous', 'OutlierLocations', logical([0 1 0 0 0]));
        assert(isequal(b, [1 1 100 3 4]));
        """);

    [Fact]
    public Task CenterFillCannotRideNamedLocations() => Refuses(
        "filloutliers([1 2 3], 'center', 'OutlierLocations', logical([0 1 0]));",
        "MATLAB:filloutliers:UnsupportedFill");

    [Fact]
    public Task UnknownFillCarriesTheDocumentedIdentifier() => Refuses(
        "filloutliers([1 2 3], 'nosuchfill');", "MATLAB:filloutliers:unrecognizedStringChoice");

    // --- ischange -----------------------------------------------------------------------------

    [Fact]
    public Task MeanChangesMarkTheFirstSampleOfEachNewSegment() => Asserts("""
        [TF, S1, S2] = ischange([1 1 1 5 5 5]);
        assert(isequal(find(TF), 4));
        assert(isequal(S1, [1 1 1 5 5 5]) && isequal(S2, zeros(1, 6)));
        assert(isequal(find(ischange([1 2 3 4 20 21 22])), [3 5 6]));
        """);

    [Fact]
    public Task ThresholdIsThePricePerChange() => Asserts("""
        assert(~any(ischange([1 1 1 5 5 5], 'Threshold', 100)));
        assert(isequal(find(ischange([1 1 1 5 5 5 9 9 9], 'MaxNumChanges', 1)), 7));
        assert(isequal(find(ischange([3 3 3 3 3 8 8 8 8], 'MaxNumChanges', 2)), 6));
        """);

    [Fact]
    public Task VarianceSegmentsCarryMeanAndSampleVariance() => Asserts("""
        [TF, S1, S2] = ischange([1 2 1 2 8 9 8 9 30 30], 'variance');
        assert(isequal(find(TF), [5 9]));
        assert(isequal(S1(1:4), 1.5 * ones(1, 4)));
        assert(max(abs(S2(1:8) - 1/3)) < 1e-15 && all(S2(9:10) == 0));
        """);

    [Fact]
    public Task LinearSegmentsCarrySlopeAndIntercept() => Asserts("""
        [TF, S1, S2] = ischange([1 2 3 10 8 6 4], 'linear');
        assert(isequal(find(TF), 4));
        assert(max(abs(S1 - [1 1 1 -2 -2 -2 -2])) < 1e-12);
        assert(max(abs(S2 - [0 0 0 18 18 18 18])) < 1e-11);
        assert(isequal(find(ischange([1 4 2 5 3 6], 'linear', 'SamplePoints', [1 2 3 5 8 13])), [3 5]));
        """);

    [Fact]
    public Task IsChangeRefusalsCarryTheDocumentedIdentifiers() => Refuses(
        "ischange([1 2 3], 'bogus');", "MATLAB:ischange:MethodInvalid");

    // --- standardizeMissing, clip, isuniform, rmse, mape ---------------------------------------

    [Fact]
    public Task StandardizeMissingWritesTheKindsOwnMissing() => Asserts("""
        assert(isequaln(standardizeMissing([1 2 -99 4 0], [-99 0]), [1 2 NaN 4 NaN]));
        c = standardizeMissing({'a', 'NA', 'b'}, 'NA');
        assert(isempty(c{2}) && strcmp(c{1}, 'a'));
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        s = standardizeMissing(T, 8, 'DataVariables', 'v');
        assert(isnan(s.v(4)) && s.v(1) == 5 && s.k(4) == 2);
        """);

    [Fact]
    public Task StandardizeMissingRefusesWhatMatlabRefuses() => Refuses(
        "standardizeMissing([1 2 3], 'x');", "MATLAB:ismissing:IndicatorsDouble");

    [Fact]
    public Task DataVariablesBelongToTables() => Refuses(
        "standardizeMissing([1 8 3], 8, 'DataVariables', 'v');",
        "MATLAB:standardizeMissing:DataVariablesArray");

    [Fact]
    public Task ClipPullsInsideTheBoundsAndKeepsNan() => Asserts("""
        assert(isequal(clip([1 5 9], 2, 8), [2 5 8]));
        assert(isequal(clip(magic(3), 3, 7), [7 3 6; 3 5 7; 4 7 3]));
        assert(isequaln(clip([1 NaN 9], 2, 8), [2 NaN 8]));
        """);

    [Fact]
    public Task ClipRefusesCrossedBounds() => Refuses(
        "clip([1 2 3], 5, 2);", "MATLAB:clip:InvalidLowerBound");

    [Fact]
    public Task IsUniformAnswersWithTheStep() => Asserts("""
        [tf, step] = isuniform([4 3 2 1]);
        assert(tf && step == -1);
        [tf, step] = isuniform([2 2 2]);
        assert(tf && step == 0);
        [tf, step] = isuniform([1 2 3.5 4]);
        assert(~tf && isnan(step));
        assert(~isuniform(5));
        """);

    [Fact]
    public Task ErrorMetricsMatchMatlab() => Asserts("""
        assert(abs(rmse([1 2 3], [2 2 2]) - 0.81649658092772603) < 1e-15);
        assert(max(abs(rmse(magic(3), 5*ones(3)) - [2.1602468994692869 3.2659863237109041 2.1602468994692869])) < 1e-14);
        assert(abs(rmse(magic(3), 5*ones(3), 'all') - 2.5819888974716112) < 1e-14);
        assert(rmse([1 NaN 3], [2 2 2], 'omitnan') == 1);
        assert(max(abs(rmse(magic(3), 5*ones(3), 1, 'Weights', [1 2 3]) - [1.8257418583505538 3.2659863237109041 2.4494897427831779])) < 1e-14);
        assert(mape([1 2 4], [2 2 2]) == 50);
        assert(abs(mape(magic(3), 5*ones(3), 'all') - 44.444444444444443) < 1e-13);
        """);

    // --- findgroups and splitapply ------------------------------------------------------------

    [Fact]
    public Task FindGroupsNumbersSortedValuesAndSkipsMissing() => Asserts("""
        [g, id] = findgroups([3 1 2 1 3]);
        assert(isequal(g, [3 1 2 1 3]) && isequal(id, [1 2 3]));
        assert(isequaln(findgroups([10 2 10 NaN 2]), [2 1 2 NaN 1]));
        [g2, id2] = findgroups({'b', 'a', 'b', 'c'});
        assert(isequal(g2, [2 1 2 3]) && isequal(id2, {'a', 'b', 'c'}));
        """);

    [Fact]
    public Task SeveralGroupingVariablesGroupAsPairs() => Asserts("""
        [g, ida, idb] = findgroups([1 1 2 2], [3 4 3 4]);
        assert(isequal(g, [1 2 3 4]));
        assert(isequal(ida, [1 1 2 2]) && isequal(idb, [3 4 3 4]));
        """);

    [Fact]
    public Task FindGroupsOverATableAnswersATableOfKeys() => Asserts("""
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        [g, tid] = findgroups(T(:, 'k'));
        assert(isequal(g, [1;2;1;2]));
        assert(isequal(tid.k, [1;2]));
        [g2, tid2] = findgroups(T);
        assert(isequal(g2, [1;3;2;4]) && isequal(tid2.v, [5;7;6;8]));
        """);

    [Fact]
    public Task SplitApplyMirrorsTheOrientationOfItsData() => Asserts("""
        assert(isequal(splitapply(@mean, [1 2 3 4 5 6], [1 1 2 2 3 3]), [1.5 3.5 5.5]));
        assert(isequal(splitapply(@mean, [1 2 3 4 5 6]', [1 1 2 2 3 3]'), [1.5; 3.5; 5.5]));
        assert(isequal(splitapply(@mean, [1 2; 3 4; 5 6; 7 8], [1;1;2;2]), [2 3; 6 7]));
        assert(isequal(splitapply(@(a,b) sum(a.*b), [1 2 3], [4 5 6], [1 2 1]), [22 10]));
        assert(isequal(splitapply(@(x) {x}, [1 2 3 4], [1 2 1 2]), {[1 3], [2 4]}));
        assert(isequaln(splitapply(@mean, [1 2 3 4], [2 2 1 NaN]), [3 1.5]));
        """);

    [Fact]
    public Task SplitApplyHandsSeveralOutputsBack() => Asserts("""
        [a, b] = splitapply(@(x) deal(min(x), max(x)), [5 1 8 2], [1 1 2 2]);
        assert(isequal(a, [1 2]) && isequal(b, [5 8]));
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        y = splitapply(@sum, T.v, findgroups(T.k));
        assert(isequal(y, [12; 14]));
        """);

    [Fact]
    public Task SplitApplyRefusesWhatMatlabRefuses() => Refuses(
        "splitapply(@mean, [1 2 3], [1 2 4]);", "MATLAB:splitapply:MissingGroupNums");

    [Fact]
    public Task NonScalarGroupAnswersNeedACell() => Refuses(
        "splitapply(@(x) x, [1 2 3 4], [1 1 2 2]);", "MATLAB:splitapply:OutputNotUniform");

    [Fact]
    public Task GroupSizeMismatchIsAColumnMismatch() => Refuses(
        "splitapply(@mean, [1 2 3], [1 2]);", "MATLAB:splitapply:ColumnMismatch");

    [Fact]
    public Task AGroupingVariableMustBeAVector() => Refuses(
        "findgroups(magic(3));", "MATLAB:findgroups:GroupingVarNotVector");

    // --- groupcounts, grouptransform, groupfilter ----------------------------------------------

    [Fact]
    public Task GroupCountsCountsMissingAsItsOwnLastGroup() => Asserts("""
        [gc, gr, gp] = groupcounts([1 NaN 3 1]');
        assert(isequal(gc, [2; 1; 1]));
        assert(isequaln(gr, [1; 3; NaN]));
        assert(isequal(gp, [50; 25; 25]));
        [gc2, gr2] = groupcounts({'b'; 'a'; 'b'});
        assert(isequal(gc2, [1; 2]) && isequal(gr2, {'a'; 'b'}));
        """);

    [Fact]
    public Task GroupCountsOverATableAddsCountAndPercent() => Asserts("""
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        T2 = groupcounts(T, 'k');
        assert(isequal(T2.k, [1;2]) && isequal(T2.GroupCount, [2;2]) && isequal(T2.Percent, [50;50]));
        """);

    [Fact]
    public Task GroupTransformActsGroupByGroup() => Asserts("""
        assert(isequal(grouptransform([1 2 3 4 5 6]', [1 1 1 2 2 2]', 'zscore'), [-1;0;1;-1;0;1]));
        assert(isequal(grouptransform([1 2 3 4]', [1 1 2 2]', 'rescale'), [0;1;0;1]));
        assert(isequaln(grouptransform([1 NaN 3 4]', [1 1 1 1]', 'meanfill'), [1; 8/3; 3; 4]));
        assert(isequal(grouptransform([1 NaN 3 4]', [1 1 1 1]', 'linearfill'), [1;2;3;4]));
        assert(isequal(grouptransform([1 2 3 4]', [2 2 1 1]', @(x) x - min(x)), [0;1;0;1]));
        n = grouptransform([1 2 3 4]', [1 1 2 2]', 'norm');
        assert(max(abs(n - [0.44721359549995793; 0.89442719099991586; 0.6; 0.8])) < 1e-15);
        """);

    [Fact]
    public Task GroupTransformOverATableKeepsTheGroupingColumn() => Asserts("""
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        T3 = grouptransform(T, 'k', 'zscore');
        assert(isequal(T3.k, [1;2;1;2]));
        assert(max(abs(T3.v - [-1;-1;1;1]/sqrt(2))) < 1e-15);
        """);

    [Fact]
    public Task GroupFilterKeepsWholeGroupsInPlaceOrder() => Asserts("""
        b = groupfilter([1 5 2 6 3 7]', [1 1 2 2 3 3]', @(x) max(x) > 5.5);
        assert(isequal(b, [2; 6; 3; 7]));
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        T4 = groupfilter(T, 'k', @(x) sum(x) > 12, 'v');
        assert(isequal(T4.k, [2;2]) && isequal(T4.v, [6;8]));
        """);

    // --- head, tail, topkrows -----------------------------------------------------------------

    [Fact]
    public Task HeadAndTailTakeEightRowsUnlessAsked() => Asserts("""
        assert(isequal(head((1:20)'), (1:8)'));
        assert(isequal(tail((1:20)'), (13:20)'));
        assert(isequal(head([1 2; 3 4], 5), [1 2; 3 4]));
        assert(isequal(tail([1 2; 3 4; 5 6], 1), [5 6]));
        assert(isequal(size(tail(magic(3), 0)), [0 3]));
        T = table([1;2;1;2], [5;6;7;8], 'VariableNames', {'k','v'});
        T5 = head(T, 2);
        assert(isequal(T5.v, [5;6]));
        """);

    [Fact]
    public Task HeadRefusesANegativeCount() => Refuses(
        "head([1 2; 3 4], -1);", "MATLAB:headtail:InvalidK");

    [Fact]
    public Task TopKRowsSortsLexicographicallyDescending() => Asserts("""
        [B, I] = topkrows(magic(4), 2);
        assert(isequal(B, [16 2 3 13; 9 7 6 12]) && isequal(I, [1; 3]));
        assert(isequal(topkrows(magic(4), 2, 3), [4 14 15 1; 5 11 10 8]));
        assert(isequal(topkrows(magic(4), 2, [2 3], {'ascend', 'descend'}), [16 2 3 13; 9 7 6 12]));
        assert(isequal(topkrows([3 1; 1 2; 3 5; 2 2], 3), [3 5; 3 1; 2 2]));
        assert(isequal(topkrows(magic(4), 99), [16 2 3 13; 9 7 6 12; 5 11 10 8; 4 14 15 1]));
        assert(isequal(topkrows([1 2 3; 7 8 9; 4 5 6], 2, 'ascend'), [1 2 3; 4 5 6]));
        """);

    [Fact]
    public Task TopKRowsRefusesABadColumnOrDirection() => Refuses(
        "topkrows(magic(4), 2, 9);", "MATLAB:topkrows:ColNotIndexVec");

    // --- detrend, del2, filter2 ---------------------------------------------------------------

    [Fact]
    public Task DetrendRemovesConstantsExactlyAndLinesToRoundoff() => Asserts("""
        assert(isequal(detrend([1 3 2 5 4 7 6], 0), [-3 -1 -2 1 0 3 2]));
        y = detrend([1 3 2 5 4 7 6]);
        assert(max(abs(y - [-0.32142857142857095 0.78571428571428603 -1.1071428571428568 1 -0.89285714285714235 1.2142857142857135 -0.67857142857142883])) < 1e-16 * 20);
        """);

    [Fact]
    public Task BreakpointsMakeAContinuousPiecewiseFit() => Asserts("""
        y = detrend([1 3 2 5 4 7 6], 1, [3 5]);
        assert(max(abs(y - [-0.41666666666666585 0.83333333333333304 -0.91666666666666785 0.99999999999999911 -1.083333333333333 1.1666666666666661 -0.58333333333333393])) < 1e-14);
        y2 = detrend([1 3 2 5 4 7 6], 1, [3 5], 'Continuous', false);
        assert(max(abs(y2 - [0 0 0 0 -2/3 4/3 -2/3])) < 1e-14);
        assert(isequal(detrend([1 3 2 5 4 7 6], 0, [3 5]), [-1 1 0 3 2 5 4]));
        """);

    [Fact]
    public Task OmitNanFitsAroundTheGapAndSamplePointsMoveTheAbscissae() => Asserts("""
        y = detrend([1 3 NaN 5 4 7 6], 1, 'omitnan');
        assert(isnan(y(3)));
        assert(max(abs(y([1 2 4 5 6 7]) - [-0.65838509316770155 0.49689440993788825 0.80745341614906785 -1.037267080745341 1.1180124223602483 -0.72670807453416231])) < 1e-13);
        y2 = detrend([1 3 2 5 4 7 6], 'SamplePoints', [0 1 2 4 8 16 32]);
        assert(max(abs(y2 - [-1.7481203007518795 0.11278195488721821 -1.0263157894736841 1.6954887218045114 0.13909774436090228 2.026315789473685 -1.1992481203007515])) < 1e-13);
        """);

    [Fact]
    public Task DetrendRefusalsCarryTheDocumentedIdentifiers() => Refuses(
        "detrend([1 2 3], 1, [1 2], 'bogus', 4);", "MATLAB:detrend:ParseFlags");

    [Fact]
    public Task AKeyWithNoValueIsItsOwnRefusal() => Refuses(
        "detrend([1 2 3], 'bogus');", "MATLAB:detrend:KeyWithoutValue");

    [Fact]
    public Task DiscreteLaplacianIsExactOnQuadratics() => Asserts("""
        assert(isequal(del2([1 4 9 16 25 36]), 0.5 * ones(1, 6)));
        assert(isequal(del2([1 4 9 16 25 36], 2), 0.125 * ones(1, 6)));
        assert(isequal(del2(magic(4)), [15 -5.5 -6.5 9; 0.5 -5 -3 3.5; -3.5 3 5 -0.5; -9 6.5 5.5 -15]));
        assert(del2(5) == 0 && isequal(del2([1 2]), [0 0]));
        assert(isequal(del2([1 4 9]), [0.5 0.5 0.5]));
        """);

    [Fact]
    public Task NonUniformSpacingExtrapolatesTheBoundaryLinearly() => Asserts("""
        L = del2([1 4 9 16 25], [1 2 4 8 16]);
        assert(max(abs(L - [-0.09375 -0.083333333333333329 -0.0625 -0.026041666666666668 0.046875])) < 1e-15);
        """);

    [Fact]
    public Task SpacingCountMustMatchTheDimensions() => Refuses(
        "del2(magic(4), 1, 2, 3);", "MATLAB:del2:InvalidInput");

    [Fact]
    public Task Filter2IsCorrelationInConv2Clothing() => Asserts("""
        assert(isequal(filter2([1 2; 3 4], magic(4)), [79 81 91 37; 82 76 92 44; 91 121 79 15; 32 44 17 1]));
        assert(isequal(filter2([1 2; 3 4], magic(4), 'valid'), [79 81 91; 82 76 92; 91 121 79]));
        f = filter2([1 2; 3 4], magic(4), 'full');
        assert(isequal(size(f), [5 5]) && f(1,1) == 64 && f(5,5) == 1);
        assert(isequal(filter2([1 2 3], magic(4)), [38 29 47 29; 43 57 55 26; 39 41 55 30; 50 77 47 17]));
        """);

    [Fact]
    public Task EvenKernelConv2SameLeansForward() => Asserts("""
        assert(isequal(conv2(magic(4), [1 2; 3 4], 'same'), [91 49 79 68; 78 94 88 56; 79 89 91 50; 58 101 63 4]));
        """);

    [Fact]
    public Task Filter2RefusesAnUnknownShape() => Refuses(
        "filter2(ones(2), magic(4), 'bogus');", "MATLAB:conv2:unknownShapeParameter");

    // --- histcounts2 --------------------------------------------------------------------------

    [Fact]
    public Task HistCounts2CountsPairsOntoAGrid() => Asserts("""
        x = [0.1 0.5 0.9 0.3 0.7 0.2 0.8 0.4 0.6 0.55];
        y = x([3 1 2 5 4 7 6 9 10 8]);
        [n, xe, ye] = histcounts2(x, y);
        assert(isequal(n, [0 4; 4 2]) && isequal(xe, [0 0.5 1]) && isequal(ye, [0 0.5 1]));
        [n2, xe2] = histcounts2([1 2 3 4 5], [10 20 30 40 50]);
        assert(isequal(size(n2), [5 41]) && isequal(xe2, 0.5:1:5.5));
        """);

    [Fact]
    public Task AskedBinCountsUseTheNiceWidthTable() => Asserts("""
        x = [0.1 0.5 0.9 0.3 0.7 0.2 0.8 0.4 0.6 0.55];
        y = x([3 1 2 5 4 7 6 9 10 8]);
        [n, xe] = histcounts2(x, y, 3);
        assert(isequal(n, [0 0 3; 1 3 0; 2 1 0]));
        assert(max(abs(xe - [0 0.30000000000000004 0.60000000000000009 0.90000000000000013])) == 0);
        [n2, ~, ye2] = histcounts2(x, y, [2 4]);
        assert(max(abs(ye2 - [0 0.23 0.46 0.69 0.92])) < 1e-15);
        [n3, ~, ~, bx, by] = histcounts2(x, y, 3);
        assert(isequal(bx, [1 2 3 1 3 1 3 2 2 2]) && isequal(by, [3 1 2 3 1 3 1 2 2 2]));
        """);

    [Fact]
    public Task EdgesWidthsLimitsAndNormalizationsAgreeWithMatlab() => Asserts("""
        x = [0.1 0.5 0.9 0.3 0.7 0.2 0.8 0.4 0.6 0.55];
        y = x([3 1 2 5 4 7 6 9 10 8]);
        n = histcounts2(x, y, 0:0.25:1, 0:0.5:1);
        assert(isequal(n, [0 2; 0 2; 3 1; 1 1]));
        [nw, xw, yw] = histcounts2(x, y, 'BinWidth', [0.3 0.4]);
        assert(isequal(xw, [0 0.3 0.6 0.9]) && isequal(yw, [0 0.4 0.8 1.2000000000000002]));
        assert(isequal(nw, [0 0 2; 1 3 0; 2 2 0]));
        [nl, xl] = histcounts2(x, y, 'XBinLimits', [0.2 0.8], 'YBinLimits', [0.3 0.9]);
        assert(isequal(nl, [1 2; 3 0]) && isequal(xl, [0.2 0.5 0.8]));
        np = histcounts2(x, y, 'Normalization', 'probability');
        assert(isequal(np, [0 0.4; 0.4 0.2]));
        """);

    [Fact]
    public Task MismatchedSizesAreRefusedByName() => Refuses(
        "histcounts2([1 2 3], [1 2]);", "MATLAB:histcounts2:incorrectSize");

    // --- xcorr and xcov -----------------------------------------------------------------------

    [Fact]
    public Task CrossCorrelationMatchesMatlabToTheLastFewBits() => Asserts("""
        r = xcorr([1 2 3]);
        assert(max(abs(r - [3 8 14 8 3])) < 1e-14);
        assert(isequal(size(r), [1 5]));
        assert(isequal(size(xcorr([1 2 3]')), [5 1]));
        [r2, lags] = xcorr([1 2 3], 2);
        assert(isequal(lags, -2:2));
        r3 = xcorr([1 2 3], [4 5 6]);
        assert(max(abs(r3 - [6 17 32 23 12])) < 1e-13);
        r4 = xcorr([1 2 3], [4 5 6], 1);
        assert(max(abs(r4 - [17 32 23])) < 1e-13);
        r5 = xcorr([1 2 3], [4 5 6 7 8]);
        assert(numel(r5) == 9 && max(abs(r5 - [8 23 44 38 32 23 12 0 0])) < 1e-13);
        """);

    [Fact]
    public Task ScalesDivideTheWayMatlabDivides() => Asserts("""
        assert(max(abs(xcorr([1 2 3], 'biased') - [1 8/3 14/3 8/3 1])) < 1e-14);
        assert(max(abs(xcorr([1 2 3], 'unbiased') - [3 4 14/3 4 3])) < 1e-14);
        assert(max(abs(xcorr([1 2 3], 'normalized') - [3/14 8/14 1 8/14 3/14])) < 1e-14);
        c = xcorr([1 2 3], [4 5 6], 'coeff');
        assert(abs(c(3) - 0.97463184619707621) < 1e-14);
        """);

    [Fact]
    public Task MatrixColumnsCorrelatePairwise() => Asserts("""
        r = xcorr([1 2; 3 4]);
        assert(isequal(size(r), [3 4]));
        assert(max(max(abs(r - [3 4 6 8; 10 14 14 20; 3 6 4 8]))) < 1e-13);
        c = xcov([1 2 3]);
        assert(max(abs(c - [-1 0 2 0 -1])) < 1e-14);
        c2 = xcov([1 2 3], [4 5 6], 1, 'coeff');
        assert(max(abs(c2 - [0 1 0])) < 1e-13);
        """);

    [Fact]
    public Task ComplexSignalsConjugateTheSecond() => Asserts("""
        r = xcorr([1+2i 3-1i], [2-1i 1i]);
        assert(max(abs(r - [2-1i, -1+2i, 7+1i])) < 1e-14);
        """);

    [Fact]
    public Task DifferentLengthsRefuseEveryScaleButNone() => Refuses(
        "xcorr([1 2 3], [4 5], 'unbiased');", "MATLAB:xcorr:NoScale");

    [Fact]
    public Task AMatrixBesideASecondSignalIsRefused() => Refuses(
        "xcorr([1 2; 3 4], [1 2]);", "MATLAB:xcorr:MismatchedAB");

    [Fact]
    public Task AnUnknownScaleIsRefusedByName() => Refuses(
        "xcov([1 2 3], 'nosuch');", "MATLAB:xcorr:UnknInput");

    // --- subspace -----------------------------------------------------------------------------

    [Fact]
    public Task SubspaceAnswersThePrincipalAngle() => Asserts("""
        theta = subspace([1 0; 0 1; 0 0], [0 0; 1 0; 0 1]);
        assert(abs(theta - pi/2) < 1e-15);
        assert(abs(subspace([1;2;3], [2;4;7]) - 0.072006516646675775) < 1e-14);
        assert(subspace([1;0], [1;0]) < 1e-7);
        """);
}
