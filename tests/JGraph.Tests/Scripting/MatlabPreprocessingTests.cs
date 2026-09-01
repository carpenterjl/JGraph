using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M66 wave A: the preprocessing family, and the sample points the moving statistics used to refuse.
/// </summary>
/// <remarks>
/// The cases here lean on the two decisions the wave turned on. One is that the family works on plain
/// numbers and gets its time-awareness from a single strip-and-dress seam, so a datetime column is
/// tested for the same behaviour a double column gets rather than for a special case. The other is
/// that <c>discretize</c> and <c>histcounts</c> share their edge chooser, so agreeing about where a
/// bin starts is asserted rather than assumed.
/// </remarks>
[Collection("JG facade")]
public class MatlabPreprocessingTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPreprocessingTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Error(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return result.Message + _output.ErrorText;
    }

    // --- normalize and rescale ------------------------------------------------------------------

    [Fact]
    public void NormalizeCentresAndScalesByDefault()
    {
        Assert.Equal("1 1\n", RunAndRead("""
            z = normalize([1 2 3 4 5]);
            fprintf('%d %d\n', abs(mean(z)) < 1e-12, abs(std(z) - 1) < 1e-12);
            """));
    }

    [Fact]
    public void NormalizeWorksDownEachColumnOfAMatrix()
    {
        // The default dimension is the first non-singleton one, so a matrix is normalized column by
        // column and the two columns do not see each other's spread.
        Assert.Equal("0 1 0 1\n", RunAndRead("""
            M = normalize([1 10; 2 20; 3 30], 'range');
            fprintf('%g %g %g %g\n', M(1,1), M(3,1), M(1,2), M(3,2));
            """));
    }

    [Fact]
    public void NormalizeTakesItsMethodAndTheMethodsOwnSetting()
    {
        Assert.Equal("-2 2 0\n", RunAndRead("""
            c = normalize([1 2 3 4 5], 'center', 'median');
            fprintf('%g %g %g\n', c(1), c(5), c(3));
            """));
    }

    [Fact]
    public void NormalizeNamesTheMethodsItHas()
    {
        Assert.Contains("no method called 'zsore'", Error("normalize([1 2 3], 'zsore');"));
    }

    [Fact]
    public void ASliceWithNoSpreadIsCentredRatherThanFilledWithInfinities()
    {
        Assert.Equal("0 0\n", RunAndRead("""
            z = normalize([7 7]);
            fprintf('%g %g\n', z(1), z(2));
            """));
    }

    [Fact]
    public void RescaleStretchesTheWholeArrayNotEachColumn()
    {
        // The difference from normalize(A, 'range'): one interval for everything, which is why the
        // second column here does not reach 1 on its own.
        Assert.Equal("0 1 0.2 0.4\n", RunAndRead("""
            R = rescale([1 3; 2 6]);
            fprintf('%g %g %g %g\n', R(1,1), R(2,2), R(2,1), R(1,2));
            """));
    }

    [Fact]
    public void RescaleTakesItsBoundsAndItsInputRange()
    {
        Assert.Equal("-1 1 0.25\n", RunAndRead("""
            a = rescale([2 4 6], -1, 1);
            b = rescale(2, 0, 1, 'InputMin', 0, 'InputMax', 8);
            fprintf('%g %g %g\n', a(1), a(3), b);
            """));
    }

    // --- discretize -----------------------------------------------------------------------------

    [Fact]
    public void DiscretizeAndHistcountsAgreeAboutWhereABinStarts()
    {
        // Both share one edge chooser, and this is the assertion that says so: the same data and the
        // same bin count must produce the same edges from either name.
        Assert.Equal("1\n", RunAndRead("""
            x = [1 2 3 4 5 6 7];
            [~, fromDiscretize] = discretize(x, 4);
            [~, fromHistcounts] = histcounts(x, 4);
            fprintf('%d\n', isequal(fromDiscretize, fromHistcounts));
            """));
    }

    [Fact]
    public void EveryBinTakesItsLeftEdgeAndTheLastTakesBoth()
    {
        Assert.Equal("1 2 2 2\n", RunAndRead("""
            fprintf('%g %g %g %g\n', discretize([1 5 9 10], [0 5 10]));
            """));
    }

    [Fact]
    public void TheIncludedEdgeCanBeFlipped()
    {
        Assert.Equal("1 1 2 2\n", RunAndRead("""
            fprintf('%g %g %g %g\n', discretize([1 5 9 10], [0 5 10], 'IncludedEdge', 'right'));
            """));
    }

    [Fact]
    public void AValueOutsideEveryBinIsNotABin()
    {
        Assert.Equal("1\n", RunAndRead("""
            b = discretize([-1 1], [0 5]);
            fprintf('%d\n', isnan(b(1)));
            """));
    }

    [Fact]
    public void NamedBinsAreRefusedRatherThanFaked()
    {
        Assert.Contains("categorical", Error("discretize([1 2], [0 1 2], {'low', 'high'});"));
    }

    // --- fillmissing and rmmissing --------------------------------------------------------------

    [Fact]
    public void AGapIsInterpolatedByDefault()
    {
        Assert.Equal("1 2 3 4 5\n", RunAndRead("""
            fprintf('%g %g %g %g %g\n', fillmissing([1 NaN NaN 4 5], 'linear'));
            """));
    }

    [Fact]
    public void TheCopyingMethodsCopyFromTheSideTheyName()
    {
        Assert.Equal("1 1 4 2 4 4\n", RunAndRead("""
            p = fillmissing([1 NaN 4], 'previous');
            n = fillmissing([2 NaN 4], 'next');
            fprintf('%g %g %g %g %g %g\n', p, n);
            """));
    }

    [Fact]
    public void TheSecondOutputSaysWhereTheGapsWere()
    {
        Assert.Equal("0 1 0 1\n", RunAndRead("""
            [~, tf] = fillmissing([1 NaN 3], 'nearest');
            fprintf('%d %d %d %d\n', tf(1), tf(2), tf(3), islogical(tf));
            """));
    }

    [Fact]
    public void TextIsFilledFromTextAndNeverInvented()
    {
        Assert.Equal("a a c\n", RunAndRead("""
            s = fillmissing(["a" "" "c"], 'previous');
            fprintf('%s %s %s\n', s(1), s(2), s(3));
            """));
    }

    [Fact]
    public void AMethodThatWouldHaveToInventAStringSaysSo()
    {
        Assert.Contains("invent a string", Error("fillmissing([\"a\" \"\" \"c\"], 'linear');"));
    }

    [Fact]
    public void FittingACurveThroughTheNeighboursIsRefusedByName()
    {
        Assert.Contains("'linear', 'nearest'", Error("fillmissing([1 NaN 3], 'spline');"));
    }

    [Fact]
    public void AVectorLosesItsMissingEntriesAndAMatrixLosesWholeRows()
    {
        Assert.Equal("2 1 3 2 1 5\n", RunAndRead("""
            v = rmmissing([1 NaN 3]);
            R = rmmissing([1 2; NaN 4; 5 6]);
            fprintf('%d %g %g %d %g %g\n', numel(v), v(1), v(2), size(R, 1), R(1,1), R(2,1));
            """));
    }

    [Fact]
    public void ARowSurvivesUntilItIsMissingEnough()
    {
        Assert.Equal("2 3\n", RunAndRead("""
            A = [1 2; NaN 4; NaN NaN];
            fprintf('%d %d\n', size(rmmissing(A, 'MinNumMissing', 2), 1), size(A, 1));
            """));
    }

    // --- islocalmax and islocalmin --------------------------------------------------------------

    [Fact]
    public void TheEndsAreNeverLocalExtrema()
    {
        Assert.Equal("0101010\n", RunAndRead("""
            fprintf('%d%d%d%d%d%d%d\n', islocalmax([1 5 2 8 3 9 1]));
            """));
    }

    [Fact]
    public void ProminenceMeasuresAPeakAgainstItsOwnValleys()
    {
        // The peak at 8 stands 5 above the higher of the two valleys that enclose it, not 8 above
        // zero — which is the whole reason MinProminence can tell a peak from a ripple.
        Assert.Equal("5 0001010\n", RunAndRead("""
            [tf, p] = islocalmax([1 5 2 8 3 9 1], 'MinProminence', 5);
            fprintf('%g %d%d%d%d%d%d%d\n', p(4), tf);
            """));
    }

    [Fact]
    public void OnlyTheStrongestExtremaSurviveAMaximumCount()
    {
        Assert.Equal("1 6\n", RunAndRead("""
            tf = islocalmax([1 5 2 8 3 9 1], 'MaxNumExtrema', 1);
            fprintf('%d %d\n', sum(tf), find(tf));
            """));
    }

    [Fact]
    public void AMinimumIsAMaximumOfTheOtherSign()
    {
        Assert.Equal("0010100\n", RunAndRead("""
            fprintf('%d%d%d%d%d%d%d\n', islocalmin([1 5 2 8 3 9 1]));
            """));
    }

    // --- smoothdata -----------------------------------------------------------------------------

    [Fact]
    public void SmoothdataReportsTheWindowItChose()
    {
        // The second output is the only way to find out what an automatic width actually was.
        // Five, and not the two a tenth of the length would give: the width is chosen from where
        // the readings keep their energy, so a ten-sample ramp gets a window of five (M123).
        Assert.Equal("5 5\n", RunAndRead("""
            [~, automatic] = smoothdata(1:10);
            [~, asked] = smoothdata(1:10, 'movmean', 5);
            fprintf('%d %d\n', automatic, asked);
            """));
    }

    [Fact]
    public void AMedianWindowIgnoresTheSpikeAMeanWindowSpreads()
    {
        Assert.Equal("1 1\n", RunAndRead("""
            d = [1 1 100 1 1];
            byMean = smoothdata(d, 'movmean', 3);
            byMedian = smoothdata(d, 'movmedian', 3);
            fprintf('%d %d\n', byMean(2) > 30, byMedian(2) == 1);
            """));
    }

    [Fact]
    public void ALocalFitFollowsALineExactly()
    {
        // lowess fits a line through the window, so a straight line comes back unchanged — the check
        // that the weighted least squares behind it is actually solving something.
        Assert.Equal("1\n", RunAndRead("""
            straight = 2 * (1:10) + 3;
            fitted = smoothdata(straight, 'lowess', 5);
            fprintf('%d\n', max(abs(fitted - straight)) < 1e-9);
            """));
    }

    [Fact]
    public void SmoothdataNamesTheMethodsItHas()
    {
        Assert.Contains("no method called 'movingmean'", Error("smoothdata([1 2 3], 'movingmean');"));
    }

    // --- groupsummary ---------------------------------------------------------------------------

    [Fact]
    public void EachGroupIsSummarisedOnceAndTheGroupsComeBackToo()
    {
        Assert.Equal("3 7 11 1 2 3\n", RunAndRead("""
            [b, g] = groupsummary([1 2 3 4 5 6], [1 1 2 2 3 3], 'sum');
            fprintf('%g %g %g %g %g %g\n', b, g);
            """));
    }

    [Fact]
    public void WithoutAMethodTheSummaryIsHowManyThereWere()
    {
        Assert.Equal("2 2 2\n", RunAndRead("""
            fprintf('%g %g %g\n', groupsummary([1 2 3 4 5 6], [1 1 2 2 3 3]));
            """));
    }

    [Fact]
    public void GroupsCanBeWordsAndComeBackAsWords()
    {
        Assert.Equal("3 4 a b\n", RunAndRead("""
            [b, g] = groupsummary([1 2 3 4 5 6], {'a','b','a','b','a','b'}, 'mean');
            fprintf('%g %g %s %s\n', b(1), b(2), g{1}, g{2});
            """));
    }

    [Fact]
    public void ATableAnswersWithATableOfGroups()
    {
        Assert.Equal("table 2 3 g,GroupCount,mean_v 15\n", RunAndRead("""
            T = table([1;1;2;2], [10;20;30;40], 'VariableNames', {'g', 'v'});
            G = groupsummary(T, 'g', 'mean');
            fprintf('%s %d %d %s %g\n', class(G), height(G), width(G), ...
                strjoin(colnames(G), ','), column(G, 'mean_v')(1));
            """));
    }

    [Fact]
    public void GroupsummaryNamesTheMethodsItHas()
    {
        Assert.Contains("no method called 'total'", Error("groupsummary([1 2], [1 1], 'total');"));
    }

    // --- SamplePoints on the moving statistics ---------------------------------------------------

    [Fact]
    public void SamplePointsMakeTheWindowADistanceRatherThanACount()
    {
        // With the last reading ten units away, its window holds only itself — which is the whole
        // point of saying where the samples were, and what counting elements cannot express.
        Assert.Equal("1.5 2 3 3.5 5\n", RunAndRead("""
            fprintf('%g %g %g %g %g\n', movmean([1 2 3 4 5], 3, 'SamplePoints', [0 1 2 3 10]));
            """));
    }

    [Fact]
    public void WithoutSamplePointsTheWindowStillCountsElements()
    {
        Assert.Equal("1.5 2 3 4 4.5\n", RunAndRead("""
            fprintf('%g %g %g %g %g\n', movmean([1 2 3 4 5], 3));
            """));
    }

    [Fact]
    public void PaddingHasNowhereToGoWhenTheSamplesHavePlaces()
    {
        Assert.Contains("nowhere to pad", Error(
            "movmean([1 2 3], 3, 'SamplePoints', [0 1 2], 'Endpoints', 0);"));
    }

    [Fact]
    public void SamplePointsMustMatchTheDataTheyPlace()
    {
        Assert.Contains("2 places for 3 values", Error(
            "movmean([1 2 3], 3, 'SamplePoints', [0 1]);"));
    }
}
